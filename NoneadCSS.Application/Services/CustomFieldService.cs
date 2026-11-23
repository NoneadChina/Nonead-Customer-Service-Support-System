using System.Text.Json;
using NoneadCSS.Application.DTOs.Responses;
using NoneadCSS.Domain;
using NoneadCSS.Domain.Entities;
using Microsoft.EntityFrameworkCore;

namespace NoneadCSS.Application.Services;

public class CustomFieldService : ICustomFieldService
{
    private readonly IUnitOfWork _unitOfWork;
    private readonly ICurrentUserAccessor _currentUserAccessor;
    private readonly ILocalizationService? _localization;

    /// <summary>已知渠道清单（SourceType 枚举名小写 → zh-Hans 显示名）。用于字段管理页下拉和列表标签。
    /// 字典值保持语言中立（zh-Hans 原文，仅作回退源），展示时按 <c>Channel_&lt;key&gt;</c> 词条本地化。</summary>
    private static readonly Dictionary<string, string> KnownChannels = new(StringComparer.OrdinalIgnoreCase)
    {
        { "email", "邮件" },
        { "web", "网页" },
        { "api", "API" },
        { "phone", "电话" },
        { "chat", "在线聊天" },
        { "social", "社交" },
        { "wechat", "微信" },
        { "whatsapp", "WhatsApp" },
        { "feishu", "飞书" },
        { "dingtalk", "钉钉" },
        { "messenger", "Messenger" },
        { "telegram", "Telegram" },
        { "line", "LINE" },
        { "imessage", "iMessage" },
    };

    /// <summary>需查询 SystemOptions 启用状态的 IM 渠道（其他如 email/web/chat 默认始终启用）。</summary>
    private static readonly Dictionary<string, string> ImChannelOptionKeys = new(StringComparer.OrdinalIgnoreCase)
    {
        { "wechat", "wechat.enabled" },
        { "whatsapp", "whatsapp.enabled" },
        { "feishu", "feishu.enabled" },
        { "dingtalk", "dingding.enabled" },
        { "messenger", "messenger.enabled" },
        { "telegram", "telegram.enabled" },
        { "line", "line.enabled" },
        { "imessage", "imessage.enabled" },
    };

    public CustomFieldService(IUnitOfWork unitOfWork, ICurrentUserAccessor currentUserAccessor, ILocalizationService? localization = null)
    {
        _unitOfWork = unitOfWork;
        _currentUserAccessor = currentUserAccessor;
        _localization = localization;
    }

    public async Task<CustomFieldDto?> GetByIdAsync(int id)
    {
        var field = await _unitOfWork.CustomFields.GetByIdAsync(id);
        if (field == null) return null;

        if (!await _currentUserAccessor.CanAccessMailboxAsync(field.MailboxId))
            return null;

        var dto = MapToDto(field);
        // 字段详情也填充禁用渠道，方便编辑器提示
        var channelStatus = await GetChannelStatusMapAsync();
        dto.DisabledChannels = ComputeDisabledChannels(field, channelStatus);
        return dto;
    }

    public async Task<List<CustomFieldDto>> GetActiveForEntityAsync(string entityType, string? sourceType = null)
    {
        var fields = await _unitOfWork.CustomFields.GetActiveForEntityAsync(entityType, sourceType);
        var channelStatus = await GetChannelStatusMapAsync();

        // 应用 HideWhenChannelDisabled 过滤：字段绑定的渠道全部关闭时，且开启自动隐藏，则不返回
        var filtered = fields.Where(f => !ShouldHideField(f, channelStatus)).ToList();

        return filtered.Select(f =>
        {
            var dto = MapToDto(f);
            // 实体场景不需要 DisabledChannels（用户只是填值，不关心渠道开关）
            return dto;
        }).ToList();
    }

    /// <summary>
    /// 判断字段是否应当被隐藏：
    /// - 字段未开 HideWhenChannelDisabled → 不隐藏
    /// - 字段为全局字段（SourceTypesJson 为 null/空）→ 不隐藏
    /// - 字段绑定的渠道中至少有一个启用 → 不隐藏
    /// - 字段绑定的渠道全部关闭 → 隐藏
    /// </summary>
    private static bool ShouldHideField(CustomField field, Dictionary<string, bool> channelStatus)
    {
        if (!field.HideWhenChannelDisabled) return false;
        var sourceTypes = DeserializeSourceTypes(field.SourceTypesJson);
        if (sourceTypes.Count == 0) return false; // 全局字段不隐藏

        foreach (var st in sourceTypes)
        {
            if (channelStatus.TryGetValue(st, out var enabled) && enabled)
            {
                return false; // 至少一个渠道启用，不隐藏
            }
        }
        return true; // 全部关闭
    }

