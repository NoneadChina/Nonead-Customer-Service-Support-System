using NoneadCSS.Application.DTOs.Requests;
using NoneadCSS.Application.DTOs.Responses;
using NoneadCSS.Domain.Enums;

namespace NoneadCSS.Application.Services;

public interface IMailTemplateService
{
    Task<(IEnumerable<MailTemplateResponse> Items, int Total)> GetPagedAsync(MailTemplateQuery query);
    Task<MailTemplateResponse?> GetByIdAsync(int id);
    Task<MailTemplateResponse?> GetBySlugAsync(string slug);
    Task<MailTemplateResponse> CreateAsync(CreateMailTemplateRequest request);
    Task<MailTemplateResponse?> UpdateAsync(int id, UpdateMailTemplateRequest request);
    Task<bool> DeleteAsync(int id);
    Task<MailTemplateOverviewResponse> GetOverviewAsync();
    Task<MailTemplatePreviewResponse> PreviewAsync(PreviewMailTemplateRequest request);
    Task<MailTemplatePreviewResponse?> PreviewTemplateAsync(int templateId, MailTemplatePreviewRequest request);
    Task<string> RenderTemplateAsync(int templateId, Dictionary<string, string> variables, HashSet<string>? rawHtmlKeys = null);
    Task<RenderedTemplateResponse> RenderTemplateFullAsync(int templateId, Dictionary<string, string> variables, HashSet<string>? rawHtmlKeys = null);
    Task<List<MailTemplateVariable>> GetBuiltInVariablesAsync();
    Task<(IEnumerable<MailTemplateResponse> Items, int Total)> GetTrashAsync(int page, int pageSize, string? keyword = null);
    Task<int> GetTrashCountAsync();
    Task<bool> RestoreAsync(int id);
    Task<bool> HardDeleteAsync(int id);
    Task<int> ClearTrashAsync();
}
