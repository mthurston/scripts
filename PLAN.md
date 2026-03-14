# GitHub PR Analytics Scraper — Implementation Plan

## Overview

A Python application that scrapes GitHub pull request data into a local SQLite
database, then computes collaboration metrics including comment classification,
code churn attribution, and per-user collaboration scores.

---

## Goals

| Goal | Description |
|------|-------------|
| **Data collection** | Capture all PR activity: comments, reviews, commits, file changes, attachments |
| **Comment classification** | Label each comment as observation / nit / suggestion / issue |
| **Churn analysis** | Measure code change volume between comment threads and subsequent commits |
| **Collaboration scoring** | Score each participant based on quality of feedback and author responsiveness |
| **Queryable store** | All data in SQLite so the team can run ad-hoc SQL against it |

---

## High-Level Architecture

```
┌─────────────────┐      GitHub REST/GraphQL API
│  CLI / Scheduler│ ──────────────────────────────►  GitHub
└────────┬────────┘                                   │
         │                                            │  rate-limited
         ▼                                            ▼
┌─────────────────┐     ┌──────────────────────────────────┐
│  Scraper Layer  │◄────│  Fetchers (PRs, Reviews,         │
│  (orchestrator) │     │  Comments, Commits, Files)       │
└────────┬────────┘     └──────────────────────────────────┘
         │
         ▼
┌─────────────────┐
│  Transformer    │  parse & normalize GitHub API responses
└────────┬────────┘
         │
         ▼
┌─────────────────┐
│  Classifier     │  keyword + heuristic comment labelling
│  (optional LLM) │
└────────┬────────┘
         │
         ▼
┌─────────────────┐
│  SQLite DB      │  persist all entities
└────────┬────────┘
         │
         ▼
┌─────────────────┐
│  Metrics Engine │  churn, collaboration score, reports
└─────────────────┘
```

---

## Phase 1 — Project Skeleton

**Deliverables**
- `pyproject.toml` / `requirements.txt`
- `.env.example` with all required environment variables
- Package layout:

```
gh_pr_analytics/
├── __init__.py
├── cli.py            # entry point (click or argparse)
├── config.py         # env/config loading
├── db/
│   ├── schema.py     # CREATE TABLE statements + migrations
│   └── session.py    # SQLite connection helper
├── fetchers/
│   ├── prs.py
│   ├── reviews.py
│   ├── comments.py
│   ├── commits.py
│   └── files.py
├── transformer.py    # raw API → typed dataclasses
├── classifier.py     # comment classification
├── metrics/
│   ├── churn.py
│   └── collaboration.py
└── reports.py        # pre-built SQL queries / formatters
```

**Key dependencies**
- `PyGithub` or raw `httpx` with GitHub REST API (REST is simpler; GraphQL
  enables fewer round-trips — see CLARIFICATIONS.md)
- `click` for the CLI
- `python-dotenv` for secrets
- `sqlite3` (stdlib)

---

## Phase 2 — Database Schema

### Entity Relationship Summary

```
repositories ──< pull_requests ──< reviews
                               ──< review_comments   >── users
                               ──< issue_comments    >── users
                               ──< commits           >── users
                               ──< pr_files
                               ──< attachments

pull_requests >──< pr_participants (users)

comment_classifications  (1:1 with review_comments / issue_comments)
collaboration_scores     (per user per PR, recomputed on demand)
```

### Table Definitions

#### `repositories`
| Column | Type | Notes |
|--------|------|-------|
| id | INTEGER PK | |
| owner | TEXT | GitHub org/user |
| name | TEXT | repo name |
| full_name | TEXT UNIQUE | `owner/name` |
| last_scraped_at | DATETIME | |

#### `users`
| Column | Type | Notes |
|--------|------|-------|
| id | INTEGER PK | GitHub user id |
| login | TEXT UNIQUE | |
| display_name | TEXT | |
| avatar_url | TEXT | |

