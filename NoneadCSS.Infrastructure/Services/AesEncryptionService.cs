using System.Security.Cryptography;
using System.Text;
using NoneadCSS.Domain.Services;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;

namespace NoneadCSS.Infrastructure.Services;

public class AesEncryptionService : IEncryptionService
{
    private readonly byte[] _key;
    private readonly byte[]? _legacyKey;
    private readonly ILogger<AesEncryptionService> _logger;
    private const int NonceSize = 12;
    private const int TagSize = 16;
    private int _legacyFallbackLogged;

    public AesEncryptionService(IConfiguration configuration, IHostEnvironment environment, ILogger<AesEncryptionService> logger)
    {
        _logger = logger;
        // 优先从环境变量读取（生产环境强制使用环境变量）
        var keyString = Environment.GetEnvironmentVariable("ENCRYPTION__Key")
            ?? Environment.GetEnvironmentVariable("ENCRYPTION_KEY")
            ?? configuration["Encryption:Key"];

        // 开发环境：未配置时自动生成随机密钥（每次启动随机，不用于生产）
        if (string.IsNullOrEmpty(keyString))
        {
            if (environment.IsDevelopment())
            {
                var devKeyBytes = RandomNumberGenerator.GetBytes(32);
                keyString = Convert.ToBase64String(devKeyBytes);
                _logger.LogWarning("[DEV] 已自动生成开发环境加密密钥（每次启动随机，请勿用于生产环境）");
                _logger.LogWarning("[DEV] 如需固定密钥，请设置 ENCRYPTION__Key 环境变量");
            }
            else
            {
                throw new InvalidOperationException(
                    "加密密钥未配置。请设置 ENCRYPTION__Key 环境变量。" +
                    "生产环境必须使用环境变量。");
            }
        }

        // C-O2: 使用 HKDF 进行密钥派生，替代直接 SHA256 摘要
        // HKDF 提供标准的密钥派生流程（Extract + Expand），安全性优于直接哈希
        _key = HKDF.DeriveKey(
            HashAlgorithmName.SHA256,
            Encoding.UTF8.GetBytes(keyString),
            32,
            null,
            Encoding.UTF8.GetBytes("NoneadCSS-AES256-Encryption-v1"));

        // 保留旧密钥（SHA256 摘要）用于向后兼容解密已有数据
        // 新数据始终用 HKDF 密钥加密，旧数据解密时先尝试 HKDF，失败则回退到 SHA256
        using var sha256 = SHA256.Create();
        _legacyKey = sha256.ComputeHash(Encoding.UTF8.GetBytes(keyString));

        // S-H2: 启动时提醒旧密钥仍存在，建议执行迁移计划
        _logger.LogWarning("已加载旧版 SHA256 加密密钥用于向后兼容解密。建议执行密钥迁移计划，将历史数据重新加密为 HKDF 密钥后移除旧密钥回退逻辑。");
    }

    public string Encrypt(string plainText)
    {
        if (string.IsNullOrEmpty(plainText))
            return plainText;

        var plainBytes = Encoding.UTF8.GetBytes(plainText);
        var nonce = new byte[NonceSize];
        var tag = new byte[TagSize];
        var ciphertext = new byte[plainBytes.Length];

        using var aesGcm = new AesGcm(_key, TagSize);
        RandomNumberGenerator.Fill(nonce);
        aesGcm.Encrypt(nonce, plainBytes, ciphertext, tag);

        var result = new byte[NonceSize + ciphertext.Length + TagSize];
        Buffer.BlockCopy(nonce, 0, result, 0, NonceSize);
        Buffer.BlockCopy(ciphertext, 0, result, NonceSize, ciphertext.Length);
        Buffer.BlockCopy(tag, 0, result, NonceSize + ciphertext.Length, TagSize);

        return Convert.ToBase64String(result);
    }

    public string Decrypt(string cipherText)
    {
        if (string.IsNullOrEmpty(cipherText))
            return cipherText;

        var cipherBytes = Convert.FromBase64String(cipherText);
        if (cipherBytes.Length < NonceSize + TagSize)
            throw new CryptographicException("Invalid ciphertext format.");

        var nonce = new byte[NonceSize];
        var tag = new byte[TagSize];
        var ciphertext = new byte[cipherBytes.Length - NonceSize - TagSize];

        Buffer.BlockCopy(cipherBytes, 0, nonce, 0, NonceSize);
        Buffer.BlockCopy(cipherBytes, NonceSize, ciphertext, 0, ciphertext.Length);
        Buffer.BlockCopy(cipherBytes, NonceSize + ciphertext.Length, tag, 0, TagSize);

        // 先尝试 HKDF 派生密钥解密（新数据）
        try
        {
            using var aesGcmNew = new AesGcm(_key, TagSize);
            var plainBytes = new byte[ciphertext.Length];
            aesGcmNew.Decrypt(nonce, ciphertext, tag, plainBytes);
            return Encoding.UTF8.GetString(plainBytes);
        }
        catch (CryptographicException) when (_legacyKey != null)
        {
            // S-H2: 记录旧密钥回退事件（仅首次告警，避免日志洪泛），便于运维监控迁移进度
            if (Interlocked.CompareExchange(ref _legacyFallbackLogged, 1, 0) == 0)
            {
                _logger.LogWarning("HKDF 密钥解密失败，已回退到旧 SHA256 密钥解密历史数据。建议执行密钥迁移计划，将旧数据重新加密为 HKDF 密钥。此警告仅记录一次。");
            }
            // C-O2 向后兼容：HKDF 解密失败时回退到旧 SHA256 派生密钥（解密历史数据）
            using var aesGcmLegacy = new AesGcm(_legacyKey, TagSize);
            var plainBytes = new byte[ciphertext.Length];
            aesGcmLegacy.Decrypt(nonce, ciphertext, tag, plainBytes);
            return Encoding.UTF8.GetString(plainBytes);
        }
    }

    public bool TryDecrypt(string cipherText, out string? plainText)
    {
        try
        {
            plainText = Decrypt(cipherText);
            return true;
        }
        catch
        {
            plainText = null;
            return false;
        }
    }
}
