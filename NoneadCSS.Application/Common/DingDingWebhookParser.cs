using System.Collections.Concurrent;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;

namespace NoneadCSS.Application.Common;

/// <summary>
/// 钉钉机器人 Webhook 消息解析器 - 签名验证、消息解析、去重。
/// 钉钉机器人 HTTP 回调签名算法：HmacSHA256(timestamp + "\n" + appSecret) → Base64
/// </summary>
public static class DingDingWebhookParser
{
    // 使用 ConcurrentDictionary 替代 HashSet + SemaphoreSlim，无锁并发读，容量超阈值时清理
    // ⚠️ 部署说明（M-S3）：此去重缓存为进程内内存，仅适用于单实例部署。
    // 多实例/横向扩展场景下需改为分布式缓存（如 Redis），否则同一 MsgId 可能被不同实例重复处理。
    private static readonly ConcurrentDictionary<string, byte> ProcessedMsgIds = new();
    private const int MaxCacheSize = 2000;

    /// <summary>
    /// 验证钉钉机器人 Webhook 签名。
    /// 签名算法：HmacSHA256(timestamp + "\n" + appSecret) → Base64 编码
    /// timestamp 与当前时间差 > 5 分钟 → 拒绝（防重放攻击）
    /// 使用时间常数比较防止计时侧信道
    /// </summary>
    public static bool VerifySignature(string? timestamp, string? sign, string appSecret)
    {
        if (string.IsNullOrEmpty(timestamp) || string.IsNullOrEmpty(sign) || string.IsNullOrEmpty(appSecret))
            return false;

        // 时间戳校验（毫秒级）
        if (!long.TryParse(timestamp, out var ts))
            return false;

        var tsDateTime = DateTimeOffset.FromUnixTimeMilliseconds(ts).UtcDateTime;
        if (Math.Abs(DateTimeOffset.UtcNow.UtcDateTime.Subtract(tsDateTime).TotalMinutes) > 5)
            return false;

        // 计算签名
        var stringToSign = timestamp + "\n" + appSecret;
        using var hmac = new HMACSHA256(Encoding.UTF8.GetBytes(appSecret));
        var hashBytes = hmac.ComputeHash(Encoding.UTF8.GetBytes(stringToSign));
        var computedSign = Convert.ToBase64String(hashBytes);

        // 时间常数比较，防止计时侧信道
        return CryptographicOperations.FixedTimeEquals(
            Encoding.UTF8.GetBytes(computedSign),
            Encoding.UTF8.GetBytes(sign));
    }

    /// <summary>
    /// 检查消息是否为重复（基于 msgId 去重，缓存上限 2000 条，超限时清理旧条目）。
    /// </summary>
    public static Task<bool> IsDuplicateAsync(string msgId)
    {
        if (string.IsNullOrEmpty(msgId))
            return Task.FromResult(false);

        // TryAdd 返回 false 表示已存在（重复消息）
        var isDuplicate = !ProcessedMsgIds.TryAdd(msgId, 0);

        // 容量超阈值时清理（保留最近条目，避免无限增长导致 OOM）
        if (ProcessedMsgIds.Count > MaxCacheSize)
        {
            // 清理一半旧条目（ConcurrentDictionary 不保证顺序，但清理后仍有足够空间）
            var keysToRemove = ProcessedMsgIds.Keys.Take(MaxCacheSize / 2).ToList();
            foreach (var key in keysToRemove)
            {
                ProcessedMsgIds.TryRemove(key, out _);
            }
        }

        return Task.FromResult(isDuplicate);
    }

