using FluentAssertions;
using NoneadCSS.Application.Common;
using Xunit;

namespace NoneadCSS.Tests.Common;

/// <summary>
/// T000106：邮件正文会话回环标记 {#FS:threadId#} 规范化。
/// 场景：历史路径生成的无样式 <div>{#FS:1115#}</div> 会向收件人泄漏明文；
/// EmailBodyMarker.Normalize 应在出站前统一转换为 display:none 隐藏 div，
/// 且纯文本备选（HtmlSanitizerHelper.ConvertToPlainText）不再包含 "#FS"。
/// </summary>
public class EmailBodyMarkerTests
{
    [Fact]
    public void Normalize_WhenBareMarkerDiv_RewritesToHiddenStyleDiv()
    {
        var body = "<div><p>您的日程已处理。</p></div>  <div>{#FS:1115#}</div>";

        var result = EmailBodyMarker.Normalize(body);

        result.Should().NotContain("<div>{#FS:", "无样式标记 div 必须被移除");
        result.Should().Contain("{#FS:1115#}");
        result.Should().Contain("display:none !important", "标记必须以隐藏样式呈现");
        // 隐藏 div 必须位于正文末尾
        result.Should().EndWith("{#FS:1115#}</div>");
        // 正文只出现一次标记（在隐藏 div 内）
        result.IndexOf("{#FS:1115#}").Should().Be(result.LastIndexOf("{#FS:1115#}"));
    }

    [Fact]
    public void Normalize_WhenAlreadyHiddenStyleDiv_KeepsSingleHiddenMarker()
    {
        var body = "<p>正文</p>\n<div style=\"display:none !important;visibility:hidden;mso-hide:all;height:0;max-height:0;overflow:hidden;font-size:0;line-height:0;\">{#FS:88#}</div>";

        var result = EmailBodyMarker.Normalize(body);

        result.Should().Contain("{#FS:88#}");
        (System.Text.RegularExpressions.Regex.Matches(result, @"\{#FS:88#\}")).Count.Should().Be(1, "不应产生重复标记");
        result.Should().Contain("display:none !important");
    }

    [Fact]
    public void Normalize_WhenNoMarker_ReturnsBodyUnchanged()
    {
        var body = "<p>普通正文，无标记</p>";

        var result = EmailBodyMarker.Normalize(body);

        result.Should().Be(body);
    }

    [Fact]
    public void Normalize_PlainTextAlternative_DoesNotContainFsText()
    {
        var body = "<div><h1>感谢您的评价</h1></div>  <div>{#FS:1115#}</div>";

        var normalized = EmailBodyMarker.Normalize(body);
        var plain = HtmlSanitizerHelper.ConvertToPlainText(normalized);

        plain.Should().NotContain("#FS", "纯文本备选不得泄漏会话标记（T000106）");
        plain.Should().Contain("感谢您的评价");
    }

    [Fact]
    public void Normalize_HtmlEncodedMarker_KeepsMarkerExtractable()
    {
        // HTML 编码标记（&#123;#FS:...#&#125;）不在此规范化范围内（历史正文），不应误伤
        var body = "<div>&#123;#FS:42#&#125;</div>";

        var result = EmailBodyMarker.Normalize(body);

        // 未破坏 HTML 编码形式，同时不新增裸标记
        result.Should().NotContain("{#FS:42#}");
        result.Should().Contain("&#123;#FS:42#&#125;");
    }
}
