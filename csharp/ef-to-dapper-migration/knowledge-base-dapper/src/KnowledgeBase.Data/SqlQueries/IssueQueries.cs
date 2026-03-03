namespace KnowledgeBase.Data.SqlQueries;

public static class IssueQueries
{
    // Returns one row per label per issue (fan-out on labels).
    // The repository collapses this into grouped IssueSummaryDto objects.
    public const string GetAll = """
        SELECT
            i.Id,
            i.GitLabId,
            i.Title,
            i.State,
            i.WebUrl,
            i.KbCategory,
            i.CreatedAt,
            u.Id        AS UserId,
            u.Name      AS UserName,
            u.Username  AS UserUsername,
            l.Id        AS LabelId,
            l.Name      AS LabelName,
            l.Color     AS LabelColor,
            l.Description AS LabelDescription
        FROM Issues i
        INNER JOIN Users u ON u.Id = i.AuthorId
        LEFT JOIN IssueLabels il ON il.IssueId = i.Id
        LEFT JOIN Labels l ON l.Id = il.LabelId
        WHERE (@State  IS NULL OR i.State      = @State)
          AND (@Author IS NULL OR u.Username   = @Author)
          AND (@Label  IS NULL OR EXISTS (
                SELECT 1
                FROM IssueLabels il2
                INNER JOIN Labels l2 ON l2.Id = il2.LabelId
                WHERE il2.IssueId = i.Id AND l2.Name = @Label
              ))
        ORDER BY i.CreatedAt DESC;
        """;

    public const string GetById = """
        SELECT
            i.Id,
            i.GitLabId,
            i.Title,
            i.Description,
            i.State,
            i.WebUrl,
            i.KbCategory,
            i.KbNotes,
            i.CreatedAt,
            i.UpdatedAt,
            u.Id       AS UserId,
            u.Name     AS UserName,
            u.Username AS UserUsername,
            l.Id       AS LabelId,
            l.Name     AS LabelName,
            l.Color    AS LabelColor,
            l.Description AS LabelDescription
        FROM Issues i
        INNER JOIN Users u ON u.Id = i.AuthorId
        LEFT JOIN IssueLabels il ON il.IssueId = i.Id
        LEFT JOIN Labels l ON l.Id = il.LabelId
        WHERE i.Id = @Id;
        """;

    public const string UpsertIssue = """
        MERGE Issues AS target
        USING (SELECT @GitLabId AS GitLabId) AS source ON target.GitLabId = source.GitLabId
        WHEN MATCHED THEN
            UPDATE SET
                Title       = @Title,
                Description = @Description,
                State       = @State,
                WebUrl      = @WebUrl,
                AuthorId    = @AuthorId,
                UpdatedAt   = @UpdatedAt
        WHEN NOT MATCHED THEN
            INSERT (GitLabId, Title, Description, State, WebUrl, AuthorId, CreatedAt, UpdatedAt)
            VALUES (@GitLabId, @Title, @Description, @State, @WebUrl, @AuthorId, @CreatedAt, @UpdatedAt)
        OUTPUT INSERTED.Id;
        """;

    public const string DeleteIssueLabelLinks = """
        DELETE FROM IssueLabels WHERE IssueId = @IssueId;
        """;

    public const string InsertIssueLabelLink = """
        INSERT INTO IssueLabels (IssueId, LabelId) VALUES (@IssueId, @LabelId);
        """;

    public const string GetLabelIdByName = """
        SELECT Id FROM Labels WHERE Name = @Name;
        """;

    public const string PatchIssue = """
        UPDATE Issues
        SET KbCategory = COALESCE(@KbCategory, KbCategory),
            KbNotes    = COALESCE(@KbNotes,    KbNotes)
        WHERE Id = @Id;
        """;
}
