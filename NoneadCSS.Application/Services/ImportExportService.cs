using System.Text.Json;
using System.Text.Json.Nodes;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;
using NoneadCSS.Application.Common;
using NoneadCSS.Application.DTOs.Requests;
using NoneadCSS.Application.DTOs.Responses;
using NoneadCSS.Domain;
using NoneadCSS.Domain.Entities;

namespace NoneadCSS.Application.Services;

public class ImportExportService : IImportExportService
{
    private readonly IUnitOfWork _unitOfWork;
    private readonly ILogger<ImportExportService> _logger;
    private readonly ICurrentUserAccessor _currentUserAccessor;
    private readonly IUserRoleResolver _userRoleResolver;
    private readonly ILocalizationService? _localization;

    public ImportExportService(
        IUnitOfWork unitOfWork,
        ILogger<ImportExportService> logger,
        ICurrentUserAccessor currentUserAccessor,
        IUserRoleResolver userRoleResolver,
        ILocalizationService? localization = null)
    {
        _unitOfWork = unitOfWork;
        _logger = logger;
        _currentUserAccessor = currentUserAccessor;
        _userRoleResolver = userRoleResolver;
        _localization = localization;
    }

    /// <summary>
    /// 可导入/导出的实体目录。Name/Description 是 API 可见的选项目录，必须按请求文化取值：
    /// 英文界面下前端虽然自带词条（<c>data.importExport.entity.*</c>），但直接调用 API 的使用者
    /// 会拿到中文，因此这里按 <c>ImportEntity_&lt;type&gt;</c> / <c>ImportEntityDesc_&lt;type&gt;</c>
    /// 取词典值，取不到时回落中文原句（中文侧必须与改动前逐字一致）。
    /// </summary>
    public Task<List<EntityTypeResponse>> GetAvailableEntitiesAsync()
    {
        var entities = new List<EntityTypeResponse>
        {
            new() { Type = "customers", Name = Tr("ImportEntity_customers", "客户"), Description = Tr("ImportEntityDesc_customers", "客户联系人信息"), SupportsExport = true, SupportsImport = true },
            new() { Type = "tags", Name = Tr("ImportEntity_tags", "标签"), Description = Tr("ImportEntityDesc_tags", "会话标签"), SupportsExport = true, SupportsImport = true },
            new() { Type = "canned_responses", Name = Tr("ImportEntity_canned_responses", "快捷回复"), Description = Tr("ImportEntityDesc_canned_responses", "快捷回复模板"), SupportsExport = true, SupportsImport = true },
            new() { Type = "mail_templates", Name = Tr("ImportEntity_mail_templates", "邮件模板"), Description = Tr("ImportEntityDesc_mail_templates", "邮件通知模板"), SupportsExport = true, SupportsImport = true },
            new() { Type = "email_signatures", Name = Tr("ImportEntity_email_signatures", "邮件签名"), Description = Tr("ImportEntityDesc_email_signatures", "邮箱签名"), SupportsExport = true, SupportsImport = true },
            new() { Type = "conversations", Name = Tr("ImportEntity_conversations", "工单"), Description = Tr("ImportEntityDesc_conversations", "工单列表（不含正文与附件）"), SupportsExport = true, SupportsImport = false },
            new() { Type = "knowledge_articles", Name = Tr("ImportEntity_knowledge_articles", "知识库文章"), Description = Tr("ImportEntityDesc_knowledge_articles", "文章标题/分类/状态/正文（Markdown 文本）"), SupportsExport = true, SupportsImport = false },
            new() { Type = "users", Name = Tr("ImportEntity_users", "用户"), Description = Tr("ImportEntityDesc_users", "系统用户"), SupportsExport = true, SupportsImport = false },
        };
        return Task.FromResult(entities);
    }

    /// <summary>按键取文案；词典缺失（返回值等于键本身）或没有注入本地化服务时回落中文原句。</summary>
    private string Tr(string key, string zhFallback)
    {
        if (_localization is null) return zhFallback;
        var value = _localization[key];
        return string.IsNullOrWhiteSpace(value) || value == key ? zhFallback : value;
    }

