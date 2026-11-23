using System.Net;
using System.Text;
using System.Text.Json;
using FluentAssertions;
using Microsoft.Extensions.Caching.Memory;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using Moq;
using NoneadCSS.Application.Configuration;
using NoneadCSS.Application.Services;
using NoneadCSS.Domain.Services;
using NoneadCSS.Infrastructure.Services.WeChat;
using Xunit.Abstractions;

namespace NoneadCSS.Tests.Services;

/// <summary>
/// 验证微信出站 API 直连微信公众平台：所有请求必须打到 https://api.weixin.qq.com/cgi-bin/...，
/// 并且永远不携带任何中转网关鉴权头（历史上曾通过 X-Proxy-Key 走 www.nonead.com 中转，现已彻底移除）。
/// 通过自定义 HttpMessageHandler 拦截请求，断言 URL 与 header。
/// 每个测试用例通过 ITestOutputHelper 输出完整请求详情，方便排查直连配置问题。
/// </summary>
public class WeChatDirectApiTests
{
    /// <summary>微信公众平台官方 API 基址（出站唯一允许的目标）</summary>
    private const string ExpectedApiBase = "https://api.weixin.qq.com/";

    /// <summary>已废弃的中转网关头名，任何请求都不应出现</summary>
    private const string LegacyProxyHeader = "X-Proxy-Key";

    private readonly ITestOutputHelper _output;

    public WeChatDirectApiTests(ITestOutputHelper output)
    {
        _output = output;
    }

    /// <summary>
    /// 将拦截到的请求详情格式化为可读字符串并输出到测试日志。
    /// 包含：方法、完整 URL、所有 header、请求体。
    /// </summary>
    private void LogRequestDetails(string scenario, HttpRequestMessage? request, string? body)
    {
        _output.WriteLine("========================================");
        _output.WriteLine($"  场景: {scenario}");
        _output.WriteLine("========================================");

        if (request == null)
        {
            _output.WriteLine("  [警告] 未捕获到任何 HTTP 请求");
            _output.WriteLine(string.Empty);
            return;
        }

        _output.WriteLine($"  方法:     {request.Method}");
        _output.WriteLine($"  完整 URL: {request.RequestUri}");

        // URL 分解：scheme / host / path / query
        var uri = request.RequestUri!;
        _output.WriteLine($"    ├─ Scheme: {uri.Scheme}");
        _output.WriteLine($"    ├─ Host:   {uri.Host}");
        _output.WriteLine($"    ├─ Path:   {uri.AbsolutePath}");
        _output.WriteLine($"    └─ Query:  {uri.Query}");

        // Headers（请求级）
        _output.WriteLine("  Headers:");
        var allHeaders = request.Headers
            .Select(h => (Key: h.Key, Values: h.Value.ToList()))
            .ToList();

        if (allHeaders.Count == 0)
        {
            _output.WriteLine("    (无)");
        }
        else
        {
            foreach (var (key, values) in allHeaders)
            {
                _output.WriteLine($"    {key}: {string.Join(", ", values)}");
            }
        }

        // 请求体（截断到 500 字符防止日志过长）
        if (!string.IsNullOrEmpty(body))
        {
            var truncated = body.Length > 500 ? body[..500] + "...(已截断)" : body;
            _output.WriteLine($"  Body: {truncated}");
        }
        else
        {
            _output.WriteLine("  Body: (空)");
        }

        _output.WriteLine("========================================");
        _output.WriteLine(string.Empty);
    }

    /// <summary>
    /// 拦截 HTTP 请求的 spy handler，记录请求信息并返回模拟响应。
    /// 支持两种响应模式：JSON 文本（默认）和二进制流（用于素材下载测试）。
    /// </summary>
    private class SpyHttpMessageHandler : HttpMessageHandler
    {
        public HttpRequestMessage? LastRequest { get; private set; }
        public string? LastRequestBody { get; private set; }

        private readonly string _responseJson;
        private readonly byte[]? _binaryData;
        private readonly string _contentType;

        /// <summary>JSON 响应模式（默认 application/json）</summary>
        public SpyHttpMessageHandler(string responseJson)
        {
            _responseJson = responseJson;
            _contentType = "application/json";
        }

