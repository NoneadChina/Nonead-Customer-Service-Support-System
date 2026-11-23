using NoneadCSS.Application.DTOs.Requests;
using NoneadCSS.Application.DTOs.Responses;
using NoneadCSS.Domain;
using NoneadCSS.Domain.Entities;
using NoneadCSS.Domain.Enums;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Logging;

namespace NoneadCSS.Application.Services;

public static class SlaConst
{
    public const string ViolationFirstResponse = "first_response";
    public const string ViolationResolution = "resolution";

    public const string StatusWarning = "warning";
    public const string StatusBreached = "breached";
    public const string StatusResolved = "resolved";

    public const string SlaStatusOnTrack = "on_track";
    public const string SlaStatusWarning = "warning";
    public const string SlaStatusBreached = "breached";
    public const string SlaStatusResolved = "resolved";
    public const string SlaStatusNotApplicable = "not_applicable";

    /// <summary>
    /// 预警阈值：剩余时间低于总时长的 20% 时进入 warning 状态。
    /// </summary>
    public const double WarningRatio = 0.2;

    public static string GetViolationTypeLabel(string type) => type switch
    {
        ViolationFirstResponse => "首次响应",
        ViolationResolution => "解决时长",
        _ => type
    };

    /// <summary>
    /// 违规记录的状态标签（warning/breached/resolved）。
    /// </summary>
    public static string GetViolationStatusLabel(string status) => status switch
    {
        StatusWarning => "预警中",
        StatusBreached => "已违规",
        StatusResolved => "已解决",
        _ => status
    };

    /// <summary>
    /// 会话 SLA 状态标签（on_track/warning/breached/resolved/not_applicable）。
    /// </summary>
    public static string GetSlaStatusLabel(string status) => status switch
    {
        SlaStatusOnTrack => "进行中",
        SlaStatusWarning => "即将到期",
        SlaStatusBreached => "已超时",
        SlaStatusResolved => "已达成",
        SlaStatusNotApplicable => "不适用",
        _ => status
    };

    /// <summary>
    /// 将小时数格式化为友好显示（如 "24 小时" / "1 天 4 小时" / "30 分钟"）。
    /// 传入 <paramref name="translate"/> 时按 <c>SlaDuration*</c> 词条取当前语言的时长模板
    /// （词条缺失即回退下面内联的 zh-Hans 原文，数字仍走槽位，不固化进词条）；
    /// 不传时输出与改动前的中文结果逐字一致（出站通知等语言中立场景保持原行为）。
    /// </summary>
    public static string FormatHours(double hours, Func<string, string, object[]?, string>? translate = null)
    {
        // 未注入词条时保持改动前的中文输出逐字一致（出站通知等语言中立场景）。
        string Fmt(string key, string zh, object[]? args) => translate is null
            ? (args is null ? zh : string.Format(zh, args))
            : translate(key, zh, args);

        if (hours <= 0) return Fmt("SlaDurationZero", "0 分钟", null);
        if (hours < 1)
        {
            var minutes = (int)Math.Round(hours * 60);
            return Fmt("SlaDurationMinutes", "{0} 分钟", new object[] { minutes });
        }
        if (hours < 24)
        {
            var wholeHours = (int)hours;
            var remainingMinutes = (int)Math.Round((hours - wholeHours) * 60);
            return remainingMinutes > 0
                ? Fmt("SlaDurationHoursMinutes", "{0} 小时 {1} 分钟", new object[] { wholeHours, remainingMinutes })
                : Fmt("SlaDurationHours", "{0} 小时", new object[] { wholeHours });
        }
        var days = (int)(hours / 24);
        var remainingHours = (int)(hours % 24);
        return remainingHours > 0
            ? Fmt("SlaDurationDaysHours", "{0} 天 {1} 小时", new object[] { days, remainingHours })
            : Fmt("SlaDurationDays", "{0} 天", new object[] { days });
    }

    /// <summary>
    /// 根据 SLA 时长和已用时长计算状态。
    /// </summary>
    public static string CalculateStatus(double targetHours, double elapsedHours, bool isResolved)
    {
        if (isResolved) return SlaStatusResolved;
        // BUG-06 修复：targetHours 为 0 或负数时（历史数据/直接修改 DB 导致），
        // 直接判定为 breached，避免下方除零计算 remainingRatio 时抛出 DivideByZeroException /
        // 得到 NaN，导致 SLA 状态显示异常和下游通知逻辑误判
        if (targetHours <= 0) return SlaStatusBreached;
        if (elapsedHours >= targetHours) return SlaStatusBreached;
        var remainingRatio = (targetHours - elapsedHours) / targetHours;
        return remainingRatio <= WarningRatio ? SlaStatusWarning : SlaStatusOnTrack;
    }
}

public class SlaService : ISlaService
{
    private readonly IUnitOfWork _unitOfWork;
    private readonly ILogger<SlaService> _logger;
    private readonly INotificationService _notificationService;
    private readonly IConfiguration _configuration;
    private readonly ICurrentUserAccessor _currentUserAccessor;
    private readonly IFrontendUrlResolver _frontendUrlResolver;
    private readonly ILocalizationService? _localization;

