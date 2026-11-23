using System.Globalization;
using System.Reflection;
using System.Resources;
using System.Runtime.CompilerServices;
using System.Text.RegularExpressions;
using FluentAssertions;
using NoneadCSS.Application.Services;
using NoneadCSS.Application.Services.Calendar;
using Xunit;

namespace NoneadCSS.Tests.Localization;

/// <summary>
/// 服务端「标签目录」本地化回归（批次 14）：日历事件类型/状态、知识库状态、在线客服会话状态、
/// 发送日志状态与邮件类型、渠道显示名、SLA 时长度量、密码校验失败原因。
/// 这些目录由服务端直接下发显示文本（响应出口词典只改写 message / errorMessage / detail，不会翻译字典元素），
/// 因此必须由 <c>Messages.resx</c> 提供 en / zh-Hans 两套词条。
/// 锁四件事：两种区域各返回对应语言；zh-Hans 与改造前代码字面量逐字一致；
/// 未注入 <see cref="ILocalizationService"/>（后台任务与旧构造点）时仍输出中文；
/// 词条缺失时回退中文原文而不是把键名吐给前端。
/// </summary>
public class OptionCatalogLocalizationTests
{
    private const string MessagesBaseName = "NoneadCSS.Application.Resources.Messages";

    private static readonly ResourceManager Resources = new(MessagesBaseName, typeof(ILocalizationService).Assembly);

    private static readonly CultureInfo En = new("en");
    private static readonly CultureInfo Zh = new("zh-Hans");

    private static readonly Regex CjkPattern = new(@"[\u3040-\u30FF\u3400-\u4DBF\u4E00-\u9FFF\uF900-\uFAFF\uFF00-\uFFEF]", RegexOptions.Compiled);
    private static readonly Regex SlotPattern = new(@"\{\d+\}", RegexOptions.Compiled);

