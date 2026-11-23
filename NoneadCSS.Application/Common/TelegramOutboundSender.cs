using System.Text.RegularExpressions;
using NoneadCSS.Application.Configuration;
using NoneadCSS.Application.DTOs.Responses;
using NoneadCSS.Application.Services;

namespace NoneadCSS.Application.Common;

/// <summary>
/// Telegram 文本出站的共享发送逻辑。
///
/// 背景：坐席手工回复（<see cref="Services.TelegramService"/>）与出站队列
/// （<c>ChatChannelMessageSender</c> 的 Telegram 分支）原先各写一套——队列分支直接调用
/// <see cref="ITelegramClient"/>，因此绕过了 4096 字符分片、<see cref="TelegramThrottle"/> 限速、
/// 403 不可达标记与 429 retry_after 处理，表现为「手工回复正常、排队消息静默失败或刷屏」。
/// 两条链路必须共用同一份实现，所以把限速/重试/失败分类收敛到这里。
/// </summary>
internal static class TelegramOutboundSender
{
    /// <summary>
    /// 从我们自己的入站线程 Body 中还原论坛话题 id。
    /// ConversationThread 没有独立的 topic 列，话题 id 只存在于 BuildThreadHtmlBody 渲染的
    /// <c>&lt;div class="telegram-topic"&gt;话题 #123&lt;/div&gt;</c> 标记里；
    /// 发送回复时必须把它带回 message_thread_id，否则回复会落到论坛的「General」而不是原话题。
    /// </summary>
    private static readonly Regex ForumTopicPattern = new(
        "class=\"telegram-topic\">话题 #(?<id>\\d+)",
        RegexOptions.Compiled | RegexOptions.CultureInvariant);

    /// <summary>按渠道配置的最大字符数分片（Telegram 官方单条上限 4096）。</summary>
    public static IReadOnlyList<string> SplitText(string text, TelegramOptions options)
        => TelegramWebhookParser.SplitLongMessage(text, options.MaxMessageChars);

    /// <summary>从线程 Body 提取论坛话题 id；不存在/为 0 时返回 null（发送侧会省略该字段）。</summary>
    public static int? ExtractForumTopicId(string? threadBody)
    {
        if (string.IsNullOrEmpty(threadBody))
        {
            return null;
        }

        var match = ForumTopicPattern.Match(threadBody);
        if (!match.Success)
        {
            return null;
        }

        return int.TryParse(match.Groups["id"].Value, out var topicId) && topicId > 0 ? topicId : null;
    }

    /// <summary>
    /// 受速率限制保护的文本发送：
    ///   1. 先按令牌桶计算等待时间，超过 MaxLocalBackoffSeconds 直接返回（交回消息队列延后重试，避免长时间占用 worker）；
    ///   2. 429/420 且 retry_after ≤ MaxLocalBackoffSeconds 时本地等待后重试一次（官方建议按 retry_after + 抖动退避）。
    /// 返回值仍是 <see cref="TelegramSendResult"/>，Blocked/Unreachable/InvalidToken/Conflict 等分类由调用方决定如何落库或标记。
    /// </summary>
    public static async Task<TelegramSendResult> SendTextWithThrottleAsync(
        ITelegramClient telegramClient,
        TelegramThrottle throttle,
        string chatId,
        string text,
        string? replyToMessageId,
        TelegramOptions options,
        int? messageThreadId = null,
        string? parseMode = null,
        CancellationToken cancellationToken = default)
    {
        // 非数字 chat_id（例如 @channelusername）在限速表里统一归到 0 号键，避免每次 new 出一个桶。
        var chatKey = long.TryParse(chatId, out var numericChatId) ? numericChatId : 0;

        // 原子预约：检查配额与记账在 TelegramThrottle 的同一把锁里完成（审计 #6）。
        // 旧写法是 PeekWaitMilliseconds → Task.Delay → RegisterSend 三步分离，两个坐席同时 Peek
        // 都会判定"还能发"，于是全局 25 条/秒与单 chat 1 条/秒都可能被成倍突破。
        // 预约成功即已占用配额，WaitMilliseconds 表示这一笔被记到了多久之后的合法槽位。
        var maxLocalWaitMs = options.MaxLocalBackoffSeconds * 1000;
        var reservation = throttle.TryReserveSend(chatKey, maxLocalWaitMs);
        if (!reservation.Granted)
        {
            // 等待成本超过本地退避上限：不占用配额，直接返回让上层按 RetryAfterSeconds 延后重试
            return new TelegramSendResult
            {
                Success = false,
                RetryAfterSeconds = (int)Math.Ceiling(reservation.WaitMilliseconds / 1000d),
                ErrorMessage = $"触发本地速率限制，建议 {Math.Ceiling(reservation.WaitMilliseconds / 1000d):0} 秒后重试"
            };
        }

        if (reservation.WaitMilliseconds > 0)
        {
            await Task.Delay(reservation.WaitMilliseconds, cancellationToken);
        }

        var result = await telegramClient.SendTextMessageAsync(
            chatId, text, replyToMessageId, cancellationToken, messageThreadId, parseMode);

        if (!result.Success && result.RetryAfterSeconds is > 0 && result.RetryAfterSeconds.Value <= options.MaxLocalBackoffSeconds)
        {
            var retryAfter = result.RetryAfterSeconds.Value;
            throttle.RegisterRetryAfter(chatKey, retryAfter, chatScoped: result.ErrorCode == 420);
            await Task.Delay(TimeSpan.FromSeconds(retryAfter), cancellationToken);

            // 重试这一笔的等待时长由 Telegram 的 retry_after 决定（服务端已限流），
            // 因此这里沿用"补记账"而不重新预约，避免把已经付过的退避时间再算一遍。
            throttle.RegisterSend(chatKey);
            result = await telegramClient.SendTextMessageAsync(
                chatId, text, replyToMessageId, cancellationToken, messageThreadId, parseMode);
        }

        return result;
    }
}
