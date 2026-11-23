#nullable enable
using System.Globalization;
using Microsoft.Extensions.Logging;
using NoneadCSS.Application.DTOs.Responses;
using NoneadCSS.Application.Mappings;
using NoneadCSS.Domain;

namespace NoneadCSS.Application.Services.Calendar;

public class CalendarQueryService : ICalendarQueryService
{
    private readonly IUnitOfWork _unitOfWork;
    private readonly ICurrentUserAccessor _currentUserAccessor;
    private readonly CalendarAuthorizationService _auth;
    private readonly IRecurrenceExpansionService _recurrence;
    private readonly ILogger<CalendarQueryService>? _logger;
    private readonly ICalendarQueryCache? _cache;
    private readonly ILocalizationService? _localization;

    public CalendarQueryService(
        IUnitOfWork unitOfWork,
        ICurrentUserAccessor currentUserAccessor,
        CalendarAuthorizationService auth,
        IRecurrenceExpansionService recurrence,
        ILogger<CalendarQueryService>? logger = null,
        ICalendarQueryCache? cache = null,
        ILocalizationService? localization = null)
    {
        ArgumentNullException.ThrowIfNull(unitOfWork);
        ArgumentNullException.ThrowIfNull(currentUserAccessor);
        ArgumentNullException.ThrowIfNull(auth);
        ArgumentNullException.ThrowIfNull(recurrence);
        _unitOfWork = unitOfWork;
        _currentUserAccessor = currentUserAccessor;
        _auth = auth;
        _recurrence = recurrence;
        _logger = logger;
        _cache = cache;
        _localization = localization;
    }

    public async Task<(List<CalendarEventDto> Items, int Total)> GetListAsync(CalendarEventQuery query, CancellationToken ct = default)
    {
        var currentUserId = _currentUserAccessor.UserId;
        query.UserIds = await _auth.ResolveAuthorizedUserIdsAsync(currentUserId).ConfigureAwait(false);

        if (_cache != null)
        {
            return await _cache.GetListAsync(currentUserId, query, async () => await GetListCoreAsync(query, ct).ConfigureAwait(false), ct).ConfigureAwait(false);
        }

        return await GetListCoreAsync(query, ct).ConfigureAwait(false);
    }

    private async Task<(List<CalendarEventDto> Items, int Total)> GetListCoreAsync(CalendarEventQuery query, CancellationToken ct = default)
    {
        var currentUserId = _currentUserAccessor.UserId;

        if (query.ExpandRecurring)
        {
            // Expand recurring requires full window fetch; size capped to prevent OOM.
            // If no date window set, enforce a default 90-day window (today-30d to today+60d) as safety guard.
            DateTime effectiveStart;
            DateTime effectiveEnd;
            if (query.StartDate.HasValue && query.EndDate.HasValue)
            {
                effectiveStart = query.StartDate.Value;
                effectiveEnd = query.EndDate.Value;
            }
            else
            {
                var today = DateTime.UtcNow.Date;
                effectiveStart = today.AddDays(-30);
                effectiveEnd = today.AddDays(60);
                query.StartDate = effectiveStart;
                query.EndDate = effectiveEnd;
                _logger?.LogWarning("Calendar GetListAsync ExpandRecurring 未设置日期窗口，自动限制为 90 天窗口 ({Start:yyyy-MM-dd} ~ {End:yyyy-MM-dd})", effectiveStart, effectiveEnd);
            }

            const int SafePageSizeCap = 2000;
            var allItems = await _unitOfWork.CalendarEvents.GetListAsync(
                query.StartDate, query.EndDate,
                query.MailboxId, query.UserId, query.UserIds,
                query.EventType, query.Status, query.Keyword,
                1, SafePageSizeCap, includeNavigation: false).ConfigureAwait(false);

            if (allItems.Count >= SafePageSizeCap)
            {
                _logger?.LogWarning("Calendar GetListAsync ExpandRecurring 命中上限 {Cap} 条，数据已被截断，total 不精确", SafePageSizeCap);
            }

            var allDtos = allItems.Select(e => e.ToDto()).ToList();
            var dtos = _recurrence.ExpandSeries(allDtos, effectiveStart, effectiveEnd);
            // total 为展开后的条数；若命中上限则标记为截断（前端需感知 total 不精确）
            var total = allItems.Count >= SafePageSizeCap ? -1 : dtos.Count;
            dtos = ApplyDtoSort(dtos, query.SortBy, query.SortDir)
                .Skip((query.Page - 1) * query.PageSize)
                .Take(query.PageSize)
                .ToList();

            // 裁剪敏感字段：非创建者且非超管不可见 ReminderEmails
            var uid = _currentUserAccessor.UserId;
            var admin = _currentUserAccessor.IsSuperAdmin;
            dtos = dtos.Select(d => d.SanitizeForUser(uid, admin)).ToList();

            await HydrateTaskOwnerNamesAsync(dtos, ct).ConfigureAwait(false);

            return (dtos, total);
        }

        var items = await _unitOfWork.CalendarEvents.GetListAsync(
            query.StartDate, query.EndDate,
            query.MailboxId, query.UserId, query.UserIds,
            query.EventType, query.Status, query.Keyword,
            query.Page, query.PageSize,
            excludeRecurrenceInstances: !query.ExpandRecurring,
            sortBy: query.SortBy,
            sortDir: query.SortDir).ConfigureAwait(false);

        var totalNonExpand = await _unitOfWork.CalendarEvents.CountAsync(
            query.StartDate, query.EndDate,
            query.MailboxId, query.UserId, query.UserIds,
            query.EventType, query.Status, query.Keyword,
            excludeRecurrenceInstances: !query.ExpandRecurring).ConfigureAwait(false);

        var dtosNonExpand = items.Select(e => e.ToDto().SanitizeForUser(_currentUserAccessor.UserId, _currentUserAccessor.IsSuperAdmin)).ToList();

        await HydrateTaskOwnerNamesAsync(dtosNonExpand, ct).ConfigureAwait(false);

        return (dtosNonExpand, totalNonExpand);
    }

