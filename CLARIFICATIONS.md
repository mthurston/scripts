# Clarifications Required Before Execution

These questions need answers before development begins. They are grouped by
area of impact. High-priority questions (marked **[BLOCKING]**) must be
answered before Phase 1 can start. Others can be deferred to their relevant
phase.

---

## 1. GitHub Access & Scope

### 1.1 [BLOCKING] Authentication method
Will this tool use:
- **Personal Access Token (PAT)** — simplest, tied to one user's identity
- **GitHub App** — preferred for org-wide access, has higher rate limits
  (15,000 req/hr vs 5,000 for PAT), actions are attributed to the app not a user
- **OAuth App** — similar to PAT but can act on behalf of multiple users

> Impact: determines `fetchers/` auth module design and token storage approach.

### 1.2 [BLOCKING] Repository scope
- One specific repo, a list of repos, or all repos in a GitHub org?
- Are any repos private? (affects PAT scope required: `repo` vs `public_repo`)
- Are there repos across multiple GitHub orgs or only one?

> Impact: CLI interface, config schema, and DB `repositories` table usage.

### 1.3 [BLOCKING] GitHub.com vs GitHub Enterprise Server (GHES)
- Is this targeting `github.com` or an internal GHES instance?
- If GHES: what version? (API surface differs slightly below v3.6)

> Impact: base API URL, TLS/certificate handling.

---

## 2. Data Scope & Retention

### 2.1 [BLOCKING] Historical depth
- Should the first run scrape **all** historical PRs, or only PRs from a
  specific date forward?
- If all history: some large repos have thousands of PRs — is a long initial
  sync acceptable?

> Impact: default `--since` value, progress reporting, estimated API call
> volume.

### 2.2 Closed vs open PRs
- Track only **merged** PRs, or also **closed-without-merge** and **open** PRs?
- Draft PRs: include or skip?

> Impact: API filter parameters, DB state values.

### 2.3 [BLOCKING] Patch/diff storage
Raw diff patches for changed files can be large (hundreds of KB per PR).
- Store the full raw patch in the DB? (enables offline line-level analysis)
- Store only line counts? (smaller DB, still enables churn metrics)
- Store nothing from files endpoint, and re-fetch on demand?

> Impact: `pr_files.patch` column, DB size projection.

### 2.4 Attachment handling
The plan parses image/file URLs from comment bodies. Options:
- **Parse and record URL only** (no download) — always safe, no storage cost
- **Download to local file store** — enables offline viewing, requires disk
  budget and a download path config
- **Download + deduplicate by content hash** — most complete, most complex

> If downloading: where should files be stored? Same directory as DB? A
> separate configurable path?

---

## 3. Comment Classification

### 3.1 Classification taxonomy agreement
The plan proposes these labels: `nit`, `observation`, `suggestion`, `issue`,
`question`, `praise`, `other`.

- Does this match your team's mental model?
- Are there labels you'd add (e.g., `security`, `performance`, `test-coverage`)?
- Are there labels you'd remove or rename?

### 3.2 [BLOCKING] LLM classifier requirement
Keyword-based classification will miss nuance. Two options:
- **Keyword only** — ships faster, zero cost, works offline, ~70% accuracy
- **LLM-assisted** — higher accuracy on nuanced comments, requires API key and
  network access (or local model)

If LLM-assisted:
- Acceptable to use the **Claude API** (Anthropic) for classification?
- Or prefer a **local model** (e.g., Ollama + llama3) to avoid sending
  comment text to a third-party service?
- Is comment text considered sensitive/confidential?

