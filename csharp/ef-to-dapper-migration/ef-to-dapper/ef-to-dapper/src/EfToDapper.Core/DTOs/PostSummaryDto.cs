namespace EfToDapper.Core.DTOs;

public class PostSummaryDto
{
    public int Id { get; set; }
    public string Title { get; set; } = string.Empty;
    public string AuthorName { get; set; } = string.Empty;
    public string Status { get; set; } = string.Empty;
    public int ViewCount { get; set; }
    public DateTime PublishedAt { get; set; }
    public int CommentCount { get; set; }
    public List<string> Tags { get; set; } = new();
}
