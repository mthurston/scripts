using EfToDapper.Core.DTOs;
using EfToDapper.Data.Context;
using EfToDapper.Data.Interceptors;
using EfToDapper.Tests.Infrastructure;
using FluentAssertions;
using Microsoft.EntityFrameworkCore;

namespace EfToDapper.Tests;

/// <summary>
/// Verifies the observable behavioral difference between antipatterns and their
/// fixed equivalents — using query counts, ChangeTracker state, and SQL text
/// to prove each scenario works as described.
/// </summary>
public class ScenarioTests : IDisposable
{
    private readonly AppDbContext _db;
    private readonly QueryCounter _counter;
    private readonly Action _cleanup;

    public ScenarioTests()
    {
        _counter = new QueryCounter();
        var (ctx, cleanup) = TestDbContextFactory.CreateSeeded(_counter);
        _db = ctx;
        _cleanup = cleanup;
    }

    public void Dispose()
    {
        _db.Dispose();
        _cleanup();
    }

    // ════════════════════════════════════════════════════════════════════════
    // SCENARIO 1 — N+1 Query Problem
    // ════════════════════════════════════════════════════════════════════════

    [Fact]
    public async Task NPlusOne_Antipattern_Issues_More_Queries_Than_Fixed()
    {
        // ANTIPATTERN: 1 query for posts + 1 query per unique author
        _counter.Reset();
        var posts = await _db.Posts.ToListAsync();
        foreach (var post in posts)
            await _db.Authors.FindAsync(post.AuthorId);

        int antipatternCount = _counter.Count;

        // FIXED: a single JOIN query
        _counter.Reset();
        await _db.Posts
            .Include(p => p.Author)
            .Select(p => new { p.Id, p.Title, AuthorName = p.Author.Name })
            .ToListAsync();

        int fixedCount = _counter.Count;

        antipatternCount.Should().BeGreaterThan(fixedCount,
            "N+1 antipattern issues one query per author; the fixed version uses a single JOIN");
        fixedCount.Should().Be(1, "Include() produces a single INNER JOIN query");
    }

    [Fact]
    public async Task NPlusOne_Fixed_Returns_Same_Data_As_Antipattern()
    {
        var antipatternResult = new List<string>();
        var posts = await _db.Posts.ToListAsync();
        foreach (var post in posts)
        {
            var author = await _db.Authors.FindAsync(post.AuthorId);
            antipatternResult.Add($"{post.Id}|{author!.Name}");
        }

        var fixedResult = await _db.Posts
            .Include(p => p.Author)
            .Select(p => $"{p.Id}|{p.Author.Name}")
            .ToListAsync();

        fixedResult.Should().BeEquivalentTo(antipatternResult,
            "both approaches must return the same author names for the same posts");
    }

    // ════════════════════════════════════════════════════════════════════════
    // SCENARIO 2 — Cartesian Explosion
    // ════════════════════════════════════════════════════════════════════════

    [Fact]
    public async Task CartesianExplosion_Fixed_SplitQuery_Returns_Correct_Data()
    {
        var demoPostId = DataSeeder.GetCartesianDemoPostId(_db);

        // Both approaches should return the same tags and comments
        var singleQuery = await _db.Posts
            .Include(p => p.PostTags).ThenInclude(pt => pt.Tag)
            .Include(p => p.Comments)
            .FirstAsync(p => p.Id == demoPostId);

        var splitQuery = await _db.Posts
            .Include(p => p.PostTags).ThenInclude(pt => pt.Tag)
            .Include(p => p.Comments)
            .AsSplitQuery()
            .FirstAsync(p => p.Id == demoPostId);

        splitQuery.PostTags.Count.Should().Be(singleQuery.PostTags.Count,
            "AsSplitQuery must load the same tags as the single-query approach");
        splitQuery.Comments.Count.Should().Be(singleQuery.Comments.Count,
            "AsSplitQuery must load the same comments as the single-query approach");
    }

    [Fact]
    public async Task CartesianExplosion_SplitQuery_Issues_More_Queries_Than_Single()
    {
        var demoPostId = DataSeeder.GetCartesianDemoPostId(_db);

        // Single query: 1 SQL statement (Cartesian JOIN)
        _counter.Reset();
        await _db.Posts
            .Include(p => p.PostTags).ThenInclude(pt => pt.Tag)
            .Include(p => p.Comments).ThenInclude(c => c.Author)
            .FirstAsync(p => p.Id == demoPostId);
        int singleQueryCount = _counter.Count;

        // Split query: one SELECT per Include
        _counter.Reset();
        await _db.Posts
            .Include(p => p.PostTags).ThenInclude(pt => pt.Tag)
            .Include(p => p.Comments).ThenInclude(c => c.Author)
            .AsSplitQuery()
            .FirstAsync(p => p.Id == demoPostId);
        int splitQueryCount = _counter.Count;

        singleQueryCount.Should().Be(1, "a single multi-join query without AsSplitQuery");
        splitQueryCount.Should().BeGreaterThan(1, "AsSplitQuery emits a separate SELECT per Include");
    }

    // ════════════════════════════════════════════════════════════════════════
    // SCENARIO 3 — Change Tracker Overhead
    // ════════════════════════════════════════════════════════════════════════

    [Fact]
    public async Task ChangeTracker_Antipattern_Tracks_Loaded_Entities()
    {
        await _db.Posts.ToListAsync(); // No AsNoTracking

        var trackedCount = _db.ChangeTracker.Entries().Count();
        trackedCount.Should().Be(30, "all 30 posts should be in the ChangeTracker");
    }

