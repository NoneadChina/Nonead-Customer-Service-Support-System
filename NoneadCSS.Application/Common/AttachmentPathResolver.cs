using System.IO;

namespace NoneadCSS.Application.Common;

/// <summary>
/// 附件根目录统一解析器（T000108 / T000112）。
///
/// 背景：上传侧 AttachmentService 以 ContentRoot 锚定（Path.Combine(ContentRootPath, UploadPath)），
/// 而部分读取方（钉钉/微信/飞书/WhatsApp 发送与收取）历史实现直接使用
/// configuration["Attachment:UploadPath"] ?? "Data/Attachments"，在配置缺失或为相对路径时
/// 会回退到"进程 CWD 相对路径"。生产后端 CWD 为 Production 而 ContentRoot 为 Production\backend，
/// 两者不同导致"上传成功但读取文件不存在"（客户收不到图片）。
///
/// 统一规则：
///   - UploadPath 为绝对路径 → 原样使用（生产环境：D:/.../Production/backend/Data/Attachments）
///   - 相对路径或缺失 → 锚定 ContentRootPath（与上传侧完全一致）
/// </summary>
public static class AttachmentPathResolver
{
    /// <summary>默认相对目录（与 AttachmentSettings/上传侧保持一致）。</summary>
    public const string DefaultRelativePath = "Data/Attachments";

    public static string ResolveBasePath(string? uploadPath, string contentRootPath)
    {
        if (!string.IsNullOrWhiteSpace(uploadPath) && Path.IsPathRooted(uploadPath))
        {
            return uploadPath.TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar);
        }

        return Path.Combine(
            contentRootPath ?? string.Empty,
            string.IsNullOrWhiteSpace(uploadPath) ? DefaultRelativePath : uploadPath);
    }
}
