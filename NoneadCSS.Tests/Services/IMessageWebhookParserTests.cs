using System.Text;
using NoneadCSS.Application.Common;
using NoneadCSS.Application.DTOs.Responses;
using Xunit;

namespace NoneadCSS.Tests.Services;

/// <summary>
/// BlueBubbles（bridge）webhook 报文解析测试：样例报文取自 docs/integration/imessage.md §4 的字段口径。
/// 覆盖：事件识别、new-message 解析、guid 去重、isFromMe 过滤、群/私聊身份归一化、AMB 入站校验、MSP 签名。
/// </summary>
public class IMessageWebhookParserTests
{
    private const string NewMessagePayload = """
    {
      "type": "new-message",
      "data": {
        "guid": "2E9D8B5F-1B3F-4C1A-9F0A-7C5E1D2A3B4C",
        "text": "你好，我想查询订单状态",
        "isFromMe": false,
        "dateCreated": 1731686400,
        "chatGuid": "iMessage;-;+15551234567",
        "handle": { "address": "+15551234567" },
        "chats": [
          { "guid": "iMessage;-;+15551234567", "chatIdentifier": "+15551234567", "style": 0 }
        ],
        "attachments": [
          { "guid": "0:1234", "filename": "photo.jpg", "mime_type": "image/jpeg", "total_bytes": 20480 }
        ]
      }
    }
    """;

    [Fact]
    public void IsBlueBubblesWebhook_RecognizesTypedEventData_Payload()
    {
        Assert.True(IMessageWebhookParser.IsBlueBubblesWebhook(NewMessagePayload));
    }

    [Fact]
    public void IsBlueBubblesWebhook_RejectsWhatsAppBusinessAccountAndGarbage()
    {
        Assert.False(IMessageWebhookParser.IsBlueBubblesWebhook(
            """{"object":"whatsapp_business_account","entry":[]}"""));
        Assert.False(IMessageWebhookParser.IsBlueBubblesWebhook("not json"));
        Assert.False(IMessageWebhookParser.IsBlueBubblesWebhook(null));
        Assert.False(IMessageWebhookParser.IsBlueBubblesWebhook("""{"type":"new-message"}"""));
    }

    [Fact]
    public void ParseEvent_NewMessage_MapsGuidTextHandleAndService()
    {
        var parsed = IMessageWebhookParser.ParseEvent(NewMessagePayload);

        Assert.Equal(IMessageEventKinds.NewMessage, parsed.Kind);
        Assert.NotNull(parsed.Message);
        Assert.Equal("2E9D8B5F-1B3F-4C1A-9F0A-7C5E1D2A3B4C", parsed.Message!.MessageGuid);
        Assert.Equal("你好，我想查询订单状态", parsed.Message.TextBody);
        Assert.Equal("+15551234567", parsed.Message.HandleAddress);
        Assert.Equal("iMessage", parsed.Message.Service);
        Assert.False(parsed.Message.IsFromMe);
        Assert.False(parsed.Message.IsGroup);
        // 秒级 dateCreated 归一化成毫秒（与坐席面一致的时间口径）
        Assert.Equal(1731686400_000L, parsed.Message.DateCreatedUnixMs);
        Assert.Single(parsed.Message.Attachments);
        Assert.Equal("photo.jpg", parsed.Message.Attachments[0].Filename);
        Assert.Equal("image/jpeg", parsed.Message.Attachments[0].MimeType);
    }

    [Fact]
    public void ParseEvent_NewMessage_PrivateExternalIdCollapsesToBareHandle()
    {
        // 同一 1:1 会话可能以 SMS;-; 前缀送达 ⇒ 私聊外部 id 收敛到裸 handle，避免会话裂开
        var payload = NewMessagePayload
            .Replace("\"chatGuid\": \"iMessage;-;+15551234567\"", "\"chatGuid\": \"SMS;-;+1 555 123 4567\"")
            .Replace("\"chatIdentifier\": \"+15551234567\"", "\"chatIdentifier\": \"+1 555 123 4567\"");

        var message = IMessageWebhookParser.ParseEvent(payload).Message!;

        Assert.Equal("+15551234567", message.ExternalId);
        Assert.False(message.IsGroup);
    }

