<!-- ABOUTME: Documents test-owned EF service providers and fresh-process factory contracts. -->
<!-- ABOUTME: Keeps provider isolation, intentional store sharing, and child failure propagation explicit. -->

# EF test isolation

EF Core 10's uncached provider build still checks the process-wide provider cache.
Once that cache contains 20 distinct configurations, even an uncached context can
throw `ManyServiceProvidersCreatedWarning`. Disposing contexts does not reset it.

Every test-owned construction path must therefore use the same policy **before**
the `DbContext` constructor (which accesses EF services before `OnConfiguring`):

- `TestDbContextOptions.Create<TContext>()` (or the non-generic overload) for raw options.
- `services.BuildIsolatedServiceProvider(...)` after all DI registrations. This adds
  the policy to EF's public `IDbContextOptionsConfiguration<TContext>` pipeline for
  every context type, including scoped contexts and pooled/non-pooled factories.
- `UseTestInMemoryDatabase(name)` gives each options object its own store root.
  Pass a test-owned `InMemoryDatabaseRoot` when separate builders deliberately
  share a database. Reusing the same options object also shares its root.

The policy leaves EF responsible for its internal providers, so production migration
SQL generators and test execution-strategy `ReplaceService` calls remain effective.
The many-providers warning remains an exception; do not log or ignore it.

`PostgreSqlContainerFixture` separately owns an `IMemoryCache` for its fixed
provider/schema model and query metadata. Reusing this metadata avoids rebuilding
the large application model for every context without re-enabling provider
caching. The fixture disposes the cache with its lifetime; it is not a global
cache shared across independent provider/schema configurations. Real tenant
filter, exact-tenant mutation and transaction-boundary tests cover this reuse.

## Opaque production contracts

Production design-time factories and `ExploreDatabaseMigrator.MigrateAndSeedAsync`
build options internally. Tests of these real entry points use
`[TUnit.Core.Executors.TestExecutor<FreshEfProcessExecutor>]` on the individual method:

- `PrimaryDatabaseProviderCompositionTests`: both `DesignTimeFactories_*` methods.
- `PrivacyErasureAuthorityDbContextFactoryTests`: the two successful factory methods.
- `PrivacyErasureAuthorityModelTests.AuthorityTopologies_HaveExactMigrationOwnersAndHistoryNamespaces`.
- `ExploreDatabaseMigratorTests`: both `MigrateAndSeedAsync_*` methods.
- `ExploreDatabaseMigratorTopologyTests.MigrateAndSeedAsync_SqliteAppliesExactlyOneAuthorityPath`.

These eight methods represent ten cases. Each selected TUnit node runs in its own
fresh process, including its normal fixture setup and teardown. The parent requires
at least one executed, non-skipped test and propagates nonzero child exit codes with
child output. `dotnet test --project` remains the entry point; no separate invocation
or manually maintained second test list is required. A single child contract must
itself stay below EF's cache threshold; split newly added configuration matrices
into parameterized cases rather than accumulating them inside one body.

`MigrateAsync` and `ApplyExternalPrivacyErasureAuthorityContractAsync` use caller-owned
contexts and do not require process isolation. The two factory validation-failure
contracts fail before context construction and also remain in-process.

## Regression checks

`EfCacheContaminationProbeTests` deliberately creates 20 cached configurations only
in a fresh child, then verifies an uncached construction throws. It is the sole
raw-options exception. `TestDbContextOptionsTests` exercises 24 mixed raw/DI/schema
configurations, explicit store sharing, exact parameterized child selection, and
real child failure propagation. Its DI precedence check verifies that registered
options cannot weaken isolation and that data survives between scopes sharing an
explicit store root. Coverage is executable; tests do not scan C# source text.
