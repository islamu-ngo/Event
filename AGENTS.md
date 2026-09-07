<!-- ABOUTME: Canonical AI-agent contract for the ISLAMU Event platform. -->
<!-- ABOUTME: Defines the Contribution Contract, critical rules, and routing logic. -->

# AGENTS.md — Canonical Agent Contract

> **This is the canonical entrypoint for every AI tool contributing to this repository.**
> Last Updated: 2026-08-12

---

## 1. The Contribution Contract (Read First)

Every change must answer these eight questions **before editing any file**:

| # | Question | Source of Truth |
|---|---|---|
| 1 | What kind of change is this? (the *intent*) | [`.agents/contract/intents.yaml`](.agents/contract/intents.yaml) |
| 2 | Which rules are authoritative? | [`docs/internal/QUICK_REFERENCE.md`](docs/internal/QUICK_REFERENCE.md) + [`.agents/rules/*.md`](.agents/rules/) |
| 3 | Which files must be read first? | The intent's `must_read_docs` field |
| 4 | Which files may be changed? | The intent's `paths_in_scope` field |
| 5 | Which tests must run at minimum? | The intent's `minimum_tests` field |
| 6 | Which docs must be updated? | The intent's `docs_to_update` field |
| 7 | Which PR checklist applies? | The intent's `pr_checklist` field |
| 8 | What is forbidden? | The intent's `forbidden_without_approval` + `docs/internal/QUICK_REFERENCE.md` |

---

## 2. Canonical Artifacts (Single Source of Truth)

| Concern | Canonical File | Purpose |
|---|---|---|
| AI agent contract | `AGENTS.md` (this) | Every agent starts here |
| Invariant reference | `docs/internal/QUICK_REFERENCE.md` | Global hard constraints |
| Governance | `docs/internal/GOVERNANCE.md` | Conventions, design patterns |
| Intent registry | `.agents/contract/intents.yaml` | Machine-readable task mapping |
| Operations | `docs/internal/OPERATIONS.md` | Build/Test details, AI operational rules |
| Durable findings | `dev/_journal/journal.md` | Decisions, non-obvious patterns |

---

## 3. Cold-Start Flow (Zero-Knowledge Agent)

> **Development Mode Notice**: This repository is in active **pre-release greenfield development** (0 users, 0 external adopters, no production releases). **Backward compatibility is explicitly rejected** in favor of the cleanest, purest architecture. Breaking changes are first-class, encouraged, and preferred over legacy shims, compatibility adapters, or obsolete ratchets. When the platform transitions to a public release/adoption phase, context engineering and governance will be comprehensively refactored.

1. **CLASSIFY**: Find matching intent in [`.agents/contract/intents.yaml`](.agents/contract/intents.yaml) and resolve its `criticality` block (`tier`, `intake_clarification_mode`, `exploration_protocol`, `testing_strategy`, `review_protocol`).
2. **DYNAMIC ALIGNMENT & INTAKE**:
   - **Tiers 0, 1, 2 (Sovereign / Security / Privacy)**: Mandate proactive `/grill-me` alignment on edge cases, threat models, and failure recovery before editing. Conduct exhaustive knowledge-graph blast-radius exploration. Author failing *Invariant-Breaker* tests first (concurrency races, state machines, tenant isolation).
   - **Tier 3 (Domain State)**: Perform bounded caller/callee tracing and standard clarification if requirements are ambiguous.
   - **Tier 4 (Standard UI / Docs)**: Proceed autonomously with economical context budgets, applying established conventions with minimal friction.
3. **LOAD**: Read `must_read_docs` and matching [`.agents/rules/*.md`](.agents/rules/).
4. **EDIT**: Work within `paths_in_scope`. Follow Clean Architecture: Domain → App → Infra → API. Embrace breaking changes to eliminate legacy debt.
5. **VERIFY & REVIEW**: Run minimum tests. For Tiers 0–2, verify real concurrency/multi-provider engine behavior, tenant boundaries, and conduct Epistemic Multi-Agent Debate (MAD) review. (Stryker mutation gating is disabled during greenfield dev).
6. **TEACH**: Provide a comprehensive technical teaching summary explaining architectural patterns, state transitions, and rollback mechanisms.
7. **ESCALATE**: If any rule conflicts with the request, stop and ask the user.

---

## 4. Rule Authority Order (Conflict Resolution)