    /// <summary>读取各渠道启用状态。IM 渠道查 SystemOptions，其他渠道默认启用。</summary>
    private async Task<Dictionary<string, bool>> GetChannelStatusMapAsync()
    {
        var result = new Dictionary<string, bool>(StringComparer.OrdinalIgnoreCase);
        foreach (var kv in KnownChannels)
        {
            result[kv.Key] = true; // 默认启用
        }

        // IM 渠道查 SystemOptions 表（批量查询，避免逐渠道 N+1）
        var keys = ImChannelOptionKeys.Values.ToList();
        var values = await _unitOfWork.SystemOptions.GetValuesByKeysAsync(keys);
        foreach (var kv in ImChannelOptionKeys)
        {
            var valueStr = values.TryGetValue(kv.Value, out var v) ? v : null;
            if (bool.TryParse(valueStr, out var enabled))
            {
                result[kv.Key] = enabled;
            }
        }
        return result;
    }

    /// <summary>计算字段的"已禁用渠道"列表（用于列表警告标签）</summary>
    private static List<string> ComputeDisabledChannels(CustomField field, Dictionary<string, bool> channelStatus)
    {
        var sourceTypes = DeserializeSourceTypes(field.SourceTypesJson);
        if (sourceTypes.Count == 0) return new List<string>();

        var disabled = new List<string>();
        foreach (var st in sourceTypes)
        {
            if (channelStatus.TryGetValue(st, out var enabled) && !enabled)
            {
                disabled.Add(st);
            }
        }
        return disabled;
    }

    private static List<string> DeserializeSourceTypes(string? json)
    {
        if (string.IsNullOrWhiteSpace(json)) return new List<string>();
        try
        {
            return JsonSerializer.Deserialize<List<string>>(json) ?? new List<string>();
        }
        catch
        {
            return new List<string>();
        }
    }

    /// <summary>获取可用渠道列表（仅返回已启用渠道 + 全局选项），用于字段管理页下拉。</summary>
    public async Task<List<CustomFieldChannelDto>> GetChannelsAsync()
    {
        var channelStatus = await GetChannelStatusMapAsync();
        var result = new List<CustomFieldChannelDto>();

        // 全局选项始终可用
        result.Add(new CustomFieldChannelDto
        {
            Value = "global",
            Label = Translate("Channel_global", "全局（所有渠道）"),
            IsEnabled = true,
        });

        foreach (var kv in KnownChannels)
        {
            var enabled = channelStatus.TryGetValue(kv.Key, out var v) && v;
            // 仅返回已启用的渠道
            if (enabled)
            {
                result.Add(new CustomFieldChannelDto
                {
                    Value = kv.Key,
                    Label = LocalizeChannelLabel(kv.Key, kv.Value),
                    IsEnabled = enabled,
                });
            }
        }
        return result;
    }

    /// <summary>
    /// 渠道显示名：字典值保持语言中立，这里按 <c>Channel_&lt;code&gt;</c> 词条本地化，
    /// 词条缺失或未注入本地化服务时回退 zh-Hans 原文（zh-Hans 输出逐字不变）。
    /// </summary>
    private string LocalizeChannelLabel(string code, string zhFallback) =>
        Translate($"Channel_{code}", zhFallback);

    /// <summary>
    /// 取词条文本：本地化服务在词条缺失时会返回键名本身，此时回退给定中文原文，避免把键名吐给前端。
    /// </summary>
    private string Translate(string key, string zhFallback, params object[] args)
    {
        if (_localization is null) return args.Length > 0 ? string.Format(zhFallback, args) : zhFallback;
        var value = _localization[key];
        if (string.IsNullOrWhiteSpace(value) || string.Equals(value, key, StringComparison.Ordinal))
        {
            return args.Length > 0 ? string.Format(zhFallback, args) : zhFallback;
        }
        return args.Length > 0 ? string.Format(value, args) : value;
    }

    public async Task<(List<CustomFieldDto> Items, int Total)> GetPagedAsync(CustomFieldQuery query)
    {
        var (items, total) = await _unitOfWork.CustomFields.GetPagedAsync(
            query.Page,
            query.PageSize,
            query.EntityType,
            query.MailboxId,
            query.IsActive,
            query.Keyword);
        var channelStatus = await GetChannelStatusMapAsync();
        var dtos = items.Select(f =>
        {
            var dto = MapToDto(f);
            // 字段管理列表需要 DisabledChannels 显示警告标签
            dto.DisabledChannels = ComputeDisabledChannels(f, channelStatus);
            return dto;
        }).ToList();
        return (dtos, total);
    }

