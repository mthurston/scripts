using Dapper;
using KnowledgeBase.Core.Domain;
using KnowledgeBase.Core.DTOs;
using KnowledgeBase.Core.Interfaces;
using KnowledgeBase.Data.SqlQueries;

namespace KnowledgeBase.Data.Repositories;

public class IssueRepository : IIssueRepository
{
    private readonly IDbConnectionFactory _connectionFactory;

    public IssueRepository(IDbConnectionFactory connectionFactory)
        => _connectionFactory = connectionFactory;

    public async Task<IEnumerable<IssueSummaryDto>> GetAllAsync(
        string? labelFilter,
        string? stateFilter,
        string? authorFilter,
        CancellationToken ct = default)
    {
        using var connection = _connectionFactory.CreateConnection();

        // The query returns one row per label per issue. We collapse with a dictionary.
        var issueLookup = new Dictionary<int, IssueSummaryDto>();

        await connection.QueryAsync<dynamic>(
            IssueQueries.GetAll,
            param: new { State = stateFilter, Author = authorFilter, Label = labelFilter });

        // Re-run with multi-mapping to split issue + author + label columns
        var rows = await connection.QueryAsync<dynamic>(
            IssueQueries.GetAll,
            new { State = stateFilter, Author = authorFilter, Label = labelFilter });

        foreach (var r in rows)
        {
            int issueId = (int)r.Id;
            if (!issueLookup.TryGetValue(issueId, out var dto))
            {
                dto = new IssueSummaryDto
                {
                    Id = issueId,
                    GitLabId = (int)r.GitLabId,
                    Title = (string)r.Title,
                    State = (string)r.State,
                    WebUrl = (string)r.WebUrl,
                    KbCategory = (string?)r.KbCategory,
                    Author = new UserDto
                    {
                        Id = (int)r.UserId,
                        Name = (string)r.UserName,
                        Username = (string)r.UserUsername
                    },
                    CreatedAt = (DateTime)r.CreatedAt
                };
                issueLookup[issueId] = dto;
            }

            if (r.LabelId is not null)
            {
                dto.Labels.Add(new LabelDto
                {
                    Id = (int)r.LabelId,
                    Name = (string)r.LabelName,
                    Color = (string)r.LabelColor,
                    Description = (string?)r.LabelDescription
                });
            }
        }

        return issueLookup.Values;
    }

    public async Task<IssueDto?> GetByIdAsync(int id, CancellationToken ct = default)
    {
        using var connection = _connectionFactory.CreateConnection();

        var rows = await connection.QueryAsync<dynamic>(IssueQueries.GetById, new { Id = id });
        var rowList = rows.ToList();

        if (rowList.Count == 0) return null;

        var first = rowList[0];
        var issue = new IssueDto
        {
            Id = (int)first.Id,
            GitLabId = (int)first.GitLabId,
            Title = (string)first.Title,
            Description = (string?)first.Description,
            State = (string)first.State,
            WebUrl = (string)first.WebUrl,
            KbCategory = (string?)first.KbCategory,
            KbNotes = (string?)first.KbNotes,
            Author = new UserDto
            {
                Id = (int)first.UserId,
                Name = (string)first.UserName,
                Username = (string)first.UserUsername
            },
            CreatedAt = (DateTime)first.CreatedAt,
            UpdatedAt = (DateTime)first.UpdatedAt
        };

        foreach (var r in rowList.Where(r => r.LabelId is not null))
        {
            issue.Labels.Add(new LabelDto
            {
                Id = (int)r.LabelId,
                Name = (string)r.LabelName,
                Color = (string)r.LabelColor,
                Description = (string?)r.LabelDescription
            });
        }

        return issue;
    }

    public async Task UpsertAsync(Issue issue, IEnumerable<string> labelNames, CancellationToken ct = default)
    {
        using var connection = _connectionFactory.CreateConnection();
        using var transaction = connection.BeginTransaction();

        try
        {
            var issueId = await connection.ExecuteScalarAsync<int>(
                IssueQueries.UpsertIssue,
                new
                {
                    issue.GitLabId,
                    issue.Title,
                    issue.Description,
                    issue.State,
                    issue.WebUrl,
                    issue.AuthorId,
                    issue.CreatedAt,
                    issue.UpdatedAt
                },
                transaction);

            // Replace label links
            await connection.ExecuteAsync(
                IssueQueries.DeleteIssueLabelLinks,
                new { IssueId = issueId },
                transaction);

            foreach (var name in labelNames)
            {
                var labelId = await connection.ExecuteScalarAsync<int?>(
                    IssueQueries.GetLabelIdByName,
                    new { Name = name },
                    transaction);

                if (labelId.HasValue)
                    await connection.ExecuteAsync(
                        IssueQueries.InsertIssueLabelLink,
                        new { IssueId = issueId, LabelId = labelId.Value },
                        transaction);
            }

            transaction.Commit();
        }
        catch
        {
            transaction.Rollback();
            throw;
        }
    }

    public async Task PatchAsync(int id, PatchIssueRequest request, CancellationToken ct = default)
    {
        using var connection = _connectionFactory.CreateConnection();
        await connection.ExecuteAsync(
            IssueQueries.PatchIssue,
            new { Id = id, request.KbCategory, request.KbNotes });
    }
}