    /// <summary>
    /// 本批次新增键：键名 / en 值 / zh-Hans 值。
    /// zh-Hans 值必须与改动前代码里的字面量逐字一致（避免视觉回归）；
    /// en 值与前端 en 词条对齐（calendar.store.eventType.* / calendar.store.status.*、knowledge.status.*、
    /// chat.status.*、data.sendLogs.status.*|mailType.*、format.sourceType.*、automation.sla.duration.*）。
    /// </summary>
    private static readonly (string Key, string En, string Zh)[] Catalog =
    [
        ("CalendarEventType_due_date", "Due date", "截止日期"),
        ("CalendarEventType_task", "Task", "计划任务"),
        ("CalendarEventType_meeting", "Meeting", "会议"),
        ("CalendarEventType_reminder", "Reminder", "提醒"),
        ("CalendarStatus_pending", "Pending", "待处理"),
        ("CalendarStatus_completed", "Completed", "已完成"),
        ("CalendarStatus_cancelled", "Cancelled", "已取消"),
        ("KnowledgeStatus_draft", "Draft", "草稿"),
        ("KnowledgeStatus_published", "Published", "已发布"),
        ("KnowledgeStatus_archived", "Archived", "已归档"),
        ("ChatStatus_waiting", "Waiting", "等待接待"),
        ("ChatStatus_active", "Active", "进行中"),
        ("ChatStatus_closed", "Closed", "已关闭"),
        ("ChatStatus_transferred", "Converted to conversation", "已转工单"),
        ("SendLogStatus_1", "Received", "已接收"),
        ("SendLogStatus_2", "Send error", "发送错误"),
        ("SendLogStatus_4", "Delivered", "投递成功"),
        ("SendLogStatus_5", "Delivery failed", "投递失败"),
        ("SendLogStatus_6", "Opened", "已打开"),
        ("SendLogStatus_7", "Clicked", "已点击"),
        ("SendLogStatus_8", "Unsubscribed", "已退订"),
        ("SendLogStatus_9", "Complained", "已投诉"),
        ("SendLogStatus_10", "Send interrupted", "发送中断"),
        ("SendLogStatus_unknown", "Unknown", "未知"),
        ("SendLogMailType_1", "Customer reply", "客户回复"),
        ("SendLogMailType_2", "User notification", "用户通知"),
        ("SendLogMailType_3", "Auto reply", "自动回复"),
        ("SendLogMailType_4", "Invite", "邀请"),
        ("SendLogMailType_5", "Password changed", "密码修改"),
        ("SendLogMailType_6", "Wrong user email", "错误邮件"),
        ("SendLogMailType_7", "Test", "测试"),
        ("SendLogMailType_8", "Alert", "警报"),
        ("SendLogMailType_10", "Calendar reminder", "日程提醒"),
        ("SendLogMailType_unknown", "Unknown", "未知"),
        ("Channel_email", "Email", "邮件"),
        ("Channel_web", "Web", "网页"),
        ("Channel_api", "API", "API"),
        ("Channel_phone", "Phone", "电话"),
        ("Channel_chat", "Live chat", "在线聊天"),
        ("Channel_social", "Social", "社交"),
        ("Channel_wechat", "WeChat", "微信"),
        ("Channel_whatsapp", "WhatsApp", "WhatsApp"),
        ("Channel_feishu", "Feishu", "飞书"),
        ("Channel_dingtalk", "DingTalk", "钉钉"),
        ("Channel_messenger", "Messenger", "Messenger"),
        ("Channel_telegram", "Telegram", "Telegram"),
        ("Channel_line", "LINE", "LINE"),
        ("Channel_imessage", "Apple Messages", "iMessage"),
        ("Channel_global", "All channels", "全局（所有渠道）"),
        ("SlaDurationZero", "0 min", "0 分钟"),
        ("SlaDurationMinutes", "{0} min", "{0} 分钟"),
        ("SlaDurationHours", "{0} h", "{0} 小时"),
        ("SlaDurationHoursMinutes", "{0}h {1}m", "{0} 小时 {1} 分钟"),
        ("SlaDurationDays", "{0} d", "{0} 天"),
        ("SlaDurationDaysHours", "{0}d {1}h", "{0} 天 {1} 小时"),
        ("PolicyErrorMinLength", "Password must be at least {0} characters", "密码长度至少 {0} 位"),
        ("PolicyErrorRequireUppercase", "Password must contain at least one uppercase letter", "密码必须包含至少一个大写字母"),
        ("PolicyErrorRequireLowercase", "Password must contain at least one lowercase letter", "密码必须包含至少一个小写字母"),
        ("PolicyErrorRequireDigit", "Password must contain at least one digit", "密码必须包含至少一个数字"),
        ("PolicyErrorRequireSymbol", "Password must contain at least one special character", "密码必须包含至少一个特殊字符"),
        ("PolicyErrorTooCommon", "Password is too common, please choose a stronger one", "密码过于常见，请选择更复杂的密码"),
        ("PolicyErrorWeakPattern", "Password contains a too-simple pattern, please choose a stronger one", "密码包含过于简单的模式，请选择更复杂的密码"),
    ];

    // ====== 词条本身 ======

    [Fact]
    public void Messages_Resx_Carries_Every_Option_Catalog_Key_In_Both_Cultures()
    {
        foreach (var (key, en, zh) in Catalog)
        {
            var enValue = Resources.GetString(key, En);
            enValue.Should().Be(en, $"{key} 的英文词条应与前端 en 措辞一致");
            CjkPattern.IsMatch(enValue!).Should().BeFalse($"{key} 的英文词条不得含中日韩字符");

            var zhValue = Resources.GetString(key, Zh);
            zhValue.Should().Be(zh, $"{key} 的 zh-Hans 词条必须与改造前的中文代码字面量逐字一致");
        }
    }

    [Fact]
    public void Messages_Resx_Keeps_Placeholder_Slots_Symmetric_Across_Cultures()
    {
        // 带槽位的词条（SLA 时长、密码最小长度）两种语言必须用同一组 {0}/{1}，
        // 否则 string.Format 会在某个区域下抛 FormatException 或错位。
        foreach (var (key, en, zh) in Catalog)
        {
            var enSlots = SlotPattern.Matches(en).Select(m => m.Value).OrderBy(v => v, StringComparer.Ordinal).ToArray();
            var zhSlots = SlotPattern.Matches(zh).Select(m => m.Value).OrderBy(v => v, StringComparer.Ordinal).ToArray();
            enSlots.Should().Equal(zhSlots, $"{key} 的两种语言槽位必须一致");
        }
    }

