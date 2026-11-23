using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Storage.ValueConversion;
using NoneadCSS.Domain.Services;
using NoneadCSS.Domain;
using NoneadCSS.Domain.Entities;
using System.Reflection;
using ModuleEntity = NoneadCSS.Domain.Entities.Module;

namespace NoneadCSS.Infrastructure.Persistence;

public class NoneadCSSDbContext : DbContext
{
    private readonly IEncryptionService? _encryptionService;

    public NoneadCSSDbContext(DbContextOptions<NoneadCSSDbContext> options)
        : base(options)
    {
    }

    public NoneadCSSDbContext(DbContextOptions<NoneadCSSDbContext> options, IEncryptionService encryptionService)
        : base(options)
    {
        _encryptionService = encryptionService;
    }

    private ValueConverter<string, string>? _encryptedStringConverter;
    private ValueConverter<string, string> EncryptedStringConverter
    {
        get
        {
            if (_encryptedStringConverter != null)
                return _encryptedStringConverter;

            if (_encryptionService == null)
                throw new InvalidOperationException("加密服务未初始化，无法配置敏感字段加密。请确保 IEncryptionService 已正确注册。");

            _encryptedStringConverter = new ValueConverter<string, string>(
                v => EncryptSafely(_encryptionService, v),
                v => DecryptSafely(_encryptionService, v));
            return _encryptedStringConverter;
        }
    }

    /// <summary>
    /// 加密字符串；若传入值已经是密文（可被成功解密），则原样返回，避免双重加密。
    /// 这样允许调用方在保存前显式加密（如 SetupController），而 EF Core 值转换器不会重复加密。
    /// </summary>
    private static string EncryptSafely(IEncryptionService encryptionService, string plainText)
    {
        if (string.IsNullOrEmpty(plainText))
            return plainText;
        // 已是密文则不再重复加密
        if (encryptionService.TryDecrypt(plainText, out _))
            return plainText;
        return encryptionService.Encrypt(plainText);
    }

    private static string DecryptSafely(IEncryptionService encryptionService, string cipherText)
    {
        if (encryptionService.TryDecrypt(cipherText, out var plainText))
        {
            return plainText ?? string.Empty;
        }
        // S-H1: 解密失败时返回空字符串，而非原始密文，防止 Base64 密文泄露到应用层或 API 响应
        return string.Empty;
    }

    public DbSet<User> Users => Set<User>();
    public DbSet<Conversation> Conversations => Set<Conversation>();
    public DbSet<NoneadCSS.Domain.Entities.ConversationThread> Threads => Set<NoneadCSS.Domain.Entities.ConversationThread>();
    public DbSet<Mailbox> Mailboxes => Set<Mailbox>();
    public DbSet<Customer> Customers => Set<Customer>();
    public DbSet<Email> Emails => Set<Email>();
    public DbSet<Folder> Folders => Set<Folder>();
    public DbSet<Attachment> Attachments => Set<Attachment>();
    public DbSet<Follower> Followers => Set<Follower>();
    public DbSet<ConversationReadState> ConversationReadStates => Set<ConversationReadState>();
    public DbSet<SendLog> SendLogs => Set<SendLog>();
    public DbSet<ConversationFolder> ConversationFolders => Set<ConversationFolder>();
    public DbSet<MailboxUser> MailboxUsers => Set<MailboxUser>();
    public DbSet<AuditLog> AuditLogs => Set<AuditLog>();
    public DbSet<Tag> Tags => Set<Tag>();
    public DbSet<ConversationTag> ConversationTags => Set<ConversationTag>();
    public DbSet<SystemOption> SystemOptions => Set<SystemOption>();
    public DbSet<NotificationTemplate> NotificationTemplates => Set<NotificationTemplate>();
    public DbSet<NotificationRule> NotificationRules => Set<NotificationRule>();
    public DbSet<CannedResponse> CannedResponses => Set<CannedResponse>();
    public DbSet<SatisfactionRating> SatisfactionRatings => Set<SatisfactionRating>();
    public DbSet<Workflow> Workflows => Set<Workflow>();
    public DbSet<WorkflowExecutionLog> WorkflowExecutionLogs => Set<WorkflowExecutionLog>();
    public DbSet<SlaPolicy> SlaPolicies => Set<SlaPolicy>();
    public DbSet<SlaViolation> SlaViolations => Set<SlaViolation>();
    public DbSet<Macro> Macros => Set<Macro>();
    public DbSet<MacroExecutionLog> MacroExecutionLogs => Set<MacroExecutionLog>();
    public DbSet<CustomField> CustomFields => Set<CustomField>();
    public DbSet<CustomFieldValue> CustomFieldValues => Set<CustomFieldValue>();
    public DbSet<Webhook> Webhooks => Set<Webhook>();
    public DbSet<WebhookLog> WebhookLogs => Set<WebhookLog>();
    public DbSet<Role> Roles => Set<Role>();
    public DbSet<UserRole> UserRoles => Set<UserRole>();
    public DbSet<MailTemplate> MailTemplates => Set<MailTemplate>();
    public DbSet<Team> Teams => Set<Team>();
    public DbSet<TeamMember> TeamMembers => Set<TeamMember>();
    public DbSet<EmailSignature> EmailSignatures => Set<EmailSignature>();
    public DbSet<ModuleEntity> Modules => Set<ModuleEntity>();
    public DbSet<ApiKey> ApiKeys => Set<ApiKey>();
    public DbSet<ApiKeyLog> ApiKeyLogs => Set<ApiKeyLog>();
    public DbSet<Star> Stars => Set<Star>();
    public DbSet<PasswordResetToken> PasswordResetTokens => Set<PasswordResetToken>();
    public DbSet<RefreshToken> RefreshTokens => Set<RefreshToken>();
    public DbSet<Translation> Translations => Set<Translation>();
    public DbSet<CalendarEvent> CalendarEvents => Set<CalendarEvent>();
    public DbSet<CalendarEventTemplate> CalendarEventTemplates => Set<CalendarEventTemplate>();
    public DbSet<CalendarReminderAttempt> CalendarReminderAttempts => Set<CalendarReminderAttempt>();
    public DbSet<KnowledgeCategory> KnowledgeCategories => Set<KnowledgeCategory>();
    public DbSet<KnowledgeArticle> KnowledgeArticles => Set<KnowledgeArticle>();
    public DbSet<KnowledgeArticleAttachment> KnowledgeArticleAttachments => Set<KnowledgeArticleAttachment>();
    public DbSet<ChatSession> ChatSessions => Set<ChatSession>();
    public DbSet<ChatMessage> ChatMessages => Set<ChatMessage>();
    public DbSet<TimeEntry> TimeEntries => Set<TimeEntry>();
    public DbSet<UserNotification> UserNotifications => Set<UserNotification>();
    public DbSet<OutlookSyncSetting> OutlookSyncSettings => Set<OutlookSyncSetting>();
    public DbSet<OutlookEventMapping> OutlookEventMappings => Set<OutlookEventMapping>();
    public DbSet<OutlookSyncLog> OutlookSyncLogs => Set<OutlookSyncLog>();
    public DbSet<OutlookSyncQueueItem> OutlookSyncQueueItems => Set<OutlookSyncQueueItem>();
    public DbSet<DistributedLock> DistributedLocks => Set<DistributedLock>();
    public DbSet<CustomReport> CustomReports => Set<CustomReport>();
    public DbSet<CustomReportSchedule> CustomReportSchedules => Set<CustomReportSchedule>();
    public DbSet<ImMessageLog> ImMessageLogs => Set<ImMessageLog>();

    protected override void OnConfiguring(DbContextOptionsBuilder optionsBuilder)
    {
        // EF Core 10 在 MigrateAsync 时会校验模型快照与当前模型是否一致。
        // 本项目存在"先手动补丁应用 schema、后通过 dotnet ef 生成正式迁移"的开发流程，
        // 加上 SQLite 对事务内 DDL 语句（如 PRAGMA foreign_keys=0）的限制，
        // 每次启动都可能触发 PendingModelChangesWarning。
        // 策略：完全 Ignore 该事件（不抛异常、不输出 ERR/WRN 日志）。
        // 真正的 schema 一致性由 Program.cs 中的 FixMigrationHistoryAsync、
        // 幂等补丁方法（Ensure*ColumnsAsync/Ensure*IndexesAsync）以及每次 dotnet ef migrations add
        // 生成的最新快照共同保障。
        optionsBuilder.ConfigureWarnings(w =>
        {
            w.Ignore(Microsoft.EntityFrameworkCore.Diagnostics.RelationalEventId.PendingModelChangesWarning);
        });
        base.OnConfiguring(optionsBuilder);
    }

    public override async Task<int> SaveChangesAsync(CancellationToken cancellationToken = default)
    {
        // 会话编号（Conversation.Number）采用 "Max(Number)+1" 生成，存在并发竞态：
        // 两个并发请求可能读到相同 maxNumber、计算出相同 nextNumber，导致第二个 INSERT 触发
        // Number 唯一索引（过滤已软删除记录）冲突，该请求的邮件/聊天转工单处理失败。
        // 此处对会话编号唯一约束冲突实现指数退避重试（50/100/200ms，最多 3 次）：
        // 重新查询 Max(Number)+1 并刷新待新增会话的编号后重试整个 SaveChanges。
        // 重试耗尽仍失败则抛出原始异常。详见 ConversationRepository.GetNextNumberAsync 的并发风险说明。
        const int maxRetries = 3;
        int[] backoffMs = { 50, 100, 200 };

        for (var attempt = 0; ; attempt++)
        {
            ApplySoftDelete();
            UpdateSearchVectors();
            try
            {
                return await base.SaveChangesAsync(cancellationToken);
            }
            catch (DbUpdateException ex) when (attempt < maxRetries && IsConversationNumberConflict(ex))
            {
                // 并发竞态：会话编号唯一约束冲突，重新分配编号后重试。
                await ReassignAddedConversationNumbersAsync(cancellationToken);
                await Task.Delay(backoffMs[attempt], cancellationToken);
            }
        }
    }

    public override int SaveChanges()
    {
        ApplySoftDelete();
        UpdateSearchVectors();
        return base.SaveChanges();
    }

    /// <summary>
    /// 判断异常是否为会话编号（Conversation.Number）唯一约束冲突。
    /// Conversation 仅在 Number 上建有唯一索引（过滤 DeletedAt IS NULL），
    /// 因此 Added 状态的 Conversation 发生唯一约束冲突即为 Number 冲突，可安全重试。
    /// 其它实体的唯一约束冲突（如 DistributedLock）不在此处理，原样抛出。
    /// </summary>
    private static bool IsConversationNumberConflict(DbUpdateException ex)
    {
        var entries = ex.Entries;
        if (entries == null)
            return false;

        foreach (var entry in entries)
        {
            if (entry.Entity is Conversation)
                return true;
        }
        return false;
    }

