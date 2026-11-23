using System.ComponentModel.DataAnnotations;
using System.Text.Json.Serialization;

namespace NoneadCSS.Application.DTOs.Responses;

/// <summary>
/// Outlook 同步设置 DTO
/// </summary>
public class OutlookSyncSettingDto
{
    [JsonPropertyName("id")]
    public int Id { get; set; }

    [JsonPropertyName("userId")]
    public int UserId { get; set; }

    [JsonPropertyName("outlookEmail")]
    public string? OutlookEmail { get; set; }

    [JsonPropertyName("outlookUserId")]
    public string? OutlookUserId { get; set; }

    /// <summary>
    /// 是否已连接（拥有有效的 Access+Refresh 令牌）
    /// </summary>
    [JsonPropertyName("isConnected")]
    public bool IsConnected { get; set; }

    /// <summary>
    /// 令牌是否已过期（需要刷新）
    /// </summary>
    [JsonPropertyName("isTokenExpired")]
    public bool IsTokenExpired { get; set; }

    [JsonPropertyName("syncEnabled")]
    public bool SyncEnabled { get; set; }

    /// <summary>
    /// 同步方向：both / to_outlook / from_outlook
    /// </summary>
    [JsonPropertyName("syncDirection")]
    public string SyncDirection { get; set; } = "both";

    [JsonPropertyName("lastSyncAt")]
    public DateTime? LastSyncAt { get; set; }

    [JsonPropertyName("lastSyncStatus")]
    public string? LastSyncStatus { get; set; }

    [JsonPropertyName("lastSyncError")]
    public string? LastSyncError { get; set; }

    /// <summary>
    /// 令牌过期时间（UTC）
    /// </summary>
    [JsonPropertyName("tokenExpiresAt")]
    public DateTime? TokenExpiresAt { get; set; }

    [JsonPropertyName("createdAt")]
    public DateTime CreatedAt { get; set; }

    [JsonPropertyName("updatedAt")]
    public DateTime UpdatedAt { get; set; }
}

/// <summary>
/// Outlook 同步状态 DTO
/// </summary>
public class OutlookSyncStatusDto
{
    /// <summary>
    /// 是否已配置同步设置
    /// </summary>
    [JsonPropertyName("isConfigured")]
    public bool IsConfigured { get; set; }

    /// <summary>
    /// 是否已连接 Outlook 账户
    /// </summary>
    [JsonPropertyName("isConnected")]
    public bool IsConnected { get; set; }

    /// <summary>
    /// 同步是否启用
    /// </summary>
    [JsonPropertyName("syncEnabled")]
    public bool SyncEnabled { get; set; }

    [JsonPropertyName("syncDirection")]
    public string SyncDirection { get; set; } = "both";

    [JsonPropertyName("outlookEmail")]
    public string? OutlookEmail { get; set; }

    [JsonPropertyName("lastSyncAt")]
    public DateTime? LastSyncAt { get; set; }

    [JsonPropertyName("lastSyncStatus")]
    public string? LastSyncStatus { get; set; }

    [JsonPropertyName("lastSyncError")]
    public string? LastSyncError { get; set; }

    /// <summary>
    /// 是否正在同步中（用于前端禁用按钮）
    /// </summary>
    [JsonPropertyName("isSyncing")]
    public bool IsSyncing { get; set; }
}

/// <summary>
/// Outlook 同步执行结果 DTO
/// </summary>
public class OutlookSyncResultDto
{
    /// <summary>
    /// 同步是否成功
    /// </summary>
    [JsonPropertyName("success")]
    public bool Success { get; set; }

    /// <summary>
    /// 从本地推送到 Outlook 的事件数量
    /// </summary>
    [JsonPropertyName("pushedToOutlook")]
    public int PushedToOutlook { get; set; }

    /// <summary>
    /// 从 Outlook 拉取到本地的事件数量
    /// </summary>
    [JsonPropertyName("pulledFromOutlook")]
    public int PulledFromOutlook { get; set; }

    /// <summary>
    /// 更新的事件数量（双向更新）
    /// </summary>
    [JsonPropertyName("updated")]
    public int Updated { get; set; }

    /// <summary>
    /// 删除的事件数量
    /// </summary>
    [JsonPropertyName("deleted")]
    public int Deleted { get; set; }

    /// <summary>
    /// 冲突数量
    /// </summary>
    [JsonPropertyName("conflicts")]
    public int Conflicts { get; set; }

    /// <summary>
    /// 跳过的事件数量（无变化或错误）
    /// </summary>
    [JsonPropertyName("skipped")]
    public int Skipped { get; set; }

    /// <summary>
    /// 错误信息（如有）
    /// </summary>
    [JsonPropertyName("errorMessage")]
    public string? ErrorMessage { get; set; }

    /// <summary>
    /// 同步耗时（毫秒）
    /// </summary>
    [JsonPropertyName("durationMs")]
    public long DurationMs { get; set; }

