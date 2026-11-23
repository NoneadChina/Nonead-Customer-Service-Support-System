using NoneadCSS.Api.Filters;
using NoneadCSS.Application.DTOs.Responses;
using NoneadCSS.Application.Services;
using NoneadCSS.Domain;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.RateLimiting;
using System.Security.Claims;

namespace NoneadCSS.Api.Controllers;

/// <summary>
/// 全局搜索控制器 - 提供跨实体类型的统一搜索接口
/// 支持会话、客户、知识库等多种实体类型的聚合搜索
/// </summary>
[ApiController]
[Route("api/[controller]")]
[Authorize]
[RequireModule("core")]
public class SearchController : ControllerBase
{
    private readonly IUnitOfWork _unitOfWork;
    private readonly IMailboxService _mailboxService;
    private readonly IUserRoleResolver _userRoleResolver;

    public SearchController(
        IUnitOfWork unitOfWork,
        IMailboxService mailboxService,
        IUserRoleResolver userRoleResolver)
    {
        _unitOfWork = unitOfWork;
        _mailboxService = mailboxService;
        _userRoleResolver = userRoleResolver;
    }

    /// <summary>
    /// 获取当前用户可访问的邮箱 ID 集合（用于在搜索中限制结果范围）
    /// 超管返回 null（不限制），普通用户仅返回其有权限的邮箱 ID
    /// </summary>
    private async Task<List<int>?> GetAccessibleMailboxIdsAsync()
    {
        var userId = int.Parse(User.FindFirst(ClaimTypes.NameIdentifier)?.Value ?? "0");
        if (await _userRoleResolver.IsSuperAdminAsync(userId))
        {
            return null; // 超管不限制
        }
        var mailboxes = await _mailboxService.GetMailboxesByUserIdAsync(userId);
        return mailboxes.Select(m => m.Id).ToList();
    }