        /// <summary>二进制响应模式（用于素材下载测试，如 image/jpeg）</summary>
        public SpyHttpMessageHandler(byte[] binaryData, string contentType)
        {
            _binaryData = binaryData;
            _contentType = contentType;
            _responseJson = string.Empty;
        }

        protected override async Task<HttpResponseMessage> SendAsync(
            HttpRequestMessage request, CancellationToken cancellationToken)
        {
            LastRequest = request;
            if (request.Content != null)
            {
                LastRequestBody = await request.Content.ReadAsStringAsync(cancellationToken);
            }

            HttpContent content;
            if (_binaryData != null)
            {
                content = new ByteArrayContent(_binaryData);
                content.Headers.ContentType = new System.Net.Http.Headers.MediaTypeHeaderValue(_contentType);
            }
            else
            {
                content = new StringContent(_responseJson, Encoding.UTF8, _contentType);
            }

            var response = new HttpResponseMessage(HttpStatusCode.OK) { Content = content };
            return response;
        }
    }

    /// <summary>
    /// 创建 WeChatClient 的辅助方法，减少重复 Arrange 代码。
    /// </summary>
    private (WeChatClient client, SpyHttpMessageHandler handler) CreateWeChatClient(string responseJson)
    {
        var handler = new SpyHttpMessageHandler(responseJson);
        var httpClient = new HttpClient(handler);

        var mockTokenService = new Mock<IWeChatAccessTokenService>();
        mockTokenService.Setup(t => t.GetAccessTokenAsync()).ReturnsAsync("test_access_token");

        var mockOptions = Options.Create(new HttpClientOptions { StandardTimeoutSeconds = 30 });
        var mockLogger = new Mock<ILogger<WeChatClient>>();

        var client = new WeChatClient(httpClient, mockOptions, mockTokenService.Object, mockLogger.Object);
        return (client, handler);
    }

    /// <summary>
    /// 统一的直连断言：URL 落在官方基址上、host 为 api.weixin.qq.com、且不含任何中转网关头。
    /// </summary>
    private static void AssertDirectApiRequest(HttpRequestMessage request, string expectedPathFragment)
    {
        var requestUrl = request.RequestUri!.ToString();
        requestUrl.Should().StartWith(ExpectedApiBase);
        request.RequestUri!.Host.Should().Be("api.weixin.qq.com");
        request.RequestUri!.Scheme.Should().Be("https");
        requestUrl.Should().Contain(expectedPathFragment);
        // 中转网关彻底移除：不得出现任何中转域名/路径与鉴权头
        requestUrl.Should().NotContain("nonead.com");
        request.Headers.Contains(LegacyProxyHeader).Should().BeFalse();
    }

    #region WeChatClient 测试

    [Fact]
    public async Task WeChatClient_SendTextMessage_ShouldPostDirectlyToWeChatOfficialApi()
    {
        // Arrange
        var handler = new SpyHttpMessageHandler("""{"errcode":0}""");
        var httpClient = new HttpClient(handler);

        var mockTokenService = new Mock<IWeChatAccessTokenService>();
        mockTokenService.Setup(t => t.GetAccessTokenAsync()).ReturnsAsync("test_access_token");

        var mockOptions = Options.Create(new HttpClientOptions { StandardTimeoutSeconds = 30 });
        var mockLogger = new Mock<ILogger<WeChatClient>>();

        var client = new WeChatClient(httpClient, mockOptions, mockTokenService.Object, mockLogger.Object);

        // Act
        var result = await client.SendTextMessageAsync("oTestOpenId123", "你好");

        // 输出拦截到的请求详情
        LogRequestDetails("客服文本消息（直连）", handler.LastRequest, handler.LastRequestBody);

        // Assert — 请求成功
        result.Should().BeTrue();

        // Assert — 直连官方 API
        AssertDirectApiRequest(handler.LastRequest!, "cgi-bin/message/custom/send");
        handler.LastRequest!.RequestUri!.Query.Should().Contain("access_token=test_access_token");

        // Assert — JSON 请求体（直连模式下带 charset 无副作用）
        handler.LastRequest!.Content!.Headers.ContentType!.MediaType.Should().Be("application/json");
        handler.LastRequestBody.Should().Contain("\"touser\":\"oTestOpenId123\"");
    }

