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
