using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;
using NoneadCSS.Application.DTOs.Requests;
using NoneadCSS.Application.DTOs.Responses;
using NoneadCSS.Application.Mappings;
using NoneadCSS.Domain;
using NoneadCSS.Domain.Entities;
using NoneadCSS.Domain.Enums;
using NoneadCSS.Domain.Events;

namespace NoneadCSS.Application.Services;

public class UserService : IUserService
{
    private readonly IUnitOfWork _unitOfWork;
    private readonly IPasswordPolicyService _passwordPolicyService;
    private readonly IUserRoleResolver _userRoleResolver;
    private readonly ICurrentUserAccessor? _currentUserAccessor;
    private readonly ILogger<UserService> _logger;
    private readonly IWebhookService? _webhookService;
    private readonly IDomainEventDispatcher? _domainEventDispatcher;

    public UserService(IUnitOfWork unitOfWork, IPasswordPolicyService passwordPolicyService, IUserRoleResolver userRoleResolver, ICurrentUserAccessor? currentUserAccessor = null, ILogger<UserService>? logger = null, IWebhookService? webhookService = null, IDomainEventDispatcher? domainEventDispatcher = null)
    {
        _unitOfWork = unitOfWork;
        _passwordPolicyService = passwordPolicyService;
        _userRoleResolver = userRoleResolver;
        _currentUserAccessor = currentUserAccessor;
        _logger = logger ?? Microsoft.Extensions.Logging.Abstractions.NullLogger<UserService>.Instance;
        _webhookService = webhookService;
        _domainEventDispatcher = domainEventDispatcher;
    }

    /// <summary>
    /// 发布强类型领域事件。未注入分发器（如部分单元测试构造）时直接跳过；
    /// 分发失败由 DomainEventDispatcher 内部捕获并记录日志，不影响主流程。
    /// </summary>
    /// <typeparam name="TEvent">事件类型。</typeparam>
    /// <param name="event">事件实例。</param>
    private Task DispatchDomainEventAsync<TEvent>(TEvent @event) where TEvent : class
        => _domainEventDispatcher is null ? Task.CompletedTask : _domainEventDispatcher.DispatchAsync(@event);

    public async Task<List<UserResponse>> GetUsersByRoleAsync(int role)
    {
        var users = await _unitOfWork.Users.GetByRoleAsync(role);
        return users.Select(u => u.ToResponse()).ToList();
    }

    public async Task<UserPermissionsResponse?> GetUserPermissionsAsync(int userId)
    {
        var user = await _unitOfWork.Users.GetByIdAsync(userId);
        if (user == null) return null;

        return new UserPermissionsResponse
        {
            UserId = user.Id,
            Roles = new List<string> { user.Role.ToString() },
            CanViewAllConversations = user.Role == UserRoleType.SuperAdmin || user.Role == UserRoleType.AgentSupervisor,
            CanViewAssignedConversationsOnly = user.Role == UserRoleType.Agent || user.Role == UserRoleType.ReadOnly,
            CanCreateConversations = user.Role != UserRoleType.ReadOnly,
            CanEditConversations = user.Role != UserRoleType.ReadOnly,
            CanDeleteConversations = user.Role == UserRoleType.SuperAdmin || user.Role == UserRoleType.AgentSupervisor,
            CanManageMailboxes = user.Role == UserRoleType.SuperAdmin,
            CanManageUsers = user.Role == UserRoleType.SuperAdmin,
            CanManageSettings = user.Role == UserRoleType.SuperAdmin,
            CanViewReports = user.Role == UserRoleType.SuperAdmin || user.Role == UserRoleType.AgentSupervisor,
            CanExportData = user.Role == UserRoleType.SuperAdmin,
            AccessibleMailboxIds = user.MailboxUsers?.Select(mu => mu.MailboxId).ToList() ?? new()
        };
    }

    public async Task<UserPermissionsResponse?> UpdateUserPermissionsAsync(int userId, UserPermissionsRequest request)
    {
        var user = await _unitOfWork.Users.GetQueryable(false)
            .FirstOrDefaultAsync(u => u.Id == userId && u.DeletedAt == null);
        if (user == null) return null;

        if (request.Roles.Any())
        {
            if (Enum.TryParse<UserRoleType>(request.Roles.First(), out var role))
            {
                user.SetRole(role);
            }
        }

        await _unitOfWork.SaveChangesAsync();

        // 用户角色/权限已变更，使权限解析缓存失效（TD-05）
        _userRoleResolver.InvalidatePermissionsCache();

        return await GetUserPermissionsAsync(userId);
    }

