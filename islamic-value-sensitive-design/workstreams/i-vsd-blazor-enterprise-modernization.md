# I-VSD Planning Assessment: Blazor Enterprise Modernization

Last Updated: 2026-10-05

## Review Metadata

- Mode: planning
- Workstream: blazor-enterprise-modernization
- Subject: Blazor client & host architecture modernization, accessibility (a11y) screen-reader parity, CSS isolation & design token governance, offline font self-hosting, and declarative UI component state.
- Report kind: implementation-planning assessment
- Report status: current / plan-aligned (planning only)
- Disposition: enterprise modernization plan aligned
- Evidence cutoff: 2026-10-05
- Reviewed input: repository at `origin/develop` plus the active modernization triad in `dev/active/blazor-enterprise-modernization/`:
  - plan `blazor-enterprise-modernization-plan.md`
  - tasks `blazor-enterprise-modernization-tasks.md`
  - context `blazor-enterprise-modernization-context.md`

## Scope And Claim Boundary

This assessment evaluates the architectural refactoring, accessibility enhancements, self-hosted typography assets, and UI state modernization across all Blazor projects in the repository:
- `src/Explore.Blazor` (Server/WASM host & BFF host)
- `src/Explore.Blazor.Client` (Interactive WebAssembly client)
- `src/Event.Web.BffHosting` (BFF reverse proxy & session infrastructure)
- `src/Event.SetupAssistant.Browser` (Standalone offline browser setup)
- `tests/Explore.Blazor.Client.Tests` (bUnit + TUnit component test suite)
- `tests/Explore.Blazor.IntegrationTests` (End-to-end hosting tests)
- `tests/Event.SetupAssistant.Browser.Tests` (Setup assistant tests)

This review focuses strictly on UI accessibility, data sovereignty, dependency minimization, code craftsmanship, and UI state determinism. It does not certify religious-legal compliance or replace external accessibility audits.

## Findings

| ID | Lifecycle / severity / claim | Stakeholder, principle and provider-controlled risk | Evidence / confidence | Mitigation / owner |
|---|---|---|---|---|
| IVSD-F001 | open / high / inclusive access | **Al-Karāmah (Human Dignity) & Al-'Adl (Justice)**: Visually impaired operators and assistive technology users encountering icon buttons without accessible names (`aria-label`) cannot perceive critical actions (e.g. deleting drafts, closing dialogs, navigating tenant sections), causing exclusion and unequal access. | Source audit identified naked `MudIconButton` instances without `aria-label` in `InstanceAdminPlanDraftEditor.razor:109`, `ModerationReportDetailPanel.razor:10`, `LoginPromptDialog.razor:8`, `EventDetailsSidebar.razor:11-22`, `TenantNavigationLinksSection.razor:96`, and `TenantFooterSection.razor`. | IVSD-M001; Blazor UI / Accessibility owner. |
| IVSD-F002 | open / high / privacy & sovereignty | **Al-Amānah (Trust & Privacy) & Ḥifẓ al-Khuṣūṣiyyah (Privacy Protection)**: Loading web fonts dynamically from `fonts.googleapis.com` leaks operator and attendee IP addresses, user-agent metadata, and access patterns to a third-party corporate provider. In self-hosted sovereign deployments, this violates operator data sovereignty and breaks completely in offline/air-gapped installations. | `src/Explore.Blazor/Components/App.razor:L36` loads Google Fonts via external CDN link. | IVSD-M002; Blazor Host & Infrastructure owner. |
| IVSD-F003 | open / medium / code truthfulness | **Al-Ṣidq (Truthfulness) & Iḥsān (Excellence/Craftsmanship)**: Suppressing compiler diagnostics via `#pragma warning disable` and retaining obsolete project documentation claiming non-existent lazy-loading logic obscures technical debt, weakens automated verification, and misleads future maintainers. | `tests/Explore.Blazor.Client.Tests/Common/MudBlazorTestMocks.cs:L120, L193, L218` uses `#pragma CS0067`; `src/Explore.Blazor/Extensions/HttpClientExtensions.cs` repeatedly disables `EXTEXP0001`; `src/Explore.Blazor.Client.csproj:L179-L223` retains 45 lines of obsolete lazy-loading notes. | IVSD-M003; Blazor Architecture owner. |
| IVSD-F004 | open / medium / determinism & agency | **Al-Amānah (Reliability & Stewardship)**: Fragile imperative `@ref` component manipulation across parent/child boundaries creates race conditions and hidden side effects that lead to UI glitches or unexpected state loss during high-stakes administrative operations. | `UserProfile.razor:121` accesses `_eventPreviewWorkspace?.SelectedEventId` via `@ref`; `CreateOrganization.razor.cs:152` imperatively invokes `_imageUpload.RemoveImage()`. | IVSD-M004; Blazor Client Components owner. |

## Recommendations

- **IVSD-M001 (Universal Accessible Affordances)**:
  - Every `MudIconButton` and icon-only interactive element SHALL provide a clear, localized or contextual `aria-label` describing the exact action (e.g., `aria-label="Delete plan draft"`, `aria-label="Close dialog"`).
  - Dialogs and modal surfaces SHALL ensure appropriate focus entrapment and programmatic landmark roles (`<main role="main">`, `<nav aria-label="...">`).
  - Verify WCAG 2.2 AA compliance for keyboard focus navigation and RTL-safe layout behavior.

- **IVSD-M002 (Self-Hosted Sovereign Typography)**:
  - Vendor the Inter font assets locally in `src/Explore.Blazor/wwwroot/fonts/inter/` (WOFF2 format).
  - Define local `@font-face` declarations in `src/Explore.Blazor/wwwroot/css/fonts.css`.
  - Remove all external stylesheet links to `fonts.googleapis.com` and `fonts.gstatic.com` in `App.razor`.
  - Guarantee zero external network calls upon initial page load, ensuring complete air-gapped support and zero IP leakage.

- **IVSD-M003 (Truthful Verification & Elimination of Suppressions)**:
  - Replace unassigned event warnings in test mocks (`CS0067`) with explicit event accessors (`{ add { } remove { } }`) rather than `#pragma warning disable`.
  - Consolidate experimental API usage (`EXTEXP0001`) into a single, centrally documented `<NoWarn>` property in `Explore.Blazor.csproj` with explicit architectural justification.
  - Purge dead file links (`Component.razor`), commented XML, and obsolete lazy-loading notes from project files.

- **IVSD-M004 (Declarative State & Event Callbacks)**:
  - Replace parent-to-child imperative `@ref` access with unidirectional parameter flow and child-to-parent `EventCallback<T>`.
  - Hoist workspace state to page-level controllers or use `ParameterState<T>` for complex two-way bound administrative controls.
  - Modernize bUnit component tests to utilize asynchronous event triggers (`await button.ClickAsync(new MouseEventArgs())`), ensuring deterministic render cycle verification.
