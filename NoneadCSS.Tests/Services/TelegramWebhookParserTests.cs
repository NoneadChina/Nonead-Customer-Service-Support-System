using FluentAssertions;
using System.Text.Json;
using NoneadCSS.Application.Common;
using NoneadCSS.Application.DTOs.Responses;

namespace NoneadCSS.Tests.Services;

/// <summary>
/// TelegramWebhookParser 单元测试（纯解析，无网络、无 IO）。
/// 覆盖：webhook secret-token 校验（fail-closed）、Update 去重键（chat.id+message_id，不用 update_id）、
/// 各消息形态解析（私聊/群/频道/callback_query/服务消息/媒体阶梯）、/start 深链参数、
/// 超长文本分片、Bot API 错误响应分类（403 blocked / 429 retry_after / 401 / 409 / migrate_to_chat_id）。
/// 依据 docs/integration/telegram.md 的字段口径构造真实样例负载。
/// </summary>
public class TelegramWebhookParserTests
{
    #region 官方样例负载

    private const string PrivateTextUpdate = """
        {
          "update_id": 987654321,
          "message": {
            "message_id": 42,
            "from": {
              "id": 5551234567890,
              "is_bot": false,
              "first_name": "张三",
              "last_name": "丰",
              "username": "zhangsf",
              "language_code": "zh-hans"
            },
            "chat": { "id": 5551234567890, "first_name": "张三", "type": "private" },
            "date": 1763100000,
            "text": "你好，我想咨询订单问题"
          }
        }
        """;

    private const string SupergroupThreadUpdate = """
        {
          "update_id": 987654322,
          "message": {
            "message_id": 1001,
            "from": { "id": 111, "is_bot": false, "first_name": "Li" },
            "chat": { "id": -1001234567890, "title": "客户群", "type": "supergroup" },
            "date": 1763100100,
            "message_thread_id": 42,
            "text": "群里提问"
          }
        }
        """;

    private const string PhotoLadderUpdate = """
        {
          "update_id": 987654323,
          "message": {
            "message_id": 2002,
            "from": { "id": 222, "is_bot": false, "first_name": "Wang" },
            "chat": { "id": 222, "type": "private" },
            "date": 1763100200,
            "photo": [
              { "file_id": "small_id", "file_unique_id": "uq_small", "file_size": 1024, "width": 90, "height": 67 },
              { "file_id": "medium_id", "file_unique_id": "uq_medium", "file_size": 20480, "width": 320, "height": 240 },
              { "file_id": "large_id", "file_unique_id": "uq_large", "file_size": 1048576, "width": 1280, "height": 960 }
            ],
            "caption": "现场照片"
          }
        }
        """;

    private const string CallbackQueryUpdate = """
        {
          "update_id": 987654324,
          "callback_query": {
            "id": "cb_abcdef123456",
            "from": { "id": 333, "is_bot": false, "first_name": "Zhao" },
            "message": {
              "message_id": 3003,
              "from": { "id": 333, "is_bot": false, "first_name": "Zhao" },
              "chat": { "id": 333, "type": "private" },
              "date": 1763100300,
              "text": "请选择服务"
            },
            "data": "menu:order_status",
            "chat_instance": "1234"
          }
        }
        """;

    #endregion

    #region Webhook secret-token 校验

    [Fact]
    public void SecretHeaderName_MatchesOfficialHeader()
        => TelegramWebhookParser.SecretHeaderName.Should().Be("X-Telegram-Bot-Api-Secret-Token");

    [Fact]
    public void VerifySecretToken_MatchingTokens_ReturnsTrue()
        => TelegramWebhookParser.VerifySecretToken("s3cr3t-token-value", "s3cr3t-token-value").Should().BeTrue();

