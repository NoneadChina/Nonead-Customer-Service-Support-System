using Microsoft.Extensions.Caching.Memory;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using NoneadCSS.Application.Configuration;
using NoneadCSS.Application.DTOs.Responses;
using NoneadCSS.Domain;

namespace NoneadCSS.Application.Services;

/// <summary>
/// 密码策略服务实现 - 从 SystemOption 表 security 分类读取密码策略
/// 策略项：
///   security.password_min_length            (默认 10)
///   security.password_requires_uppercase    (默认 true)
///   security.password_requires_lowercase    (默认 true)
///   security.password_requires_number       (默认 true)
///   security.password_requires_symbol       (默认 true)
///   security.password_check_weak_password   (默认 true)
///   security.password_check_common_patterns (默认 true)
/// </summary>
public class PasswordPolicyService : IPasswordPolicyService
{
    private const string CacheKey = "password_policy";
    private readonly TimeSpan _cacheDuration;

    private const int DefaultMinLength = 10;

    private static readonly HashSet<string> WeakPasswords =
    [
        "123456", "password", "qwerty", "admin", "123456789", "12345678", "12345",
        "1234567", "1234567890", "letmein", "welcome", "monkey", "dragon", "master",
        "login", "abc123", "111111", "000000", "password1", "password123", "qwerty123",
        "asdfgh", "zxcvbnm", "qazwsx", "123qwe", "1qaz2wsx", "123321", "654321",
        "987654321", "iloveyou", "trustno1", "sunshine", "princess", "football",
        "baseball", "soccer", "superman", "batman", "spiderman", "passw0rd",
        "p@ssw0rd", "admin123", "root", "toor", "guest", "test", "test123",
        "demo", "changeme", "123123", "abcabc", "aaaaaa", "qwe123", "qwert",
        "zxcvbn", "asdf", "1234", "password!", "p@ssword", "passw0rd1",
        "admin@123", "123456a", "password2023", "password2024", "password2025",
        "qwertyuiop", "michael", "jennifer", "charlie", "thomas", "jordan",
        "michelle", "jessica", "daniel", "andrew", "joshua", "matthew"
    ];

    private static readonly string[] KeyboardRows =
    [
        "qwertyuiop", "asdfghjkl", "zxcvbnm",
        "qazwsxedcrfvtgbyhnujmikolp",
        "1234567890",
        "poiuytrewq", "lkjhgfdsa", "mnbvcxz"
    ];

    private readonly IUnitOfWork _unitOfWork;
    private readonly IMemoryCache _cache;
    private readonly ILogger<PasswordPolicyService> _logger;
    private readonly ILocalizationService? _localization;

    /// <summary>
    /// 构造密码策略服务。<paramref name="localization"/> 为可选本地化服务：
    /// 未注入（后台任务、旧构造点）时 <c>requirements</c> 回退为改造前的中文原文，输出保持不变。
    /// </summary>
    public PasswordPolicyService(
        IUnitOfWork unitOfWork,
        IMemoryCache cache,
        ILogger<PasswordPolicyService> logger,
        IOptions<CacheOptions> cacheOptions,
        ILocalizationService? localization = null)
    {
        _unitOfWork = unitOfWork;
        _cache = cache;
        _logger = logger;
        _localization = localization;
        _cacheDuration = TimeSpan.FromMinutes(cacheOptions.Value.PasswordPolicyCacheMinutes);
    }

    public async Task<PasswordPolicyResponse> GetPolicyAsync()
    {
        if (_cache.TryGetValue(CacheKey, out PasswordPolicyResponse? cached) && cached != null)
        {
            // 缓存实例保持语言中立：Requirements 每次按当前请求语言重建，
            // 否则先到达的请求会把自己的语言固化进缓存，导致后续请求串语言。
            return WithLocalizedRequirements(cached);
        }

        var policy = await LoadPolicyFromStoreAsync();
        _cache.Set(CacheKey, policy, _cacheDuration);
        return WithLocalizedRequirements(policy);
    }

