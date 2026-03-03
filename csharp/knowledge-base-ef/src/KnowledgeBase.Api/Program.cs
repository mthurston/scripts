using System.Text.Json.Serialization;
using KnowledgeBase.Api.Middleware;
using KnowledgeBase.Core.Interfaces;
using KnowledgeBase.Data.Context;
using KnowledgeBase.Data.Repositories;
using KnowledgeBase.Data.Services;
using Microsoft.EntityFrameworkCore;

var builder = WebApplication.CreateBuilder(args);

// --- Data access (EF Core) ---
builder.Services.AddDbContext<KnowledgeBaseDbContext>(opts =>
    opts.UseSqlServer(builder.Configuration.GetConnectionString("Default")));

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
        // Prevent cycles caused by bidirectional navigation properties (Comment <-> Replies)
        o.JsonSerializerOptions.ReferenceHandler = ReferenceHandler.IgnoreCycles);

builder.Services.AddEndpointsApiExplorer();
builder.Services.AddSwaggerGen(c =>
{
    c.SwaggerDoc("v1", new() { Title = "Knowledge Base API (EF Core)", Version = "v1" });
});

var app = builder.Build();

// Auto-apply migrations on startup in Development (saves running CLI commands during demo)
if (app.Environment.IsDevelopment())
{
    using var scope = app.Services.CreateScope();
    scope.ServiceProvider.GetRequiredService<KnowledgeBaseDbContext>().Database.Migrate();
}

app.UseSwagger();
app.UseSwaggerUI();
app.UseMiddleware<GlobalExceptionMiddleware>();
app.MapControllers();
app.Run();
