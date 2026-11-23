namespace NoneadCSS.Application.Configuration;

/// <summary>
/// 在线聊天模块配置选项
/// </summary>
public class ChatOptions
{
    /// <summary>消息撤回时间窗口（分钟），超过此时间不允许撤回</summary>
    public double MessageRecallWindowMinutes { get; set; } = 2;
}
