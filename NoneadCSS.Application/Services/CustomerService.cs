using System.Text.Json;
using System.Text.RegularExpressions;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;
using NoneadCSS.Application.DTOs.Requests;
using NoneadCSS.Application.DTOs.Responses;
using NoneadCSS.Application.Mappings;
using NoneadCSS.Domain;
using NoneadCSS.Domain.Entities;

namespace NoneadCSS.Application.Services;

public class CustomerService : ICustomerService
{
    private readonly IUnitOfWork _unitOfWork;
    private readonly IAuditService _auditService;
    private readonly ILogger<CustomerService> _logger;
    private readonly ICurrentUserAccessor _currentUserAccessor;
    private readonly IUserRoleResolver _userRoleResolver;
    private readonly IWebhookService? _webhookService;

    /// <summary>宽松手机号校验：国际 E.164 + 常见中国号码格式。长度 7~20 位，允许前导 + 和中间 -/空格分隔符。</summary>
    private static readonly Regex PhoneSanitizeRegex = new(@"[^\d\+\-\s]", RegexOptions.Compiled | RegexOptions.NonBacktracking);

    public CustomerService(IUnitOfWork unitOfWork, IAuditService auditService, ILogger<CustomerService> logger,
        ICurrentUserAccessor currentUserAccessor, IUserRoleResolver userRoleResolver, IWebhookService? webhookService = null)
    {
        _unitOfWork = unitOfWork;
        _auditService = auditService;
        _logger = logger;
        _currentUserAccessor = currentUserAccessor;
        _userRoleResolver = userRoleResolver;
        _webhookService = webhookService;
    }

    /// <summary>
    /// 校验头像 URL 是否为允许的协议。
    /// 允许协议：
    ///   - https:// ：安全外链（现代浏览器混合内容策略会拦截 http 外链图像，故拒绝 http）
    ///   - data:image/*;base64,... ：内联 base64 图像
    ///   - / 或 ~/ 开头的相对路径：系统已上传到本地的附件（<img src="/attachments/...">）
    /// 拒绝：
    ///   - http:// ：混合内容降级 + 安全风险
    ///   - javascript: / vbscript: / data:text/html / data:application 等非图像 data URI
    ///   - blob: / file: / ftp: / mailto: 等其他协议
    ///   - 其他任何不在白名单中的 URI 方案
    /// </summary>
    /// <param name="avatar">原始头像值（null/空字符串视为合法，因为代表清除/未设置头像）</param>
    /// <param name="normalized">若校验通过，规范化后的头像值（会将全角空格、首尾空白去掉）；若校验失败为 null</param>
    /// <returns>校验通过返回 null；失败返回用户可读的错误消息</returns>
    internal static string? ValidateAndNormalizeAvatarUrl(string? avatar, out string? normalized)
    {
        // null / 空字符串：显式清除 / 未设置头像，合法
        if (string.IsNullOrWhiteSpace(avatar))
        {
            normalized = string.IsNullOrWhiteSpace(avatar) ? null : avatar.Trim();
            return null;
        }

        var trimmed = avatar.Trim();
        normalized = trimmed;

        // 相对路径：/path、./path、../path、~/path — 系统内部资源，允许
        if (trimmed.StartsWith('/') || trimmed.StartsWith("./") || trimmed.StartsWith("../") || trimmed.StartsWith("~/"))
        {
            return null;
        }

        // data: URI：仅允许 image/* MIME + 禁止 data:text/html（可执行 SVG/HTML）等危险类型
        if (trimmed.StartsWith("data:", StringComparison.OrdinalIgnoreCase))
        {
            // data:image/* 均允许（png/jpg/gif/webp/svg+xml 等），拒绝其他 data MIME
            if (trimmed.Length > 11
                && trimmed.IndexOf("image/", 5, StringComparison.OrdinalIgnoreCase) == 5)
            {
                return null;
            }
            return "头像仅允许使用 data:image/* 类型的内联图像（data:image/png;base64,... 等），禁止 data:text/html 或其他 data: 类型";
        }

        // 绝对 URL：必须走 https 协议
        if (!Uri.TryCreate(trimmed, UriKind.Absolute, out var uri))
        {
            // 既不是相对路径，也不是 data:，也不是合法绝对 URL
            return "头像链接格式不合法";
        }

        // 协议白名单：仅 https；显式拒绝 http、javascript、vbscript、blob、file、ftp、mailto 等
        if (string.Equals(uri.Scheme, "https", StringComparison.OrdinalIgnoreCase))
        {
            return null;
        }
        if (string.Equals(uri.Scheme, "http", StringComparison.OrdinalIgnoreCase))
        {
            return "出于安全考虑，头像仅支持使用 https:// 外链；请将图片上传到本站或更换为 https 链接";
        }
        return "头像仅支持 https:// 外链或 data:image/* 内联图像，禁止使用 javascript、vbscript、blob、file、ftp 等协议";
    }

