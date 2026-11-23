using System.Text.Json;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Caching.Memory;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using NoneadCSS.Application.Configuration;
using NoneadCSS.Application.DTOs.Responses;
using NoneadCSS.Domain;
using NoneadCSS.Domain.Entities;

namespace NoneadCSS.Application.Services;

public class RoleService : IRoleService
{
    private readonly IUnitOfWork _unitOfWork;
    private readonly IUserRoleResolver _userRoleResolver;
    private readonly IMemoryCache _cache;
    private readonly TimeSpan _cacheDuration;
    private readonly ICurrentUserAccessor? _currentUserAccessor;
    private readonly ILogger<RoleService> _logger;

    public RoleService(IUnitOfWork unitOfWork, IUserRoleResolver userRoleResolver, IMemoryCache cache, IOptions<CacheOptions> cacheOptions, ICurrentUserAccessor? currentUserAccessor = null, ILogger<RoleService>? logger = null)
    {
        _unitOfWork = unitOfWork;
        _userRoleResolver = userRoleResolver;
        _cache = cache;
        _cacheDuration = TimeSpan.FromMinutes(cacheOptions.Value.DefaultCacheMinutes);
        _currentUserAccessor = currentUserAccessor;
        _logger = logger ?? Microsoft.Extensions.Logging.Abstractions.NullLogger<RoleService>.Instance;
    }

    private void InvalidateRoleCache(int? roleId = null)
    {
        _cache.Remove("Role:Overview");
        if (roleId.HasValue)
        {
            _cache.Remove($"Role:ById:{roleId.Value}");
        }
    }

    public async Task<RoleDto?> GetByIdAsync(int id)
    {
        var cacheKey = $"Role:ById:{id}";
        return await _cache.GetOrCreateAsync(cacheKey, async entry =>
        {
            entry.AbsoluteExpirationRelativeToNow = _cacheDuration;
            var role = await _unitOfWork.Roles.GetByIdAsync(id);
            if (role == null) return null;
            var userCount = await _userRoleResolver.CountUsersInRoleAsync(role.Id);
            return MapToDto(role, userCount);
        });
    }

    public async Task<(List<RoleDto> Items, int Total)> GetPagedAsync(RoleQuery query)
    {
        var (items, total) = await _unitOfWork.Roles.GetPagedAsync(
            query.Page,
            query.PageSize,
            query.Keyword);
        var roles = items.ToList();
        var roleIds = roles.Select(r => r.Id).ToList();
        var userCounts = await _userRoleResolver.CountUsersInRolesAsync(roleIds);
        var dtos = roles.Select(r => MapToDto(r, userCounts.GetValueOrDefault(r.Id, 0))).ToList();
        return (dtos, total);
    }

    public async Task<RoleOverviewResponse> GetOverviewAsync()
    {
        var cacheKey = "Role:Overview";
        var result = await _cache.GetOrCreateAsync(cacheKey, async entry =>
        {
            entry.AbsoluteExpirationRelativeToNow = _cacheDuration;
            var stats = await _unitOfWork.Roles.GetStatsAsync();
            var allRoles = await _unitOfWork.Roles.GetAllAsync();
            var roleIds = allRoles.Select(r => r.Id).ToList();
            var userCounts = await _userRoleResolver.CountUsersInRolesAsync(roleIds);

            var usersPerRole = new Dictionary<string, int>();
            var roleDtos = new List<RoleDto>();

            foreach (var role in allRoles)
            {
                var userCount = userCounts.GetValueOrDefault(role.Id, 0);
                usersPerRole[role.Name] = userCount;
                roleDtos.Add(MapToDto(role, userCount));
            }

            var totalAssignments = usersPerRole.Values.Sum();

            return new RoleOverviewResponse
            {
                TotalRoles = stats.GetValueOrDefault("total", 0),
                SystemRoles = stats.GetValueOrDefault("system", 0),
                CustomRoles = stats.GetValueOrDefault("custom", 0),
                TotalRoleAssignments = totalAssignments,
                Roles = roleDtos,
                UsersPerRole = usersPerRole,
                PermissionGroups = GetAllPermissionGroups(),
            };
        });

        return result ?? throw new InvalidOperationException("角色概览缓存工厂返回 null，属于异常状态。");
    }

