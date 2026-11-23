using NoneadCSS.Api.Authorization;
using NoneadCSS.Api.Filters;
using NoneadCSS.Application.DTOs.Requests;
using NoneadCSS.Application.DTOs.Responses;
using NoneadCSS.Application.Services;
using NoneadCSS.Domain.Enums;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.Extensions.Logging;
using System.Security.Claims;

namespace NoneadCSS.Api.Controllers;

/// <summary>
/// 会话控制器 - 管理客户支持工单（会话）的查询、创建、状态更新等操作
/// </summary>
[Route("api/[controller]")]
[RequireModule("core")]
public class ConversationsController : BaseController
{
    private readonly IConversationService _conversationService;
    private readonly ILocalizationService _localizer;
    private readonly IMailboxService _mailboxService;
    private readonly IUserRoleResolver _userRoleResolver;
    private readonly ILogger<ConversationsController> _logger;
    private readonly ICurrentUserAccessor _currentUserAccessor;

    public ConversationsController(IConversationService conversationService, ILocalizationService localizer,
        IMailboxService mailboxService, IUserRoleResolver userRoleResolver,
        ILogger<ConversationsController> logger, ICurrentUserAccessor currentUserAccessor)
    {
        _conversationService = conversationService;
        _localizer = localizer;
        _mailboxService = mailboxService;
        _userRoleResolver = userRoleResolver;
        _logger = logger;
        _currentUserAccessor = currentUserAccessor;
    }

    private async Task<bool> HasMailboxAccess(int mailboxId)
    {
        // TODO: 使用 ICurrentUserAccessor 统一获取当前用户 ID，避免重复解析 Claims
        var userId = CurrentUserId ?? 0;
        if (await _userRoleResolver.IsSuperAdminAsync(userId))
        {
            return true;
        }
        var mailboxes = await _mailboxService.GetMailboxesByUserIdAsync(userId);
        return mailboxes.Any(m => m.Id == mailboxId);
    }

    private async Task<bool> HasBulkConversationAccess(IEnumerable<int> conversationIds)
    {
        var userId = CurrentUserId ?? 0;
        if (await _userRoleResolver.IsSuperAdminAsync(userId))
        {
            return true;
        }

        var accessibleMailboxIds = (await _mailboxService.GetMailboxesByUserIdAsync(userId))
            .Select(m => m.Id)
            .ToHashSet();

        var distinctIds = conversationIds.Distinct().ToList();
        if (distinctIds.Count == 0)
        {
            return true;
        }

        // 一次查询所有会话，避免 N+1
        var conversations = await _conversationService.GetConversationsByIdsAsync(distinctIds);
        foreach (var conversation in conversations)
        {
            if (!accessibleMailboxIds.Contains(conversation.MailboxId))
            {
                return false;
            }
        }

        return true;
    }

    /// <summary>
    /// 获取会话列表
    /// </summary>
    /// <remarks>
    /// 支持按邮箱、文件夹过滤，支持关键词搜索和分页。
    /// 不传任何参数时返回当前用户分配的会话。
    /// </remarks>
    /// <param name="mailboxId">邮箱 ID（可选，0 表示不筛选）</param>
    /// <param name="folderId">文件夹 ID（可选，0 表示不筛选）</param>
    /// <param name="page">页码（默认 1）</param>
    /// <param name="pageSize">每页条数（默认 20）</param>
    /// <param name="search">搜索关键词（可选）</param>
    /// <param name="status">状态筛选（可选，null 表示不筛选）</param>
    /// <param name="priority">优先级筛选（可选，null 表示不筛选）</param>
    /// <param name="starred">是否仅查看星标会话（默认 false）</param>
    /// <param name="customerId">客户 ID 筛选（可选，null 表示不筛选）</param>
    /// <param name="sourceType">来源类型筛选（可选，null 表示不筛选）：Email/Web/Api/Phone/Chat/Social/WeChat</param>
    /// <returns>分页会话列表</returns>
    [HttpGet]
    public async Task<IActionResult> GetConversations(
        [FromQuery] int mailboxId = 0,
        [FromQuery] int folderId = 0,
        [FromQuery] int page = 1,
        [FromQuery] int pageSize = PaginationHelper.DefaultPageSize,
        [FromQuery] string? search = null,
        [FromQuery] int? status = null,
        [FromQuery] int? priority = null,
        [FromQuery] bool starred = false,
        [FromQuery] int? customerId = null,
        [FromQuery] SourceType? sourceType = null)
    {
        var userId = CurrentUserId ?? 0;

        // 邮箱级授权（IDOR 防护）：与同文件其它 40+ 个端点保持一致，
        // 显式校验指定邮箱的归属；服务层另有白名单兜底（覆盖 mailboxId=0 的全量查询场景）。
        if (mailboxId > 0 && !await HasMailboxAccess(mailboxId))
        {
            return Forbid();
        }

        // 规范化分页参数，防止请求大量数据导致 DoS
        page = Math.Max(page, 1);
        pageSize = Math.Min(Math.Max(pageSize, 1), PaginationHelper.MaxPageSize);

        if (!string.IsNullOrEmpty(search))
        {
            var result = await _conversationService.SearchAsync(search, page, pageSize, userId, userId);
            return Ok(result);
        }

        int? starredByUserId = starred ? userId : null;
        var response = await _conversationService.GetConversationsAsync(mailboxId, folderId, page, pageSize, userId, status, priority, starredByUserId, customerId, sourceType, userId);
        return Ok(response);
    }

