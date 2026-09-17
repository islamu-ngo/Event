---
name: dotnet-efcore-guidelines
description: "Load for EF Core entities/configurations, semantic value persistence, portable constraints, DbContext/query filters, repositories/specifications, migrations, model snapshots, transactions, or seeded lookup tables; use query-optimization specifically for N+1 or slow SQL diagnosis."
type: pattern
enforcement: suggest
priority: high
---
<!-- ABOUTME: EF Core guidance for Explore.Persistence repositories, DbContext configuration, migrations, query filters, and PostgreSQL-aligned data access. -->
<!-- ABOUTME: Keeps persistence entity-first, tenant-safe, soft-delete-aware, and consistent with lookup seeding and migration discipline. -->

## Resources
- [Domain model](../../../docs/internal/DOMAIN.md) — load for entity ownership and persisted invariants.
- [Record contracts](../../../docs/internal/RECORD_CONTRACTS.md) — load for semantic value leaves, portable checks, generated-provider ownership, and migration preflight.
- [Architecture](../../../docs/internal/ARCHITECTURE.md) — load for repository and layer boundaries.
- [Codebase insights](../../../docs/internal/CODEBASE_INSIGHTS.md) — load for non-obvious EF conventions.
- [Lookup seeder](../../../src/Explore.Persistence/Seed/LookupTableSeeder.cs) — load only for lookup-row changes.

## Rules

- Repositories return entities, never DTOs or `IQueryable`; read-only entity paths prefer `AsNoTracking()`.
- Named `SoftDelete` and `Tenant` filters remain active by default. Ignore only the exact filter required by the use case so tenant isolation cannot be disabled incidentally.
- Domain entities and lifecycle rows remain classes. Persist selected semantic values through their existing scalar owner columns unless the model explicitly requires separate identity; do not introduce owned/complex entities or broad converters merely because the Domain API uses a record value.
- Portable semantic constraints come from EF model/configuration or the repository migration generator. Verify provider SQL and malformed-row behavior instead of encoding one provider's syntax in shared configuration.
- Migration and model-snapshot files are generated artifacts. Fix the model or generator, remove only an unapplied development migration through `dotnet ef migrations remove`, and regenerate every affected provider; applied migrations require a generated corrective migration.
- For MariaDB/MySQL non-transactional constraint DDL, run a bounded PII-free preflight before installing a multi-constraint semantic set.
- Pooled contexts tolerate absent scoped tenant/current-user services during migration and seeding.
- Stable lookup IDs/codes stay aligned with idempotent missing-row repair. Insert migration-dependent lookup rows before backfill and keep model `HasData()` forbidden while EF Core #36682 applies.

## Workflow

1. Lock Domain semantics and provider-neutral malformed-row behavior with failing tests.
2. Update entity/configuration or the migration generator; keep scalar column and wire ownership stable unless the task explicitly changes them.
3. Generate each provider migration and inspect generated SQL/constraint ownership without hand edits.
4. Run focused model/provider tests, real-engine lifecycle evidence where required, pending-model detection, and the owning build.

## Verification
- `dotnet test --project tests/Event.Architecture.Tests/Event.Architecture.Tests.csproj --configuration Release --verbosity quiet -- --treenode-filter "/*/*/CleanArchitectureTests/*" --minimum-expected-tests 1 --no-progress --maximum-parallel-tests 1`
- `dotnet test --project tests/Event.Persistence.IntegrationTests/Event.Persistence.IntegrationTests.csproj --configuration Release --verbosity quiet`
- `dotnet build --configuration Release --verbosity quiet`

## Related
- [../clean-architecture-rules/SKILL.md](../clean-architecture-rules/SKILL.md)
- [../cqrs-guidelines/SKILL.md](../cqrs-guidelines/SKILL.md)
- [../criticality-guardrail/SKILL.md](../criticality-guardrail/SKILL.md)
