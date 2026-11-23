using System.Text;
using FluentAssertions;
using NoneadCSS.Application.Common;

namespace NoneadCSS.Tests.Services;

/// <summary>
/// LINE Webhook 解析与验签测试（纯函数，无网络、无数据库）。
/// 覆盖：X-Line-Signature 正确/篡改/缺失、载荷识别、各类事件解析、幂等去重键、standby（频道待机）模式判定。
/// </summary>
public class LineWebhookParserTests
{
    private const string TestChannelSecret = "test_channel_secret_123456";

    private const string UserDestination = "Ua3eef5c4c6b3a3ff3e0d1f2a9b8c7d65";

    #region 签名校验

    [Fact]
    public void VerifySignature_ValidSignature_ReturnsTrue()
    {
        var body = """{"destination":"Ua3eef5c4c6b3a3ff3e0d1f2a9b8c7d65","events":[]}""";
        var bytes = Encoding.UTF8.GetBytes(body);
        var signature = LineWebhookParser.ComputeSignature(bytes, TestChannelSecret);

        LineWebhookParser.VerifySignature(bytes, TestChannelSecret, signature).Should().BeTrue();
    }

    [Fact]
    public void VerifySignature_KnownVector_MatchesBase64HmacSha256()
    {
        // 独立实现（不经被测 ComputeSignature）核对签名格式：Base64(HMAC-SHA256(raw, secret))
        var bytes = Encoding.UTF8.GetBytes("{\"events\":[{\"type\":\"message\"}]}");
        using var hmac = new System.Security.Cryptography.HMACSHA256(Encoding.UTF8.GetBytes(TestChannelSecret));
        var expected = Convert.ToBase64String(hmac.ComputeHash(bytes));

        LineWebhookParser.ComputeSignature(bytes, TestChannelSecret).Should().Be(expected);
        LineWebhookParser.VerifySignature(bytes, TestChannelSecret, expected).Should().BeTrue();
    }

    [Fact]
    public void VerifySignature_TamperedBody_ReturnsFalse()
    {
        var original = Encoding.UTF8.GetBytes("""{"destination":"Uabc","events":[{"type":"message","replyToken":"t1"}]}""");
        var signature = LineWebhookParser.ComputeSignature(original, TestChannelSecret);

        // 篡改一个字节（replyToken 的 t1 → t2），签名必须失效
        var tampered = Encoding.UTF8.GetBytes("""{"destination":"Uabc","events":[{"type":"message","replyToken":"t2"}]}""");

        LineWebhookParser.VerifySignature(tampered, TestChannelSecret, signature).Should().BeFalse();
    }

    [Fact]
    public void VerifySignature_TamperedWithinSameLength_ReturnsFalse()
    {
        // 等长篡改：FixedTimeEquals 的长度捷径不可用，必须逐字节比对失败
        var original = Encoding.UTF8.GetBytes("{\"destination\":\"Uaaa\",\"events\":[]}");
        var tampered = Encoding.UTF8.GetBytes("{\"destination\":\"Ubbb\",\"events\":[]}");
        original.LongLength.Should().Be(tampered.LongLength);

        var signature = LineWebhookParser.ComputeSignature(original, TestChannelSecret);
        LineWebhookParser.VerifySignature(tampered, TestChannelSecret, signature).Should().BeFalse();
    }

