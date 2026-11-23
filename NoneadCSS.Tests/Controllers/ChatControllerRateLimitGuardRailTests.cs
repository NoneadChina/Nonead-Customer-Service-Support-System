using System.Reflection;
using FluentAssertions;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.RateLimiting;
using NoneadCSS.Api.Controllers;
using Xunit;

namespace NoneadCSS.Tests.Controllers;

/// <summary>
/// H-02：ChatController 匿名端点速率限制守卫测试（反射级 GuardRail）
///
/// 背景：客户侧聊天接口（初始化会话、发送消息、拉取历史、满意度提交等）全部匿名，
/// 若遗漏 [EnableRateLimiting] 标注将仅受全局 300 次/分钟 保护（过于宽松）。
/// 恶意机器人可批量创建会话、发送垃圾消息、拖库满意度历史。
///
/// 测试覆盖：
///   1. 写操作 initiate / send-message → 必须使用严格策略 ChatClientStrictPolicy（10 次/分钟）
///   2. 其余 7 个匿名客户端端点 → 必须使用 ChatClientPolicy（30 次/分钟）或更严格
///   3. 所有 [AllowAnonymous] 的公共 Action 方法 → 必须标注任一 EnableRateLimiting 策略
///   4. 明确非匿名端点（客服端 JWT 鉴权） → 不要求限流标注（业务层 Authorization 负责）
/// </summary>
public class ChatControllerRateLimitGuardRailTests
{
    /// <summary>
    /// 必须使用严格策略 ChatClientStrictPolicy 的端点名称（写操作，风险更高）
    /// </summary>
    private static readonly string[] StrictPolicyEndpoints =
    [
        nameof(ChatController.InitiateChat),
        nameof(ChatController.SendCustomerMessage),
    ];

    /// <summary>
    /// 其他匿名端点（可使用 ChatClientPolicy 或更严格）
    /// —— 方法名需匹配 ChatController 的公共 Action 方法名
    /// </summary>
    private static readonly string[] AnonymousClientEndpoints =
    [
        nameof(ChatController.InitiateChat),
        nameof(ChatController.SendCustomerMessage),
        nameof(ChatController.GetCustomerMessages),
        nameof(ChatController.CloseSessionByToken),
        nameof(ChatController.RateChat),
        nameof(ChatController.MarkAsReadByToken),
        nameof(ChatController.NotifyTypingByToken),
        nameof(ChatController.GetCustomerSessionByToken),
    ];

    private static List<MethodInfo> GetAllPublicActionMethods()
    {
        return typeof(ChatController)
            .GetMethods(BindingFlags.Public | BindingFlags.Instance | BindingFlags.DeclaredOnly)
            // Action 方法：有返回值且返回 Task<...> / ActionResult / IActionResult
            .Where(m => m.ReturnType.IsClass || typeof(IActionResult).IsAssignableFrom(m.ReturnType) ||
                        (m.ReturnType.IsGenericType &&
                         (typeof(Task).IsAssignableFrom(m.ReturnType.GetGenericTypeDefinition() ?? m.ReturnType))))
            .ToList();
    }

    /// <summary>
    /// H-02.1 最关键守卫：所有标记 [AllowAnonymous] 的方法必须有 [EnableRateLimiting] 注解
    /// —— 防止新增匿名端点时遗忘限流，成为全局 300/min 宽松保护下的滥用口子
    /// </summary>
    [Fact]
    public void ChatController_AllAllowAnonymousActions_MustHaveEnableRateLimitingAttribute()
    {
        // Arrange
        var methods = typeof(ChatController)
            .GetMethods(BindingFlags.Public | BindingFlags.Instance | BindingFlags.DeclaredOnly)
            .Where(m => m.GetCustomAttribute<AllowAnonymousAttribute>() != null)
            .ToList();

        methods.Should().NotBeEmpty("ChatController 至少应有若干匿名客户端端点（initiate、send-message、history 等）");

        var missing = methods
            .Where(m => m.GetCustomAttribute<EnableRateLimitingAttribute>() == null)
            .Select(m => m.Name)
            .ToList();

        missing.Should().BeEmpty(
            "以下匿名端点缺少 [EnableRateLimiting] 标注，H-02 不允许匿名端点无速率限制：{0}",
            string.Join(", ", missing));
    }

    /// <summary>
    /// H-02.2 initiate / send-message 两个写操作必须用 ChatClientStrictPolicy（10 次/分钟）
    /// —— 禁止被误改为宽松的 ChatClientPolicy
    /// </summary>
    [Theory]
    [InlineData(nameof(ChatController.InitiateChat))]
    [InlineData(nameof(ChatController.SendCustomerMessage))]
    public void ChatController_WriteAnonActions_MustUseChatClientStrictPolicy(string methodName)
    {
        var method = typeof(ChatController).GetMethod(methodName);
        method.Should().NotBeNull($"方法 {methodName} 应存在于 ChatController");

        var attr = method!.GetCustomAttribute<EnableRateLimitingAttribute>();
        attr.Should().NotBeNull($"{methodName} 应有 [EnableRateLimiting] 属性（H-02 严格策略）");

        // 注意：EnableRateLimitingAttribute 的 PolicyName 为构造参数（第一个）
        var policyName = GetRateLimitingPolicyName(attr!);
        policyName.Should().Be("ChatClientStrictPolicy",
            $"{methodName} 是写操作（会话创建或消息发送），H-02 要求使用严格策略 ChatClientStrictPolicy (10次/分钟)，" +
            $"而不是宽松的 ChatClientPolicy (30次/分钟)。");
    }

