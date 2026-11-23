using NoneadCSS.Application.DTOs.Requests;
using NoneadCSS.Application.DTOs.Responses;

namespace NoneadCSS.Application.Services;

/// <summary>
/// WhatsApp 管理服务 - 配置读写、连接状态查询、回复消息、模板消息发送。
/// </summary>
public interface IWhatsAppService
{
    /// <summary>获取当前 WhatsApp 配置（敏感字段脱敏）。</summary>
    Task<WhatsAppSettingsDto> GetSettingsAsync();

    /// <summary>更新 WhatsApp 配置，持久化到 SystemOptions 数据库表并刷新内存缓存。</summary>
    Task<WhatsAppSettingsDto> UpdateSettingsAsync(UpdateWhatsAppSettingsRequest request);

    /// <summary>获取 WhatsApp 连接状态（是否已配置、是否已启用、API 可达性、24h 窗口状态）。</summary>
    Task<WhatsAppStatusDto> GetStatusAsync();

    /// <summary>通过 Cloud API 回复客户消息（窗口内可发任意类型，窗口外拒绝）。</summary>
    Task<WhatsAppReplyResult> ReplyAsync(WhatsAppReplyRequest request, int userId);

    /// <summary>发送模板消息（不受 24h 窗口限制）。</summary>
    Task<WhatsAppTemplateResult> SendTemplateMessageAsync(WhatsAppTemplateRequest request, int userId);

    /// <summary>测试连接 — 向商家号码发送一条测试文本，验证 Access Token 和 Phone Number ID 是否正确。</summary>
    Task<WhatsAppTestResult> TestConnectionAsync();
}

/// <summary>WhatsApp 回复结果</summary>
public class WhatsAppReplyResult
{
    /// <summary>消息是否发送成功</summary>
    public bool Sent { get; set; }

    /// <summary>线程记录是否创建成功（消息已发送但线程记录失败时为 false）</summary>
    public bool ThreadCreated { get; set; } = true;

    /// <summary>创建的回复线程 ID</summary>
    public int? ThreadId { get; set; }

    /// <summary>错误信息</summary>
    public string? ErrorMessage { get; set; }
}

/// <summary>WhatsApp 连接状态 DTO</summary>
public class WhatsAppStatusDto
{
    public bool Configured { get; set; }
    public bool Enabled { get; set; }
    public bool ApiReachable { get; set; }
    public string? CallbackUrl { get; set; }

    /// <summary>24h 窗口状态：ok(剩余>4h) / warning(剩余≤4h) / expired(已过期) / unknown(无会话)</summary>
    public string WindowStatus { get; set; } = "unknown";

    /// <summary>窗口剩余小时数（expired 时为 0）</summary>
    public int WindowRemainingHours { get; set; }

    /// <summary>最近一条客户消息时间</summary>
    public DateTime? LastCustomerMessageAt { get; set; }
}

/// <summary>WhatsApp 模板消息发送结果</summary>
public class WhatsAppTemplateResult
{
    public bool Sent { get; set; }
    public string? MessageId { get; set; }
    public string? ErrorMessage { get; set; }
}

/// <summary>WhatsApp 测试连接结果</summary>
public class WhatsAppTestResult
{
    public bool Success { get; set; }
    public string? MessageId { get; set; }
    public string? ErrorMessage { get; set; }
}