    public async Task<byte[]> ExportDataAsync(ExportRequest request)
    {
        var exportData = new JsonObject();

        foreach (var entityType in request.EntityTypes)
        {
            switch (entityType.ToLower())
            {
                case "customers":
                    // BUG-02 修复：使用 GetQueryable 过滤软删除记录 + 不截断到 100 条
                    var customers = await _unitOfWork.Customers.GetQueryable().AsNoTracking()
                        .Where(c => c.DeletedAt == null)
                        .Include(c => c.Emails)
                        .Take(10000)
                        .ToListAsync();
                    var customersArray = new JsonArray();
                    foreach (var c in customers)
                    {
                        var customerObj = new JsonObject
                        {
                            ["id"] = c.Id,
                            ["firstName"] = c.FirstName,
                            ["lastName"] = c.LastName,
                            ["avatar"] = c.Avatar,
                            ["company"] = c.Company,
                            ["address"] = c.Address,
                            ["city"] = c.City,
                            ["state"] = c.State,
                            ["zip"] = c.Zip,
                            ["country"] = c.Country,
                            ["jobTitle"] = c.JobTitle,
                            ["notes"] = c.Notes,
                            ["phones"] = c.Phones,
                            ["websites"] = c.Websites,
                            ["createdAt"] = c.CreatedAt,
                            ["updatedAt"] = c.UpdatedAt,
                            ["emails"] = new JsonArray()
                        };
                        if (c.Emails != null)
                        {
                            foreach (var e in c.Emails)
                            {
                                ((JsonArray)customerObj["emails"]!).Add(new JsonObject
                                {
                                    ["emailAddress"] = e.EmailAddress
                                });
                            }
                        }
                        customersArray.Add(customerObj);
                    }
                    exportData["customers"] = customersArray;
                    break;

                case "tags":
                    // BUG-02 修复：过滤软删除记录
                    var tags = await _unitOfWork.Tags.GetQueryable().AsNoTracking()
                        .Where(t => t.DeletedAt == null)
                        .Take(10000)
                        .ToListAsync();
                    var tagsArray = new JsonArray();
                    foreach (var t in tags)
                    {
                        tagsArray.Add(new JsonObject
                        {
                            ["id"] = t.Id,
                            ["mailboxId"] = t.MailboxId,
                            ["name"] = t.Name,
                            ["color"] = t.Color,
                            ["createdAt"] = t.CreatedAt,
                            ["updatedAt"] = t.UpdatedAt
                        });
                    }
                    exportData["tags"] = tagsArray;
                    break;

                case "canned_responses":
                    // BUG-02 修复：过滤软删除记录
                    var responses = await _unitOfWork.CannedResponses.GetQueryable().AsNoTracking()
                        .Where(r => r.DeletedAt == null)
                        .Take(10000)
                        .ToListAsync();
                    var responsesArray = new JsonArray();
                    foreach (var r in responses)
                    {
                        responsesArray.Add(new JsonObject
                        {
                            ["id"] = r.Id,
                            ["mailboxId"] = r.MailboxId,
                            ["name"] = r.Name,
                            ["body"] = r.Body,
                            ["category"] = r.Category,
                            ["sortOrder"] = r.SortOrder,
                            ["isShared"] = r.IsShared,
                            ["createdByUserId"] = r.CreatedByUserId,
                            ["createdAt"] = r.CreatedAt,
                            ["updatedAt"] = r.UpdatedAt
                        });
                    }
                    exportData["canned_responses"] = responsesArray;
                    break;

                case "mail_templates":
                    // BUG-02 修复：过滤软删除记录
                    var templates = await _unitOfWork.MailTemplates.GetQueryable().AsNoTracking()
                        .Where(t => t.DeletedAt == null)
                        .ToListAsync();
                    var templatesArray = new JsonArray();
                    foreach (var t in templates)
                    {
                        templatesArray.Add(new JsonObject
                        {
                            ["id"] = t.Id,
                            ["name"] = t.Name,
                            ["slug"] = t.Slug,
                            ["type"] = (int)t.Type,
                            ["subject"] = t.Subject,
                            ["bodyHtml"] = t.BodyHtml,
                            ["bodyText"] = t.BodyText,
                            ["variablesJson"] = t.VariablesJson,
                            ["isActive"] = t.IsActive,
                            ["isSystem"] = t.IsSystem,
                            ["mailboxId"] = t.MailboxId,
                            ["description"] = t.Description,
                            ["sortOrder"] = t.SortOrder,
                            ["createdAt"] = t.CreatedAt,
                            ["updatedAt"] = t.UpdatedAt
                        });
                    }
                    exportData["mail_templates"] = templatesArray;
                    break;

                case "email_signatures":
                    // BUG-02 修复：过滤软删除记录
                    var signatures = await _unitOfWork.EmailSignatures.GetQueryable().AsNoTracking()
                        .Where(s => s.DeletedAt == null)
                        .Take(10000)
                        .ToListAsync();
                    var signaturesArray = new JsonArray();
                    foreach (var s in signatures)
                    {
                        signaturesArray.Add(new JsonObject
                        {
                            ["id"] = s.Id,
                            ["userId"] = s.UserId,
                            ["mailboxId"] = s.MailboxId,
                            ["name"] = s.Name,
                            ["body"] = s.Body,
                            ["isDefault"] = s.IsDefault,
                            ["sortOrder"] = s.SortOrder,
                            ["createdAt"] = s.CreatedAt,
                            ["updatedAt"] = s.UpdatedAt
                        });
                    }
                    exportData["email_signatures"] = signaturesArray;
                    break;

                case "conversations":
                {
                    // 工单导出（#14）：只导元数据，不含正文与附件——
                    // 1) 正文体积可能极大；2) 正文涉及客户隐私；
                    // 3) 只读导出，不参与导入（导入会绕过邮件摄取与工作流触发）。
                    var conversations = await _unitOfWork.Conversations.GetQueryable().AsNoTracking()
                        .Where(c => c.DeletedAt == null)
                        .OrderByDescending(c => c.CreatedAt)
                        .Take(10000)
                        .ToListAsync();

                    var conversationsArray = new JsonArray();
                    foreach (var c in conversations)
                    {
                        conversationsArray.Add(new JsonObject
                        {
                            ["id"] = c.Id,
                            ["subject"] = c.Subject,
                            ["status"] = (int)c.Status,
                            ["priority"] = (int)c.Priority,
                            ["customerEmail"] = c.CustomerEmail,
                            ["mailboxId"] = c.MailboxId,
                            ["assigneeId"] = c.UserId,
                            ["createdAt"] = c.CreatedAt,
                            ["updatedAt"] = c.UpdatedAt
                        });
                    }
                    exportData["conversations"] = conversationsArray;
                    break;
                }

                case "knowledge_articles":
                {
                    // 知识库导出（#14）：文章天然是文档，连正文一起导，便于整体迁移到别的知识库。
                    var articles = await _unitOfWork.Repository<KnowledgeArticle>().GetQueryable()
                        .Where(a => a.DeletedAt == null)
                        .OrderBy(a => a.Id)
                        .Take(10000)
                        .ToListAsync();

                    var articlesArray = new JsonArray();
                    foreach (var a in articles)
                    {
                        articlesArray.Add(new JsonObject
                        {
                            ["id"] = a.Id,
                            ["title"] = a.Title,
                            ["categoryId"] = a.CategoryId,
                            ["status"] = a.Status,
                            ["content"] = a.Content,
                            ["createdAt"] = a.CreatedAt,
                            ["updatedAt"] = a.UpdatedAt
                        });
                    }
                    exportData["knowledge_articles"] = articlesArray;
                    break;
                }

                case "users":
                {
                    var operatorId = _currentUserAccessor.UserId;
                    if (operatorId <= 0 || !await _userRoleResolver.IsSuperAdminAsync(operatorId))
                    {
                        throw new UnauthorizedAccessException("导出用户数据需要超级管理员权限");
                    }

                    var users = (await _unitOfWork.Users.GetAllAsync()).Take(10000);
                    var usersArray = new JsonArray();
                    foreach (var u in users)
                    {
                        usersArray.Add(new JsonObject
                        {
                            ["id"] = u.Id,
                            ["firstName"] = u.FirstName,
                            ["lastName"] = u.LastName,
                            ["email"] = u.Email,
                            ["status"] = (int)u.Status,
                            ["role"] = (int)u.Role,
                            ["timezone"] = u.Timezone,
                            ["locale"] = u.Locale,
                            ["createdAt"] = u.CreatedAt,
                            ["updatedAt"] = u.UpdatedAt
                        });
                    }
                    exportData["users"] = usersArray;
                    break;
                }
            }
        }

        var json = exportData.ToJsonString(new JsonSerializerOptions
        {
            WriteIndented = true,
            Encoder = System.Text.Encodings.Web.JavaScriptEncoder.UnsafeRelaxedJsonEscaping
        });
        return System.Text.Encoding.UTF8.GetBytes(json);
    }

