using NoneadCSS.Domain.Entities;

namespace NoneadCSS.Infrastructure.Services;

/// <summary>
/// 基于 BCrypt 的密码哈希器实现
/// </summary>
public class BCryptPasswordHasher : IPasswordHasher
{
    /// <summary>
    /// 使用 BCrypt 算法哈希明文密码
    /// </summary>
    /// <param name="password">明文密码</param>
    /// <returns>BCrypt 哈希后的密码</returns>
    public string Hash(string password)
    {
        return BCrypt.Net.BCrypt.HashPassword(password);
    }

    /// <summary>
    /// 使用 BCrypt 算法验证密码
    /// </summary>
    /// <param name="password">明文密码</param>
    /// <param name="hashedPassword">BCrypt 哈希后的密码</param>
    /// <returns>是否匹配</returns>
    public bool Verify(string password, string hashedPassword)
    {
        return BCrypt.Net.BCrypt.Verify(password, hashedPassword);
    }
}