#### `pull_requests`
| Column | Type | Notes |
|--------|------|-------|
| id | INTEGER PK | GitHub PR id |
| repo_id | INTEGER FK | |
| number | INTEGER | PR # within repo |
| title | TEXT | |
| body | TEXT | PR description |
| author_id | INTEGER FK users | |
| state | TEXT | open / closed / merged |
| is_draft | BOOLEAN | |
| base_branch | TEXT | |
| head_branch | TEXT | |
| created_at | DATETIME | |
| updated_at | DATETIME | |
| merged_at | DATETIME | |
| closed_at | DATETIME | |
| additions | INTEGER | total lines added |
| deletions | INTEGER | total lines deleted |
| changed_files | INTEGER | |
| merge_commit_sha | TEXT | |
| UNIQUE | (repo_id, number) | |

#### `reviews`
| Column | Type | Notes |
|--------|------|-------|
| id | INTEGER PK | GitHub review id |
| pr_id | INTEGER FK | |
| reviewer_id | INTEGER FK users | |
| state | TEXT | APPROVED / CHANGES_REQUESTED / COMMENTED / DISMISSED |
| body | TEXT | top-level review body |
| submitted_at | DATETIME | |
| commit_sha | TEXT | reviewed at this commit |

#### `review_comments`
Inline comments on specific lines of a diff.

| Column | Type | Notes |
|--------|------|-------|
| id | INTEGER PK | GitHub comment id |
| pr_id | INTEGER FK | |
| review_id | INTEGER FK reviews | nullable (standalone inline comment) |
| author_id | INTEGER FK users | |
| in_reply_to_id | INTEGER | parent comment id (for threads) |
| path | TEXT | file path |
| line | INTEGER | line number in the file |
| original_line | INTEGER | line at time of comment |
| diff_hunk | TEXT | surrounding diff context |
| body | TEXT | |
| created_at | DATETIME | |
| updated_at | DATETIME | |
| is_resolved | BOOLEAN | |

#### `issue_comments`
Top-level (non-inline) PR conversation comments.

| Column | Type | Notes |
|--------|------|-------|
| id | INTEGER PK | GitHub comment id |
| pr_id | INTEGER FK | |
| author_id | INTEGER FK users | |
| body | TEXT | |
| created_at | DATETIME | |
| updated_at | DATETIME | |

#### `commits`
| Column | Type | Notes |
|--------|------|-------|
| id | INTEGER PK | |
| pr_id | INTEGER FK | |
| sha | TEXT UNIQUE | |
| author_id | INTEGER FK users | |
| committer_id | INTEGER FK users | |
| message | TEXT | |
| additions | INTEGER | |
| deletions | INTEGER | |
| committed_at | DATETIME | |

#### `pr_files`
Per-file diff stats per commit snapshot (taken at scrape time from the PR's
final diff).

| Column | Type | Notes |
|--------|------|-------|
| id | INTEGER PK | |
| pr_id | INTEGER FK | |
| commit_sha | TEXT | which commit this snapshot is from |
| path | TEXT | |
| status | TEXT | added / modified / removed / renamed |
| additions | INTEGER | |
| deletions | INTEGER | |
| patch | TEXT | raw diff patch (optional, can be large) |

#### `attachments`
Images and files linked in comment bodies (parsed from Markdown).

| Column | Type | Notes |
|--------|------|-------|
| id | INTEGER PK | |
| source_type | TEXT | review_comment / issue_comment |
| source_id | INTEGER | FK to respective comment table |
| url | TEXT | original GitHub asset URL |
| filename | TEXT | |
| local_path | TEXT | if downloaded locally |
| detected_at | DATETIME | |

#### `comment_classifications`
One row per comment (review or issue).

| Column | Type | Notes |
|--------|------|-------|
| id | INTEGER PK | |
| comment_type | TEXT | review_comment / issue_comment |
| comment_id | INTEGER | |
| classification | TEXT | observation / nit / suggestion / issue / praise / question / other |
| confidence | REAL | 0–1, null if manually set |
| method | TEXT | keyword / llm / manual |
| classified_at | DATETIME | |
| UNIQUE | (comment_type, comment_id) | |

#### `churn_intervals`
Tracks how much code changed after a comment thread was posted.

