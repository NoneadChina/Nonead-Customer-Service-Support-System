using NoneadCSS.Api.Authorization;
using NoneadCSS.Api.Filters;
using NoneadCSS.Application.Configuration;
using NoneadCSS.Application.DTOs.Requests;
using NoneadCSS.Application.Services;
using NoneadCSS.Domain.Enums;
using NoneadCSS.Infrastructure.Services;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.Extensions.Options;
using System.ComponentModel.DataAnnotations;
using System.Security.Claims;

namespace NoneadCSS.Api.Controllers;

/// <summary>
/// 附件控制器 - 管理会话附件的上传、下载、预览和管理
/// </summary>
[Route("api/[controller]")]
[RequireModule("core")]
public class AttachmentsController : BaseController
{
    private readonly IAttachmentService _service;
    private readonly ILocalizationService _localizer;
    private readonly AttachmentSettings _attachmentSettings;

    public AttachmentsController(
        IAttachmentService service,
        ILocalizationService localizer,
        IOptions<AttachmentSettings> attachmentSettings)
    {
        _service = service;
        _localizer = localizer;
        _attachmentSettings = attachmentSettings.Value;
    }

    private bool IsAdmin()
    {
        var roleClaim = User.FindFirst(ClaimTypes.Role)?.Value;
        if (int.TryParse(roleClaim, out var role))
        {
            return role == (int)UserRoleType.SuperAdmin;
        }
        return false;
    }

    /// <summary>
    /// 从当前 ClaimsPrincipal 中提取用户 ID 和管理员标识。
    /// </summary>
    private (int userId, bool isAdmin) GetCurrentUser()
    {
        var userId = CurrentUserId ?? 0;
        return (userId, IsAdmin());
    }

    /// <summary>
    /// 上传单个附件
    /// </summary>
    /// <remarks>支持将附件关联到指定的消息线程（threadId 可选）。</remarks>
    /// <param name="file">要上传的文件</param>
    /// <param name="threadId">关联的消息线程 ID（可选）</param>
    /// <returns>附件信息（包含下载地址）</returns>
    /// <response code="200">上传成功</response>
    /// <response code="400">文件验证失败（大小/类型）</response>
    [HttpPost("upload")]
    [Permission("conversations.reply")]
    public async Task<IActionResult> Upload(IFormFile file, [FromQuery] int? threadId)
    {
        try
        {
            var (userId, _) = GetCurrentUser();
            var attachment = await _service.UploadAsync(file, threadId, userId);
            return Ok(attachment);
        }
        catch (InvalidOperationException ex)
        {
            return BadRequest(new { message = ex.Message });
        }
    }

    /// <summary>
    /// 批量上传附件
    /// </summary>
    /// <param name="files">要上传的文件列表</param>
    /// <param name="threadId">关联的消息线程 ID（可选）</param>
    /// <returns>所有成功上传的附件信息</returns>
    [HttpPost("upload/batch")]
    [Permission("conversations.reply")]
    public async Task<IActionResult> UploadMultiple(List<IFormFile> files, [FromQuery] int? threadId)
    {
        // S7: 数量上限，防超大请求体导致资源耗尽（单文件大小/类型在服务层校验）
        if (files == null || files.Count == 0)
            return BadRequest(new { message = "未选择要上传的文件" });
        if (files.Count > 20)
            return BadRequest(new { message = "单次最多上传 20 个文件" });

        var (userId, _) = GetCurrentUser();
        var attachments = await _service.UploadMultipleAsync(files, threadId, userId);
        return Ok(new { data = attachments, count = attachments.Count });
    }

    /// <summary>
    /// 从 Base64 上传附件
    /// </summary>
    /// <param name="request">包含 base64Content、fileName、mimeType 的请求体</param>
    /// <param name="threadId">关联的消息线程 ID（可选）</param>
    /// <returns>上传的附件信息</returns>
    [HttpPost("upload/base64")]
    [Permission("conversations.reply")]
    public async Task<IActionResult> UploadFromBase64([FromBody] Base64UploadRequest request, [FromQuery] int? threadId)
    {
        try
        {
            var (userId, _) = GetCurrentUser();
            var attachment = await _service.UploadFromBase64Async(request.Base64Content, request.FileName, request.MimeType, threadId, userId);
            return Ok(attachment);
        }
        catch (InvalidOperationException ex)
        {
            return BadRequest(new { message = ex.Message });
        }
    }

