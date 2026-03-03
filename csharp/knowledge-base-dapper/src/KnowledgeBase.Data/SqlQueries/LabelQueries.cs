namespace KnowledgeBase.Data.SqlQueries;

public static class LabelQueries
{
    public const string GetAll = """
        SELECT Id, Name, Color, Description FROM Labels ORDER BY Name;
        """;

    public const string Upsert = """
        MERGE Labels AS target
        USING (SELECT @Name AS Name, @Color AS Color, @Description AS Description) AS source
            ON target.Name = source.Name
        WHEN MATCHED THEN
            UPDATE SET Color = source.Color, Description = source.Description
        WHEN NOT MATCHED THEN
            INSERT (Name, Color, Description)
            VALUES (source.Name, source.Color, source.Description)
        OUTPUT INSERTED.Id, INSERTED.Name, INSERTED.Color, INSERTED.Description;
        """;
}
