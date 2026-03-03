namespace KnowledgeBase.Core.DTOs;

public class CreateCommentRequest
{
    public string Body { get; set; } = string.Empty;
    public int? ParentCommentId { get; set; }
    public int AuthorId { get; set; }
}
