<!-- ABOUTME: Domain journal for persistence, EF Core, database migrations, and schema patterns. -->
<!-- ABOUTME: Captures durable findings, pitfalls, and decisions in Explore.Persistence and database stores. -->

# Persistence & Database Knowledge Ledger

> **Scope**: `Explore.Persistence`, EF Core configurations, migrations, query filters, PostgreSQL, SQLite, and Quartz schema.

---

## 1. Architectural Decisions

- **Clean-room adoption of third-party ADO schemas**: Treat external database schemas as interoperability interface facts rather than copying third-party SQL/DDL. Author DDL independently with project-native guards, parameterized tokens (e.g. `{prefix}`), and non-destructive operations safe for idempotent startup execution.
- **Auditing and Soft Delete**: Auditable entities use `CreatedAt/By` and `UpdatedAt/By`. Soft-deletable entities use `IsDeleted` and the global query filter `SoftDelete`.
- **Numeric Version Concurrency on PostgreSQL**: Use an application-managed integer `Version` token (or UUIDv7 concurrency stamp) for optimistic concurrency on PostgreSQL rather than SQL Server-style `byte[] RowVersion`.

---

## 2. Technical Insights & Patterns

- **EF Core tracking optimization**: Repositories return entities; projection and DTO mapping happen in handlers. Use `AsNoTracking()` on read-only queries.
- **DTO EventId propagation requires repository include paths**: When mapping child entities (e.g., registrations, session agenda items) for HAL or authorization, AutoMapper expressions like `src.EventSession.EventId` require repositories to explicitly include parent entities on all read paths.
- **EF migrations in dirty working trees**: `dotnet ef migrations add` snapshots the entire current model state. In dirty trees with concurrent model changes, inspect generated migration files and prune unrelated operations before applying.
- **EAV Projection Updaters**: EAV projection metrics extend `Explore.Projections` counters (`explore.projections.inline_updates_total`, `dirty_scope_skips_total`) with bounded labels (`tenant_id`, `projection_type`, `operation`). Never emit dynamic property namespaces, definition IDs, or session IDs as metrics tags.

---

## 3. Failed Approaches & Lessons

[2026-11-02 Europe/Brussels] — Existence checks must not acquire private profile data

**Context**: Recognition erasure runs with a narrow database purpose that may
fence a user identity while remaining unable to read its private profile.

**Root Cause**: An unused `User` returned by `SingleOrDefaultAsync` brought in
the configured `User.Pii` auto-include. The workflow needed only to establish
that its already-fenced subject existed. Entity materialization added a private
read that was neither required nor authorized by that purpose.

**Resolution**: Commit `8b9d3aac41` uses `AnyAsync` on the same erasure-purpose
query and subject predicate. The original identity transaction, ordered native
row fences, justified filter bypass and missing-subject refusal remain intact.
Both original PostgreSQL settlement paths passed separately: normal completion
with replay and injected-outbox rollback, with their private-read denials retained.

**Why This Matters for Future Work**: Auto-includes affect permission requirements
as well as query cost. When a workflow only requires existence, check existence
under its original filters and concurrency guards; do not grant private-table
access to make an unnecessary aggregate read succeed.

**References**:
- `src/Explore.Persistence/Repositories/ModerationRecognitionErasureRepository.Authority.cs` — `AcquireSubjectAsync`
- `src/Explore.Persistence/Configurations/UserConfiguration.cs` — `User.Pii` auto-include
- `tests/Event.Persistence.IntegrationTests/Privacy/PostgresSubjectPrivacyErasureAuthorityTests.cs`
- `docs/internal/PRIVACY_ERASURE.md`

---

- **Hand-editing EF Migrations**: Banned across the repository. If a migration is wrong, fix the entity configuration or seeder, delete the unapplied development migration, and regenerate it with `dotnet ef migrations`.
- **Destructive Statements in Startup DDL**: Adding `DROP` or `TRUNCATE` statements to startup schema initializers breaks live environments. All startup initializers must use `CREATE TABLE IF NOT EXISTS` or idempotent metadata checks.

[2026-09-06 Europe/Brussels] — A retried delete can exceed its declared batch budget

