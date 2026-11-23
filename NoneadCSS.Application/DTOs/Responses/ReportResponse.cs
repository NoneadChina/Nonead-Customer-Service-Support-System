namespace NoneadCSS.Application.DTOs.Responses;

/// <summary>
/// 综合报表响应
/// </summary>
public class ReportResponse
{
    /// <summary>开始日期</summary>
    public string StartDate { get; set; } = string.Empty;

    /// <summary>结束日期</summary>
    public string EndDate { get; set; } = string.Empty;

    /// <summary>邮箱筛选（null 表示全部）</summary>
    public int? MailboxId { get; set; }

    /// <summary>核心指标</summary>
    public ReportSummaryDto Summary { get; set; } = new();

    /// <summary>趋势数据</summary>
    public List<ReportTrendPointDto> Trend { get; set; } = [];

    /// <summary>会话状态分布</summary>
    public List<ReportStatusDistributionDto> StatusDistribution { get; set; } = [];

    /// <summary>客服绩效</summary>
    public List<ReportUserPerformanceDto> UserPerformance { get; set; } = [];

    /// <summary>邮箱负载</summary>
    public List<ReportMailboxLoadDto> MailboxLoad { get; set; } = [];

    /// <summary>客户域名 Top N</summary>
    public List<ReportCustomerDomainDto> CustomerDomains { get; set; } = [];
}

/// <summary>
/// 报表核心指标
/// </summary>
public class ReportSummaryDto
{
    /// <summary>时间范围内新建会话数</summary>
    public int NewConversations { get; set; }

    /// <summary>时间范围内关闭会话数</summary>
    public int ClosedConversations { get; set; }

    /// <summary>时间范围内客服回复数</summary>
    public int TotalReplies { get; set; }

    /// <summary>新增客户数</summary>
    public int NewCustomers { get; set; }

    /// <summary>平均首次响应时长（小时）</summary>
    public double AverageFirstResponseHours { get; set; }

    /// <summary>平均解决时长（小时）</summary>
    public double AverageResolutionHours { get; set; }

    /// <summary>SLA 达成率（百分比，0-100）</summary>
    public double SlaAchievementRate { get; set; }
}

/// <summary>
/// 报表趋势点
/// </summary>
public class ReportTrendPointDto
{
    public string Date { get; set; } = string.Empty;
    public int NewCount { get; set; }
    public int ClosedCount { get; set; }
    public int ReplyCount { get; set; }
}

/// <summary>
/// 会话状态分布
/// </summary>
public class ReportStatusDistributionDto
{
    /// <summary>状态值（1=活跃, 2=待处理, 3=已关闭）</summary>
    public int Status { get; set; }

    /// <summary>状态名称</summary>
    public string StatusName { get; set; } = string.Empty;

    /// <summary>会话数量</summary>
    public int Count { get; set; }

    /// <summary>百分比</summary>
    public double Percentage { get; set; }
}

/// <summary>
/// 客服绩效
/// </summary>
public class ReportUserPerformanceDto
{
    public int UserId { get; set; }
    public string UserName { get; set; } = string.Empty;
    public string Email { get; set; } = string.Empty;

    /// <summary>分配给该客服的会话数</summary>
    public int AssignedCount { get; set; }

    /// <summary>时间范围内回复数</summary>
    public int ReplyCount { get; set; }

    /// <summary>时间范围内关闭的会话数</summary>
    public int ClosedCount { get; set; }

    /// <summary>平均首次响应时长（小时）</summary>
    public double AverageFirstResponseHours { get; set; }
}

/// <summary>
/// 邮箱负载
/// </summary>
public class ReportMailboxLoadDto
{
    public int MailboxId { get; set; }
    public string MailboxName { get; set; } = string.Empty;
    public string Email { get; set; } = string.Empty;

    /// <summary>时间范围内新建会话数</summary>
    public int NewCount { get; set; }

    /// <summary>时间范围内关闭会话数</summary>
    public int ClosedCount { get; set; }

    /// <summary>当前活跃会话数</summary>
    public int ActiveCount { get; set; }

    /// <summary>百分比</summary>
    public double Percentage { get; set; }
}

/// <summary>
/// 客户域名统计
/// </summary>
public class ReportCustomerDomainDto
{
    /// <summary>客户邮箱域名</summary>
    public string Domain { get; set; } = string.Empty;

    /// <summary>会话数量</summary>
    public int ConversationCount { get; set; }

    /// <summary>独立客户数</summary>
    public int UniqueCustomerCount { get; set; }

    /// <summary>百分比</summary>
    public double Percentage { get; set; }
}
