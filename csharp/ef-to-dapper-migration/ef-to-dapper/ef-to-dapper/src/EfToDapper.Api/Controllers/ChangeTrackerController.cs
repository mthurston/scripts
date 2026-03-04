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
/// ║  SCENARIO 3 — Change Tracker Overhead on Reads              ║
/// ╠══════════════════════════════════════════════════════════════╣
/// ║  What:    EF Core tracks every loaded entity in memory so    ║
/// ║           it can detect changes for SaveChanges().           ║
/// ║           For read-only operations this snapshot is wasted.  ║
/// ║                                                              ║
/// ║  Impact:  ~30-40% slower for large read-only result sets.   ║
/// ║           Memory proportional to row count × entity width.  ║
/// ║                                                              ║
/// ║  Detect:  Both endpoints return a "TrackedEntityCount" field ║
/// ║           showing how many objects EF is holding in memory.  ║
/// ║           Antipattern: count > 0. Fixed: count = 0.         ║
/// ╚══════════════════════════════════════════════════════════════╝
/// </summary>
[ApiController]
[Route("api/scenario3/change-tracker")]
[Tags("Scenario 3: Change Tracker Overhead")]
public class ChangeTrackerOverheadController(AppDbContext db, QueryCounter queryCounter, DapperQueries dapper) : ControllerBase
{
    // ─────────────────────────────────────────────────────────────────────────
    // ANTIPATTERN
    // ─────────────────────────────────────────────────────────────────────────
    [HttpGet("antipattern")]
    [SwaggerOperation(
        Summary = "❌ Antipattern — tracked entities waste memory on read-only queries",
        Description = """
            Default EF Core behavior: every entity loaded into a DbContext is
            added to the ChangeTracker with a snapshot of its original state.

            This snapshot is used by SaveChanges() to detect which properties
            changed (dirty checking).  For a read-only reporting endpoint that
            never calls SaveChanges(), this work is entirely wasted.

            Cost breakdown per tracked entity:
            • Snapshot object allocation (mirrors all scalar properties)
            • Dictionary entry in ChangeTracker.Entries
            • ObjectStateEntry overhead

            At scale (reporting 10,000 rows) this adds up to significant GC
            pressure and noticeably higher latency.
            """)]
    public async Task<object> GetAntiPattern()
    {
        queryCounter.Reset();
        var sw = Stopwatch.StartNew();

        // ⚠️ ANTIPATTERN: No AsNoTracking() — all loaded entities are tracked.
        // EF Core will snapshot every Post object for change detection.
        var posts = await db.Posts.ToListAsync();

        sw.Stop();

        // This number shows exactly how many snapshots EF is holding in RAM.
        var trackedCount = db.ChangeTracker.Entries().Count();

        return new ScenarioResponse<object>
        {
            Scenario = "Change Tracker Overhead",
            Approach = "ANTIPATTERN",
            Problem = $"All {posts.Count} posts are now tracked by the ChangeTracker — wasted memory for a read-only endpoint",
            Solution = "Add .AsNoTracking() for any query that doesn't need to write changes back",
            QueryCount = queryCounter.Count,
            ElapsedMs = sw.ElapsedMilliseconds,
            Data = new
            {
                PostCount = posts.Count,
                TrackedEntityCount = trackedCount,
                ChangeTrackerNote = $"EF is holding {trackedCount} entity snapshots in memory. For a read-only report these are never used.",
                Sample = posts.Take(5).Select(p => new { p.Id, p.Title, p.Status }),
            },
        };
    }

    // ─────────────────────────────────────────────────────────────────────────
    // FIXED — EF Core (AsNoTracking)
    // ─────────────────────────────────────────────────────────────────────────
    [HttpGet("fixed-ef")]
    [SwaggerOperation(
        Summary = "✅ Fixed (EF Core) — AsNoTracking() skips snapshot allocation",
        Description = """
            AsNoTracking() instructs EF Core to load entities without registering
            them in the ChangeTracker.  The SQL query is identical; the difference
            is purely in what happens after the results are read from the database.

            Practical guideline:
            • READ-ONLY  (reports, lists, API responses) → always use AsNoTracking()
            • READ-WRITE (load entity, modify, SaveChanges) → use default tracking

            You can also set the default at the DbContext level:
              context.ChangeTracker.QueryTrackingBehavior = QueryTrackingBehavior.NoTracking;

            Or per-query:
              db.Posts.AsNoTrackingWithIdentityResolution()  // safer for navigations
            """)]
    public async Task<object> GetFixedEf()
    {
        queryCounter.Reset();
        var sw = Stopwatch.StartNew();

        // ✅ FIX: AsNoTracking() tells EF Core not to snapshot the loaded entities.
        // The query is identical; EF just skips registration in ChangeTracker.
        var posts = await db.Posts
            .AsNoTracking()   // ← The fix
            .ToListAsync();

        sw.Stop();

        var trackedCount = db.ChangeTracker.Entries().Count(); // Should be 0

        return new ScenarioResponse<object>
        {
            Scenario = "Change Tracker Overhead",
            Approach = "FIXED (EF Core — AsNoTracking)",
            Problem = "Tracked entities waste memory on read-only queries",
            Solution = "AsNoTracking() skips ChangeTracker registration — zero snapshots allocated",
            QueryCount = queryCounter.Count,
            ElapsedMs = sw.ElapsedMilliseconds,
            SqlPreview = db.Posts.AsNoTracking().ToQueryString(),
            Data = new
            {
                PostCount = posts.Count,
                TrackedEntityCount = trackedCount,
                ChangeTrackerNote = $"ChangeTracker holds {trackedCount} entities — no wasted snapshots.",
                Sample = posts.Take(5).Select(p => new { p.Id, p.Title, p.Status }),
            },
        };
    }

    // ─────────────────────────────────────────────────────────────────────────
    // DAPPER — Stored Procedure
    // ─────────────────────────────────────────────────────────────────────────
    [HttpGet("dapper")]
    [SwaggerOperation(
        Summary = "🔧 Dapper — calls usp_GetPostsForReport stored procedure",
        Description = """
            Executes the stored procedure database/StoredProcedures/usp_GetPostsForReport.sql
            
            Dapper never uses a ChangeTracker — all reads are naturally untracked.
            This is the 'fixed' behavior by default. No AsNoTracking() equivalent needed.
            Compare with /fixed-ef to verify results match.
            """)]
    public async Task<object> GetDapper()
    {
        var sw = Stopwatch.StartNew();
        var result = await dapper.GetPostsForReport();
        sw.Stop();

        var posts = result.ToList();
        return new ScenarioResponse<object>
        {
            Scenario = "Change Tracker Overhead",
            Approach = "DAPPER (Stored Procedure)",
            Problem = "Tracked entities waste memory on read-only queries",
            Solution = "Dapper has no ChangeTracker — implement usp_GetPostsForReport.sql",
            QueryCount = null,
            ElapsedMs = sw.ElapsedMilliseconds,
            SqlPreview = "See database/StoredProcedures/usp_GetPostsForReport.sql",
            Data = new
            {
                PostCount = posts.Count,
                TrackedEntityCount = 0,
                ChangeTrackerNote = "Dapper has no ChangeTracker — all reads are untracked by default.",
                Sample = posts.Take(5).Select(p => new { p.Id, p.Title, p.Status }),
            },
        };
    }
}
