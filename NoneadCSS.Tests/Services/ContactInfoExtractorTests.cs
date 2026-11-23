using FluentAssertions;
using NoneadCSS.Application.Common;

namespace NoneadCSS.Tests.Services;

/// <summary>
/// ContactInfoExtractor 单元测试 — 验证从文本中提取姓名、手机号、邮箱的逻辑。
/// 覆盖场景：
/// - 三项齐全（不同分隔符、不同顺序）
/// - 仅部分字段（只回邮箱、只回姓名+手机等）
/// - 带前缀的回复（"我叫XX"、"姓名:XX"）
/// - 国际手机号
/// - 噪声文本（问候、感谢等无关词）
/// - 边界情况（空字符串、null、纯数字）
/// </summary>
public class ContactInfoExtractorTests
{
    #region 三项齐全

    [Fact]
    public void Extract_AllThreeFields_SpaceSeparated()
    {
        var text = "张三 13800138000 zhangsan@example.com";
        var info = ContactInfoExtractor.Extract(text);

        info.Name.Should().Be("张三");
        info.Phone.Should().Be("13800138000");
        info.Email.Should().Be("zhangsan@example.com");
        info.HasAny.Should().BeTrue();
    }

    [Fact]
    public void Extract_AllThreeFields_SlashSeparated()
    {
        var text = "李四/13912345678/lisi@163.com";
        var info = ContactInfoExtractor.Extract(text);

        info.Name.Should().Be("李四");
        info.Phone.Should().Be("13912345678");
        info.Email.Should().Be("lisi@163.com");
    }

    [Fact]
    public void Extract_AllThreeFields_ChinesePunctuation()
    {
        var text = "王五，手机13700137000，邮箱wangwu@qq.com";
        var info = ContactInfoExtractor.Extract(text);

        info.Name.Should().Be("王五");
        info.Phone.Should().Be("13700137000");
        info.Email.Should().Be("wangwu@qq.com");
    }

    [Fact]
    public void Extract_AllThreeFields_WithPrefixes()
    {
        var text = "我叫赵六，手机号是13600136000，邮箱是zhaoliu@gmail.com";
        var info = ContactInfoExtractor.Extract(text);

        info.Name.Should().Be("赵六");
        info.Phone.Should().Be("13600136000");
        info.Email.Should().Be("zhaoliu@gmail.com");
    }

    [Fact]
    public void Extract_AllThreeFields_DifferentOrder()
    {
        // 邮箱在前、姓名在后、手机居中
        var text = "alice@work.com 13500135000 孙七";
        var info = ContactInfoExtractor.Extract(text);

        info.Email.Should().Be("alice@work.com");
        info.Phone.Should().Be("13500135000");
        info.Name.Should().Be("孙七");
    }

    [Fact]
    public void Extract_AllThreeFields_NewlineSeparated()
    {
        var text = "周八\n13800138000\nzhouba@outlook.com";
        var info = ContactInfoExtractor.Extract(text);

        info.Name.Should().Be("周八");
        info.Phone.Should().Be("13800138000");
        info.Email.Should().Be("zhouba@outlook.com");
    }

    #endregion

    #region 部分字段

    [Fact]
    public void Extract_OnlyEmail_ReturnsEmailOnly()
    {
        var text = "我的邮箱是 only@example.com";
        var info = ContactInfoExtractor.Extract(text);

        info.Email.Should().Be("only@example.com");
        info.Phone.Should().BeEmpty();
        info.Name.Should().BeEmpty();
        info.HasAny.Should().BeTrue();
    }

    [Fact]
    public void Extract_OnlyPhone_ReturnsPhoneOnly()
    {
        var text = "13800138000";
        var info = ContactInfoExtractor.Extract(text);

        info.Phone.Should().Be("13800138000");
        info.Email.Should().BeEmpty();
        // 纯数字不被识别为姓名
        info.Name.Should().BeEmpty();
    }

