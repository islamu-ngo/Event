ABOUTME: Dependency rules per layer (Clean Architecture).
ABOUTME: Use this to validate layer references only.

# Dependency Rules

## Allowed References (Required)
- **Domain** → none
- **Application** → Domain only
- **Persistence/Infrastructure** → Application + Domain
- **API/Blazor** → all layers (composition root)

## Quick Checks
- Domain must not reference EF Core, external mediator/mapping packages, or ASP.NET.
- Application must not reference Persistence/Infrastructure.

## Exceptions
- Domain may use `[ForeignKey]` only (no validation attributes).

**Related**: `layer-responsibilities.md`, `violation-examples.md`, `fix-patterns.md`.
