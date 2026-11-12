# Plane Project Management Standardization (Single-Project `EVENT`)

> **Audience:** Contributors | Maintainers | AI agents
> **Status:** Accepted
> **Owner:** Contributor Experience | Agent Context
> **Last Verified:** 2026-11-12
> **Source Anchors:** `docs/internal/project-management/work-item-taxonomy-and-granularity-report.md`, `AGENTS.md` (Critical Rule 20), `.agents/skills/plane-compose/SKILL.md`, `.agents/skills/plane-mcp/SKILL.md`, `.agents/rules/plane-tracking.md`, `islamic-value-sensitive-design/workstreams/i-vsd-plane-project-management-standardization.md`

---

## 1. Problem Context

ISLAMU Event tracks macro work in Plane (project `EVENT`, workspace `islamu`) as code through the gitignored `event/` Plane Compose project. By late 2026 that backlog had stopped carrying signal:

- **2,616 work items, 0 Epics, 2 sub-items.** Strategic programs had no container, so multi-week capabilities were either flat mega-blobs (`EVENT-453` *Payment Integration (Stripe/PayPal)*) or exploded into horizontal fragments.
- **Two-thirds machine backfill.** 1,060 eight-part horizontal entity tickets (*Model X*, *API contract X*, *List screen X*, …), 176 controller/worker surface stubs, and 511 single-commit `Done` tickets — none independently shippable or meaningful as a planning unit.
- **Perpetual titles** (`EVENT-430` *Testing Coverage Improvement*, `EVENT-478` *Security Audit*) that can never reach `Done`.
- **2,614 of 2,616 items pre-assigned** to one maintainer, which made personal work-in-progress views useless.
- **Drifted agent context.** Skills named stale MCP tools, mislabeled Version Modules as Cycles, and contributor docs still taught `feature/foobar` branches with free-form commit messages.

Every Plane item costs recurring human and AI attention: triage, `workitems.yaml` diffing, validation, state transitions. Noise in the tracker therefore becomes noise in every agent session that loads it.

The full analysis, industry synthesis (INVEST, SPIDR, Shape Up, the Linear Method), and worked examples live in the [work-item taxonomy and granularity standard](../project-management/work-item-taxonomy-and-granularity-report.md). This ADR records the decisions that standard depends on, so they are not relitigated per task.

---

## 2. Decision

### A. One Plane project for ISLAMU Event, forever

`islamu` is the **multi-product workspace**: each distinct ISLAMU product (Event, Configurator, I-VSD, Identity) is one Plane project. ISLAMU Event is permanently **one project, `EVENT`**. Event-specific types, states, and labels are never promoted to workspace scope (`plane ws`), and Event is never split into per-surface or per-client projects.

### B. Surfaces are Labels; programs are Epics; releases are Modules

Each Plane construct inside `EVENT` has exactly one job:

| Construct | Job | Examples |
|---|---|---|
| **Surface / Domain Labels** (multi-select) | Which codebase surface(s) or domain a deliverable touches | `API`, `Web App`, `Database`, `KMP (mobile, watch & TV)`, `Desktop (Avalonia)`, `TUI`, `CLI`, `Payments`, `Finance`, `Federation` |
| **`Epic`** (`type: Epic`) | A bounded capability program of 3–12 peer work items that **must finish and close** | `Authentication Provider Matrix v1`, `Avalonia Desktop Client v1` |
| **Version Modules** | Release scope — "how complete is this version?" | `v0.1.0` (first release, in progress), `v1.0.0` (GA horizon) |
| **Milestones** | Hard-dated freeze gates | `MVP` |

Modules are intentionally used as release containers rather than Plane Releases. Releases are tier-gated on this workspace and unsupported by `plane-compose v0.5.2`. Modules are **not** Cycles: `EVENT` has no Cycles, and agent context must never call Version Modules "Cycles".

### C. Importance is separate from urgency

- **`priority` = functional criticality**, independent of target date:
  - `urgent`: legally or regulatorily mandatory, non-negotiable security or compliance, or a foundational blocker, even when targeted at `v1.0.0`.
  - `high`: a core capability essential to the product's promise.
  - `medium`: standard valuable work.
  - `low`: optional polish.
  - `none`: untriaged.
- **Urgency = Version Module + `due_date` + Milestone.** A `v0.1.0` item is urgent because it blocks the first release; that does not make its `priority` urgent.

### D. Hierarchy and the 3-tier Definition-of-Done rule

- **Hierarchy invariant:** `Epic` (never has a `parent:`) → Work Item → optional Sub-item. The longest legal `parent:` chain is 2. A Sub-item never has children (non-Epic nesting ≤ 1). Each parent may have 2–5 sub-items at most.
- **Tier A — Definition of Done (0 Plane items):** standard unit, integration, and bUnit tests; generated EF migrations; validators; HAL affordances; and documentation parity caused by the feature. These are acceptance criteria and `dev/active/<task>/` steps, never tickets.
- **Tier B — Sub-item:** only for a separately reviewed PR stream, or a heavy dedicated test campaign, inside one deliverable.
- **Tier C — Standalone `Test`/`Refactor`/`Docs`/`CI/CD` item:** only for cross-cutting platform work spanning multiple features (for example, a TUnit migration).
- **Completion test:** every item must be closeable on a concrete date with PR/CI/ADR evidence. Perpetual areas are expressed as Type + Labels + bounded items, never as titles.
- **Assignment:** unstarted items (`Candidate`, `Backlog`, `Waiting Dependency`, `Todo`) carry no assignees. Assignment happens on the move to `Ready`/`In Progress`.

