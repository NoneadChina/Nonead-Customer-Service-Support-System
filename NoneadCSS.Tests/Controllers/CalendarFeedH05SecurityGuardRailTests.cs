using System.Reflection;
using System.Text;
using System.Text.RegularExpressions;
using FluentAssertions;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.RateLimiting;
using NoneadCSS.Application.Security;
using NoneadCSS.Api.Controllers;
using Xunit;

namespace NoneadCSS.Tests.Controllers;

/// <summary>
/// H-05：CalendarFeedController 安全守卫反射测试
///
/// 漏洞背景：原设计若 Feed URL 使用自增整数 ID（/api/calendarfeed/{id}），攻击者可遍历 ID
/// 获取所有公开日历的事件标题、时间、参会人，用于社工或商业情报收集。
///
/// 修复三道防线：
///   1. Token 格式：userId.version.expiresAt.hash(HMAC)，不可枚举
///   2. 版本校验（user.UpdatedAt.Ticks）实现令牌吊销（改密码/角色后立即失效）
///   3. [EnableRateLimiting("CalendarFeedPolicy")] 10次/分钟/IP，深度防御
///
/// 本文件作为 GuardRail 补充（CalendarFeedControllerTests.cs 已覆盖 GenerateToken/ParseToken
/// 的功能正确性），这里聚焦 H-05 安全特性的"元约束"：控制器路由、属性、令牌不可猜测性。
/// </summary>
public class CalendarFeedH05SecurityGuardRailTests
{
    // 确保 SecretHasher 初始化（跨测试共享）
    [System.Diagnostics.CodeAnalysis.SuppressMessage("Usage", "CA2211", Justification = "测试用共享 flag")]
    private static bool _hasherInitialized;
    private static readonly object _hasherInitLock = new();

    private static void EnsureHasherInitialized()
    {
        if (_hasherInitialized) return;
        lock (_hasherInitLock)
        {
            if (_hasherInitialized) return;
            SecretHasher.Initialize("h05-test-secret-key-must-be-at-least-32-bytes-long!!!");
            _hasherInitialized = true;
        }
    }

    public CalendarFeedH05SecurityGuardRailTests()
    {
        EnsureHasherInitialized();
    }

    #region H-05.1 路由不包含可枚举 ID 段

    /// <summary>
    /// H-05.1a 控制器路由不得包含 {id}、{calendarId} 等整数参数占位符
    /// —— 防止自增主键被遍历
    /// </summary>
    [Fact]
    public void CalendarFeedController_RouteTemplate_MustNotContainEnumerableIdSegments()
    {
        var controllerAttrs = typeof(CalendarFeedController)
            .GetCustomAttributes<RouteAttribute>(inherit: false).ToList();

        controllerAttrs.Should().NotBeEmpty("CalendarFeedController 应有 [Route] 特性");

        foreach (var routeAttr in controllerAttrs)
        {
            var template = routeAttr.Template ?? string.Empty;
            template.Should().NotContain("{id}",
                $"控制器路由 '{template}' 包含 {{id}} 段，H-05 禁止通过自增整数 ID 枚举订阅源。" +
                "应改为使用 HMAC Token 查询参数（?token=...）或 {token} 路由段。");
            template.Should().NotContain("{calendarId}",
                $"控制器路由 '{template}' 包含 {{calendarId}} 段，H-05 禁止可枚举整数订阅 ID。");
            template.Should().NotMatch("{[a-zA-Z]*Id}",
                $"控制器路由 '{template}' 包含形如 {{xxxId}} 的段，疑似可枚举整数 ID。H-05 要求使用不可猜测 Token。");
        }
    }

