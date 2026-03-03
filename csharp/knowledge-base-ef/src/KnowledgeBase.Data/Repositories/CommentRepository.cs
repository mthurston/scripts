using KnowledgeBase.Core.Domain;
using KnowledgeBase.Core.DTOs;
using KnowledgeBase.Core.Interfaces;
using KnowledgeBase.Data.Context;
using Microsoft.EntityFrameworkCore;

namespace KnowledgeBase.Data.Repositories;

public class CommentRepository : ICommentRepository
{
    private readonly KnowledgeBaseDbContext _context;

    public CommentRepository(KnowledgeBaseDbContext context) => _context = context;

    public async Task<List<CommentDto>> GetThreadedByIssueIdAsync(int issueId, CancellationToken ct = default)
    {
        // Load all comments flat in a single query, then build the tree in C#.
        // EF Core does not auto-recurse navigation properties for self-referencing types,
        // so we use this dictionary-based O(n) tree-building algorithm instead.
        var allComments = await _context.Comments
            .Include(c => c.Author)
            .Where(c => c.IssueId == issueId)
            .OrderBy(c => c.CreatedAt)
            .ToListAsync(ct);

        return BuildCommentTree(allComments);
    }

    public async Task<CommentDto> AddAsync(int issueId, CreateCommentRequest request, CancellationToken ct = default)
    {
        var comment = new Comment
        {
            IssueId = issueId,
            AuthorId = request.AuthorId,
            Body = request.Body,
            ParentCommentId = request.ParentCommentId,
            CreatedAt = DateTime.UtcNow,
            UpdatedAt = DateTime.UtcNow
        };

        _context.Comments.Add(comment);
        await _context.SaveChangesAsync(ct);

        await _context.Entry(comment).Reference(c => c.Author).LoadAsync(ct);

        return new CommentDto
        {
            Id = comment.Id,
            IssueId = comment.IssueId,
            Body = comment.Body,
            ParentCommentId = comment.ParentCommentId,
            Author = new UserDto { Id = comment.Author.Id, Name = comment.Author.Name, Username = comment.Author.Username },
            CreatedAt = comment.CreatedAt,
            UpdatedAt = comment.UpdatedAt
        };
    }

    private static List<CommentDto> BuildCommentTree(List<Comment> comments)
    {
        var lookup = comments.ToDictionary(c => c.Id, c => new CommentDto
        {
            Id = c.Id,
            IssueId = c.IssueId,
            Body = c.Body,
            ParentCommentId = c.ParentCommentId,
            Author = new UserDto { Id = c.Author.Id, Name = c.Author.Name, Username = c.Author.Username },
            CreatedAt = c.CreatedAt,
            UpdatedAt = c.UpdatedAt
        });

        var roots = new List<CommentDto>();
        foreach (var dto in lookup.Values)
        {
            if (dto.ParentCommentId is null)
                roots.Add(dto);
            else if (lookup.TryGetValue(dto.ParentCommentId.Value, out var parent))
                parent.Replies.Add(dto);
        }

        return roots;
    }
}
