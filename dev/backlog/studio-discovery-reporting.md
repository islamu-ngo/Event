# Studio Discovery Reporting — Deferred Delivery Contract

Last Updated: 2026-10-12 Europe/Brussels

> **Status:** Backlog; optional built-in reporting choice preserved, collection unimplemented.
> **Origin:** Studio event workspace r1, CTO split r2.
> **Owner:** Analytics/privacy implementer.
> **Dependencies:** Existing consent/governance, native guest/account order authority, privacy erasure and configured Data Protection. Independent of operational reporting rollout.

## Scope and PR split

Preserve aggregate-default views and separately consented conversions, both disabled by default. Split foundation/disposal and collector/report integration into separate PRs only if the disabled foundation is independently valid. Each PR requires its own current triad and I-VSD evidence; no reporting table/worker enters the current creation/workspace slice.

Operational reports do not wait for tracking. External providers remain optional write integrations; report imports, session replay, identity enrichment and new vendors remain outside scope.

## CTO corrections that supersede r1

1. **Periodic sweeps MUST use Quartz.** QUICK_REFERENCE rule27 and `.agents/rules/api-scheduling.md` require `AddSweepJob<TJob>`, a single-pass job and scheduler-neutral Application cleanup. Use a proposed `EventDiscoveryRetentionJob` under API Scheduling, plus an explicit `ScheduledJobNames` entry and existing composition tests. `DisallowConcurrentExecution` is scheduling protection, not a substitute for database idempotency/tenant fencing across concurrent processes.
2. **Retention availability is an activation gate.** No custom `BackgroundService` / `PeriodicTimer` fallback. Reject enabling collection when Quartz/retention composition is unavailable; if availability is lost, stop new linkage, fail health and document re-enable/drain recovery. Disabling collection must keep retained-data sweeps registered until disposal completes. Logical expiry always rejects expired tokens; physical deletion within24h is an availability-dependent operational target, not a downtime-proof guarantee.
3. **One receipt and one order each credit at most once.** Add portable tenant/event/receipt nonce uniqueness and converted-order uniqueness. Two eligible receipts issued before the same order must race to a single credit. The winner association and cohort increments commit atomically. Define dedup lifetime against the maximum allowed token/order timestamps; prove cleanup cannot erase the fence while an accepted replay can still use it.
4. **Browser clearing is insufficient withdrawal.** A copied opaque token remains usable after memory is cleared unless the server can invalidate it. Provide an event-scoped withdrawal/invalidation operation through the existing typed relay, require possession of the protected receipt, fence invalidation versus conversion, and make later use no-op without order disclosure. Offline withdrawal has no instantaneous server guarantee: stop browser sends immediately, invalidate on reconnection if within validity, and disclose this limit. Define what happens to an already aggregated conversion; do not claim retroactive deletion without attribution/provenance data.
5. **Optional collection never joins checkout transactions.** Confirmation/native order-access proof precedes analytics transaction; revalidate relevant erasure/version fence inside the linkage transaction. No payment/checkout availability depends on analytics. Replay of the optional write can lose measurement on outage but cannot change order state.
6. **Cohort and coverage truth.** Half-open UTC windows, one-order-per-visit observational conversion, categories closed before transmission, partial retention and disabled/error distinct from0. Collection-start/retained coverage is explicit; no history backfill or unique-person claim.

## Preserved design and lifetimes

### D06 — Optional first-party reporting; minimal short-lived correlation

**Decision (user-selected foundation):** Keep external write integrations; add first-party reporting independent of provider selection, not independent of governance. New tenant/instance-governed settings in the existing analytics group are reporting enabled (false), conversion enabled (false), and aggregate retention days (90; validated 7–365 and bounded by instance governance). Existing `analytics.enabled` remains the collection master switch and must also be true; do not reinterpret it as vendor-only. Consent and browser kill-switch/decline policy still constrain collection. Provider None does not disable an explicitly enabled local report, nor enable anything by itself. Operational reads do not require optional tracking to be enabled.

