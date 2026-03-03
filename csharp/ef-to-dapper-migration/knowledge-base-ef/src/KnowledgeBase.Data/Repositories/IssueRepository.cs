using KnowledgeBase.Core.Domain;
using KnowledgeBase.Core.DTOs;
using KnowledgeBase.Core.Interfaces;
using KnowledgeBase.Data.Context;
using Microsoft.EntityFrameworkCore;

namespace KnowledgeBase.Data.Repositories;

public class IssueRepository : IIssueRepository
{
    private readonly KnowledgeBaseDbContext _context;

    public IssueRepository(KnowledgeBaseDbContext context) => _context = context;

    public async Task<IEnumerable<IssueSummaryDto>> GetAllAsync(
        string? labelFilter,
        string? stateFilter,
        string? authorFilter,
        CancellationToken ct = default)
    {
        var query = _context.Issues
            .Include(i => i.Author)
            .Include(i => i.IssueLabels).ThenInclude(il => il.Label)
            .AsQueryable();

        if (!string.IsNullOrWhiteSpace(stateFilter))
            query = query.Where(i => i.State == stateFilter);

        if (!string.IsNullOrWhiteSpace(authorFilter))
            query = query.Where(i => i.Author.Username == authorFilter);

        if (!string.IsNullOrWhiteSpace(labelFilter))
            query = query.Where(i => i.IssueLabels.Any(il => il.Label.Name == labelFilter));

        return await query
            .OrderByDescending(i => i.CreatedAt)
            .Select(i => new IssueSummaryDto
            {
                Id = i.Id,
                GitLabId = i.GitLabId,
                Title = i.Title,
                State = i.State,
                WebUrl = i.WebUrl,
                KbCategory = i.KbCategory,
                Author = new UserDto { Id = i.Author.Id, Name = i.Author.Name, Username = i.Author.Username },
                Labels = i.IssueLabels.Select(il => new LabelDto
                {
                    Id = il.Label.Id,
                    Name = il.Label.Name,
                    Color = il.Label.Color,
                    Description = il.Label.Description
                }).ToList(),
                CreatedAt = i.CreatedAt
            })
            .ToListAsync(ct);
    }

    public async Task<IssueDto?> GetByIdAsync(int id, CancellationToken ct = default)
    {
        var issue = await _context.Issues
            .Include(i => i.Author)
            .Include(i => i.IssueLabels).ThenInclude(il => il.Label)
            .FirstOrDefaultAsync(i => i.Id == id, ct);

        if (issue is null) return null;

        // Load all comments flat, then build the tree in C#
        var allComments = await _context.Comments
            .Include(c => c.Author)
            .Where(c => c.IssueId == id)
            .OrderBy(c => c.CreatedAt)
            .ToListAsync(ct);

        return new IssueDto
        {
            Id = issue.Id,
            GitLabId = issue.GitLabId,
            Title = issue.Title,
            Description = issue.Description,
            State = issue.State,
            WebUrl = issue.WebUrl,
            KbCategory = issue.KbCategory,
            KbNotes = issue.KbNotes,
            Author = new UserDto { Id = issue.Author.Id, Name = issue.Author.Name, Username = issue.Author.Username },
            Labels = issue.IssueLabels.Select(il => new LabelDto
            {
                Id = il.Label.Id,
                Name = il.Label.Name,
                Color = il.Label.Color,
                Description = il.Label.Description
            }).ToList(),
            Comments = BuildCommentTree(allComments),
            CreatedAt = issue.CreatedAt,
            UpdatedAt = issue.UpdatedAt
        };
    }

    public async Task UpsertAsync(Issue issue, IEnumerable<string> labelNames, CancellationToken ct = default)
    {
        var existing = await _context.Issues
            .Include(i => i.IssueLabels)
            .FirstOrDefaultAsync(i => i.GitLabId == issue.GitLabId, ct);

        if (existing is null)
        {
            _context.Issues.Add(issue);
            await _context.SaveChangesAsync(ct);

            // Wire up labels after save so we have an Id
            await SyncLabelsAsync(issue.Id, labelNames, ct);
        }
        else
        {
            existing.Title = issue.Title;
            existing.Description = issue.Description;
            existing.State = issue.State;
            existing.WebUrl = issue.WebUrl;
            existing.AuthorId = issue.AuthorId;
            existing.UpdatedAt = issue.UpdatedAt;
            await _context.SaveChangesAsync(ct);
            await SyncLabelsAsync(existing.Id, labelNames, ct);
        }
    }

    public async Task PatchAsync(int id, PatchIssueRequest request, CancellationToken ct = default)
    {
        var issue = await _context.Issues.FindAsync([id], ct);
        if (issue is null) return;

        if (request.KbCategory is not null) issue.KbCategory = request.KbCategory;
        if (request.KbNotes is not null) issue.KbNotes = request.KbNotes;

        await _context.SaveChangesAsync(ct);
    }

    private async Task SyncLabelsAsync(int issueId, IEnumerable<string> labelNames, CancellationToken ct)
    {
        var existingLinks = await _context.IssueLabels
            .Where(il => il.IssueId == issueId)
            .ToListAsync(ct);
        _context.IssueLabels.RemoveRange(existingLinks);

        foreach (var name in labelNames)
        {
            var label = await _context.Labels.FirstOrDefaultAsync(l => l.Name == name, ct);
            if (label is not null)
                _context.IssueLabels.Add(new IssueLabel { IssueId = issueId, LabelId = label.Id });
        }

        await _context.SaveChangesAsync(ct);
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