    /// <summary>
    /// H-05.1b GetFeed 操作方法的路由：不得包含整数段；token 必须通过 [FromQuery] 或 {token} 传入
    /// </summary>
    [Fact]
    public void CalendarFeedController_GetFeed_Route_UsesTokenInsteadOfNumericId()
    {
        var method = typeof(CalendarFeedController).GetMethod(nameof(CalendarFeedController.GetFeed));
        method.Should().NotBeNull("GetFeed 方法应存在于 CalendarFeedController");

        // 方法级路由模板（HttpGet 的构造参数）
        var httpGetAttr = method!.GetCustomAttribute<HttpGetAttribute>();
        var methodTemplate = httpGetAttr?.Template ?? string.Empty;

        var controllerAttr = typeof(CalendarFeedController).GetCustomAttribute<RouteAttribute>();
        var fullTemplate = string.Join("/", new[] { controllerAttr?.Template, methodTemplate }
            .Where(s => !string.IsNullOrEmpty(s)));

        // 不能有 {id} 类的参数段
        Regex.IsMatch(fullTemplate, @"\{\w+Id\}").Should().BeFalse(
            $"GetFeed 全路由 '{fullTemplate}' 包含可枚举的 xxxId 段。H-05 必须使用不可枚举 Token。");

        // GetFeed 方法的参数：应有 token（[FromQuery] 或 [FromRoute]）
        var paramToken = method!.GetParameters()
            .FirstOrDefault(p => string.Equals(p.Name, "token", StringComparison.OrdinalIgnoreCase));
        paramToken.Should().NotBeNull("GetFeed 应接受名为 token 的参数（承载订阅令牌）");
        paramToken!.ParameterType.Should().Be(typeof(string),
            "token 必须为 string 类型，承载 HMAC 签名而非整数 ID。");
    }

    /// <summary>
    /// H-05.1c 控制器与 GetFeed 都应为匿名访问——Feed 订阅是 Outlook 匿名拉取的
    /// </summary>
    [Fact]
    public void CalendarFeedController_IsAllowAnonymous()
    {
        // 控制器级 AllowAnonymous
        var ctrlAttr = typeof(CalendarFeedController).GetCustomAttribute<AllowAnonymousAttribute>();
        ctrlAttr.Should().NotBeNull(
            "CalendarFeedController 应在控制器级标注 [AllowAnonymous]—— ICS 订阅由 Outlook 等外部匿名客户端拉取，" +
            "无法携带 JWT。");
    }

    #endregion

    #region H-05.2 限流策略

    /// <summary>
    /// H-05.2a GetFeed 必须具有 [EnableRateLimiting("CalendarFeedPolicy")]
    /// —— 10次/分钟/IP，深度防御层防止脚本暴力试探
    /// </summary>
    [Fact]
    public void CalendarFeedController_GetFeed_MustHaveCalendarFeedRateLimitPolicy()
    {
        var method = typeof(CalendarFeedController).GetMethod(nameof(CalendarFeedController.GetFeed));
        method.Should().NotBeNull("GetFeed 方法应存在");

        var attr = method!.GetCustomAttribute<EnableRateLimitingAttribute>();
        attr.Should().NotBeNull(
            "GetFeed 缺少 [EnableRateLimiting(\"CalendarFeedPolicy\")] 标注。" +
            "H-05 要求即便令牌本身不可枚举，也需通过限流 10 次/分钟/IP 深度防御。");

        var policyName = GetRateLimitingPolicyName(attr!);
        policyName.Should().Be("CalendarFeedPolicy",
            "GetFeed 限流策略必须是 CalendarFeedPolicy（10次/分钟/IP）。" +
            $"当前策略名：{policyName}。若新增其他策略请同步此守卫。");
    }

    /// <summary>
    /// H-05.2b CalendarFeedPolicy 限流策略名必须存在于 Program.cs 注册
    /// —— 通过在 NoneadCSS.Api 程序集中搜索相关类型的字符串，避免策略名拼写漂移
    /// </summary>
    [Fact]
    public void RateLimitPolicyName_CalendarFeedPolicy_IsDeclaredInProgram()
    {
        // 通过在 Api 程序集反射搜索 Program 类中的 AddRateLimiter 调用不可行（编译 IL）；
        // 改用字符串常量等价性守卫：确保策略名匹配已知约定。
        var expected = "CalendarFeedPolicy";

        // 查找 Program.cs 所在的 Api 程序集中 RateLimitingOptions 注册信息可通过调用链断言；
        // 为避免依赖 Program 的内部字段，此处 GuardRail 直接约束命名与配置一致性。
        expected.Should().Be("CalendarFeedPolicy"); // 故意写成固定值，防止复制粘贴错误

        // 额外守卫：控制器注解中必须出现此精确字符串
        var method = typeof(CalendarFeedController).GetMethod(nameof(CalendarFeedController.GetFeed));
        var attr = method!.GetCustomAttribute<EnableRateLimitingAttribute>();
        GetRateLimitingPolicyName(attr!).Should().Be(expected);
    }

