# Governed Releases

> **Audience:** Maintainers and release engineers
> **Status:** Authoritative input contract
> **Source Anchors:** `eng/release/src/ISLAMU.ReleaseEngineering/`, `eng/release/cliff.toml`, `eng/release/policy/`

This directory holds human-owned release inputs (`docs/internal/releases/<version>/release.yaml` and `summary.md`) and optional high-impact public change fragments (`docs/internal/releases/changes/CHG-*.yaml`). Generated artifacts (`release-context.v1.json`, `release-notes.md`) are written by `prepare` and must not be edited by hand.

No versioned release directory is authoritative until the Project Steward approves that version and the release operator has exact full Git object IDs. Do not add placeholder `docs/internal/releases/<version>` directories for unapproved releases.

## `release.yaml`

`release.yaml` fixes the release identity and operator dispositions before context generation. Unknown keys fail closed.

```yaml
Version: 1.1.0
Line: v1.1
Release-Date: 2026-08-14
Base-Stable-Tag: v1.0.0
Previous-Published-Tag: v1.0.0
Release-Range:
  Base-Ref: v1.0.0
  Base-Oid: aaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaa
  Previous-Ref: v1.0.0
  Previous-Oid: bbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbb
Compatibility:
  - v1
Impact-Dispositions:
  breaking: not-applicable
  security: coordinated
  migration: planned
  configuration: not-applicable
  openapi: documented
  operator: documented
```

Rules:

- `Version` is SemVer without build metadata.
- `Line` is the version-line **label** `v<major>.<minor>` and must match `Version`. It is a classification, not a Git ref; maintenance branches use `release/<major>.<minor>` and `refs/heads/v*` is reserved for version tags.
- `Release-Date` is fixed `yyyy-MM-dd`; deterministic output never uses the current clock.
- `Base-Stable-Tag` and `Previous-Published-Tag` are explicit `v<major>.<minor>.<patch>` tags.
- `Release-Range` repeats those exact refs and fixes their full lowercase SHA-1 or SHA-256 object IDs.
- For the first governed release only (when no governed stable SemVer tag is reachable), both lower-bound fields may name a signed annotated `changelog-baseline-YYYY-MM-DD` tag verified directly through `GitTagSignatureVerifier` (`verify-release-tag`) against `eng/release/trust/allowed-signers`.
- `Impact-Dispositions` must explicitly cover `breaking`, `security`, `migration`, `configuration`, `openapi`, and `operator`.

## Commit Visibility & Optional High-Impact Fragments

Commit governance is shared between C# (`CommitPolicy` + `eng/release/policy/`) and native `git-cliff` (`eng/release/cliff.toml`):

- Public-scope `feat`, `fix`, `perf`, `revert`, and `docs` commits are release-visible by default.
- Non-breaking internal types (`test`, `refactor`, `style`, `build`, `ci`, `chore`) and engineering scopes (`ci`, `dependencies`, `architecture`, `database`, `observability`, `documentation`, `release`, `testing`, `build`) are omitted automatically without requiring `Changelog: skip` trailers.
- Explicit opt-out (`Changelog: skip` + `Changelog-Reason: <reason>`) remains available for non-breaking public-scope commits that should not appear in release notes.
- Breaking changes require both `!` in the header and a non-empty `BREAKING CHANGE:` footer, remain release-visible regardless of type/scope, and cannot be skipped.
- Historical pre-release fragments and `Change-Id` rename records are retired. Optional high-impact fragments under `docs/internal/releases/changes/CHG-*.yaml` are used only when structured multi-category impact metadata or grouping is needed, and are validated through full `ReleaseInputPolicy` during `preflight-commit`, `preflight-staged`, `preflight-range`, and `prepare`.
- Ordinary prose containing `"restricted"` is allowed in optional fragments; structural embargo controls (`Public-Disclosure: embargoed` or any `Restricted-Details` YAML key) fail closed.

## `release-context.v1.json` & `prepare`

Generate the normalized release context and release notes with:

```bash
dotnet run --project eng/release/src/ISLAMU.ReleaseEngineering/ISLAMU.ReleaseEngineering.csproj --configuration Release -- \
  prepare docs/internal/releases/<version>
```

C# (`ReleaseContextPolicy`) remains release authority for selected version, baseline verification, bounded Git range, and fragment linking. `git-cliff --bumped-version` runs as offline comparison evidence; any disagreement fails preparation rather than silently choosing a version. Authoritative context excludes authors, emails, forge handles/URLs, raw commit bodies, current-clock values, and ambient-locale formatting.
