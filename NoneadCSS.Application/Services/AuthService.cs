using System.IdentityModel.Tokens.Jwt;
using System.Net;
using System.Security.Claims;
using System.Security.Cryptography;
using System.Text;
using NoneadCSS.Application.Configuration;
using NoneadCSS.Application.DTOs.Requests;
using NoneadCSS.Application.DTOs.Responses;
using NoneadCSS.Application.Mappings;
using NoneadCSS.Application.Security;
using NoneadCSS.Application.Templates;
using NoneadCSS.Domain;
using NoneadCSS.Domain.Entities;
using NoneadCSS.Domain.Enums;
using NoneadCSS.Domain.Events;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using Microsoft.EntityFrameworkCore;
using Microsoft.IdentityModel.Tokens;

namespace NoneadCSS.Application.Services;

public class AuthService : IAuthService
{
    private readonly IUnitOfWork _unitOfWork;
    private readonly IConfiguration _configuration;
    private readonly IEmailService _emailService;
    private readonly IEmailQueueService _emailQueueService;
    private readonly IMailTemplateService _mailTemplateService;
    private readonly ILogger<AuthService> _logger;
    private readonly ICaptchaService _captchaService;
    private readonly IPasswordPolicyService _passwordPolicyService;
    private readonly IUserRoleResolver _userRoleResolver;
    private readonly IFrontendUrlResolver _frontendUrlResolver;
    private readonly IHostEnvironment _environment;
    private readonly ITemplateRenderer _renderer;
    private readonly BrandOptions _brand;
    private readonly IDomainEventDispatcher? _domainEventDispatcher;

    public AuthService(IUnitOfWork unitOfWork, IConfiguration configuration, IEmailService emailService, IEmailQueueService emailQueueService, IMailTemplateService mailTemplateService, ILogger<AuthService> logger, ICaptchaService captchaService, IPasswordPolicyService passwordPolicyService, IUserRoleResolver userRoleResolver, IFrontendUrlResolver frontendUrlResolver, IHostEnvironment environment, ITemplateRenderer renderer, IOptions<BrandOptions> brand, IDomainEventDispatcher? domainEventDispatcher = null)
    {
        _unitOfWork = unitOfWork;
        _configuration = configuration;
        _emailService = emailService;
        _emailQueueService = emailQueueService;
        _mailTemplateService = mailTemplateService;
        _logger = logger;
        _captchaService = captchaService;
        _passwordPolicyService = passwordPolicyService;
        _userRoleResolver = userRoleResolver;
        _frontendUrlResolver = frontendUrlResolver;
        _environment = environment;
        _renderer = renderer;
        _brand = brand.Value;
        _domainEventDispatcher = domainEventDispatcher;
    }

    /// <summary>
    /// 发布强类型领域事件。未注入分发器（如部分单元测试构造）时直接跳过；
    /// 分发失败由 DomainEventDispatcher 内部捕获并记录日志，不影响登录主流程。
    /// </summary>
    /// <typeparam name="TEvent">事件类型。</typeparam>
    /// <param name="event">事件实例。</param>
    private Task DispatchDomainEventAsync<TEvent>(TEvent @event) where TEvent : class
        => _domainEventDispatcher is null ? Task.CompletedTask : _domainEventDispatcher.DispatchAsync(@event);

