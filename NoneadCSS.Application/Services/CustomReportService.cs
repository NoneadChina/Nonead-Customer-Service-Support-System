using System.Text.Json;
using System.Text.RegularExpressions;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using NoneadCSS.Application.Configuration;
using NoneadCSS.Application.DTOs.Requests;
using NoneadCSS.Application.DTOs.Responses;
using NoneadCSS.Domain;
using NoneadCSS.Domain.Entities;

namespace NoneadCSS.Application.Services;

public class CustomReportService : ICustomReportService
{
    private readonly IUnitOfWork _unitOfWork;
    private readonly ICurrentUserAccessor _currentUser;
    private readonly IReportService _reportService;
    private readonly IEmailService _emailService;
    private readonly CustomReportExportHelper _exportHelper;
    private readonly ILogger<CustomReportService> _logger;
    private readonly string _attachmentBasePath;

    private static readonly HashSet<string> ValidVisibilities = new(StringComparer.OrdinalIgnoreCase)
    {
        "private", "team", "public"
    };

    private static readonly HashSet<string> ValidRecurrences = new(StringComparer.OrdinalIgnoreCase)
    {
        "daily", "weekly", "monthly"
    };

    private static readonly HashSet<string> ValidCategories = new(StringComparer.OrdinalIgnoreCase)
    {
        "performance", "efficiency", "customer", "workload"
    };

    private static readonly HashSet<string> ValidAttachmentFormats = new(StringComparer.OrdinalIgnoreCase)
    {
        "csv", "xlsx", "pdf"
    };

    private static readonly Regex EmailRegex =
        new(@"^[^@\s]+@[^@\s]+\.[^@\s]+$", RegexOptions.Compiled | RegexOptions.IgnoreCase);

    public CustomReportService(
        IUnitOfWork unitOfWork,
        ICurrentUserAccessor currentUser,
        IReportService reportService,
        IEmailService emailService,
        CustomReportExportHelper exportHelper,
        IOptions<AttachmentSettings> attachmentSettings,
        ILogger<CustomReportService> logger)
    {
        _unitOfWork = unitOfWork;
        _currentUser = currentUser;
        _reportService = reportService;
        _emailService = emailService;
        _exportHelper = exportHelper;
        _logger = logger;
        _attachmentBasePath = Path.Combine(Directory.GetCurrentDirectory(), attachmentSettings.Value.UploadPath);
    }

    public async Task<(List<CustomReportDto> Items, int TotalCount)> GetPagedAsync(
        string? search, string? category, string? visibility, int page, int pageSize)
    {
        var userTeamIds = await GetCurrentUserTeamIdsAsync();
        var (items, total) = await _unitOfWork.CustomReports.GetPagedAsync(
            _currentUser.UserId,
            _currentUser.IsSuperAdmin,
            userTeamIds,
            search,
            category,
            visibility,
            page,
            pageSize);

        var dtos = await MapToDtosAsync(items);
        return (dtos, total);
    }

    public async Task<List<CustomReportDto>> GetPinnedAsync(int limit)
    {
        var userTeamIds = await GetCurrentUserTeamIdsAsync();
        var items = await _unitOfWork.CustomReports.GetDashboardPinnedAsync(
            _currentUser.UserId,
            _currentUser.IsSuperAdmin,
            userTeamIds,
            limit);

        return await MapToDtosAsync(items);
    }

    public async Task<CustomReportDto?> GetByIdAsync(int id)
    {
        var report = await _unitOfWork.CustomReports.GetByIdAsync(id);
        if (report == null || report.IsDeleted())
            return null;

        if (!await CanViewAsync(report))
            throw new InvalidOperationException("报表不存在");

        return await MapToDtoAsync(report);
    }

    public async Task<CustomReportDto> CreateAsync(CreateCustomReportRequest req)
    {
        ValidateCreateUpdateRequest(req.Name, req.Description, req.Visibility, req.TeamId, req.ReportConfig);

        var currentUserId = _currentUser.UserId;
        var exists = await _unitOfWork.CustomReports.ExistsByNameAndOwnerAsync(req.Name, currentUserId);
        if (exists)
            throw new InvalidOperationException($"已存在同名报表 \"{req.Name}\"");

        var teamId = req.Visibility.Equals("team", StringComparison.OrdinalIgnoreCase)
            ? req.TeamId
            : null;

        var now = DateTime.UtcNow;
        var report = new CustomReport
        {
            Name = req.Name,
            Description = req.Description,
            Category = req.Category,
            Visibility = req.Visibility.ToLowerInvariant(),
            OwnerId = currentUserId,
            TeamId = teamId,
            ReportConfig = req.ReportConfig,
            IsDashboardPinned = false,
            DashboardSortOrder = 0,
            CreatedAt = now,
            UpdatedAt = now
        };

        await _unitOfWork.CustomReports.AddAsync(report);
        await _unitOfWork.SaveChangesAsync();

        return await MapToDtoAsync(report);
    }

