namespace EfToDapper.Core.DTOs;

/// <summary>
/// Wraps scenario endpoint results with performance metadata so participants
/// can see query counts, timing, and generated SQL directly in the Swagger response.
/// </summary>
public class ScenarioResponse<T>
{
    public string Scenario { get; set; } = string.Empty;
    public string Approach { get; set; } = string.Empty;
    public string Problem { get; set; } = string.Empty;
    public string Solution { get; set; } = string.Empty;

    /// <summary>Number of SQL commands issued to the database for this request.</summary>
    public int QueryCount { get; set; }

    /// <summary>Wall-clock time for the data access portion only.</summary>
    public long ElapsedMs { get; set; }

    /// <summary>
    /// The SQL EF Core would generate (from ToQueryString on the IQueryable).
    /// Null for antipatterns where multiple ad-hoc queries are used.
    /// </summary>
    public string? SqlPreview { get; set; }

    public T? Data { get; set; }
}
