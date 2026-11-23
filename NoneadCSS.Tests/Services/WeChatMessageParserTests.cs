using System.Security.Cryptography;
using System.Text;
using NoneadCSS.Application.Common;
using Xunit;

namespace NoneadCSS.Tests.Services;

/// <summary>
/// 微信消息解析器单元测试。
/// 重点覆盖安全模式下的 AES-CBC 解密逻辑，包括：
/// - 短消息（PKCS7 padding 值 ≤ 16，能通过 .NET 标准 PKCS7 验证）
/// - 长消息（PKCS7 padding 值 > 16，触发微信 32 字节对齐 padding 场景）
/// - AppId 校验、异常密文、错误密钥等边界场景
/// </summary>
public class WeChatMessageParserTests
{
    // 测试用 EncodingAESKey（43 字符 Base64，解码后 32 字节 AES 密钥）
    private const string TestAESKey = "yvsfMkSQpT54VGWKxCmPSFfB6m5RtFas6WbMzhPzbXt";
    private const string TestAppId = "wx_test_appid_12345";

    #region 签名校验

    // 当前 Unix 时间戳（秒）：签名测试必须使用窗口内的实时时间戳，
    // 否则会被 ±5 分钟时间戳新鲜度校验拒绝。
    private static readonly long NowUnix = DateTimeOffset.UtcNow.ToUnixTimeSeconds();

    [Fact]
    public void ValidateSignature_ValidSignature_ReturnsTrue()
    {
        var token = "TestToken123";
        var timestamp = NowUnix.ToString();
        var nonce = "abc123";
        var signature = ComputeSignature(token, timestamp, nonce);

        var result = WeChatMessageParser.ValidateSignature(token, timestamp, nonce, signature);

        Assert.True(result);
    }

    [Fact]
    public void ValidateSignature_InvalidSignature_ReturnsFalse()
    {
        var result = WeChatMessageParser.ValidateSignature("token", "ts", "nonce", "wrong_signature");

        Assert.False(result);
    }

    [Theory]
    [InlineData("", "ts", "nonce", "sig")]
    [InlineData("token", "", "nonce", "sig")]
    [InlineData("token", "ts", "", "sig")]
    [InlineData("token", "ts", "nonce", "")]
    [InlineData(null, "ts", "nonce", "sig")]
    public void ValidateSignature_MissingParameters_ReturnsFalse(
        string? token, string ts, string nonce, string sig)
    {
        var result = WeChatMessageParser.ValidateSignature(token!, ts, nonce, sig);

        Assert.False(result);
    }

    [Fact]
    public void ValidateSignature_SignatureCaseInsensitive_ReturnsTrue()
    {
        var token = "TestToken";
        var timestamp = NowUnix.ToString();
        var nonce = "abc123";
        var signature = ComputeSignature(token, timestamp, nonce).ToUpperInvariant();

        var result = WeChatMessageParser.ValidateSignature(token, timestamp, nonce, signature);

        Assert.True(result);
    }

    [Fact]
    public void ValidateSignature_ReplayWithOldTimestamp_ReturnsFalse()
    {
        // 签名正确但时间戳已过期（2021 年）：应拒绝重放攻击
        var token = "TestToken";
        var timestamp = "1609459200"; // 2021-01-01
        var nonce = "abc123";
        var signature = ComputeSignature(token, timestamp, nonce);

        var result = WeChatMessageParser.ValidateSignature(token, timestamp, nonce, signature);

        Assert.False(result);
    }

    [Fact]
    public void ValidateSignature_NonNumericTimestamp_ReturnsFalse()
    {
        var token = "TestToken";
        var timestamp = "not-a-number";
        var nonce = "abc123";
        var signature = ComputeSignature(token, timestamp, nonce);

        var result = WeChatMessageParser.ValidateSignature(token, timestamp, nonce, signature);

        Assert.False(result);
    }

    #endregion

    #region 明文模式解析

