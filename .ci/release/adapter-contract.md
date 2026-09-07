# Release Adapter Contract

This contract is prospective release transport only. The release engine owns canonical release identity; adapters move explicit inputs, retained artifacts, and protected-ref actions without reinterpreting them.

## Canonical Boundary

Adapters receive only explicit full Git object IDs, a promoted trusted bundle, release inputs, retained artifacts, and requested protected-ref or publication actions. Provider run IDs, URLs, actors, labels, comments, and release-page metadata are noncanonical and must not change canonical checksums.

The validator/planner is `.ci/scripts/validate-release-provider-adapters.cs`. It validates `.ci/providers/**/provider-definition.v1.json`, verifies the caller-supplied synthetic release input file, checks the promoted bundle checksum, and writes provider transport plans. It never signs, tags, pushes, publishes, deploys, fetches, or executes candidate checkout code.

## Required Lanes

Every provider definition has two lanes:

- `previewLane`: untrusted candidate feedback. It may compile or test candidate release tooling but has only `contents:read`, no secrets, no write permission, no `id-token:write`, no deployment/package/protected-ref authority, and an always-present `release-adapter-preview` no-op-safe check.
- `finalLane`: trusted transport after candidate execution has stopped. It either proves trusted default-branch code, or for transport-only discovery declares `no-checkout-discovery` and proves the final workflow performs no checkout or candidate-controlled execution. It verifies immutable promoted bundle bytes again, uses approval or explicit external operator evidence, and exposes an always-present `release-adapter-final` no-op-safe check.

Final lane events are provider-specific and exact. GitHub accepts only
`workflow_dispatch`. Forgejo/Codeberg accepts only the reviewed trusted
`workflow_dispatch` discovery lane. Tangled accepts only `manual` or a reviewed
`tag_push` lane. No provider may use `pull_request`, `pull_request_target`, mixed-case
variants, or spelling variants as a final event. Preview and final workflows stay split
unless a provider has a reviewed native guard syntax and the validator can prove the
manifest/workflow parity.

## Required Inputs

The planner accepts one `release-adapter-inputs.v1` JSON document containing:

- `targetOid`: final preparation commit `B`, full SHA-1 or SHA-256 object ID.
- `expectedOldProtectedRefOid`: compare-and-swap old protected-ref object ID. This is a
  **mutation precondition**, not release identity: it exists so a protected-ref update
  fails on a race. No adapter input carries a branch ref or branch head as identity.
- `tagObjectId`: final annotated tag object ID.
- `tagName`: canonical tag name.
- `releaseBundlePath`: path under the supplied bundle root.
- `releaseBundleSha256`: expected SHA-256 of that promoted bundle artifact.
- `artifactManifestSha256`: checksum of retained artifact manifest evidence.
- `dirtyWorktree`: must be false for final planning.

Unsupported provider capabilities require a separate optional
`release-adapter-external-control-evidence.v1` JSON input supplied to the validator
with `--external-control-evidence`. Provider manifests cannot self-assert this
evidence. The evidence is bounded, rejects aliases, and must match the provider,
operation, and unsupported capability exactly.

## Required Outputs

Each successful provider plan is `provider.transport-plan.v1.json` and contains:

- provider ID and display name;
- preview and final lane events/checks;
- exact protected-ref compare-and-swap old/new IDs;
- tag object and promoted bundle checksum;
- canonical checksum set for the release inputs and promoted bundle;
- `transportOnly=true` and `metadataCanonical=false`.

## Publication Projection

Publishing a release page is **not** part of release identity. Each provider definition declares
`publicationWorkflows` (possibly empty). The validator enforces what is actually enforceable:

- The workflow runs only in a trusted lane. `pull_request`, `pull_request_target`, and `push`
  origins are rejected (`adapter_publication_untrusted_origin`).
- Every published body carries the canonical `release-notes.md` SHA-256 and its tag reference, so a
  reader can check the page against the repository (`adapter_publication_canonical_reference_missing`).
- Self-verifying assets are attached: `release-evidence.v1.json`, `artifacts.sha256`, container
  image digests, and SBOM (`adapter_publication_asset_missing`). Forge-generated `.zip`/`.tar.gz`
  archives may be linked but are never treated as reproducible artifacts.
