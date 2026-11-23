using System.Linq.Expressions;
using System.Text.Json;
using System.Text.Json.Nodes;
using FluentAssertions;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using Moq;
using NoneadCSS.Application.Configuration;
using NoneadCSS.Application.DTOs.Responses;
using NoneadCSS.Application.Services;
using NoneadCSS.Domain;
using NoneadCSS.Domain.Entities;
using NoneadCSS.Domain.Enums;
using NoneadCSS.Domain.Events;
using NoneadCSS.Domain.Repositories;
using NoneadCSS.Domain.Services;
using ConversationThread = NoneadCSS.Domain.Entities.ConversationThread;
using ThreadState = NoneadCSS.Domain.Entities.ThreadState;

namespace NoneadCSS.Tests.Services;

/// <summary>
/// MessengerIngestionService 单元测试 — Page Webhook 入站处理：新会话/已有会话、mid 幂等去重、
/// 回声与 Page 自发事件过滤、postback/optin、媒体下载落盘、回执、自动回复与联系方式引导。
/// 无网络：IMessengerClient 用 Moq 替身；Threads 仓储用内存列表按表达式过滤，
/// 以便同时覆盖「事务内去重」与「回执查线程」两类查询。
/// 依赖：IConversationRepository.FindActiveMessengerConversationByChannelIdAsync（A3/A4 共享改动）。
/// </summary>
public class MessengerIngestionServiceTests : IDisposable
{
    private const string PageId = "1055123456789012";
    private const string Psid = "1234567890123456";

    private readonly string _uploadRoot = Path.Combine(Path.GetTempPath(), "messenger-ingest-" + Guid.NewGuid().ToString("N"));

    private readonly Mock<IUnitOfWork> _mockUnitOfWork;
    private readonly Mock<IWorkflowService> _mockWorkflowService;
    private readonly Mock<IMessengerSettingsProvider> _mockSettingsProvider;
    private readonly Mock<IMessengerClient> _mockClient;
    private readonly Mock<INotificationPublisher> _mockNotificationPublisher;
    private readonly Mock<IDistributedLockService> _mockLockService;
    private readonly Mock<ILogger<MessengerIngestionService>> _mockLogger;
    private readonly Mock<IConfiguration> _mockConfiguration;
    private readonly Mock<IHostEnvironment> _mockEnv;
    private readonly MessengerIngestionService _service;

    private readonly Mock<ISystemOptionRepository> _mockSystemOptionRepo;
    private readonly Mock<IConversationRepository> _mockConversationRepo;
    private readonly Mock<IThreadRepository> _mockThreadRepo;
    private readonly Mock<ICustomerRepository> _mockCustomerRepo;
    private readonly Mock<IAttachmentRepository> _mockAttachmentRepo;
    private readonly Mock<IMailboxRepository> _mockMailboxRepo;
    private readonly Mock<IUnitOfWorkTransaction> _mockTransaction;

    /// <summary>内存线程表：FindAsync 按传入表达式过滤，贴近真实仓储语义</summary>
    private readonly List<ConversationThread> _threads = new();

    public MessengerIngestionServiceTests()
    {
        Directory.CreateDirectory(_uploadRoot);

        _mockUnitOfWork = new Mock<IUnitOfWork>();
        _mockWorkflowService = new Mock<IWorkflowService>();
        _mockSettingsProvider = new Mock<IMessengerSettingsProvider>();
        _mockClient = new Mock<IMessengerClient>();
        _mockNotificationPublisher = new Mock<INotificationPublisher>();
        _mockLockService = new Mock<IDistributedLockService>();
        _mockLogger = new Mock<ILogger<MessengerIngestionService>>();
        _mockConfiguration = new Mock<IConfiguration>();
        _mockEnv = new Mock<IHostEnvironment>();

        _mockSystemOptionRepo = new Mock<ISystemOptionRepository>();
        _mockConversationRepo = new Mock<IConversationRepository>();
        _mockThreadRepo = new Mock<IThreadRepository>();
        _mockCustomerRepo = new Mock<ICustomerRepository>();
        _mockAttachmentRepo = new Mock<IAttachmentRepository>();
        _mockMailboxRepo = new Mock<IMailboxRepository>();
        _mockTransaction = new Mock<IUnitOfWorkTransaction>();

        _mockUnitOfWork.Setup(u => u.SystemOptions).Returns(_mockSystemOptionRepo.Object);
        _mockUnitOfWork.Setup(u => u.Conversations).Returns(_mockConversationRepo.Object);
        _mockUnitOfWork.Setup(u => u.Threads).Returns(_mockThreadRepo.Object);
        _mockUnitOfWork.Setup(u => u.Customers).Returns(_mockCustomerRepo.Object);
        _mockUnitOfWork.Setup(u => u.Attachments).Returns(_mockAttachmentRepo.Object);
        _mockUnitOfWork.Setup(u => u.Mailboxes).Returns(_mockMailboxRepo.Object);
        _mockUnitOfWork.Setup(u => u.BeginTransactionAsync()).ReturnsAsync(_mockTransaction.Object);

        _mockThreadRepo
            .Setup(r => r.FindAsync(It.IsAny<Expression<Func<ConversationThread, bool>>>()))
            .ReturnsAsync((Expression<Func<ConversationThread, bool>> expr) => _threads.Where(expr.Compile()).ToList());

        _mockConfiguration.Setup(c => c["Attachment:UploadPath"]).Returns(_uploadRoot);
        _mockEnv.Setup(e => e.ContentRootPath).Returns(_uploadRoot);

        // 默认成功获取分布式锁
        _mockLockService
            .Setup(s => s.TryAcquireAsync(It.IsAny<string>(), It.IsAny<int>()))
            .ReturnsAsync(new Mock<IDistributedLockHandle>().Object);

        SetupSettings(new MessengerOptions
        {
            PageId = PageId,
            PageAccessToken = "token",
            AppSecret = "secret",
            VerifyToken = "verify",
            Enabled = true,
            MailboxId = 1,
            WindowHours = 24,
            ContactCollectionEnabled = false,
            MarkSeen = false,
            AutoReplyEnabled = false,
            TypingIndicator = false
        });

        _service = new MessengerIngestionService(
            _mockUnitOfWork.Object,
            _mockWorkflowService.Object,
            _mockSettingsProvider.Object,
            _mockClient.Object,
            _mockNotificationPublisher.Object,
            _mockLockService.Object,
            _mockLogger.Object,
            _mockConfiguration.Object,
            _mockEnv.Object);
    }

    public void Dispose()
    {
        try
        {
            if (Directory.Exists(_uploadRoot)) Directory.Delete(_uploadRoot, recursive: true);
        }
        catch (IOException)
        {
            // 临时目录清理失败不影响测试结果
        }
        GC.SuppressFinalize(this);
    }

    private void SetupSettings(MessengerOptions options)
        => _mockSettingsProvider.Setup(s => s.Current).Returns(options);

