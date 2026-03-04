githubsso


1 less step -- no SQL to obtain for troubleshooting purposes

winget install Microsoft.DotNet.SDK.10

 you can install LocalDB through the Visual Studio Installer, as part of the Data Storage and Processing workload, the ASP.NET and web development workload

sqllocaldb start MSSQLLocalDB; sqllocaldb info 

MSSQLLocalDB is running. Now in VS Code:

1. Open the SQL Server panel (cylinder icon in the sidebar)
2. Click Add Connection and use:
 - Server: (localdb)\MSSQLLocalDB
 - Auth type: Windows Authentication
 - Database: leave blank (or EfToDapperDemo once it's published)
Then to publish the database schema, open the Database Projects panel, right-click EfToDapper.Database → Publish → target (localdb)\MSSQLLocalDB, database EfToDapperDemo.

provide steps to fork repo

cd src/EfToDapper.Api && dotnet run
# Swagger: http://localhost:5000
# MiniProfiler: http://localhost:5000/profiler/results-index


for this workshop specifically, the MSSQL extension covers everything you'd use SSMS for:

Directly relevant to Scenario 4 (Key Lookup):

Query Plan Visualizer — shows actual execution plans including the "Key Lookup" and "Clustered Index Scan" operators, with cost % highlighting and interactive node collapse/expand. This is the primary thing we were pointing people to SSMS for.
Show Estimated/Actual Execution Plan — available via Command Palette (F1 → MS SQL: Show Estimated Execution Plan) or inline after running a query
Object Explorer — browse the EfToDapperDemo database, tables, indexes
Bonus over SSMS for this workshop:

GitHub Copilot @mssql — participants can paste the execution plan or a slow query and ask Copilot to explain the Key Lookup, suggest index improvements, or optimize the query. This is genuinely useful for a workshop setting.
Everything stays in VS Code — no context-switching
What SSMS still does better (not needed here):

Live Query Statistics (real-time plan while executing)
Extended Events / Profiler GUI
Database backup/restore wizard
Activity Monitor





To use it with the MSSQL extension:

Install the "SQL Database Projects" extension (ms-mssql.sql-database-projects-vscode) if not already installed
In the Database Projects panel, click Open Existing → select database/EfToDapper.Database.sqlproj
Right-click the project → Publish → set target to (localdb)\mssqllocaldb, database EfToDapperDemo
Start the ASP.NET app — EnsureCreated() will find the schema already in place and the seeder will run
Once published, you can also connect via the MSSQL panel to (localdb)\mssqllocaldb → EfToDapperDemo and run queries with actual execution plans (the Explain button) to demonstrate the Scenario 4 Key Lookup.