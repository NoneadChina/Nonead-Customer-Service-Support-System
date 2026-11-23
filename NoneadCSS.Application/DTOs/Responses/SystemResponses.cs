namespace NoneadCSS.Application.DTOs.Responses;

public class SystemStatusResponse
{
    public string Status { get; set; } = string.Empty;
    public string Version { get; set; } = string.Empty;
    public string Environment { get; set; } = string.Empty;
    public DateTime Timestamp { get; set; }
    public DatabaseStatus Database { get; set; } = new();
    public SystemResources Resources { get; set; } = new();
    public List<MailboxStatus> Mailboxes { get; set; } = [];
}

public class DatabaseStatus
{
    public bool Connected { get; set; }
    public string Provider { get; set; } = string.Empty;
    public int TableCount { get; set; }
}

public class SystemResources
{
    public int Conversations { get; set; }
    public int Customers { get; set; }
    public int Users { get; set; }
    public int Mailboxes { get; set; }
    public int SendLogs { get; set; }
    public int Webhooks { get; set; }
    public int Workflows { get; set; }
    public int Macros { get; set; }
}

public class MailboxStatus
{
    public int Id { get; set; }
    public string Name { get; set; } = string.Empty;
    public string Email { get; set; } = string.Empty;
    public int UserCount { get; set; }
    public bool HasPassword { get; set; }
    public bool IsActive { get; set; }
}

public class SystemInfoResponse
{
    public string Version { get; set; } = string.Empty;
    public string Platform { get; set; } = string.Empty;
    public string OSVersion { get; set; } = string.Empty;
    public string Framework { get; set; } = string.Empty;
    public string Environment { get; set; } = string.Empty;
    public string MachineName { get; set; } = string.Empty;
    public int ProcessorCount { get; set; }
    public long WorkingSet { get; set; }
    public DateTime StartupTime { get; set; }
    public string[] Features { get; set; } = [];
}

public class HealthCheckItem
{
    public string Name { get; set; } = string.Empty;
    public string Status { get; set; } = string.Empty;
    public string Message { get; set; } = string.Empty;
}
