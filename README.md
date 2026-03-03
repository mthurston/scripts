# Scripts Collection
As a hoarder of things that have code-smell I've decided to use this repo as a trove for my precious scripts. 

| Lang | Topic | Description |
|------|-------|-------------|
| **PowerShell** | General | [Beep](./powershell/beep.ps1) - A simple script that beeps every 30 seconds to help get my attention after a batch task has completed. |
|   | Azure DevOps | [Run Pipelines](./powershell/azure-dev-ops/run-pipelines.ps1) - Allows a windows user to kick off multiple Azure DevOps pipelines synchronously. |
|   | RabbitMQ | [Connectivity Tester](./powershell/rabbitmq/connectivity-tester.ps1) - Tests RabbitMQ server connectivity and validates queue operations via Management API and AMQP protocol. |
| **C#** | Data Access | [EF Core → Dapper Migration](./csharp/ef-to-dapper-migration) - Side-by-side ASP.NET Core 8 Web API implementations of the same GitLab knowledge base, one in EF Core 8 and one in Dapper, illustrating a full ORM-to-micro-ORM migration. |