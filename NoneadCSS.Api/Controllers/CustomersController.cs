using NoneadCSS.Api.Authorization;
using NoneadCSS.Api.Filters;
using NoneadCSS.Application.DTOs.Requests;
using NoneadCSS.Application.DTOs.Responses;
using NoneadCSS.Application.Services;
using NoneadCSS.Domain;
using NoneadCSS.Domain.Enums;
using NoneadCSS.Infrastructure.Services;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using System.Security.Claims;

namespace NoneadCSS.Api.Controllers;

/// <summary>
/// 客户控制器 - 管理客户档案、查看客户关联的会话
/// </summary>
[Route("api/[controller]")]
[RequireModule("core")]
public class CustomersController : BaseController
{
    private readonly ICustomerService _customerService;
    private readonly ILocalizationService _localizer;
    private readonly IAttachmentService _attachmentService;
    private readonly IUserRoleResolver _userRoleResolver;
    private readonly IUnitOfWork _unitOfWork;
    private readonly IMailboxService _mailboxService;

    public CustomersController(ICustomerService customerService, ILocalizationService localizer, 
        IAttachmentService attachmentService, IUserRoleResolver userRoleResolver,
        IUnitOfWork unitOfWork, IMailboxService mailboxService)
    {
        _customerService = customerService;
        _localizer = localizer;
        _attachmentService = attachmentService;
        _userRoleResolver = userRoleResolver;
        _unitOfWork = unitOfWork;
        _mailboxService = mailboxService;
    }

    private async Task<bool> HasCustomerAccess(int customerId)
    {
        var userId = CurrentUserId ?? 0;
        if (await _userRoleResolver.IsSuperAdminAsync(userId))
        {
            return true;
        }

        // M-7：GetByCustomerIdAsync 现已返回 (Items, Total) 元组
        var (conversations, _) = await _unitOfWork.Conversations.GetByCustomerIdAsync(customerId, 1, 100);
        if (!conversations.Any())
        {
            // 客户没有关联会话时，仅允许有邮箱访问权限的用户访问
            // 避免无邮箱权限的用户通过 ID 枚举访问任意客户数据
            var mailboxes = await _mailboxService.GetMailboxesByUserIdAsync(userId);
            return mailboxes.Any();
        }

        var conversationMailboxIds = conversations.Select(c => c.MailboxId).Distinct().ToHashSet();
        var userMailboxes = await _mailboxService.GetMailboxesByUserIdAsync(userId);
        return userMailboxes.Any(m => conversationMailboxIds.Contains(m.Id));
    }

    /// <summary>
    /// 搜索客户列表
    /// </summary>
    /// <param name="search">搜索关键词（姓名或邮箱）</param>
    /// <param name="page">页码（默认 1）</param>
    /// <param name="pageSize">每页条数（默认 20）</param>
    /// <returns>分页客户列表</returns>
    [HttpGet]
    public async Task<IActionResult> SearchCustomers(
        [FromQuery] string search = "",
        [FromQuery] int page = 1,
        [FromQuery] int pageSize = PaginationHelper.DefaultPageSize)
    {
        // 限制分页参数上限，防止请求大量数据导致 DoS
        page = Math.Max(page, 1);
        pageSize = Math.Min(Math.Max(pageSize, 1), PaginationHelper.MaxPageSize);

        // 防止 IDOR：非超管仅可查看其邮箱关联的客户
        var userId = CurrentUserId ?? 0;
        if (!await _userRoleResolver.IsSuperAdminAsync(userId))
        {
            var userMailboxes = await _mailboxService.GetMailboxesByUserIdAsync(userId);
            if (!userMailboxes.Any())
            {
                return Ok(new PagedResponse<CustomerResponse> { Data = new List<CustomerResponse>(), TotalItems = 0, CurrentPage = page, PageSize = pageSize, TotalPages = 0 });
            }
            // 将搜索限定在用户有权限的邮箱范围内
            var mailboxIds = userMailboxes.Select(m => m.Id).ToList();
            var customers = await _customerService.SearchCustomersByMailboxIdsAsync(search, mailboxIds, page, pageSize);
            return Ok(customers);
        }

        var allCustomers = await _customerService.SearchCustomersAsync(search, page, pageSize);
        return Ok(allCustomers);
    }

    /// <summary>
    /// 上传客户头像
    /// </summary>
    /// <param name="file">头像文件</param>
    /// <returns>头像 URL</returns>
    /// <response code="200">上传成功</response>
    /// <response code="400">文件验证失败</response>
    [HttpPost("avatar")]
    [RequestSizeLimit(5 * 1024 * 1024)] // 头像上限 5MB（与用户头像保持一致）
    [Permission("customers.update")]
    public async Task<IActionResult> UploadAvatar(IFormFile file)
    {
        try
        {
            var userId = CurrentUserId ?? 0;
            var attachment = await _attachmentService.UploadAsync(file, null, userId);
            var publicUrl = $"/api/attachments/{attachment.Id}/public-preview";
            return Ok(new { url = publicUrl });
        }
        catch (InvalidOperationException)
        {
            return BadRequest(new { message = "操作失败，请重试" });
        }
    }

