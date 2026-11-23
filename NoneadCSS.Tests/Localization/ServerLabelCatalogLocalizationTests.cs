using System.Globalization;
using System.Reflection;
using System.Resources;
using System.Text.RegularExpressions;
using FluentAssertions;
using Microsoft.Extensions.Caching.Memory;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;
using Moq;
using NoneadCSS.Application.Configuration;
using NoneadCSS.Application.Services;
using NoneadCSS.Domain;
using Xunit;

namespace NoneadCSS.Tests.Localization;

/// <summary>
/// 服务端标签目录本地化回归（批次 13）：
/// 密码策略 <c>requirements</c> 与 SLA 违规类型/状态标签属于服务端下发的标签目录，
/// 响应出口词典（<c>NoneadCSS.Api/Localization/api-message-en.json</c>）只改写
/// <c>message</c> / <c>errorMessage</c> / <c>detail</c> 三个键，不会翻译数组元素，
/// 因此这两组目录必须由 <c>Messages.resx</c> 提供 en / zh-Hans 两套词条。
/// 本测试锁三件事：两种区域各返回对应语言、zh-Hans 与改造前的中文原文逐字一致、
/// 未注入本地化服务（后台任务与旧构造点）时仍输出中文。
/// </summary>
public class ServerLabelCatalogLocalizationTests
{
    private const string MessagesBaseName = "NoneadCSS.Application.Resources.Messages";

    private static readonly CultureInfo En = new("en");
    private static readonly CultureInfo Zh = new("zh-Hans");

    /// <summary>
    /// 本批次新增的标签目录键：键名 / en 值 / zh-Hans 值。
    /// zh-Hans 值必须与改动前代码里的字面量逐字一致（避免视觉回归）；
    /// en 值与前端 en 词条措辞对齐（settings.password.rule.*、automation.sla.violationType.*、automation.sla.status.*）。
    /// </summary>
    private static readonly (string Key, string En, string Zh)[] Catalog =
    [
        ("PolicyMinLength", "At least {0} characters", "至少 {0} 个字符"),
        ("PolicyRequireUppercase", "At least one uppercase letter (A-Z)", "至少一个大写字母 (A-Z)"),
        ("PolicyRequireLowercase", "At least one lowercase letter (a-z)", "至少一个小写字母 (a-z)"),
        ("PolicyRequireDigit", "At least one digit (0-9)", "至少一个数字 (0-9)"),
        ("PolicyRequireSymbol", "At least one special character (!@#$%^&*...)", "至少一个特殊字符 (!@#$%^&*...)"),
        ("PolicyForbidWeakPassword", "Must not be a common weak password", "不能使用常见弱密码"),
        ("PolicyForbidCommonPatterns", "Must not contain simple patterns (sequential numbers/letters, repeated characters, keyboard rows)", "不能包含简单模式（连续数字/字母、重复字符、键盘行）"),
        ("SlaViolationTypeFirstResponse", "First response", "首次响应"),
        ("SlaViolationTypeResolution", "Resolution time", "解决时长"),
        ("SlaViolationStatusWarning", "Warning", "预警中"),
        ("SlaViolationStatusBreached", "Breached", "已违规"),
        ("SlaViolationStatusResolved", "Resolved", "已解决"),
        // 日历订阅指引也是服务端文案：出口词典只改 message/errorMessage/detail，不会翻译 data 里的
        // 任意字段，所以这条必须由 resx 提供两套词条（控制器 CalendarController.GetSubscription）。
        ("CalendarSubscriptionInstructions", "In Outlook: File > Open and Export > Manage Subscriptions > Add Subscription > paste this URL. Note: this URL contains your subscription token, so do not share it with others.", "在 Outlook 中：文件 → 打开和导出 → 管理订阅 → 添加订阅 → 粘贴此 URL。注意：此 URL 包含订阅令牌，请勿分享给他人。"),
    ];

