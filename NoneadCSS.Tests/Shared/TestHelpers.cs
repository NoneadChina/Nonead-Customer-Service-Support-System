using Microsoft.Extensions.Caching.Memory;
using Microsoft.Extensions.DependencyInjection;
using NoneadCSS.Domain.Repositories;
using NoneadCSS.Infrastructure.Persistence;
using NoneadCSS.Infrastructure.Repositories;

namespace NoneadCSS.Tests.Shared;

// 2026-11-21 测试卫生清理：本文件原先定义的同名"影子"类型已全部删除——
// - 空接口 IEmailService / IEmailQueueService / IMailTemplateService / IFolderService /
//   INotificationPublisher 与影子 IUnitOfWork、影子 UnitOfWork 类：
//   全仓 0 处解析到它们（唯一 using NoneadCSS.Tests.Shared 的 EmailQueueServiceTests.cs
//   已用别名/完全限定名绑到生产接口），属纯死代码，留着只会让 mock 静默脱离真实签名；
// - MockFactory（0 处使用）；
// - TestAsyncQueryProvider/TestAsyncEnumerable/TestAsyncEnumerator（与 Helpers\AsyncTestHelpers.cs
//   重复且缺 IAsyncQueryProvider.ExecuteAsync，0 处使用）。
// 真实类型见 NoneadCSS.Domain.IUnitOfWork、NoneadCSS.Infrastructure.UnitOfWork、
// NoneadCSS.Application.Services.*、NoneadCSS.Domain.Events.*；
// 异步 IQueryable/Mock DbSet 统一用 NoneadCSS.Tests.Helpers.TestAsyncQueryProvider。

/// <summary>
/// 构建测试用 ServiceProvider，注册所有仓储供 UnitOfWork 通过 DI 延迟解析。
/// </summary>
internal static class TestServiceProviderBuilder
{
    public static IServiceProvider Build(NoneadCSSDbContext context)
    {
        var services = new ServiceCollection();
        services.AddSingleton(context);
        services.AddMemoryCache();
        services.AddScoped<IUserRepository, UserRepository>();
        services.AddScoped<IConversationRepository, ConversationRepository>();
        services.AddScoped<IMailboxRepository, MailboxRepository>();
        services.AddScoped<ICustomerRepository, CustomerRepository>();
        services.AddScoped<IAttachmentRepository, AttachmentRepository>();
        services.AddScoped<IFolderRepository, FolderRepository>();
        services.AddScoped<IAuditLogRepository, AuditLogRepository>();
        services.AddScoped<IThreadRepository, ThreadRepository>();
        services.AddScoped<ISendLogRepository, SendLogRepository>();
        services.AddScoped<ITagRepository, TagRepository>();
        services.AddScoped<ISystemOptionRepository, SystemOptionRepository>();
        services.AddScoped<INotificationTemplateRepository, NotificationTemplateRepository>();
        services.AddScoped<INotificationRuleRepository, NotificationRuleRepository>();
        services.AddScoped<ICannedResponseRepository, CannedResponseRepository>();
        services.AddScoped<ISatisfactionRatingRepository, SatisfactionRatingRepository>();
        services.AddScoped<IWorkflowRepository, WorkflowRepository>();
        services.AddScoped<ISlaPolicyRepository, SlaPolicyRepository>();
        services.AddScoped<ISlaViolationRepository, SlaViolationRepository>();
        services.AddScoped<IMacroRepository, MacroRepository>();
        services.AddScoped<IMacroExecutionLogRepository, MacroExecutionLogRepository>();
        services.AddScoped<ICustomFieldRepository, CustomFieldRepository>();
        services.AddScoped<ICustomFieldValueRepository, CustomFieldValueRepository>();
        services.AddScoped<IWebhookRepository, WebhookRepository>();
        services.AddScoped<IWebhookLogRepository, WebhookLogRepository>();
        services.AddScoped<IRoleRepository, RoleRepository>();
        services.AddScoped<IUserRoleRepository, UserRoleRepository>();
        services.AddScoped<IMailTemplateRepository, MailTemplateRepository>();
        services.AddScoped<ITeamRepository, TeamRepository>();
        services.AddScoped<ITeamMemberRepository, TeamMemberRepository>();
        services.AddScoped<IEmailSignatureRepository, EmailSignatureRepository>();
        services.AddScoped<IModuleRepository, ModuleRepository>();
        services.AddScoped<IApiKeyRepository, ApiKeyRepository>();
        services.AddScoped<IApiKeyLogRepository, ApiKeyLogRepository>();
        services.AddScoped<IPasswordResetTokenRepository, PasswordResetTokenRepository>();
        services.AddScoped<IRefreshTokenRepository, RefreshTokenRepository>();
        services.AddScoped<ITranslationRepository, TranslationRepository>();
        services.AddScoped<ICalendarEventRepository, CalendarEventRepository>();
        services.AddScoped<IKnowledgeCategoryRepository, KnowledgeCategoryRepository>();
        services.AddScoped<IKnowledgeArticleRepository, KnowledgeArticleRepository>();
        services.AddScoped<IChatSessionRepository, ChatSessionRepository>();
        services.AddScoped<IChatMessageRepository, ChatMessageRepository>();
        services.AddScoped<ITimeEntryRepository, TimeEntryRepository>();
        services.AddScoped<ICustomReportRepository, CustomReportRepository>();
        services.AddScoped<ICustomReportScheduleRepository, CustomReportScheduleRepository>();
        services.AddScoped<IImMessageLogRepository, ImMessageLogRepository>();
        return services.BuildServiceProvider();
    }
}
