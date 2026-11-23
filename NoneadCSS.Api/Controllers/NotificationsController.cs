using NoneadCSS.Api.Authorization;
using NoneadCSS.Api.Filters;
using NoneadCSS.Application.DTOs.Requests;
using NoneadCSS.Application.DTOs.Responses;
using NoneadCSS.Application.Services;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;

namespace NoneadCSS.Api.Controllers;

/// <summary>
/// 通知控制器 - 管理通知模板、通知规则和发送通知
/// </summary>
[ApiController]
[Route("api/[controller]")]
[Authorize]
[RequireModule("core")]
public class NotificationsController : ControllerBase
{
    private readonly INotificationService _service;

    public NotificationsController(INotificationService service)
    {
        _service = service;
    }

    // ===== 通知模板 =====

    /// <summary>
    /// 获取通知模板概览
    /// </summary>
    [HttpGet("templates/overview")]
    [RequireSuperAdmin]
    public async Task<ActionResult<NotificationTemplateOverviewResponse>> GetTemplateOverview()
    {
        var overview = await _service.GetTemplateOverviewAsync();
        return Ok(overview);
    }

    /// <summary>
    /// 分页获取通知模板
    /// </summary>
    [HttpGet("templates")]
    [RequireSuperAdmin]
    public async Task<IActionResult> GetTemplates(
        [FromQuery] int page = 1,
        [FromQuery] int pageSize = PaginationHelper.SmallPageSize,
        [FromQuery] string? eventType = null,
        [FromQuery] bool? isActive = null,
        [FromQuery] string? search = null)
    {
        // 限制分页参数上限，防止请求大量数据导致 DoS
        page = Math.Max(page, 1);
        pageSize = Math.Min(Math.Max(pageSize, 1), PaginationHelper.MaxPageSize);
        var query = new NotificationTemplateQuery
        {
            Page = page,
            PageSize = pageSize,
            EventType = eventType,
            IsActive = isActive,
            Search = search
        };
        var (items, totalCount) = await _service.GetPagedTemplatesAsync(query);
        return Ok(new PagedResponse<NotificationTemplateResponse>
        {
            Data = items,
            TotalItems = totalCount,
            CurrentPage = page,
            PageSize = pageSize,
            TotalPages = (int)Math.Ceiling((double)totalCount / pageSize)
        });
    }

    /// <summary>
    /// 获取所有通知模板（不分页）
    /// </summary>
    [HttpGet("templates/all")]
    [RequireSuperAdmin]
    public async Task<IActionResult> GetAllTemplates()
    {
        var templates = await _service.GetTemplatesAsync();
        return Ok(templates);
    }

    /// <summary>
    /// 获取通知模板详情
    /// </summary>
    /// <param name="id">模板 ID</param>
    /// <returns>模板详情</returns>
    /// <response code="200">返回模板</response>
    /// <response code="403">无权限访问</response>
    /// <response code="404">模板不存在</response>
    [HttpGet("templates/{id}")]
    [RequireSuperAdmin]
    public async Task<IActionResult> GetTemplate(int id)
    {
        var template = await _service.GetTemplateByIdAsync(id);
        if (template == null)
        {
            return NotFound(new { message = "模板不存在" });
        }
        return Ok(template);
    }

    /// <summary>
    /// 创建通知模板
    /// </summary>
    /// <param name="request">创建模板请求</param>
    /// <returns>新创建的模板</returns>
    /// <response code="201">创建成功</response>
    /// <response code="400">模板名称已存在</response>
    [HttpPost("templates")]
    [Permission("notifications.manage")]
    public async Task<IActionResult> CreateTemplate([FromBody] CreateNotificationTemplateRequest request)
    {
        try
        {
            var template = await _service.CreateTemplateAsync(request);
            return CreatedAtAction(nameof(GetTemplate), new { id = template.Id }, template);
        }
        catch (InvalidOperationException ex)
        {
            return BadRequest(new { message = ex.Message });
        }
    }

