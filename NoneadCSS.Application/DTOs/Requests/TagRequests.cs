using System.ComponentModel.DataAnnotations;

namespace NoneadCSS.Application.DTOs.Requests;

public class CreateTagRequest
{
    [Required(ErrorMessage = "标签名称不能为空")]
    [MaxLength(100, ErrorMessage = "标签名称最多 100 个字符")]
    public string Name { get; set; } = string.Empty;

    [MaxLength(7, ErrorMessage = "颜色值格式无效")]
    public string Color { get; set; } = "#00b7ff";

    public int? MailboxId { get; set; }
}

public class UpdateTagRequest
{
    [Required(ErrorMessage = "标签名称不能为空")]
    [MaxLength(100, ErrorMessage = "标签名称最多 100 个字符")]
    public string Name { get; set; } = string.Empty;

    [MaxLength(7, ErrorMessage = "颜色值格式无效")]
    public string Color { get; set; } = "#00b7ff";
}

public class UpdateConversationTagsRequest
{
    [Required(ErrorMessage = "标签ID列表不能为空")]
    [MaxLength(100, ErrorMessage = "单次最多操作 100 个标签")]
    public List<int> TagIds { get; set; } = [];
}

public class BulkUpdateConversationTagsRequest
{
    [Required(ErrorMessage = "会话ID列表不能为空")]
    [MinLength(1, ErrorMessage = "至少需要指定一个会话")]
    [MaxLength(500, ErrorMessage = "单次最多批量操作 500 个会话")]
    public List<int> ConversationIds { get; set; } = [];

    [Required(ErrorMessage = "标签ID列表不能为空")]
    [MaxLength(100, ErrorMessage = "单次最多操作 100 个标签")]
    public List<int> TagIds { get; set; } = [];
}
