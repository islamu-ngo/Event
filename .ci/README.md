# CI/CD Implementation

`.ci` is the repository-owned CI/CD implementation layer that can be reused across forges.

GitHub-native discovery files stay in their required location:

- GitHub Actions: `.github/workflows/`

All shared scripts, policy validators, evidence writers, local composite actions, and cross-forge CI/CD definitions live here so they are reviewed and owned as one CI/CD surface. Codeberg and other mirrors should point their CI/CD settings at this folder when the provider supports custom pipeline locations.

Current layout:

- `.ci/actions/deploy-coolify/` contains the shared GitHub Coolify deployment composite action.
- `.ci/providers/` contains provider release-adapter definitions and discovery workflows for transport-only release lanes.
- `.ci/release/` contains the provider-neutral release adapter contract and strict provider manifest schema.
- `.ci/scripts/` contains file-based C# policy and evidence scripts invoked with `dotnet run <script>.cs -- ...`.
- `.ci/spectral.yaml` contains the project-owned OpenAPI lint ruleset.

`validate-release-provider-adapters.cs` validates `.ci/providers/*/provider-definition.v1.json`
against the provider-neutral adapter contract and writes deterministic
`*.transport-plan.v1.json` files. It also checks the declared discovery workflows so
manifest events, actions, final environment approval, trusted default-branch refs, and
transport-only no-checkout discovery claims match the reviewed workflow files. The
plans carry explicit local release inputs, full Git object IDs, protected-ref
compare-and-swap IDs, required check names, and canonical checksum equality only. They
deliberately do not choose versions, classify commits, render notes, sign tags, publish
releases, mutate protected refs, or make provider metadata canonical.

Current provider definitions are:

- `forgejo-codeberg`: no-checkout discovery-only mirror adapter that requires a trusted
  self-hosted final runner and default-branch proof before protected release actions can
  activate.
- `tangled`: discovery-only mirror adapter that records artifact support but marks
  protected-ref compare-and-swap and release publication as unsupported unless an
  operator supplies separate external evidence; activated execution must add default-branch proof.
- `github`: discovery-only GitHub adapter with a no-secret `pull_request` preview
  lane and an environment-approved `workflow_dispatch` final lane.

`generate-release-evidence-bundle.cs` is a durable bundle index, not a release
identity generator. Release-mode bundle generation requires exactly one retained
`release-evidence.v1.json` final manifest under the artifact root. The script
parses that canonical manifest, verifies its version/tag/target/hash fields
against explicit release inputs and retained artifacts, and keeps workflow run
IDs, URLs, provider data, and collection time as noncanonical bundle metadata.
Malformed/noncanonical JSON, unknown fields, missing or duplicate manifests,
case/NFC/path aliases, symlinks, oversized inputs, and retained-hash disagreement
fail with stable diagnostics before the final output directory is published.
The output directory must not already exist: the script publishes the complete
four-file bundle with one sibling-directory rename and never replaces a prior bundle.
The checksum manifest is written by `write-artifact-checksums.cs` so all retained
artifact hashing follows one script path.

Do not make `.github` a symlink to `.ci`: GitHub discovers workflows from `.github/workflows`, and local reusable workflow calls also use that path.
