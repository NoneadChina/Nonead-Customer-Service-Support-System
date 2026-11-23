namespace NoneadCSS.Domain.Enums;

/// <summary>
/// 重复类型
/// </summary>
public enum RecurrenceType
{
    Daily = 0,      // 每日
    Weekly = 1,     // 每周
    Monthly = 2,    // 每月
    Yearly = 3      // 每年
}

/// <summary>
/// 重复事件更新/删除模式
/// </summary>
public enum RecurrenceUpdateMode
{
    Single = 0,     // 仅本次
    Following = 1,  // 本次及后续
    All = 2         // 全部
}

/// <summary>
/// Outlook 同步冲突策略
/// </summary>
public enum OutlookSyncConflictStrategy
{
    LastWriteWins = 0,  // 较新者胜出（默认）
    LocalWins = 1,      // 本地胜出
    OutlookWins = 2     // Outlook 胜出
}
