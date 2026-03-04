using Dapper;
using EfToDapper.Core.DTOs;
using EfToDapper.Data.Dapper;
using Microsoft.AspNetCore.Mvc;
using Microsoft.Data.SqlClient;
using Swashbuckle.AspNetCore.Annotations;
using System.Data;
using System.Diagnostics;

namespace EfToDapper.Api.Controllers;

/// <summary>
/// ╔══════════════════════════════════════════════════════════════╗
/// ║  ADO.NET Antipattern Scenarios                               ║
/// ╠══════════════════════════════════════════════════════════════╣
/// ║  Demonstrates four ADO.NET-specific antipatterns that have   ║
/// ║  no direct EF Core parallel in the existing scenario set.   ║
/// ║                                                              ║
/// ║  Each group contains:                                        ║
/// ║  • /antipattern  — the broken or dangerous ADO.NET code     ║
/// ║  • /fixed-ado    — correct ADO.NET implementation           ║
/// ║  • /dapper       — same operation via Dapper (inline SQL)   ║
/// ║                                                              ║
/// ║  Group 1 — Connection Mismanagement (forgetting 'using')    ║
/// ║  Group 2 — String Concatenation / SQL Injection             ║
/// ║  Group 3 — Missing Transaction on Multi-Step Write          ║
/// ║  Group 4 — Sync-over-Async (blocking thread pool reads)     ║
/// ╚══════════════════════════════════════════════════════════════╝
/// </summary>
[ApiController]
[Route("api/ado")]
[Tags("ADO.NET Antipattern Scenarios")]
public class AdoScenariosController(IDapperConnectionFactory connectionFactory) : ControllerBase
{
    private string ConnectionString => connectionFactory.GetConnectionString();

    // ═════════════════════════════════════════════════════════════════════════
    #region Group 1 — Connection Mismanagement (Forgetting 'using')
    // ═════════════════════════════════════════════════════════════════════════
    // SqlConnection, SqlCommand, and SqlDataReader all implement IDisposable.
    // Failing to dispose them can exhaust the connection pool, silently swallow
    // exceptions, or leave server-side cursors open.
    //
    // Connection pool exhaustion looks like:
    //   System.InvalidOperationException: Timeout expired. The timeout period
    //   elapsed prior to obtaining a connection from the pool.
    //
    // Default pool size is 100 connections. Under moderate load with leaked
    // connections, this error starts appearing within seconds.

    [HttpGet("connection-mismanagement/antipattern")]
    [SwaggerOperation(
        Summary = "❌ Antipattern — SqlConnection opened without 'using' (connection leak risk)",
        Description = """
            Opens a SqlConnection and SqlDataReader without wrapping them in
            'using' statements.  If an exception is thrown between Open() and
            Close() — or if Close() is simply forgotten — the connection is
            never returned to the pool.

            Under load with 100+ concurrent requests, the connection pool
            (default max: 100 connections) exhausts and new requests get:
              InvalidOperationException: Timeout expired.
              The timeout period elapsed prior to obtaining a connection from the pool.

            The error is deferred and non-obvious — the code works fine in
            low-concurrency tests, only breaking under production load.

            Note: this endpoint deliberately skips disposal to illustrate the
            problem. In a long-running process this accumulates over many calls.
            """)]
    public async Task<ScenarioResponse<object>> ConnectionMismanagementAntiPattern()
    {
        var sw = Stopwatch.StartNew();

        // ⚠️ ANTIPATTERN: SqlConnection allocated without 'using'.
        // If the ExecuteReader call throws (e.g. network timeout, bad SQL),
        // conn.Close() is never reached and the connection leaks from the pool.
        var conn = new SqlConnection(ConnectionString);
        conn.Open(); // Physical connection check-out from pool

        var cmd = new SqlCommand(
            "SELECT Id, Title FROM Posts ORDER BY PublishedAt DESC",
            conn);

        var reader = cmd.ExecuteReader(); // Synchronous — also blocks thread pool
        var results = new List<object>();

        while (reader.Read()) // ⚠️ Also sync — see Group 4 for async details
        {
            results.Add(new { Id = reader.GetInt32(0), Title = reader.GetString(1) });
        }

        // ⚠️ Manual close — only reached if NO exception occurs.
        // Any exception above (network blip, SQL error) leaves this line
        // unreachable, and the connection is never returned to the pool.
        // reader and cmd are also never disposed — server cursor stays open.
        conn.Close();

        sw.Stop();
        return new ScenarioResponse<object>
        {
            Scenario = "Connection Mismanagement",
            Approach = "ANTIPATTERN (ADO.NET)",
            Problem = "SqlConnection/SqlCommand/SqlDataReader not wrapped in 'using' — leaks on exception",
            Solution = "Wrap all IDisposable ADO.NET objects in 'using' statements for guaranteed disposal",
            QueryCount = 1,
            ElapsedMs = sw.ElapsedMilliseconds,
            SqlPreview = "SELECT Id, Title FROM Posts ORDER BY PublishedAt DESC",
            Data = new { RowCount = results.Count, Sample = results.Take(5) },
        };
    }

