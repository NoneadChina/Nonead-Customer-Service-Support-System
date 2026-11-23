using NoneadCSS.Application.Services;
using FluentAssertions;
using Microsoft.Extensions.Logging.Abstractions;
using System.Globalization;
using System.Resources;
using Xunit;

namespace NoneadCSS.Tests.Localization;

/// <summary>
/// 导入/导出实体目录（<c>GET /api/ImportExport/entities</c>）的双语判据。
/// 该目录由服务端直接下发显示文本（响应出口词典只改写 message / errorMessage / detail，不会翻译数组元素字段），
/// 因此必须由 <c>Messages.resx</c> 提供 en / zh-Hans 两套词条。
/// 英文措辞与前端 <c>data.importExport.entity.*</c>（frontend/src/locales/en/data.ts:899-914）逐字一致，
/// 避免出现第二套英文；中文侧必须与改造前的代码字面量逐字一致。
/// </summary>
public class ImportExportCatalogLocalizationTests
{
    private const string MessagesBaseName = "NoneadCSS.Application.Resources.Messages";

    private static readonly ResourceManager Resources = new(MessagesBaseName, typeof(ILocalizationService).Assembly);

    private static readonly CultureInfo En = new("en");
    private static readonly CultureInfo Zh = new("zh-Hans");

    /// <summary>键名 / en 值 / zh-Hans 值（zh 值即改造前 <c>ImportExportService.cs:36-43</c> 的字面量）。</summary>
    private static readonly (string Key, string En, string Zh)[] Catalog =
    [
        ("ImportEntity_customers", "Customers", "客户"),
        ("ImportEntityDesc_customers", "Customer contact information", "客户联系人信息"),
        ("ImportEntity_tags", "Tags", "标签"),
        ("ImportEntityDesc_tags", "Conversation tags", "会话标签"),
        ("ImportEntity_canned_responses", "Canned responses", "快捷回复"),
        ("ImportEntityDesc_canned_responses", "Canned response templates", "快捷回复模板"),
        ("ImportEntity_mail_templates", "Mail templates", "邮件模板"),
        ("ImportEntityDesc_mail_templates", "Email notification templates", "邮件通知模板"),
        ("ImportEntity_email_signatures", "Email signatures", "邮件签名"),
        ("ImportEntityDesc_email_signatures", "Mailbox signatures", "邮箱签名"),
        ("ImportEntity_conversations", "Conversations", "工单"),
        ("ImportEntityDesc_conversations", "Conversation list (bodies and attachments excluded)", "工单列表（不含正文与附件）"),
        ("ImportEntity_knowledge_articles", "Knowledge base articles", "知识库文章"),
        ("ImportEntityDesc_knowledge_articles", "Article title / category / status / body (Markdown text)", "文章标题/分类/状态/正文（Markdown 文本）"),
        ("ImportEntity_users", "Users", "用户"),
        ("ImportEntityDesc_users", "System users", "系统用户"),
    ];

    /// <summary>直接用真实 resx 取值的本地化服务，行为与生产实现一致：取不到就返回键本身。</summary>
    private sealed class ResxLocalizationService : ILocalizationService
    {
        private readonly CultureInfo _culture;
        public ResxLocalizationService(CultureInfo culture) => _culture = culture;
        public string this[string key] => Resources.GetString(key, _culture) ?? key;
        public string GetString(string key, params object[] args)
            => args.Length == 0 ? this[key] : string.Format(_culture, this[key], args);
        public string CurrentLanguage => _culture.Name;
        public IReadOnlyList<string> SupportedLanguages { get; } = new[] { "zh-Hans", "en" };
    }

    private static ImportExportService CreateService(ILocalizationService? localization)
        => new(null!, NullLogger<ImportExportService>.Instance, null!, null!, localization);

    [Fact]
    public void Messages_Resx_Carries_Every_Import_Export_Catalog_Key_In_Both_Cultures()
    {
        foreach (var (key, en, zh) in Catalog)
        {
            var enValue = Resources.GetString(key, En);
            enValue.Should().Be(en, $"{key} 的英文词条应与前端 en 措辞一致");
            enValue!.Should().NotMatchRegex(".*[\\u3400-\\u9FFF].*", $"{key} 的英文词条不得含中日韩字符");

            var zhValue = Resources.GetString(key, Zh);
            zhValue.Should().Be(zh, $"{key} 的 zh-Hans 词条必须与改造前的中文代码字面量逐字一致");
        }
    }

    [Fact]
    public void English_Request_Culture_Returns_English_Entity_Catalog()
    {
        var entities = CreateService(new ResxLocalizationService(En)).GetAvailableEntitiesAsync().GetAwaiter().GetResult();

        entities.Should().HaveCount(8);
        entities.Should().OnlyContain(e => !ContainsHan(e.Name) && !ContainsHan(e.Description));
        entities.Single(e => e.Type == "customers").Name.Should().Be("Customers");
        entities.Single(e => e.Type == "conversations").Description
            .Should().Be("Conversation list (bodies and attachments excluded)");
    }

    [Fact]
    public void Chinese_Request_Culture_Keeps_The_Original_Chinese_Wording()
    {
        var entities = CreateService(new ResxLocalizationService(Zh)).GetAvailableEntitiesAsync().GetAwaiter().GetResult();

        entities.Single(e => e.Type == "customers").Name.Should().Be("客户");
        entities.Single(e => e.Type == "customers").Description.Should().Be("客户联系人信息");
        entities.Single(e => e.Type == "conversations").Name.Should().Be("工单");
        entities.Single(e => e.Type == "conversations").Description.Should().Be("工单列表（不含正文与附件）");
        entities.Single(e => e.Type == "knowledge_articles").Description.Should().Be("文章标题/分类/状态/正文（Markdown 文本）");
    }

    [Fact]
    public void Missing_Localization_Service_Falls_Back_To_Chinese_Instead_Of_Throwing()
    {
        // 后台作业与旧构造调用点没有本地化服务时，目录仍必须可用（不能把键名吐出去）。
        var entities = CreateService(null).GetAvailableEntitiesAsync().GetAwaiter().GetResult();

        entities.Should().HaveCount(8);
        entities.Should().OnlyContain(e => ContainsHan(e.Name) && ContainsHan(e.Description) || AlwaysAscii(e.Name) && AlwaysAscii(e.Description));
        entities.Should().NotContain(e => e.Name.StartsWith("ImportEntity"));
        entities.Single(e => e.Type == "users").Name.Should().Be("用户");
    }

    private static bool ContainsHan(string value) => value.Any(c => c >= 0x3400 && c <= 0x9FFF);
    private static bool AlwaysAscii(string value) => value.All(c => c < 0x80);
}
