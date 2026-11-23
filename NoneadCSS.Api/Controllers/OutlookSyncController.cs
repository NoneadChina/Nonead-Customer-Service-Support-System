using System.Security.Cryptography;
using System.Text;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.RateLimiting;
using Microsoft.EntityFrameworkCore;
using NoneadCSS.Api.Authentication;
using NoneadCSS.Api.Authorization;
using NoneadCSS.Api.Filters;
using NoneadCSS.Application.DTOs.Responses;
using NoneadCSS.Application.Services;
using NoneadCSS.Domain;
using NoneadCSS.Domain.Repositories;
using NoneadCSS.Domain.Services;

namespace NoneadCSS.Api.Controllers;

/// <summary>
/// Outlook 日历双向同步控制器
/// 提供 OAuth 授权、同步设置管理、同步触发等接口
/// T000099: 新增 /api/outlooksync/credentials 端点，每个用户可自配置自己的 Azure AD 凭据
/// </summary>
[ApiController]
[Route("api/outlooksync")]
[Authorize]
[RequireModule("calendar")]
public class OutlookSyncController : ControllerBase
{
    private readonly IOutlookSyncService _syncService;
    private readonly IOutlookOAuthService _oauthService;
    private readonly ICurrentUserAccessor _currentUserAccessor;
    private readonly IEncryptionService _encryptionService;
    private readonly IWebHostEnvironment _env;
    private readonly ILogger<OutlookSyncController> _logger;
    private readonly IOutlookSyncLogRepository _syncLogRepository;
    private readonly IMailboxRepository _mailboxRepository;
    // T000099: 用于读取用户级有效配置（用户级凭据 > appsettings 基线）
    private readonly IOutlookSettingsProvider _settingsProvider;
    private readonly IUnitOfWork _unitOfWork;

    /// <summary>
    /// H-O3: OAuth 会话 Cookie 名称。SameSite=Lax 以便跨站点重定向（Microsoft → 本站）时携带，
    /// 用于在回调时校验发起 OAuth 的浏览器仍处于登录态，防止 state 被盗用后他人冒充完成授权。
    /// </summary>
    private const string OAuthSessionCookieName = "outlook_oauth_session";
    private const int OAuthSessionTtlMinutes = 10;

    public OutlookSyncController(
        IOutlookSyncService syncService,
        IOutlookOAuthService oauthService,
        ICurrentUserAccessor currentUserAccessor,
        IEncryptionService encryptionService,
        IWebHostEnvironment env,
        ILogger<OutlookSyncController> logger,
        IOutlookSyncLogRepository syncLogRepository,
        IMailboxRepository mailboxRepository,
        IOutlookSettingsProvider settingsProvider,
        IUnitOfWork unitOfWork)
    {
        _oauthService = oauthService;
        _syncService = syncService;
        _currentUserAccessor = currentUserAccessor;
        _encryptionService = encryptionService;
        _env = env;
        _logger = logger;
        _syncLogRepository = syncLogRepository;
        _mailboxRepository = mailboxRepository;
        _settingsProvider = settingsProvider;
        _unitOfWork = unitOfWork;
    }

    /// <summary>
    /// 获取当前用户的同步状态（前端轮询使用）
    /// </summary>
    [HttpGet("status")]
    public async Task<IActionResult> GetStatus()
    {
        var userId = _currentUserAccessor.UserId;
        if (userId == 0) return Unauthorized();

        var status = await _syncService.GetStatusAsync(userId);
        return Ok(status);
    }

    /// <summary>
    /// 获取当前用户的同步设置
    /// </summary>
    [HttpGet("setting")]
    public async Task<IActionResult> GetSetting()
    {
        var userId = _currentUserAccessor.UserId;
        if (userId == 0) return Unauthorized();

        var setting = await _syncService.GetSettingAsync(userId);
        return Ok(setting);
    }

