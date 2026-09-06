<!-- ABOUTME: Evaluates provider responsibility for Redis-independent, database-backed ATProto login. -->
<!-- ABOUTME: Maps privacy, replay protection, browser binding, and hosting claims to implementation-plan evidence. -->

# Database-Backed ATProto Authentication - I-VSD Planning Review

Last Updated: 2026-09-06 Europe/Brussels

## Review Metadata

- Mode: planning
- Subject: database-backed ATProto transient authentication
- Workstream: database-backed-atproto-auth
- Report kind: implementation-plan
- Report status: closed
- Disposition: plan-aligned
- Evidence cutoff: 2026-09-06
- Reviewed input: database-backed-atproto-auth `plan-r2`; committed P1–P4, expiry and host-lifetime corrections; final owning gates and source review, with the remaining documentation slice reviewed in the working tree
- Supersedes: none

## Scope

Replace Redis-dependent OAuth state and tenant-session handoff storage with the existing primary database, behind an authenticated BFF-to-API boundary. Include atomic consumption, initiating-browser correlation, tenant/origin checks, bounded retention, readiness, and accurate hosting guidance. Cover SQLite, PostgreSQL, SQL Server, and MySQL/MariaDB, which are supported repository providers.

The user approved full implementation on2026-09-05 and requested a plan refresh on2026-09-06. Behavioral phases are now committed and their final owning gates accepted. This report closes the five implementation-scoped findings against the evidence below; release publication and deployment assurance are separate obligations, not implied approval.

The separate [authentication deployment documentation review](i-vsd-authentication-deployment-documentation-alignment.md) owns broader presets and deployment-documentation alignment. This report neither replaces nor edits it.

## Claim Boundary

**I-VSD design inference:** trustworthy stewardship, privacy, equitable self-hosting access, and accountable failure handling favor using an existing database without weakening authentication. The recommendation concerns software-provider choices, not a scholarly ruling, certification, legal determination, or guarantee of production security.

Repository inspection establishes current code behavior; official specifications establish protocol obligations. Neither substitutes for implementation tests, multi-provider contention evidence, or operational proof.

## Findings

### IVSD-F001 - Remove an unnecessary hosting dependency without weakening access control

- Lifecycle: resolved
- Severity: high
- Evidence level: repository-grounded; design inference
- Principle/domain: trust and justice; infrastructure and operations
- Affected stakeholders: self-hosting operators, attendees, organizers, tenant administrators
- Provider decision and mechanism: replacing the former Redis requirement improves hosting access without allowing a weaker process-memory fallback.
- Evidence: current `src/Explore.Blazor/Services/Auth/ApiBackedAtprotoTransientStore.cs`, real relational Production login tests, generated provider migrations; the removed `AtprotoAtomicCache` is historical planning evidence only.
- Linked mitigation: IVSD-M001 — one relational authority, with real provider and replica evidence.
- Recommendation: one relational authority for transient authentication in all environments, with no Redis or process-memory fallback.
- Validation requirement: Production-mode login without Redis, multi-instance consumption, restart, unavailable-store behavior, and all supported provider contracts.
- Owner: authentication/persistence implementation owner
- Resolution evidence: Production split/combined login and restart/failure cases pass in the complete API gate; `AtprotoTransientStoreRepositoryTests`, `AtprotoTransientStoreRepositorySqliteTests`, `AtprotoTransientStoreRepositoryProviderTests` and generated migration contracts pass in the final complete Persistence gate. These exercise relational authority and competing consumers, not an in-memory replacement. All four supported provider families remain in scope; the five existing structured-runtime skips are unrelated to these ATProto provider cases.
- Disposition/mapping: implemented and runtime-verified within the stated limits; `SC-01`, `SC-02`, `SC-07`, `SC-10`, `SC-11`; tasks `P1.1`, `P1.2`, `P3.1`, `P3.2`, `P4.1`, `P4.V2`.

### IVSD-F002 - Prevent bearer URL replay and login substitution

