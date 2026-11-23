using System.Globalization;
using System.Text.Json;
using System.Text.RegularExpressions;
using FluentAssertions;
using Microsoft.AspNetCore.Localization;
using Microsoft.AspNetCore.Http;
using NoneadCSS.Api.Localization;
using Xunit;

namespace NoneadCSS.Tests.Localization;

/// <summary>
/// 后端 API 返回文案「出口词典替换」的核心判据。
/// 设计前提（见 NoneadCSS.Api/Localization/ApiMessageLocalizer.cs 的类注释）：
///   - 词典键是「归一化后的中文原句」，动态部分用 U+E000 分隔符占位，英文值用 {0}/{1}… 回填；
///   - 只在响应出口调用，且只改 Message / ErrorMessage / Detail 三个字段名；
///   - 词典未命中必须返回 null（调用方保留原文），绝不能输出半截英文。
/// 这些测试只用合成对象，不依赖任何控制器，避免和 633 个端点的形状耦合。
/// </summary>
public class ApiMessageLocalizerTests
{
    /// <summary>词典键里代表「一段动态内容」的私有区码位（与生成词典的脚本同一约定）。</summary>
    private const string Sep = "\uE000";

    private const string CjkPattern = "[\u3400-\u9fff\u3000-\u303f\uff00-\uffef]";

    private static HttpContext ContextWithUiCulture(string uiCulture)
    {
        var culture = new CultureInfo(uiCulture);
        var context = new DefaultHttpContext();
        context.Features.Set<IRequestCultureFeature>(
            new RequestCultureFeature(new RequestCulture(culture, culture), null));
        return context;
    }

    // ---------- 词典装载 ----------

    [Fact]
    public void Dictionary_IsShippedNextToTheApiProject_AndHasAtLeast690Entries()
    {
        ApiMessageLocalizer.SourcePath.Should().NotBeNullOrEmpty(
            "找不到词典时整套英文替换会被静默禁用，必须有可断言的落点");
        File.Exists(ApiMessageLocalizer.SourcePath).Should().BeTrue(
            "csproj 需要 Content Update + CopyToOutputDirectory，否则 bin/publish 里没有这个文件");

        ApiMessageLocalizer.EntryCount.Should().BeGreaterThanOrEqualTo(690,
            "扫描出的客户端可见中文有 692 条，条目数骤降说明词典被截断或被覆盖写坏");
    }

    [Fact]
    public void Dictionary_ContainsNoCjkInAnyEnglishValue()
    {
        var entries = LoadDictionaryFromDisk();
        var cjk = new Regex(CjkPattern);

        entries.Should().NotBeEmpty();
        entries.Where(kv => cjk.IsMatch(kv.Value)).Select(kv => Shorten(kv.Key)).Take(10)
            .Should().BeEmpty("英文值里出现中日韩字符或全角标点意味着该条没翻译完");
    }

    /// <summary>
    /// 归一化不变式：把每条词典键的分隔符还原成一段数字，再走一次归一化查找，必须命中同一条。
    /// 这条测试同时锁住「C# 侧归一化」与「生成词典时的归一化」两套实现不会各自漂移。
    /// </summary>
    [Fact]
    public void Dictionary_RoundTripsForEveryKey()
    {
        var entries = LoadDictionaryFromDisk();
        var failures = new List<string>();

        foreach (var (key, expected) in entries)
        {
            var sample = key.Replace(Sep, "123");
            var actual = ApiMessageLocalizer.LocalizeToEnglish(sample);
            // 每个片段都用同一段「123」还原，所以期望值里每个 {N} 也应当被回填成 123。
            var expectedFilled = new Regex(@"\{\d+\}").Replace(expected, "123");

            if (actual == null)
            {
                failures.Add("NO MATCH: " + Shorten(key));
            }
            else if (actual != expectedFilled)
            {
                failures.Add("DIFFERENT ENTRY HIT: " + Shorten(key) + " -> " + Shorten(actual));
            }
        }

        failures.Should().BeEmpty(
            $"全部 {entries.Count} 条词典键都应能由还原样本重新命中；失败样例（前 10）：{string.Join(" | ", failures.Take(10))}");
    }