**Alternatives steelmanned:** Provider read adapters could reuse already collected data but require per-provider credentials, metric translation, consent/retention guarantees and network availability; existing `IAnalyticsProvider` cannot read. A mandatory external analytics service harms self-hosting optionality. An unrestricted raw clickstream is flexible but creates disproportionate privacy/retention risk. Built-in purpose-limited aggregates are selected; provider report import is deferred.

**Data flow:** Public EventDetail confirms a published public event exposure → typed bounded input through existing analytics relay → effective tenant governance/consent checks → receipt and daily aggregate updated transactionally → optional opaque event-scoped journey token returned only for permitted conversion → existing registration status UI observes confirmation → relay conversion branch executes existing native guest/account order-read contract → verifies tenant/event/current Confirmed/creation and confirmation timestamps → transaction atomically marks receipt converted and credits its daily cohort once. No optional analytics write joins or blocks checkout/payment transactions.

**Data model:** `EventDiscoveryDailyAggregate` stores tenant/event/UTC day/closed referrer category and nonnegative checked counters. `EventDiscoveryReceipt` stores event-scoped random receipt identity, issuance/expiry, eligible flag, aggregate cohort reference, replay protection and optional order association after valid conversion. The protected token contains only scope/receipt/expiry; use existing Data Protection key-ring authority through an Infrastructure protector port. No raw IP, page URL, referrer URL/host, contacts, user ID, fingerprint, user-agent profile or cross-event identifier is persisted. Referrer classification happens in browser and the server accepts only a closed category, never free-form strings. Client consent assertions are not cryptographic proof of a human choice; server governance caps them, and reports state observational limits.

**Lifetimes/defaults:** Journey validity is 30 minutes; browser token is memory-only, event-scoped and cleared on consent withdrawal. Replay/converted-order dedup receipts have a 24-hour retention target when cleanup is available; a converted order must have been created and confirmed after issuance and before journey expiry, so expiry cannot allow a second credit. View nonce/receipt replay is tenant/event-qualified. Daily aggregates default to 90 UTC days. Read paths exclude expired rows even before physical cleanup. Small discovery/form-answer cells use at least the existing three-observation suppression floor, including derived totals that would expose a suppressed cell; ordinary authorized operational counts are not unnecessarily suppressed. A complete anti-differencing anonymity guarantee is not claimed.

**Disposal/operations:** Add bounded batched cleanup using existing hosting/maintenance conventions. The former Quartz-disabled automatic-disposal promise is withdrawn: use the existing Quartz sweep authority with one-pass native cleanup operations. Reporting ingestion requires available retention composition; do not register a custom timer worker. Account-linked converted receipts join the existing local registration erasure transaction before order/user linkage is removed. Aggregate counts remain purpose-limited non-identifying observations and cannot be used to reconstruct erased identities. Startup replay and subject fencing prevent recreated order-linked receipts after erasure; revalidate authoritative order access during the analytics transaction. Document backup/restore retention handling and cleanup health. Metrics/logs use fixed low-cardinality outcomes, not payloads/tokens/contacts.

**Consequences/files:** Two Domain entities/rules, EF configs/catalogs, a dedicated aggregate/receipt repository returning entities, native ingestion/cleanup commands, receipt protector, effective bootstrap/consent fields, existing relay/browser/status UI integration. No new provider-query secret, package, service or separate database. Security/retention settings do not get tenant override beyond existing governance ceilings.

The six corrections above govern this preserved design. Governance defaults remain reporting=false, conversion=false, `analytics.enabled` required; Provider None is compatible with local opt-in. Journey validity30min, aggregate retention90days (governed7–365), receipt disposal target24h. These are planning choices, not implemented defaults. No raw URL/referrer/IP/contact/user/fingerprint or cross-event identity storage.

## Worst break and Red evidence before Green

The worst break is a consented receipt being converted twice or recreated after withdrawal/erasure, while retained order linkage crosses tenant boundaries. First write Domain/Application invariant-breakers for duplicate receipts, duplicate order across receipts, withdrawal then copied-token use, conversion-versus-withdrawal, conversion-versus-erasure, cleanup fence expiry and restored-order replay.