    /// <summary>
    /// 为所有处于 Added 状态的会话重新分配不冲突的编号（基于当前数据库 Max(Number) 递增）。
    /// 用于唯一约束冲突重试前刷新编号，避免再次命中已冲突的值。
    /// 与 ConversationRepository.GetNextNumberAsync 的计算口径保持一致。
    /// </summary>
    private async Task ReassignAddedConversationNumbersAsync(CancellationToken cancellationToken)
    {
        var addedConversations = ChangeTracker.Entries<Conversation>()
            .Where(e => e.State == EntityState.Added)
            .Select(e => e.Entity)
            .ToList();

        if (addedConversations.Count == 0)
            return;

        // 重新查询当前最大编号（仅未软删除记录）。失败的 INSERT 已在语句级回滚，不会污染此查询。
        var maxNumber = await Conversations
            .AsNoTracking()
            .Where(c => c.DeletedAt == null)
            .MaxAsync(c => (int?)c.Number, cancellationToken) ?? 0;

        foreach (var conversation in addedConversations)
        {
            conversation.Number = ++maxNumber;
        }
    }

    private void UpdateSearchVectors()
    {
        var conversations = ChangeTracker.Entries<Conversation>()
            .Where(e => e.State == EntityState.Added || e.State == EntityState.Modified)
            .Select(e => e.Entity)
            .ToList();

        foreach (var conversation in conversations)
        {
            // 确保 Customer 导航属性已加载，使 SearchVector 包含客户姓名
            if (conversation.CustomerId.HasValue && conversation.Customer == null)
            {
                Entry(conversation).Reference(c => c.Customer).Load();
            }
            conversation.UpdateSearchVector();
        }
    }

    private void ApplySoftDelete()
    {
        // 收集所有当前标记为 Deleted 的实体（包括因级联而被标记为 Deleted 的子实体）
        var allDeletedEntries = ChangeTracker.Entries()
            .Where(e => e.State == EntityState.Deleted)
            .ToList();

        // 分类：
        // 1. ISoftDelete 且 DeletedAt == null：首次软删除（Modified + DeletedAt = now）
        // 2. ISoftDelete 且 DeletedAt != null：已软删除，执行物理删除（保持 Deleted 状态）
        //    修复：之前所有 ISoftDelete 实体的 Remove 都被转为软删除，导致 HardDelete/ClearTrash 无效
        // 3. 非 ISoftDelete：物理删除（保持 Deleted 状态）
        var softDeleteEntries = allDeletedEntries
            .Where(e => e.Entity is ISoftDelete && ((ISoftDelete)e.Entity).DeletedAt == null)
            .ToList();

        var physicalDeleteEntries = allDeletedEntries
            .Where(e => !(e.Entity is ISoftDelete && ((ISoftDelete)e.Entity).DeletedAt == null))
            .ToList();

        // 先将未软删除的 ISoftDelete 实体改为 Modified + DeletedAt
        // 注意：此操作会触发 EF Core 重新评估级联规则，可能取消级联子实体的 Deleted 状态
        foreach (var entry in softDeleteEntries)
        {
            entry.State = EntityState.Modified;
            ((ISoftDelete)entry.Entity).DeletedAt = DateTime.UtcNow;
        }

        // 重新将需要物理删除的实体（非 ISoftDelete 或已软删除的 ISoftDelete）标记为 Deleted
        // 确保它们能够被物理删除，避免成为孤儿记录
        foreach (var entry in physicalDeleteEntries)
        {
            if (entry.State != EntityState.Deleted)
            {
                entry.State = EntityState.Deleted;
            }
        }
    }

    private static void ApplyGlobalSoftDeleteFilters(ModelBuilder modelBuilder)
    {
        var softDeleteTypes = new List<Type>();

        // 添加当前程序集
        softDeleteTypes.AddRange(
            Assembly.GetExecutingAssembly()
                .GetTypes()
                .Where(t => typeof(ISoftDelete).IsAssignableFrom(t) && t.IsClass && !t.IsAbstract)
        );

        // 安全地加载引用程序集（某些程序集可能在测试环境中不可用）
        foreach (var assemblyName in Assembly.GetExecutingAssembly().GetReferencedAssemblies())
        {
            try
            {
                var assembly = Assembly.Load(assemblyName);
                softDeleteTypes.AddRange(
                    assembly.GetTypes()
                        .Where(t => typeof(ISoftDelete).IsAssignableFrom(t) && t.IsClass && !t.IsAbstract)
                );
            }
            catch (FileNotFoundException)
            {
                // 忽略无法加载的程序集（如测试环境中的 nClam）
            }
            catch (ReflectionTypeLoadException)
            {
                // 忽略类型加载异常
            }
        }

        foreach (var type in softDeleteTypes.Distinct())
        {
            modelBuilder.Entity(type)
                .HasQueryFilter(BuildSoftDeleteFilter(type));
        }
    }

    private static System.Linq.Expressions.LambdaExpression BuildSoftDeleteFilter(Type entityType)
    {
        var parameter = System.Linq.Expressions.Expression.Parameter(entityType, "e");
        var property = System.Linq.Expressions.Expression.Property(parameter, "DeletedAt");
        var nullConstant = System.Linq.Expressions.Expression.Constant(null, typeof(DateTime?));
        var condition = System.Linq.Expressions.Expression.Equal(property, nullConstant);
        return System.Linq.Expressions.Expression.Lambda(condition, parameter);
    }