    /// <summary>
    /// 校验并规范化单个手机号：剔除控制字符、非数字非+分隔符，检查长度在 7~20 之间，防止超长字符串写入 JSON。
    /// </summary>
    /// <returns>(ok, normalizedValue, errorMessage)</returns>
    private static (bool Ok, string? Normalized, string? Error) ValidateAndNormalizePhone(string? raw)
    {
        if (string.IsNullOrWhiteSpace(raw))
            return (false, null, "手机号不能为空");

        var stripped = PhoneSanitizeRegex.Replace(raw.Trim(), string.Empty);
        if (stripped.Length < 7 || stripped.Length > 20)
            return (false, null, "手机号长度必须在 7~20 位之间");

        return (true, stripped, null);
    }

    /// <summary>
    /// 校验网址协议白名单：仅允许 http(s) 协议 + 相对路径，防止 javascript:/data:text/html 伪协议注入。
    /// </summary>
    private static (bool Ok, string? Normalized, string? Error) ValidateAndNormalizeWebsite(string? raw)
    {
        if (string.IsNullOrWhiteSpace(raw))
            return (false, null, "网址不能为空");

        var trimmed = raw.Trim();

        // 相对路径：系统内部页面
        if (trimmed.StartsWith('/') || trimmed.StartsWith("./") || trimmed.StartsWith("../") || trimmed.StartsWith("~/"))
            return (true, trimmed, null);

        if (!Uri.TryCreate(trimmed, UriKind.Absolute, out var uri))
            return (false, null, "网址格式不合法");

        if (string.Equals(uri.Scheme, "http", StringComparison.OrdinalIgnoreCase)
            || string.Equals(uri.Scheme, "https", StringComparison.OrdinalIgnoreCase))
        {
            // 保留规范化 URL（小写 scheme/host + Punycode 域名转义由 Uri 类处理）
            return (true, uri.ToString(), null);
        }

        return (false, null, "网址仅支持 http:// 或 https:// 协议");
    }

    /// <summary>
    /// 校验并规范化客户邮箱：去除控制字符、统一小写、防止域名字欺骗。
    /// 空值视为合法（通过多渠道后续补全）。
    /// </summary>
    private static (bool Ok, string? Normalized, string? Error) ValidateAndNormalizeEmail(string? raw)
    {
        if (string.IsNullOrWhiteSpace(raw))
            return (true, null, null);

        var trimmed = raw.Trim();
        if (trimmed.Length > 254) // RFC 5321 §4.5.3.1.1 - path length limit
            return (false, null, "邮箱长度不能超过 254 字符");

        // 去除所有 C0/C1 控制字符
        var sanitized = new string(trimmed.Where(c => !char.IsControl(c)).ToArray());

        try
        {
            var addr = new System.Net.Mail.MailAddress(sanitized);
            var normalized = $"{addr.User}@{addr.Host}".ToLowerInvariant();
            return (true, normalized, null);
        }
        catch (FormatException)
        {
            return (false, null, "邮箱格式不合法");
        }
    }

    public async Task<PagedResponse<CustomerResponse>> SearchCustomersAsync(string query, int page, int pageSize)
    {
        var (items, total) = await _unitOfWork.Customers.SearchAsync(query, page, pageSize);
        var customerIds = items.Select(c => c.Id).ToList();
        var conversationCounts = await _unitOfWork.Conversations.GetCountByCustomerIdsAsync(customerIds);
        var responses = items.Select(c => c.ToResponse(conversationCounts.TryGetValue(c.Id, out var count) ? count : 0));
        return responses.ToPagedResponse(page, pageSize, total);
    }