    public async Task<CustomReportDto> UpdateAsync(int id, UpdateCustomReportRequest req)
    {
        var currentUserId = _currentUser.UserId;
        var report = await _unitOfWork.CustomReports.GetByIdTrackedAsync(id);
        if (report == null || report.IsDeleted())
            throw new InvalidOperationException("报表不存在");

        if (!await CanEditAsync(report))
            throw new InvalidOperationException("无权修改此报表");

        ValidateCreateUpdateRequest(req.Name, req.Description, req.Visibility, req.TeamId, req.ReportConfig);

        var exists = await _unitOfWork.CustomReports.ExistsByNameAndOwnerAsync(req.Name, currentUserId, id);
        if (exists)
            throw new InvalidOperationException($"已存在同名报表 \"{req.Name}\"");

        report.Name = req.Name;
        report.Description = req.Description;
        report.Category = req.Category;
        report.Visibility = req.Visibility.ToLowerInvariant();
        report.TeamId = req.Visibility.Equals("team", StringComparison.OrdinalIgnoreCase)
            ? req.TeamId
            : null;
        report.ReportConfig = req.ReportConfig;
        report.UpdatedAt = DateTime.UtcNow;

        await _unitOfWork.SaveChangesAsync();

        return await MapToDtoAsync(report);
    }

    public async Task DeleteAsync(int id)
    {
        var report = await _unitOfWork.CustomReports.GetByIdTrackedAsync(id);
        if (report == null || report.IsDeleted())
            return;

        if (!await CanEditAsync(report))
            throw new InvalidOperationException("无权修改此报表");

        report.SoftDelete();
        await _unitOfWork.SaveChangesAsync();
    }

    public async Task<(List<CustomReportDto> Items, int TotalCount)> GetTrashPagedAsync(
        string? search, int page, int pageSize)
    {
        var userTeamIds = await GetCurrentUserTeamIdsAsync();
        var (items, total) = await _unitOfWork.CustomReports.GetTrashPagedAsync(
            _currentUser.UserId,
            _currentUser.IsSuperAdmin,
            userTeamIds,
            search,
            page,
            pageSize);

        var dtos = await MapToDtosAsync(items);
        // 为垃圾箱条目补充 DeletedAt 信息（DTO 当前不含该字段，这里不影响展示逻辑）
        return (dtos, total);
    }

    public async Task<CustomReportDto?> RestoreAsync(int id)
    {
        var report = await _unitOfWork.CustomReports.GetByIdIgnoreQueryFilterAsync(id);
        if (report == null || !report.IsDeleted())
            return null;

        // 权限：报表创建者或 SuperAdmin 可恢复
        if (!_currentUser.IsSuperAdmin && report.OwnerId != _currentUser.UserId)
            throw new InvalidOperationException("无权恢复此报表");

        report.Restore();
        await _unitOfWork.SaveChangesAsync();

        return await MapToDtoAsync(report);
    }

    public async Task<bool> HardDeleteAsync(int id)
    {
        var report = await _unitOfWork.CustomReports.GetByIdIgnoreQueryFilterAsync(id);
        if (report == null) return false;

        // 权限：SuperAdmin 可永久删除任意报表；普通用户仅能永久删除自己的报表
        if (!_currentUser.IsSuperAdmin && report.OwnerId != _currentUser.UserId)
            throw new InvalidOperationException("无权永久删除此报表");

        // 所有角色统一要求报表必须在垃圾箱中才能硬删除，避免永久删除正在使用的报表
        if (!report.IsDeleted())
            throw new InvalidOperationException("报表未在垃圾箱中，请先软删除后再永久删除");

        // 先一并清理关联的调度（软删除），再硬删除报表本身（已软删除的 ISoftDelete 会被物理删除）
        var schedules = await _unitOfWork.CustomReportSchedules.GetByReportIdAsync(id);
        foreach (var s in schedules)
        {
            s.SoftDelete();
        }

        await _unitOfWork.CustomReports.HardDeleteAsync(report);
        await _unitOfWork.SaveChangesAsync();
        return true;
    }

    public async Task<int> ClearTrashAsync()
    {
        var userTeamIds = await GetCurrentUserTeamIdsAsync();
        // ClearTrashAsync 使用 ExecuteDeleteAsync，无需再调用 SaveChanges
        var count = await _unitOfWork.CustomReports.ClearTrashAsync(
            _currentUser.UserId,
            _currentUser.IsSuperAdmin,
            userTeamIds);
        return count;
    }