    public async Task<ImportPreviewResponse> PreviewImportAsync(ImportPreviewRequest request)
    {
        var response = new ImportPreviewResponse();

        // 限制 JSON 导入数据大小为 5MB，防止超大 payload 导致内存耗尽
        const int MaxImportSizeBytes = 5 * 1024 * 1024;
        if (!string.IsNullOrEmpty(request.Data) && System.Text.Encoding.UTF8.GetByteCount(request.Data) > MaxImportSizeBytes)
        {
            response.Valid = false;
            response.Errors.Add("导入数据超过 5MB 限制");
            return response;
        }

        try
        {
            var data = JsonSerializer.Deserialize<Dictionary<string, JsonElement>>(request.Data);
            if (data == null)
            {
                response.Valid = false;
                response.Errors.Add("无效的JSON数据");
                return response;
            }

            foreach (var kvp in data)
            {
                var entityType = kvp.Key;
                var items = kvp.Value.EnumerateArray();
                var count = items.Count();

                response.Entities.Add(new ImportEntityPreview
                {
                    Type = entityType,
                    Count = count,
                    SampleData = items.Take(3).Select(i => i.ToString()).ToList()
                });
            }

            response.Valid = true;
        }
        catch (JsonException ex)
        {
            _logger.LogError(ex, "导入预览 JSON 解析失败");
            response.Valid = false;
            response.Errors.Add("JSON 解析错误: 文件格式无效，详见系统日志");
        }

        return await Task.FromResult(response);
    }

