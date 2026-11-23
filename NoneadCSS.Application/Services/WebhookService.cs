using System.Collections.Concurrent;
using System.Net;
using System.Net.Sockets;
using System.Text;
using System.Text.Json;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using NoneadCSS.Application.Configuration;
using NoneadCSS.Application.DTOs.Responses;
using NoneadCSS.Domain;
using NoneadCSS.Domain.Entities;
using NoneadCSS.Domain.Services;
using Microsoft.EntityFrameworkCore;

namespace NoneadCSS.Application.Services;

public class WebhookService : IWebhookService
{
    private static readonly ConcurrentDictionary<int, Lazy<HttpClient>> _httpClientCache = new();

    /// <summary>Webhook 日志请求体最大记录长度（超出截断，L-Q5：提取为常量便于统一调整）</summary>
    private const int WebhookLogRequestBodyMaxLength = 2000;
    /// <summary>Webhook 日志响应体最大记录长度（超出截断，L-Q5：提取为常量便于统一调整）</summary>
    private const int WebhookLogResponseBodyMaxLength = 1000;

    private readonly IUnitOfWork _unitOfWork;
    private readonly IEncryptionService _encryptionService;
    private readonly ICurrentUserAccessor _currentUserAccessor;
    private readonly IOptions<HttpClientOptions> _httpClientOptions;
    private readonly IConfiguration _configuration;
    private readonly ILogger<WebhookService> _logger;

    public WebhookService(
        IUnitOfWork unitOfWork,
        IEncryptionService encryptionService,
        ICurrentUserAccessor currentUserAccessor,
        IOptions<HttpClientOptions> httpClientOptions,
        IConfiguration configuration,
        ILogger<WebhookService> logger)
    {
        _unitOfWork = unitOfWork;
        _encryptionService = encryptionService;
        _currentUserAccessor = currentUserAccessor;
        _httpClientOptions = httpClientOptions;
        _configuration = configuration;
        _logger = logger;
    }

    /// <summary>
    /// T000105：是否允许 Webhook 指向内网/私网/回环地址（SSRF 防护开关）。
    /// 默认 false（生产公网默认开启防护）；内网/离线部署需回调内网接收端时，
    /// 通过 appsettings.Production.json 或环境变量 Webhook__AllowPrivateUrl=true 放行。
    /// </summary>
    private bool AllowPrivateUrl() => _configuration.GetValue("Webhook:AllowPrivateUrl", false);

    /// <summary>校验 Webhook URL 是否合法（SSRF 防护；allowPrivate=true 时放行内网/回环地址，供内网部署回调使用）</summary>
    private static async Task ValidateUrlAsync(string url, bool allowPrivate)
    {
        if (string.IsNullOrWhiteSpace(url))
            throw new ArgumentException("URL 不能为空");

        if (!Uri.TryCreate(url, UriKind.Absolute, out var uri))
            throw new ArgumentException("URL 格式无效");

        if (uri.Scheme != "http" && uri.Scheme != "https")
            throw new ArgumentException("URL 必须以 http:// 或 https:// 开头");

        if (!allowPrivate && uri.IsLoopback)
            throw new ArgumentException("URL 不能指向本地回环地址");

        var host = uri.Host;
        if (IPAddress.TryParse(host, out var ipAddr))
        {
            if (!allowPrivate)
                ValidateIpAddress(ipAddr);
        }
        else
        {
            try
            {
                var addresses = await Dns.GetHostAddressesAsync(host);
                if (addresses.Length == 0)
                    throw new ArgumentException("无法解析 URL 域名");

                foreach (var addr in addresses)
                {
                    if (!allowPrivate)
                        ValidateIpAddress(addr);
                }
            }
            catch (ArgumentException)
            {
                throw;
            }
            catch (Exception ex)
            {
                throw new ArgumentException($"URL 域名解析失败: {ex.Message}");
            }
        }

        if (uri.Port == 0 || uri.Port > 65535)
            throw new ArgumentException("URL 端口无效");
    }

