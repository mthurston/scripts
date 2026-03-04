using EfToDapper.Data.Context;
using EfToDapper.Data.Providers;
using StackExchange.Profiling;

var builder = WebApplication.CreateBuilder(args);

// ── Database Provider Configuration ──────────────────────────────────────
// Determine which provider to use: InMemory or SqlServerLocalDb
// Can be configured via appsettings.json or environment variables
// Set in appsettings.Development.json:
//   "DatabaseProvider": {
//     "ProviderType": "InMemory",  // or "SqlServerLocalDb"
//     "DatabaseName": "EfToDapperDemo",
//     "SeedOnStartup": true
//   }
var dbProviderOptions = builder.Configuration.GetSection("DatabaseProvider").Get<DatabaseProviderOptions>()
    ?? new DatabaseProviderOptions();

// ── Register Data Access Services ────────────────────────────────────────
// AppDbContext with the configured provider (in-memory or LocalDB)
builder.Services.AddAppDbContext(dbProviderOptions);

// Dapper data access (uses LocalDB even when EF Core is in-memory)
builder.Services.AddDapperDataAccess(dbProviderOptions.ProviderType, dbProviderOptions.DatabaseName);

// ── MiniProfiler ──────────────────────────────────────────────────────────
// Captures every EF Core SQL query automatically via DiagnosticSource.
// View results at /profiler/results-index after hitting any endpoint.
// With SQL Server, MiniProfiler also shows actual execution plans via
// SET STATISTICS XML ON — enabling deep query plan inspection.
builder.Services.AddMiniProfiler(options =>
{
    options.RouteBasePath = "/profiler";
    options.ColorScheme = ColorScheme.Auto;
    options.PopupShowTimeWithChildren = true;
    options.PopupRenderPosition = RenderPosition.BottomLeft;
    options.ShouldProfile = _ => true;          // Profile every request in this demo
    options.EnableDebugMode = true;
}).AddEntityFramework();

// ── Swagger / OpenAPI ─────────────────────────────────────────────────────
builder.Services.AddControllers();
builder.Services.AddEndpointsApiExplorer();
builder.Services.AddSwaggerGen(c =>
{
    c.SwaggerDoc("v1", new()
    {
        Title = "EF Core → Dapper: Antipattern Workshop",
        Version = "v1",
        Description = $"""
            Interactive demo of common EF Core performance antipatterns and their fixes.
            Each scenario group exposes:
              • /antipattern — the problematic EF Core code (runs correctly but inefficiently)
              • /fixed-ef    — the corrected EF Core version
              • /dapper      — a stub for you to implement with Dapper

            After calling any endpoint, visit /profiler/results-index to see
            MiniProfiler's query count and timing breakdown.

            Current database provider: {dbProviderOptions.ProviderType}
            """
    });
    // EnableAnnotations handles [Tags], [SwaggerOperation] etc. from Swashbuckle.AspNetCore.Annotations
    c.EnableAnnotations();
});

var app = builder.Build();

// ── Initialize Databases ────────────────────────────────────────────────
// Creates schema and seeds data for both EF Core and Dapper databases
// This runs once at startup, after DI is configured but before requests are handled
await app.Services.InitializeDatabaseAsync(dbProviderOptions);

// ── Middleware pipeline ───────────────────────────────────────────────────
app.UseSwagger();
app.UseSwaggerUI(c =>
{
    c.SwaggerEndpoint("/swagger/v1/swagger.json", "EF → Dapper Workshop v1");
    c.RoutePrefix = string.Empty; // Swagger UI at root "/"
    c.DocumentTitle = "EF Core → Dapper Workshop";
});

app.UseMiniProfiler();
app.MapControllers();

app.Run();