    /// <summary>
    /// 获取会话详情
    /// </summary>
    /// <param name="id">会话 ID</param>
    /// <returns>会话详细信息（包含消息线程）</returns>
    /// <response code="200">返回会话详情</response>
    /// <response code="404">会话不存在</response>
    /// <response code="403">无访问权限</response>
    [HttpGet("{id}")]
    public async Task<IActionResult> GetConversation(int id)
    {
        var conversation = await _conversationService.GetConversationByIdAsync(id);

        if (conversation == null)
        {
            return NotFound(new { message = _localizer["ConversationNotFound"] });
        }

        if (!await HasMailboxAccess(conversation.MailboxId))
        {
            return Forbid();
        }

        // 进入会话详情时标记为已读，使列表页未读徽章消失
        var userId = _currentUserAccessor.UserId;
        if (userId > 0)
        {
            try
            {
                await _conversationService.MarkConversationAsReadAsync(id, userId);
            }
            catch (Exception ex)
            {
                // 阅读状态更新失败不应阻塞会话详情查看
                _logger.LogWarning(ex, "标记会话已读失败，ConversationId={ConversationId}", id);
            }
        }

        return Ok(conversation);
    }

    /// <summary>
    /// 标记会话为已读（清除列表页未读徽章）
    /// </summary>
    /// <remarks>更新当前用户对该会话的最后阅读时间为当前 UTC 时间。下次列表查询时该会话不再显示未读标识。</remarks>
    /// <param name="id">会话 ID</param>
    /// <returns>无内容</returns>
    /// <response code="204">标记成功</response>
    /// <response code="404">会话不存在</response>
    /// <response code="403">无访问权限</response>
    [HttpPost("{id}/mark-read")]
    public async Task<IActionResult> MarkAsRead(int id)
    {
        var conversation = await _conversationService.GetConversationByIdAsync(id);

        if (conversation == null)
        {
            return NotFound(new { message = _localizer["ConversationNotFound"] });
        }

        if (!await HasMailboxAccess(conversation.MailboxId))
        {
            return Forbid();
        }

        var userId = CurrentUserId ?? 0;
        if (userId > 0)
        {
            await _conversationService.MarkConversationAsReadAsync(id, userId);
        }

        return NoContent();
    }

    /// <summary>
    /// 创建新会话
    /// </summary>
    /// <param name="request">创建会话请求</param>
    /// <returns>新创建的会话</returns>
    /// <response code="201">会话创建成功</response>
    [HttpPost]
    [Permission("conversations.create")]
    public async Task<IActionResult> CreateConversation([FromBody] CreateConversationRequest request)
    {
        var conversation = await _conversationService.CreateConversationAsync(request);
        return CreatedAtAction(nameof(GetConversation), new { id = conversation.Id }, conversation);
    }

