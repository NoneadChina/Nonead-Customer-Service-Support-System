using System.Text.Json;
using FluentAssertions;
using NoneadCSS.Api.Localization;
using Xunit;

namespace NoneadCSS.Tests.Localization;

/// <summary>
/// 「前缀键」规则判据（见 <see cref="ApiMessageLocalizer"/> 的 LocalizePrefix 注释）。
///
/// 背景：源码里存在一批「固定中文前缀 + 动态详情」的拼接错误文案，最典型的是
/// NoneadCSS.Infrastructure/Services/Line/LineClient.cs 的
/// <c>"LINE 请求异常：" + ex.Message</c> 与 Telegram 客户端的 <c>"getMe 异常：" + ex.Message</c>。
/// 这类消息整句不可能命中词典，动态部分也不是数字或引号串，所以归一化同样命中不了 ——
/// 结果就是英文界面里夹着一段中文。词典里这类键的特征是以全角冒号结尾且英文值不含 {N} 槽位，
/// 出口层据此把前缀换英文、原样接上后面的详情，并在冒号后补一个空格（避免出现
/// "LINE request exception:System…" 这种粘连）。
///
/// 本文件锁三件事：命中能翻、位置不对不能翻（防误翻客户数据）、词典里这类键的形状约束成立。
/// </summary>
public class ApiMessagePrefixKeyTests
{
    private const string LinePrefixKey = "LINE 请求异常：";
    private const string GetMePrefixKey = "getMe 异常：";

    private static Dictionary<string, string> LoadDictionary()
    {
        ApiMessageLocalizer.SourcePath.Should().NotBeNullOrEmpty("词典找不到时整套英文替换会被静默禁用");
        return JsonSerializer.Deserialize<Dictionary<string, string>>(
            File.ReadAllText(ApiMessageLocalizer.SourcePath!))!;
    }

    [Fact]
    public void PrefixKeys_AreShippedAndHaveNoSlots()
    {
        var entries = LoadDictionary();

        entries.Should().ContainKey(LinePrefixKey);
        entries.Should().ContainKey(GetMePrefixKey);
        foreach (var key in new[] { LinePrefixKey, GetMePrefixKey })
        {
            key.Should().EndWith("：", "前缀键必须以全角冒号结尾，这是运行期识别前缀的唯一约定");
            entries[key].Should().NotContain("{", "带槽位的英文值不能走前缀规则，否则回填逻辑会错位");
        }
    }

    [Fact]
    public void LineClientConcatenation_IsTranslatedWithSeparatingSpace()
    {
        const string detail = "System.Net.Http.HttpRequestException: No such host is known. (api.line.me:443)";

        var result = ApiMessageLocalizer.Localize(LinePrefixKey + detail, "en");

        result.Should().NotBeNull("固定前缀 + 动态详情的拼接必须走前缀规则，否则英文界面夹中文");
        result.Should().StartWith("LINE request exception: ", "冒号后要补空格，避免 exception:System 粘连");
        result.Should().EndWith(detail, "动态详情（异常原文）不得被改写或截断");
    }

    [Fact]
    public void GetMeConcatenation_IsTranslated()
    {
        var result = ApiMessageLocalizer.Localize(GetMePrefixKey + "The request was canceled due to the configured HttpClient.Timeout.", "en");

        result.Should().NotBeNull();
        result.Should().StartWith("getMe exception: ");
    }

    [Fact]
    public void PrefixAppearingMidMessage_IsNotTranslated()
    {
        // 客户数据或包装过的句子中途出现这些字，不能被前缀规则误翻。
        var result = ApiMessageLocalizer.Localize("上游回调报告 " + LinePrefixKey + "timeout", "en");

        result.Should().BeNull("前缀规则只认消息开头，中途命中一律不改写");
    }

    [Fact]
    public void ExactPrefixKey_StillResolvesAsExactMatch()
    {
        var result = ApiMessageLocalizer.Localize(LinePrefixKey, "en");

        result.Should().Be("LINE request exception:", "整句等于前缀键时走精确匹配，不应追加空格");
    }

    [Fact]
    public void NonEnglishCulture_LeavesTextUntouched()
    {
        ApiMessageLocalizer.Localize(LinePrefixKey + "timeout", "zh-Hans").Should().BeNull();
        ApiMessageLocalizer.Localize(LinePrefixKey + "timeout", null).Should().BeNull();
    }
}
