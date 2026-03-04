namespace EfToDapper.Data.Providers;

/// <summary>
/// Binds to the <c>DatabaseProvider</c> section of appsettings.json.
/// </summary>
/// <example>
/// appsettings.Development.json:
/// <code>
/// "DatabaseProvider": {
///   "ProviderType": "SqlServerLocalDb",
///   "DatabaseName": "EfToDapperDemo",
///   "SeedOnStartup": true,
///   "ResetDatabaseOnStartup": false
/// }
/// </code>
/// </example>
public class DatabaseProviderOptions
{
    /// <summary>Which database engine to use.  Default: SqlServerLocalDb.</summary>
    public DbProviderType ProviderType { get; set; } = DbProviderType.SqlServerLocalDb;

    /// <summary>
    /// Logical database name used when constructing the connection string for LocalDB,
    /// or as the in-memory database identifier.  Default: EfToDapperDemo.
    /// </summary>
    public string DatabaseName { get; set; } = "EfToDapperDemo";

    /// <summary>
    /// When true, <see cref="ServiceExtensions.InitializeDatabaseAsync"/> seeds the database
    /// with demo data on first run (skipped if data already exists).  Default: true.
    /// </summary>
    public bool SeedOnStartup { get; set; } = true;

    /// <summary>
    /// When true, drops and re-creates the database on every startup.
    /// CAUTION: destroys all data.  Useful for clean-slate testing only.  Default: false.
    /// </summary>
    public bool ResetDatabaseOnStartup { get; set; } = false;
}
