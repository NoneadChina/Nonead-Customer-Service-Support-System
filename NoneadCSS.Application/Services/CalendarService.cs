#nullable enable
using Microsoft.Extensions.Logging;
using NoneadCSS.Application.DTOs.Requests;
using NoneadCSS.Application.DTOs.Responses;
using NoneadCSS.Application.Services.Calendar;

namespace NoneadCSS.Application.Services;

/// <summary>
/// P3 E#1 日历服务降级为 Facade（零侵入迁移）。
/// <para>保留 <see cref="ICalendarService"/> 接口不变（<see cref="CalendarController"/> 等旧消费者无感），
/// 但所有业务实现已经搬迁到 4 个独立子服务，未来可逐步把 DI 注入替换为具体子服务，
/// 待全部调用点迁移完成后可删除本类。</para>
/// <list type="bullet">
/// <item>读请求 → <see cref="ICalendarQueryService"/></item>
/// <item>写请求 → <see cref="ICalendarMutationService"/></item>
/// <item>冲突检测 → <see cref="ICalendarConflictService"/>（内部使用，Controller 侧不用暴露）</item>
/// <item>提醒调度 → <see cref="ICalendarReminderService"/></item>
/// </list>
/// </summary>
public class CalendarService : ICalendarService
{
    private readonly ICalendarQueryService _query;
    private readonly ICalendarMutationService _mutation;
    private readonly ICalendarReminderService _reminder;
    private readonly ILogger<CalendarService>? _logger;

    public CalendarService(
        ICalendarQueryService query,
        ICalendarMutationService mutation,
        ICalendarReminderService reminder,
        ILogger<CalendarService>? logger = null)
    {
        ArgumentNullException.ThrowIfNull(query);
        ArgumentNullException.ThrowIfNull(mutation);
        ArgumentNullException.ThrowIfNull(reminder);
        _query = query;
        _mutation = mutation;
        _reminder = reminder;
        _logger = logger;
    }

    // ================= 读请求：转发到 QueryService =================
    public Task<(List<CalendarEventDto> Items, int Total)> GetListAsync(CalendarEventQuery query)
        => _query.GetListAsync(query);

    public Task<CalendarEventDto?> GetByIdAsync(int id)
        => _query.GetByIdAsync(id);

    public Task<CalendarOverviewResponse> GetOverviewAsync()
        => _query.GetOverviewAsync();

    public Task<(List<CalendarEventDto> Items, int Total)> GetTrashAsync(int page, int pageSize, string? keyword)
        => _query.GetTrashAsync(page, pageSize, keyword);

    public Task<int> GetTrashCountAsync()
        => _query.GetTrashCountAsync();

    // ================= 写请求：转发到 MutationService =================

    /// <summary>
    /// 归一化 EventType：白名单内 8 种类型保留大小写原样通过，未知类型自动降级为 "other"
    /// 并打 Warning 日志（包含原始值），绝不抛 ArgumentException，避免客户端 SignalR
    /// 重试循环把 WebSocket 打成 "未完成关闭握手" 级联断开。
    /// <para>在 Facade 入口统一归一化 + 打 Warning（使用 ILogger&lt;CalendarService&gt;，
    /// 与测试 Verify 的 Logger 类型一致），MutationService 层再做一次静默兜底归一化。</para>
    /// </summary>
    internal static string NormalizeEventType(string? eventType, ILogger? logger)
    {
        var allowed = new HashSet<string>(StringComparer.OrdinalIgnoreCase)
            { "due_date", "task", "meeting", "reminder", "personal", "travel", "work", "other" };

        if (string.IsNullOrEmpty(eventType))
        {
            return "task";
        }

        if (allowed.Contains(eventType))
        {
            return eventType;
        }

        logger?.LogWarning(
            "NormalizeEventType：未知事件类型 '{OriginalType}' 已降级为 'other'（合法值：{AllowedTypes}）",
            eventType,
            string.Join(", ", allowed));

        return "other";
    }

    private void ApplyNormalizedEventType(CreateCalendarEventRequest request)
    {
        request.EventType = NormalizeEventType(request.EventType, _logger);
    }

    public Task<CalendarEventDto> CreateAsync(CreateCalendarEventRequest request)
    {
        // Facade 入口：EventType 归一化 + 写 Warning（避免 MutationService 抛 ArgumentException
        // 被客户端 SignalR 重试放大成 WebSocket 连锁断开）。
        ApplyNormalizedEventType(request);
        return _mutation.CreateAsync(request);
    }

    public Task<CalendarEventDto?> UpdateAsync(int id, UpdateCalendarEventRequest request)
    {
        ApplyNormalizedEventType(request);
        return _mutation.UpdateAsync(id, request);
    }

    public Task<bool> DeleteAsync(int id, string deleteMode = "single", string? ifMatch = null)
        => _mutation.DeleteAsync(id, deleteMode, ifMatch);

    public Task<bool> UpdateStatusAsync(int id, string status, string? ifMatch = null)
        => _mutation.UpdateStatusAsync(id, status, ifMatch);

    public Task<bool> RestoreAsync(int id, string? ifMatch = null)
        => _mutation.RestoreAsync(id, ifMatch);

    public async Task<bool> HardDeleteAsync(int id, string? ifMatch = null)
        // Facade 为了兼容 ICalendarService.HardDeleteAsync(id) 签名（不传已查实体），这里传 null，
        // MutationService 内部会在 existingEntity==null 时自己再查一次。
        => (await _mutation.HardDeleteAsync(id, existingEntity: null, ifMatch: ifMatch).ConfigureAwait(false)) > 0;

    public Task<int> ClearTrashAsync()
        => _mutation.ClearTrashAsync();

    public Task<int> PurgeTrashAsync(int retentionDays = 30)
        => _mutation.PurgeTrashAsync(retentionDays);

    public Task<CalendarBatchOperationResponse> BatchOperationAsync(CalendarBatchOperationRequest request)
        => _mutation.BatchOperationAsync(request);

    // ================= 提醒调度：转发到 ReminderService =================
    public Task<int> ScanAndSendRemindersAsync()
        => _reminder.ScanAndSendRemindersAsync();
}
