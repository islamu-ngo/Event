# Templates and upgrades without surprising overwrites

**Read when:** standardizing multiple project schemas, applying shared templates, adopting an organization baseline, or creating reusable workspaces.

## Init is different from upgrade

- `plane init KEY -w WS -t <template>` scaffolds a **new** local project; it does not change Plane remotely until pushed.
- `plane upgrade -t <template>` **modifies an existing project**. It normally pulls remote schema first (Plane wins locally), applies template-defined values, and offers to push.
- `plane ws template <name> [path]` exports a reusable **workspace** template with IDs and secrets removed.
- `plane ws upgrade` applies a shared template to a cloned workspace.

Templates can be local paths or Git repository paths, optionally pinned with `@tag`. A Git URL must include the path within the repository. Pin trusted commits/tags for deterministic change review; audit external templates as untrusted input.

## Read-only preview that is *actually* read-only

```bash
plane upgrade -t ./templates/engineering --dry-run --skip-pull
plane ws upgrade myteam -t ./templates/workspace --dry-run --skip-pull
```

**Do not omit `--skip-pull`:** in v0.5.2, `plane upgrade --dry-run` and `plane ws upgrade --dry-run` still pull and overwrite local YAML before they show a preview. Take a Git checkpoint/backup before a non-preview upgrade.

## Project upgrade flow

1. Check intended project, current config, Git status, source version/tag and who authorized a schema change.
2. Preview with `--dry-run --skip-pull`; inspect difference and what the template wins over.
3. Decide whether remote schema must be reconciled first, as a deliberate separately-reviewed pull.
4. Apply the upgrade only after approval. `--schema-only` updates local YAML but prevents the subsequent remote push.
5. Validate schema, compare remote, dry-run push and publish with normal checks if authorized.

`--include-data` copies cycles, modules, work items and pages from the template; if a push is accepted it uses force with **no conflict check**. Treat this as high risk; never use automatically. `--force` also skips prompts and may push.

## Workspace upgrades

The same `--dry-run --skip-pull` rule applies. Workspace upgrades merge supported workspace types/properties, feature flags, labels, relations, templates, releases and webhooks, while keeping local-only items. Do not assume member/type-hierarchy writes work: `ws push` does not push those sections in v0.5.2.

The built-in `template: default` recorded by `plane init` **isn't** a valid upgrade source; the first upgrade needs `-t <real-template>`. An upgrade records the selected source in `plane.yaml` so later runs can reuse it.

**Official:** https://developers.plane.so/dev-tools/plane-compose/schema-and-templates · https://developers.plane.so/dev-tools/plane-compose/workspace