### E. GitHub → Plane Intake gate

GitHub Discussions (ideas) and GitHub Issues (reproducible bugs, documentation defects) are community inputs, not tickets. Issue templates collect evidence; they never ask reporters to mint `EVENT-XXX` IDs or split work by layer. Maintainers promote only confirmed bugs or approved capabilities through Plane **Intake** (`intakes: true`) into `EVENT` as single vertical deliverables, linked back to the originating issue.

### F. Native tooling only: `plane-compose` + Plane MCP

- **`plane-compose v0.5.2`** owns the declarative YAML truth in the gitignored `event/` project: schema, work-item fields, `EVENT-XXX` minting via `id:` write-back, offline validation, and additive push.
- **Plane MCP server** owns remote-only operations: Version Module membership, comments, links, attachments, activity, Intake triage, PQL queries, and deletions.
- **Ordering:** YAML pushes first, MCP mutations second, `plane pull` last so YAML reflects remote truth. The same `EVENT-XXX` must never appear in two `event/work/*.yaml` files. An item created through MCP must never have its `id:` hand-written into YAML, because `plane push` tracks items through `.plane/state.json` and would create a duplicate.
- **No bespoke tooling:** no custom Plane REST client, sync daemon, or Python/Node helper is added to `eng/`. `ISLAMU.AgentWorkflow` keeps only `validate-intents` and `validate-commit`.

### G. Git traceability

- Branches: `feat/EVENT-XXX-<slug>` (uppercase ID).
- Commits: `Refs: EVENT-XXX` (or `Fixes: EVENT-XXX`) above the terminal `Changelog:` declarations.
- PRs: `- Plane: [EVENT-XXX]` under `## Issues`.

Plane's GitHub integration populates the Development panel from these markers, so commit logs and PR URLs are never pasted into YAML `description:`.

---

## 3. Alternatives Considered

1. **Workspace-level Event configuration or multiple Plane projects per surface** (API, Web, Mobile, Desktop, CLI). Rejected: one vertical change routinely spans `API` + `Web App` + `Database`, so per-surface projects would clone tickets across boards. Workspace-level types would also leak Event conventions into unrelated ISLAMU products.
2. **Modules per surface instead of per version.** Rejected: it would remove Plane's native "how complete is `v0.1.0`?" answer. Labels already provide the surface axis.
3. **Plane Releases for version tracking.** Rejected: they are tier-gated on this workspace (`workflows` already returns HTTP 402) and not representable in `plane-compose v0.5.2`.
4. **Custom C# Plane REST CLI in `eng/` for bulk refactoring.** Rejected: it would add a permanent HTTP client to maintain against a moving API, duplicating capabilities that `plane-compose` (YAML, validation, ID write-back) and Plane MCP (deletes, modules, metadata) already provide.
5. **Preserving the machine-backfilled `Done` cohorts unchanged.** Rejected: 511 single-commit tickets and 176 surface stubs carry no planning meaning. Git history is already the durable record of individual commits. Consolidated capability items preserve their unique content. Any ID cited in Git, ADRs, or docs is protected from deletion.

---

## 4. Consequences

- **Positive:** the board answers real questions (version completeness, per-surface scope, must-have requirements) within minutes. Agents load less noise, and every surviving item is completable and traceable to Git.
- **Negative / accepted:** backlog consolidation permanently deletes Plane records, including their sequence numbers and activity feeds. This is mitigated by:
  - an immutable gitignored pre-refactor snapshot of `event/`;
  - a remote metadata export (comments, links, attachments) for deletion candidates;
  - a lossless consolidation manifest reviewed by the maintainer before any deletion;
  - a protected allowlist of every `EVENT-XXX` cited anywhere in the repository or Git history (those items are kept or marked `Replaced`, never deleted).
- **Operational:** `event/`, its backups, and consolidation ledgers stay gitignored because they contain internal security backlog content and workspace member details. Only this ADR, the taxonomy standard, skills, rules, and docs are committed.

---

## 5. Related

- [Work-item taxonomy and granularity standard](../project-management/work-item-taxonomy-and-granularity-report.md)
- [I-VSD assessment: Plane project management standardization](../../../islamic-value-sensitive-design/workstreams/i-vsd-plane-project-management-standardization.md)
- [Agentic Context Engineering — Single-Project `EVENT` taxonomy policy](../AGENTIC_CONTEXT_ENGINEERING.md#single-project-event-taxonomy-granularity--importance-policy)
- [`plane-compose` skill](../../../.agents/skills/plane-compose/SKILL.md) · [`plane-mcp` skill](../../../.agents/skills/plane-mcp/SKILL.md) · [`plane-tracking` path rule](../../../.agents/rules/plane-tracking.md)
