using System.Text;
using FluentAssertions;
using NoneadCSS.Application.Common;
using Xunit;

namespace NoneadCSS.Tests.Services;

/// <summary>
/// MessengerWebhookParser 单元测试 - X-Hub-Signature-256 签名验证、GET 订阅校验、Page Webhook 载荷解析。
/// 覆盖点（docs/integration/channel-integration-playbook.md 第 7 节）：
/// - 签名：正确/错误/缺失头/错误前缀/篡改 body/空 AppSecret/大小写前缀；
/// - GET 校验：challenge 原样回显、mode 不符、token 不符、未配置 token；
/// - 解析：文本/图片/定位/postback/optin、多 entry 多 messaging、mid 与 seq、时间戳换算；
/// - 过滤：is_echo、sender.id == Page id、read/delivery 回执、空消息、缺 sender.id。
/// </summary>
public class MessengerWebhookParserTests
{
    private const string TestAppSecret = "test_app_secret_123456";
    private const string PageId = "1055123456789012";
    private const string Psid = "1234567890123456";

    private static string Sign(string body, string secret = TestAppSecret)
    {
        using var hmac = new System.Security.Cryptography.HMACSHA256(Encoding.UTF8.GetBytes(secret));
        return "sha256=" + Convert.ToHexString(hmac.ComputeHash(Encoding.UTF8.GetBytes(body))).ToLowerInvariant();
    }

    #region VerifySignature

    [Fact]
    public void VerifySignature_ValidSignature_ReturnsTrue()
    {
        var body = Encoding.UTF8.GetBytes("{\"object\":\"page\",\"entry\":[]}");
        var signature = Sign("{\"object\":\"page\",\"entry\":[]}");

        MessengerWebhookParser.VerifySignature(body, TestAppSecret, signature).Should().BeTrue();
    }

    [Fact]
    public void VerifySignature_WrongSecret_ReturnsFalse()
    {
        var bodyText = "{\"object\":\"page\",\"entry\":[]}";
        var body = Encoding.UTF8.GetBytes(bodyText);
        var signature = Sign(bodyText, "other_secret");

        MessengerWebhookParser.VerifySignature(body, TestAppSecret, signature).Should().BeFalse();
    }

    [Fact]
    public void VerifySignature_TamperedBody_ReturnsFalse()
    {
        // 真实 body 与签名时的 body 不一致 => 必须判定失败（防伪造）
        var signature = Sign("{\"object\":\"page\",\"entry\":[{\"messaging\":[{\"message\":{\"text\":\"hi\"}}]}]}");
        var tampered = Encoding.UTF8.GetBytes("{\"object\":\"page\",\"entry\":[{\"messaging\":[{\"message\":{\"text\":\"evil\"}}]}]}");

        MessengerWebhookParser.VerifySignature(tampered, TestAppSecret, signature).Should().BeFalse();
    }

    [Fact]
    public void VerifySignature_MissingHeader_ReturnsFalse()
        => MessengerWebhookParser.VerifySignature(Encoding.UTF8.GetBytes("body"), TestAppSecret, null).Should().BeFalse();

    [Fact]
    public void VerifySignature_EmptyAppSecret_ReturnsFalse()
        => MessengerWebhookParser.VerifySignature(Encoding.UTF8.GetBytes("body"), "", "sha256=deadbeef").Should().BeFalse();

    [Fact]
    public void VerifySignature_LegacySha1Prefix_ReturnsFalse()
        => MessengerWebhookParser.VerifySignature(Encoding.UTF8.GetBytes("body"), TestAppSecret, "sha1=deadbeef").Should().BeFalse();

    [Fact]
    public void VerifySignature_EmptySignatureValue_ReturnsFalse()
        => MessengerWebhookParser.VerifySignature(Encoding.UTF8.GetBytes("body"), TestAppSecret, "sha256=").Should().BeFalse();

