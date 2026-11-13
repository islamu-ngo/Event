# Permission boundaries, secrets and irreversible operations

**Read when:** any external Plane write is contemplated, particularly writes involving the workspace, schema, token storage, conflicts, bulk operations or state repairs.

## Permission model

The skill is designed for an agent that can be delegated engineering/project-management work, but **having a CLI is not blanket permission** to change a shared remote service.

| Risk level | Examples | Default action |
| --- | --- | --- |
| Read/inspect | `plane status`, `plane diff`, `plane schema diff`, `plane ws diff`, `plane validate --offline`, `plane rate stats` | Allowed when tools are available; protect secrets. |
| Local edits | Create/update scoped `work/*.yaml`, amend descriptions, record proven progress, run `schema validate`, local `validate` | Allowed as part of authorized coding/PM task. Preserve unrelated edits. |
| Ordinary remote work sync | `plane push --work-only` to the **confirmed** project with normal conflict checks | Allowed when user has **explicitly delegated ongoing Plane task management**; otherwise seek approval before publishing. |
| Structural or shared remote change | `plane schema push`, `plane ws push`, project create, change feature flags/workflows | Requires specific approval and preview, even if work-item sync is delegated. |
| High risk | `schema sync`, `--prune`, `state reset`, `ws state reset`, `--no-conflict-check`, `push --json`, `push --all`, write `--force`, template `--include-data` | Stop by default. Requires targeted, informed authorization and rollback/reconciliation plan. |

Do not turn a read-only request (“show status”) into a remote mutation. If the user asks to implement a feature, routine YAML edits and permitted additive task sync can accompany code; unrelated schema/workspace refactors cannot.

## Preflight and review discipline

- Confirm `plane.yaml` and effective **server**. Self-hosted `api_url:` can override saved connection URL; `-c` on `push/pull` does not select credentials.
- Inspect Git status and back up any local data **before** a pull/import-with-force/upgrade.
- Run `plane schema validate` and `plane validate` before push (because a normal push can update schema before work validation fails).
- Use dry-run and inspect creates/updates; beware flags hidden from `--help` that silently force.
- Never override a human's changed work item without resolving conflict.
- Avoid bulk `--all` and concurrent pushes on a shared project.

## Secrets and sensitive data

`~/.config/plane-compose/config.json` stores API tokens (`0600` permissions); workspace `.plane/secrets.json` stores webhook secrets; `.plane/` stores state. Never commit or print them. `plane --debug` writes logs and may contain sensitive request details. Redact before sharing. Do not paste PAT values into work item descriptions, YAML, code examples or user-facing answers.

Use HTTPS for non-local Plane servers. Plain `http://` to a nonlocal host can transmit credentials in clear text. In CI use scoped secrets and least-privilege service tokens.

## Source of truth and recoverability

Version-control *intent* (`plane.yaml`, `schema/`, `work/`), keep runtime sync metadata out of git. Plane can also be edited by humans; treat it as shared state requiring reconciliation, not as a disposable mirror. “Additive push” is **not** synonym for no risk: updates can overwrite fields. Remote-only work items might not show up in `diff`.

**Official:** https://developers.plane.so/dev-tools/plane-compose/authentication · https://developers.plane.so/dev-tools/plane-compose/schema-and-templates · https://developers.plane.so/dev-tools/plane-compose/workspace
