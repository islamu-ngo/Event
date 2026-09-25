<!-- ABOUTME: Planning-mode I-VSD assessment of governed event-resource implementation decisions. -->
<!-- ABOUTME: Revalidates consultancy findings against current repository evidence and maps provider duties to scenarios and tasks. -->

# Governed Event Resources — I-VSD Planning Assessment

Last Updated: 2026-09-22

## Review Metadata

- Mode: planning
- Subject: first-class event materials, live audience eligibility and protected delivery
- Workstream: event-resources
- Report kind: planning-assessment
- Report status: current
- Disposition: plan-aligned
- Evidence cutoff: 2026-09-22
- Reviewed input: 2026-09-22 plan-review revision, supplemented by the execution-time D5 coordinated remote-activation clarification and P4 ownership amendment at source revision `f34f6318c5000afe5d10c3fa4aca96c1967f4de1`
- Supersedes: none. This is a planning assessment of the [standalone consultancy](../consultations/i-vsd-event-resource-consultancy-report.md), not a rewrite of its historical evidence.

## Scope

This assessment consumes the shared current-repository evidence packet and the proposed [implementation plan](../../dev/active/event-resources/event-resources-plan.md), [tasks](../../dev/active/event-resources/event-resources-tasks.md), and [context](../../dev/active/event-resources/event-resources-context.md). It traces all fifteen consultancy findings/mitigations to implementation scenarios, bounded deferrals or review gates.

The baseline supports governed stored documents and external HTTPS destinations, live authenticated subject entitlements and explicitly public resources, independent metadata disclosure, schedule-relative timing, private file delivery, native governance, and a viable local standalone deployment. It does not include guest/dependent grants, scanner/provider adapters, identified attendee-access analytics, individualized certificate generation, arbitrary expressions, active embeds, presigned resource URLs or automatic resource federation/template cloning.

## Claim Boundary

This is provider-responsibility design reasoning grounded in Islamic values and current software evidence. It is not a fatwa, legal determination, Sharia certification, security certification, accessibility conformance result, stakeholder agreement or operational assurance. The generic resource capability does not adjudicate religious content. Qualified Sunni scholarly review becomes necessary if future platform behavior curates, endorses or classifies contested religious material.

The draft's operator opt-in for unscanned documents and bounded management-audit retention are proposed product policies. No user has approved enabling them in a deployment. The plan request authorizes this assessment and planning work only.

## Findings

All IDs preserve their identity from the consultancy. The lifecycle below is **accepted for mitigation in the proposed plan**, not resolved by implemented tests. Evidence level is repository/design traceability throughout; no runtime/user outcome is claimed.

