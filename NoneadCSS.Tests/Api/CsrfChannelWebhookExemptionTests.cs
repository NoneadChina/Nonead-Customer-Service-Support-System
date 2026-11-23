using FluentAssertions;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Http;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Moq;
using NoneadCSS.Api.Middleware;

namespace NoneadCSS.Tests.Api;

/// <summary>
/// 四个新渠道（Messenger / Telegram / LINE / iMessage）的 CSRF 豁免边界。
///
/// 背景：这些渠道的 Webhook 回调与后台管理接口共用同一个 /api/&lt;channel&gt; 前缀。
/// 若按前缀整体豁免（历史实现就是 "/api/line" 这样的写法），settings PUT / reply /
/// takeover 等敏感写操作会一起失去 CSRF 防护。本测试锁定「只有 Webhook 的精确 POST
/// 路径被豁免」这一契约，防止以后又改回前缀豁免。
/// </summary>
public class CsrfChannelWebhookExemptionTests
{
    private static IWebHostEnvironment ProductionEnvironment()
    {
        var env = new Mock<IWebHostEnvironment>();
        env.SetupGet(e => e.EnvironmentName).Returns(Environments.Production);
        env.SetupGet(e => e.ApplicationName).Returns("NoneadCSS.Api");
        return env.Object;
    }

    /// <summary>返回 true 表示请求被放行（未触发 CSRF 403）。</summary>
    private static async Task<bool> IsAllowedAsync(string method, string path)
    {
        var reachedNext = false;

        var middleware = new CsrfProtectionMiddleware(
            _ =>
            {
                reachedNext = true;
                return Task.CompletedTask;
            },
            new ConfigurationBuilder().Build(),
            ProductionEnvironment());

        var context = new DefaultHttpContext
        {
            RequestServices = new ServiceCollection().BuildServiceProvider()
        };
        context.Request.Method = method;
        context.Request.Path = path;
        context.Response.Body = new MemoryStream();

        await middleware.InvokeAsync(context);

        return reachedNext;
    }

    [Theory]
    [InlineData("POST", "/api/messenger")]                    // Meta 回调（控制器根路径）
    [InlineData("POST", "/api/messenger/")]                   // 容忍尾部斜杠
    [InlineData("POST", "/api/telegram/webhook")]
    [InlineData("POST", "/api/line/webhook")]
    [InlineData("POST", "/api/imessage/bluebubbles/webhook")]
    [InlineData("POST", "/api/imessage/amb/message")]
    [InlineData("POST", "/api/auth/login")]                    // 既有豁免不受影响
    public async Task Webhook_post_paths_stay_exempt(string method, string path)
    {
        (await IsAllowedAsync(method, path)).Should().BeTrue(
            because: $"{method} {path} 是渠道 Webhook 回调，必须能在无 CSRF Token 的情况下通过");
    }

    [Theory]
    [InlineData("PUT", "/api/messenger/settings")]
    [InlineData("POST", "/api/messenger/reply")]
    [InlineData("POST", "/api/messenger/subscribe")]
    [InlineData("PUT", "/api/telegram/settings")]
    [InlineData("POST", "/api/telegram/webhook/setup")]
    [InlineData("POST", "/api/telegram/reply")]
    [InlineData("PUT", "/api/line/settings")]
    [InlineData("POST", "/api/line/reply")]
    [InlineData("POST", "/api/line/takeover")]
    [InlineData("DELETE", "/api/line/takeover/12")]
    [InlineData("PUT", "/api/imessage/settings")]
    [InlineData("POST", "/api/imessage/reply")]
    [InlineData("POST", "/api/imessage/webhook-register")]
    public async Task Channel_admin_endpoints_require_csrf_token(string method, string path)
    {
        (await IsAllowedAsync(method, path)).Should().BeFalse(
            because: $"{method} {path} 是后台敏感写操作，不能被 Webhook 的豁免顺带放行");
    }
}
