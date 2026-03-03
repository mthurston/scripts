namespace KnowledgeBase.Data.SqlQueries;

public static class CommentQueries
{
    // Recursive CTE fetches the full comment tree for an issue in a single SQL round-trip.
    //
    // Anchor member:    root comments (ParentCommentId IS NULL).
    // Recursive member: children joined to parents already in the CTE.
    //
    // OPTION (MAXRECURSION 100) caps depth at 100 levels to prevent runaway recursion.
    // The repository then builds the in-memory tree using BuildCommentTree().
    public const string GetThreadedByIssueId = """
        WITH CommentTree AS (
            SELECT
                c.Id,
                c.IssueId,
                c.AuthorId,
                c.Body,
                c.ParentCommentId,
                c.CreatedAt,
                c.UpdatedAt,
                0 AS Depth
            FROM Comments c
            WHERE c.IssueId = @IssueId
              AND c.ParentCommentId IS NULL

            UNION ALL

            SELECT
                c.Id,
                c.IssueId,
                c.AuthorId,
                c.Body,
                c.ParentCommentId,
                c.CreatedAt,
                c.UpdatedAt,
                ct.Depth + 1 AS Depth
            FROM Comments c
            INNER JOIN CommentTree ct ON c.ParentCommentId = ct.Id
        )
        SELECT
            ct.Id,
            ct.IssueId,
            ct.Body,
            ct.ParentCommentId,
            ct.CreatedAt,
            ct.UpdatedAt,
            u.Id       AS AuthorId,
            u.Name     AS AuthorName,
            u.Username AS AuthorUsername
        FROM CommentTree ct
        INNER JOIN Users u ON u.Id = ct.AuthorId
        ORDER BY ct.Depth, ct.CreatedAt
        OPTION (MAXRECURSION 100);
        """;

    public const string InsertComment = """
        INSERT INTO Comments (IssueId, AuthorId, Body, ParentCommentId, CreatedAt, UpdatedAt)
        OUTPUT INSERTED.Id
        VALUES (@IssueId, @AuthorId, @Body, @ParentCommentId, @CreatedAt, @UpdatedAt);
        """;

    public const string GetCommentAuthor = """
        SELECT u.Id, u.Name, u.Username
        FROM Comments c
        INNER JOIN Users u ON u.Id = c.AuthorId
        WHERE c.Id = @CommentId;
        """;
}