    /// <summary>
    /// 同步完成时间（UTC）
    /// </summary>
    [JsonPropertyName("syncedAt")]
    public DateTime SyncedAt { get; set; } = DateTime.UtcNow;

    /// <summary>
    /// P3-D: 新增条数（From Outlook）
    /// </summary>
    [JsonPropertyName("created")]
    public int Created { get; set; }

    /// <summary>
    /// P3-D: 是否触发了 DeltaToken 重置（true=原 token 失效，走了全量后刷新 token）
    /// </summary>
    [JsonPropertyName("deltaTokenReset")]
    public bool DeltaTokenReset { get; set; }

    /// <summary>
    /// P3-D: 同步状态：success / failed / partial / syncing
    /// </summary>
    [JsonPropertyName("status")]
    public string Status { get; set; } = "success";
}

/// <summary>
/// 更新 Outlook 同步设置请求
/// </summary>
public class UpdateOutlookSyncSettingRequest : IValidatableObject
{
    [JsonPropertyName("syncEnabled")]
    public bool SyncEnabled { get; set; } = true;

    /// <summary>
    /// 同步方向：both / to_outlook / from_outlook
    /// </summary>
    [MaxLength(20, ErrorMessage = "同步方向最多 20 个字符")]
    [JsonPropertyName("syncDirection")]
    public string SyncDirection { get; set; } = "both";

    public IEnumerable<ValidationResult> Validate(ValidationContext validationContext)
    {
        var validSyncDirections = new HashSet<string>(StringComparer.OrdinalIgnoreCase)
        {
            "both", "to_outlook", "from_outlook"
        };
        if (!validSyncDirections.Contains(SyncDirection))
        {
            yield return new ValidationResult(
                $"同步方向 '{SyncDirection}' 无效，有效值为：{string.Join(", ", validSyncDirections)}",
                new[] { nameof(SyncDirection) });
        }
    }
}

/// <summary>
/// T000099: Outlook 同步 Azure AD 凭据管理 DTO（脱敏响应）
/// 仅超管可访问，用于管理后台 UI 配置 Azure AD 应用注册凭据。
/// ClientSecret 不直接返回明文，仅返回 HasClientSecret 标识是否存在已配置的密钥。
/// </summary>
public class OutlookAdminSettingsDto
{
    /// <summary>Outlook 同步整体是否已配置必要凭据（ClientId + ClientSecret + RedirectUri 三者均非空）</summary>
    [JsonPropertyName("isConfigured")]
    public bool IsConfigured { get; set; }

    /// <summary>客户端 ID（非敏感，原值返回便于核对）</summary>
    [JsonPropertyName("clientId")]
    public string? ClientId { get; set; }

    /// <summary>是否已设置 ClientSecret（不返回明文）</summary>
    [JsonPropertyName("hasClientSecret")]
    public bool HasClientSecret { get; set; }

    /// <summary>租户 ID（common 或具体 GUID，非敏感）</summary>
    [JsonPropertyName("tenantId")]
    public string? TenantId { get; set; }

    /// <summary>OAuth 回调地址（必须与 Azure 应用注册一致，非敏感）</summary>
    [JsonPropertyName("redirectUri")]
    public string? RedirectUri { get; set; }

    /// <summary>请求的权限范围（非敏感）</summary>
    [JsonPropertyName("scopes")]
    public string? Scopes { get; set; }

    /// <summary>配置来源标识："database"=管理后台已配置，"appsettings"=仅基线配置（含环境变量覆盖）</summary>
    [JsonPropertyName("source")]
    public string Source { get; set; } = "appsettings";

    /// <summary>最后更新时间（UTC，仅当 source=database 时有效）</summary>
    [JsonPropertyName("updatedAt")]
    public DateTime? UpdatedAt { get; set; }
}

/// <summary>
/// T000099: 更新 Outlook Azure AD 凭据请求
/// 仅超管可调用。ClientSecret 为空字符串/null 表示不更新该字段，非空表示覆盖。
/// </summary>
public class UpdateOutlookAdminSettingsRequest
{
    /// <summary>客户端 ID。null 或空字符串表示不更新。</summary>
    [JsonPropertyName("clientId")]
    public string? ClientId { get; set; }

    /// <summary>
    /// 客户端密钥。null 或空字符串表示不更新；
    /// 传入非空字符串将加密存储（"ENC:" 前缀）。
    /// 若需清空密钥，请传入字面量 "CLEAR"（区分大小写）。
    /// </summary>
    [JsonPropertyName("clientSecret")]
    public string? ClientSecret { get; set; }

    /// <summary>租户 ID。null 或空字符串表示不更新。</summary>
    [JsonPropertyName("tenantId")]
    public string? TenantId { get; set; }

    /// <summary>OAuth 回调地址。null 或空字符串表示不更新。</summary>
    [JsonPropertyName("redirectUri")]
    public string? RedirectUri { get; set; }

    /// <summary>权限范围。null 或空字符串表示不更新。</summary>
    [JsonPropertyName("scopes")]
    public string? Scopes { get; set; }
}
