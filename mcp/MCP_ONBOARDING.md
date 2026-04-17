# MCP Onboarding Guide

Model Context Protocol (MCP) extends Claude Code with integrations to external tools and services. This guide covers setup for five integrations.

---

## Prerequisites

- [Claude Code CLI](https://docs.anthropic.com/en/docs/claude-code) installed
- Node.js 18+ and npm (for npx-based servers)
- Docker (optional, for container-based servers)

MCP servers are configured in `.claude/settings.json` (project-level) or `~/.claude/settings.json` (global). Use the setup script to generate config automatically:

```bash
# Unix/macOS
bash mcp/setup-mcp.sh

# Windows (PowerShell)
.\mcp\setup-mcp.ps1
```

---

## 1. Atlassian MCP

Provides access to Jira and Confluence via the Atlassian Remote MCP server.

### Prerequisites
- Atlassian account with API token — generate at: https://id.atlassian.com/manage-profile/security/api-tokens
- Your Atlassian site URL (e.g. `https://your-org.atlassian.net`)

### Configuration

```json
{
  "mcpServers": {
    "atlassian": {
      "type": "sse",
      "url": "https://mcp.atlassian.com/v1/sse",
      "headers": {
        "Authorization": "Bearer YOUR_ATLASSIAN_API_TOKEN"
      }
    }
  }
}
```

### Capabilities
- Search and read Jira issues, projects, and boards
- Create/update Jira issues and comments
- Search and read Confluence spaces and pages
- Create/update Confluence pages

---

## 2. GitHub MCP

Official GitHub MCP server providing repository, issue, PR, and code search access.

### Prerequisites
- GitHub Personal Access Token (classic or fine-grained) — create at: https://github.com/settings/tokens
- Required scopes: `repo`, `read:org`, `read:user`, `gist` (adjust to your needs)

### Option A — Docker (recommended)

```json
{
  "mcpServers": {
    "github": {
      "command": "docker",
      "args": [
        "run", "-i", "--rm",
        "-e", "GITHUB_PERSONAL_ACCESS_TOKEN",
        "ghcr.io/github/github-mcp-server"
      ],
      "env": {
        "GITHUB_PERSONAL_ACCESS_TOKEN": "YOUR_GITHUB_PAT"
      }
    }
  }
}
```

### Option B — npx

```json
{
  "mcpServers": {
    "github": {
      "command": "npx",
      "args": ["-y", "@modelcontextprotocol/server-github"],
      "env": {
        "GITHUB_PERSONAL_ACCESS_TOKEN": "YOUR_GITHUB_PAT"
      }
    }
  }
}
```

### Capabilities
- Read/create/update repositories, issues, and pull requests
- Search code across GitHub
- Manage branches, commits, and file contents
- Review and comment on pull requests

---

## 3. GitLab MCP

Community MCP server for GitLab repository and issue management.

### Prerequisites
- GitLab Personal Access Token — create at: https://gitlab.com/-/user_settings/personal_access_tokens
- Required scopes: `api`, `read_repository`
- Self-hosted GitLab: set `GITLAB_API_URL` to your instance URL

### Configuration

```json
{
  "mcpServers": {
    "gitlab": {
      "command": "npx",
      "args": ["-y", "@modelcontextprotocol/server-gitlab"],
      "env": {
        "GITLAB_PERSONAL_ACCESS_TOKEN": "YOUR_GITLAB_PAT",
        "GITLAB_API_URL": "https://gitlab.com/api/v4"
      }
    }
  }
}
```

> For self-hosted GitLab, replace `https://gitlab.com/api/v4` with `https://your-gitlab.example.com/api/v4`.

### Capabilities
- Read/create/update projects, issues, and merge requests
- Browse repository files and commits
- Search across GitLab projects
- Manage comments and labels

---

## 4. Chrome DevTools MCP

Connects Claude to a live Chrome browser session via the Chrome DevTools Protocol, enabling browser automation and inspection.

### Prerequisites
- Chrome or Chromium installed
- The `browser-tools-mcp` companion Chrome extension (optional but recommended for full DOM/console access)

### Step 1 — Launch Chrome with remote debugging

```bash
# macOS
/Applications/Google\ Chrome.app/Contents/MacOS/Google\ Chrome \
  --remote-debugging-port=9222 --no-first-run --no-default-browser-check

# Linux
google-chrome --remote-debugging-port=9222 --no-first-run

# Windows (PowerShell)
Start-Process "chrome.exe" "--remote-debugging-port=9222 --no-first-run"
```

### Step 2 — MCP configuration

```json
{
  "mcpServers": {
    "chrome-devtools": {
      "command": "npx",
      "args": ["-y", "@agentdeskai/browser-tools-mcp@latest"],
      "env": {
        "CHROME_DEBUGGING_PORT": "9222"
      }
    }
  }
}
```

### Capabilities
- Take screenshots and inspect the DOM
- Read browser console logs and network requests
- Execute JavaScript in the page context
- Navigate pages and interact with elements
- Audit accessibility and performance

---

## 5. Figma MCP

Provides read access to Figma files, components, and design tokens — useful for implementing designs directly from Figma.

### Prerequisites
- Figma account with a Personal Access Token — create at: https://www.figma.com/settings (scroll to "Personal access tokens")
- File key from any Figma file URL: `figma.com/design/FILE_KEY/...`

### Configuration

```json
{
  "mcpServers": {
    "figma": {
      "command": "npx",
      "args": ["-y", "figma-developer-mcp", "--stdio"],
      "env": {
        "FIGMA_API_KEY": "YOUR_FIGMA_PAT"
      }
    }
  }
}
```

### Capabilities
- Read Figma file structure, frames, and layers
- Extract design tokens (colors, typography, spacing)
- Export component metadata and layout properties
- Inspect assets and image references

---

## Complete Combined Configuration

Add all servers to a single config block. Store secrets as environment variables rather than hardcoding them.

```json
{
  "mcpServers": {
    "atlassian": {
      "type": "sse",
      "url": "https://mcp.atlassian.com/v1/sse",
      "headers": {
        "Authorization": "Bearer ${ATLASSIAN_API_TOKEN}"
      }
    },
    "github": {
      "command": "docker",
      "args": [
        "run", "-i", "--rm",
        "-e", "GITHUB_PERSONAL_ACCESS_TOKEN",
        "ghcr.io/github/github-mcp-server"
      ],
      "env": {
        "GITHUB_PERSONAL_ACCESS_TOKEN": "${GITHUB_PAT}"
      }
    },
    "gitlab": {
      "command": "npx",
      "args": ["-y", "@modelcontextprotocol/server-gitlab"],
      "env": {
        "GITLAB_PERSONAL_ACCESS_TOKEN": "${GITLAB_PAT}",
        "GITLAB_API_URL": "https://gitlab.com/api/v4"
      }
    },
    "chrome-devtools": {
      "command": "npx",
      "args": ["-y", "@agentdeskai/browser-tools-mcp@latest"],
      "env": {
        "CHROME_DEBUGGING_PORT": "9222"
      }
    },
    "figma": {
      "command": "npx",
      "args": ["-y", "figma-developer-mcp", "--stdio"],
      "env": {
        "FIGMA_API_KEY": "${FIGMA_API_KEY}"
      }
    }
  }
}
```

Set the corresponding environment variables in your shell profile (`~/.zshrc`, `~/.bashrc`, or Windows environment variables):

```bash
export ATLASSIAN_API_TOKEN="your-token"
export GITHUB_PAT="your-token"
export GITLAB_PAT="your-token"
export FIGMA_API_KEY="your-token"
```

---

## Verify Setup

After configuration, restart Claude Code and run:

```
/mcp
```

This lists all connected MCP servers and their status. A green indicator means the server is connected and tools are available.

---

## Troubleshooting

| Issue | Fix |
|-------|-----|
| Server not appearing in `/mcp` | Check JSON syntax in settings.json; restart Claude Code |
| `command not found: npx` | Install Node.js 18+ from https://nodejs.org |
| Docker server fails to start | Ensure Docker Desktop is running; pull the image manually with `docker pull ghcr.io/github/github-mcp-server` |
| Atlassian 401 Unauthorized | Regenerate your API token; confirm the `Authorization` header format is `Bearer <token>` |
| Chrome DevTools connection refused | Confirm Chrome launched with `--remote-debugging-port=9222`; check no firewall blocks port 9222 |
| Figma 403 Forbidden | Verify the PAT has not expired; confirm it has file read permissions |
