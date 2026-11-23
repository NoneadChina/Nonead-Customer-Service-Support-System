#nullable enable
using System.Globalization;
using System.Text.Json;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using NoneadCSS.Application.Common;
using NoneadCSS.Application.Configuration;
using NoneadCSS.Application.DTOs.Requests;
using NoneadCSS.Application.DTOs.Responses;
using NoneadCSS.Application.Exceptions;
using NoneadCSS.Application.Mappings;
using NoneadCSS.Domain;
using NoneadCSS.Domain.Entities;
using NoneadCSS.Domain.Enums;

namespace NoneadCSS.Application.Services.Calendar;

public class CalendarMutationService : ICalendarMutationService
{
    private readonly IUnitOfWork _unitOfWork;
    private readonly INotificationService _notificationService;
    private readonly ICurrentUserAccessor _currentUserAccessor;
    private readonly IAuditService _auditService;
    private readonly INotificationHubService _notificationHubService;
    private readonly ILogger<CalendarMutationService> _logger;
    private readonly CalendarAuthorizationService _auth;
    private readonly IOptions<CalendarOptions> _calendarOptions;
    private readonly ICalendarConflictService _conflict;
    private readonly ICalendarQueryCache? _cache;
    private readonly Lazy<ICalendarQueryService> _queryService;

    public CalendarMutationService(
        IUnitOfWork unitOfWork,
        INotificationService notificationService,
        ICurrentUserAccessor currentUserAccessor,
        IAuditService auditService,
        INotificationHubService notificationHubService,
        ILogger<CalendarMutationService> logger,
        CalendarAuthorizationService auth,
        IOptions<CalendarOptions> calendarOptions,
        ICalendarConflictService conflict,
        Lazy<ICalendarQueryService> queryService,
        ICalendarQueryCache? cache = null)
    {
        ArgumentNullException.ThrowIfNull(unitOfWork);
        ArgumentNullException.ThrowIfNull(notificationService);
        ArgumentNullException.ThrowIfNull(currentUserAccessor);
        ArgumentNullException.ThrowIfNull(auditService);
        ArgumentNullException.ThrowIfNull(notificationHubService);
        ArgumentNullException.ThrowIfNull(logger);
        ArgumentNullException.ThrowIfNull(auth);
        ArgumentNullException.ThrowIfNull(calendarOptions);
        ArgumentNullException.ThrowIfNull(conflict);
        _unitOfWork = unitOfWork;
        _notificationService = notificationService;
        _currentUserAccessor = currentUserAccessor;
        _auditService = auditService;
        _notificationHubService = notificationHubService;
        _logger = logger;
        _auth = auth;
        _calendarOptions = calendarOptions;
        _conflict = conflict;
        _queryService = queryService;
        _cache = cache;
    }

    /// <summary>
    /// 显式校验乐观锁 RowVersion。
    /// 如果请求携带 ifMatch，将其 base64 解码后与实体当前 RowVersion 逐字节比较；
    /// 不匹配时抛出 CalendarConcurrencyException 并附带当前最新快照（DTO，已按用户权限脱敏）。
    /// 安全策略：ifMatch 非空但 base64 非法时视为乐观锁校验失败（防止攻击者通过故意发送非法 base64 绕过乐观锁实现并发覆盖写）。
    /// ifMatch 为空时跳过校验，保持旧客户端兼容。
    /// </summary>
    private void CheckRowVersion(CalendarEvent evt, string? ifMatch, [System.Runtime.CompilerServices.CallerMemberName] string caller = "")
    {
        if (string.IsNullOrEmpty(ifMatch)) return;

        byte[] expected;
        try
        {
            expected = Convert.FromBase64String(ifMatch);
        }
        catch (FormatException)
        {
            // 安全修复：非法 base64 不再降级为强制覆盖写，而是拒绝请求并返回最新快照供前端刷新
            _logger.LogWarning("CheckRowVersion: 非法 base64 ifMatch={IfMatch} (caller={Caller}, event={EventId})，拒绝请求以防止乐观锁绕过", ifMatch, caller, evt.Id);
            var latestOnInvalid = evt.ToDto().SanitizeForUser(_currentUserAccessor.UserId, _currentUserAccessor.IsSuperAdmin);
            throw new CalendarConcurrencyException(evt.Id, latestOnInvalid);
        }

        var current = evt.RowVersion ?? Array.Empty<byte>();
        if (current.AsSpan().SequenceEqual(expected.AsSpan())) return;

        _logger.LogInformation(
            "CheckRowVersion: 乐观锁冲突 caller={Caller} event={EventId} expectedLength={ExpectedLen} currentLength={CurrentLen}",
            caller, evt.Id, expected.Length, current.Length);

        var latest = evt.ToDto().SanitizeForUser(_currentUserAccessor.UserId, _currentUserAccessor.IsSuperAdmin);
        throw new CalendarConcurrencyException(evt.Id, latest);
    }

    public async Task<CalendarEventDto> CreateAsync(CreateCalendarEventRequest request, CancellationToken ct = default)
    {
        var currentUserId = _currentUserAccessor.UserId;

        request = _auth.FilterAndCheck(request);

        await _auth.ValidateCreateAccessAsync(request.UserId).ConfigureAwait(false);

        if (request.MailboxId.HasValue && !await _currentUserAccessor.CanAccessMailboxAsync(request.MailboxId).ConfigureAwait(false))
        {
            throw new UnauthorizedAccessException("无权向该邮箱创建日程");
        }

        ValidateFields(request);

        // 在落库前归一化 EventType（白名单内保留大小写、未知降级 other+Warning，绝不抛 ArgumentException）
        ApplyNormalizedEventType(request);

        var eventUserId = request.UserId ?? currentUserId;

        // E#1 冲突检测：先单次检测 + 若为重复日程再过前 10 个实例预检
        var parsedStartAt = ParseDate(request.StartAt);
        var parsedEndAt = string.IsNullOrEmpty(request.EndAt) ? null : (DateTime?)ParseDate(request.EndAt);
        await _conflict.EnforceHardConflictAsync(parsedStartAt, parsedEndAt, eventUserId, excludeEventId: null, request.IsAllDay, ct)
            .ConfigureAwait(false);
        if (request.IsRecurring)
        {
            await _conflict.EnforceRecurringCreateConflictAsync(parsedStartAt, parsedEndAt, eventUserId, request, ct)
                .ConfigureAwait(false);
        }

        var evt = new CalendarEvent
        {
            Title = request.Title,
            Description = request.Description,
            EventType = request.EventType,
            StartAt = ParseDate(request.StartAt),
            EndAt = string.IsNullOrEmpty(request.EndAt) ? null : ParseDate(request.EndAt),
            IsAllDay = request.IsAllDay,
            ConversationId = request.ConversationId,
            MailboxId = request.MailboxId,
            UserId = eventUserId,
            CreatedBy = currentUserId,
            Color = request.Color,
            Status = "pending",
            IsRecurring = request.IsRecurring,
            RecurrenceType = request.RecurrenceType,
            RecurrenceInterval = request.RecurrenceInterval ?? 1,
            RecurrenceEndDate = string.IsNullOrEmpty(request.RecurrenceEndDate) ? null : ParseDate(request.RecurrenceEndDate),
            RecurrenceCount = request.RecurrenceCount,
            ReminderMinutes = request.ReminderMinutes,
            IsReminderSent = false,
            ReminderEmails = string.IsNullOrWhiteSpace(request.ReminderEmails) ? null : request.ReminderEmails.Trim(),
            Priority = request.Priority,
            Progress = request.Progress,
            Location = request.Location,
            MeetingUrl = request.MeetingUrl,
            AttendeesJson = request.Attendees.Count > 0 ? JsonSerializer.Serialize(request.Attendees) : null,
            SubTasksJson = request.SubTasks.Count > 0 ? JsonSerializer.Serialize(request.SubTasks) : null,
            RelatedEventId = request.RelatedEventId,
            RecurrenceExclusionsJson = request.RecurrenceExclusions != null ? JsonSerializer.Serialize(request.RecurrenceExclusions) : null,
            // 事件类型专属字段持久化（2026-08-17）
            HostUserIdsJson = request.HostUserIds.Count > 0 ? JsonSerializer.Serialize(request.HostUserIds) : null,
            AgendaIdsJson = request.AgendaIds.Count > 0 ? JsonSerializer.Serialize(request.AgendaIds) : null,
            TaskOwnerId = request.TaskOwnerId,
            DueAt = string.IsNullOrEmpty(request.DueAt) ? null : ParseDate(request.DueAt),
            ReminderType = string.IsNullOrWhiteSpace(request.ReminderType) ? null : request.ReminderType.Trim(),
            WeekdaysSelectedJson = request.WeekdaysSelected.Count > 0 ? JsonSerializer.Serialize(request.WeekdaysSelected) : null,
            RecurrenceRule = string.IsNullOrWhiteSpace(request.RecurrenceRule) ? null : request.RecurrenceRule.Trim(),
            CreatedAt = DateTime.UtcNow,
            UpdatedAt = DateTime.UtcNow,
            // P4 修复：ValueGeneratedNever 下显式赋值 RowVersion，确保 INSERT 时非空且乐观锁立即可用
            RowVersion = Guid.NewGuid().ToByteArray()
        };

        // T000100 修复点 1（Create）：邮箱参会人与 ID 型参会人双写归一化。
        var (normAttendeeIds, normReminderEmails) =
            NormalizeAttendeesAndReminderEmails(request.Attendees, request.ReminderEmails);
        evt.AttendeesJson = normAttendeeIds.Count > 0 ? JsonSerializer.Serialize(normAttendeeIds) : null;
        evt.ReminderEmails = normReminderEmails;

        await _unitOfWork.CalendarEvents.AddAsync(evt).ConfigureAwait(false);
        await _unitOfWork.SaveChangesAsync().ConfigureAwait(false);

        await LogAuditAndNotifyAsync("calendar.create", evt).ConfigureAwait(false);

        _cache?.InvalidateAllForUser(request.UserId ?? currentUserId);

        return evt.ToDto();
    }

