using EfToDapper.Data.Context;
using EfToDapper.Data.Interceptors;
using EfToDapper.Data.Providers;
using Microsoft.EntityFrameworkCore;

namespace EfToDapper.Tests.Infrastructure;

/// <summary>
/// Creates isolated AppDbContext instances for testing.
/// Supports both in-memory (fast, isolated) and SQL Server LocalDB (realistic, production-like).
/// Each test gets an isolated database — tests cannot interfere regardless of execution order.
/// The Cleanup action disposes and resets the database when the test is done.
/// </summary>
public static class TestDbContextFactory
{
    /// <summary>
    /// Creates a seeded AppDbContext backed by an isolated in-memory database.
    /// Useful for fast unit tests that don't need SQL Server.
    /// Each invocation gets a unique database name.
    /// </summary>
    public static (AppDbContext Context, Action Cleanup) CreateInMemorySeeded(
        QueryCounter? counter = null)
    {
        var dbName = $"EfToDapperTest_{Guid.NewGuid():N}";
        var (ctx, cleanup) = CreateInMemory(dbName, counter);
        DataSeeder.Seed(ctx);
        return (ctx, cleanup);
    }

    /// <summary>
    /// Creates an empty (un-seeded) AppDbContext backed by an isolated in-memory database.
    /// </summary>
    public static (AppDbContext Context, Action Cleanup) CreateInMemory(
        string? databaseName = null,
        QueryCounter? counter = null)
    {
        var dbName = databaseName ?? $"EfToDapperTest_{Guid.NewGuid():N}";
        var optionsBuilder = DbContextOptionsFactory.CreateOptions(DbProviderType.InMemory, dbName);

        if (counter is not null)
            optionsBuilder.AddInterceptors(new QueryCountingInterceptor(counter));

        var ctx = new AppDbContext(optionsBuilder.Options);
        ctx.Database.EnsureCreated();

        Action cleanup = () =>
        {
            try { ctx.Dispose(); }
            catch { /* best-effort cleanup */ }
        };

        return (ctx, cleanup);
    }

    /// <summary>
    /// Creates a seeded AppDbContext backed by an isolated LocalDB database.
    /// Useful for integration tests that need production-like SQL Server behavior.
    /// Call Cleanup() in the test's Dispose() to drop the database when done.
    /// </summary>
    public static (AppDbContext Context, Action Cleanup) CreateSeeded(
        QueryCounter? counter = null)
    {
        var (ctx, cleanup) = Create(null, counter);
        DataSeeder.Seed(ctx);
        return (ctx, cleanup);
    }

    /// <summary>
    /// Creates an empty (un-seeded) AppDbContext backed by an isolated LocalDB database.
    /// Each test class gets a uniquely-named database — tests cannot interfere regardless
    /// of execution order.
    /// </summary>
    public static (AppDbContext Context, Action Cleanup) Create(
        string? databaseName = null,
        QueryCounter? counter = null)
    {
        // Unique database name per invocation ensures full test isolation
        var dbName = databaseName ?? $"EfToDapperTest_{Guid.NewGuid():N}";
        var optionsBuilder = DbContextOptionsFactory.CreateOptions(DbProviderType.SqlServerLocalDb, dbName);

        if (counter is not null)
            optionsBuilder.AddInterceptors(new QueryCountingInterceptor(counter));

        var ctx = new AppDbContext(optionsBuilder.Options);
        ctx.Database.EnsureCreated();

        Action cleanup = () =>
        {
            try { ctx.Database.EnsureDeleted(); }
            catch { /* best-effort cleanup */ }
        };

        return (ctx, cleanup);
    }
}
