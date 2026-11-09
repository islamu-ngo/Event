# Work item authoring: correct YAML, stable identity, clear acceptance

**Read when:** creating, updating or linking tasks, bugs, features, chores or improvements in a Plane Compose project.

## First, match instead of duplicating

Look for an existing item by canonical Plane ID, title/intent, owning module, code references, and dependency graph. Search *all* `work/*.yaml` task files and (if relevant) remotely fetched items; `plane diff` alone misses remote-only items. Preserve the item's `id`, original metadata, and user-written description when updating. A title change is not justification for creating another task.

### Stable IDs (mandatory for the agent)

- Every new authored item needs a unique, stable `id`. Use an unambiguous **local tracking ID** such as `oauth-refresh-token` when no Plane sequence ID exists. First push may replace it in the YAML with `APP-37`; preserve the written-back ID on subsequent edits.
- For existing Plane items, keep the actual canonical `APP-37`, not a guessed next number. Do not invent `APP-99` based on sequence assumptions.
- Never use hash-only tracking (no `id`): editing a title can generate duplicates. IDs must be unique **across all** work-item YAML files in that project.
- Read `git diff` after push; Plane may write canonical IDs back into the task files.

## Recommended task format

The following illustrates the schema; use **only actual** states, labels, types, and people defined in the selected project. The example `APP-12` etc. represent IDs obtained from Plane, not guesses:

```yaml
workitems:
  - id: oauth-refresh-token
    title: Reject expired refresh tokens at API boundary
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
| `parent` | Work item ID (flatten child records; no nested `children:`). |
| `dependencies` | `blocked_by`, `blocking`, `starts_before`, `starts_after`, `finishes_before`, `finishes_after` → lists of item IDs. |
| `relations` | `relates_to`, `duplicate`, `implements`, `implemented_by`, or configured workspace relation names → ID lists. |
| `properties` | Existing type custom property keys and values, respecting types/requiredness. |

**Not supported on push:** `watchers` and `children`. Older flat forms (`blocked_by`, `blocking`, `relates_to`, `duplicate_of`) may be read, but write normalized `dependencies:` and `relations:` maps.

## Work item quality bar

- **One testable outcome:** clear “done when”, not generic “Improve backend”.
- **Actionable scope:** enough context to implement without narrating every code line.
- **Grounded priority:** user deadline, risk, dependency critical path, or real impact; avoid urgent by default.
- **Traceability:** document relevant code area, commit/PR/test evidence as appropriate (only if real). Never claim a URL exists before confirming it.
- **No ticket explosion:** don’t create separate work items for every tiny edit, test name or intermediate thought. Break down only when separate deliverables matter.
- **No automatic closure:** transitioning to `Done` requires the acceptance criteria to be verified; otherwise state the remaining checks.

## Validate

```bash
plane validate --offline   # parser, IDs and basic structure, without remote enum checks
plane validate             # also check types, states, labels and custom properties remotely
plane push --dry-run
```

Use `plane schema validate` if you edited schema. If missing/unsupported fields or labels fail, fix YAML first rather than sending partial updates.

**Official:** https://developers.plane.so/dev-tools/plane-compose/project-files · https://developers.plane.so/dev-tools/plane-compose/sync-projects
