using NoneadCSS.Api.Authorization;
using NoneadCSS.Application.Services;
using NoneadCSS.Domain;
using NoneadCSS.Domain.Entities;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using System.Collections;
using System.Globalization;
using System.Reflection;
using System.Resources;
using NoneadCSS.Api.Filters;

namespace NoneadCSS.Api.Controllers;

/// <summary>
/// 本地化控制器 - 提供多语言翻译查询和管理
/// </summary>
[ApiController]
[Route("api/[controller]")]
[Authorize]
[RequireModule("core")]
public class LocalizationController : ControllerBase
{
    /// <summary>
    /// 后端词条真源（`NoneadCSS.Application/Resources/Messages*.resx`）的唯一读取入口。
    ///
    /// **必须用编译后的资源名**：.resx 编译进程序集后名字是
    /// `NoneadCSS.Application.Resources.Messages.resources`（扩展名 `.resources`，不是 `.resx`）；
    /// 区域性变体（`zh-Hans`）不在主程序集里，而在附属程序集
    /// `zh-Hans\NoneadCSS.Application.resources.dll`，由 <see cref="ResourceManager"/> 按 culture 自动探测。
    ///
    /// 历史缺陷（2026-10-10 修）：此前用 `assembly.GetManifestResourceStream("…Messages{后缀}.resx")`
    /// 手工读流、并用 `resourceName.Contains("Messages.resx")` 过滤 `GetManifestResourceNames()`，
    /// 两处都恒取不到 → `/keys` 空、`/stats` 恒 0、`/diff` 恒 0、`/export` 导出空文件（连累导入报「导入内容为空」）。
    ///
    /// 为什么不走 `IStringLocalizer`：它按 `CultureInfo.CurrentUICulture` 解析，而 `/stats`
    /// 需要在一次请求里同时取多个语言；`IStringLocalizerFactory` 的 `WithCulture` 在 .NET 7+ 已移除。
    /// <see cref="ResourceManager"/> 与 `LocalizationService` 读的是同一份嵌入资源，且能显式指定 culture。
    /// </summary>
    private static readonly ResourceManager MessagesResourceManager =
        new("NoneadCSS.Application.Resources.Messages", typeof(NoneadCSS.Application.DependencyInjection).Assembly);

    private readonly ILocalizationService _localizer;
    private readonly IUnitOfWork _unitOfWork;
    private readonly ILogger<LocalizationController> _logger;

    public LocalizationController(
        ILocalizationService localizer,
        IUnitOfWork unitOfWork,
        ILogger<LocalizationController> logger)
    {
        _localizer = localizer;
        _unitOfWork = unitOfWork;
        _logger = logger;
    }

    /// <summary>
    /// 获取支持的语言列表
    /// </summary>
    /// <returns>当前语言及所有支持的语言</returns>
    [HttpGet("languages")]
    public IActionResult GetSupportedLanguages()
    {
        return Ok(new
        {
            current = _localizer.CurrentLanguage,
            supported = _localizer.SupportedLanguages
        });
    }

    /// <summary>
    /// 获取指定键的本地化字符串
    /// </summary>
    /// <param name="key">翻译键（如 LoginFailed）</param>
    /// <returns>键对应的本地化字符串</returns>
    [HttpGet("translate")]
    public IActionResult Translate([FromQuery] string key)
    {
        if (string.IsNullOrEmpty(key))
        {
            return BadRequest(new { message = "key 参数不能为空" });
        }

        return Ok(new
        {
            key,
            value = _localizer[key],
            language = _localizer.CurrentLanguage
        });
    }

    /// <summary>
    /// 批量获取多个键的本地化字符串
    /// </summary>
    /// <param name="keys">翻译键数组</param>
    /// <returns>键值对形式的翻译结果</returns>
    [HttpPost("translate-batch")]
    [RequireSuperAdmin]
    public IActionResult TranslateBatch([FromBody] string[] keys)
    {
        var result = keys.ToDictionary(k => k, k => _localizer[k]);
        return Ok(new
        {
            language = _localizer.CurrentLanguage,
            translations = result
        });
    }

