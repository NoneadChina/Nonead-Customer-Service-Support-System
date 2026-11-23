using System.Net;
using System.Net.Sockets;
using NoneadCSS.Application.Common;
using NoneadCSS.Application.Services;
using NoneadCSS.Application.Services.WorkflowActions;
using NoneadCSS.Domain;
using NoneadCSS.Domain.Entities;
using NoneadCSS.Domain.Events;
using NoneadCSS.Domain.Repositories;
using NoneadCSS.Domain.Services;
using NoneadCSS.Infrastructure.Jobs;
using NoneadCSS.Infrastructure.Persistence;
using NoneadCSS.Infrastructure.Repositories;
using NoneadCSS.Infrastructure.Services;
using NoneadCSS.Infrastructure.Services.DingDing;
using NoneadCSS.Infrastructure.Services.Outlook;
using NoneadCSS.Infrastructure.Services.WeChat;
using NoneadCSS.Infrastructure.Services.WhatsApp;
using NoneadCSS.Infrastructure.Services.Feishu;
using NoneadCSS.Infrastructure.Services.Messenger;
using NoneadCSS.Infrastructure.Services.Telegram;
using NoneadCSS.Infrastructure.Services.Line;
using NoneadCSS.Infrastructure.Services.IMessage;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Diagnostics;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Localization;
using Quartz;
using Serilog;

namespace NoneadCSS.Infrastructure;

