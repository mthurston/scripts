# Knowledge Base API — EF Core Edition

An ASP.NET Core 8 Web API that syncs GitLab issues into a SQL Server database and exposes them as a searchable knowledge base. This project uses **Entity Framework Core 8** for all data access.

## Teaching Goals

- `DbContext` and `IEntityTypeConfiguration<T>` for clean, separated model configuration
- Migrations-based schema management (`dotnet ef migrations`)
- LINQ with `Include()` and `ThenInclude()` for eager loading of related data
- Self-referencing `Comment` entity for hierarchical threaded comments
- Navigation properties replacing manual JOIN and mapping logic
- How EF tracks entity state to drive INSERT vs UPDATE decisions during sync

## Prerequisites

- .NET 8 SDK
- SQL Server or LocalDB (`(localdb)\mssqllocaldb`)
- A GitLab project with an API access token

## Setup

1. **Clone and navigate:**
   ```
   git clone <repo-url>
   cd csharp/knowledge-base-ef
   ```

2. **Configure GitLab credentials** in `src/KnowledgeBase.Api/appsettings.json`:
   ```json
   {
     "GitLab": {
       "BaseUrl": "https://gitlab.com",
       "ProjectId": "YOUR_PROJECT_ID",
       "AccessToken": "YOUR_PRIVATE_TOKEN"
     }
   }
   ```

3. **Apply EF migrations** (creates the schema):
   ```
   dotnet ef database update --project src/KnowledgeBase.Data --startup-project src/KnowledgeBase.Api
   ```
   > In Development mode, migrations are also applied automatically on startup.

4. **Run the API:**
   ```
   dotnet run --project src/KnowledgeBase.Api
   ```
   Swagger UI: http://localhost:5000/swagger

## Adding a Migration

```
dotnet ef migrations add <MigrationName> --project src/KnowledgeBase.Data --startup-project src/KnowledgeBase.Api
```

## API Endpoints

| Method | Route | Description |
|--------|-------|-------------|
| POST | /api/sync | Pull all issues from GitLab and upsert them |
| GET | /api/issues | List issues (`?label=bug&state=opened&author=jsmith`) |
| GET | /api/issues/{id} | Full issue with threaded comment tree |
| PATCH | /api/issues/{id} | Update KB category / notes |
| POST | /api/issues/{id}/comments | Add a comment (supports `parentCommentId`) |
| GET | /api/labels | List all labels |
| GET | /api/users | List all users |

## Project Structure

```
src/
  KnowledgeBase.Core/         — Domain models, DTOs, repository interfaces (no EF references)
    Domain/                   — Issue, Comment, Label, User, IssueLabel
    DTOs/                     — Response/request shapes
    Interfaces/               — IIssueRepository, ICommentRepository, etc.
    Models/GitLab/            — Deserialisation-only response records
  KnowledgeBase.Data/         — EF Core implementation
    Context/                  — KnowledgeBaseDbContext
    Configurations/           — IEntityTypeConfiguration<T> per entity
    Repositories/             — LINQ + Include() implementations
    Services/                 — GitLabService (HTTP), SyncService (orchestration)
    Migrations/               — EF-generated migration history
  KnowledgeBase.Api/          — ASP.NET Core host
    Controllers/              — IssuesController, LabelsController, UsersController, SyncController
    Middleware/               — GlobalExceptionMiddleware
    Program.cs                — DI registration (DbContext, repos, services)
```

## Key Architecture Notes

### Comment Hierarchy
EF Core does not auto-recurse self-referencing navigation properties (`Comment.Replies`). The repository loads the full flat list of comments for an issue in one query, then builds the tree in C# using a dictionary-based O(n) algorithm:

```csharp
var lookup = allComments.ToDictionary(c => c.Id, MapToDto);
foreach (var dto in lookup.Values)
    if (dto.ParentCommentId is null) roots.Add(dto);
    else lookup[dto.ParentCommentId.Value].Replies.Add(dto);
```

The `CommentConfiguration` uses `OnDelete(DeleteBehavior.Restrict)` on the self-referencing FK to avoid cascade delete loops.

### Sync Strategy
`SyncService` calls `FirstOrDefaultAsync` to check whether each entity exists, then either adds or updates it via EF state tracking, followed by `SaveChangesAsync`. Labels are synced first so issue label links can resolve.

### JSON Cycles
Controllers use `ReferenceHandler.IgnoreCycles` (set in `Program.cs`) to prevent serialization errors from bidirectional navigation properties.