    [Fact]
    public void VerifySecretToken_DifferentTokens_ReturnsFalse()
        => TelegramWebhookParser.VerifySecretToken("expected-token", "attacker-tokn").Should().BeFalse();

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("   ")]
    public void VerifySecretToken_NotConfigured_FailsClosedWithoutEchoingPayload(string? configuredSecret)
    {
        // 未登记密钥时一律拒绝：Telegram 没有请求体签名，任何人都能伪造 chat.id/message_id
        TelegramWebhookParser.VerifySecretToken(configuredSecret, "guessed-value").Should().BeFalse();
    }

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("  ")]
    public void VerifySecretToken_MissingHeader_FailsClosed(string? headerToken)
        => TelegramWebhookParser.VerifySecretToken("configured-secret", headerToken).Should().BeFalse();

    [Fact]
    public void VerifySecretToken_DifferentLength_ReturnsFalse()
        => TelegramWebhookParser.VerifySecretToken("long-configured-secret", "short").Should().BeFalse();

    #endregion

    #region Update 识别与游标

    [Fact]
    public void IsTelegramUpdate_RealWebhookPayload_ReturnsTrue()
        => TelegramWebhookParser.IsTelegramUpdate(PrivateTextUpdate).Should().BeTrue();

    [Fact]
    public void IsTelegramUpdate_GetUpdatesEnvelope_ReturnsFalse()
        => TelegramWebhookParser.IsTelegramUpdate("""{"ok":true,"result":[]}""").Should().BeFalse();

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("not json at all")]
    [InlineData("{")]
    [InlineData("""{"message_id":1}""")]
    [InlineData("""[1,2,3]""")]
    public void IsTelegramUpdate_NonUpdatePayload_ReturnsFalse(string? payload)
        => TelegramWebhookParser.IsTelegramUpdate(payload).Should().BeFalse();

    [Fact]
    public void IsTelegramUpdate_UpdateIdAsStringStillRecognised_ReturnsTrue()
        => TelegramWebhookParser.IsTelegramUpdate("""{"update_id":"7788","message":{"message_id":1}}""").Should().BeTrue();

    [Fact]
    public void PeekUpdateId_ReturnsCursorValue()
        => TelegramWebhookParser.PeekUpdateId(PrivateTextUpdate).Should().Be(987654321);

    [Fact]
    public void PeekUpdateId_NumericStringUpdateId_ReturnsValue()
        => TelegramWebhookParser.PeekUpdateId("""{"update_id":"7788"}""").Should().Be(7788);

    [Fact]
    public void PeekUpdateId_MalformedJson_ReturnsNull()
        => TelegramWebhookParser.PeekUpdateId("oops").Should().BeNull();

    #endregion

    #region 去重键：chat.id + message_id（不得依赖 update_id）

    [Fact]
    public void BuildDedupeKey_FormatIsChatIdColonMessageId()
    {
        TelegramWebhookParser.BuildDedupeKey(5551234567890, 42).Should().Be("5551234567890:42");
        // 群/频道 chat.id 为负数（supergroup 以 -100 开头）
        TelegramWebhookParser.BuildDedupeKey(-1001234567890, 1001).Should().Be("-1001234567890:1001");
    }

    [Fact]
    public void ParseUpdate_PrivateMessage_DedupeKeyUsesChatAndMessageId()
    {
        var parsed = TelegramWebhookParser.ParseUpdate(PrivateTextUpdate);

        parsed.Should().NotBeNull();
        parsed!.DedupeKey.Should().Be("5551234567890:42");
        parsed.UpdateId.Should().Be(987654321);
        parsed.ChatId.Should().Be(5551234567890);
        parsed.MessageId.Should().Be(42);
    }

    [Fact]
    public void ParseUpdate_SameChatAndMessage_DifferentUpdateId_ProduceIdenticalDedupeKey()
    {
        // update_id 约一周静默后会重置，只能当游标；去重必须靠 chat.id+message_id
        var first = TelegramWebhookParser.ParseUpdate(PrivateTextUpdate);
        var replayed = TelegramWebhookParser.ParseUpdate(PrivateTextUpdate.Replace("\"update_id\": 987654321", "\"update_id\": 1"));

        first!.DedupeKey.Should().Be(replayed!.DedupeKey);
        first.UpdateId.Should().NotBe(replayed.UpdateId);
    }

    #endregion

    #region 消息形态解析

    [Fact]
    public void ParseUpdate_PrivateTextMessage_MapsAllIdentityFields()
    {
        var parsed = TelegramWebhookParser.ParseUpdate(PrivateTextUpdate);

        parsed.Should().NotBeNull();
        parsed!.UpdateKind.Should().Be("message");
        parsed.MessageType.Should().Be("text");
        parsed.TextBody.Should().Be("你好，我想咨询订单问题");
        parsed.ChatType.Should().Be("private");
        parsed.IsGroupMessage.Should().BeFalse();
        // 私聊 chat.id == from.id
        parsed.FromId.Should().Be(parsed.ChatId);
        parsed.IsBotSender.Should().BeFalse();
        parsed.FromUsername.Should().Be("zhangsf");
        parsed.DisplayName.Should().Be("张三 丰");
        parsed.LanguageCode.Should().Be("zh-hans");
        parsed.Timestamp.Should().Be(1763100000);
        parsed.MessageThreadId.Should().BeNull();
        parsed.MigrateToChatId.Should().BeNull();
    }

    [Fact]
    public void ParseUpdate_SupergroupWithThread_MarksGroupMessageAndThread()
    {
        var parsed = TelegramWebhookParser.ParseUpdate(SupergroupThreadUpdate);

        parsed.Should().NotBeNull();
        parsed!.ChatId.Should().Be(-1001234567890);
        parsed.ChatType.Should().Be("supergroup");
        parsed.IsGroupMessage.Should().BeTrue();
        parsed.ChatTitle.Should().Be("客户群");
        parsed.MessageThreadId.Should().Be(42);
        // 会话键用 chat.id，发送者是群成员（from.id 与 chat.id 不同）
        parsed.FromId.Should().Be(111);
        parsed.DedupeKey.Should().Be("-1001234567890:1001");
    }

    [Fact]
    public void ParseUpdate_ChannelPostWithoutFrom_FallsBackToChatIdentity()
    {
        var parsed = TelegramWebhookParser.ParseUpdate("""
            {
              "update_id": 1,
              "channel_post": {
                "message_id": 7,
                "chat": { "id": -100999, "title": "公告频道", "type": "channel" },
                "date": 1763100900,
                "text": "频道广播"
              }
            }
            """);

        parsed.Should().NotBeNull();
        parsed!.UpdateKind.Should().Be("channel_post");
        parsed.FromId.Should().Be(-100999);
        parsed.DisplayName.Should().Be("公告频道");
        parsed.IsGroupMessage.Should().BeTrue();
    }

    [Fact]
    public void ParseUpdate_EditedMessage_KeepsKindAndEditDate()
    {
        var parsed = TelegramWebhookParser.ParseUpdate("""
            {
              "update_id": 5,
              "edited_message": {
                "message_id": 88,
                "from": { "id": 900, "is_bot": false, "first_name": "Ed" },
                "chat": { "id": 900, "type": "private" },
                "date": 1763100000,
                "edit_date": 1763100555,
                "text": "改过的内容"
              }
            }
            """);

        parsed.Should().NotBeNull();
        parsed!.UpdateKind.Should().Be("edited_message");
        parsed.MessageId.Should().Be(88);
        parsed.EditDate.Should().Be(1763100555);
        parsed.DedupeKey.Should().Be("900:88");
    }

    [Fact]
    public void ParseUpdate_PhotoLadder_PicksLargestRendition()
    {
        var parsed = TelegramWebhookParser.ParseUpdate(PhotoLadderUpdate);

        parsed.Should().NotBeNull();
        parsed!.MessageType.Should().Be("photo");
        parsed.FileId.Should().Be("large_id");
        parsed.FileUniqueId.Should().Be("uq_large");
        parsed.FileSize.Should().Be(1048576);
        parsed.MimeType.Should().Be("image/jpeg");
        parsed.Filename.Should().Be("photo.jpg");
        parsed.Caption.Should().Be("现场照片");
    }

    [Fact]
    public void ParseUpdate_Document_KeepsFileNameAndMimeType()
    {
        var parsed = TelegramWebhookParser.ParseUpdate("""
            {
              "update_id": 6,
              "message": {
                "message_id": 90,
                "from": { "id": 91, "is_bot": false, "first_name": "Doc" },
                "chat": { "id": 91, "type": "private" },
                "date": 1763100600,
                "document": { "file_id": "doc_id", "file_unique_id": "uq_doc", "file_name": "合同.pdf", "mime_type": "application/pdf", "file_size": 245760 }
              }
            }
            """);

        parsed.Should().NotBeNull();
        parsed!.MessageType.Should().Be("document");
        parsed.FileId.Should().Be("doc_id");
        parsed.Filename.Should().Be("合同.pdf");
        parsed.MimeType.Should().Be("application/pdf");
        parsed.FileSize.Should().Be(245760);
        parsed.TextBody.Should().BeNull();
    }

    [Fact]
    public void ParseUpdate_VoiceWithoutFileName_InfersIdentity()
    {
        var parsed = TelegramWebhookParser.ParseUpdate("""
            {
              "update_id": 7,
              "message": {
                "message_id": 91,
                "from": { "id": 92, "is_bot": false, "first_name": "Voice" },
                "chat": { "id": 92, "type": "private" },
                "date": 1763100610,
                "voice": { "file_id": "voice_id", "file_unique_id": "uq_voice", "duration": 3, "file_size": 9000 }
              }
            }
            """);

        parsed.Should().NotBeNull();
        parsed!.MessageType.Should().Be("voice");
        parsed.Filename.Should().Be("voice.ogg");
        parsed.MimeType.Should().Be("audio/ogg");
    }

    [Fact]
    public void ParseUpdate_Location_ParsesCoordinatesAndAddress()
    {
        var parsed = TelegramWebhookParser.ParseUpdate("""
            {
              "update_id": 8,
              "message": {
                "message_id": 92,
                "from": { "id": 93, "is_bot": false, "first_name": "Geo" },
                "chat": { "id": 93, "type": "private" },
                "date": 1763100620,
                "location": { "latitude": 31.2304, "longitude": 121.4737, "address": "上海市黄浦区" }
              }
            }
            """);

        parsed.Should().NotBeNull();
        parsed!.MessageType.Should().Be("location");
        parsed.Latitude.Should().BeApproximately(31.2304, 0.0001);
        parsed.Longitude.Should().BeApproximately(121.4737, 0.0001);
        parsed.TextBody.Should().Be("上海市黄浦区");
    }

    [Fact]
    public void ParseUpdate_Contact_JoinsPhoneAndName()
    {
        var parsed = TelegramWebhookParser.ParseUpdate("""
            {
              "update_id": 9,
              "message": {
                "message_id": 93,
                "from": { "id": 94, "is_bot": false, "first_name": "Con" },
                "chat": { "id": 94, "type": "private" },
                "date": 1763100630,
                "contact": { "phone_number": "+8613800138000", "first_name": "李四" }
              }
            }
            """);

        parsed.Should().NotBeNull();
        parsed!.MessageType.Should().Be("contact");
        parsed.TextBody.Should().Be("+8613800138000 / 李四");
    }

    [Fact]
    public void ParseUpdate_CallbackQuery_IsClassifiedButDoesNotIngestAsThreadBody()
    {
        var parsed = TelegramWebhookParser.ParseUpdate(CallbackQueryUpdate);

        parsed.Should().NotBeNull();
        parsed!.UpdateKind.Should().Be("callback_query");
        parsed.CallbackQueryId.Should().Be("cb_abcdef123456");
        parsed.CallbackData.Should().Be("menu:order_status");
        // answerCallbackQuery 必须在 10 秒内回执，因此按钮点击也要能定位到 chat/message
        parsed.ChatId.Should().Be(333);
        parsed.MessageId.Should().Be(3003);
        parsed.DedupeKey.Should().Be("333:3003");
        parsed.TextBody.Should().BeNull();
    }

    [Theory]
    [InlineData("my_chat_member")]
    [InlineData("chat_member")]
    [InlineData("inline_query")]
    [InlineData("poll_answer")]
    [InlineData("chosen_inline_result")]
    public void ParseUpdate_NonMessageKinds_AreRecognisedButNotIngested(string kind)
    {
        var parsed = TelegramWebhookParser.ParseUpdate($$$"""{"update_id": 10, "{{{kind}}}": { "id": "x" }}""");

        parsed.Should().NotBeNull();
        parsed!.UpdateKind.Should().Be(kind);
        parsed.MessageId.Should().Be(0);
        parsed.DedupeKey.Should().BeEmpty();
    }

    [Fact]
    public void ParseUpdate_MigrateToChatId_ServiceMessage_CarriesNewChatId()
    {
        // 升级群会换 chat.id（supergroup），必须改写会话绑定的 chat_id
        var parsed = TelegramWebhookParser.ParseUpdate("""
            {
              "update_id": 11,
              "message": {
                "message_id": 94,
                "chat": { "id": -100111, "title": "客户群", "type": "group" },
                "date": 1763100640,
                "migrate_to_chat_id": -100222
              }
            }
            """);

        parsed.Should().NotBeNull();
        parsed!.MessageType.Should().Be("migrate_chat");
        parsed.MigrateToChatId.Should().Be(-100222);
    }

    [Theory]
    [InlineData(""" "left_chat_member": { "id": 1, "is_bot": false, "first_name": "gone" } """)]
    [InlineData(""" "new_chat_title": "新群名" """)]
    public void ParseUpdate_GroupServiceMessages_AreMarkedServiceWithoutBody(string serviceField)
    {
        var payload = $$"""
            {
              "update_id": 12,
              "message": {
                "message_id": 95,
                "chat": { "id": -100333, "title": "客户群", "type": "supergroup" },
                "date": 1763100650,
                {{serviceField}}
              }
            }
            """;

        var parsed = TelegramWebhookParser.ParseUpdate(payload);

        parsed.Should().NotBeNull();
        parsed!.MessageType.Should().Be("service");
        parsed.TextBody.Should().BeNull();
    }

    [Fact]
    public void ParseUpdate_MissingDate_FallsBackToNow()
    {
        var parsed = TelegramWebhookParser.ParseUpdate("""
            {
              "update_id": 13,
              "message": {
                "message_id": 96,
                "from": { "id": 97, "is_bot": false, "first_name": "NoDate" },
                "chat": { "id": 97, "type": "private" },
                "text": "没有时间戳"
              }
            }
            """);

        parsed.Should().NotBeNull();
        parsed!.Timestamp.Should().BeGreaterThan(1700000000);
    }

    [Fact]
    public void ParseUpdate_MalformedJson_ThrowsForCallerToCatch()
    {
        // 控制器先按原始字节校验 secret-token，再交给解析器；坏 JSON 由调用方捕获后仍回 200
        var act = () => TelegramWebhookParser.ParseUpdate("{not-json");
        act.Should().Throw<JsonException>();
    }

    #endregion

    #region ParseUpdates：单对象 / 数组 / getUpdates 信封

    [Fact]
    public void ParseUpdates_SingleObject_ReturnsOneUpdate()
    {
        var updates = TelegramWebhookParser.ParseUpdates(PrivateTextUpdate);

        updates.Should().HaveCount(1);
        updates[0].DedupeKey.Should().Be("5551234567890:42");
    }

    [Fact]
    public void ParseUpdates_ArrayForm_ReturnsAllParseableUpdates()
    {
        var payload = $$"""[ {{PrivateTextUpdate}}, {{SupergroupThreadUpdate}} ]""";

        var updates = TelegramWebhookParser.ParseUpdates(payload);

        updates.Should().HaveCount(2);
        updates.Select(u => u.UpdateId).Should().BeEquivalentTo(new[] { 987654321L, 987654322L });
    }

    [Fact]
    public void ParseUpdates_GetUpdatesEnvelope_ReturnsResultItems()
    {
        var payload = $$"""{ "ok": true, "result": [ {{PrivateTextUpdate}}, {{PhotoLadderUpdate}} ] }""";

        var updates = TelegramWebhookParser.ParseUpdates(payload);

        updates.Should().HaveCount(2);
        updates[1].MessageType.Should().Be("photo");
    }

    [Fact]
    public void ParseUpdates_EmptyEnvelope_ReturnsEmptyList()
        => TelegramWebhookParser.ParseUpdates("""{"ok":true,"result":[]}""").Should().BeEmpty();

    [Fact]
    public void ParseUpdates_SkipsUnparsableItems()
    {
        var payload = $$"""[ 123, "x", {{CallbackQueryUpdate}}, { "update_id": 99 } ]""";

        var updates = TelegramWebhookParser.ParseUpdates(payload);

        // 纯标量元素被忽略；{"update_id":99} 没有内容字段 ⇒ ParseUpdateElement 返回 null
        updates.Should().ContainSingle(u => u.UpdateKind == "callback_query");
    }

    #endregion

    #region /start 深链参数（账号绑定）

    [Theory]
    [InlineData("/start BIND_abc123", "BIND_abc123")]
    [InlineData("/start", null)]
    [InlineData("/start    ", null)]
    [InlineData("/START bind_upper", "bind_upper")]
    [InlineData("  /start deep_link_value", "deep_link_value")]
    [InlineData("/start startgroup=group_token", "group_token")]
    public void TryParseStartParameter_ValidCommands(string? text, string? expected)
    {
        var ok = TelegramWebhookParser.TryParseStartParameter(text, out var parameter);

        ok.Should().BeTrue();
        parameter.Should().Be(expected);
    }

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("你好")]
    [InlineData("/help")]
    [InlineData("startnotacommand")]
    public void TryParseStartParameter_NonStartMessage_ReturnsFalse(string? text)
    {
        TelegramWebhookParser.TryParseStartParameter(text, out var parameter).Should().BeFalse();
        parameter.Should().BeNull();
    }

    [Fact]
    public void TryParseStartParameter_TruncatesToOfficial64CharLimit()
    {
        var longValue = new string('a', 90);

        TelegramWebhookParser.TryParseStartParameter($"/start {longValue}", out var parameter).Should().BeTrue();
        parameter.Should().HaveLength(64);
    }

    [Fact]
    public void ParseUpdate_StartCommand_ExposesStartParameter()
    {
        var parsed = TelegramWebhookParser.ParseUpdate("""
            {
              "update_id": 14,
              "message": {
                "message_id": 97,
                "from": { "id": 98, "is_bot": false, "first_name": "Deep" },
                "chat": { "id": 98, "type": "private" },
                "date": 1763100660,
                "text": "/start LINKTOKEN123"
              }
            }
            """);

        parsed.Should().NotBeNull();
        parsed!.StartParameter.Should().Be("LINKTOKEN123");
    }

    #endregion

    #region 超长文本分片（sendMessage 4096 上限）

    [Fact]
    public void SplitLongMessage_ShortText_SinglePart()
        => TelegramWebhookParser.SplitLongMessage("短消息", 4096).Should().Equal("短消息");

    [Fact]
    public void SplitLongMessage_ExactlyAtLimit_SinglePart()
        => TelegramWebhookParser.SplitLongMessage(new string('a', 4096), 4096).Should().HaveCount(1);

    [Fact]
    public void SplitLongMessage_Empty_ReturnsNoParts()
    {
        TelegramWebhookParser.SplitLongMessage(string.Empty, 4096).Should().BeEmpty();
    }

    [Fact]
    public void SplitLongMessage_NonPositiveLimit_FallsBackToOfficial4096()
    {
        var text = new string('b', 5000);

        var parts = TelegramWebhookParser.SplitLongMessage(text, 0);

        parts.Should().HaveCount(2);
        parts.Should().OnlyContain(p => p.Length <= 4096);
    }

    [Fact]
    public void SplitLongMessage_PrefersNewlineBoundary()
    {
        var text = new string('a', 1000) + "\n" + new string('b', 1000);

        var parts = TelegramWebhookParser.SplitLongMessage(text, 1100);

        parts.Should().HaveCount(2);
        parts[0].Should().EndWith("\n");
        parts[1].Should().StartWith("b");
        string.Concat(parts).Should().Be(text);
    }

    [Fact]
    public void SplitLongMessage_PrefersSpaceBoundaryWhenNoNewlineNearEnd()
    {
        var text = string.Concat(Enumerable.Repeat("word ", 300));

        var parts = TelegramWebhookParser.SplitLongMessage(text, 1000);

        parts.Should().OnlyContain(p => p.Length <= 1000);
        parts.Should().HaveCountGreaterThan(1);
    }

    [Fact]
    public void SplitLongMessage_NoBreakPoint_HardCutsAtLimit()
    {
        var text = new string('x', 10_000);

        var parts = TelegramWebhookParser.SplitLongMessage(text, 4096);

        parts.Should().HaveCount(3);
        parts.Should().OnlyContain(p => p.Length <= 4096);
        string.Concat(parts).Should().HaveLength(10_000);
    }

    [Fact]
    public void SplitLongMessage_NeverDropsVisibleCharacters()
    {
        var text = string.Join("\n", Enumerable.Repeat("line-of-text", 400));

        var parts = TelegramWebhookParser.SplitLongMessage(text, 500);
        var rejoined = string.Concat(parts);

        parts.Should().OnlyContain(p => p.Length <= 500 && p.Length > 0);
        rejoined.Should().Be(text);
    }

    [Fact]
    public void SplitLongMessage_MaxCharsOne_StartsWithEmoji_DoesNotThrowOrLoop()
    {
        // 审计 #14：maxChars=1 且首字符是高位代理项时，旧实现把窗口算成 0 ⇒
        // LastIndexOf('\n', start - 1, 0) 直接 ArgumentOutOfRangeException；
        // 即便不抛，start 也永不前进 ⇒ 死循环 / 产出空分片。
        var text = "👀hello";

        var parts = TelegramWebhookParser.SplitLongMessage(text, 1);

        // 代理项对是最小不可分单元：允许某一片比 maxChars 多 1 个字符，但绝不把一对拆成半个
        parts[0].Should().Be("👀");
        parts.Should().NotContain(string.Empty);
        string.Concat(parts).Should().Be(text);
    }

    [Fact]
    public void SplitLongMessage_NoNewlineLongText_MaxCharsOne_KeepsEveryCharacter()
    {
        // 整段无换行、无空格时只能逐字符硬切；关键是既不丢字符也不产生空片
        var text = new string('a', 10) + "😀" + new string('b', 10);

        var parts = TelegramWebhookParser.SplitLongMessage(text, 1);

        parts.Should().OnlyContain(p => p.Length > 0 && p.Length <= 2);
        parts.Should().HaveCountGreaterThanOrEqualTo(text.Length - 1);
        string.Concat(parts).Should().Be(text);
    }

    #endregion

    #region Bot API 响应分类（403 / 429 / 401 / 409 / migrate）

    [Fact]
    public void ParseSendResponse_Success_ReturnsPlatformMessageId()
    {
        var result = TelegramWebhookParser.ParseSendResponse("""
            { "ok": true, "result": { "message_id": 321, "chat": { "id": 555, "type": "private" }, "date": 1763100000, "text": "hi" } }
            """);

        result.Success.Should().BeTrue();
        result.MessageId.Should().Be("321");
        result.ErrorCode.Should().Be(0);
        result.Blocked.Should().BeFalse();
        result.Retryable.Should().BeFalse();
    }

    [Fact]
    public void ParseSendResponse_BlockedByUser_IsTerminalAndNotRetryable()
    {
        var result = TelegramWebhookParser.ParseSendResponse("""
            { "ok": false, "error_code": 403, "description": "Forbidden: bot was blocked by the user" }
            """);

        result.Success.Should().BeFalse();
        result.ErrorCode.Should().Be(403);
        result.Blocked.Should().BeTrue();
        // 用户拉黑是终态：不能无限重试，要把会话标记为不可达
        result.Retryable.Should().BeFalse();
        result.RetryAfterSeconds.Should().BeNull();
        result.ErrorMessage.Should().Contain("blocked");
    }

    [Fact]
    public void ParseSendResponse_BotKickedFromGroup_IsBlocked()
    {
        var result = TelegramWebhookParser.ParseSendResponse("""
            { "ok": false, "error_code": 403, "description": "Forbidden: bot is not a member of the supergroup chat" }
            """);

        result.Blocked.Should().BeTrue();
        result.Retryable.Should().BeFalse();
    }

    [Theory]
    [InlineData("""{ "ok": false, "error_code": 400, "description": "Bad Request: chat not found" }""")]
    [InlineData("""{ "ok": false, "error_code": 400, "description": "Forbidden: bot can't initiate conversation with a user" }""")]
    [InlineData("""{ "ok": false, "error_code": 400, "description": "Bad Request: bot was kicked from the group chat" }""")]
    public void ParseSendResponse_UnreachableChatDescriptions_AreMarkedUnreachable(string body)
    {
        var result = TelegramWebhookParser.ParseSendResponse(body);

        result.Unreachable.Should().BeTrue();
        result.Retryable.Should().BeFalse();
    }

    [Fact]
    public void ParseSendResponse_EntityParseFailure_IsRetryableButNotUnreachable()
    {
        var result = TelegramWebhookParser.ParseSendResponse("""
            { "ok": false, "error_code": 400, "description": "Bad Request: can't parse entities: unsupported start tag \"div\" at byte offset 0" }
            """);

        result.Unreachable.Should().BeFalse();
        result.Blocked.Should().BeFalse();
    }

    [Fact]
    public void ParseSendResponse_RateLimited429_SurfacesRetryAfter()
    {
        var result = TelegramWebhookParser.ParseSendResponse("""
            { "ok": false, "error_code": 429, "description": "Too Many Requests: retry after 37", "parameters": { "retry_after": 37 } }
            """);

        result.ErrorCode.Should().Be(429);
        result.RetryAfterSeconds.Should().Be(37);
        result.Retryable.Should().BeTrue();
        result.Blocked.Should().BeFalse();
    }

    [Fact]
    public void ParseSendResponse_FloodLimit420_SurfacesChatScopedRetryAfter()
    {
        var result = TelegramWebhookParser.ParseSendResponse("""
            { "ok": false, "error_code": 420, "description": "Flood Error", "parameters": { "retry_after": 5 } }
            """);

        result.ErrorCode.Should().Be(420);
        result.RetryAfterSeconds.Should().Be(5);
        result.Retryable.Should().BeTrue();
    }

    [Fact]
    public void ParseSendResponse_InvalidTokenIsHardFail()
    {
        var result = TelegramWebhookParser.ParseSendResponse("""{ "ok": false, "error_code": 401, "description": "Unauthorized" }""");

        result.InvalidToken.Should().BeTrue();
        result.Retryable.Should().BeFalse();
    }

    [Fact]
    public void ParseSendResponse_WebhookPollingConflictIsFlagged()
    {
        var result = TelegramWebhookParser.ParseSendResponse("""
            { "ok": false, "error_code": 409, "description": "Conflict: can't use getUpdates method while webhook is active" }
            """);

        result.Conflict.Should().BeTrue();
        result.Retryable.Should().BeFalse();
    }

    [Fact]
    public void ParseSendResponse_MigrateToChatId_SurfacesNewChatId()
    {
        var result = TelegramWebhookParser.ParseSendResponse("""
            { "ok": false, "error_code": 400, "description": "Bad Request: chat moved to supergroup", "parameters": { "migrate_to_chat_id": -100222 } }
            """);

        result.MigrateToChatId.Should().Be(-100222);
    }

    [Fact]
    public void ParseSendResponse_ServerError_IsRetryable()
    {
        var result = TelegramWebhookParser.ParseSendResponse("""{ "ok": false, "error_code": 502, "description": "Bad Gateway" }""");

        result.ErrorCode.Should().Be(502);
        result.Retryable.Should().BeTrue();
    }

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("   ")]
    public void ParseSendResponse_EmptyBody_ReportsEmptyResponse(string? body)
    {
        var result = TelegramWebhookParser.ParseSendResponse(body);

        result.Success.Should().BeFalse();
        result.ErrorMessage.Should().Be("响应为空");
    }

    [Fact]
    public void ParseSendResponse_NonJsonBody_ReportsInvalidJson()
    {
        var result = TelegramWebhookParser.ParseSendResponse("<html>502 Bad Gateway</html>");

        result.Success.Should().BeFalse();
        result.ErrorMessage.Should().Be("响应不是合法 JSON");
    }

    [Fact]
    public void ParseSendResponse_NonObjectBody_ReportsFormatError()
    {
        var result = TelegramWebhookParser.ParseSendResponse("[1,2]");

        result.Success.Should().BeFalse();
        result.ErrorMessage.Should().Be("响应格式异常");
    }

    [Fact]
    public void ParseSendResponse_GetMeStyleResult_WithoutMessageIdFieldIsStillSuccess()
    {
        var result = TelegramWebhookParser.ParseSendResponse("""
            { "ok": true, "result": { "id": 777, "is_bot": true, "first_name": "CSS Bot", "username": "css_bot" } }
            """, null);

        result.Success.Should().BeTrue();
        result.MessageId.Should().BeNull();
    }

    #endregion

    #region getUpdates 信封里的游标与限流（轮询回退路径）

    [Fact]
    public void ParseUpdates_WithinEnvelope_PreservesUpdateIdOrderForCursor()
    {
        var payload = $$"""
            { "ok": true, "result": [
              { "update_id": 100, "message": { "message_id": 1, "chat": { "id": 5, "type": "private" }, "date": 1763100000, "text": "a" } },
              { "update_id": 101, "message": { "message_id": 2, "chat": { "id": 5, "type": "private" }, "date": 1763100001, "text": "b" } }
            ] }
            """;

        var updates = TelegramWebhookParser.ParseUpdates(payload);

        updates.Should().HaveCount(2);
        updates.Max(u => u.UpdateId).Should().Be(101);
        // 同一 chat 内 message_id 唯一 ⇒ 两条去重键不同
        updates.Select(u => u.DedupeKey).Should().BeEquivalentTo(new[] { "5:1", "5:2" });
    }

    #endregion
}
