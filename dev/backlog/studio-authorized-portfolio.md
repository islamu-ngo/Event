# Studio Authorized Portfolio — Deferred Delivery Contract

Last Updated: 2026-10-12 Europe/Brussels

> **Status:** Backlog; preserved user scope, not implementation approval.
> **Origin:** Studio event workspace r1, narrowed by CTO review r2.
> **Owner:** Studio/Application authorization implementer.
> **Dependencies:** Creation/workspace may ship separately; this work needs its own current plan, tasks, I-VSD mapping and verification.

## User outcome and PR boundaries

Preserve the user's complete organizer portfolio requirement: All managed events, actor/event filters, events, attendees **and orders**. Deliver three independently reviewed PRs: (1) complete event scope and table, (2) paged attendees, (3) paged orders. Each settles its API, generated contracts, UI, public/internal docs and release evidence together. A ten-second timeout is a failure boundary, not a performance acceptance target.

The r1 plan promised global orders but supplied no replacement query/task for them. Source review of `src/Explore.Blazor.Client/Pages/Studio/StudioOrders.razor` confirmed first-100 event loading and per-event `StudioOrderList` fan-out, with no actor-change reload in the inspected page. Orders need the same completeness and stale-scope treatment as attendees; do not redesign refunds, payment transitions or refund campaigns.

## Additional correctness requirements before activation

- Evaluate management/participant/order permissions separately using native providers. HAL describes browser affordances; handlers enforce authority. Actor membership selects candidates and grants nothing.
- Preserve complete tenant-qualified union, deduplication before count/page, private no-store replies, filter validation and cancellation. Fail the entire scope request on permission-provider unavailability; do not turn denial into empty success.
- Batch permission evaluation without N+1 database detail loads. Preserve total count and page ordering when SQL ID lists are split for provider parameter limits: concatenating individually paged chunks is wrong. Count distinct authorized rows and perform one globally ordered page, or a proven merge with the same complete order.
- Pin effective filters and principal/tenant to a single request. Recheck permission at the existing response boundary; no claim of revoking bytes already delivered. Record count/page consistency limits under concurrent updates.
- Before Green, execute failing tenant/wrong-action/provider-outage/greater-than-100 regressions. Existing passing security tests remain passing; do not manufacture Red by breaking a stub.
- Proposed measurement fixture: 1,000 events, 10,000 participant/order rows, overlapping actor/team grants and disjoint action grants. Record cold/warm timings and query counts on one provider; agree an operational latency target before activation. A target miss is measured evidence for a separate policy-compatible index, never authorization truncation.

## Added order slice ownership and observable contract

Verified owners: `StudioOrders.razor`, `StudioOrderList.razor`, `StudioOrdersTests.cs`, `StudioContextService.cs`, existing registration order read/query contracts and entity repository. Bind exact backend owners with graph-first discovery before activation.

Proposed owners: `GetStudioOrdersQuery` / handler, immutable Studio order criteria and generated row/page DTOs, entity-returning repository paging, native private Studio read/HAL registration, and `StudioOrderQueryTests` / provider / HTTP tests. Search and filters concern existing order status and event/actor scope; contact fields retain current order-read/contact-sharing authority. Portfolio rows may link to existing per-event refund capabilities; a cross-event read grants no refund capability.

Acceptance: over 100 events are included once; purchasers/participants remain different; currencies/status snapshots are accurate; per-order and per-event links derive from actual capabilities; actor changes clear old rows before load; direct unauthorized requests reveal neither rows nor totals; no browser event/order fan-out.

## Preserved architecture decisions

### D03 — Complete authorization precedes paging and aggregates

**Decision:** A small Application scope resolver unifies actor membership candidates and direct event-team candidates, tenant-qualified and deduplicated. It evaluates native permissions in bounded batches before supplying authorized Event IDs to private queries. Use different authorized sets for management metadata, registrations/admissions and money according to existing operations. Selecting an actor narrows candidates; it never grants permission.

**Why:** Paging before authorization causes empty pages or leaked totals. First-page aggregation is false. Copying provider-specific policy expressions into SQL would create a second authorization engine.