    [Fact]
    public void VerifySignature_PrefixCaseInsensitive_ReturnsTrue()
    {
        var bodyText = "{\"object\":\"page\"}";
        var body = Encoding.UTF8.GetBytes(bodyText);
        var signature = "SHA256=" + Sign(bodyText)["sha256=".Length..];

        MessengerWebhookParser.VerifySignature(body, TestAppSecret, signature).Should().BeTrue();
    }

    [Fact]
    public void VerifySignature_UppercaseHex_ReturnsTrue()
    {
        var bodyText = "{\"object\":\"page\"}";
        var body = Encoding.UTF8.GetBytes(bodyText);
        using var hmac = new System.Security.Cryptography.HMACSHA256(Encoding.UTF8.GetBytes(TestAppSecret));
        var signature = "sha256=" + Convert.ToHexString(hmac.ComputeHash(body)); // 大写 hex

        MessengerWebhookParser.VerifySignature(body, TestAppSecret, signature).Should().BeTrue();
    }

    [Fact]
    public void VerifySignature_ShortenedSignature_ReturnsFalse()
    {
        var bodyText = "{\"object\":\"page\"}";
        var body = Encoding.UTF8.GetBytes(bodyText);
        var signature = Sign(bodyText)[..20]; // 截断，长度不同

        MessengerWebhookParser.VerifySignature(body, TestAppSecret, signature).Should().BeFalse();
    }

    [Fact]
    public void VerifySignature_EmptyBodyWithMatchingSignature_ReturnsTrue()
        => MessengerWebhookParser.VerifySignature(Array.Empty<byte>(), TestAppSecret, Sign("")).Should().BeTrue();

    [Fact]
    public void VerifySignature_UnicodeBody_ComputesOverUtf8Bytes()
    {
        // 含多字节字符时签名必须按 UTF-8 字节而非 char 计算
        var bodyText = "{\"object\":\"page\",\"entry\":[{\"messaging\":[{\"message\":{\"text\":\"你好，客服\"}}]}]}";
        var body = Encoding.UTF8.GetBytes(bodyText);

        MessengerWebhookParser.VerifySignature(body, TestAppSecret, Sign(bodyText)).Should().BeTrue();
        MessengerWebhookParser.VerifySignature(Encoding.Unicode.GetBytes(bodyText), TestAppSecret, Sign(bodyText)).Should().BeFalse();
    }

    #endregion

    #region VerifyWebhookSubscription

    [Theory]
    [InlineData("subscribe")]
    [InlineData("SUBSCRIBE")]
    public void VerifyWebhookSubscription_MatchingToken_EchoesChallenge(string mode)
    {
        var challenge = MessengerWebhookParser.VerifyWebhookSubscription(mode, "my_verify_token", "1158201444", "my_verify_token");

        challenge.Should().Be("1158201444");
    }

    [Fact]
    public void VerifyWebhookSubscription_WrongMode_ReturnsNull()
        => MessengerWebhookParser.VerifyWebhookSubscription("unsubscribe", "t", "challenge", "t").Should().BeNull();

    [Fact]
    public void VerifyWebhookSubscription_WrongToken_ReturnsNull()
        => MessengerWebhookParser.VerifyWebhookSubscription("subscribe", "wrong", "challenge", "correct").Should().BeNull();

    [Fact]
    public void VerifyWebhookSubscription_TokenCaseSensitive_ReturnsNull()
        => MessengerWebhookParser.VerifyWebhookSubscription("subscribe", "MyToken", "challenge", "mytoken").Should().BeNull();

    [Fact]
    public void VerifyWebhookSubscription_NotConfigured_ReturnsNull()
        => MessengerWebhookParser.VerifyWebhookSubscription("subscribe", "any", "challenge", null).Should().BeNull();

    [Fact]
    public void VerifyWebhookSubscription_MissingIncomingToken_ReturnsNull()
        => MessengerWebhookParser.VerifyWebhookSubscription("subscribe", null, "challenge", "configured").Should().BeNull();

    [Fact]
    public void VerifyWebhookSubscription_NullMode_ReturnsNull()
        => MessengerWebhookParser.VerifyWebhookSubscription(null, "t", "challenge", "t").Should().BeNull();

    #endregion

    #region IsMessengerWebhook

