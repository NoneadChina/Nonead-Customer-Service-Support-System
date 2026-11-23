using System.Security.Cryptography;
using System.Text;
using NoneadCSS.Application.DTOs.Requests;
using NoneadCSS.Application.DTOs.Responses;
using NoneadCSS.Application.Mappings;
using NoneadCSS.Application.Security;
using NoneadCSS.Domain;
using NoneadCSS.Domain.Entities;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;

namespace NoneadCSS.Application.Services;

public class ApiKeyService : IApiKeyService
{
    private readonly IUnitOfWork _unitOfWork;
    private readonly ICurrentUserAccessor? _currentUserAccessor;
    private readonly ILogger<ApiKeyService> _logger;

    public ApiKeyService(IUnitOfWork unitOfWork, ICurrentUserAccessor? currentUserAccessor = null,
        ILogger<ApiKeyService>? logger = null)
    {
        _unitOfWork = unitOfWork;
        _currentUserAccessor = currentUserAccessor;
        _logger = logger ?? Microsoft.Extensions.Logging.Abstractions.NullLogger<ApiKeyService>.Instance;
    }

    public async Task<ApiKeyOverviewResponse> GetOverviewAsync()
    {
        var stats = await _unitOfWork.ApiKeys.GetStatsAsync();

        var (recentKeys, _) = await _unitOfWork.ApiKeys.GetPagedAsync(1, 5);
        var recentKeyDtos = recentKeys.Select(k => k.ToDto()).ToList();

        var allLogs = await _unitOfWork.ApiKeyLogs.GetPagedAsync(1, 10);
        var recentLogs = allLogs.Items.Select(l => l.ToDto()).ToList();

        // 直接通过数据库端聚合获取 scope 分布（无需加载全表），避免 N+1 与全表内存加载。
        var scopesCount = await _unitOfWork.ApiKeys.GetScopeCountsAsync();

        return new ApiKeyOverviewResponse
        {
            TotalKeys = stats.GetValueOrDefault("total", 0),
            ActiveKeys = stats.GetValueOrDefault("active", 0),
            InactiveKeys = stats.GetValueOrDefault("inactive", 0),
            TotalUsage = stats.GetValueOrDefault("totalUsage", 0),
            RecentKeys = recentKeyDtos,
            RecentLogs = recentLogs,
            ScopesCount = scopesCount
        };
    }

    public async Task<(List<ApiKeyDto> Items, int TotalCount)> GetPagedAsync(ApiKeyQuery query)
    {
        var (items, total) = await _unitOfWork.ApiKeys.GetPagedAsync(
            query.Page, query.PageSize,
            query.UserId, query.IsActive, query.Keyword, query.Scope);
        return (items.Select(k => k.ToDto()).ToList(), total);
    }

    public async Task<ApiKeyDto?> GetByIdAsync(int id)
    {
        var apiKey = await _unitOfWork.ApiKeys.GetByIdAsync(id);
        // 已软删除的视为不存在
        if (apiKey == null || apiKey.DeletedAt != null) return null;
        return apiKey.ToDto();
    }

    public async Task<ApiKeyDto> CreateAsync(CreateApiKeyRequest request)
    {
        var key = GenerateApiKey();

        var apiKey = new ApiKey
        {
            Name = request.Name,
            Description = request.Description,
            Key = SecretHasher.Hash(key),
            UserId = request.UserId,
            Scopes = request.Scopes != null && request.Scopes.Count > 0
                ? string.Join(",", request.Scopes)
                : string.Empty,
            ExpiresAt = request.ExpiresAt,
            IsActive = request.IsActive
        };

        var created = await _unitOfWork.ApiKeys.AddAsync(apiKey);
        await _unitOfWork.SaveChangesAsync();

        // 仅在创建时返回明文密钥，后续无法再获取
        var dto = created.ToDto(includeFullKey: true);
        dto.Key = key;
        return dto;
    }

    public async Task<ApiKeyDto?> UpdateAsync(UpdateApiKeyRequest request, string? ifMatch = null)
    {
        // 使用 tracking 查询，避免 AsNoTracking + UpdateAsync 反模式（全字段 UPDATE）
        var existing = await _unitOfWork.ApiKeys.GetQueryable(false)
            .FirstOrDefaultAsync(k => k.Id == request.Id && k.DeletedAt == null);
        if (existing == null)
        {
            return null;
        }

        // M8: 乐观锁 If-Match 校验（HTTP 标准优先，Header 覆盖 Body）
        var effectiveIfMatch = !string.IsNullOrEmpty(ifMatch) ? ifMatch.Trim('"') : request.IfMatch;
        ConcurrencyCheckHelper.Check(existing, existing.RowVersion, effectiveIfMatch, "ApiKey", existing.Id,
            () => existing.ToDto(), _logger);

        existing.Name = request.Name;
        existing.Description = request.Description;
        existing.Scopes = request.Scopes != null && request.Scopes.Count > 0
            ? string.Join(",", request.Scopes)
            : string.Empty;
        existing.ExpiresAt = request.ExpiresAt;
        existing.IsActive = request.IsActive;
        existing.UpdatedAt = DateTime.UtcNow;

        await _unitOfWork.SaveChangesAsync();

        return existing.ToDto();
    }