    /// <summary>
    /// 更新会话状态
    /// </summary>
    /// <param name="id">会话 ID</param>
    /// <param name="request">状态更新请求</param>
    /// <returns>更新后的会话</returns>
    /// <response code="200">状态更新成功</response>
    /// <response code="404">会话不存在</response>
    /// <response code="403">无访问权限</response>
    [HttpPut("{id}/status")]
    [Permission("conversations.update")]
    public async Task<IActionResult> UpdateStatus(int id, [FromBody] UpdateConversationStatusRequest request)
    {
        var userId = CurrentUserId ?? 0;
        var conversation = await _conversationService.GetConversationByIdAsync(id);

        if (conversation == null)
        {
            return NotFound(new { message = _localizer["ConversationNotFound"] });
        }

        if (!await HasMailboxAccess(conversation.MailboxId))
        {
            return Forbid();
        }

        var updated = await _conversationService.UpdateStatusAsync(id, request.Status, userId);
        return Ok(updated);
    }

    /// <summary>
    /// 更新会话优先级
    /// </summary>
    /// <param name="id">会话 ID</param>
    /// <param name="request">优先级更新请求</param>
    /// <returns>更新后的会话</returns>
    /// <response code="200">优先级更新成功</response>
    /// <response code="404">会话不存在</response>
    /// <response code="403">无访问权限</response>
    [HttpPut("{id}/priority")]
    [Permission("conversations.update")]
    public async Task<IActionResult> UpdatePriority(int id, [FromBody] UpdateConversationPriorityRequest request)
    {
        var conversation = await _conversationService.GetConversationByIdAsync(id);

        if (conversation == null)
        {
            return NotFound(new { message = _localizer["ConversationNotFound"] });
        }

        if (!await HasMailboxAccess(conversation.MailboxId))
        {
            return Forbid();
        }

        var updated = await _conversationService.UpdatePriorityAsync(id, request.Priority);
        return Ok(updated);
    }

    /// <summary>
    /// 分配会话给指定用户
    /// </summary>
    /// <param name="id">会话 ID</param>
    /// <param name="request">分配请求（包含目标用户 ID）</param>
    /// <returns>分配后的会话</returns>
    /// <response code="200">分配成功</response>
    /// <response code="404">会话不存在</response>
    /// <response code="403">无访问权限</response>
    [HttpPut("{id}/assign")]
    [Permission("conversations.assign")]
    public async Task<IActionResult> AssignConversation(int id, [FromBody] AssignConversationRequest request)
    {
        var conversation = await _conversationService.GetConversationByIdAsync(id);

        if (conversation == null)
        {
            return NotFound(new { message = _localizer["ConversationNotFound"] });
        }

        if (!await HasMailboxAccess(conversation.MailboxId))
        {
            return Forbid();
        }

        var assigned = await _conversationService.AssignAsync(id, request.UserId);
        return Ok(assigned);
    }

    /// <summary>
    /// 删除会话
    /// </summary>
    /// <param name="id">会话 ID</param>
    /// <returns>无内容</returns>
    /// <response code="204">删除成功</response>
    /// <response code="404">会话不存在</response>
    /// <response code="403">无访问权限</response>
    [HttpDelete("{id}")]
    [Permission("conversations.delete")]
    public async Task<IActionResult> DeleteConversation(int id)
    {
        var conversation = await _conversationService.GetConversationByIdAsync(id);

        if (conversation == null)
        {
            return NotFound(new { message = _localizer["ConversationNotFound"] });
        }

        if (!await HasMailboxAccess(conversation.MailboxId))
        {
            return Forbid();
        }

        await _conversationService.DeleteConversationAsync(id);

        return NoContent();
    }

    /// <summary>
    /// 获取会话的消息线程列表
    /// </summary>
    /// <param name="id">会话 ID</param>
    /// <returns>消息线程列表</returns>
    /// <response code="200">返回消息线程列表</response>
    [HttpGet("{id}/threads")]
    public async Task<IActionResult> GetThreads(int id)
    {
        var conversation = await _conversationService.GetConversationByIdAsync(id);

        if (conversation == null)
        {
            return NotFound(new { message = _localizer["ConversationNotFound"] });
        }

        if (!await HasMailboxAccess(conversation.MailboxId))
        {
            return Forbid();
        }

        var threads = await _conversationService.GetThreadsByConversationIdAsync(id);
        return Ok(threads);
    }

