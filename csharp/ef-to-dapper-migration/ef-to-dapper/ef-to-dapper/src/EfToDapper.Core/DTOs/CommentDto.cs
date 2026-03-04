namespace EfToDapper.Core.DTOs;

public class CommentDto
{
    public int Id { get; set; }
    public string AuthorName { get; set; } = string.Empty;
    public string Body { get; set; } = string.Empty;
    public DateTime PostedAt { get; set; }
}
