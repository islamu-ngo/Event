<!-- ABOUTME: Canonical context-budget and retrieval policy for repository agents and subagents. -->
<!-- ABOUTME: Prevents duplicate reads, preserves the main agent's working set, and routes broad discovery to economical scouts. -->

# Context Engineering Contract

## 0. Repository Lifecycle & Greenfield Development Mandate

> [!IMPORTANT]
> **Active Pre-Release Greenfield Mode**: This platform is in rapid pre-alpha development with **zero users, zero adopters, and zero production release commitments**.
> 
> 1. **Backward Compatibility is Explicitly Rejected**: Agents must NEVER preserve bad routes, legacy DTO shapes, deprecated schema columns, or obsolete ratchets for backward compatibility. Breaking changes are first-class, expected, and encouraged whenever they improve architecture or clean up debt.
> 2. **Context Engineering Refactor Guarantee**: When the repository eventually transitions to a public release or adoption phase where backward compatibility becomes required, **the maintainers will comprehensively refactor this context engineering framework, testing governance, and CI rules**. Agents must NOT worry about future compatibility constraints during this development phase.
> 3. **Strict Quality Over Quantity in Testing**:
>    - **DO Test**: Rich domain state transitions, pure business invariants, concurrency race conditions, multi-tenant isolation boundaries, and fail-closed security perimeters.
>    - **DO NOT Test**: Mock-mirroring boilerplate (`Received(1)` on internal repositories/caches), framework mechanics (e.g. EF Core cancellation tokens), raw C# or CSS source-text scraping, or ephemeral mutation test project sprawl.
>    - **Stryker Mutation Gating**: Stryker threshold gating (>85%) is disabled during active greenfield development to preserve agent speed and prevent low-value micro-test churn.

## Objective

Give the main agent the smallest decision-complete working set. Repository context is retrieved once, summarized once, and reused until the underlying file or decision changes.

## Context Ledger

The main agent keeps a short in-session ledger of loaded evidence using `path + heading/symbol + revision`. Before retrieving anything, check the ledger and current conversation.

- Do not reread unchanged content already present in the session.
- A summary does not require reopening its sources unless the source changed, the summary is insufficient for a concrete decision, or contradictory evidence appears.
- After an edit, reread only the changed symbol/section and invalidated callers or tests.
- A handoff names evidence locations; it does not paste source, logs, plans, or previous conversation text.

## Retrieval Order

1. Resolve only the matching intent entry; never load the full intent registry as task context.
2. Use the knowledge graph or structural outline to locate owners, symbols, callers, and tests.
3. Retrieve only the required symbol, Markdown heading, or bounded line range.
4. Load one sibling pattern and the directly relevant test when implementation needs precedent.
5. Expand only when the first retrieval leaves a named decision unresolved.

Raw full-file reads are a fallback for small files with no structural retrieval path. Never use a broad read when an outline, symbol, heading, diff, or graph query can answer the question.

## Automated Blast Radius & Pre-Flight Graph Injection

To achieve zero-turn dependency discovery and eliminate multi-turn manual exploration, agents utilize pre-flight blast radius analysis on Turn 1. Using the repository's `code-review-graph` MCP tools (`get_impact_radius_tool`, `get_affected_flows_tool`), the agent injects a compact, bounded ~1 KB structural context slice into the intake notes or implementation plan:

```yaml
# Injected Structural Context (Pre-Flight Blast Radius)
Target: <Namespace.ClassName.MethodName>
Callers (Upstream):
  - <Controller.Action> (Route: <route_template>)
  - <Blazor.Component.Handler>
Callees (Downstream):
  - <Repository.Method>
  - <Outbox.EnqueueAsync> (Event: <DomainEventName>)
Impacted Flows:
  - Flow: <BusinessFlowName> (Criticality: <Tier>)
Test Coverage:
  - <PathToUnitTests>
  - <PathToIntegrationTests>
```

### Protocol Invariants:
1. **Zero-Turn Context**: Map the full vertical dependency chain (API Route $\rightarrow$ MediatR Handler $\rightarrow$ Outbox Event $\rightarrow$ DB Repository $\rightarrow$ Tests) on Turn 1 before authoring changes.
2. **Side-Effect Prevention**: Ensure downstream side effects (Outbox events, cache invalidation, UI links) are captured upfront.
3. **Context Budget Economy**: Replace multi-step manual file traversal with a single structured graph query.

## Default Budgets

The machine-readable limits live once in [`benchmarks/cold-start-tasks.yaml`](benchmarks/cold-start-tasks.yaml) under `context_budget`. They cover additional repository bootstrap after already-injected `AGENTS.md`, one retrieval result, the initial discovery result, scout output, duplicate unchanged bytes, and full-registry reads.

