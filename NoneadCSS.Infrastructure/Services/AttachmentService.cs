using NoneadCSS.Application.Configuration;
using NoneadCSS.Application.DTOs.Responses;
using NoneadCSS.Domain;
using NoneadCSS.Domain.Entities;
using NoneadCSS.Domain.Enums;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Hosting;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using SixLabors.ImageSharp;
using SixLabors.ImageSharp.Processing;

namespace NoneadCSS.Infrastructure.Services;

public class AttachmentService : IAttachmentService
{
    private readonly IUnitOfWork _unitOfWork;
    private readonly AttachmentSettings _settings;
    private readonly ILogger<AttachmentService> _logger;
    private readonly IVirusScanService _virusScanService;
    private readonly string _basePath;

    public AttachmentService(IUnitOfWork unitOfWork, IOptions<AttachmentSettings> settings, ILogger<AttachmentService> logger, IWebHostEnvironment env, IVirusScanService virusScanService)
    {
        _unitOfWork = unitOfWork;
        _settings = settings.Value;
        _logger = logger;
        _virusScanService = virusScanService;
        _basePath = Path.Combine(env.ContentRootPath, _settings.UploadPath);
        if (!Directory.Exists(_basePath))
        {
            Directory.CreateDirectory(_basePath);
        }
    }

    public async Task<AttachmentResponse> UploadAsync(IFormFile file, int? threadId = null, int? userId = null)
    {
        ValidateFile(file);

        if (threadId.HasValue)
        {
            await VerifyThreadUploadAccessAsync(threadId.Value, userId);
        }

        var scanResult = await _virusScanService.ScanAsync(file.OpenReadStream(), file.FileName, file.ContentType);
        if (!scanResult.IsClean)
        {
            throw new InvalidOperationException(scanResult.Message ?? "文件包含病毒或恶意内容");
        }

        var fileDir = Path.Combine(_basePath, DateTime.Now.ToString("yyyy/MM/dd"));
        Directory.CreateDirectory(fileDir);

        var fileName = Guid.NewGuid() + Path.GetExtension(file.FileName).ToLower();
        var filePath = Path.Combine(fileDir, fileName);

        using (var stream = new FileStream(filePath, FileMode.Create))
        {
            await file.CopyToAsync(stream);
        }

        var attachment = new Attachment
        {
            ThreadId = threadId,
            UserId = userId,
            FileName = SanitizeFileName(file.FileName),
            StoredFileName = fileName,
            MimeType = file.ContentType,
            FileDir = fileDir.Replace(_basePath, string.Empty).TrimStart(Path.DirectorySeparatorChar).Replace(Path.DirectorySeparatorChar, '/'),
            Size = file.Length,
            Type = GetAttachmentType(file.ContentType),
            Embedded = IsEmbeddedImage(file.ContentType)
        };

        await _unitOfWork.Attachments.AddAsync(attachment);
        await _unitOfWork.SaveChangesAsync();

        _logger.LogInformation("附件上传成功: Id={Id}, FileName={FileName}, Size={Size}", attachment.Id, attachment.FileName, attachment.Size);

        return ToResponse(attachment);
    }

    public async Task<List<AttachmentResponse>> UploadMultipleAsync(List<IFormFile> files, int? threadId = null, int? userId = null)
    {
        if (files == null || files.Count == 0)
        {
            throw new InvalidOperationException("未选择文件");
        }

        var responses = new List<AttachmentResponse>();
        var errors = new List<string>();
        foreach (var file in files)
        {
            try
            {
                var response = await UploadAsync(file, threadId, userId);
                responses.Add(response);
            }
            catch (Exception ex)
            {
                _logger.LogWarning("附件上传失败: FileName={FileName}, Error={Error}", file.FileName, ex.Message);
                errors.Add($"{file.FileName}: {ex.Message}");
            }
        }

        if (responses.Count == 0 && errors.Count > 0)
        {
            throw new InvalidOperationException(string.Join("\n", errors));
        }

        return responses;
    }