- Lifecycle: resolved
- Severity: critical
- Evidence level: repository-grounded; protocol-grounded
- Principle/domain: prevention of harm and trust; identity and user experience
- Affected stakeholders: people signing in and communities relying on account identity
- Provider decision and mechanism: encryption is not single-use or initiating-browser proof. The implementation now binds flows to proof, but final review demonstrated that a successful consume response could cross transient expiry while proof remained live.
- Evidence: `ApiBackedOAuthStateStore`, `AtprotoTenantSessionHandoffStore`, `BffAuthEndpoints`, `AtprotoAuthenticationHandler`; `AtprotoRelationalLoginFlowTests.CommittedConsumeArrivingAtExpiry_CannotIssueCookieOrReplay` observed the expired-handoff failure, then the seven-case class passed after shared post-response freshness validation. RFC 9700 Sections2.1 and4.7 remain historical protocol references.
- Linked mitigation: IVSD-M002 — preserve atomic consumption and browser binding; reject successful responses at or after transient expiry without retrying deletion.
- Recommendation: preserve exactly-one-winner consumption; bind each flow to a host-only stable browser proof at the initiating origin using a distinct per-flow HMAC; never issue an authenticated cookie at the canonical callback for a different initiating origin. Keep one bounded fifteen-minute proof cookie, require HTTPS, and never overwrite established proof during parallel flows.
- Validation requirement: races, stolen callback/handoff URLs in a second browser, absent/tampered proof, parallel logins, expiry boundaries, and interrupted consumption.
- Owner: BFF authentication implementation owner
- Resolution evidence: real BFF/private API/PostgreSQL flows cover proof rejection, contention and lost responses. The expiry regression first failed for handoff cookie issuance; after the shared post-response freshness guard, its seven-case class and the final complete API/BFF gates pass. Committed deletion stays final. Cookie attributes are verified at the HTTP surface; actual browser enforcement remains outside this evidence.
- Disposition/mapping: implemented and runtime-verified within the stated limits; `SC-02`, `SC-04`, `SC-05`, `SC-06`, `SC-12`; tasks `P1.1`, `P2.1`, `P3.1`, `P3.2`, `P3.R1`–`P3.R3`.

### IVSD-F003 - Make pre-authentication privilege explicit and narrowly bounded

- Lifecycle: resolved
- Severity: critical
- Evidence level: repository-grounded; design inference
- Principle/domain: accountability and trust; architecture and governance
- Affected stakeholders: all tenants sharing an instance
- Provider decision and mechanism: a canonical callback cannot infer the originating tenant before reading protected OAuth state. Existing session bootstrap requires DID/tenant context, so reusing it unchanged creates circular authentication or an unsafe tenant fallback.
- Evidence: `AtprotoAuthenticationHandler.CompleteCallbackAsync`, `AtprotoJwtService`, `ApiTenantResolutionMiddleware`, ADR-014.
- Linked mitigation: IVSD-M003 — dedicated machine authority and durable replay claims retained across the supported replica-clock difference, plus rejection when an in-flight INSERT commits at or after original assertion expiry. Real HTTP/PostgreSQL regressions reproduced both gaps; the replay-only cleanup margin and postcommit admission check passed focused verification, independent review and final owning gates.
- Recommendation: a dedicated machine-authenticated, instance-owned transient-auth boundary with no listing, no browser access, purpose-bound assertions, replay protection, and explicit tenant checks once the protected binding is recovered. Do not bypass tenant filters on business entities.
- Validation requirement: reject user JWTs, existing bootstrap tokens, missing/tampered/replayed service assertions, changed bodies, wrong routes/purposes, and wrong-tenant consumption. Verify exclusion from public discovery and YARP forwarding.
- Owner: API authentication implementation owner and security reviewer
- Resolution evidence: signed HTTP boundary tests reject invalid authority, body/purpose substitution and replay. The 58-case authentication and six-case PostgreSQL transaction classes pass after the clock/in-flight fixes, followed by final API/Persistence acceptance. Runtime MVC/ApiExplorer, generated public OpenAPI and BFF proxy tests exclude all four private routes; no business tenant-filter bypass was introduced.
- Disposition/mapping: implemented and runtime-verified within the stated limits; `SC-03`, `SC-04`, `SC-08`, `SC-13`, `SC-14`; tasks `P2.1`, `P2.2`, `P3.1`, `P4.R1`–`P4.R4`, `P4.V2`.

### IVSD-F004 - Bound credential custody and deletion claims

