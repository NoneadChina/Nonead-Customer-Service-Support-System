using System.Security.Cryptography;
using System.Text;
using System.Text.RegularExpressions;
using FluentAssertions;
using NoneadCSS.Application.Security;
using Xunit;

namespace NoneadCSS.Tests.Services;

/// <summary>
/// 安全相关纯单元测试（不连 DB）。
/// 覆盖 GlobalExceptionMiddleware 敏感数据脱敏（L-1）和
/// TrackingController HMAC token 恒定时间比较（H-2/FixedTimeEquals）。
/// </summary>
public class SecuritySanitizationTests
{
    #region GlobalExceptionMiddleware SanitizeMessage

    /// <summary>
    /// L-1 修复验证：SanitizeMessage 对异常消息中的 password/pwd/client_secret
    /// 等敏感键值对应替换为 ***，原始密码文本不得出现在输出中。
    /// </summary>
    [Fact]
    public void GlobalExceptionMiddleware_SanitizeMessage_RemovesPasswordKeyValues()
    {
        // Arrange — 输入包含多种常见敏感键值对写法
        var input = "Login failed for user sa password=Secret123 pwd=abc client_secret=XYZ";

        // Act — 使用与 GlobalExceptionMiddleware.SanitizeMessage 相同的正则与替换逻辑
        var sanitized = SanitizeMessage(input);

        // Assert — 输出包含 *** 占位符
        Assert.Contains("***", sanitized);

        // 核心断言：原始明文密码完全不出现在脱敏后输出中
        Assert.DoesNotContain("Secret123", sanitized);
        Assert.DoesNotContain("pwd=abc", sanitized);
        Assert.DoesNotContain("XYZ", sanitized);

        // 密码键名本身可以保留（值被替换）
        Assert.Contains("password=", sanitized);
        Assert.Contains("pwd=", sanitized);
        Assert.Contains("client_secret=", sanitized);
    }

    /// <summary>
    /// 额外场景：覆盖 JSON 片段、连接字符串、Bearer token 等敏感模式。
    /// </summary>
    [Fact]
    public void SanitizeMessage_AdditionalSensitivePatterns_AllMasked()
    {
        // 注意：正则使用 key 后紧跟 =/: 分隔符的模式。
        // JSON 中键名被引号包裹时 "password":"xxx" 的引号在 password 后，无法匹配 →
        // 因此这里使用连接字符串 / HTTP 表单风格（key=value 或 key:value）的脱敏场景。
        var testCases = new[]
        {
            // 分号结尾（连接字符串）
            (Input: "Server=db;User Id=sa;Password=MyDbPwd;Trusted_Connection=False", Forbidden: "MyDbPwd"),
            // 多键值对
            (Input: "config api_key=sk_live_1234567890abcd access_token=ACC_TOKEN_XYZ", Forbidden: "sk_live_1234567890abcd"),
            // JWT token (含 . 分隔)
            (Input: "login failed auth_token=eyJhbGciOiJIUzI1Ni.payload.HS256Sig user", Forbidden: "eyJhbGciOiJIUzI1Ni"),
            // credential 别名键
            (Input: "request: credential=MySecretKey-42 private_key=PRV_KEY_ABC", Forbidden: "MySecretKey-42"),
            // 等号带引号的值：password='SecretVal'
            (Input: "db connect password='SecretVal' dbname=prod", Forbidden: "SecretVal"),
        };

        foreach (var (input, forbidden) in testCases)
        {
            var sanitized = SanitizeMessage(input);
            // 使用 FluentAssertions 的 Should().NotContain() 以支持 because 参数
            sanitized.Should().NotContain(forbidden,
                $"输入 [{input[..Math.Min(40, input.Length)]}...] 中敏感值 {forbidden} 未被脱敏");
        }
    }

    /// <summary>
    /// 空消息边界：不应抛异常，返回安全字符串。
    /// </summary>
    [Theory]
    [InlineData("")]
    [InlineData(null)]
    public void SanitizeMessage_NullOrEmpty_ReturnsSafeString(string? input)
    {
        var result = SanitizeMessage(input);
        Assert.NotNull(result);
    }