    public async Task<ImportResponse> ImportDataAsync(ImportRequest request)
    {
        var response = new ImportResponse();
        var errors = new List<string>();
        int importedCount = 0;
        int failedCount = 0;

        // 限制 JSON 导入数据大小为 5MB，防止超大 payload 导致内存耗尽
        const int MaxImportSizeBytes = 5 * 1024 * 1024;
        if (!string.IsNullOrEmpty(request.Data) && System.Text.Encoding.UTF8.GetByteCount(request.Data) > MaxImportSizeBytes)
        {
            response.Success = false;
            response.Errors.Add("导入数据超过 5MB 限制");
            return response;
        }

        using var transaction = await _unitOfWork.BeginTransactionAsync();
        try
        {
            var data = JsonSerializer.Deserialize<Dictionary<string, JsonElement>>(request.Data);
            if (data == null)
            {
                response.Success = false;
                response.Errors.Add("无效的JSON数据");
                return response;
            }

            // 预加载现有数据用于重复检查（避免 N+1 查询）
            var existingTagKeys = new HashSet<string>();
            var existingCannedResponseNames = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
            var existingMailTemplateSlugs = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
            var existingEmailSignatureKeys = new HashSet<string>();
            var existingCustomerEmails = new HashSet<string>(StringComparer.OrdinalIgnoreCase);

            foreach (var kvp in data)
            {
                switch (kvp.Key.ToLower())
                {
                    case "tags":
                        // BUG-08 修复：重复检查必须包含软删除记录。
                        // 之前 BUG-02 在此处过滤了 DeletedAt == null，导致：
                        //   - 导入环境已存在被软删除的同名标签（DeletedAt != null）
                        //   - 重复检查集合里查不到该 key → 误判为"不存在"
                        //   - ImportTag 创建一条新的同名标签 → DB 出现两条同名记录
                        //     （一条软删除 + 一条活跃），后续按 Name 查询的代码会出现歧义。
                        // 正确做法：将所有标签（含软删除）加载到 existingTagKeys，
                        // 若同名 key 已存在（无论是否软删除），ImportTag 直接跳过，
                        // 避免产生重复记录。如需"恢复"软删除标签，应通过专门的恢复接口，
                        // 而非借助导入流程隐式完成。
                        // 注意：Tag 实体实现了 ISoftDelete，DbContext 在 OnModelCreating 中
                        // 通过 ApplyGlobalSoftDeleteFilters 自动应用 HasQueryFilter(e => e.DeletedAt == null)，
                        // 因此必须调用 IgnoreQueryFilters() 才能查询到软删除记录。
                        var allTags = await _unitOfWork.Tags.GetQueryable().AsNoTracking()
                            .IgnoreQueryFilters()
                            .ToListAsync();
                        foreach (var t in allTags)
                        {
                            existingTagKeys.Add($"{t.MailboxId}_{t.Name}");
                        }
                        break;
                    case "canned_responses":
                        var allCanned = await _unitOfWork.CannedResponses.GetQueryable().AsNoTracking()
                            .Where(r => r.DeletedAt == null).ToListAsync();
                        foreach (var r in allCanned)
                        {
                            existingCannedResponseNames.Add(r.Name);
                        }
                        break;
                    case "mail_templates":
                        var allTemplates = await _unitOfWork.MailTemplates.GetQueryable().AsNoTracking()
                            .Where(t => t.DeletedAt == null).ToListAsync();
                        foreach (var t in allTemplates)
                        {
                            existingMailTemplateSlugs.Add(t.Slug);
                        }
                        break;
                    case "email_signatures":
                        var allSigs = await _unitOfWork.EmailSignatures.GetQueryable().AsNoTracking()
                            .Where(s => s.DeletedAt == null).ToListAsync();
                        foreach (var s in allSigs)
                        {
                            existingEmailSignatureKeys.Add($"{s.UserId}_{s.Name}");
                        }
                        break;
                    case "customers":
                        // BUG-01 修复：使用专用方法获取所有未删除客户的邮箱（无 Take 限制）
                        existingCustomerEmails = await _unitOfWork.Customers.GetAllActiveEmailsAsync();
                        break;
                }
            }

            foreach (var kvp in data)
            {
                var entityType = kvp.Key;
                var items = kvp.Value.EnumerateArray().ToList();

                foreach (var item in items)
                {
                    try
                    {
                        switch (entityType.ToLower())
                        {
                            case "customers":
                                await ImportCustomer(item, existingCustomerEmails);
                                break;

                            case "tags":
                                await ImportTag(item, existingTagKeys);
                                break;

                            case "canned_responses":
                                await ImportCannedResponse(item, existingCannedResponseNames);
                                break;

                            case "mail_templates":
                                await ImportMailTemplate(item, existingMailTemplateSlugs);
                                break;

                            case "email_signatures":
                                await ImportEmailSignature(item, existingEmailSignatureKeys);
                                break;
                        }
                        importedCount++;
                    }
                    catch (Exception ex)
                    {
                        failedCount++;
                        errors.Add($"{entityType}: {ex.Message}");
                    }
                }
            }

            await _unitOfWork.SaveChangesAsync();
            await transaction.CommitAsync();

            response.Success = true;
            response.TotalCount = importedCount + failedCount;
            response.ImportedCount = importedCount;
            response.FailedCount = failedCount;
            response.Errors = errors;
        }
        catch (Exception ex)
        {
            await transaction.SafeRollbackAsync();
            _logger.LogError(ex, "数据导入失败");
            response.Success = false;
            response.Errors.Add("导入失败: 服务器内部错误，详见系统日志");
        }

        return response;
    }

