using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Caching.Memory;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using NoneadCSS.Application.Configuration;
using NoneadCSS.Application.DTOs.Requests;
using NoneadCSS.Application.DTOs.Responses;
using NoneadCSS.Domain;

namespace NoneadCSS.Application.Services;

public class SystemOptionService : ISystemOptionService
{
    private readonly IUnitOfWork _unitOfWork;
    private readonly IPasswordPolicyService _passwordPolicyService;
    private readonly ILogger<SystemOptionService> _logger;
    private readonly IMemoryCache _cache;
    private readonly TimeSpan _cacheDuration;

    public SystemOptionService(IUnitOfWork unitOfWork, IPasswordPolicyService passwordPolicyService, ILogger<SystemOptionService> logger, IMemoryCache cache, IOptions<CacheOptions> cacheOptions)
    {
        _unitOfWork = unitOfWork;
        _passwordPolicyService = passwordPolicyService;
        _logger = logger;
        _cache = cache;
        _cacheDuration = TimeSpan.FromMinutes(cacheOptions.Value.DefaultCacheMinutes);
    }

    /// <summary>
    /// 检查配置项是否属于安全分类（涉及密码策略），并清除密码策略缓存
    /// </summary>
    private void InvalidatePasswordPolicyCacheIfNeeded(IEnumerable<string> names)
    {
        var securityRelated = new HashSet<string>(StringComparer.OrdinalIgnoreCase)
        {
            "security.password_min_length",
            "security.password_requires_uppercase",
            "security.password_requires_lowercase",
            "security.password_requires_number",
            "security.password_requires_symbol"
        };

        if (names.Any(n => securityRelated.Contains(n)))
        {
            _passwordPolicyService.InvalidateCache();
        }
    }

    private void InvalidateOptionCache(string? name = null, string? category = null)
    {
        _cache.Remove("SystemOption:All");
        _cache.Remove("SystemOption:Dictionary");
        if (!string.IsNullOrEmpty(name))
        {
            _cache.Remove($"SystemOption:Name:{name}");
            _cache.Remove($"SystemOption:Value:{name}");
        }
        if (!string.IsNullOrEmpty(category))
        {
            _cache.Remove($"SystemOption:Category:{category}");
        }
    }

    public async Task<List<SystemOptionResponse>> GetAllAsync()
    {
        var cacheKey = "SystemOption:All";
        var result = await _cache.GetOrCreateAsync(cacheKey, async entry =>
        {
            entry.AbsoluteExpirationRelativeToNow = _cacheDuration;
            var options = await _unitOfWork.SystemOptions.GetAllWithOptionsAsync();
            return options.Select(ToResponse).ToList();
        });
        return result ?? throw new InvalidOperationException("系统配置缓存工厂返回 null，属于异常状态。");
    }

    public async Task<List<SystemOptionResponse>> GetByCategoryAsync(string category)
    {
        var cacheKey = $"SystemOption:Category:{category}";
        var result = await _cache.GetOrCreateAsync(cacheKey, async entry =>
        {
            entry.AbsoluteExpirationRelativeToNow = _cacheDuration;
            var options = await _unitOfWork.SystemOptions.GetByCategoryAsync(category);
            return options.Select(ToResponse).ToList();
        });
        return result ?? throw new InvalidOperationException("系统配置缓存工厂返回 null，属于异常状态。");
    }

    public async Task<SystemOptionResponse?> GetByNameAsync(string name)
    {
        var cacheKey = $"SystemOption:Name:{name}";
        return await _cache.GetOrCreateAsync(cacheKey, async entry =>
        {
            entry.AbsoluteExpirationRelativeToNow = _cacheDuration;
            var option = await _unitOfWork.SystemOptions.GetByNameAsync(name);
            return option != null ? ToResponse(option) : null;
        });
    }

    public async Task<string?> GetValueAsync(string name)
    {
        var cacheKey = $"SystemOption:Value:{name}";
        return await _cache.GetOrCreateAsync(cacheKey, async entry =>
        {
            entry.AbsoluteExpirationRelativeToNow = _cacheDuration;
            return await _unitOfWork.SystemOptions.GetValueAsync(name);
        });
    }

    public async Task<SystemOptionResponse> CreateAsync(CreateSystemOptionRequest request)
    {
        var existing = await _unitOfWork.SystemOptions.GetByNameAsync(request.Name);
        if (existing != null)
        {
            throw new InvalidOperationException($"配置项 \"{request.Name}\" 已存在");
        }

        await _unitOfWork.SystemOptions.SetAsync(request.Name, request.Value, request.Category);
        await _unitOfWork.SaveChangesAsync();
        var option = await _unitOfWork.SystemOptions.GetByNameAsync(request.Name);

        InvalidateOptionCache(name: request.Name, category: request.Category);

        if (option == null)
            throw new InvalidOperationException("配置项保存后无法重新加载，可能已被其他进程删除。");
        return ToResponse(option);
    }

