using NoneadCSS.Application.DTOs.Responses;
using Microsoft.AspNetCore.Http;

namespace NoneadCSS.Infrastructure.Services;

public interface IAttachmentService
{
    Task<AttachmentResponse> UploadAsync(IFormFile file, int? threadId = null, int? userId = null);
    Task<List<AttachmentResponse>> UploadMultipleAsync(List<IFormFile> files, int? threadId = null, int? userId = null);
    Task<AttachmentResponse> UploadFromBase64Async(string base64Content, string fileName, string mimeType, int? threadId = null, int? userId = null);
    Task<(byte[] Content, string MimeType, string FileName)> DownloadAsync(int id, int? userId = null, bool isAdmin = false);
    Task<(byte[] Content, string MimeType)> GetPreviewAsync(int id, int maxWidth = 300, int maxHeight = 300, int? userId = null, bool isAdmin = false);
    Task<bool> DeleteAsync(int id, int? userId = null, bool isAdmin = false);
    Task<bool> DeleteByThreadIdAsync(int threadId, int? userId = null, bool isAdmin = false);
    Task<bool> DeleteByConversationIdAsync(int conversationId, int? userId = null, bool isAdmin = false);
    Task<AttachmentResponse?> GetAttachmentAsync(int id, int? userId = null, bool isAdmin = false);
    Task<List<AttachmentResponse>> GetAttachmentsByThreadIdAsync(int threadId, int? userId = null, bool isAdmin = false);
    Task<List<AttachmentResponse>> GetAttachmentsByConversationIdAsync(int conversationId, int? userId = null, bool isAdmin = false);
    Task<List<AttachmentResponse>> GetAttachmentsByUserIdAsync(int userId, int? requesterId = null, bool isAdmin = false);

    /// <summary>
    /// 验证当前用户是否有权访问指定附件（IDOR 防护）。
    /// </summary>
    /// <returns>true 表示有权限；false 表示无权限或附件不存在。</returns>
    Task<bool> VerifyAttachmentAccessAsync(int attachmentId, int? userId, bool isAdmin);

    /// <summary>
    /// 验证当前用户是否有权访问指定会话的附件（IDOR 防护）。
    /// </summary>
    /// <returns>true 表示有权限；false 表示无权限或会话不存在。</returns>
    Task<bool> VerifyConversationAccessAsync(int conversationId, int? userId, bool isAdmin);

    bool IsAllowedExtension(string fileName);
    bool IsAllowedMimeType(string mimeType);
    bool IsWithinSizeLimit(long fileSize);
}