    public async Task<CustomReportDto> ToggleDashboardPinAsync(int id, bool pinned, int? sortOrder)
    {
        var report = await _unitOfWork.CustomReports.GetByIdTrackedAsync(id);
        if (report == null || report.IsDeleted())
            throw new InvalidOperationException("报表不存在");

        if (!await CanEditAsync(report))
            throw new InvalidOperationException("无权修改此报表");

        report.IsDashboardPinned = pinned;
        if (sortOrder.HasValue)
            report.DashboardSortOrder = sortOrder.Value;
        report.UpdatedAt = DateTime.UtcNow;

        await _unitOfWork.SaveChangesAsync();

        return await MapToDtoAsync(report);
    }

    public async Task<CustomReportExecutionResultDto> ExecuteAsync(int id)
    {
        var report = await _unitOfWork.CustomReports.GetByIdAsync(id);
        if (report == null || report.IsDeleted())
            throw new InvalidOperationException("报表不存在");

        if (!await CanViewAsync(report))
            throw new InvalidOperationException("报表不存在");

        return await ExecuteInternalAsync(report);
    }

    /// <summary>
    /// 基于传入配置直接执行报表预览，不持久化到数据库。
    /// 用于编辑器中的"刷新预览"按钮，确保预览与保存解耦。
    /// </summary>
    public async Task<CustomReportExecutionResultDto> ExecutePreviewAsync(CreateCustomReportRequest req)
    {
        if (string.IsNullOrWhiteSpace(req.ReportConfig))
            throw new InvalidOperationException("报表配置不能为空");

        // 构造临时实体供 ExecuteInternalAsync 使用，不写入数据库
        var tempReport = new CustomReport
        {
            Name = req.Name ?? "预览",
            Description = req.Description,
            Category = req.Category,
            Visibility = req.Visibility,
            TeamId = req.TeamId,
            ReportConfig = req.ReportConfig,
            OwnerId = _currentUser.UserId,
            CreatedAt = DateTime.UtcNow,
            UpdatedAt = DateTime.UtcNow,
        };

        return await ExecuteInternalAsync(tempReport);
    }

    public async Task<(byte[] Content, string ContentType, string FileName)> ExportAsync(int id, string format)
    {
        var report = await _unitOfWork.CustomReports.GetByIdAsync(id);
        if (report == null || report.IsDeleted())
            throw new InvalidOperationException("报表不存在");

        if (!await CanViewAsync(report))
            throw new InvalidOperationException("报表不存在");

        var result = await ExecuteInternalAsync(report);
        var safeName = CustomReportExportHelper.SanitizeFileName(report.Name);

        return format.ToLowerInvariant() switch
        {
            "csv" => (_exportHelper.ExportToCsv(result), "text/csv; charset=utf-8", $"{safeName}.csv"),
            "xlsx" or "excel" => (_exportHelper.ExportToExcel(result),
                "application/vnd.openxmlformats-officedocument.spreadsheetml.sheet", $"{safeName}.xlsx"),
            "pdf" => (_exportHelper.ExportToPdf(result), "application/pdf", $"{safeName}.pdf"),
            _ => throw new ArgumentException($"不支持的导出格式: {format}")
        };
    }

    public async Task<List<CustomReportScheduleDto>> ListSchedulesAsync(int reportId)
    {
        var report = await _unitOfWork.CustomReports.GetByIdAsync(reportId);
        if (report == null || report.IsDeleted())
            throw new InvalidOperationException("报表不存在");

        if (!await CanViewAsync(report))
            throw new InvalidOperationException("报表不存在");

        var schedules = await _unitOfWork.CustomReportSchedules.GetByReportIdAsync(reportId);
        schedules = schedules.Where(s => !s.IsDeleted()).ToList();
        return schedules.Select(MapToScheduleDto).ToList();
    }

    public async Task<CustomReportScheduleDto> CreateScheduleAsync(int reportId, CreateScheduleRequest req)
    {
        var report = await _unitOfWork.CustomReports.GetByIdAsync(reportId);
        if (report == null || report.IsDeleted())
            throw new InvalidOperationException("报表不存在");

        if (!await CanEditAsync(report))
            throw new InvalidOperationException("无权修改此报表");

        ValidateScheduleRequest(req);

        var now = DateTime.UtcNow;
        var schedule = new CustomReportSchedule
        {
            CustomReportId = reportId,
            Recurrence = req.Recurrence.ToLowerInvariant(),
            WeekDay = req.WeekDay,
            SendTime = req.SendTime,
            Recipients = req.Recipients,
            Subject = req.Subject,
            AttachmentFormat = req.AttachmentFormat.ToLowerInvariant(),
            IsActive = req.IsActive,
            OwnerId = _currentUser.UserId,
            CreatedAt = now,
            UpdatedAt = now
        };

        schedule.NextRunAt = ComputeNextRun(schedule, now);

        await _unitOfWork.CustomReportSchedules.AddAsync(schedule);
        await _unitOfWork.SaveChangesAsync();

        return MapToScheduleDto(schedule);
    }