    [Fact]
    public async Task WeChatClient_SendTemplateMessage_ShouldPostDirectlyToWeChatOfficialApi()
    {
        // Arrange
        var (client, handler) = CreateWeChatClient("""{"errcode":0}""");

        var templateData = new Dictionary<string, object>
        {
            ["thing1"] = new { value = "测试模板消息" }
        };

        // Act
        var result = await client.SendTemplateMessageAsync("oTestOpenId123", "TEMPLATE_ID_001", templateData, "https://example.com");

        // 输出拦截到的请求详情
        LogRequestDetails("模板消息（直连）", handler.LastRequest, handler.LastRequestBody);

        // Assert
        result.Should().BeTrue();
        AssertDirectApiRequest(handler.LastRequest!, "cgi-bin/message/template/send");
    }

    [Fact]
    public async Task WeChatClient_CreateMenu_ShouldPostDirectlyToWeChatOfficialApi()
    {
        // Arrange
        var (client, handler) = CreateWeChatClient("""{"errcode":0}""");

        var menu = new { button = new[] { new { type = "click", name = "测试菜单", key = "MENU_001" } } };

        // Act
        var result = await client.CreateMenuAsync(menu);

        // 输出拦截到的请求详情
        LogRequestDetails("创建自定义菜单（直连）", handler.LastRequest, handler.LastRequestBody);

        // Assert
        result.Should().BeTrue();
        AssertDirectApiRequest(handler.LastRequest!, "cgi-bin/menu/create");
    }

    #endregion

    #region WeChatClient 其余出站端点

    [Fact]
    public async Task WeChatClient_SendImageMessage_ShouldPostDirectlyToWeChatOfficialApi()
    {
        // Arrange — 客服图片消息（与文本/语音/视频共用 CustomSendUrl，此处以图片为代表）
        var (client, handler) = CreateWeChatClient("""{"errcode":0}""");

        // Act
        var result = await client.SendImageMessageAsync("oTestOpenId123", "MEDIA_ID_001");

        // 输出拦截到的请求详情
        LogRequestDetails("客服图片消息（直连）", handler.LastRequest, handler.LastRequestBody);

        // Assert
        result.Should().BeTrue();
        AssertDirectApiRequest(handler.LastRequest!, "cgi-bin/message/custom/send");
    }

    [Fact]
    public async Task WeChatClient_SendSubscribeMessage_ShouldPostDirectlyToWeChatOfficialApi()
    {
        // Arrange — 订阅通知（URL 不同于模板消息）
        var (client, handler) = CreateWeChatClient("""{"errcode":0}""");

        var subscribeData = new Dictionary<string, object>
        {
            ["thing1"] = new { value = "测试订阅通知" }
        };

        // Act
        var result = await client.SendSubscribeMessageAsync("oTestOpenId123", "SUBSCRIBE_TMPL_001", subscribeData, "pages/index");

        // 输出拦截到的请求详情
        LogRequestDetails("订阅通知（直连）", handler.LastRequest, handler.LastRequestBody);

        // Assert
        result.Should().BeTrue();
        AssertDirectApiRequest(handler.LastRequest!, "cgi-bin/message/subscribe/bizsend");
    }

    [Fact]
    public async Task WeChatClient_UploadMedia_ShouldPostMultipartDirectlyToWeChatOfficialApi()
    {
        // Arrange — 上传临时素材（MultipartFormDataContent，不同的 content type）
        var (client, handler) = CreateWeChatClient("""{"media_id":"uploaded_media_id_123"}""");

        var fileBytes = Encoding.UTF8.GetBytes("fake image content");
        var filename = "test.png";
        var mimeType = "image/png";

        // Act
        var mediaId = await client.UploadMediaAsync(fileBytes, filename, mimeType, "image");

        // 输出拦截到的请求详情
        LogRequestDetails("上传临时素材 (MultipartFormData, 直连)", handler.LastRequest, handler.LastRequestBody);

        // Assert
        mediaId.Should().Be("uploaded_media_id_123");
        AssertDirectApiRequest(handler.LastRequest!, "cgi-bin/media/upload");
        // Multipart 请求体不是纯文本，但 handler 仍会尝试 ReadAsStringAsync 记录（可能包含 boundary 等信息）
    }

