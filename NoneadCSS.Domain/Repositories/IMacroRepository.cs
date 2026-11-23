using NoneadCSS.Domain.Entities;

namespace NoneadCSS.Domain.Repositories;

public interface IMacroRepository
{
    Task<Macro?> GetByIdAsync(int id);
    Task<Macro?> GetByIdTrackedAsync(int id);
    Task<List<Macro>> GetByMailboxAsync(int? mailboxId);
    Task<(List<Macro> Items, int Total)> GetPagedAsync(int page, int pageSize, int? mailboxId = null, bool? isActive = null, string? keyword = null);
    Task<int> GetCountAsync(int? mailboxId = null, bool? isActive = null);
    Task AddAsync(Macro macro);
    Task UpdateAsync(Macro macro);
    Task DeleteAsync(int id);

    // 垃圾箱
    Task<(List<Macro> Items, int Total)> GetDeletedPagedAsync(int page, int pageSize, string? keyword = null);
    Task<int> CountDeletedAsync();
    Task SoftDeleteAsync(int id);
    Task RestoreAsync(int id);
    Task HardDeleteAsync(int id);
    Task<int> HardDeleteAllDeletedAsync();
}

public interface IMacroExecutionLogRepository
{
    Task<MacroExecutionLog?> GetByIdAsync(int id, bool includeDetails = false);
    Task<(List<MacroExecutionLog> Items, int Total)> GetPagedAsync(int page, int pageSize, int? macroId = null, int? conversationId = null);
    Task<List<MacroExecutionLog>> GetRecentAsync(int count);
    Task AddAsync(MacroExecutionLog log);
    Task<int> CountAsync();
    Task<int> CountTodayAsync();
}
