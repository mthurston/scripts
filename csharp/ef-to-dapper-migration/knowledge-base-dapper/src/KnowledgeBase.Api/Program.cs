using System.Text.Json.Serialization;
using KnowledgeBase.Api.Middleware;
using KnowledgeBase.Core.Interfaces;
using KnowledgeBase.Data;
using KnowledgeBase.Data.Repositories;
using KnowledgeBase.Data.Services;

var builder = WebApplication.CreateBuilder(args);

// --- Data access (Dapper) ---
// No DbContext. A singleton factory creates a new IDbConnection per repository call.
// Schema must be applied manually: see src/KnowledgeBase.Data/Schema/001_InitialSchema.sql
builder.Services.AddSingleton<IDbConnectionFactory, SqlConnectionFactory>();

// --- Repositories ---
builder.Services.AddScoped<IIssueRepository, IssueRepository>();
builder.Services.AddScoped<ICommentRepository, CommentRepository>();
builder.Services.AddScoped<ILabelRepository, LabelRepository>();
builder.Services.AddScoped<IUserRepository, UserRepository>();

// --- Services ---
builder.Services.AddScoped<SyncService>();
builder.Services.AddHttpClient<IGitLabService, GitLabService>();

// --- API ---
builder.Services.AddControllers()
    .AddJsonOptions(o =>
        // Prevent JSON serialization cycles — CommentDto.Replies can nest arbitrarily
        o.JsonSerializerOptions.ReferenceHandler = ReferenceHandler.IgnoreCycles);

builder.Services.AddEndpointsApiExplorer();
builder.Services.AddSwaggerGen(c =>
{
    c.SwaggerDoc("v1", new() { Title = "Knowledge Base API (Dapper)", Version = "v1" });
});

var app = builder.Build();

// NOTE: Database schema is not applied automatically.
// Run the following before starting the API:
//   sqlcmd -S (localdb)\mssqllocaldb -d KnowledgeBaseDb -i src/KnowledgeBase.Data/Schema/001_InitialSchema.sql

app.UseSwagger();
app.UseSwaggerUI();
app.UseMiddleware<GlobalExceptionMiddleware>();
app.MapControllers();
app.Run();