    private async Task ImportCustomer(JsonElement item, HashSet<string> existingEmails)
    {
        var firstName = item.TryGetProperty("firstName", out var fnElement) ? fnElement.GetString() ?? string.Empty : string.Empty;
        var lastName = item.TryGetProperty("lastName", out var lnElement) ? lnElement.GetString() ?? string.Empty : string.Empty;
        var avatar = item.TryGetProperty("avatar", out var avElement) ? avElement.GetString() ?? string.Empty : string.Empty;
        var company = item.TryGetProperty("company", out var companyElement) ? companyElement.GetString() : null;
        var address = item.TryGetProperty("address", out var addressElement) ? addressElement.GetString() : null;
        var city = item.TryGetProperty("city", out var cityElement) ? cityElement.GetString() : null;
        var state = item.TryGetProperty("state", out var stateElement) ? stateElement.GetString() : null;
        var zip = item.TryGetProperty("zip", out var zipElement) ? zipElement.GetString() : null;
        var country = item.TryGetProperty("country", out var countryElement) ? countryElement.GetString() : null;
        var jobTitle = item.TryGetProperty("jobTitle", out var jobTitleElement) ? jobTitleElement.GetString() : null;
        var notes = item.TryGetProperty("notes", out var notesElement) ? notesElement.GetString() : null;
        var phones = item.TryGetProperty("phones", out var phonesElement) ? phonesElement.GetString() : null;
        var websites = item.TryGetProperty("websites", out var websitesElement) ? websitesElement.GetString() : null;

        string? existingEmail = null;
        List<Email> emailsToAdd = new();
        if (item.TryGetProperty("emails", out var emailsElement) && emailsElement.ValueKind == JsonValueKind.Array)
        {
            var emailsArray = emailsElement.EnumerateArray().ToList();
            if (emailsArray.Any())
            {
                var firstEmail = emailsArray[0];
                if (firstEmail.TryGetProperty("emailAddress", out var emailElement))
                {
                    existingEmail = emailElement.GetString();
                }

                foreach (var emailItem in emailsArray)
                {
                    if (emailItem.TryGetProperty("emailAddress", out var eaElement))
                    {
                        var emailAddr = eaElement.GetString();
                        if (!string.IsNullOrEmpty(emailAddr))
                        {
                            var emailType = 1;
                            if (emailItem.TryGetProperty("type", out var typeElement))
                            {
                                int.TryParse(typeElement.GetString(), out emailType);
                            }
                            emailsToAdd.Add(new Email { EmailAddress = emailAddr, Type = emailType });
                        }
                    }
                }
            }
        }

        if (!string.IsNullOrEmpty(existingEmail))
        {
            if (existingEmails.Contains(existingEmail)) return;
            existingEmails.Add(existingEmail);
        }

        var customer = new Customer
        {
            FirstName = firstName,
            LastName = lastName,
            Avatar = avatar,
            Company = company,
            Address = address,
            City = city,
            State = state,
            Zip = zip,
            Country = country,
            JobTitle = jobTitle,
            Notes = notes,
            Phones = phones ?? string.Empty,
            Websites = websites ?? string.Empty,
            CreatedAt = DateTime.UtcNow,
            UpdatedAt = DateTime.UtcNow
        };

        foreach (var email in emailsToAdd)
        {
            customer.Emails.Add(email);
        }

        await _unitOfWork.Customers.AddAsync(customer);
    }