    [Fact]
    public void IsMessengerWebhook_PageObject_ReturnsTrue()
        => MessengerWebhookParser.IsMessengerWebhook("{\"object\":\"page\",\"entry\":[]}").Should().BeTrue();

    [Fact]
    public void IsMessengerWebhook_OtherObject_ReturnsFalse()
        => MessengerWebhookParser.IsMessengerWebhook("{\"object\":\"whatsapp_business_account\",\"entry\":[]}").Should().BeFalse();

    [Theory]
    [InlineData("")]
    [InlineData(null)]
    [InlineData("not json")]
    [InlineData("{}")]
    public void IsMessengerWebhook_MalformedOrEmpty_ReturnsFalse(string? payload)
        => MessengerWebhookParser.IsMessengerWebhook(payload).Should().BeFalse();

    #endregion

    #region ParseMessages - 文本

    [Fact]
    public void ParseMessages_TextMessage_ReturnsParsedMessage()
    {
        var payload = $$"""
        {
          "object": "page",
          "entry": [{
            "id": "{{PageId}}",
            "time": 1700000000000,
            "messaging": [{
              "sender": { "id": "{{Psid}}" },
              "recipient": { "id": "{{PageId}}" },
              "timestamp": 1700000000123,
              "message": { "mid": "m_eJd5n4qNsw", "seq": 7, "text": "你好，人工客服在吗" }
            }]
          }]
        }
        """;

        var messages = MessengerWebhookParser.ParseMessages(payload);

        messages.Should().HaveCount(1);
        var msg = messages[0];
        msg.MessageId.Should().Be("m_eJd5n4qNsw");
        msg.From.Should().Be(Psid);
        msg.PageId.Should().Be(PageId);
        msg.MessageType.Should().Be("text");
        msg.TextBody.Should().Be("你好，人工客服在吗");
        msg.Seq.Should().Be(7);
        msg.TimestampMs.Should().Be(1700000000123L);
        msg.Timestamp.Should().Be(1700000000L);
        msg.IsEcho.Should().BeFalse();
    }

    [Fact]
    public void ParseMessages_MultipleEntriesAndMessaging_ReturnsAll()
    {
        var payload = $$"""
        {
          "object": "page",
          "entry": [
            { "id": "{{PageId}}", "messaging": [
                { "sender": { "id": "{{Psid}}" }, "recipient": { "id": "{{PageId}}" }, "timestamp": 1700000000001, "message": { "mid": "mid_1", "text": "a" } },
                { "sender": { "id": "{{Psid}}" }, "recipient": { "id": "{{PageId}}" }, "timestamp": 1700000000002, "message": { "mid": "mid_2", "text": "b" } }
            ]},
            { "id": "{{PageId}}", "messaging": [
                { "sender": { "id": "9998887776665554" }, "recipient": { "id": "{{PageId}}" }, "timestamp": 1700000000003, "message": { "mid": "mid_3", "text": "c" } }
            ]}
          ]
        }
        """;

        MessengerWebhookParser.ParseMessages(payload).Should().HaveCount(3);
    }

    [Fact]
    public void ParseMessages_QuickReply_CapturesPayload()
    {
        var payload = $$"""
        {
          "object": "page",
          "entry": [{ "id": "{{PageId}}", "messaging": [{
            "sender": { "id": "{{Psid}}" }, "recipient": { "id": "{{PageId}}" }, "timestamp": 1700000000000,
            "message": { "mid": "mid_qr", "text": "查订单", "quick_reply": { "payload": "PAY_ORDER" } }
          }]}]
        }
        """;

        var msg = MessengerWebhookParser.ParseMessages(payload).Single();
        msg.MessageType.Should().Be("quick_reply");
        msg.QuickReplyPayload.Should().Be("PAY_ORDER");
    }

    #endregion

    #region ParseMessages - 附件