    // ---------------------------------------------------------------- 报文构造器（System.Text.Json.Nodes 组装）
    // 历史 bug：本文件原先用 C# 原始字符串插值拼报文，而括号规则按前缀不同
    // （两美元前缀里 hole 写双括号、单括号是字面量；三美元前缀里 hole 要写三括号），
    // 于是拼出过「对象落在键的位置」和「少一个右括号」两类非法 JSON。
    // 解析层遇到非法 JSON 只静默丢消息，全部用例会集体表现为空结果
    // （NewConversationCount 0 / ReplyCount 0 / AutoReplyContent null / ErrorMessage null）而没有任何异常，极难定位。
    // 现在一律用 JsonNode 组装：手写括号从根上消失，任何拼装错误在构造期就抛出（见 ParseMessagingList / ParseEventObject），
    // 并由 PayloadBuilders_AlwaysComposeValidJson 把每种构造器组合再解析一遍核对外壳字段。

    private const long DefaultEntryTime = 1763000000000;
    private const long DefaultTimestamp = 1763000000000;

    /// <summary>顶层 Page Webhook 报文；messagingJson 为一个（或多个逗号相连的）messaging 元素。</summary>
    private static string PagePayload(string messagingJson, string pageId = PageId, long entryTime = DefaultEntryTime)
        => PagePayloadEntries((pageId, entryTime, messagingJson));

    /// <summary>多 entry 报文（Meta 单次 POST 允许携带多个 entry）。</summary>
    private static string PagePayloadEntries(params (string PageId, long EntryTime, string MessagingJson)[] entries)
    {
        var entryArray = new JsonArray();
        foreach (var (pageId, entryTime, messagingJson) in entries)
        {
            entryArray.Add(PageEntryNode(pageId, entryTime, messagingJson));
        }

        var root = new JsonObject
        {
            ["object"] = "page",
            ["entry"] = entryArray,
        };
        return root.ToJsonString();
    }

    private static JsonObject PageEntryNode(string pageId, long entryTime, string messagingJson = "")
        => new JsonObject
        {
            ["id"] = pageId,
            ["time"] = entryTime,
            // 普通 page 报文一律带 messaging 键（空列表就是 []），与 EmptyMessagingArray 用例的语义一致；
            // 不带该键的 entry（entry 级回执的官方落位）请用下面的 BareEntryNode。
            ["messaging"] = ParseMessagingList(messagingJson),
        };

    /// <summary>不带 messaging 键的 entry：entry 级回执（官方 message_deliveries / message_reads 落位）用这个形状。</summary>
    private static JsonObject BareEntryNode(string pageId, long entryTime)
        => new JsonObject
        {
            ["id"] = pageId,
            ["time"] = entryTime,
        };

    /// <summary>把「逗号相连的 messaging 元素」解析成数组；空串表示空数组。拼装坏了就在这里抛出，不再静默产出坏报文。</summary>
    private static JsonArray ParseMessagingList(string messagingJson)
    {
        if (string.IsNullOrWhiteSpace(messagingJson))
        {
            return new JsonArray();
        }

        if (JsonNode.Parse("[" + messagingJson + "]") is not JsonArray array)
        {
            throw new ArgumentException($"messaging 列表不是 JSON 数组：{messagingJson}", nameof(messagingJson));
        }

        return array;
    }

    /// <summary>
    /// messaging 元素外壳：sender.id / recipient.id / timestamp 加上事件对象
    /// （message | postback | optin | reaction | delivery | read）。
    /// 没有 sender.id 时 MessengerWebhookParser.ParseSingleMessaging 会直接返回 null（回执除外），
    /// 所以外壳字段由构造器统一保证，调用方不需要也不应该自己手写。
    /// </summary>
    private static string Messaging(string eventJson, string psid = Psid, string pageId = PageId, long timestamp = DefaultTimestamp)
        => MessagingNode(ParseEventObject(eventJson), psid, pageId, timestamp).ToJsonString();

    private static JsonObject ParseEventObject(string eventJson)
    {
        if (JsonNode.Parse(eventJson) is not JsonObject obj)
        {
            throw new ArgumentException($"事件对象不是 JSON object：{eventJson}", nameof(eventJson));
        }

        return obj;
    }

    private static JsonObject MessagingNode(JsonObject eventNode, string psid, string pageId, long timestamp)
    {
        var messaging = new JsonObject
        {
            ["sender"] = new JsonObject { ["id"] = psid },
            ["recipient"] = new JsonObject { ["id"] = pageId },
            ["timestamp"] = timestamp,
        };

        foreach (var property in eventNode)
        {
            // 事件对象要并入外壳，因此必须克隆：JsonNode 不允许同一实例挂在两个父节点下。
            messaging[property.Key] = property.Value?.DeepClone();
        }

        return messaging;
    }

    private static JsonObject MessageObject(string? mid, string? text, int? seq)
    {
        var message = new JsonObject();
        if (mid is not null)
        {
            message["mid"] = mid;
        }

        if (seq is not null)
        {
            message["seq"] = seq.Value;
        }

        if (text is not null)
        {
            message["text"] = text;
        }

        return message;
    }

    /// <summary>message 对象：mid / seq / text —— 纯文本报文的事件体。</summary>
    private static string TextMessage(string mid, string text, int seq = 1)
        => new JsonObject { ["message"] = MessageObject(mid, text, seq) }.ToJsonString();

    /// <summary>message.is_echo:true —— Page/坐席侧发出的内容，入站必须跳过。</summary>
    private static string EchoMessage(string mid, string text, int seq = 2)
    {
        var message = MessageObject(mid, text, seq);
        message["is_echo"] = true;
        return new JsonObject { ["message"] = message }.ToJsonString();
    }

    /// <summary>postback 事件：title / payload / data（可选）。</summary>
    private static string PostbackMessage(string title, string payload, string? data = null)
    {
        var postback = new JsonObject { ["title"] = title, ["payload"] = payload };
        if (data is not null)
        {
            postback["data"] = data;
        }

        return new JsonObject { ["postback"] = postback }.ToJsonString();
    }

    /// <summary>optin 事件：ref / user_ref（可选）。</summary>
    private static string OptinMessage(string reference, string? userRef = null)
    {
        var optin = new JsonObject { ["ref"] = reference };
        if (userRef is not null)
        {
            optin["user_ref"] = userRef;
        }

        return new JsonObject { ["optin"] = optin }.ToJsonString();
    }

    /// <summary>reaction 事件：emoji / mid。</summary>
    private static string ReactionMessage(string emoji, string mid)
        => new JsonObject { ["reaction"] = new JsonObject { ["emoji"] = emoji, ["mid"] = mid } }.ToJsonString();

    /// <summary>message.attachments 里带 payload.url 的附件报文（image / audio / video / file / fallback）。</summary>
    private static string AttachmentMessage(string mid, string type, string url, string? attachmentId = null, int? seq = null)
    {
        var payload = new JsonObject { ["url"] = url };
        if (attachmentId is not null)
        {
            payload["attachment_id"] = attachmentId;
        }

        var attachment = new JsonObject { ["type"] = type, ["payload"] = payload };
        var message = MessageObject(mid, text: null, seq);
        var attachments = new JsonArray();
        attachments.Add(attachment);
        message["attachments"] = attachments;

        return new JsonObject { ["message"] = message }.ToJsonString();
    }