    public async Task<AttachmentResponse> UploadFromBase64Async(string base64Content, string fileName, string mimeType, int? threadId = null, int? userId = null)
    {
        if (string.IsNullOrWhiteSpace(base64Content))
        {
            throw new InvalidOperationException("Base64内容不能为空");
        }
        if (string.IsNullOrWhiteSpace(fileName))
        {
            throw new InvalidOperationException("文件名不能为空");
        }

        var sanitizedFileName = SanitizeFileName(fileName);
        ValidateFileName(sanitizedFileName);

        if (threadId.HasValue)
        {
            await VerifyThreadUploadAccessAsync(threadId.Value, userId);
        }

        var cleanBase64 = base64Content.Trim();
        if (cleanBase64.StartsWith("data:", StringComparison.OrdinalIgnoreCase))
        {
            var commaIndex = cleanBase64.IndexOf(',');
            if (commaIndex > 0)
            {
                cleanBase64 = cleanBase64.Substring(commaIndex + 1);
            }
        }

        byte[] fileContent;
        try
        {
            fileContent = Convert.FromBase64String(cleanBase64);
        }
        catch (FormatException)
        {
            throw new InvalidOperationException("无效的Base64格式");
        }

        ValidateSize(fileContent.Length);

        using var memoryStream = new MemoryStream(fileContent);
        var scanResult = await _virusScanService.ScanAsync(memoryStream, sanitizedFileName, mimeType);
        if (!scanResult.IsClean)
        {
            throw new InvalidOperationException(scanResult.Message ?? "文件包含病毒或恶意内容");
        }

        var fileDir = Path.Combine(_basePath, DateTime.Now.ToString("yyyy/MM/dd"));
        Directory.CreateDirectory(fileDir);

        var extension = Path.GetExtension(sanitizedFileName).ToLower();
        if (string.IsNullOrWhiteSpace(extension))
        {
            extension = GetExtensionFromMimeType(mimeType);
        }
        var storedFileName = Guid.NewGuid() + extension;
        var filePath = Path.Combine(fileDir, storedFileName);

        await File.WriteAllBytesAsync(filePath, fileContent);

        var attachment = new Attachment
        {
            ThreadId = threadId,
            UserId = userId,
            FileName = sanitizedFileName,
            StoredFileName = storedFileName,
            MimeType = mimeType,
            FileDir = fileDir.Replace(_basePath, string.Empty).TrimStart(Path.DirectorySeparatorChar).Replace(Path.DirectorySeparatorChar, '/'),
            Size = fileContent.Length,
            Type = GetAttachmentType(mimeType),
            Embedded = IsEmbeddedImage(mimeType)
        };

        await _unitOfWork.Attachments.AddAsync(attachment);
        await _unitOfWork.SaveChangesAsync();

        _logger.LogInformation("Base64附件上传成功: Id={Id}, FileName={FileName}, Size={Size}", attachment.Id, attachment.FileName, attachment.Size);

        return ToResponse(attachment);
    }

    public async Task<(byte[] Content, string MimeType, string FileName)> DownloadAsync(int id, int? userId = null, bool isAdmin = false)
    {
        var attachment = await _unitOfWork.Attachments.GetByIdAsync(id);
        if (attachment == null)
        {
            throw new AttachmentException(AttachmentErrorCode.NotFound, "附件不存在");
        }

        await VerifyAttachmentAccessInternalAsync(attachment, userId, isAdmin);

        var filePath = GetSafeFilePath(attachment);
        if (!File.Exists(filePath))
        {
            throw new AttachmentException(AttachmentErrorCode.NotFound, "文件已丢失");
        }

        var content = await File.ReadAllBytesAsync(filePath);
        return (content, attachment.MimeType, SanitizeFileName(attachment.FileName));
    }

    private async Task VerifyAttachmentAccessInternalAsync(Attachment attachment, int? userId, bool isAdmin)
    {
        if (isAdmin)
        {
            return;
        }

        if (userId.HasValue && attachment.UserId == userId.Value)
        {
            return;
        }

        if (attachment.ThreadId.HasValue)
        {
            var thread = await _unitOfWork.Threads.GetByIdAsync(attachment.ThreadId.Value);
            if (thread != null)
            {
                var hasAccess = await VerifyConversationAccessInternalAsync(thread.ConversationId, userId, isAdmin);
                if (hasAccess)
                {
                    return;
                }
            }
        }

        throw new AttachmentException(AttachmentErrorCode.AccessDenied, "无权访问此附件");
    }