    [Fact]
    public async Task ChangeTracker_AsNoTracking_Tracks_Zero_Entities()
    {
        await _db.Posts.AsNoTracking().ToListAsync();

        var trackedCount = _db.ChangeTracker.Entries().Count();
        trackedCount.Should().Be(0, "AsNoTracking() must not register any entities in the ChangeTracker");
    }

    [Fact]
    public async Task ChangeTracker_AsNoTracking_Returns_Same_Data()
    {
        var tracked = await _db.Posts.Select(p => p.Id).OrderBy(id => id).ToListAsync();

        // Clear tracker between the two queries
        _db.ChangeTracker.Clear();

        var untracked = await _db.Posts.AsNoTracking().Select(p => p.Id).OrderBy(id => id).ToListAsync();

        untracked.Should().BeEquivalentTo(tracked,
            "AsNoTracking returns the same rows — only the tracking behavior differs");
    }

    // ════════════════════════════════════════════════════════════════════════
    // SCENARIO 4 — Key Lookup / Missing Index
    // ════════════════════════════════════════════════════════════════════════

    [Fact]
    public async Task KeyLookup_Query_By_Email_Returns_Correct_Author()
    {
        var expected = await _db.Authors.FirstAsync();

        var result = await _db.Authors
            .Where(a => a.Email == expected.Email)
            .FirstOrDefaultAsync();

        result.Should().NotBeNull();
        result!.Id.Should().Be(expected.Id);
        result.Name.Should().Be(expected.Name);
    }

    [Fact]
    public void KeyLookup_Without_Index_SQL_Contains_Where_Clause()
    {
        var sql = _db.Authors
            .Where(a => a.Email == "test@example.com")
            .ToQueryString();

        sql.Should().Contain("Email",
            "the generated SQL must filter on the Email column");
    }

    [Fact]
    public async Task KeyLookup_Unknown_Email_Returns_Null()
    {
        var result = await _db.Authors
            .Where(a => a.Email == "nobody@nowhere.invalid")
            .FirstOrDefaultAsync();

        result.Should().BeNull();
    }

    // ════════════════════════════════════════════════════════════════════════
    // SCENARIO 5 — Over-fetching / Missing Projection
    // ════════════════════════════════════════════════════════════════════════

    [Fact]
    public void OverFetching_Antipattern_SQL_Contains_Body_Column()
    {
        var sql = _db.Posts.ToQueryString();

        sql.Should().Contain("Body",
            "without Select(), EF emits SELECT * which includes the large Body column");
    }

    [Fact]
    public void OverFetching_Projection_SQL_Does_Not_Contain_Body_Column()
    {
        var sql = _db.Posts
            .Select(p => new { p.Id, p.Title })
            .ToQueryString();

        sql.Should().NotContain("Body",
            "Select(p => new { p.Id, p.Title }) must not include the Body column in SQL");
        sql.Should().Contain("Id");
        sql.Should().Contain("Title");
    }

    [Fact]
    public async Task OverFetching_Projection_Returns_Correct_Count()
    {
        var titles = await _db.Posts
            .Select(p => new { p.Id, p.Title })
            .ToListAsync();

        titles.Count.Should().Be(30);
        titles.Should().AllSatisfy(t =>
        {
            t.Title.Should().NotBeNullOrEmpty();
        });
    }

    [Fact]
    public async Task Pagination_Returns_Correct_Page_Size()
    {
        int pageSize = 5;

        var page1 = await _db.Posts
            .AsNoTracking()
            .OrderByDescending(p => p.PublishedAt)
            .Skip(0)
            .Take(pageSize)
            .ToListAsync();

        var page2 = await _db.Posts
            .AsNoTracking()
            .OrderByDescending(p => p.PublishedAt)
            .Skip(pageSize)
            .Take(pageSize)
            .ToListAsync();

        page1.Count.Should().Be(pageSize);
        page2.Count.Should().Be(pageSize);
        page1.Select(p => p.Id).Should().NotIntersectWith(page2.Select(p => p.Id),
            "pages must not overlap");
    }

    // ════════════════════════════════════════════════════════════════════════
    // SCENARIO 6 — Compiled Query
    // ════════════════════════════════════════════════════════════════════════

    private static readonly Func<AppDbContext, string, IAsyncEnumerable<PostSummaryDto>>
        CompiledQuery = EF.CompileAsyncQuery<AppDbContext, string, PostSummaryDto>(
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
                    }));

    [Fact]
    public async Task CompiledQuery_Returns_Same_Results_As_Standard_Query()
    {
        const string status = "Published";

        var standard = await _db.Posts
            .AsNoTracking()
            .Where(p => p.Status == status)
            .Select(p => p.Id)
            .OrderBy(id => id)
            .ToListAsync();

        var compiled = new List<int>();
        await foreach (var item in CompiledQuery(_db, status))
            compiled.Add(item.Id);
        compiled.Sort();

        compiled.Should().BeEquivalentTo(standard,
            "compiled query must return the same rows as the equivalent standard query");
    }

    [Fact]
    public async Task CompiledQuery_Filters_By_Status_Correctly()
    {
        var publishedCount = await _db.Posts.CountAsync(p => p.Status == "Published");

        var compiled = new List<PostSummaryDto>();
        await foreach (var item in CompiledQuery(_db, "Published"))
            compiled.Add(item);

        compiled.Count.Should().Be(publishedCount);
        compiled.Should().AllSatisfy(p => p.Status.Should().Be("Published"));
    }
}
