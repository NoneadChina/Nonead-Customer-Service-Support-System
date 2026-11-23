using NoneadCSS.Domain.Entities;

namespace NoneadCSS.Application.Templates.Providers;

/// <summary>
/// 客户变量提供者 — customer_name, customer_email, customer_phone
/// </summary>
public class CustomerVariableProvider : ITemplateVariableProvider
{
    public int Order => 20;

    public Task<Dictionary<string, string>> ResolveAsync(VariableContext context)
    {
        var vars = new Dictionary<string, string>();

        if (context.Customer is Customer customer)
        {
            vars["customer_name"] = $"{customer.FirstName} {customer.LastName}".Trim();
            // 从会话兜底客户邮箱
            vars["customer_email"] = context.Conversation?.CustomerEmail ?? string.Empty;
        }
        else if (context.Conversation is { CustomerEmail: not null } conv)
        {
            // 没有客户实体但有会话邮箱
            vars["customer_name"] = conv.CustomerEmail;
            vars["customer_email"] = conv.CustomerEmail;
        }

        // 聊天会话的客户信息兜底
        if (context.ChatSession is ChatSession session)
        {
            if (!vars.ContainsKey("customer_name"))
                vars["customer_name"] = session.CustomerName ?? string.Empty;
            if (!vars.ContainsKey("customer_email"))
                vars["customer_email"] = session.CustomerEmail ?? string.Empty;
            vars["customer_phone"] = session.CustomerPhone ?? string.Empty;
        }

        return Task.FromResult(vars);
    }
}
