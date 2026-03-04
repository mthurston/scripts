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
/// ║  SCENARIO 5 — Over-fetching (Missing Projection)            ║
/// ╠══════════════════════════════════════════════════════════════╣
/// ║  What:    Loading full entity objects when only a subset    ║
/// ║           of columns is actually needed by the caller.      ║
/// ║                                                              ║
/// ║  Impact:  Post.Body can be KB of text.  Selecting all posts ║
/// ║           for a title list wastes network, memory, and I/O. ║
/// ║                                                              ║
/// ║  Detect:  SqlPreview field shows the full SELECT * vs the   ║
/// ║           projected SELECT Id, Title only.                  ║
/// ║                                                              ║
/// ║  Fix:     Use .Select(p => new { p.Id, p.Title }) so EF    ║
/// ║           emits only the columns you actually need.         ║
/// ╚══════════════════════════════════════════════════════════════╝
/// </summary>
[ApiController]
[Route("api/scenario5/over-fetching")]
[Tags("Scenario 5: Over-fetching / Missing Projection")]
public class OverFetchingController(AppDbContext db, QueryCounter queryCounter, DapperQueries dapper) : ControllerBase
{
    // ─────────────────────────────────────────────────────────────────────────
    // ANTIPATTERN — loads every column including large Body TEXT
    // ─────────────────────────────────────────────────────────────────────────
    [HttpGet("post-titles/antipattern")]
    [SwaggerOperation(
        Summary = "❌ Antipattern — loads full Post entities when only Id + Title are needed",
        Description = """
            ToListAsync() without a Select() emits SELECT * — every column in
            the Posts table, including the large Body TEXT field.

            For 30 posts each with 6-9 paragraphs of body text, this transfers
            significantly more data than a simple title list needs.

            Check the SqlPreview field — it shows "Body" in the SELECT list.

            Real-world impact:
            • Body column = potentially KB of content per row
            • With 10,000 posts and 2 KB average body = 20 MB per API call
            • Extra I/O, network bandwidth, and GC pressure for data never shown to the user
            """)]
    public async Task<ScenarioResponse<object>> GetTitlesAntiPattern()
    {
        queryCounter.Reset();
        var sw = Stopwatch.StartNew();

        // ⚠️ ANTIPATTERN: ToList() without Select() loads all columns.
        // Post.Body is a large TEXT column — it's included even though
        // the result set only exposes Id and Title.
        var allPosts = await db.Posts.ToListAsync(); // SELECT * FROM Posts
        var titles = allPosts.Select(p => new { p.Id, p.Title }).ToList();

        sw.Stop();

        // Show the full SQL to make the problem visible
        var sqlPreview = db.Posts.ToQueryString();

        return new ScenarioResponse<object>
        {
            Scenario = "Over-fetching / Missing Projection",
            Approach = "ANTIPATTERN",
            Problem = "SELECT * loads Body (and all other columns) even though only Id + Title are used",
            Solution = "Use .Select(p => new { p.Id, p.Title }) to project exactly what you need",
            QueryCount = queryCounter.Count,
            ElapsedMs = sw.ElapsedMilliseconds,
            SqlPreview = sqlPreview,
            Data = titles,
        };
    }

