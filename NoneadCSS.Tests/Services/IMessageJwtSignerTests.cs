using System.Text;
using System.Text.Json;
using NoneadCSS.Application.Common;
using Xunit;

namespace NoneadCSS.Tests.Services;

/// <summary>
/// AMB（Apple Messages for Business）路线的 JWT 口径测试。
/// 依据 docs/integration/imessage.md §1/§3：HS256（不是 ES256）；claims 只有 iss/aud/iat；
/// 无 kid、无 teamId、无 exp；iat 超过 1 小时 Apple 直接拒 ⇒ 至少每小时重签。
/// </summary>
public class IMessageJwtSignerTests
{
    private const string MspId = "MSP-1234567890";
    private const string Secret = "hs256-shared-secret-value";

    private static readonly DateTimeOffset FixedIssuedAt = new(2026, 11, 16, 2, 0, 0, TimeSpan.Zero);

    private static JsonElement DecodeSegment(string token, int index)
    {
        var parts = token.Split('.');
        Assert.Equal(3, parts.Length);
        var json = IMessageJwtSigner.Base64UrlDecode(parts[index]);
        using var doc = JsonDocument.Parse(Encoding.UTF8.GetString(json));
        return doc.RootElement.Clone();
    }

    [Fact]
    public void MintToken_ProducesThreeJwtSegments()
    {
        var token = IMessageJwtSigner.MintToken(MspId, Secret, issuedAtUtc: FixedIssuedAt);
        Assert.Equal(3, token.Split('.').Length);
    }

    [Fact]
    public void MintToken_HeaderIsHs256NotEs256()
    {
        var header = DecodeSegment(IMessageJwtSigner.MintToken(MspId, Secret, issuedAtUtc: FixedIssuedAt), 0);

        Assert.Equal("HS256", header.GetProperty("alg").GetString());
        Assert.Equal("JWT", header.GetProperty("typ").GetString());
        // 不存在 kid / teamId —— 出现这些字段就是给不存在的 API 建模
        Assert.False(header.TryGetProperty("kid", out _));
        Assert.False(header.TryGetProperty("crit", out _));
    }

    [Fact]
    public void MintToken_ClaimsContainOnlyIssAudIat_WithIssEqualToAud()
    {
        var payload = DecodeSegment(IMessageJwtSigner.MintToken(MspId, Secret, issuedAtUtc: FixedIssuedAt), 1);

        Assert.Equal(3, payload.EnumerateObject().Count());
        Assert.Equal(MspId, payload.GetProperty("iss").GetString());
        Assert.Equal(MspId, payload.GetProperty("aud").GetString());
        Assert.Equal(FixedIssuedAt.ToUnixTimeSeconds(), payload.GetProperty("iat").GetInt64());

        // Apple 不使用 exp ⇒ 即使调用方给了 ttl，也不得往 claims 里塞 exp
        Assert.False(payload.TryGetProperty("exp", out _));
        Assert.False(payload.TryGetProperty("nbf", out _));
        Assert.False(payload.TryGetProperty("sub", out _));
    }

    [Fact]
    public void MintToken_TtlOverHardLimit_StillNoExpClaim()
    {
        // 24h 的 ttl 会被钳到 3600，且不产生 exp
        var payload = DecodeSegment(IMessageJwtSigner.MintToken(MspId, Secret, ttlSeconds: 86_400, issuedAtUtc: FixedIssuedAt), 1);

        Assert.Equal(3, payload.EnumerateObject().Count());
        Assert.False(payload.TryGetProperty("exp", out _));
    }

    [Fact]
    public void MintToken_RejectsEmptyIssuerOrSecret()
    {
        Assert.Throws<ArgumentException>(() => IMessageJwtSigner.MintToken("", Secret));
        Assert.Throws<ArgumentException>(() => IMessageJwtSigner.MintToken(MspId, ""));
    }

    [Fact]
    public void MintToken_SameSecondAndSecret_IsDeterministic()
    {
        var a = IMessageJwtSigner.MintToken(MspId, Secret, issuedAtUtc: FixedIssuedAt);
        var b = IMessageJwtSigner.MintToken(MspId, Secret, issuedAtUtc: FixedIssuedAt);
        Assert.Equal(a, b);

        var other = IMessageJwtSigner.MintToken(MspId, "different-secret", issuedAtUtc: FixedIssuedAt);
        Assert.NotEqual(a, other);
    }

    [Fact]
    public void ReadIssuedAt_RoundTripsMintedIat()
    {
        var token = IMessageJwtSigner.MintToken(MspId, Secret, issuedAtUtc: FixedIssuedAt);
        Assert.Equal(FixedIssuedAt.ToUnixTimeSeconds(), IMessageJwtSigner.ReadIssuedAt(token));
    }

    [Fact]
    public void ReadIssuedAt_MalformedToken_ReturnsNull()
    {
        Assert.Null(IMessageJwtSigner.ReadIssuedAt("not-a-jwt"));
        Assert.Null(IMessageJwtSigner.ReadIssuedAt(string.Empty));
    }

