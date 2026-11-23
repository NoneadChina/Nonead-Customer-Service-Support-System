using NoneadCSS.Application.Mappings;
using NoneadCSS.Application.Templates;
using NoneadCSS.Domain.Entities;
using NoneadCSS.Domain;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using NoneadCSS.Application.Configuration;

namespace NoneadCSS.Application.Services.WorkflowActions;

/// <summary>
/// 工作流动作：send_chat_message
/// 在 ChatSession 的消息流内插入一条 system（系统）消息，并实时推送到客服端和在线聊天 Widget。
/// 仅对 chat_close_requested / chat_satisfaction_submitted 等 ChatSession 级 Trigger 生效；
/// 对于普通 Conversation 级工作流（如 conversation_created），该动作会被跳过（返回 false）。
///
/// 占位符（从 WorkflowActionContext.ChatSession 上下文解析，与 chat-session-closed 邮件模板变量保持一致）：
///   - {{customer_name}}   : 客户姓名（session.CustomerName 或 customer.FirstName + LastName）
///   - {{customer_email}}  : 客户邮箱
///   - {{customer_phone}}  : 客户手机
///   - {{company_name}}    : 公司名（IConfiguration:CompanyName，缺省"拓德科技"）
///   - {{chat_subject}}    : 会话主题
///   - {{agent_name}}      : 当前接待客服姓名
///   - {{chat_session_id}} : ChatSession.Id
///   - {{rating}}          : 客户评分 1-5（仅 chat_satisfaction_submitted 时会有值）
///   - {{rating_stars}}    : 客户评分对应"★"字符（例如 5 分 → "★★★★★"；未评分 → ""）
///   - {{rating_comment}}  : 客户评论文本（chat_satisfaction_submitted 时可能为空）
///   - {{timeout_minutes}} : 未评分超时分钟数（当前固定为 3；仅 chat_close_requested 时有用）
/// </summary>
public class SendChatMessageActionHandler : IWorkflowActionHandler
{
    private readonly IUnitOfWork _unitOfWork;
    private readonly INotificationHubService _notificationHub;
    private readonly IConfiguration _configuration;
    private readonly ILogger<SendChatMessageActionHandler> _logger;
    private readonly ITemplateRenderer _renderer;
    private readonly BrandOptions _brand;

    public string ActionType => WorkflowConst.ActionSendChatMessage;

    public SendChatMessageActionHandler(
        IUnitOfWork unitOfWork,
        INotificationHubService notificationHub,
        IConfiguration configuration,
        ILogger<SendChatMessageActionHandler> logger,
        ITemplateRenderer renderer,
        IOptions<BrandOptions> brand)
    {
        _unitOfWork = unitOfWork;
        _notificationHub = notificationHub;
        _configuration = configuration;
        _logger = logger;
        _renderer = renderer;
        _brand = brand.Value;
    }

    public async Task<bool> ExecuteAsync(WorkflowAction action, Conversation conversation, WorkflowActionContext context)
    {
        // ChatSession 上下文为 null 时（普通 Conversation 级工作流调用）跳过
        if (context.ChatSession == null)
        {
            _logger.LogDebug("SendChatMessageActionHandler 跳过（无 ChatSession 上下文，普通 Conversation 级 Trigger）");
            return false;
        }

        var session = context.ChatSession;
        if (string.IsNullOrWhiteSpace(action.Value))
        {
            _logger.LogWarning("SendChatMessageActionHandler 动作 Value 为空，跳过发送 system 消息（SessionId={SessionId}）",
                session.Id);
            return false;
        }

        var variables = await BuildVariablesAsync(session, context);
        var finalContent = _renderer.Render(action.Value, variables, new RenderOptions { HtmlEncode = false }).Trim();
        if (string.IsNullOrWhiteSpace(finalContent))
        {
            _logger.LogWarning("SendChatMessageActionHandler 渲染后内容为空，跳过插入（SessionId={SessionId}, Template={Template}）",
                session.Id, action.Value);
            return false;
        }

        var now = DateTime.UtcNow;
        var message = new ChatMessage
        {
            SessionId = session.Id,
            SenderType = "system",
            MessageType = "system",
            Content = finalContent,
            SenderName = "system",
            IsRead = true,
            CreatedAt = now
        };
        await _unitOfWork.ChatMessages.AddAsync(message);
        await _unitOfWork.SaveChangesAsync();

        // ========== 实时推送（payload 与 ChatService.SendMessageAsync 保持完全一致：RealtimeChat + sessionId + message + timestamp） ==========
        var dto = message.ToDto();
        var payload = new { sessionId = session.Id, message = dto, timestamp = now };
        try
        {
            // 推给客服侧（接待客服 UserId；若尚未分配客服则不推送客服侧）
            if (session.UserId.HasValue)
            {
                await _notificationHub.NotifyAgentChatAsync(session.UserId.Value, "RealtimeChat", payload);
            }
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "SendChatMessageActionHandler 推送客服侧失败：SessionId={SessionId}, MessageId={MessageId}",
                session.Id, message.Id);
        }