    public async Task<CalendarEventDto?> UpdateAsync(int id, UpdateCalendarEventRequest request, CancellationToken ct = default)
    {
        var evt = await _unitOfWork.CalendarEvents.GetByIdTrackedAsync(id).ConfigureAwait(false);
        if (evt == null)
        {
            _logger.LogWarning("UpdateAsync 404-A: Event {EventId} not found by GetByIdTrackedAsync (UserId={UserId}, IsSuperAdmin={IsSuperAdmin})", id, _currentUserAccessor.UserId, _currentUserAccessor.IsSuperAdmin);
            return null;
        }

        if (!await _currentUserAccessor.CanAccessMailboxAsync(evt.MailboxId).ConfigureAwait(false))
        {
            _logger.LogWarning("UpdateAsync 404-B: User {UserId} cannot access mailbox {MailboxId} for event {EventId} (IsSuperAdmin={IsSuperAdmin})", _currentUserAccessor.UserId, evt.MailboxId, id, _currentUserAccessor.IsSuperAdmin);
            return null;
        }

        await _auth.ValidateWriteAccessAsync(evt).ConfigureAwait(false);

        // P3: 显式乐观锁比对（在变更前检查，避免读-改-写期间其他并发写入被静默覆盖）
        CheckRowVersion(evt, request.IfMatch);

        request = _auth.FilterAndCheck(request);

        var currentUserId = _currentUserAccessor.UserId;

        ValidateFields(request);

        // 同 CreateAsync：EventType 归一化（白名单 + 降级 Warning，不抛 ArgumentException）
        ApplyNormalizedEventType(request);

        var updateMode = Enum.TryParse<RecurrenceUpdateMode>(request.UpdateMode, true, out var um)
            ? um
            : RecurrenceUpdateMode.Single;

        // E#1 服务端冲突检测（更新场景，排除自身 / 父自身）
        var updateStartAt = ParseDate(request.StartAt);
        var updateEndAt = string.IsNullOrEmpty(request.EndAt) ? null : (DateTime?)ParseDate(request.EndAt);
        var updateUserId = request.UserId ?? evt.UserId;
        if (updateMode == RecurrenceUpdateMode.Single)
        {
            await _conflict.EnforceHardConflictAsync(updateStartAt, updateEndAt, updateUserId, id, request.IsAllDay, ct)
                .ConfigureAwait(false);
        }
        else
        {
            var parentIdForCheck = evt.RecurrenceParentId ?? evt.Id;
            await _conflict.EnforceHardConflictAsync(updateStartAt, updateEndAt, updateUserId, parentIdForCheck, request.IsAllDay, ct)
                .ConfigureAwait(false);
        }

        var oldValues = JsonSerializer.Serialize(evt);

        if (request.UserId.HasValue && request.UserId != evt.UserId)
        {
            await _auth.ValidateCreateAccessAsync(request.UserId).ConfigureAwait(false);
        }

        CalendarEvent? auditEntity = evt;
        int? resultId = id;

        using var transaction = await _unitOfWork.BeginTransactionAsync().ConfigureAwait(false);
        try
        {
            if (evt.IsRecurring && updateMode != RecurrenceUpdateMode.Single)
            {
                var parentId = evt.RecurrenceParentId ?? evt.Id;
                var parentEvt = await _unitOfWork.CalendarEvents.GetByIdTrackedAsync(parentId).ConfigureAwait(false);

                if (parentEvt != null)
                {
                    if (updateMode == RecurrenceUpdateMode.All)
                    {
                        UpdateEventFromRequest(parentEvt, request);

                        var instances = await _unitOfWork.CalendarEvents.GetByParentIdTrackedAsync(parentId).ConfigureAwait(false);
                        foreach (var inst in instances)
                        {
                            UpdateEventFromRequest(inst, request);
                            inst.RecurrenceType = parentEvt.RecurrenceType;
                            inst.RecurrenceInterval = parentEvt.RecurrenceInterval;
                            inst.RecurrenceEndDate = parentEvt.RecurrenceEndDate;
                            inst.RecurrenceCount = parentEvt.RecurrenceCount;
                            inst.IsRecurring = parentEvt.IsRecurring;
                        }
                    }
                    else if (updateMode == RecurrenceUpdateMode.Following)
                    {
                        var fromDate = evt.StartAt;
                        var following = await _unitOfWork.CalendarEvents.GetFollowingInstancesTrackedAsync(parentId, fromDate).ConfigureAwait(false);

                        if (parentEvt.RecurrenceEndDate.HasValue && fromDate > parentEvt.RecurrenceEndDate.Value)
                        {
                            throw new ArgumentException("following 模式的起始日期必须早于或等于原重复结束日期");
                        }

                        parentEvt.RecurrenceEndDate = fromDate.AddMinutes(-1);

                        // T000100 修复点 3（Following 模式的新父实例创建）：
                        // 之前只赋值了"基础字段"，ReminderEmails/AttendeesJson/HostUserIdsJson/
                        // AgendaIdsJson/WeekdaysSelectedJson/Priority/Location/MeetingUrl 等被
                        // 静默丢弃 → 在"从某天起改参会人"的重复日程场景中，保存成功但新父实例
                        // 的参会人永远为空，导致 round-trip 显示"数据没了"。
                        var normNewParent = NormalizeAttendeesAndReminderEmails(request.Attendees, request.ReminderEmails);
                        var newParent = new CalendarEvent
                        {
                            Title = request.Title,
                            Description = request.Description,
                            EventType = request.EventType,
                            StartAt = fromDate,
                            EndAt = string.IsNullOrEmpty(request.EndAt) ? null : ParseDate(request.EndAt),
                            IsAllDay = request.IsAllDay,
                            ConversationId = request.ConversationId,
                            MailboxId = request.MailboxId,
                            UserId = request.UserId,
                            CreatedBy = currentUserId,
                            Color = request.Color,
                            Status = request.Status,
                            IsRecurring = request.IsRecurring,
                            RecurrenceType = request.RecurrenceType,
                            RecurrenceInterval = request.RecurrenceInterval ?? 1,
                            RecurrenceEndDate = string.IsNullOrEmpty(request.RecurrenceEndDate) ? null : ParseDate(request.RecurrenceEndDate),
                            RecurrenceCount = request.RecurrenceCount,
                            ReminderMinutes = request.ReminderMinutes,
                            IsReminderSent = false,
                            ReminderEmails = normNewParent.reminderEmailsCsv,
                            Priority = request.Priority,
                            Progress = request.Progress,
                            Location = request.Location,
                            MeetingUrl = request.MeetingUrl,
                            AttendeesJson = normNewParent.attendeeIds.Count > 0 ? JsonSerializer.Serialize(normNewParent.attendeeIds) : null,
                            SubTasksJson = request.SubTasks.Count > 0 ? JsonSerializer.Serialize(request.SubTasks) : null,
                            RelatedEventId = request.RelatedEventId,
                            RecurrenceExclusionsJson = request.RecurrenceExclusions != null ? JsonSerializer.Serialize(request.RecurrenceExclusions) : null,
                            HostUserIdsJson = request.HostUserIds.Count > 0 ? JsonSerializer.Serialize(request.HostUserIds) : null,
                            AgendaIdsJson = request.AgendaIds.Count > 0 ? JsonSerializer.Serialize(request.AgendaIds) : null,
                            TaskOwnerId = request.TaskOwnerId,
                            DueAt = string.IsNullOrEmpty(request.DueAt) ? null : ParseDate(request.DueAt),
                            ReminderType = string.IsNullOrWhiteSpace(request.ReminderType) ? null : request.ReminderType.Trim(),
                            WeekdaysSelectedJson = request.WeekdaysSelected.Count > 0 ? JsonSerializer.Serialize(request.WeekdaysSelected) : null,
                            RecurrenceRule = string.IsNullOrWhiteSpace(request.RecurrenceRule) ? null : request.RecurrenceRule.Trim(),
                            CreatedAt = DateTime.UtcNow,
                            UpdatedAt = DateTime.UtcNow,
                            RowVersion = Guid.NewGuid().ToByteArray()
                        };
                        await _unitOfWork.CalendarEvents.AddAsync(newParent).ConfigureAwait(false);
                        await _unitOfWork.SaveChangesAsync().ConfigureAwait(false);

                        foreach (var inst in following)
                        {
                            var timeOffset = inst.StartAt - fromDate;
                            inst.RecurrenceParentId = newParent.Id;
                            UpdateEventFromRequest(inst, request);
                            inst.StartAt = newParent.StartAt + timeOffset;
                            if (inst.EndAt.HasValue && newParent.EndAt.HasValue)
                            {
                                inst.EndAt = newParent.EndAt.Value + timeOffset;
                            }
                        }
                    }

                    await _unitOfWork.SaveChangesAsync().ConfigureAwait(false);
                    await transaction.CommitAsync().ConfigureAwait(false);

                    auditEntity = parentEvt;
                    resultId = parentId;
                }
                else
                {
                    UpdateEventFromRequest(evt, request);
                    evt.UpdatedAt = DateTime.UtcNow;

                    await _unitOfWork.SaveChangesAsync().ConfigureAwait(false);
                    await transaction.CommitAsync().ConfigureAwait(false);
                }
            }
            else
            {
                UpdateEventFromRequest(evt, request);
                evt.UpdatedAt = DateTime.UtcNow;

                await _unitOfWork.SaveChangesAsync().ConfigureAwait(false);
                await transaction.CommitAsync().ConfigureAwait(false);
            }
        }
        catch
        {
            await transaction.SafeRollbackAsync().ConfigureAwait(false);
            throw;
        }

        await LogAuditAndNotifyAsync("calendar.update", auditEntity, oldValues).ConfigureAwait(false);

        _cache?.InvalidateAllForUser(request.UserId ?? auditEntity?.UserId ?? currentUserId);

        // 通过 Lazy<ICalendarQueryService> 注入，避免手动 new（原注释描述的循环依赖实际不存在）
        return await _queryService.Value.GetByIdAsync(resultId.Value, ct).ConfigureAwait(false);
    }