**Context**: ATProto transient cleanup promises at most five batches of 500 rows per table. Verification used real PostgreSQL with retry-enabled provider configuration and an injected lost acknowledgement after deletion.

**Symptom / Observation**: Both fault cases initially completed instead of surfacing the injected `NpgsqlException`. Provider retry repeated a destructive operation whose first execution had already committed.

**Root Cause**: A deferred expired-ID query inside `ExecuteDeleteAsync` is re-evaluated when the execution strategy retries. After the first deletion commits, another attempt can select a fresh batch. A bounded result count therefore does not prove a bounded number of physically deleted rows.

**Resolution**: Materialize a capped immutable ID set before deletion, then use the existing provider-primitive boundary to issue one parameterized, non-retrying destructive statement. Ambiguous acknowledgement fails the pass; the next scheduled pass resumes cleanup. `dotnet run --project tests/Event.Persistence.IntegrationTests --configuration Release -- --treenode-filter "/*/*/*AtprotoTransientCleanupServiceTests/*"` passed all five real-provider cases, including both lost-acknowledgement variants and the sweep budget. Failed partial work is not published as a completed row total.

**Why This Matters for Future Work**: A batch limit and an execution-strategy retry policy are separate contracts. When physical work must remain bounded despite ambiguous acknowledgement, review what is selected on each attempt—not just the statement's returned count. Do not generalize this non-retry rule to ordinary recoverable reads or unrelated transactional business operations.

**References**:

- `src/Explore.Persistence/Database/ProviderPrimitives/AtprotoTransientCleanupDelete.cs`
- `src/Explore.Persistence/Repositories/AtprotoTransientStoreRepository.cs`
- `src/Explore.Persistence/Repositories/AtprotoTransientAssertionReplayRepository.cs`
- `tests/Event.Persistence.IntegrationTests/Repositories/AtprotoTransientCleanupServiceTests.cs`
- `docs/internal/adr/ADR-014-atproto-session-trust-bridge.md`

**Promotion Consideration**:

- [x] Stays in journal as fault-injection evidence; the bounded cleanup contract is recorded in ADR-014.

---

[2026-09-24 Europe/Brussels] — Joined DID columns need the same portable ordinal collation

**Context**: While closing the five-provider event-resource policy matrix, SQL Server failed four otherwise-passing public-event eligibility cases after the generated resource migrations.

**Symptom / Observation**: SQL Server reported `Cannot resolve the collation conflict between "SQL_Latin1_General_CP1_CI_AS" and "Latin1_General_100_BIN2" in the equal to operation.` The corresponding selected cases passed on the other four engines.

**Root Cause**: The read joined `AtprotoRecord.Did` to `AtprotoIdentity.Did`. The identity column used `UsePortableOrdinalAscii()` while the record column inherited the database default. SQL Server cannot compare those differently collated columns without an explicit compatible policy.

**Resolution**: Configure the record DID with the same portable ordinal policy and regenerate all four native EF histories, including the shared MySQL/MariaDB catalog. The selected class then passed 9/9 on SQL Server and on each other engine; every catalog reported no pending model changes. No migration or snapshot was hand-edited.

**Why This Matters for Future Work**: Column equality in a query has a cross-table collation contract. Provider-neutral test successes do not establish that a SQL Server join between a default-collated and an ordinal-collated identity will work. Align model configuration at both join endpoints, regenerate unapplied migrations, then exercise the actual provider before treating a caller-level error as an authorization decision.

**References**:

- `src/Explore.Persistence/Configurations/Entities/AtprotoRecordConfiguration.cs`
- `src/Explore.Persistence/Configurations/Entities/ActorIdentityConfiguration.cs`
- `tests/Event.Persistence.IntegrationTests/Database/PrimaryDatabaseProviderBehaviorContractTests.cs`
- `docs/internal/adr/ADR-033-governed-event-resource-delivery.md`

**Promotion Consideration**:

- [x] Stays in the persistence journal as demonstrated provider behavior.

---

[2026-10-15 Europe/Brussels] - Same-name index replacement must retain foreign-key support

