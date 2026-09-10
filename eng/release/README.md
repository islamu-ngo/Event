# Release Engineering

`ISLAMU.ReleaseEngineering` is a standalone `net10.0` console project for
governed release tooling. It has no references to product projects.

## Collision-proof change workflow

New public changes use sortable ULID-style identifiers such as
`CHG-01K3Q8Y7M6N5P4R3T2V1W0X9ZA`. Sequential identifiers are historical
provenance only; no command allocates them.

Create the fragment and exact commit footer in one operation:

```bash
dotnet run --project eng/release/src/ISLAMU.ReleaseEngineering/ISLAMU.ReleaseEngineering.csproj -- \
  create-change \
  --target develop \
  --type feat \
  --scope registration \
  --title "Attendee correction window" \
  --summary "Attendees can correct registration details." \
  --group registration-correction
```

The command atomically creates
`docs/internal/releases/changes/<Change-Id>.yaml`, initializes every required impact
section for review, and prints `commit_footer: Change-Id: <Change-Id>`. Use
`allocate-change-id --target develop` only when another tool owns fragment
creation.

## Categorized canonical notes

`GitCliffRenderer` groups validated presentation records without changing the
canonical release context, version policy, visibility, backport identity, or
complete commit range. Breaking changes precede Features, Bug Fixes,
Performance, and Other Improvements; breaking fixes belong only to the first
category. Context order is preserved within each category, and empty categories
are omitted. Fixed headings appear once per group, while each primary entry
retains its scope, title, and display ID.

`ReleasePreparation` still composes the maintainer summary, rendered details,
applicable impact evidence, and complete range. An ID repeated in an impact
reference or the complete range is not a duplicate primary entry. Migration,
configuration, security, OpenAPI, breaking, and operator evidence remains
required where applicable. Presentation does not authorize disclosure.

