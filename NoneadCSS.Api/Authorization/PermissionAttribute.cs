using Microsoft.AspNetCore.Authorization;

namespace NoneadCSS.Api.Authorization;

public class PermissionAttribute : AuthorizeAttribute
{
    public PermissionAttribute(string permission)
    {
        Policy = $"Permission_{permission}";
    }
}