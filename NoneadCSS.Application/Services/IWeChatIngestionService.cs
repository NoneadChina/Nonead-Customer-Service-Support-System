using NoneadCSS.Application.DTOs.Responses;

namespace NoneadCSS.Application.Services;

public interface IWeChatIngestionService
{
    /// <summary>
    /// 处理微信推送的消息，创建/更新会话与线程。
    /// </summary>
    /// <param name="message">解析后的微信消息 DTO</param>
    /// <returns>处理结果</returns>
    Task<WeChatIngestionResult> ProcessMessageAsync(WeChatMessageDto message);
}