    /// <summary>创建带 SSRF 防护的 HttpClient（连接时验证 IP，防止 TOCTOU / DNS rebinding）。按 timeout+allowPrivate 缓存实例，避免 socket 耗尽。</summary>
    private static HttpClient CreateSafeHttpClient(TimeSpan timeout, bool allowPrivate)
    {
        // 使用 timeout 与 allowPrivate 的组合哈希作为缓存键，避免不同 timeout/开关因哈希碰撞被错误复用
        var cacheKey = HashCode.Combine(timeout.GetHashCode(), allowPrivate);
        return _httpClientCache.GetOrAdd(cacheKey, _ => new Lazy<HttpClient>(() =>
        {
            var handler = new SocketsHttpHandler
            {
                AllowAutoRedirect = false,
                UseCookies = false,
                ConnectCallback = async (context, cancellationToken) =>
                {
                    var addresses = await Dns.GetHostAddressesAsync(context.DnsEndPoint.Host, cancellationToken);
                    if (addresses.Length == 0)
                        throw new HttpRequestException("无法解析目标主机");

                    Exception? lastException = null;
                    foreach (var addr in addresses)
                    {
                        try
                        {
                            if (!allowPrivate)
                                ValidateIpAddress(addr);

                            var socket = new Socket(addr.AddressFamily, SocketType.Stream, ProtocolType.Tcp)
                            {
                                NoDelay = true
                            };
                            await socket.ConnectAsync(addr, context.DnsEndPoint.Port, cancellationToken);
                            return new NetworkStream(socket, ownsSocket: true);
                        }
                        catch (ArgumentException)
                        {
                            throw;
                        }
                        catch (Exception ex)
                        {
                            lastException = ex;
                        }
                    }

                    throw lastException ?? new HttpRequestException("无法连接到目标服务器");
                }
            };

            return new HttpClient(handler) { Timeout = timeout };
        }, LazyThreadSafetyMode.ExecutionAndPublication)).Value;
    }

    private static readonly HashSet<string> CloudMetadataIps = new(StringComparer.Ordinal)
    {
        "169.254.169.254",
        "fd00:ec2::254"
    };

    private static void ValidateIpAddress(IPAddress ip)
    {
        if (ip == null)
            throw new ArgumentException("IP 地址不能为空");

        // 处理 IPv4 映射到 IPv6 的情况（::ffff:x.x.x.x）
        if (ip.AddressFamily == AddressFamily.InterNetworkV6 && ip.IsIPv4MappedToIPv6)
        {
            ip = ip.MapToIPv4();
        }

        if (IPAddress.IsLoopback(ip))
            throw new ArgumentException("URL 不能指向回环地址");

        // 云元数据服务 IP（AWS/GCP/Azure 等）
        if (CloudMetadataIps.Contains(ip.ToString()))
            throw new ArgumentException("URL 不能指向云元数据服务地址");

        if (ip.AddressFamily == AddressFamily.InterNetworkV6)
        {
            if (ip.IsIPv6LinkLocal || ip.IsIPv6SiteLocal)
                throw new ArgumentException("URL 不能指向内网或回环地址");
            if (ip.IsIPv6Multicast)
                throw new ArgumentException("URL 不能指向组播地址");
        }

        var bytes = ip.GetAddressBytes();
        if (bytes.Length == 4)
        {
            if (bytes[0] == 10)
                throw new ArgumentException("URL 不能指向 10.x.x.x 内网地址");
            if (bytes[0] == 172 && bytes[1] >= 16 && bytes[1] <= 31)
                throw new ArgumentException("URL 不能指向 172.16-31.x.x 内网地址");
            if (bytes[0] == 192 && bytes[1] == 168)
                throw new ArgumentException("URL 不能指向 192.168.x.x 内网地址");
            if (bytes[0] == 127)
                throw new ArgumentException("URL 不能指向 127.x.x.x 回环地址");
            if (bytes[0] == 169 && bytes[1] == 254)
                throw new ArgumentException("URL 不能指向链路本地地址");
            if (bytes[0] == 0)
                throw new ArgumentException("URL 不能指向 0.x.x.x 地址");
            // 组播地址（D类）和保留地址（E类）
            if (bytes[0] >= 224)
                throw new ArgumentException("URL 不能指向组播或保留地址");
        }
    }

