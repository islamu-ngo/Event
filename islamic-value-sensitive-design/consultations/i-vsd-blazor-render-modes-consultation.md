<!-- ABOUTME: Canonical I-VSD consultation report on Blazor WebApp render modes, self-hosting autonomy, and multi-tenant economics. -->
<!-- ABOUTME: Evaluates InteractiveServer, InteractiveWebAssembly, and InteractiveAuto across justice, resource stewardship, and sovereignty. -->

# Blazor WebApp Render Modes, Self-Hosting Autonomy, And Multi-Tenant Economics — I-VSD Consultation Report

Last Updated: 2026-09-13

## Review Metadata

- Mode: standalone
- Subject: blazor-render-modes-and-hosting-autonomy
- Workstream: none
- Report kind: consultancy-report
- Report status: current
- Disposition: advisory
- Evidence cutoff: 2026-09-13
- Reviewed input: working-tree (`Explore.Domain/Policies/RenderPolicy.cs`, `Explore.Blazor/Components/App.razor`, `Explore.Blazor.Client/Services/RuntimeRenderPolicyService.cs`, `Explore.Blazor.Client/Pages/Admin/Tenant/Components/TenantRenderPolicySection.razor`, `dev/_journal/MAJOR_DECISIONS.md:545`)
- Supersedes: none

## Scope

This consultation reviews the architectural, operational, economic, and moral dimensions of Blazor WebApp render mode management within the ISLAMU Event platform. The scope encompasses the core flagship instance ("ISLAMU"), diverse self-hosting topologies (ranging from low-power $4/month VPS or local masjid servers to enterprise infrastructure), and multi-tenant SaaS hosting tiers.

### In Scope

- The three primary Blazor interactive render modes: `InteractiveServer`, `InteractiveWebAssembly`, and `InteractiveAuto`, alongside static Server-Side Rendering (`Static SSR`) with enhanced navigation.
- Infrastructure and hardware justice: compute burden distribution between client devices and server hosts, memory footprints (SignalR circuit overhead vs. client browser WASM runtime), and network payload implications.
- Sovereignty and operational autonomy for self-hosters (*Istiqlal* & *Tamkin*): enabling resource-constrained grassroots organizations (masajid, halaqat, local charities) to eliminate server memory bloat via client-side execution.
- Multi-tenant SaaS economics and avoiding contractual uncertainty (*Gharar*): aligning tenant pricing tiers with actual server compute consumption (WASM client-compute for free/community tiers vs. dedicated server circuits for premium tiers).
- The existing codebase policy model: `RenderPolicy` domain entity, `RenderPolicyPresetEnum`, `RuntimeRenderPolicyService`, and presentation settings controllers.
- Codebase architectural refactoring: removing page-level hardcoded `@rendermode` debt across all Razor pages, establishing a clean boundary between the root router and route-level cohorts, and enabling lazy assembly loading.
- High-latency, spotty connectivity, and international diaspora accessibility: examining the user experience of SignalR circuits vs. cached WebAssembly in low-bandwidth or geographically distant environments.

### Out Of Scope

- Modifying underlying ASP.NET Core Blazor runtime code or WebAssembly JIT/AOT compilers.
- External payment gateway contracts (governed separately by `i-vsd-paid-event-payments-consultation.md`).
- Issuing religious-legal rulings (*fatwas*) regarding the permissibility of specific cloud service contracts, hardware financing, or commercial subscriptions.

---

## Claim Boundary

This report provides provider-responsibility design reasoning and technical architecture recommendations grounded in Islamic Value-Sensitive Design (I-VSD). It evaluates trade-offs in autonomy, equity, stewardship, and transparency. It does not issue religious-legal judgments (*halal*, *haram*, *makrooh*, *wajib*), nor does it certify software compliance with Sharia standards. Formal religious-legal determinations concerning commercial agreements, financing, or contract structures remain the sole authority of qualified Islamic scholars. Because the repository operates in greenfield pre-release development with 0 external adopters, backward compatibility is explicitly rejected in favor of optimal architectural purity and maintainability.

---

## Common Overlooked Failures And Outcomes

### Failures And Their Evidence Limits

1. **Monolithic Render Mode Imposition**: Assuming that one render mode fits all deployments. Imposing `InteractiveServer` globally forces grassroots self-hosters to allocate substantial RAM for persistent SignalR circuits (250–500 KB+ per active user connection), triggering Out-Of-Memory (OOM) crashes on budget servers. Conversely, imposing `InteractiveWebAssembly` globally forces users on low-bandwidth cellular networks to download 15–30 MB payloads before basic interaction is possible.
2. **Hardcoded Directive Drift Subverting Policy Engines**: While the repository established dynamic governance via `RenderPolicy.cs` and `RuntimeRenderPolicyService.cs`, over 30 individual pages in `Explore.Blazor.Client` hardcoded `@rendermode InteractiveServer` or `@rendermode InteractiveAuto`. This hardcoding silently overrides and disables tenant-configured and instance-configured policies at runtime.
3. **Cross-Boundary SPA State Corruption**: Blazor's root router cannot switch between active `InteractiveServer` and `InteractiveWebAssembly` boundaries within a single continuous client-side navigation without a full HTTP reload or an SSR root shell. Failing to isolate route cohorts leads to runtime DOM reconciliation crashes or hung SignalR circuits.
4. **Economic Blind Spots in SaaS Multi-Tenancy**: Providing unmetered `InteractiveServer` rendering to free-tier tenants in a multi-tenant SaaS deployment creates unbounded server compute liabilities. Server memory is consumed indefinitely by idle circuits, creating economic distress for the SaaS host.
5. **Fragile Network Exclusion for Overseas Diaspora**: Relying strictly on `InteractiveServer` for an international platform results in high round-trip latency (>250ms RTT) for users geographically distant from the server datacenter. Every button click, dropdown toggle, and keystroke requires a server round-trip, creating an unresponsive UI and frequent "Attempting to reconnect to server" modal lockouts on spotty connections.

