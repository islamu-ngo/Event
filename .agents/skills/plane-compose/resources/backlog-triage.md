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

## Outcome-oriented decomposition

For a large feature, use a parent Feature/Epic **only if supported**, then a small set of independently demonstrable tasks (e.g., contract, server implementation, UI, observability, tests/docs). Prefer vertical slices when that reduces integration risk. Do not create an Epic without the `epics` feature or type support. `parent` holds the real work item ID; do not nest `children` or invent canonical Plane sequence IDs.

Each proposed work item should answer: *Why now? What observable behavior changes? What proves it works? What blocks it? Where is the relevant code?* If those cannot be answered, label it as discovery rather than confidently marking it ready.

## Priority policy (project conventions win)

Use Plane's accepted values `urgent`, `high`, `medium`, `low`, `none`:

| Value | Typical evidence (examples, not a fixed formula) |
| --- | --- |
| `urgent` | Active production outage, exploitable vulnerability, or immediate release-stopper with a confirmed time constraint. |
| `high` | Customer/user-impacting defect, critical dependency, or near-term milestone blocker. |
| `medium` | Planned core functionality or improvement with meaningful benefit but no immediate blocker. |
| `low` | Nice-to-have polish, speculative optimization, low impact cleanup. |
| `none` | Untriaged, informational or uncertain; avoid false precision. |

Compare *impact, urgency, dependency leverage, confidence and effort*. Effort affects **sequencing and size**, not necessarily priority. Record assumptions; ask a targeted question if a business-critical missing fact changes the ordering. Do not invent severity, dates, ROI or estimates.

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