    /// <summary>
    /// 更新同步设置（启用/禁用、同步方向）
    /// </summary>
    [HttpPut("setting")]
    [Permission("calendar.manage")]
    public async Task<IActionResult> UpdateSetting([FromBody] UpdateOutlookSyncSettingRequest request)
    {
        var userId = _currentUserAccessor.UserId;
        if (userId == 0) return Unauthorized();

        var setting = await _syncService.UpdateSettingAsync(userId, request);
        return Ok(setting);
    }

    /// <summary>
    /// 生成 OAuth 授权 URL（前端跳转到此 URL 进行授权）
    /// T000099: 使用用户级 Azure AD 凭据生成授权 URL（每个用户自配置的 ClientId/RedirectUri）
    /// </summary>
    [HttpGet("oauth/authorize")]
    public async Task<IActionResult> GetAuthorizeUrl()
    {
        var userId = _currentUserAccessor.UserId;
        if (userId == 0) return Unauthorized();

        try
        {
            var (url, state) = await _oauthService.GetAuthorizeUrlAsync(userId);
            // H-O3: 设置 OAuth 会话 Cookie（SameSite=Lax），用于回调时校验登录态。
            // access_token Cookie 为 SameSite=Strict，在 Microsoft → 本站的跨站点重定向中不会被携带，
            // 因此需要单独的 Lax Cookie 证明回调发起者即授权发起者。
            SetOAuthSessionCookie(userId);
            return Ok(new { authorizeUrl = url, state });
        }
        catch (InvalidOperationException ex)
        {
            _logger.LogWarning(ex, "生成 Outlook OAuth 授权 URL 失败：Azure AD 未配置");
            return BadRequest(new { message = ex.Message });
        }
    }

    /// <summary>
    /// OAuth 回调端点（Azure AD 重定向到此 URL）
    /// 注意：此端点允许匿名访问，因为回调时浏览器未携带 JWT（access_token Cookie 为 SameSite=Strict）
    /// H-O3: 通过 outlook_oauth_session Cookie（SameSite=Lax）校验登录态，确保回调发起者即授权发起者
    /// H-06: state 参数抗 CSRF 已确认——BuildState 使用 IEncryptionService 加密
    ///   {domain}|{userId}|{expiresAt(ISO)}|{nonce(128bit)}，回调时 TryParseStateAsync
    ///   解密+验签+10 分钟有效期+分布式缓存 nonce 防重放；Controller 二次校验
    ///   state.userId == sessionCookie.userId，三层防护防止账号绑定劫持。
    /// </summary>
    [HttpGet("oauth/callback")]
    [AllowAnonymous]
    public async Task<IActionResult> OAuthCallback([FromQuery] string? code, [FromQuery] string? state,
        [FromQuery] string? error, [FromQuery] string? error_description)
    {
        // OAuth 错误处理
        if (!string.IsNullOrEmpty(error))
        {
            _logger.LogWarning("Outlook OAuth 授权失败：{Error} - {Description}", error, error_description);
            ClearOAuthSessionCookie();
            var errUrl = $"/calendar?outlook_error={Uri.EscapeDataString(error)}";
            return Redirect(errUrl);
        }

        if (string.IsNullOrEmpty(code) || string.IsNullOrEmpty(state))
        {
            return BadRequest(new { message = "缺少 code 或 state 参数" });
        }

        // H-O3: 校验 OAuth 会话 Cookie，确保回调来自同一浏览器的授权发起者
        if (!TryValidateOAuthSessionCookie(out var sessionUserId))
        {
            _logger.LogWarning("Outlook OAuth 回调失败：缺少或无效的会话 Cookie（登录态校验未通过）");
            return Redirect("/calendar?outlook_error=session_expired");
        }
        ClearOAuthSessionCookie();

        var userId = await _syncService.HandleOAuthCallbackAsync(code, state);
        if (userId == null)
        {
            return Redirect("/calendar?outlook_error=callback_failed");
        }

        // H-O3: 二次校验——state 中的 userId 必须与会话 Cookie 中的 userId 一致
        if (userId.Value != sessionUserId)
        {
            _logger.LogWarning("Outlook OAuth 回调 userId 不匹配：会话 Cookie={SessionUserId}，state={StateUserId}", sessionUserId, userId);
            return Redirect("/calendar?outlook_error=session_mismatch");
        }

        _logger.LogInformation("Outlook OAuth 授权成功，用户 ID={UserId}", userId);
        return Redirect("/calendar?outlook_connected=1");
    }

