using System.ComponentModel.DataAnnotations;

namespace NoneadCSS.Application.DTOs.Requests;

public class CreateFolderRequest
{
    [Range(1, int.MaxValue, ErrorMessage = "MailboxId 无效")]
    public int MailboxId { get; set; }

    [Required(AllowEmptyStrings = false, ErrorMessage = "文件夹名称不能为空")]
    [MaxLength(100, ErrorMessage = "文件夹名称不能超过 100 字符")]
    public string Name { get; set; } = string.Empty;

    [Range(0, 100, ErrorMessage = "文件夹类型无效")]
    public int Type { get; set; }
}

public class UpdateFolderRequest
{
    [Required(AllowEmptyStrings = false, ErrorMessage = "文件夹名称不能为空")]
    [MaxLength(100, ErrorMessage = "文件夹名称不能超过 100 字符")]
    public string Name { get; set; } = string.Empty;
}
