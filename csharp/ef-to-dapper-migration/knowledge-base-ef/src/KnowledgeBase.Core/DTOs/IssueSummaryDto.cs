namespace KnowledgeBase.Core.DTOs;

public class IssueSummaryDto
{
    public int Id { get; set; }
    public int GitLabId { get; set; }
    public string Title { get; set; } = string.Empty;
    public string State { get; set; } = string.Empty;
    public string WebUrl { get; set; } = string.Empty;
    public string? KbCategory { get; set; }
    public UserDto Author { get; set; } = null!;
    public List<LabelDto> Labels { get; set; } = [];
    public DateTime CreatedAt { get; set; }
}
