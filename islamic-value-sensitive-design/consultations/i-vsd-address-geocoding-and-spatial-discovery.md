<!-- ABOUTME: I-VSD review of provider-mediated address acquisition and exact spatial discovery. -->
<!-- ABOUTME: Traces principles, stakeholders, moral risks, mitigations, evidence gaps, and escalation boundaries. -->

# I-VSD Review - Address Geocoding And Spatial Discovery

Last Updated: 2026-08-26

## Scope

This project-case review covers the planned ISLAMU Event capability described in:

- [implementation plan](../dev/active/address-geocoding-and-spatial-discovery/address-geocoding-and-spatial-discovery-plan.md);
- [active context](../dev/active/address-geocoding-and-spatial-discovery/address-geocoding-and-spatial-discovery-context.md);
- [test-first task ledger](../dev/active/address-geocoding-and-spatial-discovery/address-geocoding-and-spatial-discovery-tasks.md).

It reviews provider responsibility for exact venue and private-home addresses, local address reuse, external geocoding, generated/AI-assisted location data, telemetry, self-hosting, accessibility, and the separately gated exact spatial-discovery handoff.

Out of scope:

- a halal/haram, makrooh/wajib, or Sharia-compliance ruling;
- certification that the implementation or product is ethical, private, secure, lawful, or accessible;
- independent production, stakeholder, and assistive-technology validation beyond the repository evidence;
- map-provider selection and alternative geocoders, which are deferred.

## Claim Boundary

This report is Islamic Value-Sensitive Design reasoning and traceability for provider-mediated software responsibility. It is not a fatwa, Sharia certification, product certification, legal opinion, privacy impact assessment, security audit, accessibility conformance report, or empirical proof of ethical outcomes.

The findings distinguish current repository evidence from planned controls. A planned mitigation is not treated as implemented until its Red/Green tests and phase gates pass.

## Findings