    [HttpGet("connection-mismanagement/fixed-ado")]
    [SwaggerOperation(
        Summary = "✅ Fixed (ADO.NET) — 'using' statements guarantee connection is returned to pool",
        Description = """
            Wrapping SqlConnection, SqlCommand, and SqlDataReader in 'using'
            blocks guarantees that Dispose() is called even if an exception
            occurs at any point inside the block.

            SqlConnection.Dispose() does NOT close the physical TCP socket —
            it returns the connection to the pool so the next caller can reuse
            the already-authenticated socket immediately.  This is the correct,
            efficient pattern.

            Rule of thumb: every new SqlConnection / SqlCommand / SqlDataReader
            should appear on a line that starts with 'using var'.
            """)]
    public async Task<ScenarioResponse<object>> ConnectionMismanagementFixed()
    {
        var sw = Stopwatch.StartNew();
        var results = new List<object>();

        // ✅ FIX: All three IDisposable objects wrapped in 'using'.
        // If any line inside throws, the C# compiler generates a try/finally
        // that calls Dispose() on each object in reverse declaration order.
        using var conn = new SqlConnection(ConnectionString);
        await conn.OpenAsync(); // ✅ Async open — doesn't block thread pool
        using var cmd = new SqlCommand(
            "SELECT Id, Title FROM Posts ORDER BY PublishedAt DESC",
            conn);
        using var reader = await cmd.ExecuteReaderAsync(); // ✅ Async execute

        while (await reader.ReadAsync()) // ✅ Async read — see Group 4
        {
            results.Add(new { Id = reader.GetInt32(0), Title = reader.GetString(1) });
        }
        // reader.Dispose() → closes server cursor
        // cmd.Dispose()    → releases command resources
        // conn.Dispose()   → returns connection to pool (NOT closed physically)

        sw.Stop();
        return new ScenarioResponse<object>
        {
            Scenario = "Connection Mismanagement",
            Approach = "FIXED (ADO.NET)",
            Problem = "Without 'using', exceptions leave connections leaked from the pool",
            Solution = "'using var conn / cmd / reader' guarantees Dispose() via compiler-generated try/finally",
            QueryCount = 1,
            ElapsedMs = sw.ElapsedMilliseconds,
            SqlPreview = "SELECT Id, Title FROM Posts ORDER BY PublishedAt DESC",
            Data = new { RowCount = results.Count, Sample = results.Take(5) },
        };
    }

    [HttpGet("connection-mismanagement/dapper")]
    [SwaggerOperation(
        Summary = "🔧 Dapper — connection scoped by 'using', reader lifecycle managed internally",
        Description = """
            Dapper's QueryAsync() opens the connection (if not already open),
            creates the SqlCommand, executes the reader, maps results into the
            target type, and disposes the reader — all internally.

            The only responsibility left to the caller is disposing the
            SqlConnection itself, which is handled by 'using var conn'.

            Straight inline SQL — no stored procedure.  Compare with
            /fixed-ado: same guarantees, ~70% less boilerplate.
            """)]
    public async Task<ScenarioResponse<object>> ConnectionMismanagementDapper()
    {
        var sw = Stopwatch.StartNew();

        // ✅ DAPPER: 'using' scopes the connection; QueryAsync handles the rest.
        using var conn = connectionFactory.CreateConnection();
        var results = (await conn.QueryAsync<dynamic>(
            "SELECT Id, Title FROM Posts ORDER BY PublishedAt DESC")).AsList();

        sw.Stop();
        return new ScenarioResponse<object>
        {
            Scenario = "Connection Mismanagement",
            Approach = "DAPPER",
            Problem = "Manual ADO.NET connection lifecycle requires careful using-block discipline",
            Solution = "Dapper manages reader/command lifecycle; one 'using' on the connection is all that's needed",
            QueryCount = 1,
            ElapsedMs = sw.ElapsedMilliseconds,
            SqlPreview = "SELECT Id, Title FROM Posts ORDER BY PublishedAt DESC",
            Data = new { RowCount = results.Count, Sample = results.Take(5).Select(r => new { Id = (int)r.Id, Title = (string)r.Title }) },
        };
    }

    #endregion

    // ═════════════════════════════════════════════════════════════════════════
    #region Group 2 — String Concatenation / SQL Injection
    // ═════════════════════════════════════════════════════════════════════════
    // Concatenating user-supplied input directly into a SQL string allows an
    // attacker to terminate the intended query and inject arbitrary SQL.
    //
    // Classic injection via status parameter:
    //   status = "Published'; DROP TABLE Posts; --"
    //   → WHERE Status = 'Published'; DROP TABLE Posts; --'
    //
    // Even without malicious intent, input containing a single apostrophe
    // (e.g. status = "O'Brien's Posts") causes a SqlException:
    //   Incorrect syntax near 'Brien'