Use TimeProvider for exact 30-minute and UTC-day edges. Use real selected-provider transactions before committing collection logic, then the full five-runtime-provider matrix at this backlog's plan exit. Use captured logs/metrics to prove zero sensitive payloads/tokens/guest access material. Scheduler composition is tested without starting live services; real database locking is tested only in the appropriate persistence lane.

## Original task/path evidence with scheduler corrections

These retained r1 tasks and manifests preserve the source inventory, generation commands and acceptance goals. Their phase numbering/default commit packets are historical, not execution authorization. A fresh active triad MUST incorporate withdrawal, order-level dedup, availability-aware retention, scheduler composition and owning PR verification before use.

## Phase 5 — Collect Governed First-Party Discovery Measurements — NOT STARTED

- [ ] **5.1 Specify discovery counter/consent/replay invariants and verify Red before production writes.**
  - Files: C7/C8 Domain/Application tests and minimal immutable contract shapes needed by fixtures.
  - Acceptance: S-DISC-1–5. Off/provider-none distinction; aggregate decline policy; conversion requires explicit permitted consent+setting; 30-minute validity and 24-hour replay receipt retention target plus immediate logical expiry; no stable person identity; cross-tenant/event and forged order rejection; duplicate/concurrent conversion; no checkout dependence; nonnegative checked counters. Client claims never constitute server order confirmation.
  - Ring 1: `rtk dotnet run --project tests/Event.Domain.UnitTests/Event.Domain.UnitTests.csproj --no-build -- --treenode-filter "/*/*/*EventDiscoveryRulesTests/*"` and `rtk dotnet run --project tests/Event.Application.UnitTests/Event.Application.UnitTests.csproj --no-build -- --treenode-filter "/*/*/*EventDiscoveryCollectionTests/*"` at their respective Red/Green milestones.
  - Effort: L; dependency: Phase 4; guidance: criticality/auth/privacy erasure/CQS.

- [ ] **5.2 Implement portable aggregate/receipt persistence and verify Green domain rules and migration provenance.**
  - Files: C7 entities/rules/configs/repository/ports/DbContext/DI and generated catalogs.
  - Acceptance: transactional receipt+cohort updates, portable unique constraints, exact counters, tenant/event compound relations; receipt repositories return entities. Persist no raw referrer/URL/IP/contact/user/fingerprint. No extra database/vendor. Generate four application catalogs via native EF; record exact files and inspect generated output, never patch it. Real provider races run only at final Ring 3.
  - Effort: L; dependency: 5.1; scenarios: S-DISC-2–5; guidance: EF/migrations/Clean Architecture.

- [ ] **5.3 Wire governed typed relay and confirmed-order conversion and verify disabled/forged inputs cannot write.**
  - Files: C8 existing relay/governance/settings/bootstrap, new native handlers/protector/G.
  - Acceptance: reuse existing anonymous rate-limited relay boundary, no new anonymous endpoint. Separate effective reporting-enabled/conversion-enabled/retention fields; defaults false/false/90 days; 7–365 validated ceiling. Existing `analytics.enabled` remains the collection master and must also be true; Provider None does not suppress opted-in local collection or enable it itself. Operational reads remain available without tracking. Server normalizes tenant/consent and validates published event. Protector uses configured Data Protection authority. Conversion calls existing native account/guest order queries (not their internal guard), checks event/status/timestamps and current erasure fence, then atomically credits at most once. Responses reveal no order information.
  - Effort: L; dependency: 5.2; scenarios: S-DISC-1–4; guidance: auth/CQS/BFF/analytics.

- [ ] **5.4 Connect public event/registration UI and verify consent withdrawal clears correlation without affecting registration.**
  - Files: C8 public event/status/analytics consent/interop/client tests.
  - Acceptance: event-scoped memory-only journey; closed referrer categories computed before transmission; no rawURL/referrer instrumentation. Only eligible internal server-confirmed orders report conversions; external-click participation stays non-conversion. No fingerprint/identified first-party enrichment, cookies or durable local journey storage. Optional telemetry failure is contained. JS production interop edits are allowed through apply_patch; no ad-hoc Node execution/helper scripts.
  - Effort: M; dependency: 5.3; scenarios: S-DISC-1–5; guidance: Blazor/consent/privacy/accessibility.