    [Fact]
    public void Parse_PlainTextTextMessage_ReturnsCorrectFields()
    {
        var xml = BuildPlainXml(new[]
        {
            ("MsgType", "text"),
            ("Content", "Hello, 你好"),
            ("FromUserName", "oUser123"),
            ("ToUserName", "gh_test"),
            ("MsgId", "1234567890"),
            ("CreateTime", "1609459200")
        });

        var msg = WeChatMessageParser.Parse(xml);

        Assert.NotNull(msg);
        Assert.Equal("text", msg!.MsgType);
        Assert.Equal("Hello, 你好", msg.Content);
        Assert.Equal("oUser123", msg.FromUserName);
        Assert.Equal("gh_test", msg.ToUserName);
        Assert.Equal("1234567890", msg.MsgId);
        Assert.Equal("Hello, 你好", msg.BodyText);
    }

    [Fact]
    public void Parse_PlainTextImageMessage_ReturnsPicUrl()
    {
        var xml = BuildPlainXml(new[]
        {
            ("MsgType", "image"),
            ("PicUrl", "https://example.com/test.jpg"),
            ("MediaId", "media_123"),
            ("FromUserName", "oUser123"),
            ("MsgId", "1234567891")
        });

        var msg = WeChatMessageParser.Parse(xml);

        Assert.NotNull(msg);
        Assert.Equal("image", msg!.MsgType);
        Assert.Equal("https://example.com/test.jpg", msg.PicUrl);
        Assert.Equal("media_123", msg.MediaId);
        Assert.Contains("[图片]", msg.BodyText);
        Assert.Contains("https://example.com/test.jpg", msg.BodyText);
    }

    [Fact]
    public void Parse_EmptyXml_ReturnsNullWithError()
    {
        var msg = WeChatMessageParser.Parse("", null, null, out var error);

        Assert.Null(msg);
        Assert.False(string.IsNullOrEmpty(error));
    }

    [Fact]
    public void Parse_InvalidXml_ReturnsNullWithError()
    {
        var msg = WeChatMessageParser.Parse("not a valid xml <<<", null, null, out var error);

        Assert.Null(msg);
        Assert.False(string.IsNullOrEmpty(error));
    }

    #endregion

    #region 加密模式 - 短消息（padding ≤ 16）

    [Fact]
    public void Parse_EncryptedShortTextMessage_DecryptsSuccessfully()
    {
        // 短文本消息：XML 内容较短，PKCS7 padding 值 ≤ 16
        var innerXml = BuildPlainXml(new[]
        {
            ("MsgType", "text"),
            ("Content", "Hi"),
            ("FromUserName", "oUser123"),
            ("ToUserName", "gh_test"),
            ("MsgId", "1001"),
            ("CreateTime", "1609459200")
        });

        var encryptedXml = EncryptWeChatMessage(innerXml, TestAESKey, TestAppId);

        var msg = WeChatMessageParser.Parse(encryptedXml, TestAESKey, TestAppId, out var error);

        Assert.NotNull(msg);
        Assert.Null(error);
        Assert.Equal("text", msg!.MsgType);
        Assert.Equal("Hi", msg.Content);
        Assert.Equal("oUser123", msg.FromUserName);
    }

    [Fact]
    public void Parse_EncryptedShortMessage_PaddingValueWithinStandardRange()
    {
        // 构造短消息并主动调整内容长度，使 padding 值 ≤ 16（确保测试覆盖标准 PKCS7 通过场景）
        // padding = 32 - (contentLen % 32)，要让 padding ≤ 16 需要 contentLen % 32 ≥ 16
        var innerXml = BuildPlainXml(new[]
        {
            ("MsgType", "text"),
            ("Content", "短消息"),
            ("FromUserName", "u1"),
            ("MsgId", "1")
        });

        // 主动调整内容长度，目标 padding = 8（≤ 16）
        const int targetPadding = 8;
        var adjustedXml = AdjustMessageForTargetPadding(innerXml, TestAppId, targetPadding);
        var paddingValue = GetPaddingValueForMessage(adjustedXml, TestAppId);

        Assert.Equal(targetPadding, paddingValue);
        Assert.True(paddingValue <= 16,
            $"短消息 padding 值应为 ≤ 16（标准 PKCS7 范围），实际为 {paddingValue}");

        // 验证短消息也能被正确解密
        var encryptedXml = EncryptWeChatMessage(adjustedXml, TestAESKey, TestAppId);
        var msg = WeChatMessageParser.Parse(encryptedXml, TestAESKey, TestAppId, out var error);

        Assert.NotNull(msg);
        Assert.Null(error);
        Assert.Equal("text", msg!.MsgType);
    }

