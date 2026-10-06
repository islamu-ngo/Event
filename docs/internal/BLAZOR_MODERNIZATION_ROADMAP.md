# Blazor Enterprise Modernization Roadmap

> **Audience:** Frontend maintainers, reviewers, and implementation agents
> **Status:** Bounded typography outcome verified with integration limits; all other product slices deferred
> **Owner:** Frontend
> **Updated:** 2026-10-06
> **Source:** Complete follow-up backlog from CTO revision `CTO-2026-10-05-r1`, originally grounded at `0fa923795db362e9f62a9b3e8f6abefa315b1ee2`
> **Evidence:** Executed host contracts, publications and browser observations, distinguished below

## Purpose and delivery boundary

The primary slice removes the host's third-party typography dependency using
admitted local Inter and the existing system stack. This tracked document
preserves every remaining modernization outcome from the complete ignored
`dev/backlog/blazor-enterprise-modernization-followups.md`. The fresh worktree
lacked that ignored file; graduation used its authorized root-workspace copy.
No work is omitted because of that transfer gap.

Typography and knowledge graduation are authorized; this inventory does not
authorize implementation of deferred accessibility, styling, state, test,
project/mock, or security work. It also does not claim that the entire
application works offline or emits no external integration traffic.

The [focused ADR](adr/ADR-local-typography-assets.md) owns typography's
decision, asset terms, composition, and recovery. The
[domain journal](../../dev/_journal/domains/blazor-enterprise-modernization.md)
owns reusable lessons. Neither this roadmap nor an I-VSD design assessment
replaces executed runtime evidence.

## Typography delivered and verification limits

The shell now links a local stylesheet through `Assets["css/fonts.css"]`.
It declares Inter 4.1 roman variable WOFF2 at weights 300-800 with
`font-display: swap`; the existing typography tokens own system and
unsupported-glyph fallback. CSP authorizes typography only from the instance:
`font-src 'self'` and `style-src 'self' 'unsafe-inline'`. This does not alter
unrelated integration permissions or introduce a new theme service.

Both publication consumers passed. Related Standalone prerequisites retain
composition-root appsettings and explicitly reference existing startup
migration projects instead of incidental workspace binaries. Published
Standalone starts on new SQLite storage with the selected secret authority.
No generated migration, schema, or provider behavior was changed.

| Executed evidence | Observed boundary |
|---|---|
| Typography 10/10 passed | Actual Server/WebAssembly/Auto renderer descriptors, static responses, CSP, origin/base resolution and admitted digest at root and mounted `/community/`. |
| BFF 13/13 passed | Affected existing shell/policy seam. |
| Architecture 22/22 passed | Selected Blazor client architecture owner, not the full architecture suite. |
| Split and Standalone publications passed | Both published hosts subsequently served the actual shell and decoded local font successfully. |
| Browser CSS/font delivery | 200, correct types, 352,240 font bytes, ETag, `max-age=3600, must-revalidate`; no immutable-font assertion. |
| Font identity | SHA-256 `693b77d4f32ee9b8bfc995589b5fad5e99adf2832738661f5402f9978429a8e3`; decoded Inter range 300-800 and all six weights. |
| Readability/layout | Desktop 1440 x 900 and phone 390 x 844 readable; no phone horizontal overflow; light/dark inspected on both consumers. |
| Representative language/direction | Arabic cookie `lang="ar"`, `dir="rtl"`; temporary Arabic/Greek/Cyrillic glyph probe readable, not universal font coverage. |

Missing-font recovery passed through an observed loading failure, exact
system-only/fallback metric equality and readable text without external requests.
Independent anonymized reviews accepted the privacy/admission/publication
boundary with weighted approval 1.0 and no blocking finding.

Only Server hydration was observed live. The three-mode HTTP tests verify
emitted renderer descriptors and asset delivery, not WASM/Auto hydration or
governance selection. Both shells reflowed at 720 x 450 DPR 2; native browser-menu
200% zoom was not established. Split restored English after an Arabic-cookie
reload, so representative RTL glyph evidence is from Standalone. Preserve
these limits; do not promote the evidence into a complete integration claim.

An existing launch API 308 response prevented localized translation/footer
identity verification. It is not attributed to typography. Document direction
and a temporary glyph probe do not prove those localized integration paths.
Recovery remains admitted local assets or the existing system stack, never
Google Fonts or another external-font fallback.

