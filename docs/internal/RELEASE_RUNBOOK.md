<!-- ABOUTME: Provides the current manual release procedure and future governed-release operator flow. -->
<!-- ABOUTME: Keeps operator approval explicit while defining trust, evidence, and recovery boundaries. -->

# Release Runbook

> **Status:** Current manual procedure active; governed flow is prospective
> **Owner:** Platform/Ops and release operators

## Current procedure

Until a trusted release bundle, release-engine verification, signer policy, provider
protection evidence, and advisory dry run are implemented, operators MUST use
[RELEASE_CHECKLIST.md](RELEASE_CHECKLIST.md). The current process is manual SemVer
tags and manually authored GitHub Releases. Do not invoke imagined release-engine
commands, claim automatic approval, or create generated changelog writes on
`develop`.

Generate the existing durable evidence bundle only as the checklist directs:

```sh
dotnet run .ci/scripts/generate-release-evidence-bundle.cs -- artifacts release-evidence
```

## Future governed release flow

1. Create public change metadata and its commit footer together:

   ```sh
   dotnet run --project eng/release/src/ISLAMU.ReleaseEngineering/ISLAMU.ReleaseEngineering.csproj -- \
     create-change --target develop --type <type> --scope <scope> \
     --title "<title>" --summary "<summary>"
   ```

   Install the local checks with `install-change-hooks --target develop`.
   Existing hooks are preserved and chained. Immediately before merge or
   conflict resolution, run:

   ```sh
   dotnet run --project eng/release/src/ISLAMU.ReleaseEngineering/ISLAMU.ReleaseEngineering.csproj -- \
     preflight-range --target develop --head HEAD
   ```

   If a committed feature footer collides, keep the commit immutable and run
   `rename-change --commit <full-oid> --from <old-id> --reason "<reason>"`.
   Review and commit the generated replacement fragment plus exact-commit
   correction record, then rerun `preflight-range`. Do not amend, rebase, or
   create a loose ID alias for this repair.
2. Before the first governed release only, meaning no governed stable SemVer release
   tag is reachable from the candidate, an operator may activate one explicit
   `changelog-baseline-YYYY-MM-DD` lower-bound tag after Project Steward approval.
   The tag is created outside the verifier as an SSH-signed annotated tag targeting
   the eventual merged activation commit, then verified with exact full IDs:

   ```sh
   dotnet run --project eng/release/src/ISLAMU.ReleaseEngineering/ISLAMU.ReleaseEngineering.csproj -- \
     verify-baseline changelog-baseline-YYYY-MM-DD <full-target-oid> <full-tag-object-id>
   ```

   The verifier writes `docs/internal/releases/baselines/changelog-baseline-YYYY-MM-DD.v1.json`.
   It never creates, deletes, moves, signs, pushes, publishes, or fetches tags. If the
   tag is lightweight, unsigned, unauthorized, recreated, moved, date-malformed,
   target-mismatched, or supplied with a short object ID, stop and restart from a
   reviewed operator action. Do not use `v0.0.0` or any fake SemVer tag as a baseline,
   and do not treat the selected version number as proof that this is the first
   governed release.
3. An operator selects the version-line **label** `v<major>.<minor>` the release
   belongs to and prepares only `release.yaml` and `summary.md`; public high-impact
   facts are supplied through validated change fragments. The label classifies the
   release; it never names a branch and nothing derives a ref from it.
4. The trusted bundle validates complete Git objects, policy, range, version,
   impacts, and public inputs. It normalizes context and calls the promoted
   git-cliff renderer with the `VerifiedTrustedBundle` capability returned by
   successful bundle verification. Candidate jobs never pass raw config, lock,
   executable, or digest inputs into rendering. The renderer passes
   `--config <trusted>`, `--from-context <context>`, `--offline`, and `--no-exec`
   from an isolated non-Git working directory after rechecking bundle-owned bytes.
5. The preparation command generates `release-notes.md` and creates the reviewed
   preparation commit `B`. The message explicitly explains its release-metadata
   changelog skip so the generated note does not include itself.
