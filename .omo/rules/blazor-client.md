---
name: blazor-client
description: Apply when editing Explore.Blazor.Client components, styles, or UI services.
paths:
  - "src/Explore.Blazor.Client/**/*.cs"
  - "src/Explore.Blazor.Client/**/*.razor"
  - "src/Explore.Blazor.Client/**/*.razor.css"
related_skills: [blazor-ui-conventions, blazor-css-isolation, design-system]
related_docs: [docs/internal/BLAZOR.md, docs/internal/ACCESSIBILITY.md, docs/internal/DESIGN_SYSTEM.md, docs/internal/QUICK_REFERENCE.md]
minimum_tests: [Explore.Blazor.Client.Tests, Event.Architecture.Tests]
related_intents: [blazor-component-affordance, add-hal-link]
---

<!-- ABOUTME: Path-scoped rules for Explore.Blazor.Client components, styles, and UI services. -->
<!-- ABOUTME: Twin copy at .agents/rules/blazor-client.md. When modifying this file, update both paths. -->

# Blazor Client Rules

## Applies To
- `src/Explore.Blazor.Client/**/*.{cs,razor,razor.css}`

## Path-Specific Constraints
- **Render Mode**: Default to `InteractiveAuto`. Avoid assumptions about server-only state in shared client components.
- **MudBlazor v9**: Use MudBlazor v9 APIs exclusively. Prefer repo-standard wrapper components over raw MudBlazor controls.
- **CSS Isolation (BEM)**: Every `.razor` file should have a matching `.razor.css`. Use BEM naming for scoped classes.
- **Deep Selectors**: Use `::deep` only as a last resort for third-party component overrides.
- **Accessibility**: Structural semantics (headings, focus, labels) take precedence over visual shortcuts.
- **Record-Owned State**: Follow the [primary record-selection policy](../../docs/internal/GOVERNANCE.md#primary-record-selection-policy). Handwritten immutable snapshots and structurally eligible generated response/value contracts use records. Generated protocol inputs, HAL/inherited/file/exception shapes, and reasoned mutable edit/service state remain classes; the exact generated mutable inventory is owned by `eng/tools/Explore.GeneratedContracts/mutable-generated-contracts.txt`.
- **Generated Client Ownership**: Never hand-edit `Clients/EventApiClient.g.cs`. Use the pinned NSwag plus repository-owned Roslyn target; generated record properties are `init` except `[JsonExtensionData] AdditionalProperties`, which remains settable for System.Text.Json AOT, and generated diagnostic text omits all member values.
- **Published Collections**: Handwritten record state snapshots caller-owned lists, dictionaries, sets, and bytes; expose read-only/immutable members and preserve generated-client, base64 JSON, and component rerender semantics.

## Must Read
- [docs/internal/QUICK_REFERENCE.md#critical-rules](../../docs/internal/QUICK_REFERENCE.md#critical-rules) (Rule #21)
- [docs/internal/BLAZOR.md](../../docs/internal/BLAZOR.md)
- [docs/internal/ACCESSIBILITY.md](../../docs/internal/ACCESSIBILITY.md)

## Verification
- Build: `dotnet build --configuration Release --verbosity quiet`
- Tests: `Explore.Blazor.Client.Tests`, `Event.Architecture.Tests`

## Related
- Intents: `blazor-component-affordance`, `add-hal-link`
- Agents: `presentation-engineer-agent.md`, `quality-verifier-agent.md`
- Rules: `blazor-server.md`, `api-hateoas.md`, `tests.md`