    /// <summary>
    /// 构造 SLA 服务。<paramref name="localization"/> 为可选本地化服务：
    /// 未注入（后台任务、旧构造点）时违规类型/状态标签回退 <see cref="SlaConst"/> 的中文原文，输出保持不变。
    /// </summary>
    public SlaService(IUnitOfWork unitOfWork, ILogger<SlaService> logger, INotificationService notificationService, IConfiguration configuration, ICurrentUserAccessor currentUserAccessor, IFrontendUrlResolver frontendUrlResolver, ILocalizationService? localization = null)
    {
        _unitOfWork = unitOfWork;
        _logger = logger;
        _notificationService = notificationService;
        _configuration = configuration;
        _currentUserAccessor = currentUserAccessor;
        _frontendUrlResolver = frontendUrlResolver;
        _localization = localization;
    }

    // ====== 策略管理 ======
    public async Task<(IEnumerable<SlaPolicyDto> Items, int Total)> GetListAsync(
        int page, int pageSize, int? mailboxId = null, bool? isActive = null, string? keyword = null)
    {
        var (items, total) = await _unitOfWork.SlaPolicies.GetPagedAsync(page, pageSize, mailboxId, isActive, keyword);
        return (items.Select(ToPolicyDto), total);
    }

    public async Task<SlaPolicyDto?> GetByIdAsync(int id)
    {
        var entity = await _unitOfWork.SlaPolicies.GetByIdAsync(id);
        if (entity == null) return null;

        if (!await _currentUserAccessor.CanAccessMailboxAsync(entity.MailboxId))
            return null;

        return ToPolicyDto(entity);
    }

    public async Task<SlaPolicyDto> CreateAsync(CreateSlaPolicyRequest request)
    {
        var entity = new SlaPolicy
        {
            Name = request.Name,
            Description = request.Description,
            MailboxId = request.MailboxId == 0 ? null : request.MailboxId,
            FirstResponseHours = request.FirstResponseHours > 0 ? request.FirstResponseHours : 24.0,
            ResolutionHours = request.ResolutionHours > 0 ? request.ResolutionHours : 72.0,
            BusinessHoursOnly = request.BusinessHoursOnly,
            WorkDays = request.WorkDays,
            WorkStartHour = request.WorkStartHour,
            WorkEndHour = request.WorkEndHour,
            Priority = request.Priority,
            IsActive = request.IsActive,
            SortOrder = request.SortOrder,
            CreatedAt = DateTime.UtcNow,
            UpdatedAt = DateTime.UtcNow
        };

        await _unitOfWork.SlaPolicies.AddAsync(entity);
        await _unitOfWork.SaveChangesAsync();
        _logger.LogInformation("创建 SLA 策略：{Name}（ID={Id}）", entity.Name, entity.Id);
        return ToPolicyDto(entity);
    }

    public async Task<SlaPolicyDto?> UpdateAsync(int id, UpdateSlaPolicyRequest request)
    {
        // 使用追踪查询，避免 AsNoTracking 实体修改后无法持久化
        var entity = await _unitOfWork.SlaPolicies.GetQueryable(false)
            .FirstOrDefaultAsync(p => p.Id == id && p.DeletedAt == null);
        if (entity == null) return null;

        if (!await _currentUserAccessor.CanAccessMailboxAsync(entity.MailboxId))
            return null;

        entity.Name = request.Name;
        entity.Description = request.Description;
        entity.MailboxId = request.MailboxId == 0 ? null : request.MailboxId;
        entity.FirstResponseHours = request.FirstResponseHours > 0 ? request.FirstResponseHours : 24.0;
        entity.ResolutionHours = request.ResolutionHours > 0 ? request.ResolutionHours : 72.0;
        entity.BusinessHoursOnly = request.BusinessHoursOnly;
        entity.WorkDays = request.WorkDays;
        entity.WorkStartHour = request.WorkStartHour;
        entity.WorkEndHour = request.WorkEndHour;
        entity.Priority = request.Priority;
        entity.IsActive = request.IsActive;
        entity.SortOrder = request.SortOrder;
        entity.UpdatedAt = DateTime.UtcNow;

        await _unitOfWork.SaveChangesAsync();
        return ToPolicyDto(entity);
    }

    public async Task<bool> DeleteAsync(int id)
    {
        // 使用追踪查询，确保 SoftDeleteAsync 中 Update 调用能正确持久化
        var entity = await _unitOfWork.SlaPolicies.GetQueryable(false)
            .FirstOrDefaultAsync(p => p.Id == id && p.DeletedAt == null);
        if (entity == null) return false;

        if (!await _currentUserAccessor.CanAccessMailboxAsync(entity.MailboxId))
            return false;
        // 软删除：仅标记 DeletedAt，保留数据与历史违规记录
        await _unitOfWork.SlaPolicies.SoftDeleteAsync(entity);
        // SoftDeleteAsync 不再在仓储内 SaveChanges，由本调用方统一持久化。
        await _unitOfWork.SaveChangesAsync();
        _logger.LogInformation("软删除 SLA 策略：{Name}（ID={Id}）", entity.Name, entity.Id);
        return true;
    }

    public async Task<bool?> ToggleActiveAsync(int id)
    {
        var entity = await _unitOfWork.SlaPolicies.GetQueryable(false)
            .FirstOrDefaultAsync(p => p.Id == id && p.DeletedAt == null);
        if (entity == null) return null;
        entity.IsActive = !entity.IsActive;
        entity.UpdatedAt = DateTime.UtcNow;
        await _unitOfWork.SaveChangesAsync();
        return entity.IsActive;
    }