| Finding / mitigation | Severity; principle and domain | Stakeholder / provider-controlled decision | Current evidence and correction | Mitigation owner and escalation |
| --- | --- | --- | --- | --- |
| IVSD-F001 / IVSD-M001 | High; Trust, Excellence, Promise-Keeping; technical | Organizers/attendees; first-class ownership and lifecycle | Event, session and storage lifecycle already exist; no resource aggregate exists | Domain/persistence, tasks 2.1–3.2; no scholarly escalation |
| IVSD-F002 / IVSD-M002 | Medium; Truthfulness, Excellence; design | Consumers and clients; meaning versus transport | Kind and delivery are independent requirements; only implemented delivery types are exposed | Domain/API, 2.2–2.3/7.2; individual certificates remain separate scope |
| IVSD-F003 / IVSD-M003 | High; Trust, Justice, Non-Harm; technical/governance | Subjects/admins; distinct resource identity and bounded admin authority | Current resolver/provider/HAL seams exist; the similarly named Event assembler is not a resource capability | Security, 4.1–4.3/5.2; threat/evidence review before release |
| IVSD-F004 / IVSD-M004 | High; Justice, Rights of People; technical | Participants, purchasers, speakers; exact current entitlement | LinkedUserId, HolderSubjectUserId, SubjectUserId and Actor assignments exist; purchaser is not participant | Domain/security, 4.1–4.2/6.2; guest/guardian rights need their own threat model |
| IVSD-F005 / IVSD-M005 | High; Modesty, Non-Harm, Truthfulness; design/privacy | Prospective/entitled attendees; discoverability versus content | Separate purpose-specific location disclosure is precedent; plan requires intentionally authored safe public title | API/UI, 2.2/5.1–5.2/8.1; sensitive content policy remains provider-owned |
| IVSD-F006 / IVSD-M006 | High; Trust, Non-Harm; technical/operations | Consumers and destination providers; response-time disclosure | BFF opaque upload sessions and HAL exist; ordinary resource DTOs must exclude destinations/keys | Storage/API/BFF, 6.2/7.2/8.1–8.2; external recipient resharing cannot be prevented |
| IVSD-F007 / IVSD-M007 | High; Trust, Excellence; technical | Uploaders/attendees; storage ownership and alternative access paths | PrivateOwner currently authorizes uploader; resource-owned rows need explicit generic-route denial | Storage, 3.2/6.1–6.2; retained registration/moderation evidence is a separate owner |
| IVSD-F008 / IVSD-M008 | High; Non-Harm, Truthfulness; technical/operations | File recipients and operators; release of unscanned content | No malware scanner; signature inspection can allow unknown MIME without a signature rule; public sample bucket is unsuitable | Security/operator, 4.3/6.1–6.3; instance opt-in is explicit, default remains deny |
| IVSD-F009 / IVSD-M009 | Medium; Promise-Keeping, Justice; domain/design | Attendees/speakers; schedule and cancellation effects | Public session discovery currently accepts only Published; completed-session recordings need resource-specific eligibility | Domain, 2.1–2.2/4.2; no scheduler substitutes for request-time time checks |
| IVSD-F010 / IVSD-M010 | High; Justice, Trust; governance | Tenant/instance operators and organizers; non-widening controls | Native hierarchy, locks, coordinated writes and manifest exist; SingleTenant bypass/caches cannot weaken security ceilings | Settings/security, 4.3/8.2; source review proves every mutation/import path is bounded |
| IVSD-F011 / IVSD-M011 | Medium; Avoiding Spying, Rights of People; privacy/operations | Attendees/managers; audit necessity and retention | Generic audit supports old/new snapshots, which are inappropriate for resource secrets; current access history is absent | Privacy, 3.2/5.1–5.3/6.3; audit expiry ships before audit-producing routes, identified access audit remains deferred |
| IVSD-F012 / IVSD-M012 | High; Trust, Promise-Keeping; strategic/technical | Small self-hosters and multi-provider operators; viable durable floor | Standalone default root mismatch remains; five primary providers exist; database keyring is already registered in production API | Hosting/persistence, 1.1–1.2/3.2/8.1–8.3; restore/provider evidence required before claiming readiness |
| IVSD-F013 / IVSD-M013 | Medium; Justice, Rights of People, Non-Harm; design/governance | Disabled users, rights holders, affected nonusers; metadata/alternatives/reporting/projection | Existing reporting/parent moderation and explicit template/federation seams are reused; no resource-specific behavior exists | UI/moderation/privacy, 6.3/8.1–8.3; scholarly authority only if contested content is adjudicated |
| IVSD-F014 / IVSD-M014 | High; Trust, Non-Harm; security/operations | Organizers, attendees, backup operators; persistent destination confidentiality | API already persists keys; retained expired keys still decrypt; all destinations will use dedicated tenant/resource purposes | Crypto/hosting, 7.1–7.2; full DB compromise is not defeated by keys stored unwrapped in that same DB |
| IVSD-F015 / IVSD-M015 | High; Truthfulness, Non-Harm; security/design | Attendees/incident responders; promises about revocation | Arbitrary redirects have no platform-enforceable downstream TTL; no resource presigning is planned | Security/product, 4.2/6.2/7.2; future TTL providers require measured provider guarantees |

### Material refinements to the source report

**Key lifetime:** The risk is missing/deleted/revoked key authority, not ordinary 90-day expiration. Retained expired keys can still unprotect payloads. Current production API already uses the selected primary database keyring. The plan verifies actual Combined registration and restore behavior; it does not create a redundant `/app/data/keyring` solely because the report suggested it.