    public async Task<bool> DeleteAsync(int id)
    {
        // 使用 tracking 查询，确保 SoftDelete 修改能被 EF Core 跟踪并持久化
        var existing = await _unitOfWork.ApiKeys.GetQueryable(false)
            .FirstOrDefaultAsync(k => k.Id == id && k.DeletedAt == null);
        if (existing == null)
        {
            return false;
        }

        existing.DeletedAt = DateTime.UtcNow;
        existing.UpdatedAt = DateTime.UtcNow;
        await _unitOfWork.SaveChangesAsync();
        return true;
    }

    // ====== 垃圾箱 ======
    public async Task<(List<ApiKeyDto> Items, int TotalCount)> GetTrashPagedAsync(int page = 1, int pageSize = 10, string? keyword = null)
    {
        var (items, total) = await _unitOfWork.ApiKeys.GetDeletedPagedAsync(page, pageSize, keyword);
        return (items.Select(k => k.ToDto()).ToList(), total);
    }

    public async Task<ApiKeyDto?> RestoreAsync(int id)
    {
        // 使用 IgnoreQueryFilters 查询已删除的实体（含 User 关联），避免软删除过滤器遮蔽
        var apiKey = await _unitOfWork.ApiKeys.GetDeletedByIdAsync(id);
        if (apiKey == null) return null;

        // RestoreAsync 内部使用 ExecuteUpdateAsync（bulk 操作，不跟踪实体），
        // 此处需重新查询已恢复的实体以获取最新状态（含 User 关联）
        await _unitOfWork.ApiKeys.RestoreAsync(id);

        var restored = await _unitOfWork.ApiKeys.GetQueryable(true)
            .Include(k => k.User)
            .FirstOrDefaultAsync(k => k.Id == id);

        return restored?.ToDto();
    }

    public async Task<bool> HardDeleteAsync(int id)
    {
        if (_currentUserAccessor != null && !_currentUserAccessor.IsSuperAdmin)
        {
            return false;
        }
        return await _unitOfWork.ApiKeys.HardDeleteAsync(id);
    }

    public async Task<int> ClearTrashAsync()
    {
        return await _unitOfWork.ApiKeys.HardDeleteAllDeletedAsync();
    }

    public async Task<ApiKeyDto?> ToggleActiveAsync(int id, string? ifMatch = null)
    {
        // 使用 tracking 查询，避免 AsNoTracking + UpdateAsync 反模式
        var existing = await _unitOfWork.ApiKeys.GetQueryable(false)
            .FirstOrDefaultAsync(k => k.Id == id && k.DeletedAt == null);
        if (existing == null)
        {
            return null;
        }

        // M8: 乐观锁 If-Match 校验，防止并发启停互相覆盖
        ConcurrencyCheckHelper.Check(existing, existing.RowVersion, ifMatch?.Trim('"'), "ApiKey", existing.Id,
            () => existing.ToDto(), _logger);

        existing.IsActive = !existing.IsActive;
        existing.UpdatedAt = DateTime.UtcNow;

        await _unitOfWork.SaveChangesAsync();

        return existing.ToDto();
    }

    public async Task<ApiKeyDto?> RegenerateKeyAsync(int id, string? ifMatch = null)
    {
        // 使用 tracking 查询，避免 AsNoTracking + UpdateAsync 反模式
        var existing = await _unitOfWork.ApiKeys.GetQueryable(false)
            .FirstOrDefaultAsync(k => k.Id == id && k.DeletedAt == null);
        if (existing == null)
        {
            return null;
        }

        // M8: 乐观锁 If-Match 校验，防止重生成与更新互相覆盖
        ConcurrencyCheckHelper.Check(existing, existing.RowVersion, ifMatch?.Trim('"'), "ApiKey", existing.Id,
            () => existing.ToDto(), _logger);

        var newKey = GenerateApiKey();
        existing.Key = SecretHasher.Hash(newKey);
        existing.UpdatedAt = DateTime.UtcNow;

        await _unitOfWork.SaveChangesAsync();

        // 仅在重新生成时返回明文密钥
        var dto = existing.ToDto(includeFullKey: true);
        dto.Key = newKey;
        return dto;
    }

    public async Task<List<ApiKeyLogDto>> GetLogsAsync(int apiKeyId, int limit = 50)
    {
        var logs = await _unitOfWork.ApiKeyLogs.GetByApiKeyAsync(apiKeyId, limit);
        return logs.Select(l => l.ToDto()).ToList();
    }

    public async Task<(List<ApiKeyLogDto> Items, int TotalCount)> GetLogsPagedAsync(ApiKeyLogQuery query)
    {
        var (items, total) = await _unitOfWork.ApiKeyLogs.GetPagedAsync(
            query.Page, query.PageSize,
            query.ApiKeyId, query.Endpoint, query.StatusCode);
        return (items.Select(l => l.ToDto()).ToList(), total);
    }

    private static string GenerateApiKey()
    {
        var randomBytes = RandomNumberGenerator.GetBytes(32);
        var key = new StringBuilder();
        key.Append("fsk_");
        key.Append(Convert.ToBase64String(randomBytes)
            .Replace("+", string.Empty)
            .Replace("/", string.Empty)
            .Replace("=", string.Empty)
            .Substring(0, 32));
        return key.ToString();
    }
}
