using System.Security.Cryptography;
using System.Text;

namespace NoneadCSS.Api.Services;

/// <summary>
/// 冷启动一次性安装令牌（M-07）。
/// 令牌持久化到 Data/setup-token.txt：首次启动生成，后续启动复用，
/// 由 SetupTokenBootstrapService 在应用启动时打印一次，供运维调用 /api/setup 使用。
/// </summary>
public interface ISetupTokenService
{
    string CurrentToken { get; }

    string TokenFilePath { get; }

    /// <summary>
    /// 使用常量时间比较校验候选令牌，防止时序攻击。
    /// 令牌被作废（安装完成后调用 <see cref="Invalidate"/>）后一律校验失败。
    /// </summary>
    bool Validate(string? candidate);

    /// <summary>
    /// 作废安装令牌：删除令牌文件并使内存令牌失效。
    /// 安装完成后调用，防止持有旧令牌（或恢复旧备份）的攻击者二次创建超管账号。
    /// </summary>
    void Invalidate();
}

public sealed class SetupTokenService : ISetupTokenService
{
    private readonly string _token;
    private readonly string _tokenFilePath;
    private bool _invalidated;

    public SetupTokenService(IWebHostEnvironment env)
    {
        _tokenFilePath = Path.Combine(env.ContentRootPath, "Data", "setup-token.txt");
        _token = LoadOrCreateToken(_tokenFilePath);
    }

    public string CurrentToken => _token;

    public string TokenFilePath => _tokenFilePath;

    public bool Validate(string? candidate)
    {
        if (_invalidated)
        {
            return false;
        }

        if (string.IsNullOrEmpty(candidate))
        {
            return false;
        }

        var provided = Encoding.UTF8.GetBytes(candidate);
        var expected = Encoding.UTF8.GetBytes(_token);

        return provided.Length == expected.Length
            && CryptographicOperations.FixedTimeEquals(provided, expected);
    }

    public void Invalidate()
    {
        _invalidated = true;
        try
        {
            if (File.Exists(_tokenFilePath))
            {
                File.Delete(_tokenFilePath);
            }
        }
        catch (Exception ex)
        {
            Console.Error.WriteLine($"[SETUP-TOKEN] 删除令牌文件失败（内存令牌已作废）: {ex.Message}");
        }
    }

    /// <summary>
    /// 读取已有令牌；若文件不存在或内容为空，则生成新的随机令牌并落盘。
    /// 落盘失败时不阻断启动，仅记录错误，令牌只在本次运行内有效。
    /// </summary>
    private static string LoadOrCreateToken(string filePath)
    {
        try
        {
            if (File.Exists(filePath))
            {
                var existing = File.ReadAllText(filePath).Trim();
                if (!string.IsNullOrEmpty(existing))
                {
                    return existing;
                }
            }
        }
        catch (Exception ex)
        {
            Console.Error.WriteLine($"[SETUP-TOKEN] 读取令牌文件失败: {ex.Message}，将重新生成。");
        }

        var token = GenerateToken();
        try
        {
            var directory = Path.GetDirectoryName(filePath);
            if (!string.IsNullOrEmpty(directory))
            {
                Directory.CreateDirectory(directory);
            }

            File.WriteAllText(filePath, token);
        }
        catch (Exception ex)
        {
            Console.Error.WriteLine($"[SETUP-TOKEN] 写入令牌文件失败: {ex.Message}，令牌仅在本次运行内有效。");
        }

        return token;
    }

    private static string GenerateToken()
    {
        var bytes = RandomNumberGenerator.GetBytes(32);
        return $"setup-{Convert.ToHexString(bytes).ToLowerInvariant()}";
    }
}
