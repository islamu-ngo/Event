<!-- ABOUTME: In-depth implementation guide for the Blazor dock layout system used by shell and workspace UI. -->
<!-- ABOUTME: Explains descriptors, state, responsiveness, stacking, overlays, persistence, accessibility, and current production consumers. -->

# Dock Layout

> **Audience:** Contributors | AI agents
> **Status:** Implemented
> **Owner:** Frontend
> **Last Verified:** 2026-07-22
> **Source Anchors:** `Explore.Blazor.Client/Services/Docking/`, `Explore.Blazor.Client/Components/Docking/`, `Explore.Blazor.Client/Layout/MainLayout.razor`, `Explore.Blazor.Client/Layout/MainLayout.razor.cs`, `Explore.Blazor.Client/Layout/MainLayout.razor.css`, `Explore.Blazor.Client/Components/Shell/ShellDockPanels.cs`, `Explore.Blazor.Client/Components/Shell/AiAssistantRail.razor`, `Explore.Blazor.Client/Components/Shell/WorkspaceNavigationHost.razor.cs`, `Explore.Blazor.Client/Components/Shell/Workspaces/EventsWorkspaceNavigation.razor.cs`, `Explore.Blazor.Client/Pages/Events/EventList.razor`, `Explore.Blazor.Client/Pages/Events/EventList.razor.cs`, `Explore.Blazor.Client/Pages/Events/EventList.razor.css`, `Explore.Blazor.Client/Pages/Events/EventListDockingController.cs`, `Explore.Blazor.Client/Pages/Events/EventDockPanels.cs`, `Explore.Blazor.Client/Services/Interop/LocalStorageDockLayoutPersistence.cs`, `Explore.Blazor.Client/wwwroot/js/dock-layout-persistence.js`, `Explore.Blazor.Client.Tests/Services/Docking/`, `Explore.Blazor.Client.Tests/Components/Docking/`, `Explore.Blazor.Client.Tests/Layout/DockRegistrationTests.cs`, `Event.Architecture.Tests/DockLayoutArchitectureTests.cs`

## Purpose

The dock layout system is the generic panel-hosting layer for two different UI levels:

- shell chrome owned by `MainLayout`
- page-local workspace panels owned by a feature page such as `EventList`

It is descriptor-driven rather than component-hardcoded. A panel is defined by stable metadata plus runtime state, then rendered by shared host components. That lets the same engine handle:

- the shell start dock for navigation
- the shell end dock for the AI assistant
- the event-list workspace end dock for "Customize View"
- the event-list preview inspector for event details
- temporary modal-like overlays on mobile or constrained widths

This document describes the current implementation, not a speculative future workbench.

## Current Production Surface

The current panel catalog is small and explicit.

| Panel | Id | Scope | Side | Default mode | Width | Resizable | Closeable | Persisted |
|---|---|---|---|---|---:|---|---|---|
| Workspace navigation | `shell.workspace-nav` | `Shell` | `Start` | `Docked` | 280 | Yes | Yes | Yes |
| AI Assistant | `shell.ai-assistant` | `Shell` | `End` | `Docked` | 360 | Yes | Yes | Yes |
| Customize View | `events.customize-view` | `Workspace` | `End` | `Docked` | 320 | Yes | Yes | Yes |
| Event Preview | `events.event-preview` | `Workspace` | `End` | `Inspector` | 440 | No | Yes | No |

Sources:

- `Explore.Blazor.Client/Components/Shell/ShellDockPanels.cs`
- `Explore.Blazor.Client/Pages/Events/EventDockPanels.cs`

AI assistant panel behavior:

- `AiAssistantRail` preserves the shell dock descriptor, persisted open/width state, fixed/mobile overlay projection, and close/backdrop behavior supplied by the dock layer.
- Conversation history, reference search, send, confirm, and reject actions flow through `IAiAssistantClientService` and `AiAssistantConversationState`.
- Proposed-action and reference affordances are rendered from HAL links only; the dock layer does not authorize assistant actions.
- Full-panel bUnit coverage exercises unavailable state, history/message loading, create, send, reference search/select/remove, HAL-gated proposal actions, and safe command errors.

