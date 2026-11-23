using NoneadCSS.Application.Services;
using NoneadCSS.Application.Services.Calendar;
using NoneadCSS.Application.Services.WorkflowActions;
using NoneadCSS.Application.Templates;
using NoneadCSS.Application.Templates.Providers;
using Microsoft.Extensions.DependencyInjection;

namespace NoneadCSS.Application;

public static class DependencyInjection
{
    public static IServiceCollection AddApplication(this IServiceCollection services)
    {
        services.AddMemoryCache();

        // 关键修复：必须为 Singleton，而非 Scoped。
        // MemoryCalendarQueryCache 内部用 ConcurrentDictionary<int, long> _userVersions 跟踪失效版本号，
        // 若注册为 Scoped，每个请求拿到全新实例 → _userVersions 恒为空 → 版本号恒为 0 →
        // 缓存 key 恒使用 version=0 → InvalidateAllForUser 完全失效，删除/修改后刷新页面仍返回旧数据（色块残留）。
        // IMemoryCache 本身是 Singleton（AddMemoryCache 注册），version 状态必须与之同生命周期才能真正失效缓存。
        services.AddSingleton<ICalendarQueryCache, MemoryCalendarQueryCache>();

        services.AddScoped<IAuthService, AuthService>();
        services.AddScoped<IConversationService, ConversationService>();
        services.AddScoped<IConversationThreadService, ConversationThreadService>();
        services.AddScoped<IConversationDraftService, ConversationDraftService>();
        services.AddScoped<IConversationAssignmentService, ConversationAssignmentService>();
        services.AddScoped<IConversationStatusService, ConversationStatusService>();
        services.AddScoped<IConversationTagService, ConversationTagService>();
        services.AddScoped<IMailboxService, MailboxService>();
        services.AddScoped<IUserService, UserService>();
        services.AddScoped<IUserRoleResolver, UserRoleResolver>();
        services.AddScoped<ICustomerService, CustomerService>();
        services.AddScoped<IFolderService, FolderService>();
        services.AddScoped<IDashboardService, DashboardService>();
        services.AddScoped<ITagService, TagService>();
        services.AddScoped<ISystemOptionService, SystemOptionService>();
        services.AddScoped<IPasswordPolicyService, PasswordPolicyService>();
        services.AddScoped<INotificationService, NotificationService>();
        services.AddScoped<IReportService, ReportService>();
        services.AddScoped<ICannedResponseService, CannedResponseService>();
        services.AddScoped<ISendLogService, SendLogService>();
        services.AddScoped<ISatisfactionRatingService, SatisfactionRatingService>();
        services.AddScoped<ISatisfactionTokenGenerator, SatisfactionTokenGenerator>();
        services.AddScoped<IWorkflowService, WorkflowService>();
        services.AddScoped<ISlaService, SlaService>();
        services.AddScoped<IMacroService, MacroService>();
        services.AddScoped<ICustomFieldService, CustomFieldService>();
        services.AddScoped<IWebhookService, WebhookService>();
        services.AddScoped<IRoleService, RoleService>();
        services.AddScoped<IMailTemplateService, MailTemplateService>();
        services.AddScoped<ITeamService, TeamService>();
        services.AddScoped<IEmailSignatureService, EmailSignatureService>();
        services.AddScoped<IModuleService, ModuleService>();
        services.AddScoped<IApiKeyService, ApiKeyService>();
        services.AddScoped<IImportExportService, ImportExportService>();
        services.AddScoped<ICalendarService, CalendarService>();
        services.AddScoped<ICalendarEventTemplateService, CalendarEventTemplateService>();
        services.AddScoped<ICalendarExportService, CalendarExportService>();

        // ===== P3-D: Outlook 同步健康仪表盘服务 =====
        services.AddScoped<ICalendarOutlookHealthService, CalendarOutlookHealthService>();

        // ===== P3 E#1: CalendarService 拆分后的 4 个子服务 + 辅助 =====
        // 采用 Facade 模式：ICalendarService 接口保留但实现 CalendarService 仅做转发，
        // Controller 可无感迁移；未来增量重构可逐步用具体子服务替换 ICalendarService 注入。
        services.AddScoped<ICalendarQueryService, CalendarQueryService>();
        services.AddScoped<ICalendarMutationService, CalendarMutationService>();
        services.AddScoped<ICalendarReminderService, CalendarReminderService>();
        services.AddScoped<ICalendarConflictService, CalendarConflictService>();
        services.AddScoped<CalendarAuthorizationService>();               // 具体类注入（被 4 个子服务直接依赖，非接口）
        services.AddScoped<IRecurrenceExpansionService, RecurrenceExpansionService>();
        // Lazy 注入：MutationService 需要 QueryService.GetByIdAsync 返回 DTO，用 Lazy 避免潜在循环
        services.AddScoped<Lazy<ICalendarQueryService>>(sp => new Lazy<ICalendarQueryService>(sp.GetRequiredService<ICalendarQueryService>));
        services.AddScoped<IKnowledgeService, KnowledgeService>();
        services.AddScoped<IChatService, ChatService>();
        services.AddScoped<IEmailIngestionService, EmailIngestionService>();
        services.AddScoped<ITimeEntryService, TimeEntryService>();
        services.AddScoped<IWeChatService, WeChatService>();
        services.AddScoped<IWeChatIngestionService, WeChatIngestionService>();

        // ===== WhatsApp Business Platform 接入服务注册 =====
        services.AddScoped<IWhatsAppService, WhatsAppService>();
        services.AddScoped<IWhatsAppIngestionService, WhatsAppIngestionService>();

        // ===== DingTalk 钉钉开放平台接入服务注册 =====
        services.AddScoped<IDingDingService, DingDingService>();
        services.AddScoped<IDingDingIngestionService, DingDingIngestionService>();

        // ===== 飞书机器人应用接入服务注册 =====
        services.AddScoped<IFeishuService, FeishuService>();
        services.AddScoped<IFeishuIngestionService, FeishuIngestionService>();

        // ===== Messenger Platform（Meta Graph API）接入服务注册 =====
        services.AddScoped<IMessengerService, MessengerService>();
        services.AddScoped<IMessengerIngestionService, MessengerIngestionService>();

        // ===== Telegram Bot API 接入服务注册 =====
        services.AddScoped<ITelegramService, TelegramService>();
        services.AddScoped<ITelegramIngestionService, TelegramIngestionService>();

        // ===== LINE Messaging API 接入服务注册 =====
        services.AddScoped<ILineService, LineService>();
        services.AddScoped<ILineIngestionService, LineIngestionService>();

        // ===== iMessage（桥接服务 / Apple Messages for Business）接入服务注册 =====
        services.AddScoped<IIMessageService, IMessageService>();
        services.AddScoped<IIMessageIngestionService, IMessageIngestionService>();

        services.AddScoped<CustomReportExportHelper>();
        services.AddScoped<ICustomReportService, CustomReportService>();

        // 工作流动作处理器（策略模式）：每个处理器负责一种动作类型
        services.AddScoped<SetStatusActionHandler>();
        services.AddScoped<ConversationProcessActionHandler>();
        services.AddScoped<AssignUserActionHandler>();
        services.AddScoped<AddTagActionHandler>();
        services.AddScoped<RemoveTagActionHandler>();
        services.AddScoped<SetPriorityActionHandler>();
        services.AddScoped<SendReplyForwardActionHandler>();
        services.AddScoped<SendEmailActionHandler>();
        services.AddScoped<SendImMessageActionHandler>();
        services.AddScoped<TriggerWebhookActionHandler>();
        services.AddScoped<ExecuteMacroActionHandler>();
        // 聊天渠道消息发送器：用于 IM 消息队列消费方按渠道发送消息
        services.AddScoped<IChatChannelMessageSender, ChatChannelMessageSender>();
        // 将所有处理器注册为 IWorkflowActionHandler
        services.AddScoped<IWorkflowActionHandler, SetStatusActionHandler>();
        services.AddScoped<IWorkflowActionHandler, ConversationProcessActionHandler>();
        services.AddScoped<IWorkflowActionHandler, AssignUserActionHandler>();
        services.AddScoped<IWorkflowActionHandler, AddTagActionHandler>();
        services.AddScoped<IWorkflowActionHandler, RemoveTagActionHandler>();
        services.AddScoped<IWorkflowActionHandler, SetPriorityActionHandler>();
        services.AddScoped<IWorkflowActionHandler, SendReplyForwardActionHandler>();
        services.AddScoped<IWorkflowActionHandler, SendEmailActionHandler>();
        services.AddScoped<IWorkflowActionHandler, SendImMessageActionHandler>();
        services.AddScoped<IWorkflowActionHandler, SendChatMessageActionHandler>();
        services.AddScoped<IWorkflowActionHandler, TriggerWebhookActionHandler>();
        services.AddScoped<IWorkflowActionHandler, ExecuteMacroActionHandler>();
        services.AddScoped<IWorkflowActionHandlerProvider, WorkflowActionHandlerProvider>();

        // ===== 统一模板变量系统 =====
        services.AddSingleton<ITemplateRenderer, TemplateRenderer>();
        services.AddScoped<TemplateBuilder>();
        // 所有 Provider 按 Order 排序执行
        services.AddScoped<ITemplateVariableProvider, SystemVariableProvider>();
        services.AddScoped<ITemplateVariableProvider, ConversationVariableProvider>();
        services.AddScoped<ITemplateVariableProvider, CustomerVariableProvider>();
        services.AddScoped<ITemplateVariableProvider, UserVariableProvider>();
        services.AddScoped<ITemplateVariableProvider, SlaVariableProvider>();
        services.AddScoped<ITemplateVariableProvider, RatingVariableProvider>();
        services.AddScoped<ITemplateVariableProvider, ChatVariableProvider>();
        services.AddScoped<ITemplateVariableProvider, CalendarVariableProvider>();
        services.AddScoped<ITemplateVariableProvider, MailboxVariableProvider>();

        return services;
    }
}
