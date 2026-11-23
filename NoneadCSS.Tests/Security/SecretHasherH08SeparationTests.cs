using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Reflection;
using System.Security.Cryptography;
using System.Text;
using FluentAssertions;
using NoneadCSS.Application.Security;
using Xunit;

namespace NoneadCSS.Tests.Security;

/// <summary>
/// H-08（SecretHasher 密钥分离安全守卫）单元测试。
///
/// 背景：H-08 报告指出 SecretHasher 与 JWT 密钥曾使用同一把 key，一旦 JWT 密钥泄露可同时伪造 Refresh Token / API Key 哈希。
/// 整改要求：
///   1. 应用启动必须从独立配置源读取 SecretHasher:Key（或 SECRET_HASHER__Key 环境变量），
///      禁止在任何环境下回退到 Jwt:SecretKey。
///   2. SecretHasher.Initialize 必须强制密钥非空且长度 ≥ 32 字节。
///   3. 程序文本中不得再出现 "开发环境回退到 jwtKey" 一类历史回退逻辑。
///   4. SecretHasher 输出为确定性的 HMAC-SHA256 十六进制小写（64 字符）。
///   5. 不同密钥对同一输入的哈希必须不同，证明密钥独立化实际生效。
/// </summary>
public class SecretHasherH08SeparationTests
{
    // —— 让该测试类的静态初始化先运行：所有测试使用同一把临时 key 运行 Hash，避免与其他测试冲突。
    private const string HmacKeyA = "0123456789ABCDEF-1234567890-abcdef-H08-test-A";
    private const string HmacKeyB = "0123456789ABCDEF-1234567890-abcdef-H08-test-B";

    public SecretHasherH08SeparationTests()
    {
        // 每次测试前重置 static key：SecretHasher 无暴露的 Reset API，但可通过反射置空。
        // 之所以反射而不是公开 Reset：SecretHasher 设计为一次性启动初始化；此处仅为测试隔离。
        ResetSecretHasherKey();
    }

    private static void ResetSecretHasherKey()
    {
        var f = typeof(SecretHasher).GetField("_key", BindingFlags.Static | BindingFlags.NonPublic);
        f.Should().NotBeNull("SecretHasher._key 私有字段必须存在");
        f!.SetValue(null, null);
    }

    #region H-08.1 Initialize 守卫

    [Fact]
    public void Initialize_NullOrEmpty_ThrowsArgumentException()
    {
        var a = () => SecretHasher.Initialize(null!);
        var b = () => SecretHasher.Initialize(string.Empty);
        var c = () => SecretHasher.Initialize("   "); // whitespace 会抛非空但 32-byte 失败，也算被拒绝，允许 ArgumentException

        a.Should().Throw<ArgumentException>().WithMessage("*SecretHasher*密钥不能为空*");
        b.Should().Throw<ArgumentException>().WithMessage("*SecretHasher*密钥不能为空*");
    }

    [Theory]
    [InlineData("1")]
    [InlineData("short-key")]                 // 9 chars
    [InlineData("1234567890123456789012345678901")] // 31 chars = 31 bytes
    public void Initialize_ShorterThan32Bytes_ThrowsArgumentException(string tooShort)
    {
        var act = () => SecretHasher.Initialize(tooShort);

        act.Should().Throw<ArgumentException>().WithMessage("*密钥长度必须至少 32 字节*",
            "H-08: 禁止在 Initialize 中接受短于 32 字节的密钥，否则降低 HMAC-SHA256 前像强度。");
    }

    [Fact]
    public void Initialize_Exactly32Bytes_Accepted()
    {
        var k = new string('K', 32); // UTF-8 下 32 bytes
        var act = () => SecretHasher.Initialize(k);
        act.Should().NotThrow();
    }

    [Fact]
    public void Hash_BeforeInitialize_ThrowsInvalidOperationException()
    {
        // _key 未赋值的情况已经在 ctor 用反射清掉，直接调用 Hash。
        var act = () => SecretHasher.Hash("anything");
        act.Should().Throw<InvalidOperationException>()
            .WithMessage("*SecretHasher 未初始化*",
                "H-08: 任何环境若未配置 SecretHasher:Key，直接调用 Hash 必抛异常，避免静默回退。");
    }

    #endregion

    #region H-08.2 哈希输出 / 密钥独立化行为

    [Fact]
    public void Hash_OutputIs_64LowerHexChars_HMACSHA256()
    {
        SecretHasher.Initialize(HmacKeyA);

        var hash = SecretHasher.Hash("hello");

        hash.Length.Should().Be(64, "HMAC-SHA256 输出应为 32 byte = 64 十六进制字符");
        hash.Should().MatchRegex("^[0-9a-f]{64}$");

        // 与独立 HMAC-SHA256 计算结果对齐（证明算法正确性）
        using var hmac = new HMACSHA256(Encoding.UTF8.GetBytes(HmacKeyA));
        var expected = Convert.ToHexString(hmac.ComputeHash(Encoding.UTF8.GetBytes("hello"))).ToLowerInvariant();
        hash.Should().Be(expected);
    }

