using System.Security.Cryptography;
using System.Text;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.RateLimiting;
using NoneadCSS.Application.Security;
using NoneadCSS.Application.Services;
using NoneadCSS.Domain;
using NoneadCSS.Domain.Enums;

namespace NoneadCSS.Api.Controllers;

/// <summary>
/// 日历订阅控制器 - 提供基于 Token 的 ICS 订阅源，支持 Outlook/Google Calendar 等客户端订阅。
/// 不需要 JWT 认证，通过 HMAC Token 验证身份。
/// 令牌包含过期时间与用户版本（UpdatedAt.Ticks），支持过期失效与吊销。
/// H-05：Token 为 HMAC 签名（userId.version.expiresAt.hash），不可枚举；
///   版本校验（user.UpdatedAt.Ticks）实现吊销；CalendarFeedPolicy 限流 10 次/分钟/IP。
/// </summary>
[ApiController]
[Route("api/calendar/feed")]
[AllowAnonymous]
public class CalendarFeedController : ControllerBase
{
    private readonly ICalendarExportService _exportService;
    private readonly IUnitOfWork _unitOfWork;

    /// <summary>令牌默认有效期：365 天</summary>
    private const int DefaultTokenValidityDays = 365;

    public CalendarFeedController(ICalendarExportService exportService, IUnitOfWork unitOfWork)
    {
        _exportService = exportService;
        _unitOfWork = unitOfWork;
    }

    /// <summary>
    /// 生成用户日历订阅 Token（含过期时间与用户版本，支持吊销）
    /// </summary>
    /// <param name="userId">用户 ID</param>
    /// <param name="userUpdatedAt">用户记录的 UpdatedAt（用于版本校验，变更后旧令牌失效）</param>
    /// <param name="validityDays">有效天数</param>
    public static string GenerateToken(int userId, DateTime userUpdatedAt, int validityDays = DefaultTokenValidityDays)
    {
        var expiresAt = DateTime.UtcNow.AddDays(validityDays).ToString("yyyyMMddHHmmss");
        var version = userUpdatedAt.Ticks.ToString();
        var payload = $"cal-feed:{userId}:{version}:{expiresAt}";
        var hash = SecretHasher.Hash(payload);
        return $"{userId}.{version}.{expiresAt}.{hash}";
    }

    /// <summary>
    /// 解析订阅 Token，返回 (userId, version, expiresAt)，无效返回 null
    /// </summary>
    private static (int UserId, long Version, DateTime ExpiresAt)? ParseToken(string token)
    {
        if (string.IsNullOrWhiteSpace(token)) return null;
        var parts = token.Split('.');
        if (parts.Length != 4) return null;

        if (!int.TryParse(parts[0], out var userId)) return null;
        if (!long.TryParse(parts[1], out var version)) return null;
        if (!DateTime.TryParseExact(parts[2], "yyyyMMddHHmmss",
                System.Globalization.CultureInfo.InvariantCulture,
                System.Globalization.DateTimeStyles.AssumeUniversal | System.Globalization.DateTimeStyles.AdjustToUniversal,
                out var expiresAt))
            return null;

        var expected = SecretHasher.Hash($"cal-feed:{userId}:{parts[1]}:{parts[2]}");
        // 恒定时间比较，防时序攻击
        if (!CryptographicOperations.FixedTimeEquals(
                Encoding.UTF8.GetBytes(expected),
                Encoding.UTF8.GetBytes(parts[3])))
            return null;

        return (userId, version, DateTime.SpecifyKind(expiresAt, DateTimeKind.Utc));
    }

    /// <summary>
    /// ICS 订阅源 — Outlook/Google Calendar 可直接订阅此 URL
    /// </summary>
    /// <param name="token">订阅 Token</param>
    /// <param name="months">向前/向后各覆盖的月数（默认 ±3 个月）</param>
    /// <returns>text/calendar ICS 流</returns>
    [HttpGet]
    [EnableRateLimiting("CalendarFeedPolicy")]
    public async Task<IActionResult> GetFeed([FromQuery] string? token, [FromQuery] int months = 3)
    {
        var parsed = ParseToken(token);
        if (parsed == null) return Unauthorized("无效的订阅令牌");

        var (userId, version, expiresAt) = parsed.Value;

        // 校验令牌是否已过期
        if (DateTime.UtcNow > expiresAt)
        {
            return Unauthorized("订阅令牌已过期，请重新获取");
        }

        // 验证用户是否存在且处于活跃状态
        var user = await _unitOfWork.Users.GetByIdAsync(userId);
        if (user == null || user.DeletedAt != null || user.Status != UserStatus.Active)
        {
            return Unauthorized("用户不存在或已禁用");
        }

        // 校验令牌版本：用户 UpdatedAt 变更后旧令牌立即失效（吊销机制）
        if (user.UpdatedAt.Ticks != version)
        {
            return Unauthorized("订阅令牌已失效，请重新获取");
        }

        // 限制范围，避免过大查询
        if (months < 1) months = 1;
        if (months > 12) months = 12;

        var now = DateTime.UtcNow;
        var startDate = new DateTime(now.Year, now.Month, 1).AddMonths(-months);
        var endDate = new DateTime(now.Year, now.Month, 1).AddMonths(months + 1).AddDays(-1);

        // H-38：使用 ExportRangeForFeedAsync 绕过权限交集过滤。
        // 匿名访问 Feed 时 _currentUserAccessor.UserId 为 0，经 GetListAsync 的 ResolveAuthorizedUserIdsAsync
        // 会得到空权限交集导致返回空列表；此方法直接按 userId 查询，仅返回该用户自己的事件。
        // 必须传入 userId 以限定数据范围，防止全量数据泄露
        var ics = await _exportService.ExportRangeForFeedAsync(startDate, endDate, userId);
        // T000098 修复：移除文件名参数，避免 ASP.NET Core 自动添加 Content-Disposition: attachment 头，
        // 该头会导致 Outlook 将 ICS 视为下载文件而非订阅源，同步失败。
        // 同时显式设置 Content-Disposition: inline 以强化内联呈现语义。
        Response.Headers["Content-Disposition"] = "inline";
        return File(Encoding.UTF8.GetBytes(ics), "text/calendar; charset=utf-8");
    }
}
