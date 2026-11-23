namespace NoneadCSS.Domain.Enums;

public enum FolderType
{
    Unassigned = 1,
    Mine = 20,
    Starred = 25,
    Drafts = 30,
    Assigned = 40,
    Closed = 60,
    Deleted = 70,
    Spam = 80,
    /// <summary>
    /// 自定义文件夹类型，由用户通过前端"新建文件夹"创建。
    /// 不属于系统文件夹，EnsureMailboxFoldersAsync 不会自动补齐。
    /// </summary>
    Custom = 100
}