    [Fact]
    public void ParseMessages_ImageAttachment_ReturnsMediaInfo()
    {
        var payload = $$"""
        {
          "object": "page",
          "entry": [{ "id": "{{PageId}}", "messaging": [{
            "sender": { "id": "{{Psid}}" }, "recipient": { "id": "{{PageId}}" }, "timestamp": 1700000000000,
            "message": {
              "mid": "mid_img",
              "attachments": [{ "type": "image", "payload": { "url": "https://scontent.xx/a.jpg", "attachment_id": "ATT123", "title": "订单截图" } }]
            }
          }]}]
        }
        """;

        var msg = MessengerWebhookParser.ParseMessages(payload).Single();
        msg.MessageType.Should().Be("image");
        msg.AttachmentType.Should().Be("image");
        msg.MediaUrl.Should().Be("https://scontent.xx/a.jpg");
        msg.AttachmentId.Should().Be("ATT123");
        msg.Title.Should().Be("订单截图");
    }

    [Fact]
    public void ParseMessages_LocationAttachment_ReturnsCoordinates()
    {
        var payload = $$"""
        {
          "object": "page",
          "entry": [{ "id": "{{PageId}}", "messaging": [{
            "sender": { "id": "{{Psid}}" }, "recipient": { "id": "{{PageId}}" }, "timestamp": 1700000000000,
            "message": {
              "mid": "mid_loc",
              "attachments": [{ "type": "location", "payload": { "title": "天安门", "coordinates": { "lat": 39.9042, "long": 116.4074 } } }]
            }
          }]}]
        }
        """;

        var msg = MessengerWebhookParser.ParseMessages(payload).Single();
        msg.MessageType.Should().Be("location");
        msg.Latitude.Should().BeApproximately(39.9042, 0.0001);
        msg.Longitude.Should().BeApproximately(116.4074, 0.0001);
        msg.Title.Should().Be("天安门");
    }

    [Fact]
    public void ParseMessages_FallbackAttachment_KeepsUrlAsText()
    {
        var payload = $$"""
        {
          "object": "page",
          "entry": [{ "id": "{{PageId}}", "messaging": [{
            "sender": { "id": "{{Psid}}" }, "recipient": { "id": "{{PageId}}" }, "timestamp": 1700000000000,
            "message": { "mid": "mid_fb", "attachments": [{ "type": "fallback", "payload": { "title": "分享链接", "url": "https://messenger.com/x" } }] }
          }]}]
        }
        """;

        var msg = MessengerWebhookParser.ParseMessages(payload).Single();
        msg.MessageType.Should().Be("fallback");
        msg.TextBody.Should().Be("https://messenger.com/x");
    }

    #endregion

    #region ParseMessages - postback / optin

    [Fact]
    public void ParseMessages_Postback_SynthesizesIdempotencyKey()
    {
        var payload = $$"""
        {
          "object": "page",
          "entry": [{ "id": "{{PageId}}", "messaging": [{
            "sender": { "id": "{{Psid}}" }, "recipient": { "id": "{{PageId}}" }, "timestamp": 1700000000000,
            "postback": { "title": "开始咨询", "payload": "START_CHAT", "data": "{\"ref\":\"abc\"}" }
          }]}]
        }
        """;

        var msg = MessengerWebhookParser.ParseMessages(payload).Single();
        msg.MessageType.Should().Be("postback");
        msg.PostbackPayload.Should().Be("START_CHAT");
        msg.PostbackTitle.Should().Be("开始咨询");
        msg.QuickReplyPayload.Should().Be("{\"ref\":\"abc\"}");
        // postback 无 mid => 合成键须稳定（Meta 重试时同键去重）
        msg.MessageId.Should().Be($"msg_postback_{PageId}_{Psid}_1700000000000");
    }

    [Fact]
    public void ParseMessages_Optin_ReturnsRef()
    {
        var payload = $$"""
        {
          "object": "page",
          "entry": [{ "id": "{{PageId}}", "messaging": [{
            "sender": { "id": "{{Psid}}" }, "recipient": { "id": "{{PageId}}" }, "timestamp": 1700000000000,
            "optin": { "ref": "REF_42", "user_ref": "u1" }
          }]}]
        }
        """;

        var msg = MessengerWebhookParser.ParseMessages(payload).Single();
        msg.MessageType.Should().Be("optin");
        msg.OptinRef.Should().Be("REF_42");
        msg.MessageId.Should().Be($"msg_optin_{PageId}_{Psid}_1700000000000");
    }

