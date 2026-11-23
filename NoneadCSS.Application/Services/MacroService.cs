using System.Text.Json;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;
using NoneadCSS.Application.DTOs.Requests;
using NoneadCSS.Application.DTOs.Responses;
using NoneadCSS.Domain;
using NoneadCSS.Domain.Entities;
using NoneadCSS.Domain.Enums;

namespace NoneadCSS.Application.Services;

public class MacroService : IMacroService
{
    private readonly IUnitOfWork _unitOfWork;
    private readonly IConversationService _conversationService;
    private readonly IFolderService _folderService;
    private readonly ILogger<MacroService> _logger;
    private readonly ICurrentUserAccessor _currentUserAccessor;
    private readonly IConversationThreadService _threadService;

    /// <summary>
    /// 宏动作 JSON 序列化/反序列化选项。
    /// 兼容 camelCase（前端、种子数据）和 PascalCase（C# 默认）两种属性名。
    /// </summary>
    private static readonly JsonSerializerOptions ActionJsonOptions = new()
    {
        PropertyNameCaseInsensitive = true,
        Encoder = System.Text.Encodings.Web.JavaScriptEncoder.UnsafeRelaxedJsonEscaping,
    };

    public MacroService(IUnitOfWork unitOfWork, IConversationService conversationService, IFolderService folderService, ILogger<MacroService> logger, ICurrentUserAccessor currentUserAccessor, IConversationThreadService threadService)
    {
        _unitOfWork = unitOfWork;
        _conversationService = conversationService;
        _folderService = folderService;
        _logger = logger;
        _currentUserAccessor = currentUserAccessor;
        _threadService = threadService;
    }

    public async Task<MacroDto?> GetByIdAsync(int id)
    {
        var macro = await _unitOfWork.Macros.GetByIdAsync(id);
        if (macro == null) return null;

        if (!await _currentUserAccessor.CanAccessMailboxAsync(macro.MailboxId))
            return null;

        return MapToDto(macro);
    }

    public async Task<List<MacroDto>> GetByMailboxAsync(int? mailboxId)
    {
        var macros = await _unitOfWork.Macros.GetByMailboxAsync(mailboxId);
        return macros.Select(MapToDto).ToList();
    }

    public async Task<(List<MacroDto> Items, int Total)> GetPagedAsync(MacroQuery query)
    {
        var (items, total) = await _unitOfWork.Macros.GetPagedAsync(
            query.Page,
            query.PageSize,
            query.MailboxId,
            query.IsActive,
            query.Keyword);
        return (items.Select(MapToDto).ToList(), total);
    }

    public async Task<MacroOverviewResponse> GetOverviewAsync()
    {
        var total = await _unitOfWork.Macros.GetCountAsync();
        var active = await _unitOfWork.Macros.GetCountAsync(isActive: true);
        var inactive = total - active;

        var totalExecutions = await _unitOfWork.MacroExecutionLogs.CountAsync();
        var todayExecutions = await _unitOfWork.MacroExecutionLogs.CountTodayAsync();
        var logs = await _unitOfWork.MacroExecutionLogs.GetRecentAsync(10);

        var (topItems, _) = await _unitOfWork.Macros.GetPagedAsync(1, 5, isActive: true);

        return new MacroOverviewResponse
        {
            TotalMacros = total,
            ActiveMacros = active,
            InactiveMacros = inactive,
            TotalExecutions = totalExecutions,
            TodayExecutions = todayExecutions,
            ActionTypes = MacroConst.ActionTypes,
            RecentExecutions = logs.Select(MapLogToDto).ToList(),
            TopMacros = topItems.Select(MapToDto).ToList(),
        };
    }

