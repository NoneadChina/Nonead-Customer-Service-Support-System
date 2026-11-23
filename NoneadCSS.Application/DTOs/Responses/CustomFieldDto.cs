using System.ComponentModel.DataAnnotations;

namespace NoneadCSS.Application.DTOs.Responses;

public class CustomFieldDto
{
    public int Id { get; set; }
    public string Name { get; set; } = string.Empty;
    public string Type { get; set; } = "text";
    public string EntityType { get; set; } = "conversation";
    public int? MailboxId { get; set; }
    public string? MailboxName { get; set; }

    /// <summary>
    /// 适用渠道列表（SourceType 枚举名小写）：空列表或 null 表示全局可用。
    /// 仅对 conversation 字段有效；customer 字段始终为空（全局）。
    /// </summary>
    public List<string> SourceTypes { get; set; } = new();

    /// <summary>渠道关闭后是否自动隐藏该字段（仅 conversation 字段有意义）</summary>
    public bool HideWhenChannelDisabled { get; set; }

    /// <summary>
    /// 已禁用的渠道列表（运行时计算）：字段绑定的渠道中当前已关闭的渠道。
    /// 前端据此在列表显示"渠道已关闭"警告标签。仅字段管理接口返回。
    /// </summary>
    public List<string> DisabledChannels { get; set; } = new();

    public bool IsRequired { get; set; }
    public bool IsActive { get; set; }
    public int SortOrder { get; set; }
    public List<string> Options { get; set; } = new();
    public string? DefaultValue { get; set; }
    public string? Placeholder { get; set; }
    public string? HelpText { get; set; }
    public DateTime CreatedAt { get; set; }
    public DateTime UpdatedAt { get; set; }
}

public class CustomFieldValueDto
{
    public int Id { get; set; }
    public int CustomFieldId { get; set; }
    public string FieldName { get; set; } = string.Empty;
    public string FieldType { get; set; } = string.Empty;
    public int EntityId { get; set; }
    public string EntityType { get; set; } = string.Empty;
    public string? Value { get; set; }
    public DateTime CreatedAt { get; set; }
    public DateTime UpdatedAt { get; set; }
}

public class CustomFieldOverviewResponse
{
    public int TotalFields { get; set; }
    public int ActiveFields { get; set; }
    public int InactiveFields { get; set; }
    public int ConversationFields { get; set; }
    public int CustomerFields { get; set; }
    public int GlobalFields { get; set; }
    public int MailboxSpecificFields { get; set; }
    public Dictionary<string, int> FieldsByType { get; set; } = new();
}

public class CreateCustomFieldRequest : IValidatableObject
{
    [Required(ErrorMessage = "字段名称不能为空")]
    [MaxLength(100, ErrorMessage = "字段名称最多 100 个字符")]
    [RegularExpression(@"^[^<>""'&|\\]+$", ErrorMessage = "字段名称不能包含特殊字符（< > \" ' & | \\）")]
    public string Name { get; set; } = string.Empty;

    [Required(ErrorMessage = "字段类型不能为空")]
    [RegularExpression("^(text|textarea|select|date|checkbox|number)$",
        ErrorMessage = "字段类型无效，必须为 text/textarea/select/date/checkbox/number")]
    public string Type { get; set; } = "text";

    [Required(ErrorMessage = "对象类型不能为空")]
    [RegularExpression("^(conversation|customer)$",
        ErrorMessage = "对象类型无效，必须为 conversation 或 customer")]
    public string EntityType { get; set; } = "conversation";

    public int? MailboxId { get; set; }

    /// <summary>适用渠道列表（SourceType 枚举名小写，如 "wechat"）；空列表或 null 表示全局可用。仅对 conversation 字段有效，customer 字段忽略。</summary>
    public List<string> SourceTypes { get; set; } = new();

    /// <summary>渠道关闭后是否自动隐藏该字段（仅 conversation 字段有意义）</summary>
    public bool HideWhenChannelDisabled { get; set; }

    public bool IsRequired { get; set; }

    public bool IsActive { get; set; } = true;

    [Range(0, 9999, ErrorMessage = "排序值必须在 0-9999 之间")]
    public int SortOrder { get; set; }

    public List<string> Options { get; set; } = new();

    [MaxLength(500, ErrorMessage = "默认值最多 500 个字符")]
    public string? DefaultValue { get; set; }

    [MaxLength(200, ErrorMessage = "占位提示最多 200 个字符")]
    public string? Placeholder { get; set; }

    [MaxLength(500, ErrorMessage = "帮助说明最多 500 个字符")]
    public string? HelpText { get; set; }

    public IEnumerable<ValidationResult> Validate(ValidationContext validationContext)
    {
        if (Type == "select" && (Options == null || Options.Count == 0))
        {
            yield return new ValidationResult(
                "下拉选择类型必须至少提供一个选项",
                new[] { nameof(Options) });
        }

        // 客户字段强制全局：不允许设置 SourceTypes
        if (EntityType == "customer" && SourceTypes != null && SourceTypes.Count > 0)
        {
            yield return new ValidationResult(
                "客户字段不绑定渠道，必须保持全局可用",
                new[] { nameof(SourceTypes) });
        }

        // 校验渠道标识符：必须是已知 SourceType 枚举名
        var allowed = new HashSet<string>(StringComparer.OrdinalIgnoreCase)
        {
            "email", "web", "api", "phone", "chat", "social",
            "wechat", "whatsapp", "feishu", "dingtalk",
            "messenger", "telegram", "line", "imessage"
        };
        if (SourceTypes != null)
        {
            foreach (var s in SourceTypes)
            {
                if (!allowed.Contains(s))
                {
                    yield return new ValidationResult(
                        $"未知渠道标识：{s}（合法值：email/web/wechat/whatsapp/feishu/dingtalk 等）",
                        new[] { nameof(SourceTypes) });
                }
            }
        }
    }
}

