using System.Text.Json;
using NoneadCSS.Domain.Entities;
using NoneadCSS.Application.DTOs.Requests;
using NoneadCSS.Application.DTOs.Responses;

namespace NoneadCSS.Application.Mappings;

/// <summary>
/// ToDto JSON 反序列化异常的降级记录器。
/// 不依赖 ILogger 注入（扩展方法签名保持静态稳定）。
/// 在 Debug/ASPNETCORE_ENVIRONMENT=Development 时回落到 System.Console；
/// 否则丢弃仅作为诊断线索，不影响主流程。
/// </summary>
internal static class MappingLog
{
    internal static void WriteWarning(Exception ex, string message)
    {
        try
        {
            // 仅在开发环境或 DEBUG 构建下写控制台，生产环境保持安静，避免污染 stdout 聚合日志。
#if DEBUG
            System.Console.Error.WriteLine($"[MappingExtensions-WARN] {message}{Environment.NewLine}{ex}");
#else
            var env = Environment.GetEnvironmentVariable("ASPNETCORE_ENVIRONMENT");
            if (env != null && !env.Equals("Production", StringComparison.OrdinalIgnoreCase))
            {
                System.Console.Error.WriteLine($"[MappingExtensions-WARN] {message}{Environment.NewLine}{ex.Message}");
            }
#endif
        }
        catch
        {
            // 任何日志写入失败都绝不能影响 ToDto 主流程。
        }
    }
}

public static class MappingExtensions
{
    /// <summary>
    /// 安全反序列化 JSON 列表。
    /// 策略：
    ///   - null / 空白 → fallback
    ///   - 合法 JSON 且 T 可识别 → 返回列表
    ///   - 非法 JSON / 类型不匹配（典型：历史版本误将 CSV/邮箱字符串写入 JSON 列）→
    ///     记录 Warning 并返回 fallback，绝不抛异常导致 500 或整个列表接口空响应。
    /// </summary>
    private static List<T> SafeDeserializeList<T>(string? json, List<T> fallback, [System.Runtime.CompilerServices.CallerMemberName] string caller = "", [System.Runtime.CompilerServices.CallerArgumentExpression(nameof(json))] string member = "")
    {
        if (string.IsNullOrWhiteSpace(json)) return fallback;
        try
        {
            var value = JsonSerializer.Deserialize<List<T>>(json);
            return value ?? fallback;
        }
        catch (Exception ex)
        {
            MappingLog.WriteWarning(ex,
                $"Caller={caller}, Member={member}, Type={typeof(T).Name}, RawLength={json.Length}, RawPreview={(json.Length <= 80 ? json : json[..80])}");
            return fallback;
        }
    }

    /// <summary>
    /// 安全反序列化单个对象。
    /// </summary>
    private static T? SafeDeserializeObject<T>(string? json, [System.Runtime.CompilerServices.CallerMemberName] string caller = "", [System.Runtime.CompilerServices.CallerArgumentExpression(nameof(json))] string member = "")
        where T : class
    {
        if (string.IsNullOrWhiteSpace(json)) return null;
        try
        {
            return JsonSerializer.Deserialize<T>(json);
        }
        catch (Exception ex)
        {
            MappingLog.WriteWarning(ex,
                $"Caller={caller}, Member={member}, Type={typeof(T).Name}, RawLength={json.Length}");
            return null;
        }
    }

    public static UserResponse ToResponse(this User user)
    {
        return new UserResponse
        {
            Id = user.Id,
            FirstName = user.FirstName,
            LastName = user.LastName,
            Email = user.Email,
            Role = (int)user.Role,
            Status = (int)user.Status,
            FullName = user.GetFullName(),
            Timezone = user.Timezone,
            Locale = user.Locale,
            Avatar = user.Avatar,
            MailboxIds = user.MailboxUsers?.Select(mu => mu.MailboxId).ToList() ?? [],
            Permissions = user.GetPermissions(),
            CreatedAt = user.CreatedAt,
            DeletedAt = user.DeletedAt,
            RowVersion = user.RowVersion != null ? Convert.ToBase64String(user.RowVersion) : null
        };
    }

