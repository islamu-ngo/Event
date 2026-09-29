---
name: plane-mcp
description: "Load when the user asks to create, find, update, assign, label, prioritize, transition, or organize ISLAMU Event issues/cycles/modules in Plane via MCP; not for local todo lists or implementation-plan markdown."
type: pattern
enforcement: suggest
priority: high
---
<!-- ABOUTME: Safe Plane MCP workflow for ISLAMU Event work-item operations. -->
<!-- ABOUTME: Keeps stable IDs in one resource and prevents duplicates or unverified mutations. -->

# Plane MCP

## Required project data

Read [ISLAMU Event project IDs](resources/islamu-event-project.md) before the first Plane call in a turn. Use the resource's current project, workspace, state, label, and cycle UUIDs; never copy those UUIDs into this router.

## Rules

- Search before create; update an existing matching item instead of duplicating it.
- Resolve `EVENT-NNN` to its work-item UUID before tools that require UUIDs.
- Derive state from evidence: do not mark work Done merely because a description says it exists.
- Assign at least one relevant label, and use Plane's accepted priority values.
- Create first, then assign the returned UUID to a cycle because creation does not accept a cycle.
- Use concise HTML descriptions with acceptance context and stable repository paths; never paste secrets or large source blocks.
- Confirm ambiguous target, destructive deletion, or bulk mutation scope before changing external state.
- Read back mutated items and report their identifiers.

## Workflows

### Create

1. Search by the strongest title keywords.
2. If a match exists, retrieve and compare it.
3. Otherwise create with project UUID, title, state UUID, label UUIDs, priority, and `description_html`.
4. Add the returned item UUID to the chosen cycle when requested.
5. Retrieve it and return `EVENT-NNN`, title, state, labels, priority, and cycle.

### Update

1. Retrieve by `EVENT-NNN` or UUID.
2. Apply only fields the user requested.
3. Retrieve again and report the resulting state.

### Bulk work

Search all candidates first, deduplicate the requested set, then use bounded parallel independent calls. Batch cycle assignment after creation. Return per-item successes and failures; never hide partial completion.

## Tool map

| Need | Tool shape |
|---|---|
| Search/list | `plane_search_work_items`, `plane_list_work_items` |
| Resolve EVENT number | `plane_retrieve_work_item_by_identifier` |
| Create/update/read | `plane_create_work_item`, `plane_update_work_item`, `plane_retrieve_work_item` |
| Cycle membership | `plane_add_work_items_to_cycle`, remove/transfer cycle tools |
| Labels/states/cycles | corresponding list tools; use creation tools only when explicitly requested |
| Comments/links/relations/logs | corresponding work-item tools |
| Modules/epics/milestones | corresponding create/list/membership tools |

## Verification

- Every requested mutation has a read-back result.
- New items have no known duplicate, at least one label, the intended state/priority, and requested cycle membership.
- Bulk output identifies partial failures and the exact items safe to retry.