    /// <summary>message.attachments 里的 location 附件（官方报文该类型不带 seq）。</summary>
    private static string LocationMessage(string mid, double lat, double longitude)
    {
        var coordinates = new JsonObject { ["lat"] = lat, ["long"] = longitude };
        var attachment = new JsonObject
        {
            ["type"] = "location",
            ["payload"] = new JsonObject { ["coordinates"] = coordinates },
        };

        var attachments = new JsonArray();
        attachments.Add(attachment);

        var message = MessageObject(mid, text: null, seq: null);
        message["attachments"] = attachments;

        return new JsonObject { ["message"] = message }.ToJsonString();
    }

    /// <summary>messaging 级回执：delivery / read 对象挂在 messaging 元素里（传 null 的字段省略）。</summary>
    private static string ReceiptEvent(string property, string[]? mids, long? watermark = null)
        => new JsonObject { [property] = ReceiptObject(mids, watermark) }.ToJsonString();

    /// <summary>entry 级回执（官方 message_deliveries / message_reads 落位）：entry 内直接给 delivery/read，无 messaging。</summary>
    private static string EntryReceiptPayload(string property, string[]? mids, long? watermark = null, long entryTime = DefaultEntryTime, string pageId = PageId)
    {
        var entry = BareEntryNode(pageId, entryTime);
        entry[property] = ReceiptObject(mids, watermark);

        var entryArray = new JsonArray();
        entryArray.Add(entry);

        var root = new JsonObject
        {
            ["object"] = "page",
            ["entry"] = entryArray,
        };
        return root.ToJsonString();
    }

    private static JsonObject ReceiptObject(string[]? mids, long? watermark)
    {
        var receipt = new JsonObject();
        if (mids is not null)
        {
            var midArray = new JsonArray();
            foreach (var mid in mids)
            {
                midArray.Add(JsonValue.Create(mid));
            }

            receipt["mids"] = midArray;
        }

        if (watermark is not null)
        {
            receipt["watermark"] = watermark.Value;
        }

        return receipt;
    }

    /// <summary>兼容旧形状的回执：ops[].mid（ParseReceipts 仍支持这条路径，不能丢）。</summary>
    private static string LegacyOpsReceiptEvent(string property, params string[] opsMids)
    {
        var ops = new JsonArray();
        foreach (var mid in opsMids)
        {
            ops.Add(new JsonObject { ["mid"] = mid });
        }

        return new JsonObject { [property] = new JsonObject { ["ops"] = ops } }.ToJsonString();
    }

    private Customer NewCustomer(int id = 100) => new()
    {
        Id = id,
        Channel = MessengerIngestionService.CustomerChannel,
        ChannelId = Psid
    };

    private void SetupNewCustomer()
        => _mockCustomerRepo
            .Setup(r => r.GetOrCreateByChannelAsync(MessengerIngestionService.CustomerChannel, Psid, null))
            .ReturnsAsync(NewCustomer());

    private void SetupNoActiveConversation()
        => _mockConversationRepo
            .Setup(r => r.FindActiveMessengerConversationByChannelIdAsync(Psid, It.IsAny<TimeSpan>()))
            .ReturnsAsync((Conversation?)null);

    private Conversation SetupExistingConversation(int id = 500)
    {
        var conversation = new Conversation
        {
            Id = id,
            CustomerId = 100,
            Number = 41,
            MailboxId = 1,
            SourceType = SourceType.Messenger,
            Type = ConversationType.Chat,
            Status = ConversationStatus.Active,
            CreatedByCustomerId = 100
        };
        _mockConversationRepo
            .Setup(r => r.FindActiveMessengerConversationByChannelIdAsync(Psid, It.IsAny<TimeSpan>()))
            .ReturnsAsync(conversation);
        return conversation;
    }

    private void SetupConversationInsert(int id = 500)
    {
        _mockConversationRepo.Setup(r => r.GetNextNumberAsync()).ReturnsAsync(42);
        _mockConversationRepo.Setup(r => r.AddAsync(It.IsAny<Conversation>()))
            .ReturnsAsync((Conversation c) =>
            {
                c.Id = id;
                return c;
            });
    }

    private ConversationThread CaptureThread(int id = 600)
    {
        var captured = new List<ConversationThread>();
        _mockThreadRepo.Setup(r => r.AddAsync(It.IsAny<ConversationThread>()))
            .ReturnsAsync((ConversationThread t) =>
            {
                t.Id = id + captured.Count;
                captured.Add(t);
                _threads.Add(t);
                return t;
            });
        _lastCaptured = captured;
        return null!;
    }

    private List<ConversationThread>? _lastCaptured;

    private List<ConversationThread> CapturedThreads() => _lastCaptured ?? new List<ConversationThread>();

    #region 报文识别与基础守卫

