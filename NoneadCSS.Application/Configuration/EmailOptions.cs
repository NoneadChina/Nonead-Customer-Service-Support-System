namespace NoneadCSS.Application.Configuration;

/// <summary>
/// 邮件模块配置选项
/// </summary>
public class EmailOptions
{
    /// <summary>邮件附件最大大小（字节）</summary>
    public long MaxAttachmentSize { get; set; } = 20971520;

    /// <summary>邮件收取 Cron 表达式</summary>
    public string FetchCron { get; set; } = "0 */5 * * * ?";

    /// <summary>是否启用邮件收取</summary>
    public bool FetchEnabled { get; set; } = true;

    /// <summary>
    /// 是否启用客户信息及联系方式收集功能。
    /// 开启后，邮件收取时会从邮件正文（优先签名块）中提取客户姓名/手机/邮箱并填入客户资料。
    /// 已有字段不覆盖，只补全缺失字段；每个客户最多尝试提取 3 次。
    /// </summary>
    public bool ContactCollectionEnabled { get; set; } = true;
}
