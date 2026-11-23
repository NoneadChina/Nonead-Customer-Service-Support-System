using NoneadCSS.Api.Authorization;
using NoneadCSS.Application.DTOs.Requests;
using NoneadCSS.Application.DTOs.Responses;
using NoneadCSS.Application.Services;
using NoneadCSS.Application.Services.Calendar;
using NoneadCSS.Api.Filters;
using NoneadCSS.Application.Exceptions;
using NoneadCSS.Application.Mappings;
using NoneadCSS.Domain;
using NoneadCSS.Domain.Enums;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.RateLimiting;
using Microsoft.EntityFrameworkCore;
using System.Security.Claims;

namespace NoneadCSS.Api.Controllers;

/// <summary>
/// 日历控制器 - 管理工单截止日期、计划任务、会议等日历事件
/// </summary>
[ApiController]
[Route("api/[controller]")]
[Authorize]
[RequireModule("calendar")]
public class CalendarController : ControllerBase
{
    private readonly ICalendarService _service;
    private readonly ICalendarMutationService _mutationService;
    private readonly ICalendarEventTemplateService _templateService;
    private readonly ICalendarExportService _exportService;
    private readonly IMailboxService _mailboxService;
    private readonly IUserRoleResolver _userRoleResolver;
    private readonly IUnitOfWork _unitOfWork;
    private readonly ICurrentUserAccessor _currentUserAccessor;
    private readonly ICalendarOutlookHealthService _outlookHealthService;
    private readonly IOutlookSyncService _outlookSyncService;
    private readonly IFrontendUrlResolver _frontendUrlResolver;
    private readonly ILocalizationService? _localization;

    public CalendarController(ICalendarService service, ICalendarMutationService mutationService, ICalendarEventTemplateService templateService, ICalendarExportService exportService, IMailboxService mailboxService, IUserRoleResolver userRoleResolver, IUnitOfWork unitOfWork, ICurrentUserAccessor currentUserAccessor, ICalendarOutlookHealthService outlookHealthService, IOutlookSyncService outlookSyncService, IFrontendUrlResolver frontendUrlResolver, ILocalizationService? localization = null)
    {
        _service = service;
        _mutationService = mutationService;
        _templateService = templateService;
        _exportService = exportService;
        _mailboxService = mailboxService;
        _userRoleResolver = userRoleResolver;
        _unitOfWork = unitOfWork;
        _currentUserAccessor = currentUserAccessor;
        _outlookHealthService = outlookHealthService;
        _outlookSyncService = outlookSyncService;
        _frontendUrlResolver = frontendUrlResolver;
        _localization = localization;
    }

    /// <summary>
    /// 按当前请求语言取 resx 词条；词条缺失时回落中文原文，绝不把键名返回给用户。
    /// </summary>
    private string Localize(string key, string zhFallback)
    {
        if (_localization is null) return zhFallback;
        var text = _localization.GetString(key);
        return string.IsNullOrWhiteSpace(text) || string.Equals(text, key, StringComparison.Ordinal) ? zhFallback : text;
    }
    private async Task<bool> HasMailboxAccess(int? mailboxId)
    {
        if (mailboxId == null)
        {
            return true;
        }

        var userId = _currentUserAccessor.UserId;
        if (await _userRoleResolver.IsSuperAdminAsync(userId))
        {
            return true;
        }

        var mailboxes = await _mailboxService.GetMailboxesByUserIdAsync(userId);
        return mailboxes.Any(m => m.Id == mailboxId);
    }