### 3.3 Manual override workflow
When auto-classification is wrong, should corrections be:
- Made via a **CLI command** (plan's current approach)
- Made by **editing a YAML/CSV file** and re-importing
- Via a **simple web UI** (out of scope for now, but worth knowing if planned)

---

## 4. Collaboration Scoring

### 4.1 Score definition sign-off
Before implementing, confirm the scoring model (see PLAN.md Phase 6) is
directionally correct:

- **Reviewer score** is weighted toward quality and specificity of feedback
  (`issue` > `suggestion` > `nit`)
- **Author score** is weighted toward responsiveness — addressing issues,
  answering questions, low iteration count

Are there team behaviors the score should actively reward or penalize beyond
what's listed?

Examples of possible additions:
- Penalize leaving a PR open without review for > N days
- Reward reviewers who catch bugs that would have reached production
- Penalize authors who re-open issues that were already addressed

### 4.2 "Addressed" definition
The plan marks a comment as **addressed** if a subsequent commit touches the
same file. This can have false positives (unrelated changes to the same file).

More precise alternatives:
- Same file **and** same or adjacent line range
- Thread is explicitly **resolved** in GitHub UI
- Author **replied** to the comment (any reply counts)
- Combination of the above

Which is acceptable? Stricter = more accurate but harder to compute.

### 4.3 Score visibility
Will scores be:
- Internal tooling only (no one sees their own score)
- Shared with team members
- Used in performance reviews

> This matters for how the scoring formula is calibrated and communicated.
> Gameable metrics should be designed carefully if scores are visible.

---

## 5. Infrastructure & Operations

### 5.1 [BLOCKING] Execution environment
Where will this tool run?
- **Developer laptop** (one-off or scheduled via cron)
- **CI/CD runner** (e.g., GitHub Actions on a schedule)
- **Always-on server** (could support webhooks for real-time updates)

> Impact: packaging format (standalone script vs installable package vs Docker
> image), secrets management approach.

### 5.2 Scheduling / incremental sync
- How often should data be refreshed? (daily, hourly, on-demand only)
- Should closed/merged PRs be re-scraped, or treated as immutable once merged?

> Re-scraping merged PRs catches retroactive comment edits and late reviews,
> but costs extra API calls.

### 5.3 DB location and sharing
- Is the SQLite file on each developer's machine (personal analytics)?
- Or a single shared instance (e.g., on a NAS, shared drive, or sync'd via
  git-lfs)?

> SQLite is single-writer; if multiple people run the scraper concurrently
> against a shared file, locking issues arise. A shared Postgres instance
> would be preferable for that case — but the plan currently targets SQLite.

### 5.4 Secrets management
Where is the GitHub token stored?
- `.env` file (developer laptop, gitignored)
- Environment variable injected by CI
- Secret manager (Vault, AWS Secrets Manager, GitHub Actions secrets)

---

## 6. Edge Cases & Team Conventions

### 6.1 Bot accounts
Many repos have bots posting comments (Dependabot, codecov, linters, custom
CI bots). Should bot comments be:
- **Excluded entirely** from analysis
- **Stored but flagged** (`users.is_bot`) and excluded from scoring
- **Treated like human comments**

### 6.2 Comment edits
GitHub comment bodies can be edited after posting. Should the scraper:
- Store only the **current body** (simple)
- Store an **edit history** (requires polling or webhook events)

### 6.3 Deleted comments
Deleted comments return 404 on the API. When a comment disappears between
syncs, should it be:
- **Soft-deleted** (`deleted_at` timestamp, body preserved)
- **Hard-deleted** from the DB

### 6.4 Large comment threads
Some PRs have hundreds of comments (e.g., automated linter output). Is there
a volume threshold above which a PR should be treated differently or excluded
from scoring?

### 6.5 Multi-commit review rounds
The plan defines a "review round" as: reviews between two pushes. Do you
agree with this definition, or should a round be defined differently (e.g.,
by calendar day, by explicit re-request-review action)?

---

## 7. Reporting & Output

### 7.1 Primary consumer of the data
Who queries the database and how?
- **Developers** running SQL directly
- **Team lead** who wants a summary report
- **Both** — and a CLI report command is sufficient for now

### 7.2 Export formats needed
The plan includes CSV and JSON export. Is that sufficient, or is there a need
for:
- Markdown tables (for posting to Slack/Confluence)
- Excel/XLSX
- Integration with an existing BI tool (Metabase, Grafana, etc.)

### 7.3 Desired first report
What is the **single most valuable report** to have working first? Candidates:
- "Who are my most thorough reviewers?" (feedback quality)
- "Which PRs had the most back-and-forth?" (churn ranking)
- "How quickly do authors respond to review feedback?" (responsiveness)
- "What types of issues do reviewers most commonly find?" (classification breakdown)

This will guide which phase gets prioritized.

---

## Summary Table

| # | Question | Priority | Affects Phases |
|---|----------|----------|----------------|
| 1.1 | Auth method | BLOCKING | 1, 2 |
| 1.2 | Repo scope | BLOCKING | 1, 2, CLI |
| 1.3 | GitHub.com vs GHES | BLOCKING | 2 |
| 2.1 | Historical depth | BLOCKING | 2, CLI |
| 2.3 | Patch storage | BLOCKING | 3, DB |
| 3.2 | LLM classifier | BLOCKING | 4, 8 |
| 5.1 | Execution environment | BLOCKING | 1, packaging |
| 1-7 all others | Preferences / refinements | Non-blocking | Various |
