using System.Net;
using System.Text.Json;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Options;
using NoneadCSS.Application.Common;
using NoneadCSS.Application.Configuration;
using NoneadCSS.Application.DTOs.Requests;
using NoneadCSS.Application.DTOs.Responses;
using NoneadCSS.Application.Templates;
using NoneadCSS.Domain;
using NoneadCSS.Domain.Entities;
using NoneadCSS.Domain.Enums;

namespace NoneadCSS.Application.Services;

public class MailTemplateService : IMailTemplateService
{
    private static readonly TemplateRenderer s_staticRenderer = new();
    private readonly IUnitOfWork _unitOfWork;
    private readonly ICurrentUserAccessor _currentUserAccessor;
    private readonly ITemplateRenderer _renderer;
    private readonly BrandOptions _brand;

    public MailTemplateService(
        IUnitOfWork unitOfWork,
        ICurrentUserAccessor currentUserAccessor,
        ITemplateRenderer renderer,
        IOptions<BrandOptions> brand,
        IConfiguration configuration)
    {
        _unitOfWork = unitOfWork;
        _currentUserAccessor = currentUserAccessor;
        _renderer = renderer;
        _brand = brand.Value;
    }

    public async Task<(IEnumerable<MailTemplateResponse> Items, int Total)> GetPagedAsync(MailTemplateQuery query)
    {
        var (items, total) = await _unitOfWork.MailTemplates.GetPagedAsync(
            query.Page, query.PageSize,
            query.Type, query.MailboxId, query.IsActive, query.Keyword);
        return (items.Select(MapToDto).ToList(), total);
    }

    public async Task<MailTemplateResponse?> GetByIdAsync(int id)
    {
        var template = await _unitOfWork.MailTemplates.GetByIdAsync(id);
        if (template == null || template.DeletedAt != null) return null;

        if (!await _currentUserAccessor.CanAccessMailboxAsync(template.MailboxId))
            return null;

        return MapToDto(template);
    }

    public async Task<MailTemplateResponse?> GetBySlugAsync(string slug)
    {
        var template = await _unitOfWork.MailTemplates.GetBySlugAsync(slug);
        if (template == null || template.DeletedAt != null) return null;
        return MapToDto(template);
    }

    public async Task<MailTemplateResponse> CreateAsync(CreateMailTemplateRequest request)
    {
        var existing = await _unitOfWork.MailTemplates.GetBySlugAsync(request.Slug);
        if (existing != null)
        {
            throw new InvalidOperationException("模板标识已存在");
        }

        var template = new MailTemplate
        {
            Name = request.Name,
            Slug = request.Slug,
            Type = request.Type,
            Subject = request.Subject,
            BodyHtml = HtmlSanitizerHelper.Sanitize(request.BodyHtml),
            BodyText = request.BodyText,
            VariablesJson = request.VariablesJson,
            IsActive = request.IsActive,
            IsSystem = false,
            MailboxId = request.MailboxId,
            Description = request.Description,
            SortOrder = request.SortOrder,
            CreatedAt = DateTime.UtcNow,
            UpdatedAt = DateTime.UtcNow
        };

        await _unitOfWork.MailTemplates.AddAsync(template);
        await _unitOfWork.SaveChangesAsync();

        return MapToDto(template);
    }