    /// <summary>
    /// 登录：校验验证码与凭据，维护失败计数与锁定，成功后发布 <see cref="UserLoggedInEvent"/>，
    /// 各失败分支发布 <see cref="LoginFailedEvent"/> / <see cref="LockoutEvent"/>（供审计与仪表盘「异常事件数」统计）。
    /// </summary>
    /// <param name="request">登录请求（可携带 Locale）。</param>
    /// <param name="ipAddress">客户端 IP。</param>
    /// <param name="userAgent">客户端 User-Agent，可为空。</param>
    /// <param name="locale">控制器端从请求文化解析出的界面语言，请求体未带 Locale 时用它回写 Users.Locale，可为空。</param>
    /// <param name="cancellationToken">取消令牌。</param>
    /// <returns>登录结果；失败时 Success=false 且返回统一的错误消息。</returns>
    public async Task<LoginResponse> LoginAsync(LoginRequest request, string ipAddress, string? userAgent = null, string? locale = null, CancellationToken cancellationToken = default)
    {
        // 验证验证码
        var captchaValid = await _captchaService.ValidateAsync(request.CaptchaId, request.CaptchaCode);
        if (!captchaValid)
        {
            return new LoginResponse { Success = false, ErrorMessage = "验证码错误或已过期" };
        }

        // TODO: 应通过仓储方法查询，避免在 Application Service 中直接拼接 EF 查询（C-3 架构问题）
        var user = await _unitOfWork.Users.GetQueryable(false)
            .FirstOrDefaultAsync(u => u.Email == request.Email && u.Status != UserStatus.Deleted && u.DeletedAt == null, cancellationToken);

        // 安全要求：所有登录失败分支必须返回相同的错误消息，防止攻击者通过差异枚举有效用户邮箱
        const string LoginFailedMessage = "邮箱或密码不正确";

        if (user == null)
        {
            _logger.LogWarning("登录失败：用户不存在，Email={Email}", request.Email);
            // 用户不存在时没有 UserId 可记，审计里只留尝试邮箱
            await DispatchDomainEventAsync(new LoginFailedEvent
            {
                UserId = null,
                Email = request.Email,
                Reason = "用户不存在",
                IpAddress = ipAddress,
                UserAgent = userAgent
            });
            return new LoginResponse { Success = false, ErrorMessage = LoginFailedMessage };
        }

        if (user.LockoutEnd.HasValue && user.LockoutEnd.Value > DateTime.UtcNow)
        {
            _logger.LogWarning("登录失败：账号已被锁定，Email={Email}，LockoutEnd={LockoutEnd}", request.Email, user.LockoutEnd.Value);
            // 安全要求：锁定状态也返回统一消息，防止攻击者通过差异判断账号存在性和锁定状态
            // 锁定期间的登录尝试同样发布 LockoutEvent，使审计里的 action="lockout" 计数完整
            await DispatchDomainEventAsync(new LockoutEvent
            {
                UserId = user.Id,
                Email = user.Email,
                IpAddress = ipAddress,
                UserAgent = userAgent
            });
            return new LoginResponse { Success = false, ErrorMessage = LoginFailedMessage };
        }

        if (user.IsPendingActivation())
        {
            _logger.LogWarning("登录失败：账号待激活，Email={Email}", request.Email);
            await DispatchDomainEventAsync(new LoginFailedEvent
            {
                UserId = user.Id,
                Email = user.Email,
                Reason = "账号待激活",
                IpAddress = ipAddress,
                UserAgent = userAgent
            });
            return new LoginResponse { Success = false, ErrorMessage = LoginFailedMessage };
        }

        if (!user.IsActive())
        {
            _logger.LogWarning("登录失败：账号已被禁用，Email={Email}", request.Email);
            await DispatchDomainEventAsync(new LoginFailedEvent
            {
                UserId = user.Id,
                Email = user.Email,
                Reason = "账号已被禁用",
                IpAddress = ipAddress,
                UserAgent = userAgent
            });
            return new LoginResponse { Success = false, ErrorMessage = LoginFailedMessage };
        }

        // BCrypt.Net-Next 在 user.Password 为空时会抛出 "Invalid salt"，而不是返回 false。
        // OAuth 用户或迁移数据可能未设置密码哈希，先做短路避免未处理异常冒泡。
        if (string.IsNullOrEmpty(user.Password) || string.IsNullOrEmpty(request.Password))
        {
            user.FailedLoginAttempts++;
            var lockoutThreshold = _configuration.GetValue<int?>("Security:LoginLockout:Threshold") ?? 5;
            var lockoutTriggered = false;
            if (user.FailedLoginAttempts >= lockoutThreshold)
            {
                var lockoutMinutes = _configuration.GetValue<int?>("Security:LoginLockout:DurationMinutes") ?? 15;
                user.LockoutEnd = DateTime.UtcNow.AddMinutes(lockoutMinutes);
                lockoutTriggered = true;
                _logger.LogWarning("登录失败次数过多，账号已被锁定，Email={Email}，FailedAttempts={FailedAttempts}", request.Email, user.FailedLoginAttempts);
            }
            else
            {
                _logger.LogWarning("登录失败：密码为空或存储哈希为空，Email={Email}", request.Email);
            }
            user.UpdatedAt = DateTime.UtcNow;
            await _unitOfWork.SaveChangesAsync(cancellationToken);

            // 失败计数与锁定已提交，此时发布事件（监听器用自己的 Scope 与 DbContext 写审计）
            await DispatchDomainEventAsync(new LoginFailedEvent
            {
                UserId = user.Id,
                Email = user.Email,
                Reason = "登录凭据缺失（密码或存储哈希为空）",
                IpAddress = ipAddress,
                UserAgent = userAgent
            });
            if (lockoutTriggered)
            {
                await DispatchDomainEventAsync(new LockoutEvent
                {
                    UserId = user.Id,
                    Email = user.Email,
                    IpAddress = ipAddress,
                    UserAgent = userAgent
                });
            }
            return new LoginResponse { Success = false, ErrorMessage = LoginFailedMessage };
        }

        var passwordIsValid = BCrypt.Net.BCrypt.Verify(request.Password, user.Password);

        if (!passwordIsValid)
        {
            user.FailedLoginAttempts++;
            // 登录锁定阈值与时长从配置读取（Security:LoginLockout），支持环境变量覆盖，便于按部署环境调整策略
            var lockoutThreshold = _configuration.GetValue<int?>("Security:LoginLockout:Threshold") ?? 5;
            var lockoutTriggered = false;
            if (user.FailedLoginAttempts >= lockoutThreshold)
            {
                var lockoutMinutes = _configuration.GetValue<int?>("Security:LoginLockout:DurationMinutes") ?? 15;
                user.LockoutEnd = DateTime.UtcNow.AddMinutes(lockoutMinutes);
                lockoutTriggered = true;
                _logger.LogWarning("登录失败次数过多，账号已被锁定，Email={Email}，FailedAttempts={FailedAttempts}", request.Email, user.FailedLoginAttempts);
            }
            else
            {
                _logger.LogWarning("登录失败：密码错误，Email={Email}，FailedAttempts={FailedAttempts}", request.Email, user.FailedLoginAttempts);
            }
            user.UpdatedAt = DateTime.UtcNow;
            await _unitOfWork.SaveChangesAsync(cancellationToken);

            // 失败计数与锁定已提交：发布登录失败事件，达到阈值时额外发布锁定事件。
            // AuditActionFilter 明确跳过 Auth 控制器的 login 动作（:165-170），所以这两个事件是
            // 审计里 action="loginfailed" / "lockout" 的唯一来源，仪表盘「异常事件数」依赖它们。
            await DispatchDomainEventAsync(new LoginFailedEvent
            {
                UserId = user.Id,
                Email = user.Email,
                Reason = "密码错误",
                IpAddress = ipAddress,
                UserAgent = userAgent
            });
            if (lockoutTriggered)
            {
                await DispatchDomainEventAsync(new LockoutEvent
                {
                    UserId = user.Id,
                    Email = user.Email,
                    IpAddress = ipAddress,
                    UserAgent = userAgent
                });
            }
            return new LoginResponse { Success = false, ErrorMessage = LoginFailedMessage };
        }

        user.FailedLoginAttempts = 0;
        user.LockoutEnd = null;
        user.UpdatedAt = DateTime.UtcNow;
        await _unitOfWork.SaveChangesAsync(cancellationToken);

        // 登录成功：发布 UserLoggedInEvent，由 RememberUserLocaleListener 把本次登录语言回写 Users.Locale
        //（此前没有任何等价实现）。Locale 优先取请求体，其次取控制器端解析出的请求文化；
        // 两者都为空时监听器跳过回写。必须在保存之后发布：监听器用自己的 Scope 与 DbContext。
        await DispatchDomainEventAsync(new UserLoggedInEvent
        {
            UserId = user.Id,
            Locale = string.IsNullOrWhiteSpace(request.Locale) ? locale : request.Locale,
            IpAddress = ipAddress,
            UserAgent = userAgent
        });

        var token = await GenerateJwtTokenAsync(user, cancellationToken);
        var refreshToken = await GenerateRefreshTokenAsync(user, ipAddress, cancellationToken);

        _logger.LogInformation("用户登录成功，Email={Email}", request.Email);

        var stringPermissions = await _userRoleResolver.GetPermissionsAsync(user.Id);

        return new LoginResponse
        {
            Success = true,
            Token = token,
            RefreshToken = refreshToken,
            User = user.ToResponse(stringPermissions)
        };
    }