    [Fact]
    public void ParseEvent_GroupChat_KeepsChatGuidVerbatimAsExternalId()
    {
        var payload = """
        {
          "type": "new-message",
          "data": {
            "guid": "A1",
            "text": "群里问一句",
            "isFromMe": false,
            "dateCreated": 1731686400,
            "chatGuid": "iMessage;+;chat1234567890",
            "handle": { "address": "+15551234567" },
            "chats": [
              { "guid": "iMessage;+;chat1234567890", "chatIdentifier": "chat1234567890", "style": 2, "name": "项目群" }
            ]
          }
        }
        """;

        var message = IMessageWebhookParser.ParseEvent(payload).Message!;

        // 群聊必须保留 GUID：MMS 群与 iMessage 群确实是不同会话，且群没有单一 handle
        Assert.True(message.IsGroup);
        Assert.Equal("iMessage;+;chat1234567890", message.ExternalId);
    }

    [Fact]
    public void ParseNewMessages_IgnoresIsFromMeEchoes()
    {
        var echo = NewMessagePayload.Replace("\"isFromMe\": false", "\"isFromMe\": true");

        Assert.Single(IMessageWebhookParser.ParseNewMessages(NewMessagePayload));
        Assert.Empty(IMessageWebhookParser.ParseNewMessages(echo));
    }

    [Fact]
    public void DedupeByGuid_KeepsFirstOccurrencePerGuid()
    {
        var first = new IMessageWebhookMessage { MessageGuid = "guid-1", ExternalId = "+1" };
        var second = new IMessageWebhookMessage { MessageGuid = "guid-2", ExternalId = "+2" };
        var duplicate = new IMessageWebhookMessage { MessageGuid = "guid-1", ExternalId = "+1" };
        var noGuid = new IMessageWebhookMessage { MessageGuid = null, ExternalId = "+3" };

        var result = IMessageWebhookParser.DedupeByGuid(new[] { first, duplicate, second, noGuid });

        // 无 guid 的条目一律丢弃：没有幂等键就无法保证重复投递不产生重复消息
        Assert.Equal(2, result.Count);
        Assert.Equal(new[] { "guid-1", "guid-2" }, result.Select(m => m.MessageGuid).ToArray());
    }

    [Fact]
    public void ParseEvent_UpdatedMessage_ExposesMessageGuidAndReadTime()
    {
        var payload = """
        {
          "type": "updated-message",
          "data": { "guid": "2E9D8B5F-1B3F-4C1A-9F0A-7C5E1D2A3B4C", "chatGuid": "iMessage;-;+15551234567", "dateRead": 1731686500 }
        }
        """;

        var parsed = IMessageWebhookParser.ParseEvent(payload);

        Assert.Equal(IMessageEventKinds.UpdatedMessage, parsed.Kind);
        Assert.Null(parsed.Message);
        Assert.NotNull(parsed.Update);
        Assert.Equal("2E9D8B5F-1B3F-4C1A-9F0A-7C5E1D2A3B4C", parsed.Update!.MessageGuid);
        Assert.Equal(1731686500_000L, parsed.Update.DateReadUnixMs);
    }

    [Fact]
    public void ParseEvent_MessageError_ExposesErrorMessage()
    {
        var payload = """
        {
          "type": "message-error",
          "data": { "guid": "G1", "chatGuid": "iMessage;-;+15551234567", "error": "Message send failed" }
        }
        """;

        var parsed = IMessageWebhookParser.ParseEvent(payload);

        Assert.Equal(IMessageEventKinds.MessageError, parsed.Kind);
        Assert.Contains("send failed", parsed.Update!.ErrorMessage);
    }

    [Fact]
    public void ParseEvent_TypingIndicatorWithStringData_IsTreatedAsUpdate()
    {
        var payload = """{ "type": "typing-indicator", "data": "+15551234567" }""";

        var parsed = IMessageWebhookParser.ParseEvent(payload);

        Assert.Equal(IMessageEventKinds.TypingIndicator, parsed.Kind);
        Assert.NotNull(parsed.Update);
    }

    // ===== AMB（Apple Messages for Business）入站 =====