    public async Task<UserNotificationSettingsResponse?> GetUserNotificationSettingsAsync(int userId)
    {
        var user = await _unitOfWork.Users.GetByIdAsync(userId);
        if (user == null) return null;

        return new UserNotificationSettingsResponse
        {
            UserId = user.Id,
            Locale = user.Locale ?? "zh-Hans",
            Timezone = user.Timezone ?? "Asia/Shanghai",
            EmailOnNewConversation = user.EmailOnNewConversation,
            EmailOnAssignedToMe = user.EmailOnAssignedToMe,
            EmailOnNewReply = user.EmailOnNewReply,
            EmailOnMention = user.EmailOnMention,
            BrowserOnNewConversation = user.BrowserOnNewConversation,
            BrowserOnAssignedToMe = user.BrowserOnAssignedToMe,
            BrowserOnNewReply = user.BrowserOnNewReply,
            BrowserOnMention = user.BrowserOnMention,
            DefaultSignature = user.DefaultSignature,
            UseGlobalSignature = user.UseGlobalSignature,
        };
    }

    public async Task<UserNotificationSettingsResponse?> UpdateUserNotificationSettingsAsync(int userId, UserNotificationSettingsRequest request)
    {
        var user = await _unitOfWork.Users.GetQueryable(false)
            .FirstOrDefaultAsync(u => u.Id == userId && u.DeletedAt == null);
        if (user == null) return null;

        user.UpdateNotificationSettings(
            request.Locale,
            request.Timezone,
            request.EmailOnNewConversation,
            request.EmailOnAssignedToMe,
            request.EmailOnNewReply,
            request.EmailOnMention,
            request.BrowserOnNewConversation,
            request.BrowserOnAssignedToMe,
            request.BrowserOnNewReply,
            request.BrowserOnMention,
            request.DefaultSignature,
            request.UseGlobalSignature);

        await _unitOfWork.SaveChangesAsync();

        return await GetUserNotificationSettingsAsync(userId);
    }

    public async Task UpdateUserLocaleAsync(int userId, string locale)
    {
        var user = await _unitOfWork.Users.GetQueryable(false)
            .FirstOrDefaultAsync(u => u.Id == userId && u.DeletedAt == null);
        if (user == null) return;

        user.Locale = locale;
        await _unitOfWork.SaveChangesAsync();
    }

    public async Task ActivateUserAsync(int userId)
    {
        var user = await _unitOfWork.Users.GetQueryable(false)
            .FirstOrDefaultAsync(u => u.Id == userId && u.DeletedAt == null);
        if (user == null) return;

        user.Activate();
        await _unitOfWork.SaveChangesAsync();

        // 发布强类型领域事件：由 ActivateUserListener 发「账号已激活」站内通知。
        // 激活动作已在上面完成，监听器不再重复激活。必须在保存之后发布：
        // 监听器用自己的 Scope 与 DbContext，读不到未提交的数据。
        await DispatchDomainEventAsync(new UserActivatedEvent { UserId = userId });
    }

    public async Task<bool> UpdateUserStatusAsync(int userId, UserStatus status)
    {
        var user = await _unitOfWork.Users.GetQueryable(false)
            .FirstOrDefaultAsync(u => u.Id == userId && u.DeletedAt == null);
        if (user == null) return false;

        user.SetStatus(status);
        await _unitOfWork.SaveChangesAsync();
        return true;
    }

    public async Task<bool> ResetPasswordAsync(int userId, string newPassword)
    {
        var user = await _unitOfWork.Users.GetQueryable(false)
            .FirstOrDefaultAsync(u => u.Id == userId && u.DeletedAt == null);
        if (user == null) return false;

        var validation = await _passwordPolicyService.ValidateAsync(newPassword);
        if (!validation.IsValid)
        {
            throw new InvalidOperationException(string.Join("；", validation.Errors));
        }

        user.Password = BCrypt.Net.BCrypt.HashPassword(newPassword);
        await _unitOfWork.SaveChangesAsync();
        return true;
    }

