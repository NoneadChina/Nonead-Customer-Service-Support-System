namespace NoneadCSS.Application.DTOs.Responses;

public class AuditLogResponse
{
    public int Id { get; set; }
    public int? UserId { get; set; }
    public string UserName { get; set; } = string.Empty;
    public string Action { get; set; } = string.Empty;
    public string EntityType { get; set; } = string.Empty;
    public int? EntityId { get; set; }
    public string OldValues { get; set; } = string.Empty;
    public string NewValues { get; set; } = string.Empty;
    public string IpAddress { get; set; } = string.Empty;
    public string UserAgent { get; set; } = string.Empty;
    public DateTime CreatedAt { get; set; }

    /// <summary>
    /// HMAC 签名（Base64），用于审计展示与外发对账。
    /// 仅超管可见，前端可据此判断记录是否被篡改。
    /// </summary>
    public string HmacSignature { get; set; } = string.Empty;

    /// <summary>
    /// 签名校验状态：true 表示签名匹配（未被篡改）；false 表示签名缺失或不匹配（疑似篡改）。
    /// 由服务端在校验时填充，列表查询时默认为 null（不显式校验，避免性能开销）。
    /// </summary>
    public bool? IsTampered { get; set; }
}

/// <summary>
/// 审计日志统计响应。
/// </summary>
public class AuditLogStatsResponse
{
    /// <summary>符合过滤条件的总记录数</summary>
    public int TotalLogs { get; set; }

    /// <summary>符合过滤条件的独立用户数</summary>
    public int UniqueUsers { get; set; }

    /// <summary>近 24 小时新增记录数</summary>
    public int Last24hCount { get; set; }

    /// <summary>异常事件数（登录失败 / 锁定 / 重置失败）</summary>
    public int FailedActions { get; set; }

    /// <summary>操作类型分布（Top N，按计数降序）</summary>
    public List<AuditLogDistributionItem> ActionDistribution { get; set; } = new();

    /// <summary>实体类型分布（Top N，按计数降序）</summary>
    public List<AuditLogDistributionItem> EntityDistribution { get; set; } = new();
}

public class AuditLogDistributionItem
{
    public string Key { get; set; } = string.Empty;
    public int Count { get; set; }
}