    /// <summary>
    /// 对展开后的 DTO 列表应用排序（ExpandRecurring=true 分支使用，因为数据已全部加载到内存）。
    /// sortBy 为空时保持默认行为：按 StartAt 倒序。
    /// </summary>
    private static List<CalendarEventDto> ApplyDtoSort(List<CalendarEventDto> source, string? sortBy, string? sortDir)
    {
        if (source.Count == 0) return source;

        var dir = string.IsNullOrWhiteSpace(sortDir) ? "desc" : sortDir.Trim().ToLowerInvariant();
        bool asc = dir == "asc";

        IEnumerable<CalendarEventDto> sorted = (sortBy?.Trim().ToLowerInvariant()) switch
        {
            "priority" => asc
                ? source.OrderBy(e => e.Priority ?? 0).ThenByDescending(e => ParseDtoDate(e.StartAt))
                : source.OrderByDescending(e => e.Priority ?? 0).ThenByDescending(e => ParseDtoDate(e.StartAt)),
            "progress" => asc
                ? source.OrderBy(e => e.Progress ?? 0).ThenByDescending(e => ParseDtoDate(e.StartAt))
                : source.OrderByDescending(e => e.Progress ?? 0).ThenByDescending(e => ParseDtoDate(e.StartAt)),
            "createdat" => asc
                ? source.OrderBy(e => ParseDtoDate(e.CreatedAt)).ThenByDescending(e => ParseDtoDate(e.StartAt))
                : source.OrderByDescending(e => ParseDtoDate(e.CreatedAt)).ThenByDescending(e => ParseDtoDate(e.StartAt)),
            _ => asc
                ? source.OrderBy(e => ParseDtoDate(e.StartAt))
                : source.OrderByDescending(e => ParseDtoDate(e.StartAt))
        };

        return sorted.ToList();
    }

    private static DateTime ParseDtoDate(string s)
    {
        return DateTime.TryParse(s, CultureInfo.InvariantCulture,
            DateTimeStyles.RoundtripKind, out var d)
            ? d
            : DateTime.MinValue;
    }

    /// <summary>
    /// 批量回填 DTO 的 TaskOwnerName（任务负责人姓名）和 CreatedByName（创建者姓名）。
    /// ExpandRecurring 分支使用 includeNavigation: false 以提升性能，此时 ToDto() 中的 CreatedByUser 为 null，
    /// CreatedByName 也为 null，需要在此批量回填。
    /// 对未命中用户字典的 DTO，对应字段保持为 null（由前端兜底显示）。
    /// </summary>
    private async Task HydrateTaskOwnerNamesAsync(IReadOnlyList<CalendarEventDto> dtos, CancellationToken ct = default)
    {
        if (dtos == null || dtos.Count == 0) return;

        // 收集需要查询的用户 ID：任务负责人 + 创建者
        var userIds = new HashSet<int>();
        foreach (var d in dtos)
        {
            if (d.TaskOwnerId.HasValue && d.TaskOwnerId.Value > 0 && string.IsNullOrEmpty(d.TaskOwnerName))
                userIds.Add(d.TaskOwnerId.Value);
            if (d.CreatedBy > 0 && string.IsNullOrEmpty(d.CreatedByName))
                userIds.Add(d.CreatedBy);
        }

        if (userIds.Count == 0) return;

        var users = await _unitOfWork.Users.GetByIdsAsync(userIds.ToList()).ConfigureAwait(false);
        if (users == null || users.Count == 0) return;

        var nameById = users.ToDictionary(u => u.Id, u => u.GetFullName(), comparer: null);

        foreach (var dto in dtos)
        {
            if (dto.TaskOwnerId.HasValue
                && dto.TaskOwnerId.Value > 0
                && string.IsNullOrEmpty(dto.TaskOwnerName)
                && nameById.TryGetValue(dto.TaskOwnerId.Value, out var ownerName))
            {
                dto.TaskOwnerName = ownerName;
            }
            if (dto.CreatedBy > 0
                && string.IsNullOrEmpty(dto.CreatedByName)
                && nameById.TryGetValue(dto.CreatedBy, out var creatorName))
            {
                dto.CreatedByName = creatorName;
            }
        }
    }