    public async Task<MailTemplateResponse?> UpdateAsync(int id, UpdateMailTemplateRequest request)
    {
        var template = await _unitOfWork.MailTemplates.GetQueryable(false)
            .FirstOrDefaultAsync(t => t.Id == id && t.DeletedAt == null);
        if (template == null)
        {
            return null;
        }

        if (!await _currentUserAccessor.CanAccessMailboxAsync(template.MailboxId))
            return null;

        if (template.IsSystem && template.Slug != request.Slug)
        {
            throw new InvalidOperationException("系统模板的标识不可修改");
        }

        if (template.Slug != request.Slug)
        {
            var existing = await _unitOfWork.MailTemplates.GetBySlugAsync(request.Slug);
            if (existing != null && existing.Id != id)
            {
                throw new InvalidOperationException("模板标识已存在");
            }
        }

        template.Name = request.Name;
        template.Slug = request.Slug;
        template.Type = request.Type;
        template.Subject = request.Subject;
        template.BodyHtml = HtmlSanitizerHelper.Sanitize(request.BodyHtml);
        template.BodyText = request.BodyText;
        template.VariablesJson = request.VariablesJson;
        template.IsActive = request.IsActive;
        if (!template.IsSystem)
        {
            template.MailboxId = request.MailboxId;
        }
        template.Description = request.Description;
        template.SortOrder = request.SortOrder;
        template.UpdatedAt = DateTime.UtcNow;

        await _unitOfWork.SaveChangesAsync();

        return MapToDto(template);
    }

    public async Task<bool> DeleteAsync(int id)
    {
        var template = await _unitOfWork.MailTemplates.GetQueryable(false)
            .FirstOrDefaultAsync(t => t.Id == id && t.DeletedAt == null);
        if (template == null)
        {
            return false;
        }

        if (!await _currentUserAccessor.CanAccessMailboxAsync(template.MailboxId))
            return false;

        if (template.IsSystem)
        {
            throw new InvalidOperationException("系统模板不可删除");
        }

        template.DeletedAt = DateTime.UtcNow;
        template.UpdatedAt = DateTime.UtcNow;
        await _unitOfWork.SaveChangesAsync();

        return true;
    }

    public async Task<MailTemplateOverviewResponse> GetOverviewAsync()
    {
        var stats = await _unitOfWork.MailTemplates.GetStatsAsync();

        var typeCounts = Enum.GetValues<MailTemplateType>()
            .Select(t => new MailTemplateTypeCount
            {
                Type = t,
                TypeName = GetTypeName(t),
                Count = stats.GetValueOrDefault($"type_{(int)t}", 0)
            })
            .ToList();

        return new MailTemplateOverviewResponse
        {
            Total = stats.GetValueOrDefault("total", 0),
            ActiveCount = stats.GetValueOrDefault("active", 0),
            SystemCount = stats.GetValueOrDefault("system", 0),
            CustomCount = stats.GetValueOrDefault("custom", 0),
            TypeCounts = typeCounts
        };
    }

    public Task<MailTemplatePreviewResponse> PreviewAsync(PreviewMailTemplateRequest request)
    {
        var subject = RenderVariables(request.Subject, request.Variables);
        var bodyHtml = RenderVariablesHtml(request.BodyHtml, request.Variables);
        var bodyText = RenderVariables(request.BodyText, request.Variables);

        return Task.FromResult(new MailTemplatePreviewResponse
        {
            Subject = subject,
            BodyHtml = bodyHtml,
            BodyText = bodyText
        });
    }

    public async Task<MailTemplatePreviewResponse?> PreviewTemplateAsync(int templateId, MailTemplatePreviewRequest request)
    {
        var template = await _unitOfWork.MailTemplates.GetByIdAsync(templateId);
        if (template == null || template.DeletedAt != null)
        {
            return null;
        }

        Dictionary<string, string> variables;
        if (request.CustomVariables != null && request.CustomVariables.Count > 0)
        {
            variables = request.CustomVariables;
        }
        else
        {
            variables = GetDefaultMockVariables(template.Type);
        }

        var subject = RenderVariables(template.Subject, variables);
        var bodyHtml = RenderVariablesHtml(template.BodyHtml, variables);
        var bodyText = RenderVariables(template.BodyText, variables);

        return new MailTemplatePreviewResponse
        {
            Subject = subject,
            BodyHtml = bodyHtml,
            BodyText = bodyText
        };
    }

