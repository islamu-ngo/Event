<!-- ABOUTME: I-VSD evaluation of tenancy public experience postures: Directory versus DedicatedPortal. -->
<!-- ABOUTME: Grounded in provider responsibility, truthfulness in presentation, Amanah in event curation, and organizational autonomy. -->

# I-VSD: Tenancy Public Experience Posture — Directory vs. DedicatedPortal

Last Updated: 2026-09-02 Europe/Brussels

## Review Metadata
- Mode: standalone
- Subject: `tenancy-public-experience-directory-vs-dedicatedportal`
- Workstream: none
- Report kind: consultancy-report
- Report status: current
- Disposition: ready-for-planning
- Evidence cutoff: 2026-09-02
- Reviewed input revision: `sha256:6e37b98d28a509930f3a479a9512391ce4f5e0fa0c18d9f9661cb85121b65db9`
- Supersedes: none

## Scope
This report evaluates the architectural design, vocabulary, and UX posture of tenancy public experience settings in ISLAMU Event. Specifically, it reviews the conceptual transition from the legacy `DiscoveryCentric` / `OrganizationCentric` models to **`Directory`** versus **`DedicatedPortal`**, and assesses the provider responsibilities governing:
1. Public site presentation, visitor expectations, and navigation truthfulness.
2. Tenant authority boundaries versus ATProtocol/federated actor identity.
3. Event creation policy enforcement (closing public submissions on dedicated sites).
4. Fair inclusion of organization sub-groups, committees, and departments.
5. Search and catalog scoping invariants.
6. Archival dignity and community transparency (upcoming events vs. past event records).

