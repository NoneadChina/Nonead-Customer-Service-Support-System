using System.Text;
using System.Text.Json;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;
using NoneadCSS.Application.DTOs.Responses;
using NoneadCSS.Application.Exceptions;
using NoneadCSS.Application.Mappings;
using NoneadCSS.Domain;
using NoneadCSS.Domain.Entities;
using NoneadCSS.Domain.Enums;

namespace NoneadCSS.Application.Services;

/// <summary>
/// 时间追踪服务实现
/// </summary>
public class TimeEntryService : ITimeEntryService
{
    private readonly IUnitOfWork _unitOfWork;
    private readonly ICurrentUserAccessor _currentUserAccessor;
    private readonly IUserRoleResolver _userRoleResolver;
    private readonly IAuditService _auditService;
    private readonly ILogger<TimeEntryService> _logger;

    public TimeEntryService(
        IUnitOfWork unitOfWork,
        ICurrentUserAccessor currentUserAccessor,
        IUserRoleResolver userRoleResolver,
        IAuditService auditService,
        ILogger<TimeEntryService> logger)
    {
        _unitOfWork = unitOfWork;
        _currentUserAccessor = currentUserAccessor;
        _userRoleResolver = userRoleResolver;
        _auditService = auditService;
        _logger = logger;
    }

    private async Task<IEnumerable<int>?> ResolveAuthorizedUserIdsAsync(int currentUserId)
    {
        if (await _userRoleResolver.IsSystemRoleAsync(currentUserId, UserRoleType.SuperAdmin))
            return null; // 超管不限

        if (await _userRoleResolver.IsSystemRoleAsync(currentUserId, UserRoleType.AgentSupervisor))
        {
            var result = new HashSet<int> { currentUserId };
            var agentIds = await _userRoleResolver.GetUserIdsBySystemRoleTypeAsync(UserRoleType.Agent);
            var viewerIds = await _userRoleResolver.GetUserIdsBySystemRoleTypeAsync(UserRoleType.ReadOnly);
            foreach (var id in agentIds) result.Add(id);
            foreach (var id in viewerIds) result.Add(id);
            return result;
        }

        // 专员/只读 仅本人
        return new[] { currentUserId };
    }

    /// <summary>
    /// 解析权限过滤后的 userIds 数组：
    /// - 超管 → null（不过滤）
    /// - 主管未指定 UserId → 返回授权集合（查看团队）
    /// - 主管指定了授权范围内的 UserId → 返回单元素集合
    /// - 主管指定了范围外的 UserId → 安全降级为本人
    /// - 专员/只读 → 返回单元素集合（本人）
    /// </summary>
    private async Task<int[]?> ResolveUserIdsFilterAsync(int currentUserId, int? requestedUserId)
    {
        var authorizedUserIds = await ResolveAuthorizedUserIdsAsync(currentUserId);
        if (authorizedUserIds == null) return null; // 超管

        if (requestedUserId.HasValue && authorizedUserIds.Contains(requestedUserId.Value))
            return new[] { requestedUserId.Value };

        if (!requestedUserId.HasValue)
            return authorizedUserIds.ToArray();

        // 指定了不在授权范围内的 UserId → 安全降级
        return new[] { currentUserId };
    }

    // TT-021: 提取重复的审计快照序列化逻辑
    private static string SerializeEntrySnapshot(TimeEntry entry) =>
        JsonSerializer.Serialize(new
        {
            entry.Id, entry.EntryType, entry.Description, entry.ConversationId,
            entry.CustomerId, entry.UserId, entry.MailboxId, entry.StartTime,
            entry.EndTime, entry.DurationMinutes, entry.Billable, entry.UpdatedAt
        });

