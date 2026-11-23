using System.ComponentModel.DataAnnotations;
using NoneadCSS.Domain.Enums;

namespace NoneadCSS.Application.DTOs.Requests;

public class CreateConversationRequest : IValidatableObject
{
    [Required(ErrorMessage = "邮箱ID不能为空")]
    [Range(1, int.MaxValue, ErrorMessage = "邮箱ID必须大于0")]
    public int MailboxId { get; set; }

    public int? CustomerId { get; set; }

    [EmailAddress(ErrorMessage = "无效的客户邮箱格式")]
    [MaxLength(255, ErrorMessage = "客户邮箱最多 255 个字符")]
    public string CustomerEmail { get; set; } = string.Empty;

    [MaxLength(500, ErrorMessage = "主题最多 500 个字符")]
    public string Subject { get; set; } = string.Empty;

    [MaxLength(50000, ErrorMessage = "正文最多 50000 个字符")]
    public string Body { get; set; } = string.Empty;

    public int? UserId { get; set; }

    [Range(1, 3, ErrorMessage = "类型值无效")]
    public int Type { get; set; } = 1;

    [Range(1, 10, ErrorMessage = "来源值无效")]
    public int SourceVia { get; set; } = 1;

    /// <summary>
    /// 优先级：0=Low、1=Normal、2=High、3=Urgent（默认 Normal）
    /// </summary>
    [Range(0, 3, ErrorMessage = "优先级值无效")]
    public int Priority { get; set; } = 1;

    public IEnumerable<ValidationResult> Validate(ValidationContext validationContext)
    {
        if (!Enum.IsDefined(typeof(ConversationType), Type))
        {
            yield return new ValidationResult(
                $"类型值 '{Type}' 无效，有效值为：{string.Join(", ", Enum.GetNames<ConversationType>())}",
                new[] { nameof(Type) });
        }

        if (!Enum.IsDefined(typeof(SourceVia), SourceVia))
        {
            yield return new ValidationResult(
                $"来源值 '{SourceVia}' 无效，有效值为：{string.Join(", ", Enum.GetNames<SourceVia>())}",
                new[] { nameof(SourceVia) });
        }

        if (!Enum.IsDefined(typeof(ConversationPriority), Priority))
        {
            yield return new ValidationResult(
                $"优先级值 '{Priority}' 无效，有效值为：{string.Join(", ", Enum.GetNames<ConversationPriority>())}",
                new[] { nameof(Priority) });
        }
    }
}