    #endregion

    #region 加密模式 - 长消息（padding > 16，微信 32 字节对齐）

    [Fact]
    public void Parse_EncryptedLongTextMessage_DecryptsSuccessfully()
    {
        // 长文本消息：构造足够长的内容，使 padding 值 > 16
        // 这是之前线上故障的核心场景：图片消息的 XML 较长，触发 32 字节对齐 padding
        var longContent = new string('A', 500); // 500 字符的文本，足以触发长消息场景
        var innerXml = BuildPlainXml(new[]
        {
            ("MsgType", "text"),
            ("Content", longContent),
            ("FromUserName", "oUser123"),
            ("ToUserName", "gh_test"),
            ("MsgId", "2001"),
            ("CreateTime", "1609459200")
        });

        var encryptedXml = EncryptWeChatMessage(innerXml, TestAESKey, TestAppId);

        var msg = WeChatMessageParser.Parse(encryptedXml, TestAESKey, TestAppId, out var error);

        Assert.NotNull(msg);
        Assert.Null(error);
        Assert.Equal("text", msg!.MsgType);
        Assert.Equal(longContent, msg.Content);
        Assert.Equal("oUser123", msg.FromUserName);
    }

    [Fact]
    public void Parse_EncryptedLongMessage_PaddingValueExceedsStandardRange()
    {
        // 构造长消息并验证其 padding 值确实 > 16（确保测试覆盖微信 32 字节对齐 padding 场景）
        var longContent = new string('A', 500);
        var innerXml = BuildPlainXml(new[]
        {
            ("MsgType", "text"),
            ("Content", longContent),
            ("FromUserName", "u1"),
            ("MsgId", "1")
        });

        var paddingValue = GetPaddingValueForMessage(innerXml, TestAppId);

        Assert.True(paddingValue > 16,
            $"长消息 padding 值应 > 16（微信 32 字节对齐场景），实际为 {paddingValue}");
    }

    [Fact]
    public void Parse_EncryptedImageLikeMessage_DecryptsSuccessfully()
    {
        // 模拟线上图片消息场景：XML 长度接近线上观测值（约 500+ 字节）
        // 线上图片消息 Encrypt Base64 长度=768，解码后 576 字节，padding=25
        var innerXml = BuildPlainXml(new[]
        {
            ("MsgType", "image"),
            ("PicUrl", "https://mmbiz.qpic.cn/mmbiz_jpg/test_long_url_to_simulate_real_image_message_1234567890/0.jpg"),
            ("MediaId", "long_media_id_value_to_simulate_real_image_message_scenario"),
            ("FromUserName", "oUser_long_openid_12345678"),
            ("ToUserName", "gh_670eb053d515"),
            ("MsgId", "1234567890123456"),
            ("CreateTime", "1722767969")
        });

        var encryptedXml = EncryptWeChatMessage(innerXml, TestAESKey, TestAppId);

        var msg = WeChatMessageParser.Parse(encryptedXml, TestAESKey, TestAppId, out var error);

        Assert.NotNull(msg);
        Assert.Null(error);
        Assert.Equal("image", msg!.MsgType);
        Assert.Contains("test_long_url", msg.PicUrl);
        Assert.Equal("oUser_long_openid_12345678", msg.FromUserName);
        Assert.Equal("gh_670eb053d515", msg.ToUserName);
    }

    [Fact]
    public void Parse_EncryptedMessage_PaddingValue25_SimulatesProductionScenario()
    {
        // 模拟线上观测到的精确场景：padding 值 = 25（0x19）
        // 线上日志：末尾 16 字节全是 0x19，对应 padding 值 25
        // 通过调整内容长度，使 padding 值恰好为 25
        const int paddingTarget = 25;
        var innerXmlBase = BuildPlainXml(new[]
        {
            ("MsgType", "image"),
            ("PicUrl", "https://example.com/test.jpg"),
            ("MediaId", "media_123"),
            ("FromUserName", "oUser123"),
            ("ToUserName", "gh_test"),
            ("MsgId", "1234567890"),
            ("CreateTime", "1609459200")
        });

        var adjustedXml = AdjustMessageForTargetPadding(innerXmlBase, TestAppId, paddingTarget);
        var actualPadding = GetPaddingValueForMessage(adjustedXml, TestAppId);

        Assert.Equal(paddingTarget, actualPadding);

        var encryptedXml = EncryptWeChatMessage(adjustedXml, TestAESKey, TestAppId);
        var msg = WeChatMessageParser.Parse(encryptedXml, TestAESKey, TestAppId, out var error);

        Assert.NotNull(msg);
        Assert.Null(error);
        Assert.Equal("image", msg!.MsgType);
    }