    /// <summary>
    /// 获取日历事件列表（分页+筛选）
    /// </summary>
    /// <remarks>
    /// 根据时间范围、邮箱、用户、事件类型、状态等条件查询日历事件。
    /// 
    /// 示例：
    /// - GET /api/calendar?startDate=2026-07-01&amp;endDate=2026-07-31
    /// - GET /api/calendar?userId=1&amp;status=pending
    /// </remarks>
    /// <param name="startDate">开始日期（含），格式 yyyy-MM-dd</param>
    /// <param name="endDate">结束日期（含），格式 yyyy-MM-dd</param>
    /// <param name="mailboxId">可选邮箱 ID</param>
    /// <param name="userId">可选用户 ID</param>
    /// <param name="eventType">可选事件类型：due_date / task / meeting / reminder</param>
    /// <param name="status">可选状态：pending / in_progress / completed / cancelled / missed</param>
    /// <param name="keyword">可选关键词（标题或描述）</param>
    /// <param name="page">页码，默认 1</param>
    /// <param name="pageSize">每页数量，默认 50</param>
    /// <param name="expandRecurring">是否展开重复日程，默认 true</param>
    /// <param name="sortBy">排序字段（可选），如 start / updated</param>
    /// <param name="sortDir">排序方向：asc / desc（可选）</param>
    /// <returns>分页事件列表</returns>
    [HttpGet]
    public async Task<ActionResult> GetList(
        [FromQuery] DateTime? startDate = null,
        [FromQuery] DateTime? endDate = null,
        [FromQuery] int? mailboxId = null,
        [FromQuery] int? userId = null,
        [FromQuery] string? eventType = null,
        [FromQuery] string? status = null,
        [FromQuery] string? keyword = null,
        [FromQuery] int page = 1,
        [FromQuery] int pageSize = PaginationHelper.LargePageSize,
        [FromQuery] bool expandRecurring = true,
        [FromQuery] string? sortBy = null,
        [FromQuery] string? sortDir = null)
    {
        // 防止 IDOR：指定 mailboxId 时校验邮箱归属权限
        if (mailboxId.HasValue && !await HasMailboxAccess(mailboxId))
        {
            return Forbid();
        }

        // 防止 IDOR：非 SuperAdmin 传 userId 参数时只能查自己
        if (userId.HasValue && userId.Value != _currentUserAccessor.UserId && !_currentUserAccessor.IsSuperAdmin)
        {
            return Forbid();
        }

        // 限制分页参数上限，防止请求大量数据导致 DoS
        page = Math.Max(page, 1);
        pageSize = Math.Min(Math.Max(pageSize, 1), PaginationHelper.MaxPageSize);
        // M-5：规范化 DateTime? Kind，避免 Unspecified 类型在 UTC 比较时产生时区偏差
        DateTime? normalizedStartDate = startDate.HasValue
            ? (startDate.Value.Kind == DateTimeKind.Unspecified
                ? DateTime.SpecifyKind(startDate.Value, DateTimeKind.Utc)
                : startDate.Value.ToUniversalTime())
            : null;
        DateTime? normalizedEndDate = endDate.HasValue
            ? (endDate.Value.Kind == DateTimeKind.Unspecified
                ? DateTime.SpecifyKind(endDate.Value, DateTimeKind.Utc)
                : endDate.Value.ToUniversalTime())
            : null;
        var query = new CalendarEventQuery
        {
            StartDate = normalizedStartDate,
            EndDate = normalizedEndDate,
            MailboxId = mailboxId,
            UserId = userId,
            EventType = eventType,
            Status = status,
            Keyword = keyword,
            Page = page,
            PageSize = pageSize,
            ExpandRecurring = expandRecurring,
            SortBy = sortBy,
            SortDir = sortDir
        };

        var (items, total) = await _service.GetListAsync(query);
        return Ok(new PagedResponse<CalendarEventDto>
        {
            Data = items,
            CurrentPage = page,
            TotalPages = (int)Math.Ceiling((double)total / pageSize),
            TotalItems = total,
            PageSize = pageSize
        });
    }

    /// <summary>
    /// 获取事件详情
    /// </summary>
    /// <param name="id">事件 ID</param>
    /// <returns>事件详情</returns>
    [HttpGet("{id}")]
    public async Task<ActionResult<CalendarEventDto>> GetById(int id)
    {
        var result = await _service.GetByIdAsync(id);
        if (result == null) return NotFound();

        if (!await HasMailboxAccess(result.MailboxId))
        {
            return Forbid();
        }

        return Ok(result);
    }

    /// <summary>
    /// 获取日历概览统计
    /// </summary>
    /// <returns>概览统计数据</returns>
    [HttpGet("overview")]
    public async Task<ActionResult<CalendarOverviewResponse>> GetOverview()
    {
        var result = await _service.GetOverviewAsync();
        return Ok(result);
    }

    /// <summary>
    /// 创建日历事件
    /// </summary>
    /// <param name="request">创建请求</param>
    /// <returns>创建后的事件</returns>
    [HttpPost]
    [Permission("calendar.manage")]
    public async Task<ActionResult<CalendarEventDto>> Create([FromBody] CreateCalendarEventRequest request)
    {
        if (!ModelState.IsValid) return BadRequest(ModelState);
        var result = await _service.CreateAsync(request);
        return CreatedAtAction(nameof(GetById), new { id = result.Id }, result);
    }