    /// <summary>
    /// 回复会话（添加消息线程）
    /// </summary>
    /// <param name="id">会话 ID</param>
    /// <param name="request">回复请求</param>
    /// <returns>新建的消息线程</returns>
    /// <response code="201">回复成功</response>
    /// <response code="404">会话不存在</response>
    /// <response code="403">无访问权限</response>
    [HttpPost("{id}/reply")]
    [Permission("conversations.reply")]
    public async Task<IActionResult> Reply(int id, [FromBody] CreateThreadRequest request)
    {
        request.ConversationId = id;

        var conversation = await _conversationService.GetConversationByIdAsync(id);

        if (conversation == null)
        {
            return NotFound(new { message = _localizer["ConversationNotFound"] });
        }

        if (!await HasMailboxAccess(conversation.MailboxId))
        {
            return Forbid();
        }

        var userId = CurrentUserId ?? 0;
        var thread = await _conversationService.AddReplyAsync(request, userId);
        return CreatedAtAction(nameof(GetThreads), new { id }, thread);
    }

    /// <summary>
    /// 切换关注状态（关注/取消关注会话）
    /// </summary>
    /// <param name="id">会话 ID</param>
    /// <returns>更新后的会话</returns>
    /// <response code="200">操作成功</response>
    /// <response code="404">会话不存在</response>
    /// <response code="403">无访问权限</response>
    [HttpPost("{id}/follow")]
    [Permission("conversations.update")]
    public async Task<IActionResult> ToggleFollow(int id)
    {
        var conversation = await _conversationService.GetConversationByIdAsync(id);

        if (conversation == null)
        {
            return NotFound(new { message = _localizer["ConversationNotFound"] });
        }

        if (!await HasMailboxAccess(conversation.MailboxId))
        {
            return Forbid();
        }

        var userId = CurrentUserId ?? 0;
        var updated = await _conversationService.ToggleFollowAsync(id, userId);
        return Ok(updated);
    }

    /// <summary>
    /// 获取会话关注者列表
    /// </summary>
    /// <param name="id">会话 ID</param>
    /// <returns>关注者用户 ID 列表</returns>
    /// <response code="403">无访问权限</response>
    [HttpGet("{id}/followers")]
    public async Task<IActionResult> GetFollowers(int id)
    {
        var conversation = await _conversationService.GetConversationByIdAsync(id);

        if (conversation == null)
        {
            return NotFound(new { message = _localizer["ConversationNotFound"] });
        }

        if (!await HasMailboxAccess(conversation.MailboxId))
        {
            return Forbid();
        }

        var followers = await _conversationService.GetFollowersAsync(id);
        return Ok(followers);
    }

    /// <summary>
    /// 添加内部备注
    /// </summary>
    /// <param name="id">会话 ID</param>
    /// <param name="request">备注内容</param>
    /// <returns>新建的备注线程</returns>
    /// <response code="201">备注添加成功</response>
    /// <response code="404">会话不存在</response>
    /// <response code="403">无访问权限</response>
    [HttpPost("{id}/notes")]
    [Permission("conversations.update")]
    public async Task<IActionResult> AddNote(int id, [FromBody] AddNoteRequest request)
    {
        var conversation = await _conversationService.GetConversationByIdAsync(id);

        if (conversation == null)
        {
            return NotFound(new { message = _localizer["ConversationNotFound"] });
        }

        if (!await HasMailboxAccess(conversation.MailboxId))
        {
            return Forbid();
        }

        var userId = CurrentUserId ?? 0;
        var thread = await _conversationService.AddNoteAsync(id, request, userId);
        return CreatedAtAction(nameof(GetThreads), new { id }, thread);
    }

    /// <summary>
    /// 批量分配会话
    /// </summary>
    /// <param name="request">批量分配请求（会话 ID 列表 + 目标用户 ID）</param>
    /// <returns>批量操作结果</returns>
    [HttpPost("bulk/assign")]
    [Permission("conversations.assign")]
    public async Task<IActionResult> BulkAssign([FromBody] BulkAssignRequest request)
    {
        if (!await HasBulkConversationAccess(request.ConversationIds))
        {
            return Forbid();
        }
        var userId = CurrentUserId ?? 0;
        var result = await _conversationService.BulkAssignAsync(request, userId);
        return Ok(result);
    }

    /// <summary>
    /// 批量更新会话状态
    /// </summary>
    /// <param name="request">批量状态更新请求（会话 ID 列表 + 目标状态）</param>
    /// <returns>批量操作结果</returns>
    [HttpPost("bulk/status")]
    [Permission("conversations.update")]
    public async Task<IActionResult> BulkUpdateStatus([FromBody] BulkStatusRequest request)
    {
        if (!await HasBulkConversationAccess(request.ConversationIds))
        {
            return Forbid();
        }
        var result = await _conversationService.BulkUpdateStatusAsync(request);
        return Ok(result);
    }

