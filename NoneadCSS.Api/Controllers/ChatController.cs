using NoneadCSS.Api.Authorization;
using NoneadCSS.Api.Filters;
using NoneadCSS.Application.DTOs.Requests;
using NoneadCSS.Application.DTOs.Responses;
using NoneadCSS.Application.Services;
using NoneadCSS.Domain;
using NoneadCSS.Domain.Enums;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.RateLimiting;
using System.ComponentModel.DataAnnotations;
using System.Security.Claims;

namespace NoneadCSS.Api.Controllers;

/// <summary>
/// 在线聊天控制器 - 管理聊天会话和消息
/// </summary>
[ApiController]
[Route("api/[controller]")]
[Authorize]
public class ChatController : ControllerBase
{
    private readonly IChatService _service;
    private readonly IMailboxService _mailboxService;
    private readonly IUserRoleResolver _userRoleResolver;

    public ChatController(IChatService service, IMailboxService mailboxService, IUserRoleResolver userRoleResolver)
    {
        _service = service;
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

    // ====== 客服侧 - 会话管理 ======

    [HttpGet("sessions")]
    [Authorize]
    [RequireModule("chat")]
    public async Task<ActionResult> GetSessions(
        [FromQuery] string? status = null,
        [FromQuery] int? mailboxId = null,
        [FromQuery] int? userId = null,
        [FromQuery] int? customerId = null,
        [FromQuery] string? keyword = null,
        [FromQuery] DateTime? startDate = null,
        [FromQuery] DateTime? endDate = null,
        [FromQuery] int page = 1,
        [FromQuery] int pageSize = PaginationHelper.DefaultPageSize)
    {
        // 限制分页参数上限，防止请求大量数据导致 DoS
        page = Math.Max(page, 1);
        pageSize = Math.Min(Math.Max(pageSize, 1), PaginationHelper.MaxPageSize);
        var query = new ChatSessionQuery
        {
            Status = status,
            MailboxId = mailboxId,
            UserId = userId,
            CustomerId = customerId,
            Keyword = keyword,
            StartDate = startDate,
            EndDate = endDate,
            Page = page,
            PageSize = pageSize
        };
        var result = await _service.GetSessionsAsync(query);
        return Ok(result);
    }

    [HttpGet("sessions/{id}")]
    [Authorize]
    [RequireModule("chat")]
    public async Task<ActionResult<ChatSessionDto>> GetSessionById(int id)
    {
        var result = await _service.GetSessionByIdAsync(id);
        if (result == null) return NotFound();

        if (!await HasMailboxAccess(result.MailboxId))
        {
            return Forbid();
        }

        return Ok(result);
    }

    /// <summary>
    /// 按工单 ConversationId 反查在线聊天会话（若该工单由聊天转工单生成，在工单详情页合并展示在线聊天评分）
    /// </summary>
    [HttpGet("sessions/by-conversation/{conversationId:int}")]
    [Authorize]
    public async Task<ActionResult<ChatSessionDto>> GetSessionByConversation(int conversationId)
    {
        var result = await _service.GetSessionByConversationIdAsync(conversationId);
        if (result == null) return NotFound();

        if (!await HasMailboxAccess(result.MailboxId))
        {
            return Forbid();
        }

        return Ok(result);
    }

    [HttpPost("sessions/{id}/assign")]
    [Authorize]
    [RequireModule("chat")]
    [Permission("chat.reply")]
    public async Task<ActionResult<ChatSessionDto>> AssignSession(int id, [FromBody] AssignChatSessionRequest request)
    {
        try
        {
            var session = await _service.GetSessionByIdAsync(id);
            if (session == null) return NotFound(new { message = "会话不存在" });

            if (!await HasMailboxAccess(session.MailboxId))
            {
                return Forbid();
            }

            var result = await _service.AssignSessionAsync(id, request.UserId, request.IfMatch);
            return Ok(result);
        }
        catch (InvalidOperationException)
        {
            return NotFound(new { message = "会话不存在" });
        }
    }

    [HttpPost("sessions/{id}/close")]
    [Authorize]
    [RequireModule("chat")]
    [Permission("chat.reply")]
    public async Task<ActionResult<ChatSessionDto>> CloseSession(int id, [FromBody] CloseChatSessionRequest? request = null)
    {
        try
        {
            var session = await _service.GetSessionByIdAsync(id);
            if (session == null) return NotFound(new { message = "会话不存在" });

            if (!await HasMailboxAccess(session.MailboxId))
            {
                return Forbid();
            }

            var requestRating = request?.RequestRating ?? true;
            var result = await _service.CloseSessionAsync(id, requestRating, request?.IfMatch);
            return Ok(result);
        }
        catch (InvalidOperationException)
        {
            return NotFound(new { message = "会话不存在" });
        }
    }

    [HttpGet("active-sessions")]
    [Authorize]
    [RequireModule("chat")]
    public async Task<ActionResult<List<ChatSessionDto>>> GetActiveSessions([FromQuery] int? mailboxId = null)
    {
        var result = await _service.GetActiveSessionsAsync(mailboxId);
        return Ok(result);
    }

    [HttpGet("overview")]
    [Authorize]
    [RequireModule("chat")]
    public async Task<ActionResult<ChatOverviewResponse>> GetOverview()
    {
        var result = await _service.GetOverviewAsync();
        return Ok(result);
    }

    // ====== 客服侧 - 消息管理 ======

    [HttpGet("sessions/{id}/messages")]
    [Authorize]
    [RequireModule("chat")]
    public async Task<ActionResult<List<ChatMessageDto>>> GetMessages(int id, [FromQuery] int pageSize = PaginationHelper.MaxPageSize)
    {
        // 限制返回数量上限，防止请求大量数据导致 DoS
        pageSize = Math.Min(Math.Max(pageSize, 1), PaginationHelper.MaxPageSize);

        // 跨邮箱 IDOR 防护：必须先校验当前用户对该会话所属邮箱的访问权限
        // （与 SendMessage / MarkAsRead 等其他会话端点保持一致）
        var session = await _service.GetSessionByIdAsync(id);
        if (session == null) return NotFound(new { message = "会话不存在" });

        if (!await HasMailboxAccess(session.MailboxId))
        {
            return Forbid();
        }

        var result = await _service.GetMessagesAsync(id, pageSize);
        return Ok(result);
    }

    [HttpPost("sessions/{id}/messages")]
    [Authorize]
    [RequireModule("chat")]
    [Permission("chat.reply")]
    public async Task<ActionResult<ChatMessageDto>> SendMessage(int id, [FromBody] SendChatMessageRequest request)
    {
        try
        {
            var session = await _service.GetSessionByIdAsync(id);
            if (session == null) return NotFound(new { message = "会话不存在" });

            if (!await HasMailboxAccess(session.MailboxId))
            {
                return Forbid();
            }

            // JWT 生成时使用 ClaimTypes.NameIdentifier（序列化为 nameid），不是 "id"
            var userId = int.TryParse(User.FindFirst(ClaimTypes.NameIdentifier)?.Value, out var uid) ? uid : (int?)null;
            // AuthService 中用户名使用 JwtRegisteredClaimNames.Name（序列化为 "name"）
            var userName = User.FindFirst("name")?.Value ?? "客服";
            var result = await _service.SendMessageAsync(id, request, userId, userName);
            return Ok(result);
        }
        catch (InvalidOperationException)
        {
            return BadRequest(new { message = "发送消息失败" });
        }
    }

    [HttpPost("sessions/{id}/mark-as-read")]
    [Authorize]
    [RequireModule("chat")]
    [Permission("chat.reply")]
    public async Task<ActionResult> MarkAsRead(int id, [FromBody] MarkAsReadRequest request)
    {
        var session = await _service.GetSessionByIdAsync(id);
        if (session == null) return NotFound(new { message = "会话不存在" });

        if (!await HasMailboxAccess(session.MailboxId))
        {
            return Forbid();
        }

        await _service.MarkAsReadAsync(id, request.ReaderType, request.SessionIfMatch);
        return Ok();
    }

    [HttpGet("sessions/{id}/unread-count")]
    [Authorize]
    [RequireModule("chat")]
    public async Task<ActionResult<UnreadCountResponse>> GetUnreadCount(int id, [FromQuery] string readerType)
    {
        // 跨邮箱 IDOR 防护：校验当前用户对该会话所属邮箱的访问权限
        var session = await _service.GetSessionByIdAsync(id);
        if (session == null) return NotFound(new { message = "会话不存在" });

        if (!await HasMailboxAccess(session.MailboxId))
        {
            return Forbid();
        }

        var count = await _service.GetUnreadCountAsync(id, readerType);
        return Ok(new UnreadCountResponse { Count = count });
    }

    // ====== 消息撤回/编辑/输入状态 ======

    [HttpPost("sessions/{id}/messages/{messageId}/recall")]
    [Authorize]
    [RequireModule("chat")]
    [Permission("chat.reply")]
    public async Task<ActionResult<ChatMessageDto>> RecallMessage(int id, int messageId)
    {
        try
        {
            var session = await _service.GetSessionByIdAsync(id);
            if (session == null) return NotFound(new { message = "会话不存在" });

            if (!await HasMailboxAccess(session.MailboxId))
                return Forbid();

            var userId = int.TryParse(User.FindFirst(ClaimTypes.NameIdentifier)?.Value, out var uid) ? uid : 0;
            if (userId == 0) return Unauthorized();

            var result = await _service.RecallMessageAsync(id, messageId, userId);
            return Ok(result);
        }
        catch (InvalidOperationException ex)
        {
            return BadRequest(new { message = ex.Message });
        }
    }

    [HttpPost("sessions/{id}/messages/{messageId}/recall-v2")]
    [Authorize]
    [RequireModule("chat")]
    [Permission("chat.reply")]
    public async Task<ActionResult<ChatMessageDto>> RecallMessageV2(int id, int messageId, [FromBody] RecallChatMessageRequest? request = null)
    {
        try
        {
            var session = await _service.GetSessionByIdAsync(id);
            if (session == null) return NotFound(new { message = "会话不存在" });

            if (!await HasMailboxAccess(session.MailboxId))
                return Forbid();

            var userId = int.TryParse(User.FindFirst(ClaimTypes.NameIdentifier)?.Value, out var uid) ? uid : 0;
            if (userId == 0) return Unauthorized();

            var result = await _service.RecallMessageAsync(id, messageId, userId, request?.IfMatch);
            return Ok(result);
        }
        catch (InvalidOperationException ex)
        {
            return BadRequest(new { message = ex.Message });
        }
    }

    [HttpPut("sessions/{id}/messages/{messageId}")]
    [Authorize]
    [RequireModule("chat")]
    [Permission("chat.reply")]
    public async Task<ActionResult<ChatMessageDto>> EditMessage(int id, int messageId, [FromBody] EditChatMessageRequest request)
    {
        try
        {
            var session = await _service.GetSessionByIdAsync(id);
            if (session == null) return NotFound(new { message = "会话不存在" });

            if (!await HasMailboxAccess(session.MailboxId))
                return Forbid();

            var userId = int.TryParse(User.FindFirst(ClaimTypes.NameIdentifier)?.Value, out var uid) ? uid : 0;
            if (userId == 0) return Unauthorized();

            var result = await _service.EditMessageAsync(id, messageId, request.Content, userId, request.IfMatch);
            return Ok(result);
        }
        catch (InvalidOperationException ex)
        {
            return BadRequest(new { message = ex.Message });
        }
    }

    [HttpPost("sessions/{id}/typing")]
    [Authorize]
    [RequireModule("chat")]
    [Permission("chat.reply")]
    public async Task<ActionResult> NotifyTyping(int id, [FromBody] TypingRequest request)
    {
        var session = await _service.GetSessionByIdAsync(id);
        if (session == null) return NotFound(new { message = "会话不存在" });

        if (!await HasMailboxAccess(session.MailboxId))
            return Forbid();

        var userId = int.TryParse(User.FindFirst(ClaimTypes.NameIdentifier)?.Value, out var uid) ? (int?)uid : null;
        await _service.NotifyTypingAsync(id, request.SenderType, userId);
        return Ok();
    }

    // ====== 转工单 ======

    [HttpPost("sessions/{id}/transfer")]
    [Authorize]
    [RequireModule("chat")]
    [Permission("chat.reply")]
    public async Task<ActionResult<ChatSessionDto>> TransferToTicket(int id, [FromBody] TransferToTicketRequest request)
    {
        try
        {
            var session = await _service.GetSessionByIdAsync(id);
            if (session == null) return NotFound(new { message = "会话不存在" });

            if (!await HasMailboxAccess(session.MailboxId))
            {
                return Forbid();
            }

            // JWT 生成时使用 ClaimTypes.NameIdentifier（序列化为 nameid），不是 "id"
            var userId = int.TryParse(User.FindFirst(ClaimTypes.NameIdentifier)?.Value, out var uid) ? uid : 0;
            if (userId == 0) return Unauthorized();
            var result = await _service.TransferToTicketAsync(id, request, userId);
            return Ok(result);
        }
        catch (InvalidOperationException)
        {
            return BadRequest(new { message = "转工单失败" });
        }
    }

    // ====== 软删除 / 垃圾箱 / 硬删除（仅超级管理员与客服主管） ======

    /// <summary>
    /// 软删除单个会话（移至垃圾箱）
    /// </summary>
    [HttpDelete("sessions/{id:int}")]
    [RequireModule("chat")]
    [RequireSuperAdminOrSupervisor]
    public async Task<IActionResult> DeleteSession(int id)
    {
        var ok = await _service.DeleteSessionAsync(id);
        if (!ok) return NotFound(new { message = "会话不存在" });
        return Ok(new { message = "会话已移至垃圾箱" });
    }

    /// <summary>
    /// 批量软删除会话
    /// </summary>
    [HttpPost("sessions/bulk-delete")]
    [RequireModule("chat")]
    [RequireSuperAdminOrSupervisor]
    public async Task<IActionResult> BulkDeleteSessions([FromBody] BulkDeleteChatSessionsRequest request)
    {
        if (request.Ids == null || request.Ids.Count == 0)
            return BadRequest(new { message = "未选择要删除的会话" });
        // S7: 数量上限，防超大请求体导致资源耗尽
        if (request.Ids.Count > 200)
            return BadRequest(new { message = "单次最多删除 200 个会话" });

        var ok = await _service.BulkDeleteSessionsAsync(request.Ids);
        return Ok(new { success = ok });
    }

    /// <summary>
    /// 获取垃圾箱会话列表
    /// </summary>
    [HttpGet("sessions/trash")]
    [RequireModule("chat")]
    [RequireSuperAdminOrSupervisor]
    public async Task<IActionResult> GetTrashSessions([FromQuery] int page = 1, [FromQuery] int pageSize = 20, [FromQuery] string? keyword = null)
    {
        page = Math.Max(page, 1);
        pageSize = Math.Min(Math.Max(pageSize, 1), PaginationHelper.MaxPageSize);
        var result = await _service.GetDeletedSessionsAsync(page, pageSize, keyword);
        return Ok(result);
    }

    /// <summary>
    /// 恢复垃圾箱中的会话
    /// </summary>
    [HttpPost("sessions/{id:int}/restore")]
    [RequireModule("chat")]
    [RequireSuperAdminOrSupervisor]
    public async Task<IActionResult> RestoreSession(int id)
    {
        var ok = await _service.RestoreSessionAsync(id);
        if (!ok) return NotFound(new { message = "会话不存在" });
        return Ok(new { message = "会话已恢复" });
    }

    /// <summary>
    /// 彻底删除单个会话（不可恢复）
    /// </summary>
    [HttpDelete("sessions/{id:int}/hard")]
    [RequireModule("chat")]
    [RequireSuperAdminOrSupervisor]
    public async Task<IActionResult> HardDeleteSession(int id)
    {
        var ok = await _service.HardDeleteSessionAsync(id);
        if (!ok) return NotFound(new { message = "会话不存在" });
        return Ok(new { message = "会话已彻底删除" });
    }

    /// <summary>
    /// 清空垃圾箱
    /// </summary>
    [HttpDelete("sessions/trash")]
    [RequireModule("chat")]
    [RequireSuperAdminOrSupervisor]
    public async Task<IActionResult> ClearTrash()
    {
        var count = await _service.HardDeleteAllDeletedSessionsAsync();
        return Ok(new { message = $"已清空垃圾箱，共删除 {count} 个会话", count = count });
    }

    // ====== 客户端接口（无需认证） ======

    /// <summary>
    /// 校验 sessionToken 长度至少 32 字符（防止暴力枚举），不合法直接返回 404。
    /// </summary>
    private bool IsValidSessionToken(string? sessionToken)
    {
        if (string.IsNullOrEmpty(sessionToken) || sessionToken.Length < 32)
        {
            return false;
        }
        // 仅允许字母数字（与 Guid.NewGuid().ToString("N") 输出一致）
        foreach (var c in sessionToken)
        {
            if (!char.IsLetterOrDigit(c)) return false;
        }
        return true;
    }

    [HttpPost("initiate")]
    [AllowAnonymous]
    // H-02：initiate 为会话创建写操作，使用更严格的 ChatClientStrictPolicy（10 次/分钟）
    [EnableRateLimiting("ChatClientStrictPolicy")]
    public async Task<ActionResult<InitiateChatResponse>> InitiateChat([FromBody] InitiateChatRequest request)
    {
        var customerIp = HttpContext.Connection.RemoteIpAddress?.ToString();
        var userAgent = HttpContext.Request.Headers.UserAgent.ToString();
        var result = await _service.InitiateChatAsync(request, customerIp, userAgent);
        return Ok(result);
    }

    [HttpPost("token/messages")]
    [AllowAnonymous]
    // H-02：send-message 为消息发送写操作，使用更严格的 ChatClientStrictPolicy（10 次/分钟）
    [EnableRateLimiting("ChatClientStrictPolicy")]
    public async Task<ActionResult<ChatMessageDto>> SendCustomerMessage(
        [FromHeader(Name = "X-Session-Token")] string? sessionToken, [FromBody] SendChatMessageRequest request)
    {
        // T7: sessionToken 经请求头传递（不再进 URL，防 nginx 访问日志/Referer 泄露）
        if (!IsValidSessionToken(sessionToken)) return NotFound(new { message = "会话不存在" });
        try
        {
            var result = await _service.SendCustomerMessageAsync(sessionToken, request);
            return Ok(result);
        }
        catch (InvalidOperationException)
        {
            return BadRequest(new { message = "发送消息失败" });
        }
    }

    [HttpGet("token/messages")]
    [AllowAnonymous]
    [EnableRateLimiting("ChatClientPolicy")]
    public async Task<ActionResult<List<ChatMessageDto>>> GetCustomerMessages(
        [FromHeader(Name = "X-Session-Token")] string? sessionToken, [FromQuery] int pageSize = PaginationHelper.MaxPageSize)
    {
        if (!IsValidSessionToken(sessionToken)) return NotFound(new { message = "会话不存在" });
        // 限制返回数量上限，防止请求大量数据导致 DoS
        pageSize = Math.Min(Math.Max(pageSize, 1), PaginationHelper.MaxPageSize);
        try
        {
            var result = await _service.GetCustomerMessagesAsync(sessionToken, pageSize);
            return Ok(result);
        }
        catch (InvalidOperationException)
        {
            return BadRequest(new { message = "获取消息失败" });
        }
    }

    [HttpPost("token/close")]
    [AllowAnonymous]
    [EnableRateLimiting("ChatClientPolicy")]
    public async Task<ActionResult<ChatSessionDto>> CloseSessionByToken(
        [FromHeader(Name = "X-Session-Token")] string? sessionToken)
    {
        if (!IsValidSessionToken(sessionToken)) return NotFound(new { message = "会话不存在" });
        try
        {
            var result = await _service.CloseSessionByTokenAsync(sessionToken);
            return Ok(result);
        }
        catch (InvalidOperationException)
        {
            return BadRequest(new { message = "关闭会话失败" });
        }
    }

    [HttpPost("token/rate")]
    [AllowAnonymous]
    [EnableRateLimiting("ChatClientPolicy")]
    public async Task<ActionResult<ChatSessionDto>> RateChat(
        [FromHeader(Name = "X-Session-Token")] string? sessionToken, [FromBody] RateChatRequest request)
    {
        if (!IsValidSessionToken(sessionToken)) return NotFound(new { message = "会话不存在" });
        try
        {
            var result = await _service.RateChatAsync(sessionToken, request);
            return Ok(result);
        }
        catch (InvalidOperationException)
        {
            return BadRequest(new { message = "评分失败" });
        }
    }

    [HttpPost("token/mark-as-read")]
    [AllowAnonymous]
    [EnableRateLimiting("ChatClientPolicy")]
    public async Task<ActionResult> MarkAsReadByToken(
        [FromHeader(Name = "X-Session-Token")] string? sessionToken)
    {
        if (!IsValidSessionToken(sessionToken)) return NotFound(new { message = "会话不存在" });
        var session = await _service.GetSessionByTokenAsync(sessionToken);
        if (session == null) return BadRequest(new { message = "会话不存在" });
        await _service.MarkAsReadAsync(session.Id, "customer");
        return Ok();
    }

    [HttpPost("token/typing")]
    [AllowAnonymous]
    [EnableRateLimiting("ChatClientPolicy")]
    public async Task<ActionResult> NotifyTypingByToken(
        [FromHeader(Name = "X-Session-Token")] string? sessionToken)
    {
        if (!IsValidSessionToken(sessionToken)) return NotFound(new { message = "会话不存在" });
        var session = await _service.GetSessionByTokenAsync(sessionToken);
        if (session == null) return BadRequest(new { message = "会话不存在" });
        await _service.NotifyTypingAsync(session.Id, "customer", userId: null);
        return Ok();
    }

    /// <summary>
    /// 客户侧免登录获取当前会话的完整 DTO（用于轮询状态变更，例如
    /// 客服关闭会话后，状态从 active → awaiting_rating，客户 Widget 需据此
    /// 自动切换到评分 UI，并显示评分超时倒计时）。
    /// </summary>
    [HttpGet("token/session")]
    [AllowAnonymous]
    [EnableRateLimiting("ChatClientPolicy")]
    public async Task<ActionResult<ChatSessionDto>> GetCustomerSessionByToken(
        [FromHeader(Name = "X-Session-Token")] string? sessionToken)
    {
        if (!IsValidSessionToken(sessionToken)) return NotFound(new { message = "会话不存在" });
        try
        {
            var session = await _service.GetSessionByTokenAsync(sessionToken);
            return Ok(session);
        }
        catch (InvalidOperationException)
        {
            return NotFound(new { message = "会话不存在或已过期" });
        }
    }
}

/// <summary>
/// 批量软删除在线聊天会话请求
/// </summary>
public class BulkDeleteChatSessionsRequest
{
    /// <summary>会话 ID 列表（S7: 单次最多 200 个，防超大请求体）</summary>
    [MaxLength(200)]
    public List<int> Ids { get; set; } = new();
}

