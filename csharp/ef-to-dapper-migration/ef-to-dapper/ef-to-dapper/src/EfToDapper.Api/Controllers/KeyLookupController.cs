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
/// ║  SCENARIO 4 — Key Lookup (Missing Covering Index)           ║
/// ╠══════════════════════════════════════════════════════════════╣
/// ║  What:    Querying on a non-indexed column forces the DB    ║
/// ║           to scan the table, then follow a key lookup back  ║
/// ║           to the clustered index for each matching row.     ║
/// ║                                                              ║
/// ║  In SQL Server execution plan: "Key Lookup" operator.       ║
/// ║                                                              ║
/// ║  Fix A — simple index:  HasIndex(a => a.Email)              ║
/// ║  Fix B — covering index: also IncludeProperties(a => a.Name)║
/// ║           avoids the key lookup entirely (no main-table hit) ║
/// ║                                                              ║
/// ║  Detect: In SSMS run a query → Query menu → Include Actual  ║
/// ║          Execution Plan.  Look for "Key Lookup" operator.   ║
/// ║          Hover it to see estimated vs actual rows + cost %.  ║
/// ╚══════════════════════════════════════════════════════════════╝
/// </summary>
[ApiController]
[Route("api/scenario4/key-lookup")]
[Tags("Scenario 4: Key Lookup / Missing Index")]
public class KeyLookupController(AppDbContext db, QueryCounter queryCounter, DapperQueries dapper) : ControllerBase
{
    // ─────────────────────────────────────────────────────────────────────────
    // ANTIPATTERN — no index on Authors.Email
    // ─────────────────────────────────────────────────────────────────────────
    [HttpGet("by-email/antipattern")]
    [SwaggerOperation(
        Summary = "❌ Antipattern — lookup by Email with no index (table scan + key lookup)",
        Description = """
            Querying by the Email column which has no index configured in AppDbContext.
            The database must:
              1. Scan every row in the Authors table
              2. Compare Email on each row
              3. Return matching row(s)

            On SQL Server with millions of rows this shows in the execution plan as:
              • Clustered Index Scan  (reads entire table)
              • Key Lookup            (if a non-covering index is added later)

            The EF Core code is correct — the problem lives in the schema.
            Open AppDbContext.cs to see the commented-out index definitions.

            To see the Key Lookup in SSMS:
              1. Connect to (localdb)\mssqllocaldb → EfToDapperDemo
              2. New query → Query menu → Include Actual Execution Plan
              3. Run: SELECT Name, Email, Bio FROM Authors WHERE Email = 'alice@example.com'
              4. Click the execution plan tab — look for "Clustered Index Scan"
                 (or "Key Lookup" after Option A index is added)
            """)]
    public async Task<ScenarioResponse<AuthorSummaryDto?>> GetByEmailAntiPattern(string email)
    {
        queryCounter.Reset();
        var sw = Stopwatch.StartNew();

        // ⚠️ ANTIPATTERN: Email column has no index → table scan on every call.
        // The SQL itself is fine; the missing index is the problem.
        var query = db.Authors
            .Where(a => a.Email == email)
            .Select(a => new AuthorSummaryDto
            {
                Id = a.Id,
                Name = a.Name,
                Email = a.Email,
                PostCount = a.Posts.Count(),
            });

        var sqlPreview = query.ToQueryString();
        var author = await query.FirstOrDefaultAsync();

        sw.Stop();
        return new ScenarioResponse<AuthorSummaryDto?>
        {
            Scenario = "Key Lookup / Missing Index",
            Approach = "ANTIPATTERN",
            Problem = "No index on Authors.Email — every lookup scans the full table",
            Solution = "Add HasIndex(a => a.Email).IncludeProperties(a => a.Name) in AppDbContext.OnModelCreating",
            QueryCount = queryCounter.Count,
            ElapsedMs = sw.ElapsedMilliseconds,
            SqlPreview = sqlPreview,
            Data = author,
        };
    }