    #endregion

    #region 加密模式 - AppId 校验

    [Fact]
    public void Parse_AppIdMismatch_ReturnsNullWithError()
    {
        var innerXml = BuildPlainXml(new[]
        {
            ("MsgType", "text"),
            ("Content", "test"),
            ("FromUserName", "u1"),
            ("MsgId", "1")
        });

        var encryptedXml = EncryptWeChatMessage(innerXml, TestAESKey, TestAppId);

        // 使用不同的 AppId 期望值
        var msg = WeChatMessageParser.Parse(encryptedXml, TestAESKey, "wx_different_appid", out var error);

        Assert.Null(msg);
        Assert.Contains("AppId", error ?? "");
    }

    [Fact]
    public void Parse_AppIdMatch_ReturnsMessage()
    {
        var innerXml = BuildPlainXml(new[]
        {
            ("MsgType", "text"),
            ("Content", "test"),
            ("FromUserName", "u1"),
            ("MsgId", "1")
        });

        var encryptedXml = EncryptWeChatMessage(innerXml, TestAESKey, TestAppId);

        var msg = WeChatMessageParser.Parse(encryptedXml, TestAESKey, TestAppId, out var error);

        Assert.NotNull(msg);
        Assert.Null(error);
    }

    [Fact]
    public void Parse_NoAppIdValidation_WhenExpectedAppIdIsNull_ReturnsMessage()
    {
        var innerXml = BuildPlainXml(new[]
        {
            ("MsgType", "text"),
            ("Content", "test"),
            ("FromUserName", "u1"),
            ("MsgId", "1")
        });

        var encryptedXml = EncryptWeChatMessage(innerXml, TestAESKey, TestAppId);

        // expectedAppId 为 null 时跳过 AppId 校验
        var msg = WeChatMessageParser.Parse(encryptedXml, TestAESKey, null, out var error);

        Assert.NotNull(msg);
        Assert.Null(error);
    }

    #endregion

    #region 加密模式 - 异常场景

    [Fact]
    public void Parse_EncryptedMessageWithWrongAESKey_ReturnsNullWithError()
    {
        var innerXml = BuildPlainXml(new[]
        {
            ("MsgType", "text"),
            ("Content", "test"),
            ("FromUserName", "u1"),
            ("MsgId", "1")
        });

        var encryptedXml = EncryptWeChatMessage(innerXml, TestAESKey, TestAppId);

        // 使用不同的 AESKey 解密
        var wrongKey = "AAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAA"; // 43 字符
        var msg = WeChatMessageParser.Parse(encryptedXml, wrongKey, TestAppId, out var error);

        Assert.Null(msg);
        Assert.False(string.IsNullOrEmpty(error));
    }

    [Fact]
    public void Parse_EncryptedMessageWithCorruptedCiphertext_ReturnsNullWithError()
    {
        var innerXml = BuildPlainXml(new[]
        {
            ("MsgType", "text"),
            ("Content", "test"),
            ("FromUserName", "u1"),
            ("MsgId", "1")
        });

        var encryptedXml = EncryptWeChatMessage(innerXml, TestAESKey, TestAppId);
        // 提取 Encrypt 节点内容，篡改几个字符
        var corruptEncrypt = encryptedXml.Replace("Encrypt", "Encrypt");
        // 直接修改 Base64 密文中的字符（替换为非法字符）
        var corrupted = corruptEncrypt.Replace('+', 'X'); // + 是合法 Base64，X 也是合法，但改变了密文内容
        if (corrupted == corruptEncrypt)
        {
            // 如果没有 + 字符，替换第一个字符
            corrupted = "Z" + corruptEncrypt[1..];
        }

        var msg = WeChatMessageParser.Parse(corrupted, TestAESKey, TestAppId, out var error);

        Assert.Null(msg);
        Assert.False(string.IsNullOrEmpty(error));
    }

