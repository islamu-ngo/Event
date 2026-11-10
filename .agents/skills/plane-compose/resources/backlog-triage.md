# Backlog intake, deduplication, decomposition and priority

**Read when:** a user asks for tasks, an implementation plan, prioritization, the next best task, or a complete remaining-work inventory.

## Intake protocol

1. Collect the stated outcome, deliverable, deadlines, exclusions and acceptance criteria from the user.
2. Inspect the *actual* codebase, docs, test failures, open tasks and confirmed known constraints. Record what is fact, what is an inference and what is unknown.
3. If project tracking exists: locate matching issues in **local and remote context**. A `plane diff` does not enumerate all remote-only tasks; use a guarded full pull/clone when completeness matters (see [progress reporting](progress-reporting.md)).
4. Deduplicate by desired outcome, not by literal title. Prefer expanding an existing task. If two tickets overlap, explain the duplication; don't delete or close them automatically.
5. Convert concrete gaps into a **bounded, useful backlog**. Maintain the project's exact state/type/label vocabulary.
6. Map prerequisites using `dependencies.blocked_by`, adding links only when the referenced IDs exist and the dependency is real.
7. Prioritize and report **why**. Edit project YAML only when requested/authorized, then validate and dry-run.

## Outcome-oriented decomposition & granularity bar

Every Plane work item must pass four structural gates:

1. **Completable Outcome (No Perpetual Areas):** Every item requires a verifiable finish line (merged PR/CI evidence, release, or published ADR). Never create ongoing quality-area titles (e.g., `"Make webapp secure"`, `"Improve test coverage"`); use `type:` (`Security`, `Test`, `Refactor`) + labels + concrete completable deliverables.
2. **Vertical & Atomic Slicing (INVEST / SPIDR):** One cohesive vertical capability or **1 external/DB/payment provider** per work item (`"Stripe Connect"` and `"PayPal Checkout"` are separate items; never group multiple providers together, and never split one entity across horizontal layer tickets like `Model`, `API`, `Validator`, `UI`, `Test`, `Docs`).
3. **3-Tier Placement for Tests, Docs & Sub-Items:**
   - *Tier 0 (Not in Plane):* Standard feature unit/integration/UI tests, migrations, validators, and feature doc updates belong in the parent item's Acceptance Criteria (Definition of Done) and local task execution checklists — **zero** separate Plane items or sub-items.
   - *Tier 1 (Sub-Work Items, max depth = 1, 2–5 max):* Use `parent: <parent-uuid>` only when one deliverable genuinely spans separately scheduled/reviewed PR pillars or a distinct heavy test campaign (e.g., E2E Playwright or adversarial security suite separate from the code PR). Never nest sub-sub-items.
   - *Tier 2 (Standalone top-level item or Epic):* Use standalone `Test`/`Refactor`/`Docs`/`Security` items only for cross-cutting platform work; promote multi-week multi-deliverable programs into a finite **`Epic`** (`is_epic: true` if enabled).
4. **No Pre-Assigned Backlog Graveyards:** Never set `assignees:` on unstarted `Candidate` / `Backlog` / `Waiting Dependency` items; assign only when advancing to `Ready` or `In Progress`.

Each proposed work item should answer: *Why now? What observable behavior changes? What proves it works? What blocks it? Where is the relevant code?* If those cannot be answered, label/keep it in Intake or `Candidate` rather than marking it ready.

## Priority policy (Importance vs. Time Urgency)

Separate **functional importance (`priority`)** from **schedule urgency (`module` / `milestone` / `due_date`)** using Plane's accepted values `urgent`, `high`, `medium`, `low`, `none`:

| Value | Functional Must-Have / Criticality Evidence |
| --- | --- |
| `urgent` | Legally or regulatorily mandatory (compliance/law), non-negotiable security requirement, active production outage, or foundational platform blocker — **even if planned for a later version** (e.g., `v1.0.0`). |
| `high` | High-value core or "killer" requirement essential to the product's core promise or critical dependency chain. |
| `medium` | Planned standard functionality with meaningful benefit once core paths are covered. |
| `low` | Optional nice-to-have polish or speculative enhancement easily cut without core loss. |
| `none` | Untriaged, informational, or unassessed; avoid false precision. |

Track **time urgency** orthogonally via **Target Version Module** (items in the active next release module — e.g. `v0.1.0` when building the first release — are inherently release-urgent), **Milestones**, and explicit **`due_date`** fields. Compare *requirement criticality, release horizon, dependency leverage, confidence, and effort*. Record assumptions; never invent dates or assignees.

## Select the next task

Filter to not completed/cancelled, then assess:

- Whether known `blocked_by` items are complete or still outstanding.
- Whether work is actually Ready (scope, acceptance, dependencies, ownership).
- Whether user instructions, deadlines, risk or release critical-path outrank nominal priority.
- Current work-in-progress; finish/review high-value work before opening too many new threads.
- Available engineering context. Choose one implementable slice; mention alternatives briefly.

For unverified remote state, mark recommendations **based on local snapshot** rather than presenting them as the authoritative team queue.

## Outputs

For an intake: a short proposed work-item set with *ID (or pending local ID), title, priority, rationale, dependencies, acceptance criteria, source confidence*. For “what next”: one recommended unblocked item plus why; disclose unresolved blockers and context freshness. For actual changes: include changed YAML paths and remote sync status.

**Official:** https://developers.plane.so/dev-tools/plane-compose/project-files · https://developers.plane.so/dev-tools/plane-compose/sync-projects
