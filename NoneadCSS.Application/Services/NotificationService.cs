using System.Net;
using System.Text.RegularExpressions;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Options;
using NoneadCSS.Application.Common;
using NoneadCSS.Application.Configuration;
using NoneadCSS.Application.DTOs.Requests;
using NoneadCSS.Application.DTOs.Responses;
using NoneadCSS.Application.Templates;
using NoneadCSS.Domain;
using NoneadCSS.Domain.Entities;
using NoneadCSS.Domain.Enums;
using NoneadCSS.Domain.Events;
using Microsoft.Extensions.Logging;

namespace NoneadCSS.Application.Services;

public class NotificationService : INotificationService
{
    private readonly IUnitOfWork _unitOfWork;
    private readonly IEmailService _emailService;
    private readonly IEmailQueueService _emailQueueService;
    private readonly INotificationPublisher _notificationPublisher;
    private readonly ILogger<NotificationService> _logger;
    private readonly ITemplateRenderer _renderer;
    private readonly BrandOptions _brand;

    public NotificationService(IUnitOfWork unitOfWork, IEmailService emailService,
        IEmailQueueService emailQueueService, INotificationPublisher notificationPublisher, ILogger<NotificationService> logger,
        ITemplateRenderer renderer, IOptions<BrandOptions> brand)
    {
        _unitOfWork = unitOfWork;
        _emailService = emailService;
        _emailQueueService = emailQueueService;
        _notificationPublisher = notificationPublisher;
        _logger = logger;
        _renderer = renderer;
        _brand = brand.Value;
    }

    // 通知模板管理
    public async Task<List<NotificationTemplateResponse>> GetTemplatesAsync()
    {
        var templates = await _unitOfWork.NotificationTemplates.GetAllAsync();
        return templates.Select(ToTemplateResponse).ToList();
    }

    public async Task<NotificationTemplateResponse?> GetTemplateByIdAsync(int id)
    {
        var template = await _unitOfWork.NotificationTemplates.GetByIdAsync(id);
        return template != null ? ToTemplateResponse(template) : null;
    }

    public async Task<NotificationTemplateResponse> CreateTemplateAsync(CreateNotificationTemplateRequest request)
    {
        if (await _unitOfWork.NotificationTemplates.IsNameExistsAsync(request.Name))
        {
            throw new InvalidOperationException($"模板名称 \"{request.Name}\" 已存在");
        }

        var template = new NotificationTemplate
        {
            Name = request.Name,
            Subject = request.Subject,
            // 对模板内容进行 HTML 消毒，防止存储 XSS
            Body = HtmlSanitizerHelper.Sanitize(request.Body),
            EventType = request.EventType,
            IsActive = request.IsActive
        };

        await _unitOfWork.NotificationTemplates.AddAsync(template);
        await _unitOfWork.SaveChangesAsync();

        return ToTemplateResponse(template);
    }

    public async Task<NotificationTemplateResponse?> UpdateTemplateAsync(int id, UpdateNotificationTemplateRequest request)
    {
        var template = await _unitOfWork.NotificationTemplates.GetQueryable(false)
            .FirstOrDefaultAsync(t => t.Id == id && t.DeletedAt == null);
        if (template == null)
        {
            return null;
        }

        if (await _unitOfWork.NotificationTemplates.IsNameExistsAsync(request.Name, id))
        {
            throw new InvalidOperationException($"模板名称 \"{request.Name}\" 已存在");
        }

        template.Name = request.Name;
        template.Subject = request.Subject;
        // 对模板内容进行 HTML 消毒，防止存储 XSS
        template.Body = HtmlSanitizerHelper.Sanitize(request.Body);
        template.EventType = request.EventType;
        template.IsActive = request.IsActive;
        template.UpdatedAt = DateTime.UtcNow;

        await _unitOfWork.SaveChangesAsync();

        return ToTemplateResponse(template);
    }

    public async Task<bool> DeleteTemplateAsync(int id)
    {
        var template = await _unitOfWork.NotificationTemplates.GetByIdAsync(id);
        if (template == null)
        {
            return false;
        }

        await _unitOfWork.NotificationTemplates.SoftDeleteAsync(id);
        await _unitOfWork.SaveChangesAsync();
        return true;
    }

    public async Task<NotificationTemplateResponse?> ToggleTemplateActiveAsync(int id)
    {
        var template = await _unitOfWork.NotificationTemplates.GetQueryable(false)
            .FirstOrDefaultAsync(t => t.Id == id && t.DeletedAt == null);
        if (template == null)
        {
            return null;
        }

        template.IsActive = !template.IsActive;
        template.UpdatedAt = DateTime.UtcNow;

        await _unitOfWork.SaveChangesAsync();

        return ToTemplateResponse(template);
    }

    public async Task<(List<NotificationTemplateResponse> Items, int TotalCount)> GetPagedTemplatesAsync(NotificationTemplateQuery query)
    {
        var (items, total) = await _unitOfWork.NotificationTemplates.GetPagedAsync(
            query.EventType, query.IsActive, query.Search, query.Page, query.PageSize);
        return (items.Select(ToTemplateResponse).ToList(), total);
    }

    public async Task<NotificationTemplateOverviewResponse> GetTemplateOverviewAsync()
    {
        var query = _unitOfWork.NotificationTemplates.GetQueryable();

        var total = await query.CountAsync();
        var active = await query.CountAsync(t => t.IsActive);

        var events = await query
            .GroupBy(t => t.EventType)
            .Select(g => new NotificationTemplateEventStats
            {
                EventType = g.Key,
                EventTypeLabel = GetEventTypeLabel(g.Key),
                Count = g.Count(),
                ActiveCount = g.Count(t => t.IsActive)
            })
            .OrderBy(e => e.EventType)
            .ToListAsync();

        return new NotificationTemplateOverviewResponse
        {
            Total = total,
            Active = active,
            Inactive = total - active,
            Events = events
        };
    }

    // ====== 通知模板垃圾箱 ======
    public async Task<(List<NotificationTemplateResponse> Items, int TotalCount)> GetTemplateTrashAsync(int page, int pageSize, string? search)
    {
        var (items, total) = await _unitOfWork.NotificationTemplates.GetDeletedPagedAsync(search, page, pageSize);
        return (items.Select(ToTemplateResponse).ToList(), total);
    }

