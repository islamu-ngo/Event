ABOUTME: Accessibility platform rules and conventions for WCAG 2.2 AA compliance.
ABOUTME: Defines the page shell contract, service contracts, component authoring rules, and CSS direction ban.

# Accessibility Standards

> **Target**: WCAG 2.2 Level AA compliance.
> **Standard**: Web Content Accessibility Guidelines 2.2 (W3C Recommendation, October 2023).
> **Principles**: Perceivable, Operable, Understandable, Robust (POUR).

---

## Guided Onboarding Surfaces

Getting-started uses the administration page's single H1, then H2/H3/H4 section
headings. Refresh marks the section busy and updates one polite status. Canonical
operator kind/country selectors use native labels and connected help/error IDs;
metadata translation keys do not become visible IDs or edit permissions. Optional
wizard details use a native `details`/`summary` disclosure. Sidebar labels use the
primary semantic text token in both themes; long server reason codes wrap, and
checklist links retain minimum touch targets and explicit focus indicators.

Component and architecture assertions are not accessibility certification. Record
rendered keyboard, reflow, themes, direction and assistive-technology evidence,
and explicitly retain any unavailable or uncompleted checks in the workstream
report.

## Platform Rules

### PR-1: Page Shell Contract

Every page rendered through `MainLayout` automatically gets these accessibility features:

| Feature | Element | Purpose |
|---|---|---|
| Skip-to-content link | `<a href="#main-content" class="skip-link">` | WCAG 2.4.1 — Bypass Blocks |
| Main landmark | `<main id="main-content" tabindex="-1">` | WCAG 1.3.1 — Landmark regions |
| Header landmark | `<header class="main-layout__header">` | Native landmark (preferred over `role="banner"`) |
| Sidebar navigation | `<nav aria-label="Sidebar navigation">` | Named navigation region |
| Application workspace rail | `<nav aria-label="Application workspaces">` | One semantic nav projected to a desktop rail or Xs bottom navigation |
| Events workspace navigation | `<nav aria-label="Events workspace navigation">` | Distinguishes contextual Events links from shell/header navigation |
| ARIA live region (polite) | `<div id="aria-live-polite" aria-live="polite">` | Non-interrupting announcements |
| ARIA live region (assertive) | `<div id="aria-live-assertive" aria-live="assertive">` | Critical alerts |
| Focus-on-navigate | `AccessibilityFocusService.FocusOnNavigateAsync()` | Screen reader page change announcement |

**Page authors do NOT need to add these.** They are provided by the shell.

**Page authors MUST ensure:**
- Every page has an `<h1>` element (focus target after navigation).
- Heading hierarchy is sequential (`h1` → `h2` → `h3`, no skipping).

Workspace changes use the same route-navigation focus path as page changes: `MainLayout` calls `FocusOnNavigateAsync()`, which targets the page heading or main landmark. Navigation providers must use distinct labels; do not reuse `Sidebar navigation` for nested workspace navigation.

### PR-2: Service Contracts

Two accessibility services are available via DI (registered as `Scoped`):

#### `IAccessibilityAnnouncerService`
```csharp
Task AnnouncePoliteAsync(string message);    // Status updates, search results, toasts
Task AnnounceAssertiveAsync(string message);  // Errors, session expiry, destructive confirmations
```

**When to use:**
- Dynamic content changes not triggered by focus movement → `AnnouncePoliteAsync`
- Critical errors or time-sensitive alerts → `AnnounceAssertiveAsync`
- Do NOT announce content that is already being focused (focus announces itself)

#### `IAccessibilityFocusService`
```csharp
Task FocusAsync(string cssSelector, bool preventScroll = false);
Task FocusByIdAsync(string elementId, bool preventScroll = false);
Task FocusMainContentAsync();
Task FocusOnNavigateAsync();      // Internal — called by MainLayout
Task SaveFocusAsync();            // Call before opening modal/dialog
Task RestoreFocusAsync(string? fallbackSelector = null);  // Call on modal/dialog close
Task<string> GetPreferredMotionAsync();  // Returns "reduce" or "no-preference"
```

**Focus restore fallback chain:** saved element → `fallbackSelector` → `#main-content` → `<body>`.

Instance configuration-manifest downloads use a two-stage focus and
announcement policy. A successfully started same-origin download is announced
politely without moving focus. If immediate HAL revalidation removes the
requested affordance, every download action disappears, an operator-safe alert
is rendered, and focus moves to that alert. Because focused content announces
itself, this failure path must not also call the global announcer. The section
uses native buttons through the control-plane action primitive, an `h2` under
the page `h1`, logical CSS properties, project focus/target tokens, and
localized labels and guidance.

