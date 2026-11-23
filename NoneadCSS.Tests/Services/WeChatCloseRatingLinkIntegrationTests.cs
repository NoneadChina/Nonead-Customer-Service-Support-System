using FluentAssertions;
using Microsoft.AspNetCore.Http;
using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using Moq;
using NoneadCSS.Api.Seeders;
using NoneadCSS.Application.Configuration;
using NoneadCSS.Application.DTOs.Responses;
using NoneadCSS.Application.Services;
using NoneadCSS.Application.Services.WorkflowActions;
using NoneadCSS.Application.Templates;
using NoneadCSS.Domain.Entities;
using NoneadCSS.Domain.Enums;
using NoneadCSS.Domain.Events;
using NoneadCSS.Infrastructure;
using NoneadCSS.Infrastructure.Persistence;
using NoneadCSS.Infrastructure.Services;
using Xunit;

namespace NoneadCSS.Tests.Services;

/// <summary>
/// T000109（关闭微信会话-评价链接无法打开）端到端集成测试。
///
/// 与 <see cref="FrontendUrlProductionIntegrationTests"/>（配置/解析器/token 层面）互补：
/// 本测试走真实链路 —— 真实 SQLite + 真实 im-conversation-closed 模板 + 真实
/// SendImMessageActionHandler（send_im_message 动作）+ 真实 TemplateRenderer 渲染，
/// 通过 mock IImMessageQueueService 捕获最终入队的消息文本（即客户微信里实际收到的那条），
/// 断言其中的满意度评价链接前缀为公网域名 http://css.example.com:5173（不含 corp）。
/// </summary>
public class WeChatCloseRatingLinkIntegrationTests : IAsyncLifetime
{
    private const string RequiredFrontendUrl = "http://css.example.com:5173";

    private SqliteConnection _connection = null!;
    private NoneadCSSDbContext _context = null!;
    private UnitOfWork _unitOfWork = null!;
    private int _mailboxId;

    private IConfiguration _config = null!;
    private Mock<IImMessageQueueService> _imQueueMock = null!;
    private SendImMessageActionHandler _handler = null!;
    private WorkflowService _workflowService = null!;
    private SatisfactionTokenGenerator _tokenGenerator = null!;
    private MailTemplateService _mailTemplateService = null!;
    private FrontendUrlResolver _frontendUrlResolver = null!;
    private IOptions<BrandOptions> _brandOptions = null!;

    private static string ApiProjectDir => Path.GetFullPath(Path.Combine(AppContext.BaseDirectory, "..", "..", "..", "..", "NoneadCSS.Api"));

    private static IConfiguration BuildComposedProductionConfig()
    {
        return new ConfigurationBuilder()
            .SetBasePath(ApiProjectDir)
            .AddJsonFile("appsettings.json", optional: false)
            .AddJsonFile("appsettings.Production.json", optional: false)
            .Build();
    }

    public async Task InitializeAsync()
    {
        _config = BuildComposedProductionConfig();

        // 真实 SQLite
        _connection = new SqliteConnection("DataSource=:memory:");
        await _connection.OpenAsync();
        var options = new DbContextOptionsBuilder<NoneadCSSDbContext>()
            .UseSqlite(_connection)
            .ConfigureWarnings(w => w.Ignore(Microsoft.EntityFrameworkCore.Diagnostics.RelationalEventId.MultipleCollectionIncludeWarning))
            .Options;
        _context = new NoneadCSSDbContext(options);
        await _context.Database.EnsureCreatedAsync();
        _unitOfWork = new UnitOfWork(_context, NoneadCSS.Tests.Shared.TestServiceProviderBuilder.Build(_context));

        _mailboxId = await InsertMailboxRowAsync();

        // 客户（微信渠道：无邮箱，仅 ChannelId=openId —— 触发 BuildImVariablesAsync 的 ChannelId 双路径）
        var customer = new Customer
        {
            FirstName = "微信客户",
            LastName = string.Empty,
            Channel = "WeChat",
            ChannelId = "oXyZ_test_openid_1234567890"
        };
        await _unitOfWork.Customers.AddAsync(customer);
        await _unitOfWork.SaveChangesAsync();

        // 微信会话（SourceType=WeChat，CustomerEmail 为空）。
        // Conversations.Version 为 int 并发令牌（ValueGeneratedOnAddOrUpdate），EF INSERT 时省略该列，
        // 而 EnsureCreated 的 schema 无 DEFAULT → NOT NULL 失败，故同 Mailboxes 一样用原生 SQL 插入。
        var conversationId = await InsertConversationRowAsync(_mailboxId, customer.Id);
        ConversationId = conversationId;

        // im-conversation-closed 模板（与 DatabaseSeeder 中系统模板同款 BodyText，含满意度评价链接占位符）
        await _unitOfWork.MailTemplates.AddAsync(new MailTemplate
        {
            Name = "IM 渠道会话关闭通知",
            Slug = "im-conversation-closed",
            Type = MailTemplateType.ConversationClosed,
            Subject = "会话已关闭",
            BodyHtml = string.Empty,
            BodyText = "您好 {{customer_name}}，您的问题已处理完毕，本次会话已关闭。感谢您选择 {{company_name}}！\n\n我们非常重视您的反馈，请点击下方链接评价本次服务：\n{{satisfaction_url}}\n\n如后续有其他问题，欢迎随时联系我们。",
            IsActive = true,
            IsSystem = true,
            SortOrder = 5,
            CreatedAt = DateTime.UtcNow,
            UpdatedAt = DateTime.UtcNow
        });
        await _unitOfWork.SaveChangesAsync();

        // ===== 真实组件链 =====
        _brandOptions = Options.Create(new BrandOptions
        {
            CompanyName = "拓德科技",
            FrontendUrl = RequiredFrontendUrl
        });

        var currentUserMock = new Mock<ICurrentUserAccessor>();
        currentUserMock.Setup(c => c.CanAccessMailboxAsync(It.IsAny<int?>())).ReturnsAsync(true);

        _mailTemplateService = new MailTemplateService(
            _unitOfWork,
            currentUserMock.Object,
            new TemplateRenderer(),
            _brandOptions,
            _config);

        var envMock = new Mock<IHostEnvironment>();
        envMock.SetupGet(e => e.EnvironmentName).Returns(Environments.Development); // 自动生成 ≥32B 随机密钥
        _tokenGenerator = new SatisfactionTokenGenerator(
            _config,
            envMock.Object,
            new Mock<ILogger<SatisfactionTokenGenerator>>().Object);

        _frontendUrlResolver = new FrontendUrlResolver(new HttpContextAccessor(), _config);

        var chatChannelMock = new Mock<IChatChannelMessageSender>();
        chatChannelMock.Setup(c => c.SupportsChatChannel(It.IsAny<Conversation>())).Returns(true);

        _imQueueMock = new Mock<IImMessageQueueService>();
        _imQueueMock.Setup(q => q.EnqueueAsync(It.IsAny<Conversation>(), It.IsAny<string>()))
            .ReturnsAsync(1);

        _handler = new SendImMessageActionHandler(
            _unitOfWork,
            _mailTemplateService,
            _imQueueMock.Object,
            chatChannelMock.Object,
            _tokenGenerator,
            _frontendUrlResolver,
            _config,
            new Mock<ILogger<SendImMessageActionHandler>>().Object,
            _brandOptions);

        // ===== 真实工作流引擎（引擎层）=====
        // 只注册 send_im_message 一个处理器；folder/通知/事件分发用 mock；
        // serviceProvider 仅用于填充 WorkflowActionContext.ServiceProvider（send_im_message 处理器不消费）。
        var handlerProvider = new WorkflowActionHandlerProvider(
            new IWorkflowActionHandler[] { _handler },
            new Mock<ILogger<WorkflowActionHandlerProvider>>().Object);

        _workflowService = new WorkflowService(
            _unitOfWork,
            new Mock<ILogger<WorkflowService>>().Object,
            new Mock<IFolderService>().Object,
            new Mock<INotificationPublisher>().Object,
            handlerProvider,
            new Mock<IServiceProvider>().Object,
            _brandOptions);
    }