**Context**: Final security programme assurance exercised the complete application migration history on actual SQL Server, MariaDB and MySQL engines, including rollback to an empty database and reapplication.

**Observation**: MySQL rejected dropping `ix_ai_tool_executions_proposed_action_id` because InnoDB still needed that leading-column index for the proposed-action foreign key. The generated migration recreated the index later under the same name, so moving its creation before its drop would instead collide on the index name.

**Resolution**: `ConfigurableMySqlMigrationsSqlGenerator` creates a temporary nonunique index on `proposed_action_id` before dropping the original, generates the intended replacement with its actual uniqueness, then removes the temporary index. This applies in both directions. Foreign-key enforcement stays enabled, and no generated migration or snapshot is hand-edited.

**Evidence**: `Repair-MySql-ForeignKeyIndexCoverage.trx` passed both generated-SQL ordering cases. `Repair-ServerProvider-MigrationLifecycle.trx` passed all three actual server-provider round-trips with zero skips. The latter used:

```bash
dotnet test --project tests/Event.Persistence.IntegrationTests/Event.Persistence.IntegrationTests.csproj \
  --configuration Release --no-build --verbosity quiet -- \
  --maximum-parallel-tests 1 \
  --treenode-filter "/*/*/SqlServerApplicationInitialLifecycleTests/GeneratedInitial_AppliesRollsBackAndReapplies" \
  --minimum-expected-tests 3 --detailed-stacktrace --report-trx \
  --report-trx-filename Repair-ServerProvider-MigrationLifecycle.trx
```

**Reuse**: Distinguish different-name index replacement, which can create the successor first, from same-name replacement, which needs temporary FK coverage. Verify both Up and Down on the real engine; a model-valid final schema does not prove each intermediate DDL operation is legal.

---

[2026-10-22 Europe/Brussels] — Balance EF openings independently of physical connection state

**Context**: Replacing full-profile actor ownership reads with a selected-tenant EF projection in PR #75 exposed two connection-lifetime failures.

**Symptom / Observation**: An interceptor rejected readiness after the physical connection opened, leaving it open. Separately, terminating only the test's original PostgreSQL query backend let the same context recover successfully, but its second read finished with the connection Open instead of Closed. Both failures were reproduced before their corresponding corrections.

**Root Cause**: Physical connection state and EF's successful explicit-opening count represent different ownership facts. Capturing the connector only after opening misses interceptor failure. Skipping a balancing EF close merely because the connector broke retains an opening count that leaks into the next operation.

**Resolution**: `ActorOwnershipReader` witnesses the original connector before opening, preserves interruption as a failed-read condition and separately records reopening. A successful EF opening is balanced on that same owner, including a closed connector that has not reopened. A failed initial opening uses physical cleanup only when the original backend is still witnessed. Replacement context, connector or transaction references never qualify; primary and cleanup errors remain observable.

The isolated Persistence Release build passed, followed by all 28 PostgreSQL reader/permission cases (0 failed/skipped, 318.930s, exit0) using:

```bash
dotnet tests/Event.Persistence.IntegrationTests/bin/Release/net10.0/Event.Persistence.IntegrationTests.dll \
  --treenode-filter '/*/*/(ActorOwnershipReaderTests)|(ActorOwnershipReadPermissionTests)/*' \
  --minimum-expected-tests 28 --progress off --maximum-parallel-tests 1
```

**Why This Matters for Future Work**: Cleanup must discharge the operation's actual EF ownership without treating a closed or replacement physical connection as proof of the same thing. Verify recovery on the same context, not only the first rejected invocation. Do not generalize these PostgreSQL receipts into unexecuted provider evidence.

**References**:
- `src/Explore.Persistence/Services/ActorOwnershipReader.cs`
- `src/Explore.Persistence/Database/ProviderPrimitives/RelationalConnectionIdentity.cs`
- `tests/Event.Persistence.IntegrationTests/TenantIsolation/ActorOwnershipReaderTests.cs`
- PR: https://github.com/islamu-ngo/Event/pull/75
- Framework behavior reference: https://learn.microsoft.com/en-us/ef/core/performance/advanced-performance-topics

