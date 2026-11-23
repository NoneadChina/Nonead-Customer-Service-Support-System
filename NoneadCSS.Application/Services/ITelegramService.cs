using NoneadCSS.Application.DTOs.Requests;
using NoneadCSS.Application.DTOs.Responses;

namespace NoneadCSS.Application.Services;

/// <summary>
/// Telegram 管理服务 - 配置读写（敏感字段不回显、加密存储）、Webhook 登记同步、连接状态、坐席回复。
/// 与 WhatsAppService 同构，但 Telegram 无 24 小时回复窗口：坐席可随时回复，
/// 403（用户拉黑 / bot 被移出群）时把会话标记为不可达并停止重试。
/// </summary>
public interface ITelegramService
{
    /// <summary>读取配置（脱敏）</summary>
    Task<TelegramSettingsDto> GetSettingsAsync();

    /// <summary>保存配置并按当前 Mode 同步 setWebhook / deleteWebhook</summary>
    Task<TelegramSettingsDto> UpdateSettingsAsync(UpdateTelegramSettingsRequest request);

    /// <summary>读取状态（getMe + getWebhookInfo + 轮询游标 + 不可达会话数）</summary>
    Task<TelegramStatusDto> GetStatusAsync();

    /// <summary>坐席回复（文本超长自动分片；附件走 multipart 上传；403 标记不可达）</summary>
    Task<TelegramReplyResult> ReplyAsync(TelegramReplyRequest request, int userId);

    /// <summary>手动重新登记 Webhook（配置变更后 Telegram 侧未生效时使用）</summary>
    Task<TelegramTestResult> SetupWebhookAsync(TelegramWebhookSetupRequest? request);

    /// <summary>删除 Telegram 侧 Webhook（切换轮询前 / 排障）</summary>
    Task<TelegramTestResult> RemoveWebhookAsync();

    /// <summary>清除会话的不可达标记（客户重新联系或坐席确认已恢复）</summary>
    Task<bool> ClearUnreachableMarkAsync(int conversationId);

    /// <summary>连接性测试（getMe，不发送任何消息）</summary>
    Task<TelegramTestResult> TestConnectionAsync();
}