1. **CRITICAL RULES** (Section 5 below)
2. **`docs/internal/QUICK_REFERENCE.md`** — Global project invariants
3. **`docs/internal/GOVERNANCE.md`** — Coding conventions and patterns
4. **Matching `.agents/rules/*.md`** — Path-scoped deltas

---

## 5. CRITICAL RULES (Project-Specific Invariants)

> **Rule #1:** Never assume an exception. Get explicit permission before breaking any rule.

1. Repositories return **entities**, never DTOs (map in handlers).
2. Validators are **manually instantiated** (no DI).
3. Use `int` for lookups, `Guid` (UUIDv7) for aggregates, `long` for cursors.
4. GET = `[AllowAnonymous]`, write = `[Authorize]`.
5. Every file must start with a two-line `ABOUTME:` comment summary.
6. **HAL links are the single source of truth for UI**: Clients must gate action affordances (Edit/Delete) by checking `_links` presence, never local role/claim inspection.
7. **EF Core migrations are generated artifacts**: Never hand-edit migration or model-snapshot files. Fix entities/configurations or the migration generator, then delete and regenerate an unapplied development migration with `dotnet ef migrations`.
8. **IP, clean-room, and outbound-license protection**: Never ingest third-party copyleft, source-available, proprietary, or otherwise incompatible source code, snippets, ASTs, SQL, migrations, tests, comments, or assets into implementation context or copy them into this repository. Externally informed work must pass through a source-free functional specification and use independently designed project-native structure, sequence, and organization. A dependency is forbidden unless its terms preserve every intended ISLAMU outbound licensing path or the Project Steward has documented separate licensing and distribution approval. See [`docs/internal/legal/IP_GOVERNANCE.md`](docs/internal/legal/IP_GOVERNANCE.md).
9. **Agent tooling and execution boundary (No Python/JS scripts)**: Agents must NEVER run or generate ad-hoc Python (`python`, `python3`, `python -c`) or JavaScript/Node (`node`, `npm`, `node -e`) helper scripts. File edits must use native agent editing tools (`apply_patch`, `replace_file_content`, `write_to_file`). Shell tasks must use standard POSIX Bash commands. Creating scripts is an absolute last resort (only when there is overwhelming, lasting ROI); any persistent repo tool belongs in `eng/` (e.g. `eng/scripts/` or `eng/tools/`) as a C# file-based script (`dotnet run eng/.../*.cs`) or Bash script. Never put dev tools in `.ci/scripts/` (strictly for CI/CD pipelines).
10. **Secrets Isolation & Source of Truth**: Secrets, passwords, API tokens, connection strings, and encryption keys must NEVER be hard-coded, embedded, or defined in `Explore.AppHost` (`AppHost.cs`), test files/fixtures, controllers, appsettings, or anywhere in source code. Secrets originate strictly from **Infisical**, explicit environment injection documented by **`.env.example`**, or the explicitly selected shared **.NET User Secrets** authority in **Development/Testing only**. User Secrets are rejected in every other environment and never act as fallback. Tests and local hosting must bind dynamically via an approved authority or secret provider mocks—never inline plaintext credentials.
11. **Greenfield Breaking Change Freedom (No Backward Compatibility Baggage)**: This repository is pre-release with 0 external adopters. Never preserve obsolete endpoints, bad DTO shapes, legacy columns, or adapter shims for backward compatibility. Breaking changes are encouraged whenever they simplify code or align with Clean Architecture.
12. **Strict Test Quality Over Quantity (No Mock-Mirroring or Scraping)**: Tests MUST guard true business invariants, rich domain state machines, concurrency races, tenant isolation, and security fail-closed semantics. Prohibit tautological mock-mirroring (`Received(1)`), framework-testing boilerplate (testing EF Core cancellation), raw source-code / CSS text scraping, and ephemeral mutation test project sprawl.
13. **Dual-Documentation Parity & Separation (Public vs Internal)**: Any change impacting external configuration (`.env.example`), deployment topologies (`docker-compose.yml`), public API endpoints, or administrative features MUST update both the public adopter guide in `docs/public/` (operator/adopter perspective on GitBook) and the technical source anchor in `docs/internal/` (C# architecture and invariants) within the same PR, following Single Responsibility without duplicating raw configs. See `docs/internal/DOCUMENTATION_ARCHITECTURE.md`.
14. **Self-Contained Interaction & Zero Plan-Opening Overhead**: Prompts, questions, feedback requests, and status reports to the developer MUST be completely self-contained and immediately actionable without requiring the developer to open `dev/active/<task>/...` or grep internal plan files. Agents must NEVER reference bare phase/task IDs (`P04/P06`, `T02.1`, `P03 gates`) in isolation. Every approval request, decision prompt, or milestone update MUST provide a self-contained Decision Brief inline: descriptive human names of features/components, current context, the exact choice with rationale, and clear recommended options with trade-offs. The implementation plan is internal working memory; the chat response is the developer console.
15. **The 3-Ring Progressive Verification Model & Yak-Shaving Quarantine**:
    - **Ring 1 (Inner Loop / Sliced)**: Subtask changes MUST be verified via fast in-memory TUnit sliced tests (`--treenode-filter "/*/*/*<TestClass>/*"`) targeting Domain or Application unit tests in **< 2 seconds**. Zero Docker containers or network I/O in the inner loop. 90%+ of algorithmic/normalization/business invariants belong in `Event.Domain.UnitTests`.
    - **Ring 2 (Phase Exit Gate)**: Verify the single touched project against ONE canonical provider (e.g., SQLite in-memory or single PostgreSQL container) in **< 15 seconds**.
    - **Ring 3 (Plan Exit / Workstream Gate)**: The full multi-database provider matrix (PostgreSQL, SQLite, SQL Server, MySQL), migration checks, and full suites are run ONCE at the end of the entire implementation plan before PR creation.
    - **Yak-Shaving Quarantine**: Agents are strictly FORBIDDEN from absorbing or repairing pre-existing unrelated test suite rot encountered during feature work. If an existing test fails outside the task's path, verify if it reproduces on an untouched base worktree, log it under `*-context.md` (or `dev/backlog/`), and quarantine it. Never derail feature implementation to fix unrelated persistence suite failures.

**Full list:** [`docs/internal/QUICK_REFERENCE.md`](docs/internal/QUICK_REFERENCE.md)

---

## 6. Task-Routing Entrypoints

| Starting Point | Go To |
|---|---|
| New request | [`.agents/contract/intents.yaml`](.agents/contract/intents.yaml) — find `triggers` |
| Known path | [`.agents/rules/`](.agents/rules/) — find matching `paths` |
| Pattern/Skill | [`.agents/skills/`](.agents/skills/) — load relevant `SKILL.md` |
| Build/Test | [`docs/internal/OPERATIONS.md#verification-policy`](docs/internal/OPERATIONS.md#verification-policy) |
| UI Workflow | [`docs/internal/BLAZOR_DEV_WORKFLOW.md`](docs/internal/BLAZOR_DEV_WORKFLOW.md) |
| Agent Ops | [`.agents/CONTEXT_ENGINEERING.md`](.agents/CONTEXT_ENGINEERING.md) |
| PR Review | [`.agents/skills/review-pr/SKILL.md`](.agents/skills/review-pr/SKILL.md) |
| Log Finding | [`.agents/skills/finding/SKILL.md`](.agents/skills/finding/SKILL.md) |

---

## 7. Targeted Fetch And Reuse Rule

When a task touches a topic covered by docs, skills, or rules, retrieve the **smallest relevant heading, symbol, or bounded range once**. Follow [`.agents/CONTEXT_ENGINEERING.md`](.agents/CONTEXT_ENGINEERING.md).

**Required context:**
- Use this file from injected session context; do not reread it when it is already present.
- Resolve only the matching entry from [`.agents/contract/intents.yaml`](.agents/contract/intents.yaml); never load the full registry into task context.
- Load only the required headings from relevant `docs/internal/*.md`, the matching skill routers, and matching path rules.
- Prefer graph, outline, symbol, heading, and diff retrieval over full-file reads.
- **Zero-Turn Blast Radius**: For multi-layer or high-criticality tasks, perform pre-flight impact analysis on Turn 1 via `code-review-graph` MCP tools (`get_impact_radius_tool`, `get_affected_flows_tool`) to inject a bounded structural context slice (Callers, Callees, Impacted Flows, Tests) into intake/planning before exploring files.
- Reuse an in-session `path + heading/symbol + revision` ledger. Reread only after the source changes, a concrete decision lacks evidence, or contradictory evidence appears.
- Before external functional research, third-party design analysis, or dependency selection, load the relevant sections of [`.agents/skills/ip-clean-room/SKILL.md`](.agents/skills/ip-clean-room/SKILL.md) and [`docs/internal/legal/IP_GOVERNANCE.md`](docs/internal/legal/IP_GOVERNANCE.md); do not load the full legal chain for ordinary official framework documentation.

---

## 8. Verification Baseline

Before the first product edit, ensure local tracking is fresh against upstream (`git checkout develop && git pull --ff-only`), then establish the green baseline once for code changes. Do not rerun an unchanged baseline; every PR touching product code must still leave the build and minimum tests green.

**The 3-Ring Progressive Verification Hierarchy:**
1. **Ring 1 (Inner Loop — Subtask Level, < 2s)**:
   - Run ONLY the target test class using TUnit slicing: `--treenode-filter "/*/*/*<TestClass>/*"`.
   - Strictly in-memory (`Event.Domain.UnitTests` or `Event.Application.UnitTests`).
   - Zero Docker containers, zero network I/O, zero database setup lag.
2. **Ring 2 (Phase Exit Gate — Phase Level, < 15s)**:
   - Run a single Release build and at most ONE selected project test against a single canonical provider.
   - Forbid running the multi-database provider matrix during intermediate phase exits.
3. **Ring 3 (Plan Exit Gate — Workstream Level)**:
   - Full multi-database matrix, migration round-trips, and architecture guardrails run ONCE at the end of the workstream before PR creation.

**Scope & Layer Discipline:**
- **Tier 4 (Docs / Agent Context):** For documentation, agent context, markdown-only, or comment changes, DO NOT run `dotnet build` or .NET test suites. Verification is strictly scoped to markdown formatting, link integrity, and schema checks.
- **Layer-Bounded Execution:** Never run test suites belonging to unrelated architectural layers (e.g., no database integration tests for UI changes; no Blazor tests for CQRS handlers).
- **Yak-Shaving Quarantine Rule:** Never derail feature implementation to fix pre-existing test rot or broken fixtures outside the task path. Verify reproduction on clean base, log under `*-context.md`, quarantine the failure, and proceed with the assigned deliverable.

**Build Command (Code Changes Only):**
```bash
dotnet build --configuration Release --verbosity quiet
```

**Full Test List:** [`docs/internal/OPERATIONS.md#full-project-test-list`](docs/internal/OPERATIONS.md#full-project-test-list)

---

## 9. Agent Operational Baseline

- **Tooling & Execution**: Prohibit ad-hoc Python/Node scripts. Default to native agent edit tools and POSIX Bash. Keep persistent engineering tools in `eng/`; keep `.ci/scripts/` dedicated to CI/CD pipelines.
- **Subagents**: Use the lowest-cost capable model. Broad read-only discovery goes to economical scouts with the bounded output contract in [`.agents/CONTEXT_ENGINEERING.md`](.agents/CONTEXT_ENGINEERING.md); the main agent keeps decisions and synthesis. Do not delegate atomic lookups. Index: [`.agents/agents/README.md`](.agents/agents/README.md).
- **Memory**: Resume substantial work from its task-owned `*-context.md`; do not automatically load plan/context/tasks together. Durable findings live in `dev/_journal/`.
- **Todos**: Create immediately for multi-step tasks.
- **Context**: Follow [`.agents/CONTEXT_ENGINEERING.md`](.agents/CONTEXT_ENGINEERING.md); duplicate unchanged context is a defect.

### Final Teaching Summary Requirement

Before an implementation agent ends a task, pauses for the user's next prompt, performs a handoff, or claims work is complete, the final response MUST teach the user what changed. Do not give only an abstract status line such as “email sending implemented” or “docs updated.” The user is a developer and must understand the implementation without opening the diff.

The final summary must be medium-sized and technically specific. Include the architecture/design pattern used, concrete libraries/frameworks/infrastructure/protocols, important files/classes/handlers/components changed, data/control flow, relevant best practices such as transactional outbox, CQRS/MediatR, Clean Architecture, HAL affordance gating, tenant isolation, idempotency, retry/error handling, and what was verified or remains. Keep it concise enough for chat, but detailed enough that the user learns the implemented approach.

### Self-Contained Human Interaction & Decision Brief Requirement

> **The Golden Invariant**: The developer should **never** need to open the implementation plan (`dev/active/<task>/...`) just to understand an agent's question, status update, or approval request.

Active dev-docs (`plan.md`, `tasks.md`, `context.md`) serve as machine/session working memory and an execution ledger. They are **not** the developer's primary user interface. Forcing the user to leave the conversation, navigate files, and cross-reference cryptic phase codes (e.g. *"Do you approve proceeding with P04/P06 while keeping both P03 gates explicitly open?"*) is an unacceptable agent UX failure.

Whenever an agent prompts for approval, requests architectural direction, reports milestone completion, or flags a blocker, the response MUST be formatted as a **Self-Contained Decision Brief**:

1. **Context & Current Position**: 1–2 sentences on what was just completed or where the workstream currently stands.
2. **Plain-English Substance**: Always use descriptive feature/component names alongside any phase numbers (e.g., *"Phase 4 (PostgreSQL Outbox Worker & Dead-Letter Dispatch)"* instead of bare *"P04"*).
3. **The Exact Decision & Why It Matters**: Explain in plain English what decision is needed, why it arose, and what the real-world trade-off is (e.g., *"Why keep Phase 3 gates open? Because Redis distributed locking requires integration tests that depend on the benchmark harness"*).
4. **Structured Actionable Options**: Provide explicit, numbered choices with a clear recommendation (e.g., `Option A (Recommended): ...`, `Option B: ...`) and the pros/cons of each, so the developer can approve or guide in seconds without leaving chat.
5. **Immediate Next Action**: Clearly state what the agent will execute the moment the user responds.

---

## 10. Tool-Specific Bootloaders

| Tool / Harness | Entry File | Dynamic Rules Injected Via |
|---|---|---|
| OmO (OpenCode / Senpi / Codex LazyCodex) | `AGENTS.md` (this) | [`.omo/rules/*.md`](.omo/rules/) (Hook: `rules-injector`, picomatch + distance) |
| Claude Code | `AGENTS.md` (this) | `.claude/rules/` + `AGENTS.md` |
| Cursor / Windsurf | `.cursorrules` | `.cursor/rules/` + `AGENTS.md` |
| GitHub Copilot | [`.github/copilot-instructions.md`](.github/copilot-instructions.md) | `.github/instructions/` |
| Gemini / Antigravity | [`AGENTS.md`](AGENTS.md) | Session rules injection |

> **Twin Rules Policy**: Path-scoped rules are maintained as identical copies in both `.agents/rules/*.md` (for the canonical ISLAMU contract system) and `.omo/rules/*.md` (for OmO's native `rules-injector` hook). When editing a rule, update both twin files — each twin's `ABOUTME:` header documents its counterpart path. This dual presence ensures that agents running through **any** harness receive automatic path-scoped rule injection without manual loading.

---

## Local Configuration Overrides

If an `AGENTS.local.md` file exists in the repository root, inspect and follow all instructions, environment overrides, and system-specific constraints defined in it. Treat rules in `AGENTS.local.md` as overriding or extending the guidelines in this document.

---

<!-- code-review-graph MCP tools -->
## MCP Tools: code-review-graph

**IMPORTANT: This project has a knowledge graph. ALWAYS use the
code-review-graph MCP tools BEFORE using Grep/Glob/Read to explore
the codebase.** The graph is faster, cheaper (fewer tokens), and gives
you structural context (callers, dependents, test coverage) that file
scanning cannot.

### When to use graph tools FIRST

- **Exploring code**: `semantic_search_nodes_tool` or `query_graph_tool` instead of Grep
- **Understanding impact**: `get_impact_radius_tool` instead of manually tracing imports
- **Code review**: `detect_changes_tool` + `get_review_context_tool` instead of reading entire files
- **Finding relationships**: `query_graph_tool` with callers_of/callees_of/imports_of/tests_for
- **Architecture questions**: `get_architecture_overview_tool` + `list_communities_tool`

Fall back to Grep/Glob/Read **only** when the graph doesn't cover what you need.

### Key Tools

| Tool | Use when |
| ------ | ---------- |
| `detect_changes_tool` | Reviewing code changes — gives risk-scored analysis |
| `get_review_context_tool` | Need source snippets for review — token-efficient |
| `get_impact_radius_tool` | Understanding blast radius of a change |
| `get_affected_flows_tool` | Finding which execution paths are impacted |
| `query_graph_tool` | Tracing callers, callees, imports, tests, dependencies |
| `semantic_search_nodes_tool` | Finding functions/classes by name or keyword |
| `get_architecture_overview_tool` | Understanding high-level codebase structure |
| `refactor_tool` | Planning renames, finding dead code |

### Workflow

1. The graph auto-updates on file changes (via hooks).
2. Use `detect_changes_tool` for code review.
3. Use `get_affected_flows_tool` to understand impact.
4. Use `query_graph_tool` pattern="tests_for" to check coverage.
