using NoneadCSS.Api.Authorization;
using NoneadCSS.Api.Filters;
using NoneadCSS.Application.DTOs.Requests;
using NoneadCSS.Application.Services;
using NoneadCSS.Domain.Enums;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using System.Security.Claims;

namespace NoneadCSS.Api.Controllers;

/// <summary>
/// 邮箱控制器 - 管理系统邮箱账户（基础 CRUD、垃圾箱、连接测试、权限、自动回复、签名、OAuth）
/// </summary>
/// <remarks>
/// 原 MailboxAdvancedController 已合并到本控制器，统一邮箱资源的路由前缀，避免双控制器共享
/// api/mailboxes 前缀导致的路由规范问题。所有 {id} 路由参数均使用 :int 约束确保匹配确定性。
/// </remarks>
[ApiController]
[Route("api/[controller]")]
[Authorize]
[RequireModule("core")]
public class MailboxesController : ControllerBase
{
    private readonly IMailboxService _mailboxService;
    private readonly IEmailService _emailService;
    private readonly ILocalizationService _localizer;
    private readonly ILogger<MailboxesController> _logger;
    private readonly IUserRoleResolver _userRoleResolver;

    public MailboxesController(
        IMailboxService mailboxService,
        IEmailService emailService,
        ILocalizationService localizer,
        ILogger<MailboxesController> logger,
        IUserRoleResolver userRoleResolver)
    {
        _mailboxService = mailboxService;
        _emailService = emailService;
        _localizer = localizer;
        _logger = logger;
        _userRoleResolver = userRoleResolver;
    }

    private async Task<bool> HasMailboxAccess(int mailboxId)
    {
        var userId = int.Parse(User.FindFirst(ClaimTypes.NameIdentifier)?.Value ?? "0");
        if (await _userRoleResolver.IsSuperAdminAsync(userId))
        {
            return true;
        }
        var mailboxes = await _mailboxService.GetMailboxesByUserIdAsync(userId);
        return mailboxes.Any(m => m.Id == mailboxId);
    }

    /// <summary>
    /// 获取所有邮箱列表
    /// </summary>
    /// <remarks>仅超管可调用此接口获取所有邮箱；普通用户调用会返回其可访问的邮箱列表（等同于 GET /api/mailboxes）。</remarks>
    /// <returns>邮箱列表</returns>
    [HttpGet("all")]
    [RequireSuperAdmin]
    public async Task<IActionResult> GetAllMailboxes()
    {
        var mailboxes = await _mailboxService.GetAllMailboxesAsync();
        return Ok(mailboxes);
    }

    /// <summary>
    /// 获取当前用户可访问的邮箱列表
    /// </summary>
    /// <returns>邮箱列表</returns>
    [HttpGet]
    public async Task<IActionResult> GetMailboxes()
    {
        var userId = int.Parse(User.FindFirst(ClaimTypes.NameIdentifier)?.Value ?? "0");
        var mailboxes = await _mailboxService.GetMailboxesByUserIdAsync(userId);
        return Ok(mailboxes);
    }

    /// <summary>
    /// 根据 ID 获取邮箱详情
    /// </summary>
    /// <param name="id">邮箱 ID</param>
    /// <returns>邮箱详情</returns>
    /// <response code="200">返回邮箱详情</response>
    /// <response code="404">邮箱不存在</response>
    [HttpGet("{id:int}")]
    public async Task<IActionResult> GetMailbox(int id)
    {
        if (!await HasMailboxAccess(id))
        {
            return Forbid();
        }

        var mailbox = await _mailboxService.GetMailboxByIdAsync(id);

        if (mailbox == null)
        {
            return NotFound(new { message = _localizer["MailboxNotFound"] });
        }

        return Ok(mailbox);
    }

    /// <summary>
    /// 创建新邮箱
    /// </summary>
    /// <param name="request">创建邮箱请求</param>
    /// <returns>新创建的邮箱</returns>
    /// <response code="201">邮箱创建成功</response>
    /// <response code="400">邮箱已存在</response>
    [HttpPost]
    [RequireSuperAdmin]
    public async Task<IActionResult> CreateMailbox([FromBody] CreateMailboxRequest request)
    {
        var mailbox = await _mailboxService.CreateMailboxAsync(request);

        if (mailbox == null)
        {
            return BadRequest(new { message = _localizer["MailboxAlreadyExists"] });
        }

        return CreatedAtAction(nameof(GetMailbox), new { id = mailbox.Id }, mailbox);
    }

