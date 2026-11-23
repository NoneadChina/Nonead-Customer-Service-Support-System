using System.Text;
using NoneadCSS.Application.Common;
using Xunit;

namespace NoneadCSS.Tests.Services;

public class WhatsAppWebhookParserTests
{
    private const string TestAppSecret = "test_app_secret_123456";

    #region VerifySignature

    [Fact]
    public void VerifySignature_ValidSignature_ReturnsTrue()
    {
        // Arrange
        var body = Encoding.UTF8.GetBytes("{\"object\":\"whatsapp_business_account\"}");
        using var hmac = new System.Security.Cryptography.HMACSHA256(Encoding.UTF8.GetBytes(TestAppSecret));
        var hash = hmac.ComputeHash(body);
        var signature = "sha256=" + Convert.ToHexString(hash).ToLowerInvariant();

        // Act
        var result = WhatsAppWebhookParser.VerifySignature(body, TestAppSecret, signature);

        // Assert
        Assert.True(result);
    }

    [Fact]
    public void VerifySignature_InvalidSignature_ReturnsFalse()
    {
        var body = Encoding.UTF8.GetBytes("test body");

        var result = WhatsAppWebhookParser.VerifySignature(body, TestAppSecret, "sha256=invalid_signature");

        Assert.False(result);
    }

    [Fact]
    public void VerifySignature_MissingHeader_ReturnsFalse()
    {
        var body = Encoding.UTF8.GetBytes("test body");

        var result = WhatsAppWebhookParser.VerifySignature(body, TestAppSecret, null);

        Assert.False(result);
    }

    [Fact]
    public void VerifySignature_EmptyAppSecret_ReturnsFalse()
    {
        var body = Encoding.UTF8.GetBytes("test body");

        var result = WhatsAppWebhookParser.VerifySignature(body, "", "sha256=somesig");

        Assert.False(result);
    }

    [Fact]
    public void VerifySignature_WrongPrefix_ReturnsFalse()
    {
        var body = Encoding.UTF8.GetBytes("test body");

        var result = WhatsAppWebhookParser.VerifySignature(body, TestAppSecret, "sha1=somesig");

        Assert.False(result);
    }

    #endregion

    #region VerifyWebhookSubscription

    [Fact]
    public void VerifyWebhookSubscription_ValidToken_ReturnsChallenge()
    {
        var result = WhatsAppWebhookParser.VerifyWebhookSubscription(
            "subscribe", "my_verify_token", "1158201444", "my_verify_token");

        Assert.Equal("1158201444", result);
    }

    [Fact]
    public void VerifyWebhookSubscription_WrongMode_ReturnsNull()
    {
        var result = WhatsAppWebhookParser.VerifyWebhookSubscription(
            "unsubscribe", "my_token", "challenge", "my_token");

        Assert.Null(result);
    }

    [Fact]
    public void VerifyWebhookSubscription_WrongToken_ReturnsNull()
    {
        var result = WhatsAppWebhookParser.VerifyWebhookSubscription(
            "subscribe", "wrong_token", "challenge", "correct_token");

        Assert.Null(result);
    }

    [Fact]
    public void VerifyWebhookSubscription_EmptyConfiguredToken_ReturnsNull()
    {
        var result = WhatsAppWebhookParser.VerifyWebhookSubscription(
            "subscribe", "token", "challenge", "");

        Assert.Null(result);
    }

    #endregion

    #region IsWhatsAppWebhook

    [Fact]
    public void IsWhatsAppWebhook_ValidPayload_ReturnsTrue()
    {
        var payload = "{\"object\":\"whatsapp_business_account\",\"entry\":[]}";

        var result = WhatsAppWebhookParser.IsWhatsAppWebhook(payload);

        Assert.True(result);
    }

    [Fact]
    public void IsWhatsAppWebhook_OtherObject_ReturnsFalse()
    {
        var payload = "{\"object\":\"page\",\"entry\":[]}";

        var result = WhatsAppWebhookParser.IsWhatsAppWebhook(payload);

        Assert.False(result);
    }

