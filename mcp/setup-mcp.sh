#!/usr/bin/env bash
# Interactive setup script for Claude Code MCP integrations.
# Generates MCP server config and optionally writes it to .claude/settings.json.

set -euo pipefail

RED='\033[0;31m'; GREEN='\033[0;32m'; YELLOW='\033[1;33m'; CYAN='\033[0;36m'; NC='\033[0m'
info()    { echo -e "${CYAN}[info]${NC} $*"; }
success() { echo -e "${GREEN}[ok]${NC}   $*"; }
warn()    { echo -e "${YELLOW}[warn]${NC} $*"; }
err()     { echo -e "${RED}[err]${NC}  $*" >&2; }

prompt() {
  local var_name="$1" prompt_text="$2" default="${3:-}"
  if [[ -n "$default" ]]; then
    read -r -p "$(echo -e "${CYAN}?${NC} ${prompt_text} [${default}]: ")" value
    value="${value:-$default}"
  else
    read -r -p "$(echo -e "${CYAN}?${NC} ${prompt_text}: ")" value
  fi
  printf -v "$var_name" '%s' "$value"
}

prompt_secret() {
  local var_name="$1" prompt_text="$2"
  read -r -s -p "$(echo -e "${CYAN}?${NC} ${prompt_text} (hidden): ")" value
  echo
  printf -v "$var_name" '%s' "$value"
}

yn() {
  local answer
  read -r -p "$(echo -e "${CYAN}?${NC} $1 [y/N]: ")" answer
  [[ "$answer" =~ ^[Yy]$ ]]
}

check_dep() {
  command -v "$1" &>/dev/null || { warn "$1 not found — some servers may not work without it."; }
}

# ── Dependency checks ────────────────────────────────────────────────────────
echo
info "Checking dependencies..."
check_dep node
check_dep npx
check_dep docker
echo

# ── Collect configuration per server ────────────────────────────────────────
declare -A SERVERS

# Atlassian
if yn "Configure Atlassian (Jira/Confluence)?"; then
  prompt_secret ATLASSIAN_TOKEN "Atlassian API token"
  prompt ATLASSIAN_SITE_URL "Atlassian site URL" "https://your-org.atlassian.net"
  SERVERS["atlassian"]="sse"
fi

# GitHub
if yn "Configure GitHub?"; then
  prompt_secret GITHUB_PAT "GitHub Personal Access Token"
  if command -v docker &>/dev/null && yn "Use Docker for GitHub MCP (recommended)?"; then
    SERVERS["github"]="docker"
  else
    SERVERS["github"]="npx"
  fi
fi

# GitLab
if yn "Configure GitLab?"; then
  prompt_secret GITLAB_PAT "GitLab Personal Access Token"
  prompt GITLAB_API_URL "GitLab API URL" "https://gitlab.com/api/v4"
  SERVERS["gitlab"]="npx"
fi

# Chrome DevTools
if yn "Configure Chrome DevTools MCP?"; then
  prompt CHROME_PORT "Chrome remote debugging port" "9222"
  SERVERS["chrome-devtools"]="npx"
fi

# Figma
if yn "Configure Figma?"; then
  prompt_secret FIGMA_API_KEY "Figma Personal Access Token"
  SERVERS["figma"]="npx"
fi