    /// <summary>
    /// 获取常用枚举的本地化字典
    /// </summary>
    /// <remarks>返回会话状态、用户角色、线程类型、文件夹类型的本地化文本，便于前端展示。</remarks>
    /// <returns>枚举本地化字典</returns>
    [HttpGet("enums")]
    public IActionResult GetEnumTranslations()
    {
        return Ok(new
        {
            language = _localizer.CurrentLanguage,
            conversationStatus = new
            {
                Active = _localizer["StatusActive"],
                Pending = _localizer["StatusPending"],
                Closed = _localizer["StatusClosed"],
                Spam = _localizer["StatusSpam"]
            },
            userRole = new
            {
                Admin = _localizer["RoleAdmin"],
                User = _localizer["RoleUser"]
            },
            threadType = new
            {
                Customer = _localizer["ThreadTypeCustomer"],
                Message = _localizer["ThreadTypeMessage"],
                Note = _localizer["ThreadTypeNote"]
            },
            folderType = new
            {
                Unassigned = _localizer["FolderUnassigned"],
                Mine = _localizer["FolderMine"],
                Closed = _localizer["FolderClosed"]
            }
        });
    }

    /// <summary>
    /// 获取所有翻译键列表（从资源文件）
    /// </summary>
    /// <returns>所有可用的翻译键</returns>
    [HttpGet("keys")]
    [Authorize]
    [RequireSuperAdmin]
    public IActionResult GetAllTranslationKeys()
    {
        var keys = GetResourceFileKeys();
        return Ok(keys);
    }

    /// <summary>
    /// 获取指定语言的所有翻译（数据库优先）
    /// </summary>
    /// <remarks>
    /// **必须登录**（不再标 <c>AllowAnonymous</c>）：该端点会回吐 DB 里的全部词条覆盖，
    /// 匿名可读属无谓的信息面暴露。未登录外壳（登录页 / 公开客服小部件）改用内置真源文案，
    /// 见 `I18nContext` 的未登录守卫。
    /// </remarks>
    /// <param name="culture">语言代码（zh-Hans 或 en）</param>
    /// <returns>该语言的所有翻译键值对</returns>
    [HttpGet("translations/{culture}")]
    public async Task<IActionResult> GetTranslationsByCulture(string culture)
    {
        if (!_localizer.SupportedLanguages.Contains(culture))
        {
            return BadRequest(new { message = $"不支持的语言: {culture}" });
        }

        var dbTranslations = await _unitOfWork.Translations.GetByCultureAsync(culture);
        var dbDict = dbTranslations.ToDictionary(t => t.Key, t => t.Value);

        var resourceTranslations = LoadTranslationsForCulture(culture);

        // 合并：数据库优先，资源文件补充
        var merged = new Dictionary<string, string>(resourceTranslations);
        foreach (var (key, value) in dbDict)
        {
            merged[key] = value;
        }

        return Ok(new { culture, translations = merged });
    }

    /// <summary>
    /// 获取翻译差异（未翻译的键）
    /// </summary>
    /// <param name="culture">目标语言代码</param>
    /// <returns>已翻译和未翻译的键列表</returns>
    [HttpGet("diff/{culture}")]
    [Authorize]
    [RequireSuperAdmin]
    public async Task<IActionResult> GetTranslationDiff(string culture)
    {
        if (!_localizer.SupportedLanguages.Contains(culture))
        {
            return BadRequest(new { message = $"不支持的语言: {culture}" });
        }

        var baseTranslations = LoadTranslationsForCulture("zh-Hans");
        var dbTranslations = await _unitOfWork.Translations.GetByCultureAsync(culture);
        var dbKeys = dbTranslations.Select(t => t.Key).ToHashSet();

        var translatedKeys = baseTranslations.Keys.Where(k => dbKeys.Contains(k)).ToList();
        var untranslatedKeys = baseTranslations.Keys.Where(k => !dbKeys.Contains(k)).ToList();

        return Ok(new
        {
            culture,
            total = baseTranslations.Count,
            translated = translatedKeys.Count,
            untranslated = untranslatedKeys.Count,
            translatedKeys,
            untranslatedKeys
        });
    }