    /// <summary>
    /// 带细粒度权限键的 ToResponse 重载，用于登录/profile 响应中返回字符串权限列表。
    /// </summary>
    /// <param name="user">用户实体</param>
    /// <param name="stringPermissions">细粒度权限键列表（来自 UserRoleResolver.GetPermissionsAsync）</param>
    public static UserResponse ToResponse(this User user, IEnumerable<string> stringPermissions)
    {
        var resp = user.ToResponse();
        resp.StringPermissions = stringPermissions?.ToList() ?? [];
        return resp;
    }

    public static CustomerResponse ToResponse(this Customer customer)
    {
        return customer.ToResponse(0);
    }

    public static CustomerResponse ToResponse(this Customer customer, int conversationsCount)
    {
        return new CustomerResponse
        {
            Id = customer.Id,
            FirstName = customer.FirstName,
            LastName = customer.LastName,
            FullName = customer.GetFullName(true),
            MainEmail = customer.GetMainEmail(),
            Address = customer.Address,
            City = customer.City,
            State = customer.State,
            Zip = customer.Zip,
            Country = customer.Country,
            Company = customer.Company,
            JobTitle = customer.JobTitle,
            Notes = customer.Notes,
            Channel = customer.Channel,
            ChannelId = customer.ChannelId,
            Phones = customer.GetPhones().Select(p => new CustomerPhoneDto
            {
                Value = p.Value,
                Type = p.Type
            }).ToList(),
            SocialProfiles = customer.GetSocialProfiles().Select(sp => new CustomerSocialProfileDto
            {
                Value = sp.Value,
                Type = sp.Type
            }).ToList(),
            Websites = customer.GetWebsites(),
            ConversationsCount = conversationsCount,
            Avatar = customer.Avatar,
            CreatedAt = customer.CreatedAt,
            UpdatedAt = customer.UpdatedAt,
            DeletedAt = customer.DeletedAt,
            RowVersion = customer.RowVersion != null ? Convert.ToBase64String(customer.RowVersion) : null
        };
    }

    public static ConversationResponse ToResponse(this Conversation conversation, bool includeThreads = false, DateTime? lastReadAt = null)
    {
        var response = new ConversationResponse
        {
            Id = conversation.Id,
            MailboxId = conversation.MailboxId,
            CustomerId = conversation.CustomerId,
            UserId = conversation.UserId,
            FolderId = conversation.FolderId,
            Type = (int)conversation.Type,
            SourceVia = (int)conversation.SourceVia,
            SourceType = (int)conversation.SourceType,
            Status = (int)conversation.Status,
            Priority = (int)conversation.Priority,
            Subject = conversation.Subject,
            CustomerEmail = conversation.CustomerEmail,
            Preview = conversation.Preview,
            HasAttachments = conversation.HasAttachments,
            LastReplyAt = conversation.LastReplyAt,
            ClosedAt = conversation.ClosedAt,
            CreatedAt = conversation.CreatedAt,
            UpdatedAt = conversation.UpdatedAt,
            Customer = conversation.Customer?.ToResponse(),
            User = conversation.User?.ToResponse(),
            Tags = conversation.ConversationTags?.Select(ct => ct.Tag.ToResponse()).ToList() ?? [],
            FollowerIds = conversation.Followers?.Select(f => f.UserId).ToList() ?? []
        };

        // 未读判定：用户从未查看（lastReadAt 为 null）或最后阅读时间早于最后回复时间
        var lastActivity = conversation.LastReplyAt ?? conversation.CreatedAt;
        response.HasUnreadMessages = !lastReadAt.HasValue || lastReadAt.Value < lastActivity;

        if (includeThreads && conversation.Threads != null)
        {
            response.Threads = conversation.Threads.Select(t => t.ToResponse()).ToList();
        }

        return response;
    }