    public async Task<CustomReportScheduleDto> UpdateScheduleAsync(int scheduleId, UpdateScheduleRequest req)
    {
        var schedule = await _unitOfWork.CustomReportSchedules.GetByIdTrackedAsync(scheduleId);
        if (schedule == null || schedule.IsDeleted())
            throw new InvalidOperationException("调度不存在");

        var report = await _unitOfWork.CustomReports.GetByIdAsync(schedule.CustomReportId);
        if (report == null || report.IsDeleted())
            throw new InvalidOperationException("报表不存在");

        if (!await CanEditAsync(report))
            throw new InvalidOperationException("无权修改此报表");

        ValidateScheduleRequest(req);

        schedule.Recurrence = req.Recurrence.ToLowerInvariant();
        schedule.WeekDay = req.WeekDay;
        schedule.SendTime = req.SendTime;
        schedule.Recipients = req.Recipients;
        schedule.Subject = req.Subject;
        schedule.AttachmentFormat = req.AttachmentFormat.ToLowerInvariant();
        schedule.IsActive = req.IsActive;
        schedule.UpdatedAt = DateTime.UtcNow;
        schedule.NextRunAt = ComputeNextRun(schedule, DateTime.UtcNow);

        await _unitOfWork.SaveChangesAsync();

        return MapToScheduleDto(schedule);
    }

    public async Task DeleteScheduleAsync(int scheduleId)
    {
        var schedule = await _unitOfWork.CustomReportSchedules.GetByIdTrackedAsync(scheduleId);
        if (schedule == null || schedule.IsDeleted())
            return;

        var report = await _unitOfWork.CustomReports.GetByIdAsync(schedule.CustomReportId);
        if (report != null && !report.IsDeleted())
        {
            if (!await CanEditAsync(report))
                throw new InvalidOperationException("无权修改此报表");
        }

        schedule.SoftDelete();
        await _unitOfWork.SaveChangesAsync();
    }

