---
name: domain
description: Apply when editing Explore.Domain entities, value objects, enums, or domain services.
paths:
  - "src/Explore.Domain/**/*.cs"
related_skills: [clean-architecture-rules]
related_docs: [docs/internal/DOMAIN.md, docs/internal/ARCHITECTURE.md, docs/internal/QUICK_REFERENCE.md]
minimum_tests: [Event.Domain.UnitTests, Event.Architecture.Tests]
related_intents: [add-ef-migration]
---

<!-- ABOUTME: Path-scoped rules for Explore.Domain entities, value objects, enums, and domain services. -->
<!-- ABOUTME: Twin copy at .agents/rules/domain.md. When modifying this file, update both paths. -->

# Domain Rules

## Applies To
- `src/Explore.Domain/**/*.cs`

## Path-Specific Constraints
- **Domain Purity**: Zero dependencies on Infrastructure, Persistence, or Presentation layers. No `using` statements pointing to other layers.
- **Explicit Logic**: All business rules must be explicit. Do not bury logic in auto-properties; use methods for state transitions.
- **Audit Consistency**: Ensure aggregate roots implement the expected auditing and soft-delete interfaces (e.g., `IAuditableEntity`, `ISoftDeletable`).
- **Value Semantics**: Follow the [primary record-selection policy](../../docs/internal/GOVERNANCE.md#primary-record-selection-policy). Only a small, self-contained value may be a `readonly record struct`; use a sealed record class when reference-bearing value semantics fit. EF entities and outbox lifecycle entities remain classes.
- **Published Collections**: A Domain record snapshots caller-owned collections into immutable/read-only storage. Aggregate entities may keep private mutable backing collections behind read-only views; callers never mutate navigation state directly.

## Must Read
- [docs/internal/QUICK_REFERENCE.md#critical-rules](../../docs/internal/QUICK_REFERENCE.md#critical-rules) (Rules #3, #4, #5, #6, #10)
- [docs/internal/DOMAIN.md](../../docs/internal/DOMAIN.md)

## Verification
- Build: `dotnet build --configuration Release --verbosity quiet`
- Tests: `Event.Domain.UnitTests`, `Event.Architecture.Tests`

## Related
- Intents: `add-ef-migration`
- Agents: `architect-agent.md`, `backend-engineer-agent.md`, `quality-verifier-agent.md`
- Rules: `application-layer.md`, `efcore-persistence.md`, `efcore-migrations.md`
