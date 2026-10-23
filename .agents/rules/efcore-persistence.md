---
name: efcore-persistence
description: Apply when editing Explore.Persistence repositories, DbContext, filters, or entity configurations outside migrations.
paths:
  - "src/Explore.Persistence/**/*.cs"
  - "!src/Explore.Persistence/Migrations/**/*.cs"
related_skills: [dotnet-efcore-guidelines, clean-architecture-rules]
related_docs: [docs/internal/MULTI_TENANCY.md, docs/internal/CODEBASE_INSIGHTS.md, docs/internal/ARCHITECTURE.md, docs/internal/QUICK_REFERENCE.md]
minimum_tests: [Event.Persistence.IntegrationTests, Event.Architecture.Tests]
related_intents: [update-repository-query, add-ef-migration, add-cqrs-handler]
---

<!-- ABOUTME: Apply when editing Explore.Persistence repositories, DbContext, filters, or entity configurations outside migrations. -->
<!-- ABOUTME: Twin copy at .omo/rules/efcore-persistence.md. When modifying this file, update both paths. -->

# EF Core Persistence Rules

## Applies To
- `src/Explore.Persistence/**/*.cs` (excluding Migrations)

## Path-Specific Constraints
- **Application Authority**: C# Domain/Application own business rules, authorization, workflows, concurrency coordination and completion. The database supplies storage transactions, constraints and tenant isolation, not a competing business application. Follow [the accepted decision](../../docs/internal/adr/ADR-application-owned-persistence.md).
- **EF-First Storage**: Use EF Core/LINQ, then supported provider APIs. Keep only small documented SQL storage primitives for demonstrated capability gaps; do not move use cases into stored procedures, privileged business routines or business triggers. Existing native contracts are transitional, not a requirement to preserve SQL-owned authority.
- **Necessary Permissions**: Fixed rights for an approved application purpose are normal implementation. Keep useful least privilege and RLS, but do not repeatedly seek approval for an already accepted credential-compromise trade-off. Escalate genuinely new trust boundaries, secret exposure or destructive actions; do not silently bypass existing controls before a verified C# replacement.
- **Context Pooling**: Use property injection for scoped context data; do not add scoped dependencies to `ExploreDbContext` constructor.
- **Mapping Logic**: Favor `IEntityTypeConfiguration<T>` classes over smearing mapping rules across the `DbContext` or repositories.
- **Explicit Tracking**: Use `AsNoTracking()` for all read-only query flows.
- **Specification Pattern**: Use the `EventQuerySpecification` builder for all complex queries; avoid ad-hoc LINQ in repositories.

## Must Read
- [docs/internal/QUICK_REFERENCE.md#critical-rules](../../docs/internal/QUICK_REFERENCE.md#critical-rules) (Rules #1, #12, #17)
- [docs/internal/MULTI_TENANCY.md](../../docs/internal/MULTI_TENANCY.md)

## Verification
- Build: `dotnet build --configuration Release --verbosity quiet`
- Tests: `Event.Persistence.IntegrationTests`, `Event.Architecture.Tests`

## Related
- Intents: `update-repository-query`, `add-ef-migration`, `add-cqrs-handler`
- Agents: `backend-engineer-agent.md`, `quality-verifier-agent.md`
- Rules: `application-layer.md`, `domain.md`, `efcore-migrations.md`
