using System.ComponentModel.DataAnnotations;

namespace NoneadCSS.Application.DTOs.Requests;

public class UpdateUserStatusRequest : IValidatableObject
{
    public int Status { get; set; }

    public IEnumerable<ValidationResult> Validate(ValidationContext validationContext)
    {
        if (!Enum.IsDefined(typeof(Domain.Enums.UserStatus), Status))
        {
            yield return new ValidationResult(
                $"状态值 '{Status}' 无效，有效值为：{string.Join(", ", Enum.GetNames<Domain.Enums.UserStatus>())}",
                new[] { nameof(Status) });
        }
    }
}