    /// <summary>
    /// 验证 JWT Token 是否有效
    /// </summary>
    /// <param name="token">JWT Token 字符串</param>
    /// <returns>Token 有效返回 true，无效或验证失败返回 false</returns>
    public Task<bool> ValidateTokenAsync(string token)
    {
        // 配置校验置于 try 之外：配置错误属于服务器端故障，
        // 必须向上传播（500），不能被吞没为 "token 无效"（false），
        // 否则配置缺失/错误时所有令牌均被判为无效且无法定位根因。
        var configuredKey = _configuration["Jwt:SecretKey"]
            ?? throw new InvalidOperationException("配置项 Jwt:SecretKey 缺失，请在 appsettings.json 或环境变量中配置。");
        if (Encoding.UTF8.GetByteCount(configuredKey) < 32)
        {
            throw new InvalidOperationException("Jwt:SecretKey 长度必须至少为 32 字节。");
        }
        var key = Encoding.UTF8.GetBytes(configuredKey);

        try
        {
            var tokenHandler = new JwtSecurityTokenHandler();
            tokenHandler.ValidateToken(token, new TokenValidationParameters
            {
                ValidateIssuerSigningKey = true,
                IssuerSigningKey = new SymmetricSecurityKey(key),
                ValidateIssuer = true,
                // 与 GenerateAccessTokenAsync 保持一致的默认值，避免配置缺失时令牌验证失败
                ValidIssuer = _configuration["Jwt:Issuer"] ?? "NoneadCSS",
                ValidateAudience = true,
                ValidAudience = _configuration["Jwt:Audience"] ?? "NoneadCSSApi",
                ValidateLifetime = true,
                ClockSkew = TimeSpan.Zero
            }, out _);

            return Task.FromResult(true);
        }
        catch (SecurityTokenException ex)
        {
            // SecurityTokenException 表示令牌本身无效/过期/签名不匹配，
            // 属于预期内的验证失败，应返回 false 而非抛出。
            _logger.LogWarning("Token 验证失败: {ExceptionType}", ex.GetType().Name);
            return Task.FromResult(false);
        }
        catch (ArgumentException ex)
        {
            // ArgumentException 通常表示 token 字符串格式错误（如 null/空/非 JWT 格式），
            // 同样属于预期内的验证失败。
            _logger.LogWarning("Token 格式无效: {ExceptionType}", ex.GetType().Name);
            return Task.FromResult(false);
        }
        // 其他异常（如配置错误、密钥格式异常等）不再吞没，向上传播由全局异常处理器处理。
    }