## Architecture Overview

The runtime is split into four layers.

1. Descriptor layer

- `DockPanelId` is the stable panel identifier.
- `DockPanelDescriptor` defines immutable metadata: scope, side, default mode, title, aria label, default width, min/max width, order, resize capability, close capability, persistence policy, stack strategy, mobile presentation, responsive priority, and whether responsive pressure may auto-close the panel.

2. State layer

- `DockPanelState` stores mutable runtime state: `IsOpen`, `Mode`, `Width`, `Order`, `IsActive`.
- `DockLayoutState` is the scoped state engine and registry.

3. Rendering layer

- `DockLayoutHost` owns a grid region for one `DockScope`.
- `DockSideHost` renders inline docked panels for one side.
- `DockOverlayHost` renders overlays, inspectors, temporary panels, and projected mobile panels.
- `DockPanelHost` renders one panel shell.
- `DockTabStrip` handles same-side tabbed stacks.
- `DockResizeHandle` handles keyboard and pointer resizing.

4. Persistence and adapter layer

- `ServerBackedDockLayoutPersistence` stores authenticated snapshots through the existing user-settings API and delegates anonymous snapshots to tenant-discriminated browser `localStorage`.
- `MainLayout` controls workspace navigation directly through `DockLayoutState` and adapts only AI policy state through `AiAssistantState`.
- `EventListDockingController` adapts workspace dock state to page-local booleans and autosave.

## State Model

`DockLayoutState` is the central engine for open/close, activation, width changes, snapshots, and viewport-aware projection.

Important properties and behaviors:

- `_entries` is keyed by `DockPanelId`, so ids must be unique within the scoped registry.
- `Register` validates descriptors and creates a closed default `DockPanelState`.
- `GetPanels(scope, side)` returns panels ordered by runtime `Order`, then title.
- `Open(id)` marks the target panel active and open. On desktop, activation is normalized within the same `(scope, side)` group. On mobile, activation is normalized within the same `scope` so only one modal surface is active while other open panels preserve user intent state.
- `Activate(id)` changes active selection without changing openness, using the same desktop/mobile activation grouping.
- `Close(id)` and `Resize(id)` enforce descriptor capabilities.
- `LastChangeReason` classifies why state changed so autosave can ignore viewport-only adjustments.

The main change reasons are:

- `Registration`
- `UserAction`
- `ViewportPolicy`
- `SnapshotRestore`
- `Reset`
- `Refresh`

That separation matters because responsive projections do not overwrite user preferences. Viewport changes raise `ViewportPolicy` so renderers can react, but the state engine no longer closes panels merely because the viewport became narrow.

## Scope Model

`DockScope` is what makes the system able to stack shell and page docks at the same time.

- `Shell` is global app chrome.
- `Workspace` is page-owned panel space inside page content.

The shell host wraps the page body in `MainLayout.razor`:

```razor
<DockLayoutHost Scope="@DockScope.Shell">
    <MudMainContent>
        <main id="main-content">
            @Body
        </main>
    </MudMainContent>
</DockLayoutHost>
```

The event list page creates its own nested workspace host inside that shell content region:

```razor
<DockLayoutHost Scope="@DockScope.Workspace" Class="event-list__workspace">
    <div class="event-list__page">
        <main class="event-list__main">
            ...
        </main>
    </div>
</DockLayoutHost>
```

This nesting is why the UI can show, for example:

- shell AI assistant on the outer shell end side
- event-list customization dock on the inner workspace end side

They do not compete for the exact same grid track. The workspace dock lives inside the page content region after the shell dock host has already allocated shell chrome space.

## Side Model And RTL Safety

`DockSide` uses logical directions:

- `Start`
- `End`
- `Bottom`

The engine and CSS avoid physical left/right assumptions. CSS uses logical properties such as:

- `inset-inline-start`
- `inset-inline-end`
- `border-inline-start`
- `border-inline-end`

This is also why overlay animations have explicit `:dir(rtl)` overrides in `DockSideHost.razor.css`, `DockOverlayHost.razor.css`, and `AiAssistantRail.razor.css`.

## Mode Model

`DockMode` is explicit and important.

