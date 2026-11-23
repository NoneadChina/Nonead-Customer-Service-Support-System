using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using Microsoft.Extensions.Caching.Distributed;
using NoneadCSS.Application.Services;

namespace NoneadCSS.Infrastructure.Services;

/// <summary>
/// SVG 图形验证码服务实现
/// </summary>
public class CaptchaService : ICaptchaService
{
    private readonly IDistributedCache _cache;
    private const string CachePrefix = "captcha:";
    private const int ExpireMinutes = 5;

    /// <summary>单 ID 最多允许验证失败尝试次数（达到后立即销毁验证码，强制刷新）。</summary>
    private const int MaxFailedAttempts = 3;

    // 验证码字符集（去掉易混淆字符 0/O/1/I）
    private const string CharSet = "23456789ABCDEFGHJKLMNPQRSTUVWXYZ";

    // 颜色数组（深色系，对比度好）
    private static readonly string[] Colors = { "#2563eb", "#dc2626", "#059669", "#d97706", "#7c3aed", "#db2777" };

    /// <summary>缓存值对象：记录验证码和剩余尝试次数，支持跨实例共享（Redis 等共享 IDistributedCache 实现）。</summary>
    private class CaptchaCacheEntry
    {
        public string Code { get; set; } = string.Empty;
        public int AttemptsLeft { get; set; } = MaxFailedAttempts;
    }

    /// <summary>
    /// 构造函数
    /// </summary>
    /// <param name="cache">分布式缓存（单实例部署下使用 AddDistributedMemoryCache；多实例部署下必须切换为 Redis 等共享实现）。</param>
    public CaptchaService(IDistributedCache cache)
    {
        _cache = cache;
    }

    /// <summary>
    /// 生成 SVG 格式验证码
    /// </summary>
    /// <param name="width">验证码宽度</param>
    /// <param name="height">验证码高度</param>
    /// <param name="codeLength">验证码字符长度</param>
    /// <returns>验证码ID和SVG内容</returns>
    public async Task<(string captchaId, string svgContent)> GenerateAsync(int width = 120, int height = 40, int codeLength = 4)
    {
        // 生成随机验证码
        var code = GenerateRandomCode(codeLength);

        // 生成验证码ID（GUID N，无短横线，URL 友好）
        var captchaId = Guid.NewGuid().ToString("N");

        // 写入分布式缓存：保存 code + 尝试次数，统一为不区分大小写比较
        var entry = new CaptchaCacheEntry { Code = code.ToUpperInvariant(), AttemptsLeft = MaxFailedAttempts };
        var payload = JsonSerializer.Serialize(entry);

        await _cache.SetStringAsync(
            CachePrefix + captchaId,
            payload,
            new DistributedCacheEntryOptions
            {
                AbsoluteExpirationRelativeToNow = TimeSpan.FromMinutes(ExpireMinutes)
            }).ConfigureAwait(false);

        // 生成SVG内容
        var svg = GenerateSvg(code, width, height);

        return (captchaId, svg);
    }

    /// <summary>
    /// 验证验证码
    /// </summary>
    /// <param name="captchaId">验证码ID</param>
    /// <param name="code">用户输入的验证码</param>
    /// <returns>验证是否成功</returns>
    public async Task<bool> ValidateAsync(string captchaId, string code)
    {
        if (string.IsNullOrWhiteSpace(captchaId) || string.IsNullOrWhiteSpace(code))
            return false;

        var cacheKey = CachePrefix + captchaId;
        var raw = await _cache.GetStringAsync(cacheKey).ConfigureAwait(false);

        if (string.IsNullOrEmpty(raw))
            return false; // 验证码已过期 / 已被成功消费 / 或达到最大失败次数被销毁

        CaptchaCacheEntry? entry;
        try
        {
            entry = JsonSerializer.Deserialize<CaptchaCacheEntry>(raw);
        }
        catch (JsonException)
        {
            // 兼容老版本纯字符串缓存（仅 code）：首次遇到立即删除并返回 false，让用户刷新
            await _cache.RemoveAsync(cacheKey).ConfigureAwait(false);
            return false;
        }

        if (entry == null || string.IsNullOrEmpty(entry.Code))
        {
            await _cache.RemoveAsync(cacheKey).ConfigureAwait(false);
            return false;
        }

        var input = code.Trim().ToUpperInvariant();
        var matched = string.Equals(entry.Code, input, StringComparison.Ordinal);

        if (matched)
        {
            // 成功：立即销毁验证码（一次性消费，防止重放）
            await _cache.RemoveAsync(cacheKey).ConfigureAwait(false);
            return true;
        }

        // 失败：扣减剩余尝试次数；小于等于 0 则销毁验证码，防止无限爆破
        entry.AttemptsLeft -= 1;
        if (entry.AttemptsLeft <= 0)
        {
            await _cache.RemoveAsync(cacheKey).ConfigureAwait(false);
            return false;
        }

        // 重新写回缓存并刷新过期时间（滑动窗口：失败后仍然保持 ExpireMinutes 生命周期）
        var updated = JsonSerializer.Serialize(entry);
        await _cache.SetStringAsync(
            cacheKey,
            updated,
            new DistributedCacheEntryOptions
            {
                AbsoluteExpirationRelativeToNow = TimeSpan.FromMinutes(ExpireMinutes)
            }).ConfigureAwait(false);

        return false;
    }

