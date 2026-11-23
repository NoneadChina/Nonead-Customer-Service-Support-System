using NoneadCSS.Domain.Entities;

namespace NoneadCSS.Application.Services.WorkflowActions;

/// <summary>
/// IM 消息发送队列服务接口：将待发送的 IM 消息持久化入队，
/// 由后台 Job 异步处理，支持失败重试（指数退避）。
/// 参考 <see cref="IEmailQueueService"/> 的设计模式。
/// </summary>
public interface IImMessageQueueService
{
    /// <summary>
    /// 将 IM 消息加入发送队列。
    /// 调用方应在邮件无法送达且会话来源为 IM 渠道（WeChat/Feishu/WhatsApp/DingTalk）时调用此方法。
    /// </summary>
    /// <param name="conversation">目标会话</param>
    /// <param name="text">消息文本</param>
    /// <returns>入队的队列记录 Id；若会话不支持 IM 渠道或参数无效则返回 null</returns>
    Task<int?> EnqueueAsync(Conversation conversation, string text);

    /// <summary>
    /// 处理队列中待发送的 IM 消息：扫描 Pending 状态记录，逐条调用渠道 Client 发送，
    /// 成功标记 Sent，失败按指数退避策略安排下次重试，超过最大次数标记 Failed。
    /// </summary>
    Task ProcessQueueAsync();
}