    public async Task<WebhookDto?> GetByIdAsync(int id)
    {
        var webhook = await _unitOfWork.Webhooks.GetByIdAsync(id);
        if (webhook == null) return null;

        if (!await _currentUserAccessor.CanAccessMailboxAsync(webhook.MailboxId))
            return null;

        return MapToDto(webhook);
    }

    public async Task<(List<WebhookDto> Items, int Total)> GetPagedAsync(WebhookQuery query)
    {
        var (items, total) = await _unitOfWork.Webhooks.GetPagedAsync(
            query.Page,
            query.PageSize,
            query.MailboxId,
            query.IsActive,
            query.Keyword,
            query.EventType);
        return (items.Select(MapToDto).ToList(), total);
    }

    public async Task<WebhookOverviewResponse> GetOverviewAsync()
    {
        var stats = await _unitOfWork.Webhooks.GetStatsAsync();
        var (topItems, _) = await _unitOfWork.Webhooks.GetPagedAsync(1, 5, isActive: true);

        var recentLogs = await _unitOfWork.WebhookLogs.GetPagedAsync(1, 10);
        var allWebhooks = await _unitOfWork.Webhooks.GetAllWithMailboxAsync();
        var webhookDict = allWebhooks.ToDictionary(w => w.Id, w => w.Name);

        var eventsCount = allWebhooks
            .SelectMany(w => ParseEvents(w.Events))
            .GroupBy(e => e)
            .ToDictionary(g => g.Key, g => g.Count());

        var totalTriggers = stats.GetValueOrDefault("totalTriggers", 0);
        var totalSuccess = stats.GetValueOrDefault("totalSuccess", 0);

        return new WebhookOverviewResponse
        {
            TotalWebhooks = stats.GetValueOrDefault("total", 0),
            ActiveWebhooks = stats.GetValueOrDefault("active", 0),
            InactiveWebhooks = stats.GetValueOrDefault("inactive", 0),
            GlobalWebhooks = stats.GetValueOrDefault("global", 0),
            MailboxWebhooks = stats.GetValueOrDefault("mailboxSpecific", 0),
            TotalTriggers = totalTriggers,
            TotalSuccess = totalSuccess,
            TotalFail = stats.GetValueOrDefault("totalFail", 0),
            SuccessRate = totalTriggers > 0 ? Math.Round((decimal)totalSuccess / totalTriggers * 100, 2) : 0,
            RecentLogs = recentLogs.Items.Select(l => MapLogToDto(l, webhookDict)).ToList(),
            TopWebhooks = topItems.Select(MapToDto).ToList(),
            EventsCount = eventsCount,
        };
    }

    public async Task<WebhookDto> CreateAsync(CreateWebhookRequest request)
    {
        await ValidateUrlAsync(request.Url, AllowPrivateUrl());

        var webhook = new Webhook
        {
            Name = request.Name,
            Url = request.Url,
            Method = request.Method,
            Events = string.Join(',', request.Events),
            HeadersJson = request.Headers.Count > 0 ? JsonSerializer.Serialize(request.Headers) : null,
            Secret = string.IsNullOrEmpty(request.Secret) ? null : request.Secret,
            IsActive = request.IsActive,
            MailboxId = request.MailboxId,
        };

        await _unitOfWork.Webhooks.AddAsync(webhook);
        await _unitOfWork.SaveChangesAsync();
        return MapToDto(webhook);
    }

