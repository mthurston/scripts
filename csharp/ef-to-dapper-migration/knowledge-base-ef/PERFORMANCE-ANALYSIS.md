# EF Core SQL Performance Analysis

This guide covers three toolchains for capturing and analysing the SQL that EF Core generates for this application. Each section goes from "first thing to do" to "advanced diagnostics".

---

## Step 0 — Capture the SQL EF Core is Generating

Before using any external tool you need to see the actual SQL. EF Core has several built-in ways to expose it.

### Option A — `appsettings.Development.json` (zero code change)

The simplest approach. Flip the log level for EF Core's command logger and all queries print to the console (or whatever logger sink you have configured).

```json
{
  "Logging": {
    "LogLevel": {
      "Microsoft.EntityFrameworkCore.Database.Command": "Information"
    }
  }
}
```

Output looks like:

```
info: Microsoft.EntityFrameworkCore.Database.Command[20101]
      Executed DbCommand (12ms) [Parameters=[@__id_0='?' (DbType = Int32)], CommandType='Text', CommandTimeout='30']
      SELECT i.Id, i.GitLabId, i.Title ...
      FROM Issues AS i
      WHERE i.Id = @__id_0
```

### Option B — Code-level logging with sensitive data

For a quick debugging session, configure logging directly on the `DbContext`. Do **not** leave this on in production — `EnableSensitiveDataLogging` exposes parameter values in plain text.

```csharp
// appsettings.Development.json approach is preferred;
// use this only for ad-hoc local debugging.
builder.Services.AddDbContext<KnowledgeBaseDbContext>(opts =>
    opts
        .UseSqlServer(builder.Configuration.GetConnectionString("Default"))
        .LogTo(Console.WriteLine, LogLevel.Information)
        .EnableSensitiveDataLogging()          // shows actual parameter values
        .EnableDetailedErrors());              // more descriptive exceptions
```

### Option C — Intercept queries at runtime

For programmatic capture (e.g. in a test or benchmark), use `ToQueryString()`:

```csharp
// Inspect the SQL for any IQueryable without executing it
var query = _context.Issues
    .Include(i => i.Author)
    .Where(i => i.State == "opened");

string sql = query.ToQueryString();   // EF Core 5+
Console.WriteLine(sql);
```

---

## Option A — SQL Server Management Studio (SSMS)

Best for: deep execution plan analysis, index tuning, and Query Store investigation.

### A1 — Paste-and-run in SSMS

1. Capture the SQL from the EF Core log (Step 0 above).
2. Open SSMS → connect to your LocalDB or SQL Server instance.
3. Paste the query. Replace EF's `@__param_0` placeholders with literal values.
4. Before running, enable runtime statistics:

```sql
SET STATISTICS IO, TIME ON;
SET STATISTICS PROFILE ON;   -- shows row-by-row operator stats

-- paste your EF-generated query here
SELECT i.Id, i.Title ...
FROM Issues AS i
...
```

**Reading `STATISTICS IO` output:**

```
Table 'Issues'. Scan count 1, logical reads 847, ...
```

- **Logical reads** — pages read from the buffer pool. High number = likely missing index or too-wide a scan. This is the single most useful number for query tuning.
- **Physical reads** — pages read from disk (cache cold); high on first run, near zero after.
- **CPU time / elapsed time** — wall-clock cost.

### A2 — Actual Execution Plan

In SSMS, press **Ctrl+M** (Include Actual Execution Plan) then run the query. The plan tab shows:

| What to look for | What it means |
|------------------|---------------|
| **Table Scan** on a large table | No usable index — add one |
| **Key Lookup** | Index found the row but had to fetch extra columns from the heap — add those columns to an `INCLUDE` clause |
| **Sort** with a thick arrow | Large intermediate sort; consider an index that covers the `ORDER BY` |
| **Hash Match** | Join with no index on the probe side |
| **Thick arrows between operators** | Large row estimates flowing through; hover to see estimated vs actual row count — a big mismatch means stale statistics |

**Missing index hints** appear in green text at the top of the plan. Right-click → Script to generate the `CREATE INDEX` statement. Evaluate carefully before applying — SSMS is enthusiastic about recommending indexes.

### A3 — Query Store (SQL Server 2016+)

Query Store records every query plan over time. It is the best tool for answering "did this query get slower after a deployment or a data growth event?"

