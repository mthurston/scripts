namespace KnowledgeBase.Core.DTOs;

public class SyncResultDto
{
    public int IssuesUpserted { get; set; }
    public int LabelsUpserted { get; set; }
    public int UsersUpserted { get; set; }
    public DateTime SyncedAt { get; set; }
    public string? Error { get; set; }
}