- [ ] **5.5 Implement bounded disposal/erasure and update operator guides; verify expired/erased linkage cannot return.**
  - Files: C7 job/cleanup/read filters and existing erasure implementation; C8 settings/operations/public docs and I-VSD report.
  - Acceptance: receipt reads reject expiry immediately, physical sweep bounded and active when collection is switched off; scheduler unavailability blocks new collection and raises unhealthy retention. Purge order-linked receipts in existing local erasure before registration/user linkage removal; serialization/fence prevents raced recreation, including restore replay. Minimize stored linkage and mark sensitive fields for zero-PII telemetry. Document aggregate retention, oldest undisposed receipt health, outages, backup handling and off-switch; no legal/conformance certification. Exact erasure assertions use real local settlement at final Ring 3.
  - Effort: L; dependency: 5.3–5.4; scenarios: S-DISC-5, S-METRIC-3; guidance: privacy/criticality/error-tracking.

### Native generated catalog binding for C7

Use the selected provider's approved secret-backed design-time environment and factory; do not embed credentials/connection strings. Run only `migrations add`, not a migration reset or force removal. Four producer commands, with each shell configured according to OPERATIONS (SQLite masks inherited server fields):

```bash
rtk dotnet ef migrations add StudioEventDiscoveryReporting --context ExploreDbContext --project src/Explore.Persistence/Explore.Persistence.csproj --startup-project src/Explore.Persistence/Explore.Persistence.csproj
rtk dotnet ef migrations add StudioEventDiscoveryReporting --context ExploreDbContext --project src/Explore.Persistence.Migrations.Sqlite/Explore.Persistence.Migrations.Sqlite.csproj --startup-project src/Explore.Persistence.Migrations.Sqlite/Explore.Persistence.Migrations.Sqlite.csproj
rtk dotnet ef migrations add StudioEventDiscoveryReporting --context ExploreDbContext --project src/Explore.Persistence.Migrations.SqlServer/Explore.Persistence.Migrations.SqlServer.csproj --startup-project src/Explore.Persistence.Migrations.SqlServer/Explore.Persistence.Migrations.SqlServer.csproj
rtk dotnet ef migrations add StudioEventDiscoveryReporting --context ExploreDbContext --project src/Explore.Persistence.Migrations.MySql/Explore.Persistence.Migrations.MySql.csproj --startup-project src/Explore.Persistence.Migrations.MySql/Explore.Persistence.Migrations.MySql.csproj
```

Before staging, append each producer-reported timestamped migration/designer and current snapshot's exact path to C7. MariaDB has no fifth migration project; it consumes the MySQL catalog. This timestamp binding is mandatory generated provenance, not an unresolved implementation choice. Do not touch identity/Data Protection/privacy-authority catalogs.

### Phase 5 Gate — verify once, then immediately commit C7/C8

- [ ] Run the client Release build and verify relay/bootstrap/client/schema changes compile with no warnings suppressed.
- [ ] Run `rtk dotnet test --project tests/Event.Application.UnitTests/Event.Application.UnitTests.csproj --configuration Release --verbosity quiet -- --treenode-filter "/*/*/*EventDiscoveryCollectionTests/*"` and verify consent/native-order/expiry/replay/error assertions; record Ring 1 Domain evidence.
- [ ] Execute C7/C8 packets and verify generated catalogs/contracts, paired docs and I-VSD assessment are included in the correct atomic outcome; clean scoped checkout.

### C7 — Portable discovery store and disposal

- Type/scope/title: `feat(privacy): retain only bounded event discovery measurements`
- Description: `Optional event reporting stores daily aggregate counters and short-lived event-scoped receipts instead of a raw visitor history. Add portable transactional persistence, expiry and local erasure participation, with cleanup using the existing Quartz sweep; disabling collection does not unregister disposal.`
- Changelog: public privacy/migration/operator outcome; native fragment required.
- Trailers: native `Change-Id` footer.
- Message override: Not overridden.
- Existing paths (E):