    /// <summary>改动前 <c>BuildRequirements</c> 的中文原文（全部检查项启用时的顺序）。</summary>
    private static readonly string[] OriginalChineseRequirements =
    [
        "至少 10 个字符",
        "至少一个大写字母 (A-Z)",
        "至少一个小写字母 (a-z)",
        "至少一个数字 (0-9)",
        "至少一个特殊字符 (!@#$%^&*...)",
        "不能使用常见弱密码",
        "不能包含简单模式（连续数字/字母、重复字符、键盘行）",
    ];

    // ====== 词条本身：两份 resx 都要有，且 en 侧不得混入中日韩字符 ======

    [Fact]
    public void Messages_Resx_Carries_Every_Label_Catalog_Key_In_Both_Cultures()
    {
        // ResourceManager 在此 TFM 上未实现 IDisposable，不能写 using；显式释放即可。
        var resources = new ResourceManager(MessagesBaseName, typeof(ILocalizationService).Assembly);

        foreach (var (key, en, zh) in Catalog)
        {
            var enValue = resources.GetString(key, En);
            enValue.Should().Be(en, $"{key} 的英文词条应与前端 en 措辞一致");
            Regex.IsMatch(enValue!, @"[\u3040-\u30FF\u4E00-\u9FFF\uFF00-\uFFEF]")
                .Should().BeFalse($"{key} 的英文词条不得含中日韩字符");

            var zhValue = resources.GetString(key, Zh);
            zhValue.Should().Be(zh, $"{key} 的中文词条应与改动前的中文原文逐字一致");
        }
    }

    // ====== 密码策略 requirements ======

    [Fact]
    public async Task PasswordPolicy_Requirements_Return_English_Catalog_Under_En()
    {
        var policy = await BuildPasswordPolicyService(new StubLocalizationService(En)).GetPolicyAsync();

        policy.Requirements.Should().Equal(
            "At least 10 characters",
            "At least one uppercase letter (A-Z)",
            "At least one lowercase letter (a-z)",
            "At least one digit (0-9)",
            "At least one special character (!@#$%^&*...)",
            "Must not be a common weak password",
            "Must not contain simple patterns (sequential numbers/letters, repeated characters, keyboard rows)");
    }

    [Fact]
    public async Task PasswordPolicy_Requirements_Return_Original_Chinese_Under_ZhHans()
    {
        var policy = await BuildPasswordPolicyService(new StubLocalizationService(Zh)).GetPolicyAsync();

        policy.Requirements.Should().Equal(OriginalChineseRequirements);
    }

    [Fact]
    public async Task PasswordPolicy_Requirements_Stay_Chinese_Without_Localization_Service()
    {
        // 旧构造点（后台任务、既有测试）不传本地化服务：必须保持改造前的中文输出
        var service = new PasswordPolicyService(
            new Mock<IUnitOfWork>().Object,
            new MemoryCache(new MemoryCacheOptions()),
            NullLogger<PasswordPolicyService>.Instance,
            Options.Create(new CacheOptions()));

        var policy = await service.GetPolicyAsync();

        policy.Requirements.Should().Equal(OriginalChineseRequirements);
    }

    [Fact]
    public async Task PasswordPolicy_Cache_Must_Not_Leak_The_First_Request_Language()
    {
        var localization = new StubLocalizationService(En);
        var service = BuildPasswordPolicyService(localization);

        var english = await service.GetPolicyAsync();
        english.Requirements.Should().Contain("At least 10 characters");

        localization.Culture = Zh;
        var chinese = await service.GetPolicyAsync();

        chinese.Requirements.Should().Contain("至少 10 个字符");
        // 缓存实例本身保持语言中立，否则上面那次英文请求会被改写
        english.Requirements.Should().Contain("At least 10 characters");
    }

    // ====== SLA 违规类型 / 状态标签 ======

    [Fact]
    public void Sla_Violation_Labels_Return_English_Catalog_Under_En()
    {
        var service = BuildSlaService(new StubLocalizationService(En));

        InvokeLabel(service, "LocalizeViolationType", SlaConst.ViolationFirstResponse).Should().Be("First response");
        InvokeLabel(service, "LocalizeViolationType", SlaConst.ViolationResolution).Should().Be("Resolution time");
        InvokeLabel(service, "LocalizeViolationStatus", SlaConst.StatusWarning).Should().Be("Warning");
        InvokeLabel(service, "LocalizeViolationStatus", SlaConst.StatusBreached).Should().Be("Breached");
        InvokeLabel(service, "LocalizeViolationStatus", SlaConst.StatusResolved).Should().Be("Resolved");
    }

