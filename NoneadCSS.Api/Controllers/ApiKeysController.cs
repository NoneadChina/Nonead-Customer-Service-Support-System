using NoneadCSS.Api.Authorization;
using NoneadCSS.Api.Filters;
using NoneadCSS.Application.DTOs.Requests;
using NoneadCSS.Application.DTOs.Responses;
using NoneadCSS.Application.Services;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using System.Security.Claims;

namespace NoneadCSS.Api.Controllers;

/// <summary>
/// API 密钥控制器 - 管理系统 API 密钥
/// </summary>
[ApiController]
[Route("api/[controller]")]
[Authorize]
[RequireModule("core")]
public class ApiKeysController : ControllerBase
{
    private readonly IApiKeyService _apiKeyService;

    public ApiKeysController(IApiKeyService apiKeyService)
    {
        _apiKeyService = apiKeyService;
    }

    /// <summary>
    /// 获取 API 密钥概览统计
    /// </summary>
    /// <returns>概览统计数据</returns>
    [HttpGet("overview")]
    [Permission("api_keys.view")]
    public async Task<IActionResult> GetOverview()
    {
        var overview = await _apiKeyService.GetOverviewAsync();
        return Ok(overview);
    }

    /// <summary>
    /// 获取 API 密钥列表（分页）
    /// </summary>
    /// <param name="userId">用户 ID 筛选</param>
    /// <param name="isActive">启用状态筛选</param>
    /// <param name="keyword">搜索关键词</param>
    /// <param name="scope">权限范围筛选</param>
    /// <param name="page">页码</param>
    /// <param name="pageSize">每页数量</param>
    /// <returns>分页 API 密钥列表</returns>
    [HttpGet]
    [Permission("api_keys.view")]
    public async Task<IActionResult> GetAll(
        [FromQuery] int? userId = null,
        [FromQuery] bool? isActive = null,
        [FromQuery] string? keyword = null,
        [FromQuery] string? scope = null,
        [FromQuery] int page = 1,
        [FromQuery] int pageSize = PaginationHelper.DefaultPageSize)
    {
        // 限制分页参数上限，防止请求大量数据导致 DoS
        page = Math.Max(page, 1);
        pageSize = Math.Min(Math.Max(pageSize, 1), PaginationHelper.MaxPageSize);
        var query = new ApiKeyQuery
        {
            UserId = userId,
            IsActive = isActive,
            Keyword = keyword,
            Scope = scope,
            Page = page,
            PageSize = pageSize
        };
        var (items, total) = await _apiKeyService.GetPagedAsync(query);
        return Ok(new PagedResponse<ApiKeyDto>
        {
            Data = items,
            TotalItems = total,
            CurrentPage = page,
            TotalPages = (int)Math.Ceiling((double)total / pageSize),
            PageSize = pageSize
        });
    }

    /// <summary>
    /// 获取 API 密钥详情
    /// </summary>
    /// <param name="id">密钥 ID</param>
    /// <returns>密钥详情</returns>
    /// <response code="200">返回密钥</response>
    /// <response code="404">密钥不存在</response>
    [HttpGet("{id}")]
    [Permission("api_keys.view")]
    public async Task<IActionResult> GetById(int id)
    {
        var apiKey = await _apiKeyService.GetByIdAsync(id);
        if (apiKey == null)
        {
            return NotFound(new { message = "API 密钥不存在" });
        }
        return Ok(apiKey);
    }

    /// <summary>
    /// 创建新的 API 密钥
    /// </summary>
    /// <param name="request">创建密钥请求</param>
    /// <returns>新创建的密钥（包含完整密钥值）</returns>
    /// <response code="201">创建成功</response>
    [HttpPost]
    [RequireSuperAdmin]
    public async Task<IActionResult> Create([FromBody] CreateApiKeyRequest request)
    {
        var apiKey = await _apiKeyService.CreateAsync(request);
        return CreatedAtAction(nameof(GetById), new { id = apiKey.Id }, apiKey);
    }

    /// <summary>
    /// 更新 API 密钥
    /// </summary>
    /// <param name="id">密钥 ID</param>
    /// <param name="request">更新请求</param>
    /// <returns>更新后的密钥</returns>
    /// <response code="200">更新成功</response>
    /// <response code="404">密钥不存在</response>
    [HttpPut("{id}")]
    [RequireSuperAdmin]
    public async Task<IActionResult> Update(int id, [FromBody] UpdateApiKeyRequest request,
        [FromHeader(Name = "If-Match")] string? ifMatch = null)
    {
        request.Id = id;
        var apiKey = await _apiKeyService.UpdateAsync(request, ifMatch);
        if (apiKey == null)
        {
            return NotFound(new { message = "API 密钥不存在" });
        }
        return Ok(apiKey);
    }

    /// <summary>
    /// 切换 API 密钥启用状态
    /// </summary>
    /// <param name="id">密钥 ID</param>
    /// <returns>更新后的密钥</returns>
    /// <response code="200">操作成功</response>
    /// <response code="404">密钥不存在</response>
    [HttpPost("{id}/toggle")]
    [RequireSuperAdmin]
    public async Task<IActionResult> ToggleActive(int id, [FromHeader(Name = "If-Match")] string? ifMatch = null)
    {
        var apiKey = await _apiKeyService.ToggleActiveAsync(id, ifMatch);
        if (apiKey == null)
        {
            return NotFound(new { message = "API 密钥不存在" });
        }
        return Ok(apiKey);
    }