```text
src/Explore.Persistence/ExploreDbContext.cs
src/Explore.Persistence/PersistenceServicesRegistration.cs
src/Explore.Persistence/Repositories/UserLocationPrivacyErasureRepository.cs
src/Explore.API/Hosting/ApiHostServiceCollectionExtensions.cs
docs/internal/DOMAIN.md
docs/internal/PRIVACY_ERASURE.md
docs/internal/OPERATIONS.md
schemas/islamu-event.md
docs/public/documentation/readme/security-and-identity/privacy-erasure.md
```

- New paths (N):

```text
src/Explore.Domain/Analytics/EventDiscoveryDailyAggregate.cs
src/Explore.Domain/Analytics/EventDiscoveryReceipt.cs
src/Explore.Domain/Analytics/EventDiscoveryRules.cs
src/Explore.Application/Contracts/Persistence/IEventDiscoveryRepository.cs
src/Explore.Application/Features/Studio/Requests/Commands/PurgeEventDiscoveryReceiptsCommand.cs
src/Explore.Application/Features/Studio/Handlers/Commands/PurgeEventDiscoveryReceiptsCommandHandler.cs
src/Explore.Persistence/Configurations/Entities/EventDiscoveryDailyAggregateConfiguration.cs
src/Explore.Persistence/Configurations/Entities/EventDiscoveryReceiptConfiguration.cs
src/Explore.Persistence/Repositories/EventDiscoveryRepository.cs
src/Explore.API/Scheduling/Jobs/EventDiscoveryRetentionJob.cs
tests/Event.Domain.UnitTests/Analytics/EventDiscoveryRulesTests.cs
tests/Event.Persistence.IntegrationTests/Repositories/EventDiscoveryReportingProviderTests.cs
tests/Event.Persistence.IntegrationTests/Privacy/EventDiscoveryErasureTests.cs
```

- Commit paths: changed E/N, exact four-catalog generated files bound above and exact native fragment. Store/disposal is independently valid while disabled; no collector is enabled by this commit.

### C8 — Governed collection and attribution

- Type/scope/title: `feat(privacy)!: add opt-in event discovery reporting`
- Description: `Operators can enable built-in aggregate event views without choosing an external analytics provider, and separately permit short-lived consented conversion measurement. Reuse the rate-limited relay, confirm orders through native access contracts, reject replay and scope forgery, and keep optional measurement outside registration and payment success.`
- Changelog: public privacy/config/API/security; native fragment required.
- Trailers: native `Change-Id` footer plus `BREAKING CHANGE: The analytics relay and consent bootstrap use regenerated typed reporting contracts, including the optional event-scoped receipt response.`
- Message override: Not overridden.
- Existing paths (E):

```text
src/Explore.Domain/Settings/Definitions/AnalyticsSettingDefinitions.cs
src/Explore.Domain/Constants/GovernanceSettingKeys.cs
src/Explore.Application/Settings/Groups/AnalyticsSettingGroup.cs
src/Explore.Application/DTOs/Analytics/RelayAnalyticsEventDto.cs
src/Explore.Application/DTOs/Analytics/AnalyticsGovernanceSettingsDto.cs
src/Explore.Application/DTOs/Onboarding/AnalyticsConsentBootstrap.cs
src/Explore.Application/DTOs/Onboarding/PublicExperienceSettingsDto.cs
src/Explore.Application/Features/PublicExperience/Requests/Commands/RelayAnalyticsEventCommand.cs
src/Explore.Application/Features/PublicExperience/Handlers/Commands/RelayAnalyticsEventCommandHandler.cs
src/Explore.Application/Features/InstanceOnboarding/Handlers/Commands/UpdateAnalyticsGovernanceSettingsCommandHandler.cs
src/Explore.Application/Features/InstanceOnboarding/Handlers/Queries/GetAnalyticsGovernanceSettingsQueryHandler.cs
src/Explore.Application/Analytics/AnalyticsRuntimeProfileResolver.cs
src/Explore.Application/Services/AnalyticsGovernanceService.cs
src/Explore.Infrastructure/InfrastructureServicesRegistration.cs
src/Explore.API/Controllers/AnalyticsRelayController.cs
src/Explore.Blazor.Client/Shared/AnalyticsInitializer.razor
src/Explore.Blazor.Client/Shared/CookieConsentBanner.razor
src/Explore.Blazor.Client/Pages/Events/EventDetail.razor
src/Explore.Blazor.Client/Pages/Registration/GuestRegistrationStatus.razor
src/Explore.Blazor.Client/wwwroot/js/analytics-bridge.js
tests/Event.Application.UnitTests/Features/PublicExperience/Commands/RelayAnalyticsEventCommandHandlerTests.cs
tests/Event.Application.UnitTests/Analytics/AnalyticsRuntimeProfileResolverTests.cs
tests/Event.Application.UnitTests/Analytics/AnalyticsConsentBootstrapDtoTests.cs
tests/Event.Application.UnitTests/Services/AnalyticsGovernanceServiceTests.cs
tests/Explore.Blazor.Client.Tests/Components/AnalyticsInitializerTests.cs
docs/internal/CONFIGURATION.md
docs/internal/OPERATIONS.md
docs/internal/API.md

docs/internal/API_CHANGELOG.md
docs/public/documentation/SUMMARY.md
islamic-value-sensitive-design/workstreams/i-vsd-studio-event-workspace.md
```

