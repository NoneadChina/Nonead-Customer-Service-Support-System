using NoneadCSS.Api.Authorization;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using NoneadCSS.Application.DTOs.Requests;
using NoneadCSS.Application.DTOs.Responses;
using NoneadCSS.Application.Services;
using NoneadCSS.Domain.Enums;
using System.Security.Claims;
using NoneadCSS.Api.Filters;

namespace NoneadCSS.Api.Controllers;

[Route("api/[controller]")]
[ApiController]
[Authorize]
[RequireModule("core")]
public class ImportExportController : ControllerBase
{
    private int GetCurrentUserId()
    {
        return int.Parse(User.FindFirst(ClaimTypes.NameIdentifier)?.Value ?? "0");
    }

    private bool IsAdmin()
    {
        var roleClaim = User.FindFirst(ClaimTypes.Role)?.Value;
        if (int.TryParse(roleClaim, out var role))
        {
            return role == (int)UserRoleType.SuperAdmin;
        }
        return false;
    }

    private IActionResult RequireAdmin()
    {
        if (!IsAdmin())
        {
            return Forbid();
        }
        return null!;
    }

    private readonly IImportExportService _importExportService;

    public ImportExportController(IImportExportService importExportService)
    {
        _importExportService = importExportService;
    }

    [HttpGet("entities")]
    [RequireSuperAdmin]
    public async Task<IActionResult> GetAvailableEntities()
    {
        var entities = await _importExportService.GetAvailableEntitiesAsync();
        return Ok(entities);
    }

    [HttpPost("export")]
    [RequireSuperAdmin]
    [SkipApiResponseWrapper]
    public async Task<IActionResult> ExportData([FromBody] ExportRequest request)
    {
        if (request.EntityTypes == null || request.EntityTypes.Count == 0)
        {
            return BadRequest(new { message = "请选择要导出的数据类型" });
        }

        var data = await _importExportService.ExportDataAsync(request);
        var filename = $"NoneadCSS_export_{DateTime.UtcNow:yyyyMMdd_HHmmss}.json";

        return File(data, "application/json", filename);
    }

    [HttpPost("import/preview")]
    [RequireSuperAdmin]
    public async Task<IActionResult> PreviewImport([FromBody] ImportPreviewRequest request)
    {
        if (string.IsNullOrEmpty(request.Data))
        {
            return BadRequest(new { message = "请提供要导入的数据" });
        }

        var preview = await _importExportService.PreviewImportAsync(request);
        return Ok(preview);
    }

    [HttpPost("import")]
    [RequireSuperAdmin]
    public async Task<IActionResult> ImportData([FromBody] ImportRequest request)
    {
        if (string.IsNullOrEmpty(request.Data))
        {
            return BadRequest(new { message = "请提供要导入的数据" });
        }

        var result = await _importExportService.ImportDataAsync(request);
        return Ok(result);
    }
}
