using NoneadCSS.Application.Services;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc.Filters;
using Microsoft.AspNetCore.Mvc;

namespace NoneadCSS.Api.Filters;

public class ModuleAuthorizationFilter : IAsyncActionFilter
{
    private readonly IModuleService _moduleService;

    public ModuleAuthorizationFilter(IModuleService moduleService)
    {
        _moduleService = moduleService;
    }

    public async Task OnActionExecutionAsync(ActionExecutingContext context, ActionExecutionDelegate next)
    {
        // 匿名访问的 Action（标注了 [AllowAnonymous]）跳过模块检查，
        // 避免 Controller 级 [RequireModule] 误拦截匿名端点（如满意度评价提交）
        if (context.ActionDescriptor.EndpointMetadata.OfType<AllowAnonymousAttribute>().Any())
        {
            await next();
            return;
        }

        var requiredModules = GetRequiredModules(context);

        if (requiredModules.Count > 0)
        {
            var allModules = await _moduleService.GetAllAsync();
            var allModuleNames = new HashSet<string>(
                allModules.Select(m => m.Name),
                StringComparer.OrdinalIgnoreCase
            );
            var activeModuleNames = new HashSet<string>(
                allModules.Where(m => m.IsActive).Select(m => m.Name),
                StringComparer.OrdinalIgnoreCase
            );

            foreach (var module in requiredModules)
            {
                if (!allModuleNames.Contains(module)) continue;
                if (!activeModuleNames.Contains(module))
                {
                    context.Result = new ObjectResult(new
                    {
                        message = $"模块 '{module}' 未启用，无法执行此操作",
                        moduleName = module
                    })
                    {
                        StatusCode = StatusCodes.Status403Forbidden
                    };
                    return;
                }
            }
        }

        await next();
    }

    private static List<string> GetRequiredModules(ActionExecutingContext context)
    {
        var modules = new List<string>();

        var actionAttributes = context.ActionDescriptor.EndpointMetadata
            .OfType<RequireModuleAttribute>()
            .ToList();

        foreach (var attr in actionAttributes)
        {
            if (!modules.Contains(attr.ModuleName, StringComparer.OrdinalIgnoreCase))
            {
                modules.Add(attr.ModuleName);
            }
        }

        var controllerType = context.Controller.GetType();
        var controllerAttributes = controllerType
            .GetCustomAttributes(typeof(RequireModuleAttribute), true)
            .Cast<RequireModuleAttribute>()
            .ToList();

        foreach (var attr in controllerAttributes)
        {
            if (!modules.Contains(attr.ModuleName, StringComparer.OrdinalIgnoreCase))
            {
                modules.Add(attr.ModuleName);
            }
        }

        return modules;
    }
}