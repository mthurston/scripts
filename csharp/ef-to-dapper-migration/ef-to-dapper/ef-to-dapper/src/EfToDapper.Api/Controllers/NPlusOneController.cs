using System.Diagnostics;
using EfToDapper.Core.DTOs;
using EfToDapper.Data.Context;
using EfToDapper.Data.Dapper;
using EfToDapper.Data.Interceptors;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using Swashbuckle.AspNetCore.Annotations;

namespace EfToDapper.Api.Controllers;

/// <summary>
/// ╔══════════════════════════════════════════════════════════════╗
/// ║  SCENARIO 1 — N+1 Query Problem                             ║
/// ╠══════════════════════════════════════════════════════════════╣
/// ║  What:    Loading a list, then issuing a separate DB query   ║
/// ║           per item to load a related entity.                 ║
/// ║                                                              ║
/// ║  Impact:  30 posts → 31 SQL queries instead of 1.           ║
/// ║           Latency grows linearly with result set size.       ║
/// ║                                                              ║
/// ║  Detect:  MiniProfiler shows 31 queries on /antipattern     ║
/// ║           and 1 query on /fixed-ef.                          ║
/// ║           Look for "SELECT ... WHERE Id = @p0" repeating.   ║
/// ╚══════════════════════════════════════════════════════════════╝
/// </summary>
[ApiController]
[Route("api/scenario1/n-plus-one")]
[Tags("Scenario 1: N+1 Query Problem")]
public class NPlusOneController(AppDbContext db, QueryCounter queryCounter, DapperQueries dapper) : ControllerBase
{
    // ─────────────────────────────────────────────────────────────────────────
    // ANTIPATTERN
    // ─────────────────────────────────────────────────────────────────────────
    [HttpGet("antipattern")]
    [SwaggerOperation(
        Summary = "❌ Antipattern — loads Author with a separate query per post",
        Description = """
            Loads all 30 posts with one query, then issues an additional SELECT per
            post to retrieve the Author — 31 total queries.

            Watch MiniProfiler at /profiler/results-index after this call.
            You will see the repeating pattern:
              SELECT "a"."Id", "a"."Bio", ... FROM "Authors" AS "a" WHERE "a"."Id" = @p0

            This pattern is especially damaging when:
            • The result set is large (100+ rows → 100+ extra queries)
            • The navigation property is accessed inside a loop
            • Lazy loading proxies are enabled (the queries become implicit/invisible)
            """)]
    public async Task<ScenarioResponse<List<PostSummaryDto>>> GetAntiPattern()
    {
        queryCounter.Reset();
        var sw = Stopwatch.StartNew();

        var posts = await db.Posts.ToListAsync(); // Query 1: loads all posts, no JOIN

        var result = new List<PostSummaryDto>();
        foreach (var post in posts)
        {
            // ⚠️ ANTIPATTERN: FindAsync hits the database on every iteration.
            // With 30 posts this issues 30 additional SELECT statements.
            // EF Core's identity map will cache after the first hit per PK, but
            // repeated Posts from different authors still each cost a round-trip.
            var author = await db.Authors.FindAsync(post.AuthorId); // Query 2…N+1

            result.Add(new PostSummaryDto
            {
                Id = post.Id,
                Title = post.Title,
                AuthorName = author!.Name,
                Status = post.Status,
                ViewCount = post.ViewCount,
                PublishedAt = post.PublishedAt,
            });
        }

        sw.Stop();
        return new ScenarioResponse<List<PostSummaryDto>>
        {
            Scenario = "N+1 Query Problem",
            Approach = "ANTIPATTERN",
            Problem = "One SELECT per post to load Author — N posts = N+1 total queries",
            Solution = "Use .Include(p => p.Author) or project with .Select() to get a single JOIN",
            QueryCount = queryCounter.Count,
            ElapsedMs = sw.ElapsedMilliseconds,
            SqlPreview = null, // Multiple ad-hoc queries — no single SQL to preview
            Data = result,
        };
    }

    // ─────────────────────────────────────────────────────────────────────────
    // FIXED — EF Core
    // ─────────────────────────────────────────────────────────────────────────
    [HttpGet("fixed-ef")]
    [SwaggerOperation(
        Summary = "✅ Fixed (EF Core) — single JOIN via Include + projection",
        Description = """
            Uses .Include(p => p.Author) combined with .Select() so EF Core
            emits a single INNER JOIN query.  All 30 post summaries are returned
            in one round-trip to the database.

            Generated SQL (approximate):
              SELECT p.Id, p.Title, p.Status, p.ViewCount, p.PublishedAt,
                     a.Name
              FROM Posts AS p
              INNER JOIN Authors AS a ON a.Id = p.AuthorId
              ORDER BY p.PublishedAt DESC
            """)]
    public async Task<ScenarioResponse<List<PostSummaryDto>>> GetFixedEf()
    {
        queryCounter.Reset();
        var sw = Stopwatch.StartNew();

        // ✅ FIX: Include loads Author in the same query via INNER JOIN.
        // Projecting with Select() is even better — EF Core only selects the
        // columns the DTO actually needs (no full entity hydration).
        var query = db.Posts
            .Include(p => p.Author)
            .OrderByDescending(p => p.PublishedAt)
            .Select(p => new PostSummaryDto
            {
                Id = p.Id,
                Title = p.Title,
                AuthorName = p.Author.Name,   // EF Core resolves via the JOIN
                Status = p.Status,
                ViewCount = p.ViewCount,
                PublishedAt = p.PublishedAt,
            });

        var sqlPreview = query.ToQueryString();
        var result = await query.ToListAsync();

        sw.Stop();
        return new ScenarioResponse<List<PostSummaryDto>>
        {
            Scenario = "N+1 Query Problem",
            Approach = "FIXED (EF Core)",
            Problem = "One SELECT per post to load Author — N posts = N+1 total queries",
            Solution = ".Include(p => p.Author) collapses N+1 into a single INNER JOIN",
            QueryCount = queryCounter.Count,
            ElapsedMs = sw.ElapsedMilliseconds,
            SqlPreview = sqlPreview,
            Data = result,
        };
    }

    // ─────────────────────────────────────────────────────────────────────────
    // DAPPER — Stored Procedure
    // ─────────────────────────────────────────────────────────────────────────
    [HttpGet("dapper")]
    [SwaggerOperation(
        Summary = "🔧 Dapper — calls usp_GetPostSummaries stored procedure",
        Description = """
            Executes the stored procedure database/StoredProcedures/usp_GetPostSummaries.sql
            which you implement to return post summaries with a single JOIN.
            
            Stored procedures are pre-compiled by SQL Server, similar to EF.CompileAsyncQuery.
            Compare results with /fixed-ef to verify correctness.
            """)]
    public async Task<ScenarioResponse<IEnumerable<PostSummaryDto>>> GetDapper()
    {
        var sw = Stopwatch.StartNew();
        var result = await dapper.GetPostSummaries();
        sw.Stop();

        return new ScenarioResponse<IEnumerable<PostSummaryDto>>
        {
            Scenario = "N+1 Query Problem",
            Approach = "DAPPER (Stored Procedure)",
            Problem = "One SELECT per post to load Author",
            Solution = "Stored procedure with single JOIN — implement usp_GetPostSummaries.sql",
            QueryCount = null,
            ElapsedMs = sw.ElapsedMilliseconds,
            SqlPreview = "See database/StoredProcedures/usp_GetPostSummaries.sql",
            Data = result,
        };
    }
}