## Full retained inventory

The delivery order below preserves the backlog's independent boundaries.
It is not a dependency requiring typography to wait for cleanup.

| Retained outcome | Independent increment | Promotion and I-VSD ownership |
|---|---|---|
| Accessible controls and actual interaction gaps | Audit all six named surfaces; fix each coherent demonstrated defect. | Correct source findings and rendered evidence; `IVSD-F001` / `IVSD-M001`. |
| Scoped styling | Event sidebar. | Own component/tokens/visual-equivalence contract. |
| Scoped styling | Appearance editor. | Establish actual behavior/test owner before edits. |
| Scoped styling | Main layout. | Separate shell layout/RTL/theme contract. |
| Profile selection ownership | Nullable state notification and stale-result safety. | Shared caller analysis; `IVSD-F004` / `IVSD-M004`. |
| Organization logo reset | Complete file/model/control reset and stale-upload safety. | Separate attachment-state contract; `IVSD-F004` / `IVSD-M004`. |
| Deterministic component interactions | Modernize touched tests first; remaining inventory is test-only. | Preserve behavior; no suite-wide ownership wildcard. |
| Project metadata cleanup | Dead host link/commented XML and obsolete client lazy-loading claims. | Routing evidence; `IVSD-F003` / `IVSD-M003`. |
| External-service mock cleanup | Remove suppressions without losing event behavior. | Real fixture consumers; `IVSD-F003` / `IVSD-M003`. |
| Experimental HTTP API remediation | Supported API or explicitly approved policy decision, preserving no-repeat semantics. | Separate high-criticality security workstream; `IVSD-F003` / `IVSD-M003`. |
| Knowledge graduation and convention promotion | Durable decisions, lessons, roadmap, and only evidence-driven rule changes. | Truthful completion and twin-rule/schema checks where applicable. |

## Promotion contract

Promote one outcome at a time into synchronized plan/context/tasks artifacts.
Recheck source and graph freshness, trace real callers, resolve exact ownership
and I-VSD mappings, capture the relevant baseline, and author literal-file
Conventional Commit packets before source edits. No slice inherits blanket
authorization, guessed test counts, or historical "100% compliance" claims.

Use existing typed services, generated contracts, wrappers, and token ownership.
Do not introduce a generic UI framework, duplicated domain model, compatibility
API, or permanent mutable state abstraction. API authorization remains the
enforcement boundary; HAL link presence gates UI mutation affordances. BFF
tokens remain server-side, with existing cookie isolation, antiforgery, tenant
forwarding, and retry boundaries preserved.

Choose proportional verification for the actual changed layer. Build the owning
test executable before `--no-build`; use the repository's selected
Microsoft.Testing.Platform/TUnit runner:

```bash
dotnet test --project <owning-test-project.csproj> --configuration Release --no-build --treenode-filter "/*/*/<actual-test-class>/*" --minimum-expected-tests 1
```

This is a future command template, not evidence of execution. Select existing
declared classes or explicitly introduced owners, and reject empty runs.
TUnit lazy discovery/listing is not an authoritative complete test inventory.
Tests subscribe to the relevant completion before triggering asynchronous
behavior, use bounded completion, and re-query DOM after rerender. Fixed sleeps,
polling delays, repeated-until-green runs, mock call-count mirrors, raw
source/CSS scraping, and prose pinning are prohibited. Use TimeProvider where
time itself is the behavior; never assume every existing test already uses it.

Pure prose and dead XML/comment cleanup require no artificial regression test.
Behavioral privacy/state/security changes begin with their own failing
invariant scenario. Quarantine unrelated pre-existing failures after confirming
them on an untouched base; do not absorb them into the promoted slice.

## Accessible controls and actual interaction gaps

**Outcome:** Operators identify and operate each audited control using keyboard
and assistive technology. The prior missing-label examples in the draft editor,
moderation panel, login dialog, and sidebar were stale. Preserve their correct
action names instead of manufacturing fixes.

**Audit all six source surfaces:**

- `src/Explore.Blazor.Client/Components/InstanceAdmin/InstanceAdminPlanDraftEditor.razor`
- `src/Explore.Blazor.Client/Components/Moderation/ModerationReportDetailPanel.razor`
- `src/Explore.Blazor.Client/Pages/Admin/Tenant/Components/TenantNavigationLinksSection.razor`
- `src/Explore.Blazor.Client/Pages/Admin/Tenant/Components/TenantFooterSection.razor`
- `src/Explore.Blazor.Client/Shared/LoginPromptDialog.razor`
- `src/Explore.Blazor.Client/Components/Events/EventDetailsSidebar.razor`