    [Fact]
    public void Sla_Violation_Labels_Return_Original_Chinese_Under_ZhHans()
    {
        var service = BuildSlaService(new StubLocalizationService(Zh));

        InvokeLabel(service, "LocalizeViolationType", SlaConst.ViolationFirstResponse).Should().Be("首次响应");
        InvokeLabel(service, "LocalizeViolationType", SlaConst.ViolationResolution).Should().Be("解决时长");
        InvokeLabel(service, "LocalizeViolationStatus", SlaConst.StatusWarning).Should().Be("预警中");
        InvokeLabel(service, "LocalizeViolationStatus", SlaConst.StatusBreached).Should().Be("已违规");
        InvokeLabel(service, "LocalizeViolationStatus", SlaConst.StatusResolved).Should().Be("已解决");
    }

    [Fact]
    public void Sla_Violation_Labels_Fall_Back_To_SlaConst_Without_Localization_Service()
    {
        var service = BuildSlaService(localization: null);

        InvokeLabel(service, "LocalizeViolationType", SlaConst.ViolationFirstResponse)
            .Should().Be(SlaConst.GetViolationTypeLabel(SlaConst.ViolationFirstResponse));
        InvokeLabel(service, "LocalizeViolationStatus", SlaConst.StatusBreached)
            .Should().Be(SlaConst.GetViolationStatusLabel(SlaConst.StatusBreached));
        // 未知码不翻译，原样透传（与 SlaConst 的兜底行为一致）
        InvokeLabel(service, "LocalizeViolationType", "custom_type").Should().Be("custom_type");
        InvokeLabel(service, "LocalizeViolationStatus", "custom_status").Should().Be("custom_status");
    }

    // ====== 构造与反射辅助 ======

    private static PasswordPolicyService BuildPasswordPolicyService(ILocalizationService localization) => new(
        new Mock<IUnitOfWork>().Object,
        new MemoryCache(new MemoryCacheOptions()),
        NullLogger<PasswordPolicyService>.Instance,
        Options.Create(new CacheOptions()),
        localization);

    /// <summary>只用标签方法，其余依赖传 null 即可（构造体只做字段赋值）。</summary>
    private static SlaService BuildSlaService(ILocalizationService? localization) =>
        new(null!, null!, null!, null!, null!, null!, localization);

    private static string InvokeLabel(SlaService service, string methodName, string code)
    {
        var method = typeof(SlaService).GetMethod(methodName, BindingFlags.Instance | BindingFlags.NonPublic);
        method.Should().NotBeNull($"{nameof(SlaService)}.{methodName} 必须存在（改名需同步本测试）");

        var result = method!.Invoke(service, new object[] { code });
        result.Should().BeOfType<string>();
        return (string)result!;
    }

    /// <summary>
    /// 直读 <c>Messages.resx</c> 的轻量 <see cref="ILocalizationService"/>，
    /// 语言由 <see cref="Culture"/> 决定（模拟请求区域；不依赖 HttpContext 与数据库词条）。
    /// </summary>
    private sealed class StubLocalizationService : ILocalizationService
    {
        private readonly ResourceManager _resources;

        public StubLocalizationService(CultureInfo culture)
        {
            _resources = new ResourceManager(MessagesBaseName, typeof(ILocalizationService).Assembly);
            Culture = culture;
        }

        public CultureInfo Culture { get; set; }

        public string this[string key] => _resources.GetString(key, Culture) ?? key;

        public string GetString(string key, params object[] args)
        {
            var value = this[key];
            return args.Length == 0 ? value : string.Format(value, args);
        }

        public string CurrentLanguage => Culture.Name;

        public IReadOnlyList<string> SupportedLanguages => ["en", "zh-Hans"];
    }
}