    public async Task<CalendarEventDto?> GetByIdAsync(int id, CancellationToken ct = default)
    {
        var evt = await _unitOfWork.CalendarEvents.GetByIdAsync(id).ConfigureAwait(false);
        if (evt == null || evt.DeletedAt != null) return null;

        if (!await _currentUserAccessor.CanAccessMailboxAsync(evt.MailboxId).ConfigureAwait(false))
            return null;

        var currentUserId = _currentUserAccessor.UserId;
        CalendarEventDto? dto = null;
        // SuperAdmin 可访问任何事件，无需走 authorizedUserIds 范围检查
        // （修复：原实现漏掉 SuperAdmin 短路，导致超管更新他人事件后 GetByIdAsync 返回 null → 404）
        if (_currentUserAccessor.IsSuperAdmin) dto = evt.ToDto();
        else if (evt.CreatedBy == currentUserId) dto = evt.ToDto();
        else if (evt.UserId == currentUserId) dto = evt.ToDto().SanitizeForUser(currentUserId, _currentUserAccessor.IsSuperAdmin);
        else
        {
            var authorizedUserIds = await _auth.ResolveAuthorizedUserIdsAsync(currentUserId).ConfigureAwait(false);
            if (authorizedUserIds != null && evt.UserId.HasValue && authorizedUserIds.Contains(evt.UserId.Value))
                dto = evt.ToDto().SanitizeForUser(currentUserId, _currentUserAccessor.IsSuperAdmin);
        }

        if (dto != null) await HydrateTaskOwnerNamesAsync(new[] { dto }, ct).ConfigureAwait(false);
        return dto;
    }

    public async Task<Domain.Entities.CalendarEvent?> GetByIdTrackedEntityAsync(int id, CancellationToken ct = default)
        => await GetByIdTrackedEntityAsync(id, false, ct).ConfigureAwait(false);

    /// <summary>
    /// 按ID获取带EF跟踪的实体，用于后续写操作。
    /// 注意：此方法只过滤软删除和Mailbox访问权限；
    /// 用户级所有权/跨用户可见性由上层 Facade 的 EnsureCanAccess/EnsureCanModify 判定
    /// （以便区分"实体不存在"和"无权操作"两种语义）。
    /// </summary>
    public async Task<Domain.Entities.CalendarEvent?> GetByIdTrackedEntityAsync(int id, bool includeDeleted, CancellationToken ct = default)
    {
        var evt = includeDeleted
            ? await _unitOfWork.CalendarEvents.GetByIdTrackedAsync(id, true).ConfigureAwait(false)
            : await _unitOfWork.CalendarEvents.GetByIdTrackedAsync(id).ConfigureAwait(false);
        if (evt == null) return null;
        if (!includeDeleted && evt.DeletedAt != null) return null;

        if (!await _currentUserAccessor.CanAccessMailboxAsync(evt.MailboxId).ConfigureAwait(false))
            return null;

        return evt;
    }

    public async Task<CalendarOverviewResponse> GetOverviewAsync(CancellationToken ct = default)
    {
        var currentUserId = _currentUserAccessor.UserId;

        if (_cache != null)
        {
            // 缓存内保存的是语言中立的标签目录，出口再按当前语言本地化，避免语言串台。
            var cached = await _cache.GetOverviewAsync(currentUserId, async () => await GetOverviewCoreAsync(ct).ConfigureAwait(false), ct).ConfigureAwait(false);
            return WithLocalizedOptions(cached);
        }

        return WithLocalizedOptions(await GetOverviewCoreAsync(ct).ConfigureAwait(false));
    }