    // ====== 1. 日历标签目录 ======

    [Fact]
    public void CalendarQueryService_EventTypeOptions_Are_Localized_Per_Culture()
    {
        InvokeStringOptions(BuildServiceWithLocalization<CalendarQueryService>(new ResxLocalizationService(En)), "GetLocalizedEventTypeOptions")
            .Should().BeEquivalentTo(new Dictionary<string, string>
            {
                ["due_date"] = "Due date",
                ["task"] = "Task",
                ["meeting"] = "Meeting",
                ["reminder"] = "Reminder"
            });

        InvokeStringOptions(BuildServiceWithLocalization<CalendarQueryService>(new ResxLocalizationService(Zh)), "GetLocalizedEventTypeOptions")
            .Should().BeEquivalentTo(new Dictionary<string, string>
            {
                ["due_date"] = "截止日期",
                ["task"] = "计划任务",
                ["meeting"] = "会议",
                ["reminder"] = "提醒"
            });
    }

    [Fact]
    public void CalendarQueryService_StatusOptions_Are_Localized_Per_Culture()
    {
        InvokeStringOptions(BuildServiceWithLocalization<CalendarQueryService>(new ResxLocalizationService(En)), "GetLocalizedStatusOptions")
            .Should().BeEquivalentTo(new Dictionary<string, string>
            {
                ["pending"] = "Pending",
                ["completed"] = "Completed",
                ["cancelled"] = "Cancelled"
            });

        InvokeStringOptions(BuildServiceWithLocalization<CalendarQueryService>(new ResxLocalizationService(Zh)), "GetLocalizedStatusOptions")
            .Should().BeEquivalentTo(new Dictionary<string, string>
            {
                ["pending"] = "待处理",
                ["completed"] = "已完成",
                ["cancelled"] = "已取消"
            });
    }

    [Fact]
    public void CalendarQueryService_Falls_Back_To_Chinese_Without_Localization()
    {
        var service = BuildServiceWithLocalization<CalendarQueryService>(null);
        InvokeStringOptions(service, "GetLocalizedEventTypeOptions").Should().BeEquivalentTo(new Dictionary<string, string>
        {
            ["due_date"] = "截止日期",
            ["task"] = "计划任务",
            ["meeting"] = "会议",
            ["reminder"] = "提醒"
        });
        InvokeStringOptions(service, "GetLocalizedStatusOptions").Should().BeEquivalentTo(new Dictionary<string, string>
        {
            ["pending"] = "待处理",
            ["completed"] = "已完成",
            ["cancelled"] = "已取消"
        });
    }

    // ====== 2. 知识库状态目录 ======

    [Fact]
    public void KnowledgeService_StatusOptions_Are_Localized_Per_Culture()
    {
        InvokeStringOptions(BuildServiceWithLocalization<KnowledgeService>(new ResxLocalizationService(En)), "BuildLocalizedStatusOptions")
            .Should().BeEquivalentTo(new Dictionary<string, string> { ["draft"] = "Draft", ["published"] = "Published", ["archived"] = "Archived" });

        InvokeStringOptions(BuildServiceWithLocalization<KnowledgeService>(new ResxLocalizationService(Zh)), "BuildLocalizedStatusOptions")
            .Should().BeEquivalentTo(new Dictionary<string, string> { ["draft"] = "草稿", ["published"] = "已发布", ["archived"] = "已归档" });

        InvokeStringOptions(BuildServiceWithLocalization<KnowledgeService>(null), "BuildLocalizedStatusOptions")
            .Should().BeEquivalentTo(new Dictionary<string, string> { ["draft"] = "草稿", ["published"] = "已发布", ["archived"] = "已归档" });
    }

    // ====== 3. 在线客服会话状态目录 ======