    public int ConversationId { get; private set; }

    public Task DisposeAsync()
    {
        _connection.Dispose();
        return Task.CompletedTask;
    }

    [Fact]
    public async Task WeChatCloseImMessage_RendersSatisfactionUrl_WithRequiredPublicPrefix()
    {
        var conversation = await _unitOfWork.Conversations.GetByIdAsync(ConversationId);
        conversation.Should().NotBeNull();

        var action = new WorkflowAction { Type = "send_im_message", Value = "im-conversation-closed" };
        var context = new WorkflowActionContext();

        string? sentText = null;
        _imQueueMock.Setup(q => q.EnqueueAsync(It.IsAny<Conversation>(), It.IsAny<string>()))
            .Callback<Conversation, string>((_, text) => sentText = text)
            .ReturnsAsync(1);

        var executed = await _handler.ExecuteAsync(action, conversation!, context);

        executed.Should().BeTrue("send_im_message 动作应成功入队（微信客户实际收到该消息）");
        sentText.Should().NotBeNullOrWhiteSpace();

        sentText!.Should().Contain(RequiredFrontendUrl,
            "微信客户收到的关闭通知必须包含公网域名前缀的评价链接（T000109）");
        sentText.Should().MatchRegex(@"http://css\.example\.com:5173/satisfaction/[^/\s]+",
            "评价链接形态应为 {前缀}/satisfaction/{token}");
        sentText.Should().NotContain("corp", "外发链接不能使用内网 corp 域名");
        sentText.Should().NotContain("{{", "模板占位符必须全部渲染，不能把 {{satisfaction_url}} 字面量发给客户");

        // 提取链接中的 token 并验证：评分页可正常打开/提交
        var urlMatch = System.Text.RegularExpressions.Regex.Match(sentText!, @"http://css\.example\.com:5173/satisfaction/([^/\s]+)");
        urlMatch.Success.Should().BeTrue();
        var token = urlMatch.Groups[1].Value;
        _tokenGenerator.ValidateTokenByChannelId(token, ConversationId, "oXyZ_test_openid_1234567890").Should().BeTrue(
            "链接中的 token 必须能被后端验证，评分页才可正常打开（'链接无法打开'的直接体现）");
    }

    /// <summary>
    /// 引擎级端到端测试（第三次"请为刚才的修复生成对应的集成测试用例"所指向的补强）：
    /// 走真实工作流引擎 —— 真实 WorkflowService.CreateAsync 创建 conversation_closed 规则 →
    /// 真实 ExecuteForConversationAsync 执行（条件评估 → handler 分发 → send_im_message 动作）
    /// → 断言微信客户实际入队的关闭通知文本中的评价链接为公网前缀且 token 可验证。
    /// </summary>
    [Fact]
    public async Task ConversationClosed_WeChatWorkflow_EngineSendsMessageWithPublicSatisfactionUrl()
    {
        // 引擎配置层：通过真实 CreateAsync 落库一条会话关闭触发器的工作流
        var created = await _workflowService.CreateAsync(new CreateWorkflowRequest
        {
            Name = "[IT] 微信会话关闭满意度通知",
            Description = "T000109 集成测试-引擎层",
            MailboxId = _mailboxId,
            IsActive = true,
            SortOrder = 1,
            TriggerType = WorkflowConst.TriggerConversationClosed,
            Actions = new List<WorkflowActionDto>
            {
                new() { Type = WorkflowConst.ActionSendImMessage, Value = "im-conversation-closed" }
            }
        });
        created.Id.Should().BeGreaterThan(0, "CreateAsync 应落库并返回自增 Id");

        string? sentText = null;
        _imQueueMock.Setup(q => q.EnqueueAsync(It.IsAny<Conversation>(), It.IsAny<string>()))
            .Callback<Conversation, string>((_, text) => sentText = text)
            .ReturnsAsync(1);

        // 引擎执行层：触发会话关闭（模拟真实服务台"关闭微信会话"后的工作流分发）
        var executed = await _workflowService.ExecuteForConversationAsync(
            ConversationId, WorkflowConst.TriggerConversationClosed);

        executed.Should().Be(1, "conversation_closed 应恰好命中 1 条已启用工作流并成功入队");

        sentText.Should().NotBeNullOrWhiteSpace("工作流引擎执行后，微信客户必须实际收到关闭通知消息");
        sentText!.Should().Contain(RequiredFrontendUrl,
            "引擎渲染出的关闭通知必须包含公网域名前缀的评价链接（T000109）");
        sentText.Should().MatchRegex(@"http://css\.example\.com:5173/satisfaction/[^/\s]+",
            "评价链接形态应为 {前缀}/satisfaction/{token}");
        sentText.Should().NotContain("corp", "外发链接不能使用内网 corp 域名");
        sentText.Should().NotContain("{{", "模板占位符必须全部渲染，不能把 {{satisfaction_url}} 字面量发给客户");

        // token 可验证：评分页可正常打开/提交（'链接无法打开'的直接体现）
        var urlMatch = System.Text.RegularExpressions.Regex.Match(sentText!, @"http://css\.example\.com:5173/satisfaction/([^/\s]+)");
        urlMatch.Success.Should().BeTrue();
        _tokenGenerator.ValidateTokenByChannelId(urlMatch.Groups[1].Value, ConversationId, "oXyZ_test_openid_1234567890").Should().BeTrue(
            "引擎链路产出的 token 必须能被后端验证");
    }