    public async Task<MacroDto> CreateAsync(CreateMacroRequest request)
    {
        // 过滤掉无效动作（Type 为空的项）
        var validActions = request.Actions
            .Where(a => !string.IsNullOrWhiteSpace(a.Type))
            .ToList();

        var macro = new Macro
        {
            Name = request.Name,
            Description = request.Description,
            MailboxId = request.MailboxId,
            IsActive = request.IsActive,
            SortOrder = request.SortOrder,
            ActionsJson = JsonSerializer.Serialize(validActions, ActionJsonOptions),
        };

        await _unitOfWork.Macros.AddAsync(macro);
        await _unitOfWork.SaveChangesAsync();
        return MapToDto(macro);
    }

    public async Task<MacroDto?> UpdateAsync(UpdateMacroRequest request)
    {
        var macro = await _unitOfWork.Macros.GetByIdTrackedAsync(request.Id);
        if (macro == null) return null;

        if (!await _currentUserAccessor.CanAccessMailboxAsync(macro.MailboxId))
            return null;

        // 过滤掉无效动作（Type 为空的项）
        var validActions = request.Actions
            .Where(a => !string.IsNullOrWhiteSpace(a.Type))
            .ToList();

        macro.Name = request.Name;
        macro.Description = request.Description;
        macro.MailboxId = request.MailboxId;
        macro.IsActive = request.IsActive;
        macro.SortOrder = request.SortOrder;
        macro.ActionsJson = JsonSerializer.Serialize(validActions, ActionJsonOptions);
        macro.UpdatedAt = DateTime.UtcNow;

        await _unitOfWork.SaveChangesAsync();
        return MapToDto(macro);
    }

    public async Task<bool> DeleteAsync(int id)
    {
        var macro = await _unitOfWork.Macros.GetByIdAsync(id);
        if (macro == null) return false;

        if (!await _currentUserAccessor.CanAccessMailboxAsync(macro.MailboxId))
            return false;

        await _unitOfWork.Macros.SoftDeleteAsync(id);
        await _unitOfWork.SaveChangesAsync();
        return true;
    }

    public async Task<bool> ToggleActiveAsync(int id)
    {
        var macro = await _unitOfWork.Macros.GetByIdTrackedAsync(id);
        if (macro == null) return false;

        macro.IsActive = !macro.IsActive;
        macro.UpdatedAt = DateTime.UtcNow;
        await _unitOfWork.SaveChangesAsync();
        return true;
    }

    public async Task<MacroDto?> DuplicateAsync(int id, string? newName = null)
    {
        var source = await _unitOfWork.Macros.GetByIdAsync(id);
        if (source == null) return null;

        var clone = new Macro
        {
            Name = string.IsNullOrWhiteSpace(newName) ? $"{source.Name} (副本)" : newName,
            Description = source.Description,
            MailboxId = source.MailboxId,
            IsActive = source.IsActive,
            SortOrder = source.SortOrder,
            ActionsJson = source.ActionsJson,
            UsageCount = 0,
            LastUsedAt = null,
            CreatedAt = DateTime.UtcNow,
            UpdatedAt = DateTime.UtcNow,
        };

        await _unitOfWork.Macros.AddAsync(clone);
        await _unitOfWork.SaveChangesAsync();
        return MapToDto(clone);
    }