- `Docked`: panel participates in inline layout.
- `Overlay`: floating overlay without consuming track width.
- `Temporary`: temporary overlay used for mobile-style or projected dock behavior.
- `Inspector`: overlay-style detail panel, currently used by event preview.
- `Collapsed`: registered but not rendered as a normal open panel.

Two implementation details are important here:

1. A panel can remain `Docked` in state but still be rendered as an overlay.

`DockLayoutState.ShouldRenderDockedPanelAsOverlay(entry)` is the projection rule. `DockOverlayHost` converts those projected entries to effective `Temporary` render entries without rewriting stored state.

2. Inspector is a rendering contract, not just a label.

`events.event-preview` defaults to `DockMode.Inspector`, so it opens in `DockOverlayHost` with dialog semantics, backdrop handling, focus trap, and overlay z-index behavior.

## Descriptor Behavior Policy

The dock engine now keeps behavior policy on each `DockPanelDescriptor` instead of inferring every decision from `DockSide`.

Current descriptor policy fields:

- `StackStrategy`: declares whether same-side peers should use an accessible tab stack or a visible split stack.
- `MobilePresentation`: records how a docked panel should be projected on mobile. The current implementation uses `TemporaryOverlay` as the safe default.
- `ResponsivePriority`: gives future planners a stable ordering signal for constrained layouts without relying on registration order.
- `CanAutoCloseWhenConstrained`: defaults to `false`, preserving user open intent unless a panel explicitly opts into responsive closure.

Production descriptors use those defaults deliberately:

- `shell.workspace-nav`: tabbed stack strategy and priority `10`.
- `shell.ai-assistant`: split stack strategy and priority `20`.
- `events.customize-view`: split stack strategy and priority `20`.
- `events.event-preview`: split stack strategy and priority `30`, non-persistent inspector state.

This metadata is intentionally small. It avoids introducing a separate planner framework while still making coexistence, stacking, and future responsive decisions explicit and testable.

## Rendering Pipeline

### `DockLayoutHost`

`DockLayoutHost` is the root layout component for one scope.

Responsibilities:

- subscribes to `DockLayoutState.Changed`
- subscribes to MudBlazor `IBrowserViewportService`
- computes inline grid track widths from currently open docked panels
- renders side hosts for `Start`, `End`, and `Bottom`
- renders a `DockOverlayHost` for the same scope

Its grid is defined in `DockLayoutHost.razor.css`:

```css
grid-template-columns: var(--dock-layout-start-width, 0px) minmax(0, 1fr) var(--dock-layout-end-width, 0px);
grid-template-rows: minmax(0, 1fr) auto;
grid-template-areas:
    "start content end"
    "bottom bottom bottom";
```

That means docked panels reserve actual inline layout space instead of forcing page-level compensation hacks.

### `DockSideHost`

`DockSideHost` renders only desktop inline docked panels for one `(scope, side)` pair.

Key behavior:

- filters to `IsOpen && Mode == Docked`
- suppresses mobile rendering entirely
- suppresses any entry that `ShouldRenderDockedPanelAsOverlay`
- keeps closed panels mounted briefly for exit animation

If more than one docked panel is open on the end side for the same scope, it renders them as a side-by-side split stack in descriptor order. Start and bottom multi-panel groups keep the tab-stack fallback and render only the active tab body.

### `DockOverlayHost`

`DockOverlayHost` renders all non-inline panels for one scope.

It includes:

- backdrop
- focus trap via `MudFocusTrap`
- body scroll lock
- saved focus / restored focus
- Escape-to-close
- backdrop click close
- exit animation persistence

It treats these as overlays:

- `Overlay`
- `Temporary`
- `Inspector`
- projected docked panels from `ShouldRenderDockedPanelAsOverlay`

### `DockPanelHost`

`DockPanelHost` renders one panel shell.

Important behavior:

- panel width is emitted as `--dock-panel-width`
- `role="dialog"` and `aria-modal="true"` are used only when `IsModal == true`
- inline docked panels use `role="complementary"`
- resize handles are rendered only for resizable `Docked` `Start`/`End` panels
- panel content is provided as a `CascadingValue` of `DockPanelEntry`

