using System.Data.Common;
using Microsoft.EntityFrameworkCore.Diagnostics;

namespace EfToDapper.Data.Interceptors;

/// <summary>
/// Scoped EF Core interceptor that counts the number of SQL commands issued
/// during a single HTTP request.  Inject <see cref="QueryCounter"/> into
/// controllers to read the count after each scenario endpoint runs.
/// </summary>
public sealed class QueryCounter
{
    private int _count;

    public int Count => _count;

    public void Reset() => _count = 0;

    internal void Increment() => Interlocked.Increment(ref _count);
}

/// <summary>
/// DbCommandInterceptor wired to the scoped <see cref="QueryCounter"/>.
/// Registered via AddDbContext(...).AddInterceptors(...).
/// </summary>
public sealed class QueryCountingInterceptor(QueryCounter counter) : DbCommandInterceptor
{
    public override DbDataReader ReaderExecuted(
        DbCommand command,
        CommandExecutedEventData eventData,
        DbDataReader result)
    {
        counter.Increment();
        return base.ReaderExecuted(command, eventData, result);
    }

    public override ValueTask<DbDataReader> ReaderExecutedAsync(
        DbCommand command,
        CommandExecutedEventData eventData,
        DbDataReader result,
        CancellationToken cancellationToken = default)
    {
        counter.Increment();
        return base.ReaderExecutedAsync(command, eventData, result, cancellationToken);
    }

    // Count scalar (e.g. ExecuteScalar) and non-query (ExecuteNonQuery) commands too
    public override object? ScalarExecuted(DbCommand command, CommandExecutedEventData eventData, object? result)
    {
        counter.Increment();
        return base.ScalarExecuted(command, eventData, result);
    }

    public override int NonQueryExecuted(DbCommand command, CommandExecutedEventData eventData, int result)
    {
        counter.Increment();
        return base.NonQueryExecuted(command, eventData, result);
    }
}