**Enable it on your database:**

```sql
ALTER DATABASE KnowledgeBaseDb
SET QUERY_STORE = ON (
    OPERATION_MODE = READ_WRITE,
    MAX_STORAGE_SIZE_MB = 100,
    INTERVAL_LENGTH_MINUTES = 60
);
```

**Access it in SSMS:** expand your database → **Query Store** node → open **Top Resource Consuming Queries**.

Useful views:

```sql
-- Top 10 queries by average logical reads in the last hour
SELECT TOP 10
    qs.query_id,
    qt.query_sql_text,
    rs.avg_logical_io_reads,
    rs.avg_duration,
    rs.count_executions
FROM sys.query_store_query       qs
JOIN sys.query_store_query_text  qt ON qt.query_text_id = qs.query_text_id
JOIN sys.query_store_plan        qp ON qp.query_id      = qs.query_id
JOIN sys.query_store_runtime_stats rs ON rs.plan_id     = qp.plan_id
ORDER BY rs.avg_logical_io_reads DESC;
```

You can also **force a plan** from the GUI — useful when a regression is caused by plan choice, not the query itself.

### A4 — Extended Events (modern Profiler replacement)

SQL Server Profiler is deprecated. Use Extended Events to trace EF Core queries from a running application.

```sql
-- Create a lightweight XE session that captures slow queries
CREATE EVENT SESSION [EFCoreSlow] ON SERVER
ADD EVENT sqlserver.sql_statement_completed (
    ACTION (sqlserver.sql_text, sqlserver.database_name, sqlserver.client_app_name)
    WHERE  sqlserver.duration > 500000          -- > 500 ms (in microseconds)
      AND  sqlserver.database_name = N'KnowledgeBaseDb'
)
ADD TARGET package0.ring_buffer (SET max_memory = 51200)
WITH (STARTUP_STATE = OFF);

ALTER EVENT SESSION [EFCoreSlow] ON SERVER STATE = START;
```

To view results:

```sql
SELECT
    xdr.value('(event/@name)[1]',         'nvarchar(100)')  AS event_name,
    xdr.value('(event/@timestamp)[1]',    'datetime2')       AS event_time,
    xdr.value('(event/data[@name="duration"]/value)[1]', 'bigint') / 1000 AS duration_ms,
    xdr.value('(event/action[@name="sql_text"]/value)[1]', 'nvarchar(max)') AS sql_text
FROM (
    SELECT CAST(target_data AS XML) AS target_data
    FROM   sys.dm_xe_sessions s
    JOIN   sys.dm_xe_session_targets t ON t.event_session_address = s.address
    WHERE  s.name = 'EFCoreSlow'
      AND  t.target_name = 'ring_buffer'
) AS data
CROSS APPLY target_data.nodes('RingBufferTarget/event') AS xdr (xdr)
ORDER BY event_time DESC;

-- Clean up when done
ALTER EVENT SESSION [EFCoreSlow] ON SERVER STATE = STOP;
DROP EVENT SESSION [EFCoreSlow] ON SERVER;
```

---

## Option B — Visual Studio Profiling Tools

Best for: correlating slow EF queries with specific controller actions, spotting N+1 patterns, and memory/CPU profiling alongside database work.

### B1 — Diagnostic Tools Window (built in)

Run the application under the debugger (**F5**). The **Diagnostic Tools** window opens automatically (Debug → Windows → Diagnostic Tools).

- The **Events** tab shows every EF Core query logged at `Information` level, with its duration, as a timeline event. Click an event to jump to that point in the CPU/memory graphs.
- The **Memory Usage** tab lets you take heap snapshots — useful if `DbContext` is being kept alive too long (a common cause of memory growth in EF Core apps).

> Tip: The Events tab only shows EF commands if the `Microsoft.EntityFrameworkCore.Database.Command` log level is `Information` or lower — same setting as Option A1 above.

### B2 — .NET Async Profiler / CPU Sampling

For production-like load testing under Visual Studio:

1. **Debug → Performance Profiler** (Alt+F2).
2. Select **CPU Usage** and **.NET Async** together.
3. Run a realistic workload against the API (e.g. Postman collection or `k6` script).
4. Stop collection → the call-tree view shows which repository methods and EF Core internals consumed the most time.

