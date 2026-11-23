using FluentAssertions;
using Moq;
using NoneadCSS.Application.Common;
using NoneadCSS.Application.Configuration;
using NoneadCSS.Application.DTOs.Responses;
using NoneadCSS.Application.Services;

namespace NoneadCSS.Tests.Services;

/// <summary>
/// Telegram 出站限流的并发口径测试（审计 #6）。
/// 时钟用固定委托注入，因此"记账落在哪个时刻"完全可断言，不依赖真实 sleep 的长短。
/// </summary>
public class TelegramThrottleTests
{
    private const long FakeNowMs = 1_763_100_000_000;
    private const long ChatId = 5551234567890;

    // ---------------- 原子预约：检查配额 + 记账在同一把锁里 ----------------

    [Fact]
    public void TryReserveSend_ConcurrentSameChat_KeepsOneMessagePerSecondWindow()
    {
        // 单 chat 1 条/秒：六个坐席同时预约，旧写法（PeekWait 再 RegisterSend）会让它们全部判定"还能发"
        var throttle = new TelegramThrottle(25, 1, () => FakeNowMs);

        var reservations = new ThrottleReservation[6];
        Parallel.For(0, 6, i => reservations[i] = throttle.TryReserveSend(ChatId, 60_000));

        reservations.Should().OnlyContain(r => r.Granted);

        // 记账时刻必须逐条排开：任意 1 秒窗口内最多 1 条
        var slots = reservations.Select(r => FakeNowMs + r.WaitMilliseconds).OrderBy(t => t).ToList();
        for (var i = 1; i < slots.Count; i++)
        {
            (slots[i] - slots[i - 1]).Should().BeGreaterThanOrEqualTo(1000);
        }
    }

    [Fact]
    public void TryReserveSend_ConcurrentAcrossChats_RespectsGlobalPerSecondCeiling()
    {
        // 全局 5 条/秒（用小数便于断言窗口），单 chat 放宽：跨 chat 并发也只能按全局上限排队
        var throttle = new TelegramThrottle(5, 1000, () => FakeNowMs);

        var reservations = new ThrottleReservation[12];
        Parallel.For(0, 12, i => reservations[i] = throttle.TryReserveSend(ChatId + i, 60_000));

        reservations.Should().OnlyContain(r => r.Granted);

        // 任意 1 秒窗口内不超过 5 条：排序后第 i+5 条必须比第 i 条晚至少 1 秒
        var slots = reservations.Select(r => FakeNowMs + r.WaitMilliseconds).OrderBy(t => t).ToList();
        for (var i = 0; i + 5 < slots.Count; i++)
        {
            (slots[i + 5] - slots[i]).Should().BeGreaterThanOrEqualTo(1000);
        }
    }

    [Fact]
    public void TryReserveSend_WhenWaitExceedsCap_DoesNotAccountQuota()
    {
        var throttle = new TelegramThrottle(25, 1, () => FakeNowMs);
        throttle.RegisterSend(ChatId);

        // 需要等 1 秒，但本地上限为 0：拒绝预约，且不得占用配额
        var rejected = throttle.TryReserveSend(ChatId, 0);
        rejected.Granted.Should().BeFalse();
        rejected.WaitMilliseconds.Should().Be(1000);
        throttle.PeekWaitMilliseconds(ChatId).Should().Be(1000);

        // 上限放宽后同一条消息仍能拿到槽位（说明刚才被拒的预约没留下水印）
        var granted = throttle.TryReserveSend(ChatId, 1000);
        granted.Granted.Should().BeTrue();
        granted.WaitMilliseconds.Should().Be(1000);
    }

    [Fact]
    public void TryReserveSend_RespectsChatRetryAfterBackoff()
    {
        var throttle = new TelegramThrottle(25, 1, () => FakeNowMs);
        throttle.RegisterRetryAfter(ChatId, 30, chatScoped: true);

        // 退避未结束前不得提前占用配额：本地上限不足时直接拒绝
        var rejected = throttle.TryReserveSend(ChatId, 10_000);
        rejected.Granted.Should().BeFalse();
        rejected.WaitMilliseconds.Should().Be(30_000);

        // 允许等待时，槽位落在退避结束的那一刻，而不是 now
        var granted = throttle.TryReserveSend(ChatId, 30_000);
        granted.Granted.Should().BeTrue();
        (FakeNowMs + granted.WaitMilliseconds).Should().Be(FakeNowMs + 30_000);
    }

    // ---------------- 发送链路改用预约后，同一条 chat 不会双双落在同一时刻 ----------------

    [Fact]
    public async Task SendTextWithThrottleAsync_TwoConcurrentSends_SameChat_NeverShareOneSecondWindow()
    {
        var throttle = new TelegramThrottle(25, 1, () => FakeNowMs);
        var client = new Mock<ITelegramClient>();
        client.Setup(c => c.SendTextMessageAsync(
                It.IsAny<string>(), It.IsAny<string>(), It.IsAny<string?>(),
                It.IsAny<CancellationToken>(), It.IsAny<int?>(), It.IsAny<string?>()))
            .ReturnsAsync(new TelegramSendResult { Success = true, MessageId = "77" });

        var options = new TelegramOptions { BotToken = "123456:AAHtoken", MaxLocalBackoffSeconds = 60 };

        var first = TelegramOutboundSender.SendTextWithThrottleAsync(client.Object, throttle, ChatId.ToString(), "a", null, options);
        var second = TelegramOutboundSender.SendTextWithThrottleAsync(client.Object, throttle, ChatId.ToString(), "b", null, options);
        var results = await Task.WhenAll(first, second);

        results.Should().OnlyContain(r => r.Success);
        client.Verify(c => c.SendTextMessageAsync(
            It.IsAny<string>(), It.IsAny<string>(), It.IsAny<string?>(),
            It.IsAny<CancellationToken>(), It.IsAny<int?>(), It.IsAny<string?>()), Times.Exactly(2));

        // 两条都被记账（旧写法 Peek→Register 会把两笔都落在 now，下一次预约只会等到 1000ms）
        var next = throttle.TryReserveSend(ChatId, 0);
        next.Granted.Should().BeFalse();
        next.WaitMilliseconds.Should().Be(2000);
    }

    [Fact]
    public async Task SendTextWithThrottleAsync_WhenReservationRefused_DoesNotCallTelegramAndKeepsQuota()
    {
        var throttle = new TelegramThrottle(25, 1, () => FakeNowMs);
        throttle.RegisterRetryAfter(ChatId, 30, chatScoped: true);
        var client = new Mock<ITelegramClient>();

        var options = new TelegramOptions { BotToken = "123456:AAHtoken", MaxLocalBackoffSeconds = 10 };

        var result = await TelegramOutboundSender.SendTextWithThrottleAsync(client.Object, throttle, ChatId.ToString(), "a", null, options);

        result.Success.Should().BeFalse();
        result.RetryAfterSeconds.Should().Be(30);
        result.ErrorMessage.Should().Contain("速率限制").And.Contain("30 秒");
        client.Verify(c => c.SendTextMessageAsync(
            It.IsAny<string>(), It.IsAny<string>(), It.IsAny<string?>(),
            It.IsAny<CancellationToken>(), It.IsAny<int?>(), It.IsAny<string?>()), Times.Never);

        // 被拒的发送没有额外占用配额：等待时长仍由 retry_after 决定
        throttle.PeekWaitMilliseconds(ChatId).Should().Be(30_000);
    }
}
