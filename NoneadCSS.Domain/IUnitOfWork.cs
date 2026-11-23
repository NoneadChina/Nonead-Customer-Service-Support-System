using NoneadCSS.Domain.Repositories;

namespace NoneadCSS.Domain;

public interface IUnitOfWorkTransaction : IDisposable, IAsyncDisposable
{
    Task CommitAsync();
    Task RollbackAsync();
}

public interface IUnitOfWork
{
    IUserRepository Users { get; }
    IConversationRepository Conversations { get; }
    IMailboxRepository Mailboxes { get; }
    ICustomerRepository Customers { get; }
    IAttachmentRepository Attachments { get; }
    IFolderRepository Folders { get; }
    IAuditLogRepository AuditLogs { get; }
    IThreadRepository Threads { get; }
    ISendLogRepository SendLogs { get; }
    ITagRepository Tags { get; }
    ISystemOptionRepository SystemOptions { get; }
    INotificationTemplateRepository NotificationTemplates { get; }
    INotificationRuleRepository NotificationRules { get; }
    /// <summary>站内通知（UserNotifications）：通知规则命中后落库的站内信，解决「通知中心只有邮件」。</summary>
    IUserNotificationRepository UserNotifications { get; }
    ICannedResponseRepository CannedResponses { get; }
    ISatisfactionRatingRepository SatisfactionRatings { get; }
    IWorkflowRepository Workflows { get; }
    ISlaPolicyRepository SlaPolicies { get; }
    ISlaViolationRepository SlaViolations { get; }
    IMacroRepository Macros { get; }
    IMacroExecutionLogRepository MacroExecutionLogs { get; }
    ICustomFieldRepository CustomFields { get; }
    ICustomFieldValueRepository CustomFieldValues { get; }
    IWebhookRepository Webhooks { get; }
    IWebhookLogRepository WebhookLogs { get; }
    IRoleRepository Roles { get; }
    IUserRoleRepository UserRoles { get; }
    IMailTemplateRepository MailTemplates { get; }
    ITeamRepository Teams { get; }
    ITeamMemberRepository TeamMembers { get; }
    IEmailSignatureRepository EmailSignatures { get; }
    IModuleRepository Modules { get; }
    IApiKeyRepository ApiKeys { get; }
    IApiKeyLogRepository ApiKeyLogs { get; }
    IPasswordResetTokenRepository PasswordResetTokens { get; }
    IRefreshTokenRepository RefreshTokens { get; }
    ITranslationRepository Translations { get; }
    ICalendarEventRepository CalendarEvents { get; }
    IKnowledgeCategoryRepository KnowledgeCategories { get; }
    IKnowledgeArticleRepository KnowledgeArticles { get; }
    IChatSessionRepository ChatSessions { get; }
    IChatMessageRepository ChatMessages { get; }
    ITimeEntryRepository TimeEntries { get; }
    ICustomReportRepository CustomReports { get; }
    ICustomReportScheduleRepository CustomReportSchedules { get; }
    IImMessageLogRepository ImMessageLogs { get; }

    /// <summary>
    /// 动态获取指定实体类型的泛型仓储，无需在接口中预定义属性。
    /// 适用于仅需基础 CRUD 操作的简单实体，减少接口膨胀。
    /// </summary>
    /// <typeparam name="T">实体类型</typeparam>
    /// <returns>泛型仓储实例</returns>
    IRepository<T> Repository<T>() where T : class;

    Task<int> SaveChangesAsync();

    /// <summary>
    /// 异步保存变更，支持取消令牌以取消网络IO等长时间运行的操作。
    /// </summary>
    /// <param name="cancellationToken">取消令牌</param>
    Task<int> SaveChangesAsync(CancellationToken cancellationToken);

    Task<IUnitOfWorkTransaction> BeginTransactionAsync();

    /// <summary>
    /// 清除变更跟踪器中所有已跟踪实体的状态。
    /// 用于事务回滚后丢弃尚未提交的内存变更，避免后续 SaveChangesAsync 重新写入已回滚的数据。
    /// </summary>
    void ClearChanges();

    /// <summary>
    /// 清除变更跟踪器中处于 Added/Deleted/Modified 状态的实体（即工作流内部新增、删除或修改的实体），
    /// 保留外层调用方已跟踪的 Unchanged 实体，避免副作用影响外层事务。
    /// 用于工作流执行失败后清理工作流自身产生的待持久化实体，防止回滚后的内存修改被下一次 SaveChangesAsync 重新写入。
    /// </summary>
    void ClearPendingChanges();
}
