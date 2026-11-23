using NoneadCSS.Domain.Entities;
using NoneadCSS.Infrastructure.Persistence;

namespace NoneadCSS.Api.Seeders;

/// <summary>
/// 系统选项种子数据初始化器
/// 从 DatabaseSeeder 拆分而来，负责系统配置项的增量初始化。
/// </summary>
public static class SystemOptionSeeder
{
    /// <summary>
    /// 初始化系统配置种子数据。仅当 SystemOptions 表为空时执行。
    /// </summary>
    /// <param name="context">数据库上下文</param>
    public static async Task SeedAsync(NoneadCSSDbContext context)
    {
        // 系统配置种子数据
        if (!context.SystemOptions.Any())
        {
            var systemOptions = new[]
            {
                // 常规设置
                new SystemOption { Name = "app.name", Value = "拓德客户服务与支持系统", Category = "general", CreatedAt = DateTime.UtcNow, UpdatedAt = DateTime.UtcNow },
                new SystemOption { Name = "app.company_name", Value = "拓德科技", Category = "general", CreatedAt = DateTime.UtcNow, UpdatedAt = DateTime.UtcNow },
                new SystemOption { Name = "app.locale", Value = "zh-Hans", Category = "general", CreatedAt = DateTime.UtcNow, UpdatedAt = DateTime.UtcNow },
                new SystemOption { Name = "app.timezone", Value = "Asia/Shanghai", Category = "general", CreatedAt = DateTime.UtcNow, UpdatedAt = DateTime.UtcNow },
                new SystemOption { Name = "app.currency", Value = "CNY", Category = "general", CreatedAt = DateTime.UtcNow, UpdatedAt = DateTime.UtcNow },
                new SystemOption { Name = "app.date_format", Value = "yyyy-MM-dd", Category = "general", CreatedAt = DateTime.UtcNow, UpdatedAt = DateTime.UtcNow },
                new SystemOption { Name = "app.time_format", Value = "HH:mm:ss", Category = "general", CreatedAt = DateTime.UtcNow, UpdatedAt = DateTime.UtcNow },

                // 邮件设置
                new SystemOption { Name = "mail.from_name", Value = "nCSS 客服系统", Category = "email", CreatedAt = DateTime.UtcNow, UpdatedAt = DateTime.UtcNow },
                new SystemOption { Name = "mail.fetch_frequency", Value = "5", Category = "email", CreatedAt = DateTime.UtcNow, UpdatedAt = DateTime.UtcNow },
                new SystemOption { Name = "mail.max_fetch_per_run", Value = "50", Category = "email", CreatedAt = DateTime.UtcNow, UpdatedAt = DateTime.UtcNow },
                new SystemOption { Name = "mail.delete_after_fetch", Value = "false", Category = "email", CreatedAt = DateTime.UtcNow, UpdatedAt = DateTime.UtcNow },
                new SystemOption { Name = "mail.mark_as_read", Value = "true", Category = "email", CreatedAt = DateTime.UtcNow, UpdatedAt = DateTime.UtcNow },
                new SystemOption { Name = "mail.save_to_sent", Value = "true", Category = "email", CreatedAt = DateTime.UtcNow, UpdatedAt = DateTime.UtcNow },

                // 安全设置：密码策略种子值必须与 PasswordPolicyService 的代码默认值一致
                // （DefaultMinLength=10 且大小写/数字/符号全开）。
                // 历史缺陷：此处曾种子化为 6 位 + 复杂度全 false，显式覆盖了服务层默认值，
                // 导致新部署的有效策略退化为"6 位、无复杂度要求"（审计报告标记的中危项）。
                new SystemOption { Name = "security.password_min_length", Value = "10", Category = "security", CreatedAt = DateTime.UtcNow, UpdatedAt = DateTime.UtcNow },
                new SystemOption { Name = "security.password_requires_uppercase", Value = "true", Category = "security", CreatedAt = DateTime.UtcNow, UpdatedAt = DateTime.UtcNow },
                new SystemOption { Name = "security.password_requires_lowercase", Value = "true", Category = "security", CreatedAt = DateTime.UtcNow, UpdatedAt = DateTime.UtcNow },
                new SystemOption { Name = "security.password_requires_number", Value = "true", Category = "security", CreatedAt = DateTime.UtcNow, UpdatedAt = DateTime.UtcNow },
                new SystemOption { Name = "security.password_requires_symbol", Value = "true", Category = "security", CreatedAt = DateTime.UtcNow, UpdatedAt = DateTime.UtcNow },
                new SystemOption { Name = "security.session_timeout", Value = "120", Category = "security", CreatedAt = DateTime.UtcNow, UpdatedAt = DateTime.UtcNow },
                new SystemOption { Name = "security.max_login_attempts", Value = "5", Category = "security", CreatedAt = DateTime.UtcNow, UpdatedAt = DateTime.UtcNow },
                new SystemOption { Name = "security.allow_registration", Value = "false", Category = "security", CreatedAt = DateTime.UtcNow, UpdatedAt = DateTime.UtcNow },

                // 通知设置
                new SystemOption { Name = "notify.new_conversation", Value = "true", Category = "notification", CreatedAt = DateTime.UtcNow, UpdatedAt = DateTime.UtcNow },
                new SystemOption { Name = "notify.new_reply", Value = "true", Category = "notification", CreatedAt = DateTime.UtcNow, UpdatedAt = DateTime.UtcNow },
                new SystemOption { Name = "notify.customer_reply", Value = "true", Category = "notification", CreatedAt = DateTime.UtcNow, UpdatedAt = DateTime.UtcNow },
                new SystemOption { Name = "notify.mention", Value = "true", Category = "notification", CreatedAt = DateTime.UtcNow, UpdatedAt = DateTime.UtcNow },
                new SystemOption { Name = "notify.assignment", Value = "true", Category = "notification", CreatedAt = DateTime.UtcNow, UpdatedAt = DateTime.UtcNow },

                // 会话设置
                new SystemOption { Name = "conversation.default_status", Value = "active", Category = "conversation", CreatedAt = DateTime.UtcNow, UpdatedAt = DateTime.UtcNow },
                new SystemOption { Name = "conversation.auto_close_hours", Value = "168", Category = "conversation", CreatedAt = DateTime.UtcNow, UpdatedAt = DateTime.UtcNow },
                new SystemOption { Name = "conversation.allow_delete", Value = "true", Category = "conversation", CreatedAt = DateTime.UtcNow, UpdatedAt = DateTime.UtcNow },
                new SystemOption { Name = "conversation.show_thread_time", Value = "absolute", Category = "conversation", CreatedAt = DateTime.UtcNow, UpdatedAt = DateTime.UtcNow },
                new SystemOption { Name = "conversation.signature_position", Value = "before_quote", Category = "conversation", CreatedAt = DateTime.UtcNow, UpdatedAt = DateTime.UtcNow },
            };
            context.SystemOptions.AddRange(systemOptions);
            await context.SaveChangesAsync();
        }

        // 增量补全：确保各渠道的关键配置项存在（已有数据库也能补全缺失记录）
        await EnsureChannelOptionsAsync(context);
    }

