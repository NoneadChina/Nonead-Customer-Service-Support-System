using System.ComponentModel.DataAnnotations;
using NoneadCSS.Domain.Enums;

namespace NoneadCSS.Application.DTOs.Requests;

public class UpdateConversationStatusRequest : IValidatableObject
{
    [Range(0, 10, ErrorMessage = "状态值无效")]
    public int Status { get; set; }

    public IEnumerable<ValidationResult> Validate(ValidationContext validationContext)
    {
        if (!Enum.IsDefined(typeof(ConversationStatus), Status))
        {
            yield return new ValidationResult(
                $"状态值 '{Status}' 无效，有效值为：{string.Join(", ", Enum.GetNames<ConversationStatus>())}",
                new[] { nameof(Status) });
        }
    }
}
