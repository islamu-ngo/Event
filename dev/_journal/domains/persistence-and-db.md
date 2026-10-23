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
- `src/Explore.Persistence/Repositories/ActorOwnershipReader.cs`
- `src/Explore.Persistence/Database/ProviderPrimitives/RelationalConnectionIdentity.cs`
- `tests/Event.Persistence.IntegrationTests/TenantIsolation/ActorOwnershipReaderTests.cs`
- PR: https://github.com/islamu-ngo/Event/pull/75
- Framework behavior reference: https://learn.microsoft.com/en-us/ef/core/performance/advanced-performance-topics

**Promotion Consideration**:
- [x] Stays in journal only; do not introduce another general connection wrapper without a demonstrated caller need.