    /// <summary>
    /// H-02.3 其余匿名端点（历史记录、关闭、满意度、已读、typing、session）应至少使用 ChatClientPolicy
    /// —— 即策略为 ChatClientPolicy 或 ChatClientStrictPolicy 之一
    /// </summary>
    [Theory]
    [InlineData(nameof(ChatController.GetCustomerMessages))]
    [InlineData(nameof(ChatController.CloseSessionByToken))]
    [InlineData(nameof(ChatController.RateChat))]
    [InlineData(nameof(ChatController.MarkAsReadByToken))]
    [InlineData(nameof(ChatController.NotifyTypingByToken))]
    [InlineData(nameof(ChatController.GetCustomerSessionByToken))]
    public void ChatController_ReadOrLightWriteAnonActions_MustUseAtLeastChatClientPolicy(string methodName)
    {
        var method = typeof(ChatController).GetMethod(methodName);
        method.Should().NotBeNull($"方法 {methodName} 应存在于 ChatController");

        // 必须是匿名端点
        method!.GetCustomAttribute<AllowAnonymousAttribute>()
            .Should().NotBeNull($"{methodName} 应该是客户端匿名端点");

        var attr = method!.GetCustomAttribute<EnableRateLimitingAttribute>();
        attr.Should().NotBeNull($"{methodName} 应有 [EnableRateLimiting] 属性");

        var policyName = GetRateLimitingPolicyName(attr!);
        new[] { "ChatClientPolicy", "ChatClientStrictPolicy" }
            .Should().Contain(policyName,
                $"{methodName} 匿名端点必须使用 ChatClientPolicy 或更严格的 ChatClientStrictPolicy，当前策略：{policyName}");
    }

    /// <summary>
    /// H-02.4 严格策略端点绝对不能使用宽松的 ChatClientPolicy（双保险：防止 StrictPolicyEndpoints
    /// 列表与实现不一致，对所有写操作匿名端点逐一反向校验）
    /// </summary>
    [Fact]
    public void ChatController_StrictPolicyEndpoints_MustNotUseLooseChatClientPolicy()
    {
        var looseEndpoints = StrictPolicyEndpoints
            .Select(name => typeof(ChatController).GetMethod(name))
            .Where(m => m != null)
            .Where(m =>
            {
                var policy = GetRateLimitingPolicyName(m!.GetCustomAttribute<EnableRateLimitingAttribute>()!);
                return policy == "ChatClientPolicy"; // 禁止用宽松策略
            })
            .Select(m => m!.Name)
            .ToList();

        looseEndpoints.Should().BeEmpty(
            "以下高风险写操作端点被配置为宽松策略 ChatClientPolicy (30次/分钟)，" +
            "H-02 要求必须使用 ChatClientStrictPolicy (10次/分钟)：{0}",
            string.Join(", ", looseEndpoints));
    }

    /// <summary>
    /// H-02.5 校验匿名端点数量：ChatController 应恰好有 9 个 AllowAnonymous 方法
    /// —— 新增匿名端点时此测试会失败，提醒开发者补充限流并加入理论列表
    /// </summary>
    [Fact]
    public void ChatController_AnonymousEndpointCount_ShouldMatchKnownList()
    {
        var anonMethods = typeof(ChatController)
            .GetMethods(BindingFlags.Public | BindingFlags.Instance | BindingFlags.DeclaredOnly)
            .Where(m => m.GetCustomAttribute<AllowAnonymousAttribute>() != null)
            .Select(m => m.Name)
            .OrderBy(n => n)
            .ToList();

        var expected = AnonymousClientEndpoints.Distinct().OrderBy(n => n).ToList();

        anonMethods.Should().BeEquivalentTo(expected,
            options => options.WithStrictOrdering(),
            "匿名端点清单发生变化：若新增了匿名端点，请在 H-02 测试文件的 AnonymousClientEndpoints 列表补充、" +
            "并确保 [EnableRateLimiting] 策略标注正确（写操作用 StrictPolicy，其它用 ChatClientPolicy）。" +
            $"当前匿名端点：[{string.Join(", ", anonMethods)}]");
    }

    /// <summary>
    /// 辅助：从 EnableRateLimitingAttribute 获取 PolicyName
    /// （属性本身只有构造参数，无公开 Property；通过字段/构造参数反射获取）
    /// </summary>
    private static string GetRateLimitingPolicyName(EnableRateLimitingAttribute attr)
    {
        // EnableRateLimitingAttribute 有公共属性 PolicyName（ASP.NET Core 7+ 确认）
        var prop = typeof(EnableRateLimitingAttribute).GetProperty("PolicyName",
            BindingFlags.Public | BindingFlags.Instance);
        if (prop != null)
        {
            return prop.GetValue(attr) as string ?? string.Empty;
        }
        // 回退：构造参数（第一个参数即策略名）
        var field = typeof(EnableRateLimitingAttribute)
            .GetFields(BindingFlags.NonPublic | BindingFlags.Instance)
            .FirstOrDefault(f => f.FieldType == typeof(string));
        return field?.GetValue(attr) as string ?? string.Empty;
    }
}
