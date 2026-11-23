using NoneadCSS.Api.Authorization;
using NoneadCSS.Api.Filters;
using NoneadCSS.Application.DTOs.Responses;
using NoneadCSS.Application.Services;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.OutputCaching;

namespace NoneadCSS.Api.Controllers;

/// <summary>
/// 自定义字段控制器 - 管理会话和客户的自定义字段
/// </summary>
[ApiController]
[Route("api/[controller]")]
[Authorize]
[RequireModule("core")]
public class CustomFieldsController : ControllerBase
{
    private readonly ICustomFieldService _service;

    public CustomFieldsController(ICustomFieldService service)
    {
        _service = service;
    }

    /// <summary>
    /// 获取自定义字段列表（分页）
    /// </summary>
    [HttpGet]
    [RequireSuperAdmin]
    [OutputCache(PolicyName = "CustomFields")]
    public async Task<ActionResult> GetList(
        [FromQuery] int page = 1,
        [FromQuery] int pageSize = PaginationHelper.DefaultPageSize,
        [FromQuery] string? entityType = null,
        [FromQuery] int? mailboxId = null,
        [FromQuery] bool? isActive = null,
        [FromQuery] string? keyword = null)
    {
        // 限制分页参数上限，防止请求大量数据导致 DoS
        page = Math.Max(page, 1);
        pageSize = Math.Min(Math.Max(pageSize, 1), PaginationHelper.MaxPageSize);
        var (items, total) = await _service.GetPagedAsync(new CustomFieldQuery
        {
            Page = page,
            PageSize = pageSize,
            EntityType = entityType,
            MailboxId = mailboxId,
            IsActive = isActive,
            Keyword = keyword,
        });

        return Ok(new PagedResponse<CustomFieldDto>
        {
            Data = items,
            CurrentPage = page,
            TotalPages = (int)Math.Ceiling((double)total / pageSize),
            TotalItems = total,
            PageSize = pageSize
        });
    }

    /// <summary>
    /// 获取自定义字段详情
    /// </summary>
    [HttpGet("{id}")]
    [RequireSuperAdmin]
    [OutputCache(PolicyName = "CustomFields")]
    public async Task<ActionResult<CustomFieldDto>> GetById(int id)
    {
        var result = await _service.GetByIdAsync(id);
        if (result == null) return NotFound();
        return Ok(result);
    }

    /// <summary>
    /// 获取自定义字段概览统计
    /// </summary>
    [HttpGet("overview")]
    [RequireSuperAdmin]
    [OutputCache(PolicyName = "CustomFields")]
    public async Task<ActionResult<CustomFieldOverviewResponse>> GetOverview()
    {
        var result = await _service.GetOverviewAsync();
        return Ok(result);
    }

    /// <summary>
    /// 获取指定对象类型可用的自定义字段（按渠道过滤）。
    /// 传入 sourceType（如 "wechat"）返回：全局字段 + 该渠道专属字段；
    /// 并按 HideWhenChannelDisabled 规则过滤已关闭渠道的字段。
    /// </summary>
    [HttpGet("for-entity/{entityType}")]
    [OutputCache(PolicyName = "CustomFields")]
    public async Task<ActionResult<List<CustomFieldDto>>> GetForEntity(
        string entityType,
        [FromQuery] string? sourceType = null)
    {
        var result = await _service.GetActiveForEntityAsync(entityType, sourceType);
        return Ok(result);
    }

    /// <summary>
    /// 获取可用渠道列表（仅返回已启用渠道 + 全局选项），用于字段管理页"适用渠道"下拉。
    /// </summary>
    [HttpGet("channels")]
    [OutputCache(PolicyName = "CustomFields")]
    public async Task<ActionResult<List<CustomFieldChannelDto>>> GetChannels()
    {
        var result = await _service.GetChannelsAsync();
        return Ok(result);
    }

    /// <summary>
    /// 创建自定义字段
    /// </summary>
    [HttpPost]
    [Permission("custom_fields.manage")]
    public async Task<ActionResult<CustomFieldDto>> Create([FromBody] CreateCustomFieldRequest request)
    {
        var result = await _service.CreateAsync(request);
        return CreatedAtAction(nameof(GetById), new { id = result.Id }, result);
    }

    /// <summary>
    /// 更新自定义字段
    /// </summary>
    [HttpPut("{id}")]
    [Permission("custom_fields.manage")]
    public async Task<ActionResult<CustomFieldDto>> Update(int id, [FromBody] UpdateCustomFieldRequest request)
    {
        if (id != request.Id) return BadRequest(new { message = "ID 不匹配" });

        var result = await _service.UpdateAsync(request);
        if (result == null) return NotFound();
        return Ok(result);
    }

