using System.ComponentModel.DataAnnotations;

namespace NoneadCSS.Application.DTOs.Requests;

public class BulkConversationRequest
{
    [Required(ErrorMessage = "会话ID列表不能为空")]
    [MinLength(1, ErrorMessage = "至少选择一个会话")]
    [MaxLength(500, ErrorMessage = "最多支持 500 个会话批量操作")]
    public List<int> ConversationIds { get; set; } = [];
}

public class BulkAssignRequest : BulkConversationRequest
{
    [Range(1, int.MaxValue, ErrorMessage = "用户ID必须大于0")]
    public int UserId { get; set; }
}

public class BulkStatusRequest : BulkConversationRequest
{
    [Range(1, 10, ErrorMessage = "状态值无效")]
    public int Status { get; set; }
}

public class BulkPriorityRequest : BulkConversationRequest
{
    /// <summary>
    /// 优先级：0=Low、1=Normal、2=High、3=Urgent
    /// </summary>
    [Range(0, 3, ErrorMessage = "优先级值无效")]
    public int Priority { get; set; }
}

public class AddNoteRequest
{
    [MaxLength(50000, ErrorMessage = "备注最多 50000 个字符")]
    public string Body { get; set; } = string.Empty;
    public List<int>? AttachmentIds { get; set; }
}
