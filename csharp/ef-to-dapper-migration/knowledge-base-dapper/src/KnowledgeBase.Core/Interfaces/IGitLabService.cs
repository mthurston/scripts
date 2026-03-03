using KnowledgeBase.Core.Models.GitLab;

namespace KnowledgeBase.Core.Interfaces;

public interface IGitLabService
{
    Task<IEnumerable<GitLabIssueResponse>> FetchAllIssuesAsync(CancellationToken ct = default);
    Task<IEnumerable<GitLabLabelResponse>> FetchAllLabelsAsync(CancellationToken ct = default);
}
