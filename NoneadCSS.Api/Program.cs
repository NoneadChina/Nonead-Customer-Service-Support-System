using System.Globalization;
using System.Security.Cryptography;
using System.Text;
using NoneadCSS.Api;
using NoneadCSS.Api.Authentication;
using NoneadCSS.Api.Authorization;
using NoneadCSS.Application;
using NoneadCSS.Application.Configuration;
using NoneadCSS.Domain;
using NoneadCSS.Domain.Entities;
using NoneadCSS.Domain.Enums;
using NoneadCSS.Infrastructure;
using NoneadCSS.Infrastructure.Persistence;
using NoneadCSS.Api.Database;
using Microsoft.AspNetCore.Authentication.JwtBearer;
using Microsoft.AspNetCore.Diagnostics.HealthChecks;
using Microsoft.AspNetCore.HttpOverrides;
using Microsoft.AspNetCore.Localization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.RateLimiting;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Diagnostics.HealthChecks;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.IdentityModel.Tokens;
using Scalar.AspNetCore;
using Serilog;
using System.Threading.RateLimiting;

// 初始化 Serilog（启动早期日志）
Log.Logger = new LoggerConfiguration()
    .ReadFrom.Configuration(new ConfigurationBuilder()
        .AddJsonFile("appsettings.json")
        .Build())
    .Enrich.FromLogContext()
    .CreateBootstrapLogger();

var builder = WebApplication.CreateBuilder(args);

// 使用 Serilog 作为日志提供程序
// Serilog 内置 Destructuring + 自定义 Enricher 实现敏感数据脱敏，
// 无需包装 ILoggerProvider（避免递归依赖问题）。
builder.Host.UseSerilog((context, services, configuration) => configuration
    .ReadFrom.Configuration(context.Configuration)
    .ReadFrom.Services(services)
    .Enrich.FromLogContext()
    .Enrich.With(new NoneadCSS.Infrastructure.Logging.SensitiveDataEnricher()));

// 支持的语言文化
var supportedCultures = new[]
{
    new CultureInfo("zh-Hans"),
    new CultureInfo("en")
};

// 配置转发头（用于反向代理部署，如 Nginx/Docker）
// 安全要求：不应清空 KnownProxies/KnownNetworks，否则任意 IP 都可伪造 X-Forwarded-* 头
// 默认仅信任 loopback（localhost），生产环境应通过配置 "ForwardedHeaders:KnownProxies" 显式指定反向代理 IP
builder.Services.Configure<ForwardedHeadersOptions>(options =>
{
    options.ForwardedHeaders = ForwardedHeaders.XForwardedFor | ForwardedHeaders.XForwardedProto | ForwardedHeaders.XForwardedHost;

    // 从配置读取受信任的代理 IP 列表（逗号分隔），未配置时保持默认（仅信任 loopback）
    var knownProxies = builder.Configuration["ForwardedHeaders:KnownProxies"];
    if (!string.IsNullOrWhiteSpace(knownProxies))
    {
        options.KnownProxies.Clear();
        foreach (var proxy in knownProxies.Split(',', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries))
        {
            if (System.Net.IPAddress.TryParse(proxy, out var ipAddress))
            {
                options.KnownProxies.Add(ipAddress);
            }
        }
    }
});

builder.Services.AddLocalization(options => options.ResourcesPath = "Resources");

builder.Services.Configure<RequestLocalizationOptions>(options =>
{
    // 默认使用简体中文
    options.DefaultRequestCulture = new RequestCulture("zh-Hans");
    options.SupportedCultures = supportedCultures;
    options.SupportedUICultures = supportedCultures;

    // 通过查询字符串、Cookie、Accept-Language 头确定语言
    options.RequestCultureProviders.Insert(0, new QueryStringRequestCultureProvider
    {
        QueryStringKey = "lang",
        UIQueryStringKey = "lang"
    });
});

// 健康检查：liveness 仅探测进程存活，readiness 检查数据库等依赖
builder.Services.AddHealthChecks()
    .AddDbContextCheck<NoneadCSSDbContext>("database", tags: new[] { "ready" });

// 内存缓存（用于密码策略等场景）
builder.Services.AddMemoryCache();

// 日历模块业务监控指标采集器（基于 MeterListener，无额外 NuGet 依赖）
// 启动时订阅 CalendarMetrics.Meter 下的所有 Counter，提供 Prometheus 文本输出
builder.Services.AddSingleton<NoneadCSS.Application.Metrics.CalendarMetricsExporter>();
builder.Services.AddHostedService(sp => sp.GetRequiredService<NoneadCSS.Application.Metrics.CalendarMetricsExporter>());
// #5：工作流中断检测（启动时把超过 10 分钟仍为 pending 的执行日志标为 interrupted）
builder.Services.AddHostedService<NoneadCSS.Api.Services.WorkflowInterruptionHostedService>();

// 分布式缓存（用于 Outlook OAuth nonce 防重放等场景；单实例下使用内存实现）
builder.Services.AddDistributedMemoryCache();

// 响应缓存（用于只读 API 端点）
var responseCachingCfg = builder.Configuration.GetSection("Performance:ResponseCaching").Get<NoneadCSS.Application.Configuration.ResponseCachingOptions>()
    ?? new NoneadCSS.Application.Configuration.ResponseCachingOptions();
var mgmtCache = TimeSpan.FromMinutes(responseCachingCfg.ManagementListCacheMinutes);
var sysOptCache = TimeSpan.FromMinutes(responseCachingCfg.SystemOptionsCacheMinutes);
var auditCache = TimeSpan.FromMinutes(responseCachingCfg.AuditLogCacheMinutes);
// S9: 按登录用户区分输出缓存键（框架无 SetVaryByAuthenticatedUser API，用 VaryByValue 等价实现）
static KeyValuePair<string, string> VaryByUser(HttpContext ctx)
{
    if (ctx.User?.Identity?.IsAuthenticated == true)
    {
        var id = ctx.User.FindFirst(System.Security.Claims.ClaimTypes.NameIdentifier)?.Value;
        return new KeyValuePair<string, string>("user", string.IsNullOrEmpty(id) ? "user" : id);
    }
    return new KeyValuePair<string, string>("user", "anon");
}

builder.Services.AddOutputCache(options =>
{
    // S9: 所有缓存策略按登录用户区分（SetVaryByValue(VaryByUser)），
    // 防止一个用户的响应被另一个已认证用户命中（缓存键隔离）
    options.AddPolicy("Roles", policy => policy.Expire(mgmtCache).VaryByValue(VaryByUser).SetVaryByQuery("page", "pageSize", "keyword"));
    options.AddPolicy("Tags", policy => policy.Expire(mgmtCache).VaryByValue(VaryByUser).SetVaryByQuery("page", "pageSize", "keyword"));
    options.AddPolicy("CustomFields", policy => policy.Expire(mgmtCache).VaryByValue(VaryByUser).SetVaryByQuery("page", "pageSize", "keyword"));
    options.AddPolicy("SystemOptions", policy => policy.Expire(sysOptCache).VaryByValue(VaryByUser));
    // 审计日志：超管端点，数据频繁增长，使用较短过期时间。
    // 缓存键包含所有过滤参数，避免不同过滤条件命中同一缓存。
    options.AddPolicy("AuditLogs", policy => policy
        .Expire(auditCache)
        .VaryByValue(VaryByUser)
        .SetVaryByQuery("page", "pageSize", "action", "entityType", "userId", "entityId", "ipAddress", "keyword", "startDate", "endDate", "maxRows"));
    // 团队、Webhook、模块、SLA 策略、快捷回复：管理端配置数据，相对稳定
    options.AddPolicy("Teams", policy => policy.Expire(mgmtCache).VaryByValue(VaryByUser).SetVaryByQuery("page", "pageSize", "mailboxId", "isActive", "keyword"));
    options.AddPolicy("Webhooks", policy => policy.Expire(mgmtCache).VaryByValue(VaryByUser).SetVaryByQuery("page", "pageSize", "mailboxId", "isActive", "keyword", "eventType", "webhookId", "isSuccess"));
    options.AddPolicy("Modules", policy => policy.Expire(mgmtCache).VaryByValue(VaryByUser).SetVaryByQuery("category", "isActive", "search", "page", "pageSize"));
    options.AddPolicy("Sla", policy => policy.Expire(mgmtCache).VaryByValue(VaryByUser).SetVaryByQuery("page", "pageSize", "mailboxId", "isActive", "keyword"));
    options.AddPolicy("CannedResponses", policy => policy.Expire(mgmtCache).VaryByValue(VaryByUser).SetVaryByQuery("mailboxId", "category", "keyword"));
});

builder.Services.AddControllers()
    .AddJsonOptions(options =>
    {
        options.JsonSerializerOptions.Encoder = System.Text.Encodings.Web.JavaScriptEncoder.UnsafeRelaxedJsonEscaping;
        options.JsonSerializerOptions.PropertyNamingPolicy = System.Text.Json.JsonNamingPolicy.CamelCase;
        options.JsonSerializerOptions.Converters.Add(new UtcDateTimeConverter());
        options.JsonSerializerOptions.Converters.Add(new UtcDateTimeNullableConverter());
    })
    .AddDataAnnotationsLocalization()
    .AddMvcOptions(options =>
    {
        options.Filters.Add<NoneadCSS.Api.Filters.AuditActionFilter>();
        options.Filters.Add<NoneadCSS.Api.Filters.ModuleAuthorizationFilter>();
        options.Filters.Add<NoneadCSS.Api.Filters.HtmlSanitizingResponseFilter>();
        options.Filters.Add<NoneadCSS.Api.Filters.ApiResponseWrapperFilter>();
    })
    .ConfigureApiBehaviorOptions(options =>
    {
        options.InvalidModelStateResponseFactory = context =>
        {
            var errors = context.ModelState
                .Where(x => x.Value?.Errors.Count > 0)
                .SelectMany(x => x.Value!.Errors.Select(e => e.ErrorMessage))
                .ToList();

            var message = errors.Count > 0 ? errors[0] : "参数验证失败";

            // T000105：模型绑定失败（如 [FromBody] 参数绑不上）时，ASP.NET Core 返回的
            // "The request field is required." 等英文内部消息对用户不友好，翻译为可操作的中文提示。
            if (message.Contains("request field is required", StringComparison.OrdinalIgnoreCase)
                || message.Contains("field is required", StringComparison.OrdinalIgnoreCase))
            {
                message = "请求体解析失败：提交的数据格式与接口要求不匹配，请检查填写内容（如请求头 JSON 需为字符串键值对）";
            }

            // 出口词典替换：状态码与上面那层「绑定失败」判定都已完成，这里只做展示文案的中文→英文。
            // Data 是匿名对象（属性只读），所以必须在构造前把 errors 列表本身换掉。
            var validationCulture = NoneadCSS.Api.Localization.ApiRequestCulture.Of(context.HttpContext);
            for (var i = 0; i < errors.Count; i++)
            {
                errors[i] = NoneadCSS.Api.Localization.ApiMessageLocalizer.Localize(errors[i], validationCulture) ?? errors[i];
            }

            message = NoneadCSS.Api.Localization.ApiMessageLocalizer.Localize(message, validationCulture) ?? message;

            var result = new NoneadCSS.Application.DTOs.Responses.ApiResponse
            {
                Success = false,
                Code = 400,
                Message = message,
                Data = new { errors, traceId = context.HttpContext.TraceIdentifier }
            };

            return new BadRequestObjectResult(result);
        };
    });

builder.Services.AddEndpointsApiExplorer();

// T000102：OpenAPI 文档服务在全部环境注册（生产环境 Scalar /docs 依赖它生成 /openapi/v1.json）。
// 原 `if (!IsProduction())` 限定导致生产环境 OpenAPI 端点 404。
builder.Services.AddOpenApi(options =>
{
    // 中文文档（默认，`/openapi/v1.json`）：接口级文案来自控制器的 XML 注释（简体中文），
    // info 段文案由 OpenApiDocumentationTexts 统一给出。
    options.AddDocumentTransformer((document, context, cancellationToken) =>
    {
        NoneadCSS.Api.OpenApi.OpenApiDocumentationTexts.ApplyChineseInfo(document);
        return Task.CompletedTask;
    });
});

// OpenAPI 文档国际化（用户要求 /docs 支持英文与简体中文）：再注册一份名为 en 的文档，
// 由 app.MapOpenApi() 自动暴露在 `/openapi/en.json`。它与中文文档同源（同一批控制器与 XML 注释），
// 差异只在文案：接口级 summary/description/参数说明按词典
// （OpenApi/openapi-en-dictionary.json，简体中文 → 英文）整体替换，info 段直接给英文。
// 页面侧的语言切换由 ScalarCustomizationMiddleware 改写 /docs HTML 里的文档 URL 完成
// （`/docs?lang=en`），不依赖 Scalar 尚未公开的 multi-document 下拉。
builder.Services.AddOpenApi(NoneadCSS.Api.OpenApi.OpenApiEnglishLocalizer.EnglishDocumentName, options =>
{
    options.AddDocumentTransformer((document, context, cancellationToken) =>
    {
        var translated = NoneadCSS.Api.OpenApi.OpenApiEnglishLocalizer.ApplyEnglish(document);
        if (translated == 0)
        {
            Log.Warning("OpenAPI 英文文档没有任何字段被翻译：词典缺失或未命中（期望路径 {Path}）",
                NoneadCSS.Api.OpenApi.OpenApiEnglishLocalizer.DictionaryRelativePath);
        }
        else
        {
            Log.Debug("OpenAPI 英文文档：{Count} 个字段已按词典翻译", translated);
        }
        NoneadCSS.Api.OpenApi.OpenApiDocumentationTexts.ApplyEnglishInfo(document);
        return Task.CompletedTask;
    });
});


// JWT 配置：优先从环境变量读取，生产环境强制使用环境变量（安全要求）。
// 校验与默认值解析逻辑已下沉到 JwtBootstrapValidator（可单元测试覆盖所有 Fail-Fast 分支）。
var jwtBootstrap = NoneadCSS.Application.Security.JwtBootstrapValidator.ValidateAndResolve(
    builder.Configuration,
    builder.Environment);
if (jwtBootstrap.IsAutoGeneratedDevelopmentKey)
{
    Log.Information("[DEV] 已自动生成开发环境 JWT 密钥（每次启动随机，请勿用于生产环境）");
    Log.Information("[DEV] 如需固定密钥，请设置 JWT__Key 环境变量");
}
var jwtKey = jwtBootstrap.SecretKey;
var jwtIssuer = jwtBootstrap.Issuer;
var jwtAudience = jwtBootstrap.Audience;
var jwtAccessTokenExpireMinutes = jwtBootstrap.AccessTokenExpireMinutes.ToString();

