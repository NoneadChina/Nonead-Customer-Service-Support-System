using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using System.Security.Claims;

namespace NoneadCSS.Api.Controllers;

/// <summary>
/// 控制器基类 - 集中提供当前用户 ID 提取逻辑与通用特性。
/// 派生类自动继承 [ApiController] 与 [Authorize]，无需重复声明。
/// </summary>
[ApiController]
[Authorize]
public abstract class BaseController : ControllerBase
{
    /// <summary>
    /// 从当前 ClaimsPrincipal 中提取用户 ID。
    /// claim 缺失或无法解析时返回 null，调用方应使用 ?? 0 兼容旧逻辑或显式校验。
    /// </summary>
    protected int? CurrentUserId
    {
        get
        {
            var claim = User.FindFirst(ClaimTypes.NameIdentifier)?.Value;
            return int.TryParse(claim, out var id) ? id : null;
        }
    }
}
