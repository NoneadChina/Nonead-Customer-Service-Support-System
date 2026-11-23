using FluentAssertions;
using Microsoft.AspNetCore.Http;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using Moq;
using NoneadCSS.Application.Services;
using NoneadCSS.Infrastructure.Services;
using Xunit;

namespace NoneadCSS.Tests.Services;

/// <summary>
/// T000109（关闭微信会话-评价链接无法打开）集成测试。
///
/// 缺陷根因：生产实际加载的 Production\backend\appsettings.Production.json 中
/// App:FrontendUrl/BaseUrl 被手动改为内网域名 http://css.example.com:5173，
/// 微信客户收到的满意度评价链接前缀为 corp 域名 → 外部无法解析 → "链接无法打开"。
/// 修复：统一为公网域名 http://css.example.com:5173（要求：所有外发链接前缀必须为此）。
///
/// 本测试锁定四条回归防线：
/// 1) 源生产配置 App:FrontendUrl/BaseUrl 必须为 http://css.example.com:5173（不含 corp）；
/// 2) 部署副本配置与源保持一致（防止再次手改部署文件导致漂移）；
/// 3) 运行时解析器（真实 FrontendUrlResolver + 组合配置）返回要求的公网前缀；
/// 4) 真实 token 生成器产出评价链接：前缀正确 + token 可验证（客户点击后评分页可用）。
/// </summary>
public class FrontendUrlProductionIntegrationTests
{
    private static string ApiProjectDir => Path.GetFullPath(Path.Combine(AppContext.BaseDirectory, "..", "..", "..", "..", "NoneadCSS.Api"));

    private static string RepoRootDir => Path.GetFullPath(Path.Combine(ApiProjectDir, ".."));

    private static string DeployedProductionConfig => Path.Combine(RepoRootDir, "Production", "backend", "appsettings.Production.json");

    private const string RequiredFrontendUrl = "http://css.example.com:5173";

    /// <summary>组合运行时代码的实际加载顺序：appsettings.json + appsettings.Production.json（仅文件，避免测试机环境变量干扰）。</summary>
    private static IConfiguration BuildComposedProductionConfig()
    {
        return new ConfigurationBuilder()
            .SetBasePath(ApiProjectDir)
            .AddJsonFile("appsettings.json", optional: false)
            .AddJsonFile("appsettings.Production.json", optional: false)
            .Build();
    }

    // ==================== 1) 源生产配置回归防线 ====================

    [Fact]
    public void ProductionConfig_AppFrontendUrl_IsRequiredPublicDomain()
    {
        var cfg = new ConfigurationBuilder()
            .SetBasePath(ApiProjectDir)
            .AddJsonFile("appsettings.Production.json", optional: false)
            .Build();

        cfg["App:FrontendUrl"].Should().Be(RequiredFrontendUrl, "T000109 要求所有外发链接前缀为 http://css.example.com:5173");
        cfg["App:BaseUrl"].Should().Be(RequiredFrontendUrl);
        cfg["App:FrontendUrl"]!.Should().NotContain("corp", "不能使用内网 corp 域名，外部微信客户无法解析");
        cfg["App:BaseUrl"]!.Should().NotContain("corp");
    }

    // ==================== 2) 部署副本一致性回归防线 ====================

    [Fact]
    public void DeployedProductionConfig_AppFrontendUrl_MatchesSource()
    {
        if (!File.Exists(DeployedProductionConfig))
        {
            return; // 本机无生产部署目录时跳过（环境相关护栏）
        }

        var deployed = new ConfigurationBuilder()
            .SetBasePath(Path.GetDirectoryName(DeployedProductionConfig)!)
            .AddJsonFile(Path.GetFileName(DeployedProductionConfig), optional: false)
            .Build();

        deployed["App:FrontendUrl"].Should().Be(RequiredFrontendUrl,
            "生产部署副本被手动改为 css.example.com 导致 T000109；部署副本必须与源配置保持一致");
        deployed["App:BaseUrl"].Should().Be(RequiredFrontendUrl);
        deployed["App:FrontendUrl"]!.Should().NotContain("corp");
    }

    // ==================== 3) 运行时解析器（真实实现 + 组合配置） ====================

    [Fact]
    public void RuntimeFrontendUrlResolver_ReturnsRequiredPrefix()
    {
        var cfg = BuildComposedProductionConfig();

        // 真实 FrontendUrlResolver（与生产同一实现），HttpContextAccessor 不参与（配置非空时不走请求推导）
        var resolver = new FrontendUrlResolver(new HttpContextAccessor(), cfg);

        resolver.GetFrontendUrl().Should().Be(RequiredFrontendUrl);
        resolver.GetBaseUrl().Should().Be(RequiredFrontendUrl);

        // Brand:FrontendUrl 是 conversation_url / event_url 等模板链接的前缀来源（默认配置，生产未覆盖）
        cfg["Brand:FrontendUrl"].Should().Be(RequiredFrontendUrl);
        cfg["Brand:LoginUrl"].Should().Be($"{RequiredFrontendUrl}/login");
    }

    // ==================== 4) 评价链接组合（真实 token 生成器，微信渠道 ChannelId 路径） ====================

    [Fact]
    public void SatisfactionUrl_WeChatChannel_ComposedWithRequiredPrefix_AndTokenValidates()
    {
        var cfg = BuildComposedProductionConfig();

        var envMock = new Mock<IHostEnvironment>();
        envMock.SetupGet(e => e.EnvironmentName).Returns(Environments.Development); // 开发环境自动生成随机密钥（≥32B）
        var tokenGenerator = new SatisfactionTokenGenerator(
            cfg,
            envMock.Object,
            new Mock<ILogger<SatisfactionTokenGenerator>>().Object);

        var conversationId = 123;
        var weChatOpenId = "oXyZ_abc123";
        var token = tokenGenerator.GenerateTokenByChannelId(conversationId, weChatOpenId);

        // 与 SendImMessageActionHandler / ChatService / ComplexActionHandlers 完全相同的链接拼法
        var frontendUrl = new FrontendUrlResolver(new HttpContextAccessor(), cfg).GetFrontendUrl();
        var satisfactionUrl = $"{frontendUrl}/satisfaction/{token}";

        satisfactionUrl.Should().StartWith($"{RequiredFrontendUrl}/satisfaction/",
            "微信客户收到的评价链接前缀必须是公网域名（T000109）");
        satisfactionUrl.Should().MatchRegex(@"^http://css\.example\.com:5173/satisfaction/[^/\s]+$");

        // 客户点击后评分页会用同一密钥验证 token → 验证通过才可评分
        tokenGenerator.ValidateTokenByChannelId(token, conversationId, weChatOpenId).Should().BeTrue(
            "链接中的 token 必须能被验证，否则评分页无法打开/提交（'链接无法打开'的另一面）");
    }

    [Fact]
    public void ConversationUrl_ComposedWithRequiredPrefix()
    {
        var cfg = BuildComposedProductionConfig();
        var frontendUrl = new FrontendUrlResolver(new HttpContextAccessor(), cfg).GetFrontendUrl();

        // conversation_url 模板变量（ConversationVariableProvider / SendImMessageActionHandler 拼法）
        var conversationUrl = $"{frontendUrl}/conversations/456";

        conversationUrl.Should().StartWith($"{RequiredFrontendUrl}/conversations/456");
        conversationUrl.Should().NotContain("corp");
    }
}
