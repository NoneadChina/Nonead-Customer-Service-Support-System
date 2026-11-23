using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Diagnostics;
using Microsoft.Extensions.Logging;
using System.Text.RegularExpressions;
using NoneadCSS.Application.Common;

namespace NoneadCSS.Infrastructure.Persistence;

/// <summary>
/// EF Core SaveChanges 拦截器：对入库字符串做 HTML 消毒（入站纵深防御）。
/// 服务层已通过 HtmlSanitizerHelper 对业务富文本做白名单消毒，本拦截器作为兜底，
/// 拦截未经服务层、直接写入 DbContext 的内容（如仓储直写、种子数据、迁移脚本）。
/// 仅对"明显包含完整 HTML 标签"的字符串触发消毒，保留纯文本原样，避免误编码。
/// 敏感属性（密码、令牌、密钥等）一律跳过，防御性不对其做任何变换。
/// </summary>
public partial class HtmlSanitizingSaveChangesInterceptor : SaveChangesInterceptor
{
    private readonly ILogger<HtmlSanitizingSaveChangesInterceptor> _logger;

    /// <summary>
    /// 敏感属性名黑名单：这些字段可能是凭据/令牌/密钥，消毒可能破坏其语义，必须跳过。
    /// </summary>
    private static readonly HashSet<string> SensitiveProperties = new(StringComparer.OrdinalIgnoreCase)
    {
        "Password", "PasswordHash", "RefreshToken", "AccessToken",
        "OutPassword", "InPassword", "OauthAccessToken", "OauthRefreshToken",
        "Secret", "ApiKey", "ApiSecret", "ConnectionString", "Token",
        "SetupToken", "SecretKey", "PrivateKey", "Signature", "Hash"
    };

    /// <summary>
    /// 出站邮件载荷白名单（实体名.属性名）：入库时跳过兜底消毒。
    /// T000051：SendLog.Body 是"已组装完成的出站邮件完整 HTML"——
    /// 由受信模板 + 已消毒消息内容（HtmlSanitizerHelper.Sanitize 在收取/富文本入口已执行）+ HTML 编码变量渲染而成。
    /// 若在此再次执行 HtmlSanitizer 兜底消毒，会：
    ///   1. 剥离通知模板自带的内联样式（style 不在白名单）→ 邮件外观/布局被破坏；
    ///   2. 用 AngleSharp 重新解析并把 &lt;p&gt;&lt;div&gt;…&lt;/div&gt;&lt;/p&gt; 这类非法嵌套自动闭合为
    ///      &lt;p&gt;&lt;/p&gt;&lt;div&gt;…&lt;/div&gt;&lt;p&gt;&lt;/p&gt;，向收件人泄漏空段/标签痕迹。
    /// 出站正文的消毒职责已由各消息入口（邮件收取、富文本回复等）承担，此处仅需保持入站纵深防御。
    /// </summary>
    private static readonly HashSet<string> OutboundEmailPayloadProperties = new(StringComparer.OrdinalIgnoreCase)
    {
        "SendLog.Body"
    };

    /// <summary>
    /// 识别"明显包含 HTML 标签"的字符串：
    /// 匹配 <tag ...> 、</tag> 这类完整标签。纯文本中的孤立 &lt;（如 "a &lt; b"）不会命中，
    /// 从而避免对非富文本字段做转义。
    /// </summary>
    private static readonly Regex HtmlTagRegex = new(
        @"<[a-zA-Z][^>]*>|</[a-zA-Z][^>]*>",
        RegexOptions.Compiled);

    public HtmlSanitizingSaveChangesInterceptor(ILogger<HtmlSanitizingSaveChangesInterceptor> logger)
    {
        _logger = logger;
    }

    public override InterceptionResult<int> SavingChanges(DbContextEventData eventData, InterceptionResult<int> result)
    {
        SanitizeHtmlProperties(eventData.Context);
        return base.SavingChanges(eventData, result);
    }

    public override ValueTask<InterceptionResult<int>> SavingChangesAsync(
        DbContextEventData eventData, InterceptionResult<int> result, CancellationToken cancellationToken = default)
    {
        SanitizeHtmlProperties(eventData.Context);
        return base.SavingChangesAsync(eventData, result, cancellationToken);
    }

    private void SanitizeHtmlProperties(DbContext? context)
    {
        if (context == null)
        {
            return;
        }

        try
        {
            foreach (var entry in context.ChangeTracker.Entries())
            {
                if (entry.State != EntityState.Added && entry.State != EntityState.Modified)
                {
                    continue;
                }

                foreach (var property in entry.Properties)
                {
                    // 仅处理字符串属性，且忽略导航属性的 CurrentValue（此时是实体而非文本）
                    if (property.CurrentValue is not string value || string.IsNullOrWhiteSpace(value))
                    {
                        continue;
                    }

                    if (SensitiveProperties.Contains(property.Metadata.Name))
                    {
                        continue;
                    }

                    // 出站邮件载荷跳过消毒（T000051，原因见 OutboundEmailPayloadProperties 注释）
                    var entityPropertyKey = $"{entry.Metadata.ClrType.Name}.{property.Metadata.Name}";
                    if (OutboundEmailPayloadProperties.Contains(entityPropertyKey))
                    {
                        continue;
                    }

                    // 仅对包含完整 HTML 标签的内容消毒，避免对纯文本误编码
                    if (!HtmlTagRegex.IsMatch(value))
                    {
                        continue;
                    }

                    var sanitized = HtmlSanitizerHelper.Sanitize(value, _logger);
                    if (!string.Equals(sanitized, value, StringComparison.Ordinal))
                    {
                        _logger.LogDebug(
                            "[HtmlSanitizer] SaveChanges 兜底消毒：实体类型={EntityType}, 属性={Property}, 长度 {OldLen}->{NewLen}",
                            entry.Metadata.ClrType.Name, property.Metadata.Name, value.Length, sanitized.Length);
                        property.CurrentValue = sanitized;
                    }
                }
            }
        }
        catch (Exception ex)
        {
            // 消毒失败不阻断主事务，仅记录告警
            _logger.LogError(ex, "HTML 消毒拦截器执行失败，跳过本次消毒（不影响主事务）");
        }
    }
}