When a tool would exceed a budget, return a summary plus artifact/path handles and retrieve the next section only if needed. Build and test tools retain full logs outside the conversation and return exit code, counts, first actionable failures, and the artifact path.

## Model Economy And Delegation

Use model capability for decisions, not for bulk reading.

| Work | Default model tier |
|---|---|
| File discovery, symbol inventory, documentation routing, codebase search, mechanical evidence collection | `economical` |
| Focused implementation and deterministic verification | `balanced` |
| Architecture synthesis, security/privacy judgement, adversarial review, unresolved multi-system debugging | `advanced` |

The main agent retains goals, constraints, decisions, and synthesis. Economical read-only scouts receive one narrow question, exact scope, and the machine-readable result cap. They return findings and locations only, never raw files, command logs, or copied documentation. Escalate a scout to a stronger model only after a concrete ambiguity or failed evidence pass, not because the repository is large.

Do not delegate an atomic lookup: each subagent pays its own bootstrap cost. Delegate only when the scout keeps a larger body of discovery out of the main context or when independent lanes can run in parallel.

## Dynamic Exploration Budget & Criticality Matrix

Context budgets dynamically adapt based on the task's criticality tier resolved from [`.agents/contract/intents.yaml`](contract/intents.yaml):

| Criticality Tier | Exploration Protocol | Intake & Inquiry Mode | Test Strategy | Multi-Agent Review |
|---|---|---|---|---|
| **Tier 0: Sovereign** | Exhaustive Knowledge Graph (callers, callees, outbox, DB locks, ADRs) | Mandatory `/grill-me` (money flows, hold expiration, refund authority) | Invariant-Breaker concurrency tests + Postgres container races | Anonymized Epistemic MAD (Weighted Voting) |
| **Tier 1: Security** | Exhaustive Graph + Policy (Cerbos, BFF, global filters, tokens) | Mandatory `/grill-me` (threat modeling, fail-closed auth, tenant spoofing) | Invariant-Breakers + multi-provider DB tests + tenant isolation | Anonymized Epistemic MAD (Weighted Voting) |
| **Tier 2: Privacy** | Exhaustive Data Flow (all `*Pii` fields, `IAiContextGateway`, log sinks) | Mandatory `/grill-me` (erasure authority, anti-resurrection, receipt tokens) | Invariant-Breakers + log sink PII scans + purge tests | Anonymized Epistemic MAD (Weighted Voting) |
| **Tier 3: Domain State** | Bounded caller/callee tracing of target aggregate/handler | Standard Q&A (only if requirements are ambiguous) | Pure domain invariant & CQRS contract tests | Peer Review (`backend-engineer-agent`) |
| **Tier 4: Standard** | Local surface reading (target razor/css/doc file only) | Autonomous defaults (zero unnecessary interruptions) | HAL affordance & component render tests (`_links`) | Lightweight Self-Check (`presentation-engineer-agent`) |

## In-Session Test Economy & Clean Architecture Scoping

To eliminate the 50% test diagnosis and 15% container troubleshooting bottleneck during interactive coding sessions, agents MUST adhere to Clean Architecture layer scoping and the **3-Ring Progressive Verification Model**:

### 1. The 3-Ring Progressive Verification Model

| Ring | Scope & Cadence | Target Execution Time | Permitted Suites & Infrastructure | Purpose |
|---|---|---|---|---|
| **Ring 1: Inner Loop** | Subtask level (every code edit) | **< 2 seconds** | Sliced in-memory TUnit (`--treenode-filter`) in `Event.Domain.UnitTests` or `Event.Application.UnitTests`. **0 Docker containers, 0 network I/O, 0 database lag**. | Instant Red/Green validation of business logic, state machines, and invariants. |
| **Ring 2: Phase Exit Gate** | Phase boundary (before phase commit) | **< 15 seconds** | Single Release build (`dotnet build -c Release -v q`) + at most **one** selected test project against **one canonical provider** (e.g. SQLite in-memory or single PostgreSQL container). | Verify project compilability and single-layer integration without matrix delays. |
| **Ring 3: Plan Exit Gate** | Workstream boundary (before PR creation) | Minutes | Full 5-database matrix (PostgreSQL, SQLite, SQL Server, MySQL), EF Core migrations, and `Event.Architecture.Tests`. | Catch multi-dialect edge cases and architecture drift once before PR submission. |

### 2. The Yak-Shaving Quarantine Rule

