namespace KnowledgeBase.Core.Domain;

public class User
{
    public int Id { get; set; }
    public int GitLabUserId { get; set; }
    public string Name { get; set; } = string.Empty;
    public string Username { get; set; } = string.Empty;
}