    /// <summary>
    /// 下载附件
    /// </summary>
    /// <param name="id">附件 ID</param>
    /// <returns>附件文件流</returns>
    /// <response code="200">返回文件流</response>
    /// <response code="404">附件不存在或文件已丢失</response>
    [HttpGet("{id}/download")]
    [SkipApiResponseWrapper]
    public async Task<IActionResult> Download(int id)
    {
        try
        {
            var (userId, isAdmin) = GetCurrentUser();
            var (content, mimeType, fileName) = await _service.DownloadAsync(id, userId, isAdmin);
            return File(content, mimeType, fileName);
        }
        catch (AttachmentException ex)
        {
            // 按错误码分支：文案本地化后 `ex.Message.Contains(...)` 会失效，故不再用文案判定
            return ex.Code switch
            {
                AttachmentErrorCode.AccessDenied => Forbid(),
                _ => NotFound(new { message = _localizer["AttachmentNotFound"] })
            };
        }
        catch (InvalidOperationException)
        {
            // 兜底：未编码的意外错误维持改造前行为（按"附件不存在"处理）
            return NotFound(new { message = _localizer["AttachmentNotFound"] });
        }
    }

    /// <summary>
    /// 获取图片预览（缩略图）
    /// </summary>
    /// <param name="id">附件 ID</param>
    /// <param name="maxWidth">最大宽度（默认 300）</param>
    /// <param name="maxHeight">最大高度（默认 300）</param>
    /// <returns>缩略图文件流</returns>
    /// <response code="200">返回缩略图</response>
    /// <response code="400">非图片类型不支持预览</response>
    /// <response code="403">无权访问此附件</response>
    /// <response code="404">附件不存在</response>
    [HttpGet("{id}/preview")]
    [SkipApiResponseWrapper]
    public async Task<IActionResult> Preview(int id, [FromQuery] int maxWidth = 300, [FromQuery] int maxHeight = 300)
    {
        try
        {
            var (userId, isAdmin) = GetCurrentUser();
            var (content, mimeType) = await _service.GetPreviewAsync(id, maxWidth, maxHeight, userId, isAdmin);
            return File(content, mimeType);
        }
        catch (AttachmentException ex)
        {
            return ex.Code switch
            {
                AttachmentErrorCode.AccessDenied => Forbid(),
                AttachmentErrorCode.NotImage => BadRequest(new { message = _localizer["AttachmentPreviewImageOnly"] }),
                _ => NotFound(new { message = _localizer["AttachmentNotFound"] })
            };
        }
        catch (InvalidOperationException)
        {
            return NotFound(new { message = _localizer["AttachmentNotFound"] });
        }
    }

    /// <summary>
    /// 公开预览图片（用于头像等公开显示场景）
    /// </summary>
    /// <remarks>
    /// 命名说明（L-S3）：方法名含 "Public" 但实际仍需认证，仅返回当前用户有权访问的附件缩略图，
    /// 并非完全公开（匿名）访问。"公开"此处指"无需会话上下文即可渲染图片"（如 img src 直链），
    /// 但请求仍须携带有效凭据。如未来需真正的匿名公开访问，应单独新增 [AllowAnonymous] 端点并严格限制资源范围。
    /// </remarks>
    /// <param name="id">附件 ID</param>
    /// <param name="maxWidth">最大宽度（默认 300）</param>
    /// <param name="maxHeight">最大高度（默认 300）</param>
    /// <returns>缩略图文件流</returns>
    /// <response code="200">返回缩略图</response>
    /// <response code="403">无权访问此附件</response>
    /// <response code="404">附件不存在</response>
    [HttpGet("{id}/public-preview")]
    [SkipApiResponseWrapper]
    public async Task<IActionResult> PublicPreview(int id, [FromQuery] int maxWidth = 300, [FromQuery] int maxHeight = 300)
    {
        try
        {
            var (userId, isAdmin) = GetCurrentUser();
            var (content, mimeType) = await _service.GetPreviewAsync(id, maxWidth, maxHeight, userId, isAdmin);
            return File(content, mimeType);
        }
        catch (AttachmentException ex)
        {
            return ex.Code switch
            {
                AttachmentErrorCode.AccessDenied => Forbid(),
                _ => NotFound()
            };
        }
        catch (InvalidOperationException)
        {
            return NotFound();
        }
    }