// 开发环境开箱即用：版本库里的 ConnectionStrings:SqliteConnection 有意留空（真实连接串属机器私有信息）。
// 未显式配置时给一个仓库内默认 SQLite 库；已配置但为相对路径时按 ContentRoot 解析并确保父目录存在——
// 否则 SQLite 只会报 Error 14 "unable to open database file"，而进程 CWD 会随启动方式变化（与 T000108 同类陷阱）。
// 生产环境不受此处影响：下方第 0 项 Fail-Fast 仍要求显式配置连接串。
if (!builder.Environment.IsProduction())
{
    var devUseSqliteRaw = builder.Configuration["Database:UseSqlite"];
    var devUseSqlite = !bool.TryParse(devUseSqliteRaw, out var parsed) || parsed;
    if (devUseSqlite)
    {
        var devConn = builder.Configuration["ConnectionStrings:SqliteConnection"];
        if (string.IsNullOrWhiteSpace(devConn))
            devConn = "Data Source=data/noneadcss-dev.db";

        try
        {
            var csb = new Microsoft.Data.Sqlite.SqliteConnectionStringBuilder(devConn);
            var dataSource = csb.DataSource;
            if (!string.IsNullOrWhiteSpace(dataSource) && dataSource != ":memory:" && !Path.IsPathRooted(dataSource))
            {
                csb.DataSource = Path.GetFullPath(Path.Combine(builder.Environment.ContentRootPath, dataSource));
                builder.Configuration["ConnectionStrings:SqliteConnection"] = csb.ToString();
                Log.Information("[DEV] ConnectionStrings:SqliteConnection 的相对路径 {Rel} 已按 ContentRoot 解析为 {Abs}",
                    dataSource, csb.DataSource);
            }

            var dbDir = Path.GetDirectoryName(csb.DataSource);
            if (!string.IsNullOrEmpty(dbDir) && !Directory.Exists(dbDir))
            {
                Directory.CreateDirectory(dbDir);
                Log.Information("[DEV] 已创建 SQLite 数据目录 {Dir}", dbDir);
            }

            Log.Information("[DEV] 当前使用开发库 {Db}（可用环境变量 CONNECTIONSTRINGS__SqliteConnection 覆盖）", csb.DataSource);
        }
        catch (ArgumentException ex)
        {
            Log.Warning(ex, "[DEV] ConnectionStrings:SqliteConnection 解析失败，沿用原值 {Conn}", devConn);
        }
    }
}

// H-04 生产环境密钥非空校验（对齐 JWT 的 L217-226 模式）。
// 任一密钥为空 → Fail Fast 抛 InvalidOperationException，避免默认空值上线。
if (builder.Environment.IsProduction())
{
    // 0. 数据库连接字符串：Fail-Fast 校验，避免启动后首次访问数据库才暴露空配置
    //    默认按 Database:UseSqlite 分支读取对应连接字符串
    var useSqliteRaw =
        Environment.GetEnvironmentVariable("DATABASE__UseSqlite")
        ?? Environment.GetEnvironmentVariable("DATABASE_USESQLITE")
        ?? builder.Configuration["Database:UseSqlite"];
    // 未配置 UseSqlite 时默认按 SQLite 处理（与 appsettings.json 默认值一致）
    var useSqlite = !bool.TryParse(useSqliteRaw, out var useSqliteValue) || useSqliteValue;
    var connectionString = useSqlite
        ? (Environment.GetEnvironmentVariable("CONNECTIONSTRINGS__SqliteConnection")
           ?? Environment.GetEnvironmentVariable("CONNECTIONSTRINGS_SQLITECONNECTION")
           ?? builder.Configuration["ConnectionStrings:SqliteConnection"])
        : (Environment.GetEnvironmentVariable("CONNECTIONSTRINGS__DefaultConnection")
           ?? Environment.GetEnvironmentVariable("CONNECTIONSTRINGS_DEFAULTCONNECTION")
           ?? builder.Configuration["ConnectionStrings:DefaultConnection"]);
    if (string.IsNullOrWhiteSpace(connectionString))
    {
        throw new InvalidOperationException(
            "生产环境必须配置数据库连接字符串。" +
            "默认读取 ConnectionStrings:SqliteConnection（Database:UseSqlite=true 时）或 ConnectionStrings:DefaultConnection。" +
            "请通过环境变量 CONNECTIONSTRINGS__SqliteConnection / CONNECTIONSTRINGS__DefaultConnection 显式注入，" +
            "或写入 appsettings.Production.json。");
    }

    // 1. AES 加密密钥（邮箱密码/OAuth Token 等敏感字段加密）
    var encryptionKey =
        Environment.GetEnvironmentVariable("ENCRYPTION__Key")
        ?? Environment.GetEnvironmentVariable("ENCRYPTION_KEY")
        ?? builder.Configuration["Encryption:Key"];
    if (string.IsNullOrWhiteSpace(encryptionKey))
    {
        throw new InvalidOperationException(
            "生产环境必须设置 ENCRYPTION__Key 或 ENCRYPTION_KEY 环境变量（AES 加密密钥）。");
    }

    // 2. 审计日志 HMAC 签名密钥（未配置时可回退 Encryption:Key，但生产必须显式配置）
    var auditHmacKey =
        Environment.GetEnvironmentVariable("AUDIT__HmacKey")
        ?? Environment.GetEnvironmentVariable("AUDIT_HMACKEY")
        ?? builder.Configuration["Audit:HmacKey"];
    if (string.IsNullOrWhiteSpace(auditHmacKey))
    {
        // 允许回退：Audit 未配置时复用 Encryption Key（带域分隔符签名区分）
        // 但不允许两者都空
        throw new InvalidOperationException(
            "生产环境必须设置 AUDIT__HmacKey 或 AUDIT_HMACKEY 环境变量（审计日志 HMAC 签名密钥）。" +
            "若希望复用加密密钥，请显式设置为与 ENCRYPTION__Key 相同的值（审计内部会加域分隔符）。");
    }

    // 3. 满意度评价令牌签名密钥
    var satisfactionTokenKey =
        Environment.GetEnvironmentVariable("APP__SatisfactionTokenKey")
        ?? Environment.GetEnvironmentVariable("APP_SATISFACTIONTOKENKEY")
        ?? builder.Configuration["App:SatisfactionTokenKey"];
    if (string.IsNullOrWhiteSpace(satisfactionTokenKey))
    {
        throw new InvalidOperationException(
            "生产环境必须设置 APP__SatisfactionTokenKey 或 APP_SATISFACTIONTOKENKEY 环境变量（满意度令牌签名密钥）。");
    }
    if (Encoding.UTF8.GetByteCount(satisfactionTokenKey) < 32)
    {
        throw new InvalidOperationException(
            "App:SatisfactionTokenKey 强度不足：至少需要 32 字节（UTF-8）。");
    }

    // 4. AzureAd OAuth 密钥：仅当配置了 ClientId（启用 Outlook 同步）时才强制校验
    var azureAdClientId =
        Environment.GetEnvironmentVariable("AZUREAD__ClientId")
        ?? Environment.GetEnvironmentVariable("AZUREAD_CLIENTID")
        ?? builder.Configuration["AzureAd:ClientId"];
    if (!string.IsNullOrWhiteSpace(azureAdClientId))
    {
        var azureAdClientSecret =
            Environment.GetEnvironmentVariable("AZUREAD__ClientSecret")
            ?? Environment.GetEnvironmentVariable("AZUREAD_CLIENTSECRET")
            ?? builder.Configuration["AzureAd:ClientSecret"];
        if (string.IsNullOrWhiteSpace(azureAdClientSecret))
        {
            throw new InvalidOperationException(
                "已启用 AzureAd OAuth（ClientId 已配置），生产环境必须设置 AZUREAD__ClientSecret 或 AZUREAD_CLIENTSECRET 环境变量。");
        }

        // L-05 校验 RedirectUri：appsettings.json 默认值指向 localhost:5008，
        // 生产环境必须通过环境变量 AZUREAD__RedirectUri 覆盖为 https:// 回调地址，
        // 否则 Azure OAuth 授权码流程回调可能失败或被劫持到非 HTTPS 地址。
        var azureAdRedirectUri =
            Environment.GetEnvironmentVariable("AZUREAD__RedirectUri")
            ?? Environment.GetEnvironmentVariable("AZUREAD_REDIRECTURI")
            ?? builder.Configuration["AzureAd:RedirectUri"];
        if (!string.IsNullOrWhiteSpace(azureAdRedirectUri)
            && !azureAdRedirectUri.StartsWith("https://", StringComparison.OrdinalIgnoreCase))
        {
            Log.Warning(
                "AzureAd:RedirectUri={RedirectUri} 未使用 HTTPS，生产环境应通过环境变量 AZUREAD__RedirectUri 覆盖为 https:// 回调地址，" +
                "否则 OAuth 回调可能失败或被中间人劫持。",
                azureAdRedirectUri);
        }
    }

    // 统一的配置读取口径（下面 5–12 各渠道块全部走它；AzureAd / VirusScan 两段本来就是同一顺序）：
    //   1) SECTION__Key —— .NET 环境变量的分段写法（双下划线 ⇒ 冒号），deploy/ 脚本与 docs/ 用的就是它；
    //   2) SECTION_KEY  —— 全大写单下划线的兼容别名（历史遗留，保留以免破坏既有部署；现所有渠道一致支持）；
    //   3) Section:Key  —— IConfiguration 回落（appsettings / user-secrets / 命令行）。
    // (1) 本来就会被 .NET 的环境变量提供程序映射进 (3)，显式先读 (1) 只为让优先级在代码里可见；appsettings
    // 给了值同样能通过下面的 fail-fast ⇒ 报错文案必须把三种形式都列出来，否则运维只会去翻环境变量。
    static string? ReadCfg(WebApplicationBuilder b, string sectionEnv, string sectionCfg, string key)
        => Environment.GetEnvironmentVariable($"{sectionEnv}__{key}")
           ?? Environment.GetEnvironmentVariable($"{sectionEnv}_{key.ToUpperInvariant()}")
           ?? b.Configuration[$"{sectionCfg}:{key}"];
    static string Need(string sectionEnv, string sectionCfg, string key, string note = "")
        => $"{sectionEnv}__{key}（或 {sectionEnv}_{key.ToUpperInvariant()} 环境变量 / {sectionCfg}:{key} 配置项）{note}";

    // 5. WeChat 接入密钥：仅当 Enabled=true 时强制校验
    var wechatEnabledRaw = ReadCfg(builder, "WECHAT", "WeChat", "Enabled");
    if (bool.TryParse(wechatEnabledRaw, out var wechatEnabled) && wechatEnabled)
    {
        var missing = new List<string>();
        if (string.IsNullOrWhiteSpace(ReadCfg(builder, "WECHAT", "WeChat", "AppSecret")))
            missing.Add(Need("WECHAT", "WeChat", "AppSecret"));
        if (string.IsNullOrWhiteSpace(ReadCfg(builder, "WECHAT", "WeChat", "Token")))
            missing.Add(Need("WECHAT", "WeChat", "Token"));
        // EncodingAESKey 仅在安全模式需要；但若明文已设置 appsecret/token，也强制要求避免后续误开
        var encryptMode = ReadCfg(builder, "WECHAT", "WeChat", "EncryptMode");
        if (!string.Equals(encryptMode, "plain", StringComparison.OrdinalIgnoreCase))
        {
            if (string.IsNullOrWhiteSpace(ReadCfg(builder, "WECHAT", "WeChat", "EncodingAESKey")))
                missing.Add(Need("WECHAT", "WeChat", "EncodingAESKey", "（非 plain 模式必需）"));
        }
        if (missing.Count > 0)
        {
            throw new InvalidOperationException(
            "已启用 WeChat 模块，生产环境必须设置以下配置项（环境变量或配置文件任一）：" + string.Join("、", missing));
        }
    }

    // 6. WhatsApp 接入密钥：仅当 Enabled=true 时强制校验
    var whatsappEnabledRaw = ReadCfg(builder, "WHATSAPP", "WhatsApp", "Enabled");
    if (bool.TryParse(whatsappEnabledRaw, out var whatsappEnabled) && whatsappEnabled)
    {
        var whatsappMissing = new List<string>();
        if (string.IsNullOrWhiteSpace(ReadCfg(builder, "WHATSAPP", "WhatsApp", "AccessToken")))
            whatsappMissing.Add(Need("WHATSAPP", "WhatsApp", "AccessToken"));
        if (string.IsNullOrWhiteSpace(ReadCfg(builder, "WHATSAPP", "WhatsApp", "AppSecret")))
            whatsappMissing.Add(Need("WHATSAPP", "WhatsApp", "AppSecret"));
        if (string.IsNullOrWhiteSpace(ReadCfg(builder, "WHATSAPP", "WhatsApp", "VerifyToken")))
            whatsappMissing.Add(Need("WHATSAPP", "WhatsApp", "VerifyToken"));
        if (string.IsNullOrWhiteSpace(ReadCfg(builder, "WHATSAPP", "WhatsApp", "PhoneNumberId")))
            whatsappMissing.Add(Need("WHATSAPP", "WhatsApp", "PhoneNumberId"));
        if (whatsappMissing.Count > 0)
        {
            throw new InvalidOperationException(
            "已启用 WhatsApp 模块，生产环境必须设置以下配置项（环境变量或配置文件任一）：" + string.Join("、", whatsappMissing));
        }
    }

    // 7. DingDing 接入密钥：仅当 Enabled=true 时强制校验
    var dingdingEnabledRaw = ReadCfg(builder, "DINGDING", "DingDing", "Enabled");
    if (bool.TryParse(dingdingEnabledRaw, out var dingdingEnabled) && dingdingEnabled)
    {
        var dingdingMissing = new List<string>();
        if (string.IsNullOrWhiteSpace(ReadCfg(builder, "DINGDING", "DingDing", "AppKey")))
            dingdingMissing.Add(Need("DINGDING", "DingDing", "AppKey"));
        if (string.IsNullOrWhiteSpace(ReadCfg(builder, "DINGDING", "DingDing", "AppSecret")))
            dingdingMissing.Add(Need("DINGDING", "DingDing", "AppSecret"));
        // HTTP 模式（回调）需 CallbackToken + EncodingAesKey；Stream 模式可省略
        var dingdingReceiveMode = ReadCfg(builder, "DINGDING", "DingDing", "ReceiveMode") ?? "Http";
        if (!string.Equals(dingdingReceiveMode, "Stream", StringComparison.OrdinalIgnoreCase))
        {
            if (string.IsNullOrWhiteSpace(ReadCfg(builder, "DINGDING", "DingDing", "CallbackToken")))
                dingdingMissing.Add(Need("DINGDING", "DingDing", "CallbackToken", "（HTTP 模式必需）"));
            if (string.IsNullOrWhiteSpace(ReadCfg(builder, "DINGDING", "DingDing", "EncodingAesKey")))
                dingdingMissing.Add(Need("DINGDING", "DingDing", "EncodingAesKey", "（HTTP 模式必需）"));
        }
        if (dingdingMissing.Count > 0)
        {
            throw new InvalidOperationException(
            "已启用 DingDing 模块，生产环境必须设置以下配置项（环境变量或配置文件任一）：" + string.Join("、", dingdingMissing));
        }
    }

    // 8. Feishu 接入密钥：仅当 Enabled=true 时强制校验
    var feishuEnabledRaw = ReadCfg(builder, "FEISHU", "Feishu", "Enabled");
    if (bool.TryParse(feishuEnabledRaw, out var feishuEnabled) && feishuEnabled)
    {
        var feishuMissing = new List<string>();
        if (string.IsNullOrWhiteSpace(ReadCfg(builder, "FEISHU", "Feishu", "AppId")))
            feishuMissing.Add(Need("FEISHU", "Feishu", "AppId"));
        if (string.IsNullOrWhiteSpace(ReadCfg(builder, "FEISHU", "Feishu", "AppSecret")))
            feishuMissing.Add(Need("FEISHU", "Feishu", "AppSecret"));
        // HTTP 模式需 VerificationToken；Stream 模式可省略
        var feishuReceiveMode = ReadCfg(builder, "FEISHU", "Feishu", "ReceiveMode") ?? "Http";
        if (!string.Equals(feishuReceiveMode, "Stream", StringComparison.OrdinalIgnoreCase))
        {
            if (string.IsNullOrWhiteSpace(ReadCfg(builder, "FEISHU", "Feishu", "VerificationToken")))
                feishuMissing.Add(Need("FEISHU", "Feishu", "VerificationToken", "（HTTP 模式必需）"));
        }
        if (feishuMissing.Count > 0)
        {
            throw new InvalidOperationException(
            "已启用 Feishu 模块，生产环境必须设置以下配置项（环境变量或配置文件任一）：" + string.Join("、", feishuMissing));
        }
    }

    // 9. Messenger（Meta Graph API）接入密钥：仅当 Enabled=true 时强制校验
    var messengerEnabledRaw = ReadCfg(builder, "MESSENGER", "Messenger", "Enabled");
    if (bool.TryParse(messengerEnabledRaw, out var messengerEnabled) && messengerEnabled)
    {
        var messengerMissing = new List<string>();
        if (string.IsNullOrWhiteSpace(ReadCfg(builder, "MESSENGER", "Messenger", "PageAccessToken")))
            messengerMissing.Add(Need("MESSENGER", "Messenger", "PageAccessToken"));
        // AppSecret 用于校验回调签名（X-Hub-Signature-256）：缺失则入站无法验签，直接拒绝启动
        if (string.IsNullOrWhiteSpace(ReadCfg(builder, "MESSENGER", "Messenger", "AppSecret")))
            messengerMissing.Add(Need("MESSENGER", "Messenger", "AppSecret", "（回调验签必需）"));
        if (string.IsNullOrWhiteSpace(ReadCfg(builder, "MESSENGER", "Messenger", "VerifyToken")))
            messengerMissing.Add(Need("MESSENGER", "Messenger", "VerifyToken", "（Webhook 订阅校验必需）"));
        if (messengerMissing.Count > 0)
        {
            throw new InvalidOperationException(
            "已启用 Messenger 模块，生产环境必须设置以下配置项（环境变量或配置文件任一）：" + string.Join("、", messengerMissing));
        }
    }

    // 10. Telegram Bot API 接入密钥：仅当 Enabled=true 时强制校验
    var telegramEnabledRaw = ReadCfg(builder, "TELEGRAM", "Telegram", "Enabled");
    if (bool.TryParse(telegramEnabledRaw, out var telegramEnabled) && telegramEnabled)
    {
        var telegramMissing = new List<string>();
        if (string.IsNullOrWhiteSpace(ReadCfg(builder, "TELEGRAM", "Telegram", "BotToken")))
            telegramMissing.Add(Need("TELEGRAM", "Telegram", "BotToken"));
        // Telegram 回调无请求体签名，secret_token 是入站来源的唯一校验手段
        var telegramMode = ReadCfg(builder, "TELEGRAM", "Telegram", "Mode") ?? "webhook";
        if (!string.Equals(telegramMode, "polling", StringComparison.OrdinalIgnoreCase)
            && string.IsNullOrWhiteSpace(ReadCfg(builder, "TELEGRAM", "Telegram", "WebhookSecretToken")))
            telegramMissing.Add(Need("TELEGRAM", "Telegram", "WebhookSecretToken", "（webhook 模式必需）"));
        if (telegramMissing.Count > 0)
        {
            throw new InvalidOperationException(
            "已启用 Telegram 模块，生产环境必须设置以下配置项（环境变量或配置文件任一）：" + string.Join("、", telegramMissing));
        }
    }

    // 11. LINE Messaging API 接入密钥：仅当 Enabled=true 时强制校验
    var lineEnabledRaw = ReadCfg(builder, "LINE", "Line", "Enabled");
    if (bool.TryParse(lineEnabledRaw, out var lineEnabled) && lineEnabled)
    {
        var lineMissing = new List<string>();
        // ChannelSecret 用于校验回调签名（X-Line-Signature），缺失则入站无法验签
        if (string.IsNullOrWhiteSpace(ReadCfg(builder, "LINE", "Line", "ChannelSecret")))
            lineMissing.Add(Need("LINE", "Line", "ChannelSecret", "（回调验签必需）"));
        if (string.IsNullOrWhiteSpace(ReadCfg(builder, "LINE", "Line", "ChannelAccessToken")))
            lineMissing.Add(Need("LINE", "Line", "ChannelAccessToken"));
        if (lineMissing.Count > 0)
        {
            throw new InvalidOperationException(
            "已启用 LINE 模块，生产环境必须设置以下配置项（环境变量或配置文件任一）：" + string.Join("、", lineMissing));
        }
    }

    // 12. iMessage / Apple Messages 接入参数：仅当 Enabled=true 时按传输模式强制校验
    var imessageEnabledRaw = ReadCfg(builder, "IMESSAGE", "IMessage", "Enabled");
    if (bool.TryParse(imessageEnabledRaw, out var imessageEnabled) && imessageEnabled)
    {
        var imessageMissing = new List<string>();
        var imessageMode = ReadCfg(builder, "IMESSAGE", "IMessage", "Mode") ?? "bridge";
        if (string.Equals(imessageMode, "bridge", StringComparison.OrdinalIgnoreCase))
        {
            if (string.IsNullOrWhiteSpace(ReadCfg(builder, "IMESSAGE", "IMessage", "BaseUrl")))
                imessageMissing.Add(Need("IMESSAGE", "IMessage", "BaseUrl", "（桥接服务地址）"));
            if (string.IsNullOrWhiteSpace(ReadCfg(builder, "IMESSAGE", "IMessage", "Password")))
                imessageMissing.Add(Need("IMESSAGE", "IMessage", "Password", "（桥接服务口令）"));
        }
        else
        {
            if (string.IsNullOrWhiteSpace(ReadCfg(builder, "IMESSAGE", "IMessage", "ApiBaseUrl")))
                imessageMissing.Add(Need("IMESSAGE", "IMessage", "ApiBaseUrl", "（MSP 接入地址）"));
            if (string.IsNullOrWhiteSpace(ReadCfg(builder, "IMESSAGE", "IMessage", "ApiKey")))
                imessageMissing.Add(Need("IMESSAGE", "IMessage", "ApiKey"));
            if (string.IsNullOrWhiteSpace(ReadCfg(builder, "IMESSAGE", "IMessage", "WebhookSecret")))
                imessageMissing.Add(Need("IMESSAGE", "IMessage", "WebhookSecret", "（入站验签密钥）"));
        }
        if (imessageMissing.Count > 0)
        {
            throw new InvalidOperationException(
            "已启用 iMessage 模块，生产环境必须设置以下配置项（环境变量或配置文件任一）：" + string.Join("、", imessageMissing));
        }
    }

    // 13. 附件病毒扫描：生产环境默认应启用 ClamAV。未启用时仅警告不阻断启动，
    //    但建议在环境变量 VirusScan__EnableClamAv=true 启用并部署 ClamAV 守护进程。
    var virusScanEnabledRaw =
        Environment.GetEnvironmentVariable("VIRUSSCAN__EnableClamAv")
        ?? Environment.GetEnvironmentVariable("VIRUSSCAN_ENABLECLAMAV")
        ?? builder.Configuration["VirusScan:EnableClamAv"];
    if (!bool.TryParse(virusScanEnabledRaw, out var virusScanEnabled) || !virusScanEnabled)
    {
        Log.Warning("生产环境未启用附件病毒扫描（VirusScan:EnableClamAv=false），建议配置 ClamAV 并设置环境变量 VirusScan__EnableClamAv=true");
    }
}

