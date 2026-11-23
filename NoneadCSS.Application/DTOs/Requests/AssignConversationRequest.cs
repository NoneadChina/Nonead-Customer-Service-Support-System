using System.ComponentModel.DataAnnotations;

namespace NoneadCSS.Application.DTOs.Requests;

public class AssignConversationRequest
{
    [Range(1, int.MaxValue, ErrorMessage = "用户ID必须大于0")]
    public int UserId { get; set; }
}