    /// <summary>
    /// 获取附件详情
    /// </summary>
    /// <param name="id">附件 ID</param>
    /// <returns>附件详情</returns>
    /// <response code="200">返回附件详情</response>
    /// <response code="403">无权访问此附件</response>
    /// <response code="404">附件不存在</response>
    [HttpGet("{id}")]
    public async Task<IActionResult> GetAttachment(int id)
    {
        try
        {
            var (userId, isAdmin) = GetCurrentUser();
            var attachment = await _service.GetAttachmentAsync(id, userId, isAdmin);
            if (attachment == null)
            {
                return NotFound(new { message = _localizer["AttachmentNotFound"] });
            }
            return Ok(attachment);
        }
        catch (AttachmentException ex) when (ex.Code == AttachmentErrorCode.AccessDenied)
        {
            return Forbid();
        }
    }

    /// <summary>
    /// 获取消息线程的所有附件
    /// </summary>
    /// <param name="threadId">消息线程 ID</param>
    /// <returns>附件列表</returns>
    /// <response code="403">无权访问此会话的附件</response>
    [HttpGet("thread/{threadId}")]
    public async Task<IActionResult> GetByThreadId(int threadId)
    {
        try
        {
            var (userId, isAdmin) = GetCurrentUser();
            var attachments = await _service.GetAttachmentsByThreadIdAsync(threadId, userId, isAdmin);
            return Ok(attachments);
        }
        catch (AttachmentException ex) when (ex.Code == AttachmentErrorCode.AccessDenied)
        {
            return Forbid();
        }
    }

    /// <summary>
    /// 获取会话的所有附件
    /// </summary>
    /// <param name="conversationId">会话 ID</param>
    /// <returns>附件列表</returns>
    /// <response code="403">无权访问此会话的附件</response>
    [HttpGet("conversation/{conversationId}")]
    public async Task<IActionResult> GetByConversationId(int conversationId)
    {
        try
        {
            var (userId, isAdmin) = GetCurrentUser();
            var attachments = await _service.GetAttachmentsByConversationIdAsync(conversationId, userId, isAdmin);
            return Ok(attachments);
        }
        catch (AttachmentException ex) when (ex.Code == AttachmentErrorCode.AccessDenied)
        {
            return Forbid();
        }
    }

    /// <summary>
    /// 获取用户上传的所有附件
    /// </summary>
    /// <param name="userId">用户 ID</param>
    /// <returns>附件列表</returns>
    /// <response code="403">无权查看其他用户的附件</response>
    [HttpGet("user/{userId}")]
    public async Task<IActionResult> GetByUserId(int userId)
    {
        try
        {
            var (requesterId, isAdmin) = GetCurrentUser();
            var attachments = await _service.GetAttachmentsByUserIdAsync(userId, requesterId, isAdmin);
            return Ok(attachments);
        }
        catch (AttachmentException ex) when (ex.Code == AttachmentErrorCode.AccessDenied)
        {
            return Forbid();
        }
    }

    /// <summary>
    /// 删除附件
    /// </summary>
    /// <param name="id">附件 ID</param>
    /// <returns>无内容</returns>
    /// <response code="204">删除成功</response>
    /// <response code="403">无权访问此附件</response>
    /// <response code="404">附件不存在</response>
    [HttpDelete("{id}")]
    [Permission("attachments.manage")]
    public async Task<IActionResult> Delete(int id)
    {
        try
        {
            var (userId, isAdmin) = GetCurrentUser();
            var result = await _service.DeleteAsync(id, userId, isAdmin);
            if (!result)
            {
                return NotFound(new { message = _localizer["AttachmentNotFound"] });
            }
            return NoContent();
        }
        catch (AttachmentException ex) when (ex.Code == AttachmentErrorCode.AccessDenied)
        {
            return Forbid();
        }
    }

