ABOUTME: Repository usage rules for EF Core in this project.
ABOUTME: Emphasizes entity returns and handler-side mapping.

# Repository Pattern (Lean)

## Rules
- Repositories live in Persistence; interfaces in Application.
- Repositories return entities, not DTOs.
- Handlers map entities → DTOs with Mapperly.
- Do not return `IQueryable` from repositories.

## Related
- [dbcontext-patterns.md](dbcontext-patterns.md)
