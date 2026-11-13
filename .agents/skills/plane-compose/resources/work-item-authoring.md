# Work item authoring: correct YAML, stable identity, clear acceptance

**Read when:** creating, updating or linking tasks, bugs, features, chores or improvements in a Plane Compose project.

## First, match instead of duplicating

Look for an existing item by canonical Plane ID, title/intent, owning module, code references, and dependency graph. Search *all* `work/*.yaml` task files and (if relevant) remotely fetched items; `plane diff` alone misses remote-only items. Preserve the item's `id`, original metadata, and user-written description when updating. A title change is not justification for creating another task.

### Stable IDs & canonical `PROJ-N` writeback (v0.5.2)

- **Existing Plane items:** Keep the canonical uppercase ID (`APP-37`). Never invent `APP-99` based on sequence guesses.
- **Brand-new items pushed before branching (recommended for Git linking):** Omit `id:` in the YAML item (`- title: ...`), finalize the title locally, and push once (`plane validate && plane push --work-only`). In `v0.5.2`, `id_writer.py` **only writes `APP-37` back into YAML when `id:` was omitted** (it skips writeback if any user `id:` is present), and remaps `.plane/state.json` automatically to `id:APP-37`. That gives you canonical uppercase `APP-37` immediately for your branch, commits, and PR.
- **Brand-new items kept offline before push authorization:** Either keep `id:` omitted while still unpushed (accepting the non-blocking `validate` warning, and pushing once before renaming a pushed title), or set a temporary local slug (`id: active-<slug>`) knowing that `v0.5.2` keeps that slug in YAML on push while storing `sequence_id` only in `.plane/state.json` (`plane state show --json`). Never hand-edit a YAML `id:` from a local slug to `APP-37` without updating `.plane/state.json` or running `plane pull`, or the next push will create a duplicate.
- **No duplicate IDs:** IDs must remain unique across **all** `work/*.yaml` files. Retire a finished active file by folding its blocks (with their IDs) into `work/workitems.yaml` and deleting it; see [plan-tracking](plan-tracking.md).
- **Do not ship placeholder templates inside `work/*.yaml`:** Any `*.yaml` directly under `work/` (except `cycles.yaml`, `modules.yaml`, `milestones.yaml`, `pages.yaml`) is ingested as live work items. Store templates outside `work/` (e.g., `templates/active-workitem.yaml`).

## Recommended task format

The following illustrates the schema; use **only actual** states, labels, types, and people defined in the selected project. The example `APP-12` etc. represent IDs obtained from Plane, not guesses:

```yaml
workitems:
  - title: Reject expired refresh tokens at API boundary  # omit `id:` on first push so id_writer.py writes canonical APP-N; set `id: APP-37` for existing items
    type: Task
    state: Ready
    priority: high
    labels: [Backend]
    description: |
      Context: The refresh endpoint currently accepts expired tokens.
      Outcome: Expired tokens are rejected with the documented error.

      Acceptance criteria:
      - Requests with expired refresh tokens fail consistently.
      - Valid refresh tokens retain the expected behavior.
      - Integration tests cover both cases.

      Verification: pending implementation and test execution.
    dependencies:
      blocked_by: [APP-12]
    relations:
      relates_to: [APP-19]
```

For a task already published, use its canonical ID. An initial task should not contain imaginary dependencies, assignees or dates; omit fields you cannot justify. `description` is free text, so acceptance-criteria headings are a **skill convention**, not special Plane Compose fields.

## Supported fields (v0.5.2)

| Field | Behavior |
| --- | --- |
| `title` | Required. Make it outcome/action specific. |
| `id` | Stable tracking identity; strongly recommended for all items. |
| `type` | Existing Plane name, e.g. `Task`, `Bug`, `Feature`, `Chore`, `Improvement`, or `{name: Task, id: <remote_uuid>}` as written by pull. |
| `state` | Must match `schema/states.yaml`; transition validity may be restricted by workflows. |
| `priority` | `urgent`, `high`, `medium`, `low`, `none`. |
| `labels` | Existing names from schema; do not invent ad hoc labels. |
| `assignees` | Known member email(s) or UUID(s). Never assign a human without evidence/instruction. |
| `start_date`, `due_date` | ISO `YYYY-MM-DD`; due date cannot precede start date. |
| `parent` | Parent work item **UUID** in `v0.5.2` (as pulled from API; `v0.5.2` passes `parent:` raw without resolving `APP-12`). Flatten child records; no nested `children:`. |
| `dependencies` | `blocked_by`, `blocking`, `starts_before`, `starts_after`, `finishes_before`, `finishes_after` → lists of item IDs (`APP-12` or UUID). |
| `relations` | `relates_to`, `duplicate`, `implements`, `implemented_by`, or configured workspace relation names → ID lists (`APP-12` or UUID). |
| `properties` | Existing type custom property keys and values, respecting types/requiredness. |

**Not supported on push in v0.5.2:** `watchers`, `children`, `links` (external Links widget), comments, worklogs, linked pages, `estimate_point` values, `release_picker` custom properties, `formula` push, and Oct 2026 Enterprise fields (`cascading` properties, work-item `audiences`). Older flat relation keys (`blocked_by`, `blocking`, `relates_to`, `duplicate_of`) may be read, but write normalized `dependencies:` and `relations:` maps.

