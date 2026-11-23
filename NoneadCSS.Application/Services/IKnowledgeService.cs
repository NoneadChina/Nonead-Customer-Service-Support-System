using NoneadCSS.Application.DTOs.Responses;

namespace NoneadCSS.Application.Services;

public interface IKnowledgeService
{
    // 分类
    Task<List<KnowledgeCategoryDto>> GetCategoriesAsync(KnowledgeCategoryQuery query);
    Task<KnowledgeCategoryDto?> GetCategoryByIdAsync(int id);
    Task<KnowledgeCategoryDto> CreateCategoryAsync(CreateKnowledgeCategoryRequest request);
    Task<KnowledgeCategoryDto> UpdateCategoryAsync(int id, UpdateKnowledgeCategoryRequest request, string? ifMatch = null);
    Task DeleteCategoryAsync(int id);

    // 文章
    Task<PagedResponse<KnowledgeArticleDto>> GetArticlesAsync(KnowledgeArticleQuery query);
    Task<KnowledgeArticleDto?> GetArticleByIdAsync(int id, bool incrementView = false);
    Task<KnowledgeArticleDto> CreateArticleAsync(CreateKnowledgeArticleRequest request);
    Task<KnowledgeArticleDto> UpdateArticleAsync(int id, UpdateKnowledgeArticleRequest request, string? ifMatch = null);
    Task DeleteArticleAsync(int id);
    Task<bool> LikeArticleAsync(int id, string? userFingerprint);

    // 文章附件（PDF/图片/文档，关联文章）
    Task<List<KnowledgeArticleAttachmentDto>> GetAttachmentsAsync(int articleId);
    Task<KnowledgeArticleAttachmentDto> UploadAttachmentAsync(int articleId, KnowledgeAttachmentUploadRequest request, int? uploaderId = null);
    Task<bool> DeleteAttachmentAsync(int articleId, int attachmentId);
    /// <summary>下载附件文件流（调用方负责释放 Stream）</summary>
    Task<(Stream Stream, string MimeType, string FileName)?> GetAttachmentFileAsync(int articleId, int attachmentId);

    // 搜索和概览
    Task<KnowledgeSearchResult> SearchAsync(string keyword, int page = 1, int pageSize = 20);
    Task<KnowledgeOverviewResponse> GetOverviewAsync();

    // 文章垃圾箱
    Task<PagedResponse<KnowledgeArticleDto>> GetArticleTrashAsync(int page = 1, int pageSize = 10, string? keyword = null);
    Task<KnowledgeArticleDto?> RestoreArticleAsync(int id);
    Task HardDeleteArticleAsync(int id);
    Task<int> ClearArticleTrashAsync();

    // 分类垃圾箱
    Task<PagedResponse<KnowledgeCategoryDto>> GetCategoryTrashAsync(int page = 1, int pageSize = 10, string? keyword = null);
    Task<KnowledgeCategoryDto?> RestoreCategoryAsync(int id);
    Task HardDeleteCategoryAsync(int id);
    Task<int> ClearCategoryTrashAsync();
}