    private async Task LogAuditAsync(string action, TimeEntry entry, string? oldValues = null)
    {
        try
        {
            await _auditService.LogAsync(
                action: action,
                entityType: "TimeEntry",
                entityId: entry.Id,
                oldValues: oldValues,
                newValues: SerializeEntrySnapshot(entry));
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "审计日志记录失败");
        }
    }

    /// <summary>
    /// 校验是否为超级管理员；非超管抛 UnauthorizedAccessException（服务层纵深防御）
    /// </summary>
    private async Task RequireSuperAdminAsync()
    {
        var currentUserId = _currentUserAccessor.UserId;
        if (!await _userRoleResolver.IsSystemRoleAsync(currentUserId, UserRoleType.SuperAdmin))
            throw new UnauthorizedAccessException("仅超级管理员可执行此操作");
    }

    public async Task<(List<TimeEntryDto> Items, int Total)> GetListAsync(TimeEntryQuery query)
    {
        var currentUserId = _currentUserAccessor.UserId;
        var userIds = await ResolveUserIdsFilterAsync(currentUserId, query.UserId);

        // TT-020: pageSize 上限 200，防止客户端请求超大批次导致内存压力
        var pageSize = Math.Min(Math.Max(1, query.PageSize), 200);

        var items = await _unitOfWork.TimeEntries.GetListAsync(
            null, query.ConversationId, query.CustomerId,
            query.EntryType, query.Billable,
            query.StartDate, query.EndDate,
            query.Search, Math.Max(1, query.Page), pageSize, userIds);

        var total = await _unitOfWork.TimeEntries.CountAsync(
            null, query.ConversationId, query.CustomerId,
            query.EntryType, query.Billable,
            query.StartDate, query.EndDate,
            query.Search, userIds);

        return (items.Select(e => e.ToDto()).ToList(), total);
    }

    public async Task<TimeEntryDto?> GetByIdAsync(int id)
    {
        var entry = await _unitOfWork.TimeEntries.GetByIdAsync(id);
        if (entry == null) return null;

        // 权限校验
        var currentUserId = _currentUserAccessor.UserId;
        var authorizedUserIds = await ResolveAuthorizedUserIdsAsync(currentUserId);
        if (authorizedUserIds != null && !authorizedUserIds.Contains(entry.UserId))
            return null;

        return entry.ToDto();
    }

    public async Task<TimeEntryDto> CreateAsync(CreateTimeEntryRequest request)
    {
        var currentUserId = _currentUserAccessor.UserId;

        // TT-002: timer 类型且无 EndTime → 统一走 StartTimer 路径，检查已有活跃计时器
        var isTimerRunning = string.Equals(request.EntryType, "timer", StringComparison.OrdinalIgnoreCase)
                             && request.EndTime == null;
        if (isTimerRunning)
        {
            var existing = await _unitOfWork.TimeEntries.GetActiveTimerAsync(currentUserId);
            if (existing != null)
                throw new InvalidOperationException("已有运行中的计时器，请先停止当前计时器");
        }

        // 手动录入：如果有 DurationMinutes 但没有 EndTime，自动计算 EndTime
        DateTime? endTime = request.EndTime;
        int durationMinutes = request.DurationMinutes ?? CalculateDuration(request.StartTime, request.EndTime);

        if (string.Equals(request.EntryType, "manual", StringComparison.OrdinalIgnoreCase))
        {
            // 手动录入确保有 EndTime（避免显示为"进行中"）
            if (endTime == null)
                endTime = request.StartTime.AddMinutes(durationMinutes);
        }

        var entry = new TimeEntry
        {
            EntryType = request.EntryType,
            Description = request.Description,
            ConversationId = request.ConversationId,
            CustomerId = request.CustomerId,
            MailboxId = request.MailboxId,
            UserId = currentUserId,
            StartTime = request.StartTime,
            EndTime = endTime,
            DurationMinutes = durationMinutes,
            Billable = request.Billable
        };

        // TT-010: 事务包裹
        await using var transaction = await _unitOfWork.BeginTransactionAsync();
        try
        {
            await _unitOfWork.TimeEntries.AddAsync(entry);
            await _unitOfWork.SaveChangesAsync();
            await LogAuditAsync("time_entry.create", entry);
            await transaction.CommitAsync();
        }
        catch
        {
            await transaction.RollbackAsync();
            throw;
        }

        return entry.ToDto();
    }

    public async Task<TimeEntryDto?> UpdateAsync(int id, UpdateTimeEntryRequest request, string? ifMatch = null)
    {
        var entry = await _unitOfWork.TimeEntries.GetByIdTrackedAsync(id);
        if (entry == null) return null;

        // 权限校验
        var currentUserId = _currentUserAccessor.UserId;
        var authorizedUserIds = await ResolveAuthorizedUserIdsAsync(currentUserId);
        if (authorizedUserIds != null && !authorizedUserIds.Contains(entry.UserId))
            return null;

        // TT-003: 乐观锁校验（HTTP If-Match 头优先于 body.ifMatch）
        var effectiveIfMatch = !string.IsNullOrEmpty(ifMatch) ? ifMatch : request.IfMatch;
        ConcurrencyCheckHelper.Check(
            entry, entry.RowVersion, effectiveIfMatch,
            "TimeEntry", entry.Id,
            () => entry.ToDto(), _logger);

        var oldValues = SerializeEntrySnapshot(entry);

        if (request.Description != null)
            entry.Description = request.Description;
        entry.StartTime = request.StartTime;
        entry.Billable = request.Billable;

        // 计算 EndTime 与 DurationMinutes：优先使用请求传入的值，
        // 未传值时根据记录当前状态（是否已停止、类型）推断合理值，避免已停止记录因编辑而变回"进行中"
        int? reqDuration = request.DurationMinutes;
        DateTime? reqEndTime = request.EndTime;
        bool wasStopped = entry.EndTime != null; // 编辑前是否已停止

        int durationMinutes;
        DateTime? endTime;

        if (reqEndTime.HasValue)
        {
            // 1) 传入了 EndTime：直接用，Duration 用请求值或重新计算
            endTime = reqEndTime.Value;
            durationMinutes = reqDuration ?? CalculateDuration(request.StartTime, endTime);
        }
        else if (reqDuration.HasValue && reqDuration.Value > 0)
        {
            // 2) 没传 EndTime 但传了 Duration：从 StartTime 推算 EndTime
            durationMinutes = reqDuration.Value;
            endTime = request.StartTime.AddMinutes(durationMinutes);
        }
        else if (wasStopped)
        {
            // 3) 编辑前已停止（不论类型），但请求没传 EndTime 和 Duration：
            //    保持原有 Duration，用新 StartTime 重新计算 EndTime，避免编辑 StartTime 后意外变回"进行中"
            durationMinutes = entry.DurationMinutes > 0 ? entry.DurationMinutes : 0;
            endTime = durationMinutes > 0
                ? request.StartTime.AddMinutes(durationMinutes)
                : request.StartTime; // 兜底：零时长也给个 EndTime
        }
        else if (string.Equals(entry.EntryType, "manual", StringComparison.OrdinalIgnoreCase))
        {
            // 4) manual 类型且之前未停止（防御性分支）：也必须给 EndTime
            durationMinutes = 0;
            endTime = request.StartTime;
        }
        else
        {
            // 5) timer 类型且仍在运行中：保持 EndTime=null，Duration=0
            durationMinutes = 0;
            endTime = null;
        }

        entry.EndTime = endTime;
        entry.DurationMinutes = durationMinutes;

        // TT-010: 事务包裹
        await using var transaction = await _unitOfWork.BeginTransactionAsync();
        try
        {
            await _unitOfWork.TimeEntries.UpdateAsync(entry);
            await _unitOfWork.SaveChangesAsync();
            await LogAuditAsync("time_entry.update", entry, oldValues);
            await transaction.CommitAsync();
        }
        catch
        {
            await transaction.RollbackAsync();
            throw;
        }

        return entry.ToDto();
    }

    public async Task<bool> DeleteAsync(int id)
    {
        var entry = await _unitOfWork.TimeEntries.GetByIdTrackedAsync(id);
        if (entry == null) return false;

        // 权限校验
        var currentUserId = _currentUserAccessor.UserId;
        var authorizedUserIds = await ResolveAuthorizedUserIdsAsync(currentUserId);
        if (authorizedUserIds != null && !authorizedUserIds.Contains(entry.UserId))
            return false;

        var oldValues = SerializeEntrySnapshot(entry);

        // TT-010: 事务包裹
        await using var transaction = await _unitOfWork.BeginTransactionAsync();
        try
        {
            var result = await _unitOfWork.TimeEntries.DeleteAsync(id);
            await _unitOfWork.SaveChangesAsync();
            if (result)
                await LogAuditAsync("time_entry.delete", entry, oldValues);
            await transaction.CommitAsync();
            return result;
        }
        catch
        {
            await transaction.RollbackAsync();
            throw;
        }
    }

    public async Task<TimeEntryDto> StartTimerAsync(StartTimerRequest request)
    {
        var currentUserId = _currentUserAccessor.UserId;

        // TT-001: 事务包裹检查+创建，防御并发竞态
        await using var transaction = await _unitOfWork.BeginTransactionAsync();
        try
        {
            // 检查是否已有运行中的计时器（事务内）
            var existing = await _unitOfWork.TimeEntries.GetActiveTimerAsync(currentUserId);
            if (existing != null)
                throw new InvalidOperationException("已有运行中的计时器，请先停止当前计时器");

            var entry = new TimeEntry
            {
                EntryType = "timer",
                Description = request.Description,
                ConversationId = request.ConversationId,
                CustomerId = request.CustomerId,
                MailboxId = request.MailboxId,
                UserId = currentUserId,
                StartTime = DateTime.UtcNow,
                EndTime = null,
                DurationMinutes = 0,
                Billable = request.Billable
            };

            await _unitOfWork.TimeEntries.AddAsync(entry);
            await _unitOfWork.SaveChangesAsync();
            await LogAuditAsync("time_entry.timer_start", entry);
            await transaction.CommitAsync();

            return entry.ToDto();
        }
        catch (InvalidOperationException)
        {
            await transaction.RollbackAsync();
            throw;
        }
        catch (Microsoft.EntityFrameworkCore.DbUpdateException ex)
            when (ex.InnerException?.Message.Contains("UNIQUE constraint", StringComparison.OrdinalIgnoreCase) == true
                  || ex.InnerException?.Message.Contains("unique", StringComparison.OrdinalIgnoreCase) == true)
        {
            // 数据库唯一索引兜底：并发竞态时两个请求同时通过检查，唯一索引阻止第二个
            await transaction.RollbackAsync();
            throw new InvalidOperationException("已有运行中的计时器，请先停止当前计时器", ex);
        }
        catch
        {
            await transaction.RollbackAsync();
            throw;
        }
    }

    public async Task<TimeEntryDto?> StopTimerAsync(int id, string? ifMatch = null)
    {
        var entry = await _unitOfWork.TimeEntries.GetByIdTrackedAsync(id);
        if (entry == null || entry.EndTime != null) return null;

        // 权限校验
        var currentUserId = _currentUserAccessor.UserId;
        var authorizedUserIds = await ResolveAuthorizedUserIdsAsync(currentUserId);
        if (authorizedUserIds != null && !authorizedUserIds.Contains(entry.UserId))
            return null;

        // TT-004: 乐观锁校验
        ConcurrencyCheckHelper.Check(
            entry, entry.RowVersion, ifMatch,
            "TimeEntry", entry.Id,
            () => entry.ToDto(), _logger);

        var oldValues = SerializeEntrySnapshot(entry);

        var now = DateTime.UtcNow;
        entry.EndTime = now;
        entry.DurationMinutes = CalculateDuration(entry.StartTime, now);
        entry.UpdatedAt = now;

        // TT-010: 事务包裹
        await using var transaction = await _unitOfWork.BeginTransactionAsync();
        try
        {
            await _unitOfWork.TimeEntries.UpdateAsync(entry);
            await _unitOfWork.SaveChangesAsync();
            await LogAuditAsync("time_entry.timer_stop", entry, oldValues);
            await transaction.CommitAsync();
        }
        catch
        {
            await transaction.RollbackAsync();
            throw;
        }

        return entry.ToDto();
    }

    public async Task<TimeEntryDto?> GetActiveTimerAsync()
    {
        var currentUserId = _currentUserAccessor.UserId;
        var entry = await _unitOfWork.TimeEntries.GetActiveTimerAsync(currentUserId);
        return entry?.ToDto();
    }

    public async Task<TimeTrackingOverviewResponse> GetOverviewAsync()
    {
        var currentUserId = _currentUserAccessor.UserId;
        // TT-005: 主管查看团队数据，不再收敛为本人
        var userIds = await ResolveUserIdsFilterAsync(currentUserId, null);

        var utcNow = DateTime.UtcNow;
        var todayStart = utcNow.Date;
        var todayEnd = todayStart.AddDays(1);
        // 周一起始（中国业务习惯）：Sunday=0 → 偏移 6 天；Monday=1 → 偏移 0 天
        var dayOfWeek = (int)utcNow.DayOfWeek;
        var weekStart = todayStart.AddDays(-(dayOfWeek == 0 ? 6 : dayOfWeek - 1));
        var weekEnd = weekStart.AddDays(7);
        var monthStart = new DateTime(utcNow.Year, utcNow.Month, 1);
        var monthEnd = monthStart.AddMonths(1);

        var activeTimer = await _unitOfWork.TimeEntries.GetActiveTimerAsync(currentUserId);

        // TT-022: 7 次独立统计查询合并为 1 次条件聚合
        var stats = await _unitOfWork.TimeEntries.GetOverviewStatsAsync(
            todayStart, todayEnd, weekStart, weekEnd, monthStart, monthEnd, userIds);

        return new TimeTrackingOverviewResponse
        {
            TodayMinutes = stats.TodayMinutes,
            WeekMinutes = stats.WeekMinutes,
            MonthMinutes = stats.MonthMinutes,
            TotalMinutes = stats.TotalMinutes,
            ActiveTimerId = activeTimer?.Id,
            ActiveTimerDescription = activeTimer?.Description,
            ActiveTimerStartTime = activeTimer != null ? FormatIsoUtc(activeTimer.StartTime) : null,
            TodayCount = stats.TodayCount,
            WeekCount = stats.WeekCount,
            BillableMinutes = stats.BillableMinutes
        };
    }

    public async Task<TimeTrackingReportResponse> GetReportAsync(DateTime? startDate, DateTime? endDate)
    {
        var utcNow = DateTime.UtcNow;
        var start = startDate ?? utcNow.AddDays(-30);
        var end = endDate ?? utcNow;

        // TT-023: 日期范围校验——不能超过 366 天，防止超长范围查询拖慢系统
        if (end < start)
            throw new ArgumentException("结束日期不能早于开始日期");
        if (end - start > TimeSpan.FromDays(366))
            throw new ArgumentException("报表日期范围不能超过一年");

        // TT-005: 主管查看团队报表数据
        var currentUserId = _currentUserAccessor.UserId;
        var userIds = await ResolveUserIdsFilterAsync(currentUserId, null);

        var byUser = await _unitOfWork.TimeEntries.GetByUserAsync(start, end, null, userIds);
        var byConversation = await _unitOfWork.TimeEntries.GetByConversationAsync(start, end, null, userIds);
        var byDate = await _unitOfWork.TimeEntries.GetByDateAsync(start, end, null, userIds);

        return new TimeTrackingReportResponse
        {
            ByUser = byUser.Select(r => new TimeTrackingReportItem
            {
                Label = r.Label, TotalMinutes = r.TotalMinutes, Count = r.Count, BillableMinutes = r.BillableMinutes
            }).ToList(),
            ByConversation = byConversation.Select(r => new TimeTrackingReportItem
            {
                Label = r.Label, TotalMinutes = r.TotalMinutes, Count = r.Count, BillableMinutes = r.BillableMinutes
            }).ToList(),
            ByDate = byDate.Select(r => new TimeTrackingReportItem
            {
                Label = r.Label, TotalMinutes = r.TotalMinutes, Count = r.Count, BillableMinutes = r.BillableMinutes
            }).ToList(),
            TotalMinutes = byDate.Sum(r => r.TotalMinutes),
            TotalCount = byDate.Sum(r => r.Count)
        };
    }

    public async Task<byte[]> ExportAsync(TimeEntryQuery query)
    {
        var currentUserId = _currentUserAccessor.UserId;
        var userIds = await ResolveUserIdsFilterAsync(currentUserId, query.UserId);

        // TT-008: 上限 10000 防止大数据量 OOM；如需更大批量应改用流式导出
        var items = await _unitOfWork.TimeEntries.GetListAsync(
            null, query.ConversationId, query.CustomerId,
            query.EntryType, query.Billable,
            query.StartDate, query.EndDate,
            query.Search, 1, 10000, userIds);

        var sb = new StringBuilder();
        sb.AppendLine("ID,类型,描述,工单号,客户,用户,开始时间,结束时间,时长(分钟),可计费");
        foreach (var e in items)
        {
            sb.AppendLine(string.Join(",",
                e.Id,
                e.EntryType,
                EscapeCsv(e.Description),
                e.Conversation?.Number.ToString() ?? "",
                EscapeCsv(e.Customer?.GetFullName()),
                EscapeCsv(e.User?.GetFullName()),
                FormatIsoUtc(e.StartTime),
                e.EndTime.HasValue ? FormatIsoUtc(e.EndTime.Value) : "",
                e.DurationMinutes,
                e.Billable ? "是" : "否"));
        }
        // 添加 UTF-8 BOM 前导，避免 Excel 打开时中文乱码
        return Encoding.UTF8.GetPreamble()
            .Concat(Encoding.UTF8.GetBytes(sb.ToString())).ToArray();
    }

    public async Task<(List<TimeEntryDto> Items, int Total)> GetTrashAsync(int page, int pageSize, string? keyword)
    {
        // TT-006: 服务层纵深防御
        await RequireSuperAdminAsync();

        var items = await _unitOfWork.TimeEntries.GetTrashAsync(page, pageSize, keyword);
        // TT-012: CountTrashAsync 传递 keyword，保持与 GetTrashAsync 过滤一致
        var total = await _unitOfWork.TimeEntries.CountTrashAsync(keyword);
        return (items.Select(e => e.ToDto()).ToList(), total);
    }

    public async Task<bool> RestoreAsync(int id)
    {
        // TT-006: 服务层纵深防御
        await RequireSuperAdminAsync();

        await using var transaction = await _unitOfWork.BeginTransactionAsync();
        try
        {
            var result = await _unitOfWork.TimeEntries.RestoreAsync(id);
            await _unitOfWork.SaveChangesAsync();
            if (result)
            {
                var entry = await _unitOfWork.TimeEntries.GetByIdAsync(id);
                if (entry != null)
                    await LogAuditAsync("time_entry.restore", entry);
            }
            await transaction.CommitAsync();
            return result;
        }
        catch
        {
            await transaction.RollbackAsync();
            throw;
        }
    }

    public async Task<bool> HardDeleteAsync(int id)
    {
        // TT-006: 服务层纵深防御
        await RequireSuperAdminAsync();

        await using var transaction = await _unitOfWork.BeginTransactionAsync();
        try
        {
            var result = await _unitOfWork.TimeEntries.HardDeleteAsync(id);
            await _unitOfWork.SaveChangesAsync();
            await transaction.CommitAsync();
            return result;
        }
        catch
        {
            await transaction.RollbackAsync();
            throw;
        }
    }

    public async Task<int> ClearTrashAsync()
    {
        // TT-006: 服务层纵深防御
        await RequireSuperAdminAsync();

        await using var transaction = await _unitOfWork.BeginTransactionAsync();
        try
        {
            var count = await _unitOfWork.TimeEntries.ClearTrashAsync();
            await transaction.CommitAsync();
            return count;
        }
        catch
        {
            await transaction.RollbackAsync();
            throw;
        }
    }

    private static int CalculateDuration(DateTime start, DateTime? end)
    {
        if (!end.HasValue) return 0;
        // TT-011: 防御负时长（EndTime < StartTime 时返回 0 而非负数）
        return Math.Max(0, (int)Math.Round((end.Value - start).TotalMinutes));
    }

    private static string FormatIsoUtc(DateTime dt)
    {
        return DateTime.SpecifyKind(dt, DateTimeKind.Utc).ToString("o");
    }

    /// <summary>
    /// CSV 字段转义：处理逗号/引号/换行，并防御 CSV 公式注入（=/+/-/@ 开头的值前加单引号）
    /// </summary>
    private static string EscapeCsv(string? value)
    {
        if (string.IsNullOrEmpty(value)) return "";
        var v = value;
        // TT-007: 防御 CSV 公式注入——以 =/+/-/@ 开头的内容在 Excel 中可能被解释为公式
        if (v.Length > 0 && (v[0] == '=' || v[0] == '+' || v[0] == '-' || v[0] == '@'))
            v = "'" + v;
        if (v.Contains(',') || v.Contains('"') || v.Contains('\n'))
            return $"\"{v.Replace("\"", "\"\"")}\"";
        return v;
    }
}