    /// <summary>
    /// 引擎层负面用例：工作流停用后，同样触发不应再发送 IM 消息。
    /// 防止"关不掉/停用后仍发通知"的回归（引擎级守卫）。
    /// </summary>
    [Fact]
    public async Task ConversationClosed_InactiveWorkflow_DoesNotEnqueueMessage()
    {
        var created = await _workflowService.CreateAsync(new CreateWorkflowRequest
        {
            Name = "[IT] 微信会话关闭满意度通知-停用",
            MailboxId = _mailboxId,
            IsActive = false, // 停用状态
            SortOrder = 2,
            TriggerType = WorkflowConst.TriggerConversationClosed,
            Actions = new List<WorkflowActionDto>
            {
                new() { Type = WorkflowConst.ActionSendImMessage, Value = "im-conversation-closed" }
            }
        });
        created.Id.Should().BeGreaterThan(0);

        string? sentText = null;
        _imQueueMock.Setup(q => q.EnqueueAsync(It.IsAny<Conversation>(), It.IsAny<string>()))
            .Callback<Conversation, string>((_, text) => sentText = text)
            .ReturnsAsync(1);

        var executed = await _workflowService.ExecuteForConversationAsync(
            ConversationId, WorkflowConst.TriggerConversationClosed);

        executed.Should().Be(0, "停用的工作流不应命中执行");
        sentText.Should().BeNull("停用工作流不得向微信客户入队任何消息");
    }

    // ==================== 生产种子（WorkflowSeeder）级集成测试 ====================
    // 上述手动 CreateAsync 用例覆盖引擎执行，以下用例覆盖生产环境的真实载体：
    // WorkflowSeeder 落库的 [IM] 会话关闭满意度通知（source_type 条件 + send_im_message 动作）。
    // 这是 T000109 修复真正在产线运行的配置形态。

    [Fact]
    public async Task WorkflowSeeder_SeedsImConversationClosed_WithChannelConditionAndImAction()
    {
        await WorkflowSeeder.SeedAsync(_context);

        var imWorkflow = _context.Workflows.FirstOrDefault(w => w.Name == "[IM] 会话关闭满意度通知");
        imWorkflow.Should().NotBeNull("生产种子必须存在 [IM] 会话关闭满意度通知 系统工作流（T000109 修复载体）");
        imWorkflow!.TriggerType.Should().Be(WorkflowConst.TriggerConversationClosed,
            "IM 满意度通知在会话关闭时触发");
        imWorkflow.IsSystem.Should().BeTrue("系统内置工作流不可被误删");
        imWorkflow.IsActive.Should().BeTrue("默认启用，否则微信客户收不到关闭通知");
        imWorkflow.MailboxId.Should().BeNull("系统工作流对所有邮箱生效");
        imWorkflow.ActionsJson.Should().Contain(WorkflowConst.ActionSendImMessage)
            .And.Contain("im-conversation-closed",
                "动作必须为 send_im_message + im-conversation-closed 模板（微信客户实际收到的消息）");
        imWorkflow.ConditionsJson.Should().Contain("\"matchType\":\"any\"",
            "matchType=any 实现 source_type IN (...) 语义");
        foreach (var channel in new[] { "WeChat", "Feishu", "WhatsApp", "DingTalk" })
        {
            imWorkflow.ConditionsJson.Should().Contain($"\"value\":\"{channel}\"",
                $"IM 满意度通知应覆盖 {channel} 渠道");
        }

        // 渠道隔离回归：邮件关闭通知工作流必须限定 source_type equals Email，
        // 否则微信会话关闭时邮件工作流也会命中，客户收到重复通知。
        var emailWorkflow = _context.Workflows.FirstOrDefault(w => w.Name == "[邮件] 会话关闭通知客户");
        emailWorkflow.Should().NotBeNull();
        emailWorkflow!.ConditionsJson.Should().Contain("\"field\":\"source_type\"")
            .And.Contain("\"operator\":\"equals\"")
            .And.Contain("\"value\":\"Email\"");
    }

    [Fact]
    public async Task SeededImWorkflow_EngineExecuteOnWeChatConversation_SendsPublicSatisfactionUrl()
    {
        await WorkflowSeeder.SeedAsync(_context);

        string? sentText = null;
        _imQueueMock.Setup(q => q.EnqueueAsync(It.IsAny<Conversation>(), It.IsAny<string>()))
            .Callback<Conversation, string>((_, text) => sentText = text)
            .ReturnsAsync(1);

        var executed = await _workflowService.ExecuteForConversationAsync(
            ConversationId, WorkflowConst.TriggerConversationClosed);

        executed.Should().Be(1,
            "微信会话关闭应命中生产种子 [IM] 工作流 1 条（[邮件] 工作流因 source_type 条件互斥不命中）");
        sentText.Should().NotBeNullOrWhiteSpace("微信客户必须收到生产种子工作流渲染的关闭通知");
        sentText!.Should().Contain(RequiredFrontendUrl,
            "生产种子工作流产出的链接必须是公网域名前缀（T000109）");
        sentText.Should().MatchRegex(@"http://css\.example\.com:5173/satisfaction/[^/\s]+");
        sentText.Should().NotContain("corp", "外发链接不能使用内网 corp 域名");
        sentText.Should().NotContain("{{", "模板占位符必须全部渲染");

        var urlMatch = System.Text.RegularExpressions.Regex.Match(sentText!, @"http://css\.example\.com:5173/satisfaction/([^/\s]+)");
        urlMatch.Success.Should().BeTrue();
        _tokenGenerator.ValidateTokenByChannelId(urlMatch.Groups[1].Value, ConversationId, "oXyZ_test_openid_1234567890").Should().BeTrue(
            "生产种子工作流产出的 token 必须能被后端验证，评分页才能正常打开");
    }

    [Fact]
    public async Task SeededImWorkflow_EngineExecuteOnEmailConversation_DoesNotEnqueueImMessage()
    {
        await WorkflowSeeder.SeedAsync(_context);

        // 邮件渠道会话：含邮箱、无 ChannelId —— 模拟纯邮件来源（与微信会话形成渠道对照）
        var emailCustomer = new Customer
        {
            FirstName = "邮件客户",
            LastName = string.Empty,
            Channel = "Email",
            ChannelId = string.Empty
        };
        await _unitOfWork.Customers.AddAsync(emailCustomer);
        await _unitOfWork.SaveChangesAsync();

        var emailConversationId = await InsertConversationRowAsync(
            _mailboxId, emailCustomer.Id,
            SourceType.Email, "customer@example.com", "打印机报错（邮件）", number: 9002);

        string? sentText = null;
        _imQueueMock.Setup(q => q.EnqueueAsync(It.IsAny<Conversation>(), It.IsAny<string>()))
            .Callback<Conversation, string>((_, text) => sentText = text)
            .ReturnsAsync(1);

        var executed = await _workflowService.ExecuteForConversationAsync(
            emailConversationId, WorkflowConst.TriggerConversationClosed);

        executed.Should().Be(0,
            "邮件会话关闭不得命中 [IM] 满意度通知工作流（source_type 条件隔离，避免跨渠道重复通知）");
        sentText.Should().BeNull("邮件渠道会话不得触发任何 IM 消息入队");
    }

    // ==================== T000110：客户二次问答不应收到自己邮件副本 ====================
    // 根因：旧版 [客户回复确认邮件] 工作流在 new_reply 时用 customer-reply-default 模板
    // （正文渲染 {{message_body}}=客户刚发的最新消息）把邮件回发给客户本人。
    // 修复：该工作流默认停用；客户二次问答仅由 [邮件] 客户新回复通知（extra=assignee）通知客服。