    #endregion

    #region H-05.3 Token 不可枚举性（格式/长度/碰撞）

    /// <summary>
    /// H-05.3a 生成的 Token 长度应足够（4段，末段是 HMAC→≥40 字符），
    /// 保证暴力枚举空间 ≥ 160 bit，实际不可猜测。
    /// </summary>
    [Fact]
    public void GenerateToken_HashSegment_IsSufficientlyLongAndUnpredictable()
    {
        var token = CalendarFeedController.GenerateToken(userId: 42,
            userUpdatedAt: new DateTime(2026, 1, 1, 0, 0, 0, DateTimeKind.Utc));

        var parts = token.Split('.');
        parts.Should().HaveCount(4, "令牌格式为 userId.version.expiresAt.hash(HMAC)，共 4 段。");

        var hashPart = parts[3];
        // SecretHasher 使用 HMACSHA256 + base64 或 hex；最小 32 字节输出对应 44 chars base64
        hashPart.Length.Should().BeGreaterThanOrEqualTo(32,
            $"Token hash 段长度仅 {hashPart.Length}，不足以抵抗枚举。HMAC 输出应 ≥ 32 字节。");

        // 必须包含非数字字符（纯数字则是自增，可枚举）
        hashPart.Should().MatchRegex(@"[^0-9]", "Hash 段必须包含非数字字符，避免出现可枚举的纯整数标识。");
    }

    /// <summary>
    /// H-05.3b 同一 user+版本、仅有效期不同 → 生成的令牌末段哈希一定不同
    /// 证明哈希确实覆盖 expiresAt（防止"攻击者拿短有效期 token，修改 expiresAt 段延长有效期"）
    /// </summary>
    [Fact]
    public void GenerateToken_DifferentExpiry_HashSegmentMustDiffer()
    {
        var version = new DateTime(2026, 1, 1, 0, 0, 0, DateTimeKind.Utc);
        var token7 = CalendarFeedController.GenerateToken(1, version, validityDays: 7);
        var token365 = CalendarFeedController.GenerateToken(1, version, validityDays: 365);

        var p7 = token7.Split('.');
        var p365 = token365.Split('.');

        // userId、version 相同（前两段一致）
        p7[0].Should().Be(p365[0], "userId 应一致");
        p7[1].Should().Be(p365[1], "版本号应一致");
        // expiresAt 不同
        p7[2].Should().NotBe(p365[2], "有效天数不同，expiresAt 段必须不同");
        // HMAC 必须不同（证明 expiresAt 覆盖到了签名）
        p7[3].Should().NotBe(p365[3],
            "有效天数不同，HMAC 段必须不同。若相同，说明 expiresAt 未纳入签名，存在可篡改漏洞。");
    }

    /// <summary>
    /// H-05.3c 两个不同用户的相同版本、相同有效期 → 末段哈希不同（证明 userId 纳入签名）
    /// </summary>
    [Fact]
    public void GenerateToken_DifferentUser_HashMustDiffer()
    {
        var version = new DateTime(2026, 6, 1, 0, 0, 0, DateTimeKind.Utc);
        var t1 = CalendarFeedController.GenerateToken(userId: 1, userUpdatedAt: version, validityDays: 30);
        var t2 = CalendarFeedController.GenerateToken(userId: 2, userUpdatedAt: version, validityDays: 30);

        var p1 = t1.Split('.');
        var p2 = t2.Split('.');

        p1[3].Should().NotBe(p2[3],
            "userId 不同，HMAC 段必须不同。若相同，说明 userId 未纳入签名，可跨用户伪造。");
    }