    public async Task<RoleDto> CreateAsync(CreateRoleRequest request)
    {
        var existing = await _unitOfWork.Roles.GetBySlugAsync(request.Slug);
        if (existing != null)
            throw new InvalidOperationException("角色标识已存在");

        var role = new Role
        {
            Name = request.Name,
            Slug = request.Slug.ToLowerInvariant().Replace(" ", "-"),
            Description = request.Description,
            PermissionsJson = JsonSerializer.Serialize(request.Permissions),
            IsSystem = false,
            SortOrder = request.SortOrder,
        };

        await _unitOfWork.Roles.AddAsync(role);
        await _unitOfWork.SaveChangesAsync();

        InvalidateRoleCache();
        _userRoleResolver.InvalidatePermissionsCache();

        return MapToDto(role, 0);
    }

    public async Task<RoleDto?> UpdateAsync(UpdateRoleRequest request)
    {
        // 使用追踪查询获取实体，确保修改能持久化到数据库
        var role = await _unitOfWork.Roles.GetQueryable(false)
            .FirstOrDefaultAsync(r => r.Id == request.Id);
        if (role == null) return null;

        // 系统角色的权限由种子数据定义，不允许通过 API 修改权限和标识
        if (role.IsSystem)
        {
            role.Description = request.Description;
            role.SortOrder = request.SortOrder;
            role.UpdatedAt = DateTime.UtcNow;
        }
        else
        {
            role.Name = request.Name;
            role.Description = request.Description;
            role.PermissionsJson = JsonSerializer.Serialize(request.Permissions);
            role.SortOrder = request.SortOrder;
            role.UpdatedAt = DateTime.UtcNow;
        }

        await _unitOfWork.SaveChangesAsync();

        InvalidateRoleCache(role.Id);
        _userRoleResolver.InvalidatePermissionsCache();

        var userCount = await _userRoleResolver.CountUsersInRoleAsync(role.Id);
        return MapToDto(role, userCount);
    }

    public async Task<bool> DeleteAsync(int id)
    {
        var role = await _unitOfWork.Roles.GetByIdTrackedAsync(id);
        if (role == null || role.DeletedAt != null) return false;
        if (role.IsSystem)
            throw new InvalidOperationException("系统角色不可删除");

        role.DeletedAt = DateTime.UtcNow;
        role.UpdatedAt = DateTime.UtcNow;
        await _unitOfWork.SaveChangesAsync();

        InvalidateRoleCache(id);
        _userRoleResolver.InvalidatePermissionsCache();

        return true;
    }

    public async Task<bool> AssignRoleAsync(AssignRoleRequest request)
    {
        var existing = await _unitOfWork.UserRoles.GetByUserAndRoleAsync(
            request.UserId, request.RoleId, request.MailboxId);
        if (existing != null) return true;

        var userRole = new UserRole
        {
            UserId = request.UserId,
            RoleId = request.RoleId,
            MailboxId = request.MailboxId,
        };

        try
        {
            await _unitOfWork.UserRoles.AddAsync(userRole);
            await _unitOfWork.SaveChangesAsync();
        }
        catch (DbUpdateException ex) when (IsUniqueViolation(ex))
        {
            // S2: 并发重复分配——唯一索引兜底，幂等视为成功（另一请求已创建同一关联）
            _logger.LogInformation("角色分配并发冲突（已存在），视为幂等成功：UserId={UserId}, RoleId={RoleId}, MailboxId={MailboxId}",
                request.UserId, request.RoleId, request.MailboxId);
            return true;
        }

        InvalidateRoleCache(request.RoleId);
        _userRoleResolver.InvalidatePermissionsCache();

        return true;
    }

