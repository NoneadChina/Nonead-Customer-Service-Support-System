using System.Collections.Concurrent;
using NoneadCSS.Domain;
using NoneadCSS.Domain.Entities;
using NoneadCSS.Domain.Repositories;
using NoneadCSS.Infrastructure.Persistence;
using NoneadCSS.Infrastructure.Repositories;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Storage;
using Microsoft.Extensions.DependencyInjection;

namespace NoneadCSS.Infrastructure;

public class EfCoreUnitOfWorkTransaction : IUnitOfWorkTransaction
{
    private readonly IDbContextTransaction _transaction;
    private readonly NoneadCSSDbContext _context;

    public EfCoreUnitOfWorkTransaction(IDbContextTransaction transaction, NoneadCSSDbContext context)
    {
        _transaction = transaction;
        _context = context;
    }

    public async Task CommitAsync()
    {
        await _transaction.CommitAsync();
    }

    /// <summary>
    /// 回滚事务，并同步清空 EF 变更跟踪器。
    ///
    /// 为什么必须清空（生产缺陷 2026-09-21 01:48，会话 613）：
    /// 回滚只作用于数据库，不会回退 EF 的跟踪状态。事务内已 INSERT 并 SaveChanges 的实体，
    /// 在回滚后仍以 Unchanged 状态持有、且保留着已回填的真实主键。而 SQLite 的表级 AUTOINCREMENT
    /// 只防止"提交后删除"的 rowid 复用，**不防止"回滚后"的复用**（sqlite_sequence 随事务一并回退，实测确认）。
    /// 于是同一 DbContext 作用域内下一次 INSERT 拿到被回滚的同一个 rowid，EF 在回填主键时
    /// （StateManager.UpdateIdentityMap → IdentityMap.Add）抛
    /// "The instance of entity type 'X' cannot be tracked because another instance with the same key value
    ///  for {'Id'} is already being tracked"，导致邮件入队静默失败。
    ///
    /// 清空在语义上总是安全的：回滚后数据库已回到事务前状态，跟踪器里 Unchanged 的"已插入"实体
    /// 其行已不存在，Modified/Deleted 的变更也已作废。代价仅是调用方需要重新查询实体。
    /// 调用方若仍需使用实体，请在回滚后重新查询（本仓库既有代码已按此模式处理）。
    /// </summary>
    public async Task RollbackAsync()
    {
        await _transaction.RollbackAsync();
        _context.ChangeTracker.Clear();
    }

    public void Dispose()
    {
        _transaction.Dispose();
    }

    public async ValueTask DisposeAsync()
    {
        await _transaction.DisposeAsync();
    }
}

public class UnitOfWork : IUnitOfWork
{
    private readonly NoneadCSSDbContext _context;
    private readonly IServiceProvider _serviceProvider;

    /// <summary>
    /// 动态创建的泛型仓储缓存，按实体类型缓存实例以避免重复创建。
    /// </summary>
    private readonly ConcurrentDictionary<Type, object> _repositoryCache = new();

    public UnitOfWork(NoneadCSSDbContext context, IServiceProvider serviceProvider)
    {
        _context = context;
        _serviceProvider = serviceProvider;
    }

    private IUserRepository? _users;
    public IUserRepository Users => _users ??= _serviceProvider.GetRequiredService<IUserRepository>();

    private IConversationRepository? _conversations;
    public IConversationRepository Conversations => _conversations ??= _serviceProvider.GetRequiredService<IConversationRepository>();

    private IMailboxRepository? _mailboxes;
    public IMailboxRepository Mailboxes => _mailboxes ??= _serviceProvider.GetRequiredService<IMailboxRepository>();

    private ICustomerRepository? _customers;
    public ICustomerRepository Customers => _customers ??= _serviceProvider.GetRequiredService<ICustomerRepository>();

    private IAttachmentRepository? _attachments;
    public IAttachmentRepository Attachments => _attachments ??= _serviceProvider.GetRequiredService<IAttachmentRepository>();

    private IFolderRepository? _folders;
    public IFolderRepository Folders => _folders ??= _serviceProvider.GetRequiredService<IFolderRepository>();

    private IAuditLogRepository? _auditLogs;
    public IAuditLogRepository AuditLogs => _auditLogs ??= _serviceProvider.GetRequiredService<IAuditLogRepository>();

    private IThreadRepository? _threads;
    public IThreadRepository Threads => _threads ??= _serviceProvider.GetRequiredService<IThreadRepository>();

    private ISendLogRepository? _sendLogs;
    public ISendLogRepository SendLogs => _sendLogs ??= _serviceProvider.GetRequiredService<ISendLogRepository>();