    [Fact]
    public void Parse_EncryptedMessageWithNonBase64Char_ReturnsNullWithError()
    {
        var innerXml = BuildPlainXml(new[]
        {
            ("MsgType", "text"),
            ("Content", "test"),
            ("FromUserName", "u1"),
            ("MsgId", "1")
        });

        var encryptedXml = EncryptWeChatMessage(innerXml, TestAESKey, TestAppId);
        // 在 Encrypt CDATA 内容中插入空格（非法 Base64 字符，模拟代理服务器损坏）
        var corruptStart = encryptedXml.IndexOf("<![CDATA[", encryptedXml.IndexOf("Encrypt"), StringComparison.Ordinal) + 9;
        var corruptEnd = encryptedXml.IndexOf("]]>", corruptStart, StringComparison.Ordinal);
        var encryptContent = encryptedXml.Substring(corruptStart, corruptEnd - corruptStart);
        // 在密文中间插入空格
        var mid = encryptContent.Length / 2;
        var corruptedContent = encryptContent[..mid] + " " + encryptContent[mid..];
        var corrupted = encryptedXml[..corruptStart] + corruptedContent + encryptedXml[corruptEnd..];

        var msg = WeChatMessageParser.Parse(corrupted, TestAESKey, TestAppId, out var error);

        Assert.Null(msg);
        Assert.Contains("非 Base64", error ?? "");
    }

    [Fact]
    public void Parse_EncryptedMessageButNoAESKey_ReturnsNullWithError()
    {
        var innerXml = BuildPlainXml(new[]
        {
            ("MsgType", "text"),
            ("Content", "test"),
            ("FromUserName", "u1"),
            ("MsgId", "1")
        });

        var encryptedXml = EncryptWeChatMessage(innerXml, TestAESKey, TestAppId);

        var msg = WeChatMessageParser.Parse(encryptedXml, null, TestAppId, out var error);

        Assert.Null(msg);
        Assert.Contains("EncodingAESKey", error ?? "");
    }

    #endregion

    #region 被动回复构建

    [Fact]
    public void BuildTextReply_ReturnsValidXmlWithCorrectContent()
    {
        var reply = WeChatMessageParser.BuildTextReply("user1", "gh_test", "你好，世界");

        Assert.Contains("<ToUserName><![CDATA[user1]]></ToUserName>", reply);
        Assert.Contains("<FromUserName><![CDATA[gh_test]]></FromUserName>", reply);
        Assert.Contains("<MsgType><![CDATA[text]]></MsgType>", reply);
        Assert.Contains("<Content><![CDATA[你好，世界]]></Content>", reply);
        Assert.Contains("<CreateTime>", reply);
    }

    [Fact]
    public void BuildTextReply_EscapesSpecialXmlCharacters()
    {
        var reply = WeChatMessageParser.BuildTextReply("user1", "gh_test", "<test>&");

        Assert.Contains("&lt;test&gt;&amp;", reply);
    }

    #endregion

    #region 辅助方法

    /// <summary>
    /// 计算微信签名：token + timestamp + nonce 字典排序后 SHA1。
    /// </summary>
    private static string ComputeSignature(string token, string timestamp, string nonce)
    {
        var arr = new[] { token, timestamp, nonce };
        Array.Sort(arr, StringComparer.Ordinal);
        var joined = string.Concat(arr);
        var hash = SHA1.HashData(Encoding.UTF8.GetBytes(joined));
        return BitConverter.ToString(hash).Replace("-", "").ToLowerInvariant();
    }

    /// <summary>
    /// 构建明文微信 XML 消息。
    /// </summary>
    private static string BuildPlainXml((string Key, string Value)[] fields)
    {
        var sb = new StringBuilder("<xml>");
        foreach (var (key, value) in fields)
        {
            sb.Append($"<{key}><![CDATA[{value}]]></{key}>");
        }
        sb.Append("</xml>");
        return sb.ToString();
    }