    public async Task<bool> BatchAssignRoleAsync(BatchAssignRoleRequest request)
    {
        using var transaction = await _unitOfWork.BeginTransactionAsync();
        try
        {
            // 批量查询已存在的用户角色关联，避免 N+1 查询（原先每个 userId 都查一次）
            var existingUserIds = await _unitOfWork.UserRoles.GetQueryable()
                .Where(ur => ur.RoleId == request.RoleId
                    && ur.MailboxId == request.MailboxId
                    && request.UserIds.Contains(ur.UserId))
                .Select(ur => ur.UserId)
                .ToListAsync();

            var existingSet = new HashSet<int>(existingUserIds);
            foreach (var userId in request.UserIds)
            {
                if (existingSet.Contains(userId)) continue;

                await _unitOfWork.UserRoles.AddAsync(new UserRole
                {
                    UserId = userId,
                    RoleId = request.RoleId,
                    MailboxId = request.MailboxId,
                });
            }
            await _unitOfWork.SaveChangesAsync();
            await transaction.CommitAsync();
        }
        catch (DbUpdateException ex) when (IsUniqueViolation(ex))
        {
            // S2: 并发重复分配——唯一索引兜底，幂等视为成功（冲突行已由并发请求创建）
            await transaction.SafeRollbackAsync();
            _logger.LogInformation("批量角色分配并发冲突（部分已存在），视为幂等成功：RoleId={RoleId}, MailboxId={MailboxId}, Count={Count}",
                request.RoleId, request.MailboxId, request.UserIds.Count);
        }
        catch
        {
            await transaction.SafeRollbackAsync();
            throw;
        }

        InvalidateRoleCache(request.RoleId);
        _userRoleResolver.InvalidatePermissionsCache();

        return true;
    }

    /// <summary>
    /// 判断 DbUpdateException 是否由唯一约束冲突引起（SQLite 和 SQL Server 兼容）。
    /// </summary>
    private static bool IsUniqueViolation(DbUpdateException ex)
    {
        var msg = (ex.InnerException?.Message ?? ex.Message)?.ToLowerInvariant();
        if (string.IsNullOrEmpty(msg)) return false;
        // SQLite: "UNIQUE constraint failed"
        // SQL Server: "Cannot insert duplicate key row" / "Violation of UNIQUE KEY constraint"
        return msg.Contains("unique constraint")
               || msg.Contains("duplicate key")
               || msg.Contains("violation of unique");
    }

    public async Task<bool> RemoveRoleAssignmentAsync(int userRoleId)
    {
        var userRole = await _unitOfWork.UserRoles.GetByIdAsync(userRoleId);
        if (userRole == null) return false;

        await _unitOfWork.UserRoles.DeleteAsync(userRole);
        await _unitOfWork.SaveChangesAsync();

        InvalidateRoleCache(userRole.RoleId);
        _userRoleResolver.InvalidatePermissionsCache();

        return true;
    }

    public Task<bool> UserHasPermissionAsync(int userId, string permission, int? mailboxId = null)
    {
        return _userRoleResolver.HasPermissionAsync(userId, permission, mailboxId);
    }

    public Task<List<string>> GetUserPermissionsAsync(int userId, int? mailboxId = null)
    {
        return _userRoleResolver.GetPermissionsAsync(userId, mailboxId);
    }

    public async Task<List<RoleDto>> GetUserRolesAsync(int userId, int? mailboxId = null)
    {
        var roles = await _unitOfWork.Roles.GetByUserIdAsync(userId, mailboxId);
        var dtos = new List<RoleDto>();
        foreach (var role in roles)
        {
            dtos.Add(MapToDto(role, 0));
        }
        return dtos;
    }