**Alternatives steelmanned:** Full graph loading is easy to implement but scales poorly and over-fetches PII. SQL policy pushdown is fastest for simple local roles but cannot faithfully represent the current provider-native policy contract. A mandatory reporting projection/index would add synchronization and infrastructure. Choose minimal entity candidate reads plus native batch checks, complete authorized IDs, and server SQL reads. Do not silently stop at 100 events.

**Consequences/files:** Reuse `EventQuerySpecification` and existing filters/sorts; add only missing management filters. Minimal no-tracking Event candidates must not use `IncludeStandardDetails`. Existing repositories gain entity-returning query seams. Resolve/revalidate authority at response boundaries; no cross-request authority cache. Authorization changes after that boundary apply to the next request, as with existing native reads; do not promise instantaneous revocation of bytes already delivered.

**Bounds:** Event/attendee page default 25, maximum 100; deterministic sort with Event/participant UUID tie-breaker, bounded search input and explicit filter validation. Management offset paging supports numbered pages; it is not a snapshot across edits. Aggregate range defaults to last 30 UTC days, maximum 366 days. Native policy batches use a fixed bounded batch size; evaluate the complete scope under a 10-second request deadline. If it cannot complete, return explicit workload/availability ProblemDetails and ask for narrower filters. No fabricated partial totals. Parameter-limit-safe batching is mandatory for all provider SQL queries.

### D04 — Attendee portfolio is one paged query, not nested fan-out

**Decision:** Add a native cross-event participant query with authorization-scoped, immutable criteria and entity-returning repository paging. Map rows in the handler. Filter by scope/event, participant/order status, assignment state, admission target/day/session and search. Reuse the same query with a fixed event on the contextual attendees page.

**Why:** Rendering one component per event/order is not a scalable attendee-management model. Participants, unnamed ticket assignments and purchasers must retain different semantics.

**Alternatives:** Browser merging is rejected for N+1, paging and privacy. A second attendee database is unnecessary.

**Consequences/files:** Default rows show participant/assignment/status and event/entitlement context. Contact details remain behind existing authorized order/participant actions; do not broaden contact-sharing rights or include phone everywhere. No new bulk actions or exports. Cancel previous loads, clear old data immediately and ignore stale generations on actor/filter/event changes.

## Preserved behavioral scenarios

### Portfolio events and attendees

The system **SHALL** cover every event currently manageable by the principal in the current tenant, including direct event-team assignments. Membership **MUST NOT** substitute for per-event authority. Counts/filtering **MUST NOT** be derived from the visible page alone.

| Scenario | GIVEN | WHEN | THEN |
| --- | --- | --- | --- |
| S-SCOPE-1 Complete union | More than 100 manageable events exist through overlapping actor/team sources | All managed events is selected and filtered/paged | Events are deduplicated, ordered deterministically, and totals describe the complete authorized filtered population. |
| S-SCOPE-2 Isolation | Another tenant's IDs or an unauthorized actor/event filter are supplied | A private list/report is requested | No unauthorized row, total, facet or contact is disclosed; missing identity/authority fails closed. |
| S-SCOPE-3 Attendee population | A scoped set of internally registering events exists | Attendees are searched/filtered/paged | Only authorized participant rows appear, with order/assignment/admission context; purchaser and participant are not conflated. |
| S-SCOPE-4 Workload boundary | The complete authorized scope cannot be evaluated within the request budget | A broad read is requested | The response explains that narrower filters/retry are needed; no truncated total is presented as complete. |

Add S-SCOPE-5: GIVEN more than 100 events and disjoint order rights, WHEN the global order table is filtered/paged or the actor changes, THEN only the complete order-authorized population and its total appear, and prior-scope orders cannot return.

## Original task and path evidence

The following r1 tasks/manifests are retained for zero-loss discovery. Their numbering and commit packets are historical, **not executable approval**. On activation, assign fresh phases/commits, run a selected-provider/API contract gate in the owning PR, and run its final affected matrix before PR creation; never wait for the separate reporting workstream to validate these queries.

## Phase 3 — Manage The Complete Event Portfolio — NOT STARTED