    private ITagRepository? _tags;
    public ITagRepository Tags => _tags ??= _serviceProvider.GetRequiredService<ITagRepository>();

    private ISystemOptionRepository? _systemOptions;
    public ISystemOptionRepository SystemOptions => _systemOptions ??= _serviceProvider.GetRequiredService<ISystemOptionRepository>();

    private INotificationTemplateRepository? _notificationTemplates;
    public INotificationTemplateRepository NotificationTemplates => _notificationTemplates ??= _serviceProvider.GetRequiredService<INotificationTemplateRepository>();

    private INotificationRuleRepository? _notificationRules;
    public INotificationRuleRepository NotificationRules => _notificationRules ??= _serviceProvider.GetRequiredService<INotificationRuleRepository>();

    private IUserNotificationRepository? _userNotifications;
    public IUserNotificationRepository UserNotifications => _userNotifications ??= _serviceProvider.GetRequiredService<IUserNotificationRepository>();

    private ICannedResponseRepository? _cannedResponses;
    public ICannedResponseRepository CannedResponses => _cannedResponses ??= _serviceProvider.GetRequiredService<ICannedResponseRepository>();

    private ISatisfactionRatingRepository? _satisfactionRatings;
    public ISatisfactionRatingRepository SatisfactionRatings => _satisfactionRatings ??= _serviceProvider.GetRequiredService<ISatisfactionRatingRepository>();

    private IWorkflowRepository? _workflows;
    public IWorkflowRepository Workflows => _workflows ??= _serviceProvider.GetRequiredService<IWorkflowRepository>();

    private ISlaPolicyRepository? _slaPolicies;
    public ISlaPolicyRepository SlaPolicies => _slaPolicies ??= _serviceProvider.GetRequiredService<ISlaPolicyRepository>();

    private ISlaViolationRepository? _slaViolations;
    public ISlaViolationRepository SlaViolations => _slaViolations ??= _serviceProvider.GetRequiredService<ISlaViolationRepository>();

    private IMacroRepository? _macros;
    public IMacroRepository Macros => _macros ??= _serviceProvider.GetRequiredService<IMacroRepository>();

    private IMacroExecutionLogRepository? _macroExecutionLogs;
    public IMacroExecutionLogRepository MacroExecutionLogs => _macroExecutionLogs ??= _serviceProvider.GetRequiredService<IMacroExecutionLogRepository>();

    private ICustomFieldRepository? _customFields;
    public ICustomFieldRepository CustomFields => _customFields ??= _serviceProvider.GetRequiredService<ICustomFieldRepository>();

    private ICustomFieldValueRepository? _customFieldValues;
    public ICustomFieldValueRepository CustomFieldValues => _customFieldValues ??= _serviceProvider.GetRequiredService<ICustomFieldValueRepository>();

    private IWebhookRepository? _webhooks;
    public IWebhookRepository Webhooks => _webhooks ??= _serviceProvider.GetRequiredService<IWebhookRepository>();

    private IWebhookLogRepository? _webhookLogs;
    public IWebhookLogRepository WebhookLogs => _webhookLogs ??= _serviceProvider.GetRequiredService<IWebhookLogRepository>();

    private IRoleRepository? _roles;
    public IRoleRepository Roles => _roles ??= _serviceProvider.GetRequiredService<IRoleRepository>();

    private IUserRoleRepository? _userRoles;
    public IUserRoleRepository UserRoles => _userRoles ??= _serviceProvider.GetRequiredService<IUserRoleRepository>();

    private IMailTemplateRepository? _mailTemplates;
    public IMailTemplateRepository MailTemplates => _mailTemplates ??= _serviceProvider.GetRequiredService<IMailTemplateRepository>();

    private ITeamRepository? _teams;
    public ITeamRepository Teams => _teams ??= _serviceProvider.GetRequiredService<ITeamRepository>();

    private ITeamMemberRepository? _teamMembers;
    public ITeamMemberRepository TeamMembers => _teamMembers ??= _serviceProvider.GetRequiredService<ITeamMemberRepository>();

    private IEmailSignatureRepository? _emailSignatures;
    public IEmailSignatureRepository EmailSignatures => _emailSignatures ??= _serviceProvider.GetRequiredService<IEmailSignatureRepository>();

    private IModuleRepository? _modules;
    public IModuleRepository Modules => _modules ??= _serviceProvider.GetRequiredService<IModuleRepository>();

    private IApiKeyRepository? _apiKeys;
    public IApiKeyRepository ApiKeys => _apiKeys ??= _serviceProvider.GetRequiredService<IApiKeyRepository>();

