namespace NoneadCSS.Application.DTOs.Responses;

public class GlobalSearchResponse
{
    public string Query { get; set; } = string.Empty;
    public int Page { get; set; }
    public int PageSize { get; set; }
    public int Total { get; set; }
    public int ConversationTotal { get; set; }
    public int CustomerTotal { get; set; }
    public int KnowledgeTotal { get; set; }
    public List<ConversationSearchItem> Conversations { get; set; } = new();
    public List<CustomerSearchItem> Customers { get; set; } = new();
    public List<KnowledgeSearchItem> Knowledge { get; set; } = new();
}

public class ConversationSearchItem
{
    public int Id { get; set; }
    public string? Subject { get; set; }
    public string? Preview { get; set; }
    public string? CustomerEmail { get; set; }
    public int Status { get; set; }
    public int Priority { get; set; }
    public DateTime CreatedAt { get; set; }
    public DateTime? LastReplyAt { get; set; }
    public int MailboxId { get; set; }
    public string Snippet { get; set; } = string.Empty;
}

public class CustomerSearchItem
{
    public int Id { get; set; }
    public string FullName { get; set; } = string.Empty;
    public string? Email { get; set; }
    public string? Phone { get; set; }
    public string? Company { get; set; }
    public DateTime CreatedAt { get; set; }
    public string Snippet { get; set; } = string.Empty;
}

public class KnowledgeSearchItem
{
    public int Id { get; set; }
    public string Title { get; set; } = string.Empty;
    public string Excerpt { get; set; } = string.Empty;
    public int? CategoryId { get; set; }
    public int ViewCount { get; set; }
    public DateTime CreatedAt { get; set; }
    public DateTime? UpdatedAt { get; set; }
    public string Snippet { get; set; } = string.Empty;
}