    public async Task<WebhookDto?> UpdateAsync(UpdateWebhookRequest request, string? ifMatch = null)
    {
        // 使用 tracking 查询，确保修改能持久化到数据库
        var webhook = await _unitOfWork.Webhooks.GetQueryable(false)
            .FirstOrDefaultAsync(w => w.Id == request.Id && w.DeletedAt == null);
        if (webhook == null) return null;

        if (!await _currentUserAccessor.CanAccessMailboxAsync(webhook.MailboxId))
            return null;

        // M8: 乐观锁 If-Match 校验（HTTP 标准优先，Header 覆盖 Body）
        var effectiveIfMatch = !string.IsNullOrEmpty(ifMatch) ? ifMatch.Trim('"') : request.IfMatch;
        ConcurrencyCheckHelper.Check(webhook, webhook.RowVersion, effectiveIfMatch, "Webhook", webhook.Id,
            () => MapToDto(webhook), _logger);

        await ValidateUrlAsync(request.Url, AllowPrivateUrl());

        webhook.Name = request.Name;
        webhook.Url = request.Url;
        webhook.Method = request.Method;
        webhook.Events = string.Join(',', request.Events);
        webhook.HeadersJson = request.Headers.Count > 0 ? JsonSerializer.Serialize(request.Headers) : null;
        if (request.Secret != null)
        {
            webhook.Secret = string.IsNullOrEmpty(request.Secret) ? null : request.Secret;
        }
        webhook.IsActive = request.IsActive;
        webhook.MailboxId = request.MailboxId;
        webhook.UpdatedAt = DateTime.UtcNow;

        await _unitOfWork.SaveChangesAsync();
        return MapToDto(webhook);
    }

    public async Task<bool> DeleteAsync(int id)
    {
        // 使用追踪查询获取实体，确保修改能持久化到数据库
        var webhook = await _unitOfWork.Webhooks.GetQueryable(false)
            .FirstOrDefaultAsync(w => w.Id == id);
        if (webhook == null) return false;

        if (!await _currentUserAccessor.CanAccessMailboxAsync(webhook.MailboxId))
            return false;

        webhook.DeletedAt = DateTime.UtcNow;
        webhook.UpdatedAt = DateTime.UtcNow;
        await _unitOfWork.SaveChangesAsync();
        return true;
    }

    // ====== 垃圾箱 ======
    public async Task<(List<WebhookDto> Items, int Total)> GetTrashPagedAsync(int page = 1, int pageSize = 10, string? keyword = null)
    {
        var (items, total) = await _unitOfWork.Webhooks.GetDeletedPagedAsync(page, pageSize, keyword);
        return (items.Select(MapToDto).ToList(), total);
    }

    public async Task<WebhookDto?> RestoreAsync(int id)
    {
        var webhook = await _unitOfWork.Webhooks.GetDeletedByIdAsync(id);
        if (webhook == null) return null;
        await _unitOfWork.Webhooks.RestoreAsync(id);
        return await GetByIdAsync(id);
    }

    public async Task<bool> HardDeleteAsync(int id)
    {
        return await _unitOfWork.Webhooks.HardDeleteAsync(id);
    }

    public async Task<int> ClearTrashAsync()
    {
        return await _unitOfWork.Webhooks.HardDeleteAllDeletedAsync();
    }

    public async Task<bool> ToggleActiveAsync(int id, string? ifMatch = null)
    {
        var webhook = await _unitOfWork.Webhooks.GetQueryable(false)
            .FirstOrDefaultAsync(w => w.Id == id && w.DeletedAt == null);
        if (webhook == null) return false;

        // M8: 乐观锁 If-Match 校验，防止并发启停互相覆盖
        ConcurrencyCheckHelper.Check(webhook, webhook.RowVersion, ifMatch?.Trim('"'), "Webhook", webhook.Id,
            () => MapToDto(webhook), _logger);

        webhook.IsActive = !webhook.IsActive;
        webhook.UpdatedAt = DateTime.UtcNow;
        await _unitOfWork.SaveChangesAsync();
        return true;
    }

