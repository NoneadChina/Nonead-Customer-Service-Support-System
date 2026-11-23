using System.Text.Json;
using NoneadCSS.Application.Common;
using NoneadCSS.Application.Configuration;
using NoneadCSS.Application.DTOs.Responses;
using NoneadCSS.Application.Mappings;
using NoneadCSS.Application.Services.WorkflowActions;
using NoneadCSS.Domain;
using NoneadCSS.Domain.Entities;
using NoneadCSS.Domain.Enums;
using NoneadCSS.Domain.Events;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

namespace NoneadCSS.Application.Services;

public static class WorkflowConst
{
    public const string TriggerNewConversation = "new_conversation";
    public const string TriggerNewReply = "new_reply";
    public const string TriggerAgentReplied = "agent_replied";
    public const string TriggerStatusChanged = "status_changed";
    public const string TriggerAssigneeChanged = "assignee_changed";
    public const string TriggerConversationClosed = "conversation_closed";
    public const string TriggerConversationReopened = "conversation_reopened";
    public const string TriggerTagAdded = "tag_added";
    public const string TriggerTagRemoved = "tag_removed";
    public const string TriggerSatisfactionSubmitted = "satisfaction_submitted";
    public const string TriggerNoteAdded = "note_added";
    public const string TriggerPriorityChanged = "priority_changed";
    /// <summary>
    /// 在线聊天（Widget 渠道）客服点"关闭会话"时触发：
    ///   → 此时会话尚未真关闭，而是进入 awaiting_rating 等待客户评分。
    /// 系统工作流"[聊天]关闭满意度消息"用此触发器，自动向客户侧插入"感谢 + 满意度调查"的 system message。
    /// </summary>
    public const string TriggerChatCloseRequested = "chat_close_requested";
    /// <summary>
    /// 在线聊天会话客户提交满意度评分（1-5 分 + 可选评论）时触发。
    /// 系统工作流"[聊天]满意度评价感谢消息"用此触发器，自动向会话插入一条"感谢您的评价"的 system message。
    /// </summary>
    public const string TriggerChatSatisfactionSubmitted = "chat_satisfaction_submitted";

    public static string GetTriggerLabel(string trigger) => trigger switch
    {
        TriggerNewConversation => "新会话创建",
        TriggerNewReply => "收到新回复",
        TriggerAgentReplied => "客服回复",
        TriggerStatusChanged => "状态变更",
        TriggerAssigneeChanged => "分配变更",
        TriggerConversationClosed => "会话关闭",
        TriggerConversationReopened => "会话重新打开",
        TriggerTagAdded => "标签添加",
        TriggerTagRemoved => "标签移除",
        TriggerSatisfactionSubmitted => "满意度评价提交",
        TriggerNoteAdded => "添加内部备注",
        TriggerPriorityChanged => "优先级变更",
        TriggerChatCloseRequested => "在线聊天-客服发起关闭（等待评分）",
        TriggerChatSatisfactionSubmitted => "在线聊天-客户提交评分",
        _ => trigger
    };

    public const string ActionSendEmail = "send_email";
    public const string ActionSendImMessage = "send_im_message";
    public const string ActionConversationProcess = "conversation_process";
    public const string ActionTriggerWebhook = "trigger_webhook";
    public const string ActionExecuteMacro = "execute_macro";
    /// <summary>
    /// 在线聊天（Chat/Widget 渠道）会话流内插入一条系统消息（SenderType=system, MessageType=system）。
    /// Workflow ActionJson 的 value = 聊天内消息内容模板的 MailTemplate.Slug
    ///   （例如 chat-close-requested = 关闭满意度请求、chat-satisfaction-thanks = 评分后感谢）。
    /// BodyText 用作最终内容，{{变量}} 占位符由 Workflow 执行器替换。
    /// </summary>
    public const string ActionSendChatMessage = "send_chat_message";

    public static string GetActionLabel(string type) => type switch
    {
        "set_status" => "设置状态",
        "assign_user" => "分配客服",
        "add_tag" => "添加标签",
        "remove_tag" => "移除标签",
        "send_reply" => "发送回复",
        "set_priority" => "设置优先级",
        "forward" => "转发会话",
        ActionSendEmail => "发送邮件（自定义模板）",
        ActionSendImMessage => "发送 IM 消息（自定义模板）",
        ActionConversationProcess => "会话处理",
        ActionTriggerWebhook => "触发 Webhook",
        ActionExecuteMacro => "执行宏",
        ActionSendChatMessage => "在线聊天内发送系统消息（自定义模板）",
        _ => type
    };

    public static string GetFieldLabel(string field) => field switch
    {
        "subject" => "主题",
        "from_email" => "发件人邮箱",
        "to_email" => "收件人邮箱",
        "body" => "邮件正文",
        "customer_name" => "客户姓名",
        "status" => "会话状态",
        "priority" => "优先级",
        "tag" => "标签",
        "mailbox" => "所属邮箱",
        "assignee" => "分配客服",
        "satisfaction_rating" => "满意度评分",
        "satisfaction_comment" => "满意度评价内容",
        "source_type" => "来源渠道",
        _ => field
    };

    public static string GetOperatorLabel(string op) => op switch
    {
        "contains" => "包含",
        "not_contains" => "不包含",
        "equals" => "等于",
        "not_equals" => "不等于",
        "starts_with" => "以...开头",
        "ends_with" => "以...结尾",
        "regex" => "正则匹配",
        _ => op
    };
}

public class WorkflowService : IWorkflowService
{
    private readonly IUnitOfWork _unitOfWork;
    private readonly ILogger<WorkflowService> _logger;
    private readonly IFolderService _folderService;
    private readonly INotificationPublisher _notificationPublisher;
    private readonly IWorkflowActionHandlerProvider _handlerProvider;
    private readonly IServiceProvider _serviceProvider;
    private readonly BrandOptions _brand;

    public WorkflowService(
        IUnitOfWork unitOfWork,
        ILogger<WorkflowService> logger,
        IFolderService folderService,
        INotificationPublisher notificationPublisher,
        IWorkflowActionHandlerProvider handlerProvider,
        IServiceProvider serviceProvider,
        IOptions<BrandOptions> brand)
    {
        _unitOfWork = unitOfWork;
        _logger = logger;
        _folderService = folderService;
        _notificationPublisher = notificationPublisher;
        _handlerProvider = handlerProvider;
        _serviceProvider = serviceProvider;
        _brand = brand.Value;
    }

    public async Task<(IEnumerable<WorkflowDto> Items, int Total)> GetListAsync(
        int page, int pageSize, int? mailboxId = null, bool? isActive = null,
        string? triggerType = null, string? keyword = null)
    {
        var (items, total) = await _unitOfWork.Workflows.GetPagedAsync(page, pageSize, mailboxId, isActive, triggerType, keyword);
        return (items.Select(ToDto), total);
    }

    public async Task<WorkflowDto?> GetByIdAsync(int id)
    {
        var entity = await _unitOfWork.Workflows.GetByIdAsync(id);
        return entity == null ? null : ToDto(entity);
    }

    public async Task<WorkflowDto> CreateAsync(CreateWorkflowRequest request)
    {
        var entity = new Workflow
        {
            Name = request.Name,
            Description = request.Description,
            MailboxId = request.MailboxId == 0 ? null : request.MailboxId,
            IsActive = request.IsActive,
            SortOrder = request.SortOrder,
            TriggerType = request.TriggerType,
            ConditionsJson = SerializeConditions(request.Conditions),
            ActionsJson = SerializeActions(request.Actions),
            CreatedAt = DateTime.UtcNow,
            UpdatedAt = DateTime.UtcNow
        };

        await _unitOfWork.Workflows.AddAsync(entity);
        await _unitOfWork.SaveChangesAsync();
        return ToDto(entity);
    }

    public async Task<WorkflowDto?> UpdateAsync(int id, UpdateWorkflowRequest request)
    {
        var entity = await _unitOfWork.Workflows.GetByIdTrackedAsync(id);
        if (entity == null) return null;

        entity.Name = request.Name;
        entity.Description = request.Description;
        entity.MailboxId = request.MailboxId == 0 ? null : request.MailboxId;
        entity.IsActive = request.IsActive;
        entity.SortOrder = request.SortOrder;
        entity.TriggerType = request.TriggerType;
        entity.ConditionsJson = SerializeConditions(request.Conditions);
        entity.ActionsJson = SerializeActions(request.Actions);
        entity.UpdatedAt = DateTime.UtcNow;

        await _unitOfWork.SaveChangesAsync();
        return ToDto(entity);
    }