    protected override void OnModelCreating(ModelBuilder modelBuilder)
    {
        ApplyGlobalSoftDeleteFilters(modelBuilder);

        modelBuilder.Entity<MailboxUser>()
            .HasKey(mu => new { mu.MailboxId, mu.UserId });

        modelBuilder.Entity<ConversationFolder>()
            .HasKey(cf => new { cf.ConversationId, cf.FolderId });

        modelBuilder.Entity<ConversationFolder>()
            .HasOne(cf => cf.Conversation)
            .WithMany(c => c.ConversationFolders)
            .HasForeignKey(cf => cf.ConversationId)
            .OnDelete(DeleteBehavior.Cascade)
            .IsRequired(false);

        // Folder 是软删除实体，配置为 Optional 避免全局查询过滤器警告
        modelBuilder.Entity<ConversationFolder>()
            .HasOne(cf => cf.Folder)
            .WithMany(f => f.ConversationFolders)
            .HasForeignKey(cf => cf.FolderId)
            .OnDelete(DeleteBehavior.Cascade)
            .IsRequired(false);

        modelBuilder.Entity<User>()
            .HasIndex(u => u.Email)
            .IsUnique()
            .HasFilter("\"DeletedAt\" IS NULL");

        // H-P3-7a: User 乐观锁（登录限流并发写 FailedLoginAttempts/LockoutEnd、资料编辑、权限/通知设置）
        modelBuilder.Entity<User>()
            .Property(u => u.RowVersion)
            .IsRowVersion()
            .ValueGeneratedOnAddOrUpdate();

        modelBuilder.Entity<Mailbox>()
            .HasIndex(m => m.Email)
            .IsUnique()
            .HasFilter("\"DeletedAt\" IS NULL");

        if (_encryptionService != null)
        {
            modelBuilder.Entity<Mailbox>()
                .Property(m => m.InPassword)
                .HasConversion(EncryptedStringConverter);

            modelBuilder.Entity<Mailbox>()
                .Property(m => m.OutPassword)
                .HasConversion(EncryptedStringConverter);

            modelBuilder.Entity<Mailbox>()
                .Property(m => m.OauthAccessToken)
                .HasConversion(EncryptedStringConverter);

            modelBuilder.Entity<Mailbox>()
                .Property(m => m.OauthRefreshToken)
                .HasConversion(EncryptedStringConverter);

            modelBuilder.Entity<Webhook>()
                .Property(w => w.Secret)
#pragma warning disable CS8620 // EF Core ValueConverter 可空引用类型标注已知限制
                .HasConversion(EncryptedStringConverter);
#pragma warning restore CS8620
        }

        modelBuilder.Entity<Customer>()
            .HasMany(c => c.Emails)
            .WithOne(e => e.Customer)
            .HasForeignKey(e => e.CustomerId)
            .OnDelete(DeleteBehavior.Cascade)
            .IsRequired(false);

        // Customer (Channel, ChannelId) 索引：IM 渠道接入时 GetOrCreateByChannelAsync 高频查询
        modelBuilder.Entity<Customer>()
            .HasIndex(c => new { c.Channel, c.ChannelId })
            .HasFilter("\"DeletedAt\" IS NULL");

        // Customer CreatedAt 索引：SearchAsync 排序、CountCreatedInDateRangeAsync 范围过滤
        modelBuilder.Entity<Customer>()
            .HasIndex(c => c.CreatedAt)
            .HasFilter("\"DeletedAt\" IS NULL");

        // H-P3-7a: Customer 乐观锁（GetOrCreateByEmail/Channel 并发、资料合并、渠道画像回写、客服批量打标签）
        modelBuilder.Entity<Customer>()
            .Property(c => c.RowVersion)
            .IsRowVersion()
            .ValueGeneratedOnAddOrUpdate();

        // Email.EmailAddress 唯一索引：防止并发场景下创建重复客户
        // 配合 GetOrCreateByEmailAsync 事务使用，在 DB 层面保证邮箱地址唯一性
        modelBuilder.Entity<Email>()
            .HasIndex(e => e.EmailAddress)
            .IsUnique();

        modelBuilder.Entity<Conversation>()
            .HasMany(c => c.Threads)
            .WithOne(t => t.Conversation)
            .HasForeignKey(t => t.ConversationId)
            .OnDelete(DeleteBehavior.Cascade)
            .IsRequired(false);

        // Conversation 的用户关系配置（显式指定反向属性避免 shadow 外键）
        modelBuilder.Entity<Conversation>()
            .HasOne(c => c.User)
            .WithMany(u => u.Conversations)
            .HasForeignKey(c => c.UserId)
            .OnDelete(DeleteBehavior.SetNull)
            .IsRequired(false);

        modelBuilder.Entity<Conversation>()
            .HasOne(c => c.Customer)
            .WithMany(cu => cu.Conversations)
            .HasForeignKey(c => c.CustomerId)
            .OnDelete(DeleteBehavior.SetNull)
            .IsRequired(false);

        modelBuilder.Entity<Conversation>()
            .HasOne(c => c.CreatedByUser)
            .WithMany()
            .HasForeignKey(c => c.CreatedByUserId)
            .OnDelete(DeleteBehavior.SetNull)
            .IsRequired(false);

        modelBuilder.Entity<Conversation>()
            .HasOne(c => c.ClosedByUser)
            .WithMany()
            .HasForeignKey(c => c.ClosedByUserId)
            .OnDelete(DeleteBehavior.SetNull)
            .IsRequired(false);

        modelBuilder.Entity<Conversation>()
            .HasIndex(c => new { c.MailboxId, c.Status, c.LastReplyAt })
            .HasFilter("\"DeletedAt\" IS NULL");

        modelBuilder.Entity<Conversation>()
            .HasIndex(c => new { c.MailboxId, c.UserId })
            .HasFilter("\"DeletedAt\" IS NULL");

        modelBuilder.Entity<Conversation>()
            .HasIndex(c => new { c.FolderId, c.Status })
            .HasFilter("\"DeletedAt\" IS NULL");

        modelBuilder.Entity<Conversation>()
            .HasIndex(c => new { c.Status, c.Priority, c.LastReplyAt })
            .HasFilter("\"DeletedAt\" IS NULL");

        // 复合索引：支撑日期范围聚合查询（报表中的新建/关闭统计）
        modelBuilder.Entity<Conversation>()
            .HasIndex(c => new { c.MailboxId, c.CreatedAt })
            .HasFilter("\"DeletedAt\" IS NULL");

        modelBuilder.Entity<Conversation>()
            .HasIndex(c => new { c.MailboxId, c.ClosedAt })
            .HasFilter("\"DeletedAt\" IS NULL");

        modelBuilder.Entity<Conversation>()
            .HasIndex(c => new { c.UserId, c.Status })
            .HasFilter("\"DeletedAt\" IS NULL");

        modelBuilder.Entity<Conversation>()
            .HasIndex(c => new { c.CustomerEmail })
            .HasFilter("\"DeletedAt\" IS NULL");

        modelBuilder.Entity<Conversation>()
            .HasIndex(c => c.CustomerId)
            .HasFilter("\"DeletedAt\" IS NULL");

        // 渠道过滤索引：支撑按 SourceType (Email/WeChat/Feishu/WhatsApp/DingTalk/Chat 等) 过滤并配合 LastReplyAt 排序
        modelBuilder.Entity<Conversation>()
            .HasIndex(c => new { c.SourceType, c.Status, c.LastReplyAt })
            .HasFilter("\"DeletedAt\" IS NULL");

        modelBuilder.Entity<Conversation>()
            .HasIndex(c => c.Number)
            .IsUnique()
            .HasFilter("\"DeletedAt\" IS NULL");

        // SQLite 无原生 rowversion：见 VersionPropertyMappingExtensions 的说明（空库 EnsureCreated 必须带默认值）
        modelBuilder.Entity<Conversation>()
            .Property(c => c.Version)
            .ApplyVersionMapping(this.IsSqliteProvider());

        modelBuilder.Entity<Conversation>()
            .Property(c => c.SearchVector)
            .HasMaxLength(2000);

        modelBuilder.Entity<ConversationThread>()
            .HasOne(t => t.User)
            .WithMany()
            .HasForeignKey(t => t.UserId)
            .OnDelete(DeleteBehavior.SetNull)
            .IsRequired(false);

        modelBuilder.Entity<ConversationThread>()
            .HasOne(t => t.Customer)
            .WithMany()
            .HasForeignKey(t => t.CustomerId)
            .OnDelete(DeleteBehavior.SetNull)
            .IsRequired(false);

        modelBuilder.Entity<ConversationThread>()
            .HasOne(t => t.CreatedByUser)
            .WithMany()
            .HasForeignKey(t => t.CreatedByUserId)
            .OnDelete(DeleteBehavior.SetNull)
            .IsRequired(false);

        modelBuilder.Entity<ConversationThread>()
            .HasMany(t => t.Attachments)
            .WithOne(a => a.Thread)
            .HasForeignKey(a => a.ThreadId)
            .OnDelete(DeleteBehavior.Cascade);

        modelBuilder.Entity<ConversationThread>()
            .HasIndex(t => t.ConversationId);

        modelBuilder.Entity<ConversationThread>()
            .HasIndex(t => t.Type);

        modelBuilder.Entity<ConversationThread>()
            .HasIndex(t => t.CreatedAt);

        modelBuilder.Entity<ConversationThread>()
            .HasIndex(t => t.UserId);

        modelBuilder.Entity<ConversationThread>()
            .HasIndex(t => t.CustomerId);

        // 复合索引：支撑按会话 ID + 类型 + 创建时间 的报表/查询
        modelBuilder.Entity<ConversationThread>()
            .HasIndex(t => new { t.ConversationId, t.Type, t.CreatedAt });

        // 复合索引：支撑按类型 + 创建时间 + 用户 的报表/聚合查询
        modelBuilder.Entity<ConversationThread>()
            .HasIndex(t => new { t.Type, t.CreatedAt, t.UserId });

        // 复合索引：支撑按 ConversationId + CreatedAt 排序查询
        modelBuilder.Entity<ConversationThread>()
            .HasIndex(t => new { t.ConversationId, t.CreatedAt });

        modelBuilder.Entity<ConversationThread>()
            .Property(t => t.Version)
            .ApplyVersionMapping(this.IsSqliteProvider());

        // 注意：ConversationThread.User 不再使用 AutoInclude。
        // 原因：AutoInclude 会在所有 Thread 查询（含统计、批量、内部查找）上强制 JOIN Users 表，
        // 造成不必要的性能开销。已在 ThreadRepository 的读取方法中按需添加 .Include(t => t.User)。
        modelBuilder.Entity<Follower>()
            .HasIndex(f => new { f.ConversationId, f.UserId })
            .IsUnique();

        modelBuilder.Entity<Follower>()
            .HasOne(f => f.Conversation)
            .WithMany(c => c.Followers)
            .HasForeignKey(f => f.ConversationId)
            .OnDelete(DeleteBehavior.Cascade)
            .IsRequired(false);

        // User 是软删除实体，配置为 Optional 避免全局查询过滤器警告
        modelBuilder.Entity<Follower>()
            .HasOne(f => f.User)
            .WithMany(u => u.Followers)
            .HasForeignKey(f => f.UserId)
            .OnDelete(DeleteBehavior.Cascade)
            .IsRequired(false);

        modelBuilder.Entity<Mailbox>()
            .HasMany(m => m.Conversations)
            .WithOne(c => c.Mailbox)
            .HasForeignKey(c => c.MailboxId)
            .OnDelete(DeleteBehavior.Cascade);

        modelBuilder.Entity<Mailbox>()
            .HasMany(m => m.Folders)
            .WithOne(f => f.Mailbox)
            .HasForeignKey(f => f.MailboxId)
            .OnDelete(DeleteBehavior.Cascade);

        modelBuilder.Entity<User>()
            .HasMany(u => u.MailboxUsers)
            .WithOne(mu => mu.User)
            .HasForeignKey(mu => mu.UserId)
            .OnDelete(DeleteBehavior.Cascade)
            .IsRequired(false);

        modelBuilder.Entity<Mailbox>()
            .HasMany(m => m.MailboxUsers)
            .WithOne(mu => mu.Mailbox)
            .HasForeignKey(mu => mu.MailboxId)
            .OnDelete(DeleteBehavior.Cascade)
            .IsRequired(false);

        modelBuilder.Entity<Mailbox>()
            .Property(m => m.Version)
            .ApplyVersionMapping(this.IsSqliteProvider());

        modelBuilder.Entity<AuditLog>()
            .HasIndex(a => a.UserId);

        modelBuilder.Entity<AuditLog>()
            .HasIndex(a => new { a.EntityType, a.EntityId });

        modelBuilder.Entity<AuditLog>()
            .HasIndex(a => a.CreatedAt);

        // HMAC 签名固定为 Base64 编码的 SHA-256 哈希（44 字符），设置 MaxLength=88 留余量
        modelBuilder.Entity<AuditLog>()
            .Property(a => a.HmacSignature)
            .HasMaxLength(88);

        // 审计日志与用户的关联：删除用户时不级联删除审计记录，保留历史审计数据
        // 标记为 Optional：避免 EF Core 全局查询过滤器警告（用户软删除后 Required 关系产生矛盾）
        modelBuilder.Entity<AuditLog>()
            .HasOne(a => a.User)
            .WithMany()
            .HasForeignKey(a => a.UserId)
            .OnDelete(DeleteBehavior.SetNull)
            .IsRequired(false);

        modelBuilder.Entity<ConversationTag>()
            .HasKey(ct => new { ct.ConversationId, ct.TagId });

        modelBuilder.Entity<Conversation>()
            .HasMany(c => c.ConversationTags)
            .WithOne(ct => ct.Conversation)
            .HasForeignKey(ct => ct.ConversationId)
            .OnDelete(DeleteBehavior.Cascade)
            .IsRequired(false);

        modelBuilder.Entity<Tag>()
            .HasMany(t => t.ConversationTags)
            .WithOne(ct => ct.Tag)
            .HasForeignKey(ct => ct.TagId)
            .OnDelete(DeleteBehavior.Cascade)
            .IsRequired(false);

        modelBuilder.Entity<Tag>()
            .HasIndex(t => new { t.MailboxId, t.Name })
            .IsUnique()
            .HasFilter("\"DeletedAt\" IS NULL");

        modelBuilder.Entity<SystemOption>()
            .HasIndex(o => o.Name)
            .IsUnique();

        modelBuilder.Entity<NotificationTemplate>()
            .HasIndex(t => t.EventType);

        modelBuilder.Entity<NotificationRule>()
            .HasIndex(r => r.EventType);

        // 高频查询路径复合索引：DispatchRulesAsync(GetActiveByEventTypeAsync) / GetPagedRulesAsync
        //   过滤条件：IsActive=true + EventType = ? + (MailboxId=? OR MailboxId IS NULL) + UserId=?/NULL + DeletedAt IS NULL
        //   覆盖：按事件类型取全局/邮箱级激活规则的主路径
        modelBuilder.Entity<NotificationRule>()
            .HasIndex(r => new { r.EventType, r.IsActive, r.MailboxId, r.UserId, r.DeletedAt })
            .HasDatabaseName("IX_NotificationRules_EventType_Active_Mailbox_User_Deleted");

        // 按邮箱维度列出通知规则（GetByMailboxIdAsync / 通知规则列表筛选 MailboxId）
        modelBuilder.Entity<NotificationRule>()
            .HasIndex(r => new { r.MailboxId, r.IsActive, r.DeletedAt })
            .HasDatabaseName("IX_NotificationRules_MailboxId_Active_Deleted");

        // 按用户维度列出个人通知规则（GetPagedRulesAsync 传 userId）
        modelBuilder.Entity<NotificationRule>()
            .HasIndex(r => new { r.UserId, r.IsActive, r.DeletedAt })
            .HasDatabaseName("IX_NotificationRules_UserId_Active_Deleted");

        // 模板外键索引（GetPagedRulesAsync / DuplicateRuleAsync 会走 TemplateId 关联查询），
        // 显式声明避免部分数据库未为 FK 自动建索引
        modelBuilder.Entity<NotificationRule>()
            .HasIndex(r => r.TemplateId)
            .HasDatabaseName("IX_NotificationRules_TemplateId");

        modelBuilder.Entity<NotificationRule>()
            .HasOne(r => r.Template)
            .WithMany()
            .HasForeignKey(r => r.TemplateId)
            .OnDelete(DeleteBehavior.SetNull)
            .IsRequired(false);

        // M8: NotificationRule 乐观锁（规则更新/启用停用并发写）
        modelBuilder.Entity<NotificationRule>()
            .Property(r => r.RowVersion)
            .IsRowVersion()
            .ValueGeneratedOnAddOrUpdate();

        modelBuilder.Entity<CannedResponse>()
            .HasIndex(c => new { c.MailboxId, c.Name });

        modelBuilder.Entity<CannedResponse>()
            .HasIndex(c => c.Category);

        modelBuilder.Entity<CannedResponse>()
            .HasIndex(c => c.IsShared);

        modelBuilder.Entity<CannedResponse>()
            .HasOne(c => c.Mailbox)
            .WithMany()
            .HasForeignKey(c => c.MailboxId)
            .OnDelete(DeleteBehavior.Cascade);

        modelBuilder.Entity<CannedResponse>()
            .HasOne(c => c.CreatedByUser)
            .WithMany()
            .HasForeignKey(c => c.CreatedByUserId)
            .OnDelete(DeleteBehavior.SetNull)
            .IsRequired(false);

        modelBuilder.Entity<SatisfactionRating>()
            .HasIndex(r => r.ConversationId)
            .IsUnique();

        modelBuilder.Entity<SatisfactionRating>()
            .HasIndex(r => r.Rating);

        modelBuilder.Entity<SatisfactionRating>()
            .HasIndex(r => r.CreatedAt);

        modelBuilder.Entity<SatisfactionRating>()
            .HasIndex(r => r.UserId);

        modelBuilder.Entity<SatisfactionRating>()
            .HasOne(r => r.Conversation)
            .WithMany(c => c.SatisfactionRatings)
            .HasForeignKey(r => r.ConversationId)
            .OnDelete(DeleteBehavior.Cascade)
            .IsRequired(false);

        modelBuilder.Entity<SatisfactionRating>()
            .HasOne(r => r.Customer)
            .WithMany()
            .HasForeignKey(r => r.CustomerId)
            .OnDelete(DeleteBehavior.Cascade)
            .IsRequired(false);

        modelBuilder.Entity<SatisfactionRating>()
            .HasOne(r => r.User)
            .WithMany()
            .HasForeignKey(r => r.UserId)
            .OnDelete(DeleteBehavior.SetNull)
            .IsRequired(false);

        // 工作流规则
        modelBuilder.Entity<Workflow>()
            .HasIndex(w => new { w.IsActive, w.TriggerType });

        modelBuilder.Entity<Workflow>()
            .HasIndex(w => w.MailboxId);

        modelBuilder.Entity<Workflow>()
            .HasIndex(w => w.SortOrder);

        modelBuilder.Entity<Workflow>()
            .Property(w => w.Name)
            .IsRequired()
            .HasMaxLength(100);

        modelBuilder.Entity<Workflow>()
            .Property(w => w.TriggerType)
            .IsRequired()
            .HasMaxLength(50);

        modelBuilder.Entity<Workflow>()
            .Property(w => w.ConditionsJson)
            .HasMaxLength(8000);

        modelBuilder.Entity<Workflow>()
            .Property(w => w.ActionsJson)
            .HasMaxLength(8000);

        modelBuilder.Entity<Workflow>()
            .HasOne(w => w.Mailbox)
            .WithMany()
            .HasForeignKey(w => w.MailboxId)
            .OnDelete(DeleteBehavior.SetNull)
            .IsRequired(false);

        // 工作流执行日志
        modelBuilder.Entity<WorkflowExecutionLog>()
            .HasIndex(l => l.WorkflowId);

        modelBuilder.Entity<WorkflowExecutionLog>()
            .HasIndex(l => l.ConversationId);

        modelBuilder.Entity<WorkflowExecutionLog>()
            .HasIndex(l => l.ExecutedAt);

        modelBuilder.Entity<WorkflowExecutionLog>()
            .Property(l => l.Status)
            .IsRequired()
            .HasMaxLength(20);

        modelBuilder.Entity<WorkflowExecutionLog>()
            .HasOne(l => l.Workflow)
            .WithMany()
            .HasForeignKey(l => l.WorkflowId)
            .OnDelete(DeleteBehavior.Cascade)
            .IsRequired(false);

        // 工作流执行日志属于审计数据，删除会话时不级联删除，保留历史记录
        // 配置为 Optional 关系，避免 EF Core 全局查询过滤器警告
        modelBuilder.Entity<WorkflowExecutionLog>()
            .HasOne(l => l.Conversation)
            .WithMany()
            .HasForeignKey(l => l.ConversationId)
            .OnDelete(DeleteBehavior.Restrict)
            .IsRequired(false);

        // SLA 策略
        modelBuilder.Entity<SlaPolicy>()
            .HasIndex(p => new { p.IsActive, p.MailboxId });

        modelBuilder.Entity<SlaPolicy>()
            .HasIndex(p => p.Priority);

        modelBuilder.Entity<SlaPolicy>()
            .HasIndex(p => p.SortOrder);

        modelBuilder.Entity<SlaPolicy>()
            .Property(p => p.Name)
            .IsRequired()
            .HasMaxLength(100);

        modelBuilder.Entity<SlaPolicy>()
            .Property(p => p.FirstResponseHours)
            .HasDefaultValue(24.0);

        modelBuilder.Entity<SlaPolicy>()
            .Property(p => p.ResolutionHours)
            .HasDefaultValue(72.0);

        modelBuilder.Entity<SlaPolicy>()
            .HasOne(p => p.Mailbox)
            .WithMany()
            .HasForeignKey(p => p.MailboxId)
            .OnDelete(DeleteBehavior.SetNull)
            .IsRequired(false);

        // SLA 违规
        modelBuilder.Entity<SlaViolation>()
            .HasIndex(v => v.SlaPolicyId);

        modelBuilder.Entity<SlaViolation>()
            .HasIndex(v => v.ConversationId);

        modelBuilder.Entity<SlaViolation>()
            .HasIndex(v => v.Status);

        modelBuilder.Entity<SlaViolation>()
            .HasIndex(v => v.DueAt);

        modelBuilder.Entity<SlaViolation>()
            .HasIndex(v => new { v.ConversationId, v.ViolationType });

        modelBuilder.Entity<SlaViolation>()
            .Property(v => v.ViolationType)
            .IsRequired()
            .HasMaxLength(20);

        modelBuilder.Entity<SlaViolation>()
            .Property(v => v.Status)
            .IsRequired()
            .HasMaxLength(20);

        modelBuilder.Entity<SlaViolation>()
            .HasOne(v => v.SlaPolicy)
            .WithMany()
            .HasForeignKey(v => v.SlaPolicyId)
            .OnDelete(DeleteBehavior.Cascade)
            .IsRequired(false);

        // SLA 违规记录属于审计数据，删除会话时不级联删除，保留历史记录
        // 配置为 Optional 关系，避免 EF Core 全局查询过滤器警告
        modelBuilder.Entity<SlaViolation>()
            .HasOne(v => v.Conversation)
            .WithMany()
            .HasForeignKey(v => v.ConversationId)
            .OnDelete(DeleteBehavior.Restrict)
            .IsRequired(false);

        // 宏
        modelBuilder.Entity<Macro>()
            .HasIndex(m => new { m.IsActive, m.MailboxId });

        modelBuilder.Entity<Macro>()
            .HasIndex(m => m.SortOrder);

        modelBuilder.Entity<Macro>()
            .HasIndex(m => m.UsageCount);

        modelBuilder.Entity<Macro>()
            .Property(m => m.Name)
            .IsRequired()
            .HasMaxLength(100);

        modelBuilder.Entity<Macro>()
            .Property(m => m.ActionsJson)
            .HasDefaultValue("[]")
            .HasMaxLength(8000);

        modelBuilder.Entity<Macro>()
            .HasOne(m => m.Mailbox)
            .WithMany()
            .HasForeignKey(m => m.MailboxId)
            .OnDelete(DeleteBehavior.SetNull)
            .IsRequired(false);

        // 宏执行日志
        modelBuilder.Entity<MacroExecutionLog>()
            .HasIndex(l => l.MacroId);

        modelBuilder.Entity<MacroExecutionLog>()
            .HasIndex(l => l.ConversationId);

        modelBuilder.Entity<MacroExecutionLog>()
            .HasIndex(l => l.ExecutedByUserId);

        modelBuilder.Entity<MacroExecutionLog>()
            .HasIndex(l => l.ExecutedAt);

        modelBuilder.Entity<MacroExecutionLog>()
            .Property(l => l.Status)
            .IsRequired()
            .HasMaxLength(20);

        modelBuilder.Entity<MacroExecutionLog>()
            .HasOne(l => l.Macro)
            .WithMany()
            .HasForeignKey(l => l.MacroId)
            .OnDelete(DeleteBehavior.Cascade)
            .IsRequired(false);

        // 宏执行日志属于审计数据，删除会话时不级联删除，保留历史记录
        // 配置为 Optional 关系，避免 EF Core 全局查询过滤器警告
        modelBuilder.Entity<MacroExecutionLog>()
            .HasOne(l => l.Conversation)
            .WithMany()
            .HasForeignKey(l => l.ConversationId)
            .OnDelete(DeleteBehavior.Restrict)
            .IsRequired(false);

        modelBuilder.Entity<MacroExecutionLog>()
            .HasOne(l => l.ExecutedByUser)
            .WithMany()
            .HasForeignKey(l => l.ExecutedByUserId)
            .OnDelete(DeleteBehavior.SetNull)
            .IsRequired(false);

        modelBuilder.Entity<CustomField>()
            .HasIndex(f => f.EntityType);

        modelBuilder.Entity<CustomField>()
            .HasIndex(f => f.MailboxId);

        modelBuilder.Entity<CustomField>()
            .HasIndex(f => f.IsActive);

        modelBuilder.Entity<CustomField>()
            .HasOne(f => f.Mailbox)
            .WithMany()
            .HasForeignKey(f => f.MailboxId)
            .OnDelete(DeleteBehavior.Cascade);

        modelBuilder.Entity<CustomFieldValue>()
            .HasIndex(v => new { v.EntityType, v.EntityId });

        modelBuilder.Entity<CustomFieldValue>()
            .HasIndex(v => v.CustomFieldId);

        modelBuilder.Entity<CustomFieldValue>()
            .HasOne(v => v.CustomField)
            .WithMany(f => f.Values)
            .HasForeignKey(v => v.CustomFieldId)
            .OnDelete(DeleteBehavior.Cascade)
            .IsRequired(false);

        modelBuilder.Entity<Webhook>()
            .HasIndex(w => w.IsActive);

        modelBuilder.Entity<Webhook>()
            .HasIndex(w => w.MailboxId);

        modelBuilder.Entity<Webhook>()
            .HasOne(w => w.Mailbox)
            .WithMany()
            .HasForeignKey(w => w.MailboxId)
            .OnDelete(DeleteBehavior.Cascade);

        // H-P3-7a: Webhook 乐观锁（高并发事件回调时 TriggerCount/SuccessCount/FailCount 原子累加）
        modelBuilder.Entity<Webhook>()
            .Property(w => w.RowVersion)
            .IsRowVersion()
            .ValueGeneratedOnAddOrUpdate();

        modelBuilder.Entity<WebhookLog>()
            .HasIndex(l => l.WebhookId);

        modelBuilder.Entity<WebhookLog>()
            .HasIndex(l => l.EventType);

        modelBuilder.Entity<WebhookLog>()
            .HasIndex(l => l.CreatedAt);

        modelBuilder.Entity<WebhookLog>()
            .HasOne(l => l.Webhook)
            .WithMany(w => w.Logs)
            .HasForeignKey(l => l.WebhookId)
            .OnDelete(DeleteBehavior.Cascade)
            .IsRequired(false);

        modelBuilder.Entity<Role>()
            .HasIndex(r => r.Slug)
            .IsUnique()
            .HasFilter("\"DeletedAt\" IS NULL");

        modelBuilder.Entity<Role>()
            .HasIndex(r => r.IsSystem);

        modelBuilder.Entity<UserRole>()
            .HasIndex(ur => ur.UserId);

        modelBuilder.Entity<UserRole>()
            .HasIndex(ur => ur.RoleId);

        modelBuilder.Entity<UserRole>()
            .HasIndex(ur => ur.MailboxId);

        // S2: (UserId, RoleId, MailboxId) 唯一索引，防止并发重复分配角色。
        // 注意：迁移中已手工改写为 COALESCE 表达式索引，确保 MailboxId 为 NULL 的全局角色也被约束。
        modelBuilder.Entity<UserRole>()
            .HasIndex(ur => new { ur.UserId, ur.RoleId, ur.MailboxId })
            .IsUnique();

        modelBuilder.Entity<UserRole>()
            .HasOne(ur => ur.User)
            .WithMany()
            .HasForeignKey(ur => ur.UserId)
            .OnDelete(DeleteBehavior.Cascade)
            .IsRequired(false);

        modelBuilder.Entity<UserRole>()
            .HasOne(ur => ur.Role)
            .WithMany(r => r.UserRoles)
            .HasForeignKey(ur => ur.RoleId)
            .OnDelete(DeleteBehavior.Cascade)
            .IsRequired(false);

        modelBuilder.Entity<UserRole>()
            .HasOne(ur => ur.Mailbox)
            .WithMany()
            .HasForeignKey(ur => ur.MailboxId)
            .OnDelete(DeleteBehavior.Cascade);

        modelBuilder.Entity<MailTemplate>()
            .HasIndex(mt => mt.Slug)
            .IsUnique()
            .HasFilter("\"DeletedAt\" IS NULL");

        modelBuilder.Entity<MailTemplate>()
            .HasIndex(mt => mt.Type);

        modelBuilder.Entity<MailTemplate>()
            .HasIndex(mt => mt.IsActive);

        modelBuilder.Entity<MailTemplate>()
            .HasIndex(mt => mt.MailboxId);

        modelBuilder.Entity<MailTemplate>()
            .HasOne(mt => mt.Mailbox)
            .WithMany()
            .HasForeignKey(mt => mt.MailboxId)
            .OnDelete(DeleteBehavior.SetNull)
            .IsRequired(false);

        modelBuilder.Entity<Team>()
            .HasIndex(t => t.Name);

        modelBuilder.Entity<Team>()
            .HasIndex(t => t.MailboxId);

        modelBuilder.Entity<Team>()
            .HasIndex(t => t.IsActive);

        modelBuilder.Entity<Team>()
            .HasOne(t => t.Mailbox)
            .WithMany()
            .HasForeignKey(t => t.MailboxId)
            .OnDelete(DeleteBehavior.SetNull)
            .IsRequired(false);

        modelBuilder.Entity<TeamMember>()
            .HasIndex(tm => new { tm.TeamId, tm.UserId })
            .IsUnique();

        modelBuilder.Entity<TeamMember>()
            .HasIndex(tm => tm.TeamId);

        modelBuilder.Entity<TeamMember>()
            .HasIndex(tm => tm.UserId);

        modelBuilder.Entity<TeamMember>()
            .HasOne(tm => tm.Team)
            .WithMany(t => t.Members)
            .HasForeignKey(tm => tm.TeamId)
            .OnDelete(DeleteBehavior.Cascade)
            .IsRequired(false);

        modelBuilder.Entity<TeamMember>()
            .HasOne(tm => tm.User)
            .WithMany()
            .HasForeignKey(tm => tm.UserId)
            .OnDelete(DeleteBehavior.Cascade)
            .IsRequired(false);

        modelBuilder.Entity<EmailSignature>()
            .HasIndex(es => es.UserId);

        modelBuilder.Entity<EmailSignature>()
            .HasIndex(es => es.MailboxId);

        modelBuilder.Entity<EmailSignature>()
            .HasIndex(es => es.IsDefault);

        modelBuilder.Entity<EmailSignature>()
            .HasOne(es => es.User)
            .WithMany()
            .HasForeignKey(es => es.UserId)
            .OnDelete(DeleteBehavior.Cascade)
            .IsRequired(false);

        modelBuilder.Entity<EmailSignature>()
            .HasOne(es => es.Mailbox)
            .WithMany()
            .HasForeignKey(es => es.MailboxId)
            .OnDelete(DeleteBehavior.SetNull)
            .IsRequired(false);

        modelBuilder.Entity<ModuleEntity>()
            .HasIndex(m => m.Name)
            .IsUnique();

        modelBuilder.Entity<ModuleEntity>()
            .HasIndex(m => m.Category);

        modelBuilder.Entity<ModuleEntity>()
            .HasIndex(m => m.IsActive);

        modelBuilder.Entity<ModuleEntity>()
            .HasIndex(m => m.SortOrder);

        modelBuilder.Entity<ApiKey>()
            .HasIndex(k => k.Key)
            .IsUnique()
            .HasFilter("\"DeletedAt\" IS NULL");

        modelBuilder.Entity<ApiKey>()
            .HasIndex(k => k.UserId);

        modelBuilder.Entity<ApiKey>()
            .HasIndex(k => k.IsActive);

        modelBuilder.Entity<ApiKey>()
            .HasOne(k => k.User)
            .WithMany()
            .HasForeignKey(k => k.UserId)
            .OnDelete(DeleteBehavior.Cascade);

        // H-P3-7a: ApiKey 乐观锁（高并发鉴权调用下 UsageCount/LastUsedAt 原子自增避免 lost update）
        modelBuilder.Entity<ApiKey>()
            .Property(k => k.RowVersion)
            .IsRowVersion()
            .ValueGeneratedOnAddOrUpdate();

        modelBuilder.Entity<ApiKeyLog>()
            .HasIndex(l => l.ApiKeyId);

        modelBuilder.Entity<ApiKeyLog>()
            .HasIndex(l => l.Endpoint);

        modelBuilder.Entity<ApiKeyLog>()
            .HasIndex(l => l.StatusCode);

        modelBuilder.Entity<ApiKeyLog>()
            .HasIndex(l => l.CreatedAt);

        modelBuilder.Entity<ApiKeyLog>()
            .HasOne(l => l.ApiKey)
            .WithMany()
            .HasForeignKey(l => l.ApiKeyId)
            .OnDelete(DeleteBehavior.Cascade)
            .IsRequired(false);

        modelBuilder.Entity<SendLog>(entity =>
        {
            entity.HasKey(e => e.Id);
            entity.HasIndex(e => e.ThreadId);
            entity.HasIndex(e => e.MessageId);
            entity.HasIndex(e => e.Email);
            entity.HasIndex(e => e.Status);
            entity.HasIndex(e => e.MailType);
            entity.HasIndex(e => e.CustomerId);
            entity.HasIndex(e => e.UserId);
            entity.HasIndex(e => e.MailboxId);
            entity.HasIndex(e => e.CreatedAt);
            // 复合索引：支撑按邮箱 + 日期范围 的发送统计聚合（报表高频查询）
            entity.HasIndex(e => new { e.MailboxId, e.CreatedAt });
            entity.Property(e => e.MessageId).HasMaxLength(500);
            entity.Property(e => e.Email).HasMaxLength(255);
            entity.Property(e => e.AttachmentIds).HasMaxLength(1000);
            entity.Property(e => e.Cc).HasMaxLength(500);
            entity.Property(e => e.Bcc).HasMaxLength(500);
            entity.Property(e => e.Subject).HasMaxLength(500);
            entity.Property(e => e.ErrorMessage).HasMaxLength(2000);
            entity.Property(e => e.InReplyTo).HasMaxLength(500);
            entity.Property(e => e.References).HasMaxLength(2000).HasColumnName("References");
        });

        modelBuilder.Entity<RefreshToken>(entity =>
        {
            entity.HasKey(e => e.Id);
            entity.HasIndex(e => e.Token).IsUnique();
            entity.HasIndex(e => e.UserId);
            entity.HasIndex(e => new { e.UserId, e.RevokedAt });
            entity.Property(e => e.Token).HasMaxLength(255);
            entity.Property(e => e.CreatedByIp).HasMaxLength(50);
            entity.Property(e => e.ReplacedByToken).HasMaxLength(255);
            entity.Property(e => e.ReasonRevoked).HasMaxLength(255);
            entity.HasOne(e => e.User)
                .WithMany()
                .HasForeignKey(e => e.UserId)
                .OnDelete(DeleteBehavior.Cascade)
                .IsRequired(false);
        });

        // PasswordResetToken: Token 字段用于密码重置热路径查询，UserId 用于用户清理，ExpiresAt 用于过期清理
        modelBuilder.Entity<PasswordResetToken>(entity =>
        {
            entity.HasKey(e => e.Id);
            entity.HasIndex(e => e.Token).IsUnique();
            entity.HasIndex(e => e.UserId);
            entity.HasIndex(e => e.ExpiresAt);
            entity.Property(e => e.Token).HasMaxLength(255);
            entity.HasOne(e => e.User)
                .WithMany()
                .HasForeignKey(e => e.UserId)
                .OnDelete(DeleteBehavior.Cascade)
                .IsRequired(false);
        });

        modelBuilder.Entity<Translation>()
            .HasIndex(t => new { t.Key, t.Culture })
            .IsUnique();

        modelBuilder.Entity<Translation>()
            .HasIndex(t => t.Culture);

        modelBuilder.Entity<CalendarEvent>()
            .HasIndex(e => e.StartAt);

        modelBuilder.Entity<CalendarEvent>()
            .HasIndex(e => e.UserId);

        modelBuilder.Entity<CalendarEvent>()
            .HasIndex(e => e.MailboxId);

        modelBuilder.Entity<CalendarEvent>()
            .HasIndex(e => e.Status);

        // P1-D#1: 复合索引优化日历主查询路径
        // 顺序1：按 UserId → StartAt → Status（最常见：先按权限过滤用户，再按日期范围取事件，最后按状态筛选）
        modelBuilder.Entity<CalendarEvent>()
            .HasIndex(e => new { e.UserId, e.StartAt, e.Status })
            .HasFilter("\"DeletedAt\" IS NULL")
            .HasDatabaseName("IX_CalendarEvents_UserId_StartAt_Status");

        // 顺序2：按 StartAt → UserId → Status（报表/跨用户聚合查询场景）
        modelBuilder.Entity<CalendarEvent>()
            .HasIndex(e => new { e.StartAt, e.UserId, e.Status })
            .HasFilter("\"DeletedAt\" IS NULL")
            .HasDatabaseName("IX_CalendarEvents_StartAt_UserId_Status");

        // 顺序3：按 MailboxId → StartAt（邮箱维度日历查询）
        modelBuilder.Entity<CalendarEvent>()
            .HasIndex(e => new { e.MailboxId, e.StartAt })
            .HasFilter("\"DeletedAt\" IS NULL")
            .HasDatabaseName("IX_CalendarEvents_MailboxId_StartAt");

        // 顺序4：按 CreatedBy → StartAt（按创建者查询自己创建的事件）
        modelBuilder.Entity<CalendarEvent>()
            .HasIndex(e => new { e.CreatedBy, e.StartAt })
            .HasFilter("\"DeletedAt\" IS NULL")
            .HasDatabaseName("IX_CalendarEvents_CreatedBy_StartAt");

        // P3-E#4-1: 高频查询覆盖/过滤索引
        // a. 用户+删除标记+开始时间 复合覆盖索引：主列表查询 GetListAsync(GetOverviewAggregateAsync) 高频路径
        //    过滤 DeletedAt IS NULL，IncludeProperties 覆盖 Id/Title/EndAt/EventType/Status 避免回表
        modelBuilder.Entity<CalendarEvent>()
            .HasIndex(e => new { e.UserId, e.DeletedAt, e.StartAt })
            .HasFilter("\"DeletedAt\" IS NULL")
            .IncludeProperties(e => new { e.Id, e.Title, e.EndAt, e.EventType, e.Status })
            .HasDatabaseName("IX_CalendarEvents_UserId_DeletedAt_StartAt");

        // b. 邮箱+删除标记+状态：按邮箱维度 + 状态过滤查询（报表/队列）
        modelBuilder.Entity<CalendarEvent>()
            .HasIndex(e => new { e.MailboxId, e.DeletedAt, e.Status })
            .HasFilter("\"DeletedAt\" IS NULL")
            .HasDatabaseName("IX_CalendarEvents_Mailbox_DeletedAt_Status");

        // c. 提醒调度兜底扫描：ReminderSentAt 非空 + Status + DeletedAt
        //    对应 CalendarReminderFallbackJob 扫描已发送但未确认的提醒
        modelBuilder.Entity<CalendarEvent>()
            .HasIndex(e => new { e.ReminderSentAt, e.Status, e.DeletedAt })
            .HasFilter("\"DeletedAt\" IS NULL AND \"ReminderSentAt\" IS NOT NULL")
            .HasDatabaseName("IX_CalendarEvents_ReminderDue");

        // d. 重复日程查询：IsRecurring + RecurrenceType + DeletedAt
        //    对应 GetRecurringEventsAsync 展开周期性事件
        //    注：实体无 RecurrenceRule 字段，用 RecurrenceType（重复类型：每日/每周/每月/每年）替代
        modelBuilder.Entity<CalendarEvent>()
            .HasIndex(e => new { e.IsRecurring, e.RecurrenceType, e.DeletedAt })
            .HasFilter("\"DeletedAt\" IS NULL AND \"IsRecurring\" = 1")
            .HasDatabaseName("IX_CalendarEvents_Recurring");

        // e. Outlook 字段：CalendarEvent 实体不包含 OutlookIcalUid/OutlookChangeKey（映射在 OutlookEventMapping 表），跳过此索引

        modelBuilder.Entity<CalendarEvent>()
            .Property(e => e.Location)
            .HasMaxLength(200);

        modelBuilder.Entity<CalendarEvent>()
            .Property(e => e.MeetingUrl)
            .HasMaxLength(500);

        // 新增字段配置（2026-08-17：会议/任务/提醒/重复面板字段持久化）
        modelBuilder.Entity<CalendarEvent>()
            .Property(e => e.HostUserIdsJson)
            .HasMaxLength(2000);

        modelBuilder.Entity<CalendarEvent>()
            .Property(e => e.AgendaIdsJson)
            .HasMaxLength(2000);

        modelBuilder.Entity<CalendarEvent>()
            .Property(e => e.ReminderType)
            .HasMaxLength(20);

        modelBuilder.Entity<CalendarEvent>()
            .Property(e => e.WeekdaysSelectedJson)
            .HasMaxLength(200);

        modelBuilder.Entity<CalendarEvent>()
            .Property(e => e.RecurrenceRule)
            .HasMaxLength(500);

        modelBuilder.Entity<CalendarEvent>()
            .Property(e => e.CreatedBy)
            .HasDefaultValue(0);

        // 创建者导航属性：映射到已有的 CreatedBy 外键列（无需新增数据库列）
        // 与 User 导航属性（UserId）区分，避免 EF Core 约定生成 CreatedByUserId 列
        modelBuilder.Entity<CalendarEvent>()
            .HasOne(e => e.CreatedByUser)
            .WithMany()
            .HasForeignKey(e => e.CreatedBy)
            .OnDelete(DeleteBehavior.Restrict)
            .IsRequired(false);

        // CalendarEvent EventType/Status 值转换器：统一转小写存储，保证 DB 查询大小写一致。
        // 注意：不能 Strip 下划线（旧 bug：v => v.ToLowerInvariant().Replace("_","") 会把 "due_date" 写成 "duedate"，
        // 但反向转换器 v => v 无法还原下划线，导致读回的 "duedate" 在前端 EVENT_TYPE_STYLE 映射失败回退到 other 灰色）。
        modelBuilder.Entity<CalendarEvent>()
            .Property(e => e.EventType)
            .HasMaxLength(20)
            .HasConversion(
                v => v.ToString().ToLowerInvariant(),
                v => v
            );

        modelBuilder.Entity<CalendarEvent>()
            .Property(e => e.Status)
            .HasMaxLength(20)
            .HasConversion(
                v => v.ToString().ToLowerInvariant(),
                v => v
            );

        // H-S1: 乐观锁配置，防止并发更新覆盖
        // P4 修复：IsRowVersion() + ValueGeneratedOnAddOrUpdate() 会让 EF Core 在 INSERT 时传 NULL，
        // 触发 SQLite NOT NULL 约束冲突（"操作冲突，数据可能已被修改或存在约束冲突"）。
        // 改为 IsConcurrencyToken() + ValueGeneratedNever() + IsRequired(false)：
        // - IsConcurrencyToken: 仍参与乐观锁 WHERE RowVersion = @original 检查
        // - ValueGeneratedNever: EF Core 用 CurrentValue 生成 INSERT 参数（由 C# 显式赋值或 DB DEFAULT 兜底）
        // - IsRequired(false): 列允许 NULL，DB 级 DEFAULT randomblob(16) 兜底，避免插入失败
        modelBuilder.Entity<CalendarEvent>()
            .Property(e => e.RowVersion)
            .IsConcurrencyToken()
            .ValueGeneratedNever()
            .IsRequired(false);

        // P2P3 #7.3：CalendarReminderAttempts 提醒投递审计表
        // 显式映射表名（复数），避免 EF Core 默认使用实体类名（单数 CalendarReminderAttempt）导致 "no such table" 错误
        modelBuilder.Entity<CalendarReminderAttempt>()
            .ToTable("CalendarReminderAttempts");

        modelBuilder.Entity<CalendarReminderAttempt>()
            .HasIndex(a => new { a.EventId, a.AttemptChannel })
            .HasDatabaseName("IX_CalRem_EventId_Channel");

        modelBuilder.Entity<CalendarReminderAttempt>()
            .HasIndex(a => a.CreatedAt)
            .HasDatabaseName("IX_CalendarReminderAttempts_CreatedAt");

        modelBuilder.Entity<CalendarReminderAttempt>()
            .HasIndex(a => new { a.Status, a.CreatedAt })
            .HasDatabaseName("IX_CalendarReminderAttempts_Status_CreatedAt");

        modelBuilder.Entity<CalendarReminderAttempt>()
            .HasOne(a => a.Event)
            .WithMany()
            .HasForeignKey(a => a.EventId)
            .OnDelete(DeleteBehavior.Cascade);

        // 全局查询过滤器：软删除审计记录不返回（与 ISoftDelete 约定一致）
        modelBuilder.Entity<CalendarReminderAttempt>()
            .HasQueryFilter(a => a.DeletedAt == null);

        // 知识库
        modelBuilder.Entity<KnowledgeCategory>()
            .HasIndex(c => c.ParentId);

        modelBuilder.Entity<KnowledgeCategory>()
            .HasIndex(c => c.MailboxId);

        modelBuilder.Entity<KnowledgeArticle>()
            .HasIndex(a => a.CategoryId);

        modelBuilder.Entity<KnowledgeArticle>()
            .HasIndex(a => a.Status);

        modelBuilder.Entity<KnowledgeArticle>()
            .HasIndex(a => a.AuthorId);

        // 显式配置 Author 导航属性与 User 的关系（与 Conversation.User 等惯例一致）
        // User 是软删除实体，配置为 Optional 避免全局查询过滤器警告
        modelBuilder.Entity<KnowledgeArticle>()
            .HasOne(a => a.Author)
            .WithMany()
            .HasForeignKey(a => a.AuthorId)
            .OnDelete(DeleteBehavior.SetNull)
            .IsRequired(false);

        // H-P3-7a+: KnowledgeCategory 乐观锁（分类树拖拽/改名/迁移与并发编辑冲突）
        modelBuilder.Entity<KnowledgeCategory>()
            .Property(c => c.RowVersion)
            .IsRowVersion()
            .ValueGeneratedOnAddOrUpdate();

        // H-P3-7a+: KnowledgeArticle 乐观锁（多人同时编辑文章 + 浏览点赞计数 lost update）
        modelBuilder.Entity<KnowledgeArticle>()
            .Property(a => a.RowVersion)
            .IsRowVersion()
            .ValueGeneratedOnAddOrUpdate();

        // 知识库文章附件：文章级附件表，关联文章，软删除，索引 ArticleId
        modelBuilder.Entity<KnowledgeArticleAttachment>()
            .HasIndex(a => a.ArticleId);

        modelBuilder.Entity<KnowledgeArticleAttachment>()
            .HasOne(a => a.Article)
            .WithMany(a => a.Attachments)
            .HasForeignKey(a => a.ArticleId)
            .OnDelete(DeleteBehavior.Cascade);

        modelBuilder.Entity<KnowledgeArticleAttachment>()
            .Property(a => a.FileSize);

        // 全局查询过滤器：软删除附件不返回（与 ISoftDelete 约定一致）
        modelBuilder.Entity<KnowledgeArticleAttachment>()
            .HasQueryFilter(a => a.DeletedAt == null);

        // 在线聊天
        modelBuilder.Entity<ChatSession>()
            .HasIndex(s => s.SessionToken)
            .IsUnique();

        modelBuilder.Entity<ChatSession>()
            .HasIndex(s => s.Status);

        modelBuilder.Entity<ChatSession>()
            .HasIndex(s => s.MailboxId);

        modelBuilder.Entity<ChatSession>()
            .HasIndex(s => s.UserId);

        modelBuilder.Entity<ChatSession>()
            .HasIndex(s => s.CustomerId);

        modelBuilder.Entity<ChatSession>()
            .HasIndex(s => s.ConversationId);

        modelBuilder.Entity<ChatSession>()
            .HasIndex(s => s.CreatedAt);

        // 软删除：全局查询过滤器自动排除已删除的会话
        modelBuilder.Entity<ChatSession>()
            .HasQueryFilter(e => e.DeletedAt == null);

        // 软删除过滤索引：支撑垃圾箱查询与正常查询的性能
        modelBuilder.Entity<ChatSession>()
            .HasIndex(e => e.DeletedAt)
            .HasFilter("\"DeletedAt\" IS NULL");

        modelBuilder.Entity<ChatSession>()
            .HasOne(s => s.Mailbox)
            .WithMany()
            .HasForeignKey(s => s.MailboxId)
            .OnDelete(DeleteBehavior.SetNull)
            .IsRequired(false);

        modelBuilder.Entity<ChatSession>()
            .HasOne(s => s.User)
            .WithMany()
            .HasForeignKey(s => s.UserId)
            .OnDelete(DeleteBehavior.SetNull)
            .IsRequired(false);

        modelBuilder.Entity<ChatSession>()
            .HasOne(s => s.Customer)
            .WithMany()
            .HasForeignKey(s => s.CustomerId)
            .OnDelete(DeleteBehavior.SetNull)
            .IsRequired(false);

        modelBuilder.Entity<ChatSession>()
            .HasOne(s => s.Conversation)
            .WithMany()
            .HasForeignKey(s => s.ConversationId)
            .OnDelete(DeleteBehavior.SetNull)
            .IsRequired(false);

        // ChatSession 是软删除实体，配置为 Optional 避免全局查询过滤器警告
        // 当 ChatSession 被软删除（DeletedAt != null）时，query filter 会将其排除，
        // 若关系为 required，通过 ChatMessage.Session 导航访问会返回 null 导致 NRE。
        // 配置为 Optional 后，EF Core 不再警告；SessionId 字段仍为 int NOT NULL（由属性类型决定）。
        modelBuilder.Entity<ChatSession>()
            .HasMany(s => s.Messages)
            .WithOne(m => m.Session)
            .HasForeignKey(m => m.SessionId)
            .OnDelete(DeleteBehavior.Cascade)
            .IsRequired(false);

        modelBuilder.Entity<ChatMessage>()
            .HasIndex(m => m.SessionId);

        modelBuilder.Entity<ChatMessage>()
            .HasIndex(m => m.SenderType);

        modelBuilder.Entity<ChatMessage>()
            .HasIndex(m => m.IsRead);

        modelBuilder.Entity<ChatMessage>()
            .HasIndex(m => m.CreatedAt);

        // H-P3-7a: ChatSession 乐观锁（客户/客服同时写 LastActivityAt/UnreadCount/Status/评分/关闭会话；多端 UI 同时打开）
        modelBuilder.Entity<ChatSession>()
            .Property(s => s.RowVersion)
            .IsRowVersion()
            .ValueGeneratedOnAddOrUpdate();

        // H-P3-7a: ChatMessage 乐观锁（撤回/编辑消息、客服标记已读时与其他用户操作并发）
        modelBuilder.Entity<ChatMessage>()
            .Property(m => m.RowVersion)
            .IsRowVersion()
            .ValueGeneratedOnAddOrUpdate();

        // ===== TimeEntry 配置 =====
        modelBuilder.Entity<TimeEntry>()
            .HasQueryFilter(e => e.DeletedAt == null);

        modelBuilder.Entity<TimeEntry>()
            .HasIndex(e => e.UserId);

        modelBuilder.Entity<TimeEntry>()
            .HasIndex(e => e.StartTime);

        modelBuilder.Entity<TimeEntry>()
            .HasIndex(e => e.ConversationId);

        modelBuilder.Entity<TimeEntry>()
            .HasIndex(e => e.EndTime);

        // TT-001: 活跃计时器唯一约束（数据库层兜底防御并发竞态）
        // 每个用户最多只能有一个 EndTime IS NULL 的记录（运行中的计时器）
        modelBuilder.Entity<TimeEntry>()
            .HasIndex(e => new { e.UserId, e.EndTime })
            .IsUnique()
            .HasFilter("\"EndTime\" IS NULL AND \"DeletedAt\" IS NULL");

        modelBuilder.Entity<TimeEntry>()
            .Property(e => e.RowVersion)
            .IsRowVersion();

        modelBuilder.Entity<TimeEntry>()
            .HasOne(e => e.Conversation)
            .WithMany()
            .HasForeignKey(e => e.ConversationId)
            .OnDelete(DeleteBehavior.SetNull)
            .IsRequired(false);

        modelBuilder.Entity<TimeEntry>()
            .HasOne(e => e.Customer)
            .WithMany()
            .HasForeignKey(e => e.CustomerId)
            .OnDelete(DeleteBehavior.SetNull)
            .IsRequired(false);

        modelBuilder.Entity<TimeEntry>()
            .HasOne(e => e.User)
            .WithMany()
            .HasForeignKey(e => e.UserId)
            .OnDelete(DeleteBehavior.Restrict)
            .IsRequired(true);

        modelBuilder.Entity<TimeEntry>()
            .HasOne(e => e.Mailbox)
            .WithMany()
            .HasForeignKey(e => e.MailboxId)
            .OnDelete(DeleteBehavior.SetNull)
            .IsRequired(false);

        // 枚举 CHECK 约束：确保 SQLite 中枚举字段值有效
        modelBuilder.Entity<Conversation>()
            .ToTable(tb =>
            {
                tb.HasCheckConstraint("CK_Conversation_Status", "[Status] IN (1, 2, 3, 4)");
                tb.HasCheckConstraint("CK_Conversation_State", "[State] IN (1, 2)");
                tb.HasCheckConstraint("CK_Conversation_Priority", "[Priority] IN (0, 1, 2, 3)");
            });

        modelBuilder.Entity<ConversationThread>()
            .ToTable(tb => tb.HasCheckConstraint("CK_ConversationThread_State", "[State] IN (1, 2, 3)"));

        // 必须覆盖 SendLogStatus 的全部取值：Accepted=1, SendError=2, DeliverySuccess=4, DeliveryError=5,
        // Opened=6, Clicked=7, Unsubscribed=8, Complained=9, SendIntermediateError=10。
        // 历史缺陷：原约束仅 IN (1,2,3,4,5)，与代码实际写入的 6/7/8/9/10 冲突；
        // 生产库因该约束未落表而侥幸正常，一旦新建库或 SQLite 表重建让约束生效，邮件队列与打开/点击追踪会整表写入失败。
        modelBuilder.Entity<SendLog>()
            .ToTable(tb => tb.HasCheckConstraint("CK_SendLog_Status", "[Status] IN (1, 2, 3, 4, 5, 6, 7, 8, 9, 10)"));

        modelBuilder.Entity<User>()
            .ToTable(tb => tb.HasCheckConstraint("CK_User_Role", "[Role] IN (1, 2, 3, 4)"));

        // Star：支撑按用户+会话查询星标、按用户列出所有星标
        modelBuilder.Entity<Star>()
            .HasIndex(s => new { s.ConversationId, s.UserId })
            .IsUnique();

        modelBuilder.Entity<Star>()
            .HasIndex(s => s.UserId);

        modelBuilder.Entity<Star>()
            .HasOne(s => s.Conversation)
            .WithMany()
            .HasForeignKey(s => s.ConversationId)
            .OnDelete(DeleteBehavior.Cascade)
            .IsRequired(false);

        modelBuilder.Entity<Star>()
            .HasOne(s => s.User)
            .WithMany()
            .HasForeignKey(s => s.UserId)
            .OnDelete(DeleteBehavior.Cascade)
            .IsRequired(false);

        // ConversationReadState：按用户-会话维度记录最后阅读时间，用于未读排序与徽章
        modelBuilder.Entity<ConversationReadState>()
            .HasIndex(r => new { r.ConversationId, r.UserId })
            .IsUnique();

        modelBuilder.Entity<ConversationReadState>()
            .HasIndex(r => r.UserId);

        modelBuilder.Entity<ConversationReadState>()
            .HasOne(r => r.Conversation)
            .WithMany()
            .HasForeignKey(r => r.ConversationId)
            .OnDelete(DeleteBehavior.Cascade)
            .IsRequired(false);

        modelBuilder.Entity<ConversationReadState>()
            .HasOne(r => r.User)
            .WithMany()
            .HasForeignKey(r => r.UserId)
            .OnDelete(DeleteBehavior.Cascade)
            .IsRequired(false);

        modelBuilder.Entity<UserNotification>()
            .HasOne(un => un.User)
            .WithMany()
            .HasForeignKey(un => un.UserId)
            .OnDelete(DeleteBehavior.Cascade)
            .IsRequired(false);

        // Attachment：支撑按线程查询附件列表
        modelBuilder.Entity<Attachment>()
            .HasIndex(a => a.ThreadId);

        // ConversationThread.MessageId：支撑邮件线程关联查询（高频）
        // TD-06: 提升为唯一过滤索引（排除空 MessageId，如手动创建的 Note），数据库层幂等兜底
        modelBuilder.Entity<ConversationThread>()
            .HasIndex(t => t.MessageId)
            .IsUnique()
            .HasFilter("\"MessageId\" != ''");

        // ===== Outlook 日历双向同步实体配置 =====
        // OutlookSyncSetting：每个用户最多一条同步配置
        modelBuilder.Entity<OutlookSyncSetting>(entity =>
        {
            entity.HasKey(e => e.Id);
            // 一个用户只能有一条同步设置（软删除范围内唯一）
            entity.HasIndex(e => e.UserId)
                .IsUnique()
                .HasFilter("\"DeletedAt\" IS NULL");
            entity.HasIndex(e => e.SyncEnabled);
            entity.HasIndex(e => e.LastSyncAt);
            entity.Property(e => e.SyncDirection).HasMaxLength(20).HasDefaultValue("both");
            entity.Property(e => e.LastSyncStatus).HasMaxLength(20);
            entity.Property(e => e.OutlookEmail).HasMaxLength(256);
            entity.Property(e => e.OutlookUserId).HasMaxLength(128);
            // 令牌字段长度（加密后可能变长，预留充足空间）
            entity.Property(e => e.AccessToken).HasMaxLength(2048);
            entity.Property(e => e.RefreshToken).HasMaxLength(2048);
            entity.Property(e => e.LastSyncError).HasMaxLength(2000);
            entity.HasOne<User>()
                .WithMany()
                .HasForeignKey(e => e.UserId)
                .OnDelete(DeleteBehavior.Cascade);
            // T000099: 用户自配置 Azure AD 凭据字段（每用户独立，空值回退到 appsettings 基线）
            entity.Property(e => e.ClientId).HasMaxLength(256);
            entity.Property(e => e.ClientSecret).HasMaxLength(2048); // 加密后变长，预留充足空间
            entity.Property(e => e.TenantId).HasMaxLength(128);
            entity.Property(e => e.RedirectUri).HasMaxLength(512);
            entity.Property(e => e.Scopes).HasMaxLength(512);
        });

        // OutlookEventMapping：本地事件与 Outlook 事件的映射关系
        modelBuilder.Entity<OutlookEventMapping>(entity =>
        {
            entity.HasKey(e => e.Id);
            // 一个本地事件 + 用户 最多映射到一条 Outlook 事件
            entity.HasIndex(e => new { e.CalendarEventId, e.UserId })
                .IsUnique();
            // 通过 OutlookEventId 反查本地事件（Webhook 回调时使用）
            entity.HasIndex(e => e.OutlookEventId);
            entity.HasIndex(e => e.UserId);
            entity.HasIndex(e => e.Source);
            entity.Property(e => e.OutlookEventId).HasMaxLength(256);
            entity.Property(e => e.Source).HasMaxLength(20).HasDefaultValue("local");
            entity.HasOne<CalendarEvent>()
                .WithMany()
                .HasForeignKey(e => e.CalendarEventId)
                .OnDelete(DeleteBehavior.Cascade);
            entity.HasOne<User>()
                .WithMany()
                .HasForeignKey(e => e.UserId)
                .OnDelete(DeleteBehavior.Cascade);
        });

        // DistributedLock 配置
        modelBuilder.Entity<DistributedLock>(entity =>
        {
            entity.HasKey(e => e.Id);
            entity.HasIndex(e => e.LockKey)
                .IsUnique();
            entity.HasIndex(e => e.ExpiresAt);
            entity.Property(e => e.LockKey).HasMaxLength(256).IsRequired();
            entity.Property(e => e.Owner).HasMaxLength(64).IsRequired();
        });

        // 对 OutlookSyncSetting 的敏感令牌字段启用加密（与 Mailbox 保持一致）
        if (_encryptionService != null)
        {
#pragma warning disable CS8620 // EF Core ValueConverter 可空引用类型标注已知限制
            modelBuilder.Entity<OutlookSyncSetting>()
                .Property(s => s.AccessToken)
                .HasConversion(EncryptedStringConverter);

            modelBuilder.Entity<OutlookSyncSetting>()
                .Property(s => s.RefreshToken)
                .HasConversion(EncryptedStringConverter);

            // T000099: 用户自配置的 ClientSecret 同样启用加密（与 AccessToken/RefreshToken 一致）
            modelBuilder.Entity<OutlookSyncSetting>()
                .Property(s => s.ClientSecret)
                .HasConversion(EncryptedStringConverter);
#pragma warning restore CS8620
        }

        // ===== CustomReport 配置 =====
        modelBuilder.Entity<CustomReport>()
            .HasKey(x => x.Id);

        modelBuilder.Entity<CustomReport>()
            .Property(x => x.Name)
            .IsRequired()
            .HasMaxLength(200);

        modelBuilder.Entity<CustomReport>()
            .Property(x => x.Description)
            .HasMaxLength(2000);

        modelBuilder.Entity<CustomReport>()
            .Property(x => x.Category)
            .IsRequired()
            .HasMaxLength(50);

        modelBuilder.Entity<CustomReport>()
            .Property(x => x.Visibility)
            .IsRequired()
            .HasMaxLength(20);

        modelBuilder.Entity<CustomReport>()
            .Property(x => x.ReportConfig)
            .IsRequired();

        modelBuilder.Entity<CustomReport>()
            .Property(x => x.CreatedAt)
            .IsRequired();

        modelBuilder.Entity<CustomReport>()
            .Property(x => x.UpdatedAt)
            .IsRequired();

        modelBuilder.Entity<CustomReport>()
            .HasOne(x => x.Owner)
            .WithMany()
            .HasForeignKey(x => x.OwnerId)
            .OnDelete(DeleteBehavior.Restrict)
            .IsRequired();

        modelBuilder.Entity<CustomReport>()
            .HasOne(x => x.Team)
            .WithMany()
            .HasForeignKey(x => x.TeamId)
            .OnDelete(DeleteBehavior.SetNull)
            .IsRequired(false);

        modelBuilder.Entity<CustomReport>()
            .HasIndex(x => new { x.Name, x.OwnerId })
            .IsUnique()
            .HasFilter("\"DeletedAt\" IS NULL");

        modelBuilder.Entity<CustomReport>()
            .HasIndex(x => x.OwnerId)
            .HasFilter("\"DeletedAt\" IS NULL");

        modelBuilder.Entity<CustomReport>()
            .HasIndex(x => new { x.IsDashboardPinned, x.DashboardSortOrder })
            .HasFilter("\"DeletedAt\" IS NULL");

        // ===== CustomReportSchedule 配置 =====
        modelBuilder.Entity<CustomReportSchedule>()
            .HasKey(x => x.Id);

        modelBuilder.Entity<CustomReportSchedule>()
            .Property(x => x.Recurrence)
            .IsRequired()
            .HasMaxLength(20);

        modelBuilder.Entity<CustomReportSchedule>()
            .Property(x => x.SendTime)
            .IsRequired()
            .HasMaxLength(5);

        modelBuilder.Entity<CustomReportSchedule>()
            .Property(x => x.Recipients)
            .IsRequired();

        modelBuilder.Entity<CustomReportSchedule>()
            .Property(x => x.Subject)
            .IsRequired()
            .HasMaxLength(200);

        modelBuilder.Entity<CustomReportSchedule>()
            .Property(x => x.AttachmentFormat)
            .IsRequired()
            .HasMaxLength(10);

        modelBuilder.Entity<CustomReportSchedule>()
            .Property(x => x.CreatedAt)
            .IsRequired();

        modelBuilder.Entity<CustomReportSchedule>()
            .Property(x => x.UpdatedAt)
            .IsRequired();

        modelBuilder.Entity<CustomReportSchedule>()
            .HasOne(x => x.CustomReport)
            .WithMany()
            .HasForeignKey(x => x.CustomReportId)
            .OnDelete(DeleteBehavior.Cascade)
            .IsRequired();

        modelBuilder.Entity<CustomReportSchedule>()
            .HasOne(x => x.Owner)
            .WithMany()
            .HasForeignKey(x => x.OwnerId)
            .OnDelete(DeleteBehavior.Restrict)
            .IsRequired();

        modelBuilder.Entity<CustomReportSchedule>()
            .HasIndex(x => new { x.IsActive, x.NextRunAt })
            .HasFilter("\"DeletedAt\" IS NULL");

        // ===== IM 消息发送日志（ImMessageLog）配置 =====
        // 参考 SendLog 配置模式，支撑 IM 消息重试队列的查询和状态管理
        modelBuilder.Entity<ImMessageLog>(entity =>
        {
            entity.HasKey(e => e.Id);
            entity.HasIndex(e => e.ConversationId);
            entity.HasIndex(e => e.CustomerId);
            entity.HasIndex(e => e.Status);
            entity.HasIndex(e => e.SourceType);
            // 复合索引：支撑队列扫描 - 按状态 + 下次重试时间过滤
            entity.HasIndex(e => new { e.Status, e.NextRetryAt });
            entity.HasIndex(e => e.CreatedAt);
            entity.HasIndex(e => e.NextRetryAt);
            entity.Property(e => e.ChannelId).HasMaxLength(255);
            entity.Property(e => e.MessageText).IsRequired();
            entity.Property(e => e.ErrorMessage).HasMaxLength(2000);
            entity.Property(e => e.ExternalMessageId).HasMaxLength(500);
            entity.Property(e => e.Status).HasDefaultValue(1);
            entity.Property(e => e.AttemptCount).HasDefaultValue(0);
            entity.Property(e => e.MaxAttempts).HasDefaultValue(4);

            // 导航配置为 optional：ConversationId 为 int（非空），FK 在数据库层仍为 NOT NULL，
            // 但导航设为 optional 可避免 Conversation 全局查询过滤器（软删除）与必需导航的冲突警告
            entity.HasOne(e => e.Conversation)
                .WithMany()
                .HasForeignKey(e => e.ConversationId)
                .OnDelete(DeleteBehavior.Cascade)
                .IsRequired(false);

            entity.HasOne(e => e.Customer)
                .WithMany()
                .HasForeignKey(e => e.CustomerId)
                .OnDelete(DeleteBehavior.Restrict)
                .IsRequired(false);
        });

        // 状态 CHECK 约束：1=Pending, 2=Sending, 3=Sent, 4=Failed
        modelBuilder.Entity<ImMessageLog>()
            .ToTable(tb => tb.HasCheckConstraint("CK_ImMessageLog_Status", "[Status] IN (1, 2, 3, 4)"));

        // ===== Outlook 同步审计日志（OutlookSyncLog）配置 =====
        // 任务 D#2：按 MailboxId + StartedAt DESC 查询分页
        modelBuilder.Entity<OutlookSyncLog>(entity =>
        {
            entity.HasKey(e => e.Id);
            entity.HasIndex(e => new { e.MailboxId, e.StartedAt })
                .HasDatabaseName("IX_OutlookSyncLog_MailboxId_StartedAt")
                .IsDescending(false, true);
            entity.HasIndex(e => e.Status);
            entity.HasIndex(e => e.CreatedAt);
            entity.Property(e => e.Direction).HasMaxLength(20).IsRequired();
            entity.Property(e => e.Status).HasMaxLength(20).IsRequired();
            entity.Property(e => e.ErrorMessage).HasMaxLength(4000);
            entity.HasOne(e => e.Mailbox)
                .WithMany()
                .HasForeignKey(e => e.MailboxId)
                .OnDelete(DeleteBehavior.Cascade);
        });

        // ===== Outlook 同步重试队列（OutlookSyncQueueItem）配置 =====
        // 任务 D#3：按 Status + NextRetryAt + MailboxId 扫描待重试项
        modelBuilder.Entity<OutlookSyncQueueItem>(entity =>
        {
            entity.HasKey(e => e.Id);
            entity.HasIndex(e => new { e.Status, e.NextRetryAt, e.MailboxId })
                .HasDatabaseName("IX_OutlookSyncQueue_Status_NextRetryAt_MailboxId");
            entity.HasIndex(e => e.MailboxId);
            entity.HasIndex(e => e.Status);
            entity.HasIndex(e => e.NextRetryAt);
            entity.Property(e => e.Direction).HasMaxLength(20).IsRequired();
            entity.Property(e => e.Operation).HasMaxLength(20).IsRequired();
            entity.Property(e => e.Status).HasMaxLength(20).IsRequired();
            entity.Property(e => e.RetryCount).HasDefaultValue(0);
            entity.Property(e => e.MaxRetry).HasDefaultValue(5);
            entity.Property(e => e.LastError).HasMaxLength(4000);
            entity.HasOne(e => e.Mailbox)
                .WithMany()
                .HasForeignKey(e => e.MailboxId)
                .OnDelete(DeleteBehavior.Cascade);
        });

        // P4 修复：统一覆盖所有 byte[] 并发令牌属性。
        // 背景：IsRowVersion() + ValueGeneratedOnAddOrUpdate() 在 SQLite 提供程序下会在 INSERT 时传 NULL，
        // 触发 NOT NULL 约束冲突。此处在所有实体配置完成后遍历模型，统一改为：
        // - IsConcurrencyToken(): 保留乐观锁 WHERE 检查语义
        // - ValueGeneratedNever(): EF Core 用 CurrentValue 生成 INSERT 参数（由 C# 显式赋值或 DB DEFAULT 兜底）
        // - IsRequired(false): 列允许 NULL，配合 DB 级 DEFAULT randomblob(16) 兜底
        // 这样既保留乐观锁能力，又避免 INSERT 时 NULL 约束冲突。
        // 注意：此覆盖会重写前面对 RowVersion/Version 的 IsRowVersion() 配置，统一行为。
        foreach (var entityType in modelBuilder.Model.GetEntityTypes())
        {
            foreach (var property in entityType.GetProperties())
            {
                if (property.ClrType == typeof(byte[]))
                {
                    var builder = modelBuilder.Entity(entityType.ClrType)
                        .Property(property.Name);
                    // 仅处理标记为并发令牌的 byte[]（IsRowVersion 隐含 IsConcurrencyToken）
                    if (property.IsConcurrencyToken)
                    {
                        builder
                            .IsConcurrencyToken()
                            .ValueGeneratedNever()
                            .IsRequired(false);
                    }
                }
            }
        }
    }
}