    /// <summary>
    /// 更新日历事件
    /// </summary>
    /// <param name="id">事件 ID</param>
    /// <param name="request">更新请求（updateMode: single=仅本次, following=本次及后续, all=全部）</param>
    /// <param name="ifMatch">可选：ETag（Base64 RowVersion）用于并发冲突检测，优先级高于 request.ifMatch，冲突返回 409</param>
    /// <returns>更新后的事件</returns>
    [HttpPut("{id}")]
    [Permission("calendar.manage")]
    public async Task<ActionResult<CalendarEventDto>> Update(int id, [FromBody] UpdateCalendarEventRequest request,
        [FromHeader(Name = "If-Match")] string? ifMatch = null)
    {
        if (!ModelState.IsValid) return BadRequest(ModelState);
        if (id != request.Id) return BadRequest("ID 不一致");
        // HTTP 标准优先：If-Match 请求头覆盖 body.ifMatch（保持与 Delete/UpdateStatus 等端点一致）
        if (!string.IsNullOrEmpty(ifMatch)) request.IfMatch = ifMatch;
        try
        {
            var result = await _service.UpdateAsync(id, request);
            if (result == null) return NotFound();
            return Ok(result);
        }
        catch (CalendarConcurrencyException ex)
        {
            return Conflict(new
            {
                message = ex.Message,
                eventId = ex.EventId,
                latest = ex.Latest
            });
        }
        catch (ConcurrencyException ex)
        {
            return Conflict(new
            {
                message = ex.Message,
                entityId = ex.EntityId,
                entityType = ex.EntityType,
                latest = ex.Latest
            });
        }
        catch (DbUpdateConcurrencyException)
        {
            var latest = await BuildLatestSnapshotAsync(id).ConfigureAwait(false);
            return Conflict(new
            {
                message = "事件已被其他用户修改，请刷新后重试",
                eventId = id,
                latest
            });
        }
    }

    private async Task<CalendarEventDto?> BuildLatestSnapshotAsync(int id)
    {
        var evt = await _unitOfWork.CalendarEvents.GetByIdAsync(id).ConfigureAwait(false);
        if (evt == null) return null;
        return evt.ToDto().SanitizeForUser(_currentUserAccessor.UserId, _currentUserAccessor.IsSuperAdmin);
    }

    /// <summary>
    /// 删除日历事件（软删除）
    /// </summary>
    /// <param name="id">事件 ID</param>
    /// <param name="deleteMode">删除模式：single=仅本次, following=本次及后续, all=全部</param>
    /// <param name="ifMatch">可选：ETag（Base64 RowVersion）用于并发冲突检测，冲突返回 409</param>
    [HttpDelete("{id}")]
    [Permission("calendar.manage")]
    public async Task<ActionResult> Delete(int id, [FromQuery] string deleteMode = "single",
        [FromHeader(Name = "If-Match")] string? ifMatch = null)
    {
        var success = await _service.DeleteAsync(id, deleteMode, ifMatch);
        if (!success) return NotFound();
        return NoContent();
    }

    /// <summary>
    /// 更新事件状态
    /// </summary>
    /// <param name="id">事件 ID</param>
    /// <param name="status">新状态：pending / in_progress / completed / cancelled / missed</param>
    /// <param name="ifMatch">可选：ETag（Base64 RowVersion）用于并发冲突检测，冲突返回 409</param>
    [HttpPost("{id}/status")]
    [Permission("calendar.manage")]
    public async Task<ActionResult> UpdateStatus(int id, [FromQuery] string status,
        [FromHeader(Name = "If-Match")] string? ifMatch = null)
    {
        var success = await _service.UpdateStatusAsync(id, status, ifMatch);
        if (!success) return NotFound();
        return Ok();
    }

    /// <summary>
    /// 复制日历事件（创建新事件，可选调整时间/标题）
    /// </summary>
    /// <param name="id">源事件 ID</param>
    /// <param name="request">可选请求体：startAt/endAt/title（不传则使用原事件值，标题加" (副本)"后缀）</param>
    /// <returns>复制后的新事件</returns>
    /// <remarks>
    /// 示例：
    /// - POST /api/calendar/123/duplicate（无 body，直接复制）
    /// - POST /api/calendar/123/duplicate  body: { "startAt": "2026-09-01T09:00:00Z", "title": "新标题" }
    /// </remarks>
    [HttpPost("{id}/duplicate")]
    [Permission("calendar.manage")]
    public async Task<ActionResult<CalendarEventDto>> Duplicate(int id, [FromBody] DuplicateCalendarEventRequest? request = null)
    {
        if (!ModelState.IsValid) return BadRequest(ModelState);
        var result = await _mutationService.DuplicateAsync(id, request);
        return CreatedAtAction(nameof(GetById), new { id = result.Id }, result);
    }