    [HttpGet("sql-injection/antipattern")]
    [SwaggerOperation(
        Summary = "❌ Antipattern — string concatenation builds SQL from user input (injection risk)",
        Description = """
            Builds the WHERE clause by directly appending the caller-supplied
            'status' query parameter to the SQL string.

            Try it with a benign input first:
              GET /antipattern?status=Published   → works

            Then try breaking the query:
              GET /antipattern?status=Published' AND 1=0 --   → 0 results
              GET /antipattern?status=Published' OR '1'='1   → all rows

            In a real application the attacker-supplied string might:
            • Extract data from other tables via UNION SELECT
            • Execute xp_cmdshell to run OS commands (if SA-level access)
            • DROP or TRUNCATE tables
            • Authenticate as any user (classic 'OR 1=1' login bypass)

            Using SqlParameter eliminates this entire class of vulnerability.
            """)]
    public async Task<ScenarioResponse<object>> SqlInjectionAntiPattern(string status = "Published")
    {
        var sw = Stopwatch.StartNew();
        var results = new List<object>();

        using var conn = new SqlConnection(ConnectionString);
        await conn.OpenAsync();

        // ⚠️ ANTIPATTERN: User input concatenated directly into SQL string.
        // A single quote in 'status' breaks the query.  A crafted value can
        // exfiltrate data, modify data, or drop tables.
        var sql = $"SELECT Id, Title, Status FROM Posts WHERE Status = '{status}'";

        using var cmd = new SqlCommand(sql, conn);
        using var reader = await cmd.ExecuteReaderAsync();
        while (await reader.ReadAsync())
        {
            results.Add(new { Id = reader.GetInt32(0), Title = reader.GetString(1), Status = reader.GetString(2) });
        }

        sw.Stop();
        return new ScenarioResponse<object>
        {
            Scenario = "String Concatenation / SQL Injection",
            Approach = "ANTIPATTERN (ADO.NET)",
            Problem = "User input injected directly into SQL — one crafted 'status' value can compromise the database",
            Solution = "Always use SqlParameter / parameterized queries — user data never becomes executable SQL",
            QueryCount = 1,
            ElapsedMs = sw.ElapsedMilliseconds,
            SqlPreview = sql, // ← deliberately showing the dangerous concatenated SQL
            Data = new { RowCount = results.Count, Sample = results.Take(5) },
        };
    }

    [HttpGet("sql-injection/fixed-ado")]
    [SwaggerOperation(
        Summary = "✅ Fixed (ADO.NET) — SqlParameter prevents injection by separating code from data",
        Description = """
            Parameterized queries send the SQL template and the parameter values
            as separate wire protocol messages.  The database server receives
            them independently and never interprets the parameter value as SQL.

            Attackers can inject arbitrary characters into the parameter value —
            single quotes, semicolons, comment markers — none of it is executed.
            The value is treated as a literal string in the predicate.

            SqlParameter also improves SQL Server plan cache reuse:
            the same plan is reused for every status value instead of
            generating a new plan per unique concatenated SQL string.
            """)]
    public async Task<ScenarioResponse<object>> SqlInjectionFixed(string status = "Published")
    {
        var sw = Stopwatch.StartNew();
        var results = new List<object>();

        using var conn = new SqlConnection(ConnectionString);
        await conn.OpenAsync();

        // ✅ FIX: sql template uses @Status placeholder.
        // The value is sent over the wire as a typed parameter — never parsed as SQL.
        const string sql = "SELECT Id, Title, Status FROM Posts WHERE Status = @Status";
        using var cmd = new SqlCommand(sql, conn);
        cmd.Parameters.Add(new SqlParameter("@Status", SqlDbType.NVarChar, 50) { Value = status });

        using var reader = await cmd.ExecuteReaderAsync();
        while (await reader.ReadAsync())
        {
            results.Add(new { Id = reader.GetInt32(0), Title = reader.GetString(1), Status = reader.GetString(2) });
        }

        sw.Stop();
        return new ScenarioResponse<object>
        {
            Scenario = "String Concatenation / SQL Injection",
            Approach = "FIXED (ADO.NET)",
            Problem = "String concatenation allows user input to become executable SQL",
            Solution = "SqlParameter separates code from data — the value is never treated as SQL",
            QueryCount = 1,
            ElapsedMs = sw.ElapsedMilliseconds,
            SqlPreview = $"{sql}  -- @Status = '{status}' (sent as typed parameter, not interpolated)",
            Data = new { RowCount = results.Count, Sample = results.Take(5) },
        };
    }