    [Fact]
    public async Task WorkflowSeeder_SeedsCustomerReplyConfirm_InactiveByDefault_T000110()
    {
        await WorkflowSeeder.SeedAsync(_context);

        var confirmWorkflow = await _context.Workflows.FirstOrDefaultAsync(w => w.Name == "客户回复确认邮件");
        confirmWorkflow.Should().NotBeNull("新装库应种子出 客户回复确认邮件 工作流");
        confirmWorkflow!.IsActive.Should().BeFalse("新装库默认停用：客户二次问答时不得回发其本人消息副本（T000110）");

        // 对照：客服通知工作流必须保持启用，预期行为才成立
        var notifyWorkflow = await _context.Workflows.FirstOrDefaultAsync(w => w.Name == "[邮件] 客户新回复通知");
        notifyWorkflow.Should().NotBeNull();
        notifyWorkflow!.IsActive.Should().BeTrue("客户二次问答时，客服新回复通知工作流必须保持启用");
        notifyWorkflow.ActionsJson.Should().Contain("\"extra\":\"assignee\"",
            "客服通知邮件应发给分配客服（assignee），而非客户");
    }

    [Fact]
    public async Task WorkflowSeeder_MigratesLegacyActiveReplyConfirmToInactive_T000110()
    {
        // 模拟旧版生产库：已存在"启用态"的 客户回复确认邮件（升级路径）
        await _unitOfWork.Workflows.AddAsync(new Workflow
        {
            Name = "客户回复确认邮件",
            Description = "旧版：收到客户回复时向客户发送确认邮件",
            MailboxId = null,
            IsActive = true,
            SortOrder = 12,
            TriggerType = WorkflowConst.TriggerNewReply,
            ConditionsJson = """{"matchType":"all","conditions":[]}""",
            ActionsJson = """[{"type":"send_email","value":"customer-reply-default","extra":"customer"}]""",
            CreatedAt = DateTime.UtcNow.AddDays(-1),
            UpdatedAt = DateTime.UtcNow.AddDays(-1)
        });
        await _unitOfWork.SaveChangesAsync();

        await WorkflowSeeder.SeedAsync(_context);

        var migrated = await _context.Workflows.FirstOrDefaultAsync(w => w.Name == "客户回复确认邮件");
        migrated.Should().NotBeNull();
        migrated!.IsActive.Should().BeFalse("生产库中已启用的 客户回复确认邮件 必须被迁移为停用（T000110）");
    }

    [Fact]
    public async Task NewReply_EmailConversation_CustomerGetsNoEchoButAssigneeGetsNotification()
    {
        // 生产种子：客户回复确认邮件 已被禁用（T000110 修复）
        await WorkflowSeeder.SeedAsync(_context);

        // new-reply 模板（DatabaseSeeder 同款，发送给客服的通知邮件）
        await _unitOfWork.MailTemplates.AddAsync(new MailTemplate
        {
            Name = "客户新回复通知",
            Slug = "new-reply",
            Type = MailTemplateType.NewReply,
            Subject = "[新回复] {{subject}}",
            BodyHtml = "<p>您好 {{user_name}}，您跟进的会话收到了新回复：{{customer_name}} 回复道：{{message_body}}</p>",
            BodyText = "您好 {{user_name}}，您跟进的会话收到了新回复：{{customer_name}} 回复道：{{message_body}}",
            IsActive = true,
            IsSystem = true,
            SortOrder = 2,
            CreatedAt = DateTime.UtcNow,
            UpdatedAt = DateTime.UtcNow
        });
        await _unitOfWork.SaveChangesAsync();

        // 客服（分配人）
        var agent = new User { FirstName = "客服", LastName = "小拓", Email = "agent.t000110@example.com" };
        await _unitOfWork.Users.AddAsync(agent);
        await _unitOfWork.SaveChangesAsync();

        // 邮件会话：客户 + 分配客服（客户二次问答场景）
        var emailCustomer = new Customer { FirstName = "邮件客户", LastName = "二次问答", Channel = "Email", ChannelId = string.Empty };
        await _unitOfWork.Customers.AddAsync(emailCustomer);
        await _unitOfWork.SaveChangesAsync();

        var conversationId = await InsertConversationRowAsync(
            _mailboxId, emailCustomer.Id,
            SourceType.Email, "customer.t000110@example.com", "请问打印机如何重置？", number: 9101, userId: agent.Id);

        // 真实 send_email 处理器 + mock 邮件队列（捕获发给谁）
        var emailQueueMock = new Mock<IEmailQueueService>();
        emailQueueMock.Setup(q => q.EnqueueEmailAsync(
                It.IsAny<int>(), It.IsAny<string>(), It.IsAny<string>(), It.IsAny<string>(),
                It.IsAny<List<int>?>(), It.IsAny<string?>(), It.IsAny<string?>(), It.IsAny<int?>(),
                It.IsAny<string?>(), It.IsAny<string?>(), It.IsAny<int>(), It.IsAny<int?>(), It.IsAny<int?>()))
            .Returns(Task.CompletedTask);
        var sendEmailHandler = new SendEmailActionHandler(
            _unitOfWork, _mailTemplateService, emailQueueMock.Object, _config, _tokenGenerator, _frontendUrlResolver,
            new Mock<ILogger<SendEmailActionHandler>>().Object, _brandOptions);

        // 专用引擎：同时注册 send_email + send_im_message（贴近生产完整 handler 集）
        var provider = new WorkflowActionHandlerProvider(
            new IWorkflowActionHandler[] { sendEmailHandler, _handler },
            new Mock<ILogger<WorkflowActionHandlerProvider>>().Object);
        var workflowService = new WorkflowService(
            _unitOfWork,
            new Mock<ILogger<WorkflowService>>().Object,
            new Mock<IFolderService>().Object,
            new Mock<INotificationPublisher>().Object,
            provider,
            new Mock<IServiceProvider>().Object,
            _brandOptions);

        // 触发客户二次问答（new_reply）
        var executed = await workflowService.ExecuteForConversationAsync(conversationId, WorkflowConst.TriggerNewReply);

        executed.Should().Be(1,
            "客户二次问答应恰好命中 [邮件] 客户新回复通知 1 条（客户回复确认邮件已禁用，不参与执行）");

        // 客服收到新回复通知邮件
        emailQueueMock.Verify(q => q.EnqueueEmailAsync(
                It.IsAny<int>(), "agent.t000110@example.com", It.IsAny<string>(), It.IsAny<string>(),
                It.IsAny<List<int>?>(), It.IsAny<string?>(), It.IsAny<string?>(), It.IsAny<int?>(),
                It.IsAny<string?>(), It.IsAny<string?>(), It.IsAny<int>(), It.IsAny<int?>(), It.IsAny<int?>()),
            Times.AtLeastOnce, "客服必须收到客户二次问答的新回复通知邮件（预期行为）");

        // 客户不再收到自己消息的副本
        emailQueueMock.Verify(q => q.EnqueueEmailAsync(
                It.IsAny<int>(), "customer.t000110@example.com", It.IsAny<string>(), It.IsAny<string>(),
                It.IsAny<List<int>?>(), It.IsAny<string?>(), It.IsAny<string?>(), It.IsAny<int?>(),
                It.IsAny<string?>(), It.IsAny<string?>(), It.IsAny<int>(), It.IsAny<int?>(), It.IsAny<int?>()),
            Times.Never, "客户不得收到自己发送的二次邮件副本（T000110）");
    }