if [[ ${#SERVERS[@]} -eq 0 ]]; then
  warn "No servers selected. Exiting."
  exit 0
fi

# ── Build JSON ───────────────────────────────────────────────────────────────
build_json() {
  local json='{\n  "mcpServers": {'
  local first=true

  for server in "${!SERVERS[@]}"; do
    $first || json+=','
    first=false

    case "$server" in
      atlassian)
        json+="\n    \"atlassian\": {
      \"type\": \"sse\",
      \"url\": \"https://mcp.atlassian.com/v1/sse\",
      \"headers\": {
        \"Authorization\": \"Bearer ${ATLASSIAN_TOKEN}\"
      }
    }"
        ;;
      github)
        if [[ "${SERVERS[$server]}" == "docker" ]]; then
          json+="\n    \"github\": {
      \"command\": \"docker\",
      \"args\": [\"run\", \"-i\", \"--rm\", \"-e\", \"GITHUB_PERSONAL_ACCESS_TOKEN\", \"ghcr.io/github/github-mcp-server\"],
      \"env\": {
        \"GITHUB_PERSONAL_ACCESS_TOKEN\": \"${GITHUB_PAT}\"
      }
    }"
        else
          json+="\n    \"github\": {
      \"command\": \"npx\",
      \"args\": [\"-y\", \"@modelcontextprotocol/server-github\"],
      \"env\": {
        \"GITHUB_PERSONAL_ACCESS_TOKEN\": \"${GITHUB_PAT}\"
      }
    }"
        fi
        ;;
      gitlab)
        json+="\n    \"gitlab\": {
      \"command\": \"npx\",
      \"args\": [\"-y\", \"@modelcontextprotocol/server-gitlab\"],
      \"env\": {
        \"GITLAB_PERSONAL_ACCESS_TOKEN\": \"${GITLAB_PAT}\",
        \"GITLAB_API_URL\": \"${GITLAB_API_URL}\"
      }
    }"
        ;;
      chrome-devtools)
        json+="\n    \"chrome-devtools\": {
      \"command\": \"npx\",
      \"args\": [\"-y\", \"@agentdeskai/browser-tools-mcp@latest\"],
      \"env\": {
        \"CHROME_DEBUGGING_PORT\": \"${CHROME_PORT}\"
      }
    }"
        ;;
      figma)
        json+="\n    \"figma\": {
      \"command\": \"npx\",
      \"args\": [\"-y\", \"figma-developer-mcp\", \"--stdio\"],
      \"env\": {
        \"FIGMA_API_KEY\": \"${FIGMA_API_KEY}\"
      }
    }"
        ;;
    esac
  done

  json+='\n  }\n}'
  printf '%b' "$json"
}

GENERATED_JSON="$(build_json)"

echo
info "Generated MCP configuration:"
echo "────────────────────────────────────────"
echo "$GENERATED_JSON"
echo "────────────────────────────────────────"

# ── Write config ─────────────────────────────────────────────────────────────
echo
echo "Where would you like to write the config?"
echo "  1) Project settings  (.claude/settings.json in current directory)"
echo "  2) Global settings   (~/.claude/settings.json)"
echo "  3) Print only        (copy it yourself)"
read -r -p "$(echo -e "${CYAN}?${NC} Choice [1/2/3]: ")" CHOICE

write_config() {
  local target_dir="$1"
  local settings_file="${target_dir}/settings.json"

  mkdir -p "$target_dir"

  if [[ -f "$settings_file" ]]; then
    if command -v jq &>/dev/null; then
      # Merge with existing settings using jq
      local existing
      existing="$(cat "$settings_file")"
      local new_servers
      new_servers="$(echo "$GENERATED_JSON" | jq '.mcpServers')"
      echo "$existing" | jq --argjson servers "$new_servers" '.mcpServers = (.mcpServers // {}) * $servers' > "${settings_file}.tmp"
      mv "${settings_file}.tmp" "$settings_file"
      success "Merged into existing $settings_file"
    else
      warn "jq not found — backing up existing file and overwriting."
      cp "$settings_file" "${settings_file}.bak"
      echo "$GENERATED_JSON" > "$settings_file"
      success "Written to $settings_file (backup at ${settings_file}.bak)"
    fi
  else
    echo "$GENERATED_JSON" > "$settings_file"
    success "Written to $settings_file"
  fi
}

case "$CHOICE" in
  1) write_config ".claude" ;;
  2) write_config "${HOME}/.claude" ;;
  3) info "Config printed above — copy it into your settings.json manually." ;;
  *) warn "Invalid choice. Config printed above." ;;
esac

# ── Shell export hints ────────────────────────────────────────────────────────
echo
info "Add these exports to your shell profile (~/.zshrc or ~/.bashrc) to avoid hardcoding secrets:"
echo
[[ -v ATLASSIAN_TOKEN ]] && echo "  export ATLASSIAN_API_TOKEN=\"${ATLASSIAN_TOKEN:0:4}...\""
[[ -v GITHUB_PAT ]]      && echo "  export GITHUB_PAT=\"${GITHUB_PAT:0:4}...\""
[[ -v GITLAB_PAT ]]      && echo "  export GITLAB_PAT=\"${GITLAB_PAT:0:4}...\""
[[ -v FIGMA_API_KEY ]]   && echo "  export FIGMA_API_KEY=\"${FIGMA_API_KEY:0:4}...\""
echo
info "Restart Claude Code and run /mcp to verify connected servers."
