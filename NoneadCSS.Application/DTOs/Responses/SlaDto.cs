namespace NoneadCSS.Application.DTOs.Responses;

public class SlaPolicyDto
{
    public int Id { get; set; }
    public string Name { get; set; } = string.Empty;
    public string? Description { get; set; }
    public int? MailboxId { get; set; }
    public string? MailboxName { get; set; }
    public double FirstResponseHours { get; set; }
    public double ResolutionHours { get; set; }
    public bool BusinessHoursOnly { get; set; }

    /// <summary>
    /// 工作日配置（1=周一 … 7=周日），逗号分隔，默认周一至周五 "1,2,3,4,5"。
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

    public int Priority { get; set; }
    public bool IsActive { get; set; }
    public int SortOrder { get; set; }
    public int AppliedCount { get; set; }
    public int ViolationCount { get; set; }
    public DateTime? LastAppliedAt { get; set; }
    public DateTime CreatedAt { get; set; }
    public DateTime UpdatedAt { get; set; }
    public DateTime? DeletedAt { get; set; }

    /// <summary>
    /// 首次响应时间目标格式化字符串（如 "24 小时"）
    /// </summary>
    public string FirstResponseLabel { get; set; } = string.Empty;

    /// <summary>
    /// 解决时间目标格式化字符串
    /// </summary>
    public string ResolutionLabel { get; set; } = string.Empty;
}

public class SlaViolationDto
{
    public int Id { get; set; }
    public int SlaPolicyId { get; set; }
    public string SlaPolicyName { get; set; } = string.Empty;
    public int ConversationId { get; set; }
    public string? ConversationSubject { get; set; }
    public string ViolationType { get; set; } = "first_response";
    public string ViolationTypeLabel { get; set; } = string.Empty;
    public DateTime DueAt { get; set; }
    public DateTime DetectedAt { get; set; }
    public DateTime? ResolvedAt { get; set; }
    public string Status { get; set; } = "warning";
    public string StatusLabel { get; set; } = string.Empty;

    /// <summary>
    /// 距离截止时间剩余分钟数（已违规为负数）
    /// </summary>
    public double RemainingMinutes { get; set; }

    public DateTime CreatedAt { get; set; }
}

public class SlaOverviewResponse
{
    public Dictionary<string, int> PolicyStats { get; set; } = new();
    public Dictionary<string, int> ViolationStats { get; set; } = new();
    public List<SlaViolationDto> RecentViolations { get; set; } = new();
    public List<SlaViolationDto> ActiveWarnings { get; set; } = new();
    public List<SlaPolicyDto> TopPolicies { get; set; } = new();
}

public class CreateSlaPolicyRequest
{
    public string Name { get; set; } = string.Empty;
    public string? Description { get; set; }
    public int? MailboxId { get; set; }
    public double FirstResponseHours { get; set; } = 24.0;
    public double ResolutionHours { get; set; } = 72.0;
    public bool BusinessHoursOnly { get; set; } = false;

    /// <summary>
    /// 工作日配置（1=周一 … 7=周日），逗号分隔，默认周一至周五 "1,2,3,4,5"。
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

    public int Priority { get; set; } = 0;
    public bool IsActive { get; set; } = true;
    public int SortOrder { get; set; }
}

public class UpdateSlaPolicyRequest : CreateSlaPolicyRequest
{
    public int Id { get; set; }
}

public class SlaConversationStatusResponse
{
    public int ConversationId { get; set; }
    public string? PolicyName { get; set; }
    public double? FirstResponseHours { get; set; }
    public double? ResolutionHours { get; set; }
    public DateTime? FirstResponseDueAt { get; set; }
    public DateTime? ResolutionDueAt { get; set; }
    public DateTime? FirstResponseAt { get; set; }
    public DateTime? ResolvedAt { get; set; }

    /// <summary>
    /// 首次响应 SLA 状态：on_track / warning / breached / resolved / not_applicable
    /// </summary>
    public string FirstResponseStatus { get; set; } = "not_applicable";

    /// <summary>
    /// 解决 SLA 状态：on_track / warning / breached / resolved / not_applicable
    /// </summary>
    public string ResolutionStatus { get; set; } = "not_applicable";

    public double? FirstResponseRemainingMinutes { get; set; }
    public double? ResolutionRemainingMinutes { get; set; }
}
