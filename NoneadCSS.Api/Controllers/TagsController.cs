using NoneadCSS.Api.Authorization;
using NoneadCSS.Api.Filters;
using NoneadCSS.Application.DTOs.Requests;
using NoneadCSS.Application.Services;
using NoneadCSS.Domain;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.OutputCaching;
using System.Security.Claims;

namespace NoneadCSS.Api.Controllers;

/// <summary>
/// 标签控制器 - 管理系统标签
/// </summary>
[ApiController]
[Route("api/[controller]")]
[Authorize]
[RequireModule("core")]
public class TagsController : ControllerBase
{
    private readonly ITagService _tagService;
    private readonly ILocalizationService _localizer;
    private readonly IMailboxService _mailboxService;
    private readonly IUserRoleResolver _userRoleResolver;
    private readonly IUnitOfWork _unitOfWork;

    public TagsController(
        ITagService tagService,
        ILocalizationService localizer,
        IMailboxService mailboxService,
        IUserRoleResolver userRoleResolver,
        IUnitOfWork unitOfWork)
    {
        _tagService = tagService;
        _localizer = localizer;
        _mailboxService = mailboxService;
        _userRoleResolver = userRoleResolver;
        _unitOfWork = unitOfWork;
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
        return mailboxes.Any(m => m.Id == mailboxId.Value);
    }

    private async Task<bool> HasConversationAccess(int conversationId)
    {
        var userId = int.Parse(User.FindFirst(ClaimTypes.NameIdentifier)?.Value ?? "0");
        if (await _userRoleResolver.IsSuperAdminAsync(userId))
        {
            return true;
        }

        var conversation = await _unitOfWork.Conversations.GetByIdAsync(conversationId);
        if (conversation == null)
        {
            return false;
        }

        return await HasMailboxAccess(conversation.MailboxId);
    }

    /// <summary>
    /// 获取标签列表
    /// </summary>
    /// <param name="mailboxId">邮箱 ID（可选，不传返回全局标签）</param>
    /// <returns>标签列表</returns>
    [HttpGet]
    [OutputCache(PolicyName = "Tags")]
    public async Task<IActionResult> GetTags([FromQuery] int? mailboxId = null)
    {
        var tags = await _tagService.GetTagsAsync(mailboxId);
        return Ok(tags);
    }

    /// <summary>
    /// 获取标签详情
    /// </summary>
    /// <param name="id">标签 ID</param>
    /// <returns>标签详情</returns>
    /// <response code="200">返回标签详情</response>
    /// <response code="404">标签不存在</response>
    [HttpGet("{id:int}")]
    [OutputCache(PolicyName = "Tags")]
    public async Task<IActionResult> GetTag(int id)
    {
        var tag = await _tagService.GetTagByIdAsync(id);
        if (tag == null)
        {
            return NotFound(new { message = "标签不存在" });
        }

        if (!await HasMailboxAccess(tag.MailboxId))
        {
            return Forbid();
        }

        return Ok(tag);
    }

    /// <summary>
    /// 创建新标签
    /// </summary>
    /// <param name="request">创建标签请求</param>
    /// <returns>新创建的标签</returns>
    /// <response code="201">标签创建成功</response>
    /// <response code="400">标签名称已存在</response>
    [HttpPost]
    [Permission("tags.manage")]
    public async Task<IActionResult> CreateTag([FromBody] CreateTagRequest request)
    {
        try
        {
            var tag = await _tagService.CreateTagAsync(request);
            return CreatedAtAction(nameof(GetTag), new { id = tag.Id }, tag);
        }
        catch (InvalidOperationException ex)
        {
            return BadRequest(new { message = ex.Message });
        }
    }

