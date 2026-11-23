using System.ComponentModel.DataAnnotations;
using NoneadCSS.Domain.Enums;

namespace NoneadCSS.Application.DTOs.Requests;

/// <summary>
/// 更新会话优先级请求
/// </summary>
public class UpdateConversationPriorityRequest : IValidatableObject
{
    /// <summary>
    /// 优先级：0=Low、1=Normal、2=High、3=Urgent
    /// </summary>
    [Range(0, 3, ErrorMessage = "优先级值无效")]
    public int Priority { get; set; }

    public IEnumerable<ValidationResult> Validate(ValidationContext validationContext)
    {
        if (!Enum.IsDefined(typeof(ConversationPriority), Priority))
        {
            yield return new ValidationResult(
                $"优先级值 '{Priority}' 无效，有效值为：{string.Join(", ", Enum.GetNames<ConversationPriority>())}",
                new[] { nameof(Priority) });
        }
    }
}
