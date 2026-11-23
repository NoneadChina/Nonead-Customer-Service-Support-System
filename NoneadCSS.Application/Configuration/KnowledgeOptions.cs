namespace NoneadCSS.Application.Configuration;

/// <summary>
/// 知识库模块配置选项
/// </summary>
public class KnowledgeOptions
{
    /// <summary>点赞去重窗口（小时），同一用户在此时间内重复点赞只计一次</summary>
    public int LikeDedupWindowHours { get; set; } = 24;
}
