using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using NoneadCSS.Application.Common;
using NoneadCSS.Application.Configuration;
using NoneadCSS.Domain;
using NoneadCSS.Domain.Entities;
using NoneadCSS.Domain.Enums;

namespace NoneadCSS.Application.Services.WorkflowActions;

/// <summary>
/// 发送 IM 消息动作处理器：根据模板 slug 渲染消息文本，通过 IM 消息队列异步发送到客户对应的聊天渠道。
/// 仅支持 IM 渠道（WeChat/Feishu/WhatsApp/DingTalk），非 IM 渠道会直接返回 false。
/// 与 <see cref="SendEmailActionHandler"/> 职责分离：本处理器不依赖 EmailQueueService，
/// 通过 <see cref="IImMessageQueueService"/> 入队，由后台 Job 处理发送和失败重试。
/// </summary>
public class SendImMessageActionHandler : IWorkflowActionHandler
{
    private readonly IUnitOfWork _unitOfWork;
    private readonly IMailTemplateService _mailTemplateService;
    private readonly IImMessageQueueService _imMessageQueueService;
    private readonly IChatChannelMessageSender _chatChannelMessageSender;
    private readonly ISatisfactionTokenGenerator _satisfactionTokenGenerator;
    private readonly IFrontendUrlResolver _frontendUrlResolver;
    private readonly IConfiguration _configuration;
    private readonly ILogger<SendImMessageActionHandler> _logger;
    private readonly BrandOptions _brand;

    public SendImMessageActionHandler(
        IUnitOfWork unitOfWork,
        IMailTemplateService mailTemplateService,
        IImMessageQueueService imMessageQueueService,
        IChatChannelMessageSender chatChannelMessageSender,
        ISatisfactionTokenGenerator satisfactionTokenGenerator,
        IFrontendUrlResolver frontendUrlResolver,
        IConfiguration configuration,
        ILogger<SendImMessageActionHandler> logger,
        IOptions<BrandOptions> brand)
    {
        _unitOfWork = unitOfWork;
        _mailTemplateService = mailTemplateService;
        _imMessageQueueService = imMessageQueueService;
        _chatChannelMessageSender = chatChannelMessageSender;
        _satisfactionTokenGenerator = satisfactionTokenGenerator;
        _frontendUrlResolver = frontendUrlResolver;
        _configuration = configuration;
        _logger = logger;
        _brand = brand.Value;
    }

    public string ActionType => WorkflowConst.ActionSendImMessage;

    public async Task<bool> ExecuteAsync(WorkflowAction action, Conversation conversation, WorkflowActionContext context)
    {
        // 前置校验：必须是 IM 渠道
        if (!_chatChannelMessageSender.SupportsChatChannel(conversation))
        {
            _logger.LogDebug("send_im_message 动作会话 {ConversationId} 来源类型 {SourceType} 非 IM 渠道，跳过",
                conversation.Id, conversation.SourceType);
            return false;
        }

        var slug = action.Value?.Trim();
        if (string.IsNullOrWhiteSpace(slug))
        {
            _logger.LogWarning("send_im_message 动作缺少模板 slug，工作流会话 {ConversationId}", conversation.Id);
            return false;
        }

        var template = await _mailTemplateService.GetBySlugAsync(slug);
        if (template == null)
        {
            _logger.LogError("send_im_message 动作未找到模板 slug={Slug}，工作流配置错误", slug);
            return false;
        }
        if (!template.IsActive)
        {
            _logger.LogInformation("send_im_message 动作模板 slug={Slug} 已禁用，跳过发送", slug);
            return false;
        }

        // 构造模板变量（IM 场景精简版，含满意度评价链接双路径生成）
        var variables = await BuildImVariablesAsync(conversation, context.Rating);

        // 渲染模板：优先使用 BodyText（纯文本，适合 IM 消息），兜底使用 Subject
        var rendered = await _mailTemplateService.RenderTemplateFullAsync(template.Id, variables);
        var imText = !string.IsNullOrWhiteSpace(rendered.BodyText)
            ? rendered.BodyText
            : rendered.Subject;

        if (string.IsNullOrWhiteSpace(imText))
        {
            _logger.LogWarning("send_im_message 动作渲染后消息文本为空：slug={Slug}, conversation={ConversationId}",
                slug, conversation.Id);
            return false;
        }

        // 通过 IM 消息队列入队，由后台 Job 异步发送（支持失败重试，指数退避）
        var queueId = await _imMessageQueueService.EnqueueAsync(conversation, imText);
        if (queueId.HasValue)
        {
            _logger.LogInformation("send_im_message 动作入队成功：slug={Slug}, conversation={ConversationId}, sourceType={SourceType}, QueueId={QueueId}",
                slug, conversation.Id, conversation.SourceType, queueId.Value);
            return true;
        }

        _logger.LogWarning("send_im_message 动作入队失败（客户无 ChannelId 或参数无效）：slug={Slug}, conversation={ConversationId}",
            slug, conversation.Id);
        return false;
    }