    [Fact]
    public void PayloadBuilders_AlwaysComposeValidJson()
    {
        // 回归守卫：历史 bug 是手工拼报文时出现「对象落在键的位置」或少一个右括号的非法 JSON ——
        // 解析层只是静默丢消息，其余用例会集体表现为空结果而难以定位。
        // 现在报文一律由 JsonNode 组装，这里把每个构造器的每种组合都重新解析一遍，
        // 并核对外壳必备字段（object / entry[].id / messaging[].sender.id / recipient.id / timestamp / 事件键），
        // 任何拼装错误都会直接在这条用例里失败，而不是让后面几十条用例空跑。
        var messagingCases = new (string Label, string Payload)[]
        {
            ("text", PagePayload(Messaging(TextMessage("m_valid_1", "你好")))),
            ("echo", PagePayload(Messaging(EchoMessage("m_echo", "坐席已发出的内容")))),
            ("postback", PagePayload(Messaging(PostbackMessage("开始咨询", "START_CHAT", "referred")))),
            ("optin", PagePayload(Messaging(OptinMessage("PLUGINS_REF_1", "u1")))),
            ("reaction", PagePayload(Messaging(ReactionMessage("👍", "m_target")))),
            ("image-attachment", PagePayload(Messaging(AttachmentMessage("m_img", "image", "https://scontent.example.com/a.png", "att_1", 3)))),
            ("file-attachment", PagePayload(Messaging(AttachmentMessage("m_big", "file", "https://download.example.com/big.bin")))),
            ("location", PagePayload(Messaging(LocationMessage("m_loc", 31.23, 121.47)))),
            ("delivery@messaging", PagePayload(Messaging(ReceiptEvent("delivery", new[] { "m_valid_1" }, 1763000000000)))),
            ("read@messaging", PagePayload(Messaging(ReceiptEvent("read", new[] { "m_valid_1" }, 1763000000001)))),
            ("legacy-ops@messaging", PagePayload(Messaging(LegacyOpsReceiptEvent("delivery", "m_valid_1")))),
            ("empty-messaging", PagePayload(string.Empty)),
            ("two-messaging", PagePayload(string.Join(",", Messaging(TextMessage("m_a", "第一句")), Messaging(TextMessage("m_b", "第二句"))))),
            ("two-entries", PagePayloadEntries((PageId, 1763000000000, Messaging(TextMessage("m_e1", "入口一"))), (PageId, 1763000001000, Messaging(TextMessage("m_e2", "入口二"))))),
        };

        var eventKeys = new[] { "message", "postback", "optin", "reaction", "delivery", "read" };

        foreach (var (label, payload) in messagingCases)
        {
            using var doc = JsonDocument.Parse(payload);
            var root = doc.RootElement;
            root.GetProperty("object").GetString().Should().Be("page", "{0}", label);
            var entries = root.GetProperty("entry");
            entries.GetArrayLength().Should().Be(label == "two-entries" ? 2 : 1, "{0}", label);
            entries[0].GetProperty("id").GetString().Should().Be(PageId, "{0}", label);

            // messaging 键必须存在：空列表写成 []，缺键只允许出现在 entry 级回执那条路径上。
            entries[0].TryGetProperty("messaging", out var messaging).Should().BeTrue("{0}: entry 缺少 messaging 键", label);
            messaging.ValueKind.Should().Be(JsonValueKind.Array, "{0}: messaging 不是数组", label);
            if (label == "empty-messaging")
            {
                messaging.GetArrayLength().Should().Be(0, "{0}", label);
                continue;
            }

            messaging.GetArrayLength().Should().Be(label == "two-messaging" ? 2 : 1, "{0}", label);

            var item = messaging[0];
            item.GetProperty("sender").GetProperty("id").GetString().Should().Be(Psid, "{0}", label);
            item.GetProperty("recipient").GetProperty("id").GetString().Should().Be(PageId, "{0}", label);
            item.GetProperty("timestamp").GetInt64().Should().Be(DefaultTimestamp, "{0}", label);

            // 事件对象必须出现在「值的位置」上 —— 这正是历史上「键位置出现对象」那条 bug 的判据。
            var hasEventObject = eventKeys.Any(key => item.TryGetProperty(key, out _));
            hasEventObject.Should().BeTrue("{0}: messaging 元素缺少事件对象", label);
        }

        // Page 自发事件（sender.id == PageId）同样必须拼成合法外壳，且保留调用方覆盖的 psid。
        using (var doc = JsonDocument.Parse(PagePayload(Messaging(TextMessage("m_self", "页面广播"), psid: PageId))))
        {
            doc.RootElement.GetProperty("entry")[0].GetProperty("messaging")[0]
                .GetProperty("sender").GetProperty("id").GetString().Should().Be(PageId);
        }

        // entry 级回执：没有 messaging 元素，但回执对象必须是合法 JSON，且 mids / watermark 各自按给出与否出现。
        // 期望值直接写进用例表：mids 与 watermark 只在给出时出现，且出现时逐项比对确切值。
        var entryReceiptCases = new (string Label, string Property, string[]? Mids, long? Watermark, string Payload)[]
        {
            ("delivery-mids-watermark", "delivery", new[] { "m_replied" }, 1763000001000, EntryReceiptPayload("delivery", new[] { "m_replied" }, 1763000001000)),
            ("read-watermark-only", "read", null, 1763000002000, EntryReceiptPayload("read", mids: null, watermark: 1763000002000)),
            ("delivery-mids-no-watermark", "delivery", new[] { "m_unknown_mid" }, null, EntryReceiptPayload("delivery", new[] { "m_unknown_mid" }, watermark: null)),
        };

        foreach (var (label, property, expectedMids, expectedWatermark, payload) in entryReceiptCases)
        {
            using var doc = JsonDocument.Parse(payload);
            var root = doc.RootElement;
            root.GetProperty("object").GetString().Should().Be("page", "{0}", label);
            var entry = root.GetProperty("entry")[0];
            entry.GetProperty("id").GetString().Should().Be(PageId, "{0}", label);
            entry.GetProperty("time").GetInt64().Should().Be(DefaultEntryTime, "{0}", label);
            entry.TryGetProperty("messaging", out _).Should().BeFalse("{0}: entry 级回执不应带 messaging", label);

            var receipt = entry.GetProperty(property);
            receipt.TryGetProperty("mids", out var mids).Should().Be(expectedMids is not null, "{0}: mids 出现与否不符", label);
            if (expectedMids is not null)
            {
                mids.GetArrayLength().Should().Be(expectedMids.Length, "{0}", label);
                for (var idx = 0; idx < expectedMids.Length; idx++)
                {
                    mids[idx].GetString().Should().Be(expectedMids[idx], "{0}", label);
                }
            }

            receipt.TryGetProperty("watermark", out var watermark).Should().Be(expectedWatermark is not null, "{0}: watermark 出现与否不符", label);
            if (expectedWatermark is not null)
            {
                watermark.GetInt64().Should().Be(expectedWatermark.Value, "{0}", label);
            }
        }
    }

    [Fact]
    public async Task ProcessWebhookPayloadAsync_NullPayload_ReturnsEmptyResult()
    {
        var result = await _service.ProcessWebhookPayloadAsync(null);

        result.NewConversationCount.Should().Be(0);
        result.ReplyCount.Should().Be(0);
        result.ErrorMessage.Should().BeNull();
        _mockThreadRepo.Verify(r => r.AddAsync(It.IsAny<ConversationThread>()), Times.Never);
    }

    [Fact]
    public async Task ProcessWebhookPayloadAsync_NonPagePayload_IsIgnored()
    {
        // 非 page 对象（WhatsApp Business Account 的 webhook 形状）：整段应被忽略
        var payload = "{\"object\":\"whatsapp_business_account\",\"entry\":[]}";

        var result = await _service.ProcessWebhookPayloadAsync(payload);

        result.NewConversationCount.Should().Be(0);
        _mockCustomerRepo.Verify(r => r.GetOrCreateByChannelAsync(It.IsAny<string>(), It.IsAny<string>(), It.IsAny<string?>()), Times.Never);
    }

    [Fact]
    public async Task ProcessWebhookPayloadAsync_MalformedPayload_IsIgnored()
    {
        var result = await _service.ProcessWebhookPayloadAsync("{not-json");

        result.NewConversationCount.Should().Be(0);
        result.ReplyCount.Should().Be(0);
    }

    [Fact]
    public async Task ProcessWebhookPayloadAsync_EmptyMessagingArray_TouchesNothing()
    {
        var result = await _service.ProcessWebhookPayloadAsync(PagePayload(string.Empty));

        result.NewConversationCount.Should().Be(0);
        _mockLockService.Verify(s => s.TryAcquireAsync(It.IsAny<string>(), It.IsAny<int>()), Times.Never);
    }

    #endregion

    #region 新会话 / 已有会话

