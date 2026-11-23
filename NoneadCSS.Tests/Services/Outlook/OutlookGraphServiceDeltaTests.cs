using System;
using System.Collections.Generic;
using System.Net;
using System.Net.Http;
using System.Text;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;
using FluentAssertions;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;
using Moq;
using Moq.Protected;
using NoneadCSS.Application.Configuration;
using NoneadCSS.Application.Services;
using NoneadCSS.Infrastructure.Services.Outlook;
using Xunit;

namespace NoneadCSS.Tests.Services.Outlook;

/// <summary>
/// OutlookGraphService ListEventsDeltaAsync 单元测试。
/// </summary>
public class OutlookGraphServiceDeltaTests
{
    private const string TestAccessToken = "ey.test.token";
    private const string ExpectedDeltaToken = "https://graph.microsoft.com/v1.0/me/calendarView/delta?$deltatoken=abc123";

    #region 1. 首次 Delta 查询 → 返回 deltaLink

    [Fact]
    public async Task ListEventsDeltaAsync_NoDeltaToken_CallsDeltaEndpoint()
    {
        var json = $$"""
        {
          "value": [
            {
              "id": "event-1",
              "subject": "Delta Meeting",
              "start": { "dateTime": "2026-08-10T09:00:00.0000000", "timeZone": "UTC" },
              "end":   { "dateTime": "2026-08-10T10:00:00.0000000", "timeZone": "UTC" }
            }
          ],
          "@odata.deltaLink": "{{ExpectedDeltaToken}}"
        }
        """;

        var sut = CreateSut(out var mockHandler, HttpStatusCode.OK, json,
            req => req.RequestUri!.AbsoluteUri.Contains("calendarView/delta"));

        var (events, newDeltaToken, isExpired) = await sut.ListEventsDeltaAsync(
            TestAccessToken,
            new DateTime(2026, 8, 1),
            new DateTime(2026, 8, 31),
            deltaToken: null);

        isExpired.Should().BeFalse();
        newDeltaToken.Should().Be(ExpectedDeltaToken);
        events.Should().HaveCount(1);
        events[0].Id.Should().Be("event-1");
        events[0].Subject.Should().Be("Delta Meeting");
        events[0].IsDeleted.Should().BeFalse();

        mockHandler.Protected().Verify(
            "SendAsync",
            Times.Exactly(1),
            ItExpr.IsAny<HttpRequestMessage>(),
            ItExpr.IsAny<CancellationToken>());
    }

    #endregion

    #region 2. 410 Gone → IsDeltaExpired=true

    [Fact]
    public async Task ListEventsDeltaAsync_410Gone_ReturnsExpiredTrue()
    {
        var sut = CreateSut(out var mockHandler, HttpStatusCode.Gone, "{}");

        var (events, newDeltaToken, isExpired) = await sut.ListEventsDeltaAsync(
            TestAccessToken,
            new DateTime(2026, 8, 1),
            new DateTime(2026, 8, 31),
            deltaToken: "https://graph.microsoft.com/v1.0/me/calendarView/delta?$deltatoken=expired-tok");

        isExpired.Should().BeTrue();
        newDeltaToken.Should().BeNull();
        events.Should().BeEmpty();

        mockHandler.Protected().Verify(
            "SendAsync",
            Times.Exactly(1),
            ItExpr.IsAny<HttpRequestMessage>(),
            ItExpr.IsAny<CancellationToken>());
    }

    #endregion

    #region 3. @removed.reason=deleted → IsDeleted=true

    [Fact]
    public async Task ListEventsDeltaAsync_RemovedFlag_SetsIsDeletedTrue()
    {
        var json = """
        {
          "value": [
            {
              "id": "event-deleted",
              "subject": null,
              "@removed": { "reason": "deleted" }
            }
          ],
          "@odata.deltaLink": "https://graph.microsoft.com/v1.0/me/calendarView/delta?$deltatoken=after"
        }
        """;

        var sut = CreateSut(out _, HttpStatusCode.OK, json);

        var (events, _, _) = await sut.ListEventsDeltaAsync(
            TestAccessToken,
            new DateTime(2026, 8, 1),
            new DateTime(2026, 8, 31),
            deltaToken: null);

        events.Should().HaveCount(1);
        events[0].Id.Should().Be("event-deleted");
        events[0].IsDeleted.Should().BeTrue();
    }

    #endregion

    #region SUT Factory

    private static OutlookGraphService CreateSut(
        out Mock<DelegatingHandler> mockHandler,
        HttpStatusCode status,
        string jsonResponse,
        Predicate<HttpRequestMessage>? requestFilter = null)
    {
        mockHandler = new Mock<DelegatingHandler>(MockBehavior.Strict);

        mockHandler.Protected()
            .Setup<Task<HttpResponseMessage>>(
                "SendAsync",
                ItExpr.Is<HttpRequestMessage>(req => requestFilter == null || requestFilter(req)),
                ItExpr.IsAny<CancellationToken>())
            .ReturnsAsync(() =>
            {
                var response = new HttpResponseMessage(status);
                if (!string.IsNullOrEmpty(jsonResponse))
                {
                    response.Content = new StringContent(jsonResponse, Encoding.UTF8, "application/json");
                }
                return response;
            });

        var client = new HttpClient(mockHandler.Object)
        {
            BaseAddress = new Uri("https://graph.microsoft.com/")
        };

        var graphOptions = Options.Create(new OutlookSyncOptions
        {
            GraphBaseUrl = "https://graph.microsoft.com/v1.0"
        });

        var httpOptions = Options.Create(new HttpClientOptions
        {
            StandardTimeoutSeconds = 10,
            OutlookGraphTimeoutSeconds = 10
        });

        var nullLogger = NullLogger<OutlookGraphService>.Instance;

        return new OutlookGraphService(client, graphOptions, httpOptions, nullLogger);
    }

    #endregion
}