- Lifecycle: resolved
- Severity: high
- Evidence level: repository-grounded; design inference
- Principle/domain: privacy and trust; data governance
- Affected stakeholders: account holders and database/backup operators
- Provider decision and mechanism: transient payloads can contain PKCE/DPoP material and platform session data. Database storage adds backup retention and key-custody consequences even when payloads are encrypted.
- Evidence: protected payloads in the two existing BFF stores; `BffDataProtectionExtensions`; EF Core provider documentation.
- Linked mitigation: IVSD-M004 — unchanged credential TTLs and bounded deletion; retain only hashed replay metadata for the additional ten-second pairwise clock margin. Do not extend stored assertion acceptance expiry, which also guards late admission.
- Recommendation: retain BFF encryption and purpose separation; persist only hashed opaque locators and required metadata; use short server-enforced TTLs and bounded cleanup; keep keys outside the payload store; prohibit sensitive logs and telemetry labels.
- Validation requirement: ciphertext-only persistence, expiry without dependence on cleanup timing, key loss/rotation behavior, cleanup contention, and secret-free traces. Document that database backups are not synchronously erased by row deletion.
- Owner: persistence/operations implementation owner
- Resolution evidence: provider persistence and key/expiry flow contracts pass; `AtprotoTransientCleanupServiceTests` proves fixed-ID bounded cleanup and no second destructive attempt after lost acknowledgement. `AtprotoTransientTelemetryTests` verifies closed, non-sensitive output. Final Persistence/API gates cover the committed guards. ADR-014 and operator guidance distinguish unchanged ciphertext TTLs from ten-second extra hashed-replay retention and explicitly disclaim synchronous backup erasure.
- Disposition/mapping: implemented and runtime-verified within the stated limits; `SC-06`, `SC-07`, `SC-09`, `SC-10`, `SC-13`; tasks `P1.1`, `P1.2`, `P4.1`, `P4.2`, `P4.R1`–`P4.R3`, `P5.1`.

### IVSD-F005 - Publish operational truth rather than a stateless-authentication claim

- Lifecycle: resolved
- Severity: medium
- Evidence level: repository-grounded; design inference
- Principle/domain: truthfulness and accountability; communication and operations
- Affected stakeholders: adopters choosing hosting topology and incident responders
- Provider decision and mechanism: removing Redis state storage does not remove the need for persistent/shared BFF Data Protection keys, database migrations, or configured ATProto signing authorities.
- Evidence: `AtprotoOAuthClientFactory.GetReadiness`, `BffProviderReadinessService`, `BffDataProtectionExtensions`, public ATProto and troubleshooting pages.
- Linked mitigation: IVSD-M005 — publish real readiness/key requirements, a five-second absolute host-clock synchronization obligation, and truthful verification limits. Failed full-suite runs are not release evidence.
- Recommendation: readiness must check usable storage, not just DI registration; document supported key persistence independently of Redis and fail-closed restart behavior.
- Validation requirement: healthy/unhealthy dependency transitions through the real health surface; operator instructions for persistent single-node keys and shared multi-replica keys; explicit distinction from stateless checkout.
- Owner: operations/documentation implementation owner
- Resolution evidence: `AtprotoOperationalReadinessTests`, `AtprotoTransientProbeTests` and relational preflight cases pass through native BFF/private HTTP/relational surfaces. Independent cancellation-link breakers fail as intended; restored tests and final API/BFF gates pass. Public/internal parity review confirms usable-store readiness, optional-provider isolation, key persistence and clock obligations. Primary-provider provisioning versus optional exact-DID linking is stated consistently; neither implies role grants or publication consent.
- Disposition/mapping: implemented, runtime-verified and documentation-reviewed within the stated limits; `SC-01`, `SC-07`, `SC-10`, `SC-13`; tasks `P4.1`, `P4.2`, `P4.R3`, `P5.1`.

## Recommendations

Proceed with the database-backed design under the scenarios and phase gates above. Keep authentication failures closed; restart a login after an uncertain consume response rather than replaying a potentially successful consume. Do not expand this work into generic cache replacement, payment changes, secret-authority migration, or unrelated hosting presets.

Rejected alternatives remain process-local authentication fallback, stateless reusable handoffs, blind consume retry, and arbitrary credential-retention grace. For the clock correction, also reject extending assertion validity or its stored acceptance deadline; delay only replay cleanup by the explicitly bounded clock difference.

## Common Overlooked Failures And Outcomes