// 设置默认值（仅在配置文件和环境变量都未提供时使用）
jwtIssuer ??= "NoneadCSS";
jwtAudience ??= "NoneadCSSApi";

// 将解析后的密钥写回配置系统，使 AuthService 等通过 IConfiguration 读取密钥的服务也能获取到
builder.Configuration["Jwt:SecretKey"] = jwtKey;
builder.Configuration["Jwt:Issuer"] = jwtIssuer;
builder.Configuration["Jwt:Audience"] = jwtAudience;

// H-08：初始化 SecretHasher，使用独立的密钥（密钥分离原则：JWT 签名密钥与 HMAC 哈希密钥不应复用）。
// 已删除"开发环境回退到 jwtKey"分支——避免密钥复用导致单密钥攻破两道防线。
// 开发环境若未配置 SecretHasher:Key，则自动生成一次性随机密钥（与 JWT 的处理口径一致），
// 既不阻塞「克隆即运行」，也不与 JWT 复用；生产环境仍必须通过环境变量显式注入。
var secretHasherKey =
    Environment.GetEnvironmentVariable("SECRET_HASHER__Key")
    ?? Environment.GetEnvironmentVariable("SECRET_HASHER_KEY")
    ?? builder.Configuration["SecretHasher:Key"];
if (string.IsNullOrWhiteSpace(secretHasherKey))
{
    if (!builder.Environment.IsProduction())
    {
        secretHasherKey = Convert.ToBase64String(System.Security.Cryptography.RandomNumberGenerator.GetBytes(48));
        builder.Configuration["SecretHasher:Key"] = secretHasherKey;
        Log.Warning("[DEV] 未配置 SecretHasher:Key，已自动生成开发环境 HMAC 密钥（每次启动随机）。" +
                    "影响：ApiKeys / Webhooks 等 HMAC 哈希在重启后失效，开发库中已发的 API Key 需重新生成；" +
                    "如需固定，请设置环境变量 SECRET_HASHER__Key 或在 appsettings.Development.json 配置 SecretHasher:Key。生产环境仍强制显式注入。");
    }
    else
    {
        throw new InvalidOperationException(
            "必须设置 SECRET_HASHER__Key 或 SECRET_HASHER_KEY 环境变量（SecretHasher HMAC 密钥，与 JWT 密钥隔离）。" +
            "开发环境请在 appsettings.Development.json 的 SecretHasher:Key 配置独立密钥；生产环境必须通过环境变量注入。");
    }
}
NoneadCSS.Application.Security.SecretHasher.Initialize(secretHasherKey!);

builder.Services.AddAuthentication(options =>
{
    options.DefaultAuthenticateScheme = JwtBearerDefaults.AuthenticationScheme;
    options.DefaultChallengeScheme = JwtBearerDefaults.AuthenticationScheme;
})
.AddJwtBearer(options =>
{
    options.TokenValidationParameters = new TokenValidationParameters
    {
        ValidateIssuer = true,
        ValidateAudience = true,
        ValidateLifetime = true,
        ValidateIssuerSigningKey = true,
        ValidIssuer = jwtIssuer,
        ValidAudience = jwtAudience,
        IssuerSigningKey = new SymmetricSecurityKey(Encoding.UTF8.GetBytes(jwtKey)),
        ClockSkew = TimeSpan.Zero
    };

    // 校验全局令牌版本号，支持"强制所有用户登出"功能
    // 当管理员执行强制登出时，security.token_global_version 会递增，
    // 所有旧令牌的 gv claim 与当前版本号不匹配，将被拒绝
    options.Events = new JwtBearerEvents
    {
        // 优先从 HttpOnly Cookie 读取 token（XSS 防护）；
        // 若 Cookie 中无 token，则回退到 Authorization header（保持向后兼容）
        OnMessageReceived = context =>
        {
            var token = context.Request.Cookies["access_token"];
            if (!string.IsNullOrEmpty(token))
            {
                context.Token = token;
            }
            return Task.CompletedTask;
        },
        OnTokenValidated = async context =>
        {
            var gvClaim = context.Principal?.FindFirst("gv")?.Value;
            if (string.IsNullOrEmpty(gvClaim))
            {
                // 旧令牌没有 gv claim，允许通过（向后兼容）
                return;
            }

            try
            {
                var unitOfWork = context.HttpContext.RequestServices.GetRequiredService<IUnitOfWork>();
                var currentVersion = await unitOfWork.SystemOptions.GetValueAsync("security.token_global_version") ?? "0";

                if (gvClaim != currentVersion)
                {
                    context.Fail("令牌已被失效，请重新登录");
                }
            }
            catch (Exception ex)
            {
                var logger = context.HttpContext.RequestServices.GetRequiredService<ILogger<Program>>();
                logger.LogError(ex, "校验令牌版本号失败，按 fail-closed 拒绝本次请求");
                // 安全优先（fail-closed）：无法确认令牌是否已被全局吊销时必须拒绝。
                // 原实现只记日志不阻断，导致数据库抖动窗口内已登出/已撤销的令牌仍可继续访问（审核报告 P1）。
                // 代价是 DB 不可用时该请求返回 401，但此时下游业务同样不可用，安全收益大于可用性损失。
                context.Fail("令牌状态校验失败，请稍后重试");
            }
        }
    };
})
.AddScheme<ApiKeyAuthenticationOptions, ApiKeyAuthenticationHandler>(
    ApiKeyAuthenticationOptions.Scheme,
    options => { });

builder.Services.AddAuthorization(options =>
{
    // 默认策略：优先 JWT，ApiKey 作为备用
    options.DefaultPolicy = new Microsoft.AspNetCore.Authorization.AuthorizationPolicyBuilder(
        JwtBearerDefaults.AuthenticationScheme,
        ApiKeyAuthenticationOptions.Scheme)
        .RequireAuthenticatedUser()
        .Build();
});

builder.Services.AddPermissionAuthorization();

// Rate Limiting 配置：防止暴力破解攻击
var rateLimitCfg = builder.Configuration.GetSection("Performance:RateLimit").Get<NoneadCSS.Application.Configuration.RateLimitOptions>()
    ?? new NoneadCSS.Application.Configuration.RateLimitOptions();
