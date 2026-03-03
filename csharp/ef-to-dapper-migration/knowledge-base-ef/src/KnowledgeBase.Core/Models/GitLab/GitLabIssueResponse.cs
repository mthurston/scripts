using System.Text.Json.Serialization;

namespace KnowledgeBase.Core.Models.GitLab;

public record GitLabIssueResponse(
    [property: JsonPropertyName("iid")] int Iid,
    [property: JsonPropertyName("title")] string Title,
    [property: JsonPropertyName("description")] string? Description,
    [property: JsonPropertyName("state")] string State,
    [property: JsonPropertyName("web_url")] string WebUrl,
    [property: JsonPropertyName("author")] GitLabUserResponse Author,
    [property: JsonPropertyName("labels")] List<string> Labels,
    [property: JsonPropertyName("created_at")] DateTime CreatedAt,
    [property: JsonPropertyName("updated_at")] DateTime UpdatedAt
);

public record GitLabUserResponse(
    [property: JsonPropertyName("id")] int Id,
    [property: JsonPropertyName("name")] string Name,
    [property: JsonPropertyName("username")] string Username
);

public record GitLabLabelResponse(
    [property: JsonPropertyName("name")] string Name,
    [property: JsonPropertyName("color")] string Color,
    [property: JsonPropertyName("description")] string? Description
);