    /// <summary>
    /// 快速更新事件（仅更新指定字段，其余保持不变）
    /// </summary>
    /// <param name="id">事件 ID</param>
    /// <param name="request">快速更新请求（所有字段可选，null 表示不更新）</param>
    /// <param name="ifMatch">可选：ETag（Base64 RowVersion）用于并发冲突检测，优先级高于 request.ifMatch，冲突返回 409</param>
    /// <returns>更新后的事件</returns>
    /// <remarks>
    /// 支持快速更新：status / priority / progress / location / meetingUrl / title / description / color / reminderMinutes。
    /// 不涉及时间/重复规则变更；若携带 If-Match 仍会触发并发冲突检测，防止高并发下计数/状态后写覆盖先写。
    /// </remarks>
    [HttpPatch("{id}/quick-update")]
    [Permission("calendar.manage")]
    public async Task<ActionResult<CalendarEventDto>> QuickUpdate(int id, [FromBody] QuickUpdateCalendarEventRequest request,
        [FromHeader(Name = "If-Match")] string? ifMatch = null)
    {
        if (!ModelState.IsValid) return BadRequest(ModelState);
        // HTTP 标准优先：If-Match 请求头覆盖 body.ifMatch
        if (!string.IsNullOrEmpty(ifMatch)) request.IfMatch = ifMatch;
        try
        {
            var result = await _mutationService.QuickUpdateAsync(id, request);
            if (result == null) return NotFound();
            return Ok(result);
        }
        catch (CalendarConcurrencyException ex)
        {
            return Conflict(new
            {
                message = ex.Message,
                eventId = ex.EventId,
                latest = ex.Latest
            });
        }
        catch (ConcurrencyException ex)
        {
            return Conflict(new
            {
                message = ex.Message,
                entityId = ex.EntityId,
                entityType = ex.EntityType,
                latest = ex.Latest
            });
        }
        catch (DbUpdateConcurrencyException)
        {
            var latest = await BuildLatestSnapshotAsync(id).ConfigureAwait(false);
            return Conflict(new
            {
                message = "事件已被其他用户修改，请刷新后重试",
                eventId = id,
                latest
            });
        }
    }

    // 软删除 - 垃圾箱

    /// <summary>
    /// 获取垃圾箱列表
    /// </summary>
    /// <param name="page">页码，默认 1</param>
    /// <param name="pageSize">每页数量，默认 50</param>
    /// <param name="keyword">可选关键词</param>
    /// <returns>垃圾箱事件列表</returns>
    [HttpGet("trash")]
    [Permission("calendar.manage")]
    public async Task<ActionResult> GetTrash(
        [FromQuery] int page = 1,
        [FromQuery] int pageSize = PaginationHelper.LargePageSize,
        [FromQuery] string? keyword = null)
    {
        // 限制分页参数上限，防止请求大量数据导致 DoS
        page = Math.Max(page, 1);
        pageSize = Math.Min(Math.Max(pageSize, 1), PaginationHelper.MaxPageSize);
        var (items, total) = await _service.GetTrashAsync(page, pageSize, keyword);
        return Ok(new PagedResponse<CalendarEventDto>
        {
            Data = items,
            CurrentPage = page,
            TotalPages = (int)Math.Ceiling((double)total / pageSize),
            TotalItems = total,
            PageSize = pageSize
        });
    }

    /// <summary>
    /// 获取垃圾箱数量
    /// </summary>
    /// <returns>垃圾箱事件数量</returns>
    [HttpGet("trash/count")]
    [Permission("calendar.manage")]
    public async Task<ActionResult<int>> GetTrashCount()
    {
        var count = await _service.GetTrashCountAsync();
        return Ok(count);
    }

    /// <summary>
    /// 恢复已删除的事件
    /// </summary>
    /// <param name="id">事件 ID</param>
    /// <param name="ifMatch">可选：ETag（Base64 RowVersion）用于并发冲突检测，冲突返回 409</param>
    [HttpPost("{id}/restore")]
    [Permission("calendar.manage")]
    public async Task<ActionResult> Restore(int id,
        [FromHeader(Name = "If-Match")] string? ifMatch = null)
    {
        var success = await _service.RestoreAsync(id, ifMatch);
        if (!success) return NotFound();
        return Ok();
    }

    /// <summary>
    /// 彻底删除事件（不可恢复）
    /// </summary>
    /// <param name="id">事件 ID</param>
    /// <param name="ifMatch">可选：ETag（Base64 RowVersion）用于并发冲突检测，冲突返回 409</param>
    [HttpDelete("{id}/hard")]
    [Permission("calendar.manage")]
    public async Task<ActionResult> HardDelete(int id,
        [FromHeader(Name = "If-Match")] string? ifMatch = null)
    {
        var success = await _service.HardDeleteAsync(id, ifMatch);
        if (!success) return NotFound();
        return NoContent();
    }