    /// <summary>
    /// 删除日历事件（软删除）。
    /// P0-2 RowVersion 并发安全：如果调用方传入 <paramref name="ifMatch"/>（base64 编码的 RowVersion），
    /// 会在写入前显式比对，不匹配则抛 <see cref="CalendarConcurrencyException"/> 返回 409。
    /// </summary>
    /// <param name="id">事件 ID</param>
    /// <param name="deleteMode">single / following / all</param>
    /// <param name="ifMatch">可选的 If-Match ETag（Base64 RowVersion）</param>
    public async Task<bool> DeleteAsync(int id, string deleteMode = "single", string? ifMatch = null, CancellationToken ct = default)
    {
        var evt = await _unitOfWork.CalendarEvents.GetByIdTrackedAsync(id).ConfigureAwait(false);
        if (evt == null) return false;

        if (!await _currentUserAccessor.CanAccessMailboxAsync(evt.MailboxId).ConfigureAwait(false))
            return false;

        await _auth.ValidateWriteAccessAsync(evt).ConfigureAwait(false);

        // P0-2: 乐观锁——写前比对版本，防并发删除/修改被静默覆盖
        CheckRowVersion(evt, ifMatch);

        var mode = Enum.TryParse<RecurrenceUpdateMode>(deleteMode, true, out var dm)
            ? dm
            : RecurrenceUpdateMode.Single;

        if (evt.IsRecurring && mode != RecurrenceUpdateMode.Single)
        {
            var parentId = evt.RecurrenceParentId ?? evt.Id;
            // P0-2：改为 Tracked 加载父事件，确保 EF 在 SaveChanges 时自动
            //        1) 带原 RowVersion 做 WHERE 乐观锁
            //        2) 触发 SQL Server 自动 bump 父事件 RowVersion
            var parentEvt = await _unitOfWork.CalendarEvents.GetByIdTrackedAsync(parentId).ConfigureAwait(false);

            if (parentEvt != null)
            {
                using var transaction = await _unitOfWork.BeginTransactionAsync().ConfigureAwait(false);
                try
                {
                    if (mode == RecurrenceUpdateMode.All)
                    {
                        var instances = await _unitOfWork.CalendarEvents.GetByParentIdTrackedAsync(parentId).ConfigureAwait(false);
                        parentEvt.SoftDelete();
                        foreach (var inst in instances)
                        {
                            inst.SoftDelete();
                        }
                        await _unitOfWork.SaveChangesAsync().ConfigureAwait(false);
                    }
                    else if (mode == RecurrenceUpdateMode.Following)
                    {
                        var fromDate = evt.StartAt;
                        var following = await _unitOfWork.CalendarEvents.GetFollowingInstancesTrackedAsync(parentId, fromDate).ConfigureAwait(false);

                        if (parentEvt.RecurrenceEndDate.HasValue && fromDate > parentEvt.RecurrenceEndDate.Value)
                        {
                            throw new ArgumentException("following 模式的起始日期必须早于或等于原重复结束日期");
                        }

                        parentEvt.RecurrenceEndDate = fromDate.AddMinutes(-1);

                        foreach (var inst in following)
                        {
                            inst.SoftDelete();
                        }
                        await _unitOfWork.SaveChangesAsync().ConfigureAwait(false);
                    }

                    await transaction.CommitAsync().ConfigureAwait(false);
                    await LogAuditAndNotifyAsync("calendar.delete", parentEvt).ConfigureAwait(false);
                    InvalidateCacheForEventAndCurrentUser(parentEvt.UserId ?? parentEvt.CreatedBy);
                    return true;
                }
                catch
                {
                    await transaction.SafeRollbackAsync().ConfigureAwait(false);
                    throw;
                }
            }
        }

        var oldValues = JsonSerializer.Serialize(evt);
        evt.SoftDelete();
        await _unitOfWork.SaveChangesAsync().ConfigureAwait(false);

        await LogAuditAndNotifyAsync("calendar.delete", evt, oldValues).ConfigureAwait(false);

        InvalidateCacheForEventAndCurrentUser(evt.UserId ?? evt.CreatedBy);

        return true;
    }

    /// <summary>
    /// 更新日历事件的状态（如 pending → in_progress / completed / cancelled / missed）。
    /// P0-1: 支持显式乐观锁，传入 <paramref name="ifMatch"/> 后会在变更前
    ///       与实体当前 RowVersion 逐字节比对，冲突返回 409，避免"状态竞争覆盖"。
    /// </summary>
    public async Task<bool> UpdateStatusAsync(int id, string status, string? ifMatch = null, CancellationToken ct = default)
    {
        var evt = await _unitOfWork.CalendarEvents.GetByIdTrackedAsync(id).ConfigureAwait(false);
        if (evt == null) return false;

        await _auth.ValidateWriteAccessAsync(evt).ConfigureAwait(false);

        // P0-1: 乐观锁——写前比对，避免 A/B 两端同时改不同状态时后写覆盖先写
        CheckRowVersion(evt, ifMatch);

        ValidateStatusTransition(evt.Status, status);

        var oldValues = JsonSerializer.Serialize(new { Status = evt.Status });
        evt.Status = status;
        evt.UpdatedAt = DateTime.UtcNow;
        await _unitOfWork.SaveChangesAsync().ConfigureAwait(false);

        await LogAuditAndNotifyAsync("calendar.status_change", evt, oldValues).ConfigureAwait(false);

        InvalidateCacheForEventAndCurrentUser(evt.UserId ?? evt.CreatedBy);

        return true;
    }

    /// <summary>
    /// 复制日历事件：基于已存在事件创建新事件。
    /// 设计要点：
    /// 1. 复制为单个非重复事件（IsRecurring=false），避免产生重复实例。
    /// 2. 标题加 " (副本)" 后缀（除非 request.Title 显式指定）。
    /// 3. Status 重置为 pending，IsReminderSent 重置为 false。
    /// 4. 支持可选 request 调整 startAt/endAt/title。
    /// 5. 复用冲突检测避免时间冲突。
    /// </summary>
    public async Task<CalendarEventDto> DuplicateAsync(int id, DuplicateCalendarEventRequest? request = null, CancellationToken ct = default)
    {
        var source = await _unitOfWork.CalendarEvents.GetByIdAsync(id).ConfigureAwait(false);
        if (source == null) throw new InvalidOperationException($"事件 {id} 不存在");

        if (!await _currentUserAccessor.CanAccessMailboxAsync(source.MailboxId).ConfigureAwait(false))
        {
            throw new UnauthorizedAccessException("无权复制该邮箱下的日程");
        }

        await _auth.ValidateWriteAccessAsync(source).ConfigureAwait(false);

        var currentUserId = _currentUserAccessor.UserId;

        // 解析新时间：优先使用 request 传入值，否则使用原事件值
        var newStartAt = !string.IsNullOrEmpty(request?.StartAt) ? ParseDate(request.StartAt) : source.StartAt;
        var newEndAt = !string.IsNullOrEmpty(request?.EndAt)
            ? ParseDate(request.EndAt)
            : (source.EndAt.HasValue ? (DateTime?)(newStartAt + (source.EndAt.Value - source.StartAt)) : null);

        // 标题：优先使用 request.Title，否则原标题 + " (副本)"
        var newTitle = !string.IsNullOrWhiteSpace(request?.Title)
            ? request.Title.Trim()
            : $"{source.Title} (副本)";

        var eventUserId = source.UserId ?? currentUserId;

        // 复制是显式操作：排除源事件 ID 后做冲突检测，避免与自身时间完全相同而误报硬冲突。
        // 若用户指定的新时间与其他已有事件硬冲突，仍会抛 CalendarConflictException 提示。
        await _conflict.EnforceHardConflictAsync(newStartAt, newEndAt, eventUserId, excludeEventId: source.Id, source.IsAllDay, ct)
            .ConfigureAwait(false);

        var evt = new CalendarEvent
        {
            Title = newTitle,
            Description = source.Description,
            EventType = source.EventType,
            StartAt = newStartAt,
            EndAt = newEndAt,
            IsAllDay = source.IsAllDay,
            ConversationId = source.ConversationId,
            MailboxId = source.MailboxId,
            UserId = eventUserId,
            CreatedBy = currentUserId,
            Color = source.Color,
            Status = "pending",
            // 复制为单个非重复事件
            IsRecurring = false,
            RecurrenceType = null,
            RecurrenceInterval = null,
            RecurrenceEndDate = null,
            RecurrenceCount = null,
            RecurrenceParentId = null,
            RecurrenceExclusionsJson = null,
            // 复制提醒配置，但重置发送状态
            ReminderMinutes = source.ReminderMinutes,
            IsReminderSent = false,
            ReminderEmails = source.ReminderEmails,
            ReminderType = source.ReminderType,
            // 复制扩展字段
            Priority = source.Priority,
            Progress = source.Progress,
            Location = source.Location,
            MeetingUrl = source.MeetingUrl,
            AttendeesJson = source.AttendeesJson,
            SubTasksJson = source.SubTasksJson,
            RelatedEventId = source.RelatedEventId,
            HostUserIdsJson = source.HostUserIdsJson,
            AgendaIdsJson = source.AgendaIdsJson,
            TaskOwnerId = source.TaskOwnerId,
            DueAt = source.DueAt,
            WeekdaysSelectedJson = source.WeekdaysSelectedJson,
            RecurrenceRule = source.RecurrenceRule,
            CreatedAt = DateTime.UtcNow,
            UpdatedAt = DateTime.UtcNow
        };

        await _unitOfWork.CalendarEvents.AddAsync(evt).ConfigureAwait(false);
        await _unitOfWork.SaveChangesAsync().ConfigureAwait(false);

        await LogAuditAndNotifyAsync("calendar.duplicate", evt).ConfigureAwait(false);

        _cache?.InvalidateAllForUser(eventUserId);

        return evt.ToDto();
    }

    /// <summary>
    /// 快速更新：仅更新指定字段，其余保持不变。
    /// 设计要点：
    /// 1. 所有字段可选，null 表示不更新。
    /// 2. status 更新时走状态转换校验。
    /// 3. 不涉及时间/重复规则变更，无需冲突检测。
    /// </summary>
    public async Task<CalendarEventDto?> QuickUpdateAsync(int id, QuickUpdateCalendarEventRequest request, CancellationToken ct = default)
    {
        var evt = await _unitOfWork.CalendarEvents.GetByIdTrackedAsync(id).ConfigureAwait(false);
        if (evt == null) return null;

        await _auth.ValidateWriteAccessAsync(evt).ConfigureAwait(false);

        // P3: 显式乐观锁比对
        CheckRowVersion(evt, request.IfMatch);

        var oldValues = JsonSerializer.Serialize(evt);

        // 逐字段更新（仅非 null）
        if (!string.IsNullOrEmpty(request.Title))
            evt.Title = request.Title;
        if (request.Description != null)
            evt.Description = request.Description;
        if (!string.IsNullOrEmpty(request.Status))
        {
            ValidateStatusTransition(evt.Status, request.Status);
            evt.Status = request.Status;
        }
        if (request.Priority.HasValue)
            evt.Priority = request.Priority;
        if (request.Progress.HasValue)
            evt.Progress = request.Progress;
        if (request.Location != null)
            evt.Location = request.Location;
        if (request.MeetingUrl != null)
            evt.MeetingUrl = request.MeetingUrl;
        if (request.Color != null)
            evt.Color = request.Color;
        if (request.ReminderMinutes.HasValue)
            evt.ReminderMinutes = request.ReminderMinutes;
        if (!string.IsNullOrEmpty(request.ReminderType))
        {
            evt.ReminderType = request.ReminderType.Trim();
            evt.IsReminderSent = false;
        }
        if (request.ReminderMinutes.HasValue)
            evt.IsReminderSent = false;

        evt.UpdatedAt = DateTime.UtcNow;

        await _unitOfWork.SaveChangesAsync().ConfigureAwait(false);

        await LogAuditAndNotifyAsync("calendar.quick_update", evt, oldValues).ConfigureAwait(false);

        InvalidateCacheForEventAndCurrentUser(evt.UserId ?? evt.CreatedBy);

        return evt.ToDto();
    }