    /// <summary>
    /// 重新生成 API 密钥
    /// </summary>
    /// <param name="id">密钥 ID</param>
    /// <returns>包含新密钥值的密钥对象</returns>
    /// <response code="200">操作成功</response>
    /// <response code="404">密钥不存在</response>
    [HttpPost("{id}/regenerate")]
    [RequireSuperAdmin]
    public async Task<IActionResult> Regenerate(int id, [FromHeader(Name = "If-Match")] string? ifMatch = null)
    {
        var apiKey = await _apiKeyService.RegenerateKeyAsync(id, ifMatch);
        if (apiKey == null)
        {
            return NotFound(new { message = "API 密钥不存在" });
        }
        return Ok(apiKey);
    }

    /// <summary>
    /// 删除 API 密钥
    /// </summary>
    /// <param name="id">密钥 ID</param>
    /// <returns>无内容</returns>
    /// <response code="204">删除成功</response>
    /// <response code="404">密钥不存在</response>
    [HttpDelete("{id}")]
    [RequireSuperAdmin]
    public async Task<IActionResult> Delete(int id)
    {
        var result = await _apiKeyService.DeleteAsync(id);
        if (!result)
        {
            return NotFound(new { message = "API 密钥不存在" });
        }
        return NoContent();
    }

    /// <summary>
    /// 获取 API 密钥的调用日志
    /// </summary>
    /// <param name="id">密钥 ID</param>
    /// <param name="pageSize">返回数量限制</param>
    /// <returns>调用日志列表</returns>
    [HttpGet("{id}/logs")]
    [Permission("api_keys.view")]
    public async Task<IActionResult> GetLogs(int id, [FromQuery] int pageSize = PaginationHelper.LargePageSize)
    {
        // 限制返回数量上限，防止请求大量数据导致 DoS
        pageSize = Math.Min(Math.Max(pageSize, 1), PaginationHelper.MaxPageSize);
        var logs = await _apiKeyService.GetLogsAsync(id, pageSize);
        return Ok(logs);
    }

    /// <summary>
    /// 获取所有 API 密钥调用日志（分页）
    /// </summary>
    /// <param name="apiKeyId">密钥 ID 筛选</param>
    /// <param name="endpoint">端点筛选</param>
    /// <param name="statusCode">状态码筛选</param>
    /// <param name="page">页码</param>
    /// <param name="pageSize">每页数量</param>
    /// <returns>分页日志列表</returns>
    [HttpGet("logs")]
    [Permission("api_keys.view")]
    public async Task<IActionResult> GetAllLogs(
        [FromQuery] int? apiKeyId = null,
        [FromQuery] string? endpoint = null,
        [FromQuery] int? statusCode = null,
        [FromQuery] int page = 1,
        [FromQuery] int pageSize = PaginationHelper.DefaultPageSize)
    {
        // 限制分页参数上限，防止请求大量数据导致 DoS
        page = Math.Max(page, 1);
        pageSize = Math.Min(Math.Max(pageSize, 1), PaginationHelper.MaxPageSize);
        var query = new ApiKeyLogQuery
        {
            ApiKeyId = apiKeyId,
            Endpoint = endpoint,
            StatusCode = statusCode,
            Page = page,
            PageSize = pageSize
        };
        var (items, total) = await _apiKeyService.GetLogsPagedAsync(query);
        return Ok(new PagedResponse<ApiKeyLogDto>
        {
            Data = items,
            TotalItems = total,
            CurrentPage = page,
            TotalPages = (int)Math.Ceiling((double)total / pageSize),
            PageSize = pageSize
        });
    }

    /// <summary>
    /// 获取垃圾箱中的 API 密钥列表（分页）
    /// </summary>
    [HttpGet("trash")]
    [Permission("api_keys.view")]
    public async Task<IActionResult> GetTrash(
        [FromQuery] int page = 1,
        [FromQuery] int pageSize = PaginationHelper.SmallPageSize,
        [FromQuery] string? keyword = null)
    {
        // 限制分页参数上限，防止请求大量数据导致 DoS
        page = Math.Max(page, 1);
        pageSize = Math.Min(Math.Max(pageSize, 1), PaginationHelper.MaxPageSize);
        var (items, total) = await _apiKeyService.GetTrashPagedAsync(page, pageSize, keyword);
        return Ok(new PagedResponse<ApiKeyDto>
        {
            Data = items,
            CurrentPage = page,
            TotalPages = (int)Math.Ceiling((double)total / pageSize),
            TotalItems = total,
            PageSize = pageSize
        });
    }

    /// <summary>
    /// 恢复已删除的 API 密钥
    /// </summary>
    [HttpPost("{id}/restore")]
    [RequireSuperAdmin]
    public async Task<IActionResult> Restore(int id)
    {
        var result = await _apiKeyService.RestoreAsync(id);
        if (result == null) return NotFound(new { message = "API 密钥不存在" });
        return Ok(result);
    }

    /// <summary>
    /// 彻底删除 API 密钥（管理员）
    /// </summary>
    [HttpDelete("{id}/hard")]
    [RequireSuperAdmin]
    public async Task<IActionResult> HardDelete(int id)
    {
        await _apiKeyService.HardDeleteAsync(id);
        return Ok();
    }

    /// <summary>
    /// 清空垃圾箱（管理员）
    /// </summary>
    [HttpDelete("trash")]
    [RequireSuperAdmin]
    public async Task<IActionResult> ClearTrash()
    {
        var count = await _apiKeyService.ClearTrashAsync();
        return Ok(new { count });
    }
}
