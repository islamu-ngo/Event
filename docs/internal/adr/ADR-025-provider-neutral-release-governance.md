# ADR-025: Provider-Neutral Release Governance

> **Status:** Accepted and implemented at source level (Revision 6); production SSH signer enrollment and live release-candidate rehearsal remain gated on Project Steward approval
> **Date:** 2026-08-13 (Updated 2026-11-26)
> **Decision owners:** Platform/Ops and Project Steward

## Context

Earlier pre-release iterations accumulated ~28,300 lines of custom release infrastructure inside `ISLAMU.ReleaseEngineering` (user-space `/proc` process surveillance, custom trusted-bundle promotion PKI, synthetic Commit-`B` four-manifest hash cycles, 3-branch GitBook SSH state machines, 678 stale pre-release fragments, and a `PresentationConfigGrammar` censor over `git-cliff`), while leaving container Dockerfiles drifted between `.NET 10` and `.NET 11` and shipping no real artifact signing, attestations, or automated contract diffing.

ISLAMU Event requires an enterprise-grade, maintainable, provider-neutral release architecture that:
- Keeps commit governance frictionless for internal engineering changes while strictly protecting breaking changes and security embargoes;
- Uses `git-cliff` natively for Conventional Commit traversal, grouping, and Tera rendering while keeping C# as the authoritative policy engine;
- Generates conservative, deterministic machine-readable contract evidence across OpenAPI/HAL, configuration contracts, and discovered EF Core migration inventories;
- Publishes source-bound, cryptographically verifiable `.NET 10` container images and release file artifacts using industry standards (OpenSSH signed annotated Git tags, `SHA256SUMS`, Sigstore Cosign OIDC bundles, Buildx SBOMs, and GitHub SLSA v1.0 attestations);
- Preserves contributor privacy (`Huquq al-'Ibad`) in immutable signed artifacts while allowing warm, editable/removable contributor recognition on mutable release pages.

## Decision

1. **The SSH-signed annotated Git tag is the sole immutable release identity.**
   Every release declares a version-line label `v<major>.<minor>` (classification only; never a branch ref). `refs/heads/v*` is forbidden; maintenance branches `release/<major>.<minor>` are opened lazily from verified signed stable tags only when needed and may be deleted freely. `develop` never receives generated `Unreleased` changelog writes. `verify-release-tag` verifies annotated tags directly against `eng/release/trust/allowed-signers` (`gpg.format=ssh`, `gpg.ssh.allowedSignersFile`), binding tag name, target commit OID, event SHA, and signer principal.
2. **Retire internal security theater without compatibility shims.**
   Custom `/proc` process surveillance (`LoadProcessOwner` extracted separately to `eng/tools/Event.LoadProcessOwner` solely for distributed load harness tests), custom trusted-bundle promotion PKI, Commit-`B` circular manifests, GitBook publication-inventory commands, `ChangeIdRenamePolicy`, `GitCliffRenderer`, historical pre-release fragments, and placeholder `printf` release workflows are permanently retired. `ISLAMU.ReleaseEngineering` builds cleanly with `AllowUnsafeBlocks=false`, `RunAnalyzersDuringBuild=true`, and zero warning suppressions.
3. **C# remains release authority; verified `git-cliff` `2.14.2` owns traversal and Tera rendering.**
   - `eng/release/toolchain.lock.json` pins `git-cliff` `2.14.2` across four platforms by archive and executable SHA-256. `ensure-git-cliff` is the sole network provisioning path; all authoritative executions run `--config`, `--offline`, and `--no-exec` via argument arrays under a scrubbed environment (`GIT_CLIFF_OFFLINE=1`).
   - Authority (`release-context.v1.json`) and rendering projection (`release-rendering-context.v1.json`) are separate artifacts. Stage 1 (`--context`) raw output stays transient in memory and is never persisted or logged. Stage 2 (`GitCliffContextEnricher`) constructs a new allowlisted native context array correlated with C# authority (never denylist scrubbing), rejecting unknown/identity/embargo leakage. Stage 3 (`--from-context -`) renders three-persona notes (`Community Admin`, `Self-Hoster / Operator`, `Developer / API Integrator`) with `format = true`, gated by post-render `ReleaseArtifactPolicy`.
   - `git-cliff --bumped-version` is comparison evidence only; disagreement with the selected stable version fails closed.
4. **Frictionless, parity-tested commit governance and optional high-impact fragments.**
   Non-breaking internal commit types and engineering scopes omit automatically without `Changelog: skip` trailers across both C# `CommitPolicy` and `eng/release/cliff.toml` (parity-tested against the real v2.14.2 binary with `require_conventional = true`, `protect_breaking_commits = true`, `fail_on_unmatched_commit = true`, no catch-all parser). `.githooks/commit-msg` is a thin wrapper over `preflight-commit`. Optional `CHG-*.yaml` fragments undergo full `ReleaseInputPolicy.Validate` with structural embargo checks (`Public-Disclosure: embargoed`, `Restricted-Details`).
5. **Conservative, pinned-revision contract diffing (`diff-contracts`).**
   Ordered `IReleaseContractDiffer` strategies compare pinned Git OIDs (never the working tree) across OpenAPI/HAL, configuration schemas + redacted `.env.example`, and dynamically discovered EF Core migration inventories. Unknown structural changes classify as `review_required`. Automated diffs never claim runtime compatibility or semantic provider parity; existing provider apply/idempotency tests and EF pending-model checks own semantic validation.