**Dialog/modal pattern:**
```csharp
// Before opening
await AccessibilityFocusService.SaveFocusAsync();
// ... dialog interaction ...
// After closing
await AccessibilityFocusService.RestoreFocusAsync();
```

### PR-3: Component Authoring Rules

These 8 rules apply to every `.razor` component:

1. **Images**: Every `<img>` and `<MudImage>` MUST have meaningful `Alt` text. Use `Alt=""` (empty string) only for purely decorative images.

2. **Icons as buttons**: Icon-only buttons MUST have `aria-label` describing the action (not the icon name).
   ```razor
   @* Correct *@
   <MudIconButton Icon="@Icons.Material.Filled.Delete" aria-label="Delete event" />
   @* Wrong *@
   <MudIconButton Icon="@Icons.Material.Filled.Delete" />
   ```

3. **Form inputs**: Every input MUST have an associated label. Use MudBlazor's `Label` parameter or explicit `<label for="">`.

4. **Headings**: Use heading elements (`<MudText Typo="Typo.h1">` through `h6`) for structure, not for visual size. One `h1` per page.

5. **Color alone**: Never use color as the sole way to convey information. Add text, icons, or patterns.

6. **Keyboard**: All interactive elements must be reachable via Tab and activatable via Enter/Space. Custom widgets need `role`, `tabindex`, and key handlers.

7. **Focus visible**: Never remove focus indicators. Use `outline` (not `border`) for custom focus styles. Minimum 2px, contrasting color.

8. **Dynamic content**: Content that updates without page navigation must be announced via `IAccessibilityAnnouncerService` OR be within an ARIA live region.

AI assistant rail additions:

- The shell AI assistant rail is a complementary landmark and must keep a meaningful `aria-label` in both docked and mobile-overlay modes.
- Reference chips must be keyboard-removable with Delete or Backspace in addition to pointer removal.
- Proposed action Confirm/Reject buttons must remain real buttons, not clickable text or icons without labels.
- Loading, empty, disabled, and safe error states need visible text so the panel remains understandable without color or motion.
- Assistant actions are affordances from HAL links; do not expose disabled buttons based on local role/claim guesses when the API omitted the corresponding link.

### PR-4: CSS Direction Ban (Immediate)

**Banned properties** (use logical equivalents):

| Banned | Use Instead |
|---|---|
| `margin-left`, `margin-right` | `margin-inline-start`, `margin-inline-end` |
| `padding-left`, `padding-right` | `padding-inline-start`, `padding-inline-end` |
| `border-left`, `border-right` | `border-inline-start`, `border-inline-end` |
| `left`, `right` (positioning) | `inset-inline-start`, `inset-inline-end` |
| `text-align: left/right` | `text-align: start/end` |
| `float: left/right` | Flexbox/Grid instead |

**Exceptions**: `direction: rtl` on `<html>`, browser-reset styles, third-party overrides.

This ensures correct RTL rendering for Arabic-speaking communities without code changes.

---

## RTL and Direction Support

### Automatic RTL
Arabic language selection automatically activates RTL mode via:
1. `LanguageProvider` reads `lang` cookie → `LanguageContext.IsRtl` detects RTL languages (ar, he, fa, ur)
2. `LanguageContext.EffectiveIsRtl` resolves: user override > language-based detection
3. `MudRTLProvider RightToLeft="@_isRtl"` cascades to all MudBlazor components
4. `localization.setDirection()` sets `dir` on `<html>` element
5. App.razor reads `lang` cookie server-side for correct SSR (`<html lang="ar" dir="rtl">`)

### User Direction Override
Users can override auto-detected direction via appearance preferences:
- `"auto"` — language-based (default)
- `"ltr"` — force left-to-right even with Arabic
- `"rtl"` — force right-to-left even with English

Persisted via `POST /bff/direction?dir={value}`, stored as `appearance.direction` user preference.

### CSS Logical Properties
All component CSS uses logical properties. See PR-4 above.

---

## Color Contrast (WCAG 1.4.3, 1.4.11)

All palette tokens pass WCAG AA in both light and dark themes:

| Token | Light | Dark | Min Ratio on BG |
|-------|-------|------|-----------------|
| Primary | `#2563EB` (blue-600) | `#60A5FA` | 5.18:1 / 8.31:1 |
| Success | `#047857` (emerald-700) | `#34D399` | 5.49:1 / 9.41:1 |
| Warning | `#B45309` (amber-700) | `#FBBF24` | 5.01:1 / 13.5:1 |
| Error | `#DC2626` (red-600) | `#F87171` | 4.85:1 / 7.24:1 |
| TextPrimary | `#0F172A` | `#F1F5F9` | 17.1:1 / 16.4:1 |
| TextSecondary | `#64748B` | `#94A3B8` | 4.53:1 / 6.43:1 |

Palette defined in `AppearanceThemeService.cs`. Change tokens at the source — fixes contrast everywhere.

---

## Focus and Target Size (WCAG 2.4.7, 2.5.8)

- **Focus indicator**: Global `:focus-visible` — 2px solid outline using `--mud-palette-primary`, 2px offset
- **Mouse/touch suppression**: `:focus:not(:focus-visible)` — no ring for pointer interactions
- **Forced colors**: Windows High Contrast gets 3px `Highlight` outline
- **Target size**: All `button`, `[role="button"]`, checkbox, radio, select, summary ≥ 24×24 CSS px (`--isl-target-min: 1.5rem`)

CSS tokens in `tokens.css`: `--isl-target-min`, `--isl-focus-ring-width`, `--isl-focus-ring-offset`.

---

## Dialog and Modal Patterns (WCAG 2.4.3)

MudBlazor dialogs automatically provide:
- `role="dialog"` + `aria-modal="true"`
- Focus trap (Tab cycles within dialog)
- Escape key dismissal

**Developers MUST add focus save/restore:**
```csharp
await AccessibilityFocusService.SaveFocusAsync();
var dialog = await DialogService.ShowAsync<MyDialog>(title, parameters, options);
var result = await dialog.Result;
await AccessibilityFocusService.RestoreFocusAsync();
```

Nested dialog calls are covered by the outermost save/restore.

---

## Keyboard Patterns (WCAG 2.1.1)

### Custom Interactive Elements
When `<button>` or `<a>` can't be used:
```razor
<MudCard tabindex="0" role="link" aria-label="View event: @evt.Title"
         @onclick="@(() => Navigate(evt))"
         @onkeydown="@(e => HandleKeyDown(e, evt))">
```
```csharp
private void HandleKeyDown(KeyboardEventArgs e, EventDto evt)
{
    if (e.Key is "Enter" or " ") Navigate(evt);
}
```

### Composite Widgets (Radio Groups, Tab Bars)
Use roving tabindex + arrow keys. See `EventReviewDialog.razor` star rating for reference implementation.

---

## WCAG 2.2 AA Criteria Mapping

| WCAG Criterion | Implementation |
|----------------|---------------|
| 1.1.1 Non-text Content | `Alt` on all images; `aria-label` on icon buttons |
| 1.3.1 Info and Relationships | Semantic landmarks (`<header>`, `<nav>`, `<main>`, `<footer>`), heading hierarchy |
| 1.3.4 Orientation | No orientation lock — responsive layout |
| 1.4.3 Contrast (Minimum) | Palette tokens ≥ 4.5:1 (text), ≥ 3:1 (large text/UI) |
| 1.4.11 Non-text Contrast | Focus outlines, form borders ≥ 3:1 |
| 1.4.12 Text Spacing | No clipping when spacing increased (MudBlazor handles) |
| 2.1.1 Keyboard | All interactive elements reachable + activatable |
| 2.4.1 Bypass Blocks | Skip-to-content link |
| 2.4.2 Page Titled | `<PageTitle>` on every routable page |
| 2.4.3 Focus Order | Logical DOM order, dialog focus trap, focus restore |
| 2.4.7 Focus Visible | Global `:focus-visible` + forced-colors fallback |
| 2.4.11 Focus Not Obscured | Content doesn't obscure focused element (MudBlazor handles) |
| 2.5.8 Target Size | ≥ 24×24 CSS px via `--isl-target-min` |
| 3.2.6 Consistent Help | Help/contact links in consistent footer position |
| 3.3.1 Error Identification | `role="alert"` wraps on error messages |
| 3.3.2 Labels or Instructions | MudBlazor `Label` parameter on all inputs |
| 3.3.7 Redundant Entry | No duplicate data entry in multi-step flows |
| 3.3.8 Accessible Authentication | Login via Keycloak redirect (no CAPTCHA) |
| 4.1.3 Status Messages | `IAccessibilityAnnouncerService` for dynamic updates |

---

## Component Development Checklist

Before merging any `.razor` component:

- [ ] Page has `<PageTitle>` and one `<h1>` (use `HtmlTag="h1"` on MudText)
- [ ] All images have meaningful `Alt` or `Alt=""` for decorative
- [ ] All form inputs have labels (`Label` parameter or `<label for="">`)
- [ ] All icon-only buttons have `aria-label`
- [ ] Custom interactive elements have `role`, `tabindex`, keyboard handler
- [ ] Error displays wrapped in `<div role="alert">`
- [ ] Success messages wrapped in `<div role="status">`
- [ ] Dialog callers use `SaveFocusAsync()`/`RestoreFocusAsync()`
- [ ] Data-loading pages announce results via `IAccessibilityAnnouncerService`
- [ ] CSS uses logical properties only (no physical direction)
- [ ] Heading hierarchy is sequential (h1 → h2 → h3)
- [ ] No color-only information indicators

---

## CSS Utilities

### `.sr-only`
Visually hidden but accessible to screen readers. Defined in `StyleGlobal.css`.

```razor
<span class="sr-only">Search results: 42 events found</span>
```

### `.skip-link`
Skip-to-content link. Automatically present in MainLayout. No page-level action needed.

---

## Media Query Support

| Query | Purpose | Behavior |
|---|---|---|
| `prefers-reduced-motion: reduce` | Users who disabled animations | All transitions/animations set to ~0ms |
| `prefers-contrast: more` | Users who need stronger contrast | Shadows strengthened |
| `forced-colors: active` | Windows High Contrast mode | Focus outlines use system colors |

---

## Testing

### Current Test Coverage

| Layer | Project | What It Tests |
|---|---|---|
| bUnit component tests | `Explore.Blazor.Client.Tests/Accessibility/` | Rendered markup: `role="alert"`, `alt` text, ARIA attributes |
| Architecture convention tests | `Event.Architecture.Tests/AccessibilityConventionTests.cs` | File scanning: h1 in pages, MainLayout landmarks, CSS physical-direction ban |
| CI pipeline | `.github/workflows/test.yml` | Build + all test suites on every push/PR |

### Running Accessibility Tests

```bash
# bUnit accessibility and component tests
dotnet test --project Explore.Blazor.Client.Tests/Explore.Blazor.Client.Tests.csproj --configuration Release --verbosity quiet

# Architecture convention tests, including accessibility guardrails
dotnet test --project Event.Architecture.Tests/Event.Architecture.Tests.csproj --configuration Release --verbosity quiet
```

Use TUnit `--treenode-filter` only for locally verified focused runs. Do not use VSTest-style `--filter` examples for TUnit projects.

### Manual Testing Checklist

- Keyboard-only navigation: Tab through all interactive elements, verify focus visibility.
- Screen reader (NVDA/VoiceOver): critical flows (event browse, registration, dialog open/close).
- Dialog flows: verify focus trap, Escape dismissal, focus restoration to trigger element.
- Dark mode: verify all text passes WCAG AA contrast ratios.
- RTL mode: verify layout mirrors correctly, no text overlap.

Key experience classes for manual accessibility verification:
- Anonymous browsing (event list, event detail, about, contact)
- Authenticated user (registrations, reviews, profile settings)
- Admin panels (instance, tenant, organization settings)
- Dialog-heavy flows (event registration, aspect editing)
- Dark mode + RTL mode variants
- Configuration portability: keyboard-only upload and section selection,
  blocking/warning announcement, mapping and approval labels, preview-to-apply
  focus movement, receipt/history reading, rollback confirmation, mobile width,
  forced colors, and RTL layout. Actions must disappear with their HAL relation
  without leaving an enabled but unauthorized control.

---

## Architecture Test Coverage

Convention tests in `Event.Architecture.Tests/` enforce accessibility rules at build time.

### AccessibilityConventionTests (8 tests)

| Test | Rule |
|---|---|
| `RoutablePages_MustContainH1Heading` | Every routable page has `<h1>` (excludes Settings wrapper pages where `<h1>` is in active tab) |
| `MainLayout_MustContainSkipLink` | `MainLayout.razor` contains skip-to-content link |
| `MainLayout_MustContainMainLandmark` | `<main>` landmark present |
| `MainLayout_MustContainHeaderLandmark` | `<header>` landmark present |
| `MainLayout_MustContainNavigationLandmark` | `<nav>` with `aria-label` present |
| `MainLayout_MustContainAriaLiveRegions` | Polite and assertive `aria-live` regions present |
| `ScopedCss_MustNotUsePhysicalDirectionProperties` | **ADVISORY** — flags `margin-left/right`, `padding-left/right`, `border-left/right` in `.razor.css` (Phase 5 RTL fix) |
| `ScopedCss_MustNotUsePhysicalPositionProperties` | **ADVISORY** — flags `left:`/`right:` positioning in `.razor.css` |