Agents are **strictly forbidden** from absorbing, investigating, or repairing pre-existing test suite rot or broken fixtures outside the phase-owned scope:
1. **Detect**: If an existing test fails outside the task's path during a run.
2. **Isolate & Verify**: Prove whether the failure reproduces on a clean worktree of the base branch (`git checkout develop`).
3. **Log & Quarantine**: If pre-existing/unrelated, log the exact failing command, error snippet, and path under `*-context.md` (`Validation Baseline / Pre-Existing Technical Debt`) or `dev/backlog/<slug>.md`.
4. **Halt Yak-Shaving**: Under no circumstances modify unrelated tests or production code to "fix" the suite.
5. **Proceed**: Continue executing the assigned scope once the phase-owned slice is green.

### 3. Layer-Bounded Test Scoping
Run ONLY the test project that directly protects the modified layer:

| Modified Source Path | Permitted Test Suites | Strictly Forbidden Suites (Sprawl) |
|---|---|---|
| `docs/**`, `.agents/**`, `dev/**`, `*.md` | **None** (Markdown/lint checks only) | All `dotnet build` & `.csproj` test runs |
| `src/Explore.Blazor.Client/**` | `Explore.Blazor.Client.Tests`<br/>`Event.Architecture.Tests` | `Event.Application.UnitTests`, `Event.Domain.UnitTests`, `Event.Persistence.IntegrationTests`, `Event.API.IntegrationTests` |
| `src/Explore.Blazor/**` (BFF) | `Explore.Blazor.IntegrationTests`<br/>`Event.Architecture.Tests` | Persistence, Domain, Application, Quartz integration tests |
| `src/Explore.Application/**` | `Event.Application.UnitTests`<br/>`Event.Architecture.Tests` | Blazor Client tests, Persistence/DB container tests (unless repository port changed) |
| `src/Explore.Domain/**` | `Event.Domain.UnitTests`<br/>`Event.Application.UnitTests` | Blazor UI tests, BFF tests, Quartz integration tests |
| `src/Explore.Persistence/**` | `Event.Persistence.IntegrationTests`<br/>`Event.Architecture.Tests` | Blazor UI tests, BFF tests, Secrets unit tests |
| `src/Explore.API/**` | `Event.API.IntegrationTests`<br/>`Event.Architecture.Tests` | Blazor Client UI tests, Secrets unit tests |
| `src/Explore.Secrets/**` | `Explore.Secrets.UnitTests` | All other test suites |

### 4. Fast-Loop TUnit Slicing (`--treenode-filter`)
During active development of a task or handler, agents must NEVER run the entire test project. Use TUnit's `--treenode-filter` to run ONLY the target test class:
```bash
# ✅ Fast Loop (~1.5s, clean output):
dotnet run --project tests/Event.Application.UnitTests/Event.Application.UnitTests.csproj --no-build -- --treenode-filter "/*/*/*<TargetTestClassName>/*"
```
Full project-level test runs (`dotnet test --project <path>.csproj`) are reserved strictly for the **Phase Exit Gate** (Ring 2) or PR completion (Ring 3).

### 5. Domain Invariants vs Persistence Queries
Shift 90%+ of algorithmic, normalization, validation, and state-machine checks from database integration tests into fast in-memory domain tests:
- **Domain Unit Tests (`< 50ms`)**: Unicode FormC normalization, string trimming, case-folding, regex matching, entity state machines, status transition rules.
- **Persistence Integration Tests**: Reserved strictly for EF Core mapping annotations, foreign key cascade behaviors, and engine-specific SQL dialect peculiarities.

## Research Boundary

Repository research stays local first. Official documentation or external research is loaded only for a named unresolved framework, protocol, dependency, standard, or advisory question. Search results are summarized into repository-relevant facts and source handles; raw external content never enters implementation context.

## Tooling & Execution Environment Boundary

Agents and subagents must operate strictly within the repository's native environment and toolset:

- **Prohibited Runtimes**: Never run or generate ad-hoc Python (`python`, `python3`, `pip`, `python -c`) or JavaScript/Node.js (`node`, `npm`, `npx`, `node -e`) scripts for file inspection, text extraction, data transformation, or scratch automation.
- **Allowed Edit Tools**: Use native agent file manipulation tools (`apply_patch`, `replace_file_content`, `write_to_file`) or built-in IDE diff mechanisms.
- **Allowed Shell Utilities**: Use standard POSIX Bash commands (`grep`, `sed`, `awk`, `find`, `jq`, `git`, `dotnet`).
- **Scripting is a Last Resort (High-ROI Only)**: Do not create temporary or scratch scripts for one-off tasks. If a persistent developer automation tool provides indispensable, lasting architectural value, it MUST reside under `eng/` (e.g. `eng/scripts/` or `eng/tools/`) as a file-based C# script (`*.cs` runnable via `dotnet run`) or Bash script.
- **CI/CD Isolation**: Scripts under `.ci/scripts/` are strictly dedicated to CI/CD pipeline automation (e.g. release bundle verification, license policy enforcement) and must never be repurposed for agent/dev scratch tasks.
- **Secrets Source of Truth**: Secrets, connection strings, API tokens, passwords, and encryption keys must strictly reside in **Infisical** or **`.env`** (with schema/keys documented in **`.env.example`**). NEVER hard-code, define, or embed secrets in `AppHost.cs`, test fixtures, classes, or configuration files.
- **Verification Scoping Discipline**: Documentation, agent contract, and markdown-only changes (Tier 4) must NEVER run full `dotnet build` or .NET test suites. Verification for documentation tasks is strictly scoped to file format, schema validation, and link integrity.

## Human-Agent Interaction & Zero-Friction Prompts

Active task artifacts (`plan.md`, `tasks.md`, `context.md` in `dev/active/<task>/`) exist exclusively to maintain deterministic, structured session memory and execution boundaries across agent context resets and worktree transitions. They are **machine and session working memory**, not the developer's user interface.

- **Zero Plan-Opening Mandate**: The developer must **never** be forced to open `dev/active/<task>/` to understand what an agent is asking, reporting, or proposing.
- **Prohibited Shorthand**: Prompts, approvals, and questions must never use bare IDs (`P01`, `P04/P06`, `T02.1`, `Gate 3`) without immediate inline functional names and plain-English explanations.
- **Decision Brief Architecture**: Any turn pausing for developer decision or approval must present an inline Decision Brief detailing:
  1. *Context*: Recent progress and current architectural position.
  2. *Substance*: Human-readable component and domain names.
  3. *Decision & Trade-offs*: Why the choice matters and what each path entails.
  4. *Actionable Choices*: Labeled options with a recommended default.
  5. *Next Action*: What will be executed immediately upon reply.

## Workstream And Handoff State

For a substantial active workstream, resume from its task-owned `*-context.md`. Read the current state and current task first, then zoom only the referenced plan heading. Do not automatically load plan, context, and task files together.

A handoff is written directly into the task-owned context and contains only:

- current outcome and next action;
- decisions and unresolved risks;
- modified paths;
- verification commands and results;
- evidence locations;
- unrelated dirty-worktree notes.

Generic dev-directory README and handoff templates are intentionally absent because directory-context systems may inject them into every nested read.

## Multi-Harness Rule Architecture

This repository supports multiple AI agent harnesses. Each harness has its own native rule-discovery mechanism that scans specific filesystem paths on every prompt or tool execution. To ensure automatic path-scoped rule injection across all harnesses, architectural rules are maintained as identical twin copies:

| Location | Consumed By | Discovery Mechanism |
|---|---|---|
| `.agents/rules/*.md` | ISLAMU contract system (all agents via `AGENTS.md`), Gemini/Antigravity | Intent registry `must_read_docs` + manual load from matching `paths` |
| `.omo/rules/*.md` | OmO (OpenCode plugin, Senpi, Codex LazyCodex) | `rules-injector` hook: picomatch glob/path matching with YAML frontmatter, distance weighting, and char-budget truncation (12K/rule, 40K total) |
| `.claude/rules/` | Claude Code | Native Claude rules injection |
| `.cursor/rules/` | Cursor / Windsurf | Native Cursor rules injection |
| `.github/instructions/` | GitHub Copilot | Copilot instructions injection |

### Twin Synchronization Contract

Rules in `.agents/rules/*.md` and `.omo/rules/*.md` are identical copies. Each file's second `ABOUTME:` line documents its twin path. When modifying any rule:

1. Edit the file you are working in.
2. Apply the identical change to its twin at the path stated in the `ABOUTME:` header.
3. Never use symlinks — OmO's `rules-engine` resolves real paths and deduplicates by `realpathSync`, which would collapse symlinked twins into a single candidate.

### OmO Rule Frontmatter Compatibility

OmO's `rules-engine` parses YAML frontmatter fields for automatic path matching. The ISLAMU contract rules already use compatible frontmatter. OmO's matcher accepts:

- `globs:` — Picomatch glob patterns (e.g. `"src/Explore.API/Controllers/**/*.cs"`)
- `paths:` — Same as globs, treated identically
- `alwaysApply: true` — Inject this rule on every prompt regardless of file context
- `description:` — Human-readable summary shown in OmO's rule list
- Negative globs (`!pattern`) — Exclude matching paths

