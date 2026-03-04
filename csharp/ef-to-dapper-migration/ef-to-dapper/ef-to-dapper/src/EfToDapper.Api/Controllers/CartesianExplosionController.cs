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
/// ║  SCENARIO 2 — Cartesian Explosion                           ║
/// ╠══════════════════════════════════════════════════════════════╣
/// ║  What:    Including two or more collection navigations in    ║
/// ║           a single EF Core query produces a Cartesian join:  ║
/// ║           TagCount × CommentCount rows per post.             ║
/// ║                                                              ║
/// ║  Impact:  Demo post (8 tags, 20 comments) → 160 rows from   ║
/// ║           the DB instead of 28.  Scales exponentially.      ║
/// ║                                                              ║
/// ║  Detect:  Use /api/scenario2/cartesian-explosion/demo-id to  ║
/// ║           get the ID of the special demo post.  Then call    ║
/// ║           both endpoints and compare QueryCount + ElapsedMs. ║
/// ║           MiniProfiler shows 1 query vs 3 split queries.     ║
/// ╚══════════════════════════════════════════════════════════════╝
/// </summary>
[ApiController]
[Route("api/scenario2/cartesian-explosion")]
[Tags("Scenario 2: Cartesian Explosion")]
public class CartesianExplosionController(AppDbContext db, QueryCounter queryCounter, DapperQueries dapper) : ControllerBase
{
    [HttpGet("demo-id")]
    [SwaggerOperation(Summary = "ℹ️ Get the ID of the Cartesian demo post (8 tags + 20 comments)")]
    public IActionResult GetDemoPostId()
    {
        var id = DataSeeder.GetCartesianDemoPostId(db);
        return Ok(new { PostId = id, Note = "Use this ID in the /antipattern and /fixed-ef endpoints." });
    }

    // ─────────────────────────────────────────────────────────────────────────
    // ANTIPATTERN
    // ─────────────────────────────────────────────────────────────────────────
    [HttpGet("{postId}/antipattern")]
    [SwaggerOperation(
        Summary = "❌ Antipattern — Include(Tags) + Include(Comments) causes Cartesian product",
        Description = """
            Including two independent collections (Tags and Comments) in a single
            query forces SQL to produce a full Cartesian join:
              row_count = TagCount × CommentCount

            For the demo post (8 tags, 20 comments):
              8 × 20 = 160 rows transferred — EF Core then deduplicates in memory.

            The generated SQL looks like:
              SELECT p.*, t.*, c.*
              FROM Posts p
              LEFT JOIN PostTags pt ON pt.PostId = p.Id
              LEFT JOIN Tags t ON t.Id = pt.TagId
              LEFT JOIN Comments c ON c.PostId = p.Id
              LEFT JOIN Authors a ON a.Id = c.AuthorId
              WHERE p.Id = @postId

            This is not a bug — the result is correct.  The problem is the
            unnecessary data transfer and memory pressure for large collections.
            """)]
    public async Task<ScenarioResponse<PostDetailDto?>> GetAntiPattern(int postId)
    {
        queryCounter.Reset();
        var sw = Stopwatch.StartNew();

        // ⚠️ ANTIPATTERN: Two collection Includes without AsSplitQuery().
        // EF Core issues a single SQL query with multiple LEFT JOINs, creating
        // a Cartesian product of Tags × Comments rows per post.
        var post = await db.Posts
            .Include(p => p.Author)
            .Include(p => p.PostTags)
                .ThenInclude(pt => pt.Tag)
            .Include(p => p.Comments)
                .ThenInclude(c => c.Author)
            .FirstOrDefaultAsync(p => p.Id == postId);

        sw.Stop();

        PostDetailDto? dto = null;
        if (post is not null)
        {
            dto = new PostDetailDto
            {
                Id = post.Id,
                Title = post.Title,
                Body = post.Body,
                AuthorName = post.Author.Name,
                AuthorEmail = post.Author.Email,
                Status = post.Status,
                ViewCount = post.ViewCount,
                PublishedAt = post.PublishedAt,
                Tags = post.PostTags.Select(pt => pt.Tag.Name).ToList(),
                Comments = post.Comments.Select(c => new CommentDto
                {
                    Id = c.Id,
                    AuthorName = c.Author.Name,
                    Body = c.Body,
                    PostedAt = c.PostedAt,
                }).ToList(),
            };
        }

        return new ScenarioResponse<PostDetailDto?>
        {
            Scenario = "Cartesian Explosion",
            Approach = "ANTIPATTERN",
            Problem = $"Single JOIN query returns {dto?.Tags.Count ?? 0} × {dto?.Comments.Count ?? 0} = {(dto?.Tags.Count ?? 0) * (dto?.Comments.Count ?? 0)} rows (Cartesian product)",
            Solution = "Add .AsSplitQuery() to emit separate SELECTs per Include — one query per collection",
            QueryCount = queryCounter.Count,
            ElapsedMs = sw.ElapsedMilliseconds,
            SqlPreview = null, // Complex multi-join SQL generated by EF — check MiniProfiler for full text
            Data = dto,
        };
    }