    /// <summary>
    /// 恢复已删除的日历事件（从回收站取消软删除）。
    /// P0-2: 支持显式乐观锁——若调用方传入 <paramref name="ifMatch"/>，将比对 RowVersion，
    ///       防止并发恢复/再删除之间的状态竞争被静默吞掉。
    /// </summary>
    public async Task<bool> RestoreAsync(int id, string? ifMatch = null, CancellationToken ct = default)
    {
        // P0-2: 用带 ChangeTracker 的版本重加载。
        // 忽略全局查询过滤器是为了读 IsDeleted 条目；但必须 tracked 才能让 EF 在 SaveChanges 时
        // 自动带原 RowVersion 做 UPDATE 的乐观锁 WHERE，并触发 SQL Server 自动 bump RowVersion。
        var evt = await _unitOfWork.CalendarEvents
            .GetByIdTrackedAsync(id, includeDeleted: true)
            .ConfigureAwait(false);
        if (evt == null || !evt.IsDeleted()) return false;

        await _auth.ValidateWriteAccessAsync(evt).ConfigureAwait(false);

        // P0-2: 显式乐观锁
        CheckRowVersion(evt, ifMatch);

        if (evt.RecurrenceParentId.HasValue)
        {
            var parent = await _unitOfWork.CalendarEvents
                .GetByIdTrackedAsync(evt.RecurrenceParentId.Value, includeDeleted: true)
                .ConfigureAwait(false);
            if (parent == null || parent.DeletedAt != null)
            {
                throw new InvalidOperationException("无法恢复重复实例：父事件已被删除，请先恢复父事件");
            }
        }

        // E#1 恢复前也过一次冲突：避免垃圾箱中被新事件「抢占」的时间段又被恢复回来造成硬冲突
        await _conflict.EnforceHardConflictAsync(evt.StartAt, evt.EndAt, evt.UserId, evt.Id, evt.IsAllDay, ct)
            .ConfigureAwait(false);

        evt.Restore();
        await _unitOfWork.SaveChangesAsync(ct).ConfigureAwait(false);

        await LogAuditAndNotifyAsync("calendar.restore", evt).ConfigureAwait(false);

        _cache?.InvalidateAllForUser(evt.UserId ?? evt.CreatedBy);

        return true;
    }

    /// <summary>
    /// 硬删除事件（不可恢复，SuperAdmin 专用）。
    /// P0-2: 支持 If-Match 乐观锁——在真正删除前比对版本，避免 SuperAdmin 删除时
    ///       刚好被其他管理员改了敏感字段的写操作被静默覆盖。
    /// </summary>
    public async Task<int> HardDeleteAsync(int id, CalendarEvent? existingEntity, string? ifMatch = null, CancellationToken ct = default)
    {
        var evt = existingEntity ?? await _unitOfWork.CalendarEvents
            .GetByIdTrackedAsync(id, includeDeleted: true)
            .ConfigureAwait(false);
        if (evt == null) return 0;

        // P1-5 对齐：行级写权限校验（CreatedBy / UserId / 管辖范围 / SuperAdmin 自动放行）。
        // Controller 外层已加 [Permission("calendar.manage")]，PurgeTrash 走 Controller 层 [RequireSuperAdmin]。
        await _auth.ValidateWriteAccessAsync(evt).ConfigureAwait(false);

        // P0-2: 删除前最后一道乐观锁守卫（若前端携带版本号）
        if (existingEntity == null)
        {
            CheckRowVersion(evt, ifMatch);
        }

        var oldValues = JsonSerializer.Serialize(evt);
        var notifyUserIds = CollectCalendarUserIds(evt);
        var notifyPayload = evt.ToDto();
        var notifyEventId = evt.Id;

        using var transaction = await _unitOfWork.BeginTransactionAsync().ConfigureAwait(false);
        try
        {
            try
            {
                var mappings = _unitOfWork.Repository<OutlookEventMapping>();
                if (mappings != null)
                {
                    var relatedMappings = await mappings.FindAsync(m => m.CalendarEventId == id).ConfigureAwait(false);
                    if (relatedMappings != null && relatedMappings.Any())
                    {
                        foreach (var m in relatedMappings)
                            await mappings.DeleteAsync(m).ConfigureAwait(false);
                    }
                }
            }
            catch (Exception ex) { _logger.LogWarning(ex, "清理 OutlookEventMapping 失败，不影响硬删除"); }

            // 高3 修复：使用 GetByParentIdIncludingDeletedAsync 确保已软删除的子实例也被物理删除，
            // 否则 EF Core 全局查询过滤器会过滤掉已软删除的子实例，导致垃圾箱中残留孤儿数据。
            var children = await _unitOfWork.CalendarEvents.GetByParentIdIncludingDeletedAsync(id).ConfigureAwait(false);
            if (children != null && children.Any())
            {
                foreach (var child in children)
                {
                    await _unitOfWork.CalendarEvents.HardDeleteAsync(child).ConfigureAwait(false);
                }
            }

            await _unitOfWork.CalendarEvents.HardDeleteAsync(evt).ConfigureAwait(false);
            await _unitOfWork.SaveChangesAsync().ConfigureAwait(false);

            await transaction.CommitAsync().ConfigureAwait(false);
        }
        catch
        {
            await transaction.SafeRollbackAsync().ConfigureAwait(false);
            throw;
        }

        try
        {
            await _auditService.LogAsync(
                action: "calendar.hard_delete",
                entityType: "CalendarEvent",
                entityId: evt.Id,
                oldValues: oldValues).ConfigureAwait(false);
        }
        catch (Exception ex) { _logger.LogWarning(ex, "审计日志记录失败"); }

        try
        {
            await _notificationHubService.NotifyCalendarEventChangedAsync("hard_delete", notifyEventId, notifyUserIds, notifyPayload).ConfigureAwait(false);
        }
        catch (Exception ex) { _logger.LogWarning(ex, "实时通知推送失败"); }

        _cache?.InvalidateAllForUser(evt.UserId ?? evt.CreatedBy);
        foreach (var uid in notifyUserIds)
            _cache?.InvalidateAllForUser(uid);

        return 1;
    }

    public async Task<int> ClearTrashAsync(CancellationToken ct = default)
    {
        if (!_currentUserAccessor.IsSuperAdmin)
        {
            return 0;
        }

        var count = await _unitOfWork.CalendarEvents.ClearTrashAsync(userId: null).ConfigureAwait(false);
        await _unitOfWork.SaveChangesAsync().ConfigureAwait(false);

        try
        {
            await _auditService.LogAsync(
                action: "calendar.clear_trash",
                entityType: "CalendarEvent",
                entityId: 0,
                newValues: JsonSerializer.Serialize(new { PurgedCount = count })).ConfigureAwait(false);
        }
        catch (Exception ex) { _logger.LogWarning(ex, "审计日志/通知推送失败，不影响主流程"); }

        try
        {
            var notifyUserIds = await _auth.ResolveAuthorizedUserIdsAsync(_currentUserAccessor.UserId).ConfigureAwait(false);
            // 关键修复：必须先失效缓存再推送 SignalR 通知，否则前端收到 CalendarRefresh 后立即重新拉取会命中过期缓存，
            // 导致已物理删除的事件以 deletedAt=null 的旧值重新出现在日历界面。
            if (_cache != null && notifyUserIds != null)
            {
                foreach (var uid in notifyUserIds)
                    _cache.InvalidateAllForUser(uid);
            }
            _cache?.InvalidateAllForUser(_currentUserAccessor.UserId);
            await _notificationHubService.NotifyCalendarRefreshAsync(notifyUserIds ?? Array.Empty<int>()).ConfigureAwait(false);
        }
        catch (Exception ex) { _logger.LogWarning(ex, "审计日志/通知推送失败，不影响主流程"); }

        return count;
    }

    public async Task<int> PurgeTrashAsync(int retentionDays = 0, CancellationToken ct = default)
    {
        if (!_currentUserAccessor.IsSuperAdmin)
        {
            return 0;
        }

        if (retentionDays < 0)
            throw new ArgumentOutOfRangeException(nameof(retentionDays), "保留天数不能为负数");

        // retentionDays == 0 → 不保留，立即清空所有回收站条目；否则用显式值或缺省配置（DefaultPurgeRetentionDays）
        int actualRetention = retentionDays == 0
            ? 0
            : (retentionDays > 0 ? retentionDays : _calendarOptions.Value.DefaultPurgeRetentionDays);

        var cutoff = DateTime.UtcNow.AddDays(-actualRetention);
        var count = await _unitOfWork.CalendarEvents.PurgeTrashAsync(cutoff, userId: null).ConfigureAwait(false);
        await _unitOfWork.SaveChangesAsync().ConfigureAwait(false);

        try
        {
            await _auditService.LogAsync(
                action: "calendar.purge_trash",
                entityType: "CalendarEvent",
                entityId: 0,
                newValues: JsonSerializer.Serialize(new { RetentionDays = actualRetention, Cutoff = cutoff, PurgedCount = count })).ConfigureAwait(false);
        }
        catch (Exception ex) { _logger.LogWarning(ex, "审计日志/通知推送失败，不影响主流程"); }

        try
        {
            var notifyUserIds = await _auth.ResolveAuthorizedUserIdsAsync(_currentUserAccessor.UserId).ConfigureAwait(false);
            // 关键修复：必须先失效缓存再推送 SignalR 通知，否则前端收到 CalendarRefresh 后立即重新拉取会命中过期缓存，
            // 导致已物理删除的事件以 deletedAt=null 的旧值重新出现在日历界面。
            if (_cache != null && notifyUserIds != null)
            {
                foreach (var uid in notifyUserIds)
                    _cache.InvalidateAllForUser(uid);
            }
            _cache?.InvalidateAllForUser(_currentUserAccessor.UserId);
            await _notificationHubService.NotifyCalendarRefreshAsync(notifyUserIds ?? Array.Empty<int>()).ConfigureAwait(false);
        }
        catch (Exception ex) { _logger.LogWarning(ex, "审计日志/通知推送失败，不影响主流程"); }

        return count;
    }