    [HttpGet("sql-injection/dapper")]
    [SwaggerOperation(
        Summary = "🔧 Dapper — anonymous object parameters are always parameterized (injection-safe by design)",
        Description = """
            Dapper parameterizes anonymous object properties automatically.
            There is no API in Dapper that allows string concatenation into
            the query template — the pattern forces correct usage.

            Passing { Status = status } is equivalent to adding a @Status
            SqlParameter.  The value can never break out of the parameter
            literal no matter what characters it contains.

            Straight inline SQL — no stored procedure.
            """)]
    public async Task<ScenarioResponse<object>> SqlInjectionDapper(string status = "Published")
    {
        var sw = Stopwatch.StartNew();

        // ✅ DAPPER: The anonymous object { Status = status } generates a
        // @Status SqlParameter internally.  Injection is structurally impossible.
        using var conn = connectionFactory.CreateConnection();
        var results = (await conn.QueryAsync<dynamic>(
            "SELECT Id, Title, Status FROM Posts WHERE Status = @Status",
            new { Status = status })).AsList();

        sw.Stop();
        return new ScenarioResponse<object>
        {
            Scenario = "String Concatenation / SQL Injection",
            Approach = "DAPPER",
            Problem = "ADO.NET string concatenation requires developer discipline to avoid injection",
            Solution = "Dapper's anonymous-object parameter API makes parameterization the only available path",
            QueryCount = 1,
            ElapsedMs = sw.ElapsedMilliseconds,
            SqlPreview = "SELECT Id, Title, Status FROM Posts WHERE Status = @Status  -- @Status bound from anonymous object",
            Data = new { RowCount = results.Count, Sample = results.Take(5).Select(r => new { Id = (int)r.Id, Title = (string)r.Title, Status = (string)r.Status }) },
        };
    }

    #endregion

    // ═════════════════════════════════════════════════════════════════════════
    #region Group 3 — Missing Transaction on Multi-Step Write
    // ═════════════════════════════════════════════════════════════════════════
    // When two or more writes must succeed or fail together, they must be
    // wrapped in a transaction.  Without a transaction, a failure between
    // the writes leaves the database in a partially-updated, inconsistent state.
    //
    // Classic example: bank transfer
    //   UPDATE Accounts SET Balance = Balance - 100 WHERE Id = @from   ← committed
    //   UPDATE Accounts SET Balance = Balance + 100 WHERE Id = @to     ← fails
    //   → $100 vanishes from the system; total money supply changes
    //
    // In this demo we increment a post's ViewCount and update its Status
    // as a two-step operation.  The antipattern simulates a crash between
    // the two writes.  Both endpoints roll back to keep demo data clean.