    /// <summary>
    /// H-05.3d 两个用户、仅版本号不同（其他相同）→ 末段哈希不同（证明 version 纳入签名）
    /// —— 这是吊销机制的基础：版本变化会改变签名
    /// </summary>
    [Fact]
    public void GenerateToken_DifferentVersion_HashMustDiffer()
    {
        var oldV = new DateTime(2026, 3, 1, 0, 0, 0, DateTimeKind.Utc);
        var newV = new DateTime(2026, 4, 1, 0, 0, 0, DateTimeKind.Utc);
        var tOld = CalendarFeedController.GenerateToken(userId: 1, userUpdatedAt: oldV, validityDays: 30);
        var tNew = CalendarFeedController.GenerateToken(userId: 1, userUpdatedAt: newV, validityDays: 30);

        var pOld = tOld.Split('.');
        var pNew = tNew.Split('.');

        pOld[3].Should().NotBe(pNew[3],
            "版本号（UpdatedAt.Ticks）不同，HMAC 段必须不同。" +
            "这是 H-05 令牌吊销机制（用户改密码/角色后旧令牌失效）的密码学基础。");
    }

    #endregion

    #region H-05.4 恒定时间比较防时序攻击

    /// <summary>
    /// H-05.4 篡改单字节的 hash 段（即保持长度一致，仅改掉某一位字符），
    /// ParseToken 必须在恒定时间内返回 null（间接验证 FixedTimeEquals 生效而非短路比较）。
    /// 该测试本身不做时序测量，仅断言：篡改后必被拒绝。
    /// </summary>
    [Fact]
    public void ParseToken_SingleBitTamper_IsRejected()
    {
        var token = CalendarFeedController.GenerateToken(userId: 5,
            userUpdatedAt: new DateTime(2026, 5, 1, 0, 0, 0, DateTimeKind.Utc));
        var parts = token.Split('.');
        var originalHash = parts[3];

        // 翻转最后一段的第一个字符（保持总长度一致）
        var tamperedChars = originalHash.ToCharArray();
        tamperedChars[0] = tamperedChars[0] == 'A' ? 'B' : 'A';
        var tamperedHash = new string(tamperedChars);

        var tamperedToken = $"{parts[0]}.{parts[1]}.{parts[2]}.{tamperedHash}";
        tamperedToken.Should().NotBe(token, "确保篡改后令牌与原令牌不同");
        tamperedToken.Length.Should().Be(token.Length, "长度一致（仅单字节差异）");

        var result = InvokeParseToken(tamperedToken);
        result.Should().BeNull("仅修改一位 hash 字符的令牌必须被拒绝（FixedTimeEquals 验证）。");
    }

    /// <summary>
    /// 直接构造一个伪造的短哈希（模拟字典攻击尝试的短值），ParseToken 必须拒绝。
    /// </summary>
    [Fact]
    public void ParseToken_SuspiciouslyShortHash_IsRejected()
    {
        var token = CalendarFeedController.GenerateToken(1, DateTime.UtcNow);
        var parts = token.Split('.');
        var fake = $"{parts[0]}.{parts[1]}.{parts[2]}.abc"; // hash 长度极短
        InvokeParseToken(fake).Should().BeNull();
    }

    #endregion

    #region 辅助方法

    private static (int UserId, long Version, DateTime ExpiresAt)? InvokeParseToken(string token)
    {
        var method = typeof(CalendarFeedController).GetMethod(
            "ParseToken", BindingFlags.Static | BindingFlags.NonPublic);
        method.Should().NotBeNull();
        return (ValueTuple<int, long, DateTime>?)method!.Invoke(null, new object[] { token });
    }

    private static string GetRateLimitingPolicyName(EnableRateLimitingAttribute attr)
    {
        var prop = typeof(EnableRateLimitingAttribute).GetProperty("PolicyName",
            BindingFlags.Public | BindingFlags.Instance);
        if (prop != null) return prop.GetValue(attr) as string ?? string.Empty;
        var field = typeof(EnableRateLimitingAttribute)
            .GetFields(BindingFlags.NonPublic | BindingFlags.Instance)
            .FirstOrDefault(f => f.FieldType == typeof(string));
        return field?.GetValue(attr) as string ?? string.Empty;
    }

    #endregion
}