    /// <summary>
    /// 更新邮箱配置
    /// </summary>
    /// <param name="id">邮箱 ID</param>
    /// <param name="request">更新邮箱请求</param>
    /// <returns>更新后的邮箱</returns>
    /// <response code="200">邮箱更新成功</response>
    /// <response code="400">邮箱已存在</response>
    /// <response code="404">邮箱不存在</response>
    [HttpPut("{id:int}")]
    [RequireSuperAdmin]
    public async Task<IActionResult> UpdateMailbox(int id, [FromBody] CreateMailboxRequest request)
    {
        var mailbox = await _mailboxService.UpdateMailboxAsync(id, request);

        if (mailbox == null)
        {
            var existingMailbox = await _mailboxService.GetMailboxByIdAsync(id);
            if (existingMailbox == null)
            {
                return NotFound(new { message = _localizer["MailboxNotFound"] });
            }
            return BadRequest(new { message = _localizer["MailboxAlreadyExists"] });
        }

        return Ok(mailbox);
    }

    /// <summary>
    /// 删除邮箱（软删除，移入垃圾箱）
    /// </summary>
    /// <param name="id">邮箱 ID</param>
    /// <returns>无内容</returns>
    /// <response code="204">删除成功</response>
    /// <response code="404">邮箱不存在</response>
    [HttpDelete("{id:int}")]
    [RequireSuperAdmin]
    public async Task<IActionResult> DeleteMailbox(int id)
    {
        var result = await _mailboxService.DeleteMailboxAsync(id);

        if (!result)
        {
            return NotFound(new { message = _localizer["MailboxNotFound"] });
        }

        return NoContent();
    }

    /// <summary>
    /// 切换邮箱启用状态
    /// </summary>
    /// <param name="id">邮箱 ID</param>
    /// <returns>无内容</returns>
    /// <response code="204">操作成功</response>
    /// <response code="404">邮箱不存在</response>
    [HttpPost("{id:int}/toggle-active")]
    [RequireSuperAdmin]
    public async Task<IActionResult> ToggleActive(int id)
    {
        var result = await _mailboxService.ToggleActiveAsync(id);

        if (!result)
        {
            return NotFound(new { message = _localizer["MailboxNotFound"] });
        }

        return NoContent();
    }

    /// <summary>
    /// 获取垃圾箱中的邮箱列表
    /// </summary>
    /// <returns>已删除的邮箱列表</returns>
    [HttpGet("trash")]
    [RequireSuperAdmin]
    public async Task<IActionResult> GetTrash()
    {
        var mailboxes = await _mailboxService.GetDeletedMailboxesAsync();
        return Ok(mailboxes);
    }

    /// <summary>
    /// 从垃圾箱恢复邮箱
    /// </summary>
    /// <param name="id">邮箱 ID</param>
    /// <returns>恢复后的邮箱</returns>
    [HttpPost("{id:int}/restore")]
    [RequireSuperAdmin]
    public async Task<IActionResult> RestoreMailbox(int id)
    {
        var mailbox = await _mailboxService.RestoreMailboxAsync(id);
        if (mailbox == null)
        {
            return NotFound(new { message = _localizer["MailboxNotFound"] });
        }
        return Ok(mailbox);
    }

    /// <summary>
    /// 彻底删除邮箱（从数据库永久删除）
    /// </summary>
    /// <param name="id">邮箱 ID</param>
    /// <returns>无内容</returns>
    [HttpDelete("{id:int}/hard")]
    [RequireSuperAdmin]
    public async Task<IActionResult> HardDeleteMailbox(int id)
    {
        var result = await _mailboxService.HardDeleteMailboxAsync(id);
        if (!result)
        {
            return NotFound(new { message = _localizer["MailboxNotFound"] });
        }
        return NoContent();
    }

    /// <summary>
    /// 清空垃圾箱（永久删除所有已软删除的邮箱）
    /// </summary>
    /// <returns>清空的邮箱数量</returns>
    [HttpDelete("trash")]
    [RequireSuperAdmin]
    public async Task<IActionResult> ClearTrash()
    {
        var count = await _mailboxService.ClearTrashAsync();
        return Ok(new { count });
    }

