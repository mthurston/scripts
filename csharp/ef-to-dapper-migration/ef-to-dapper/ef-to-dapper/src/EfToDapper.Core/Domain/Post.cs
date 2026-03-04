namespace EfToDapper.Core.Domain;

public class Post
{
    public int Id { get; set; }
    public string Title { get; set; } = string.Empty;

    // Large text column — deliberately wide to illustrate over-fetching cost
    public string Body { get; set; } = string.Empty;

    public int AuthorId { get; set; }
    public DateTime PublishedAt { get; set; }
    public string Status { get; set; } = "Published"; // Published | Draft | Archived
    public int ViewCount { get; set; }

    // Navigation properties
    public Author Author { get; set; } = null!;
    public ICollection<Comment> Comments { get; set; } = new List<Comment>();
    public ICollection<PostTag> PostTags { get; set; } = new List<PostTag>();
}