    public async Task<int> GetTemplateTrashCountAsync()
    {
        return await _unitOfWork.NotificationTemplates.CountDeletedAsync();
    }

    public async Task<bool> RestoreTemplateAsync(int id)
    {
        // 先检查是否存在已删除的模板，避免对不存在的记录执行 ExecuteUpdate
        var deleted = await _unitOfWork.NotificationTemplates.GetQueryable(false)
            .IgnoreQueryFilters()
            .FirstOrDefaultAsync(t => t.Id == id && t.DeletedAt != null);
        if (deleted == null) return false;

        // 先恢复，再检查名称冲突
        await _unitOfWork.NotificationTemplates.RestoreAsync(id);

        // 使用追踪查询获取实体，确保名称修改能持久化到数据库
        var restored = await _unitOfWork.NotificationTemplates.GetQueryable(false)
            .FirstOrDefaultAsync(t => t.Id == id);
        if (restored == null) return false;

        // 检查名称冲突（同名且未删除的模板）
        if (await _unitOfWork.NotificationTemplates.IsNameExistsAsync(restored.Name, id))
        {
            restored.Name = $"{restored.Name}（恢复_{DateTime.UtcNow:yyyyMMddHHmmss}）";
        }

        await _unitOfWork.SaveChangesAsync();
        return true;
    }

    public async Task<bool> HardDeleteTemplateAsync(int id)
    {
        // 先检查是否存在已删除的模板，避免对不存在的记录执行 ExecuteDelete
        var deleted = await _unitOfWork.NotificationTemplates.GetQueryable(false)
            .IgnoreQueryFilters()
            .FirstOrDefaultAsync(t => t.Id == id && t.DeletedAt != null);
        if (deleted == null) return false;

        await _unitOfWork.NotificationTemplates.HardDeleteAsync(id);
        await _unitOfWork.SaveChangesAsync();
        return true;
    }

    public async Task<int> ClearTemplateTrashAsync()
    {
        var count = await _unitOfWork.NotificationTemplates.HardDeleteAllDeletedAsync();
        if (count > 0)
        {
            await _unitOfWork.SaveChangesAsync();
        }
        return count;
    }

    public async Task<NotificationTemplateResponse?> DuplicateTemplateAsync(int id, string? newName = null)
    {
        var template = await _unitOfWork.NotificationTemplates.GetByIdAsync(id);
        if (template == null) return null;

        var name = newName ?? $"{template.Name} - 副本";
        var suffix = 1;
        while (await _unitOfWork.NotificationTemplates.IsNameExistsAsync(name))
        {
            name = $"{newName ?? template.Name} - 副本 {suffix++}";
        }

        var duplicate = new NotificationTemplate
        {
            Name = name,
            Subject = template.Subject,
            Body = template.Body,
            EventType = template.EventType,
            IsActive = false,
        };

        await _unitOfWork.NotificationTemplates.AddAsync(duplicate);
        await _unitOfWork.SaveChangesAsync();

        return ToTemplateResponse(duplicate);
    }

    public TemplateVariableValidateResponse ValidateTemplateVariables(string subject, string body)
    {
        var subjectVars = ExtractVariables(subject);
        var bodyVars = ExtractVariables(body);
        var allVars = subjectVars.Union(bodyVars).OrderBy(v => v).ToList();
        var commonVars = allVars.Where(v => CommonVariableSet.Contains(v)).ToList();
        var uncommonVars = allVars.Where(v => !CommonVariableSet.Contains(v)).ToList();

        return new TemplateVariableValidateResponse
        {
            SubjectVariables = subjectVars,
            BodyVariables = bodyVars,
            AllVariables = allVars,
            CommonVariables = commonVars,
            UncommonVariables = uncommonVars,
        };
    }

    public async Task<NotificationTemplatePreviewResponse?> PreviewTemplateAsync(int templateId, NotificationTemplatePreviewRequest request)
    {
        var template = await _unitOfWork.NotificationTemplates.GetByIdAsync(templateId);
        if (template == null)
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
            variables = GetDefaultMockVariables(template.EventType);
        }

        var subject = RenderTemplate(template.Subject, variables);
        var body = RenderTemplate(template.Body, variables);