    public async Task ProcessScheduledReportsAsync(DateTime now)
    {
        var schedules = await _unitOfWork.CustomReportSchedules.GetPendingScansAsync(now, 100);

        foreach (var schedule in schedules)
        {
            try
            {
                await ProcessSingleScheduleAsync(schedule, now);
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "处理调度报表失败 ScheduleId={ScheduleId}", schedule.Id);
            }
        }
    }

    private async Task ProcessSingleScheduleAsync(CustomReportSchedule schedule, DateTime now)
    {
        var trackedSchedule = await _unitOfWork.CustomReportSchedules.GetByIdTrackedAsync(schedule.Id);
        if (trackedSchedule == null || trackedSchedule.IsDeleted() || !trackedSchedule.IsActive)
            return;

        var report = await _unitOfWork.CustomReports.GetByIdAsync(trackedSchedule.CustomReportId);
        if (report == null || report.IsDeleted())
        {
            trackedSchedule.IsActive = false;
            trackedSchedule.UpdatedAt = now;
            await _unitOfWork.SaveChangesAsync();
            return;
        }

        List<string> recipients;
        try
        {
            recipients = JsonSerializer.Deserialize<List<string>>(trackedSchedule.Recipients) ?? new List<string>();
        }
        catch
        {
            recipients = new List<string>();
        }

        if (recipients.Count == 0)
        {
            trackedSchedule.LastRunAt = now;
            trackedSchedule.NextRunAt = ComputeNextRun(trackedSchedule, now);
            trackedSchedule.UpdatedAt = now;
            await _unitOfWork.SaveChangesAsync();
            return;
        }

        var (content, contentType, fileName) = await ExportInternalAsync(report, trackedSchedule.AttachmentFormat);

        var owner = await _unitOfWork.Users.GetByIdAsync(trackedSchedule.OwnerId);
        Mailbox? sendMailbox = null;
        if (owner != null)
        {
            sendMailbox = await _unitOfWork.Mailboxes.GetQueryable()
                .AsNoTracking()
                .FirstOrDefaultAsync();
        }

        trackedSchedule.LastRunAt = now;
        trackedSchedule.NextRunAt = ComputeNextRun(trackedSchedule, now);
        trackedSchedule.UpdatedAt = now;

        if (sendMailbox != null)
        {
            var tempDir = Path.Combine(_attachmentBasePath, "temp");
            Directory.CreateDirectory(tempDir);
            var tempFile = Path.Combine(tempDir, $"report_{trackedSchedule.Id}_{now:yyyyMMddHHmmss}_{fileName}");
            var attachmentEntity = new NoneadCSS.Domain.Entities.Attachment
            {
                FileName = fileName,
                StoredFileName = Path.GetFileName(tempFile),
                FileDir = "temp",
                MimeType = contentType,
                Size = content.Length,
                CreatedAt = now
            };

            var body = $"<p>您好，</p><p>请查收定时报表：<b>{System.Net.WebUtility.HtmlEncode(report.Name)}</b></p>" +
                       $"<p>统计周期详见附件。</p>";

            var attachmentList = new List<NoneadCSS.Domain.Entities.Attachment> { attachmentEntity };

            try
            {
                await File.WriteAllBytesAsync(tempFile, content);

                foreach (var to in recipients)
                {
                    try
                    {
                        await _emailService.SendEmailAsync(sendMailbox, to, trackedSchedule.Subject, body, attachmentList);
                    }
                    catch (Exception ex)
                    {
                        _logger.LogWarning(ex, "发送调度报表邮件失败 To={To} ScheduleId={ScheduleId}", to, trackedSchedule.Id);
                    }
                }
            }
            finally
            {
                try { if (File.Exists(tempFile)) File.Delete(tempFile); } catch { /* ignore */ }
            }
        }

        await _unitOfWork.SaveChangesAsync();
    }

    private async Task<(byte[] Content, string ContentType, string FileName)> ExportInternalAsync(
        CustomReport report, string format)
    {
        var result = await ExecuteInternalAsync(report);
        var safeName = CustomReportExportHelper.SanitizeFileName(report.Name);

        return format.ToLowerInvariant() switch
        {
            "csv" => (_exportHelper.ExportToCsv(result), "text/csv; charset=utf-8", $"{safeName}.csv"),
            "xlsx" or "excel" => (_exportHelper.ExportToExcel(result),
                "application/vnd.openxmlformats-officedocument.spreadsheetml.sheet", $"{safeName}.xlsx"),
            _ => (_exportHelper.ExportToPdf(result), "application/pdf", $"{safeName}.pdf")
        };
    }

    private void ValidateCreateUpdateRequest(string name, string? description, string visibility, int? teamId, string reportConfig)
    {
        if (string.IsNullOrWhiteSpace(name))
            throw new InvalidOperationException("报表名称不能为空");

        if (name.Length > 100)
            throw new InvalidOperationException("报表名称最多 100 字符");

        if (!string.IsNullOrEmpty(description) && description.Length > 500)
            throw new InvalidOperationException("描述最多 500 字符");

        if (!ValidVisibilities.Contains(visibility))
            throw new InvalidOperationException("可见性必须是 private/team/public 之一");

        if (visibility.Equals("team", StringComparison.OrdinalIgnoreCase) && !teamId.HasValue)
            throw new InvalidOperationException("team 可见性需要指定 TeamId");

        if (reportConfig.Length > 10000)
            throw new InvalidOperationException("报表配置过大");

        try
        {
            using var doc = JsonDocument.Parse(reportConfig);
        }
        catch
        {
            throw new InvalidOperationException("报表配置 JSON 格式无效");
        }
    }

    private static void ValidateScheduleRequest(CreateScheduleRequest req)
    {
        if (!ValidRecurrences.Contains(req.Recurrence))
            throw new InvalidOperationException("重复周期必须是 daily/weekly/monthly 之一");

        if (req.Recurrence.Equals("weekly", StringComparison.OrdinalIgnoreCase))
        {
            if (!req.WeekDay.HasValue || req.WeekDay.Value < 0 || req.WeekDay.Value > 6)
                throw new InvalidOperationException("weekly 周期需要指定有效的 WeekDay (0-6)");
        }

        if (!ValidAttachmentFormats.Contains(req.AttachmentFormat))
            throw new InvalidOperationException("附件格式必须是 csv/xlsx/pdf 之一");

        if (string.IsNullOrWhiteSpace(req.Subject))
            throw new InvalidOperationException("邮件主题不能为空");

        if (req.Subject.Length > 200)
            throw new InvalidOperationException("邮件主题最多 200 字符");

        List<string> recipients;
        try
        {
            recipients = JsonSerializer.Deserialize<List<string>>(req.Recipients) ?? new List<string>();
        }
        catch
        {
            throw new InvalidOperationException("收件人 JSON 格式无效");
        }

        if (recipients.Count == 0)
            throw new InvalidOperationException("请至少添加一个收件人");

        if (recipients.Count > 50)
            throw new InvalidOperationException("收件人最多 50 个");

        var invalidEmail = recipients.FirstOrDefault(r => !EmailRegex.IsMatch(r));
        if (invalidEmail != null)
            throw new InvalidOperationException($"邮箱格式不正确: {invalidEmail}");
    }

    private async Task<bool> CanViewAsync(CustomReport report)
    {
        if (_currentUser.IsSuperAdmin)
            return true;

        var currentUserId = _currentUser.UserId;
        if (report.OwnerId == currentUserId)
            return true;

        if (report.Visibility.Equals("public", StringComparison.OrdinalIgnoreCase))
            return true;

        if (report.Visibility.Equals("team", StringComparison.OrdinalIgnoreCase) && report.TeamId.HasValue)
        {
            var teamIds = await GetCurrentUserTeamIdsAsync();
            return teamIds.Contains(report.TeamId.Value);
        }

        return false;
    }

    private Task<bool> CanEditAsync(CustomReport report)
    {
        var canEdit = _currentUser.IsSuperAdmin || report.OwnerId == _currentUser.UserId;
        return Task.FromResult(canEdit);
    }

    private async Task<List<int>> GetCurrentUserTeamIdsAsync()
    {
        var teams = await _unitOfWork.Teams.GetByUserIdAsync(_currentUser.UserId);
        return teams.Select(t => t.Id).ToList();
    }

    private async Task<CustomReportExecutionResultDto> ExecuteInternalAsync(CustomReport report)
    {
        ReportConfigDto? config;
        try
        {
            // report.ReportConfig 由创建/更新接口写入，正常应为合法 JSON。
            // 反序列化失败属数据损坏或程序 bug，必须记录日志并明确告知调用方，
            // 不能静默回退到默认配置（会产生与用户预期不符的报表，掩盖真实问题）。
            if (string.IsNullOrWhiteSpace(report.ReportConfig))
            {
                _logger.LogWarning("自定义报表 ReportConfig 为空，使用默认配置：ReportId={ReportId}", report.Id);
                config = new ReportConfigDto();
            }
            else
            {
                config = JsonSerializer.Deserialize<ReportConfigDto>(report.ReportConfig)
                    ?? new ReportConfigDto();
            }
        }
        catch (JsonException ex)
        {
            _logger.LogError(ex, "自定义报表 ReportConfig 反序列化失败，使用默认配置：ReportId={ReportId}", report.Id);
            config = new ReportConfigDto();
        }

        config ??= new ReportConfigDto();

        var (startDate, endDate) = ComputeDateRange(config.DateRange);
        var fullReport = await _reportService.GetReportAsync(startDate, endDate, config.MailboxId);

        var sections = new List<SectionResultDto>();
        var configSections = config.Sections.Count > 0
            ? config.Sections
            : new List<SectionConfig>
            {
                new() { Key = "summary", ChartType = "table" },
                new() { Key = "trend", ChartType = "line" },
                new() { Key = "statusDistribution", ChartType = "pie" },
                new() { Key = "userPerformance", ChartType = "bar" },
                new() { Key = "mailboxLoad", ChartType = "bar" },
                new() { Key = "customerDomains", ChartType = "table" }
            };

        foreach (var sec in configSections)
        {
            object? data = sec.Key switch
            {
                "summary" => fullReport.Summary,
                "trend" => ApplyGroupBy(fullReport.Trend, config.GroupBy),
                "statusDistribution" => fullReport.StatusDistribution,
                "userPerformance" => fullReport.UserPerformance,
                "mailboxLoad" => fullReport.MailboxLoad,
                "customerDomains" => fullReport.CustomerDomains,
                _ => null
            };

            if (data != null)
            {
                sections.Add(new SectionResultDto
                {
                    Key = sec.Key,
                    ChartType = sec.ChartType,
                    Data = data
                });
            }
            else
            {
                // 未知 section key 此前被直接丢弃：前端能保存配置、执行后该段却凭空消失，
                // 且没有任何日志可查。至少留下可检索的错误日志，指明是哪个报表的哪个 Key。
                _logger.LogError("自定义报表包含未知的数据段 Key={Key}，该数据段不会出现在执行结果中：ReportId={ReportId}", sec.Key, report.Id);
            }
        }

        return new CustomReportExecutionResultDto
        {
            ReportMeta = new ReportMetaDto
            {
                Id = report.Id,
                Name = report.Name,
                StartDate = startDate,
                EndDate = endDate,
                MailboxId = config.MailboxId,
                GroupBy = config.GroupBy
            },
            Sections = sections
        };
    }

    private static object ApplyGroupBy(List<ReportTrendPointDto> trend, string groupBy)
    {
        if (groupBy.Equals("none", StringComparison.OrdinalIgnoreCase) || trend.Count == 0)
            return trend;

        if (!DateTime.TryParse(trend[0].Date, out _))
            return trend;

        var grouped = new Dictionary<string, ReportTrendPointDto>();
        foreach (var p in trend)
        {
            if (!DateTime.TryParse(p.Date, out var dt)) continue;
            var key = groupBy switch
            {
                "week" => dt.AddDays(-(int)dt.DayOfWeek).ToString("yyyy-MM-dd"),
                "month" => dt.ToString("yyyy-MM-01"),
                _ => p.Date
            };

            if (!grouped.ContainsKey(key))
            {
                grouped[key] = new ReportTrendPointDto { Date = key };
            }
            grouped[key].NewCount += p.NewCount;
            grouped[key].ClosedCount += p.ClosedCount;
            grouped[key].ReplyCount += p.ReplyCount;
        }

        return grouped.Values.OrderBy(x => x.Date).ToList();
    }

    private static (DateTime Start, DateTime End) ComputeDateRange(DateRangeConfig range)
    {
        var now = DateTime.UtcNow;
        return range.Type switch
        {
            "custom" when !string.IsNullOrEmpty(range.Start) && !string.IsNullOrEmpty(range.End) =>
                ParseRange(range.Start, range.End),
            _ => ComputePresetRange(range.Preset, now)
        };
    }

    private static (DateTime Start, DateTime End) ComputePresetRange(string? preset, DateTime now)
    {
        return preset switch
        {
            "last7" => (now.AddDays(-7).Date, now.Date.AddDays(1)),
            "last30" => (now.AddDays(-30).Date, now.Date.AddDays(1)),
            "last90" => (now.AddDays(-90).Date, now.Date.AddDays(1)),
            "thisWeek" => (now.AddDays(-(int)now.DayOfWeek).Date, now.Date.AddDays(1)),
            "thisMonth" => (new DateTime(now.Year, now.Month, 1), new DateTime(now.Year, now.Month, 1).AddMonths(1)),
            "lastMonth" => (new DateTime(now.Year, now.Month, 1).AddMonths(-1), new DateTime(now.Year, now.Month, 1)),
            _ => (now.AddDays(-30).Date, now.Date.AddDays(1))
        };
    }

    private static (DateTime Start, DateTime End) ParseRange(string start, string end)
    {
        if (!DateTime.TryParse(start, out var s)) s = DateTime.UtcNow.AddDays(-30);
        if (!DateTime.TryParse(end, out var e)) e = DateTime.UtcNow;
        return (s.Date, e.Date.AddDays(1));
    }

    private static DateTime ComputeNextRun(CustomReportSchedule schedule, DateTime nowUtc)
    {
        TimeZoneInfo tz;
        try
        {
            tz = TimeZoneInfo.FindSystemTimeZoneById("Asia/Shanghai");
        }
        catch
        {
            tz = TimeZoneInfo.Local;
        }

        var nowTz = TimeZoneInfo.ConvertTimeFromUtc(nowUtc, tz);
        if (!TimeSpan.TryParse(schedule.SendTime, out var sendTs))
            sendTs = new TimeSpan(9, 0, 0);

        DateTime candidateTz;
        switch (schedule.Recurrence.ToLowerInvariant())
        {
            case "weekly":
                var targetDow = (DayOfWeek)(schedule.WeekDay ?? 0);
                var thisWeek = nowTz.Date.AddDays(-(int)nowTz.DayOfWeek + (int)targetDow).Date + sendTs;
                candidateTz = thisWeek > nowTz ? thisWeek : thisWeek.AddDays(7);
                break;

            case "monthly":
                try
                {
                    var thisMonth = new DateTime(nowTz.Year, nowTz.Month, 1).Date + sendTs;
                    if (thisMonth > nowTz)
                    {
                        candidateTz = thisMonth;
                    }
                    else
                    {
                        var next = nowTz.Date.AddMonths(1);
                        candidateTz = new DateTime(next.Year, next.Month, 1).Date + sendTs;
                    }
                }
                catch
                {
                    candidateTz = nowTz.Date.AddDays(1) + sendTs;
                }
                break;

            case "daily":
            default:
                var today = nowTz.Date + sendTs;
                candidateTz = today > nowTz ? today : today.AddDays(1);
                break;
        }

        return TimeZoneInfo.ConvertTimeToUtc(candidateTz, tz);
    }

    private async Task<List<CustomReportDto>> MapToDtosAsync(IEnumerable<CustomReport> reports)
    {
        var reportList = reports.ToList();
        var ownerIds = reportList.Select(r => r.OwnerId).Distinct().ToList();
        var teamIds = reportList.Where(r => r.TeamId.HasValue).Select(r => r.TeamId!.Value).Distinct().ToList();

        var owners = (await _unitOfWork.Users.GetByIdsAsync(ownerIds))
            .ToDictionary(u => u.Id);

        var teams = new Dictionary<int, Team>();
        if (teamIds.Count > 0)
        {
            var teamList = await _unitOfWork.Teams.FindAsync(t => teamIds.Contains(t.Id) && t.DeletedAt == null);
            teams = teamList.ToDictionary(t => t.Id);
        }

        var currentUserId = _currentUser.UserId;
        var result = new List<CustomReportDto>();
        foreach (var r in reportList)
        {
            var dto = new CustomReportDto
            {
                Id = r.Id,
                Name = r.Name,
                Description = r.Description,
                Category = r.Category,
                Visibility = r.Visibility,
                OwnerId = r.OwnerId,
                TeamId = r.TeamId,
                ReportConfig = r.ReportConfig,
                IsDashboardPinned = r.IsDashboardPinned,
                DashboardSortOrder = r.DashboardSortOrder,
                CreatedAt = r.CreatedAt,
                UpdatedAt = r.UpdatedAt,
                CanEdit = _currentUser.IsSuperAdmin || r.OwnerId == currentUserId,
                CanDelete = _currentUser.IsSuperAdmin || r.OwnerId == currentUserId
            };

            if (owners.TryGetValue(r.OwnerId, out var owner))
                dto.OwnerName = $"{owner.FirstName} {owner.LastName}".Trim();

            if (r.TeamId.HasValue && teams.TryGetValue(r.TeamId.Value, out var team))
                dto.TeamName = team.Name;

            result.Add(dto);
        }
        return result;
    }

    private async Task<CustomReportDto> MapToDtoAsync(CustomReport report)
    {
        var currentUserId = _currentUser.UserId;
        var dto = new CustomReportDto
        {
            Id = report.Id,
            Name = report.Name,
            Description = report.Description,
            Category = report.Category,
            Visibility = report.Visibility,
            OwnerId = report.OwnerId,
            TeamId = report.TeamId,
            ReportConfig = report.ReportConfig,
            IsDashboardPinned = report.IsDashboardPinned,
            DashboardSortOrder = report.DashboardSortOrder,
            CreatedAt = report.CreatedAt,
            UpdatedAt = report.UpdatedAt,
            CanEdit = _currentUser.IsSuperAdmin || report.OwnerId == currentUserId,
            CanDelete = _currentUser.IsSuperAdmin || report.OwnerId == currentUserId
        };

        var owner = await _unitOfWork.Users.GetByIdAsync(report.OwnerId);
        if (owner != null)
            dto.OwnerName = $"{owner.FirstName} {owner.LastName}".Trim();

        if (report.TeamId.HasValue)
        {
            var team = await _unitOfWork.Teams.GetByIdAsync(report.TeamId.Value);
            if (team != null)
                dto.TeamName = team.Name;
        }

        return dto;
    }

    private static CustomReportScheduleDto MapToScheduleDto(CustomReportSchedule s)
    {
        List<string> recipients;
        try
        {
            recipients = JsonSerializer.Deserialize<List<string>>(s.Recipients) ?? new List<string>();
        }
        catch
        {
            recipients = new List<string>();
        }

        return new CustomReportScheduleDto
        {
            Id = s.Id,
            CustomReportId = s.CustomReportId,
            Recurrence = s.Recurrence,
            WeekDay = s.WeekDay,
            SendTime = s.SendTime,
            Recipients = recipients,
            Subject = s.Subject,
            AttachmentFormat = s.AttachmentFormat,
            IsActive = s.IsActive,
            LastRunAt = s.LastRunAt,
            NextRunAt = s.NextRunAt
        };
    }
}

public class ReportConfigDto
{
    public DateRangeConfig DateRange { get; set; } = new();
    public int? MailboxId { get; set; }
    public string GroupBy { get; set; } = "none";
    public List<SectionConfig> Sections { get; set; } = new();
}

public class DateRangeConfig
{
    public string Type { get; set; } = "preset";
    public string? Preset { get; set; }
    public string? Start { get; set; }
    public string? End { get; set; }
}

public class SectionConfig
{
    public string Key { get; set; } = string.Empty;
    public string ChartType { get; set; } = "table";
}
