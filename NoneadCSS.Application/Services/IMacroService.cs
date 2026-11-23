using NoneadCSS.Application.DTOs.Responses;

namespace NoneadCSS.Application.Services;

public interface IMacroService
{
    Task<MacroDto?> GetByIdAsync(int id);
    Task<List<MacroDto>> GetByMailboxAsync(int? mailboxId);
    Task<(List<MacroDto> Items, int Total)> GetPagedAsync(MacroQuery query);
    Task<MacroOverviewResponse> GetOverviewAsync();
    Task<MacroDto> CreateAsync(CreateMacroRequest request);
    Task<MacroDto?> UpdateAsync(UpdateMacroRequest request);
    Task<bool> DeleteAsync(int id);
    Task<bool> ToggleActiveAsync(int id);
    Task<MacroDto?> DuplicateAsync(int id, string? newName = null);
    Task<MacroPreviewResponse> PreviewAsync(int id, int conversationId);

    // 垃圾箱
    Task<(List<MacroDto> Items, int Total)> GetTrashAsync(int page, int pageSize, string? keyword = null);
    Task<int> GetTrashCountAsync();
    Task<bool> RestoreAsync(int id);
    Task<bool> HardDeleteAsync(int id);
    Task<int> ClearTrashAsync();

    Task<ExecuteMacroResponse> ExecuteAsync(int macroId, int conversationId, int userId);
    Task<ExecuteMacroResponse> ExecuteForWorkflowAsync(int macroId, int conversationId);
    Task<(List<MacroExecutionLogDto> Items, int Total)> GetExecutionLogsAsync(MacroExecutionLogQuery query);
}
