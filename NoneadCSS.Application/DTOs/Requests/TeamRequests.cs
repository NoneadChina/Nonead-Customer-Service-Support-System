using System.ComponentModel.DataAnnotations;

namespace NoneadCSS.Application.DTOs.Requests;

public class CreateTeamRequest
{
    [Required(ErrorMessage = "团队名称不能为空")]
    [MaxLength(100, ErrorMessage = "团队名称最多 100 个字符")]
    public string Name { get; set; } = string.Empty;

    [MaxLength(500, ErrorMessage = "团队描述最多 500 个字符")]
    public string? Description { get; set; }

    [Range(1, int.MaxValue, ErrorMessage = "邮箱ID必须大于0")]
    public int? MailboxId { get; set; }

    public bool IsActive { get; set; } = true;

    [Range(0, 9999, ErrorMessage = "排序值无效")]
    public int SortOrder { get; set; }
}

public class UpdateTeamRequest
{
    [Required(ErrorMessage = "团队名称不能为空")]
    [MaxLength(100, ErrorMessage = "团队名称最多 100 个字符")]
    public string Name { get; set; } = string.Empty;

    [MaxLength(500, ErrorMessage = "团队描述最多 500 个字符")]
    public string? Description { get; set; }

    [Range(1, int.MaxValue, ErrorMessage = "邮箱ID必须大于0")]
    public int? MailboxId { get; set; }

    public bool IsActive { get; set; }

    [Range(0, 9999, ErrorMessage = "排序值无效")]
    public int SortOrder { get; set; }
}

public class AddTeamMemberRequest
{
    [Range(1, int.MaxValue, ErrorMessage = "用户ID必须大于0")]
    public int UserId { get; set; }

    [Range(1, 2, ErrorMessage = "角色值无效（1=成员, 2=负责人）")]
    public int Role { get; set; } = 1;
}

public class UpdateTeamMemberRequest
{
    [Range(1, 2, ErrorMessage = "角色值无效（1=成员, 2=负责人）")]
    public int Role { get; set; }
}

public class TeamQuery
{
    [Range(1, int.MaxValue, ErrorMessage = "页码必须大于0")]
    public int Page { get; set; } = 1;

    [Range(1, 100, ErrorMessage = "每页条数 1-100")]
    public int PageSize { get; set; } = 20;

    [Range(1, int.MaxValue, ErrorMessage = "邮箱ID必须大于0")]
    public int? MailboxId { get; set; }

    public bool? IsActive { get; set; }

    [MaxLength(100, ErrorMessage = "关键词最多 100 个字符")]
    public string? Keyword { get; set; }
}