    /// <summary>
    /// 全局搜索（跨会话、客户、知识库等实体类型）
    /// </summary>
    /// <remarks>
    /// 按 entity 类型过滤：conversation / customer / knowledge / all（默认 all）。
    /// 使用 SQLite 的 LIKE 查询进行子串匹配，返回每类最多 pageSize 条结果。
    /// 搜索仅返回已授权访问的数据，不返回软删除记录。
    /// </remarks>
    /// <param name="q">搜索关键词（至少 2 个字符）</param>
    /// <param name="entity">实体类型：conversation / customer / knowledge / all（默认 all）</param>
    /// <param name="page">页码（默认 1）</param>
    /// <param name="pageSize">每页条数（默认 20，最大 50）</param>
    /// <returns>按类型分组的搜索结果</returns>
    [HttpGet]
    [EnableRateLimiting("SearchPolicy")]
    public async Task<IActionResult> GlobalSearch(
        [FromQuery] string? q,
        [FromQuery] string entity = "all",
        [FromQuery] int page = 1,
        [FromQuery] int pageSize = PaginationHelper.DefaultPageSize)
    {
        if (string.IsNullOrWhiteSpace(q) || q.Length < 2)
        {
            return BadRequest(new { message = "搜索关键词至少需要 2 个字符" });
        }

        q = q.Trim();
        if (page <= 0) page = 1;
        pageSize = Math.Clamp(pageSize, 1, 50);

        var scope = entity.Trim().ToLowerInvariant();
        var result = new GlobalSearchResponse
        {
            Query = q,
            Page = page,
            PageSize = pageSize
        };

        // IDOR 防护：先统一获取当前用户可访问的邮箱 ID 集合
        // null = 超管不限制；空列表 = 无邮箱权限；非空 = 邮箱作用域
        var accessibleMailboxIds = await GetAccessibleMailboxIdsAsync();
        // accessibleMailboxArray 用于客户/知识库搜索的邮箱作用域过滤参数（null = 不限制）
        int[]? accessibleMailboxArray = accessibleMailboxIds?.ToArray();

        if (scope is "all" or "conversation")
        {
            // 防止 IDOR：仅返回当前用户可访问的邮箱内的会话
                if (accessibleMailboxIds != null && accessibleMailboxIds.Count == 0)
                {
                    // 用户没有任何邮箱权限，返回空结果
                    result.Conversations = new List<ConversationSearchItem>();
                    result.ConversationTotal = 0;
                }
                else
                {
                    var (items, total) = await _unitOfWork.Conversations
                        .AdvancedSearchAsync(
                            keyword: q,
                            mailboxIds: accessibleMailboxIds,
                            page: page,
                            pageSize: pageSize);
                    result.Conversations = items.Select(c => new ConversationSearchItem
                    {
                        Id = c.Id,
                        Subject = c.Subject,
                        Preview = c.Preview,
                        CustomerEmail = c.CustomerEmail,
                        Status = (int)c.Status,
                        Priority = (int)c.Priority,
                        CreatedAt = c.CreatedAt,
                        LastReplyAt = c.LastReplyAt,
                        MailboxId = c.MailboxId,
                        Snippet = BuildSnippet(c.Subject, c.Preview, q)
                    }).ToList();
                    result.ConversationTotal = total;
                }
            }

            if (scope is "all" or "customer")
            {
                // 防止 IDOR：仅返回曾在授权邮箱下产生会话的客户
                var (items, total) = await _unitOfWork.Customers.SearchAsync(q, page, pageSize, accessibleMailboxArray);
                result.Customers = items.Select(c => new CustomerSearchItem
                {
                    Id = c.Id,
                    FullName = c.GetFullName(),
                    Email = c.GetMainEmail(),
                    Phone = c.GetPhones().FirstOrDefault()?.Value,
                    Company = c.Company,
                    CreatedAt = c.CreatedAt,
                    Snippet = BuildSnippet(c.GetFullName(), c.GetMainEmail(), q)
                }).ToList();
                result.CustomerTotal = total;
            }

            if (scope is "all" or "knowledge")
            {
                // 防止 IDOR：仅返回全局分类或属于授权邮箱的知识库文章
                var articles = await _unitOfWork.KnowledgeArticles.SearchAsync(q, page, pageSize, accessibleMailboxArray);
                var total = await _unitOfWork.KnowledgeArticles.SearchCountAsync(q, accessibleMailboxArray);
                result.Knowledge = articles.Select(a => new KnowledgeSearchItem
                {
                    Id = a.Id,
                    Title = a.Title,
                    Excerpt = a.Content.Length > 200 ? a.Content.Substring(0, 200) + "..." : a.Content,
                    CategoryId = a.CategoryId,
                    ViewCount = a.ViewCount,
                    CreatedAt = a.CreatedAt,
                    UpdatedAt = a.UpdatedAt,
                    Snippet = BuildSnippet(a.Title, a.Content, q)
                }).ToList();
                result.KnowledgeTotal = total;
            }

            result.Total = result.ConversationTotal + result.CustomerTotal + result.KnowledgeTotal;
            return Ok(result);
    }

    private static string BuildSnippet(string? primary, string? secondary, string keyword)
    {
        var primaryLower = (primary ?? string.Empty).ToLowerInvariant();
        var secondaryLower = (secondary ?? string.Empty).ToLowerInvariant();
        var kw = keyword.ToLowerInvariant();

        var source = primaryLower.Contains(kw) ? primary : secondary;
        if (string.IsNullOrEmpty(source)) return string.Empty;

        var idx = source.ToLowerInvariant().IndexOf(kw, StringComparison.Ordinal);
        if (idx < 0) return source.Length > 200 ? source.Substring(0, 200) + "..." : source;

        const int radius = 40;
        var start = Math.Max(0, idx - radius);
        var end = Math.Min(source.Length, idx + keyword.Length + radius);
        var snippet = (start > 0 ? "..." : string.Empty) +
                      source.Substring(start, end - start) +
                      (end < source.Length ? "..." : string.Empty);
        return snippet;
    }
}