    /// <summary>
    /// 批量更新会话优先级
    /// </summary>
    /// <param name="request">批量优先级更新请求（会话 ID 列表 + 目标优先级）</param>
    /// <returns>批量操作结果</returns>
    [HttpPost("bulk/priority")]
    [Permission("conversations.update")]
    public async Task<IActionResult> BulkUpdatePriority([FromBody] BulkPriorityRequest request)
    {
        if (!await HasBulkConversationAccess(request.ConversationIds))
        {
            return Forbid();
        }
        var result = await _conversationService.BulkUpdatePriorityAsync(request);
        return Ok(result);
    }

    /// <summary>
    /// 批量删除会话
    /// </summary>
    /// <param name="request">批量删除请求（会话 ID 列表）</param>
    /// <returns>批量操作结果</returns>
    [HttpPost("bulk/delete")]
    [Permission("conversations.delete")]
    public async Task<IActionResult> BulkDelete([FromBody] BulkConversationRequest request)
    {
        if (!await HasBulkConversationAccess(request.ConversationIds))
        {
            return Forbid();
        }
        var result = await _conversationService.BulkDeleteAsync(request);
        return Ok(result);
    }

    /// <summary>
    /// 保存草稿
    /// </summary>
    /// <param name="id">会话 ID</param>
    /// <param name="request">草稿内容</param>
    /// <returns>草稿线程</returns>
    /// <response code="200">保存成功</response>
    /// <response code="404">会话不存在</response>
    [HttpPost("{id}/draft")]
    [Permission("conversations.update")]
    public async Task<IActionResult> SaveDraft(int id, [FromBody] CreateThreadRequest request)
    {
        var conversation = await _conversationService.GetConversationByIdAsync(id);

        if (conversation == null)
        {
            return NotFound(new { message = _localizer["ConversationNotFound"] });
        }

        if (!await HasMailboxAccess(conversation.MailboxId))
        {
            return Forbid();
        }

        request.ConversationId = id;
        var userId = CurrentUserId ?? 0;
        var draft = await _conversationService.SaveDraftAsync(request, userId);

        if (draft == null)
        {
            return NotFound(new { message = _localizer["ConversationNotFound"] });
        }

        return Ok(draft);
    }

    /// <summary>
    /// 获取会话草稿
    /// </summary>
    /// <param name="id">会话 ID</param>
    /// <returns>草稿线程</returns>
    /// <response code="200">返回草稿</response>
    /// <response code="404">草稿不存在</response>
    [HttpGet("{id}/draft")]
    public async Task<IActionResult> GetDraft(int id)
    {
        var conversation = await _conversationService.GetConversationByIdAsync(id);

        if (conversation == null)
        {
            return NotFound(new { message = _localizer["ConversationNotFound"] });
        }

        if (!await HasMailboxAccess(conversation.MailboxId))
        {
            return Forbid();
        }

        var draft = await _conversationService.GetDraftAsync(id);

        if (draft == null)
        {
            return NotFound(new { message = _localizer["DraftNotFound"] });
        }

        return Ok(draft);
    }

    /// <summary>
    /// 删除草稿
    /// </summary>
    /// <param name="id">会话 ID</param>
    /// <returns>无内容</returns>
    /// <response code="204">删除成功</response>
    /// <response code="404">草稿不存在</response>
    [HttpDelete("{id}/draft")]
    [Permission("conversations.update")]
    public async Task<IActionResult> DeleteDraft(int id)
    {
        var conversation = await _conversationService.GetConversationByIdAsync(id);

        if (conversation == null)
        {
            return NotFound(new { message = _localizer["ConversationNotFound"] });
        }

        if (!await HasMailboxAccess(conversation.MailboxId))
        {
            return Forbid();
        }

        var result = await _conversationService.DeleteDraftAsync(id);

        if (!result)
        {
            return NotFound(new { message = _localizer["DraftNotFound"] });
        }

        return NoContent();
    }

