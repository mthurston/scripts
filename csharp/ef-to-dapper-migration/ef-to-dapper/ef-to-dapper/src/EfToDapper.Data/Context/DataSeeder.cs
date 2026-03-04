using EfToDapper.Core.Domain;

namespace EfToDapper.Data.Context;

/// <summary>
/// Seeds deterministic, realistic data into the SQL Server LocalDB database.
/// The seed is designed to make each antipattern clearly visible:
///   - 5 authors   → N+1 shows 31 queries (1 + 30 per-author lookups)
///   - 30 posts    → Over-fetching shows the Body column cost
///   - 8 tags      → Post "Cartesian Demo" has all 8 tags
///   - 300+ comments → Post "Cartesian Demo" has 20 comments (8×20 = 160 rows!)
/// </summary>
public static class DataSeeder
{
    public static void Seed(AppDbContext context)
    {
        if (context.Authors.Any()) return; // Already seeded

        // ── Authors ──────────────────────────────────────────────────────────
        var authors = new[]
        {
            new Author { Name = "Alice Nguyen",    Email = "alice@example.com",   Bio = "Senior backend engineer, 10 years .NET experience.",         JoinedAt = new DateTime(2018, 3, 10) },
            new Author { Name = "Bob Kaminski",    Email = "bob@example.com",     Bio = "Data access specialist, SQL Server MVP.",                    JoinedAt = new DateTime(2019, 7, 22) },
            new Author { Name = "Carmen Diaz",     Email = "carmen@example.com",  Bio = "Full-stack developer, passionate about clean architecture.", JoinedAt = new DateTime(2020, 1, 5)  },
            new Author { Name = "David Park",      Email = "david@example.com",   Bio = "DBA turned developer, loves query plan analysis.",           JoinedAt = new DateTime(2021, 5, 14) },
            new Author { Name = "Elena Marchetti", Email = "elena@example.com",   Bio = "Open-source contributor, Dapper core team.",                 JoinedAt = new DateTime(2022, 9, 30) },
        };
        context.Authors.AddRange(authors);
        context.SaveChanges();

        // ── Tags ─────────────────────────────────────────────────────────────
        var tags = new[]
        {
            new Tag { Name = "C#",           Color = "#178600" },
            new Tag { Name = ".NET",         Color = "#512bd4" },
            new Tag { Name = "EF Core",      Color = "#ff6600" },
            new Tag { Name = "Dapper",       Color = "#1e8bc3" },
            new Tag { Name = "SQL",          Color = "#cc2927" },
            new Tag { Name = "Performance",  Color = "#e44d26" },
            new Tag { Name = "Architecture", Color = "#6f42c1" },
            new Tag { Name = "Best Practices", Color = "#28a745" },
        };
        context.Tags.AddRange(tags);
        context.SaveChanges();

        // ── Posts ─────────────────────────────────────────────────────────────
        var lorem = "Lorem ipsum dolor sit amet, consectetur adipiscing elit. Sed do eiusmod tempor " +
                    "incididunt ut labore et dolore magna aliqua. Ut enim ad minim veniam, quis nostrud " +
                    "exercitation ullamco laboris nisi ut aliquip ex ea commodo consequat. Duis aute irure " +
                    "dolor in reprehenderit in voluptate velit esse cillum dolore eu fugiat nulla pariatur. " +
                    "Excepteur sint occaecat cupidatat non proident, sunt in culpa qui officia deserunt mollit " +
                    "anim id est laborum. ";

        var postTitles = new[]
        {
            // Alice's posts
            "Understanding EF Core Change Tracking",
            "When AsNoTracking Matters Most",
            "Projection Queries: Select Only What You Need",
            "Avoiding the N+1 Query Problem",
            "Batch Updates with ExecuteUpdate",
            // Bob's posts
            "SQL Server Execution Plans Demystified",
            "Covering Indexes: The Key Lookup Killer",
            "Statistics and Query Plan Stability",
            "Cartesian Explosion in EF Core Includes",
            "Parameterized Queries and Plan Cache",
            // Carmen's posts
            "Clean Architecture with Repository Pattern",
            "Migrating from EF Core to Dapper",
            "Connection Pooling Best Practices",
            "Async Data Access Patterns",
            "Testing Data Access with SQLite",
            // David's posts
            "Reading SQL Execution Plans",
            "Index Design for OLTP Workloads",
            "Diagnosing Slow Queries in Production",
            "Split Queries vs Single Join Queries",
            "Bulk Insert Strategies in .NET",
            // Elena's posts
            "Getting Started with Dapper",
            "Multi-Mapping in Dapper",
            "Dapper vs EF Core: When to Use Each",
            "Custom Type Handlers in Dapper",
            "Stored Procedures with Dapper",
            // Mixed
            "Pagination Strategies for Large Tables",
            "Connection String Security Best Practices",
            "Global Query Filters in EF Core",
            "Compiled Queries for Hot Paths",

            // SPECIAL: This post has ALL 8 tags and 20 comments — the Cartesian Explosion demo
            "Cartesian Explosion Demo: Many Tags + Many Comments",
        };

        var statuses = new[] { "Published", "Published", "Published", "Draft", "Archived" };
        var posts = new List<Post>();
        var baseDate = new DateTime(2024, 1, 1);

        for (int i = 0; i < postTitles.Length; i++)
        {
            var authorIndex = i % authors.Length;
            posts.Add(new Post
            {
                Title = postTitles[i],
                Body = string.Concat(Enumerable.Repeat(lorem, 6 + (i % 4))), // 6-9 paragraphs
                AuthorId = authors[authorIndex].Id,
                PublishedAt = baseDate.AddDays(i * 7),
                Status = statuses[i % statuses.Length],
                ViewCount = (i + 1) * 137 % 5000,
            });
        }
        context.Posts.AddRange(posts);
        context.SaveChanges();

        // ── Post-Tag Assignments ───────────────────────────────────────────────
        // Each regular post gets 2-5 tags (deterministic by index)
        var postTags = new List<PostTag>();
        for (int i = 0; i < posts.Count - 1; i++) // exclude the Cartesian demo post
        {
            int tagCount = 2 + (i % 4); // 2, 3, 4, or 5 tags
            for (int t = 0; t < tagCount; t++)
            {
                var tagIndex = (i + t) % tags.Length;
                postTags.Add(new PostTag { PostId = posts[i].Id, TagId = tags[tagIndex].Id });
            }
        }

        // Cartesian demo post: ALL 8 tags
        var demoPost = posts[^1];
        foreach (var tag in tags)
            postTags.Add(new PostTag { PostId = demoPost.Id, TagId = tag.Id });

        context.PostTags.AddRange(postTags);
        context.SaveChanges();

        // ── Comments ──────────────────────────────────────────────────────────
        var commentBodies = new[]
        {
            "Great article! Really helped me understand this concept.",
            "I ran into this exact problem last week. Wish I'd seen this sooner.",
            "Have you considered using compiled queries for even better performance?",
            "The benchmark numbers here are eye-opening. Thanks for sharing.",
            "One thing worth noting: this behavior changed in EF Core 7.",
            "I'd love to see a follow-up post on the SQL Server execution plan.",
            "This is the canonical answer to a question I've been asked dozens of times.",
            "The `AsNoTracking()` tip alone saved us 40% on a reporting endpoint.",
            "What about when you have multiple DbContext instances?",
            "We saw this in production with 500k rows. Took down the app for 20 minutes.",
        };

        var comments = new List<Comment>();
        for (int i = 0; i < posts.Count - 1; i++) // regular posts: 5-12 comments each
        {
            int commentCount = 5 + (i % 8);
            for (int c = 0; c < commentCount; c++)
            {
                comments.Add(new Comment
                {
                    PostId = posts[i].Id,
                    AuthorId = authors[(i + c) % authors.Length].Id,
                    Body = commentBodies[(i + c) % commentBodies.Length],
                    PostedAt = baseDate.AddDays(i * 7 + c + 1),
                });
            }
        }

        // Cartesian demo post: 20 comments (8 tags × 20 comments = 160-row Cartesian product!)
        for (int c = 0; c < 20; c++)
        {
            comments.Add(new Comment
            {
                PostId = demoPost.Id,
                AuthorId = authors[c % authors.Length].Id,
                Body = $"[Demo comment #{c + 1}] {commentBodies[c % commentBodies.Length]}",
                PostedAt = baseDate.AddDays(210 + c),
            });
        }

        context.Comments.AddRange(comments);
        context.SaveChanges();
    }

    /// <summary>Returns the ID of the special "Cartesian Explosion Demo" post.</summary>
    public static int GetCartesianDemoPostId(AppDbContext context) =>
        context.Posts
            .Where(p => p.Title.Contains("Cartesian Explosion Demo"))
            .Select(p => p.Id)
            .First();
}
