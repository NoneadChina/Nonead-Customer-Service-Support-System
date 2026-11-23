using FluentAssertions;
using NoneadCSS.Application.Common;

namespace NoneadCSS.Tests.Services;

/// <summary>
/// EmailExtractor 单元测试 — 验证从文本中提取邮箱地址的正则逻辑。
/// 覆盖场景：
/// - 标准邮箱格式
/// - 混在中文/英文句子中的邮箱
/// - 多邮箱提取
/// - 无效邮箱不误匹配
/// - 边界情况（空字符串、null）
/// </summary>
public class EmailExtractorTests
{
    #region ExtractFirst - 标准场景

    [Fact]
    public void ExtractFirst_StandardEmail_ReturnsEmail()
    {
        var text = "我的邮箱是 user@example.com";
        var result = EmailExtractor.ExtractFirst(text);

        result.Should().Be("user@example.com");
    }

    [Fact]
    public void ExtractFirst_EmailInChineseSentence_ReturnsEmail()
    {
        var text = "您好，我的电子邮箱地址是 zhang.san@company.cn，请惠存。";
        var result = EmailExtractor.ExtractFirst(text);

        result.Should().Be("zhang.san@company.cn");
    }

    [Fact]
    public void ExtractFirst_EmailInEnglishSentence_ReturnsEmail()
    {
        var text = "Please contact me at john.doe+test@gmail.com for details.";
        var result = EmailExtractor.ExtractFirst(text);

        result.Should().Be("john.doe+test@gmail.com");
    }

    [Fact]
    public void ExtractFirst_EmailWithSubdomain_ReturnsEmail()
    {
        var text = "Send to admin@mail.example.org thanks";
        var result = EmailExtractor.ExtractFirst(text);

        result.Should().Be("admin@mail.example.org");
    }

    #endregion

    #region ExtractFirst - 大小写归一化

    [Fact]
    public void ExtractFirst_UpperCaseEmail_ReturnsLowerCase()
    {
        var text = "My email is USER@EXAMPLE.COM";
        var result = EmailExtractor.ExtractFirst(text);

        result.Should().Be("user@example.com");
    }

    [Fact]
    public void ExtractFirst_MixedCaseEmail_ReturnsLowerCase()
    {
        var text = "Email: John.Doe@Example.Com";
        var result = EmailExtractor.ExtractFirst(text);

        result.Should().Be("john.doe@example.com");
    }

    #endregion

    #region ExtractFirst - 模拟微信/WhatsApp对话场景

    [Fact]
    public void ExtractFirst_WeChatScenario_CustomerRepliesWithEmail()
    {
        // 模拟客户回复："我的邮箱是 xxx@xxx.com"
        var text = "我的邮箱是 alice@163.com";
        var result = EmailExtractor.ExtractFirst(text);

        result.Should().Be("alice@163.com");
    }

    [Fact]
    public void ExtractFirst_WeChatScenario_EmailWithSurroundingText()
    {
        // 模拟客户回复："好的，我的邮箱是 xxx@xxx.com 谢谢"
        var text = "好的，我的邮箱是 bob@qq.com 谢谢";
        var result = EmailExtractor.ExtractFirst(text);

        result.Should().Be("bob@qq.com");
    }

    [Fact]
    public void ExtractFirst_WhatsAppScenario_EmailInEnglishReply()
    {
        // 模拟 WhatsApp 客户回复
        var text = "Sure, my email is charlie.test@outlook.com";
        var result = EmailExtractor.ExtractFirst(text);

        result.Should().Be("charlie.test@outlook.com");
    }

    [Fact]
    public void ExtractFirst_WhatsAppScenario_EmailWithNewline()
    {
        // 模拟 WhatsApp 客户回复换行
        var text = "Here it is:\nmary@company.co.uk\nThanks";
        var result = EmailExtractor.ExtractFirst(text);

        result.Should().Be("mary@company.co.uk");
    }

    [Fact]
    public void ExtractFirst_EmailWithPlusSign_ReturnsEmail()
    {
        // Gmail 别名邮箱
        var text = "Use foo+bar@gmail.com";
        var result = EmailExtractor.ExtractFirst(text);

        result.Should().Be("foo+bar@gmail.com");
    }

