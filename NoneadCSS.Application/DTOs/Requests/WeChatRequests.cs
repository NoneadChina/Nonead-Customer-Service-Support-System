using System.ComponentModel.DataAnnotations;

namespace NoneadCSS.Application.DTOs.Requests;

/// <summary>
/// 微信服务号配置请求
/// </summary>
public class UpdateWeChatSettingsRequest
{
    [MaxLength(64, ErrorMessage = "AppId 最多 64 个字符")]
    public string? AppId { get; set; }

    [MaxLength(128, ErrorMessage = "AppSecret 最多 128 个字符")]
    public string? AppSecret { get; set; }

    [MaxLength(64, ErrorMessage = "Token 最多 64 个字符")]
    public string? Token { get; set; }

    [MaxLength(64, ErrorMessage = "EncodingAESKey 最多 64 个字符")]
    public string? EncodingAESKey { get; set; }

    /// <summary>
    /// 消息加解密方式：plain(明文)、compatible(兼容)、safe(安全)
    /// </summary>
    [MaxLength(20, ErrorMessage = "加密方式最多 20 个字符")]
    public string EncryptMode { get; set; } = "plain";

    /// <summary>
    /// 关联的邮箱 ID（微信会话归属此邮箱）
    /// </summary>
    public int? MailboxId { get; set; }

    /// <summary>
    /// 是否启用客户信息及联系方式收集功能（新会话时发引导消息，客户回复后自动提取姓名/手机/邮箱）
    /// </summary>
    public bool ContactCollectionEnabled { get; set; }

    /// <summary>
    /// 是否启用自动回复（收到首条消息时自动回复欢迎语）
    /// </summary>
    public bool AutoReplyEnabled { get; set; }

    [MaxLength(500, ErrorMessage = "自动回复内容最多 500 个字符")]
    public string? AutoReplyMessage { get; set; }

    /// <summary>
    /// 是否启用微信接入
    /// </summary>
    public bool Enabled { get; set; }
}

/// <summary>
/// 通过微信客服消息接口回复客户请求
/// </summary>
public class WeChatReplyRequest
{
    [Required(ErrorMessage = "会话 ID 不能为空")]
    public int ConversationId { get; set; }

    /// <summary>
    /// 回复文本内容。当有附件时可为空。
    /// </summary>
    [MaxLength(2000, ErrorMessage = "回复内容最多 2000 个字符")]
    public string Content { get; set; } = string.Empty;

    /// <summary>
    /// 附件 ID 列表（图片、语音、视频）。
    /// 服务端将逐个上传到微信临时素材并作为客服消息发送。
    /// </summary>
    public List<int>? AttachmentIds { get; set; }
}

/// <summary>
/// 发送微信模板消息请求
/// </summary>
public class WeChatTemplateRequest
{
    [Required(ErrorMessage = "会话 ID 不能为空")]
    public int ConversationId { get; set; }

    [Required(ErrorMessage = "模板 ID 不能为空")]
    [MaxLength(64, ErrorMessage = "模板 ID 最多 64 个字符")]
    public string TemplateId { get; set; } = string.Empty;

    /// <summary>
    /// 模板数据 JSON（键值对，值为 { value, color } 结构）
    /// 例：{ "first": { "value": "您好", "color": "#173177" }, "keyword1": { "value": "工单已创建" } }
    /// </summary>
    [Required(ErrorMessage = "模板数据不能为空")]
    public string DataJson { get; set; } = string.Empty;

    /// <summary>
    /// 点击模板消息跳转的 URL（可选）
    /// </summary>
    [MaxLength(500, ErrorMessage = "URL 最多 500 个字符")]
    public string? Url { get; set; }
}

/// <summary>
/// 发送微信订阅通知请求（新版，替代旧版模板消息）
/// </summary>
public class WeChatSubscribeRequest
{
    [Required(ErrorMessage = "会话 ID 不能为空")]
    public int ConversationId { get; set; }

    [Required(ErrorMessage = "模板 ID 不能为空")]
    [MaxLength(64, ErrorMessage = "模板 ID 最多 64 个字符")]
    public string TemplateId { get; set; } = string.Empty;

    /// <summary>
    /// 模板数据 JSON（键值对，值为 { value } 结构，无 color 字段）
    /// 例：{ "thing1": { "value": "工单已创建" }, "time2": { "value": "2026-08-04 12:00" } }
    /// </summary>
    [Required(ErrorMessage = "模板数据不能为空")]
    public string DataJson { get; set; } = string.Empty;

    /// <summary>
    /// 点击订阅通知跳转的页面路径（可选，如 pages/index）
    /// </summary>
    [MaxLength(500, ErrorMessage = "页面路径最多 500 个字符")]
    public string? Page { get; set; }
}

/// <summary>
/// 创建/更新微信自定义菜单请求
/// </summary>
public class WeChatMenuRequest
{
    /// <summary>
    /// 菜单 JSON（微信公众平台菜单格式）
    /// 例：{ "button": [ { "type": "click", "name": "今日歌曲", "key": "V1001_TODAY_MUSIC" } ] }
    /// </summary>
    [Required(ErrorMessage = "菜单 JSON 不能为空")]
    public string MenuJson { get; set; } = string.Empty;
}

/// <summary>
/// 【测试】注入模拟微信位置消息请求
/// </summary>
public class MockLocationRequest
{
    /// <summary>纬度（默认北京天安门 39.90923）</summary>
    public string? Latitude { get; set; }

    /// <summary>经度（默认北京天安门 116.397428）</summary>
    public string? Longitude { get; set; }

    /// <summary>模拟客户的 OpenId（默认 mock_openid_test_location）</summary>
    public string? OpenId { get; set; }

    /// <summary>位置标签（仅用于日志，不影响消息内容）</summary>
    public string? Label { get; set; }
}