var perMinuteWindow = TimeSpan.FromSeconds(rateLimitCfg.PerMinuteWindowSeconds);
var perHourWindow = TimeSpan.FromSeconds(rateLimitCfg.PerHourWindowSeconds);
builder.Services.AddRateLimiter(options =>
{
    options.RejectionStatusCode = StatusCodes.Status429TooManyRequests;

    // 全局基础限流策略：每 IP 每分钟最多 300 次请求（用于未指定策略的接口）
    options.AddSlidingWindowLimiter("GlobalPolicy", slidingOptions =>
    {
        slidingOptions.PermitLimit = 300;
        slidingOptions.Window = perMinuteWindow;
        slidingOptions.SegmentsPerWindow = 6;
        slidingOptions.QueueProcessingOrder = QueueProcessingOrder.OldestFirst;
        slidingOptions.QueueLimit = 0;
    });

    // M-03 全局限流器：与 GlobalPolicy 参数保持一致，对未通过 [EnableRateLimiting("...")]
    // 显式指定策略的接口生效（含未标注的公共端点），确保全局限流不会因为缺少策略特性而被绕过。
    // 分区键使用客户端 IP，避免单 IP 配额被多 IP 耗尽；RemoteIpAddress 为空时回退到 "unknown"。
    options.GlobalLimiter = PartitionedRateLimiter.Create<HttpContext, string>(httpContext =>
    {
        var ipAddress = httpContext.Connection.RemoteIpAddress?.ToString() ?? "unknown";
        return RateLimitPartition.GetSlidingWindowLimiter(ipAddress, _ => new SlidingWindowRateLimiterOptions
        {
            PermitLimit = 300,
            Window = perMinuteWindow,
            SegmentsPerWindow = 6,
            QueueProcessingOrder = QueueProcessingOrder.OldestFirst,
            QueueLimit = 0
        });
    });

    // 登录接口限流策略：每 IP 每分钟最多 5 次尝试
    options.AddSlidingWindowLimiter("LoginPolicy", slidingOptions =>
    {
        slidingOptions.PermitLimit = 5;
        slidingOptions.Window = perMinuteWindow;
        slidingOptions.SegmentsPerWindow = 2;
        slidingOptions.QueueProcessingOrder = QueueProcessingOrder.OldestFirst;
        slidingOptions.QueueLimit = 0; // 不允许排队，直接拒绝
    });

    // 密码重置接口限流策略：每 IP 每小时最多 3 次
    options.AddSlidingWindowLimiter("ForgotPasswordPolicy", slidingOptions =>
    {
        slidingOptions.PermitLimit = 3;
        slidingOptions.Window = perHourWindow;
        slidingOptions.SegmentsPerWindow = 4;
        slidingOptions.QueueProcessingOrder = QueueProcessingOrder.OldestFirst;
        slidingOptions.QueueLimit = 0; // 不允许排队，直接拒绝
    });

    // 全局搜索限流策略：每 IP 每分钟最多 30 次（防止大规模扫描）
    options.AddSlidingWindowLimiter("SearchPolicy", slidingOptions =>
    {
        slidingOptions.PermitLimit = 30;
        slidingOptions.Window = perMinuteWindow;
        slidingOptions.SegmentsPerWindow = 3;
        slidingOptions.QueueProcessingOrder = QueueProcessingOrder.OldestFirst;
        slidingOptions.QueueLimit = 0;
    });

    // 报表接口限流策略：每 IP 每分钟最多 10 次（报表为重型计算）
    options.AddSlidingWindowLimiter("ReportsPolicy", slidingOptions =>
    {
        slidingOptions.PermitLimit = 10;
        slidingOptions.Window = perMinuteWindow;
        slidingOptions.SegmentsPerWindow = 2;
        slidingOptions.QueueProcessingOrder = QueueProcessingOrder.OldestFirst;
        slidingOptions.QueueLimit = 0;
    });

    // 在线聊天客户端接口限流：每 IP 每分钟最多 30 次请求
    options.AddSlidingWindowLimiter("ChatClientPolicy", slidingOptions =>
    {
        slidingOptions.PermitLimit = 30;
        slidingOptions.Window = perMinuteWindow;
        slidingOptions.SegmentsPerWindow = 6;
        slidingOptions.QueueProcessingOrder = QueueProcessingOrder.OldestFirst;
        slidingOptions.QueueLimit = 0;
    });

    // H-02：聊天写操作严格限流策略：每 IP 每分钟最多 10 次（initiate / send-message）
    // 比 ChatClientPolicy（30 次/分钟）更严格，防止恶意机器人批量创建会话、发送垃圾消息
    options.AddSlidingWindowLimiter("ChatClientStrictPolicy", slidingOptions =>
    {
        slidingOptions.PermitLimit = 10;
        slidingOptions.Window = perMinuteWindow;
        slidingOptions.SegmentsPerWindow = 2;
        slidingOptions.QueueProcessingOrder = QueueProcessingOrder.OldestFirst;
        slidingOptions.QueueLimit = 0;
    });

    // 邮件追踪像素限流：每 IP 每分钟最多 60 次请求
    options.AddSlidingWindowLimiter("TrackingPolicy", slidingOptions =>
    {
        slidingOptions.PermitLimit = 60;
        slidingOptions.Window = perMinuteWindow;
        slidingOptions.SegmentsPerWindow = 6;
        slidingOptions.QueueProcessingOrder = QueueProcessingOrder.OldestFirst;
        slidingOptions.QueueLimit = 0;
    });

    // 满意度评价公开提交限流：每 IP 每小时最多 10 次（防止评价数据被恶意刷库）
    options.AddSlidingWindowLimiter("SatisfactionPolicy", slidingOptions =>
    {
        slidingOptions.PermitLimit = 10;
        slidingOptions.Window = perHourWindow;
        slidingOptions.SegmentsPerWindow = 6;
        slidingOptions.QueueProcessingOrder = QueueProcessingOrder.OldestFirst;
        slidingOptions.QueueLimit = 0;
    });

    // 注册接口限流：每小时 5 次，防止批量注册垃圾账号
    options.AddSlidingWindowLimiter("RegisterPolicy", slidingOptions =>
    {
        slidingOptions.PermitLimit = 5;
        slidingOptions.Window = perHourWindow;
        slidingOptions.SegmentsPerWindow = 2;
        slidingOptions.QueueProcessingOrder = QueueProcessingOrder.OldestFirst;
        slidingOptions.QueueLimit = 0;
    });

    // Refresh Token 限流：每小时 30 次
    options.AddSlidingWindowLimiter("RefreshTokenPolicy", slidingOptions =>
    {
        slidingOptions.PermitLimit = 30;
        slidingOptions.Window = perHourWindow;
        slidingOptions.SegmentsPerWindow = 6;
        slidingOptions.QueueProcessingOrder = QueueProcessingOrder.OldestFirst;
        slidingOptions.QueueLimit = 0;
    });

    // 密码重置限流：每小时 10 次
    options.AddSlidingWindowLimiter("ResetPasswordPolicy", slidingOptions =>
    {
        slidingOptions.PermitLimit = 10;
        slidingOptions.Window = perHourWindow;
        slidingOptions.SegmentsPerWindow = 2;
        slidingOptions.QueueProcessingOrder = QueueProcessingOrder.OldestFirst;
        slidingOptions.QueueLimit = 0;
    });

    // 安装接口限流：每 IP 每小时最多 10 次（防止暴力安装或抢占）
    options.AddSlidingWindowLimiter("SetupPolicy", slidingOptions =>
    {
        slidingOptions.PermitLimit = 10;
        slidingOptions.Window = perHourWindow;
        slidingOptions.SegmentsPerWindow = 2;
        slidingOptions.QueueProcessingOrder = QueueProcessingOrder.OldestFirst;
        slidingOptions.QueueLimit = 0;
    });

    // 知识库点赞限流：每 IP 每分钟最多 20 次（防止恶意刷点赞数）
    // 使用 AddPolicy + GetSlidingWindowLimiter 实现按 IP 分区，避免全局配额被单用户耗尽
    options.AddPolicy("KnowledgeLikePolicy", context =>
    {
        var ipAddress = context.Connection.RemoteIpAddress?.ToString() ?? "unknown";
        return RateLimitPartition.GetSlidingWindowLimiter(ipAddress, _ => new SlidingWindowRateLimiterOptions
        {
            PermitLimit = 20,
            Window = perMinuteWindow,
            SegmentsPerWindow = 4,
            QueueProcessingOrder = QueueProcessingOrder.OldestFirst,
            QueueLimit = 0
        });
    });

    // 系统日志查看限流：每 IP 每分钟最多 120 次（实时监控每 2 秒 1 次 = 30 次/分钟，留 4 倍余量）
    // 独立于全局限流器，避免与全局 300 次/分钟配额争抢，保证实时监控稳定运行
    options.AddPolicy("SystemLogsPolicy", context =>
    {
        var ipAddress = context.Connection.RemoteIpAddress?.ToString() ?? "unknown";
        return RateLimitPartition.GetSlidingWindowLimiter(ipAddress, _ => new SlidingWindowRateLimiterOptions
        {
            PermitLimit = 120,
            Window = perMinuteWindow,
            SegmentsPerWindow = 6,
            QueueProcessingOrder = QueueProcessingOrder.OldestFirst,
            QueueLimit = 0
        });
    });

    // H-05/H-C2: 日历订阅 Feed 限流（匿名 Token 访问）：每 IP 每分钟最多 10 次，
    // 防止恶意枚举/拉取大量 ICS 数据导致调度信息泄露或数据库/内存压力。
    // Token 本身为 HMAC 签名（不可枚举），此限流为深度防御层。
    options.AddSlidingWindowLimiter("CalendarFeedPolicy", slidingOptions =>
    {
        slidingOptions.PermitLimit = 10;
        slidingOptions.Window = perMinuteWindow;
        slidingOptions.SegmentsPerWindow = 2;
        slidingOptions.QueueProcessingOrder = QueueProcessingOrder.OldestFirst;
        slidingOptions.QueueLimit = 0;
    });

    // H-C3: 日历重型操作（导入/导出/同步）限流：每 IP 每分钟最多 10 次，
    // 这些操作涉及大量数据库读写与外部 API 调用，需防止滥用导致 DoS
    options.AddSlidingWindowLimiter("CalendarHeavyPolicy", slidingOptions =>
    {
        slidingOptions.PermitLimit = 10;
        slidingOptions.Window = perMinuteWindow;
        slidingOptions.SegmentsPerWindow = 2;
        slidingOptions.QueueProcessingOrder = QueueProcessingOrder.OldestFirst;
        slidingOptions.QueueLimit = 0;
    });

    // 日历轻量操作（如提醒确认）限流：每 IP 每分钟最多 30 次。
    // 该端点由用户主动触发且为轻量交互，配额略高于重型操作，避免正常用户误触限流。
    options.AddSlidingWindowLimiter("CalendarLightPolicy", slidingOptions =>
    {
        slidingOptions.PermitLimit = 30;
        slidingOptions.Window = perMinuteWindow;
        slidingOptions.SegmentsPerWindow = 2;
        slidingOptions.QueueProcessingOrder = QueueProcessingOrder.OldestFirst;
        slidingOptions.QueueLimit = 0;
    });

    // 自定义限流拒绝响应（结构化日志记录，便于监控告警）
    options.OnRejected = async (context, token) =>
    {
        context.HttpContext.Response.StatusCode = StatusCodes.Status429TooManyRequests;
        context.HttpContext.Response.ContentType = "application/json; charset=utf-8";

        var retryAfter = context.Lease.TryGetMetadata(MetadataName.RetryAfter, out var retryAfterValue)
            ? retryAfterValue.TotalSeconds
            : 60;

        var rateMessage = $"请求过于频繁，请在 {retryAfter} 秒后重试";
        var response = new
        {
            // 限流拒绝是最贴近用户的出口之一（429 直写裸 JSON，不走 ApiResponse 信封），必须在构造时换文案
            message = NoneadCSS.Api.Localization.ApiMessageLocalizer.Localize(
                rateMessage, NoneadCSS.Api.Localization.ApiRequestCulture.Of(context.HttpContext)) ?? rateMessage,
            retryAfter = (int)retryAfter,
            traceId = context.HttpContext.TraceIdentifier
        };

        var logger = context.HttpContext.RequestServices.GetRequiredService<ILoggerFactory>()
            .CreateLogger("RateLimiter");
        logger.LogWarning("限流拒绝: Path={Path}, Method={Method}, TraceId={TraceId}, RetryAfter={RetryAfter}s",
            context.HttpContext.Request.Path,
            context.HttpContext.Request.Method,
            context.HttpContext.TraceIdentifier,
            (int)retryAfter);

        await context.HttpContext.Response.WriteAsJsonAsync(response, token);
    };
});

// CORS 策略：统一从 IConfiguration / 环境变量读取白名单；
// 开发环境无配置时默认放行本地常见开发端口（localhost / 127.0.0.1），
// 业务域名、出口 IP 等必须通过配置显式指定，禁止永久硬编码到源码
var corsAllowedOrigins = new List<string>();

// 统一读取顺序：环境变量 CORS__AllowedOrigins > appsettings 配置 > (开发环境兜底本地端口)
var envOriginsGlobal = Environment.GetEnvironmentVariable("CORS__AllowedOrigins");
var configOrigins = builder.Configuration.GetSection("Cors:AllowedOrigins").Get<string[]>();
var envOrigins = !string.IsNullOrEmpty(envOriginsGlobal)
    ? envOriginsGlobal.Split(',', StringSplitOptions.RemoveEmptyEntries).Select(o => o.Trim())
    : Enumerable.Empty<string>();
var configuredOrigins = (configOrigins ?? Enumerable.Empty<string>())
    .Concat(envOrigins)
    .Distinct(StringComparer.OrdinalIgnoreCase)
    .ToList();

if (configuredOrigins.Count > 0)
{
    corsAllowedOrigins.AddRange(configuredOrigins);
}
else if (builder.Environment.IsDevelopment())
{
    // 开发环境兜底：仅放行本地回环 + 常见开发端口（Vite / CRA / Vue CLI / http/https 默认）
    var defaultLocalPorts = new[] { "5171", "5172", "5173", "3000", "8080", "80", "443" };
    var defaultSchemes = new[] { "http://", "https://" };
    var defaultHosts = new[] { "localhost", "127.0.0.1" };
    foreach (var scheme in defaultSchemes)
        foreach (var host in defaultHosts)
            foreach (var port in defaultLocalPorts)
                corsAllowedOrigins.Add($"{scheme}{host}:{port}");
}
else
{
    // 生产环境：未显式配置 CORS 白名单 → Fail Fast，
    // 避免部署者忘记设置环境变量，误用旧默认域名造成数据外泄。
    throw new InvalidOperationException(
        "CORS 生产环境白名单未配置。请设置环境变量 CORS__AllowedOrigins " +
        "（以逗号分隔多个域名，例如 \"https://a.example.com,https://b.example.com\"）；" +
        "或在 appsettings.json 配置 \"Cors\":{\"AllowedOrigins\":[\"https://a.example.com\"]}。");
}

builder.Services.AddCors(options =>
{
    // 策略名改为 AllowedOrigins，准确反映白名单语义（之前误导性地命名为 AllowAll）
    options.AddPolicy("AllowedOrigins", policy =>
    {
        policy.WithOrigins(corsAllowedOrigins.ToArray())
              .WithMethods("GET", "POST", "PUT", "DELETE", "PATCH", "OPTIONS")
              .WithHeaders("Authorization", "Content-Type", "X-Requested-With")
              .AllowCredentials();
    });

    options.AddPolicy("SignalR", policy =>
    {
        policy.WithOrigins(corsAllowedOrigins.ToArray())
              .WithMethods("GET", "POST", "OPTIONS")
              .WithHeaders("Authorization", "Content-Type", "X-Requested-With")
              .AllowCredentials();
    });
});