    [Fact]
    public void VerifySignature_WrongSecret_ReturnsFalse()
    {
        var bytes = Encoding.UTF8.GetBytes("{\"destination\":\"Uabc\",\"events\":[]}");
        var signature = LineWebhookParser.ComputeSignature(bytes, TestChannelSecret);

        LineWebhookParser.VerifySignature(bytes, "another_secret_value_999", signature).Should().BeFalse();
    }

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("   ")]
    public void VerifySignature_MissingOrBlankHeader_ReturnsFalse(string? header)
    {
        var bytes = Encoding.UTF8.GetBytes("{\"destination\":\"Uabc\",\"events\":[]}");
        LineWebhookParser.VerifySignature(bytes, TestChannelSecret, header).Should().BeFalse();
    }

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("   ")]
    public void VerifySignature_BlankChannelSecret_ReturnsFalse(string? secret)
    {
        var bytes = Encoding.UTF8.GetBytes("{\"destination\":\"Uabc\",\"events\":[]}");
        var signature = LineWebhookParser.ComputeSignature(bytes, "some_secret");

        LineWebhookParser.VerifySignature(bytes, secret, signature).Should().BeFalse();
    }

    [Fact]
    public void VerifySignature_NullBody_ReturnsFalse()
    {
        LineWebhookParser.VerifySignature(null, TestChannelSecret, "AA").Should().BeFalse();
    }

    [Fact]
    public void VerifySignature_EmptyBody_ReturnsFalse()
    {
        LineWebhookParser.VerifySignature(Array.Empty<byte>(), TestChannelSecret, "AA").Should().BeFalse();
    }

    [Theory]
    [InlineData("not-base64-at-all")]
    [InlineData("c2lnbmF0dXJlLXdpdGctd3JvbmctbGVuZ3Ro")]
    [InlineData("YWJj")]
    public void VerifySignature_MalformedHeader_ReturnsFalse(string header)
    {
        var bytes = Encoding.UTF8.GetBytes("{\"destination\":\"Uabc\",\"events\":[]}");
        LineWebhookParser.VerifySignature(bytes, TestChannelSecret, header).Should().BeFalse();
    }

    [Fact]
    public void VerifySignature_WhitespacePaddedHeader_StillVerifies()
    {
        // 反向代理有时把签名头拼接出换行/空格，应容错而不是拒绝合法回调
        var bytes = Encoding.UTF8.GetBytes("{\"destination\":\"Uabc\",\"events\":[]}");
        var signature = LineWebhookParser.ComputeSignature(bytes, TestChannelSecret);

        LineWebhookParser.VerifySignature(bytes, TestChannelSecret, $"  {signature}\r\n").Should().BeTrue();
    }

    [Fact]
    public void VerifySignature_Utf8MultiByteBody_IsByteExact()
    {
        // 中文/emoji 正文：验签必须按原始字节而非重新编码后的字符串
        var json = """{"destination":"Uabc","events":[{"type":"message","message":{"id":"1","type":"text","text":"こんにちは 🙏"}}]}""";
        var rawBytes = Encoding.UTF8.GetBytes(json);
        var signature = LineWebhookParser.ComputeSignature(rawBytes, TestChannelSecret);

        LineWebhookParser.VerifySignature(rawBytes, TestChannelSecret, signature).Should().BeTrue();

        var reEncoded = Encoding.UTF8.GetBytes(Encoding.UTF8.GetString(rawBytes));
        LineWebhookParser.VerifySignature(reEncoded, TestChannelSecret, signature).Should().BeTrue();
    }

    [Fact]
    public void SignatureHeader_Constant_IsLineSignature()
    {
        LineWebhookParser.SignatureHeader.Should().Be("X-Line-Signature");
    }

    #endregion

    #region 载荷识别

    [Theory]
    [InlineData("""{"destination":"Uabc","events":[]}""")]
    [InlineData("""{"events":[{"type":"follow"}]}""")]
    public void IsLineWebhook_LinePayload_ReturnsTrue(string payload)
    {
        LineWebhookParser.IsLineWebhook(payload).Should().BeTrue();
    }

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("   ")]
    [InlineData("not json at all")]
    [InlineData("""{"object":"page","entry":[]}""")]
    [InlineData("""{"events":"not-an-array"}""")]
    public void IsLineWebhook_NonLinePayload_ReturnsFalse(string? payload)
    {
        LineWebhookParser.IsLineWebhook(payload).Should().BeFalse();
    }

    [Fact]
    public void ParseDestination_ReturnsDestination()
    {
        var payload = $$"""{"destination":"{{UserDestination}}","events":[]}""";
        LineWebhookParser.ParseDestination(payload).Should().Be(UserDestination);
    }

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("broken")]
    [InlineData("""{"events":[]}""")]
    public void ParseDestination_MissingOrInvalid_ReturnsNull(string? payload)
    {
        LineWebhookParser.ParseDestination(payload).Should().BeNull();
    }

    #endregion

    #region 消息事件

    [Fact]
    public void ParseEvents_TextMessage_PopulatesReplyTokenUserAndDedupeKey()
    {
        // docs/integration/line.md 的样例消息事件
        var payload = $$"""
        {
          "destination": "{{UserDestination}}",
          "events": [
            {
              "type": "message",
              "replyToken": "nHmvY7VXpXc6dK1m2c4e5f6a7b8c9d0e",
              "userId": "U4af8f4c5c6b3a3ff3e0d1f2a9b8c7d65",
              "timestamp": 1763000000000,
              "mode": "active",
              "source": { "type": "user", "userId": "U4af8f4c5c6b3a3ff3e0d1f2a9b8c7d65" },
              "message": { "id": "10987654321098765432", "type": "text", "text": "hi", "quoteMessageId": "99" }
            }
          ]
        }
        """;

        var events = LineWebhookParser.ParseEvents(payload);

        events.Should().HaveCount(1);
        var evt = events[0];
        evt.EventType.Should().Be("message");
        evt.IsCustomerMessage.Should().BeTrue();
        evt.MessageType.Should().Be("text");
        evt.TextBody.Should().Be("hi");
        evt.MessageId.Should().Be("10987654321098765432");
        evt.DedupeKey.Should().Be("10987654321098765432");
        evt.QuoteMessageId.Should().Be("99");
        evt.ReplyToken.Should().Be("nHmvY7VXpXc6dK1m2c4e5f6a7b8c9d0e");
        evt.UserId.Should().Be("U4af8f4c5c6b3a3ff3e0d1f2a9b8c7d65");
        evt.SourceType.Should().Be("user");
        evt.Mode.Should().Be("active");
        evt.IsReplyForbidden.Should().BeFalse();
        evt.Timestamp.Should().Be(1763000000000L);
    }

    [Fact]
    public void ParseEvents_MarkAsReadToken_PreservedForChatEnabledChannels()
    {
        var payload = """
        {"destination":"Uabc","events":[{"type":"message","replyToken":"r1","markAsReadToken":"hoyaaaaa-token",
        "timestamp":1763000000000,"mode":"active","source":{"type":"user","userId":"U1"},
        "message":{"id":"m1","type":"text","text":"x"}}]}
        """;

        var evt = LineWebhookParser.ParseEvents(payload).Single();
        evt.MarkAsReadToken.Should().Be("hoyaaaaa-token");
    }

    [Fact]
    public void ParseEvents_StandbyModeEvent_IsMarkedReplyForbidden()
    {
        var payload = """
        {"destination":"Uabc","events":[{"type":"message","replyToken":"r1","timestamp":1,"mode":"standby",
        "source":{"type":"user","userId":"U1"},"message":{"id":"m1","type":"text","text":"x"}}]}
        """;

        var evt = LineWebhookParser.ParseEvents(payload).Single();
        evt.Mode.Should().Be("standby");
        evt.IsReplyForbidden.Should().BeTrue();
    }

    [Fact]
    public void ParseEvents_StandbyModeCheck_IsCaseInsensitive()
    {
        var payload = """
        {"destination":"Uabc","events":[{"type":"message","timestamp":1,"mode":"STANDBY",
        "source":{"type":"user","userId":"U1"},"message":{"id":"m1","type":"text","text":"x"}}]}
        """;

        LineWebhookParser.ParseEvents(payload).Single().IsReplyForbidden.Should().BeTrue();
    }

    [Fact]
    public void ParseEvents_ImageMessage_ExposesContentMessageId()
    {
        // LINE 载荷不含媒体 URL，只能用 message.id 去 api-data.line.me 换取
        var payload = """
        {"destination":"Uabc","events":[{"type":"message","replyToken":"r1","timestamp":2,"mode":"active",
        "source":{"type":"user","userId":"U1"},"message":{"id":"content-9001","type":"image"}}]}
        """;

        var evt = LineWebhookParser.ParseEvents(payload).Single();
        evt.MessageType.Should().Be("image");
        evt.ContentMessageId.Should().Be("content-9001");
        evt.DedupeKey.Should().Be("content-9001");
        evt.TextBody.Should().BeNull();
    }

    [Fact]
    public void ParseEvents_FileMessage_ParsesFileNameAndSize()
    {
        var payload = """
        {"destination":"Uabc","events":[{"type":"message","timestamp":3,"mode":"active",
        "source":{"type":"user","userId":"U1"},
        "message":{"id":"f1","type":"file","fileName":"报价单.pdf","fileSize":204800}}]}
        """;

        var evt = LineWebhookParser.ParseEvents(payload).Single();
        evt.MessageType.Should().Be("file");
        evt.FileName.Should().Be("报价单.pdf");
        evt.FileSize.Should().Be(204800L);
    }

    [Fact]
    public void ParseEvents_LocationMessage_ParsesCoordinates()
    {
        var payload = """
        {"destination":"Uabc","events":[{"type":"message","timestamp":4,"mode":"active",
        "source":{"type":"user","userId":"U1"},
        "message":{"id":"l1","type":"location","title":"公司门口","address":"台北市信義區市府路1號",
        "latitude":25.038,"longitude":121.563}}]}
        """;

        var evt = LineWebhookParser.ParseEvents(payload).Single();
        evt.MessageType.Should().Be("location");
        evt.Title.Should().Be("公司门口");
        evt.LocationAddress.Should().Be("台北市信義區市府路1號");
        evt.Latitude.Should().BeApproximately(25.038, 0.0001);
        evt.Longitude.Should().BeApproximately(121.563, 0.0001);
    }

    [Fact]
    public void ParseEvents_StickerMessage_GetsReadablePlaceholder()
    {
        var payload = """
        {"destination":"Uabc","events":[{"type":"message","timestamp":5,"mode":"active",
        "source":{"type":"user","userId":"U1"},
        "message":{"id":"s1","type":"sticker","packageId":"9755","stickerId":"521"}}]}
        """;

        var evt = LineWebhookParser.ParseEvents(payload).Single();
        evt.MessageType.Should().Be("sticker");
        evt.TextBody.Should().Be("[sticker 9755/521]");
    }

    [Fact]
    public void ParseEvents_MessageWithoutId_UsesSyntheticDedupeKey()
    {
        var payload = """
        {"destination":"Uabc","events":[{"type":"message","timestamp":1763000000001,"mode":"active",
        "source":{"type":"user","userId":"U777"},"message":{"type":"text","text":"no id"}}]}
        """;

        var evt = LineWebhookParser.ParseEvents(payload).Single();
        evt.MessageId.Should().BeNull();
        evt.DedupeKey.Should().Be("msg_U777_1763000000001");
    }

    [Fact]
    public void ParseEvents_MessageObjectMissing_EventSkipped()
    {
        var payload = """{"destination":"Uabc","events":[{"type":"message","timestamp":6,"mode":"active","source":{"type":"user","userId":"U1"}}]}""";

        LineWebhookParser.ParseEvents(payload).Should().BeEmpty();
    }

    [Fact]
    public void ParseEvents_GroupMessage_CarriesChatIdAndSourceType()
    {
        var payload = """
        {"destination":"Uabc","events":[{"type":"message","replyToken":"r1","timestamp":7,"mode":"active",
        "source":{"type":"group","groupId":"C123","userId":"U1"},
        "message":{"id":"g1","type":"text","text":"群里发言"}}]}
        """;

        var evt = LineWebhookParser.ParseEvents(payload).Single();
        evt.SourceType.Should().Be("group");
        evt.ChatId.Should().Be("C123");
        evt.UserId.Should().Be("U1");
        evt.IsCustomerMessage.Should().BeTrue();
    }

    [Fact]
    public void ParseEvents_RoomMessage_UsesRoomIdAsChatId()
    {
        var payload = """
        {"destination":"Uabc","events":[{"type":"message","timestamp":8,"mode":"active",
        "source":{"type":"room","roomId":"R456"},
        "message":{"id":"r1","type":"text","text":"房间发言"}}]}
        """;

        var evt = LineWebhookParser.ParseEvents(payload).Single();
        evt.SourceType.Should().Be("room");
        evt.ChatId.Should().Be("R456");
    }

    #endregion

    #region 非消息事件

    [Fact]
    public void ParseEvents_Postback_ExposesDataAndSyntheticKey()
    {
        var payload = """
        {"destination":"Uabc","events":[{"type":"postback","replyToken":"pb1","timestamp":9,"mode":"active",
        "source":{"type":"user","userId":"U42"},
        "postback":{"data":"action=menu&orderId=88","inputMessage":"m-orig"}}]}
        """;

        var evt = LineWebhookParser.ParseEvents(payload).Single();
        evt.EventType.Should().Be("postback");
        evt.MessageType.Should().Be("postback");
        evt.PostbackData.Should().Be("action=menu&orderId=88");
        evt.QuoteMessageId.Should().Be("m-orig");
        evt.DedupeKey.Should().Be("postback_U42_9");
        evt.IsCustomerMessage.Should().BeFalse();
    }

    [Fact]
    public void ParseEvents_Follow_KeptWithEventDedupeKey()
    {
        var payload = """{"destination":"Uabc","events":[{"type":"follow","replyToken":"f1","timestamp":10,"mode":"active","source":{"type":"user","userId":"U9"}}]}""";

        var evt = LineWebhookParser.ParseEvents(payload).Single();
        evt.EventType.Should().Be("follow");
        evt.DedupeKey.Should().Be("follow_U9_10");
        evt.UserId.Should().Be("U9");
    }

    [Fact]
    public void ParseEvents_Unfollow_Kept()
    {
        var payload = """{"destination":"Uabc","events":[{"type":"unfollow","timestamp":11,"mode":"active","source":{"type":"user","userId":"U9"}}]}""";

        var evt = LineWebhookParser.ParseEvents(payload).Single();
        evt.EventType.Should().Be("unfollow");
        evt.DedupeKey.Should().Be("unfollow_U9_11");
    }

    [Fact]
    public void ParseEvents_Unsend_CollectsMessageIds()
    {
        var payload = """
        {"destination":"Uabc","events":[{"type":"unsend","timestamp":12,"mode":"active",
        "source":{"type":"user","userId":"U9"},"unsend":{"messageIds":["m1","m2"]}}]}
        """;

        var evt = LineWebhookParser.ParseEvents(payload).Single();
        evt.UnsentMessageIds.Should().Equal("m1", "m2");
        evt.DedupeKey.Should().Be("unsend_m1,m2");
    }

    [Fact]
    public void ParseEvents_MemberJoined_And_Join_Kept()
    {
        var payload = """
        {"destination":"Uabc","events":[
          {"type":"join","timestamp":13,"mode":"active","source":{"type":"group","groupId":"C1"}},
          {"type":"memberJoined","timestamp":14,"mode":"active","source":{"type":"group","groupId":"C1"},
           "joined":{"members":[{"type":"user","userId":"U5"}]}}]}
        """;

        var events = LineWebhookParser.ParseEvents(payload);
        events.Should().HaveCount(2);
        events[0].DedupeKey.Should().Be("join_C1_13");
        events[1].DedupeKey.Should().Be("memberJoined_C1_14");
    }

    [Fact]
    public void ParseEvents_UnknownEventType_StillReturned()
    {
        var payload = """{"destination":"Uabc","events":[{"type":"someFutureEvent","timestamp":15,"mode":"active","source":{"type":"user","userId":"U1"}}]}""";

        var evt = LineWebhookParser.ParseEvents(payload).Single();
        evt.EventType.Should().Be("someFutureEvent");
        evt.DedupeKey.Should().Be("someFutureEvent_U1_15");
    }

    #endregion

    #region 批量与畸形载荷

    [Fact]
    public void ParseEvents_MultipleEvents_PreservesOrder()
    {
        var payload = """
        {"destination":"Uabc","events":[
          {"type":"follow","timestamp":1,"mode":"active","source":{"type":"user","userId":"U1"}},
          {"type":"message","timestamp":2,"mode":"active","source":{"type":"user","userId":"U1"},
           "message":{"id":"m1","type":"text","text":"hello"}}]}
        """;

        var events = LineWebhookParser.ParseEvents(payload);
        events.Select(e => e.EventType).Should().Equal("follow", "message");
    }

    [Fact]
    public void ParseEvents_MalformedEntries_SkippedWithoutThrowing()
    {
        var payload = """{"destination":"Uabc","events":[ 42, null, {"type":123}, {"type":"message"},{"type":"message","source":"bad","message":{"id":"ok1","type":"text","text":"kept"}}]}""";

        var events = LineWebhookParser.ParseEvents(payload);
        events.Should().NotBeNull();
        events.Any(e => e.MessageId == "ok1").Should().BeTrue();
    }

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("   ")]
    [InlineData("{ not valid json")]
    public void ParseEvents_InvalidPayload_ReturnsEmptyList(string? payload)
    {
        LineWebhookParser.ParseEvents(payload).Should().BeEmpty();
    }

    [Fact]
    public void ParseEvents_NoEventsArray_ReturnsEmptyList()
    {
        LineWebhookParser.ParseEvents("""{"destination":"Uabc"}""").Should().BeEmpty();
    }

    [Fact]
    public void ParseMessageEvents_FiltersOutNonMessageEvents()
    {
        var payload = """
        {"destination":"Uabc","events":[
          {"type":"follow","timestamp":1,"mode":"active","source":{"type":"user","userId":"U1"}},
          {"type":"postback","timestamp":2,"mode":"active","source":{"type":"user","userId":"U1"},
           "postback":{"data":"d=1"}},
          {"type":"message","timestamp":3,"mode":"active","source":{"type":"user","userId":"U1"},
           "message":{"id":"m1","type":"text","text":"hi"}}]}
        """;

        var messages = LineWebhookParser.ParseMessageEvents(payload);
        messages.Should().HaveCount(1);
        messages[0].MessageId.Should().Be("m1");
    }

    #endregion
}