    [Fact]
    public void ParseMessages_OptinWithoutRef_ReturnsEmpty()
    {
        var payload = $$"""
        { "object": "page", "entry": [{ "id": "{{PageId}}", "messaging": [
          { "sender": { "id": "{{Psid}}" }, "recipient": { "id": "{{PageId}}" }, "timestamp": 1, "optin": { "type": "checkbox" } }
        ]}]}
        """;

        MessengerWebhookParser.ParseMessages(payload).Should().BeEmpty();
    }

    #endregion

    #region ParseMessages - 过滤规则

    [Fact]
    public void ParseMessages_IsEcho_Skipped()
    {
        var payload = $$"""
        {
          "object": "page",
          "entry": [{ "id": "{{PageId}}", "messaging": [{
            "sender": { "id": "{{Psid}}" }, "recipient": { "id": "{{PageId}}" }, "timestamp": 1700000000000,
            "message": { "is_echo": true, "app_id": 123, "mid": "m_echo", "text": "坐席已回复" }
          }]}]
        }
        """;

        MessengerWebhookParser.ParseMessages(payload).Should().BeEmpty();
    }

    [Fact]
    public void ParseMessages_SenderIsPage_Skipped()
    {
        var payload = $$"""
        {
          "object": "page",
          "entry": [{ "id": "{{PageId}}", "messaging": [{
            "sender": { "id": "{{PageId}}" }, "recipient": { "id": "{{PageId}}" }, "timestamp": 1700000000000,
            "message": { "mid": "m_self", "text": "Page 自己发的" }
          }]}]
        }
        """;

        MessengerWebhookParser.ParseMessages(payload).Should().BeEmpty();
    }

    [Fact]
    public void ParseMessages_ReadAndDelivery_Skipped()
    {
        var payload = $$"""
        {
          "object": "page",
          "entry": [{ "id": "{{PageId}}", "messaging": [
            { "sender": { "id": "{{Psid}}" }, "recipient": { "id": "{{PageId}}" }, "timestamp": 1700000000000, "read": { "watermark": 1700000000000 } },
            { "sender": { "id": "{{Psid}}" }, "recipient": { "id": "{{PageId}}" }, "timestamp": 1700000000001, "delivery": { "watermark": 1700000000001, "ops": [{ "mid": "m_1" }] } }
          ]}]
        }
        """;

        MessengerWebhookParser.ParseMessages(payload).Should().BeEmpty();
    }

    [Fact]
    public void ParseMessages_ReactionAndHandover_Skipped()
    {
        var payload = $$"""
        {
          "object": "page",
          "entry": [{ "id": "{{PageId}}", "messaging": [
            { "sender": { "id": "{{Psid}}" }, "recipient": { "id": "{{PageId}}" }, "timestamp": 1, "reaction": { "mid": "m_1", "reaction_type": "like" } },
            { "sender": { "id": "{{Psid}}" }, "recipient": { "id": "{{PageId}}" }, "timestamp": 2, "handover_action": "APPS_REVIEWING" }
          ]}]
        }
        """;

        MessengerWebhookParser.ParseMessages(payload).Should().BeEmpty();
    }

    [Fact]
    public void ParseMessages_EmptyMessageObject_Skipped()
    {
        var payload = $$"""
        { "object": "page", "entry": [{ "id": "{{PageId}}", "messaging": [
          { "sender": { "id": "{{Psid}}" }, "recipient": { "id": "{{PageId}}" }, "timestamp": 1, "message": { "mid": "m_empty" } }
        ]}]}
        """;

        MessengerWebhookParser.ParseMessages(payload).Should().BeEmpty();
    }

