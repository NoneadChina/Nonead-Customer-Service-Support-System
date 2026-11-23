using NoneadCSS.Domain.Entities;
using NoneadCSS.Infrastructure.Persistence;

namespace NoneadCSS.Api.Seeders;

/// <summary>
/// 工作流自动化规则种子数据初始化器
/// 从 DatabaseSeeder 拆分而来，负责 send_email 示例工作流的增量补充与历史迁移。
/// </summary>
public static class WorkflowSeeder
{
    /// <summary>
    /// 增量添加工作流自动化规则（按名称检查，确保已有数据库也能补充）。
    /// 包含旧版本工作流的迁移修复逻辑。
    /// </summary>
    /// <param name="context">数据库上下文</param>
    public static async Task SeedAsync(NoneadCSSDbContext context)
    {
        // 工作流自动化规则种子数据 - 增量添加 send_email 示例（按名称检查，确保已有数据库也能补充）
        {
            var existingWorkflowNames = new HashSet<string>(
                context.Workflows.Select(w => w.Name),
                StringComparer.OrdinalIgnoreCase);

            // 迁移：修复旧版本中错误配置的"会话状态变更感谢邮件"工作流
            // 旧版本将 TriggerType 错误设置为 "status_changed"，导致关闭会话时就发送"感谢您的评价"邮件
            // 正确行为应该是"客户提交满意度评价后"才发送，即 TriggerType = "satisfaction_submitted"
            var legacyThanksWorkflow = context.Workflows
                .FirstOrDefault(w => w.Name == "会话状态变更感谢邮件"
                    || (w.TriggerType == "status_changed" && w.ActionsJson != null && w.ActionsJson.Contains("satisfaction-thanks")));
            if (legacyThanksWorkflow != null)
            {
                legacyThanksWorkflow.Name = "满意度评价感谢邮件";
                legacyThanksWorkflow.Description = "客户提交满意度评价后，自动调用 satisfaction-thanks 自定义邮件模板向客户发送感谢邮件";
                legacyThanksWorkflow.TriggerType = "satisfaction_submitted";
                legacyThanksWorkflow.UpdatedAt = DateTime.UtcNow;
                await context.SaveChangesAsync();
                // 同步现有名称集合，避免下方再次尝试添加同名工作流
                existingWorkflowNames.Add("满意度评价感谢邮件");
            }

            // 迁移：启用 3 个标准自动化工作流（之前默认禁用，现改为默认启用）
            // 涉及工作流：客户回复确认邮件、紧急工单邮件通知、会话升级通知邮件
            var workflowNamesToActivate = new[]
            {
                "客户回复确认邮件",
                "紧急工单邮件通知",
                "会话升级通知邮件"
            };
            var workflowsToActivate = context.Workflows
                .Where(w => workflowNamesToActivate.Contains(w.Name) && !w.IsActive)
                .ToList();
            if (workflowsToActivate.Count > 0)
            {
                foreach (var w in workflowsToActivate)
                {
                    w.IsActive = true;
                    w.UpdatedAt = DateTime.UtcNow;
                }
                await context.SaveChangesAsync();
            }

            // 迁移：禁用"客户回复确认邮件"工作流（T000110）
            // 该工作流在"客户二次问答"（new_reply）时通过 customer-reply-default 模板向客户回发邮件，
            // 模板正文渲染 {{message_body}}（客户自己刚发送的最新消息），导致客户收到自己邮件的副本。
            // 预期行为：客户二次问答时仅客服收到 [邮件] 客户新回复通知（extra=assignee），客户不收取回执。
            // customer-reply-default 模板仅保留为"客服回复客户"时的正文后备（ConversationService 自动调用）。
            var replyConfirmWorkflow = context.Workflows
                .FirstOrDefault(w => w.Name == "客户回复确认邮件" && w.IsActive);
            if (replyConfirmWorkflow != null)
            {
                replyConfirmWorkflow.IsActive = false;
                replyConfirmWorkflow.Description = "（默认停用，T000110）客户二次问答时仅通知客服，不再向客户回发其本人消息副本";
                replyConfirmWorkflow.UpdatedAt = DateTime.UtcNow;
                await context.SaveChangesAsync();
            }

            // 迁移：禁用"新会话客户回访邮件"工作流
            // 该工作流在新会话创建时即发送回访邮件，不符合业务逻辑（客服尚未回复就回访客户）
            var followupWorkflow = context.Workflows
                .FirstOrDefault(w => w.Name == "新会话客户回访邮件" && w.IsActive);
            if (followupWorkflow != null)
            {
                followupWorkflow.IsActive = false;
                followupWorkflow.Description = "新会话创建后自动调用 customer-followup 自定义邮件模板向客户发送回访邮件（默认禁用，按需启用）";
                followupWorkflow.UpdatedAt = DateTime.UtcNow;
                await context.SaveChangesAsync();
            }

            // 迁移：禁用旧版本遗留的重复工作流（与 [邮件] 前缀版本功能完全相同）
            // 旧版本通过 DatabaseSeeder 或早期版本创建，新版 [邮件] 前缀版本由 WorkflowSeeder 管理
            // 两者同时活跃会导致同一触发器执行两次，客户收到重复邮件
            var legacyDuplicateNames = new[]
            {
                "会话关闭通知客户",   // 重复 [邮件] 会话关闭通知客户
                "会话分配通知客服",   // 重复 [邮件] 会话分配通知
                "新会话通知客服",     // 重复 [邮件] 新会话通知客服
                "客户新回复通知",     // 重复 [邮件] 客户新回复通知
            };
            var legacyDuplicates = context.Workflows
                .Where(w => legacyDuplicateNames.Contains(w.Name) && w.IsActive)
                .ToList();
            if (legacyDuplicates.Count > 0)
            {
                foreach (var w in legacyDuplicates)
                {
                    w.IsActive = false;
                    w.UpdatedAt = DateTime.UtcNow;
                }
                await context.SaveChangesAsync();
            }

            // 迁移：更新 [邮件] 会话关闭通知客户 工作流的条件，限定为仅邮件渠道触发
            // 原条件为空（对所有渠道生效），新条件为 source_type equals Email
            // IM 渠道由新增的 [IM] 会话关闭满意度通知 工作流独立处理，避免重复发送
            var emailClosedWorkflow = context.Workflows
                .FirstOrDefault(w => w.Name == "[邮件] 会话关闭通知客户" && w.IsActive);
            if (emailClosedWorkflow != null)
            {
                var newConditions = """{"matchType":"all","conditions":[{"field":"source_type","operator":"equals","value":"Email"}]}""";
                if (emailClosedWorkflow.ConditionsJson != newConditions)
                {
                    emailClosedWorkflow.ConditionsJson = newConditions;
                    emailClosedWorkflow.Description = "会话关闭时，向客户发送关闭通知（含满意度评价链接）。仅对邮件渠道会话生效，IM 渠道由 [IM] 会话关闭满意度通知 工作流处理";
                    emailClosedWorkflow.UpdatedAt = DateTime.UtcNow;
                    await context.SaveChangesAsync();
                }
            }

            var workflowsToAdd = new[]
            {
                new Workflow
                {
                    Name = "[邮件] 新会话通知客服",
                    Description = "新会话创建并分配后，向被分配客服发送通知邮件",
                    MailboxId = null,
                    IsActive = true,
                    IsSystem = true,
                    SortOrder = 1,
                    TriggerType = "new_conversation",
                    ConditionsJson = """{"matchType":"all","conditions":[]}""",
                    ActionsJson = """[{"type":"send_email","value":"new-conversation","extra":"assignee"}]""",
                    CreatedAt = DateTime.UtcNow.AddDays(-1),
                    UpdatedAt = DateTime.UtcNow.AddDays(-1)
                },
                new Workflow
                {
                    Name = "[邮件] 会话分配通知",
                    Description = "会话被分配或改派时，向被分配客服发送通知邮件",
                    MailboxId = null,
                    IsActive = true,
                    IsSystem = true,
                    SortOrder = 2,
                    TriggerType = "assignee_changed",
                    ConditionsJson = """{"matchType":"all","conditions":[]}""",
                    ActionsJson = """[{"type":"send_email","value":"conversation-assigned","extra":"assignee"}]""",
                    CreatedAt = DateTime.UtcNow.AddDays(-1),
                    UpdatedAt = DateTime.UtcNow.AddDays(-1)
                },
                new Workflow
                {
                    Name = "[邮件] 会话关闭通知客户",
                    Description = "会话关闭时，向客户发送关闭通知（含满意度评价链接）。仅对邮件渠道会话生效，IM 渠道由 [IM] 会话关闭满意度通知 工作流处理",
                    MailboxId = null,
                    IsActive = true,
                    IsSystem = true,
                    SortOrder = 3,
                    TriggerType = "conversation_closed",
                    ConditionsJson = """{"matchType":"all","conditions":[{"field":"source_type","operator":"equals","value":"Email"}]}""",
                    ActionsJson = """[{"type":"send_email","value":"conversation-closed","extra":"customer"}]""",
                    CreatedAt = DateTime.UtcNow.AddDays(-1),
                    UpdatedAt = DateTime.UtcNow.AddDays(-1)
                },
                new Workflow
                {
                    Name = "[IM] 会话关闭满意度通知",
                    Description = "IM 渠道（微信/飞书/WhatsApp/钉钉/Messenger/Telegram/LINE/iMessage）会话关闭时，通过对应 App 发送关闭通知（含满意度评价链接）。走 IM 消息队列，支持失败重试",
                    MailboxId = null,
                    IsActive = true,
                    IsSystem = true,
                    SortOrder = 3,
                    TriggerType = "conversation_closed",
                    // matchType=any 实现 source_type IN (WeChat, Feishu, WhatsApp, DingTalk, Messenger, Telegram, Line, IMessage) 语义
                    ConditionsJson = """{"matchType":"any","conditions":[{"field":"source_type","operator":"equals","value":"WeChat"},{"field":"source_type","operator":"equals","value":"Feishu"},{"field":"source_type","operator":"equals","value":"WhatsApp"},{"field":"source_type","operator":"equals","value":"DingTalk"},{"field":"source_type","operator":"equals","value":"Messenger"},{"field":"source_type","operator":"equals","value":"Telegram"},{"field":"source_type","operator":"equals","value":"Line"},{"field":"source_type","operator":"equals","value":"IMessage"}]}""",
                    ActionsJson = """[{"type":"send_im_message","value":"im-conversation-closed"}]""",
                    CreatedAt = DateTime.UtcNow.AddDays(-1),
                    UpdatedAt = DateTime.UtcNow.AddDays(-1)
                },
                new Workflow
                {
                    Name = "[邮件] 客户新回复通知",
                    Description = "客户发来新回复时，向当前处理客服发送通知邮件",
                    MailboxId = null,
                    IsActive = true,
                    IsSystem = true,
                    SortOrder = 4,
                    TriggerType = "new_reply",
                    ConditionsJson = """{"matchType":"all","conditions":[]}""",
                    ActionsJson = """[{"type":"send_email","value":"new-reply","extra":"assignee"}]""",
                    CreatedAt = DateTime.UtcNow.AddDays(-1),
                    UpdatedAt = DateTime.UtcNow.AddDays(-1)
                },
                new Workflow
                {
                    Name = "紧急工单邮件通知",
                    Description = "新会话主题包含「紧急」时，自动调用 sla-warning 自定义邮件模板通知客服",
                    MailboxId = null,
                    IsActive = true,
                    SortOrder = 10,
                    TriggerType = "new_conversation",
                    ConditionsJson = """{"matchType":"any","conditions":[{"field":"subject","operator":"contains","value":"紧急"},{"field":"subject","operator":"contains","value":"急"}]}""",
                    // type=send_email，value=邮件模板 slug，extra=收件人（assignee=分配客服；如未分配则发给 admin@example.com）
                    ActionsJson = """[{"type":"assign_user","value":"1"},{"type":"send_email","value":"sla-warning","extra":"assignee,admin@example.com"}]""",
                    CreatedAt = DateTime.UtcNow.AddDays(-1),
                    UpdatedAt = DateTime.UtcNow.AddDays(-1)
                },
                new Workflow
                {
                    Name = "新会话客户回访邮件",
                    Description = "新会话创建后自动调用 customer-followup 自定义邮件模板向客户发送回访邮件（默认禁用，按需启用）",
                    MailboxId = null,
                    IsActive = false,
                    SortOrder = 11,
                    TriggerType = "new_conversation",
                    ConditionsJson = """{"matchType":"all","conditions":[]}""",
                    // type=send_email，value=邮件模板 slug，extra=收件人（customer=客户邮箱）
                    ActionsJson = """[{"type":"send_email","value":"customer-followup","extra":"customer"}]""",
                    CreatedAt = DateTime.UtcNow.AddDays(-1),
                    UpdatedAt = DateTime.UtcNow.AddDays(-1)
                },
                // T000110：默认停用。客户二次问答时不应收到自己消息的副本，
                // 仅由 [邮件] 客户新回复通知（extra=assignee）通知客服。
                // customer-reply-default 模板仅作为"客服回复客户"时的正文后备（ConversationService 自动调用）。
                new Workflow
                {
                    Name = "客户回复确认邮件",
                    Description = "（默认停用，T000110）客户二次问答时仅通知客服，不再向客户回发其本人消息副本",
                    MailboxId = null,
                    IsActive = false,
                    SortOrder = 12,
                    TriggerType = "new_reply",
                    ConditionsJson = """{"matchType":"all","conditions":[]}""",
                    // type=send_email，value=邮件模板 slug，extra=收件人（customer=客户邮箱）
                    ActionsJson = """[{"type":"send_email","value":"customer-reply-default","extra":"customer"}]""",
                    CreatedAt = DateTime.UtcNow.AddDays(-1),
                    UpdatedAt = DateTime.UtcNow.AddDays(-1)
                },
                // 新增：对应 satisfaction-thanks 自定义模板
                // 触发时机：客户提交满意度评价后，向客户发送感谢邮件
                new Workflow
                {
                    Name = "满意度评价感谢邮件",
                    Description = "客户提交满意度评价后，自动调用 satisfaction-thanks 自定义邮件模板向客户发送感谢邮件",
                    MailboxId = null,
                    IsActive = true,
                    IsSystem = true,
                    SortOrder = 13,
                    TriggerType = "satisfaction_submitted",
                    ConditionsJson = """{"matchType":"all","conditions":[]}""",
                    // type=send_email，value=邮件模板 slug，extra=收件人（customer=客户邮箱）
                    ActionsJson = """[{"type":"send_email","value":"satisfaction-thanks","extra":"customer"}]""",
                    CreatedAt = DateTime.UtcNow.AddDays(-1),
                    UpdatedAt = DateTime.UtcNow.AddDays(-1)
                },
                // 新增：IM 渠道满意度评价感谢消息
                // 触发时机：客户提交满意度评价后，通过对应 App 向客户发送感谢消息
                // 仅对 IM 渠道（微信/飞书/WhatsApp/钉钉）生效，走 IM 消息队列，支持失败重试
                new Workflow
                {
                    Name = "[IM] 满意度评价感谢消息",
                    Description = "IM 渠道（微信/飞书/WhatsApp/钉钉）客户提交满意度评价后，通过对应 App 发送感谢消息。走 IM 消息队列，支持失败重试",
                    MailboxId = null,
                    IsActive = true,
                    IsSystem = true,
                    SortOrder = 13,
                    TriggerType = "satisfaction_submitted",
                    // matchType=any 实现 source_type IN (WeChat, Feishu, WhatsApp, DingTalk, Messenger, Telegram, Line, IMessage) 语义
                    ConditionsJson = """{"matchType":"any","conditions":[{"field":"source_type","operator":"equals","value":"WeChat"},{"field":"source_type","operator":"equals","value":"Feishu"},{"field":"source_type","operator":"equals","value":"WhatsApp"},{"field":"source_type","operator":"equals","value":"DingTalk"},{"field":"source_type","operator":"equals","value":"Messenger"},{"field":"source_type","operator":"equals","value":"Telegram"},{"field":"source_type","operator":"equals","value":"Line"},{"field":"source_type","operator":"equals","value":"IMessage"}]}""",
                    ActionsJson = """[{"type":"send_im_message","value":"im-satisfaction-thanks"}]""",
                    CreatedAt = DateTime.UtcNow.AddDays(-1),
                    UpdatedAt = DateTime.UtcNow.AddDays(-1)
                },
                // 新增：对应 conversation-escalation 自定义模板
                // 触发时机：新会话主题包含"升级/投诉/emergency"时，自动通知客服
                new Workflow
                {
                    Name = "会话升级通知邮件",
                    Description = "新会话主题包含「升级/投诉/emergency」时，自动调用 conversation-escalation 自定义邮件模板通知客服",
                    MailboxId = null,
                    IsActive = true,
                    SortOrder = 14,
                    TriggerType = "new_conversation",
                    ConditionsJson = """{"matchType":"any","conditions":[{"field":"subject","operator":"contains","value":"升级"},{"field":"subject","operator":"contains","value":"投诉"},{"field":"subject","operator":"contains","value":"emergency"}]}""",
                    // type=send_email，value=邮件模板 slug，extra=收件人（assignee=分配客服；如未分配则发给 admin@example.com）
                    ActionsJson = """[{"type":"assign_user","value":"1"},{"type":"send_email","value":"conversation-escalation","extra":"assignee,admin@example.com"}]""",
                    CreatedAt = DateTime.UtcNow.AddDays(-1),
                    UpdatedAt = DateTime.UtcNow.AddDays(-1)
                },
                // 需求2：在线聊天（Widget 渠道）客服主动点击"关闭会话"时触发
                //   → ChatSession.Status = awaiting_rating，RatingRequestedAt / RatingTimeoutAt 已被 ChatService.CloseSessionAsync 设置
                // 本系统工作流自动在聊天流里插入一条"感谢 + 满意度调查邀请"的 system message。
                new Workflow
                {
                    Name = "[聊天]关闭满意度消息",
                    Description = "需求2：在线聊天（Widget 渠道）客服点'关闭会话'后，会话进入 awaiting_rating 阶段，自动在聊天流内发送一条包含感谢 + 满意度调查的系统消息。内容来自 chat-close-requested 模板。",
                    MailboxId = null,
                    IsActive = true,
                    IsSystem = true,
                    SortOrder = 15,
                    TriggerType = "chat_close_requested",
                    ConditionsJson = """{"matchType":"all","conditions":[]}""",
                    // type=send_chat_message，value=聊天内消息模板的 MailTemplate.Slug
                    ActionsJson = """[{"type":"send_chat_message","value":"chat-close-requested"}]""",
                    CreatedAt = DateTime.UtcNow.AddDays(-1),
                    UpdatedAt = DateTime.UtcNow.AddDays(-1)
                },
                // 需求2：在线聊天（Widget 渠道）客户在 awaiting_rating 阶段提交评分（1-5 分 + 可选评论）后触发
                // 自动在聊天流里插入一条"感谢评价"的 system message。
                new Workflow
                {
                    Name = "[聊天]满意度评价感谢消息",
                    Description = "需求2：在线聊天客户在 awaiting_rating 阶段完成满意度评分后，自动在聊天流内发送感谢消息。内容来自 chat-satisfaction-thanks 模板。",
                    MailboxId = null,
                    IsActive = true,
                    IsSystem = true,
                    SortOrder = 16,
                    TriggerType = "chat_satisfaction_submitted",
                    ConditionsJson = """{"matchType":"all","conditions":[]}""",
                    ActionsJson = """[{"type":"send_chat_message","value":"chat-satisfaction-thanks"}]""",
                    CreatedAt = DateTime.UtcNow.AddDays(-1),
                    UpdatedAt = DateTime.UtcNow.AddDays(-1)
                }
            };

            var workflowsToAddFiltered = workflowsToAdd
                .Where(w => !existingWorkflowNames.Contains(w.Name))
                .ToList();
            if (workflowsToAddFiltered.Count > 0)
            {
                context.Workflows.AddRange(workflowsToAddFiltered);
                await context.SaveChangesAsync();
            }
        }
    }
}