    /// <summary>
    /// 验证当前用户是否有权访问指定会话（IDOR 防护）。
    /// 管理员、会话所属用户、会话创建者、关注者、有邮箱权限的用户有权访问。
    /// </summary>
    private async Task<bool> VerifyConversationAccessInternalAsync(int conversationId, int? userId, bool isAdmin)
    {
        if (isAdmin)
        {
            return true;
        }

        if (!userId.HasValue)
        {
            return false;
        }

        var conversation = await _unitOfWork.Conversations.GetByIdAsync(conversationId);
        if (conversation == null)
        {
            return false;
        }

        if (conversation.UserId == userId || conversation.CreatedByUserId == userId)
        {
            return true;
        }

        var isFollower = (await _unitOfWork.Repository<Follower>()
            .FindAsync(f => f.ConversationId == conversation.Id && f.UserId == userId.Value))
            .Any();
        if (isFollower)
        {
            return true;
        }

        var hasMailboxAccess = (await _unitOfWork.Repository<MailboxUser>()
            .FindAsync(mu => mu.MailboxId == conversation.MailboxId && mu.UserId == userId.Value))
            .Any();
        if (hasMailboxAccess)
        {
            return true;
        }

        return false;
    }

    public async Task<bool> VerifyAttachmentAccessAsync(int attachmentId, int? userId, bool isAdmin)
    {
        var attachment = await _unitOfWork.Attachments.GetByIdAsync(attachmentId);
        if (attachment == null)
        {
            return false;
        }

        try
        {
            await VerifyAttachmentAccessInternalAsync(attachment, userId, isAdmin);
            return true;
        }
        catch (InvalidOperationException)
        {
            return false;
        }
    }

    public async Task<bool> VerifyConversationAccessAsync(int conversationId, int? userId, bool isAdmin)
    {
        return await VerifyConversationAccessInternalAsync(conversationId, userId, isAdmin);
    }

    public async Task<(byte[] Content, string MimeType)> GetPreviewAsync(int id, int maxWidth = 300, int maxHeight = 300, int? userId = null, bool isAdmin = false)
    {
        var attachment = await _unitOfWork.Attachments.GetByIdAsync(id);
        if (attachment == null)
        {
            throw new AttachmentException(AttachmentErrorCode.NotFound, "附件不存在");
        }

        await VerifyAttachmentAccessInternalAsync(attachment, userId, isAdmin);

        if (!IsImage(attachment.MimeType))
        {
            throw new AttachmentException(AttachmentErrorCode.NotImage, "仅图片类型支持预览");
        }

        var filePath = GetSafeFilePath(attachment);
        if (!File.Exists(filePath))
        {
            throw new AttachmentException(AttachmentErrorCode.NotFound, "文件已丢失");
        }

        using var image = Image.Load(filePath);
        image.Mutate(x => x.Resize(new ResizeOptions
        {
            Size = new Size(maxWidth, maxHeight),
            Mode = ResizeMode.Max
        }));

        using var stream = new MemoryStream();
        image.SaveAsJpeg(stream);
        return (stream.ToArray(), "image/jpeg");
    }