    private async Task<string> GenerateJwtTokenAsync(User user, CancellationToken cancellationToken)
    {
        var tokenHandler = new JwtSecurityTokenHandler();
        var configuredKey = _configuration["Jwt:SecretKey"]
            ?? throw new InvalidOperationException("配置项 Jwt:SecretKey 缺失，请在 appsettings.json 或环境变量中配置。");
        var key = Encoding.UTF8.GetBytes(configuredKey);

        // 读取全局令牌版本号，用于支持"强制所有用户登出"功能
        // 当管理员执行强制登出时，版本号会递增，使所有旧令牌失效
        var globalVersion = await _unitOfWork.SystemOptions.GetValueAsync("security.token_global_version") ?? "0";

        var claims = new[]
        {
            new Claim(ClaimTypes.NameIdentifier, user.Id.ToString()),
            new Claim(ClaimTypes.Email, user.Email),
            new Claim(ClaimTypes.Role, ((int)user.Role).ToString()),
            new Claim(JwtRegisteredClaimNames.Name, user.GetFullName()),
            new Claim("gv", globalVersion)
        };

        var tokenDescriptor = new SecurityTokenDescriptor
        {
            Subject = new ClaimsIdentity(claims),
            Expires = DateTime.UtcNow.AddMinutes(_configuration.GetValue<double?>("Jwt:AccessTokenExpireMinutes") ?? 60),
            Issuer = _configuration["Jwt:Issuer"] ?? "NoneadCSS",
            Audience = _configuration["Jwt:Audience"] ?? "NoneadCSSApi",
            SigningCredentials = new SigningCredentials(
                new SymmetricSecurityKey(key),
                SecurityAlgorithms.HmacSha256Signature)
        };

        var token = tokenHandler.CreateToken(tokenDescriptor);
        return tokenHandler.WriteToken(token);
    }

