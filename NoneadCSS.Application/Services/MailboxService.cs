using Microsoft.Extensions.Caching.Memory;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Options;
using NoneadCSS.Application.Configuration;
using NoneadCSS.Application.DTOs.Requests;
using NoneadCSS.Application.DTOs.Responses;
using NoneadCSS.Application.Mappings;
using NoneadCSS.Domain;
using NoneadCSS.Domain.Entities;
using NoneadCSS.Domain.Enums;
using NoneadCSS.Domain.Events;
using Microsoft.EntityFrameworkCore;

namespace NoneadCSS.Application.Services;

public class MailboxService : IMailboxService
{
    private readonly IUnitOfWork _unitOfWork;
    private readonly IEmailService _emailService;
    private readonly IMemoryCache _cache;
    private readonly ISystemOptionService _systemOptionService;
    private readonly IServiceProvider _serviceProvider;
    private readonly IDomainEventDispatcher? _domainEventDispatcher;

    private const string CacheKeyAll = "mailboxes:all";
    private static string CacheKeyById(int id) => $"mailbox:{id}";
    private readonly TimeSpan _cacheExpiration;

    public MailboxService(IUnitOfWork unitOfWork, IEmailService emailService, IMemoryCache cache, ISystemOptionService systemOptionService, IOptions<CacheOptions> cacheOptions, IServiceProvider serviceProvider, IDomainEventDispatcher? domainEventDispatcher = null)
    {
        _unitOfWork = unitOfWork;
        _emailService = emailService;
        _cache = cache;
        _systemOptionService = systemOptionService;
        _cacheExpiration = TimeSpan.FromMinutes(cacheOptions.Value.MailboxCacheMinutes);
        _serviceProvider = serviceProvider;
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

    private void InvalidateMailboxCache(int? id = null)
    {
        _cache.Remove(CacheKeyAll);
        if (id.HasValue)
        {
            _cache.Remove(CacheKeyById(id.Value));
        }
    }

    public async Task<List<MailboxResponse>> GetMailboxesByUserIdAsync(int userId)
    {
        var mailboxes = await _unitOfWork.Mailboxes.GetByUserIdAsync(userId);
        var response = mailboxes.Select(m => m.ToResponse()).ToList();
        await PopulateStatsAsync(response);
        return response;
    }

    public async Task<MailboxResponse?> GetMailboxByIdAsync(int id)
    {
        var cacheKey = CacheKeyById(id);
        if (_cache.TryGetValue(cacheKey, out MailboxResponse? cached))
        {
            return cached;
        }

        var mailbox = await _unitOfWork.Mailboxes.GetByIdAsync(id);
        if (mailbox != null)
        {
            await EnsureDefaultFoldersAsync(mailbox);
        }
        var response = mailbox?.ToResponse();
        if (response != null)
        {
            await PopulateStatsAsync(new List<MailboxResponse> { response });
            _cache.Set(cacheKey, response, _cacheExpiration);
        }
        return response;
    }

    public async Task<bool> TestConnectionAsync(int id)
    {
        var mailbox = await _unitOfWork.Mailboxes.GetByIdAsync(id);
        if (mailbox == null)
        {
            return false;
        }

        return await _emailService.TestConnectionAsync(mailbox);
    }

    public async Task<(bool Success, string? ErrorMessage)> TestConnectionWithDetailsAsync(int id)
    {
        var mailbox = await _unitOfWork.Mailboxes.GetByIdAsync(id);
        if (mailbox == null)
        {
            return (false, "邮箱不存在");
        }

        return await _emailService.TestConnectionWithDetailsAsync(mailbox);
    }

    public async Task<List<MailboxResponse>> GetAllMailboxesAsync()
    {
        if (_cache.TryGetValue(CacheKeyAll, out List<MailboxResponse>? cached))
        {
            return cached ?? new List<MailboxResponse>();
        }

        var mailboxes = await _unitOfWork.Mailboxes.GetAllAsync();
        var response = mailboxes.Select(m => m.ToResponse()).ToList();
        await PopulateStatsAsync(response);
        _cache.Set(CacheKeyAll, response, _cacheExpiration);
        return response;
    }

    public async Task<MailboxResponse?> CreateMailboxAsync(CreateMailboxRequest request)
    {
        var existing = await _unitOfWork.Mailboxes.GetByEmailAsync(request.Email);
        if (existing != null)
        {
            return null;
        }

        var mailbox = new Mailbox
        {
            Name = request.Name,
            Email = request.Email,
            InHost = request.InHost,
            InPort = request.InPort,
            InEncryption = (EncryptionType)request.InEncryption,
            InValidateCert = request.InValidateCert,
            InUsername = request.InUsername,
            InPassword = request.InPassword,
            ImapSentFolder = request.ImapSentFolder,
            OutHost = request.OutHost,
            OutPort = request.OutPort,
            OutEncryption = (EncryptionType)request.OutEncryption,
            OutUsername = request.OutUsername,
            OutPassword = request.OutPassword,
            Aliases = request.Aliases,
            AliasesReply = request.AliasesReply,
            AutoBcc = request.AutoBcc ?? string.Empty,
            AutoReplyEnabled = request.AutoReplyEnabled,
            AutoReplySubject = request.AutoReplySubject,
            AutoReplyMessage = request.AutoReplyMessage,
            BeforeReply = request.BeforeReply,
            Signature = request.Signature,
            OauthEnabled = request.OauthEnabled,
            OauthProvider = request.OauthProvider,
            OauthAccessToken = request.OauthAccessToken,
            OauthRefreshToken = request.OauthRefreshToken,
            OauthTokenExpiresAt = request.OauthTokenExpiresAt,
            IsActive = request.IsActive,
            FetchFrequency = await GetDefaultFetchFrequencyAsync(request.FetchFrequency),
            CreatedAt = DateTime.UtcNow,
            UpdatedAt = DateTime.UtcNow
        };

        var folderTypes = new Dictionary<int, string>
        {
            [(int)FolderType.Unassigned] = "未分配",
            [(int)FolderType.Mine] = "我的会话",
            [(int)FolderType.Closed] = "已关闭"
        };

        foreach (var ft in folderTypes)
        {
            mailbox.Folders.Add(new Folder
            {
                Type = (FolderType)ft.Key,
                Name = ft.Value,
                ActiveCount = 0,
                TotalCount = 0
            });
        }

        if (request.UserIds != null && request.UserIds.Any())
        {
            // 一次性查询所有用户，避免 N+1
            var uniqueUserIds = request.UserIds.Distinct().ToList();
            var existingUsers = (await _unitOfWork.Users.GetByIdsAsync(uniqueUserIds))
                .Select(u => u.Id)
                .ToHashSet();

            foreach (var userId in uniqueUserIds)
            {
                // 仅添加真实存在的用户（防止引用不存在的 UserId）
                if (existingUsers.Contains(userId))
                {
                    mailbox.MailboxUsers.Add(new MailboxUser
                    {
                        UserId = userId,
                        Access = "{}"
                    });
                }
            }
        }

        await _unitOfWork.Mailboxes.AddAsync(mailbox);
        await _unitOfWork.SaveChangesAsync();

        InvalidateMailboxCache();
        return mailbox.ToResponse();
    }

    public async Task<MailboxResponse?> UpdateMailboxAsync(int id, CreateMailboxRequest request)
    {
        var mailbox = await _unitOfWork.Mailboxes.GetByIdTrackedAsync(id);
        if (mailbox == null)
        {
            return null;
        }

        var existing = await _unitOfWork.Mailboxes.GetByEmailAsync(request.Email);
        if (existing != null && existing.Id != id)
        {
            return null;
        }

        mailbox.Name = request.Name;
        mailbox.Email = request.Email;
        mailbox.InHost = request.InHost;
        mailbox.InPort = request.InPort;
        mailbox.InEncryption = (EncryptionType)request.InEncryption;
        mailbox.InValidateCert = request.InValidateCert;
        mailbox.InUsername = request.InUsername;
        mailbox.InPassword = string.IsNullOrEmpty(request.InPassword) ? mailbox.InPassword : request.InPassword;
        mailbox.ImapSentFolder = request.ImapSentFolder;
        mailbox.OutHost = request.OutHost;
        mailbox.OutPort = request.OutPort;
        mailbox.OutEncryption = (EncryptionType)request.OutEncryption;
        mailbox.OutUsername = request.OutUsername;
        mailbox.OutPassword = string.IsNullOrEmpty(request.OutPassword) ? mailbox.OutPassword : request.OutPassword;
        mailbox.Aliases = request.Aliases;
        mailbox.AliasesReply = request.AliasesReply;
        mailbox.AutoBcc = request.AutoBcc ?? string.Empty;
        mailbox.AutoReplyEnabled = request.AutoReplyEnabled;
        mailbox.AutoReplySubject = request.AutoReplySubject;
        mailbox.AutoReplyMessage = request.AutoReplyMessage;
        mailbox.BeforeReply = request.BeforeReply;
        mailbox.Signature = request.Signature;
        mailbox.OauthEnabled = request.OauthEnabled;
        mailbox.OauthProvider = request.OauthProvider;
        mailbox.OauthAccessToken = string.IsNullOrEmpty(request.OauthAccessToken) ? mailbox.OauthAccessToken : request.OauthAccessToken;
        mailbox.OauthRefreshToken = string.IsNullOrEmpty(request.OauthRefreshToken) ? mailbox.OauthRefreshToken : request.OauthRefreshToken;
        mailbox.OauthTokenExpiresAt = request.OauthTokenExpiresAt ?? mailbox.OauthTokenExpiresAt;
        mailbox.IsActive = request.IsActive;
        mailbox.FetchFrequency = request.FetchFrequency;
        mailbox.UpdatedAt = DateTime.UtcNow;

        if (request.UserIds != null)
        {
            // 确保 MailboxUsers 集合已初始化且不为空
            mailbox.MailboxUsers ??= new List<MailboxUser>();

            var currentUserIds = mailbox.MailboxUsers.Select(mu => mu.UserId).ToList();
            var usersToRemove = currentUserIds.Except(request.UserIds).ToList();
            foreach (var userId in usersToRemove)
            {
                var mu = mailbox.MailboxUsers.FirstOrDefault(x => x.UserId == userId);
                if (mu != null)
                {
                    mailbox.MailboxUsers.Remove(mu);
                }
            }

            // 仅添加真实存在的用户（避免 N+1 查询 + 防止引用不存在 UserId）
            var newUserIds = request.UserIds.Except(currentUserIds).Distinct().ToList();
            if (newUserIds.Any())
            {
                var existingUserIds = (await _unitOfWork.Users.GetByIdsAsync(newUserIds))
                    .Select(u => u.Id)
                    .ToHashSet();

                foreach (var userId in newUserIds)
                {
                    // 数据库级兜底校验：规避并发请求下内存集合过期、重复插入同一映射
                    // 触发 MailboxUsers.MailboxId+UserId 唯一键冲突（生产日志 2026-09-01 15:22:36）
                    if (existingUserIds.Contains(userId)
                        && !await _unitOfWork.Mailboxes.UserHasAccessAsync(mailbox.Id, userId))
                    {
                        mailbox.MailboxUsers.Add(new MailboxUser
                        {
                            UserId = userId,
                            Access = "{}"
                        });
                    }
                }
            }
        }

        // mailbox 已通过 GetByIdTrackedAsync 追踪，无需调用 UpdateAsync
        await _unitOfWork.SaveChangesAsync();

        // 发布强类型领域事件：由 UpdateMailboxCountersListener 排程 UpdateFolderCountersJob 重算该邮箱的文件夹计数。
        // 必须在保存之后发布：监听器用自己的 Scope 与 DbContext，读不到未提交的数据。
        await DispatchDomainEventAsync(new MailboxUpdatedEvent { MailboxId = id });

        InvalidateMailboxCache(id);
        return mailbox.ToResponse();
    }

    public async Task<bool> DeleteMailboxAsync(int id)
    {
        var mailbox = await _unitOfWork.Mailboxes.GetByIdTrackedAsync(id);
        if (mailbox == null || mailbox.DeletedAt != null)
        {
            return false;
        }

        mailbox.IsActive = false;
        mailbox.DeletedAt = DateTime.UtcNow;
        mailbox.UpdatedAt = DateTime.UtcNow;

        await _unitOfWork.SaveChangesAsync();

        InvalidateMailboxCache(id);
        return true;
    }

    public async Task<bool> ToggleActiveAsync(int id)
    {
        var mailbox = await _unitOfWork.Mailboxes.GetQueryable(false)
            .FirstOrDefaultAsync(m => m.Id == id && m.DeletedAt == null);
        if (mailbox == null)
        {
            return false;
        }

        mailbox.IsActive = !mailbox.IsActive;
        mailbox.UpdatedAt = DateTime.UtcNow;

        await _unitOfWork.SaveChangesAsync();

        InvalidateMailboxCache(id);
        return true;
    }

    public async Task<List<MailboxResponse>> GetDeletedMailboxesAsync()
    {
        var mailboxes = await _unitOfWork.Mailboxes.GetDeletedAsync();
        return mailboxes.Select(m => m.ToResponse()).ToList();
    }

    public async Task<MailboxResponse?> RestoreMailboxAsync(int id)
    {
        var mailbox = await _unitOfWork.Mailboxes.GetDeletedByIdAsync(id);
        if (mailbox == null)
        {
            return null;
        }

        mailbox.DeletedAt = null;
        mailbox.IsActive = true;
        mailbox.UpdatedAt = DateTime.UtcNow;

        // mailbox 已通过 GetDeletedByIdAsync 追踪（未使用 AsNoTracking），无需调用 UpdateAsync
        await _unitOfWork.SaveChangesAsync();

        InvalidateMailboxCache(id);
        return mailbox.ToResponse();
    }

    public async Task<bool> HardDeleteMailboxAsync(int id)
    {
        // 延迟解析（同一 Scoped 内同实例），避免构造期与 ICurrentUserAccessor / HttpContextCurrentUserAccessor
        // 之间形成循环依赖（ICurrentUserAccessor → IMailboxService → ICurrentUserAccessor）。
        // 生产环境 BuildServiceProvider 会执行 ValidateScopes 严格校验循环依赖。
        var currentUserAccessor = _serviceProvider.GetService<ICurrentUserAccessor>();
        if (currentUserAccessor != null && !currentUserAccessor.IsSuperAdmin)
        {
            return false;
        }
        var mailbox = await _unitOfWork.Mailboxes.GetDeletedByIdAsync(id);
        if (mailbox == null)
        {
            return false;
        }

        await _unitOfWork.Mailboxes.HardDeleteAsync(id);
        InvalidateMailboxCache(id);
        return true;
    }

    public async Task<int> ClearTrashAsync()
    {
        var deleted = await _unitOfWork.Mailboxes.GetDeletedAsync();
        var count = deleted.Count();
        await _unitOfWork.Mailboxes.HardDeleteAllDeletedAsync();
        InvalidateMailboxCache();
        return count;
    }

    public async Task<List<string>> GetImapFoldersAsync(int id)
    {
        var mailbox = await _unitOfWork.Mailboxes.GetByIdAsync(id);
        if (mailbox == null) return new List<string>();
        return await _emailService.GetImapFoldersAsync(mailbox);
    }

    public async Task<MailboxPermissionResponse?> GetMailboxPermissionsAsync(int mailboxId)
    {
        var mailbox = await _unitOfWork.Mailboxes.GetByIdAsync(mailboxId);
        if (mailbox == null) return null;

        return new MailboxPermissionResponse
        {
            MailboxId = mailbox.Id,
            MailboxName = mailbox.Name,
            MailboxEmail = mailbox.Email,
            UserPermissions = mailbox.MailboxUsers?.Select(mu => new MailboxUserPermission
            {
                UserId = mu.UserId,
                UserName = mu.User != null ? $"{mu.User.FirstName} {mu.User.LastName}" : string.Empty,
                UserEmail = mu.User?.Email ?? string.Empty,
                CanView = true,
                CanReply = true,
                CanDelete = false,
                CanManage = false
            }).ToList() ?? new()
        };
    }

    public async Task<MailboxPermissionResponse?> UpdateMailboxPermissionsAsync(int mailboxId, MailboxPermissionRequest request)
    {
        // 使用 tracking 查询（含 MailboxUsers Include），确保修改能持久化到数据库
        var mailbox = await _unitOfWork.Mailboxes.GetByIdTrackedAsync(mailboxId);
        if (mailbox == null) return null;

        foreach (var perm in request.UserPermissions)
        {
            var mu = mailbox.MailboxUsers.FirstOrDefault(x => x.UserId == perm.UserId);
            if (mu != null)
            {
                mu.Access = System.Text.Json.JsonSerializer.Serialize(new
                {
                    CanView = perm.CanView,
                    CanReply = perm.CanReply,
                    CanDelete = perm.CanDelete,
                    CanManage = perm.CanManage
                });
            }
        }

        await _unitOfWork.SaveChangesAsync();
        InvalidateMailboxCache(mailboxId);

        return await GetMailboxPermissionsAsync(mailboxId);
    }

    public async Task<MailboxAutoReplyResponse?> GetMailboxAutoReplyAsync(int mailboxId)
    {
        var mailbox = await _unitOfWork.Mailboxes.GetByIdAsync(mailboxId);
        if (mailbox == null) return null;

        return new MailboxAutoReplyResponse
        {
            MailboxId = mailbox.Id
        };
    }

    public async Task<MailboxAutoReplyResponse?> UpdateMailboxAutoReplyAsync(int mailboxId, MailboxAutoReplyRequest request)
    {
        // 使用 tracking 查询，确保修改能持久化到数据库
        var mailbox = await _unitOfWork.Mailboxes.GetByIdTrackedAsync(mailboxId);
        if (mailbox == null) return null;

        mailbox.AutoReplyEnabled = request.Enabled;
        mailbox.AutoReplySubject = request.Subject ?? string.Empty;
        mailbox.AutoReplyMessage = request.Body ?? string.Empty;

        await _unitOfWork.SaveChangesAsync();
        InvalidateMailboxCache(mailboxId);

        return await GetMailboxAutoReplyAsync(mailboxId);
    }

    public async Task<MailboxSignatureResponse?> GetMailboxSignatureAsync(int mailboxId)
    {
        var mailbox = await _unitOfWork.Mailboxes.GetByIdAsync(mailboxId);
        if (mailbox == null) return null;

        // 此前只回填 MailboxId，Signature / DefaultSignature 恒为空，
        // 导致前端「读取默认签名」永远拿不到内容（写进去的签名读不出来）。
        // 实体字段映射：Signature = 签名正文，BeforeReply = 默认签名（见 UpdateMailboxSignatureAsync）。
        return new MailboxSignatureResponse
        {
            MailboxId = mailbox.Id,
            Signature = mailbox.Signature ?? string.Empty,
            DefaultSignature = mailbox.BeforeReply ?? string.Empty,
            IsHtml = true
        };
    }

    public async Task<MailboxSignatureResponse?> UpdateMailboxSignatureAsync(int mailboxId, MailboxSignatureRequest request)
    {
        // 使用追踪查询获取实体，确保修改能持久化到数据库
        var mailbox = await _unitOfWork.Mailboxes.GetQueryable(false)
            .FirstOrDefaultAsync(m => m.Id == mailboxId);
        if (mailbox == null) return null;

        mailbox.Signature = request.Signature ?? string.Empty;
        mailbox.BeforeReply = request.DefaultSignature ?? string.Empty;

        await _unitOfWork.SaveChangesAsync();
        InvalidateMailboxCache(mailboxId);

        return await GetMailboxSignatureAsync(mailboxId);
    }

    /// <summary>
    /// 完成邮箱 OAuth 授权。
    /// </summary>
    /// <remarks>
    /// 该方法早先以"简化实现"方式写入伪造的 access/refresh token (形如 oauth-token-{Ticks})，
    /// 存在严重安全风险：攻击者只要拿到任意 code 即可绕过 OAuth 授权流程。
    /// 现版本已弃用该占位实现，调用方应改用 <c>OauthController.SaveOAuthToken</c> 完成标准 OAuth 流程。
    /// 保留签名仅为兼容旧调用，但写入操作被显式拒绝。
    /// </remarks>
    [Obsolete("请改用 OauthController.SaveOAuthToken 完成标准 OAuth 流程；本方法已禁用以避免伪造 token 写入。")]
    public Task<MailboxOAuthStatusResponse?> CompleteMailboxOAuthAsync(int mailboxId, string code, string redirectUri)
    {
        // 不再执行任何写入操作：直接返回 null，调用方需使用 OauthController 流程。
        // 该方法早期会写入伪 token (oauth-token-{Ticks})，已禁用以避免安全风险。
        return Task.FromResult<MailboxOAuthStatusResponse?>(null);
    }

    private async Task PopulateStatsAsync(List<MailboxResponse> mailboxes)
    {
        if (!mailboxes.Any()) return;

        var today = DateTime.UtcNow.Date;
        var tomorrow = today.AddDays(1);

        var conversationStats = await _unitOfWork.Conversations.GetStatusCountsByMailboxIdAsync();
        var sendStats = await _unitOfWork.SendLogs.GetStatsByMailboxAsync(today, tomorrow);

        var sendStatsDict = sendStats.ToDictionary(s => s.MailboxId);

        foreach (var mailbox in mailboxes)
        {
            if (conversationStats.TryGetValue(mailbox.Id, out var convStats))
            {
                mailbox.ConversationCount = convStats.Active + convStats.Pending + convStats.Closed;
                mailbox.OpenConversationCount = convStats.Active + convStats.Pending;
            }

            if (sendStatsDict.TryGetValue(mailbox.Id, out var stats))
            {
                mailbox.TodaySendCount = stats.TotalCount;
            }
        }
    }

    private static readonly (FolderType Type, string Name)[] DefaultFolderDefinitions =
    {
        (FolderType.Unassigned, "未分配"),
        (FolderType.Mine, "我的会话"),
        (FolderType.Closed, "已关闭")
    };

    private async Task EnsureDefaultFoldersAsync(Mailbox mailbox)
    {
        var existingTypes = mailbox.Folders?.Select(f => f.Type).ToHashSet() ?? new HashSet<FolderType>();
        var missingFolders = DefaultFolderDefinitions.Where(d => !existingTypes.Contains(d.Type)).ToList();

        if (missingFolders.Count == 0) return;

        foreach (var (type, name) in missingFolders)
        {
            mailbox.Folders ??= new List<Folder>();
            mailbox.Folders.Add(new Folder
            {
                Type = type,
                Name = name,
                ActiveCount = 0,
                TotalCount = 0
            });
        }

        await _unitOfWork.SaveChangesAsync();
        InvalidateMailboxCache(mailbox.Id);
    }

    private async Task<int> GetDefaultFetchFrequencyAsync(int requestedFrequency)
    {
        if (requestedFrequency != 5)
        {
            return requestedFrequency;
        }

        var systemFrequencyStr = await _systemOptionService.GetValueAsync("mail.fetch_frequency");
        if (int.TryParse(systemFrequencyStr, out var systemFrequency) && systemFrequency >= 1 && systemFrequency <= 60)
        {
            return systemFrequency;
        }

        return requestedFrequency;
    }
}
