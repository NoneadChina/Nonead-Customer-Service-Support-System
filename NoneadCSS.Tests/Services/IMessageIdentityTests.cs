using NoneadCSS.Application.Common;
using Xunit;

namespace NoneadCSS.Tests.Services;

/// <summary>
/// 身份归一化测试（docs/integration/imessage.md §5）：私聊变体收敛、群聊保留 GUID、
/// (mode, externalId) 作用域前缀，以及回复时沿用入站 GUID（避免主动回复降级成 SMS）。
/// </summary>
public class IMessageIdentityTests
{
    [Theory]
    [InlineData("iMessage;-;+15551234567", false)]
    [InlineData("SMS;-;+15551234567", false)]
    [InlineData("RCS;-;+15551234567", false)]
    [InlineData("any;-;user@example.com", false)]
    [InlineData("iMessage;+;chat1234567890", true)]
    [InlineData("SMS;+;chat-abc", true)]
    public void IsGroupChatGuid_DetectsSeparatorAndChatIdentifier(string guid, bool expected)
    {
        Assert.Equal(expected, IMessageIdentity.IsGroupChatGuid(guid));
    }

    [Theory]
    [InlineData("+1 (555) 123-4567", "+15551234567")]
    [InlineData("  +15551234567 ", "+15551234567")]
    [InlineData("User@Example.com", "user@example.com")]
    [InlineData("+15551234567", "+15551234567")]
    public void NormalizeHandle_CollapsesPrivateChatVariants(string input, string expected)
    {
        Assert.Equal(expected, IMessageIdentity.NormalizeHandle(input));
    }

    [Fact]
    public void ResolveExternalId_PrivateChat_PrefersChatIdentifierOverHandle()
    {
        var (externalId, isGroup) = IMessageIdentity.ResolveExternalId(
            chatGuid: "SMS;-;+1 555 123 4567",
            chatIdentifier: "+15551234567",
            handleAddress: "+15551234567",
            isGroupChat: null);

        Assert.False(isGroup);
        Assert.Equal("+15551234567", externalId);
    }

    [Fact]
    public void ResolveExternalId_PrivateChat_WithBlankIdentifierFallsBackToNormalizedHandle()
    {
        var (externalId, isGroup) = IMessageIdentity.ResolveExternalId(
            chatGuid: "any;-;Foo@Example.COM",
            chatIdentifier: null,
            handleAddress: " Foo@Example.COM ",
            isGroupChat: null);

        Assert.False(isGroup);
        Assert.Equal("foo@example.com", externalId);
    }

    [Fact]
    public void ResolveExternalId_GroupChat_KeepsGuidVerbatim()
    {
        // 群聊绝不能用 handle 收敛：MMS 群与 iMessage 群是两个不同会话
        var (externalId, isGroup) = IMessageIdentity.ResolveExternalId(
            chatGuid: "iMessage;+;chat9876",
            chatIdentifier: "chat9876",
            handleAddress: "+15551234567",
            isGroupChat: null);

        Assert.True(isGroup);
        Assert.Equal("iMessage;+;chat9876", externalId);
        Assert.NotEqual("+15551234567", externalId);
    }

    [Fact]
    public void BuildScopedChannelId_SeparatesIdentityNamespacesPerMode()
    {
        // AMB 的不透明 id 与 BlueBubbles 的 chatGuid 可能相撞 ⇒ ChannelId 必须带模式前缀
        var bridge = IMessageIdentity.BuildScopedChannelId("bridge", "+15551234567");
        var amb = IMessageIdentity.BuildScopedChannelId("amb_msp", "+15551234567");

        Assert.Equal("bridge:+15551234567", bridge);
        Assert.Equal("amb_msp:+15551234567", amb);
        Assert.NotEqual(bridge, amb);
        Assert.Equal("+15551234567", IMessageIdentity.ExtractExternalId(bridge));
        Assert.Equal("+15551234567", IMessageIdentity.ExtractExternalId(amb));
    }

    [Fact]
    public void ExtractExternalId_UnscopedPrefixedValue_PassesThrough()
    {
        // 没有模式前缀时原样返回（历史数据 / 其它渠道写入的 id 形态）
        Assert.Equal("no-prefix-value", IMessageIdentity.ExtractExternalId("no-prefix-value"));
        // 带前缀时只切第一段，urn: 形态的内部冒号必须保留
        Assert.Equal("urn:mbid:abc", IMessageIdentity.ExtractExternalId("amb_msp:urn:mbid:abc"));
    }