    // ====== 垃圾箱 ======
    public async Task<(IEnumerable<SlaPolicyDto> Items, int Total)> GetTrashAsync(int page, int pageSize, string? keyword = null)
    {
        var (items, total) = await _unitOfWork.SlaPolicies.GetDeletedPagedAsync(page, pageSize, keyword);
        return (items.Select(ToPolicyDto), total);
    }

    public async Task<int> GetTrashCountAsync()
    {
        return await _unitOfWork.SlaPolicies.CountDeletedAsync();
    }

    public async Task<bool> RestoreAsync(int id)
    {
        var entity = await _unitOfWork.SlaPolicies.GetDeletedByIdAsync(id);
        if (entity == null) return false;

        // L-004: 恢复前检查同名策略是否存在，若存在则自动重命名避免冲突
        var hasNameConflict = await _unitOfWork.SlaPolicies.GetQueryable()
            .AnyAsync(p => p.Name == entity.Name && p.Id != entity.Id);
        if (hasNameConflict)
        {
            entity.Name = $"{entity.Name} (恢复_{DateTime.UtcNow:yyyyMMdd})";
        }

        await _unitOfWork.SlaPolicies.RestoreAsync(entity);
        await _unitOfWork.SaveChangesAsync();
        _logger.LogInformation("恢复 SLA 策略：{Name}（ID={Id}）", entity.Name, entity.Id);
        return true;
    }

    public async Task<bool> HardDeleteAsync(int id)
    {
        var entity = await _unitOfWork.SlaPolicies.GetDeletedByIdAsync(id);
        if (entity == null) return false;
        await _unitOfWork.SlaPolicies.HardDeleteAsync(entity);
        await _unitOfWork.SaveChangesAsync();
        _logger.LogInformation("彻底删除 SLA 策略：{Name}（ID={Id}）", entity.Name, entity.Id);
        return true;
    }

    public async Task<int> ClearTrashAsync()
    {
        var count = await _unitOfWork.SlaPolicies.HardDeleteAllDeletedAsync();
        if (count > 0)
        {
            await _unitOfWork.SaveChangesAsync();
            _logger.LogInformation("清空 SLA 策略垃圾箱：共 {Count} 条", count);
        }
        return count;
    }

    // ====== 违规管理 ======
    public async Task<(IEnumerable<SlaViolationDto> Items, int Total)> GetViolationsAsync(
        int page, int pageSize,
        int? policyId = null, int? conversationId = null, string? violationType = null,
        string? status = null, DateTime? startDate = null, DateTime? endDate = null)
    {
        var (items, total) = await _unitOfWork.SlaViolations.GetPagedAsync(
            page, pageSize, policyId, conversationId, violationType, status, startDate, endDate);
        return (items.Select(ToViolationDto), total);
    }

    public async Task<List<SlaViolationDto>> GetViolationsByConversationAsync(int conversationId)
    {
        var list = await _unitOfWork.SlaViolations.GetByConversationAsync(conversationId);
        return list.Select(ToViolationDto).ToList();
    }

    // ====== 概览 ======
    public async Task<SlaOverviewResponse> GetOverviewAsync()
    {
        var policyStats = await _unitOfWork.SlaPolicies.GetStatsAsync();
        var violationStats = await _unitOfWork.SlaViolations.GetStatsAsync();
        var recentViolations = await _unitOfWork.SlaViolations.GetRecentAsync(10);
        var activeWarnings = await _unitOfWork.SlaViolations.GetActiveWarningsAsync();
        var topPolicies = await _unitOfWork.SlaPolicies.GetAllWithMailboxAsync();

        return new SlaOverviewResponse
        {
            PolicyStats = policyStats,
            ViolationStats = violationStats,
            RecentViolations = recentViolations.Select(ToViolationDto).ToList(),
            ActiveWarnings = activeWarnings.Select(ToViolationDto).Take(10).ToList(),
            TopPolicies = topPolicies.Take(5).Select(ToPolicyDto).ToList()
        };
    }