    public async Task<TestWebhookResponse> TestAsync(int id, TestWebhookRequest request)
    {
        // 使用 tracking 查询，LogDelivery 会修改 webhook 统计字段（TriggerCount 等），需被跟踪以持久化
        var webhook = await _unitOfWork.Webhooks.GetQueryable(false)
            .FirstOrDefaultAsync(w => w.Id == id && w.DeletedAt == null);
        if (webhook == null)
        {
            return new TestWebhookResponse { Success = false, Message = "Webhook 不存在" };
        }

        var testPayload = GenerateTestPayload(request.EventType);
        var startTime = DateTime.UtcNow;

        try
        {
            await ValidateUrlAsync(webhook.Url, AllowPrivateUrl());

            var redirectSafeClient = CreateSafeHttpClient(TimeSpan.FromSeconds(_httpClientOptions.Value.WebhookTimeoutSeconds), AllowPrivateUrl());

            using var requestMessage = new HttpRequestMessage(new HttpMethod(webhook.Method), webhook.Url);
            requestMessage.Headers.Add("X-Webhook-Event", request.EventType);
            requestMessage.Headers.Add("X-Webhook-Delivery", Guid.NewGuid().ToString());

            if (!string.IsNullOrEmpty(webhook.Secret))
            {
                // Secret 已由 DbContext 的 EncryptedStringConverter 自动解密
                var signature = GenerateSignature(testPayload, webhook.Secret);
                requestMessage.Headers.Add("X-Webhook-Signature", signature);
            }

            var headers = ParseHeaders(webhook.HeadersJson);
            foreach (var header in headers)
            {
                if (IsRestrictedWebhookHeader(header.Key)) continue;
                requestMessage.Headers.TryAddWithoutValidation(header.Key, header.Value);
            }

            requestMessage.Content = new StringContent(testPayload, Encoding.UTF8, "application/json");

            using var response = await redirectSafeClient.SendAsync(requestMessage);
            var duration = (int)(DateTime.UtcNow - startTime).TotalMilliseconds;
            var responseBody = response.IsSuccessStatusCode
                ? await response.Content.ReadAsStringAsync()
                : null;

            if (responseBody != null && responseBody.Length > WebhookLogResponseBodyMaxLength)
            {
                responseBody = responseBody[..WebhookLogResponseBodyMaxLength] + "...";
            }

            await LogDelivery(webhook, request.EventType, testPayload, (int)response.StatusCode, responseBody, response.IsSuccessStatusCode, null, duration);

            return new TestWebhookResponse
            {
                Success = response.IsSuccessStatusCode,
                Message = response.IsSuccessStatusCode ? "发送成功" : $"HTTP {(int)response.StatusCode}",
                StatusCode = (int)response.StatusCode,
                ResponseBody = responseBody,
                DurationMs = duration,
            };
        }
        catch (Exception ex)
        {
            var duration = (int)(DateTime.UtcNow - startTime).TotalMilliseconds;
            await LogDelivery(webhook, request.EventType, testPayload, null, null, false, ex.Message, duration);

            return new TestWebhookResponse
            {
                Success = false,
                Message = ex.Message,
                DurationMs = duration,
            };
        }
    }

    public async Task<List<WebhookLogDto>> GetLogsAsync(int webhookId, int limit = 50)
    {
        var logs = await _unitOfWork.WebhookLogs.GetByWebhookAsync(webhookId, limit);
        var webhook = await _unitOfWork.Webhooks.GetByIdAsync(webhookId);
        var name = webhook?.Name ?? string.Empty;
        return logs.Select(l => MapLogToDto(l, new Dictionary<int, string> { { webhookId, name } })).ToList();
    }

    public async Task<(List<WebhookLogDto> Items, int Total)> GetLogsPagedAsync(WebhookLogQuery query)
    {
        var (items, total) = await _unitOfWork.WebhookLogs.GetPagedAsync(
            query.Page,
            query.PageSize,
            query.WebhookId,
            query.EventType,
            query.IsSuccess);

        var webhookIds = items.Select(l => l.WebhookId).Distinct().ToList();
        var webhookNames = new Dictionary<int, string>();

        // 一次性查询所有相关 webhook 名称，避免 N+1
        if (webhookIds.Count > 0)
        {
            var webhooks = await _unitOfWork.Webhooks
                .GetQueryable()
                .Where(w => webhookIds.Contains(w.Id))
                .Select(w => new { w.Id, w.Name })
                .ToListAsync();
            foreach (var w in webhooks)
            {
                webhookNames[w.Id] = w.Name;
            }
        }

        return (items.Select(l => MapLogToDto(l, webhookNames)).ToList(), total);
    }