6. The authoritative bundle verifies candidate `B` and emits deterministic
   pre-tag candidate evidence from immutable objects only. Integrating `B` into the
   branch it was prepared on is a fast-forward-only compare-and-swap; that
   compare-and-swap is a precondition of the *push*, not part of verification. Any
   replacement of `B` requires regeneration and review because it is a new object.
7. An authorized release operator creates an SSH-signed annotated tag targeting
   exactly `B`. Tag verification records signer, tag object ID, candidate digest,
   policy/tool hashes, and note/context hashes in final evidence.
8. For the newest stable release only, protected `main` advances by normal
   fast-forward to exactly `B`. Prereleases and older-line patches do not move
   `main` backwards.
9. The provider adapter retains artifacts and may publish a derived enriched view,
   but it cannot alter canonical notes, identity, or approval.

### Provider adapter planning

Before a forge-specific adapter transports retained artifacts or protected-ref inputs,
validate the provider definitions and write transport plans from explicit local release
inputs:

```sh
dotnet run .ci/scripts/validate-release-provider-adapters.cs -- \
  --providers .ci/providers \
  --inputs /absolute/path/release-adapter-inputs.v1.json \
  --bundle-root /absolute/path/promoted-bundle-root \
  --output /absolute/path/release-adapter-plans
```

The input file supplies `release-adapter-inputs.v1`, full target/tag/protected-ref
object IDs, the promoted bundle relative path, expected bundle SHA-256, artifact
manifest SHA-256, tag name, and dirty-worktree status. The validator rejects path
aliases, short object IDs, checksum drift, preview secrets, final candidate-code
authority, preview write/OIDC permissions, mutable GitHub Actions pins, missing
required checks, provider/action misrepresentation, symlink/reparse/hardlink aliases,
and metadata-canonical plans before publishing output.

For a provider action whose capability is unsupported, pass separate external control
evidence only after an operator has recorded the provider-side control:

```sh
dotnet run .ci/scripts/validate-release-provider-adapters.cs -- \
  --providers .ci/providers \
  --provider tangled \
  --operation publish-release \
  --external-control-evidence /absolute/path/external-control-evidence.v1.json \
  --inputs /absolute/path/release-adapter-inputs.v1.json \
  --bundle-root /absolute/path/promoted-bundle-root \
  --output /absolute/path/release-adapter-plans
```

The provider manifest cannot provide this evidence itself. The evidence file is bounded
and must exactly match `providerId`, `operation`, and the unsupported capability.

Successful output is bounded:

```text
adapter_validation_passed: providers=<count>
```

The resulting `*.transport-plan.v1.json` files are provider transport instructions
only. They preserve checksum equality across Forgejo/Codeberg, Tangled, and GitHub;
they do not render notes, sign tags, classify commits, choose versions, publish
releases, or update protected refs by themselves.

## Required controls before activation

### Preparation command

After `docs/internal/releases/<version>/release.yaml` and `summary.md` are reviewed, check out
the commit the release should end at and run the command from that working tree. The
range end is the checked-out `HEAD`; no branch name is derived from the line label.

```sh
dotnet run --project eng/release/src/ISLAMU.ReleaseEngineering/ISLAMU.ReleaseEngineering.csproj -- \
  prepare docs/internal/releases/<version>
```