    [Fact]
    public async Task WeChatClient_DownloadMedia_ShouldGetDirectlyFromWeChatOfficialApi()
    {
        // Arrange — 下载临时素材（GET 请求 + 二进制响应，与 POST JSON 模式完全不同）
        var fakeImageBytes = new byte[] { 0x89, 0x50, 0x4E, 0x47 }; // PNG 文件头
        var handler = new SpyHttpMessageHandler(fakeImageBytes, "image/jpeg");
        var httpClient = new HttpClient(handler);

        var mockTokenService = new Mock<IWeChatAccessTokenService>();
        mockTokenService.Setup(t => t.GetAccessTokenAsync()).ReturnsAsync("test_access_token");
        var mockOptions = Options.Create(new HttpClientOptions { StandardTimeoutSeconds = 30 });
        var mockLogger = new Mock<ILogger<WeChatClient>>();

        var client = new WeChatClient(httpClient, mockOptions, mockTokenService.Object, mockLogger.Object);

        // Act
        var result = await client.DownloadMediaAsync("MEDIA_ID_001");

        // 输出拦截到的请求详情（GET 请求无 body）
        LogRequestDetails("下载临时素材 (GET 二进制, 直连)", handler.LastRequest, handler.LastRequestBody);

        // Assert
        result.Should().NotBeNull();
        result!.Value.data.Should().Equal(fakeImageBytes);
        handler.LastRequest!.Method.Should().Be(HttpMethod.Get);
        AssertDirectApiRequest(handler.LastRequest!, "cgi-bin/media/get");
    }

    [Fact]
    public async Task WeChatClient_GetUserInfo_ShouldGetDirectlyFromWeChatOfficialApi()
    {
        // Arrange — 获取用户信息（GET 请求 + JSON 响应）
        var (client, handler) = CreateWeChatClient(
            """{"subscribe":1,"openid":"oTestOpenId123","nickname":"测试用户","sex":1,"language":"zh_CN"}""");

        // Act
        var userInfo = await client.GetUserInfoAsync("oTestOpenId123");

        // 输出拦截到的请求详情
        LogRequestDetails("获取用户信息 (GET JSON, 直连)", handler.LastRequest, handler.LastRequestBody);

        // Assert
        userInfo.Should().NotBeNull();
        userInfo!.Value.GetProperty("nickname").GetString().Should().Be("测试用户");
        handler.LastRequest!.Method.Should().Be(HttpMethod.Get);
        AssertDirectApiRequest(handler.LastRequest!, "cgi-bin/user/info");
    }

    [Fact]
    public async Task WeChatClient_GetMenu_ShouldGetDirectlyFromWeChatOfficialApi()
    {
        // Arrange — 查询自定义菜单（GET 请求 + JSON 响应）
        var (client, handler) = CreateWeChatClient(
            """{"is_menu_open":1,"selfmenu_info":{"button":[{"name":"测试菜单"}]}}""");

        // Act
        var menu = await client.GetMenuAsync();

        // 输出拦截到的请求详情
        LogRequestDetails("查询自定义菜单 (GET JSON, 直连)", handler.LastRequest, handler.LastRequestBody);

        // Assert
        menu.Should().NotBeNull();
        handler.LastRequest!.Method.Should().Be(HttpMethod.Get);
        AssertDirectApiRequest(handler.LastRequest!, "cgi-bin/get_current_selfmenu_info");
    }

    [Fact]
    public async Task WeChatClient_DeleteMenu_ShouldPostDirectlyToWeChatOfficialApi()
    {
        // Arrange — 删除自定义菜单（POST 到不同 URL）
        var (client, handler) = CreateWeChatClient("""{"errcode":0}""");

        // Act
        var result = await client.DeleteMenuAsync();

        // 输出拦截到的请求详情
        LogRequestDetails("删除自定义菜单（直连）", handler.LastRequest, handler.LastRequestBody);

        // Assert
        result.Should().BeTrue();
        AssertDirectApiRequest(handler.LastRequest!, "cgi-bin/menu/delete");
    }

    #endregion

    #region WeChatAccessTokenService 测试