    /// <summary>
    /// 获取当前用户的所有草稿
    /// </summary>
    /// <returns>草稿列表</returns>
    [HttpGet("drafts")]
    public async Task<IActionResult> GetMyDrafts()
    {
        var userId = CurrentUserId ?? 0;
        var drafts = await _conversationService.GetDraftsByUserIdAsync(userId);
        return Ok(drafts);
    }

    /// <summary>
    /// 高级搜索会话
    /// </summary>
    /// <remarks>
    /// 支持多条件组合筛选，包括关键词、状态、类型、来源、邮箱、用户、客户、日期范围等。
    /// </remarks>
    /// <param name="keyword">搜索关键词（匹配主题、客户邮箱、预览文本、客户姓名）</param>
    /// <param name="status">会话状态</param>
    /// <param name="type">会话类型（Email/Phone/Chat）</param>
    /// <param name="sourceType">来源类型（Email/Web/API等）</param>
    /// <param name="mailboxId">邮箱 ID</param>
    /// <param name="userId">分配用户 ID</param>
    /// <param name="customerId">客户 ID</param>
    /// <param name="customerEmail">客户邮箱</param>
    /// <param name="startDate">创建开始日期（ISO格式）</param>
    /// <param name="endDate">创建结束日期（ISO格式）</param>
    /// <param name="lastReplyStartDate">最后回复开始日期（ISO格式）</param>
    /// <param name="lastReplyEndDate">最后回复结束日期（ISO格式）</param>
    /// <param name="hasAttachments">是否包含附件</param>
    /// <param name="isUnassigned">是否未分配</param>
    /// <param name="page">页码（默认 1）</param>
    /// <param name="pageSize">每页条数（默认 20）</param>
    /// <returns>分页会话列表</returns>
    [HttpGet("advanced-search")]
    public async Task<IActionResult> AdvancedSearch(
        [FromQuery] string? keyword = null,
        [FromQuery] ConversationStatus? status = null,
        [FromQuery] ConversationType? type = null,
        [FromQuery] SourceType? sourceType = null,
        [FromQuery] int? mailboxId = null,
        [FromQuery] int? userId = null,
        [FromQuery] int? customerId = null,
        [FromQuery] string? customerEmail = null,
        [FromQuery] DateTime? startDate = null,
        [FromQuery] DateTime? endDate = null,
        [FromQuery] DateTime? lastReplyStartDate = null,
        [FromQuery] DateTime? lastReplyEndDate = null,
        [FromQuery] bool? hasAttachments = null,
        [FromQuery] bool? isUnassigned = null,
        [FromQuery] int page = 1,
        [FromQuery] int pageSize = PaginationHelper.DefaultPageSize)
    {
        // 限制分页参数上限，防止请求大量数据导致 DoS
        page = Math.Max(page, 1);
        pageSize = Math.Min(Math.Max(pageSize, 1), PaginationHelper.MaxPageSize);

        var currentUserId = CurrentUserId ?? 0;
        var isSuperAdmin = await _userRoleResolver.IsSuperAdminAsync(currentUserId);

        if (!isSuperAdmin)
        {
            if (mailboxId.HasValue)
            {
                if (!await HasMailboxAccess(mailboxId.Value))
                {
                    return Forbid();
                }
            }
            else
            {
                // 非超管用户不传 mailboxId 时，限制到第一个可访问邮箱，
                // 防止越权搜索所有邮箱的会话（IDOR）
                var mailboxes = await _mailboxService.GetMailboxesByUserIdAsync(currentUserId);
                if (!mailboxes.Any())
                {
                    return Ok(new PagedResponse<ConversationResponse>
                    {
                        Data = new List<ConversationResponse>(),
                        CurrentPage = page,
                        PageSize = pageSize,
                        TotalItems = 0,
                        TotalPages = 0
                    });
                }
                mailboxId = mailboxes.First().Id;
            }
        }

        var result = await _conversationService.AdvancedSearchAsync(
            keyword, status, type, sourceType, mailboxId, userId, customerId,
            customerEmail, startDate, endDate, lastReplyStartDate, lastReplyEndDate,
            hasAttachments, isUnassigned, page, pageSize, currentUserId);

        return Ok(result);
    }

