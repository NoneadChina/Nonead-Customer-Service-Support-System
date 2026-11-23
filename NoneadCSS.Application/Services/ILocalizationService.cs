namespace NoneadCSS.Application.Services;

/// <summary>
/// 本地化服务接口，用于根据当前请求语言获取翻译字符串
/// </summary>
public interface ILocalizationService
{
    /// <summary>
    /// 获取指定键的本地化字符串
    /// </summary>
    string this[string key] { get; }

    /// <summary>
    /// 获取指定键的本地化字符串，并格式化参数
    /// </summary>
    string GetString(string key, params object[] args);

    /// <summary>
    /// 获取当前请求所使用的语言代码（如 en、zh-Hans）
    /// </summary>
    string CurrentLanguage { get; }

    /// <summary>
    /// 获取所有支持的语言代码
    /// </summary>
    IReadOnlyList<string> SupportedLanguages { get; }
}