Look for:
- `Microsoft.EntityFrameworkCore.Query.Internal.*` — EF's LINQ translator
- `Microsoft.EntityFrameworkCore.ChangeTracking.*` — unexpected change-tracker overhead (fix: add `AsNoTracking()` to read-only queries)
- Your own `*Repository.*` methods showing disproportionate time

### B3 — `dotnet-trace` (cross-platform, works without Visual Studio)

```bash
# Install once
dotnet tool install --global dotnet-trace

# Attach to a running API process (find PID first)
dotnet-trace collect --process-id <PID> \
  --providers Microsoft-EntityFrameworkCore \
  --output eftrace.nettrace
```

Open `eftrace.nettrace` in Visual Studio's **PerfView** or directly in Visual Studio (File → Open → Performance Trace). You get the same call tree and event timeline without needing the project loaded.

### B4 — `dotnet-counters` (live metrics)

```bash
dotnet tool install --global dotnet-counters

# Watch EF Core counters live while the app is running
dotnet-counters monitor --process-id <PID> \
  Microsoft.EntityFrameworkCore
```

Key counters to watch:

| Counter | What a high value means |
|---------|------------------------|
| `active-db-contexts` | DbContext instances alive — should track request count, not grow unboundedly |
| `total-queries` | Total query executions |
| `queries-per-second` | Throughput |
| `total-save-changes` | Write operations |
| `compiled-query-cache-hit-rate` | Low rate = lots of unique query shapes; consider Compiled Queries |

---

## Option C — VS Code

Best for: developers who live in VS Code, quick query analysis without launching SSMS, and integration with EF Core's own tooling.

### C1 — SQL Server (mssql) Extension

Install the **SQL Server (mssql)** extension (published by Microsoft).

1. **Ctrl+Shift+P** → `MS SQL: Connect` → choose your LocalDB connection.
2. Open a `.sql` file, paste your EF-generated query.
3. **Ctrl+Shift+E** to execute.
4. In the results pane, click the **Execution Plan** tab (the chart icon) — VS Code renders an interactive graphical plan, equivalent to SSMS's plan viewer.

You can also run `STATISTICS IO`:

```sql
SET STATISTICS IO, TIME ON;
-- your query
```

The Messages tab shows the IO output alongside the results.

### C2 — `appsettings.Development.json` + integrated terminal

The cheapest approach: run `dotnet run` in VS Code's integrated terminal with EF logging on (C0 above). Every query prints inline with your normal output. Pipe through `grep` to focus:

```bash
dotnet run --project src/KnowledgeBase.Api 2>&1 | grep -A 5 "Executed DbCommand"
```

### C3 — MiniProfiler (in-process query profiler)

MiniProfiler adds a lightweight profiling UI to the API itself. Every request shows query count, per-query duration, and the SQL — inside the Swagger UI or as a JSON endpoint.

**Install:**

```bash
dotnet add src/KnowledgeBase.Api package MiniProfiler.AspNetCore.Mvc
dotnet add src/KnowledgeBase.Api package MiniProfiler.EntityFrameworkCore
```

**Wire up in `Program.cs`:**

```csharp
builder.Services.AddMiniProfiler(opts =>
{
    opts.RouteBasePath = "/profiler";   // profiler UI at /profiler/results-index
    opts.ColorScheme = StackExchange.Profiling.ColorScheme.Dark;
}).AddEntityFramework();               // automatically wraps all EF commands

// ...

app.UseMiniProfiler();                 // place before MapControllers
```

Then hit `http://localhost:5000/profiler/results-index` after any request to see:
- Total time per request
- Number of queries executed (**the key indicator of N+1**)
- Each query's SQL and duration
- Duplicate query detection

### C4 — OpenTelemetry traces (structured observability)

For a more production-grade setup, EF Core has first-class OpenTelemetry support. Add Jaeger or Zipkin locally and see distributed traces with database spans.

**Install:**

```bash
dotnet add src/KnowledgeBase.Api package OpenTelemetry.Extensions.Hosting
dotnet add src/KnowledgeBase.Api package OpenTelemetry.Instrumentation.EntityFrameworkCore --prerelease
dotnet add src/KnowledgeBase.Api package OpenTelemetry.Exporter.Jaeger
```

**Wire up:**