    /// <summary>
    /// 更新通知模板
    /// </summary>
    /// <param name="id">模板 ID</param>
    /// <param name="request">更新请求</param>
    /// <returns>更新后的模板</returns>
    /// <response code="200">更新成功</response>
    /// <response code="404">模板不存在</response>
    [HttpPut("templates/{id}")]
    [Permission("notifications.manage")]
    public async Task<IActionResult> UpdateTemplate(int id, [FromBody] UpdateNotificationTemplateRequest request)
    {
        try
        {
            var template = await _service.UpdateTemplateAsync(id, request);
            if (template == null)
            {
                return NotFound(new { message = "模板不存在" });
            }
            return Ok(template);
        }
        catch (InvalidOperationException ex)
        {
            return BadRequest(new { message = ex.Message });
        }
    }

    /// <summary>
    /// 删除通知模板
    /// </summary>
    /// <param name="id">模板 ID</param>
    /// <returns>无内容</returns>
    /// <response code="204">删除成功</response>
    /// <response code="404">模板不存在</response>
    [HttpDelete("templates/{id}")]
    [Permission("notifications.manage")]
    public async Task<IActionResult> DeleteTemplate(int id)
    {
        var result = await _service.DeleteTemplateAsync(id);
        if (!result)
        {
            return NotFound(new { message = "模板不存在" });
        }
        return Ok();
    }

    /// <summary>
    /// 切换通知模板启用状态
    /// </summary>
    [HttpPost("templates/{id}/toggle")]
    [Permission("notifications.manage")]
    public async Task<ActionResult<NotificationTemplateResponse>> ToggleTemplateActive(int id)
    {
        var template = await _service.ToggleTemplateActiveAsync(id);
        if (template == null)
        {
            return NotFound(new { message = "模板不存在" });
        }
        return Ok(template);
    }

    /// <summary>
    /// 复制通知模板
    /// </summary>
    [HttpPost("templates/{id}/duplicate")]
    [Permission("notifications.manage")]
    public async Task<IActionResult> DuplicateTemplate(int id, [FromBody] DuplicateNotificationTemplateRequest? request = null)
    {
        var template = await _service.DuplicateTemplateAsync(id, request?.NewName);
        if (template == null)
        {
            return NotFound(new { message = "模板不存在" });
        }
        return Ok(template);
    }

    /// <summary>
    /// 校验模板变量
    /// </summary>
    [HttpPost("templates/validate-variables")]
    [Permission("notifications.manage")]
    public ActionResult<TemplateVariableValidateResponse> ValidateVariables([FromBody] ValidateTemplateVariablesRequest request)
    {
        var result = _service.ValidateTemplateVariables(request.Subject ?? string.Empty, request.Body ?? string.Empty);
        return Ok(result);
    }

    /// <summary>
    /// 预览通知模板渲染结果
    /// </summary>
    /// <param name="id">模板 ID</param>
    /// <param name="request">预览请求</param>
    /// <returns>渲染后的模板</returns>
    /// <response code="200">预览成功</response>
    /// <response code="404">模板不存在</response>
    [HttpPost("templates/{id}/preview")]
    [Permission("notifications.manage")]
    public async Task<IActionResult> PreviewTemplate(int id, [FromBody] NotificationTemplatePreviewRequest request)
    {
        var result = await _service.PreviewTemplateAsync(id, request);
        if (result == null)
        {
            return NotFound(new { message = "模板不存在" });
        }
        return Ok(result);
    }