    // ─────────────────────────────────────────────────────────────────────────
    // FIXED — project only the required columns
    // ─────────────────────────────────────────────────────────────────────────
    [HttpGet("post-titles/fixed-ef")]
    [SwaggerOperation(
        Summary = "✅ Fixed (EF Core) — Select() projects only the columns needed",
        Description = """
            Projecting with Select() before ToListAsync() causes EF Core to emit
            a targeted SQL SELECT that includes only the requested columns.

            Body, Status, ViewCount, PublishedAt, AuthorId are NOT in the query.
            Only Id and Title are fetched from the database.

            The generated SQL will be:
              SELECT "p"."Id", "p"."Title"
              FROM "Posts" AS "p"

            This applies equally to navigations — if you only need Author.Name,
            project it directly instead of including the full Author entity.
            """)]
    public async Task<ScenarioResponse<object>> GetTitlesFixedEf()
    {
        queryCounter.Reset();
        var sw = Stopwatch.StartNew();

        // ✅ FIX: Project directly in the query — only Id and Title are fetched.
        // EF Core translates the anonymous type to SELECT "p"."Id", "p"."Title"
        var query = db.Posts
            .Select(p => new { p.Id, p.Title }); // Only these two columns in SQL

        var sqlPreview = query.ToQueryString();
        var titles = await query.ToListAsync();

        sw.Stop();
        return new ScenarioResponse<object>
        {
            Scenario = "Over-fetching / Missing Projection",
            Approach = "FIXED (EF Core — projection)",
            Problem = "SELECT * fetches all columns including large Body TEXT",
            Solution = "Select() projection emits only the columns needed — Body stays on the server",
            QueryCount = queryCounter.Count,
            ElapsedMs = sw.ElapsedMilliseconds,
            SqlPreview = sqlPreview,
            Data = titles,
        };
    }

    // ─────────────────────────────────────────────────────────────────────────
    // DAPPER — Stored Procedure (Projection)
    // ─────────────────────────────────────────────────────────────────────────
    [HttpGet("post-titles/dapper")]
    [SwaggerOperation(
        Summary = "🔧 Dapper — calls usp_GetPostTitles stored procedure",
        Description = """
            Executes the stored procedure database/StoredProcedures/usp_GetPostTitles.sql
            which should SELECT only Id and Title columns.
            
            With Dapper, you write the SQL yourself — there's no SELECT * by default.
            You only get the columns you explicitly select. Compare with /fixed-ef.
            """)]
    public async Task<ScenarioResponse<object>> GetTitlesDapper()
    {
        var sw = Stopwatch.StartNew();
        var titles = await dapper.GetPostTitles();
        sw.Stop();

        return new ScenarioResponse<object>
        {
            Scenario = "Over-fetching / Missing Projection",
            Approach = "DAPPER (Stored Procedure)",
            Problem = "SELECT * fetches all columns including large Body TEXT",
            Solution = "SELECT only Id, Title in stored procedure — implement usp_GetPostTitles.sql",
            QueryCount = null,
            ElapsedMs = sw.ElapsedMilliseconds,
            SqlPreview = "See database/StoredProcedures/usp_GetPostTitles.sql",
            Data = titles,
        };
    }

    // ─────────────────────────────────────────────────────────────────────────
    // Bonus — DAPPER Pagination
    // ─────────────────────────────────────────────────────────────────────────
    [HttpGet("all-posts/antipattern")]
    [SwaggerOperation(
        Summary = "❌ Antipattern — eager-loads all related data without pagination",
        Description = """
            Combining over-fetching with unbounded result sets: loads ALL posts
            with ALL their relationships included in a single call.

            This pattern is dangerous because:
            1. No pagination — returns every row in the table
            2. Eager-loading all navigations pulls in Tags and Comments for every post
            3. Memory usage grows linearly with the dataset

            In production with 100,000 posts this will:
            • Time out before completing
            • Exhaust server memory
            • Generate query plans that SQL Server cannot optimize
            """)]
    public async Task<ScenarioResponse<object>> GetAllPostsAntiPattern()
    {
        queryCounter.Reset();
        var sw = Stopwatch.StartNew();

        // ⚠️ ANTIPATTERN: No pagination + Include everything
        var posts = await db.Posts
            .Include(p => p.Author)
            .Include(p => p.PostTags).ThenInclude(pt => pt.Tag)
            .Include(p => p.Comments).ThenInclude(c => c.Author)
            .ToListAsync(); // No Skip/Take — returns ALL rows

        sw.Stop();
        return new ScenarioResponse<object>
        {
            Scenario = "Eager Loading Everything + No Pagination",
            Approach = "ANTIPATTERN",
            Problem = $"Loaded ALL {posts.Count} posts with all navigations — no limit on result size",
            Solution = "Add .Skip(offset).Take(pageSize) and only Include what the caller needs",
            QueryCount = queryCounter.Count,
            ElapsedMs = sw.ElapsedMilliseconds,
            Data = new
            {
                TotalPostsLoaded = posts.Count,
                TotalCommentsLoaded = posts.Sum(p => p.Comments.Count),
                TotalTagAssignmentsLoaded = posts.Sum(p => p.PostTags.Count),
                Warning = "In production with large tables this query will exhaust memory or time out",
            },
        };
    }