## Native Git Development linking vs `description:`

Do **not** dump commit logs, branch names, or PR URLs into YAML `description:` to simulate Git tracking:
- Plane's native **Development** section (Pro+ with GitHub, GitLab, or Bitbucket integration connected) automatically attaches branches, commits, pull/merge requests, and CI statuses whenever uppercase canonical work item IDs (`PROJ-123`) appear in:
  1. **Branch names:** `feat/PROJ-123-short-slug` or `<user>/PROJ-123` (*lowercase `proj-123` does NOT link*).
  2. **Commit messages:** e.g. Git trailer `Refs: PROJ-123` or `Fixes: PROJ-123`.
  3. **Pull/Merge request titles or descriptions:** `[PROJ-123]` with brackets links the PR/branch in **Development**, adds a backlink comment, **and triggers PR state automation**; `PROJ-123` without brackets links as reference-only without automatic state transition.
- **External links & pages outside Git:** External URLs (docs, specs, runbooks) belong in Plane's **Links** widget (`POST /work-items/{id}/links/` REST API / MCP) or **Linked pages**, not cluttering the work item description, and `plane-compose` currently leaves `links` unmanaged rather than overwriting them.

## Work item quality bar

- **One completable outcome:** clear “done when”, not unfinishable areas like `"Make webapp secure"` or `"Improve testing"`; track ongoing areas via `type:` + `labels:` + bounded deliverables. Use 1 provider or vertical capability per item (never lump `"Stripe/PayPal"` into one ticket or split one entity into 8 horizontal `Model/API/UI/Test/Docs` tickets).
- **Actionable scope & client surfaces:** enough context to implement without narrating every code line; use Surface `labels:`, `Epics`, `dependencies:`, `relations:`, and `milestones.yaml` instead of duplicating tickets across codebase sub-projects or hiding structure in prose. Keep the hierarchy `Epic` (never has a `parent:`) → Work Item → optional Sub-item (never has children): the longest `parent:` chain is 2 and non-Epic nesting is at most 1. Use sub-items only for genuinely separate PRs or heavy standalone test campaigns; standard unit tests, migrations, and docs live in the Acceptance Criteria DoD.
- **Must-have `priority` vs. time urgency:** set `priority` by functional criticality / legal-regulatory must-have level (`urgent` = mandatory law/security/core blocker, `high` = core/killer feature, `low` = optional nice-to-have), and express time urgency via target Version `Module` + `due_date`. Never assign humans (`assignees:`) to unstarted `Candidate`/`Backlog` items.
- **Traceability:** bind commits/branches/PRs with canonical uppercase IDs rather than manual URL copy-paste. Never claim a PR, CI run, or remote sync occurred before observing it.
- **No ticket explosion:** don’t create separate work items for every tiny edit, test name or intermediate thought. Break down only when separate deliverables matter.
- **No automatic closure:** transitioning to `Done` requires the acceptance criteria to be verified; otherwise state the remaining checks.

## ISLAMU Event (`EVENT`) specifics

- Types are `Epic`, `Feature` (default; `event/plane.yaml` `defaults.type: Feature`), `Bug`, `Security`, `Refactor`, `Test`, `Docs`, `CI/CD`, `Config`, `Chore`. `Task`, `Story`, and `Improvement` do not exist in `EVENT`.
- Epics are pushed first (`type: Epic`, `id:` omitted). Children reference the Epic's **UUID** in `parent:` after it is minted.
- Edit existing `EVENT-XXX` items in `work/workitems.yaml`; put only new items in `work/active-*.yaml`. One ID, one file.
- Version Module membership (`v0.1.0`, `v1.0.0`) is not pushed from YAML in `v0.5.2`; assign it through Plane MCP `module` `manage_workitems`.
- Sizing rules: [work-item taxonomy standard](../../../../docs/internal/project-management/work-item-taxonomy-and-granularity-report.md).

### Reusable provider / vertical onboarding checklist

Plane work-item templates are tier-gated on this workspace, so `schema/workitem_templates.yaml` stays empty and this checklist is pasted into the Acceptance Criteria of every new payment, identity, or database provider item instead of creating extra tickets:

- Credentials resolve only from an approved secret authority (SecretKit/Infisical); none in source, fixtures, or appsettings.
- Inbound webhooks verify signatures and are idempotent; outbound calls have bounded retries.
- Sandbox-backed Ring 2 test and Ring 3 provider-matrix coverage exist for the provider.
- Financial, privacy, or audit trails cover the provider's state changes.
- Public operator guide (`docs/public/`) and internal architecture anchor (`docs/internal/`) are updated in the same PR.

## Validate

```bash
plane validate --offline   # parser, IDs and basic structure, without remote enum checks
plane validate             # also check types, states, labels and custom properties remotely
plane push --dry-run
```

Use `plane schema validate` if you edited schema. If missing/unsupported fields or labels fail, fix YAML first rather than sending partial updates.

**Official:** https://developers.plane.so/dev-tools/plane-compose/project-files · https://developers.plane.so/dev-tools/plane-compose/sync-projects
