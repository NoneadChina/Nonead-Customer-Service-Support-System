using NoneadCSS.Domain.Sla;

namespace NoneadCSS.Domain.Entities;

/// <summary>
/// SLA 策略。定义会话的响应时间和解决时间目标，用于监控服务质量。
/// </summary>
public class SlaPolicy : ISoftDelete
{
    public int Id { get; set; }
    public string Name { get; set; } = string.Empty;
    public string? Description { get; set; }

    /// <summary>
    /// 所属邮箱 ID。null 表示对所有邮箱生效。
    /// </summary>
    public int? MailboxId { get; set; }

    /// <summary>
    /// 首次响应时间目标（小时）。客服首次回复客户的最大时长。
    /// </summary>
    public double FirstResponseHours { get; set; } = 24.0;

    /// <summary>
    /// 解决时间目标（小时）。会话从创建到关闭的最大时长。
    /// </summary>
    public double ResolutionHours { get; set; } = 72.0;

    /// <summary>
    /// 是否仅在业务时间内计算。当为 true 时，SLA 目标时长按工作时段折算，
    /// 即跳过非工作日与工作时段之外的时间。
    /// </summary>
    public bool BusinessHoursOnly { get; set; } = false;

    /// <summary>
    /// 工作日配置（1=周一 … 7=周日），逗号分隔，默认周一至周五 "1,2,3,4,5"。
    /// 仅当 BusinessHoursOnly=true 时生效。
    /// </summary>
    public string WorkDays { get; set; } = "1,2,3,4,5";

    /// <summary>
    /// 每日工作开始小时（0-23），默认 9。
    /// </summary>
    public int WorkStartHour { get; set; } = 9;

    /// <summary>
    /// 每日工作结束小时（0-24），默认 18。
    /// </summary>
    public int WorkEndHour { get; set; } = 18;

    /// <summary>
    /// 优先级，数字越大优先级越高。同一邮箱多个策略时取优先级最高的。
    /// </summary>
    public int Priority { get; set; } = 0;

    public bool IsActive { get; set; } = true;
    public int SortOrder { get; set; }

    /// <summary>
    /// 累计应用次数（会话匹配到该策略的次数）。
    /// </summary>
    public int AppliedCount { get; set; }

    /// <summary>
    /// 累计违规次数。
    /// </summary>
    public int ViolationCount { get; set; }

    public DateTime? LastAppliedAt { get; set; }
    public DateTime CreatedAt { get; set; } = DateTime.UtcNow;
    public DateTime UpdatedAt { get; set; } = DateTime.UtcNow;

    /// <summary>软删除标记（null 表示未删除，非空表示已删除并记录删除时间）</summary>
    public DateTime? DeletedAt { get; set; }

    public Mailbox? Mailbox { get; set; }

    /// <summary>
    /// 计算首次响应截止时间。当 <see cref="BusinessHoursOnly"/> 为 true 时，
    /// 目标时长按工作时段折算（跳过非工作日与工作时段之外的时间）；否则按自然时间累加。
    /// </summary>
    /// <param name="firstCustomerMessageAt">客户首次消息时间</param>
    public DateTime CalculateFirstResponseDueAt(DateTime firstCustomerMessageAt)
        => AddBusinessTime(firstCustomerMessageAt, FirstResponseHours);

    /// <summary>
    /// 计算解决截止时间。当 <see cref="BusinessHoursOnly"/> 为 true 时，
    /// 目标时长按工作时段折算（跳过非工作日与工作时段之外的时间）；否则按自然时间累加。
    /// </summary>
    /// <param name="conversationCreatedAt">会话创建时间</param>
    public DateTime CalculateResolutionDueAt(DateTime conversationCreatedAt)
        => AddBusinessTime(conversationCreatedAt, ResolutionHours);

    /// <summary>
    /// 首次响应是否已达成（在目标时间内回复客户）。
    /// </summary>
    public bool IsFirstResponseFulfilled(DateTime firstCustomerMessageAt, DateTime? firstReplyAt)
        => firstReplyAt.HasValue && BusinessElapsedHours(firstCustomerMessageAt, firstReplyAt.Value) < FirstResponseHours;

    /// <summary>
    /// 首次响应是否已违规。
    /// </summary>
    public bool IsFirstResponseBreached(DateTime firstCustomerMessageAt, DateTime? firstReplyAt, DateTime now)
    {
        var elapsedHours = firstReplyAt.HasValue
            ? BusinessElapsedHours(firstCustomerMessageAt, firstReplyAt.Value)
            : BusinessElapsedHours(firstCustomerMessageAt, now);
        return elapsedHours >= FirstResponseHours;
    }