    // ─────────────────────────────────────────────────────────────────────────
    // FIXED — index defined in DbContext
    // ─────────────────────────────────────────────────────────────────────────
    [HttpGet("by-email/fixed-ef")]
    [SwaggerOperation(
        Summary = "✅ Fixed (EF Core) — covering index eliminates the key lookup",
        Description = """
            The SQL query is IDENTICAL to the antipattern.  The difference is
            a covering index defined in AppDbContext.OnModelCreating:

              modelBuilder.Entity<Author>()
                  .HasIndex(a => a.Email)
                  .IncludeProperties(a => new { a.Name, a.Bio })
                  .HasDatabaseName("IX_Authors_Email_Covering");

            A COVERING index includes additional columns in the index leaf pages.
            This means the database can satisfy the query entirely from the index
            — it never needs to follow a pointer back to the main table (no key lookup).

            To see the difference:
            1. Open AppDbContext.cs, uncomment Option B (covering index)
            2. Drop the EfToDapperDemo database in SSMS (or LocalDB CLI)
            3. Restart the app — EnsureCreated rebuilds the schema with the index
            4. Re-run the SSMS query with Actual Execution Plan
               • Option A: "Index Seek" + "Key Lookup" (still a lookup, but on fewer rows)
               • Option B: "Index Seek" only — the index is self-contained, no lookup needed
            """)]
    public async Task<ScenarioResponse<AuthorSummaryDto?>> GetByEmailFixedEf(string email)
    {
        queryCounter.Reset();
        var sw = Stopwatch.StartNew();

        // ✅ FIX: Same EF Core query — the fix is the index in AppDbContext.
        // Uncomment Option B in AppDbContext.OnModelCreating to enable the covering index.
        var query = db.Authors
            .Where(a => a.Email == email)
            .Select(a => new AuthorSummaryDto
            {
                Id = a.Id,
                Name = a.Name,
                Email = a.Email,
                PostCount = a.Posts.Count(),
            });

        var sqlPreview = query.ToQueryString();
        var author = await query.FirstOrDefaultAsync();

        sw.Stop();
        return new ScenarioResponse<AuthorSummaryDto?>
        {
            Scenario = "Key Lookup / Missing Index",
            Approach = "FIXED (EF Core — covering index)",
            Problem = "No index on Authors.Email causes table scans",
            Solution = "Covering index added in AppDbContext — see commented code in OnModelCreating",
            QueryCount = queryCounter.Count,
            ElapsedMs = sw.ElapsedMilliseconds,
            SqlPreview = sqlPreview,
            Data = author,
        };
    }

    // Convenience endpoint: list all authors with their emails
    [HttpGet("authors")]
    [SwaggerOperation(Summary = "ℹ️ List all authors (use their emails in the lookup endpoints)")]
    public async Task<IActionResult> ListAuthors()
    {
        var authors = await db.Authors
            .AsNoTracking()
            .Select(a => new { a.Id, a.Name, a.Email })
            .ToListAsync();
        return Ok(authors);
    }

    // ─────────────────────────────────────────────────────────────────────────
    // DAPPER — Stored Procedure
    // ─────────────────────────────────────────────────────────────────────────
    [HttpGet("by-email/dapper")]
    [SwaggerOperation(
        Summary = "🔧 Dapper — calls usp_GetAuthorByEmail stored procedure",
        Description = """
            Executes the stored procedure database/StoredProcedures/usp_GetAuthorByEmail.sql
            
            The SQL fix (covering index) is schema-level, not query-level.
            Whether you use EF or Dapper, add the index in AppDbContext.OnModelCreating.
            Compare with /fixed-ef to verify results match.
            """)]
    public async Task<ScenarioResponse<AuthorSummaryDto?>> GetByEmailDapper(string email)
    {
        var sw = Stopwatch.StartNew();
        var author = await dapper.GetAuthorByEmail(email);
        sw.Stop();

        return new ScenarioResponse<AuthorSummaryDto?>
        {
            Scenario = "Key Lookup / Missing Index",
            Approach = "DAPPER (Stored Procedure)",
            Problem = "No index on Authors.Email causes table scans",
            Solution = "Implement usp_GetAuthorByEmail.sql + add covering index in AppDbContext",
            QueryCount = null,
            ElapsedMs = sw.ElapsedMilliseconds,
            SqlPreview = "See database/StoredProcedures/usp_GetAuthorByEmail.sql",
            Data = author,
        };
    }
}