public static class DependencyInjection
{
    public static IServiceCollection AddInfrastructure(this IServiceCollection services, IConfiguration configuration)
    {
        // 与 Program.cs 里的 Fail-Fast 保持同一口径：Database:UseSqlite 未配置或无法解析时按 SQLite 处理。
        // 这里若用 GetValue<bool>() 直接取，"缺键"会被读成 false，于是从错误的工作目录启动
        // （appsettings.json 未被加载）时会静默切到 SQL Server 分支并拿到空连接串，最终在
        // DatabaseSeeder 打开连接时抛 "ConnectionString 属性尚未初始化"，现象与配置完全无关，极难定位。
        var useSqlite = ResolveUseSqlite(configuration);

        if (useSqlite)
        {
            services.AddScoped<AuditSaveChangesInterceptor>();
            services.AddScoped<HtmlSanitizingSaveChangesInterceptor>();
            services.AddScoped<SqliteConnectionInterceptor>();
            services.AddDbContext<NoneadCSSDbContext>((sp, options) =>
                options.UseSqlite(configuration.GetConnectionString("SqliteConnection"))
                    .ConfigureWarnings(w => w.Ignore(RelationalEventId.MultipleCollectionIncludeWarning))
                    .AddInterceptors(sp.GetRequiredService<HtmlSanitizingSaveChangesInterceptor>())
                    .AddInterceptors(sp.GetRequiredService<AuditSaveChangesInterceptor>())
                    .AddInterceptors(sp.GetRequiredService<SqliteConnectionInterceptor>()));
        }
        else
        {
            services.AddScoped<AuditSaveChangesInterceptor>();
            services.AddScoped<HtmlSanitizingSaveChangesInterceptor>();
            services.AddDbContext<NoneadCSSDbContext>((sp, options) =>
                options.UseSqlServer(configuration.GetConnectionString("DefaultConnection"))
                    .ConfigureWarnings(w => w.Ignore(RelationalEventId.MultipleCollectionIncludeWarning))
                    .AddInterceptors(sp.GetRequiredService<HtmlSanitizingSaveChangesInterceptor>())
                    .AddInterceptors(sp.GetRequiredService<AuditSaveChangesInterceptor>()));
        }

        services.AddScoped<IUnitOfWork, UnitOfWork>();
        services.AddScoped<IUserRepository, UserRepository>();
        services.AddScoped<IConversationRepository, ConversationRepository>();
        services.AddScoped<IMailboxRepository, MailboxRepository>();
        services.AddScoped<ICustomerRepository, CustomerRepository>();
        services.AddScoped<IAttachmentRepository, AttachmentRepository>();
        services.AddScoped<IFolderRepository, FolderRepository>();
        services.AddScoped<IAuditLogRepository, AuditLogRepository>();
        // D#2: Outlook 同步审计日志仓储
        services.AddScoped<IOutlookSyncLogRepository, OutlookSyncLogRepository>();
        // D#3: Outlook 同步重试队列仓储
        services.AddScoped<IOutlookSyncQueueRepository, OutlookSyncQueueRepository>();
        services.AddScoped<IThreadRepository, ThreadRepository>();
        services.AddScoped<ITagRepository, TagRepository>();
        services.AddScoped<ISystemOptionRepository, SystemOptionRepository>();
        services.AddScoped<INotificationTemplateRepository, NotificationTemplateRepository>();
        services.AddScoped<INotificationRuleRepository, NotificationRuleRepository>();
        services.AddScoped<IUserNotificationRepository, UserNotificationRepository>();
        services.AddScoped<IPasswordResetTokenRepository, PasswordResetTokenRepository>();
        services.AddScoped<IRefreshTokenRepository, RefreshTokenRepository>();
        services.AddScoped<IEmailService, EmailService>();
        services.AddScoped<IEmailProcessingService, EmailProcessingService>();
        services.AddScoped<IAuditService, AuditService>();
        services.AddScoped<IAttachmentService, AttachmentService>();
        services.AddScoped<IEmailQueueService, EmailQueueService>();
        services.AddScoped<IImMessageQueueService, ImMessageQueueService>();
        services.AddScoped<IImMessageLogRepository, ImMessageLogRepository>();
        // 补充注册 UnitOfWork 通过 DI 延迟解析的专用仓储
        services.AddScoped<ISendLogRepository, SendLogRepository>();
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
        services.AddScoped<ITranslationRepository, TranslationRepository>();
        services.AddScoped<ICalendarEventRepository, CalendarEventRepository>();
        services.AddScoped<IKnowledgeCategoryRepository, KnowledgeCategoryRepository>();
        services.AddScoped<IKnowledgeArticleRepository, KnowledgeArticleRepository>();
        services.AddScoped<IChatSessionRepository, ChatSessionRepository>();
        services.AddScoped<IChatMessageRepository, ChatMessageRepository>();
        services.AddScoped<ITimeEntryRepository, TimeEntryRepository>();
        services.AddScoped<ICustomReportRepository, CustomReportRepository>();
        services.AddScoped<ICustomReportScheduleRepository, CustomReportScheduleRepository>();
        services.AddScoped<IPasswordHasher, BCryptPasswordHasher>();
        services.AddSingleton<IEncryptionService, AesEncryptionService>();
        services.AddSingleton<AuditHmacSigner>();

        services.AddSingleton<ILocalizationService, LocalizationService>();
        // 后台预热翻译缓存，避免同步路径中的 sync-over-async 数据库查询
        services.AddHostedService<TranslationCacheWarmupService>();
        services.AddHttpContextAccessor();
        services.AddScoped<IFrontendUrlResolver, FrontendUrlResolver>();

        // SignalR 连接管理器（全局单例，维护 userId → connectionId 映射）
        services.AddSingleton<IConnectionManager, ConnectionManager>();
        services.AddScoped<INotificationHubService, NotificationHubService>();
        services.AddScoped<INotificationPublisher, NotificationPublisher>();

        // 验证码服务（基于 IDistributedCache：单实例 AddDistributedMemoryCache，多实例必须 Redis 等共享实现）
        services.AddScoped<ICaptchaService, CaptchaService>();

        services.AddQuartz(q =>
        {
            // M7: Quartz 集群存储（AdoJobStore + SQLite + isClustered）。
            // 默认关闭（Quartz:Cluster:Enabled=false）时保持 RAMJobStore 单实例行为；
            // 多实例部署时开启，各实例通过数据库锁竞争触发，避免定时任务重复执行。
            // 注意：SQLite 仅支持单文件共享，开启前需确保所有实例挂载同一数据库文件。
            var clusterEnabled = configuration.GetValue<bool?>("Quartz:Cluster:Enabled") ?? false;
            if (clusterEnabled)
            {
                var sqliteConnectionString = configuration.GetConnectionString("SqliteConnection")
                    ?? throw new InvalidOperationException("启用 Quartz 集群存储时，必须配置 ConnectionStrings:SqliteConnection");

                q.SchedulerId = "AUTO"; // 集群内实例唯一 ID，避免多实例使用相同实例名导致调度状态互踩

                q.UsePersistentStore(store =>
                {
                    store.UseProperties = true; // JobDataMap 以字符串列持久化，本项目全部以 GetString 读取，安全
                    store.UseMicrosoftSQLite(o => o.ConnectionString = sqliteConnectionString);
                    store.UseClustering(c =>
                    {
                        c.CheckinInterval = TimeSpan.FromSeconds(
                            configuration.GetValue<int?>("Quartz:Cluster:CheckinIntervalSeconds") ?? 15);
                        c.CheckinMisfireThreshold = TimeSpan.FromSeconds(
                            configuration.GetValue<int?>("Quartz:Cluster:CheckinMisfireThresholdSeconds") ?? 30);
                    });
                    store.RetryInterval = TimeSpan.FromSeconds(
                        configuration.GetValue<int?>("Quartz:Cluster:RetryIntervalSeconds") ?? 15);
                    // QRTZ_ 表由 QuartzSchemaInitializer 在调度器启动前引导创建，跳过内置校验
                    store.PerformSchemaValidation = false;
                });
            }

            // 邮件收取 Cron：优先从 SystemOptions 数据库表 mail.fetch_frequency 读取（分钟级），
            // 未配置或查询失败时回退到 appsettings.json Quartz:Jobs:FetchEmail / Email:FetchCron / 默认 5 分钟
            var fetchEmailCron = ResolveFetchEmailCronFromSystemOptions(configuration);
            // T000057: 邮件队列处理频率从 10 分钟改为 1 分钟，确保客服回复邮件能尽快发送给客户，
            // 客户回复后系统也能更快收取并显示在会话中
            var processEmailQueueCron = configuration["Quartz:Jobs:ProcessEmailQueue"]
                ?? "0 * * * * ?";
            var slaScanCron = configuration["Quartz:Jobs:SlaScan"]
                ?? "0 0/30 * * * ?";
            var calendarReminderCron = configuration["Quartz:Jobs:CalendarReminder"]
                ?? "0 * * * * ?";

            var fetchEmailJobKey = new JobKey("FetchEmailJob");
            q.AddJob<FetchEmailJob>(opts => opts.WithIdentity(fetchEmailJobKey));

            // 由 FetchEmailJob 内部根据每个邮箱的 FetchFrequency 决定是否收取
            q.AddTrigger(opts => opts
                .ForJob(fetchEmailJobKey)
                .WithIdentity("FetchEmailTrigger")
                .WithCronSchedule(fetchEmailCron));

            var processEmailQueueJobKey = new JobKey("ProcessEmailQueueJob");
            q.AddJob<ProcessEmailQueueJob>(opts => opts.WithIdentity(processEmailQueueJobKey));

            // 邮件队列处理：优先使用 Cron 配置，未配置时回退到 30 秒简单调度
            q.AddTrigger(opts => opts
                .ForJob(processEmailQueueJobKey)
                .WithIdentity("ProcessEmailQueueTrigger")
                .WithCronSchedule(processEmailQueueCron));

            // IM 消息队列处理：默认每分钟扫描一次（参考邮件队列配置）
            // 用于异步发送微信/飞书/WhatsApp/钉钉渠道消息，支持失败重试（指数退避）
            var processImMessageQueueCron = configuration["Quartz:Jobs:ProcessImMessageQueue"]
                ?? "0 * * * * ?";
            var processImMessageQueueJobKey = new JobKey("ProcessImMessageQueueJob");
            q.AddJob<ProcessImMessageQueueJob>(opts => opts.WithIdentity(processImMessageQueueJobKey));
            q.AddTrigger(opts => opts
                .ForJob(processImMessageQueueJobKey)
                .WithIdentity("ProcessImMessageQueueTrigger")
                .WithCronSchedule(processImMessageQueueCron));

            // SLA 违规扫描任务
            var slaScanJobKey = new JobKey("SlaScanJob");
            q.AddJob<SlaScanJob>(opts => opts.WithIdentity(slaScanJobKey));

            q.AddTrigger(opts => opts
                .ForJob(slaScanJobKey)
                .WithIdentity("SlaScanTrigger")
                .WithCronSchedule(slaScanCron));

            // 日历提醒扫描任务
            var calendarReminderJobKey = new JobKey("CalendarReminderJob");
            q.AddJob<CalendarReminderJob>(opts => opts.WithIdentity(calendarReminderJobKey));

            q.AddTrigger(opts => opts
                .ForJob(calendarReminderJobKey)
                .WithIdentity("CalendarReminderTrigger")
                .WithCronSchedule(calendarReminderCron));

            // P2P3 §3.2 #7.3：提醒推送兜底补发任务（A-scheme multi-tier fallback）
            // DEV-3 CORRECTION: 实现者修正 cron —— 规范要求 "* * * * * ?"（每秒）会放大 DB 负载
            // 且加大 SELECT→claim→UPDATE 的竞态窗口。改为 "0 * * * * ?"（每分钟第 0 秒），
            // 既满足分钟级兜底时效，又显著降低 DB 压力与重复发送风险。
            var fallbackJobKey = new JobKey("CalendarReminderFallbackJob");
            q.AddJob<CalendarReminderFallbackJob>(opts => opts.WithIdentity(fallbackJobKey));
            q.AddTrigger(opts => opts
                .ForJob(fallbackJobKey)
                .WithIdentity("CalendarReminderFallbackTrigger")
                .WithCronSchedule("0 * * * * ?"));

            // 日历回收站清理：默认每天凌晨 3 点
            var purgeCalendarTrashCron = configuration["Quartz:Jobs:PurgeCalendarTrash"]
                ?? "0 0 3 * * ?";
            var purgeCalendarTrashJobKey = new JobKey("PurgeCalendarTrashJob");
            q.AddJob<PurgeCalendarTrashJob>(opts => opts.WithIdentity(purgeCalendarTrashJobKey));
            q.AddTrigger(opts => opts
                .ForJob(purgeCalendarTrashJobKey)
                .WithIdentity("PurgeCalendarTrashTrigger")
                .WithCronSchedule(purgeCalendarTrashCron));

            // Outlook 日历双向同步：默认每 30 分钟
            var outlookSyncCron = configuration["Quartz:Jobs:OutlookCalendarSync"]
                ?? "0 0/30 * * * ?";
            var outlookSyncJobKey = new JobKey("OutlookCalendarSyncJob");
            q.AddJob<OutlookCalendarSyncJob>(opts => opts.WithIdentity(outlookSyncJobKey));
            q.AddTrigger(opts => opts
                .ForJob(outlookSyncJobKey)
                .WithIdentity("OutlookCalendarSyncTrigger")
                .WithCronSchedule(outlookSyncCron));

            // 自定义报表调度处理：默认每 5 分钟
            var processCustomReportSchedulesCron = configuration["Quartz:Jobs:ProcessCustomReportSchedules"]
                ?? "0 0/5 * * * ?";
            var processCustomReportSchedulesJobKey = new JobKey("ProcessCustomReportSchedulesJob");
            q.AddJob<ProcessCustomReportSchedulesJob>(opts => opts.WithIdentity(processCustomReportSchedulesJobKey));
            q.AddTrigger(opts => opts
                .ForJob(processCustomReportSchedulesJobKey)
                .WithIdentity("ProcessCustomReportSchedulesTrigger")
                .WithCronSchedule(processCustomReportSchedulesCron));

            // --- PurgeOutlookSyncLogJob（P2P3 #6 审计合规：每日 3:30 清理过期同步审计日志） ---
            var purgeOutlookLogCron = configuration["Quartz:Jobs:PurgeOutlookSyncLog"] ?? "0 30 3 * * ?";
            var purgeOutlookLogJobKey = new JobKey("PurgeOutlookSyncLogJob");
            q.AddJob<PurgeOutlookSyncLogJob>(opts => opts.WithIdentity(purgeOutlookLogJobKey));
            q.AddTrigger(opts => opts
                .ForJob(purgeOutlookLogJobKey)
                .WithIdentity("PurgeOutlookSyncLogTrigger")
                .WithCronSchedule(purgeOutlookLogCron));
        });

        // M7: 集群模式下，必须在 Quartz 调度器启动前完成 QRTZ_ 表引导建表
        // （IHostedService 按注册顺序启动，故此处先注册，再注册 AddQuartzHostedService）
        if (configuration.GetValue<bool?>("Quartz:Cluster:Enabled") ?? false)
        {
            services.AddHostedService<QuartzSchemaInitializer>();
        }

        services.AddQuartzHostedService(q => q.WaitForJobsToComplete = true);

        // ===== Outlook 日历双向同步服务注册 =====
        services.Configure<OutlookSyncOptions>(configuration.GetSection(OutlookSyncOptions.SectionName));
        services.Configure<OutlookSyncOptions>(configuration.GetSection("Calendar:OutlookSync"));
        // P2P3 #6：Outlook 审计日志保留天数（Options 配置，默认 90 天）
        services.Configure<NoneadCSS.Application.Configuration.PurgeOutlookSyncLogOptions>(o =>
            o.RetentionDays = configuration.GetValue<int?>("Calendar:OutlookSyncLogRetentionDays") ?? 90);
        // P2P3 #7：提醒确认窗口（默认 5 分钟）与其他日历全局配置
        services.Configure<NoneadCSS.Application.Configuration.CalendarOptions>(
            configuration.GetSection(NoneadCSS.Application.Configuration.CalendarOptions.SectionName));
        // C-O3: 使用 IHttpClientFactory 注册 HttpClient，避免 static HttpClient 的 DNS 过期和连接耗尽问题
        services.AddHttpClient<OutlookOAuthService>();
        services.AddScoped<IOutlookOAuthService, OutlookOAuthService>();
        services.AddHttpClient<OutlookGraphService>();
        services.AddScoped<IOutlookGraphService, OutlookGraphService>();
        services.AddScoped<IOutlookSyncService, OutlookSyncService>();
        // T000099: Outlook 同步有效配置提供器（合并 appsettings 基线 + SystemOptions 数据库覆盖）
        // 注册为 Singleton：内部使用 IMemoryCache 跨请求缓存 + IServiceScopeFactory 后台刷新，模式与 WeChatSettingsProvider 一致
        services.AddSingleton<IOutlookSettingsProvider, OutlookSettingsProvider>();

        // ===== 分布式锁（支持多实例横向扩展下的互斥） =====
        services.AddScoped<IDistributedLockService, DistributedLockService>();

        // ===== 微信服务号接入服务注册 =====
        services.AddSingleton<IWeChatSettingsProvider, WeChatSettingsProvider>();

        // 注意：必须使用 AddHttpClient<TInterface, TImplementation>() 重载，确保通过接口注入时
        // 拿到的是 HttpClient 工厂登记的实例（含工厂级配置）。
        // 若分别用 AddHttpClient<TImpl> + AddScoped<TInterface, TImpl>，则接口注入的实例不会应用 HttpClient 配置。
        // 出站微信 API 直连微信公众平台（https://api.weixin.qq.com/cgi-bin/...），无需任何网关鉴权头。
        services.AddHttpClient<IWeChatAccessTokenService, WeChatAccessTokenService>();
        services.AddHttpClient<IWeChatClient, WeChatClient>();

        // ===== WhatsApp Business Platform 接入服务注册 =====
        services.AddSingleton<IWhatsAppSettingsProvider, WhatsAppSettingsProvider>();
        services.AddHttpClient<WhatsAppClient>();
        services.AddScoped<IWhatsAppClient, WhatsAppClient>();

        // ===== 飞书机器人应用接入服务注册 =====
        services.AddSingleton<IFeishuSettingsProvider, FeishuSettingsProvider>();
        services.AddHttpClient<FeishuAccessTokenService>();
        services.AddScoped<IFeishuAccessTokenService, FeishuAccessTokenService>();
        services.AddHttpClient<FeishuClient>();
        services.AddScoped<IFeishuClient, FeishuClient>();
        // Stream 连接状态跟踪器（Singleton，跨请求共享，供 GetStatusAsync 查询）
        services.AddSingleton<IFeishuStreamConnectionStatus, FeishuStreamConnectionStatus>();
        // Stream 模式独立命名 HttpClient（与 FeishuClient 出站 API 请求隔离）
        services.AddHttpClient("FeishuStream")
            .ConfigureHttpClient(client => { client.Timeout = TimeSpan.FromSeconds(30); });
        services.AddHostedService<FeishuStreamHostedService>();

        // ===== DingTalk 钉钉开放平台接入服务注册 =====
        services.Configure<NoneadCSS.Application.Configuration.DingDingOptions>(
            configuration.GetSection(NoneadCSS.Application.Configuration.DingDingOptions.SectionName));
        services.AddSingleton<IDingDingSettingsProvider, DingDingSettingsProvider>();
        // Stream 连接状态跟踪器（Singleton，跨请求共享，供 GetStatusAsync 查询）
        services.AddSingleton<IDingTalkStreamConnectionStatus, DingTalkStreamConnectionStatus>();
        // 钉钉 API 域名 DNS 默认返回 IPv6，但开发机无 IPv6 出口，强制 IPv4 避免连接超时
        // 必须使用 AddHttpClient<TInterface, TImplementation>() 重载，确保通过 IDingDingClient 接口
        // 注入时也能应用 HttpClientFactory 配置（IPv4 handler）。
        // 若分别用 AddHttpClient<DingDingClient> + AddScoped<IDingDingClient, DingDingClient>，
        // 则接口注入的实例不经过 HttpClientFactory，IPv4 handler 不生效，导致 30s 超时。
        services.AddHttpClient<IDingDingClient, DingDingClient>()
            .ConfigurePrimaryHttpMessageHandler(CreateIpv4OnlyHandler);
        // Stream 模式：独立命名 HttpClient（Timeout=30s，注册用的 IHttpClientFactory
        // 在 HostedService 内创建实例），不与 DingDingClient 的出站 API 请求共用
        services.AddHttpClient("DingTalkStream")
            .ConfigureHttpClient(client => { client.Timeout = TimeSpan.FromSeconds(30); })
            .ConfigurePrimaryHttpMessageHandler(CreateIpv4OnlyHandler);
        services.AddHostedService<DingTalkStreamHostedService>();

        // ===== Messenger Platform（Meta Graph API，v25.0）接入服务注册 =====
        // 出站地址 graph.facebook.com 由 MessengerClient 按 settings.ApiVersion 拼接（版本号是配置项，Meta 无 v2x 别名）
        services.AddSingleton<IMessengerSettingsProvider, MessengerSettingsProvider>();
        // 必须用 AddHttpClient<TInterface, TImplementation>() 合并重载，拆开写会静默丢掉 HttpClientFactory 配置
        services.AddHttpClient<IMessengerClient, MessengerClient>();

        // ===== Telegram Bot API（webhook 入站 + 可选 polling 回退）=====
        services.AddSingleton<ITelegramSettingsProvider, TelegramSettingsProvider>();
        // 每 bot 共享的速率桶（全局 25/s、单 chat 1/s；自建 Bot API 时可在设置页调整）
        services.AddSingleton<TelegramThrottle>();
        services.AddSingleton<ITelegramPollingConnectionStatus, TelegramPollingConnectionStatus>();
        services.AddHttpClient<ITelegramClient, TelegramClient>();
        // polling 回退：Mode=polling 时长轮询拉取 Update；Mode=webhook 时空转探测（二者互斥，409）
        services.AddHostedService<TelegramPollingHostedService>();

        // ===== LINE Messaging API 接入服务注册 =====
        // 出站 api.line.me 与 api-data.line.me 两个基址均由 settings 里的绝对地址拼接，故无需 BaseAddress
        services.AddSingleton<ILineSettingsProvider, LineSettingsProvider>();
        services.AddHttpClient<ILineClient, LineClient>();

        // ===== iMessage / Apple Messages 接入服务注册 =====
        // 一套抽象两种传输：Mode=bridge（自建 macOS BlueBubbles，默认）/ amb_msp / amb_apple_direct（需 Apple 核准 MSP，默认关闭）
        services.AddSingleton<IIMessageSettingsProvider, IMessageSettingsProvider>();
        // 出站每分钟限流（自动化/批量发送有 Apple ID 锁定风险）
        services.AddSingleton<IMessageRateLimiter>();
        services.AddHttpClient<IIMessageClient, IMessageClient>();

        return services;
    }