    public async Task<List<RoleUserDto>> GetRoleUsersAsync(int roleId)
    {
        var role = await _unitOfWork.Roles.GetByIdAsync(roleId);
        if (role == null) return new List<RoleUserDto>();

        var members = await _userRoleResolver.GetUsersInRoleAsync(roleId);
        return members.Select(m => new RoleUserDto
        {
            UserId = m.UserId,
            UserName = m.UserName,
            UserEmail = m.UserEmail,
            RoleId = role.Id,
            RoleName = role.Name,
            MailboxId = m.MailboxId,
            MailboxName = m.MailboxName,
            CreatedAt = m.CreatedAt,
        }).ToList();
    }

    public List<PermissionGroup> GetAllPermissionGroups()
    {
        return new List<PermissionGroup>
        {
            new()
            {
                Key = "conversations",
                Label = "会话管理",
                Permissions = new List<PermissionItem>
                {
                    new() { Key = "conversations.view", Label = "查看会话", Description = "查看会话列表和详情" },
                    new() { Key = "conversations.create", Label = "创建会话", Description = "创建新会话" },
                    new() { Key = "conversations.update", Label = "编辑会话", Description = "编辑会话属性和内容" },
                    new() { Key = "conversations.delete", Label = "删除会话", Description = "删除会话" },
                    new() { Key = "conversations.reply", Label = "回复会话", Description = "回复客户" },
                    new() { Key = "conversations.assign", Label = "分配会话", Description = "分配/转移会话给其他用户" },
                    new() { Key = "conversations.manage_tags", Label = "管理标签", Description = "添加/移除会话标签" },
                    new() { Key = "conversations.view_all", Label = "查看所有会话", Description = "查看所有邮箱的所有会话" },
                }
            },
            new()
            {
                Key = "customers",
                Label = "客户管理",
                Permissions = new List<PermissionItem>
                {
                    new() { Key = "customers.view", Label = "查看客户", Description = "查看客户列表和详情" },
                    new() { Key = "customers.create", Label = "创建客户", Description = "创建新客户" },
                    new() { Key = "customers.update", Label = "编辑客户", Description = "编辑客户信息" },
                    new() { Key = "customers.delete", Label = "删除客户", Description = "删除客户" },
                    new() { Key = "customers.export", Label = "导出客户", Description = "导出客户数据" },
                    new() { Key = "customers.import", Label = "导入客户", Description = "批量导入客户" },
                }
            },
            new()
            {
                Key = "mailboxes",
                Label = "邮箱管理",
                Permissions = new List<PermissionItem>
                {
                    new() { Key = "mailboxes.view", Label = "查看邮箱", Description = "查看邮箱配置" },
                    new() { Key = "mailboxes.create", Label = "创建邮箱", Description = "创建新邮箱" },
                    new() { Key = "mailboxes.update", Label = "编辑邮箱", Description = "编辑邮箱配置" },
                    new() { Key = "mailboxes.delete", Label = "删除邮箱", Description = "删除邮箱" },
                    new() { Key = "mailboxes.manage", Label = "管理邮箱设置", Description = "管理邮箱的所有设置" },
                }
            },
            new()
            {
                Key = "users",
                Label = "用户管理",
                Permissions = new List<PermissionItem>
                {
                    new() { Key = "users.view", Label = "查看用户", Description = "查看用户列表和详情" },
                    new() { Key = "users.create", Label = "创建用户", Description = "创建新用户" },
                    new() { Key = "users.update", Label = "编辑用户", Description = "编辑用户信息" },
                    new() { Key = "users.delete", Label = "删除用户", Description = "删除用户" },
                    new() { Key = "users.manage_roles", Label = "管理角色", Description = "分配/移除用户角色" },
                }
            },
            new()
            {
                Key = "profile",
                Label = "个人资料",
                Permissions = new List<PermissionItem>
                {
                    new() { Key = "profile.update", Label = "编辑个人资料", Description = "编辑自己的个人资料（昵称、头像等）" },
                    new() { Key = "profile.change_password", Label = "修改密码", Description = "修改自己的登录密码" },
                }
            },
            new()
            {
                Key = "settings",
                Label = "系统设置",
                Permissions = new List<PermissionItem>
                {
                    new() { Key = "settings.view", Label = "查看设置", Description = "查看系统设置" },
                    new() { Key = "settings.update", Label = "修改设置", Description = "修改系统设置" },
                    new() { Key = "settings.manage", Label = "管理设置", Description = "全面管理系统设置" },
                }
            },
            new()
            {
                Key = "reports",
                Label = "报表统计",
                Permissions = new List<PermissionItem>
                {
                    new() { Key = "reports.view", Label = "查看报表", Description = "查看统计报表" },
                    new() { Key = "reports.export", Label = "导出报表", Description = "导出报表数据" },
                    new() { Key = "reports.manage", Label = "管理报表", Description = "创建和管理自定义报表" },
                }
            },
            new()
            {
                Key = "custom_reports",
                Label = "自定义报表",
                Permissions = new List<PermissionItem>
                {
                    new() { Key = "custom_reports.create", Label = "创建报表", Description = "创建自定义报表" },
                    new() { Key = "custom_reports.update", Label = "编辑报表", Description = "编辑自定义报表配置" },
                    new() { Key = "custom_reports.delete", Label = "删除报表", Description = "删除自定义报表" },
                    new() { Key = "custom_reports.execute", Label = "执行报表", Description = "执行报表查询与预览" },
                    new() { Key = "custom_reports.schedules.manage", Label = "管理报表计划", Description = "创建和编辑报表定时发送计划" },
                }
            },
            new()
            {
                Key = "automation",
                Label = "自动化配置",
                Permissions = new List<PermissionItem>
                {
                    new() { Key = "workflows.view", Label = "查看工作流", Description = "查看工作流配置" },
                    new() { Key = "workflows.manage", Label = "管理工作流", Description = "创建和编辑工作流" },
                    new() { Key = "sla.view", Label = "查看 SLA", Description = "查看 SLA 策略" },
                    new() { Key = "sla.manage", Label = "管理 SLA", Description = "创建和编辑 SLA 策略" },
                    new() { Key = "macros.view", Label = "查看宏", Description = "查看宏配置" },
                    new() { Key = "macros.manage", Label = "管理宏", Description = "创建和编辑宏" },
                    new() { Key = "webhooks.view", Label = "查看 Webhook", Description = "查看 Webhook 配置" },
                    new() { Key = "webhooks.manage", Label = "管理 Webhook", Description = "创建和编辑 Webhook" },
                    new() { Key = "custom_fields.view", Label = "查看自定义字段", Description = "查看自定义字段" },
                    new() { Key = "custom_fields.manage", Label = "管理自定义字段", Description = "创建和编辑自定义字段" },
                }
            },
            new()
            {
                Key = "teams",
                Label = "团队管理",
                Permissions = new List<PermissionItem>
                {
                    new() { Key = "teams.view", Label = "查看团队", Description = "查看团队列表和详情" },
                    new() { Key = "teams.create", Label = "创建团队", Description = "创建新团队" },
                    new() { Key = "teams.update", Label = "编辑团队", Description = "编辑团队信息" },
                    new() { Key = "teams.delete", Label = "删除团队", Description = "删除团队" },
                    new() { Key = "teams.manage_members", Label = "管理团队成员", Description = "添加/移除团队成员" },
                }
            },
            new()
            {
                Key = "time_tracking",
                Label = "工时管理",
                Permissions = new List<PermissionItem>
                {
                    new() { Key = "time-tracking.view", Label = "查看工时", Description = "查看工时记录、概览与报表" },
                    new() { Key = "time-tracking.manage", Label = "管理工时", Description = "记录和编辑工时，开始/停止计时器" },
                }
            },
            new()
            {
                Key = "content",
                Label = "内容管理",
                Permissions = new List<PermissionItem>
                {
                    new() { Key = "canned_responses.view", Label = "查看快捷回复", Description = "查看快捷回复模板" },
                    new() { Key = "canned_responses.manage", Label = "管理快捷回复", Description = "创建和编辑快捷回复" },
                    new() { Key = "email_signatures.view", Label = "查看邮件签名", Description = "查看邮件签名配置" },
                    new() { Key = "email_signatures.manage", Label = "管理邮件签名", Description = "创建和编辑邮件签名" },
                    new() { Key = "mail_templates.view", Label = "查看邮件模板", Description = "查看邮件模板" },
                    new() { Key = "mail_templates.manage", Label = "管理邮件模板", Description = "创建和编辑邮件模板" },
                    new() { Key = "tags.view", Label = "查看标签", Description = "查看标签列表" },
                    new() { Key = "tags.manage", Label = "管理标签", Description = "创建和编辑标签" },
                    new() { Key = "knowledge.view", Label = "查看知识库", Description = "查看知识库文章" },
                    new() { Key = "knowledge.manage", Label = "管理知识库", Description = "创建和编辑知识库文章" },
                }
            },
            new()
            {
                Key = "attachments",
                Label = "附件管理",
                Permissions = new List<PermissionItem>
                {
                    new() { Key = "attachments.manage", Label = "管理附件", Description = "删除会话、消息线程和用户的附件" },
                }
            },
            new()
            {
                Key = "communication",
                Label = "沟通管理",
                Permissions = new List<PermissionItem>
                {
                    new() { Key = "chat.view", Label = "查看聊天", Description = "查看在线聊天" },
                    new() { Key = "chat.reply", Label = "回复聊天", Description = "回复在线聊天" },
                    new() { Key = "calendar.view", Label = "查看日历", Description = "查看日历事件" },
                    new() { Key = "calendar.manage", Label = "管理日历", Description = "创建和编辑日历事件" },
                    new() { Key = "wechat.view", Label = "查看微信", Description = "查看微信聊天配置和会话" },
                    new() { Key = "wechat.manage", Label = "管理微信", Description = "配置微信公众号接入和回复消息" },
                    new() { Key = "whatsapp.view", Label = "查看 WhatsApp", Description = "查看 WhatsApp 配置和会话" },
                    new() { Key = "whatsapp.manage", Label = "管理 WhatsApp", Description = "配置 WhatsApp 接入和回复消息" },
                    new() { Key = "feishu.view", Label = "查看飞书", Description = "查看飞书配置和会话" },
                    new() { Key = "feishu.manage", Label = "管理飞书", Description = "配置飞书接入和回复消息" },
                    new() { Key = "dingding.view", Label = "查看钉钉", Description = "查看钉钉配置和会话" },
                    new() { Key = "dingding.manage", Label = "管理钉钉", Description = "配置钉钉接入和回复消息" },
                    new() { Key = "messenger.view", Label = "查看 Messenger", Description = "查看 Facebook Messenger 配置和会话" },
                    new() { Key = "messenger.manage", Label = "管理 Messenger", Description = "配置 Facebook Messenger 接入和回复消息" },
                    new() { Key = "telegram.view", Label = "查看 Telegram", Description = "查看 Telegram Bot 配置和会话" },
                    new() { Key = "telegram.manage", Label = "管理 Telegram", Description = "配置 Telegram Bot 接入和回复消息" },
                    new() { Key = "line.view", Label = "查看 LINE", Description = "查看 LINE Official Account 配置和会话" },
                    new() { Key = "line.manage", Label = "管理 LINE", Description = "配置 LINE Messaging API 接入和回复消息" },
                    new() { Key = "imessage.view", Label = "查看 iMessage", Description = "查看 iMessage 接入配置和会话" },
                    new() { Key = "imessage.manage", Label = "管理 iMessage", Description = "配置 iMessage 接入和回复消息" },
                }
            },
            new()
            {
                Key = "system",
                Label = "系统管理",
                Permissions = new List<PermissionItem>
                {
                    new() { Key = "api_keys.view", Label = "查看 API 密钥", Description = "查看 API 密钥列表" },
                    new() { Key = "api_keys.manage", Label = "管理 API 密钥", Description = "创建和管理 API 密钥" },
                    new() { Key = "import_export.view", Label = "查看导入导出", Description = "查看导入导出功能" },
                    new() { Key = "import_export.manage", Label = "管理导入导出", Description = "执行数据导入导出" },
                    new() { Key = "system_tools.view", Label = "查看系统工具", Description = "查看系统工具" },
                    new() { Key = "system_tools.manage", Label = "管理系统工具", Description = "使用系统工具" },
                    new() { Key = "localization.view", Label = "查看翻译", Description = "查看翻译管理" },
                    new() { Key = "localization.manage", Label = "管理翻译", Description = "编辑翻译内容" },
                    new() { Key = "modules.view", Label = "查看模块", Description = "查看功能模块" },
                    new() { Key = "modules.manage", Label = "管理模块", Description = "启用/禁用功能模块" },
                }
            },
            new()
            {
                Key = "audit",
                Label = "审计与日志",
                Permissions = new List<PermissionItem>
                {
                    new() { Key = "audit_logs.view", Label = "查看审计日志", Description = "查看审计日志" },
                    new() { Key = "send_logs.view", Label = "查看发送日志", Description = "查看邮件发送日志" },
                    new() { Key = "notifications.view", Label = "查看通知", Description = "查看通知列表" },
                    new() { Key = "notifications.manage", Label = "管理通知", Description = "管理通知设置和模板" },
                    new() { Key = "satisfaction.view", Label = "查看满意度", Description = "查看满意度评价" },
                }
            }
        };
    }