    [Fact]
    public void Dictionary_SlotPlaceholdersAreNeverLeftUnfilled()
    {
        foreach (var (key, _) in LoadDictionaryFromDisk())
        {
            var actual = ApiMessageLocalizer.LocalizeToEnglish(key.Replace(Sep, "123"));

            actual.Should().NotBeNull(Shorten(key));
            Regex.Matches(actual!, @"\{\d+\}").Count.Should().Be(0,
                $"渲染后不应残留未回填的槽位：{Shorten(key)} -> {Shorten(actual!)}");
        }
    }

    // ---------- 命中与不命中 ----------

    [Theory]
    [InlineData("这是一条词典里不可能存在的中文提示文案")]
    public void Localize_ReturnsNull_WhenTheMessageIsNotInTheDictionary(string message)
    {
        ApiMessageLocalizer.Localize(message, "en").Should().BeNull(
            "词典未命中时返回 null，由调用方保留原文，不能猜译文");
    }

    [Theory]
    [InlineData("会话不存在", "zh-Hans")]
    [InlineData("会话不存在", "zh-CN")]
    public void Localize_ReturnsNull_ForChineseCultures(string message, string culture)
    {
        ApiMessageLocalizer.Localize(message, culture).Should().BeNull("中文界面不做任何替换");
    }

    [Fact]
    public void Localize_HitsStaticMessagesForEnglishCultures()
    {
        ApiMessageLocalizer.Localize("会话不存在", "en").Should().NotBeNull();
        ApiMessageLocalizer.Localize("会话不存在", "en-GB").Should().NotBeNull();
    }

    [Fact]
    public void Localize_HitsDynamicMessagesByNormalizingDigitRuns()
    {
        var english = ApiMessageLocalizer.LocalizeToEnglish("文件大小超过限制（最大 50MB）");

        english.Should().NotBeNull();
        english.Should().Contain("50");
        english.Should().NotContain("最大");
    }

    [Fact]
    public void Localize_HitsDynamicMessagesByNormalizingQuotedRuns()
    {
        var english = ApiMessageLocalizer.LocalizeToEnglish("模块 'calendar' 未启用，无法执行此操作");

        english.Should().NotBeNull();
        english.Should().Contain("calendar");
    }

    [Fact]
    public void Localize_FillsMultipleSlotsInSourceOrder()
    {
        var english = ApiMessageLocalizer.LocalizeToEnglish("时间冲突：与 3 个已有事件重叠 15 分钟，请调整时间后重试。");

        english.Should().NotBeNull();
        english.Should().Contain("3");
        english.Should().Contain("15");
    }

    [Fact]
    public void Localize_ReturnsNullWhenTheRuntimeStringHasFewerSlotsThanTheKey()
    {
        // 词典里「时间冲突」这条有两个槽位；运行时只给出一个数字时归一化结果不同键，必须整条放弃。
        var english = ApiMessageLocalizer.LocalizeToEnglish("时间冲突：与 3 个已有事件重叠 分钟，请调整时间后重试。");

        english.Should().BeNull();
    }

    [Fact]
    public void Localize_DoesNotTouchAlreadyEnglishText()
    {
        ApiMessageLocalizer.LocalizeToEnglish("Invalid email or password").Should().BeNull(
            "英文原句不该被词典二次改写");
    }

    // ---------- 出口对象图替换 ----------

    [Fact]
    public void LocalizeMessageProperties_RewritesOnlyWhitelistedStringFields()
    {
        var graph = new SampleGraph
        {
            Message = "会话不存在",
            ErrorMessage = "模板不存在",
            Detail = "请求的资源不存在。",
            Name = "会话不存在",
            Inner = new SampleGraph { Message = "转工单失败" }
        };

        var changed = ApiMessageLocalizer.LocalizeMessageProperties(graph, ContextWithUiCulture("en"));

        changed.Should().BeGreaterThanOrEqualTo(4);
        graph.Message.Should().NotBe("会话不存在");
        graph.ErrorMessage.Should().NotBe("模板不存在");
        graph.Detail.Should().NotBe("请求的资源不存在。");
        graph.Name.Should().Be("会话不存在", "白名单之外的字段可能是客户数据，绝不能当系统文案改写");
        graph.Inner!.Message.Should().NotBe("转工单失败", "嵌套对象里的同名字段也要换掉");
    }

