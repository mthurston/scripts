namespace KnowledgeBase.Core.Domain;

public class Issue
{
    public int Id { get; set; }
    public int GitLabId { get; set; }
    public string Title { get; set; } = string.Empty;
    public string? Description { get; set; }
    public string State { get; set; } = string.Empty;
    public string WebUrl { get; set; } = string.Empty;

    public string? KbCategory { get; set; }
    public string? KbNotes { get; set; }

    public int AuthorId { get; set; }
    public User Author { get; set; } = null!;

    public ICollection<IssueLabel> IssueLabels { get; set; } = [];
    public ICollection<Comment> Comments { get; set; } = [];

    public DateTime CreatedAt { get; set; }
    public DateTime UpdatedAt { get; set; }
}