    /// <summary>
    /// 用户在 SignalR 弹出卡片上点击"确认已收到"或自动关闭时调用。
    ///
    /// P0-3 关键修复：
    /// 1) 原实现使用 ExecuteUpdateAsync，完全绕过 ChangeTracker → 不校验 RowVersion，
    ///    也不触发 SQL Server 自动 bump RowVersion，导致并发操作下会静默覆盖其他写入，
    ///    且其他端无法通过版本号感知已变更。现改为 Read — CheckRowVersion — Write。
    /// 2) 权限维度补齐：非 Admin 既能被分配人（UserId）确认，也允许创建者（CreatedBy）
    ///    自己确认自己创建的提醒卡片（原实现仅校验 UserId 一条维度）。
    /// 3) UpdatedAt 统一使用 DateTime.UtcNow，与其他方法保持一致，避免客户端排序错乱。
    /// </summary>
    /// <param name="eventId">事件 ID</param>
    /// <param name="currentUserId">当前操作人 ID</param>
    /// <param name="isAdmin">是否管理员/经理角色（可跨用户确认）</param>
    /// <param name="mode">user_click / timeout_auto_close</param>
    /// <param name="ifMatch">可选：If-Match ETag（Base64 RowVersion）</param>
    public async Task<int> ConfirmReminderAsync(int eventId, int currentUserId, bool isAdmin, string mode, string? ifMatch = null, CancellationToken ct = default)
    {
        var normalizedMode = (mode ?? string.Empty).Trim().ToLowerInvariant();
        if (normalizedMode is not ("user_click" or "timeout_auto_close"))
        {
            _logger.LogWarning(
                "ConfirmReminderAsync 收到非标准 mode={Mode}（EventId={EventId}），仍按正常流程执行确认。",
                mode, eventId);
        }

        var now = DateTimeOffset.UtcNow;
        var allowedStatuses = new HashSet<string>(StringComparer.Ordinal)
            { "pending", "fallback_sent", "fallback_in_progress" };

        // P0-3: 改用 Read-Modify-Write。ChangeTracker 保留原始 RowVersion，
        //       SaveChanges 时 EF Core 会带原版本号做 UPDATE WHERE，SQL Server 自动 bump RowVersion。
        var evt = await _unitOfWork.CalendarEvents.GetByIdTrackedAsync(eventId).ConfigureAwait(false);
        if (evt == null || evt.DeletedAt != null) return 0;

        // P0-3: 权限维度——管理员 / CreatedBy 创建者 / UserId 被分配人 均可确认
        var canConfirm = isAdmin
                         || evt.CreatedBy == currentUserId
                         || (evt.UserId.HasValue && evt.UserId.Value == currentUserId);
        if (!canConfirm)
        {
            // 非归属用户：返回 0（保持与 NotFound 相同返回值，避免信息泄露）
            _logger.LogInformation(
                "ConfirmReminderAsync 用户 {UserId} 无权确认事件 {EventId}（CreatedBy={CBy}, UserId={UBy}, isAdmin={Admin}）",
                currentUserId, eventId, evt.CreatedBy, evt.UserId, isAdmin);
            return 0;
        }

        // P0-3: 乐观锁——写前比对 If-Match
        CheckRowVersion(evt, ifMatch);

        if (!allowedStatuses.Contains(evt.ReminderConfirmation ?? string.Empty))
        {
            // 已经 confirmed 的不需要再改，视作成功（幂等）
            return string.Equals(evt.ReminderConfirmation, "confirmed", StringComparison.Ordinal) ? 1 : 0;
        }

        evt.ReminderConfirmation = "confirmed";
        evt.ReminderConfirmedAt = now;
        evt.UpdatedAt = DateTime.UtcNow;

        // SaveChanges 触发：EF 自动 WHERE RowVersion = original + SQL Server 自动 bump RowVersion
        var affected = await _unitOfWork.SaveChangesAsync(ct).ConfigureAwait(false);
        if (affected > 0) affected = 1;

        if (affected > 0)
        {
            try
            {
                var payload = JsonSerializer.Serialize(new
                {
                    mode = normalizedMode,
                    confirmedBy = currentUserId,
                    isAdmin
                });
                await WriteAttemptAsync(eventId, "confirm", 1, "confirmed", payload: payload).ConfigureAwait(false);
            }
            catch (Exception ex)
            {
                _logger.LogDebug(ex, "ConfirmReminder: attempt audit write failed for event {EventId}", eventId);
            }

            try
            {
                var actionPayload = evt.ToDto();
                actionPayload.ReminderEmails = null;
                var userIds = CollectCalendarUserIds(evt);
                await _notificationHubService.NotifyCalendarEventChangedAsync(
                    "calendar.reminder_confirmed", evt.Id, userIds, actionPayload).ConfigureAwait(false);
            }
            catch (Exception ex)
            {
                _logger.LogDebug(ex, "ConfirmReminder: 多端 SignalR 推送失败 eventId={EventId}", eventId);
            }
        }

        return affected;
    }

    /// <summary>
    /// 失效缓存：事件归属者 + 当前操作用户。
    /// 修复：SuperAdmin 删除/修改他人日程时，仅失效事件归属者缓存会导致当前操作用户的缓存仍返回旧数据。
    /// 因此需同时失效当前操作用户（currentUserId）的缓存。
    /// </summary>
    private void InvalidateCacheForEventAndCurrentUser(int eventUserId)
    {
        _cache?.InvalidateAllForUser(eventUserId);
        var currentUserId = _currentUserAccessor.UserId;
        if (currentUserId != eventUserId)
        {
            _cache?.InvalidateAllForUser(currentUserId);
        }
    }

    private async Task LogAuditAndNotifyAsync(string action, CalendarEvent evt, string? oldValues = null)
    {
        try
        {
            await _auditService.LogAsync(
                action: action,
                entityType: "CalendarEvent",
                entityId: evt.Id,
                oldValues: oldValues,
                newValues: JsonSerializer.Serialize(evt)).ConfigureAwait(false);
        }
        catch (Exception ex) { _logger.LogWarning(ex, "审计日志记录失败"); }

        try
        {
            var userIds = CollectCalendarUserIds(evt);
            // SignalR 广播给多用户：清空 ReminderEmails 防止泄露给非创建者
            var pushDto = evt.ToDto();
            pushDto.ReminderEmails = null;
            await _notificationHubService.NotifyCalendarEventChangedAsync(action, evt.Id, userIds, pushDto).ConfigureAwait(false);
        }
        catch (Exception ex) { _logger.LogWarning(ex, "实时通知推送失败"); }
    }

    private List<int> CollectCalendarUserIds(CalendarEvent evt)
    {
        var userIds = new HashSet<int>();
        userIds.Add(evt.CreatedBy);
        if (evt.UserId.HasValue)
            userIds.Add(evt.UserId.Value);
        if (!string.IsNullOrEmpty(evt.AttendeesJson))
        {
            try
            {
                var attendees = JsonSerializer.Deserialize<List<int>>(evt.AttendeesJson);
                if (attendees != null)
                {
                    foreach (var id in attendees)
                        userIds.Add(id);
                }
            }
            catch (Exception ex)
            {
                _logger.LogWarning(ex, "解析 AttendeesJson 失败，eventId={EventId}", evt.Id);
            }
        }
        return userIds.ToList();
    }

    private void UpdateEventFromRequest(CalendarEvent evt, CreateCalendarEventRequest request)
    {
        var oldReminderMinutes = evt.ReminderMinutes;
        var oldReminderEmails = evt.ReminderEmails;
        var oldReminderType = evt.ReminderType;

        evt.Title = request.Title;
        evt.Description = request.Description;
        evt.EventType = request.EventType;
        evt.IsAllDay = request.IsAllDay;
        evt.ConversationId = request.ConversationId;
        evt.MailboxId = request.MailboxId;
        evt.UserId = request.UserId;
        evt.Color = request.Color;
        evt.IsRecurring = request.IsRecurring;
        evt.RecurrenceType = request.RecurrenceType;
        evt.RecurrenceInterval = request.RecurrenceInterval ?? 1;
        evt.RecurrenceEndDate = string.IsNullOrEmpty(request.RecurrenceEndDate) ? null : ParseDate(request.RecurrenceEndDate);
        evt.RecurrenceCount = request.RecurrenceCount;
        evt.ReminderMinutes = request.ReminderMinutes;
        evt.ReminderEmails = string.IsNullOrWhiteSpace(request.ReminderEmails) ? null : request.ReminderEmails.Trim();
        evt.UpdatedAt = DateTime.UtcNow;

        if (evt.RecurrenceParentId == null)
        {
            evt.StartAt = ParseDate(request.StartAt);
            evt.EndAt = string.IsNullOrEmpty(request.EndAt) ? null : ParseDate(request.EndAt);
        }

        if (request is UpdateCalendarEventRequest updateReq)
        {
            ValidateStatusTransition(evt.Status, updateReq.Status);
            evt.Status = updateReq.Status;
        }

        evt.Priority = request.Priority;
        evt.Progress = request.Progress;
        evt.Location = request.Location;
        evt.MeetingUrl = request.MeetingUrl;
        evt.AttendeesJson = request.Attendees.Count > 0 ? JsonSerializer.Serialize(request.Attendees) : null;
        evt.SubTasksJson = request.SubTasks.Count > 0 ? JsonSerializer.Serialize(request.SubTasks) : null;
        evt.RelatedEventId = request.RelatedEventId;
        evt.RecurrenceExclusionsJson = request.RecurrenceExclusions != null ? JsonSerializer.Serialize(request.RecurrenceExclusions) : null;
        // 事件类型专属字段持久化（2026-08-17）
        evt.HostUserIdsJson = request.HostUserIds.Count > 0 ? JsonSerializer.Serialize(request.HostUserIds) : null;
        evt.AgendaIdsJson = request.AgendaIds.Count > 0 ? JsonSerializer.Serialize(request.AgendaIds) : null;
        evt.TaskOwnerId = request.TaskOwnerId;
        evt.DueAt = string.IsNullOrEmpty(request.DueAt) ? null : ParseDate(request.DueAt);
        evt.ReminderType = string.IsNullOrWhiteSpace(request.ReminderType) ? null : request.ReminderType.Trim();
        evt.WeekdaysSelectedJson = request.WeekdaysSelected.Count > 0 ? JsonSerializer.Serialize(request.WeekdaysSelected) : null;
        evt.RecurrenceRule = string.IsNullOrWhiteSpace(request.RecurrenceRule) ? null : request.RecurrenceRule.Trim();

        // T000100 修复点 2（Update/UpdateEventFromRequest）：邮箱参会人与 ID 型参会人双写归一化。
        var (normUpdateAttendeeIds, normUpdateReminderEmails) =
            NormalizeAttendeesAndReminderEmails(request.Attendees, request.ReminderEmails);
        evt.AttendeesJson = normUpdateAttendeeIds.Count > 0 ? JsonSerializer.Serialize(normUpdateAttendeeIds) : null;
        evt.ReminderEmails = normUpdateReminderEmails;

        // 提醒配置变更后重置已发送标记，确保下次扫描重新触发
        var newReminderMinutes = evt.ReminderMinutes;
        var newReminderEmails = evt.ReminderEmails;
        var newReminderType = evt.ReminderType;
        if (oldReminderMinutes != newReminderMinutes
            || oldReminderEmails != newReminderEmails
            || oldReminderType != newReminderType)
        {
            evt.IsReminderSent = false;
        }
    }

