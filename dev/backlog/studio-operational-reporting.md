# Studio Operational Reporting — Deferred Delivery Contract

Last Updated: 2026-10-12 Europe/Brussels

> **Status:** Backlog; user-selected capability preserved.
> **Origin:** Studio event workspace r1, CTO split r2.
> **Owner:** Reporting/Application implementer.
> **Dependencies:** Complete native authorized scope from the portfolio contract. Discovery ingestion is not a prerequisite.

## User outcome and independent delivery

Portfolio and event reports should describe the organizer's real operational facts. Ship this as its own reporting PR after scope authority exists. Operational reads remain usable while analytics is disabled or no external provider is configured. A reporting API and its initial accessible summary/table can ship without collecting a single discovery receipt.

The combined r1 reporting phase made operational metrics wait for new consented tracking and coupled both to one broad final gate. Remove that dependency. Later discovery reports add separate measures and coverage without changing operational definitions.

## Preserved metric and read-port design

### D05 — Reports have a metric dictionary, not arbitrary counters

**Decision:** Use one reporting query family for portfolio and fixed-event scope. A dedicated read port computes counts and exact monetary sums over the authorized sets, returning Domain reporting values; handlers map them to generated API contracts. No report write side effects. Results include scope, effective filters, UTC range, generated-at, coverage, per-measure status and definition labels.

| Measure | Population and time basis | Important limit |
| --- | --- | --- |
| Managed events/upcoming/unscheduled | Complete management-authorized set after event/status/schedule filters; upcoming is current scheduled rollup | Not counted from table page; internal drafts do not become public occurrences |
| Order status distribution | Orders created in selected UTC period, grouped by current status | Current status can change; this is not an immutable historical status series |
| Confirmed registrations | Currently Confirmed internal orders with `ConfirmedAt` in period | A group purchase is one order, not one attendee |
| Participants/assignments | Participant units on scoped orders, distinguish assigned/unnamed and current eligibility | No unique-person claim across events |
| Capacity and active admissions | Current native capacity-pool and active check-in state per explicit target | Snapshot now, not a period total; do not sum shared pools or day/session targets as event headcount |
| Check-in/undo activity | Native action facts occurring in UTC period, separately labeled | Re-entry counts activity; active state/unique people are different |
| Confirmed order value/organizer earnings | Current Confirmed orders with `ConfirmedAt` in period; existing organizer-directed/earnings snapshots, grouped by currency | Exact minor units; exclude platform contributions from organizer earnings; not settled payouts, refund-adjusted net revenue or tax accounting |
| Event views/referrer mix | Public event page exposures, UTC day and Direct/Search/Social/External/Unknown categories | Refresh can count a view; no unique visitors, raw referrer or identity-rich attribution |
| Eligible visits/conversion rate | Consented event-scoped visits and at most one confirmed order per visit in a 30-minute window | Numerator/denominator belong to the visit's UTC-day cohort; anonymous traffic is excluded, attribution is observational |

**Why/alternatives:** Existing metrics have domain meaning. Arbitrary dashboard counters or combining money currencies would mislead. Materialized operational projections may be useful later if measured query volume demands them; the initial scope uses native indexed reads and daily discovery aggregates. Exact money aggregation must detect overflow/provider numeric conversion errors instead of emitting rounded values; final provider tests guard beyond-IEEE-754 precision.

## Additional contract decisions

- Report envelopes identify effective tenant/event/actor filters, generated-at UTC, period `[fromInclusive, toExclusive)`, measure population, snapshot-versus-period basis, coverage and availability. Defaults: last 30 UTC days, maximum 366. Apply only meaningful filters to each measure and say which were applied.
- Authority is per measure: event metadata, registration/participant/admission and money populations can differ. Denied measures are absent/unavailable without unauthorized counts; provider failure must not become a misleading partial success.
- Exact minor-unit sums stay integers/decimal-exact at storage/transport boundaries, grouped by currency. Do not use floating point in SQL, JSON consumption or chart totals. Provider tests cover values greater than 2^53, signed/unsigned limits and overflow. An unsupported measure is unavailable with a reason.
- Shared capacity pools count once at the correct target. Current active admission state, action activity/re-entry/undo and participant units are separate facts. Current order status is not a historical time series.
- Define a report's consistency point: preferably one provider-supported read snapshot for related counts; if measures are independently sampled, disclose timestamps/partial availability and never imply an atomic global accounting snapshot.
- Small discovery/form-answer cells and derived totals need inference-safe suppression. Ordinary authorized operational counts do not automatically inherit discovery suppression.
- Do not change money, refund, platform-contribution, tax, payout, admission or publication lifecycle to reconcile a dashboard.

