using NoneadCSS.Application.Configuration;
using Microsoft.Extensions.Options;
using NoneadCSS.Domain.Entities;

namespace NoneadCSS.Application.Templates.Providers;

/// <summary>
/// 会话变量提供者 — subject, conversation_id, conversation_url, priority 等
/// </summary>
public class ConversationVariableProvider : ITemplateVariableProvider
{
    private readonly BrandOptions _brand;

    public int Order => 10;

    public ConversationVariableProvider(IOptions<BrandOptions> brand)
    {
        _brand = brand.Value;
    }

    public Task<Dictionary<string, string>> ResolveAsync(VariableContext context)
    {
        var vars = new Dictionary<string, string>();

        if (context.Conversation is Conversation conv)
        {
            vars["subject"] = conv.Subject ?? string.Empty;
            vars["conversation_id"] = conv.Id.ToString();
            vars["conversation_url"] = $"{_brand.FrontendUrl}/conversations/{conv.Id}";
            vars["priority"] = conv.Priority.ToString();
            vars["customer_email"] = conv.CustomerEmail ?? string.Empty;
        }

        if (!string.IsNullOrWhiteSpace(context.MessageBody))
            vars["message_body"] = context.MessageBody;

        if (!string.IsNullOrWhiteSpace(context.FirstMessageBody))
            vars["first_message_body"] = context.FirstMessageBody;

        return Task.FromResult(vars);
    }
}