    [Fact]
    public void IsWhatsAppWebhook_EmptyPayload_ReturnsFalse()
    {
        var result = WhatsAppWebhookParser.IsWhatsAppWebhook("");

        Assert.False(result);
    }

    #endregion

    #region ParseMessages

    [Fact]
    public void ParseMessages_TextMessage_ReturnsParsedMessage()
    {
        var payload = """
        {
            "object": "whatsapp_business_account",
            "entry": [{
                "id": "123",
                "changes": [{
                    "field": "messages",
                    "value": {
                        "metadata": { "display_phone_number": "+8613800138000", "phone_number_id": "123456" },
                        "contacts": [{ "profile": { "name": "Test User" }, "wa_id": "8613800138000" }],
                        "messages": [{
                            "from": "8613800138000",
                            "id": "wamid.HBgM...",
                            "timestamp": "1700000000",
                            "type": "text",
                            "text": { "body": "Hello World" }
                        }]
                    }
                }]
            }]
        }
        """;

        var messages = WhatsAppWebhookParser.ParseMessages(payload);

        Assert.Single(messages);
        var msg = messages[0];
        Assert.Equal("wamid.HBgM...", msg.MessageId);
        Assert.Equal("8613800138000", msg.From);
        Assert.Equal("Test User", msg.ProfileName);
        Assert.Equal("text", msg.MessageType);
        Assert.Equal("Hello World", msg.TextBody);
        Assert.Equal(1700000000L, msg.Timestamp);
        Assert.Equal("+8613800138000", msg.DisplayPhoneNumber);
    }

    [Fact]
    public void ParseMessages_ImageMessage_ReturnsMediaInfo()
    {
        var payload = """
        {
            "object": "whatsapp_business_account",
            "entry": [{
                "changes": [{
                    "field": "messages",
                    "value": {
                        "messages": [{
                            "from": "8613800138000",
                            "id": "wamid.img123",
                            "timestamp": "1700000000",
                            "type": "image",
                            "image": {
                                "id": "media_id_123",
                                "mime_type": "image/jpeg",
                                "caption": "Test photo"
                            }
                        }]
                    }
                }]
            }]
        }
        """;

        var messages = WhatsAppWebhookParser.ParseMessages(payload);

        Assert.Single(messages);
        var msg = messages[0];
        Assert.Equal("image", msg.MessageType);
        Assert.Equal("media_id_123", msg.MediaId);
        Assert.Equal("image/jpeg", msg.MimeType);
        Assert.Equal("Test photo", msg.Caption);
    }

    [Fact]
    public void ParseMessages_LocationMessage_ReturnsLocationInfo()
    {
        var payload = """
        {
            "object": "whatsapp_business_account",
            "entry": [{
                "changes": [{
                    "field": "messages",
                    "value": {
                        "messages": [{
                            "from": "8613800138000",
                            "id": "wamid.loc123",
                            "timestamp": "1700000000",
                            "type": "location",
                            "location": {
                                "latitude": 39.9042,
                                "longitude": 116.4074,
                                "name": "天安门",
                                "address": "北京市东城区"
                            }
                        }]
                    }
                }]
            }]
        }
        """;

        var messages = WhatsAppWebhookParser.ParseMessages(payload);

        Assert.Single(messages);
        var msg = messages[0];
        Assert.Equal("location", msg.MessageType);
        Assert.Equal(39.9042, msg.Latitude);
        Assert.Equal(116.4074, msg.Longitude);
        Assert.Equal("天安门", msg.LocationName);
        Assert.Equal("北京市东城区", msg.LocationAddress);
    }

    [Fact]
    public void ParseMessages_InteractiveButtonReply_ReturnsTitle()
    {
        var payload = """
        {
            "object": "whatsapp_business_account",
            "entry": [{
                "changes": [{
                    "field": "messages",
                    "value": {
                        "messages": [{
                            "from": "8613800138000",
                            "id": "wamid.btn123",
                            "timestamp": "1700000000",
                            "type": "interactive",
                            "interactive": {
                                "type": "button_reply",
                                "button_reply": { "id": "btn_1", "title": "是" }
                            }
                        }]
                    }
                }]
            }]
        }
        """;

        var messages = WhatsAppWebhookParser.ParseMessages(payload);

        Assert.Single(messages);
        Assert.Equal("是", messages[0].InteractiveTitle);
    }