builder.Services.Configure<AttachmentSettings>(builder.Configuration.GetSection("Attachment"));
builder.Services.Configure<NoneadCSS.Infrastructure.Services.VirusScanOptions>(
    builder.Configuration.GetSection("VirusScan"));
builder.Services.AddSingleton<NoneadCSS.Infrastructure.Services.IVirusScanService, NoneadCSS.Infrastructure.Services.VirusScanService>();
builder.Services.AddOptions<AppOptions>()
    .Bind(builder.Configuration.GetSection("App"))
    .ValidateDataAnnotations()
    .ValidateOnStart();
builder.Services.AddOptions<NoneadCSS.Application.Configuration.BrandOptions>()
    .Bind(builder.Configuration.GetSection(NoneadCSS.Application.Configuration.BrandOptions.SectionName))
    // 联动回退：部署者只配了 App:FrontendUrl 时，自动把值同步进 BrandOptions.FrontendUrl/LoginUrl/SupportEmail，
    // 避免邮件模板预览、IM 消息卡片、通知模板里的外部链接变成空相对路径。
    .PostConfigure<IConfiguration>((brand, config) =>
    {
        if (string.IsNullOrWhiteSpace(brand.FrontendUrl))
        {
            brand.FrontendUrl = (config["App:FrontendUrl"] ?? config["App:BaseUrl"] ?? "").TrimEnd('/');
        }
        if (string.IsNullOrWhiteSpace(brand.LoginUrl) && !string.IsNullOrWhiteSpace(brand.FrontendUrl))
        {
            brand.LoginUrl = $"{brand.FrontendUrl.TrimEnd('/')}/login";
        }
        if (string.IsNullOrWhiteSpace(brand.SupportEmail))
        {
            brand.SupportEmail = config["App:SupportEmail"] ?? "";
        }
    })
    .ValidateDataAnnotations()
    .ValidateOnStart();
builder.Services.Configure<NoneadCSS.Application.Configuration.WeChatOptions>(
    builder.Configuration.GetSection(WeChatOptions.SectionName));

// M-07: 冷启动一次性安装令牌（SetupController 的三个 POST 端点需携带 X-Setup-Token）
builder.Services.AddSingleton<NoneadCSS.Api.Services.ISetupTokenService, NoneadCSS.Api.Services.SetupTokenService>();
builder.Services.AddHostedService<NoneadCSS.Api.Services.SetupTokenBootstrapService>();

// 启动时加载微信配置（避免首次访问 Current 读取 appsettings 基线而非数据库合并值）
builder.Services.AddHostedService<WeChatSettingsBootstrapService>();

// 在线聊天：客服发起关闭后 3 分钟未评分 → 自动关闭会话 + 发邮件
builder.Services.AddHostedService<NoneadCSS.Infrastructure.Services.ChatRatingTimeoutService>();

// D#3: Outlook 同步重试队列处理器（每 30s 扫描一次，支持指数退避和死信）
builder.Services.AddHostedService<NoneadCSS.Api.Services.OutlookRetryProcessor>();

// WhatsApp 配置绑定
builder.Services.Configure<NoneadCSS.Application.Configuration.WhatsAppOptions>(
    builder.Configuration.GetSection(WhatsAppOptions.SectionName));

// 飞书配置绑定
builder.Services.Configure<FeishuOptions>(builder.Configuration.GetSection(FeishuOptions.SectionName));

// Messenger（Meta Graph API）配置绑定
builder.Services.Configure<MessengerOptions>(builder.Configuration.GetSection(MessengerOptions.SectionName));

// Telegram Bot API 配置绑定
builder.Services.Configure<TelegramOptions>(builder.Configuration.GetSection(TelegramOptions.SectionName));

// LINE Messaging API 配置绑定
builder.Services.Configure<LineOptions>(builder.Configuration.GetSection(LineOptions.SectionName));

// iMessage / Apple Messages 配置绑定（bridge 或 amb_*，默认关闭）
builder.Services.Configure<IMessageOptions>(builder.Configuration.GetSection(IMessageOptions.SectionName));

// /metrics 端点访问控制（默认强制 SuperAdmin，可通过 Security:Metrics:AllowedIpRanges 加内网 Prometheus 白名单）
builder.Services.Configure<NoneadCSS.Application.Configuration.MetricsOptions>(builder.Configuration.GetSection("Security:Metrics"));

// 性能与超时配置：将硬编码 TimeSpan 提取到 IOptions，支持环境变量覆盖
builder.Services.Configure<NoneadCSS.Application.Configuration.EmailOptions>(builder.Configuration.GetSection("Email"));
builder.Services.Configure<NoneadCSS.Application.Configuration.ChatOptions>(builder.Configuration.GetSection("Performance:Chat"));
builder.Services.Configure<NoneadCSS.Application.Configuration.CacheOptions>(builder.Configuration.GetSection("Performance:Cache"));
builder.Services.Configure<NoneadCSS.Application.Configuration.IngestionMatchOptions>(builder.Configuration.GetSection("Performance:IngestionMatch"));
builder.Services.Configure<NoneadCSS.Application.Configuration.HttpClientOptions>(builder.Configuration.GetSection("Performance:HttpClient"));
builder.Services.Configure<NoneadCSS.Application.Configuration.ResponseCachingOptions>(builder.Configuration.GetSection("Performance:ResponseCaching"));
builder.Services.Configure<NoneadCSS.Application.Configuration.RateLimitOptions>(builder.Configuration.GetSection("Performance:RateLimit"));
builder.Services.Configure<NoneadCSS.Application.Configuration.SignalROptions>(builder.Configuration.GetSection("Performance:SignalR"));
builder.Services.Configure<NoneadCSS.Application.Configuration.TokenRefreshOptions>(builder.Configuration.GetSection("Performance:TokenRefresh"));
builder.Services.Configure<NoneadCSS.Application.Configuration.RetryOptions>(builder.Configuration.GetSection("Performance:Retry"));
builder.Services.Configure<NoneadCSS.Application.Configuration.KnowledgeOptions>(builder.Configuration.GetSection("Performance:Knowledge"));

builder.Services.AddInfrastructure(builder.Configuration);
builder.Services.AddApplication();
builder.Services.AddHttpClient();

// 注册当前用户访问器（Service 层归属过滤依赖）
builder.Services.AddHttpContextAccessor();
builder.Services.AddScoped<NoneadCSS.Application.Services.ICurrentUserAccessor, NoneadCSS.Api.Authentication.HttpContextCurrentUserAccessor>();

// SignalR 实时通信：仅在开发环境启用详细错误
var signalRCfg = builder.Configuration.GetSection("Performance:SignalR").Get<NoneadCSS.Application.Configuration.SignalROptions>()
    ?? new NoneadCSS.Application.Configuration.SignalROptions();
builder.Services.AddSignalR(options =>
{
    options.EnableDetailedErrors = builder.Environment.IsDevelopment();
    options.KeepAliveInterval = TimeSpan.FromSeconds(signalRCfg.KeepAliveIntervalSeconds);
    options.ClientTimeoutInterval = TimeSpan.FromSeconds(signalRCfg.ClientTimeoutIntervalSeconds);
});

// 注册强类型领域事件分发器（接口已移到 Domain 层，便于 Application 服务直接依赖）
builder.Services.AddSingleton<NoneadCSS.Domain.Events.IDomainEventDispatcher, NoneadCSS.Infrastructure.Services.DomainEventDispatcher>();

// 注册所有事件监听器（对应 PHP 版本的 Listeners）
// 说明：RefreshConversations / LogUserDeletion / ProcessSwiftMessage / UserCreatedConversation /
// UserAddedNote / UserCreatedDraft 六个监听器已删除——其副作用（SignalR 刷新、审计写入、SWIFT 解析）
// 已分别由 NoneadCSS.Api/Controllers/RealtimeController.cs 与 NoneadCSS.Api/Filters/AuditActionFilter.cs 内联完成，
// 接线只会造成重复；SWIFT 通道在本仓库不存在（无 SourceType.Swift、无入站实现）。
builder.Services.AddScoped<NoneadCSS.Infrastructure.Services.IListener<NoneadCSS.Domain.Events.UserLoggedInEvent>, NoneadCSS.Infrastructure.Listeners.RememberUserLocaleListener>();
builder.Services.AddScoped<NoneadCSS.Infrastructure.Services.IListener<NoneadCSS.Domain.Events.PasswordChangedEvent>, NoneadCSS.Infrastructure.Listeners.SendPasswordChangedListener>();
builder.Services.AddScoped<NoneadCSS.Infrastructure.Services.IListener<NoneadCSS.Domain.Events.PasswordResetEvent>, NoneadCSS.Infrastructure.Listeners.LogPasswordResetListener>();
builder.Services.AddScoped<NoneadCSS.Infrastructure.Services.IListener<NoneadCSS.Domain.Events.LoginFailedEvent>, NoneadCSS.Infrastructure.Listeners.LogFailedLoginListener>();
builder.Services.AddScoped<NoneadCSS.Infrastructure.Services.IListener<NoneadCSS.Domain.Events.LockoutEvent>, NoneadCSS.Infrastructure.Listeners.LogLockoutListener>();
builder.Services.AddScoped<NoneadCSS.Infrastructure.Services.IListener<NoneadCSS.Domain.Events.UserActivatedEvent>, NoneadCSS.Infrastructure.Listeners.ActivateUserListener>();
builder.Services.AddScoped<NoneadCSS.Infrastructure.Services.IListener<NoneadCSS.Domain.Events.MailboxUpdatedEvent>, NoneadCSS.Infrastructure.Listeners.UpdateMailboxCountersListener>();
builder.Services.AddScoped<NoneadCSS.Infrastructure.Services.IListener<NoneadCSS.Domain.Events.ConversationStatusChangedEvent>, NoneadCSS.Infrastructure.Listeners.ConversationStatusChangedListener>();
builder.Services.AddScoped<NoneadCSS.Infrastructure.Services.IListener<NoneadCSS.Domain.Events.ConversationUserChangedEvent>, NoneadCSS.Infrastructure.Listeners.ConversationUserChangedListener>();
builder.Services.AddScoped<NoneadCSS.Infrastructure.Services.IListener<NoneadCSS.Domain.Events.UserRepliedEvent>, NoneadCSS.Infrastructure.Listeners.UserRepliedListener>();

var app = builder.Build();

// 生产环境启动校验
if (app.Environment.IsProduction())
{
    // T5: 生产环境外发链接（密码重置/满意度/通知）若使用 HTTP 存在中间人截获风险，启动时告警
    var frontendUrl = builder.Configuration["App:FrontendUrl"]
                      ?? builder.Configuration["App:BaseUrl"]
                      ?? string.Empty;
    if (!string.IsNullOrWhiteSpace(frontendUrl)
        && !frontendUrl.StartsWith("https://", StringComparison.OrdinalIgnoreCase))
    {
        Log.Warning("T5: 生产环境 App:FrontendUrl={FrontendUrl} 非 HTTPS——密码重置/满意度/通知外发链接将以明文传输，" +
                    "存在被中间人截获令牌的风险；请配置 TLS 后在 nginx 启用 443 并将该值改为 https://。", frontendUrl);
    }
}

// 全局异常处理中间件（必须放在最前面）
app.UseMiddleware<NoneadCSS.Api.Middleware.GlobalExceptionMiddleware>();

// 转发头中间件（必须放在其他中间件之前，用于反向代理部署）
app.UseForwardedHeaders();

// 启用请求本地化中间件（必须在其他中间件之前）
var localizationOptions = app.Services.GetRequiredService<Microsoft.Extensions.Options.IOptions<RequestLocalizationOptions>>().Value;
app.UseRequestLocalization(localizationOptions);

using (var scope = app.Services.CreateScope())
{
    var context = scope.ServiceProvider.GetRequiredService<NoneadCSSDbContext>();
    var migrationLogger = scope.ServiceProvider.GetRequiredService<ILoggerFactory>().CreateLogger("DbMigrationSafety");

    try
    {
        await FixMigrationHistoryAsync(context);
    }
    catch (Exception ex)
    {
        Log.Warning(ex, "修复迁移历史记录失败，继续启动");
    }

    // 迁移前加固：SQLite 单文件库走代码迁移管道时，先冷备（保留最近 10 份 + SHA256 旁录）。
    // 冷备使用 SqliteConnection.BackupDatabase 分页拷贝（文件级复制可能遇到 "database is locked" / 半页损坏）。
    var backupPath = await SqliteMigrationSafety.BackupBeforeMigrateAsync(context, migrationLogger);
    if (!string.IsNullOrEmpty(backupPath))
    {
        Log.Information("迁移前冷备已生成: {BackupPath}；后续若迁移异常可从此文件原地还原", backupPath);
    }

    try
    {
        // 空库（全新克隆 / 首次部署）走「按当前模型建库 + 标记历史迁移」的路径：
        // 本仓库的历史迁移链在空库上不可重放（no such index / duplicate column 等），
        // 直接 Migrate 会让 API 在 Kestrel 监听前退出（前端表现为登录页「获取验证码失败」）。
        var initializedFreshDatabase = await FreshDatabaseBootstrap.TryInitializeFreshDatabaseAsync(context, migrationLogger);
        if (initializedFreshDatabase)
        {
            Log.Information("空数据库已按当前 EF 模型初始化完成");
        }
        else
        {
            await context.Database.MigrateAsync();
            Log.Information("数据库迁移完成");
        }
    }
    catch (Exception ex)
    {
        // M-01 生产环境下数据库迁移失败属于致命问题，不能继续启动
        // （schema 不一致会导致后续请求崩溃、数据写入异常）。
        // 仅开发环境允许继续，便于本地调试时手动修复迁移。
        if (!app.Environment.IsDevelopment())
        {
            Log.Fatal(ex, "数据库迁移失败，生产环境终止启动；最近一次冷备: {BackupPath}", backupPath);
            throw;
        }
        Log.Warning(ex, "数据库迁移失败，开发环境继续启动");
    }

    // 迁移后自校验：核对本次补丁核心目标 — WorkflowExecutionLogs.ConversationId 已可空
    var check = await SqliteMigrationSafety.CheckWorkflowExecutionLogConversationIdNullableAsync(context, migrationLogger);
    if (check.Checked && check.Nullable != true)
    {
        if (!app.Environment.IsDevelopment())
        {
            throw new InvalidOperationException(
                $"迁移后自校验未通过：WorkflowExecutionLogs.ConversationId 仍为 NOT NULL（{check.Reason}）。" +
                $"冷备文件: {backupPath}。请在空库验证脚本或确认是否缺少最新迁移装配。");
        }
        Log.Error("迁移后自校验未通过（开发环境继续启动）：ConversationId 仍为 NOT NULL（{Reason}）。冷备: {BackupPath}", check.Reason, backupPath);
    }
    else if (check.Checked)
    {
        Log.Information("迁移后自校验通过：WorkflowExecutionLogs.ConversationId 已为 NULLABLE（{Reason}）", check.Reason);
    }
    else
    {
        Log.Debug("迁移后自校验：当前 Provider 跳过 NULL 性核对（{Reason}）", check.Reason);
    }

    // 补丁性索引创建：PasswordResetToken 表的 Token/UserId/ExpiresAt 索引
    // 由于迁移系统与模型存在差异，无法通过 dotnet ef migrations add 干净地添加索引
    // 在此通过幂等 SQL 检查并创建，失败不影响启动
    try
    {
        await EnsurePasswordResetTokenIndexesAsync(context);
    }
    catch (Exception ex)
    {
        Log.Warning(ex, "PasswordResetToken 索引创建失败，继续启动");
    }

    try
    {
        await EnsureFailedLoginColumnsAsync(context);
    }
    catch (Exception ex)
    {
        Log.Warning(ex, "Users 登录失败列添加失败，继续启动");
    }

    try
    {
        await EnsureOutlookCredentialsColumnsAsync(context);
    }
    catch (Exception ex)
    {
        Log.Warning(ex, "OutlookSyncSetting 凭据列添加失败，继续启动");
    }

    try
    {
        await EnsureCalendarEventsAttendeeColumnsAsync(context);
    }
    catch (Exception ex)
    {
        Log.Warning(ex, "CalendarEvents 参会人列添加失败，继续启动");
    }

    try
    {
        await EnsureKnowledgeArticleLikesTableAsync(context);
    }
    catch (Exception ex)
    {
        Log.Warning(ex, "KnowledgeArticleLikes 点赞表补齐失败，继续启动");
    }

    var env = scope.ServiceProvider.GetRequiredService<IWebHostEnvironment>();
    var seederLogger = scope.ServiceProvider.GetRequiredService<ILoggerFactory>().CreateLogger("DatabaseSeeder");
    await DatabaseSeeder.SeedAsync(context, env, seederLogger);
}