| ID | Finding | I-VSD basis | Affected stakeholders | Provider-controlled decision | Required mitigation and implementation owner | Status |
|---|---|---|---|---|---|---|
| IVSD-AG-01 | Exact address queries can disclose private-home or venue PII to an external provider without a user or operator understanding the disclosure. | Amanah, Non-Harm, Rights of People, Avoiding Spying | Editors, residents, venue owners, non-user bystanders, tenants | Whether a provider is enabled, what is sent, and whether provider use is visible/optional | `Provider=None` default; operator approval; minimal query; no hidden fallback; clear provider attribution/status; Phase 4 | Required before provider activation |
| IVSD-AG-02 | Tenant-wide local autocomplete can expose creator-private, organization-scoped, quarantined, or Private Home addresses. | Amanah, Justice, Non-Harm, Rights of People | Private-home residents, organizers, organization members, tenant users | Visibility model, SQL predicates, promotion authority and defaults | SQL-first tenant plus visibility filters; `UnknownLegacy+Quarantined`; Private Home never tenant-approved; explicit moderation; Phase 2 | Blocking |
| IVSD-AG-03 | Browser, nested Event, or AI/model coordinate inputs can be mistaken for trusted provider data. Current MCP coordinate fields are an authorized disclosure projection, not a write path. | Truthfulness, Amanah, Non-Harm | Editors, attendees, residents, operators | Which inputs are authoritative and how provenance is represented | Remove raw coordinate write members from all untrusted/generated paths; explicit manual/provider transitions; opaque protected provider token; Phase 1/4 | Blocking |
| IVSD-AG-04 | A protected token that is not tenant/actor/target bound can be replayed across users, organizations, locations, or concurrent edits. | Amanah, Rights of People, Non-Harm | Editors, location owners, tenants | Token purpose, scope, lifetime and mismatch behavior | Bind tenant, actor, organization scope, purpose, target Location/concurrency for update, provider/config, issuance and expiry; reject before persistence; Phase 4 | Blocking |
| IVSD-AG-05 | Address/query/coordinate/token/provider payloads can leak through logs, traces, metrics, cache keys, URLs, ProblemDetails, health checks, or support artifacts. | Avoiding Spying, Modesty, Non-Harm, Amanah | All data subjects, operators, support staff | Telemetry schema, HTTP method/cache policy, error and health payloads | Private POST/no-store; bounded instrument allowlist; captured-sink tests; no high-cardinality or sensitive labels; all phases | Blocking |
| IVSD-AG-06 | A mandatory external geocoder would make self-hosters dependent on provider availability, policy, cost, data footprint, or deplatforming risk. | Promise-Keeping, Amanah, Avoiding Gharar, Rights of People | Self-hosters, operators, tenants, users | Defaults, fallback semantics, deployment topology and provider substitution | Complete local/manual product first; `Provider=None` healthy; public demo forbidden for production; documented recovery and exit; Phases 3-4 | Required |
| IVSD-AG-07 | Assigning guessed provenance or broad visibility to existing rows would make uncertain data look trustworthy and could widen exposure. | Truthfulness, Amanah, Non-Harm | Existing location owners, tenants, operators | Migration defaults and moderation workflow | Generated append-only migration to `UnknownLegacy+Quarantined`; no heuristic backfill; explicit review before reuse; Phase 2 | Blocking |
| IVSD-AG-08 | Location-wide spatial approval would bypass the shipped per-EventLocation disclosure authority and could reveal an occurrence that is not public for that purpose or time. | Amanah, Rights of People, Non-Harm, Truthfulness | Event hosts, residents, attendees, tenants | Spatial eligibility and authorization owner | Home Discovery Phase 6 must derive each candidate from existing per-EventLocation/occurrence disclosure authority; no location-wide approval shortcut | Blocking before ADR acceptance |
| IVSD-AG-09 | Disabling an installed spatial capability while stopping lifecycle cleanup can preserve erased coordinates and resurrect them when re-enabled. | Promise-Keeping, Non-Harm, Rights of People | Erasure requesters, residents, operators | Capability lifecycle and cleanup registration | `Absent`/`InstalledDisabled`/`Serving`; installed-disabled continues transactional erasure/correction; absent requires verified cleanup/schema removal; Home Discovery Phase 6 | Blocking before ADR acceptance |
| IVSD-AG-10 | Area-only or degraded behavior can be misrepresented as exact "nearby" results. | Truthfulness, Avoiding Deception, Avoiding Gharar | Attendees, organizers, tenants | Product wording and fallback semantics | Area-only wording remains explicit; exact mode only after readiness and user action; no approximation fallback; Home Discovery Phase 6 | Required |
| IVSD-AG-11 | Map/provider-first UX can exclude keyboard, screen-reader, RTL, low-bandwidth, no-JS, or constrained self-hosted users. | Justice, Ihsan, Rights of People | Disabled users, RTL users, constrained-device users, self-hosters | Whether a map/provider is required and how controls are designed | Complete local/manual text experience; accessible combobox; no map requirement; source/visibility/status labels; Phase 3 | Required |
| IVSD-AG-12 | Provider software, service, image, and dataset terms can undermine promised open-source/self-hostable/outbound distribution paths or omit required attribution. | Amanah, Promise-Keeping, Truthfulness | Project Steward, contributors, self-hosters, data communities, recipients | Dependency/service/data selection, redistribution mode and notices | Clean-room source register; dependency/service/data license gate; operator-pulled vs ISLAMU-conveyed distinction; attribution; Phase 4 | Blocking before provider distribution |

## Recommendations

### Required Before PR A

1. Lock all untrusted coordinate write paths with failing tests before editing production code.
2. Preserve authorized disclosure/read coordinates, including current MCP projections; contract only browser/API/model/AI write authority.
3. Make manual/provider transitions explicit and atomic in the aggregate.
4. Fail closed when command tenant facts disagree with trusted context.
5. Add truthful source/visibility states and quarantine all legacy rows.
6. Make Private Home tenant-wide promotion impossible by invariant and test.

### Required Before PR B

1. Ship local-only acquisition first, with no provider dependency.
2. Apply tenant/visibility predicates before exact PII projection.
3. Use authenticated private POST, no-store, bounded bodies/results and dedicated rate limiting.
4. Prove current BFF antiforgery/header trust behavior instead of adding a new BFF endpoint.
5. Gate every UI mutation/search/moderation control by HAL link presence.
6. Provide a complete keyboard/screen-reader/RTL/no-map experience.

### Required Before PR C

1. Approve Photon topology, data footprint, support, recovery, terms, attribution and capacity.
2. Keep concrete provider selection and transport configuration outside Application.
3. Return no raw provider coordinates to the browser.
4. Use least-privilege time-limited tokens bound to tenant/actor/scope/target/concurrency/config.
5. Enforce the `Explore.Geocoding` instrument allowlist and zero-PII captured-sink tests.
6. Retain `Provider=None` as the documented healthy default and recovery path.

### Required Before Spatial ADR Acceptance

1. Consolidate exact-discovery execution under Home Discovery Phase 6 only.
2. Replace location-wide approval with per-EventLocation/occurrence disclosure eligibility.
3. Define and test `Absent`, `InstalledDisabled`, and `Serving`.
4. Continue transactional erasure/correction while installed-disabled.
5. Keep area-only wording and behavior when exact readiness is not green.