    // ==================== T000111：关闭邮件会话-评价链接 must 可打开 ====================
    // 根因：邮件渠道满意度评价链接前缀曾为内网/本机地址（LAN-IP:5173、localhost:5173）或空 href，
    // 外部客户点击无法打开（邮件客户端显示 about:blank）。
    // 修复：App:FrontendUrl 统一公网前缀（T000109）+ 本文件三处加固
    //   (a) SendEmailActionHandler.BuildMailVariablesAsync 回退 Customer.GetMainEmail()
    //   (b) SendEmailActionHandler.ResolveRecipientsAsync(customer) 回退主邮箱
    //   (c) ConversationStatusService.SendSatisfactionSurveyAsync 无邮箱时跳过
    // 以下用例锁定：关闭邮件会话 → 发送的评价邮件链接 = 公网前缀 + 可验证 token，绝不产生空/内网链接。

    [Fact]
    public async Task ConversationClosed_EmailWorkflow_SendsSatisfactionLink_WithPublicPrefixAndValidToken()
    {
        await WorkflowSeeder.SeedAsync(_context);
        await InsertConversationClosedTemplateAsync();

        // 邮件会话（CustomerEmail 非空 —— 正常关闭场景）
        var emailCustomer = new Customer { FirstName = "邮件客户", LastName = "关闭评价", Channel = "Email", ChannelId = string.Empty };
        await _unitOfWork.Customers.AddAsync(emailCustomer);
        await _unitOfWork.SaveChangesAsync();

        var conversationId = await InsertConversationRowAsync(
            _mailboxId, emailCustomer.Id,
            SourceType.Email, "customer.t000111@example.com", "打印机报错-关闭会话", number: 9201);

        string? sentTo = null;
        string? sentBody = null;
        var emailQueueMock = new Mock<IEmailQueueService>();
        emailQueueMock.Setup(q => q.EnqueueEmailAsync(
                It.IsAny<int>(), It.IsAny<string>(), It.IsAny<string>(), It.IsAny<string>(),
                It.IsAny<List<int>?>(), It.IsAny<string?>(), It.IsAny<string?>(), It.IsAny<int?>(),
                It.IsAny<string?>(), It.IsAny<string?>(), It.IsAny<int>(), It.IsAny<int?>(), It.IsAny<int?>()))
            .Callback<int, string, string, string, List<int>?, string?, string?, int?, string?, string?, int, int?, int?>((_, to, _, body, _, _, _, _, _, _, _, _, _) => { sentTo = to; sentBody = body; })
            .Returns(Task.CompletedTask);
        var sendEmailHandler = new SendEmailActionHandler(
            _unitOfWork, _mailTemplateService, emailQueueMock.Object, _config, _tokenGenerator, _frontendUrlResolver,
            new Mock<ILogger<SendEmailActionHandler>>().Object, _brandOptions);
        var engine = BuildEngineWithHandlers(new IWorkflowActionHandler[] { sendEmailHandler, _handler });

        var executed = await engine.ExecuteForConversationAsync(conversationId, WorkflowConst.TriggerConversationClosed);

        executed.Should().Be(1,
            "邮件会话关闭应命中 [邮件] 会话关闭通知客户 1 条（[IM] 因 source_type 条件不命中）");
        sentTo.Should().Be("customer.t000111@example.com", "关闭通知应发给客户邮箱");
        sentBody.Should().NotBeNullOrWhiteSpace("客户必须收到包含评价链接的关闭邮件");
        sentBody!.Should().Contain($"href=\"{RequiredFrontendUrl}/satisfaction/",
            "邮件中的评价链接必须为公网域名前缀（T000111）");
        sentBody.Should().MatchRegex(@"href=""http://css\.example\.com:5173/satisfaction/[^""\s]+""",
            "评价链接形态应为 href=\"{前缀}/satisfaction/{token}\"");
        sentBody.Should().NotContain("corp").And.NotContain("localhost").And.NotContain("192.168")
            .And.NotContain("about:blank").And.NotContain("{{",
            "不能出现内网/本机前缀、空链接或未渲染占位符");

        var m = System.Text.RegularExpressions.Regex.Match(sentBody!, @"href=""http://css\.example\.com:5173/satisfaction/([^""\s]+)""");
        m.Success.Should().BeTrue();
        _tokenGenerator.ValidateToken(m.Groups[1].Value, conversationId, "customer.t000111@example.com").Should().BeTrue(
            "邮件中的评价 token 必须能被后端验证，客户点击后才能正常打开评分页（T000111）");
    }

    [Fact]
    public async Task ConversationClosed_EmailWithoutDenormalizedEmail_FallsBackToCustomerMainEmail()
    {
        await WorkflowSeeder.SeedAsync(_context);
        await InsertConversationClosedTemplateAsync();

        // 客户邮箱仅存在于 Customer.Emails 集合，会话反规范化列 CustomerEmail 为空
        // → 加固 (a)(b)：评价链接用主邮箱生成 token，收件人回退主邮箱，客户仍能收到可打开的评价邮件
        var emailCustomer = new Customer
        {
            FirstName = "邮件客户",
            LastName = "仅主邮箱",
            Channel = "Email",
            ChannelId = string.Empty,
            Emails = new List<Email> { new() { EmailAddress = "main.t000111@example.com", Type = 1 } }
        };
        await _unitOfWork.Customers.AddAsync(emailCustomer);
        await _unitOfWork.SaveChangesAsync();

        var conversationId = await InsertConversationRowAsync(
            _mailboxId, emailCustomer.Id,
            SourceType.Email, string.Empty, "打印机报错-主邮箱场景", number: 9202);

        string? sentTo = null;
        string? sentBody = null;
        var emailQueueMock = new Mock<IEmailQueueService>();
        emailQueueMock.Setup(q => q.EnqueueEmailAsync(
                It.IsAny<int>(), It.IsAny<string>(), It.IsAny<string>(), It.IsAny<string>(),
                It.IsAny<List<int>?>(), It.IsAny<string?>(), It.IsAny<string?>(), It.IsAny<int?>(),
                It.IsAny<string?>(), It.IsAny<string?>(), It.IsAny<int>(), It.IsAny<int?>(), It.IsAny<int?>()))
            .Callback<int, string, string, string, List<int>?, string?, string?, int?, string?, string?, int, int?, int?>((_, to, _, body, _, _, _, _, _, _, _, _, _) => { sentTo = to; sentBody = body; })
            .Returns(Task.CompletedTask);
        var sendEmailHandler = new SendEmailActionHandler(
            _unitOfWork, _mailTemplateService, emailQueueMock.Object, _config, _tokenGenerator, _frontendUrlResolver,
            new Mock<ILogger<SendEmailActionHandler>>().Object, _brandOptions);
        var engine = BuildEngineWithHandlers(new IWorkflowActionHandler[] { sendEmailHandler, _handler });

        var executed = await engine.ExecuteForConversationAsync(conversationId, WorkflowConst.TriggerConversationClosed);

        executed.Should().Be(1, "即使会话列 CustomerEmail 为空，关闭工作流仍应成功送达");
        sentTo.Should().Be("main.t000111@example.com", "收件人应回退到 Customer.Emails 主邮箱（加固 (b)）");
        sentBody.Should().Contain($"href=\"{RequiredFrontendUrl}/satisfaction/",
            "评价链接必须存在且为公网前缀（加固 (a)：用主邮箱生成 token）");
        sentBody.Should().NotContain("about:blank").And.NotContain("{{");

        var m = System.Text.RegularExpressions.Regex.Match(sentBody!, @"href=""http://css\.example\.com:5173/satisfaction/([^""\s]+)""");
        m.Success.Should().BeTrue();
        _tokenGenerator.ValidateToken(m.Groups[1].Value, conversationId, "main.t000111@example.com").Should().BeTrue(
            "主邮箱 token 必须可验证，客户才能打开评分页（加固 (a)）");
    }