    // ====== 会话 SLA 状态 ======
    public async Task<SlaConversationStatusResponse?> GetConversationStatusAsync(int conversationId)
    {
        var conversation = await _unitOfWork.Conversations.GetByIdAsync(conversationId);
        if (conversation == null) return null;

        var policy = await _unitOfWork.SlaPolicies.GetPolicyForConversationAsync(conversation.MailboxId);
        if (policy == null)
        {
            return new SlaConversationStatusResponse
            {
                ConversationId = conversationId,
                FirstResponseStatus = SlaConst.SlaStatusNotApplicable,
                ResolutionStatus = SlaConst.SlaStatusNotApplicable
            };
        }

        var threads = await _unitOfWork.Threads.FindAsync(t => t.ConversationId == conversationId);
        var firstCustomerMessageAt = threads
            .Where(t => t.Type == ThreadType.Customer)
            .OrderBy(t => t.CreatedAt)
            .Select(t => (DateTime?)t.CreatedAt)
            .FirstOrDefault();
        var firstReplyAt = threads
            .Where(t => t.Type == ThreadType.Message)
            .OrderBy(t => t.CreatedAt)
            .Select(t => (DateTime?)t.CreatedAt)
            .FirstOrDefault();

        var now = DateTime.UtcNow;
        var response = new SlaConversationStatusResponse
        {
            ConversationId = conversationId,
            PolicyName = policy.Name,
            FirstResponseHours = policy.FirstResponseHours,
            ResolutionHours = policy.ResolutionHours,
            FirstResponseAt = firstReplyAt,
            ResolvedAt = conversation.ClosedAt
        };

        // 首次响应状态
        if (firstCustomerMessageAt.HasValue)
        {
            var baseTime = firstCustomerMessageAt.Value;
            response.FirstResponseDueAt = policy.CalculateFirstResponseDueAt(baseTime);
            response.FirstResponseStatus = policy.CalculateFirstResponseStatus(baseTime, firstReplyAt, now);
            response.FirstResponseRemainingMinutes = (response.FirstResponseDueAt.Value - (firstReplyAt ?? now)).TotalMinutes;
        }
        else
        {
            response.FirstResponseStatus = SlaConst.SlaStatusNotApplicable;
        }

        // 解决状态
        if (conversation.Status == ConversationStatus.Closed && conversation.ClosedAt.HasValue)
        {
            response.ResolutionDueAt = policy.CalculateResolutionDueAt(conversation.CreatedAt);
            response.ResolutionStatus = policy.CalculateResolutionStatus(conversation.CreatedAt, true, conversation.ClosedAt, now);
            // H-005: 已关闭会话的解决剩余时间可能为负数（关闭时间晚于截止时间），负数时返回 0
            var resolutionRemaining = (response.ResolutionDueAt.Value - conversation.ClosedAt.Value).TotalMinutes;
            response.ResolutionRemainingMinutes = Math.Max(0, resolutionRemaining);
        }
        else if (conversation.Status == ConversationStatus.Active || conversation.Status == ConversationStatus.Pending)
        {
            response.ResolutionDueAt = policy.CalculateResolutionDueAt(conversation.CreatedAt);
            response.ResolutionStatus = policy.CalculateResolutionStatus(conversation.CreatedAt, false, null, now);
            response.ResolutionRemainingMinutes = (response.ResolutionDueAt.Value - now).TotalMinutes;
        }
        else
        {
            response.ResolutionStatus = SlaConst.SlaStatusNotApplicable;
        }

        return response;
    }