**Promotion Consideration**:
- [x] Stays in journal only; do not introduce another general connection wrapper without a demonstrated caller need.

---

[2026-10-31 Europe/Brussels] — Canonical migration contexts can lack a bound null-tenant lease

**Context**: Native PostgreSQL benchmark setup exercised the real migration coordinator while executing the approved testing-assurance follow-up. This was a diagnostic invocation, not an admitted benchmark or passing migration check.

**Symptom / Observation**: After container startup and purpose-login provisioning, setup failed with `System.InvalidOperationException` at `ExploreDbContext.RequireBoundTenantLease`, called by `PostgresCategoryReadPermissions.ApplyAsync` from `ExploreDatabaseMigrator.MigrateCoreAsync`. Value-free failure-wrapper metadata retained the call chain without the original exception message, credentials or argument values.

**Root Cause**: `MigrateAndSeedAsync` constructs a new context without binding its tenant lease. `MigrateCoreAsync` uses that context directly when the model and target schemas match. The remapped-schema branch instead creates a namespace execution context whose factory explicitly binds the lease. Thus successful remapped-schema fixtures do not establish that canonical migration startup satisfies the same installer admission. A null tenant and an explicitly bound null-tenant lease are different states: `RequireBoundTenantLease` requires the immutable ownership snapshot, not a fabricated tenant identifier.

**Resolution (verified November 2)**: Commit `435159ba56` uses the existing namespace execution context for canonical as well as remapped native installation, retaining lease validation and database permissions without binding the borrowed caller. The canonical native regression passed after the intended `tenant_lease_not_bound` Red. A separate physical-default regression passed and was committed in `89105f1602`; neither receipt substitutes for the final provider matrix. A fixture-only schema change would have hidden the startup defect. No lease guard or generated migration was weakened, and the temporary fresh-connection diagnostic setting was removed.

**Why This Matters for Future Work**: Exercise both canonical and remapped migration paths before accepting installer readiness. A fixture that always remaps its schema can bind ownership through a helper that ordinary startup never calls. Do not replace an immutable lease requirement with an ambient null check or treat this setup failure as benchmark measurement.

**References**:
- `src/Explore.Persistence/ExploreDbContext.cs:63-87`
- `src/Explore.Persistence/Schema/ProviderPrimitives/ExploreDatabaseMigrator.cs:73`
- `src/Explore.Persistence/Schema/ProviderPrimitives/ExploreDatabaseMigrator.cs:139-170`
- `src/Explore.Persistence/Schema/ProviderPrimitives/ExploreDatabaseMigrator.cs:350-351`
- `src/Explore.Persistence/Schema/ProviderPrimitives/PostgresCategoryReadPermissions.cs:55`
- `tests/Event.Benchmarks/Api/PostgreSqlApiBenchmarkHostFactory.cs:102-114`
- `docs/internal/testing/DISTRIBUTED_ASSURANCE.md`
- Diagnostic-wrapper commit: `1f22c74618932f75ec8e9d09faafbe28d9db002b`; native diagnostic invocation `2026103109`, exit 2, no admitted result.

**Promotion Consideration**:
- [x] The verified migration ownership mechanism is documented in the paired migration guides; this entry retains the diagnostic distinction between canonical and remapped paths.

[2026-10-31 Europe/Brussels] — Receipt issuance rejects its native recovery advancement

**Context**: Native SQLite grant-settlement tests failed during ordinary intake setup, before their settlement assertions. Test-only probes observed the real issuer and committer without changing production code, native return values or assertions.

**Symptom / Observation**: Both `ReadGrantSettlementCommitsOnlyItsNativeRecoveryAdvancement` variants returned a null submission receipt after transaction startup, receipt save and successful `RecheckIssuanceAsync`. Caller/tenant checks were valid, the tracker was clean, no ambient transaction existed and receipt lifetime checks were valid. The tracker contained an unchanged target source, target receipt and `ModerationRecoveryStoreState`.