    /// <summary>
    /// 清空垃圾箱（彻底删除所有软删除的日历事件，SuperAdmin 专用）。
    /// </summary>
    /// <returns>清空的事件数量</returns>
    [HttpPost("trash/clear")]
    [RequireSuperAdmin]
    [Permission("calendar.manage")]
    public async Task<ActionResult<int>> ClearTrash()
    {
        var count = await _service.ClearTrashAsync();
        return Ok(new { cleared = count });
    }

    /// <summary>
    /// 按保留期清空过期回收站（SuperAdmin 专用）。
    /// </summary>
    /// <param name="retentionDays">保留天数，默认 30；传 0 表示清空全部垃圾箱</param>
    [HttpPost("trash/purge")]
    [RequireSuperAdmin]
    [Permission("calendar.manage")]
    public async Task<ActionResult<int>> PurgeTrash([FromQuery] int retentionDays = 30)
    {
        if (retentionDays < 0) return BadRequest("retentionDays 不能小于 0");
        var count = await _service.PurgeTrashAsync(retentionDays);
        return Ok(new { purged = count, retentionDays });
    }

    // ICalendar (.ics) 导入/导出

    /// <summary>
    /// 导出单个事件为 .ics 文件
    /// </summary>
    /// <param name="id">事件 ID</param>
    /// <returns>text/calendar 文件流</returns>
    [HttpGet("{id:int}/export")]
    [EnableRateLimiting("CalendarHeavyPolicy")]
    public async Task<IActionResult> ExportEvent(int id)
    {
        // 先通过 Service 获取事件（含权限校验），确认当前用户有权访问
        var evt = await _service.GetByIdAsync(id);
        if (evt == null) return NotFound();

        if (!await HasMailboxAccess(evt.MailboxId))
        {
            return Forbid();
        }

        var ics = await _exportService.ExportEventAsync(id);
        return File(System.Text.Encoding.UTF8.GetBytes(ics), "text/calendar", $"event-{id}.ics");
    }

    /// <summary>
    /// 按日期范围导出日历为 .ics 文件
    /// </summary>
    /// <param name="startDate">开始日期（含），格式 yyyy-MM-dd</param>
    /// <param name="endDate">结束日期（含），格式 yyyy-MM-dd</param>
    /// <param name="mailboxId">可选邮箱 ID</param>
    /// <returns>text/calendar 文件流</returns>
    [HttpGet("export")]
    [EnableRateLimiting("CalendarHeavyPolicy")]
    public async Task<IActionResult> ExportRange([FromQuery] DateTime startDate, [FromQuery] DateTime endDate, [FromQuery] int? mailboxId)
    {
        // M-5：规范化 DateTime Kind，避免 Unspecified 类型在 UTC 比较时产生时区偏差
        var normalizedStartDate = startDate.Kind == DateTimeKind.Unspecified
            ? DateTime.SpecifyKind(startDate, DateTimeKind.Utc)
            : startDate.ToUniversalTime();
        var normalizedEndDate = endDate.Kind == DateTimeKind.Unspecified
            ? DateTime.SpecifyKind(endDate, DateTimeKind.Utc)
            : endDate.ToUniversalTime();

        var isSuperAdmin = _currentUserAccessor.IsSuperAdmin;

        // 防止 IDOR：非超管只能导出自己有权访问的邮箱，未指定 mailboxId 时限定为本人事件
        if (mailboxId.HasValue)
        {
            if (!await HasMailboxAccess(mailboxId))
            {
                return Forbid();
            }
        }
        else if (!isSuperAdmin)
        {
            // 非超管未指定邮箱时，仅导出本人创建的事件
            var ics = await _exportService.ExportRangeAsync(normalizedStartDate, normalizedEndDate, null, _currentUserAccessor.UserId);
            return File(System.Text.Encoding.UTF8.GetBytes(ics), "text/calendar", $"calendar-{normalizedStartDate:yyyyMMdd}-{normalizedEndDate:yyyyMMdd}.ics");
        }

        var icsAll = await _exportService.ExportRangeAsync(normalizedStartDate, normalizedEndDate, mailboxId);
        return File(System.Text.Encoding.UTF8.GetBytes(icsAll), "text/calendar", $"calendar-{normalizedStartDate:yyyyMMdd}-{normalizedEndDate:yyyyMMdd}.ics");
    }

