using NoneadCSS.Domain.Entities;

namespace NoneadCSS.Application.Templates.Providers;

/// <summary>
/// 聊天变量提供者 — chat_session_id, chat_created_at, agent_name, chat_subject 等
/// </summary>
public class ChatVariableProvider : ITemplateVariableProvider
{
    public int Order => 60;

    public Task<Dictionary<string, string>> ResolveAsync(VariableContext context)
    {
        var vars = new Dictionary<string, string>();

        if (context.ChatSession is ChatSession session)
        {
            vars["chat_session_id"] = session.Id.ToString();
            vars["chat_created_at"] = session.CreatedAt.ToString("yyyy-MM-dd HH:mm");
            vars["chat_subject"] = session.Subject ?? string.Empty;
            vars["timeout_minutes"] = string.Empty; // 由 Extra 覆盖

            // agent_name 从 User 获取
            if (context.User is User agent)
            {
                vars["agent_name"] = $"{agent.FirstName} {agent.LastName}".Trim();
            }
        }

        return Task.FromResult(vars);
    }
}