The host intentionally does not render a generic title/header bar. Panel content owns its own internal chrome.

### `DockTabStrip`

This is the implementation for same-side same-scope stacks that intentionally remain tabbed, such as start-side and bottom-side stacks.

It provides:

- `role="tablist"`
- `role="tab"`
- active/inactive roving `tabindex`
- keyboard navigation with Arrow keys, Home, End
- focus handoff through `IAccessibilityFocusService`

The active tab maps to the active panel body through stable ids created by `DockElementIds`.

### `DockResizeHandle`

The resize handle is accessible and descriptor-bound.

Supported keyboard interactions:

- `ArrowLeft` / `ArrowRight`
- `Shift+ArrowLeft` / `Shift+ArrowRight` for larger steps
- `Home` for descriptor minimum width
- `End` for descriptor maximum width

Pointer interactions:

- primary pointer only
- pointer capture via `/js/dock-resize.js`
- side-aware delta calculation so `Start` and `End` resize in opposite directions
- descriptor clamping through the same `DockLayoutState.Resize` path

## Responsive Behavior

This is the main policy the user asked about.

### Hard Mobile Threshold

Hard mobile is not every small breakpoint. The code currently treats only MudBlazor `Breakpoint.Xs` as mobile.

In `DockLayoutHost`:

```csharp
var isMobile = args.Breakpoint is Breakpoint.Xs;
DockLayoutState.UpdateViewport(args.BrowserWindowSize.Width, isMobile);
```

That means:

- `Xs` uses mobile behavior
- `Sm` and above still use desktop/constrained-desktop behavior

### Content Floor

`DockLayoutHost.MinimumContentWidth` supplies a generic per-scope content floor to `DockLayoutState`. MainLayout maps the active workspace to Events `375`, AI `520`, Settings `560`, or Studio `720`; the dock engine does not know workspace names.

The floor is projection policy only. Floor and viewport changes use `ViewportPolicy`, never enter snapshots, and cannot trigger the `UserAction`/`Reset` persistence lane.

### Start-side Projection Policy

The start side is usually the navigation dock.

Rules:

1. On hard mobile, open docked panels render through overlay chrome instead of reserving inline grid width.
2. On constrained desktop, a start docked panel is projected to overlay chrome when opening it inline would leave less than the caller-supplied content floor after accounting for docked end panels in the same `DockScope`.
3. Projection does not close the start panel. It changes only the rendering path.

This produces the important middle behavior you asked about:

- the panel remains logically open
- it stops consuming grid width while projected
- it is shown as a temporary overlay with backdrop chrome instead

That is the current implementation of the "almost full-screen while the rest of the page dims" behavior for constrained start-side docks.

### End-side Preservation Policy

The end side preserves open state by default.

Earlier revisions closed all but one open end panel under mobile or constrained-width pressure. That close-first policy caused shell and workspace docks to fight each other: opening `events.event-preview` or `events.customize-view` could close an unrelated shell right-side panel. The current implementation treats `DockPanelState.IsOpen` as user intent. Opening or resizing one panel does not silently close another end panel just because the viewport is narrow.

Rendering still adapts:

- desktop docked end panels remain inline when they fit the host layout
- inspector/temporary/overlay panels render through `DockOverlayHost`
- mobile can keep multiple panels open in state, but only the active overlay surface for a scope is rendered as the modal surface

This means `shell.ai-assistant`, `events.customize-view`, and `events.event-preview` can preserve their open state independently. The renderer decides what is visible and active for the current viewport instead of destroying panel state.

### Mobile Overlay Sizes

The overlay CSS distinguishes between start and end overlays on mobile.

On `max-width: 600px`:

- start overlays use `inline-size: min(88vw, var(--dock-panel-width, 360px))`
- end overlays use `inline-size: min(100vw, var(--dock-panel-width, 440px))`

So current mobile behavior is:

- navigation-style start panels become partial-width overlay drawers
- end-side temporary/inspector/overlay panels keep descriptor-driven width until the viewport is narrower than that width

This matches the three practical responsive outcomes you described:

1. stay docked inline when space allows
2. project to overlay chrome when constrained
3. on mobile end-side panels, clamp to the viewport naturally instead of snapping to unconditional fullscreen

