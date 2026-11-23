using System.Security.Cryptography;
using System.Text;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using NoneadCSS.Domain.Entities;

namespace NoneadCSS.Infrastructure.Services;

/// <summary>
/// 审计日志 HMAC-SHA256 签名器，用于防止审计记录被事后篡改。
/// 密钥从 Audit:HmacKey 配置派生，回退到 Encryption:Key（带域分隔符），
/// 开发环境自动生成随机密钥。
/// </summary>
public sealed class AuditHmacSigner
{
    private readonly byte[] _hmacKey;
    private readonly ILogger<AuditHmacSigner> _logger;

    // 域分隔符：确保即便与 Encryption:Key 相同，派生出的 HMAC 密钥也不同
    private const string KeyDomainSeparator = "AuditLog:Hmac:v1";

    public AuditHmacSigner(IConfiguration configuration, IHostEnvironment environment, ILogger<AuditHmacSigner> logger)
    {
        _logger = logger;
        var keyString = Environment.GetEnvironmentVariable("AUDIT__HmacKey")
            ?? Environment.GetEnvironmentVariable("AUDIT_HMAC_KEY")
            ?? configuration["Audit:HmacKey"];

        if (string.IsNullOrEmpty(keyString))
        {
            // 回退到 Encryption:Key（带域分隔符），避免新增必填配置项
            keyString = Environment.GetEnvironmentVariable("ENCRYPTION__Key")
                ?? Environment.GetEnvironmentVariable("ENCRYPTION_KEY")
                ?? configuration["Encryption:Key"];
        }

        if (string.IsNullOrEmpty(keyString))
        {
            if (environment.IsDevelopment())
            {
                var devKeyBytes = RandomNumberGenerator.GetBytes(32);
                keyString = Convert.ToBase64String(devKeyBytes);
                _logger.LogWarning("[DEV] 已自动生成开发环境审计 HMAC 密钥（每次启动随机，请勿用于生产环境）");
            }
            else
            {
                throw new InvalidOperationException(
                    "审计 HMAC 密钥未配置。请设置 AUDIT__HmacKey 或 ENCRYPTION__Key 环境变量。");
            }
        }

        // 使用 HKDF-like 派生：SHA256(域分隔符 || 密钥)，确保密钥长度归一化且域隔离
        using var sha256 = SHA256.Create();
        var separatorBytes = Encoding.UTF8.GetBytes(KeyDomainSeparator);
        var keyBytes = Encoding.UTF8.GetBytes(keyString);
        var combined = new byte[separatorBytes.Length + keyBytes.Length];
        Buffer.BlockCopy(separatorBytes, 0, combined, 0, separatorBytes.Length);
        Buffer.BlockCopy(keyBytes, 0, combined, separatorBytes.Length, keyBytes.Length);
        _hmacKey = sha256.ComputeHash(combined);
    }

    /// <summary>
    /// 对审计日志的关键字段计算 HMAC-SHA256 签名。
    /// 签名覆盖：UserId、Action、EntityType、EntityId、CreatedAt。
    /// </summary>
    public string ComputeSignature(AuditLog log)
    {
        var payload = BuildSignaturePayload(log);
        var payloadBytes = Encoding.UTF8.GetBytes(payload);
        using var hmac = new HMACSHA256(_hmacKey);
        var hash = hmac.ComputeHash(payloadBytes);
        return Convert.ToBase64String(hash);
    }

    /// <summary>
    /// 验证审计日志的签名是否匹配（检测篡改）。
    /// </summary>
    public bool VerifySignature(AuditLog log)
    {
        if (string.IsNullOrEmpty(log.HmacSignature))
            return false;

        var expected = ComputeSignature(log);
        return CryptographicOperations.FixedTimeEquals(
            Encoding.UTF8.GetBytes(expected),
            Encoding.UTF8.GetBytes(log.HmacSignature));
    }

    private static string BuildSignaturePayload(AuditLog log)
    {
        // 使用 | 分隔字段，防止字段拼接歧义
        // CreatedAt 使用 ISO 8601 round-trip 格式，避免时区/精度差异。
        // 注意：写入时 CreatedAt 来自 DateTime.UtcNow（Kind=Utc，ToString("O") 带 Z 后缀）；
        // 但 EF 从 SQLite 读回后 Kind=Unspecified，ToString("O") 不带 Z 后缀，
        // 导致写入与校验时生成的载荷不一致、HMAC 永远不匹配。
        // 这里统一按 Utc 渲染，确保签名与校验载荷一致（SpecifyKind 不偏移时间）。
        var createdAt = log.CreatedAt.Kind == DateTimeKind.Utc
            ? log.CreatedAt
            : DateTime.SpecifyKind(log.CreatedAt, DateTimeKind.Utc);
        return string.Join("|",
            log.UserId?.ToString() ?? "null",
            log.Action,
            log.EntityType,
            log.EntityId?.ToString() ?? "null",
            createdAt.ToString("O"));
    }
}
