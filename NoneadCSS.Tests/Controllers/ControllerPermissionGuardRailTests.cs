using System.Reflection;
using FluentAssertions;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.Extensions.Caching.Memory;
using Microsoft.Extensions.Options;
using Moq;
using NoneadCSS.Api.Authorization;
using NoneadCSS.Api.Controllers;
using NoneadCSS.Application.Configuration;
using NoneadCSS.Application.Services;
using NoneadCSS.Domain;
using NoneadCSS.Domain.Enums;
using Xunit;

namespace NoneadCSS.Tests.Controllers;

/// <summary>
/// 控制器权限守卫反射测试
/// 确保所有写操作端点（POST/PUT/DELETE/PATCH）都标注了 [Permission] 特性，
/// 防止未来重构时意外删除授权注解导致越权风险。
/// </summary>
public class ControllerPermissionGuardRailTests
{
    /// <summary>
    /// 标识写操作的 HTTP 方法特性类型
    /// </summary>
    private static readonly Type[] WriteHttpVerbAttributes =
    [
        typeof(HttpPostAttribute),
        typeof(HttpPutAttribute),
        typeof(HttpDeleteAttribute),
        typeof(HttpPatchAttribute)
    ];

    /// <summary>
    /// 获取控制器中所有标注了写操作 HTTP 动词特性的公共方法
    /// </summary>
    private static List<MethodInfo> GetWriteEndpoints(Type controllerType)
    {
        return controllerType
            .GetMethods(BindingFlags.Public | BindingFlags.Instance | BindingFlags.DeclaredOnly)
            .Where(m => m.GetCustomAttributes().Any(a => WriteHttpVerbAttributes.Contains(a.GetType())))
            .ToList();
    }

    /// <summary>
    /// 获取方法上的 [Permission] 特性的 Policy 值（格式为 "Permission_{permission}"）
    /// </summary>
    private static string? GetPermissionPolicy(MethodInfo method)
    {
        var attr = method.GetCustomAttribute<PermissionAttribute>();
        return attr?.Policy;
    }

    #region CustomReportsController

    [Fact]
    public void CustomReportsController_AllWriteEndpoints_ShouldHavePermissionAttribute()
    {
        var writeEndpoints = GetWriteEndpoints(typeof(CustomReportsController));
        writeEndpoints.Should().NotBeEmpty("CustomReportsController 应该有写操作端点");

        var missing = writeEndpoints
            .Where(m => GetPermissionPolicy(m) == null)
            .Select(m => m.Name)
            .ToList();

        missing.Should().BeEmpty(
            "以下写操作端点缺少 [Permission] 注解: {0}",
            string.Join(", ", missing));
    }

    [Theory]
    [InlineData(nameof(CustomReportsController.Create), "custom_reports.create")]
    [InlineData(nameof(CustomReportsController.Update), "custom_reports.update")]
    [InlineData(nameof(CustomReportsController.Delete), "custom_reports.delete")]
    [InlineData(nameof(CustomReportsController.TogglePin), "custom_reports.update")]
    [InlineData(nameof(CustomReportsController.ExecutePreview), "custom_reports.execute")]
    [InlineData(nameof(CustomReportsController.CreateSchedule), "custom_reports.schedules.manage")]
    [InlineData(nameof(CustomReportsController.UpdateSchedule), "custom_reports.schedules.manage")]
    [InlineData(nameof(CustomReportsController.DeleteSchedule), "custom_reports.schedules.manage")]
    public void CustomReportsController_WriteEndpoint_ShouldHaveCorrectPermission(string methodName, string expectedPermission)
    {
        var method = typeof(CustomReportsController).GetMethod(methodName);
        method.Should().NotBeNull($"方法 {methodName} 应该存在");

        var policy = GetPermissionPolicy(method!);
        policy.Should().Be($"Permission_{expectedPermission}",
            $"{methodName} 应该标注 [Permission(\"{expectedPermission}\")]");
    }

    #endregion

    #region ProfileController

    [Fact]
    public void ProfileController_AllWriteEndpoints_ShouldHavePermissionAttribute()
    {
        var writeEndpoints = GetWriteEndpoints(typeof(ProfileController));
        writeEndpoints.Should().NotBeEmpty("ProfileController 应该有写操作端点");

        var missing = writeEndpoints
            .Where(m => GetPermissionPolicy(m) == null)
            .Select(m => m.Name)
            .ToList();

        missing.Should().BeEmpty(
            "以下写操作端点缺少 [Permission] 注解: {0}",
            string.Join(", ", missing));
    }