**Root Cause**: `PrepareModerationRecoveryAsync` adds the canonical recovery advancement during the native save. The receipt committer's post-save `HasForeignIssuanceWork` recognizes only its target source and receipt; its default branch therefore rejects that context-owned recovery state as foreign work. The neighboring read-grant settlement boundary already distinguishes the exact owned recovery object, coordinates, installation slot and live transaction through `IsOwnedModerationRecoveryAdvancement`. A blanket recovery-type allowance would lose those ownership fences.

**Resolution (verified November 2)**: Commit `dfb9b9b9c6` repaired the intake committer's recovery-custody check, and `89397eec89` retained receipt-token non-publication when publication authority changes. Exact recovery ownership remains required; arbitrary rows of the recovery type are not admitted. Seventeen native case receipt/read-grant checks passed alongside four lifecycle controls in `lifecycle-and-receipt-native` (21 passed, zero skipped, 6:32.032). That receipt does not qualify the separate adjudicated-notice settlement family. Diagnostic-only progress output is being removed from the qualified case fixture paths before final candidate verification.

**Why This Matters for Future Work**: A clean change tracker is not an empty tracker. Native save boundaries can introduce legitimate unchanged coordination rows, so closed write-set checks must recognize invocation-owned side effects without admitting arbitrary rows of the same type. Diagnose the first failed setup stage before modifying the settlement operation that the test has not reached.

**References**:
- `src/Explore.Persistence/Moderation/ModerationCaseIntakeCommitter.cs:91-99`
- `src/Explore.Persistence/Moderation/ModerationCaseIntakeCommitter.cs:285-292`
- `src/Explore.Persistence/ExploreDbContext.ModerationRecovery.cs:19-31`
- `src/Explore.Persistence/ExploreDbContext.ModerationRecovery.cs:34-90`
- `src/Explore.Persistence/ExploreDbContext.ModerationReadGrantSettlement.cs:48-50`
- `tests/Event.Persistence.IntegrationTests/Moderation/ModerationCaseNativeProofTests.ReadReceiptIssuance.cs:301-335`
- Candidate: `034625d60b3be687ff29856d5aee9048de073e0f`; temporary test-only diagnostic `mon_B5JNJXFV1KGT4T2K`, two failed/zero passed or skipped, 45.898 seconds, no settlement acceptance.

**Promotion Consideration**:
- [x] Stays in journal only until exact receipt/recovery ownership repair and native regressions are verified.

**Follow-up invariant evidence (2026-10-31)**: Four native SQLite recovery-tampering cases executed before any product repair. Changed, replaced and deleted state are refused with full rollback; detached state incorrectly returns a real issued receipt. Removing the row from the tracker evades the foreign-work check, so simply accepting the recovery type would address the normal-path failure without establishing lost-advancement refusal. Require both positive ownership and rejection of missing ownership. Targeted Release build passed (0 errors, 1047 warnings); the native slice is 3 passed/1 failed/0 skipped, 88.593 seconds, monitor `mon_Q4R8KVPNFGWBDQ4X`. The failed regression remains uncommitted; no repaired-product claim.

**Review qualification (2026-10-31)**: Two independent bounded source reviews agree on both refusal mechanisms; the neighboring read-grant settlement's positive recovery ownership and lost-advancement checks were also independently read in the main session. Detachment after save does not undo SQL already executed or demonstrate persisted corruption. The proven defect is acceptance after loss of the existing transaction-bound ownership witness. Current passing changed/replaced/deleted cases can be incidental rejection by foreign-work or dirty-state checks; exact custody still needs positive controls, clean-state coordinate tampering, transaction/object substitution and legitimate persisted-source reuse coverage after the approved repair.

---

[2026-10-31 Europe/Brussels] — Model-only authority comparison separates mapping from drift

**Context**: A PostgreSQL privacy test failed with `PendingModelChangesWarning` before its protected-work assertions. Replacing its custom storage schema with the canonical schema did not resolve the failure.

**Symptom / Observation**: The real provider's `IMigrationsModelDiffer` reported identical missing recovery columns and a check constraint for both canonical and custom storage inputs. No database connection, credentials or container was needed to reproduce that difference.