    // ====== 扫描违规 ======
    public async Task<(int Scanned, int NewViolations, int Breached)> ScanViolationsAsync()
    {
        const int batchSize = 100;
        var scanned = 0;
        var newViolations = 0;
        var breached = 0;
        var page = 0;
        var now = DateTime.UtcNow;
        var modifiedPolicies = new HashSet<SlaPolicy>();

        while (true)
        {
            var conversations = await _unitOfWork.Conversations.GetPagedByStatusesAsync(
                page + 1,
                batchSize,
                new[] { ConversationStatus.Active, ConversationStatus.Pending });

            var conversationList = conversations.Items.ToList();
            if (conversationList.Count == 0)
            {
                break;
            }

            var conversationIds = conversationList.Select(c => c.Id).ToList();
            var mailboxIds = conversationList.Select(c => c.MailboxId).Distinct().ToList();
            var customerIds = conversationList
                .Where(c => c.CustomerId.HasValue)
                .Select(c => c.CustomerId!.Value)
                .Distinct()
                .ToList();
            var assigneeIds = conversationList
                .Where(c => c.UserId.HasValue)
                .Select(c => c.UserId!.Value)
                .Distinct()
                .ToList();

            // 批量预加载本批次所需数据，避免在循环内逐条查询造成 N+1
            var policyByMailbox = await _unitOfWork.SlaPolicies.GetPoliciesForMailboxesAsync(mailboxIds);
            var threadsByConversation = await _unitOfWork.Threads.GetByConversationIdsAsync(conversationIds);
            var violationByConversationAndType = await _unitOfWork.SlaViolations.GetByConversationIdsAsync(conversationIds);
            var mailboxesById = (await _unitOfWork.Mailboxes.GetByIdsAsync(mailboxIds))
                .ToDictionary(m => m.Id);
            var customersById = customerIds.Count > 0
                ? (await _unitOfWork.Customers.GetByIdsAsync(customerIds)).ToDictionary(c => c.Id)
                : new Dictionary<int, Customer>();
            var assigneesById = assigneeIds.Count > 0
                ? (await _unitOfWork.Users.GetByIdsAsync(assigneeIds)).ToDictionary(u => u.Id)
                : new Dictionary<int, User>();

            // F-C2: 每个 batch 包裹事务边界，确保违规状态更新原子性
            using var transaction = await _unitOfWork.BeginTransactionAsync();
            try
            {
            foreach (var conversation in conversationList)
            {
                scanned++;
                // L-005: 跳过无策略会话时记录日志
                if (!policyByMailbox.TryGetValue(conversation.MailboxId, out var policy) || policy == null)
                {
                    _logger.LogDebug("SLA 扫描跳过无策略会话：ConversationId={ConversationId}, MailboxId={MailboxId}", conversation.Id, conversation.MailboxId);
                    continue;
                }

                if (!threadsByConversation.TryGetValue(conversation.Id, out var threadList))
                {
                    threadList = new List<ConversationThread>();
                }
                var threads = threadList;
                var firstCustomerMessageAt = threads
                    .Where(t => t.Type == ThreadType.Customer)
                    .OrderBy(t => t.CreatedAt)
                    .Select(t => (DateTime?)t.CreatedAt)
                    .FirstOrDefault();
                var firstReplyAt = threads
                    .Where(t => t.Type == ThreadType.Message)
                    .OrderBy(t => t.CreatedAt)
                    .Select(t => (DateTime?)t.CreatedAt)
                    .FirstOrDefault();

                // 预先解析通知所需关联实体（已批量加载，不再触发 DB 查询）
                mailboxesById.TryGetValue(conversation.MailboxId, out var mailbox);
                Customer? customer = null;
                if (conversation.CustomerId.HasValue)
                {
                    customersById.TryGetValue(conversation.CustomerId.Value, out customer);
                }
                User? assignee = null;
                if (conversation.UserId.HasValue)
                {
                    assigneesById.TryGetValue(conversation.UserId.Value, out assignee);
                }
                var firstThread = threads.OrderBy(t => t.CreatedAt).FirstOrDefault();

                // 首次响应违规检测
                if (firstCustomerMessageAt.HasValue)
                {
                    var baseTime = firstCustomerMessageAt.Value;
                    var dueAt = policy.CalculateFirstResponseDueAt(baseTime);
                    // 违约判定必须比较「首次回复时间」与「应到期限」：
                    // 只要存在回复就置为 resolved，会把「迟到回复」洗白成「按时响应」，
                    // 使达标率/违约率失真（审核报告 P0-02）。改为三态：按时响应 / 迟到响应 / 未响应。
                    var respondedOnTime = firstReplyAt.HasValue && firstReplyAt.Value <= dueAt;
                    var respondedLate = firstReplyAt.HasValue && firstReplyAt.Value > dueAt;

                    violationByConversationAndType.TryGetValue((conversation.Id, SlaConst.ViolationFirstResponse), out var existingFirstResponse);
                    if (respondedOnTime)
                    {
                        // 按时响应：标记为 resolved
                        if (existingFirstResponse != null && existingFirstResponse.Status != SlaConst.StatusResolved)
                        {
                            existingFirstResponse.Status = SlaConst.StatusResolved;
                            existingFirstResponse.ResolvedAt = firstReplyAt;
                        }
                    }
                    else if (respondedLate)
                    {
                        // 迟到响应：不得洗白。已存在记录保持 breached（只补 ResolvedAt 供追溯），
                        // 不存在则补建一条 breached 记录（历史上超时但扫描未及时捕获的情形）。
                        if (existingFirstResponse == null)
                        {
                            await CreateViolationAsync(policy.Id, conversation.Id, SlaConst.ViolationFirstResponse, dueAt, now, SlaConst.StatusBreached,
                                conversation, policy, mailbox, customer, assignee, firstThread);
                            newViolations++;
                            breached++;
                            policy.RecordViolation();
                            modifiedPolicies.Add(policy);
                        }
                        else
                        {
                            if (existingFirstResponse.Status != SlaConst.StatusBreached)
                            {
                                var wasWarning = existingFirstResponse.Status == SlaConst.StatusWarning;
                                existingFirstResponse.Status = SlaConst.StatusBreached;
                                existingFirstResponse.DetectedAt = now;
                                breached++;
                                if (wasWarning)
                                {
                                    await SendSlaNotificationAsync(conversation, policy, mailbox, customer, assignee, firstThread,
                                        SlaConst.ViolationFirstResponse, SlaConst.StatusBreached, dueAt);
                                }
                            }
                            existingFirstResponse.ResolvedAt ??= firstReplyAt;
                        }
                    }
                    else if (policy.IsFirstResponseBreached(baseTime, null, now))
                    {
                        // 已违规
                        if (existingFirstResponse == null)
                        {
                            await CreateViolationAsync(policy.Id, conversation.Id, SlaConst.ViolationFirstResponse, dueAt, now, SlaConst.StatusBreached,
                                conversation, policy, mailbox, customer, assignee, firstThread);
                            newViolations++;
                            breached++;
                            policy.RecordViolation();
                            modifiedPolicies.Add(policy);
                        }
                        else if (existingFirstResponse.Status != SlaConst.StatusBreached)
                        {
                            existingFirstResponse.Status = SlaConst.StatusBreached;
                            existingFirstResponse.DetectedAt = now;
                            breached++;
                            // M-003: warning→breached 升级时发送通知
                            await SendSlaNotificationAsync(conversation, policy, mailbox, customer, assignee, firstThread,
                                SlaConst.ViolationFirstResponse, SlaConst.StatusBreached, dueAt);
                        }
                    }
                    else if (policy.IsFirstResponseWarning(baseTime, null, now))
                    {
                        // 即将到期，创建预警
                        if (existingFirstResponse == null)
                        {
                            await CreateViolationAsync(policy.Id, conversation.Id, SlaConst.ViolationFirstResponse, dueAt, now, SlaConst.StatusWarning,
                                conversation, policy, mailbox, customer, assignee, firstThread);
                            newViolations++;
                        }
                    }
                }

                // 解决时长违规检测
                if (conversation.Status == ConversationStatus.Active || conversation.Status == ConversationStatus.Pending)
                {
                    var dueAt = policy.CalculateResolutionDueAt(conversation.CreatedAt);

                    violationByConversationAndType.TryGetValue((conversation.Id, SlaConst.ViolationResolution), out var existingResolution);
                    if (policy.IsResolutionBreached(conversation.CreatedAt, null, now))
                    {
                        if (existingResolution == null)
                        {
                            await CreateViolationAsync(policy.Id, conversation.Id, SlaConst.ViolationResolution, dueAt, now, SlaConst.StatusBreached,
                                conversation, policy, mailbox, customer, assignee, firstThread);
                            newViolations++;
                            breached++;
                            policy.RecordViolation();
                            modifiedPolicies.Add(policy);
                        }
                        else if (existingResolution.Status != SlaConst.StatusBreached)
                        {
                            existingResolution.Status = SlaConst.StatusBreached;
                            existingResolution.DetectedAt = now;
                            // M-003: warning→breached 升级时发送通知
                            await SendSlaNotificationAsync(conversation, policy, mailbox, customer, assignee, firstThread,
                                SlaConst.ViolationResolution, SlaConst.StatusBreached, dueAt);
                        }
                    }
                    else if (policy.IsResolutionWarning(conversation.CreatedAt, null, now))
                    {
                        if (existingResolution == null)
                        {
                            await CreateViolationAsync(policy.Id, conversation.Id, SlaConst.ViolationResolution, dueAt, now, SlaConst.StatusWarning,
                                conversation, policy, mailbox, customer, assignee, firstThread);
                            newViolations++;
                        }
                    }
                }

                policy.RecordApplication(now);
                modifiedPolicies.Add(policy);
            }

            // modifiedPolicies 中的策略已通过 GetPoliciesForMailboxesAsync 追踪，
            // 无需调用 UpdateAsync，SaveChangesAsync 会自动检测变更
            await _unitOfWork.SaveChangesAsync();
            await transaction.CommitAsync();
            modifiedPolicies.Clear();
            }
            catch
            {
                await transaction.SafeRollbackAsync();
                _unitOfWork.ClearChanges();
                throw;
            }

            page++;
        }

        _logger.LogInformation("SLA 扫描完成：扫描 {Scanned} 个会话，新增 {NewViolations} 条违规，{Breached} 条已超时", scanned, newViolations, breached);
        return (scanned, newViolations, breached);
    }