## Red and verification gates

Before Green: `StudioMetricRulesTests` protects exact currencies, order/participant distinctions, shared pools, interval edges and unavailable-versus-zero. Application tests protect disjoint permission sets, tenant spoofing and provider outage. The worst break is exposing another event's attendee/money facts under a broader metadata grant.

One selected provider validates SQL translation/exact sums before the report slice is committed. Its final PostgreSQL/SQLite/SQL Server/MySQL/MariaDB lane verifies provider arithmetic and filtering once, with actual dispatch evidence. UI tests assert text/table explanations and old-generation rejection. No mock call-count mirrors or source scraping.

## Preserved scenarios

### Operational and discovery reporting

Reports **SHALL** share explicit event/scope/date filters. Each metric **MUST** state its population, time basis, availability and currency where relevant. Private attendee/money authority **MUST** remain distinct from event visibility.

| Scenario | GIVEN | WHEN | THEN |
| --- | --- | --- | --- |
| S-METRIC-1 Truthful operations | Orders, participants, capacity pools and repeated/undone admissions exist | Operational reporting runs | Independent facts are counted correctly over the entire authorized scope; current capacity/admission snapshots are labeled separately from period activity. |
| S-METRIC-2 Money safety | Confirmed orders use multiple currencies or totals exceed representable minor units | Money reporting runs | Currencies never merge; checked exact integer amounts are shown or that measure reports overflow/unavailable. Order value/earnings snapshots are not claimed to be settled payouts. |
| S-METRIC-3 No-data truth | Reporting is disabled, retention excludes part of the range, permissions are absent or a dependency fails | A dashboard loads | Unavailable/partial/error is distinguished from measured zero; coverage and last successful refresh are visible. |
| S-DISC-1 Default and decline | Built-in collection is disabled or effective consent/kill-switch disallows it | A public event page is viewed | No first-party record is written and normal event/registration behavior is unaffected. |
| S-DISC-2 Aggregate view | Built-in collection is enabled and anonymous aggregate measurement is permitted | A published public event is viewed | Only event-scoped aggregate views and closed referrer categories are recorded; no contact, raw URL, IP, fingerprint or stable person identity is retained. |
| S-DISC-3 Confirmed conversion | Separate conversion collection is enabled and consent permits short-lived linkage | A correlated internal order is confirmed within the observation window | At most one confirmed order credits the eligible event visit; server evidence and order-access authority are required. Replay/concurrency cannot increase it twice. |
| S-DISC-4 Forged/expired conversion | A forged token, different event/tenant/order, unconfirmed order or expired journey is submitted | Conversion is attempted | It is rejected/no-op without disclosing order status. External clicks and unlinked confirmations are not attributed conversions. |
| S-DISC-5 Withdrawal and disposal | Consent is withdrawn, receipts expire, an account is erased or data is restored | A later write/read/cleanup occurs | Linkage stops, expired/erased receipts cannot be used or resurrected, and retained data follows documented limits. |
| S-UX-1 Inclusive controls | Keyboard, RTL/localized or narrow-screen use | Creation, tables, filters or overview is operated | Controls remain labeled, keyboard-operable and readable; focus and announcements are predictable and chart information has a textual/table equivalent. |

S-METRIC-1–3 and S-UX-1 belong here. S-DISC-1–5 are owned by the separate discovery contract. Preserve original semantics; do not present deferred discovery charts as implemented.

## Candidate ownership and original outcome packets

### C9 — Authorized report contract

- Type/scope/title: `feat(events): report complete operational and discovery metrics`
- Description: `Organizers can read operational and discovery measures over their complete authorized event scope. Define each population and time basis, keep money currencies and admission facts separate, and return explicit coverage or unavailable states through a dedicated native reporting read port.`
- Changelog: public feature/API/security/financial reporting; native fragment required.
- Trailers: native `Change-Id` footer.
- Message override: Not overridden.
- Existing paths (E):

```text
src/Explore.Persistence/PersistenceServicesRegistration.cs
src/Explore.API/Controllers/StudioController.cs
src/Explore.API/Hateoas/Policies/StudioContextLinkPolicy.cs
src/Explore.API/Hateoas/RouteNames.cs
src/Explore.API/OpenApi/HalOpenApiSchemaCatalog.cs
docs/internal/DOMAIN.md
docs/internal/API.md
docs/internal/API_CHANGELOG.md
docs/internal/AUTHORIZATION_PATTERNS.md
docs/public/documentation/readme/events-and-ticketing/paid-events-and-payouts.md
```

