namespace NoneadCSS.Infrastructure.Services;

/// <summary>
/// 附件相关的**可预期业务错误**（2026-09-27 批次 12 切片④a）。
///
/// 背景：控制器此前用 `ex.Message.Contains("无权访问")` / `Contains("仅图片类型")` 判断该回 403 还是 404/400，
/// 也就是**把中文异常文案当作控制流判据** —— 一旦把文案本地化（en 界面下文案变英文），这些 `Contains`
/// 就会静默失效、分支行为随之改变。现在改为按 <see cref="Code"/> 判定；<see cref="Exception.Message"/>
/// 只作日志/诊断用途，**不参与任何分支**。
///
/// 兼容性：继承 <see cref="InvalidOperationException"/>，因此既有的
/// `catch (InvalidOperationException)` 仍然捕获它 —— 本次改造不改变任何未涉及调用方的行为。
/// </summary>
public class AttachmentException : InvalidOperationException
{
    /// <summary>业务错误码（文案与它解耦，由 resx / DB 覆盖层提供）。</summary>
    public AttachmentErrorCode Code { get; }

    public AttachmentException(AttachmentErrorCode code, string? message = null)
        : base(message ?? code.ToString())
    {
        Code = code;
    }
}

/// <summary>附件业务错误码。</summary>
public enum AttachmentErrorCode
{
    /// <summary>附件不存在或文件已丢失</summary>
    NotFound,

    /// <summary>无权访问该附件（含附件 / 会话 / 用户 / 线程维度的越权）</summary>
    AccessDenied,

    /// <summary>仅图片类型支持预览</summary>
    NotImage,
}
