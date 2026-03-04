# Dapper SQL Performance Analysis

This guide mirrors the EF Core performance analysis guide, covering the same three toolchains — SSMS, Visual Studio, and VS Code. The Dapper story is meaningfully different in a few places, so the differences are called out explicitly throughout.

---

## The Dapper Difference: You Already Have the SQL

With EF Core, Step 0 was "capture the generated SQL." With Dapper that step does not exist — **your SQL is already sitting in `SqlQueries/*.cs`**. You can open `CommentQueries.cs`, copy the CTE, and paste it straight into SSMS or the mssql extension. No logging configuration, no interception, no parameter decoding.

This is one of Dapper's core practical advantages for performance work: the thing you are analysing and the thing running in production are the same text.

**Getting parameters into your query for manual analysis:**

EF Core uses `@__param_0` style placeholders. Dapper uses `@ParamName` matching the anonymous object property names. Replace them with literals for manual testing:

```sql
-- In code (IssueQueries.GetAll):
--   WHERE (@State IS NULL OR i.State = @State)
--     AND (@Author IS NULL OR u.Username = @Author)

-- For SSMS analysis, replace with:
DECLARE @State  NVARCHAR(50)  = 'opened';
DECLARE @Author NVARCHAR(100) = NULL;
DECLARE @Label  NVARCHAR(100) = NULL;

-- then paste the full query body
```

---

## Option A — SQL Server Management Studio (SSMS)

Best for: tuning the specific SQL you own, analysing recursive CTEs, and understanding multi-join fan-out patterns.

### A1 — Statistics IO and execution plans

The workflow is identical to EF Core but faster — no log scraping step.

```sql
SET STATISTICS IO, TIME ON;

-- Paste from CommentQueries.GetThreadedByIssueId
DECLARE @IssueId INT = 1;

WITH CommentTree AS (
    SELECT c.Id, c.IssueId, c.AuthorId, c.Body,
           c.ParentCommentId, c.CreatedAt, c.UpdatedAt, 0 AS Depth
    FROM Comments c
    WHERE c.IssueId = @IssueId AND c.ParentCommentId IS NULL

    UNION ALL

    SELECT c.Id, c.IssueId, c.AuthorId, c.Body,
           c.ParentCommentId, c.CreatedAt, c.UpdatedAt, ct.Depth + 1
    FROM Comments c
    INNER JOIN CommentTree ct ON c.ParentCommentId = ct.Id
)
SELECT ct.*, u.Id AS AuthorId, u.Name AS AuthorName, u.Username AS AuthorUsername
FROM CommentTree ct
INNER JOIN Users u ON u.Id = ct.AuthorId
ORDER BY ct.Depth, ct.CreatedAt
OPTION (MAXRECURSION 100);
```

Press **Ctrl+M** before running to get the Actual Execution Plan.

### A2 — Reading a recursive CTE execution plan

Recursive CTEs produce a distinctive plan shape. Know what you are looking at:

| Operator | What it means |
|----------|---------------|
| **Clustered Index Scan on Comments** in the anchor | No index on `IssueId` — add one |
| **Table Spool (Lazy)** | The recursive join's working storage; normal and expected |
| **Assert** | Enforces `MAXRECURSION`; confirms the limit is being applied |
| **Nested Loops** in the recursive part | Fine for small trees; a large comment set will see this grow |

**The most common fix** — ensure the anchor member's filter columns are indexed:

```sql
-- Without this, every recursion scans the Comments table
CREATE INDEX IX_Comments_IssueId_ParentCommentId
ON Comments (IssueId, ParentCommentId)
INCLUDE (AuthorId, Body, CreatedAt, UpdatedAt);
```

This turns the anchor's Clustered Index Scan into a seek, and the recursive join reuses the same index on `ParentCommentId`.

### A3 — Analysing the multi-join fan-out query

`IssueQueries.GetAll` LEFT JOINs Labels, producing one row per label per issue. In SSMS, run it with actual stats:

```sql
SET STATISTICS IO, TIME ON;

DECLARE @State  NVARCHAR(50)  = 'opened';
DECLARE @Author NVARCHAR(100) = NULL;
DECLARE @Label  NVARCHAR(100) = NULL;

SELECT
    i.Id, i.GitLabId, i.Title, i.State, i.WebUrl, i.KbCategory, i.CreatedAt,
    u.Id AS UserId, u.Name AS UserName, u.Username AS UserUsername,
    l.Id AS LabelId, l.Name AS LabelName, l.Color AS LabelColor, l.Description AS LabelDescription
FROM Issues i
INNER JOIN Users u ON u.Id = i.AuthorId
LEFT JOIN IssueLabels il ON il.IssueId = i.Id
LEFT JOIN Labels l ON l.Id = il.LabelId
WHERE (@State IS NULL OR i.State = @State)
  AND (@Author IS NULL OR u.Username = @Author)
  AND (@Label IS NULL OR EXISTS (
        SELECT 1 FROM IssueLabels il2
        INNER JOIN Labels l2 ON l2.Id = il2.LabelId
        WHERE il2.IssueId = i.Id AND l2.Name = @Label
      ))
ORDER BY i.CreatedAt DESC;
```

**What to check in the plan:**

- The `EXISTS` subquery for label filtering — does it use an index seek on `LabelId` or a scan? If a scan, add an index on `Labels(Name)`.
- The `LEFT JOIN IssueLabels` — is it a Hash Match or Nested Loops? For small result sets Nested Loops is fine; for large sets consider whether `IssueLabels` has a covering index on `(IssueId)` including `LabelId`.
- Row estimate accuracy for the `@State IS NULL OR` pattern — SQL Server may not estimate well with `OR NULL` patterns; consider two separate query paths in the repository if the state filter is always provided.

### A4 — MERGE plan analysis

Dapper's `SyncService` uses `MERGE` for upserts. `MERGE` plans can be surprising:

```sql
SET STATISTICS IO, TIME ON;

MERGE Users AS target
USING (SELECT 12345 AS GitLabUserId, N'Ted Lasso' AS Name, N'ted' AS Username) AS source
    ON target.GitLabUserId = source.GitLabUserId
WHEN MATCHED     THEN UPDATE SET Name = source.Name, Username = source.Username
WHEN NOT MATCHED THEN INSERT (GitLabUserId, Name, Username)
                      VALUES (source.GitLabUserId, source.Name, source.Username)
OUTPUT INSERTED.*;
```

A well-indexed `MERGE` on a small source should show:
- A **Clustered Index Seek** on the target (using the `UQ_Users_GitLabUserId` unique index)
- A **Compute Scalar** to evaluate the MATCHED/NOT MATCHED condition
- The `OUTPUT` clause adds a minimal `Table Spool`

If you see a **Clustered Index Scan** on the target, the join column is not indexed — verify the unique constraint exists on `GitLabUserId`.

### A5 — Query Store for Dapper workloads

Query Store captures by query hash, so it works the same regardless of whether EF Core or Dapper sent the query. The Dapper difference: because you control the SQL text directly, the query hash is stable. EF Core sometimes generates slightly different SQL shapes across app restarts; Dapper's static string constants never change, giving Query Store more consistent history to work from.

```sql
-- Find the top 10 queries from Dapper by average duration
SELECT TOP 10
    qs.query_id,
    SUBSTRING(qt.query_sql_text, 1, 200) AS query_preview,
    rs.avg_duration / 1000               AS avg_ms,
    rs.avg_logical_io_reads,
    rs.count_executions
FROM sys.query_store_query            qs
JOIN sys.query_store_query_text       qt ON qt.query_text_id = qs.query_text_id
JOIN sys.query_store_plan             qp ON qp.query_id      = qs.query_id
JOIN sys.query_store_runtime_stats    rs ON rs.plan_id       = qp.plan_id
WHERE qt.query_sql_text NOT LIKE '%query_store%'
ORDER BY rs.avg_duration DESC;
```

---

## Option B — Visual Studio Profiling Tools

The key difference from EF Core: **Dapper has no built-in query logging**. There is no `LogTo()` or `LogLevel.Information` shortcut. You get what ADO.NET gives you, which is nothing by default. You have two paths: instrument the connection factory yourself, or use a profiler that hooks ADO.NET.

### B1 — Logging decorator around `IDbConnectionFactory`

The cleanest approach for development. Wrap every connection in a decorator that times and logs each command.