    public async Task<bool> DeleteAsync(int id)
    {
        var entity = await _unitOfWork.Workflows.GetByIdTrackedAsync(id);
        if (entity == null) return false;
        if (entity.IsSystem)
            throw new ArgumentException("系统内置工作流不可删除");

        entity.SoftDelete();
        await _unitOfWork.SaveChangesAsync();
        return true;
    }

    public async Task<(IEnumerable<WorkflowDto> Items, int Total)> GetDeletedListAsync(
        int page, int pageSize, int? mailboxId = null, string? keyword = null)
    {
        var (items, total) = await _unitOfWork.Workflows.GetDeletedPagedAsync(page, pageSize, mailboxId, keyword);
        return (items.Select(ToDto), total);
    }

    public async Task<int> GetDeletedCountAsync(int? mailboxId = null)
    {
        return await _unitOfWork.Workflows.GetDeletedCountAsync(mailboxId);
    }

    public async Task<bool> RestoreAsync(int id)
    {
        var entity = await _unitOfWork.Workflows.GetDeletedByIdAsync(id);
        if (entity == null) return false;
        if (entity.IsSystem)
            throw new ArgumentException("系统内置工作流不需要恢复");

        entity.Restore();
        await _unitOfWork.SaveChangesAsync();
        return true;
    }

    public async Task<bool> HardDeleteAsync(int id)
    {
        var entity = await _unitOfWork.Workflows.GetDeletedByIdAsync(id);
        if (entity == null) return false;
        if (entity.IsSystem)
            throw new ArgumentException("系统内置工作流不可删除");

        await _unitOfWork.Workflows.HardDeleteByIdAsync(id);
        await _unitOfWork.SaveChangesAsync();
        return true;
    }

    public async Task<int> ClearTrashAsync(int? mailboxId = null)
    {
        return await _unitOfWork.Workflows.HardDeleteAllDeletedAsync(mailboxId);
    }

    public async Task<bool> ToggleActiveAsync(int id)
    {
        var entity = await _unitOfWork.Workflows.GetQueryable(false)
            .FirstOrDefaultAsync(w => w.Id == id && w.DeletedAt == null);
        if (entity == null) return false;

        // M-007: 系统内置工作流不可禁用
        if (entity.IsSystem)
            throw new ArgumentException("系统内置工作流不可禁用");

        entity.IsActive = !entity.IsActive;
        entity.UpdatedAt = DateTime.UtcNow;
        await _unitOfWork.SaveChangesAsync();
        return true;
    }

    public async Task<WorkflowOverviewResponse> GetOverviewAsync()
    {
        var stats = await _unitOfWork.Workflows.GetStatsAsync();
        var (recent, _) = await _unitOfWork.Workflows.GetPagedAsync(1, 5);
        var (recentLogs, _) = await _unitOfWork.Workflows.GetExecutionLogsPagedAsync(1, 10);

        return new WorkflowOverviewResponse
        {
            Stats = stats,
            RecentWorkflows = recent.Select(ToDto).ToList(),
            RecentExecutions = recentLogs.Select(ToLogDto).ToList(),
            TriggerOptions = new Dictionary<string, string>
            {
                { WorkflowConst.TriggerNewConversation, WorkflowConst.GetTriggerLabel(WorkflowConst.TriggerNewConversation) },
                { WorkflowConst.TriggerNewReply, WorkflowConst.GetTriggerLabel(WorkflowConst.TriggerNewReply) },
                { WorkflowConst.TriggerStatusChanged, WorkflowConst.GetTriggerLabel(WorkflowConst.TriggerStatusChanged) },
                { WorkflowConst.TriggerAssigneeChanged, WorkflowConst.GetTriggerLabel(WorkflowConst.TriggerAssigneeChanged) },
                { WorkflowConst.TriggerConversationClosed, WorkflowConst.GetTriggerLabel(WorkflowConst.TriggerConversationClosed) },
                { WorkflowConst.TriggerConversationReopened, WorkflowConst.GetTriggerLabel(WorkflowConst.TriggerConversationReopened) },
                { WorkflowConst.TriggerTagAdded, WorkflowConst.GetTriggerLabel(WorkflowConst.TriggerTagAdded) },
                { WorkflowConst.TriggerTagRemoved, WorkflowConst.GetTriggerLabel(WorkflowConst.TriggerTagRemoved) },
                { WorkflowConst.TriggerSatisfactionSubmitted, WorkflowConst.GetTriggerLabel(WorkflowConst.TriggerSatisfactionSubmitted) },
                { WorkflowConst.TriggerNoteAdded, WorkflowConst.GetTriggerLabel(WorkflowConst.TriggerNoteAdded) },
                { WorkflowConst.TriggerPriorityChanged, WorkflowConst.GetTriggerLabel(WorkflowConst.TriggerPriorityChanged) },
                { WorkflowConst.TriggerChatCloseRequested, WorkflowConst.GetTriggerLabel(WorkflowConst.TriggerChatCloseRequested) },
                { WorkflowConst.TriggerChatSatisfactionSubmitted, WorkflowConst.GetTriggerLabel(WorkflowConst.TriggerChatSatisfactionSubmitted) }
            },
            FieldOptions = new Dictionary<string, string>
            {
                { "subject", WorkflowConst.GetFieldLabel("subject") },
                { "from_email", WorkflowConst.GetFieldLabel("from_email") },
                { "to_email", WorkflowConst.GetFieldLabel("to_email") },
                { "body", WorkflowConst.GetFieldLabel("body") },
                { "customer_name", WorkflowConst.GetFieldLabel("customer_name") },
                { "status", WorkflowConst.GetFieldLabel("status") },
                { "priority", WorkflowConst.GetFieldLabel("priority") },
                { "tag", WorkflowConst.GetFieldLabel("tag") },
                { "mailbox", WorkflowConst.GetFieldLabel("mailbox") },
                { "assignee", WorkflowConst.GetFieldLabel("assignee") },
                { "satisfaction_rating", WorkflowConst.GetFieldLabel("satisfaction_rating") },
                { "satisfaction_comment", WorkflowConst.GetFieldLabel("satisfaction_comment") },
                { "source_type", WorkflowConst.GetFieldLabel("source_type") }
            },
            OperatorOptions = new Dictionary<string, string>
            {
                { "contains", WorkflowConst.GetOperatorLabel("contains") },
                { "not_contains", WorkflowConst.GetOperatorLabel("not_contains") },
                { "equals", WorkflowConst.GetOperatorLabel("equals") },
                { "not_equals", WorkflowConst.GetOperatorLabel("not_equals") },
                { "starts_with", WorkflowConst.GetOperatorLabel("starts_with") },
                { "ends_with", WorkflowConst.GetOperatorLabel("ends_with") },
                { "regex", WorkflowConst.GetOperatorLabel("regex") }
            },
            ActionTypeOptions = new Dictionary<string, string>
            {
                { "set_status", WorkflowConst.GetActionLabel("set_status") },
                { "assign_user", WorkflowConst.GetActionLabel("assign_user") },
                { "add_tag", WorkflowConst.GetActionLabel("add_tag") },
                { "remove_tag", WorkflowConst.GetActionLabel("remove_tag") },
                { "send_reply", WorkflowConst.GetActionLabel("send_reply") },
                { "set_priority", WorkflowConst.GetActionLabel("set_priority") },
                { "forward", WorkflowConst.GetActionLabel("forward") },
                { WorkflowConst.ActionSendEmail, WorkflowConst.GetActionLabel(WorkflowConst.ActionSendEmail) },
                { WorkflowConst.ActionSendImMessage, WorkflowConst.GetActionLabel(WorkflowConst.ActionSendImMessage) },
                { WorkflowConst.ActionConversationProcess, WorkflowConst.GetActionLabel(WorkflowConst.ActionConversationProcess) },
                { WorkflowConst.ActionTriggerWebhook, WorkflowConst.GetActionLabel(WorkflowConst.ActionTriggerWebhook) },
                { WorkflowConst.ActionExecuteMacro, WorkflowConst.GetActionLabel(WorkflowConst.ActionExecuteMacro) },
                { WorkflowConst.ActionSendChatMessage, WorkflowConst.GetActionLabel(WorkflowConst.ActionSendChatMessage) }
            }
        };
    }

    public async Task<(IEnumerable<WorkflowExecutionLogDto> Items, int Total)> GetExecutionLogsAsync(
        int page, int pageSize, int? workflowId = null, int? conversationId = null,
        string? status = null, DateTime? startDate = null, DateTime? endDate = null)
    {
        var (items, total) = await _unitOfWork.Workflows.GetExecutionLogsPagedAsync(
            page, pageSize, workflowId, conversationId, status, startDate, endDate);
        return (items.Select(ToLogDto), total);
    }