**Revocation:** Same-origin files have fresh authorization per request. A cancellation committed before the final authorization snapshot denies; an already authorized stream may finish. An already revealed external link remains reusable until its provider expires/revokes/rotates it. The report's proposed maximum TTL cannot be honestly guaranteed for arbitrary external links. Presigned/provider-generated access is deferred, not silently represented as immediately revocable.

**Privacy:** Metadata defaults to EligibleOnly. An intentionally authored public title prevents fallback from a sensitive management title. Identified attendee access history is excluded; minimal manager action audit has a stated purpose and bounded retention. Audit minimization is implemented before file delivery.

**Governance and sequence:** Native instance ceilings, upload ownership and HAL checks precede delivery, rather than waiting for a UI group. The four delivery groups remain, with additional atomic phases that keep independently useful concerns reviewable.

## Recommendations

**Execution-time activation revalidation:** IVSD-F003/M003, F010/M010 and F015/M015
remain accepted for implementation with a durable resource-specific activation
fence. Missing, uncertain or changing remote policy deployments deny resource
access; no request-time Admin API observation or cached grant is introduced.
Operators and external policy writers must announce transitions and establish
replica convergence before activation. Unannounced remote edits are outside the
enforceable guarantee and must not be advertised as automatically detected.
This makes the deployment duty explicit, preserves the local self-hosting floor
and avoids an availability-first bypass of resource rights. It adds no attendee
history or new content disclosure. These are design conclusions, not evidence
that the activation protocol has already been implemented or operated.

**Plan-review revalidation, 2026-09-22:** the prior stale binding was substantively re-evaluated, not refreshed by metadata alone. The current plan preserves all fifteen finding/mitigation identities while refining S35–S42, native CQS authority, provider-input freezing, disclosure status behavior, audit-retention sequencing, delivery-track independence and HAL-only UI proof. Hidden/nonexistent/cross-tenant resources remain indistinguishable 404; intentionally disclosed known metadata uses 401 for anonymous authentication-required access and 403 for authenticated ineligibility. Denial produces bounded aggregate telemetry only, never identified attendee access history or a content-bearing audit event.

The recommendation is **plan-aligned** for technical implementation planning with fail-closed defaults and security controls before each delivery type. This does not authorize implementation, deployment opt-in, religious/legal certification, or an unscanned-file exception on any installation.

The selected design is repository-native: rich entities, typed relational rules, Application resolution of live facts, entity-returning repositories, existing authorization/PDP abstractions, same-origin streaming, encrypted write-only destinations, native settings, and BFF/HAL consumption. The plan does not require new runtime packages.

Independent security and persistence/executability reviews identified five concrete refinements: exact action authority, first-hop redirect limits, mandatory atomic slices, actual CI-selected five-provider cases, and explicit storage mapping/ownership constraints. All were applied and rechecked with no remaining high findings within that review scope. Reviews were independent and shared no peer output; post-hoc weighting is 60% security and 40% persistence/executability. This is planning self-review, not the separate CTO approval or an implementation security audit.

| Alternative | Strongest benefit | Reason not selected for this baseline |
| --- | --- | --- |
| Bare file/link fields or custom properties | Small initial feature surface | Cannot express independent disclosure, timing, revocation and management rights |
| Arbitrary policy DSL | Broad organizer flexibility | Unbounded security/support burden without a demonstrated need |
| Presigned resource downloads | Offloads transfer bandwidth | Adds a stale bearer window and provider/cache guarantees not needed for initial documents |
| Mandatory scanner integration | Stronger file-safety operating model | Changes provider/dependency/deployment scope; explicit optional clarification remains available, while the baseline defaults to denying unscanned files |
| Separate filesystem keyring for this feature | Easy to name a durable mount | Duplicates an existing database authority and may fragment Combined/enterprise keys |
| Identifier-rich access audit | Incident detail | No evidence establishes necessity; default access history would create surveillance risk |
| General bulk resource export/import | Convenient portability | Requires rights/secret/large-archive/import threat model; existing metadata export and protected files/backup provide a bounded initial path |

## Common Overlooked Failures and Outcomes

The most consequential failures are uploader access through generic StorageObject routes, a permissive admin/PDP shortcut overriding a domain ceiling, stale settings/identity facts authorizing new access, metadata totals leaking hidden rows, lost key/byte state during restore, and following external redirects with platform credentials. Another risk is describing container validation or explicit unscanned acceptance as a malware-clean verdict.