    /// <summary>
    /// 更新标签
    /// </summary>
    /// <param name="id">标签 ID</param>
    /// <param name="request">更新标签请求</param>
    /// <returns>更新后的标签</returns>
    /// <response code="200">更新成功</response>
    /// <response code="404">标签不存在</response>
    /// <response code="400">标签名称已存在</response>
    [HttpPut("{id:int}")]
    [Permission("tags.manage")]
    public async Task<IActionResult> UpdateTag(int id, [FromBody] UpdateTagRequest request)
    {
        try
        {
            var tag = await _tagService.GetTagByIdAsync(id);
            if (tag == null)
            {
                return NotFound(new { message = "标签不存在" });
            }

            if (!await HasMailboxAccess(tag.MailboxId))
            {
                return Forbid();
            }

            var updatedTag = await _tagService.UpdateTagAsync(id, request);
            if (updatedTag == null)
            {
                return NotFound(new { message = "标签不存在" });
            }
            return Ok(updatedTag);
        }
        catch (InvalidOperationException ex)
        {
            return BadRequest(new { message = ex.Message });
        }
    }

    /// <summary>
    /// 删除标签（软删除，移至垃圾箱）
    /// </summary>
    /// <param name="id">标签 ID</param>
    /// <returns>无内容</returns>
    /// <response code="204">删除成功</response>
    /// <response code="404">标签不存在</response>
    [HttpDelete("{id:int}")]
    [Permission("tags.manage")]
    public async Task<IActionResult> DeleteTag(int id)
    {
        var tag = await _tagService.GetTagByIdAsync(id);
        if (tag == null)
        {
            return NotFound(new { message = "标签不存在" });
        }

        if (!await HasMailboxAccess(tag.MailboxId))
        {
            return Forbid();
        }

        var result = await _tagService.DeleteTagAsync(id);
        if (!result)
        {
            return NotFound(new { message = "标签不存在" });
        }
        return NoContent();
    }

    /// <summary>
    /// 获取垃圾箱中的标签列表
    /// </summary>
    /// <param name="page">页码（默认 1）</param>
    /// <param name="pageSize">每页条数（默认 20）</param>
    /// <param name="search">搜索关键词</param>
    /// <returns>分页标签列表</returns>
    [HttpGet("trash")]
    [RequireSuperAdmin]
    public async Task<IActionResult> GetTrash(
        [FromQuery] int page = 1,
        [FromQuery] int pageSize = PaginationHelper.DefaultPageSize,
        [FromQuery] string search = "")
    {
        // 限制分页参数上限，防止请求大量数据导致 DoS
        page = Math.Max(page, 1);
        pageSize = Math.Min(Math.Max(pageSize, 1), PaginationHelper.MaxPageSize);
        var result = await _tagService.GetTrashAsync(page, pageSize, search);
        return Ok(result);
    }

    /// <summary>
    /// 获取垃圾箱中标签的数量
    /// </summary>
    /// <returns>已删除标签数量</returns>
    [HttpGet("trash/count")]
    [RequireSuperAdmin]
    public async Task<IActionResult> GetTrashCount()
    {
        var count = await _tagService.GetTrashCountAsync();
        return Ok(new { count });
    }

    /// <summary>
    /// 恢复已删除的标签
    /// </summary>
    /// <param name="id">标签 ID</param>
    /// <returns>无内容</returns>
    [HttpPost("{id:int}/restore")]
    [Permission("tags.manage")]
    public async Task<IActionResult> RestoreTag(int id)
    {
        var result = await _tagService.RestoreTagAsync(id);
        if (!result)
        {
            return NotFound(new { message = "标签不存在" });
        }
        return NoContent();
    }

    /// <summary>
    /// 彻底删除标签
    /// </summary>
    /// <param name="id">标签 ID</param>
    /// <returns>无内容</returns>
    [HttpDelete("{id:int}/hard")]
    [Permission("tags.manage")]
    public async Task<IActionResult> HardDeleteTag(int id)
    {
        var result = await _tagService.HardDeleteTagAsync(id);
        if (!result)
        {
            return NotFound(new { message = "标签不存在" });
        }
        return NoContent();
    }

