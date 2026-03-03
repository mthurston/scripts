using Dapper;
using KnowledgeBase.Core.DTOs;
using KnowledgeBase.Core.Interfaces;
using KnowledgeBase.Data.SqlQueries;

namespace KnowledgeBase.Data.Repositories;

public class CommentRepository : ICommentRepository
{
    private readonly IDbConnectionFactory _connectionFactory;

    public CommentRepository(IDbConnectionFactory connectionFactory)
        => _connectionFactory = connectionFactory;

    public async Task<List<CommentDto>> GetThreadedByIssueIdAsync(int issueId, CancellationToken ct = default)
    {
        using var connection = _connectionFactory.CreateConnection();

        // The recursive CTE returns the entire comment tree in one round-trip.
        // Each row includes the flattened author columns (AuthorId, AuthorName, AuthorUsername).
        // We build the in-memory tree from the flat result using a dictionary lookup.
        var rows = await connection.QueryAsync<dynamic>(
            CommentQueries.GetThreadedByIssueId,
            new { IssueId = issueId });

        var flatList = rows.Select(r => new CommentDto
        {
            Id = (int)r.Id,
            IssueId = (int)r.IssueId,
            Body = (string)r.Body,
            ParentCommentId = (int?)r.ParentCommentId,
            Author = new UserDto
            {
                Id = (int)r.AuthorId,
                Name = (string)r.AuthorName,
                Username = (string)r.AuthorUsername
            },
            CreatedAt = (DateTime)r.CreatedAt,
            UpdatedAt = (DateTime)r.UpdatedAt
        }).ToList();

        return BuildCommentTree(flatList);
    }

    public async Task<CommentDto> AddAsync(int issueId, CreateCommentRequest request, CancellationToken ct = default)
    {
        using var connection = _connectionFactory.CreateConnection();
        var now = DateTime.UtcNow;

        var newId = await connection.ExecuteScalarAsync<int>(
            CommentQueries.InsertComment,
            new
            {
                IssueId = issueId,
                AuthorId = request.AuthorId,
                Body = request.Body,
                ParentCommentId = request.ParentCommentId,
                CreatedAt = now,
                UpdatedAt = now
            });

        var authorRow = await connection.QuerySingleAsync<dynamic>(
            CommentQueries.GetCommentAuthor,
            new { CommentId = newId });

        return new CommentDto
        {
            Id = newId,
            IssueId = issueId,
            Body = request.Body,
            ParentCommentId = request.ParentCommentId,
            Author = new UserDto
            {
                Id = (int)authorRow.Id,
                Name = (string)authorRow.Name,
                Username = (string)authorRow.Username
            },
            CreatedAt = now,
            UpdatedAt = now
        };
    }

    // Converts a flat list (depth-ordered) into a nested tree.
    // Dictionary lookup keeps the algorithm O(n) regardless of tree depth.
    private static List<CommentDto> BuildCommentTree(List<CommentDto> flatList)
    {
        var lookup = flatList.ToDictionary(c => c.Id);
        var roots = new List<CommentDto>();

        foreach (var comment in flatList)
        {
            if (comment.ParentCommentId is null)
                roots.Add(comment);
            else if (lookup.TryGetValue(comment.ParentCommentId.Value, out var parent))
                parent.Replies.Add(comment);
        }

        return roots;
    }
}