    public async Task ApplyToConversationAsync(int conversationId)
    {
        // S1: 违规记录创建 + 策略应用必须在单一事务中完成，失败时整体回滚，避免半提交状态
        await using var transaction = await _unitOfWork.BeginTransactionAsync();
        try
        {
            var conversation = await _unitOfWork.Conversations.GetByIdAsync(conversationId);
            if (conversation == null) return;

            var policy = await _unitOfWork.SlaPolicies.GetPolicyForConversationAsync(conversation.MailboxId);
            if (policy == null) return;

            var now = DateTime.UtcNow;

            // L-006: 查询首条客户消息时间作为 baseTime，若无法查询则回退到 CreatedAt
            var threads = await _unitOfWork.Threads.FindAsync(t => t.ConversationId == conversationId);
            var firstCustomerMessageAt = threads
                .Where(t => t.Type == ThreadType.Customer)
                .OrderBy(t => t.CreatedAt)
                .Select(t => (DateTime?)t.CreatedAt)
                .FirstOrDefault();
            var firstResponseBaseTime = firstCustomerMessageAt ?? conversation.CreatedAt;

            var firstResponseDueAt = policy.CalculateFirstResponseDueAt(firstResponseBaseTime);
            var resolutionDueAt = policy.CalculateResolutionDueAt(conversation.CreatedAt);

            // 创建首次响应违规记录（F-C3: 根据是否已超时判断 warning/breached）
            var existingFirst = await _unitOfWork.SlaViolations.GetByConversationAsync(conversationId, SlaConst.ViolationFirstResponse);
            if (existingFirst == null)
            {
                var firstResponseStatus = firstResponseDueAt < now ? SlaConst.StatusBreached : SlaConst.StatusWarning;
                await CreateViolationAsync(policy.Id, conversationId, SlaConst.ViolationFirstResponse, firstResponseDueAt, now, firstResponseStatus);
            }

            // 创建解决时长违规记录（F-C3: 根据是否已超时判断 warning/breached）
            var existingResolution = await _unitOfWork.SlaViolations.GetByConversationAsync(conversationId, SlaConst.ViolationResolution);
            if (existingResolution == null)
            {
                var resolutionStatus = resolutionDueAt < now ? SlaConst.StatusBreached : SlaConst.StatusWarning;
                await CreateViolationAsync(policy.Id, conversationId, SlaConst.ViolationResolution, resolutionDueAt, now, resolutionStatus);
            }

            policy.RecordApplication(now);
            // policy 已通过 GetPolicyForConversationAsync 追踪，无需调用 UpdateAsync
            await _unitOfWork.SaveChangesAsync();
            await transaction.CommitAsync();
        }
        catch
        {
            await transaction.SafeRollbackAsync();
            _unitOfWork.ClearChanges();
            throw;
        }
    }

    private async Task CreateViolationAsync(int policyId, int conversationId, string violationType, DateTime dueAt, DateTime detectedAt, string status)
    {
        await _unitOfWork.SlaViolations.AddAsync(new SlaViolation
        {
            SlaPolicyId = policyId,
            ConversationId = conversationId,
            ViolationType = violationType,
            DueAt = dueAt,
            DetectedAt = detectedAt,
            Status = status,
            CreatedAt = DateTime.UtcNow
        });

        await SendSlaNotificationAsync(conversationId, violationType, status, dueAt);
    }

    /// <summary>
    /// 创建违规记录并使用预加载的关联数据发送通知，避免在批量扫描时产生 N+1 查询。
    /// </summary>
    private async Task CreateViolationAsync(
        int policyId,
        int conversationId,
        string violationType,
        DateTime dueAt,
        DateTime detectedAt,
        string status,
        Conversation conversation,
        SlaPolicy policy,
        Mailbox? mailbox,
        Customer? customer,
        User? assignee,
        ConversationThread? firstThread)
    {
        await _unitOfWork.SlaViolations.AddAsync(new SlaViolation
        {
            SlaPolicyId = policyId,
            ConversationId = conversationId,
            ViolationType = violationType,
            DueAt = dueAt,
            DetectedAt = detectedAt,
            Status = status,
            CreatedAt = DateTime.UtcNow
        });

        await SendSlaNotificationAsync(conversation, policy, mailbox, customer, assignee, firstThread, violationType, status, dueAt);
    }