    [Fact]
    public void ChatService_StatusOptions_Are_Localized_Per_Culture()
    {
        InvokeStringOptions(BuildServiceWithLocalization<ChatService>(new ResxLocalizationService(En)), "BuildLocalizedStatusOptions")
            .Should().BeEquivalentTo(new Dictionary<string, string>
            {
                ["waiting"] = "Waiting",
                ["active"] = "Active",
                ["closed"] = "Closed",
                ["transferred"] = "Converted to conversation"
            });

        InvokeStringOptions(BuildServiceWithLocalization<ChatService>(new ResxLocalizationService(Zh)), "BuildLocalizedStatusOptions")
            .Should().BeEquivalentTo(new Dictionary<string, string>
            {
                ["waiting"] = "等待接待",
                ["active"] = "进行中",
                ["closed"] = "已关闭",
                ["transferred"] = "已转工单"
            });

        InvokeStringOptions(BuildServiceWithLocalization<ChatService>(null), "BuildLocalizedStatusOptions")
            .Should().BeEquivalentTo(new Dictionary<string, string>
            {
                ["waiting"] = "等待接待",
                ["active"] = "进行中",
                ["closed"] = "已关闭",
                ["transferred"] = "已转工单"
            });
    }

    // ====== 4. 发送日志状态 / 邮件类型 ======

    [Fact]
    public async Task SendLogService_Status_Options_Are_Localized_Per_Culture()
    {
        var en = await BuildServiceWithLocalization<SendLogService>(new ResxLocalizationService(En)).GetStatusOptionsAsync();
        en.Should().BeEquivalentTo(new Dictionary<int, string>
        {
            [1] = "Received",
            [2] = "Send error",
            [4] = "Delivered",
            [5] = "Delivery failed",
            [6] = "Opened",
            [7] = "Clicked",
            [8] = "Unsubscribed",
            [9] = "Complained",
            [10] = "Send interrupted"
        });

        var zh = await BuildServiceWithLocalization<SendLogService>(new ResxLocalizationService(Zh)).GetStatusOptionsAsync();
        zh.Should().BeEquivalentTo(new Dictionary<int, string>
        {
            [1] = "已接收",
            [2] = "发送错误",
            [4] = "投递成功",
            [5] = "投递失败",
            [6] = "已打开",
            [7] = "已点击",
            [8] = "已退订",
            [9] = "已投诉",
            [10] = "发送中断"
        });

        var withoutLocalization = await BuildServiceWithLocalization<SendLogService>(null).GetStatusOptionsAsync();
        withoutLocalization.Should().BeEquivalentTo(zh);
    }

    [Fact]
    public async Task SendLogService_MailType_Options_Are_Localized_Per_Culture()
    {
        var en = await BuildServiceWithLocalization<SendLogService>(new ResxLocalizationService(En)).GetMailTypeOptionsAsync();
        en.Should().BeEquivalentTo(new Dictionary<int, string>
        {
            [1] = "Customer reply",
            [2] = "User notification",
            [3] = "Auto reply",
            [4] = "Invite",
            [5] = "Password changed",
            [6] = "Wrong user email",
            [7] = "Test",
            [8] = "Alert",
            [10] = "Calendar reminder"
        });

        var zh = await BuildServiceWithLocalization<SendLogService>(new ResxLocalizationService(Zh)).GetMailTypeOptionsAsync();
        zh.Should().BeEquivalentTo(new Dictionary<int, string>
        {
            [1] = "客户回复",
            [2] = "用户通知",
            [3] = "自动回复",
            [4] = "邀请",
            [5] = "密码修改",
            [6] = "错误邮件",
            [7] = "测试",
            [8] = "警报",
            [10] = "日程提醒"
        });

        var withoutLocalization = await BuildServiceWithLocalization<SendLogService>(null).GetMailTypeOptionsAsync();
        withoutLocalization.Should().BeEquivalentTo(zh);
    }

