using NoneadCSS.Application.DTOs.Requests;
using NoneadCSS.Application.DTOs.Responses;

namespace NoneadCSS.Application.Services;

public interface INotificationService
{
    // 通知模板管理
    Task<List<NotificationTemplateResponse>> GetTemplatesAsync();
    Task<NotificationTemplateResponse?> GetTemplateByIdAsync(int id);
    Task<NotificationTemplateResponse> CreateTemplateAsync(CreateNotificationTemplateRequest request);
    Task<NotificationTemplateResponse?> UpdateTemplateAsync(int id, UpdateNotificationTemplateRequest request);
    Task<bool> DeleteTemplateAsync(int id);
    Task<NotificationTemplateResponse?> ToggleTemplateActiveAsync(int id);
    Task<(List<NotificationTemplateResponse> Items, int TotalCount)> GetPagedTemplatesAsync(NotificationTemplateQuery query);
    Task<NotificationTemplateOverviewResponse> GetTemplateOverviewAsync();
    Task<NotificationTemplateResponse?> DuplicateTemplateAsync(int id, string? newName = null);
    TemplateVariableValidateResponse ValidateTemplateVariables(string subject, string body);
    Task<NotificationTemplatePreviewResponse?> PreviewTemplateAsync(int templateId, NotificationTemplatePreviewRequest request);

    // 通知模板垃圾箱
    Task<(List<NotificationTemplateResponse> Items, int TotalCount)> GetTemplateTrashAsync(int page, int pageSize, string? search);
    Task<int> GetTemplateTrashCountAsync();
    Task<bool> RestoreTemplateAsync(int id);
    Task<bool> HardDeleteTemplateAsync(int id);
    Task<int> ClearTemplateTrashAsync();

    // 通知规则管理
    Task<List<NotificationRuleResponse>> GetRulesAsync();
    Task<List<NotificationRuleResponse>> GetRulesByEventTypeAsync(string eventType);
    Task<NotificationRuleResponse?> GetRuleByIdAsync(int id);
    Task<NotificationRuleResponse> CreateRuleAsync(CreateNotificationRuleRequest request);
    Task<NotificationRuleResponse?> UpdateRuleAsync(int id, UpdateNotificationRuleRequest request, string? ifMatch = null);
    Task<bool> DeleteRuleAsync(int id);
    Task<NotificationRuleResponse?> ToggleRuleActiveAsync(int id);
    Task<(List<NotificationRuleResponse> Items, int TotalCount)> GetPagedRulesAsync(NotificationRuleQuery query);
    Task<NotificationRuleOverviewResponse> GetRuleOverviewAsync();
    Task<NotificationRuleResponse?> DuplicateRuleAsync(int id, string? newName = null);

    // 通知规则垃圾箱
    Task<(List<NotificationRuleResponse> Items, int TotalCount)> GetRuleTrashAsync(int page, int pageSize, string? search);
    Task<int> GetRuleTrashCountAsync();
    Task<bool> RestoreRuleAsync(int id);
    Task<bool> HardDeleteRuleAsync(int id);
    Task<int> ClearRuleTrashAsync();

    // 通知发送
    Task<NotificationSendResultResponse> SendNotificationAsync(SendNotificationRequest request);
    string RenderTemplate(string template, Dictionary<string, string> variables);

    // 扩展方法 - 对应 PHP 版本的 SendNotificationToUsers
    Task SendNotificationAsync(int userId, string title, string message, string? link = null);
    Task<List<int>> GetConversationFollowersAsync(int conversationId);
}
