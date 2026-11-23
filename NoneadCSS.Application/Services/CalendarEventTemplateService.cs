using Microsoft.EntityFrameworkCore;
using NoneadCSS.Application.Common;
using NoneadCSS.Application.DTOs.Requests;
using NoneadCSS.Application.DTOs.Responses;
using NoneadCSS.Domain;
using NoneadCSS.Domain.Entities;

namespace NoneadCSS.Application.Services;

/// <summary>
/// 日历事件模板服务实现
/// </summary>
public class CalendarEventTemplateService : ICalendarEventTemplateService
{
    private readonly IUnitOfWork _unitOfWork;
    private readonly ICurrentUserAccessor _currentUserAccessor;
    private readonly IUserRoleResolver _userRoleResolver;

    public CalendarEventTemplateService(IUnitOfWork unitOfWork, ICurrentUserAccessor currentUserAccessor, IUserRoleResolver userRoleResolver)
    {
        _unitOfWork = unitOfWork;
        _currentUserAccessor = currentUserAccessor;
        _userRoleResolver = userRoleResolver;
    }

    public async Task<(List<CalendarEventTemplateDto> Items, int Total)> GetListAsync(int? mailboxId = null, bool includeShared = true)
    {
        var currentUserId = _currentUserAccessor.UserId;
        var isSuperAdmin = await _userRoleResolver.IsSuperAdminAsync(currentUserId);

        var query = _unitOfWork.Repository<CalendarEventTemplate>()
            .GetQueryable(false)
            .Where(t => t.DeletedAt == null);

        // 邮箱过滤
        if (mailboxId.HasValue)
        {
            query = query.Where(t => t.MailboxId == mailboxId.Value || t.MailboxId == null);
        }

        // 权限过滤：非管理员只能看共享模板或自己创建的模板
        if (!isSuperAdmin)
        {
            query = query.Where(t =>
                t.IsShared ||                              // 全局共享
                t.CreatedByUserId == currentUserId);       // 自己创建的
        }

        var total = await query.CountAsync();
        var items = await query
            .Include(t => t.Mailbox)
            .Include(t => t.CreatedByUser)
            .OrderBy(t => t.SortOrder)
            .ThenBy(t => t.Id)
            .ToListAsync();

        return (items.Select(ToDto).ToList(), total);
    }

    public async Task<CalendarEventTemplateDto?> GetByIdAsync(int id)
    {
        var entity = await _unitOfWork.Repository<CalendarEventTemplate>()
            .GetQueryable(false)
            .Include(t => t.Mailbox)
            .Include(t => t.CreatedByUser)
            .FirstOrDefaultAsync(t => t.Id == id && t.DeletedAt == null);
        if (entity == null) return null;

        // 权限校验
        var currentUserId = _currentUserAccessor.UserId;
        var isSuperAdmin = await _userRoleResolver.IsSuperAdminAsync(currentUserId);
        if (!isSuperAdmin && !entity.IsShared && entity.CreatedByUserId != currentUserId)
            return null;

        return ToDto(entity);
    }

    public async Task<CalendarEventTemplateDto> CreateAsync(CreateCalendarEventTemplateRequest request)
    {
        var currentUserId = _currentUserAccessor.UserId;
        var isSuperAdmin = await _userRoleResolver.IsSuperAdminAsync(currentUserId);

        var entity = new CalendarEventTemplate
        {
            Name = request.Name.Trim(),
            Title = request.Title.Trim(),
            EventType = NormalizeEventType(request.EventType),
            DurationMin = Math.Clamp(request.DurationMin, 0, 1440),
            IsAllDay = request.IsAllDay,
            Description = string.IsNullOrWhiteSpace(request.Description) ? null : request.Description.Trim(),
            Location = string.IsNullOrWhiteSpace(request.Location) ? null : request.Location.Trim(),
            SortOrder = request.SortOrder,
            IsShared = isSuperAdmin ? request.IsShared : false, // 非超管默认不能创建共享模板
            MailboxId = request.MailboxId,
            CreatedByUserId = currentUserId == 0 ? null : currentUserId,
            CreatedAt = DateTime.UtcNow,
            UpdatedAt = DateTime.UtcNow,
        };

        await _unitOfWork.Repository<CalendarEventTemplate>().AddAsync(entity);
        await _unitOfWork.SaveChangesAsync();
        return ToDto(entity);
    }