    public async Task<PagedResponse<CustomerResponse>> SearchCustomersByMailboxIdsAsync(string query, List<int> mailboxIds, int page, int pageSize)
    {
        var (items, total) = await _unitOfWork.Customers.SearchAsync(query, page, pageSize, mailboxIds.ToArray());
        var customerIds = items.Select(c => c.Id).ToList();
        var conversationCounts = await _unitOfWork.Conversations.GetCountByCustomerIdsAsync(customerIds);
        var responses = items.Select(c => c.ToResponse(conversationCounts.TryGetValue(c.Id, out var count) ? count : 0));
        return responses.ToPagedResponse(page, pageSize, total);
    }

    public async Task<CustomerResponse?> GetCustomerByIdAsync(int id)
    {
        var customer = await _unitOfWork.Customers.GetByIdAsync(id);
        if (customer == null)
        {
            return null;
        }
        var conversationsCount = await _unitOfWork.Conversations.CountActiveByCustomerIdAsync(id);
        return customer.ToResponse(conversationsCount);
    }

    public async Task<(CustomerResponse? Customer, string? ErrorMessage)> CreateCustomerAsync(CreateCustomerRequest request)
    {
        // 1. 邮箱规范化 + 唯一性检查（空邮箱允许，返回 normalized == null）
        var (emailOk, normalizedEmail, emailErr) = ValidateAndNormalizeEmail(request.Email);
        if (!emailOk)
            return (null, emailErr);
        if (normalizedEmail != null)
        {
            var emailExists = await _unitOfWork.Customers.ExistsByEmailAsync(normalizedEmail);
            if (emailExists)
                return (null, "该邮箱已被使用");
        }

        // 2. 头像 URL 协议校验（防止 http 混合内容 / javascript: 伪协议注入）
        var avatarErr = ValidateAndNormalizeAvatarUrl(request.Avatar, out var normalizedAvatar);
        if (avatarErr != null)
            return (null, avatarErr);

        // 3. 手机号规范化 + 安全校验（剔除控制字符，限制长度）
        List<CustomerPhone>? normalizedPhones = null;
        if (request.Phones != null && request.Phones.Count > 0)
        {
            normalizedPhones = new List<CustomerPhone>(request.Phones.Count);
            foreach (var p in request.Phones)
            {
                var (phoneOk, normPhone, phoneErr) = ValidateAndNormalizePhone(p.Value);
                if (!phoneOk)
                    return (null, $"手机号校验失败：{phoneErr}");
                normalizedPhones.Add(new CustomerPhone { Value = normPhone!, Type = p.Type });
            }
        }

        // 4. 网址白名单协议校验：仅允许 http(s) / 相对路径
        List<string>? normalizedWebsites = null;
        if (request.Websites != null && request.Websites.Count > 0)
        {
            normalizedWebsites = new List<string>(request.Websites.Count);
            foreach (var w in request.Websites)
            {
                var (webOk, normWeb, webErr) = ValidateAndNormalizeWebsite(w);
                if (!webOk)
                    return (null, $"网址校验失败：{webErr}");
                normalizedWebsites.Add(normWeb!);
            }
        }

        var customer = new Customer
        {
            FirstName = request.FirstName,
            LastName = request.LastName,
            Address = request.Address,
            City = request.City,
            State = request.State,
            Zip = request.Zip,
            Country = request.Country,
            Company = request.Company,
            JobTitle = request.JobTitle,
            Notes = request.Notes,
            Avatar = normalizedAvatar,
            CreatedAt = DateTime.UtcNow,
            UpdatedAt = DateTime.UtcNow
        };

        if (normalizedPhones != null)
            customer.SetPhones(normalizedPhones);

        if (request.SocialProfiles != null)
        {
            customer.SetSocialProfiles(request.SocialProfiles.Select(sp => new CustomerSocialProfile
            {
                Value = sp.Value,
                Type = sp.Type
            }).ToList());
        }

        if (normalizedWebsites != null)
            customer.SetWebsites(normalizedWebsites);

        if (normalizedEmail != null)
        {
            customer.Emails.Add(new Email
            {
                EmailAddress = normalizedEmail,
                Type = 1
            });
        }

        await _unitOfWork.Customers.AddAsync(customer);
        await _unitOfWork.SaveChangesAsync();

        // 触发 Webhook 通知（失败仅记录日志，不阻塞主流程）
        if (_webhookService != null)
        {
            try
            {
                await _webhookService.TriggerAsync("customer_created", null, customer.ToResponse(0), "Customer", customer.Id);
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "触发 Webhook 失败：Event={Event}, Entity={EntityType}:{EntityId}", "customer_created", "Customer", customer.Id);
            }
        }

