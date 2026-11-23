using NoneadCSS.Api.Authorization;
using NoneadCSS.Api.Filters;
using NoneadCSS.Application.DTOs.Requests;
using NoneadCSS.Application.Services;
using NoneadCSS.Domain;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using System.Security.Claims;

namespace NoneadCSS.Api.Controllers;

/// <summary>
/// 文件夹控制器 - 管理会话文件夹的查询、创建、更新、删除
/// </summary>
[ApiController]
[Route("api/[controller]")]
[Authorize]
[RequireModule("core")]
public class FoldersController : ControllerBase
{
    private readonly IFolderService _folderService;
    private readonly ILocalizationService _localizer;
    private readonly IMailboxService _mailboxService;
    private readonly IUserRoleResolver _userRoleResolver;

    public FoldersController(IFolderService folderService, ILocalizationService localizer, IMailboxService mailboxService, IUserRoleResolver userRoleResolver)
    {
        _folderService = folderService;
        _localizer = localizer;
        _mailboxService = mailboxService;
        _userRoleResolver = userRoleResolver;
    }

    private async Task<bool> HasMailboxAccess(int? mailboxId)
    {
        if (mailboxId == null)
        {
            return true;
        }

        var userId = int.Parse(User.FindFirst(ClaimTypes.NameIdentifier)?.Value ?? "0");
        if (await _userRoleResolver.IsSuperAdminAsync(userId))
        {
            return true;
        }

        var mailboxes = await _mailboxService.GetMailboxesByUserIdAsync(userId);
        return mailboxes.Any(m => m.Id == mailboxId);
    }

    /// <summary>
    /// 获取邮箱下的文件夹列表
    /// </summary>
    /// <param name="mailboxId">邮箱 ID</param>
    /// <returns>文件夹列表</returns>
    [HttpGet("mailbox/{mailboxId}")]
    public async Task<IActionResult> GetFoldersByMailbox(int mailboxId)
    {
        // 防止 IDOR：校验邮箱归属权限
        if (!await HasMailboxAccess(mailboxId))
        {
            return Forbid();
        }

        var folders = await _folderService.GetFoldersByMailboxIdAsync(mailboxId);
        return Ok(folders);
    }

    /// <summary>
    /// 获取用户的文件夹列表
    /// </summary>
    /// <param name="userId">用户 ID</param>
    /// <returns>文件夹列表</returns>
    /// <remarks>仅管理员可查询任意用户的文件夹；普通用户只能查询自己的文件夹</remarks>
    [HttpGet("user/{userId}")]
    public async Task<IActionResult> GetFoldersByUser(int userId)
    {
        // 防止 IDOR：仅允许超管查询他人文件夹，否则强制使用当前用户 ID
        var currentUserId = int.Parse(User.FindFirst(System.Security.Claims.ClaimTypes.NameIdentifier)?.Value ?? "0");
        var isSuperAdmin = await _userRoleResolver.IsSuperAdminAsync(currentUserId);
        if (!isSuperAdmin && userId != currentUserId)
        {
            return Forbid();
        }

        var folders = await _folderService.GetFoldersByUserIdAsync(userId);
        return Ok(folders);
    }

    /// <summary>
    /// 根据 ID 获取文件夹详情
    /// </summary>
    /// <param name="id">文件夹 ID</param>
    /// <returns>文件夹详情</returns>
    [HttpGet("{id}")]
    public async Task<IActionResult> GetFolder(int id)
    {
        var folder = await _folderService.GetFolderByIdAsync(id);

        if (folder == null)
        {
            return NotFound(new { message = "文件夹不存在" });
        }

        if (!await HasMailboxAccess(folder.MailboxId))
        {
            return Forbid();
        }

        return Ok(folder);
    }