    [Fact]
    public void SendLogService_Row_Labels_And_Unknown_Fallback_Are_Localized()
    {
        var en = BuildServiceWithLocalization<SendLogService>(new ResxLocalizationService(En));
        InvokeStringLabel(en, "GetStatusLabel", 5).Should().Be("Delivery failed");
        InvokeStringLabel(en, "GetMailTypeLabel", 6).Should().Be("Wrong user email");
        InvokeStringLabel(en, "GetStatusLabel", 99).Should().Be("Unknown");
        InvokeStringLabel(en, "GetMailTypeLabel", 99).Should().Be("Unknown");

        var zh = BuildServiceWithLocalization<SendLogService>(new ResxLocalizationService(Zh));
        InvokeStringLabel(zh, "GetStatusLabel", 5).Should().Be("投递失败");
        InvokeStringLabel(zh, "GetMailTypeLabel", 6).Should().Be("错误邮件");
        InvokeStringLabel(zh, "GetStatusLabel", 99).Should().Be("未知");

        var withoutLocalization = BuildServiceWithLocalization<SendLogService>(null);
        InvokeStringLabel(withoutLocalization, "GetStatusLabel", 5).Should().Be("投递失败");
        InvokeStringLabel(withoutLocalization, "GetMailTypeLabel", 99).Should().Be("未知");
    }

    [Fact]
    public void SendLogService_StatusCategory_Stays_Machine_Readable()
    {
        // StatusCategory 是给前端做配色/筛选的机器可读值，不参与本地化。
        var en = BuildServiceWithLocalization<SendLogService>(new ResxLocalizationService(En));
        InvokeStringLabel(en, "GetStatusCategory", 4).Should().Be("success");
        InvokeStringLabel(en, "GetStatusCategory", 5).Should().Be("failed");
        InvokeStringLabel(en, "GetStatusCategory", 1).Should().Be("pending");
        InvokeStringLabel(en, "GetStatusCategory", 8).Should().Be("warning");
        InvokeStringLabel(en, "GetStatusCategory", 99).Should().Be("unknown");
    }

    // ====== 5. 渠道显示名 ======

    [Fact]
    public void CustomFieldService_KnownChannels_Stay_Language_Neutral_But_Labels_Are_Localized()
    {
        var known = (Dictionary<string, string>)typeof(CustomFieldService)
            .GetField("KnownChannels", BindingFlags.Static | BindingFlags.NonPublic)!
            .GetValue(null)!;

        known.Should().BeEquivalentTo(new Dictionary<string, string>
        {
            ["email"] = "邮件",
            ["web"] = "网页",
            ["api"] = "API",
            ["phone"] = "电话",
            ["chat"] = "在线聊天",
            ["social"] = "社交",
            ["wechat"] = "微信",
            ["whatsapp"] = "WhatsApp",
            ["feishu"] = "飞书",
            ["dingtalk"] = "钉钉",
            ["messenger"] = "Messenger",
            ["telegram"] = "Telegram",
            ["line"] = "LINE",
            ["imessage"] = "iMessage"
        }, "KnownChannels 的值必须保持语言中立（zh-Hans 原文，仅作回退源），不能把英文写进字典");

        var en = BuildServiceWithLocalization<CustomFieldService>(new ResxLocalizationService(En));
        InvokeStringLabel(en, "LocalizeChannelLabel", "email", "邮件").Should().Be("Email");
        InvokeStringLabel(en, "LocalizeChannelLabel", "chat", "在线聊天").Should().Be("Live chat");
        InvokeStringLabel(en, "LocalizeChannelLabel", "imessage", "iMessage").Should().Be("Apple Messages");
        InvokeStringLabel(en, "Translate", "Channel_global", "全局（所有渠道）").Should().Be("All channels");

        var zh = BuildServiceWithLocalization<CustomFieldService>(new ResxLocalizationService(Zh));
        InvokeStringLabel(zh, "LocalizeChannelLabel", "wechat", "微信").Should().Be("微信");
        InvokeStringLabel(zh, "LocalizeChannelLabel", "imessage", "iMessage").Should().Be("iMessage");

        var withoutLocalization = BuildServiceWithLocalization<CustomFieldService>(null);
        InvokeStringLabel(withoutLocalization, "LocalizeChannelLabel", "dingtalk", "钉钉").Should().Be("钉钉");
        InvokeStringLabel(withoutLocalization, "Translate", "Channel_global", "全局（所有渠道）").Should().Be("全局（所有渠道）");
    }

    // ====== 6. SLA 时长度量 ======

