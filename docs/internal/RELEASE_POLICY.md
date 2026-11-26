# Release Policy

> **Audience:** Maintainers | Release operators | Contributors | AI agents
> **Status:** Implemented at source level; production signer enrollment and live release rehearsal remain gated on Project Steward approval
> **Owner:** Platform/Ops and Project Steward
> **Last Verified:** 2026-11-26
> **Source Anchors:** `eng/release/`, `.github/workflows/release.yml`, `.github/workflows/_container-build.yml`, `docs/internal/RELEASE_RUNBOOK.md`, `docs/internal/RELEASE_CHECKLIST.md`, `docs/internal/adr/ADR-025-provider-neutral-release-governance.md`

This policy defines the normative invariants and trust boundaries for ISLAMU Event releases. In a conflict, this policy governs release invariants; [ADR-025](adr/ADR-025-provider-neutral-release-governance.md) records the architectural rationale; [RELEASE_RUNBOOK.md](RELEASE_RUNBOOK.md) defines operator execution and recovery; and [RELEASE_CHECKLIST.md](RELEASE_CHECKLIST.md) tracks release-readiness gates.

## Complete release-impact metadata

License admission covers every tracked product and engineering lock profile,
including conditional deployment and file-based engineering-tool graphs.
A pass using a populated developer cache does not establish clean-runner restore coverage.
Missing package metadata remains a failure; profile servicing must not add audit exemptions.

