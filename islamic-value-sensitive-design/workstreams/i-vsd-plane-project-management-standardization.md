# ISLAMU Event — Plane Project Management Standardization & Backlog Refactoring — I-VSD Planning Report

Last Updated: 2026-11-11 Europe/Brussels

## Review Metadata
- Mode: planning
- Subject: Multi-phased standardization of documentation, Agentic Context Engineering artifacts, repository tooling, and lossless Plane Cloud backlog refactoring across all 2,616 work items in `EVENT`
- Workstream: plane-project-management-standardization
- Report kind: planning report
- Report status: current
- Disposition: plan-aligned
- Evidence cutoff: 2026-11-11
- Reviewed input: `develop` (`8b890d71da`), local gitignored `event/` snapshot (`2,616` work items), and plan triad `dev/active/plane-project-management-standardization/`
- Supersedes: none (extends `islamic-value-sensitive-design/workstreams/i-vsd-plane-compose-workflow-integration.md`)

---

## Executive Summary

This planning assessment evaluates the provider-controlled ethical, operational, and data-stewardship risks of standardizing ISLAMU Event's entire project-management lifecycle around [`work-item-taxonomy-and-granularity-report.md`](../../docs/internal/project-management/work-item-taxonomy-and-granularity-report.md) and refactoring **all `2,616` existing Plane work items** (including merging, converting into depth-1 sub-work items, absorbing horizontal fragments into descriptions, and permanently deleting non-compliant tickets on Plane Cloud).

An empirical audit of [`event/work/workitems.yaml`](../../event/work/workitems.yaml) shows that **`1,747` of `2,616` items (`66.8%`)** came from three historical machine-backfill cohorts (`1,060` unstarted Oppworx 8-part/4-part horizontal scaffolds, `176` controller/worker surface-inventory items, and `511` one-commit-per-ticket `Done` items), alongside **`2,614/2,616` items** pre-assigned to `Amir Akrari` and **`0` Epics**. Via `/grill-me` intake, the maintainer approved:
1. **Option A (Full Compliance Across Open + `685` Backfilled `Done` Cohorts):** Refactor both open cohorts and backfilled `Done` commit/controller items into cohesive vertical `v0.1.0` capabilities while locking every externally referenced `EVENT-XXX` ID (`PinnedIds`) and preserving all unique specifications without data loss.
2. **Zero Custom Plane API Tooling (`plane-compose` + `plane-mcp` Only):** Reject adding any custom C# Plane REST client or complex maintenance scripts; execute schema/item YAML sync natively via **`plane-compose v0.5.2`** and perform remote deletions, Version Module assignments (`v0.1.0`/`v1.0.0`), Epic UUID queries, and pre-delete comment/link audits via **`plane-mcp`**, gated by a gitignored Consolidation Manifest review pause before live deletions execute.

**Plan Alignment:** All five findings (`IVSD-F001`–`IVSD-F005`) map directly to controls in [`plane-project-management-standardization-plan.md`](../../dev/active/plane-project-management-standardization/plane-project-management-standardization-plan.md) §9 (`current | plan-aligned`). Live remote destructive deletions remain explicitly gated behind Phase 3 backup verification, remote comment/link pre-export, and user approval of the Consolidation Manifest.

---

## Scope

In scope for provider-responsibility evaluation during planning intake:
1. **Backlog Data Stewardship & Lossless Refactoring**: Consolidating, re-parenting, updating, and deleting non-compliant items across `2,616` work items in `ISLAMU Event` (`EVENT`), assigning Version Modules (`v0.1.0`/`v1.0.0`), bounded Epics, Surface Labels, Importance-based `priority`, and clearing stale auto-assignments.
2. **Documentation Normalization & Linking**: Integrating [`docs/internal/project-management/work-item-taxonomy-and-granularity-report.md`](../../docs/internal/project-management/work-item-taxonomy-and-granularity-report.md) into `docs/README.md`, `docs/internal/index.md`, `CONTRIBUTING.md`, `docs/internal/CONTRIBUTING.md`, `docs/internal/QUICK_REFERENCE.md`, `docs/internal/GOVERNANCE.md`, `docs/internal/AGENTIC_CONTEXT_ENGINEERING.md`, and ADR/operational governance docs (fixing artifact-style absolute `file://` URIs and missing metadata blocks).
3. **Agentic Context Engineering Integration**: Updating `.agents/agents/*`, `.agents/rules/*`, stale `.agents/skills/plane-mcp/` data, `.agents/contract/intents.yaml`, `.agents/benchmarks/cold-start-tasks.yaml`, `AGENTS.md`, and `.github/ISSUE_TEMPLATE/`, verified by existing `ISLAMU.AgentWorkflow` validation (`validate-intents` & `ISLAMU.AgentWorkflow.Tests`).
4. **Remote Execution & Credential Boundaries**: Performing schema push, additive Epic/Feature updates, and full post-cleanup pull via **`plane-compose v0.5.2`**, while performing remote comment/link inspection, Version Module assignment (`v0.1.0`/`v1.0.0`), and controlled deletions strictly via **`plane-mcp`** under authenticated PAT/MCP boundaries without adding custom REST tooling or exposing `event/` data in version control.