    [Fact]
    public void ExtractFirst_EmailWithUnderscore_ReturnsEmail()
    {
        var text = "my email: first_last@domain.com";
        var result = EmailExtractor.ExtractFirst(text);

        result.Should().Be("first_last@domain.com");
    }

    [Fact]
    public void ExtractFirst_EmailWithPercentSign_ReturnsEmail()
    {
        var text = "contact: user%name@example.com";
        var result = EmailExtractor.ExtractFirst(text);

        result.Should().Be("user%name@example.com");
    }

    #endregion

    #region ExtractFirst - 无效/边界场景

    [Fact]
    public void ExtractFirst_NoEmail_ReturnsNull()
    {
        var text = "您好，我想咨询一下产品价格";
        var result = EmailExtractor.ExtractFirst(text);

        result.Should().BeNull();
    }

    [Fact]
    public void ExtractFirst_EmptyString_ReturnsNull()
    {
        var result = EmailExtractor.ExtractFirst("");

        result.Should().BeNull();
    }

    [Fact]
    public void ExtractFirst_Null_ReturnsNull()
    {
        var result = EmailExtractor.ExtractFirst(null);

        result.Should().BeNull();
    }

    [Fact]
    public void ExtractFirst_WhitespaceOnly_ReturnsNull()
    {
        var result = EmailExtractor.ExtractFirst("   ");

        result.Should().BeNull();
    }

    [Fact]
    public void ExtractFirst_NoAtSign_ReturnsNull()
    {
        var text = "This is just plain text without email";
        var result = EmailExtractor.ExtractFirst(text);

        result.Should().BeNull();
    }

    [Fact]
    public void ExtractFirst_AtSignButNoDomain_ReturnsNull()
    {
        // "user@.com" — 域名部分以点开头，不匹配
        var text = "broken@.com";
        var result = EmailExtractor.ExtractFirst(text);

        result.Should().BeNull();
    }

    [Fact]
    public void ExtractFirst_AtSignButTooShort_ReturnsNull()
    {
        // "a@b.c" — .c 只有 1 字符，正则要求 {2,}，不匹配
        var text = "a@b.c";
        var result = EmailExtractor.ExtractFirst(text);

        result.Should().BeNull();
    }

    [Fact]
    public void ExtractFirst_PlainTextWithoutAtSign_ReturnsNull()
    {
        // 模拟客户回复纯中文不含邮箱
        var text = "好的，我已经收到了，谢谢";
        var result = EmailExtractor.ExtractFirst(text);

        result.Should().BeNull();
    }

    #endregion

    #region ExtractAll - 多邮箱场景

    [Fact]
    public void ExtractAll_MultipleEmails_ReturnsAll()
    {
        var text = "Primary: alice@test.com, Backup: alice.b@backup.org";
        var result = EmailExtractor.ExtractAll(text);

        result.Should().HaveCount(2);
        result.Should().Contain("alice@test.com");
        result.Should().Contain("alice.b@backup.org");
    }

    [Fact]
    public void ExtractAll_DuplicateEmails_ReturnsDistinct()
    {
        var text = "Email: test@example.com and again test@example.com";
        var result = EmailExtractor.ExtractAll(text);

        result.Should().HaveCount(1);
        result[0].Should().Be("test@example.com");
    }

    [Fact]
    public void ExtractAll_CaseInsensitiveDuplicate_ReturnsOne()
    {
        var text = "TEST@EXAMPLE.COM and test@example.com";
        var result = EmailExtractor.ExtractAll(text);

        result.Should().HaveCount(1);
        result[0].Should().Be("test@example.com");
    }

    [Fact]
    public void ExtractAll_NoEmail_ReturnsEmptyList()
    {
        var result = EmailExtractor.ExtractAll("no emails here");

        result.Should().BeEmpty();
    }

    [Fact]
    public void ExtractAll_Null_ReturnsEmptyList()
    {
        var result = EmailExtractor.ExtractAll(null);

        result.Should().BeEmpty();
    }

    #endregion
}
