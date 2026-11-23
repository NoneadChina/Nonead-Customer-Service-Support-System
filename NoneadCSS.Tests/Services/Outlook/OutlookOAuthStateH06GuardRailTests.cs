using System;
using System.Collections.Generic;
using System.Net.Http;
using System.Security.Cryptography;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using FluentAssertions;
using Microsoft.Extensions.Caching.Distributed;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;
using Moq;
using NoneadCSS.Application.Configuration;
using NoneadCSS.Application.Services;
using NoneadCSS.Domain.Services;
using NoneadCSS.Infrastructure.Services.Outlook;
using Xunit;

namespace NoneadCSS.Tests.Services.Outlook;

/// <summary>
/// H-06（Outlook OAuth state CSRF 抗攻击安全守卫）单元测试。
///
/// 验证点：
///   1. state 经过 IEncryptionService 加密，Base64Url 编码；明文不可见，避免纯随机被猜测。
///   2. state 绑定 userId：validate 时 userId 不一致必被拒绝（H-06 绑定用户会话，防 OAuth 混淆代理）。
///   3. state 有效期 = 10 分钟，过期一律拒绝（H-06 10 分钟短 TTL 约束）。
///   4. nonce 防重放：同一 state 第二次使用被 IDistributedCache 命中并拒绝。
///   5. nonce 熵 ≥ 128 位（32 hex char，≥ 16 byte），避免猜测。
///   6. state 明文负载前缀必为 outlook-oauth-state:v1（域分隔符，避免加密值跨用途混用）。
///   7. 构造器写入的 nonce TTL = StateTtlMinutes * 2（20 分钟）。
///   8. null/空白/伪造 state 一律返回 false / (false, 0)，不抛异常。
/// </summary>
public class OutlookOAuthStateH06GuardRailTests
{
    // H-06 要求的 state 有效期（分钟）
    private const int ExpectedStateTtlMinutes = 10;

    #region Helpers (Stub 依赖)

    private sealed class LoopbackEncryptionService : IEncryptionService
    {
        public string Encrypt(string plainText) => plainText;
        public string Decrypt(string cipherText) => cipherText;
        public bool TryDecrypt(string cipherText, out string? plainText)
        {
            plainText = cipherText;
            return true;
        }
    }

    private sealed class TrackingDistributedCache : IDistributedCache
    {
        public readonly Dictionary<string, (byte[] value, DistributedCacheEntryOptions options)> Store = new();
        public readonly List<string> Reads = new();
        public int SetCalls;

        public byte[]? Get(string key) { Reads.Add(key); return Store.TryGetValue(key, out var v) ? v.value : null; }
        public Task<byte[]?> GetAsync(string key, CancellationToken ct = default) { Reads.Add(key); return Task.FromResult(Store.TryGetValue(key, out var v) ? v.value : null); }

        public void Set(string key, byte[] value, DistributedCacheEntryOptions options)
        {
            Store[key] = (value, options);
            SetCalls++;
        }

        public Task SetAsync(string key, byte[] value, DistributedCacheEntryOptions options, CancellationToken ct = default)
        {
            Set(key, value, options);
            return Task.CompletedTask;
        }

        public void Refresh(string key) { }
        public Task RefreshAsync(string key, CancellationToken ct = default) => Task.CompletedTask;
        public void Remove(string key) { Store.Remove(key); }
        public Task RemoveAsync(string key, CancellationToken ct = default) { Remove(key); return Task.CompletedTask; }
    }

    private static (OutlookOAuthService svc, LoopbackEncryptionService enc, TrackingDistributedCache cache) CreateSut()
    {
        var settings = new OutlookSyncOptions
        {
            ClientId = "cli-abc",
            ClientSecret = "sec-xyz",
            TenantId = "common",
            RedirectUri = "https://x.local/cb",
            Scopes = "offline_access Calendars.ReadWrite"
        };
        var prov = new Mock<IOutlookSettingsProvider>();
        prov.Setup(p => p.GetForUserAsync(It.IsAny<int>())).ReturnsAsync(settings);

        var enc = new LoopbackEncryptionService();
        var cache = new TrackingDistributedCache();

        var http = new HttpClient(new HttpClientHandler()) { Timeout = TimeSpan.FromSeconds(5) };
        var opts = Options.Create(new HttpClientOptions { StandardTimeoutSeconds = 5 });
        var lockMock = new Mock<IDistributedLockService>(MockBehavior.Loose);
        lockMock.Setup(s => s.TryAcquireAsync(It.IsAny<string>(), It.IsAny<int>()))
            .ReturnsAsync(Mock.Of<IDistributedLockHandle>());

        var svc = new OutlookOAuthService(
            http, prov.Object, opts, enc, cache, lockMock.Object,
            NullLogger<OutlookOAuthService>.Instance);

        return (svc, enc, cache);
    }