    public async Task<PasswordValidationResult> ValidateAsync(string password)
    {
        var policy = await GetPolicyAsync();
        var errors = new List<string>();

        if (string.IsNullOrEmpty(password))
        {
            errors.Add("密码不能为空");
            return new PasswordValidationResult { IsValid = false, Errors = errors };
        }

        // 防止超长密码 DoS：BCrypt/PBKDF2 对超长输入消耗大量 CPU
        const int MaxPasswordLength = 1024;
        if (password.Length > MaxPasswordLength)
        {
            errors.Add($"密码长度不能超过 {MaxPasswordLength} 个字符");
            return new PasswordValidationResult { IsValid = false, Errors = errors };
        }

        if (password.Length < policy.MinLength)
        {
            errors.Add(Translate("PolicyErrorMinLength", "密码长度至少 {0} 位", policy.MinLength));
        }

        if (policy.RequireUppercase && !password.Any(char.IsUpper))
        {
            errors.Add(Translate("PolicyErrorRequireUppercase", "密码必须包含至少一个大写字母"));
        }

        if (policy.RequireLowercase && !password.Any(char.IsLower))
        {
            errors.Add(Translate("PolicyErrorRequireLowercase", "密码必须包含至少一个小写字母"));
        }

        if (policy.RequireDigit && !password.Any(char.IsDigit))
        {
            errors.Add(Translate("PolicyErrorRequireDigit", "密码必须包含至少一个数字"));
        }

        if (policy.RequireNonAlphanumeric && !password.Any(c => !char.IsLetterOrDigit(c)))
        {
            errors.Add(Translate("PolicyErrorRequireSymbol", "密码必须包含至少一个特殊字符"));
        }

        if (policy.CheckWeakPassword && IsWeakPassword(password))
        {
            errors.Add(Translate("PolicyErrorTooCommon", "密码过于常见，请选择更复杂的密码"));
        }

        if (policy.CheckCommonPatterns && HasCommonPattern(password))
        {
            errors.Add(Translate("PolicyErrorWeakPattern", "密码包含过于简单的模式，请选择更复杂的密码"));
        }

        return new PasswordValidationResult
        {
            IsValid = errors.Count == 0,
            Errors = errors
        };
    }

    private static bool IsWeakPassword(string password)
    {
        return WeakPasswords.Contains(password.ToLowerInvariant());
    }

    private static bool HasCommonPattern(string password)
    {
        if (password.Length < 4)
            return false;

        var lowerPassword = password.ToLowerInvariant();

        if (HasConsecutiveDigits(lowerPassword, 4))
            return true;

        if (HasConsecutiveLetters(lowerPassword, 4))
            return true;

        if (HasRepeatedCharacters(lowerPassword, 4))
            return true;

        if (HasKeyboardPattern(lowerPassword, 4))
            return true;

        return false;
    }

    private static bool HasConsecutiveDigits(string password, int minLength)
    {
        var count = 1;
        for (var i = 1; i < password.Length; i++)
        {
            if (char.IsDigit(password[i]) && char.IsDigit(password[i - 1]))
            {
                if (password[i] == password[i - 1] + 1 || password[i] == password[i - 1] - 1)
                {
                    count++;
                    if (count >= minLength)
                        return true;
                }
                else
                {
                    count = 1;
                }
            }
            else
            {
                count = 1;
            }
        }
        return false;
    }

    private static bool HasConsecutiveLetters(string password, int minLength)
    {
        var count = 1;
        for (var i = 1; i < password.Length; i++)
        {
            if (char.IsLetter(password[i]) && char.IsLetter(password[i - 1]))
            {
                if (password[i] == password[i - 1] + 1 || password[i] == password[i - 1] - 1)
                {
                    count++;
                    if (count >= minLength)
                        return true;
                }
                else
                {
                    count = 1;
                }
            }
            else
            {
                count = 1;
            }
        }
        return false;
    }

    private static bool HasRepeatedCharacters(string password, int minLength)
    {
        var count = 1;
        for (var i = 1; i < password.Length; i++)
        {
            if (password[i] == password[i - 1])
            {
                count++;
                if (count >= minLength)
                    return true;
            }
            else
            {
                count = 1;
            }
        }
        return false;
    }

    private static bool HasKeyboardPattern(string password, int minLength)
    {
        foreach (var row in KeyboardRows)
        {
            for (var i = 0; i <= row.Length - minLength; i++)
            {
                var pattern = row.Substring(i, minLength);
                if (password.Contains(pattern))
                    return true;
            }
        }
        return false;
    }

    public void InvalidateCache()
    {
        _cache.Remove(CacheKey);
    }

