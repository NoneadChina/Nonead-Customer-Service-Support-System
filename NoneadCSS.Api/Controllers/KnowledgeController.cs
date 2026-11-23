using NoneadCSS.Api.Authorization;
using NoneadCSS.Application.DTOs.Responses;
using NoneadCSS.Application.Services;
using NoneadCSS.Api.Filters;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.RateLimiting;
using System.Security.Cryptography;
using System.Text;

namespace NoneadCSS.Api.Controllers;

/// <summary>
/// 知识库控制器 - 管理知识库分类和文章
/// </summary>
[ApiController]
[Route("api/[controller]")]
[Authorize]
[RequireModule("knowledgebase")]
public class KnowledgeController : ControllerBase
{
    private readonly IKnowledgeService _service;

    public KnowledgeController(IKnowledgeService service)
    {
        _service = service;
    }

    /// <summary>
    /// 将业务层 InvalidOperationException 映射为正确的 HTTP 状态码。
    /// - 消息包含"无权" → 403 Forbidden
    /// - 消息包含"不存在" → 404 Not Found
    /// - 其他业务校验失败 → 400 BadRequest
    /// </summary>
    // KB-004 修复：异常映射改用类型判断，不再依赖脆弱的字符串匹配
    private ActionResult MapBusinessException(Exception ex)
    {
        var msg = ex.Message ?? string.Empty;
        if (ex is UnauthorizedAccessException)
        {
            return Forbid();
        }
        if (ex is KeyNotFoundException)
        {
            return NotFound(new { message = msg });
        }
        return BadRequest(new { message = msg });
    }

    // ====== 分类 ======

    [HttpGet("categories")]
    public async Task<ActionResult<List<KnowledgeCategoryDto>>> GetCategories(
        [FromQuery] int? parentId = null,
        [FromQuery] int? mailboxId = null,
        [FromQuery] bool? isActive = null,
        [FromQuery] string? keyword = null)
    {
        var query = new KnowledgeCategoryQuery
        {
            ParentId = parentId,
            MailboxId = mailboxId,
            IsActive = isActive,
            Keyword = keyword
        };
        var result = await _service.GetCategoriesAsync(query);
        return Ok(result);
    }

    [HttpGet("categories/{id}")]
    [RequireSuperAdmin]
    public async Task<ActionResult<KnowledgeCategoryDto>> GetCategoryById(int id)
    {
        var result = await _service.GetCategoryByIdAsync(id);
        if (result == null) return NotFound();
        return Ok(result);
    }

    [HttpPost("categories")]
    [RequireSuperAdmin]
    public async Task<ActionResult<KnowledgeCategoryDto>> CreateCategory([FromBody] CreateKnowledgeCategoryRequest request)
    {
        try
        {
            var result = await _service.CreateCategoryAsync(request);
            return CreatedAtAction(nameof(GetCategoryById), new { id = result.Id }, result);
        }
        catch (Exception ex) when (ex is UnauthorizedAccessException or KeyNotFoundException or InvalidOperationException)
        {
            return MapBusinessException(ex);
        }
    }

    [HttpPut("categories/{id}")]
    [RequireSuperAdmin]
    public async Task<ActionResult<KnowledgeCategoryDto>> UpdateCategory(int id, [FromBody] UpdateKnowledgeCategoryRequest request)
    {
        if (id != request.Id) return BadRequest("ID 不一致");
        try
        {
            var headerIfMatch = Request.Headers.IfMatch.FirstOrDefault();
            var ifMatch = !string.IsNullOrEmpty(headerIfMatch)
                ? headerIfMatch.Trim('"')
                : request.IfMatch;
            var result = await _service.UpdateCategoryAsync(id, request, ifMatch);
            return Ok(result);
        }
        catch (Exception ex) when (ex is UnauthorizedAccessException or KeyNotFoundException or InvalidOperationException)
        {
            return MapBusinessException(ex);
        }
    }

    [HttpDelete("categories/{id}")]
    [RequireSuperAdmin]
    public async Task<ActionResult> DeleteCategory(int id)
    {
        await _service.DeleteCategoryAsync(id);
        return NoContent();
    }

    // ====== 文章 ======

    [HttpGet("articles")]
    public async Task<ActionResult> GetArticles(
        [FromQuery] int? categoryId = null,
        [FromQuery] string? status = null,
        [FromQuery] string? keyword = null,
        [FromQuery] int? authorId = null,
        [FromQuery] bool? isPinned = null,
        [FromQuery] int page = 1,
        [FromQuery] int pageSize = PaginationHelper.DefaultPageSize)
    {
        // 限制分页参数上限，防止请求大量数据导致 DoS
        page = Math.Max(page, 1);
        pageSize = Math.Min(Math.Max(pageSize, 1), PaginationHelper.MaxPageSize);
        var query = new KnowledgeArticleQuery
        {
            CategoryId = categoryId,
            Status = status,
            Keyword = keyword,
            AuthorId = authorId,
            IsPinned = isPinned,
            Page = page,
            PageSize = pageSize
        };
        var result = await _service.GetArticlesAsync(query);
        return Ok(result);
    }