**Existing test owners under `tests/Explore.Blazor.Client.Tests`:**

- `Components/Event/EventDetailsSidebarTests.cs`
- `Components/Moderation/ModerationReportDetailPanelTests.cs`
- `Pages/Admin/TenantNavigationLinksSectionTests.cs`
- `Pages/Admin/TenantFooterSectionTests.cs`

Draft-editor or login-dialog tests must be declared new if no actual owner
exists. Do not filter a nonexistent `LoginPromptDialogTests` class.

**Behavior and negative paths:** Accessible names describe the real action
without unnecessarily overriding visible labels. Name/role/state stay
accurate. Disabled, absent-HAL, keyboard, dialog close, focus restoration,
localized text, and RTL paths work. Audit rendered DOM/accessibility trees,
not source strings or component names. A `Title` attribute alone neither
proves nor disproves the final accessible name.

**Verification and promotion:** Relevant bUnit owners plus final keyboard/focus,
200% zoom, contrast, light/dark, mobile/desktop, RTL, and risk-based screen-reader
checks. A scanner is not WCAG certification. Refresh corrected evidence and
`IVSD-F001` / `IVSD-M001` mappings before implementation.

**Commit boundary:** `fix(accessibility)` per coherent demonstrated interaction
defect, with tests and necessary component/docs changes together. Do not claim
a public fix for an unchanged correctly labeled control.

## Scoped styling: three independent component increments

**Outcome:** Sidebar, appearance editor, and shell styling use existing
parameters/utilities/tokens and scoped ownership without theme, layout, or
direction regressions.

| Increment | Source ownership | Known test owner |
|---|---|---|
| Event sidebar | `src/Explore.Blazor.Client/Components/Events/EventDetailsSidebar.razor`, `.razor.css`, and `.razor.cs` only if conditional class logic needs it | `tests/Explore.Blazor.Client.Tests/Components/Event/EventDetailsSidebarTests.cs` |
| Appearance editor | `src/Explore.Blazor.Client/Shared/AppearanceEditor.razor` and `.razor.css` | No owner established by review; declare a behavioral test only for observable changed behavior. |
| Main layout | `src/Explore.Blazor.Client/Layout/MainLayout.razor`, `.razor.css`, `.razor.cs` | `tests/Explore.Blazor.Client.Tests/Layout/MainLayoutTests.cs` |

Existing scoped styles are not new files. Prefer component parameters, utility
classes, theme tokens, and wrapper-owned CSS in that order. Use BEM, logical
properties, native nesting under the repository depth limit, and `CssBuilder`
where conditional composition needs it. Wrapper before `::deep`; no
`!important`, global MudBlazor overrides, speculative utility framework, or
static-class explosion for dynamic values. Do not claim class composition is
type safety or an allocation improvement without evidence.

Preserve tenant branding, disabled/action state, focus visibility, 200% zoom,
reflow, browser asset loading, and LTR/RTL in light/dark at desktop/mobile
widths. bUnit cannot prove visual equivalence. Do not assert "440 styles
eliminated" from an unverified historical inventory.

**Commit boundary:** Each component increment uses `refactor(architecture)`
with both changelog-skip trailers. Do not combine the three independently
shippable components into an umbrella refactor.

## Profile selection state ownership

**Outcome:** Parent highlight and shared preview agree through select, close,
delete, dock dismissal, rapid reselection, and navigation.

**Ownership:** Under `src/Explore.Blazor.Client`,
`Pages/User/UserProfile.razor` and `.razor.cs`,
`Components/Events/EventPreviewWorkspace.razor.cs`, markup only if necessary,
and actual shared callers. Existing test owner:
`tests/Explore.Blazor.Client.Tests/Pages/User/UserProfileTests.cs`.

Published selection remains `Guid?`, including null deselection. Use a typed
notification or existing binding seam without duplicating detail loading,
dialogs, edit navigation, delete, or share orchestration in the page. Preserve
legitimate component commands; an `@ref` alone is not a defect and does not
authorize parameter mutation.

**Failing invariant evidence:** Subscribe before actions and control detail
completion so a late result cannot replace a newer selection or reopen a
dismissed panel. Assert rendered selection/detail identity and missing-HAL
behavior rather than internal call counts.