    private async Task<PasswordPolicyResponse> LoadPolicyFromStoreAsync()
    {
        try
        {
            var minLenStr = await _unitOfWork.SystemOptions.GetValueAsync("security.password_min_length");
            var reqUpperStr = await _unitOfWork.SystemOptions.GetValueAsync("security.password_requires_uppercase");
            var reqLowerStr = await _unitOfWork.SystemOptions.GetValueAsync("security.password_requires_lowercase");
            var reqNumberStr = await _unitOfWork.SystemOptions.GetValueAsync("security.password_requires_number");
            var reqSymbolStr = await _unitOfWork.SystemOptions.GetValueAsync("security.password_requires_symbol");
            var checkWeakStr = await _unitOfWork.SystemOptions.GetValueAsync("security.password_check_weak_password");
            var checkPatternsStr = await _unitOfWork.SystemOptions.GetValueAsync("security.password_check_common_patterns");

            var minLen = int.TryParse(minLenStr, out var ml) && ml > 0 ? ml : DefaultMinLength;

            var policy = new PasswordPolicyResponse
            {
                MinLength = minLen,
                RequireUppercase = ParseBool(reqUpperStr, true),
                RequireLowercase = ParseBool(reqLowerStr, true),
                RequireDigit = ParseBool(reqNumberStr, true),
                RequireNonAlphanumeric = ParseBool(reqSymbolStr, true),
                CheckWeakPassword = ParseBool(checkWeakStr, true),
                CheckCommonPatterns = ParseBool(checkPatternsStr, true)
            };

            // Requirements 不写进缓存：由 GetPolicyAsync 按当前请求语言即时生成（WithLocalizedRequirements）
            return policy;
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "读取密码策略失败，使用默认策略");
            var fallback = new PasswordPolicyResponse
            {
                MinLength = DefaultMinLength,
                RequireUppercase = true,
                RequireLowercase = true,
                RequireDigit = true,
                RequireNonAlphanumeric = true,
                CheckWeakPassword = true,
                CheckCommonPatterns = true
            };
            return fallback;
        }
    }

    private static bool ParseBool(string? value, bool defaultValue = false)
    {
        if (string.IsNullOrWhiteSpace(value)) return defaultValue;
        return string.Equals(value, "true", StringComparison.OrdinalIgnoreCase)
            || value == "1"
            || string.Equals(value, "yes", StringComparison.OrdinalIgnoreCase);
    }

    /// <summary>
    /// 复制一份策略并按当前请求语言生成可读描述；缓存中的实例保持语言中立，
    /// 避免先到达的请求把自己的语言固化进缓存后串给其它语言的请求。
    /// </summary>
    private PasswordPolicyResponse WithLocalizedRequirements(PasswordPolicyResponse source) => new()
    {
        MinLength = source.MinLength,
        RequireUppercase = source.RequireUppercase,
        RequireLowercase = source.RequireLowercase,
        RequireDigit = source.RequireDigit,
        RequireNonAlphanumeric = source.RequireNonAlphanumeric,
        CheckWeakPassword = source.CheckWeakPassword,
        CheckCommonPatterns = source.CheckCommonPatterns,
        Requirements = BuildRequirements(source)
    };

    /// <summary>
    /// 密码策略可读描述：走 Messages 资源（en / zh-Hans），英文措辞与前端 en 词条
    /// （settings.password.rule.*）保持一致；词条缺失或未注入本地化服务时回退改造前的中文原文，
    /// 保证 zh-Hans 输出逐字不变（含数字槽位，不硬编码长度）。
    /// </summary>
    private List<string> BuildRequirements(PasswordPolicyResponse policy)
    {
        var list = new List<string> { Translate("PolicyMinLength", "至少 {0} 个字符", policy.MinLength) };
        if (policy.RequireUppercase) list.Add(Translate("PolicyRequireUppercase", "至少一个大写字母 (A-Z)"));
        if (policy.RequireLowercase) list.Add(Translate("PolicyRequireLowercase", "至少一个小写字母 (a-z)"));
        if (policy.RequireDigit) list.Add(Translate("PolicyRequireDigit", "至少一个数字 (0-9)"));
        if (policy.RequireNonAlphanumeric) list.Add(Translate("PolicyRequireSymbol", "至少一个特殊字符 (!@#$%^&*...)"));
        if (policy.CheckWeakPassword) list.Add(Translate("PolicyForbidWeakPassword", "不能使用常见弱密码"));
        if (policy.CheckCommonPatterns) list.Add(Translate("PolicyForbidCommonPatterns", "不能包含简单模式（连续数字/字母、重复字符、键盘行）"));
        return list;
    }

    /// <summary>
    /// 取词条文本并按 <paramref name="args"/> 格式化。本地化服务在词条缺失时会返回键名本身，
    /// 此时回退给定的中文原文（与改造前字面量一致），避免把键名吐给前端。
    /// </summary>
    private string Translate(string key, string zhFallback, params object[] args)
    {
        var text = zhFallback;
        if (_localization is not null)
        {
            var value = _localization[key];
            if (!string.IsNullOrWhiteSpace(value) && !string.Equals(value, key, StringComparison.Ordinal))
            {
                text = value;
            }
        }

        return args.Length == 0 ? text : string.Format(text, args);
    }
}