    [Theory]
    [InlineData(nameof(ProfileController.UpdateProfile), "profile.update")]
    [InlineData(nameof(ProfileController.UploadAvatar), "profile.update")]
    [InlineData(nameof(ProfileController.RemoveAvatar), "profile.update")]
    [InlineData(nameof(ProfileController.ChangePassword), "profile.change_password")]
    public void ProfileController_WriteEndpoint_ShouldHaveCorrectPermission(string methodName, string expectedPermission)
    {
        var method = typeof(ProfileController).GetMethod(methodName);
        method.Should().NotBeNull($"方法 {methodName} 应该存在");

        var policy = GetPermissionPolicy(method!);
        policy.Should().Be($"Permission_{expectedPermission}",
            $"{methodName} 应该标注 [Permission(\"{expectedPermission}\")]");
    }

    #endregion

    #region ReportsController — 本轮新增的分页端点守卫

    [Fact]
    public void ReportsController_AllWriteEndpoints_ShouldHavePermissionAttribute()
    {
        var writeEndpoints = GetWriteEndpoints(typeof(ReportsController));
        // ReportsController 当前只有 GET 端点，无写操作
        // 如果未来添加写端点，此测试会自动生效
        var missing = writeEndpoints
            .Where(m => GetPermissionPolicy(m) == null)
            .Select(m => m.Name)
            .ToList();

        missing.Should().BeEmpty(
            "以下写操作端点缺少 [Permission] 注解: {0}",
            string.Join(", ", missing));
    }

    #endregion

    #region CalendarController — P1-5 写操作守卫 + SuperAdmin 强校验

    [Fact]
    public void CalendarController_AllWriteEndpoints_ShouldHavePermissionAttribute()
    {
        var writeEndpoints = GetWriteEndpoints(typeof(CalendarController));
        writeEndpoints.Should().NotBeEmpty("CalendarController 应该有写操作端点");

        var missing = writeEndpoints
            .Where(m => GetPermissionPolicy(m) == null &&
                        m.GetCustomAttribute<RequireSuperAdminAttribute>() == null)
            .Select(m => m.Name)
            .ToList();

        missing.Should().BeEmpty(
            "以下写操作端点缺少 [Permission] 或 [RequireSuperAdmin] 注解: {0}",
            string.Join(", ", missing));
    }

    /// <summary>
    /// P1-5: 清空回收站类高危操作必须在 Controller 层显式标注 [RequireSuperAdmin]
    /// —— 不能仅依赖 Service 层内部的 IsSuperAdmin 软阻断（返回 0），
    /// 因为未来重构移除 Service 层判断后会产生越权漏洞。
    /// </summary>
    [Theory]
    [InlineData(nameof(CalendarController.ClearTrash))]
    [InlineData(nameof(CalendarController.PurgeTrash))]
    public void CalendarController_PurgeAndClearTrash_ShouldRequireSuperAdmin(string methodName)
    {
        var method = typeof(CalendarController).GetMethod(methodName);
        method.Should().NotBeNull($"方法 {methodName} 应该存在");

        var attr = method!.GetCustomAttribute<RequireSuperAdminAttribute>();
        attr.Should().NotBeNull(
            $"{nameof(CalendarController)}.{methodName} 必须标注 [RequireSuperAdmin]，防止普通用户通过 API 彻底删除回收站数据");
    }

    [Theory]
    [InlineData(nameof(CalendarController.Create), "calendar.manage")]
    [InlineData(nameof(CalendarController.Update), "calendar.manage")]
    [InlineData(nameof(CalendarController.Delete), "calendar.manage")]
    [InlineData(nameof(CalendarController.UpdateStatus), "calendar.manage")]
    [InlineData(nameof(CalendarController.Duplicate), "calendar.manage")]
    [InlineData(nameof(CalendarController.QuickUpdate), "calendar.manage")]
    [InlineData(nameof(CalendarController.GetTrash), "calendar.manage")]
    [InlineData(nameof(CalendarController.GetTrashCount), "calendar.manage")]
    [InlineData(nameof(CalendarController.Restore), "calendar.manage")]
    [InlineData(nameof(CalendarController.HardDelete), "calendar.manage")]
    [InlineData(nameof(CalendarController.BatchOperation), "calendar.manage")]
    [InlineData(nameof(CalendarController.CreateTemplate), "calendar.manage")]
    [InlineData(nameof(CalendarController.UpdateTemplate), "calendar.manage")]
    [InlineData(nameof(CalendarController.DeleteTemplate), "calendar.manage")]
    [InlineData(nameof(CalendarController.ConfirmReminder), "calendar.manage")]
    [InlineData(nameof(CalendarController.GetOutlookHealth), "calendar.manage")]
    [InlineData(nameof(CalendarController.PullOutlookDelta), "calendar.manage")]
    [InlineData(nameof(CalendarController.ImportEvent), "calendar.manage")]
    public void CalendarController_WriteEndpoint_ShouldHaveCorrectPermission(string methodName, string expectedPermission)
    {
        var method = typeof(CalendarController).GetMethod(methodName);
        method.Should().NotBeNull($"方法 {methodName} 应该存在");

        var policy = GetPermissionPolicy(method!);
        policy.Should().Be($"Permission_{expectedPermission}",
            $"{methodName} 应该标注 [Permission(\"{expectedPermission}\")]");
    }

