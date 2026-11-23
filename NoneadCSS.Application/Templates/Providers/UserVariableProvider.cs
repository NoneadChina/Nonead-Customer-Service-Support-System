using NoneadCSS.Domain.Entities;

namespace NoneadCSS.Application.Templates.Providers;

/// <summary>
/// 用户/客服变量提供者 — user_name, user_email, user_role, assignee_name 等
/// </summary>
public class UserVariableProvider : ITemplateVariableProvider
{
    public int Order => 30;

    public Task<Dictionary<string, string>> ResolveAsync(VariableContext context)
    {
        var vars = new Dictionary<string, string>();

        if (context.User is User user)
        {
            vars["user_name"] = $"{user.FirstName} {user.LastName}".Trim();
            vars["user_email"] = user.Email ?? string.Empty;
            vars["user_role"] = user.Role.ToString();
            // 受让人 = 当前分配的客服
            vars["assignee_name"] = $"{user.FirstName} {user.LastName}".Trim();
        }

        // Actor 是操作人（分配人/关闭人/更新人等）
        if (context.Actor is User actor)
        {
            var actorName = $"{actor.FirstName} {actor.LastName}".Trim();
            // 这些角色变量由调用场景决定，Actor 作为通用操作人
            // 具体角色由 Extra 覆盖
            if (!vars.ContainsKey("assigner_name"))
                vars["assigner_name"] = actorName;
        }

        return Task.FromResult(vars);
    }
}
