namespace EfToDapper.Core.Domain;

public class PostTag
{
    public int PostId { get; set; }
    public int TagId { get; set; }

    // Navigation properties
    public Post Post { get; set; } = null!;
    public Tag Tag { get; set; } = null!;
}