    #endregion

    #region 权限目录根因守卫 — [Permission] 引用的权限必须能在角色界面授予

    /// <summary>
    /// 2026-09-23 缺陷根因：ProfileController 的四个写操作标注了
    /// [Permission("profile.update")] / [Permission("profile.change_password")]，
    /// 但 RoleService.GetAllPermissionGroups()（角色界面可勾选的权限全集）里没有这两个键，
    /// 数据库种子角色也没有，于是管理员无法授予它们 ——
    /// 除超管（拥有 "*"）外所有用户调用 PUT /api/profile、头像上传/删除、
    /// 修改密码都只会得到 403（i18n 语言切换写资料时静默失败就是这样被发现的）。
    ///
    /// 本测试即该缺陷的根因守卫：反射扫描 NoneadCSS.Api 程序集中所有 [Permission("...")] 特性，
    /// 断言其权限字符串都存在于权限目录中；任何新增的、目录里没有的 [Permission("x")] 都会让它失败。
    ///
    /// 2026-09-23 补齐 15 条历史遗留权限后，本守卫转为**硬门禁**：不再保留任何豁免清单，
    /// 目录必须覆盖全部 [Permission] 引用（其中 mailbox.manage / customfields.manage 两条
    /// 已按目录拼写统一为 mailboxes.manage / custom_fields.manage）。
    /// </summary>
    [Fact]
    public void AllPermissionAttributes_InApiAssembly_ShouldExistInPermissionCatalog()
    {
        var catalogKeys = GetCatalogPermissionKeys().ToHashSet(StringComparer.Ordinal);
        catalogKeys.Should().NotBeEmpty("权限目录 RoleService.GetAllPermissionGroups() 不应该为空");

        var usedPermissions = GetPermissionAttributeValues(typeof(ProfileController).Assembly);

        var missing = usedPermissions
            .Where(p => !catalogKeys.Contains(p))
            .OrderBy(p => p, StringComparer.Ordinal)
            .ToList();

        missing.Should().BeEmpty(
            "以下 [Permission] 特性引用的权限不在 RoleService.GetAllPermissionGroups() 权限目录中，" +
            "管理员无法通过角色界面授予，非超管用户调用对应端点只会得到 403: {0}",
            string.Join(", ", missing));
    }

    /// <summary>
    /// 读取 RoleService.GetAllPermissionGroups() 定义的权限目录键（角色界面可勾选的权限全集）
    /// </summary>
    private static IEnumerable<string> GetCatalogPermissionKeys()
    {
        var service = new RoleService(
            Mock.Of<IUnitOfWork>(),
            Mock.Of<IUserRoleResolver>(),
            new MemoryCache(new MemoryCacheOptions()),
            Options.Create(new CacheOptions()));

        return service.GetAllPermissionGroups()
            .SelectMany(g => g.Permissions)
            .Select(p => p.Key);
    }

    /// <summary>
    /// 反射收集程序集中所有 [Permission("...")] 特性声明的权限名（类型级与方法级都算）
    /// </summary>
    private static List<string> GetPermissionAttributeValues(Assembly assembly)
    {
        var permissions = new List<string>();

        foreach (var type in assembly.GetTypes())
        {
            permissions.AddRange(GetPermissionAttributeValues(type.GetCustomAttributes<PermissionAttribute>(inherit: true)));

            foreach (var method in type.GetMethods(BindingFlags.Public | BindingFlags.NonPublic |
                                                   BindingFlags.Instance | BindingFlags.Static |
                                                   BindingFlags.DeclaredOnly))
            {
                permissions.AddRange(GetPermissionAttributeValues(method.GetCustomAttributes<PermissionAttribute>()));
            }
        }

        return permissions.Distinct(StringComparer.Ordinal).ToList();
    }

    /// <summary>
    /// 从 [Permission] 特性实例还原权限名（其 Policy 约定为 "Permission_{permission}"）
    /// </summary>
    private static IEnumerable<string> GetPermissionAttributeValues(IEnumerable<PermissionAttribute> attributes)
    {
        const string policyPrefix = "Permission_";

        foreach (var attribute in attributes)
        {
            if (string.IsNullOrEmpty(attribute.Policy)) continue;

            yield return attribute.Policy.StartsWith(policyPrefix, StringComparison.Ordinal)
                ? attribute.Policy[policyPrefix.Length..]
                : attribute.Policy;
        }
    }

    #endregion
}
