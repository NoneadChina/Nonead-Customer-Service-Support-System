using NoneadCSS.Domain.Entities;
using NoneadCSS.Infrastructure.Persistence;

namespace NoneadCSS.Api.Seeders;

/// <summary>
/// 通知规则种子数据初始化器
/// 从 DatabaseSeeder 拆分而来，负责 NotificationRule 的增量补充。
/// 通知规则依赖通知模板，因此通知模板需先于本 Seeder 执行并保存到数据库
/// （首次启动时模板尚未持久化，规则查询模板会返回 null 而跳过创建；
///  再次启动后模板已存在，规则即可正常建立关联）。
/// 本 Seeder 在末尾调用 SaveChangesAsync，统一持久化模板与规则的变更。
/// </summary>
public static class NotificationRuleSeeder
{
    /// <summary>
    /// 增量添加通知规则。查询数据库中已存在的通知模板，按规则名称去重后补充缺失的规则。
    /// </summary>
    /// <param name="context">数据库上下文</param>
    public static async Task SeedAsync(NoneadCSSDbContext context)
    {
        // 通知规则种子数据 - 增量添加
        var existingRuleNames = context.NotificationRules.Select(r => r.Name).ToList();
        var nrMailbox = context.Mailboxes.FirstOrDefault(m => m.Email == "support@example.com");
        var nrAdminUser = context.Users.FirstOrDefault(u => u.Email == "admin@example.com");

        var newConvTemplate = context.NotificationTemplates.FirstOrDefault(t => t.EventType == "new_conversation");
        var customerReplyTemplate = context.NotificationTemplates.FirstOrDefault(t => t.EventType == "customer_reply");
        var assignedTemplate = context.NotificationTemplates.FirstOrDefault(t => t.EventType == "conversation_assigned");
        var newReplyTemplate = context.NotificationTemplates.FirstOrDefault(t => t.EventType == "new_reply");
        var closedTemplate = context.NotificationTemplates.FirstOrDefault(t => t.EventType == "conversation_closed");
        var mentionTemplate = context.NotificationTemplates.FirstOrDefault(t => t.EventType == "mention");
        var slaViolationTemplate = context.NotificationTemplates.FirstOrDefault(t => t.EventType == "sla_violation");
        var updatedTemplate = context.NotificationTemplates.FirstOrDefault(t => t.EventType == "conversation_updated");

        if (!existingRuleNames.Contains("新会话通知管理员") && newConvTemplate != null)
        {
            context.NotificationRules.Add(new NotificationRule
            {
                Name = "新会话通知管理员",
                EventType = "new_conversation",
                Mailbox = nrMailbox,
                User = nrAdminUser,
                Template = newConvTemplate,
                IsActive = true,
                CreatedAt = DateTime.UtcNow,
                UpdatedAt = DateTime.UtcNow
            });
        }

        if (!existingRuleNames.Contains("客户回复通知处理人") && customerReplyTemplate != null)
        {
            context.NotificationRules.Add(new NotificationRule
            {
                Name = "客户回复通知处理人",
                EventType = "customer_reply",
                Mailbox = nrMailbox,
                User = null,
                Template = customerReplyTemplate,
                IsActive = true,
                CreatedAt = DateTime.UtcNow,
                UpdatedAt = DateTime.UtcNow
            });
        }

        if (!existingRuleNames.Contains("会话分配通知") && assignedTemplate != null)
        {
            context.NotificationRules.Add(new NotificationRule
            {
                Name = "会话分配通知",
                EventType = "conversation_assigned",
                Mailbox = nrMailbox,
                User = null,
                Template = assignedTemplate,
                IsActive = true,
                CreatedAt = DateTime.UtcNow,
                UpdatedAt = DateTime.UtcNow
            });
        }

        if (!existingRuleNames.Contains("新回复通知处理人") && newReplyTemplate != null)
        {
            context.NotificationRules.Add(new NotificationRule
            {
                Name = "新回复通知处理人",
                EventType = "new_reply",
                Mailbox = nrMailbox,
                User = null,
                Template = newReplyTemplate,
                IsActive = true,
                CreatedAt = DateTime.UtcNow,
                UpdatedAt = DateTime.UtcNow
            });
        }

        if (!existingRuleNames.Contains("会话关闭通知管理员") && closedTemplate != null)
        {
            context.NotificationRules.Add(new NotificationRule
            {
                Name = "会话关闭通知管理员",
                EventType = "conversation_closed",
                Mailbox = nrMailbox,
                User = nrAdminUser,
                Template = closedTemplate,
                IsActive = true,
                CreatedAt = DateTime.UtcNow,
                UpdatedAt = DateTime.UtcNow
            });
        }

        if (!existingRuleNames.Contains("被提及通知") && mentionTemplate != null)
        {
            context.NotificationRules.Add(new NotificationRule
            {
                Name = "被提及通知",
                EventType = "mention",
                Mailbox = nrMailbox,
                User = null,
                Template = mentionTemplate,
                IsActive = true,
                CreatedAt = DateTime.UtcNow,
                UpdatedAt = DateTime.UtcNow
            });
        }

        if (!existingRuleNames.Contains("SLA违规通知管理员") && slaViolationTemplate != null)
        {
            context.NotificationRules.Add(new NotificationRule
            {
                Name = "SLA违规通知管理员",
                EventType = "sla_violation",
                Mailbox = nrMailbox,
                User = nrAdminUser,
                Template = slaViolationTemplate,
                IsActive = true,
                CreatedAt = DateTime.UtcNow,
                UpdatedAt = DateTime.UtcNow
            });
        }

        if (!existingRuleNames.Contains("会话更新通知处理人") && updatedTemplate != null)
        {
            context.NotificationRules.Add(new NotificationRule
            {
                Name = "会话更新通知处理人",
                EventType = "conversation_updated",
                Mailbox = nrMailbox,
                User = null,
                Template = updatedTemplate,
                IsActive = true,
                CreatedAt = DateTime.UtcNow,
                UpdatedAt = DateTime.UtcNow
            });
        }

        // ===== 新增通知规则 =====
        var slaWarningTemplate = context.NotificationTemplates.FirstOrDefault(t => t.EventType == "sla_warning");
        var csatTemplate = context.NotificationTemplates.FirstOrDefault(t => t.EventType == "customer_satisfaction");
        var autoReplyTemplate = context.NotificationTemplates.FirstOrDefault(t => t.EventType == "auto_reply");
        var timeoutTemplate = context.NotificationTemplates.FirstOrDefault(t => t.EventType == "conversation_timeout");
        var escalatedTemplate = context.NotificationTemplates.FirstOrDefault(t => t.EventType == "ticket_escalated");
        var welcomeTemplate = context.NotificationTemplates.FirstOrDefault(t => t.EventType == "user_welcome");
        var pwdResetTemplate = context.NotificationTemplates.FirstOrDefault(t => t.EventType == "password_reset");
        var bulkImportTemplate = context.NotificationTemplates.FirstOrDefault(t => t.EventType == "bulk_import_completed");

        if (!existingRuleNames.Contains("SLA即将超时预警") && slaWarningTemplate != null)
        {
            context.NotificationRules.Add(new NotificationRule
            {
                Name = "SLA即将超时预警",
                EventType = "sla_warning",
                Mailbox = nrMailbox,
                User = null,
                Template = slaWarningTemplate,
                IsActive = true,
                CreatedAt = DateTime.UtcNow,
                UpdatedAt = DateTime.UtcNow
            });
        }

        if (!existingRuleNames.Contains("客户满意度评价邀请") && csatTemplate != null)
        {
            context.NotificationRules.Add(new NotificationRule
            {
                Name = "客户满意度评价邀请",
                EventType = "customer_satisfaction",
                Mailbox = nrMailbox,
                User = null,
                Template = csatTemplate,
                IsActive = false,
                CreatedAt = DateTime.UtcNow,
                UpdatedAt = DateTime.UtcNow
            });
        }

        if (!existingRuleNames.Contains("自动回复-邮件已收到") && autoReplyTemplate != null)
        {
            context.NotificationRules.Add(new NotificationRule
            {
                Name = "自动回复-邮件已收到",
                EventType = "auto_reply",
                Mailbox = nrMailbox,
                User = null,
                Template = autoReplyTemplate,
                IsActive = false,
                CreatedAt = DateTime.UtcNow,
                UpdatedAt = DateTime.UtcNow
            });
        }

        if (!existingRuleNames.Contains("会话超时未响应提醒") && timeoutTemplate != null)
        {
            context.NotificationRules.Add(new NotificationRule
            {
                Name = "会话超时未响应提醒",
                EventType = "conversation_timeout",
                Mailbox = nrMailbox,
                User = null,
                Template = timeoutTemplate,
                IsActive = false,
                CreatedAt = DateTime.UtcNow,
                UpdatedAt = DateTime.UtcNow
            });
        }

        if (!existingRuleNames.Contains("工单升级通知管理员") && escalatedTemplate != null)
        {
            context.NotificationRules.Add(new NotificationRule
            {
                Name = "工单升级通知管理员",
                EventType = "ticket_escalated",
                Mailbox = nrMailbox,
                User = nrAdminUser,
                Template = escalatedTemplate,
                IsActive = true,
                CreatedAt = DateTime.UtcNow,
                UpdatedAt = DateTime.UtcNow
            });
        }

        if (!existingRuleNames.Contains("新用户欢迎邮件") && welcomeTemplate != null)
        {
            context.NotificationRules.Add(new NotificationRule
            {
                Name = "新用户欢迎邮件",
                EventType = "user_welcome",
                Mailbox = nrMailbox,
                User = null,
                Template = welcomeTemplate,
                IsActive = false,
                CreatedAt = DateTime.UtcNow,
                UpdatedAt = DateTime.UtcNow
            });
        }

        if (!existingRuleNames.Contains("密码重置通知") && pwdResetTemplate != null)
        {
            context.NotificationRules.Add(new NotificationRule
            {
                Name = "密码重置通知",
                EventType = "password_reset",
                Mailbox = nrMailbox,
                User = null,
                Template = pwdResetTemplate,
                IsActive = true,
                CreatedAt = DateTime.UtcNow,
                UpdatedAt = DateTime.UtcNow
            });
        }

        if (!existingRuleNames.Contains("批量导入完成通知") && bulkImportTemplate != null)
        {
            context.NotificationRules.Add(new NotificationRule
            {
                Name = "批量导入完成通知",
                EventType = "bulk_import_completed",
                Mailbox = nrMailbox,
                User = null,
                Template = bulkImportTemplate,
                IsActive = true,
                CreatedAt = DateTime.UtcNow,
                UpdatedAt = DateTime.UtcNow
            });
        }

        await context.SaveChangesAsync();
    }
}