    [HttpGet("all-posts/fixed-ef")]
    [SwaggerOperation(
        Summary = "✅ Fixed (EF Core) — paginated query with minimal projection",
        Description = """
            Fixes both problems: pagination via Skip/Take and projection to avoid
            loading columns and navigations that the summary view doesn't need.

            The generated SQL uses:
              OFFSET @offset ROWS FETCH NEXT @pageSize ROWS ONLY  (SQL Server)
            """)]
    public async Task<ScenarioResponse<object>> GetAllPostsFixedEf(
        [FromQuery] int page = 1,
        [FromQuery] int pageSize = 10)
    {
        queryCounter.Reset();
        var sw = Stopwatch.StartNew();

        // ✅ FIX: Paginate + project only needed columns
        var query = db.Posts
            .AsNoTracking()
            .OrderByDescending(p => p.PublishedAt)
            .Skip((page - 1) * pageSize)
            .Take(pageSize)
            .Select(p => new PostSummaryDto
            {
                Id = p.Id,
                Title = p.Title,
                AuthorName = p.Author.Name,
                Status = p.Status,
                ViewCount = p.ViewCount,
                PublishedAt = p.PublishedAt,
            });

        var sqlPreview = query.ToQueryString();
        var posts = await query.ToListAsync();
        var totalCount = await db.Posts.CountAsync();

        sw.Stop();
        return new ScenarioResponse<object>
        {
            Scenario = "Eager Loading Everything + No Pagination",
            Approach = "FIXED (EF Core — pagination + projection)",
            Problem = "Unbounded result set with full entity loading",
            Solution = "Skip/Take for pagination + Select for minimal column projection",
            QueryCount = queryCounter.Count,
            ElapsedMs = sw.ElapsedMilliseconds,
            SqlPreview = sqlPreview,
            Data = new
            {
                Page = page,
                PageSize = pageSize,
                TotalCount = totalCount,
                TotalPages = (int)Math.Ceiling((double)totalCount / pageSize),
                Posts = posts,
            },
        };
    }

    [HttpGet("all-posts/dapper")]
    [SwaggerOperation(
        Summary = "🔧 Dapper — calls usp_GetPostsPaged stored procedure",
        Description = """
            Executes the stored procedure database/StoredProcedures/usp_GetPostsPaged.sql
            which should use OFFSET x ROWS FETCH NEXT y ROWS ONLY for pagination.
            
            Compare with /fixed-ef pagination to verify results match.
            """)]
    public async Task<ScenarioResponse<object>> GetAllPostsDapper(
        [FromQuery] int page = 1,
        [FromQuery] int pageSize = 10)
    {
        var sw = Stopwatch.StartNew();
        var posts = await dapper.GetPostsPaged(page, pageSize);
        sw.Stop();

        var postList = posts.ToList();
        var totalCount = await db.Posts.CountAsync();

        return new ScenarioResponse<object>
        {
            Scenario = "Eager Loading Everything + No Pagination",
            Approach = "DAPPER (Stored Procedure)",
            Problem = "Unbounded result set with full entity loading",
            Solution = "Paginated query in stored procedure — implement usp_GetPostsPaged.sql",
            QueryCount = null,
            ElapsedMs = sw.ElapsedMilliseconds,
            SqlPreview = "See database/StoredProcedures/usp_GetPostsPaged.sql",
            Data = new
            {
                Page = page,
                PageSize = pageSize,
                TotalCount = totalCount,
                TotalPages = (int)Math.Ceiling((double)totalCount / pageSize),
                Posts = postList,
            },
        };
    }
}
