namespace EfToDapper.Core.Domain;

public class Comment
{
    public int Id { get; set; }
    public int PostId { get; set; }
    public int AuthorId { get; set; }
    public string Body { get; set; } = string.Empty;
    public DateTime PostedAt { get; set; }

    // Navigation properties
    public Post Post { get; set; } = null!;
    public Author Author { get; set; } = null!;
}
