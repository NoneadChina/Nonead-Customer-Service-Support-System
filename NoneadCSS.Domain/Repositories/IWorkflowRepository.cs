using NoneadCSS.Domain.Entities;

namespace NoneadCSS.Domain.Repositories;

public interface IWorkflowRepository : IRepository<Workflow>
{
    /// <summary>
    /// 获取启用中的工作流（按邮箱筛选，包含"所有邮箱"的全局规则），按 SortOrder 排序。
    /// </summary>
    Task<List<Workflow>> GetActiveByMailboxAsync(int mailboxId, string triggerType);

    Task<(IEnumerable<Workflow> Items, int Total)> GetPagedAsync(
        int page, int pageSize, int? mailboxId = null, bool? isActive = null, string? triggerType = null, string? keyword = null);

    Task<(IEnumerable<Workflow> Items, int Total)> GetDeletedPagedAsync(
        int page, int pageSize, int? mailboxId = null, string? keyword = null);

    Task<int> GetDeletedCountAsync(int? mailboxId = null);

    Task<Workflow?> GetDeletedByIdAsync(int id);

    Task<int> HardDeleteAllDeletedAsync(int? mailboxId = null);

    Task HardDeleteByIdAsync(int id);

    Task<List<WorkflowExecutionLog>> GetExecutionLogsAsync(
        int workflowId, int limit = 50);

    Task<(IEnumerable<WorkflowExecutionLog> Items, int Total)> GetExecutionLogsPagedAsync(
        int page, int pageSize, int? workflowId = null, int? conversationId = null, string? status = null,
        DateTime? startDate = null, DateTime? endDate = null);

    Task AddExecutionLogAsync(WorkflowExecutionLog log);
    Task<Dictionary<string, int>> GetStatsAsync();
    Task<List<Workflow>> GetAllWithMailboxAsync();
}