    private static List<string> ParsePermissions(string json)
    {
        if (string.IsNullOrEmpty(json)) return new List<string>();
        try
        {
            return JsonSerializer.Deserialize<List<string>>(json) ?? new List<string>();
        }
        catch
        {
            return new List<string>();
        }
    }

    private static RoleDto MapToDto(Role role, int userCount)
    {
        return new RoleDto
        {
            Id = role.Id,
            Name = role.Name,
            Slug = role.Slug,
            Description = role.Description,
            Permissions = ParsePermissions(role.PermissionsJson),
            IsSystem = role.IsSystem,
            SortOrder = role.SortOrder,
            UserCount = userCount,
            CreatedAt = role.CreatedAt,
            UpdatedAt = role.UpdatedAt,
            DeletedAt = role.DeletedAt,
        };
    }

    public async Task<(IEnumerable<RoleDto> Items, int Total)> GetTrashAsync(int page, int pageSize, string? keyword = null)
    {
        var (items, total) = await _unitOfWork.Roles.GetDeletedAsync(page, pageSize, keyword);
        var roles = items.ToList();
        var roleIds = roles.Select(r => r.Id).ToList();
        var userCounts = await _userRoleResolver.CountUsersInRolesAsync(roleIds);
        var dtos = roles.Select(r => MapToDto(r, userCounts.GetValueOrDefault(r.Id, 0))).ToList();
        return (dtos, total);
    }

    public async Task<int> GetTrashCountAsync()
    {
        return await _unitOfWork.Roles.CountDeletedAsync();
    }

    public async Task<bool> RestoreAsync(int id)
    {
        var result = await _unitOfWork.Roles.RestoreAsync(id);
        if (result)
        {
            InvalidateRoleCache(id);
            _userRoleResolver.InvalidatePermissionsCache();
        }
        return result;
    }

    public async Task<bool> HardDeleteAsync(int id)
    {
        if (_currentUserAccessor != null && !_currentUserAccessor.IsSuperAdmin)
        {
            return false;
        }
        return await _unitOfWork.Roles.HardDeleteAsync(id);
    }

    public async Task<int> ClearTrashAsync()
    {
        // 权限校验：仅 SuperAdmin 可清空垃圾箱，与 HardDeleteAsync 保持一致
        if (_currentUserAccessor != null && !_currentUserAccessor.IsSuperAdmin)
        {
            return 0;
        }
        return await _unitOfWork.Roles.HardDeleteAllDeletedAsync();
    }

}
