---
name: plane-mcp
description: "Load when operating ISLAMU Event (`EVENT`) work items directly through the Plane MCP server: PQL queries, retrieving EVENT-NNN, Version Module membership (v0.1.0/v1.0.0), comments, links, attachments, Intake triage, epics/parent nesting, or deletions. Do not use for YAML authoring, ID minting, or `plane push`/`pull` (use plane-compose)."
type: pattern
enforcement: suggest
priority: high
---

# Plane MCP

## Required project data

Read [ISLAMU Event project IDs](resources/islamu-event-project.md) before the first mutating call in a turn. It holds the project, state, label, type, and Version Module UUIDs; never copy them into this router. Sizing and hierarchy rules come from the [work-item taxonomy standard](../../../docs/internal/project-management/work-item-taxonomy-and-granularity-report.md).

## Boundary with plane-compose

- `plane-compose` owns YAML truth: fields, schema, and `EVENT-XXX` minting through `id:` write-back. MCP owns what YAML cannot express: Version Module membership, comments, links, attachments, activity, Intake, PQL, deletions.
- Never create an item through MCP and then hand-write its `id:` into `event/work/*.yaml`. `plane push` tracks items through `.plane/state.json` and would create a duplicate.
- Ordering: finish `plane push`, then MCP mutations, then `plane pull` so YAML reflects remote changes. Do not leave unpushed local YAML edits for items you mutate through MCP.
- `EVENT` uses **Version Modules**, not Cycles. Release scope lives in modules `v0.1.0` and `v1.0.0`; there are no Event cycles.

## Rules

- Search or PQL-list before create; update a matching item instead of duplicating it.
- Resolve `EVENT-NNN` with `workitem` `retrieve_by_identifier` before any call that needs a UUID.
- Request sparse `fields` (for example `id,name,state,updated_at`) on reads; full payloads waste context.
- Derive state from evidence (merged PR, CI run, ADR), never from description text alone.
- An Epic is a work item whose `type_id` is the `Epic` type. An Epic never gets a `parent`; a Sub-item (child of a Work Item) never gets children.
- Unstarted items (`Candidate`, `Backlog`, `Waiting Dependency`, `Todo`) carry `assignees=[]`. `priority` encodes functional criticality, not deadline.
- Deletion, bulk mutation, or ambiguous targets need explicit maintainer confirmation of the exact ID set first. Never delete an ID cited in Git history, `docs/`, `.agents/`, or an ADR; mark it `Replaced` instead.
- Respect Plane Cloud's limit of about 60 requests per minute. Run bulk work in bounded sequential batches, checkpoint completed IDs, and resume from the checkpoint after HTTP 429.

## Tool map (live resource-action server)

| Need | Tool and action |
|---|---|
| Find / filter | `workitem` `search` (`query`) or `list` (`project_id`, `pql`, `per_page`, `cursor`, `fields`); read `get_pql_reference` before composing PQL |
| Count / audit | `workitem` `count` with `group_by` (`state_id`, `type_id`, `issue_module__module_id`, `assignees__id`) |
| Read one | `workitem` `retrieve_by_identifier` (`EVENT-NNN`) or `retrieve` |
| Create / edit / nest | `workitem` `create` / `update` (`parent`, `type_id`, `state`, `priority`, `assignees`, `labels`) |
| Labels / assignees delta | `workitem` `manage_label` / `manage_assignee` (merge, removals first) |
| Delete / archive | `workitem` `delete` (irreversible) / `archive` (completed or cancelled only) |
| Version Module membership | `module` `manage_workitems` (`add_ids`, `remove_ids`), read back with `list_workitems` |
| Epic type / children | `workitem_type` `resolve` (`name="Epic"`); children via PQL `childOf("EVENT-NNN")` |
| Comments / links / files | `workitem_comment`, `workitem_link`, `workitem_attachment` |
| History / relations | `workitem_activity`, `workitem_relation` |
| Community intake | `intake` |
| Schema reads | `state`, `label`, `workitem_type`, `module`, `milestone` |

## Workflow

### Single item

1. Search or retrieve; compare any match.
2. Create or update only the requested fields.
3. For release scope, add the item to its Version Module.
4. Read back and report `EVENT-NNN`, title, state, labels, priority, and module.

### Bulk change

1. Build the exact ID set with paginated PQL and dedupe it. Record it in the task ledger before mutating.
2. Remove protected (Git/ADR/docs-cited) IDs from any delete set and confirm the remainder with the maintainer.
3. Execute in bounded batches, logging completed and remaining IDs after each batch.
4. Verify with `count` or `list_workitems` read-back, then run `plane pull` in `event/`.

## Verification

- Every mutation has a read-back; bulk runs report per-batch success, failure, and the exact IDs safe to retry.
- After MCP changes, `cd event && plane pull` followed by `plane validate --offline` shows no errors, and `plane push --dry-run` shows no unexpected diffs.