- [ ] **3.1 Author scope isolation/completeness anchors and verify Red on current incomplete management behavior.**
  - Files: C4 tests and immutable new query/filter contract shapes only as necessary to compile.
  - Acceptance: S-SCOPE-1–2,4 and S-SPACE-4–5. More than 100 events, direct event-team grant without actor membership, overlapping candidates, denied count/facet, malicious tenant/actor scope, policy outage and revocation. Assertions concern returned data/errors, not provider call counts.
  - Ring 1: `rtk dotnet run --project tests/Event.Application.UnitTests/Event.Application.UnitTests.csproj --no-build -- --treenode-filter "/*/*/*StudioEventScopeTests/*"`.
  - Effort: M; dependency: Phase 2; guidance: auth, criticality/CQS, native provider semantics.

- [ ] **3.2 Implement minimal complete candidate/permission resolution and verify isolation anchors Green.**
  - Files: C4 scope resolver/port, Event entity repository/contract, context handler and immutable specifications.
  - Acceptance: candidates union personal/managed actors and direct event assignments; tenant-qualified dedup; membership is not permission. Bounded native batches, complete authorized IDs, no graph Includes/PII candidate overfetch, no policy-to-SQL reimplementation or cross-request authority cache. Scope errors are explicit and fail closed.
  - Effort: L; dependency: 3.1; guidance: auth/Clean Architecture/EF.

- [ ] **3.3 Wire server managed query and capabilities and verify complete filtered totals with stable ordering.**
  - Files: C4 native query/DTO/API/links/schema and SQL/read tests; G.
  - Acceptance: search/status/date/actor filters normalize and validate; page 25/max100; null-schedule placement and UUID tie-breaker are explicit; authorized totals/facets are independent of page. Parameter-limit-safe batches and 10-second deadline return honest narrowing errors. Revalidate authority before response. Existing actor-specific reads with independent non-Studio consumers remain real features; replace Studio callers, and remove old code only if no independent feature uses it—never retain a compatibility adapter.
  - Effort: L; dependency: 3.2; scenarios: S-SCOPE-1–2,4; guidance: CQS/EF/API/HAL.

- [ ] **3.4 Replace first-100 loading with the portfolio table and verify scope/filter changes cancel and clear old results.**
  - Files: C5 table/base/service/navigation/tests/docs.
  - Acceptance: S-SCOPE-1–2,4, S-SPACE-5, S-UX-1. All managed default; authorized actor/event narrowing; page/sort/filter passed server-side. Remove inconsistent first-page summary cards (full report cards return in Phase 6). Restore filters when returning from an event. Distinguish empty, loading, error and unavailable.
  - Effort: M; dependency: 3.3; guidance: MudTable server data/cancellation, Blazor/HAL/accessibility.

### Phase 3 Gate — verify once, then immediately commit C4/C5

- [ ] Run the client Release build and verify regenerated private query contracts compile.
- [ ] Run `rtk dotnet test --project tests/Event.Application.UnitTests/Event.Application.UnitTests.csproj --configuration Release --verbosity quiet -- --treenode-filter "/*/*/*StudioEventScopeTests/*"` and verify full-scope authority/completeness assertions; new SQL/HTTP cases wait for Ring 3.
- [ ] Execute C4/C5 packets and verify independent owned outcomes and clean scoped checkout.

### C4 — Authorized complete management read

- Type/scope/title: `feat(events): query the complete authorized management scope`
- Description: `Studio can list every event the current principal may manage, including direct event-team assignments, without deriving totals from the first page. Resolve tenant-qualified candidates and native permissions before deterministic server filtering and paging; refuse incomplete or unauthorized scope rather than leaking counts.`
- Changelog: public feature/security/API; native fragment required.
- Trailers: native `Change-Id` footer.
- Message override: Not overridden.
- Existing paths (E):

```text
src/Explore.Application/DTOs/Studio/StudioContextDto.cs
src/Explore.Application/Features/Studio/Handlers/Queries/GetStudioContextQueryHandler.cs
src/Explore.Application/Contracts/Persistence/IEventRepository.cs
src/Explore.Application/Specifications/Events/EventQuerySpecification.cs
src/Explore.Application/Specifications/Events/EventFilter.cs
src/Explore.Application/Specifications/Events/EventSort.cs
src/Explore.Application/ApplicationServicesRegistration.cs
src/Explore.Persistence/Repositories/EventRepository.cs
src/Explore.API/Controllers/StudioController.cs
src/Explore.API/Hateoas/Policies/StudioContextLinkPolicy.cs
src/Explore.API/Hateoas/RouteNames.cs
src/Explore.API/OpenApi/HalOpenApiSchemaCatalog.cs
src/Explore.API/Extensions/HateoasServiceExtensions.cs
docs/internal/API.md
docs/internal/API_CHANGELOG.md
docs/internal/AUTHORIZATION_PATTERNS.md
docs/public/documentation/readme/events-and-ticketing/studio-workspace.md
```

