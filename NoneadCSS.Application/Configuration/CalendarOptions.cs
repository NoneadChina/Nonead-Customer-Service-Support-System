namespace NoneadCSS.Application.Configuration;

/// <summary>
/// 日历模块配置（P2P3 §3.2 提醒加固配置项）
/// </summary>
public class CalendarOptions
{
    public const string SectionName = "Calendar";

    /// <summary>
    /// 提醒确认窗口（分钟）。默认 5 分钟。
    /// 当 ReminderSentAt + 窗口 < Now 且 ReminderConfirmation 仍为 pending 时，
    /// FallbackJob 会触发邮件+IM 补发流程。
    /// </summary>
    public int ReminderConfirmationWindowMinutes { get; set; } = 5;

    /// <summary>
    /// FallbackJob 扫描的"古老事件"截止小时数。默认 48 小时。
    /// 超过此时间的 pending 事件不再兜底补发（避免历史事件被反复扫描）。
    /// </summary>
    public int FallbackAncientCutoffHours { get; set; } = 48;

    /// <summary>
    /// 错误详情清洗的最大字符长度。默认 2000。
    /// 用于限制写入 CalendarReminderAttempts.ErrorDetail 的长度。
    /// </summary>
    public int SanitizeErrorMaxLength { get; set; } = 2000;

    /// <summary>
    /// ConfirmReminder 前端超时自动关闭的超时时间（毫秒）。默认 30000ms (30s)。
    /// </summary>
    public int ConfirmReminderAutoCloseTimeoutMs { get; set; } = 30000;

    /// <summary>
    /// 日历提醒扫描窗口（小时）。默认 1 小时。
    /// ScanAndSendRemindersAsync 将扫描 [now-30min, now+ScanWindowHours] 范围内的事件。
    /// </summary>
    public int ScanWindowHours { get; set; } = 1;

    /// <summary>
    /// 日历提醒扫描回溯时间（分钟）。默认 30 分钟。
    /// 用于避免"准时"提醒（ReminderMinutes=0）因扫描间隔错过触发时机。
    /// </summary>
    public int ScanLookbackMinutes { get; set; } = 30;

    /// <summary>
    /// 过期提醒补发截止时间（小时）。默认 2 小时。
    /// 超过此时间的"过期"提醒不再补发，避免系统恢复后批量补发无用提醒。
    /// </summary>
    public int ScanOverdueExpireHours { get; set; } = 2;

    /// <summary>
    /// 回收站默认保留天数（PurgeTrashAsync 未显式指定时使用）。默认 30 天。
    /// </summary>
    public int DefaultPurgeRetentionDays { get; set; } = 30;
}