    public async Task<List<UserResponse>> GetAssignableUsersAsync()
    {
        // 在数据库层过滤 Active 用户，避免加载全部用户到内存后再过滤
        var users = await _unitOfWork.Users.FindAsync(u => u.Status == UserStatus.Active && u.DeletedAt == null);
        return users
            .Select(u => u.ToResponse())
            .ToList();
    }

    public async Task<(List<UserResponse> Items, int Total)> GetPagedUsersAsync(
        int page, int pageSize, string? keyword = null, int? status = null)
    {
        var (items, total) = await _unitOfWork.Users.GetPagedAsync(page, pageSize, keyword, status);
        return (items.Select(u => u.ToResponse()).ToList(), total);
    }

    public async Task<(int Total, int Active, int Disabled, int Pending)> GetStatsAsync()
    {
        return await _unitOfWork.Users.GetStatsAsync();
    }

    public async Task<UserResponse?> GetUserByIdAsync(int id)
    {
        var user = await _unitOfWork.Users.GetByIdAsync(id);
        return user?.ToResponse();
    }

    public async Task<UserResponse?> GetUserByEmailAsync(string email)
    {
        var user = await _unitOfWork.Users.GetByEmailAsync(email);
        return user?.ToResponse();
    }

    public async Task<UserResponse?> CreateUserAsync(CreateUserRequest request)
    {
        var existingUser = await _unitOfWork.Users.GetByEmailAsync(request.Email);
        if (existingUser != null)
        {
            return null;
        }

        var pwdValidation = await _passwordPolicyService.ValidateAsync(request.Password);
        if (!pwdValidation.IsValid)
        {
            throw new InvalidOperationException(string.Join("；", pwdValidation.Errors));
        }

        var user = new User
        {
            FirstName = request.FirstName,
            LastName = request.LastName,
            Email = request.Email,
            Password = BCrypt.Net.BCrypt.HashPassword(request.Password),
            Role = UserRoleType.ReadOnly,
            Status = UserStatus.Active,
            CreatedAt = DateTime.UtcNow,
            UpdatedAt = DateTime.UtcNow
        };

        if (request.MailboxIds != null && request.MailboxIds.Any())
        {
            var mailboxes = await _unitOfWork.Mailboxes.GetByIdsAsync(request.MailboxIds);
            var mailboxIdSet = new HashSet<int>(mailboxes.Select(m => m.Id));
            foreach (var mailboxId in request.MailboxIds)
            {
                if (mailboxIdSet.Contains(mailboxId))
                {
                    user.MailboxUsers.Add(new MailboxUser
                    {
                        MailboxId = mailboxId,
                        Access = "{}"
                    });
                }
            }
        }

        // 用户创建 + 角色分配需在单一事务中完成，保证原子性（M10）
        await using var createTx = await _unitOfWork.BeginTransactionAsync();
        try
        {
            await _unitOfWork.Users.AddAsync(user);
            await _unitOfWork.SaveChangesAsync();

            // 处理角色分配：1-4 为系统角色枚举值，>4 为 Roles 表中的自定义角色 ID
            // 需在用户保存后执行，以便获取 user.Id 用于 UserRoles 关联
            await AssignRoleToUserAsync(user, request.Role);
            await _unitOfWork.SaveChangesAsync();

            await createTx.CommitAsync();
        }
        catch
        {
            await createTx.SafeRollbackAsync();
            throw;
        }

        // 用户角色/权限已变更，使权限解析缓存失效（TD-05）
        _userRoleResolver.InvalidatePermissionsCache();

        // 触发 Webhook 通知（失败仅记录日志，不阻塞主流程）
        if (_webhookService != null)
        {
            try
            {
                await _webhookService.TriggerAsync("user_created", null, user.ToResponse(), "User", user.Id);
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "触发 Webhook 失败：Event={Event}, Entity={EntityType}:{EntityId}", "user_created", "User", user.Id);
            }
        }