### Negative Consequences

- **Hardware Exclusion and Economic Injustice (*Gharar* & *Zulm*)**: Small community masajid and cash-strapped charities are forced into expensive, complex cloud architectures or abandon self-hosting entirely because the application cannot run on modest hardware.
- **Digital Divide and Exclusion of Under-Resourced Users (*Darar*)**: Attendees with budget mobile devices or limited data caps in the Global South are locked out by bloated WASM bundles or crippled by unusable SignalR latency over high-loss mobile networks.
- **Provider Burnout and Infrastructure Collapse**: Multi-tenant hosts face spiraling server compute costs or catastrophic system-wide outages when traffic surges exhaust SignalR circuit limits on shared application servers.

### Intended Positive Outcomes

- **True Deployment Autonomy and Empowerment (*Tamkin / Istiqlal*)**: An unprecedented level of architectural sovereignty in open-source software, where self-hosters select the exact operational model matching their technical and financial reality.
- **Stewardship and Resource Justice (*'Adl* & *Amanah*)**: Minimizing wasted energy, compute, and memory by distributing computation appropriately: client compute for low-spec hosts, server compute for low-spec clients.
- **Sustainable and Transparent Multi-Tenancy**: Aligning the true economic cost of infrastructure directly with tenant subscription tiers, eliminating hidden subsidies, arbitrary platform fees, or predatory data monetization.

---

## Findings

The evaluation identified seven core findings regarding render mode handling, architecture governance, and moral infrastructure responsibility.

```text
┌────────────────────────────────────────────────────────────────────────────────────────┐
│                                 I-VSD FINDINGS SUMMARY                                 │
├────────────┬────────────────────────────────────────┬─────────────┬────────────────────┤
│ ID         │ Title                                  │ Severity    │ Lifecycle Status   │
├────────────┼────────────────────────────────────────┼─────────────┼────────────────────┤
│ IVSD-F001  │ Monolithic Render Mode Imposition      │ High        │ Open               │
│ IVSD-F002  │ Page-Level Directive Debt              │ High        │ Open               │
│ IVSD-F003  │ Router Boundary Friction               │ High        │ Open               │
│ IVSD-F004  │ Economic Gharar in Multi-Tenancy       │ Medium      │ Open               │
│ IVSD-F005  │ High-Latency Overseas User Exclusion   │ Medium      │ Open               │
│ IVSD-F006  │ WASM Bundle Bloat & Low-Bandwidth Risk │ Medium      │ Open               │
│ IVSD-F007  │ BFF Security & Circuit Isolation       │ High        │ Open               │
└────────────┴────────────────────────────────────────┴─────────────┴────────────────────┘
```

### IVSD-F001 - Monolithic Render Mode Imposition And Infrastructure Hardship

- **Lifecycle:** Open
- **Severity / Claim:** High; provider-controlled deployment architecture and infrastructure access.
- **Principle / Domain:** Justice (*'Adl*), Removing Hardship (*Raf' al-Haraj*), and Autonomy (*Tamkin*); Technical and Strategic domains.
- **Stakeholders / Provider Decision:** Grassroots self-hosters, local masajid, low-budget operators, and system architects; whether the platform enforces a single render mode or provides full runtime sovereignty.
- **Evidence:** `src/Explore.Domain/Policies/RenderPolicy.cs:8-17` outlines preset options (`AllInteractiveServer`, `SeoBalanced`, `AllInteractiveAutoNoPrerender`), but the system lacks documentation and end-to-end operational validation demonstrating that a self-hoster can cleanly run in 100% `InteractiveWebAssembly` mode with zero server circuit allocation.
- **Analysis:** For the flagship "ISLAMU" instance, selecting `InteractiveServer` delivers immediate, high-touch interactivity with zero client download delay. However, forcing this model upon a community organizer self-hosting on a single $4/month VPS (1 GB RAM, 1 vCPU) is an act of infrastructure injustice. Each active `InteractiveServer` circuit retains its component tree, view state, and event handlers in server memory (typically 250 KB to 500 KB+ per user). Under a surge of 300 simultaneous users registering for Ramadan events, server RAM spikes by 150 MB+, triggering memory pressure, garbage collection stalls, or Linux OOM process termination. Conversely, allowing the self-hoster to select `InteractiveWebAssembly` offloads 100% of the UI execution to attendee browsers, reducing the server to a lightweight, stateless JSON API host that can easily sustain thousands of concurrent attendees on a budget server.
- **Mitigation:** [IVSD-M001](#ivsd-m001---tri-mode-sovereignty-engine-with-zero-circuit-wasm-capability).
- **Owner / Next Validation:** Frontend Architecture Lead; verify memory profile of a headless standalone host under 500 concurrent connections across Server vs. WASM.

### IVSD-F002 - Hardcoded Directive Debt Subverting Policy Governance

- **Lifecycle:** Open
- **Severity / Claim:** High; architectural integrity and policy enforcement failure.
- **Principle / Domain:** Truthfulness (*Sidq*) and Promise-Keeping (*Wafa'*); Technical domain.
- **Stakeholders / Provider Decision:** Instance administrators, tenant administrators, and developers; whether UI components adhere to runtime configuration or hardcode compile-time directives.
- **Evidence:** `MAJOR_DECISIONS.md:545-549` notes that all 32 pages previously hardcoded `@rendermode InteractiveServer`. Direct inspection of `src/Explore.Blazor.Client` reveals lingering hardcoded directives:
  - `CreateEvent.razor:9`: `@rendermode InteractiveServer`
  - `EventDetail.razor:1`: `@rendermode InteractiveServer`
  - `EventList.razor:3`: `@rendermode InteractiveServer`
  - `MyReportsPage.razor:5`: `@rendermode InteractiveAuto`
  - `ModerationReportQueuePage.razor:5`: `@rendermode InteractiveAuto`
  - `EventTemplateSyncPage.razor:2`: `@rendermode InteractiveAuto`
- **Analysis:** Hardcoded `@rendermode` directives on Razor page components represent severe technical debt that actively subverts the Clean Architecture governance pipeline. When an instance administrator or tenant administrator customizes their render policy via `TenantRenderPolicySection.razor` or `InstancePresentationSettingsController.cs`, their choices are silently overridden whenever a user navigates to an explicitly marked page. Furthermore, in Blazor, if a parent router operates in one interactive mode and a child component declares a conflicting interactive mode, unpredictable behavior, duplicate SignalR circuits, or compilation/runtime mismatches occur.
- **Mitigation:** [IVSD-M002](#ivsd-m002---complete-eradication-of-page-level-rendermode-directives).
- **Owner / Next Validation:** Frontend Lead; execute a repository-wide refactoring pass stripping all `@rendermode` directives from `Explore.Blazor.Client/Pages` and enforce via architecture tests.

### IVSD-F003 - Router Boundary Friction Across Route Cohorts

- **Lifecycle:** Open
- **Severity / Claim:** High; runtime stability and user navigation reliability.
- **Principle / Domain:** Excellence (*Ihsan*) and Non-Harm (*La Darar*); Technical and Design domains.
- **Stakeholders / Provider Decision:** End users, attendees, event organizers, and administrative users; how transitions between distinct render mode cohorts are mediated.
- **Evidence:** `src/Explore.Blazor/Components/App.razor:45-65` applies `@rendermode="@_effectiveRenderMode"` directly to `<HeadOutlet>` and `<Routes>`. `RuntimeRenderPolicyService.cs:24-87` classifies routes into `PublicSeo`, `Admin`, `Onboarding`, and `Operational`.
- **Analysis:** In Blazor Web Apps (.NET 8/9/10), placing `@rendermode` on the root `<Routes>` component establishes a single interactive boundary for the entire application lifecycle of that browser tab. If `App.razor` evaluates `_effectiveRenderMode` as `InteractiveServer` on initial load (e.g., loading `/setup`), any subsequent client-side SPA navigation to `/events` remains trapped inside the `InteractiveServer` SignalR circuit, even if `PublicSeoRenderMode` is configured as `InteractiveWebAssembly`! Blazor cannot dynamically transform a live server-side WebSocket circuit into a WebAssembly client runtime without a full browser HTTP reload (`forceLoad: true`). If the application attempts to swap interactive render modes on a route-by-route basis while retaining a single interactive root router, internal state mismatches occur.
- **Mitigation:** [IVSD-M003](#ivsd-m003---clean-cohort-routing-static-ssr-root-or-shell-boundary-gateway).
- **Owner / Next Validation:** Blazor Platform Architect; implement and test clean cohort navigation transitions.

### IVSD-F004 - Economic Gharar And Unsustainable Multi-Tenant Subsidization

- **Lifecycle:** Open
- **Severity / Claim:** Medium; commercial fairness and financial stewardship.
- **Principle / Domain:** Avoiding Gharar (Excessive Uncertainty), Justice (*'Adl*), and Stewardship (*Amanah*); Strategic and Technical domains.
- **Stakeholders / Provider Decision:** Multi-tenant SaaS instance providers, commercial subscribers, and free/grassroots tenants; how server compute costs are structured and governed across tenant tiers.
- **Evidence:** `TenantRenderPolicySection.razor:13-18` allows tenant override of render policies, but `RenderPolicy.cs:19-22` provides instance locks (`LockTenantPublicSeo`, `LockTenantOperational`, `LockTenantAdmin`). Currently, there is no domain connection between a tenant's commercial tier (e.g., Free, Community, Enterprise) and the permitted render mode presets.
- **Analysis:** In a multi-tenant SaaS deployment, compute costs directly correlate with the active render mode. Running thousands of free-tier community tenants on `InteractiveServer` forces the platform provider to absorb continuous cloud infrastructure costs (large server clusters, high RAM allocations, Redis SignalR backplanes) with zero cost recovery. This creates economic *Gharar* (indeterminacy and risk of insolvency), often driving platforms toward exploitative business practices (introducing surveillance tracking, sponsored ads, or sudden lock-in paywalls). By grounding render modes in economic reality, a provider can offer a genuinely free, perpetual Community tier hosted in `InteractiveWebAssembly` (where client devices bear the compute cost, costing the host virtually nothing beyond CDN bandwidth and lightweight API calls), while reserving `InteractiveServer` for paid or enterprise tiers that fund their dedicated server circuit capacity.
- **Mitigation:** [IVSD-M004](#ivsd-m004---tier-bound-render-governance-and-saas-economic-alignment).
- **Owner / Next Validation:** Product & Business Operations Lead; bind `TenantPlan` aggregate in Domain to allowable `RenderPolicyPreset` options.

### IVSD-F005 - High-Latency Overseas User Exclusion

- **Lifecycle:** Open
- **Severity / Claim:** Medium; equitable community access across geographical and network divides.
- **Principle / Domain:** Justice (*'Adl*), Truthfulness (*Sidq*), and Non-Harm (*La Darar*); Design and Technical domains.
- **Stakeholders / Provider Decision:** International diaspora users, attendees traveling abroad, users on unstable mobile networks; how the flagship "ISLAMU" instance accommodates remote attendees while maintaining full Server interactivity.
- **Evidence:** The user specification affirms: *"for 'ISLAMU' islamic instance, we want FULL interactivity Server render mode. cause we value best experience (yeah I know that its less good experience for visitors from other side of the world, bad for low connection, and so on...)"*.
- **Analysis:** While `InteractiveServer` delivers instant Time-To-Interactive (TTI) without downloading large binaries, it is structurally fragile over high-latency (>200ms) or lossy cellular connections. Because every user interaction (opening a MudSelect dropdown, typing in a search box, paginating a schedule) requires a SignalR packet to cross the globe, be processed on the server, and return a DOM diff, international users experience noticeable input lag. If a mobile network temporarily drops packets (e.g., passing through a tunnel or weak coverage), the entire UI freezes and displays an unyielding "Attempting to reconnect..." modal. If the connection cannot recover within the circuit timeout, all uncommitted form state is lost. Prioritizing domestic peak performance must not lead to the complete functional disenfranchisement of overseas community members.
- **Mitigation:** [IVSD-M005](#ivsd-m005---resilient-signalr-circuit-recovery-and-network-transparency).
- **Owner / Next Validation:** UX & Frontend Team; audit SignalR circuit lifetime settings, reconnection UI, and form draft auto-saving.

### IVSD-F006 - WebAssembly Bundle Bloat And Low-Bandwidth Digital Exclusion

- **Lifecycle:** Open
- **Severity / Claim:** Medium; digital inclusion, data usage equity, and mobile accessibility.
- **Principle / Domain:** Justice (*'Adl*) and Removing Hardship (*Raf' al-Haraj*); Technical and Design domains.
- **Stakeholders / Provider Decision:** Attendees with limited cellular data budgets, users in developing nations, mobile attendees; optimizing the initial download footprint of WASM mode.
- **Evidence:** `Explore.Blazor.Client.csproj:140-182` contains detailed architectural notes on Blazor WASM Lazy Loading, noting that currently all pages are bundled into a single monolithic `Explore.Blazor.Client.dll`. It documents planned library separation:
  - `Explore.Blazor.Client.Pages.Admin`
  - `Explore.Blazor.Client.Pages.Onboarding`
  - `Explore.Blazor.Client.Pages.Organization`
- **Analysis:** When an operator or tenant selects `InteractiveWebAssembly`, the browser must download the .NET WebAssembly runtime (`dotnet.native.wasm`), BCL core libraries, and application assemblies before execution begins. In a monolithic client assembly, an attendee who only wants to view a public event schedule is forced to download the code for the entire administrative control plane, event studio, moderation queues, and onboarding wizards! For a user in an emerging economy on a metered, expensive mobile data connection, downloading 20 MB+ of unused administrative code represents unnecessary financial waste (*Israf*) and friction.
- **Mitigation:** [IVSD-M006](#ivsd-m006---complete-assembly-splitting-and-route-level-lazy-loading).
- **Owner / Next Validation:** Build & Performance Lead; execute project split for client pages and activate `BlazorWebAssemblyLazyLoad`.

### IVSD-F007 - BFF Security, State Leakage, And Circuit Boundary Integrity

- **Lifecycle:** Open
- **Severity / Claim:** High; security stewardship, session isolation, and privacy protection.
- **Principle / Domain:** Trust (*Amanah*), Modesty/Privacy (*Hifdh al-Khususiya*), and Non-Harm (*La Darar*); Technical domain.
- **Stakeholders / Provider Decision:** All authenticated users, tenant administrators, and instance operators; maintaining identical security postures across disparate render runtimes.
- **Evidence:** `Explore.Blazor.csproj:35` references `Event.Web.BffHosting`. `MAJOR_DECISIONS.md:550-554` tracks historical debt regarding singleton mutable state (`SetupSecretSessionService`, `CircuitAccessTokenService._tokenStore`).
- **Analysis:** In `InteractiveServer` mode, user code executes directly on the server host; in `InteractiveWebAssembly` mode, user code executes inside the client browser sandbox. If the platform allows seamless switching between these modes across tenants or instances, the security and authentication model must remain invariant. Specifically, access tokens must never be exposed to the browser's JavaScript environment (e.g., LocalStorage) in WASM mode; all communication must traverse the secure Backend-For-Frontend (BFF) proxy via encrypted, SameSite, HttpOnly cookies. Furthermore, services in `InteractiveServer` must strictly adhere to Scoped lifetimes tied to the user circuit to prevent catastrophic cross-tenant state leakage between distinct user sessions.
- **Mitigation:** [IVSD-M007](#ivsd-m007---hermetic-bff-security-boundary-and-scoped-circuit-invariance).
- **Owner / Next Validation:** Security & Identity Lead; verify token storage zero-exposure in WASM and run concurrent circuit cross-talk integration tests.

---

## Recommendations

The recommendations provide a comprehensive architectural roadmap that transforms Blazor's render mode complexity from an engineering burden into an unprecedented competitive advantage and ethical foundation for the platform.

```text
┌────────────────────────────────────────────────────────────────────────────────────────┐
│                                ARCHITECTURAL BLUEPRINT                                 │
│                                                                                        │
│   ┌────────────────────────────────────────────────────────────────────────────────┐   │
│   │                         App.razor (Static SSR Root Host)                       │   │
│   │  • Resolves Tenant & Route Group via RuntimeRenderPolicyService                │   │
│   │  • Enforces Strict CSP Nonce & HttpOnly BFF Cookie Security                    │   │
│   └───────────────────────────────────────┬────────────────────────────────────────┘   │
│                                           │                                            │
│                 ┌─────────────────────────┴─────────────────────────┐                  │
│                 ▼                                                   ▼                  │
│   ┌───────────────────────────┐                       ┌───────────────────────────┐    │
│   │   InteractiveServer Shell │                       │ InteractiveWebAssembly    │    │
│   │   • Flagship ISLAMU       │                       │ • Budget Self-Hosters     │    │
│   │   • Enterprise Tenants    │                       │ • Free Community Tenants  │    │
│   │   • Low-Spec Client Devs  │                       │ • Low-Spec VPS Servers    │    │
│   │   • 0 MB Client Bundle    │                       │ • 0 Server Circuit RAM    │    │
│   └───────────────────────────┘                       └───────────────────────────┘    │
│                 │                                                   │                  │
│                 └─────────────────────────┬─────────────────────────┘                  │
│                                           │                                            │
│                                           ▼                                            │
│   ┌────────────────────────────────────────────────────────────────────────────────┐   │
│   │             Shared UI Layer (Explore.Blazor.Client - 100% Agnostic)            │   │
│   │  • Zero hardcoded @rendermode directives in page components                    │   │
│   │  • Interacts purely via IEventApiClient (BFF-proxied HTTP)                     │   │
│   │  • Lazy-loaded assemblies (Admin, Studio, Onboarding) for lean public WASM     │   │
│   └────────────────────────────────────────────────────────────────────────────────┘   │
└────────────────────────────────────────────────────────────────────────────────────────┘
```

### Architecture & Design Recommendations

#### IVSD-M001 - Tri-Mode Sovereignty Engine With Zero-Circuit WASM Capability
- **Mapped Finding:** [IVSD-F001](#ivsd-f001---monolithic-render-mode-imposition-and-infrastructure-hardship)
- **Design:** Formalize the platform's render mode engine into three first-class, officially supported deployment profiles:
  1. **Full InteractiveServer Profile (The "ISLAMU" Experience)**:
     - Target: Flagship instance, enterprise deployments, intranet environments, and users on budget/low-spec mobile devices.
     - Architecture: Root `<Routes @rendermode="InteractiveServer">`. Instant startup, 0 MB client WebAssembly payload. All logic runs on the server.
  2. **Full InteractiveWebAssembly Profile (The "Sovereign Self-Hoster" Experience)**:
     - Target: Community organizers, local masajid, and self-hosters running on $4/month VPS nodes (1 vCPU, 1 GB RAM) or home servers.
     - Architecture: Root `<Routes @rendermode="InteractiveWebAssembly">`. Server maintains 0 SignalR circuits. The server acts strictly as a stateless REST API and static asset host. 100% of UI execution occurs inside client browser sandboxes.
  3. **InteractiveAuto Profile (The "Balanced Progressive" Experience)**:
     - Target: Modern cloud deployments.
     - Architecture: Root `<Routes @rendermode="InteractiveAuto">`. Starts immediately with server-side rendering while the WebAssembly bundle caches in the background. Subsequent navigations seamlessly run client-side.
- **Operational Implementation:** Provide clean, declarative environment variables in `.env.example` (`EVENT_DEFAULT_RENDER_PRESET=AllInteractiveServer|AllInteractiveWebAssembly|AllInteractiveAuto|SeoBalanced`) so self-hosters can configure their deployment profile in one line.

#### IVSD-M002 - Complete Eradication Of Page-Level Directive Debt
- **Mapped Finding:** [IVSD-F002](#ivsd-f002---hardcoded-directive-debt-subverting-policy-governance)
- **Design:** Completely remove `@rendermode` declarations from all 30+ Razor page files in `Explore.Blazor.Client`. Pages must become completely render-mode agnostic pure components.
- **Clean Architecture Rule:** A page or component in `Explore.Blazor.Client` must never dictate how it is rendered. The rendering execution context is a presentation infrastructure concern governed by `RenderPolicy`, resolved by `App.razor`, and injected into the component hierarchy.
- **Guardrail:** Author an automated Roslyn Architecture Test in `Event.ArchitectureTests` that scans all `.razor` files in `Explore.Blazor.Client/Pages` and fails the build if any `@rendermode` attribute is detected.

#### IVSD-M003 - Clean Cohort Routing: Static SSR Root Or Shell Boundary Gateway
- **Mapped Finding:** [IVSD-F003](#ivsd-f003---router-boundary-friction-across-route-cohorts)
- **Design:** To enable true route-level hybridization (e.g., Public SEO in SSR/Auto, Admin Studio in Server) without breaking Blazor's single-router boundary:
  - **Option A (Shell Separation - Recommended for Clean Architecture)**: Leverage the existing architectural split in `App.razor:58-65`. Keep public attendee routes on the primary `Routes.razor` shell, and run administrative/studio workflows on `EmbeddedControlPlaneRoutes.razor`. When navigating between public and administrative shells, perform an intentional full HTTP transition (`NavigationManager.NavigateTo(url, forceLoad: true)`), allowing the host document to cleanly boot the appropriate render mode for that route cohort.
  - **Option B (Static SSR Root with Interactive Islands)**: Leave `<Routes>` without any `@rendermode` (pure Static SSR with enhanced navigation). Wrap interactive UI components within dynamic render mode boundaries (`<RenderModeBoundary Cohort="RouteCohort.PublicSeo">`).
  - **Selected Architectural Path:** Option A is selected. It aligns with Clean Architecture, preserves MudBlazor dialog/theme providers within their respective shells, and eliminates complex nested boundary state reconciliation.

#### IVSD-M004 - Tier-Bound Render Governance And SaaS Economic Alignment
- **Mapped Finding:** [IVSD-F004](#ivsd-f004---economic-gharar-in-multi-tenant-subsidization)
- **Design:** Extend the `TenantPlan` and `TenantPolicy` aggregates in `Explore.Domain` to formally couple tenant subscription tiers with allowed render mode presets:
  - **Community / Free Tier**: Locked to `InteractiveWebAssembly` (client compute). The tenant can host unlimited attendees without incurring SignalR circuit memory costs on the host's server cluster.
  - **Pro / Standard Tier**: Configurable between `InteractiveWebAssembly` and `InteractiveAuto`.
  - **Enterprise / Masajid Tier**: Grants access to `AllInteractiveServer` with dedicated server memory allocation, real-time priority websockets, and white-glove latency optimization.
- **Governance Locks:** Instance administrators retain ultimate authority via `LockTenantPublicSeo`, `LockTenantOperational`, and `LockTenantAdmin` in `RenderPolicy.cs`, preventing rogue tenants from overloading the host.

#### IVSD-M005 - Resilient SignalR Circuit Recovery And Network Transparency
- **Mapped Finding:** [IVSD-F005](#ivsd-f005---high-latency-overseas-user-exclusion)
- **Design:** For instances operating in `InteractiveServer` mode (including the flagship "ISLAMU" instance), implement state resilience and network transparency for international diaspora users:
  1. **Customized Reconnection UX**: Replace Blazor's default blocking gray modal with a polite, non-modal banner that attempts automatic exponential reconnection without freezing the visible DOM.
  2. **Client-Side Draft Persistence**: Important form inputs (e.g., event registration, organizer claim forms) must persist partial state to browser `sessionStorage` via lightweight JS interop, ensuring that if a circuit disconnects permanently, user inputs are restored upon reload.
  3. **Transparent Latency Indicator**: Provide an unobtrusive status chip in the footer indicating connection quality (e.g., "Connected • Low Latency" vs. "High Latency • Reconnecting"), respecting the Islamic principle of Truthfulness (*Sidq*).

#### IVSD-M006 - Complete Assembly Splitting And Route-Level Lazy Loading
- **Mapped Finding:** [IVSD-F006](#ivsd-f006---webassembly-bundle-bloat-and-low-bandwidth-risk)
- **Design:** Execute the planned refactoring in `Explore.Blazor.Client.csproj:140`:
  - Split `Explore.Blazor.Client` into modular class libraries:
    - `Explore.Blazor.Client.Core` (Shared components, discovery, event listings, layout)
    - `Explore.Blazor.Client.Admin` (Control plane, tenant management, system settings)
    - `Explore.Blazor.Client.Studio` (Organizer event editor, scheduling, ticket designer)
    - `Explore.Blazor.Client.Onboarding` (Instance and tenant setup wizards)
  - Configure `BlazorWebAssemblyLazyLoad` in project files.
  - In `InteractiveWebAssembly` mode, an attendee viewing events downloads only `Explore.Blazor.Client.Core` (~4 MB compressed), deferring administrative assemblies until an admin actually logs in and navigates to `/admin`.

#### IVSD-M007 - Hermetic BFF Security Boundary And Scoped Circuit Invariance
- **Mapped Finding:** [IVSD-F007](#ivsd-f007---bff-security-and-circuit-isolation)
- **Design:** Maintain an invariant security contract across all render modes:
  - In both Server and WASM, the browser client must NEVER touch raw JWT access tokens or refresh tokens. All authentication is maintained via encrypted, SameSite=Lax (or Strict), HttpOnly cookies issued by the BFF host (`Event.Web.BffHosting`).
  - In WASM mode, API calls from `IEventApiClient` target relative BFF endpoints (`/api/...`), where YARP forwards the authenticated session with downstream access token injection.
  - In Server mode, components resolve `IEventApiClient` identically, routing through the BFF handler to maintain uniform logging, correlation IDs, and tenant isolation.
  - Service lifetimes in `Explore.Blazor.Client` must remain strictly Scoped or Transient—zero mutable singleton services holding per-user state.

---

### Implementation Roadmap (Greenfield Clean Architecture Refactoring)

Because this repository is in active pre-release greenfield development with 0 external adopters, backward compatibility is rejected. The following sequence establishes the cleanest possible implementation path:

```text
┌────────────────────────────────────────────────────────────────────────────────────────┐
│                               PHASED IMPLEMENTATION PLAN                               │
├───────────────────┬──────────────────────────────────────────┬─────────────────────────┤
│ Phase             │ Scope & Deliverables                     │ Verification Gate       │
├───────────────────┼──────────────────────────────────────────┼─────────────────────────┤
│ Phase 1: Cleansing│ Strip all hardcoded @rendermode directives│ Roslyn Architecture     │
│                   │ from all 30+ pages in Explore.Blazor.    │ Test in CI verifying    │
│                   │ Client.                                  │ zero page directives.   │
├───────────────────┼──────────────────────────────────────────┼─────────────────────────┤
│ Phase 2: Router   │ Refactor App.razor & shell routing to    │ Verify seamless full-   │
│ Shell Boundary    │ support clean cohort transitions between │ HTTP navigation across  │
│                   │ Public (WASM/Auto) and Admin (Server).   │ shells with no errors.  │
├───────────────────┼──────────────────────────────────────────┼─────────────────────────┤
│ Phase 3: Assembly │ Split Explore.Blazor.Client into Core,   │ WASM initial download   │
│ Modularization    │ Admin, and Studio lazy-loaded libraries. │ reduced by >40% in      │
│                   │                                          │ Chrome DevTools audit.  │
├───────────────────┼──────────────────────────────────────────┼─────────────────────────┤
│ Phase 4: Domain   │ Bind TenantPlan tiers to RenderPolicy    │ Domain unit tests &     │
│ Tier Binding      │ presets. Enforce economic locks in API.  │ tenant admin UI tests.  │
├───────────────────┼──────────────────────────────────────────┼─────────────────────────┤
│ Phase 5: Resilient│ Implement non-blocking SignalR reconnect │ Simulated network drop  │
│ Experience        │ banner and sessionStorage draft saver.   │ retains form inputs.    │
└───────────────────┴──────────────────────────────────────────┴─────────────────────────┘
```

### Alternatives Considered And Rejected

1. **Retaining Hardcoded `@rendermode InteractiveServer` on Admin Pages**:
   - *Rationale for Rejection*: Violates Clean Architecture and breaks runtime customization. Even if admin pages are predominantly suited for Server mode, the decision belongs to the policy engine, not hardcoded compile-time attributes.
2. **Dynamic In-Page Runtime Injection via Razor DynamicComponent**:
   - *Rationale for Rejection*: Attempting to wrap every page in a dynamic component that queries an API for `@rendermode` introduces significant rendering latency, breaks Blazor static analysis, and complicates dependency injection.
3. **Pure Client-Side Blazor (WASM-Only Platform)**:
   - *Rationale for Rejection*: Degrades the flagship "ISLAMU" instance experience by forcing 15–30 MB initial downloads and eliminates the rapid, high-touch reactivity of Server mode on low-spec client devices.
4. **Pure Server-Side Blazor (Server-Only Platform)**:
   - *Rationale for Rejection*: Violates hardware justice (*'Adl*) by making self-hosting prohibitively expensive on budget VPS hardware and creates high latency for the global diaspora.

---

## Stakeholders

| Stakeholder Group | Representation & Role | Moral Concern & Impact |
|---|---|---|
| **Flagship ISLAMU Attendees** | Community members, youth, families accessing the central Islamic instance | Desire instant, fluid, responsive interaction without waiting for large WASM bundles; need reliable registration even on older smartphones. |
| **Global Diaspora & Overseas Attendees** | Community members accessing the platform across international boundaries | Suffer severe latency and frequent disconnections if forced into Server mode; benefit immensely from cached WASM execution. |
| **Grassroots Self-Hosters & Masajid** | Volunteer admins, local masajid, halaqat running on $4/month VPS or refurbished PC | Excluded from self-hosting by memory-heavy Server circuits; empowered by client-side WebAssembly hosting. |
| **Enterprise / Institutional Tenants** | Large Islamic organizations, universities, national relief charities | Require high-throughput, instant interactivity, dedicated compute resources, and strict administrative control. |
| **Multi-Tenant SaaS Providers** | Platform operators offering hosted instances to the broader community | Risk financial ruin (*Gharar*) if free tiers consume unbounded server memory; need economic tier alignment. |
| **Core Maintainers & Developers** | Engineers maintaining the codebase | Suffer technical debt and bugs when pages hardcode directives; benefit from Clean Architecture render decoupling. |

---

## I-VSD Principles And Domains

| Principle | Primary Domain | Manifestation in Render Mode Governance |
|---|---|---|
| **Justice (*'Adl*)** | Technical & Strategic | Distributing compute burdens equitably: offloading to client browsers for cash-strapped hosts, absorbing onto servers for low-power client devices. |
| **Removing Hardship (*Raf' al-Haraj*)** | Technical & Operational | Ensuring that neither self-hosters nor end users are excluded by insurmountable hardware or bandwidth barriers. |
| **Stewardship (*Amanah*)** | Operational & Technical | Conserving compute energy, server memory, and client bandwidth (*Hifdh al-Mal* / avoidance of *Israf*). |
| **Avoiding Uncertainty (*Gharar*)** | Strategic & Governance | Transparently linking SaaS subscription tiers to actual server compute consumption, eliminating hidden operational liabilities. |
| **Truthfulness (*Sidq*)** | Design & Marketing | Transparently communicating render mode trade-offs (initial download size vs. runtime latency vs. offline resilience). |
| **Autonomy (*Tamkin / Istiqlal*)** | Governance & Strategic | Guaranteeing that self-hosters retain 100% sovereignty over how their application interacts with client browsers. |
| **Excellence (*Ihsan*)** | Technical | Engineering an enterprise-grade, clean, decoupled architecture that elevates Blazor's multi-mode design into a world-class feature. |

---

## Validation Gaps

1. **Empirical Benchmarking on Low-Spec Hardware**: While theoretical memory models predict 250–500 KB per active SignalR circuit, formal load tests using k6 or NBomber against a 1 vCPU / 1 GB RAM SQLite container in `InteractiveServer` vs. `InteractiveWebAssembly` have not yet been executed.
2. **Real-World Bundle Compression Metrics**: The precise compressed Brotli/Gzip payload size of `Explore.Blazor.Client` after assembly modularization and tree trimming requires verification in Release mode.
3. **Cross-Continental Latency Audits**: Quantitative latency measurements (Time to First Meaningful Interaction and input response lag) across different geographical regions (Europe vs. North America vs. Middle East vs. Southeast Asia) remain pending.

---

## Escalation Needed

- **Commercial Tier Pricing Policy**: Formal pricing structures and plan boundaries for multi-tenant SaaS hosting should be reviewed by instance governance leadership to confirm that tier definitions remain just, non-exploitative, and transparent.
- **Contractual & Financing Compliance**: Any commercial hosting agreements involving infrastructure financing, late fees, or service-level warranties must be escalated to qualified Sunni scholars to ensure strict avoidance of *Riba* and prohibited contractual ambiguity.

---

## Evidence Reviewed

- `src/Explore.Domain/Policies/RenderPolicy.cs`: Core policy slots for presets, route groups, and tenant override permissions.
- `src/Explore.Domain/Enums/RenderPolicyPresetEnum.cs`: Presets (`SeoBalanced`, `AllPrerendered`, `AllInteractiveAutoNoPrerender`, `CustomAdvanced`, `AllInteractiveServer`).
- `src/Explore.Application/DTOs/Instance/RenderPolicySettingsDto.cs`: Presentation DTOs and versioning.
- `src/Explore.Application/DTOs/Instance/Validators/RenderPolicySettingsDtoValidator.cs`: FluentValidation rules for presets and override flags.
- `src/Explore.Blazor/Components/App.razor`: Host document evaluating `_effectiveRenderMode`, CSP nonces, and shell routing.
- `src/Explore.Blazor.Client/Services/RuntimeRenderPolicyService.cs`: Route group classification (`PublicSeo`, `Admin`, `Onboarding`, `Operational`) and decision resolution.
- `src/Explore.Blazor.Client/Pages/Admin/Tenant/Components/TenantRenderPolicySection.razor`: Tenant UI for configuring render presets and locks.
- `src/Explore.Blazor.Client/Explore.Blazor.Client.csproj`: MSBuild configuration and lazy-loading roadmap notes.
- `src/Explore.Blazor/Explore.Blazor.csproj`: WebAssembly Server package references and BFF hosting integration.
- `dev/_journal/MAJOR_DECISIONS.md`: Records historical decision 545 regarding `@rendermode` hardcoding debt and cohort migration.

---

## Missing Evidence

- Production telemetry regarding average SignalR circuit reconnection frequency across mobile networks.
- User feedback from grassroots community organizers attempting self-hosting on budget hardware.
- Formal latency profiling data for overseas diaspora users connecting to European/North American datacenters.

---

## Context Inventory

- **Workspace & Repository**: Full access to C# source code, Razor components, project files, journal decision logs, and domain policy aggregates.
- **Project Context Integrations**: None active or required for this architectural review.
- **CLIs & Retrieval Helpers**: Native file search, ripgrep pattern matching, and source file inspections.
- **User-Provided Context**: Explicit instructions emphasizing full `InteractiveServer` for the flagship ISLAMU instance, full `InteractiveWebAssembly` for budget self-hosters, `InteractiveAuto` for balanced deployments, hybrid route cohorts, SaaS tenant tier economic alignment, and absolute freedom from backward compatibility baggage during pre-release greenfield development.

---

## Review Lifecycle

| Date | Previous Status | New Status | Trigger | Evidence / Replacement |
|---|---|---|---|---|
| 2026-09-13 | None | Current | User requested I-VSD consultation on Blazor WebApp render modes, self-hosting autonomy, and multi-tenant economics | Canonical report written to `islamic-value-sensitive-design/consultations/i-vsd-blazor-render-modes-consultation.md` based on working-tree inspection. |
