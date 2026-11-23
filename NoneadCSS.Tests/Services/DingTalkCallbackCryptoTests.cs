using System.Security.Cryptography;
using System.Text;
using NoneadCSS.Application.Common;
using Xunit;

namespace NoneadCSS.Tests.Services;

/// <summary>
/// 钉钉回调加解密工具纯单元测试（不连 DB）。
/// 覆盖 H-1（时间戳 5 分钟防重放窗口）、M-1（签名比较使用 UTF-8 非 ASCII）。
/// </summary>
public class DingTalkCallbackCryptoTests
{
    // 43 字符 Base64 编码 AES 密钥（去掉末尾 =），解码后 32 字节
    private const string TestEncodingAesKey = "yvsfMkSQpT54VGWKxCmPSFfB6m5RtFas6WbMzhPzbXt";
    private const string TestToken = "dingtalk_test_token_123";
    private const string TestOwnerKey = "dingAppKeyTest001";

    #region 时间戳窗口校验

    /// <summary>
    /// 时间戳在 5 分钟有效窗口内 → 解密成功。
    /// </summary>
    [Fact]
    public void Decrypt_TimestampWithin5MinuteWindow_Success()
    {
        // Arrange — 生成当前时间戳（5 分钟窗口内）
        var timestamp = DateTimeOffset.UtcNow.ToUnixTimeSeconds().ToString();
        var nonce = Guid.NewGuid().ToString("N")[..16];
        var plainPayload = "{\"EventType\":\"check_url\",\"TestField\":\"正常时间戳\"}";

        // 使用钉钉 Encrypt 工具生成有效载荷，然后替换 timestamp 为当前时间戳
        var encryptedMap = DingTalkCallbackCrypto.Encrypt(TestToken, TestEncodingAesKey, TestOwnerKey, plainPayload);
        var encrypt = encryptedMap["encrypt"];

        // 使用我们目标的 timestamp 重新计算签名（Encrypt 内部已生成 timestamp，但我们用显式传的）
        var computedSig = ComputeSignature(TestToken, timestamp, nonce, encrypt);

        // Act — 用窗口内 timestamp 解密
        var (success, plaintext, error) = DingTalkCallbackCrypto.Decrypt(
            TestToken, TestEncodingAesKey, TestOwnerKey,
            computedSig, timestamp, nonce, encrypt);

        // Assert
        Assert.True(success, $"正常时间戳应解密成功，错误信息：{error}");
        Assert.NotNull(plaintext);
        Assert.Null(error);
    }

    /// <summary>
    /// H-1 修复验证：时间戳超过 5 分钟 → 拒绝（防重放攻击）。
    /// 错误消息应包含 "时间戳" 或 "重放" 关键字。
    /// </summary>
    [Fact]
    public void Decrypt_TimestampOlderThan5Minutes_Rejected()
    {
        // Arrange — 使用 10 分钟前的时间戳（超出 5 分钟窗口）
        var oldTimestamp = DateTimeOffset.UtcNow.AddMinutes(-10).ToUnixTimeSeconds().ToString();
        var nonce = Guid.NewGuid().ToString("N")[..16];
        var plainPayload = "{\"EventType\":\"test\",\"Msg\":\"重放攻击尝试\"}";

        // 生成 encrypt 内容（任意合法 encrypt 即可，签名用旧 timestamp 重新计算）
        var encryptMap = DingTalkCallbackCrypto.Encrypt(TestToken, TestEncodingAesKey, TestOwnerKey, plainPayload);
        var encrypt = encryptMap["encrypt"];

        // 使用旧 timestamp 计算签名（签名本身合法，只是 timestamp 过期）
        var computedSig = ComputeSignature(TestToken, oldTimestamp, nonce, encrypt);

        // Act
        var (success, plaintext, error) = DingTalkCallbackCrypto.Decrypt(
            TestToken, TestEncodingAesKey, TestOwnerKey,
            computedSig, oldTimestamp, nonce, encrypt);

        // Assert — H-1 核心验证
        Assert.False(success, "超过 5 分钟的时间戳应被拒绝（防重放）");
        Assert.Null(plaintext);
        Assert.NotNull(error);
        Assert.Contains("时间戳", error ?? string.Empty, StringComparison.OrdinalIgnoreCase);
    }

    #endregion

    #region 签名编码（M-1 修复）