    private async Task<string> GenerateRefreshTokenAsync(User user, string ipAddress, CancellationToken cancellationToken)
    {
        // 仅生成明文 token，持久化交由调用方在事务中完成
        var token = Convert.ToHexString(RandomNumberGenerator.GetBytes(32));
        var expireDays = _configuration.GetValue<double?>("Jwt:RefreshTokenExpireDays") ?? 7;

        var refreshToken = new RefreshToken
        {
            UserId = user.Id,
            Token = SecretHasher.Hash(token),
            ExpiresAt = DateTime.UtcNow.AddDays(expireDays),
            CreatedByIp = ipAddress
        };

        await _unitOfWork.RefreshTokens.AddAsync(refreshToken);
        await _unitOfWork.SaveChangesAsync(cancellationToken);

        return token;
    }

    /// <summary>
    /// 仅生成 RefreshToken 明文与实体（不持久化），用于在调用方事务中保存
    /// </summary>
    private string GenerateRefreshTokenPlain(User user, string ipAddress, out RefreshToken entity)
    {
        var token = Convert.ToHexString(RandomNumberGenerator.GetBytes(32));
        var expireDays = _configuration.GetValue<double?>("Jwt:RefreshTokenExpireDays") ?? 7;

        entity = new RefreshToken
        {
            UserId = user.Id,
            Token = SecretHasher.Hash(token),
            ExpiresAt = DateTime.UtcNow.AddDays(expireDays),
            CreatedByIp = ipAddress
        };

        return token;
    }

    public async Task<LoginResponse> RegisterAsync(RegisterRequest request, CancellationToken cancellationToken = default)
    {
        var allowRegistration = await _unitOfWork.SystemOptions.GetValueAsync("security.allow_registration");
        if (!bool.TryParse(allowRegistration, out var isAllowed) || !isAllowed)
        {
            return new LoginResponse { Success = false, ErrorMessage = "用户注册功能未开放" };
        }

        var existingUser = await _unitOfWork.Users.GetByEmailAsync(request.Email);
        if (existingUser != null)
        {
            _logger.LogWarning("注册失败：邮箱已被注册，Email={Email}", request.Email);
            return new LoginResponse { Success = false, ErrorMessage = "邮箱已被注册" };
        }

        var pwdValidation = await _passwordPolicyService.ValidateAsync(request.Password);
        if (!pwdValidation.IsValid)
        {
            _logger.LogWarning("注册失败：密码不符合策略，Email={Email}", request.Email);
            return new LoginResponse { Success = false, ErrorMessage = string.Join("；", pwdValidation.Errors) };
        }

        User user;
        using (var transaction = await _unitOfWork.BeginTransactionAsync())
        {
            try
            {
                user = new User
                {
                    Email = request.Email,
                    Password = BCrypt.Net.BCrypt.HashPassword(request.Password),
                    FirstName = request.FirstName,
                    LastName = request.LastName,
                    Role = UserRoleType.ReadOnly,
                    Status = UserStatus.PendingActivation,
                    Locale = "zh-Hans",
                    Timezone = "Asia/Shanghai",
                    CreatedAt = DateTime.UtcNow,
                    UpdatedAt = DateTime.UtcNow
                };

                await _unitOfWork.Users.AddAsync(user);
                await _unitOfWork.SaveChangesAsync(cancellationToken);

                await transaction.CommitAsync();
            }
            catch (DbUpdateException ex) when (IsUniqueConstraintViolation(ex))
            {
                // M11: 并发注册竞态——check-then-act 检查通过后，另一请求已抢先插入相同邮箱，
                // 数据库唯一索引兜底抛出冲突。回滚后返回业务提示而非裸 409。
                await transaction.SafeRollbackAsync();
                _logger.LogWarning("注册失败（并发冲突）：邮箱已被注册，Email={Email}", request.Email);
                return new LoginResponse { Success = false, ErrorMessage = "邮箱已被注册" };
            }
            catch
            {
                await transaction.SafeRollbackAsync();
                throw;
            }
        }

        _logger.LogInformation("用户注册成功，Email={Email}", request.Email);

        await SendWelcomeEmailAsync(user, cancellationToken);

        var stringPermissions = await _userRoleResolver.GetPermissionsAsync(user.Id);

        return new LoginResponse
        {
            Success = true,
            Token = string.Empty,
            User = user.ToResponse(stringPermissions)
        };
    }

