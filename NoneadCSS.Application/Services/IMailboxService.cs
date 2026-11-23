using NoneadCSS.Application.DTOs.Requests;
using NoneadCSS.Application.DTOs.Responses;

namespace NoneadCSS.Application.Services;

public interface IMailboxService
{
    Task<List<MailboxResponse>> GetMailboxesByUserIdAsync(int userId);
    Task<MailboxResponse?> GetMailboxByIdAsync(int id);
    Task<bool> TestConnectionAsync(int id);
    Task<(bool Success, string? ErrorMessage)> TestConnectionWithDetailsAsync(int id);
    Task<MailboxResponse?> CreateMailboxAsync(CreateMailboxRequest request);
    Task<MailboxResponse?> UpdateMailboxAsync(int id, CreateMailboxRequest request);
    Task<bool> DeleteMailboxAsync(int id);
    Task<bool> ToggleActiveAsync(int id);
    Task<List<MailboxResponse>> GetAllMailboxesAsync();
    Task<List<string>> GetImapFoldersAsync(int id);

    // 垃圾箱
    Task<List<MailboxResponse>> GetDeletedMailboxesAsync();
    Task<MailboxResponse?> RestoreMailboxAsync(int id);
    Task<bool> HardDeleteMailboxAsync(int id);
    Task<int> ClearTrashAsync();

    // 扩展方法 - 对应 PHP 邮箱高级管理
    Task<MailboxPermissionResponse?> GetMailboxPermissionsAsync(int mailboxId);
    Task<MailboxPermissionResponse?> UpdateMailboxPermissionsAsync(int mailboxId, MailboxPermissionRequest request);
    Task<MailboxAutoReplyResponse?> GetMailboxAutoReplyAsync(int mailboxId);
    Task<MailboxAutoReplyResponse?> UpdateMailboxAutoReplyAsync(int mailboxId, MailboxAutoReplyRequest request);
    Task<MailboxSignatureResponse?> GetMailboxSignatureAsync(int mailboxId);
    Task<MailboxSignatureResponse?> UpdateMailboxSignatureAsync(int mailboxId, MailboxSignatureRequest request);
    Task<MailboxOAuthStatusResponse?> CompleteMailboxOAuthAsync(int mailboxId, string code, string redirectUri);
}