    /// <summary>
    /// 从 .ics 文件导入事件
    /// </summary>
    /// <param name="file">.ics 文件</param>
    /// <param name="mailboxId">可选邮箱 ID（导入事件将绑定到此邮箱）</param>
    /// <returns>导入结果 { imported, skipped }</returns>
    [HttpPost("import")]
    [Permission("calendar.manage")]
    [EnableRateLimiting("CalendarHeavyPolicy")]
    public async Task<ActionResult> ImportEvent(IFormFile file, [FromQuery] int? mailboxId)
    {
        if (file == null || file.Length == 0)
            return BadRequest("请上传有效的 .ics 文件");

        // H-17/M-1：校验文件扩展名，仅允许 .ics
        var extension = Path.GetExtension(file.FileName).ToLowerInvariant();
        if (extension != ".ics")
        {
            return BadRequest("仅支持 .ics 文件");
        }

        // H-17/M-1：校验文件大小（10MB 上限）
        const int maxFileSize = 10 * 1024 * 1024;
        if (file.Length > maxFileSize)
        {
            return BadRequest($"文件大小超过限制（最大 {maxFileSize / 1024 / 1024}MB）");
        }

        // H-17/M-1：宽松校验 Content-Type，仅记录不拒绝（某些浏览器可能不正确设置 Content-Type）
        var contentType = file.ContentType?.ToLowerInvariant() ?? "";
        if (contentType != "text/calendar" && contentType != "application/octet-stream" && contentType != "text/plain")
        {
            // 不阻止导入，避免误伤未正确设置 Content-Type 的客户端
        }

        if (mailboxId.HasValue && !await HasMailboxAccess(mailboxId))
        {
            return Forbid();
        }

        using var reader = new StreamReader(file.OpenReadStream());
        var icsContent = await reader.ReadToEndAsync();

        var (imported, skipped) = await _exportService.ImportAsync(icsContent, mailboxId);
        return Ok(new { imported, skipped });
    }

    /// <summary>
    /// 获取当前用户的日历订阅 URL（用于 Outlook/Google Calendar 订阅）
    /// </summary>
    /// <returns>订阅 URL 和 Token</returns>
    [HttpGet("subscription")]
    public async Task<ActionResult> GetSubscription()
    {
        var userId = _currentUserAccessor.UserId;
        if (userId == 0) return Unauthorized();

        // 获取用户记录的 UpdatedAt，用于生成带版本号的订阅令牌（支持吊销）
        var user = await _unitOfWork.Users.GetByIdAsync(userId);
        if (user == null || user.DeletedAt != null) return Unauthorized();

        var token = CalendarFeedController.GenerateToken(userId, user.UpdatedAt);
        // T000098 修复：使用配置的 App:BaseUrl 生成订阅 URL，避免使用 Request.Host 动态推导。
        // 当用户从本地（127.0.0.1）访问前端时，Request.Host 会返回 127.0.0.1:5173，
        // 导致生成的订阅 URL 在外部 Outlook 客户端无法访问（127.0.0.1 指向客户端本机）。
        // 使用 IFrontendUrlResolver.GetBaseUrl() 确保使用配置的外部可访问域名。
        var baseUrl = _frontendUrlResolver.GetBaseUrl();
        var feedUrl = $"{baseUrl}/api/calendar/feed?token={token}";

        return Ok(new
        {
            url = feedUrl,
            token,
            // 订阅指引是服务端文案（不是数据），出口词典不会翻译 200 响应里的任意字段，
            // 因此这里按 resx 取值；取不到词条时回落中文原文（后台/旧构造点同样安全）。
            instructions = Localize("CalendarSubscriptionInstructions", "在 Outlook 中：文件 → 打开和导出 → 管理订阅 → 添加订阅 → 粘贴此 URL。注意：此 URL 包含订阅令牌，请勿分享给他人。")
        });
    }