- External actions in publication workflows use full SHA pins (`adapter_action_pin_mutable`).
- A provider whose `capabilities.releasePublication` is false must set
  `operatorEvidenceRequired` and declare a `recorded-no-op` path
  (`adapter_publication_noop_evidence_required`). A missing release API or a forge outage is a
  recorded no-op with operator evidence, never a failed release.

`.ci/scripts/report-publication-drift.cs` consumes a bounded `release-publication-projection.v1`
document and the release's own `release-evidence.v1.json`, then writes
`publication-drift-report.v1.json` plus a Markdown summary. It **reports**: it never edits a page,
never rewrites canonical bytes, and never invalidates a release. Its report always carries
`autoRepair: false` and `releaseInvalidated: false`. Drift exits `0` by default; operators who want
a blocking gate pass `--fail-on-drift`.

## Reserved Ref Namespace

Version tags own the `v*` glob outright. Providers MUST configure a protected-ref rule
that rejects branch creation under `refs/heads/v*`, so no branch can shadow or be
confused with a version tag. Maintenance lines use `release/<major>.<minor>` and are
opened on demand from a verified signed stable tag; adapters never create them as part
of a release. Release identity is the annotated tag object alone — an adapter that
cannot perform a protected-ref action fails closed and never substitutes a branch state.

## Stable Diagnostics

The validator emits bounded diagnostic codes, including `adapter_definition_unknown_key`, `adapter_definition_missing_key`, `adapter_input_oid_not_full`, `adapter_preview_secrets_forbidden`, `adapter_preview_permission_forbidden`, `adapter_final_candidate_code_forbidden`, `adapter_final_trusted_ref_invalid`, `adapter_final_event_forbidden`, `adapter_final_environment_required`, `adapter_no_checkout_discovery_invalid`, `adapter_action_pin_mutable`, `adapter_action_manifest_mismatch`, `adapter_discovery_workflow_missing`, `adapter_discovery_workflow_mismatch`, `adapter_required_check_missing`, `adapter_provider_action_unsupported`, `adapter_external_control_evidence_invalid`, `adapter_checksum_drift`, `adapter_path_alias`, `adapter_dirty_worktree`, and `adapter_misleading_success_forbidden`.

## Provider Requirements

`finalLane.trustedRef = "default-branch"` is reserved for activated final lanes that
prove trusted default-branch checkout before any execution. `finalLane.trustedRef =
"no-checkout-discovery"` is allowed only for transport-only final discovery workflows
whose checked workflow text contains no checkout, candidate ref/path, external command,
mutable action/image, or nonliteral `run:` expression. Activated release execution for
Forgejo/Codeberg, Tangled, or any future provider must migrate from
`no-checkout-discovery` to default-branch proof before it can run release logic.

Forgejo/Codeberg requires a trusted self-hosted runner for final operations. Hosted Codeberg Actions may be useful for preview feedback, but hosted limitations mean final protected operations require the self-hosted trusted runner lane plus default-branch checkout proof before any release-engine execution.

Tangled supports preview transport and AT Protocol artifact records, but protected refs, required checks, manual approval, and dedicated release publication remain undocumented in this contract. Its current final workflow is no-checkout discovery only. Final protected-ref and release publication actions are unsupported unless an operator supplies explicit external control evidence through the validator input. Without that separate evidence, Tangled plans fail closed; the manifest cannot make itself authoritative.

GitHub splits `pull_request` preview from environment-approved `workflow_dispatch` final. The final lane checks out the repository default branch from workflow repository metadata, not `github.sha`, and must never check out or execute pull-request head code in a privileged `pull_request_target` context. External GitHub actions in discovery workflows use full SHA pins with same-line version comments.

Promoted bundle files, release input files, provider manifests, and external-control evidence files reject symlinks, reparse points, and hardlinks before their bytes become authoritative.

## Cleanup And Retention

Provider definitions declare artifact retention days and cleanup support. Cleanup is transport metadata only; canonical evidence remains the release-engine and durable bundle responsibility.
