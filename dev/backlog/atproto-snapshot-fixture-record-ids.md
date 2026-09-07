<!-- ABOUTME: Quarantines invalid record IDs in the merged ATProto PostgreSQL snapshot fixtures. -->
<!-- ABOUTME: Preserves the four observed failure causes without weakening production validation or absorbing unrelated repairs. -->

# ATProto Snapshot Fixture Record IDs

Last Updated: 2026-09-07 Europe/Brussels

Status: open, separate federation test repair under PR #38's quarantine rule. Owner: federation/testing maintainer.

The post-PR38 PostgreSQL run of `AtprotoPdsSnapshotRepositoryTests` fails four cases: duplicate/older snapshot idempotency, missing-record tombstoning, preservation of a newer tombstone, and cancellation after reconciliation writes. Their accepted-item record IDs are respectively14,12,15 and14 characters; the existing production `IsValidTid` guard requires13. Reconciliation returns before writing, so the first three success assertions fail and the fourth never reaches its injected cancellation point.

The test and repository are unchanged from published develop `d6b1e17ed0f4a4e924705d4e0122be975a69dcce`. Current execution evidence is `/home/amir/.cache/agent-tmp/unicode-pr38-changed-persistence.log`:16total,12passed,4failed,0skipped,native exit2. All eight Unicode query cases pass; the four failures above are not Unicode failures. This is real-engine reproduction plus source parity, not an independently executed untouched-base suite.

Repair only the fixture record IDs using existing valid project-native TID examples or generation. Preserve idempotency/version/tombstone and post-write cancellation assertions; never relax production record-key validation or change expected success to rejection. Run the complete corrected PostgreSQL class and verify the cancellation path actually reaches its intended write boundary. No production/schema/Unicode compatibility change is needed by this finding.