```csharp
// TimingDbConnection.cs — wraps IDbConnection and logs every Execute/Query
public class TimingDbConnection : IDbConnection
{
    private readonly IDbConnection _inner;
    private readonly ILogger _logger;

    public TimingDbConnection(IDbConnection inner, ILogger logger)
    {
        _inner = inner;
        _logger = logger;
    }

    public IDbCommand CreateCommand()
    {
        var cmd = _inner.CreateCommand();
        return new TimingDbCommand(cmd, _logger);
    }

    // Delegate all other IDbConnection members to _inner
    public string ConnectionString { get => _inner.ConnectionString; set => _inner.ConnectionString = value; }
    public int ConnectionTimeout => _inner.ConnectionTimeout;
    public string Database       => _inner.Database;
    public ConnectionState State => _inner.State;
    public IDbTransaction BeginTransaction()                         => _inner.BeginTransaction();
    public IDbTransaction BeginTransaction(IsolationLevel il)        => _inner.BeginTransaction(il);
    public void ChangeDatabase(string databaseName)                  => _inner.ChangeDatabase(databaseName);
    public void Close()                                              => _inner.Close();
    public void Open()                                               => _inner.Open();
    public void Dispose()                                            => _inner.Dispose();
}

// TimingDbCommand.cs — wraps IDbCommand, times ExecuteReader/ExecuteNonQuery/ExecuteScalar
public class TimingDbCommand : IDbCommand
{
    private readonly IDbCommand _inner;
    private readonly ILogger _logger;

    public TimingDbCommand(IDbCommand inner, ILogger logger)
    {
        _inner = inner;
        _logger = logger;
    }

    public IDataReader ExecuteReader()
    {
        var sw = Stopwatch.StartNew();
        var reader = _inner.ExecuteReader();
        _logger.LogInformation("Dapper query ({ms}ms): {sql}", sw.ElapsedMilliseconds, CommandText);
        return reader;
    }

    public IDataReader ExecuteReader(CommandBehavior behavior)
    {
        var sw = Stopwatch.StartNew();
        var reader = _inner.ExecuteReader(behavior);
        _logger.LogInformation("Dapper query ({ms}ms): {sql}", sw.ElapsedMilliseconds, CommandText);
        return reader;
    }

    public int ExecuteNonQuery()
    {
        var sw = Stopwatch.StartNew();
        var rows = _inner.ExecuteNonQuery();
        _logger.LogInformation("Dapper execute ({ms}ms) rows={rows}: {sql}", sw.ElapsedMilliseconds, rows, CommandText);
        return rows;
    }

    public object? ExecuteScalar()
    {
        var sw = Stopwatch.StartNew();
        var result = _inner.ExecuteScalar();
        _logger.LogInformation("Dapper scalar ({ms}ms): {sql}", sw.ElapsedMilliseconds, CommandText);
        return result;
    }

    // Delegate everything else
    public string     CommandText     { get => _inner.CommandText;     set => _inner.CommandText = value; }
    public int        CommandTimeout  { get => _inner.CommandTimeout;  set => _inner.CommandTimeout = value; }
    public CommandType CommandType    { get => _inner.CommandType;     set => _inner.CommandType = value; }
    public IDbConnection? Connection  { get => _inner.Connection;      set => _inner.Connection = value; }
    public IDataParameterCollection Parameters  => _inner.Parameters;
    public IDbTransaction? Transaction { get => _inner.Transaction;   set => _inner.Transaction = value; }
    public UpdateRowSource UpdatedRowSource { get => _inner.UpdatedRowSource; set => _inner.UpdatedRowSource = value; }
    public void Cancel()                        => _inner.Cancel();
    public IDbDataParameter CreateParameter()   => _inner.CreateParameter();
    public void Prepare()                       => _inner.Prepare();
    public void Dispose()                       => _inner.Dispose();
}
```

Register it in `Program.cs` only in Development:

```csharp
if (app.Environment.IsDevelopment())
{
    // Replace the singleton factory with a logging version
    // (remove the original registration first if using AddSingleton earlier)
    builder.Services.AddSingleton<IDbConnectionFactory>(sp =>
    {
        var inner = new SqlConnectionFactory(sp.GetRequiredService<IConfiguration>());
        var logger = sp.GetRequiredService<ILogger<TimingDbConnection>>();
        return new LoggingConnectionFactory(inner, logger);
    });
}
```

### B2 — `dotnet-trace` with ADO.NET events

Dapper sits on top of ADO.NET, so the `Microsoft-AdoNet-SystemData` provider captures every command:

```bash
dotnet tool install --global dotnet-trace

dotnet-trace collect --process-id <PID> \
  --providers "Microsoft-AdoNet-SystemData:0x1:5" \
  --output dappertrace.nettrace
```

Open `dappertrace.nettrace` in PerfView or Visual Studio. The `BeforeExecuteCommand` and `AfterExecuteCommand` events bracket each Dapper call and include the command text and connection string.

### B3 — `dotnet-counters` for connection pool health

Unlike EF Core, Dapper exposes no custom counters. The relevant metrics come from the SQL Client connection pool:

```bash
dotnet-counters monitor --process-id <PID> \
  "Microsoft.Data.SqlClient"
```

| Counter | Healthy | Warning |
|---------|---------|---------|
| `number-of-pooled-connections` | Steady state ≤ configured max | Growing unboundedly → connections not being disposed |
| `number-of-stasis-connections` | 0 | > 0 → connections held open without being returned |
| `number-of-reclaimed-connections` | 0 | > 0 → GC had to reclaim connections (missing `using`) |

A `number-of-reclaimed-connections` > 0 means somewhere a `using var connection = ...` is missing. In Dapper repositories this is almost always a `return` inside the `using` block — which is fine — or a missing `using` entirely.

### B4 — Benchmark.NET for micro-benchmarking specific queries

When choosing between two SQL approaches (e.g. CTE vs multiple queries, `dynamic` vs typed results), Benchmark.NET gives precise numbers:

```csharp
[MemoryDiagnoser]
[SimpleJob(RuntimeMoniker.Net80)]
public class CommentQueryBenchmarks
{
    private IDbConnectionFactory _factory = null!;

    [GlobalSetup]
    public void Setup()
    {
        var config = new ConfigurationBuilder()
            .AddJsonFile("appsettings.Development.json")
            .Build();
        _factory = new SqlConnectionFactory(config);
    }

    [Benchmark(Baseline = true)]
    public async Task<List<CommentDto>> CteApproach()
    {
        using var connection = _factory.CreateConnection();
        var rows = await connection.QueryAsync<dynamic>(
            CommentQueries.GetThreadedByIssueId,
            new { IssueId = 1 });
        return BuildTree(rows.ToList());
    }

    [Benchmark]
    public async Task<List<CommentDto>> TypedMapping()
    {
        using var connection = _factory.CreateConnection();
        // Compare dynamic vs strongly-typed Dapper mapping
        var rows = await connection.QueryAsync<CommentRow>(
            CommentQueries.GetThreadedByIssueId,
            new { IssueId = 1 });
        return BuildTree(rows.ToList());
    }
}
```

Run with: `dotnet run -c Release --project Benchmarks`

---

## Option C — VS Code

### C1 — mssql extension (same as EF Core, but simpler workflow)

Since Dapper queries are static strings in source files, the workflow is:

1. Open `src/KnowledgeBase.Data/SqlQueries/CommentQueries.cs`
2. Select the SQL string body (everything inside the `""" ... """`)
3. Copy into a new `.sql` file
4. Replace `@IssueId` with `DECLARE @IssueId INT = 1;` at the top
5. **Ctrl+Shift+E** to execute, click the plan tab

No log tailing, no parameter decoding from EF output.

### C2 — MiniProfiler with Dapper (its original home)

MiniProfiler was created by the Stack Overflow team alongside Dapper — the two were designed to work together. Dapper integration requires wrapping the connection, which MiniProfiler does automatically.

**Install:**

```bash
dotnet add src/KnowledgeBase.Api package MiniProfiler.AspNetCore.Mvc
dotnet add src/KnowledgeBase.Api package MiniProfiler.Integrations.SqlClientCore
```

**Modify `SqlConnectionFactory.cs`** to return a profiled connection:

```csharp
using StackExchange.Profiling.Data;

public class SqlConnectionFactory : IDbConnectionFactory
{
    private readonly string _connectionString;
    private readonly IWebHostEnvironment _env;

    public SqlConnectionFactory(IConfiguration config, IWebHostEnvironment env)
    {
        _connectionString = config.GetConnectionString("Default")!;
        _env = env;
    }

    public IDbConnection CreateConnection()
    {
        var connection = new SqlConnection(_connectionString);

        // Wrap in profiled connection in Development so MiniProfiler sees Dapper calls
        if (_env.IsDevelopment())
            return new ProfiledDbConnection(connection, MiniProfiler.Current);

        return connection;
    }
}
```

