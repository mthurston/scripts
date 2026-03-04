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
/// ║  SCENARIO 6 — Compiled Query Overhead on Hot Paths          ║
/// ╠══════════════════════════════════════════════════════════════╣
/// ║  What:    EF Core re-translates LINQ to SQL on every call   ║
/// ║           unless you use EF.CompileAsyncQuery.              ║
/// ║                                                              ║
/// ║  Impact:  ~1-5ms LINQ translation overhead per call.        ║
/// ║           Negligible for occasional queries; significant for ║
/// ║           high-throughput endpoints called thousands/sec.   ║
/// ║                                                              ║
/// ║  Related: Compiled queries also prevent "query plan          ║
/// ║           pollution" in SQL Server's plan cache when         ║
/// ║           parameter sniffing causes bad plan selection.     ║
/// ╚══════════════════════════════════════════════════════════════╝
/// </summary>
[ApiController]
[Route("api/scenario6/compiled-query")]
[Tags("Scenario 6: Compiled Query / Hot Path Overhead")]
public class CompiledQueryHotPathController(AppDbContext db, QueryCounter queryCounter, DapperQueries dapper) : ControllerBase
{
    // Pre-compiled query — defined once at the class level (ideally static).
    // EF Core translates this LINQ expression to SQL exactly once; subsequent
    // calls skip the translation step entirely.
    // Explicit type params needed: compiler resolves IOrderedQueryable (from OrderByDescending)
    // to the wrong CompileAsyncQuery overload without them.
    private static readonly Func<AppDbContext, string, IAsyncEnumerable<PostSummaryDto>>
        CompiledGetByStatus = EF.CompileAsyncQuery<AppDbContext, string, PostSummaryDto>(
            (AppDbContext ctx, string status) =>
                ctx.Posts
                    .AsNoTracking()
                    .Where(p => p.Status == status)
                    .Select(p => new PostSummaryDto
                    {
                        Id = p.Id,
                        Title = p.Title,
                        AuthorName = p.Author.Name,
                        Status = p.Status,
                        ViewCount = p.ViewCount,
                        PublishedAt = p.PublishedAt,
                    })
                    .OrderByDescending(p => p.PublishedAt));

    // ─────────────────────────────────────────────────────────────────────────
    // ANTIPATTERN — re-translates LINQ every call
    // ─────────────────────────────────────────────────────────────────────────
    [HttpGet("{status}/antipattern")]
    [SwaggerOperation(
        Summary = "❌ Antipattern — LINQ expression translated to SQL on every request",
        Description = """
            Every call to ToListAsync() on a plain LINQ query causes EF Core to:
            1. Walk the LINQ expression tree
            2. Generate parameterized SQL
            3. Build a DbCommand
            4. Execute against the database

            Steps 1-3 happen on every single call, even if the query shape never
            changes.  For endpoints called thousands of times per second, this
            translation overhead becomes measurable.

            EF Core has an internal query cache that mitigates this for many
            scenarios, but it still performs expression tree hashing and lookup
            on every call.  Compiled queries skip this entirely.
            """)]
    public async Task<ScenarioResponse<List<PostSummaryDto>>> GetAntiPattern(string status = "Published")
    {
        queryCounter.Reset();
        var sw = Stopwatch.StartNew();

        // ⚠️ ANTIPATTERN: Plain LINQ — EF translates expression tree each request.
        var query = db.Posts
            .AsNoTracking()
            .Where(p => p.Status == status)
            .Select(p => new PostSummaryDto
            {
                Id = p.Id,
                Title = p.Title,
                AuthorName = p.Author.Name,
                Status = p.Status,
                ViewCount = p.ViewCount,
                PublishedAt = p.PublishedAt,
            })
            .OrderByDescending(p => p.PublishedAt);

        var sqlPreview = query.ToQueryString();
        var result = await query.ToListAsync();

        sw.Stop();
        return new ScenarioResponse<List<PostSummaryDto>>
        {
            Scenario = "Compiled Query / Hot Path Overhead",
            Approach = "ANTIPATTERN",
            Problem = "EF Core re-translates LINQ expression tree on every request (~1-5ms overhead)",
            Solution = "Use EF.CompileAsyncQuery() to translate once, reuse forever",
            QueryCount = queryCounter.Count,
            ElapsedMs = sw.ElapsedMilliseconds,
            SqlPreview = sqlPreview,
            Data = result,
        };
    }

