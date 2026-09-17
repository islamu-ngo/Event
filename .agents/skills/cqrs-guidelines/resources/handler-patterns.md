ABOUTME: Handler rules for CQRS in this project.
ABOUTME: Emphasizes repos, manual validators, and DTO mapping.

# Handler Patterns (Lean)

## Core Rules
- Repositories only (no DbContext in handlers).
- Handlers map entities ↔ DTOs; repos return entities.
- Validators are manually instantiated inside handlers.
- **Transactional Writes**: Use `IUnitOfWork` for multi‑entity writes. Wrap all write operations in `ExecuteInTransactionAsync`.
- Always pass `CancellationToken` to async calls.

## Command vs Query
- Commands return `BaseCommandResponse<Guid>` (except legacy delete patterns).
- Queries return DTOs directly; `null` for not found.

## Related
- [command-patterns.md](command-patterns.md)
- [query-patterns.md](query-patterns.md)
- [validation-integration.md](validation-integration.md)