    [Fact]
    public async Task ProcessWebhookPayloadAsync_FirstMessage_CreatesConversationAndCustomerThread()
    {
        SetupNewCustomer();
        SetupNoActiveConversation();
        SetupConversationInsert(500);
        CaptureThread(600);

        var result = await _service.ProcessWebhookPayloadAsync(PagePayload(Messaging(TextMessage("m_inbound_1", "你好，我想咨询订单"))));

        result.NewConversationCount.Should().Be(1);
        result.ReplyCount.Should().Be(0);
        result.ConversationId.Should().Be(500);
        result.IsDuplicate.Should().BeFalse();

        var thread = CapturedThreads().Should().HaveCount(1).And.ContainSingle().Subject;
        thread.MessageId.Should().Be("m_inbound_1");
        thread.From.Should().Be(Psid);
        thread.To.Should().Be(PageId);
        thread.SourceType.Should().Be(SourceType.Messenger);
        thread.Type.Should().Be(ThreadType.Customer);
        thread.SourceVia.Should().Be(SourceVia.Customer);
        thread.State.Should().NotBe(ThreadState.Deleted);

        _mockConversationRepo.Verify(r => r.AddAsync(It.Is<Conversation>(c =>
            c.SourceType == SourceType.Messenger && c.Type == ConversationType.Chat && c.Number == 42)), Times.Once);
        _mockUnitOfWork.Verify(u => u.SaveChangesAsync(), Times.AtLeastOnce);
        _mockTransaction.Verify(t => t.CommitAsync(), Times.Once);
        _mockWorkflowService.Verify(w => w.ExecuteForConversationAsync(500, WorkflowConst.TriggerNewConversation), Times.Once);
        _mockNotificationPublisher.Verify(n => n.NotifyMailboxAsync("1", "conversation_updated", It.IsAny<object>()), Times.Once);
    }

    [Fact]
    public async Task ProcessWebhookPayloadAsync_CustomerMappedByChannelMessenger()
    {
        SetupNewCustomer();
        SetupNoActiveConversation();
        SetupConversationInsert(500);
        CaptureThread(600);

        await _service.ProcessWebhookPayloadAsync(PagePayload(Messaging(TextMessage("m_channel_key", "hi"))));

        // Customer.Channel 必须与 A4/A17 的字符串字面量完全一致
        _mockCustomerRepo.Verify(r => r.GetOrCreateByChannelAsync("messenger", Psid, null), Times.Once);
    }

    [Fact]
    public async Task ProcessWebhookPayloadAsync_ExistingConversation_AppendsReplyWithoutNewConversation()
    {
        var conversation = SetupExistingConversation(500);
        _mockCustomerRepo
            .Setup(r => r.GetOrCreateByChannelAsync(MessengerIngestionService.CustomerChannel, Psid, null))
            .ReturnsAsync(NewCustomer(100));
        CaptureThread(601);

        var before = conversation.LastReplyAt;
        var result = await _service.ProcessWebhookPayloadAsync(PagePayload(Messaging(TextMessage("m_inbound_2", "再问一次"))));

        result.NewConversationCount.Should().Be(0);
        result.ReplyCount.Should().Be(1);
        result.ConversationId.Should().Be(500);
        conversation.LastReplyAt.Should().BeOnOrAfter(before ?? DateTime.MinValue);
        conversation.LastReplyFrom.Should().Be(SourceVia.Customer);

        _mockConversationRepo.Verify(r => r.AddAsync(It.IsAny<Conversation>()), Times.Never);
        _mockWorkflowService.Verify(w => w.ExecuteForConversationAsync(500, WorkflowConst.TriggerNewReply), Times.Once);
    }

    [Fact]
    public async Task ProcessWebhookPayloadAsync_CustomerCreationFails_ReportsErrorAndSkipsThread()
    {
        _mockCustomerRepo
            .Setup(r => r.GetOrCreateByChannelAsync(MessengerIngestionService.CustomerChannel, Psid, null))
            .ReturnsAsync((Customer?)null);
        CaptureThread();

        var result = await _service.ProcessWebhookPayloadAsync(PagePayload(Messaging(TextMessage("m_no_customer", "hi"))));

        result.ErrorMessage.Should().Contain("客户创建失败");
        _mockThreadRepo.Verify(r => r.AddAsync(It.IsAny<ConversationThread>()), Times.Never);
    }

    [Fact]
    public async Task ProcessWebhookPayloadAsync_NoUsableMailbox_ReportsError()
    {
        SetupSettings(new MessengerOptions
        {
            PageId = PageId, PageAccessToken = "t", AppSecret = "s", VerifyToken = "v",
            Enabled = true, MailboxId = null, ContactCollectionEnabled = false, MarkSeen = false
        });
        SetupNewCustomer();
        _mockMailboxRepo.Setup(r => r.FindAsync(It.IsAny<Expression<Func<Mailbox, bool>>>()))
            .ReturnsAsync(new List<Mailbox>());

        var result = await _service.ProcessWebhookPayloadAsync(PagePayload(Messaging(TextMessage("m_no_mailbox", "hi"))));

        result.ErrorMessage.Should().Contain("未找到可用邮箱");
        _mockThreadRepo.Verify(r => r.AddAsync(It.IsAny<ConversationThread>()), Times.Never);
    }

    #endregion

    #region 幂等：mid 去重与分布式锁

    [Fact]
    public async Task ProcessWebhookPayloadAsync_SameMidTwice_SecondRunIsDuplicate()
    {
        SetupNewCustomer();
        SetupNoActiveConversation();
        SetupConversationInsert(500);
        CaptureThread(600);

        var first = await _service.ProcessWebhookPayloadAsync(PagePayload(Messaging(TextMessage("m_dup", "第一条"))));
        // 复用同一会话，模拟 Meta 重投
        SetupExistingConversation(500);
        var second = await _service.ProcessWebhookPayloadAsync(PagePayload(Messaging(TextMessage("m_dup", "第一条"))));

        first.NewConversationCount.Should().Be(1);
        second.IsDuplicate.Should().BeTrue();
        second.NewConversationCount.Should().Be(0);
        second.ReplyCount.Should().Be(0);
        _threads.Should().HaveCount(1);
        _mockLockService.Verify(s => s.TryAcquireAsync("messenger_ingest:m_dup", It.IsAny<int>()), Times.Exactly(2));
    }

    [Fact]
    public async Task ProcessWebhookPayloadAsync_LockHeldElsewhere_SkipsWithoutTouchingRepositories()
    {
        _mockLockService.Setup(s => s.TryAcquireAsync(It.IsAny<string>(), It.IsAny<int>()))
            .ReturnsAsync((IDistributedLockHandle?)null);

        var result = await _service.ProcessWebhookPayloadAsync(PagePayload(Messaging(TextMessage("m_locked", "hi"))));

        result.IsDuplicate.Should().BeTrue();
        _mockCustomerRepo.Verify(r => r.GetOrCreateByChannelAsync(It.IsAny<string>(), It.IsAny<string>(), It.IsAny<string?>()), Times.Never);
        _mockThreadRepo.Verify(r => r.AddAsync(It.IsAny<ConversationThread>()), Times.Never);
    }

    [Fact]
    public async Task ProcessWebhookPayloadAsync_UniqueConstraintRace_BecomesIdempotentSkip()
    {
        SetupNewCustomer();
        SetupNoActiveConversation();
        SetupConversationInsert(500);

        _mockThreadRepo.Setup(r => r.AddAsync(It.IsAny<ConversationThread>()))
            .ReturnsAsync((ConversationThread t) =>
            {
                // 并发实例已插入同一 mid：本实例写入触发唯一约束
                _threads.Add(t);
                throw new Microsoft.EntityFrameworkCore.DbUpdateException(
                    "Cannot insert duplicate key row in object 'dbo.Threads' with unique index 'UX_Threads_MessageId'. The duplicate key value is (m_race).");
            });

        var result = await _service.ProcessWebhookPayloadAsync(PagePayload(Messaging(TextMessage("m_race", "hi"))));

        result.IsDuplicate.Should().BeTrue();
        result.ErrorMessage.Should().BeNull();
        _mockUnitOfWork.Verify(u => u.ClearChanges(), Times.Once);
    }