    /// <summary>
    /// 获取通知模板垃圾箱列表
    /// </summary>
    [HttpGet("templates/trash")]
    [RequireSuperAdmin]
    public async Task<IActionResult> GetTemplateTrash(
        [FromQuery] int page = 1,
        [FromQuery] int pageSize = PaginationHelper.SmallPageSize,
        [FromQuery] string? search = null)
    {
        // 限制分页参数上限，防止请求大量数据导致 DoS
        page = Math.Max(page, 1);
        pageSize = Math.Min(Math.Max(pageSize, 1), PaginationHelper.MaxPageSize);
        var (items, totalCount) = await _service.GetTemplateTrashAsync(page, pageSize, search);
        return Ok(new PagedResponse<NotificationTemplateResponse>
        {
            Data = items,
            TotalItems = totalCount,
            CurrentPage = page,
            PageSize = pageSize,
            TotalPages = (int)Math.Ceiling((double)totalCount / pageSize)
        });
    }

    /// <summary>
    /// 获取通知模板垃圾箱数量
    /// </summary>
    [HttpGet("templates/trash/count")]
    [RequireSuperAdmin]
    public async Task<IActionResult> GetTemplateTrashCount()
    {
        var count = await _service.GetTemplateTrashCountAsync();
        return Ok(new { count });
    }

    /// <summary>
    /// 恢复通知模板
    /// </summary>
    [HttpPost("templates/{id}/restore")]
    [Permission("notifications.manage")]
    public async Task<IActionResult> RestoreTemplate(int id)
    {
        var success = await _service.RestoreTemplateAsync(id);
        if (!success) return NotFound(new { message = "模板不存在" });
        return Ok();
    }

    /// <summary>
    /// 彻底删除通知模板
    /// </summary>
    [HttpDelete("templates/{id}/hard")]
    [Permission("notifications.manage")]
    public async Task<IActionResult> HardDeleteTemplate(int id)
    {
        var success = await _service.HardDeleteTemplateAsync(id);
        if (!success) return NotFound(new { message = "模板不存在" });
        return Ok();
    }

    /// <summary>
    /// 清空通知模板垃圾箱
    /// </summary>
    [HttpDelete("templates/trash")]
    [Permission("notifications.manage")]
    public async Task<IActionResult> ClearTemplateTrash()
    {
        var count = await _service.ClearTemplateTrashAsync();
        return Ok(new { count });
    }

    // ===== 通知规则 =====

    /// <summary>
    /// 获取通知规则概览
    /// </summary>
    [HttpGet("rules/overview")]
    [RequireSuperAdmin]
    public async Task<ActionResult<NotificationRuleOverviewResponse>> GetRuleOverview()
    {
        var overview = await _service.GetRuleOverviewAsync();
        return Ok(overview);
    }

    /// <summary>
    /// 分页获取通知规则
    /// </summary>
    [HttpGet("rules")]
    [RequireSuperAdmin]
    public async Task<IActionResult> GetRules(
        [FromQuery] int page = 1,
        [FromQuery] int pageSize = PaginationHelper.SmallPageSize,
        [FromQuery] string? eventType = null,
        [FromQuery] int? mailboxId = null,
        [FromQuery] int? userId = null,
        [FromQuery] bool? isActive = null,
        [FromQuery] string? search = null)
    {
        // 限制分页参数上限，防止请求大量数据导致 DoS
        page = Math.Max(page, 1);
        pageSize = Math.Min(Math.Max(pageSize, 1), PaginationHelper.MaxPageSize);
        var query = new NotificationRuleQuery
        {
            Page = page,
            PageSize = pageSize,
            EventType = eventType,
            MailboxId = mailboxId,
            UserId = userId,
            IsActive = isActive,
            Search = search
        };
        var (items, totalCount) = await _service.GetPagedRulesAsync(query);
        return Ok(new PagedResponse<NotificationRuleResponse>
        {
            Data = items,
            TotalItems = totalCount,
            CurrentPage = page,
            PageSize = pageSize,
            TotalPages = (int)Math.Ceiling((double)totalCount / pageSize)
        });
    }

    /// <summary>
    /// 获取所有通知规则（不分页）
    /// </summary>
    [HttpGet("rules/all")]
    [RequireSuperAdmin]
    public async Task<IActionResult> GetAllRules()
    {
        var rules = await _service.GetRulesAsync();
        return Ok(rules);
    }

