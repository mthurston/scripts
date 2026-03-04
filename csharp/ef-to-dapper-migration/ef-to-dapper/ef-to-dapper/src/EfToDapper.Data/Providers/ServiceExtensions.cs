using EfToDapper.Data.Context;
using EfToDapper.Data.Dapper;
using EfToDapper.Data.Interceptors;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;

namespace EfToDapper.Data.Providers;

/// <summary>
/// ASP.NET Core DI extension methods for registering the data access layer.
/// Called from <c>Program.cs</c>:
/// <code>
/// builder.Services.AddAppDbContext(dbProviderOptions);
/// builder.Services.AddDapperDataAccess(providerType, databaseName);
/// await app.Services.InitializeDatabaseAsync(dbProviderOptions);
/// </code>
/// </summary>
public static class ServiceExtensions
{
    // ──────────────────────────────────────────────────────────────────────────
    // EF Core — AppDbContext
    // ──────────────────────────────────────────────────────────────────────────

    /// <summary>
    /// Registers <see cref="AppDbContext"/> with the correct database provider and wires up
    /// the scoped <see cref="QueryCounter"/> / <see cref="QueryCountingInterceptor"/> so
    /// every controller can inject <c>QueryCounter</c> to measure SQL output per request.
    /// </summary>
    public static IServiceCollection AddAppDbContext(
        this IServiceCollection services,
        DatabaseProviderOptions options)
    {
        // QueryCounter is scoped — one instance per HTTP request.
        // Controllers call queryCounter.Reset() at the start of each action.
        services.AddScoped<QueryCounter>();

        services.AddDbContext<AppDbContext>((sp, dbOptions) =>
        {
            var counter = sp.GetRequiredService<QueryCounter>();

            switch (options.ProviderType)
            {
                case DbProviderType.InMemory:
                    dbOptions.UseInMemoryDatabase(options.DatabaseName);
                    break;

                case DbProviderType.SqlServerLocalDb:
                    dbOptions.UseSqlServer(
                        DbContextOptionsFactory.BuildLocalDbConnectionString(options.DatabaseName));
                    break;

                default:
                    throw new ArgumentException(
                        $"Unknown provider type: {options.ProviderType}", nameof(options));
            }

            // Wire the scoped QueryCountingInterceptor so it shares the same
            // QueryCounter instance as the controller for this request.
            dbOptions.AddInterceptors(new QueryCountingInterceptor(counter));
        });

        return services;
    }

    // ──────────────────────────────────────────────────────────────────────────
    // Dapper — IDapperConnectionFactory + DapperQueries
    // ──────────────────────────────────────────────────────────────────────────

    /// <summary>
    /// Registers <see cref="IDapperConnectionFactory"/> (singleton) and
    /// <see cref="DapperQueries"/> (transient) for Dapper data access.
    /// When the EF Core provider is InMemory, Dapper falls back to a dedicated
    /// LocalDB database with a <c>_Dapper</c> suffix so tests still get a real
    /// SQL Server connection for the Dapper exercises.
    /// </summary>
    public static IServiceCollection AddDapperDataAccess(
        this IServiceCollection services,
        DbProviderType providerType,
        string databaseName = "EfToDapperDemo")
    {
        services.AddSingleton<IDapperConnectionFactory>(
            _ => new DapperConnectionFactory(providerType, databaseName));

        services.AddTransient<DapperQueries>();

        return services;
    }

    // ──────────────────────────────────────────────────────────────────────────
    // Startup initialization — schema creation + seeding
    // ──────────────────────────────────────────────────────────────────────────

    /// <summary>
    /// Creates the database schema and seeds demo data.
    /// Call this once after <c>app.Build()</c> but before <c>app.Run()</c>.
    /// </summary>
    /// <remarks>
    /// Behavior:
    /// <list type="bullet">
    ///   <item>If <see cref="DatabaseProviderOptions.ResetDatabaseOnStartup"/> is true,
    ///         drops and re-creates the database (all data is lost).</item>
    ///   <item>Calls <c>EnsureCreated()</c> to create schema tables that do not yet exist.</item>
    ///   <item>If <see cref="DatabaseProviderOptions.SeedOnStartup"/> is true,
    ///         seeds demo data via <see cref="DataSeeder.Seed"/> (idempotent — skipped if
    ///         any authors already exist).</item>
    /// </list>
    /// <para>
    /// <b>Stored procedures</b> are NOT auto-created here — they are workshop exercises.
    /// Implement each stored procedure in <c>database/StoredProcedures/</c>, then execute
    /// the <c>.sql</c> file against the LocalDB database.  Until implemented, the
    /// corresponding <c>/dapper</c> endpoints return a SQL error indicating the missing proc.
    /// </para>
    /// </remarks>
    public static async Task InitializeDatabaseAsync(
        this IServiceProvider services,
        DatabaseProviderOptions options)
    {
        // Use a dedicated scope — AppDbContext is scoped, cannot be resolved from root
        await using var scope = services.CreateAsyncScope();
        var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();

        if (options.ResetDatabaseOnStartup)
        {
            await db.Database.EnsureDeletedAsync();
        }

        // EnsureCreated creates tables that don't exist yet.
        // For InMemory this is a no-op (tables exist the moment the context is used).
        // For LocalDB this runs the EF Core CREATE TABLE DDL.
        await db.Database.EnsureCreatedAsync();

        if (options.SeedOnStartup)
        {
            // DataSeeder.Seed is idempotent — exits immediately if Authors already seeded.
            DataSeeder.Seed(db);
        }
    }
}