    private Dictionary<string, string> GetDefaultMockVariables(MailTemplateType type)
    {
        // 启动时 BrandOptions 通过 ValidateOnStart 做强制校验，但如果用户直接改运行时配置或
        // 使用 OptionsMonitor 热重载清空值，这里再次强校验并抛出可读性更高的错误，
        // 避免出现 "/conversations/123" 这种错误的相对链接。
        if (string.IsNullOrWhiteSpace(_brand.FrontendUrl))
        {
            throw new InvalidOperationException(
                "配置 App:FrontendUrl 未设置，邮件模板预览链接无法生成。请在 appsettings 的 Brand 段设置 FrontendUrl，或通过环境变量 Brand__FrontendUrl / App__FrontendUrl 配置。");
        }
        var frontendUrl = _brand.FrontendUrl.TrimEnd('/');
        return type switch
        {
            MailTemplateType.NewConversation => new Dictionary<string, string>
            {
                ["conversation_id"] = "123",
                ["conversation_number"] = "CNV-20260725-001",
                ["subject"] = "产品使用问题咨询",
                ["customer_name"] = "张三",
                ["customer_email"] = "zhangsan@example.com",
                ["mailbox_name"] = "客服邮箱",
                ["assigned_user_name"] = "李客服",
                ["content"] = "您好，我在使用产品时遇到了问题...",
                ["conversation_url"] = $"{frontendUrl}/conversations/123",
                ["user_name"] = "李客服",
                ["user_email"] = "support@example.com",
                ["mailbox_email"] = "support@example.com",
                ["message_body"] = "您好，我在使用产品时遇到了问题...",
                ["created_at"] = "2026-07-25 10:00",
                ["company_name"] = "拓德科技",
                ["login_url"] = $"{frontendUrl}/login",
                ["reset_password_url"] = $"{frontendUrl}/reset-password",
                ["activation_url"] = $"{frontendUrl}/activate",
                ["received_time"] = "2026-07-25 10:00",
                ["assigned_to"] = "李客服",
                ["priority"] = "Normal",
                ["waiting_time"] = "5 分钟",
                ["timeout_minutes"] = "5",
                ["last_reply_time"] = "2026-07-25 10:00",
                ["escalation_reason"] = string.Empty,
                ["escalated_by"] = string.Empty,
                ["escalated_at"] = string.Empty,
                ["replier_name"] = "张三",
                ["assignee_name"] = "李客服",
                ["assigner_name"] = string.Empty,
                ["closer_name"] = string.Empty,
                ["updater_name"] = string.Empty,
                ["update_content"] = string.Empty,
                ["mentioner_name"] = string.Empty,
                ["rating_url"] = string.Empty,
                ["feedback_url"] = string.Empty,
                ["satisfaction_url"] = string.Empty,
                ["sla_response_time"] = string.Empty,
                ["user_role"] = "Agent",
                ["reset_url"] = $"{frontendUrl}/reset-password",
                ["expire_time"] = "24小时",
                ["total_count"] = string.Empty,
                ["success_count"] = string.Empty,
                ["failed_count"] = string.Empty,
                ["error_report_url"] = string.Empty
            },
            MailTemplateType.NewReply => new Dictionary<string, string>
            {
                ["conversation_id"] = "123",
                ["subject"] = "产品使用问题咨询",
                ["customer_name"] = "张三",
                ["reply_content"] = "感谢您的回复，问题已解决！",
                ["reply_from"] = "张三",
                ["conversation_url"] = $"{frontendUrl}/conversations/123",
                ["user_name"] = "李客服",
                ["user_email"] = "support@example.com",
                ["mailbox_name"] = "客服邮箱",
                ["mailbox_email"] = "support@example.com",
                ["message_body"] = "感谢您的回复，问题已解决！",
                ["created_at"] = "2026-07-25 10:00",
                ["company_name"] = "拓德科技",
                ["login_url"] = $"{frontendUrl}/login",
                ["reset_password_url"] = $"{frontendUrl}/reset-password",
                ["activation_url"] = $"{frontendUrl}/activate",
                ["received_time"] = "2026-07-25 10:00",
                ["assigned_to"] = "李客服",
                ["priority"] = "Normal",
                ["waiting_time"] = "5 分钟",
                ["timeout_minutes"] = "5",
                ["last_reply_time"] = "2026-07-25 10:30",
                ["escalation_reason"] = string.Empty,
                ["escalated_by"] = string.Empty,
                ["escalated_at"] = string.Empty,
                ["replier_name"] = "张三",
                ["assignee_name"] = "李客服",
                ["assigner_name"] = string.Empty,
                ["closer_name"] = string.Empty,
                ["updater_name"] = string.Empty,
                ["update_content"] = string.Empty,
                ["mentioner_name"] = string.Empty,
                ["rating_url"] = string.Empty,
                ["feedback_url"] = string.Empty,
                ["satisfaction_url"] = string.Empty,
                ["sla_response_time"] = string.Empty,
                ["user_role"] = "Agent",
                ["reset_url"] = $"{frontendUrl}/reset-password",
                ["expire_time"] = "24小时",
                ["total_count"] = string.Empty,
                ["success_count"] = string.Empty,
                ["failed_count"] = string.Empty,
                ["error_report_url"] = string.Empty
            },
            MailTemplateType.ConversationAssigned => new Dictionary<string, string>
            {
                ["conversation_id"] = "123",
                ["subject"] = "产品使用问题咨询",
                ["assigned_user_name"] = "李客服",
                ["assigner_name"] = "王主管",
                ["conversation_url"] = $"{frontendUrl}/conversations/123",
                ["customer_name"] = "张三",
                ["customer_email"] = "zhangsan@example.com",
                ["user_name"] = "李客服",
                ["user_email"] = "support@example.com",
                ["mailbox_name"] = "客服邮箱",
                ["mailbox_email"] = "support@example.com",
                ["message_body"] = "您好，我在使用产品时遇到了问题...",
                ["created_at"] = "2026-07-25 10:00",
                ["company_name"] = "拓德科技",
                ["login_url"] = $"{frontendUrl}/login",
                ["reset_password_url"] = $"{frontendUrl}/reset-password",
                ["activation_url"] = $"{frontendUrl}/activate",
                ["received_time"] = "2026-07-25 10:00",
                ["assigned_to"] = "李客服",
                ["priority"] = "Normal",
                ["waiting_time"] = "5 分钟",
                ["timeout_minutes"] = "5",
                ["last_reply_time"] = "2026-07-25 10:00",
                ["escalation_reason"] = string.Empty,
                ["escalated_by"] = string.Empty,
                ["escalated_at"] = string.Empty,
                ["replier_name"] = string.Empty,
                ["assignee_name"] = "李客服",
                ["assigner_name"] = "王主管",
                ["closer_name"] = string.Empty,
                ["updater_name"] = string.Empty,
                ["update_content"] = string.Empty,
                ["mentioner_name"] = string.Empty,
                ["rating_url"] = string.Empty,
                ["feedback_url"] = string.Empty,
                ["satisfaction_url"] = string.Empty,
                ["sla_response_time"] = string.Empty,
                ["user_role"] = "Agent",
                ["reset_url"] = $"{frontendUrl}/reset-password",
                ["expire_time"] = "24小时",
                ["total_count"] = string.Empty,
                ["success_count"] = string.Empty,
                ["failed_count"] = string.Empty,
                ["error_report_url"] = string.Empty
            },
            MailTemplateType.ConversationClosed => new Dictionary<string, string>
            {
                ["conversation_id"] = "123",
                ["subject"] = "产品使用问题咨询",
                ["customer_name"] = "张三",
                ["closed_by"] = "李客服",
                ["satisfaction_url"] = $"{frontendUrl}/satisfaction/xxx",
                ["conversation_url"] = $"{frontendUrl}/conversations/123",
                ["user_name"] = "李客服",
                ["user_email"] = "support@example.com",
                ["mailbox_name"] = "客服邮箱",
                ["mailbox_email"] = "support@example.com",
                ["message_body"] = "您好，我在使用产品时遇到了问题...",
                ["created_at"] = "2026-07-25 10:00",
                ["company_name"] = "拓德科技",
                ["login_url"] = $"{frontendUrl}/login",
                ["reset_password_url"] = $"{frontendUrl}/reset-password",
                ["activation_url"] = $"{frontendUrl}/activate",
                ["received_time"] = "2026-07-25 10:00",
                ["assigned_to"] = "李客服",
                ["priority"] = "Normal",
                ["waiting_time"] = "30 分钟",
                ["timeout_minutes"] = "30",
                ["last_reply_time"] = "2026-07-25 10:30",
                ["escalation_reason"] = string.Empty,
                ["escalated_by"] = string.Empty,
                ["escalated_at"] = string.Empty,
                ["replier_name"] = "李客服",
                ["assignee_name"] = "李客服",
                ["assigner_name"] = string.Empty,
                ["closer_name"] = "李客服",
                ["updater_name"] = string.Empty,
                ["update_content"] = string.Empty,
                ["mentioner_name"] = string.Empty,
                ["rating_url"] = $"{frontendUrl}/satisfaction/xxx",
                ["feedback_url"] = string.Empty,
                ["satisfaction_url"] = $"{frontendUrl}/satisfaction/xxx",
                ["sla_response_time"] = string.Empty,
                ["user_role"] = "Agent",
                ["reset_url"] = $"{frontendUrl}/reset-password",
                ["expire_time"] = "24小时",
                ["total_count"] = string.Empty,
                ["success_count"] = string.Empty,
                ["failed_count"] = string.Empty,
                ["error_report_url"] = string.Empty
            },
            MailTemplateType.SatisfactionSurvey => new Dictionary<string, string>
            {
                ["conversation_id"] = "123",
                ["subject"] = "产品使用问题咨询",
                ["customer_name"] = "张三",
                ["rating_url"] = $"{frontendUrl}/satisfaction/xxx",
                ["conversation_url"] = $"{frontendUrl}/conversations/123",
                ["user_name"] = "李客服",
                ["user_email"] = "support@example.com",
                ["mailbox_name"] = "客服邮箱",
                ["mailbox_email"] = "support@example.com",
                ["message_body"] = "感谢您使用我们的服务，请对本次服务进行评价。",
                ["created_at"] = "2026-07-25 10:00",
                ["company_name"] = "拓德科技",
                ["login_url"] = $"{frontendUrl}/login",
                ["reset_password_url"] = $"{frontendUrl}/reset-password",
                ["activation_url"] = $"{frontendUrl}/activate",
                ["received_time"] = "2026-07-25 10:00",
                ["assigned_to"] = "李客服",
                ["priority"] = "Normal",
                ["waiting_time"] = string.Empty,
                ["timeout_minutes"] = string.Empty,
                ["last_reply_time"] = "2026-07-25 10:30",
                ["escalation_reason"] = string.Empty,
                ["escalated_by"] = string.Empty,
                ["escalated_at"] = string.Empty,
                ["replier_name"] = string.Empty,
                ["assignee_name"] = "李客服",
                ["assigner_name"] = string.Empty,
                ["closer_name"] = "李客服",
                ["updater_name"] = string.Empty,
                ["update_content"] = string.Empty,
                ["mentioner_name"] = string.Empty,
                ["rating_url"] = $"{frontendUrl}/satisfaction/xxx",
                ["feedback_url"] = string.Empty,
                ["satisfaction_url"] = $"{frontendUrl}/satisfaction/xxx",
                ["sla_response_time"] = string.Empty,
                ["user_role"] = "Agent",
                ["reset_url"] = $"{frontendUrl}/reset-password",
                ["expire_time"] = "24小时",
                ["total_count"] = string.Empty,
                ["success_count"] = string.Empty,
                ["failed_count"] = string.Empty,
                ["error_report_url"] = string.Empty
            },
            MailTemplateType.PasswordReset => new Dictionary<string, string>
            {
                ["user_name"] = "张三",
                ["user_email"] = "zhangsan@example.com",
                ["reset_url"] = $"{frontendUrl}/reset-password?token=xxx",
                ["reset_password_url"] = $"{frontendUrl}/reset-password?token=xxx",
                ["company_name"] = "拓德科技",
                ["login_url"] = $"{frontendUrl}/login",
                ["expire_time"] = "24小时",
                ["created_at"] = "2026-07-25 10:00"
            },
            MailTemplateType.Welcome => new Dictionary<string, string>
            {
                ["user_name"] = "张三",
                ["user_email"] = "zhangsan@example.com",
                ["login_url"] = $"{frontendUrl}/login",
                ["company_name"] = "拓德科技",
                ["activation_url"] = $"{frontendUrl}/activate?token=xxx",
                ["created_at"] = "2026-07-25 10:00"
            },
            _ => new Dictionary<string, string>
            {
                ["conversation_id"] = "123",
                ["conversation_number"] = "CNV-20260725-001",
                ["subject"] = "示例会话标题",
                ["customer_name"] = "张三",
                ["customer_email"] = "zhangsan@example.com",
                ["user_name"] = "李客服",
                ["user_email"] = "support@example.com",
                ["mailbox_name"] = "客服邮箱",
                ["mailbox_email"] = "support@example.com",
                ["message_body"] = "您好，我在使用产品时遇到了问题...",
                ["content"] = "您好，我在使用产品时遇到了问题...",
                ["created_at"] = "2026-07-25 10:00",
                ["company_name"] = "拓德科技",
                ["conversation_url"] = $"{frontendUrl}/conversations/123",
                ["login_url"] = $"{frontendUrl}/login",
                ["reset_password_url"] = $"{frontendUrl}/reset-password",
                ["activation_url"] = $"{frontendUrl}/activate",
                ["assigned_user_name"] = "李客服",
                ["assignee_name"] = "李客服",
                ["satisfaction_url"] = $"{frontendUrl}/satisfaction/xxx",
                ["rating_url"] = $"{frontendUrl}/satisfaction/xxx",
                ["reset_url"] = $"{frontendUrl}/reset-password",
                ["expire_time"] = "24小时"
            }
        };
    }