    [HttpGet("missing-transaction/antipattern")]
    [SwaggerOperation(
        Summary = "❌ Antipattern — two writes without a transaction; partial failure leaves inconsistent state",
        Description = """
            Executes two UPDATE statements in sequence without a transaction.
            A simulated exception is thrown between them.

            Result: the first UPDATE (ViewCount) is immediately committed to
            the database.  The second UPDATE (Status) never runs.  The post
            now has an incremented ViewCount but its Status was not updated —
            the two fields are inconsistent with each other.

            This demo rolls back the first write manually for repeatability,
            but in a real failure scenario (network drop, process crash, OOM)
            there is no recovery path — the inconsistency is permanent.

            Real-world impact: order partially shipped, payment taken but
            not recorded, audit log missing, inventory double-decremented.
            """)]
    public async Task<ScenarioResponse<object>> MissingTransactionAntiPattern()
    {
        var sw = Stopwatch.StartNew();

        // Grab the first available post to use as our demo target
        int postId;
        int originalViewCount;
        string originalStatus;

        using (var setupConn = new SqlConnection(ConnectionString))
        {
            await setupConn.OpenAsync();
            using var setupCmd = new SqlCommand(
                "SELECT TOP 1 Id, ViewCount, Status FROM Posts ORDER BY Id", setupConn);
            using var setupReader = await setupCmd.ExecuteReaderAsync();
            await setupReader.ReadAsync();
            postId = setupReader.GetInt32(0);
            originalViewCount = setupReader.GetInt32(1);
            originalStatus = setupReader.GetString(2);
        }

        string inconsistencyReport = string.Empty;
        Exception? capturedEx = null;

        using var conn = new SqlConnection(ConnectionString);
        await conn.OpenAsync();

        try
        {
            // ── Write 1: increment ViewCount ──────────────────────────────
            // ⚠️ ANTIPATTERN: No transaction opened before this write.
            //   This UPDATE auto-commits immediately when ExecuteNonQueryAsync returns.
            //   There is no way to undo it if the next operation fails.
            using (var cmd1 = new SqlCommand(
                "UPDATE Posts SET ViewCount = ViewCount + 1 WHERE Id = @Id", conn))
            {
                cmd1.Parameters.AddWithValue("@Id", postId);
                await cmd1.ExecuteNonQueryAsync();
            }
            // At this point ViewCount = originalViewCount + 1, committed to disk.

            // ── Simulated failure between the two writes ──────────────────
            throw new InvalidOperationException(
                "Simulated crash between Write 1 (ViewCount++) and Write 2 (Status update). " +
                "Write 1 is already committed — the database is now inconsistent.");
        }
        catch (InvalidOperationException ex)
        {
            capturedEx = ex;

            // ── Cleanup: manually restore for demo repeatability ──────────
            // In a real crash (process killed, network drop) this code never
            // runs — the inconsistency becomes permanent.
            using var undoCmd = new SqlCommand(
                "UPDATE Posts SET ViewCount = @OriginalViewCount WHERE Id = @Id", conn);
            undoCmd.Parameters.AddWithValue("@OriginalViewCount", originalViewCount);
            undoCmd.Parameters.AddWithValue("@Id", postId);
            await undoCmd.ExecuteNonQueryAsync();

            inconsistencyReport =
                $"Write 1 (ViewCount → {originalViewCount + 1}) committed BEFORE the crash. " +
                $"Write 2 (Status) never executed. " +
                $"ViewCount manually restored to {originalViewCount} for demo repeatability — " +
                $"a real crash has no such recovery path.";
        }

        sw.Stop();
        return new ScenarioResponse<object>
        {
            Scenario = "Missing Transaction on Multi-Step Write",
            Approach = "ANTIPATTERN (ADO.NET)",
            Problem = "Write 1 auto-commits before Write 2 runs; crash between them = permanent inconsistency",
            Solution = "Wrap multi-step writes in a SqlTransaction — ROLLBACK on error returns to consistent state",
            QueryCount = 3,
            ElapsedMs = sw.ElapsedMilliseconds,
            SqlPreview =
                "UPDATE Posts SET ViewCount = ViewCount + 1 WHERE Id = @Id\n" +
                "-- ← auto-committed, no transaction\n" +
                "[CRASH — second UPDATE never runs]",
            Data = new
            {
                AffectedPostId = postId,
                OriginalViewCount = originalViewCount,
                OriginalStatus = originalStatus,
                SimulatedError = capturedEx?.Message,
                InconsistencyReport = inconsistencyReport,
            },
        };
    }

    [HttpGet("missing-transaction/fixed-ado")]
    [SwaggerOperation(
        Summary = "✅ Fixed (ADO.NET) — SqlTransaction wraps both writes; ROLLBACK on failure",
        Description = """
            Both UPDATE statements execute inside a single SqlTransaction.
            Neither write is visible to other connections until Commit() succeeds.

            If an exception occurs between the two writes (or during the second
            write), the catch block calls Rollback() and the database returns
            to exactly the state it was in before the transaction started.

            ACID guarantee: Atomicity — either both writes happen, or neither does.

            This demo intentionally throws after the first write, then calls
            Rollback() to demonstrate the atomicity guarantee.  The final
            ViewCount is confirmed unchanged in the response.
            """)]
    public async Task<ScenarioResponse<object>> MissingTransactionFixed()
    {
        var sw = Stopwatch.StartNew();

        int postId;
        int originalViewCount;
        string originalStatus;

        using (var setupConn = new SqlConnection(ConnectionString))
        {
            await setupConn.OpenAsync();
            using var setupCmd = new SqlCommand(
                "SELECT TOP 1 Id, ViewCount, Status FROM Posts ORDER BY Id", setupConn);
            using var setupReader = await setupCmd.ExecuteReaderAsync();
            await setupReader.ReadAsync();
            postId = setupReader.GetInt32(0);
            originalViewCount = setupReader.GetInt32(1);
            originalStatus = setupReader.GetString(2);
        }

        string atomicityReport = string.Empty;
        int viewCountAfterRollback = originalViewCount;

        using var conn = new SqlConnection(ConnectionString);
        await conn.OpenAsync();

        // ✅ FIX: Begin a transaction before the first write.
        using var tx = conn.BeginTransaction();
        try
        {
            // ── Write 1: increment ViewCount (inside transaction) ─────────
            using (var cmd1 = new SqlCommand(
                "UPDATE Posts SET ViewCount = ViewCount + 1 WHERE Id = @Id", conn, tx))
            {
                cmd1.Parameters.AddWithValue("@Id", postId);
                await cmd1.ExecuteNonQueryAsync();
            }
            // Write 1 is NOT yet committed — held in transaction buffer.
            // Other connections still see the original ViewCount.

            // ── Simulated failure ─────────────────────────────────────────
            throw new InvalidOperationException(
                "Simulated crash — Write 1 is in the transaction but NOT committed.");
        }
        catch (InvalidOperationException)
        {
            // ✅ ROLLBACK: Both writes are undone atomically.
            // SQL Server releases the transaction's row-level locks and discards
            // the in-flight changes.  Database is back to its pre-transaction state.
            await tx.RollbackAsync();
        }

        // Verify ViewCount is unchanged after rollback
        using (var verifyConn = new SqlConnection(ConnectionString))
        {
            await verifyConn.OpenAsync();
            using var verifyCmd = new SqlCommand(
                "SELECT ViewCount FROM Posts WHERE Id = @Id", verifyConn);
            verifyCmd.Parameters.AddWithValue("@Id", postId);
            viewCountAfterRollback = (int)(await verifyCmd.ExecuteScalarAsync())!;
            atomicityReport =
                $"Write 1 executed inside the transaction (ViewCount would have become {originalViewCount + 1}). " +
                $"Exception triggered ROLLBACK. ViewCount after rollback = {viewCountAfterRollback} " +
                $"(matches original {originalViewCount}) — atomicity confirmed.";
        }

        sw.Stop();
        return new ScenarioResponse<object>
        {
            Scenario = "Missing Transaction on Multi-Step Write",
            Approach = "FIXED (ADO.NET)",
            Problem = "Without a transaction, a mid-operation failure leaves a partial, inconsistent write",
            Solution = "SqlTransaction + ROLLBACK on error guarantees atomicity — all-or-nothing semantics",
            QueryCount = 3,
            ElapsedMs = sw.ElapsedMilliseconds,
            SqlPreview =
                "BEGIN TRANSACTION\n" +
                "  UPDATE Posts SET ViewCount = ViewCount + 1 WHERE Id = @Id\n" +
                "  [exception thrown here]\n" +
                "ROLLBACK",
            Data = new
            {
                AffectedPostId = postId,
                OriginalViewCount = originalViewCount,
                ViewCountAfterRollback = viewCountAfterRollback,
                AtomicityReport = atomicityReport,
            },
        };
    }