    public async Task<SystemOptionResponse?> UpdateAsync(string name, UpdateSystemOptionRequest request)
    {
        var option = await _unitOfWork.SystemOptions.GetByNameTrackedAsync(name);
        if (option == null)
        {
            return null;
        }

        option.Value = request.Value;
        if (request.Category != null)
        {
            option.Category = request.Category;
        }
        option.UpdatedAt = DateTime.UtcNow;
        // option 已通过 tracked 查询，由 SaveChangesAsync 持久化，无需 UpdateAsync

        if (name == "mail.fetch_frequency" && int.TryParse(request.Value, out var newFrequency))
        {
            await UpdateAllMailboxFetchFrequencyAsync(newFrequency);
        }

        await _unitOfWork.SaveChangesAsync();

        InvalidateOptionCache(name: name, category: option.Category);
        InvalidatePasswordPolicyCacheIfNeeded(new[] { name });

        return ToResponse(option);
    }

    private async Task UpdateAllMailboxFetchFrequencyAsync(int newFrequency)
    {
        // 使用 tracking 查询，确保修改 FetchFrequency 能持久化到数据库。
        // 仅更新活跃邮箱（非活跃邮箱 IsValidForFetch 为 false，不会执行收取，无需更新其频率）。
        var mailboxes = await _unitOfWork.Mailboxes.GetActiveTrackedAsync();
        foreach (var mailbox in mailboxes)
        {
            mailbox.FetchFrequency = newFrequency;
            mailbox.UpdatedAt = DateTime.UtcNow;
        }
        _logger.LogInformation("系统设置修改：将所有邮箱的收取频率更新为 {Frequency} 分钟", newFrequency);
    }

    public async Task<bool> DeleteAsync(string name)
    {
        var option = await _unitOfWork.SystemOptions.GetByNameAsync(name);
        if (option == null)
        {
            return false;
        }

        await _unitOfWork.SystemOptions.DeleteAsync(option);
        await _unitOfWork.SaveChangesAsync();

        InvalidateOptionCache(name: name, category: option.Category);

        return true;
    }

    public async Task<Dictionary<string, string>> GetAllAsDictionaryAsync()
    {
        var cacheKey = "SystemOption:Dictionary";
        var result = await _cache.GetOrCreateAsync(cacheKey, async entry =>
        {
            entry.AbsoluteExpirationRelativeToNow = _cacheDuration;
            var options = await _unitOfWork.SystemOptions.GetAllWithOptionsAsync();
            return options.ToDictionary(o => o.Name, o => o.Value);
        });
        return result ?? throw new InvalidOperationException("系统配置字典缓存工厂返回 null，属于异常状态。");
    }

    public async Task BatchUpdateAsync(Dictionary<string, string> options)
    {
        var affectedNames = new List<string>();

        using var transaction = await _unitOfWork.BeginTransactionAsync();
        try
        {
            // 批量查询所有需要更新的配置项，避免 N+1 查询（原先每个 key 都查一次）
            var optionNames = options.Keys.ToList();
            var existingOptions = await _unitOfWork.SystemOptions.GetQueryable(false)
                .Where(o => optionNames.Contains(o.Name))
                .ToListAsync();

            var existingDict = existingOptions.ToDictionary(o => o.Name);
            foreach (var kvp in options)
            {
                if (existingDict.TryGetValue(kvp.Key, out var existing))
                {
                    existing.Value = kvp.Value;
                    existing.UpdatedAt = DateTime.UtcNow;
                    // existing 已通过 tracked 查询，由 SaveChangesAsync 持久化，无需 UpdateAsync
                    affectedNames.Add(kvp.Key);
                }
            }
            await _unitOfWork.SaveChangesAsync();
            await transaction.CommitAsync();
        }
        catch
        {
            await transaction.SafeRollbackAsync();
            throw;
        }

        foreach (var name in affectedNames)
        {
            InvalidateOptionCache(name: name);
            // 同步失效仓储层 30 秒缓存，确保 EmailIngestionService / ChatService 等直读仓储的渠道立即生效
            _unitOfWork.SystemOptions.InvalidateCache(name);
        }

        InvalidatePasswordPolicyCacheIfNeeded(options.Keys);
    }

    private static SystemOptionResponse ToResponse(Domain.Entities.SystemOption option)
    {
        return new SystemOptionResponse
        {
            Id = option.Id,
            Name = option.Name,
            Value = option.Value,
            Category = option.Category,
            UpdatedAt = option.UpdatedAt
        };
    }
}