## Horizontal Stacking Behavior

There are two different kinds of "stacking" in the current implementation.

### 1. Cross-scope horizontal stacking

This is implemented today.

Example:

- `shell.ai-assistant` is open in the shell host
- `events.customize-view` is open in the nested workspace host

Because the workspace host lives inside the shell host content region, both can remain visible and appear horizontally stacked from the user's perspective.

This is not a split-pane manager inside one host. It is nested host composition:

- outer shell grid reserves shell AI width
- inner workspace grid reserves customize-view width inside the already-shrunk page area

### 2. Same-scope same-side stacking

This now has two policies based on side.

For desktop `End` docks, multiple open docked panels in the same scope render side-by-side:

- `DockSideHost` renders a split-stack container
- every open end-side docked panel body is mounted in descriptor order
- each panel keeps its own descriptor width via `--dock-panel-width`
- `DockLayoutHost` reserves the combined end-side width because it already sums open docked panel widths

For `Start` and `Bottom` stacks, the engine keeps the tabbed fallback:

- `DockSideHost` renders `DockTabStrip`
- only the active panel body is rendered
- the inactive open panels stay open in state but are not shown side-by-side

So if you want two panels on the same right/end side of the same scope today, the engine gives you:

- visible side-by-side dock stack

For left/start and bottom groups, it gives you:

- tabbed stack

not:

- resizable nested groups
- detachable windows

## Event Preview Inspector

The event preview is the current popup-style dock for event details.

Implementation:

- descriptor id: `events.event-preview`
- scope: `Workspace`
- side: `End`
- mode: `Inspector`
- width: `440`
- not persisted
- not resizable

When a card is selected in `EventList`:

1. the page sets `_selectedEvent`
2. `_detailDrawerOpen = true`
3. `EventListDockingController.OpenEventPreview()` opens the dock panel
4. the panel renders through `DockOverlayHost`
5. event detail and sessions are fetched asynchronously

The inspector is therefore not just a normal sidebar. It is an overlay-hosted panel with modal behavior.

Opening the inspector no longer closes `events.customize-view`. The page preserves both user intents and lets dock projection decide which surface is inline, overlayed, active, or hidden by the mobile modal rule. Likewise, opening customization no longer closes the event preview; explicit close actions still clear preview-only transient state.

### Inspector close behavior

Closing can happen through:

- panel header close button in page-owned panel content
- backdrop click
- Escape key
- workspace synchronization after state changes

When it closes, `EventList` clears transient preview state such as:

- loaded event detail DTO
- loaded session collection
- inline registration popup visibility
- tag/category popup visibility

## Nested Popup Behavior Inside Event Preview

The event preview dock also hosts secondary popup-style surfaces inside the panel body.

These are not separate dock panels. They are page-owned overlays inside the inspector body.

### Inline registration popup

`EventList.razor.css` anchors it to the preview panel body:

```css
[data-dock-panel-id="events.event-preview"] .dock-panel-host__body {
    position: relative;
}
```

Then the registration overlay uses absolute positioning over that body:

- `.drawer-reg-overlay` covers the inspector body with a blurred/dimmed layer
- `.drawer-reg-popup` is a centered popup card inside the inspector

This means the registration popup is modal relative to the preview dock, not modal relative to the whole page.

### Tag/category management popup

The event preview also toggles `_showTagCatPopup` from `OpenTagManagement()` and `OpenCategoryManagement()`.

Like the inline registration flow, this popup is preview-local state. It is cleared when:

- navigating to previous/next event
- closing the detail drawer
- clicking outside while a popup is open

Implementation summary:

- dock system owns the outer preview inspector
- event page owns popup layers inside the preview inspector body

## Shell Integration

`MainLayout` is the shell integration point.

Responsibilities:

- registers shell descriptors on initialization
- renders shell panel content through render fragments
- hydrates shell snapshot from `layoutKey = "shell"`
- derives workspace-navigation availability from `UiShellState` plus `IWorkspaceRegistry` and controls its panel directly through `DockLayoutState`
- mirrors `AiAssistantState` policy/open state into the AI dock and back on dock changes
- autosaves only meaningful user/reset shell layout changes
- resets shell layout by closing, restoring default mode, restoring default width, and deleting persisted state

