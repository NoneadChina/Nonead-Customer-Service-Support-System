using Microsoft.EntityFrameworkCore;
using NoneadCSS.Application.Common;
using NoneadCSS.Application.DTOs.Requests;
using NoneadCSS.Application.DTOs.Responses;
using NoneadCSS.Domain;
using NoneadCSS.Domain.Entities;

namespace NoneadCSS.Application.Services;

public class EmailSignatureService : IEmailSignatureService
{
    private readonly IUnitOfWork _unitOfWork;
    private readonly ICurrentUserAccessor _currentUserAccessor;

    public EmailSignatureService(IUnitOfWork unitOfWork, ICurrentUserAccessor currentUserAccessor)
    {
        _unitOfWork = unitOfWork;
        _currentUserAccessor = currentUserAccessor;
    }

    public async Task<(IEnumerable<EmailSignatureDto> Items, int Total)> GetPagedAsync(EmailSignatureQuery query)
    {
        var (items, total) = await _unitOfWork.EmailSignatures.GetPagedAsync(
            query.Page, query.PageSize,
            query.UserId, query.MailboxId,
            query.IsDefault, query.Keyword);

        var dtos = items.Select(MapToDto).ToList();
        return (dtos, total);
    }

    public async Task<EmailSignatureDto?> GetByIdAsync(int id)
    {
        var sig = await _unitOfWork.EmailSignatures.GetByIdAsync(id);
        // 已软删除的签名视为不存在
        if (sig == null || sig.DeletedAt != null) return null;

        if (!await _currentUserAccessor.CanAccessMailboxAsync(sig.MailboxId))
            return null;

        return MapToDto(sig);
    }

    public async Task<EmailSignatureDto> CreateAsync(int userId, CreateEmailSignatureRequest request)
    {
        var sig = new EmailSignature
        {
            UserId = userId,
            Name = request.Name,
            Body = HtmlSanitizerHelper.Sanitize(request.Body),
            MailboxId = request.MailboxId,
            IsDefault = request.IsDefault,
            SortOrder = request.SortOrder,
            CreatedAt = DateTime.UtcNow,
            UpdatedAt = DateTime.UtcNow
        };

        await _unitOfWork.EmailSignatures.AddAsync(sig);
        await _unitOfWork.SaveChangesAsync();

        if (request.IsDefault)
        {
            await _unitOfWork.EmailSignatures.SetDefaultAsync(userId, request.MailboxId, sig.Id);
            await _unitOfWork.SaveChangesAsync();
        }

        return MapToDto(sig);
    }

    public async Task<EmailSignatureDto?> UpdateAsync(int id, UpdateEmailSignatureRequest request)
    {
        var sig = await _unitOfWork.EmailSignatures.GetQueryable(false)
            .FirstOrDefaultAsync(s => s.Id == id && s.DeletedAt == null);
        if (sig == null) return null;

        if (!await _currentUserAccessor.CanAccessMailboxAsync(sig.MailboxId))
            return null;

        sig.Name = request.Name;
        sig.Body = HtmlSanitizerHelper.Sanitize(request.Body);
        sig.MailboxId = request.MailboxId;
        sig.IsDefault = request.IsDefault;
        sig.SortOrder = request.SortOrder;
        sig.UpdatedAt = DateTime.UtcNow;

        await _unitOfWork.SaveChangesAsync();

        if (request.IsDefault)
        {
            await _unitOfWork.EmailSignatures.SetDefaultAsync(sig.UserId, request.MailboxId, sig.Id);
            await _unitOfWork.SaveChangesAsync();
        }

        return MapToDto(sig);
    }

    public async Task<bool> DeleteAsync(int id)
    {
        var sig = await _unitOfWork.EmailSignatures.GetQueryable(false)
            .FirstOrDefaultAsync(s => s.Id == id && s.DeletedAt == null);
        if (sig == null) return false;

        if (!await _currentUserAccessor.CanAccessMailboxAsync(sig.MailboxId))
            return false;

        // 软删除：标记删除时间，并取消默认状态以避免恢复后冲突
        sig.DeletedAt = DateTime.UtcNow;
        sig.UpdatedAt = DateTime.UtcNow;
        if (sig.IsDefault)
        {
            sig.IsDefault = false;
        }
        await _unitOfWork.SaveChangesAsync();

        return true;
    }