    /// <summary>
    /// 触发一次同步（用户主动点击同步按钮）
    /// </summary>
    [HttpPost("sync")]
    [Permission("calendar.manage")]
    [EnableRateLimiting("CalendarHeavyPolicy")]
    public async Task<IActionResult> SyncNow([FromQuery] bool force = false)
    {
        var userId = _currentUserAccessor.UserId;
        if (userId == 0) return Unauthorized();

        _logger.LogInformation("用户 {UserId} 手动触发 Outlook 同步，force={Force}", userId, force);
        var result = await _syncService.SyncAsync(userId, force);
        return Ok(result);
    }

    /// <summary>
    /// 断开 Outlook 连接（清除令牌，保留设置记录）
    /// </summary>
    [HttpDelete("disconnect")]
    [Permission("calendar.manage")]
    public async Task<IActionResult> Disconnect()
    {
        var userId = _currentUserAccessor.UserId;
        if (userId == 0) return Unauthorized();

        var ok = await _syncService.DisconnectAsync(userId);
        if (!ok) return NotFound(new { message = "未找到同步设置" });
        return Ok(new { message = "已断开 Outlook 连接" });
    }

    /// <summary>
    /// 任务 D#2：查询 Outlook 同步审计日志（按 mailboxId 分页）。
    /// 路由：GET /api/calendar/outlook/sync-logs（覆盖默认前缀）。
    /// </summary>
    [HttpGet("~/api/calendar/outlook/sync-logs")]
    [Permission("calendar.manage")]
    public async Task<IActionResult> GetSyncLogs([FromQuery] int mailboxId, [FromQuery] int page = 1, [FromQuery] int pageSize = 20)
    {
        var userId = _currentUserAccessor.UserId;
        if (userId == 0) return Unauthorized();

        // S6: mailboxId 必须校验归属，非超管只能查看其可访问邮箱的同步日志
        if (!await _currentUserAccessor.CanAccessMailboxAsync(mailboxId))
            return StatusCode(StatusCodes.Status403Forbidden, new { message = "无权访问该邮箱" });

        // 权限校验：用户必须有权限访问该 mailbox（要么是管理员，要么是该 mailbox 相关用户）
        var mailbox = await _mailboxRepository.GetByIdAsync(mailboxId);
        if (mailbox == null) return NotFound(new { message = "Mailbox 不存在" });

        // 简单的权限控制：管理员角色可查看全部；非管理员暂只允许查看用户自建的（后续可按需要扩展 mailbox.UserId 关系）
        // 这里若缺少 UserId 关联，我们至少不阻止管理员（Permission 已要求 calendar.manage）。
        if (page < 1) page = 1;
        if (pageSize < 1) pageSize = 20;
        if (pageSize > 200) pageSize = 200;

        var (items, total) = await _syncLogRepository.ListByMailboxIdAsync(mailboxId, page, pageSize);

        var dtos = items.Select(x => new OutlookSyncLogDto
        {
            Id = x.Id,
            MailboxId = x.MailboxId,
            Direction = x.Direction,
            StartedAt = x.StartedAt,
            FinishedAt = x.FinishedAt,
            Status = x.Status,
            CreatedCount = x.CreatedCount,
            UpdatedCount = x.UpdatedCount,
            DeletedCount = x.DeletedCount,
            ErrorCount = x.ErrorCount,
            ErrorMessage = x.ErrorMessage,
            CreatedAt = x.CreatedAt
        }).ToList();

        var totalPages = pageSize > 0 ? (int)Math.Ceiling((double)total / pageSize) : 0;
        return Ok(new OutlookSyncLogListResponse
        {
            Data = dtos,
            CurrentPage = page,
            TotalPages = totalPages,
            TotalItems = total,
            PageSize = pageSize
        });
    }