        return new NotificationTemplatePreviewResponse
        {
            Subject = subject,
            Body = body
        };
    }

    private Dictionary<string, string> GetDefaultMockVariables(string eventType)
    {
        var url = _brand.FrontendUrl?.TrimEnd('/') ?? "http://localhost:5173";
        return eventType switch
        {
            "new_conversation" => new Dictionary<string, string>
            {
                ["conversation_id"] = "123",
                ["subject"] = "产品使用问题咨询",
                ["customer_name"] = "张三",
                ["mailbox_name"] = "客服邮箱",
                ["conversation_url"] = $"{url}/conversations/123",
                ["company_name"] = "拓德科技"
            },
            "conversation_assigned" => new Dictionary<string, string>
            {
                ["conversation_id"] = "123",
                ["subject"] = "产品使用问题咨询",
                ["assignee_name"] = "李客服",
                ["assigner_name"] = "王主管",
                ["customer_name"] = "张三",
                ["mailbox_name"] = "客服邮箱",
                ["conversation_url"] = $"{url}/conversations/123",
                ["company_name"] = "拓德科技"
            },
            "conversation_closed" => new Dictionary<string, string>
            {
                ["conversation_id"] = "123",
                ["subject"] = "产品使用问题咨询",
                ["customer_name"] = "张三",
                ["closer_name"] = "李客服",
                ["mailbox_name"] = "客服邮箱",
                ["conversation_url"] = $"{url}/conversations/123",
                ["company_name"] = "拓德科技"
            },
            "new_reply" => new Dictionary<string, string>
            {
                ["conversation_id"] = "123",
                ["subject"] = "产品使用问题咨询",
                ["customer_name"] = "张三",
                ["replier_name"] = "张三",
                ["mailbox_name"] = "客服邮箱",
                ["conversation_url"] = $"{url}/conversations/123",
                ["company_name"] = "拓德科技"
            },
            "sla_warning" => new Dictionary<string, string>
            {
                ["conversation_id"] = "123",
                ["subject"] = "产品使用问题咨询",
                ["customer_name"] = "张三",
                ["sla_name"] = "标准SLA",
                ["sla_level"] = "Normal",
                ["sla_deadline"] = "2026-08-12 18:00",
                ["remaining_time"] = "2小时30分钟",
                ["mailbox_name"] = "客服邮箱",
                ["conversation_url"] = $"{url}/conversations/123",
                ["company_name"] = "拓德科技"
            },
            "sla_violation" => new Dictionary<string, string>
            {
                ["conversation_id"] = "123",
                ["subject"] = "产品使用问题咨询",
                ["sla_name"] = "标准SLA",
                ["violation_type"] = "首次响应超时",
                ["deadline"] = "2026-08-12 16:00",
                ["mailbox_name"] = "客服邮箱",
                ["conversation_url"] = $"{url}/conversations/123",
                ["company_name"] = "拓德科技"
            },
            "conversation_updated" => new Dictionary<string, string>
            {
                ["conversation_id"] = "123",
                ["subject"] = "产品使用问题咨询",
                ["customer_name"] = "张三",
                ["updater_name"] = "李客服",
                ["update_content"] = "已更新会话状态和标签",
                ["mailbox_name"] = "客服邮箱",
                ["conversation_url"] = $"{url}/conversations/123",
                ["company_name"] = "拓德科技"
            },
            "mention" => new Dictionary<string, string>
            {
                ["conversation_id"] = "123",
                ["subject"] = "产品使用问题咨询",
                ["user_name"] = "李客服",
                ["mentioner_name"] = "王主管",
                ["mailbox_name"] = "客服邮箱",
                ["conversation_url"] = $"{url}/conversations/123",
                ["company_name"] = "拓德科技"
            },
            "customer_reply" => new Dictionary<string, string>
            {
                ["conversation_id"] = "123",
                ["subject"] = "产品使用问题咨询",
                ["customer_name"] = "张三",
                ["mailbox_name"] = "客服邮箱",
                ["conversation_url"] = $"{url}/conversations/123",
                ["company_name"] = "拓德科技"
            },
            "conversation_reopened" => new Dictionary<string, string>
            {
                ["conversation_id"] = "123",
                ["subject"] = "产品使用问题咨询",
                ["reopened_by"] = "张三",
                ["reopen_reason"] = "问题再次出现",
                ["conversation_url"] = $"{url}/conversations/123"
            },
            "conversation_transferred" => new Dictionary<string, string>
            {
                ["conversation_id"] = "123",
                ["subject"] = "产品使用问题咨询",
                ["transferred_from"] = "客服A组",
                ["transferred_to"] = "技术支持组",
                ["transfer_reason"] = "需要技术人员协助",
                ["conversation_url"] = $"{url}/conversations/123"
            },
            "satisfaction_submitted" => new Dictionary<string, string>
            {
                ["conversation_id"] = "123",
                ["subject"] = "产品使用问题咨询",
                ["customer_name"] = "张三",
                ["rating"] = "5",
                ["rating_label"] = "远超期望",
                ["satisfaction_comment"] = "服务非常好，问题很快得到了解决",
                ["conversation_url"] = $"{url}/conversations/123"
            },
            "ticket_escalated" => new Dictionary<string, string>
            {
                ["conversation_id"] = "123",
                ["subject"] = "严重系统故障",
                ["customer_name"] = "张三",
                ["priority"] = "紧急",
                ["escalated_by"] = "李客服",
                ["escalation_reason"] = "客户问题严重，需要升级处理",
                ["escalated_at"] = "2026-08-12 14:30",
                ["conversation_url"] = $"{url}/conversations/123",
                ["company_name"] = "拓德科技"
            },
            "conversation_timeout" => new Dictionary<string, string>
            {
                ["conversation_id"] = "123",
                ["subject"] = "产品使用问题咨询",
                ["customer_name"] = "张三",
                ["assigned_to"] = "李客服",
                ["last_reply_time"] = "2026-08-12 10:00",
                ["waiting_time"] = "45 分钟",
                ["timeout_minutes"] = "60",
                ["conversation_url"] = $"{url}/conversations/123",
                ["company_name"] = "拓德科技"
            },
            "customer_satisfaction" => new Dictionary<string, string>
            {
                ["conversation_id"] = "123",
                ["customer_name"] = "张三",
                ["rating_url"] = $"{url}/satisfaction/rate/123",
                ["feedback_url"] = $"{url}/satisfaction/feedback/123",
                ["company_name"] = "拓德科技"
            },
            "auto_reply" => new Dictionary<string, string>
            {
                ["conversation_id"] = "123",
                ["subject"] = "产品使用问题咨询",
                ["customer_name"] = "张三",
                ["received_time"] = "2026-08-12 14:00",
                ["sla_response_time"] = "2 小时",
                ["company_name"] = "拓德科技"
            },
            "user_welcome" => new Dictionary<string, string>
            {
                ["user_name"] = "张三",
                ["user_email"] = "zhangsan@example.com",
                ["user_role"] = "客服",
                ["login_url"] = $"{url}/login",
                ["company_name"] = "拓德科技"
            },
            "password_reset" => new Dictionary<string, string>
            {
                ["user_name"] = "张三",
                ["reset_url"] = $"{url}/reset-password?token=xxx",
                ["expire_time"] = "24小时",
                ["company_name"] = "拓德科技"
            },
            "bulk_import_completed" => new Dictionary<string, string>
            {
                ["total_count"] = "100",
                ["success_count"] = "95",
                ["failed_count"] = "5",
                ["error_report_url"] = $"{url}/import/results/123",
                ["user_name"] = "张三",
                ["company_name"] = "拓德科技"
            },
            "daily_report" => new Dictionary<string, string>
            {
                ["report_date"] = "2026-07-25",
                ["new_conversations"] = "42",
                ["closed_conversations"] = "38",
                ["average_response_time"] = "15分钟",
                ["satisfaction_rate"] = "96.5%"
            },
            "account_locked" => new Dictionary<string, string>
            {
                ["user_name"] = "张三",
                ["user_email"] = "zhangsan@example.com",
                ["lock_reason"] = "连续登录失败次数过多",
                ["unlock_time"] = "30分钟后"
            },
            "system_maintenance" => new Dictionary<string, string>
            {
                ["maintenance_title"] = "系统维护通知",
                ["maintenance_start"] = "2026-07-26 02:00",
                ["maintenance_end"] = "2026-07-26 04:00",
                ["maintenance_content"] = "系统将于本周日凌晨进行例行维护，期间服务可能短暂中断。"
            },
            _ => new Dictionary<string, string>
            {
                ["conversation_id"] = "123",
                ["subject"] = "示例会话标题",
                ["customer_name"] = "张三",
                ["customer_email"] = "zhangsan@example.com",
                ["user_name"] = "李客服",
                ["user_email"] = "support@example.com",
                ["mailbox_name"] = "客服邮箱",
                ["conversation_url"] = $"{url}/conversations/123",
                ["company_name"] = "Nonead Customer Service"
            }
        };
    }

    private static readonly HashSet<string> CommonVariableSet = new()
    {
        // 会话
        "subject", "conversation_id", "conversation_url",
        // 客户
        "customer_name", "customer_email",
        // 用户
        "user_name", "user_email", "user_role",
        // 邮箱
        "mailbox_name", "mailbox_email",
        // 角色/人员
        "assignee_name", "assigner_name", "closer_name", "updater_name",
        "replier_name", "mentioner_name", "escalated_by", "assigned_to",
        // SLA
        "sla_name", "sla_level", "sla_deadline", "deadline",
        "sla_remaining", "remaining_time", "violation_type",
        // 满意度
        "satisfaction_url", "rating_url", "feedback_url",
        "rating", "rating_label", "satisfaction_comment",
        // 升级
        "escalation_reason", "escalated_at", "priority",
        // 系统
        "company_name", "login_url", "reset_url", "expire_time",
        // 消息
        "message_body", "update_content",
        // 时间
        "created_at", "received_time", "sla_response_time",
        "last_reply_time", "waiting_time", "timeout_minutes",
        // 批量导入
        "total_count", "success_count", "failed_count", "error_report_url",
    };

    private static readonly Regex VariableRegex = new(@"\{(\w+)\}", RegexOptions.Compiled);

    private static List<string> ExtractVariables(string input)
    {
        if (string.IsNullOrEmpty(input)) return new List<string>();
        return VariableRegex.Matches(input)
            .Select(m => m.Groups[1].Value)
            .Distinct()
            .OrderBy(v => v)
            .ToList();
    }
    public async Task<List<NotificationRuleResponse>> GetRulesAsync()
    {
        var rules = await _unitOfWork.NotificationRules.GetAllAsync();
        return rules.Select(ToRuleResponse).ToList();
    }

    public async Task<List<NotificationRuleResponse>> GetRulesByEventTypeAsync(string eventType)
    {
        var rules = await _unitOfWork.NotificationRules.GetByEventTypeAsync(eventType);
        return rules.Select(ToRuleResponse).ToList();
    }

    public async Task<NotificationRuleResponse?> GetRuleByIdAsync(int id)
    {
        var rule = await _unitOfWork.NotificationRules.GetByIdAsync(id);
        return rule != null ? ToRuleResponse(rule) : null;
    }

    public async Task<NotificationRuleResponse> CreateRuleAsync(CreateNotificationRuleRequest request)
    {
        var rule = new NotificationRule
        {
            Name = request.Name,
            EventType = request.EventType,
            MailboxId = request.MailboxId,
            UserId = request.UserId,
            RoleId = request.RoleId,
            TeamId = request.TeamId,
            RecipientMailboxId = request.RecipientMailboxId,
            TemplateId = request.TemplateId,
            IsActive = request.IsActive
        };

        await _unitOfWork.NotificationRules.AddAsync(rule);
        await _unitOfWork.SaveChangesAsync();

        // 直接复用已构造的 rule 实体返回，避免 GetActiveAsync 过滤未激活规则导致 null
        // ToRuleResponse 中所有导航属性访问均使用 ?. 空条件运算符，不会抛 NRE
        return ToRuleResponse(rule);
    }

    public async Task<NotificationRuleResponse?> UpdateRuleAsync(int id, UpdateNotificationRuleRequest request, string? ifMatch = null)
    {
        var rule = await _unitOfWork.NotificationRules.GetQueryable(false)
            .FirstOrDefaultAsync(r => r.Id == id && r.DeletedAt == null);
        if (rule == null)
        {
            return null;
        }

        // M8: 乐观锁 If-Match 校验（HTTP 标准优先，Header 覆盖 Body）
        var effectiveIfMatch = !string.IsNullOrEmpty(ifMatch) ? ifMatch.Trim('"') : request.IfMatch;
        ConcurrencyCheckHelper.Check(rule, rule.RowVersion, effectiveIfMatch, "NotificationRule", rule.Id,
            () => ToRuleResponse(rule), _logger);

        rule.Name = request.Name;
        rule.EventType = request.EventType;
        rule.MailboxId = request.MailboxId;
        rule.UserId = request.UserId;
        rule.RoleId = request.RoleId;
        rule.TeamId = request.TeamId;
        rule.RecipientMailboxId = request.RecipientMailboxId;
        rule.TemplateId = request.TemplateId;
        rule.IsActive = request.IsActive;
        rule.UpdatedAt = DateTime.UtcNow;

        await _unitOfWork.SaveChangesAsync();

        return ToRuleResponse(rule);
    }

    public async Task<bool> DeleteRuleAsync(int id)
    {
        var rule = await _unitOfWork.NotificationRules.GetByIdAsync(id);
        if (rule == null)
        {
            return false;
        }

        await _unitOfWork.NotificationRules.SoftDeleteAsync(id);
        await _unitOfWork.SaveChangesAsync();
        return true;
    }

    public async Task<NotificationRuleResponse?> ToggleRuleActiveAsync(int id)
    {
        var rule = await _unitOfWork.NotificationRules.GetQueryable(false)
            .FirstOrDefaultAsync(r => r.Id == id && r.DeletedAt == null);
        if (rule == null)
        {
            return null;
        }

        rule.IsActive = !rule.IsActive;
        rule.UpdatedAt = DateTime.UtcNow;

        await _unitOfWork.SaveChangesAsync();

        return ToRuleResponse(rule);
    }

    public async Task<(List<NotificationRuleResponse> Items, int TotalCount)> GetPagedRulesAsync(NotificationRuleQuery query)
    {
        var (items, total) = await _unitOfWork.NotificationRules.GetPagedAsync(
            query.EventType, query.MailboxId, query.UserId, query.IsActive, query.Search, query.Page, query.PageSize);
        return (items.Select(ToRuleResponse).ToList(), total);
    }

    public async Task<NotificationRuleOverviewResponse> GetRuleOverviewAsync()
    {
        var query = _unitOfWork.NotificationRules.GetQueryable();

        var total = await query.CountAsync();
        var active = await query.CountAsync(r => r.IsActive);

        var events = await query
            .GroupBy(r => r.EventType)
            .Select(g => new NotificationRuleEventStats
            {
                EventType = g.Key,
                EventTypeLabel = GetEventTypeLabel(g.Key),
                Count = g.Count(),
                ActiveCount = g.Count(r => r.IsActive)
            })
            .OrderBy(e => e.EventType)
            .ToListAsync();

        return new NotificationRuleOverviewResponse
        {
            Total = total,
            Active = active,
            Inactive = total - active,
            Events = events
        };
    }

    public async Task<NotificationRuleResponse?> DuplicateRuleAsync(int id, string? newName = null)
    {
        var rule = await _unitOfWork.NotificationRules.GetByIdAsync(id);
        if (rule == null) return null;

        var name = newName ?? $"{rule.Name} - 副本";
        var suffix = 1;
        while ((await _unitOfWork.NotificationRules.FindAsync(r => r.Name == name && r.DeletedAt == null)).Any())
        {
            name = $"{newName ?? rule.Name} - 副本 {suffix++}";
        }

        var duplicate = new NotificationRule
        {
            Name = name,
            EventType = rule.EventType,
            MailboxId = rule.MailboxId,
            UserId = rule.UserId,
            RoleId = rule.RoleId,
            TeamId = rule.TeamId,
            RecipientMailboxId = rule.RecipientMailboxId,
            TemplateId = rule.TemplateId,
            IsActive = false,
        };

        await _unitOfWork.NotificationRules.AddAsync(duplicate);
        await _unitOfWork.SaveChangesAsync();

        var created = await _unitOfWork.NotificationRules.GetByIdAsync(duplicate.Id);
        return created != null ? ToRuleResponse(created) : null;
    }

    // ====== 通知规则垃圾箱 ======
    public async Task<(List<NotificationRuleResponse> Items, int TotalCount)> GetRuleTrashAsync(int page, int pageSize, string? search)
    {
        var (items, total) = await _unitOfWork.NotificationRules.GetDeletedPagedAsync(search, page, pageSize);
        return (items.Select(ToRuleResponse).ToList(), total);
    }

    public async Task<int> GetRuleTrashCountAsync()
    {
        return await _unitOfWork.NotificationRules.CountDeletedAsync();
    }

    public async Task<bool> RestoreRuleAsync(int id)
    {
        // 先检查是否存在已删除的规则，避免对不存在的记录执行 ExecuteUpdate
        var deleted = await _unitOfWork.NotificationRules.GetQueryable(false)
            .IgnoreQueryFilters()
            .FirstOrDefaultAsync(r => r.Id == id && r.DeletedAt != null);
        if (deleted == null) return false;

        // 先恢复
        await _unitOfWork.NotificationRules.RestoreAsync(id);

        // 使用追踪查询获取实体，确保名称修改能持久化到数据库
        var restored = await _unitOfWork.NotificationRules.GetQueryable(false)
            .FirstOrDefaultAsync(r => r.Id == id);
        if (restored == null) return false;

        // 恢复后检查名称冲突
        var existing = await _unitOfWork.NotificationRules.FindAsync(
            r => r.Name == restored.Name && r.Id != id);
        if (existing.Any())
        {
            restored.Name = $"{restored.Name}（恢复_{DateTime.UtcNow:yyyyMMddHHmmss}）";
        }

        await _unitOfWork.SaveChangesAsync();
        return true;
    }

    public async Task<bool> HardDeleteRuleAsync(int id)
    {
        // 先检查是否存在已删除的规则，避免对不存在的记录执行 ExecuteDelete
        var deleted = await _unitOfWork.NotificationRules.GetQueryable(false)
            .IgnoreQueryFilters()
            .FirstOrDefaultAsync(r => r.Id == id && r.DeletedAt != null);
        if (deleted == null) return false;

        await _unitOfWork.NotificationRules.HardDeleteAsync(id);
        await _unitOfWork.SaveChangesAsync();
        return true;
    }

    public async Task<int> ClearRuleTrashAsync()
    {
        var count = await _unitOfWork.NotificationRules.HardDeleteAllDeletedAsync();
        if (count > 0)
        {
            await _unitOfWork.SaveChangesAsync();
        }
        return count;
    }

    // 通知发送
    public async Task<NotificationSendResultResponse> SendNotificationAsync(SendNotificationRequest request)
    {
        var result = new NotificationSendResultResponse();
        // 站内通知收集器：邮件入队之外，再把命中规则的收件人落一条 UserNotifications，
        // 让「通知中心」拥有可持久化的站内信（此前只有邮件队列，刷新页面即丢）。
        var inAppNotifications = new List<UserNotification>();
        var rules = await _unitOfWork.NotificationRules.GetActiveByEventTypeAsync(request.EventType);

        var filteredRules = rules
            .Where(r => !request.MailboxId.HasValue || !r.MailboxId.HasValue || r.MailboxId == request.MailboxId)
            .ToList();
        result.TotalRules = filteredRules.Count;

        // 预加载所有需要的数据，避免 N+1 查询
        var (userDict, adminEmails, mailboxDict, roleUserEmails, teamUserEmails, mailboxUserEmails) = await PreloadNotificationDataAsync(filteredRules, request.MailboxId);

        foreach (var rule in filteredRules)
        {
            try
            {
                var recipients = GetRecipientsFromCache(rule, userDict, adminEmails, roleUserEmails, teamUserEmails, mailboxUserEmails);
                if (recipients.Count == 0)
                {
                    result.FailedCount++;
                    result.Errors.Add($"规则 \"{rule.Name}\" 无法找到收件人");
                    continue;
                }

                var template = rule.Template;
                if (template == null || !template.IsActive)
                {
                    result.FailedCount++;
                    result.Errors.Add($"规则 \"{rule.Name}\" 没有可用模板");
                    continue;
                }

                var subject = RenderTemplate(template.Subject, request.Variables);
                var body = RenderTemplate(template.Body, request.Variables);

                var mailbox = GetMailboxFromCache(rule.MailboxId, request.MailboxId, mailboxDict);
                if (mailbox == null)
                {
                    result.FailedCount++;
                    result.Errors.Add($"规则 \"{rule.Name}\" 无法找到可用邮箱发送");
                    continue;
                }

                foreach (var recipient in recipients)
                {
                    await _emailQueueService.EnqueueEmailAsync(mailbox.Id, recipient, subject, body);
                    result.SentCount++;

                    // 站内信落库：按预加载的用户字典把收件人邮箱反查成 userId，
                    // 命中则记一条 UserNotification（未命中说明该收件人是外部地址，只走邮件）。
                    var recipientUser = userDict.Values.FirstOrDefault(u =>
                        !string.IsNullOrEmpty(u.Email) &&
                        string.Equals(u.Email, recipient, StringComparison.OrdinalIgnoreCase));
                    if (recipientUser != null)
                    {
                        inAppNotifications.Add(new UserNotification
                        {
                            UserId = recipientUser.Id,
                            Title = subject,
                            Message = body,
                            Type = "notification",
                            CreatedAt = DateTime.UtcNow
                        });
                    }

                    _logger.LogInformation("通知已加入队列: 规则={RuleName}, 收件人={Recipient}", rule.Name, recipient);
                }
            }
            catch (Exception ex)
            {
                result.FailedCount++;
                result.Errors.Add($"规则 \"{rule.Name}\" 发送失败: 服务器内部错误，详见系统日志");
                _logger.LogError(ex, "通知发送失败: 规则={RuleName}", rule.Name);
            }
        }

        // 站内信一次性落库（AddRangeAsync 只挂到 ChangeTracker，这里统一保存一次）
        if (inAppNotifications.Count > 0)
        {
            try
            {
                await _unitOfWork.UserNotifications.AddRangeAsync(inAppNotifications);
                await _unitOfWork.SaveChangesAsync();
                _logger.LogInformation("站内通知已落库：{Count} 条", inAppNotifications.Count);
            }
            catch (Exception ex)
            {
                // 站内信写入失败不应让整次通知发送失败（邮件已入队）
                _logger.LogError(ex, "站内通知落库失败：{Count} 条", inAppNotifications.Count);
            }
        }

        return result;
    }

    public string RenderTemplate(string template, Dictionary<string, string> variables)
    {
        return _renderer.Render(template, variables);
    }

    /// <summary>
    /// 预加载通知发送所需的数据（批量查询，避免 N+1）
    /// </summary>
    private async Task<(
        Dictionary<int, User> UserDict, 
        List<string> AdminEmails, 
        Dictionary<int, Mailbox> MailboxDict,
        Dictionary<int, List<string>> RoleUserEmails,
        Dictionary<int, List<string>> TeamUserEmails,
        Dictionary<int, List<string>> MailboxUserEmails)> 
        PreloadNotificationDataAsync(List<NotificationRule> rules, int? requestMailboxId)
    {
        // 收集所有需要查询的用户 ID
        var userIds = rules.Where(r => r.UserId.HasValue).Select(r => r.UserId!.Value).Distinct().ToList();

        // 收集所有需要查询的角色 ID
        var roleIds = rules.Where(r => r.RoleId.HasValue).Select(r => r.RoleId!.Value).Distinct().ToList();

        // 收集所有需要查询的团队 ID
        var teamIds = rules.Where(r => r.TeamId.HasValue).Select(r => r.TeamId!.Value).Distinct().ToList();

        // 收集所有需要查询的收件人邮箱 ID
        var recipientMailboxIds = rules.Where(r => r.RecipientMailboxId.HasValue).Select(r => r.RecipientMailboxId!.Value).Distinct().ToList();

        // 收集所有需要查询的邮箱 ID（规则适用范围 + 收件人邮箱 + 请求邮箱）
        var mailboxIds = rules.Where(r => r.MailboxId.HasValue).Select(r => r.MailboxId!.Value).ToList();
        foreach (var mbId in recipientMailboxIds)
        {
            if (!mailboxIds.Contains(mbId))
            {
                mailboxIds.Add(mbId);
            }
        }
        if (requestMailboxId.HasValue && !mailboxIds.Contains(requestMailboxId.Value))
        {
            mailboxIds.Add(requestMailboxId.Value);
        }

        // 顺序预加载所有数据（DbContext 非线程安全，禁止并行访问同一 UnitOfWork）
        var userDict = userIds.Any()
            ? (await _unitOfWork.Users.GetByIdsAsync(userIds)).ToDictionary(u => u.Id)
            : new Dictionary<int, User>();

        var adminEmails = await _unitOfWork.Users.GetQueryable()
            .Where(u => (u.Role == UserRoleType.SuperAdmin || u.Role == UserRoleType.AgentSupervisor) && u.Status == UserStatus.Active && !string.IsNullOrEmpty(u.Email))
            .Select(u => u.Email!)
            .Distinct()
            .ToListAsync();

        var mailboxDict = mailboxIds.Any()
            ? (await _unitOfWork.Mailboxes.GetByIdsAsync(mailboxIds)).ToDictionary(m => m.Id)
            : new Dictionary<int, Mailbox>();

        // 预加载角色对应的用户邮箱
        var roleUserEmails = await LoadRoleUserEmailsAsync(roleIds);

        // 预加载团队对应的用户邮箱
        var teamUserEmails = await LoadTeamUserEmailsAsync(teamIds);

        // 预加载邮箱对应的客服邮箱
        var mailboxUserEmails = await LoadMailboxUserEmailsAsync(recipientMailboxIds);

        return (userDict, adminEmails, mailboxDict, roleUserEmails, teamUserEmails, mailboxUserEmails);
    }

    /// <summary>
    /// 加载角色对应的用户邮箱列表（批量查询，避免 N+1）
    /// </summary>
    private async Task<Dictionary<int, List<string>>> LoadRoleUserEmailsAsync(List<int> roleIds)
    {
        var result = new Dictionary<int, List<string>>();
        if (!roleIds.Any()) return result;

        var allUserRoles = await _unitOfWork.UserRoles.GetQueryable()
            .Where(ur => roleIds.Contains(ur.RoleId))
            .ToListAsync();

        var allUserIds = allUserRoles.Select(ur => ur.UserId).Distinct().ToList();
        Dictionary<int, User> userDict = new();
        if (allUserIds.Any())
        {
            var users = await _unitOfWork.Users.GetByIdsAsync(allUserIds);
            userDict = users.ToDictionary(u => u.Id);
        }

        foreach (var roleId in roleIds)
        {
            var userIdsInRole = allUserRoles
                .Where(ur => ur.RoleId == roleId)
                .Select(ur => ur.UserId)
                .Distinct()
                .ToList();

            var emails = userIdsInRole
                .Select(uid => userDict.TryGetValue(uid, out var u) ? u : null)
                .Where(u => u != null && !string.IsNullOrEmpty(u.Email) && u.Status == UserStatus.Active)
                .Select(u => u!.Email!)
                .Distinct()
                .ToList();

            result[roleId] = emails;
        }

        return result;
    }

    /// <summary>
    /// 加载团队对应的用户邮箱列表（批量查询，避免 N+1）
    /// </summary>
    private async Task<Dictionary<int, List<string>>> LoadTeamUserEmailsAsync(List<int> teamIds)
    {
        var result = new Dictionary<int, List<string>>();
        if (!teamIds.Any()) return result;

        var allTeamMembers = await _unitOfWork.TeamMembers.GetByTeamIdsAsync(teamIds);

        var allUserIds = allTeamMembers.Select(tm => tm.UserId).Distinct().ToList();
        Dictionary<int, User> userDict = new();
        if (allUserIds.Any())
        {
            var users = await _unitOfWork.Users.GetByIdsAsync(allUserIds);
            userDict = users.ToDictionary(u => u.Id);
        }

        foreach (var teamId in teamIds)
        {
            var userIdsInTeam = allTeamMembers
                .Where(tm => tm.TeamId == teamId)
                .Select(tm => tm.UserId)
                .Distinct()
                .ToList();

            var emails = userIdsInTeam
                .Select(uid => userDict.TryGetValue(uid, out var u) ? u : null)
                .Where(u => u != null && !string.IsNullOrEmpty(u.Email) && u.Status == UserStatus.Active)
                .Select(u => u!.Email!)
                .Distinct()
                .ToList();

            result[teamId] = emails;
        }

        return result;
    }

    /// <summary>
    /// 加载邮箱对应的客服邮箱列表（批量查询，避免 N+1）
    /// </summary>
    private async Task<Dictionary<int, List<string>>> LoadMailboxUserEmailsAsync(List<int> mailboxIds)
    {
        var result = new Dictionary<int, List<string>>();
        if (!mailboxIds.Any()) return result;

        // 一次性查询所有相关 MailboxUser 关联（避免先全表加载）
        var allMailboxUsers = await _unitOfWork.Repository<MailboxUser>().GetQueryable()
            .Where(mu => mailboxIds.Contains(mu.MailboxId))
            .ToListAsync();

        // 收集所有涉及的用户 ID 并一次性查询用户邮箱
        var allUserIds = allMailboxUsers.Select(mu => mu.UserId).Distinct().ToList();
        Dictionary<int, string> userEmailDict = new();
        if (allUserIds.Any())
        {
            var users = await _unitOfWork.Users.GetByIdsAsync(allUserIds);
            userEmailDict = users
                .Where(u => !string.IsNullOrEmpty(u.Email) && u.Status == UserStatus.Active)
                .ToDictionary(u => u.Id, u => u.Email!);
        }

        // 按邮箱 ID 分组（避免每邮箱一次查询）
        foreach (var mailboxId in mailboxIds)
        {
            var userIds = allMailboxUsers
                .Where(mu => mu.MailboxId == mailboxId)
                .Select(mu => mu.UserId)
                .Distinct()
                .ToList();

            var emails = userIds
                .Where(uid => userEmailDict.ContainsKey(uid))
                .Select(uid => userEmailDict[uid])
                .Distinct()
                .ToList();

            result[mailboxId] = emails;
        }

        return result;
    }

    /// <summary>
    /// 从缓存中获取收件人（无数据库查询）
    /// 优先级：用户 > 角色 > 团队 > 邮箱成员 > 所有管理员（默认）
    /// </summary>
    private List<string> GetRecipientsFromCache(
        NotificationRule rule, 
        Dictionary<int, User> userDict, 
        List<string> adminEmails,
        Dictionary<int, List<string>> roleUserEmails,
        Dictionary<int, List<string>> teamUserEmails,
        Dictionary<int, List<string>> mailboxUserEmails)
    {
        var recipients = new List<string>();

        // 1. 指定用户
        if (rule.UserId.HasValue && userDict.TryGetValue(rule.UserId.Value, out var user))
        {
            if (!string.IsNullOrEmpty(user.Email))
            {
                recipients.Add(user.Email);
            }
        }
        // 2. 指定角色
        else if (rule.RoleId.HasValue && roleUserEmails.TryGetValue(rule.RoleId.Value, out var roleEmails))
        {
            recipients.AddRange(roleEmails);
        }
        // 3. 指定团队
        else if (rule.TeamId.HasValue && teamUserEmails.TryGetValue(rule.TeamId.Value, out var teamEmails))
        {
            recipients.AddRange(teamEmails);
        }
        // 4. 指定邮箱的所有客服
        else if (rule.RecipientMailboxId.HasValue && mailboxUserEmails.TryGetValue(rule.RecipientMailboxId.Value, out var mbUserEmails))
        {
            recipients.AddRange(mbUserEmails);
        }
        // 5. 默认：所有管理员（保持向后兼容）
        else
        {
            recipients.AddRange(adminEmails);
        }

        return recipients.Distinct().ToList();
    }

    /// <summary>
    /// 从缓存中获取邮箱（无数据库查询）
    /// </summary>
    private Mailbox? GetMailboxFromCache(int? ruleMailboxId, int? requestMailboxId, Dictionary<int, Mailbox> mailboxDict)
    {
        var mailboxId = ruleMailboxId ?? requestMailboxId;
        if (!mailboxId.HasValue)
        {
            return mailboxDict.Values.FirstOrDefault(m => m.IsActive);
        }

        if (mailboxDict.TryGetValue(mailboxId.Value, out var mailbox))
        {
            return mailbox.IsActive ? mailbox : null;
        }

        return null;
    }

    private static NotificationTemplateResponse ToTemplateResponse(NotificationTemplate template)
    {
        var subjectVars = ExtractVariables(template.Subject);
        var bodyVars = ExtractVariables(template.Body);
        var allVars = subjectVars.Union(bodyVars).OrderBy(v => v).ToList();

        return new NotificationTemplateResponse
        {
            Id = template.Id,
            Name = template.Name,
            Subject = template.Subject,
            Body = template.Body,
            EventType = template.EventType,
            EventTypeLabel = GetEventTypeLabel(template.EventType),
            IsActive = template.IsActive,
            CreatedAt = template.CreatedAt,
            UpdatedAt = template.UpdatedAt,
            DeletedAt = template.DeletedAt,
            Variables = allVars,
        };
    }

    private static NotificationRuleResponse ToRuleResponse(NotificationRule rule)
    {
        return new NotificationRuleResponse
        {
            Id = rule.Id,
            Name = rule.Name,
            EventType = rule.EventType,
            EventTypeLabel = GetEventTypeLabel(rule.EventType),
            MailboxId = rule.MailboxId,
            MailboxName = rule.Mailbox?.Name,
            UserId = rule.UserId,
            UserName = rule.User != null ? rule.User.GetFullName() : null,
            RoleId = rule.RoleId,
            RoleName = rule.Role?.Name,
            TeamId = rule.TeamId,
            TeamName = rule.Team?.Name,
            RecipientMailboxId = rule.RecipientMailboxId,
            RecipientMailboxName = rule.RecipientMailbox?.Name,
            TemplateId = rule.TemplateId,
            TemplateName = rule.Template?.Name,
            IsActive = rule.IsActive,
            CreatedAt = rule.CreatedAt,
            UpdatedAt = rule.UpdatedAt,
            DeletedAt = rule.DeletedAt,
            RowVersion = rule.RowVersion != null ? Convert.ToBase64String(rule.RowVersion) : null,
        };
    }

    private static string GetEventTypeLabel(string eventType)
    {
        return eventType switch
        {
            "new_conversation" => "新会话",
            "new_reply" => "新回复",
            "conversation_assigned" => "会话已分配",
            "conversation_updated" => "会话已更新",
            "conversation_closed" => "会话已关闭",
            "customer_reply" => "客户回复",
            "mention" => "被提及",
            "sla_violation" => "SLA 违规",
            "sla_warning" => "SLA 预警",
            "ticket_created" => "工单已创建",
            "ticket_updated" => "工单已更新",
            "ticket_escalated" => "工单已升级",
            "customer_satisfaction" => "客户满意度评价",
            "auto_reply" => "自动回复",
            "user_welcome" => "新用户欢迎",
            "password_reset" => "密码重置",
            "conversation_timeout" => "会话超时未响应",
            "bulk_import_completed" => "批量导入完成",
            "conversation_reopened" => "会话已重新打开",
            "conversation_transferred" => "会话已转交",
            "satisfaction_submitted" => "满意度评价已提交",
            "daily_report" => "每日报表汇总",
            "account_locked" => "账户已锁定",
            "system_maintenance" => "系统维护",
            _ => eventType
        };
    }

    // 扩展方法 - 对应 PHP 版本的 SendNotificationToUsers
    public async Task SendNotificationAsync(int userId, string title, string message, string? link = null)
    {
        try
        {
            var user = await _unitOfWork.Users.GetByIdAsync(userId);
            if (user == null)
            {
                _logger.LogInformation("用户 {UserId} 不存在，无法发送通知", userId);
                return;
            }

            var notification = new UserNotification
            {
                UserId = userId,
                Title = title,
                Message = message,
                Link = link,
                Type = "info",
                IsRead = false
            };

            var repository = _unitOfWork.Repository<UserNotification>();
            await repository.AddAsync(notification);
            await _unitOfWork.SaveChangesAsync();

            await _notificationPublisher.NotifyUserAsync(userId.ToString(), "SystemNotification", new
            {
                id = notification.Id,
                title = notification.Title,
                message = notification.Message,
                type = notification.Type,
                link = notification.Link,
                createdAt = notification.CreatedAt
            });

            _logger.LogInformation("通知已发送给用户 {UserId}: {Title}", userId, title);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "发送通知失败: UserId={UserId}", userId);
        }
    }

    public async Task<List<int>> GetConversationFollowersAsync(int conversationId)
    {
        // 不吞异常：DB 查询失败时向上传播，让调用方（事件监听器）决定降级策略，
        // 避免与"会话没有关注者"的正常空列表混淆导致通知静默丢失。
        var conversation = await _unitOfWork.Conversations.GetByIdAsync(conversationId);
        if (conversation == null)
        {
            return new List<int>();
        }

        // 完整实现：会话分配人 + 所有关注者（去重）。
        // GetByIdAsync 已包含 Followers（ApplyFullIncludes），无需额外查询。
        var followerIds = new HashSet<int>();
        if (conversation.UserId != null)
        {
            followerIds.Add(conversation.UserId.Value);
        }
        foreach (var follower in conversation.Followers)
        {
            followerIds.Add(follower.UserId);
        }
        return followerIds.ToList();
    }
}