    [Fact]
    public async Task AccessTokenService_RefreshToken_ShouldPostDirectlyToStableTokenApi()
    {
        // Arrange
        var handler = new SpyHttpMessageHandler(
            """{"access_token":"new_test_token_abc123","expires_in":7200}""");
        var httpClient = new HttpClient(handler);

        var weChatOptions = new WeChatOptions
        {
            AppId = "wx_test_appid",
            AppSecret = "encrypted_secret_placeholder",
            Token = "test_token",
            Enabled = true
        };

        var mockSettingsProvider = new Mock<IWeChatSettingsProvider>();
        mockSettingsProvider.Setup(p => p.Current).Returns(weChatOptions);

        var mockEncryption = new Mock<IEncryptionService>();
        // DecryptSecret 调用 TryDecrypt；返回 false 走降级路径（明文直传），不影响 HTTP 拦截验证
        string? plain = null;
        mockEncryption.Setup(e => e.TryDecrypt(It.IsAny<string>(), out plain)).Returns(false);

        var mockCache = new Mock<IMemoryCache>();
        object? cacheOut = null;
        mockCache.Setup(c => c.TryGetValue(It.IsAny<object>(), out cacheOut)).Returns(false);
        mockCache.Setup(c => c.CreateEntry(It.IsAny<object>()))
            .Returns(new Mock<ICacheEntry>().Object);

        var mockHttpOptions = Options.Create(new HttpClientOptions { TokenServiceTimeoutSeconds = 15 });
        var mockTokenRefreshOptions = Options.Create(new TokenRefreshOptions { RefreshBufferMinutes = 5 });
        var mockLogger = new Mock<ILogger<WeChatAccessTokenService>>();

        var tokenService = new WeChatAccessTokenService(
            httpClient,
            mockHttpOptions,
            mockTokenRefreshOptions,
            mockSettingsProvider.Object,
            mockEncryption.Object,
            mockCache.Object,
            mockLogger.Object);

        // Act
        var token = await tokenService.RefreshAccessTokenAsync();

        // 输出拦截到的请求详情
        LogRequestDetails("刷新 access_token (stable_token, 直连)", handler.LastRequest, handler.LastRequestBody);

        // Assert — 返回了模拟的 access_token
        token.Should().Be("new_test_token_abc123");

        // Assert — 稳定版 token 接口直连官方基址
        AssertDirectApiRequest(handler.LastRequest!, "cgi-bin/stable_token");
        handler.LastRequest!.RequestUri!.AbsolutePath.Should().Be("/cgi-bin/stable_token");

        // Assert — 请求体带 client_credential 凭据参数
        handler.LastRequestBody.Should().Contain("client_credential");
        handler.LastRequestBody.Should().Contain("\"appid\":\"wx_test_appid\"");
    }

    #endregion

    #region 中转网关残留检查

    [Fact]
    public async Task WeChatClient_ShouldNeverSendLegacyProxyKeyHeader()
    {
        // Arrange — 即便调用方自行往 HttpClient 上挂历史遗留头名，客户端自身也不得再引入任何网关配置
        var handler = new SpyHttpMessageHandler("""{"errcode":0}""");
        var httpClient = new HttpClient(handler);

        var mockTokenService = new Mock<IWeChatAccessTokenService>();
        mockTokenService.Setup(t => t.GetAccessTokenAsync()).ReturnsAsync("test_access_token");

        var mockOptions = Options.Create(new HttpClientOptions { StandardTimeoutSeconds = 30 });
        var mockLogger = new Mock<ILogger<WeChatClient>>();

        var client = new WeChatClient(httpClient, mockOptions, mockTokenService.Object, mockLogger.Object);

        // Act
        var result = await client.SendTextMessageAsync("oTestOpenId123", "测试");

        // 输出拦截到的请求详情
        LogRequestDetails("无网关配置的客服文本消息", handler.LastRequest, handler.LastRequestBody);

        // Assert — 请求成功且直连官方域名，且没有任何网关鉴权头
        result.Should().BeTrue();
        var requestUrl = handler.LastRequest!.RequestUri!.ToString();
        requestUrl.Should().StartWith(ExpectedApiBase);
        handler.LastRequest!.Headers.Should().NotContainKey(LegacyProxyHeader);
        handler.LastRequest!.Headers.Select(h => h.Key).Should().NotContain(LegacyProxyHeader);
    }

    #endregion
}
