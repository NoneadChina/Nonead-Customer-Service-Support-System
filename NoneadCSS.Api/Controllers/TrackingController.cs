using NoneadCSS.Domain;
using NoneadCSS.Domain.Events;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using NoneadCSS.Api.Filters;
using Microsoft.AspNetCore.RateLimiting;
using NoneadCSS.Application.Security;
using System.Security.Cryptography;
using System.Text;

namespace NoneadCSS.Api.Controllers;

/// <summary>
/// 邮件追踪控制器 - 无需认证（追踪像素由邮件客户端加载）
/// </summary>
[ApiController]
[Route("api/[controller]")]
[AllowAnonymous]
public class TrackingController : ControllerBase
{
    /// <summary>1x1 透明 GIF 追踪像素（43 bytes）</summary>
    private static readonly byte[] Pixel =
        Convert.FromBase64String("R0lGODlhAQABAIAAAAAAAP///yH5BAEAAAAALAAAAAABAAEAAAIBRAA7");

    private readonly IUnitOfWork _unitOfWork;
    private readonly INotificationPublisher _notificationPublisher;
    private readonly IWebHostEnvironment _env;

    public TrackingController(
        IUnitOfWork unitOfWork,
        INotificationPublisher notificationPublisher,
        IWebHostEnvironment env)
    {
        _unitOfWork = unitOfWork;
        _notificationPublisher = notificationPublisher;
        _env = env;
    }

    /// <summary>
    /// 邮件打开追踪像素 - 返回 1x1 透明 GIF（带 token 校验，推荐路由）
    /// H-2 修复：通过 HMAC token 校验防止任意用户伪造 id 触发计数/通知。
    /// token 生成方：在邮件发送端生成像素链接时附加 token = SecretHasher.Hash($"email-track:{id}")。
    /// </summary>
    /// <param name="id">发送日志 ID</param>
    /// <param name="token">HMAC 签名 token，由 Hash($"email-track:{id}") 生成</param>
    /// <returns>1x1 透明 GIF 图片</returns>
    [HttpGet("open/{id:int}/{token}")]
    [SkipApiResponseWrapper]
    [Produces("image/gif")]
    [EnableRateLimiting("TrackingPolicy")]
    public async Task<IActionResult> TrackOpen(int id, string token)
    {
        // 校验 token：必须等于 SecretHasher.Hash($"email-track:{id}")
        // 使用 FixedTimeEquals 做时间常量比较，防止计时侧信道
        if (!ValidateTrackingToken(id, token))
        {
            // 静默拒绝，返回 404，避免泄露端点存在性及 token 校验细节
            return NotFound();
        }

        return await RecordOpenAsync(id);
    }

    /// <summary>
    /// 邮件打开追踪像素 - 旧版无 token 路由（已废弃，保留仅为向后兼容）
    /// H-2 修复：新邮件应使用带 token 的路由 open/{id:int}/{token}。
    /// 旧路由保留以便存量已发送邮件的追踪像素链接不立即失效，后续版本将移除。
    /// </summary>
    /// <param name="id">发送日志 ID</param>
    /// <returns>1x1 透明 GIF 图片</returns>
    [HttpGet("open/{id:int}")]
    [SkipApiResponseWrapper]
    [Produces("image/gif")]
    [EnableRateLimiting("TrackingPolicy")]
    [Obsolete("请使用带 token 的新路由 open/{id:int}/{token}，旧路由将在后续版本移除")]
    public async Task<IActionResult> TrackOpenLegacy(int id)
    {
        // S-M1 修复：旧版无 token 路由生产环境禁用，避免任意用户遍历 ID 伪造邮件打开事件。
        // 仅开发环境保留以便本地测试，生产环境返回 404。
        if (!_env.IsDevelopment())
        {
            return NotFound();
        }

        return await RecordOpenAsync(id);
    }

    /// <summary>
    /// 校验邮件追踪 token 是否匹配 Hash($"email-track:{id}")。
    /// </summary>
    private static bool ValidateTrackingToken(int id, string token)
    {
        if (string.IsNullOrEmpty(token))
            return false;

        string expected;
        try
        {
            expected = SecretHasher.Hash($"email-track:{id}");
        }
        catch (InvalidOperationException)
        {
            // SecretHasher 未初始化（启动配置异常），拒绝所有请求避免放行伪造 token
            return false;
        }

        var expectedBytes = Encoding.UTF8.GetBytes(expected);
        var actualBytes = Encoding.UTF8.GetBytes(token);
        return expectedBytes.Length == actualBytes.Length
               && CryptographicOperations.FixedTimeEquals(expectedBytes, actualBytes);
    }

    /// <summary>
    /// 记录邮件打开事件并返回 1x1 透明 GIF 像素。
    /// </summary>
    private async Task<IActionResult> RecordOpenAsync(int id)
    {
        var now = DateTime.UtcNow;

        // T12: 原子递增打开计数（并发像素请求不丢更新），并更新 LastOpenedAt
        var updated = await _unitOfWork.SendLogs.GetQueryable(false)
            .Where(x => x.Id == id)
            .ExecuteUpdateAsync(setters => setters
                .SetProperty(x => x.OpenCount, x => x.OpenCount + 1)
                .SetProperty(x => x.LastOpenedAt, now));

        if (updated == 0)
        {
            // 记录不存在：直接返回像素，不触发事件/通知
            return File(Pixel, "image/gif");
        }

        // 首次打开：仅在 FirstOpenedAt 为 NULL 时原子占用（并发下仅一个请求成功）
        var firstCount = await _unitOfWork.SendLogs.GetQueryable(false)
            .Where(x => x.Id == id && x.FirstOpenedAt == null)
            .ExecuteUpdateAsync(setters => setters.SetProperty(x => x.FirstOpenedAt, now));
        var firstOpened = firstCount > 0;

        var threadId = await _unitOfWork.SendLogs.GetQueryable(false)
            .Where(x => x.Id == id)
            .Select(x => x.ThreadId)
            .FirstOrDefaultAsync();

        // T12: 仅在首次打开时发送实时通知（避免每次重复打开都重复通知）
        if (firstOpened)
        {
            var userId = await _unitOfWork.SendLogs.GetQueryable(false)
                .Where(x => x.Id == id)
                .Select(x => x.UserId)
                .FirstOrDefaultAsync();
            if (userId.HasValue)
            {
                await _notificationPublisher.NotifyUserAsync(
                    userId.Value.ToString(),
                    "email_opened",
                    new { sendLogId = id, threadId = threadId, openedAt = now });
            }
        }

        // 返回 1x1 透明 GIF (43 bytes)
        return File(Pixel, "image/gif");
    }
}