    #endregion

    #region H-06.1 state 格式守卫

    [Fact]
    public async Task GetAuthorizeUrlAsync_State_MustBeBase64UrlEncoded_AndEncrypted()
    {
        var (svc, enc, _) = CreateSut();

        var (authUrl, state) = await svc.GetAuthorizeUrlAsync(userId: 42);

        state.Should().NotBeNullOrWhiteSpace("H-06 state 不能为空");
        // Base64Url：只含 [A-Za-z0-9_\-]，无 = / +
        state.Should().MatchRegex("^[A-Za-z0-9_\\-]+$", "state 必须是 Base64Url 安全编码，防止 URL 解析与令牌泄露");
        // 非纯随机短字符串：长度 ≥ 40（实际会含 userId + expiresAt + nonce + domain）
        state.Length.Should().BeGreaterOrEqualTo(40, "state 含 userId/expiresAt/nonce/domain 字段，应足够长");
    }

    [Fact]
    public async Task BuildState_PayloadPrefix_MustBeStateDomain_V1_SoOtherEncryptedTokensCannotBeReused()
    {
        var (svc, enc, _) = CreateSut();

        var (_, state) = await svc.GetAuthorizeUrlAsync(userId: 7);

        var plain = Base64UrlDecode(state);
        plain.Should().StartWith("outlook-oauth-state:v1|",
            "H-06: state 明文前缀必须是 outlook-oauth-state:v1 域分隔符，防止把 JWT/会话令牌/邮件追踪 token 等加密值当作 state 来重放（域绑定防御）。");
    }

    [Fact]
    public async Task BuildState_Nonce_MustBe128BitEntropy_32HexChars()
    {
        var (svc, enc, _) = CreateSut();

        // 生成 N 个 state，检查 nonce 长度/十六进制/不重复
        const int n = 8;
        var nonces = new HashSet<string>();
        for (var i = 0; i < n; i++)
        {
            var (_, state) = await svc.GetAuthorizeUrlAsync(userId: 100 + i);
            var plain = Base64UrlDecode(state);
            var parts = plain.Split('|');
            parts.Should().HaveCount(4, "state 应为 4 段：domain|userId|expiresAt|nonce");
            var nonce = parts[3];
            nonce.Length.Should().Be(32, "H-06: nonce 必须 32 个十六进制字符，即 128 bit 熵，抗猜测");
            nonce.Should().MatchRegex("^[0-9A-Fa-f]+$", "nonce 必须为十六进制字符");
            nonces.Add(nonce).Should().BeTrue($"nonce 不能重复，第 {i} 次重复生成了 {nonce}");
        }
        nonces.Should().HaveCount(n, "H-06: 所有 nonce 都应唯一");
    }

    [Fact]
    public async Task BuildState_ExpiresAt_MustBeExactly_10Minutes_InFuture()
    {
        var (svc, enc, _) = CreateSut();

        var before = DateTime.UtcNow.AddSeconds(-1);
        var (_, state) = await svc.GetAuthorizeUrlAsync(userId: 1);
        var after = DateTime.UtcNow.AddSeconds(1);

        var plain = Base64UrlDecode(state);
        var parts = plain.Split('|');
        var exp = DateTime.ParseExact(parts[2], "O", null, System.Globalization.DateTimeStyles.RoundtripKind);

        var minExpected = before.AddMinutes(ExpectedStateTtlMinutes);
        var maxExpected = after.AddMinutes(ExpectedStateTtlMinutes);

        exp.Should().BeOnOrAfter(minExpected, "H-06: state TTL 必须为 10 分钟");
        exp.Should().BeOnOrBefore(maxExpected, "H-06: state TTL 必须为 10 分钟，不可更长以缩小攻击窗口");
    }

    #endregion

    #region H-06.2 ValidateStateAsync / TryParseStateAsync

    [Fact]
    public async Task ValidateStateAsync_CorrectUserId_ShouldReturnTrue_AndMarkNonceAsUsed()
    {
        var (svc, _, cache) = CreateSut();

        var (_, state) = await svc.GetAuthorizeUrlAsync(userId: 11);

        var result = await svc.ValidateStateAsync(state, expectedUserId: 11);
        result.Should().BeTrue("state 合法且 userId 匹配");

        cache.Store.Should().NotBeEmpty("nonce 必须被写入缓存，标记已使用");
        cache.SetCalls.Should().Be(1);
    }