## Claim Boundary
This document provides software design reasoning, architectural analysis, and provider-responsibility evaluation grounded in Islamic Value-Sensitive Design (I-VSD). It does **not** issue Sharia rulings, fatwas, or declarations of halal/haram/makrooh/wajib. Any religious-legal questions regarding specific contractual terms, ticket sales, or religious arbitration are explicitly marked for escalation to qualified scholarly authority under [Escalation Needed](#escalation-needed).

## Findings

| Finding ID | Title | Severity | Status | Principle / Domain | Decision / Rule | Linked Mitigation |
|---|---|---|---|---|---|---|
| **IVSD-F001** | False Openness: Public submission affordances leak on dedicated organization sites | High | open | Amanah / Trust, Dar' al-Mafasid | `NavMenu.razor.cs` enables anonymous "Add Event" button when organization publishing is enabled | **IVSD-M001** |
| **IVSD-F002** | Misleading Catalog Escape: "View Events" routes visitors from dedicated site to global directory | Medium | open | Sidq / Truthfulness, UX Integrity | Home and Nav buttons hardcode route to unconstrained `/events` catalog | **IVSD-M002** |
| **IVSD-F003** | Sub-Group Exclusion: Single ActorId filtering hides internal committee/group events | High | open | Adl / Justice, Organizational Dignity | `ApplyOrganizationCentricDefaults` scopes strictly to `PrimaryOrganizationId` actor | **IVSD-M003** |
| **IVSD-F004** | Semantic Inversion: Forcing tenant admins to select an "Organization Actor" inside their own dedicated tenant | Medium | open | Clarity, Respect for User Resources | Admin UI treats the tenant as a secondary host of an actor rather than the organization's dedicated portal | **IVSD-M004** |
| **IVSD-F005** | Historical Erasure: Lack of past events archive undermines organizational institutional memory | Low | open | Amanah, Transparency | `Home.razor` caps events at 3 upcoming items without an archive or timeline | **IVSD-M005** |
| **IVSD-F006** | Navigation Disorientation: Absence of persistent return link for redirected visitors | Low | open | Courtesy, Visitor Agency | No header link to return to the parent organizational website | **IVSD-M006** |

---

### Detailed Findings

#### IVSD-F001 — False Openness: Public submission affordances leak on dedicated organization sites
- **Context**: In `NavMenu.razor.cs`, `_showAddEventForAnonymous` evaluates to `true` whenever `AllowOrganizationSubmittedEvents` is enabled. In a dedicated site, organization staff must be allowed to create events in Studio, which inadvertently causes the public navbar to present an "Add Event" button to anonymous visitors.
- **Moral Risk**: When visitors from a mosque or community center land on `events.alnoor.org`, seeing "Add Event" conveys that the site is an open community bulletin board where anyone can post. This violates *Amanah* (custodianship) by misrepresenting the curation and endorsement standards of the organization.
- **Provider Controlled Decision**: The logic governing public button visibility in `NavMenu.razor.cs` and `MainLayout.razor.cs`.

#### IVSD-F002 — Misleading Catalog Escape: "View Events" routes visitors to an unconstrained catalog
- **Context**: In `GetPublicExperienceShellQueryHandler.cs`, `EventCatalog.Url` defaults to `/events`. In `EventList.razor.cs`, the page does not automatically scope to the tenant or primary organization unless explicitly passed in the query string.
- **Moral Risk**: A user clicking "View Events" or searching in the header is transported into a search-engine-style multi-filter interface that can expose events outside the organization or display confusing marketplace filters (e.g. multi-madhab filtering, unrelated tags). This violates *Sidq* (truthful representation) of what the visitor asked to see.
- **Provider Controlled Decision**: Default catalog routing and automatic scoping in `EventList.razor.cs`.

#### IVSD-F003 — Sub-Group Exclusion: Actor-level scoping erases internal committees
- **Context**: `ApplyOrganizationCentricDefaults` filters events strictly by `ActorId == PrimaryOrganization.ActorId`.
- **Moral Risk**: In Muslim organizations and non-profits, events are frequently organized and published by sub-entities (e.g., *Al-Noor Youth Committee*, *Sisters Halaqa Circle*, *Relief & Food Drive Taskforce*). Filtering exclusively by the root organization's Actor ID silences or hides programs organized by youth and women's groups within the institution, conflicting with *Adl* (fairness and balanced representation).
- **Provider Controlled Decision**: The boundary definition for dedicated site event retrieval (tenant-wide approved events vs single-actor queries).

#### IVSD-F004 — Semantic Inversion: Confusing Tenant Admin identity with Actor records
- **Context**: The existing naming `OrganizationCentric` led to code requiring a `PrimaryOrganizationId` foreign key, forcing tenant admins to create an Organization actor and select themselves from a dropdown.
- **Moral Risk**: Unnecessary technical complexity burdens grassroots community leaders, volunteer administrators, and educators, diverting resources from their primary mission.
- **Provider Controlled Decision**: Domain and Application naming conventions; eliminating artificial configuration friction.

#### IVSD-F005 — Historical Erasure: Omitting past event records
- **Context**: `Home.razor` loads only 3 upcoming events and has no section for completed programs, recordings, or past dates.
- **Moral Risk**: Community institutions rely on their track record of past classes, seminars, and lectures for accountability, educational continuity, and communal memory. Omitting past events makes an active, long-standing institution appear empty or inactive when no immediate event is scheduled.
- **Provider Controlled Decision**: Data queries and presentation components on the dedicated portal home page.

#### IVSD-F006 — Navigation Disorientation: Trapping visitors away from the parent website
- **Context**: When an organization links from `alnoor.org` to `events.alnoor.org`, there is no standardized header link to return to the primary organizational website.
- **Moral Risk**: Degrades user agency and causes confusion regarding site ownership and continuity.
- **Provider Controlled Decision**: Top navigation links and shell branding header composition.

---

## Recommendations

### IVSD-M001 — Enforce Fail-Closed Public Event Creation on Dedicated Portals
- **Action**: In `DedicatedPortal` mode, set `ShowAnonymousEventAction = false` unconditionally.
- **Rule**: Anonymous event submission is strictly forbidden on a `DedicatedPortal`. Event creation is reserved for authenticated staff/organizers operating within the Studio workspace.

### IVSD-M002 — Scope Catalog, Search, and EventList to the Tenant Boundary
- **Action**: When `Mode == DedicatedPortal`, `EventList.razor` must automatically enforce tenant-bounded scoping. Search inputs in `NavMenu.razor` must query within the organization's events.
- **Rule**: Visitors must never be redirected to an unconstrained multi-publisher directory when browsing within a dedicated portal.

### IVSD-M003 — Inclusive Tenant-Scoped Aggregation for Internal Groups
- **Action**: Instead of filtering solely by `PrimaryOrganization.ActorId`, `DedicatedPortal` queries all approved events published under that tenant (`e.TenantId == currentTenantId`).
- **Benefit**: Events organized by the organization's youth committees, educational wings, and volunteer groups are seamlessly showcased on the main site.

### IVSD-M004 — Adopt Clean Vocabulary: `Directory` vs. `DedicatedPortal`
- **Action**: Rename `PublicExperienceMode`:
  - `DiscoveryCentric` → `Directory`
  - `OrganizationCentric` → `DedicatedPortal`
- **Benefit**: Removes ambiguity. A tenant is either a shared multi-publisher **Directory** or an organization's **DedicatedPortal**.
- **Rule**: In `DedicatedPortal` mode, derive branding directly from Tenant settings (`BrandDisplayName`, `BrandLogoUrl`, `WebsiteUrl`), eliminating the mandatory `PrimaryOrganizationId` dropdown.

### IVSD-M005 — Integrate Unified Upcoming + Past Archive UI (Adopt `ProfileEventSections`)
- **Action**: Replace the static 3-card event slice in `Home.razor` with a structured presentation incorporating the `ProfileEventSections` pattern:
  1. **Upcoming Events Section**: Full schedule of forthcoming programs, prayer schedules, or workshops with date badges and registration links.
  2. **Past Events Archive Section**: Chronological timeline of past programs, enabling attendees to review past series or access linked materials.
  3. **Calendar Integration**: Exportable iCal feed and "Add to Google/Apple Calendar" affordances.

### IVSD-M006 — Provide a Prominent "Return to Main Website" Header Link
- **Action**: When `DedicatedPortal` is active and `WebsiteUrl` is provided in settings, render an accessible link in the top bar: `[ ↖ Back to {BrandDisplayName} ]`.

### Rejected Alternatives
- *Keeping `OrganizationCentric` and adding more explanatory text*: Rejected. Explanatory text cannot compensate for an inverted mental model that confuses tenants with actor entities.
- *Preserving backward compatibility shims for `PublicExperienceMode`*: Rejected. Per Rule 11 of `AGENTS.md`, this pre-release greenfield codebase eliminates obsolete ratchets and favors clean architecture.

---

## Stakeholders

| Stakeholder | Role & Authority | Material Moral Interest |
|---|---|---|
| **Community Member / Visitor** | Seeks trustworthy, accurate information about an organization's events | Protection from misleading open-submission content; easy access to current programs, past archives, and calendar sync. |
| **Organization / Mosque Leadership** | Fulfills *Amanah* of hosting and guiding the community | Full control over what appears under their name; inclusion of youth and women's committees; clear representation. |
| **Volunteers & Committee Organizers** | Organize specific events under the organization's umbrella | Fair visibility for committee events without being erased by a single-actor filter. |
| **Directory Operator** | Runs a community-wide regional or thematic event hub | Needs the `Directory` mode to support discovery, submissions, and broad aggregation without forcing dedicated branding. |
| **Platform Maintainers** | Maintain codebase cleanliness, security, and integrity | Clean Architecture without obsolete shims, clear invariants, and fail-closed safety. |

---

## I-VSD Principles And Domains

| Principle | Primary Domain | Manifestation in this Posture Refactor |
|---|---|---|
| **Amanah (Trust & Custodianship)** | Governance & UX | Dedicated portals must never deceive visitors by displaying public event submission forms that falsely imply community-wide uncurated posting. |
| **Sidq (Truthfulness)** | UX & Information Architecture | Clear distinction between a shared `Directory` and an institution's `DedicatedPortal`. Search and catalog links stay true to the visitor's context. |
| **Adl (Justice & Equity)** | Data & Architecture | Events from internal youth clubs, sisters halaqas, and charity drives within the organization are not suppressed in favor of a single administrative actor. |
| **Dar' al-Mafasid (Repelling Harm)** | Security & Policy | Fail-closed defaults: disabling public submissions by default on dedicated portals prevents unauthorized or harmful event postings. |
| **Ihtiram al-Waqt (Respect for Human Resources)** | Architecture & Admin | Eliminating redundant configuration steps (actor selection dropdowns) honors the limited time and capacity of community volunteers. |

---

## Common Overlooked Failures And Outcomes

1. **The Ghost Town Effect**: An organization has no public events scheduled for the next two weeks. On the current 3-card upcoming-only layout, the home page renders an empty alert, making the mosque look inactive. *Mitigation*: The past events archive timeline demonstrates an active, established history of community service.
2. **The Hijacked Portal**: An admin leaves event submission open because they want committee members to post. An anonymous bad actor signs in and publishes an unvetted event on the mosque's domain. *Mitigation*: Strictly separate internal group management (authenticated via Studio) from public submission.
3. **The Trap Navigation**: A visitor arrives at `events.masjid.org` from a flyer or Instagram link, needs to find the mosque's prayer times or address on `masjid.org`, but cannot find a link back. *Mitigation*: Persistent `[ Back to masjid.org ]` in the top navbar.

---

## Validation Gaps
- User research with volunteer mosque administrators on preferred terminology for committee-published events.
- Verification of mobile layout performance when rendering the combined upcoming and past timeline sections.
- Verification of ATProtocol identity federation when an organization operates primarily as a `DedicatedPortal`.

---

## Escalation Needed
- **Commercial & Payment Terms**: Any future paid ticketing fee structures or refund dispute mechanisms must undergo independent scholarly fiqh review to prevent *Gharar* (ambiguity) and ensure valid contractual offer/acceptance (*Ijab wa Qabul*).
- **Content Governance**: Specific criteria for community guidelines and event moderation policies should be reviewed by local scholarly leadership.

---

## Evidence Reviewed
- [`PublicExperienceMode.cs`](file:///home/amir/ISLAMU/Github/Event/src/Explore.Application/Models/PublicExperienceMode.cs)
- [`GetPublicExperienceShellQueryHandler.cs`](file:///home/amir/ISLAMU/Github/Event/src/Explore.Application/Features/PublicExperience/Handlers/Queries/GetPublicExperienceShellQueryHandler.cs)
- [`OrganizationCentricGuardrailTests.cs`](file:///home/amir/ISLAMU/Github/Event/tests/Event.Architecture.Tests/OrganizationCentricGuardrailTests.cs)
- [`Home.razor`](file:///home/amir/ISLAMU/Github/Event/src/Explore.Blazor.Client/Pages/Home.razor)
- [`OrganizationProfile.razor`](file:///home/amir/ISLAMU/Github/Event/src/Explore.Blazor.Client/Pages/Organizations/OrganizationProfile.razor)
- [`ProfileEventSections.razor`](file:///home/amir/ISLAMU/Github/Event/src/Explore.Blazor.Client/Components/Events/ProfileEventSections.razor)
- [`NavMenu.razor`](file:///home/amir/ISLAMU/Github/Event/src/Explore.Blazor.Client/Layout/NavMenu.razor)
- [`TenantPublicExperienceSection.razor`](file:///home/amir/ISLAMU/Github/Event/src/Explore.Blazor.Client/Pages/Admin/Tenant/Components/TenantPublicExperienceSection.razor)
- [`ADR-019-workspace-shell-composition.md`](file:///home/amir/ISLAMU/Github/Event/docs/adr/ADR-019-workspace-shell-composition.md)

## Missing Evidence
- Telemetry on bounce rates of visitors encountering empty upcoming event lists in early testing.
- Direct feedback from non-technical mosque administrators regarding custom domain DNS configuration.

## Context Inventory
- Primary Tenant Settings: `GovernanceSettingKeys.PublicExperience.*`
- Deployment Models: SingleTenant vs MultiTenant
- Target Client: Blazor WebAssembly / BFF
- Relevant ADRs: ADR-019 (Workspace Shell Composition)

## Review Lifecycle
| Date | Previous status | New status | Trigger | Evidence/replacement |
|---|---|---|---|---|
| 2026-09-02 | none | current | User request for I-VSD review of experience mode rename (`Directory` vs `DedicatedPortal`) | Direct codebase inspection and architectural analysis |