    public async Task<bool> DeleteAsync(int id, int? userId = null, bool isAdmin = false)
    {
        var attachment = await _unitOfWork.Attachments.GetByIdAsync(id);
        if (attachment == null)
        {
            return false;
        }

        await VerifyAttachmentAccessInternalAsync(attachment, userId, isAdmin);

        try
        {
            var filePath = GetSafeFilePath(attachment);
            if (File.Exists(filePath))
            {
                File.Delete(filePath);
            }

            await _unitOfWork.Attachments.DeleteAsync(attachment);
            await _unitOfWork.SaveChangesAsync();

            _logger.LogInformation("附件删除成功: Id={Id}", id);
            return true;
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "附件删除失败: Id={Id}", id);
            return false;
        }
    }

    public async Task<bool> DeleteByThreadIdAsync(int threadId, int? userId = null, bool isAdmin = false)
    {
        // 先验证用户是否有权访问该线程关联的会话
        var thread = await _unitOfWork.Threads.GetByIdAsync(threadId);
        if (thread == null)
        {
            return false;
        }

        if (!isAdmin && !await VerifyConversationAccessInternalAsync(thread.ConversationId, userId, isAdmin))
        {
            throw new AttachmentException(AttachmentErrorCode.AccessDenied, "无权访问此会话的附件");
        }

        var attachments = await _unitOfWork.Attachments.GetByThreadIdAsync(threadId);
        foreach (var attachment in attachments)
        {
            await DeleteAsync(attachment.Id, userId, isAdmin);
        }
        return true;
    }

    public async Task<bool> DeleteByConversationIdAsync(int conversationId, int? userId = null, bool isAdmin = false)
    {
        if (!isAdmin && !await VerifyConversationAccessInternalAsync(conversationId, userId, isAdmin))
        {
            throw new AttachmentException(AttachmentErrorCode.AccessDenied, "无权访问此会话的附件");
        }

        var attachments = await _unitOfWork.Attachments.GetByConversationIdAsync(conversationId);
        foreach (var attachment in attachments)
        {
            await DeleteAsync(attachment.Id, userId, isAdmin);
        }
        return true;
    }

    public async Task<AttachmentResponse?> GetAttachmentAsync(int id, int? userId = null, bool isAdmin = false)
    {
        var attachment = await _unitOfWork.Attachments.GetByIdAsync(id);
        if (attachment == null)
        {
            return null;
        }

        await VerifyAttachmentAccessInternalAsync(attachment, userId, isAdmin);

        return ToResponse(attachment);
    }

    public async Task<List<AttachmentResponse>> GetAttachmentsByThreadIdAsync(int threadId, int? userId = null, bool isAdmin = false)
    {
        // IDOR 防护：验证用户是否有权访问该线程关联的会话
        var thread = await _unitOfWork.Threads.GetByIdAsync(threadId);
        if (thread == null)
        {
            return new List<AttachmentResponse>();
        }

        if (!isAdmin && !await VerifyConversationAccessInternalAsync(thread.ConversationId, userId, isAdmin))
        {
            throw new AttachmentException(AttachmentErrorCode.AccessDenied, "无权访问此会话的附件");
        }

        var attachments = await _unitOfWork.Attachments.GetByThreadIdAsync(threadId);
        return attachments.Select(ToResponse).ToList();
    }

    public async Task<List<AttachmentResponse>> GetAttachmentsByConversationIdAsync(int conversationId, int? userId = null, bool isAdmin = false)
    {
        if (!isAdmin && !await VerifyConversationAccessInternalAsync(conversationId, userId, isAdmin))
        {
            throw new AttachmentException(AttachmentErrorCode.AccessDenied, "无权访问此会话的附件");
        }

        var attachments = await _unitOfWork.Attachments.GetByConversationIdAsync(conversationId);
        return attachments.Select(ToResponse).ToList();
    }

    public async Task<List<AttachmentResponse>> GetAttachmentsByUserIdAsync(int userId, int? requesterId = null, bool isAdmin = false)
    {
        // IDOR 防护：非管理员只能查看自己的附件
        if (!isAdmin && requesterId.HasValue && requesterId.Value != userId)
        {
            throw new AttachmentException(AttachmentErrorCode.AccessDenied, "无权查看其他用户的附件");
        }

        var attachments = await _unitOfWork.Attachments.GetByUserIdAsync(userId);
        return attachments.Select(ToResponse).ToList();
    }

    public bool IsAllowedExtension(string fileName)
    {
        var ext = Path.GetExtension(fileName)?.ToLower() ?? string.Empty;
        return _settings.AllowedExtensions.Any(e => ext.EndsWith(e.ToLower()));
    }

    public bool IsAllowedMimeType(string mimeType)
    {
        return _settings.AllowedMimeTypes.Any(m => mimeType.StartsWith(m));
    }

    public bool IsWithinSizeLimit(long fileSize)
    {
        return fileSize <= _settings.MaxSize;
    }

    private void ValidateFile(IFormFile file)
    {
        if (file == null || file.Length == 0)
        {
            throw new InvalidOperationException("未选择文件");
        }

        if (!IsWithinSizeLimit(file.Length))
        {
            throw new InvalidOperationException($"文件大小超过限制（最大 {FormatSize(_settings.MaxSize)}）");
        }

        if (!IsAllowedExtension(file.FileName))
        {
            throw new InvalidOperationException("文件类型不允许");
        }

        if (!IsAllowedMimeType(file.ContentType))
        {
            throw new InvalidOperationException("文件 MIME 类型不允许");
        }
    }

    private string GetSafeFilePath(Attachment attachment)
    {
        var storedName = !string.IsNullOrEmpty(attachment.StoredFileName)
            ? attachment.StoredFileName
            : attachment.FileName;

        var cleanFileDir = (attachment.FileDir ?? string.Empty).Trim().Replace('\\', '/');
        var cleanStoredName = Path.GetFileName(storedName) ?? string.Empty;

        var fullPath = Path.GetFullPath(Path.Combine(_basePath, cleanFileDir, cleanStoredName));
        var normalizedBasePath = Path.GetFullPath(_basePath);

        if (!fullPath.StartsWith(normalizedBasePath, StringComparison.OrdinalIgnoreCase))
        {
            throw new InvalidOperationException("无效的文件路径");
        }

        return fullPath;
    }

    private static string SanitizeFileName(string fileName)
    {
        if (string.IsNullOrWhiteSpace(fileName))
        {
            return "unnamed";
        }

        var name = Path.GetFileName(fileName) ?? fileName;

        var invalidChars = Path.GetInvalidFileNameChars();
        foreach (var c in invalidChars)
        {
            name = name.Replace(c, '_');
        }

        name = name.Trim('.', ' ');

        if (string.IsNullOrEmpty(name))
        {
            name = "unnamed";
        }

        if (name.Length > 255)
        {
            var ext = Path.GetExtension(name);
            var baseName = Path.GetFileNameWithoutExtension(name);
            if (baseName.Length > 200)
            {
                baseName = baseName[..200];
            }
            name = baseName + ext;
        }

        return name;
    }

    private async Task VerifyThreadUploadAccessAsync(int threadId, int? userId)
    {
        var thread = await _unitOfWork.Threads.GetByIdAsync(threadId);
        if (thread == null)
        {
            throw new InvalidOperationException("指定的线程不存在");
        }

        var hasAccess = await VerifyConversationAccessInternalAsync(thread.ConversationId, userId, false);
        if (!hasAccess)
        {
            throw new AttachmentException(AttachmentErrorCode.AccessDenied, "无权向此线程上传附件");
        }
    }

    private static bool IsImage(string mimeType)
    {
        return mimeType.StartsWith("image/");
    }

    private static bool IsEmbeddedImage(string mimeType)
    {
        return mimeType.StartsWith("image/inline-") || mimeType.StartsWith("image/svg+xml");
    }

    private static AttachmentType GetAttachmentType(string mimeType)
    {
        if (mimeType.StartsWith("text/")) return AttachmentType.Text;
        if (mimeType.StartsWith("image/")) return AttachmentType.Image;
        if (mimeType.StartsWith("audio/")) return AttachmentType.Audio;
        if (mimeType.StartsWith("video/")) return AttachmentType.Video;
        if (mimeType.StartsWith("application/")) return AttachmentType.Application;
        return AttachmentType.Other;
    }

    private static string FormatSize(long bytes)
    {
        if (bytes < 1024) return $"{bytes} B";
        if (bytes < 1024 * 1024) return $"{bytes / 1024:F1} KB";
        return $"{bytes / (1024 * 1024):F1} MB";
    }

    private void ValidateFileName(string fileName)
    {
        if (!IsAllowedExtension(fileName))
        {
            throw new InvalidOperationException("文件类型不允许");
        }
    }

    private void ValidateSize(long fileSize)
    {
        if (!IsWithinSizeLimit(fileSize))
        {
            throw new InvalidOperationException($"文件大小超过限制（最大 {FormatSize(_settings.MaxSize)}）");
        }
    }

    private string GetExtensionFromMimeType(string mimeType)
    {
        return mimeType switch
        {
            "image/jpeg" => ".jpg",
            "image/png" => ".png",
            "image/gif" => ".gif",
            "image/bmp" => ".bmp",
            "application/pdf" => ".pdf",
            "text/plain" => ".txt",
            "application/msword" => ".doc",
            "application/vnd.openxmlformats-officedocument.wordprocessingml.document" => ".docx",
            "application/vnd.ms-excel" => ".xls",
            "application/vnd.openxmlformats-officedocument.spreadsheetml.sheet" => ".xlsx",
            "application/zip" => ".zip",
            _ => ".bin"
        };
    }

    private AttachmentResponse ToResponse(Attachment attachment)
    {
        return new AttachmentResponse
        {
            Id = attachment.Id,
            ThreadId = attachment.ThreadId,
            FileName = attachment.FileName,
            MimeType = attachment.MimeType,
            Type = (int)attachment.Type,
            Size = attachment.Size,
            Embedded = attachment.Embedded,
            Url = $"/api/attachments/{attachment.Id}/download",
            CreatedAt = attachment.CreatedAt
        };
    }
}