    public async Task<CustomFieldOverviewResponse> GetOverviewAsync()
    {
        var stats = await _unitOfWork.CustomFields.GetStatsAsync();
        var allFields = await _unitOfWork.CustomFields.GetAllWithMailboxAsync();

        var fieldsByType = allFields
            .GroupBy(f => f.Type)
            .ToDictionary(g => g.Key, g => g.Count());

        return new CustomFieldOverviewResponse
        {
            TotalFields = stats.GetValueOrDefault("total", 0),
            ActiveFields = stats.GetValueOrDefault("active", 0),
            InactiveFields = stats.GetValueOrDefault("inactive", 0),
            ConversationFields = stats.GetValueOrDefault("conversationFields", 0),
            CustomerFields = stats.GetValueOrDefault("customerFields", 0),
            GlobalFields = stats.GetValueOrDefault("globalFields", 0),
            MailboxSpecificFields = stats.GetValueOrDefault("total", 0) - stats.GetValueOrDefault("globalFields", 0),
            FieldsByType = fieldsByType,
        };
    }

    public async Task<CustomFieldDto> CreateAsync(CreateCustomFieldRequest request)
    {
        // 客户字段强制全局：忽略 SourceTypes
        var sourceTypes = request.EntityType == "customer"
            ? null
            : (request.SourceTypes != null && request.SourceTypes.Count > 0
                ? JsonSerializer.Serialize(request.SourceTypes.Distinct(StringComparer.OrdinalIgnoreCase).ToList())
                : null);

        var field = new CustomField
        {
            Name = request.Name,
            Type = request.Type,
            EntityType = request.EntityType,
            MailboxId = request.MailboxId,
            SourceTypesJson = sourceTypes,
            HideWhenChannelDisabled = request.EntityType == "customer" ? false : request.HideWhenChannelDisabled,
            IsRequired = request.IsRequired,
            IsActive = request.IsActive,
            SortOrder = request.SortOrder,
            OptionsJson = request.Options.Count > 0 ? JsonSerializer.Serialize(request.Options) : null,
            DefaultValue = request.DefaultValue,
            Placeholder = request.Placeholder,
            HelpText = request.HelpText,
        };

        await _unitOfWork.CustomFields.AddAsync(field);
        await _unitOfWork.SaveChangesAsync();
        return MapToDto(field);
    }

    public async Task<CustomFieldDto?> UpdateAsync(UpdateCustomFieldRequest request)
    {
        // 使用 tracking 查询，确保修改能持久化到数据库
        var field = await _unitOfWork.CustomFields.GetQueryable(false)
            .FirstOrDefaultAsync(f => f.Id == request.Id && f.DeletedAt == null);
        if (field == null) return null;

        if (!await _currentUserAccessor.CanAccessMailboxAsync(field.MailboxId))
            return null;

        // 客户字段强制全局：忽略 SourceTypes
        var sourceTypes = request.EntityType == "customer"
            ? null
            : (request.SourceTypes != null && request.SourceTypes.Count > 0
                ? JsonSerializer.Serialize(request.SourceTypes.Distinct(StringComparer.OrdinalIgnoreCase).ToList())
                : null);

        field.Name = request.Name;
        field.Type = request.Type;
        field.EntityType = request.EntityType;
        field.MailboxId = request.MailboxId;
        field.SourceTypesJson = sourceTypes;
        field.HideWhenChannelDisabled = request.EntityType == "customer" ? false : request.HideWhenChannelDisabled;
        field.IsRequired = request.IsRequired;
        field.IsActive = request.IsActive;
        field.SortOrder = request.SortOrder;
        field.OptionsJson = request.Options.Count > 0 ? JsonSerializer.Serialize(request.Options) : null;
        field.DefaultValue = request.DefaultValue;
        field.Placeholder = request.Placeholder;
        field.HelpText = request.HelpText;
        field.UpdatedAt = DateTime.UtcNow;

        await _unitOfWork.SaveChangesAsync();
        return MapToDto(field);
    }

    public async Task<bool> DeleteAsync(int id)
    {
        // 使用 tracking 查询，确保软删除修改能持久化到数据库
        var field = await _unitOfWork.CustomFields.GetQueryable(false)
            .FirstOrDefaultAsync(f => f.Id == id && f.DeletedAt == null);
        if (field == null) return false;

        if (!await _currentUserAccessor.CanAccessMailboxAsync(field.MailboxId))
            return false;

        field.DeletedAt = DateTime.UtcNow;
        field.UpdatedAt = DateTime.UtcNow;
        await _unitOfWork.SaveChangesAsync();
        return true;
    }

    // ====== 垃圾箱 ======
    public async Task<(List<CustomFieldDto> Items, int Total)> GetTrashPagedAsync(int page = 1, int pageSize = 10, string? keyword = null)
    {
        var (items, total) = await _unitOfWork.CustomFields.GetDeletedPagedAsync(page, pageSize, keyword);
        return (items.Select(MapToDto).ToList(), total);
    }

    public async Task<CustomFieldDto?> RestoreAsync(int id)
    {
        var field = await _unitOfWork.CustomFields.GetDeletedByIdAsync(id);
        if (field == null) return null;
        await _unitOfWork.CustomFields.RestoreAsync(id);
        return await GetByIdAsync(id);
    }