    public async Task<WorkflowTestResponse> TestAsync(int workflowId, WorkflowTestRequest request)
    {
        var entity = await _unitOfWork.Workflows.GetByIdAsync(workflowId);
        if (entity == null)
        {
            return new WorkflowTestResponse();
        }

        var context = new WorkflowEvaluationContext
        {
            Subject = request.Subject ?? string.Empty,
            FromEmail = request.FromEmail ?? string.Empty,
            ToEmail = request.ToEmail ?? string.Empty,
            Body = request.Body ?? string.Empty,
            CustomerName = request.CustomerName ?? string.Empty,
            Status = request.Status ?? string.Empty,
            Priority = request.Priority ?? string.Empty,
            Mailbox = request.Mailbox ?? string.Empty,
            Assignee = request.Assignee ?? string.Empty,
            SatisfactionRating = request.SatisfactionRating ?? string.Empty,
            SatisfactionComment = request.SatisfactionComment ?? string.Empty,
            Tags = string.IsNullOrEmpty(request.Tags)
                ? new List<string>()
                : request.Tags.Split(',', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries).ToList()
        };

        var matched = entity.EvaluateConditions(context, out var matchedConditions);
        var actions = entity.DeserializeActions().Select(ToActionDto).ToList();

        // 构建动作预览详情
        var actionPreviews = actions.Select(a => new WorkflowActionPreview
        {
            Type = a.Type,
            TypeLabel = WorkflowConst.GetActionLabel(a.Type),
            Value = a.Value,
            Extra = a.Extra,
            Description = BuildActionDescription(a)
        }).ToList();

        // T000041: 规则匹配成功后，send_email 动作实际执行发送测试邮件，其他动作仅预览
        var actionResults = new List<WorkflowTestActionResult>();
        if (matched)
        {
            foreach (var action in actions)
            {
                var result = new WorkflowTestActionResult
                {
                    Type = action.Type,
                    TypeLabel = WorkflowConst.GetActionLabel(action.Type),
                    Executed = false,
                    Success = false,
                    Message = "仅预览（测试模式不执行此动作）"
                };

                if (action.Type == WorkflowConst.ActionSendEmail)
                {
                    result.Executed = true;
                    try
                    {
                        var (success, message) = await ExecuteTestSendEmailAsync(action, entity, request);
                        result.Success = success;
                        result.Message = message;
                    }
                    catch (Exception ex)
                    {
                        _logger.LogError(ex, "测试规则 send_email 动作执行失败，工作流 ID={WorkflowId}", workflowId);
                        result.Success = false;
                        result.Message = $"发送失败：{ex.Message}";
                    }
                }
                else if (action.Type == WorkflowConst.ActionExecuteMacro)
                {
                    // T000053：测试模式真实执行宏（沙箱回滚，不持久化）。
                    result.Executed = true;
                    try
                    {
                        var (success, message) = await ExecuteTestMacroAsync(action, entity, request);
                        result.Success = success;
                        result.Message = message;
                    }
                    catch (Exception ex)
                    {
                        _logger.LogError(ex, "测试规则 execute_macro 动作执行失败，工作流 ID={WorkflowId}", workflowId);
                        result.Success = false;
                        result.Message = $"执行失败：{ex.Message}";
                    }
                }

                actionResults.Add(result);
            }
        }

        return new WorkflowTestResponse
        {
            Matched = matched,
            MatchedConditions = matchedConditions,
            ActionsToExecute = actions.Select(a => $"{WorkflowConst.GetActionLabel(a.Type)}: {a.Value ?? "-"}").ToList(),
            ActionPreviews = actionPreviews,
            ActionResults = actionResults
        };
    }

    /// <summary>
    /// 测试模式下实际执行 send_email 动作：使用测试输入数据构建虚拟会话上下文，
    /// 渲染邮件模板并入队发送。不持久化任何数据，仅真实发送邮件（T000041）。
    /// </summary>
    private async Task<(bool Success, string Message)> ExecuteTestSendEmailAsync(
        WorkflowActionDto action, Workflow workflow, WorkflowTestRequest request)
    {
        // ⚠️ 安全说明（M-F2）：测试模式会真实发送邮件到 request.FromEmail 或 action.Extra 指定的收件人，
        // 未配置收件人域名白名单。当前仅限后台运营人员触发，且测试输入由操作者填写，风险可控。
        // 若开放给非可信用户，应增加收件人域名校验（仅允许内部域名或当前登录用户邮箱）。
        _logger.LogWarning("工作流测试发送邮件将被真实投递：WorkflowId={WorkflowId}, FromEmail={FromEmail}, Extra={Extra}",
            workflow.Id, request.FromEmail, action.Extra);

        var slug = action.Value?.Trim();
        if (string.IsNullOrWhiteSpace(slug))
        {
            return (false, "缺少模板 slug");
        }

        var mailTemplateService = _serviceProvider.GetRequiredService<IMailTemplateService>();
        var emailQueueService = _serviceProvider.GetRequiredService<IEmailQueueService>();
        var configuration = _serviceProvider.GetRequiredService<IConfiguration>();

        var template = await mailTemplateService.GetBySlugAsync(slug);
        if (template == null)
        {
            return (false, $"未找到模板 slug={slug}");
        }
        if (!template.IsActive)
        {
            return (false, $"模板「{slug}」已禁用");
        }

        // 解析邮箱 ID：优先使用工作流绑定的邮箱，其次请求指定的邮箱
        var mailboxId = workflow.MailboxId ?? request.MailboxId;
        if (mailboxId <= 0)
        {
            // 工作流未绑定特定邮箱时，取第一个可用邮箱作为发件邮箱
            var anyMailbox = await _unitOfWork.Mailboxes.GetQueryable()
                .FirstOrDefaultAsync(m => m.IsActive);
            if (anyMailbox == null)
            {
                return (false, "未找到可用邮箱，请先在邮箱管理中配置邮箱");
            }
            mailboxId = anyMailbox.Id;
        }

        var frontendUrl = configuration["App:FrontendUrl"] ?? "http://localhost:5173";

        // 构建测试变量（使用测试输入数据填充，不查询数据库）
        var variables = new Dictionary<string, string>
        {
            ["subject"] = request.Subject ?? string.Empty,
            ["conversation_id"] = "测试",
            ["conversation_url"] = $"{frontendUrl}/conversations",
            ["customer_name"] = request.CustomerName ?? request.FromEmail ?? string.Empty,
            ["customer_email"] = request.FromEmail ?? string.Empty,
            ["user_name"] = string.Empty,
            ["user_email"] = string.Empty,
            ["mailbox_name"] = string.Empty,
            ["mailbox_email"] = string.Empty,
            ["message_body"] = request.Body ?? string.Empty,
            ["first_message_body"] = request.Body ?? string.Empty,
            ["created_at"] = DateTime.UtcNow.ToString("yyyy-MM-dd HH:mm"),
            ["company_name"] = _brand.CompanyName,
            ["login_url"] = $"{frontendUrl}/login",
            ["reset_password_url"] = $"{frontendUrl}/reset-password",
            ["activation_url"] = $"{frontendUrl}/activate",
            ["received_time"] = DateTime.UtcNow.ToString("yyyy-MM-dd HH:mm"),
            ["priority"] = "Normal",
            ["waiting_time"] = "0 分钟",
            ["timeout_minutes"] = "0",
            ["last_reply_time"] = string.Empty,
            ["escalation_reason"] = string.Empty,
            ["escalated_by"] = string.Empty,
            ["escalated_at"] = string.Empty,
            ["replier_name"] = string.Empty,
            ["assignee_name"] = string.Empty,
            ["assigner_name"] = string.Empty,
            ["closer_name"] = string.Empty,
            ["updater_name"] = string.Empty,
            ["update_content"] = string.Empty,
            ["mentioner_name"] = string.Empty,
            ["rating_url"] = string.Empty,
            ["feedback_url"] = string.Empty,
            ["satisfaction_url"] = string.Empty,
            ["sla_response_time"] = string.Empty,
            ["user_role"] = string.Empty,
            ["reset_url"] = $"{frontendUrl}/reset-password",
            ["expire_time"] = "24小时",
            ["total_count"] = string.Empty,
            ["success_count"] = string.Empty,
            ["failed_count"] = string.Empty,
            ["error_report_url"] = string.Empty
        };

        // 渲染模板
        var rendered = await mailTemplateService.RenderTemplateFullAsync(template.Id, variables);

        // 解析收件人：测试模式下优先使用 action.Extra 中的邮箱地址，
        // 若为 customer/assignee 关键字或为空，则使用测试输入的 fromEmail 作为收件人
        // （T000103：assignee 无分配客服、白名单过滤、空收件人均会兜底到操作者发件人邮箱，绝不裸失败）
        var (recipients, recipientNote) = ResolveTestRecipients(action.Extra, request.FromEmail);
        if (recipients.Count == 0)
        {
            return (false, "未解析到有效收件人，请在测试输入中填写发件人邮箱");
        }

        // 入队发送
        var sendSubject = rendered.Subject;
        if (!string.IsNullOrEmpty(sendSubject) && !sendSubject.Contains("[测试]"))
        {
            sendSubject = $"[测试] {sendSubject}";
        }

        foreach (var to in recipients)
        {
            await emailQueueService.EnqueueEmailAsync(
                mailboxId: mailboxId,
                to: to,
                subject: sendSubject,
                body: rendered.BodyHtml,
                attachmentIds: null,
                cc: null,
                bcc: null,
                threadId: null,
                inReplyTo: null,
                references: null,
                customerId: null);
        }

        _logger.LogInformation("测试规则 send_email 动作完成：slug={Slug}, 收件人={Recipients}, 工作流={WorkflowId}",
            slug, string.Join(",", recipients), workflow.Id);

        var successMessage = $"已发送测试邮件到：{string.Join(", ", recipients)}";
        if (!string.IsNullOrEmpty(recipientNote))
        {
            successMessage += $"（{recipientNote}）";
        }
        return (true, successMessage);
    }

