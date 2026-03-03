using KnowledgeBase.Core.Domain;
using KnowledgeBase.Core.DTOs;

namespace KnowledgeBase.Core.Interfaces;

public interface IIssueRepository
{
    Task<IEnumerable<IssueSummaryDto>> GetAllAsync(
        string? labelFilter,
        string? stateFilter,
        string? authorFilter,
        CancellationToken ct = default);

    Task<IssueDto?> GetByIdAsync(int id, CancellationToken ct = default);

    Task UpsertAsync(Issue issue, IEnumerable<string> labelNames, CancellationToken ct = default);

    Task PatchAsync(int id, PatchIssueRequest request, CancellationToken ct = default);
}