    public async Task<string> RenderTemplateAsync(int templateId, Dictionary<string, string> variables, HashSet<string>? rawHtmlKeys = null)
    {
        var template = await _unitOfWork.MailTemplates.GetByIdAsync(templateId);
        if (template == null || template.DeletedAt != null)
        {
            throw new InvalidOperationException("模板不存在");
        }

        return RenderVariablesHtml(template.BodyHtml, variables, rawHtmlKeys);
    }

    public async Task<RenderedTemplateResponse> RenderTemplateFullAsync(int templateId, Dictionary<string, string> variables, HashSet<string>? rawHtmlKeys = null)
    {
        var template = await _unitOfWork.MailTemplates.GetByIdAsync(templateId);
        if (template == null || template.DeletedAt != null)
        {
            throw new InvalidOperationException("模板不存在");
        }

        return new RenderedTemplateResponse
        {
            Subject = RenderVariables(template.Subject, variables),
            BodyHtml = RenderVariablesHtml(template.BodyHtml, variables, rawHtmlKeys),
            BodyText = RenderVariables(template.BodyText, variables)
        };
    }

    public Task<List<MailTemplateVariable>> GetBuiltInVariablesAsync()
    {
        var variables = TemplateVariableRegistry.GetAll()
            .Select(v => new MailTemplateVariable
            {
                Key = v.Canonical,
                Label = v.Label,
                Category = v.Category
            })
            .ToList();

        return Task.FromResult(variables);
    }