    /// <summary>
    /// T000053：测试模式下真实执行"执行宏"动作（沙箱回滚，不持久化任何副作用）。
    /// 背景：原"测试规则"仅真实执行 send_email，其它动作（含 execute_macro）一律返回
    /// "仅预览（测试模式不执行此动作）"，导致测试人员反馈"条件已满足但宏动作未触发"。
    /// 这里在事务内创建一个临时会话并真正运行宏（宏动作对会话/标签/备注的修改均落在本事务），
    /// 无论成功失败都回滚事务，仅用于验证"条件满足时宏动作能否被触发、动作是否全部成功"。
    /// </summary>
    private async Task<(bool Success, string Message)> ExecuteTestMacroAsync(
        WorkflowActionDto action, Workflow workflow, WorkflowTestRequest request)
    {
        if (!int.TryParse(action.Value?.Trim(), out var macroId) || macroId <= 0)
        {
            return (false, "宏 ID 无效，请重新选择宏");
        }

        // 解析邮箱：优先工作流绑定邮箱，其次请求指定邮箱，最后取第一个可用邮箱
        var mailboxId = workflow.MailboxId ?? request.MailboxId;
        if (mailboxId <= 0)
        {
            var anyMailbox = await _unitOfWork.Mailboxes.GetQueryable()
                .FirstOrDefaultAsync(m => m.IsActive);
            if (anyMailbox == null)
            {
                return (false, "未找到可用邮箱，请先在邮箱管理中配置邮箱");
            }
            mailboxId = anyMailbox.Id;
        }

        using (var transaction = await _unitOfWork.BeginTransactionAsync())
        {
            try
            {
                // 沙箱会话：仅存在于当前事务内，随回滚一并丢弃，不污染真实数据
                var sandbox = new Conversation
                {
                    MailboxId = mailboxId,
                    Number = await _unitOfWork.Conversations.GetNextNumberAsync(),
                    Subject = string.IsNullOrWhiteSpace(request.Subject) ? "[工作流测试] 临时会话" : request.Subject,
                    CustomerEmail = request.FromEmail ?? string.Empty,
                    Status = ConversationStatus.Active,
                    State = ConversationState.Published,
                    SourceVia = SourceVia.Customer,
                    SourceType = SourceType.Email,
                    CreatedAt = DateTime.UtcNow,
                    UpdatedAt = DateTime.UtcNow,
                    ThreadsCount = 0
                };
                sandbox.SetPreview(request.Body ?? string.Empty);

                await _unitOfWork.Conversations.AddAsync(sandbox);
                await _unitOfWork.SaveChangesAsync();

                var macroService = _serviceProvider.GetRequiredService<IMacroService>();
                var result = await macroService.ExecuteForWorkflowAsync(macroId, sandbox.Id);

                var success = result.Success;
                var message = result.Message ?? (success ? "宏已执行" : "宏执行失败");

                // 回滚并清空变更跟踪器：丢弃沙箱会话与宏产生的全部副作用（不写入数据库）
                await transaction.RollbackAsync();
                _unitOfWork.ClearPendingChanges();
                return (success,
                    success
                        ? $"已触发宏执行（沙箱验证，未持久化）：{message}"
                        : $"宏触发失败：{message}");
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "测试规则执行宏异常：WorkflowId={WorkflowId}, MacroId={MacroId}",
                    workflow.Id, macroId);
                try
                {
                    await transaction.RollbackAsync();
                }
                catch (Exception rollbackEx)
                {
                    _logger.LogWarning(rollbackEx, "测试规则宏事务回滚失败：WorkflowId={WorkflowId}",
                        workflow.Id);
                }
                _unitOfWork.ClearPendingChanges();
                return (false, $"宏测试执行异常：{ex.Message}");
            }
        }
    }

    /// <summary>
    /// 测试模式下允许的收件人邮箱域名白名单（M-004）。
    /// 非白名单域名的邮箱将被过滤并记录告警日志，防止测试邮件误发到外部地址。
    /// T000103 补全：加入本部署内部域 example.com（规则中配置的内部收件人可被测试）。
    /// </summary>
    private static readonly HashSet<string> AllowedTestEmailDomains = new(StringComparer.OrdinalIgnoreCase)
    {
        "nonead.com",
        "example.com"
    };

    /// <summary>邮箱地址最大长度（RFC 5321），超过直接拒绝，避免投递必然失败。</summary>
    private const int MaxEmailLength = 254;

    /// <summary>
    /// 解析测试模式下的收件人列表（T000103 全场景覆盖）：
    /// - action.Extra 为空或 "customer"：使用操作者填写的发件人邮箱（self 收件，不做域名白名单过滤）；
    /// - action.Extra 为 "assignee"：测试模式无分配客服，回退到操作者发件人邮箱（操作者扮演分配人）；
    /// - action.Extra 为具体邮箱地址：仍走域名白名单（M-004 防误发外部地址），被过滤时记入 Note；
    /// - 兜底：上述均未解析出收件人时，回退到操作者发件人邮箱并记入 Note，
    ///   保证只要操作者填写了发件人邮箱，测试邮件必然能送达，不再出现"未解析到有效收件人"死路。
    /// </summary>
    private (List<string> Recipients, string? Note) ResolveTestRecipients(string? extra, string? defaultEmail)
    {
        var result = new List<string>();
        var warnings = new List<string>();

        if (string.IsNullOrWhiteSpace(extra))
        {
            AddIfValidEmail(result, defaultEmail);
            return (result, null);
        }

        var tokens = extra.Split(',', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);
        foreach (var token in tokens)
        {
            if (string.Equals(token, "customer", StringComparison.OrdinalIgnoreCase))
            {
                AddIfValidEmail(result, defaultEmail);
            }
            else if (string.Equals(token, "assignee", StringComparison.OrdinalIgnoreCase))
            {
                // 测试模式无分配客服：回退到操作者发件人邮箱并提示
                AddIfValidEmail(result, defaultEmail);
                warnings.Add("规则收件人为 assignee，测试模式无分配客服，已改用测试输入的发件人邮箱");
            }
            else if (token.Contains('@'))
            {
                // 规则配置中写死的邮箱地址：仍走域名白名单（M-004 防误发外部地址）
                if (!TryAddWhitelisted(result, token))
                {
                    warnings.Add($"「{token}」域名不在测试白名单，未发送给该地址");
                }
            }
            // 其他非邮箱 token：忽略
        }

        // 兜底：只要操作者填写了发件人邮箱，任何场景都保证测试邮件能送达
        if (result.Count == 0)
        {
            AddIfValidEmail(result, defaultEmail);
            if (result.Count > 0)
            {
                warnings.Add("未解析到规则收件人，已改用测试输入的发件人邮箱");
            }
        }

        // 收件人去重（大小写不敏感）：customer+assignee 同源、规则里重复填写同一邮箱等场景，
        // 避免同一封测试邮件被 Enqueue 多次重复发送。
        var distinct = new List<string>();
        var seen = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        foreach (var email in result)
        {
            if (seen.Add(email)) distinct.Add(email);
        }

        return (distinct, warnings.Count > 0 ? string.Join("；", warnings) : null);
    }

    /// <summary>
    /// 添加操作者显式输入的邮箱（测试收件人），仅做基本格式校验，不做域名白名单过滤。
    /// 超长地址（>254，RFC 5321 邮箱地址上限）与格式非法地址直接拒绝并记 Warning，
    /// 便于排查"测试邮件未送达"类问题（如操作者误填了超长/畸形邮箱）。
    /// </summary>
    private void AddIfValidEmail(List<string> recipients, string? email)
    {
        if (string.IsNullOrWhiteSpace(email)) return;
        var trimmed = email.Trim();
        if (trimmed.Length > MaxEmailLength)
        {
            _logger.LogWarning(
                "工作流测试邮件收件人地址超长（>{MaxEmailLength} 字符，RFC 5321 上限），已忽略：长度={Length}",
                MaxEmailLength, trimmed.Length);
            return;
        }
        var atCount = trimmed.Count(c => c == '@');
        if (atCount == 1 && !trimmed.StartsWith('@') && !trimmed.EndsWith('@'))
        {
            recipients.Add(trimmed);
        }
        else
        {
            _logger.LogWarning("工作流测试邮件收件人地址格式非法，已忽略：{Email}", trimmed);
        }
    }

    /// <summary>
    /// 校验邮箱域名是否在白名单中，通过则加入收件人列表并返回 true，否则 LogWarning 并返回 false。
    /// 超长地址单独记录日志（区分于一般格式非法），便于统计/排查超长收件人配置。
    /// </summary>
    private bool TryAddWhitelisted(List<string> recipients, string email)
    {
        if (email.Length > MaxEmailLength)
        {
            _logger.LogWarning(
                "工作流测试邮件收件人地址超长（>{MaxEmailLength} 字符，RFC 5321 上限），已过滤：长度={Length}",
                MaxEmailLength, email.Length);
            return false;
        }

        // 基本格式校验：仅接受"有且只有一个 @ 且 @ 两侧非空"的邮箱形态，
        // 避免 a@b@example.com 这类畸形地址因 Split('@').Last() 命中白名单而被放行。
        var atCount = email.Count(c => c == '@');
        if (atCount != 1 || email.StartsWith('@') || email.EndsWith('@'))
        {
            _logger.LogWarning("工作流测试邮件收件人格式非法，已过滤：{Email}", email);
            return false;
        }

        var domain = email.Split('@').LastOrDefault();
        if (string.IsNullOrEmpty(domain) || !AllowedTestEmailDomains.Contains(domain))
        {
            _logger.LogWarning("工作流测试邮件收件人域名不在白名单中，已过滤：{Email}", email);
            return false;
        }
        recipients.Add(email);
        return true;
    }

    /// <summary>
    /// 构建工作流动作的可读描述，用于测试预览
    /// </summary>
    private static string BuildActionDescription(WorkflowActionDto action)
    {
        var label = WorkflowConst.GetActionLabel(action.Type);
        return action.Type switch
        {
            "set_status" => $"将会话状态设置为 {action.Value ?? "未知"}",
            "assign_user" => $"将会话分配给用户 ID={action.Value ?? "未知"}",
            "add_tag" => $"为会话添加标签「{action.Value ?? "未知"}」",
            "remove_tag" => $"从会话移除标签「{action.Value ?? "未知"}」",
            "set_priority" => $"将会话优先级设置为 {action.Value ?? "未知"}",
            "send_reply" => $"发送回复邮件，收件人：{action.Extra ?? "客户"}，内容/模板：{action.Value ?? "未知"}",
            "forward" => $"将会话转发至：{action.Extra ?? action.Value ?? "未指定收件人"}",
            WorkflowConst.ActionSendEmail => $"使用模板「{action.Value ?? "未知"}」发送邮件，收件人：{action.Extra ?? "客户"}",
            WorkflowConst.ActionConversationProcess => $"执行会话处理：{action.Value ?? "未知"}",
            WorkflowConst.ActionTriggerWebhook => $"触发事件类型「{action.Value ?? "未知"}」的 Webhook",
            WorkflowConst.ActionExecuteMacro => $"执行宏 #{action.Value ?? "未知"}",
            _ => $"{label}: {action.Value ?? "-"}"
        };
    }

    public async Task<int> ExecuteForConversationAsync(int conversationId, string triggerType)
    {
        return await ExecuteWorkflowsForConversationAsync(
            conversationId,
            triggerType,
            buildContextAsync: async (c) => await BuildContextFromConversation(c));
    }

    public async Task<int> ExecuteForSatisfactionRatingAsync(int ratingId, string triggerType)
    {
        var rating = await _unitOfWork.SatisfactionRatings.GetByIdAsync(ratingId);
        if (rating == null) return 0;

        var conversation = await _unitOfWork.Conversations.GetByIdAsync(rating.ConversationId);
        if (conversation == null) return 0;

        return await ExecuteWorkflowsForConversationAsync(
            rating.ConversationId,
            triggerType,
            buildContextAsync: async (c) =>
            {
                var context = await BuildContextFromConversation(c);
                context.SatisfactionRating = rating.Rating.ToString();
                context.SatisfactionComment = rating.Comment ?? string.Empty;
                return context;
            },
            rating);
    }

    public async Task<int> ExecuteForChatSessionAsync(int chatSessionId, string triggerType, int? chatRating = null, string? chatRatingComment = null)
    {
        return await ExecuteWorkflowsForChatSessionAsync(chatSessionId, triggerType, chatRating, chatRatingComment);
    }

    /// <summary>
    /// ChatSession 级工作流执行器：专门用于 chat_close_requested / chat_satisfaction_submitted 两个在线聊天 Trigger。
    /// 流程：Get ChatSession → 按 MailboxId 拉工作流 → 条件评估 → 通过 IWorkflowActionHandlerProvider 对匹配动作逐个执行。
    /// 设计上与 ExecuteWorkflowsForConversationAsync 保持一致，但允许 ChatSession.ConversationId 为 null（未转工单），
    /// 此时传一个 MailboxId 正确的 "DummyConversation" 给条件评估与 Handler，保证不 NRE。
    /// </summary>
    private async Task<int> ExecuteWorkflowsForChatSessionAsync(
        int chatSessionId,
        string triggerType,
        int? chatRating = null,
        string? chatRatingComment = null)
    {
        var chatSession = await _unitOfWork.ChatSessions.GetByIdAsync(chatSessionId);
        if (chatSession == null)
        {
            _logger.LogWarning("ExecuteForChatSessionAsync 找不到 ChatSession（Id={ChatSessionId}, Trigger={Trigger}）",
                chatSessionId, triggerType);
            return 0;
        }

        var mailboxId = chatSession.MailboxId ?? 0;
        var workflows = await _unitOfWork.Workflows.GetActiveByMailboxAsync(mailboxId, triggerType);
        if (workflows.Count == 0) return 0;

        // 若 ChatSession 已关联 Conversation，则用真实 Conversation；否则构造 DummyConversation 保证条件评估/Handler 拿到合法对象
        Conversation? conversation = null;
        Mailbox? cachedMailbox = null;
        if (chatSession.ConversationId.HasValue)
        {
            conversation = await _unitOfWork.Conversations.GetByIdTrackedAsync(chatSession.ConversationId.Value);
            if (conversation != null && conversation.Mailbox == null)
            {
                cachedMailbox = await _unitOfWork.Mailboxes.GetByIdAsync(conversation.MailboxId);
                if (cachedMailbox != null)
                {
                    conversation.Mailbox = cachedMailbox;
                }
            }
        }
        if (conversation == null)
        {
            cachedMailbox ??= await _unitOfWork.Mailboxes.GetByIdAsync(mailboxId);
            conversation = new Conversation
            {
                Id = 0,
                MailboxId = mailboxId,
                Status = ConversationStatus.Active,
                Subject = chatSession.Subject ?? string.Empty,
                CustomerId = chatSession.CustomerId,
                CustomerEmail = chatSession.CustomerEmail ?? string.Empty,
                CreatedAt = chatSession.CreatedAt,
                UpdatedAt = chatSession.UpdatedAt
            };
            if (cachedMailbox != null)
            {
                conversation.Mailbox = cachedMailbox;
            }
        }

        var needReloadEntities = false;
        int executedTotal = 0;
        // 维护本次执行产生的日志列表，循环结束后批量持久化（参照 ExecuteWorkflowsForConversationAsync 模式）
        var logsToSave = new List<WorkflowExecutionLog>();
        foreach (var workflow in workflows)
        {
            Conversation? localConversation = conversation;
            ChatSession? localChatSession = chatSession;
            if (needReloadEntities)
            {
                localChatSession = await _unitOfWork.ChatSessions.GetByIdAsync(chatSessionId);
                if (localChatSession == null)
                {
                    _logger.LogWarning("ChatSession 重载失败（Id={ChatSessionId}），中止后续工作流", chatSessionId);
                    break;
                }
                if (localChatSession.ConversationId.HasValue)
                {
                    localConversation = await _unitOfWork.Conversations.GetByIdTrackedAsync(localChatSession.ConversationId.Value);
                }
                if (localConversation == null)
                {
                    localConversation = new Conversation
                    {
                        Id = 0,
                        MailboxId = mailboxId,
                        Status = ConversationStatus.Active,
                        Subject = localChatSession.Subject ?? string.Empty,
                        CustomerId = localChatSession.CustomerId,
                        CustomerEmail = localChatSession.CustomerEmail ?? string.Empty,
                        CreatedAt = localChatSession.CreatedAt,
                        UpdatedAt = localChatSession.UpdatedAt
                    };
                }
                needReloadEntities = false;
            }

            // 日志中的 ConversationId：ChatSession 已关联 Conversation 时用真实 Id，否则为 null（禁止写入 0，避免违反外键约束）
            var rawConversationId = localChatSession?.ConversationId ?? 0;
            int? logConversationId = rawConversationId == 0 ? null : rawConversationId;

            // #5 崩溃可见性：同 conversation 执行器，执行前先落 pending 并立即持久化。
            WorkflowExecutionLog? pendingLog = null;
            try
            {
                pendingLog = new WorkflowExecutionLog
                {
                    WorkflowId = workflow.Id,
                    ConversationId = logConversationId,
                    ExecutedAt = DateTime.UtcNow,
                    ActionsExecuted = 0,
                    Status = "pending"
                };
                await _unitOfWork.Workflows.AddExecutionLogAsync(pendingLog);
                await _unitOfWork.SaveChangesAsync();

                WorkflowEvaluationContext evalContext;
                if (workflow.HasConditions())
                {
                    evalContext = await BuildContextFromConversation(localConversation!);
                    if (!workflow.EvaluateConditions(evalContext, out _))
                    {
                        _logger.LogDebug("ChatSession 工作流条件不匹配：WorkflowId={WorkflowId}, Trigger={Trigger}, ChatSessionId={ChatSessionId}",
                            workflow.Id, triggerType, chatSessionId);
                        if (pendingLog != null)
                        {
                            pendingLog.Status = "not_matched";
                            pendingLog.ActionsExecuted = 0;
                        }
                        continue;
                    }
                }
                else
                {
                    evalContext = new WorkflowEvaluationContext();
                }

                var actions = workflow.DeserializeActions();
                var pendingSideEffects = new List<Func<Task>>();
                int executed = 0;
                var errors = new List<string>();
                string? status = "success";
                string? errorMessage = null;

                using var transaction = await _unitOfWork.BeginTransactionAsync();
                try
                {
                    var allActionsSucceeded = true;
                    foreach (var action in actions)
                    {
                        try
                        {
                            var actionContext = new WorkflowActionContext
                            {
                                ChatSession = localChatSession,
                                ChatRating = chatRating,
                                ChatRatingComment = chatRatingComment,
                                PendingSideEffects = pendingSideEffects,
                                ServiceProvider = _serviceProvider
                            };

                            bool handled = await ExecuteActionThroughHandlersAsync(action, localConversation!, actionContext);
                            if (handled)
                            {
                                executed++;
                            }
                            else
                            {
                                allActionsSucceeded = false;
                                errors.Add($"{WorkflowConst.GetActionLabel(action.Type)}: 执行失败");
                            }
                        }
                        catch (Exception ex)
                        {
                            allActionsSucceeded = false;
                            errors.Add($"{WorkflowConst.GetActionLabel(action.Type)}: {ex.Message}");
                            _logger.LogError(ex,
                                "ChatSession 工作流动作执行失败：WorkflowId={WorkflowId}, ActionType={ActionType}, ChatSessionId={ChatSessionId}",
                                workflow.Id, action.Type, chatSessionId);
                        }
                    }

                    if (allActionsSucceeded)
                    {
                        await _unitOfWork.SaveChangesAsync();
                        await transaction.CommitAsync();
                        status = "success";
                        executedTotal += executed;

                        foreach (var sideEffect in pendingSideEffects)
                        {
                            try { await sideEffect(); }
                            catch (Exception ex) { _logger.LogError(ex, "ChatSession 工作流副作用执行失败：ChatSessionId={ChatSessionId}", chatSessionId); }
                        }
                    }
                    else
                    {
                        await transaction.RollbackAsync();
                        _unitOfWork.ClearChanges();
                        needReloadEntities = true;
                        executed = 0;
                        status = "failed";
                        errorMessage = errors.Count > 0 ? string.Join("; ", errors) : null;
                    }
                }
                catch (Exception ex)
                {
                    await transaction.SafeRollbackAsync();
                    _unitOfWork.ClearChanges();
                    needReloadEntities = true;
                    executed = 0;
                    status = "failed";
                    errorMessage = ex.Message;
                    _logger.LogError(ex, "ChatSession 工作流事务执行失败，WorkflowId={WorkflowId}, ChatSessionId={ChatSessionId}", workflow.Id, chatSessionId);

                    logsToSave.Add(new WorkflowExecutionLog
                    {
                        WorkflowId = workflow.Id,
                        ConversationId = logConversationId,
                        ExecutedAt = DateTime.UtcNow,
                        ActionsExecuted = executed,
                        Status = status,
                        ErrorMessage = errorMessage
                    });
                    break;
                }

                // 更新同一条 pending 记录（不再新增，避免每次执行留下两条日志）
                if (pendingLog != null)
                {
                    pendingLog.ActionsExecuted = executed;
                    pendingLog.Status = status;
                    pendingLog.ErrorMessage = errorMessage;
                }
            }
            catch (Exception ex)
            {
                _logger.LogError(ex,
                    "ChatSession 工作流执行失败：WorkflowId={WorkflowId}, Trigger={Trigger}, ChatSessionId={ChatSessionId}",
                    workflow.Id, triggerType, chatSessionId);
                needReloadEntities = true;

                logsToSave.Add(new WorkflowExecutionLog
                {
                    WorkflowId = workflow.Id,
                    ConversationId = logConversationId,
                    ExecutedAt = DateTime.UtcNow,
                    Status = "failed",
                    ErrorMessage = ex.Message,
                    ActionsExecuted = 0
                });
            }
        }

        // 批量持久化执行日志（参照 ExecuteWorkflowsForConversationAsync 模式）
        foreach (var log in logsToSave)
        {
            await _unitOfWork.Workflows.AddExecutionLogAsync(log);
        }
        if (logsToSave.Count > 0)
        {
            try
            {
                await _unitOfWork.SaveChangesAsync();
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "ChatSession 工作流执行日志批量保存失败：ChatSessionId={ChatSessionId}", chatSessionId);
                _unitOfWork.ClearChanges();
            }
        }

        await CheckAndAlertContinuousFailuresAsync(logsToSave);

        if (executedTotal > 0)
        {
            _logger.LogInformation("ChatSession 工作流执行完毕：ChatSessionId={ChatSessionId}, Trigger={Trigger}, ExecutedActions={Executed}",
                chatSessionId, triggerType, executedTotal);
        }
        return executedTotal;
    }

    /// <summary>
    /// 通过 IWorkflowActionHandlerProvider 按动作 Type 找处理器并执行（复用 Conversation 级的同一套 Handler）。
    /// 对当前实现而言，真正匹配的是 SendChatMessageActionHandler（send_chat_message）；
    /// 其他动作如 send_email / send_im_message 若配置了，也会走对应 Handler。
    /// </summary>
    private async Task<bool> ExecuteActionThroughHandlersAsync(
        WorkflowAction action,
        Conversation conversation,
        WorkflowActionContext actionContext)
    {
        if (!_handlerProvider.TryGetHandler(action.Type, out var handler) || handler == null)
        {
            _logger.LogDebug("ChatSession 工作流动作无匹配 Handler：ActionType={ActionType}",
                action.Type);
            return false;
        }

        var ok = await handler.ExecuteAsync(action, conversation, actionContext);
        _logger.LogDebug("ChatSession 动作 Handler 执行结果：ActionType={ActionType}, Ok={Ok}",
            action.Type, ok);
        return ok;
    }

    /// <summary>
    /// 工作流执行核心方法：遍历匹配的工作流，执行动作并记录日志。
    /// 支持通过 buildContextAsync 自定义上下文构建（会话/满意度评价等场景），
    /// 通过 rating 参数传递给 ExecuteActionAsync 以支持满意度相关动作。
    /// </summary>
    private async Task<int> ExecuteWorkflowsForConversationAsync(
        int conversationId,
        string triggerType,
        Func<Conversation, Task<WorkflowEvaluationContext>> buildContextAsync,
        SatisfactionRating? rating = null)
    {
        var conversation = await _unitOfWork.Conversations.GetByIdTrackedAsync(conversationId);
        if (conversation == null) return 0;

        // 预加载 Mailbox 用于 BuildContextFromConversation（仅查询 Mailbox 实体本身，不 Include MailboxUsers）
        // 不能使用 Mailboxes.GetByIdAsync（会 Include MailboxUsers），因为把 AsNoTracking 的 Mailbox
        // （含 MailboxUsers 集合）赋值给 tracked conversation.Mailbox 会触发 EF Core fixup，
        // 把 MailboxUsers 错误标记为 Added，导致 SaveChangesAsync 时 UNIQUE constraint 冲突
        if (conversation.Mailbox == null)
        {
            var mailbox = await _unitOfWork.Repository<Mailbox>()
                .GetQueryable(true)
                .FirstOrDefaultAsync(m => m.Id == conversation.MailboxId && m.DeletedAt == null);
            if (mailbox != null)
            {
                conversation.Mailbox = mailbox;
            }
        }

        var mailboxId = conversation.MailboxId;
        var workflows = await _unitOfWork.Workflows.GetActiveByMailboxAsync(mailboxId, triggerType);

        var executedCount = 0;
        var logsToSave = new List<WorkflowExecutionLog>();
        // 仅在事务回滚（ClearChanges 会使实体脱 tracked）或外层异常后重新查询，
        // 避免每个工作流迭代都重复查询同一会话（N+1）。
        var needReloadConversation = false;
        // 归一化：参数 conversationId 为 0 时写 null，避免外键约束违规
        int? logConversationId = conversationId == 0 ? null : conversationId;

        foreach (var workflow in workflows)
        {
            // #5 崩溃可见性：执行前先落一条 pending 日志并**立即**持久化。
            // 原实现把日志攒到循环结束后批量保存，进程若在动作执行中途被杀，这次执行不会留下任何痕迹；
            // 现在重启后能凭 pending 判定"有任务被打断"（由 WorkflowInterruptionHostedService 标 interrupted）。
            WorkflowExecutionLog? pendingLog = null;
            try
            {
                pendingLog = new WorkflowExecutionLog
                {
                    WorkflowId = workflow.Id,
                    ConversationId = logConversationId,
                    ExecutedAt = DateTime.UtcNow,
                    ActionsExecuted = 0,
                    Status = "pending"
                };
                await _unitOfWork.Workflows.AddExecutionLogAsync(pendingLog);
                await _unitOfWork.SaveChangesAsync();

                if (needReloadConversation || conversation == null)
                {
                    conversation = await _unitOfWork.Conversations.GetByIdTrackedAsync(conversationId);
                    needReloadConversation = false;
                    if (conversation != null && conversation.Mailbox == null)
                    {
                        // 使用泛型 Repository 查询（不 Include MailboxUsers），避免 EF Core fixup 问题
                        var mailbox = await _unitOfWork.Repository<Mailbox>()
                            .GetQueryable(true)
                            .FirstOrDefaultAsync(m => m.Id == conversation.MailboxId && m.DeletedAt == null);
                        if (mailbox != null)
                        {
                            conversation.Mailbox = mailbox;
                        }
                    }
                }
                if (conversation == null) break;

                var context = await buildContextAsync(conversation);
                var matched = workflow.EvaluateConditions(context, out _);
                var actions = workflow.DeserializeActions();

                if (!matched)
                {
                    logsToSave.Add(new WorkflowExecutionLog
                    {
                        WorkflowId = workflow.Id,
                        ConversationId = logConversationId,
                        ExecutedAt = DateTime.UtcNow,
                        Status = "not_matched",
                        ActionsExecuted = 0
                    });
                    continue;
                }

                var executed = 0;
                var errors = new List<string>();
                string? status = "success";
                string? errorMessage = null;
                var pendingSideEffects = new List<Func<Task>>();

                using (var transaction = await _unitOfWork.BeginTransactionAsync())
                {
                    try
                    {
                        var allActionsSucceeded = true;
                        foreach (var action in actions)
                        {
                            try
                            {
                                var ok = await ExecuteActionAsync(action, conversation, rating, pendingSideEffects);
                                if (ok)
                                {
                                    executed++;
                                }
                                else
                                {
                                    allActionsSucceeded = false;
                                    errors.Add($"{WorkflowConst.GetActionLabel(action.Type)}: 执行失败");
                                }
                            }
                            catch (Exception ex)
                            {
                                allActionsSucceeded = false;
                                errors.Add($"{WorkflowConst.GetActionLabel(action.Type)}: {ex.Message}");
                                _logger.LogError(ex, "工作流 {WorkflowId} 执行动作 {ActionType}（Value={ActionValue}）时发生异常，会话 {ConversationId}",
                                    workflow.Id, action.Type, action.Value, conversationId);
                            }
                        }

                        if (allActionsSucceeded)
                        {
                            workflow.RecordMatch();
                            // workflow 已通过 GetActiveByMailboxAsync 追踪，无需调用 UpdateAsync
                            await _unitOfWork.SaveChangesAsync();
                            await transaction.CommitAsync();
                            status = "success";

                            foreach (var sideEffect in pendingSideEffects)
                            {
                                try { await sideEffect(); }
                                catch (Exception ex) { _logger.LogError(ex, "执行外部副作用失败（工作流 {WorkflowId}）", workflow.Id); }
                            }
                        }
                        else
                        {
                            await transaction.RollbackAsync();
                            // detach 工作流内部新增/删除/修改的实体，保留外层 IngestionService 的 Unchanged 实体
                            _unitOfWork.ClearPendingChanges();
                            executed = 0;
                            status = "failed";
                            errorMessage = errors.Count > 0 ? string.Join("; ", errors) : null;
                            needReloadConversation = true;
                        }
                    }
                    catch (Exception ex)
                    {
                        await transaction.SafeRollbackAsync();
                        // detach 工作流内部新增/删除/修改的实体，保留外层 IngestionService 的 Unchanged 实体
                        _unitOfWork.ClearPendingChanges();
                        executed = 0;
                        status = "failed";
                        errorMessage = ex.Message;
                        _logger.LogError(ex, "执行工作流 {WorkflowId} 时发生异常", workflow.Id);
                        needReloadConversation = true;
                    }
                }

                // 更新同一条 pending 记录（不再新增，避免每次执行留下两条日志）
                if (pendingLog != null)
                {
                    pendingLog.ActionsExecuted = executed;
                    pendingLog.Status = status;
                    pendingLog.ErrorMessage = errorMessage;
                }

                if (status == "success")
                {
                    executedCount++;
                }
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "执行工作流 {WorkflowId} 失败", workflow.Id);

                if (pendingLog != null)
                {
                    pendingLog.Status = "failed";
                    pendingLog.ErrorMessage = ex.Message;
                }
                // 外层异常可能发生在 buildContextAsync/EvaluateConditions 阶段，
                // conversation 状态不确定，下一轮需重新查询。
                needReloadConversation = true;
            }
        }

        // 批量持久化执行日志。日志保存失败不应影响主流程，也不应让异常冒泡导致调用方误以为工作流执行失败。
        // 失败时记录错误日志并清理变更跟踪器，避免残留的 Added 状态日志实体污染下一次 SaveChangesAsync。
        try
        {
            foreach (var log in logsToSave)
            {
                await _unitOfWork.Workflows.AddExecutionLogAsync(log);
            }
            if (logsToSave.Count > 0)
            {
                await _unitOfWork.SaveChangesAsync();
            }
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "工作流执行日志批量保存失败，共 {LogCount} 条日志丢失", logsToSave.Count);
            _unitOfWork.ClearChanges();
        }

        await CheckAndAlertContinuousFailuresAsync(logsToSave);

        return executedCount;
    }

    /// <summary>
    /// 检查工作流连续失败次数，超过阈值时记录告警日志。
    /// </summary>
    private async Task CheckAndAlertContinuousFailuresAsync(List<WorkflowExecutionLog> logs)
    {
        var failedWorkflowIds = logs
            .Where(l => l.Status == "failed")
            .Select(l => l.WorkflowId)
            .Distinct()
            .ToList();

        if (failedWorkflowIds.Count == 0) return;

        foreach (var workflowId in failedWorkflowIds)
        {
            try
            {
                var (recentLogs, _) = await _unitOfWork.Workflows.GetExecutionLogsPagedAsync(
                    1, 5, workflowId, null, "failed");
                var recentList = recentLogs.ToList();

                // L-F4：连续失败告警阈值提取为常量，便于统一调整
                if (recentList.Count >= ContinuousFailureAlertThreshold && recentList.Take(ContinuousFailureAlertThreshold).All(l => l.Status == "failed"))
                {
                    _logger.LogWarning(
                        "工作流 {WorkflowId} 连续失败 {FailureCount} 次，请检查工作流配置或相关服务是否正常",
                        workflowId,
                        recentList.Count(l => l.Status == "failed"));
                }
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "检查工作流 {WorkflowId} 连续失败状态时出错", workflowId);
            }
        }
    }

    /// <summary>工作流连续失败告警阈值（L-F4）：连续失败达到此次数时记录告警日志。</summary>
    private const int ContinuousFailureAlertThreshold = 3;

    /// <summary>
    /// 执行工作流动作：通过 IWorkflowActionHandlerProvider 查找对应的策略处理器并执行。
    /// 注意：conversation 通过 GetByIdTrackedAsync 加载，已被 EF Core 跟踪。
    /// 修改属性后无需调用 UpdateAsync，EF Core 会自动检测变更并在 SaveChangesAsync 时持久化。
    /// </summary>
    private async Task<bool> ExecuteActionAsync(WorkflowAction action, Conversation conversation, SatisfactionRating? rating = null, List<Func<Task>>? pendingSideEffects = null)
    {
        if (!_handlerProvider.TryGetHandler(action.Type, out var handler) || handler == null)
        {
            // 配置了未知动作类型时不能只留一条 Warning：工作流会「看起来在跑，但该动作什么都不做」，
            // 使用者无从察觉。升级为 Error 并在消息里写明不会生效，便于按日志定位配置错误。
            _logger.LogError("工作流动作类型 {ActionType} 没有对应的处理器，该动作不会生效，请检查工作流配置", action.Type);
            return false;
        }

        var context = new WorkflowActionContext
        {
            Rating = rating,
            PendingSideEffects = pendingSideEffects,
            ServiceProvider = _serviceProvider
        };

        return await handler.ExecuteAsync(action, conversation, context);
    }

    private async Task<WorkflowEvaluationContext> BuildContextFromConversation(Conversation conversation)
    {
        // conversation 通过 ConversationRepository.GetByIdAsync 加载，已通过 ApplyFullIncludes
        // 包含 Customer（含 Emails）、User、ConversationTags（含 Tag）、Threads（含 Attachments）、Followers。
        // 直接使用已加载的导航属性，避免在循环中重复查询 DB（N+1 问题）。
        // 仅 Mailbox 未在标准 Include 中，需单独查询。
        var customer = conversation.Customer;
        var firstThread = conversation.Threads?
            .Where(t => t.State != Domain.Entities.ThreadState.Deleted && t.State != Domain.Entities.ThreadState.Draft)
            .OrderBy(t => t.CreatedAt)
            .FirstOrDefault();
        var tagNames = conversation.ConversationTags?
            .Where(ct => ct.Tag != null)
            .Select(ct => ct.Tag!.Name)
            .ToList() ?? new List<string>();
        var assignee = conversation.User;

        var mailbox = conversation.Mailbox;
        if (mailbox == null)
        {
            // 使用泛型 Repository 查询（不 Include MailboxUsers），避免 EF Core fixup 问题
            mailbox = await _unitOfWork.Repository<Mailbox>()
                .GetQueryable(true)
                .FirstOrDefaultAsync(m => m.Id == conversation.MailboxId && m.DeletedAt == null);
        }

        return new WorkflowEvaluationContext
        {
            Subject = conversation.Subject ?? string.Empty,
            FromEmail = customer?.GetMainEmail() ?? conversation.CustomerEmail ?? string.Empty,
            ToEmail = string.Empty,
            Body = firstThread?.Body ?? string.Empty,
            CustomerName = customer?.GetFullName() ?? string.Empty,
            Status = ((int)conversation.Status).ToString(),
            Priority = ((int)conversation.Priority).ToString(),
            Tags = tagNames,
            Mailbox = mailbox?.Name ?? string.Empty,
            Assignee = assignee != null ? $"{assignee.FirstName} {assignee.LastName}".Trim() : string.Empty,
            SourceType = conversation.SourceType.ToString()
        };
    }

    private static readonly JsonSerializerOptions WriteJsonOptions = new()
    {
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
        Encoder = System.Text.Encodings.Web.JavaScriptEncoder.UnsafeRelaxedJsonEscaping,
    };

    private static string? SerializeConditions(WorkflowConditionGroup? group)
    {
        if (group == null) return null;
        return JsonSerializer.Serialize(group, WriteJsonOptions);
    }

    private static string? SerializeActions(List<WorkflowActionDto> actions)
    {
        if (actions == null || actions.Count == 0) return null;
        return JsonSerializer.Serialize(actions, WriteJsonOptions);
    }

    private static WorkflowActionDto ToActionDto(WorkflowAction action)
    {
        return new WorkflowActionDto
        {
            Type = action.Type,
            Value = action.Value,
            Extra = action.Extra
        };
    }

    public async Task TriggerActionAsync(string actionType, string entityType, int entityId, IDictionary<string, object>? jobData = null)
    {
        try
        {
            _logger.LogInformation("动作 {ActionType} 触发: {EntityType}#{EntityId}", actionType, entityType, entityId);

            if (entityType != "conversation")
            {
                _logger.LogWarning("不支持的实体类型: {EntityType}，仅支持 conversation", entityType);
                return;
            }

            // 使用 GetByIdTrackedAsync 加载被跟踪的实体，确保后续动作处理器对会话的修改能通过 SaveChangesAsync 持久化。
            // GetByIdAsync 使用 AsNoTracking，修改无法被 EF Core 检测到，会导致 SaveChangesAsync 不写入任何变更。
            var conversation = await _unitOfWork.Conversations.GetByIdTrackedAsync(entityId);
            if (conversation == null)
            {
                _logger.LogInformation("会话不存在: {EntityId}，可能已被删除（异步工作流竞态）", entityId);
                return;
            }

            var actionValue = string.Empty;
            var actionExtra = string.Empty;

            if (jobData != null)
            {
                if (jobData.ContainsKey("value"))
                    actionValue = jobData["value"]?.ToString() ?? string.Empty;
                else if (actionType == "assign_user" && jobData.ContainsKey("userId"))
                    actionValue = jobData["userId"]?.ToString() ?? string.Empty;

                if (jobData.ContainsKey("extra"))
                    actionExtra = jobData["extra"]?.ToString() ?? string.Empty;
            }

            var action = new WorkflowAction
            {
                Type = actionType,
                Value = actionValue,
                Extra = actionExtra
            };

            var executed = await ExecuteActionAsync(action, conversation);

            if (executed)
            {
                await _unitOfWork.SaveChangesAsync();
                _logger.LogInformation("动作 {ActionType} 执行成功: {EntityType}#{EntityId}", actionType, entityType, entityId);
            }
            else
            {
                _logger.LogWarning("动作 {ActionType} 未执行（参数无效或不支持）: {EntityType}#{EntityId}", actionType, entityType, entityId);
            }
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "触发动作失败: {ActionType}", actionType);
            // 上抛异常让 RetryableJobBase 统一处理重试，避免静默失败导致工作流定时动作（如自动关闭工单、提醒）丢失
            throw;
        }
    }

    private static WorkflowDto ToDto(Workflow entity)
    {
        var mailboxName = entity.Mailbox?.Name;
        return new WorkflowDto
        {
            Id = entity.Id,
            Name = entity.Name,
            Description = entity.Description,
            MailboxId = entity.MailboxId,
            MailboxName = mailboxName,
            IsActive = entity.IsActive,
            IsSystem = entity.IsSystem,
            SortOrder = entity.SortOrder,
            TriggerType = entity.TriggerType,
            TriggerLabel = WorkflowConst.GetTriggerLabel(entity.TriggerType),
            Conditions = entity.DeserializeConditions(),
            Actions = entity.DeserializeActions().Select(ToActionDto).ToList(),
            MatchCount = entity.MatchCount,
            LastMatchedAt = entity.LastMatchedAt,
            CreatedAt = entity.CreatedAt,
            UpdatedAt = entity.UpdatedAt,
            DeletedAt = entity.DeletedAt
        };
    }

    private static WorkflowExecutionLogDto ToLogDto(WorkflowExecutionLog log)
    {
        var statusLabel = log.Status switch
        {
            "success" => "成功",
            "partial" => "部分成功",
            "failed" => "失败",
            "not_matched" => "未匹配",
            _ => log.Status
        };

        return new WorkflowExecutionLogDto
        {
            Id = log.Id,
            WorkflowId = log.WorkflowId,
            WorkflowName = log.Workflow?.Name ?? $"#{log.WorkflowId}",
            ConversationId = log.ConversationId,
            ConversationSubject = log.Conversation?.Subject,
            Status = log.Status,
            StatusLabel = statusLabel,
            ActionsExecuted = log.ActionsExecuted,
            ErrorMessage = log.ErrorMessage,
            ExecutedAt = log.ExecutedAt
        };
    }

}