    [HttpGet("missing-transaction/dapper")]
    [SwaggerOperation(
        Summary = "🔧 Dapper — transaction passed explicitly to Execute; same atomicity guarantee",
        Description = """
            Dapper accepts a SqlTransaction as an optional parameter to all
            Execute* and Query* methods.  The pattern is identical to ADO.NET
            but with less boilerplate — no SqlCommand construction needed.

            Dapper does not manage transactions automatically; the caller is
            responsible for Begin/Commit/Rollback.  This is intentional —
            Dapper is a thin mapper, not an ORM, and transaction scope is a
            business logic concern.

            Straight inline SQL — no stored procedure.
            This demo rolls back intentionally to show the atomicity guarantee.
            """)]
    public async Task<ScenarioResponse<object>> MissingTransactionDapper()
    {
        var sw = Stopwatch.StartNew();

        // Fetch the demo post
        using var setupConn = connectionFactory.CreateConnection();
        var post = await setupConn.QueryFirstAsync<dynamic>(
            "SELECT TOP 1 Id, ViewCount, Status FROM Posts ORDER BY Id");
        int postId = (int)post.Id;
        int originalViewCount = (int)post.ViewCount;

        string atomicityReport = string.Empty;
        int viewCountAfterRollback = originalViewCount;

        // ✅ DAPPER: transaction passed explicitly to ExecuteAsync calls
        using var conn = new SqlConnection(ConnectionString);
        await conn.OpenAsync();
        using var tx = conn.BeginTransaction();

        try
        {
            // Write 1: inside the Dapper-managed transaction
            await conn.ExecuteAsync(
                "UPDATE Posts SET ViewCount = ViewCount + 1 WHERE Id = @Id",
                new { Id = postId },
                transaction: tx);

            // Simulated failure — Write 1 is buffered but not committed
            throw new InvalidOperationException("Simulated crash — Dapper write is in transaction, not committed.");
        }
        catch (InvalidOperationException)
        {
            await tx.RollbackAsync(); // Dapper defers to the ADO.NET transaction for rollback
        }

        // Verify via Dapper read
        using var verifyConn = connectionFactory.CreateConnection();
        viewCountAfterRollback = await verifyConn.QuerySingleAsync<int>(
            "SELECT ViewCount FROM Posts WHERE Id = @Id", new { Id = postId });

        atomicityReport =
            $"Dapper ExecuteAsync ran inside the transaction (ViewCount would have become {originalViewCount + 1}). " +
            $"Rollback confirmed: ViewCount = {viewCountAfterRollback} (original = {originalViewCount}).";

        sw.Stop();
        return new ScenarioResponse<object>
        {
            Scenario = "Missing Transaction on Multi-Step Write",
            Approach = "DAPPER",
            Problem = "Multi-step writes without a transaction can partially commit on failure",
            Solution = "Pass the transaction to each Dapper call; rollback on failure restores consistency",
            QueryCount = 3,
            ElapsedMs = sw.ElapsedMilliseconds,
            SqlPreview =
                "-- tx = conn.BeginTransaction()\n" +
                "conn.ExecuteAsync(\"UPDATE Posts SET ViewCount = ...\", new { Id }, transaction: tx)\n" +
                "-- [exception] → tx.RollbackAsync()",
            Data = new
            {
                AffectedPostId = postId,
                OriginalViewCount = originalViewCount,
                ViewCountAfterRollback = viewCountAfterRollback,
                AtomicityReport = atomicityReport,
            },
        };
    }