    private string RenderVariables(string template, Dictionary<string, string> variables)
    {
        return _renderer.Render(template, variables, new RenderOptions { HtmlEncode = false });
    }

    /// <summary>
    /// 静态版模版变量渲染（供 CalendarService 等外部调用方使用）
    /// </summary>
    public static string RenderVariablesStatic(string template, Dictionary<string, string> variables)
    {
        return s_staticRenderer.Render(template, variables, new RenderOptions { HtmlEncode = false });
    }

    /// <summary>
    /// 静态版 HTML 模版变量渲染（含 HTML 消毒）
    /// </summary>
    public static string RenderVariablesHtmlStatic(string template, Dictionary<string, string> variables)
    {
        return s_staticRenderer.Render(template, variables, new RenderOptions { HtmlEncode = true });
    }

    /// <summary>
    /// 渲染模板变量到 HTML 上下文 - 委托给统一渲染引擎
    /// </summary>
    private string RenderVariablesHtml(string template, Dictionary<string, string> variables, HashSet<string>? rawHtmlKeys = null)
    {
        return _renderer.Render(template, variables, new RenderOptions { HtmlEncode = true, RawHtmlKeys = rawHtmlKeys });
    }

    private MailTemplateResponse MapToDto(MailTemplate template)
    {
        var variables = new List<MailTemplateVariable>();
        if (!string.IsNullOrEmpty(template.VariablesJson))
        {
            try
            {
                variables = JsonSerializer.Deserialize<List<MailTemplateVariable>>(template.VariablesJson) ?? [];
            }
            catch
            {
                variables = [];
            }
        }

        return new MailTemplateResponse
        {
            Id = template.Id,
            Name = template.Name,
            Slug = template.Slug,
            Type = template.Type,
            TypeName = GetTypeName(template.Type),
            Subject = template.Subject,
            BodyHtml = template.BodyHtml,
            BodyText = template.BodyText,
            VariablesJson = template.VariablesJson,
            Variables = variables,
            IsActive = template.IsActive,
            IsSystem = template.IsSystem,
            MailboxId = template.MailboxId,
            MailboxName = template.Mailbox?.Name,
            Description = template.Description,
            SortOrder = template.SortOrder,
            CreatedAt = template.CreatedAt,
            UpdatedAt = template.UpdatedAt,
            DeletedAt = template.DeletedAt
        };
    }

