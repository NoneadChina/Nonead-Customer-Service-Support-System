using Microsoft.EntityFrameworkCore;
using Microsoft.AspNetCore.Hosting;
using Microsoft.Extensions.Logging;
using NoneadCSS.Domain.Entities;
using NoneadCSS.Domain.Enums;
using NoneadCSS.Infrastructure.Persistence;

namespace NoneadCSS.Api;

/// <summary>
/// 数据库种子数据初始化器
/// </summary>
public static class DatabaseSeeder
{
    /// <summary>
    /// 初始化数据库并填充种子数据
    /// </summary>
    /// <param name="context">数据库上下文</param>
    /// <param name="environment">主机环境</param>
    /// <param name="logger">日志记录器</param>
    public static async Task SeedAsync(NoneadCSSDbContext context, IWebHostEnvironment environment, ILogger logger)
    {
        await using var conn = context.Database.GetDbConnection();
        await conn.OpenAsync();
        
        await using var walCmd = conn.CreateCommand();
        walCmd.CommandText = "PRAGMA journal_mode=WAL;";
        await walCmd.ExecuteNonQueryAsync();
        
        await using var checkCmd = conn.CreateCommand();
        checkCmd.CommandText = "SELECT COUNT(*) FROM sqlite_master WHERE type='table'";
        var existingTableCount = Convert.ToInt32(await checkCmd.ExecuteScalarAsync());
        
        if (existingTableCount == 0)
        {
            logger.LogInformation("[SEED] Creating database tables from SQL script...");
            var sqlPath = Path.Combine(AppContext.BaseDirectory, "create_db.sql");
            if (!File.Exists(sqlPath))
            {
                sqlPath = Path.Combine(Path.GetDirectoryName(System.Reflection.Assembly.GetExecutingAssembly().Location) ?? "", "create_db.sql");
            }
            if (!File.Exists(sqlPath))
            {
                sqlPath = Path.Combine(Directory.GetCurrentDirectory(), "create_db.sql");
            }
            if (!File.Exists(sqlPath))
            {
                sqlPath = Path.Combine(Path.GetDirectoryName(System.Reflection.Assembly.GetEntryAssembly()?.Location) ?? "", "create_db.sql");
            }
            
            logger.LogInformation("[SEED] Trying SQL path: {SqlPath}", sqlPath);
            
            if (File.Exists(sqlPath))
            {
                var sqlContent = await File.ReadAllTextAsync(sqlPath);
                var sqlStatements = sqlContent.Split(';')
                    .Select(s => s.Trim())
                    .Where(s => !string.IsNullOrEmpty(s));
                
                foreach (var statement in sqlStatements)
                {
                    await using var createCmd = conn.CreateCommand();
                    createCmd.CommandText = statement;
                    await createCmd.ExecuteNonQueryAsync();
                }
                logger.LogInformation("[SEED] All tables created successfully");
            }
            else
            {
                logger.LogError("[SEED] SQL file not found: {SqlPath}", sqlPath);
                throw new FileNotFoundException("SQL script file not found", sqlPath);
            }
        }
        
        await using var countCmd = conn.CreateCommand();
        countCmd.CommandText = "SELECT COUNT(*) FROM sqlite_master WHERE type='table'";
        var tableCount = Convert.ToInt32(await countCmd.ExecuteScalarAsync());
        Console.WriteLine($"[SEED] Total tables: {tableCount}");
        await conn.CloseAsync();
        
        await EnsureCalendarEventsTableAsync(context);
        await EnsureRefreshTokensTableAsync(context);
        await EnsureKnowledgeTablesAsync(context);
        await EnsureChatTablesAsync(context);
        await EnsureTimeEntryActiveTimerIndexAsync(context);

        context.Database.ExecuteSqlRaw("PRAGMA encoding = 'UTF-8';");
        context.Database.ExecuteSqlRaw("PRAGMA foreign_keys = ON;");

        var isNewDatabase = !context.Users.Any();
        if (isNewDatabase)
        {
            context.Database.ExecuteSqlRaw("PRAGMA foreign_keys = OFF;");
        }

        User? adminUser = null;
        Mailbox? testMailbox = null;
        Customer? testCustomer = null;
        Conversation? testConversation = null;

        if (!context.Users.Any())
        {
            var adminEmail = Environment.GetEnvironmentVariable("ADMIN_EMAIL");
            var adminPassword = Environment.GetEnvironmentVariable("ADMIN_PASSWORD");

            // 默认凭据仅限开发环境使用：避免部署到非开发环境时静默启用已知口令。
            // 这是开源发布用的占位口令，不参与任何真实环境；首次登录后应立即修改。
            const string devAdminEmail = "admin@example.com";
            const string devAdminPassword = "ChangeMe!123";

            // 非开发环境：绝不使用硬编码凭据（即使误设环境名），必须显式配置 ADMIN_EMAIL / ADMIN_PASSWORD。
            // 未提供时跳过创建默认超管并记录警告，避免因已知口令引发安全风险，同时不阻塞应用启动。
            var createAdmin = true;
            if (environment.IsDevelopment())
            {
                adminEmail ??= devAdminEmail;
                adminPassword ??= devAdminPassword;
            }
            else if (string.IsNullOrEmpty(adminEmail) || string.IsNullOrEmpty(adminPassword))
            {
                logger.LogWarning("[SEED] 非开发环境未配置环境变量 ADMIN_EMAIL / ADMIN_PASSWORD，跳过创建默认超管，拒绝使用硬编码凭据。");
                createAdmin = false;
            }

            if (createAdmin)
            {
                logger.LogInformation("[SEED] 创建初始管理员账号：{AdminEmail}，角色：{Role}", adminEmail, UserRoleType.SuperAdmin);

                adminUser = new User
                {
                    FirstName = "Admin",
                    LastName = "User",
                    Email = adminEmail!,
                    Password = BCrypt.Net.BCrypt.HashPassword(adminPassword!),
                    Role = UserRoleType.SuperAdmin,
                    Status = UserStatus.Active
                };
                context.Users.Add(adminUser);
                await context.SaveChangesAsync();
            }
        }
        else
        {
            var adminEmail = Environment.GetEnvironmentVariable("ADMIN_EMAIL");
            if (string.IsNullOrEmpty(adminEmail))
            {
                // 开发环境才允许回退到默认演示账号邮箱；非开发环境未配置时跳过（不触碰硬编码凭据）
                if (environment.IsDevelopment())
                {
                    adminEmail = "admin@example.com";
                }
                else
                {
                    logger.LogWarning("[SEED] 非开发环境未配置环境变量 ADMIN_EMAIL，跳过按默认邮箱查询超管用于关联种子数据。");
                }
            }

            if (!string.IsNullOrEmpty(adminEmail))
            {
                adminUser = await context.Users.FirstOrDefaultAsync(u => u.Email == adminEmail);
            }
        }

        if (!context.Mailboxes.Any())
        {
            testMailbox = new Mailbox
            {
                Name = "支持邮箱",
                Email = "support@example.com",
                IsActive = true
            };
            context.Mailboxes.Add(testMailbox);
            await context.SaveChangesAsync();
        }
        else
        {
            testMailbox = await context.Mailboxes.FirstOrDefaultAsync(m => m.Email == "support@example.com");
        }

        if (!context.Folders.Any() && testMailbox != null)
        {
            var unassignedFolder = new Folder
            {
                Mailbox = testMailbox,
                Type = FolderType.Unassigned,
                Name = "未分配"
            };
            var mineFolder = new Folder
            {
                Mailbox = testMailbox,
                Type = FolderType.Mine,
                Name = "我的"
            };
            var closedFolder = new Folder
            {
                Mailbox = testMailbox,
                Type = FolderType.Closed,
                Name = "已关闭"
            };
            context.Folders.AddRange(unassignedFolder, mineFolder, closedFolder);
            await context.SaveChangesAsync();
        }

        if (environment.IsDevelopment())
        {
            if (!context.Customers.Any())
            {
                testCustomer = new Customer
                {
                    FirstName = "测试",
                    LastName = "客户"
                };
                testCustomer.Emails.Add(new Email { EmailAddress = "customer@example.com", Type = 1 });
                context.Customers.Add(testCustomer);
                await context.SaveChangesAsync();
            }
            else
            {
                testCustomer = await context.Customers
                    .Include(c => c.Emails)
                    .FirstOrDefaultAsync(c => c.Emails.Any(e => e.EmailAddress == "customer@example.com"));
            }

            if (!context.Conversations.Any() && testMailbox != null && testCustomer != null && adminUser != null)
            {
                testConversation = new Conversation
                {
                    Mailbox = testMailbox,
                    Customer = testCustomer,
                    User = adminUser,
                    Subject = "测试工单 - 系统登录问题",
                    CustomerEmail = "customer@example.com",
                    Status = ConversationStatus.Active,
                    State = ConversationState.Published,
                    LastReplyAt = DateTime.UtcNow
                };
                testConversation.Threads.Add(new ConversationThread
                {
                    Type = ThreadType.Customer,
                    Body = "<p>您好，我无法登录系统，提示密码错误。</p>",
                    From = "customer@example.com",
                    Customer = testCustomer
                });
                testConversation.Threads.Add(new ConversationThread
                {
                    Type = ThreadType.Message,
                    Body = "<p>您好，请问您的账号是什么？我帮您重置密码。</p>",
                    From = "support@example.com",
                    User = adminUser
                });
                context.Conversations.Add(testConversation);
                await context.SaveChangesAsync();
            }
            else
            {
                testConversation = await context.Conversations
                    .Include(c => c.Threads)
                    .FirstOrDefaultAsync(c => c.Subject == "测试工单 - 系统登录问题");
            }
        }

        if (!context.MailboxUsers.Any() && testMailbox != null && adminUser != null)
        {
            context.MailboxUsers.Add(new MailboxUser
            {
                Mailbox = testMailbox,
                User = adminUser
            });
            await context.SaveChangesAsync();
        }

        // 快捷回复模板种子数据
        if (!context.CannedResponses.Any() && adminUser != null)
        {
            var cannedResponses = new[]
            {
                new CannedResponse
                {
                    Name = "欢迎回复",
                    Category = "欢迎",
                    Body = "<p>您好，</p><p>感谢您联系我们的客服团队！我们已经收到您的消息，将尽快为您处理。</p><p>如有任何问题，请随时回复此邮件。</p><p>此致<br/>客服团队</p>",
                    IsShared = true,
                    SortOrder = 1,
                    CreatedByUser = adminUser,
                    Mailbox = testMailbox
                },
                new CannedResponse
                {
                    Name = "密码重置指引",
                    Category = "技术支持",
                    Body = "<p>您好，</p><p>关于您反馈的登录问题，请按以下步骤重置密码：</p><ol><li>点击登录页面的「忘记密码」链接</li><li>输入您的注册邮箱</li><li>查收重置邮件并点击链接</li><li>设置新密码后重新登录</li></ol><p>如果仍有问题，请提供您的账号信息以便我们核实。</p><p>此致<br/>技术支持团队</p>",
                    IsShared = true,
                    SortOrder = 2,
                    CreatedByUser = adminUser,
                    Mailbox = testMailbox
                },
                new CannedResponse
                {
                    Name = "退款申请确认",
                    Category = "退款",
                    Body = "<p>您好，</p><p>我们已收到您的退款申请。</p><p>请您提供以下信息以便我们尽快处理：</p><ul><li>订单编号</li><li>退款原因</li><li>支付方式</li></ul><p>我们将在 1-3 个工作日内审核并处理您的申请。</p><p>此致<br/>客服团队</p>",
                    IsShared = true,
                    SortOrder = 3,
                    CreatedByUser = adminUser,
                    Mailbox = testMailbox
                },
                new CannedResponse
                {
                    Name = "工单已关闭通知",
                    Category = "通用",
                    Body = "<p>您好，</p><p>您的工单已处理完毕并关闭。</p><p>如果您对服务满意，欢迎对本次服务进行评价。如果后续还有其他问题，欢迎随时联系我们。</p><p>感谢您的支持！</p><p>此致<br/>客服团队</p>",
                    IsShared = true,
                    SortOrder = 4,
                    CreatedByUser = adminUser,
                    Mailbox = testMailbox
                },
                new CannedResponse
                {
                    Name = "升级处理通知",
                    Category = "通用",
                    Body = "<p>您好，</p><p>您反馈的问题已升级至相关团队处理。</p><p>我们会在 <strong>24 小时内</strong>与您联系，请您耐心等待。</p><p>如需紧急处理，请直接回复此邮件并注明「紧急」。</p><p>此致<br/>客服团队</p>",
                    IsShared = true,
                    SortOrder = 5,
                    CreatedByUser = adminUser,
                    Mailbox = testMailbox
                },
                new CannedResponse
                {
                    Name = "节假日自动回复",
                    Category = "通知",
                    Body = "<p>您好，</p><p>感谢您的来信！由于法定节假日，客服团队值班人员有限，回复可能稍有延迟。</p><p>我们将于下一个工作日优先处理您的问题，给您带来的不便敬请谅解。</p><p>假期快乐！</p><p>此致<br/>客服团队</p>",
                    IsShared = true,
                    SortOrder = 6,
                    CreatedByUser = adminUser
                },
                new CannedResponse
                {
                    Name = "产品咨询通用回复",
                    Category = "售前",
                    Body = "<p>您好，</p><p>感谢您对我们产品的关注！</p><p>关于您咨询的产品信息，我为您简单介绍：</p><p>我们的产品提供 <strong>7 天免费试用</strong>，支持 <strong>全功能体验</strong>。如果您有具体的需求场景，欢迎告诉我们，我们将为您提供定制化的解决方案。</p><p>如需预约演示，请直接回复此邮件。</p><p>此致<br/>销售团队</p>",
                    IsShared = true,
                    SortOrder = 7,
                    CreatedByUser = adminUser
                }
            };
            context.CannedResponses.AddRange(cannedResponses);
            await context.SaveChangesAsync();
        }

        // 邮件发送日志种子数据（仅开发环境）
        if (environment.IsDevelopment() && !context.SendLogs.Any() && testConversation != null && testCustomer != null && adminUser != null)
        {
            var testThreads = testConversation.Threads.ToList();
            if (testThreads.Count >= 2)
            {
                var replyThread = testThreads[1];
                var sendLogs = new[]
                {
                    new SendLog
                    {
                        Thread = replyThread,
                        MessageId = "<msg-001@example.com>",
                        Email = "customer@example.com",
                        MailType = 1,
                        Status = 4,
                        Customer = testCustomer,
                        User = adminUser,
                        CreatedAt = DateTime.UtcNow.AddHours(-2)
                    },
                    new SendLog
                    {
                        Thread = replyThread,
                        MessageId = "<msg-002@example.com>",
                        Email = "customer@example.com",
                        MailType = 3,
                        Status = 4,
                        Customer = testCustomer,
                        CreatedAt = DateTime.UtcNow.AddHours(-2).AddMinutes(1)
                    },
                    new SendLog
                    {
                        Thread = replyThread,
                        MessageId = "<msg-003@example.com>",
                        Email = "customer@example.com",
                        MailType = 1,
                        Status = 5,
                        Customer = testCustomer,
                        User = adminUser,
                        CreatedAt = DateTime.UtcNow.AddHours(-1)
                    },
                    new SendLog
                    {
                        Thread = replyThread,
                        MessageId = "<msg-004@example.com>",
                        Email = "customer@example.com",
                        MailType = 2,
                        Status = 6,
                        Customer = testCustomer,
                        User = adminUser,
                        CreatedAt = DateTime.UtcNow.AddMinutes(-30)
                    },
                    new SendLog
                    {
                        Thread = replyThread,
                        MessageId = "<msg-005@example.com>",
                        Email = "customer@example.com",
                        MailType = 1,
                        Status = 1,
                        Customer = testCustomer,
                        User = adminUser,
                        CreatedAt = DateTime.UtcNow.AddMinutes(-5)
                    },
                };
                context.SendLogs.AddRange(sendLogs);
                await context.SaveChangesAsync();
            }
        }

        // 满意度评价种子数据（仅开发环境）
        if (environment.IsDevelopment() && !context.SatisfactionRatings.Any() && testConversation != null && testCustomer != null && adminUser != null)
        {
            var satisfactionRatings = new[]
            {
                new SatisfactionRating
                {
                    Conversation = testConversation,
                    Customer = testCustomer,
                    User = adminUser,
                    Rating = 3, // 满意
                    Comment = "客服回复很快，问题解决得很好！",
                    CustomerName = "测试客户",
                    CustomerEmail = "customer@example.com",
                    IpAddress = "127.0.0.1",
                    CreatedAt = DateTime.UtcNow.AddDays(-3)
                }
            };
            context.SatisfactionRatings.AddRange(satisfactionRatings);
            await context.SaveChangesAsync();
        }

        // 工作流自动化规则种子数据
        if (!context.Workflows.Any())
        {
            var workflows = new[]
            {
                new Workflow
                {
                    Name = "紧急工单自动分配",
                    Description = "包含「紧急」关键词的会话自动分配给管理员",
                    MailboxId = 1,
                    IsActive = true,
                    SortOrder = 1,
                    TriggerType = "new_conversation",
                    ConditionsJson = """{"matchType":"any","conditions":[{"field":"subject","operator":"contains","value":"紧急"},{"field":"subject","operator":"contains","value":"急"}]}""",
                    ActionsJson = """[{"type":"assign_user","value":"1"},{"type":"add_tag","value":"紧急"}]""",
                    CreatedAt = DateTime.UtcNow.AddDays(-7),
                    UpdatedAt = DateTime.UtcNow.AddDays(-7)
                },
                new Workflow
                {
                    Name = "自动关闭 spam 工单",
                    Description = "主题包含垃圾邮件关键词（spam/viagra/casino/lottery 等）的会话标记为垃圾邮件状态",
                    MailboxId = null,
                    IsActive = true,
                    SortOrder = 2,
                    TriggerType = "new_conversation",
                    ConditionsJson = """{"matchType":"any","conditions":[{"field":"subject","operator":"contains","value":"spam"},{"field":"subject","operator":"contains","value":"viagra"},{"field":"subject","operator":"contains","value":"casino"},{"field":"subject","operator":"contains","value":"lottery"},{"field":"subject","operator":"contains","value":"free money"},{"field":"subject","operator":"contains","value":"彩票"},{"field":"subject","operator":"contains","value":"博彩"},{"field":"subject","operator":"contains","value":"广告"}]}""",
                    ActionsJson = """[{"type":"set_status","value":"4"}]""",
                    CreatedAt = DateTime.UtcNow.AddDays(-5),
                    UpdatedAt = DateTime.UtcNow.AddDays(-5)
                },
                new Workflow
                {
                    Name = "VIP 客户优先处理",
                    Description = "来自 VIP 邮箱的客户会话自动添加 VIP 标签",
                    MailboxId = null,
                    IsActive = false,
                    SortOrder = 3,
                    TriggerType = "new_conversation",
                    ConditionsJson = """{"matchType":"any","conditions":[{"field":"from_email","operator":"ends_with","value":"@vip.com"},{"field":"from_email","operator":"ends_with","value":"@vip.cn"}]}""",
                    ActionsJson = """[{"type":"add_tag","value":"VIP"}]""",
                    CreatedAt = DateTime.UtcNow.AddDays(-2),
                    UpdatedAt = DateTime.UtcNow.AddDays(-2)
                }
            };
            context.Workflows.AddRange(workflows);
            await context.SaveChangesAsync();
        }

        // SLA 策略种子数据
        if (!context.SlaPolicies.Any())
        {
            var slaPolicies = new[]
            {
                new SlaPolicy
                {
                    Name = "标准响应时效",
                    Description = "适用于所有邮箱的默认 SLA：24 小时内首次响应，72 小时内解决",
                    MailboxId = null,
                    FirstResponseHours = 24.0,
                    ResolutionHours = 72.0,
                    BusinessHoursOnly = false,
                    Priority = 0,
                    IsActive = true,
                    SortOrder = 1,
                    CreatedAt = DateTime.UtcNow.AddDays(-10),
                    UpdatedAt = DateTime.UtcNow.AddDays(-10)
                },
                new SlaPolicy
                {
                    Name = "支持邮箱 VIP 时效",
                    Description = "支持邮箱的高优先级 SLA：2 小时内首次响应，24 小时内解决",
                    MailboxId = 1,
                    FirstResponseHours = 2.0,
                    ResolutionHours = 24.0,
                    BusinessHoursOnly = false,
                    Priority = 10,
                    IsActive = true,
                    SortOrder = 2,
                    CreatedAt = DateTime.UtcNow.AddDays(-8),
                    UpdatedAt = DateTime.UtcNow.AddDays(-8)
                },
                new SlaPolicy
                {
                    Name = "紧急工单时效",
                    Description = "1 小时内首次响应，8 小时内解决。用于紧急工单监控。",
                    MailboxId = null,
                    FirstResponseHours = 1.0,
                    ResolutionHours = 8.0,
                    BusinessHoursOnly = false,
                    Priority = 20,
                    IsActive = false,
                    SortOrder = 3,
                    CreatedAt = DateTime.UtcNow.AddDays(-3),
                    UpdatedAt = DateTime.UtcNow.AddDays(-3)
                }
            };
            context.SlaPolicies.AddRange(slaPolicies);
            await context.SaveChangesAsync();

            if (testConversation != null)
            {
                var vipPolicy = slaPolicies.First(p => p.Name == "支持邮箱 VIP 时效");
                var slaViolations = new[]
                {
                    new SlaViolation
                    {
                        SlaPolicy = vipPolicy,
                        Conversation = testConversation,
                        ViolationType = "first_response",
                        DueAt = testConversation.CreatedAt.AddHours(2),
                        DetectedAt = testConversation.CreatedAt.AddHours(1.5),
                        ResolvedAt = null,
                        Status = "breached",
                        CreatedAt = testConversation.CreatedAt.AddHours(1.5)
                    },
                    new SlaViolation
                    {
                        SlaPolicy = vipPolicy,
                        Conversation = testConversation,
                        ViolationType = "resolution",
                        DueAt = testConversation.CreatedAt.AddHours(24),
                        DetectedAt = testConversation.CreatedAt.AddHours(20),
                        ResolvedAt = null,
                        Status = "warning",
                        CreatedAt = testConversation.CreatedAt.AddHours(20)
                    }
                };
                context.SlaViolations.AddRange(slaViolations);
                vipPolicy.ViolationCount = 1;
                vipPolicy.AppliedCount = 1;
                vipPolicy.LastAppliedAt = DateTime.UtcNow.AddDays(-1);
                await context.SaveChangesAsync();
            }
        }

        // 宏种子数据
        if (!context.Macros.Any())
        {
            var macros = new[]
            {
                new Macro
                {
                    Name = "关闭并标记已完成",
                    Description = "将会话状态改为已关闭，并添加'已完成'标签",
                    MailboxId = null,
                    IsActive = true,
                    SortOrder = 0,
                    ActionsJson = "[{\"type\":\"status\",\"value\":3},{\"type\":\"add_tag\",\"value\":\"已完成\"}]",
                    UsageCount = 12,
                    LastUsedAt = DateTime.UtcNow.AddHours(-2),
                    CreatedAt = DateTime.UtcNow.AddDays(-10),
                    UpdatedAt = DateTime.UtcNow.AddDays(-5)
                },
                new Macro
                {
                    Name = "分配给技术支持组",
                    Description = "将会话分配给技术支持组（用户ID 2），并添加'技术支持'标签",
                    MailboxId = testMailbox != null ? testMailbox.Id : null,
                    IsActive = true,
                    SortOrder = 1,
                    ActionsJson = "[{\"type\":\"assign\",\"value\":2},{\"type\":\"add_tag\",\"value\":\"技术支持\"}]",
                    UsageCount = 8,
                    LastUsedAt = DateTime.UtcNow.AddHours(-5),
                    CreatedAt = DateTime.UtcNow.AddDays(-8),
                    UpdatedAt = DateTime.UtcNow.AddDays(-3)
                },
                new Macro
                {
                    Name = "标记为待处理并加标签",
                    Description = "将会话状态改为待处理，添加'待跟进'标签",
                    MailboxId = null,
                    IsActive = true,
                    SortOrder = 2,
                    ActionsJson = "[{\"type\":\"status\",\"value\":2},{\"type\":\"add_tag\",\"value\":\"待跟进\"}]",
                    UsageCount = 5,
                    LastUsedAt = DateTime.UtcNow.AddDays(-1),
                    CreatedAt = DateTime.UtcNow.AddDays(-6),
                    UpdatedAt = DateTime.UtcNow.AddDays(-2)
                },
                new Macro
                {
                    Name = "升级处理",
                    Description = "添加'紧急'标签，状态改为待处理，用于需要升级优先级的工单",
                    MailboxId = null,
                    IsActive = false,
                    SortOrder = 3,
                    ActionsJson = "[{\"type\":\"add_tag\",\"value\":\"紧急\"},{\"type\":\"status\",\"value\":2}]",
                    UsageCount = 0,
                    LastUsedAt = null,
                    CreatedAt = DateTime.UtcNow.AddDays(-4),
                    UpdatedAt = DateTime.UtcNow.AddDays(-1)
                }
            };
            context.Macros.AddRange(macros);
            await context.SaveChangesAsync();

            // 宏执行日志种子数据（仅开发环境）
            // 注意：ExecutedByUserId 必须引用**真实存在的**用户 Id。
            // 历史版本这里写死 1 / 2，在空库（SQLite EnsureCreated + 外键开启）上会直接
            // SQLite Error 19: FOREIGN KEY constraint failed，导致进程在 Kestrel 监听前崩掉。
            if (environment.IsDevelopment() && testConversation != null && adminUser != null)
            {
                var macroLogs = new[]
                {
                    new MacroExecutionLog
                    {
                        Macro = macros[0],
                        Conversation = testConversation,
                        ExecutedByUserId = adminUser.Id,
                        Status = "success",
                        ActionsExecuted = 2,
                        ExecutedAt = DateTime.UtcNow.AddHours(-2)
                    },
                    new MacroExecutionLog
                    {
                        Macro = macros[1],
                        Conversation = testConversation,
                        ExecutedByUserId = adminUser.Id,
                        Status = "success",
                        ActionsExecuted = 2,
                        ExecutedAt = DateTime.UtcNow.AddHours(-5)
                    },
                    new MacroExecutionLog
                    {
                        Macro = macros[2],
                        Conversation = testConversation,
                        ExecutedByUserId = adminUser.Id,
                        Status = "success",
                        ActionsExecuted = 2,
                        ExecutedAt = DateTime.UtcNow.AddDays(-1)
                    }
                };
                context.MacroExecutionLogs.AddRange(macroLogs);
                await context.SaveChangesAsync();
            }
        }

        // 自定义字段种子数据
        if (!context.CustomFields.Any())
        {
                var customFields = new[]
                {
                    new CustomField
                    {
                        Name = "优先级",
                        Type = "select",
                        EntityType = "conversation",
                        MailboxId = null,
                        IsRequired = false,
                        IsActive = true,
                        SortOrder = 1,
                        OptionsJson = "[\"低\",\"中\",\"高\",\"紧急\"]",
                        DefaultValue = "中",
                        Placeholder = "请选择优先级",
                        HelpText = "会话的紧急程度",
                        CreatedAt = DateTime.UtcNow.AddDays(-10),
                        UpdatedAt = DateTime.UtcNow.AddDays(-5)
                    },
                    new CustomField
                    {
                        Name = "订单号",
                        Type = "text",
                        EntityType = "conversation",
                        MailboxId = null,
                        IsRequired = false,
                        IsActive = true,
                        SortOrder = 2,
                        OptionsJson = null,
                        DefaultValue = null,
                        Placeholder = "请输入订单号",
                        HelpText = "关联的订单编号",
                        CreatedAt = DateTime.UtcNow.AddDays(-9),
                        UpdatedAt = DateTime.UtcNow.AddDays(-4)
                    },
                    new CustomField
                    {
                        Name = "客户等级",
                        Type = "select",
                        EntityType = "customer",
                        MailboxId = null,
                        IsRequired = false,
                        IsActive = true,
                        SortOrder = 1,
                        OptionsJson = "[\"普通会员\",\"银卡会员\",\"金卡会员\",\"钻石会员\"]",
                        DefaultValue = "普通会员",
                        Placeholder = "请选择客户等级",
                        HelpText = "客户的会员等级",
                        CreatedAt = DateTime.UtcNow.AddDays(-8),
                        UpdatedAt = DateTime.UtcNow.AddDays(-3)
                    },
                    new CustomField
                    {
                        Name = "产品型号",
                        Type = "text",
                        EntityType = "conversation",
                        MailboxId = 1,
                        IsRequired = false,
                        IsActive = true,
                        SortOrder = 3,
                        OptionsJson = null,
                        DefaultValue = null,
                        Placeholder = "请输入产品型号",
                        HelpText = "客户咨询的产品型号",
                        CreatedAt = DateTime.UtcNow.AddDays(-7),
                        UpdatedAt = DateTime.UtcNow.AddDays(-2)
                    },
                    new CustomField
                    {
                        Name = "预计完成日期",
                        Type = "date",
                        EntityType = "conversation",
                        MailboxId = null,
                        IsRequired = false,
                        IsActive = true,
                        SortOrder = 4,
                        OptionsJson = null,
                        DefaultValue = null,
                        Placeholder = "选择日期",
                        HelpText = "预计处理完成的日期",
                        CreatedAt = DateTime.UtcNow.AddDays(-6),
                        UpdatedAt = DateTime.UtcNow.AddDays(-1)
                    },
                    new CustomField
                    {
                        Name = "已验证身份",
                        Type = "checkbox",
                        EntityType = "customer",
                        MailboxId = null,
                        IsRequired = false,
                        IsActive = true,
                        SortOrder = 2,
                        OptionsJson = null,
                        DefaultValue = "false",
                        Placeholder = null,
                        HelpText = "客户是否已完成身份验证",
                        CreatedAt = DateTime.UtcNow.AddDays(-5),
                        UpdatedAt = DateTime.UtcNow.AddHours(-12)
                    }
                };
                context.CustomFields.AddRange(customFields);

                // 自定义字段值种子数据（仅开发环境）
                if (environment.IsDevelopment() && testConversation != null && testCustomer != null)
                {
                    var customFieldValues = new[]
                    {
                        new CustomFieldValue
                        {
                            CustomField = customFields[0],
                            EntityId = testConversation.Id,
                            EntityType = "conversation",
                            Value = "高",
                            CreatedAt = DateTime.UtcNow.AddHours(-3),
                            UpdatedAt = DateTime.UtcNow.AddHours(-3)
                        },
                        new CustomFieldValue
                        {
                            CustomField = customFields[1],
                            EntityId = testConversation.Id,
                            EntityType = "conversation",
                            Value = "ORD-20240115-001",
                            CreatedAt = DateTime.UtcNow.AddHours(-3),
                            UpdatedAt = DateTime.UtcNow.AddHours(-3)
                        },
                        new CustomFieldValue
                        {
                            CustomField = customFields[2],
                            EntityId = testCustomer.Id,
                            EntityType = "customer",
                            Value = "金卡会员",
                            CreatedAt = DateTime.UtcNow.AddDays(-5),
                            UpdatedAt = DateTime.UtcNow.AddDays(-5)
                        },
                        new CustomFieldValue
                        {
                            CustomField = customFields[5],
                            EntityId = testCustomer.Id,
                            EntityType = "customer",
                            Value = "true",
                            CreatedAt = DateTime.UtcNow.AddDays(-2),
                            UpdatedAt = DateTime.UtcNow.AddDays(-2)
                        }
                    };
                    context.CustomFieldValues.AddRange(customFieldValues);
                }
            }

            // Webhook 种子数据
            if (!context.Webhooks.Any())
            {
                var webhooks = new[]
                {
                    new Webhook
                    {
                        Name = "新会话通知",
                        Url = "https://example.com/webhook/new-conversation",
                        Method = "POST",
                        Events = "conversation_created",
                        HeadersJson = null,
                        Secret = null,
                        IsActive = true,
                        MailboxId = null,
                        TriggerCount = 15,
                        SuccessCount = 14,
                        FailCount = 1,
                        LastTriggeredAt = DateTime.UtcNow.AddHours(-2),
                        LastError = null,
                        CreatedAt = DateTime.UtcNow.AddDays(-10),
                        UpdatedAt = DateTime.UtcNow.AddHours(-2)
                    },
                    new Webhook
                    {
                        Name = "CRM 客户同步",
                        Url = "https://crm.example.com/api/sync-customer",
                        Method = "POST",
                        Events = "customer_created,customer_updated",
                        HeadersJson = "{\"X-Custom-Header\":\"NoneadCSS\"}",
                        Secret = null,
                        IsActive = true,
                        MailboxId = null,
                        TriggerCount = 42,
                        SuccessCount = 42,
                        FailCount = 0,
                        LastTriggeredAt = DateTime.UtcNow.AddHours(-5),
                        LastError = null,
                        CreatedAt = DateTime.UtcNow.AddDays(-8),
                        UpdatedAt = DateTime.UtcNow.AddHours(-5)
                    },
                    new Webhook
                    {
                        Name = "技术部工单系统",
                        Url = "https://ticket.example.com/api/webhook",
                        Method = "POST",
                        Events = "conversation_created,conversation_status_changed,thread_created",
                        HeadersJson = null,
                        Secret = null,
                        IsActive = false,
                        MailboxId = 1,
                        TriggerCount = 8,
                        SuccessCount = 6,
                        FailCount = 2,
                        LastTriggeredAt = DateTime.UtcNow.AddDays(-3),
                        LastError = "连接超时",
                        CreatedAt = DateTime.UtcNow.AddDays(-5),
                        UpdatedAt = DateTime.UtcNow.AddDays(-3)
                    },
                    new Webhook
                    {
                        Name = "数据分析平台",
                        Url = "https://analytics.example.com/webhook",
                        Method = "PUT",
                        Events = "conversation_updated,conversation_deleted",
                        HeadersJson = null,
                        Secret = null,
                        IsActive = true,
                        MailboxId = null,
                        TriggerCount = 28,
                        SuccessCount = 28,
                        FailCount = 0,
                        LastTriggeredAt = DateTime.UtcNow.AddHours(-1),
                        LastError = null,
                        CreatedAt = DateTime.UtcNow.AddDays(-6),
                        UpdatedAt = DateTime.UtcNow.AddHours(-1)
                    }
                };
                context.Webhooks.AddRange(webhooks);

                // Webhook 日志种子数据
                var webhookLogs = new[]
                {
                    new WebhookLog
                    {
                        Webhook = webhooks[0],
                        EventType = "conversation_created",
                        EntityType = "conversation",
                        EntityId = 5,
                        Url = webhooks[0].Url,
                        Method = "POST",
                        RequestBody = "{\"event_type\":\"conversation_created\",\"data\":{\"id\":5,\"subject\":\"测试问题\"...",
                        ResponseStatusCode = 200,
                        ResponseBody = "{\"ok\":true}",
                        IsSuccess = true,
                        DurationMs = 156,
                        CreatedAt = DateTime.UtcNow.AddHours(-2)
                    },
                    new WebhookLog
                    {
                        Webhook = webhooks[1],
                        EventType = "customer_updated",
                        EntityType = "customer",
                        EntityId = 3,
                        Url = webhooks[1].Url,
                        Method = "POST",
                        RequestBody = "{\"event_type\":\"customer_updated\",\"data\":{\"id\":3...",
                        ResponseStatusCode = 200,
                        ResponseBody = "{\"status\":\"synced\"}",
                        IsSuccess = true,
                        DurationMs = 89,
                        CreatedAt = DateTime.UtcNow.AddHours(-5)
                    },
                    new WebhookLog
                    {
                        Webhook = webhooks[2],
                        EventType = "conversation_created",
                        EntityType = "conversation",
                        EntityId = 8,
                        Url = webhooks[2].Url,
                        Method = "POST",
                        RequestBody = "{\"event_type\":\"conversation_created\",\"data\":{\"id\":8...",
                        ResponseStatusCode = null,
                        ResponseBody = null,
                        IsSuccess = false,
                        ErrorMessage = "连接超时",
                        DurationMs = 10000,
                        CreatedAt = DateTime.UtcNow.AddDays(-3)
                    }
                };
                context.WebhookLogs.AddRange(webhookLogs);
            }

            // 角色与权限种子数据
            if (!context.Roles.Any())
            {
                var seedRoles = new[]
                {
                    new Role
                    {
                        Name = "超级管理员",
                        Slug = "admin",
                        Description = "拥有所有系统权限",
                        PermissionsJson = "[\"*\"]",
                        IsSystem = true,
                        SortOrder = 1,
                        CreatedAt = DateTime.UtcNow,
                        UpdatedAt = DateTime.UtcNow
                    },
                    new Role
                    {
                        Name = "客服主管",
                        Slug = "supervisor",
                        Description = "管理团队和查看全部会话",
                        // 必须与下面「升级覆盖」分支里的 supervisor 权限清单逐项一致（历史缺陷：这里漏了全部渠道 .view，
                        // 升级分支又漏了 whatsapp.view / dingding.view，导致新装与老库升级的 supervisor 看到的面板不同）。
                        // 渠道 .manage 一律不下放给 supervisor（渠道配置写操作是 [RequireSuperAdmin]），保持只读口径。
                        PermissionsJson = "[\"conversations.view\",\"conversations.create\",\"conversations.update\",\"conversations.delete\",\"conversations.reply\",\"conversations.assign\",\"conversations.manage_tags\",\"conversations.view_all\",\"customers.view\",\"customers.create\",\"customers.update\",\"mailboxes.view\",\"mail_templates.view\",\"tags.view\",\"knowledge.view\",\"chat.view\",\"chat.reply\",\"calendar.view\",\"calendar.manage\",\"send_logs.view\",\"notifications.view\",\"satisfaction.view\",\"macros.view\",\"teams.view\",\"time-tracking.view\",\"time-tracking.manage\",\"wechat.view\",\"whatsapp.view\",\"feishu.view\",\"dingding.view\",\"messenger.view\",\"telegram.view\",\"line.view\",\"imessage.view\",\"profile.update\",\"profile.change_password\"]",
                        IsSystem = true,
                        SortOrder = 2,
                        CreatedAt = DateTime.UtcNow,
                        UpdatedAt = DateTime.UtcNow
                    },
                    new Role
                    {
                        Name = "客服专员",
                        Slug = "agent",
                        Description = "处理分配的会话",
                        // 注意：以下权限需与后端控制器的 [RequireSuperAdmin] 保持一致：
                        // - CalendarController 的 create/update/delete 均需超级管理员
                        // - EmailSignaturesController 的 list/create/update 均需超级管理员
        // - CannedResponsesController 的所有端点均需超级管理员
        // - MacrosController 需检查
        // 因此 agent 不能拥有 calendar.manage, email_signatures.manage,
        // canned_responses.manage 等权限，否则前端菜单显示后会被 403
        PermissionsJson = "[\"conversations.view\",\"conversations.create\",\"conversations.update\",\"conversations.reply\",\"conversations.manage_tags\",\"customers.view\",\"customers.create\",\"customers.update\",\"mailboxes.view\",\"macros.view\",\"knowledge.view\",\"chat.view\",\"chat.reply\",\"calendar.view\",\"calendar.manage\",\"notifications.view\",\"satisfaction.view\",\"time-tracking.view\",\"wechat.view\",\"whatsapp.view\",\"feishu.view\",\"dingding.view\",\"messenger.view\",\"messenger.manage\",\"telegram.view\",\"telegram.manage\",\"line.view\",\"line.manage\",\"imessage.view\",\"imessage.manage\",\"profile.update\",\"profile.change_password\"]",
                        IsSystem = true,
                        SortOrder = 3,
                        CreatedAt = DateTime.UtcNow,
                        UpdatedAt = DateTime.UtcNow
                    },
                    new Role
                    {
                        Name = "只读用户",
                        Slug = "viewer",
                        Description = "只能查看会话和客户",
                        PermissionsJson = "[\"conversations.view\",\"customers.view\",\"mailboxes.view\",\"mail_templates.view\",\"tags.view\",\"satisfaction.view\",\"teams.view\",\"macros.view\",\"notifications.view\",\"send_logs.view\",\"calendar.view\",\"knowledge.view\",\"chat.view\",\"time-tracking.view\",\"wechat.view\",\"whatsapp.view\",\"feishu.view\",\"dingding.view\",\"messenger.view\",\"telegram.view\",\"line.view\",\"imessage.view\",\"profile.update\",\"profile.change_password\"]",
                        IsSystem = true,
                        SortOrder = 4,
                        CreatedAt = DateTime.UtcNow,
                        UpdatedAt = DateTime.UtcNow
                    }
                };
                context.Roles.AddRange(seedRoles);

                // 给 admin 用户分配超级管理员角色
                context.UserRoles.Add(new UserRole
                {
                    User = adminUser,
                    Role = seedRoles[0],
                    MailboxId = null,
                    CreatedAt = DateTime.UtcNow
                });
            }
            else
            {
                var supervisorRole = await context.Roles.FirstOrDefaultAsync(r => r.Slug == "supervisor");
                if (supervisorRole != null)
                {
                    // 必须与上面「首次播种」分支里的 supervisor 权限清单逐项一致，否则老库升级后
                    // supervisor 会丢掉 whatsapp.view / dingding.view（历史缺陷：升级分支少写这两项，
                    // 导致 WhatsApp/钉钉会话页对 supervisor 显示 403）。渠道 .manage 一律不下放给
                    // supervisor（渠道配置写操作是 [RequireSuperAdmin]），保持只读口径。
                    supervisorRole.PermissionsJson = "[\"conversations.view\",\"conversations.create\",\"conversations.update\",\"conversations.delete\",\"conversations.reply\",\"conversations.assign\",\"conversations.manage_tags\",\"conversations.view_all\",\"customers.view\",\"customers.create\",\"customers.update\",\"mailboxes.view\",\"mail_templates.view\",\"tags.view\",\"knowledge.view\",\"chat.view\",\"chat.reply\",\"calendar.view\",\"calendar.manage\",\"send_logs.view\",\"notifications.view\",\"satisfaction.view\",\"macros.view\",\"teams.view\",\"time-tracking.view\",\"time-tracking.manage\",\"wechat.view\",\"whatsapp.view\",\"feishu.view\",\"dingding.view\",\"messenger.view\",\"telegram.view\",\"line.view\",\"imessage.view\",\"profile.update\",\"profile.change_password\"]";
                    supervisorRole.UpdatedAt = DateTime.UtcNow;
                }

                var agentRole = await context.Roles.FirstOrDefaultAsync(r => r.Slug == "agent");
                if (agentRole != null)
                {
                    // 注意：以下权限需与后端控制器的 [RequireSuperAdmin] 保持一致：
                    // - CalendarController 的 create/update/delete 均需超级管理员
                    // - EmailSignaturesController 的 list/create/update 均需超级管理员
                    // - CannedResponsesController 的所有端点均需超级管理员
                    // - TagsController 的写操作和垃圾箱均需超级管理员
                    // 因此 agent 不能拥有 calendar.manage, email_signatures.manage,
                    // canned_responses.manage, tags.manage 等权限，否则前端菜单显示后会被 403
                    agentRole.PermissionsJson = "[\"conversations.view\",\"conversations.create\",\"conversations.update\",\"conversations.reply\",\"conversations.manage_tags\",\"customers.view\",\"customers.create\",\"customers.update\",\"mailboxes.view\",\"macros.view\",\"knowledge.view\",\"chat.view\",\"chat.reply\",\"calendar.view\",\"calendar.manage\",\"notifications.view\",\"satisfaction.view\",\"time-tracking.view\",\"wechat.view\",\"whatsapp.view\",\"feishu.view\",\"dingding.view\",\"messenger.view\",\"messenger.manage\",\"telegram.view\",\"telegram.manage\",\"line.view\",\"line.manage\",\"imessage.view\",\"imessage.manage\",\"profile.update\",\"profile.change_password\"]";
                    agentRole.UpdatedAt = DateTime.UtcNow;
                }

                var viewerRole = await context.Roles.FirstOrDefaultAsync(r => r.Slug == "viewer");
                if (viewerRole != null)
                {
                    viewerRole.PermissionsJson = "[\"conversations.view\",\"customers.view\",\"mailboxes.view\",\"mail_templates.view\",\"tags.view\",\"satisfaction.view\",\"teams.view\",\"macros.view\",\"notifications.view\",\"send_logs.view\",\"calendar.view\",\"knowledge.view\",\"chat.view\",\"time-tracking.view\",\"wechat.view\",\"whatsapp.view\",\"feishu.view\",\"dingding.view\",\"messenger.view\",\"telegram.view\",\"line.view\",\"imessage.view\",\"profile.update\",\"profile.change_password\"]";
                    viewerRole.UpdatedAt = DateTime.UtcNow;
                }

                await context.SaveChangesAsync();
            }

            // 确保 time-tracking 模块在已有数据库中存在且被激活
            {
                var existingModule = await context.Modules.FirstOrDefaultAsync(m => m.Name == "time-tracking");
                if (existingModule == null)
                {
                    // 模块不存在，插入
                    context.Modules.Add(new Module
                    {
                        Name = "time-tracking",
                        Title = "时间追踪",
                        Description = "记录处理每个工单所花费的时间，支持工时统计与报表导出。",
                        Version = "1.3.0",
                        Author = "nCSS Team",
                        Icon = "clock",
                        IsActive = true,
                        Category = "tools",
                        SortOrder = 6,
                        CreatedAt = DateTime.UtcNow,
                        UpdatedAt = DateTime.UtcNow
                    });
                    await context.SaveChangesAsync();
                    Console.WriteLine("[SEED] time-tracking 模块已创建并激活");
                }
                else if (!existingModule.IsActive)
                {
                    existingModule.IsActive = true;
                    existingModule.UpdatedAt = DateTime.UtcNow;
                    await context.SaveChangesAsync();
                    Console.WriteLine("[SEED] time-tracking 模块已激活");
                }
            }

            // 邮件模板种子数据（按 slug 增量添加，确保每次启动都能补全新模板）
            {
                var existingSlugs = new HashSet<string>(
                    context.MailTemplates.Select(t => t.Slug),
                    StringComparer.OrdinalIgnoreCase);

                var mailTemplates = new[]
                {
                    new MailTemplate
                    {
                        Name = "新会话通知",
                        Slug = "new-conversation",
                        Type = MailTemplateType.NewConversation,
                        Subject = "[新会话] {{subject}}",
                        BodyHtml = """
                        <p>您好 {{user_name}}，</p>
                        <p>收到了一封新的客户会话：</p>
                        <table width="100%" cellpadding="8" cellspacing="0" border="0">
                          <tr>
                            <td width="20%" bgcolor="#f5f5f5"><strong>主题</strong></td>
                            <td>{{subject}}</td>
                          </tr>
                          <tr>
                            <td bgcolor="#f5f5f5"><strong>客户</strong></td>
                            <td>{{customer_name}} &lt;{{customer_email}}&gt;</td>
                          </tr>
                          <tr>
                            <td bgcolor="#f5f5f5"><strong>邮箱</strong></td>
                            <td>{{mailbox_name}}</td>
                          </tr>
                        </table>
                        <p><a href="{{conversation_url}}">查看会话详情 →</a></p>
                        <p>此致<br/>{{company_name}} 客服团队</p>
                        """,
                        BodyText = "您好 {{user_name}}，\n\n收到了一封新的客户会话：\n\n主题：{{subject}}\n客户：{{customer_name}} <{{customer_email}}>\n邮箱：{{mailbox_name}}\n\n查看详情：{{conversation_url}}\n\n此致\n{{company_name}} 客服团队",
                        IsActive = true,
                        IsSystem = true,
                        Description = "当有新会话创建时发送给客服的通知邮件",
                        SortOrder = 1,
                        CreatedAt = DateTime.UtcNow,
                        UpdatedAt = DateTime.UtcNow
                    },
                    new MailTemplate
                    {
                        Name = "新回复通知",
                        Slug = "new-reply",
                        Type = MailTemplateType.NewReply,
                        Subject = "[新回复] {{subject}}",
                        BodyHtml = """
                        <p>您好 {{user_name}}，</p>
                        <p>您跟进的会话收到了新回复：</p>
                        <div style="background:#f9f9f9;padding:15px;border-left:3px solid #007bff;margin:10px 0;">
                          <p><strong>{{customer_name}}</strong> 回复道：</p>
                          <p>{{message_body}}</p>
                        </div>
                        <p><a href="{{conversation_url}}">查看完整会话 →</a></p>
                        <p>此致<br/>{{company_name}} 客服团队</p>
                        """,
                        BodyText = "您好 {{user_name}}，\n\n您跟进的会话收到了新回复：\n\n{{customer_name}} 回复道：\n{{message_body}}\n\n查看完整会话：{{conversation_url}}\n\n此致\n{{company_name}} 客服团队",
                        IsActive = true,
                        IsSystem = true,
                        Description = "客户回复会话时发送给客服的通知邮件",
                        SortOrder = 2,
                        CreatedAt = DateTime.UtcNow,
                        UpdatedAt = DateTime.UtcNow
                    },
                    new MailTemplate
                    {
                        Name = "会话分配通知",
                        Slug = "conversation-assigned",
                        Type = MailTemplateType.ConversationAssigned,
                        Subject = "[会话分配] {{subject}}",
                        BodyHtml = """
                        <p>您好 {{user_name}}，</p>
                        <p>有一个会话已分配给您：</p>
                        <table width="100%" cellpadding="8" cellspacing="0" border="0">
                          <tr>
                            <td width="20%" bgcolor="#f5f5f5"><strong>主题</strong></td>
                            <td>{{subject}}</td>
                          </tr>
                          <tr>
                            <td bgcolor="#f5f5f5"><strong>客户</strong></td>
                            <td>{{customer_name}} &lt;{{customer_email}}&gt;</td>
                          </tr>
                        </table>
                        <p><a href="{{conversation_url}}">立即处理 →</a></p>
                        <p>此致<br/>{{company_name}} 客服团队</p>
                        """,
                        BodyText = "您好 {{user_name}}，\n\n有一个会话已分配给您：\n\n主题：{{subject}}\n客户：{{customer_name}} <{{customer_email}}>\n\n立即处理：{{conversation_url}}\n\n此致\n{{company_name}} 客服团队",
                        IsActive = true,
                        IsSystem = true,
                        Description = "会话分配给客服时发送的通知邮件",
                        SortOrder = 3,
                        CreatedAt = DateTime.UtcNow,
                        UpdatedAt = DateTime.UtcNow
                    },
                    new MailTemplate
                    {
                        Name = "会话关闭通知",
                        Slug = "conversation-closed",
                        Type = MailTemplateType.ConversationClosed,
                        Subject = "[会话关闭/Closed] {{subject}}",
                        BodyHtml = """
                        <div style="max-width:600px;margin:0 auto;font-family:-apple-system,BlinkMacSystemFont,'Segoe UI',Roboto,'Helvetica Neue',Arial,'PingFang SC','Microsoft YaHei',sans-serif;">
                          <div style="background:linear-gradient(135deg,#4f46e0 0%,#7c3aed 100%);padding:28px 40px;border-radius:12px 12px 0 0;">
                            <h1 style="margin:0;color:#fff;font-size:20px;font-weight:600;letter-spacing:0.5px;">会话已关闭 / Conversation Closed</h1>
                          </div>
                          <div style="background:#ffffff;padding:32px 40px;border:1px solid #e5e7eb;border-top:none;">
                            <p style="margin:0 0 16px;font-size:15px;line-height:1.7;color:#1f2937;">您好 {{customer_name}}，</p>
                            <p style="margin:0 0 16px;font-size:15px;line-height:1.7;color:#1f2937;">您的问题已经处理完毕，本次会话已正式关闭。感谢您选择 {{company_name}} 的服务！</p>
                            <div style="background:#f0f9ff;border:1px solid #bae6fd;border-radius:10px;padding:20px;margin:24px 0;">
                              <p style="margin:0 0 12px;font-size:14px;font-weight:600;color:#0369a1;">我们非常重视您的反馈</p>
                              <p style="margin:0 0 16px;font-size:14px;line-height:1.6;color:#475569;">请花一分钟时间，对本次服务进行评价。您的意见是我们持续改进的动力。</p>
                              <div style="text-align:center;">
                                <a href="{{satisfaction_url}}" style="display:inline-block;padding:12px 32px;background:#4f46e0;color:#fff;text-decoration:none;border-radius:8px;font-size:15px;font-weight:600;">评价本次服务 →</a>
                              </div>
                            </div>
                            <p style="margin:0 0 16px;font-size:15px;line-height:1.7;color:#1f2937;">如果后续还有其他问题，欢迎随时联系我们。</p>
                            <hr style="border:none;border-top:1px solid #e5e7eb;margin:28px 0;" />
                            <p style="margin:0 0 8px;font-size:15px;line-height:1.7;color:#6b7280;">Dear {{customer_name}},</p>
                            <p style="margin:0 0 16px;font-size:15px;line-height:1.7;color:#6b7280;">Your issue has been resolved and this conversation is now closed. Thank you for choosing {{company_name}}!</p>
                            <p style="margin:0 0 16px;font-size:15px;line-height:1.7;color:#6b7280;">We value your feedback. Please take a moment to rate our service — your input helps us improve.</p>
                            <p style="margin:0;font-size:15px;line-height:1.7;color:#6b7280;">If you have any further questions, please don't hesitate to contact us.</p>
                          </div>
                          <div style="background:#f9fafb;padding:20px 40px;border-radius:0 0 12px 12px;text-align:center;">
                            <p style="margin:0;font-size:13px;color:#6b7280;line-height:1.6;">{{company_name}} 客服团队 / Customer Support Team</p>
                            <p style="margin:4px 0 0;font-size:12px;color:#9ca3af;">此邮件为系统自动发送，请勿直接回复 / This is an automated email, please do not reply directly.</p>
                          </div>
                        </div>
                        """,
                        BodyText = "您好 {{customer_name}}，\n\n您的问题已经处理完毕，会话已关闭。感谢您选择 {{company_name}} 的服务！\n\n我们非常重视您的反馈，请花一分钟时间评价本次服务：\n{{satisfaction_url}}\n\n如果后续还有其他问题，欢迎随时联系我们。\n\n---\nDear {{customer_name}},\n\nYour issue has been resolved and this conversation is now closed. Thank you for choosing {{company_name}}!\n\nWe value your feedback. Please take a moment to rate our service:\n{{satisfaction_url}}\n\nIf you have any further questions, please don't hesitate to contact us.\n\n{{company_name}} 客服团队 / Customer Support Team",
                        IsActive = true,
                        IsSystem = true,
                        Description = "会话关闭时发送给客户的通知邮件（中英双语）",
                        SortOrder = 4,
                        CreatedAt = DateTime.UtcNow,
                        UpdatedAt = DateTime.UtcNow
                    },
                    new MailTemplate
                    {
                        Name = "IM 渠道会话关闭通知",
                        Slug = "im-conversation-closed",
                        Type = MailTemplateType.ConversationClosed,
                        Subject = "会话已关闭",
                        // IM 消息为纯文本，不使用 HTML；BodyText 为发送内容主体
                        BodyHtml = "",
                        BodyText = "您好 {{customer_name}}，您的问题已处理完毕，本次会话已关闭。感谢您选择 {{company_name}}！\n\n我们非常重视您的反馈，请点击下方链接评价本次服务：\n{{satisfaction_url}}\n\n如后续有其他问题，欢迎随时联系我们。",
                        IsActive = true,
                        IsSystem = true,
                        Description = "IM 渠道（微信/飞书/WhatsApp/钉钉）会话关闭时通过对应 App 发送的通知消息（纯文本，含满意度评价链接）",
                        SortOrder = 5,
                        CreatedAt = DateTime.UtcNow,
                        UpdatedAt = DateTime.UtcNow
                    },
                    new MailTemplate
                    {
                        Name = "在线聊天渠道会话关闭通知",
                        Slug = "chat-session-closed",
                        Type = MailTemplateType.ChatSessionClosed,
                        Subject = "【会话已关闭】在线聊天 #{{chat_session_id}} 结束通知",
                        // 需求3：纯文本，含满意度评价链接（BodyHtml 留空，不发 HTML 版本）
                        BodyHtml = "",
                        BodyText = """
                        您好 {{customer_name}}，

                        本次在线对话已经结束，感谢您联系 {{company_name}} 的客服团队！

                        请花 30 秒为本次服务评分，帮助我们做得更好：
                        【评价链接】{{satisfaction_url}}

                        本次会话信息：
                        - 会话编号：#{{chat_session_id}}
                        - 接入时间：{{chat_created_at}}
                        - 接待客服：{{agent_name}}
                        - 会话主题：{{chat_subject}}

                        如果您对本次服务还有任何建议或问题，欢迎随时通过同一链接再次联系我们。

                        再次感谢，期待下次再见！
                        {{company_name}} 客户服务中心

                        (此邮件为系统自动发送，请勿直接回复)
                        """,
                        VariablesJson = """
                        {
                          "customer_name": "客户姓名（可从 ChatSession.CustomerName/Customer 姓名回填）",
                          "company_name": "系统设置中的公司名",
                          "satisfaction_url": "满意度评价链接（带 chat session token 免登录）",
                          "chat_session_id": "在线聊天会话编号（ChatSession.Id）",
                          "chat_created_at": "会话接入时间（格式化）",
                          "agent_name": "接待客服姓名",
                          "chat_subject": "会话主题/首条消息摘要"
                        }
                        """,
                        IsActive = true,
                        IsSystem = true,
                        Description = "需求3：在线聊天（Widget 渠道）会话最终关闭时，向客户邮箱发送的纯文本会话关闭通知，必含满意度评价链接。区别于通用 conversation-closed，是在线聊天渠道专属文案。",
                        SortOrder = 15,
                        CreatedAt = DateTime.UtcNow,
                        UpdatedAt = DateTime.UtcNow
                    },
                    new MailTemplate
                    {
                        Name = "[聊天]关闭满意度请求消息（聊天内）",
                        Slug = "chat-close-requested",
                        Type = MailTemplateType.Custom,
                        Subject = "（在线聊天内系统消息，Subject 不用）",
                        // 在线聊天 Widget 内发送的系统消息（纯文本），保持短句以便在手机端与聊天流宽度内可读性
                        BodyHtml = "",
                        BodyText = """
                        您好 {{customer_name}}，您的问题已经处理完毕，如还有其他疑问请随时提出。
                        为帮助我们改进服务，请花几秒钟对本次服务打个分吧：
                        ⭐ 非常满意 (5分)  → 点 "5 星"
                        😊 满意 (4分)      → 点 "4 星"
                        🙂 一般 (3分)      → 点 "3 星"
                        😐 不太满意 (2分)  → 点 "2 星"
                        😞 不满意 (1分)    → 点 "1 星"
                        完成评分后会话将自动关闭，感谢您的反馈 💯
                        （若 3 分钟内未评分，会话将自动关闭）
                        """,
                        VariablesJson = """
                        {
                          "customer_name": "客户姓名"
                        }
                        """,
                        IsActive = true,
                        IsSystem = true,
                        Description = "需求2：系统工作流 [聊天]关闭满意度消息 使用的内容模板。在线聊天客服主动点关闭 → 会话进入 awaiting_rating 阶段后，自动在聊天流里插入这条 system message。",
                        SortOrder = 16,
                        CreatedAt = DateTime.UtcNow,
                        UpdatedAt = DateTime.UtcNow
                    },
                    new MailTemplate
                    {
                        Name = "[聊天]满意度评价感谢消息（聊天内）",
                        Slug = "chat-satisfaction-thanks",
                        Type = MailTemplateType.Custom,
                        Subject = "（在线聊天内系统消息，Subject 不用）",
                        BodyHtml = "",
                        BodyText = """
                        感谢您的评分：{{rating_stars}} （{{rating_label}}）
                        {{rating_comment}}
                        您的反馈对我们非常重要，我们会持续改进服务质量。
                        本次会话已关闭，期待下次再见！ 👋
                        """,
                        VariablesJson = """
                        {
                          "rating_stars": "评分对应的星星符号（如 '★★★★☆'）",
                          "rating_label": "评分中文标签（如 '非常满意/满意/一般/不太满意/不满意'）",
                          "rating_comment": "客户可选填写的评论文本（没填时为空字符串）"
                        }
                        """,
                        IsActive = true,
                        IsSystem = true,
                        Description = "需求2：系统工作流 [聊天]满意度评价感谢消息 使用的内容模板。当客户在 awaiting_rating 阶段完成评分后，自动在聊天流里插入这条感谢 system message（随后会话真正关闭）。",
                        SortOrder = 17,
                        CreatedAt = DateTime.UtcNow,
                        UpdatedAt = DateTime.UtcNow
                    },
                    new MailTemplate
                    {
                        Name = "满意度调查",
                        Slug = "satisfaction-survey",
                        Type = MailTemplateType.SatisfactionSurvey,
                        Subject = "[服务评价/Service Rating] {{subject}}",
                        BodyHtml = """
                        <div style="max-width:600px;margin:0 auto;font-family:-apple-system,BlinkMacSystemFont,'Segoe UI',Roboto,'Helvetica Neue',Arial,'PingFang SC','Microsoft YaHei',sans-serif;">
                          <div style="background:linear-gradient(135deg,#4f46e0 0%,#7c3aed 100%);padding:28px 40px;border-radius:12px 12px 0 0;">
                            <h1 style="margin:0;color:#fff;font-size:20px;font-weight:600;letter-spacing:0.5px;">服务满意度调查 / Satisfaction Survey</h1>
                          </div>
                          <div style="background:#ffffff;padding:32px 40px;border:1px solid #e5e7eb;border-top:none;">
                            <p style="margin:0 0 16px;font-size:15px;line-height:1.7;color:#1f2937;">您好 {{customer_name}}，</p>
                            <p style="margin:0 0 16px;font-size:15px;line-height:1.7;color:#1f2937;">感谢您使用我们的服务！请花一分钟时间，对本次服务进行评价：</p>
                            <p style="margin:0 0 20px;font-size:17px;font-weight:600;text-align:center;color:#1f2937;">您对本次服务满意吗？</p>
                            <table width="100%" cellpadding="0" cellspacing="0" border="0" style="margin:0 0 24px;">
                              <tr>
                                <td align="center" style="padding:6px 4px;">
                                  <a href="{{satisfaction_url}}?rating=5" style="display:block;width:100%;max-width:180px;padding:14px 8px;background:#16a34a;color:#fff;text-decoration:none;border-radius:10px;font-size:14px;font-weight:600;line-height:1.4;">😍 远超期望</a>
                                </td>
                                <td align="center" style="padding:6px 4px;">
                                  <a href="{{satisfaction_url}}?rating=4" style="display:block;width:100%;max-width:180px;padding:14px 8px;background:#059669;color:#fff;text-decoration:none;border-radius:10px;font-size:14px;font-weight:600;line-height:1.4;">😊 物有所值</a>
                                </td>
                              </tr>
                              <tr>
                                <td align="center" style="padding:6px 4px;">
                                  <a href="{{satisfaction_url}}?rating=3" style="display:block;width:100%;max-width:180px;padding:14px 8px;background:#d97706;color:#fff;text-decoration:none;border-radius:10px;font-size:14px;font-weight:600;line-height:1.4;">😐 中规中矩</a>
                                </td>
                                <td align="center" style="padding:6px 4px;">
                                  <a href="{{satisfaction_url}}?rating=2" style="display:block;width:100%;max-width:180px;padding:14px 8px;background:#ea580c;color:#fff;text-decoration:none;border-radius:10px;font-size:14px;font-weight:600;line-height:1.4;">😟 略有失望</a>
                                </td>
                              </tr>
                              <tr>
                                <td align="center" colspan="2" style="padding:6px 4px;">
                                  <a href="{{satisfaction_url}}?rating=1" style="display:inline-block;padding:14px 32px;background:#dc2626;color:#fff;text-decoration:none;border-radius:10px;font-size:14px;font-weight:600;line-height:1.4;">😡 无法接受</a>
                                </td>
                              </tr>
                            </table>
                            <p style="margin:0 0 16px;font-size:14px;line-height:1.6;color:#475569;text-align:center;">您的反馈对我们非常重要！点击对应选项即可完成评价。</p>
                            <hr style="border:none;border-top:1px solid #e5e7eb;margin:28px 0;" />
                            <p style="margin:0 0 8px;font-size:15px;line-height:1.7;color:#6b7280;">Dear {{customer_name}},</p>
                            <p style="margin:0 0 16px;font-size:15px;line-height:1.7;color:#6b7280;">Thank you for using our service! Please take a moment to rate your experience:</p>
                            <p style="margin:0 0 20px;font-size:17px;font-weight:600;text-align:center;color:#6b7280;">How was our service?</p>
                            <table width="100%" cellpadding="0" cellspacing="0" border="0" style="margin:0 0 24px;">
                              <tr>
                                <td align="center" style="padding:6px 4px;">
                                  <a href="{{satisfaction_url}}?rating=5" style="display:block;width:100%;max-width:180px;padding:14px 8px;background:#16a34a;color:#fff;text-decoration:none;border-radius:10px;font-size:14px;font-weight:600;line-height:1.4;">😍 Exceeds Expectations</a>
                                </td>
                                <td align="center" style="padding:6px 4px;">
                                  <a href="{{satisfaction_url}}?rating=4" style="display:block;width:100%;max-width:180px;padding:14px 8px;background:#059669;color:#fff;text-decoration:none;border-radius:10px;font-size:14px;font-weight:600;line-height:1.4;">😊 Good Value</a>
                                </td>
                              </tr>
                              <tr>
                                <td align="center" style="padding:6px 4px;">
                                  <a href="{{satisfaction_url}}?rating=3" style="display:block;width:100%;max-width:180px;padding:14px 8px;background:#d97706;color:#fff;text-decoration:none;border-radius:10px;font-size:14px;font-weight:600;line-height:1.4;">😐 Average</a>
                                </td>
                                <td align="center" style="padding:6px 4px;">
                                  <a href="{{satisfaction_url}}?rating=2" style="display:block;width:100%;max-width:180px;padding:14px 8px;background:#ea580c;color:#fff;text-decoration:none;border-radius:10px;font-size:14px;font-weight:600;line-height:1.4;">😟 Disappointing</a>
                                </td>
                              </tr>
                              <tr>
                                <td align="center" colspan="2" style="padding:6px 4px;">
                                  <a href="{{satisfaction_url}}?rating=1" style="display:inline-block;padding:14px 32px;background:#dc2626;color:#fff;text-decoration:none;border-radius:10px;font-size:14px;font-weight:600;line-height:1.4;">😡 Unacceptable</a>
                                </td>
                              </tr>
                            </table>
                            <p style="margin:0;font-size:14px;line-height:1.6;color:#6b7280;text-align:center;">Your feedback is very important to us! Click an option above to complete your rating.</p>
                          </div>
                          <div style="background:#f9fafb;padding:20px 40px;border-radius:0 0 12px 12px;text-align:center;">
                            <p style="margin:0;font-size:13px;color:#6b7280;line-height:1.6;">{{company_name}} 客服团队 / Customer Support Team</p>
                            <p style="margin:4px 0 0;font-size:12px;color:#9ca3af;">此邮件为系统自动发送，请勿直接回复 / This is an automated email, please do not reply directly.</p>
                          </div>
                        </div>
                        """,
                        BodyText = "您好 {{customer_name}}，\n\n感谢您使用我们的服务！请对本次服务进行评价：\n\n  5 - 远超期望  {{satisfaction_url}}?rating=5\n  4 - 物有所值  {{satisfaction_url}}?rating=4\n  3 - 中规中矩  {{satisfaction_url}}?rating=3\n  2 - 略有失望  {{satisfaction_url}}?rating=2\n  1 - 无法接受  {{satisfaction_url}}?rating=1\n\n您的反馈对我们非常重要！\n\n---\nDear {{customer_name}},\n\nThank you for using our service! Please rate your experience:\n\n  5 - Exceeds Expectations  {{satisfaction_url}}?rating=5\n  4 - Good Value             {{satisfaction_url}}?rating=4\n  3 - Average                {{satisfaction_url}}?rating=3\n  2 - Disappointing          {{satisfaction_url}}?rating=2\n  1 - Unacceptable           {{satisfaction_url}}?rating=1\n\nYour feedback is very important to us!\n\n{{company_name}} 客服团队 / Customer Support Team",
                        IsActive = true,
                        IsSystem = true,
                        Description = "会话关闭后发送给客户的满意度调查邮件（中英双语，5分制）",
                        SortOrder = 5,
                        CreatedAt = DateTime.UtcNow,
                        UpdatedAt = DateTime.UtcNow
                    },
                    new MailTemplate
                    {
                        Name = "密码重置",
                        Slug = "password-reset",
                        Type = MailTemplateType.PasswordReset,
                        Subject = "重置您的密码",
                        BodyHtml = """
                        <p>您好 {{user_name}}，</p>
                        <p>我们收到了您的密码重置请求。</p>
                        <p>请点击下面的链接重置您的密码：</p>
                        <p style="text-align:center;margin:20px 0;">
                          <a href="{{reset_password_url}}" style="display:inline-block;padding:10px 20px;background:#007bff;color:#fff;text-decoration:none;border-radius:4px;">重置密码</a>
                        </p>
                        <p>如果链接无法点击，请复制以下地址到浏览器：</p>
                        <p style="background:#f5f5f5;padding:10px;font-family:monospace;word-break:break-all;">{{reset_password_url}}</p>
                        <p><strong>注意：</strong>此链接将在 24 小时后失效。</p>
                        <p>如果您没有请求重置密码，请忽略此邮件。</p>
                        <p>此致<br/>{{company_name}} 团队</p>
                        """,
                        BodyText = "您好 {{user_name}}，\n\n我们收到了您的密码重置请求。\n\n请点击下面的链接重置您的密码：\n{{reset_password_url}}\n\n注意：此链接将在 24 小时后失效。\n\n如果您没有请求重置密码，请忽略此邮件。\n\n此致\n{{company_name}} 团队",
                        IsActive = true,
                        IsSystem = true,
                        Description = "用户请求重置密码时发送的邮件",
                        SortOrder = 6,
                        CreatedAt = DateTime.UtcNow,
                        UpdatedAt = DateTime.UtcNow
                    },
                    new MailTemplate
                    {
                        Name = "欢迎邮件",
                        Slug = "welcome",
                        Type = MailTemplateType.Welcome,
                        Subject = "欢迎使用 {{company_name}}",
                        BodyHtml = """
                        <p>您好 {{user_name}}，</p>
                        <p>欢迎使用 {{company_name}} 客服系统！</p>
                        <p>您的账号已创建成功：</p>
                        <ul>
                          <li><strong>用户名：</strong>{{user_email}}</li>
                          <li><strong>登录链接：</strong><a href="{{login_url}}">{{login_url}}</a></li>
                        </ul>
                        <p>如有任何问题，请随时联系我们的支持团队。</p>
                        <p>祝您使用愉快！</p>
                        <p>此致<br/>{{company_name}} 团队</p>
                        """,
                        BodyText = "您好 {{user_name}}，\n\n欢迎使用 {{company_name}} 客服系统！\n\n您的账号已创建成功：\n- 用户名：{{user_email}}\n- 登录链接：{{login_url}}\n\n如有任何问题，请随时联系我们的支持团队。\n\n祝您使用愉快！\n\n此致\n{{company_name}} 团队",
                        IsActive = true,
                        IsSystem = true,
                        Description = "新用户注册时发送的欢迎邮件",
                        SortOrder = 7,
                        CreatedAt = DateTime.UtcNow,
                        UpdatedAt = DateTime.UtcNow
                    },
                    // 新增必要系统模板：账号激活、邮箱验证
                    new MailTemplate
                    {
                        Name = "账号激活",
                        Slug = "account-activation",
                        Type = MailTemplateType.Welcome,
                        Subject = "激活您的 {{company_name}} 账号",
                        BodyHtml = """
                        <p>您好 {{user_name}}，</p>
                        <p>感谢您注册 {{company_name}}！</p>
                        <p>请点击下方按钮激活您的账号：</p>
                        <p style="text-align:center;margin:20px 0;">
                          <a href="{{activation_url}}" style="display:inline-block;padding:10px 24px;background:#28a745;color:#fff;text-decoration:none;border-radius:4px;font-weight:bold;">激活账号</a>
                        </p>
                        <p style="background:#f5f5f5;padding:10px;font-family:monospace;word-break:break-all;">{{activation_url}}</p>
                        <p><strong>注意：</strong>此激活链接将在 48 小时后失效。</p>
                        <p>如果您没有注册账号，请忽略此邮件。</p>
                        <p>此致<br/>{{company_name}} 团队</p>
                        """,
                        BodyText = "您好 {{user_name}}，\n\n感谢您注册 {{company_name}}！\n\n请访问以下链接激活您的账号：\n{{activation_url}}\n\n注意：此激活链接将在 48 小时后失效。\n\n如果您没有注册账号，请忽略此邮件。\n\n此致\n{{company_name}} 团队",
                        IsActive = true,
                        IsSystem = true,
                        Description = "用户注册后需要激活账号时发送的邮件",
                        SortOrder = 8,
                        CreatedAt = DateTime.UtcNow,
                        UpdatedAt = DateTime.UtcNow
                    },
                    // 新增必要自定义模板：客户回复模板（自动调用）
                    new MailTemplate
                    {
                        Name = "通用客户回复模板",
                        Slug = "customer-reply-default",
                        Type = MailTemplateType.Custom,
                        Subject = "Re: {{subject}}",
                        BodyHtml = """
                        <p>您好 {{customer_name}}，</p>
                        <p>感谢您联系我们。关于您提到的问题，我们已经收到并处理如下：</p>
                        <div style="background:#f9f9f9;padding:15px;border-left:3px solid #007bff;margin:10px 0;">
                          <p><strong>{{user_name}}</strong> 回复道：</p>
                          <p>{{message_body}}</p>
                        </div>
                        <p>如果还有其他问题，请随时回复本邮件。</p>
                        <p>此致<br/>{{company_name}} 客服团队</p>
                        """,
                        BodyText = "您好 {{customer_name}}，\n\n感谢您联系我们。关于您提到的问题，我们已经收到并处理如下：\n\n{{user_name}} 回复道：\n{{message_body}}\n\n如果还有其他问题，请随时回复本邮件。\n\n此致\n{{company_name}} 客服团队",
                        IsActive = true,
                        IsSystem = false,
                        Description = "默认的客户回复模板，作为 new-reply 模板的后备（自动调用）",
                        SortOrder = 100,
                        CreatedAt = DateTime.UtcNow,
                        UpdatedAt = DateTime.UtcNow
                    },
                    // 新增自定义模板：工单超时提醒
                    new MailTemplate
                    {
                        Name = "工单超时提醒",
                        Slug = "sla-warning",
                        Type = MailTemplateType.Custom,
                        Subject = "[超时提醒] {{subject}} 即将超过 SLA",
                        BodyHtml = """
                        <p>您好 {{user_name}}，</p>
                        <p>您处理的会话即将超过 SLA 时限，请尽快响应：</p>
                        <table width="100%" cellpadding="8" cellspacing="0" border="0">
                          <tr>
                            <td width="20%" bgcolor="#fff3cd"><strong>主题</strong></td>
                            <td>{{subject}}</td>
                          </tr>
                          <tr>
                            <td bgcolor="#fff3cd"><strong>客户</strong></td>
                            <td>{{customer_name}} &lt;{{customer_email}}&gt;</td>
                          </tr>
                          <tr>
                            <td bgcolor="#fff3cd"><strong>剩余时间</strong></td>
                            <td>{{sla_remaining}}</td>
                          </tr>
                        </table>
                        <p style="text-align:center;margin:20px 0;">
                          <a href="{{conversation_url}}" style="display:inline-block;padding:10px 20px;background:#ffc107;color:#fff;text-decoration:none;border-radius:4px;">立即处理 →</a>
                        </p>
                        <p>此致<br/>{{company_name}} 系统通知</p>
                        """,
                        BodyText = "您好 {{user_name}}，\n\n您处理的会话即将超过 SLA 时限，请尽快响应：\n\n主题：{{subject}}\n客户：{{customer_name}} <{{customer_email}}>\n剩余时间：{{sla_remaining}}\n\n立即处理：{{conversation_url}}\n\n此致\n{{company_name}} 系统通知",
                        IsActive = true,
                        IsSystem = false,
                        Description = "工单即将超过 SLA 时限发送给客服的提醒邮件",
                        SortOrder = 101,
                        CreatedAt = DateTime.UtcNow,
                        UpdatedAt = DateTime.UtcNow
                    },
                    // 新增自定义模板：满意度感谢邮件
                    new MailTemplate
                    {
                        Name = "满意度评价感谢",
                        Slug = "satisfaction-thanks",
                        Type = MailTemplateType.Custom,
                        Subject = "[感谢您的评价/Thank You] {{subject}}",
                        BodyHtml = """
                        <div style="max-width:600px;margin:0 auto;font-family:-apple-system,BlinkMacSystemFont,'Segoe UI',Roboto,'Helvetica Neue',Arial,'PingFang SC','Microsoft YaHei',sans-serif;">
                          <div style="background:linear-gradient(135deg,#4f46e0 0%,#7c3aed 100%);padding:28px 40px;border-radius:12px 12px 0 0;">
                            <h1 style="margin:0;color:#fff;font-size:20px;font-weight:600;letter-spacing:0.5px;">感谢您的评价 / Thank You for Your Rating</h1>
                          </div>
                          <div style="background:#ffffff;padding:32px 40px;border:1px solid #e5e7eb;border-top:none;">
                            <p style="margin:0 0 16px;font-size:15px;line-height:1.7;color:#1f2937;">您好 {{customer_name}}，</p>
                            <p style="margin:0 0 16px;font-size:15px;line-height:1.7;color:#1f2937;">非常感谢您抽出宝贵时间为我们的服务打分！</p>
                            <div style="background:linear-gradient(135deg,#f0f9ff 0%,#faf5ff 100%);border:1px solid #e0e7ff;border-radius:10px;padding:20px 24px;margin:24px 0;text-align:center;">
                              <p style="margin:0 0 8px;font-size:14px;color:#475569;">您的评价 / Your Rating</p>
                              <p style="margin:0;font-size:24px;font-weight:700;color:#4f46e0;">{{satisfaction_rating}}</p>
                            </div>
                            <p style="margin:0 0 16px;font-size:15px;line-height:1.7;color:#1f2937;">您的反馈是我们持续改进的动力。如果您有任何进一步的建议，欢迎随时回复本邮件告诉我们。</p>
                            <p style="margin:0;font-size:15px;line-height:1.7;color:#1f2937;">期待再次为您服务！</p>
                            <hr style="border:none;border-top:1px solid #e5e7eb;margin:28px 0;" />
                            <p style="margin:0 0 8px;font-size:15px;line-height:1.7;color:#6b7280;">Dear {{customer_name}},</p>
                            <p style="margin:0 0 16px;font-size:15px;line-height:1.7;color:#6b7280;">Thank you very much for taking the time to rate our service!</p>
                            <div style="background:linear-gradient(135deg,#f0f9ff 0%,#faf5ff 100%);border:1px solid #e0e7ff;border-radius:10px;padding:20px 24px;margin:24px 0;text-align:center;">
                              <p style="margin:0 0 8px;font-size:14px;color:#6b7280;">Your Rating</p>
                              <p style="margin:0;font-size:24px;font-weight:700;color:#4f46e0;">{{satisfaction_rating}}</p>
                            </div>
                            <p style="margin:0 0 16px;font-size:15px;line-height:1.7;color:#6b7280;">Your feedback is the driving force behind our continuous improvement. If you have any further suggestions, please feel free to reply to this email.</p>
                            <p style="margin:0;font-size:15px;line-height:1.7;color:#6b7280;">We look forward to serving you again!</p>
                          </div>
                          <div style="background:#f9fafb;padding:20px 40px;border-radius:0 0 12px 12px;text-align:center;">
                            <p style="margin:0;font-size:13px;color:#6b7280;line-height:1.6;">{{company_name}} 客服团队 / Customer Support Team</p>
                            <p style="margin:4px 0 0;font-size:12px;color:#9ca3af;">此邮件为系统自动发送，请勿直接回复 / This is an automated email, please do not reply directly.</p>
                          </div>
                        </div>
                        """,
                        BodyText = "您好 {{customer_name}}，\n\n非常感谢您抽出宝贵时间为我们的服务打分！\n\n您的评价：{{satisfaction_rating}}\n\n您的反馈是我们持续改进的动力。如果您有任何进一步的建议，欢迎随时回复本邮件告诉我们。\n\n期待再次为您服务！\n\n---\nDear {{customer_name}},\n\nThank you very much for taking the time to rate our service!\n\nYour rating: {{satisfaction_rating}}\n\nYour feedback is the driving force behind our continuous improvement. If you have any further suggestions, please feel free to reply to this email.\n\nWe look forward to serving you again!\n\n{{company_name}} 客服团队 / Customer Support Team",
                        IsActive = true,
                        IsSystem = false,
                        Description = "客户提交满意度评价后发送的感谢邮件（中英双语）",
                        SortOrder = 102,
                        CreatedAt = DateTime.UtcNow,
                        UpdatedAt = DateTime.UtcNow
                    },
                    // 新增自定义模板：IM 渠道满意度感谢消息（纯文本，供 send_im_message 动作使用）
                    new MailTemplate
                    {
                        Name = "IM 渠道满意度评价感谢",
                        Slug = "im-satisfaction-thanks",
                        Type = MailTemplateType.Custom,
                        Subject = "感谢您的评价",
                        // IM 消息为纯文本，不使用 HTML；BodyText 为发送内容主体
                        BodyHtml = "",
                        BodyText = "您好 {{customer_name}}，非常感谢您抽出宝贵时间为我们的服务打分！\n\n您的评价：{{satisfaction_rating}}\n\n您的反馈是我们持续改进的动力。如后续有任何建议，欢迎随时联系我们。\n\n期待再次为您服务！\n\n{{company_name}} 客服团队",
                        IsActive = true,
                        IsSystem = false,
                        Description = "IM 渠道（微信/飞书/WhatsApp/钉钉）客户提交满意度评价后，通过对应 App 发送的感谢消息（纯文本）",
                        SortOrder = 103,
                        CreatedAt = DateTime.UtcNow,
                        UpdatedAt = DateTime.UtcNow
                    },
                    // 新增自定义模板：客户回访
                    new MailTemplate
                    {
                        Name = "客户回访邮件",
                        Slug = "customer-followup",
                        Type = MailTemplateType.Custom,
                        Subject = "回访 - 关于您之前咨询的 {{subject}}",
                        BodyHtml = """
                        <p>您好 {{customer_name}}，</p>
                        <p>前段时间您联系我们咨询过 <strong>{{subject}}</strong>，我们想了解问题是否已经得到妥善解决。</p>
                        <p>如果您还有任何疑问或需要进一步的帮助，请随时回复本邮件或访问：</p>
                        <p style="text-align:center;margin:20px 0;">
                          <a href="{{conversation_url}}" style="display:inline-block;padding:10px 20px;background:#007bff;color:#fff;text-decoration:none;border-radius:4px;">查看历史会话 →</a>
                        </p>
                        <p>感谢您选择 {{company_name}}，期待再次为您服务！</p>
                        <p>此致<br/>{{company_name}} 客服团队</p>
                        """,
                        BodyText = "您好 {{customer_name}}，\n\n前段时间您联系我们咨询过 {{subject}}，我们想了解问题是否已经得到妥善解决。\n\n如果您还有任何疑问或需要进一步的帮助，请随时回复本邮件或访问：\n{{conversation_url}}\n\n感谢您选择 {{company_name}}，期待再次为您服务！\n\n此致\n{{company_name}} 客服团队",
                        IsActive = true,
                        IsSystem = false,
                        Description = "客服定期回访客户、确认问题是否解决时使用的邮件模板",
                        SortOrder = 103,
                        CreatedAt = DateTime.UtcNow,
                        UpdatedAt = DateTime.UtcNow
                    },
                    // 新增自定义模板：会话升级通知
                    new MailTemplate
                    {
                        Name = "会话升级通知",
                        Slug = "conversation-escalation",
                        Type = MailTemplateType.Custom,
                        Subject = "[紧急升级] {{subject}}",
                        BodyHtml = """
                        <p>您好 {{user_name}}，</p>
                        <p>有一个会话已被升级为紧急，需要您的关注：</p>
                        <table width="100%" cellpadding="8" cellspacing="0" border="0">
                          <tr>
                            <td width="20%" bgcolor="#f8d7da"><strong>主题</strong></td>
                            <td>{{subject}}</td>
                          </tr>
                          <tr>
                            <td bgcolor="#f8d7da"><strong>客户</strong></td>
                            <td>{{customer_name}} &lt;{{customer_email}}&gt;</td>
                          </tr>
                          <tr>
                            <td bgcolor="#f8d7da"><strong>升级原因</strong></td>
                            <td>{{escalation_reason}}</td>
                          </tr>
                        </table>
                        <p style="text-align:center;margin:20px 0;">
                          <a href="{{conversation_url}}" style="display:inline-block;padding:10px 20px;background:#dc3545;color:#fff;text-decoration:none;border-radius:4px;">立即查看 →</a>
                        </p>
                        <p>此致<br/>{{company_name}} 系统通知</p>
                        """,
                        BodyText = "您好 {{user_name}}，\n\n有一个会话已被升级为紧急，需要您的关注：\n\n主题：{{subject}}\n客户：{{customer_name}} <{{customer_email}}>\n升级原因：{{escalation_reason}}\n\n立即查看：{{conversation_url}}\n\n此致\n{{company_name}} 系统通知",
                        IsActive = true,
                        IsSystem = false,
                        Description = "会话被升级为紧急级别时发送给客服的通知邮件",
                        SortOrder = 104,
                        CreatedAt = DateTime.UtcNow,
                        UpdatedAt = DateTime.UtcNow
                    },
                    new MailTemplate
                    {
                        Name = "日历提醒",
                        Slug = "calendar-reminder",
                        Type = MailTemplateType.CalendarReminder,
                        Subject = "【{{event_type}}提醒】{{event_title}} - {{event_start_time}}",
                        BodyHtml = """
<!DOCTYPE html>
<html><body style="margin:0;padding:0;background:#f9fafb;font-family:-apple-system,BlinkMacSystemFont,'Segoe UI',Roboto,sans-serif;">
<div style="max-width:600px;margin:0 auto;padding:24px;">
  <div style="background:#fff;border-radius:12px;overflow:hidden;box-shadow:0 1px 3px rgba(0,0,0,0.08);">
    <div style="background:linear-gradient(135deg,#3b82f6,#8b5cf6);padding:24px 28px;">
      <div style="color:#fff;font-size:12px;text-transform:uppercase;letter-spacing:1px;margin-bottom:4px;opacity:0.85;">{{event_type}}提醒</div>
      <div style="color:#fff;font-size:20px;font-weight:700;">{{event_title}}</div>
    </div>
    <div style="padding:20px 28px;">
      <table style="width:100%;border-collapse:collapse;font-size:14px;">
        <tr><td style="padding:8px 0;color:#6b7280;width:80px;">开始时间</td><td style="padding:8px 0;color:#111827;font-weight:500;">{{event_start_time}}</td></tr>
        <tr><td style="padding:8px 0;color:#6b7280;">结束时间</td><td style="padding:8px 0;color:#111827;">{{event_end_time}}</td></tr>
        <tr><td style="padding:8px 0;color:#6b7280;">优先级</td><td style="padding:8px 0;color:#111827;">{{event_priority}}</td></tr>
        <tr><td style="padding:8px 0;color:#6b7280;">地点</td><td style="padding:8px 0;color:#111827;">{{event_location}}</td></tr>
        <tr><td style="padding:8px 0;color:#6b7280;">会议链接</td><td style="padding:8px 0;"><a href="{{event_meeting_url}}" style="color:#3b82f6;text-decoration:none;">{{event_meeting_url}}</a></td></tr>
      </table>
      <div style="margin-top:16px;padding-top:16px;border-top:1px solid #f3f4f6;">
        <div style="color:#6b7280;font-size:13px;margin-bottom:6px;">详情</div>
        <div style="color:#374151;font-size:14px;line-height:1.6;white-space:pre-wrap;">{{event_description}}</div>
      </div>
    </div>
    <div style="background:#f9fafb;padding:16px 28px;text-align:center;">
      <a href="{{event_url}}" style="display:inline-block;padding:10px 28px;background:#3b82f6;color:#fff;text-decoration:none;border-radius:8px;font-size:14px;font-weight:500;">查看详情</a>
    </div>
  </div>
  <div style="text-align:center;padding:16px 0;color:#9ca3af;font-size:12px;">此邮件由系统自动发送，请勿直接回复</div>
</div>
</body></html>
""",
                        BodyText = """{{event_type}}提醒：{{event_title}}\n\n开始时间：{{event_start_time}}\n结束时间：{{event_end_time}}\n优先级：{{event_priority}}\n地点：{{event_location}}\n会议链接：{{event_meeting_url}}\n\n详情：{{event_description}}\n\n查看详情：{{event_url}}\n\n此邮件由系统自动发送，请勿直接回复""",
                        IsActive = true,
                        IsSystem = true,
                        Description = "日历事件提醒邮件，发送给创建者、责任人和参会人",
                        SortOrder = 105,
                        CreatedAt = DateTime.UtcNow,
                        UpdatedAt = DateTime.UtcNow
                    }
                };

                var toAdd = mailTemplates.Where(t => !existingSlugs.Contains(t.Slug)).ToList();
                if (toAdd.Count > 0)
                {
                    context.MailTemplates.AddRange(toAdd);
                }
            }

            // 团队种子数据
            if (!context.Teams.Any())
            {
                // 优先使用已解析的 adminUser（ADMIN_EMAIL），兼容自定义管理员邮箱
                var teamAdminUser = adminUser ?? context.Users.FirstOrDefault(u => u.Email == "admin@example.com");
                var supportMailbox = context.Mailboxes.First(m => m.Email == "support@example.com");

                var teams = new[]
                {
                    new Team
                    {
                        Name = "技术支持组",
                        Description = "处理技术相关的客户问题，包括产品故障、功能异常等",
                        MailboxId = null,
                        IsActive = true,
                        SortOrder = 1,
                        CreatedAt = DateTime.UtcNow,
                        UpdatedAt = DateTime.UtcNow,
                        Members = new List<TeamMember>
                        {
                            new()
                            {
                                User = teamAdminUser,
                                Role = TeamMemberRole.Leader,
                                JoinedAt = DateTime.UtcNow
                            }
                        }
                    },
                    new Team
                    {
                        Name = "售前咨询组",
                        Description = "处理产品咨询、价格、功能介绍等售前问题",
                        MailboxId = null,
                        IsActive = true,
                        SortOrder = 2,
                        CreatedAt = DateTime.UtcNow,
                        UpdatedAt = DateTime.UtcNow,
                        Members = new List<TeamMember>()
                    },
                    new Team
                    {
                        Name = "售后客服组",
                        Description = "处理订单、退款、投诉等售后问题",
                        MailboxId = supportMailbox.Id,
                        IsActive = true,
                        SortOrder = 3,
                        CreatedAt = DateTime.UtcNow,
                        UpdatedAt = DateTime.UtcNow,
                        Members = new List<TeamMember>()
                    }
                };
                context.Teams.AddRange(teams);
            }

            // 会话标签种子数据
            if (!context.Tags.Any())
            {
                var tags = new[]
                {
                    // 优先级
                    new Tag { Name = "紧急", Color = "#ef4444", CreatedAt = DateTime.UtcNow, UpdatedAt = DateTime.UtcNow },
                    new Tag { Name = "高优先级", Color = "#f97316", CreatedAt = DateTime.UtcNow, UpdatedAt = DateTime.UtcNow },
                    new Tag { Name = "普通", Color = "#64748b", CreatedAt = DateTime.UtcNow, UpdatedAt = DateTime.UtcNow },
                    new Tag { Name = "低优先级", Color = "#94a3b8", CreatedAt = DateTime.UtcNow, UpdatedAt = DateTime.UtcNow },

                    // 处理状态
                    new Tag { Name = "待处理", Color = "#eab308", CreatedAt = DateTime.UtcNow, UpdatedAt = DateTime.UtcNow },
                    new Tag { Name = "处理中", Color = "#3b82f6", CreatedAt = DateTime.UtcNow, UpdatedAt = DateTime.UtcNow },
                    new Tag { Name = "待回复", Color = "#f59e0b", CreatedAt = DateTime.UtcNow, UpdatedAt = DateTime.UtcNow },
                    new Tag { Name = "已解决", Color = "#22c55e", CreatedAt = DateTime.UtcNow, UpdatedAt = DateTime.UtcNow },
                    new Tag { Name = "待确认", Color = "#a855f7", CreatedAt = DateTime.UtcNow, UpdatedAt = DateTime.UtcNow },
                    new Tag { Name = "待跟进", Color = "#8b5cf6", CreatedAt = DateTime.UtcNow, UpdatedAt = DateTime.UtcNow },

                    // 客户类型
                    new Tag { Name = "VIP客户", Color = "#ec4899", CreatedAt = DateTime.UtcNow, UpdatedAt = DateTime.UtcNow },
                    new Tag { Name = "潜在客户", Color = "#06b6d4", CreatedAt = DateTime.UtcNow, UpdatedAt = DateTime.UtcNow },
                    new Tag { Name = "内部问题", Color = "#6366f1", CreatedAt = DateTime.UtcNow, UpdatedAt = DateTime.UtcNow },
                    new Tag { Name = "合作伙伴", Color = "#14b8a6", CreatedAt = DateTime.UtcNow, UpdatedAt = DateTime.UtcNow },

                    // 问题类型
                    new Tag { Name = "技术问题", Color = "#0ea5e9", CreatedAt = DateTime.UtcNow, UpdatedAt = DateTime.UtcNow },
                    new Tag { Name = "售后问题", Color = "#d946ef", CreatedAt = DateTime.UtcNow, UpdatedAt = DateTime.UtcNow },
                    new Tag { Name = "售前咨询", Color = "#10b981", CreatedAt = DateTime.UtcNow, UpdatedAt = DateTime.UtcNow },
                    new Tag { Name = "投诉", Color = "#dc2626", CreatedAt = DateTime.UtcNow, UpdatedAt = DateTime.UtcNow },
                    new Tag { Name = "功能建议", Color = "#84cc16", CreatedAt = DateTime.UtcNow, UpdatedAt = DateTime.UtcNow },
                    new Tag { Name = "Bug反馈", Color = "#f43f5e", CreatedAt = DateTime.UtcNow, UpdatedAt = DateTime.UtcNow },
                };
                context.Tags.AddRange(tags);
            }

            // 邮件签名种子数据
            if (!context.EmailSignatures.Any())
            {
                var sigAdminUser = adminUser ?? context.Users.FirstOrDefault(u => u.Email == "admin@example.com");
                var sigMailbox = context.Mailboxes.FirstOrDefault(m => m.Email == "support@example.com");

                var signatures = new[]
                {
                    new EmailSignature
                    {
                        User = sigAdminUser,
                        Mailbox = null,
                        Name = "默认签名",
                        Body = """
                        <p>此致<br/>
                        <strong>{{user_name}}</strong><br/>
                        {{company_name}} 客服团队</p>
                        """,
                        IsDefault = true,
                        SortOrder = 1,
                        CreatedAt = DateTime.UtcNow,
                        UpdatedAt = DateTime.UtcNow
                    },
                    new EmailSignature
                    {
                        User = sigAdminUser,
                        Mailbox = sigMailbox,
                        Name = "技术支持签名",
                        Body = """
                        <p>祝好！<br/>
                        <strong>{{user_name}}</strong> - 技术支持工程师<br/>
                        {{mailbox_name}}<br/>
                        <em>{{company_name}}</em></p>
                        """,
                        IsDefault = false,
                        SortOrder = 2,
                        CreatedAt = DateTime.UtcNow,
                        UpdatedAt = DateTime.UtcNow
                    },
                    new EmailSignature
                    {
                        User = sigAdminUser,
                        Mailbox = null,
                        Name = "简洁签名",
                        Body = """
                        <p>{{user_name}} | {{company_name}}</p>
                        """,
                        IsDefault = false,
                        SortOrder = 3,
                        CreatedAt = DateTime.UtcNow,
                        UpdatedAt = DateTime.UtcNow
                    }
                };
                context.EmailSignatures.AddRange(signatures);
            }

            if (!context.Modules.Any())
            {
                var modules = new[]
                {
                    new Module
                    {
                        Name = "core",
                        Title = "核心功能",
                        Description = "系统核心功能模块，包括会话管理、客户管理、邮箱管理、用户权限等基础功能。此模块不可禁用。",
                        Version = "1.0.0",
                        Author = "nCSS Team",
                        Icon = "box",
                        IsActive = true,
                        Category = "core",
                        SortOrder = 0,
                        CreatedAt = DateTime.UtcNow,
                        UpdatedAt = DateTime.UtcNow
                    },
                    new Module
                    {
                        Name = "calendar",
                        Title = "日历",
                        Description = "在日历视图中查看和管理所有工单的截止日期与计划任务。支持按用户、邮箱、状态筛选。",
                        Version = "1.0.0",
                        Author = "nCSS Team",
                        Icon = "calendar",
                        IsActive = false,
                        Category = "tools",
                        SortOrder = 1,
                        CreatedAt = DateTime.UtcNow,
                        UpdatedAt = DateTime.UtcNow
                    },
                    new Module
                    {
                        Name = "telegram",
                        Title = "Telegram 集成",
                        Description = "通过 Telegram 机器人接收和回复客户消息，实现多渠道统一管理。",
                        Version = "1.2.0",
                        Author = "nCSS Team",
                        Icon = "telegram",
                        // 该模块行是历史遗留的空上架（此前仓库内没有任何 Telegram 实现）。
                        // 实现落地并验证后由管理员在「模块管理」里启用，默认关闭与其他渠道一致。
                        IsActive = false,
                        Category = "integration",
                        SortOrder = 2,
                        CreatedAt = DateTime.UtcNow,
                        UpdatedAt = DateTime.UtcNow
                    },
                    new Module
                    {
                        Name = "whatsapp",
                        Title = "WhatsApp 集成",
                        Description = "通过 WhatsApp Business API 收发客户消息，支持图片、文档等附件。",
                        Version = "1.1.0",
                        Author = "nCSS Team",
                        Icon = "whatsapp",
                        IsActive = false,
                        Category = "integration",
                        SortOrder = 3,
                        CreatedAt = DateTime.UtcNow,
                        UpdatedAt = DateTime.UtcNow
                    },
                    new Module
                    {
                        Name = "slack",
                        Title = "Slack 集成",
                        Description = "将工单通知推送到 Slack 频道，支持直接在 Slack 中回复。",
                        Version = "1.0.0",
                        Author = "nCSS Team",
                        Icon = "slack",
                        IsActive = false,
                        Category = "integration",
                        SortOrder = 4,
                        CreatedAt = DateTime.UtcNow,
                        UpdatedAt = DateTime.UtcNow
                    },
                    new Module
                    {
                        Name = "custom-reports",
                        Title = "自定义报表",
                        Description = "创建自定义报表，深入分析客服团队绩效、工单处理效率等关键指标。",
                        Version = "2.0.0",
                        Author = "nCSS Team",
                        Icon = "chart",
                        IsActive = true,
                        Category = "reports",
                        SortOrder = 5,
                        CreatedAt = DateTime.UtcNow,
                        UpdatedAt = DateTime.UtcNow
                    },
                    new Module
                    {
                        Name = "time-tracking",
                        Title = "时间追踪",
                        Description = "记录处理每个工单所花费的时间，支持工时统计与报表导出。",
                        Version = "1.3.0",
                        Author = "nCSS Team",
                        Icon = "clock",
                        IsActive = true,
                        Category = "tools",
                        SortOrder = 6,
                        CreatedAt = DateTime.UtcNow,
                        UpdatedAt = DateTime.UtcNow
                    },
                    new Module
                    {
                        Name = "chat",
                        Title = "在线聊天",
                        Description = "在网站上添加实时聊天小部件，访客可直接发起对话并自动创建工单。",
                        Version = "1.5.0",
                        Author = "nCSS Team",
                        Icon = "chat",
                        IsActive = false,
                        Category = "communication",
                        SortOrder = 7,
                        CreatedAt = DateTime.UtcNow,
                        UpdatedAt = DateTime.UtcNow
                    },
                    new Module
                    {
                        Name = "knowledgebase",
                        Title = "知识库",
                        Description = "创建和管理帮助文档，客户可自助查找答案，减少重复工单。",
                        Version = "1.4.0",
                        Author = "nCSS Team",
                        Icon = "book",
                        IsActive = false,
                        Category = "general",
                        SortOrder = 8,
                        CreatedAt = DateTime.UtcNow,
                        UpdatedAt = DateTime.UtcNow
                    },
                    new Module
                    {
                        Name = "automation-rules",
                        Title = "自动化规则",
                        Description = "设置基于条件的自动化规则，自动分配、标记、回复工单。",
                        Version = "1.0.0",
                        Author = "nCSS Team",
                        Icon = "zap",
                        IsActive = true,
                        Category = "automation",
                        SortOrder = 9,
                        CreatedAt = DateTime.UtcNow,
                        UpdatedAt = DateTime.UtcNow
                    },
                    new Module
                    {
                        Name = "mailbox",
                        Title = "邮件模块",
                        Description = "邮件收发、SMTP/IMAP 配置、邮件模板管理。",
                        Version = "1.0.0",
                        Author = "nCSS Team",
                        Icon = "mail",
                        IsActive = true,
                        Category = "core",
                        SortOrder = 10,
                        CreatedAt = DateTime.UtcNow,
                        UpdatedAt = DateTime.UtcNow
                    },
                    new Module
                    {
                        Name = "livechat",
                        Title = "在线聊天模块",
                        Description = "网站在线聊天小部件、访客实时对话。",
                        Version = "1.0.0",
                        Author = "nCSS Team",
                        Icon = "chat",
                        IsActive = false,
                        Category = "communication",
                        SortOrder = 11,
                        CreatedAt = DateTime.UtcNow,
                        UpdatedAt = DateTime.UtcNow
                    },
                    new Module
                    {
                        Name = "wechat",
                        Title = "WeChat 集成",
                        Description = "通过微信公众号/小程序接收和回复客户消息，实现多渠道统一管理。",
                        Version = "1.0.0",
                        Author = "nCSS Team",
                        Icon = "wechat",
                        IsActive = false,
                        Category = "integration",
                        SortOrder = 12,
                        CreatedAt = DateTime.UtcNow,
                        UpdatedAt = DateTime.UtcNow
                    },
                    new Module
                    {
                        Name = "feishu",
                        Title = "Feishu 集成",
                        Description = "通过飞书机器人应用接收和回复客户消息，支持文本、图片、文件等消息类型。",
                        Version = "1.0.0",
                        Author = "nCSS Team",
                        Icon = "feishu",
                        IsActive = false,
                        Category = "integration",
                        SortOrder = 13,
                        CreatedAt = DateTime.UtcNow,
                        UpdatedAt = DateTime.UtcNow
                    },
                    new Module
                    {
                        Name = "dingding",
                        Title = "DingTalk 集成",
                        Description = "通过钉钉开放平台机器人收发客户消息，支持文本、图片、文件等附件。",
                        Version = "1.0.0",
                        Author = "nCSS Team",
                        Icon = "dingding",
                        IsActive = false,
                        Category = "integration",
                        SortOrder = 14,
                        CreatedAt = DateTime.UtcNow,
                        UpdatedAt = DateTime.UtcNow
                    }
                };
                context.Modules.AddRange(modules);
            }

            // 模块种子数据 - 增量添加，确保每次启动都检查并补全模块
            // 注意：原 Modules 块内的种子代码只在全新数据库时执行，
            // 此处独立增量逻辑保证已有数据库也能按 Name 补充缺失的模块
            {
                var incrementalModules = new[]
                {
                    new Module
                    {
                        Name = "wechat",
                        Title = "WeChat 集成",
                        Description = "通过微信公众号/小程序接收和回复客户消息，实现多渠道统一管理。",
                        Version = "1.0.0",
                        Author = "nCSS Team",
                        Icon = "wechat",
                        IsActive = false,
                        Category = "integration",
                        SortOrder = 12,
                        CreatedAt = DateTime.UtcNow,
                        UpdatedAt = DateTime.UtcNow
                    },
                    new Module
                    {
                        Name = "custom-reports",
                        Title = "自定义报表",
                        Description = "创建自定义报表，深入分析客服团队绩效、工单处理效率等关键指标。",
                        Version = "2.0.0",
                        Author = "nCSS Team",
                        Icon = "chart",
                        IsActive = true,
                        Category = "reports",
                        SortOrder = 5,
                        CreatedAt = DateTime.UtcNow,
                        UpdatedAt = DateTime.UtcNow
                    },
                    new Module
                    {
                        Name = "feishu",
                        Title = "Feishu 集成",
                        Description = "通过飞书机器人应用接收和回复客户消息，支持文本、图片、文件等消息类型。",
                        Version = "1.0.0",
                        Author = "nCSS Team",
                        Icon = "feishu",
                        IsActive = false,
                        Category = "integration",
                        SortOrder = 13,
                        CreatedAt = DateTime.UtcNow,
                        UpdatedAt = DateTime.UtcNow
                    },
                    new Module
                    {
                        Name = "dingding",
                        Title = "DingTalk 集成",
                        Description = "通过钉钉开放平台机器人收发客户消息，支持文本、图片、文件等附件。",
                        Version = "1.0.0",
                        Author = "nCSS Team",
                        Icon = "dingding",
                        IsActive = false,
                        Category = "integration",
                        SortOrder = 14,
                        CreatedAt = DateTime.UtcNow,
                        UpdatedAt = DateTime.UtcNow
                    },
                    // ↓ 四个新 IM 渠道模块：增量数组每次启动都会按 Name 补全，
                    //   新建库与既有库都能拿到；模块默认关闭，由管理员在「模块管理」里启用。
                    new Module
                    {
                        Name = "messenger",
                        Title = "Messenger 集成",
                        Description = "通过 Facebook Messenger（Meta Graph API）收发客户消息，支持 24 小时回复窗口、附件与账户绑定。",
                        Version = "1.0.0",
                        Author = "nCSS Team",
                        Icon = "messenger",
                        IsActive = false,
                        Category = "integration",
                        SortOrder = 15,
                        CreatedAt = DateTime.UtcNow,
                        UpdatedAt = DateTime.UtcNow
                    },
                    new Module
                    {
                        Name = "telegram",
                        Title = "Telegram 集成",
                        Description = "通过 Telegram Bot API 收发客户消息，支持 Webhook 与自建 Bot API 服务。",
                        Version = "1.2.0",
                        Author = "nCSS Team",
                        Icon = "telegram",
                        IsActive = false,
                        Category = "integration",
                        SortOrder = 2,
                        CreatedAt = DateTime.UtcNow,
                        UpdatedAt = DateTime.UtcNow
                    },
                    new Module
                    {
                        Name = "line",
                        Title = "LINE 集成",
                        Description = "通过 LINE Messaging API 收发客户消息，支持 replyToken 回复、推送配额与附件直链。",
                        Version = "1.0.0",
                        Author = "nCSS Team",
                        Icon = "line",
                        IsActive = false,
                        Category = "integration",
                        SortOrder = 17,
                        CreatedAt = DateTime.UtcNow,
                        UpdatedAt = DateTime.UtcNow
                    },
                    new Module
                    {
                        Name = "imessage",
                        Title = "iMessage 集成",
                        Description = "通过 macOS 桥接服务（BlueBubbles）或 Apple Messages for Business 收发客户消息。",
                        Version = "1.0.0",
                        Author = "nCSS Team",
                        Icon = "imessage",
                        IsActive = false,
                        Category = "integration",
                        SortOrder = 18,
                        CreatedAt = DateTime.UtcNow,
                        UpdatedAt = DateTime.UtcNow
                    }
                };

                var modulesToAdd = incrementalModules
                    .Where(m => !context.Modules.Any(x => x.Name == m.Name))
                    .ToList();
                if (modulesToAdd.Count > 0)
                {
                    context.Modules.AddRange(modulesToAdd);
                    await context.SaveChangesAsync();
                }
            }

            if (!context.AuditLogs.Any())
            {
                var auditAdmin = adminUser ?? context.Users.FirstOrDefault(u => u.Email == "admin@example.com");
                if (auditAdmin != null)
                {
                var auditLogs = new[]
                {
                    new AuditLog
                    {
                        UserId = auditAdmin.Id,
                        Action = "login",
                        EntityType = "User",
                        EntityId = auditAdmin.Id,
                        OldValues = "",
                        NewValues = "",
                        IpAddress = "192.168.1.100",
                        UserAgent = "Mozilla/5.0 (Windows NT 10.0; Win64; x64) Chrome/120.0.0.0",
                        CreatedAt = DateTime.UtcNow.AddHours(-2)
                    },
                    new AuditLog
                    {
                        UserId = auditAdmin.Id,
                        Action = "create",
                        EntityType = "Conversation",
                        EntityId = 1,
                        OldValues = "",
                        NewValues = "{\"subject\":\"测试工单 - 系统登录问题\",\"status\":1}",
                        IpAddress = "192.168.1.100",
                        UserAgent = "Mozilla/5.0 (Windows NT 10.0; Win64; x64) Chrome/120.0.0.0",
                        CreatedAt = DateTime.UtcNow.AddHours(-3)
                    },
                    new AuditLog
                    {
                        UserId = auditAdmin.Id,
                        Action = "update",
                        EntityType = "Conversation",
                        EntityId = 1,
                        OldValues = "{\"status\":1,\"userId\":null}",
                        NewValues = "{\"status\":2,\"userId\":1}",
                        IpAddress = "192.168.1.100",
                        UserAgent = "Mozilla/5.0 (Windows NT 10.0; Win64; x64) Chrome/120.0.0.0",
                        CreatedAt = DateTime.UtcNow.AddHours(-3).AddMinutes(30)
                    },
                    new AuditLog
                    {
                        UserId = auditAdmin.Id,
                        Action = "reply",
                        EntityType = "Conversation",
                        EntityId = 1,
                        OldValues = "",
                        NewValues = "{\"threadType\":\"message\",\"body\":\"您好，请问您的账号是什么？\"}",
                        IpAddress = "192.168.1.100",
                        UserAgent = "Mozilla/5.0 (Windows NT 10.0; Win64; x64) Chrome/120.0.0.0",
                        CreatedAt = DateTime.UtcNow.AddHours(-3).AddMinutes(45)
                    },
                    new AuditLog
                    {
                        UserId = auditAdmin.Id,
                        Action = "create",
                        EntityType = "Customer",
                        EntityId = 1,
                        OldValues = "",
                        NewValues = "{\"firstName\":\"测试\",\"lastName\":\"客户\",\"email\":\"customer@example.com\"}",
                        IpAddress = "192.168.1.100",
                        UserAgent = "Mozilla/5.0 (Windows NT 10.0; Win64; x64) Chrome/120.0.0.0",
                        CreatedAt = DateTime.UtcNow.AddHours(-4)
                    },
                    new AuditLog
                    {
                        UserId = auditAdmin.Id,
                        Action = "create",
                        EntityType = "Mailbox",
                        EntityId = 1,
                        OldValues = "",
                        NewValues = "{\"name\":\"支持邮箱\",\"email\":\"support@example.com\",\"isActive\":true}",
                        IpAddress = "192.168.1.100",
                        UserAgent = "Mozilla/5.0 (Windows NT 10.0; Win64; x64) Chrome/120.0.0.0",
                        CreatedAt = DateTime.UtcNow.AddHours(-5)
                    },
                    new AuditLog
                    {
                        UserId = auditAdmin.Id,
                        Action = "assign",
                        EntityType = "Conversation",
                        EntityId = 1,
                        OldValues = "{\"userId\":null}",
                        NewValues = "{\"userId\":1}",
                        IpAddress = "192.168.1.100",
                        UserAgent = "Mozilla/5.0 (Windows NT 10.0; Win64; x64) Chrome/120.0.0.0",
                        CreatedAt = DateTime.UtcNow.AddHours(-6)
                    },
                    new AuditLog
                    {
                        UserId = auditAdmin.Id,
                        Action = "update",
                        EntityType = "Tag",
                        EntityId = 1,
                        OldValues = "{\"name\":\"待处理\",\"color\":\"#f59e0b\"}",
                        NewValues = "{\"name\":\"待跟进\",\"color\":\"#3b82f6\"}",
                        IpAddress = "192.168.1.100",
                        UserAgent = "Mozilla/5.0 (Windows NT 10.0; Win64; x64) Chrome/120.0.0.0",
                        CreatedAt = DateTime.UtcNow.AddHours(-8)
                    },
                    new AuditLog
                    {
                        UserId = auditAdmin.Id,
                        Action = "status_change",
                        EntityType = "Conversation",
                        EntityId = 1,
                        OldValues = "{\"status\":2}",
                        NewValues = "{\"status\":1}",
                        IpAddress = "192.168.1.100",
                        UserAgent = "Mozilla/5.0 (Windows NT 10.0; Win64; x64) Chrome/120.0.0.0",
                        CreatedAt = DateTime.UtcNow.AddHours(-10)
                    },
                    new AuditLog
                    {
                        UserId = auditAdmin.Id,
                        Action = "note",
                        EntityType = "Conversation",
                        EntityId = 1,
                        OldValues = "",
                        NewValues = "{\"body\":\"客户反馈密码重置问题，需要协助处理\"}",
                        IpAddress = "192.168.1.100",
                        UserAgent = "Mozilla/5.0 (Windows NT 10.0; Win64; x64) Chrome/120.0.0.0",
                        CreatedAt = DateTime.UtcNow.AddHours(-12)
                    },
                    new AuditLog
                    {
                        UserId = auditAdmin.Id,
                        Action = "logout",
                        EntityType = "User",
                        EntityId = auditAdmin.Id,
                        OldValues = "",
                        NewValues = "",
                        IpAddress = "192.168.1.100",
                        UserAgent = "Mozilla/5.0 (Windows NT 10.0; Win64; x64) Chrome/120.0.0.0",
                        CreatedAt = DateTime.UtcNow.AddHours(-15)
                    },
                    new AuditLog
                    {
                        UserId = auditAdmin.Id,
                        Action = "create",
                        EntityType = "ApiKey",
                        EntityId = 1,
                        OldValues = "",
                        NewValues = "{\"name\":\"默认集成密钥\",\"scopes\":\"conversations:read,conversations:write\"}",
                        IpAddress = "192.168.1.100",
                        UserAgent = "Mozilla/5.0 (Windows NT 10.0; Win64; x64) Chrome/120.0.0.0",
                        CreatedAt = DateTime.UtcNow.AddDays(-30)
                    }
                };
                context.AuditLogs.AddRange(auditLogs);
                }
            }

            // 仅当 ApiKeys 表已存在 id=1/2 的密钥时才补充日志种子数据；
            // 全新安装时 ApiKeys 尚未创建，跳过以避免外键约束失败。
            if (!context.ApiKeyLogs.Any() && context.ApiKeys.Any(k => k.Id == 1 || k.Id == 2))
            {
                var apiKeyLogs = new[]
                {
                    new ApiKeyLog
                    {
                        ApiKeyId = 1,
                        Endpoint = "/api/conversations",
                        Method = "GET",
                        StatusCode = 200,
                        IpAddress = "192.168.1.100",
                        UserAgent = "curl/7.85.0",
                        DurationMs = 125,
                        CreatedAt = DateTime.UtcNow.AddHours(-1)
                    },
                    new ApiKeyLog
                    {
                        ApiKeyId = 1,
                        Endpoint = "/api/customers",
                        Method = "GET",
                        StatusCode = 200,
                        IpAddress = "192.168.1.100",
                        UserAgent = "curl/7.85.0",
                        DurationMs = 89,
                        CreatedAt = DateTime.UtcNow.AddHours(-1).AddMinutes(15)
                    },
                    new ApiKeyLog
                    {
                        ApiKeyId = 2,
                        Endpoint = "/api/conversations/1",
                        Method = "GET",
                        StatusCode = 200,
                        IpAddress = "192.0.2.50",
                        UserAgent = "python-requests/2.31.0",
                        DurationMs = 56,
                        CreatedAt = DateTime.UtcNow.AddMinutes(-30)
                    },
                    new ApiKeyLog
                    {
                        ApiKeyId = 2,
                        Endpoint = "/api/customers",
                        Method = "GET",
                        StatusCode = 200,
                        IpAddress = "192.0.2.50",
                        UserAgent = "python-requests/2.31.0",
                        DurationMs = 44,
                        CreatedAt = DateTime.UtcNow.AddMinutes(-25)
                    },
                    new ApiKeyLog
                    {
                        ApiKeyId = 1,
                        Endpoint = "/api/conversations",
                        Method = "POST",
                        StatusCode = 401,
                        IpAddress = "192.168.1.101",
                        UserAgent = "Unknown/1.0",
                        DurationMs = 23,
                        ErrorMessage = "Invalid API key",
                        CreatedAt = DateTime.UtcNow.AddHours(-2)
                    }
                };
                context.ApiKeyLogs.AddRange(apiKeyLogs);
            }

        // 系统配置种子数据（已拆分到 SystemOptionSeeder）
        await Seeders.SystemOptionSeeder.SeedAsync(context);

        if (isNewDatabase)
        {
            context.Database.ExecuteSqlRaw("PRAGMA foreign_keys = ON;");
        }

        // SLA 策略种子数据 - 增量添加，确保每次启动都检查并补全策略
        // 注意：原 Users 块内的 SLA 种子代码只在全新数据库时执行，
        // 此处独立增量逻辑保证已有数据库也能按名称补充缺失的策略
        {
            var extraSlaPolicies = new[]
            {
                new SlaPolicy
                {
                    Name = "企业 VIP 客户时效",
                    Description = "面向企业 VIP 客户的严格时效：30 分钟内首次响应，4 小时内解决。优先级最高，适用于 VIP 邮箱或人工升级场景。",
                    MailboxId = null,
                    FirstResponseHours = 0.5,
                    ResolutionHours = 4.0,
                    BusinessHoursOnly = false,
                    Priority = 30,
                    IsActive = false,
                    SortOrder = 10,
                    CreatedAt = DateTime.UtcNow.AddDays(-2),
                    UpdatedAt = DateTime.UtcNow.AddDays(-2)
                },
                new SlaPolicy
                {
                    Name = "技术故障报告时效",
                    Description = "技术故障/系统异常报告的紧急时效：15 分钟内首次响应，2 小时内解决。适用于技术故障类工单监控。",
                    MailboxId = null,
                    FirstResponseHours = 0.25,
                    ResolutionHours = 2.0,
                    BusinessHoursOnly = false,
                    Priority = 25,
                    IsActive = false,
                    SortOrder = 11,
                    CreatedAt = DateTime.UtcNow.AddDays(-2),
                    UpdatedAt = DateTime.UtcNow.AddDays(-2)
                },
                new SlaPolicy
                {
                    Name = "账单/退款咨询时效",
                    Description = "账单、退款、支付类咨询的时效：2 小时内首次响应，24 小时内解决。适用于财务相关邮箱。",
                    MailboxId = null,
                    FirstResponseHours = 2.0,
                    ResolutionHours = 24.0,
                    BusinessHoursOnly = false,
                    Priority = 15,
                    IsActive = false,
                    SortOrder = 12,
                    CreatedAt = DateTime.UtcNow.AddDays(-2),
                    UpdatedAt = DateTime.UtcNow.AddDays(-2)
                },
                new SlaPolicy
                {
                    Name = "普通工单时效",
                    Description = "普通工单的中等时效：4 小时内首次响应，48 小时内解决。介于标准响应时效与紧急工单时效之间。",
                    MailboxId = null,
                    FirstResponseHours = 4.0,
                    ResolutionHours = 48.0,
                    BusinessHoursOnly = false,
                    Priority = 5,
                    IsActive = false,
                    SortOrder = 13,
                    CreatedAt = DateTime.UtcNow.AddDays(-2),
                    UpdatedAt = DateTime.UtcNow.AddDays(-2)
                },
                new SlaPolicy
                {
                    Name = "非工作时段宽松时效",
                    Description = "夜间/周末等非工作时段收到的非紧急咨询：8 小时内首次响应，5 天内解决。启用 BusinessHoursOnly 后，仅统计工作日 9:00-18:00 内的耗时。",
                    MailboxId = null,
                    FirstResponseHours = 8.0,
                    ResolutionHours = 120.0,
                    BusinessHoursOnly = false,
                    Priority = -5,
                    IsActive = false,
                    SortOrder = 14,
                    CreatedAt = DateTime.UtcNow.AddDays(-2),
                    UpdatedAt = DateTime.UtcNow.AddDays(-2)
                }
            };

            var slaToAdd = extraSlaPolicies
                .Where(p => !context.SlaPolicies.Any(x => x.Name == p.Name))
                .ToList();
            if (slaToAdd.Count > 0)
            {
                context.SlaPolicies.AddRange(slaToAdd);
                await context.SaveChangesAsync();
            }
        }

        // 邮件模板种子数据 - 增量添加，确保每次启动都检查并补全新模板
        // 注意：原 Users 块内的种子代码只在全新数据库时执行，
        // 此处独立增量逻辑保证已有数据库也能按 slug 补充缺失的模板
        {
            // 注意：用 IgnoreQueryFilters 包含软删除记录，
            // 否则当 DB 旧 UNIQUE 索引未带 DeletedAt IS NULL 过滤器时，
            // 软删除的同 Slug 记录会触发 UNIQUE constraint failed
            var mailExistingSlugs = new HashSet<string>(
                context.MailTemplates.IgnoreQueryFilters().Select(t => t.Slug),
                StringComparer.OrdinalIgnoreCase);

            var mailTemplatesToAdd = new[]
            {
                new MailTemplate
                {
                    Name = "新会话通知",
                    Slug = "new-conversation",
                    Type = MailTemplateType.NewConversation,
                    Subject = "[新会话] {{subject}}",
                    BodyHtml = """
                    <p>您好 {{user_name}}，</p>
                    <p>收到了一封新的客户会话：</p>
                    <table width="100%" cellpadding="8" cellspacing="0" border="0">
                      <tr>
                        <td width="20%" bgcolor="#f5f5f5"><strong>主题</strong></td>
                        <td>{{subject}}</td>
                      </tr>
                      <tr>
                        <td bgcolor="#f5f5f5"><strong>客户</strong></td>
                        <td>{{customer_name}} &lt;{{customer_email}}&gt;</td>
                      </tr>
                      <tr>
                        <td bgcolor="#f5f5f5"><strong>邮箱</strong></td>
                        <td>{{mailbox_name}}</td>
                      </tr>
                    </table>
                    <p><a href="{{conversation_url}}">查看会话详情 →</a></p>
                    <p>此致<br/>{{company_name}} 客服团队</p>
                    """,
                    BodyText = "您好 {{user_name}}，\n\n收到了一封新的客户会话：\n\n主题：{{subject}}\n客户：{{customer_name}} <{{customer_email}}>\n邮箱：{{mailbox_name}}\n\n查看详情：{{conversation_url}}\n\n此致\n{{company_name}} 客服团队",
                    IsActive = true,
                    IsSystem = true,
                    Description = "当有新会话创建时发送给客服的通知邮件",
                    SortOrder = 1,
                    CreatedAt = DateTime.UtcNow,
                    UpdatedAt = DateTime.UtcNow
                },
                new MailTemplate
                {
                    Name = "新回复通知",
                    Slug = "new-reply",
                    Type = MailTemplateType.NewReply,
                    Subject = "[新回复] {{subject}}",
                    BodyHtml = """
                    <p>您好 {{user_name}}，</p>
                    <p>您跟进的会话收到了新回复：</p>
                    <div style="background:#f9f9f9;padding:15px;border-left:3px solid #007bff;margin:10px 0;">
                      <p><strong>{{customer_name}}</strong> 回复道：</p>
                      <p>{{message_body}}</p>
                    </div>
                    <p><a href="{{conversation_url}}">查看完整会话 →</a></p>
                    <p>此致<br/>{{company_name}} 客服团队</p>
                    """,
                    BodyText = "您好 {{user_name}}，\n\n您跟进的会话收到了新回复：\n\n{{customer_name}} 回复道：\n{{message_body}}\n\n查看完整会话：{{conversation_url}}\n\n此致\n{{company_name}} 客服团队",
                    IsActive = true,
                    IsSystem = true,
                    Description = "客户回复会话时发送给客服的通知邮件",
                    SortOrder = 2,
                    CreatedAt = DateTime.UtcNow,
                    UpdatedAt = DateTime.UtcNow
                },
                new MailTemplate
                {
                    Name = "会话分配通知",
                    Slug = "conversation-assigned",
                    Type = MailTemplateType.ConversationAssigned,
                    Subject = "[会话分配] {{subject}}",
                    BodyHtml = """
                    <p>您好 {{user_name}}，</p>
                    <p>有一个会话已分配给您：</p>
                    <table width="100%" cellpadding="8" cellspacing="0" border="0">
                      <tr>
                        <td width="20%" bgcolor="#f5f5f5"><strong>主题</strong></td>
                        <td>{{subject}}</td>
                      </tr>
                      <tr>
                        <td bgcolor="#f5f5f5"><strong>客户</strong></td>
                        <td>{{customer_name}} &lt;{{customer_email}}&gt;</td>
                      </tr>
                    </table>
                    <p><a href="{{conversation_url}}">立即处理 →</a></p>
                    <p>此致<br/>{{company_name}} 客服团队</p>
                    """,
                    BodyText = "您好 {{user_name}}，\n\n有一个会话已分配给您：\n\n主题：{{subject}}\n客户：{{customer_name}} <{{customer_email}}>\n\n立即处理：{{conversation_url}}\n\n此致\n{{company_name}} 客服团队",
                    IsActive = true,
                    IsSystem = true,
                    Description = "会话分配给客服时发送的通知邮件",
                    SortOrder = 3,
                    CreatedAt = DateTime.UtcNow,
                    UpdatedAt = DateTime.UtcNow
                },
                new MailTemplate
                {
                    Name = "会话关闭通知",
                    Slug = "conversation-closed",
                    Type = MailTemplateType.ConversationClosed,
                    Subject = "[会话关闭/Closed] {{subject}}",
                    BodyHtml = """
                    <div style="max-width:600px;margin:0 auto;font-family:-apple-system,BlinkMacSystemFont,'Segoe UI',Roboto,'Helvetica Neue',Arial,'PingFang SC','Microsoft YaHei',sans-serif;">
                      <div style="background:linear-gradient(135deg,#4f46e0 0%,#7c3aed 100%);padding:28px 40px;border-radius:12px 12px 0 0;">
                        <h1 style="margin:0;color:#fff;font-size:20px;font-weight:600;letter-spacing:0.5px;">会话已关闭 / Conversation Closed</h1>
                      </div>
                      <div style="background:#ffffff;padding:32px 40px;border:1px solid #e5e7eb;border-top:none;">
                        <p style="margin:0 0 16px;font-size:15px;line-height:1.7;color:#1f2937;">您好 {{customer_name}}，</p>
                        <p style="margin:0 0 16px;font-size:15px;line-height:1.7;color:#1f2937;">您的问题已经处理完毕，本次会话已正式关闭。感谢您选择 {{company_name}} 的服务！</p>
                        <div style="background:#f0f9ff;border:1px solid #bae6fd;border-radius:10px;padding:20px;margin:24px 0;">
                          <p style="margin:0 0 12px;font-size:14px;font-weight:600;color:#0369a1;">我们非常重视您的反馈</p>
                          <p style="margin:0 0 16px;font-size:14px;line-height:1.6;color:#475569;">请花一分钟时间，对本次服务进行评价。您的意见是我们持续改进的动力。</p>
                          <div style="text-align:center;">
                            <a href="{{satisfaction_url}}" style="display:inline-block;padding:12px 32px;background:#4f46e0;color:#fff;text-decoration:none;border-radius:8px;font-size:15px;font-weight:600;">评价本次服务 →</a>
                          </div>
                        </div>
                        <p style="margin:0 0 16px;font-size:15px;line-height:1.7;color:#1f2937;">如果后续还有其他问题，欢迎随时联系我们。</p>
                        <hr style="border:none;border-top:1px solid #e5e7eb;margin:28px 0;" />
                        <p style="margin:0 0 8px;font-size:15px;line-height:1.7;color:#6b7280;">Dear {{customer_name}},</p>
                        <p style="margin:0 0 16px;font-size:15px;line-height:1.7;color:#6b7280;">Your issue has been resolved and this conversation is now closed. Thank you for choosing {{company_name}}!</p>
                        <p style="margin:0 0 16px;font-size:15px;line-height:1.7;color:#6b7280;">We value your feedback. Please take a moment to rate our service — your input helps us improve.</p>
                        <p style="margin:0;font-size:15px;line-height:1.7;color:#6b7280;">If you have any further questions, please don't hesitate to contact us.</p>
                      </div>
                      <div style="background:#f9fafb;padding:20px 40px;border-radius:0 0 12px 12px;text-align:center;">
                        <p style="margin:0;font-size:13px;color:#6b7280;line-height:1.6;">{{company_name}} 客服团队 / Customer Support Team</p>
                        <p style="margin:4px 0 0;font-size:12px;color:#9ca3af;">此邮件为系统自动发送，请勿直接回复 / This is an automated email, please do not reply directly.</p>
                      </div>
                    </div>
                    """,
                    BodyText = "您好 {{customer_name}}，\n\n您的问题已经处理完毕，会话已关闭。感谢您选择 {{company_name}} 的服务！\n\n我们非常重视您的反馈，请花一分钟时间评价本次服务：\n{{satisfaction_url}}\n\n如果后续还有其他问题，欢迎随时联系我们。\n\n---\nDear {{customer_name}},\n\nYour issue has been resolved and this conversation is now closed. Thank you for choosing {{company_name}}!\n\nWe value your feedback. Please take a moment to rate our service:\n{{satisfaction_url}}\n\nIf you have any further questions, please don't hesitate to contact us.\n\n{{company_name}} 客服团队 / Customer Support Team",
                    IsActive = true,
                    IsSystem = true,
                    Description = "会话关闭时发送给客户的通知邮件（中英双语）",
                    SortOrder = 4,
                    CreatedAt = DateTime.UtcNow,
                    UpdatedAt = DateTime.UtcNow
                },
                new MailTemplate
                {
                    Name = "在线聊天渠道会话关闭通知",
                    Slug = "chat-session-closed",
                    Type = MailTemplateType.ChatSessionClosed,
                    Subject = "【会话已关闭】在线聊天 #{{chat_session_id}} 结束通知",
                    BodyHtml = "",
                    BodyText = """
                    您好 {{customer_name}}，

                    本次在线对话已经结束，感谢您联系 {{company_name}} 的客服团队！

                    请花 30 秒为本次服务评分，帮助我们做得更好：
                    【评价链接】{{satisfaction_url}}

                    本次会话信息：
                    - 会话编号：#{{chat_session_id}}
                    - 接入时间：{{chat_created_at}}
                    - 接待客服：{{agent_name}}
                    - 会话主题：{{chat_subject}}

                    如果您对本次服务还有任何建议或问题，欢迎随时通过同一链接再次联系我们。

                    再次感谢，期待下次再见！
                    {{company_name}} 客户服务中心

                    (此邮件为系统自动发送，请勿直接回复)
                    """,
                    VariablesJson = """
                    {
                      "customer_name": "客户姓名（可从 ChatSession.CustomerName/Customer 姓名回填）",
                      "company_name": "系统设置中的公司名",
                      "satisfaction_url": "满意度评价链接（带 chat session token 免登录）",
                      "chat_session_id": "在线聊天会话编号（ChatSession.Id）",
                      "chat_created_at": "会话接入时间（格式化）",
                      "agent_name": "接待客服姓名",
                      "chat_subject": "会话主题/首条消息摘要"
                    }
                    """,
                    IsActive = true,
                    IsSystem = true,
                    Description = "需求3：在线聊天（Widget 渠道）会话最终关闭时，向客户邮箱发送的纯文本会话关闭通知，必含满意度评价链接。区别于通用 conversation-closed，是在线聊天渠道专属文案。",
                    SortOrder = 15,
                    CreatedAt = DateTime.UtcNow,
                    UpdatedAt = DateTime.UtcNow
                },
                new MailTemplate
                {
                    Name = "[聊天]关闭满意度请求消息（聊天内）",
                    Slug = "chat-close-requested",
                    Type = MailTemplateType.Custom,
                    Subject = "（在线聊天内系统消息，Subject 不用）",
                    BodyHtml = "",
                    BodyText = """
                    您好 {{customer_name}}，您的问题已经处理完毕，如还有其他疑问请随时提出。
                    为帮助我们改进服务，请花几秒钟对本次服务打个分吧：
                    ⭐ 非常满意 (5分)  → 点 "5 星"
                    😊 满意 (4分)      → 点 "4 星"
                    🙂 一般 (3分)      → 点 "3 星"
                    😐 不太满意 (2分)  → 点 "2 星"
                    😞 不满意 (1分)    → 点 "1 星"
                    完成评分后会话将自动关闭，感谢您的反馈 💯
                    （若 3 分钟内未评分，会话将自动关闭）
                    """,
                    VariablesJson = """
                    {
                      "customer_name": "客户姓名"
                    }
                    """,
                    IsActive = true,
                    IsSystem = true,
                    Description = "需求2：系统工作流 [聊天]关闭满意度消息 使用的内容模板。在线聊天客服主动点关闭 → 会话进入 awaiting_rating 阶段后，自动在聊天流里插入这条 system message。",
                    SortOrder = 16,
                    CreatedAt = DateTime.UtcNow,
                    UpdatedAt = DateTime.UtcNow
                },
                new MailTemplate
                {
                    Name = "[聊天]满意度评价感谢消息（聊天内）",
                    Slug = "chat-satisfaction-thanks",
                    Type = MailTemplateType.Custom,
                    Subject = "（在线聊天内系统消息，Subject 不用）",
                    BodyHtml = "",
                    BodyText = """
                    感谢您的评分：{{rating_stars}} （{{rating_label}}）
                    {{rating_comment}}
                    您的反馈对我们非常重要，我们会持续改进服务质量。
                    本次会话已关闭，期待下次再见！ 👋
                    """,
                    VariablesJson = """
                    {
                      "rating_stars": "评分对应的星星符号（如 '★★★★☆'）",
                      "rating_label": "评分中文标签（如 '非常满意/满意/一般/不太满意/不满意'）",
                      "rating_comment": "客户可选填写的评论文本（没填时为空字符串）"
                    }
                    """,
                    IsActive = true,
                    IsSystem = true,
                    Description = "需求2：系统工作流 [聊天]满意度评价感谢消息 使用的内容模板。当客户在 awaiting_rating 阶段完成评分后，自动在聊天流里插入这条感谢 system message（随后会话真正关闭）。",
                    SortOrder = 17,
                    CreatedAt = DateTime.UtcNow,
                    UpdatedAt = DateTime.UtcNow
                },
                new MailTemplate
                {
                    Name = "满意度调查",
                    Slug = "satisfaction-survey",
                    Type = MailTemplateType.SatisfactionSurvey,
                    Subject = "[服务评价/Service Rating] {{subject}}",
                    BodyHtml = """
                    <div style="max-width:600px;margin:0 auto;font-family:-apple-system,BlinkMacSystemFont,'Segoe UI',Roboto,'Helvetica Neue',Arial,'PingFang SC','Microsoft YaHei',sans-serif;">
                      <div style="background:linear-gradient(135deg,#4f46e0 0%,#7c3aed 100%);padding:28px 40px;border-radius:12px 12px 0 0;">
                        <h1 style="margin:0;color:#fff;font-size:20px;font-weight:600;letter-spacing:0.5px;">服务满意度调查 / Satisfaction Survey</h1>
                      </div>
                      <div style="background:#ffffff;padding:32px 40px;border:1px solid #e5e7eb;border-top:none;">
                        <p style="margin:0 0 16px;font-size:15px;line-height:1.7;color:#1f2937;">您好 {{customer_name}}，</p>
                        <p style="margin:0 0 16px;font-size:15px;line-height:1.7;color:#1f2937;">感谢您使用我们的服务！请花一分钟时间，对本次服务进行评价：</p>
                        <p style="margin:0 0 20px;font-size:17px;font-weight:600;text-align:center;color:#1f2937;">您对本次服务满意吗？</p>
                        <table width="100%" cellpadding="0" cellspacing="0" border="0" style="margin:0 0 24px;">
                          <tr>
                            <td align="center" style="padding:6px 4px;">
                              <a href="{{satisfaction_url}}?rating=5" style="display:block;width:100%;max-width:180px;padding:14px 8px;background:#16a34a;color:#fff;text-decoration:none;border-radius:10px;font-size:14px;font-weight:600;line-height:1.4;">😍 远超期望</a>
                            </td>
                            <td align="center" style="padding:6px 4px;">
                              <a href="{{satisfaction_url}}?rating=4" style="display:block;width:100%;max-width:180px;padding:14px 8px;background:#059669;color:#fff;text-decoration:none;border-radius:10px;font-size:14px;font-weight:600;line-height:1.4;">😊 物有所值</a>
                            </td>
                          </tr>
                          <tr>
                            <td align="center" style="padding:6px 4px;">
                              <a href="{{satisfaction_url}}?rating=3" style="display:block;width:100%;max-width:180px;padding:14px 8px;background:#d97706;color:#fff;text-decoration:none;border-radius:10px;font-size:14px;font-weight:600;line-height:1.4;">😐 中规中矩</a>
                            </td>
                            <td align="center" style="padding:6px 4px;">
                              <a href="{{satisfaction_url}}?rating=2" style="display:block;width:100%;max-width:180px;padding:14px 8px;background:#ea580c;color:#fff;text-decoration:none;border-radius:10px;font-size:14px;font-weight:600;line-height:1.4;">😟 略有失望</a>
                            </td>
                          </tr>
                          <tr>
                            <td align="center" colspan="2" style="padding:6px 4px;">
                              <a href="{{satisfaction_url}}?rating=1" style="display:inline-block;padding:14px 32px;background:#dc2626;color:#fff;text-decoration:none;border-radius:10px;font-size:14px;font-weight:600;line-height:1.4;">😡 无法接受</a>
                            </td>
                          </tr>
                        </table>
                        <p style="margin:0 0 16px;font-size:14px;line-height:1.6;color:#475569;text-align:center;">您的反馈对我们非常重要！点击对应选项即可完成评价。</p>
                        <hr style="border:none;border-top:1px solid #e5e7eb;margin:28px 0;" />
                        <p style="margin:0 0 8px;font-size:15px;line-height:1.7;color:#6b7280;">Dear {{customer_name}},</p>
                        <p style="margin:0 0 16px;font-size:15px;line-height:1.7;color:#6b7280;">Thank you for using our service! Please take a moment to rate your experience:</p>
                        <p style="margin:0 0 20px;font-size:17px;font-weight:600;text-align:center;color:#6b7280;">How was our service?</p>
                        <table width="100%" cellpadding="0" cellspacing="0" border="0" style="margin:0 0 24px;">
                          <tr>
                            <td align="center" style="padding:6px 4px;">
                              <a href="{{satisfaction_url}}?rating=5" style="display:block;width:100%;max-width:180px;padding:14px 8px;background:#16a34a;color:#fff;text-decoration:none;border-radius:10px;font-size:14px;font-weight:600;line-height:1.4;">😍 Exceeds Expectations</a>
                            </td>
                            <td align="center" style="padding:6px 4px;">
                              <a href="{{satisfaction_url}}?rating=4" style="display:block;width:100%;max-width:180px;padding:14px 8px;background:#059669;color:#fff;text-decoration:none;border-radius:10px;font-size:14px;font-weight:600;line-height:1.4;">😊 Good Value</a>
                            </td>
                          </tr>
                          <tr>
                            <td align="center" style="padding:6px 4px;">
                              <a href="{{satisfaction_url}}?rating=3" style="display:block;width:100%;max-width:180px;padding:14px 8px;background:#d97706;color:#fff;text-decoration:none;border-radius:10px;font-size:14px;font-weight:600;line-height:1.4;">😐 Average</a>
                            </td>
                            <td align="center" style="padding:6px 4px;">
                              <a href="{{satisfaction_url}}?rating=2" style="display:block;width:100%;max-width:180px;padding:14px 8px;background:#ea580c;color:#fff;text-decoration:none;border-radius:10px;font-size:14px;font-weight:600;line-height:1.4;">😟 Disappointing</a>
                            </td>
                          </tr>
                          <tr>
                            <td align="center" colspan="2" style="padding:6px 4px;">
                              <a href="{{satisfaction_url}}?rating=1" style="display:inline-block;padding:14px 32px;background:#dc2626;color:#fff;text-decoration:none;border-radius:10px;font-size:14px;font-weight:600;line-height:1.4;">😡 Unacceptable</a>
                            </td>
                          </tr>
                        </table>
                        <p style="margin:0;font-size:14px;line-height:1.6;color:#6b7280;text-align:center;">Your feedback is very important to us! Click an option above to complete your rating.</p>
                      </div>
                      <div style="background:#f9fafb;padding:20px 40px;border-radius:0 0 12px 12px;text-align:center;">
                        <p style="margin:0;font-size:13px;color:#6b7280;line-height:1.6;">{{company_name}} 客服团队 / Customer Support Team</p>
                        <p style="margin:4px 0 0;font-size:12px;color:#9ca3af;">此邮件为系统自动发送，请勿直接回复 / This is an automated email, please do not reply directly.</p>
                      </div>
                    </div>
                    """,
                    BodyText = "您好 {{customer_name}}，\n\n感谢您使用我们的服务！请对本次服务进行评价：\n\n  5 - 远超期望  {{satisfaction_url}}?rating=5\n  4 - 物有所值  {{satisfaction_url}}?rating=4\n  3 - 中规中矩  {{satisfaction_url}}?rating=3\n  2 - 略有失望  {{satisfaction_url}}?rating=2\n  1 - 无法接受  {{satisfaction_url}}?rating=1\n\n您的反馈对我们非常重要！\n\n---\nDear {{customer_name}},\n\nThank you for using our service! Please rate your experience:\n\n  5 - Exceeds Expectations  {{satisfaction_url}}?rating=5\n  4 - Good Value             {{satisfaction_url}}?rating=4\n  3 - Average                {{satisfaction_url}}?rating=3\n  2 - Disappointing          {{satisfaction_url}}?rating=2\n  1 - Unacceptable           {{satisfaction_url}}?rating=1\n\nYour feedback is very important to us!\n\n{{company_name}} 客服团队 / Customer Support Team",
                    IsActive = true,
                    IsSystem = true,
                    Description = "会话关闭后发送给客户的满意度调查邮件（中英双语，5分制）",
                    SortOrder = 5,
                    CreatedAt = DateTime.UtcNow,
                    UpdatedAt = DateTime.UtcNow
                },
                new MailTemplate
                {
                    Name = "密码重置",
                    Slug = "password-reset",
                    Type = MailTemplateType.PasswordReset,
                    Subject = "重置您的密码",
                    BodyHtml = """
                    <p>您好 {{user_name}}，</p>
                    <p>我们收到了您的密码重置请求。</p>
                    <p>请点击下面的链接重置您的密码：</p>
                    <p style="text-align:center;margin:20px 0;">
                      <a href="{{reset_password_url}}" style="display:inline-block;padding:10px 20px;background:#007bff;color:#fff;text-decoration:none;border-radius:4px;">重置密码</a>
                    </p>
                    <p>如果链接无法点击，请复制以下地址到浏览器：</p>
                    <p style="background:#f5f5f5;padding:10px;font-family:monospace;word-break:break-all;">{{reset_password_url}}</p>
                    <p><strong>注意：</strong>此链接将在 24 小时后失效。</p>
                    <p>如果您没有请求重置密码，请忽略此邮件。</p>
                    <p>此致<br/>{{company_name}} 团队</p>
                    """,
                    BodyText = "您好 {{user_name}}，\n\n我们收到了您的密码重置请求。\n\n请点击下面的链接重置您的密码：\n{{reset_password_url}}\n\n注意：此链接将在 24 小时后失效。\n\n如果您没有请求重置密码，请忽略此邮件。\n\n此致\n{{company_name}} 团队",
                    IsActive = true,
                    IsSystem = true,
                    Description = "用户请求重置密码时发送的邮件",
                    SortOrder = 6,
                    CreatedAt = DateTime.UtcNow,
                    UpdatedAt = DateTime.UtcNow
                },
                new MailTemplate
                {
                    Name = "欢迎邮件",
                    Slug = "welcome",
                    Type = MailTemplateType.Welcome,
                    Subject = "欢迎使用 {{company_name}}",
                    BodyHtml = """
                    <p>您好 {{user_name}}，</p>
                    <p>欢迎使用 {{company_name}} 客服系统！</p>
                    <p>您的账号已创建成功：</p>
                    <ul>
                      <li><strong>用户名：</strong>{{user_email}}</li>
                      <li><strong>登录链接：</strong><a href="{{login_url}}">{{login_url}}</a></li>
                    </ul>
                    <p>如有任何问题，请随时联系我们的支持团队。</p>
                    <p>祝您使用愉快！</p>
                    <p>此致<br/>{{company_name}} 团队</p>
                    """,
                    BodyText = "您好 {{user_name}}，\n\n欢迎使用 {{company_name}} 客服系统！\n\n您的账号已创建成功：\n- 用户名：{{user_email}}\n- 登录链接：{{login_url}}\n\n如有任何问题，请随时联系我们的支持团队。\n\n祝您使用愉快！\n\n此致\n{{company_name}} 团队",
                    IsActive = true,
                    IsSystem = true,
                    Description = "新用户注册时发送的欢迎邮件",
                    SortOrder = 7,
                    CreatedAt = DateTime.UtcNow,
                    UpdatedAt = DateTime.UtcNow
                },
                // 新增必要系统模板：账号激活
                new MailTemplate
                {
                    Name = "账号激活",
                    Slug = "account-activation",
                    Type = MailTemplateType.Welcome,
                    Subject = "激活您的 {{company_name}} 账号",
                    BodyHtml = """
                    <p>您好 {{user_name}}，</p>
                    <p>感谢您注册 {{company_name}}！</p>
                    <p>请点击下方按钮激活您的账号：</p>
                    <p style="text-align:center;margin:20px 0;">
                      <a href="{{activation_url}}" style="display:inline-block;padding:10px 24px;background:#28a745;color:#fff;text-decoration:none;border-radius:4px;font-weight:bold;">激活账号</a>
                    </p>
                    <p style="background:#f5f5f5;padding:10px;font-family:monospace;word-break:break-all;">{{activation_url}}</p>
                    <p><strong>注意：</strong>此激活链接将在 48 小时后失效。</p>
                    <p>如果您没有注册账号，请忽略此邮件。</p>
                    <p>此致<br/>{{company_name}} 团队</p>
                    """,
                    BodyText = "您好 {{user_name}}，\n\n感谢您注册 {{company_name}}！\n\n请访问以下链接激活您的账号：\n{{activation_url}}\n\n注意：此激活链接将在 48 小时后失效。\n\n如果您没有注册账号，请忽略此邮件。\n\n此致\n{{company_name}} 团队",
                    IsActive = true,
                    IsSystem = true,
                    Description = "用户注册后需要激活账号时发送的邮件",
                    SortOrder = 8,
                    CreatedAt = DateTime.UtcNow,
                    UpdatedAt = DateTime.UtcNow
                },
                // 新增必要自定义模板：客户回复模板（自动调用）
                new MailTemplate
                {
                    Name = "通用客户回复模板",
                    Slug = "customer-reply-default",
                    Type = MailTemplateType.Custom,
                    Subject = "Re: {{subject}}",
                    BodyHtml = """
                    <p>您好 {{customer_name}}，</p>
                    <p>感谢您联系我们。关于您提到的问题，我们已经收到并处理如下：</p>
                    <div style="background:#f9f9f9;padding:15px;border-left:3px solid #007bff;margin:10px 0;">
                      <p><strong>{{user_name}}</strong> 回复道：</p>
                      <p>{{message_body}}</p>
                    </div>
                    <p>如果还有其他问题，请随时回复本邮件。</p>
                    <p>此致<br/>{{company_name}} 客服团队</p>
                    """,
                    BodyText = "您好 {{customer_name}}，\n\n感谢您联系我们。关于您提到的问题，我们已经收到并处理如下：\n\n{{user_name}} 回复道：\n{{message_body}}\n\n如果还有其他问题，请随时回复本邮件。\n\n此致\n{{company_name}} 客服团队",
                    IsActive = true,
                    IsSystem = false,
                    Description = "默认的客户回复模板，作为 new-reply 模板的后备（自动调用）",
                    SortOrder = 100,
                    CreatedAt = DateTime.UtcNow,
                    UpdatedAt = DateTime.UtcNow
                },
                // 新增自定义模板：工单超时提醒
                new MailTemplate
                {
                    Name = "工单超时提醒",
                    Slug = "sla-warning",
                    Type = MailTemplateType.Custom,
                    Subject = "[超时提醒] {{subject}} 即将超过 SLA",
                    BodyHtml = """
                    <p>您好 {{user_name}}，</p>
                    <p>您处理的会话即将超过 SLA 时限，请尽快响应：</p>
                    <table width="100%" cellpadding="8" cellspacing="0" border="0">
                      <tr>
                        <td width="20%" bgcolor="#fff3cd"><strong>主题</strong></td>
                        <td>{{subject}}</td>
                      </tr>
                      <tr>
                        <td bgcolor="#fff3cd"><strong>客户</strong></td>
                        <td>{{customer_name}} &lt;{{customer_email}}&gt;</td>
                      </tr>
                      <tr>
                        <td bgcolor="#fff3cd"><strong>剩余时间</strong></td>
                        <td>{{sla_remaining}}</td>
                      </tr>
                    </table>
                    <p style="text-align:center;margin:20px 0;">
                      <a href="{{conversation_url}}" style="display:inline-block;padding:10px 20px;background:#ffc107;color:#fff;text-decoration:none;border-radius:4px;">立即处理 →</a>
                    </p>
                    <p>此致<br/>{{company_name}} 系统通知</p>
                    """,
                    BodyText = "您好 {{user_name}}，\n\n您处理的会话即将超过 SLA 时限，请尽快响应：\n\n主题：{{subject}}\n客户：{{customer_name}} <{{customer_email}}>\n剩余时间：{{sla_remaining}}\n\n立即处理：{{conversation_url}}\n\n此致\n{{company_name}} 系统通知",
                    IsActive = true,
                    IsSystem = false,
                    Description = "工单即将超过 SLA 时限发送给客服的提醒邮件",
                    SortOrder = 101,
                    CreatedAt = DateTime.UtcNow,
                    UpdatedAt = DateTime.UtcNow
                },
                // 新增自定义模板：满意度感谢邮件
                new MailTemplate
                {
                    Name = "满意度评价感谢",
                    Slug = "satisfaction-thanks",
                    Type = MailTemplateType.Custom,
                    Subject = "[感谢您的评价/Thank You] {{subject}}",
                    BodyHtml = """
                    <div style="max-width:600px;margin:0 auto;font-family:-apple-system,BlinkMacSystemFont,'Segoe UI',Roboto,'Helvetica Neue',Arial,'PingFang SC','Microsoft YaHei',sans-serif;">
                      <div style="background:linear-gradient(135deg,#4f46e0 0%,#7c3aed 100%);padding:28px 40px;border-radius:12px 12px 0 0;">
                        <h1 style="margin:0;color:#fff;font-size:20px;font-weight:600;letter-spacing:0.5px;">感谢您的评价 / Thank You for Your Rating</h1>
                      </div>
                      <div style="background:#ffffff;padding:32px 40px;border:1px solid #e5e7eb;border-top:none;">
                        <p style="margin:0 0 16px;font-size:15px;line-height:1.7;color:#1f2937;">您好 {{customer_name}}，</p>
                        <p style="margin:0 0 16px;font-size:15px;line-height:1.7;color:#1f2937;">非常感谢您抽出宝贵时间为我们的服务打分！</p>
                        <div style="background:linear-gradient(135deg,#f0f9ff 0%,#faf5ff 100%);border:1px solid #e0e7ff;border-radius:10px;padding:20px 24px;margin:24px 0;text-align:center;">
                          <p style="margin:0 0 8px;font-size:14px;color:#475569;">您的评价 / Your Rating</p>
                          <p style="margin:0;font-size:24px;font-weight:700;color:#4f46e0;">{{satisfaction_rating}}</p>
                        </div>
                        <p style="margin:0 0 16px;font-size:15px;line-height:1.7;color:#1f2937;">您的反馈是我们持续改进的动力。如果您有任何进一步的建议，欢迎随时回复本邮件告诉我们。</p>
                        <p style="margin:0;font-size:15px;line-height:1.7;color:#1f2937;">期待再次为您服务！</p>
                        <hr style="border:none;border-top:1px solid #e5e7eb;margin:28px 0;" />
                        <p style="margin:0 0 8px;font-size:15px;line-height:1.7;color:#6b7280;">Dear {{customer_name}},</p>
                        <p style="margin:0 0 16px;font-size:15px;line-height:1.7;color:#6b7280;">Thank you very much for taking the time to rate our service!</p>
                        <div style="background:linear-gradient(135deg,#f0f9ff 0%,#faf5ff 100%);border:1px solid #e0e7ff;border-radius:10px;padding:20px 24px;margin:24px 0;text-align:center;">
                          <p style="margin:0 0 8px;font-size:14px;color:#6b7280;">Your Rating</p>
                          <p style="margin:0;font-size:24px;font-weight:700;color:#4f46e0;">{{satisfaction_rating}}</p>
                        </div>
                        <p style="margin:0 0 16px;font-size:15px;line-height:1.7;color:#6b7280;">Your feedback is the driving force behind our continuous improvement. If you have any further suggestions, please feel free to reply to this email.</p>
                        <p style="margin:0;font-size:15px;line-height:1.7;color:#6b7280;">We look forward to serving you again!</p>
                      </div>
                      <div style="background:#f9fafb;padding:20px 40px;border-radius:0 0 12px 12px;text-align:center;">
                        <p style="margin:0;font-size:13px;color:#6b7280;line-height:1.6;">{{company_name}} 客服团队 / Customer Support Team</p>
                        <p style="margin:4px 0 0;font-size:12px;color:#9ca3af;">此邮件为系统自动发送，请勿直接回复 / This is an automated email, please do not reply directly.</p>
                      </div>
                    </div>
                    """,
                    BodyText = "您好 {{customer_name}}，\n\n非常感谢您抽出宝贵时间为我们的服务打分！\n\n您的评价：{{satisfaction_rating}}\n\n您的反馈是我们持续改进的动力。如果您有任何进一步的建议，欢迎随时回复本邮件告诉我们。\n\n期待再次为您服务！\n\n---\nDear {{customer_name}},\n\nThank you very much for taking the time to rate our service!\n\nYour rating: {{satisfaction_rating}}\n\nYour feedback is the driving force behind our continuous improvement. If you have any further suggestions, please feel free to reply to this email.\n\nWe look forward to serving you again!\n\n{{company_name}} 客服团队 / Customer Support Team",
                    IsActive = true,
                    IsSystem = false,
                    Description = "客户提交满意度评价后发送的感谢邮件（中英双语）",
                    SortOrder = 102,
                    CreatedAt = DateTime.UtcNow,
                    UpdatedAt = DateTime.UtcNow
                },
                // 新增自定义模板：IM 渠道满意度感谢消息（纯文本，供 send_im_message 动作使用）
                new MailTemplate
                {
                    Name = "IM 渠道满意度评价感谢",
                    Slug = "im-satisfaction-thanks",
                    Type = MailTemplateType.Custom,
                    Subject = "感谢您的评价",
                    // IM 消息为纯文本，不使用 HTML；BodyText 为发送内容主体
                    BodyHtml = "",
                    BodyText = "您好 {{customer_name}}，非常感谢您抽出宝贵时间为我们的服务打分！\n\n您的评价：{{satisfaction_rating}}\n\n您的反馈是我们持续改进的动力。如后续有任何建议，欢迎随时联系我们。\n\n期待再次为您服务！\n\n{{company_name}} 客服团队",
                    IsActive = true,
                    IsSystem = false,
                    Description = "IM 渠道（微信/飞书/WhatsApp/钉钉）客户提交满意度评价后，通过对应 App 发送的感谢消息（纯文本）",
                    SortOrder = 103,
                    CreatedAt = DateTime.UtcNow,
                    UpdatedAt = DateTime.UtcNow
                },
                // 新增自定义模板：客户回访
                new MailTemplate
                {
                    Name = "客户回访邮件",
                    Slug = "customer-followup",
                    Type = MailTemplateType.Custom,
                    Subject = "回访 - 关于您之前咨询的 {{subject}}",
                    BodyHtml = """
                    <p>您好 {{customer_name}}，</p>
                    <p>前段时间您联系我们咨询过 <strong>{{subject}}</strong>，我们想了解问题是否已经得到妥善解决。</p>
                    <p>如果您还有任何疑问或需要进一步的帮助，请随时回复本邮件或访问：</p>
                    <p style="text-align:center;margin:20px 0;">
                      <a href="{{conversation_url}}" style="display:inline-block;padding:10px 20px;background:#007bff;color:#fff;text-decoration:none;border-radius:4px;">查看历史会话 →</a>
                    </p>
                    <p>感谢您选择 {{company_name}}，期待再次为您服务！</p>
                    <p>此致<br/>{{company_name}} 客服团队</p>
                    """,
                    BodyText = "您好 {{customer_name}}，\n\n前段时间您联系我们咨询过 {{subject}}，我们想了解问题是否已经得到妥善解决。\n\n如果您还有任何疑问或需要进一步的帮助，请随时回复本邮件或访问：\n{{conversation_url}}\n\n感谢您选择 {{company_name}}，期待再次为您服务！\n\n此致\n{{company_name}} 客服团队",
                    IsActive = true,
                    IsSystem = false,
                    Description = "客服定期回访客户、确认问题是否解决时使用的邮件模板",
                    SortOrder = 103,
                    CreatedAt = DateTime.UtcNow,
                    UpdatedAt = DateTime.UtcNow
                },
                // 新增自定义模板：会话升级通知
                new MailTemplate
                {
                    Name = "会话升级通知",
                    Slug = "conversation-escalation",
                    Type = MailTemplateType.Custom,
                    Subject = "[紧急升级] {{subject}}",
                    BodyHtml = """
                    <p>您好 {{user_name}}，</p>
                    <p>有一个会话已被升级为紧急，需要您的关注：</p>
                    <table width="100%" cellpadding="8" cellspacing="0" border="0">
                      <tr>
                        <td width="20%" bgcolor="#f8d7da"><strong>主题</strong></td>
                        <td>{{subject}}</td>
                      </tr>
                      <tr>
                        <td bgcolor="#f8d7da"><strong>客户</strong></td>
                        <td>{{customer_name}} &lt;{{customer_email}}&gt;</td>
                      </tr>
                      <tr>
                        <td bgcolor="#f8d7da"><strong>升级原因</strong></td>
                        <td>{{escalation_reason}}</td>
                      </tr>
                    </table>
                    <p style="text-align:center;margin:20px 0;">
                      <a href="{{conversation_url}}" style="display:inline-block;padding:10px 20px;background:#dc3545;color:#fff;text-decoration:none;border-radius:4px;">立即查看 →</a>
                    </p>
                    <p>此致<br/>{{company_name}} 系统通知</p>
                    """,
                    BodyText = "您好 {{user_name}}，\n\n有一个会话已被升级为紧急，需要您的关注：\n\n主题：{{subject}}\n客户：{{customer_name}} <{{customer_email}}>\n升级原因：{{escalation_reason}}\n\n立即查看：{{conversation_url}}\n\n此致\n{{company_name}} 系统通知",
                    IsActive = true,
                    IsSystem = false,
                    Description = "会话被升级为紧急级别时发送给客服的通知邮件",
                    SortOrder = 104,
                    CreatedAt = DateTime.UtcNow,
                    UpdatedAt = DateTime.UtcNow
                }
            };

            var mailToAdd = mailTemplatesToAdd.Where(t => !mailExistingSlugs.Contains(t.Slug)).ToList();
            if (mailToAdd.Count > 0)
            {
                context.MailTemplates.AddRange(mailToAdd);
                try
                {
                    await context.SaveChangesAsync();
                }
                catch (DbUpdateException ex) when (ex.InnerException is Microsoft.Data.Sqlite.SqliteException se
                    && se.SqliteErrorCode == 19) // SQLITE_CONSTRAINT
                {
                    // 某些环境下 SQLite 旧索引未带 DeletedAt IS NULL 过滤器，
                    // 或 EF Core 变更跟踪器状态异常导致 UNIQUE 冲突。
                    // 降级为逐条插入并跳过已存在的 Slug，不阻塞启动。
                    logger.LogWarning("MailTemplate 批量插入失败（{Msg}），降级为逐条插入", se.Message);
                    foreach (var item in mailToAdd)
                    {
                        try
                        {
                            context.MailTemplates.Add(item);
                            await context.SaveChangesAsync();
                        }
                        catch (DbUpdateException)
                        {
                            // 跳过已存在的 Slug，继续下一条
                            context.Entry(item).State = EntityState.Detached;
                        }
                    }
                }
            }
        }

        // 工作流自动化规则种子数据（已拆分到 WorkflowSeeder）
        await Seeders.WorkflowSeeder.SeedAsync(context);

        // 常用宏种子数据 - 增量添加，确保每次启动都检查并补全
        // 注意：原 Users 块内的 4 个宏只在全新数据库时执行，
        // 此处独立增量逻辑保证已有数据库也能按名称补充缺失的常用宏
        {
            var extraMacros = new[]
            {
                new Macro
                {
                    Name = "关闭 - 重复问题",
                    Description = "关闭会话并标记为重复问题",
                    MailboxId = null,
                    IsActive = true,
                    SortOrder = 10,
                    ActionsJson = "[{\"type\":\"status\",\"value\":3},{\"type\":\"add_tag\",\"value\":\"重复\"}]",
                    CreatedAt = DateTime.UtcNow.AddDays(-2),
                    UpdatedAt = DateTime.UtcNow.AddDays(-2)
                },
                new Macro
                {
                    Name = "关闭 - 无法解决",
                    Description = "关闭会话并标记为无法解决",
                    MailboxId = null,
                    IsActive = true,
                    SortOrder = 11,
                    ActionsJson = "[{\"type\":\"status\",\"value\":3},{\"type\":\"add_tag\",\"value\":\"无法解决\"}]",
                    CreatedAt = DateTime.UtcNow.AddDays(-2),
                    UpdatedAt = DateTime.UtcNow.AddDays(-2)
                },
                new Macro
                {
                    Name = "标记为垃圾",
                    Description = "将会话标记为垃圾邮件",
                    MailboxId = null,
                    IsActive = true,
                    SortOrder = 12,
                    ActionsJson = "[{\"type\":\"status\",\"value\":4},{\"type\":\"add_tag\",\"value\":\"垃圾\"}]",
                    CreatedAt = DateTime.UtcNow.AddDays(-2),
                    UpdatedAt = DateTime.UtcNow.AddDays(-2)
                },
                new Macro
                {
                    Name = "升级紧急",
                    Description = "将优先级改为紧急并添加紧急标签，状态改为待处理",
                    MailboxId = null,
                    IsActive = true,
                    SortOrder = 13,
                    ActionsJson = "[{\"type\":\"priority\",\"value\":3},{\"type\":\"add_tag\",\"value\":\"紧急\"},{\"type\":\"status\",\"value\":2}]",
                    CreatedAt = DateTime.UtcNow.AddDays(-2),
                    UpdatedAt = DateTime.UtcNow.AddDays(-2)
                },
                new Macro
                {
                    Name = "降为普通",
                    Description = "将优先级改为普通，并移除紧急标签",
                    MailboxId = null,
                    IsActive = true,
                    SortOrder = 14,
                    ActionsJson = "[{\"type\":\"priority\",\"value\":1},{\"type\":\"remove_tag\",\"value\":\"紧急\"}]",
                    CreatedAt = DateTime.UtcNow.AddDays(-2),
                    UpdatedAt = DateTime.UtcNow.AddDays(-2)
                },
                new Macro
                {
                    Name = "等待客户回复",
                    Description = "将状态改为待处理，并添加'等待客户'标签",
                    MailboxId = null,
                    IsActive = true,
                    SortOrder = 15,
                    ActionsJson = "[{\"type\":\"status\",\"value\":2},{\"type\":\"add_tag\",\"value\":\"等待客户\"}]",
                    CreatedAt = DateTime.UtcNow.AddDays(-2),
                    UpdatedAt = DateTime.UtcNow.AddDays(-2)
                },
                new Macro
                {
                    Name = "VIP 客户处理",
                    Description = "将优先级改为高、添加 VIP 标签",
                    MailboxId = null,
                    IsActive = true,
                    SortOrder = 16,
                    ActionsJson = "[{\"type\":\"priority\",\"value\":2},{\"type\":\"add_tag\",\"value\":\"VIP\"}]",
                    CreatedAt = DateTime.UtcNow.AddDays(-2),
                    UpdatedAt = DateTime.UtcNow.AddDays(-2)
                },
                new Macro
                {
                    Name = "重新打开",
                    Description = "将会话状态改回活跃，并添加'重新打开'标签",
                    MailboxId = null,
                    IsActive = true,
                    SortOrder = 17,
                    ActionsJson = "[{\"type\":\"status\",\"value\":1},{\"type\":\"add_tag\",\"value\":\"重新打开\"}]",
                    CreatedAt = DateTime.UtcNow.AddDays(-2),
                    UpdatedAt = DateTime.UtcNow.AddDays(-2)
                },
                new Macro
                {
                    Name = "添加跟进备注",
                    Description = "添加内部备注：已处理，需后续跟进确认客户是否还有其他问题",
                    MailboxId = null,
                    IsActive = true,
                    SortOrder = 18,
                    ActionsJson = "[{\"type\":\"add_note\",\"value\":\"已处理，需后续跟进确认客户是否还有其他问题。\"}]",
                    CreatedAt = DateTime.UtcNow.AddDays(-2),
                    UpdatedAt = DateTime.UtcNow.AddDays(-2)
                },
                new Macro
                {
                    Name = "致谢并关闭",
                    Description = "发送感谢回复、关闭会话并添加'已解决'标签",
                    MailboxId = null,
                    IsActive = true,
                    SortOrder = 19,
                    ActionsJson = "[{\"type\":\"reply\",\"value\":\"感谢您的耐心等待，问题已解决。如有其他疑问欢迎随时联系。\"},{\"type\":\"status\",\"value\":3},{\"type\":\"add_tag\",\"value\":\"已解决\"}]",
                    CreatedAt = DateTime.UtcNow.AddDays(-2),
                    UpdatedAt = DateTime.UtcNow.AddDays(-2)
                },
                new Macro
                {
                    Name = "请求更多信息",
                    Description = "回复客户请求提供更多信息，状态改为待处理",
                    MailboxId = null,
                    IsActive = true,
                    SortOrder = 20,
                    ActionsJson = "[{\"type\":\"reply\",\"value\":\"为了更好地协助您处理此问题，请提供以下信息：\\n1. 问题发生的时间\\n2. 您看到的错误提示截图\\n3. 您使用的浏览器/设备\"},{\"type\":\"status\",\"value\":2},{\"type\":\"add_tag\",\"value\":\"待补充信息\"}]",
                    CreatedAt = DateTime.UtcNow.AddDays(-2),
                    UpdatedAt = DateTime.UtcNow.AddDays(-2)
                },
                new Macro
                {
                    Name = "转交二线支持",
                    Description = "升级优先级、添加二线支持标签、添加转交备注",
                    MailboxId = null,
                    IsActive = true,
                    SortOrder = 21,
                    ActionsJson = "[{\"type\":\"priority\",\"value\":2},{\"type\":\"add_tag\",\"value\":\"二线支持\"},{\"type\":\"add_note\",\"value\":\"客户问题复杂，已转交二线支持团队跟进。\"}]",
                    CreatedAt = DateTime.UtcNow.AddDays(-2),
                    UpdatedAt = DateTime.UtcNow.AddDays(-2)
                }
            };

            var macrosToAdd = extraMacros
                .Where(m => !context.Macros.Any(x => x.Name == m.Name))
                .ToList();
            if (macrosToAdd.Count > 0)
            {
                context.Macros.AddRange(macrosToAdd);
                await context.SaveChangesAsync();
            }
        }

        // 通知模板种子数据（已拆分到 NotificationTemplateSeeder）
        await Seeders.NotificationTemplateSeeder.SeedAsync(context);

        // 通知规则种子数据（已拆分到 NotificationRuleSeeder）
        await Seeders.NotificationRuleSeeder.SeedAsync(context);

        // ============ T000051：系统邮件模板正文规范化（属性式版式升级） ============
        // 旧版 new-reply / customer-reply-default 模板将消息正文 {{message_body}} 直接包在 <p> 内
        // （正文是块级 HTML，<p><div>…</div></p> 属非法嵌套），且版式依赖 style 内联样式。
        // 模板保存与 SendLog.Body 入库时若再次经过 HtmlSanitizer，style 被剥离、嵌套被 AngleSharp
        // 重排为 <p></p><div>…</div><p></p>，导致客服收到的"新回复通知"邮件无样式、出现空段/标签痕迹。
        // 升级为属性式版式（bgcolor/width/cellpadding/cellspacing/border 均在此 HtmlSanitizer 白名单内），
        // 并把消息正文放入 td 块级上下文。仅当模板仍是旧版 "<p>{{message_body}}</p>" 结构时覆盖，
        // 避免覆盖管理员后续的自定义修改。
        {
            var templateLayoutUpgraded = false;
            const string legacyMessageParagraph = "<p>{{message_body}}</p>";

            var systemReplyTemplates = context.MailTemplates
                .IgnoreQueryFilters()
                // 不限制 IsSystem：customer-reply-default 是客服回复邮件的默认正文模板
                // （数据库种子将其标记为 IsSystem=false 的 Custom 类型），同样需要升级。
                .Where(t => t.Slug == "new-reply" || t.Slug == "customer-reply-default")
                .ToList();

            foreach (var tpl in systemReplyTemplates)
            {
                var needsUpgrade = tpl.BodyHtml != null
                    && tpl.BodyHtml.Contains(legacyMessageParagraph, StringComparison.OrdinalIgnoreCase);
                if (!needsUpgrade)
                {
                    continue;
                }

                if (tpl.Slug == "new-reply")
                {
                    tpl.BodyHtml = """
                        <p>您好 {{user_name}}，</p>
                        <p>您跟进的会话收到了新回复：</p>
                        <table width="100%" cellpadding="0" cellspacing="0" border="0">
                          <tr>
                            <td width="4" bgcolor="#007bff">&nbsp;</td>
                            <td bgcolor="#f8f9fa" align="left" valign="top">
                              <p><strong>{{customer_name}}</strong> 回复道：</p>
                              {{message_body}}
                            </td>
                          </tr>
                        </table>
                        <p><a href="{{conversation_url}}">查看完整会话 →</a></p>
                        <p>此致<br/>{{company_name}} 客服团队</p>
                        """;
                    tpl.BodyText = "您好 {{user_name}}，\n\n您跟进的会话收到了新回复：\n\n{{customer_name}} 回复道：\n{{message_body}}\n\n查看完整会话：{{conversation_url}}\n\n此致\n{{company_name}} 客服团队";
                }
                else
                {
                    tpl.BodyHtml = """
                        <p>您好 {{customer_name}}，</p>
                        <p>感谢您联系我们。关于您提到的问题，我们已经收到并处理如下：</p>
                        <table width="100%" cellpadding="0" cellspacing="0" border="0">
                          <tr>
                            <td width="4" bgcolor="#007bff">&nbsp;</td>
                            <td bgcolor="#f8f9fa" align="left" valign="top">
                              <p><strong>{{user_name}}</strong> 回复道：</p>
                              {{message_body}}
                            </td>
                          </tr>
                        </table>
                        <p>如果还有其他问题，请随时回复本邮件。</p>
                        <p>此致<br/>{{company_name}} 客服团队</p>
                        """;
                    tpl.BodyText = "您好 {{customer_name}}，\n\n感谢您联系我们。关于您提到的问题，我们已经收到并处理如下：\n\n{{user_name}} 回复道：\n{{message_body}}\n\n如果还有其他问题，请随时回复本邮件。\n\n此致\n{{company_name}} 客服团队";
                }

                tpl.UpdatedAt = DateTime.UtcNow;
                templateLayoutUpgraded = true;
            }

            if (templateLayoutUpgraded)
            {
                await context.SaveChangesAsync();
            }
        }
    }