    /// <summary>
    /// H-O3: 设置 OAuth 会话 Cookie。SameSite=Lax 以便 Microsoft → 本站的顶层重定向携带。
    /// Cookie 值为加密后的 "{userId}|{nonce}"，仅一次性使用。
    /// </summary>
    private void SetOAuthSessionCookie(int userId)
    {
        var nonce = RandomNumberGenerator.GetHexString(16, lowercase: false);
        var payload = $"{userId}|{nonce}";
        var encrypted = _encryptionService.Encrypt(payload);
        Response.Cookies.Append(OAuthSessionCookieName, encrypted, new CookieOptions
        {
            HttpOnly = true,
            Secure = !_env.IsDevelopment(),
            // Lax 允许跨站点顶层 GET 重定向携带（Microsoft 回调），同时阻挡跨站点子请求/POST
            SameSite = SameSiteMode.Lax,
            Path = "/api/outlooksync",
            Expires = DateTimeOffset.UtcNow.AddMinutes(OAuthSessionTtlMinutes)
        });
    }

    /// <summary>
    /// H-O3: 校验 OAuth 会话 Cookie，提取并返回其中的 userId。
    /// </summary>
    private bool TryValidateOAuthSessionCookie(out int userId)
    {
        userId = 0;
        var cookieValue = Request.Cookies[OAuthSessionCookieName];
        if (string.IsNullOrEmpty(cookieValue)) return false;

        try
        {
            if (!_encryptionService.TryDecrypt(cookieValue, out var plain) || string.IsNullOrEmpty(plain))
                return false;

            var parts = plain!.Split('|');
            if (parts.Length != 2) return false;
            return int.TryParse(parts[0], out userId) && userId > 0;
        }
        catch
        {
            return false;
        }
    }

    /// <summary>
    /// H-O3: 清除 OAuth 会话 Cookie（一次性使用）。
    /// </summary>
    private void ClearOAuthSessionCookie()
    {
        Response.Cookies.Delete(OAuthSessionCookieName, new CookieOptions
        {
            HttpOnly = true,
            Secure = !_env.IsDevelopment(),
            SameSite = SameSiteMode.Lax,
            Path = "/api/outlooksync"
        });
    }

    /// <summary>
    /// T000099: 获取当前用户的 Outlook 同步凭据配置（Azure AD 应用注册凭据）。
    /// 每个用户可自配置自己的 Azure AD 应用注册凭据，空值回退到 appsettings 基线。
    /// 返回脱敏配置信息（ClientSecret 仅返回是否存在标识，不返回明文）。
    /// 路由：GET /api/outlooksync/credentials
    /// </summary>
    [HttpGet("credentials")]
    public async Task<ActionResult<OutlookAdminSettingsDto>> GetCredentials()
    {
        var userId = _currentUserAccessor.UserId;
        if (userId == 0) return Unauthorized();

        // T000099: 读取用户级有效配置（用户级 > 全局基线）
        var effectiveOptions = await _settingsProvider.GetForUserAsync(userId);

        // 查询用户的 OutlookSyncSetting 记录是否存在及凭据字段是否已配置
        var repo = _unitOfWork.Repository<Domain.Entities.OutlookSyncSetting>();
        var setting = await repo.GetQueryable(true)
            .Where(s => s.UserId == userId && s.DeletedAt == null)
            .Select(s => new
            {
                s.ClientId,
                s.ClientSecret,
                s.TenantId,
                s.RedirectUri,
                s.Scopes,
                s.UpdatedAt
            })
            .FirstOrDefaultAsync();

        var hasUserConfig = !string.IsNullOrWhiteSpace(setting?.ClientId)
            || !string.IsNullOrWhiteSpace(setting?.ClientSecret)
            || !string.IsNullOrWhiteSpace(setting?.TenantId)
            || !string.IsNullOrWhiteSpace(setting?.RedirectUri)
            || !string.IsNullOrWhiteSpace(setting?.Scopes);

        var hasClientSecret = !string.IsNullOrWhiteSpace(setting?.ClientSecret)
            ? true
            : !string.IsNullOrWhiteSpace(effectiveOptions.ClientSecret);

        return Ok(new OutlookAdminSettingsDto
        {
            IsConfigured = effectiveOptions.IsConfigured,
            ClientId = effectiveOptions.ClientId,
            HasClientSecret = hasClientSecret,
            TenantId = effectiveOptions.TenantId,
            RedirectUri = effectiveOptions.RedirectUri,
            Scopes = effectiveOptions.Scopes,
            Source = hasUserConfig ? "user" : "appsettings",
            UpdatedAt = setting?.UpdatedAt
        });
    }

