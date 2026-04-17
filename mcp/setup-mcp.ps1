#Requires -Version 5.1
<#
.SYNOPSIS
    Interactive setup script for Claude Code MCP integrations.
.DESCRIPTION
    Prompts for credentials and generates MCP server configuration for
    Atlassian, GitHub, GitLab, Chrome DevTools, and Figma. Optionally
    writes the config to .claude\settings.json (project or global).
#>

Set-StrictMode -Version Latest
$ErrorActionPreference = 'Stop'

function Write-Info    { param($Msg) Write-Host "[info]  $Msg" -ForegroundColor Cyan }
function Write-Ok      { param($Msg) Write-Host "[ok]    $Msg" -ForegroundColor Green }
function Write-Warn    { param($Msg) Write-Host "[warn]  $Msg" -ForegroundColor Yellow }

function Prompt-Value {
    param([string]$Text, [string]$Default = '')
    $hint = if ($Default) { " [$Default]" } else { '' }
    $answer = Read-Host "? $Text$hint"
    if (-not $answer -and $Default) { $Default } else { $answer }
}

function Prompt-Secret {
    param([string]$Text)
    $secure = Read-Host "? $Text" -AsSecureString
    [System.Runtime.InteropServices.Marshal]::PtrToStringAuto(
        [System.Runtime.InteropServices.Marshal]::SecureStringToBSTR($secure)
    )
}

function Prompt-YN {
    param([string]$Text)
    $answer = Read-Host "? $Text [y/N]"
    $answer -match '^[Yy]$'
}

function Test-Command { param($Cmd) $null -ne (Get-Command $Cmd -ErrorAction SilentlyContinue) }

# ── Dependency checks ────────────────────────────────────────────────────────
Write-Host ''
Write-Info 'Checking dependencies...'
foreach ($dep in @('node', 'npx', 'docker')) {
    if (-not (Test-Command $dep)) { Write-Warn "$dep not found — some servers may not work." }
}
Write-Host ''

# ── Collect configuration ────────────────────────────────────────────────────
$Servers = [ordered]@{}

# Atlassian
if (Prompt-YN 'Configure Atlassian (Jira/Confluence)?') {
    $AtlassianToken = Prompt-Secret 'Atlassian API token'
    $Servers['atlassian'] = @{ Type = 'sse'; Token = $AtlassianToken }
}

# GitHub
if (Prompt-YN 'Configure GitHub?') {
    $GitHubPAT = Prompt-Secret 'GitHub Personal Access Token'
    $useDocker = (Test-Command 'docker') -and (Prompt-YN 'Use Docker for GitHub MCP (recommended)?')
    $Servers['github'] = @{ UseDocker = $useDocker; PAT = $GitHubPAT }
}

# GitLab
if (Prompt-YN 'Configure GitLab?') {
    $GitLabPAT    = Prompt-Secret 'GitLab Personal Access Token'
    $GitLabApiUrl = Prompt-Value  'GitLab API URL' 'https://gitlab.com/api/v4'
    $Servers['gitlab'] = @{ PAT = $GitLabPAT; ApiUrl = $GitLabApiUrl }
}

# Chrome DevTools
if (Prompt-YN 'Configure Chrome DevTools MCP?') {
    $ChromePort = Prompt-Value 'Chrome remote debugging port' '9222'
    $Servers['chrome-devtools'] = @{ Port = $ChromePort }
}

# Figma
if (Prompt-YN 'Configure Figma?') {
    $FigmaKey = Prompt-Secret 'Figma Personal Access Token'
    $Servers['figma'] = @{ ApiKey = $FigmaKey }
}

if ($Servers.Count -eq 0) {
    Write-Warn 'No servers selected. Exiting.'
    exit 0
}

# ── Build config object ──────────────────────────────────────────────────────
$McpServers = [ordered]@{}

