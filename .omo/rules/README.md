<!-- ABOUTME: Documentation for the .omo/rules/ twin rule directory. -->
<!-- ABOUTME: Explains the twin synchronization contract between .agents/rules/ and .omo/rules/. -->

# OmO Rules Directory

This directory contains **twin copies** of the path-scoped architectural rules from `.agents/rules/`.

## Why Twin Copies?

Oh-My-OpenAgent (OmO) automatically discovers and injects rules from `.omo/rules/` into the agent's context on every prompt and tool execution via its `rules-injector` hook. OmO's `rules-engine` does NOT scan `.agents/rules/` — it only scans `.omo/rules/`, `.claude/rules/`, `.cursor/rules/`, and `.github/instructions/`.

By maintaining identical copies here, agents running through OmO (OpenCode plugin, Senpi, Codex LazyCodex) automatically receive path-scoped architectural guidance without manual loading.

## Synchronization Contract

- Each rule file here is an **identical copy** of its twin at `.agents/rules/<name>.md`.
- Each file's second `ABOUTME:` line documents its twin path.
- **When modifying any rule**: update both the `.agents/rules/` and `.omo/rules/` copies.
- **Never use symlinks**: OmO's `rules-engine` resolves real paths via `realpathSync` and deduplicates — symlinks would collapse twins into a single candidate.

## How OmO Matches Rules

OmO's `rules-engine` (`packages/rules-engine` in the oh-my-openagent repo) parses YAML frontmatter:

| Field | Behavior |
|---|---|
| `paths:` or `globs:` | Picomatch glob patterns matched against the edited file's relative path |
| `description:` | Human-readable summary shown in rule lists |
| `alwaysApply: true` | Inject on every prompt regardless of file context |
| Negative globs (`!pattern`) | Exclude matching paths |

Rules are ranked by:
1. **Source priority**: `.omo/rules` has priority 0 (highest) in OmO's `SOURCE_PRIORITY` map.
2. **Distance**: Rules closer to the edited file's directory win over distant rules.
3. **Character budget**: 12K per rule, 40K total — rules are truncated when budgets are exceeded.

## Files

All 16 path-scoped rules plus `_schema.md` are twins of `.agents/rules/`:

| File | Matches |
|---|---|
| `api-controllers.md` | `src/Explore.API/Controllers/**/*.cs` |
| `api-hateoas.md` | `src/Explore.API/Hateoas/**/*.cs` |
| `api-scheduling.md` | `src/Explore.API/Controllers/**/Scheduling*/**/*.cs` |
| `application-layer.md` | `src/Explore.Application/**/*.cs` |
| `auth-trust-boundaries.md` | Auth/trust boundary paths |
| `blazor-client.md` | `src/Explore.Blazor.Client/**` |
| `blazor-server.md` | `src/Explore.Blazor/**` (BFF) |
| `domain.md` | `src/Explore.Domain/**/*.cs` |
| `efcore-migrations.md` | `src/Explore.Persistence/Migrations/**/*.cs` |
| `efcore-persistence.md` | `src/Explore.Persistence/**/*.cs` |
| `ip-clean-room.md` | IP/licensing paths |
| `payments-commerce.md` | Payment/commerce paths |
| `plane-tracking.md` | Plane taxonomy docs, Plane skills, GitHub issue/PR templates |
| `privacy-and-pii.md` | Privacy/PII paths |
| `tests.md` | `tests/**/*.cs` |
| `work-criticality-matrix.md` | `**/*.cs`, `**/*.razor` |
