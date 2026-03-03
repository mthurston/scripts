using KnowledgeBase.Core.DTOs;

namespace KnowledgeBase.Core.Interfaces;

public interface ICommentRepository
{
    Task<List<CommentDto>> GetThreadedByIssueIdAsync(int issueId, CancellationToken ct = default);
    Task<CommentDto> AddAsync(int issueId, CreateCommentRequest request, CancellationToken ct = default);
}