    #endregion

    // ═════════════════════════════════════════════════════════════════════════
    #region Group 4 — Sync-over-Async (Blocking Thread Pool Reads)
    // ═════════════════════════════════════════════════════════════════════════
    // ADO.NET exposes both synchronous (Open, ExecuteReader, Read) and
    // asynchronous (OpenAsync, ExecuteReaderAsync, ReadAsync) variants of
    // every I/O operation.
    //
    // Calling the synchronous variant from an async method blocks the thread
    // pool thread for the entire duration of the network wait (SQL Server
    // round-trip typically 0.1–5 ms for local, 5–50 ms for remote).
    //
    // At 1,000 concurrent requests, 30 ms × 1,000 threads = 30 thread-seconds
    // of wasted thread pool capacity per second.  The async variants free the
    // thread immediately after issuing the I/O request and resume on completion.
    //
    // Thread pool exhaustion looks like:
    //   System.Threading.ThreadPool: unable to allocate more threads.
    //   Slow response times, timeouts, cascading failures.

    [HttpGet("sync-over-async/antipattern")]
    [SwaggerOperation(
        Summary = "❌ Antipattern — synchronous Read() in async context blocks a thread pool thread",
        Description = """
            Calls Open(), ExecuteReader(), and Read() — the synchronous ADO.NET
            variants — from inside an async method.

            The async method signature (async Task<...>) means it runs on a
            thread pool thread.  When that thread calls the synchronous Open()
            or Read(), it makes a blocking network call and waits on the thread
            — the thread cannot be reused for other work during the wait.

            With enough concurrent requests this starves the thread pool:
            • CPU utilization stays near 0% (threads are blocked, not computing)
            • Request queue depth grows
            • .NET's thread injection heuristic adds threads slowly (1 per 500 ms)
            • Response time climbs linearly until threads catch up or time out

            This is the ADO.NET equivalent of Task.Result or .GetAwaiter().GetResult()
            called inside an async method — blocking a thread while waiting for I/O.
            """)]
    public async Task<ScenarioResponse<object>> SyncOverAsyncAntiPattern()
    {
        var sw = Stopwatch.StartNew();
        var results = new List<object>();

        using var conn = new SqlConnection(ConnectionString);

        // ⚠️ ANTIPATTERN: Synchronous Open() — blocks thread pool thread
        // during TCP handshake + SQL Server login acknowledgment.
        conn.Open(); // Synchronous — thread blocked until connection is fully established

        using var cmd = new SqlCommand(
            "SELECT Id, Title, Status, ViewCount FROM Posts ORDER BY PublishedAt DESC",
            conn);

        // ⚠️ ANTIPATTERN: Synchronous ExecuteReader() — blocks during
        // network round-trip to SQL Server and first result set delivery.
        using var reader = cmd.ExecuteReader(); // Synchronous

        // ⚠️ ANTIPATTERN: Synchronous Read() — blocks on each network packet
        // containing the next batch of rows.
        while (reader.Read()) // Synchronous row iteration
        {
            results.Add(new
            {
                Id = reader.GetInt32(0),
                Title = reader.GetString(1),
                Status = reader.GetString(2),
                ViewCount = reader.GetInt32(3),
            });
        }

        sw.Stop();
        return new ScenarioResponse<object>
        {
            Scenario = "Sync-over-Async",
            Approach = "ANTIPATTERN (ADO.NET)",
            Problem = "Open/ExecuteReader/Read block a thread pool thread for the entire network wait",
            Solution = "Use OpenAsync / ExecuteReaderAsync / ReadAsync to release the thread during I/O",
            QueryCount = 1,
            ElapsedMs = sw.ElapsedMilliseconds,
            SqlPreview =
                "-- conn.Open()          [BLOCKING — thread held]\n" +
                "-- cmd.ExecuteReader()  [BLOCKING — thread held]\n" +
                "-- reader.Read()        [BLOCKING — thread held per row batch]\n" +
                "SELECT Id, Title, Status, ViewCount FROM Posts ORDER BY PublishedAt DESC",
            Data = new { RowCount = results.Count, Sample = results.Take(5) },
        };
    }