    public static ThreadResponse ToResponse(this ConversationThread thread)
    {
        return new ThreadResponse
        {
            Id = thread.Id,
            ConversationId = thread.ConversationId,
            UserId = thread.UserId,
            CustomerId = thread.CustomerId,
            Type = (int)thread.Type,
            SourceType = (int)thread.SourceType,
            State = (int)thread.State,
            Body = thread.Body,
            From = thread.From,
            To = thread.To,
            Cc = thread.Cc,
            Bcc = thread.Bcc,
            Subject = thread.Subject,
            IsInternal = thread.IsInternal,
            HasAttachments = thread.HasAttachments,
            CreatedAt = thread.CreatedAt,
            SendStatus = (int?)thread.SendStatus,
            User = thread.User?.ToResponse(),
            Attachments = thread.Attachments?.Select(a => a.ToResponse()).ToList() ?? []
        };
    }

    public static AttachmentResponse ToResponse(this Attachment attachment)
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

    public static MailboxResponse ToResponse(this Mailbox mailbox)
    {
        return new MailboxResponse
        {
            Id = mailbox.Id,
            Name = mailbox.Name,
            Email = mailbox.Email,
            InHost = mailbox.InHost,
            InPort = mailbox.InPort,
            InEncryption = (int)mailbox.InEncryption,
            InValidateCert = mailbox.InValidateCert,
            InUsername = mailbox.InUsername,
            HasInPassword = !string.IsNullOrEmpty(mailbox.InPassword),
            ImapSentFolder = mailbox.ImapSentFolder,
            OutHost = mailbox.OutHost,
            OutPort = mailbox.OutPort,
            OutEncryption = (int)mailbox.OutEncryption,
            OutUsername = mailbox.OutUsername,
            HasOutPassword = !string.IsNullOrEmpty(mailbox.OutPassword),
            Aliases = mailbox.Aliases,
            AliasesReply = mailbox.AliasesReply,
            AutoBcc = mailbox.AutoBcc,
            AutoReplyEnabled = mailbox.AutoReplyEnabled,
            AutoReplySubject = mailbox.AutoReplySubject,
            AutoReplyMessage = mailbox.AutoReplyMessage,
            BeforeReply = mailbox.BeforeReply,
            Signature = mailbox.Signature,
            OauthEnabled = mailbox.OauthEnabled,
            OauthProvider = mailbox.OauthProvider,
            OauthTokenExpiresAt = mailbox.OauthTokenExpiresAt,
            IsActive = mailbox.IsActive,
            UserIds = mailbox.MailboxUsers?.Select(mu => mu.UserId).ToList() ?? [],
            Folders = mailbox.Folders?.Select(f => f.ToResponse()).ToList() ?? [],
            FetchFrequency = mailbox.FetchFrequency,
            LastFetchAt = mailbox.LastFetchAt,
            CreatedAt = mailbox.CreatedAt,
            DeletedAt = mailbox.DeletedAt
        };
    }

    public static FolderResponse ToResponse(this Folder folder)
    {
        return new FolderResponse
        {
            Id = folder.Id,
            MailboxId = folder.MailboxId,
            UserId = folder.UserId,
            Type = (int)folder.Type,
            Name = folder.Name,
            ActiveCount = folder.ActiveCount,
            TotalCount = folder.TotalCount,
            CreatedAt = folder.CreatedAt,
            UpdatedAt = folder.UpdatedAt
        };
    }

    public static TagResponse ToResponse(this Tag tag, int conversationCount = 0)
    {
        return new TagResponse
        {
            Id = tag.Id,
            Name = tag.Name,
            Color = tag.Color,
            MailboxId = tag.MailboxId,
            ConversationCount = conversationCount,
            CreatedAt = tag.CreatedAt,
            UpdatedAt = tag.UpdatedAt,
            DeletedAt = tag.DeletedAt
        };
    }

    public static PagedResponse<T> ToPagedResponse<T>(this IEnumerable<T> items, int page, int pageSize, int totalItems)
    {
        return new PagedResponse<T>
        {
            Data = items.ToList(),
            CurrentPage = page,
            TotalPages = (int)Math.Ceiling((double)totalItems / pageSize),
            TotalItems = totalItems,
            PageSize = pageSize
        };
    }