**Root Cause**: The co-located authority migrator deliberately uses the canonical model schema while the namespace machinery selects physical storage. Its separate generated snapshot lacked already-configured recovery coordinates. Changing the storage namespace therefore could not repair the stale catalog.

**Resolution**: Commit `4c01baf17639948675bd57e550ba0502bc4a1b63` adds the EF-generated correction and snapshot, with paired upgrade guidance. Both model comparisons passed, followed by both populated PostgreSQL predecessor upgrades and the protected-work case. These are separate receipts, not a full provider-matrix result. The exact model-only command was:

```bash
timeout --signal=INT --kill-after=15s 360s dotnet tests/Event.Persistence.IntegrationTests/bin/Release/net10.0/Event.Persistence.IntegrationTests.dll --treenode-filter "/*/*/ExploreDbContextModelProviderTests/CoLocatedAuthorityMigratorModelMatchesItsSnapshot" --minimum-expected-tests 2 --maximum-parallel-tests 1 --timeout 300s --report-trx --results-directory "$TMPDIR/pr79-assurance-intake/authority-recovery-migration-model-green"
```

**Why This Matters for Future Work**: Compare the selected design-time model with its own initialized snapshot before attributing a migration warning to schema remapping. Keep model parity distinct from physical DDL, backfill and lifecycle evidence. A model-only pass cannot certify those native behaviors.

**References**:
- `tests/Event.Persistence.IntegrationTests/Database/ExploreDbContextModelProviderTests.cs`
- `tests/Event.Persistence.IntegrationTests/Migrations/PostgresMigrationAuthorityIsolationTests.cs`
- `src/Explore.Persistence/Database/PrimaryDatabaseProviderComposition.cs`
- `src/Explore.Persistence/Migrations/CoLocatedPrivacyErasureAuthority/20261031102545_RestoreCoLocatedAuthorityRecoveryCoordinates.cs:13`
- `docs/internal/PRIVACY_ERASURE.md`
- `docs/internal/BACKUP_RESTORE_UPGRADE.md`

**Promotion Consideration**:
- [x] Stays in journal as a diagnostic lesson; implementation and downgrade limits are documented in the referenced technical and operator guides.

---

[2026-11-02 Europe/Brussels] — SQLite expression defaults do not backfill rebuilt rows

**Context**: Native startup qualification exposed an embedded erasure-authority catalog missing its model's recovery coordinates. The correction had to preserve retained facts, sequence floors and existing store identities.

**Symptom / Observation**: An empty-store upgrade passed while populated upgrades failed. A direct addition with a UUID expression default failed with `Cannot add a column with non-constant default`. Splitting preparation from restoration then failed with `NOT NULL constraint failed: ef_temp_ie_authority_counter.store_id`.

**Root Cause**: The generated rebuild explicitly supplied the old null `store_id` in its INSERT/SELECT. A column default cannot replace an explicitly supplied null. The actual SQLite 3.53.3 runtime supported the expression; this was a missing data backfill, not a runtime-version defect.

**Resolution**: Generate a nullable preparation and constrained restoration through EF. The authority-specific SQL generator inserts a null-only backfill before the rebuild. Nonnull identities remain untouched, and final constraints reject invalid identities or nonpositive counters. Physical-file tests passed for empty/populated stores, preserved identities and facts, fresh-context reopening, and separately for all three invalid-coordinate cases. The initial three-case command was `dotnet tests/Event.Persistence.IntegrationTests/bin/Release/net10.0/Event.Persistence.IntegrationTests.dll --treenode-filter "/*/*/EmbeddedAuthorityMigrationRecoveryTests/*" --minimum-expected-tests 3 --maximum-parallel-tests 1 --timeout 60s --progress off --report-trx --results-directory "$TMPDIR/pr79-assurance-intake/embedded-backfill-native"`. Later reopen and rejection receipts are separate evidence; arbitrary interruption inside SQLite rebuild phases remains unqualified.

**Why This Matters for Future Work**: Inspect generated copy SQL as well as the model default. Empty-database success and snapshot parity cannot establish a populated upgrade. Fix the source model or a narrowly scoped generator mechanism; never patch generated migrations by hand.

