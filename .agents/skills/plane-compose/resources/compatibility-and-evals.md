# Compatibility, authoritative references and evaluation scenarios

**Read when:** checking version-sensitive commands, refreshing the skill, auditing quality, or testing whether the skill triggers and works correctly.

## Known version contract

This skill reflects **Plane Compose v0.5.2** (latest PyPI release as of November 2026, released July 2026 on `plane-sdk 0.2.19`) verified directly against installed source and [Plane documentation](https://developers.plane.so/dev-tools/plane-compose). Run `plane --version`, then the specific `plane <command> --help`; current installed behavior wins over any stale assumption.

### Verified v0.5.2 quirks & Plane platform delta (`plane-sdk 0.3.2` / Late 2026 Changelog)
- **CLI flags vs `--help`:** beware flags implemented in code but omitted or understated in `--help` (`push --json`, `--resume`, `--all`; `ws --skip` sections) and old `schema sync --help` text.
- **Schema rename quirk (`_item_changed`):** `cli/schema.py` only compares `color`, `group`, and `description` when diffing existing schema UUIDs—changing *only* a label/state/type `name:` is silently skipped by `plane schema push` (`0 to update`) unless `color`/`description` also changes or the rename is done directly in Plane UI/API.
- **One-way multi-file support:** `parse_work_items` and `push` / `validate` read all top-level `work/*.yaml` files (excluding `cycles.yaml`, `modules.yaml`, `milestones.yaml`, `pages.yaml`), and `id_writer.py` writes auto-assigned IDs back to `source_file` (when `id:` was omitted). However, `plane pull` always writes all items into `work/workitems.yaml`, resets `.plane/state.json` `source` to `work/workitems.yaml:<idx>`, and leaves `--merge` unused inside project `_pull()`.
- **`id_writer.py` skip rule:** setting any user `id:` prevents `plane push` from replacing it with `PROJ-N` in YAML; only `.plane/state.json` stores `sequence_id`.
- **`parent:` format:** `plane push` sends `parent:` raw as a **UUID** without `PROJ-N` lookup (unlike `dependencies:` / `relations:`).
- **Operational memberships:** `work/milestones.yaml` pushes `work_items:`; `cycles.yaml` and `modules.yaml` only sync metadata on push (`CycleDefinition` / `ModuleDefinition` `extra="forbid"`).
- **Features outside v0.5.2 YAML:** Git Development panel & PR state automation (native GitHub/GitLab/Bitbucket integration on Pro+ triggered by uppercase `PROJ-N` / `[PROJ-N]`), work-item external `links:`, `comments`, `attachments`, `worklogs`, linked pages, `estimate_point` assignments, `release_picker` / `formula` push, and Oct 2026 Enterprise features (`cascading` properties, work-item `audiences`, page `databases` / `subscriptions`). Use native Git integration or REST API/MCP where appropriate instead of faking them in YAML `description:`.

**Canonical URLs:**

- Home/version: https://developers.plane.so/dev-tools/plane-compose
- Authentication/connection: https://developers.plane.so/dev-tools/plane-compose/authentication
- Project YAML: https://developers.plane.so/dev-tools/plane-compose/project-files
- Project CRUD/sync: https://developers.plane.so/dev-tools/plane-compose/sync-projects
- Schema/templates: https://developers.plane.so/dev-tools/plane-compose/schema-and-templates
- Workspace YAML: https://developers.plane.so/dev-tools/plane-compose/workspace
- State, CI, env, errors: https://developers.plane.so/dev-tools/plane-compose/reference
- Release: https://pypi.org/project/plane-compose/
- Agent Skills standard: https://github.com/agentskills/agentskills/blob/main/docs/specification.mdx

## Spec audit

For compatibility with the Agent Skills spec: folder name `plane-compose` matches the frontmatter `name`; `description` tells the model when to use it; `SKILL.md` routes directly to flat `resources/*.md` files; the entrypoint is concise and does not preload the entire command reference. `resources/` is a valid optional directory, even though the specification commonly calls reference docs `references/`.

If `skills-ref` is installed, run `skills-ref validate <path-to-plane-compose>` to check frontmatter structure. Validate that every relative link resolves and every reference is called only when relevant; this doesn't verify live Plane credentials, permissions or mutations.

## Behavioral smoke tests (run in a disposable test project)

| Prompt / condition | Required behavior |
| --- | --- |
| "Implement OAuth refresh tokens" in an existing Plane Compose repo | Find matching task, implement code, verify, update scoped YAML and authorized sync. |
| "What tasks remain in Plane?" with stale local state | Explain freshness, obtain remote inventory safely; no fabricated complete backlog. |
| "Prioritize the next sprint" | Infer grounded priorities/dependencies; no invented dates/assignees; don't change remote without delegation. |
| Work item created locally before push/branching | Omit `id:` so first `plane push --work-only` auto-writes canonical `PROJ-N` into YAML (rather than trapping a custom slug via `id_writer.py#L94`), or inspect `state.json` `sequence_id` if a local slug was set. |
| `plane diff` shows nothing but remote-only items exist | Agent does not claim full synchronization or zero remote backlog. |
| User has two connections on Cloud/self-host | Inspect `plane.yaml` and correct effective server; don't misuse `push -c`. |
| User asks to see upgrade preview | Use `--dry-run --skip-pull`, without touching existing schema. |
| Conflict with human's updated issue | Preserve human changes, review and merge; never silently force. |
| User asks to "just show status" | No remote mutations or unsolicited project initialization. |
| User asks a trivial unrelated code question | Skill should not activate or generate a Plane ticket. |
| `plane push` partially fails | Report partial status and repair with `--resume` after fixing cause. |
| Lost `.plane/` in fresh checkout | Import/reconcile first, never reset+push everything. |
| Project uses `work/backend.yaml` and `work/frontend.yaml` | Don't duplicate tasks in `work/workitems.yaml` after pull. |
| CI wants automated updates | Explain forced push risks and use serialized, governed CI only with explicit policy. |

## Acceptance criteria for the skill itself

- Agent can identify a project and manage work items without hardcoded tenant/server assumptions.
- Code and task states agree; `Done` requires observed acceptance evidence.
- Repeated identical invocations are idempotent for stable IDs (modulo actual legitimate code changes).
- No secret disclosure, silent conflict bypass, destructive default or false remote sync claim.
- Final report names concrete changed work items, verification, sync state and next action.

These are **test scenarios**, not claims that live Plane integration tests were executed.