    public static ModuleDto ToDto(this Module module)
    {
        return new ModuleDto
        {
            Id = module.Id,
            Name = module.Name,
            Title = module.Title,
            Description = module.Description,
            Version = module.Version,
            Author = module.Author,
            Icon = module.Icon,
            // 核心模块永远对外呈现为"已启用"，即使 DB 中被意外篡改
            IsActive = module.IsSystemModule || module.IsActive,
            Category = module.Category,
            Settings = module.Settings,
            SortOrder = module.SortOrder,
            CreatedAt = module.CreatedAt,
            UpdatedAt = module.UpdatedAt,
            IsSystemModule = module.IsSystemModule
        };
    }

    public static ApiKeyDto ToDto(this ApiKey apiKey, bool includeFullKey = false)
    {
        return new ApiKeyDto
        {
            Id = apiKey.Id,
            Name = apiKey.Name,
            Description = apiKey.Description,
            KeyPrefix = apiKey.Key.Length > 8 ? apiKey.Key.Substring(0, 8) + "..." : apiKey.Key,
            Key = includeFullKey ? apiKey.Key : null,
            UserId = apiKey.UserId,
            UserName = apiKey.User != null ? apiKey.User.GetFullName() : null,
            Scopes = string.IsNullOrEmpty(apiKey.Scopes) ? [] : apiKey.Scopes.Split(',', StringSplitOptions.RemoveEmptyEntries).ToList(),
            IsActive = apiKey.IsActive,
            ExpiresAt = apiKey.ExpiresAt,
            LastUsedAt = apiKey.LastUsedAt,
            LastUsedIp = apiKey.LastUsedIp,
            UsageCount = apiKey.UsageCount,
            CreatedAt = apiKey.CreatedAt,
            UpdatedAt = apiKey.UpdatedAt,
            RowVersion = apiKey.RowVersion != null ? Convert.ToBase64String(apiKey.RowVersion) : null
        };
    }

    public static ApiKeyLogDto ToDto(this ApiKeyLog apiKeyLog)
    {
        return new ApiKeyLogDto
        {
            Id = apiKeyLog.Id,
            ApiKeyId = apiKeyLog.ApiKeyId,
            ApiKeyName = apiKeyLog.ApiKey?.Name ?? string.Empty,
            Endpoint = apiKeyLog.Endpoint,
            Method = apiKeyLog.Method,
            StatusCode = apiKeyLog.StatusCode,
            IpAddress = apiKeyLog.IpAddress,
            UserAgent = apiKeyLog.UserAgent,
            DurationMs = apiKeyLog.DurationMs,
            ErrorMessage = apiKeyLog.ErrorMessage,
            CreatedAt = apiKeyLog.CreatedAt
        };
    }

    private static string ToIsoUtc(DateTime dt) =>
        DateTime.SpecifyKind(dt, DateTimeKind.Utc).ToString("yyyy-MM-ddTHH:mm:ssZ");

    public static TimeEntryDto ToDto(this TimeEntry entry)
    {
        return new TimeEntryDto
        {
            Id = entry.Id,
            EntryType = entry.EntryType,
            Description = entry.Description,
            ConversationId = entry.ConversationId,
            ConversationNumber = entry.Conversation?.Number,
            ConversationSubject = entry.Conversation?.Subject,
            CustomerId = entry.CustomerId,
            CustomerName = entry.Customer?.GetFullName(),
            UserId = entry.UserId,
            UserName = entry.User?.GetFullName() ?? entry.User?.Email ?? "",
            MailboxId = entry.MailboxId,
            StartTime = ToIsoUtc(entry.StartTime),
            EndTime = entry.EndTime.HasValue ? ToIsoUtc(entry.EndTime.Value) : null,
            DurationMinutes = entry.DurationMinutes,
            IsRunning = entry.IsRunning,
            Billable = entry.Billable,
            CreatedAt = ToIsoUtc(entry.CreatedAt),
            UpdatedAt = ToIsoUtc(entry.UpdatedAt),
            RowVersion = entry.RowVersion != null ? Convert.ToBase64String(entry.RowVersion) : null
        };
    }