    private async Task SendSlaNotificationAsync(int conversationId, string violationType, string status, DateTime dueAt)
    {
        try
        {
            var conversation = await _unitOfWork.Conversations.GetByIdAsync(conversationId);
            if (conversation == null) return;

            var policy = await _unitOfWork.SlaPolicies.GetPolicyForConversationAsync(conversation.MailboxId);
            if (policy == null) return;

            var mailbox = await _unitOfWork.Mailboxes.GetByIdAsync(conversation.MailboxId);
            Customer? customer = null;
            if (conversation.CustomerId.HasValue)
            {
                customer = await _unitOfWork.Customers.GetByIdAsync(conversation.CustomerId.Value);
            }
            User? assignee = null;
            if (conversation.UserId.HasValue)
            {
                assignee = await _unitOfWork.Users.GetByIdAsync(conversation.UserId.Value);
            }

            var threads = await _unitOfWork.Threads.FindAsync(t => t.ConversationId == conversationId);
            var firstThread = threads.OrderBy(t => t.CreatedAt).FirstOrDefault();

            await SendSlaNotificationAsync(conversation, policy, mailbox, customer, assignee, firstThread, violationType, status, dueAt);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "发送 SLA 通知失败: ConversationId={ConversationId}", conversationId);
        }
    }

    /// <summary>
    /// 使用预加载的关联数据发送 SLA 通知，避免在批量扫描时重复查询数据库。
    /// </summary>
    private async Task SendSlaNotificationAsync(
        Conversation conversation,
        SlaPolicy policy,
        Mailbox? mailbox,
        Customer? customer,
        User? assignee,
        ConversationThread? firstThread,
        string violationType,
        string status,
        DateTime dueAt)
    {
        try
        {
            var frontendUrl = _frontendUrlResolver.GetFrontendUrl();
            var now = DateTime.UtcNow;
            var remainingHours = (dueAt - now).TotalHours;
            var slaRemaining = SlaConst.FormatHours(remainingHours);

            var variables = new Dictionary<string, string>
            {
                ["subject"] = conversation.Subject ?? string.Empty,
                ["conversation_id"] = conversation.Id.ToString(),
                ["conversation_url"] = $"{frontendUrl}/conversations/{conversation.Id}",
                ["customer_name"] = customer != null ? $"{customer.FirstName} {customer.LastName}".Trim() : (conversation.CustomerEmail ?? string.Empty),
                ["customer_email"] = conversation.CustomerEmail ?? string.Empty,
                ["user_name"] = assignee != null ? $"{assignee.FirstName} {assignee.LastName}".Trim() : string.Empty,
                ["user_email"] = assignee?.Email ?? string.Empty,
                ["mailbox_name"] = mailbox?.Name ?? string.Empty,
                ["mailbox_email"] = mailbox?.Email ?? string.Empty,
                ["message_body"] = firstThread?.Body ?? string.Empty,
                ["created_at"] = conversation.CreatedAt.ToString("yyyy-MM-dd HH:mm"),
                ["company_name"] = _configuration["CompanyName"] ?? "拓德科技",
                ["sla_name"] = policy.Name ?? string.Empty,
                ["sla_level"] = policy.Priority.ToString(),
                ["deadline"] = dueAt.ToString("yyyy-MM-dd HH:mm"),
                ["sla_deadline"] = dueAt.ToString("yyyy-MM-dd HH:mm"),
                ["sla_remaining"] = slaRemaining,
                ["remaining_time"] = slaRemaining,
                ["violation_type"] = violationType switch
                {
                    "first_response" => "首次响应超时",
                    "resolution" => "解决时长超时",
                    _ => violationType
                },
                // 升级相关变量：保证 SLA 通知模板中 {escalation_reason} 等占位符能正确渲染
                ["escalation_reason"] = status == "breached"
                    ? $"SLA {SlaConst.GetViolationTypeLabel(violationType)}违规触发升级"
                    : $"SLA {SlaConst.GetViolationTypeLabel(violationType)}即将超时预警",
                ["escalated_by"] = "SLA 监控系统",
                ["escalated_at"] = now.ToString("yyyy-MM-dd HH:mm"),
                ["priority"] = conversation.Priority.ToString()
            };

            var eventType = status == "breached" ? "sla_violation" : "sla_warning";
            await _notificationService.SendNotificationAsync(new SendNotificationRequest
            {
                EventType = eventType,
                MailboxId = conversation.MailboxId,
                Variables = variables
            });

            // SLA 违规（breached）时，额外触发 ticket_escalated 事件，
            // 让"工单升级通知管理员"规则生效，便于管理层介入处理。
            if (status == "breached")
            {
                try
                {
                    await _notificationService.SendNotificationAsync(new SendNotificationRequest
                    {
                        EventType = "ticket_escalated",
                        MailboxId = conversation.MailboxId,
                        Variables = variables
                    });
                    _logger.LogInformation("SLA 违规已触发工单升级通知: ConversationId={ConversationId}", conversation.Id);
                }
                catch (Exception exEscalation)
                {
                    _logger.LogWarning(exEscalation, "触发工单升级通知失败（不影响主流程）: ConversationId={ConversationId}", conversation.Id);
                }
            }

            _logger.LogInformation("SLA 通知已发送: ConversationId={ConversationId}, EventType={EventType}", conversation.Id, eventType);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "发送 SLA 通知失败: ConversationId={ConversationId}", conversation.Id);
        }
    }

    // ====== 转换 ======
    private SlaPolicyDto ToPolicyDto(SlaPolicy entity)
    {
        return new SlaPolicyDto
        {
            Id = entity.Id,
            Name = entity.Name,
            Description = entity.Description,
            MailboxId = entity.MailboxId,
            MailboxName = entity.Mailbox?.Name,
            FirstResponseHours = entity.FirstResponseHours,
            ResolutionHours = entity.ResolutionHours,
            BusinessHoursOnly = entity.BusinessHoursOnly,
            WorkDays = entity.WorkDays,
            WorkStartHour = entity.WorkStartHour,
            WorkEndHour = entity.WorkEndHour,
            Priority = entity.Priority,
            IsActive = entity.IsActive,
            SortOrder = entity.SortOrder,
            AppliedCount = entity.AppliedCount,
            ViolationCount = entity.ViolationCount,
            LastAppliedAt = entity.LastAppliedAt,
            CreatedAt = entity.CreatedAt,
            UpdatedAt = entity.UpdatedAt,
            DeletedAt = entity.DeletedAt,
            FirstResponseLabel = SlaConst.FormatHours(entity.FirstResponseHours, TranslateDuration),
            ResolutionLabel = SlaConst.FormatHours(entity.ResolutionHours, TranslateDuration)
        };
    }

    /// <summary>
    /// 把 <see cref="SlaConst.FormatHours"/> 的时长模板接到当前语言的 Messages 词条上：
    /// 词条缺失或未注入本地化服务时回退 zh-Hans 原文模板，数字始终走槽位。
    /// </summary>
    private string TranslateDuration(string key, string zhFallback, object[]? args) =>
        Translate(key, zhFallback, args ?? Array.Empty<object>());

    private SlaViolationDto ToViolationDto(SlaViolation entity)
    {
        var now = DateTime.UtcNow;
        var remainingMinutes = (entity.DueAt - now).TotalMinutes;
        if (entity.ResolvedAt.HasValue)
        {
            remainingMinutes = (entity.DueAt - entity.ResolvedAt.Value).TotalMinutes;
        }

        return new SlaViolationDto
        {
            Id = entity.Id,
            SlaPolicyId = entity.SlaPolicyId,
            SlaPolicyName = entity.SlaPolicy?.Name ?? string.Empty,
            ConversationId = entity.ConversationId,
            ConversationSubject = entity.Conversation?.Subject,
            ViolationType = entity.ViolationType,
            ViolationTypeLabel = LocalizeViolationType(entity.ViolationType),
            DueAt = entity.DueAt,
            DetectedAt = entity.DetectedAt,
            ResolvedAt = entity.ResolvedAt,
            Status = entity.Status,
            StatusLabel = LocalizeViolationStatus(entity.Status),
            RemainingMinutes = remainingMinutes,
            CreatedAt = entity.CreatedAt
        };
    }

    /// <summary>
    /// 违规类型标签：走 Messages 资源（en / zh-Hans）；未注入本地化服务或词条缺失时
    /// 回退 <see cref="SlaConst.GetViolationTypeLabel"/> 的中文原文，保证 zh-Hans 输出逐字不变。
    /// </summary>
    private string LocalizeViolationType(string type)
    {
        var key = type switch
        {
            SlaConst.ViolationFirstResponse => "SlaViolationTypeFirstResponse",
            SlaConst.ViolationResolution => "SlaViolationTypeResolution",
            _ => null
        };
        return key is null ? type : Translate(key, SlaConst.GetViolationTypeLabel(type));
    }

    /// <summary>
    /// 违规状态标签：同上，回退 <see cref="SlaConst.GetViolationStatusLabel"/> 的中文原文。
    /// </summary>
    private string LocalizeViolationStatus(string status)
    {
        var key = status switch
        {
            SlaConst.StatusWarning => "SlaViolationStatusWarning",
            SlaConst.StatusBreached => "SlaViolationStatusBreached",
            SlaConst.StatusResolved => "SlaViolationStatusResolved",
            _ => null
        };
        return key is null ? status : Translate(key, SlaConst.GetViolationStatusLabel(status));
    }

    /// <summary>
    /// 取词条文本：本地化服务在词条缺失时会返回键名本身，此时回退给定中文原文，避免把键名吐给前端。
    /// </summary>
    private string Translate(string key, string zhFallback, params object[] args)
    {
        if (_localization is null) return FormatFallback(zhFallback, args);
        var value = _localization[key];
        if (string.IsNullOrWhiteSpace(value) || string.Equals(value, key, StringComparison.Ordinal))
        {
            return FormatFallback(zhFallback, args);
        }
        return args.Length > 0 ? string.Format(value, args) : value;
    }

    /// <summary>
    /// 词条缺失时的中文回退：有槽位时按 zh-Hans 原文模板填充，无槽位时原样返回。
    /// </summary>
    private static string FormatFallback(string zhFallback, object[] args) =>
        args.Length > 0 ? string.Format(zhFallback, args) : zhFallback;
}