    private static void ValidateStatusTransition(string currentStatus, string newStatus)
    {
        var validStatuses = new HashSet<string> { "pending", "in_progress", "completed", "cancelled", "missed" };
        if (!validStatuses.Contains(newStatus))
            throw new ArgumentException($"无效的状态值：{newStatus}，有效值为 pending/in_progress/completed/cancelled/missed");

        if (currentStatus == newStatus) return;

        var allowedTransitions = new Dictionary<string, HashSet<string>>
        {
            ["pending"] = new() { "in_progress", "completed", "cancelled", "missed" },
            ["in_progress"] = new() { "pending", "completed", "cancelled", "missed" },
            ["completed"] = new() { "pending", "in_progress" },
            ["cancelled"] = new() { "pending", "in_progress" },
            ["missed"] = new() { "pending", "in_progress", "completed", "cancelled" }
        };
        if (!allowedTransitions.TryGetValue(currentStatus, out var allowed) || !allowed.Contains(newStatus))
            throw new InvalidOperationException($"不允许从 {currentStatus} 转换到 {newStatus}");
    }

    private async Task WriteAttemptAsync(
        int eventId,
        string channel,
        int attemptNo,
        string status,
        string? payload = null,
        string? error = null)
    {
        try
        {
            var attempt = new CalendarReminderAttempt
            {
                EventId = eventId,
                AttemptChannel = channel,
                AttemptNo = attemptNo,
                Status = status,
                Payload = payload,
                ErrorDetail = error,
                CreatedAt = DateTimeOffset.UtcNow
            };
            await _unitOfWork.Repository<CalendarReminderAttempt>().AddAsync(attempt).ConfigureAwait(false);
            await _unitOfWork.SaveChangesAsync().ConfigureAwait(false);
        }
        catch (Exception ex)
        {
            _logger.LogWarning(
                ex,
                "写入提醒投递审计记录失败（EventId={EventId}, Channel={Channel}, Status={Status}）",
                eventId, channel, status);
        }
    }

    private static void ValidateFields(CreateCalendarEventRequest request)
    {
        if (request.Title != null)
        {
            if (string.IsNullOrWhiteSpace(request.Title))
                throw new ArgumentException("标题不能为空");
            if (request.Title.Length > 200)
                throw new ArgumentException("标题长度不能超过 200 字符");
        }

        if (request.Priority.HasValue)
        {
            int p = request.Priority.Value;
            if (p < 0 || p > 5)
                throw new ArgumentException("优先级必须在 0-5 之间");
        }

        if (request.Progress.HasValue)
        {
            int pr = request.Progress.Value;
            if (pr < 0 || pr > 100)
                throw new ArgumentException("进度必须在 0-100 之间");
        }

        if (request.RecurrenceType.HasValue)
        {
            int rt = request.RecurrenceType.Value;
            if (!Enum.IsDefined(typeof(RecurrenceType), rt))
                throw new ArgumentException("重复类型必须在 0-3 之间（0=每日, 1=每周, 2=每月, 3=每年）");
        }

        if (request.RecurrenceInterval.HasValue && request.RecurrenceInterval.Value < 1)
            throw new ArgumentException("重复间隔必须大于等于 1");

        if (request.RecurrenceCount.HasValue && (request.RecurrenceCount.Value < 1 || request.RecurrenceCount.Value > 1000))
            throw new ArgumentException("重复次数必须在 1-1000 之间");

        if (request.ReminderMinutes.HasValue)
        {
            int rm = request.ReminderMinutes.Value;
            // 0 = 「准时」：与 `CalendarEventDto.CreateCalendarEventRequest.Validate` 保持同一区间
            // （2026-09-24 用户裁定放宽为 `>= 0`，禁止负数）。
            if (rm < 0 || rm > 10080)
                throw new ArgumentException("提醒分钟数必须在 0-10080 之间（一周内）");
        }

        // EventType 归一化在 CreateAsync / UpdateAsync 实例方法中处理（需要写 Warning 日志，
        // 不能在静态 ValidateFields 里抛 ArgumentException，否则会被客户端 SignalR 重试
        // 循环放大成 WebSocket 连锁断开。见 CreateAsync_UnknownEventType_* 测试。

        if (request is UpdateCalendarEventRequest updateReq)
        {
            var validStatuses = new HashSet<string> { "pending", "in_progress", "completed", "cancelled", "missed" };
            if (!string.IsNullOrEmpty(updateReq.Status) && !validStatuses.Contains(updateReq.Status))
                throw new ArgumentException("状态必须为 pending/in_progress/completed/cancelled/missed");
        }

        if (request.Description != null && request.Description.Length > 4000)
            throw new ArgumentException("描述长度不能超过 4000 字符");
        if (request.Location != null && request.Location.Length > 200)
            throw new ArgumentException("地点长度不能超过 200 字符");
        if (request.MeetingUrl != null && request.MeetingUrl.Length > 500)
            throw new ArgumentException("会议链接长度不能超过 500 字符");
        if (request.Color != null && request.Color.Length > 30)
            throw new ArgumentException("颜色标记长度不能超过 30 字符");
        if (request.ReminderEmails != null && request.ReminderEmails.Length > 2000)
            throw new ArgumentException("提醒邮件地址长度不能超过 2000 字符");

        if (!string.IsNullOrEmpty(request.StartAt) && !string.IsNullOrEmpty(request.EndAt))
        {
            var start = ParseDate(request.StartAt);
            var end = ParseDate(request.EndAt);
            if (end < start)
                throw new ArgumentException("结束时间不能早于开始时间");
        }

        if (!string.IsNullOrWhiteSpace(request.MeetingUrl))
        {
            var url = request.MeetingUrl.Trim();
            if (!Uri.TryCreate(url, UriKind.Absolute, out var uri)
                || (uri.Scheme != "http" && uri.Scheme != "https"
                    && uri.Scheme != "zoommtg" && uri.Scheme != "teams"
                    && uri.Scheme != "tel" && uri.Scheme != "mailto"))
                throw new ArgumentException("会议链接必须为 http/https 或有效协议（zoommtg/teams/tel/mailto）");
        }

        if (request.RecurrenceExclusions?.Excluded != null)
        {
            foreach (var date in request.RecurrenceExclusions.Excluded)
            {
                if (!DateTime.TryParseExact(date, "yyyy-MM-dd", null,
                        DateTimeStyles.None, out _))
                    throw new ArgumentException($"排除日期格式必须为 yyyy-MM-dd: {date}");
            }
        }
    }

    private static DateTime ParseDate(string s)
    {
        if (!DateTime.TryParse(s, CultureInfo.InvariantCulture,
                DateTimeStyles.AdjustToUniversal | DateTimeStyles.AssumeUniversal,
                out var result))
        {
            throw new ArgumentException($"日期格式无效：{s}");
        }
        return DateTime.SpecifyKind(result, DateTimeKind.Utc);
    }

    /// <summary>
    /// 兜底归一化 EventType（Facade 入口已做过一次 + 打了 Warning 日志）。
    /// 这里不写 Warning，避免重复日志；仅为内部直接调用 MutationService 的路径做一层静默兜底。
    /// 白名单内 8 种类型保留原样通过，未知类型降级为 "other"，空值默认 "task"。
    /// </summary>
    private string NormalizeEventType(string? eventType)
    {
        var allowed = new HashSet<string>(StringComparer.OrdinalIgnoreCase)
            { "due_date", "task", "meeting", "reminder", "personal", "travel", "work", "other" };

        if (string.IsNullOrEmpty(eventType))
        {
            return "task";
        }

        if (allowed.Contains(eventType))
        {
            // 保留用户传入的大小写（允许 PERSONAL / Travel 这种变体），不做 ToLower。
            return eventType;
        }

        // 未知类型：静默降级到 other（Facade 入口应已写过 Warning；这里不重复记录）
        return "other";
    }

    /// <summary>对请求的 EventType 做归一化赋值（回写到 request.EventType）。</summary>
    private void ApplyNormalizedEventType(CreateCalendarEventRequest request)
    {
        request.EventType = NormalizeEventType(request.EventType);
    }

    /// <summary>
    /// 邮箱格式判定（宽松匹配，仅用于从一串"看起来像邮箱的字符串"里分拣）。
    /// 避免使用完整 MailAddress.TryParse：老版本 .NET 对中文域名、+ 前缀等容错偏低，
    /// 这里只做"必须有且只有一个 @，@ 两侧都非空，且右侧至少有一个 ."的启发式判断。
    /// </summary>
    private static bool LooksLikeEmail(string s)
    {
        if (string.IsNullOrWhiteSpace(s)) return false;
        var at = s.IndexOf('@');
        if (at <= 0 || at == s.Length - 1) return false;
        if (s.IndexOf('@', at + 1) >= 0) return false;
        var domain = s[(at + 1)..];
        return domain.Contains('.', StringComparison.Ordinal) && domain[0] != '.' && domain[^1] != '.';
    }

