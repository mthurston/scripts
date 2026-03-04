namespace EfToDapper.Core.DTOs;

public class PostDetailDto
{
    public int Id { get; set; }
    public string Title { get; set; } = string.Empty;
    public string Body { get; set; } = string.Empty;
    public string AuthorName { get; set; } = string.Empty;
    public string AuthorEmail { get; set; } = string.Empty;
    public string Status { get; set; } = string.Empty;
    public int ViewCount { get; set; }
    public DateTime PublishedAt { get; set; }
    public List<CommentDto> Comments { get; set; } = new();
    public List<string> Tags { get; set; } = new();
}
