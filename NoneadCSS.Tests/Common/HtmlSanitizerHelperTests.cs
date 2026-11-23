using FluentAssertions;
using NoneadCSS.Application.Common;
using Xunit;

namespace NoneadCSS.Tests.Common;

/// <summary>
/// T000106：邮件纯文本替代部分（TextBody）必须剥离 {#FS:线程ID#} 追踪标记。
/// 背景：邮件客户端显示纯文本版时，若标记泄漏会看到 `{#FS:1115#}` 字样；
/// 且 Sanitize() 白名单不含 style 属性，会先剥离 div 的 display:none 样式，
/// 因此去标记不能依赖 style 匹配。
/// </summary>
public class HtmlSanitizerHelperTests
{
    [Fact]
    public void ConvertToPlainText_HiddenFsTrackingDiv_ShouldStripMarker()
    {
        var html = "<p>您好，工单已分配。</p>\n" +
                   "<div style=\"display:none !important;visibility:hidden;mso-hide:all;height:0;max-height:0;overflow:hidden;font-size:0;line-height:0;\">{#FS:99#}</div>";

        var text = HtmlSanitizerHelper.ConvertToPlainText(html);

        text.Should().NotContain("#FS:");
        text.Should().NotContain("{#FS");
        text.Should().Contain("您好，工单已分配。");
    }

    [Fact]
    public void ConvertToPlainText_BareFsMarker_ShouldStripMarker()
    {
        // 兜底：即使 div 包裹被外部处理剥掉，裸标记也必须从纯文本清除
        var text = HtmlSanitizerHelper.ConvertToPlainText("正文内容 {#FS:42#}");

        text.Should().NotContain("#FS:");
        text.Should().Contain("正文内容");
    }

    [Fact]
    public void ConvertToPlainText_NormalHtml_ShouldKeepText()
    {
        var html = "<div><p>第一段</p><p>第二段</p></div>";

        var text = HtmlSanitizerHelper.ConvertToPlainText(html);

        text.Should().Contain("第一段");
        text.Should().Contain("第二段");
        text.Should().NotContain("<");
    }
}