Tests must demonstrate these failures at HTTP, persistence, policy and log/serialization boundaries. A correctly styled UI or a HAL link omitted once is not security evidence. Positive outcomes are precise access, safer operator defaults, usable accessibility information, honest availability/revocation promises and a durable standalone option; none is claimed as measured yet.

## Stakeholders

Affected groups include public visitors, linked participants, purchasers acting for others, ticket transferees, session speakers, organizers/staff, tenant/instance operators, small self-hosters, disabled users, content rights holders, people depicted in uploaded material, moderators, support/security teams and external hosting/meeting providers. Guests, unclaimed participants and dependent attendees are explicitly denied unsupported grants rather than being assigned another person's identity.

## I-VSD Principles and Domains

Trust and Promise-Keeping require durable storage and a truthful access contract. Non-Harm requires private bytes, safe intake, denial under uncertainty and responsible takedown. Justice requires exact subject/session entitlements, correction paths and accessible participation. Truthfulness requires distinguishing metadata, eligibility, scanning status and external resharing limits. Modesty and Rights of People require controlled titles/destinations and data minimization. Avoiding Spying bounds individual access telemetry. Excellence favors tested native seams and maintainable domain ownership over a parallel authorization/storage framework.

Strategic, design, technical, operational, governance and evaluation domains all apply. Monetization and religious-content adjudication are not added by this workstream.

## Validation Gaps

- No implemented EventResource tests, runtime deployment, security audit or restore exercise exists yet.
- No stakeholder interviews demonstrate comprehension of teasers, live eligibility or unscanned warnings.
- No selected scanner service or meeting-provider TTL/rotation contract exists.
- No purpose/retention review supports identified attendee-access records; that feature is deferred.
- Existing keyring tests are useful but do not replace final resource-specific production/Combined composition evidence.
- Five-provider and RLS deployment claims require their actual configured evidence; current documentation is inconsistent about RLS status.
- Rendered component tests do not prove document accessibility or WCAG conformance for third-party meeting tools.

## Escalation Needed

- **Before implementation approval:** user review of proposed scope, final-snapshot revocation wording, unscanned operating model and management-audit retention. The draft is not user-approved.
- **Before protected file deployment:** security review of storage ownership bypasses, final decision ordering, private bucket posture, policy ceilings and scanner/unscanned configuration; operator accepts the real failure/backup responsibilities.
- **Before identified access history:** a separately scoped privacy purpose, retention, access/export/erasure and jurisdictional review. No such history is enabled by this plan.
- **Before a scanner/provider adapter or bulk bundle:** dependency/provenance review and provider-specific behavior tests.
- **Before making conformance/content claims:** accessibility specialists evaluate actual experiences; qualified Sunni scholarly governance decides any religious-legal or contested-content question.

These are deployment/future-feature ownership gates. They do not claim that a separate authority has approved the plan.

## Evidence Reviewed

Activation clarification evidence at `f34f6318c5000afe5d10c3fa4aca96c1967f4de1`:
`RuntimeAuthorizationProvider.cs` caches provider selection;
`CerbosConfigResolver.cs` caches settings-derived routing and resolves optional
Admin secrets; `CerbosAuthorizationService.ExecuteCheckAsync` enriches the
principal after receiving facts; `AUTHORIZATION.md` section 4.7 excludes Admin
HTTP observations from runtime authority. The independent architecture consultation
confirmed there is no reusable durable activation fence. D5 and P4 now own that
missing mechanism and its failure/ABA/stale-writer tests. This source/design
evidence supplements, rather than replaces, the historical manifest below.

Repository source revision inspected for this review: `0dca286c6e3d8d8d95fac2e78c8a7b29c4b2374a`. The source consultancy's current file SHA-256 is `05faaf7af7b67ff9db6e0f1bf43d62bf787c2db05c3c5a3f2fb2e8dc7ff35aef`; its internal historical evidence digest is not the hash of the current file.

The main agent reviewed the following source manifest after graph-first discovery. Concatenate GNU `sha256sum` output for these paths in this exact order and hash the concatenation to reproduce the evidence revision; no plan/report file includes its own digest.

