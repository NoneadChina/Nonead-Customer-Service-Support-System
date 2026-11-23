namespace NoneadCSS.Domain.Entities;

public class NotificationTemplate : ISoftDelete
{
    public int Id { get; set; }
    public string Name { get; set; } = string.Empty;
    public string Subject { get; set; } = string.Empty;
    public string Body { get; set; } = string.Empty;
    public string EventType { get; set; } = string.Empty;
    public bool IsActive { get; set; } = true;
    public DateTime CreatedAt { get; set; } = DateTime.UtcNow;
    public DateTime UpdatedAt { get; set; } = DateTime.UtcNow;

    public DateTime? DeletedAt { get; set; }

    public bool IsDeleted() => DeletedAt.HasValue;

    public void SoftDelete()
    {
        DeletedAt = DateTime.UtcNow;
        UpdatedAt = DateTime.UtcNow;
    }

    public void Restore()
    {
        DeletedAt = null;
        UpdatedAt = DateTime.UtcNow;
    }

    public bool SupportsEventType(string eventType) => string.Equals(EventType, eventType, StringComparison.OrdinalIgnoreCase);

    public void Activate()
    {
        IsActive = true;
        UpdatedAt = DateTime.UtcNow;
    }

    public void Deactivate()
    {
        IsActive = false;
        UpdatedAt = DateTime.UtcNow;
    }

    public void UpdateName(string newName)
    {
        Name = newName;
        UpdatedAt = DateTime.UtcNow;
    }

    public void UpdateContent(string subject, string body)
    {
        Subject = subject;
        Body = body;
        UpdatedAt = DateTime.UtcNow;
    }

    public void UpdateSubject(string subject)
    {
        Subject = subject;
        UpdatedAt = DateTime.UtcNow;
    }

    public void UpdateBody(string body)
    {
        Body = body;
        UpdatedAt = DateTime.UtcNow;
    }

    public void UpdateEventType(string eventType)
    {
        EventType = eventType;
        UpdatedAt = DateTime.UtcNow;
    }

    public string Render(IDictionary<string, string> variables)
    {
        var result = Body;
        foreach (var (key, value) in variables)
        {
            result = result.Replace($"{{{key}}}", value ?? string.Empty);
        }
        return result;
    }

    public string RenderSubject(IDictionary<string, string> variables)
    {
        var result = Subject;
        foreach (var (key, value) in variables)
        {
            result = result.Replace($"{{{key}}}", value ?? string.Empty);
        }
        return result;
    }

    public bool ContainsVariable(string variableName)
    {
        var placeholder = $"{{{variableName}}}";
        return Body.Contains(placeholder) || Subject.Contains(placeholder);
    }

    public IEnumerable<string> ExtractVariables()
    {
        var variablePattern = new System.Text.RegularExpressions.Regex(@"{(\w+)}");
        var matches = variablePattern.Matches(Body + Subject);
        return matches.Select(m => m.Groups[1].Value).Distinct();
    }
}