| Failure | Required outcome |
| --- | --- |
| Successful consume arrives after transient expiry | No cookie; deletion remains final even though browser proof is still live |
| Cleanup clock leads an accepting API clock | Used assertion still rejected; hashed claim retained until the slowest supported verifier rejects |
| An INSERT completes after the assertion admission deadline | Reject authentication while retaining the committed claim; no compensating delete or extended validity |
| Cleanup loses its committed acknowledgement | Stop without selecting/deleting another retry batch |
| Concurrent health callers exhaust login quota | Share one bounded probe per BFF cache miss; retain ordinary login admission |
| An earlier discovery failure makes a security test appear green | Prove the intended external metadata boundary was reached before rejection |
| An unrelated client timeout masks missing deadline propagation | Prove the driven cancellation reaches the actual request, not merely an eventual cancellation exception |
| Test process runs out of memory | Report incomplete verification, not a product fix or a passing release gate |

## Planning Handoff

- Workstream: database-backed-atproto-auth
- Status: current
- Reviewed input: `plan-r2` and its synchronized task/context artifacts
- Findings and mitigations: IVSD-F001→IVSD-M001; IVSD-F002→IVSD-M002; IVSD-F003→IVSD-M003; IVSD-F004→IVSD-M004; IVSD-F005→IVSD-M005
- Required plan mappings: Section9; expiry follow-up P3.R1–R3; replay-clock/in-flight correction P4.R1–R4; readiness deadline fidelity P4.V1c–V1d; final evidence/graduation P5.1–P5.5
- Escalations required before release: unresolved security findings or missing owning-gate evidence prevent closure; no scholarly/legal ruling is sought.
- Refresh triggers: authority, browser routing/binding, key custody, credential TTL, replay-retention bound, provider support, or weakened verification scope

## Stakeholders

Operators own deployment keys, backups, and retention policies. The platform owns safe defaults, correct tenant/browser boundaries, understandable errors, and truthful deployment requirements. Attendees and organizers must not need to understand Redis or recover from infrastructure details themselves.

## I-VSD Principles And Domains

Trust/Amanah maps to credential custody and exactly-one-winner state transitions. Privacy maps to encrypted short-lived payloads and no sensitive logs. Justice maps to viable lightweight hosting without a weaker authentication contract. Prevention of harm maps to browser/session substitution defenses. Truthfulness and accountability map to readiness, evidence, and explicit operational limits.

These are engineering interpretations using the repository I-VSD framework; no religious-legal conclusion is asserted.

## Validation Gaps

Implementation evidence exists for real provider contracts, Production split/combined HTTP login, key rotation, private assertion rejection and operational behavior. Post-response expiry, replay-clock retention and late committed admission have observed Red/Green evidence and independent review. Native clock-binding and cancellation-causality assertions detect each deliberately disconnected cancellation input. Final Release and complete BFF/API/Persistence gates passed on the restored source; behavioral commits are complete. Earlier failed or incomplete runs are not substituted as evidence. Graph coverage is empty for the worktree, so bounded source/caller tracing was used. No TestServer result proves browser TLS/SameSite enforcement, live external provider interoperability, production key mounts, least-privilege database configuration or backup erasure. Closing these implementation findings does not waive final documentation commits, release checks or deployment validation.

## Escalation Needed

No scholarly/legal escalation is identified for this narrow infrastructure decision. Any proposal to accept replay, remove browser binding, expose the private store publicly, or retain payloads beyond documented TTLs requires renewed security review and user scope alignment.

## Evidence Reviewed

- Current owning source and real regression classes named above; task-owned P3/P4 verification and final-review artifacts provide exact local log handles but are not durable release dependencies.
- The expiry class's seven passing cases were verified after its shared transport correction, followed by final owning API/BFF gates. Unchanged Application, Architecture and Standalone evidence is reused only for unchanged owning inputs.
- `AtprotoTransientAuthenticationTests` covers replica-clock drift and both first-use/replayed assertions whose INSERT crosses expiry; the 58-case class and six real PostgreSQL transaction-boundary cases passed after the guards. Final full Persistence/API acceptance is separate.
- `AtprotoRelationalPreflightTests` observes external metadata reachability before negative outcomes; an intentional earlier discovery failure broke the three affected assertions, then the restored eleven-case class passed.
- Final restored-source acceptance: full API 2,862 passed with one existing skip; full BFF 557 passed; full Persistence 1,601 passed with five existing structured-runtime skips, 1,606 total, 32m49.182s; zero failures and exit0 for all three. Release built with zero errors and 174 warnings. Persistence used an isolated native Podman engine after the shared engine's container-state calls stalled; binaries, provider selection and full single-process inventory were unchanged. This does not diagnose or repair the shared engine. Final independent source reconciliation found no remaining demonstrated blocker.
- Remaining entries below preserve the original planning source register; removed class names are historical evidence, not current implementation claims.