- New paths (N):

```text
src/Explore.Domain/Analytics/StudioReportingValues.cs
src/Explore.Domain/Analytics/StudioMetricRules.cs
src/Explore.Application/Contracts/Studio/IStudioReportingQuery.cs
src/Explore.Application/DTOs/Studio/StudioAnalyticsDto.cs
src/Explore.Application/Features/Studio/Requests/Queries/GetStudioAnalyticsQuery.cs
src/Explore.Application/Features/Studio/Handlers/Queries/GetStudioAnalyticsQueryHandler.cs
src/Explore.Persistence/Queries/StudioReportingQuery.cs
tests/Event.Domain.UnitTests/Analytics/StudioMetricRulesTests.cs
tests/Event.Application.UnitTests/Features/Studio/StudioAnalyticsQueryTests.cs
tests/Event.Persistence.IntegrationTests/Repositories/StudioReportingProviderTests.cs
tests/Event.API.IntegrationTests/Features/StudioAnalyticsScopeHttpTests.cs
```

- Commit paths: changed E/N, changed G and exact native fragment. Real dedicated aggregate read port is intentional; do not change entity repositories to return DTOs.

### C10 — Reporting experience and knowledge graduation

- Type/scope/title: `feat(events): show scoped reports in the Studio workspace`
- Description: `Portfolio and event dashboards now use the same filtered reporting contract with visible metric definitions, currencies, consent coverage and error states. Keep contextual answer analytics distinct and document the durable workspace, authority and retention rules for maintainers and self-hosters.`
- Changelog: public feature/operator outcome; native fragment required.
- Trailers: native `Change-Id` footer.
- Message override: Not overridden.
- Existing paths (E):

```text
src/Explore.Blazor.Client/Routes.razor
src/Explore.Blazor.Client/Pages/Studio/StudioHome.razor
src/Explore.Blazor.Client/Pages/Studio/StudioEventOverview.razor
src/Explore.Blazor.Client/Components/Shell/Workspaces/StudioEventNavigation.razor
src/Explore.Blazor.Client/Pages/Studio/StudioRegistrationAnalytics.razor
src/Explore.Blazor.Client/Components/Shell/Workspaces/StudioWorkspaceNavigation.razor
src/Explore.Blazor.Client/Services/StudioContextService.cs
docs/internal/BLAZOR.md
docs/internal/OPERATIONS.md
docs/public/documentation/readme/events-and-ticketing/studio-workspace.md
docs/public/documentation/readme/configuration-and-operations/event-analytics.md
docs/public/documentation/SUMMARY.md
dev/_journal/domains/presentation-and-blazor.md
islamic-value-sensitive-design/workstreams/i-vsd-studio-event-workspace.md
```

- New paths (N):

```text
src/Explore.Blazor.Client/Pages/Studio/StudioAnalytics.razor
src/Explore.Blazor.Client/Pages/Studio/StudioAnalytics.razor.css
src/Explore.Blazor.Client/Pages/Studio/StudioEventAnalytics.razor
tests/Explore.Blazor.Client.Tests/Pages/Studio/StudioAnalyticsTests.cs
docs/internal/adr/ADR-studio-event-workspace-and-reporting.md
dev/backlog/external-analytics-reporting-import.md
dev/backlog/advanced-inline-program-authoring.md
```

- Commit paths: changed E/N above and exact native fragment. “Existing” in C10 includes files created by earlier approved phases; before their creation they remain proposed new, not evidence of current code. Add a second journal entry only for a verified non-obvious finding and record its exact owned path.

These are historical r1 source/path packets, not approved commands. Split C9/C10 into a reporting read-contract outcome and its minimal presentation outcome with immediate gates and complete literal path commits. Move discovery-only owners/settings/I-VSD bindings out of this slice. Do not write `ADR-studio-event-workspace-and-reporting.md` as a second authority over the proposed workspace ADR; reporting gets its own decision record.

## Documentation and operations

Update DOMAIN/API/API_CHANGELOG/AUTHORIZATION_PATTERNS, public paid-events/payouts guidance and the Studio reporting guide with definitions, currencies, permissions and unavailable states. Generated schema/client travel with their producer. No new reporting vendor, service, environment key, secret or migration is assumed.

Keep native release ULID fragments, recorded timings and no-PII diagnostics. External provider imports and materialized indexes remain separate backlog triggers. IVSD-F002/M002, F003/M003, F005/M005 and F006/M006 require refreshed ownership and evidence when this backlog becomes an active triad.