    [HttpGet("articles/{id}")]
    public async Task<ActionResult<KnowledgeArticleDto>> GetArticleById(int id, [FromQuery] bool incrementView = false)
    {
        var result = await _service.GetArticleByIdAsync(id, incrementView);
        if (result == null) return NotFound();
        return Ok(result);
    }

    [HttpPost("articles")]
    [RequireSuperAdmin]
    public async Task<ActionResult<KnowledgeArticleDto>> CreateArticle([FromBody] CreateKnowledgeArticleRequest request)
    {
        try
        {
            var result = await _service.CreateArticleAsync(request);
            return CreatedAtAction(nameof(GetArticleById), new { id = result.Id }, result);
        }
        catch (Exception ex) when (ex is UnauthorizedAccessException or KeyNotFoundException or InvalidOperationException)
        {
            return MapBusinessException(ex);
        }
    }

    [HttpPut("articles/{id}")]
    [RequireSuperAdmin]
    public async Task<ActionResult<KnowledgeArticleDto>> UpdateArticle(int id, [FromBody] UpdateKnowledgeArticleRequest request)
    {
        if (id != request.Id) return BadRequest("ID 不一致");
        try
        {
            var headerIfMatch = Request.Headers.IfMatch.FirstOrDefault();
            var ifMatch = !string.IsNullOrEmpty(headerIfMatch)
                ? headerIfMatch.Trim('"')
                : request.IfMatch;
            var result = await _service.UpdateArticleAsync(id, request, ifMatch);
            return Ok(result);
        }
        catch (Exception ex) when (ex is UnauthorizedAccessException or KeyNotFoundException or InvalidOperationException)
        {
            return MapBusinessException(ex);
        }
    }

    [HttpDelete("articles/{id}")]
    [RequireSuperAdmin]
    public async Task<ActionResult> DeleteArticle(int id)
    {
        await _service.DeleteArticleAsync(id);
        return NoContent();
    }

    [HttpPost("articles/{id}/like")]
    [EnableRateLimiting("KnowledgeLikePolicy")]
    public async Task<ActionResult> LikeArticle(int id)
    {
        // 控制器标注 [Authorize]，未登录用户已被框架拦截返回 401。
        // 点赞指纹由服务层基于 _currentUserAccessor.UserId 生成（u:{userId}），
        // 确保每个登录用户唯一且不可伪造。

        try
        {
            // T000101: toggle 语义——已点赞则取消（返回 liked=false），未点赞则点赞（返回 liked=true）
            var liked = await _service.LikeArticleAsync(id, userFingerprint: null);
            return Ok(new { success = true, liked });
        }
        catch (Exception ex) when (ex is UnauthorizedAccessException or KeyNotFoundException or InvalidOperationException)
        {
            return MapBusinessException(ex);
        }
    }

    // ====== 文章附件 ======

    /// <summary>
    /// 获取文章附件列表
    /// </summary>
    [HttpGet("articles/{articleId}/attachments")]
    public async Task<ActionResult<List<KnowledgeArticleAttachmentDto>>> GetAttachments(int articleId)
    {
        try
        {
            var result = await _service.GetAttachmentsAsync(articleId);
            return Ok(result);
        }
        catch (Exception ex) when (ex is UnauthorizedAccessException or KeyNotFoundException or InvalidOperationException)
        {
            return MapBusinessException(ex);
        }
    }

    /// <summary>
    /// 上传文章附件（PDF/图片/文档）。仅超管可操作。
    /// 限制：25MB，扩展名白名单（pdf/jpg/png/gif/doc/docx/xls/xlsx/txt）。
    /// </summary>
    [HttpPost("articles/{articleId}/attachments")]
    [RequireSuperAdmin]
    [RequestSizeLimit(26214400)] // 25MB
    public async Task<ActionResult<KnowledgeArticleAttachmentDto>> UploadAttachment(int articleId, [FromForm] IFormFile file)
    {
        if (file == null || file.Length == 0)
            return BadRequest(new { message = "未选择文件" });

        try
        {
            // 将 IFormFile 转换为 Application 层 DTO，避免 Application 层引用 AspNetCore
            var uploadRequest = new KnowledgeAttachmentUploadRequest
            {
                Stream = file.OpenReadStream(),
                FileName = file.FileName,
                MimeType = file.ContentType,
                FileSize = file.Length
            };
            var result = await _service.UploadAttachmentAsync(articleId, uploadRequest);
            return CreatedAtAction(nameof(GetAttachments), new { articleId }, result);
        }
        catch (Exception ex) when (ex is UnauthorizedAccessException or KeyNotFoundException or InvalidOperationException)
        {
            return MapBusinessException(ex);
        }
    }

