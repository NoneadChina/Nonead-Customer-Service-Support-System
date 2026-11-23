namespace NoneadCSS.Domain.Entities;

/// <summary>
/// 自定义字段定义
/// </summary>
public class CustomField : ISoftDelete
{
    public int Id { get; set; }

    /// <summary>字段名称</summary>
    public string Name { get; set; } = string.Empty;

    /// <summary>字段类型：text / textarea / select / date / checkbox / number</summary>
    public string Type { get; set; } = "text";

    /// <summary>关联对象类型：conversation / customer</summary>
    public string EntityType { get; set; } = "conversation";

    /// <summary>所属邮箱 ID。null 表示全局可用。</summary>
    public int? MailboxId { get; set; }

    /// <summary>
    /// 适用渠道 JSON 数组（SourceType 枚举名小写）：
    /// ["wechat","feishu"] 表示仅在这两个渠道的会话展示；null 或空数组表示全局可用。
    /// 仅对 entityType=conversation 生效；customer 类型字段始终全局（此字段强制为 null）。
    /// </summary>
    public string? SourceTypesJson { get; set; }

    /// <summary>
    /// 渠道关闭后是否自动隐藏该字段：
    /// true=字段绑定的渠道全部关闭后，新会话不再展示该字段（已有值仍保留）；
    /// false=即使渠道关闭也展示（默认行为）。
    /// </summary>
    public bool HideWhenChannelDisabled { get; set; }

    /// <summary>是否必填</summary>
    public bool IsRequired { get; set; }

    /// <summary>是否启用</summary>
    public bool IsActive { get; set; } = true;

    /// <summary>排序顺序</summary>
    public int SortOrder { get; set; }

    /// <summary>
    /// 选项配置 JSON（select 类型使用）：
    /// ["选项1", "选项2", "选项3"]
    /// </summary>
    public string? OptionsJson { get; set; }

    /// <summary>默认值</summary>
    public string? DefaultValue { get; set; }

    /// <summary>占位提示文本</summary>
    public string? Placeholder { get; set; }

    /// <summary>帮助说明文本</summary>
    public string? HelpText { get; set; }

    public DateTime CreatedAt { get; set; } = DateTime.UtcNow;
    public DateTime UpdatedAt { get; set; } = DateTime.UtcNow;
    public DateTime? DeletedAt { get; set; }

    public Mailbox? Mailbox { get; set; }
    public ICollection<CustomFieldValue> Values { get; set; } = [];
}

/// <summary>
/// 自定义字段值
/// </summary>
public class CustomFieldValue
{
    public int Id { get; set; }

    /// <summary>字段 ID</summary>
    public int CustomFieldId { get; set; }

    /// <summary>关联对象 ID</summary>
    public int EntityId { get; set; }

    /// <summary>关联对象类型</summary>
    public string EntityType { get; set; } = "conversation";

    /// <summary>字段值（文本存储，不同类型自行解析）</summary>
    public string? Value { get; set; }

    public DateTime CreatedAt { get; set; } = DateTime.UtcNow;
    public DateTime UpdatedAt { get; set; } = DateTime.UtcNow;

    public CustomField? CustomField { get; set; }
}