    /// <summary>
    /// 确保迁移历史表存在，处理从 EnsureCreated 到 Migrate 的过渡
    /// </summary>
    /// <remarks>
    /// 如果数据库已存在但没有迁移历史表（即之前由 EnsureCreated 创建），
    /// 则创建迁移历史表并将所有现有迁移标记为已应用，
    /// 避免 Migrate 尝试重复创建已存在的表。
    /// </remarks>
    /// <param name="context">数据库上下文</param>
    private static async Task EnsureMigrationsHistoryAsync(NoneadCSSDbContext context)
    {
        var databaseExists = await context.Database.CanConnectAsync();
        if (!databaseExists)
        {
            return;
        }

        // 检查迁移历史表是否存在
        await using var connection = context.Database.GetDbConnection();
        await connection.OpenAsync();
        await using var command = connection.CreateCommand();
        command.CommandText = "SELECT COUNT(*) FROM sqlite_master WHERE type='table' AND name='__EFMigrationsHistory'";
        var result = await command.ExecuteScalarAsync();
        var historyTableExists = Convert.ToInt32(result) > 0;

        if (historyTableExists)
        {
            return;
        }

        // 检查是否有任何用户表（Users）存在，只有当数据库已有表时才标记迁移为已应用
        // 如果数据库是空的（刚创建），则不标记迁移，让 MigrateAsync 正常创建所有表
        command.CommandText = "SELECT COUNT(*) FROM sqlite_master WHERE type='table' AND name='Users'";
        result = await command.ExecuteScalarAsync();
        var hasUserTable = Convert.ToInt32(result) > 0;

        if (!hasUserTable)
        {
            await connection.CloseAsync();
            return;
        }

        // 创建迁移历史表
        await context.Database.ExecuteSqlRawAsync(@"
            CREATE TABLE __EFMigrationsHistory (
                MigrationId TEXT NOT NULL CONSTRAINT PK___EFMigrationsHistory PRIMARY KEY,
                ProductVersion TEXT NOT NULL
            )");

        // 获取所有已定义的迁移并标记为已应用
        var migrations = context.Database.GetMigrations().ToList();
        foreach (var migrationId in migrations)
        {
            await context.Database.ExecuteSqlRawAsync(
                "INSERT INTO __EFMigrationsHistory (MigrationId, ProductVersion) VALUES ({0}, {1})",
                migrationId,
                "10.0.0");
        }
    }

    /// <summary>
    /// 确保 CalendarEvents 表存在（用于日历模块）。
    /// 由于 CalendarEvent 实体新增较晚，可能未包含在已应用的迁移中，
    /// 此方法在已存在的数据库上手动创建表，避免 MigrateAsync 失败。
    /// </summary>
    /// <param name="context">数据库上下文</param>
    private static async Task EnsureCalendarEventsTableAsync(NoneadCSSDbContext context)
    {
        await using var connection = context.Database.GetDbConnection();
        await connection.OpenAsync();
        await using var checkCmd = connection.CreateCommand();
        checkCmd.CommandText = "SELECT COUNT(*) FROM sqlite_master WHERE type='table' AND name='CalendarEvents'";
        var exists = Convert.ToInt32(await checkCmd.ExecuteScalarAsync()) > 0;
        if (exists)
        {
            await connection.CloseAsync();
            return;
        }

        await using var createCmd = connection.CreateCommand();
        createCmd.CommandText = @"
            CREATE TABLE CalendarEvents (
                Id INTEGER NOT NULL CONSTRAINT PK_CalendarEvents PRIMARY KEY AUTOINCREMENT,
                Title TEXT NOT NULL,
                Description TEXT,
                EventType TEXT NOT NULL DEFAULT 'task',
                StartAt TEXT NOT NULL,
                EndAt TEXT,
                IsAllDay INTEGER NOT NULL DEFAULT 1,
                ConversationId INTEGER,
                MailboxId INTEGER,
                UserId INTEGER,
                Status TEXT NOT NULL DEFAULT 'pending',
                Color TEXT,
                CreatedAt TEXT NOT NULL,
                UpdatedAt TEXT NOT NULL,
                FOREIGN KEY (ConversationId) REFERENCES Conversations(Id),
                FOREIGN KEY (MailboxId) REFERENCES Mailboxes(Id),
                FOREIGN KEY (UserId) REFERENCES Users(Id)
            );
            CREATE INDEX IX_CalendarEvents_StartAt ON CalendarEvents (StartAt);
            CREATE INDEX IX_CalendarEvents_UserId ON CalendarEvents (UserId);
            CREATE INDEX IX_CalendarEvents_MailboxId ON CalendarEvents (MailboxId);
            CREATE INDEX IX_CalendarEvents_Status ON CalendarEvents (Status);
        ";
        await createCmd.ExecuteNonQueryAsync();
        await connection.CloseAsync();
    }

    /// <summary>
    /// TT-001: 确保 TimeEntries 表有活跃计时器唯一索引（数据库层兜底防御并发竞态）。
    /// 每个用户最多只能有一个 EndTime IS NULL 的记录（运行中的计时器）。
    /// 幂等：先清理重复活跃计时器，再用 CREATE UNIQUE INDEX IF NOT EXISTS。
    /// </summary>
    private static async Task EnsureTimeEntryActiveTimerIndexAsync(NoneadCSSDbContext context)
    {
        await using var connection = context.Database.GetDbConnection();
        await connection.OpenAsync();

        try
        {
            // 1) 清理重复活跃计时器：每个用户仅保留最新一条，其余软删除
            await using var cleanupCmd = connection.CreateCommand();
            cleanupCmd.CommandText = @"
                UPDATE TimeEntries
                SET ""DeletedAt"" = datetime('now'),
                    ""UpdatedAt"" = datetime('now')
                WHERE ""EndTime"" IS NULL
                  AND ""DeletedAt"" IS NULL
                  AND ""Id"" NOT IN (
                      SELECT MAX(t2.""Id"")
                      FROM TimeEntries t2
                      WHERE t2.""EndTime"" IS NULL
                        AND t2.""DeletedAt"" IS NULL
                      GROUP BY t2.""UserId""
                  );";
            await cleanupCmd.ExecuteNonQueryAsync();

            // 2) 创建唯一部分索引（幂等）
            await using var indexCmd = connection.CreateCommand();
            indexCmd.CommandText = @"
                CREATE UNIQUE INDEX IF NOT EXISTS IX_TimeEntries_UserId_EndTime_ActiveTimer
                ON TimeEntries (""UserId"", ""EndTime"")
                WHERE ""EndTime"" IS NULL AND ""DeletedAt"" IS NULL;";
            await indexCmd.ExecuteNonQueryAsync();
        }
        catch (Exception ex)
        {
            // 索引创建失败不阻塞启动（可能已有重复数据无法清理）
            Console.WriteLine($"[SEED] WARN: TimeEntry 活跃计时器唯一索引创建失败: {ex.Message}");
        }
        finally
        {
            await connection.CloseAsync();
        }
    }

    private static async Task EnsureKnowledgeTablesAsync(NoneadCSSDbContext context)
    {
        await using var connection = context.Database.GetDbConnection();
        await connection.OpenAsync();

        // 知识库分类表
        await using var checkCatCmd = connection.CreateCommand();
        checkCatCmd.CommandText = "SELECT COUNT(*) FROM sqlite_master WHERE type='table' AND name='KnowledgeCategories'";
        var catExists = Convert.ToInt32(await checkCatCmd.ExecuteScalarAsync()) > 0;
        if (!catExists)
        {
            await using var createCatCmd = connection.CreateCommand();
            createCatCmd.CommandText = @"
                CREATE TABLE KnowledgeCategories (
                    Id INTEGER NOT NULL CONSTRAINT PK_KnowledgeCategories PRIMARY KEY AUTOINCREMENT,
                    Name TEXT NOT NULL,
                    Description TEXT,
                    ParentId INTEGER,
                    MailboxId INTEGER,
                    SortOrder INTEGER NOT NULL DEFAULT 0,
                    Icon TEXT,
                    IsActive INTEGER NOT NULL DEFAULT 1,
                    CreatedAt TEXT NOT NULL,
                    UpdatedAt TEXT NOT NULL,
                    FOREIGN KEY (ParentId) REFERENCES KnowledgeCategories(Id),
                    FOREIGN KEY (MailboxId) REFERENCES Mailboxes(Id)
                );
                CREATE INDEX IX_KnowledgeCategories_ParentId ON KnowledgeCategories (ParentId);
                CREATE INDEX IX_KnowledgeCategories_MailboxId ON KnowledgeCategories (MailboxId);
            ";
            await createCatCmd.ExecuteNonQueryAsync();
        }

        // 知识库文章表
        await using var checkArtCmd = connection.CreateCommand();
        checkArtCmd.CommandText = "SELECT COUNT(*) FROM sqlite_master WHERE type='table' AND name='KnowledgeArticles'";
        var artExists = Convert.ToInt32(await checkArtCmd.ExecuteScalarAsync()) > 0;
        if (!artExists)
        {
            await using var createArtCmd = connection.CreateCommand();
            createArtCmd.CommandText = @"
                CREATE TABLE KnowledgeArticles (
                    Id INTEGER NOT NULL CONSTRAINT PK_KnowledgeArticles PRIMARY KEY AUTOINCREMENT,
                    Title TEXT NOT NULL,
                    Content TEXT NOT NULL DEFAULT '',
                    Summary TEXT,
                    CategoryId INTEGER,
                    AuthorId INTEGER,
                    Tags TEXT,
                    Status TEXT NOT NULL DEFAULT 'draft',
                    ViewCount INTEGER NOT NULL DEFAULT 0,
                    LikeCount INTEGER NOT NULL DEFAULT 0,
                    SortOrder INTEGER NOT NULL DEFAULT 0,
                    IsPinned INTEGER NOT NULL DEFAULT 0,
                    CreatedAt TEXT NOT NULL,
                    UpdatedAt TEXT NOT NULL,
                    PublishedAt TEXT,
                    FOREIGN KEY (CategoryId) REFERENCES KnowledgeCategories(Id),
                    FOREIGN KEY (AuthorId) REFERENCES Users(Id)
                );
                CREATE INDEX IX_KnowledgeArticles_CategoryId ON KnowledgeArticles (CategoryId);
                CREATE INDEX IX_KnowledgeArticles_Status ON KnowledgeArticles (Status);
                CREATE INDEX IX_KnowledgeArticles_AuthorId ON KnowledgeArticles (AuthorId);
            ";
            await createArtCmd.ExecuteNonQueryAsync();
        }

        await connection.CloseAsync();
    }

    /// <summary>
    /// 确保 RefreshTokens 表存在（用于 Refresh Token 轮换机制）。
    /// 若实体新增较晚且数据库已通过旧版本创建，则手动创建表。
    /// </summary>
    /// <param name="context">数据库上下文</param>
    private static async Task EnsureRefreshTokensTableAsync(NoneadCSSDbContext context)
    {
        await using var connection = context.Database.GetDbConnection();
        await connection.OpenAsync();
        await using var checkCmd = connection.CreateCommand();
        checkCmd.CommandText = "SELECT COUNT(*) FROM sqlite_master WHERE type='table' AND name='RefreshTokens'";
        var exists = Convert.ToInt32(await checkCmd.ExecuteScalarAsync()) > 0;
        if (exists)
        {
            await connection.CloseAsync();
            return;
        }

        await using var createCmd = connection.CreateCommand();
        createCmd.CommandText = @"
            CREATE TABLE RefreshTokens (
                Id INTEGER NOT NULL CONSTRAINT PK_RefreshTokens PRIMARY KEY AUTOINCREMENT,
                Token TEXT NOT NULL,
                ExpiresAt TEXT NOT NULL,
                CreatedAt TEXT NOT NULL,
                CreatedByIp TEXT NOT NULL,
                RevokedAt TEXT,
                ReplacedByToken TEXT,
                ReasonRevoked TEXT,
                UserId INTEGER NOT NULL,
                CONSTRAINT FK_RefreshTokens_Users_UserId FOREIGN KEY (UserId) REFERENCES Users(Id) ON DELETE CASCADE
            );
            CREATE UNIQUE INDEX IX_RefreshTokens_Token ON RefreshTokens (Token);
            CREATE INDEX IX_RefreshTokens_UserId ON RefreshTokens (UserId);
            CREATE INDEX IX_RefreshTokens_UserId_RevokedAt ON RefreshTokens (UserId, RevokedAt);
        ";
        await createCmd.ExecuteNonQueryAsync();
        await connection.CloseAsync();
    }

    private static async Task EnsureChatTablesAsync(NoneadCSSDbContext context)
    {
        await using var connection = context.Database.GetDbConnection();
        await connection.OpenAsync();

        await using var checkSessionCmd = connection.CreateCommand();
        checkSessionCmd.CommandText = "SELECT COUNT(*) FROM sqlite_master WHERE type='table' AND name='ChatSessions'";
        var sessionExists = Convert.ToInt32(await checkSessionCmd.ExecuteScalarAsync()) > 0;
        if (!sessionExists)
        {
            await using var createSessionCmd = connection.CreateCommand();
            createSessionCmd.CommandText = @"
                CREATE TABLE ChatSessions (
                    Id INTEGER NOT NULL CONSTRAINT PK_ChatSessions PRIMARY KEY AUTOINCREMENT,
                    SessionToken TEXT NOT NULL,
                    CustomerName TEXT NOT NULL,
                    CustomerEmail TEXT NOT NULL,
                    CustomerIp TEXT,
                    CustomerUserAgent TEXT,
                    CustomerId INTEGER,
                    MailboxId INTEGER,
                    UserId INTEGER,
                    ConversationId INTEGER,
                    Status TEXT NOT NULL DEFAULT 'waiting',
                    Subject TEXT,
                    CustomerLastSeenAt TEXT,
                    AgentLastSeenAt TEXT,
                    CustomerUnreadCount INTEGER NOT NULL DEFAULT 0,
                    AgentUnreadCount INTEGER NOT NULL DEFAULT 0,
                    Rating INTEGER,
                    RatingComment TEXT,
                    CreatedAt TEXT NOT NULL,
                    UpdatedAt TEXT NOT NULL,
                    ClosedAt TEXT,
                    FOREIGN KEY (CustomerId) REFERENCES Customers(Id),
                    FOREIGN KEY (MailboxId) REFERENCES Mailboxes(Id),
                    FOREIGN KEY (UserId) REFERENCES Users(Id),
                    FOREIGN KEY (ConversationId) REFERENCES Conversations(Id)
                );
                CREATE UNIQUE INDEX IX_ChatSessions_SessionToken ON ChatSessions (SessionToken);
                CREATE INDEX IX_ChatSessions_Status ON ChatSessions (Status);
                CREATE INDEX IX_ChatSessions_MailboxId ON ChatSessions (MailboxId);
                CREATE INDEX IX_ChatSessions_UserId ON ChatSessions (UserId);
                CREATE INDEX IX_ChatSessions_CustomerId ON ChatSessions (CustomerId);
                CREATE INDEX IX_ChatSessions_ConversationId ON ChatSessions (ConversationId);
                CREATE INDEX IX_ChatSessions_CreatedAt ON ChatSessions (CreatedAt);
            ";
            await createSessionCmd.ExecuteNonQueryAsync();
        }

        await using var checkMessageCmd = connection.CreateCommand();
        checkMessageCmd.CommandText = "SELECT COUNT(*) FROM sqlite_master WHERE type='table' AND name='ChatMessages'";
        var messageExists = Convert.ToInt32(await checkMessageCmd.ExecuteScalarAsync()) > 0;
        if (!messageExists)
        {
            await using var createMessageCmd = connection.CreateCommand();
            createMessageCmd.CommandText = @"
                CREATE TABLE ChatMessages (
                    Id INTEGER NOT NULL CONSTRAINT PK_ChatMessages PRIMARY KEY AUTOINCREMENT,
                    SessionId INTEGER NOT NULL,
                    SenderType TEXT NOT NULL DEFAULT 'customer',
                    SenderId INTEGER,
                    SenderName TEXT NOT NULL,
                    MessageType TEXT NOT NULL DEFAULT 'text',
                    Content TEXT NOT NULL,
                    AttachmentUrl TEXT,
                    AttachmentName TEXT,
                    IsRead INTEGER NOT NULL DEFAULT 0,
                    CreatedAt TEXT NOT NULL,
                    FOREIGN KEY (SessionId) REFERENCES ChatSessions(Id) ON DELETE CASCADE
                );
                CREATE INDEX IX_ChatMessages_SessionId ON ChatMessages (SessionId);
                CREATE INDEX IX_ChatMessages_SenderType ON ChatMessages (SenderType);
                CREATE INDEX IX_ChatMessages_IsRead ON ChatMessages (IsRead);
                CREATE INDEX IX_ChatMessages_CreatedAt ON ChatMessages (CreatedAt);
            ";
            await createMessageCmd.ExecuteNonQueryAsync();
        }

        await connection.CloseAsync();
    }
}