    [Fact]
    public async Task ProcessWebhookPayloadAsync_PersistenceFailure_RollsBackAndWrapsException()
    {
        SetupNewCustomer();
        SetupNoActiveConversation();
        SetupConversationInsert(500);
        _mockThreadRepo.Setup(r => r.AddAsync(It.IsAny<ConversationThread>()))
            .ThrowsAsync(new Microsoft.EntityFrameworkCore.DbUpdateException("connection lost"));

        var act = () => _service.ProcessWebhookPayloadAsync(PagePayload(Messaging(TextMessage("m_fail", "hi"))));

        await act.Should().ThrowAsync<InvalidOperationException>()
            .WithMessage("Messenger 消息持久化失败");
        // Moq 无法 verify 扩展方法（SafeRollbackAsync 是 UnitOfWorkTransactionExtensions 上的扩展，
        // 会抛 NotSupportedException: Unsupported expression），因此断言它真正调用的接口方法 RollbackAsync()。
        // 与 LineIngestionServiceTests 的同一口径保持一致。
        _mockTransaction.Verify(t => t.RollbackAsync(), Times.Once);
    }

    #endregion

    #region 回声 / Page 自发 / 非消息事件

    [Fact]
    public async Task ProcessWebhookPayloadAsync_EchoMessage_IsIgnoredAndCounted()
    {
        var echo = Messaging(EchoMessage("m_echo", "坐席已发出的内容"));

        var result = await _service.ProcessWebhookPayloadAsync(PagePayload(echo));

        result.IgnoredEchoCount.Should().Be(1);
        result.NewConversationCount.Should().Be(0);
        _mockThreadRepo.Verify(r => r.AddAsync(It.IsAny<ConversationThread>()), Times.Never);
    }

    [Fact]
    public async Task ProcessWebhookPayloadAsync_SenderIsThePage_IsIgnored()
    {
        var self = Messaging(TextMessage("m_self", "页面广播"), psid: PageId);

        var result = await _service.ProcessWebhookPayloadAsync(PagePayload(self));

        result.NewConversationCount.Should().Be(0);
        _mockThreadRepo.Verify(r => r.AddAsync(It.IsAny<ConversationThread>()), Times.Never);
    }

    [Fact]
    public async Task ProcessWebhookPayloadAsync_Postback_CreatesThreadWithSyntheticId()
    {
        SetupNewCustomer();
        SetupNoActiveConversation();
        SetupConversationInsert(500);
        CaptureThread(600);

        var postback = Messaging(PostbackMessage("开始咨询", "START_CHAT", "referred"));

        var result = await _service.ProcessWebhookPayloadAsync(PagePayload(postback));

        result.NewConversationCount.Should().Be(1);
        var thread = CapturedThreads().Should().ContainSingle().Subject;
        thread.MessageId.Should().StartWith("msg_postback_");
        thread.Body.Should().Contain("开始咨询");
    }

    [Fact]
    public async Task ProcessWebhookPayloadAsync_Optin_CreatesThreadWithRef()
    {
        SetupNewCustomer();
        SetupNoActiveConversation();
        SetupConversationInsert(500);
        CaptureThread(600);

        var optin = Messaging(OptinMessage("PLUGINS_REF_1", "u1"));

        var result = await _service.ProcessWebhookPayloadAsync(PagePayload(optin));

        result.NewConversationCount.Should().Be(1);
        CapturedThreads().Should().ContainSingle().Which.Body.Should().Contain("PLUGINS_REF_1");
    }

    [Fact]
    public async Task ProcessWebhookPayloadAsync_ReactionOnly_IsNotPersisted()
    {
        var reaction = Messaging(ReactionMessage("👍", "m_target"));

        var result = await _service.ProcessWebhookPayloadAsync(PagePayload(reaction));

        result.NewConversationCount.Should().Be(0);
        result.ReplyCount.Should().Be(0);
        _mockThreadRepo.Verify(r => r.AddAsync(It.IsAny<ConversationThread>()), Times.Never);
    }

    #endregion

    #region 媒体消息

    [Fact]
    public async Task ProcessWebhookPayloadAsync_ImageAttachment_DownloadsImmediatelyAndAttaches()
    {
        SetupNewCustomer();
        SetupNoActiveConversation();
        SetupConversationInsert(500);
        CaptureThread(600);

        var image = Messaging(AttachmentMessage("m_img", "image", "https://scontent.example.com/a.png", "att_1", 3));
        _mockClient.Setup(c => c.DownloadMediaAsync("https://scontent.example.com/a.png"))
            .ReturnsAsync((new byte[] { 1, 2, 3, 4 }, "image/png"));

        var result = await _service.ProcessWebhookPayloadAsync(PagePayload(image));

        result.NewConversationCount.Should().Be(1);
        _mockClient.Verify(c => c.DownloadMediaAsync("https://scontent.example.com/a.png"), Times.Once);
        _mockAttachmentRepo.Verify(a => a.AddAsync(It.Is<Attachment>(att =>
            att.Type == AttachmentType.Image && att.MimeType == "image/png" && att.Size == 4)), Times.Once);
    }

    [Fact]
    public async Task ProcessWebhookPayloadAsync_LocationAttachment_CreatesThreadWithoutDownload()
    {
        SetupNewCustomer();
        SetupNoActiveConversation();
        SetupConversationInsert(500);
        CaptureThread(600);

        var location = Messaging(LocationMessage("m_loc", 31.23, 121.47));

        var result = await _service.ProcessWebhookPayloadAsync(PagePayload(location));

        result.NewConversationCount.Should().Be(1);
        _mockClient.Verify(c => c.DownloadMediaAsync(It.IsAny<string>()), Times.Never);
        CapturedThreads().Should().ContainSingle().Which.Body.Should().Contain("31.23");
    }

    [Fact]
    public async Task ProcessWebhookPayloadAsync_MediaDownloadFails_ThreadStillCreated()
    {
        SetupNewCustomer();
        SetupNoActiveConversation();
        SetupConversationInsert(500);
        CaptureThread(600);

        var image = Messaging(AttachmentMessage("m_img_404", "image", "https://scontent.example.com/expired.png"));
        _mockClient.Setup(c => c.DownloadMediaAsync(It.IsAny<string>())).ReturnsAsync(((byte[] data, string? mimeType)?)null);

        var result = await _service.ProcessWebhookPayloadAsync(PagePayload(image));

        result.NewConversationCount.Should().Be(1);
        result.ErrorMessage.Should().BeNull();
        _mockAttachmentRepo.Verify(a => a.AddAsync(It.IsAny<Attachment>()), Times.Never);
        _mockTransaction.Verify(t => t.CommitAsync(), Times.Once);
    }