| Column | Type | Notes |
|--------|------|-------|
| id | INTEGER PK | |
| pr_id | INTEGER FK | |
| comment_type | TEXT | |
| comment_id | INTEGER | anchor comment that triggered interval |
| from_commit_sha | TEXT | commit just before comment posted |
| to_commit_sha | TEXT | next commit(s) after comment |
| files_affected | INTEGER | files that changed |
| lines_added | INTEGER | |
| lines_deleted | INTEGER | |
| same_path_changed | BOOLEAN | comment's file was touched in interval |
| computed_at | DATETIME | |

#### `collaboration_scores`
Recomputed on demand, stored for history.

| Column | Type | Notes |
|--------|------|-------|
| id | INTEGER PK | |
| pr_id | INTEGER FK | |
| user_id | INTEGER FK users | |
| role | TEXT | author / reviewer |
| feedback_quality_score | REAL | reviewer: weighted sum of comment classifications |
| responsiveness_score | REAL | author: % of issues/suggestions addressed |
| churn_attribution_score | REAL | churn caused vs. churn resolved |
| iteration_count | INTEGER | review rounds before merge |
| total_score | REAL | composite |
| computed_at | DATETIME | |

---

## Phase 3 — GitHub API Fetchers

### Authentication
- Personal Access Token (PAT) or GitHub App private key
- Token stored in `.env` as `GITHUB_TOKEN`

### Rate Limiting Strategy
- Check `X-RateLimit-Remaining` header on each response
- If remaining < 50, sleep until `X-RateLimit-Reset`
- Use conditional requests (`If-None-Match` / ETag) for incremental syncs

### Incremental Sync
- Store `last_scraped_at` per repo
- On subsequent runs, only fetch PRs updated after `last_scraped_at`
- Use `?since=` parameter on comment endpoints

### Endpoints Required

| Data | Endpoint |
|------|----------|
| PRs | `GET /repos/{owner}/{repo}/pulls?state=all` |
| PR detail | `GET /repos/{owner}/{repo}/pulls/{number}` |
| Reviews | `GET /repos/{owner}/{repo}/pulls/{number}/reviews` |
| Review comments | `GET /repos/{owner}/{repo}/pulls/{number}/comments` |
| Issue comments | `GET /repos/{owner}/{repo}/issues/{number}/comments` |
| Commits | `GET /repos/{owner}/{repo}/pulls/{number}/commits` |
| Files | `GET /repos/{owner}/{repo}/pulls/{number}/files` |
| Commit detail | `GET /repos/{owner}/{repo}/commits/{sha}` |

All paginated endpoints will use the `Link` header for cursor-based paging.

---

## Phase 4 — Comment Classifier

### Classification Taxonomy

| Label | Description | Example signal words |
|-------|-------------|----------------------|
| `nit` | Minor style / cosmetic | "nit:", "nitpick", "minor:", "style:" |
| `observation` | Factual remark, no action required | "I notice", "FYI", "just noting" |
| `suggestion` | Improvement idea, optional | "consider", "might want", "could", "what if" |
| `issue` | Problem that should be fixed | "bug", "broken", "incorrect", "should", "must", "error", "fail" |
| `question` | Clarification request | "?", "why", "what does", "how does" |
| `praise` | Positive feedback | "nice", "great", "love this", "well done" |
| `other` | Catch-all | |

### Classification Pipeline

1. **Pre-processing**: strip code blocks, strip quoted text (replies)
2. **Explicit prefix detection**: `nit:`, `issue:`, `suggestion:` at start of
   comment — highest confidence
3. **Keyword/regex matching**: weighted scoring against each label's word list
4. **Optional LLM pass**: if `CLASSIFIER_MODEL` env var is set (e.g. a local
   Ollama model or Claude API key), send ambiguous comments for LLM
   classification
5. **Manual override**: CLI command `classify override <comment_id> <label>`
   writes to `comment_classifications` with `method=manual`

### Scoring Weights for Collaboration Score

| Classification | Reviewer weight | Author response weight |
|----------------|-----------------|----------------------|
| `issue` | 3.0 | 3.0 (expected to fix) |
| `suggestion` | 2.0 | 1.5 (expected to address or explain) |
| `question` | 1.5 | 2.0 (must respond) |
| `nit` | 0.5 | 0.5 (optional) |
| `observation` | 0.5 | 0.0 (no action needed) |
| `praise` | 0.0 | 0.0 |