### Authority Order With OmO

When an agent runs through OmO, rule authority follows:

1. **CRITICAL RULES** (`AGENTS.md` § 5)
2. **`docs/QUICK_REFERENCE.md`** — Global project invariants
3. **OmO-injected `.omo/rules/*.md`** — Auto-matched by edited file path (highest priority in OmO's `SOURCE_PRIORITY` map)
4. **`docs/GOVERNANCE.md`** — Coding conventions and patterns
5. **Matching `.agents/rules/*.md`** — Manually loaded via intent `must_read_docs`

OmO-injected rules and intent-loaded rules are identical twins, so no conflict is possible.

## QA Evidence Gate

For Tier 0 (Sovereign), Tier 1 (Security), and Tier 2 (Privacy) changes, agents MUST record verification evidence as plain files under `.omo/evidence/<YYYYMMDD>-<task-slug>/` or `evidence/<YYYYMMDD>-<task-slug>/`. This evidence is reviewer-readable proof that the verification actually happened.

### Required Evidence Artifacts (Tiers 0–2)

| Artifact | Contents | When Required |
|---|---|---|
| `test-results.txt` | Fast-loop TUnit slice output (`--treenode-filter`) or full project test output | Every code change |
| `invariant-breaker-results.txt` | Concurrency race, tenant spoofing, double-capture, or replay adversarial test logs | Tiers 0–1 |
| `blast-radius.yaml` | Pre-flight knowledge graph dump (callers, callees, impacted flows, tests) | Multi-layer changes |
| `summary.md` | What was tested, what was observed, why it is sufficient, what was omitted | Always |

### Evidence Rules

- **No evidence file == verification did not happen.** Do not claim "tests passed" without captured output.
- Redact or summarize secret-bearing logs, tokens, connection strings, and credentials — never copy raw secrets into evidence files.
- Evidence for Tier 3 (Domain State) and Tier 4 (Standard) changes is optional but encouraged for complex behavioral changes.
- OmO's `work-with-pr` skill and QA skills (`opencode-qa`, `codex-qa`, `senpi-qa`) natively write evidence to `.omo/evidence/` — this aligns with the repository convention.

## Hashline Edit Support

When agents operate through OmO with `hashline_edit` enabled, every file-read operation tags output lines with content hashes (`LINE#HASH|` via xxhash32). Subsequent edits validate these hashes against the current file state — if code shifted under the agent (due to concurrent edits, rebases, or context compaction), the edit is rejected immediately rather than silently overwriting the wrong lines.

### When Hashline Helps

- Large C# files (>200 lines) where line-number drift causes silent overwrites.
- Concurrent editing sessions where multiple agents or the developer modify the same file.
- Post-compaction resumption where the agent's cached file state may be stale.

### Compatibility

Hashline is an OmO-specific feature. Agents running through Claude Code, Cursor, Copilot, or Gemini/Antigravity use their native edit tools and are unaffected. The twin rules, intent registry, and verification policies apply identically regardless of whether hashline is active.

## Dual-Documentation Parity Protocol (Internal vs Public Website)

The repository maintains two strictly separated documentation tracks:
1. `docs/internal/`: Canonical engineering brain for Developers & AI Agents.
2. `docs/public/`: Curated public guides for Adopters, Operators & Integrators (synced with GitBook).

### The Parity Rule
Whenever a change modifies:
- An environment variable or configuration option (`ConfigurationExtensions.cs`, `.env.example`)
- A deployment manifest or service topology (`docker-compose.yml`, `Coolify`, `Dockerfile`)
- An API endpoint or public request/response contract (`Controllers`, `OpenAPI`)
- An administrative workflow or self-hosting runbook

The agent **MUST update both documentation tracks in the same PR/turn**:
1. Update the **Technical Anchor** in `docs/internal/` with complete invariants, types, and defaults.
2. Update the **Public Projection** in `docs/public/` with adopter-focused instructions (avoiding internal framework/C# class details).

Consult the **Documentation Twin Parity Matrix** in `docs/internal/DOCUMENTATION_ARCHITECTURE.md` to resolve the corresponding twin document. Failure to update both documentation experiences constitutes a broken acceptance contract.

## Measurement

Cold-start benchmarks record first-turn input tokens, maximum live context, cumulative input/cache-read tokens, tool-result bytes, duplicate bytes by content hash, full-file reads, and scout result size. Correctness is required, but a scenario that exceeds its context budget is not a pass.