    /// <summary>
    /// 生成随机验证码
    /// </summary>
    /// <param name="length">长度</param>
    /// <returns>验证码字符串</returns>
    private static string GenerateRandomCode(int length)
    {
        // 使用加密安全的随机数生成器，避免可预测的验证码
        var sb = new StringBuilder(length);
        for (var i = 0; i < length; i++)
        {
            sb.Append(CharSet[RandomNumberGenerator.GetInt32(0, CharSet.Length)]);
        }
        return sb.ToString();
    }

    /// <summary>
    /// 生成 SVG 验证码图片
    /// </summary>
    /// <param name="code">验证码</param>
    /// <param name="width">宽度</param>
    /// <param name="height">高度</param>
    /// <returns>SVG 字符串</returns>
    private static string GenerateSvg(string code, int width, int height)
    {
        var sb = new StringBuilder();

        // SVG 头部
        sb.Append($"<svg xmlns='http://www.w3.org/2000/svg' width='{width}' height='{height}' viewBox='0 0 {width} {height}'>");

        // 背景
        sb.Append($"<rect width='100%' height='100%' fill='#f8fafc' rx='6' ry='6'/>");

        // 干扰线
        for (var i = 0; i < 4; i++)
        {
            var x1 = RandomNumberGenerator.GetInt32(0, width);
            var y1 = RandomNumberGenerator.GetInt32(0, height);
            var x2 = RandomNumberGenerator.GetInt32(0, width);
            var y2 = RandomNumberGenerator.GetInt32(0, height);
            var color = Colors[RandomNumberGenerator.GetInt32(0, Colors.Length)];
            sb.Append($"<line x1='{x1}' y1='{y1}' x2='{x2}' y2='{y2}' stroke='{color}' stroke-width='1' opacity='0.6'/>");
        }

        // 干扰点
        for (var i = 0; i < 30; i++)
        {
            var cx = RandomNumberGenerator.GetInt32(0, width);
            var cy = RandomNumberGenerator.GetInt32(0, height);
            var r = RandomNumberGenerator.GetInt32(1, 3);
            var color = Colors[RandomNumberGenerator.GetInt32(0, Colors.Length)];
            sb.Append($"<circle cx='{cx}' cy='{cy}' r='{r}' fill='{color}' opacity='0.5'/>");
        }

        // 验证码文字
        var fontSize = (int)(height * 0.6);
        var charWidth = (double)width / code.Length;

        for (var i = 0; i < code.Length; i++)
        {
            var ch = code[i];
            var x = charWidth * i + charWidth / 2;
            var y = height * 0.65;
            var color = Colors[RandomNumberGenerator.GetInt32(0, Colors.Length)];
            var rotate = RandomNumberGenerator.GetInt32(-25, 26);
            var dx = RandomNumberGenerator.GetInt32(-2, 3);
            var dy = RandomNumberGenerator.GetInt32(-3, 4);

            sb.Append($"<text x='{x + dx}' y='{y + dy}' ");
            sb.Append($"font-size='{fontSize}' ");
            sb.Append($"fill='{color}' ");
            sb.Append($"font-family='Arial, Helvetica, sans-serif' ");
            sb.Append($"font-weight='bold' ");
            sb.Append($"text-anchor='middle' ");
            sb.Append($"transform='rotate({rotate} {x + dx} {y + dy})' ");
            sb.Append($"style='user-select: none;'>{ch}</text>");
        }

        // SVG 尾部
        sb.Append("</svg>");

        return sb.ToString();
    }
}