    /// <summary>
    /// 构造 IM 消息模板变量字典（IM 场景精简版）。
    /// 与 SendEmailActionHandler.BuildMailVariablesAsync 相比，移除了邮件专用变量（mailbox_email、
    /// reset_url、login_url 等），保留 IM 场景必需的变量，并复用相同的满意度评价链接双路径生成逻辑。
    /// 在 satisfaction_submitted 触发的工作流中，通过 rating 参数注入满意度评价相关变量。
    /// </summary>
    private async Task<Dictionary<string, string>> BuildImVariablesAsync(Conversation conversation, SatisfactionRating? rating = null)
    {
        var frontendUrl = _frontendUrlResolver.GetFrontendUrl();
        Customer? customer = null;
        if (conversation.CustomerId.HasValue)
        {
            customer = await _unitOfWork.Customers.GetByIdAsync(conversation.CustomerId.Value);
        }
        User? assignee = null;
        if (conversation.UserId.HasValue)
        {
            assignee = await _unitOfWork.Users.GetByIdAsync(conversation.UserId.Value);
        }

        var variables = new Dictionary<string, string>
        {
            ["subject"] = conversation.Subject ?? string.Empty,
            ["conversation_id"] = conversation.Id.ToString(),
            ["conversation_url"] = $"{frontendUrl}/conversations/{conversation.Id}",
            ["customer_name"] = customer != null ? $"{customer.FirstName} {customer.LastName}".Trim() : (conversation.CustomerEmail ?? string.Empty),
            ["customer_email"] = conversation.CustomerEmail ?? string.Empty,
            ["user_name"] = assignee != null ? $"{assignee.FirstName} {assignee.LastName}".Trim() : string.Empty,
            ["assignee_name"] = assignee != null ? $"{assignee.FirstName} {assignee.LastName}".Trim() : string.Empty,
            ["created_at"] = conversation.CreatedAt.ToString("yyyy-MM-dd HH:mm"),
            ["company_name"] = _brand.CompanyName,
            ["priority"] = conversation.Priority.ToString(),
            ["satisfaction_url"] = string.Empty,
            ["rating_url"] = string.Empty,
            ["feedback_url"] = string.Empty
        };

        // 满意度评价链接（双路径生成，与 SendEmailActionHandler 保持一致）
        // - 邮件渠道：CustomerEmail 非空，用邮箱生成 token
        // - IM 渠道：CustomerEmail 为空，用 Customer.ChannelId 生成 token
        if (!string.IsNullOrWhiteSpace(conversation.CustomerEmail))
        {
            var satisfactionToken = _satisfactionTokenGenerator.GenerateToken(conversation.Id, conversation.CustomerEmail);
            var satisfactionUrl = $"{frontendUrl}/satisfaction/{satisfactionToken}";
            variables["satisfaction_url"] = satisfactionUrl;
            variables["rating_url"] = satisfactionUrl;
            variables["feedback_url"] = satisfactionUrl;
        }
        else if (customer != null && !string.IsNullOrWhiteSpace(customer.ChannelId))
        {
            var satisfactionToken = _satisfactionTokenGenerator.GenerateTokenByChannelId(conversation.Id, customer.ChannelId);
            var satisfactionUrl = $"{frontendUrl}/satisfaction/{satisfactionToken}";
            variables["satisfaction_url"] = satisfactionUrl;
            variables["rating_url"] = satisfactionUrl;
            variables["feedback_url"] = satisfactionUrl;
        }

        // 满意度评价变量（仅在 satisfaction_submitted 触发的工作流中可用）
        // 与 SendEmailActionHandler.BuildMailVariablesAsync 保持一致
        if (rating != null)
        {
            variables["satisfaction_rating"] = GetSatisfactionRatingLabel(rating.Rating);
            variables["satisfaction_rating_value"] = rating.Rating.ToString();
            variables["satisfaction_comment"] = rating.Comment ?? string.Empty;
        }

        return variables;
    }

    /// <summary>
    /// 将满意度评分数值转换为文字标签。
    /// 与 SendEmailActionHandler.GetSatisfactionRatingLabel 保持一致。
    /// </summary>
    private static string GetSatisfactionRatingLabel(int rating) => rating switch
    {
        5 => "远超期望",
        4 => "物有所值",
        3 => "中规中矩",
        2 => "略有失望",
        1 => "无法接受",
        _ => "未知"
    };
}
