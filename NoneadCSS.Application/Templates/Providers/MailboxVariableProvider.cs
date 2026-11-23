using NoneadCSS.Domain.Entities;

namespace NoneadCSS.Application.Templates.Providers;

/// <summary>
/// 邮箱变量提供者 — mailbox_name, mailbox_email
/// </summary>
public class MailboxVariableProvider : ITemplateVariableProvider
{
    public int Order => 80;

    public Task<Dictionary<string, string>> ResolveAsync(VariableContext context)
    {
        var vars = new Dictionary<string, string>();

        if (context.Mailbox is Mailbox mailbox)
        {
            vars["mailbox_name"] = mailbox.Name ?? string.Empty;
            vars["mailbox_email"] = mailbox.Email ?? string.Empty;
        }

        return Task.FromResult(vars);
    }
}
