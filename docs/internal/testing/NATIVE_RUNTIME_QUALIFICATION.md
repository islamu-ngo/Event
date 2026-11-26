# Native runtime qualification

> Audience: maintainers of database startup, privacy, workers and hosted assurance.
> Source anchors: `ExploreDatabaseMigrator`, `OwnedAppHostFixture`, the native
> provider test projects and [application-owned persistence](../adr/ADR-application-owned-persistence.md).

## What each result establishes

Keep implementation, execution and admission separate. A source review can find
a defect or establish an intended control, but cannot certify a running topology.
A successful fixture or parser regression proves its named contract. It does not
prove worker delivery, browser behavior or a registered acceptance lane. Discovery
and compilation are prerequisites, never successful execution.

Record the candidate, build inputs, selected provider/topology, discovered cases,
exact command, terminal result and limitations together. A setup failure does not
establish failure of a business assertion that never ran. A timeout is evidence
of an unfinished operation; diagnose its first waiting command before increasing
budgets. Historical counts help locate cohorts, but are not permanent test quotas.

## Fresh database startup

Test the shipping migration path against an empty database. `EnsureCreated`
does not exercise the same behavior as `Migrate`, even if both produce a host
that answers health requests. Standalone SQLite must complete its in-process
migrations before HTTP readiness. A split PostgreSQL application waits for the
separately authorized MigrationService; application startup does not acquire
migration credentials to repair a schema.

For a foreign-key rebuild failure, trace the EF entity configuration, principal
alternate key, dependent column order and provider migration generator together.
Fix the model or generator, then regenerate the unapplied development migration
with `dotnet ef`. Never hand-edit generated migration/snapshot files, disable
foreign keys, or suppress pending-model-change diagnostics to obtain a pass.
Validate each affected migration assembly using its actual provider configuration.
Application, identity, erasure and Data Protection migration ownership stays separate.

This pre-release repository has no supported old-data upgrade contract. A fresh
schema repair does not require compatibility columns or historical-data adapters.
Reset only disposable, positively owned test storage; this is not permission to
delete an operator's database or a foreign volume.

## PostgreSQL readiness and application authority

Migration installs fixed storage permissions and tenant-isolation primitives.
Runtime admission checks readiness without repairing grants or switching roles.
The approved purpose chooses its own connection pool; test the actual selected
login without helper-role enrollment that the application never receives.

An installed relation name or an empty ACL difference is insufficient readiness
proof. Check the relevant complete policy, ownership, privilege and transaction
contracts. Installation must distinguish an admissible incomplete state from a
complete state, remain idempotent, and reject unexpected drift. Preserve the
original connection/transaction identity across installation and rollback.

PostgreSQL permissions and row policies are separate controls. Restrictive and
permissive policies compose differently; enabling RLS without an applicable
policy denies rows, and table owners normally have different behavior unless
forced RLS applies. Diagnose installation order and actual policy semantics
before widening access. Grant an approved application purpose the fixed rights
its C# workflow needs; do not preserve privileged business routines as the final
architecture merely because an old test expected them.

For slow catalogue admission, identify repeated reads, locks and query plans in
the failing operation. Reuse validated facts only within the same valid operation
where their identity and transaction remain bound. Do not cache authority across
operations or bypass drift checks. Keep supplied values parameterized; delimit
validated identifiers separately. Hostile identifier/literal tests must reach
installation as well as the later read path.

## Recovery, sessions and measurements

Worker recovery proof starts with an original persisted operation and claim.
Observe its interruption, restart the actual worker and require completion of
that original operation without duplicate external emission. Creating replacement
work, changing ownership, or disabling workers cannot satisfy recovery.

For BFF/Data Protection restart, observe terminal resource/process state before
restarting, then verify saved sessions under the selected shared authority.
HTTP unavailability alone is not proof that the old process exited. Check
partition mismatch, expiry, revocation and authority loss as separate boundaries;
do not replace the selected external authority with an in-memory key ring.

Metrics must be joined to the workload's actual exporter, endpoint, process and
container identities. A syntactically valid OpenMetrics response is not evidence
that a particular worker emitted it. Report load correctness separately from
capacity and benchmark measurements. Logs, traces and metrics have independent
acceptance contracts; no Loki installation is assumed by this guidance.

## Economical verification and safe evidence

Implement a coherent bounded repair before its native acceptance run. Use fast
in-memory invariant-breakers for pure business rules; use real providers where
the invariant depends on migration, RLS, transactions or concurrency. Build one
affected project at a time and execute with `--no-build`. Run one selected provider
at phase exit and the affected provider/topology union at PR acceptance. Reuse an
unchanged passing result only when its candidate/input contract permits it.
Never manufacture a phase pass to meet a latency target when native setup takes
longer. Record actual duration and keep expensive runs out of the edit loop.

The selected resource profile governs TUnit and container bounds. On a dedicated
64 GiB host, use `balanced` unless actual pressure requires adjustment; keep total
RAM below the maintainer's 55 GiB ceiling. Check capacity at heavy-run admission
or after pressure symptoms, not repeatedly between ordinary edits. .NET projects
still execute sequentially under the repository policy.

Evidence and cleanup must retain positive ownership. Do not terminate foreign
processes, prune the daemon or delete shared volumes. Keep raw secret-bearing
logs, environment files and authentication state private and ignored; publish
only bounded redacted outcomes. Private preservation archives remain private
when moved from an active programme to an archive.

## Official functional references

Reviewed 2026-11-22; these are behavioral facts, not imported implementation.

- [EF Core SQLite limitations](https://learn.microsoft.com/en-us/ef/core/providers/sqlite/limitations): some schema operations require table rebuilds, whose support depends on model-owned artifacts.
- [EF Core pending-model-change behavior](https://learn.microsoft.com/en-us/ef/core/what-is-new/ef-core-9.0/breaking-changes#pending-model-changes): migration application detects a model that differs from its last migration.
- [PostgreSQL row security](https://www.postgresql.org/docs/18/ddl-rowsecurity.html): permissions, applicable policies, policy composition and owner behavior require separate consideration.
- [ASP.NET Core Data Protection configuration](https://learn.microsoft.com/en-us/aspnet/core/security/data-protection/configuration/overview?view=aspnetcore-10.0): shared key storage and application isolation are explicit deployment decisions; access to key storage is security-sensitive.

See [owned distributed assurance](DISTRIBUTED_ASSURANCE.md) for the existing
harness and [testing policy](../TESTING.md) for project-specific execution.
