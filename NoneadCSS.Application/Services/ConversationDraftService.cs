using NoneadCSS.Application.Common;
using NoneadCSS.Application.DTOs.Requests;
using NoneadCSS.Application.DTOs.Responses;
using NoneadCSS.Application.Mappings;
using NoneadCSS.Domain;
using NoneadCSS.Domain.Entities;
using NoneadCSS.Domain.Enums;

namespace NoneadCSS.Application.Services;

public interface IConversationDraftService
{
    Task<ThreadResponse?> SaveDraftAsync(CreateThreadRequest request, int? userId = null);
    Task<ThreadResponse?> GetDraftAsync(int conversationId);
    Task<bool> DeleteDraftAsync(int conversationId);
    Task<List<ThreadResponse>> GetDraftsByUserIdAsync(int userId);
}

public class ConversationDraftService : IConversationDraftService
{
    private readonly IUnitOfWork _unitOfWork;

    public ConversationDraftService(IUnitOfWork unitOfWork)
    {
        _unitOfWork = unitOfWork;
    }

    public async Task<ThreadResponse?> SaveDraftAsync(CreateThreadRequest request, int? userId = null)
    {
        var conversation = await _unitOfWork.Conversations.GetByIdTrackedAsync(request.ConversationId);
        if (conversation == null)
        {
            return null;
        }

        var existingDraft = await _unitOfWork.Threads.GetDraftThreadTrackedAsync(request.ConversationId);

        ConversationThread thread;
        if (existingDraft != null)
        {
            thread = existingDraft;
            thread.Body = HtmlSanitizerHelper.Sanitize(request.Body);
            thread.Subject = conversation.Subject;
            thread.From = request.From;
            thread.To = request.To ?? conversation.CustomerEmail;
            thread.Cc = request.Cc ?? string.Empty;
            thread.Bcc = request.Bcc ?? string.Empty;
            thread.IsInternal = request.IsNote;
            thread.UpdatedAt = DateTime.UtcNow;
        }
        else
        {
            thread = new ConversationThread
            {
                ConversationId = request.ConversationId,
                UserId = userId,
                CustomerId = conversation.CustomerId,
                Type = request.IsNote ? ThreadType.Note : ThreadType.Message,
                State = Domain.Entities.ThreadState.Draft,
                Body = HtmlSanitizerHelper.Sanitize(request.Body),
                Subject = conversation.Subject,
                From = request.From,
                To = request.To ?? conversation.CustomerEmail,
                Cc = request.Cc ?? string.Empty,
                Bcc = request.Bcc ?? string.Empty,
                IsInternal = request.IsNote,
                CreatedAt = DateTime.UtcNow,
                UpdatedAt = DateTime.UtcNow
            };
            conversation.Threads.Add(thread);
        }

        if (request.AttachmentIds != null && request.AttachmentIds.Any())
        {
            var attachments = await _unitOfWork.Attachments.GetByIdsTrackedAsync(request.AttachmentIds) ?? new List<Attachment>();
            foreach (var attachment in attachments)
            {
                thread.Attachments.Add(attachment);
            }
        }

        conversation.UpdatedAt = DateTime.UtcNow;

        using (var transaction = await _unitOfWork.BeginTransactionAsync())
        {
            try
            {
                // existingDraft 通过 tracked 查询，直接 SaveChangesAsync 即可持久化修改，无需 UpdateAsync
                await _unitOfWork.SaveChangesAsync();

                if (thread.Attachments.Any())
                {
                    foreach (var attachment in thread.Attachments)
                    {
                        attachment.ThreadId = thread.Id;
                    }
                    await _unitOfWork.SaveChangesAsync();
                }

                await transaction.CommitAsync();
            }
            catch
            {
                await transaction.SafeRollbackAsync();
                throw;
            }
        }

        return thread.ToResponse();
    }

    public async Task<ThreadResponse?> GetDraftAsync(int conversationId)
    {
        var draft = await _unitOfWork.Threads.GetDraftThreadAsync(conversationId);
        return draft?.ToResponse();
    }

    public async Task<bool> DeleteDraftAsync(int conversationId)
    {
        var draft = await _unitOfWork.Threads.GetDraftThreadAsync(conversationId);
        if (draft == null)
        {
            return false;
        }

        await _unitOfWork.Threads.DeleteAsync(draft);
        await _unitOfWork.SaveChangesAsync();
        return true;
    }

    public async Task<List<ThreadResponse>> GetDraftsByUserIdAsync(int userId)
    {
        var drafts = await _unitOfWork.Threads.GetDraftsByUserIdAsync(userId);
        return drafts.Select(t => t.ToResponse()).ToList();
    }
}