    private string GetTypeName(MailTemplateType type)
    {
        return type switch
        {
            MailTemplateType.NewConversation => "新会话通知",
            MailTemplateType.NewReply => "新回复通知",
            MailTemplateType.ConversationAssigned => "会话分配通知",
            MailTemplateType.ConversationClosed => "会话关闭通知",
            MailTemplateType.SatisfactionSurvey => "满意度调查",
            MailTemplateType.PasswordReset => "密码重置",
            MailTemplateType.Welcome => "欢迎邮件",
            MailTemplateType.Custom => "自定义模板",
            _ => type.ToString()
        };
    }

    public async Task<(IEnumerable<MailTemplateResponse> Items, int Total)> GetTrashAsync(int page, int pageSize, string? keyword = null)
    {
        var (items, total) = await _unitOfWork.MailTemplates.GetDeletedAsync(page, pageSize, keyword);
        return (items.Select(MapToDto).ToList(), total);
    }

    public async Task<int> GetTrashCountAsync()
    {
        return await _unitOfWork.MailTemplates.CountDeletedAsync();
    }

    public async Task<bool> RestoreAsync(int id)
    {
        return await _unitOfWork.MailTemplates.RestoreAsync(id);
    }

    public async Task<bool> HardDeleteAsync(int id)
    {
        if (_currentUserAccessor != null && !_currentUserAccessor.IsSuperAdmin)
        {
            return false;
        }
        return await _unitOfWork.MailTemplates.HardDeleteAsync(id);
    }

    public async Task<int> ClearTrashAsync()
    {
        return await _unitOfWork.MailTemplates.HardDeleteAllDeletedAsync();
    }
}