    [Fact]
    public void ParseMessages_MissingSender_Skipped()
    {
        var payload = $$"""
        { "object": "page", "entry": [{ "id": "{{PageId}}", "messaging": [
          { "recipient": { "id": "{{PageId}}" }, "timestamp": 1, "message": { "mid": "m_nosender", "text": "hi" } }
        ]}]}
        """;

        MessengerWebhookParser.ParseMessages(payload).Should().BeEmpty();
    }

    [Fact]
    public void ParseMessages_MissingMid_SynthesizesStableKey()
    {
        var payload = $$"""
        { "object": "page", "entry": [{ "id": "{{PageId}}", "messaging": [
          { "sender": { "id": "{{Psid}}" }, "recipient": { "id": "{{PageId}}" }, "timestamp": 1700000000456, "message": { "text": "无 mid" } }
        ]}]}
        """;

        var msg = MessengerWebhookParser.ParseMessages(payload).Single();
        msg.MessageId.Should().Be($"msg_message_{PageId}_{Psid}_1700000000456");
    }

    [Fact]
    public void ParseMessages_EntryWithoutMessaging_ReturnsEmpty()
        => MessengerWebhookParser.ParseMessages($"{{\"object\":\"page\",\"entry\":[{{\"id\":\"{PageId}\"}}]}}").Should().BeEmpty();

    [Theory]
    [InlineData("")]
    [InlineData(null)]
    public void ParseMessages_EmptyPayload_ReturnsEmpty(string? payload)
        => MessengerWebhookParser.ParseMessages(payload).Should().BeEmpty();

    #endregion

    #region CountIgnoredEchoes

    [Fact]
    public void CountIgnoredEchoes_CountsEchoAndSelfOnly()
    {
        var payload = $$"""
        {
          "object": "page",
          "entry": [{ "id": "{{PageId}}", "messaging": [
            { "sender": { "id": "{{Psid}}" }, "recipient": { "id": "{{PageId}}" }, "timestamp": 1, "message": { "is_echo": true, "mid": "m_1", "text": "a" } },
            { "sender": { "id": "{{PageId}}" }, "recipient": { "id": "{{PageId}}" }, "timestamp": 2, "message": { "mid": "m_2", "text": "b" } },
            { "sender": { "id": "{{Psid}}" }, "recipient": { "id": "{{PageId}}" }, "timestamp": 3, "message": { "mid": "m_3", "text": "c" } }
          ]}]
        }
        """;

        MessengerWebhookParser.CountIgnoredEchoes(payload).Should().Be(2);
        MessengerWebhookParser.ParseMessages(payload).Should().HaveCount(1);
    }

    [Fact]
    public void CountIgnoredEchoes_EmptyPayload_ReturnsZero()
        => MessengerWebhookParser.CountIgnoredEchoes("").Should().Be(0);

    #endregion

    #region ParseReceipts

    [Fact]
    public void ParseReceipts_DeliveryOps_ReturnsPerMessage()
    {
        var payload = $$"""
        {
          "object": "page",
          "entry": [{ "id": "{{PageId}}", "messaging": [{
            "sender": { "id": "{{Psid}}" }, "recipient": { "id": "{{PageId}}" }, "timestamp": 1700000000000,
            "delivery": { "watermark": 1700000000000, "ops": [{ "mid": "m_a", "status": "delivered" }, { "mid": "m_b" }] }
          }]}]
        }
        """;

        var receipts = MessengerWebhookParser.ParseReceipts(payload);

        receipts.Should().HaveCount(2);
        receipts[0].MessageId.Should().Be("m_a");
        receipts[0].Status.Should().Be("delivered");
        receipts[0].WatermarkMs.Should().Be(1700000000000L);
        receipts[0].Timestamp.Should().Be(1700000000L);
        receipts[1].MessageId.Should().Be("m_b");
    }

    [Fact]
    public void ParseReceipts_WatermarkOnly_ReturnsSingleEntryWithoutMid()
    {
        var payload = $$"""
        { "object": "page", "entry": [{ "id": "{{PageId}}", "messaging": [
          { "sender": { "id": "{{Psid}}" }, "recipient": { "id": "{{PageId}}" }, "timestamp": 1700000000000, "read": { "watermark": 1700000123000 } }
        ]}]}
        """;

        var receipts = MessengerWebhookParser.ParseReceipts(payload);

        receipts.Should().HaveCount(1);
        receipts[0].MessageId.Should().BeEmpty();
        receipts[0].Status.Should().Be("read");
        receipts[0].WatermarkMs.Should().Be(1700000123000L);
    }