- New paths (N):

```text
src/Explore.Application/DTOs/Analytics/EventDiscoveryReceiptDto.cs
src/Explore.Application/Contracts/Infrastructure/IEventDiscoveryReceiptProtector.cs
src/Explore.Application/Features/Studio/Requests/Commands/RecordEventDiscoveryCommand.cs
src/Explore.Application/Features/Studio/Handlers/Commands/RecordEventDiscoveryCommandHandler.cs
src/Explore.Infrastructure/Analytics/EventDiscoveryReceiptProtector.cs
tests/Event.Application.UnitTests/Features/Studio/EventDiscoveryCollectionTests.cs
tests/Event.API.IntegrationTests/Features/EventDiscoveryCollectionHttpTests.cs
tests/Explore.Blazor.Client.Tests/Components/EventDiscoveryConsentTests.cs
docs/public/documentation/readme/configuration-and-operations/event-analytics.md
```

- Commit paths: changed E/N, changed G, exact native fragment and verified consent/registration-account/telemetry owners discovered before editing. Do not mislabel an unlocated analytics settings editor or authenticated status page as existing: locate its current owner using graph/service consumers, append exact path, then update all bootstrap/governance consumers. Reuse the existing analytics group UI rather than inventing an admin panel.

## Required additional owners

- Existing `src/Explore.API/Extensions/QuartzSchedulerExtensions.cs`, `ScheduledJobNames` and composition tests: resolve exact Application/test paths before activation.
- Existing relay contracts/handlers add typed withdrawal; no new anonymous route or separate privacy engine.
- Existing native guest/account order queries and `UserLocationPrivacyErasureRepository` remain authority; no direct reuse of an internal registration guard.
- One dedicated reporting read port/query may expose discovery values after authority/governance checks; do not enable operational metrics by enabling tracking.

## Release, recovery and scope limits

Generate four owning application migration catalogs only; MariaDB consumes MySQL but needs separate runtime evidence. No hand-edited migrations/snapshots, no identity/privacy-authority catalog reset, no retained-data Down without an explicit approved disposal/backup decision. Apply schema first, then disabled collector, then operator activation after healthy retention. No external-provider historical backfill.

Public/internal CONFIGURATION, OPERATIONS, PRIVACY_ERASURE, schema and analytics guide must explain activation, withdrawal limits, 24h target/health breach, backup/restore fencing, key-ring availability and collection off-switch. No privacy/legal/anonymity certification from suppressed cells. Pure aggregates may survive identity erasure only with an explicitly justified non-identifying purpose; linked receipts participate in authoritative local erasure.

IVSD-F002/M002, F003/M003, F004/M004, F005/M005 and F006/M006 require a fresh assessment for these revised authority/disposal mappings. The r1 assessment cannot authorize the withdrawal and scheduler corrections by implication.