    [Fact]
    public async Task ProcessWebhookPayloadAsync_MediaOverCap_NotWrittenToDisk()
    {
        SetupSettings(new MessengerOptions
        {
            PageId = PageId, PageAccessToken = "t", AppSecret = "s", VerifyToken = "v",
            Enabled = true, MailboxId = 1, MediaDownloadMaxMB = 1, ContactCollectionEnabled = false, MarkSeen = false
        });
        SetupNewCustomer();
        SetupNoActiveConversation();
        SetupConversationInsert(500);
        CaptureThread(600);

        var big = Messaging(AttachmentMessage("m_big", "file", "https://download.example.com/big.bin"));
        _mockClient.Setup(c => c.DownloadMediaAsync(It.IsAny<string>()))
            .ReturnsAsync((new byte[1024 * 1024 + 1], "application/octet-stream"));

        await _service.ProcessWebhookPayloadAsync(PagePayload(big));

        _mockAttachmentRepo.Verify(a => a.AddAsync(It.IsAny<Attachment>()), Times.Never);
    }

    #endregion

    #region 回执（delivery / read）

    [Fact]
    public async Task ProcessWebhookPayloadAsync_DeliveryReceipt_UpdatesMatchingThread()
    {
        var existing = new ConversationThread
        {
            Id = 900, ConversationId = 500, Type = ThreadType.Message, State = ThreadState.Active,
            SourceType = SourceType.Messenger, MessageId = "m_replied", From = PageId, To = Psid,
            CreatedAt = DateTime.UtcNow.AddMinutes(-30), UpdatedAt = DateTime.UtcNow.AddMinutes(-30)
        };
        _threads.Add(existing);

        var receiptPayload = EntryReceiptPayload("delivery", new[] { "m_replied" }, watermark: 1763000001000, entryTime: 1763000001000);

        var result = await _service.ProcessWebhookPayloadAsync(receiptPayload);

        result.ReceiptCount.Should().Be(1);
        result.NewConversationCount.Should().Be(0);
        existing.UpdatedAt.Should().BeOnOrAfter(existing.CreatedAt);
        _mockUnitOfWork.Verify(u => u.SaveChangesAsync(), Times.AtLeastOnce);
    }

    [Fact]
    public async Task ProcessWebhookPayloadAsync_WatermarkOnlyReceipt_CountedWithoutThreadLookup()
    {
        var payload = EntryReceiptPayload("read", mids: null, watermark: 1763000002000, entryTime: 1763000002000);

        var result = await _service.ProcessWebhookPayloadAsync(payload);

        result.ReceiptCount.Should().Be(1);
        result.NewConversationCount.Should().Be(0);
        _mockThreadRepo.Verify(r => r.AddAsync(It.IsAny<ConversationThread>()), Times.Never);
    }

    [Fact]
    public async Task ProcessWebhookPayloadAsync_ReceiptForUnknownMid_DoesNotThrow()
    {
        var payload = EntryReceiptPayload("delivery", new[] { "m_unknown_mid" }, watermark: null, entryTime: 1763000003000);

        var result = await _service.ProcessWebhookPayloadAsync(payload);

        result.ReceiptCount.Should().Be(1);
        result.ErrorMessage.Should().BeNull();
    }

    [Fact]
    public async Task ProcessWebhookPayloadAsync_MessagingLevelDeliveryReceipt_CountedWithoutCreatingConversation()
    {
        // messaging[].delivery 是回执的另一种落位（ParseReceipts 同时支持 entry 级与 messaging 级）：
        // 两种落位都要计入 ReceiptCount，且都不该新建会话或回复。
        var existing = new ConversationThread
        {
            Id = 901, ConversationId = 500, Type = ThreadType.Message, State = ThreadState.Active,
            SourceType = SourceType.Messenger, MessageId = "m_replied_messaging", From = PageId, To = Psid,
            CreatedAt = DateTime.UtcNow.AddMinutes(-30), UpdatedAt = DateTime.UtcNow.AddMinutes(-30)
        };
        _threads.Add(existing);

        var payload = PagePayload(Messaging(ReceiptEvent("delivery", new[] { "m_replied_messaging" }, 1763000004000)));

        var result = await _service.ProcessWebhookPayloadAsync(payload);

        result.ReceiptCount.Should().Be(1);
        result.NewConversationCount.Should().Be(0);
        result.ReplyCount.Should().Be(0);
        _mockThreadRepo.Verify(r => r.AddAsync(It.IsAny<ConversationThread>()), Times.Never);
        _mockConversationRepo.Verify(r => r.AddAsync(It.IsAny<Conversation>()), Times.Never);
        _mockUnitOfWork.Verify(u => u.SaveChangesAsync(), Times.AtLeastOnce);
    }

    [Fact]
    public async Task ProcessWebhookPayloadAsync_LegacyOpsReceipt_StillCountedWithoutThreadLookupFailure()
    {
        // 官方口径现在是 delivery.mids[]，但 ParseReceipts 对旧形状 ops[].mid 的兼容必须留在入站链路上：
        // 这条用例钉住「旧形状仍被计数、并且能刷到对应线程」，防止后续按官方口径重写解析层时把它丢掉。
        var existing = new ConversationThread
        {
            Id = 902, ConversationId = 500, Type = ThreadType.Message, State = ThreadState.Active,
            SourceType = SourceType.Messenger, MessageId = "m_legacy_ops", From = PageId, To = Psid,
            CreatedAt = DateTime.UtcNow.AddMinutes(-30), UpdatedAt = DateTime.UtcNow.AddMinutes(-30)
        };
        _threads.Add(existing);

        var payload = PagePayload(Messaging(LegacyOpsReceiptEvent("delivery", "m_legacy_ops")));

        var result = await _service.ProcessWebhookPayloadAsync(payload);

        result.ReceiptCount.Should().Be(1);
        result.NewConversationCount.Should().Be(0);
        result.ErrorMessage.Should().BeNull();
        _mockThreadRepo.Verify(r => r.AddAsync(It.IsAny<ConversationThread>()), Times.Never);
        _mockUnitOfWork.Verify(u => u.SaveChangesAsync(), Times.AtLeastOnce);
    }

    #endregion

    #region 客户资料补全 / 自动回复 / 联系方式引导

    [Fact]
    public async Task ProcessWebhookPayloadAsync_ProfileEnrichment_FillsNamesFromGraph()
    {
        SetupSettings(new MessengerOptions
        {
            PageId = PageId, PageAccessToken = "t", AppSecret = "s", VerifyToken = "v",
            Enabled = true, MailboxId = 1, ContactCollectionEnabled = true, MarkSeen = false
        });
        var customer = new Customer { Id = 100, Channel = MessengerIngestionService.CustomerChannel, ChannelId = Psid };
        _mockCustomerRepo.Setup(r => r.GetOrCreateByChannelAsync(MessengerIngestionService.CustomerChannel, Psid, null))
            .ReturnsAsync(customer);
        SetupNoActiveConversation();
        SetupConversationInsert(500);
        CaptureThread(600);
        _mockClient.Setup(c => c.GetCustomerProfileAsync(Psid)).ReturnsAsync(new MessengerCustomerProfile
        {
            FirstName = "Li",
            LastName = "Wang",
            ProfilePic = "https://cdn.example.com/pic.png",
            Locale = "zh_CN"
        });

        await _service.ProcessWebhookPayloadAsync(PagePayload(Messaging(TextMessage("m_profile", "你好"))));

        customer.FirstName.Should().Be("Li");
        customer.Avatar.Should().Be("https://cdn.example.com/pic.png");
    }

