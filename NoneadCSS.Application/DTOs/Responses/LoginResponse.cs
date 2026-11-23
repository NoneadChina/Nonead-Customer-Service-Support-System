namespace NoneadCSS.Application.DTOs.Responses;

public class LoginResponse
{
    public bool Success { get; set; }
    public string Token { get; set; } = string.Empty;
    public string? RefreshToken { get; set; }
    public UserResponse? User { get; set; }
    public string? ErrorMessage { get; set; }
}
