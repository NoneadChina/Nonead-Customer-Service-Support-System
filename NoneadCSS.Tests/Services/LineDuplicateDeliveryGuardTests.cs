using FluentAssertions;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using Moq;
using NoneadCSS.Application.Configuration;
using NoneadCSS.Application.Services;
using NoneadCSS.Infrastructure.Services.Line;
using NoneadCSS.Tests.Helpers;
using System.Net;
using Xunit;

namespace NoneadCSS.Tests.Services;

/// <summary>
/// LINE 重复投递守卫（审计项 #6，严重）：
/// <c>PostSendAsync</c> 会对 429/5xx 内部重试，若重试后才被判为 replyToken 问题，
/// 第一次请求可能已被 LINE 接受 ⇒ 客户端不得再回落 push，否则客户收到两条同样的消息，
/// 同时白耗月度配额。回落与否的判断权交回 <c>LineService</c>（它会看 Attempts / DuplicateRisk）。
/// </summary>
public class LineDuplicateDeliveryGuardTests
{
    private const string LineUserId = "U111111111111111111111111111111111";

    private static LineClient CreateClient(StubLineHttpHandler stub)
    {
        var options = new LineOptions
        {
            Enabled = true,
            ChannelId = "@opencss-demo",
            ChannelSecret = "channel_secret_value",
            ChannelAccessToken = "channel_access_token_value",
            TextChunkChars = 5000,
            MaxMessagesPerRequest = 5,
            RetryMaxAttempts = 1,
        };

        var provider = new Mock<ILineSettingsProvider>();
        provider.Setup(s => s.Current).Returns(options);

        return new LineClient(new HttpClient(stub), Options.Create(new HttpClientOptions()), provider.Object, new Mock<ILogger<LineClient>>().Object);
    }

    [Fact]
    public async Task ReplyFailsOnlyAfterRetry_DoesNotFallBackToPush()
    {
        var callCount = 0;
        var stub = new StubLineHttpHandler
        {
            RespondWith = _ =>
            {
                callCount++;
                // 第一次 503（可重试），第二次 409（令牌已用）—— 409 出现在重试之后，投递状态不确定
                return callCount == 1
                    ? (HttpStatusCode.ServiceUnavailable, "{\"message\":\"temporarily unavailable\"}")
                    : (HttpStatusCode.Conflict, "{\"message\":\"Invalid reply token\"}");
            }
        };

        var client = CreateClient(stub);
        var result = await client.SendTextAsync("hello", replyToken: "rt-once", toUserId: LineUserId);

        result.Success.Should().BeFalse("重试后才拿到 409，不能假装成功");
        result.Attempts.Should().Be(2);
        result.Error!.HttpStatus.Should().Be(409);
        result.DuplicateRisk.Should().BeTrue("必须把「可能已投递」这个事实传上去");
        stub.Requests.Should().NotContain(r => r.Url.EndsWith("/v2/bot/message/push"),
            "回落 push 会让客户收到两条同样的消息");
    }

    [Fact]
    public async Task ReplyRejectedOnFirstAttempt_StillFallsBackToPush()
    {
        var callCount = 0;
        var stub = new StubLineHttpHandler
        {
            RespondWith = r =>
            {
                callCount++;
                if (r.Url.EndsWith("/v2/bot/message/reply"))
                {
                    return (HttpStatusCode.Conflict, "{\"message\":\"Invalid reply token\"}");
                }
                return (HttpStatusCode.OK, "{\"sentMessages\":[{\"id\":\"m1\"}]}");
            }
        };

        var client = CreateClient(stub);
        var result = await client.SendTextAsync("hello", replyToken: "rt-expired", toUserId: LineUserId);

        result.Success.Should().BeTrue("首次就被确定性地拒绝（未投递），回落 push 是安全的");
        result.Via.Should().Be("push");
        result.DuplicateRisk.Should().BeFalse();
        stub.Requests.Should().Contain(r => r.Url.EndsWith("/v2/bot/message/push"));
    }
}