Review complete changed-file metadata, including previous names for renames.
A large PR must not bypass impact classification or lower its declared file count.
See [the trusted metadata contract](CI_CD_GOVERNANCE.md#complete-pr-file-metadata).
A workflow correction on a PR branch does not replace the trusted-base version
used by `pull_request_target`; arrange an independently reviewed base update
before relying on its rerun.

## Scanner, runtime servicing, and advisory coverage boundaries

Scanner admission requires completed analysis for the selected candidate.
A job budget correction does not waive cancelled or missing query results;
the [CI execution budget](CI_CD_GOVERNANCE.md#required-repository-settings)
preserves the full configured suite.

SDK/runtime servicing evidence must cover the component actually delivered:
package resolution, framework-provided assemblies, and self-contained/container
runtime bytes have distinct ownership. Pruning an obsolete package reference
does not establish that a deployed runtime is patched. All four release Dockerfiles
(`src/Explore.API/Dockerfile`, `src/Explore.Blazor/Dockerfile`, `src/Event.Standalone/Dockerfile`,
and `src/Event.MigrationService/Dockerfile`) MUST remain aligned to the repository's
active `.NET 10` SDK and digest-pinned runtime baseline recorded in `global.json` and
[Operations](OPERATIONS.md#sdk-and-runtime-servicing).

Codecov publication is advisory shipped-product evidence, not release approval.
Reports are comparable only within their declared suite scope; incomplete
runtime lanes must not be presented as complete platform coverage. Its scope and ownership are defined in
[CI/CD Governance](CI_CD_GOVERNANCE.md#coverage-publication-policy).

## Governed release authority and `git-cliff` boundary

- **The signed annotated tag is the release.** `refs/tags/v<major>.<minor>.<patch>[-prerelease]`
  MUST be the sole immutable release identity. Tag verification (`verify-release-tag`)
  MUST enforce an annotated Git tag object (`cat-file -t == tag`), exact embedded `tag`
  header equality, valid OpenSSH signature against `eng/release/trust/allowed-signers`
  (`gpg.format=ssh`, `gpg.ssh.allowedSignersFile`), authorized signer principal, and exact
  target commit / GitHub event SHA binding. Verification MUST NOT depend on any branch ref
  under `refs/heads/*`, so every release remains verifiable after branches advance or are deleted.
- **Fail-closed signer root:** `eng/release/trust/allowed-signers` MUST remain comment-only
  (causing fail-closed `tag_allowed_signers_empty` rejection) until the Project Steward
  approves and enrolls production release signer public keys.
- **Version-line label:** every release MUST declare a version-line label `v<major>.<minor>`.
  The label classifies the release; it is not a ref and nothing may derive a branch name from it.
  `develop` MUST NOT receive generated `Unreleased` changelog writes.
- **C# owns release authority; `git-cliff` owns traversal and Tera rendering:**
  `ISLAMU.ReleaseEngineering` (`ReleaseContextPolicy`, `ReleaseInputPolicy`, `ReleasePreparation`,
  `ReleaseArtifactPolicy`, `ReleaseImageManifest`, `ReleaseArtifactManifest`) MUST remain the
  sole authority for commit validity, visibility, high-impact dispositions, SemVer selection,
  range/baseline bounds, embargo enforcement, and artifact sealing.
  - `release-context.v1.json` is the C# authority and evidence artifact.
  - `release-rendering-context.v1.json` is the separate, allowlisted, identity-free native
    context array passed to `git-cliff --from-context -` for three-persona Tera rendering
    (`Community Admin`, `Self-Hoster / Operator`, `Developer / API Integrator`).
  - `git-cliff --bumped-version` is comparison evidence only; disagreement with the selected
    stable version in `release.yaml` fails closed.
- **Verified offline toolchain:** `eng/release/toolchain.lock.json` pins `git-cliff` `2.14.2`
  across four platforms (`linux-x64`, `linux-arm64`, `osx-arm64`, `windows-x64`) by both archive
  SHA-256 and extracted executable SHA-256.
  - `ensure-git-cliff` is the ONLY network-capable provisioning command. `verify-tools`,
    `prepare`, `diff-contracts`, `seal-artifacts`, and tests MUST never download silently.
  - Every authoritative `git-cliff` execution MUST verify the binary digest and version and
    pass `--config`, `--offline`, and `--no-exec` via argument arrays (never a shell) under
    a scrubbed deterministic environment (`GIT_CLIFF_OFFLINE=1`).
- **Allowlisted privacy projection (never denylist scrubbing):**
  Stage 1 (`git-cliff --context`) raw output MUST remain transient in memory and MUST NEVER
  be persisted or logged. `GitCliffContextEnricher` MUST construct new native release/commit
  objects solely from an explicit scalar allowlist plus validated curated `extra` metadata,
  correlating visible and omitted OIDs with C# authority. Authors, committers, emails, raw
  bodies, footers, remote identities, repository paths, links, timestamps, statistics, and
  unknown future fields MUST never enter signed artifacts. `ReleaseArtifactPolicy` enforces
  a second post-render gate before writing `release-notes.md`.

## Commit governance, optional fragments, and embargo boundary

- Non-breaking internal commit types (`test`, `refactor`, `style`, `build`, `ci`, `chore`)
  and engineering scopes (`ci`, `dependencies`, `architecture`, `database`, `observability`,
  `documentation`, `release`, `testing`, `build`) omit automatically without requiring
  `Changelog: skip` trailers. Both `CommitPolicy` and `eng/release/cliff.toml` MUST remain
  parity-tested against the shared corpus (`require_conventional = true`,
  `protect_breaking_commits = true`, `fail_on_unmatched_commit = true`, no catch-all parser).
- Breaking changes MUST carry both `!` in the header and a non-empty `BREAKING CHANGE:` footer,
  MUST remain release-visible regardless of type or scope, and MUST NOT be skipped.
- Explicit non-breaking opt-out remains supported via `Changelog: skip` paired with a non-empty
  `Changelog-Reason:` trailer. Blank lines between terminal trailer paragraphs are permitted;
  duplicate or conflicting declarations fail closed.
- `.githooks/commit-msg` MUST remain a thin wrapper delegating to `preflight-commit`; range
  enforcement MUST use `preflight-range --target <ref> --head <ref>` over the bounded range.
- High-impact public fragments (`docs/internal/releases/changes/CHG-<26 Crockford Base32>.yaml`)
  are optional for routine changes and required only when referenced by high-impact release
  dispositions (`breaking`, `security`, `migration`, `configuration`, `openapi`, `operator`).
  Every staged or referenced fragment MUST pass full `ReleaseInputPolicy.Validate` at preflight.
- **Human-owned embargo authority:** structural markers (`Public-Disclosure: embargoed` or a
  `Restricted-Details` YAML key) MUST fail closed before public generation or sealing. Ordinary
  public prose containing the English word `"restricted"` is permitted.

## Conservative contract evidence limits

`diff-contracts <base-oid> <head-oid> <output-path>` reads exclusively from pinned Git objects
(never the dirty working tree) and emits deterministic `contract-diff.v1.json` across three
ordered families:

1. **OpenAPI & HAL (`schemas/openapi_islamu-event.json`):** syntax-aware structural diffing.
   Removed operations/links, required additions, and type/required/security narrowing classify
   as `breaking`; optional additions classify as `additive`; unresolved `$ref`, extension,
   default, or unrecognized structural changes classify as `review_required`. Automated JSON
   diffing does NOT prove runtime or semantic compatibility.
2. **Configuration contracts (`.env.example`, configuration manifest schema, tenant package schema):**
   reports variable/property additions, removals, and structural changes while redacting raw
   secret and default values from `.env.example`.
3. **EF Core migration ledger:** dynamically discovers `*ModelSnapshot.cs` and migration files
   at both pinned revisions across persistence assemblies (excluding `.Designer.cs` and snapshots
   from migration-ID enumeration). Asymmetric provider coverage classifies as `review_required`
   evidence, NOT proof of defect or provider parity. Real provider apply/idempotency tests and
   EF Core pending-model checks remain the semantic authority.

## Source-bound artifact staging, signing, and publication

1. **Per-release human inputs:** `docs/internal/releases/<version>/release.yaml` and `summary.md`
   are the sole human-owned per-release inputs committed before tagging. First-release baseline
   descriptors (`changelog-baseline-YYYY-MM-DD`) verify directly through `GitTagSignatureVerifier`
   and fail closed once any stable SemVer tag is reachable.
2. **Pre-build verification and non-clobber guard:** before any container image builds,
   `.github/workflows/release.yml` MUST run under the protected `release` environment and
   per-tag non-cancelling concurrency (`cancel-in-progress: false`), verify the signed annotated
   tag via `verify-release-tag --event-sha`, and query GitHub Releases explicitly (HTTP `404`
   = absent; HTTP `200` = verify existing sealed release cryptographically as an idempotent
   no-op or fail closed; any other HTTP status = fail closed). Existing published releases MUST
   NEVER be replaced or overwritten.
3. **Digest-only container staging before note rendering:** four multi-arch `.NET 10` OCI indexes
   (`islamu-event-api`, `islamu-event-ui`, `islamu-event-standalone`, `islamu-event-migration-service`)
   MUST be built, Trivy-scanned, SBOM/SLSA-attested, and staged via `_container-build.yml` in
   `push-mode: digest-only` (`push=true,push-by-digest=true,name-canonical=true`), assigning ZERO
   mutable or version registry tags. GHCR (`ghcr.io/<owner>`) is the built-in primary registry
   (authenticated with `GITHUB_TOKEN`); an optional secondary OCI registry mirror (for example
   ATCR, Quay, or another OCI-compliant registry) is configured strictly through GitHub Variables
   (`vars.CONTAINER_REGISTRY`, `vars.CONTAINER_REGISTRY_USER`) and Secret
   (`secrets.CONTAINER_REGISTRY_PASSWORD`) with zero hardcoded personal handles or forced third-party
   hosts in workflow YAML. Only after all four digests and all canonical `registry/repository@sha256:<digest>`
   references (4 in GHCR-only mode; 8 when a secondary registry is configured) are collected in
   `release-images.v1.json` may `prepare --image-manifest` render notes, so release notes never
   advertise unbuilt or aspirational image references.
4. **Sealing and cryptographic signing:**
   - `seal-artifacts` validates the exact five generated artifacts (`contract-diff.v1.json`,
     `release-context.v1.json`, `release-images.v1.json`, `release-notes.md`,
     `release-rendering-context.v1.json`) with zero missing/extra files, zero symlinks or
     subdirectories, strict 4-image symmetric registry coverage (each image covering the exact
     same configured registry set with canonical tagless `@sha256:` references), and post-render
     note privacy checks, writing canonical `release-artifact-manifest.v1.json` and `SHA256SUMS`
     (sorted ordinal relative paths, GNU `sha256sum --check` format) without clobbering.
   - `actions/attest` generates GitHub SLSA Build Provenance attestations for all seven sealed
     file artifacts (and `_container-build.yml` attests GHCR OCI images).
   - Cosign (`sigstore/cosign-installer` pinned by full SHA, Cosign `v3.1.5`) signs and verifies
     **every** published canonical `registry/repository@sha256:<digest>` reference across every
     configured registry individually (a GHCR signature does not authenticate a mirror reference)
     and signs `SHA256SUMS` with `cosign sign-blob --yes --bundle SHA256SUMS.sigstore.json`.
5. **Verified draft publish-once & non-atomicity honesty:**
   - Exact eight release assets (six files covered by `SHA256SUMS`, `SHA256SUMS`, and
     `SHA256SUMS.sigstore.json`) upload to a draft GitHub Release without `--clobber`, are
     re-downloaded and verified byte-for-byte against `SHA256SUMS`, and are published once
     (`gh release edit --draft=false`).
   - GitHub and two container registries do NOT commit atomically. Interrupted runs may leave
     unadvertised digest-only registry objects or an unpublished draft release; reruns verify
     exact matching state or fail closed without clobbering.
   - A published release is immutable. Corrections require a new version release and advisory,
     never asset replacement or tag re-pointing.
   - SBOM and SLSA provenance establish build origin and contents, NOT byte-for-byte container
     reproducibility (no reproducible-container claim is made without a two-build equality check).
6. **Offline vs. online verification boundary:**
   - `git verify-tag`, `verify-release-tag`, and `sha256sum --check SHA256SUMS` verify tag
     signatures and file integrity **offline**.
   - `cosign verify`, `cosign verify-blob`, and `gh attestation verify` verify OIDC signer
     identity, Fulcio/Rekor transparency proofs, and GitHub SLSA provenance and normally
     require network access to trust roots / registries / GitHub APIs.
7. **Model 2 Hybrid Split contributor recognition (`Huquq al-'Ibad`):**
   - Authoritative signed artifacts remain 100% offline and identity-free.
   - After sealed publication completes, `append-contributors` executes a separate online
     `--no-exec` `git-cliff` invocation (`eng/release/cliff-github-contributors.toml`, verified
     v2.14.2 binary, best-effort `continue-on-error: true`, guarded missing-remote fallback,
     strict raw-email rejection) to append `@handle` and PR links exclusively to the **mutable
     GitHub Release page body**.
   - Network or API failure omits contributor credit without weakening any security gate.
     Contributors may request edits or removal at any time without breaking cryptographic
     verification of `release-notes.md` or `SHA256SUMS`.

## Ref namespace

- Version tags own the `v*` glob outright. Creating any branch matching `refs/heads/v*`
  MUST be rejected by provider branch/tag rulesets so no branch can shadow a version tag.
- Maintenance branches MUST be named `release/<major>.<minor>`, opened lazily from a verified
  signed stable tag only when a real backport is required, and may be deleted freely once
  the line finishes because every release remains verifiable from its tag object alone.

## Public changelog projection

`docs/public/changelog/README.md` MUST remain an entries-only chronological release ledger
managed by `sync-public-changelog` (`PublicChangelogPolicy`, sentinel markers
`<!-- BEGIN GENERATED RELEASES -->` / `<!-- END GENERATED RELEASES -->`, 1 MiB UTF-8 budget,
optional YAML frontmatter only outside the markers). General adopter upgrade and verification
guidance lives separately in
[Releases and Changelog](../public/documentation/readme/configuration-and-operations/releases-and-changelog.md).
`sync-public-changelog` cryptographically binds every included release directory and linked
fragment to the exact signed annotated tag tree in Git and fails closed on post-tag edits or
accepted-page drift.
