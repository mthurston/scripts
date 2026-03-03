namespace KnowledgeBase.Core.DTOs;

public class IssueDto
{
    public int Id { get; set; }
    public int GitLabId { get; set; }
    public string Title { get; set; } = string.Empty;
    public string? Description { get; set; }
    public string State { get; set; } = string.Empty;
    public string WebUrl { get; set; } = string.Empty;
    public string? KbCategory { get; set; }
    public string? KbNotes { get; set; }
    public UserDto Author { get; set; } = null!;
    public List<LabelDto> Labels { get; set; } = [];
    public List<CommentDto> Comments { get; set; } = [];
    public DateTime CreatedAt { get; set; }
    public DateTime UpdatedAt { get; set; }
}
