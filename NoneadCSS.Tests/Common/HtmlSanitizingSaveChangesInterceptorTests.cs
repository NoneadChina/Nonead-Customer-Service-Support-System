using FluentAssertions;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging.Abstractions;
using NoneadCSS.Domain.Entities;
using NoneadCSS.Domain.Enums;
using NoneadCSS.Infrastructure.Persistence;
using NoneadCSS.Infrastructure.Repositories;
using Xunit;

namespace NoneadCSS.Tests.Common;

/// <summary>
/// T000051：HtmlSanitizingSaveChangesInterceptor 出站邮件载荷豁免的回归测试。
/// 背景：SendLog.Body 是"已组装完成的出站邮件完整 HTML"（受信模板 + 已消毒消息 + HTML 编码变量）。
/// 若在入库时再次 HtmlSanitizer 兜底消毒，会剥离模板 style 并把 &lt;p&gt;&lt;div&gt;…&lt;/div&gt;&lt;/p&gt;
/// 非法嵌套重排为空段，导致客服收到的新回复通知邮件样式丢失、出现空段/标签痕迹。
/// </summary>
public class HtmlSanitizingSaveChangesInterceptorTests
{
    private static NoneadCSSDbContext CreateContext()
    {
        var options = new DbContextOptionsBuilder<NoneadCSSDbContext>()
            .UseInMemoryDatabase($"InterceptorTestDb_{Guid.NewGuid()}")
            .AddInterceptors(new HtmlSanitizingSaveChangesInterceptor(
                NullLogger<HtmlSanitizingSaveChangesInterceptor>.Instance))
            .Options;
        return new NoneadCSSDbContext(options);
    }

    [Fact]
    public async Task SaveChanges_SendLogBodyWithInlineStyle_ShouldKeepStylingUntouched()
    {
        await using var context = CreateContext();

        var sendLog = new SendLog
        {
            MailboxId = 1,
            Email = "assignee@example.com",
            Subject = "[#55] [新回复] 测试二次发送邮件",
            // 出站通知邮件：模板自带内联样式 + 消息正文（合法块级 HTML，无非法嵌套）
            Body = "<p>您好 文庆 张，</p>\r\n" +
                   "<p>您跟进的会话收到了新回复：</p>\r\n" +
                   "<div style=\"background:#f9f9f9;padding:15px;border-left:3px solid #007bff;margin:10px 0;\">\r\n" +
                   "  <p><strong>试 测</strong> 回复道：</p>\r\n" +
                   "  <div>客户二次问答</div>\r\n" +
                   "</div>",
            Status = SendLogStatus.Accepted,
            MailType = 1,
            CreatedAt = DateTime.UtcNow,
            AttemptCount = 0
        };
        context.SendLogs.Add(sendLog);
        await context.SaveChangesAsync();

        var stored = await context.SendLogs.AsNoTracking().FirstAsync(s => s.Id == sendLog.Id);
        // style 必须原样保留，不得被消毒为 <div>（导致邮件无样式）
        stored.Body.Should().Contain("style=\"background:#f9f9f9;padding:15px;border-left:3px solid #007bff;margin:10px 0;\"");
        // 合法块级嵌套 <div>…</div> 不得被重排
        stored.Body.Should().Contain("<div>客户二次问答</div>");
    }

    [Fact]
    public async Task SaveChanges_SendLogBodyWithoutIllegalNesting_ShouldNotCreateEmptyParagraphs()
    {
        await using var context = CreateContext();

        var sendLog = new SendLog
        {
            MailboxId = 1,
            Email = "assignee@example.com",
            Subject = "[新回复] 主题",
            // 回归：旧行为会把 <p><div>…</div></p> 重排成 <p></p><div>…</div><p></p>
            Body = "<p>回复道：</p><div>客户二次问答</div>",
            Status = SendLogStatus.Accepted,
            MailType = 1,
            CreatedAt = DateTime.UtcNow,
            AttemptCount = 0
        };
        context.SendLogs.Add(sendLog);
        await context.SaveChangesAsync();

        var stored = await context.SendLogs.AsNoTracking().FirstAsync(s => s.Id == sendLog.Id);
        stored.Body.Should().NotContain("<p></p>");
        stored.Body.Should().Contain("<div>客户二次问答</div>");
    }

    [Fact]
    public async Task SaveChanges_GenericHtmlProperty_ShouldStillBeSanitized()
    {
        await using var context = CreateContext();

        // 非出站载荷（MailTemplate.BodyHtml 等用户可编辑富文本）仍需兜底消毒
        var template = new MailTemplate
        {
            Name = "测试模板",
            Slug = "interceptor-skip-test",
            Type = MailTemplateType.Custom,
            Subject = "测试",
            BodyHtml = "<script>alert(1)</script><p onclick=\"bad()\">正文</p>",
            IsActive = true,
            IsSystem = false,
            CreatedAt = DateTime.UtcNow,
            UpdatedAt = DateTime.UtcNow
        };
        context.MailTemplates.Add(template);
        await context.SaveChangesAsync();

        var stored = await context.MailTemplates.AsNoTracking().FirstAsync(t => t.Id == template.Id);
        stored.BodyHtml.Should().NotContain("<script>");
        stored.BodyHtml.Should().NotContain("onclick");
        stored.BodyHtml.Should().Contain("正文");
    }
}