`WorkspaceShellScenarioMatrixTests` complements these static checks with rendered rows for anonymous, seeker, organizer, tenant-admin, instance-admin, and multi-role shells. Mobile and desktop use the same semantic application-workspace navigation; browser placement, focus order, RTL mirroring, and console checks are verified by the manual matrix in `DOCK_LAYOUT.md`.

Personal Settings uses two deliberately distinct navigation labels when administrative scopes are available: `Settings scopes` changes the governed scope, while `Personal settings sections` changes the focused section. The section navigation precedes content in DOM and focus order, remains sticky below the fixed app bar on desktop, and becomes non-sticky above the content on narrow viewports. View all keeps one page `h1`, gives every rendered section a unique `h2`/`aria-labelledby` pair, announces metadata-search result counts politely, uses logical CSS, and disables section entrance motion when `prefers-reduced-motion` is active.

### AuthorizationParityTests (3 tests)

| Test | Rule |
|---|---|
| `RegisteredResourceKinds_ShouldHave_FallbackCase` | Every resource kind has a fallback case in authorization |
| `RegisteredResourceKinds_ShouldHave_CerbosPolicy` | Every resource kind maps to a Cerbos policy file |
| `CerbosPolicies_ShouldHave_FallbackCase` | Every Cerbos policy defines a fallback case |

### Service Integration Guide

**Announcer in data-loading pages:**
```csharp
@inject IAccessibilityAnnouncerService Announcer

private async Task LoadData()
{
    _isLoading = true;
    var result = await EventService.GetEventsAsync();
    _isLoading = false;
    await Announcer.AnnouncePoliteAsync($"{result.Count} events loaded");
}
```

**Focus management after navigation:**
```csharp
@inject IAccessibilityFocusService Focus
@inject NavigationManager Navigation

protected override void OnInitialized()
{
    Navigation.LocationChanged += async (_, _) =>
        await Focus.FocusOnNavigateAsync();
}
```

**Focus save/restore in dialogs:**
```csharp
await Focus.SaveFocusAsync();
var dialogResult = await DialogService.ShowAsync<MyDialog>();
await Focus.RestoreFocusAsync(fallbackSelector: "#trigger-button");
```

---

## Key Files

| File | Purpose |
|---|---|
| `Explore.Blazor.Client/wwwroot/js/accessibility.js` | JS interop module (focus, announce, save/restore) |
| `Explore.Blazor.Client/Contracts/Services/Accessibility/IAccessibilityAnnouncerService.cs` | ARIA live region announcements |
| `Explore.Blazor.Client/Contracts/Services/Accessibility/IAccessibilityFocusService.cs` | Focus management |
| `Explore.Blazor.Client/Services/Accessibility/AccessibilityAnnouncerService.cs` | Announcer implementation |
| `Explore.Blazor.Client/Services/Accessibility/AccessibilityFocusService.cs` | Focus implementation |
| `Explore.Blazor.Client/Layout/MainLayout.razor` | Page shell (skip-nav, landmarks, live regions) |
| `Explore.Blazor/wwwroot/css/utilities.css` | `.sr-only`, `.skip-link`, focus-visible, media queries |
| `Explore.Blazor/wwwroot/css/tokens.css` | `--isl-target-min`, `--isl-focus-ring-*` tokens |
| `Explore.Blazor/wwwroot/css/base.css` | Target size minimums on interactive elements |
| `Explore.Blazor.Client/Services/AppearanceThemeService.cs` | WCAG AA compliant color palette |
| `Explore.Blazor.Client.Tests/Accessibility/` | bUnit accessibility tests |
| `Event.Architecture.Tests/AccessibilityConventionTests.cs` | Static file-scanning convention tests |
| `.github/workflows/test.yml` | CI pipeline running all tests |
| `Explore.Blazor.Client/Models/LanguageContext.cs` | Direction override + EffectiveIsRtl |
| `Explore.Blazor.Client/Providers/LanguageProvider.razor` | Language/direction cascading provider |
| `Explore.Blazor/Extensions/BffPreferenceEndpoints.cs` | Direction preference BFF endpoint |
| `Explore.Domain/Settings/Definitions/AppearanceSettingDefinitions.cs` | Direction setting definition |
| `.agents/skills/accessibility/SKILL.md` | AI agent accessibility rules |