    /// <summary>
    /// 增量补全各渠道（微信/钉钉/飞书/WhatsApp/Messenger/Telegram/LINE/iMessage）的 contact_collection_enabled 等关键配置项。
    /// 仅插入缺失的记录，不覆盖已有值。
    /// </summary>
    private static async Task EnsureChannelOptionsAsync(NoneadCSSDbContext context)
    {
        var defaults = new (string Name, string Value, string Category)[]
        {
            // 客户信息收集开关（默认开启）
            ("wechat.contact_collection_enabled", "true", "wechat"),
            ("dingding.contact_collection_enabled", "true", "dingding"),
            ("feishu.contact_collection_enabled", "true", "feishu"),
            ("whatsapp.contact_collection_enabled", "true", "whatsapp"),
            ("messenger.contact_collection_enabled", "true", "messenger"),
            ("telegram.contact_collection_enabled", "true", "telegram"),
            ("line.contact_collection_enabled", "true", "line"),
            ("imessage.contact_collection_enabled", "true", "imessage"),
        };

        foreach (var (name, value, category) in defaults)
        {
            if (!context.SystemOptions.Any(o => o.Name == name))
            {
                context.SystemOptions.Add(new SystemOption
                {
                    Name = name,
                    Value = value,
                    Category = category,
                    CreatedAt = DateTime.UtcNow,
                    UpdatedAt = DateTime.UtcNow
                });
            }
        }

        await context.SaveChangesAsync();
    }
}
