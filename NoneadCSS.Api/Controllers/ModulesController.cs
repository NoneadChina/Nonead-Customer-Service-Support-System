using NoneadCSS.Api.Authorization;
using NoneadCSS.Api.Filters;
using NoneadCSS.Application.DTOs.Requests;
using NoneadCSS.Application.DTOs.Responses;
using NoneadCSS.Application.Services;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.OutputCaching;

namespace NoneadCSS.Api.Controllers;

/// <summary>
/// 模块管理控制器 - 管理系统扩展模块
/// </summary>
[ApiController]
[Route("api/[controller]")]
[Authorize]
[RequireModule("core")]
public class ModulesController : ControllerBase
{
    private readonly IModuleService _moduleService;

    public ModulesController(IModuleService moduleService)
    {
        _moduleService = moduleService;
    }

    /// <summary>
    /// 获取模块概览统计
    /// </summary>
    /// <returns>概览统计数据</returns>
    [HttpGet("overview")]
    [Permission("modules.view")]
    [OutputCache(PolicyName = "Modules")]
    public async Task<IActionResult> GetOverview()
    {
        var overview = await _moduleService.GetOverviewAsync();
        return Ok(overview);
    }

    /// <summary>
    /// 获取所有模块（分页）
    /// </summary>
    /// <param name="category">分类筛选</param>
    /// <param name="isActive">启用状态筛选</param>
    /// <param name="search">搜索关键词</param>
    /// <param name="page">页码</param>
    /// <param name="pageSize">每页数量</param>
    /// <returns>分页模块列表</returns>
    [HttpGet]
    [Permission("modules.view")]
    [OutputCache(PolicyName = "Modules")]
    public async Task<IActionResult> GetAll(
        [FromQuery] string? category = null,
        [FromQuery] bool? isActive = null,
        [FromQuery] string? search = null,
        [FromQuery] int page = 1,
        [FromQuery] int pageSize = PaginationHelper.DefaultPageSize)
    {
        // 限制分页参数上限，防止请求大量数据导致 DoS
        page = Math.Max(page, 1);
        pageSize = Math.Min(Math.Max(pageSize, 1), PaginationHelper.MaxPageSize);
        var query = new ModuleQuery
        {
            Category = category,
            IsActive = isActive,
            Search = search,
            Page = page,
            PageSize = pageSize
        };
        var (items, total) = await _moduleService.GetPagedAsync(query);
        return Ok(new PagedResponse<ModuleDto>
        {
            Data = items,
            TotalItems = total,
            CurrentPage = page,
            TotalPages = (int)Math.Ceiling((double)total / pageSize),
            PageSize = pageSize
        });
    }

    /// <summary>
    /// 按分类获取模块
    /// </summary>
    /// <param name="category">分类名称</param>
    /// <returns>模块列表</returns>
    [HttpGet("category/{category}")]
    [Permission("modules.view")]
    [OutputCache(PolicyName = "Modules")]
    public async Task<IActionResult> GetByCategory(string category)
    {
        var modules = await _moduleService.GetByCategoryAsync(category);
        return Ok(modules);
    }

    /// <summary>
    /// 获取已启用的模块
    /// </summary>
    /// <returns>已启用模块列表</returns>
    [HttpGet("active")]
    [Permission("modules.view")]
    [OutputCache(PolicyName = "Modules")]
    public async Task<IActionResult> GetActive()
    {
        var modules = await _moduleService.GetActiveAsync();
        return Ok(modules);
    }

    /// <summary>
    /// 获取所有模块（不分页）
    /// </summary>
    /// <returns>所有模块列表</returns>
    [HttpGet("all")]
    [Permission("modules.view")]
    [OutputCache(PolicyName = "Modules")]
    public async Task<IActionResult> GetAllModules()
    {
        var modules = await _moduleService.GetAllAsync();
        return Ok(modules);
    }

    /// <summary>
    /// 按名称获取模块详情
    /// </summary>
    /// <param name="name">模块名称</param>
    /// <returns>模块详情</returns>
    /// <response code="200">返回模块</response>
    /// <response code="404">模块不存在</response>
    [HttpGet("{name}")]
    [Permission("modules.view")]
    [OutputCache(PolicyName = "Modules")]
    public async Task<IActionResult> GetByName(string name)
    {
        var module = await _moduleService.GetByNameAsync(name);
        if (module == null)
        {
            return NotFound(new { message = "模块不存在" });
        }
        return Ok(module);
    }

    /// <summary>
    /// 创建/安装新模块
    /// </summary>
    /// <param name="request">创建模块请求</param>
    /// <returns>新创建的模块</returns>
    /// <response code="201">创建成功</response>
    /// <response code="400">模块已存在</response>
    [HttpPost]
    [RequireSuperAdmin]
    public async Task<IActionResult> Create([FromBody] CreateModuleRequest request)
    {
        try
        {
            var module = await _moduleService.CreateAsync(request);
            return CreatedAtAction(nameof(GetByName), new { name = module!.Name }, module);
        }
        catch (InvalidOperationException ex)
        {
            return BadRequest(new { message = ex.Message });
        }
    }

    /// <summary>
    /// 更新模块
    /// </summary>
    /// <param name="name">模块名称</param>
    /// <param name="request">更新请求</param>
    /// <returns>更新后的模块</returns>
    /// <response code="200">更新成功</response>
    /// <response code="404">模块不存在</response>
    [HttpPut("{name}")]
    [RequireSuperAdmin]
    public async Task<IActionResult> Update(string name, [FromBody] UpdateModuleRequest request)
    {
        var module = await _moduleService.UpdateAsync(name, request);
        if (module == null)
        {
            return NotFound(new { message = "模块不存在" });
        }
        return Ok(module);
    }

    /// <summary>
    /// 切换模块启用状态
    /// </summary>
    /// <param name="name">模块名称</param>
    /// <returns>更新后的模块</returns>
    /// <response code="200">操作成功</response>
    /// <response code="400">核心模块无法被关闭</response>
    /// <response code="404">模块不存在</response>
    [HttpPost("{name}/toggle")]
    [RequireSuperAdmin]
    public async Task<IActionResult> ToggleActive(string name)
    {
        // 控制器层快速失败：核心模块不允许通过 toggle 接口关闭
        if (string.Equals(name, Domain.Entities.Module.CoreModuleName, StringComparison.OrdinalIgnoreCase))
        {
            // 确保返回状态仍为已启用（幂等兜底）
            var core = await _moduleService.GetByNameAsync(name);
            if (core != null && !core.IsActive)
                core = await _moduleService.ToggleActiveAsync(name);
            return Ok(core);
        }
        var module = await _moduleService.ToggleActiveAsync(name);
        if (module == null)
        {
            return NotFound(new { message = "模块不存在" });
        }
        return Ok(module);
    }

    /// <summary>
    /// 卸载/删除模块
    /// </summary>
    /// <param name="name">模块名称</param>
    /// <returns>无内容</returns>
    /// <response code="204">删除成功</response>
    /// <response code="400">核心模块无法被删除</response>
    /// <response code="404">模块不存在</response>
    [HttpDelete("{name}")]
    [RequireSuperAdmin]
    public async Task<IActionResult> Delete(string name)
    {
        try
        {
            var result = await _moduleService.DeleteAsync(name);
            if (!result)
            {
                return NotFound(new { message = "模块不存在" });
            }
            return NoContent();
        }
        catch (InvalidOperationException ex)
        {
            return BadRequest(new { message = ex.Message });
        }
    }
}
