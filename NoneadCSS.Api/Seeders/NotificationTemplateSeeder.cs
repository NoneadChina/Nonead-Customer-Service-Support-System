using NoneadCSS.Domain.Entities;
using NoneadCSS.Infrastructure.Persistence;

namespace NoneadCSS.Api.Seeders;

/// <summary>
/// 通知模板种子数据初始化器
/// 从 DatabaseSeeder 拆分而来，负责 NotificationTemplate 的增量补充。
/// 注意：本 Seeder 仅将模板加入 DbContext 的变更跟踪器，不调用 SaveChangesAsync。
/// 调用方需在 NotificationRuleSeeder 执行完毕后统一保存，以保持与原 DatabaseSeeder 相同的
/// 增量 seeding 行为（首次启动仅创建模板，再次启动后规则才能查询到模板并创建关联）。
/// </summary>
public static class NotificationTemplateSeeder
{
    /// <summary>
    /// 增量添加通知模板（按 EventType 检查，确保每次启动都补全缺失的模板）。
    /// </summary>
    /// <param name="context">数据库上下文</param>
    public static async Task SeedAsync(NoneadCSSDbContext context)
    {
        // 通知模板种子数据 - 增量添加，确保每次启动都检查
        var existingTemplateEventTypes = context.NotificationTemplates.Select(t => t.EventType).ToList();

        if (!existingTemplateEventTypes.Contains("new_conversation"))
        {
            context.NotificationTemplates.Add(new NotificationTemplate
            {
                Name = "新会话通知",
                Subject = "[{mailbox_name}] 新会话 #{conversation_id}: {subject}",
                Body = """
                    <p>您好，</p>
                    <p>有新的会话需要处理：</p>
                    <ul>
                        <li>会话编号：#{conversation_id}</li>
                        <li>主题：{subject}</li>
                        <li>客户：{customer_name}</li>
                        <li>邮箱：{mailbox_name}</li>
                    </ul>
                    <p><a href="{conversation_url}">点击查看会话</a></p>
                    <p>此致<br/>
                    {company_name} 客服系统</p>
                    """,
                EventType = "new_conversation",
                IsActive = true,
                CreatedAt = DateTime.UtcNow,
                UpdatedAt = DateTime.UtcNow
            });
        }

        if (!existingTemplateEventTypes.Contains("new_reply"))
        {
            context.NotificationTemplates.Add(new NotificationTemplate
            {
                Name = "新回复通知",
                Subject = "[{mailbox_name}] 新回复 #{conversation_id}: {subject}",
                Body = """
                    <p>您好，</p>
                    <p>会话 #{conversation_id} 有新回复：</p>
                    <ul>
                        <li>主题：{subject}</li>
                        <li>回复人：{replier_name}</li>
                        <li>客户：{customer_name}</li>
                    </ul>
                    <p><a href="{conversation_url}">点击查看会话</a></p>
                    <p>此致<br/>
                    {company_name} 客服系统</p>
                    """,
                EventType = "new_reply",
                IsActive = true,
                CreatedAt = DateTime.UtcNow,
                UpdatedAt = DateTime.UtcNow
            });
        }

        if (!existingTemplateEventTypes.Contains("conversation_assigned"))
        {
            context.NotificationTemplates.Add(new NotificationTemplate
            {
                Name = "会话分配通知",
                Subject = "[{mailbox_name}] 会话已分配 #{conversation_id}",
                Body = """
                    <p>您好 {assignee_name}，</p>
                    <p>以下会话已分配给您处理：</p>
                    <ul>
                        <li>会话编号：#{conversation_id}</li>
                        <li>主题：{subject}</li>
                        <li>客户：{customer_name}</li>
                        <li>分配人：{assigner_name}</li>
                    </ul>
                    <p><a href="{conversation_url}">点击查看会话</a></p>
                    <p>此致<br/>
                    {company_name} 客服系统</p>
                    """,
                EventType = "conversation_assigned",
                IsActive = true,
                CreatedAt = DateTime.UtcNow,
                UpdatedAt = DateTime.UtcNow
            });
        }

        if (!existingTemplateEventTypes.Contains("customer_reply"))
        {
            context.NotificationTemplates.Add(new NotificationTemplate
            {
                Name = "客户回复通知",
                Subject = "[{mailbox_name}] 客户回复 #{conversation_id}: {subject}",
                Body = """
                    <p>您好，</p>
                    <p>客户回复了会话 #{conversation_id}：</p>
                    <ul>
                        <li>主题：{subject}</li>
                        <li>客户：{customer_name}</li>
                    </ul>
                    <p><a href="{conversation_url}">点击查看会话</a></p>
                    <p>此致<br/>
                    {company_name} 客服系统</p>
                    """,
                EventType = "customer_reply",
                IsActive = true,
                CreatedAt = DateTime.UtcNow,
                UpdatedAt = DateTime.UtcNow
            });
        }

        if (!existingTemplateEventTypes.Contains("conversation_closed"))
        {
            context.NotificationTemplates.Add(new NotificationTemplate
            {
                Name = "会话关闭通知",
                Subject = "[{mailbox_name}] 会话已关闭 #{conversation_id}: {subject}",
                Body = """
                    <p>您好，</p>
                    <p>会话 #{conversation_id} 已关闭：</p>
                    <ul>
                        <li>主题：{subject}</li>
                        <li>客户：{customer_name}</li>
                        <li>关闭人：{closer_name}</li>
                    </ul>
                    <p><a href="{conversation_url}">点击查看会话</a></p>
                    <p>此致<br/>
                    {company_name} 客服系统</p>
                    """,
                EventType = "conversation_closed",
                IsActive = true,
                CreatedAt = DateTime.UtcNow,
                UpdatedAt = DateTime.UtcNow
            });
        }

        if (!existingTemplateEventTypes.Contains("conversation_updated"))
        {
            context.NotificationTemplates.Add(new NotificationTemplate
            {
                Name = "会话更新通知",
                Subject = "[{mailbox_name}] 会话已更新 #{conversation_id}: {subject}",
                Body = """
                    <p>您好，</p>
                    <p>会话 #{conversation_id} 已更新：</p>
                    <ul>
                        <li>主题：{subject}</li>
                        <li>客户：{customer_name}</li>
                        <li>更新人：{updater_name}</li>
                        <li>更新内容：{update_content}</li>
                    </ul>
                    <p><a href="{conversation_url}">点击查看会话</a></p>
                    <p>此致<br/>
                    {company_name} 客服系统</p>
                    """,
                EventType = "conversation_updated",
                IsActive = true,
                CreatedAt = DateTime.UtcNow,
                UpdatedAt = DateTime.UtcNow
            });
        }

        if (!existingTemplateEventTypes.Contains("mention"))
        {
            context.NotificationTemplates.Add(new NotificationTemplate
            {
                Name = "被提及通知",
                Subject = "[{mailbox_name}] 有人在会话中提到了您 #{conversation_id}",
                Body = """
                    <p>您好 {user_name}，</p>
                    <p>{mentioner_name} 在会话中提到了您：</p>
                    <ul>
                        <li>会话编号：#{conversation_id}</li>
                        <li>主题：{subject}</li>
                    </ul>
                    <p><a href="{conversation_url}">点击查看会话</a></p>
                    <p>此致<br/>
                    {company_name} 客服系统</p>
                    """,
                EventType = "mention",
                IsActive = true,
                CreatedAt = DateTime.UtcNow,
                UpdatedAt = DateTime.UtcNow
            });
        }

        if (!existingTemplateEventTypes.Contains("sla_violation"))
        {
            context.NotificationTemplates.Add(new NotificationTemplate
            {
                Name = "SLA 违规通知",
                Subject = "[{mailbox_name}] ⚠️ SLA 违规 #{conversation_id}",
                Body = """
                    <p>您好，</p>
                    <p>以下会话已违反 SLA 政策：</p>
                    <ul>
                        <li>会话编号：#{conversation_id}</li>
                        <li>主题：{subject}</li>
                        <li>SLA 政策：{sla_name}</li>
                        <li>违规类型：{violation_type}</li>
                        <li>截止时间：{deadline}</li>
                    </ul>
                    <p><a href="{conversation_url}">点击查看会话</a></p>
                    <p>此致<br/>
                    {company_name} 客服系统</p>
                    """,
                EventType = "sla_violation",
                IsActive = true,
                CreatedAt = DateTime.UtcNow,
                UpdatedAt = DateTime.UtcNow
            });
        }

        // ===== 新增通知模板 =====
        if (!existingTemplateEventTypes.Contains("sla_warning"))
        {
            context.NotificationTemplates.Add(new NotificationTemplate
            {
                Name = "SLA 即将超时预警",
                Subject = "[{mailbox_name}] ⚡ SLA 即将超时 #{conversation_id}",
                Body = """
                    <p>您好，</p>
                    <p>以下会话即将超出 SLA 响应时限，请及时处理：</p>
                    <ul>
                        <li>会话编号：#{conversation_id}</li>
                        <li>主题：{subject}</li>
                        <li>客户：{customer_name}</li>
                        <li>SLA 等级：{sla_level}</li>
                        <li>预计超时时间：{sla_deadline}</li>
                        <li>剩余时间：{remaining_time}</li>
                    </ul>
                    <p><a href="{conversation_url}">点击查看会话</a></p>
                    <p style="color:#f59e0b;font-size:12px;">请在超时前回复客户，避免 SLA 违规。</p>
                    <p>此致<br/>
                    {company_name} 客服系统</p>
                    """,
                EventType = "sla_warning",
                IsActive = true,
                CreatedAt = DateTime.UtcNow,
                UpdatedAt = DateTime.UtcNow
            });
        }

        if (!existingTemplateEventTypes.Contains("customer_satisfaction"))
        {
            context.NotificationTemplates.Add(new NotificationTemplate
            {
                Name = "客户满意度评价邀请",
                Subject = "[服务评价/Service Rating] 请为我们的服务打分 #{conversation_id}",
                Body = """
                    <div style="max-width:600px;margin:0 auto;font-family:-apple-system,BlinkMacSystemFont,'Segoe UI',Roboto,'Helvetica Neue',Arial,'PingFang SC','Microsoft YaHei',sans-serif;">
                      <div style="background:linear-gradient(135deg,#4f46e0 0%,#7c3aed 100%);padding:24px 32px;border-radius:12px 12px 0 0;">
                        <h1 style="margin:0;color:#fff;font-size:18px;font-weight:600;letter-spacing:0.5px;">服务满意度评价邀请 / Satisfaction Rating Invitation</h1>
                      </div>
                      <div style="background:#ffffff;padding:28px 32px;border:1px solid #e5e7eb;border-top:none;">
                        <p style="margin:0 0 14px;font-size:15px;line-height:1.7;color:#1f2937;">尊敬的 {customer_name}，您好！</p>
                        <p style="margin:0 0 16px;font-size:15px;line-height:1.7;color:#1f2937;">感谢您与我们的客服团队沟通。为了不断提升我们的服务质量，诚邀您对本次服务进行评价：</p>
                        <p style="margin:0 0 18px;font-size:16px;font-weight:600;text-align:center;color:#1f2937;">您对本次服务满意吗？</p>
                        <table width="100%" cellpadding="0" cellspacing="0" border="0" style="margin:0 0 20px;">
                          <tr>
                            <td align="center" style="padding:6px 4px;">
                              <a href="{rating_url}?rating=5" style="display:block;width:100%;max-width:180px;padding:12px 8px;background:#16a34a;color:#fff;text-decoration:none;border-radius:10px;font-size:13px;font-weight:600;line-height:1.4;">😍 远超期望</a>
                            </td>
                            <td align="center" style="padding:6px 4px;">
                              <a href="{rating_url}?rating=4" style="display:block;width:100%;max-width:180px;padding:12px 8px;background:#059669;color:#fff;text-decoration:none;border-radius:10px;font-size:13px;font-weight:600;line-height:1.4;">😊 物有所值</a>
                            </td>
                          </tr>
                          <tr>
                            <td align="center" style="padding:6px 4px;">
                              <a href="{rating_url}?rating=3" style="display:block;width:100%;max-width:180px;padding:12px 8px;background:#d97706;color:#fff;text-decoration:none;border-radius:10px;font-size:13px;font-weight:600;line-height:1.4;">😐 中规中矩</a>
                            </td>
                            <td align="center" style="padding:6px 4px;">
                              <a href="{rating_url}?rating=2" style="display:block;width:100%;max-width:180px;padding:12px 8px;background:#ea580c;color:#fff;text-decoration:none;border-radius:10px;font-size:13px;font-weight:600;line-height:1.4;">😟 略有失望</a>
                            </td>
                          </tr>
                          <tr>
                            <td align="center" colspan="2" style="padding:6px 4px;">
                              <a href="{rating_url}?rating=1" style="display:inline-block;padding:12px 32px;background:#dc2626;color:#fff;text-decoration:none;border-radius:10px;font-size:13px;font-weight:600;line-height:1.4;">😡 无法接受</a>
                            </td>
                          </tr>
                        </table>
                        <p style="margin:0 0 16px;font-size:13px;line-height:1.6;color:#475569;text-align:center;">点击对应选项即可完成评价</p>
                        <p style="margin:0 0 16px;font-size:15px;line-height:1.7;color:#1f2937;">您的每一条反馈都对我们非常重要。<a href="{feedback_url}" style="color:#4f46e0;text-decoration:underline;">填写详细反馈</a></p>
                        <hr style="border:none;border-top:1px solid #e5e7eb;margin:24px 0;" />
                        <p style="margin:0 0 6px;font-size:15px;line-height:1.7;color:#6b7280;">Dear {customer_name},</p>
                        <p style="margin:0 0 16px;font-size:15px;line-height:1.7;color:#6b7280;">Thank you for communicating with our customer support team. To help us continually improve our service quality, we sincerely invite you to rate your experience:</p>
                        <p style="margin:0 0 18px;font-size:16px;font-weight:600;text-align:center;color:#6b7280;">How was our service?</p>
                        <table width="100%" cellpadding="0" cellspacing="0" border="0" style="margin:0 0 20px;">
                          <tr>
                            <td align="center" style="padding:6px 4px;">
                              <a href="{rating_url}?rating=5" style="display:block;width:100%;max-width:180px;padding:12px 8px;background:#16a34a;color:#fff;text-decoration:none;border-radius:10px;font-size:13px;font-weight:600;line-height:1.4;">😍 Exceeds Expectations</a>
                            </td>
                            <td align="center" style="padding:6px 4px;">
                              <a href="{rating_url}?rating=4" style="display:block;width:100%;max-width:180px;padding:12px 8px;background:#059669;color:#fff;text-decoration:none;border-radius:10px;font-size:13px;font-weight:600;line-height:1.4;">😊 Good Value</a>
                            </td>
                          </tr>
                          <tr>
                            <td align="center" style="padding:6px 4px;">
                              <a href="{rating_url}?rating=3" style="display:block;width:100%;max-width:180px;padding:12px 8px;background:#d97706;color:#fff;text-decoration:none;border-radius:10px;font-size:13px;font-weight:600;line-height:1.4;">😐 Average</a>
                            </td>
                            <td align="center" style="padding:6px 4px;">
                              <a href="{rating_url}?rating=2" style="display:block;width:100%;max-width:180px;padding:12px 8px;background:#ea580c;color:#fff;text-decoration:none;border-radius:10px;font-size:13px;font-weight:600;line-height:1.4;">😟 Disappointing</a>
                            </td>
                          </tr>
                          <tr>
                            <td align="center" colspan="2" style="padding:6px 4px;">
                              <a href="{rating_url}?rating=1" style="display:inline-block;padding:12px 32px;background:#dc2626;color:#fff;text-decoration:none;border-radius:10px;font-size:13px;font-weight:600;line-height:1.4;">😡 Unacceptable</a>
                            </td>
                          </tr>
                        </table>
                        <p style="margin:0;font-size:13px;line-height:1.6;color:#6b7280;text-align:center;">Click an option above to complete your rating.</p>
                      </div>
                      <div style="background:#f9fafb;padding:18px 32px;border-radius:0 0 12px 12px;text-align:center;">
                        <p style="margin:0;font-size:13px;color:#6b7280;line-height:1.6;">{company_name} 客服团队 / Customer Support Team</p>
                        <p style="margin:4px 0 0;font-size:12px;color:#9ca3af;">此邮件为系统自动发送，请勿直接回复 / This is an automated email, please do not reply directly.</p>
                      </div>
                    </div>
                    """,
                EventType = "customer_satisfaction",
                IsActive = true,
                CreatedAt = DateTime.UtcNow,
                UpdatedAt = DateTime.UtcNow
            });
        }

        if (!existingTemplateEventTypes.Contains("auto_reply"))
        {
            context.NotificationTemplates.Add(new NotificationTemplate
            {
                Name = "自动回复 - 邮件已收到",
                Subject = "我们已收到您的来信 #{conversation_id}: {subject}",
                Body = """
                    <p>尊敬的 {customer_name}，您好！</p>
                    <p>感谢您联系 {company_name} 客服中心。我们已收到您的邮件，工单编号为 <strong>#{conversation_id}</strong>。</p>
                    <div style="background:#f0f9ff;border:1px solid #bae6fd;border-radius:8px;padding:16px;margin:16px 0;">
                        <p style="margin:0 0 8px 0;"><strong>📩 您的来信信息：</strong></p>
                        <p style="margin:4px 0;">主题：{subject}</p>
                        <p style="margin:4px 0;">收件时间：{received_time}</p>
                        <p style="margin:4px 0;">预计响应时间：{sla_response_time} 内</p>
                    </div>
                    <p>我们的客服团队将在第一时间为您处理，请您耐心等待。</p>
                    <p style="color:#6b7280;font-size:12px;">此邮件为系统自动发送，请勿直接回复。</p>
                    <p>此致<br/>
                    {company_name} 客服团队</p>
                    """,
                EventType = "auto_reply",
                IsActive = true,
                CreatedAt = DateTime.UtcNow,
                UpdatedAt = DateTime.UtcNow
            });
        }

        if (!existingTemplateEventTypes.Contains("conversation_timeout"))
        {
            context.NotificationTemplates.Add(new NotificationTemplate
            {
                Name = "会话超时未响应提醒",
                Subject = "⏰ 会话超时未响应 #{conversation_id}: {subject}",
                Body = """
                    <p>您好 {assigned_to}，</p>
                    <p>以下会话客户已等待超过 <strong>{timeout_minutes} 分钟</strong>，尚未收到您的回复：</p>
                    <ul>
                        <li>会话编号：#{conversation_id}</li>
                        <li>主题：{subject}</li>
                        <li>客户：{customer_name}</li>
                        <li>最后回复时间：{last_reply_time}</li>
                        <li>等待时长：{waiting_time}</li>
                    </ul>
                    <p style="color:#dc2626;">请尽快回复客户，避免影响客户体验和 SLA 指标。</p>
                    <p><a href="{conversation_url}">立即处理</a></p>
                    <p>此致<br/>
                    {company_name} 客服系统</p>
                    """,
                EventType = "conversation_timeout",
                IsActive = true,
                CreatedAt = DateTime.UtcNow,
                UpdatedAt = DateTime.UtcNow
            });
        }

        if (!existingTemplateEventTypes.Contains("ticket_escalated"))
        {
            context.NotificationTemplates.Add(new NotificationTemplate
            {
                Name = "工单升级通知",
                Subject = "⬆️ 工单已升级 #{conversation_id}: {subject}",
                Body = """
                    <p>您好，</p>
                    <p>以下会话已被升级，请优先处理：</p>
                    <ul>
                        <li>会话编号：#{conversation_id}</li>
                        <li>主题：{subject}</li>
                        <li>客户：{customer_name}</li>
                        <li>优先级：<strong style="color:#dc2626;">{priority}</strong></li>
                        <li>升级人：{escalated_by}</li>
                        <li>升级原因：{escalation_reason}</li>
                        <li>升级时间：{escalated_at}</li>
                    </ul>
                    <p><a href="{conversation_url}">点击查看会话</a></p>
                    <p>此致<br/>
                    {company_name} 客服系统</p>
                    """,
                EventType = "ticket_escalated",
                IsActive = true,
                CreatedAt = DateTime.UtcNow,
                UpdatedAt = DateTime.UtcNow
            });
        }

        if (!existingTemplateEventTypes.Contains("user_welcome"))
        {
            context.NotificationTemplates.Add(new NotificationTemplate
            {
                Name = "新用户欢迎邮件",
                Subject = "欢迎使用 {company_name} 客服系统",
                Body = """
                    <p>尊敬的 {user_name}，您好！</p>
                    <p>欢迎您加入 {company_name} 客服系统！您的账户已创建成功。</p>
                    <div style="background:#f0fdf4;border:1px solid #bbf7d0;border-radius:8px;padding:16px;margin:16px 0;">
                        <p style="margin:0 0 8px 0;"><strong>✅ 账户信息：</strong></p>
                        <p style="margin:4px 0;">用户名：{user_name}</p>
                        <p style="margin:4px 0;">邮箱：{user_email}</p>
                        <p style="margin:4px 0;">角色：{user_role}</p>
                    </div>
                    <p>您可以点击下方链接登录系统：</p>
                    <p><a href="{login_url}" style="display:inline-block;padding:10px 24px;background:#2563eb;color:#fff;text-decoration:none;border-radius:6px;">立即登录</a></p>
                    <p>如有任何问题，请联系系统管理员。</p>
                    <p>此致<br/>
                    {company_name} 团队</p>
                    """,
                EventType = "user_welcome",
                IsActive = true,
                CreatedAt = DateTime.UtcNow,
                UpdatedAt = DateTime.UtcNow
            });
        }

        if (!existingTemplateEventTypes.Contains("password_reset"))
        {
            context.NotificationTemplates.Add(new NotificationTemplate
            {
                Name = "密码重置通知",
                Subject = "密码重置请求 - {company_name} 客服系统",
                Body = """
                    <p>您好 {user_name}，</p>
                    <p>我们收到了您的密码重置请求。请点击下方链接重置密码：</p>
                    <p style="text-align:center;margin:24px 0;">
                        <a href="{reset_url}" style="display:inline-block;padding:12px 32px;background:#2563eb;color:#fff;text-decoration:none;border-radius:6px;font-weight:600;">重置密码</a>
                    </p>
                    <p>此链接将在 <strong>{expire_time}</strong> 后失效。</p>
                    <p style="background:#fef3c7;border:1px solid #fcd34d;border-radius:6px;padding:12px;font-size:13px;color:#92400e;">
                        <strong>⚠️ 安全提示：</strong>如果您没有请求重置密码，请忽略此邮件。您的密码不会被更改。
                    </p>
                    <p>此致<br/>
                    {company_name} 客服系统</p>
                    """,
                EventType = "password_reset",
                IsActive = true,
                CreatedAt = DateTime.UtcNow,
                UpdatedAt = DateTime.UtcNow
            });
        }

        if (!existingTemplateEventTypes.Contains("bulk_import_completed"))
        {
            context.NotificationTemplates.Add(new NotificationTemplate
            {
                Name = "批量导入完成通知",
                Subject = "批量导入完成 - 共 {total_count} 条数据",
                Body = """
                    <p>您好 {user_name}，</p>
                    <p>您的批量导入任务已完成。以下是导入结果：</p>
                    <div style="background:#f8fafc;border:1px solid #e2e8f0;border-radius:8px;padding:16px;margin:16px 0;">
                        <div style="display:flex;justify-content:space-between;border-bottom:1px solid #e2e8f0;padding:8px 0;">
                            <span>总记录数</span><strong>{total_count}</strong>
                        </div>
                        <div style="display:flex;justify-content:space-between;border-bottom:1px solid #e2e8f0;padding:8px 0;color:#16a34a;">
                            <span>成功导入</span><strong>{success_count}</strong>
                        </div>
                        <div style="display:flex;justify-content:space-between;padding:8px 0;color:#dc2626;">
                            <span>失败数量</span><strong>{failed_count}</strong>
                        </div>
                    </div>
                    <p><a href="{error_report_url}">查看错误详情报告</a></p>
                    <p>此致<br/>
                    {company_name} 客服系统</p>
                    """,
                EventType = "bulk_import_completed",
                IsActive = true,
                CreatedAt = DateTime.UtcNow,
                UpdatedAt = DateTime.UtcNow
            });
        }

        await Task.CompletedTask;
    }
}