6. **Build before rendering image claims; variable-driven tagless digest-only OCI staging.**
   All four Dockerfiles (`Explore.API`, `Explore.Blazor`, `Event.Standalone`, `Event.MigrationService`) stay aligned on `.NET 10` (`sdk:10.0.404`). Governed `v*` tags trigger `.github/workflows/release.yml` exclusively (never `deploy-coolify.yml`). `.github/workflows/release.yml` runs `verify-release-tag`, runs the pre-build sealed-release guard (before requiring mirror credentials), and validates registry variables/secrets under the protected `release` environment before building. GHCR (`ghcr.io/<repo-owner>`, always using the lowercase repository owner) is the zero-secret built-in primary registry; any optional external OCI registry mirror (Docker Hub, Quay, ATCR, etc.) is configured strictly via GitHub Variables (`vars.CONTAINER_REGISTRY`, `vars.CONTAINER_REGISTRY_USER` for the OCI namespace, optional `vars.CONTAINER_REGISTRY_LOGIN_USER` for robot/org logins) and Secret (`secrets.CONTAINER_REGISTRY_PASSWORD`) with zero hardcoded personal handles or forced hosts in YAML. `_container-build.yml` (`push-mode: digest-only`) builds, Trivy-scans, SBOM/SLSA-attests, and pushes all four OCI indexes with `push-by-digest=true,name-canonical=true` and zero registry tags. `release-images.v1.json` enforces symmetric registry coverage across all four images and feeds `prepare --image-manifest` so notes never claim unbuilt digests.
7. **Direct cryptographic sealing, per-reference OCI signing, verified-draft publish-once, and post-tag changelog sync.**
   `seal-artifacts` produces deterministic `release-artifact-manifest.v1.json` and sorted `SHA256SUMS` without clobbering. `actions/attest` attests all sealed files; Cosign signs and verifies **every** published canonical `registry/repository@sha256:<digest>` reference across every configured registry individually (`4` in GHCR-only mode; `8` when a secondary mirror is configured) and signs `SHA256SUMS` via `cosign sign-blob --yes --bundle SHA256SUMS.sigstore.json`. Exact eight assets upload to a draft GitHub Release without `--clobber`, pass round-trip verification, tag-object OID (`tag-object-id`) binding, and asset-identity snapshot verification, publish once by numeric draft ID, and pass immediate post-publish verification. `sync-public-changelog` verifies signed tag-tree inputs (`release.yaml`, `summary.md`, linked fragments) together with sealed post-tag release archives without moving tags. Published releases are never replaced; corrections ship as new releases and advisories.
8. **Model 2 Hybrid Split contributor recognition (`Huquq al-'Ibad`).**
   Signed artifacts remain 100% offline and identity-free. Best-effort post-publication `append-contributors` executes verified `git-cliff` `2.14.2` with `eng/release/cliff-github-contributors.toml` (`--no-exec`, guarded missing-remote fallback, zero raw emails) to append `@handle` and PR acknowledgements solely to the mutable GitHub Release body so contributors can request edits or removal at any time without breaking cryptographic verification.

## Consequences

- **High maintainability and truthful CI:** `ISLAMU.ReleaseEngineering` is concise, safe (`AllowUnsafeBlocks=false`), analyzer-clean (zero warnings), and required in `_build-test.yml` and `workflow-security.yml`. All four Dockerfiles are validated in `workflow-security.yml`.
- **Honest verification and failure boundaries:**
  - Offline verification covers Git tag signatures (`git verify-tag` / `verify-release-tag` against an independently obtained trusted `allowed-signers` file) and file checksum consistency (`sha256sum --check SHA256SUMS` after authenticating `SHA256SUMS`).
  - Online verification covers Cosign OIDC identity/transparency proofs (`cosign verify`, `cosign verify-blob`) and GitHub SLSA attestations (`gh attestation verify`).
  - Cross-provider publication (GitHub + GHCR + any configured external OCI mirror) is not atomic, and GitHub Releases does not support atomic compare-and-swap on `draft=false`; `v*` tag rulesets, restricted `contents: write` access, pre-publish tag/asset-identity checks, and immediate post-publish verification mitigate and detect tampering, while failed pre-publish runs may leave unadvertised digest-only objects or an unpublished draft that reruns verify or fail closed without clobbering.
  - SBOM and SLSA provenance do not claim byte-for-byte container reproducibility.
- **Activation gate:** Source-level implementation and tests do not prove hosted OIDC, registry, Cosign, or draft-recovery behavior. Project Steward approval is required to enroll production SSH signers in `eng/release/trust/allowed-signers` and run the authorized fault-injecting release-candidate rehearsal before `v0.1.0`.

## References

- [Release Policy](../RELEASE_POLICY.md)
- [Release Runbook](../RELEASE_RUNBOOK.md)
- [Release Checklist](../RELEASE_CHECKLIST.md)
- [CI/CD Governance](../CI_CD_GOVERNANCE.md)
- [Adopter Releases and Changelog Guide](../../public/documentation/readme/configuration-and-operations/releases-and-changelog.md)
- [I-VSD Release Engineering Modernization Assessment](../../../islamic-value-sensitive-design/workstreams/i-vsd-release-engineering-modernization.md)