    private async Task LogDelivery(
        Webhook webhook, string eventType, string requestBody,
        int? statusCode, string? responseBody, bool isSuccess, string? errorMessage, int durationMs,
        string? entityType = null, int? entityId = null)
    {
        var log = new WebhookLog
        {
            WebhookId = webhook.Id,
            EventType = eventType,
            EntityType = entityType,
            EntityId = entityId,
            Url = webhook.Url,
            Method = webhook.Method,
            RequestBody = requestBody.Length > WebhookLogRequestBodyMaxLength ? requestBody[..WebhookLogRequestBodyMaxLength] + "..." : requestBody,
            ResponseStatusCode = statusCode,
            ResponseBody = responseBody,
            IsSuccess = isSuccess,
            ErrorMessage = errorMessage,
            DurationMs = durationMs,
        };

        await _unitOfWork.WebhookLogs.AddAsync(log);

        // 统计字段改用原子递增（ExecuteUpdateAsync），避免测试/触发路径并发更新时
        // RowVersion 冲突抛出 DbUpdateConcurrencyException。失败仅记录日志，不影响发送结果。
        try
        {
            await _unitOfWork.Webhooks.IncrementTriggerStatsAsync(webhook.Id, isSuccess, errorMessage);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "原子递增 Webhook 统计失败（不影响发送结果）：WebhookId={WebhookId}", webhook.Id);
        }