        return (customer.ToResponse(0), null);
    }

    public async Task<(CustomerResponse? Customer, string? ErrorMessage)> UpdateCustomerAsync(int id, CreateCustomerRequest request, string? ifMatch = null)
    {
        var customer = await _unitOfWork.Customers.GetQueryable(asNoTracking: false)
            .Include(c => c.Emails)
            .FirstOrDefaultAsync(c => c.Id == id && c.DeletedAt == null);
        if (customer == null)
        {
            return (null, null);
        }

        // TD-07 并发冲突：同客户资料被多客服同时编辑 / 渠道回调同步画像回写
        await ConcurrencyCheckHelper.CheckAsync(
            customer,
            customer.RowVersion,
            ifMatch ?? (request as UpdateCustomerRequest)?.IfMatch,
            entityType: "Customer",
            entityId: customer.Id,
            latestFactory: async () =>
            {
                var convCount = await _unitOfWork.Conversations.CountActiveByCustomerIdAsync(customer.Id).ConfigureAwait(false);
                return customer.ToResponse(convCount);
            },
            _logger);

        // 1. 邮箱规范化 + 唯一性检查（允许空字符串 = 清除邮箱）
        var (emailOk, normalizedEmail, emailErr) = ValidateAndNormalizeEmail(request.Email);
        if (!emailOk)
            return (null, emailErr);
        if (normalizedEmail != null)
        {
            var emailExists = await _unitOfWork.Customers.ExistsByEmailAsync(normalizedEmail, id);
            if (emailExists)
                return (null, "该邮箱已被使用");
        }

        customer.FirstName = request.FirstName;
        customer.LastName = request.LastName;
        customer.Address = request.Address;
        customer.City = request.City;
        customer.State = request.State;
        customer.Zip = request.Zip;
        customer.Country = request.Country;
        customer.Company = request.Company;
        customer.JobTitle = request.JobTitle;
        customer.Notes = request.Notes;

        // 头像字段：null 表示未提供（保留原值），非 null（含空字符串）表示显式更新/清除
        // 这样前端编辑其他字段时未传 avatar 不会丢失已有头像，而显式传 "" 可清除头像
        if (request.Avatar != null)
        {
            // 协议校验（防止 http 混合内容 / javascript: 伪协议注入）
            var avatarErr = ValidateAndNormalizeAvatarUrl(request.Avatar, out var normalizedAvatar);
            if (avatarErr != null)
                return (null, avatarErr);
            customer.Avatar = normalizedAvatar;
        }
        customer.UpdatedAt = DateTime.UtcNow;

        // 2. 手机号规范化 + 安全校验
        if (request.Phones != null)
        {
            var normalizedPhones = new List<CustomerPhone>(request.Phones.Count);
            foreach (var p in request.Phones)
            {
                var (phoneOk, normPhone, phoneErr) = ValidateAndNormalizePhone(p.Value);
                if (!phoneOk)
                    return (null, $"手机号校验失败：{phoneErr}");
                normalizedPhones.Add(new CustomerPhone { Value = normPhone!, Type = p.Type });
            }
            customer.SetPhones(normalizedPhones);
        }

        if (request.SocialProfiles != null)
        {
            customer.SetSocialProfiles(request.SocialProfiles.Select(sp => new CustomerSocialProfile
            {
                Value = sp.Value,
                Type = sp.Type
            }).ToList());
        }

        // 3. 网址白名单协议校验
        if (request.Websites != null)
        {
            var normalizedWebsites = new List<string>(request.Websites.Count);
            foreach (var w in request.Websites)
            {
                var (webOk, normWeb, webErr) = ValidateAndNormalizeWebsite(w);
                if (!webOk)
                    return (null, $"网址校验失败：{webErr}");
                normalizedWebsites.Add(normWeb!);
            }
            customer.SetWebsites(normalizedWebsites);
        }

        // 4. 邮箱写入（normalizedEmail == null 表示前端未传/传空 → 不主动清空；若传空串需显式保留：由 ValidateAndNormalizeEmail 返回 null）
        //   与原逻辑保持一致：仅当 request.Email 非空才写入/更新
        if (!string.IsNullOrEmpty(request.Email) && normalizedEmail != null)
        {
            var existingEmail = customer.Emails.FirstOrDefault();
            if (existingEmail != null)
            {
                existingEmail.EmailAddress = normalizedEmail;
            }
            else
            {
                customer.Emails.Add(new Email
                {
                    EmailAddress = normalizedEmail,
                    Type = 1
                });
            }
        }

        await _unitOfWork.SaveChangesAsync();

        var conversationsCount = await _unitOfWork.Conversations.CountActiveByCustomerIdAsync(id);

        // 触发 Webhook 通知（失败仅记录日志，不阻塞主流程）
        if (_webhookService != null)
        {
            try
            {
                await _webhookService.TriggerAsync("customer_updated", null, customer.ToResponse(conversationsCount), "Customer", customer.Id);
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "触发 Webhook 失败：Event={Event}, Entity={EntityType}:{EntityId}", "customer_updated", "Customer", customer.Id);
            }
        }

        return (customer.ToResponse(conversationsCount), null);
    }

    public async Task<(bool Success, string? ErrorMessage)> DeleteCustomerAsync(int id)
    {
        var customer = await _unitOfWork.Customers.GetQueryable(false)
            .FirstOrDefaultAsync(c => c.Id == id && c.DeletedAt == null);
        if (customer == null)
        {
            return (false, null);
        }

        // 删除前检查是否有关联的活跃会话
        var conversationCount = await _unitOfWork.Conversations.CountActiveByCustomerIdAsync(id);
        if (conversationCount > 0)
        {
            return (false, $"该客户关联了 {conversationCount} 个会话，请先删除或转移这些会话后再删除客户");
        }

        using (var transaction = await _unitOfWork.BeginTransactionAsync())
        {
            try
            {
                // 级联软删除客户关联的非活跃会话
                var deletedConversationCount = await _unitOfWork.Conversations.SoftDeleteByCustomerIdAsync(id);

                customer.DeletedAt = DateTime.UtcNow;
                customer.UpdatedAt = DateTime.UtcNow;
                await _unitOfWork.SaveChangesAsync();
                await transaction.CommitAsync();

                // 如果级联软删除了会话，记录批量审计日志（与 RestoreCustomerAsync 对称）
                if (deletedConversationCount > 0)
                {
                    var convNewValues = JsonSerializer.Serialize(new { CustomerId = id, Count = deletedConversationCount });
                    await _auditService.LogAsync(
                        action: "bulk_soft_delete",
                        entityType: "Conversation",
                        entityId: null,
                        oldValues: null,
                        newValues: convNewValues);
                }

                return (true, null);
            }
            catch
            {
                await transaction.SafeRollbackAsync();
                throw;
            }
        }
    }

    public async Task<PagedResponse<CustomerResponse>> GetTrashAsync(int page, int pageSize, string? query = null)
    {
        var (items, total) = await _unitOfWork.Customers.GetDeletedAsync(page, pageSize, query);
        var customerIds = items.Select(c => c.Id).ToList();
        var conversationCounts = await _unitOfWork.Conversations.GetCountByCustomerIdsAsync(customerIds);
        var responses = items.Select(c => c.ToResponse(conversationCounts.TryGetValue(c.Id, out var count) ? count : 0));
        return responses.ToPagedResponse(page, pageSize, total);
    }

    public async Task<bool> RestoreCustomerAsync(int id)
    {
        using (var transaction = await _unitOfWork.BeginTransactionAsync())
        {
            try
            {
                var restoredConversationCount = await _unitOfWork.Conversations.RestoreByCustomerIdAsync(id);
                var result = await _unitOfWork.Customers.RestoreAsync(id);
                await _unitOfWork.SaveChangesAsync();
                await transaction.CommitAsync();

                if (result)
                {
                    var newValues = JsonSerializer.Serialize(new { DeletedAt = (DateTime?)null, UpdatedAt = DateTime.UtcNow });
                    await _auditService.LogAsync(
                        action: "restore",
                        entityType: "Customer",
                        entityId: id,
                        oldValues: null,
                        newValues: newValues);
                }

                if (restoredConversationCount > 0)
                {
                    var convNewValues = JsonSerializer.Serialize(new { CustomerId = id, Count = restoredConversationCount });
                    await _auditService.LogAsync(
                        action: "bulk_restore",
                        entityType: "Conversation",
                        entityId: null,
                        oldValues: null,
                        newValues: convNewValues);
                }

                return result;
            }
            catch
            {
                await transaction.SafeRollbackAsync();
                throw;
            }
        }
    }

    public async Task<(int ActiveCount, int TrashCount)> GetHardDeleteStatusAsync(int customerId)
    {
        var activeCount = await _unitOfWork.Conversations.CountActiveByCustomerIdAsync(customerId);
        var trashCount = await _unitOfWork.Conversations.CountDeletedByCustomerIdAsync(customerId);
        return (activeCount, trashCount);
    }

    public async Task<(bool Success, string? ErrorMessage)> HardDeleteCustomerAsync(int id)
    {
        // 服务层双重校验：无论 Controller 是否加了 Attribute，这里再次强制超管校验（深度防御）
        var operatorId = _currentUserAccessor.UserId;
        if (operatorId <= 0 || !await _userRoleResolver.IsSuperAdminAsync(operatorId))
        {
            _logger.LogWarning("HardDeleteCustomerAsync 非超管用户被拦截: OperatorId={OperatorId}, CustomerId={CustomerId}", operatorId, id);
            throw new UnauthorizedAccessException("只有超级管理员才能彻底删除客户");
        }

        var customer = await _unitOfWork.Customers.GetDeletedByIdAsync(id);
        if (customer == null)
        {
            return (false, null);
        }

        // 硬删除前检查关联会话：要求用户先在会话垃圾箱中彻底删除该客户的会话
        var activeCount = await _unitOfWork.Conversations.CountActiveByCustomerIdAsync(id);
        var trashCount = await _unitOfWork.Conversations.CountDeletedByCustomerIdAsync(id);
        if (activeCount > 0 || trashCount > 0)
        {
            var parts = new List<string>();
            if (activeCount > 0)
                parts.Add($"还有 {activeCount} 条会话未删除");
            if (trashCount > 0)
                parts.Add($"垃圾箱中还有 {trashCount} 条会话未彻底删除");
            return (false, $"无法彻底删除客户：{string.Join("，", parts)}。请先到「会话垃圾箱」中彻底删除该客户的所有会话，然后再来删除客户。");
        }

        var oldValues = JsonSerializer.Serialize(new
        {
            customer.Id,
            customer.FirstName,
            customer.LastName,
            customer.DeletedAt
        });

        bool result;
        using (var transaction = await _unitOfWork.BeginTransactionAsync())
        {
            try
            {
                result = await _unitOfWork.Customers.HardDeleteAsync(id);
                await transaction.CommitAsync();
            }
            catch (DbUpdateException ex)
            {
                await transaction.SafeRollbackAsync();
                _logger.LogWarning(ex, "彻底删除客户失败，仍有关联数据未清理：CustomerId={CustomerId}", id);
                return (false, "无法彻底删除客户：该客户仍有关联数据未完全清理（如 IM 发送记录、时间跟踪等）。请先到对应模块中彻底删除该客户的关联数据，或先将关联会话彻底删除后再重试。");
            }
            catch
            {
                await transaction.SafeRollbackAsync();
                throw;
            }
        }

        if (result)
        {
            await _auditService.LogAsync(
                action: "hard_delete",
                entityType: "Customer",
                entityId: id,
                oldValues: oldValues,
                newValues: null);
        }

        return (result, null);
    }

    public async Task<(int CustomerCount, int TotalActiveCount, int TotalTrashCount)> GetClearTrashStatusAsync()
    {
        // 直接使用 CountAsync，将 3 条 SQL（IDs→ALL→SUM）降为 3 条 COUNT（纯聚合不搬数据）
        var customerCount = await _unitOfWork.Customers.GetQueryable(false)
            .IgnoreQueryFilters()
            .Where(c => c.DeletedAt != null)
            .CountAsync();

        if (customerCount == 0)
            return (0, 0, 0);

        var ids = await _unitOfWork.Customers.GetQueryable(false)
            .IgnoreQueryFilters()
            .Where(c => c.DeletedAt != null)
            .Select(c => c.Id)
            .ToListAsync();

        var activeCounts = await _unitOfWork.Conversations.CountActiveByCustomerIdsAsync(ids);
        var trashCounts = await _unitOfWork.Conversations.CountDeletedByCustomerIdsAsync(ids);
        return (customerCount, activeCounts.Values.Sum(), trashCounts.Values.Sum());
    }

    public async Task<(int Count, string? ErrorMessage)> ClearTrashAsync()
    {
        // 服务层双重校验：清空批量永久删除操作，强制超管
        var operatorId = _currentUserAccessor.UserId;
        if (operatorId <= 0 || !await _userRoleResolver.IsSuperAdminAsync(operatorId))
        {
            _logger.LogWarning("Customer ClearTrashAsync 非超管用户被拦截: OperatorId={OperatorId}", operatorId);
            throw new UnauthorizedAccessException("只有超级管理员才能清空客户垃圾箱");
        }

        // 清空垃圾箱前检查所有已删除客户的关联会话
        var (customerCount, totalActiveCount, totalTrashCount) = await GetClearTrashStatusAsync();
        if (customerCount == 0)
        {
            return (0, null);
        }
        if (totalActiveCount > 0 || totalTrashCount > 0)
        {
            var parts = new List<string>();
            if (totalActiveCount > 0)
                parts.Add($"还有 {totalActiveCount} 条会话未删除");
            if (totalTrashCount > 0)
                parts.Add($"垃圾箱中还有 {totalTrashCount} 条会话未彻底删除");
            return (0, $"无法清空垃圾箱：{string.Join("，", parts)}。请先到「会话垃圾箱」中彻底删除这些客户的所有会话，然后再来清空客户垃圾箱。");
        }

        int count;
        using (var transaction = await _unitOfWork.BeginTransactionAsync())
        {
            try
            {
                count = await _unitOfWork.Customers.HardDeleteAllDeletedAsync();
                await transaction.CommitAsync();
            }
            catch (DbUpdateException ex)
            {
                await transaction.SafeRollbackAsync();
                _logger.LogWarning(ex, "清空客户垃圾箱失败，仍有关联数据未清理");
                return (0, "无法清空客户垃圾箱：仍有客户的关联数据未完全清理（如 IM 发送记录、时间跟踪等）。请先将这些客户的所有会话彻底删除后，再重试清空垃圾箱。");
            }
            catch
            {
                await transaction.SafeRollbackAsync();
                throw;
            }
        }

        if (count > 0)
        {
            var oldValues = JsonSerializer.Serialize(new { Count = count });
            await _auditService.LogAsync(
                action: "bulk_hard_delete",
                entityType: "Customer",
                entityId: null,
                oldValues: oldValues,
                newValues: null);
        }

        return (count, null);
    }

    public async Task<BulkOperationResponse> BulkDeleteAsync(List<int> ids)
    {
        var response = new BulkOperationResponse
        {
            TotalCount = ids.Count
        };

        var distinctIds = ids.Distinct().ToList();
        if (distinctIds.Count == 0)
            return response;

        // ── 批量前置查询（2 次 SQL，替代原来 N 次循环查询） ──────────────────
        // 1. 批量取出所有未软删除的客户
        var customers = (await _unitOfWork.Customers.GetByIdsAsync(distinctIds))
            .Where(c => c.DeletedAt == null)
            .ToDictionary(c => c.Id);
        // 2. 批量统计每个客户的活跃会话数（用于前置校验）
        var activeConvCounts = await _unitOfWork.Conversations.CountActiveByCustomerIdsAsync(
            customers.Values.Select(c => c.Id));

        // 分类：可删除 vs 不可删除（不在 DB / 有活跃会话）
        var safeCustomerIds = new List<int>();
        foreach (var id in distinctIds)
        {
            if (!customers.TryGetValue(id, out _))
            {
                response.FailedCount++;
                response.Errors.Add($"客户 {id} 删除失败: 客户不存在或已删除");
                continue;
            }
            if (activeConvCounts.TryGetValue(id, out var cnt) && cnt > 0)
            {
                response.FailedCount++;
                response.Errors.Add($"客户 {id} 删除失败: 该客户关联了 {cnt} 个活跃会话，请先删除或转移后再删除客户");
                continue;
            }
            safeCustomerIds.Add(id);
        }

        if (safeCustomerIds.Count == 0)
            return response;

        // ── 单事务统一处理所有可删除客户（原来 N 次事务 → 1 次） ──────────────
        using (var transaction = await _unitOfWork.BeginTransactionAsync())
        {
            try
            {
                // 批量级联软删除会话（1 条 ExecuteUpdate 代替 N 条）
                var totalDeletedConvs = await _unitOfWork.Conversations.SoftDeleteByCustomerIdsAsync(safeCustomerIds);

                // 批量标记客户软删除：用 tracking 查询一次性取出 → 统一赋值 → SaveChanges
                var trackedCustomers = await _unitOfWork.Customers.GetQueryable(asNoTracking: false)
                    .Where(c => safeCustomerIds.Contains(c.Id) && c.DeletedAt == null)
                    .ToListAsync();
                var now = DateTime.UtcNow;
                foreach (var c in trackedCustomers)
                {
                    c.DeletedAt = now;
                    c.UpdatedAt = now;
                }
                await _unitOfWork.SaveChangesAsync();
                await transaction.CommitAsync();

                response.SuccessCount = trackedCustomers.Count;
                // 未被 tracking 查询命中的 id 视为失败（竞态条件下被其他请求先删了）
                foreach (var id in safeCustomerIds.Except(trackedCustomers.Select(c => c.Id)))
                {
                    response.SuccessCount--;
                    response.FailedCount++;
                    response.Errors.Add($"客户 {id} 删除失败: 客户不存在或已删除");
                }

                // 合并写一条聚合审计日志（替代 N 条明细，与 DeleteCustomerAsync 行为保持一致但更高效）
                if (totalDeletedConvs > 0)
                {
                    var convNewValues = JsonSerializer.Serialize(new { CustomerIds = safeCustomerIds, Count = totalDeletedConvs });
                    await _auditService.LogAsync(
                        action: "bulk_soft_delete",
                        entityType: "Conversation",
                        entityId: null,
                        oldValues: null,
                        newValues: convNewValues);
                }
            }
            catch (Exception ex)
            {
                await transaction.SafeRollbackAsync();
                // 事务失败：所有 safeCustomerIds 都视为失败
                _logger.LogError(ex, "批量删除客户事务失败: CustomerIds={CustomerIds}", string.Join(",", safeCustomerIds));
                response.SuccessCount = 0;
                foreach (var id in safeCustomerIds)
                {
                    response.FailedCount++;
                    response.Errors.Add($"客户 {id} 删除失败: 服务器内部错误，详见系统日志");
                }
            }
        }

        return response;
    }

    public async Task<(byte[] Content, string FileName)> ExportAsync(List<int>? ids = null)
    {
        var sb = new System.Text.StringBuilder();
        sb.AppendLine("ID,姓名,邮箱,公司,职位,城市,国家,电话,备注,创建时间");

        if (ids != null && ids.Count > 0)
        {
            // 指定 ID 导出：ID 数量通常有限，直接批量查询；若 ID 数量过大也做分批
            const int idBatchSize = 1000;
            for (int i = 0; i < ids.Count; i += idBatchSize)
            {
                var batchIds = ids.Skip(i).Take(idBatchSize).ToList();
                var customers = await _unitOfWork.Customers.GetByIdsAsync(batchIds);
                foreach (var customer in customers)
                {
                    AppendCustomerRow(sb, customer);
                }
            }
        }
        else
        {
            // 全量导出：分页逐批加载，避免一次性加载大列表导致内存溢出
            int page = 1;
            const int pageSize = 1000;
            while (true)
            {
                var (items, total) = await _unitOfWork.Customers.SearchAsync("", page, pageSize);
                foreach (var customer in items)
                {
                    AppendCustomerRow(sb, customer);
                }
                // 当前页加载不满 或 已达到总数，退出
                if (items.Count() < pageSize || page * pageSize >= total)
                    break;
                page++;
            }
        }

        var content = System.Text.Encoding.UTF8.GetPreamble()
            .Concat(System.Text.Encoding.UTF8.GetBytes(sb.ToString())).ToArray();
        var fileName = $"customers_export_{DateTime.UtcNow:yyyyMMddHHmmss}.csv";
        return (content, fileName);
    }

    private static void AppendCustomerRow(System.Text.StringBuilder sb, Customer c)
    {
        var phones = c.GetPhones();
        var phoneStr = phones.Count > 0 ? phones[0].Value : "";
        var email = c.Emails.FirstOrDefault()?.EmailAddress ?? "";
        sb.AppendLine($"{c.Id},{EscapeCsv(c.GetFullName())},{EscapeCsv(email)},{EscapeCsv(c.Company ?? "")},{EscapeCsv(c.JobTitle ?? "")},{EscapeCsv(c.City ?? "")},{EscapeCsv(c.Country ?? "")},{EscapeCsv(phoneStr)},{EscapeCsv(c.Notes ?? "")},{c.CreatedAt:yyyy-MM-dd HH:mm:ss}");
    }

    private static string EscapeCsv(string field)
    {
        if (field.Contains(',') || field.Contains('"') || field.Contains('\n'))
        {
            return $"\"{field.Replace("\"", "\"\"")}\"";
        }
        return field;
    }
}