    // ─────────────────────────────────────────────────────────────────────────
    // FIXED — EF Core (AsSplitQuery)
    // ─────────────────────────────────────────────────────────────────────────
    [HttpGet("{postId}/fixed-ef")]
    [SwaggerOperation(
        Summary = "✅ Fixed (EF Core) — AsSplitQuery emits a separate SELECT per Include",
        Description = """
            AsSplitQuery() tells EF Core to issue one SQL SELECT per Include
            instead of a single multi-join query.

            For the demo post:
              Query 1: SELECT the post + Author  (1 row)
              Query 2: SELECT PostTags + Tags    (8 rows)
              Query 3: SELECT Comments + Authors (20 rows)
              Total transferred: 29 rows — down from 160!

            Trade-off: multiple round-trips instead of one.
            AsSplitQuery is the right choice when collections are large
            and the Cartesian product would be enormous.

            For small collections (< ~10 items each), the single-query
            approach is usually fine despite the duplication.
            """)]
    public async Task<ScenarioResponse<PostDetailDto?>> GetFixedEf(int postId)
    {
        queryCounter.Reset();
        var sw = Stopwatch.StartNew();

        // ✅ FIX: AsSplitQuery() prevents the Cartesian product.
        // EF Core issues one SELECT per Include, then stitches in memory.
        var post = await db.Posts
            .Include(p => p.Author)
            .Include(p => p.PostTags)
                .ThenInclude(pt => pt.Tag)
            .Include(p => p.Comments)
                .ThenInclude(c => c.Author)
            .AsSplitQuery()   // ← The fix
            .FirstOrDefaultAsync(p => p.Id == postId);

        sw.Stop();

        PostDetailDto? dto = null;
        if (post is not null)
        {
            dto = new PostDetailDto
            {
                Id = post.Id,
                Title = post.Title,
                Body = post.Body,
                AuthorName = post.Author.Name,
                AuthorEmail = post.Author.Email,
                Status = post.Status,
                ViewCount = post.ViewCount,
                PublishedAt = post.PublishedAt,
                Tags = post.PostTags.Select(pt => pt.Tag.Name).ToList(),
                Comments = post.Comments.Select(c => new CommentDto
                {
                    Id = c.Id,
                    AuthorName = c.Author.Name,
                    Body = c.Body,
                    PostedAt = c.PostedAt,
                }).ToList(),
            };
        }

        return new ScenarioResponse<PostDetailDto?>
        {
            Scenario = "Cartesian Explosion",
            Approach = "FIXED (EF Core — AsSplitQuery)",
            Problem = "Single JOIN query returns TagCount × CommentCount rows",
            Solution = $"AsSplitQuery issues {queryCounter.Count} separate SELECTs — {dto?.Tags.Count ?? 0} + {dto?.Comments.Count ?? 0} rows instead of {(dto?.Tags.Count ?? 0) * (dto?.Comments.Count ?? 0)}",
            QueryCount = queryCounter.Count,
            ElapsedMs = sw.ElapsedMilliseconds,
            SqlPreview = null,
            Data = dto,
        };
    }

    // ─────────────────────────────────────────────────────────────────────────
    // DAPPER — Stored Procedure
    // ─────────────────────────────────────────────────────────────────────────
    [HttpGet("{postId}/dapper")]
    [SwaggerOperation(
        Summary = "🔧 Dapper — calls usp_GetPostDetails stored procedure",
        Description = """
            Executes the stored procedure database/StoredProcedures/usp_GetPostDetails.sql
            which should return two result sets:
              1. Post header + one row per tag
              2. One row per comment
            
            DapperQueries.GetPostDetails() uses QueryMultipleAsync to read both result sets
            and manually assembles the PostDetailDto. Compare with /fixed-ef.
            """)]
    public async Task<ScenarioResponse<PostDetailDto?>> GetDapper(int postId)
    {
        var sw = Stopwatch.StartNew();
        var result = await dapper.GetPostDetails(postId);
        sw.Stop();

        return new ScenarioResponse<PostDetailDto?>
        {
            Scenario = "Cartesian Explosion",
            Approach = "DAPPER (Stored Procedure)",
            Problem = "TagCount × CommentCount rows in single query",
            Solution = "Two result sets from stored procedure — implement usp_GetPostDetails.sql",
            QueryCount = null,
            ElapsedMs = sw.ElapsedMilliseconds,
            SqlPreview = "See database/StoredProcedures/usp_GetPostDetails.sql",
            Data = result,
        };
    }
}