    public async Task<MacroPreviewResponse> PreviewAsync(int id, int conversationId)
    {
        var macro = await _unitOfWork.Macros.GetByIdAsync(id);
        if (macro == null)
        {
            return new MacroPreviewResponse { Success = false, Message = "宏不存在" };
        }

        var conversation = await _unitOfWork.Conversations.GetByIdAsync(conversationId);
        if (conversation == null)
        {
            // 尝试获取一个可用的会话ID供用户参考（在数据库层面过滤+排序+取1，避免全表加载到内存）
            var anyConversation = await _unitOfWork.Conversations.GetQueryable()
                .Where(c => c.State != Domain.Enums.ConversationState.Deleted)
                .OrderByDescending(c => c.CreatedAt)
                .FirstOrDefaultAsync();
            var suggestion = anyConversation != null
                ? $"（可用的会话ID：{anyConversation.Id}）"
                : "（当前没有可用会话，请先创建会话）";
            return new MacroPreviewResponse { Success = false, Message = $"会话 #{conversationId} 不存在 {suggestion}" };
        }

        var actions = macro.DeserializeActions(ActionJsonOptions);
        var descriptions = actions.Select(a => new MacroActionPreviewItem
        {
            Type = a.Type,
            TypeLabel = MacroConst.ActionTypes.GetValueOrDefault(NormalizeActionType(a.Type), a.Type),
            Value = a.Value,
            Description = DescribeAction(a),
        }).ToList();

        return new MacroPreviewResponse
        {
            Success = true,
            MacroId = macro.Id,
            MacroName = macro.Name,
            ConversationId = conversationId,
            ConversationSubject = conversation.Subject,
            ActionCount = actions.Count,
            Actions = descriptions,
        };
    }

    // ====== 垃圾箱 ======
    public async Task<(List<MacroDto> Items, int Total)> GetTrashAsync(int page, int pageSize, string? keyword = null)
    {
        var (items, total) = await _unitOfWork.Macros.GetDeletedPagedAsync(page, pageSize, keyword);
        return (items.Select(MapToDto).ToList(), total);
    }

    public async Task<int> GetTrashCountAsync()
    {
        return await _unitOfWork.Macros.CountDeletedAsync();
    }

    public async Task<bool> RestoreAsync(int id)
    {
        await _unitOfWork.Macros.RestoreAsync(id);
        await _unitOfWork.SaveChangesAsync();
        return true;
    }

    public async Task<bool> HardDeleteAsync(int id)
    {
        if (_currentUserAccessor != null && !_currentUserAccessor.IsSuperAdmin)
        {
            return false;
        }
        await _unitOfWork.Macros.HardDeleteAsync(id);
        await _unitOfWork.SaveChangesAsync();
        return true;
    }

    public async Task<int> ClearTrashAsync()
    {
        var count = await _unitOfWork.Macros.HardDeleteAllDeletedAsync();
        await _unitOfWork.SaveChangesAsync();
        return count;
    }

