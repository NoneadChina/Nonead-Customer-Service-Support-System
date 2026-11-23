using NoneadCSS.Application.DTOs.Responses;

namespace NoneadCSS.Application.Services;

/// <summary>
/// iMessage 入站处理抽象 — 接收 BlueBubbles webhook 事件（或 MSP/Apple 推送）并落库。
/// 控制器必须在快速返回 2xx 之后调用本服务（Apple 只对 5xx 重试 4 次，退避自 100ms 起）。
/// </summary>
public interface IIMessageIngestionService
{
    /// <summary>
    /// 处理入站报文。按事件类型分派：bridge 的 new-message / updated-message / message-error /
    /// typing-indicator / read-receipt 等；入站按消息 guid 幂等去重，isFromMe=true 一律忽略。
    /// </summary>
    /// <param name="payload">原始 JSON 文本（已解压、已验签）</param>
    /// <param name="modeHint">传输模式（bridge / amb_msp / amb_apple_direct）；空则取当前配置</param>
    Task<IMessageIngestionResult> ProcessWebhookPayloadAsync(string? payload, string? modeHint = null);

    /// <summary>
    /// 处理 Apple Messages for Business（AMB）单条入站推送。Apple 的入站身份在**请求头**里
    /// （Source-Id / Destination-Id / Id），必须与 body 一致，否则应回 400。
    /// 若返回结果的 ErrorMessage 非空，调用方必须回 400（Apple 不会重投 4xx）。
    /// </summary>
    Task<IMessageIngestionResult> ProcessAmbInboundAsync(
        string? payload,
        string? sourceIdHeader,
        string? destinationIdHeader,
        string? idHeader);
}