    /// <summary>
    /// 首次响应是否即将到期（预警）。
    /// </summary>
    public bool IsFirstResponseWarning(DateTime firstCustomerMessageAt, DateTime? firstReplyAt, DateTime now)
    {
        if (firstReplyAt.HasValue) return false;
        var elapsedHours = BusinessElapsedHours(firstCustomerMessageAt, now);
        return elapsedHours < FirstResponseHours
            && elapsedHours >= FirstResponseHours * (1 - SlaStatuses.WarningRatio);
    }

    /// <summary>
    /// 解决时间是否已达成（在目标时间内关闭会话）。
    /// </summary>
    public bool IsResolutionFulfilled(DateTime conversationCreatedAt, DateTime? closedAt)
        => closedAt.HasValue && BusinessElapsedHours(conversationCreatedAt, closedAt.Value) < ResolutionHours;

    /// <summary>
    /// 解决时间是否已违规。
    /// </summary>
    public bool IsResolutionBreached(DateTime conversationCreatedAt, DateTime? closedAt, DateTime now)
    {
        var elapsedHours = closedAt.HasValue
            ? BusinessElapsedHours(conversationCreatedAt, closedAt.Value)
            : BusinessElapsedHours(conversationCreatedAt, now);
        return elapsedHours >= ResolutionHours;
    }

    /// <summary>
    /// 解决时间是否即将到期（预警）。
    /// </summary>
    public bool IsResolutionWarning(DateTime conversationCreatedAt, DateTime? closedAt, DateTime now)
    {
        if (closedAt.HasValue) return false;
        var elapsedHours = BusinessElapsedHours(conversationCreatedAt, now);
        return elapsedHours < ResolutionHours
            && elapsedHours >= ResolutionHours * (1 - SlaStatuses.WarningRatio);
    }

    /// <summary>
    /// 计算首次响应状态（on_track / warning / breached / resolved / not_applicable）。
    /// </summary>
    public string CalculateFirstResponseStatus(DateTime firstCustomerMessageAt, DateTime? firstReplyAt, DateTime now)
    {
        if (firstReplyAt.HasValue)
            return CalculateStatus(FirstResponseHours, BusinessElapsedHours(firstCustomerMessageAt, firstReplyAt.Value), true);

        return CalculateStatus(FirstResponseHours, BusinessElapsedHours(firstCustomerMessageAt, now), false);
    }

    /// <summary>
    /// 计算解决时长状态。
    /// </summary>
    /// <param name="conversationCreatedAt">会话创建时间</param>
    /// <param name="isClosed">会话是否已关闭</param>
    /// <param name="closedAt">关闭时间</param>
    /// <param name="now">当前时间</param>
    public string CalculateResolutionStatus(DateTime conversationCreatedAt, bool isClosed, DateTime? closedAt, DateTime now)
    {
        if (isClosed && closedAt.HasValue)
            return CalculateStatus(ResolutionHours, BusinessElapsedHours(conversationCreatedAt, closedAt.Value), true);

        if (!isClosed)
            return CalculateStatus(ResolutionHours, BusinessElapsedHours(conversationCreatedAt, now), false);

        return SlaStatuses.ConversationNotApplicable;
    }

    /// <summary>
    /// 记录策略被应用到会话。
    /// </summary>
    public void RecordApplication(DateTime? at = null)
    {
        AppliedCount++;
        LastAppliedAt = at ?? DateTime.UtcNow;
        UpdatedAt = DateTime.UtcNow;
    }

    /// <summary>
    /// 记录一次违规。
    /// </summary>
    public void RecordViolation()
    {
        ViolationCount++;
    }

    /// <summary>
    /// 判断给定日期是否为工作日（依据 <see cref="WorkDays"/> 配置）。
    /// </summary>
    private bool IsWorkDay(DateTime dt)
    {
        var dayOfWeek = ((int)dt.DayOfWeek + 6) % 7 + 1;
        if (string.IsNullOrWhiteSpace(WorkDays)) return false;
        foreach (var part in WorkDays.Split(','))
        {
            if (int.TryParse(part.Trim(), out var day) && day == dayOfWeek)
                return true;
        }
        return false;
    }

    /// <summary>
    /// 标准化每日工作窗口（开始小时与结束小时）。结束小时取值范围 [1,24]，且始终大于开始小时。
    /// </summary>
    private (int StartHour, int EndHour) GetBusinessWindow()
    {
        var start = Math.Clamp(WorkStartHour, 0, 23);
        var end = Math.Clamp(WorkEndHour, 1, 24);
        if (end <= start) end = Math.Min(start + 1, 24);
        return (start, end);
    }

