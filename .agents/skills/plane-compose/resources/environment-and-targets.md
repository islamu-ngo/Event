# Environment, authentication and exact project targeting

**Read when:** the CLI is missing; access fails; there are multiple connections, workspaces, servers or local Plane folders; using Plane Cloud versus self-hosted Plane.

## Locate and classify

- At repo root, search for `plane.yaml` without assuming it is located at the top level. Include project folders in `projects/` or service subfolders; exclude generated/vendor directories. Inspect each file, not just its filename.
- `type: project` (or the existing project layout) is for `plane push`, `pull`, `diff`, `validate`, `schema`. `type: workspace` is for `plane ws ...`; never treat one as the other.
- Choose the exact matching project based on affected code, workspace, project key and user context. If ambiguous, operate read-only and explain the ambiguity.
- Check `git status --short` before using a command that writes local YAML.

## CLI and connection checks

```bash
plane --version
plane auth list-connections
plane status
plane validate --offline
```

Run commands from the **chosen project directory**, unless passing a supported explicit project path. `plane status` and `validate --offline` do not authenticate; `plane diff` does.

Plane Compose requires Python 3.10+. Recommended installation: `pipx install plane-compose`; verify `plane --version`. The skill is based on **v0.5.2**, so check help and the official docs if using a newer/older build.

## Authentication

```bash
# Illustrative only; source a real token securely from the user/secret store.
plane auth login --server-url https://api.plane.so --auth-type pat \
  --token "$PLANE_TOKEN" --workspace myteam -c main

# Additional workspace on the same saved connection
plane auth connect-workspace otherteam -c main
```

Other auth commands: `plane auth list-connections`, `plane auth disconnect-workspace <slug> [-c <connection>]`, `plane auth logout <connection>`. These change local auth configuration; ask before deleting links or connections.

Non-interactive login falls back to `PLANE_SERVER_URL`, `PLANE_AUTH_TYPE`, `PLANE_AUTH_TOKEN`, `PLANE_WORKSPACE`, and `PLANE_CONNECTION`. All five values (or flags) are needed to avoid prompts. The login check calls `GET /api/v1/workspaces/<slug>/projects/` using `X-Api-Key`; unsuccessful validation doesn't save the connection.

The file `~/.config/plane-compose/config.json` holds saved credentials, protected with `0600` permissions. Do not print it or check it into git. `PLANE_CONFIG_DIR` changes the configuration location.

## Credential and API URL resolution (critical)

For **project** commands, Plane Compose picks the connection in this order:

1. A valid `connection:` in that project's `plane.yaml`.
2. Connection linked to its `workspace:` slug.
3. Default workspace's connection.
4. First saved connection.

In v0.5.2, `-c` / `--connection` on **`plane push` and `plane pull` does not select credentials**. It only changes the rate-counter label. Set the intended `connection:` in `plane.yaml` instead. `-c` on `init`, `clone`, `ws clone` *does* select a connection.

For self-hosted Plane, log in against the exact HTTPS instance URL and set `connection:` accordingly. Non-default `api_url:` in `plane.yaml` overrides the server stored in the connection. Confirm hostname and workspace **before** every remote write. `PLANE_API_URL` is ignored in v0.5.2; do not rely on it or on `.env` auto-loading.

## Scope and auth failure

If the CLI, credentials or remote connection are unavailable: continue code and local YAML work when appropriate; never claim remote updates succeeded. Report the missing prerequisite, without requesting token disclosure in the chat or leaking authentication values in logs.

**Official:** https://developers.plane.so/dev-tools/plane-compose/authentication · https://developers.plane.so/dev-tools/plane-compose/reference