    /// <summary>
    /// 重新生成当前用户的日历订阅令牌（使旧链接立即失效）
    /// </summary>
    [HttpPost("subscription/regenerate")]
    [Permission("calendar.manage")]
    public async Task<ActionResult> RegenerateSubscription()
    {
        var userId = _currentUserAccessor.UserId;
        if (userId == 0) return Unauthorized();

        // 使用 GetByIdTrackedAsync 获取跟踪实体，避免 AsNoTracking + Update 导致整个对象图被标记为 Modified
        var user = await _unitOfWork.Users.GetByIdTrackedAsync(userId);
        if (user == null || user.DeletedAt != null) return Unauthorized();

        // 刷新 UpdatedAt 使旧令牌失效（GenerateToken 基于 UpdatedAt 生成）
        user.UpdatedAt = DateTime.UtcNow;
        await _unitOfWork.SaveChangesAsync();

        var token = CalendarFeedController.GenerateToken(userId, user.UpdatedAt);
        // T000098 修复：同步使用 IFrontendUrlResolver.GetBaseUrl() 生成订阅 URL
        var baseUrl = _frontendUrlResolver.GetBaseUrl();
        var feedUrl = $"{baseUrl}/api/calendar/feed?token={token}";

        return Ok(new
        {
            url = feedUrl,
            token,
            instructions = "已生成新的订阅链接，旧链接已立即失效。"
        });
    }

    // ============================================================
    //  P1-2: 批量操作
    // ============================================================

    /// <summary>
    /// 日历批量操作
    /// </summary>
    /// <remarks>
    /// 支持操作：delete / setStatus / setEventType / moveOffset
    /// 
    /// 示例：
    /// - 删除：{ "ids":[1,2,3], "op":"delete" }
    /// - 改状态：{ "ids":[1,2], "op":"setStatus", "status":"completed" }
    /// - 改类型：{ "ids":[1,2], "op":"setEventType", "eventType":"meeting" }
    /// - 移动1天+30分：{ "ids":[1,2], "op":"moveOffset", "daysOffset":1, "minutesOffset":30 }
    /// </remarks>
    [HttpPost("batch")]
    [Permission("calendar.manage")]
    [EnableRateLimiting("CalendarHeavyPolicy")]
    public async Task<ActionResult<CalendarBatchOperationResponse>> BatchOperation(
        [FromBody] CalendarBatchOperationRequest request)
    {
        if (!ModelState.IsValid) return BadRequest(ModelState);
        var result = await _service.BatchOperationAsync(request);
        return Ok(result);
    }

    // ============================================================
    //  P1-3: 事件模板
    // ============================================================

    /// <summary>
    /// 获取日历事件模板列表
    /// </summary>
    /// <param name="mailboxId">可选邮箱 ID 过滤</param>
    [HttpGet("templates")]
    public async Task<ActionResult> GetTemplates([FromQuery] int? mailboxId = null)
    {
        var (items, total) = await _templateService.GetListAsync(mailboxId, includeShared: true);
        return Ok(new { items, total });
    }

    /// <summary>
    /// 获取单个事件模板
    /// </summary>
    [HttpGet("templates/{id:int}")]
    public async Task<ActionResult> GetTemplate(int id)
    {
        var dto = await _templateService.GetByIdAsync(id);
        if (dto == null) return NotFound("模板不存在或无权限");
        return Ok(dto);
    }

    /// <summary>
    /// 创建事件模板
    /// </summary>
    [HttpPost("templates")]
    [Permission("calendar.manage")]
    public async Task<ActionResult> CreateTemplate([FromBody] CreateCalendarEventTemplateRequest request)
    {
        if (!ModelState.IsValid) return BadRequest(ModelState);
        var dto = await _templateService.CreateAsync(request);
        return CreatedAtAction(nameof(GetTemplate), new { id = dto.Id }, dto);
    }

    /// <summary>
    /// 更新事件模板
    /// </summary>
    [HttpPut("templates/{id:int}")]
    [Permission("calendar.manage")]
    public async Task<ActionResult> UpdateTemplate(int id, [FromBody] UpdateCalendarEventTemplateRequest request)
    {
        if (!ModelState.IsValid) return BadRequest(ModelState);
        var dto = await _templateService.UpdateAsync(id, request);
        if (dto == null) return NotFound("模板不存在或无权限");
        return Ok(dto);
    }

    /// <summary>
    /// 删除事件模板（软删除）
    /// </summary>
    [HttpDelete("templates/{id:int}")]
    [Permission("calendar.manage")]
    public async Task<ActionResult> DeleteTemplate(int id)
    {
        var ok = await _templateService.DeleteAsync(id);
        if (!ok) return NotFound("模板不存在或无权限");
        return NoContent();
    }

    // ============================================================
    //  P2-A#5 #7.3: 提醒确认 ACK（避免 5 分钟兜底补发）
    // ============================================================

