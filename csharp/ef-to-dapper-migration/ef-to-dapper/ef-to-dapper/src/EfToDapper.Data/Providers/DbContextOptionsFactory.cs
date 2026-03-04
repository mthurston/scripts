using EfToDapper.Data.Context;
using EfToDapper.Data.Interceptors;
using Microsoft.EntityFrameworkCore;

namespace EfToDapper.Data.Providers;

/// <summary>
/// Creates <see cref="DbContextOptionsBuilder{AppDbContext}"/> instances for the configured
/// database provider.  Used by both the production DI setup (via
/// <see cref="ServiceExtensions"/>) and the test infrastructure
/// (<c>TestDbContextFactory</c>) so every consumer uses identical options.
/// </summary>
public static class DbContextOptionsFactory
{
    private const string LocalDbServer = @"(localdb)\mssqllocaldb";

    /// <summary>
    /// Builds EF Core options for the given provider type.
    /// </summary>
    /// <param name="providerType">InMemory or SqlServerLocalDb.</param>
    /// <param name="databaseName">Logical database / in-memory store name.</param>
    /// <param name="counter">
    /// Optional <see cref="QueryCounter"/> to wire up the
    /// <see cref="QueryCountingInterceptor"/>.  Pass null to skip interceptor registration
    /// (e.g. inside AddDbContext where the counter is injected via scoped DI instead).
    /// </param>
    public static DbContextOptionsBuilder<AppDbContext> CreateOptions(
        DbProviderType providerType,
        string databaseName = "EfToDapperDemo",
        QueryCounter? counter = null)
    {
        var builder = new DbContextOptionsBuilder<AppDbContext>();

        switch (providerType)
        {
            case DbProviderType.InMemory:
                builder.UseInMemoryDatabase(databaseName);
                break;

            case DbProviderType.SqlServerLocalDb:
                builder.UseSqlServer(BuildLocalDbConnectionString(databaseName));
                break;

            default:
                throw new ArgumentException($"Unsupported provider type: {providerType}", nameof(providerType));
        }

        if (counter is not null)
            builder.AddInterceptors(new QueryCountingInterceptor(counter));

        return builder;
    }

    /// <summary>
    /// Builds a SQL Server LocalDB connection string for the given database name.
    /// MARS is enabled so that the CartesianExplosion scenario's split-query reader
    /// pattern works correctly during tests.
    /// </summary>
    public static string BuildLocalDbConnectionString(string databaseName) =>
        $"Server={LocalDbServer};Database={databaseName};Trusted_Connection=True;" +
        "MultipleActiveResultSets=true;TrustServerCertificate=true";
}