    /// <summary>
    /// 删除消息线程的所有附件
    /// </summary>
    /// <param name="threadId">消息线程 ID</param>
    /// <response code="204">无内容</response>
    /// <response code="403">无权访问此会话的附件</response>
    [HttpDelete("thread/{threadId}")]
    [Permission("attachments.manage")]
    public async Task<IActionResult> DeleteByThreadId(int threadId)
    {
        try
        {
            var (userId, isAdmin) = GetCurrentUser();
            await _service.DeleteByThreadIdAsync(threadId, userId, isAdmin);
            return NoContent();
        }
        catch (AttachmentException ex) when (ex.Code == AttachmentErrorCode.AccessDenied)
        {
            return Forbid();
        }
    }

    /// <summary>
    /// 删除会话的所有附件
    /// </summary>
    /// <param name="conversationId">会话 ID</param>
    /// <response code="204">无内容</response>
    /// <response code="403">无权访问此会话的附件</response>
    [HttpDelete("conversation/{conversationId}")]
    [Permission("attachments.manage")]
    public async Task<IActionResult> DeleteByConversationId(int conversationId)
    {
        try
        {
            var (userId, isAdmin) = GetCurrentUser();
            await _service.DeleteByConversationIdAsync(conversationId, userId, isAdmin);
            return NoContent();
        }
        catch (AttachmentException ex) when (ex.Code == AttachmentErrorCode.AccessDenied)
        {
            return Forbid();
        }
    }

    /// <summary>
    /// 获取附件配置信息（允许的文件类型、大小限制等）
    /// </summary>
    /// <returns>附件配置信息</returns>
    [HttpGet("config")]
    public IActionResult GetConfig()
    {
        // 从配置读取扩展名列表，确保与 appsettings.json 中 Attachment:AllowedExtensions 一致
        var allowedExtensions = _attachmentSettings.AllowedExtensions ?? new List<string>();
        // 统一补充前导点号，便于前端直接匹配
        var normalizedExtensions = allowedExtensions
            .Select(ext => ext.StartsWith(".") ? ext : "." + ext)
            .ToList();

        var maxSize = _attachmentSettings.MaxSize;
        return Ok(new
        {
            maxSize = maxSize,
            maxSizeFormatted = FormatSize(maxSize),
            allowedExtensions = normalizedExtensions
        });
    }

    private static string FormatSize(long bytes)
    {
        if (bytes >= 1024 * 1024 * 1024)
        {
            return $"{bytes / (1024.0 * 1024 * 1024):0.#} GB";
        }
        if (bytes >= 1024 * 1024)
        {
            return $"{bytes / (1024.0 * 1024):0.#} MB";
        }
        if (bytes >= 1024)
        {
            return $"{bytes / 1024.0:0.#} KB";
        }
        return $"{bytes} B";
    }

    /// <summary>
    /// 检查文件是否允许上传
    /// </summary>
    /// <param name="request">文件验证请求</param>
    /// <returns>检查结果</returns>
    [HttpPost("validate")]
    [Permission("attachments.manage")]
    public IActionResult Validate([FromBody] AttachmentValidateRequest request)
    {
        var errors = new List<string>();

        if (!_service.IsWithinSizeLimit(request.FileSize))
        {
            errors.Add("文件大小超过限制");
        }

        if (!_service.IsAllowedExtension(request.FileName))
        {
            errors.Add("文件类型不允许");
        }

        if (!_service.IsAllowedMimeType(request.MimeType))
        {
            errors.Add("文件 MIME 类型不允许");
        }

        return Ok(new
        {
            isValid = errors.Count == 0,
            errors = errors
        });
    }
}