The protected environment supplies the promoted bundle, receipt, detached
signature, principal, expected manifest SHA-256, and expected bundle, policy,
config, and trust versions through `ISLAMU_RELEASE_TRUSTED_BUNDLE`,
`ISLAMU_RELEASE_PROMOTION_RECEIPT`, `ISLAMU_RELEASE_PROMOTION_SIGNATURE`,
`ISLAMU_RELEASE_PROMOTION_PRINCIPAL`, `ISLAMU_RELEASE_MANIFEST_SHA256`,
`ISLAMU_RELEASE_BUNDLE_ID`, `ISLAMU_RELEASE_BUNDLE_VERSION`,
`ISLAMU_RELEASE_POLICY_VERSION`, `ISLAMU_RELEASE_CONFIG_VERSION`, and
`ISLAMU_RELEASE_TRUST_VERSION`.
The command verifies that promotion before rendering, reads the descriptor and
summary, derives the complete local Git range from `Previous-Published-Tag` to the
checked-out `HEAD`, validates linked public fragments and impact evidence,
atomically creates `release-notes.md`, and writes canonical
`release-context.v1.json` next to the release inputs. The generated notes always
use this order: maintainer summary, release-visible details only, then complete
provider-neutral full commit range. Empty detail/impact sections are omitted.
It prints the exact `docs(release): prepare <version>` commit message with
`Changelog: skip` and `Changelog-Reason: release metadata commit`; it never
commits, tags, pushes, publishes, invokes candidate code, or uses the network.
An identical rerun is a byte-idempotent success. A different existing generated
file, path escape, symbolic link, context/range/fragment drift, renderer failure,
or write failure stops without changing `release.yaml` or `summary.md`.

### Candidate verification command

After the reviewed preparation commit `B` is created, run the promoted bundle verifier
against that exact full commit object. No branch has to point at `B`, and the command
works identically in a clone that fetched only `refs/tags/*`:

```sh
dotnet run --project eng/release/src/ISLAMU.ReleaseEngineering/ISLAMU.ReleaseEngineering.csproj -- \
  verify-candidate docs/internal/releases/<version> <full-B-oid>
```

The command reads the same protected environment variables as `prepare`. It validates
the local repository without lazy fetches or replacement objects, resolves the base and
previous tags as annotated tag objects, requires `B` to descend from the base tag along
a linear range, requires `B` to be the terminal commit in the descriptor-selected range,
and requires `B`'s terminal footers to be exactly
`Changelog: skip` and `Changelog-Reason: release metadata commit`. It recomputes the
release context through `B`, rerenders the notes with the separately promoted trusted
bundle, and compares both committed generated artifacts byte-for-byte. It then writes
or verifies `docs/internal/releases/<version>/release-candidate.v1.json`.

`release-candidate.v1.json` is pre-tag evidence only. It records schema/object format,
full `B` and range commit IDs, version/line-label/date, base and previous tag commit
IDs, the exact parent as `expectedIntegrationOldOid` and `B` as
`expectedIntegrationNewOid` for the integration push precondition, trusted
bundle/policy/config/trust/tool hashes, and release descriptor, fragment, summary,
context, and note hashes. It deliberately omits every branch ref and branch head object
ID, tag object IDs, current time, provider metadata, identities, emails, raw commit
bodies, tokens, and secrets.

Successful output is one bounded line:

```text
release_candidate_verified: docs/internal/releases/<version>/release-candidate.v1.json
```

Failures are also bounded and use stable diagnostic codes, for example:

```text
verify_candidate_failed: candidate_committed_artifact_mismatch
verify_candidate_failed: candidate_object_anchors_moved
verify_candidate_failed: candidate_terminal_commit_not_release_metadata_skip
verify_candidate_failed: candidate_release_context_mismatch
verify_candidate_failed: candidate_release_notes_mismatch
verify_candidate_failed: candidate_manifest_stale
```

Base or previous tag recreation, squash/rebase/merge replacement, wrong parent/range,
descriptor, fragment, policy, config, tool, bundle, context, summary, or note drift,
shallow/partial/replace/graft state, object-format mismatch, stale candidate manifest,
or dirty committed generated artifact stops before tagging or publication.

### Tag message and final tag verification commands

After `verify-candidate` succeeds, generate the annotated-tag message from canonical
release sources. Operators do not edit the tag narrative independently:

```sh
dotnet run --project eng/release/src/ISLAMU.ReleaseEngineering/ISLAMU.ReleaseEngineering.csproj -- \
  tag-message docs/internal/releases/<version> > /tmp/islamu-release-tag-message.txt
```