    public async Task<ExecuteMacroResponse> ExecuteAsync(int macroId, int conversationId, int userId)
    {
        var macro = await _unitOfWork.Macros.GetByIdTrackedAsync(macroId);
        if (macro == null || macro.DeletedAt != null)
        {
            return new ExecuteMacroResponse { Success = false, Message = "宏不存在" };
        }

        var conversation = await _unitOfWork.Conversations.GetByIdAsync(conversationId);
        if (conversation == null)
        {
            return new ExecuteMacroResponse { Success = false, Message = "会话不存在" };
        }

        if (!macro.CanExecuteFor(conversation.MailboxId))
        {
            return new ExecuteMacroResponse { Success = false, Message = "宏未启用或当前邮箱不可用" };
        }

        if (!macro.ValidateActions(out var invalidActions))
        {
            return new ExecuteMacroResponse
            {
                Success = false,
                Message = $"宏包含无效动作：{string.Join(", ", invalidActions)}"
            };
        }

        var actions = macro.DeserializeActions(ActionJsonOptions);
        var executed = new List<string>();
        var actionsExecuted = 0;
        string? failureMessage = null;

        // 所有宏动作在同一事务内执行，确保原子性：任何一个动作失败则整体回滚
        // 同时进入抑制作用域：add_note/send_reply 动作会调用 ThreadService 的 AddNote/AddReply，
        // 若不禁用其内层事务，会与当前事务形成嵌套导致 InvalidOperationException（TD-01）。
        using (var transaction = await _unitOfWork.BeginTransactionAsync())
        using (_threadService.BeginSuppressedScope())
        {
            try
            {
                // 预加载会话一次（追踪查询），避免每个动作重复查询（N+1 → 1）
                var trackedConversation = await _unitOfWork.Conversations.GetByIdForUpdateAsync(conversationId);
                if (trackedConversation == null)
                {
                    return new ExecuteMacroResponse
                    {
                        Success = false,
                        Message = "会话不存在",
                        ActionsExecuted = 0,
                        ExecutedActions = new List<string>(),
                    };
                }

                foreach (var action in actions)
                {
                    var (success, actionName, error) = await ExecuteActionAsync(action, trackedConversation, userId);
                    if (success)
                    {
                        actionsExecuted++;
                        executed.Add(actionName);
                    }
                    else
                    {
                        // 任何一个动作失败，整个宏执行回滚
                        failureMessage = string.IsNullOrEmpty(error) ? actionName : $"{actionName}：{error}";
                        await transaction.RollbackAsync();
                        break;
                    }
                }

                if (failureMessage == null)
                {
                    // 全部动作成功：记录使用次数并提交事务
                    macro.RecordUsage();
                    await _unitOfWork.SaveChangesAsync();
                    await transaction.CommitAsync();
                }
            }
            catch (Exception ex)
            {
                await transaction.SafeRollbackAsync();
                failureMessage = $"宏执行异常：{DescribeException(ex)}";
                _logger.LogError(ex, "宏执行异常: MacroId={MacroId}, ConversationId={ConversationId}",
                    macroId, conversationId);
            }
        }

        // 事务外记录执行日志（无副作用的操作）
        if (failureMessage != null)
        {
            // 回滚后清理变更跟踪器，避免将已回滚的变更重新写入数据库
            _unitOfWork.ClearChanges();
            actionsExecuted = 0;
            executed.Clear();
        }

        try
        {
            var log = new MacroExecutionLog
            {
                MacroId = macroId,
                ConversationId = conversationId,
                ExecutedByUserId = userId,
                Status = failureMessage == null ? "success" : "failed",
                ActionsExecuted = actionsExecuted,
                ErrorMessage = failureMessage,
            };
            await _unitOfWork.MacroExecutionLogs.AddAsync(log);
            await _unitOfWork.SaveChangesAsync();
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "记录宏执行日志失败: MacroId={MacroId}, ConversationId={ConversationId}",
                macroId, conversationId);
        }

        if (failureMessage != null)
        {
            return new ExecuteMacroResponse
            {
                Success = false,
                Message = $"宏执行失败，已回滚：{failureMessage}",
                ActionsExecuted = 0,
                ExecutedActions = new List<string>(),
            };
        }

