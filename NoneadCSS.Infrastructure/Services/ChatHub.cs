using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.SignalR;
using Microsoft.Extensions.Logging;
using NoneadCSS.Domain;

namespace NoneadCSS.Infrastructure.Services;

/// <summary>
/// 聊天专用 Hub - 面向客户侧（基于 sessionToken 鉴权，无需登录）
/// 客户端连接时通过 query string 传递 sessionToken，连接成功后自动加入会话组
/// </summary>
[AllowAnonymous]
public class ChatHub : Hub
{
    private readonly ILogger<ChatHub> _logger;
    private readonly IUnitOfWork _unitOfWork;

    public ChatHub(ILogger<ChatHub> logger, IUnitOfWork unitOfWork)
    {
        _logger = logger;
        _unitOfWork = unitOfWork;
    }

    /// <summary>
    /// 客户连接时，从 query string 中提取 sessionToken 进行鉴权（兼容旧客户端）。
    /// M13: 前端新版本不再把 sessionToken 放进 URL（防日志/历史/Referer 泄露），
    /// 改为连接成功后调用 <see cref="JoinSession"/> 显式加入会话组；
    /// 因此缺少 sessionToken 时不再直接拒绝连接，由 JoinSession 完成校验。
    /// </summary>
    public override async Task OnConnectedAsync()
    {
        var sessionToken = Context.GetHttpContext()?.Request.Query["sessionToken"].ToString();

        if (!string.IsNullOrWhiteSpace(sessionToken))
        {
            var session = await _unitOfWork.ChatSessions.GetByTokenAsync(sessionToken);
            if (session != null && session.Status != "closed")
            {
                await Groups.AddToGroupAsync(Context.ConnectionId, $"chat_session_{session.Id}");
                _logger.LogInformation("客户连接 ChatHub，会话 ID: {SessionId}，连接 ID: {ConnectionId}",
                    session.Id, Context.ConnectionId);
            }
            else
            {
                _logger.LogWarning("ChatHub 连接拒绝：无效的 sessionToken 或会话已关闭");
                Context.Abort();
                return;
            }
        }

        await base.OnConnectedAsync();
    }

    /// <summary>
    /// 主动加入会话组（M13 起为主要鉴权路径：sessionToken 经消息体传递，不进 URL）
    /// </summary>
    public async Task JoinSession(string sessionToken)
    {
        if (string.IsNullOrWhiteSpace(sessionToken))
            throw new HubException("sessionToken 不能为空");

        var session = await _unitOfWork.ChatSessions.GetByTokenAsync(sessionToken);
        if (session == null || session.Status == "closed")
            throw new HubException("会话不存在或已关闭");

        await Groups.AddToGroupAsync(Context.ConnectionId, $"chat_session_{session.Id}");
        _logger.LogDebug("连接 {ConnectionId} 加入会话组 chat_session_{SessionId}", Context.ConnectionId, session.Id);
    }

    /// <summary>
    /// 离开会话组
    /// </summary>
    public async Task LeaveSession(string sessionToken)
    {
        if (string.IsNullOrWhiteSpace(sessionToken))
            return;

        var session = await _unitOfWork.ChatSessions.GetByTokenAsync(sessionToken);
        if (session != null)
        {
            await Groups.RemoveFromGroupAsync(Context.ConnectionId, $"chat_session_{session.Id}");
        }
    }
}