    /// <summary>
    /// T000099: 更新当前用户的 Outlook 同步凭据配置（Azure AD 应用注册凭据）。
    /// 每个用户可自配置自己的凭据，支持部分更新（仅非 null 字段会被写入）。
    /// ClientSecret 特殊值 "CLEAR" 表示清空已配置的密钥；空字符串/null 表示不更新。
    /// ClientSecret 由 EF Core HasConversion 自动加密存储，无需手动加密。
    /// 路由：PUT /api/outlooksync/credentials
    /// </summary>
    [HttpPut("credentials")]
    [Permission("calendar.manage")]
    public async Task<ActionResult<OutlookAdminSettingsDto>> UpdateCredentials(
        [FromBody] UpdateOutlookAdminSettingsRequest request)
    {
        var userId = _currentUserAccessor.UserId;
        if (userId == 0) return Unauthorized();

        if (request == null)
            return BadRequest(new { message = "请求体不能为空" });

        // 获取或创建用户的 OutlookSyncSetting 记录（追踪查询，修改后 SaveChanges 自动持久化）
        var repo = _unitOfWork.Repository<Domain.Entities.OutlookSyncSetting>();
        var setting = await repo.GetQueryable(false)
            .Where(s => s.UserId == userId && s.DeletedAt == null)
            .FirstOrDefaultAsync();

        if (setting == null)
        {
            setting = new Domain.Entities.OutlookSyncSetting
            {
                UserId = userId,
                SyncEnabled = false, // 凭据配置阶段尚未连接，先禁用同步
                SyncDirection = "both",
                CreatedAt = DateTime.UtcNow,
                UpdatedAt = DateTime.UtcNow
            };
            await repo.AddAsync(setting);
        }

        // ClientId 更新（空字符串视为清空，null 表示不更新）
        if (request.ClientId != null)
        {
            setting.ClientId = string.IsNullOrWhiteSpace(request.ClientId) ? null : request.ClientId;
        }

        // ClientSecret 更新：特殊值 "CLEAR" 清空，非空字符串赋值（EF Core 自动加密），null 不更新
        if (request.ClientSecret != null)
        {
            if (request.ClientSecret == "CLEAR")
            {
                setting.ClientSecret = null;
            }
            else if (!string.IsNullOrWhiteSpace(request.ClientSecret))
            {
                setting.ClientSecret = request.ClientSecret;
            }
        }

        // TenantId 更新
        if (request.TenantId != null)
        {
            setting.TenantId = string.IsNullOrWhiteSpace(request.TenantId) ? null : request.TenantId;
        }

        // RedirectUri 更新
        if (request.RedirectUri != null)
        {
            setting.RedirectUri = string.IsNullOrWhiteSpace(request.RedirectUri) ? null : request.RedirectUri;
        }

        // Scopes 更新
        if (request.Scopes != null)
        {
            setting.Scopes = string.IsNullOrWhiteSpace(request.Scopes) ? null : request.Scopes;
        }

        setting.UpdatedAt = DateTime.UtcNow;
        await _unitOfWork.SaveChangesAsync();

        _logger.LogInformation("用户 {UserId} 已更新自己的 Outlook Azure AD 凭据配置", userId);

        return await GetCredentials();
    }
}