Shell content registration is currently:

- `EventsWorkspaceNavigation` through `WorkspaceNavigationHost` for `shell.workspace-nav`
- `AiAssistantRail HostedInDock="true"` for `shell.ai-assistant`

### `WorkspaceNavigationHost` overlay chrome

`WorkspaceNavigationHost` receives the current `DockPanelEntry` as a cascading parameter. In overlay-like modes it renders the shared brand header and close action before the active workspace provider, so `EventsWorkspaceNavigation`, Settings navigation, and future providers inherit the same shell chrome.

### `AiAssistantRail` dual-mode behavior

`AiAssistantRail` supports two rendering modes:

- legacy fixed overlay rail when `HostedInDock == false`
- dock-owned content when `HostedInDock == true`

In docked mode it becomes layout-neutral content:

- `position: relative`
- `inline-size: 100%`
- `block-size: 100%`
- no fixed transform/visibility shell behavior

## Workspace Integration On Event List

`EventList` uses `EventListDockingController` so page rendering logic is not overloaded with persistence and synchronization details.

The controller handles:

- registering `Customize View` and `Event Preview`
- hydrating `layoutKey = "events"`
- synchronizing dock open state with `_customizationDrawerOpen` and `_detailDrawerOpen`
- autosaving workspace layout
- reset behavior
- unregistering panels on disposal

The important distinction is:

- `Customize View` is a durable user-preference workspace dock
- `Event Preview` is transient inspector UI and is intentionally not persisted

## Persistence Model

Persistence follows authentication state without introducing another storage model:

- authenticated users use `ServerBackedDockLayoutPersistence` over the existing `api/settings/user/{category}` settings API;
- anonymous users delegate to `LocalStorageDockLayoutPersistence` and `/js/dock-layout-persistence.js` under `dock_layout:v1:{tenantSlug}:`;
- an anonymous snapshot is promoted once when the user authenticates and no server snapshot exists;
- stale pre-tenant-discriminator keys are not read.

Snapshots are schema-versioned envelopes:

- `SchemaVersion`
- `LayoutKey`
- `Snapshot`

Current layout keys:

- shell layout: `shell`
- event workspace layout: `events`

### What is persisted

Only panels whose descriptor sets `PersistState = true` are included in snapshots.

Currently that means:

- `shell.workspace-nav`
- `shell.ai-assistant`
- `events.customize-view`

Not persisted:

- `events.event-preview`

### Autosave behavior

Both `MainLayout` and `EventListDockingController` debounce dock persistence by `500ms`.

Autosave only runs when:

- `LastChangeReason` is `UserAction` or `Reset`
- the scoped snapshot actually changed
- hydration is complete
- autosave is not currently suppressed

Autosave does not run for:

- registration changes
- refresh changes
- viewport policy changes

That prevents mobile/constrained projection side effects from becoming the new saved default layout.

### Restore behavior

`RestoreSnapshot` is defensive.

It ignores:

- wrong layout keys
- wrong scope panels
- unknown panel ids
- unsupported/corrupt payloads

It also normalizes restored state:

- widths are clamped to current descriptor bounds
- non-resizable panels keep current width
- non-closeable panels cannot be restored closed if they are supposed to remain open
- on desktop, only one active panel remains per `(scope, side)` group
- on mobile, only one active panel remains per `scope`

This mirrors runtime activation: restored snapshots preserve `IsOpen` as user intent, but active-state normalization follows the current viewport so a mobile restore cannot expose multiple modal-active surfaces in the same shell or workspace scope.

## Reset Behavior

There are two reset flows.

### Shell reset

`MainLayout.ResetShellDockLayoutAsync()`:

- closes closable shell panels
- restores their default modes
- restores default widths for resizable panels
- deletes the `shell` persisted snapshot
- resynchronizes bridge services

### Event workspace reset

`EventListDockingController.ResetWorkspaceDockLayoutAsync()` resets only the persistent customization workspace state and deletes the `events` snapshot.

The reset restores `events.customize-view` to its descriptor defaults:

- closed
- `DockMode.Docked`
- descriptor default width

It does not persist a replacement snapshot during the reset operation. The event preview inspector remains transient because `events.event-preview` has `PersistState = false`; hydration cannot resurrect it from local storage, and workspace reset does not treat preview state as persisted layout state.

## Accessibility Behavior

The dock system has substantial accessibility behavior built in.

### Inline docked panels

- rendered as `role="complementary"`
- no `aria-modal`
- no focus trap

### Overlay and inspector panels

- rendered as `role="dialog"`
- `aria-modal="true"`
- backdrop close button has an explicit accessible label
- focus is saved before opening
- body scroll is locked while open
- focus moves into the panel host
- focus restores to `#main-content` when overlay lifecycle ends

On mobile, multiple panels can remain open in state, but `DockOverlayHost` renders only the active overlay-eligible surface for a scope. This prevents simultaneous modal focus traps while preserving the user's open panel intent for later desktop expansion or tab/overlay reactivation.

The current policy is intentionally binary: panels rendered by `DockOverlayHost` are modal surfaces, while inline docked and split-stack panels are non-modal complementary surfaces. That keeps dimming, scroll lock, focus trap, Escape, and backdrop close semantics aligned instead of showing a dimmer for content that is still meant to coexist with the page.

### Tab stacks

- proper `tablist`/`tab`/`tabpanel` structure
- roving `tabindex`
- Arrow/Home/End keyboard navigation

### Split stacks

End-side split stacks are not tab lists. They render each open docked panel as `role="complementary"` content, so they avoid assigning `tabpanel` semantics to multiple simultaneously visible bodies. Active state still exists for styling and command targeting, but visibility is no longer limited to the active entry.

Split stacks also deliberately avoid overlay chrome: no backdrop, no `MudFocusTrap`, no body scroll lock, and no `aria-modal`. They are for parallel desktop work, not interruptive mobile/modal flows.

### Resize handles

- `role="separator"`
- `aria-orientation="vertical"`
- `aria-controls`
- `aria-valuemin`, `aria-valuemax`, `aria-valuenow`

## Motion And Visual Behavior

The dock system intentionally keeps close animations alive long enough to play reverse motion before unmount.

Patterns used:

- grid track transitions on `DockLayoutHost`
- side-panel exit animations in `DockSideHost`
- backdrop fade and slide animations in `DockOverlayHost`
- reduced-motion overrides with `animation-duration: 1ms` or `transition-duration: 1ms`

Visual ownership is split like this:

- dock hosts own placement, sizing, backdrop, modal behavior, and motion
- panel content owns titles, toolbar buttons, internal layout, and local popup behavior

### Workspace shell scenario matrix

`WorkspaceShellScenarioMatrixTests` covers the implemented product rows with real bUnit components and shell services:

| Capability/profile | Auth | Rail | Events navigation | Settings scopes | Default/revocation |
|---|---|---|---|---|---|
| Discovery visitor | Anonymous | Events only | Discovery shortcuts | Public preferences remain page-owned | Events |
| Organization-centric visitor | Anonymous | Events-only public shell | Branded Programs catalog | Public preferences remain page-owned | Organization catalog/Events shell |
| Event seeker | Authenticated | Events, optional AI, Settings | Discovery shortcuts | Personal | Last valid workspace or Events |
| Organizer | Authenticated | Events, Studio, optional AI, Settings | Studio is actor-contextual | Personal + authorized organization/group | Studio when configured and available |
| Tenant administrator without organizer capability | Authenticated | Events, optional AI, Settings; no Studio | No Studio provider | Personal + Tenant | Revoked Studio falls back to Events |
| Instance administrator only | Authenticated | Events, optional AI, Settings; no Studio | No Studio provider | Personal + Instance | Settings or last valid workspace |
| Multi-role user | Authenticated | Union in primary event → Studio → AI → Settings order | Contextual provider | Union of explicitly authorized scopes | Last valid workspace |

Mobile and desktop rows intentionally render one semantic `<nav aria-label="Application workspaces">` DOM. The Xs CSS projection changes placement, not links or accessibility semantics. `Settings` remains last and becomes the bottom-pinned gear on desktop.