    [Fact]
    public void ParseMessages_MultipleMessages_ReturnsAll()
    {
        var payload = """
        {
            "object": "whatsapp_business_account",
            "entry": [{
                "changes": [{
                    "field": "messages",
                    "value": {
                        "messages": [
                            { "from": "8613800138000", "id": "wamid.1", "timestamp": "1700000000", "type": "text", "text": { "body": "Msg 1" } },
                            { "from": "8613800138000", "id": "wamid.2", "timestamp": "1700000001", "type": "text", "text": { "body": "Msg 2" } }
                        ]
                    }
                }]
            }]
        }
        """;

        var messages = WhatsAppWebhookParser.ParseMessages(payload);

        Assert.Equal(2, messages.Count);
    }

    [Fact]
    public void ParseMessages_NoMessagesField_ReturnsEmpty()
    {
        var payload = """
        {
            "object": "whatsapp_business_account",
            "entry": [{
                "changes": [{
                    "field": "messages",
                    "value": { "statuses": [{ "id": "wamid.s1", "status": "delivered" }] }
                }]
            }]
        }
        """;

        var messages = WhatsAppWebhookParser.ParseMessages(payload);

        Assert.Empty(messages);
    }

    [Fact]
    public void ParseMessages_EmptyPayload_ReturnsEmpty()
    {
        var messages = WhatsAppWebhookParser.ParseMessages("");

        Assert.Empty(messages);
    }

    [Fact]
    public void ParseMessages_UnknownType_SetsUnknownType()
    {
        var payload = """
        {
            "object": "whatsapp_business_account",
            "entry": [{
                "changes": [{
                    "field": "messages",
                    "value": {
                        "messages": [{
                            "from": "8613800138000",
                            "id": "wamid.unknown1",
                            "timestamp": "1700000000",
                            "type": "reaction",
                            "reaction": { "message_id": "wamid.other", "emoji": "👍" }
                        }]
                    }
                }]
            }]
        }
        """;

        var messages = WhatsAppWebhookParser.ParseMessages(payload);

        Assert.Single(messages);
        Assert.Equal("unknown", messages[0].MessageType);
    }

    #endregion

    #region ParseStatuses

    [Fact]
    public void ParseStatuses_DeliveredStatus_ReturnsParsed()
    {
        var payload = """
        {
            "object": "whatsapp_business_account",
            "entry": [{
                "changes": [{
                    "field": "messages",
                    "value": {
                        "statuses": [{
                            "id": "wamid.HBgM...",
                            "recipient_id": "8613800138000",
                            "status": "delivered",
                            "timestamp": "1700000000",
                            "conversation": { "id": "conv_123" }
                        }]
                    }
                }]
            }]
        }
        """;

        var statuses = WhatsAppWebhookParser.ParseStatuses(payload);

        Assert.Single(statuses);
        var s = statuses[0];
        Assert.Equal("wamid.HBgM...", s.MessageId);
        Assert.Equal("8613800138000", s.RecipientWaId);
        Assert.Equal("delivered", s.Status);
        Assert.Equal("conv_123", s.ConversationId);
    }

    [Fact]
    public void ParseStatuses_FailedStatus_ReturnsErrorMessage()
    {
        var payload = """
        {
            "object": "whatsapp_business_account",
            "entry": [{
                "changes": [{
                    "field": "messages",
                    "value": {
                        "statuses": [{
                            "id": "wamid.failed1",
                            "status": "failed",
                            "timestamp": "1700000000",
                            "errors": [{ "code": 131047, "message": "Re-engagement message" }]
                        }]
                    }
                }]
            }]
        }
        """;

        var statuses = WhatsAppWebhookParser.ParseStatuses(payload);

        Assert.Single(statuses);
        Assert.Equal("failed", statuses[0].Status);
        Assert.Equal("Re-engagement message", statuses[0].ErrorMessage);
    }

    #endregion
}