    /// <summary>
    /// 创建电话会话
    /// </summary>
    /// <remarks>
    /// 创建电话类型的会话，支持通过电话号码查找或创建客户，并记录通话信息。
    /// </remarks>
    /// <param name="request">创建电话会话请求</param>
    /// <returns>新创建的电话会话</returns>
    /// <response code="201">电话会话创建成功</response>
    [HttpPost("phone")]
    [Permission("conversations.create")]
    public async Task<IActionResult> CreatePhoneConversation([FromBody] CreatePhoneConversationRequest request)
    {
        if (!await HasMailboxAccess(request.MailboxId))
        {
            return Forbid();
        }

        var userId = CurrentUserId ?? 0;
        var conversation = await _conversationService.CreatePhoneConversationAsync(request, userId);
        return CreatedAtAction(nameof(GetConversation), new { id = conversation.Id }, conversation);
    }

    /// <summary>
    /// 切换星标状态（星标/取消星标会话）
    /// </summary>
    /// <param name="id">会话 ID</param>
    /// <returns>更新后的会话</returns>
    /// <response code="200">操作成功</response>
    /// <response code="404">会话不存在</response>
    [HttpPost("{id}/star")]
    [Permission("conversations.update")]
    public async Task<IActionResult> ToggleStar(int id)
    {
        var conversation = await _conversationService.GetConversationByIdAsync(id);

        if (conversation == null)
        {
            return NotFound(new { message = _localizer["ConversationNotFound"] });
        }

        if (!await HasMailboxAccess(conversation.MailboxId))
        {
            return Forbid();
        }

        var userId = CurrentUserId ?? 0;
        var updated = await _conversationService.ToggleStarAsync(id, userId);

        if (updated == null)
        {
            return NotFound(new { message = _localizer["ConversationNotFound"] });
        }

        return Ok(updated);
    }

    /// <summary>
    /// 获取当前用户星标的会话列表
    /// </summary>
    /// <param name="page">页码（默认 1）</param>
    /// <param name="pageSize">每页条数（默认 20）</param>
    /// <returns>分页星标会话列表</returns>
    [HttpGet("starred")]
    public async Task<IActionResult> GetStarredConversations(
        [FromQuery] int page = 1,
        [FromQuery] int pageSize = PaginationHelper.DefaultPageSize)
    {
        // 限制分页参数上限，防止请求大量数据导致 DoS
        page = Math.Max(page, 1);
        pageSize = Math.Min(Math.Max(pageSize, 1), PaginationHelper.MaxPageSize);
        var userId = CurrentUserId ?? 0;
        var conversations = await _conversationService.GetStarredConversationsAsync(userId, page, pageSize);
        return Ok(conversations);
    }

    /// <summary>
    /// 获取当前用户星标的会话 ID 列表
    /// </summary>
    /// <returns>星标会话 ID 列表</returns>
    [HttpGet("starred-ids")]
    public async Task<IActionResult> GetStarredConversationIds()
    {
        var userId = CurrentUserId ?? 0;
        var ids = await _conversationService.GetStarredConversationIdsAsync(userId);
        return Ok(ids);
    }

    /// <summary>
    /// 克隆会话
    /// </summary>
    /// <remarks>创建一个现有会话的副本，包含原始会话的基本信息但不包含消息线程。</remarks>
    /// <param name="id">源会话 ID</param>
    /// <returns>新克隆的会话</returns>
    /// <response code="201">克隆成功</response>
    /// <response code="404">源会话不存在</response>
    [HttpPost("{id}/clone")]
    [Permission("conversations.create")]
    public async Task<IActionResult> CloneConversation(int id)
    {
        var conversation = await _conversationService.GetConversationByIdAsync(id);

        if (conversation == null)
        {
            return NotFound(new { message = _localizer["ConversationNotFound"] });
        }

        if (!await HasMailboxAccess(conversation.MailboxId))
        {
            return Forbid();
        }

        var userId = CurrentUserId ?? 0;
        var cloned = await _conversationService.CloneConversationAsync(id, userId);

        if (cloned == null)
        {
            return NotFound(new { message = _localizer["ConversationNotFound"] });
        }

        return CreatedAtAction(nameof(GetConversation), new { id = cloned.Id }, cloned);
    }