    public async Task<bool> HardDeleteAsync(int id)
    {
        if (_currentUserAccessor != null && !_currentUserAccessor.IsSuperAdmin)
        {
            return false;
        }
        return await _unitOfWork.CustomFields.HardDeleteAsync(id);
    }

    public async Task<int> ClearTrashAsync()
    {
        return await _unitOfWork.CustomFields.HardDeleteAllDeletedAsync();
    }

    public async Task<bool> ToggleActiveAsync(int id)
    {
        var field = await _unitOfWork.CustomFields.GetQueryable(false)
            .FirstOrDefaultAsync(f => f.Id == id && f.DeletedAt == null);
        if (field == null) return false;

        field.IsActive = !field.IsActive;
        field.UpdatedAt = DateTime.UtcNow;
        await _unitOfWork.SaveChangesAsync();
        return true;
    }

    public async Task<List<CustomFieldValueDto>> GetEntityValuesAsync(string entityType, int entityId)
    {
        var values = await _unitOfWork.CustomFieldValues.GetByEntityAsync(entityType, entityId);
        return values.Select(MapValueToDto).ToList();
    }

    public async Task<CustomFieldValueDto?> SetEntityValueAsync(
        int customFieldId, string entityType, int entityId, string? value)
    {
        var field = await _unitOfWork.CustomFields.GetByIdAsync(customFieldId);
        if (field == null) return null;

        var existing = await _unitOfWork.CustomFieldValues.GetByFieldAndEntityAsync(
            customFieldId, entityType, entityId);

        if (existing != null)
        {
            existing.Value = value;
            existing.UpdatedAt = DateTime.UtcNow;
            await _unitOfWork.SaveChangesAsync();
            return MapValueToDto(existing);
        }

        var newValue = new CustomFieldValue
        {
            CustomFieldId = customFieldId,
            EntityId = entityId,
            EntityType = entityType,
            Value = value,
        };

        await _unitOfWork.CustomFieldValues.AddAsync(newValue);
        await _unitOfWork.SaveChangesAsync();
        return MapValueToDto(newValue);
    }

    public async Task<List<CustomFieldValueDto>> BatchSetEntityValuesAsync(
        string entityType, int entityId, Dictionary<int, string?> values)
    {
        var results = new List<CustomFieldValueDto>();

        using var transaction = await _unitOfWork.BeginTransactionAsync();
        try
        {
            foreach (var (fieldId, value) in values)
            {
                var result = await SetEntityValueAsync(fieldId, entityType, entityId, value);
                if (result != null)
                {
                    results.Add(result);
                }
            }
            await transaction.CommitAsync();
        }
        catch
        {
            await transaction.SafeRollbackAsync();
            throw;
        }

        return results;
    }

    public async Task<bool> DeleteEntityValuesAsync(string entityType, int entityId)
    {
        await _unitOfWork.CustomFieldValues.DeleteByEntityAsync(entityType, entityId);
        await _unitOfWork.SaveChangesAsync();
        return true;
    }

    private static CustomFieldDto MapToDto(CustomField field)
    {
        List<string> options = new();
        if (!string.IsNullOrEmpty(field.OptionsJson))
        {
            try
            {
                options = JsonSerializer.Deserialize<List<string>>(field.OptionsJson) ?? new();
            }
            catch
            {
                options = new List<string>();
            }
        }

        return new CustomFieldDto
        {
            Id = field.Id,
            Name = field.Name,
            Type = field.Type,
            EntityType = field.EntityType,
            MailboxId = field.MailboxId,
            MailboxName = field.Mailbox?.Name,
            SourceTypes = DeserializeSourceTypes(field.SourceTypesJson),
            HideWhenChannelDisabled = field.HideWhenChannelDisabled,
            // DisabledChannels 默认空，由调用方按需填充
            IsRequired = field.IsRequired,
            IsActive = field.IsActive,
            SortOrder = field.SortOrder,
            Options = options,
            DefaultValue = field.DefaultValue,
            Placeholder = field.Placeholder,
            HelpText = field.HelpText,
            CreatedAt = field.CreatedAt,
            UpdatedAt = field.UpdatedAt,
        };
    }

    private static CustomFieldValueDto MapValueToDto(CustomFieldValue value)
    {
        return new CustomFieldValueDto
        {
            Id = value.Id,
            CustomFieldId = value.CustomFieldId,
            FieldName = value.CustomField?.Name ?? string.Empty,
            FieldType = value.CustomField?.Type ?? string.Empty,
            EntityId = value.EntityId,
            EntityType = value.EntityType,
            Value = value.Value,
            CreatedAt = value.CreatedAt,
            UpdatedAt = value.UpdatedAt,
        };
    }
}
