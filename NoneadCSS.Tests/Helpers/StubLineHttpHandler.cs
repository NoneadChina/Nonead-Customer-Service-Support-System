using System.Net;
using System.Text;

namespace NoneadCSS.Tests.Helpers;

/// <summary>
/// 无网络桩 HttpMessageHandler：记录请求 URL/正文/Authorization，按委托返回响应。
/// 原为 LineServiceTests.cs 与 LineOutboundLinkAndChunkingTests.cs 各自私有的逐字重复实现，
/// 2026-11-21 收口为这一份共享实现（两处均改引用本类型）。
/// </summary>
internal sealed class StubLineHttpHandler : HttpMessageHandler
{
    public sealed record RecordedRequest(string Url, string? Body, string? Authorization);

    public List<RecordedRequest> Requests { get; } = [];

    public Func<RecordedRequest, (HttpStatusCode Status, string Body)> RespondWith { get; set; } =
        _ => (HttpStatusCode.OK, "{}");

    protected override async Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
    {
        var body = request.Content is null ? null : await request.Content.ReadAsStringAsync(cancellationToken);
        var recorded = new RecordedRequest(
            request.RequestUri!.ToString(),
            body,
            request.Headers.Authorization?.ToString());
        Requests.Add(recorded);

        var (status, responseBody) = RespondWith(recorded);
        return new HttpResponseMessage(status)
        {
            Content = new StringContent(responseBody, Encoding.UTF8, "application/json"),
        };
    }
}