    private async Task ImportTag(JsonElement item, HashSet<string> existingKeys)
    {
        var name = item.GetProperty("name").GetString() ?? string.Empty;
        var color = item.GetProperty("color").GetString() ?? "#00b7ff";
        var mailboxId = item.TryGetProperty("mailboxId", out var mbId) && mbId.ValueKind != JsonValueKind.Null
            ? mbId.GetInt32()
            : (int?)null;

        var key = $"{mailboxId}_{name}";
        if (existingKeys.Contains(key)) return;
        existingKeys.Add(key);

        var tag = new Tag
        {
            Name = name,
            Color = color,
            MailboxId = mailboxId,
            CreatedAt = DateTime.UtcNow,
            UpdatedAt = DateTime.UtcNow
        };

        await _unitOfWork.Tags.AddAsync(tag);
    }

    private async Task ImportCannedResponse(JsonElement item, HashSet<string> existingNames)
    {
        var name = item.GetProperty("name").GetString() ?? string.Empty;
        var body = HtmlSanitizerHelper.Sanitize(item.GetProperty("body").GetString() ?? string.Empty);

        if (existingNames.Contains(name)) return;
        existingNames.Add(name);

        var response = new CannedResponse
        {
            Name = name,
            Body = body,
            Category = item.TryGetProperty("category", out var cat) ? cat.GetString() : null,
            SortOrder = item.TryGetProperty("sortOrder", out var sort) ? sort.GetInt32() : 0,
            IsShared = item.TryGetProperty("isShared", out var shared) ? shared.GetBoolean() : true,
            MailboxId = item.TryGetProperty("mailboxId", out var mbId) && mbId.ValueKind != JsonValueKind.Null
                ? mbId.GetInt32()
                : (int?)null,
            CreatedAt = DateTime.UtcNow,
            UpdatedAt = DateTime.UtcNow
        };

        await _unitOfWork.CannedResponses.AddAsync(response);
    }