Personal Settings owns page-local navigation rather than a shell dock panel. `/settings/personal` renders searchable View all; focused paths render one section. At desktop widths, `SettingsLayout` uses a two-column grid whose `Personal settings sections` navigation sticks below the fixed app bar while the content column scrolls normally. Below `59.997em`, the grid becomes one column and navigation returns to document flow above content. The independent `Settings scopes` navigation is omitted for Personal-only users and stacks its authorized links at 320/375px.

### Responsive visual QA matrix

Use this matrix for focused manual browser verification after changing dock layout or responsive behavior.

The matrix covers these widths and modes:

| Scenario | Viewport | Direction | Motion | Purpose |
|---|---:|---|---|---|
| `mobile-320-ltr` | 320 x 844 | LTR | Default | Minimum supported width, bottom rail, and active-modal behavior. |
| `mobile-375-rtl` | 375 x 844 | RTL | Default | Logical start/end placement and RTL animation direction. |
| `mobile-375-reduced-motion` | 375 x 844 | LTR | Reduced | Reduced-motion override contract for mobile overlays. |
| `tablet-768-ltr` | 768 x 900 | LTR | Default | Tablet-sized content pressure without hard fullscreen snap. |
| `desktop-1280-ltr` | 1280 x 900 | LTR | Default | Normal desktop inline dock tracks and split stack behavior. |
| `wide-1920-ltr` | 1920 x 1080 | LTR | Default | Wide desktop shell + workspace parallel dock layout. |

Run each width for the applicable anonymous, seeker, organizer, tenant-admin, and instance-admin rows above. Verify authoritative rail order, bottom-nav reachability at Xs, active provider content, Settings scope links, shell/workspace dock hosts, logical focus order, and zero console errors. For Personal Settings, also verify all nine View all sections, metadata search and its empty state, sticky desktop navigation at 1280/1920px, stacked non-overflowing navigation at 320/375/768px, distinct scope/section landmarks, and reduced-motion suppression. Enable the operating system or browser reduced-motion preference for the reduced-motion scenario.

## What "Everything Related" Means In Current Code

Today, the full dock system includes:

- descriptor-defined panels with stable ids
- scoped shared state engine
- nested shell/workspace host composition
- docked inline tracks
- end-side same-scope split stacks
- temporary and inspector overlays
- constrained-width projection of docked panels into overlay chrome
- projection-first responsive behavior that preserves open intent instead of closing panels as a side effect of viewport pressure
- start/bottom same-side tab stacking
- cross-scope horizontal stacking through nested hosts
- keyboard and pointer resizing
- schema-versioned authenticated server persistence plus tenant-discriminated anonymous local persistence
- per-scope autosave and reset
- focus, scroll-lock, Escape, backdrop click, and ARIA semantics
- inspector-local popups inside the event preview panel body
- architecture tests that prevent backsliding into a central panel enum or page-level shell compensation hacks

## Important Non-Goals In The Current Implementation

The following are not implemented today:

- detachable windows
- drag-reorder tab stacks
- arbitrary panel plugins loaded at runtime
- persisted inspector/detail popup state

If future work needs those features, it should be added as a new documented behavior, not inferred from the current architecture.

## Verification Surface

The implementation is heavily test-covered.

Key test areas:

- `DockLayoutStateTests`: registration, open/close, resize clamping, projection-first responsive state preservation, snapshot restore, reset
- `DockHostTests`: rendering, mobile projection, tab stacks, focus trap, Escape/backdrop close, exit animations, resize interactions
- `EventDockPanelsTests`: descriptor contracts for workspace panels
- `LocalStorageDockLayoutPersistenceTests`: schema versioning, corrupt data handling, non-browser safety
- `DockRegistrationTests`: DI registration
- `DockLayoutArchitectureTests`: no central panel enum and no new page-level shell compensation hacks
- `WorkspaceShellScenarioMatrixTests` for profile/auth/capability/viewport semantics and manual responsive proof at 320, 375, 768, 1280, and 1920 px, including RTL and reduced-motion variants

Those tests are the best proof of intended behavior when changing widths, breakpoints, stacking logic, or modal behavior.