    /// <summary>
    /// 彻底删除会话（从数据库永久删除）
    /// </summary>
    /// <remarks>仅超管可调用此接口。永久删除会话及其所有关联数据，无法恢复。</remarks>
    /// <param name="id">会话 ID</param>
    /// <returns>无内容</returns>
    /// <response code="204">删除成功</response>
    /// <response code="404">会话不存在</response>
    [HttpDelete("{id}/hard")]
    [RequireSuperAdmin]
    public async Task<IActionResult> HardDelete(int id)
    {
        var result = await _conversationService.HardDeleteConversationAsync(id);

        if (!result)
        {
            return NotFound(new { message = _localizer["ConversationNotFound"] });
        }

        return NoContent();
    }

    /// <summary>
    /// 获取垃圾箱列表（已软删除的会话）。
    /// </summary>
    /// <param name="mailboxId">邮箱 ID（可选，0 或不传表示所有邮箱）</param>
    /// <param name="page">页码</param>
    /// <param name="pageSize">每页条数</param>
    /// <returns>分页已删除会话列表</returns>
    [HttpGet("trash")]
    public async Task<IActionResult> GetTrash([FromQuery] int mailboxId = 0, [FromQuery] int page = 1, [FromQuery] int pageSize = PaginationHelper.DefaultPageSize)
    {
        // 防止 IDOR：mailboxId=0 表示所有邮箱，仅超管可查看全局垃圾箱
        if (mailboxId == 0)
        {
            var userId = CurrentUserId ?? 0;
            if (!await _userRoleResolver.IsSuperAdminAsync(userId))
            {
                return Forbid();
            }
        }
        else if (!await HasMailboxAccess(mailboxId))
        {
            return Forbid();
        }

        // 限制分页参数上限，防止请求大量数据导致 DoS
        page = Math.Max(page, 1);
        pageSize = Math.Min(Math.Max(pageSize, 1), PaginationHelper.MaxPageSize);
        var response = await _conversationService.GetTrashByMailboxAsync(mailboxId, page, pageSize);
        return Ok(response);
    }

    /// <summary>
    /// 恢复指定会话（从垃圾箱还原为已发布状态）。
    /// </summary>
    /// <param name="id">会话 ID</param>
    /// <returns>恢复后的会话</returns>
    [HttpPost("{id}/restore")]
    [Permission("conversations.delete")]
    public async Task<IActionResult> Restore(int id)
    {
        var conversation = await _conversationService.RestoreConversationAsync(id);

        if (conversation == null)
        {
            return NotFound(new { message = _localizer["ConversationNotFound"] });
        }

        if (!await HasMailboxAccess(conversation.MailboxId))
        {
            return Forbid();
        }

        return Ok(conversation);
    }

    /// <summary>
    /// 清空垃圾箱（彻底删除所有已软删除的会话）。
    /// </summary>
    /// <param name="mailboxId">邮箱 ID（可选，0 或不传表示清空所有邮箱的垃圾箱）</param>
    /// <returns>被彻底删除的会话数量</returns>
    [HttpDelete("trash")]
    [RequireSuperAdmin]
    public async Task<IActionResult> ClearTrash([FromQuery] int mailboxId = 0)
    {
        // 该操作属于永久物理删除，整体提升到超管权限
        var userId = CurrentUserId ?? 0;
        if (!await _userRoleResolver.IsSuperAdminAsync(userId))
        {
            return Forbid();
        }

        // 防止 IDOR：mailboxId=0 表示清空所有邮箱垃圾箱，仅超管可操作（上面已强制）
        if (mailboxId > 0 && !await HasMailboxAccess(mailboxId))
        {
            return Forbid();
        }

        var count = await _conversationService.ClearTrashByMailboxAsync(mailboxId);
        return Ok(new { count });
    }

    /// <summary>
    /// 获取会话的聊天模式消息线程
    /// </summary>
    /// <remarks>按聊天模式（时间正序）返回会话的所有消息线程，适用于实时聊天场景。</remarks>
    /// <param name="id">会话 ID</param>
    /// <returns>消息线程列表</returns>
    /// <response code="200">返回消息线程列表</response>
    [HttpGet("{id}/chat-mode")]
    public async Task<IActionResult> GetChatModeThreads(int id)
    {
        var conversation = await _conversationService.GetConversationByIdAsync(id);

        if (conversation == null)
        {
            return NotFound(new { message = _localizer["ConversationNotFound"] });
        }

        if (!await HasMailboxAccess(conversation.MailboxId))
        {
            return Forbid();
        }

        var threads = await _conversationService.GetChatModeThreadsAsync(id);
        return Ok(threads);
    }
}
