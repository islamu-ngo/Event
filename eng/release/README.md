# Release Engineering

`ISLAMU.ReleaseEngineering` is a standalone `net10.0` console project for governed release tooling (`AllowUnsafeBlocks=false`, `RunAnalyzersDuringBuild=true`, zero compiler/analyzer warnings). It has no references to product projects.

## Verified `git-cliff` Toolchain (`2.14.2`)

`eng/release/toolchain.lock.json` pins `git-cliff` `2.14.2` across four platforms (`linux-x64`, `linux-arm64`, `osx-arm64`, `windows-x64`) with official release archive SHA-256 and extracted executable SHA-256 digests.

Provision the pinned binary explicitly (the sole network-capable command):

```bash
dotnet run --project eng/release/src/ISLAMU.ReleaseEngineering/ISLAMU.ReleaseEngineering.csproj --configuration Release -- ensure-git-cliff
```

Verify the locally provisioned binary offline without network access:

```bash
dotnet run --project eng/release/src/ISLAMU.ReleaseEngineering/ISLAMU.ReleaseEngineering.csproj --configuration Release -- verify-tools
```

`GitCliffToolchainStore` validates lock schema integrity, rejects archive path traversal and symlinks/hardlinks, verifies archive and executable SHA-256 digests before moving the executable into place, and verifies exact `git-cliff 2.14.2` `--version` output. Authoritative invocations pass `--config`, `--offline`, and `--no-exec` via argument arrays (never a shell) under a scrubbed deterministic environment (`GIT_CLIFF_OFFLINE=1`). Set `ISLAMU_RELEASE_TOOL_BUNDLE` to point to a pre-provisioned local directory or `ISLAMU_RELEASE_TOOL_CACHE` to override the default user cache root.

## Annotated SSH Tag Verification

Verify a signed annotated release or baseline tag directly against `eng/release/trust/allowed-signers` (or `--allowed-signers <path>`):

```bash
dotnet run --project eng/release/src/ISLAMU.ReleaseEngineering/ISLAMU.ReleaseEngineering.csproj --configuration Release -- \
  verify-release-tag --tag v0.1.0 --target <full-commit-oid> [--event-sha <github-sha>] [--signer <principal>]
```

`GitTagSignatureVerifier` enforces annotated tags (`cat-file -t == tag`), verifies the tag object's embedded `tag` header matches the ref name, binds target commit and optional event SHA, requires a non-empty allowed-signers file (failing closed while `eng/release/trust/allowed-signers` remains comment-only pending Project Steward enrollment), and invokes `git -c gpg.format=ssh -c gpg.ssh.allowedSignersFile=... verify-tag`.

## Release Preparation & Public Changelog Projection

Prepare normalized release context and release notes for `docs/internal/releases/<version>/`:

```bash
dotnet run --project eng/release/src/ISLAMU.ReleaseEngineering/ISLAMU.ReleaseEngineering.csproj --configuration Release -- \
  prepare docs/internal/releases/<version>
```

First-release baseline descriptors (`changelog-baseline-YYYY-MM-DD`) are verified directly through `GitTagSignatureVerifier` rather than synthetic Commit-B receipts.

Project signed local releases into `docs/public/changelog/README.md` (with atomic crash recovery and `--check` drift verification):

```bash
dotnet run --project eng/release/src/ISLAMU.ReleaseEngineering/ISLAMU.ReleaseEngineering.csproj --configuration Release -- \
  sync-public-changelog --publication-base https://github.com/islamu-ngo/Event/blob/ [--allowed-signers <path>] [--check]
```

## Change Workflow & Commit Preflight

```bash
dotnet run --project eng/release/src/ISLAMU.ReleaseEngineering/ISLAMU.ReleaseEngineering.csproj --configuration Release -- \
  preflight-commit /path/to/commit-msg --target develop

dotnet run --project eng/release/src/ISLAMU.ReleaseEngineering/ISLAMU.ReleaseEngineering.csproj --configuration Release -- \
  preflight-range --target develop --head HEAD
```

Supporting commands: `allocate-change-id`, `create-change`, `preflight-staged`, `rename-change`, `install-change-hooks`, `select-security-changes`, `select-build-test-changes`, and `verify-security-register`.

## Running Release Tests

Build once sequentially, then run tests with `--no-build`:

```bash
dotnet build eng/release/tests/ISLAMU.ReleaseEngineering.Tests/ISLAMU.ReleaseEngineering.Tests.csproj --configuration Release -m:1 -nr:false --verbosity quiet
dotnet test --project eng/release/tests/ISLAMU.ReleaseEngineering.Tests/ISLAMU.ReleaseEngineering.Tests.csproj --configuration Release --no-build --verbosity quiet
```