    private async Task SendWelcomeEmailAsync(User user, CancellationToken cancellationToken)
    {
        try
        {
            // 获取第一个活跃邮箱作为发件邮箱
            var senderMailbox = await _unitOfWork.Mailboxes.GetFirstActiveAsync();
            if (senderMailbox == null)
            {
                _logger.LogWarning("没有可用的活跃邮箱来发送欢迎邮件");
                return;
            }

            var templates = await _unitOfWork.MailTemplates.GetByTypeAsync(MailTemplateType.Welcome);
            var template = templates.FirstOrDefault(t => t.IsActive);
            if (template == null)
            {
                return;
            }

            var baseUrl = _frontendUrlResolver.GetBaseUrl();

            var variables = new Dictionary<string, string>
            {
                ["company_name"] = _brand.CompanyName,
                ["user_name"] = user.GetFullName(),
                ["user_email"] = user.Email,
                ["login_url"] = $"{baseUrl}/login"
            };

            var subject = _renderer.Render(template.Subject ?? string.Empty, variables, new RenderOptions { HtmlEncode = false });
            var bodyHtml = _renderer.Render(template.BodyHtml ?? string.Empty, variables);

            await _emailQueueService.EnqueueEmailAsync(
                senderMailbox.Id,
                user.Email,
                subject,
                bodyHtml);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "发送欢迎邮件失败：{Email}", user.Email);
        }
    }

    /// <summary>
    /// 判断 DbUpdateException 是否由唯一约束冲突引起（SQLite 和 SQL Server 兼容）。
    /// </summary>
    private static bool IsUniqueConstraintViolation(DbUpdateException ex)
    {
        var msg = (ex.InnerException?.Message ?? ex.Message)?.ToLowerInvariant();
        if (string.IsNullOrEmpty(msg)) return false;
        // SQLite: "UNIQUE constraint failed"
        // SQL Server: "Cannot insert duplicate key row" / "Violation of UNIQUE KEY constraint"
        return msg.Contains("unique constraint")
               || msg.Contains("duplicate key")
               || msg.Contains("violation of unique");
    }

    public async Task<(bool Success, string ErrorMessage)> ForgotPasswordAsync(ForgotPasswordRequest request, CancellationToken cancellationToken = default)
    {
        // 先通过邮箱查找用户
        var user = await _unitOfWork.Users.GetByEmailAsync(request.Email);
        if (user == null)
        {
            _logger.LogWarning("密码重置失败：用户不存在，Email={Email}", request.Email);
            return (false, "姓名或邮箱不正确");
        }

        // 验证名、姓和邮箱是否匹配
        if (!string.Equals(user.FirstName, request.FirstName, StringComparison.OrdinalIgnoreCase) ||
            !string.Equals(user.LastName, request.LastName, StringComparison.OrdinalIgnoreCase))
        {
            _logger.LogWarning("密码重置失败：姓名不匹配，Email={Email}", request.Email);
            return (false, "姓名或邮箱不正确");
        }

        var token = Convert.ToHexString(RandomNumberGenerator.GetBytes(32));
        // 密码重置令牌有效期从配置读取（Security:PasswordReset:TokenExpireHours），支持环境变量覆盖
        var tokenExpireHours = _configuration.GetValue<int?>("Security:PasswordReset:TokenExpireHours") ?? 24;
        var resetToken = new PasswordResetToken
        {
            UserId = user.Id,
            Token = SecretHasher.Hash(token),
            ExpiresAt = DateTime.UtcNow.AddHours(tokenExpireHours),
            CreatedAt = DateTime.UtcNow
        };

        await _unitOfWork.PasswordResetTokens.AddAsync(resetToken);
        await _unitOfWork.SaveChangesAsync(cancellationToken);

        _logger.LogInformation("密码重置令牌已生成，Email={Email}", request.Email);

        // 获取第一个活跃邮箱用于发送系统通知
        var senderMailbox = await _unitOfWork.Mailboxes.GetFirstActiveAsync();
        if (senderMailbox == null)
        {
            _logger.LogError("密码重置失败：没有可用的活跃邮箱，Email={Email}", request.Email);
            return (false, "没有可用的活跃邮箱来发送密码重置邮件");
        }

        // 邮件链接中携带明文 token，服务端收到后哈希再比对
        var resetUrl = $"{_frontendUrlResolver.GetBaseUrl()}/reset-password?token={token}";

        // 尝试使用邮件模板系统的密码重置模板
        var templates = await _unitOfWork.MailTemplates.GetByTypeAsync(MailTemplateType.PasswordReset);
        var template = templates.FirstOrDefault(t => t.IsActive);

        string emailSubject;
        string emailBody;

        if (template != null)
        {
            var variables = new Dictionary<string, string>
            {
                ["user_name"] = user.GetFullName() ?? user.Email,
                ["user_email"] = user.Email,
                ["reset_password_url"] = resetUrl,
                ["company_name"] = _brand.CompanyName,
                ["login_url"] = $"{_frontendUrlResolver.GetBaseUrl()}/login"
            };

            var preview = await _mailTemplateService.PreviewAsync(new DTOs.Requests.PreviewMailTemplateRequest
            {
                Subject = template.Subject,
                BodyHtml = template.BodyHtml,
                BodyText = template.BodyText,
                Variables = variables
            });

            emailSubject = preview.Subject;
            emailBody = preview.BodyHtml;
        }
        else
        {
            // Production 环境必须有模板，Development 才允许 fallback
            if (!_environment.IsDevelopment())
            {
                throw new InvalidOperationException("密码重置邮件模板未配置，请在管理后台配置密码重置邮件模板。");
            }
            // 回退到默认硬编码内容（仅 Development 环境）
            emailSubject = "nCSS 密码重置请求";
            // 用户名经 HtmlEncode 防止邮件客户端 XSS（与 SendWelcomeEmailAsync 的处理保持一致）
            var encodedUserName = WebUtility.HtmlEncode(user.GetFullName() ?? user.Email);
            emailBody = $@"
<h2>密码重置请求</h2>
<p>您好 {encodedUserName}，</p>
<p>您收到了此邮件，因为有人请求重置您的 nCSS 账户密码。</p>
<p>请点击以下链接重置密码（1小时内有效）：</p>
<p><a href=""{resetUrl}"" style=""padding: 10px 20px; background-color: #3b82f6; color: white; text-decoration: none; border-radius: 4px;"">重置密码</a></p>
<p>或复制链接到浏览器：{resetUrl}</p>
<p>如果您没有请求重置密码，请忽略此邮件。</p>
";
        }

        await _emailService.SendSystemNotificationAsync(
            senderMailbox,
            user.Email,
            emailSubject,
            emailBody,
            cancellationToken
        );

        _logger.LogInformation("密码重置邮件已发送，Email={Email}", request.Email);

        return (true, string.Empty);
    }

    public async Task<(bool Success, string ErrorMessage)> ResetPasswordAsync(ResetPasswordRequest request, CancellationToken cancellationToken = default)
    {
        if (request.NewPassword != request.ConfirmPassword)
        {
            return (false, "两次输入的密码不一致");
        }

        var pwdValidation = await _passwordPolicyService.ValidateAsync(request.NewPassword);
        if (!pwdValidation.IsValid)
        {
            return (false, string.Join("；", pwdValidation.Errors));
        }

        var tokenHash = SecretHasher.Hash(request.Token);
        var resetToken = await _unitOfWork.PasswordResetTokens.GetByTokenAsync(tokenHash);

        if (resetToken == null || !resetToken.IsValid)
        {
            return (false, "令牌无效或已过期");
        }

        // 显式事务：密码更新 + 失效所有重置令牌 + 撤销所有 Refresh Token 必须原子提交
        using (var transaction = await _unitOfWork.BeginTransactionAsync())
        {
            try
            {
                // 用追踪查询获取用户实体
                var user = await _unitOfWork.Users.GetQueryable(false)
                    .FirstOrDefaultAsync(u => u.Id == resetToken.UserId && u.DeletedAt == null, cancellationToken);
                if (user == null)
                {
                    return (false, "用户不存在");
                }

                user.Password = BCrypt.Net.BCrypt.HashPassword(request.NewPassword);
                user.UpdatedAt = DateTime.UtcNow;

                // 标记当前令牌已使用
                resetToken.UsedAt = DateTime.UtcNow;

                // 失效该用户所有未使用的密码重置令牌
                await _unitOfWork.PasswordResetTokens.InvalidateUserTokensAsync(resetToken.UserId);

                // 撤销该用户所有 Refresh Token（密码变更后所有会话失效）
                await _unitOfWork.RefreshTokens.RevokeUserTokensAsync(resetToken.UserId, "Password reset");

                await _unitOfWork.SaveChangesAsync(cancellationToken);
                await transaction.CommitAsync();
            }
            catch
            {
                await transaction.SafeRollbackAsync();
                throw;
            }
        }

        _logger.LogInformation("用户密码重置成功，UserId={UserId}", resetToken.UserId);
        return (true, string.Empty);
    }

    public async Task<bool> GetAllowRegistrationAsync(CancellationToken cancellationToken = default)
    {
        var value = await _unitOfWork.SystemOptions.GetValueAsync("security.allow_registration");
        return bool.TryParse(value, out var result) && result;
    }

    public async Task<(bool Success, string? AccessToken, string? RefreshToken, string? ErrorMessage)> RefreshTokenAsync(string refreshToken, string ipAddress, CancellationToken cancellationToken = default)
    {
        if (string.IsNullOrWhiteSpace(refreshToken))
        {
            return (false, null, null, "Refresh Token 无效");
        }

        var token = await _unitOfWork.RefreshTokens.GetByTokenAsync(SecretHasher.Hash(refreshToken));
        if (token == null)
        {
            _logger.LogWarning("Refresh Token 无效");
            return (false, null, null, "Refresh Token 无效或已过期");
        }

        if (token.IsRevoked)
        {
            if (!string.IsNullOrEmpty(token.ReplacedByToken))
            {
                _logger.LogError("检测到 Refresh Token 重用攻击，已撤销用户所有令牌，UserId={UserId}", token.UserId);
                await _unitOfWork.RefreshTokens.RevokeUserTokensAsync(token.UserId, "Token reuse detected");
                await _unitOfWork.SaveChangesAsync(cancellationToken);
            }
            return (false, null, null, "Refresh Token 无效或已过期");
        }

        if (token.IsExpired)
        {
            _logger.LogWarning("Refresh Token 已过期");
            return (false, null, null, "Refresh Token 无效或已过期");
        }

        if (token.User == null || token.User.Status != UserStatus.Active)
        {
            _logger.LogWarning("Refresh Token 对应用户不存在或已被禁用，UserId={UserId}", token.UserId);
            return (false, null, null, "用户不存在或已被禁用");
        }

        var newAccessToken = await GenerateJwtTokenAsync(token.User, cancellationToken);

        // 用事务确保新 token 生成和旧 token 撤销的原子性
        string newRefreshToken;
        using (var transaction = await _unitOfWork.BeginTransactionAsync())
        {
            try
            {
                // 1. 生成新 RefreshToken 实体（不立即持久化）
                newRefreshToken = GenerateRefreshTokenPlain(token.User, ipAddress, out var newRefreshTokenEntity);
                await _unitOfWork.RefreshTokens.AddAsync(newRefreshTokenEntity);

                // 2. 撤销旧 token
                token.RevokedAt = DateTime.UtcNow;
                token.ReplacedByToken = SecretHasher.Hash(newRefreshToken);
                token.ReasonRevoked = "Token rotated";

                await _unitOfWork.SaveChangesAsync(cancellationToken);
                await transaction.CommitAsync();
            }
            catch
            {
                await transaction.SafeRollbackAsync();
                throw;
            }
        }

        _logger.LogInformation("用户刷新 Access Token 成功，UserId={UserId}", token.UserId);
        return (true, newAccessToken, newRefreshToken, null);
    }

    public async Task<bool> RevokeRefreshTokenAsync(string refreshToken, CancellationToken cancellationToken = default)
    {
        if (string.IsNullOrWhiteSpace(refreshToken))
        {
            return false;
        }

        var token = await _unitOfWork.RefreshTokens.GetByTokenAsync(SecretHasher.Hash(refreshToken));
        if (token == null || !token.IsActive)
        {
            return false;
        }

        token.RevokedAt = DateTime.UtcNow;
        token.ReasonRevoked = "User logged out";

        await _unitOfWork.SaveChangesAsync(cancellationToken);

        return true;
    }
}