    public static CalendarEventDto ToDto(this CalendarEvent evt)
    {
        var eventTypeLabels = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase)
        {
            ["due_date"] = "截止日期",
            ["task"] = "计划任务",
            ["meeting"] = "会议",
            ["reminder"] = "提醒",
            ["personal"] = "个人",
            ["travel"] = "出行",
            ["work"] = "工作",
            ["other"] = "其他"
        };
        var statusLabels = new Dictionary<string, string>
        {
            ["pending"] = "待处理",
            ["completed"] = "已完成",
            ["cancelled"] = "已取消"
        };

        return new CalendarEventDto
        {
            Id = evt.Id,
            Title = evt.Title,
            Description = evt.Description,
            EventType = evt.EventType,
            EventTypeLabel = eventTypeLabels.TryGetValue(evt.EventType, out var etl) ? etl : evt.EventType,
            StartAt = ToIsoUtc(evt.StartAt),
            EndAt = evt.EndAt.HasValue ? ToIsoUtc(evt.EndAt.Value) : null,
            IsAllDay = evt.IsAllDay,
            ConversationId = evt.ConversationId,
            ConversationSubject = evt.Conversation?.Subject,
            MailboxId = evt.MailboxId,
            MailboxName = evt.Mailbox?.Name,
            UserId = evt.UserId,
            UserName = evt.User != null ? $"{evt.User.FirstName} {evt.User.LastName}".Trim() : null,
            Status = evt.Status,
            StatusLabel = statusLabels.TryGetValue(evt.Status, out var sl) ? sl : evt.Status,
            Color = evt.Color,
            DeletedAt = evt.DeletedAt.HasValue ? ToIsoUtc(evt.DeletedAt.Value) : null,
            IsRecurring = evt.IsRecurring,
            RecurrenceType = evt.RecurrenceType,
            RecurrenceInterval = evt.RecurrenceInterval,
            RecurrenceEndDate = evt.RecurrenceEndDate.HasValue ? ToIsoUtc(evt.RecurrenceEndDate.Value) : null,
            RecurrenceCount = evt.RecurrenceCount,
            RecurrenceParentId = evt.RecurrenceParentId,
            RecurrenceOriginalStart = evt.RecurrenceOriginalStart.HasValue ? ToIsoUtc(evt.RecurrenceOriginalStart.Value) : null,
            ReminderMinutes = evt.ReminderMinutes,
            IsReminderSent = evt.IsReminderSent,
            ReminderEmails = evt.ReminderEmails,
            CreatedBy = evt.CreatedBy,
            // 创建者姓名：优先从导航属性读取（Include 链生效时），否则由查询服务批量回填
            CreatedByName = evt.CreatedByUser != null ? evt.CreatedByUser.GetFullName() : null,
            Priority = evt.Priority,
            Progress = evt.Progress,
            Location = evt.Location,
            MeetingUrl = evt.MeetingUrl,
            Attendees = SafeDeserializeList<int>(evt.AttendeesJson, new()),
            SubTasks = SafeDeserializeList<SubTaskDto>(evt.SubTasksJson, new()),
            RelatedEventId = evt.RelatedEventId,
            RecurrenceExclusions = SafeDeserializeObject<RecurrenceExclusionsDto>(evt.RecurrenceExclusionsJson),
            // 事件类型专属字段（2026-08-17 持久化）
            HostUserIds = SafeDeserializeList<int>(evt.HostUserIdsJson, new()),
            AgendaIds = SafeDeserializeList<int>(evt.AgendaIdsJson, new()),
            TaskOwnerId = evt.TaskOwnerId,
            DueAt = evt.DueAt.HasValue ? ToIsoUtc(evt.DueAt.Value) : null,
            ReminderType = evt.ReminderType,
            WeekdaysSelected = SafeDeserializeList<int>(evt.WeekdaysSelectedJson, new()),
            RecurrenceRule = evt.RecurrenceRule,
            CreatedAt = ToIsoUtc(evt.CreatedAt),
            UpdatedAt = ToIsoUtc(evt.UpdatedAt),
            RowVersion = evt.RowVersion != null ? Convert.ToBase64String(evt.RowVersion) : null
        };
    }

    /// <summary>
    /// 按当前用户权限裁剪 DTO 中的敏感字段。
    /// 创建者、事件负责人（可编辑该日程的用户，UserId 匹配）与超管保留 ReminderEmails；
    /// 仅对无关用户清空 ReminderEmails（可能包含其他用户邮箱地址）。
    /// T000100：此前未将事件负责人(UserId)纳入放行集合，导致负责人编辑保存后回显被清空，
    /// 再次编辑打开参会人邮箱为空。
    /// </summary>
    public static CalendarEventDto SanitizeForUser(this CalendarEventDto dto, int currentUserId, bool isSuperAdmin)
    {
        if (isSuperAdmin || dto.CreatedBy == currentUserId || dto.UserId == currentUserId) return dto;
        dto.ReminderEmails = null;
        return dto;
    }

    public static KnowledgeCategoryDto ToDto(this KnowledgeCategory c)
    {
        return new KnowledgeCategoryDto
        {
            Id = c.Id,
            Name = c.Name,
            Description = c.Description,
            ParentId = c.ParentId,
            ParentName = c.Parent?.Name,
            MailboxId = c.MailboxId,
            SortOrder = c.SortOrder,
            Icon = c.Icon,
            IsActive = c.IsActive,
            CreatedAt = c.CreatedAt.ToString("yyyy-MM-ddTHH:mm:ssZ"),
            UpdatedAt = c.UpdatedAt.ToString("yyyy-MM-ddTHH:mm:ssZ"),
            Children = new List<KnowledgeCategoryDto>(),
            RowVersion = c.RowVersion != null ? Convert.ToBase64String(c.RowVersion) : null
        };
    }

    public static KnowledgeArticleDto ToDto(this KnowledgeArticle a)
    {
        return a.ToDto(snippet: null, rank: null);
    }

    /// <summary>
    /// 带搜索片段和排序值的映射（供全文搜索使用）。
    /// </summary>
    public static KnowledgeArticleDto ToDto(this KnowledgeArticle a, string? snippet, double? rank)
    {
        var statusLabels = new Dictionary<string, string>
        {
            ["draft"] = "草稿",
            ["published"] = "已发布",
            ["archived"] = "已归档"
        };
        var tagList = string.IsNullOrWhiteSpace(a.Tags)
            ? new List<string>()
            : a.Tags.Split(',', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries).ToList();

        return new KnowledgeArticleDto
        {
            Id = a.Id,
            Title = a.Title,
            Content = a.Content,
            ContentType = string.IsNullOrWhiteSpace(a.ContentType) ? "markdown" : a.ContentType,
            Summary = a.Summary,
            CategoryId = a.CategoryId,
            CategoryName = a.Category?.Name,
            AuthorId = a.AuthorId,
            // 作者名称：User 实体无 FullName 字段，需拼接 FirstName + LastName（与 ChatSessionDto 等保持一致）
            AuthorName = a.Author != null ? $"{a.Author.FirstName} {a.Author.LastName}".Trim() : null,
            Tags = a.Tags,
            TagList = tagList,
            Attachments = a.Attachments?
                .Where(att => att.DeletedAt == null)
                .Select(att => att.ToDto())
                .ToList() ?? new List<KnowledgeArticleAttachmentDto>(),
            Status = a.Status,
            StatusLabel = statusLabels.TryGetValue(a.Status, out var sl) ? sl : a.Status,
            ViewCount = a.ViewCount,
            LikeCount = a.LikeCount,
            SortOrder = a.SortOrder,
            IsPinned = a.IsPinned,
            CreatedAt = a.CreatedAt.ToString("yyyy-MM-ddTHH:mm:ssZ"),
            UpdatedAt = a.UpdatedAt.ToString("yyyy-MM-ddTHH:mm:ssZ"),
            PublishedAt = a.PublishedAt?.ToString("yyyy-MM-ddTHH:mm:ssZ"),
            RowVersion = a.RowVersion != null ? Convert.ToBase64String(a.RowVersion) : null,
            Snippet = snippet,
            Rank = rank,
        };
    }

    /// <summary>
    /// 知识库文章附件 → DTO
    /// </summary>
    public static KnowledgeArticleAttachmentDto ToDto(this KnowledgeArticleAttachment att)
    {
        return new KnowledgeArticleAttachmentDto
        {
            Id = att.Id,
            ArticleId = att.ArticleId,
            FileName = att.FileName,
            Url = att.Url,
            MimeType = att.MimeType,
            FileSize = att.FileSize,
            Extension = att.Extension,
            CreatedAt = att.CreatedAt.ToString("yyyy-MM-ddTHH:mm:ssZ")
        };
    }

    public static ChatSessionDto ToDto(this ChatSession s)
    {
        var statusLabels = new Dictionary<string, string>
        {
            ["waiting"] = "等待接待",
            ["active"] = "进行中",
            ["awaiting_rating"] = "等待评分",
            ["closed"] = "已关闭",
            ["transferred"] = "已转工单"
        };

        return new ChatSessionDto
        {
            Id = s.Id,
            SessionToken = s.SessionToken,
            CustomerName = s.CustomerName,
            CustomerEmail = s.CustomerEmail,
            CustomerPhone = s.CustomerPhone,
            CustomerIp = s.CustomerIp,
            CustomerId = s.CustomerId,
            MailboxId = s.MailboxId,
            MailboxName = s.Mailbox?.Name,
            UserId = s.UserId,
            UserName = s.User != null ? $"{s.User.FirstName} {s.User.LastName}".Trim() : null,
            ConversationId = s.ConversationId,
            Status = s.Status,
            StatusLabel = statusLabels.TryGetValue(s.Status, out var sl) ? sl : s.Status,
            Subject = s.Subject,
            CustomerLastSeenAt = s.CustomerLastSeenAt,
            AgentLastSeenAt = s.AgentLastSeenAt,
            CustomerUnreadCount = s.CustomerUnreadCount,
            AgentUnreadCount = s.AgentUnreadCount,
            Rating = s.Rating,
            RatingComment = s.RatingComment,
            RatingRequestedAt = s.RatingRequestedAt,
            RatingTimeoutAt = s.RatingTimeoutAt,
            MessageCount = 0,
            CreatedAt = s.CreatedAt.ToString("yyyy-MM-ddTHH:mm:ssZ"),
            UpdatedAt = s.UpdatedAt.ToString("yyyy-MM-ddTHH:mm:ssZ"),
            ClosedAt = s.ClosedAt?.ToString("yyyy-MM-ddTHH:mm:ssZ"),
            RowVersion = s.RowVersion != null ? Convert.ToBase64String(s.RowVersion) : null
        };
    }

    public static ChatMessageDto ToDto(this ChatMessage m)
    {
        return new ChatMessageDto
        {
            Id = m.Id,
            SessionId = m.SessionId,
            SenderType = m.SenderType,
            SenderId = m.SenderId,
            SenderName = m.SenderName,
            MessageType = m.MessageType,
            Content = m.Content,
            AttachmentUrl = m.AttachmentUrl,
            AttachmentName = m.AttachmentName,
            IsRead = m.IsRead,
            ReadAt = m.ReadAt?.ToString("yyyy-MM-ddTHH:mm:ssZ"),
            IsRecalled = m.IsRecalled,
            RecalledAt = m.RecalledAt?.ToString("yyyy-MM-ddTHH:mm:ssZ"),
            EditedAt = m.EditedAt?.ToString("yyyy-MM-ddTHH:mm:ssZ"),
            CreatedAt = m.CreatedAt.ToString("yyyy-MM-ddTHH:mm:ssZ"),
            RowVersion = m.RowVersion != null ? Convert.ToBase64String(m.RowVersion) : null
        };
    }
}
