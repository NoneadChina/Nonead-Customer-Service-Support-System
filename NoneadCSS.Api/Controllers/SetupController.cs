using System.ComponentModel.DataAnnotations;
using NoneadCSS.Api.Services;
using NoneadCSS.Application.DTOs.Requests;
using NoneadCSS.Application.Services;
using NoneadCSS.Domain;
using NoneadCSS.Domain.Entities;
using NoneadCSS.Domain.Enums;
using NoneadCSS.Domain.Services;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.RateLimiting;
using Microsoft.EntityFrameworkCore;

namespace NoneadCSS.Api.Controllers;

[ApiController]
[Route("api/[controller]")]
[AllowAnonymous]
[EnableRateLimiting("SetupPolicy")]
public class SetupController : ControllerBase
{
    private const string SetupTokenHeader = "X-Setup-Token";

    private readonly IUnitOfWork _unitOfWork;
    private readonly IAuthService _authService;
    private readonly IMailboxService _mailboxService;
    private readonly IPasswordPolicyService _passwordPolicyService;
    private readonly IEncryptionService _encryptionService;
    private readonly ISetupTokenService _setupTokenService;

    // 安装流程全局锁：防止并发请求重复创建管理员/邮箱或重复完成安装
    // 安装为一次性操作，内存锁足够应对单实例并发；数据库唯一索引作为最终防线
    private static readonly SemaphoreSlim _setupLock = new(1, 1);

    public SetupController(IUnitOfWork unitOfWork, IAuthService authService, IMailboxService mailboxService, IPasswordPolicyService passwordPolicyService, IEncryptionService encryptionService, ISetupTokenService setupTokenService)
    {
        _unitOfWork = unitOfWork;
        _authService = authService;
        _mailboxService = mailboxService;
        _passwordPolicyService = passwordPolicyService;
        _encryptionService = encryptionService;
        _setupTokenService = setupTokenService;
    }

    [HttpGet("status")]
    public async Task<IActionResult> GetSetupStatus()
    {
        var hasUsers = await _unitOfWork.Users.GetQueryable().AnyAsync();
        var hasMailboxes = await _unitOfWork.Mailboxes.GetQueryable().AnyAsync();
        var installedValue = await _unitOfWork.SystemOptions.GetValueAsync("installed");
        var isInstalled = string.Equals(installedValue, "true", StringComparison.OrdinalIgnoreCase);

        return Ok(new
        {
            isInstalled,
            // 已完成安装时固定返回完成态（4），避免前端看到「step=3 未装完」而实际系统早已投产的语义矛盾。
            step = isInstalled ? 4 : GetCurrentStep(hasUsers, hasMailboxes),
            hasUsers,
            hasMailboxes,
            // 语义修正：这里判断的是「是否已写入安装标记」，原字段名 hasSystemOptions 有误导（并非检查系统配置项）。
            hasInstallMarker = !string.IsNullOrEmpty(installedValue),
            // 保留旧字段名向后兼容（前端可能仍读它），取值与 hasInstallMarker 一致
            hasSystemOptions = !string.IsNullOrEmpty(installedValue)
        });
    }

    [HttpPost("admin")]
    public async Task<IActionResult> CreateAdminUser([FromBody] CreateAdminRequest request)
    {
        var tokenDenial = CheckSetupToken();
        if (tokenDenial != null)
        {
            return tokenDenial;
        }

        await _setupLock.WaitAsync();
        try
        {
            // 已安装系统禁止再次创建管理员，防止重复执行或绕过流程
            if (await IsSystemInstalledAsync())
            {
                return StatusCode(StatusCodes.Status403Forbidden, new { message = "系统已安装，禁止重复执行安装操作" });
            }

            var hasUsers = await _unitOfWork.Users.GetQueryable().AnyAsync();
            if (hasUsers)
            {
                // 用户已存在（含种子管理员）时拒绝重复创建，属于安全拒绝而非请求错误
                return StatusCode(StatusCodes.Status403Forbidden, new { message = "管理员用户已存在" });
            }

            var pwdValidation = await _passwordPolicyService.ValidateAsync(request.Password);
            if (!pwdValidation.IsValid)
            {
                return BadRequest(new { message = string.Join("；", pwdValidation.Errors) });
            }

            var user = new User
            {
                Email = request.Email,
                Password = BCrypt.Net.BCrypt.HashPassword(request.Password),
                FirstName = request.FirstName,
                LastName = request.LastName,
                Role = UserRoleType.SuperAdmin,
                Status = UserStatus.Active,
                Locale = "zh-Hans",
                Timezone = "Asia/Shanghai",
                CreatedAt = DateTime.UtcNow,
                UpdatedAt = DateTime.UtcNow
            };

            await _unitOfWork.Users.AddAsync(user);
            await _unitOfWork.SaveChangesAsync();

            return Ok(new { message = "管理员用户创建成功", userId = user.Id });
        }
        finally
        {
            _setupLock.Release();
        }
    }