    /// <summary>
    /// 根据 ID 获取客户详情
    /// </summary>
    /// <param name="id">客户 ID</param>
    /// <returns>客户详情</returns>
    /// <response code="200">返回客户详情</response>
    /// <response code="404">客户不存在</response>
    /// <response code="403">无访问权限</response>
    [HttpGet("{id:int}")]
    public async Task<IActionResult> GetCustomer(int id)
    {
        var customer = await _customerService.GetCustomerByIdAsync(id);

        if (customer == null)
        {
            return NotFound(new { message = _localizer["CustomerNotFound"] });
        }

        if (!await HasCustomerAccess(id))
        {
            return Forbid();
        }

        return Ok(customer);
    }

    /// <summary>
    /// 创建新客户
    /// </summary>
    /// <param name="request">创建客户请求</param>
    /// <returns>新创建的客户</returns>
    /// <response code="201">客户创建成功</response>
    [HttpPost]
    [Permission("customers.create")]
    public async Task<IActionResult> CreateCustomer([FromBody] CreateCustomerRequest request)
    {
        var (customer, errorMessage) = await _customerService.CreateCustomerAsync(request);
        if (errorMessage != null)
        {
            return BadRequest(new { message = errorMessage });
        }
        return CreatedAtAction(nameof(GetCustomer), new { id = customer?.Id }, customer);
    }

    /// <summary>
    /// 更新客户信息
    /// </summary>
    /// <param name="id">客户 ID</param>
    /// <param name="request">更新客户请求</param>
    /// <returns>更新后的客户</returns>
    /// <response code="200">客户更新成功</response>
    /// <response code="404">客户不存在</response>
    /// <response code="403">无访问权限</response>
    [HttpPut("{id:int}")]
    [Permission("customers.update")]
    public async Task<IActionResult> UpdateCustomer(int id, [FromBody] UpdateCustomerRequest request)
    {
        var existing = await _customerService.GetCustomerByIdAsync(id);

        if (existing == null)
        {
            return NotFound(new { message = _localizer["CustomerNotFound"] });
        }

        if (!await HasCustomerAccess(id))
        {
            return Forbid();
        }

        // 优先取 Header If-Match，兼容同时在 Header 与 Body 中传递版本号的前端
        // 与日历 / 会话模块契约保持一致：优先 Header，其次 Body.ifMatch。
        var headerIfMatch = Request.Headers.IfMatch.FirstOrDefault();
        var ifMatch = !string.IsNullOrEmpty(headerIfMatch)
            ? headerIfMatch.Trim('"')
            : request.IfMatch;

        var (customer, errorMessage) = await _customerService.UpdateCustomerAsync(id, request, ifMatch);

        if (errorMessage != null)
        {
            return BadRequest(new { message = errorMessage });
        }

        return Ok(customer);
    }

    /// <summary>
    /// 删除客户（软删除）
    /// </summary>
    /// <param name="id">客户 ID</param>
    /// <returns>无内容</returns>
    /// <response code="204">删除成功</response>
    /// <response code="404">客户不存在</response>
    /// <response code="403">无访问权限</response>
    [HttpDelete("{id:int}")]
    [RequireSuperAdmin]
    public async Task<IActionResult> DeleteCustomer(int id)
    {
        var existing = await _customerService.GetCustomerByIdAsync(id);

        if (existing == null)
        {
            return NotFound(new { message = _localizer["CustomerNotFound"] });
        }

        if (!await HasCustomerAccess(id))
        {
            return Forbid();
        }

        var (success, errorMessage) = await _customerService.DeleteCustomerAsync(id);

        if (!success && errorMessage != null)
        {
            return BadRequest(new { message = errorMessage });
        }

        if (!success)
        {
            return NotFound(new { message = _localizer["CustomerNotFound"] });
        }

        return NoContent();
    }

    /// <summary>
    /// 获取垃圾箱中的客户列表
    /// </summary>
    /// <param name="page">页码（默认 1）</param>
    /// <param name="pageSize">每页条数（默认 20）</param>
    /// <param name="search">搜索关键词</param>
    /// <returns>分页客户列表</returns>
    [HttpGet("trash")]
    [RequireSuperAdmin]
    public async Task<IActionResult> GetTrash(
        [FromQuery] int page = 1,
        [FromQuery] int pageSize = PaginationHelper.DefaultPageSize,
        [FromQuery] string search = "")
    {
        // 限制分页参数上限，防止请求大量数据导致 DoS
        page = Math.Max(page, 1);
        pageSize = Math.Min(Math.Max(pageSize, 1), PaginationHelper.MaxPageSize);
        var result = await _customerService.GetTrashAsync(page, pageSize, search);
        return Ok(result);
    }