        try
        {
            // 推给 Widget 侧（客户）：通过 ChatHub 的 sessionId 通道
            await _notificationHub.SendToChatSessionAsync(session.Id, "RealtimeChat", payload);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "SendChatMessageActionHandler 推送 Widget 失败：SessionId={SessionId}, MessageId={MessageId}",
                session.Id, message.Id);
        }

        _logger.LogInformation("在线聊天系统工作流插入 system 消息：SessionId={SessionId}, MessageId={MessageId}, ActionType={ActionType}",
            session.Id, message.Id, action.Type);
        return true;
    }

    private async Task<Dictionary<string, string>> BuildVariablesAsync(ChatSession session, WorkflowActionContext context)
    {
        var customerName = !string.IsNullOrWhiteSpace(session.CustomerName) ? session.CustomerName.Trim() : string.Empty;
        if (string.IsNullOrEmpty(customerName) && session.CustomerId.HasValue)
        {
            var customer = await _unitOfWork.Customers.GetByIdAsync(session.CustomerId.Value);
            if (customer != null)
                customerName = $"{customer.FirstName} {customer.LastName}".Trim();
        }

        string agentName = string.Empty;
        if (session.UserId.HasValue)
        {
            var user = await _unitOfWork.Users.GetByIdAsync(session.UserId.Value);
            if (user != null)
                agentName = $"{user.FirstName} {user.LastName}".Trim();
        }

        int? rating = context.ChatRating ?? session.Rating;
        string ratingText = rating.HasValue ? rating.Value.ToString() : string.Empty;
        string stars = rating.HasValue ? new string('★', Math.Clamp(rating.Value, 0, 5)) : string.Empty;
        string ratingLabel = rating.HasValue ? GetRatingLabel(rating.Value) : string.Empty;
        string ratingComment = string.IsNullOrWhiteSpace(context.ChatRatingComment)
            ? (session.RatingComment ?? string.Empty)
            : context.ChatRatingComment;

        var vars = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase)
        {
            ["customer_name"] = customerName,
            ["customer_email"] = session.CustomerEmail ?? string.Empty,
            ["customer_phone"] = session.CustomerPhone ?? string.Empty,
            ["company_name"] = _brand.CompanyName,
            ["chat_subject"] = session.Subject ?? string.Empty,
            ["agent_name"] = agentName,
            ["chat_session_id"] = session.Id.ToString(),
            ["rating"] = ratingText,
            ["rating_stars"] = stars,
            ["rating_label"] = ratingLabel,
            ["rating_comment"] = ratingComment,
            ["timeout_minutes"] = ((int)ChatService.RatingWaitTimeout.TotalMinutes).ToString()
        };
        return vars;
    }

    /// <summary>
    /// 将满意度评分数值转换为文字标签
    /// </summary>
    private static string GetRatingLabel(int rating) => rating switch
    {
        5 => "远超期望",
        4 => "物有所值",
        3 => "中规中矩",
        2 => "略有失望",
        1 => "无法接受",
        _ => "未知"
    };
}