    /// <summary>
    /// 更新或创建翻译
    /// </summary>
    [HttpPut("translations/{culture}")]
    [RequireSuperAdmin]
    public async Task<IActionResult> UpdateTranslation(string culture, [FromBody] Dictionary<string, string> translations)
    {
        if (!_localizer.SupportedLanguages.Contains(culture))
        {
            return BadRequest(new { message = $"不支持的语言: {culture}" });
        }

        var updatedCount = 0;
        foreach (var (key, value) in translations)
        {
            var existing = await _unitOfWork.Translations.GetByKeyAndCultureTrackedAsync(key, culture);
            if (existing != null)
            {
                existing.Value = value;
                existing.UpdatedAt = DateTime.UtcNow;
                // existing 已通过 tracked 查询，由 SaveChangesAsync 持久化，无需 UpdateAsync
            }
            else
            {
                await _unitOfWork.Translations.AddAsync(new Translation
                {
                    Key = key,
                    Value = value,
                    Culture = culture,
                    CreatedAt = DateTime.UtcNow,
                    UpdatedAt = DateTime.UtcNow
                });
            }
            updatedCount++;
        }

        await _unitOfWork.SaveChangesAsync();
        return Ok(new { message = $"成功更新 {updatedCount} 个翻译", updatedCount });
    }

    /// <summary>
    /// 导出翻译
    /// </summary>
    [HttpPost("translations/{culture}/export")]
    [RequireSuperAdmin]
    [SkipApiResponseWrapper]
    public async Task<IActionResult> ExportTranslations(string culture)
    {
        if (!_localizer.SupportedLanguages.Contains(culture))
        {
            return BadRequest(new { message = $"不支持的语言: {culture}" });
        }

        var dbTranslations = await _unitOfWork.Translations.GetByCultureAsync(culture);
        var dbDict = dbTranslations.ToDictionary(t => t.Key, t => t.Value);
        var resourceTranslations = LoadTranslationsForCulture(culture);

        var merged = new Dictionary<string, string>(resourceTranslations);
        foreach (var (key, value) in dbDict)
        {
            merged[key] = value;
        }

        var json = System.Text.Json.JsonSerializer.Serialize(merged, new System.Text.Json.JsonSerializerOptions { WriteIndented = true });

        return File(System.Text.Encoding.UTF8.GetBytes(json), "application/json", $"translations_{culture}.json");
    }

    /// <summary>
    /// 导入翻译
    /// </summary>
    [HttpPost("translations/{culture}/import")]
    [RequireSuperAdmin]
    public async Task<IActionResult> ImportTranslations(string culture, [FromBody] Dictionary<string, string>? translations)
    {
        if (!_localizer.SupportedLanguages.Contains(culture))
        {
            return BadRequest(new { message = $"不支持的语言: {culture}" });
        }

        if (translations == null || translations.Count == 0)
        {
            return BadRequest(new { message = "导入内容为空" });
        }

        var importedCount = 0;
        foreach (var (key, value) in translations)
        {
            if (string.IsNullOrEmpty(key)) continue;
            var existing = await _unitOfWork.Translations.GetByKeyAndCultureTrackedAsync(key, culture);
            if (existing != null)
            {
                existing.Value = value;
                existing.UpdatedAt = DateTime.UtcNow;
                // existing 已通过 tracked 查询，由 SaveChangesAsync 持久化，无需 UpdateAsync
            }
            else
            {
                await _unitOfWork.Translations.AddAsync(new Translation
                {
                    Key = key,
                    Value = value,
                    Culture = culture,
                    CreatedAt = DateTime.UtcNow,
                    UpdatedAt = DateTime.UtcNow
                });
            }
            importedCount++;
        }

        await _unitOfWork.SaveChangesAsync();
        return Ok(new { message = $"成功导入 {importedCount} 个翻译", importedCount });
    }