## Stakeholders

| Stakeholder | Interest / possible harm | Provider responsibility |
|---|---|---|
| Event organizers and editors | Efficient address entry without accidental disclosure or stale location data | Clear source/status, usable fallback, correction, safe errors |
| Attendees | Truthful discovery and no leakage of private venue details | Purpose-limited disclosure and honest proximity claims |
| Private-home residents and non-user bystanders | Address/coordinate privacy, dignity, correction and erasure | Conservative defaults, owner-specific reuse, anti-resurrection |
| Organization administrators | Scoped reusable locations without cross-organization leakage | Explicit grants, moderation and auditability |
| Tenant administrators | Governance controls and recoverable provider configuration | Lockable settings, HAL authority, bounded diagnostics |
| Self-hosters/operators | Optional dependencies, predictable resource needs, recovery and exit | `None` default, topology/runbook, no hidden cloud dependency |
| Support and incident responders | Useful diagnostics without receiving PII | Bounded event IDs/categories and redacted evidence |
| Disabled, RTL and constrained-device users | Equal access without map/provider requirement | Accessible text-first interactions and tested alternatives |
| OSM/Photon/data communities and licensors | Correct attribution and license/usage respect | Source register, notices, fair-use/production boundary |
| Project Steward and downstream recipients | Lawful distribution and truthful open-source/self-hosting claims | Dependency/service/data review for every delivery mode |

## I-VSD Principles And Domains

| Principle | Application to this workstream | Domains |
|---|---|---|
| Amanah / Trust | Steward exact addresses, admin power, provider disclosure, token keys and self-hosting promises | Technical, Operational, Governance |
| Truthfulness / Sidq | Do not call quarantined data approved, degraded area results exact, or planned controls implemented | Design, Technical, Evaluation |
| Justice / Adl | Do not impose provider/map/device/accessibility barriers or expose one group to another | Design, Technical, Governance |
| Non-Harm / La Darar | Prevent private-home, tenant, telemetry, token replay and erasure-resurrection harms | Technical, Operational |
| Rights of People | Preserve privacy, correction, erasure, consent and control over publication | Design, Technical, Governance |
| Avoiding Gharar | Make provider limits, costs, availability, data use and fallback explicit | Strategic, Operational |
| Avoiding Deception | No hidden provider request, false proximity, provider-washing or ambiguous approval state | Design, Evaluation |
| Promise-Keeping | Keep optionality, self-hosting, recovery, attribution and erasure commitments in actual behavior | Strategic, Operational |
| Ihsan / Excellence | Test adversarial boundaries, accessibility and recovery beyond minimal happy paths | Technical, Evaluation |
| Modesty / Haya | Avoid unnecessary public/private address exposure | Design, Technical |
| Avoiding Spying / Tajassus | No automatic origin, cross-context tracking or sensitive telemetry | Technical, Governance |

## Common Overlooked Failures And Outcomes

Feature type: exact address acquisition and proximity discovery.

Common overlooked failures:

- autocomplete queries silently leave the self-hosted instance;
- local private or legacy rows appear in tenant-wide results;
- model-generated coordinates are trusted because they are numeric;
- map/provider failure blocks basic address entry;
- logs or health payloads retain address fragments;
- erasure stops when an optional capability is disabled;
- location-wide approval ignores event-specific reveal time/audience;
- provider attribution or redistribution obligations are hidden by white-labeling.

Possible bad outcomes:

- exposure of a private home or sensitive venue;
- stalking, harassment, reputational harm, or cross-tenant data leakage;
- resurrection of erased coordinates;
- inaccessible event administration;
- provider throttling/outage or unexpected operating cost;
- false proximity claims and loss of user trust;
- breach of data/service/license commitments;
- self-hoster lock-in or unusable recovery.

Positive outcomes if implemented responsibly:

- safer, more accurate address entry without browser-trusted coordinates;
- clear provenance and conservative reuse;
- credible self-hosting with optional provider use;
- stronger erasure/correction evidence;
- accessible provider-free operation;
- honest discovery wording and auditable spatial eligibility;
- better privacy/security/license review evidence.

Provider questions before implementation:

- What exact fields leave the instance, to which service, for what purpose and retention?
- Who may see or promote each local address scope?
- How can a resident correct or erase data and prove no optional copy survives?
- What happens during provider outage, throttling, contract termination or dataset rebuild?
- Which provider/service/data obligations apply to operator-pulled and ISLAMU-conveyed distributions?
- What evidence proves accessibility and tenant isolation rather than merely planning them?