        return new ExecuteMacroResponse
        {
            Success = true,
            Message = $"已执行 {actionsExecuted} 个动作",
            ActionsExecuted = actionsExecuted,
            ExecutedActions = executed,
        };
    }

    /// <summary>
    /// 由工作流引擎调用的宏执行入口。
    /// 关键约束：
    /// 1. **不管理事务**：由外层工作流开启/提交/回滚事务，避免嵌套事务造成 Savepoints 异常；
    /// 2. **不调用 ClearChanges**：外层工作流还追踪着会话/工作流实体，ClearChanges 会把这些实体脱轨导致后续保存失败；
    /// 3. **抑制嵌套工作流**：宏里的 add_note/send_reply 动作会触发 note_added/agent_replied 工作流，
    ///    若不抑制则可能形成递归（宏→note_added 工作流→同宏→…）或在失败分支执行 ClearChanges 破坏外层状态；
    /// 4. **复用外层追踪的会话**：调用方应传入 conversation 实例（与 ExecuteWorkflowsForConversationAsync 中同一个 conversation 引用），
    ///    保证宏对话属性的修改立即对外层可见；若为 null 则退化为根据 id 查询。
    /// </summary>
    public async Task<ExecuteMacroResponse> ExecuteForWorkflowAsync(int macroId, int conversationId)
    {
        var macro = await _unitOfWork.Macros.GetByIdTrackedAsync(macroId);
        if (macro == null || macro.DeletedAt != null)
        {
            return new ExecuteMacroResponse { Success = false, Message = "宏不存在" };
        }

        // 复用外层已追踪的会话（如果 DbContext 已经追踪过 conversationId，优先返回追踪实例），
        // 这样宏修改的 Status/Priority/Assignee 等属性对同 DbContext 下的外层工作流立即可见。
        var conversation = await _unitOfWork.Conversations.GetByIdForUpdateAsync(conversationId)
                           ?? await _unitOfWork.Conversations.GetByIdTrackedAsync(conversationId);
        if (conversation == null)
        {
            return new ExecuteMacroResponse { Success = false, Message = "会话不存在" };
        }

        if (!macro.CanExecuteFor(conversation.MailboxId))
        {
            return new ExecuteMacroResponse { Success = false, Message = "宏未启用或当前邮箱不可用" };
        }

        if (!macro.ValidateActions(out var invalidActions))
        {
            return new ExecuteMacroResponse
            {
                Success = false,
                Message = $"宏包含无效动作：{string.Join(", ", invalidActions)}"
            };
        }

        var actions = macro.DeserializeActions(ActionJsonOptions);
        var executed = new List<string>();
        var actionsExecuted = 0;
        string? failureMessage = null;

        // 进入抑制作用域：后续 ThreadService 的 AddReply/AddNote 不会再开启自己的事务，也不会触发嵌套工作流。
        using (_threadService.BeginSuppressedScope())
        {
            try
            {
                foreach (var action in actions)
                {
                    var (success, actionName, error) = await ExecuteActionAsync(action, conversation, 0);
                    if (success)
                    {
                        actionsExecuted++;
                        executed.Add(actionName);
                    }
                    else
                    {
                        failureMessage = string.IsNullOrEmpty(error) ? actionName : $"{actionName}：{error}";
                        // 注意：此处 **不** Rollback，也不 ClearChanges。
                        // 回滚统一交给外层 ExecuteWorkflowsForConversationAsync 的 using (transaction) 统一处理。
                        break;
                    }
                }

                if (failureMessage == null)
                {
                    macro.RecordUsage();
                    // 执行 SaveChanges 但不 Commit：事务仍由外层工作流控制。
                    // 失败时外层会 Rollback 整个工作流（包括宏的所有改动）。
                    await _unitOfWork.SaveChangesAsync();
                }
            }
            catch (Exception ex)
            {
                failureMessage = $"宏执行异常：{DescribeException(ex)}";
                _logger.LogError(ex, "宏执行异常: MacroId={MacroId}, ConversationId={ConversationId}",
                    macroId, conversationId);
            }
        }

        if (failureMessage != null)
        {
            return new ExecuteMacroResponse
            {
                Success = false,
                Message = failureMessage,
                ActionsExecuted = 0,
                ExecutedActions = new List<string>(),
            };
        }

        return new ExecuteMacroResponse
        {
            Success = true,
            Message = $"已执行 {actionsExecuted} 个动作",
            ActionsExecuted = actionsExecuted,
            ExecutedActions = executed,
        };
    }

    private async Task<(bool Success, string ActionName, string? Error)> ExecuteActionAsync(MacroAction action, Conversation conversation, int userId)
    {
        // 兼容旧命名（数据库中可能存储了旧命名的宏），统一映射到新命名再查标签
        var canonicalType = NormalizeActionType(action.Type);
        var actionName = MacroConst.ActionTypes.GetValueOrDefault(canonicalType, action.Type);
        var conversationId = conversation.Id;

        try
        {
            switch (canonicalType)
            {
                case "set_status":
                    {
                        var status = GetValueInt(action.Value);
                        conversation.Status = (ConversationStatus)status;
                        conversation.UpdatedAt = DateTime.UtcNow;
                        await _folderService.UpdateConversationFolderAsync(conversation);
                        await _unitOfWork.SaveChangesAsync();
                        break;
                    }
                case "assign_user":
                    {
                        var assigneeId = GetValueInt(action.Value);
                        conversation.AssignTo(assigneeId);
                        await _folderService.UpdateConversationFolderAsync(conversation);
                        await _unitOfWork.SaveChangesAsync();
                        break;
                    }
                case "set_priority":
                    {
                        var priority = GetValueInt(action.Value);
                        conversation.Priority = (ConversationPriority)priority;
                        conversation.UpdatedAt = DateTime.UtcNow;
                        await _unitOfWork.SaveChangesAsync();
                        break;
                    }
                case "add_tag":
                    {
                        var tagName = GetValueString(action.Value);
                        if (string.IsNullOrWhiteSpace(tagName))
                        {
                            return (false, actionName, "标签名为空");
                        }
                        if (conversation.MailboxId <= 0)
                        {
                            return (false, actionName, "会话未关联邮箱");
                        }

                        var tag = await _unitOfWork.Tags.GetByNameAsync(tagName, conversation.MailboxId);
                        if (tag == null)
                        {
                            tag = new Tag { MailboxId = conversation.MailboxId, Name = tagName, Color = "#3b82f6" };
                            await _unitOfWork.Tags.AddAsync(tag);
                        }

                        var existing = (await _unitOfWork.Repository<ConversationTag>().FindAsync(
                            ct => ct.ConversationId == conversationId && ct.TagId == tag.Id)).FirstOrDefault();
                        if (existing == null)
                        {
                            await _unitOfWork.Repository<ConversationTag>().AddAsync(new ConversationTag
                            {
                                ConversationId = conversationId,
                                TagId = tag.Id,
                            });
                        }
                        break;
                    }
                case "remove_tag":
                    {
                        var removeTagName = GetValueString(action.Value);
                        if (string.IsNullOrWhiteSpace(removeTagName))
                        {
                            return (false, actionName, "标签名为空");
                        }
                        if (conversation.MailboxId <= 0)
                        {
                            return (false, actionName, "会话未关联邮箱");
                        }

                        var tag = await _unitOfWork.Tags.GetByNameAsync(removeTagName, conversation.MailboxId);
                        if (tag != null)
                        {
                            var existing = (await _unitOfWork.Repository<ConversationTag>().FindAsync(
                                ct => ct.ConversationId == conversationId && ct.TagId == tag.Id)).FirstOrDefault();
                            if (existing != null)
                            {
                                await _unitOfWork.Repository<ConversationTag>().DeleteAsync(existing);
                            }
                        }
                        break;
                    }
                case "add_note":
                    {
                        var body = GetValueString(action.Value);
                        if (string.IsNullOrWhiteSpace(body))
                        {
                            return (false, actionName, "备注内容为空");
                        }
                        await _conversationService.AddNoteAsync(conversationId, new AddNoteRequest { Body = body }, userId);
                        break;
                    }
                case "send_reply":
                    {
                        var body = GetValueString(action.Value);
                        if (string.IsNullOrWhiteSpace(body))
                        {
                            return (false, actionName, "回复内容为空");
                        }
                        var customerEmail = conversation.Customer?.Emails?.FirstOrDefault()?.EmailAddress
                            ?? conversation.CustomerEmail;
                        if (string.IsNullOrWhiteSpace(customerEmail))
                        {
                            return (false, actionName, "客户邮箱缺失");
                        }
                        await _conversationService.AddReplyAsync(new CreateThreadRequest
                        {
                            ConversationId = conversationId,
                            Body = body,
                            From = conversation.Mailbox?.Email ?? "noreply@example.com",
                            To = customerEmail,
                            Subject = conversation.Subject,
                        }, userId);
                        break;
                    }
                default:
                    return (false, actionName, $"不支持的动作类型：{canonicalType}");
            }

            return (true, actionName, null);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "宏动作执行失败: Action={ActionType}, ConversationId={ConversationId}", canonicalType, conversationId);
            return (false, actionName, DescribeException(ex));
        }
    }

    /// <summary>
    /// 生成动作的可读化描述（用于预览和 UI 展示）
    /// </summary>
    private static string DescribeAction(MacroAction action)
    {
        return action.Type switch
        {
            "status" or "set_status" => $"将状态改为 {StatusName(GetValueInt(action.Value))}",
            "assign" or "assign_user" => $"分配给客服 #{GetValueString(action.Value)}",
            "priority" or "set_priority" => $"优先级改为 {PriorityName(GetValueInt(action.Value))}",
            "add_tag" => $"添加标签「{GetValueString(action.Value)}」",
            "remove_tag" => $"移除标签「{GetValueString(action.Value)}」",
            "add_note" => $"添加内部备注（{GetValueString(action.Value).Length} 字符）",
            "reply" or "send_reply" => $"发送回复（{GetValueString(action.Value).Length} 字符）",
            _ => $"{action.Type}: {GetValueString(action.Value)}",
        };
    }

    private static string DescribeAction(MacroActionDto action)
    {
        return action.Type switch
        {
            "status" or "set_status" => $"将状态改为 {StatusName(GetValueInt(action.Value))}",
            "assign" or "assign_user" => $"分配给客服 #{GetValueString(action.Value)}",
            "priority" or "set_priority" => $"优先级改为 {PriorityName(GetValueInt(action.Value))}",
            "add_tag" => $"添加标签「{GetValueString(action.Value)}」",
            "remove_tag" => $"移除标签「{GetValueString(action.Value)}」",
            "add_note" => $"添加内部备注（{GetValueString(action.Value).Length} 字符）",
            "reply" or "send_reply" => $"发送回复（{GetValueString(action.Value).Length} 字符）",
            _ => $"{action.Type}: {GetValueString(action.Value)}",
        };
    }

    /// <summary>
    /// 将旧动作类型命名归一化为新命名（向后兼容数据库中已存储的旧命名宏）。
    /// 新命名与 WorkflowService 保持一致：动词+宾语的长形式。
    /// </summary>
    private static string NormalizeActionType(string type) => type switch
    {
        "status" => "set_status",
        "assign" => "assign_user",
        "priority" => "set_priority",
        "reply" => "send_reply",
        _ => type,
    };

    /// <summary>
    /// 提取异常链中最内层的真实错误文本（如 Microsoft.Data.Sqlite.SqliteException 的
    /// "SQLite Error 19: UNIQUE constraint failed: ..."），避免只暴露 EF Core 的
    /// "An error occurred while saving the entity changes..." 泛化外壳（T000059）。
    /// </summary>
    private static string DescribeException(Exception ex)
    {
        var current = ex;
        while (current.InnerException != null)
        {
            current = current.InnerException;
        }
        return string.IsNullOrWhiteSpace(current.Message) ? ex.Message : current.Message;
    }

    /// <summary>
    /// 将 MacroActionDto.Value（可能为 JsonElement 或基础类型）统一转换为字符串。
    /// </summary>
    private static string GetValueString(object? value)
    {
        if (value is null) return string.Empty;
        if (value is System.Text.Json.JsonElement el)
        {
            return el.ValueKind switch
            {
                System.Text.Json.JsonValueKind.String => el.GetString() ?? string.Empty,
                System.Text.Json.JsonValueKind.Number => el.GetRawText(),
                System.Text.Json.JsonValueKind.True => "true",
                System.Text.Json.JsonValueKind.False => "false",
                System.Text.Json.JsonValueKind.Null => string.Empty,
                _ => el.GetRawText(),
            };
        }
        return value.ToString() ?? string.Empty;
    }

    /// <summary>
    /// 将 MacroActionDto.Value（可能为 JsonElement 或基础类型）统一转换为整数。
    /// </summary>
    private static int GetValueInt(object? value)
    {
        var s = GetValueString(value);
        return int.TryParse(s, out var v) ? v : 0;
    }

    private static string StatusName(int v) => v switch
    {
        1 => "活跃",
        2 => "待处理",
        3 => "已关闭",
        4 => "垃圾邮件",
        _ => v.ToString(),
    };

    private static string PriorityName(int v) => v switch
    {
        0 => "低",
        1 => "普通",
        2 => "高",
        3 => "紧急",
        _ => v.ToString(),
    };

    public async Task<(List<MacroExecutionLogDto> Items, int Total)> GetExecutionLogsAsync(MacroExecutionLogQuery query)
    {
        var (items, total) = await _unitOfWork.MacroExecutionLogs.GetPagedAsync(
            query.Page,
            query.PageSize,
            query.MacroId,
            query.ConversationId);
        return (items.Select(MapLogToDto).ToList(), total);
    }

    private MacroDto MapToDto(Macro macro)
    {
        var actions = new List<MacroActionDto>();
        var hasParseError = false;
        try
        {
            if (!string.IsNullOrWhiteSpace(macro.ActionsJson))
            {
                actions = JsonSerializer.Deserialize<List<MacroActionDto>>(macro.ActionsJson, ActionJsonOptions) ?? new();
            }
        }
        catch (Exception ex)
        {
            hasParseError = true;
            _logger.LogError(ex, "反序列化宏动作 JSON 失败: MacroId={MacroId}, ActionsJson={ActionsJson}",
                macro.Id, macro.ActionsJson);
        }

        return new MacroDto
        {
            Id = macro.Id,
            Name = macro.Name,
            Description = macro.Description,
            MailboxId = macro.MailboxId,
            MailboxName = macro.Mailbox?.Name,
            IsActive = macro.IsActive,
            SortOrder = macro.SortOrder,
            UsageCount = macro.UsageCount,
            LastUsedAt = macro.LastUsedAt,
            CreatedAt = macro.CreatedAt,
            UpdatedAt = macro.UpdatedAt,
            DeletedAt = macro.DeletedAt,
            Actions = actions,
            HasActionsParseError = hasParseError,
            ActionsDescription = string.Join(" → ", actions.Select(DescribeAction)),
        };
    }

    private static MacroExecutionLogDto MapLogToDto(MacroExecutionLog log)
    {
        return new MacroExecutionLogDto
        {
            Id = log.Id,
            MacroId = log.MacroId,
            MacroName = log.Macro?.Name ?? string.Empty,
            ConversationId = log.ConversationId,
            ConversationSubject = log.Conversation?.Subject,
            ExecutedByUserId = log.ExecutedByUserId,
            ExecutedByUserName = log.ExecutedByUser?.GetFullName(),
            Status = log.Status,
            ActionsExecuted = log.ActionsExecuted,
            ErrorMessage = log.ErrorMessage,
            ExecutedAt = log.ExecutedAt,
        };
    }
}

public static class MacroConst
{
    public static readonly Dictionary<string, string> ActionTypes = new()
    {
        { "set_status", "更改状态" },
        { "assign_user", "分配客服" },
        { "set_priority", "设置优先级" },
        { "add_tag", "添加标签" },
        { "remove_tag", "移除标签" },
        { "add_note", "添加备注" },
        { "send_reply", "发送回复" },
    };

    public static readonly Dictionary<string, string> StatusNames = new()
    {
        { "1", "活跃" },
        { "2", "待处理" },
        { "3", "已关闭" },
        { "4", "垃圾邮件" },
    };

    public static readonly Dictionary<string, string> PriorityNames = new()
    {
        { "0", "低" },
        { "1", "普通" },
        { "2", "高" },
        { "3", "紧急" },
    };
}
