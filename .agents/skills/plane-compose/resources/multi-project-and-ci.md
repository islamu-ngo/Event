# Multiple Plane projects, shared agents and CI pipelines

**Read when:** a monorepo contains several `plane.yaml` projects, a team has multiple agents working concurrently, or the user wants headless CI checks/publishing.

## Many projects in one repo

From a parent folder, Plane Compose finds projects by their `plane.yaml`. Read the matching configurations before doing anything; don't assume all use the same workspace, connection or Plane server.

```bash
plane status --all
plane status --all -w myteam
plane push --all -w myteam --dry-run
```

Use `.planeignore` in the execution directory to exclude archived/test projects (`archive/*`, etc.). Ensure intended scoping in a monorepo; `status --all`, `push --all`, and `pull --all` have different filter/flag support.

**Important:** `plane push --all` automatically runs individual pushes with **force**; it ignores certain flags including `--skip`, `--path`, `--no-conflict-check` and `--exit-code`. Therefore do not use `push --all` for everyday agent writes. Select one project and use normal conflict checks. `plane pull --all` modifies many local projects; avoid as a passive freshness check.

## Multi-agent coordination

- Agents may work on separate code/tasks and separate YAML files, but changes to `.plane/state.json` for a single project need **serialized** pushes; its lock is only within one process.
- Coordinate scope/ownership before editing the same item. Prefer a small work-item file per subsystem if repo policy supports it.
- Use Git diff/review to merge commits. A pull can write all remote items to `work/workitems.yaml`, so review duplicate IDs against shards.
- Do not have several agents simultaneously run `plane push`, `schema push`, `schema import`, or state repair on the same project without coordination.
- Never interpret a failed concurrent update as permission to `--force`.

## CI as checks first

Recommended read/validation jobs:

```bash
plane schema validate
plane validate --offline
# If CI has safe read-only Plane credentials:
plane validate
plane schema diff --exit-code
plane diff --exit-code
```

Treat exit `0` as in sync, `1` as an error and `2` as drift for the diff commands. A `2` must not be misread as an execution failure. `plane diff --json` is for work items only and misses remote-only items.

## Auth for headless CI

Use secret-managed environment variables or an explicitly named connection:

```bash
plane auth login --server-url https://api.plane.so --auth-type pat \
  --token "$PLANE_TOKEN" --workspace myteam -c ci
```

Do not embed the actual token in YAML, logs or scripts. Provide least-privilege access and select `connection: ci` in the intended `plane.yaml` when appropriate. The documented env fallbacks are `PLANE_SERVER_URL`, `PLANE_AUTH_TYPE`, `PLANE_AUTH_TOKEN`, `PLANE_WORKSPACE`, `PLANE_CONNECTION`. `PLANE_API_URL` does **not** work in v0.5.2.

## CI publishing requires extra governance

The Plane docs show `plane push --force --no-conflict-check --exit-code` for unattended CI. This deliberately removes conflict protection. Do **not** adopt that as a general default. If a user/team explicitly wants automated publishing, require protected credentials, a designated writer/branch, serialized execution per project, preview/approval gates and a rollback plan. Otherwise CI should lint/diff/report drift, not force changes into a shared Plane workspace.

## Rate control

`plane rate stats` inspects client throttling. `PLANE_RATE_LIMIT_PER_MINUTE` defaults to 50; retry environment variable in v0.5.2 is literally `PLANE_PLANE_API_RETRY_ATTEMPTS` (double `PLANE_`), not the more intuitive spelling.

**Official:** https://developers.plane.so/dev-tools/plane-compose/reference · https://developers.plane.so/dev-tools/plane-compose/sync-projects
