using System.Reflection;
using FluentAssertions;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using NoneadCSS.Api.Authorization;
using NoneadCSS.Api.Controllers;
using Xunit;

namespace NoneadCSS.Tests.Controllers;

/// <summary>
/// 回收站恢复端点权限守卫反射测试（M-05）。
///
/// 背景：所有 ISoftDelete 实体均配置了全局软删除 QueryFilter（DeletedAt == null），
/// 恢复/审计场景通过 IgnoreQueryFilters 绕过该过滤器读取已删除数据。
/// 已删除的客户/会话/附件可能包含历史敏感数据，绝不能让普通用户通过恢复接口
/// 反向读取这些记录。因此每一个 Restore 端点必须显式标注提升后的授权属性。
///
/// 本测试扫描 NoneadCSS.Api 程序集中所有名字包含 "Restore" 的 HTTP 端点，
/// 断言其具备 [Permission] / [RequireSuperAdmin] / [RequireSuperAdminOrSupervisor]
/// 之一，防止未来新增恢复端点时遗漏授权注解导致越权数据泄露。
/// </summary>
public class RestoreEndpointPermissionGuardRailTests
{
    /// <summary>
    /// 标识 HTTP 请求的动词特性类型（恢复端点通常使用 POST，少数使用 PUT/PATCH）
    /// </summary>
    private static readonly Type[] HttpVerbAttributes =
    [
        typeof(HttpPostAttribute),
        typeof(HttpPutAttribute),
        typeof(HttpDeleteAttribute),
        typeof(HttpPatchAttribute)
    ];

    private static List<MethodInfo> GetRestoreEndpoints()
    {
        var apiAssembly = typeof(CustomReportsController).Assembly;

        return apiAssembly
            .GetTypes()
            .Where(t => typeof(ControllerBase).IsAssignableFrom(t))
            .SelectMany(t => t.GetMethods(BindingFlags.Public | BindingFlags.Instance | BindingFlags.DeclaredOnly))
            .Where(m => m.Name.Contains("Restore", StringComparison.OrdinalIgnoreCase))
            .Where(m => m.GetCustomAttributes().Any(a => HttpVerbAttributes.Contains(a.GetType())))
            .ToList();
    }

    /// <summary>
    /// 判断方法是否具备任意一种"提升后"授权属性。
    /// </summary>
    private static bool HasElevatedAuthorization(MethodInfo method)
    {
        return method.GetCustomAttribute<PermissionAttribute>() != null
            || method.GetCustomAttribute<RequireSuperAdminAttribute>() != null
            || method.GetCustomAttribute<RequireSuperAdminOrSupervisorAttribute>() != null
            || method.GetCustomAttribute<RequireSuperAdminOrSelfAttribute>() != null;
    }

    [Fact]
    public void AllRestoreEndpoints_ShouldHaveElevatedAuthorization()
    {
        var restoreEndpoints = GetRestoreEndpoints();

        restoreEndpoints.Should().NotBeEmpty(
            "应该存在至少一个恢复端点；若为空说明扫描逻辑或控件命名发生了变化");

        var missing = restoreEndpoints
            .Where(m => !HasElevatedAuthorization(m))
            .Select(m => $"{m.DeclaringType!.Name}.{m.Name}")
            .ToList();

        missing.Should().BeEmpty(
            "以下恢复端点缺少 [Permission] / [RequireSuperAdmin] / [RequireSuperAdminOrSupervisor] 授权注解，可能导致普通用户通过 IgnoreQueryFilters 读取已删除敏感数据: {0}",
            string.Join(", ", missing));
    }

    [Theory]
    [InlineData(nameof(CustomersController) + "." + nameof(CustomersController.RestoreCustomer))]
    [InlineData(nameof(KnowledgeController) + "." + nameof(KnowledgeController.RestoreArticle))]
    [InlineData(nameof(KnowledgeController) + "." + nameof(KnowledgeController.RestoreCategory))]
    [InlineData(nameof(ConversationsController) + "." + nameof(ConversationsController.Restore))]
    [InlineData(nameof(CalendarController) + "." + nameof(CalendarController.Restore))]
    [InlineData(nameof(ChatController) + "." + nameof(ChatController.RestoreSession))]
    public void RestoreEndpoint_ShouldHaveElevatedAuthorization(string qualifiedName)
    {
        var qualified = GetRestoreEndpoints()
            .Select(m => $"{m.DeclaringType!.Name}.{m.Name}")
            .FirstOrDefault(n => n == qualifiedName);

        qualified.Should().NotBeNull($"恢复端点 {qualifiedName} 应该存在");

        var method = GetRestoreEndpoints()
            .First(m => $"{m.DeclaringType!.Name}.{m.Name}" == qualifiedName);

        var hasElevated = HasElevatedAuthorization(method);
        hasElevated.Should().BeTrue(
            $"{qualifiedName} 必须标注 [Permission] / [RequireSuperAdmin] / [RequireSuperAdminOrSupervisor]");
    }

    [Fact]
    public void ChatController_RestoreSession_ShouldRequireSuperAdminOrSupervisor()
    {
        var method = GetRestoreEndpoints()
            .FirstOrDefault(m => m.Name == nameof(ChatController.RestoreSession));

        method.Should().NotBeNull("ChatController.RestoreSession 应该存在");

        var attr = method!.GetCustomAttribute<RequireSuperAdminOrSupervisorAttribute>();
        attr.Should().NotBeNull(
            "在线聊天会话恢复涉及读取已删除会话的历史消息（含敏感对话），必须限制为 SuperAdmin 或 AgentSupervisor，不能仅依赖 [Permission] 普通权限");
    }
}
