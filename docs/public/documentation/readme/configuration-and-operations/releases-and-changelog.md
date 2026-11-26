---
description: "Read release entries, consume digest-pinned .NET 10 containers, and verify signed Git tags, checksums, Cosign signatures, and SLSA attestations."
---

# Releases and Changelog

ISLAMU Event is in active pre-release development (`0.x`). The source-level release tooling (`ISLAMU.ReleaseEngineering`) and tag-driven release pipeline (`.github/workflows/release.yml`) are implemented; production SSH release signers remain intentionally unenrolled (`eng/release/trust/allowed-signers` fails closed) and the live release-candidate signing/registry rehearsal remains pending Project Steward approval before `v0.1.0`.

## Read an entry and plan an upgrade

The [Changelog](../../../changelog/README.md) is an entries-only chronological release ledger, not a setup manual. Each release produces three audience-focused sections inside `release-notes.md`:

- **Community Admin:** plain-language product features, fixes, and user-facing improvements.
- **Self-Hoster / Operator:** breaking operational changes, configuration contract changes (with `.env.example` secret/default values redacted), discovered EF Core migration ledger changes, and verified digest-pinned `.NET 10` container references.
- **Developer / API Integrator:** breaking API/contract changes and syntax-aware OpenAPI 3.1.1 / HAL `_links` contract deltas.

Automated contract diffs (`contract-diff.v1.json`) are conservative: unrecognized structural changes and asymmetric provider migrations are marked `review_required` for human review rather than claimed compatible. Always inspect every intervening release entry, capture a backup before upgrading, and follow the [backup and upgrade runbook](backup-restore-upgrade.md). Apply [environment variable changes](environment-variables.md) and [secret changes](secrets.md) through your chosen configuration and secret authority.

## Consume tagless digest-pinned container images

Every release builds four multi-arch (`linux/amd64`, `linux/arm64`) `.NET 10` container images:

- `islamu-event-standalone` (minimum standalone API + Blazor BFF/UI + SQLite)
- `islamu-event-api`
- `islamu-event-ui`
- `islamu-event-migration-service`

Release builds stage OCI indexes strictly **by digest** (`push-by-digest=true`, `name-canonical=true`) with **zero mutable or version registry tags** (`:latest`, `:vX.Y.Z`) assigned during staging. Always reference published release images by their canonical immutable digest from `release-images.v1.json` or `release-notes.md`:

```bash
docker pull ghcr.io/islamu-ngo/islamu-event-standalone@sha256:<digest>
```

When an optional external OCI registry mirror (such as Docker Hub `docker.io`, Quay `quay.io`, ATCR, or another OCI-compliant registry configured via `CONTAINER_REGISTRY`) is active for a release, its canonical `registry/namespace/image@sha256:<digest>` reference is listed alongside GHCR and signed individually.

## Verify a release (trust root, signatures, and checksums)

A complete release check has two distinct layers: **cryptographic authenticity** (proving who signed the tag and which workflow built the assets) and **file checksum consistency** (proving downloaded files were not corrupted). Always establish trust roots and authenticate `SHA256SUMS` before relying on `sha256sum --check`:

### 1. Authenticate the annotated SSH-signed Git tag (offline)

