namespace NoneadCSS.Api.Controllers;

/// <summary>
/// 分页参数辅助类，统一管理分页上限与规范化逻辑。
/// </summary>
public static class PaginationHelper
{
    /// <summary>
    /// 默认每页条数。
    /// </summary>
    public const int DefaultPageSize = 20;

    /// <summary>
    /// 列表/日志等大数据量场景的默认每页条数。
    /// </summary>
    public const int LargePageSize = 50;

    /// <summary>
    /// 回收站/通知等小数据量场景的默认每页条数。
    /// </summary>
    public const int SmallPageSize = 10;

    /// <summary>
    /// 通用每页条数上限，防止请求大量数据导致 DoS。
    /// </summary>
    public const int MaxPageSize = 100;

    /// <summary>
    /// 审计日志完整性校验等特殊场景的上限（默认 100 条校验较慢，允许放大到 500）。
    /// </summary>
    public const int MaxAuditLogPageSize = 500;

    /// <summary>
    /// 规范化分页参数：page 不小于 1，pageSize 限制在 [1, <see cref="MaxPageSize"/>]。
    /// </summary>
    /// <param name="page">页码</param>
    /// <param name="pageSize">每页条数</param>
    /// <returns>规范化后的 (Page, PageSize) 元组</returns>
    public static (int Page, int PageSize) Normalize(int page, int pageSize)
    {
        return (Math.Max(page, 1), Math.Min(Math.Max(pageSize, 1), MaxPageSize));
    }
}