    /// <summary>
    /// 按事件类型获取通知规则
    /// </summary>
    /// <param name="eventType">事件类型</param>
    /// <returns>规则列表</returns>
    [HttpGet("rules/type/{eventType}")]
    [RequireSuperAdmin]
    public async Task<IActionResult> GetRulesByEventType(string eventType)
    {
        var rules = await _service.GetRulesByEventTypeAsync(eventType);
        return Ok(rules);
    }

    /// <summary>
    /// 获取通知规则详情
    /// </summary>
    /// <param name="id">规则 ID</param>
    /// <returns>规则详情</returns>
    /// <response code="200">返回规则</response>
    /// <response code="404">规则不存在</response>
    [HttpGet("rules/{id}")]
    [RequireSuperAdmin]
    public async Task<IActionResult> GetRule(int id)
    {
        var rule = await _service.GetRuleByIdAsync(id);
        if (rule == null)
        {
            return NotFound(new { message = "规则不存在" });
        }
        return Ok(rule);
    }

    /// <summary>
    /// 创建通知规则
    /// </summary>
    /// <param name="request">创建规则请求</param>
    /// <returns>新创建的规则</returns>
    /// <response code="201">创建成功</response>
    [HttpPost("rules")]
    [Permission("notifications.manage")]
    public async Task<IActionResult> CreateRule([FromBody] CreateNotificationRuleRequest request)
    {
        var rule = await _service.CreateRuleAsync(request);
        return CreatedAtAction(nameof(GetRule), new { id = rule.Id }, rule);
    }

    /// <summary>
    /// 更新通知规则
    /// </summary>
    /// <param name="id">规则 ID</param>
    /// <param name="request">更新请求</param>
    /// <returns>更新后的规则</returns>
    /// <response code="200">更新成功</response>
    /// <response code="404">规则不存在</response>
    [HttpPut("rules/{id}")]
    [Permission("notifications.manage")]
    public async Task<IActionResult> UpdateRule(int id, [FromBody] UpdateNotificationRuleRequest request,
        [FromHeader(Name = "If-Match")] string? ifMatch = null)
    {
        var rule = await _service.UpdateRuleAsync(id, request, ifMatch);
        if (rule == null)
        {
            return NotFound(new { message = "规则不存在" });
        }
        return Ok(rule);
    }

    /// <summary>
    /// 删除通知规则
    /// </summary>
    /// <param name="id">规则 ID</param>
    /// <returns>无内容</returns>
    /// <response code="204">删除成功</response>
    /// <response code="404">规则不存在</response>
    [HttpDelete("rules/{id}")]
    [Permission("notifications.manage")]
    public async Task<IActionResult> DeleteRule(int id)
    {
        var result = await _service.DeleteRuleAsync(id);
        if (!result)
        {
            return NotFound(new { message = "规则不存在" });
        }
        return Ok();
    }

    /// <summary>
    /// 切换通知规则启用状态
    /// </summary>
    [HttpPost("rules/{id}/toggle")]
    [Permission("notifications.manage")]
    public async Task<ActionResult<NotificationRuleResponse>> ToggleRuleActive(int id)
    {
        var rule = await _service.ToggleRuleActiveAsync(id);
        if (rule == null)
        {
            return NotFound(new { message = "规则不存在" });
        }
        return Ok(rule);
    }

    /// <summary>
    /// 复制通知规则
    /// </summary>
    [HttpPost("rules/{id}/duplicate")]
    [Permission("notifications.manage")]
    public async Task<IActionResult> DuplicateRule(int id, [FromBody] DuplicateNotificationRuleRequest? request = null)
    {
        var rule = await _service.DuplicateRuleAsync(id, request?.NewName);
        if (rule == null)
        {
            return NotFound(new { message = "规则不存在" });
        }
        return Ok(rule);
    }