    [Fact]
    public async Task ConversationClosed_EmailWithoutAnyIdentifier_DoesNotSendBrokenLink()
    {
        await WorkflowSeeder.SeedAsync(_context);
        await InsertConversationClosedTemplateAsync();

        // 客户既无邮箱（会话列与 Emails 集合皆空）也无渠道 ID
        // → 加固 (b)(c)：不发送空收件人/空链接的邮件，避免产生 about:blank 类坏链接
        var anonymousCustomer = new Customer { FirstName = "匿名", LastName = "无邮箱", Channel = "Email", ChannelId = string.Empty };
        await _unitOfWork.Customers.AddAsync(anonymousCustomer);
        await _unitOfWork.SaveChangesAsync();

        var conversationId = await InsertConversationRowAsync(
            _mailboxId, anonymousCustomer.Id,
            SourceType.Email, string.Empty, "无标识会话", number: 9203);

        var emailQueueMock = new Mock<IEmailQueueService>();
        emailQueueMock.Setup(q => q.EnqueueEmailAsync(
                It.IsAny<int>(), It.IsAny<string>(), It.IsAny<string>(), It.IsAny<string>(),
                It.IsAny<List<int>?>(), It.IsAny<string?>(), It.IsAny<string?>(), It.IsAny<int?>(),
                It.IsAny<string?>(), It.IsAny<string?>(), It.IsAny<int>(), It.IsAny<int?>(), It.IsAny<int?>()))
            .Returns(Task.CompletedTask);
        var sendEmailHandler = new SendEmailActionHandler(
            _unitOfWork, _mailTemplateService, emailQueueMock.Object, _config, _tokenGenerator, _frontendUrlResolver,
            new Mock<ILogger<SendEmailActionHandler>>().Object, _brandOptions);
        var engine = BuildEngineWithHandlers(new IWorkflowActionHandler[] { sendEmailHandler, _handler });

        var executed = await engine.ExecuteForConversationAsync(conversationId, WorkflowConst.TriggerConversationClosed);

        executed.Should().Be(0, "无任何客户标识时不得发送评价邮件（避免空收件人/空链接）");
        emailQueueMock.Verify(q => q.EnqueueEmailAsync(
                It.IsAny<int>(), It.IsAny<string>(), It.IsAny<string>(), It.IsAny<string>(),
                It.IsAny<List<int>?>(), It.IsAny<string?>(), It.IsAny<string?>(), It.IsAny<int?>(),
                It.IsAny<string?>(), It.IsAny<string?>(), It.IsAny<int>(), It.IsAny<int?>(), It.IsAny<int?>()),
            Times.Never, "无标识客户不得收到含空链接的评价邮件");
    }

    private async Task InsertConversationClosedTemplateAsync()
    {
        await _unitOfWork.MailTemplates.AddAsync(new MailTemplate
        {
            Name = "会话关闭通知客户",
            Slug = "conversation-closed",
            Type = MailTemplateType.ConversationClosed,
            Subject = "[会话关闭/Closed] {{subject}}",
            BodyHtml = "<p>您好 {{customer_name}}，您的问题已经处理完毕，本次会话已关闭。</p><a href=\"{{satisfaction_url}}\">评价本次服务 →</a>",
            BodyText = "您好 {{customer_name}}，请点击链接评价本次服务：{{satisfaction_url}}",
            IsActive = true,
            IsSystem = true,
            SortOrder = 4,
            CreatedAt = DateTime.UtcNow,
            UpdatedAt = DateTime.UtcNow
        });
        await _unitOfWork.SaveChangesAsync();
    }

    private WorkflowService BuildEngineWithHandlers(params IWorkflowActionHandler[] handlers)
    {
        var provider = new WorkflowActionHandlerProvider(
            handlers,
            new Mock<ILogger<WorkflowActionHandlerProvider>>().Object);
        return new WorkflowService(
            _unitOfWork,
            new Mock<ILogger<WorkflowService>>().Object,
            new Mock<IFolderService>().Object,
            new Mock<INotificationPublisher>().Object,
            provider,
            new Mock<IServiceProvider>().Object,
            _brandOptions);
    }

    // ==================== T000111 补充：满意度调查直发路径（ConversationStatusService.SendSatisfactionSurveyAsync） ====================
    // 除工作流 [邮件] 会话关闭通知客户 外，关闭邮件会话还会走 ConversationStatusService.CloseAsync →
    // SendSatisfactionSurveyAsync（satisfaction-survey 模板）发送带 ?rating=N 快捷评分的调查邮件。
    // 加固 (c) 让该路径在 CustomerEmail 为空时回退 Customer.Emails 主邮箱、两者皆无时跳过。
    // 以下用例锁定该直发路径产出的评价链接同样为公网前缀 + 可验证 token，绝不产生空/内网链接。