```text
islamic-value-sensitive-design/consultations/i-vsd-event-resource-consultancy-report.md
src/Explore.Domain/Event.cs
src/Explore.Domain/EventSession.cs
src/Explore.Domain/RegistrationParticipant.cs
src/Explore.Domain/EventRegistration.cs
src/Explore.Domain/AdmissionTicket.cs
src/Explore.Domain/ParticipantAdmissionEligibility.cs
src/Explore.Domain/AdmissionCheckInState.cs
src/Explore.Domain/StorageObject.cs
src/Explore.Domain/StorageConstants.cs
src/Explore.Domain/Settings/SettingDefinition.cs
src/Explore.Domain/Settings/Definitions/StorageSettingDefinitions.cs
src/Explore.Domain/Constants/PermissionCodes.cs
src/Explore.Persistence/Extensions/PublicEventEligibilityQueryExtensions.cs
src/Explore.Persistence/Configurations/Entities/StorageObjectConfiguration.cs
src/Explore.Persistence/Configurations/Entities/StorageUploadSessionConfiguration.cs
src/Explore.Persistence/Database/PrimaryDatabaseProviderComposition.cs
src/Explore.API/Hosting/ApiHostServiceCollectionExtensions.cs
src/Explore.Blazor/Extensions/BffDataProtectionExtensions.cs
src/Event.Standalone/Program.cs
src/Event.Standalone/appsettings.json
src/Explore.Application/Contracts/Persistence/IUnitOfWork.cs
src/Explore.Application/Services/StorageContentSignaturePolicy.cs
src/Explore.Infrastructure/Services/RegistrationSensitiveValueProtector.cs
src/Explore.Blazor.Client/Explore.Blazor.Client.csproj
tests/Event.Persistence.IntegrationTests/DataProtection/DataProtectionKeyPersistenceTests.cs
tests/Event.Persistence.IntegrationTests/Fixtures/PrimaryDatabaseProviderBehaviorFixture.cs
tests/Event.Persistence.IntegrationTests/Database/PrimaryDatabaseProviderBehaviorContractTests.cs
.github/workflows/_build-test.yml
docs/internal/RELEASE_POLICY.md
```

### External functional evidence and provenance

Accessed 2026-09-06 through Context7 and Tavily, limited to framework/security/HTTP facts. No competitor implementation, third-party asset, dependency, source code, SQL or copied implementation structure is included in the planning handoff. The implementation derives names/decomposition from this repository.

