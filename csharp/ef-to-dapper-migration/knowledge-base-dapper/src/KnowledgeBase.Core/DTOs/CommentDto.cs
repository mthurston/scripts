namespace KnowledgeBase.Core.DTOs;

public class CommentDto
{
    public int Id { get; set; }
    public int IssueId { get; set; }
    public UserDto Author { get; set; } = null!;
    public string Body { get; set; } = string.Empty;
    public int? ParentCommentId { get; set; }
    public List<CommentDto> Replies { get; set; } = [];
    public DateTime CreatedAt { get; set; }
    public DateTime UpdatedAt { get; set; }
}