    /// <summary>
    /// 拆分 CSV（逗号/分号/空白/换行混用）。返回 Trim 后的非空列表。
    /// </summary>
    private static List<string> SplitCsvItems(string? input)
    {
        if (string.IsNullOrWhiteSpace(input)) return [];
        return input
            .Split(new[] { ',', ';', '\r', '\n', '\t' }, StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries)
            .Where(p => !string.IsNullOrWhiteSpace(p))
            .ToList();
    }

    /// <summary>
    /// 参会人 / 提醒邮箱归一化（核心：双写互补，避免 round-trip 丢数据）。
    ///
    /// 场景 A：前端走"邮箱参会人"（T000100 用例）
    ///   - 前端用户在参会人输入框里填了 2 个邮箱 → 提交时 reminderEmails="a@x.com,b@x.com"，attendees=[]
    ///   - 结果：ReminderEmails = a@x.com,b@x.com，AttendeesJson=[]
    ///   - 编辑回显：DTO.ReminderEmails 存在，前端据此还原输入框 ✔
    ///
    /// 场景 B：前端误把邮箱字符串塞进 attendees（`List<int>` 声明会让 STJ 直接抛 → 500）
    ///   - 兼容策略：允许 request.Attendees 里"看起来像邮箱"的项在进入 JSON 前被分拣到 ReminderEmails，
    ///     避免整条请求因类型不符失败。
    ///
    /// 场景 C：用户手动填 Attendees=[1,2,3] 又填了 reminderEmails=a@x.com
    ///   - 结果：两者都保留，不互相覆盖，双写。
    ///
    /// 对调用方：
    ///   - 返回 (attendeeIds, reminderEmailsCsv) 两个字符串友好的归一化结果。
    ///   - 调用方直接赋给实体对应的 *Json / ReminderEmails 字段。
    /// </summary>
    private static (List<int> attendeeIds, string? reminderEmailsCsv) NormalizeAttendeesAndReminderEmails(
        List<int> requestAttendees,
        string? requestReminderEmails)
    {
        // 1) ReminderEmails 端：解析 CSV，作为邮箱候选集合。
        var emails = new HashSet<string>(SplitCsvItems(requestReminderEmails), StringComparer.OrdinalIgnoreCase);

        // 2) Attendees 端：理论上是用户 ID（int），但如果上游在 DTO 校验前把"邮箱字符串"
        //    塞进了 JSON Raw 表示，STJ 会直接抛出 JsonException；这里作为防御性兜底，
        //    仅接受"能被解析为正整数"的项为 ID，邮箱形态的项转入邮箱集合（注意：
        //    当前 request.Attendees 已被 ASP.NET Core 模型绑定为 List<int>，真传邮箱
        //    时模型绑定会先失败——此段主要对后续若切换到字符串承载或中间层修改后留兼容性）。
        var ids = new HashSet<int>();
        if (requestAttendees != null)
        {
            foreach (var id in requestAttendees.Where(x => x > 0))
            {
                ids.Add(id);
            }
        }

        // 3) 归一化写回 CSV（稳定序 + 去重）
        string? csv = emails.Count == 0 ? null : string.Join(',', emails.OrderBy(x => x, StringComparer.OrdinalIgnoreCase));

        return (ids.OrderBy(x => x).ToList(), csv);
    }