    /// <summary>
    /// 模拟微信加密流程，使用 32 字节对齐的 PKCS7 填充。
    /// 加密格式：16字节随机 + 4字节长度(大端) + 消息内容 + AppId + PKCS7填充(32字节对齐)
    /// </summary>
    private static string EncryptWeChatMessage(string xml, string encodingAESKey, string appId)
    {
        var key = Convert.FromBase64String(encodingAESKey + "=");
        var xmlBytes = Encoding.UTF8.GetBytes(xml);
        var appIdBytes = Encoding.UTF8.GetBytes(appId);

        // 16 字节随机字符串 + 4 字节长度 + 消息 + AppId
        var contentLen = 16 + 4 + xmlBytes.Length + appIdBytes.Length;
        // PKCS7 填充按 32 字节对齐（微信的非标准实现）
        var paddingLen = 32 - (contentLen % 32);
        if (paddingLen == 0) paddingLen = 32; // PKCS7 至少填充 1 字节

        var plainText = new byte[contentLen + paddingLen];
        // 16 字节随机字符串
        RandomNumberGenerator.Fill(plainText.AsSpan(0, 16));
        // 4 字节消息长度（大端）
        var lenBytes = BitConverter.GetBytes(System.Net.IPAddress.HostToNetworkOrder(xmlBytes.Length));
        Array.Copy(lenBytes, 0, plainText, 16, 4);
        // 消息内容
        Array.Copy(xmlBytes, 0, plainText, 20, xmlBytes.Length);
        // AppId
        Array.Copy(appIdBytes, 0, plainText, 20 + xmlBytes.Length, appIdBytes.Length);
        // PKCS7 填充
        for (var i = 0; i < paddingLen; i++)
        {
            plainText[contentLen + i] = (byte)paddingLen;
        }

        // AES-CBC 加密，IV = key 前 16 字节
        using var aes = Aes.Create();
        aes.Key = key;
        aes.Mode = CipherMode.CBC;
        aes.Padding = PaddingMode.None; // 已手动填充
        aes.IV = key[..16];

        using var encryptor = aes.CreateEncryptor();
        var cipherText = encryptor.TransformFinalBlock(plainText, 0, plainText.Length);
        var encryptedBase64 = Convert.ToBase64String(cipherText);

        // 包装为微信加密 XML 格式
        return $"<xml><ToUserName><![CDATA[{appId}]]></ToUserName><Encrypt><![CDATA[{encryptedBase64}]]></Encrypt></xml>";
    }

    /// <summary>
    /// 计算给定消息的 PKCS7 padding 值（按微信 32 字节对齐）。
    /// </summary>
    private static int GetPaddingValueForMessage(string xml, string appId)
    {
        var xmlBytes = Encoding.UTF8.GetBytes(xml);
        var appIdBytes = Encoding.UTF8.GetBytes(appId);
        var contentLen = 16 + 4 + xmlBytes.Length + appIdBytes.Length;
        var paddingLen = 32 - (contentLen % 32);
        if (paddingLen == 0) paddingLen = 32;
        return paddingLen;
    }

    /// <summary>
    /// 调整 XML 内容长度，使消息的 PKCS7 padding 值等于目标值。
    /// 通过在 XML 末尾的注释中追加占位字符实现，不影响解析结果。
    /// padding = 32 - (contentLen % 32)，要让 padding = target：
    /// delta = (currentPadding - target) mod 32
    /// </summary>
    private static string AdjustMessageForTargetPadding(string xml, string appId, int targetPadding)
    {
        var currentPadding = GetPaddingValueForMessage(xml, appId);
        var delta = ((currentPadding - targetPadding) % 32 + 32) % 32;
        if (delta == 0) delta = 32; // 至少调整一次以避免原地踏步

        // 在 </xml> 前追加 XML 注释占位符，不影响解析结果
        // 注释格式：<!-- xxx -->，固定开销 9 字节，内容长度 = delta - 9（delta < 9 时用多个注释）
        var remaining = delta;
        var sb = new StringBuilder();
        while (remaining > 0)
        {
            if (remaining >= 9)
            {
                sb.Append("<!-- ").Append('x', remaining - 9).Append(" -->");
                remaining = 0;
            }
            else
            {
                // delta < 9 时无法用单个注释精确填充，改用空格（XML 元素间空白会被忽略）
                sb.Append(' ', remaining);
                remaining = 0;
            }
        }

        return xml.Replace("</xml>", sb + "</xml>");
    }

    #endregion
}
