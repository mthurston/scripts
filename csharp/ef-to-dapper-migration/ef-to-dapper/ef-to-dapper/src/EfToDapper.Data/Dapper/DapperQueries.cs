using Dapper;
using EfToDapper.Core.DTOs;

namespace EfToDapper.Data.Dapper;

/// <summary>
/// Executes each workshop scenario via a stored procedure.
/// Implement the corresponding stored procedure in database/StoredProcedures/
/// then restart the app and call the /dapper endpoint for that scenario.
/// </summary>
public class DapperQueries(IDapperConnectionFactory connectionFactory)
{
    // ── Scenario 1 — N+1 Fix ────────────────────────────────────────────────
    // Implement: database/StoredProcedures/usp_GetPostSummaries.sql
    public async Task<IEnumerable<PostSummaryDto>> GetPostSummaries()
    {
        using var conn = connectionFactory.CreateConnection();
        return await conn.QueryAsync<PostSummaryDto>("EXEC usp_GetPostSummaries");
    }

    // ── Scenario 2 — Cartesian Explosion Fix ────────────────────────────────
    // Implement: database/StoredProcedures/usp_GetPostDetails.sql
    // The stored procedure must return two result sets:
    //   Result set 1 — post header + one row per tag
    //   Result set 2 — one row per comment
    public async Task<PostDetailDto?> GetPostDetails(int postId)
    {
        using var conn = connectionFactory.CreateConnection();
        using var multi = await conn.QueryMultipleAsync(
            "EXEC usp_GetPostDetails @PostId", new { PostId = postId });

        // Result set 1: post header columns + one row per tag
        var tagRows = (await multi.ReadAsync<dynamic>()).AsList();
        if (tagRows.Count == 0) return null;

        var first = tagRows[0];
        var dto = new PostDetailDto
        {
            Id        = first.Id,
            Title     = first.Title,
            Body      = first.Body,
            AuthorName  = first.AuthorName,
            AuthorEmail = first.AuthorEmail,
            Status    = first.Status,
            ViewCount = first.ViewCount,
            PublishedAt = first.PublishedAt,
            Tags = tagRows
                .Where(r => r.TagName != null)
                .Select(r => (string)r.TagName)
                .Distinct()
                .ToList(),
        };

        // Result set 2: comments
        dto.Comments = (await multi.ReadAsync<CommentDto>()).AsList();
        return dto;
    }

    // ── Scenario 3 — Change Tracker (Read-Only Report) ──────────────────────
    // Implement: database/StoredProcedures/usp_GetPostsForReport.sql
    // Dapper reads are always untracked — no ChangeTracker overhead.
    public async Task<IEnumerable<PostSummaryDto>> GetPostsForReport()
    {
        using var conn = connectionFactory.CreateConnection();
        return await conn.QueryAsync<PostSummaryDto>("EXEC usp_GetPostsForReport");
    }

    // ── Scenario 4 — Key Lookup / Author by Email ───────────────────────────
    // Implement: database/StoredProcedures/usp_GetAuthorByEmail.sql
    public async Task<AuthorSummaryDto?> GetAuthorByEmail(string email)
    {
        using var conn = connectionFactory.CreateConnection();
        return await conn.QuerySingleOrDefaultAsync<AuthorSummaryDto>(
            "EXEC usp_GetAuthorByEmail @Email", new { Email = email });
    }

    // ── Scenario 5 — Over-fetching Fix (Projection) ─────────────────────────
    // Implement: database/StoredProcedures/usp_GetPostTitles.sql
    public async Task<IEnumerable<object>> GetPostTitles()
    {
        using var conn = connectionFactory.CreateConnection();
        return await conn.QueryAsync("EXEC usp_GetPostTitles");
    }

    // ── Scenario 5 — Unbounded Results Fix (Pagination) ─────────────────────
    // Implement: database/StoredProcedures/usp_GetPostsPaged.sql
    public async Task<IEnumerable<PostSummaryDto>> GetPostsPaged(int page, int pageSize)
    {
        int offset = (page - 1) * pageSize;
        using var conn = connectionFactory.CreateConnection();
        return await conn.QueryAsync<PostSummaryDto>(
            "EXEC usp_GetPostsPaged @PageSize, @Offset", new { PageSize = pageSize, Offset = offset });
    }

    // ── Scenario 6 — Compiled Query / Hot Path ──────────────────────────────
    // Implement: database/StoredProcedures/usp_GetPostsByStatus.sql
    // Stored procedures are pre-compiled by SQL Server — execution plan is
    // cached on first call, equivalent to EF.CompileAsyncQuery.
    public async Task<IEnumerable<PostSummaryDto>> GetPostsByStatus(string status)
    {
        using var conn = connectionFactory.CreateConnection();
        return await conn.QueryAsync<PostSummaryDto>(
            "EXEC usp_GetPostsByStatus @Status", new { Status = status });
    }
}