public class UpdateCustomFieldRequest : IValidatableObject
{
    [Range(1, int.MaxValue, ErrorMessage = "ID 必须大于 0")]
    public int Id { get; set; }

    [Required(ErrorMessage = "字段名称不能为空")]
    [MaxLength(100, ErrorMessage = "字段名称最多 100 个字符")]
    [RegularExpression(@"^[^<>""'&|\\]+$", ErrorMessage = "字段名称不能包含特殊字符（< > \" ' & | \\）")]
    public string Name { get; set; } = string.Empty;

    [Required(ErrorMessage = "字段类型不能为空")]
    [RegularExpression("^(text|textarea|select|date|checkbox|number)$",
        ErrorMessage = "字段类型无效，必须为 text/textarea/select/date/checkbox/number")]
    public string Type { get; set; } = "text";

    [Required(ErrorMessage = "对象类型不能为空")]
    [RegularExpression("^(conversation|customer)$",
        ErrorMessage = "对象类型无效，必须为 conversation 或 customer")]
    public string EntityType { get; set; } = "conversation";

    public int? MailboxId { get; set; }

    /// <summary>适用渠道列表（SourceType 枚举名小写，如 "wechat"）；空列表或 null 表示全局可用。仅对 conversation 字段有效，customer 字段忽略。</summary>
    public List<string> SourceTypes { get; set; } = new();

    /// <summary>渠道关闭后是否自动隐藏该字段（仅 conversation 字段有意义）</summary>
    public bool HideWhenChannelDisabled { get; set; }

    public bool IsRequired { get; set; }

    public bool IsActive { get; set; }

    [Range(0, 9999, ErrorMessage = "排序值必须在 0-9999 之间")]
    public int SortOrder { get; set; }

    public List<string> Options { get; set; } = new();

    [MaxLength(500, ErrorMessage = "默认值最多 500 个字符")]
    public string? DefaultValue { get; set; }

    [MaxLength(200, ErrorMessage = "占位提示最多 200 个字符")]
    public string? Placeholder { get; set; }

    [MaxLength(500, ErrorMessage = "帮助说明最多 500 个字符")]
    public string? HelpText { get; set; }

    public IEnumerable<ValidationResult> Validate(ValidationContext validationContext)
    {
        if (Type == "select" && (Options == null || Options.Count == 0))
        {
            yield return new ValidationResult(
                "下拉选择类型必须至少提供一个选项",
                new[] { nameof(Options) });
        }

        // 客户字段强制全局：不允许设置 SourceTypes
        if (EntityType == "customer" && SourceTypes != null && SourceTypes.Count > 0)
        {
            yield return new ValidationResult(
                "客户字段不绑定渠道，必须保持全局可用",
                new[] { nameof(SourceTypes) });
        }

        // 校验渠道标识符
        var allowed = new HashSet<string>(StringComparer.OrdinalIgnoreCase)
        {
            "email", "web", "api", "phone", "chat", "social",
            "wechat", "whatsapp", "feishu", "dingtalk",
            "messenger", "telegram", "line", "imessage"
        };
        if (SourceTypes != null)
        {
            foreach (var s in SourceTypes)
            {
                if (!allowed.Contains(s))
                {
                    yield return new ValidationResult(
                        $"未知渠道标识：{s}（合法值：email/web/wechat/whatsapp/feishu/dingtalk 等）",
                        new[] { nameof(SourceTypes) });
                }
            }
        }
    }
}

public class CustomFieldQuery
{
    public int Page { get; set; } = 1;
    public int PageSize { get; set; } = 20;
    public string? EntityType { get; set; }
    public int? MailboxId { get; set; }
    public bool? IsActive { get; set; }
    public string? Keyword { get; set; }

    /// <summary>按渠道过滤（传入会话 sourceType 字符串，如 "wechat"），返回全局字段 + 该渠道专属字段</summary>
    public string? SourceType { get; set; }
}

/// <summary>
/// 渠道启用状态响应：用于字段管理页"适用渠道"下拉选项。
/// 只返回当前已启用的渠道（"通用"始终可用，对应全局字段）。
/// </summary>
public class CustomFieldChannelDto
{
    /// <summary>渠道标识（SourceType 枚举名小写，如 "wechat"）；"global" 表示全局</summary>
    public string Value { get; set; } = string.Empty;

    /// <summary>渠道显示名（如 "微信"、"全局"）</summary>
    public string Label { get; set; } = string.Empty;

    /// <summary>是否当前已启用（全局始终为 true）</summary>
    public bool IsEnabled { get; set; } = true;
}

public class UpdateCustomFieldValueRequest
{
    [MaxLength(2000, ErrorMessage = "字段值最多 2000 个字符")]
    public string? Value { get; set; }
}

public class BatchUpdateCustomFieldValuesRequest
{
    [Range(1, int.MaxValue, ErrorMessage = "对象 ID 必须大于 0")]
    public int EntityId { get; set; }

    [Required(ErrorMessage = "对象类型不能为空")]
    [RegularExpression("^(conversation|customer)$",
        ErrorMessage = "对象类型无效，必须为 conversation 或 customer")]
    public string EntityType { get; set; } = "conversation";

    public Dictionary<int, string?> Values { get; set; } = new();
}
