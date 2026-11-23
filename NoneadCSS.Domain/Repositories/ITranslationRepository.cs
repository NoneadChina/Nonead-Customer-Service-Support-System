using NoneadCSS.Domain.Entities;

namespace NoneadCSS.Domain.Repositories;

public interface ITranslationRepository
{
    Task<Translation?> GetByKeyAndCultureAsync(string key, string culture);

    /// <summary>
    /// 按 key + culture 查询翻译（tracking，适用于后续修改 + SaveChanges 的场景）
    /// </summary>
    Task<Translation?> GetByKeyAndCultureTrackedAsync(string key, string culture);

    Task<List<Translation>> GetByCultureAsync(string culture);
    Task<List<Translation>> GetAllAsync();
    Task AddAsync(Translation translation);
    Task UpdateAsync(Translation translation);
    Task DeleteAsync(Translation translation);
}