    [Theory]
    [InlineData(null, "bridge")]
    [InlineData("", "bridge")]
    [InlineData("Bridge", "bridge")]
    [InlineData("amb_msp", "amb_msp")]
    [InlineData("amb_apple_direct", "amb_apple_direct")]
    [InlineData("garbage", "bridge")]
    public void NormalizeMode_FallsBackToBridge(string? input, string expected)
    {
        Assert.Equal(expected, IMessageIdentity.NormalizeMode(input));
    }

    [Theory]
    [InlineData("bridge", false)] // bridge = BlueBubbles 桥接，不走 AMB（docs/integration/imessage.md:64）
    [InlineData("bridge:+1555", false)]
    [InlineData("amb_msp", true)]
    // 只有三种合法模式；"amb:…" 这种半截前缀会被 NormalizeMode 兜回 bridge ⇒ 判为 false
    [InlineData("amb:urn:mbid:x", false)]
    [InlineData("amb_apple_direct:urn:mbid:x", true)]
    [InlineData("amb_msp:+1555", true)]
    [InlineData(null, false)]
    public void IsAmbMode_RecognizesBareModeAndScopedChannelId(string? input, bool expected)
    {
        Assert.Equal(expected, IMessageIdentity.IsAmbMode(input));
    }

    [Theory]
    [InlineData("bridge", true)]
    [InlineData("amb_msp", true)]
    [InlineData("amb_apple_direct", true)]
    [InlineData("BRIDGE", true)]
    [InlineData("", false)]
    [InlineData("apple", false)]
    [InlineData(null, false)]
    public void IsModeValid_WhitelistsOnlyKnownModes(string? mode, bool expected)
    {
        Assert.Equal(expected, IMessageIdentity.IsModeValid(mode));
    }

    [Fact]
    public void BuildReplyGuid_GroupChat_UsesExternalIdDirectly()
    {
        var guid = IMessageIdentity.BuildReplyGuid("bridge:iMessage;+;chat9876", inboundService: null);
        Assert.Equal("iMessage;+;chat9876", guid);
    }

    [Fact]
    public void BuildReplyGuid_PrivateChat_RebuildsGuidWithServicePrefixPreserved()
    {
        // 主动回复必须保留服务前缀：剥掉前缀曾让 iMessage 回复静默降级成 SMS
        var guid = IMessageIdentity.BuildReplyGuid("bridge:+15551234567", inboundService: "SMS");

        Assert.Equal("SMS;-;+15551234567", guid);
        Assert.StartsWith("SMS;-;", guid);
    }

    [Fact]
    public void BuildReplyGuid_PrivateChat_DefaultsToiMessageService()
    {
        var guid = IMessageIdentity.BuildReplyGuid("bridge:user@example.com", inboundService: null);
        Assert.Equal("iMessage;-;user@example.com", guid);
    }

    [Fact]
    public void ChatGuidCache_RemembersInboundGuidForOutboundReply()
    {
        var scoped = "bridge:+15550001111";
        IMessageIdentity.RememberChatGuid(scoped, "SMS;-;+15550001111");

        Assert.True(IMessageIdentity.TryGetChatGuid(scoped, out var cached));
        Assert.Equal("SMS;-;+15550001111", cached);
        // 入站 GUID 优先于按 handle 重建的 GUID
        Assert.Equal("SMS;-;+15550001111", IMessageIdentity.BuildReplyGuid(scoped, inboundService: null));
    }

    [Fact]
    public void ChatGuidCache_UnknownScope_ReturnsFalse()
    {
        Assert.False(IMessageIdentity.TryGetChatGuid("bridge:+19999999999", out var cached));
        Assert.Equal(string.Empty, cached);
    }

    [Theory]
    [InlineData("iMessage;-;+15551234567", "+15551234567")]
    [InlineData("iMessage;+;chat123", "chat123")]
    [InlineData("no-separator", "no-separator")]
    [InlineData(null, "")]
    public void GetIdentifierFromGuid_SplitsOnServiceSeparator(string? guid, string expected)
    {
        Assert.Equal(expected, IMessageIdentity.GetIdentifierFromGuid(guid));
    }

    [Theory]
    [InlineData("iMessage;-;+1", "iMessage")]
    [InlineData("SMS;+;chat1", "SMS")]
    [InlineData("weird", "unknown")]
    [InlineData(null, "unknown")]
    public void GetServiceFromGuid_ReturnsServicePrefix(string? guid, string expected)
    {
        Assert.Equal(expected, IMessageIdentity.GetServiceFromGuid(guid));
    }
}