    [HttpPost("mailbox")]
    public async Task<IActionResult> CreateMailbox([FromBody] SetupMailboxRequest request)
    {
        var tokenDenial = CheckSetupToken();
        if (tokenDenial != null)
        {
            return tokenDenial;
        }

        await _setupLock.WaitAsync();
        try
        {
            // 已安装系统禁止再次创建邮箱，防止重复执行或绕过流程
            if (await IsSystemInstalledAsync())
            {
                return StatusCode(StatusCodes.Status403Forbidden, new { message = "系统已安装，禁止重复执行安装操作" });
            }

            var hasMailboxes = await _unitOfWork.Mailboxes.GetQueryable().AnyAsync();
            if (hasMailboxes)
            {
                return StatusCode(StatusCodes.Status403Forbidden, new { message = "邮箱已配置" });
            }

            var mailbox = new Mailbox
            {
                Name = request.Name,
                Email = request.Email,
                InProtocol = InProtocol.Imap,
                InHost = request.InHost,
                InPort = request.InPort,
                InEncryption = (EncryptionType)request.InEncryption,
                InValidateCert = true,
                InUsername = request.InUsername,
                // 安全要求：邮箱密码必须加密后再写入数据库，防止明文存储敏感凭据
                InPassword = _encryptionService.Encrypt(request.InPassword),
                ImapSentFolder = "Sent",
                OutMethod = OutMethod.Smtp,
                OutHost = request.OutHost,
                OutPort = request.OutPort,
                OutEncryption = (EncryptionType)request.OutEncryption,
                OutUsername = request.OutUsername,
                OutPassword = _encryptionService.Encrypt(request.OutPassword),
                IsActive = true,
                CreatedAt = DateTime.UtcNow,
                UpdatedAt = DateTime.UtcNow
            };

            await _unitOfWork.Mailboxes.AddAsync(mailbox);
            await _unitOfWork.SaveChangesAsync();

            return Ok(new { message = "邮箱配置成功", mailboxId = mailbox.Id });
        }
        finally
        {
            _setupLock.Release();
        }
    }

    [HttpPost("complete")]
    public async Task<IActionResult> CompleteSetup()
    {
        var tokenDenial = CheckSetupToken();
        if (tokenDenial != null)
        {
            return tokenDenial;
        }

        await _setupLock.WaitAsync();
        try
        {
            // 已安装系统禁止重复完成安装，防止重置 installation_date 或注入额外的 MailboxUser
            if (await IsSystemInstalledAsync())
            {
                return StatusCode(StatusCodes.Status403Forbidden, new { message = "系统已安装，禁止重复执行安装操作" });
            }

            var hasUsers = await _unitOfWork.Users.GetQueryable().AnyAsync();
            var hasMailboxes = await _unitOfWork.Mailboxes.GetQueryable().AnyAsync();

            if (!hasUsers || !hasMailboxes)
            {
                return BadRequest(new { message = "请先完成管理员用户和邮箱配置" });
            }

            await _unitOfWork.SystemOptions.SetAsync("installed", "true");
            await _unitOfWork.SystemOptions.SetAsync("installation_date", DateTime.UtcNow.ToString("o"));

            var adminUser = await _unitOfWork.Users.GetQueryable().FirstOrDefaultAsync(u => u.Role == UserRoleType.SuperAdmin);
            var defaultMailbox = await _unitOfWork.Mailboxes.GetQueryable().FirstOrDefaultAsync();

            if (adminUser != null && defaultMailbox != null)
            {
                await _unitOfWork.Repository<MailboxUser>().AddAsync(new MailboxUser
                {
                    MailboxId = defaultMailbox.Id,
                    UserId = adminUser.Id
                });
            }

            await _unitOfWork.SaveChangesAsync();

            // 安装完成后作废安装令牌（删除令牌文件 + 内存失效），
            // 防止持有旧令牌或恢复旧备份的攻击者二次创建超管账号。
            _setupTokenService.Invalidate();

            return Ok(new { message = "安装完成", isInstalled = true });
        }
        finally
        {
            _setupLock.Release();
        }
    }

    /// <summary>
    /// 判断系统是否已完成安装。已安装系统禁止再次访问安装接口。
    /// </summary>
    private async Task<bool> IsSystemInstalledAsync()
    {
        var installedValue = await _unitOfWork.SystemOptions.GetValueAsync("installed");
        return installedValue == "true";
    }

    /// <summary>
    /// 校验冷启动安装令牌（X-Setup-Token 头）。令牌有效返回 null，无效返回拒绝响应。
    /// </summary>
    private IActionResult? CheckSetupToken()
    {
        var provided = Request.Headers[SetupTokenHeader].FirstOrDefault();
        if (!_setupTokenService.Validate(provided))
        {
            return StatusCode(StatusCodes.Status403Forbidden, new { message = "无效的安装令牌" });
        }

        return null;
    }

    private int GetCurrentStep(bool hasUsers, bool hasMailboxes)
    {
        if (!hasUsers) return 1;
        if (!hasMailboxes) return 2;
        return 3;
    }
}
