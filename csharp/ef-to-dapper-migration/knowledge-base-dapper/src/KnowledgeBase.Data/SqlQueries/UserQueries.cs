namespace KnowledgeBase.Data.SqlQueries;

public static class UserQueries
{
    public const string GetAll = """
        SELECT Id, Name, Username FROM Users ORDER BY Name;
        """;

    public const string Upsert = """
        MERGE Users AS target
        USING (SELECT @GitLabUserId AS GitLabUserId, @Name AS Name, @Username AS Username) AS source
            ON target.GitLabUserId = source.GitLabUserId
        WHEN MATCHED THEN
            UPDATE SET Name = source.Name, Username = source.Username
        WHEN NOT MATCHED THEN
            INSERT (GitLabUserId, Name, Username)
            VALUES (source.GitLabUserId, source.Name, source.Username)
        OUTPUT INSERTED.Id, INSERTED.GitLabUserId, INSERTED.Name, INSERTED.Username;
        """;
}