**Wire up in `Program.cs`:**

```csharp
builder.Services.AddMiniProfiler(opts =>
{
    opts.RouteBasePath = "/profiler";
    opts.ColorScheme = StackExchange.Profiling.ColorScheme.Dark;
    // No .AddEntityFramework() needed — Dapper goes through the wrapped connection
});

app.UseMiniProfiler();
```

Hit `http://localhost:5000/profiler/results-index` after any request. MiniProfiler shows:
- Query count per request (the fastest way to spot N+1)
- Individual query duration
- The exact SQL sent (including the CTE and MERGE statements)
- Duplicate query detection with a warning badge

**MiniProfiler is particularly valuable for Dapper because it highlights hand-rolled N+1 patterns** — places where you looped over a result set and issued one query per row rather than batching.

### C3 — Structured logging with Serilog + Seq

For a more persistent query log than console output:

```bash
dotnet add src/KnowledgeBase.Api package Serilog.AspNetCore
dotnet add src/KnowledgeBase.Api package Serilog.Sinks.Seq
```

Use the `TimingDbCommand` decorator from B1 but log structured events:

```csharp
_logger.LogInformation(
    "DapperQuery {Sql} completed in {ElapsedMs}ms affecting {Rows} rows",
    CommandText, sw.ElapsedMilliseconds, rowsAffected);
```

Run Seq locally:

```bash
docker run -d -p 5341:80 datalust/seq
```

Open `http://localhost:5341` → filter by `DapperQuery` → sort by `ElapsedMs` descending → see your slowest queries with full SQL and context.

### C4 — OpenTelemetry with manual Dapper spans

EF Core instruments itself automatically. Dapper does not — you add spans manually where it matters. The `TimingDbCommand` decorator is the right place to add them:

```bash
dotnet add src/KnowledgeBase.Api package OpenTelemetry.Extensions.Hosting
dotnet add src/KnowledgeBase.Api package OpenTelemetry.Exporter.Jaeger
```

```csharp
// In Program.cs
builder.Services.AddOpenTelemetry()
    .WithTracing(tracing => tracing
        .AddAspNetCoreInstrumentation()
        // No AddEntityFrameworkCoreInstrumentation — we instrument Dapper manually
        .AddSource("KnowledgeBase.Dapper")   // custom ActivitySource name
        .AddJaegerExporter());
```

```csharp
// In TimingDbCommand.ExecuteReader()
private static readonly ActivitySource _activity = new("KnowledgeBase.Dapper");

public IDataReader ExecuteReader(CommandBehavior behavior)
{
    using var span = _activity.StartActivity("dapper.query", ActivityKind.Client);
    span?.SetTag("db.statement", CommandText);
    span?.SetTag("db.system", "mssql");

    var sw = Stopwatch.StartNew();
    var reader = _inner.ExecuteReader(behavior);
    span?.SetTag("dapper.duration_ms", sw.ElapsedMilliseconds);

    return reader;
}
```

In Jaeger (`http://localhost:16686`) you now see each Dapper query as a span nested inside the HTTP request — the same view as EF Core's automatic instrumentation, just wired up manually.

---

## Dapper-Specific Performance Patterns

### Buffered vs unbuffered queries

By default, `QueryAsync<T>` buffers the entire result set into a `List<T>` before returning. For large result sets (thousands of rows) this holds memory and delays the first byte.

```csharp
// Default — buffers everything, fine for typical API responses
var issues = await connection.QueryAsync<Issue>(sql, param);

// Unbuffered — streams rows, lower memory, start processing immediately
// Use for exports, batch jobs, or result sets > ~10k rows
var issues = connection.Query<Issue>(sql, param, buffered: false);
foreach (var issue in issues)
    ProcessIssue(issue);   // streaming — does not materialise the whole set
```

### `dynamic` vs typed mapping

`QueryAsync<dynamic>` avoids defining a type but has measurable overhead. For hot paths, map to a concrete type:

```csharp
// Slower — dynamic uses DapperRow with dictionary-based property access
var rows = await connection.QueryAsync<dynamic>(sql, param);

// Faster — Dapper generates IL to map directly to properties by name
var rows = await connection.QueryAsync<IssueSummaryRow>(sql, param);
```