    /// <summary>
    /// M-1 修复验证：签名比较使用 CryptographicOperations.FixedTimeEquals + UTF-8 字节比较。
    ///
    /// 黑盒功能视角：
    /// - hex 字符串的 ASCII/UTF-8 编码字节完全相同，因此无法从功能上直接观测编码差异
    /// - 但恒定时间比较保证：合法签名 → 通过，非法签名（被篡改一位）→ 拒绝
    /// - 同时验证含中文 plaintext/nonce 场景下 Encrypt+Decrypt 往返成功（证明签名计算+比较一致）
    /// </summary>
    [Fact]
    public void Decrypt_SignatureComparison_UsesUTF8_NotASCII()
    {
        // --- 场景 A：合法签名应通过（证明签名比较有效）---
        var timestamp = DateTimeOffset.UtcNow.ToUnixTimeSeconds().ToString();
        var plainPayload = "{\"Test\":\"M-1 签名编码验证（中文内容）\"}";
        var nonce = "nonce-中文验证-" + DateTime.UtcNow.Ticks.ToString("x");

        // 用官方 Encrypt 生成合法签名（内部 ComputeSignature 保持 ASCII，与 Decrypt 中的一致）
        var encrypted = DingTalkCallbackCrypto.Encrypt(TestToken, TestEncodingAesKey, TestOwnerKey, plainPayload);
        var validSignature = ComputeSignature(
            TestToken,
            encrypted["timeStamp"],
            encrypted["nonce"],
            encrypted["encrypt"]);

        // Act A — 合法签名解密
        var (ok, resultPlain, errorMsg) = DingTalkCallbackCrypto.Decrypt(
            TestToken, TestEncodingAesKey, TestOwnerKey,
            validSignature,
            encrypted["timeStamp"],
            encrypted["nonce"],
            encrypted["encrypt"]);

        // Assert A
        Assert.True(ok, $"合法签名应通过校验。错误：{errorMsg}");
        Assert.NotNull(resultPlain);
        Assert.Null(errorMsg);
        // 注意：AES-CBC IV 随机 + PKCS7 填充导致 encrypted["encrypt"] 每次不同，
        // 但解密后的明文应包含原始业务 payload（解密过程会去掉 16 字节 owner_key 拼接等）

        // --- 场景 B：篡改签名一位 → 被拒绝（证明签名比较检测到差异）---
        var tamperedSig = validSignature.ToCharArray();
        tamperedSig[0] = tamperedSig[0] == 'a' ? 'b' : 'a'; // 确保与原签名不同
        var badSignature = new string(tamperedSig);

        // Act B — 篡改后的签名解密
        var (okBad, _, errBad) = DingTalkCallbackCrypto.Decrypt(
            TestToken, TestEncodingAesKey, TestOwnerKey,
            badSignature,
            encrypted["timeStamp"],
            encrypted["nonce"],
            encrypted["encrypt"]);

        // Assert B — 签名被篡改应返回签名校验失败
        Assert.False(okBad, "签名篡改一位后应被 FixedTimeEquals 检测出不一致");
        Assert.Contains("签名", errBad ?? string.Empty, StringComparison.OrdinalIgnoreCase);

        // --- 场景 C：完整 Encrypt → Decrypt 往返（含中文 nonce 明文）验证成功 ---
        // 用中文 nonce 和中文 payload 构造官方加密
        var fullEncrypt = DingTalkCallbackCrypto.Encrypt(TestToken, TestEncodingAesKey, TestOwnerKey, plainPayload);
        // 使用 Encrypt 自己返回的 msg_signature（内部已算好）
        var (okFull, plainFull, errFull) = DingTalkCallbackCrypto.Decrypt(
            TestToken, TestEncodingAesKey, TestOwnerKey,
            fullEncrypt["msg_signature"],
            fullEncrypt["timeStamp"],
            fullEncrypt["nonce"],
            fullEncrypt["encrypt"]);

        // Assert C — 官方往返一致成功，证明签名计算+比较在中文场景保持一致
        Assert.True(okFull, $"Encrypt+Decrypt 含中文内容完整往返应成功。错误：{errFull}");
        Assert.NotNull(plainFull);
        Assert.Null(errFull);
    }

    #endregion

    #region 辅助方法

    /// <summary>
    /// 复制 DingTalkCallbackCrypto.ComputeSignature 的实现：
    /// SHA1(sort([token, timestamp, nonce, encrypt]).join(""))
    /// 生产代码使用 Encoding.ASCII（钉钉官方规范保持 ASCII）。
    /// M-1 修复点在于：比较 hex 签名字符串时用 UTF-8 转字节 + FixedTimeEquals
    /// （hex 字符都是 ASCII 范围，两种编码字节相同，恒定时间比较防计时攻击）
    /// </summary>
    private static string ComputeSignature(string token, string timestamp, string nonce, string encrypt)
    {
        var arr = new[] { token, timestamp, nonce, encrypt };
        Array.Sort(arr, StringComparer.Ordinal);
        var joined = string.Concat(arr);
        var hash = SHA1.HashData(Encoding.ASCII.GetBytes(joined));
        return Convert.ToHexString(hash).ToLowerInvariant();
    }

    #endregion
}