    /// <summary>
    /// 解析钉钉机器人接收消息 JSON。
    /// </summary>
    public static DingDingIncomingMessage? ParseMessage(string payload)
    {
        try
        {
            using var doc = JsonDocument.Parse(payload);
            var root = doc.RootElement;

            var msgType = root.TryGetProperty("msgtype", out var mtEl) ? mtEl.GetString() : "text";
            var msgId = root.TryGetProperty("msgId", out var idEl) ? idEl.GetString() : null;
            var conversationType = root.TryGetProperty("conversationType", out var ctEl) ? ctEl.GetString() : "1";
            var conversationId = root.TryGetProperty("conversationId", out var cidEl) ? cidEl.GetString() : null;
            var senderStaffId = root.TryGetProperty("senderStaffId", out var ssEl) ? ssEl.GetString() : null;
            var senderNick = root.TryGetProperty("senderNick", out var snEl) ? snEl.GetString() : null;
            var senderCorpId = root.TryGetProperty("senderCorpId", out var scEl) ? scEl.GetString() : null;
            var createAt = root.TryGetProperty("createAt", out var caEl) ? caEl.GetInt64() : 0;
            var sessionWebhook = root.TryGetProperty("sessionWebhook", out var swEl) ? swEl.GetString() : null;
            var sessionWebhookExpiredTime = root.TryGetProperty("sessionWebhookExpiredTime", out var swetEl) ? swetEl.GetInt64() : 0;
            var isAdmin = root.TryGetProperty("isAdmin", out var iaEl) && iaEl.GetBoolean();
            var conversationId2 = root.TryGetProperty("chatbotUserId", out var cuEl) ? cuEl.GetString() : null;

            // 提取消息内容
            string? textContent = null;
            string? downloadCode = null;
            string? fileName = null;
            string? recognition = null;
            int? duration = null;

            if (msgType == "text" && root.TryGetProperty("text", out var textEl))
            {
                textContent = textEl.TryGetProperty("content", out var cEl) ? cEl.GetString()?.Trim() : null;
            }
            else if (msgType == "picture" && root.TryGetProperty("content", out var picEl))
            {
                downloadCode = picEl.TryGetProperty("downloadCode", out var dcEl) ? dcEl.GetString() : null;
            }
            else if (msgType == "audio" && root.TryGetProperty("content", out var audioEl))
            {
                downloadCode = audioEl.TryGetProperty("downloadCode", out var dcEl) ? dcEl.GetString() : null;
                recognition = audioEl.TryGetProperty("recognition", out var rEl) ? rEl.GetString() : null;
                duration = audioEl.TryGetProperty("duration", out var dEl) ? dEl.GetInt32() : null;
            }
            else if (msgType == "video" && root.TryGetProperty("content", out var videoEl))
            {
                downloadCode = videoEl.TryGetProperty("downloadCode", out var dcEl) ? dcEl.GetString() : null;
                duration = videoEl.TryGetProperty("duration", out var dEl) ? dEl.GetInt32() : null;
            }
            else if (msgType == "file" && root.TryGetProperty("content", out var fileEl))
            {
                downloadCode = fileEl.TryGetProperty("downloadCode", out var dcEl) ? dcEl.GetString() : null;
                fileName = fileEl.TryGetProperty("fileName", out var fnEl) ? fnEl.GetString() : null;
            }
            else if (msgType == "richText" && root.TryGetProperty("content", out var rtEl))
            {
                // 富文本：提取文本部分
                if (rtEl.TryGetProperty("richText", out var arrEl) && arrEl.ValueKind == JsonValueKind.Array)
                {
                    var sb = new StringBuilder();
                    foreach (var item in arrEl.EnumerateArray())
                    {
                        if (item.TryGetProperty("text", out var tEl))
                            sb.Append(tEl.GetString());
                    }
                    textContent = sb.ToString();
                }
            }

            return new DingDingIncomingMessage
            {
                MsgId = msgId,
                MsgType = msgType,
                ConversationType = conversationType,
                ConversationId = conversationId,
                SenderStaffId = senderStaffId,
                SenderNick = senderNick,
                SenderCorpId = senderCorpId,
                CreateAt = createAt,
                SessionWebhook = sessionWebhook,
                SessionWebhookExpiredTime = sessionWebhookExpiredTime,
                IsAdmin = isAdmin,
                ChatbotUserId = conversationId2,
                TextContent = textContent,
                DownloadCode = downloadCode,
                FileName = fileName,
                Recognition = recognition,
                Duration = duration
            };
        }
        catch (Exception)
        {
            return null;
        }
    }
}

/// <summary>
/// 钉钉机器人接收消息解析结果
/// </summary>
public class DingDingIncomingMessage
{
    public string? MsgId { get; set; }
    public string? MsgType { get; set; }
    public string? ConversationType { get; set; }
    public string? ConversationId { get; set; }
    public string? SenderStaffId { get; set; }
    public string? SenderNick { get; set; }
    public string? SenderCorpId { get; set; }
    public long CreateAt { get; set; }
    public string? SessionWebhook { get; set; }
    public long SessionWebhookExpiredTime { get; set; }
    public bool IsAdmin { get; set; }
    public string? ChatbotUserId { get; set; }

    // 消息内容
    public string? TextContent { get; set; }
    public string? DownloadCode { get; set; }
    public string? FileName { get; set; }
    public string? Recognition { get; set; }
    public int? Duration { get; set; }

    /// <summary>是否为单聊</summary>
    public bool IsSingleChat => ConversationType == "1";

    /// <summary>消息创建时间（UTC）</summary>
    public DateTime CreatedAtUtc =>
        CreateAt > 0 ? DateTimeOffset.FromUnixTimeMilliseconds(CreateAt).UtcDateTime : DateTime.UtcNow;
}
