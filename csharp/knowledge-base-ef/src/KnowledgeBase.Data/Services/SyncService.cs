using KnowledgeBase.Core.Domain;
using KnowledgeBase.Core.DTOs;
using KnowledgeBase.Core.Interfaces;

namespace KnowledgeBase.Data.Services;

public class SyncService
{
    private readonly IGitLabService _gitLab;
    private readonly IIssueRepository _issues;
    private readonly ILabelRepository _labels;
    private readonly IUserRepository _users;

    public SyncService(
        IGitLabService gitLab,
        IIssueRepository issues,
        ILabelRepository labels,
        IUserRepository users)
    {
        _gitLab = gitLab;
        _issues = issues;
        _labels = labels;
        _users = users;
    }

    public async Task<SyncResultDto> SyncAsync(CancellationToken ct = default)
    {
        try
        {
            // Sync labels first so they exist when issues reference them
            var glLabels = (await _gitLab.FetchAllLabelsAsync(ct)).ToList();
            foreach (var gl in glLabels)
                await _labels.UpsertAsync(gl.Name, gl.Color, gl.Description, ct);

            var glIssues = (await _gitLab.FetchAllIssuesAsync(ct)).ToList();

            foreach (var gl in glIssues)
            {
                // Upsert the author user
                var user = await _users.UpsertAsync(gl.Author.Id, gl.Author.Name, gl.Author.Username, ct);

                var issue = new Issue
                {
                    GitLabId = gl.Iid,
                    Title = gl.Title,
                    Description = gl.Description,
                    State = gl.State,
                    WebUrl = gl.WebUrl,
                    AuthorId = user.Id,
                    Author = user,
                    CreatedAt = gl.CreatedAt,
                    UpdatedAt = gl.UpdatedAt
                };

                await _issues.UpsertAsync(issue, gl.Labels, ct);
            }

            return new SyncResultDto
            {
                IssuesUpserted = glIssues.Count,
                LabelsUpserted = glLabels.Count,
                UsersUpserted = glIssues.Select(i => i.Author.Id).Distinct().Count(),
                SyncedAt = DateTime.UtcNow
            };
        }
        catch (Exception ex)
        {
            return new SyncResultDto
            {
                SyncedAt = DateTime.UtcNow,
                Error = ex.Message
            };
        }
    }
}