    /// <summary>
    /// 创建强制使用 IPv4 的 SocketsHttpHandler。
    /// 钉钉 API 域名 DNS 默认返回 IPv6 地址，但部分内网环境无 IPv6 出口，
    /// 导致 HttpClient 在 IPv6 连接超时后才回退 IPv4，触发整体请求超时。
    /// </summary>
    private static SocketsHttpHandler CreateIpv4OnlyHandler() => new()
    {
        ConnectCallback = async (context, ct) =>
        {
            var host = context.DnsEndPoint.Host;
            var port = context.DnsEndPoint.Port;
            var addresses = await Dns.GetHostAddressesAsync(host, AddressFamily.InterNetwork, ct);
            if (addresses.Length == 0)
                throw new SocketException((int)SocketError.HostNotFound);
            var socket = new Socket(AddressFamily.InterNetwork, SocketType.Stream, ProtocolType.Tcp) { NoDelay = true };
            try
            {
                await socket.ConnectAsync(new IPEndPoint(addresses[0], port), ct);
                return new NetworkStream(socket, ownsSocket: true);
            }
            catch (Exception) when (socket is not null)
            {
                socket.Dispose();
                throw;
            }
        }
    };

    /// <summary>
    /// 从 SystemOptions 数据库表读取 mail.fetch_frequency（分钟），生成 Quartz cron 表达式。
    /// 查询失败或值无效时回退到 appsettings.json 配置或默认 5 分钟。
    /// 这样修改 SystemOption 后重启服务即可生效，无需修改 appsettings.json。
    /// </summary>
    private static string ResolveFetchEmailCronFromSystemOptions(IConfiguration configuration)
    {
        var fallbackCron = configuration["Quartz:Jobs:FetchEmail"]
            ?? configuration["Email:FetchCron"]
            ?? "0 0/5 * * * ?";

        try
        {
            var useSqlite = ResolveUseSqlite(configuration);
            var connStr = useSqlite
                ? configuration.GetConnectionString("SqliteConnection")
                : configuration.GetConnectionString("DefaultConnection");

            if (string.IsNullOrEmpty(connStr))
                return fallbackCron;

            using var conn = new Microsoft.Data.Sqlite.SqliteConnection(connStr);
            conn.Open();
            using var cmd = conn.CreateCommand();
            cmd.CommandText = "SELECT Value FROM SystemOptions WHERE Name = 'mail.fetch_frequency' LIMIT 1";
            var result = cmd.ExecuteScalar()?.ToString();

            if (int.TryParse(result, out var freq) && freq >= 1 && freq <= 60)
            {
                return $"0 0/{freq} * * * ?";
            }
        }
        catch (Exception ex)
        {
            // 数据库未初始化（首次启动）或查询失败，回退到默认值
            Log.Logger.Warning(ex, "读取邮件轮询配置失败，回退到默认值: {Message}", ex.Message);
        }

        return fallbackCron;
    }

    /// <summary>
    /// 解析 Database:UseSqlite：未配置或无法解析时按 SQLite 处理
    /// （与 Program.cs 生产环境 Fail-Fast 里"未配置 UseSqlite 时默认按 SQLite"的口径一致）。
    /// </summary>
    private static bool ResolveUseSqlite(IConfiguration configuration)
    {
        var raw = configuration["Database:UseSqlite"];
        return !bool.TryParse(raw, out var parsed) || parsed;
    }
}

