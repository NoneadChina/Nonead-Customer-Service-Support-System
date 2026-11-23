using NoneadCSS.Domain.Entities;

namespace NoneadCSS.Domain.Events;

/// <summary>
/// 用户登录成功事件（由 AuthService.LoginAsync 在保存登录计数之后发布）
/// 监听器：RememberUserLocaleListener —— 把本次登录使用的界面语言回写 Users.Locale
/// </summary>
public class UserLoggedInEvent
{
    public int UserId { get; set; }
    /// <summary>本次登录请求携带的界面语言（优先取 LoginRequest.Locale，其次取请求文化），为空时监听器跳过回写。</summary>
    public string? Locale { get; set; }
    public string? IpAddress { get; set; }
    public string? UserAgent { get; set; }
    public DateTime LoginAt { get; set; } = DateTime.UtcNow;
}

/// <summary>
/// 密码变更事件（由 UserService.ChangePasswordAsync 在新哈希保存之后发布）
/// 监听器：SendPasswordChangedListener —— 站内通知 + 安全提醒邮件
/// </summary>
public class PasswordChangedEvent
{
    public User User { get; set; } = null!;
    public string? IpAddress { get; set; }
    public string? UserAgent { get; set; }
}

/// <summary>
/// 密码重置事件
/// 监听器：LogPasswordResetListener —— 写入 Reset / ResetFailed 审计
/// 注意：当前没有发布点（重置密码走 AuthController → AuditActionFilter 的通用动词兜底），详见事件接线清单。
/// </summary>
public class PasswordResetEvent
{
    public int UserId { get; set; }
    public string? Email { get; set; }
    public bool Success { get; set; }
    public string? IpAddress { get; set; }
    public string? UserAgent { get; set; }
}

/// <summary>
/// 登录失败事件（由 AuthService.LoginAsync 各失败分支发布）
/// 监听器：LogFailedLoginListener —— 写入 action="loginfailed" 的审计，供仪表盘「异常事件数」统计
/// </summary>
public class LoginFailedEvent
{
    /// <summary>用户不存在时为 null。</summary>
    public int? UserId { get; set; }
    public string? Email { get; set; }
    public string? Reason { get; set; }
    public string? IpAddress { get; set; }
    public string? UserAgent { get; set; }
}

/// <summary>
/// 账号锁定事件（由 AuthService.LoginAsync 在失败次数达到阈值触发锁定时发布）
/// 监听器：LogLockoutListener —— 写入 action="lockout" 的审计，供仪表盘「异常事件数」统计
/// </summary>
public class LockoutEvent
{
    public int UserId { get; set; }
    public string? Email { get; set; }
    public string? IpAddress { get; set; }
    public string? UserAgent { get; set; }
}

/// <summary>
/// 用户激活事件（由 UserService.ActivateUserAsync 在激活保存之后发布）
/// 监听器：ActivateUserListener —— 仅发「账号已激活」站内通知（激活动作本身已在服务层完成）
/// </summary>
public class UserActivatedEvent
{
    public int UserId { get; set; }
    public string? IpAddress { get; set; }
}

/// <summary>
/// 邮箱配置更新事件（由 MailboxService.UpdateMailboxAsync 在保存之后发布）
/// 监听器：UpdateMailboxCountersListener —— 排程 UpdateFolderCountersJob 重算文件夹计数
/// </summary>
public class MailboxUpdatedEvent
{
    public int MailboxId { get; set; }
    public string? IpAddress { get; set; }
}

/// <summary>
/// 会话状态变更事件（由 ConversationStatusService.UpdateStatusAsync 在事务提交之后发布）
/// 监听器：ConversationStatusChangedListener —— 仅发站内通知（审计与 SignalR 已在过滤器/服务内联）
/// </summary>
public class ConversationStatusChangedEvent
{
    public Conversation Conversation { get; set; } = null!;
    public string OldStatus { get; set; } = string.Empty;
    public string NewStatus { get; set; } = string.Empty;
    public int? ChangedByUserId { get; set; }
    public string? IpAddress { get; set; }
}

/// <summary>
/// 会话归属（受理人）变更事件（由 ConversationAssignmentService.AssignAsync 在事务提交之后发布）
/// 监听器：ConversationUserChangedListener —— 仅发站内通知（审计与 SignalR 已在过滤器/服务内联）
/// </summary>
public class ConversationUserChangedEvent
{
    public Conversation Conversation { get; set; } = null!;
    public int? OldUserId { get; set; }
    public int? NewUserId { get; set; }
    public int? ChangedByUserId { get; set; }
    public string? IpAddress { get; set; }
}

/// <summary>
/// 坐席回复事件（由 ConversationThreadService.AddReplyAsync 在回复保存之后发布，备注不算回复）
/// 监听器：UserRepliedListener —— 给会话关注者发站内通知（审计 action="reply" 已由 AuditActionFilter 覆盖）
/// </summary>
public class UserRepliedEvent
{
    public Conversation Conversation { get; set; } = null!;
    /// <summary>本次回复对应的会话线程（ConversationThread，而非 System.Threading.Thread）。</summary>
    public ConversationThread Thread { get; set; } = null!;
    public User User { get; set; } = null!;
    public string? IpAddress { get; set; }
}