    [Fact]
    public async Task ProcessWebhookPayloadAsync_ProfileLookupFails_DoesNotBreakIngestion()
    {
        SetupSettings(new MessengerOptions
        {
            PageId = PageId, PageAccessToken = "t", AppSecret = "s", VerifyToken = "v",
            Enabled = true, MailboxId = 1, ContactCollectionEnabled = true, MarkSeen = false
        });
        SetupNewCustomer();
        SetupNoActiveConversation();
        SetupConversationInsert(500);
        CaptureThread(600);
        _mockClient.Setup(c => c.GetCustomerProfileAsync(It.IsAny<string>()))
            .ThrowsAsync(new HttpRequestException("graph unreachable"));

        var result = await _service.ProcessWebhookPayloadAsync(PagePayload(Messaging(TextMessage("m_profile_fail", "你好"))));

        result.NewConversationCount.Should().Be(1);
        result.ErrorMessage.Should().BeNull();
    }

    [Fact]
    public async Task ProcessWebhookPayloadAsync_AutoReplyForNewConversation_SendsAndPersistsSystemThread()
    {
        SetupSettings(new MessengerOptions
        {
            PageId = PageId, PageAccessToken = "t", AppSecret = "s", VerifyToken = "v",
            Enabled = true, MailboxId = 1, AutoReplyEnabled = true, AutoReplyMessage = "已收到，客服稍后回复",
            ContactCollectionEnabled = false, MarkSeen = false, TypingIndicator = true
        });
        SetupNewCustomer();
        SetupNoActiveConversation();
        SetupConversationInsert(500);
        CaptureThread(600);
        _mockClient.Setup(c => c.SendTextMessageAsync(Psid, "已收到，客服稍后回复", null, null)).ReturnsAsync("m_auto_reply");

        var result = await _service.ProcessWebhookPayloadAsync(PagePayload(Messaging(TextMessage("m_auto_src", "在吗"))));

        result.AutoReplyContent.Should().Be("已收到，客服稍后回复");
        _mockClient.Verify(c => c.SendTextMessageAsync(Psid, "已收到，客服稍后回复", null, null), Times.Once);
        CapturedThreads().Should().HaveCount(2);
        CapturedThreads()[1].From.Should().Be("messenger@system");
        CapturedThreads()[1].MessageId.Should().StartWith("messenger-auto-reply-");
    }

    [Fact]
    public async Task ProcessWebhookPayloadAsync_AutoReplyNotSentForExistingConversation()
    {
        SetupSettings(new MessengerOptions
        {
            PageId = PageId, PageAccessToken = "t", AppSecret = "s", VerifyToken = "v",
            Enabled = true, MailboxId = 1, AutoReplyEnabled = true, AutoReplyMessage = "欢迎",
            ContactCollectionEnabled = false, MarkSeen = false
        });
        SetupExistingConversation(500);
        _mockCustomerRepo.Setup(r => r.GetOrCreateByChannelAsync(MessengerIngestionService.CustomerChannel, Psid, null))
            .ReturnsAsync(NewCustomer());
        CaptureThread(601);

        var result = await _service.ProcessWebhookPayloadAsync(PagePayload(Messaging(TextMessage("m_second_in_window", "在吗"))));

        result.AutoReplyContent.Should().BeNull();
        _mockClient.Verify(c => c.SendTextMessageAsync(It.IsAny<string>(), It.IsAny<string>(), It.IsAny<string?>(), It.IsAny<string?>()), Times.Never);
    }

    [Fact]
    public async Task ProcessWebhookPayloadAsync_NewConversationWithoutContactInfo_SendsGuidance()
    {
        SetupSettings(new MessengerOptions
        {
            PageId = PageId, PageAccessToken = "t", AppSecret = "s", VerifyToken = "v",
            Enabled = true, MailboxId = 1, ContactCollectionEnabled = true, MarkSeen = false
        });
        var customer = new Customer { Id = 100, Channel = MessengerIngestionService.CustomerChannel, ChannelId = Psid };
        _mockCustomerRepo.Setup(r => r.GetOrCreateByChannelAsync(MessengerIngestionService.CustomerChannel, Psid, null))
            .ReturnsAsync(customer);
        SetupNoActiveConversation();
        SetupConversationInsert(500);
        CaptureThread(600);
        _mockClient.Setup(c => c.SendTextMessageAsync(It.IsAny<string>(), It.IsAny<string>(), It.IsAny<string?>(), It.IsAny<string?>()))
            .ReturnsAsync("m_guidance");

        await _service.ProcessWebhookPayloadAsync(PagePayload(Messaging(TextMessage("m_guidance_src", "我想咨询"))));

        // 姓名/手机/邮箱任一缺失的新会话 → 发送联系方式收集引导（与 WhatsApp 行为一致）
        _mockClient.Verify(c => c.SendTextMessageAsync(Psid, It.Is<string>(s => s.Contains("姓名")), null, null), Times.Once);
        CapturedThreads().Should().Contain(t => t.From == "messenger@system" && t.MessageId!.StartsWith("messenger-contact-guidance-"));
    }

    #endregion

    #region 批量与多 entry

    [Fact]
    public async Task ProcessWebhookPayloadAsync_TwoMessagesInOnePost_BothIntoSameConversation()
    {
        SetupExistingConversation(500);
        _mockCustomerRepo.Setup(r => r.GetOrCreateByChannelAsync(MessengerIngestionService.CustomerChannel, Psid, null))
            .ReturnsAsync(NewCustomer());
        CaptureThread(700);

        // 单条 POST 内两条 messaging（Meta 单次最多可推 1000 条）
        var twoMessages = string.Join(",", new[]
        {
            Messaging(TextMessage("m_batch_1", "第一句")),
            Messaging(TextMessage("m_batch_2", "第二句"))
        });

        var result = await _service.ProcessWebhookPayloadAsync(PagePayload(twoMessages));

        result.ReplyCount.Should().Be(2);
        result.NewConversationCount.Should().Be(0);
        _threads.Should().HaveCount(2);
        _threads.Select(t => t.MessageId).Should().BeEquivalentTo(new[] { "m_batch_1", "m_batch_2" });
        _mockConversationRepo.Verify(r => r.AddAsync(It.IsAny<Conversation>()), Times.Never);
    }

    [Fact]
    public async Task ProcessWebhookPayloadAsync_MultipleEntries_ProcessedAcrossEntries()
    {
        SetupNewCustomer();
        SetupNoActiveConversation();
        SetupConversationInsert(500);
        CaptureThread(600);

        var payload = PagePayloadEntries(
            (PageId, 1763000000000, Messaging(TextMessage("m_e1", "入口一"))),
            (PageId, 1763000001000, Messaging(TextMessage("m_e2", "入口二"))));

        await _service.ProcessWebhookPayloadAsync(payload);

        _threads.Should().HaveCount(2);
        _threads.Select(t => t.MessageId).Should().BeEquivalentTo(new[] { "m_e1", "m_e2" });
    }

    #endregion
}
