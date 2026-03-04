using EfToDapper.Data.Context;
using EfToDapper.Tests.Infrastructure;
using FluentAssertions;
using Microsoft.EntityFrameworkCore;

namespace EfToDapper.Tests;

/// <summary>
/// Verifies that DataSeeder produces the expected data shape so the workshop
/// scenarios have a consistent and predictable baseline.
/// </summary>
public class SeedingTests : IDisposable
{
    private readonly AppDbContext _db;
    private readonly Action _cleanup;

    public SeedingTests()
    {
        var (ctx, cleanup) = TestDbContextFactory.CreateSeeded();
        _db = ctx;
        _cleanup = cleanup;
    }

    public void Dispose()
    {
        _db.Dispose();
        _cleanup();
    }

    // ── Author assertions ────────────────────────────────────────────────────

    [Fact]
    public void Seeds_Five_Authors()
    {
        _db.Authors.Count().Should().Be(5);
    }

    [Fact]
    public void All_Authors_Have_Unique_Emails()
    {
        var emails = _db.Authors.Select(a => a.Email).ToList();
        emails.Distinct().Count().Should().Be(emails.Count);
    }

    [Fact]
    public void All_Authors_Have_Non_Empty_Name_And_Bio()
    {
        _db.Authors
            .Where(a => a.Name == "" || a.Bio == "")
            .Count().Should().Be(0);
    }

    // ── Tag assertions ───────────────────────────────────────────────────────

    [Fact]
    public void Seeds_Eight_Tags()
    {
        _db.Tags.Count().Should().Be(8);
    }

    // ── Post assertions ──────────────────────────────────────────────────────

    [Fact]
    public void Seeds_Thirty_Posts()
    {
        _db.Posts.Count().Should().Be(30);
    }

    [Fact]
    public void All_Posts_Have_A_Valid_Author()
    {
        var authorIds = _db.Authors.Select(a => a.Id).ToHashSet();

        _db.Posts
            .Where(p => !authorIds.Contains(p.AuthorId))
            .Count().Should().Be(0, "every post must reference a seeded author");
    }

    [Fact]
    public void All_Posts_Have_Non_Empty_Body()
    {
        _db.Posts
            .Where(p => p.Body == null || p.Body == "")
            .Count().Should().Be(0);
    }

    [Fact]
    public void Posts_Have_Mixed_Statuses()
    {
        var statuses = _db.Posts.Select(p => p.Status).Distinct().ToList();
        statuses.Should().Contain("Published");
        statuses.Should().Contain("Draft");
    }

    // ── Tag assignment assertions ────────────────────────────────────────────

    [Fact]
    public void All_Posts_Have_At_Least_One_Tag()
    {
        var postIdsWithTags = _db.PostTags.Select(pt => pt.PostId).Distinct().ToHashSet();
        var allPostIds = _db.Posts.Select(p => p.Id).ToList();

        foreach (var postId in allPostIds)
        {
            postIdsWithTags.Should().Contain(postId,
                because: $"post {postId} must have at least one tag");
        }
    }

    [Fact]
    public void Cartesian_Demo_Post_Has_All_Eight_Tags()
    {
        var demoPostId = DataSeeder.GetCartesianDemoPostId(_db);
        var tagCount = _db.PostTags.Count(pt => pt.PostId == demoPostId);
        tagCount.Should().Be(8, "the Cartesian demo post must have all 8 tags to produce a visible explosion");
    }

    // ── Comment assertions ───────────────────────────────────────────────────

    [Fact]
    public void Seeds_More_Than_100_Comments_Total()
    {
        _db.Comments.Count().Should().BeGreaterThan(100);
    }

    [Fact]
    public void Cartesian_Demo_Post_Has_Twenty_Comments()
    {
        var demoPostId = DataSeeder.GetCartesianDemoPostId(_db);
        var commentCount = _db.Comments.Count(c => c.PostId == demoPostId);
        commentCount.Should().Be(20, "the demo post needs 20 comments so 8×20=160 Cartesian rows are produced");
    }

    [Fact]
    public void Demo_Post_Cartesian_Product_Would_Be_160_Rows()
    {
        // Verifies the demo post produces a meaningful Cartesian product
        var demoPostId = DataSeeder.GetCartesianDemoPostId(_db);
        var tagCount = _db.PostTags.Count(pt => pt.PostId == demoPostId);
        var commentCount = _db.Comments.Count(c => c.PostId == demoPostId);

        (tagCount * commentCount).Should().Be(160,
            because: "160 rows is the Cartesian product that demonstrates the explosion clearly");
    }

    [Fact]
    public void All_Comments_Reference_Valid_Posts_And_Authors()
    {
        var postIds = _db.Posts.Select(p => p.Id).ToHashSet();
        var authorIds = _db.Authors.Select(a => a.Id).ToHashSet();

        var invalidComments = _db.Comments
            .Where(c => !postIds.Contains(c.PostId) || !authorIds.Contains(c.AuthorId))
            .Count();

        invalidComments.Should().Be(0);
    }

    // ── Idempotency ──────────────────────────────────────────────────────────

    [Fact]
    public void Seeder_Is_Idempotent_When_Called_Twice()
    {
        // Calling Seed a second time should be a no-op
        DataSeeder.Seed(_db);

        _db.Authors.Count().Should().Be(5, "second seed call should not duplicate authors");
        _db.Posts.Count().Should().Be(30, "second seed call should not duplicate posts");
    }
}