- Repository revision named in metadata; existing local planning-contract changes were read and preserved.
- `src/Explore.Blazor/Services/Auth/AtprotoAtomicCache.cs`, `CacheBackedOAuthStateStore.cs`, `AtprotoTenantSessionHandoffStore.cs`, `ApiBackedOAuthSessionStore.cs`.
- `src/Explore.Blazor/Authentication/AtprotoAuthenticationHandler.cs`, `src/Explore.Blazor/Extensions/BffAuthEndpoints.cs`.
- `src/Explore.API/Authentication/AtprotoJwtService.cs`, `AtprotoAuthenticationHandlers.cs`, `src/Explore.API/Controllers/AtprotoSessionController.cs`.
- `src/Explore.Persistence/Repositories/IdempotencyRepository.cs`, `AtprotoBootstrapReplayRepository.cs`, and PostgreSQL/SQLite integration tests.
- `docs/internal/adr/ADR-014-atproto-session-trust-bridge.md`, `docs/internal/HOSTING_ARCHITECTURE.md`.
- [AT Protocol OAuth specification](https://atproto.com/specs/auth).
- [RFC 9700 OAuth Security Best Current Practice](https://www.rfc-editor.org/rfc/rfc9700.html).
- [EF Core set-based writes and concurrency](https://learn.microsoft.com/en-us/ef/core/saving/execute-insert-update-delete).
- [EF Core SQLite limitations](https://learn.microsoft.com/en-us/ef/core/providers/sqlite/limitations).
- [ASP.NET Core Data Protection configuration](https://learn.microsoft.com/en-us/aspnet/core/security/data-protection/configuration/overview?view=aspnetcore-10.0).

## Missing Evidence

Context7 MCP was explicitly requested but is not available in this session's tool catalog; searches for both `context7` and `resolve-library-id` returned no tool. Official documentation was retrieved directly after web search. No external implementation source or dependency was imported.

## Context Inventory

The canonical local workstream is `dev/active/database-backed-atproto-auth/`, containing `database-backed-atproto-auth-plan.md`, `database-backed-atproto-auth-tasks.md`, and `database-backed-atproto-auth-context.md`. These gitignored files are local working memory, not a durable dependency of this report. Durable implementation decisions graduate to ADR-014 and operator documentation before workstream closure.

## Review Lifecycle

Initial evidence review: 2026-09-05. The user selected relational storage and rejected compatibility baggage. No unresolved user-choice fork remains; cryptographic, transaction, and browser-proof details are engineering constraints.

Revalidated on 2026-09-05 against the authored `plan-r1` plan/context/tasks triad: all five finding IDs map to existing behavioral scenarios and executable task IDs; the report and plan agree on trust boundaries, browser-proof lifetime, expiry and hosting claims. Disposition is `plan-aligned`, not implementation approval. Findings stay open until implementation evidence resolves them. Changes to API authority, callback routing, key storage, payload retention, or provider support make this review stale.

| Date | Previous status | New status | Trigger | Evidence/replacement |
| --- | --- | --- | --- | --- |
| 2026-09-05 | draft | current | Initial plan alignment | plan-r1; all five findings open |
| 2026-09-06 | current | current | Re-evaluated timing/retention and observed implementation evidence | plan-r2; stable finding IDs plus mitigation IDs; all findings remain open until full closure |
| 2026-09-06 | current | current | Reconciled replay/in-flight and reachability evidence; recorded deadline-test review gap | plan-r2/SC-14; provider behavior and finding identities unchanged; acceptance still incomplete |
| 2026-09-06 | current | closed | Reconciled final owning gates, committed behavior and reviewed operator guidance | All five findings resolved against their named runtime/documentation evidence; authority and deployment limits retained; release workflow remains separate |