    private async Task ImportMailTemplate(JsonElement item, HashSet<string> existingSlugs)
    {
        var slug = item.GetProperty("slug").GetString() ?? string.Empty;

        if (existingSlugs.Contains(slug)) return;
        existingSlugs.Add(slug);

        var template = new MailTemplate
        {
            Name = item.GetProperty("name").GetString() ?? string.Empty,
            Slug = slug,
            Subject = item.GetProperty("subject").GetString() ?? string.Empty,
            BodyHtml = HtmlSanitizerHelper.Sanitize(item.GetProperty("bodyHtml").GetString() ?? string.Empty),
            BodyText = item.GetProperty("bodyText").GetString() ?? string.Empty,
            VariablesJson = item.TryGetProperty("variablesJson", out var vars) ? vars.GetString() : null,
            IsActive = item.TryGetProperty("isActive", out var active) ? active.GetBoolean() : true,
            IsSystem = item.TryGetProperty("isSystem", out var system) ? system.GetBoolean() : false,
            MailboxId = item.TryGetProperty("mailboxId", out var mbId) && mbId.ValueKind != JsonValueKind.Null
                ? mbId.GetInt32()
                : (int?)null,
            Description = item.TryGetProperty("description", out var desc) ? desc.GetString() : null,
            SortOrder = item.TryGetProperty("sortOrder", out var sort) ? sort.GetInt32() : 0,
            CreatedAt = DateTime.UtcNow,
            UpdatedAt = DateTime.UtcNow
        };

        await _unitOfWork.MailTemplates.AddAsync(template);
    }

    private async Task ImportEmailSignature(JsonElement item, HashSet<string> existingKeys)
    {
        var name = item.GetProperty("name").GetString() ?? string.Empty;
        var userId = item.GetProperty("userId").GetInt32();

        var key = $"{userId}_{name}";
        if (existingKeys.Contains(key)) return;
        existingKeys.Add(key);

        var signature = new EmailSignature
        {
            UserId = userId,
            Name = name,
            Body = HtmlSanitizerHelper.Sanitize(item.GetProperty("body").GetString() ?? string.Empty),
            IsDefault = item.TryGetProperty("isDefault", out var def) ? def.GetBoolean() : false,
            SortOrder = item.TryGetProperty("sortOrder", out var sort) ? sort.GetInt32() : 0,
            MailboxId = item.TryGetProperty("mailboxId", out var mbId) && mbId.ValueKind != JsonValueKind.Null
                ? mbId.GetInt32()
                : (int?)null,
            CreatedAt = DateTime.UtcNow,
            UpdatedAt = DateTime.UtcNow
        };

        await _unitOfWork.EmailSignatures.AddAsync(signature);
    }
}
