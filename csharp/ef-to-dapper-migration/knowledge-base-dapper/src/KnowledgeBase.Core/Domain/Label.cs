namespace KnowledgeBase.Core.Domain;

public class Label
{
    public int Id { get; set; }
    public string Name { get; set; } = string.Empty;
    public string Color { get; set; } = string.Empty;
    public string? Description { get; set; }

    public ICollection<IssueLabel> IssueLabels { get; set; } = [];
}