The message contains only the canonical tag name, version, release line, exact
candidate `B`, candidate-manifest SHA-256, and release-note SHA-256. It has no
independently editable summary or final-manifest digest and omits current time,
provider UI state, identities, emails, raw commit bodies, tokens, and secrets.

An authorized operator then creates the SSH-signed annotated tag outside the verifier:

```sh
git -c gpg.format=ssh -c user.signingKey=/path/to/release-signing-key \
  tag -s v<version> <full-B-oid> -F /tmp/islamu-release-tag-message.txt
```

Verify the named tag locally; the command resolves and records its full tag object ID:

```sh
dotnet run --project eng/release/src/ISLAMU.ReleaseEngineering/ISLAMU.ReleaseEngineering.csproj -- \
  verify-tag docs/internal/releases/<version> v<version>
```

The promoted verifier retains the internal exact-object seam for prior published tag
object drift checks; the public command accepts no caller-selected trust root or
object override. `verify-tag` verifies the tag object is an
annotated tag, the OpenSSH signature validates against the promoted allowed-signers
root, the promoted signing policy authorizes the release role/principal/algorithm/date,
the tag's internal name is exactly `v<version>`, the target is exactly `B`, the release
line and descriptor fields still match, the candidate manifest digest and source-file
hashes still match, and the existing `release-evidence.v1.json` is byte-identical on
rerun. It writes or verifies:

```text
docs/internal/releases/<version>/release-evidence.v1.json
```

Successful output is one bounded line:

```text
release_tag_verified: docs/internal/releases/<version>/release-evidence.v1.json
```

Stable failure diagnostics include:

```text
verify_tag_failed: release_tag_not_annotated
verify_tag_failed: release_tag_signature_invalid
verify_tag_failed: release_signer_unauthorized
verify_tag_failed: release_signer_revoked
verify_tag_failed: release_signer_not_current
verify_tag_failed: release_signer_algorithm_forbidden
verify_tag_failed: release_tag_wrong_target
verify_tag_failed: release_tag_name_mismatch
verify_tag_failed: release_tag_message_mismatch
verify_tag_failed: release_candidate_manifest_drift
verify_tag_failed: release_notes_hash_mismatch
verify_tag_failed: release_evidence_manifest_stale
```

Do not delete, recreate, or move tags to repair a failure. Correct the source of drift,
discard stale candidate/final evidence, and repeat the governed candidate and tag flow.

### Stable main verification command

After final tag verification succeeds, resolve the full old `origin/main` commit and the
final tag object ID locally, then verify the protected-ref action before any provider
adapter updates `main`:

```sh
old_main_oid=$(git rev-parse --verify refs/remotes/origin/main^{commit})
tag_object_id=$(git rev-parse --verify refs/tags/v<version>^{object})
dotnet run --project eng/release/src/ISLAMU.ReleaseEngineering/ISLAMU.ReleaseEngineering.csproj -- \
  verify-main docs/internal/releases/<version> "$old_main_oid" "$tag_object_id"
```

The command reads only local Git objects, refs, and `release-evidence.v1.json`. It never
fetches, pushes, signs, tags, commits, updates refs, executes candidate code, or calls a
forge API. Every successful stable action requires `refs/remotes/origin/main` to still
equal the supplied old full OID. Newest stable moves also require the final evidence
target `B` to be a normal fast-forward descendant. If `main` is already at `B`, the
command is idempotent. Older stable-line patches may publish but emit `no-main-move`;
prereleases never advance `main`.

Successful output is one bounded line with exact object IDs and a runbook action:

```text
release_main_verified: action=move-main old=<old-main-oid> new=<full-B-oid> tag=v<version> instruction=update-main-fast-forward
release_main_verified: action=already-at-target old=<full-B-oid> new=<full-B-oid> tag=v<version> instruction=no-op-main-already-at-release
release_main_verified: action=no-main-move old=<old-main-oid> new=<full-B-oid> tag=v<older-version> instruction=publish-release-without-main-update
```

Stable failure diagnostics include:

