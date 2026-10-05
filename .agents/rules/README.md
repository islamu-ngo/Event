<!-- ABOUTME: Index of path-scoped rules auto-loaded by Claude Code for this repository. -->
<!-- ABOUTME: Explains how rule files refine intent-scoped context without duplicating governing docs. -->

# Path-Scoped Rules

Claude Code auto-loads rule files in this folder when the file being edited matches a rule's YAML `paths:` glob.

## How To Use These Rules

| Principle | Meaning |
|---|---|
| Intent first | `intents.yaml` is primary; path rules refine it |
| Governing docs win | `docs/internal/QUICK_REFERENCE.md` and `docs/internal/GOVERNANCE.md` outrank every rule here |
| Cross-reference only | Rules point at governing docs; they must not duplicate them |
| Surgical context | Keep rules specific to file paths, not whole-project summaries |

See [`_schema.md`](_schema.md) before adding or editing any rule file.

## Indexed Rules

| Rule | Paths | Focus |
|---|---|---|
| `blazor-server.md` | `src/Explore.Blazor/**/*.cs`, `src/Explore.Blazor/**/*.razor` | BFF, YARP, cookie auth, SSR |
| `blazor-client.md` | `src/Explore.Blazor.Client/**/*.cs`, `src/Explore.Blazor.Client/**/*.razor`, `src/Explore.Blazor.Client/**/*.razor.css` | MudBlazor v9, BEM, CSS isolation, HAL gating |
| `application-layer.md` | `src/Explore.Application/**/*.cs` | native CQS, handler boundaries, Mapperly |
| `api-controllers.md` | `src/Explore.API/Controllers/**/*.cs` | route contracts and controller authoring |
| `api-hateoas.md` | `src/Explore.API/Hateoas/**/*.cs` | HAL policies, route-name alignment, affordances, registration helpers |
| `api-scheduling.md` | `src/Explore.API/Scheduling/**/*.cs`, `src/Explore.API/BackgroundServices/**/*.cs` | Quartz jobs, sweep registration, operator-visible job contract |
| `domain.md` | `src/Explore.Domain/**/*.cs` | entity modeling and invariants |
| `efcore-persistence.md` | `src/Explore.Persistence/**/*.cs`, except migrations | EF Core configs, repositories, filters |
| `efcore-migrations.md` | `src/Explore.Persistence/Migrations/**/*.cs` | migration discipline and seed sync |
| `tests.md` | `**/*Tests/*.cs`, `**/*UnitTests/*.cs`, `**/*IntegrationTests/*.cs`, `**/*.Tests/*.cs` | test execution and suite hygiene |
| `ip-clean-room.md` | `src/**/*`, `docs/internal/**/*`, `dev/active/**/*` | clean-room source isolation, SSO differentiation, provenance, and dependency-license compatibility |

## Layout Notes

- This repo's actual code roots are `Explore.*`, not `Event.*`; globs here follow the real layout.
- `api-hateoas.md` uses `src/Explore.API/Hateoas/**/*.cs` because link policies live there and there are no `*HalLink*.cs` files today.
