# Knowledge Base API — Dapper Edition

An ASP.NET Core 8 Web API that syncs GitLab issues into a SQL Server database and exposes them as a searchable knowledge base. This project uses **Dapper** for all data access.

## Teaching Goals

- Raw SQL queries with full visibility and control — no magic translation layer
- Multi-mapping (`splitOn`) to hydrate related objects from a single query result set
- **Recursive CTE** (`WITH CommentTree AS ...`) to load the full comment tree in one SQL round-trip
- Manual object graph construction: the flat CTE result becomes a nested `CommentDto` tree via an O(n) dictionary algorithm
- Schema managed with plain SQL scripts (no ORM migration tooling required)
- `MERGE` statements for efficient, atomic upsert during sync
- `IDbConnectionFactory` pattern for clean connection lifetime management

## Prerequisites

- .NET 8 SDK
- SQL Server or LocalDB (`(localdb)\mssqllocaldb`)
- A GitLab project with an API access token
- `sqlcmd` or SSMS / Azure Data Studio to apply the schema

## Setup

1. **Clone and navigate:**
   ```
   git clone <repo-url>
   cd csharp/knowledge-base-dapper
   ```

2. **Create the database and apply the schema:**
   ```
   sqlcmd -S "(localdb)\mssqllocaldb" -Q "CREATE DATABASE KnowledgeBaseDb"
   sqlcmd -S "(localdb)\mssqllocaldb" -d KnowledgeBaseDb -i src/KnowledgeBase.Data/Schema/001_InitialSchema.sql
   ```
   The script is idempotent — safe to run multiple times.

3. **Configure GitLab credentials** in `src/KnowledgeBase.Api/appsettings.json`:
   ```json
   {
     "GitLab": {
       "BaseUrl": "https://gitlab.com",
       "ProjectId": "YOUR_PROJECT_ID",
       "AccessToken": "YOUR_PRIVATE_TOKEN"
     }
   }
   ```

4. **Run the API:**
   ```
   dotnet run --project src/KnowledgeBase.Api
   ```
   Swagger UI: http://localhost:5000/swagger

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
  KnowledgeBase.Core/         — Domain models, DTOs, repository + connection interfaces
    Domain/                   — Issue, Comment, Label, User, IssueLabel
    DTOs/                     — Response/request shapes
    Interfaces/               — IIssueRepository, ICommentRepository, IDbConnectionFactory, etc.
    Models/GitLab/            — Deserialisation-only response records
  KnowledgeBase.Data/         — Dapper implementation
    SqlQueries/               — Static string constants for all SQL (IssueQueries, CommentQueries, etc.)
    Repositories/             — Raw SQL + multi-mapping implementations
    Services/                 — GitLabService (HTTP), SyncService (MERGE-based orchestration)
    Schema/                   — 001_InitialSchema.sql (replaces EF migrations)
    SqlConnectionFactory.cs   — IDbConnectionFactory implementation
  KnowledgeBase.Api/          — ASP.NET Core host
    Controllers/              — IssuesController, LabelsController, UsersController, SyncController
    Middleware/               — GlobalExceptionMiddleware
    Program.cs                — DI registration (IDbConnectionFactory, repos, services)
```

## Key Architecture Notes

### Comment Hierarchy — Recursive CTE
The recursive CTE in `CommentQueries.GetThreadedByIssueId` fetches the entire comment tree for an issue in one SQL round-trip:

```sql
WITH CommentTree AS (
    -- Anchor: root comments
    SELECT c.*, 0 AS Depth FROM Comments c
    WHERE c.IssueId = @IssueId AND c.ParentCommentId IS NULL

    UNION ALL

    -- Recursive: attach children to their parents
    SELECT c.*, ct.Depth + 1 FROM Comments c
    INNER JOIN CommentTree ct ON c.ParentCommentId = ct.Id
)
SELECT ct.*, u.Id AS AuthorId, u.Name AS AuthorName, u.Username AS AuthorUsername
FROM CommentTree ct INNER JOIN Users u ON u.Id = ct.AuthorId
ORDER BY Depth, CreatedAt
OPTION (MAXRECURSION 100);
```

The flat result is converted into a nested tree by `BuildCommentTree()` — the same O(n) dictionary algorithm used in the EF Core edition, making the two implementations directly comparable.

### Sync Strategy — MERGE
Each entity type uses a SQL `MERGE` statement for idempotent upsert:

```sql
MERGE Users AS target
USING (SELECT @GitLabUserId, @Name, @Username) AS source ON target.GitLabUserId = source.GitLabUserId
WHEN MATCHED THEN UPDATE SET Name = source.Name, Username = source.Username
WHEN NOT MATCHED THEN INSERT (GitLabUserId, Name, Username) VALUES (...)
OUTPUT INSERTED.Id;
```

Issue + label link sync runs inside a transaction: label links are deleted and re-inserted atomically.

### Multi-mapping for Labels
The `GetAll` query for issues LEFT JOINs labels, returning one row per label per issue. The repository collapses these into grouped objects using a dictionary keyed by issue ID.

### Connection Lifetime
`SqlConnectionFactory` is registered as `Singleton`; each repository method opens and disposes its own `IDbConnection` via `using`. This avoids connection lifetime coupling between repositories in the same request.