// 修复迁移历史记录表与实际迁移文件不一致的问题。
// 当迁移文件被删除或重命名，导致 __EFMigrationsHistory 与程序集中的迁移不匹配时，
// MigrateAsync 可能会失败（例如尝试创建已存在的表）。
//
// 安全策略（基于时间戳排序）：
// 迁移 ID 格式为 "YYYYMMDDHHMMSS_Name"，按字符串排序即为时间顺序。
// 仅当一个缺失迁移的时间戳 <= 最后已应用迁移的时间戳时，才标记为已应用
// （因为数据库已应用了更新的迁移，schema 必然包含此迁移的变更）。
// 对于时间戳晚于最后已应用迁移的缺失迁移，不标记，交由 MigrateAsync 正常应用。
// 这避免了"盲目标记导致 schema 不一致"的严重问题（如缺少列导致启动崩溃）。
async Task FixMigrationHistoryAsync(NoneadCSSDbContext context)
{
    var connection = context.Database.GetDbConnection();

    if (connection.State != System.Data.ConnectionState.Open)
    {
        await connection.OpenAsync();
    }

    await using var command = connection.CreateCommand();
    command.CommandText = "SELECT COUNT(*) FROM sqlite_master WHERE type='table' AND name='Customers'";
    var customersTableExists = (long)(await command.ExecuteScalarAsync() ?? 0) > 0;
    if (!customersTableExists)
    {
        return;
    }

    command.CommandText = "SELECT COUNT(*) FROM sqlite_master WHERE type='table' AND name='__EFMigrationsHistory'";
    var historyTableExists = (long)(await command.ExecuteScalarAsync() ?? 0) > 0;
    if (!historyTableExists)
    {
        return;
    }

    var appliedMigrations = new HashSet<string>();
    command.CommandText = "SELECT \"MigrationId\" FROM \"__EFMigrationsHistory\"";
    await using var reader = await command.ExecuteReaderAsync();
    while (await reader.ReadAsync())
    {
        appliedMigrations.Add(reader.GetString(0));
    }
    await reader.CloseAsync();

    var allMigrations = context.Database.GetMigrations().ToList();
    var missingFromHistory = allMigrations.Where(m => !appliedMigrations.Contains(m)).ToList();

    if (missingFromHistory.Count == 0)
    {
        return;
    }

    // 获取已应用迁移中时间戳最大的（包括已从程序集删除的历史记录）
    // 这些"孤儿"记录仍代表数据库已达到的 schema 状态
    var lastAppliedMigrationId = appliedMigrations
        .OrderByDescending(m => m, StringComparer.Ordinal)
        .FirstOrDefault();

    command.CommandText = "SELECT \"ProductVersion\" FROM \"__EFMigrationsHistory\" LIMIT 1";
    var productVersionObj = await command.ExecuteScalarAsync();
    var productVersion = productVersionObj?.ToString() ?? "10.0.0";

    var markedCount = 0;
    var skippedCount = 0;
    foreach (var migrationId in missingFromHistory)
    {
        // 1) 优先走历史时间戳判定：<= 最后已应用迁移的，直接视为已应用（schema 已含在后续迁移里）
        var safeByTimestamp = lastAppliedMigrationId == null ||
                             string.Compare(migrationId, lastAppliedMigrationId, StringComparison.Ordinal) <= 0;

        // 2) 迁移特征 schema 探测：即使时间戳晚于 lastApplied，只要"该迁移要改的 schema 已经存在"，
        //    就说明该迁移是因历史原因被直接应用/漏入快照，此时补一条历史记录即可，不能再让 MigrateAsync 重跑 DDL。
        //    典型：现场库 Workflows 表已有 IsSystem 列，但 AddIsSystemToWorkflow 不在快照里
        //    → MigrateAsync 再跑 ALTER TABLE ADD "IsSystem" → SQLite Error 1 duplicate column name: IsSystem。
        var schemaProbeMatched = await IsSchemaSignatureMatchedForMigrationAsync(command, migrationId);

        // 3) 关键安全修复：如果迁移名包含「可被特征探测识别的关键词」（即命名为 AddXxxEntity / AddXxxColumn /
        //    FixXxxIndex 等特征化迁移），但 schemaProbeMatched=false，这意味着库中确实缺少该迁移的 DDL 产物。
        //    此时必须忽略 safeByTimestamp（基于时间戳的盲目信任），交给 MigrateAsync 正常应用，避免：
        //      - AddOutlookSyncEntities 被 safeByTimestamp 标记为"已应用"（但表根本不存在）
        //      - 后续 AddOutlookDeltaTokenAndRetryCounters 执行 ALTER TABLE 不存在的表 → SQLite Error 1
        var isNamedSchemaMigration = IsNamedSchemaMigration(migrationId);
        var ignoreSafeByTimestamp = isNamedSchemaMigration && !schemaProbeMatched;

        var isSafeToMark = (!ignoreSafeByTimestamp && safeByTimestamp) || schemaProbeMatched;

        if (!isSafeToMark)
        {
            Log.Warning("迁移 {MigrationId} 的时间戳晚于最后已应用迁移 {LastApplied} 且未命中 schema 特征探测，不标记为已应用，将由 MigrateAsync 正常应用",
                migrationId, lastAppliedMigrationId);
            skippedCount++;
            continue;
        }

        command.CommandText = "INSERT INTO \"__EFMigrationsHistory\" (\"MigrationId\", \"ProductVersion\") VALUES (@id, @version)";
        command.Parameters.Clear();
        var idParam = command.CreateParameter();
        idParam.ParameterName = "@id";
        idParam.Value = migrationId;
        command.Parameters.Add(idParam);
        var verParam = command.CreateParameter();
        verParam.ParameterName = "@version";
        verParam.Value = productVersion;
        command.Parameters.Add(verParam);
        await command.ExecuteNonQueryAsync();
        if (schemaProbeMatched)
        {
            Log.Warning("迁移 {MigrationId} 命中 schema 特征探测（该迁移的表/列/索引已存在），直接补入历史表，避免 MigrateAsync 重跑 DDL 导致 duplicate column / table", migrationId);
        }
        else
        {
            Log.Warning("已验证 schema 时序一致，将缺失的迁移记录补入历史表: {MigrationId}", migrationId);
        }
        markedCount++;
    }

    if (markedCount > 0 || skippedCount > 0)
    {
        Log.Information("迁移历史修复完成：标记 {Marked} 项，跳过 {Skipped} 项（待 MigrateAsync 应用）",
            markedCount, skippedCount);
    }
}

// 迁移 ID → schema 特征探测：返回 true 表示"该迁移对应的 DDL 变更已经在库里实际存在"。
// 所有探测都使用 sqlite_master / PRAGMA table_info 的只读查询，不写 schema。
async Task<bool> IsSchemaSignatureMatchedForMigrationAsync(System.Data.Common.DbCommand command, string migrationId)
{
    // Workflow IsSystem 列：现场常见漏快照迁移
    if (migrationId.Contains("AddIsSystemToWorkflow", StringComparison.Ordinal) ||
        migrationId.Contains("IsSystem", StringComparison.Ordinal))
    {
        return await ColumnExistsAsync(command, "Workflows", "IsSystem");
    }
    // PasswordResetToken 表 + 索引（迁移 AddPasswordResetTokenIndexes 等）
    if (migrationId.Contains("PasswordResetToken", StringComparison.Ordinal))
    {
        var hasTable = await TableExistsAsync(command, "PasswordResetTokens");
        if (!hasTable) return false;
        // 该迁移主语义是"建表 + 建索引"，只要表已存在就视为已应用过对应批次（索引缺失不影响 DDL 幂等，后续 EnsurePasswordResetTokenIndexesAsync 会兜底）
        return true;
    }
    // UserNotificationSettings 表
    if (migrationId.Contains("UserNotificationSettings", StringComparison.Ordinal))
    {
        return await TableExistsAsync(command, "UserNotificationSettings");
    }
    // AuditLog HMAC 签名列
    if (migrationId.Contains("AuditLogHmacSignature", StringComparison.Ordinal) ||
        migrationId.Contains("AuditLog", StringComparison.Ordinal))
    {
        return await ColumnExistsAsync(command, "AuditLogs", "HmacSignature") ||
               await ColumnExistsAsync(command, "AuditLogs", "Signature");
    }
    // FixUniqueIndexSoftDelete：任何"修复唯一索引 + 软删除"的迁移，用典型 Customers 索引名探测 + 对应列存在性双判定
    if (migrationId.Contains("FixUniqueIndexSoftDelete", StringComparison.Ordinal))
    {
        return await IndexExistsAsync(command, "IX_Customers_Email_Normalized") ||
               await IndexExistsAsync(command, "IX_Customers_Email");
    }
    // CalendarEventExtendedFields
    if (migrationId.Contains("CalendarEventExtendedFields", StringComparison.Ordinal))
    {
        return await ColumnExistsAsync(command, "CalendarEvents", "RecurrenceRule") ||
               await ColumnExistsAsync(command, "CalendarEvents", "IcalUid");
    }
    // AddOutlookSyncLogConflictFields：OutlookSyncLog 表新增 ConflictCount/ConflictDetailsJson 列
    if (migrationId.Contains("AddOutlookSyncLogConflictFields", StringComparison.Ordinal))
    {
        return await ColumnExistsAsync(command, "OutlookSyncLogs", "ConflictCount") &&
               await ColumnExistsAsync(command, "OutlookSyncLogs", "ConflictDetailsJson");
    }
    // OutlookSync 实体
    if (migrationId.Contains("OutlookSyncEntities", StringComparison.Ordinal) ||
        migrationId.Contains("Outlook", StringComparison.Ordinal))
    {
        return await TableExistsAsync(command, "OutlookOAuthTokens") ||
               await TableExistsAsync(command, "OutlookSyncStates") ||
               await TableExistsAsync(command, "OutlookSynchronizations");
    }
    // CalendarReminderEmails
    if (migrationId.Contains("CalendarReminderEmails", StringComparison.Ordinal))
    {
        return await ColumnExistsAsync(command, "CalendarReminders", "EmailSentAt") ||
               await TableExistsAsync(command, "CalendarReminderEmails") ||
               await ColumnExistsAsync(command, "CalendarReminders", "LastEmailAttemptAt");
    }
    // ExtendChatMessageFields
    if (migrationId.Contains("ExtendChatMessageFields", StringComparison.Ordinal))
    {
        return await ColumnExistsAsync(command, "ChatMessages", "ExternalProviderMessageId") ||
               await ColumnExistsAsync(command, "ChatMessages", "ExternalThreadId");
    }
    // CalendarEventRowVersion
    if (migrationId.Contains("CalendarEventRowVersion", StringComparison.Ordinal))
    {
        return await ColumnExistsAsync(command, "CalendarEvents", "RowVersion");
    }
    // AddTimeEntry
    if (migrationId.Contains("AddTimeEntry", StringComparison.Ordinal) || migrationId.Contains("TimeEntry", StringComparison.Ordinal))
    {
        return await TableExistsAsync(command, "TimeEntries");
    }
    // CustomReports
    if (migrationId.Contains("CustomReports", StringComparison.Ordinal))
    {
        return await TableExistsAsync(command, "CustomReports") ||
               await TableExistsAsync(command, "CustomReportSchedules");
    }
    // AddWeChatAndModuleThreadModelChanges
    if (migrationId.Contains("AddWeChatAndModuleThreadModelChanges", StringComparison.Ordinal) ||
        migrationId.Contains("WeChat", StringComparison.Ordinal) || migrationId.Contains("ModuleThread", StringComparison.Ordinal))
    {
        return await TableExistsAsync(command, "WeChatAccounts") ||
               await TableExistsAsync(command, "ModuleThreads") ||
               await TableExistsAsync(command, "WeChatMessages");
    }
    // AddConversationReadState
    if (migrationId.Contains("AddConversationReadState", StringComparison.Ordinal))
    {
        return await ColumnExistsAsync(command, "Conversations", "LastReadByAgentAt") ||
               await ColumnExistsAsync(command, "Conversations", "LastReadByCustomerAt") ||
               await TableExistsAsync(command, "ConversationReadStates");
    }
    // AddCustomerContactExtractAttempts
    if (migrationId.Contains("AddCustomerContactExtractAttempts", StringComparison.Ordinal))
    {
        return await TableExistsAsync(command, "CustomerContactExtractAttempts");
    }
    // AddImMessageLog
    if (migrationId.Contains("AddImMessageLog", StringComparison.Ordinal) || migrationId.Contains("ImMessage", StringComparison.Ordinal))
    {
        return await TableExistsAsync(command, "ImMessageLogs");
    }
    // AddDistributedLockTable
    if (migrationId.Contains("AddDistributedLockTable", StringComparison.Ordinal) || migrationId.Contains("DistributedLock", StringComparison.Ordinal))
    {
        return await TableExistsAsync(command, "DistributedLocks");
    }
    // AddCustomerPhoneToChatSession
    if (migrationId.Contains("AddCustomerPhoneToChatSession", StringComparison.Ordinal))
    {
        return await ColumnExistsAsync(command, "ChatSessions", "CustomerPhone") ||
               await ColumnExistsAsync(command, "ChatSessions", "CustomerPhoneNormalized");
    }
    // AddChatSessionRatingRequestedAtAndTimeoutAt
    if (migrationId.Contains("AddChatSessionRatingRequestedAtAndTimeoutAt", StringComparison.Ordinal) || migrationId.Contains("ChatSessionRating", StringComparison.Ordinal))
    {
        return await ColumnExistsAsync(command, "ChatSessions", "RatingRequestedAt") ||
               await ColumnExistsAsync(command, "ChatSessions", "RatingTimeoutAt");
    }
    // WorkflowLogConversationIdNullable（本次新增）
    if (migrationId.Contains("WorkflowLogConversationIdNullable", StringComparison.Ordinal) ||
        migrationId.Contains("WorkflowExecutionLog", StringComparison.Ordinal))
    {
        // WorkflowExecutionLogs.ConversationId 当前是否允许 NULL → pragma table_info 查 notnull=0
        return await ColumnIsNullableAsync(command, "WorkflowExecutionLogs", "ConversationId");
    }
    // AddMailboxVersion：Mailboxes.Version 列已存在说明该迁移已应用过（可能由历史快照或手动 ALTER 添加）
    if (migrationId.Contains("AddMailboxVersion", StringComparison.Ordinal) ||
        migrationId.Contains("MailboxVersion", StringComparison.Ordinal))
    {
        return await ColumnExistsAsync(command, "Mailboxes", "Version");
    }

    // AddCalendarEventExtendedFields：CalendarEvents 7 个新列任一已存在即视为已应用
    // （可能由手动 ALTER 或快照恢复已添加，跳过 DDL 避免 duplicate column 错误）
    if (migrationId.Contains("AddCalendarEventExtendedFields", StringComparison.Ordinal) ||
        migrationId.Contains("CalendarEventExtendedFields", StringComparison.Ordinal))
    {
        if (await ColumnExistsAsync(command, "CalendarEvents", "HostUserIdsJson")) return true;
        if (await ColumnExistsAsync(command, "CalendarEvents", "AgendaIdsJson")) return true;
        if (await ColumnExistsAsync(command, "CalendarEvents", "TaskOwnerId")) return true;
        if (await ColumnExistsAsync(command, "CalendarEvents", "DueAt")) return true;
        if (await ColumnExistsAsync(command, "CalendarEvents", "ReminderType")) return true;
        if (await ColumnExistsAsync(command, "CalendarEvents", "WeekdaysSelectedJson")) return true;
        if (await ColumnExistsAsync(command, "CalendarEvents", "RecurrenceRule")) return true;
    }

    return false;
}

