ABOUTME: Validation rules for CQRS handlers in this project.
ABOUTME: Keeps manual validator instantiation as the core constraint.

# Validation Integration (Lean)

## Rules
- Validators are instantiated manually inside handlers.
- Validators can require repositories (pass via constructor).
- Validation failures populate `BaseCommandResponse.Errors`.
- Queries skip validation unless explicitly required.

## Related
- [command-patterns.md](command-patterns.md)