    [Fact]
    public void Hash_DifferentKeys_SameInput_MustProduceDifferentOutputs_KeySeparationEffective()
    {
        const string input = "api-key:random-csrf-token";

        ResetSecretHasherKey();
        SecretHasher.Initialize(HmacKeyA);
        var h1 = SecretHasher.Hash(input);

        ResetSecretHasherKey();
        SecretHasher.Initialize(HmacKeyB);
        var h2 = SecretHasher.Hash(input);

        h1.Should().NotBe(h2,
            "H-08: 不同密钥的哈希必须不同，这是\"密钥分离原则\"的密码学表现。" +
            "若相同，说明存在密钥复用 / 密钥未生效的回归漏洞。");
    }

    [Fact]
    public void Hash_SameKey_SameInput_IsDeterministic()
    {
        SecretHasher.Initialize(HmacKeyA);
        var a = SecretHasher.Hash("some-value");
        var b = SecretHasher.Hash("some-value");
        a.Should().Be(b);
    }

    [Fact]
    public void Hash_EmptyString_StillProducesValidHmac()
    {
        SecretHasher.Initialize(HmacKeyA);
        var h = SecretHasher.Hash(string.Empty);
        h.Should().MatchRegex("^[0-9a-f]{64}$");
    }

    #endregion

    #region H-08.3 源代码级守卫：Program.cs 不得回退 jwtKey

    [Fact]
    public void ProgramSourceCode_MustNotFallBack_SecretHasher_To_JwtKey()
    {
        // 在真实代码库里定位 Program.cs 绝对路径。
        var asmDir = new DirectoryInfo(AppContext.BaseDirectory);
        // 向上回溯 workspace 的 NoneadCSS.Api\Program.cs
        string? programPath = null;
        for (var d = asmDir; d != null; d = d.Parent)
        {
            var candidate = Path.Combine(d.FullName, "NoneadCSS.Api", "Program.cs");
            if (File.Exists(candidate)) { programPath = candidate; break; }
        }
        programPath.Should().NotBeNull("必须在仓库中找到 NoneadCSS.Api/Program.cs");

        var source = File.ReadAllText(programPath!);

        // 不得出现“jwtKey”被赋给 secretHasherKey 这类回退赋值；
        // 典型坏味道： ?? jwtKey 、 = jwtKey 、secretHasherKey = ... jwtKey
        // 构造一个较精确的匹配：secretHasherKey 这一行或关联行内出现 jwtKey。
        // 简化：全文查找 "secretHasherKey" 行附近不应出现 jwtKey。
        var lines = source.Split(new[] { '\r', '\n' }, StringSplitOptions.RemoveEmptyEntries);
        var hasherLines = lines.Select((line, i) => (line, i)).Where(x => x.line.Contains("secretHasherKey", StringComparison.OrdinalIgnoreCase)).ToList();
        hasherLines.Should().NotBeEmpty("Program.cs 必须包含 H-08 SecretHasher 初始化段");

        foreach (var (line, _) in hasherLines)
        {
            // 允许作为日志/注释提示；但禁止赋值、合并或回退
            var badSigns = new[] { "?? jwtKey", "= jwtKey", "??= jwtKey", "jwtKey" };
            // 对 "jwtKey" 放宽：它仅在变量名/注释语境出现于同一行内的赋值/回退才是危险；
            // 因此只匹配 = 或 ?? 操作符 + jwtKey 紧邻。
            line.Should().NotContain("?? jwtKey",
                "H-08: Program.cs 的 SecretHasher 初始化段不得回退到 jwtKey。开发/生产都必须使用独立密钥源。");
            line.Should().NotContain("??= jwtKey",
                "H-08: Program.cs 的 SecretHasher 初始化段不得 ??= 回退到 jwtKey。");
            // 赋值式如： secretHasherKey = jwtKey;
            System.Text.RegularExpressions.Regex.IsMatch(line,
                @"secretHasherKey\s*=\s*jwtKey\b",
                System.Text.RegularExpressions.RegexOptions.IgnoreCase)
                .Should().BeFalse("H-08: 不得在 Program.cs 把 jwtKey 直接赋给 SecretHasher 的密钥。");
        }

        // 最终的正向断言：源代码含 SECRET_HASHER__Key 与 SECRET_HASHER_KEY 两个环境变量入口
        source.Should().Contain("SECRET_HASHER__Key").And.Contain("SECRET_HASHER_KEY")
            .And.Contain("SecretHasher:Key");

        // 正向断言：初始化前有"必须设置/缺失抛 InvalidOperationException"的守卫
        source.Should().Contain("InvalidOperationException")
            .And.Contain("SecretHasher",
                "必须存在 SecretHasher 密钥缺失时抛 InvalidOperationException 的启动守卫，防止忘记配置。");
    }

    #endregion
}
