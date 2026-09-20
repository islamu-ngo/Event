ABOUTME: Command conventions for CQRS in this codebase.
ABOUTME: Focuses on BaseCommandResponse and manual validation.

# Command Patterns (Lean)

## Rules
- Commands wrap DTOs, not individual properties.
- Commands return `BaseCommandResponse<Guid>`.
- Validators are manually instantiated in handlers (no DI).
- Handlers set system defaults; domain entities have no defaults.

## Delete Variants
- Follow existing feature pattern (bool, BaseCommandResponse, or Unit).

## Related
- [handler-patterns.md](handler-patterns.md)