    [HttpGet("sync-over-async/fixed-ado")]
    [SwaggerOperation(
        Summary = "✅ Fixed (ADO.NET) — OpenAsync / ExecuteReaderAsync / ReadAsync free the thread during I/O",
        Description = """
            Each I/O operation uses its async variant.  The 'await' keyword
            releases the thread pool thread back to the .NET scheduler
            immediately when the I/O request is issued, and resumes on a
            (potentially different) thread when the result arrives.

            Under load this multiplies throughput:
            • 1,000 concurrent requests × 30 ms average I/O
            • Sync:  1,000 blocked threads × 30 ms = 30 thread-seconds wasted/sec
            • Async: threads are recycled during I/O; far fewer threads needed

            Rule: any method that calls ADO.NET must be 'async Task' and must
            'await' every I/O operation.  Mixing sync calls into an async chain
            negates the benefit (and can cause deadlocks on some runtimes).
            """)]
    public async Task<ScenarioResponse<object>> SyncOverAsyncFixed()
    {
        var sw = Stopwatch.StartNew();
        var results = new List<object>();

        using var conn = new SqlConnection(ConnectionString);

        // ✅ FIX: OpenAsync() issues the connection request and releases the thread.
        // The thread is returned to the pool while waiting for SQL Server handshake.
        await conn.OpenAsync();

        using var cmd = new SqlCommand(
            "SELECT Id, Title, Status, ViewCount FROM Posts ORDER BY PublishedAt DESC",
            conn);

        // ✅ FIX: ExecuteReaderAsync() releases thread during initial network round-trip.
        using var reader = await cmd.ExecuteReaderAsync();

        // ✅ FIX: ReadAsync() releases thread between row-batch network packets.
        // Each await yields the thread; resumes when the next batch arrives.
        while (await reader.ReadAsync())
        {
            results.Add(new
            {
                Id = reader.GetInt32(0),
                Title = reader.GetString(1),
                Status = reader.GetString(2),
                ViewCount = reader.GetInt32(3),
            });
        }

        sw.Stop();
        return new ScenarioResponse<object>
        {
            Scenario = "Sync-over-Async",
            Approach = "FIXED (ADO.NET)",
            Problem = "Sync variants block the thread pool thread for entire I/O duration",
            Solution = "Async variants yield the thread at each await point, maximizing thread pool efficiency",
            QueryCount = 1,
            ElapsedMs = sw.ElapsedMilliseconds,
            SqlPreview =
                "-- await conn.OpenAsync()           [thread released during handshake]\n" +
                "-- await cmd.ExecuteReaderAsync()   [thread released during round-trip]\n" +
                "-- await reader.ReadAsync()         [thread released between row batches]\n" +
                "SELECT Id, Title, Status, ViewCount FROM Posts ORDER BY PublishedAt DESC",
            Data = new { RowCount = results.Count, Sample = results.Take(5) },
        };
    }

    [HttpGet("sync-over-async/dapper")]
    [SwaggerOperation(
        Summary = "🔧 Dapper — QueryAsync is always async end-to-end; no synchronous variant to accidentally use",
        Description = """
            Dapper exposes only async methods for queries: QueryAsync, QueryFirstAsync,
            ExecuteAsync, etc.  The synchronous Query() method exists but returns a
            fully-materialized list rather than streaming, making accidental blocking
            less common on hot paths.

            QueryAsync internally calls OpenAsync, ExecuteReaderAsync, and ReadAsync —
            the same fix as the ADO.NET /fixed-ado endpoint, with zero boilerplate.

            Straight inline SQL — no stored procedure.  For CPU-bound post-
            processing (projection, ordering) the result set is already fully
            materialized in memory when the Task completes.
            """)]
    public async Task<ScenarioResponse<object>> SyncOverAsyncDapper()
    {
        var sw = Stopwatch.StartNew();

        // ✅ DAPPER: QueryAsync is async end-to-end.  Thread is free during all I/O.
        using var conn = connectionFactory.CreateConnection();
        var results = (await conn.QueryAsync<dynamic>(
            "SELECT Id, Title, Status, ViewCount FROM Posts ORDER BY PublishedAt DESC")).AsList();

        sw.Stop();
        return new ScenarioResponse<object>
        {
            Scenario = "Sync-over-Async",
            Approach = "DAPPER",
            Problem = "ADO.NET has both sync and async variants — easy to accidentally call the blocking one",
            Solution = "Dapper's QueryAsync encapsulates the async chain; a single await is all that's needed",
            QueryCount = 1,
            ElapsedMs = sw.ElapsedMilliseconds,
            SqlPreview = "SELECT Id, Title, Status, ViewCount FROM Posts ORDER BY PublishedAt DESC",
            Data = new
            {
                RowCount = results.Count,
                Sample = results.Take(5).Select(r => new
                {
                    Id = (int)r.Id,
                    Title = (string)r.Title,
                    Status = (string)r.Status,
                    ViewCount = (int)r.ViewCount,
                }),
            },
        };
    }

    #endregion
}
