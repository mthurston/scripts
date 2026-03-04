using Microsoft.Data.SqlClient;
using System.Data;

namespace EfToDapper.Data.Dapper;

/// <summary>
/// Creates SQL Server (LocalDB) connections for Dapper.
/// Note: Dapper/ADO.NET require a real database connection string.
/// When using in-memory EF Core, Dapper will use LocalDB as a fallback.
/// </summary>
public interface IDapperConnectionFactory
{
    /// <summary>
    /// Creates and opens a new SQL Server connection for Dapper queries.
    /// </summary>
    IDbConnection CreateConnection();

    /// <summary>
    /// Gets the connection string being used by this factory.
    /// </summary>
    string GetConnectionString();
}

public sealed class DapperConnectionFactory : IDapperConnectionFactory
{
    private readonly string _connectionString;
    private const string LocalDbServer = @"(localdb)\mssqllocaldb";

    /// <summary>
    /// Creates a DapperConnectionFactory for the given provider configuration.
    /// When in-memory EF Core is used, Dapper defaults to LocalDB with a suffixed database name.
    /// </summary>
    /// <param name="providerType">The database provider type.</param>
    /// <param name="databaseName">Base database name.</param>
    public DapperConnectionFactory(Providers.DbProviderType providerType, string databaseName = "EfToDapperDemo")
    {
        _connectionString = providerType switch
        {
            Providers.DbProviderType.InMemory =>
                // For in-memory EF Core, Dapper uses LocalDB with a "_Dapper" suffix
                // This allows parallel in-memory EF testing while Dapper queries a real DB
                $"Server={LocalDbServer};Database={databaseName}_Dapper;Trusted_Connection=True;MultipleActiveResultSets=true",

            Providers.DbProviderType.SqlServerLocalDb =>
                // For LocalDB, use the same connection string
                $"Server={LocalDbServer};Database={databaseName};Trusted_Connection=True;MultipleActiveResultSets=true",

            _ => throw new ArgumentException($"Unsupported provider type: {providerType}")
        };
    }

    /// <summary>
    /// Creates a DapperConnectionFactory with an explicit connection string.
    /// Useful when you want full control over the Dapper connection independently.
    /// </summary>
    public DapperConnectionFactory(string connectionString)
    {
        _connectionString = connectionString ?? throw new ArgumentNullException(nameof(connectionString));
    }

    /// <inheritdoc/>
    public IDbConnection CreateConnection()
    {
        var conn = new SqlConnection(_connectionString);
        conn.Open();
        return conn;
    }

    /// <inheritdoc/>
    public string GetConnectionString() => _connectionString;
}
