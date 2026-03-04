namespace EfToDapper.Core.Domain;

public class Tag
{
    public int Id { get; set; }
    public string Name { get; set; } = string.Empty;
    public string Color { get; set; } = string.Empty;

    // Navigation properties
    public ICollection<PostTag> PostTags { get; set; } = new List<PostTag>();
}
