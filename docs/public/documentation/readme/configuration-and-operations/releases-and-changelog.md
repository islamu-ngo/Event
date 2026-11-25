---
description: "Read release entries and verify signed Git tags and release artifacts."
---

# Releases and Changelog

ISLAMU Event is in active pre-release development (`0.x`). Automated tag-driven container and artifact signing workflows are being introduced incrementally; no production release has shipped yet, and production SSH release signers are intentionally unenrolled (`eng/release/trust/allowed-signers` fails closed until Project Steward enrollment).

## Read an entry and plan an upgrade

The Changelog section is an entries-only release ledger, not a setup manual. Each dated entry identifies its version, stable or pre-release status, release line, summary, breaking changes, upgrade actions, categorized improvements, and verification references. Newest release date comes first; a maintenance release can precede a higher mainline version. Pre-releases remain visible after a stable release and are not recommendations for production.

Before upgrading, read every applicable intervening entry and its authoritative notes. Follow the release-specific migration, configuration, API, and security instructions; do not infer a safe rollback from a short summary. Capture your current revision, image digests, configuration, and data recovery point, then use the [backup and upgrade runbook](backup-restore-upgrade.md). Apply [configuration changes](environment-variables.md) and [secret changes](secrets.md) through their selected authorities, not through the changelog.

## Verify a release tag offline

Once production release signers are enrolled and a release is tagged, operators can verify the annotated SSH-signed Git tag directly offline with Git and the reviewed allowed-signers file:

```bash
git -c gpg.format=ssh \
  -c gpg.ssh.allowedSignersFile=eng/release/trust/allowed-signers \
  verify-tag v<version>
```

You can also invoke the repository release verifier to confirm the tag is an annotated SSH-signed tag bound to the expected target commit OID:

```bash
dotnet run --project eng/release/src/ISLAMU.ReleaseEngineering/ISLAMU.ReleaseEngineering.csproj --configuration Release -- \
  verify-release-tag --tag v<version> --target <full-commit-oid>
```

A mutable web page or forge-generated source archive is never signed release authority. Missing verification evidence or a moved/deleted tag is an integrity incident; stop and investigate rather than trusting the newest branch contents.

## Current status of release publication

- **Available now:** Offline verified `git-cliff` `2.14.2` toolchain pinning (`ensure-git-cliff` / `verify-tools`), direct SSH-signed annotated tag verification (`verify-release-tag`), release context/note preparation (`prepare`), and local public changelog synchronization (`sync-public-changelog`).
- **Planned in the release pipeline follow-up:** Automated tag-driven multi-arch `.NET 10` digest-only container staging, `SHA256SUMS` sealing, Sigstore Cosign blob and OCI signatures, GitHub artifact attestations, and online verification commands.