// 判断一个迁移 ID 是否为「命名特征化迁移」——即迁移命名中包含具体 DDL 动作 + 目标实体的组合，
// 这类迁移通常能被 IsSchemaSignatureMatchedForMigrationAsync 识别。如果该判定为 true 但特征探测
// 未命中，则 safeByTimestamp 不能作为兜底，必须交给 MigrateAsync 实际应用 DDL，
// 否则会出现「迁移被标记为已应用但实际表/列不存在」的严重不一致，导致后续迁移 ALTER TABLE 失败。
static bool IsNamedSchemaMigration(string migrationId)
{
    var keywords = new[]
    {
        // 与 IsSchemaSignatureMatchedForMigrationAsync 中覆盖的关键字一一对应
        "IsSystem", "PasswordResetToken", "UserNotificationSettings",
        "AuditLog", "AuditLogHmacSignature", "FixUniqueIndexSoftDelete",
        "CalendarEventExtendedFields", "AddOutlookSyncLogConflictFields",
        "OutlookSyncEntities", "Outlook", "CalendarReminderEmails",
        "ExtendChatMessageFields", "CalendarEventRowVersion",
        "AddTimeEntry", "TimeEntry", "CustomReports",
        "AddWeChatAndModuleThreadModelChanges", "WeChat", "ModuleThread",
        "AddConversationReadState", "AddCustomerContactExtractAttempts",
        "AddImMessageLog", "ImMessage", "AddDistributedLockTable", "DistributedLock",
        "AddCustomerPhoneToChatSession", "AddChatSessionRatingRequestedAtAndTimeoutAt", "ChatSessionRating",
        "WorkflowLogConversationIdNullable", "WorkflowExecutionLog",
        "AddMailboxVersion", "MailboxVersion",
        // 额外覆盖本次问题场景中会出错的几个重要迁移
        "KnowledgeArticleLikes", "KnowledgeArticlesFts", "Fts5",
        "CustomFieldSourceTypes",
        "AddKnowledgeArticle", "OutlookSyncSettings", "OutlookSyncLog",
        "AddOutlookDeltaToken", "RetryCounters", "OutlookSyncSettingRowVersion",
        // 常见 DDL 前缀/动作名
        "AddWorkflow", "FixConversation", "AddConversation",
        "CreateWorkflow", "DropWorkflow", "FixIndex"
    };
    foreach (var kw in keywords)
    {
        if (migrationId.Contains(kw, StringComparison.OrdinalIgnoreCase))
            return true;
    }
    return false;
}

async Task<bool> TableExistsAsync(System.Data.Common.DbCommand command, string tableName)
{
    command.CommandText = "SELECT COUNT(*) FROM sqlite_master WHERE type='table' AND name=@name COLLATE NOCASE";
    command.Parameters.Clear();
    var p = command.CreateParameter(); p.ParameterName = "@name"; p.Value = tableName;
    command.Parameters.Add(p);
    return (long)(await command.ExecuteScalarAsync() ?? 0) > 0;
}

async Task<bool> IndexExistsAsync(System.Data.Common.DbCommand command, string indexName)
{
    command.CommandText = "SELECT COUNT(*) FROM sqlite_master WHERE type='index' AND name=@name COLLATE NOCASE";
    command.Parameters.Clear();
    var p = command.CreateParameter(); p.ParameterName = "@name"; p.Value = indexName;
    command.Parameters.Add(p);
    return (long)(await command.ExecuteScalarAsync() ?? 0) > 0;
}

async Task<bool> ColumnExistsAsync(System.Data.Common.DbCommand command, string tableName, string columnName)
{
    command.CommandText = $"PRAGMA table_info(\"{tableName.Replace("\"", "\"\"")}\")";
    command.Parameters.Clear();
    var columns = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
    await using var reader = await command.ExecuteReaderAsync();
    while (await reader.ReadAsync())
    {
        // PRAGMA table_info: cid,name,type,notnull,dflt_value,pk
        var colName = reader.GetString(1);
        columns.Add(colName);
    }
    await reader.CloseAsync();
    return columns.Contains(columnName);
}

async Task<bool> ColumnIsNullableAsync(System.Data.Common.DbCommand command, string tableName, string columnName)
{
    command.CommandText = $"PRAGMA table_info(\"{tableName.Replace("\"", "\"\"")}\")";
    command.Parameters.Clear();
    await using var reader = await command.ExecuteReaderAsync();
    while (await reader.ReadAsync())
    {
        var colName = reader.GetString(1);
        if (string.Equals(colName, columnName, StringComparison.OrdinalIgnoreCase))
        {
            // notnull = 1 说明 NOT NULL；0 说明可空（同时需要结合 EF 模型判断默认值约束，但此处用于匹配"迁移已经跑过"的判定已经足够）
            var notNull = reader.GetInt32(3);
            await reader.CloseAsync();
            return notNull == 0;
        }
    }
    await reader.CloseAsync();
    return false;
}

// 为 PasswordResetToken 表创建索引（如果不存在）。索引用于密码重置热路径查询、用户清理与过期清理。
async Task EnsurePasswordResetTokenIndexesAsync(NoneadCSSDbContext context)
{
    var connection = context.Database.GetDbConnection();

    // 确保连接已打开（DbContext 在 DbCommand 执行时可能未显式打开连接）
    if (connection.State != System.Data.ConnectionState.Open)
    {
        await connection.OpenAsync();
    }

    await using var command = connection.CreateCommand();

    // SQLite 不支持 IF NOT EXISTS 语法创建索引，需要先查询 sqlite_master 判断
    var indexesToCreate = new[]
    {
        ("IX_PasswordResetTokens_Token", "CREATE UNIQUE INDEX \"IX_PasswordResetTokens_Token\" ON \"PasswordResetTokens\" (\"Token\")"),
        ("IX_PasswordResetTokens_UserId", "CREATE INDEX \"IX_PasswordResetTokens_UserId\" ON \"PasswordResetTokens\" (\"UserId\")"),
        ("IX_PasswordResetTokens_ExpiresAt", "CREATE INDEX \"IX_PasswordResetTokens_ExpiresAt\" ON \"PasswordResetTokens\" (\"ExpiresAt\")")
    };

    foreach (var (indexName, createSql) in indexesToCreate)
    {
        command.CommandText = "SELECT COUNT(*) FROM sqlite_master WHERE type='index' AND name=@name";
        command.Parameters.Clear();
        var param = command.CreateParameter();
        param.ParameterName = "@name";
        param.Value = indexName;
        command.Parameters.Add(param);
        var exists = (long)(await command.ExecuteScalarAsync() ?? 0) > 0;
        if (!exists)
        {
            command.CommandText = createSql;
            command.Parameters.Clear();
            await command.ExecuteNonQueryAsync();
            Log.Information("已创建索引 {IndexName}", indexName);
        }
    }
}

// 为 Users 表添加登录失败锁定相关列（FailedLoginAttempts 和 LockoutEnd）。
// 由于迁移系统与模型存在差异，通过幂等 SQL 检查并添加，失败不影响启动。
async Task EnsureFailedLoginColumnsAsync(NoneadCSSDbContext context)
{
    var connection = context.Database.GetDbConnection();

    if (connection.State != System.Data.ConnectionState.Open)
    {
        await connection.OpenAsync();
    }

    await using var command = connection.CreateCommand();

    var columnsToAdd = new[]
    {
        ("FailedLoginAttempts", "INTEGER NOT NULL DEFAULT 0"),
        ("LockoutEnd", "TEXT NULL")
    };

    foreach (var (columnName, columnDef) in columnsToAdd)
    {
        command.CommandText = "SELECT COUNT(*) FROM pragma_table_info('Users') WHERE name = @name";
        command.Parameters.Clear();
        var param = command.CreateParameter();
        param.ParameterName = "@name";
        param.Value = columnName;
        command.Parameters.Add(param);
        var exists = (long)(await command.ExecuteScalarAsync() ?? 0) > 0;
        if (!exists)
        {
            command.CommandText = $"ALTER TABLE \"Users\" ADD COLUMN \"{columnName}\" {columnDef}";
            command.Parameters.Clear();
            await command.ExecuteNonQueryAsync();
            Log.Information("已添加 Users 表列 {ColumnName}", columnName);
        }
    }
}

// T000099: 为 OutlookSyncSettings 表添加用户自配置 Azure AD 凭据列。
// 表名为复数（EF Core 默认 DbSet 属性名约定），使用复数避免 "no such table" 错误。
// 通过幂等 SQL 检查并添加，失败不影响启动。
async Task EnsureOutlookCredentialsColumnsAsync(NoneadCSSDbContext context)
{
    var connection = context.Database.GetDbConnection();

    if (connection.State != System.Data.ConnectionState.Open)
    {
        await connection.OpenAsync();
    }

    await using var command = connection.CreateCommand();

    // T000099: 用户级 Azure AD 凭据字段（空值回退到 appsettings 基线）
    var columnsToAdd = new[]
    {
        ("ClientId", "TEXT NULL"),
        ("ClientSecret", "TEXT NULL"),
        ("TenantId", "TEXT NULL"),
        ("RedirectUri", "TEXT NULL"),
        ("Scopes", "TEXT NULL")
    };

    foreach (var (columnName, columnDef) in columnsToAdd)
    {
        command.CommandText = "SELECT COUNT(*) FROM pragma_table_info('OutlookSyncSettings') WHERE name = @name";
        command.Parameters.Clear();
        var param = command.CreateParameter();
        param.ParameterName = "@name";
        param.Value = columnName;
        command.Parameters.Add(param);
        var exists = (long)(await command.ExecuteScalarAsync() ?? 0) > 0;
        if (!exists)
        {
            command.CommandText = $"ALTER TABLE \"OutlookSyncSettings\" ADD COLUMN \"{columnName}\" {columnDef}";
            command.Parameters.Clear();
            await command.ExecuteNonQueryAsync();
            Log.Information("已添加 OutlookSyncSettings 表列 {ColumnName}", columnName);
        }
    }
}

// T000100: CalendarEvents 表的参会人/提醒相关列若在历史库中缺失（典型：DB 先建，后加迁移但 MigrateAsync
// 因 __EFMigrationsHistory 对不上被跳过），会导致写库时报 "no such column" 500 且数据为空。
// 用幂等 pragma_table_info 探测并按需 ADD COLUMN；任何异常吞掉保证启动不阻塞。
async Task EnsureCalendarEventsAttendeeColumnsAsync(NoneadCSSDbContext context)
{
    var connection = context.Database.GetDbConnection();
    if (connection.State != System.Data.ConnectionState.Open)
    {
        await connection.OpenAsync();
    }

    await using var command = connection.CreateCommand();

    // 先确认表存在（避免全新库/未 seed 的场景无谓报错）
    command.CommandText = "SELECT COUNT(*) FROM sqlite_master WHERE type='table' AND name='CalendarEvents'";
    var tableExists = (long)(await command.ExecuteScalarAsync() ?? 0) > 0;
    if (!tableExists) return;

    var columnsToAdd = new[]
    {
        ("ReminderEmails", "TEXT NULL"),
        ("AttendeesJson", "TEXT NULL"),
        ("HostUserIdsJson", "TEXT NULL"),
        ("AgendaIdsJson", "TEXT NULL"),
        ("WeekdaysSelectedJson", "TEXT NULL"),
        ("RecurrenceRule", "TEXT NULL"),
        ("SubTasksJson", "TEXT NULL"),
        ("RecurrenceExclusionsJson", "TEXT NULL"),
        ("DueAt", "TEXT NULL"),
        ("ReminderType", "TEXT NULL"),
        ("Location", "TEXT NULL"),
        ("MeetingUrl", "TEXT NULL"),
        ("Priority", "INTEGER NULL"),
        ("Progress", "INTEGER NULL"),
        ("RowVersion", "BLOB NULL")
    };

    foreach (var (columnName, columnDef) in columnsToAdd)
    {
        command.CommandText = "SELECT COUNT(*) FROM pragma_table_info('CalendarEvents') WHERE name = @name";
        command.Parameters.Clear();
        var param = command.CreateParameter();
        param.ParameterName = "@name";
        param.Value = columnName;
        command.Parameters.Add(param);
        var exists = (long)(await command.ExecuteScalarAsync() ?? 0) > 0;
        if (!exists)
        {
            command.CommandText = $"ALTER TABLE \"CalendarEvents\" ADD COLUMN \"{columnName}\" {columnDef}";
            command.Parameters.Clear();
            await command.ExecuteNonQueryAsync();
            Log.Information("已添加 CalendarEvents 表列 {ColumnName}", columnName);
        }
    }
}