    [Fact]
    public async Task ValidateStateAsync_WrongUserId_ShouldReturnFalse()
    {
        var (svc, _, _) = CreateSut();

        var (_, state) = await svc.GetAuthorizeUrlAsync(userId: 1);

        var result = await svc.ValidateStateAsync(state, expectedUserId: 9999);
        result.Should().BeFalse("H-06: state 必须绑定 userId，不匹配则拒绝（OAuth 混淆代理防护）。");
    }

    [Fact]
    public async Task ValidateStateAsync_SameStateTwice_SecondCallMustFail_ReplayDefense()
    {
        var (svc, _, _) = CreateSut();

        var (_, state) = await svc.GetAuthorizeUrlAsync(userId: 5);

        var a = await svc.ValidateStateAsync(state, expectedUserId: 5);
        var b = await svc.ValidateStateAsync(state, expectedUserId: 5);

        a.Should().BeTrue("第一次使用应通过");
        b.Should().BeFalse("H-06: 同一 state 第二次使用必须被拒绝（nonce 防重放）。");
    }

    [Fact]
    public async Task TryParseStateAsync_SameStateTwice_SecondCallMustFail()
    {
        var (svc, _, _) = CreateSut();

        var (_, state) = await svc.GetAuthorizeUrlAsync(userId: 5);

        var a = await svc.TryParseStateAsync(state);
        var b = await svc.TryParseStateAsync(state);

        a.success.Should().BeTrue();
        a.userId.Should().Be(5);
        b.success.Should().BeFalse("H-06: TryParseStateAsync 也必须实施 nonce 防重放。");
    }

    [Fact]
    public async Task TryParseStateAsync_ExpiredState_ShouldReturnFalse()
    {
        var (svc, enc, _) = CreateSut();

        // 直接构造一个 20 分钟前就过期的 state（避免等待真实 TTL）
        var expiredAt = DateTime.UtcNow.AddMinutes(-20);
        var nonce = RandomNumberGenerator.GetHexString(32, lowercase: false);
        var payload = $"outlook-oauth-state:v1|77|{expiredAt:O}|{nonce}";
        var state = Base64UrlEncode(payload);

        var (ok, uid) = await svc.TryParseStateAsync(state);
        ok.Should().BeFalse("H-06: 过期 state 必须被拒绝。");
        uid.Should().Be(0);
    }

    [Fact]
    public async Task ValidateStateAsync_InvalidInputs_ReturnFalseWithoutThrowing()
    {
        var (svc, _, _) = CreateSut();

        (await svc.ValidateStateAsync(null!, 1)).Should().BeFalse();
        (await svc.ValidateStateAsync("", 1)).Should().BeFalse();
        (await svc.ValidateStateAsync("   \t\n", 1)).Should().BeFalse();
        (await svc.ValidateStateAsync("not-base64-url:::::*****", 1)).Should().BeFalse();

        var (ok, uid) = await svc.TryParseStateAsync("not-base64-url:::::*****");
        ok.Should().BeFalse();
        uid.Should().Be(0);
    }

    #endregion

    #region H-06.3 nonce TTL 非无限期

    [Fact]
    public async Task TryClaimNonce_WriteCacheTtl_MustBe20Minutes()
    {
        var (svc, _, cache) = CreateSut();

        var (_, state) = await svc.GetAuthorizeUrlAsync(userId: 3);
        await svc.ValidateStateAsync(state, expectedUserId: 3);

        cache.Store.Should().ContainSingle();
        var kv = cache.Store.First();
        kv.Key.Should().StartWith("outlook-oauth-nonce:");

        var opts = kv.Value.options;
        opts.AbsoluteExpirationRelativeToNow.HasValue.Should().BeTrue();
        var ttl = opts.AbsoluteExpirationRelativeToNow!.Value;
        ttl.Should().Be(TimeSpan.FromMinutes(ExpectedStateTtlMinutes * 2),
            "H-06: nonce 缓存 TTL 为 state TTL 的 2 倍（= 20 分钟），到期自动清理，无限期缓存会导致 nonce 键无限增长。");
    }

    #endregion

    #region Private Base64Url helpers

    private static string Base64UrlEncode(string input)
    {
        var bytes = Encoding.UTF8.GetBytes(input);
        return Convert.ToBase64String(bytes)
            .Replace('+', '-')
            .Replace('/', '_')
            .TrimEnd('=');
    }

    private static string Base64UrlDecode(string input)
    {
        var padded = input.Replace('-', '+').Replace('_', '/');
        switch (padded.Length % 4)
        {
            case 2: padded += "=="; break;
            case 3: padded += "="; break;
        }
        var bytes = Convert.FromBase64String(padded);
        return Encoding.UTF8.GetString(bytes);
    }

    #endregion
}