    /// <summary>
    /// 计算在业务时间内累加 <paramref name="targetHours"/> 小时之后的截止时间。
    /// 当 <see cref="BusinessHoursOnly"/> 为 false 时退回为自然时间累加，保持原有行为。
    /// </summary>
    private DateTime AddBusinessTime(DateTime start, double targetHours)
    {
        if (targetHours <= 0) return start;
        if (!BusinessHoursOnly) return start.AddHours(targetHours);

        var (startHour, endHour) = GetBusinessWindow();
        var remaining = targetHours * 60.0;
        var current = start;
        var guard = 0;

        while (remaining > 0)
        {
            if (++guard > 100000) break;
            if (!IsWorkDay(current))
            {
                current = current.Date.AddDays(1);
                continue;
            }

            var workStart = current.Date.AddHours(startHour);
            var workEnd = current.Date.AddHours(endHour);
            if (current < workStart)
            {
                current = workStart;
                continue;
            }

            if (current >= workEnd)
            {
                current = current.Date.AddDays(1);
                continue;
            }

            var availableMinutes = (workEnd - current).TotalMinutes;
            if (availableMinutes >= remaining)
            {
                current = current.AddMinutes(remaining);
                remaining = 0;
            }
            else
            {
                remaining -= availableMinutes;
                current = workEnd;
            }
        }

        return current;
    }

    /// <summary>
    /// 计算两个时间点之间耗时（小时）。当 <see cref="BusinessHoursOnly"/> 为 true 时，
    /// 仅统计工作日且在工作时段内的耗时；否则退回为自然时间差。
    /// </summary>
    private double BusinessElapsedHours(DateTime start, DateTime end)
    {
        if (end <= start) return 0;
        if (!BusinessHoursOnly) return (end - start).TotalHours;

        var (startHour, endHour) = GetBusinessWindow();
        var totalMinutes = 0.0;
        var current = start;
        var guard = 0;

        while (current < end)
        {
            if (++guard > 100000) break;
            if (!IsWorkDay(current))
            {
                current = current.Date.AddDays(1);
                continue;
            }

            var workStart = current.Date.AddHours(startHour);
            var workEnd = current.Date.AddHours(endHour);
            if (current < workStart)
            {
                current = workStart;
                continue;
            }

            if (current >= workEnd)
            {
                current = current.Date.AddDays(1);
                continue;
            }

            var overlapEnd = end < workEnd ? end : workEnd;
            totalMinutes += (overlapEnd - current).TotalMinutes;
            current = overlapEnd;
            if (current >= workEnd) current = workEnd;
        }

        return totalMinutes / 60.0;
    }

    private static string CalculateStatus(double targetHours, double elapsedHours, bool isResolved)
    {
        if (isResolved) return SlaStatuses.ConversationResolved;
        // BUG-06 修复：targetHours 为 0 或负数时（历史数据/直接修改 DB 导致），
        // 直接判定为 breached，避免下方除零计算 remainingRatio 时抛出 DivideByZeroException /
        // 得到 NaN，导致 SLA 状态显示异常和下游通知逻辑误判
        if (targetHours <= 0) return SlaStatuses.ConversationBreached;
        if (elapsedHours >= targetHours) return SlaStatuses.ConversationBreached;
        var remainingRatio = (targetHours - elapsedHours) / targetHours;
        return remainingRatio <= SlaStatuses.WarningRatio ? SlaStatuses.ConversationWarning : SlaStatuses.ConversationOnTrack;
    }
}

/// <summary>
/// SLA 违规记录。当会话超过 SLA 目标时间未响应或未解决时产生。
/// </summary>
public class SlaViolation
{
    public int Id { get; set; }
    public int SlaPolicyId { get; set; }
    public int ConversationId { get; set; }

    /// <summary>
    /// 违规类型：first_response（首次响应）/ resolution（解决时长）
    /// </summary>
    public string ViolationType { get; set; } = "first_response";

    /// <summary>
    /// 截止时间（创建时间 + SLA 目标时长）。
    /// </summary>
    public DateTime DueAt { get; set; }

    /// <summary>
    /// 检测到违规的时间。
    /// </summary>
    public DateTime DetectedAt { get; set; } = DateTime.UtcNow;

    /// <summary>
    /// 实际响应/解决时间。null 表示尚未响应/解决。
    /// </summary>
    public DateTime? ResolvedAt { get; set; }

    /// <summary>
    /// 状态：warning（即将到期预警）/ breached（已违规）/ resolved（已解决）
    /// </summary>
    public string Status { get; set; } = "warning";

    public DateTime CreatedAt { get; set; } = DateTime.UtcNow;

    public SlaPolicy? SlaPolicy { get; set; }
    public Conversation? Conversation { get; set; }
}
