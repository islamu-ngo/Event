# Restore AT Protocol snapshot reconciliation invariants

Status: quarantined inherited failure; separate federation repair required.

PR #40 verification reproduced four failures in the unchanged
`AtprotoPdsSnapshotRepositoryTests` on product base
`425e4b48343690094637dda860304f3bfb04a5cd`:

- `CancelAfterSaveChanges_CancelsReconciliationAndRollsBackCleanly`
- `OlderSnapshot_DoesNotOverwriteRecentTombstone`
- `DuplicateOrOlderSnapshots_AreIdempotentAndPreserveHigherVersions`
- `MissingInboundRecords_AreTombstonedAndProjectionsRemoved`

The base class completed eight cases: four passed and four failed. The same four
failures recur in the finalized 103-case Federation partition on the feature
branch. This establishes inheritance, not the underlying root cause, and does
not make a required integration check green. No assertions were disabled.

Reproduce using the Release Persistence test runner and
`--treenode-filter "/*/*/*AtprotoPdsSnapshotRepositoryTests/*"`
with `--minimum-expected-tests 8 --maximum-parallel-tests 1`, a working
Testcontainers runtime, and the existing approved fixture secret authority.

Acceptance criteria:

- Identify the repository or fixture cause through the real database seam.
- Preserve rollback after cancellation, monotonic snapshot/tombstone versions,
  and removal of projections for records absent from an authoritative snapshot.
- Pass all eight owning cases and the adjacent federation runtime partition.
- Retain tenant boundaries; do not weaken assertions, skip cases or infer success
  from stale snapshot data.

Local provenance: `.omo/evidence/pr40-snapshot-quarantine.md`; baseline TRX
`/home/amir/.cache/agent-tmp/pr40-snapshot-baseline/final.trx`, SHA-256
`92f589ce43ceb82507e6bfa4d6839bbad78106def0f54fb91af5dd9a33b241bf`.