    [Fact]
    public async Task CloseEmailConversation_SendsSatisfactionSurvey_WithPublicLinksAndValidToken()
    {
        await InsertSatisfactionSurveyTemplateAsync();

        var emailCustomer = new Customer { FirstName = "邮件客户", LastName = "满意度", Channel = "Email", ChannelId = string.Empty };
        await _unitOfWork.Customers.AddAsync(emailCustomer);
        await _unitOfWork.SaveChangesAsync();

        var conversationId = await InsertConversationRowAsync(
            _mailboxId, emailCustomer.Id,
            SourceType.Email, "customer.survey@example.com", "满意度调查-正常关闭",
            number: 9301, status: ConversationStatus.Active);

        string? sentTo = null;
        string? sentBody = null;
        var emailQueueMock = new Mock<IEmailQueueService>();
        emailQueueMock.Setup(q => q.EnqueueEmailAsync(
                It.IsAny<int>(), It.IsAny<string>(), It.IsAny<string>(), It.IsAny<string>(),
                It.IsAny<List<int>?>(), It.IsAny<string?>(), It.IsAny<string?>(), It.IsAny<int?>(),
                It.IsAny<string?>(), It.IsAny<string?>(), It.IsAny<int>(), It.IsAny<int?>(), It.IsAny<int?>()))
            .Callback<int, string, string, string, List<int>?, string?, string?, int?, string?, string?, int, int?, int?>((_, to, _, body, _, _, _, _, _, _, _, _, _) => { sentTo = to; sentBody = body; })
            .Returns(Task.CompletedTask);

        var service = BuildStatusService(emailQueueMock);
        var result = await service.UpdateStatusAsync(conversationId, (int)ConversationStatus.Closed);

        result.Should().NotBeNull("关闭会话应成功返回状态响应");
        sentTo.Should().Be("customer.survey@example.com", "满意度调查邮件应发给客户邮箱");
        sentBody.Should().NotBeNullOrWhiteSpace("关闭会话后必须发送满意度调查邮件");
        sentBody!.Should().Contain($"href=\"{RequiredFrontendUrl}/satisfaction/",
            "调查邮件中的快捷评分链接必须为公网域名前缀（T000111）");
        sentBody.Should().MatchRegex(@"href=""http://css\.example\.com:5173/satisfaction/[^""\s]+\?rating=\d""",
            "快捷评分链接形态应为 {前缀}/satisfaction/{token}?rating=N");
        sentBody.Should().NotContain("corp").And.NotContain("localhost").And.NotContain("192.168")
            .And.NotContain("about:blank").And.NotContain("{{",
            "不能出现内网/本机前缀、空链接或未渲染占位符");

        var m = System.Text.RegularExpressions.Regex.Match(sentBody!, @"href=""http://css\.example\.com:5173/satisfaction/([^""\s?]+)");
        m.Success.Should().BeTrue();
        _tokenGenerator.ValidateToken(m.Groups[1].Value, conversationId, "customer.survey@example.com").Should().BeTrue(
            "调查邮件中的评价 token 必须能被后端验证（T000111）");
    }

    [Fact]
    public async Task CloseEmailConversation_WithoutDenormalizedEmail_SurveyFallsBackToMainEmail()
    {
        await InsertSatisfactionSurveyTemplateAsync();

        // 客户邮箱仅存在于 Customer.Emails 集合 → 加固 (c)：token 用主邮箱生成、收件人回退主邮箱
        var emailCustomer = new Customer
        {
            FirstName = "邮件客户",
            LastName = "调查主邮箱",
            Channel = "Email",
            ChannelId = string.Empty,
            Emails = new List<Email> { new() { EmailAddress = "main.survey@example.com", Type = 1 } }
        };
        await _unitOfWork.Customers.AddAsync(emailCustomer);
        await _unitOfWork.SaveChangesAsync();

        var conversationId = await InsertConversationRowAsync(
            _mailboxId, emailCustomer.Id,
            SourceType.Email, string.Empty, "满意度调查-主邮箱", number: 9302, status: ConversationStatus.Active);

        string? sentTo = null;
        string? sentBody = null;
        var emailQueueMock = new Mock<IEmailQueueService>();
        emailQueueMock.Setup(q => q.EnqueueEmailAsync(
                It.IsAny<int>(), It.IsAny<string>(), It.IsAny<string>(), It.IsAny<string>(),
                It.IsAny<List<int>?>(), It.IsAny<string?>(), It.IsAny<string?>(), It.IsAny<int?>(),
                It.IsAny<string?>(), It.IsAny<string?>(), It.IsAny<int>(), It.IsAny<int?>(), It.IsAny<int?>()))
            .Callback<int, string, string, string, List<int>?, string?, string?, int?, string?, string?, int, int?, int?>((_, to, _, body, _, _, _, _, _, _, _, _, _) => { sentTo = to; sentBody = body; })
            .Returns(Task.CompletedTask);

        var service = BuildStatusService(emailQueueMock);
        await service.UpdateStatusAsync(conversationId, (int)ConversationStatus.Closed);

        sentTo.Should().Be("main.survey@example.com", "调查邮件收件人应回退到 Customer.Emails 主邮箱（加固 (c)）");
        sentBody.Should().Contain($"href=\"{RequiredFrontendUrl}/satisfaction/", "评价链接必须为公网前缀（加固 (c)）");
        sentBody.Should().NotContain("about:blank").And.NotContain("{{");

        var m = System.Text.RegularExpressions.Regex.Match(sentBody!, @"href=""http://css\.example\.com:5173/satisfaction/([^""\s?]+)");
        m.Success.Should().BeTrue();
        _tokenGenerator.ValidateToken(m.Groups[1].Value, conversationId, "main.survey@example.com").Should().BeTrue(
            "主邮箱 token 必须可验证（加固 (c)）");
    }

    [Fact]
    public async Task CloseEmailConversation_WithoutAnyEmail_SkipsSatisfactionSurvey()
    {
        await InsertSatisfactionSurveyTemplateAsync();

        // 客户既无邮箱（会话列与 Emails 集合皆空）也无渠道 ID → 加固 (c)：跳过调查邮件，绝不发空收件人/空链接
        var anonymousCustomer = new Customer { FirstName = "匿名", LastName = "调查无邮箱", Channel = "Email", ChannelId = string.Empty };
        await _unitOfWork.Customers.AddAsync(anonymousCustomer);
        await _unitOfWork.SaveChangesAsync();

        var conversationId = await InsertConversationRowAsync(
            _mailboxId, anonymousCustomer.Id,
            SourceType.Email, string.Empty, "满意度调查-无邮箱", number: 9303, status: ConversationStatus.Active);

        var emailQueueMock = new Mock<IEmailQueueService>();
        emailQueueMock.Setup(q => q.EnqueueEmailAsync(
                It.IsAny<int>(), It.IsAny<string>(), It.IsAny<string>(), It.IsAny<string>(),
                It.IsAny<List<int>?>(), It.IsAny<string?>(), It.IsAny<string?>(), It.IsAny<int?>(),
                It.IsAny<string?>(), It.IsAny<string?>(), It.IsAny<int>(), It.IsAny<int?>(), It.IsAny<int?>()))
            .Returns(Task.CompletedTask);

        var service = BuildStatusService(emailQueueMock);
        await service.UpdateStatusAsync(conversationId, (int)ConversationStatus.Closed);

        emailQueueMock.Verify(q => q.EnqueueEmailAsync(
                It.IsAny<int>(), It.IsAny<string>(), It.IsAny<string>(), It.IsAny<string>(),
                It.IsAny<List<int>?>(), It.IsAny<string?>(), It.IsAny<string?>(), It.IsAny<int?>(),
                It.IsAny<string?>(), It.IsAny<string?>(), It.IsAny<int>(), It.IsAny<int?>(), It.IsAny<int?>()),
            Times.Never, "无任何客户标识时不得发送满意度调查邮件（加固 (c)）");
    }