    [Fact]
    public void Extract_NameAndPhone_NoEmail()
    {
        var text = "吴九 13900139000";
        var info = ContactInfoExtractor.Extract(text);

        info.Name.Should().Be("吴九");
        info.Phone.Should().Be("13900139000");
        info.Email.Should().BeEmpty();
    }

    [Fact]
    public void Extract_NameAndEmail_NoPhone()
    {
        var text = "郑十 zhengshi@example.com";
        var info = ContactInfoExtractor.Extract(text);

        info.Name.Should().Be("郑十");
        info.Email.Should().Be("zhengshi@example.com");
        info.Phone.Should().BeEmpty();
    }

    #endregion

    #region 姓名前缀清洗

    [Fact]
    public void Extract_NamePrefix_IAm()
    {
        var text = "我是张三 13800138000 zhangsan@example.com";
        var info = ContactInfoExtractor.Extract(text);

        info.Name.Should().Be("张三");
    }

    [Fact]
    public void Extract_NamePrefix_NameColon()
    {
        var text = "姓名: 李四 13900139000 lisi@test.com";
        var info = ContactInfoExtractor.Extract(text);

        info.Name.Should().Be("李四");
    }

    [Fact]
    public void Extract_NamePrefix_NameChineseColon()
    {
        var text = "姓名：王五 13700137000 wangwu@test.org";
        var info = ContactInfoExtractor.Extract(text);

        info.Name.Should().Be("王五");
    }

    [Fact]
    public void Extract_NamePrefix_EnglishName()
    {
        var text = "My name is John Doe +1234567890 john@example.com";
        var info = ContactInfoExtractor.Extract(text);

        info.Name.Should().Be("John");
        info.Phone.Should().Be("+1234567890");
        info.Email.Should().Be("john@example.com");
    }

    #endregion

    #region 国际手机号

    [Fact]
    public void Extract_InternationalPhone_WithPlus()
    {
        var text = "Alice +1 2025551234 alice@example.com";
        var info = ContactInfoExtractor.Extract(text);

        info.Phone.Should().StartWith("+1");
        info.Email.Should().Be("alice@example.com");
    }

    [Fact]
    public void Extract_InternationalPhone_WithCountryCode()
    {
        var text = "Bob +86 13800138000 bob@example.com";
        var info = ContactInfoExtractor.Extract(text);

        info.Phone.Should().NotBeEmpty();
        info.Email.Should().Be("bob@example.com");
    }

    #endregion

    #region 噪声与边界

    [Fact]
    public void Extract_NullText_ReturnsEmpty()
    {
        var info = ContactInfoExtractor.Extract(null);

        info.Name.Should().BeEmpty();
        info.Phone.Should().BeEmpty();
        info.Email.Should().BeEmpty();
        info.HasAny.Should().BeFalse();
    }

    [Fact]
    public void Extract_EmptyText_ReturnsEmpty()
    {
        var info = ContactInfoExtractor.Extract("");

        info.HasAny.Should().BeFalse();
    }

    [Fact]
    public void Extract_WhitespaceOnly_ReturnsEmpty()
    {
        var info = ContactInfoExtractor.Extract("   ");

        info.HasAny.Should().BeFalse();
    }

    [Fact]
    public void Extract_PureNoise_NoContactInfo()
    {
        var text = "你好，我想咨询一下产品信息";
        var info = ContactInfoExtractor.Extract(text);

        info.HasAny.Should().BeFalse();
    }

    [Fact]
    public void Extract_PhoneNotInIsolation_NotExtracted()
    {
        // 长数字串中的 11 位不应被识别为手机号
        var text = "订单号 123456789012345";
        var info = ContactInfoExtractor.Extract(text);

        info.Phone.Should().BeEmpty();
    }

    [Fact]
    public void Extract_GreetingAndThanks_OnlyNameExtracted()
    {
        var text = "你好，我叫陈十一，谢谢 13800138000 chen@test.com";
        var info = ContactInfoExtractor.Extract(text);

        info.Name.Should().Be("陈十一");
        info.Phone.Should().Be("13800138000");
        info.Email.Should().Be("chen@test.com");
    }

    #endregion
}
