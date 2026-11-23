namespace NoneadCSS.Domain.Entities;

public class AuditLog
{
    public int Id { get; set; }
    public int? UserId { get; set; }
    public string Action { get; set; } = string.Empty;
    public string EntityType { get; set; } = string.Empty;
    public int? EntityId { get; set; }
    public string OldValues { get; set; } = string.Empty;
    public string NewValues { get; set; } = string.Empty;
    public string IpAddress { get; set; } = string.Empty;
    public string UserAgent { get; set; } = string.Empty;
    public DateTime CreatedAt { get; set; } = DateTime.UtcNow;

    /// <summary>
    /// 审计日志的 HMAC-SHA256 签名（Base64），用于检测事后篡改。
    /// 签名覆盖：UserId、Action、EntityType、EntityId、CreatedAt。
    /// 不签名 OldValues/NewValues 以避免敏感数据长度的 DoS。
    /// </summary>
    public string HmacSignature { get; set; } = string.Empty;

    public User? User { get; set; }
}