    [Theory]
    [InlineData(0.0, "0 分钟")]
    [InlineData(0.5, "30 分钟")]
    [InlineData(2.5, "2 小时 30 分钟")]
    [InlineData(20.0, "20 小时")]
    [InlineData(24.0, "1 天")]
    [InlineData(26.0, "1 天 2 小时")]
    public void SlaConst_FormatHours_Without_Translator_Is_Unchanged_Chinese(double hours, string expected)
    {
        // 通知变量与旧构造点（不传委托）必须与改造前逐字一致。
        SlaConst.FormatHours(hours).Should().Be(expected);
    }

    [Theory]
    [InlineData(0.0, "0 min")]
    [InlineData(0.5, "30 min")]
    [InlineData(2.5, "2h 30m")]
    [InlineData(20.0, "20 h")]
    [InlineData(24.0, "1 d")]
    [InlineData(26.0, "1d 2h")]
    public void SlaConst_FormatHours_With_En_Translator_Uses_En_Units(double hours, string expected)
    {
        SlaConst.FormatHours(hours, ResxTranslate(En)).Should().Be(expected);
    }

    [Fact]
    public void SlaService_Translate_Formats_Slots_And_Falls_Back_Without_Localization()
    {
        var en = BuildServiceWithLocalization<SlaService>(new ResxLocalizationService(En));
        InvokeStringLabel(en, "Translate", "SlaDurationHoursMinutes", "{0} 小时 {1} 分钟", new object[] { 2, 30 })
            .Should().Be("2h 30m");

        var zh = BuildServiceWithLocalization<SlaService>(new ResxLocalizationService(Zh));
        InvokeStringLabel(zh, "Translate", "SlaDurationHoursMinutes", "{0} 小时 {1} 分钟", new object[] { 2, 30 })
            .Should().Be("2 小时 30 分钟");

        var withoutLocalization = BuildServiceWithLocalization<SlaService>(null);
        InvokeStringLabel(withoutLocalization, "Translate", "SlaDurationDaysHours", "{0} 天 {1} 小时", new object[] { 1, 2 })
            .Should().Be("1 天 2 小时");
    }

    // ====== 7. 密码校验失败原因 ======

    [Fact]
    public void PasswordPolicyService_Validation_Errors_Are_Localized_Per_Culture()
    {
        var en = BuildServiceWithLocalization<PasswordPolicyService>(new ResxLocalizationService(En));
        InvokeStringLabel(en, "Translate", "PolicyErrorMinLength", "密码长度至少 {0} 位", new object[] { 10 })
            .Should().Be("Password must be at least 10 characters");
        InvokeStringLabel(en, "Translate", "PolicyErrorRequireUppercase", "密码必须包含至少一个大写字母")
            .Should().Be("Password must contain at least one uppercase letter");
        InvokeStringLabel(en, "Translate", "PolicyErrorTooCommon", "密码过于常见，请选择更复杂的密码")
            .Should().Be("Password is too common, please choose a stronger one");

        var zh = BuildServiceWithLocalization<PasswordPolicyService>(new ResxLocalizationService(Zh));
        InvokeStringLabel(zh, "Translate", "PolicyErrorMinLength", "密码长度至少 {0} 位", new object[] { 10 })
            .Should().Be("密码长度至少 10 位", "zh 侧必须用「位」而不是「个字符」");
        InvokeStringLabel(zh, "Translate", "PolicyErrorWeakPattern", "密码包含过于简单的模式，请选择更复杂的密码")
            .Should().Be("密码包含过于简单的模式，请选择更复杂的密码");

        // PasswordPolicyServiceTests.cs 用不带 localizer 的构造点断言中文子串，这两条锁住回退行为。
        var withoutLocalization = BuildServiceWithLocalization<PasswordPolicyService>(null);
        InvokeStringLabel(withoutLocalization, "Translate", "PolicyErrorMinLength", "密码长度至少 {0} 位", new object[] { 10 })
            .Should().Be("密码长度至少 10 位");
        InvokeStringLabel(withoutLocalization, "Translate", "PolicyErrorRequireSymbol", "密码必须包含至少一个特殊字符")
            .Should().Be("密码必须包含至少一个特殊字符");
    }