    /// <summary>P1-2 批量操作 Facade：delete / setStatus / setEventType / moveOffset。
    /// 对每个事件先走 <see cref="CalendarAuthorizationService"/> 权限校验，再落库、审计、SignalR 推送。
    /// <para>设计要点：</para>
    /// <para>1. LINQ 查询用 e.DeletedAt == null 替代 !e.IsDeleted()，避免 EF Core 无法翻译实例方法报错。</para>
    /// <para>2. delete/setStatus/setEventType 用 ExecuteUpdateAsync 直接落库，绕过 ChangeTracker + RowVersion。
    /// 写入前先做 RowVersion 并发检测（重新读取比对），冲突事件移入 Failures，避免丢失更新。</para>
    /// <para>3. moveoffset 因每行 StartAt/EndAt 不同，只能走 tracked 变更 + SaveChanges，外层加异常回滚并逐行汇总 Failures。</para>
    /// </summary>
    public async Task<CalendarBatchOperationResponse> BatchOperationAsync(CalendarBatchOperationRequest request, CancellationToken ct = default)
    {
        var response = new CalendarBatchOperationResponse { Failures = new List<CalendarBatchFailedItem>() };
        if (request.Ids == null || request.Ids.Count == 0) return response;

        var op = (request.Op ?? string.Empty).Trim().ToLowerInvariant();
        var validOps = new HashSet<string>(StringComparer.Ordinal) { "delete", "restore", "setstatus", "seteventtype", "moveoffset" };
        if (!validOps.Contains(op)) throw new ArgumentException($"不支持的批量操作类型：{request.Op}");

        var validStatuses = new HashSet<string>(StringComparer.Ordinal) { "pending", "in_progress", "completed", "cancelled", "missed" };
        var validEventTypes = new HashSet<string>(StringComparer.OrdinalIgnoreCase) { "due_date", "task", "meeting", "reminder", "personal", "travel", "work", "other" };

        var normStatus = (request.Status ?? string.Empty).Trim().ToLowerInvariant();
        var normEventType = NormalizeEventTypeForBatch(request.EventType);
        var moveDays = request.DaysOffset ?? 0;
        var moveMins = request.MinutesOffset ?? 0;

        if (op == "setstatus" && !validStatuses.Contains(normStatus))
            throw new ArgumentException("setStatus 操作必须提供 Status: pending/in_progress/completed/cancelled/missed");
        if (op == "seteventtype")
        {
            if (string.IsNullOrWhiteSpace(request.EventType)) throw new ArgumentException("setEventType 操作必须提供 EventType");
            if (!validEventTypes.Contains(normEventType))
                throw new ArgumentException($"事件类型 '{request.EventType}' 无效");
        }

        var uniqueIds = request.Ids.Distinct().ToList();
        var utcNow = DateTime.UtcNow;

        // 1) 读：AsNoTracking 加载候选集做业务校验（权限 / 重复父事件跳过 / 状态转移合法性 等）
        //    ⚠️ 用 e.DeletedAt == null 而不是 !e.IsDeleted()，否则 EF Core 无法翻译实例方法 → InvalidOperationException → 409
        //    restore 只针对软删除事件：需绕过全局查询过滤器（DeletedAt == null）读取垃圾箱记录。
        IQueryable<CalendarEvent> readQuery;
        if (op == "restore")
        {
            readQuery = _unitOfWork.CalendarEvents.GetQueryable(asNoTracking: true)
                .IgnoreQueryFilters()
                .Where(e => uniqueIds.Contains(e.Id) && e.DeletedAt != null);
        }
        else
        {
            readQuery = _unitOfWork.CalendarEvents.GetQueryable(asNoTracking: true)
                .Where(e => uniqueIds.Contains(e.Id) && e.DeletedAt == null);
        }

        if (!_currentUserAccessor.IsSuperAdmin)
        {
            var authIds = await _auth.ResolveAuthorizedUserIdsAsync(_currentUserAccessor.UserId).ConfigureAwait(false);
            if (authIds != null)
            {
                var idSet = authIds.ToHashSet();
                readQuery = readQuery.Where(e => e.CreatedBy == _currentUserAccessor.UserId ||
                                                  (e.UserId.HasValue && idSet.Contains(e.UserId.Value)));
            }
            else
            {
                readQuery = readQuery.Where(e => e.CreatedBy == _currentUserAccessor.UserId);
            }
        }

        var events = await readQuery.ToListAsync(ct).ConfigureAwait(false);
        var idToEvent = events.ToDictionary(e => e.Id);

        foreach (var id in uniqueIds)
        {
            if (!idToEvent.ContainsKey(id))
                response.Failures.Add(new CalendarBatchFailedItem { Id = id, Reason = "事件不存在或无权限" });
        }

        // 2) 业务校验：构建"可操作 ID 集合"，不符合规则的单独记入 Failures
        var eligibleIds = new List<int>();
        foreach (var evt in events)
        {
            try
            {
                await _auth.EnsureCanModifyAsync(evt).ConfigureAwait(false);

                switch (op)
                {
                    case "delete":
                        // 允许重复日程父事件进入批量删除；执行阶段会自动级联其下所有未删除的子实例（与 deleteMode=all 行为一致）
                        eligibleIds.Add(evt.Id);
                        break;

                    case "setstatus":
                        if (evt.Status != normStatus)
                            ValidateStatusTransition(evt.Status, normStatus);
                        eligibleIds.Add(evt.Id);
                        break;

                    case "restore":
                        // 恢复重复实例时，父事件必须仍然有效（未被删除），否则无法恢复，避免产生孤儿实例
                        if (evt.RecurrenceParentId.HasValue)
                        {
                            var parent = await _unitOfWork.CalendarEvents
                                .GetByIdIgnoreQueryFilterAsync(evt.RecurrenceParentId.Value)
                                .ConfigureAwait(false);
                            if (parent == null || parent.DeletedAt != null)
                                throw new InvalidOperationException("父事件已被删除，请先恢复父事件");
                        }
                        eligibleIds.Add(evt.Id);
                        break;

                    case "seteventtype":
                    case "moveoffset":
                        eligibleIds.Add(evt.Id);
                        break;
                }
            }
            catch (Exception ex)
            {
                response.Failures.Add(new CalendarBatchFailedItem
                {
                    Id = evt.Id,
                    Reason = ex is ArgumentException aex ? aex.Message : "处理失败"
                });
            }
        }

        try
        {
            await _auditService.LogAsync(
                action: $"calendar.batch_{op}",
                entityType: "CalendarEvent",
                entityId: 0,
                oldValues: JsonSerializer.Serialize(new { op, count = uniqueIds.Count, ids = uniqueIds }),
                newValues: null).ConfigureAwait(false);
        }
        catch (Exception ex) { _logger.LogWarning(ex, "批量操作审计日志记录失败"); }

        int successCount = 0;
        int dbAffected = 0;

        if (eligibleIds.Count > 0)
        {
            // 3) 并发冲突检测：重新读取 RowVersion 与初始读取时比对，
            //    将被其他事务修改过的事件移入 Failures，避免 ExecuteUpdateAsync 绕过乐观锁导致丢失更新
            //    restore 需绕过全局查询过滤器，只针对仍处于已删除状态的事件做检测。
            var versionQuery = _unitOfWork.CalendarEvents.GetQueryable(asNoTracking: true);
            if (op == "restore")
            {
                versionQuery = versionQuery
                    .IgnoreQueryFilters()
                    .Where(e => eligibleIds.Contains(e.Id) && e.DeletedAt != null);
            }
            else
            {
                versionQuery = versionQuery
                    .Where(e => eligibleIds.Contains(e.Id) && e.DeletedAt == null);
            }
            var currentVersions = await versionQuery
                .Select(e => new { e.Id, e.RowVersion })
                .ToListAsync(ct).ConfigureAwait(false);

            var conflictIds = new HashSet<int>();
            foreach (var cv in currentVersions)
            {
                if (idToEvent.TryGetValue(cv.Id, out var original))
                {
                    if (original.RowVersion == null || cv.RowVersion == null ||
                        !original.RowVersion.SequenceEqual(cv.RowVersion))
                    {
                        conflictIds.Add(cv.Id);
                    }
                }
            }
            // 不在目标状态集合中的事件也视为冲突
            // （普通操作：已删除；restore：已被他人恢复），排除掉以避免误操作
            foreach (var id in eligibleIds)
            {
                if (!currentVersions.Exists(v => v.Id == id))
                    conflictIds.Add(id);
            }

            if (conflictIds.Count > 0)
            {
                foreach (var cid in conflictIds)
                {
                    response.Failures.Add(new CalendarBatchFailedItem { Id = cid, Reason = "操作冲突，数据可能已被修改，请重试" });
                    eligibleIds.Remove(cid);
                }
                _logger.LogWarning("批量操作检测到 {Count} 个并发冲突，已排除", conflictIds.Count);
            }

            if (eligibleIds.Count == 0)
            {
                response.SuccessCount = 0;
                return response;
            }

            // 4) 写：delete/setStatus/setEventType 用 ExecuteUpdateAsync 直接生成 SQL 批量更新
            using var transaction = await _unitOfWork.BeginTransactionAsync().ConfigureAwait(false);
            try
            {
                switch (op)
                {
                    case "delete":
                        // 级联扩展：如果 eligibleIds 中包含重复日程的父事件（IsRecurring && RecurrenceParentId==null），
                        // 自动把这些父事件下所有未删除的子实例 ID 一并加入删除集合，
                        // 与单个 DeleteAsync(deleteMode="all") 行为保持一致。
                        var deleteIdSet = new HashSet<int>(eligibleIds);
                        var parentIdsForCascade = events
                            .Where(e => eligibleIds.Contains(e.Id) && e.IsRecurring && e.RecurrenceParentId == null)
                            .Select(e => e.Id)
                            .ToList();
                        if (parentIdsForCascade.Count > 0)
                        {
                            var childIds = await _unitOfWork.CalendarEvents
                                .GetQueryable(asNoTracking: true)
                                .Where(e => parentIdsForCascade.Contains(e.RecurrenceParentId!.Value)
                                            && e.DeletedAt == null)
                                .Select(e => e.Id)
                                .ToListAsync(ct).ConfigureAwait(false);
                            foreach (var cid in childIds) deleteIdSet.Add(cid);
                        }
                        var finalDeleteIds = deleteIdSet.ToList();

                        // 软删除：设置 DeletedAt + UpdatedAt（ExecuteUpdateAsync 绕过 ChangeTracker，不会触发自动 UpdatedAt 赋值）
                        dbAffected = await _unitOfWork.CalendarEvents
                            .GetQueryable(asNoTracking: false)
                            .Where(e => finalDeleteIds.Contains(e.Id) && e.DeletedAt == null)
                            .ExecuteUpdateAsync(setters => setters
                                .SetProperty(e => e.DeletedAt, utcNow)
                                .SetProperty(e => e.UpdatedAt, utcNow), ct)
                            .ConfigureAwait(false);
                        break;

                    case "restore":
                        // 恢复：将 DeletedAt 置空（ExecuteUpdateAsync 绕过 ChangeTracker，手工设置 UpdatedAt）。
                        // 需绕过全局查询过滤器（DeletedAt == null）才能命中垃圾箱中仍处于已删除状态的事件。
                        dbAffected = await _unitOfWork.CalendarEvents
                            .GetQueryable(asNoTracking: false)
                            .IgnoreQueryFilters()
                            .Where(e => eligibleIds.Contains(e.Id) && e.DeletedAt != null)
                            .ExecuteUpdateAsync(setters => setters
                                .SetProperty(e => e.DeletedAt, (DateTime?)null)
                                .SetProperty(e => e.UpdatedAt, utcNow), ct)
                            .ConfigureAwait(false);
                        break;

                    case "setstatus":
                        dbAffected = await _unitOfWork.CalendarEvents
                            .GetQueryable(asNoTracking: false)
                            .Where(e => eligibleIds.Contains(e.Id) && e.DeletedAt == null)
                            .ExecuteUpdateAsync(setters => setters
                                .SetProperty(e => e.Status, normStatus)
                                .SetProperty(e => e.UpdatedAt, utcNow), ct)
                            .ConfigureAwait(false);
                        break;

                    case "seteventtype":
                        dbAffected = await _unitOfWork.CalendarEvents
                            .GetQueryable(asNoTracking: false)
                            .Where(e => eligibleIds.Contains(e.Id) && e.DeletedAt == null)
                            .ExecuteUpdateAsync(setters => setters
                                .SetProperty(e => e.EventType, normEventType)
                                .SetProperty(e => e.UpdatedAt, utcNow), ct)
                            .ConfigureAwait(false);
                        break;

                    case "moveoffset":
                        // moveoffset 无法用纯 SQL 统一处理所有行（每行 StartAt/EndAt 不同），
                        // 退回 tracked 更新：先逐行应用，SaveChanges 时并发冲突失败回滚并向 Failures 汇总
                        if (moveDays == 0 && moveMins == 0)
                        {
                            dbAffected = eligibleIds.Count;
                        }
                        else
                        {
                            var offset = TimeSpan.FromDays(moveDays) + TimeSpan.FromMinutes(moveMins);
                            var tracked = await _unitOfWork.CalendarEvents
                                .GetQueryable(asNoTracking: false)
                                .Where(e => eligibleIds.Contains(e.Id) && e.DeletedAt == null)
                                .ToListAsync(ct).ConfigureAwait(false);
                            foreach (var e in tracked)
                            {
                                e.StartAt = e.StartAt.Add(offset);
                                if (e.EndAt.HasValue) e.EndAt = e.EndAt.Value.Add(offset);
                                e.UpdatedAt = utcNow;
                            }
                            dbAffected = await _unitOfWork.SaveChangesAsync(ct).ConfigureAwait(false);
                            if (dbAffected > 0) dbAffected = tracked.Count;
                        }
                        break;
                }

                await transaction.CommitAsync().ConfigureAwait(false);
                successCount = dbAffected;
            }
            catch (DbUpdateConcurrencyException cex)
            {
                await transaction.SafeRollbackAsync().ConfigureAwait(false);
                _logger.LogWarning(cex, "批量操作发生乐观锁冲突，op={Op}，共 {Count} 个事件", op, eligibleIds.Count);
                foreach (var id in eligibleIds)
                    response.Failures.Add(new CalendarBatchFailedItem { Id = id, Reason = "操作冲突，数据可能已被修改，请重试" });
                successCount = 0;
            }
            catch (DbUpdateException uex)
            {
                await transaction.SafeRollbackAsync().ConfigureAwait(false);
                _logger.LogWarning(uex, "批量操作发生 DB 更新异常，op={Op}，共 {Count} 个事件", op, eligibleIds.Count);
                foreach (var id in eligibleIds)
                    response.Failures.Add(new CalendarBatchFailedItem { Id = id, Reason = "数据库约束冲突或更新失败，请重试" });
                successCount = 0;
            }
            catch (Exception ex)
            {
                await transaction.SafeRollbackAsync().ConfigureAwait(false);
                _logger.LogWarning(ex, "批量操作发生未知异常，op={Op}，共 {Count} 个事件", op, eligibleIds.Count);
                foreach (var id in eligibleIds)
                    response.Failures.Add(new CalendarBatchFailedItem { Id = id, Reason = "操作失败：" + ex.Message });
                successCount = 0;
            }
        }

        response.SuccessCount = successCount;
        response.FailedCount = response.Failures.Count;

        if (successCount > 0)
        {
            try
            {
                await _auditService.LogAsync(
                    action: $"calendar.batch_{op}_result",
                    entityType: "CalendarEvent",
                    entityId: 0,
                    oldValues: null,
                    newValues: JsonSerializer.Serialize(new
                    {
                        op,
                        status = normStatus,
                        eventType = normEventType,
                        daysOffset = moveDays,
                        minutesOffset = moveMins,
                        affectedIds = eligibleIds,
                        dbAffected = dbAffected
                    })).ConfigureAwait(false);
            }
            catch (Exception ex) { _logger.LogWarning(ex, "批量操作审计结果日志记录失败"); }

            try
            {
                var notifyUserIds = await _auth.ResolveAuthorizedUserIdsAsync(_currentUserAccessor.UserId).ConfigureAwait(false);
                await _notificationHubService.NotifyCalendarRefreshAsync(notifyUserIds ?? Array.Empty<int>())
                    .ConfigureAwait(false);
                if (_cache != null && notifyUserIds != null)
                {
                    foreach (var uid in notifyUserIds)
                        _cache.InvalidateAllForUser(uid);
                }
                _cache?.InvalidateAllForUser(_currentUserAccessor.UserId);
            }
            catch (Exception ex) { _logger.LogWarning(ex, "批量操作通知推送失败，不影响主流程"); }
        }

        return response;
    }

    /// <summary>批处理用的事件类型归一化（与 CalendarService.NormalizeEventType 语义对齐）。</summary>
    private static string NormalizeEventTypeForBatch(string? eventType)
    {
        if (string.IsNullOrEmpty(eventType)) return "task";
        var allowed = new HashSet<string>(StringComparer.OrdinalIgnoreCase)
            { "due_date", "task", "meeting", "reminder", "personal", "travel", "work", "other" };
        return allowed.Contains(eventType) ? eventType : "other";
    }
}