    /// <summary>
    /// 测试邮箱连接
    /// </summary>
    /// <remarks>测试邮箱的 IMAP/SMTP 连接是否正常。</remarks>
    /// <param name="id">邮箱 ID</param>
    /// <returns>连接测试结果</returns>
    [HttpGet("{id:int}/test-connection")]
    [RequireSuperAdmin]
    public async Task<IActionResult> TestConnection(int id)
    {
        var (success, errorMessage) = await _mailboxService.TestConnectionWithDetailsAsync(id);
        return Ok(new { success, message = errorMessage });
    }

    // ===== 高级管理（原 MailboxAdvancedController，已合并） =====

    /// <summary>
    /// 获取邮箱权限
    /// </summary>
    [HttpGet("{id:int}/permissions")]
    [RequireSuperAdmin]
    public async Task<IActionResult> GetPermissions(int id)
    {
        var permissions = await _mailboxService.GetMailboxPermissionsAsync(id);
        if (permissions == null) return NotFound();
        return Ok(permissions);
    }

    /// <summary>
    /// 更新邮箱权限
    /// </summary>
    [HttpPut("{id:int}/permissions")]
    [RequireSuperAdmin]
    public async Task<IActionResult> UpdatePermissions(int id, [FromBody] MailboxPermissionRequest request)
    {
        var permissions = await _mailboxService.UpdateMailboxPermissionsAsync(id, request);
        if (permissions == null) return NotFound();
        return Ok(permissions);
    }

    /// <summary>
    /// 获取邮箱自动回复
    /// </summary>
    [HttpGet("{id:int}/auto-reply")]
    [RequireSuperAdmin]
    public async Task<IActionResult> GetAutoReply(int id)
    {
        var autoReply = await _mailboxService.GetMailboxAutoReplyAsync(id);
        if (autoReply == null) return NotFound();
        return Ok(autoReply);
    }

    /// <summary>
    /// 更新邮箱自动回复
    /// </summary>
    [HttpPut("{id:int}/auto-reply")]
    [RequireSuperAdmin]
    public async Task<IActionResult> UpdateAutoReply(int id, [FromBody] MailboxAutoReplyRequest request)
    {
        var autoReply = await _mailboxService.UpdateMailboxAutoReplyAsync(id, request);
        if (autoReply == null) return NotFound();
        return Ok(autoReply);
    }

    /// <summary>
    /// 获取邮箱签名
    /// </summary>
    [HttpGet("{id:int}/signature")]
    [RequireSuperAdmin]
    public async Task<IActionResult> GetSignature(int id)
    {
        var signature = await _mailboxService.GetMailboxSignatureAsync(id);
        if (signature == null) return NotFound();
        return Ok(signature);
    }

    /// <summary>
    /// 更新邮箱签名
    /// </summary>
    [HttpPut("{id:int}/signature")]
    [RequireSuperAdmin]
    public async Task<IActionResult> UpdateSignature(int id, [FromBody] MailboxSignatureRequest request)
    {
        var signature = await _mailboxService.UpdateMailboxSignatureAsync(id, request);
        if (signature == null) return NotFound();
        return Ok(signature);
    }

    /// <summary>
    /// 获取IMAP文件夹列表
    /// </summary>
    [HttpGet("{id:int}/imap-folders")]
    [RequireSuperAdmin]
    public async Task<IActionResult> GetImapFolders(int id)
    {
        var folders = await _mailboxService.GetImapFoldersAsync(id);
        return Ok(new { folders });
    }

    /// <summary>
    /// 完成OAuth授权（已弃用）
    /// </summary>
    /// <remarks>
    /// 该接口已禁用：原实现直接写入伪造的 token，存在严重安全风险。
    /// 请通过 OAuth 提供商页面获取 access/refresh token 后调用
    /// <c>POST /api/oauth/mailbox/{mailboxId}/token</c> 保存。
    /// </remarks>
    [HttpPost("{id:int}/oauth/callback")]
    [RequireSuperAdmin]
    [Obsolete("请改用 /api/oauth/mailbox/{mailboxId}/token 写入由 OAuth 提供商返回的真实 token。")]
    public IActionResult OAuthCallback(int id, [FromBody] OAuthCallbackRequest request)
    {
        _logger.LogWarning("已禁用的 OAuthCallback 接口被调用，MailboxId={MailboxId}", id);
        return StatusCode(410, new { message = "该接口已禁用，请通过 OAuth 提供商页面获取 access/refresh token 后调用 /api/oauth/mailbox/{mailboxId}/token 保存。" });
    }
}