Out of scope: changing `ISLAMU` workspace billing tiers, splitting `ISLAMU Event` into multiple Plane projects, introducing Python/Node scripts (forbidden by `AGENTS.md` Rule #9), or issuing religious-legal rulings.

---

## Claim Boundary

All factual claims reflect the repository on branch `develop` (`HEAD` commit `8b890d71da`) and local `event/` YAML state verified on `2026-11-11` (`plane v0.5.2`). Statements about historical Plane behavior rely on observed `event/work/workitems.yaml` cohorts, inspected `plane-compose v0.5.2` / `plane-sdk 0.2.19` Python sources, and Plane REST API v1 specifications (`60 req/min` cloud limit). Execution time estimates (`~20–45 minutes` for bulk API mutations) are bounded inferences derived from the verified `60 req/min` Plane Cloud rate limit; no destructive remote mutations or `plane push` writes have been run.

---

## Findings

### `IVSD-F001`: Irreversible Data Loss Risk During Bulk Plane Work-Item Deletion and Consolidation
- **Affected persons & systems:** Project maintainers, contributors, AI planning agents, and anyone auditing historical design intent in `EVENT`.
- **Failure mechanism:** Over **`1,423` of `1,634` non-Done items** and **`775` of `982` Done items** contain rich multi-line descriptions detailing domain constraints, tenant-isolation expectations, privacy checks, and abuse cases (including security notes in `EVENT-4970..4988` and domain models in `EVENT-5020..6069`). Because `plane-compose v0.5.2` omits CLI `delete` support, bulk deletion via REST API `DELETE /work-items/{id}/` immediately destroys remote records. If a tool crashes mid-run under `HTTP 429` throttling or deletes source items before verifying every domain requirement was absorbed into its surviving parent Epic/Feature, valuable specifications are permanently lost.
- **Provider-controlled decision:** Whether destructive deletions require mandatory pre-mutation export snapshots, lossless paragraph-level absorption verification, dry-run diff previews, and resumable per-ID execution ledgers (`IVSD-M001`).
- **Evidence limits:** `workitems.yaml` line-length measurements (`1,423` rich non-Done, `775` rich Done) are directly observed; whether remote Plane contains extra UI comments or activity history not exported into `workitems.yaml` (`plane-compose v0.5.2` does not export comments/links/worklogs) is a known missing-evidence risk.

### `IVSD-F002`: Misleading Historical Metrics & Broken Traceability From Backfilled `Done` Cohorts vs. Deletion
- **Affected persons & systems:** Adopters reading roadmap/status views, contributors tracing why code/ADRs exist, and repository Git history (`EVENT-XXX` trailers).
- **Failure mechanism:** Two backfilled cohorts—`174 Done` one-surface-per-item tickets (`EVENT-6075..6250`) and `511 Done` one-commit-per-ticket items (`EVENT-6312..6821`)—simultaneously inflate `Done` counts (making internal work look like 685 separate finished user features when commits belong in Git) while risking broken audit references if deleted blindly where commits, docs, PRs, or `milestones.yaml` cite specific IDs (verified existing refs: `EVENT-001`, `EVENT-350`, `EVENT-4950`, `EVENT-4958`, `EVENT-4968`, `EVENT-4942`, `EVENT-453`, `EVENT-430`).
- **Provider-controlled decision:** Whether `685` backfilled `Done` items are consolidated/deleted alongside open items or preserved in read-only archival form, and how externally referenced `EVENT-XXX` IDs are pinned (`IVSD-M002`).
- **Evidence limits:** Git commit grep (`git log --all --grep="EVENT-"`) and repo grep (`docs/`, `.agents/`, `src/`, `tests/`, `pages.yaml`, `milestones.yaml`) are verified; external PR comments on GitHub not mirrored in local git commits are not locally verifiable without querying GitHub.

### `IVSD-F003`: Confidential Security Details, Operational Notes & Token Leakage Across Docs, Ledgers & Tooling
- **Affected persons & systems:** Instance operators, beta testers, future self-hosters, and contributors reading public Git history.
- **Failure mechanism:** `event/work/workitems.yaml` contains `151` `Security` items (threat modeling, tenant bypass safeguards, impersonation controls), PAT connections in Plane CLI config, and team member emails/UUIDs. Writing migration mapping ledgers (`EVENT-old -> EVENT-new`), C# refactoring artifacts, or documentation examples inside tracked directories (`dev/`, `docs/`, `eng/`) could accidentally leak private security posture or member PII/tokens into public Git commits; furthermore, copying local `file:///home/<user>/...` workspace paths into tracked docs (`docs/internal/project-management/work-item-taxonomy-and-granularity-report.md`) leaks local workstation paths and breaks CI link checks.
- **Provider-controlled decision:** Strict gitignore isolation of `event/` + `event-backup-*/` + ephemeral cleanup ledgers, scrubbing personal filesystem paths in tracked docs, and loading Plane API credentials solely from approved secret stores / env / CLI auth config without plaintext logging (`IVSD-M003`).

### `IVSD-F004`: Operational Exhaustion (`Israf` / `Raf' al-Haraj`) From `2,614` Pre-Assigned Items & `60 req/min` API Limits
- **Affected persons & systems:** Human maintainers (`Amir Akrari`, currently auto-assigned to `2,614` of `2,616` items) and autonomous AI coding agents parsing bloated context.
- **Failure mechanism:** Keeping `1,449` unstarted `Backlog`/`Candidate` items assigned to one maintainer renders personal "Assigned to me" views useless, while leaving `1,060` 8-part scaffold tickets causes agents to waste context tokens deduplicating dead noise and risk partial API failure under Plane Cloud's `60 req/min` rate limit if bulk edits lack pacing and checkpointing. Meanwhile, stale agent guidance in `.agents/skills/plane-mcp/resources/islamu-event-project.md` actively instructs agents to assign `Modules` (`v0.1.0`/`v1.0.0`) as `"Cycles"` and keeps outdated workflows.
- **Provider-controlled decision:** Executing rate-limited batch mutations natively via `plane-compose` (`--resume`) and bounded `plane-mcp` operations (zero bespoke REST tooling in `eng/`), unassigning unstarted backlog queues, fixing stale MCP/agent/doc surfaces, and validating Plane YAML contracts structurally (`IVSD-M004`).

### `IVSD-F005`: Cross-Member & Shared Workspace Governance Authority (`Shura`)
- **Affected persons & systems:** All `4–5` active workspace members on `islamu` (`Amir Akrari`, `zah_nab`, `wassimyez04`, `Plane MCP`).
- **Failure mechanism:** Running destructive `DELETE` operations or bulk-reassigning/closing tickets remotely on shared Plane Cloud without explicit maintainer confirmation of the scope, `Done` disposition, and dry-run manifest violates shared stewardship and cannot be undone once Plane Cloud deletes the IDs (even backups restore only YAML data, losing original sequence numbers or remote comments/activities).
- **Provider-controlled decision:** Mandatory interactive `/grill-me` alignment before finalizing deletion scope plus explicit per-phase dry-run and backup confirmation gates before live remote execution (`IVSD-M005`).

### Finding Index

| ID | Lifecycle | Severity | Claim type | Principle / Domain | Stakeholder | Provider-controlled decision | Evidence | Validation | Mitigation |
|---|---|---|---|---|---|---|---|---|---|
| `IVSD-F001` | open | High | Data Loss / Integrity | Amanah (stewardship), Data integrity | Maintainers, contributors, AI agents | Scope & verification of irreversible bulk deletes + merges | `workitems.yaml` audit (`1,423` rich open, `775` rich Done) | Direct YAML audit | `IVSD-M001` |
| `IVSD-F002` | open | High | Truthfulness / Traceability | Sidq (truthfulness), Auditability | Maintainers, adopters, contributors | Disposition of `685` backfilled `Done` items & ID pinning | Cohort breakdown (`174` surface + `511` commit backfill), git/doc grep | Git/repo grep | `IVSD-M002` |
| `IVSD-F003` | open | High | Confidentiality / Secret Exposure | Sirr (privacy), Secret hygiene | Operators, beta testers, workspace members | Keeping `event/`, cleanup ledgers, PATs & local paths out of Git | `.gitignore#L457`, tracked taxonomy doc `file://` inspection | File & `.gitignore` check | `IVSD-M003` |
| `IVSD-F004` | open | Medium | Cognitive Waste / Friction | Israf, Raf' al-Haraj | Maintainers (`2,614` assigned), AI agents | Native `plane-compose` + `plane-mcp` batching, `assignees:` reset & skill/doc sync | Assignee count (`2,614`), `rate_limit.py`, stale `plane-mcp` resource | CLI & skill inspection | `IVSD-M004` |
| `IVSD-F005` | open | High | Unauthorized Shared Mutation | Shura (governance), Authority boundaries | All `islamu` workspace members | Explicit approval via `/grill-me` + dry-run gates before `DELETE` | Workspace member sync (`5` members), Plane REST `DELETE` behavior | API docs & CLI output | `IVSD-M005` |

---

## Recommendations

- **`IVSD-M001`: Immutable Pre-Mutation Snapshot + Lossless Description Absorption Ledger**: Before executing any write or delete on Plane Cloud, create a timestamped gitignored snapshot (`event-backup-<YYYYMMDD-HHMMSS>/`) plus export any remote comments, external links, and attachments via `plane-mcp` before deletion. Require a machine-verified absorption manifest proving every merged/deleted item's unique requirements, security constraints, and acceptance notes appear inside a surviving parent Feature/Epic (or consolidated Plane Page archive) before `DELETE` executes.
- **`IVSD-M002`: Immutable External-Reference Pinning + Truthful `v0.1.0` Rollup**: Run an automated repo-wide guard scanning Git commit logs, `docs/`, `.agents/`, `eng/`, `islamic-value-sensitive-design/`, `pages.yaml`, and `milestones.yaml` to produce a protected `PinnedIds` allowlist (`EVENT-001`, `EVENT-350`, `EVENT-4950`, `EVENT-4958`, `EVENT-4968`, etc.) that tools are strictly forbidden from deleting. Roll remaining machine-backfilled commit/controller items into true completed vertical Features according to the user-selected `/grill-me` disposition.
- **`IVSD-M003`: Strict Gitignore & Path-Sanitization Quarantine**: Store all temporary refactoring manifests, old-to-new ID mapping ledgers, and backup snapshots exclusively under gitignored paths (`event-backup-*/` or `dev/active/`), resolve Plane credentials solely via existing auth config/environment injection (never hardcode PATs or log auth headers), and rewrite local workstation URIs (`file:///home/<user>/...`) in `docs/internal/project-management/work-item-taxonomy-and-granularity-report.md` to portable relative Markdown links verified by CI/diff checks.
- **`IVSD-M004`: Native `plane-compose` + `plane-mcp` Execution & Full Doc/Agent Alignment**: Perform YAML creation/updates/pulls with `plane-compose v0.5.2` (native `50 req/min` limiter + `--resume`) and perform remote deletions, comment/link pre-exports, and Version Module assignments with `plane-mcp` (zero custom REST maintenance tools); clear stale pre-assignments on unstarted `Candidate`/`Backlog`/`Waiting Dependency`/`Todo` items; author `.agents/rules/plane-tracking.md`, normalize orphaned docs, and overhaul stale `plane-mcp` / agent definitions.
- **`IVSD-M005`: Approved Scope & Staged Pre-Deletion Manifest Gate**: Option A and native `plane-compose` + `plane-mcp` tooling are approved by maintainer `/grill-me` alignment; live destructive deletions in Phase 4 remain gated behind explicit user sign-off on the Phase 3 gitignored Consolidation Manifest and immutable snapshot verification.

*Rejected alternatives:* (1) Soft-closing ~1,700+ items as `Replaced` without `DELETE` was rejected as default because it preserves backlog bloat (`1.75 MB`) against zero-non-compliant-items goals (`Replaced` remains reserved only for externally pinned IDs if needed). (2) Building a custom C# REST API HTTP client project under `eng/tools/` was explicitly rejected by maintainer `/grill-me` decision to avoid fragile, high-maintenance bespoke Plane API code when `plane-compose` + `plane-mcp` already cover every operation natively. (3) Leaving the `685` machine-backfilled `Done` items untouched (Option B) was rejected in favor of Option A full compliance with Git/ADR `PinnedIds` protection.

---

## Stakeholders

- **Workspace Owners & Maintainers (`Amir Akrari` + core team):** Rely on trustworthy release progress (`v0.1.0`, `v1.0.0`), non-polluted assignee views, and zero accidental deletion of real roadmap/security ideas.
- **Co-Contributors (`zah_nab`, `wassimyez04`):** Share the `islamu` Plane workspace (`EVENT`) and depend on clean taxonomy, readable boards, and documented contribution rules (`CONTRIBUTING.md`, `plane-mcp`).
- **AI Coding Agents & Orchestrators:** Consume `AGENTS.md`, `.agents/`, `docs/internal/`, and `event/work/` daily; suffer severe token bloat and misclassification when `2,616` flat items and stale `plane-mcp` instructions conflict.
- **External Adopters & Evaluators (Indirect):** Read public roadmaps (`docs/public/home/README.md`) and technical credibility reflected by `docs/`.

---

## I-VSD Principles And Domains

- **`Amanah` (Trusteeship & Non-Destruction of Shared Knowledge):** Protects contributor specs, security designs, and remote comments against silent destruction during bulk consolidation.
- **`Sidq` (Truthfulness in Engineering Evidence & Status):** Ensures `Done` items and `v0.1.0` completion metrics reflect genuine shipped capabilities rather than 511 individual commit logs or 8-part boilerplate stubs.
- **`Sirr` (Confidentiality & Privacy Boundary):** Prevents internal security tickets, workspace member identifiers, and credentials from leaking into public git commits or developer filesystem links.
- **`Israf` & `Raf' al-Haraj` (Stewardship Against Waste & Hardship):** Eliminates cognitive/token waste of a 1.75 MB uncurated backlog and prevents rate-limit thrashing or manual maintenance toil.
- **`Shura` (Consultative Governance):** Requires explicit maintainer sign-off before destructive Plane Cloud edits affect shared workspace collaborators.

---

## Validation Gaps

- **Remote-only Plane metadata not yet exported:** `plane-compose v0.5.2` does not pull `work_items/{id}/comments/`, `links/`, `worklogs/`, or `attachments/` into `workitems.yaml`. Deleting any work item remotely without first querying/archiving remote comments & external links via REST API creates an unverified data-loss blind spot.
- **External GitHub PR/Issue links:** Local `git log` was audited, but GitHub PR descriptions/comments referencing historical `EVENT-XXX` numbers outside commit messages have not been scraped.
- **Cycle/Module item assignments in Plane Cloud:** `event/work/modules.yaml` does not currently mirror full remote `work_items:` lists offline without querying remote module membership via `plane-mcp`.

---

## Escalation Needed

1. **Execution-time destructive confirmation (`implementation` gate):** Before firing batch `DELETE` operations via `plane-mcp` in Phase 4, the implementing agent must present the Phase 3 gitignored Consolidation Manifest summary (`backlog-consolidation-ledger.md`) and pre-refactor snapshot verification (`event-backup-*`) for maintainer confirmation (`IVSD-M005`).
*(Planning-level `Done`-cohort scope and tooling architecture branches were resolved via `/grill-me` Option A + native `plane-compose` & `plane-mcp` decision.)*

---

## Evidence Reviewed

- Local Plane snapshot: `event/work/workitems.yaml` (`2,616` items analyzed across cohorts: `1,060` Oppworx `EVENT-4969..6069`, `176` Surface `EVENT-6075..6250`, `511` Git commit backfill `EVENT-6312..6821`, `869` hand-authored `EVENT-0001..4959`), `event/work/modules.yaml`, `event/work/milestones.yaml`, `event/work/pages.yaml`, `event/schema/*.yaml`, `event/workspace.yaml`.
- CLI & SDK source verification: `plane-compose v0.5.2` (`sync/planner.py`, `sync/executor.py`, `cli/push.py`, `cli/schema.py`, `utils/rate_limit.py`, `backend/plane.py`) and `plane-sdk 0.2.19` (`plane/api/work_items/*.py`, `plane/api/modules.py`).
- Repository docs & agent context: `docs/README.md`, `docs/internal/index.md`, `docs/internal/QUICK_REFERENCE.md`, `docs/internal/GOVERNANCE.md`, `docs/internal/CONTRIBUTING.md`, `docs/internal/AGENTIC_CONTEXT_ENGINEERING.md`, `docs/internal/DOCUMENTATION_ARCHITECTURE.md`, `docs/internal/AI_AGENT_EXPERIENCE_HARDENING.md`, `docs/internal/project-management/work-item-taxonomy-and-granularity-report.md`, `docs/public/home/README.md`, `.agents/CONTEXT_ENGINEERING.md`, `.agents/agents/*`, `.agents/rules/*`, `.agents/skills/plane-mcp/*`, `.agents/contract/intents.yaml`, `eng/agent-workflow/src/ISLAMU.AgentWorkflow/Program.cs`, `eng/tools/*`, and `git log --all --grep="EVENT-"`.

---

## Missing Evidence

- Live audit of remote Plane comments/links/attachments on candidate deletion targets (resolved in Phase 3 Task 3.4 via `plane-mcp`).
- Remote module membership inventory via `plane-mcp` for `v0.1.0` and `v1.0.0` (resolved in Phase 3 Task 3.4).
- External GitHub PR descriptions or review comments referencing historical `EVENT-XXX` IDs outside local `git log` (optional remote cross-check before Phase 4 Task 4.4).

---

## Context Inventory

- Existing sibling report: [`islamic-value-sensitive-design/workstreams/i-vsd-plane-compose-workflow-integration.md`](i-vsd-plane-compose-workflow-integration.md) (covers gitignore boundary, active file sharding, evidence-based `Done` transitions, and worktree symlinks — preserved intact).
- Newly discovered tracked doc: [`docs/internal/project-management/work-item-taxonomy-and-granularity-report.md`](../../docs/internal/project-management/work-item-taxonomy-and-granularity-report.md) (untracked, orphaned, contains absolute `/home/<user>/...` `file://` URIs needing normalization).
- Stale reference artifact: [`.agents/skills/plane-mcp/resources/islamu-event-project.md`](../../.agents/skills/plane-mcp/resources/islamu-event-project.md) (outdated `Cycle` terminology for `Modules` and draft v1 backfill instructions).

---

## Review Lifecycle

| Date | Previous status | New status | Trigger | Evidence/replacement |
|---|---|---|---|---|
| 2026-11-11 | none | draft | Planning intake for `plane-project-management-standardization` | Shared repository + `workitems.yaml` audit at `8b890d71da` |
| 2026-11-11 | draft | current | `/grill-me` branches resolved (Option A + native `plane-compose` & `plane-mcp` stack) and mapped 1:1 into completed plan triad | `dev/active/plane-project-management-standardization/` |

---

## Planning Handoff

- Workstream: `plane-project-management-standardization`
- Status: `current` (`plan-aligned`)
- Reviewed input: `develop` (`8b890d71da`) + `event/work/workitems.yaml` (`2,616` items) + `dev/active/plane-project-management-standardization/`
- Findings and mitigations: `IVSD-F001 -> IVSD-M001`, `IVSD-F002 -> IVSD-M002`, `IVSD-F003 -> IVSD-M003`, `IVSD-F004 -> IVSD-M004`, `IVSD-F005 -> IVSD-M005`
- Required plan mappings:
  - `IVSD-F001 / IVSD-M001` -> Invariant 3.3 + Phase 3 Tasks 3.1, 3.4, 3.5 & Phase 4 Tasks 4.2, 4.4 (Plan §9)
  - `IVSD-F002 / IVSD-M002` -> Invariant 3.4 + Phase 3 Task 3.3 & Phase 4 Tasks 4.1, 4.3, 4.4 (Plan §9)
  - `IVSD-F003 / IVSD-M003` -> Invariant 3.1 + Phase 1 Task 1.1, Phase 2 Tasks 2.3, 2.4 & Phase 3 Tasks 3.1, 3.5 (Plan §9)
  - `IVSD-F004 / IVSD-M004` -> Invariant 3.2 + Phase 1–4 docs/skills/MCP sync + Phase 4 Tasks 4.2, 4.6 (Plan §9)
  - `IVSD-F005 / IVSD-M005` -> Invariant 3.5 + Phase 3 Task 3.6 Review Gate & Phase 4 Task 4.4 (Plan §9)
- Escalations required before: `implementation` (live remote dry-run manifest review & approval in Phase 3 Task 3.6 before firing `plane-mcp` deletions in Phase 4 Task 4.4).
- Refresh triggers: Changes to deletion scope (`Done` cohort inclusion), introduction of custom REST scripts outside `plane-compose`/`plane-mcp`, credential storage changes, or moving gitignored ledgers into tracked paths.