    public async Task<CalendarEventTemplateDto?> UpdateAsync(int id, UpdateCalendarEventTemplateRequest request)
    {
        var currentUserId = _currentUserAccessor.UserId;
        var isSuperAdmin = await _userRoleResolver.IsSuperAdminAsync(currentUserId);

        var entity = await _unitOfWork.Repository<CalendarEventTemplate>()
            .GetQueryable(false)
            .FirstOrDefaultAsync(t => t.Id == id && t.DeletedAt == null);
        if (entity == null) return null;

        // 权限校验：管理员或创建者本人
        if (!isSuperAdmin && entity.CreatedByUserId != currentUserId)
            return null;

        entity.Name = request.Name.Trim();
        entity.Title = request.Title.Trim();
        entity.EventType = NormalizeEventType(request.EventType);
        entity.DurationMin = Math.Clamp(request.DurationMin, 0, 1440);
        entity.IsAllDay = request.IsAllDay;
        entity.Description = string.IsNullOrWhiteSpace(request.Description) ? null : request.Description.Trim();
        entity.Location = string.IsNullOrWhiteSpace(request.Location) ? null : request.Location.Trim();
        entity.SortOrder = request.SortOrder;
        entity.IsShared = isSuperAdmin ? request.IsShared : entity.IsShared; // 非超管不能修改共享状态
        entity.MailboxId = request.MailboxId;
        entity.UpdatedAt = DateTime.UtcNow;

        await _unitOfWork.SaveChangesAsync();
        return ToDto(entity);
    }

    public async Task<bool> DeleteAsync(int id)
    {
        var currentUserId = _currentUserAccessor.UserId;
        var isSuperAdmin = await _userRoleResolver.IsSuperAdminAsync(currentUserId);

        var entity = await _unitOfWork.Repository<CalendarEventTemplate>()
            .GetQueryable(false)
            .FirstOrDefaultAsync(t => t.Id == id && t.DeletedAt == null);
        if (entity == null) return false;

        // 权限校验：管理员或创建者本人
        if (!isSuperAdmin && entity.CreatedByUserId != currentUserId)
            return false;

        entity.SoftDelete();
        await _unitOfWork.SaveChangesAsync();
        return true;
    }

    private static string NormalizeEventType(string? eventType)
    {
        var allowed = new HashSet<string> { "due_date", "task", "meeting", "reminder", "personal", "travel", "work", "other" };
        var t = eventType?.Trim().ToLowerInvariant() ?? "task";
        return allowed.Contains(t) ? t : "other";
    }

    private static CalendarEventTemplateDto ToDto(CalendarEventTemplate entity)
    {
        return new CalendarEventTemplateDto
        {
            Id = entity.Id,
            Name = entity.Name,
            Title = entity.Title,
            EventType = entity.EventType,
            DurationMin = entity.DurationMin,
            IsAllDay = entity.IsAllDay,
            Description = entity.Description,
            Location = entity.Location,
            SortOrder = entity.SortOrder,
            IsShared = entity.IsShared,
            MailboxId = entity.MailboxId,
            MailboxName = entity.Mailbox?.Name,
            CreatedByUserId = entity.CreatedByUserId,
            CreatedByUserName = !string.IsNullOrEmpty(entity.CreatedByUser?.FirstName) && !string.IsNullOrEmpty(entity.CreatedByUser?.LastName)
                ? $"{entity.CreatedByUser.FirstName} {entity.CreatedByUser.LastName}".Trim()
                : entity.CreatedByUser?.Email,
            CreatedAt = entity.CreatedAt,
            UpdatedAt = entity.UpdatedAt,
            DeletedAt = entity.DeletedAt,
        };
    }
}
