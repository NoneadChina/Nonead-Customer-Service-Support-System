using Microsoft.EntityFrameworkCore.Storage.ValueConversion;
using NoneadCSS.Domain.Services;

namespace NoneadCSS.Infrastructure.Persistence;

public class EncryptedStringConverter : ValueConverter<string, string>
{
    public EncryptedStringConverter(IEncryptionService encryptionService)
        : base(
            v => encryptionService.Encrypt(v),
            v => DecryptSafely(encryptionService, v))
    {
    }

    private static string DecryptSafely(IEncryptionService encryptionService, string cipherText)
    {
        if (encryptionService.TryDecrypt(cipherText, out var plainText))
        {
            return plainText ?? string.Empty;
        }
        // S-H1: 解密失败时返回空字符串，而非原始密文，防止 Base64 密文泄露到应用层或 API 响应
        return string.Empty;
    }
}