| Source | Tool / bounded factual use |
| --- | --- |
| [Microsoft: Data Protection key management](https://learn.microsoft.com/en-us/aspnet/core/security/data-protection/implementation/key-management?view=aspnetcore-10.0) | Context7 and Tavily: expired versus revoked/deleted keys; rotation does not delete historical keys |
| [Microsoft: Data Protection defaults](https://learn.microsoft.com/en-us/aspnet/core/security/data-protection/configuration/default-settings?view=aspnetcore-10.0) | Context7 `/dotnet/aspnetcore.docs`: container key persistence and retained key lifetime |
| [Microsoft: Data Protection configuration](https://learn.microsoft.com/en-us/aspnet/core/security/data-protection/configuration/overview?view=aspnetcore-10.0) | Context7/Tavily: shared identity/keyring and explicit protection-at-rest considerations |
| [Microsoft: EF Core concurrency](https://learn.microsoft.com/en-us/ef/core/saving/concurrency) | Context7 `/dotnet/entityframework.docs`: application-managed concurrency and isolation; update tokens do not themselves secure a later external read |
| [OWASP: File upload](https://cheatsheetseries.owasp.org/cheatsheets/File_Upload_Cheat_Sheet.html) | Tavily: independent allowlist/signature/content controls, private storage, authorization and limits; no safety certification from one check |
| [RFC 9205: Building protocols with HTTP](https://www.rfc-editor.org/rfc/rfc9205.html) | Tavily: no-store prevents compliant caching; no-cache alone does not mean never store |

Dependency decision: no new runtime dependency selected. Existing .NET/EF/Data Protection/MediatR/Cerbos/MudBlazor/NSwag/storage primitives suffice for the selected baseline. Scanner/meeting-provider selection is deferred with its own clean-room/provenance gate.

## Missing Evidence

Operational private-bucket tests, resource-specific concurrency/policy parity, real recipient experiences, accessible file samples, scanner policy acceptance, identified-audit necessity and actual provider-specific expiration guarantees are absent. These remain explicit implementation/release or future-feature gates, never inferred from this analysis.

## Context Inventory

Available: current repository source, policy, tests, docs, local workstream contexts, all fifteen source report findings, official documentation, and graph navigation with source corroboration. The graph is stale and had no affected-flow result for the initial slice. Connected issues/support/incident/analytics sources were not required or consulted. No message was sent to external collaborators.

## Planning Handoff

- Workstream: event-resources
- Status: current
- Reviewed input: 2026-09-22 plan-review triad revision at repository source `0dca286c6e3d8d8d95fac2e78c8a7b29c4b2374a`
- Findings and mitigations: IVSD-F001→IVSD-M001 through IVSD-F015→IVSD-M015, same identities as the source consultancy
- Required mappings: plan §9 maps every pair to S01–S42 and tasks 1.1–8.3. Deferred features have named persistent backlog targets in tasks.
- Escalations: explicit execution request before implementation; actual security/provider/restore evidence before delivery; separate privacy/provider/scholarly gates only for their named future claims/features
- Current binding: D5 coordinated remote-activation clarification and P4 ownership amendment above supplement the historical plan-review hashes below; execution status is not bound to those historical hashes.
- Refresh triggers: changes to default audience/disclosure, scanner/unscanned policy, audit identity/retention, ownership, revocation timing, secret/key authority, guest/dependent access, export/import, federation/templates, provider integrations, or a material CTO rewrite

| Historical plan-review artifact | SHA-256 |
| --- | --- |
| `dev/active/event-resources/event-resources-plan.md` | `a5309071e0d42ea72e14cf2fe405074db6919405e5ab482cd5726541bbfa6591` |
| `dev/active/event-resources/event-resources-tasks.md` | `77ac58154a50d79d2b510446f30804285d72b8356cf1e3c94a66f8c9a65cc379` |
| `dev/active/event-resources/event-resources-context.md` | `45b64431848997a6538ab2a18a402d21a5f29a467da447a8cd80f9a7c09a7792` |

These hashes bind the reviewed plan, not execution status forever. Status-only task/context updates do not change provider responsibilities; a material design change requires the refresh workflow above. Local Markdown links resolve, all four artifacts carry the required documentation headers, and all fifteen consultancy pairs map to 42 scenarios and 21 implementation tasks. Product builds/tests were not run during planning.

## Review Lifecycle

| Date | Previous state | New state | Trigger | Evidence |
| --- | --- | --- | --- | --- |
| 2026-09-06 | None | Draft | Historical implementation-plan request; source report mapped to then-current repository behavior | Historical shared evidence manifest and proposed triad |
| 2026-09-06 | Draft | Current / plan-aligned | Historical completed-triad mapping and integrity review | Historical revision: 34 scenarios, 22 implementation tasks, nine phases and all fifteen finding/mitigation pairs |
| 2026-09-06 | Current / plan-aligned | Stale / changes-required | Historical user-requested CTO rewrite changed revocation/discovery/retention promises and mitigation sequencing | Historical revision: S35–S42, D5/D8/D9 and six PR boundaries; planning-mode revalidation was required |
| 2026-09-22 | Stale / changes-required | Current / plan-aligned | Senior CTO rebase against native CQS and current authorization/storage/release seams; security and architecture findings applied | 42 scenarios, 21 implementation tasks, eight phases/seven PRs, current §9 mapping and corrected artifact links |
| 2026-09-22 | Current / plan-aligned | Current / plan-aligned with activation clarification | Re-evaluated coordinated remote policy deployment duties and the limit of detecting unannounced changes | D5/P4 amendment, source revision `f34f6318c5000afe5d10c3fa4aca96c1967f4de1`, IVSD-F003/F010/F015; implementation evidence remains required |

All accepted findings remain unimplemented until their scenarios have actual evidence. Plan alignment is a mapping decision, not a declaration that mitigations are complete.