    /// <summary>
    /// 删除文章附件（软删除 + 删除物理文件）。仅超管可操作。
    /// </summary>
    [HttpDelete("articles/{articleId}/attachments/{attachmentId}")]
    [RequireSuperAdmin]
    public async Task<ActionResult> DeleteAttachment(int articleId, int attachmentId)
    {
        try
        {
            var deleted = await _service.DeleteAttachmentAsync(articleId, attachmentId);
            if (!deleted) return NotFound();
            return NoContent();
        }
        catch (Exception ex) when (ex is UnauthorizedAccessException or KeyNotFoundException or InvalidOperationException)
        {
            return MapBusinessException(ex);
        }
    }

    /// <summary>
    /// 下载文章附件。鉴权后返回文件流。
    /// </summary>
    [HttpGet("articles/{articleId}/attachments/{attachmentId}/download")]
    public async Task<ActionResult> DownloadAttachment(int articleId, int attachmentId)
    {
        var result = await _service.GetAttachmentFileAsync(articleId, attachmentId);
        if (result == null) return NotFound();

        var (stream, mimeType, fileName) = result.Value;
        // File(Stream, ...) 会自动 dispose stream
        return File(stream, mimeType, fileName);
    }

    // ====== 文章垃圾箱 ======

    [HttpGet("articles/trash")]
    [RequireSuperAdmin]
    public async Task<ActionResult> GetArticleTrash([FromQuery] int page = 1, [FromQuery] int pageSize = PaginationHelper.SmallPageSize, [FromQuery] string? keyword = null)
    {
        // 限制分页参数上限，防止请求大量数据导致 DoS
        page = Math.Max(page, 1);
        pageSize = Math.Min(Math.Max(pageSize, 1), PaginationHelper.MaxPageSize);
        var result = await _service.GetArticleTrashAsync(page, pageSize, keyword);
        return Ok(result);
    }

    [HttpPost("articles/{id}/restore")]
    [RequireSuperAdmin]
    public async Task<ActionResult> RestoreArticle(int id)
    {
        var result = await _service.RestoreArticleAsync(id);
        if (result == null) return NotFound();
        return Ok(result);
    }

    [HttpDelete("articles/{id}/hard")]
    [RequireSuperAdmin]
    public async Task<ActionResult> HardDeleteArticle(int id)
    {
        await _service.HardDeleteArticleAsync(id);
        return NoContent();
    }

    [HttpDelete("articles/trash")]
    [RequireSuperAdmin]
    public async Task<ActionResult> ClearArticleTrash()
    {
        var count = await _service.ClearArticleTrashAsync();
        return Ok(new { count });
    }

    // ====== 分类垃圾箱 ======

    [HttpGet("categories/trash")]
    [RequireSuperAdmin]
    public async Task<ActionResult> GetCategoryTrash([FromQuery] int page = 1, [FromQuery] int pageSize = PaginationHelper.SmallPageSize, [FromQuery] string? keyword = null)
    {
        // 限制分页参数上限，防止请求大量数据导致 DoS
        page = Math.Max(page, 1);
        pageSize = Math.Min(Math.Max(pageSize, 1), PaginationHelper.MaxPageSize);
        var result = await _service.GetCategoryTrashAsync(page, pageSize, keyword);
        return Ok(result);
    }

    [HttpPost("categories/{id}/restore")]
    [RequireSuperAdmin]
    public async Task<ActionResult> RestoreCategory(int id)
    {
        var result = await _service.RestoreCategoryAsync(id);
        if (result == null) return NotFound();
        return Ok(result);
    }

    [HttpDelete("categories/{id}/hard")]
    [RequireSuperAdmin]
    public async Task<ActionResult> HardDeleteCategory(int id)
    {
        await _service.HardDeleteCategoryAsync(id);
        return NoContent();
    }

    [HttpDelete("categories/trash")]
    [RequireSuperAdmin]
    public async Task<ActionResult> ClearCategoryTrash()
    {
        var count = await _service.ClearCategoryTrashAsync();
        return Ok(new { count });
    }

    // ====== 搜索和概览 ======

    [HttpGet("search")]
    public async Task<ActionResult<KnowledgeSearchResult>> Search(
        [FromQuery] string keyword,
        [FromQuery] int page = 1,
        [FromQuery] int pageSize = PaginationHelper.DefaultPageSize)
    {
        // 限制分页参数上限，防止请求大量数据导致 DoS
        page = Math.Max(page, 1);
        pageSize = Math.Min(Math.Max(pageSize, 1), PaginationHelper.MaxPageSize);
        if (string.IsNullOrWhiteSpace(keyword))
        {
            return Ok(new KnowledgeSearchResult());
        }
        var result = await _service.SearchAsync(keyword, page, pageSize);
        return Ok(result);
    }

    [HttpGet("overview")]
    public async Task<ActionResult<KnowledgeOverviewResponse>> GetOverview()
    {
        var result = await _service.GetOverviewAsync();
        return Ok(result);
    }
}