        await _unitOfWork.SaveChangesAsync();
    }

    /// <summary>
    /// 单次 webhook 发送结果（不含日志，日志由 TriggerAsync 批量记录）。
    /// </summary>
    private sealed record WebhookSendResult(
        Webhook Webhook,
        string EventType,
        string PayloadJson,
        string? EntityType,
        int? EntityId,
        int? StatusCode,
        string? ResponseBody,
        bool IsSuccess,
        string? ErrorMessage,
        int DurationMs);

    /// <summary>
    /// 触发指定事件的所有活跃 Webhook。
    /// 并发发送 HTTP 请求（限制并发度 5），发送完成后统一批量记录日志（一次 SaveChangesAsync）。
    /// 单个 webhook 发送失败不影响其他 webhook，失败不抛异常，仅记录到 WebhookLog。
    /// </summary>
    public async Task<int> TriggerAsync(string eventType, int? mailboxId = null, object? payload = null, string? entityType = null, int? entityId = null)
    {
        if (string.IsNullOrWhiteSpace(eventType))
        {
            return 0;
        }

        // 使用 AsNoTracking 查询：统计字段（TriggerCount 等）不再通过 ChangeTracker 修改，
        // 改为原子递增（IncrementTriggerStatsAsync），避免并发触发时 RowVersion 冲突导致
        // DbUpdateConcurrencyException 传播到调用方（工作流 trigger_webhook 动作会整体回滚）。
        var webhooks = await _unitOfWork.Webhooks.GetActiveForEventAsync(eventType, mailboxId);
        if (webhooks.Count == 0)
        {
            return 0;
        }

        // 序列化负载（默认使用包含 event_type 和 timestamp 的标准结构）
        var payloadJson = payload != null
            ? JsonSerializer.Serialize(payload, new JsonSerializerOptions { WriteIndented = false })
            : JsonSerializer.Serialize(new
            {
                event_type = eventType,
                timestamp = DateTime.UtcNow,
                test = false,
                data = new { entity_type = entityType, entity_id = entityId }
            });

        // 并发发送 HTTP 请求（限制并发度 5），单个失败不影响其他
        using var semaphore = new SemaphoreSlim(5);
        var sendTasks = webhooks.Select(async webhook =>
        {
            await semaphore.WaitAsync();
            try
            {
                return await SendWebhookHttpAsync(webhook, eventType, payloadJson, entityType, entityId);
            }
            finally
            {
                semaphore.Release();
            }
        }).ToList();
        var results = await Task.WhenAll(sendTasks);

        // 批量记录日志：收集所有 delivery log，统一 AddRangeAsync + 一次 SaveChangesAsync
        // 统计字段（TriggerCount/SuccessCount/FailCount 等）改用原子递增（ExecuteUpdateAsync），
        // 不经过 ChangeTracker、不校验 RowVersion，避免并发触发时 DbUpdateConcurrencyException
        // 传播到调用方导致工作流/业务事务整体回滚。
        var logs = new List<WebhookLog>();
        var triggeredCount = 0;
        foreach (var result in results)
        {
            var log = new WebhookLog
            {
                WebhookId = result.Webhook.Id,
                EventType = result.EventType,
                EntityType = result.EntityType,
                EntityId = result.EntityId,
                Url = result.Webhook.Url,
                Method = result.Webhook.Method,
                RequestBody = result.PayloadJson.Length > 2000 ? result.PayloadJson[..2000] + "..." : result.PayloadJson,
                ResponseStatusCode = result.StatusCode,
                ResponseBody = result.ResponseBody,
                IsSuccess = result.IsSuccess,
                ErrorMessage = result.ErrorMessage,
                DurationMs = result.DurationMs,
            };
            logs.Add(log);

            if (result.IsSuccess)
            {
                triggeredCount++;
            }
        }

        await _unitOfWork.WebhookLogs.AddRangeAsync(logs);
        await _unitOfWork.SaveChangesAsync();

        // 原子递增统计字段（在每个 webhook 发送结果上），不抛异常，失败仅记录日志
        foreach (var result in results)
        {
            try
            {
                await _unitOfWork.Webhooks.IncrementTriggerStatsAsync(
                    result.Webhook.Id, result.IsSuccess, result.ErrorMessage);
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "原子递增 Webhook 统计失败（不影响发送结果）：WebhookId={WebhookId}", result.Webhook.Id);
            }
        }

        return triggeredCount;
    }

    /// <summary>
    /// 仅发送 webhook HTTP 请求，不记录日志。日志由 TriggerAsync 批量记录。
    /// 失败返回包含错误信息的结果，不抛异常。
    /// </summary>
    private async Task<WebhookSendResult> SendWebhookHttpAsync(Webhook webhook, string eventType, string payloadJson, string? entityType, int? entityId)
    {
        var startTime = DateTime.UtcNow;
        try
        {
            await ValidateUrlAsync(webhook.Url, AllowPrivateUrl());

            var client = CreateSafeHttpClient(TimeSpan.FromSeconds(_httpClientOptions.Value.WebhookTimeoutSeconds), AllowPrivateUrl());

            using var requestMessage = new HttpRequestMessage(new HttpMethod(webhook.Method), webhook.Url);
            requestMessage.Headers.Add("X-Webhook-Event", eventType);
            requestMessage.Headers.Add("X-Webhook-Delivery", Guid.NewGuid().ToString());

            if (!string.IsNullOrEmpty(webhook.Secret))
            {
                // Secret 已由 DbContext 的 EncryptedStringConverter 自动解密
                var signature = GenerateSignature(payloadJson, webhook.Secret);
                requestMessage.Headers.Add("X-Webhook-Signature", signature);
            }

            var headers = ParseHeaders(webhook.HeadersJson);
            foreach (var header in headers)
            {
                if (IsRestrictedWebhookHeader(header.Key)) continue;
                requestMessage.Headers.TryAddWithoutValidation(header.Key, header.Value);
            }

            requestMessage.Content = new StringContent(payloadJson, Encoding.UTF8, "application/json");

            using var response = await client.SendAsync(requestMessage);
            var duration = (int)(DateTime.UtcNow - startTime).TotalMilliseconds;
            var responseBody = response.IsSuccessStatusCode
                ? await response.Content.ReadAsStringAsync()
                : null;

            if (responseBody != null && responseBody.Length > WebhookLogResponseBodyMaxLength)
            {
                responseBody = responseBody[..WebhookLogResponseBodyMaxLength] + "...";
            }

            return new WebhookSendResult(webhook, eventType, payloadJson, entityType, entityId, (int)response.StatusCode, responseBody, response.IsSuccessStatusCode, null, duration);
        }
        catch (Exception ex)
        {
            var duration = (int)(DateTime.UtcNow - startTime).TotalMilliseconds;
            return new WebhookSendResult(webhook, eventType, payloadJson, entityType, entityId, null, null, false, ex.Message, duration);
        }
    }

    private static string GenerateTestPayload(string eventType)
    {
        var payload = new
        {
            event_type = eventType,
            timestamp = DateTime.UtcNow,
            test = true,
            data = new
            {
                id = 1,
                subject = "测试会话 - Webhook 连通性测试",
                status = "active",
                mailbox_id = 1,
                customer = new
                {
                    id = 1,
                    email = "test@example.com",
                    name = "测试客户",
                },
            },
        };
        return JsonSerializer.Serialize(payload, new JsonSerializerOptions { WriteIndented = true });
    }

    private static string GenerateSignature(string payload, string secret)
    {
        using var hmac = new System.Security.Cryptography.HMACSHA256(Encoding.UTF8.GetBytes(secret));
        var hash = hmac.ComputeHash(Encoding.UTF8.GetBytes(payload));
        return "sha256=" + Convert.ToBase64String(hash);
    }

    private static Dictionary<string, string> ParseHeaders(string? headersJson)
    {
        if (string.IsNullOrEmpty(headersJson)) return new Dictionary<string, string>();
        try
        {
            return JsonSerializer.Deserialize<Dictionary<string, string>>(headersJson) ?? new();
        }
        catch
        {
            return new Dictionary<string, string>();
        }
    }

    /// <summary>
    /// 禁止用户通过 HeadersJson 自定义的请求头：由 HTTP 传输层或系统自身管理，
    /// 允许覆盖可能导致请求走私 / 头注入 / 破坏传输语义（如 Host、Content-Length、Connection）。
    /// </summary>
    private static readonly HashSet<string> RestrictedWebhookHeaders = new(StringComparer.OrdinalIgnoreCase)
    {
        "Host",
        "Content-Length",
        "Transfer-Encoding",
        "Connection",
        "Expect",
        "Upgrade",
        "Keep-Alive",
        "Proxy-Connection",
        "TE",
        "Trailer",
        "Proxy-Authenticate",
        "Proxy-Authorization"
    };

    /// <summary>
    /// 判断自定义请求头是否被限制：命中黑名单或 X-Webhook-* 系统保留前缀（事件/投递ID/签名）。
    /// </summary>
    private static bool IsRestrictedWebhookHeader(string name)
    {
        return RestrictedWebhookHeaders.Contains(name)
               || name.StartsWith("X-Webhook-", StringComparison.OrdinalIgnoreCase);
    }

    private static List<string> ParseEvents(string events)
    {
        if (string.IsNullOrEmpty(events)) return new List<string>();
        return events.Split(',', StringSplitOptions.RemoveEmptyEntries).ToList();
    }

    private static WebhookDto MapToDto(Webhook webhook)
    {
        return new WebhookDto
        {
            Id = webhook.Id,
            Name = webhook.Name,
            Url = webhook.Url,
            Method = webhook.Method,
            Events = ParseEvents(webhook.Events),
            Headers = ParseHeaders(webhook.HeadersJson),
            HasSecret = !string.IsNullOrEmpty(webhook.Secret),
            IsActive = webhook.IsActive,
            MailboxId = webhook.MailboxId,
            MailboxName = webhook.Mailbox?.Name,
            TriggerCount = webhook.TriggerCount,
            SuccessCount = webhook.SuccessCount,
            FailCount = webhook.FailCount,
            LastTriggeredAt = webhook.LastTriggeredAt,
            LastError = webhook.LastError,
            CreatedAt = webhook.CreatedAt,
            UpdatedAt = webhook.UpdatedAt,
            RowVersion = webhook.RowVersion != null ? Convert.ToBase64String(webhook.RowVersion) : null,
        };
    }

    private static WebhookLogDto MapLogToDto(WebhookLog log, Dictionary<int, string> webhookNames)
    {
        return new WebhookLogDto
        {
            Id = log.Id,
            WebhookId = log.WebhookId,
            WebhookName = webhookNames.TryGetValue(log.WebhookId, out var name) ? name : string.Empty,
            EventType = log.EventType,
            EntityType = log.EntityType,
            EntityId = log.EntityId,
            Url = log.Url,
            Method = log.Method,
            ResponseStatusCode = log.ResponseStatusCode,
            IsSuccess = log.IsSuccess,
            ErrorMessage = log.ErrorMessage,
            DurationMs = log.DurationMs,
            CreatedAt = log.CreatedAt,
        };
    }
}