- New paths (N):

```text
src/Explore.Application/DTOs/Studio/StudioScopeDto.cs
src/Explore.Application/DTOs/Studio/StudioEventQueryDto.cs
src/Explore.Application/Contracts/Services/IStudioEventScopeResolver.cs
src/Explore.Application/Services/Studio/StudioEventScopeResolver.cs
src/Explore.Application/Features/Studio/Requests/Queries/GetStudioEventsQuery.cs
src/Explore.Application/Features/Studio/Handlers/Queries/GetStudioEventsQueryHandler.cs
tests/Event.Application.UnitTests/Features/Studio/StudioEventScopeTests.cs
tests/Event.Persistence.IntegrationTests/Repositories/StudioManagedEventQueryTests.cs
tests/Event.API.IntegrationTests/Features/StudioManagedScopeHttpTests.cs
```

- Commit paths: changed E/N, changed G and exact native fragment. New filter/sort members follow existing immutable specification style; do not add a competing query-builder abstraction.

### C5 — Portfolio event table

- Type/scope/title: `feat(events): filter and page the Studio event portfolio`
- Description: `Organizers can search, filter and page their complete management portfolio and open an event without losing list context. Replace first-page loading with the authorized server query, visible scope controls and generation-safe loading states.`
- Changelog: public feature; native fragment required.
- Trailers: native `Change-Id` footer.
- Message override: Not overridden.
- Existing paths (E):

```text
src/Explore.Blazor.Client/Pages/Studio/StudioEvents.razor
src/Explore.Blazor.Client/Pages/Studio/StudioEventPageBase.cs
src/Explore.Blazor.Client/Pages/Studio/StudioHome.razor
src/Explore.Blazor.Client/Services/StudioContextService.cs
src/Explore.Blazor.Client/Contracts/Services/IStudioContextService.cs
src/Explore.Blazor.Client/Components/Shell/Workspaces/StudioWorkspaceNavigation.razor
tests/Explore.Blazor.Client.Tests/Pages/Studio/StudioPagesTests.cs
docs/internal/BLAZOR.md
docs/public/documentation/readme/events-and-ticketing/studio-workspace.md
```

- New paths (N):

```text
tests/Explore.Blazor.Client.Tests/Pages/Studio/StudioEventPortfolioTests.cs
```

- Commit paths: changed E/N above and exact native fragment. The service interface path was verified; reuse it rather than creating a duplicate interface.

## Phase 4 — Manage Attendees Across Authorized Events — NOT STARTED

- [ ] **4.1 Specify participant/privacy scope and verify Red for denied/partial/late-scope behavior.**
  - Files: C6 tests and native immutable query shape.
  - Acceptance: S-SCOPE-2–4 and S-SPACE-5. Include participant versus purchaser, unnamed assignment, separate registration authority, more than 100 events/orders, disallowed contact details, tenant/actor filter spoofing and denial/outage. No test assertion uses internal Received counts.
  - Ring 1: `rtk dotnet run --project tests/Event.Application.UnitTests/Event.Application.UnitTests.csproj --no-build -- --treenode-filter "/*/*/*StudioAttendeeQueryTests/*"`.
  - Effort: M; dependency: Phase 3; guidance: auth/privacy/CQS.

- [ ] **4.2 Implement the authorized paged participant read and verify the privacy invariants Green.**
  - Files: C6 repository/contract/specification/native handler/API/HAL/G.
  - Acceptance: one server query family; minimal participant entities and required navigation only, DTO mapping in handler. Search/order/status/assignment/day/session/target predicates are server-applied after authorized scope. Default page25/max100, fully unique order, complete authorized totals. No phone/contact broadening or global contact export. Current existing contact/order capabilities remain authority.
  - Effort: L; dependency: 4.1; scenarios: S-SCOPE-2–4; guidance: EF/CQS/HAL.