    // ====== 词条缺失时必须回退中文，而不是把键名吐给前端 ======

    [Fact]
    public void Missing_Resx_Entry_Falls_Back_To_Chinese_Instead_Of_The_Key()
    {
        var missing = new MissingKeyLocalizationService();

        InvokeStringOptions(BuildServiceWithLocalization<CalendarQueryService>(missing), "GetLocalizedEventTypeOptions")["due_date"]
            .Should().Be("截止日期");
        InvokeStringOptions(BuildServiceWithLocalization<KnowledgeService>(missing), "BuildLocalizedStatusOptions")["published"]
            .Should().Be("已发布");
        InvokeStringOptions(BuildServiceWithLocalization<ChatService>(missing), "BuildLocalizedStatusOptions")["transferred"]
            .Should().Be("已转工单");
        InvokeStringLabel(BuildServiceWithLocalization<SendLogService>(missing), "GetMailTypeLabel", 1).Should().Be("客户回复");
        InvokeStringLabel(BuildServiceWithLocalization<SendLogService>(missing), "GetStatusLabel", 99).Should().Be("未知");
        InvokeStringLabel(BuildServiceWithLocalization<CustomFieldService>(missing), "LocalizeChannelLabel", "feishu", "飞书")
            .Should().Be("飞书");
        InvokeStringLabel(BuildServiceWithLocalization<PasswordPolicyService>(missing), "Translate", "PolicyErrorRequireDigit", "密码必须包含至少一个数字")
            .Should().Be("密码必须包含至少一个数字");
        SlaConst.FormatHours(2.5, ResxTranslate(En, missing)).Should().Be("2 小时 30 分钟");
    }

    // ====== 反射辅助：标签方法只依赖注入的本地化服务，因此跳过构造函数直接建实例并填字段 ======

    /// <summary>
    /// 被测标签方法都是「注入的本地化服务」的纯函数，构造函数却需要 UnitOfWork 等真实依赖。
    /// 这里跳过构造函数分配实例，只写入 <c>_localization</c> 字段，避免把标签回归测试写成集成测试。
    /// </summary>
    private static TService BuildServiceWithLocalization<TService>(ILocalizationService? localization)
        where TService : class
    {
        var service = (TService)RuntimeHelpers.GetUninitializedObject(typeof(TService));

        var field = typeof(TService).GetField("_localization", BindingFlags.Instance | BindingFlags.NonPublic);
        field.Should().NotBeNull($"{nameof(TService)} 必须持有可空 _localization 字段（可选尾参数注入）");
        field!.SetValue(service, localization);

        return service;
    }

    private static Dictionary<string, string> InvokeStringOptions<TService>(TService service, string methodName)
        where TService : class
    {
        var method = FindMethod(typeof(TService), methodName);
        return (Dictionary<string, string>)method.Invoke(service, null)!;
    }

    private static string InvokeStringLabel<TService>(TService service, string methodName, params object?[] args)
        where TService : class
    {
        var method = FindMethod(typeof(TService), methodName, args.Length);
        return (string)method.Invoke(service, AlignArguments(method, args))!;
    }

    /// <summary>
    /// 把实参数组对齐到被反射方法的形参个数。<c>Translate(key, zhFallback, params object[] args)</c>
    /// 这类方法的形参是 3 个（含 <c>object[]</c>），而 <see cref="MethodInfo.Invoke"/> 不会像 C# 调用点那样
    /// 自动展开 <c>params</c>：直接传 2 个元素会抛 <see cref="TargetParameterCountException"/>。
    /// 这里未传 params 实参时补一个空数组；传了多于固定形参的实参时把尾部打包进 params 数组。
    /// </summary>
    private static object?[] AlignArguments(MethodInfo method, object?[] args)
    {
        var parameters = method.GetParameters();
        var hasParamArray = parameters.Length > 0
            && Attribute.IsDefined(parameters[^1], typeof(ParamArrayAttribute));

        if (parameters.Length == args.Length)
        {
            return args;
        }

