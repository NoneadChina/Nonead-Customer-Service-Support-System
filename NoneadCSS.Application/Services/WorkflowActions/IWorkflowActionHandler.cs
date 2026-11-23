using Microsoft.Extensions.Logging;
using NoneadCSS.Domain.Entities;

namespace NoneadCSS.Application.Services.WorkflowActions;

/// <summary>
/// 工作流动作处理器接口（策略模式）。
/// 每种动作类型对应一个处理器实现，通过 ActionType 属性匹配。
/// </summary>
public interface IWorkflowActionHandler
{
    /// <summary>
    /// 该处理器负责的主动作类型（如 "set_status"、"assign_user" 等）
    /// </summary>
    string ActionType { get; }

    /// <summary>
    /// 该处理器额外支持的动作类型（可选，用于一个处理器复用多种动作的场景，
    /// 例如 send_reply 与 forward 共用同一占位实现）。
    /// 默认为空数组。
    /// </summary>
    string[] AdditionalActionTypes => Array.Empty<string>();

    /// <summary>
    /// 执行工作流动作
    /// </summary>
    /// <param name="action">工作流动作定义</param>
    /// <param name="conversation">目标会话（已被 EF Core 跟踪）</param>
    /// <param name="context">执行上下文，包含满意度评价、待执行副作用等</param>
    /// <returns>动作是否执行成功</returns>
    Task<bool> ExecuteAsync(WorkflowAction action, Conversation conversation, WorkflowActionContext context);
}

/// <summary>
/// 工作流动作处理器解析器：根据动作类型查找对应的处理器实例。
/// </summary>
public interface IWorkflowActionHandlerProvider
{
    /// <summary>
    /// 尝试获取指定动作类型的处理器。
    /// </summary>
    /// <param name="actionType">动作类型字符串</param>
    /// <param name="handler">匹配的处理器；未找到时为 null</param>
    /// <returns>是否找到匹配的处理器</returns>
    bool TryGetHandler(string actionType, out IWorkflowActionHandler? handler);
}

/// <summary>
/// 默认的处理器解析器：启动时扫描所有 IWorkflowActionHandler 注册，
/// 构建 ActionType → Handler 的字典以支持 O(1) 查找。
/// </summary>
public class WorkflowActionHandlerProvider : IWorkflowActionHandlerProvider
{
    private readonly Dictionary<string, IWorkflowActionHandler> _handlers;
    private readonly ILogger<WorkflowActionHandlerProvider> _logger;

    public WorkflowActionHandlerProvider(
        IEnumerable<IWorkflowActionHandler> handlers,
        ILogger<WorkflowActionHandlerProvider> logger)
    {
        _logger = logger;
        _handlers = new Dictionary<string, IWorkflowActionHandler>(StringComparer.Ordinal);

        foreach (var handler in handlers)
        {
            RegisterHandler(handler.ActionType, handler);

            foreach (var extraType in handler.AdditionalActionTypes)
            {
                RegisterHandler(extraType, handler);
            }
        }

        _logger.LogInformation("已注册 {Count} 个工作流动作处理器：{Types}",
            _handlers.Count, string.Join(", ", _handlers.Keys));
    }

    private void RegisterHandler(string actionType, IWorkflowActionHandler handler)
    {
        if (string.IsNullOrWhiteSpace(actionType))
        {
            _logger.LogWarning("处理器 {HandlerType} 的 ActionType 为空，已跳过", handler.GetType().Name);
            return;
        }

        if (_handlers.TryGetValue(actionType, out var existing))
        {
            _logger.LogWarning("动作类型 {ActionType} 已被 {ExistingHandler} 注册，{NewHandler} 将被忽略",
                actionType, existing.GetType().Name, handler.GetType().Name);
            return;
        }

        _handlers[actionType] = handler;
    }

    public bool TryGetHandler(string actionType, out IWorkflowActionHandler? handler)
    {
        return _handlers.TryGetValue(actionType, out handler);
    }
}

/// <summary>
/// 工作流动作执行上下文，封装跨动作共享的状态和服务访问。
/// </summary>
public class WorkflowActionContext
{
    /// <summary>
    /// 满意度评价（仅 satisfaction_submitted 触发的工作流有值）
    /// </summary>
    public SatisfactionRating? Rating { get; init; }

    /// <summary>
    /// 目标在线聊天会话（Chat-close-requested / Chat-satisfaction-submitted 等在线聊天 trigger 时有值）。
    /// send_chat_message 动作优先使用该上下文（忽略 Conversation 占位对象）。
    /// </summary>
    public ChatSession? ChatSession { get; init; }

    /// <summary>
    /// 在线聊天客户提交的评分（1-5，仅 chat_satisfaction_submitted trigger 有值）
    /// </summary>
    public int? ChatRating { get; init; }

    /// <summary>
    /// 在线聊天客户提交的评论文本（仅 chat_satisfaction_submitted trigger，且客户有填写时非空）
    /// </summary>
    public string? ChatRatingComment { get; init; }

    /// <summary>
    /// 待执行的副作用（事务提交后执行），如 Webhook 调用。
    /// 为 null 表示非事务场景，应立即执行副作用。
    /// </summary>
    public List<Func<Task>>? PendingSideEffects { get; init; }

    /// <summary>
    /// 服务提供器，用于延迟解析服务（避免循环依赖）
    /// </summary>
    public IServiceProvider ServiceProvider { get; init; } = null!;
}