    /// <summary>
    /// 获取翻译统计
    /// </summary>
    /// <remarks>
    /// 返回的 <c>total/translated/untranslated/percentage</c> 以**后端 resx 词条集**为分母；
    /// 翻译管理页的词条集还包含前端 UI 键（`frontend/src/locales/*`，数千条），
    /// 因此页面另行叠加 <c>uiKeyCount + total</c>，并用 <c>dbKeyCount</c>（DB 覆盖行数）作为"已定制"数。
    /// </remarks>
    [HttpGet("stats")]
    [Authorize]
    [RequireSuperAdmin]
    public async Task<IActionResult> GetTranslationStatistics()
    {
        var baseTranslations = LoadTranslationsForCulture("zh-Hans");
        var totalKeys = baseTranslations.Count;

        var stats = new List<object>();
        foreach (var lang in _localizer.SupportedLanguages)
        {
            var dbTranslations = await _unitOfWork.Translations.GetByCultureAsync(lang);
            // 空值行不算"已定制"（前端覆盖层里空串会把真源文案抹成空白，属无效配置）
            var customised = dbTranslations.Where(t => !string.IsNullOrEmpty(t.Value)).ToList();
            var dbKeys = customised.Select(t => t.Key).ToHashSet(StringComparer.Ordinal);
            var translated = baseTranslations.Keys.Count(k => dbKeys.Contains(k));
            var untranslated = totalKeys - translated;

            stats.Add(new
            {
                culture = lang,
                total = totalKeys,
                translated,
                untranslated,
                percentage = totalKeys > 0 ? Math.Round((double)translated / totalKeys * 100, 2) : 0,
                dbKeyCount = customised.Count
            });
        }

        return Ok(new { totalKeys, languages = stats });
    }

    /// <summary>
    /// 后端资源文件里的全部词条键（各受支持语言的并集）。两个 resx 必须键数相等：
    /// <c>NoneadCSS.Application/Resources/Messages.resx</c>（中性=英文）与 <c>Messages.zh-Hans.resx</c>，
    /// 当前各 139 条。新增词条只改一处会让另一语言回落到键名，因此两侧同步是硬性要求。
    /// </summary>
    private List<string> GetResourceFileKeys()
    {
        var keys = new SortedSet<string>(StringComparer.Ordinal);
        foreach (var culture in _localizer.SupportedLanguages)
        {
            foreach (var key in LoadTranslationsForCulture(culture).Keys)
            {
                keys.Add(key);
            }
        }

        return keys.ToList();
    }

    /// <summary>
    /// 读取指定语言的后端词条真源（resx）。
    /// 资源名与附属程序集探测见 <see cref="MessagesResourceManager"/> 的说明。
    /// </summary>
    private Dictionary<string, string> LoadTranslationsForCulture(string culture)
    {
        var translations = new Dictionary<string, string>(StringComparer.Ordinal);

        try
        {
            // tryParents=true：en 没有独立资源集，落到中性资源（Messages.resources 本身就是英文）；
            // zh-Hans 命中附属程序集里的 Messages.zh-Hans.resources。
            var resourceSet = MessagesResourceManager.GetResourceSet(
                new CultureInfo(culture),
                createIfNotExists: true,
                tryParents: true);

            if (resourceSet == null)
            {
                _logger.LogWarning("后端翻译资源为空：culture={Culture}", culture);
                return translations;
            }

            foreach (DictionaryEntry entry in resourceSet)
            {
                if (entry.Key is string key && entry.Value is string value
                    && !string.IsNullOrEmpty(key) && !string.IsNullOrEmpty(value))
                {
                    translations[key] = value;
                }
            }
        }
        catch (MissingManifestResourceException ex)
        {
            // 资源名写错时这里会抛（历史缺陷即如此）；如实记录，不让统计/导出静默退化成空数据
            _logger.LogError(ex, "读取后端翻译资源失败：culture={Culture}", culture);
        }

        return translations;
    }
}