    private async Task InsertSatisfactionSurveyTemplateAsync()
    {
        await _unitOfWork.MailTemplates.AddAsync(new MailTemplate
        {
            Name = "满意度调查",
            Slug = "satisfaction-survey",
            Type = MailTemplateType.SatisfactionSurvey,
            Subject = "[服务评价/Service Rating] {{subject}}",
            BodyHtml = "<p>感谢您使用我们的服务，请评价本次服务：</p>" +
                       "<a href=\"{{satisfaction_url}}?rating=5\">远超期望</a>" +
                       "<a href=\"{{satisfaction_url}}?rating=1\">无法接受</a>",
            BodyText = "请评价本次服务：{{satisfaction_url}}",
            IsActive = true,
            IsSystem = true,
            SortOrder = 6,
            CreatedAt = DateTime.UtcNow,
            UpdatedAt = DateTime.UtcNow
        });
        await _unitOfWork.SaveChangesAsync();
    }

    private ConversationStatusService BuildStatusService(Mock<IEmailQueueService> emailQueueMock)
    {
        var workflowMock = new Mock<IWorkflowService>();
        workflowMock.Setup(w => w.ExecuteForConversationAsync(It.IsAny<int>(), It.IsAny<string>())).ReturnsAsync(0);

        return new ConversationStatusService(
            _unitOfWork,
            new Mock<IFolderService>().Object,
            new Mock<INotificationPublisher>().Object,
            emailQueueMock.Object,
            workflowMock.Object,
            _config,
            _tokenGenerator,
            _frontendUrlResolver,
            new Mock<ILogger<ConversationStatusService>>().Object,
            new TemplateRenderer());
    }

    // ==================== 真实 SQLite 基建（同 WorkflowRecipientIntegrationTests） ====================

    private async Task<int> InsertMailboxRowAsync()
    {
        var cols = new List<string>();
        await using (var cmd = _connection.CreateCommand())
        {
            cmd.CommandText = "PRAGMA table_info(Mailboxes)";
            await using var reader = await cmd.ExecuteReaderAsync();
            while (await reader.ReadAsync())
            {
                var name = reader.GetString(1);
                var notNull = reader.GetInt64(3) == 1;
                var hasDefault = !reader.IsDBNull(4);
                if (notNull && !hasDefault && !string.Equals(name, "Id", StringComparison.OrdinalIgnoreCase))
                {
                    cols.Add(name);
                }
            }
        }

        var colList = string.Join(", ", cols.Select(c => $"\"{c}\""));
        var placeholders = string.Join(", ", cols.Select((_, i) => $"$p{i}"));
        await using var insert = _connection.CreateCommand();
        insert.CommandText = $"INSERT INTO Mailboxes ({colList}) VALUES ({placeholders})";
        for (var i = 0; i < cols.Count; i++)
        {
            insert.Parameters.AddWithValue($"$p{i}", ResolveMailboxDummyValue(cols[i]));
        }
        await insert.ExecuteNonQueryAsync();

        await using var idCmd = _connection.CreateCommand();
        idCmd.CommandText = "SELECT last_insert_rowid()";
        return Convert.ToInt32(await idCmd.ExecuteScalarAsync());
    }

    private static object ResolveMailboxDummyValue(string column) => column switch
    {
        "Version" => 1,
        "InPort" or "OutPort" => 993,
        "FetchFrequency" => 5,
        "LastUid" => 0L,
        "InValidateCert" or "AutoReplyEnabled" or "OauthEnabled" or "IsActive" => 1L,
        "InProtocol" or "InEncryption" or "OutMethod" or "OutEncryption" => 0,
        "CreatedAt" or "UpdatedAt" => "2026-01-01T00:00:00.0000000",
        _ => "it-test"
    };

    /// <summary>原生 SQL 插入会话行（绕过 EF 对 Version 并发令牌的值生成），返回自增 Id。</summary>
    private async Task<int> InsertConversationRowAsync(
        int mailboxId,
        int customerId,
        SourceType sourceType = SourceType.WeChat,
        string customerEmail = "",
        string subject = "微信咨询-打印机报错",
        int number = 9001,
        int? userId = null,
        ConversationStatus status = ConversationStatus.Closed)
    {
        var cols = new List<string>();
        await using (var cmd = _connection.CreateCommand())
        {
            cmd.CommandText = "PRAGMA table_info(Conversations)";
            await using var reader = await cmd.ExecuteReaderAsync();
            while (await reader.ReadAsync())
            {
                var name = reader.GetString(1);
                var notNull = reader.GetInt64(3) == 1;
                var hasDefault = !reader.IsDBNull(4);
                if (notNull && !hasDefault && !string.Equals(name, "Id", StringComparison.OrdinalIgnoreCase))
                {
                    cols.Add(name);
                }
            }
        }

        // 可空但本测试必须显式设置的列（PRAGMA 只列出"非空且无默认值"列，可空列需手动补充）
        var allCols = cols.Union(new[] { "CustomerId" })
            .Union(userId.HasValue ? new[] { "UserId" } : Array.Empty<string>())
            .ToList();

        var colList = string.Join(", ", allCols.Select(c => $"\"{c}\""));
        var placeholders = string.Join(", ", allCols.Select((_, i) => $"$p{i}"));
        await using var insert = _connection.CreateCommand();
        insert.CommandText = $"INSERT INTO Conversations ({colList}) VALUES ({placeholders})";
        for (var i = 0; i < allCols.Count; i++)
        {
            insert.Parameters.AddWithValue($"$p{i}", ResolveConversationValue(allCols[i], mailboxId, customerId, sourceType, customerEmail, subject, number, userId, status));
        }
        await insert.ExecuteNonQueryAsync();

        await using var idCmd = _connection.CreateCommand();
        idCmd.CommandText = "SELECT last_insert_rowid()";
        return Convert.ToInt32(await idCmd.ExecuteScalarAsync());
    }

    private static object ResolveConversationValue(
        string column, int mailboxId, int customerId,
        SourceType sourceType, string customerEmail, string subject, int number, int? userId, ConversationStatus status) => column switch
    {
        "Version" => 1,
        "MailboxId" => mailboxId,
        "CustomerId" => customerId,
        "UserId" => userId ?? 0,
        "Number" => number,
        "Type" => (int)ConversationType.Chat,
        "SourceType" => (int)sourceType,
        "State" => (int)ConversationState.Published,
        "Status" => (int)status,
        "Priority" => 1,
        // 微信渠道会话无邮箱：必须为空串（触发 BuildImVariablesAsync 的 ChannelId 双路径）
        "CustomerEmail" => customerEmail,
        "Subject" => subject,
        "CreatedAt" or "UpdatedAt" => "2026-01-01T00:00:00.0000000",
        "ThreadsCount" or "UserId" or "FolderId" or "AssigneeId" or "SlaPolicyId" or "Priority" => 0,
        _ => "wechat-it-test"
    };
}