- [ ] **4.3 Replace attendee fan-out and verify actor/event changes clear rows before any next result.**
  - Files: C6 UI/service/tests/docs.
  - Acceptance: portfolio/event pages reuse the read; actor/filter change resets paging and cancels old generation; old PII never flashes into new scope. Rows link to existing authorized attendee/order actions. Keyboard filters/table and localization work with text empty/error states.
  - Effort: M; dependency: 4.2; scenarios: S-SPACE-5, S-SCOPE-3, S-UX-1; guidance: Blazor/accessibility.

### Phase 4 Gate — verify once, then immediately commit C6

- [ ] Run the client Release build and verify participant query/client integration compiles.
- [ ] Run `rtk dotnet test --project tests/Event.Application.UnitTests/Event.Application.UnitTests.csproj --configuration Release --verbosity quiet -- --treenode-filter "/*/*/*StudioAttendeeQueryTests/*"` and verify authorized participant/error outcomes.
- [ ] Execute C6's packet and verify only owned changes committed/clean scoped checkout.

### C6 — Cross-event attendees (one vertical participant-read outcome)

- Type/scope/title: `feat(registration): manage attendees across authorized events`
- Description: `Organizers can search and page attendees across their permitted events instead of loading events, orders and participants in the browser. Keep purchaser, assignment and admission context distinct, preserve existing contact authority, and discard results from an old scope.`
- Changelog: public feature/privacy/API; native fragment required.
- Trailers: native `Change-Id` footer.
- Message override: Not overridden.
- Existing paths (E):

```text
src/Explore.Application/Contracts/Persistence/IRegistrationParticipantRepository.cs
src/Explore.Persistence/Repositories/RegistrationParticipantRepository.cs
src/Explore.API/Controllers/StudioController.cs
src/Explore.API/Hateoas/Policies/StudioContextLinkPolicy.cs
src/Explore.API/Hateoas/RouteNames.cs
src/Explore.API/OpenApi/HalOpenApiSchemaCatalog.cs
src/Explore.Blazor.Client/Pages/Studio/StudioAttendees.razor
src/Explore.Blazor.Client/Pages/Studio/StudioAttendeeList.razor
src/Explore.Blazor.Client/Pages/Studio/StudioEventAttendees.razor
src/Explore.Blazor.Client/Services/StudioContextService.cs
tests/Explore.Blazor.Client.Tests/Pages/Studio/StudioAttendeesTests.cs
docs/internal/API.md
docs/internal/API_CHANGELOG.md
docs/internal/BLAZOR.md
docs/public/documentation/readme/events-and-ticketing/ticketing-and-check-in.md
```

- New paths (N):

```text
src/Explore.Application/Specifications/Registration/StudioAttendeeQuerySpecification.cs
src/Explore.Application/DTOs/Studio/StudioAttendeeQueryDto.cs
src/Explore.Application/Features/Studio/Requests/Queries/GetStudioAttendeesQuery.cs
src/Explore.Application/Features/Studio/Handlers/Queries/GetStudioAttendeesQueryHandler.cs
tests/Event.Application.UnitTests/Features/Studio/StudioAttendeeQueryTests.cs
tests/Event.Persistence.IntegrationTests/Repositories/StudioAttendeeQueryProviderTests.cs
tests/Event.API.IntegrationTests/Features/StudioAttendeeScopeHttpTests.cs
```

- Commit paths: changed E/N, changed G and exact native fragment. A single query/UI outcome stays together; do not also redesign orders, admission scanner, transfers or form answers.

## I-VSD and scope limits

IVSD-F002/M002 (authority), F003/M003 (truthful complete counts), F005/M005 (usable scoped tables), F006/M006 (bounded practical hosting) require a refreshed assessment tied to this split. Source paths are r1/CTO evidence at `072f2b6dbd8e4b87e16b795bc02a9ea23f902046`, not passing evidence.

Do not add exports, bulk messages, role-derived client permission flags, a competing query builder, contact-right expansion or compatibility routes. Reuse immutable Event specifications and native entity repositories. No schema migration is assumed until actual query-plan evidence shows an index is needed.
