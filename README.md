# Scripts Collection
As a hoarder of things that have code-smell I've decided to use this repo as a trove for my precious scripts. 

| Lang | Topic | Description |
|------|-------|-------------|
| **PowerShell** | General | [Beep](./powershell/beep.ps1) - A simple script that beeps every 30 seconds to help get my attention after a batch task has completed. |
|   | Azure DevOps | [Run Pipelines](./powershell/azure-dev-ops/run-pipelines.ps1) - Allows a windows user to kick off multiple Azure DevOps pipelines synchronously. |
|   | RabbitMQ | [Connectivity Tester](./powershell/rabbitmq/connectivity-tester.ps1) - Tests RabbitMQ server connectivity and validates queue operations via Management API and AMQP protocol. |
| **C#** | Data Access | [Knowledge Base API — EF Core](./csharp/knowledge-base-ef) - ASP.NET Core 8 Web API syncing GitLab issues into SQL Server using Entity Framework Core 8. Demonstrates DbContext, migrations, LINQ eager loading, and self-referencing comment hierarchy. |
|   | Data Access | [Knowledge Base API — Dapper](./csharp/knowledge-base-dapper) - The same API implemented with Dapper. Demonstrates raw SQL, recursive CTEs for threaded comments, multi-mapping, MERGE-based upsert, and manual schema management. |