    /// <summary>
    /// 获取通知规则垃圾箱列表
    /// </summary>
    [HttpGet("rules/trash")]
    [RequireSuperAdmin]
    public async Task<IActionResult> GetRuleTrash(
        [FromQuery] int page = 1,
        [FromQuery] int pageSize = PaginationHelper.SmallPageSize,
        [FromQuery] string? search = null)
    {
        // 限制分页参数上限，防止请求大量数据导致 DoS
        page = Math.Max(page, 1);
        pageSize = Math.Min(Math.Max(pageSize, 1), PaginationHelper.MaxPageSize);
        var (items, totalCount) = await _service.GetRuleTrashAsync(page, pageSize, search);
        return Ok(new PagedResponse<NotificationRuleResponse>
        {
            Data = items,
            TotalItems = totalCount,
            CurrentPage = page,
            PageSize = pageSize,
            TotalPages = (int)Math.Ceiling((double)totalCount / pageSize)
        });
    }

    /// <summary>
    /// 获取通知规则垃圾箱数量
    /// </summary>
    [HttpGet("rules/trash/count")]
    [RequireSuperAdmin]
    public async Task<IActionResult> GetRuleTrashCount()
    {
        var count = await _service.GetRuleTrashCountAsync();
        return Ok(new { count });
    }

    /// <summary>
    /// 恢复通知规则
    /// </summary>
    [HttpPost("rules/{id}/restore")]
    [Permission("notifications.manage")]
    public async Task<IActionResult> RestoreRule(int id)
    {
        var success = await _service.RestoreRuleAsync(id);
        if (!success) return NotFound(new { message = "规则不存在" });
        return Ok();
    }

    /// <summary>
    /// 彻底删除通知规则
    /// </summary>
    [HttpDelete("rules/{id}/hard")]
    [Permission("notifications.manage")]
    public async Task<IActionResult> HardDeleteRule(int id)
    {
        var success = await _service.HardDeleteRuleAsync(id);
        if (!success) return NotFound(new { message = "规则不存在" });
        return Ok();
    }

    /// <summary>
    /// 清空通知规则垃圾箱
    /// </summary>
    [HttpDelete("rules/trash")]
    [Permission("notifications.manage")]
    public async Task<IActionResult> ClearRuleTrash()
    {
        var count = await _service.ClearRuleTrashAsync();
        return Ok(new { count });
    }

    // ===== 发送通知 =====

    /// <summary>
    /// 发送通知（手动触发）
    /// </summary>
    /// <param name="request">发送通知请求</param>
    /// <returns>发送结果</returns>
    /// <remarks>
    /// 事件类型示例:
    /// - conversation.created - 新会话创建
    /// - conversation.assigned - 会话被分配
    /// - conversation.replied - 会话收到回复
    /// - conversation.closed - 会话被关闭
    /// 
    /// 变量示例: {customer_name}, {subject}, {conversation_id}, {user_name}
    /// </remarks>
    [HttpPost("send")]
    [Permission("notifications.manage")]
    public async Task<IActionResult> Send([FromBody] SendNotificationRequest request)
    {
        // EventType 空白/长度/格式校验已由 [ApiController] 基于 SendNotificationRequest 的
        // [Required] + [MaxLength(100)] + [RegularExpression] 自动处理

        var result = await _service.SendNotificationAsync(request);
        return Ok(result);
    }

    /// <summary>
    /// 预览模板渲染结果
    /// </summary>
    /// <param name="request">包含模板内容和变量的请求</param>
    /// <returns>渲染后的文本</returns>
    [HttpPost("preview")]
    [Permission("notifications.manage")]
    public IActionResult PreviewTemplate([FromBody] PreviewTemplateRequest request)
    {
        var rendered = _service.RenderTemplate(request.Template, request.Variables);
        string? renderedSubject = null;
        if (request.Subject != null)
        {
            renderedSubject = _service.RenderTemplate(request.Subject, request.Variables);
        }
        return Ok(new { rendered, renderedSubject });
    }
}