```csharp
builder.Services.AddOpenTelemetry()
    .WithTracing(tracing => tracing
        .AddAspNetCoreInstrumentation()
        .AddEntityFrameworkCoreInstrumentation(opts =>
            opts.SetDbStatementForText = true)   // include SQL in spans
        .AddJaegerExporter());
```

Run Jaeger locally via Docker:

```bash
docker run -d -p 16686:16686 -p 4317:4317 jaegertracing/all-in-one
```

Open `http://localhost:16686` → find traces for `GET /api/issues/{id}` → expand the EF Core span to see the exact SQL, its duration, and how it nests within the full HTTP request lifecycle.

---

## Common EF Core Performance Patterns to Look For

Once you have the SQL captured, these are the patterns most commonly found in EF Core applications.

### N+1 queries

**Symptom:** MiniProfiler shows 50 queries for a page that returns 50 issues. XE session shows a burst of near-identical `SELECT ... WHERE Id = N` statements.

**Cause:** Accessing a navigation property inside a loop without `Include()`.

```csharp
// Bad — one query per issue to load Author
var issues = await _context.Issues.ToListAsync();
foreach (var i in issues)
    Console.WriteLine(i.Author.Name);   // lazy load fires here, N+1

// Fixed — one query with JOIN
var issues = await _context.Issues.Include(i => i.Author).ToListAsync();
```

### Cartesian explosion

**Symptom:** A query returns far more rows than expected. Logical reads are disproportionately high. MiniProfiler shows one query but with a huge result set.

**Cause:** Two or more `Include()` calls on collection navigations produce a cross-join effect.

```csharp
// Risky for large related collections — IssueLabels × Comments rows
var issue = await _context.Issues
    .Include(i => i.IssueLabels).ThenInclude(il => il.Label)
    .Include(i => i.Comments)
    .FirstOrDefaultAsync(i => i.Id == id);
```

**Fix:** Use `AsSplitQuery()` to issue separate queries per collection (EF Core 5+):

```csharp
var issue = await _context.Issues
    .Include(i => i.IssueLabels).ThenInclude(il => il.Label)
    .Include(i => i.Comments)
    .AsSplitQuery()                         // 3 queries instead of 1 wide join
    .FirstOrDefaultAsync(i => i.Id == id);
```

### Change tracker overhead on read-only paths

**Symptom:** CPU profiler shows significant time in `ChangeTracker` internals for list endpoints that never write.

**Fix:** Add `AsNoTracking()` to all read-only queries. It eliminates identity-map lookups and snapshot creation.

```csharp
var query = _context.Issues
    .AsNoTracking()                         // skip the change tracker entirely
    .Include(i => i.Author)
    .Where(i => i.State == stateFilter);
```

### Missing covering index (Key Lookup)

**Symptom:** Execution plan shows an Index Seek followed by a Key Lookup (the bookmark lookup back to the clustered index). The Key Lookup appears thick — many rows flowing through it.

**Fix:** Add the extra columns to the index as `INCLUDE`:

```sql
-- Before: index only covers the seek predicate
CREATE INDEX IX_Issues_State ON Issues (State);

-- After: index also covers the columns the query projects, eliminating the lookup
CREATE INDEX IX_Issues_State ON Issues (State)
INCLUDE (Title, AuthorId, WebUrl, CreatedAt, KbCategory);
```

In EF Core, hint at an index with `HasIndex(...).IncludeProperties(...)` in your configuration:

```csharp
builder.HasIndex(i => i.State)
       .IncludeProperties(i => new { i.Title, i.AuthorId, i.WebUrl, i.CreatedAt });
```

---

## Quick Decision Guide

| Goal | Best tool |
|------|-----------|
| See what SQL EF is generating right now | `appsettings.Development.json` log level + console |
| Find N+1 problems during development | MiniProfiler (C3) |
| Tune a specific slow query | SSMS Actual Execution Plan (A2) |
| Investigate a performance regression over time | Query Store (A3) |
| Profile CPU/memory alongside queries | Visual Studio Diagnostic Tools (B1) or `dotnet-trace` (B3) |
| Monitor live query throughput | `dotnet-counters` (B4) |
| Capture slow queries from a running app without code changes | Extended Events (A4) |
| Structured traces across HTTP + EF in one view | OpenTelemetry + Jaeger (C4) |
| Everything, in VS Code, without SSMS installed | mssql extension (C1) + MiniProfiler (C3) |