**References**:
- `src/Explore.Persistence/Privacy/ErasureAuthority/ProviderPrimitives/EmbeddedPrivacyErasureMigrationsSqlGenerator.cs`
- `src/Explore.Persistence/Privacy/ErasureAuthority/Configurations/EmbeddedPrivacyErasureCounterConfiguration.cs`
- `tests/Event.Persistence.IntegrationTests/Privacy/EmbeddedAuthorityMigrationRecoveryTests.cs`
- `docs/internal/PRIVACY_ERASURE.md`

**Promotion Consideration**:
- [x] Stays in the persistence journal; the concrete upgrade and downgrade contract belongs to the paired privacy guides.

---

[2026-11-02 Europe/Brussels] — Enrollment reads must retain their writer context

**Context**: Recognition lifecycle initialization read the erasure counter while its original serialized enrollment transaction remained open.

**Symptom / Observation**: Shared-cache SQLite blocked the second connection on `ie_authority_counter`. A physical-file regression instead observed sequence 0 after sequence 1 had been appended inside enrollment. Both failures came from the same ownership mistake.

**Root Cause**: `GetStateAsync` opened a new authority context even while `_identityGate` held the original writer context. `ReadAfterAsync` already reused that owner correctly. Depending on SQLite storage mode, the extra reader either waits on the writer or sees an older committed snapshot.

**Resolution**: Route active-enrollment state reads through the same context, preserving cancellation, no-tracking reads and the missing-counter corruption check. The reader does not save, commit or dispose the owner. The native regression passed after failing with expected1/found0; it also throws the original abort and proves both counter and fact rollback. It executed in the selected two-case command using `--treenode-filter "/*/*/*/(EnrollmentStateReadsObserveOriginalTransactionWithoutCommittingIt)|(RecognitionLifecycleRejectsUnacknowledgedNewRuntimeAndSecondStandaloneOwner)" --minimum-expected-tests 2 --maximum-parallel-tests 1 --timeout 180s --progress off --report-trx --results-directory "$TMPDIR/pr79-assurance-intake/authority-state-owner-green"`. That receipt had one state-read pass and a separate lifecycle fixture failure; the latter was subsequently qualified under its explicit trusted-maintenance context.

**Why This Matters for Future Work**: A fresh context is not automatically safer for a read. Inside a serialized workflow, retain the original transaction's visibility and lifetime; raising timeouts cannot repair an incorrect owner or stale snapshot.

**References**:
- `src/Explore.Persistence/Privacy/ErasureAuthority/Repositories/EmbeddedPrivacyErasureAuthorityRepository.cs`
- `src/Explore.Persistence/Privacy/ErasureAuthority/Repositories/EmbeddedPrivacyErasureAuthorityRepository.IdentityFence.cs`
- `tests/Event.Persistence.IntegrationTests/Privacy/EmbeddedPrivacyErasureRecoveryTests.cs`

**Promotion Consideration**:
- [x] Stays in the persistence journal as an ownership diagnostic; no global query-filter or retry-policy exception is introduced.

---

[2026-11-02 Europe/Brussels] — Recognition capture, acknowledgement recovery and hidden references have different proof boundaries

**Context**: Native erasure qualification reached capture persistence, retained-fact binding and returning-account enrollment in succession. The original receipt and retention ceiling had to survive every retry.

**Symptom / Observation**: Fresh generated-key capture children were tracked as Modified and attempted to update missing rows. Once insertion succeeded, binding repeated the pre-append key restore-floor check against the intentionally advanced authority head. Recovering a lost acknowledgement then exposed a separate tenant enrollment refusal: its continuity binding referenced a capture row intentionally hidden by tenant query filters.

**Resolution**: Explicitly add newly minted capture children after lifecycle enlistment. Keep pre-append qualification before append; binding authenticates the exact retained fact and rechecks material under its original transaction and deadline. An invocation-local observed fact is only a recovery lookup hint. A fresh native gate proves its committed recovery coordinates before repairing the original primary binding. Missing original obligations remain a refusal. Independent primary transactions commit inside that gate; shared SQLite completion belongs to the authority owner.