foreach ($name in $Servers.Keys) {
    $cfg = $Servers[$name]
    switch ($name) {
        'atlassian' {
            $McpServers['atlassian'] = [ordered]@{
                type    = 'sse'
                url     = 'https://mcp.atlassian.com/v1/sse'
                headers = @{ Authorization = "Bearer $($cfg.Token)" }
            }
        }
        'github' {
            if ($cfg.UseDocker) {
                $McpServers['github'] = [ordered]@{
                    command = 'docker'
                    args    = @('run', '-i', '--rm', '-e', 'GITHUB_PERSONAL_ACCESS_TOKEN', 'ghcr.io/github/github-mcp-server')
                    env     = @{ GITHUB_PERSONAL_ACCESS_TOKEN = $cfg.PAT }
                }
            } else {
                $McpServers['github'] = [ordered]@{
                    command = 'npx'
                    args    = @('-y', '@modelcontextprotocol/server-github')
                    env     = @{ GITHUB_PERSONAL_ACCESS_TOKEN = $cfg.PAT }
                }
            }
        }
        'gitlab' {
            $McpServers['gitlab'] = [ordered]@{
                command = 'npx'
                args    = @('-y', '@modelcontextprotocol/server-gitlab')
                env     = @{
                    GITLAB_PERSONAL_ACCESS_TOKEN = $cfg.PAT
                    GITLAB_API_URL               = $cfg.ApiUrl
                }
            }
        }
        'chrome-devtools' {
            $McpServers['chrome-devtools'] = [ordered]@{
                command = 'npx'
                args    = @('-y', '@agentdeskai/browser-tools-mcp@latest')
                env     = @{ CHROME_DEBUGGING_PORT = $cfg.Port }
            }
        }
        'figma' {
            $McpServers['figma'] = [ordered]@{
                command = 'npx'
                args    = @('-y', 'figma-developer-mcp', '--stdio')
                env     = @{ FIGMA_API_KEY = $cfg.ApiKey }
            }
        }
    }
}

$Config     = [ordered]@{ mcpServers = $McpServers }
$ConfigJson = $Config | ConvertTo-Json -Depth 10

# ── Display generated config ─────────────────────────────────────────────────
Write-Host ''
Write-Info 'Generated MCP configuration:'
Write-Host ('─' * 50)
Write-Host $ConfigJson
Write-Host ('─' * 50)

# ── Write config ─────────────────────────────────────────────────────────────
Write-Host ''
Write-Host 'Where would you like to write the config?'
Write-Host '  1) Project settings  (.claude\settings.json in current directory)'
Write-Host '  2) Global settings   ($env:USERPROFILE\.claude\settings.json)'
Write-Host '  3) Print only        (copy it yourself)'
$choice = Read-Host '? Choice [1/2/3]'

function Write-Config {
    param([string]$TargetDir)
    $settingsFile = Join-Path $TargetDir 'settings.json'
    New-Item -ItemType Directory -Force -Path $TargetDir | Out-Null

    if (Test-Path $settingsFile) {
        $backup = "$settingsFile.bak"
        Copy-Item $settingsFile $backup
        $existing = Get-Content $settingsFile -Raw | ConvertFrom-Json -AsHashtable
        if (-not $existing.ContainsKey('mcpServers')) { $existing['mcpServers'] = @{} }
        foreach ($k in $McpServers.Keys) { $existing['mcpServers'][$k] = $McpServers[$k] }
        $existing | ConvertTo-Json -Depth 10 | Set-Content $settingsFile -Encoding UTF8
        Write-Ok "Merged into existing $settingsFile (backup at $backup)"
    } else {
        $ConfigJson | Set-Content $settingsFile -Encoding UTF8
        Write-Ok "Written to $settingsFile"
    }
}

switch ($choice) {
    '1' { Write-Config (Join-Path (Get-Location) '.claude') }
    '2' { Write-Config (Join-Path $env:USERPROFILE '.claude') }
    default { Write-Info 'Config printed above — copy it into your settings.json manually.' }
}

# ── Chrome launch helper ─────────────────────────────────────────────────────
if ($Servers.ContainsKey('chrome-devtools')) {
    Write-Host ''
    Write-Info 'To start Chrome with remote debugging, run:'
    Write-Host '  Start-Process "chrome.exe" "--remote-debugging-port=9222 --no-first-run"' -ForegroundColor DarkGray
}

# ── Environment variable hints ────────────────────────────────────────────────
Write-Host ''
Write-Info 'Add these to your system or user environment variables to avoid hardcoding secrets:'
if ($Servers.ContainsKey('atlassian'))     { Write-Host '  $env:ATLASSIAN_API_TOKEN = "..."' -ForegroundColor DarkGray }
if ($Servers.ContainsKey('github'))        { Write-Host '  $env:GITHUB_PAT          = "..."' -ForegroundColor DarkGray }
if ($Servers.ContainsKey('gitlab'))        { Write-Host '  $env:GITLAB_PAT          = "..."' -ForegroundColor DarkGray }
if ($Servers.ContainsKey('figma'))         { Write-Host '  $env:FIGMA_API_KEY       = "..."' -ForegroundColor DarkGray }
Write-Host ''
Write-Info 'Restart Claude Code and run /mcp to verify connected servers.'