    /// <summary>
    /// 恢复已删除的客户
    /// </summary>
    /// <param name="id">客户 ID</param>
    /// <returns>无内容</returns>
    /// <response code="403">无访问权限</response>
    [HttpPost("{id:int}/restore")]
    [RequireSuperAdmin]
    public async Task<IActionResult> RestoreCustomer(int id)
    {
        if (!await HasCustomerAccess(id))
        {
            return Forbid();
        }

        var result = await _customerService.RestoreCustomerAsync(id);
        if (!result)
        {
            return NotFound(new { message = _localizer["CustomerNotFound"] });
        }
        return NoContent();
    }

    /// <summary>
    /// 查询指定客户彻底删除前的关联会话状态（活跃会话数、垃圾箱会话数）
    /// </summary>
    /// <param name="id">客户 ID</param>
    /// <returns>活跃会话数与垃圾箱会话数</returns>
    [HttpGet("{id:int}/hard-delete-status")]
    [RequireSuperAdmin]
    public async Task<IActionResult> GetHardDeleteStatus(int id)
    {
        if (!await HasCustomerAccess(id))
        {
            return Forbid();
        }
        var (activeCount, trashCount) = await _customerService.GetHardDeleteStatusAsync(id);
        return Ok(new { activeCount, trashCount });
    }

    /// <summary>
    /// 彻底删除客户
    /// </summary>
    /// <param name="id">客户 ID</param>
    /// <returns>无内容</returns>
    /// <response code="400">存在关联会话，需先彻底删除会话</response>
    /// <response code="403">无访问权限</response>
    [HttpDelete("{id:int}/hard")]
    [RequireSuperAdmin]
    public async Task<IActionResult> HardDeleteCustomer(int id)
    {
        if (!await HasCustomerAccess(id))
        {
            return Forbid();
        }

        var (success, errorMessage) = await _customerService.HardDeleteCustomerAsync(id);
        if (!success && errorMessage != null)
        {
            return BadRequest(new { message = errorMessage });
        }
        if (!success)
        {
            return NotFound(new { message = _localizer["CustomerNotFound"] });
        }
        return NoContent();
    }

    /// <summary>
    /// 查询清空垃圾箱前的关联会话状态（垃圾箱客户数、活跃会话总数、垃圾箱会话总数）
    /// </summary>
    /// <returns>垃圾箱客户数、活跃会话总数、垃圾箱会话总数</returns>
    [HttpGet("trash/clear-status")]
    [RequireSuperAdmin]
    public async Task<IActionResult> GetClearTrashStatus()
    {
        var (customerCount, totalActiveCount, totalTrashCount) = await _customerService.GetClearTrashStatusAsync();
        return Ok(new { customerCount, totalActiveCount, totalTrashCount });
    }

    /// <summary>
    /// 清空垃圾箱（彻底删除所有已软删除的客户）
    /// </summary>
    /// <returns>删除的客户数量</returns>
    /// <response code="400">存在关联会话，需先彻底删除会话</response>
    [HttpDelete("trash")]
    [RequireSuperAdmin]
    public async Task<IActionResult> ClearTrash()
    {
        var (count, errorMessage) = await _customerService.ClearTrashAsync();
        if (errorMessage != null)
        {
            return BadRequest(new { message = errorMessage });
        }
        return Ok(new { count });
    }

    /// <summary>
    /// 批量删除客户（软删除，级联删除关联会话）
    /// </summary>
    /// <param name="request">客户 ID 列表</param>
    /// <returns>批量操作结果，包含成功/失败数量和失败详情</returns>
    [HttpPost("bulk-delete")]
    [RequireSuperAdmin]
    public async Task<IActionResult> BulkDelete([FromBody] BulkDeleteRequest request)
    {
        if (request?.Ids == null || request.Ids.Count == 0)
        {
            return BadRequest(new { message = "请选择要删除的客户" });
        }
        // S7: 数量上限，防超大请求体导致资源耗尽
        if (request.Ids.Count > 500)
        {
            return BadRequest(new { message = "单次最多删除 500 个客户" });
        }
        var result = await _customerService.BulkDeleteAsync(request.Ids);
        return Ok(result);
    }

    /// <summary>
    /// 导出客户数据（CSV）
    /// </summary>
    /// <param name="ids">客户 ID 列表（为空时导出全部）</param>
    /// <returns>CSV 文件</returns>
    [HttpGet("export")]
    [RequireSuperAdmin]
    public async Task<IActionResult> Export([FromQuery] string? ids = null)
    {
        List<int>? idList = null;
        if (!string.IsNullOrEmpty(ids))
        {
            idList = ids.Split(',', StringSplitOptions.RemoveEmptyEntries)
                .Select(s => int.TryParse(s.Trim(), out var v) ? v : 0)
                .Where(v => v > 0)
                .ToList();
        }
        var (content, fileName) = await _customerService.ExportAsync(idList);
        return File(content, "text/csv; charset=utf-8", fileName);
    }
}