For the fan-out queries (multiple rows per entity), a small intermediate record type keeps the mapping fast and avoids `(int)r.Id` casts throughout:

```csharp
private record IssueSummaryRow(
    int Id, int GitLabId, string Title, string State, string WebUrl, string? KbCategory, DateTime CreatedAt,
    int UserId, string UserName, string UserUsername,
    int? LabelId, string? LabelName, string? LabelColor, string? LabelDescription
);
```

### `QueryMultiple` to eliminate round trips

When `GetById` needs the issue plus its labels plus its comment count, three round trips is three times the latency. `QueryMultiple` sends all three queries in one batch:

```csharp
public const string GetByIdMultiple = """
    SELECT i.*, u.Id AS UserId, u.Name AS UserName, u.Username AS UserUsername
    FROM Issues i JOIN Users u ON u.Id = i.AuthorId
    WHERE i.Id = @Id;

    SELECT l.Id, l.Name, l.Color, l.Description
    FROM Labels l JOIN IssueLabels il ON il.LabelId = l.Id
    WHERE il.IssueId = @Id;

    SELECT COUNT(*) FROM Comments WHERE IssueId = @Id;
    """;

using var multi = await connection.QueryMultipleAsync(GetByIdMultiple, new { Id = id });
var issue        = await multi.ReadSingleOrDefaultAsync<IssueRow>();
var labels       = (await multi.ReadAsync<LabelDto>()).ToList();
var commentCount = await multi.ReadSingleAsync<int>();
```

Three queries, one network round trip, one `SqlCommand` execution.

### Hand-rolled N+1 (the Dapper trap)

EF Core's N+1 is usually caused by lazy loading. Dapper's N+1 is always caused by a loop in your repository code:

```csharp
// Bad — one query per issue to load its labels
var issues = await connection.QueryAsync<Issue>("SELECT * FROM Issues");
foreach (var issue in issues)
{
    issue.Labels = (await connection.QueryAsync<Label>(
        "SELECT l.* FROM Labels l JOIN IssueLabels il ON ... WHERE il.IssueId = @Id",
        new { Id = issue.Id })).ToList();
}

// Good — one query with LEFT JOIN, collapse in C#
var rows = await connection.QueryAsync<dynamic>(IssueQueries.GetAll, param);
// ... dictionary-based collapsing (as implemented in IssueRepository)
```

MiniProfiler's duplicate query detector is the fastest way to catch this in development.

### Watching connection pool pressure

Dapper connections are short-lived by design (`using var connection = ...`). If you see pool pressure — timeouts waiting for a connection — the first place to check is repositories that open but do not close connections promptly:

```csharp
// Fine — connection disposed at end of method
public async Task<IEnumerable<UserDto>> GetAllAsync(CancellationToken ct)
{
    using var connection = _connectionFactory.CreateConnection();
    return await connection.QueryAsync<UserDto>(UserQueries.GetAll);
}

// Problem — connection stays open for the lifetime of the IEnumerable enumeration
// if buffered: false and the caller is slow
public IAsyncEnumerable<UserDto> StreamAll()
{
    var connection = _connectionFactory.CreateConnection();  // no using!
    // ... connection never disposed if caller abandons enumeration
}
```

---

## Quick Decision Guide

| Goal | Best tool |
|------|-----------|
| Analyse a specific query you just wrote | Paste directly into SSMS + Ctrl+M (A1) |
| Tune the recursive CTE comment query | SSMS Actual Execution Plan — look for Index Scan in anchor (A2) |
| Tune the multi-join fan-out query | SSMS Statistics IO — logical reads on IssueLabels and Labels (A3) |
| Audit MERGE plan quality | SSMS — verify Index Seek on target, not Scan (A4) |
| Track regressions over time | Query Store (A5) |
| See queries alongside request timing | MiniProfiler (C2) — Dapper's native companion |
| Catch hand-rolled N+1 in development | MiniProfiler duplicate query badge (C2) |
| Structured query log with search | Serilog + Seq (C3) |
| Distributed traces HTTP + Dapper spans | OpenTelemetry + Jaeger, manual spans (C4) |
| Compare two SQL approaches precisely | Benchmark.NET (B4) |
| Connection pool health | `dotnet-counters` SqlClient metrics (B3) |
| Stream large result sets | Switch to `buffered: false` + check memory in Diagnostic Tools (B1) |