    // ─────────────────────────────────────────────────────────────────────────
    // FIXED — EF.CompileAsyncQuery
    // ─────────────────────────────────────────────────────────────────────────
    [HttpGet("{status}/fixed-ef")]
    [SwaggerOperation(
        Summary = "✅ Fixed (EF Core) — EF.CompileAsyncQuery() translates once, reuses forever",
        Description = """
            EF.CompileAsyncQuery() translates the LINQ expression to SQL exactly
            once when the static field is initialized.  Every subsequent call
            executes the pre-built DbCommand directly.

            How to define a compiled query:
              private static readonly Func<AppDbContext, string, IAsyncEnumerable<T>>
                  MyQuery = EF.CompileAsyncQuery(
                      (AppDbContext ctx, string param) =>
                          ctx.Entity.Where(e => e.Field == param));

            When to use compiled queries:
            • High-throughput endpoints (> 1,000 req/s)
            • Query shapes that never change (only parameters vary)
            • Latency-sensitive paths where milliseconds matter

            When NOT to bother:
            • Infrequent queries (reports, admin operations)
            • Queries with dynamic shape (variable Include chains)
            """)]
    public async Task<ScenarioResponse<List<PostSummaryDto>>> GetFixedEf(string status = "Published")
    {
        queryCounter.Reset();
        var sw = Stopwatch.StartNew();

        // ✅ FIX: Use the pre-compiled query — no LINQ translation at runtime.
        var result = new List<PostSummaryDto>();
        await foreach (var item in CompiledGetByStatus(db, status))
            result.Add(item);

        sw.Stop();
        return new ScenarioResponse<List<PostSummaryDto>>
        {
            Scenario = "Compiled Query / Hot Path Overhead",
            Approach = "FIXED (EF Core — EF.CompileAsyncQuery)",
            Problem = "LINQ re-translation overhead on every request",
            Solution = "Compiled query translates LINQ once at startup — subsequent calls skip translation entirely",
            QueryCount = queryCounter.Count,
            ElapsedMs = sw.ElapsedMilliseconds,
            SqlPreview = "SQL pre-compiled — see static CompiledGetByStatus field definition in controller",
            Data = result,
        };
    }

    // ─────────────────────────────────────────────────────────────────────────
    // DAPPER — Stored Procedure
    // ─────────────────────────────────────────────────────────────────────────
    [HttpGet("{status}/dapper")]
    [SwaggerOperation(
        Summary = "🔧 Dapper — calls usp_GetPostsByStatus stored procedure",
        Description = """
            Executes the stored procedure database/StoredProcedures/usp_GetPostsByStatus.sql
            
            Stored procedures are pre-compiled by SQL Server and their execution plans are
            cached on first call — similar to EF.CompileAsyncQuery but at the database level.
            Compare with /fixed-ef to verify results match.
            """)]
    public async Task<ScenarioResponse<List<PostSummaryDto>>> GetDapper(string status = "Published")
    {
        var sw = Stopwatch.StartNew();
        var result = (await dapper.GetPostsByStatus(status)).ToList();
        sw.Stop();

        return new ScenarioResponse<List<PostSummaryDto>>
        {
            Scenario = "Compiled Query / Hot Path Overhead",
            Approach = "DAPPER (Stored Procedure)",
            Problem = "LINQ re-translation overhead on every request",
            Solution = "Stored procedure pre-compiled by SQL Server — implement usp_GetPostsByStatus.sql",
            QueryCount = null,
            ElapsedMs = sw.ElapsedMilliseconds,
            SqlPreview = "See database/StoredProcedures/usp_GetPostsByStatus.sql",
            Data = result,
        };
    }
}
