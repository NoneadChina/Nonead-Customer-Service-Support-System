namespace NoneadCSS.Application.DTOs.Requests;

public class OAuthTokenRequest
{
    public string Provider { get; set; } = string.Empty;
    public string AccessToken { get; set; } = string.Empty;
    public string? RefreshToken { get; set; }
    public DateTime? ExpiresAt { get; set; }
    public string State { get; set; } = string.Empty;
}
