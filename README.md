# Scripts Collection
As a hoarder of things that have code-smell I've decided to use this repo as a trove for my precious scripts. 

| Lang | Topic | Description |
|------|-------|-------------|
| **PowerShell** | General | [Beep](./powershell/beep.ps1) - A simple script that beeps every 30 seconds to help get my attention after a batch task has completed. |
|   | Azure DevOps | [Run Pipelines](./powershell/azure-dev-ops/run-pipelines.ps1) - Allows a windows user to kick off multiple Azure DevOps pipelines synchronously. |
|   | RabbitMQ | [Connectivity Tester](./powershell/rabbitmq/connectivity-tester.ps1) - Tests RabbitMQ server connectivity and validates queue operations via Management API and AMQP protocol. |
| **MCP** | Claude Code | [MCP Onboarding](./mcp/MCP_ONBOARDING.md) - Setup guide and scripts for Atlassian, GitHub, GitLab, Chrome DevTools, and Figma MCP integrations. |
|   |   | [setup-mcp.sh](./mcp/setup-mcp.sh) - Interactive bash script to generate and write MCP config (Unix/macOS). |
|   |   | [setup-mcp.ps1](./mcp/setup-mcp.ps1) - Interactive PowerShell script to generate and write MCP config (Windows). |