**Promotion and commit boundary:** Trace shared callers before choosing exact
paths and refresh `IVSD-F004` / `IVSD-M004`. Use `refactor(events)` with skip
trailers if behavior is preserved, or `fix(events)` only for an evidenced
defect. No parallel UI state framework or compatibility setter is introduced.

## Organization logo reset state

**Outcome:** Failed upload leaves preview, selected bytes, storage ID, model
attachment, and native upload control consistently clear; the same file can
be selected again.

**Ownership:**

- `src/Explore.Blazor.Client/Pages/Organizations/CreateOrganization.razor`
- `src/Explore.Blazor.Client/Pages/Organizations/CreateOrganization.razor.cs`
- `src/Explore.Blazor.Client/Shared/ImageUpload.razor`
- `tests/Explore.Blazor.Client.Tests/Pages/Organizations/CreateOrganizationTests.cs`

Declare necessary shared-control tests new. Existing `RemoveImage()` invokes
preview/file callbacks and clears the upload control as well as preview/error
state. A parent preview-null assignment is insufficient. Retain that command
if it is the smallest correct contract; if declarative reset is chosen,
specify the complete transition and update shared callers without aliases.

**Failing invariant evidence:** Controlled upload outcomes cover success,
failure, reset, same-file reselection, and obsolete completion after reset or
replacement. An old upload must not attach a stale logo. No real uploaded
content, sensitive diagnostics, sleeps, or upload-service call-count mirrors.

**Promotion and commit boundary:** A complete `refactor(storage)` or evidenced
`fix(storage)` slice, independent of profile selection and typography. Refresh
`IVSD-F004` / `IVSD-M004` across both state slices.

## Deterministic component test interactions

**Outcome:** Tests observe completed UI behavior rather than renderer timing
luck.

Modernize tests touched by each product slice first. Broad remaining conversion
requires a separate test-only inventory; it does not grant ownership of
`**/*Tests.cs`. Prefer async interaction helpers, fresh DOM queries after
rerender, and subscription to exact external completion before an action.
`ClickAsync` does not automatically await unrelated background work.

Retain meaningful assertions and existing behavior. Follow the promotion
contract's deterministic test rules; do not weaken failures or replace
observable assertions with mock mirrors.

**Verification:** Build the affected executable, run each affected existing
class with `--minimum-expected-tests 1`, then run the full component project
once at this test-only slice's exit. Lazy class discovery alone cannot
establish the complete inventory.

**Commit boundary:** `test(testing)` with both skip trailers. Keep unrelated
pre-existing failures quarantined.

## Project metadata and external-service mocks

These remain two independently shippable outcomes.

### Project metadata

Remove the verified dead `Component.razor` link/commented XML in
`src/Explore.Blazor/Explore.Blazor.csproj` and obsolete lazy-loading
implementation claims in
`src/Explore.Blazor.Client/Explore.Blazor.Client.csproj`. Check current routing
before deleting related documentation. Do not add a lazy-loading library to
make an obsolete comment true, or treat a comment as measured performance.

**Verification and commit:** Independently green owning-project build and
relevant routing/documentation review; pure comment/XML deletion needs no
artificial regression test. Use `build(build)` with both skip trailers.

### External-service mocks

Remove `CS0067` suppressions from
`tests/Explore.Blazor.Client.Tests/Common/MudBlazorTestMocks.cs`. Explicit
no-op event accessors are acceptable only for deliberately non-emitting
third-party-infrastructure mocks. When event behavior is asserted, use a
controllable fake that retains subscribers and emits the real event.

**Verification and commit:** Select real fixture consumers or the full component
suite at this fixture slice's exit; the mocks file is not a test class.
Use `test(testing)` with both skip trailers. Neither outcome permits moving
diagnostics into a global suppression policy. Refresh `IVSD-F003` /
`IVSD-M003` for truthful cleanup evidence.

## Experimental HTTP API and security-boundary remediation

**Status:** Separate high-criticality workstream, not authorized by
modernization cleanup.

`src/Explore.Blazor/Extensions/HttpClientExtensions.cs` removes inherited
resilience for credential/transient consumption and disables unsafe retries.
The proposal to move `EXTEXP0001` into project-wide `NoWarn` is rejected.