---

## Phase 5 — Churn Analysis

### Approach

For each comment posted at time `T` on commit `C_before`:
1. Find the next commit `C_after` pushed to the PR after `T`
2. Fetch the diff between `C_before` and `C_after`
3. Record lines added/deleted, and whether the file the comment was on was
   touched

### "Addressed" Heuristic

A comment is considered **addressed** if:
- An `issue` or `suggestion` comment is followed by a commit that touches the
  same file path within a configurable window (default: next 3 commits)
- OR the thread is marked **resolved** (GitHub's resolve conversation feature)
- OR the PR author replied to the thread acknowledging/declining the change

A comment is considered **declined/discussed** if:
- The PR author replied without any subsequent code change
- The reviewer approved after the response without pushing back

### Churn Score

```
churn_score(PR) = Σ(lines_changed_in_interval) / total_PR_lines_changed
```

High churn relative to PR size suggests significant back-and-forth. This is
stored per-interval and aggregated per PR.

---

## Phase 6 — Collaboration Scoring

### Per-Reviewer Score

```
feedback_quality = Σ(classification_weight * comment) / total_comments
                   — penalize if reviewer leaves only praise/observations
                   — bonus if issues found prevented bugs (heuristic: issue
                     comments that were addressed)
```

### Per-Author Score

```
responsiveness = addressed_comments / (issue_comments + suggestion_comments)
               — comments declined with explanation count as 0.7 credit
               — unanswered questions count as 0
iteration_penalty = log2(review_rounds)   # more rounds = lower score
```

### Composite Score (0–100)

```
author_score    = (responsiveness * 60) + ((1 - iteration_penalty/10) * 40)
reviewer_score  = (feedback_quality * 70) + (speed_bonus * 30)
               # speed_bonus: first review within 24h = 1.0, within 48h = 0.5
```

All scores are stored in `collaboration_scores` with a `computed_at` timestamp
so trends can be tracked over time.

---

## Phase 7 — CLI Interface

```
gh-analytics scrape  --repo owner/repo [--since 2024-01-01] [--pr 123]
gh-analytics classify [--rerun] [--model ollama/llama3]
gh-analytics score   --repo owner/repo [--pr 123]
gh-analytics report  --repo owner/repo --type summary|churn|scores
gh-analytics export  --format csv|json [--output ./reports/]
gh-analytics db      --shell   # opens sqlite3 interactive shell
```

---

## Phase 8 — Reporting Queries (built-in)

| Report | Description |
|--------|-------------|
| `summary` | Per-PR: participants, review rounds, merge time, total comments |
| `churn` | PRs ranked by churn score; top churning comment threads |
| `scores` | Per-user collaboration scores across all PRs |
| `response_time` | Average time from comment → next commit per author |
| `review_quality` | Breakdown of comment classifications per reviewer |
| `unaddressed` | Open issues/suggestions never addressed before merge |

---

## Implementation Sequence

```
Phase 1  ─ Project skeleton, config, DB schema, migrations
Phase 2  ─ Fetchers + incremental sync
Phase 3  ─ Transformer + DB writes
Phase 4  ─ Classifier (keyword pass, no LLM yet)
Phase 5  ─ Churn computation
Phase 6  ─ Collaboration scoring
Phase 7  ─ CLI polish + reporting
Phase 8  ─ Optional: LLM classifier integration
Phase 9  ─ Optional: attachment downloader
```

---

## Technical Decisions & Trade-offs

| Decision | Choice | Rationale |
|----------|--------|-----------|
| API style | REST first, GraphQL later | REST is simpler to debug; GraphQL can reduce calls once schema is stable |
| ORM vs raw SQL | Raw SQL with `sqlite3` | Avoids dependency weight; SQLite is simple enough |
| Async vs sync | Sync with `httpx` | Avoids complexity; rate limits already force sequential calls |
| Comment classification | Keyword first, optional LLM | Works offline; LLM pass is additive |
| Patch storage | Optional (env flag) | Patches can be large; most metrics only need line counts |

---

## Out of Scope (for now)

- Real-time webhook ingestion
- Web UI / dashboard
- Cross-repo aggregation
- PR template compliance checking
- Test coverage correlation
