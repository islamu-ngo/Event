# Compatibility, authoritative references and evaluation scenarios

**Read when:** checking version-sensitive commands, refreshing the skill, auditing quality, or testing whether the skill triggers and works correctly.

## Known version contract

This skill was authored against **Plane Compose v0.5.2** documentation (latest PyPI release verified in November 2026). Run `plane --version`, then the specific `plane <command> --help`; current installed behavior wins over a stale assumption. Beware v0.5.2 flags intentionally documented but not always shown in `--help` (`push --json`, `--resume`, `--all`; `ws --skip` sections) and old misleading `schema sync --help` text about deletion. Check official docs for changes instead of inferring behavior from package age.

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
| Work item has no `id` | Agent adds stable ID before push, not hash-only identity. |
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
