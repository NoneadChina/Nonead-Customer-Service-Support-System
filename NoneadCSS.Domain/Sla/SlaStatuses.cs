namespace NoneadCSS.Domain.Sla;

/// <summary>
/// SLA 核心状态常量。放置于领域层，供实体与应用服务共用。
/// </summary>
public static class SlaStatuses
{
    public const string ViolationFirstResponse = "first_response";
    public const string ViolationResolution = "resolution";

    public const string ViolationWarning = "warning";
    public const string ViolationBreached = "breached";
    public const string ViolationResolved = "resolved";

    public const string ConversationOnTrack = "on_track";
    public const string ConversationWarning = "warning";
    public const string ConversationBreached = "breached";
    public const string ConversationResolved = "resolved";
    public const string ConversationNotApplicable = "not_applicable";

    /// <summary>
    /// 预警阈值：剩余时间低于总时长的 20% 时进入 warning 状态。
    /// </summary>
    public const double WarningRatio = 0.2;
}
