namespace NoneadCSS.Domain.Entities;

public class ConversationFolder
{
    public int ConversationId { get; set; }
    public int FolderId { get; set; }

    public Conversation Conversation { get; set; } = null!;
    public Folder Folder { get; set; } = null!;
}