    [Fact]
    public void LocalizeMessageProperties_RewritesWhitelistedKeysInsideDictionaries()
    {
        // 登录失败这类出口会把文案同时写进信封 Message 与 Data 里的 "message" 键，
        // Data 是字典而非对象，属性反射碰不到，必须走字典键白名单这条路。
        var graph = new Dictionary<string, object?>
        {
            ["message"] = "验证码错误或已过期",
            ["data"] = new Dictionary<string, object?> { ["message"] = "验证码错误或已过期", ["captchaId"] = "abc" }
        };

        var changed = ApiMessageLocalizer.LocalizeMessageProperties(graph, ContextWithUiCulture("en"));

        changed.Should().BeGreaterThanOrEqualTo(2);
        graph["message"].Should().NotBe("验证码错误或已过期");
        ((Dictionary<string, object?>)graph["data"]!)["message"].Should().NotBe("验证码错误或已过期");
        ((Dictionary<string, object?>)graph["data"]!)["captchaId"].Should().Be("abc",
            "非白名单键（这里是业务数据）不得被改写");
    }

    [Fact]
    public void LocalizeMessageProperties_DoesNothingForChineseCulture()
    {
        var graph = new SampleGraph { Message = "会话不存在" };

        ApiMessageLocalizer.LocalizeMessageProperties(graph, ContextWithUiCulture("zh-Hans")).Should().Be(0);
        graph.Message.Should().Be("会话不存在");
    }

    [Fact]
    public void LocalizeMessageProperties_ToleratesNullAndBareStrings()
    {
        ApiMessageLocalizer.LocalizeMessageProperties(null!, ContextWithUiCulture("en")).Should().Be(0);
        ApiMessageLocalizer.LocalizeMessageProperties("会话不存在", ContextWithUiCulture("en")).Should().Be(0,
            "字符串本身没有可写的白名单字段，出口层只处理对象图");
    }

    [Fact]
    public void ApiRequestCulture_Of_PrefersTheFeatureOverTheThreadCulture()
    {
        var previous = CultureInfo.CurrentUICulture;
        try
        {
            CultureInfo.CurrentUICulture = new CultureInfo("zh-Hans");

            ApiRequestCulture.Of(ContextWithUiCulture("en")).Should().Be("en",
                "GlobalExceptionMiddleware 在 UseRequestLocalization 上游，线程文化已被还原，必须读 feature");
            ApiRequestCulture.Of(null).Should().Be("zh-Hans", "没有 HttpContext 时退回线程文化");
        }
        finally
        {
            CultureInfo.CurrentUICulture = previous;
        }
    }

    // ---------- helpers ----------

    private static IReadOnlyDictionary<string, string> LoadDictionaryFromDisk()
    {
        // 先通过一次查找触发懒加载，SourcePath 才会被填上。
        ApiMessageLocalizer.Localize("会话不存在", "en");
        ApiMessageLocalizer.SourcePath.Should().NotBeNullOrEmpty();

        var json = File.ReadAllText(ApiMessageLocalizer.SourcePath!);
        var parsed = JsonSerializer.Deserialize<Dictionary<string, string>>(json);
        parsed.Should().NotBeNull();
        return parsed!;
    }

    private static string Shorten(string value) =>
        value.Length <= 80 ? value : value[..80] + "…";

    private sealed class SampleGraph
    {
        public string? Message { get; set; }
        public string? ErrorMessage { get; set; }
        public string? Detail { get; set; }
        public string? Name { get; set; }
        public SampleGraph? Inner { get; set; }
    }
}