    private IApiKeyLogRepository? _apiKeyLogs;
    public IApiKeyLogRepository ApiKeyLogs => _apiKeyLogs ??= _serviceProvider.GetRequiredService<IApiKeyLogRepository>();

    private IPasswordResetTokenRepository? _passwordResetTokens;
    public IPasswordResetTokenRepository PasswordResetTokens => _passwordResetTokens ??= _serviceProvider.GetRequiredService<IPasswordResetTokenRepository>();

    private IRefreshTokenRepository? _refreshTokens;
    public IRefreshTokenRepository RefreshTokens => _refreshTokens ??= _serviceProvider.GetRequiredService<IRefreshTokenRepository>();

    private ITranslationRepository? _translations;
    public ITranslationRepository Translations => _translations ??= _serviceProvider.GetRequiredService<ITranslationRepository>();

    private ICalendarEventRepository? _calendarEvents;
    public ICalendarEventRepository CalendarEvents => _calendarEvents ??= _serviceProvider.GetRequiredService<ICalendarEventRepository>();

    private IKnowledgeCategoryRepository? _knowledgeCategories;
    public IKnowledgeCategoryRepository KnowledgeCategories => _knowledgeCategories ??= _serviceProvider.GetRequiredService<IKnowledgeCategoryRepository>();

    private IKnowledgeArticleRepository? _knowledgeArticles;
    public IKnowledgeArticleRepository KnowledgeArticles => _knowledgeArticles ??= _serviceProvider.GetRequiredService<IKnowledgeArticleRepository>();

    private IChatSessionRepository? _chatSessions;
    public IChatSessionRepository ChatSessions => _chatSessions ??= _serviceProvider.GetRequiredService<IChatSessionRepository>();

    private IChatMessageRepository? _chatMessages;
    public IChatMessageRepository ChatMessages => _chatMessages ??= _serviceProvider.GetRequiredService<IChatMessageRepository>();

    private ITimeEntryRepository? _timeEntries;
    public ITimeEntryRepository TimeEntries => _timeEntries ??= _serviceProvider.GetRequiredService<ITimeEntryRepository>();

    private ICustomReportRepository? _customReports;
    public ICustomReportRepository CustomReports => _customReports ??= _serviceProvider.GetRequiredService<ICustomReportRepository>();

    private ICustomReportScheduleRepository? _customReportSchedules;
    public ICustomReportScheduleRepository CustomReportSchedules => _customReportSchedules ??= _serviceProvider.GetRequiredService<ICustomReportScheduleRepository>();

    private IImMessageLogRepository? _imMessageLogs;
    public IImMessageLogRepository ImMessageLogs => _imMessageLogs ??= _serviceProvider.GetRequiredService<IImMessageLogRepository>();

    /// <summary>
    /// 动态获取指定实体类型的泛型仓储。
    /// 对于已预定义属性的专用仓储，优先返回专用实例；
    /// 对于仅需基础 CRUD 的简单实体，动态创建泛型仓储并缓存。
    /// </summary>
    /// <typeparam name="T">实体类型</typeparam>
    /// <returns>泛型仓储实例</returns>
    public IRepository<T> Repository<T>() where T : class
    {
        var entityType = typeof(T);

        return (IRepository<T>)_repositoryCache.GetOrAdd(entityType, _ => new Repository<T>(_context));
    }

    public async Task<int> SaveChangesAsync()
    {
        return await _context.SaveChangesAsync();
    }

    /// <inheritdoc/>
    public async Task<int> SaveChangesAsync(CancellationToken cancellationToken)
    {
        return await _context.SaveChangesAsync(cancellationToken);
    }

    public async Task<IUnitOfWorkTransaction> BeginTransactionAsync()
    {
        var transaction = await _context.Database.BeginTransactionAsync();
        return new EfCoreUnitOfWorkTransaction(transaction, _context);
    }

    /// <inheritdoc/>
    public void ClearChanges()
    {
        _context.ChangeTracker.Clear();
    }

    /// <inheritdoc/>
    public void ClearPendingChanges()
    {
        // detach 工作流内部新增/删除/修改的实体，保留外层调用方已跟踪的 Unchanged 实体。
        // 之前仅 detach Added/Deleted 而保留 Modified，会导致事务回滚后工作流对会话的修改
        // （如状态变更）仍留在变更跟踪器中，下一次 SaveChangesAsync 会重新写入已回滚的数据。
        var entriesToDetach = _context.ChangeTracker.Entries()
            .Where(e => e.State == EntityState.Added
                || e.State == EntityState.Deleted
                || e.State == EntityState.Modified)
            .ToList();
        foreach (var entry in entriesToDetach)
        {
            entry.State = EntityState.Detached;
        }
    }
}
