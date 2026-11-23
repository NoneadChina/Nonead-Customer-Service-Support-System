using NoneadCSS.Domain.Entities;
using NoneadCSS.Domain.Repositories;
using NoneadCSS.Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore;

namespace NoneadCSS.Infrastructure.Repositories;

public class TranslationRepository : ITranslationRepository
{
    private readonly NoneadCSSDbContext _context;

    public TranslationRepository(NoneadCSSDbContext context)
    {
        _context = context;
    }

    public async Task<Translation?> GetByKeyAndCultureAsync(string key, string culture)
    {
        return await _context.Translations
            .AsNoTracking()
            .FirstOrDefaultAsync(t => t.Key == key && t.Culture == culture);
    }

    /// <summary>
    /// 按 key + culture 查询翻译（tracking，适用于后续修改 + SaveChanges 的场景）
    /// </summary>
    public async Task<Translation?> GetByKeyAndCultureTrackedAsync(string key, string culture)
    {
        return await _context.Translations
            .FirstOrDefaultAsync(t => t.Key == key && t.Culture == culture);
    }

    public async Task<List<Translation>> GetByCultureAsync(string culture)
    {
        return await _context.Translations
            .AsNoTracking()
            .Where(t => t.Culture == culture)
            .OrderBy(t => t.Key)
            .ToListAsync();
    }

    public async Task<List<Translation>> GetAllAsync()
    {
        return await _context.Translations
            .AsNoTracking()
            .OrderBy(t => t.Culture)
            .ThenBy(t => t.Key)
            .ToListAsync();
    }

    public async Task AddAsync(Translation translation)
    {
        await _context.Translations.AddAsync(translation);
    }

    public Task UpdateAsync(Translation translation)
    {
        // 实体应由调用方通过 GetByKeyAndCultureTrackedAsync 获取（tracked），
        // 直接修改属性后由 SaveChangesAsync 持久化，无需显式 Update。
        // 保留方法以满足 ITranslationRepository 接口契约。
        return Task.CompletedTask;
    }

    public Task DeleteAsync(Translation translation)
    {
        _context.Translations.Remove(translation);
        return Task.CompletedTask;
    }
}