The engine and packaged template must be promoted together through the existing
trusted-bundle procedure. Never modify an already promoted bundle or regenerate
signed historical notes with a newer formatter. Candidate verification at exact
`B` recomposes the notes and rejects different committed bytes with
`candidate_release_notes_mismatch`. Before `B`, recover differing generated
files using the [runbook](../../docs/internal/RELEASE_RUNBOOK.md#generated-note-mismatch-recovery);
after signing, correct forward.

Install local commit checks once:

```bash
dotnet run --project eng/release/src/ISLAMU.ReleaseEngineering/ISLAMU.ReleaseEngineering.csproj -- \
  install-change-hooks --target develop
```

The installer creates `pre-commit` and `commit-msg` checks. If either hook
already exists, it is preserved as `<hook>.before-islamu-release` and called
first. An ambiguous overwrite fails closed.

Before starting a merge, validate the complete feature range:

```bash
dotnet run --project eng/release/src/ISLAMU.ReleaseEngineering/ISLAMU.ReleaseEngineering.csproj -- \
  preflight-range --target develop --head HEAD
```

This compares effective Change-Ids in `develop..HEAD` with every ID reachable
from `develop`, rejects duplicates inside the feature range, and requires an
exactly named fragment for every linked ID. Fragments and correction records
must already be committed at `HEAD`; staged, modified, or untracked release
sources fail closed.

If an immutable commit already has a colliding footer, do not amend or rebase
it. Bind only that exact full commit object to a generated replacement ID:

```bash
dotnet run --project eng/release/src/ISLAMU.ReleaseEngineering/ISLAMU.ReleaseEngineering.csproj -- \
  rename-change \
  --commit <full-commit-oid> \
  --from CHG-2026-0011 \
  --reason "Target branch already owns the original identifier."
```

The command creates or reuses the replacement fragment and writes
`docs/internal/releases/change-id-renames/<full-commit-oid>.yaml`. Preparation and
candidate verification apply the replacement only when the immutable commit
still has the recorded old footer. The old footer remains unchanged; a loose
alias, branch-bound mapping, or reused replacement ID is rejected. Commit the
fragment and correction record before rerunning `preflight-range`.

```bash
export ISLAMU_RELEASE_TOOL_BUNDLE=/absolute/path/to/promoted/bundle
dotnet run --project eng/release/src/ISLAMU.ReleaseEngineering/ISLAMU.ReleaseEngineering.csproj -- verify-tools
```

The bundle directory must contain the platform executable at its lock-file name:
`git-cliff` on Linux x64 or `git-cliff.exe` on Windows x64. `verify-tools`
selects only the current approved platform from `toolchain.lock.json`, checks the
executable SHA-256, and requires the exact `git-cliff 2.13.1` version response.
Missing, malformed, mismatched, unsupported, noisy, failed, or hung tools fail
closed. The command never downloads a tool; bundle acquisition and promotion are
operator responsibilities outside this process.

Final release attestation must use a separately promoted trusted bundle, not the
candidate checkout. `TrustedBundlePolicy` verifies the caller-supplied manifest
SHA-256, exact-bundle-bound promotion receipt, complete manifest file set,
release-engine binary, policy/schema/context versions, packaged `cliff.toml`,
`toolchain.lock.json`, and trust roots before any candidate data is trusted. The
same receipt may be verified repeatedly for the same immutable bundle, but it cannot
promote different bundle bytes. Root aliases, symlink roots, symlink entries,
hardlinked bundle files, traversal, and candidate-local overrides fail closed.
Repository `allowed-signers` is comment-only until operators promote real SSH
principals, so production activation fails closed.

`TrustedBundlePolicy.Verify` returns a `VerifiedTrustedBundle` capability only
after manifest, promotion receipt, config digest, toolchain digest, paths, and
link safety pass. `GitCliffRenderer` accepts that capability, not raw bundle,
lock, config, or caller-supplied digest values. At render time it rereads the
bundle-owned `config/cliff.toml`, `toolchain.lock.json`, and executable, rejects
symlink/reparse and hardlink aliases, and rechecks bytes against the verified
capability and lock digests before copying anything into its isolated run
directory.

The packaged `cliff.toml` grammar is intentionally strict: comments and blank
lines, one exact `[changelog]` table, one multiline `body`, `trim = true`, and
`render_always = true`. The body may reference only `version`, loop over
`commits`, and print `commit.group`, `commit.scope`, `commit.message`, and `commit.id`. Dotted,
quoted, spaced, remote/provider/parser/bump/tag/range/processor/exec/URL variants
fail closed. The renderer invokes the verified binary with `--config`,
`--from-context`, `--offline`, and `--no-exec` from a temporary non-Git working
directory with a cleared deterministic environment. Candidate-local configuration
and environment variables are never consulted; there is no fallback renderer.

After `prepare` output is committed as the final release-preparation commit `B`,
`verify-candidate` validates and records pre-tag candidate evidence:

```bash
dotnet run --project eng/release/src/ISLAMU.ReleaseEngineering/ISLAMU.ReleaseEngineering.csproj -- \
  verify-candidate docs/internal/releases/<version> <full-B-oid>
```

The command is read-only with respect to Git: it never commits, tags, pushes, fetches,
or executes candidate checkout code. It recomputes the descriptor-selected Git range
through exact `B`, requires `B`'s terminal changelog skip reason, rerenders notes with
the verified promoted bundle, and writes or verifies the deterministic
`release-candidate.v1.json` manifest beside the release notes. The manifest is safe
for later tag closure because it contains only canonical object IDs and hashes; tag
object IDs and provider, clock, identity, raw-body, token, and secret data are excluded.

After candidate verification, generate the canonical annotated-tag message from the
release sources and candidate digest, then sign the tag outside the verifier:

```bash
dotnet run --project eng/release/src/ISLAMU.ReleaseEngineering/ISLAMU.ReleaseEngineering.csproj -- \
  tag-message docs/internal/releases/<version> > /tmp/islamu-release-tag-message.txt

git -c gpg.format=ssh -c user.signingKey=/path/to/release-signing-key \
  tag -s v<version> <full-B-oid> -F /tmp/islamu-release-tag-message.txt
```

Verify the resulting tag object locally with the same promoted bundle:

```bash
tag_object_id=$(git rev-parse "refs/tags/v<version>^{object}")
dotnet run --project eng/release/src/ISLAMU.ReleaseEngineering/ISLAMU.ReleaseEngineering.csproj -- \
  verify-tag docs/internal/releases/<version> <full-B-oid> "$tag_object_id"
```

`verify-tag` is read-only with respect to Git. It rejects lightweight, unsigned,
wrong-target, wrong-name, wrong-line, recreated, moved, unauthorized, revoked,
expired, wrong-algorithm, candidate-drift, note-drift, policy-drift, and stale-output
states before writing or accepting `release-evidence.v1.json`. The final manifest
chains to `release-candidate.v1.json` by SHA-256 and records the full tag object ID,
target `B`, signer principal/role/algorithm/fingerprint, prior tag relationship, and
trusted bundle/tool/policy/config hashes. It still excludes clocks, provider metadata,
emails, raw bodies, tokens, and secrets.

Before a protected-ref adapter advances stable `main`, verify the local topology and
compare-and-swap inputs:

```bash
dotnet run --project eng/release/src/ISLAMU.ReleaseEngineering/ISLAMU.ReleaseEngineering.csproj -- \
  verify-main docs/internal/releases/<version> <expected-old-origin-main-oid> "$tag_object_id"
```

`verify-main` is also read-only. It checks the final evidence target, tag object, and
local `origin/main` compare-and-swap OID before every successful stable action. Newest
stable moves also require normal fast-forward topology before printing the exact
old/new OIDs and the runbook instruction. Older-line stable patches emit
`no-main-move`; prereleases, stale evidence/tags, races, missing or short objects, and
non-descendant targets fail closed.

The existing durable CI bundle consumes that final manifest instead of inventing a
second release identity:

```bash
export RELEASE_VERSION=<version>
export GITHUB_SHA=<full-B-oid>
export GITHUB_REF=refs/tags/v<version>
export RELEASE_TAG_OBJECT_ID=<full-tag-object-id>
dotnet run .ci/scripts/generate-release-evidence-bundle.cs -- artifacts release-evidence
```

The artifact tree must contain exactly one `release-evidence.v1.json`. Bundle output
records provider and workflow collection metadata separately, but `releaseIdentity`
is copied from the canonical final manifest and verified against retained
`release.yaml`, `summary.md`, `release-context.v1.json`, `release-notes.md`,
`release-candidate.v1.json`, trusted-bundle policy/config/trust/tool files, and the
explicit environment inputs. Missing, duplicate, stale, tampered, or disagreeing
final manifests fail before `release-evidence.json` is accepted.

## Tests

The locked-renderer checks are TUnit `[Explicit]` methods. The ordinary suite
does not run them, even when `ISLAMU_RELEASE_TOOL_BUNDLE` is set. Provision the
Linux executable using the archive URL, archive SHA-256, executable name, and
executable SHA-256 in `toolchain.lock.json`, retain its license notices, and
select each method explicitly. Run these commands sequentially: independent test
processes share the output directory's synthetic promotion-root file, so TUnit's
in-process nonparallel grouping cannot protect concurrent invocations.

```bash
export TMPDIR="$HOME/.cache/agent-tmp"
export ISLAMU_RELEASE_TOOL_BUNDLE=/absolute/path/to/verified-test-tool-directory
dotnet build eng/release/tests/ISLAMU.ReleaseEngineering.Tests/ISLAMU.ReleaseEngineering.Tests.csproj --configuration Release --verbosity quiet
dotnet test --project eng/release/tests/ISLAMU.ReleaseEngineering.Tests/ISLAMU.ReleaseEngineering.Tests.csproj \
  --configuration Release --no-build \
  --treenode-filter "/*/*/GitCliffRendererTests/PromotedBinaryRendersTwiceByteIdenticallyOutsideGit" \
  --minimum-expected-tests 1
dotnet test --project eng/release/tests/ISLAMU.ReleaseEngineering.Tests/ISLAMU.ReleaseEngineering.Tests.csproj \
  --configuration Release --no-build \
  --treenode-filter "/*/*/FirstGovernedReleaseTests/CategorizedReleaseVerifiesAfterBranchDeletion" \
  --minimum-expected-tests 1
dotnet test --project eng/release/tests/ISLAMU.ReleaseEngineering.Tests/ISLAMU.ReleaseEngineering.Tests.csproj \
  --configuration Release --no-build \
  --treenode-filter "/*/*/ReleaseCandidateVerificationTests/VerifyCandidateRejectsCleanUnsignedBreakingNotesTamper" \
  --minimum-expected-tests 1
```

Missing or mismatched executable bytes fail the selected checks. Stub executables
still cover ordinary policy rejection cases, but are not real-renderer evidence.
The explicit fixtures use the real pinned renderer and shipped template under
synthetic promotion roots, receipts, SSH signatures, and dummy release-engine
bundle bytes. They prove rendering and local Git verification, not production
bundle promotion, signer approval, provider activation, or GitBook delivery.