    [Fact]
    public void ParseReceipts_EntryLevelMids_ReturnsPerMessageUpdates()
    {
        // message_deliveries / message_reads 的官方落位是 entry[].delivery = {"mids":[…],"watermark":<epochMs>}，
        // 事件时间取 entry[].time（Meta 的 delivery 载荷用 mids 字符串数组，不是 ops[].mid）。
        var payload = $$"""
        { "object": "page", "entry": [{
            "id": "{{PageId}}", "time": 1700000500000,
            "delivery": { "mids": ["m_x", "m_y"], "watermark": 1700000400000 }
        }]}
        """;

        var receipts = MessengerWebhookParser.ParseReceipts(payload);

        receipts.Should().HaveCount(2);
        receipts[0].MessageId.Should().Be("m_x");
        receipts[0].Status.Should().Be("delivered");
        receipts[0].WatermarkMs.Should().Be(1700000400000L);
        receipts[0].Timestamp.Should().Be(1700000500L);
        receipts[1].MessageId.Should().Be("m_y");
    }

    [Fact]
    public void ParseReceipts_MessagingLevelMids_ReturnsPerMessageUpdate()
    {
        var payload = $$"""
        { "object": "page", "entry": [{ "id": "{{PageId}}", "messaging": [{
            "sender": { "id": "{{Psid}}" }, "recipient": { "id": "{{PageId}}" }, "timestamp": 1700000600000,
            "read": { "mids": ["m_z"] }
        }]}]}
        """;

        var receipts = MessengerWebhookParser.ParseReceipts(payload);

        receipts.Should().HaveCount(1);
        receipts[0].MessageId.Should().Be("m_z");
        receipts[0].Status.Should().Be("read");
        receipts[0].WatermarkMs.Should().BeNull();
        receipts[0].Timestamp.Should().Be(1700000600L);
    }

    [Fact]
    public void ParseReceipts_MessagesOnly_ReturnsEmpty()
    {
        var payload = $$"""
        { "object": "page", "entry": [{ "id": "{{PageId}}", "messaging": [
          { "sender": { "id": "{{Psid}}" }, "recipient": { "id": "{{PageId}}" }, "timestamp": 1, "message": { "mid": "m_1", "text": "hi" } }
        ]}]}
        """;

        MessengerWebhookParser.ParseReceipts(payload).Should().BeEmpty();
    }

    [Fact]
    public void ParseReceipts_EmptyPayload_ReturnsEmpty()
        => MessengerWebhookParser.ParseReceipts(null).Should().BeEmpty();

    #endregion

    #region ExtractPageId

    [Fact]
    public void ExtractPageId_EntryId_ReturnsPageId()
        => MessengerWebhookParser.ExtractPageId($"{{\"object\":\"page\",\"entry\":[{{\"id\":\"{PageId}\"}}]}}").Should().Be(PageId);

    [Fact]
    public void ExtractPageId_NumericEntryId_ReturnsRawText()
        => MessengerWebhookParser.ExtractPageId("{\"object\":\"page\",\"entry\":[{\"id\":1055123456789012}]}").Should().Be("1055123456789012");

    [Theory]
    [InlineData("")]
    [InlineData(null)]
    [InlineData("bad json")]
    [InlineData("{\"object\":\"page\",\"entry\":[]}")]
    public void ExtractPageId_MissingOrMalformed_ReturnsNull(string? payload)
        => MessengerWebhookParser.ExtractPageId(payload).Should().BeNull();

    #endregion

    #region SubscribeMode

    [Fact]
    public void SubscribeMode_IsSubscribe_MatchesGraphContract()
        => MessengerWebhookParser.SubscribeMode.Should().Be("subscribe");

    #endregion
}