    /// <summary>
    /// 用户在 SignalR 弹出卡片上点击"确认已收到"或自动关闭时调用本接口。
    /// 原子地把事件的 ReminderConfirmation 从 pending/fallback_in_progress/fallback_sent
    /// 更新为 confirmed，同时写入 ReminderConfirmedAt = now，避免 5 分钟 FallbackJob
    /// 再次走 EMAIL+IM 兜底通道。
    /// </summary>
    /// <param name="id">事件 ID</param>
    /// <param name="mode">确认来源：user_click（用户点确认按钮）或 timeout_auto_close（弹层超时关闭）</param>
    /// <param name="ifMatch">可选：ETag（Base64 RowVersion）用于并发冲突检测，冲突返回 409</param>
    [HttpPost("{id:int}/reminder-confirm")]
    [Permission("calendar.manage")]
    [EnableRateLimiting("CalendarLightPolicy")]
    public async Task<ActionResult> ConfirmReminder(int id, [FromQuery] string mode = "user_click",
        [FromHeader(Name = "If-Match")] string? ifMatch = null)
    {
        var userIdStr = User.FindFirst(ClaimTypes.NameIdentifier)?.Value;
        if (!int.TryParse(userIdStr, out var userId)) return Unauthorized();
        var isAdmin = User.IsInRole("Admin") || User.IsInRole("Manager");

        var affected = await _mutationService.ConfirmReminderAsync(id, userId, isAdmin, mode, ifMatch);
        if (affected == 0) return NotFound("事件不存在或确认已完成");
        return Ok(new { eventId = id, mode, confirmed = true });
    }

    // ============================================================
    //  P3-D: Outlook 同步健康仪表盘 + 手动 Delta 拉取
    // ============================================================

    /// <summary>
    /// P3-D: 获取 Outlook 同步健康仪表盘数据
    /// 路由：GET /api/calendar/outlook/health
    /// 若指定 mailboxId 则返回单个邮箱健康数据，否则返回当前用户可访问的所有邮箱列表
    /// </summary>
    /// <param name="mailboxId">可选：指定邮箱 ID（需有管理权限）</param>
    /// <param name="recentCount">返回最近 N 次同步历史，默认 10</param>
    /// <param name="historyDays">统计最近多少天的处理记录数，默认 7</param>
    [HttpGet("outlook/health")]
    [Permission("calendar.manage")]
    public async Task<ActionResult> GetOutlookHealth(
        [FromQuery] int? mailboxId = null,
        [FromQuery] int recentCount = 10,
        [FromQuery] int historyDays = 7)
    {
        var currentUserId = _currentUserAccessor.UserId;
        if (currentUserId == 0) return Unauthorized();

        if (recentCount < 1) recentCount = 1;
        if (recentCount > 100) recentCount = 100;
        if (historyDays < 1) historyDays = 1;
        if (historyDays > 365) historyDays = 365;

        if (mailboxId.HasValue)
        {
            // 指定 mailboxId 时校验权限
            if (!await HasMailboxAccess(mailboxId.Value))
                return Forbid();

            var dto = await _outlookHealthService.GetMailboxHealthAsync(mailboxId.Value, recentCount, historyDays);
            if (dto == null) return NotFound(new { message = "Mailbox 不存在" });
            return Ok(dto);
        }

        // 未指定 mailboxId 时返回当前用户可访问的所有邮箱健康数据
        var list = await _outlookHealthService.GetUserMailboxesHealthAsync(currentUserId, recentCount, historyDays);
        return Ok(new { items = list, total = list.Count });
    }

    /// <summary>
    /// P3-D: 手动触发指定邮箱的 Outlook Delta 增量同步
    /// 路由：POST /api/calendar/outlook/pull-delta?mailboxId=X
    /// 要求：Authorize + 用户有该邮箱管理权
    /// </summary>
    /// <param name="mailboxId">目标邮箱 ID（必填）</param>
    /// <param name="forceDeltaOnly">是否强制只走增量：true 时若 DeltaToken 失效会报错而非回退全量，默认 false</param>
    [HttpPost("outlook/pull-delta")]
    [Permission("calendar.manage")]
    [EnableRateLimiting("CalendarHeavyPolicy")]
    public async Task<ActionResult<OutlookSyncResultDto>> PullOutlookDelta(
        [FromQuery] int mailboxId,
        [FromQuery] bool forceDeltaOnly = false)
    {
        var currentUserId = _currentUserAccessor.UserId;
        if (currentUserId == 0) return Unauthorized();

        if (mailboxId <= 0)
            return BadRequest(new { message = "mailboxId 参数无效" });

        // 校验用户对该邮箱是否有管理权限
        if (!await HasMailboxAccess(mailboxId))
            return Forbid();

        var result = await _outlookSyncService.SyncByMailboxIdAsync(mailboxId, currentUserId, forceDeltaOnly);
        return Ok(result);
    }
}
