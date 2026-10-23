# Transaction-bound email policy authority

## Intended native Red packet

`EmailDeliveryPolicyNativeAuthorityTests.OwningTransactionSeesForeignOverridesLocksRevisionAndRollback`
has two cases: generated ordinary Runtime bound to tenant A and generated ordinary
Global with an absent tenant lease. Both use the production factories, the migrated
PostgreSQL fixture, and forced RLS. No SMTP credential acquisition occurs.

From `.worktrees/event-api-security-remediation`, main executes:

```bash
TMPDIR="$HOME/.cache/agent-tmp" dotnet test --project tests/Event.Persistence.IntegrationTests/Event.Persistence.IntegrationTests.csproj --configuration Release --verbosity quiet -- --treenode-filter "/*/*/EmailDeliveryPolicyNativeAuthorityTests/OwningTransactionSeesForeignOverridesLocksRevisionAndRollback" --minimum-expected-tests 2 --no-progress --maximum-parallel-tests 1
```

Minimum: two executed, zero skipped. Intended Red is the foreign tenant B
`TransportTenantId` assertion: expected B, actual null. A migration, admission,
compilation, permission, or setup failure is not that Red.

Main reported the corrected actual Red in `bash134`,
`email-policy-unmasked.trx`: both Runtime and Global failed this exact foreign
transport assertion, exit 2, duration 1m08s139. Existing registry seed rows were
updated rather than inserted twice; the assertions were unchanged.

## Evidence and subsequent phase-exit checks

Before the intended failing assertion, confirm ordinary role flags, enabled and
forced RLS, two seeded tenants, and direct override visibility of three rows for
Runtime and zero for Global. After remediation, the same test must reach the
owning serializable transaction's instance sender edit, processor revision 11 to
12, observer revision still 11, uncommitted instance lock overriding B's explicit
enable, and rollback restoring revision, sender value, enabled value and lock.
The final ordinary override read and tenant identity must remain unchanged.

## Delivered source mechanism and execution groups

`PostgresEmailDeliveryPolicyContract` installs
`email_delivery_policy_overrides()` in the configured application namespace.
The parameterless, `STABLE SECURITY DEFINER` function pins `search_path` to
`pg_catalog`, qualifies every table, and filters keys to
`EmailDeliverySettingKeys.All` in both its query and its owner-only SELECT RLS
policy. It returns the nine mapped `TenantSetting` scalar columns. Identity,
locks and creation/update metadata are necessary because
`EmailDeliverySettingsWriter.PrepareAsync` can attach these detached entities
for a later ordinary tenant-scoped mutation.

The owner has only column-level SELECT on these override columns and tenant ID,
no table-level SELECT and no DML. The function requires persisted tenant
membership. PUBLIC execution is revoked. The four groups are NOLOGIN,
NOSUPERUSER, NOBYPASSRLS, NOINHERIT, NOCREATEDB, NOCREATEROLE and NOREPLICATION:

- `event_email_policy_owner` owns the function and its bounded SELECT policy.
- `event_email_policy_migrator` may assume the owner during installation.
- `event_email_policy_runtime` grants only schema usage and function execution
  to authorized ordinary Runtime setting-mutation logins.
- `event_email_policy_global` independently grants the same function execution
  to authorized Global setting-mutation logins.

Both execution groups are deliberate: the static reader runs inside a transaction
owned by its caller, including `EmailDeliverySettingsWriter`, the policy revision
tracker, and system-setting reconciliation callers. Moving reads to a separate
Global pool would miss uncommitted instance or tenant settings. This function
runs through the existing context and transaction; it never reacquires a context,
retargets a tenant lease, acquires SMTP credentials or alters claim/delivery logic.
Exact-tenant reads and non-PostgreSQL readers keep their existing predicates.
Tenant catalog materialization remains in the reader, including empty override
maps for tenants that inherit instance policy.

## Main-owned central wiring

Add
`await PostgresEmailDeliveryPolicyContract.ApplyAsync(executionDatabase, cancellationToken);`
to `ExploreDatabaseMigrator.MigrateAsync` beside the existing purpose contracts,
using its namespace execution context before seeding. Mirror installation in
`PostgreSqlContainerFixture.InitializeAsync` on `migratorContext` after forced
RLS installation. The packet's native class currently installs it explicitly,
twice in the owning transaction test to cover idempotency.

Provision independent authorized logins with
`GRANT event_email_policy_runtime TO <runtime_login> WITH INHERIT TRUE, SET FALSE`
and
`GRANT event_email_policy_global TO <global_login> WITH INHERIT TRUE, SET FALSE`.
Neither login receives owner or migrator membership, table-wide cross-tenant
rights, nor a Runtime-to-Global credential fallback.
Central registration, migrator, shared fixture, guides and programme triad are
main-owned and were not edited in this packet.

## Pending phase-exit checks

No worker build, Green test or commit was run. Source diagnostics and whitespace
are not native execution proof. Main's phase exit runs the affected Persistence
Release build and these exact companions:

```bash
dotnet build src/Explore.Persistence/Explore.Persistence.csproj --configuration Release --verbosity quiet
dotnet test --project tests/Event.Persistence.IntegrationTests/Event.Persistence.IntegrationTests.csproj --configuration Release --verbosity quiet -- --treenode-filter "/*/*/EmailDeliveryPolicyNativeAuthorityTests/*" --minimum-expected-tests 6 --no-progress --maximum-parallel-tests 1
dotnet test --project tests/Event.Persistence.IntegrationTests/Event.Persistence.IntegrationTests.csproj --configuration Release --verbosity quiet -- --treenode-filter "/*/*/EmailDeliveryPolicyRevisionTests/*" --minimum-expected-tests 13 --no-progress --maximum-parallel-tests 1
```

The native six cover the original two transaction/revision/rollback cases,
two fixed-key/entity-metadata/empty-membership/ACL/owner-elevation cases, and
two unentitled-login denials. The owning SQLite revision companion adds a
transaction-local instance lock/revision rollback case while preserving all
existing suppression and operator-state assertions. Broader architecture and
provider gates remain workstream-owned.