```text
verify_main_failed: release_main_oid_not_full
verify_main_failed: release_main_expected_old_missing
verify_main_failed: release_main_cas_mismatch
verify_main_failed: release_main_non_fast_forward
verify_main_failed: release_main_tag_object_mismatch
verify_main_failed: release_main_evidence_target_mismatch
verify_main_failed: release_main_prerelease_no_move
```

The provider adapter, not `verify-main`, performs the protected compare-and-swap update
after this local proof. On any failure, discard the protected-ref action and repeat the
candidate/tag/main verification from current local objects.

- The authoritative job MUST download and verify a separately promoted bundle before
  processing candidate data. An operator/provider supplies an immutable canonical
  promotion receipt and detached SSH signature separately from both the bundle and
  candidate checkout. The previously promoted verifier resolves its promoter trust
  root from its own fixed protected application directory, never from a request,
  candidate path, current working directory, or bundle under verification. The signed
  receipt binds the manifest, bundle, policy, config, and trust digests and versions;
  reusing it for the same immutable bundle is idempotent, while using it for any
  different bundle fails. No caller assertion or candidate checkout copy is
  authoritative. Candidate jobs MUST have no signing, publication, deployment,
  registry-write, OIDC, or protected-ref credentials.
- The bundle MUST include the release engine, policy, renderer config, tool pin, and
  SSH signer roots. The provider MUST retain protected-ref and environment approval
  evidence for final operations.
- `B`, the candidate record, the tag target, the committed notes, and the stable
  `main` target MUST be checked as one exact full-object identity. No branch head takes
  part in that identity; a branch head appears only as a push precondition.
- Public canonical artifacts MUST be deterministic and provider-neutral. Capture
  required hashes and inputs without wall-clock, provider, author, raw-body, or token
  data.
- Restricted security inputs MUST remain in the embargo lane outside the public
  checkout. If disclosure is not authorized, stop before public generation or tag.

### Publication projection and drift reporting

Publishing to a forge release page is a derived, noncanonical view of the signed tag. Canonical
truth stays the tag object plus `release-notes.md` committed at `B`. A release is complete without
any published page.

Each published page MUST carry the canonical `release-notes.md` SHA-256 and its tag reference, and
MUST attach `release-evidence.v1.json`, `artifacts.sha256`, container image digests, and SBOM.
Those assets are self-verifying; forge-generated source archives may be linked but are never
treated as reproducible artifacts.

To report drift between what is published and what was signed:

```sh
dotnet run --file .ci/scripts/report-publication-drift.cs -- \
  --release-directory docs/internal/releases/<version> \
  --projections publication-projection.v1.json \
  --output publication-drift
```

The projection input is a bounded `release-publication-projection.v1` document that the operator or
adapter assembles from what each forge currently shows. For each provider it records `state`
(`published`, `unavailable`, or `unsupported`), the `declaredCanonicalNotesSha256`, the
`declaredTagRef`, the attached `assets`, and either the `publishedBody` or its
`publishedBodySha256`.

Outcomes:

- `in-sync` — the page carries the canonical hash and tag reference and the required assets.
- `drift` — reported with specific findings. The command still exits `0`; drift never invalidates a
  release, and the tool never edits a page. Pass `--fail-on-drift` if you want a blocking gate.
- `recorded-no-op` — the provider has no release API, or the forge was unavailable, and an
  `operatorEvidenceReference` was supplied. Without that reference this is reported as drift, so a
  silent omission cannot pass as a deliberate no-op.

If the local `release-notes.md` no longer hashes to the value in `release-evidence.v1.json`, the
command fails closed with `drift_canonical_notes_mismatch`. That is a local checkout problem, not a
forge problem: fix the checkout before drawing conclusions about a published page.

### Maintenance lines (open on demand, delete freely)

No branch is provisioned when a release is tagged. The default state after `v0.1.0` is
one tag and zero new branches. Open a maintenance line only when a real backport to an
already-released line is required:

```sh
git switch -c release/<major>.<minor> v<major>.<minor>.<patch>
```

Plan the action first. The engine derives the source from the release's own verified tag, so
there is no source argument to get wrong:

```sh
dotnet run --project eng/release/src/ISLAMU.ReleaseEngineering/ISLAMU.ReleaseEngineering.csproj -- \
  open-maintenance-line docs/internal/releases/<version> <full-tag-object-id>
```

It re-verifies the tag through the promoted bundle, refuses prereleases, derives
`refs/heads/release/<major>.<minor>` from the version-line label, and prints one bounded line:

```text
maintenance_line_verified: action=create-maintenance-line branch=refs/heads/release/<M>.<m> source-tag=v<M>.<m>.<p> expected-old=none expected-new=<full-B-oid> instruction=git switch -c release/<M>.<m> v<M>.<m>.<p>
```

If the branch already contains the released commit the action is `already-open` with
`instruction=no-op-maintenance-line-already-open`. The command never creates, moves, deletes, or
force-updates a ref; the operator runs the printed command.

Rules:

- The **only** legal source is a verified signed stable tag on that line. `develop`,
  `main`, and arbitrary commits are rejected with `maintenance_line_source_not_release_tag`,
  because a branch cut from anywhere else contains commits that were never in the release and a
  patch built on it would ship unreviewed integration work.
- The branch MUST be named `release/<major>.<minor>` — no `v` prefix. `refs/heads/v*` is
  a reserved, protected namespace owned by version tags; creation there is rejected by
  provider settings and by `ReleaseRefNamespacePolicy`.
- Re-running against an existing branch is a no-op. Never force-update it.
- Deleting the branch after its final patch is supported, non-destructive cleanup:

```sh
git branch -D release/<major>.<minor>
git push origin --delete release/<major>.<minor>
```

Every release on that line remains fully verifiable afterwards, because verification
reads only the tag object, `B`, the tree at `B`, and ancestry from the base tag. To
reconstruct the line at any later time, re-run the `git switch -c` command above.

### Trust activation (two people, once)

Until this runs, `eng/release/trust/` is comment-only and every attestation path fails closed. That
is the correct default: a release-signing root asserted by nobody is worse than no root at all.

Activation is deliberately a **two-person act**. `separationOfDuty.releaseSignerCannotPromoteOwnCandidateBundle`
means one key must never be able to both promote the tooling bundle and sign the release that bundle
attests — otherwise a single compromised key forges the entire chain. Two people therefore each
generate their own key and keep their own private half:

```sh
# Release operator, on their own machine:
ssh-keygen -t ed25519 -C "islamu-release-operator"

# Tooling promoter, on a different machine:
ssh-keygen -t ed25519 -C "islamu-tooling-promoter"
```

Each person sends **only the `.pub` file**. Private keys never enter this repository, a trusted
bundle, a CI secret store, or a chat message. Then, with both public keys present:

```sh
dotnet run --project eng/release/src/ISLAMU.ReleaseEngineering/ISLAMU.ReleaseEngineering.csproj -- \
  activate-trust \
  --release-principal <release-operator-principal> \
  --release-key /path/to/release.pub \
  --promotion-principal <tooling-promoter-principal> \
  --promotion-key /path/to/promotion.pub \
  --valid-from <yyyy-MM-dd> --valid-until <yyyy-MM-dd> \
  --output eng/release/trust
```

The command accepts public key material only and refuses a file containing `PRIVATE KEY`. It
rejects a non-`ssh-ed25519` algorithm, a malformed key, an inverted or malformed validity window, a
malformed principal, and any attempt to use one principal, one key, or one fingerprint for both
roles. It writes `allowed-signers`, `promotion-allowed-signers`, and an activated
`release-signing-policy.yaml`, then prints both fingerprints:

```text
trust_activated: output=eng/release/trust
trust_release_signer: principal=... algorithm=ssh-ed25519 fingerprint=SHA256:... valid-from=... valid-until=...
trust_promotion_signer: principal=... algorithm=ssh-ed25519 fingerprint=SHA256:...
```