// T000101: KnowledgeArticleLikes 点赞去重表若在历史库中缺失（迁移 20260826160000_AddKnowledgeArticleLikes 已存在但
// MigrateAsync 因 __EFMigrationsHistory 对不上被跳过），会导致点赞接口 INSERT OR IGNORE 时报
// SQLite Error 1 no such table: KnowledgeArticleLikes，前端显示“服务器内部错误”。
// 按迁移原始 DDL 用 CREATE TABLE IF NOT EXISTS 幂等补齐，同时创建反向索引。
async Task EnsureKnowledgeArticleLikesTableAsync(NoneadCSSDbContext context)
{
    var connection = context.Database.GetDbConnection();
    if (connection.State != System.Data.ConnectionState.Open)
    {
        await connection.OpenAsync();
    }

    await using var command = connection.CreateCommand();

    // 主表：复合主键 (ArticleId, UserFingerprint) 保证唯一性；外键级联删除避免孤儿点赞记录
    command.CommandText = @"
CREATE TABLE IF NOT EXISTS KnowledgeArticleLikes (
    ArticleId INTEGER NOT NULL,
    UserFingerprint TEXT NOT NULL,
    CreatedAt TEXT NOT NULL DEFAULT CURRENT_TIMESTAMP,
    PRIMARY KEY (ArticleId, UserFingerprint),
    FOREIGN KEY (ArticleId) REFERENCES KnowledgeArticles(Id) ON DELETE CASCADE
);";
    command.Parameters.Clear();
    await command.ExecuteNonQueryAsync();

    // 反向索引：支持按 UserFingerprint 查询用户点赞过的文章列表
    command.CommandText = @"
CREATE INDEX IF NOT EXISTS IX_KnowledgeArticleLikes_UserFingerprint
ON KnowledgeArticleLikes(UserFingerprint);";
    command.Parameters.Clear();
    await command.ExecuteNonQueryAsync();

    Log.Information("KnowledgeArticleLikes 点赞表已校验/补齐完成");
}

// H-03：SecurityHeadersMiddleware 必须位于 UseDefaultFiles / UseStaticFiles 之前。
// 原因：静态文件中间件短路后直接写入 response，在此之后注册的中间件无法包装响应流
// 以进行 __CSP_NONCE__ 占位符替换。先注册本中间件 → 先替换 Body 为 MemoryStream →
// 下游中间件写入 buffer → 最后在 InvokeAsync 尾部替换 nonce 并写回原始流。
app.UseMiddleware<NoneadCSS.Api.Middleware.SecurityHeadersMiddleware>();

// 启用默认文档（/ → index.html） + 静态文件服务（用于 SPA、Favicon、Scalar 自定义 CSS）
// 注意：必须在 MapScalarApiReference 之前，确保静态文件请求不被 Scalar 拦截
app.UseDefaultFiles(new DefaultFilesOptions
{
    DefaultFileNames = new List<string> { "index.html" }
});
app.UseStaticFiles();

// T000102：API 文档（Scalar /docs + OpenAPI /openapi/*）在全部环境注册，
// 不再限定 Development —— 生产环境（部署域名）也必须能查看 API 文档。
// Scalar 页面与内嵌资源（/scalar/*）均由 Kestrel 提供服务，Nginx 需反代 docs/openapi/scalar 前缀。
app.UseMiddleware<NoneadCSS.Api.Middleware.ScalarCustomizationMiddleware>();

app.MapOpenApi();

app.MapScalarApiReference("/docs", options =>
{
    options
        .WithTitle("NoneadCSS API 文档")
        .WithTheme(ScalarTheme.Purple)
        .WithFavicon("/favicon.svg")
        .WithCustomCss("/scalar-custom.css")
        .WithDarkMode(true)
        .WithDefaultHttpClient(ScalarTarget.CSharp, ScalarClient.HttpClient);
});

if (app.Environment.IsDevelopment())
{
    // 开发环境便捷入口：根路径重定向到 API 文档。
    // 生产环境 / 由 UseDefaultFiles 提供 SPA index.html（登录页），不做重定向。
    app.MapGet("/", () => Results.Redirect("/docs")).ExcludeFromDescription();
}

// HTTPS 强制：生产环境默认启用 HSTS（浏览器缓存 30 天，S9），可通过 Security:EnableHsts=false 关闭
// Docker 环境下容器内部使用 HTTP，HTTPS 由反向代理（Nginx等）处理，因此 HTTPS 重定向仍由 Security:EnableHttpsRedirection 控制
var enableHsts = builder.Configuration.GetValue<bool?>("Security:EnableHsts") ?? true;
if (enableHsts && !app.Environment.IsDevelopment())
{
    app.UseHsts();
}

var enableHttpsRedirection = builder.Configuration.GetValue<bool>("Security:EnableHttpsRedirection");
if (enableHttpsRedirection)
{
    app.UseHttpsRedirection();
}

app.UseCors("AllowedOrigins");

// RateLimiter 中间件置于 CORS 之后、认证之前，确保所有限制请求都能被计数
// （之前注册在管道深处，可能在认证之前就被绕过）
app.UseRateLimiter();

app.Use(async (context, next) =>
{
    await next();
    if (!context.Response.HasStarted
        && context.Response.ContentType != null
        && context.Response.ContentType.Contains("application/json")
        && !context.Response.ContentType.Contains("charset"))
    {
        context.Response.ContentType += "; charset=utf-8";
    }
});

app.UseAuthentication();
app.UseMiddleware<ApiKeyLoggingMiddleware>();
app.UseMiddleware<NoneadCSS.Api.Middleware.CsrfProtectionMiddleware>();
app.UseAuthorization();
app.UseOutputCache();

// 健康检查端点（无需认证，用于容器编排和监控系统探测）
// /health 为 liveness 探针：只返回最小化状态 JSON，避免暴露内部依赖详情/路径/SQL 片段
// /health/ready 为 readiness 探针：仅在非 Production 环境输出 entries 详情；
//                Production 环境只输出整体状态，防止内部配置泄漏。
app.MapHealthChecks("/health", new HealthCheckOptions
{
    Predicate = _ => false, // liveness：不执行任何检查，仅探测进程存活
    ResponseWriter = WriteLivenessResponseAsync
});
app.MapHealthChecks("/health/ready", new HealthCheckOptions
{
    Predicate = check => check.Tags.Contains("ready"), // readiness：仅检查带 "ready" 标签的依赖
    ResponseWriter = WriteReadinessResponseAsync
});

// 业务监控指标端点（Prometheus 0.0.4 文本格式）
// 访问控制：先匹配 Security:Metrics:AllowedIpRanges 白名单（Prometheus 内网抓取器用），
//           未命中则要求 SuperAdmin 角色鉴权（人类管理员诊断用）。
app.MapGet("/metrics", (
    HttpContext context,
    NoneadCSS.Application.Metrics.CalendarMetricsExporter exporter,
    Microsoft.Extensions.Options.IOptions<NoneadCSS.Application.Configuration.MetricsOptions> metricsOptions) =>
{
    var opts = metricsOptions.Value;
    var remoteIp = context.Connection.RemoteIpAddress;
    var superAdminRole = ((int)UserRoleType.SuperAdmin).ToString();

    static bool IsInCidrRange(System.Net.IPAddress ip, string cidr)
    {
        try
        {
            var parts = cidr.Split('/', 2, StringSplitOptions.TrimEntries);
            if (parts.Length != 2) return false;
            if (!System.Net.IPAddress.TryParse(parts[0], out var network)) return false;
            if (!int.TryParse(parts[1], out var prefixBits) || prefixBits < 0) return false;

            // 处理 IPv4 映射到 IPv6 的兼容：把 ::ffff:x.x.x.x 统一按对应族比较
            if (ip.AddressFamily != network.AddressFamily)
            {
                if (network.AddressFamily == System.Net.Sockets.AddressFamily.InterNetwork
                    && ip.IsIPv4MappedToIPv6)
                {
                    ip = ip.MapToIPv4();
                }
                else if (ip.AddressFamily == System.Net.Sockets.AddressFamily.InterNetwork
                         && network.IsIPv4MappedToIPv6)
                {
                    network = network.MapToIPv4();
                }
                else
                {
                    return false; // 不同地址族且无可映射关系，直接拒绝
                }
            }

            byte[] ipBytes = ip.GetAddressBytes();
            byte[] netBytes = network.GetAddressBytes();
            if (ipBytes.Length != netBytes.Length) return false;
            if (prefixBits > ipBytes.Length * 8) return false;

            int fullBytes = prefixBits / 8;
            int remainderBits = prefixBits % 8;
            for (int i = 0; i < fullBytes; i++)
            {
                if (ipBytes[i] != netBytes[i]) return false;
            }
            if (remainderBits > 0)
            {
                int mask = byte.MaxValue << (8 - remainderBits) & 0xFF;
                if ((ipBytes[fullBytes] & mask) != (netBytes[fullBytes] & mask)) return false;
            }
            return true;
        }
        catch
        {
            return false;
        }
    }

    bool ipAllowed = false;
    if (remoteIp != null && opts.AllowedIpRanges is { Count: > 0 })
    {
        // 当 TrustXForwardedFor = true 时，优先使用 X-Forwarded-For 最左侧真实 IP；
        // 同时必须依赖 ForwardedHeaders 中间件（Program 开头已启用）来防止伪造。
        var candidateIp = remoteIp;
        if (opts.TrustXForwardedFor
            && context.Request.Headers.TryGetValue("X-Forwarded-For", out var xff)
            && !string.IsNullOrWhiteSpace(xff))
        {
            var firstIp = xff.ToString()!.Split(',', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries)
                .FirstOrDefault();
            if (System.Net.IPAddress.TryParse(firstIp, out var parsedRealIp))
                candidateIp = parsedRealIp;
        }

        foreach (var range in opts.AllowedIpRanges)
        {
            if (string.IsNullOrWhiteSpace(range)) continue;
            // 支持单 IP（不带 /prefix）：构造为 /32 或 /128
            if (range.Contains('/'))
            {
                if (IsInCidrRange(candidateIp, range)) { ipAllowed = true; break; }
            }
            else
            {
                if (System.Net.IPAddress.TryParse(range, out var exactIp) && System.Net.IPAddress.Equals(exactIp, candidateIp))
                { ipAllowed = true; break; }
            }
        }
    }

    if (!ipAllowed && opts.RequireSuperAdmin)
    {
        var user = context.User;
        if (!user.Identity?.IsAuthenticated ?? true)
        {
            return Results.Unauthorized();
        }
        if (!user.IsInRole(superAdminRole))
        {
            return Results.Forbid();
        }
    }

    var body = exporter.GetPrometheusText();
    return Results.Text(body, "text/plain; version=0.0.4; charset=utf-8", Encoding.UTF8);
}).ExcludeFromDescription();

app.MapControllers();

// SPA fallback：未匹配到任何 Controller/Hub 的路由（例如前端路由 /calendar、/knowledge）
// 统一返回 wwwroot/index.html，交给客户端 React Router 处理。
// 注意：放在 MapControllers / MapHub 之后，避免拦截真实 API/Hubs 请求；
// 同时排除以 /api/、/hubs/、/attachments/ 开头的路径，防止误返回 index.html。
app.MapFallback(context =>
{
    var path = context.Request.Path.Value ?? "/";
    if (path.StartsWith("/api/", StringComparison.OrdinalIgnoreCase) ||
        path.StartsWith("/hubs/", StringComparison.OrdinalIgnoreCase) ||
        path.StartsWith("/attachments/", StringComparison.OrdinalIgnoreCase) ||
        path.StartsWith("/health", StringComparison.OrdinalIgnoreCase) ||
        path.StartsWith("/metrics", StringComparison.OrdinalIgnoreCase))
    {
        context.Response.StatusCode = StatusCodes.Status404NotFound;
        return Task.CompletedTask;
    }
    context.Response.ContentType = "text/html; charset=utf-8";
    return context.Response.SendFileAsync(Path.Combine(app.Environment.WebRootPath, "index.html"));
}).ExcludeFromDescription();

// SignalR Hub 端点（使用 SignalR 专用 CORS 策略，支持 AllowCredentials）
app.MapHub<NoneadCSS.Infrastructure.Services.NotificationHub>("/hubs/notifications")
    .RequireCors("SignalR");

// 聊天专用 Hub（面向客户侧，基于 sessionToken 鉴权，无需登录）
app.MapHub<NoneadCSS.Infrastructure.Services.ChatHub>("/hubs/chat")
    .RequireCors("SignalR");

try
{
    Log.Information("应用程序启动中...");
    app.Run();
}
catch (Exception ex)
{
    Log.Fatal(ex, "应用程序启动失败");
}
finally
{
    Log.CloseAndFlush();
}

// M-04: Liveness 探针最小化输出 —— 不返回 entries/description/data，
//       防止内部依赖结构、SQL 片段、部署路径等通过匿名端点泄漏。
//       Liveness 语义仅需要"进程存活"，因此输出最简 JSON {status:...}。
static async Task WriteLivenessResponseAsync(HttpContext context, HealthReport report)
{
    context.Response.ContentType = "application/json; charset=utf-8";
    context.Response.StatusCode = report.Status == HealthStatus.Healthy
        ? StatusCodes.Status200OK
        : StatusCodes.Status503ServiceUnavailable;
    await context.Response.WriteAsJsonAsync(new { status = report.Status.ToString() });
}

// M-04: Readiness 分级披露：
//  - Production 环境：与 Liveness 相同，仅输出整体 status；
//  - 其他环境：输出 entries 详情，便于内部排查。
static async Task WriteReadinessResponseAsync(HttpContext context, HealthReport report)
{
    var env = context.RequestServices.GetRequiredService<IWebHostEnvironment>();
    context.Response.ContentType = "application/json; charset=utf-8";
    context.Response.StatusCode = report.Status == HealthStatus.Healthy
        ? StatusCodes.Status200OK
        : StatusCodes.Status503ServiceUnavailable;

    if (env.IsProduction())
    {
        await context.Response.WriteAsJsonAsync(new { status = report.Status.ToString() });
        return;
    }

    var entries = report.Entries.ToDictionary(
        e => e.Key,
        e => new
        {
            status = e.Value.Status.ToString(),
            description = e.Value.Description,
            durationMs = e.Value.Duration.TotalMilliseconds,
            tags = e.Value.Tags,
            data = e.Value.Data?.ToDictionary(kv => kv.Key, kv => kv.Value?.ToString())
        });

    await context.Response.WriteAsJsonAsync(new
    {
        status = report.Status.ToString(),
        totalDurationMs = report.TotalDuration.TotalMilliseconds,
        entries
    });
}