Never rely solely on `eng/release/trust/allowed-signers` from the same untrusted checkout or tag you are verifying—an attacker who supplies both a forged tag and checkout could replace the signer file inside that checkout. Obtain and authenticate the trusted `allowed-signers` file (or verify the operator's SSH key fingerprints) from an **independent trusted source** first (for example, an already-verified prior release commit, an independently reviewed protected `main` branch, or the out-of-band key fingerprints recorded in `eng/release/trust/rotation-history.md`), save it outside the untrusted checkout (for example `/path/to/trusted-allowed-signers`), and then verify:

```bash
git -c gpg.format=ssh \
  -c gpg.ssh.allowedSignersFile=/path/to/trusted-allowed-signers \
  verify-tag v<version>
```

You can also invoke the repository verifier with that independent signer file to confirm tag-object header equality and exact commit binding. Because `dotnet run` compiles and executes code from the current working tree—which `git verify-tag` does **not** authenticate—run this command **only** from one of:

- a checkout that you have already trusted, or
- a clean checkout of the authenticated target commit with no modified, untracked, or ignored files (verify with `git status --ignored`).

```bash
dotnet run --project eng/release/src/ISLAMU.ReleaseEngineering/ISLAMU.ReleaseEngineering.csproj --configuration Release -- \
  verify-release-tag --tag v<version> --target <full-commit-oid> \
  --allowed-signers /path/to/trusted-allowed-signers
```

### 2. Authenticate `SHA256SUMS` before verifying asset checksums

Running `sha256sum --check SHA256SUMS` alone proves only internal checksum consistency among downloaded files; it does **not** prove authenticity, because the signed Git tag covers the source commit tree rather than post-build generated release assets, and an attacker who replaces an asset could also replace `SHA256SUMS`.

Download all eight release assets (`contract-diff.v1.json`, `release-artifact-manifest.v1.json`, `release-context.v1.json`, `release-images.v1.json`, `release-notes.md`, `release-rendering-context.v1.json`, `SHA256SUMS`, and `SHA256SUMS.sigstore.json`) into one directory, **authenticate `SHA256SUMS` first** with Cosign (online against Sigstore trust roots), and **then** verify every file checksum offline:

```bash
# Step A: Cryptographically authenticate SHA256SUMS via its Sigstore OIDC bundle (online)
cosign verify-blob \
  --bundle SHA256SUMS.sigstore.json \
  --certificate-identity "https://github.com/islamu-ngo/Event/.github/workflows/release.yml@refs/tags/v<version>" \
  --certificate-oidc-issuer "https://token.actions.githubusercontent.com" \
  SHA256SUMS

# Step B: Verify every downloaded release asset matches the authenticated SHA256SUMS (offline)
sha256sum --check SHA256SUMS
```

In air-gapped environments where Cosign online verification is not available on the target host, run Step A (`cosign verify-blob`) on an online verification station before transferring the assets. Note that the operator's offline OpenSSH detached countersignature (`SHA256SUMS.sig`) is **not** one of the eight initial GitHub Release assets: it is created after release publication and committed to the repository at `docs/internal/releases/<version>/SHA256SUMS.sig` when the post-release changelog sync PR is merged. Once that commit is available, air-gapped operators can verify that repository-committed `SHA256SUMS.sig` offline against their independently trusted `allowed-signers` file under namespace `islamu-release-checksums` (`ssh-keygen -Y verify -f /path/to/trusted-allowed-signers -I <principal> -n islamu-release-checksums -s docs/internal/releases/<version>/SHA256SUMS.sig < SHA256SUMS`) before running `sha256sum --check SHA256SUMS`.

### 3. Verify container image signatures and GitHub SLSA attestations (online)

Online verification establishes cryptographic OIDC workload identity, Sigstore transparency-log inclusion, and GitHub SLSA v1.0 provenance (normally requiring network access to trust roots, container registries, and GitHub APIs):

1. **Verify every published container image digest reference listed in `release-artifact-manifest.v1.json`:**

   Signatures are repository-scoped (a GHCR signature does not authenticate a mirror reference in another registry). Verify the exact reference you pull:

   ```bash
   cosign verify \
     --certificate-identity "https://github.com/islamu-ngo/Event/.github/workflows/release.yml@refs/tags/v<version>" \
     --certificate-oidc-issuer "https://token.actions.githubusercontent.com" \
     <registry>/<namespace>/<image>@sha256:<digest>
   ```

2. **Verify GitHub SLSA v1.0 attestations for sealed files and GHCR OCI images:**

   ```bash
   gh attestation verify release-notes.md \
     --repo islamu-ngo/Event \
     --signer-workflow islamu-ngo/Event/.github/workflows/release.yml \
     --source-ref "refs/tags/v<version>" \
     --source-digest <full-commit-oid> \
     --deny-self-hosted-runners

   gh attestation verify "oci://ghcr.io/islamu-ngo/islamu-event-standalone@sha256:<digest>" \
     --repo islamu-ngo/Event \
     --signer-workflow islamu-ngo/Event/.github/workflows/_container-build.yml \
     --source-ref "refs/tags/v<version>" \
     --source-digest <full-commit-oid> \
     --predicate-type "https://slsa.dev/provenance/v1" \
     --deny-self-hosted-runners
   ```

SBOMs and SLSA provenance attest build origin and contents; they do not claim byte-for-byte reproducible container builds. A mutable web page or forge-generated source archive is never signed release authority. Published release assets are immutable; if a defect is found after publication, a new patch release and advisory are published rather than replacing sealed assets.

## Contributor privacy and credit adjustments

Authoritative signed release artifacts (`release-context.v1.json`, `release-rendering-context.v1.json`, `contract-diff.v1.json`, `release-images.v1.json`, `release-artifact-manifest.v1.json`, `release-notes.md`, `SHA256SUMS`, and the public Changelog ledger) are generated strictly offline and contain **no** contributor names, emails, or account handles.

Optional contributor acknowledgements (`@handle` and PR links, never raw emails) appear exclusively in the **mutable GitHub Release page body** appended after sealing. Any contributor may request edits to or removal of their acknowledgement at any time by contacting the maintainers or opening an issue—updating the mutable GitHub Release body never invalidates `SHA256SUMS`, Cosign signatures, or SLSA attestations.