    /// <summary>
    /// 删除自定义字段
    /// </summary>
    [HttpDelete("{id}")]
    [Permission("custom_fields.manage")]
    public async Task<ActionResult> Delete(int id)
    {
        var result = await _service.DeleteAsync(id);
        if (!result) return NotFound();
        return Ok();
    }

    /// <summary>
    /// 切换启用/禁用状态
    /// </summary>
    [HttpPost("{id}/toggle-active")]
    [Permission("custom_fields.manage")]
    public async Task<ActionResult> ToggleActive(int id)
    {
        var result = await _service.ToggleActiveAsync(id);
        if (!result) return NotFound();
        return Ok();
    }

    /// <summary>
    /// 获取某个对象的所有自定义字段值
    /// </summary>
    [HttpGet("values/{entityType}/{entityId}")]
    public async Task<ActionResult<List<CustomFieldValueDto>>> GetEntityValues(
        string entityType, int entityId)
    {
        var result = await _service.GetEntityValuesAsync(entityType, entityId);
        return Ok(result);
    }

    /// <summary>
    /// 设置某个对象的指定自定义字段值
    /// </summary>
    [HttpPut("values/{entityType}/{entityId}/{customFieldId}")]
    [Permission("custom_fields.manage")]
    public async Task<ActionResult<CustomFieldValueDto>> SetEntityValue(
        string entityType, int entityId, int customFieldId,
        [FromBody] UpdateCustomFieldValueRequest request)
    {
        var result = await _service.SetEntityValueAsync(customFieldId, entityType, entityId, request.Value);
        if (result == null) return NotFound();
        return Ok(result);
    }

    /// <summary>
    /// 批量设置某个对象的自定义字段值
    /// </summary>
    [HttpPost("values/batch")]
    [Permission("custom_fields.manage")]
    public async Task<ActionResult<List<CustomFieldValueDto>>> BatchSetEntityValues(
        [FromBody] BatchUpdateCustomFieldValuesRequest request)
    {
        var result = await _service.BatchSetEntityValuesAsync(request.EntityType, request.EntityId, request.Values);
        return Ok(result);
    }

    /// <summary>
    /// 删除某个对象的所有自定义字段值
    /// </summary>
    [HttpDelete("values/{entityType}/{entityId}")]
    [Permission("custom_fields.manage")]
    public async Task<ActionResult> DeleteEntityValues(string entityType, int entityId)
    {
        var result = await _service.DeleteEntityValuesAsync(entityType, entityId);
        if (!result) return NotFound();
        return Ok();
    }

    /// <summary>
    /// 获取垃圾箱中的自定义字段列表（分页）
    /// </summary>
    [HttpGet("trash")]
    [RequireSuperAdmin]
    public async Task<ActionResult> GetTrash(
        [FromQuery] int page = 1,
        [FromQuery] int pageSize = PaginationHelper.SmallPageSize,
        [FromQuery] string? keyword = null)
    {
        // 限制分页参数上限，防止请求大量数据导致 DoS
        page = Math.Max(page, 1);
        pageSize = Math.Min(Math.Max(pageSize, 1), PaginationHelper.MaxPageSize);
        var (items, total) = await _service.GetTrashPagedAsync(page, pageSize, keyword);
        return Ok(new PagedResponse<CustomFieldDto>
        {
            Data = items,
            CurrentPage = page,
            TotalPages = (int)Math.Ceiling((double)total / pageSize),
            TotalItems = total,
            PageSize = pageSize
        });
    }

    /// <summary>
    /// 恢复已删除的自定义字段
    /// </summary>
    [HttpPost("{id}/restore")]
    [Permission("custom_fields.manage")]
    public async Task<ActionResult> Restore(int id)
    {
        var result = await _service.RestoreAsync(id);
        if (result == null) return NotFound();
        return Ok(result);
    }

    /// <summary>
    /// 彻底删除自定义字段（管理员）
    /// </summary>
    [HttpDelete("{id}/hard")]
    [Permission("custom_fields.manage")]
    public async Task<ActionResult> HardDelete(int id)
    {
        await _service.HardDeleteAsync(id);
        return Ok();
    }

    /// <summary>
    /// 清空垃圾箱（管理员）
    /// </summary>
    [HttpDelete("trash")]
    [Permission("custom_fields.manage")]
    public async Task<ActionResult> ClearTrash()
    {
        var count = await _service.ClearTrashAsync();
        return Ok(new { count });
    }
}
