# EF Core → Dapper: Antipattern Workshop

An interactive ASP.NET Core 10 application demonstrating common EF Core performance
antipatterns, their EF-native fixes, and how the same problems look (or disappear)
when migrated to Dapper.

---

## Prerequisites

- [.NET 10 SDK](https://dotnet.microsoft.com/download/dotnet/10.0)
- An HTTP client (Swagger UI is built in — no extra tooling needed)

---

## Running the App

```bash
cd src/EfToDapper.Api
dotnet run
```

Then open your browser:

| Tool | URL |
|------|-----|
| **Swagger UI** | http://localhost:5000 |
| **MiniProfiler** | http://localhost:5000/profiler/results-index |

The SQL Server LocalDB database is seeded automatically on startup with:
- 5 authors
- 30 posts (with realistic body text)
- 8 tags
- ~300 comments
- 1 special "Cartesian Demo" post with **8 tags + 20 comments** (8×20 = 160-row explosion)

---

## Running the Tests

```bash
cd src/EfToDapper.Tests
dotnet test
```

Tests verify: correct seeding counts, N+1 query counts, ChangeTracker state,
SQL projection correctness, and pagination behavior.

---

## Workshop Scenarios

### Scenario 1 — N+1 Query Problem
**`/api/scenario1/n-plus-one`**

| Endpoint | What happens |
|----------|-------------|
| `GET /antipattern` | Loads 30 posts, then issues **1 SELECT per post** to find the Author → 31 total queries |
| `GET /fixed-ef` | Uses `.Include(p => p.Author)` → **1 JOIN query** |

**MiniProfiler tells the story:** hit `/antipattern`, then `/profiler/results-index`.
You'll see ~31 identical `SELECT ... FROM Authors WHERE Id = @p0` statements.

**Root cause:** Accessing a navigation property (`.Author.Name`) that wasn't loaded
with `Include()`.  With lazy loading proxies this is even more insidious because
the extra queries are invisible in code.

**EF Fix:**
```csharp
// Before (N+1):
var posts = await db.Posts.ToListAsync();
foreach (var post in posts) { var author = await db.Authors.FindAsync(post.AuthorId); }

// After (1 query):
var posts = await db.Posts.Include(p => p.Author).ToListAsync();
```

---

### Scenario 2 — Cartesian Explosion
**`/api/scenario2/cartesian-explosion`**

| Endpoint | What happens |
|----------|-------------|
| `GET /demo-id` | Returns the ID of the special demo post |
| `GET /{id}/antipattern` | Two collection Includes → **8 × 20 = 160 rows** transferred |
| `GET /{id}/fixed-ef` | `AsSplitQuery()` → **3 queries, 29 rows** |

**Why it matters:** EF Core's multi-collection `Include()` produces a SQL JOIN where
every tag row is duplicated for every comment row.  With 50 tags and 1,000 comments:
50,000 rows instead of 1,050.

**EF Fix:**
```csharp
// Before (Cartesian):
db.Posts.Include(p => p.Tags).Include(p => p.Comments)

// After (3 separate queries):
db.Posts.Include(p => p.Tags).Include(p => p.Comments).AsSplitQuery()
```

---

### Scenario 3 — Change Tracker Overhead
**`/api/scenario3/change-tracker`**

| Endpoint | TrackedEntityCount in response |
|----------|-------------------------------|
| `GET /antipattern` | 30 — all posts tracked with state snapshots |
| `GET /fixed-ef` | 0 — no snapshots allocated |

**Why it matters:** EF Core snapshots every loaded entity to enable dirty-check on
`SaveChanges()`.  For read-only endpoints (reports, lists, API responses) this work
is wasted — extra memory allocation and GC pressure proportional to result set size.

**EF Fix:**
```csharp
// Before (tracked):
var posts = await db.Posts.ToListAsync();

// After (no snapshots):
var posts = await db.Posts.AsNoTracking().ToListAsync();
```

> **Dapper advantage:** Dapper never uses a ChangeTracker — all reads are
> inherently untracked, so this problem simply doesn't exist.

---

### Scenario 4 — Key Lookup / Missing Covering Index
**`/api/scenario4/key-lookup`**

| Endpoint | What it shows |
|----------|--------------|
| `GET /authors` | Lists authors with their emails |
| `GET /by-email/antipattern?email=...` | Query on un-indexed `Email` column |
| `GET /by-email/fixed-ef?email=...` | Same query — fix is in `AppDbContext.cs` |

**Why it matters:** In SQL Server, a non-indexed column lookup produces:
1. **Clustered Index Scan** — reads the entire table
2. **Key Lookup** — follows a pointer back to fetch remaining columns

A **covering index** stores the additional columns in the index leaf pages, eliminating
the key lookup entirely.

**EF Fix (in `AppDbContext.OnModelCreating`):**
```csharp
// Option A — simple index (prevents table scan):
modelBuilder.Entity<Author>()
    .HasIndex(a => a.Email)
    .HasDatabaseName("IX_Authors_Email");

// Option B — covering index (also eliminates key lookup):
modelBuilder.Entity<Author>()
    .HasIndex(a => a.Email)
    .IncludeProperties(a => new { a.Name, a.Bio })
    .HasDatabaseName("IX_Authors_Email_Covering");
```

Uncomment Option B in `AppDbContext.cs`, restart the app, and re-run the query.
In SQL Server you can verify with: SSMS Actual Execution Plan or `SET STATISTICS IO ON`

---

### Scenario 5 — Over-fetching / Missing Projection
**`/api/scenario5/over-fetching`**

| Endpoint | SQL generated |
|----------|--------------|
| `GET /post-titles/antipattern` | `SELECT Id, Title, **Body**, Status, ViewCount, ...` |
| `GET /post-titles/fixed-ef` | `SELECT Id, Title` only |
| `GET /all-posts/antipattern` | All posts + all navigations, no pagination |
| `GET /all-posts/fixed-ef` | Paginated, projected, AsNoTracking |

Check the `SqlPreview` field in each response to see the difference in generated SQL.

**EF Fix:**
```csharp
// Before (SELECT *):
var posts = await db.Posts.ToListAsync();

// After (only the columns you need):
var titles = await db.Posts
    .Select(p => new { p.Id, p.Title })
    .ToListAsync();
```

**Dapper advantage:** With Dapper you write the SQL yourself — there is no
`SELECT *` by default.  You only get what you select.

---

### Scenario 6 — Compiled Query Overhead
**`/api/scenario6/compiled-query`**

| Endpoint | What happens |
|----------|-------------|
| `GET /{status}/antipattern` | EF Core translates LINQ to SQL on every call |
| `GET /{status}/fixed-ef` | Pre-compiled query — translation happens once at startup |

**When to use:** High-throughput endpoints (> 1,000 req/s) where the LINQ translation
overhead (~1-5ms) accumulates.

**EF Fix:**
```csharp
// Compiled once at class initialization:
private static readonly Func<AppDbContext, string, IAsyncEnumerable<MyDto>>
    MyCompiledQuery = EF.CompileAsyncQuery(
        (AppDbContext ctx, string status) =>
            ctx.Posts.Where(p => p.Status == status).Select(p => new MyDto { ... }));

// Zero translation overhead at call time:
await foreach (var item in MyCompiledQuery(db, "Published")) { ... }
```

---

## Dapper Exercises

**`/api/dapper`** — All endpoints return `501 Not Implemented` until you fill them in.

### Workflow

1. Open [src/EfToDapper.Data/Dapper/DapperQueries.cs](src/EfToDapper.Data/Dapper/DapperQueries.cs)
2. Find the method for the exercise you're working on
3. Read the SQL hint in the comments
4. Replace `throw new NotImplementedException(...)` with working Dapper code
5. Call the corresponding `/api/dapper/...` endpoint to test
6. Compare results with the `/fixed-ef` endpoint for the same scenario

### Exercise Reference

| Exercise | Dapper Method | Compares With |
|----------|--------------|---------------|
| 1 — N+1 fix | `GetPostSummaries()` | `/api/scenario1/.../fixed-ef` |
| 2 — Cartesian fix | `GetPostDetails(postId)` | `/api/scenario2/.../fixed-ef` |
| 3 — No change tracker | `GetPostsForReport()` | `/api/scenario3/.../fixed-ef` |
| 4 — Key lookup | `GetAuthorByEmail(email)` | `/api/scenario4/.../fixed-ef` |
| 5 — Projection | `GetPostTitles()` | `/api/scenario5/.../fixed-ef` |
| 6 — Pagination | `GetPostsPaged(page, size)` | `/api/scenario5/all-posts/fixed-ef` |

---

## MiniProfiler Usage

After hitting any endpoint:
1. Navigate to `/profiler/results-index`
2. Click the most recent request
3. Expand "sql" to see every SQL statement, its parameters, and execution time

The **N+1 scenario** is the most dramatic — you'll see the same `SELECT FROM Authors WHERE Id = @p0`
statement listed ~30 times.

MiniProfiler captures EF Core queries via `DiagnosticSource` integration
(`AddEntityFramework()` in `Program.cs`).

---

## Project Structure

```
ef-to-dapper/
├── EfToDapper.sln
└── src/
    ├── EfToDapper.Core/                 # Persistence-ignorant domain + DTOs
    │   ├── Domain/                      # Author, Post, Comment, Tag, PostTag
    │   └── DTOs/                        # PostSummaryDto, PostDetailDto, ScenarioResponse<T>
    │
    ├── EfToDapper.Data/                 # Data access implementations
    │   ├── Context/AppDbContext.cs      # SQL Server config + index comments
    │   ├── Context/DataSeeder.cs        # Deterministic seed data
    │   ├── Interceptors/                # QueryCounter + QueryCountingInterceptor
    │   └── Dapper/                      # DapperConnectionFactory + DapperQueries (stubs)
    │
    ├── EfToDapper.Api/                  # ASP.NET Core Web API
    │   ├── Program.cs                   # DI, SQL Server LocalDB, MiniProfiler, Swagger
    │   └── Controllers/
    │       ├── NPlusOneController.cs           # Scenario 1
    │       ├── CartesianExplosionController.cs  # Scenario 2
    │       ├── ChangeTrackerController.cs       # Scenario 3
    │       ├── KeyLookupController.cs           # Scenario 4
    │       ├── OverFetchingController.cs        # Scenario 5
    │       ├── CompiledQueryController.cs       # Scenario 6
    │       └── DapperController.cs             # Exercise stubs
    │
    └── EfToDapper.Tests/                # xUnit test suite
        ├── Infrastructure/TestDbContextFactory.cs
        ├── SeedingTests.cs              # Verify seeded data shape
        └── ScenarioTests.cs             # Verify antipattern vs. fix behavior
```

---

## Key Concepts Summary

| Antipattern | Detection | EF Fix | Dapper Advantage |
|-------------|-----------|--------|-----------------|
| N+1 | MiniProfiler query list | `.Include()` / projection | Always explicit JOIN in SQL |
| Cartesian Explosion | Row count × collections | `.AsSplitQuery()` | Two queries + manual stitch |
| Change Tracker Overhead | `ChangeTracker.Entries().Count()` | `.AsNoTracking()` | No ChangeTracker exists |
| Key Lookup | SQL Server execution plan | Covering index in `OnModelCreating` | Same — index is schema concern |
| Over-fetching | `ToQueryString()` shows `SELECT *` | `.Select()` projection | You write only what you need |
| Hot Path Translation | Profiling / throughput tests | `EF.CompileAsyncQuery()` | SQL is already a constant |