## Validation Gaps

- Repository implementation and focused Red/Green evidence now exist, but no production deployment evidence exists.
- No stakeholder interviews with private-home residents, organizers, disabled users, tenant administrators, support staff, or self-hosters were reviewed.
- No formal privacy impact assessment, threat model workshop, or data-retention schedule was provided.
- Photon remains disabled by default; each operator must approve and own its production topology, capacity, dataset, support, privacy position, terms and attribution before enablement.
- No representative query volume, latency evidence, alert ownership, or incident history exists.
- No independent accessibility audit or assistive-technology evidence exists.
- ADR-013 has no acceptance decider/date and its current location-wide model needs the plan's disclosure/lifecycle correction.

## Escalation Needed

- **Privacy/security owner:** approve the address data flow, token scope, telemetry allowlist, legacy quarantine and Private Home rules.
- **Architecture/Product/Privacy decider:** own ADR-013 acceptance and Home Discovery spatial eligibility/lifecycle changes.
- **Operations:** own Photon topology, capacity, update/swap, recovery, SLOs and alerts.
- **Qualified legal/distribution review:** required when service/data/dependency terms or alternative-license distribution compatibility are unclear.
- **Accessibility reviewer:** verify the implemented combobox and provider/no-provider states with assistive technology.
- **Sunni scholarly authority:** not currently required because no religious-legal ruling or contested religious-content decision is being made. Escalate only if later product claims introduce such a question.

## Evidence Reviewed

Repository evidence:

- `AGENTS.md`, `docs/QUICK_REFERENCE.md`, `docs/GOVERNANCE.md`
- `docs/legal/IP_GOVERNANCE.md`, `legal/CLA.md`, `docs/DUAL_VERSIONING.md`, `docs/CI_CD_GOVERNANCE.md`
- `docs/adr/ADR-013-postgis-proximity-discovery.md`
- `dev/active/home-discovery-experience/`
- `src/Explore.Domain/Location.cs`, `LocationPii.cs`
- Location create/update DTOs, validators, commands, handlers and mapping profile
- nested Event and AI draft location write paths
- `LocationController.cs`, current location dialogs and tenant lookup table UI
- `EventApiProxyExtensions.cs`
- current migration/provider/package composition
- location/privacy/Home Discovery/API/BFF/Blazor/architecture test inventory

Official external evidence, accessed 2026-08-25:

- Npgsql EF Core spatial mapping: https://www.npgsql.org/efcore/mapping/nts.html
- EF Core transactions and cross-context transaction requirements: https://learn.microsoft.com/en-us/ef/core/saving/transactions
- ASP.NET Core time-limited Data Protection: https://learn.microsoft.com/en-us/aspnet/core/security/data-protection/consumer-apis/limited-lifetime-payloads?view=aspnetcore-10.0
- .NET HTTP resilience: https://learn.microsoft.com/en-us/dotnet/core/resilience/http-resilience
- PostGIS `ST_DWithin`: https://postgis.net/docs/ST_DWithin.html
- Photon public service terms: https://photon.komoot.io/

Research tooling:

- `web_search` was used. Two framework searches returned no provider results; the PostGIS search returned the official PostGIS documentation.
- Direct official-document fetches were used for the named unresolved framework/service constraints.
- Context7 was requested through a documentation librarian but no Context7 MCP tool was available. No Context7 evidence is claimed.

## Missing Evidence

- Current provider contract/DPA/retention documentation for the selected production Photon topology.
- Operator load profile and dataset sizing.
- User-facing privacy/consent/status copy and translation review.
- Implemented telemetry schemas and captured-sink results.
- Implemented BFF/API/HAL/accessibility evidence.
- Home Discovery Phase 6 rewrite showing per-EventLocation disclosure and capability lifecycle.
- Legal approval if any optional service/image/dataset is conveyed by ISLAMU.
- Stakeholder complaints, incidents, support records, analytics, or usability studies.

## Context Inventory

Available and reviewed:

- repository/workspace planning, architecture, security, privacy, operations, legal and self-hosting documentation;
- current code/config/tests/generated contract paths for location, event, AI draft, BFF and Home Discovery;
- official framework/provider documentation retrieved through web tools;
- one repository scout and one independent Senior CTO plan audit.

Unavailable or not connected:

- Context7 MCP;
- external issue tracker/roadmap/support/incident/analytics integrations;
- provider account/contract/operations evidence;
- stakeholder and scholarly review records.