        if (hasParamArray && parameters.Length == args.Length + 1)
        {
            var withEmptyParams = new object?[parameters.Length];
            Array.Copy(args, withEmptyParams, args.Length);
            withEmptyParams[^1] = Array.Empty<object>();
            return withEmptyParams;
        }

        if (hasParamArray && args.Length > parameters.Length - 1)
        {
            var fixedCount = parameters.Length - 1;
            var packed = new object?[parameters.Length];
            Array.Copy(args, packed, fixedCount);
            var rest = new object[args.Length - fixedCount];
            Array.Copy(args, fixedCount, rest, 0, rest.Length);
            packed[^1] = rest;
            return packed;
        }

        throw new InvalidOperationException(
            $"{method.DeclaringType!.Name}.{method.Name} 的形参个数（{parameters.Length}）与测试传入的实参个数（{args.Length}）不匹配");
    }

    private static MethodInfo FindMethod(Type type, string methodName, int? argumentCount = null)
    {
        // 标签方法大多是 private 实例方法，GetStatusCategory 仍是 private static：两种都接受。
        var candidates = type
            .GetMethods(BindingFlags.Instance | BindingFlags.Static | BindingFlags.NonPublic | BindingFlags.Public)
            .Where(candidate => candidate.Name == methodName)
            .ToList();

        candidates.Should().NotBeEmpty($"{type.Name}.{methodName} 必须存在（改名需同步本测试）");

        if (argumentCount is null)
        {
            return candidates.Single();
        }

        // 优先精确匹配形参个数（如 LocalizeChannelLabel 两参、Translate 已带 params 数组时三参），
        // 其次接受「少一个实参、尾部是 params」的重载（由 AlignArguments 补空数组）。
        var method = candidates.FirstOrDefault(candidate => candidate.GetParameters().Length == argumentCount.Value)
            ?? candidates.FirstOrDefault(candidate =>
            {
                var parameters = candidate.GetParameters();
                return parameters.Length > 0
                    && Attribute.IsDefined(parameters[^1], typeof(ParamArrayAttribute))
                    && parameters.Length == argumentCount.Value + 1;
            });

        method.Should().NotBeNull($"{type.Name}.{methodName} 必须存在与 {argumentCount.Value} 个实参匹配的重载");
        return method!;
    }

    /// <summary>复刻服务端的回退口径（词条缺失即回退中文原文），用于校验 <see cref="SlaConst.FormatHours"/> 的委托通路。</summary>
    private static Func<string, string, object[]?, string> ResxTranslate(CultureInfo culture, ILocalizationService? overrideService = null)
    {
        var service = overrideService ?? new ResxLocalizationService(culture);
        return (key, zhFallback, args) =>
        {
            var value = service[key];
            if (string.IsNullOrWhiteSpace(value) || string.Equals(value, key, StringComparison.Ordinal))
            {
                return args is null || args.Length == 0 ? zhFallback : string.Format(zhFallback, args);
            }

            return args is null || args.Length == 0 ? value : string.Format(value, args);
        };
    }

    /// <summary>直读 <c>Messages.resx</c> 的轻量 <see cref="ILocalizationService"/>（不依赖 HttpContext 与数据库词条）。</summary>
    private sealed class ResxLocalizationService : ILocalizationService
    {
        public ResxLocalizationService(CultureInfo culture) => Culture = culture;

        public CultureInfo Culture { get; set; }

        public string this[string key] => Resources.GetString(key, Culture) ?? key;

        public string GetString(string key, params object[] args)
        {
            var value = this[key];
            return args.Length == 0 ? value : string.Format(value, args);
        }

        public string CurrentLanguage => Culture.Name;

        public IReadOnlyList<string> SupportedLanguages => ["en", "zh-Hans"];
    }

    /// <summary>模拟词条缺失：实现约定「找不到时返回键名本身」，服务侧必须显式回退中文。</summary>
    private sealed class MissingKeyLocalizationService : ILocalizationService
    {
        public string this[string key] => key;

        public string GetString(string key, params object[] args) => key;

        public string CurrentLanguage => "en";

        public IReadOnlyList<string> SupportedLanguages => ["en", "zh-Hans"];
    }
}