Re-running with identical inputs is a byte-idempotent no-op. Replacing an already-activated root is
a **rotation**, not an activation: it fails with `trust_activation_would_replace_existing_root`
unless `--replace` is passed, and the rotation must also be appended to
`eng/release/trust/rotation-history.md`.

Both fingerprints must be confirmed out of band with the two key holders before the first release is
signed, and `genesisIndependentReviewRequired` still applies to the first promoted bundle.

### Trusted-bundle bootstrap and upgrades

Genesis promotion is a required operator action because no earlier release engine can
validate the first bundle. Independent reviewers must bind the exact source and tool
hashes to a Release build, focused release-engine tests, SBOM, checksums, protected
approval, and an SSH-signed annotated tooling tag. Later upgrades use the same
separate promotion lane and cite the previously promoted bundle; candidate checkout
content is never promotion evidence.

Final attestation receives the promoted bundle plus its separately stored canonical
receipt and detached signature. The already promoted verifier uses only its protected
runtime trust root to verify the receipt before using the receipt-bound manifest
SHA-256, then verifies canonical manifest bytes, required and complete file sets,
  every file hash, and policy/config/trust versions and digests. Reusing the same signed
  receipt for the exact same manifest is an idempotent read-only verification, not a
  replay decision; the verifier stores no receipt registry and accepts no caller replay
  state. Manifest, receipt, signature, trust-root, file-count, per-file, total-size,
  path-length, path-depth, hardlink/link-count, and enumeration limits fail closed.
  Traversal, root aliases, symbolic links, duplicate normalized paths, and
  case-insensitive path collisions also fail before payload hashing. Final jobs must
  reverify the promoted bundle immediately before use from immutable promoted storage.
  Candidate executables run only in an unprivileged preview lane.

Task 5 tag verification must derive `SshTagAuthorizationRequest` signer, signature,
tag-kind, verification-date, and tag-object facts from local Git objects and local
OpenSSH verification evidence. Forge badges, provider UI states, or hosted release
metadata are never signer evidence.

Production activation remains blocked: no real release/tooling principals, public
keys, custody/rotation owners, or promoted artifact store and retention authority are
recorded. `eng/release/trust/allowed-signers` and the verifier-owned
`eng/release/trust/promotion-allowed-signers` are intentionally comment-only until a
reviewed promotion adds real principals and public keys, so activation returns
`trusted_bundle_promotion_authority_not_configured` and fails closed.

The first-release baseline is also blocked until the Project Steward approves the first
governed version and the real merged activation commit exists. Do not create a real
baseline tag, signed release tag, or versioned release directory from placeholders.

### Restricted security input

Embargo input is a separate access-controlled input mounted only after candidate code
has stopped. It must not enter the public checkout, candidate bundle, canonical
context, notes, manifests, checksums, retained diagnostics, logs, or provider
metadata. Diagnostics use bounded codes only and never echo paths, identities,
secrets, provider data, restricted values, or raw exception text. After disclosure
authorization, only the reviewed public disposition and public advisory reference may
cross into canonical generation, and neither field may exactly alias restricted
details, secret material, identities, storage paths, or provider metadata after
Unicode and whitespace normalization; otherwise the release stops.

## Stop and recovery rules

- Stop on missing Git objects, shallow/partial history, ref replacement, policy or
  bundle mismatch, renderer drift, unauthorized/unsigned/replaced tags, or a moved
  branch. Correct the input or promote a new bundle; do not bypass the check.
- If `B` changes after candidate verification, discard its candidate evidence and
  return to generation/review. Do not retarget a tag or force-push `main`.
- If an embargo field appears in a public artifact, stop publication, revoke public
  artifact access where possible, preserve incident evidence without reproducing the
  restricted value, and use the security incident process before retrying.
- If provider transport fails after local verification, retain local evidence and
  resolve the protected adapter failure. The provider adapter is not permitted to
  regenerate or reinterpret the canonical release.