    private async Task<CalendarOverviewResponse> GetOverviewCoreAsync(CancellationToken ct = default)
    {
        var currentUserId = _currentUserAccessor.UserId;
        var authorizedUserIds = await _auth.ResolveAuthorizedUserIdsAsync(currentUserId).ConfigureAwait(false);

        var now = DateTime.UtcNow;

        var (stats, upcomingEvents) = await _unitOfWork.CalendarEvents.GetOverviewAggregateAsync(
            authorizedUserIds, now, 10).ConfigureAwait(false);

        var upcomingDtos = upcomingEvents.Select(e => e.ToDto()).ToList();
        await HydrateTaskOwnerNamesAsync(upcomingDtos, ct).ConfigureAwait(false);

        return new CalendarOverviewResponse
        {
            Stats = new Dictionary<string, int>
            {
                ["total"] = stats.Total,
                ["pending"] = stats.Pending,
                ["completed"] = stats.Completed,
                ["cancelled"] = stats.Cancelled,
                ["upcoming"] = stats.Upcoming,
                ["overdue"] = stats.Overdue
            },
            UpcomingEvents = upcomingDtos,
            EventTypeOptions = BuildNeutralEventTypeOptions(),
            StatusOptions = BuildNeutralStatusOptions()
        };
    }

    /// <summary>
    /// 事件类型目录（语言中立：值为 zh-Hans 原文，仅作回退源）。
    /// </summary>
    private static Dictionary<string, string> BuildNeutralEventTypeOptions() => new()
    {
        ["due_date"] = "截止日期",
        ["task"] = "计划任务",
        ["meeting"] = "会议",
        ["reminder"] = "提醒"
    };

    /// <summary>
    /// 事件状态目录（语言中立：值为 zh-Hans 原文，仅作回退源）。
    /// </summary>
    private static Dictionary<string, string> BuildNeutralStatusOptions() => new()
    {
        ["pending"] = "待处理",
        ["completed"] = "已完成",
        ["cancelled"] = "已取消"
    };

    private Dictionary<string, string> GetLocalizedEventTypeOptions() =>
        LocalizeOptions(BuildNeutralEventTypeOptions(), "CalendarEventType_");

    private Dictionary<string, string> GetLocalizedStatusOptions() =>
        LocalizeOptions(BuildNeutralStatusOptions(), "CalendarStatus_");

    /// <summary>
    /// 复制一份响应并替换为按当前语言本地化后的标签目录，保证被缓存的原对象保持中立。
    /// </summary>
    private CalendarOverviewResponse WithLocalizedOptions(CalendarOverviewResponse response) => new()
    {
        Stats = response.Stats,
        UpcomingEvents = response.UpcomingEvents,
        EventTypeOptions = GetLocalizedEventTypeOptions(),
        StatusOptions = GetLocalizedStatusOptions()
    };

    private Dictionary<string, string> LocalizeOptions(Dictionary<string, string> neutral, string keyPrefix)
    {
        var result = new Dictionary<string, string>(neutral.Count, StringComparer.Ordinal);
        foreach (var (key, zhFallback) in neutral)
        {
            result[key] = Translate(keyPrefix + key, zhFallback);
        }
        return result;
    }

    /// <summary>
    /// 取词条文本：本地化服务在词条缺失时会返回键名本身，此时回退给定中文原文，避免把键名吐给前端。
    /// </summary>
    private string Translate(string key, string zhFallback, params object[] args)
    {
        if (_localization is null) return zhFallback;
        var value = _localization[key];
        if (string.IsNullOrWhiteSpace(value) || string.Equals(value, key, StringComparison.Ordinal))
        {
            return args.Length > 0 ? string.Format(zhFallback, args) : zhFallback;
        }
        return args.Length > 0 ? string.Format(value, args) : value;
    }

    public async Task<(List<CalendarEventDto> Items, int Total)> GetTrashAsync(int page, int pageSize, string? keyword, CancellationToken ct = default)
    {
        // SuperAdmin 可查看全部垃圾箱；普通用户仅能看到自己创建(CreatedBy)或归属(UserId)的已删除事件
        int? filterUserId = _currentUserAccessor.IsSuperAdmin ? null : _currentUserAccessor.UserId;

        var items = await _unitOfWork.CalendarEvents.GetTrashAsync(page, pageSize, keyword, filterUserId).ConfigureAwait(false);
        var total = await _unitOfWork.CalendarEvents.CountTrashAsync(keyword, filterUserId).ConfigureAwait(false);
        var dtos = items.Select(e => e.ToDto()).ToList();
        await HydrateTaskOwnerNamesAsync(dtos, ct).ConfigureAwait(false);
        return (dtos, total);
    }

    public async Task<int> GetTrashCountAsync(CancellationToken ct = default)
    {
        int? filterUserId = _currentUserAccessor.IsSuperAdmin ? null : _currentUserAccessor.UserId;
        return await _unitOfWork.CalendarEvents.CountTrashAsync(null, filterUserId).ConfigureAwait(false);
    }
}
