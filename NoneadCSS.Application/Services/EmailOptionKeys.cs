namespace NoneadCSS.Application.Services;

/// <summary>
/// 邮件渠道 SystemOptions 配置键常量。
/// 用于在 SystemOptions 表中存储邮件渠道的可配置项（前端 Settings 页可读写）。
/// </summary>
public static class EmailOptionKeys
{
    /// <summary>是否启用客户信息及联系方式收集功能（邮件收取时从签名块提取姓名/手机/邮箱）</summary>
    public const string ContactCollectionEnabled = "email.contact_collection_enabled";

    /// <summary>SystemOptions 分类</summary>
    public const string Category = "email";
}