Tenant enrollment retains its runtime purpose. Its one hidden capture reference uses a permit for the exact qualified binding object, original context/tenant/physical transaction and finite qualification deadline. Capture ownership before the first await. Check the permit before the generic Added-parent shortcut and revoke it when staging fails. Copying the object or adding its parent cannot recreate qualification; ordinary tenant read filters remain intact.

**Verification**: Commit `da046e6f00` includes native original-material/lost-acknowledgement controls, eight tenant-reference attacks, and seven recovery ownership cases using real SQLite writes and commits. The ownership cases prove rollback after post-save or pre-commit failures; the shared-store positive observes exactly one outer authority commit on the same physical transaction. Review tightened negative assertions to identify the refused capture FK and the explicit replaced-owner error, avoiding false positives from unrelated validation or disposed-transaction errors. Exact commands and receipts are recorded in the PR79 execution ledger; no startup-recovery or final provider-matrix claim follows from these tests.

**Why This Matters for Future Work**: New capture, binding an appended fact, and recovering committed work need different evidence. Retrying the whole acquisition path can invalidate legitimate intermediate state. Broad filter bypasses hide ownership mistakes; narrowly qualified references preserve tenant scope without making hidden rows generally readable.

**References**:
- `src/Explore.Persistence/Repositories/ModerationRecognitionErasureRepository.Recovery.cs`
- `src/Explore.Persistence/ExploreDbContext.RecognitionContinuityAuthority.cs`
- `tests/Event.Persistence.IntegrationTests/Moderation/ModerationRecognitionRecoveryOwnershipTests.cs`
- `tests/Event.Persistence.IntegrationTests/Moderation/ModerationRecognitionTenantReferenceTests.cs`
- `docs/internal/PRIVACY_ERASURE.md`

**Promotion Consideration**:
- [x] Diagnostic lesson stays here; the implemented privacy contract belongs to the paired internal and public privacy guides.

---

[2026-11-05 Europe/Brussels] — Fixture grants can hide missing canonical enrollment

**Context**: Real Split startup failed public directory requests although native fixture paths had the required read capability.

**Symptom / Observation**: PostgreSQL returned `42501: permission denied for function tenants_public_directory`. The shared fixture explicitly granted the directory read role to Global; canonical migration installed the capability without enrolling the serving login.

**Root Cause**: The fixture installed more authority than the production migration path. A test using that fixture alone could validate query behavior while missing deployment enrollment.

**Resolution**: Enroll the declared Global login in the existing read-only directory role with INHERIT enabled and SET/ADMIN disabled. Do not add table grants or change the disclosure primitive. A fresh canonical installation now invokes the actual reader. The behavior test also proves revocation denies access, exact restoration recovers, inactive tenants stay absent, ambiguous domains return no tenant, and Runtime cannot read private data. Both cases passed:

```bash
dotnet run --project tests/Event.Persistence.IntegrationTests/Event.Persistence.IntegrationTests.csproj --configuration Release --no-restore -- --treenode-filter '/*/*/(TenantLookupReadOwnershipTests|PostgresMigrationAuthorityIsolationTests)/(LookupReadsActiveDirectoryAndResolvesDomainsWithoutPromotingRuntimeCaller|CanonicalMigrationOwnsItsExecutionLeaseWithoutBindingTheCaller)' --minimum-expected-tests 2 --maximum-parallel-tests 1
```

**Why This Matters for Future Work**: Compare fixture enrollment with canonical startup before changing grants. Functional reads against a privileged fixture do not establish that a deployed purpose can acquire the same capability. Focused success still does not certify whole-host startup.

**References**:
- `src/Explore.Persistence/Schema/ProviderPrimitives/ExploreDatabaseMigrator.cs`
- `tests/Event.Persistence.IntegrationTests/Fixtures/PostgreSqlContainerFixture.cs:175`
- `tests/Event.Persistence.IntegrationTests/Migrations/PostgresMigrationAuthorityIsolationTests.cs:217`
- `tests/Event.Persistence.IntegrationTests/Health/TenantLookupReadOwnershipTests.cs`

**Promotion Consideration**:
- [x] Stays in the journal; the production enrollment is documented in the security model.