    /// <summary>
    /// 创建新文件夹
    /// </summary>
    /// <param name="request">创建请求</param>
    /// <returns>新创建的文件夹</returns>
    /// <response code="201">文件夹创建成功</response>
    /// <response code="400">邮箱不存在或文件夹类型已存在</response>
    [HttpPost]
    [RequireSuperAdmin]
    public async Task<IActionResult> CreateFolder([FromBody] CreateFolderRequest request)
    {
        // request == null 与 ModelState 校验已由 [ApiController] 自动处理
        if (!Enum.IsDefined(typeof(NoneadCSS.Domain.Enums.FolderType), request.Type))
        {
            return BadRequest(new { message = "文件夹类型无效" });
        }

        var folder = await _folderService.CreateFolderAsync(request.MailboxId, request.Name, request.Type);

        if (folder == null)
        {
            return BadRequest(new { message = "邮箱不存在或文件夹类型已存在" });
        }

        return CreatedAtAction(nameof(GetFolder), new { id = folder.Id }, folder);
    }

    /// <summary>
    /// 更新文件夹名称
    /// </summary>
    /// <param name="id">文件夹 ID</param>
    /// <param name="request">更新请求</param>
    /// <returns>更新后的文件夹</returns>
    /// <response code="200">文件夹更新成功</response>
    /// <response code="404">文件夹不存在</response>
    [HttpPut("{id}")]
    [RequireSuperAdmin]
    public async Task<IActionResult> UpdateFolder(int id, [FromBody] UpdateFolderRequest request)
    {
        // request == null、Name 空白与长度校验已由 [ApiController] 基于 UpdateFolderRequest 的
        // [Required(AllowEmptyStrings=false)] 与 [MaxLength(100)] 自动处理

        var folder = await _folderService.UpdateFolderAsync(id, request.Name);

        if (folder == null)
        {
            return NotFound(new { message = "文件夹不存在" });
        }

        return Ok(folder);
    }

    /// <summary>
    /// 删除文件夹
    /// </summary>
    /// <param name="id">文件夹 ID</param>
    /// <returns>无内容</returns>
    /// <response code="204">删除成功</response>
    /// <response code="404">文件夹不存在</response>
    [HttpDelete("{id}")]
    [RequireSuperAdmin]
    public async Task<IActionResult> DeleteFolder(int id)
    {
        var result = await _folderService.DeleteFolderAsync(id);

        if (!result)
        {
            return NotFound(new { message = "文件夹不存在" });
        }

        return NoContent();
    }

    /// <summary>
    /// 重新计算并刷新指定邮箱下所有文件夹的活跃/总数计数
    /// </summary>
    /// <param name="mailboxId">邮箱 ID</param>
    /// <returns>受影响的文件夹数量</returns>
    [HttpPost("mailbox/{mailboxId}/refresh-counters")]
    [RequireSuperAdmin]
    public async Task<IActionResult> RefreshCounters(int mailboxId)
    {
        if (!await HasMailboxAccess(mailboxId))
        {
            return Forbid();
        }

        await _folderService.UpdateMailboxFolderCountersAsync(mailboxId);
        var folders = await _folderService.GetFoldersByMailboxIdAsync(mailboxId);
        return Ok(new { affected = folders.Count(), folders });
    }

    /// <summary>
    /// 确保指定邮箱下存在所有系统文件夹（未分配/我的/星标/草稿/已分配/已关闭/已删除/垃圾邮件）
    /// </summary>
    /// <param name="mailboxId">邮箱 ID</param>
    /// <returns>创建的文件夹数量</returns>
    [HttpPost("mailbox/{mailboxId}/ensure-system")]
    [RequireSuperAdmin]
    public async Task<IActionResult> EnsureSystemFolders(int mailboxId)
    {
        if (!await HasMailboxAccess(mailboxId))
        {
            return Forbid();
        }

        // 记录创建前数量
        var before = (await _folderService.GetFoldersByMailboxIdAsync(mailboxId)).Count();
        await _folderService.EnsureMailboxFoldersAsync(mailboxId);
        var after = (await _folderService.GetFoldersByMailboxIdAsync(mailboxId)).Count();
        return Ok(new { created = after - before, total = after });
    }
}