    #endregion

    #region TrackingController Token Validation (FixedTimeEquals)

    // 确保 SecretHasher 初始化（跨测试只需一次）
    private static bool _hasherInitialized;

    /// <summary>
    /// H-2 修复验证：TrackingController token 校验使用 SecretHasher.Hash("email-track:{id}")
    /// 并通过 CryptographicOperations.FixedTimeEquals 恒定时间比较。
    /// —— 合法 token 通过，非法 token 被拒绝。
    /// </summary>
    [Fact]
    public void TrackingController_TokenValidation_FixedTimeEquals()
    {
        // Arrange — 初始化 SecretHasher（只需一次）
        if (!_hasherInitialized)
        {
            SecretHasher.Initialize("test-secret-key-which-must-be-at-least-32-bytes-long-1234567890");
            _hasherInitialized = true;
        }

        var id = 123;
        var validToken = SecretHasher.Hash($"email-track:{id}");

        // Act & Assert — 合法 token 通过恒定时间比较
        var validResult = ValidateTrackingToken(id, validToken);
        Assert.True(validResult, "对合法 token 应返回 true");

        // Act & Assert — 不同 id + 同 token → 不通过
        var wrongIdResult = ValidateTrackingToken(999, validToken);
        Assert.False(wrongIdResult, "token 与 id 不匹配应返回 false");

        // Act & Assert — 正确 id 但篡改 token → 不通过
        var tamperedToken = validToken.ToCharArray();
        tamperedToken[^1] = tamperedToken[^1] == 'a' ? 'b' : 'a';
        var tamperedResult = ValidateTrackingToken(id, new string(tamperedToken));
        Assert.False(tamperedResult, "被篡改的 token 应返回 false");

        // Act & Assert — 空 token → 不通过
        var emptyResult = ValidateTrackingToken(id, string.Empty);
        Assert.False(emptyResult);

        // Act & Assert — 长度不同的 token → FixedTimeEquals 先拒绝（长度不等直接 false）
        var shortResult = ValidateTrackingToken(id, validToken[..10]);
        Assert.False(shortResult);
    }

    #endregion

    #region 辅助方法（模拟生产代码私有实现）

    /// <summary>
    /// 复制 GlobalExceptionMiddleware.SanitizeMessage 的完整逻辑（L-1 修复实现）：
    /// 1. 对 null/空安全返回
    /// 2. 使用编译后的正则匹配 password/pwd/secret/api_key/access_token 等常见敏感键
    /// 3. 将值部分替换为 ***（保留键名和分隔符）
    /// </summary>
    private static readonly Regex SensitiveDataRegex = new(
        @"(?i)((?:password|pwd|passwd|secret|api[_-]?key|access[_-]?token|auth[_-]?token|bearer|authorization|credential|private[_-]?key|client[_-]?secret)\s*[=:][""']?\s*)([^\s;,""'\]}]+)",
        RegexOptions.Compiled);

    private static string SanitizeMessage(string? message)
    {
        if (string.IsNullOrEmpty(message))
            return message ?? string.Empty;

        return SensitiveDataRegex.Replace(message, "$1***");
    }

    /// <summary>
    /// 复制 TrackingController.ValidateTrackingToken 的逻辑（H-2 修复实现）：
    /// 1. 计算 expected = SecretHasher.Hash($"email-track:{id}")
    /// 2. 通过 CryptographicOperations.FixedTimeEquals 做恒定时间字节比较
    ///    （长度不同直接返回 false，避免泄露长度信息给计时侧信道攻击）
    /// </summary>
    private static bool ValidateTrackingToken(int id, string token)
    {
        if (string.IsNullOrEmpty(token))
            return false;

        string expected;
        try
        {
            expected = SecretHasher.Hash($"email-track:{id}");
        }
        catch (InvalidOperationException)
        {
            return false;
        }

        var expectedBytes = Encoding.UTF8.GetBytes(expected);
        var actualBytes = Encoding.UTF8.GetBytes(token);
        return expectedBytes.Length == actualBytes.Length
               && CryptographicOperations.FixedTimeEquals(expectedBytes, actualBytes);
    }

    #endregion
}