    [Fact]
    public void ParseAmbInbound_MissingHeaders_ReportsClientError()
    {
        var payload = """{"type":"text","v":1,"id":"uuid-1","sourceId":"opaque-1","body":"hi"}""";

        var parsed = IMessageWebhookParser.ParseAmbInbound(payload, null, "urn:biz:x", "uuid-1", out var error);

        Assert.False(string.IsNullOrWhiteSpace(error));
        Assert.Null(parsed.Message);
    }

    [Fact]
    public void ParseAmbInbound_RequiresEveryField()
    {
        var payload = """{"type":"text","v":1,"id":"uuid-1","sourceId":"opaque-1","body":"hi"}""";

        IMessageWebhookParser.ParseAmbInbound(payload, "opaque-1", "urn:biz:x", "uuid-1", out var error);

        Assert.Null(error);
    }

    [Fact]
    public void ParseAmbInbound_BodyDestinationMismatchWithHeaders_ReportsClientError()
    {
        // Apple 语义：400 = 头缺失 / JSON 非法 / destination-id 与 body 不一致
        var payload = """{"type":"text","v":1,"id":"uuid-1","sourceId":"opaque-1","destinationId":"urn:biz:OTHER","body":"hi"}""";

        IMessageWebhookParser.ParseAmbInbound(payload, "opaque-1", "urn:biz:MINE", "uuid-1", out var error);

        Assert.False(string.IsNullOrWhiteSpace(error));
    }

    [Fact]
    public void ParseAmbInbound_TextMessage_MapsSourceIdAndIdempotencyId()
    {
        var payload = """{"type":"text","v":1,"id":"uuid-9","sourceId":"tel:+15559998888","body":"帮我查订单"}""";

        var parsed = IMessageWebhookParser.ParseAmbInbound(payload, "tel:+15559998888", "urn:biz:mine", "uuid-9", out var error);

        Assert.True(string.IsNullOrWhiteSpace(error));
        Assert.NotNull(parsed.Message);
        // Apple 的 Id 头 / body.id 是幂等键（不是 BlueBubbles 的 guid）
        Assert.Equal("uuid-9", parsed.Message!.MessageGuid);
        // tel: 形态归一化成裸号码；urn: 形态原样保留（不透明 id 不可猜测）
        Assert.Equal("+15559998888", parsed.Message.ExternalId);
        Assert.Equal("帮我查订单", parsed.Message.TextBody);
    }

    [Fact]
    public void ParseAmbInbound_CloseEvent_IsSurfacedAsNoticeKind()
    {
        // 用户左滑删除并确认"停止接收" ⇒ type:"close"；此后必须屏蔽坐席发送
        var payload = """{"type":"close","v":1,"id":"uuid-c","sourceId":"urn:mbid:abc","body":""}""";

        var parsed = IMessageWebhookParser.ParseAmbInbound(payload, "urn:mbid:abc", "urn:biz:mine", "uuid-c", out var error);

        Assert.True(string.IsNullOrWhiteSpace(error));
        Assert.Equal("amb-close", parsed.Kind);
    }

    // ===== MSP HMAC 签名 =====

    [Fact]
    public void VerifyMspSignature_AcceptsHexAndSha256Prefix_AndRejectsWrongSecret()
    {
        var body = Encoding.UTF8.GetBytes("""{"type":"text","body":"hi"}""");
        var secret = "test-webhook-secret";
        using var hmac = new System.Security.Cryptography.HMACSHA256(Encoding.UTF8.GetBytes(secret));
        var hex = Convert.ToHexString(hmac.ComputeHash(body)).ToLowerInvariant();

        Assert.True(IMessageWebhookParser.VerifyMspSignature(body, secret, hex));
        Assert.True(IMessageWebhookParser.VerifyMspSignature(body, secret, "sha256=" + hex));
        Assert.False(IMessageWebhookParser.VerifyMspSignature(body, secret, "sha256=deadbeef"));
        Assert.False(IMessageWebhookParser.VerifyMspSignature(body, secret, null));
        Assert.False(IMessageWebhookParser.VerifyMspSignature(body, "", hex));
    }
}
