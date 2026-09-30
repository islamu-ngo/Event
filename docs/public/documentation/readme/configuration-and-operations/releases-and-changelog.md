---
description: "Read release entries, verify signed evidence offline, and operate prospective changelog publication."
---

# Releases and Changelog

Governed changelog publication is **prospective and unverified** until operators
record protected-branch controls and observed live GitBook synchronization.
Repository configuration or a successful local render does not prove delivery.
Continue using the current manual release procedure until the governed cutover
is explicitly accepted.

## Read an entry and plan an upgrade

The Changelog section is an entries-only release ledger, not a setup manual.
Each dated entry identifies its version, stable or pre-release status, release
line, summary, breaking changes, upgrade actions, categorized improvements,
and verification references. Newest release date comes first; a recent
maintenance release can precede a higher mainline version. Pre-releases remain
visible after a stable release and are not recommendations for production.
The latest dated entry is not necessarily the highest stable version, and
neither establishes a support or end-of-life policy.

Before upgrading, read every applicable intervening entry and its authoritative
notes. Follow the release-specific migration, configuration, API and security
instructions; do not infer a safe rollback from a short summary. Capture your
current revision, image digests, configuration and data recovery point, then
use the [backup and upgrade runbook](backup-restore-upgrade.md). Apply
[configuration changes](environment-variables.md) and
[secret changes](secrets.md) through their selected authorities, not through
the changelog.

## Verify a release offline

1. Obtain the signed annotated version tag, its target Git objects, committed
   authoritative notes, final release evidence, artifact checksums, image
   digests and SBOM through the entry's durable references.
2. Obtain the independently promoted verifier bundle, promotion evidence and
   approved signer roots through the operator's trusted distribution channel.
   A verifier supplied only by the candidate being checked is not authority.
3. Use that bundle's documented verification interface to verify the annotated
   tag signature, authorized signer, full tag-object ID and target commit, then
   the notes and artifact checksums against final evidence. Publication CLI
   syntax is not a substitute for release verification.
4. Compare the entry's tag reference and authoritative-notes SHA-256 with those
   verified bytes. A matching page hash alone does not verify the signature or
   binaries. Keep the evidence locally so verification survives hosting outages
   and movement or deletion of the original development branch.

A mutable forge page, GitBook page or forge-generated source archive is not
signed release authority. Missing evidence or a moved/deleted tag is an
integrity incident; stop and request the retained evidence rather than trusting
the newest branch contents. Publication failure never invalidates an otherwise
verified signed release.

## Fresh publication setup for operators

This setup is an activation checklist, not a claim that the installation is
already configured.

1. Approve the publication cutover and retain evidence of the release trust
   bootstrap, immutable bundle promotion, signer roots and protected approvals.
   Use a dedicated protected publication runner with an independently reviewed
   launcher outside the documentation checkout and promoted bundle. Publication
   must execute authenticated promoted code, never code built from its checkout.
2. Retain a complete inventory of finally verified, human-authorized releases
   before publication dispatch. Preserve full tag-object IDs, disclosure
   authorization, final evidence and authoritative bundles beyond expiring CI
   artifacts. A list of release directories is not an authorized inventory.
3. Configure `docs/publication` as the protected acceptance branch containing
   the complete public documentation tree. Require review and validation;
   grant neither the publisher nor GitBook bypass on this branch.
4. Configure `docs/gitbook-sync` as a separate mutable mirror. Explicitly
   approve any GitBook app bypass only on this mirror. Never automatically
   merge GitBook write-back into protected acceptance or a release branch.
5. Verify the actual GitBook repository, mirror branch, Project directory
   `docs/public`, existing section/space mappings and initial Git-to-GitBook
   direction. Preserve existing mapping keys, especially Changelog `space-4`.
   Preview the complete site before switching branches.
6. Prove that the chosen bot identity and event strategy really run required
   checks on generated proposals. If token-created proposals do not trigger
   checks, keep publication pending and use reviewed validation/event handling;
   do not merge with missing checks or expand permissions as a workaround.
   Validate the exact reviewed proposal head before acceptance, and revalidate
   the complete inventory before mirror transport. Retain attempt and delivery
   receipts in protected durable storage, not only expiring CI artifacts.
7. Observe the accepted revision on the mirror and on the live site. Verify
   routes, version anchors and desktop/mobile rendering, including the chosen
   1 MiB UTF-8 page budget. Retain actual preview and delivery evidence; do not
   invent a GitBook change-request URL from a forge pull request.

Publication uses Git Sync, not a new forge or GitBook content API token.
Contributor previews must stay read-only and secret-free. Documentation-only
publication must not deploy the application.

## Rotation, reconciliation and recovery

Rotate the selected transport credential in its approved secret authority with
the same least-privilege scope. Pause publication writes, revoke the old
credential, install the replacement privately, verify branch permissions and
bot checks, then reconcile. Never log credentials or fall back to broader
permissions when a token is revoked.

After a publisher upgrade, independently approve any new trusted bundle,
verify its checksums and promotion evidence, rehearse a read-only projection
check against retained inventory, and review changed output before acceptance.
Never edit an already promoted bundle or historical signed notes.

| Situation | Operator action |
| --- | --- |
| Dispatch omitted or coalesced | Reconcile the complete retained inventory, including releases absent from the accepted page; the last dispatch is not the source of truth. |
| Concurrent maintenance/mainline proposals | Rebuild the union against the newly observed acceptance commit; a changed union needs fresh review. |
| Crash before acceptance | Check whether acceptance occurred, then retry the same verified inputs without duplicate entries or proposals. |
| Crash after acceptance, before receipt | Recover the accepted commit and digests; observe mirror/site delivery before claiming success. |
| GitBook write-back or manual mirror edits | Preserve drift evidence, compare with protected accepted content, and obtain explicit review for repair. Do not silently overwrite or reverse-merge. |
| Missing authorization, evidence or tag identity mismatch | Quarantine the proposal and restore trustworthy retained inputs; do not substitute current tags or omit history. |
| Outage, revoked credential or wrong sync branch | Keep delivery pending or failed with a bounded diagnostic; correct the transport/settings and reconcile. |

To disable publication, pause dispatch and mirror writes and revoke the
publication credential if needed. Preserve the inventory, accepted commit,
digests and receipts. Re-enabling requires the same approval and verification
boundaries, followed by reconciliation. Do not retag, rebuild binaries, move
stable `main`, or rewrite authoritative notes as a publication repair.