    /// <summary>
    /// 清空垃圾箱（彻底删除所有已软删除的标签）
    /// </summary>
    /// <returns>删除的标签数量</returns>
    [HttpDelete("trash")]
    [Permission("tags.manage")]
    public async Task<IActionResult> ClearTrash()
    {
        var count = await _tagService.ClearTrashAsync();
        return Ok(new { count });
    }

    /// <summary>
    /// 获取会话的标签列表
    /// </summary>
    /// <param name="conversationId">会话 ID</param>
    /// <returns>标签列表</returns>
    [HttpGet("conversations/{conversationId}")]
    public async Task<IActionResult> GetConversationTags(int conversationId)
    {
        if (!await HasConversationAccess(conversationId))
        {
            return Forbid();
        }
        var tags = await _tagService.GetConversationTagsAsync(conversationId);
        return Ok(tags);
    }

    /// <summary>
    /// 更新会话的标签（全量替换）
    /// </summary>
    /// <param name="conversationId">会话 ID</param>
    /// <param name="request">标签 ID 列表</param>
    /// <returns>更新后的标签列表</returns>
    /// <response code="200">更新成功</response>
    [HttpPut("conversations/{conversationId}")]
    [Permission("conversations.update")]
    public async Task<IActionResult> UpdateConversationTags(int conversationId, [FromBody] UpdateConversationTagsRequest request)
    {
        if (!await HasConversationAccess(conversationId))
        {
            return Forbid();
        }
        try
        {
            var tags = await _tagService.UpdateConversationTagsAsync(conversationId, request);
            return Ok(tags);
        }
        catch (InvalidOperationException ex)
        {
            return BadRequest(new { message = ex.Message });
        }
    }

    /// <summary>
    /// 给会话添加单个标签
    /// </summary>
    /// <param name="conversationId">会话 ID</param>
    /// <param name="tagId">标签 ID</param>
    /// <returns>更新后的标签列表</returns>
    /// <response code="200">添加成功</response>
    [HttpPost("conversations/{conversationId}/tags/{tagId}")]
    [Permission("conversations.update")]
    public async Task<IActionResult> AddTagToConversation(int conversationId, int tagId)
    {
        if (!await HasConversationAccess(conversationId))
        {
            return Forbid();
        }
        try
        {
            var tags = await _tagService.AddTagToConversationAsync(conversationId, tagId);
            return Ok(tags);
        }
        catch (InvalidOperationException ex)
        {
            return BadRequest(new { message = ex.Message });
        }
    }

    /// <summary>
    /// 从会话移除单个标签
    /// </summary>
    /// <param name="conversationId">会话 ID</param>
    /// <param name="tagId">标签 ID</param>
    /// <returns>无内容</returns>
    /// <response code="204">移除成功</response>
    /// <response code="404">标签不在会话中</response>
    [HttpDelete("conversations/{conversationId}/tags/{tagId}")]
    [Permission("conversations.update")]
    public async Task<IActionResult> RemoveTagFromConversation(int conversationId, int tagId)
    {
        if (!await HasConversationAccess(conversationId))
        {
            return Forbid();
        }
        var result = await _tagService.RemoveTagFromConversationAsync(conversationId, tagId);
        if (!result)
        {
            return NotFound(new { message = "标签不在该会话中" });
        }
        return NoContent();
    }

    /// <summary>
    /// 批量更新会话标签
    /// </summary>
    /// <param name="request">批量标签更新请求（会话 ID 列表 + 标签 ID 列表）</param>
    /// <returns>批量操作结果</returns>
    [HttpPost("conversations/bulk")]
    [Permission("conversations.update")]
    public async Task<IActionResult> BulkUpdateConversationTags([FromBody] BulkUpdateConversationTagsRequest request)
    {
        // ConversationIds null/空校验已由 [ApiController] 基于 [Required] + [MinLength(1)] 自动处理

        foreach (var conversationId in request.ConversationIds.Distinct())
        {
            if (!await HasConversationAccess(conversationId))
            {
                return Forbid();
            }
        }

        var result = await _tagService.BulkUpdateConversationTagsAsync(request);
        return Ok(result);
    }
}