    [Fact]
    public void TryValidate_AcceptsFreshBearerToken_WithMatchingAudience()
    {
        var token = IMessageJwtSigner.MintToken(MspId, Secret, issuedAtUtc: FixedIssuedAt);

        var ok = IMessageJwtSigner.TryValidate(
            "Bearer " + token, Secret, MspId, out var error, nowUtc: FixedIssuedAt.AddMinutes(10));

        Assert.True(ok);
        Assert.True(string.IsNullOrWhiteSpace(error));
    }

    [Fact]
    public void TryValidate_RejectsTokenOlderThanOneHour()
    {
        // Apple 硬规则：iat 超过 1 小时直接拒 ⇒ 每小时必须重签（绝不跨小时缓存）
        var token = IMessageJwtSigner.MintToken(MspId, Secret, issuedAtUtc: FixedIssuedAt);

        var ok = IMessageJwtSigner.TryValidate(
            token, Secret, MspId, out var error, nowUtc: FixedIssuedAt.AddMinutes(65));

        Assert.False(ok);
        Assert.False(string.IsNullOrWhiteSpace(error));
    }

    [Fact]
    public void TryValidate_AcceptsTokenWithinOneHourPlusSkew()
    {
        var token = IMessageJwtSigner.MintToken(MspId, Secret, issuedAtUtc: FixedIssuedAt);

        Assert.True(IMessageJwtSigner.TryValidate(
            token, Secret, MspId, out _, nowUtc: FixedIssuedAt.AddMinutes(61)));
    }

    [Fact]
    public void TryValidate_RejectsWrongSecretAndTamperedPayload()
    {
        var token = IMessageJwtSigner.MintToken(MspId, Secret, issuedAtUtc: FixedIssuedAt);

        Assert.False(IMessageJwtSigner.TryValidate(token, "wrong-secret", MspId, out _, nowUtc: FixedIssuedAt));

        var parts = token.Split('.');
        var forgedPayload = IMessageJwtSigner.Base64UrlEncode(
            Encoding.UTF8.GetBytes("""{"iss":"ATTACKER","aud":"ATTACKER","iat":1731722400}"""));
        var tampered = $"{parts[0]}.{forgedPayload}.{parts[2]}";

        Assert.False(IMessageJwtSigner.TryValidate(tampered, Secret, MspId, out _, nowUtc: FixedIssuedAt));
    }

    [Fact]
    public void TryValidate_RejectsForeignIssuerEvenWithValidSignature()
    {
        var token = IMessageJwtSigner.MintToken("OTHER-MSP", Secret, issuedAtUtc: FixedIssuedAt);

        var ok = IMessageJwtSigner.TryValidate(
            token, Secret, MspId, out var error, nowUtc: FixedIssuedAt.AddMinutes(5));

        Assert.False(ok);
        Assert.False(string.IsNullOrWhiteSpace(error));
    }

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("Bearer")]
    [InlineData("not-a-jwt")]
    [InlineData("a.b")]
    [InlineData("a.b.c.d")]
    public void TryValidate_RejectsMalformedAuthorizationHeader(string? header)
    {
        Assert.False(IMessageJwtSigner.TryValidate(header, Secret, MspId, out var error, nowUtc: FixedIssuedAt));
        Assert.False(string.IsNullOrWhiteSpace(error));
    }

    [Fact]
    public void TryValidate_RejectsMissingSharedSecret()
    {
        var token = IMessageJwtSigner.MintToken(MspId, Secret, issuedAtUtc: FixedIssuedAt);

        // 未配置密钥时必须拒绝，而不是"跳过校验"放行
        Assert.False(IMessageJwtSigner.TryValidate(token, string.Empty, MspId, out var error, nowUtc: FixedIssuedAt));
        Assert.False(string.IsNullOrWhiteSpace(error));
    }

    [Fact]
    public void TryValidate_RejectsFarFutureIat()
    {
        // 超出时钟漂移窗口的未来 iat 同样不可信
        var token = IMessageJwtSigner.MintToken(MspId, Secret, issuedAtUtc: FixedIssuedAt);

        Assert.False(IMessageJwtSigner.TryValidate(
            token, Secret, MspId, out _, nowUtc: FixedIssuedAt.AddMinutes(-30)));
    }

    [Fact]
    public void Base64Url_RoundTripsUnsafeCharacters_WithoutPadding()
    {
        var bytes = new byte[] { 0xFB, 0xFF, 0xFE, 0x00, 0x80 };
        var encoded = IMessageJwtSigner.Base64UrlEncode(bytes);

        Assert.DoesNotContain("+", encoded);
        Assert.DoesNotContain("/", encoded);
        Assert.DoesNotContain("=", encoded);
        Assert.Equal(bytes, IMessageJwtSigner.Base64UrlDecode(encoded));
    }
}
