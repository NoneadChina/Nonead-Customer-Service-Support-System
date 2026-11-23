using NoneadCSS.Api.Authorization;
using NoneadCSS.Api.Filters;
using NoneadCSS.Application.DTOs.Requests;
using NoneadCSS.Application.DTOs.Responses;
using NoneadCSS.Application.Services;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.OutputCaching;
using System.Security.Claims;

namespace NoneadCSS.Api.Controllers;

/// <summary>
/// 系统选项控制器 - 管理系统配置选项
/// </summary>
[ApiController]
[Route("api/[controller]")]
[Authorize]
[RequireModule("core")]
public class SystemOptionsController : ControllerBase
{
    private readonly ISystemOptionService _service;

    public SystemOptionsController(ISystemOptionService service)
    {
        _service = service;
    }

    /// <summary>
    /// 获取所有系统配置项
    /// </summary>
    /// <returns>配置项列表</returns>
    [HttpGet]
    [OutputCache(PolicyName = "SystemOptions")]
    [Permission("settings.view")]
    public async Task<IActionResult> GetAll()
    {
        var options = await _service.GetAllAsync();
        return Ok(options);
    }

    /// <summary>
    /// 按分类获取系统配置项
    /// </summary>
    /// <param name="category">配置分类</param>
    /// <returns>配置项列表</returns>
    [HttpGet("category/{category}")]
    [OutputCache(PolicyName = "SystemOptions")]
    [Permission("settings.view")]
    public async Task<IActionResult> GetByCategory(string category)
    {
        var options = await _service.GetByCategoryAsync(category);
        return Ok(options);
    }

    /// <summary>
    /// 按名称获取配置项
    /// </summary>
    /// <param name="name">配置项名称</param>
    /// <returns>配置项详情</returns>
    /// <response code="200">返回配置项</response>
    /// <response code="404">配置项不存在</response>
    [HttpGet("{name}")]
    [Permission("settings.view")]
    public async Task<IActionResult> GetByName(string name)
    {
        var option = await _service.GetByNameAsync(name);
        if (option == null)
        {
            return NotFound(new { message = "配置项不存在" });
        }
        return Ok(option);
    }

    /// <summary>
    /// 创建新的配置项
    /// </summary>
    /// <param name="request">创建配置项请求</param>
    /// <returns>新创建的配置项</returns>
    /// <response code="201">创建成功</response>
    /// <response code="400">配置项已存在</response>
    [HttpPost]
    [RequireSuperAdmin]
    public async Task<IActionResult> Create([FromBody] CreateSystemOptionRequest request)
    {
        try
        {
            var option = await _service.CreateAsync(request);
            return CreatedAtAction(nameof(GetByName), new { name = option.Name }, option);
        }
        catch (InvalidOperationException ex)
        {
            return BadRequest(new { message = ex.Message });
        }
    }

    /// <summary>
    /// 更新配置项
    /// </summary>
    /// <param name="name">配置项名称</param>
    /// <param name="request">更新请求</param>
    /// <returns>更新后的配置项</returns>
    /// <response code="200">更新成功</response>
    /// <response code="404">配置项不存在</response>
    [HttpPut("{name}")]
    [RequireSuperAdmin]
    public async Task<IActionResult> Update(string name, [FromBody] UpdateSystemOptionRequest request)
    {
        var option = await _service.UpdateAsync(name, request);
        if (option == null)
        {
            return NotFound(new { message = "配置项不存在" });
        }
        return Ok(option);
    }

    /// <summary>
    /// 删除配置项
    /// </summary>
    /// <param name="name">配置项名称</param>
    /// <returns>无内容</returns>
    /// <response code="204">删除成功</response>
    /// <response code="404">配置项不存在</response>
    [HttpDelete("{name}")]
    [RequireSuperAdmin]
    public async Task<IActionResult> Delete(string name)
    {
        var result = await _service.DeleteAsync(name);
        if (!result)
        {
            return NotFound(new { message = "配置项不存在" });
        }
        return NoContent();
    }

    /// <summary>
    /// 批量更新系统配置
    /// </summary>
    /// <param name="request">配置项字典</param>
    /// <returns>更新结果</returns>
    [HttpPut("batch")]
    [RequireSuperAdmin]
    public async Task<IActionResult> BatchUpdate([FromBody] BatchUpdateSystemOptionRequest request)
    {
        await _service.BatchUpdateAsync(request.Options);
        return Ok(new { message = "配置已更新" });
    }

    /// <summary>
    /// 获取所有配置项为字典格式
    /// </summary>
    /// <returns>配置项字典</returns>
    [HttpGet("dictionary")]
    [OutputCache(PolicyName = "SystemOptions")]
    [Permission("settings.view")]
    public async Task<IActionResult> GetAsDictionary()
    {
        var options = await _service.GetAllAsDictionaryAsync();
        return Ok(options);
    }
}