        return user.ToResponse();
    }

    public async Task<UserResponse?> UpdateUserAsync(int id, UpdateUserRequest request, string? ifMatch = null)
    {
        var user = await _unitOfWork.Users.GetQueryable(false)
            .Include(u => u.MailboxUsers)
            .FirstOrDefaultAsync(u => u.Id == id && u.DeletedAt == null);
        if (user == null)
        {
            return null;
        }

        // M8: 乐观锁 If-Match 校验（HTTP 标准优先，Header 覆盖 Body）
        var effectiveIfMatch = !string.IsNullOrEmpty(ifMatch) ? ifMatch.Trim('"') : request.IfMatch;
        ConcurrencyCheckHelper.Check(user, user.RowVersion, effectiveIfMatch, "User", user.Id,
            () => user.ToResponse(), _logger);

        var existingUser = await _unitOfWork.Users.GetByEmailAsync(request.Email);
        if (existingUser != null && existingUser.Id != id)
        {
            return null;
        }

        user.FirstName = request.FirstName;
        user.LastName = request.LastName;
        user.Email = request.Email;
        user.UpdatedAt = DateTime.UtcNow;

        // 处理角色分配：1-4 为系统角色枚举值，>4 为 Roles 表中的自定义角色 ID
        await AssignRoleToUserAsync(user, request.Role);

        if (!string.IsNullOrEmpty(request.Password))
        {
            var pwdValidation = await _passwordPolicyService.ValidateAsync(request.Password);
            if (!pwdValidation.IsValid)
            {
                throw new InvalidOperationException(string.Join("；", pwdValidation.Errors));
            }
            user.Password = BCrypt.Net.BCrypt.HashPassword(request.Password);
        }

        if (request.MailboxIds != null)
        {
            var mailboxUsersList = user.MailboxUsers.ToList();
            var mailboxesToRemove = mailboxUsersList.Where(mu => !request.MailboxIds.Contains(mu.MailboxId)).ToList();
            foreach (var mu in mailboxesToRemove)
            {
                user.MailboxUsers.Remove(mu);
            }

            foreach (var mailboxId in request.MailboxIds)
            {
                // 内存集合 + 数据库级兜底校验：规避并发请求下重复插入同一映射
                // 触发 MailboxUsers.MailboxId+UserId 唯一键冲突（生产日志 2026-09-01 15:22:36）
                if (!user.MailboxUsers.Any(mu => mu.MailboxId == mailboxId)
                    && !await _unitOfWork.Mailboxes.UserHasAccessAsync(mailboxId, user.Id))
                {
                    user.MailboxUsers.Add(new MailboxUser
                    {
                        MailboxId = mailboxId,
                        Access = "{}"
                    });
                }
            }
        }

        await _unitOfWork.SaveChangesAsync();

        // 用户角色/权限已变更，使权限解析缓存失效（TD-05）
        _userRoleResolver.InvalidatePermissionsCache();

        // 触发 Webhook 通知（失败仅记录日志，不阻塞主流程）
        if (_webhookService != null)
        {
            try
            {
                await _webhookService.TriggerAsync("user_updated", null, user.ToResponse(), "User", user.Id);
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "触发 Webhook 失败：Event={Event}, Entity={EntityType}:{EntityId}", "user_updated", "User", user.Id);
            }
        }

        return user.ToResponse();
    }

    /// <summary>
    /// 处理角色分配：1-4 为系统角色枚举值（UserRoleType），>4 为 Roles 表中的自定义角色 ID。
    /// 系统角色直接设置 User.Role 字段；自定义角色设置 User.Role 为 Agent（默认基础角色），
    /// 并通过 UserRoles 关联表分配自定义角色。
    /// </summary>
    private async Task AssignRoleToUserAsync(User user, int roleValue)
    {
        if (roleValue >= 1 && roleValue <= 4)
        {
            user.Role = (UserRoleType)roleValue;
            return;
        }

        // 自定义角色：查找 Roles 表
        var role = await _unitOfWork.Roles.GetByIdAsync(roleValue);
        if (role == null || role.DeletedAt != null)
        {
            user.Role = UserRoleType.ReadOnly;
            return;
        }

        // 自定义角色对应的基础角色设为 Agent（客服专员），权限由自定义角色的 PermissionsJson 决定
        user.Role = UserRoleType.Agent;

        // 移除该用户已有的自定义角色关联（避免一个用户关联多个自定义角色）
        var existingUserRoles = await _unitOfWork.UserRoles.GetByUserIdAsync(user.Id);
        foreach (var ur in existingUserRoles)
        {
            await _unitOfWork.UserRoles.DeleteAsync(ur);
        }

        // 添加新的自定义角色关联
        await _unitOfWork.UserRoles.AddAsync(new UserRole
        {
            UserId = user.Id,
            RoleId = role.Id,
            MailboxId = null,
            CreatedAt = DateTime.UtcNow
        });
    }

    public async Task<(bool Success, string? ErrorMessage)> DeleteUserAsync(int id)
    {
        var user = await _unitOfWork.Users.GetQueryable(false)
            .FirstOrDefaultAsync(u => u.Id == id && u.DeletedAt == null);
        if (user == null)
        {
            return (false, null);
        }

        // 删除前检查是否有关联的活跃会话
        var conversationCount = await _unitOfWork.Conversations.CountActiveByUserIdAsync(id);
        if (conversationCount > 0)
        {
            return (false, $"该用户关联了 {conversationCount} 个会话，请先删除或转移这些会话后再删除用户");
        }

        user.Status = UserStatus.Deleted;
        user.DeletedAt = DateTime.UtcNow;
        user.UpdatedAt = DateTime.UtcNow;

        await _unitOfWork.SaveChangesAsync();

        return (true, null);
    }

    public async Task<List<UserResponse>> GetDeletedUsersAsync()
    {
        var users = await _unitOfWork.Users.GetDeletedAsync();
        return users.Select(u => u.ToResponse()).ToList();
    }

    public async Task<UserResponse?> RestoreUserAsync(int id)
    {
        // 使用追踪查询获取已删除用户（IgnoreQueryFilters 绕过软删除过滤器，
        // 不使用 AsNoTracking 以便后续修改直接被 SaveChangesAsync 持久化）
        var user = await _unitOfWork.Users.GetQueryable(false)
            .IgnoreQueryFilters()
            .FirstOrDefaultAsync(u => u.Id == id && u.DeletedAt != null);
        if (user == null)
        {
            return null;
        }

        // 恢复前检查邮箱是否已被其他活跃用户占用，避免唯一约束失败
        var hasEmailConflict = await _unitOfWork.Users.GetQueryable(false)
            .AnyAsync(u => u.Email == user.Email && u.DeletedAt == null && u.Id != user.Id);
        if (hasEmailConflict)
        {
            throw new InvalidOperationException("该邮箱已被其他活跃用户使用，无法恢复");
        }

        user.DeletedAt = null;
        user.Status = UserStatus.Active;
        user.UpdatedAt = DateTime.UtcNow;

        await _unitOfWork.SaveChangesAsync();

        return user.ToResponse();
    }

    public async Task<bool> HardDeleteUserAsync(int id)
    {
        // 第一层校验：服务层防御性权限校验（即使控制器已标 [RequireSuperAdmin]，
        // 仍需在服务层兜底，避免绕过控制器直接调用服务导致越权）
        if (_currentUserAccessor != null && !_currentUserAccessor.IsSuperAdmin)
        {
            return false;
        }

        // 第二层校验：记录必须在垃圾箱中（DeletedAt != null）
        var user = await _unitOfWork.Users.GetDeletedByIdAsync(id);
        if (user == null)
        {
            return false;
        }

        // 第二层附加校验：禁止硬删除当前登录用户自己（防止误操作锁死会话）
        if (_currentUserAccessor != null && _currentUserAccessor.UserId == id)
        {
            return false;
        }

        // 第二层附加校验：若待删除用户是 SuperAdmin，必须保证系统中至少保留 1 个活跃 SuperAdmin
        if (user.Role == UserRoleType.SuperAdmin)
        {
            var otherActiveSuperAdmins = await _unitOfWork.Users.GetQueryable(false)
                .IgnoreQueryFilters()
                .CountAsync(u => u.Id != id
                    && u.Role == UserRoleType.SuperAdmin
                    && u.Status == UserStatus.Active
                    && u.DeletedAt == null);
            if (otherActiveSuperAdmins == 0)
            {
                return false;
            }
        }

        await _unitOfWork.Users.HardDeleteAsync(id);
        return true;
    }

    public async Task<int> ClearTrashAsync()
    {
        // 第一层校验：服务层防御性权限校验（SuperAdmin 专属，控制器已标 [RequireSuperAdmin]）
        if (_currentUserAccessor != null && !_currentUserAccessor.IsSuperAdmin)
        {
            return 0;
        }

        var deleted = await _unitOfWork.Users.GetDeletedAsync();
        var usersToDelete = deleted.ToList();

        // 第二层校验：过滤掉会导致「最后一个活跃 SuperAdmin 被清空」的记录
        var safeToDelete = new List<User>();
        foreach (var user in usersToDelete)
        {
            if (user.Role == UserRoleType.SuperAdmin)
            {
                // 对每个 SuperAdmin 重新评估：排除当前已选入 safeToDelete 的其他 SuperAdmin
                var committedSuperAdminIds = safeToDelete
                    .Where(u => u.Role == UserRoleType.SuperAdmin)
                    .Select(u => u.Id)
                    .ToHashSet();
                var remainingActiveSuperAdmins = await _unitOfWork.Users.GetQueryable(false)
                    .IgnoreQueryFilters()
                    .CountAsync(u => !committedSuperAdminIds.Contains(u.Id)
                        && u.Id != user.Id
                        && u.Role == UserRoleType.SuperAdmin
                        && u.Status == UserStatus.Active
                        && u.DeletedAt == null);
                if (remainingActiveSuperAdmins == 0)
                {
                    // 保留：不能删除导致系统无活跃 SuperAdmin
                    continue;
                }
            }

            // 第二层校验：禁止删除当前登录用户
            if (_currentUserAccessor != null && _currentUserAccessor.UserId == user.Id)
            {
                continue;
            }

            safeToDelete.Add(user);
        }

        if (safeToDelete.Count == 0)
        {
            return 0;
        }

        // 逐条硬删除（仓储层 HardDeleteAllDeletedAsync 不支持按 ID 过滤，因此安全子集用循环删除）
        foreach (var user in safeToDelete)
        {
            await _unitOfWork.Users.HardDeleteAsync(user.Id);
        }
        return safeToDelete.Count;
    }

    public async Task<UserResponse?> UpdateProfileAsync(int userId, UpdateProfileRequest request)
    {
        var user = await _unitOfWork.Users.GetQueryable(false)
            .FirstOrDefaultAsync(u => u.Id == userId && u.DeletedAt == null);
        if (user == null)
        {
            return null;
        }

        user.FirstName = request.FirstName;
        user.LastName = request.LastName;

        if (!string.IsNullOrEmpty(request.Timezone))
        {
            user.Timezone = request.Timezone;
        }

        if (!string.IsNullOrEmpty(request.Locale))
        {
            user.Locale = request.Locale;
        }

        // 头像字段：显式赋值以支持 null 清除，并校验协议白名单防止 XSS
        if (string.IsNullOrWhiteSpace(request.Avatar))
        {
            user.Avatar = null;
        }
        else
        {
            var avatarErr = CustomerService.ValidateAndNormalizeAvatarUrl(request.Avatar, out var normalizedAvatar);
            if (avatarErr != null)
            {
                throw new InvalidOperationException(avatarErr);
            }
            user.Avatar = normalizedAvatar;
        }

        user.UpdatedAt = DateTime.UtcNow;

        await _unitOfWork.SaveChangesAsync();

        return user.ToResponse();
    }

    public async Task<bool> ChangePasswordAsync(int userId, ChangePasswordRequest request)
    {
        if (request.NewPassword != request.ConfirmPassword)
        {
            return false;
        }

        var user = await _unitOfWork.Users.GetQueryable(false)
            .FirstOrDefaultAsync(u => u.Id == userId && u.DeletedAt == null);
        if (user == null)
        {
            return false;
        }

        // BCrypt.Net-Next 在存储哈希为空时报 "Invalid salt"，短路返回 false
        if (string.IsNullOrEmpty(user.Password) || string.IsNullOrEmpty(request.CurrentPassword))
        {
            return false;
        }

        if (!BCrypt.Net.BCrypt.Verify(request.CurrentPassword, user.Password))
        {
            return false;
        }

        var pwdValidation = await _passwordPolicyService.ValidateAsync(request.NewPassword);
        if (!pwdValidation.IsValid)
        {
            throw new InvalidOperationException(string.Join("；", pwdValidation.Errors));
        }

        user.Password = BCrypt.Net.BCrypt.HashPassword(request.NewPassword);
        user.UpdatedAt = DateTime.UtcNow;

        await _unitOfWork.SaveChangesAsync();

        // 发布强类型领域事件：由 SendPasswordChangedListener 发「密码已修改」站内通知 + 安全提醒邮件。
        // 这是改密成功通知的唯一实现（AuthService 里的 SendSystemNotificationAsync 是「忘记密码发重置链接」，
        // 与本事件无关）。必须在保存之后发布：监听器用自己的 Scope 与 DbContext，读不到未提交的数据。
        await DispatchDomainEventAsync(new PasswordChangedEvent { User = user });

        return true;
    }
}