Before selecting a replacement, read the current service-default pipeline
and registration/caller graph. Preserve cookie isolation, authentication and
session boundaries, trusted tenant forwarding, cancellation, safe/unsafe
method policy, and credential-consumption semantics. Prefer supported APIs
only if they preserve that contract. If an intentional compiler ratchet is
actually required, obtain explicit maintainer approval for repository-owned
policy; do not invent a wrapper or broad suppression to hide it.

**Worst break and failing invariant evidence:** An ambiguous response after
credential consumption must never cause another consumption request.
Inherited/default resilience must not silently introduce unsafe retry or
hedging. Test real outbound requests with controlled transport and concurrency,
not internal `Received(1)` assertions.

**Promotion gate:** Follow the matched security intent's intake,
invariant-breaker, review, and proportional real-boundary requirements.
Zero graph flows does not mean zero impact. Refresh `IVSD-F003` /
`IVSD-M003`; choose the exact independently reviewable security packet only
after behavior and policy ownership are settled.

## I-VSD traceability and claim limits

The [revalidated assessment](../../islamic-value-sensitive-design/workstreams/i-vsd-blazor-enterprise-modernization.md)
is current / plan-aligned for its reviewed design, not runtime, legal, scholarly,
or WCAG certification. Its historical source snapshot remains distinct from
the subsequently executed typography mitigation; deferred findings remain open.

| Finding and mitigation | Retained responsibility and completion evidence |
|---|---|
| `IVSD-F001` / `IVSD-M001` | Corrected six-surface accessibility audit; preserve the four already named controls; rendered semantics, keyboard/focus, contextual localization, RTL, and risk-based assistive-technology evidence. Justice and practical access, not label counts. |
| `IVSD-F002` / `IVSD-M002` | Local typography admission, actual resource/CSP/origin/base-path checks, both published consumers, supported weights, representative language fallback and unavailable-font recovery verified. Trust and privacy promise limited to typography; live WASM/Auto hydration, native zoom and localization limits remain explicit. |
| `IVSD-F003` / `IVSD-M003` | Independent project metadata and mock cleanup, separate HTTP no-repeat/security contract, and truthful graduation/evidence. No global `NoWarn`, concealed warnings, fake test counts, or whole-program completion claim. |
| `IVSD-F004` / `IVSD-M004` | Both profile nullable-selection and complete logo-reset slices, with stale-completion tests and real caller ownership. Trust and user agency require complete transitions, not a blanket component-reference ban. |

Asset/distribution changes require renewed admission. Material privacy defaults,
recovery, tenant/authentication boundaries, state behavior, or scope changes
require assessment refresh before implementation. Status/evidence-location
bookkeeping alone does not constitute a new moral review. The report owner
retains authority over finding closure; typography progress does not close
deferred findings.

## Knowledge graduation, documentation, and completion

Persist durable decisions in `docs/internal/adr/`, non-obvious lessons in
`dev/_journal/domains/`, and retained scope here. Neither ignored active task
memory nor the original local backlog is force-staged.

Update developer conventions only when new evidence changes an existing rule.
Do not duplicate or expand skills just to repeat current guidance. Necessary
agent-rule edits update both twins and pass skill/schema/link checks in their
own slice. Administrative or operator-visible behavior changes ship both
public adopter guidance and the internal technical source anchor, preserving
their separate audiences.

Each promoted slice records exact executed commands, exit codes, nonzero test
counts, rendered evidence where relevant, unresolved gates, and owned-file
commit boundaries. Refactor/build/test-only increments that require skip
trailers include both `Changelog: skip` and a truthful `Changelog-Reason`.
Tracked graduation belongs in its own internal-documentation commit.

Typography completion does not close this roadmap. Unverified historical
3,011-test, 17.8-second, zero-warning, style-count, click-count, universal
TimeProvider, and full HAL-compliance claims are not acceptance metrics.
The full program closes only as each independent outcome meets its own
behavior, documentation, verification, and review contract.

## Related

- [Blazor architecture and project boundaries](BLAZOR.md)
- [Host workflow and typography verification](BLAZOR_DEV_WORKFLOW.md#5-host-owned-typography)
- [Accessibility standards](ACCESSIBILITY.md)
- [Design system](DESIGN_SYSTEM.md)
- [Typography ADR](adr/ADR-local-typography-assets.md)
- [Inter admission](legal/dependencies/inter.md)
- [Domain journal](../../dev/_journal/domains/blazor-enterprise-modernization.md)
