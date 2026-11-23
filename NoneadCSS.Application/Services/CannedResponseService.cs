using Microsoft.EntityFrameworkCore;
using NoneadCSS.Application.Common;
using NoneadCSS.Application.DTOs.Requests;
using NoneadCSS.Application.DTOs.Responses;
using NoneadCSS.Domain;
using NoneadCSS.Domain.Entities;

namespace NoneadCSS.Application.Services;

/// <summary>
/// 快捷回复模板服务实现
/// </summary>
public class CannedResponseService : ICannedResponseService
{
    private readonly IUnitOfWork _unitOfWork;
    private readonly ICurrentUserAccessor _currentUserAccessor;

    public CannedResponseService(IUnitOfWork unitOfWork, ICurrentUserAccessor currentUserAccessor)
    {
        _unitOfWork = unitOfWork;
        _currentUserAccessor = currentUserAccessor;
    }

    public async Task<List<CannedResponseDto>> GetListAsync(int? mailboxId = null, int? userId = null, string? category = null)
    {
        var list = await _unitOfWork.CannedResponses.GetByMailboxAsync(mailboxId, userId);

        if (!string.IsNullOrEmpty(category))
        {
            list = list.Where(c => c.Category == category).ToList();
        }

        return list.Select(ToDto).ToList();
    }

    public async Task<CannedResponseDto?> GetByIdAsync(int id)
    {
        var entity = await _unitOfWork.CannedResponses.GetByIdAsync(id);
        // 已软删除的视为不存在
        if (entity == null || entity.DeletedAt != null) return null;

        if (!await _currentUserAccessor.CanAccessMailboxAsync(entity.MailboxId))
            return null;

        return ToDto(entity);
    }

    public async Task<List<CannedResponseDto>> SearchAsync(string keyword, int? mailboxId = null, int? userId = null)
    {
        if (string.IsNullOrWhiteSpace(keyword))
        {
            return await GetListAsync(mailboxId, userId);
        }
        var list = await _unitOfWork.CannedResponses.SearchAsync(keyword, mailboxId, userId);
        return list.Select(ToDto).ToList();
    }

    public async Task<List<string>> GetCategoriesAsync(int? mailboxId = null)
    {
        return await _unitOfWork.CannedResponses.GetAllCategoriesAsync(mailboxId);
    }

    public async Task<CannedResponseDto> CreateAsync(CreateCannedResponseRequest request, int createdByUserId)
    {
        var entity = new CannedResponse
        {
            MailboxId = request.MailboxId,
            Name = request.Name.Trim(),
            Body = HtmlSanitizerHelper.Sanitize(request.Body),
            Category = string.IsNullOrWhiteSpace(request.Category) ? null : request.Category.Trim(),
            SortOrder = request.SortOrder,
            IsShared = request.IsShared,
            CreatedByUserId = createdByUserId,
            CreatedAt = DateTime.UtcNow,
            UpdatedAt = DateTime.UtcNow,
        };

        await _unitOfWork.CannedResponses.AddAsync(entity);
        await _unitOfWork.SaveChangesAsync();
        return ToDto(entity);
    }

    public async Task<CannedResponseDto?> UpdateAsync(int id, UpdateCannedResponseRequest request, int userId, bool isAdmin)
    {
        var entity = await _unitOfWork.CannedResponses.GetQueryable(false)
            .FirstOrDefaultAsync(c => c.Id == id && c.DeletedAt == null);
        if (entity == null) return null;

        // 归属过滤：检查当前用户是否有权访问该资源关联的邮箱
        if (!await _currentUserAccessor.CanAccessMailboxAsync(entity.MailboxId))
            return null;

        // 权限校验：管理员或创建者本人
        if (!isAdmin && entity.CreatedByUserId != userId)
        {
            return null;
        }

        entity.Name = request.Name.Trim();
        entity.Body = HtmlSanitizerHelper.Sanitize(request.Body);
        entity.Category = string.IsNullOrWhiteSpace(request.Category) ? null : request.Category.Trim();
        entity.MailboxId = request.MailboxId;
        entity.SortOrder = request.SortOrder;
        entity.IsShared = request.IsShared;
        entity.UpdatedAt = DateTime.UtcNow;

        await _unitOfWork.SaveChangesAsync();
        return ToDto(entity);
    }

    public async Task<bool> DeleteAsync(int id, int userId, bool isAdmin)
    {
        var entity = await _unitOfWork.CannedResponses.GetQueryable(false)
            .FirstOrDefaultAsync(c => c.Id == id && c.DeletedAt == null);
        if (entity == null) return false;

        // 归属过滤：检查当前用户是否有权访问该资源关联的邮箱
        if (!await _currentUserAccessor.CanAccessMailboxAsync(entity.MailboxId))
            return false;

        // 权限校验：管理员或创建者本人
        if (!isAdmin && entity.CreatedByUserId != userId)
        {
            return false;
        }

        // 软删除：仅标记删除时间，不物理删除
        entity.DeletedAt = DateTime.UtcNow;
        entity.UpdatedAt = DateTime.UtcNow;
        await _unitOfWork.SaveChangesAsync();
        return true;
    }

    /// <inheritdoc />
    public async Task<(IEnumerable<CannedResponseDto> Items, int Total)> GetTrashAsync(int page, int pageSize, string? keyword = null)
    {
        var (items, total) = await _unitOfWork.CannedResponses.GetDeletedAsync(page, pageSize, keyword);
        return (items.Select(ToDto).ToList(), total);
    }

    /// <inheritdoc />
    public async Task<int> GetTrashCountAsync()
    {
        return await _unitOfWork.CannedResponses.CountDeletedAsync();
    }

    /// <inheritdoc />
    public async Task<bool> RestoreAsync(int id, int userId, bool isAdmin)
    {
        var entity = await _unitOfWork.CannedResponses.GetDeletedByIdAsync(id);
        if (entity == null) return false;

        // 权限校验：管理员或创建者本人
        if (!isAdmin && entity.CreatedByUserId != userId)
        {
            return false;
        }

        return await _unitOfWork.CannedResponses.RestoreAsync(id);
    }

    /// <inheritdoc />
    public async Task<bool> HardDeleteAsync(int id, int userId, bool isAdmin)
    {
        var entity = await _unitOfWork.CannedResponses.GetDeletedByIdAsync(id);
        if (entity == null) return false;

        // 权限校验：管理员或创建者本人
        if (!isAdmin && entity.CreatedByUserId != userId)
        {
            return false;
        }

        return await _unitOfWork.CannedResponses.HardDeleteAsync(id);
    }

    /// <inheritdoc />
    public async Task<int> ClearTrashAsync()
    {
        return await _unitOfWork.CannedResponses.HardDeleteAllDeletedAsync();
    }

    private static CannedResponseDto ToDto(CannedResponse entity)
    {
        return new CannedResponseDto
        {
            Id = entity.Id,
            MailboxId = entity.MailboxId,
            MailboxName = entity.Mailbox?.Name,
            Name = entity.Name,
            Body = entity.Body,
            Category = entity.Category,
            SortOrder = entity.SortOrder,
            IsShared = entity.IsShared,
            CreatedByUserId = entity.CreatedByUserId,
            CreatedByUserName = entity.CreatedByUser?.GetFullName(),
            CreatedAt = entity.CreatedAt,
            UpdatedAt = entity.UpdatedAt,
            DeletedAt = entity.DeletedAt,
        };
    }
}
