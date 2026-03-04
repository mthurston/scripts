namespace EfToDapper.Data.Providers;

/// <summary>
/// Selects the underlying database engine for EF Core and Dapper connections.
/// Configure via <c>appsettings.json → DatabaseProvider.ProviderType</c>.
/// </summary>
public enum DbProviderType
{
    /// <summary>
    /// Microsoft.EntityFrameworkCore.InMemory — zero-dependency, fast, ideal for unit tests.
    /// Dapper endpoints fall back to SQL Server LocalDB when this is selected because
    /// there is no real database to connect to.
    /// </summary>
    InMemory,

    /// <summary>
    /// SQL Server (LocalDB) — production-like, supports execution plans, MiniProfiler I/O stats,
    /// key lookups, and all SQL Server-specific features.  Recommended for the full workshop.
    /// </summary>
    SqlServerLocalDb,
}