    public async Task<EmailSignatureOverviewResponse> GetOverviewAsync()
    {
        var (items, total) = await _unitOfWork.EmailSignatures.GetPagedAsync(1, 100);
        var allItems = items.ToList();

        var defaultCount = allItems.Count(s => s.IsDefault);
        var mailboxCount = allItems.Count(s => s.MailboxId.HasValue);
        var globalCount = allItems.Count(s => !s.MailboxId.HasValue);

        var (topItems, _) = await _unitOfWork.EmailSignatures.GetPagedAsync(1, 5, isDefault: true);

        return new EmailSignatureOverviewResponse
        {
            TotalSignatures = total,
            DefaultSignatures = defaultCount,
            MailboxSignatures = mailboxCount,
            GlobalSignatures = globalCount,
            TopSignatures = topItems.Select(MapToDto).ToList()
        };
    }

    public async Task<List<EmailSignatureDto>> GetByUserIdAsync(int userId, int? mailboxId = null)
    {
        var sigs = await _unitOfWork.EmailSignatures.GetByUserIdAndMailboxIdAsync(userId, mailboxId);
        return sigs.Select(MapToDto).ToList();
    }

    public async Task<EmailSignatureDto?> GetDefaultAsync(int userId, int? mailboxId = null)
    {
        var sig = await _unitOfWork.EmailSignatures.GetDefaultByUserIdAsync(userId, mailboxId);
        return sig != null ? MapToDto(sig) : null;
    }

    public async Task<bool> SetDefaultAsync(int userId, int id)
    {
        var sig = await _unitOfWork.EmailSignatures.GetByIdAsync(id);
        if (sig == null || sig.UserId != userId || sig.DeletedAt != null) return false;

        await _unitOfWork.EmailSignatures.SetDefaultAsync(userId, sig.MailboxId, id);
        await _unitOfWork.SaveChangesAsync();
        return true;
    }

    private EmailSignatureDto MapToDto(EmailSignature sig)
    {
        return new EmailSignatureDto
        {
            Id = sig.Id,
            UserId = sig.UserId,
            UserName = sig.User != null ? $"{sig.User.FirstName} {sig.User.LastName}" : string.Empty,
            UserEmail = sig.User?.Email ?? string.Empty,
            MailboxId = sig.MailboxId,
            MailboxName = sig.Mailbox?.Name,
            Name = sig.Name,
            Body = sig.Body,
            IsDefault = sig.IsDefault,
            SortOrder = sig.SortOrder,
            CreatedAt = sig.CreatedAt,
            UpdatedAt = sig.UpdatedAt,
            DeletedAt = sig.DeletedAt
        };
    }

    /// <inheritdoc />
    public async Task<(IEnumerable<EmailSignatureDto> Items, int Total)> GetTrashAsync(int page, int pageSize, string? keyword = null)
    {
        var (items, total) = await _unitOfWork.EmailSignatures.GetDeletedAsync(page, pageSize, keyword);
        return (items.Select(MapToDto).ToList(), total);
    }

    /// <inheritdoc />
    public async Task<int> GetTrashCountAsync()
    {
        return await _unitOfWork.EmailSignatures.CountDeletedAsync();
    }

    /// <inheritdoc />
    public async Task<bool> RestoreAsync(int id)
    {
        return await _unitOfWork.EmailSignatures.RestoreAsync(id);
    }

    /// <inheritdoc />
    public async Task<bool> HardDeleteAsync(int id)
    {
        if (_currentUserAccessor != null && !_currentUserAccessor.IsSuperAdmin)
        {
            return false;
        }
        return await _unitOfWork.EmailSignatures.HardDeleteAsync(id);
    }

    /// <inheritdoc />
    public async Task<int> ClearTrashAsync()
    {
        return await _unitOfWork.EmailSignatures.HardDeleteAllDeletedAsync();
    }
}
