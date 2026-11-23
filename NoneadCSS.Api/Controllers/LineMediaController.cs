using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using NoneadCSS.Api.Authorization;
using NoneadCSS.Api.Filters;
using NoneadCSS.Application.Common;
using NoneadCSS.Application.Services;
using NoneadCSS.Domain;
using NoneadCSS.Domain.Enums;
using NoneadCSS.Domain.Repositories;
using NoneadCSS.Infrastructure.Services;

namespace NoneadCSS.Api.Controllers;

/// <summary>
/// LINE 出站附件直链控制器 - LINE 没有聊天媒体上传接口，
/// 图片/视频/音频/文件消息必须引用「本部署自有的公网 HTTPS 直链」（docs/integration/line.md §媒体出站）。
/// 因此这里提供带短期签名令牌的匿名下载端点：
///   GET /api/line/media/{attachmentId}?exp=&lt;unixSeconds&gt;&amp;sig=&lt;base64url HMAC&gt;
/// 令牌由 LineService 用 LineMediaLinkSigner + ChannelSecret 生成（TTL = AttachmentPublicUrlTtlMinutes，默认 7 天），
/// 且只绑定属于本渠道会话的附件；令牌不可伪造、过期即 403，日志不记录签名。
/// </summary>
[ApiController]
[Route("api/line/media")]
[AllowAnonymous]
[RequireModule("line")]
public class LineMediaController : ControllerBase
{
    private readonly ILineSettingsProvider _settingsProvider;
    private readonly IAttachmentService _attachmentService;
    private readonly IUnitOfWork _unitOfWork;
    private readonly ILogger<LineMediaController> _logger;

    public LineMediaController(
        ILineSettingsProvider settingsProvider,
        IAttachmentService attachmentService,
        IUnitOfWork unitOfWork,
        ILogger<LineMediaController> logger)
    {
        _settingsProvider = settingsProvider;
        _attachmentService = attachmentService;
        _unitOfWork = unitOfWork;
        _logger = logger;
    }

    /// <summary>按签名直链取回附件内容（LINE 服务器会在其后跟进抓取）</summary>
    [HttpGet("{attachmentId:int}")]
    [SkipApiResponseWrapper]
    public async Task<IActionResult> GetAttachment(int attachmentId, [FromQuery] long exp, [FromQuery] string? sig)
    {
        var settings = _settingsProvider.Current;

        if (string.IsNullOrWhiteSpace(settings.ChannelSecret))
        {
            _logger.LogWarning("LINE 附件直链拒绝：ChannelSecret 未配置，无法校验签名");
            return Unauthorized();
        }

        if (!LineMediaLinkSigner.Validate(attachmentId, exp, sig, settings.ChannelSecret!, DateTimeOffset.UtcNow))
        {
            // 不打印 sig 内容，只说明判定结果
            _logger.LogWarning("LINE 附件直链验签失败或已过期 attachmentId={AttachmentId}", attachmentId);
            return Forbid();
        }

        // 只放行 LINE 渠道会话挂载的附件：即便直链泄露，也不能取到其他渠道/用户的文件
        if (!await IsLineChannelAttachmentAsync(attachmentId))
        {
            _logger.LogWarning("LINE 附件直链越界：attachmentId={AttachmentId} 不属于 LINE 会话", attachmentId);
            return NotFound();
        }

        try
        {
            // 令牌已替代用户级鉴权 ⇒ 以管理员语义取内容（内部仍受存储路径规范化保护）
            var (content, mimeType, fileName) = await _attachmentService.DownloadAsync(attachmentId, userId: null, isAdmin: true);
            Response.Headers.CacheControl = "no-store";

            var isImage = mimeType.StartsWith("image/", StringComparison.OrdinalIgnoreCase);
            if (isImage)
            {
                // 图片必须 inline，LINE 客户端直接渲染 originalContentUrl / previewImageUrl
                return File(content, mimeType, enableRangeProcessing: false);
            }

            // 文件消息要求可预测的文件名（LINE file 消息携带 fileName/fileSize）
            return File(content, mimeType, fileName);
        }
        catch (AttachmentException ex) when (ex.Code == AttachmentErrorCode.NotFound)
        {
            return NotFound();
        }
        catch (AttachmentException ex) when (ex.Code == AttachmentErrorCode.AccessDenied)
        {
            _logger.LogWarning("LINE 附件直链访问被拒绝 attachmentId={AttachmentId}", attachmentId);
            return Forbid();
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "LINE 附件直链读取失败 attachmentId={AttachmentId}", attachmentId);
            return StatusCode(StatusCodes.Status500InternalServerError);
        }
    }

    /// <summary>
    /// 图片预览直链（LINE image 消息的 previewImageUrl）。
    /// 本仓库不引入图像缩放库（无新依赖约束），因此预览直链回同一份字节；
    /// 若图片超过 LINE 预览上限，LineService 会自动省略 previewImageUrl，只发 originalContentUrl。
    /// </summary>
    [HttpGet("{attachmentId:int}/preview")]
    [SkipApiResponseWrapper]
    public async Task<IActionResult> GetPreview(int attachmentId, [FromQuery] long exp, [FromQuery] string? sig)
    {
        var settings = _settingsProvider.Current;

        if (string.IsNullOrWhiteSpace(settings.ChannelSecret))
        {
            return Unauthorized();
        }

        if (!LineMediaLinkSigner.Validate(attachmentId, exp, sig, settings.ChannelSecret!, DateTimeOffset.UtcNow))
        {
            _logger.LogWarning("LINE 预览直链验签失败或已过期 attachmentId={AttachmentId}", attachmentId);
            return Forbid();
        }

        if (!await IsLineChannelAttachmentAsync(attachmentId))
        {
            return NotFound();
        }

        try
        {
            var (content, mimeType, _) = await _attachmentService.DownloadAsync(attachmentId, userId: null, isAdmin: true);
            if (!mimeType.StartsWith("image/", StringComparison.OrdinalIgnoreCase))
            {
                return NotFound();
            }

            Response.Headers.CacheControl = "no-store";
            return File(content, mimeType);
        }
        catch (AttachmentException ex) when (ex.Code is AttachmentErrorCode.NotFound or AttachmentErrorCode.NotImage)
        {
            return NotFound();
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "LINE 预览直链读取失败 attachmentId={AttachmentId}", attachmentId);
            return StatusCode(StatusCodes.Status500InternalServerError);
        }
    }

    private async Task<bool> IsLineChannelAttachmentAsync(int attachmentId)
    {
        var attachment = await _unitOfWork.Attachments.GetByIdAsync(attachmentId);
        if (attachment?.ThreadId is null)
        {
            return false;
        }

        var thread = await _unitOfWork.Threads.GetByIdAsync(attachment.ThreadId.Value);
        return thread != null
            && thread.SourceType == SourceType.Line
            && thread.State != Domain.Entities.ThreadState.Deleted;
    }
}
