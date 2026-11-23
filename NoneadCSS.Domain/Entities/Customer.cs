using System.ComponentModel.DataAnnotations.Schema;

namespace NoneadCSS.Domain.Entities;

public class Customer : ISoftDelete
{
    public int Id { get; set; }
    public string FirstName { get; set; } = string.Empty;
    public string LastName { get; set; } = string.Empty;
    public string? Address { get; set; }
    public string? City { get; set; }
    public string? State { get; set; }
    public string? Zip { get; set; }
    public string? Country { get; set; }
    public string? Company { get; set; }
    public string? JobTitle { get; set; }
    public string? Notes { get; set; }
    public string? Channel { get; set; }
    public string? ChannelId { get; set; }
    public string Phones { get; set; } = string.Empty;
    public string SocialProfiles { get; set; } = string.Empty;
    public string Websites { get; set; } = string.Empty;
    public string? Avatar { get; set; }
    /// <summary>
    /// 联系方式提取尝试次数（新会话及客户回复引导消息时 TryExtractContactInfoFromMessageAsync 的调用次数）。
    /// 达到 MaxContactExtractAttempts（3）后，即使字段还缺也不再尝试提取，避免每条消息反复跑正则。
    /// </summary>
    public int ContactExtractAttempts { get; set; }
    public DateTime CreatedAt { get; set; } = DateTime.UtcNow;
    public DateTime UpdatedAt { get; set; } = DateTime.UtcNow;
    public DateTime? DeletedAt { get; set; }

    /// <summary>
    /// 乐观锁 RowVersion；GetOrCreateByEmailAsync 并发、客服编辑资料/打标签、渠道同步回写客户画像等场景。
    /// </summary>
    public byte[]? RowVersion { get; set; }

    public ICollection<Email> Emails { get; set; } = [];

    [NotMapped]
    public ICollection<Conversation> Conversations { get; set; } = [];

    [NotMapped]
    public ICollection<ConversationThread> Threads { get; set; } = [];

    public ICollection<SendLog> SendLogs { get; set; } = [];

    public string? GetMainEmail() => Emails.FirstOrDefault()?.EmailAddress;

    public string GetFullName(bool emailIfEmpty = false)
    {
        var fullName = $"{FirstName} {LastName}".Trim();
        if (!string.IsNullOrEmpty(fullName))
        {
            return fullName;
        }
        if (emailIfEmpty)
        {
            return GetMainEmail() ?? string.Empty;
        }
        return string.Empty;
    }

    /// <summary>
    /// 获取客户电话号码列表
    /// </summary>
    /// <returns>电话号码列表，JSON 解析失败时返回空列表</returns>
    public List<CustomerPhone> GetPhones()
    {
        if (string.IsNullOrEmpty(Phones))
        {
            return [];
        }
        try
        {
            return System.Text.Json.JsonSerializer.Deserialize<List<CustomerPhone>>(Phones) ?? [];
        }
        catch (System.Text.Json.JsonException)
        {
            // JSON 格式无效时降级返回空列表，避免影响主流程
            return [];
        }
    }

    public void SetPhones(List<CustomerPhone> phones)
    {
        Phones = System.Text.Json.JsonSerializer.Serialize(phones);
    }

    /// <summary>
    /// 获取客户社交资料列表
    /// </summary>
    /// <returns>社交资料列表，JSON 解析失败时返回空列表</returns>
    public List<CustomerSocialProfile> GetSocialProfiles()
    {
        if (string.IsNullOrEmpty(SocialProfiles))
        {
            return [];
        }
        try
        {
            return System.Text.Json.JsonSerializer.Deserialize<List<CustomerSocialProfile>>(SocialProfiles) ?? [];
        }
        catch (System.Text.Json.JsonException)
        {
            // JSON 格式无效时降级返回空列表，避免影响主流程
            return [];
        }
    }

    public void SetSocialProfiles(List<CustomerSocialProfile> socialProfiles)
    {
        SocialProfiles = System.Text.Json.JsonSerializer.Serialize(socialProfiles);
    }

    /// <summary>
    /// 获取客户网站列表
    /// </summary>
    /// <returns>网站列表，JSON 解析失败时返回空列表</returns>
    public List<string> GetWebsites()
    {
        if (string.IsNullOrEmpty(Websites))
        {
            return [];
        }
        try
        {
            return System.Text.Json.JsonSerializer.Deserialize<List<string>>(Websites) ?? [];
        }
        catch (System.Text.Json.JsonException)
        {
            // JSON 格式无效时降级返回空列表，避免影响主流程
            return [];
        }
    }

    public void SetWebsites(List<string> websites)
    {
        Websites = System.Text.Json.JsonSerializer.Serialize(websites);
    }
}

public class CustomerPhone
{
    public string Value { get; set; } = string.Empty;
    public int Type { get; set; } = 1;
    public string? N { get; set; }
}

public class CustomerSocialProfile
{
    public string Value { get; set; } = string.Empty;
    public int Type { get; set; }
}
