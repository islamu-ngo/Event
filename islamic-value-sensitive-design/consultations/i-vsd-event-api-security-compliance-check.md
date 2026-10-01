# ISLAMU Event API Security: Maintained I-VSD Compliance-Style Assessment

Last Updated: 2026-10-01

## Review Metadata

- Mode: standalone
- Subject: ISLAMU Event API security against the complete vulnerable-.NET example catalogue
- Workstream: none; this is an assessment, not an implementation plan
- Report kind: compliance-check
- Report status: current
- Disposition: changes-required
- Evidence cutoff: 2026-10-01
- Reviewed input: Event commit `22909436883ea90f3e1477685ce3020b230c41da` on `develop`, including the explicitly identified dirty working-tree evidence below
- Reference input: `AlexGoOn/the-most-vulnerable-dotnet-app`, commit `60d060faf08079887e71a98498fe9a9d623e8ffc`
- Supersedes: none; this subject-specific report supplements, rather than replaces, the broader I-VSD and launch consultations
- Maintenance identity: this file; preserve its finding, mitigation, reference, and analysis identifiers in subsequent updates

## Executive Outcome

**Changes are required before an unconditional API security sign-off.** Event has substantial source-visible protections: native server authorization, selected-provider fail-closed behavior, typed DTO/SQL boundaries, private/capability-scoped responses, positive checked money calculations, purpose-bound identity proof, document inspection and durable payment claims. Those protections coexist with concrete unsafe decisions.

The comparison accounts for **53/53 documented families and 209/209 individual records**:

| Primary record disposition | Count | Interpretation |
| --- | --- | --- |
| Protected | 95 | A concrete control was traced in the named path, not a passing attack test or global certification. |
| Vulnerable | 5 | SSRF destination/redirect, response-byte and regex failure classes have source-supported unsafe paths under stated prerequisites. |
| Improvable | 25 | Partial controls, privacy contract, custody, defaults or defense-in-depth need refinement. |
| Unverified | 43 | Material runtime/provider/rendering/logging/hash/projection evidence remains absent. |
| Not applicable | 41 | Exact reference mechanism has no identified counterpart in the bounded API surface, or is an unestablished allegation. |
| Total | 209 | No reference record omitted or collapsed into an unchecked range. |

There are **27 stable findings and 27 corresponding mitigations**, including additional defects that are not one-to-one equivalents of the reference examples. Five Vulnerable rows therefore do **not** mean only five issues exist; neither do the 27 findings mean 27 exploited vulnerabilities.

Highest-priority source findings are recoverable Google client-secret settings (IVSD-F015), raw API keys retained by idempotency (IVSD-F016), privileged SSRF/DNS/redirect gaps (IVSD-F009-011), unbounded custom-property regex (IVSD-F012), and inconsistent PostgreSQL policy/session/role activation (IVSD-F024). Scope matters: a Google-disabled, local-webhook-disabled or Local-disabled deployment does not automatically expose its dormant feature, but software release must communicate and secure supported modes.

Additional priorities are Local enumeration/password policy, Keycloak token context and test quality, final file headers, response allocation/admission/fleet limits, host/operational exposure, Data Protection custody, public identity privacy and application-level commit/replay recovery.

**Launch readiness remains unverified.** This is a current-state source assessment and a maintained repair/verification ledger, not a live penetration test, ASVS certification, scholarly determination or operator sign-off. No code fixes, .NET builds or product tests were performed.

## Scope

The requested deliverable is one extensive, reusable assessment of **every vulnerability example** in the reference repository against **Event's API projects**. No product changes, configuration changes, migrations, dependency upgrades, commits, deployments, or vulnerability fixes are authorized by this report. Recommendations describe future work, not work already performed.

The comparison covers the reference's 53 documented vulnerability families and 209 separately inventoried functional records. Those records include implemented examples, illustrations, simulations, hidden examples, repeated demonstrations, and additional missing guards. The number is a coverage ledger, **not a count of 209 exploitable defects in either application**.

### API projects and execution boundaries

| Boundary | Included responsibility | Important exclusions and qualifications |
| --- | --- | --- |
| `src/Explore.API` | Dedicated ASP.NET Core API composition root, controllers, authentication schemes, middleware, HAL, OpenAPI, MCP, operational endpoints, and API-owned background work | Controller metadata is not sufficient evidence of resource or property authorization. |
| `src/Event.Standalone` | Combined host embedding the same API, route classification, cookie-to-bearer bridge, in-process dispatch, shared key registration, and host-specific initialization | This is a second host of the API, not an independent duplicate API implementation. |
| `src/Explore.Application` | Native command/query authorization decorators, handlers, manually instantiated validators, identity/tenant authority, DTO projection, capability and replay semantics | Review follows API callees; it is not a claim that every non-API application workflow was audited. |
| `src/Explore.Domain` | API-reachable value constraints, money/quantity calculations, state transitions, visibility and registration/ticket invariants | Domain rules matter only if every relevant adapter actually invokes them. |
| `src/Explore.Persistence` | API-reachable EF Core repositories, raw SQL, tenant filters, explicit bypasses, transactions, provider primitives, identity persistence and key storage | Provider-specific enforcement is not silently generalized across PostgreSQL, SQLite, SQL Server, MySQL and MariaDB. |
| `src/Explore.Infrastructure` | API-triggered HTTP providers, storage, templates, webhooks, authentication integrations, secret-bearing provider operations | An operator-configured internal service is distinguished from an untrusted caller-selected destination. |
| `src/Explore.Secrets` | Selected secret authority, signing/encryption material, database/TLS configuration, rotation-aware transport | No actual secret values or production credentials are reproduced. |
| `src/Explore.ServiceDefaults` | Health/metrics mapping, telemetry, redaction and outbound-client defaults used by API hosts | Registration of a redactor does not prove all logging calls use it. |
| `src/Event.Web.BffHosting` and relevant `src/Explore.Blazor` adapters | Only the API ingress trust boundary: cookie sessions, antiforgery, privileged-header sanitization and transport | Browser components, DOM sinks, accessibility and the complete frontend are outside this API-only audit. |
| Wire contracts, setup/configuration contracts and existing tests | Only contracts and tests needed to explain API authority and input handling | The setup browser/desktop/terminal/CLI projects are not additional HTTP API hosts. |

`Explore.AppHost` orchestrates local services; `Event.MigrationService` owns migration execution. Neither is treated as a public business API. Their deployment or privilege choices are relevant where they determine the API's runtime boundary. The complete Blazor BFF endpoint catalogue is not assessed as a separate product surface.

The reviewed generated OpenAPI working-tree artifact contains **759 paths and 947 GET/POST/PUT/PATCH/DELETE operations**. There are **204 tracked controller files**, including controller bases. These are inventory measurements, not security pass counts. OpenAPI does not enumerate the entire runtime surface: `/mcp`, `/health`, `/alive`, `/metrics`, webhook-readiness health routes and the optional scheduler status endpoint require separate consideration.

### Hosting and control flow

Both hosts target .NET 10. `global.json` selects SDK `10.0.302`; centrally declared ASP.NET Core JWT/EF Core packages include `10.0.10`. The Application project uses repository-native CQS interfaces and Mapperly; old references to MediatR in some documentation are not the current dispatch mechanism.

The dedicated host delegates to `AddApiHostServices`, `RunApiHostStartupAsync`, `UseApiHostMiddleware` and `MapApiHostEndpoints`. The observed API order is exception handling/forwarded headers/security headers/logging, routing/private-response handling, tenant resolution, authentication, post-authentication tenant binding, rate limiting, authorization, tenant lifecycle gating, idempotency, support audit, output cache and ETag. Special ATProto transport branches have their own preceding guards. The old pipeline list in `docs/internal/API.md:244-271` is not an authoritative substitute for this implementation.

In Standalone, forwarded headers and the combined bridge run before the API branch. A successful cookie session is converted into the server-held API bearer token; the API authenticates it again. Browser-controlled privileged headers must be removed before that conversion. Cookie-origin writes need antiforgery; an explicitly supplied external bearer is a different credential boundary. API-owned paths are determined by explicit prefixes and controller endpoint metadata, not simply by whether a page looks administrative.

### Launch context

The existing [launch consultation](i-vsd-v0-1-launch-consultancy-report.md) records intended simultaneous public software release and an Islamic-only Official Instance: SingleTenant, Amir administering the instance, Keycloak authentication and Maileroo delivery. This is **intent, not verified deployment state**. SingleTenant does not remove same-tenant organizer, group, organization, attendee, administrator or capability isolation duties. The software assessment therefore also covers supported Local Identity and ATProto modes rather than treating intended Keycloak use as proof that their weaknesses cannot affect adopters.

### Repository and provenance boundary

Event had unrelated tracked and untracked changes before this review. They are not reverted, committed or repaired. The report is based on the working-tree implementations actually inspected, not a fictional clean commit snapshot:

- Relevant backend/test diff fingerprint, obtained with `git diff -- src/Explore.API src/Explore.Application src/Explore.Persistence src/Explore.Infrastructure src/Explore.Secrets tests/Event.API.IntegrationTests tests/Event.Architecture.Tests | sha256sum`: `70ee549ddbf880c825a6eda237a69ef7982fa5c4a2d8494791667d630512b518`.
- OpenAPI SHA-256: `26d3a63374f28b6d2f8d2ae6fa479ce2521c54beecbc280b6f59bb7bd5ee1fec`.
- Relevant pre-existing modified files include API lookup/organization/user/add-on controllers, `SetupLiveApplicationService`, `TriggerManagedControlPlaneRegistrationCommandHandler`, `CerbosPolicyPackageService`, `EfCoreUnitOfWork`, `RotationAwareHttpClientFactory`, and architecture/integration tests.
- This is not a whole-tree fingerprint: untracked files and changes outside the named diff paths require separate evidence when they affect a finding.

The external reference repository's MIT license was checked before research. Its first-party examples were inspected by a read-only researcher; the Event assessment consumes an independently worded, **source-free functional catalogue**, not external implementation snippets or an implementation blueprint. Reference filenames and line numbers are retained as provenance. No third-party code, attack payloads, sample credentials, documentation passages or assets are copied into Event. Future fixes must start from Event-native requirements and current code in a fresh implementation context, following [IP governance](../../docs/internal/legal/IP_GOVERNANCE.md).

### Contribution contract for this change

This is a report-only Tier 4 documentation deliverable, although its subject is security-critical. The intent registry has no exact report-only API security audit entry; auth/write-endpoint/change intents are not falsely applied as product-edit authorization. The explicit user scope and standalone I-VSD report contract own the file location and content.

Before editing, the review loaded the I-VSD integration/report/context/evidence contracts, selected technical/operational/governance/data lenses, the Quick Reference, relevant governance headings, API/auth rules and source-provenance requirements. Only this subject report is changed. Minimum verification is report coverage, schema/metadata, relative links, source locators and whitespace. No .NET build or product suite is required for this Markdown-only change. There is no endpoint, deployment, external configuration or administrative feature change requiring public/internal product-document parity. No PR is being created and no PR merge gate is claimed.

## Claim Boundary

This report provides security design reasoning and implementation traceability through I-VSD. It is not a fatwa, Sharia certification, product certification, legal opinion, completed penetration test, ASVS certification, or proof of secure operation or ethical outcomes.

The evidence supports two levels: **design validation** and **implementation traceability**. Existing test code is useful evidence of an intended invariant, but an unread test name is not evidence, and a read test is not a passing test run. No Event application, exploit, product test suite or build has been executed as part of this report-only assessment. No deployment, browser execution, network policy, production log export, actual identity realm or provider account has been audited.

The following terms have deliberately narrow meanings:

| Disposition | Meaning in this report | What it does not mean |
| --- | --- | --- |
| Protected | A cited control addresses the particular functional failure in the inspected path | Every endpoint/provider/configuration is safe or the control passed runtime attack testing. |
| Vulnerable | A cited implementation permits the unsafe condition under stated prerequisites | An exploit was executed or all deployments are exploitable. Static and runtime claims remain separate. |
| Improvable | A real control exists but a documented design, coverage or defense-in-depth gap remains | The reference exploit necessarily succeeds. |
| Unverified | Material evidence is missing or a mechanism's effectiveness needs runtime/operational validation | Absence of evidence is a confirmed bug. |
| Not applicable | The reference-specific mechanism has no identified equivalent within the bounded API surface | Universal absence of the weakness, including future features or out-of-scope clients. |
| Mixed | Different variants, schemes or provider paths have different outcomes | Permission to flatten the unsafe path into a broad pass. |

I-VSD checklist results are correspondingly `Pass`, `Concern`, `Fail` or `Not reviewed` for the **named scope**. A static vulnerability uses `Fail` only for the observed unsafe decision; uncertainty about deployment is retained. No religious-legal conclusion is needed to recommend ordinary access control, data minimization or secure engineering. Financial/religious/legal determinations remain with qualified authorities.

Severity is an engineering triage judgment, not an invented CVSS score. High means plausible account, tenant, private-data, privileged-provider or consequential state compromise; Medium means material hardening, availability or disclosure exposure under narrower prerequisites; Low means limited exposure or clarity debt. A release evidence gate may be high priority without being a confirmed high-severity exploit.

## Reference Inventory And Interpretation

### Exhaustiveness ledger

At reference commit `60d060faf08079887e71a98498fe9a9d623e8ffc`, the inventory accounted for:

| Inventory unit | Count and treatment |
| --- | --- |
| Tracked files | 179 |
| First-party text files | 132, read completely by the reference researcher |
| Documented families / registry families / demo pages | 53 / 53 / 53 |
| Other page files | 3 |
| Controller files | 24, including the common demo base |
| HTTP controller actions | 44, including ordinary/protected comparison operations |
| Service files | 7, including an empty file and the unused NoSQL example |
| Predefined fixtures | 48: 13 labelled ordinary and 35 labelled attacks |
| Tracked tests | 0 |
| Vendored Bootstrap distribution files | 44, accounted for but excluded from first-party example analysis |
| Other assets | Screenshot viewed; favicon accounted for; ZIP central directory inspected without extraction |
| Functional comparison records | 209, `REF-001` through `REF-209` |

Counting preserves distinct failure mechanisms and additional guards; repeated values alone do not create new vulnerabilities. REF identifiers are this report's stable audit keys, not IDs supplied by the reference author.

External locators in the catalogue use these prefixes, always relative to the pinned reference repository:

- `P/`: `DotnetSecurityFailures/Components/Pages/`
- `C/`: `DotnetSecurityFailures/Controllers/`
- `S/`: `DotnetSecurityFailures/Services/`
- `A/`: `DotnetSecurityFailures/AttackerSite/`
- `W/`: `DotnetSecurityFailures/wwwroot/`
- `F/`: `DotnetSecurityFailures/`

Evidence labels: `S` means executable code inspected statically; `I` illustration; `M` simulation; `H` hidden/unused; `G` supplementary missing guard; `D` duplicate occurrence; `Q` a claim not established or technically misleading. None means exploitation was observed.

### Why the reference is a checklist, not a security oracle

The reference contains useful failure classes but must not supply prevention advice without independent verification:

1. Its CRLF example renders apparent response headers in a text body; it does not establish actual HTTP splitting or an open redirect.
2. Its certificate page simulates TLS results. Its YAML example performs a selected demonstration side effect without an operative YAML parser. Its JSON process-launch side effect is not evidence that a deserialization gadget executed.
3. Binary-deserialization switches do not prove the unsafe API works on the selected .NET 10 runtime. Template fixtures do not match the configured engine's syntax.
4. LDAP is a character-driven simulation, not a live directory experiment. Hidden NoSQL examples are registered but have no identified exposed caller.
5. Stored XSS is component-instance persistence, not database-wide persistence. Dynamically inserted script elements do not always execute; event handlers and other active contexts have different behavior.
6. CSS demonstrations are principally HTML attribute breakouts. Ordinary framework attribute binding is not shown to be inherently unsafe.
7. JSON document injection does not itself authorize a user; duplicate-property behavior and downstream authority consumption matter. One fixture's label misstates the added property.
8. CORS and CSRF samples do not establish an authenticated victim session. Some deletion, transfer, password-reset and registration actions merely report success.
9. Directory enumeration does not prove hidden environment files are served. The supposedly internal admin sample also has a public static alias.
10. Outbound responses are fully buffered before display truncation. The truncated preview does not demonstrate full secret extraction.
11. The archive sample has one traversal member and differs from the page's description. It was not extracted.
12. Time-seeded token reproduction does not prove that every modern random generator or UUID is predictable. A UUIDv7 aggregate ID is not intended to be a secret capability.
13. Fast password hashing and collision resistance are separate issues. A salt alone does not supply a password work factor.
14. Several purported safe alternatives still invoke a shell, build code strings, accept unsigned session authority, or assume structured logs automatically escape every formatter.
15. Registry source associations and a profile fixture label are inaccurate. Actual source locators take precedence.
16. Reference dependency vulnerabilities, browser runtime, filesystem permissions and network reachability were not tested. No package is declared vulnerable merely because it is old.

The comparison below evaluates the independently defined security failure, including when the external example is only illustrative.

### All 53 documented families

Every page family below has a corresponding individual-record comparison. Paths are reference provenance, not Event source or recommended implementation patterns.

| Family | Reference page under `P/` | Individual records | Event analysis |
| --- | --- | --- | --- |
| Exposed integration keys | `ApiKeysExposure.razor` | REF-112-117 | A14; IVSD-F015/016 distinguish guarded disclosure from unsafe custody. |
| Arbitrary file writing | `ArbitraryFileWrite.razor` | REF-182-184 | A07; generated keys/lexical containment, hostile filesystem limits. |
| Basic SSRF | `BasicSsrf.razor` | REF-188-190,194 | A08; three concrete egress findings and response budgets. |
| JWT verification | `BrokenJwt.razor` | REF-084-087 | A09; signatures versus context, IVSD-F006/008. |
| Certificate validation | `CertificateValidation.razor` | REF-067,163-165 | A06/A08/A15; simulated reference, scoped development exceptions. |
| C# script injection | `CodeInjectionCSharpScript.razor` | REF-008 | A02; no matching scoped evaluator. |
| Shell injection | `CommandInjection.razor` | REF-004-007,202 | A02; actual shell semantics, no matching API sink. |
| CORS | `CorsMisconfiguration.razor` | REF-097-103 | A12; explicit origins and actual credential boundary. |
| CRLF injection | `CrlfInjection.razor` | REF-036-038 | A05; header-looking text is not a wire exploit. |
| CSRF | `Csrf.razor` | REF-093-096,109,208 | A12/A13; cookie gate versus explicit machine credentials. |
| Sensitive URL data | `DataInUrls.razor` | REF-092,122-124 | A10/A14; reset POST and fragment proofs, URL logging gaps. |
| Directory listing | `DirectoryListing.razor` | REF-137-144 | A18; no storage/log directory-browser mount found. |
| DOM/interop XSS | `DomBasedXss.razor` | REF-048-051 | A06; client rendering outside API-only claim. |
| ECB encryption | `EcbMode.razor` | REF-159-160 | A15; AEAD/context binding and key custody. |
| Excessive data exposure | `ExcessiveDataExposure.razor` | REF-134-135 | A11/A14; explicit DTOs, residual complete-projection audit gap. |
| Expression injection | `ExpressionInjection.razor` | REF-009-011 | A02; no dynamic evaluator, analogous regex risk kept separate. |
| Hard-coded credentials | `HardcodedCredentials.razor` | REF-068,118-121 | A09/A14/A15; external authority, concrete persistence exceptions. |
| IDOR | `Idor.razor` | REF-073-074,076,205 | A11; current-user/resource checks, not identifier entropy. |
| Unsafe binary serialization | `InsecureDeserialization.razor` | REF-166-168,179 | A16/A09; declared contracts and authenticated session authority. |
| Insufficient/deprecated cipher strength | `InsufficientKeyLength.razor` | REF-161-162 | A15; no matching obsolete cipher in scoped paths. |
| Integer overflow | `IntegerOverflow.razor` | REF-106-108 | A13; money bounds and checked integer paths require distinct treatment. |
| JSON runtime-type deserialization | `JsonDeserialization.razor` | REF-169-170 | A16/A02; no matching payload-selected CLR type/process sink. |
| JSON construction | `JsonInjection.razor` | REF-032-035 | A04/A11; encoding and authority are separate. |
| LDAP injection | `LdapInjection.razor` | REF-016-020 | A04; simulation, no scoped LDAP sink. |
| Log forgery | `LogInjection.razor` | REF-039-042 | A05; actual formatter/exporter evidence still needed. |
| Mass assignment | `MassAssignment.razor` | REF-077-080,136,206-207 | A11; explicit profile groups and destructive/default semantics. |
| Missing authorization | `MissingAuthorization.razor` | REF-070-072 | A11; server-side action/resource authority. |
| Missing attempt limits | `MissingRateLimiting.razor` | REF-069,110-111 | A09/A10/A17; lockout, admission, loopback and replicas. |
| Negative quantities | `NegativeQuantities.razor` | REF-104-105 | A13; positive domain values and actual fulfillment path. |
| Missing password salts | `NoSaltHashing.razor` | REF-154 | A10/A15; effective hasher parameters unverified. |
| Recovery host poisoning | `PasswordResetPoisoning.razor` | REF-088-091,125-126 | A10; configured HTTPS, bound one-use proof, conditional error oracle. |
| Path traversal | `PathTraversal.razor` | REF-180-181 | A07; lexical root boundary versus symlinks. |
| Posted privilege escalation | `PrivilegeEscalation.razor` | REF-075,203-204 | A11; controlled enrollment and server-derived facts. |
| Reflected XSS | `ReflectedXss.razor` | REF-043-045 | A06; downstream rendering remains Unverified. |
| Sensitive logs | `SensitiveDataLogs.razor` | REF-127-129 | A05/A14; request-log safeguards do not certify every exception. |
| SQL injection | `SqlInjection.razor` | REF-001-003 | A01; actual parameterization and raw identifiers. |
| SSRF from uploaded SVG | `SsrfFileUpload.razor` | REF-191-193 | A07/A08; no scoped server-side reference fetcher. |
| Stored XSS | `StoredXss.razor` | REF-046-047 | A06; instance-persistent reference, client audit outside scope. |
| Server template injection | `TemplateInjection.razor` | REF-012-015 | A02/A05; evaluator search and rendering coverage limits. |
| Verbose errors | `VerboseErrors.razor` | REF-130-133 | A05/A14; generic HTTP errors versus raw operational exceptions. |
| Weak password hashing | `WeakHashing.razor` | REF-150-153 | A10/A15; salted work factor requires real evidence. |
| Weak password policy | `WeakPassword.razor` | REF-081-083 | A10; exact short examples blocked, policy gap IVSD-F005. |
| Weak randomness | `WeakRandom.razor` | REF-155-158 | A09/A15; CSPRNG opaque secrets versus IDs/allegations. |
| XML entity bomb | `XmlBomb.razor` | REF-173 | A16/A07; OOXML entity/character/depth budgets. |
| XML construction | `XmlInjection.razor` | REF-028-031 | A04; writer-based output, no posted XML privilege workflow. |
| XPath injection | `XpathInjection.razor` | REF-021-027 | A04/A05; no scoped XPath evaluation. |
| Attribute/protocol XSS | `XssAttributes.razor` | REF-052-054 | A06/A07; exact navigation checks, other clients unverified. |
| Style/markup XSS | `XssCss.razor` | REF-055-056 | A06; attribute breakout, not a CSS execution engine. |
| Uploaded active content | `XssFileUpload.razor` | REF-064-066,195 | A07; specialized versus generic storage and final policy. |
| Inline SVG XSS | `XssSvg.razor` | REF-057-063 | A06/A07; inline browser contexts outside scope. |
| XXE | `XxeInjection.razor` | REF-145,174-178 | A16; external resolution disabled at the inspected document parser. |
| YAML unsafe type/side effects | `YamlDeserialization.razor` | REF-171-172 | A16; simulation versus local package trust. |
| Archive traversal | `ZipSlip.razor` | REF-185-187 | A07; no extraction, container/expansion budgets. |

Hidden NoSQL records REF-196-201, duplicate/missing-guard records REF-202-208 and host-port REF-209 are intentionally included beyond the 53-page checklist. Unrelated simple controllers/comparison helpers do not disappear from inventory merely because the README lacks a separate title.

## Findings

### IVSD-F001: Security integration change selection misses current API trust boundaries

- Lifecycle: open.
- Severity / claim type: Medium; confirmed verification-routing defect, implementation traceability, not a demonstrated API exploit.
- I-VSD result: Concern.
- Principles / domains: Amanah, Sidq and Ihsan; Technical, Governance and Evaluation.
- Stakeholders: adopters, attendees, organizers, maintainers and release approvers.
- Provider-controlled decision: which code changes receive the container-backed security integration lane before release.
- Evidence: `.github/workflows/security-tests.yml:79-90` selects legacy path families but omits API `Hosting/`, API `Authentication/`, Standalone and `Event.Web.BffHosting`. A manual evaluation of the actual case predicate classified `ApiHostApplicationExtensions.cs`, `DynamicJwtBearerPostConfigureOptions.cs` and `CombinedApiBridgeMiddleware.cs` as omitted, while correctly selecting `ApiTenantResolutionMiddleware.cs`. The complete shared BFF directory is also absent from the selector; the real request-policy implementation is in `EventBffRequestEnricher.cs`. Scheduled/manual runs select the lane independently, so the omission is not a claim that security tests never execute.
- Failure condition: a PR confined to one of the omitted security-owning files can receive the workflow's intentional no-op pass instead of executing this lane. Other workflows may exercise related tests; their existence does not repair this selector's stated contract.
- Mitigation: **IVSD-M001** - Derive the path gate from present API security ownership, include both hosts and shared ingress services, and exercise the machine-consumed selection predicate against selected and unselected path fixtures. Retain a clearly labelled distinction between executed and no-op checks.
- Owner / next validation: release engineering and API security; demonstrate actual execution for an authentication-only and combined-host-only change.
- Rejected alternative: relying on a future nightly run to supply evidence that a specific merge was safe.
- Escalation boundary: security/release approvers decide whether missing revision-bound evidence blocks a candidate; this report does not approve release.

### IVSD-F002: RLS and middleware documentation overstate or misdescribe current protection

- Lifecycle: open.
- Severity / claim type: Medium; confirmed documentation contradiction and assurance risk, implementation traceability.
- I-VSD result: Concern.
- Principles / domains: Sidq, Amanah and Promise-Keeping; Technical, Operational and Governance.
- Stakeholders: operators, developers, tenant administrators and people whose private data relies on isolation.
- Provider-controlled decision: whether operators and reviewers are told which isolation and replay controls are actually active.
- Evidence: `docs/internal/QUICK_REFERENCE.md:24` and its tenancy section describe forced PostgreSQL RLS as current protection. `docs/internal/SECURITY-MODEL.md:1122-1132` describes a prototype, default-disabled session registration and no production table-policy migration. However, `ExploreDatabaseMigrator.cs:82-88` **does install model-derived policies in its normal PostgreSQL provider-adjustment step**, while `PersistenceServicesRegistration.cs:86-88` conditionally registers session binding. The statements therefore cannot all describe the current runtime contract. This is not resolved by the absence of a generated migration. `ApiHostApplicationExtensions.cs:85-112` also puts authorization/lifecycle checks before idempotency, whereas the old `docs/internal/API.md:244-271` list gives a different order and old CQS terminology.
- Failure condition: a maintainer may approve a raw-SQL bypass assuming database row policies protect it, or reason about replay using the wrong middleware order.
- Mitigation: **IVSD-M002** - Reconcile authoritative security docs with the actual migrator/session contract in IVSD-F024; explicitly separate EF filtering, database constraints and effective RLS. Update pipeline and native CQS descriptions. Require real role/policy/session evidence per deployment rather than a documentation checkmark.
- Owner / next validation: persistence, API and documentation owners; review all public/internal RLS assertions and the generated migration/runtime configuration before changing this disposition.
- Rejected alternative: treating the strongest statement in any document as evidence of implemented defense.
- Escalation boundary: actual isolation failures require security review; absence of optional RLS alone is not proof of a tenant escape or an automatic mandate to add it.

### IVSD-F003: Launch security assurance is not established by this source assessment

- Lifecycle: open.
- Severity / claim type: High release-evidence priority; missing operational validation, not a confirmed exploit.
- I-VSD result: Not reviewed for deployed protection.
- Principles / domains: Amanah, Sidq, Non-Harm, Rights of People and Ihsan; Strategic, Operational, Governance and Evaluation.
- Stakeholders: everyone relying on the Official Instance, self-hosting operators and incident responders.
- Provider-controlled decision: whether release approval, public security claims and incident responsibility are supported by actual evidence.
- Evidence: the reviewed source and existing tests provide implementation traces; no revision-bound passing security suite, deployed configuration packet, penetration-test result, restore rehearsal, MFA/recovery proof, egress policy or incident acknowledgment was supplied. `SECURITY.md:104-121` has an undefined acknowledgment window. The launch consultation already distinguishes deployment intent from demonstrated operation.
- Failure condition: treating this catalogue's coverage, generated OpenAPI, or an unrun test as a launch sign-off conceals real operational unknowns.
- Mitigation: **IVSD-M003** - Assemble one candidate-revision evidence packet: exact software/container identities, executed security tests, selected topology/provider/schemes, negative authorization checks, configured identity realm, trusted proxies, TLS and egress rules, key recovery, log redaction, realistic resource budgets and reachable incident ownership. Publish achievable response targets rather than a fictional guarantee.
- Owner / next validation: Amir/operator, security reviewer and release engineering; record named owners and actual evidence in this file.
- Rejected alternative: declaring "protected against all 209 vulnerabilities" because no exploit was run.
- Escalation boundary: operational release approval belongs to the operator and qualified security reviewers. Scholarly or legal review is separate and cannot substitute for technical verification.

### IVSD-F004: Local sign-in exposes the locked-versus-missing account distinction

- Lifecycle: open.
- Severity / claim type: Medium; static implementation defect, not an executed account-enumeration exploit.
- I-VSD result: Fail for uniform public credential failure.
- Principles / domains: Rights of People, Non-Harm, Amanah and Justice; Technical and Design.
- Stakeholders: Local account holders, administrators and recovery/support staff.
- Provider-controlled decision: public response content before the supplied identifier has been authenticated.
- Evidence: `src/Explore.Persistence/Identity/LocalIdentityAuthService.cs:45-74` returns `InvalidCredentials` for a missing user, but `AccountLocked` for an existing locked user and after the failed-attempt threshold. `src/Explore.API/ExceptionHandling/LocalAuthenticationResultMapper.cs:21-30,67-78` publishes different detail and failure codes. Default lockout is five failures for 15 minutes (`LocalIdentityOptions.cs:13-15`); the default HTTP write allowance is 30/minute.
- Prerequisites and impact: Local must be the active provider and the attacker must reach the login route with valid-shaped input. Existing and missing identifiers can follow different public response transitions; a known account can be temporarily locked. This is account discovery/targeted denial, not proof of password bypass. An already locked account is distinguishable immediately.
- Existing protection: missing users receive dummy password verification; native failed-access counters enforce throttling. Neither hides the distinct public result.
- Mitigation: **IVSD-M004** - Keep account-bound attempt controls internally but normalize unauthenticated missing/incorrect/locked outcomes to one public envelope. Give legitimate lockout/recovery guidance through an independently verified channel. Evaluate shared-network fairness and targeted-lockout recovery.
- Owner / next validation: identity/API owners; interleave existing/missing identifier attempts and compare status, ProblemDetails fields/codes and bounded timing distributions before/after lockout. `tests/Event.Persistence.IntegrationTests/Identity/LocalIdentityAuthServiceTests.cs:54-76` currently asserts the service distinction; HTTP privacy is a separate required invariant.
- Escalation boundary: a Keycloak-only deployment does not establish Local exploitability there; software adopters using Local remain affected.

### IVSD-F005: Local password policy lacks the current single-factor length and blocklist baseline

- Lifecycle: open.
- Severity / claim type: Medium; confirmed policy gap against an explicitly adopted engineering benchmark.
- I-VSD result: Concern.
- Principles / domains: Amanah, Non-Harm and Ihsan; Technical, Design and Evaluation.
- Stakeholders: Local users and operators, particularly accounts with consequential authority.
- Provider-controlled decision: server policy shared by credential creation, change, recovery and mandatory first-use replacement.
- Evidence: `LocalIdentityOptions.cs:8-9` sets minimum 12 and maximum 128. `PersistenceServicesRegistration.cs:122-135` composes the Identity policy without common/compromised-password validation and disables arbitrary character-class rules. `LocalIdentityLifecycleStore.cs:346-359` and `LocalIdentityCredentialStateStore.cs:450-478` consume native validators. No Local MFA step is established; `docs/internal/AUTHENTICATION.md:604-612` describes MFA as deferred.
- Assessment: three-character reference passwords are rejected, so that specific example is Protected. The absence of a blocklist leaves sufficiently long common passwords acceptable. NIST SP 800-63B-4 uses 15 characters for single-factor passwords, permits at least eight only when part of MFA, recommends at least 64 maximum, requires a full-password blocklist and rejects arbitrary composition/periodic-change rules. Local's maximum and absence of composition rules are positive controls; its minimum/blocklist need improvement.
- Mitigation: **IVSD-M005** - Define the Local single-factor policy with minimum 15 and one server-owned full-password blocklist reused by every credential-mutating path. Keep passphrases/password-manager use practical. Measure and document the actual salted work-factor hasher separately.
- Owner / next validation: identity/security owners; reject 12-14 characters and a blocklisted long password on change/recovery/replacement; accept a long non-blocklisted passphrase without forced symbols.
- Escalation boundary: this does not certify NIST conformity, establish legal applicability, or describe the unreviewed Keycloak/PDS password policy.

### IVSD-F006: Keycloak treats authorized client as an alternative to API audience

- Lifecycle: open.
- Severity / claim type: Medium, potentially High depending on issuer grants; confirmed token-context hardening gap with unverified exploitability.
- I-VSD result: Concern.
- Principles / domains: Amanah, Non-Harm and Sidq; Technical and Governance.
- Stakeholders: API users, operators and resource owners.
- Provider-controlled decision: which correctly signed issuer tokens confer API resource-server authority.
- Evidence: `src/Explore.API/Extensions/AuthenticationExtensions.cs:102-120` accepts an allowed `aud`, or an allowed `azp` when audience acceptance fails. The default accepted set includes API and browser client identifiers. An accepted audience also returns before an independent client check. The auth skill's "both" claim is therefore inaccurate.
- Prerequisites and impact: a valid issuer-signed token with one accepted claim and an unintended other context is needed. A forged unsigned payload does not suffice. Realm clients, grant types, audience mappers and intended authorized-client restrictions determine whether practical token-purpose confusion exists.
- Mitigation: **IVSD-M006** - Define API audience and authorized-client requirements independently; require the intended API audience and, where client restrictions are required, separately enforce `azp` with documented missing-claim/type behavior. Do not blindly require `azp` for token types whose agreed contract does not use it.
- Owner / next validation: authentication/security and Keycloak operator; correctly sign all accepted/rejected `aud`/`azp` combinations, including missing/multiple audiences, and exercise the API through the actual realm contract.
- Escalation boundary: ordinary signature/issuer validation remains a real protection; this is not evidence that arbitrary tokens are accepted.

### IVSD-F007: Target-only recovery failures can violate uniform account-discovery responses

- Lifecycle: open.
- Severity / claim type: Low-to-Medium conditional concern; static failure-path gap, no normal-operation oracle demonstrated.
- I-VSD result: Concern.
- Principles / domains: Rights of People, Non-Harm and Sidq; Technical and Operational.
- Stakeholders: Local users and recipients of recovery messages.
- Provider-controlled decision: public response when an eligible account's durable email/ledger work fails.
- Evidence: `RequestLocalPasswordRecoveryCommandHandler.cs:22-30` calls lifecycle email only for an eligible target and has no exception mapping despite its comment promising hidden failures. `DefaultAccountAuthorityLifecycleEmailService.cs:28-66`, `LocalIdentityLifecycleEmailService.cs:27-35` and `LocalIdentityLifecycleStore.BeginAsync` can propagate exceptional target-only failures.
- Prerequisites and impact: identifier resolution succeeds, an eligible target exists, then a later target-specific operation fails while missing-target requests still return accepted. The response may reveal account eligibility during that failure. A global outage producing the same response for everyone is not this condition.
- Mitigation: **IVSD-M007** - Define expected bounded delivery/ledger failure outcomes and preserve the public accepted envelope without suppressing cancellation or indiscriminately swallowing infrastructure defects. Retain durable internal retry/diagnosis without recipient or token disclosure.
- Owner / next validation: Local lifecycle/email owners; inject a failure after target resolution and compare eligible/missing-target HTTP envelopes.
- Escalation boundary: recovery proof remains purpose-bound and transactional; this finding does not establish account takeover.

### IVSD-F008: Existing token tests do not isolate their advertised security properties

- Lifecycle: open.
- Severity / claim type: Medium; confirmed evidence-quality defect, not a new token-validation bypass.
- I-VSD result: Concern.
- Principles / domains: Sidq and Ihsan; Evaluation and Technical.
- Stakeholders: reviewers, operators and users depending on launch assurance.
- Provider-controlled decision: whether test results substantiate expiry/audience/signature claims.
- Evidence: `tests/Event.API.IntegrationTests/Features/SecurityIntegrationTests.cs:187-300` has a tampered-signature branch that silently performs no assertion if token shape is unexpected; its expiry test changes payload without resigning, so signature failure can explain rejection; its named missing-audience test uses a valid audience-mapped token and expects authentication success. Local scheme-isolation tests deliberately disable lifetime validation for their narrower purpose.
- Mitigation: **IVSD-M008** - Use genuinely issuer-signed expired and wrong-context tokens; make unexpected fixture token shapes fail; preserve narrow isolation-test intent and add separate authenticity/lifetime/context tests through the real handler. Assert forbidden resource/state changes, not just one response code.
- Owner / next validation: API identity test owners; execute the corrected scenarios against selected real schemes and retain signing/issuer fixture provenance without committed secrets.
- Escalation boundary: an unrun or mislabeled test cannot close REF-084/085/087; production validation is assessed independently.

### IVSD-F009: Local webhook DNS validation is not bound to the connection

- Lifecycle: open.
- Severity / claim type: High, conditional privileged SSRF; static implementation defect.
- I-VSD result: Fail for the restrictive local-webhook destination guarantee.
- Principles / domains: Amanah, Non-Harm and Rights of People; Technical and Operational.
- Stakeholders: operators, tenant administrators, internal-service owners and people whose records are reachable on the internal network.
- Provider-controlled decision: whether endpoint configuration can cause delivery outside the approved network boundary.
- Evidence: `WebhookEndpointSafetyPolicy.cs:61-81` resolves/checks DNS, and `WebhookDeliveryDrainService.cs:362,407` validates then later sends through a separate client. `InfrastructureServicesRegistration.cs:441-454` disables redirects/cookies but supplies no vetted-address connector. `WebhookRegistrationProviderSubmissionSink.cs:40` and its named-client registration at `:333-343` have the same validation/connection separation.
- Prerequisites: endpoint-configuration authority, controllable DNS, a new applicable local-delivery connection and internal network reachability. Validation can see permitted addresses while connection resolution sees private/loopback/metadata addresses. This is not established anonymous SSRF or a claim about hosted Svix delivery.
- Existing controls: scheme/userinfo/direct-address/all-address checks, metadata denial, redirects disabled and payload/response limits remain meaningful.
- Mitigation: **IVSD-M009** - Validate at connection time and connect to the exact vetted address while preserving hostname-based TLS verification. Preserve webhook-specific private-CIDR and metadata semantics. Event already has a vetted-address pattern in `WebPushSafeConnector.cs:13-27` and its registration at `InfrastructureServicesRegistration.cs:806-814`; reuse principles, not an incompatible policy.
- Owner / next validation: webhook and infrastructure owners; deterministic resolver/connector evidence must prove a changed/mixed DNS answer causes zero forbidden handoff.
- Escalation boundary: network egress can reduce impact but its actual enforcement is unverified; do not close this finding merely because the first DNS lookup is safe.

### IVSD-F010: AI model discovery does not enforce restrictive hostname or redirect boundaries

- Lifecycle: open.
- Severity / claim type: High, conditional privileged SSRF; static implementation defect.
- I-VSD result: Fail for the advertised non-local endpoint restriction.
- Principles / domains: Amanah, Non-Harm and Avoiding Spying; Technical, Governance and Operational.
- Stakeholders: tenant/platform administrators, internal-service users and provider credential owners.
- Provider-controlled decision: accepting an administrator-supplied model-discovery endpoint and what network access it grants.
- Evidence: `AiAssistantController.cs:123-180` accepts `EndpointUrl` at `POST /api/ai/assistant/models` after tenant/platform-admin checks. `AiProviderSettingsValidator.cs:166-187,224-234` checks literals/localhost but does not resolve other hosts. `OpenAiCompatibleChatProvider.cs:88-99` sends discovery; named clients at `InfrastructureServicesRegistration.cs:691-709` have no redirect prohibition or vetted-address connector.
- Prerequisites: authenticated model-discovery administration, controllable hostname/redirect peer and internal reachability. The provider-specific operation path is appended, so arbitrary-path fetching is not established; an internal request can still occur even when its response fails the model parser.
- Mitigation: **IVSD-M010** - Disable unvalidated redirects, enforce actual connection destinations and distinguish explicitly approved local AI origins from arbitrary tenant-supplied endpoints. Apply equivalent policy to saved/runtime provider configuration, not only startup validation.
- Owner / next validation: AI infrastructure and API security; controlled redirect and DNS observations must establish zero private/metadata request when local endpoints are disabled.
- Escalation boundary: intentional `AllowLocalProviderEndpoints` is a supported operator choice, not intrinsically a vulnerability. The defect is bypassing the restrictive choice through a hostname.

### IVSD-F011: Tenant BYO Cerbos administration has the same hostname/redirect gap

- Lifecycle: open.
- Severity / claim type: High where BYO publication is enabled; static conditional SSRF defect.
- I-VSD result: Fail for restricted BYO private-destination protection.
- Principles / domains: Amanah, Non-Harm and Justice; Technical and Governance.
- Stakeholders: tenants, operators, policy administrators and internal services.
- Provider-controlled decision: whether tenant policy-provider publication may reach operator-private infrastructure.
- Evidence: `CerbosAdminEndpointValidator.cs:20-52,82-88` checks scheme, HTTPS, userinfo/query/fragment and literal/local addresses but not DNS. `CerbosPolicyPackageService.cs:538,750-765` consumes the validator and named client. `InfrastructureServicesRegistration.cs:618-622` gives `CerbosAdminClient` a ten-second timeout without redirect/connection-destination control.
- Prerequisites: BYO configuration/publication authority, applicable credentials/feature and network reachability. A public-looking hostname can resolve privately or redirect; this is not an ordinary anonymous API request.
- Existing tests: `CerbosPolicyPackageServiceTests.cs:405-437` checks plain HTTP/localhost/loopback/userinfo through a recording handler; it does not establish actual DNS or redirect safety.
- Mitigation: **IVSD-M011** - Keep trusted instance-local Cerbos distinct from restricted tenant BYO, disable redirects or reauthorize every hop, and enforce vetted-address connections. Explicit private BYO exceptions need narrow authority and network scope.
- Owner / next validation: authorization infrastructure/security; negative DNS/mixed-address/redirect tests must preserve real publishing authorization and prove no forbidden request.
- Escalation boundary: do not disable authorization or broaden fallback access to work around provider network failures.

### IVSD-F012: Custom-property regular expressions can monopolize synchronous request execution

- Lifecycle: open.
- Severity / claim type: Medium; static resource-exhaustion defect.
- I-VSD result: Fail for bounded evaluation.
- Principles / domains: Non-Harm, Ihsan and Justice; Technical, Operational and Design.
- Stakeholders: organizers submitting properties, other users sharing capacity and operators.
- Provider-controlled decision: execution budget for configured patterns and submitted values.
- Evidence: `CustomPropertyRuntimeValueValidator.cs:293` uses `Regex.IsMatch` without a match deadline. Length errors are accumulated but matching still runs. Definition validation bounds string length, not complexity (`CreateEventCustomPropertyDefinitionDtoValidator.cs:34-41`). Event/session single/multiple value commands use this validator (`SetEventCustomPropertyValueCommandHandler.cs:46-54` and sibling handlers).
- Prerequisites: an expensive/malformed definition pattern and authority to submit affected values. Short patterns can backtrack expensively; request cancellation does not preempt this synchronous call. Invalid pattern exceptions are not locally converted to a stable failure.
- Mitigation: **IVSD-M012** - Reject size violations before matching; use bounded matching and stable invalid-pattern/timeout codes; validate syntax during definition authoring. `RegistrationAnswerNormalizer.cs:89-104` already uses a 100 ms deadline and fixed failures. A non-backtracking engine requires a deliberate supported-pattern contract.
- Owner / next validation: custom-property/Application owners; deterministic malformed/timeout tests through every affected event/session command, without sleep-based timing assertions.
- Escalation boundary: this is regex denial of service, not proof of general-purpose code injection.

### IVSD-F013: Global response headers remove the intended file sandbox

- Lifecycle: open.
- Severity / claim type: Low; confirmed defense-in-depth/header-composition defect, no demonstrated XSS.
- I-VSD result: Concern.
- Principles / domains: Non-Harm and Ihsan; Technical and Evaluation.
- Stakeholders: recipients of stored files and operators.
- Provider-controlled decision: final browser policy on API-delivered content.
- Evidence: `StorageObjectController.cs:367-371` and `EventResourceFileResult.cs:33-35` set `default-src 'none'; sandbox`. `SecurityHeadersMiddleware.cs:18-38` unconditionally assigns a different CSP in `OnStarting`, removing `sandbox` from that response path.
- Impact qualification: attachment disposition, `nosniff` and `default-src 'none'` remain controls. This finding does not establish inline executable HTML/SVG or a browser exploit.
- Mitigation: **IVSD-M013** - Preserve or centrally compose the endpoint's restrictive content policy so sandbox survives. Verify the final HTTP header rather than the controller's intermediate assignment.
- Owner / next validation: API delivery/security owners; actual generic-document and event-resource downloads must retain attachment, `nosniff` and the intended sandbox.
- Escalation boundary: any separately discovered inline active-content path would require its own higher-severity finding.

### IVSD-F014: Inspected AI provider responses have no explicit byte budget

- Lifecycle: open.
- Severity / claim type: Medium; conditional provider-induced resource exhaustion, static traceability.
- I-VSD result: Concern.
- Principles / domains: Amanah, Non-Harm and Ihsan; Technical and Operational.
- Stakeholders: API users sharing memory/CPU, operators paying provider/network costs.
- Provider-controlled decision: maximum untrusted provider response allocated and parsed.
- Evidence: `OpenAiCompatibleChatProvider.cs:99,191-192` reads the model response as a string and parses chat from an unbounded stream; corresponding Responses API paths are `OpenAiResponsesChatProvider.cs:94,186-187`.
- Prerequisites: malicious/compromised or simply oversized configured provider output. A deadline and requested output-token count do not bound bytes from an uncooperative server; clipping the displayed result after buffering is insufficient.
- Mitigation: **IVSD-M014** - Enforce operation-specific response byte/depth/item budgets before allocation/parsing, including unknown/chunked lengths and large strings. Preserve fixed provider-failure codes without reflecting response content.
- Owner / next validation: AI infrastructure; controlled known-length/chunked responses must stop at the allowed budget plus at most an overflow sentinel.
- Escalation boundary: Anthropic/Refit/Azure SDK buffering internals were not fully inspected and must not inherit an unwarranted pass or vulnerability claim.

### IVSD-F015: Google OAuth secrets are copied into ordinary database settings

- Lifecycle: open.
- Severity / claim type: High credential-custody defect; static implementation traceability.
- I-VSD result: Fail for the declared exclusive external-secret-authority model.
- Principles / domains: Amanah, Non-Harm and Promise-Keeping; Technical, Governance and Operational.
- Stakeholders: identity-provider administrators, adopters and users relying on credential confidentiality.
- Provider-controlled decision: where a submitted OAuth client secret is retained and recoverable.
- Evidence: `UpdateAuthProviderConfigurationCommandHandler.cs:102` passes the value; `AuthProviderConfigurationService.cs:278-300` serializes it into `SystemSetting.Value`; `SystemSettingRepository.cs:175` persists the value directly; `SystemSettingConfiguration.cs:28` has no encryption conversion. The service reads the value back at `:332-342`. The "sensitive" setting flag is classification, not encryption or external custody.
- Prerequisites and impact: an authorized administrator/setup workflow configures Google OAuth. Database/backup disclosure then exposes recoverable credential material, and the internal setup read intentionally resolves it. No anonymous configuration disclosure is established. Selecting Infisical/Environment/User Secrets does not remove this additional copy.
- Existing tests: `InstanceOnboardingControllerTests.cs:825` protects admin/public redaction but intentionally expects the setup-secret internal read to contain the configured secret. That is not evidence of exclusive external authority.
- Mitigation: **IVSD-M015** - Replace persisted value with an approved external binding and resolve through the selected authority. Keep nonsecret ownership/readiness metadata in ordinary settings. When implementation is authorized, assess configured-secret rotation/removal and backup custody rather than preserving the unsafe field for compatibility.
- Owner / next validation: identity configuration/secrets/persistence owners; compare database/export/internal-read artifacts under each supported authority without storing test credentials in source.
- Escalation boundary: exact administrative API permissions remain real; authorized secret retrieval and recoverable database custody are separate threats.

### IVSD-F016: One-time API-key output can be retained and replayed by generic idempotency

- Lifecycle: open.
- Severity / claim type: High credential-custody/one-time-disclosure defect; static implementation traceability.
- I-VSD result: Fail for shown-once authority.
- Principles / domains: Amanah, Promise-Keeping, Non-Harm and Sidq; Technical, Design and Operational.
- Stakeholders: external-key owners, operators and downstream resources authorized by those keys.
- Provider-controlled decision: whether the raw key survives successful issuance outside its digest entity.
- Evidence: `ExternalApiKeyController.cs:77-105` has HTTP no-store but no idempotency-storage suppression/protected replay metadata. `CreateExternalApiKeyCommandHandler.cs:90,128` stores a hash and returns the raw key. `IdempotencyMiddleware.cs:72,279,285-317,544` captures/stores/replays successful JSON unchanged when suppression/protection metadata is absent. `Cache-Control: no-store` does not stop this application-database write.
- Prerequisites and impact: authorized key creation with `Idempotency-Key` and a successful within-limit response. `IdempotencyRecord.ResponseBody` gains a recoverable raw credential and matching replay can show it again. The digest-only API-key entity remains a useful control but does not protect this second store. **No cross-user replay is established**: the request identity at `:419` binds principal, method, target, type and body.
- Existing tests: controller metadata checks HTTP no-store; key-entity tests issue without an idempotency header; synthetic middleware tests prove suppression only when explicitly attached. None closes the real issuance path.
- Mitigation: **IVSD-M016** - Suppress generic response storage on issuance; if idempotent creation is required, use domain-owned issuance semantics that do not retain recoverable raw keys. Review/expire existing response copies and require appropriate credential rotation in affected environments.
- Owner / next validation: API-key/idempotency owners; issue through the actual endpoint with a key, inspect persistence and replay, and prove no raw secret survives in any response record.
- Escalation boundary: HTTP caching headers and principal-scoped replay do not repair recoverable-secret persistence.

### IVSD-F017: Response-size thresholds do not bound middleware buffering

- Lifecycle: open.
- Severity / claim type: Medium resource-exhaustion concern; confirmed buffering mechanism, no measured OOM.
- I-VSD result: Concern.
- Principles / domains: Non-Harm and Ihsan; Technical and Operational.
- Stakeholders: users sharing host resources and operators.
- Provider-controlled decision: memory allocated before deciding whether to hash/cache/store a response.
- Evidence: `ETagMiddleware.cs:42-78` buffers the complete response before status/type/256 KiB checks. Non-JSON responses can therefore be captured before exclusion. `IdempotencyMiddleware.cs:285-317,544` captures and converts to a string before its 1 MiB storage decision.
- Prerequisites: a reachable eligible response is large; idempotency additionally needs an applicable keyed write. Not every response is caller-expandable, and private-no-store endpoints bypass ETag.
- Mitigation: **IVSD-M017** - Exempt known streaming/non-JSON responses before capture; use bounded tee/buffering that switches to direct forwarding after the threshold without losing bytes. Bound response-store conversion separately and define oversized-write replay behavior.
- Owner / next validation: middleware/API owners; exercise known/unknown-length and concurrent large responses and assert bounded buffering plus intact downstream bytes.
- Escalation boundary: recyclable streams improve allocation reuse; they do not impose a maximum retained size.

### IVSD-F018: Dedicated-host HSTS precedes effective forwarded protocol

- Lifecycle: open.
- Severity / claim type: Medium transport hardening defect, topology-dependent.
- I-VSD result: Concern.
- Principles / domains: Non-Harm and Ihsan; Technical and Operational.
- Stakeholders: browser users and TLS-terminating operators.
- Provider-controlled decision: ordering middleware that uses effective external scheme.
- Evidence: `ApiHostApplicationExtensions.cs:73,79` installs HSTS before forwarded headers. A trusted proxy's HTTP backend request initially appears non-HTTPS. Standalone applies forwarding first (`StandaloneHostApplicationExtensions.cs:32`), so this specific order defect is dedicated-host only.
- Prerequisites and impact: external TLS terminates at a trusted proxy forwarding effective HTTPS over HTTP; API HSTS can be omitted. HTTPS redirection is later and this finding does not establish a redirect loop.
- Mitigation: **IVSD-M018** - Apply trusted forwarding before scheme/host/IP-dependent middleware and verify both host topologies. Ingress-owned HSTS can mitigate exposure but needs retained evidence.
- Owner / next validation: hosting/operator owners; inspect final headers on actual proxied HTTPS and reject untrusted forwarded proto.
- Escalation boundary: this is not proof that the deployment uses plaintext transport.

### IVSD-F019: Normal authentication work precedes admission limits

- Lifecycle: open.
- Severity / claim type: Medium availability/cost concern.
- I-VSD result: Concern.
- Principles / domains: Non-Harm and Justice; Technical and Operational.
- Stakeholders: users sharing authentication/database capacity and operators.
- Provider-controlled decision: resources consumed before a request can be rejected with 429.
- Evidence: `ApiHostApplicationExtensions.cs:103,107,112` resolves tenant/authenticates before normal rate limiting. The ATProto transient branch deliberately has earlier guard/deadline/limit/body enforcement (`:95`).
- Prerequisites and impact: invalid/repeated requests trigger real identity/tenant/metadata work before rejection. Post-auth identity-based limits are valuable but are not a pre-auth capacity budget.
- Mitigation: **IVSD-M019** - Add conservative pre-auth admission/concurrency protection while preserving verified-identity policies after authentication. Bound lookup/provider work rather than moving every identity-dependent policy blindly.
- Owner / next validation: API/auth/operator owners; count actual blocked expensive operations under malformed/invalid credential pressure through real handlers.
- Escalation boundary: no authentication bypass is established; account-bound Local lockout is a separate protection.

### IVSD-F020: Loopback exemption and per-process limits need explicit deployment semantics

- Lifecycle: open.
- Severity / claim type: Medium, topology-dependent limit weakness.
- I-VSD result: Concern.
- Principles / domains: Justice, Amanah and Non-Harm; Technical, Operational and Governance.
- Stakeholders: public users, shared-network users and operators of multiple replicas.
- Provider-controlled decision: which effective peers and replica scope receive an abuse budget.
- Evidence: `RateLimitingExtensions.cs:600` returns `GetNoLimiter` for loopback without a production restriction. Buckets in `RateLimitingExtensions.cs:268` and `AnonymousRegistrationRateLimiting.cs:30` are process-local; Redis registration does not make them distributed.
- Prerequisites and impact: public traffic forwarded by a loopback proxy without an effective original-client address can bypass the global bucket; named policies still apply. Multiple independently reachable replicas multiply process budgets. Neither is a bypass of every limiter or proof of current deployment exposure.
- Mitigation: **IVSD-M020** - Remove/constrain network-address exemptions, distinguish server-owned in-process transport explicitly, state per-replica versus fleet-wide limits and provide aggregate ingress/shared counters where multiplication is unacceptable. Avoid punishing all users behind one NAT without a proportional policy.
- Owner / next validation: hosting/rate-limit/operator owners; test missing/malformed forwarding and N-replica aggregate behavior with enabled policies.
- Escalation boundary: exact proxy/ingress configuration is required to judge launch exposure.

### IVSD-F021: Coop moderation responses are time-bounded but not byte-bounded

- Lifecycle: open.
- Severity / claim type: Medium provider-induced allocation concern.
- I-VSD result: Concern.
- Principles / domains: Amanah, Non-Harm and Ihsan; Technical and Operational.
- Stakeholders: moderation operators and users sharing API/worker capacity.
- Provider-controlled decision: response volume accepted from a configured moderation provider.
- Evidence: `CoopReviewQueueProvider.cs:45-67` uses a linked 1-300 second deadline and headers-first completion but reads the complete successful body as a string. `InfrastructureServicesRegistration.cs:389` gives the client no separate response-byte handler; useful field normalization happens after allocation/deserialization.
- Prerequisites: configured contacted provider returns oversized success quickly enough to fit the deadline.
- Mitigation: **IVSD-M021** - Keep the duration budget and add bounded byte/depth/item parsing before allocation, with fixed failures and no provider-body reflection.
- Owner / next validation: moderation infrastructure owners; known/chunked large responses must stop at a declared ceiling.
- Escalation boundary: this does not claim moderation decisions or callback authentication are bypassed.

### IVSD-F022: Host and operational surfaces depend on unverified ingress confinement

- Lifecycle: open.
- Severity / claim type: Medium conditional exposure / host-authority concern; not a demonstrated privileged-route bypass.
- I-VSD result: Concern.
- Principles / domains: Rights of People, Avoiding Spying, Amanah and Sidq; Technical and Operational.
- Stakeholders: tenants, people appearing in telemetry and operators.
- Provider-controlled decision: effective host authority and public reachability of management/metrics routes.
- Evidence: API/Standalone appsettings ship `AllowedHosts: "*"`, explicitly trust loopback forwarding and accept forwarded Host. `ForwardedHeadersTrustOptions` restricts proxies/networks but does not populate a forwarded-host allowlist. `ApiTenantResolutionMiddleware.cs:175` uses effective host for tenant domains; `EventTicketingController.cs:293` generates an absolute onboarding URL. `Explore.ServiceDefaults/Extensions.cs:190,216` and `ApiHostApplicationExtensions.cs:165` map health/metrics/readiness without endpoint authorization; `BusinessMetrics.cs:885` can label metrics with `tenant_id`. Scheduler status is separately authorized.
- Prerequisites: public ingress forwards operational routes or accepts attacker-selected Host/forwarding; backend listener bypass or trust mistakes may amplify exposure. Redacted health JSON is not raw exception disclosure.
- Mitigation: **IVSD-M022** - Validate security-relevant effective host/domain authority, restrict trusted forwarding and listener reachability, and confine health/metrics to a private/authenticated scrape surface. Support genuine tenant domains without making absolute security-sensitive links depend on unconstrained Host.
- Owner / next validation: hosting/operators; prove forbidden hosts, direct forwarding spoofing and public operational paths fail while valid tenant domains and authorized scrapers work.
- Escalation boundary: `"*"` alone is not a demonstrated tenant escape, and no host-port-selected hidden admin page equivalent was found.

### IVSD-F023: Shared Data Protection persistence is not database-compromise isolation

- Lifecycle: open.
- Severity / claim type: Medium defense-in-depth decision, not a demonstrated cryptographic failure.
- I-VSD result: Concern.
- Principles / domains: Amanah, Non-Harm and Promise-Keeping; Technical, Operational and Governance.
- Stakeholders: cookie/capability owners and backup operators.
- Provider-controlled decision: whether database disclosure also yields usable Data Protection key material.
- Evidence: `ApiHostServiceCollectionExtensions.cs:255` and `CombinedApiDataProtectionExtensions.cs:16` persist keys in the application database; no separately controlled wrapping is established in those compositions. `DataProtectionServiceCollectionExtensions.cs:16` explicitly documents that sharing the ring is not a database-compromise defense.
- Prerequisites/impact: database/keyring/backup disclosure. Actual key XML/environment wrapping and token purposes must be inspected before estimating forged-authority impact; this report does not assume every opaque capability uses Data Protection.
- Mitigation: **IVSD-M023** - Decide and document the database-compromise threat model; where separation is required, wrap persisted keys under independently controlled authority and rehearse overlap/recovery without invalidating legitimate sessions unpredictably. Protect backups regardless.
- Owner / next validation: secrets/hosting/operator owners; demonstrate actual key wrapping, purpose isolation, retired-key behavior and restore fencing.
- Escalation boundary: this is not a mandate to add an incompatible dependency or an assertion that current cipher integrity is broken.

### IVSD-F024: PostgreSQL policy installation and tenant-session activation are not one coherent contract

- Lifecycle: open.
- Severity / claim type: High deployment/isolation priority; static configuration inconsistency with runtime protection Unverified.
- I-VSD result: Concern; deployed policy/role behavior Not reviewed.
- Principles / domains: Amanah, Justice, Non-Harm and Sidq; Technical, Operational and Governance.
- Stakeholders: tenants, data subjects, background-worker owners and database operators.
- Provider-controlled decision: when RLS is installed, which connections bind tenant authority and which roles perform migration/global work.
- Evidence: `ExploreDatabaseMigrator.cs:68-88` migrates through a separate context, then applies PostgreSQL constraints/RLS through the runtime context without checking the interceptor opt-in. `PostgresTenantRowLevelSecurityModel.cs:114` generates ENABLE/FORCE plus USING/WITH CHECK. `PersistenceServicesRegistration.cs:86` gates session interception; `PostgresTenantSessionInterceptor.cs:14,42` binds the tenant through parameterized `set_config` on connection opening.
- Failure conditions: installed policies plus a normal runtime role without session binding can hide tenant rows/reject writes; a superuser/BYPASSRLS runtime role defeats the claimed RLS boundary; unbound global workers can miss/fail work even when EF filters are explicitly bypassed; skipped/failed provider adjustment leaves policies absent. Nullable/global rows and inherited-parent policies have deliberate exceptions, so missing context is not identical for every table.
- Additional privilege concern: provider adjustment uses the runtime context and needs table-altering authority there; separate migration credentials alone do not prove least-privilege startup.
- Mitigation: **IVSD-M024** - Define one jointly validated policy/session/role contract, appropriate DDL execution authority and narrowly scoped global-worker model. Fail clearly when the chosen contract is unsatisfied. Verify actual tables, policies, role attributes, bound sessions, connection pooling and workers rather than changing only documentation.
- Owner / next validation: persistence/hosting/database operator; real non-BYPASSRLS roles on application tables must prove cross-tenant read/write rejection, missing-tenant behavior, tenant change/pooling and global maintenance under selected permissions.
- Escalation boundary: PostgreSQL probe tests are not full deployed table-policy proof; SQLite/SQL Server/MySQL do not inherit PostgreSQL RLS. No running tenant escape or outage was executed.

### IVSD-F025: Filter bypass and generic entity CRUD rely on each caller's authority

- Lifecycle: open.
- Severity / claim type: Medium systemic hardening priority; conditional internal hazard, no public exploit established.
- I-VSD result: Concern.
- Principles / domains: Amanah, Justice and Ihsan; Technical and Governance.
- Stakeholders: tenant resource owners and maintainers.
- Provider-controlled decision: allowing global/tenant-bypassed lookups and tracked entity mutation without automatic ownership authorization.
- Evidence: `QueryFilterExtensions.cs:1` requires a reason but does not authorize it; `GenericRepository.cs:1` offers identifier CRUD and `FindAsync`; `ExploreDbContext.SaveChanges.cs:550` enforces specific relationships, not universal request authorization. Bypass callsites include API keys, membership/role queries, workers, bootstrap/seeding and development reset.
- Failure condition: a future/current unreviewed handler supplies an attacker-controlled identifier to a bypass predicate missing tenant/active state, or uses a tracked cross-tenant entity from the identity map without independent resource authorization. No such omission was established in the traced profile/payment flows.
- Mitigation: **IVSD-M025** - Require persisted resource/tenant predicates at each bypass and handler authority boundary; prefer tenant-only bypass when deleted rows are unnecessary. Test same-context tracked-entity access after a trusted global lookup, not only fresh contexts. Review new bypass callsites as security-sensitive.
- Owner / next validation: Application/persistence/security owners; complete a caller-owned bypass inventory and negative authorization tests for every API-reachable global lookup.
- Escalation boundary: a nonempty audit reason, entity repository return type or filter presence is not an authorization guarantee.

### IVSD-F026: Public identity and membership fields need a documented privacy contract

- Lifecycle: open.
- Severity / claim type: Medium privacy/design concern; unauthorized disclosure depends on intended policy and is not established.
- I-VSD result: Concern.
- Principles / domains: Rights of People, Avoiding Spying and Sidq; Design, Governance and Technical.
- Stakeholders: submitters, attendees, actors, organizations and associated non-users.
- Provider-controlled decision: publication of stable internal/public identities and relationship metadata.
- Evidence: `EventMapper.Projections.cs:25,315` populates public event identity references; `EventDto.cs:1` exposes that DTO surface. Public actors deliberately include federation/group/organization associations, while `GetUserOrganizationsRequestHandler` separately restricts personal memberships. Global public identity and tenant participation discoverability are different contracts.
- Failure condition: product promises keep submitter linkage/organization relationship private while an anonymous projection discloses it. UUIDs are not passwords but can still enable correlation.
- Mitigation: **IVSD-M026** - Define public fields and association policy per projection, separate public/authenticated detail shapes where necessary, and test forbidden serialized fields plus hidden/deleted parent behavior. Do not expose linkage merely because it exists on an entity.
- Owner / next validation: API/privacy/product owners; resolve intended public identity contract with stakeholder context and exercise anonymous actual JSON/count/cache variants.
- Escalation boundary: this report does not establish every named identity field is private or prescribe removal of intentional public federation identity.

### IVSD-F027: Generic HTTP receipts do not atomically commit every business operation

- Lifecycle: open.
- Severity / claim type: Medium-to-High by operation consequence; general reliability/replay hardening gap, no duplicate payment demonstrated.
- I-VSD result: Concern.
- Principles / domains: Amanah, Promise-Keeping and reducing excessive uncertainty; Technical and Operational.
- Stakeholders: purchasers, organizers, provider operators and maintainers.
- Provider-controlled decision: behavior after business commit but before idempotency response completion.
- Evidence: `IdempotencyMiddleware.cs:175` claims a request then invokes downstream logic and separately saves its response; `IdempotencyRepository.cs:1` owns the replay record. A handler's business transaction is not the middleware receipt transaction. Payment claim/dispatch flows have additional durable lineage, locks, effects and uniqueness.
- Failure condition: business mutation commits, response/store completion fails, then retry follows an incomplete receipt. Duplicate logical creation depends on each operation's own deduplication; a key header alone is not exactly-once state.
- Mitigation: **IVSD-M027** - Keep consequential effects behind application-owned durable operation identity/outbox/provider idempotency and define recovery of committed work with an incomplete receipt. Audit ordinary create operations that promise exactly-once logical behavior. Reauthorize sensitive replay where state/authority can change.
- Owner / next validation: affected application/API owners; signal-coordinated commit-before-receipt failure and retry must leave one logical effect, with explicit ambiguous outcome handling.
- Escalation boundary: no remote provider call is one atomic database transaction; money/riba/religious-legal judgments are not supplied by a concurrency test.

## Detailed API Control Analysis

Analysis keys are stable section identities used by the individual reference ledger. A shared section describes common controls without collapsing distinct reference records. Each row retains its own disposition and failure condition.

### A01: SQL structure, parameterized values and provider isolation

Inspected ordinary event queries use typed specifications/enumerated sort choices (`EventFilter.cs`, `EventSort.cs`, `EventRepository.cs:18`) rather than caller-authored SQL/predicates. SQL values and SQL identifiers are different boundaries: bound IDs/text protect values; model-owned delimited identifiers and validated deployment schema/prefix choices protect structure.

The following hand-authored construction families were traced. This is not statement-by-statement certification of generated migrations, EF-generated SQL, packaged schema scripts or third-party providers.

| Construction family under `src/Explore.Persistence/` unless stated | Concrete source | Boundary / qualification |
| --- | --- | --- |
| Entity row fences | `Database/ProviderPrimitives/RelationalEntityRowFence.cs:12` | Model table/key/tenant identifiers and bound key/tenant values; lock execution is provider-specific. |
| Named/advisory locks | `Database/ProviderPrimitives/RelationalNamedLock.cs:225` | Server-generated resource identity supplied as a value; PostgreSQL/MySQL/SQL Server lifetimes differ. |
| Projection locks | `Database/ProviderPrimitives/RelationalProjectionLock.cs:1` | Fixed/provider-selected structure and bound values. |
| Bootstrap state lock | `Database/ProviderPrimitives/RelationalInstanceBootstrapStateLock.cs:1` | Model identifiers and state-key parameters; administrative startup scope. |
| Skip-locked selection | `Database/ProviderPrimitives/RelationalSkipLockedQuery.cs:1` | Internal structure/model identifiers; caller tenant/global predicates remain essential. |
| Payment reconciliation claim | `Database/ProviderPrimitives/RegistrationPaymentAttemptRepository.Reconciliation.cs:155` | Bound time/batch/fence values; intentionally worker-scoped claim. |
| Fanout claims | `Database/ProviderPrimitives/NotificationFanoutRunRepository.cs:740` | Fixed queue/lease commands, not inserted request SQL. |
| Email suppression | `Database/ProviderPrimitives/NotificationFanoutEmailSuppressionRepository.cs:25` | Fixed structure with tenant/event/work values. |
| Email outbox/schedule/backlog/leases | `Database/ProviderPrimitives/EmailDispatchOutboxRepository.cs:26,299,446,1440,2490` | Distinct fixed SQL/CTEs with parameters; full scheduling/worker authority is not certified. |
| ATProto commit fence | `Database/ProviderPrimitives/AtprotoJetstreamCommitFence.cs:1` | Fixed provider transaction/commit logic. |
| ATProto cleanup | `Database/ProviderPrimitives/AtprotoTransientCleanupDelete.cs:1` | Metadata identifiers and bound cleanup criteria. |
| ATProto conditional consume | `Repositories/AtprotoTransientStoreRepository.cs:128` | Model-delimited DELETE; candidate/purpose/digest/tenant/expiry parameters and server-selected branch. |
| External erasure authority | `Privacy/ErasureAuthority/ProviderPrimitives/EfCorePrivacyErasureAuthorityRepository.cs:1` | Fixed authority/fencing operations; separate context/full erasure topology remains bounded. |
| Embedded authority setup | `Privacy/ErasureAuthority/ProviderPrimitives/EmbeddedPrivacyErasureAuthorityConnectionInterceptor.cs`; `EmbeddedPrivacyErasureAuthorityStorage.cs` | Fixed SQLite setup rather than request SQL. |
| Database clock | `Schema/ProviderPrimitives/RelationalDatabaseClock.cs` | Fixed provider clock query. |
| SQLite WAL | `Database/ProviderPrimitives/SqliteDatabaseInitializer.cs:10` | Constant startup PRAGMA. |
| Quartz startup | `src/Explore.API/Scheduling/QuartzSchemaInitializer.cs:70,165` | Provider script selection; substituted prefix constrained to ASCII letters/digits/underscore. |
| PostgreSQL constraints/indexes | `Schema/ProviderPrimitives/PostgresModelConstraintApplier.cs:1,144` | Model identifiers/literal quoting; DDL privileges/deployment unresolved. |
| PostgreSQL RLS | `Security/ProviderPrimitives/PostgresTenantRowLevelSecurityModel.cs:114` | Model policy DDL, no request-input injection; activation inconsistency F024. |
| Tenant session | `Security/ProviderPrimitives/PostgresTenantSessionInterceptor.cs:42` | Parameterized `set_config` on open; pooling/current context and role matter. |
| Schema/migration rewriting | `Schema/ProviderPrimitives/ConfigurableSchemaMigrationsSqlGenerators.cs:295`; `src/Explore.Secrets/Database/PrimaryDatabaseConfiguration.cs:627` | Generated commands/trusted schema, portable identifier allowlist. |
| Migrator setup | `Schema/ProviderPrimitives/ExploreDatabaseMigrator.cs:74` | Constant setup and generated migration execution; privileged internal scope. |
| Agent-browser reset | `Schema/ProviderPrimitives/AgentBrowserDatabaseReset.cs` | Model identifiers with guarded development reset; not an ordinary public query. |

No injected request text was established in these construction families. Raw SQL **does not automatically inherit EF filters**. A worker/global query needs explicit predicates/role authority, and forced RLS does not constrain superuser/BYPASSRLS roles. The real RLS installation/session inconsistency is F024 rather than a fictional absence of all policy code.

Normal named tenant/soft-delete filters are in `ExploreDbContext.QueryFilters.cs:16,270`; global users have separate soft-delete filtering (`:727`). `RegistrationOrderLineConfiguration.cs:28` uses tenant-aware order/ticket composite relationships. These protect normal filtered reads/persisted ownership where the deployed schema matches, not identity-map CRUD, all bypasses or all providers (F025).

PostgreSQL RLS, SQLite transaction/WAL, MySQL named locks and SQL Server lock behavior require separate execution evidence. Existing `TenantQueryFilterFailClosedTests.cs:14` checks selected PostgreSQL rows; `PostgresTenantSessionRlsPrototypeTests.cs` uses probe roles/tables, not the entire application model. A SQLite pass cannot establish PostgreSQL policy or MySQL locking.

### A02: Shell and general-purpose server code

The scoped sink search in API, Application, Infrastructure and Secrets found no matching user-input-to-shell/process, C# script, dynamic LINQ evaluator or arbitrary expression evaluator corresponding to REF-004-011/202. Those exact mechanisms are Not applicable to the searched API surface, not universally absent from every package, tool, provider or future feature. There is no reason to import the reference's purported safe shell validator: separating process arguments while still invoking a shell leaves an interpretation boundary.

Domain event/session/form templates do not imply executable source templates. No RazorLight/Scriban/CSharpScript execution sink was identified in the scoped layers. The complete SMTP/template rendering chain was not traced sufficiently to certify every rendering path; template records retain that uncertainty. Custom-property regex is a real bounded-evaluation concern under IVSD-F012, not an excuse to call it arbitrary C# execution.

Closure for a future evaluator must define a closed grammar/registry, resource budget, allowed facts and independently enforced visibility. A sandbox label or process timeout alone cannot replace input/authority isolation.

### A04: LDAP, XPath, XML and JSON construction

No LDAP filter or XPath evaluation sink corresponding to the examples was found in the scoped backend search. Keycloak management uses HTTP APIs rather than a caller-authored LDAP query; a Keycloak realm may independently integrate LDAP, which is operator/provider configuration not verified here.

Sitemap construction uses `XDocument`/`XElement`, so ordinary text does not become new XML nodes. That protection concerns document structure, not correctness of the public host/URL authority. Public DTO minimization is assessed separately rather than labelled safe merely because it is serialized as XML.

Configuration import and inspected webhook payloads use typed serializers/JSON writers, not concatenation of profile values. `ConfigurationPortabilityJsonCodec.cs:64-96,111-132` enforces strict bounded parsing, declared metadata, recursive duplicate/forbidden-member rejection and depth/byte limits. Contract records reject unmapped members; `ConfigurationManifestV1Alpha2.cs:29-31` limits artifacts to 4 MiB, depth 32 and bounded tenant count. `ConfigurationImportArtifactParser.cs:27-65` maps fixed parser failures without reflecting the invalid payload.

These are concrete protections for that codec. General API duplicate-property behavior and every converter were not exhaustively audited. A safe JSON representation does not grant permission to set role, balance, tenant or other authority properties, and a strict import can still configure an unsafe downstream network endpoint.

### A06: Browser active content and API rendering contexts

REF-043-062 and REF-067 primarily concern browser rendering, DOM insertion, code-string construction, attributes, CSS and inline SVG. The complete browser consumer is outside the requested API-only scope. Those records are Unverified for downstream execution, not blanket Protected or irrelevant because the API returns JSON.

Reviewed API-specific safeguards include JSON writer/serializer boundaries, restrictive API CSP, safe filename headers, destination URL checks and attachment-oriented document delivery. They do not prove that a generated client later uses text rather than raw markup, validates active protocols, or excludes CSS/SVG/event contexts. API-fed descriptions, comments, legal Markdown, custom-property URLs and identity fields need a separately scoped client-consumption review before claiming end-to-end XSS prevention.

Document signatures are not sanitizers or malware verdicts. A valid PDF or OOXML container can still be malicious or socially deceptive. Positive controls must be described by their actual context: raster framing, supported document parts, attachment headers, no-referrer navigation and exact delivery authority.

### A07: Uploads, file delivery, paths and archives

Event-resource documents have a stronger policy than generic storage. `FinalizeStorageUploadSessionCommandHandler.cs:86-91` routes their sessions to `EventResourceFileUploadWorkflow`. The workflow reserves quota and server-selected metadata (`:43-81`) then checks actual length/type and invokes document inspection (`:114-120`).

`EventResourceDocumentInspection.cs:28-76` restricts PDF/DOCX/PPTX with matching extension, snapshots exactly reserved bytes plus an overflow sentinel into a private temporary handle, and transfers the inspected content. `EventResourceDocumentPolicy.cs:64-112,118-208,239-281,307-327` checks archive entry/count/expanded-byte/ratio/name/attribute/compression/CRC/container constraints, rejects external relationships, and prohibits DTD/external resolution with XML depth/character limits. Archive entries are inspected, **not extracted to caller-controlled names**.

Limitations remain explicit: PDF is signature-only; embedded raster parts are not full malware inspection; valid supported containers are not certified safe documents. Generic `StorageContentSignaturePolicy.cs:38-84` has a pass-through non-image branch and prefix-only recognized-document checks. Its raster path (`:94-129`) has MIME/extension/length/framing controls, but generic OOXML must not inherit the specialized event-resource archive pass.

`StorageObjectContentReader.cs:47-80` gates quarantine, disclosure and retention; `:132-143` selects attachment for non-safe-raster content. Public image delivery needs safe metadata. `StorageObjectController.cs:367-397` uses safe download-name fallback and `nosniff`; `EventResourceFileResult.cs:17-39` completes authority immediately before sending and forces a filename. The final sandbox defect is IVSD-F013. Full registration-file intake/scan/release and storage-provider configurations remain unverified.

`LocalFileStorageProvider.cs:316-358` rejects absolute/empty/dot/invalid segments, normalizes separators, canonicalizes and checks root containment. Writes use generated tenant keys, unique `CreateNew` temporary files, byte bounds and non-overwriting moves. Lexical checks do not resolve symlinks or prevent concurrent filesystem replacement. No API-only path to plant those links was established; the root/ancestors must be operator/service controlled or gain a stronger filesystem boundary.

S3 uses bounded hashing streams and SDK object writes (`S3FileStorageProvider.cs:96-119`), not local archive extraction. SDK endpoint/redirect behavior, bucket policy and every presigned response override remain operational/inspection gaps. Cerbos ZIP processing inspected here is export; logical paths derive from local package enumeration, not an established uploaded traversal chain.

Existing tests read: `EventResourceDocumentPolicyTests.cs:69-190` (malformed/active/expanding containers); `StorageContentSignaturePolicySecurityTests.cs:26-99` (spoofed/truncated/active-tail raster); `LocalFileStorageProviderTests.cs:300-316` (lexical traversal only). They were not run. Required future checks include real HTTP final headers, HTML/SVG disguised as raster, MIME/extension mismatch, quarantine, retention, symlinks if hostile filesystem mutation is supported, and zero external request on rejected OOXML relations/DTDs.

### A08: SSRF, provider consumption, redirects and TLS

Actual destination ownership matters:

| Sink | Caller/configuration boundary | Existing controls | Residual disposition |
| --- | --- | --- | --- |
| Local webhooks / registration webhook submission | Authorized endpoint/provider configuration | Direct private/metadata checks, all validation answers, redirects off, cookies off, bounded delivery | Vulnerable to validation-versus-connection DNS change under IVSD-F009. |
| AI model discovery | Authenticated tenant/platform administrator can submit endpoint | Scheme/userinfo/query/fragment/literal checks, provider-specific operation path | Vulnerable hostname/redirect boundary, IVSD-F010; explicit local hosting is separately allowed. |
| Tenant BYO Cerbos publication | Authorized policy-provider administration | HTTPS by default, literal/local/userinfo checks, deadline | Vulnerable hostname/redirect boundary, IVSD-F011. Trusted instance-local endpoints are a different scope. |
| Web Push | Subscription/outbound push boundary | Vetted-address connector, exact `IPEndPoint`, redirects off | Useful native connection-time protection; do not assume its policy matches webhook exceptions. |
| Photon geocoding | Deployment-configured origin; ordinary caller supplies search values | Fixed operation, escaped search/locale/country, HTTPS endpoint structure, redirects off, response byte bounds | Protected against query authority replacement; endpoint/network custody remains operator-owned. |
| Keycloak Admin | Configured identity authority and privileged management | Authority structure, escaped realm/client segments, redirects off, restricted HTTP choices | Protected against inspected path/query/redirect manipulation; complete setter permission and DNS/discovery chain unverified. |
| Event-resource external redirect | Browser navigation, not server fetching | Bounded HTTPS DNS URL, no controls/userinfo, allowed origin, current governance, no-store/no-referrer | Not SSRF at this sink; downstream browser/provider navigation remains outside this audit. |
| Infisical / rotation-aware generic client | Selected deployment/secret authority | Structured authority and secret-owned configuration | Default redirects/headers and real factory composition are not globally certified; not established ordinary-request-controlled SSRF. |

`PhotonAddressGeocoder.cs:159-200` escapes query inputs and bounds both known/unknown response lengths; `PhotonOptionsValidator.cs:120-137` enforces endpoint structure and rejects public demo hosting; client registration at `InfrastructureServicesRegistration.cs:121-130` disables redirects. A private self-hosted geocoder can be intentional.

`KeycloakAdminClient.cs:222-319` validates authority/realm/client and HTTP exceptions; its registration at `InfrastructureServicesRegistration.cs:884-910` disables redirects. HTTPS authenticates the selected server; it does not make arbitrary DNS destinations public or authorize the caller to select them.

`EventResourceDestinationValidator.cs:13-29` and `EventResourceAccessService.cs:35-47` validate current decrypted navigation authority; `EventResourceRedirectResult.cs:32-34` emits no-store/no-referrer. Client navigation is not conflated with backend fetch.

Development certificate exceptions are scoped by environment/host predicates (`AuthenticationExtensions.cs:140-146,565-578`; `DynamicJwtConfigurationService.cs:175-179,204-217`), not an identified global unconditional bypass. They deliberately relax certificate error handling within that trust set and must never be generalized as normal TLS validation. HTTPS metadata can also be explicitly overridden; actual deployment settings remain unverified.

Future negative evidence must include direct IPv4/IPv6/mapped/link-local/metadata targets, mixed public/private A+AAAA, DNS changes, redirect statuses, credential propagation, selected private exceptions and known/unknown response byte budgets. A timeout, clipped display or HTTPS prefix is not an egress policy. Network segmentation is complementary defense, not a substitute for the API contract.

### A16: Deserialization, XML entities and parser authority

No BinaryFormatter or payload-controlled Newtonsoft `TypeNameHandling` sink was found in the searched API/backend layers. Inspected reflection converter factories select declared contract types, not arbitrary CLR names supplied by JSON. Thus the exact unsafe-object examples are Not applicable to those paths, while every framework/provider parser is not globally certified.

Configuration codec safety is described in A04. Event-resource OOXML explicitly prohibits DTD/external resolution and bounds document/expanded archive processing (A07), addressing both file/network XXE and entity-expansion classes. Rejected XML does not prove that every third-party renderer or future attachment processor uses those settings.

Cerbos YAML (`CerbosPolicyPackageService.cs:669-684,731-746`) comes from locally enumerated package files; no API-payload-controlled CLR type selection was established. YAML alias-expansion budgets were not established. Control of the local policy package is a distinct prerequisite and should remain an explicit supply-chain/configuration assumption. Export ZIP construction is not untrusted archive extraction.

Safe deserialization and authentic authority are separate: a plain JSON session can still be forged without any gadget. Data Protection/session signing and the ATProto provider/session checks in A09 address that class; strict types alone do not.

Registration regex already maps a 100 ms deadline and malformed-pattern failures; custom properties do not (IVSD-F012). Rejecting a regex string because it is short cannot establish bounded evaluation.

### A05: Header, log and error injection versus actual sinks

The reference's header-looking body is not HTTP splitting. Actual Event response values require their own analysis: correlation accepts one printable ASCII value up to 128 (`CorrelationIdMiddleware.cs:46`), excluding CR/LF; inspected filenames reject controls/separators; resource destinations reject controls/whitespace; webhook headers derive from fixed names, identifiers and signatures. These support the named header boundaries, not all configured arbitrary outbound headers.

The main request logger records route identity rather than a raw secret-bearing URL (`RequestLoggingMiddleware.cs:39`), excludes bodies/credential headers and specially redacts storage/resource activity. Framework raw request-start/finish is forced to Warning (`ApiHostServiceCollectionExtensions.cs:370`); the dedicated host declares compact JSON logs. Escaped JSON formatting can prevent visual line forgery, but structured placeholders alone are not an escape guarantee across all sinks.

Read tests: `CorrelationIdTests.cs:67` checks replacement of overlong values; `EventResourceRequestLoggingTests.cs:30,49` checks URL/query canary exclusion from logs/traces; `ApiKeyAuthenticationLoggingTests.cs:47` checks bounded outcomes without raw keys across Microsoft/Serilog sinks. None was run or establishes all categories/exporters.

`GlobalExceptionHandler.cs:104` sends generic unclassified detail outside Development. `ExceptionHandlingIntegrationTests.cs:60` reads a real HTTP-path assertion excluding a stack trace. Known application/validation messages intentionally have different mappings; Development may reveal error messages. `GlobalExceptionHandler.cs:116`, `ValidationExceptionHandler.cs:46` and JWT callbacks (`AuthenticationExtensions.cs:183`) still log exception objects/descriptions. Registration of secret redaction is not proof that every exception, scope, custom sink and exporter is safe.

`AuditingSecretProviderDecorator.cs:169` independently rereads correlation headers rather than consuming the main sanitized value. Its initialize/refresh audit was not traced to an externally reachable request-triggered abuse path; this is an independent audit hardening concern, not a confirmed public log injection.

Therefore REF-039-042 remain Unverified for end-to-end forgery, while the main correlation/request-log boundary has positive traceability. Parser/database/crypto error records distinguish public response genericity from operational logs. Future evidence must use canaries through actual failure categories and exporters, inspecting lines/scopes/traces without retaining secrets or PII.

### A14: Secrets, disclosure, diagnostics and privacy

The selected-authority subsystem has meaningful fail-closed behavior: `SecretAuthorityConfiguration.cs:49,116` builds Environment from environment only and rejects User Secrets outside Development/Testing; Infisical bootstrap comes from explicit environment (`ConfigurationBuilderExtensions.cs:83`). `SecretResolver.cs:280,314` rejects source mismatch and bounds provider failures without fallback. Success cache is five minutes; failure is not treated as successful absence.

Existing `SecretAuthorityConfigurationTests.cs:11,22` and `SecretRuntimeRedactionTests.cs:102` inspect production rejection/no fallback and bounded provider-exception logging. Actual external custody, deployment injection and every consumer remain unverified. Environment/User Secrets changes require declared consumer restart; resolver TTL is not backing-source reload.

Exceptions to the intended guarantee are concrete: Google credential settings (IVSD-F015) and raw API-key idempotency copies (IVSD-F016). Public onboarding redaction and digest-only key entities do not erase those second stores.

Password-recovery URLs use fragments rather than password-bearing queries; normal HTTP logs avoid raw request URLs. OAuth authorization codes, receipt proofs and other legitimate URL-carried capabilities still require short life/purpose, no-referrer/no-store and scrubbed observability; "no secrets ever appear in URLs" would be false. No complete production log/export/backup scan was performed.

Private response metadata is explicit, not automatic secret detection. Public actor identity associations and operational tenant labels are published/observable information even when not plaintext credentials. The provider owes purpose/retention and non-user protection, not merely compliance with a DTO shape.

### A15: Cryptography, randomness, custody and rotation

`AtprotoSessionEnvelopeProtector.cs:18,41,175` uses AES-256-GCM, fresh 12-byte CSPRNG nonce, 16-byte tag and associated data for tenant/user/provider/DID/PDS/client-key/version. Ring parsing (`:206`) bounds size/depth/key count, rejects ambiguity, requires one active key and 32-byte material. `RepositoryBackedOAuthSessionStoreTests.cs:30,63,79,151` reads assertions for round-trip without plaintext persistence, retired-key rewrite, unknown/tampered key and invalid ring. This is a narrow pass, not all encrypted artifacts.

Opaque API/setup/scanner/receipt credentials are distinguished from human passwords and UUIDv7 identifiers. API keys use random 32-byte secrets/digests with fixed-time comparison. No MD5/SHA-1 password, DES/Triple-DES or ECB implementation was identified in the scoped shared-security search. Exact human-password work factor remains Unverified (A10), not inferred from the framework brand.

Database keyring sharing (IVSD-F023) supplies cross-host cookie/token continuity, not separate-custody protection. Required database TLS defaults and `TrustServerCertificate=false` are in `PrimaryDatabaseConfiguration.cs:58`; PostgreSQL/MySQL required mode uses verification unless explicitly overridden (`:405,415`). Real certificate/hostname and override settings require runtime proof.

`docs/internal/SECRETS.md:415` separates overlap rollout, coordinated restart and unsupported live rotation. `SecretRotationReplicaConvergenceTests.cs:46,78,131,152` inspects acknowledgment/common-attempt/deadline/candidate rejection, not an actual fleet controller. The generic HTTP rotation helper's default candidate validation returns true (`RotationAwareHttpClientFactory.cs:44`); local acknowledgment is not credential acceptance by the provider. `RotationOptions.cs:30` defaults Enabled true while documentation says false, a separate helper-default drift. Runtime registration/consumer scope must be identified before judging impact.

Future closure must include selected key authority, entropy/material policy, actual wrapped persistence, overlap/retirement, purpose-bound tamper rejection, compromised-key invalidation and restore after rotation. Do not replace a cryptographic random secret with a timestamp, UUID or encoded identifier.

### A17: Request, computation, storage and fleet budgets

Budgets need the correct unit and placement: CPU match deadline, body bytes, decompressed bytes, response bytes, concurrent connections, per-account attempts, aggregate provider cost and persistent storage are independent.

Positive controls include endpoint-specific body/rate metadata, setup/proof/scanner limits, anonymous-registration chained policies, bounded configuration manifests, specialized document/ZIP/XML budgets and an early ATProto transient branch. Testing deliberately substitutes no-limit policies; ordinary Testing HTTP success does not prove production 429 behavior.

Known gaps are synchronous custom-property regex (IVSD-F012), full middleware buffering before thresholds (IVSD-F017), AI/Coop provider bytes (IVSD-F014/021), authentication before normal admission (IVSD-F019), loopback exemption and fleet semantics (IVSD-F020). Request cancellation/deadlines do not automatically interrupt regex, bounded display does not bound buffering, and Redis registration does not make native buckets distributed.

Event-resource quota reservation and upload byte enforcement address the reviewed path's unlimited-retention demonstration. Cleanup/backlog/object-store quotas, registration attachments, large query results and operator-configured limits are not universally certified. Rate limiting alone cannot enforce business-flow state, per-resource ownership, valid positive amounts or one-use proof.

Future tests must enable real policies, coordinate exact observation signals and run representative multi-replica/source failure cases. No container/production cost test was run for this report.

### A18: Host, static and operational exposure

No directory-browser registration or arbitrary data/log-storage static mount was found in the inspected API/combined compositions. Standalone serves bundled UI assets outside API routing; it does not thereby serve its storage directory. The reference's hidden-page/static-alias/port demo is Not applicable to those paths.

Forwarding is not trust-all: `ForwardedHeadersTrustOptions.cs:24` requires configured trust, clears defaults, bounds depth to 1-10 and entries to 32, and rejects wildcard addresses/zero-width networks. But loopback trust, wildcard AllowedHosts and accepted forwarded Host create the operational conditions in IVSD-F022. Dedicated HSTS order is IVSD-F018; combined forwarding occurs earlier.

Operational health JSON intentionally includes named safe check/status/timing information, not arbitrary stack traces. Anonymous mapping and tenant-labelled Prometheus metrics must be ingress-confined or authenticated. Scheduler status is explicitly authorized, so it is not flattened into the anonymous operational class.

No live backend-port exposure, cloud/security group, TLS chain, forwarded-host allowlist, public metrics route or host-based tenant confusion was tested. Development/Testing is not an acceptable public deployment profile; limited development certificate overrides must remain within that profile.

### A19: HTTP caching, private replay and restoration of authority

`PrivateNoStoreMiddleware.cs:9` runs before authorization/action execution and reasserts private/no-store, pragma and no-referrer on start. ETag skips this metadata. `CachingExtensions.cs:12` sets a no-cache base; selected public policies vary by host/tenant headers and auth-aware policies include Authorization. ETag hashes a body, not resource authority.

Local login explicitly suppresses response storage; external-key issuance does not (IVSD-F016). Keycloak operation tests (`KeycloakOperationHttpTests.cs:49,240,274`) read assertions for bounded inspection/no-store/rejected generation before provider contact. Cache-key variation alone does not prove cross-principal isolation; Redis cache does not prove shared output-store eviction.

Middleware lifecycle/authorization checks precede replay, and principal/body/target fingerprints prevent the inspected generic cross-user replay condition. Resource-level decorator authorization generally occurs inside a handler and may not run on a generic cached replay, so revocation/consumption/replay semantics need operation-specific evidence rather than a blanket Protected result. Restore can reintroduce consumed records, older rings or revoked keys without durable fencing; no actual restore rehearsal was supplied.

Future evidence must cover wrong-user/tenant/operation replay, removed role/membership, expired/consumed capability, current privacy/retention/public lifecycle, cache refresh/304 after hidden state and restored authority. A successful old response is not permission to bypass newly required authority.

### A09: Authentication schemes, token authenticity and narrow capability authority

The registration at `AuthenticationExtensions.cs:78-308` contains 11 concrete schemes plus MultiAuth dispatch. Dispatch is not verification: an issuer peek may choose a handler, but the selected handler must validate the token before granting identity.

| Scheme | Reviewed authority | Assessment and next validation |
| --- | --- | --- |
| Keycloak Bearer | Dynamic issuer/metadata, signing-key/lifetime checks and custom audience validator (`AuthenticationExtensions.cs:88-205`; `DynamicJwtBearerPostConfigureOptions.cs:16-35`) | Tampered-signature protection is supported; exact unsigned/two-part rejection and authentic expiry tests remain unverified. Audience/client OR rule is IVSD-F006. |
| Local Identity | Signed HS256; issuer, audience, purpose/type, lifetime and current session authority (`AuthenticationExtensions.cs:206-251`) | Protects against inspected cross-authority substitution; credential/security-stamp changes must revoke current authority. |
| Local replacement | Separate signed audience/type/purpose, restricted header/payload fields and short lifetime (`LocalCredentialReplacementHandler.cs:30-133`) | Replacement authority is not ordinary login or admin authority; native binding/generation consumption remains decisive. |
| API key | Server-stored/configured digest, active/expiry status and server-derived scopes/owner (`ApiKeyAuthenticationHandler.cs:32-186`) | No posted role/scopes become authority. Resource ownership and current revocation need independent checks. |
| Managed control plane | Enabled mode, persisted registration/digest and bounded read/write principal (`ManagedControlPlaneAuthenticationHandler.cs:21-69`) | A narrow managed-machine credential, not generic browser/user identity. Routing-only tests do not prove real registration lifecycle. |
| Setup secret | Exact path/method allowlist, single header and current setup-completion state (`SetupSecretAuthenticationHandler.cs:24-149`) | Generated/injected secret is not a fixed PIN. Completed setup denies stale authority; injection quality/custody remains operator-owned. |
| ATProto bootstrap | Signed assertion bound to tenant/method/path/DID with durable one-time consumption (`AtprotoAuthenticationHandlers.cs:25-67`) | Purpose-bound bridge, not an ordinary bearer session. |
| ATProto session | Separate signer/issuer/audience and tenant/internal subject (`AtprotoAuthenticationHandlers.cs:83-137`; `AtprotoJwtService.cs:180-196,250-291`) | Inspected purpose/key/tenant confusion is constrained. Provider revocation is not fully established by these local token checks. |
| ATProto transient | ES256, unique properties, exact use/method/path/purpose/body digest, freshness and durable replay (`AtprotoTransientAssertionValidator.cs:15-99`) | Private machine transport; it does not supply arbitrary user/tenant authority. |
| Admission scanner | Opaque digest-bound capability; active/expiry and persisted event/tenant/target/actions (`AdmissionScannerAuthenticationService.cs:14-96`) | Narrow scanner authorization, not body-supplied event authority; real wrong-target/expiry/revocation evidence remains needed. |
| Erasure receipt | Opaque digest, original intent, expiry and bounded status authority (`RetainedAuthorityPrivacyErasureWorkflow.cs:84-105`; `PrivacyErasureSaga.cs:76-85`) | Read-only status; ordinary bearer identity cannot replace the required receipt. Fake-service HTTP tests do not prove digest/expiry persistence. |

`ApiAuthenticationConflictMiddleware.cs:40-61` rejects Authorization/API-key/managed-key combinations before authentication. It is not a universal conflict rule for every header; setup/scanner routes have purpose-specific handling. `ApiTenantPostAuthenticationMiddleware.cs:58-92` rejects mismatched API-key tenant binding.

`SetupSecretProvider.cs:45-71,86-119,162-172` uses injection or 16 cryptographically random bytes, fixed-time comparison and persisted completion. `ApiKeyHashing.cs:13-49` uses 32 cryptographically random bytes and a digest. Fast SHA-256 for a high-entropy opaque secret is not the unsalted human-password weakness in REF-150-154. Aggregate UUIDv7 IDs, route IDs and concurrency stamps are not standalone secret proof.

ATProto submitted session JSON is not simply trusted: `AtprotoOAuthSecurityGateway.cs:30-81` compares token DID/PDS and the provider's `getSession` result; its protected persistence envelope is context-bound (`:101-118`). This addresses the unsigned-session illustration independently of whether a serializer has code-execution gadgets.

MCP is a distinct presentation adapter. The transport is anonymously mapped so explicitly public tools can be discovered, but `McpAuthorizationPolicies` require authenticated read/propose authority and additional event-read scopes. `EventManagementMcpTools.cs:98-170` clamps public pagination to 25, normalizes search and uses native query handlers. Management reads and proposals retain their own policy/resource checks. `McpAuthorizationTests.cs:1-210` inspects anonymous-safe discovery and unavailable lifecycle states, but test fixture auth/stubs do not prove every real scheme. `EnableLegacySse` exists in settings while actual transport behavior depends on SDK composition; do not infer a second live endpoint merely from that property.

### A10: Passwords, recovery and account discovery

Local's native password verification and mutation paths use `UserManager` rather than directly comparing or disguising plaintext. This protects the exact plaintext/prefix examples. It does not by itself establish effective algorithm version, unique salts, configured work factor, historical hash quality, benchmark cost or rehash-on-login. Those remain Unverified under REF-150-154; Keycloak/PDS hashing is provider-owned.

The Local policy is stronger than the reference's three-character rule but has the length/blocklist and enumeration gaps in IVSD-F004/005. A shorter policy is not justified by MFA when the Local path has no established second factor. Correct-password entry may still fail while an account is locked; targeted lockout is part of the harm assessment.

Recovery uses JSON POSTs in `LocalPasswordRecoveryController.cs:23-64`, not a state-changing GET. Eligible recovery requires verified native address and Ready credential binding (`LocalIdentityLifecycleStore.cs:19-34`). Consumption verifies operation-specific native proof, expiry/consumption, security and concurrency stamps inside a serializable transaction (`:228-276`). Purpose includes operation/generation/actor/external login/address (`:386-400`); password replacement and proof consumption commit together. A completed-operation replay is not permission to change the password again or issue a session.

Links derive from a configured HTTPS public address (`LocalIdentityLifecycleDeliveryProcessor.cs:27-30`), not request Host. SMTP puts proof in a URL fragment (`LocalIdentityLifecycleSmtpTransport.cs:20-32`), never a proposed password in the query. Fragments reduce request-target/referrer exposure but remain sensitive mailbox/browser content. Sender/recipient integrity, link cleanup and actual mail-provider logging are unverified. The target-only exception condition remains IVSD-F007.

Read existing assertions include lifecycle HTTP ordinary missing/ineligible uniformity and replay, an explicitly enabled limiter scenario, and native lockout transitions. They do not prove target-only exceptional uniformity, actual hasher cost or production delivery confidentiality.

### A11: Authorization, IDOR, property authority and public projection

`RequestAuthorization<TRequest>.AuthorizeAsync` (`src/Explore.Application/Authorization/RequestAuthorization.cs:25-71`) resolves catalogued action/resource facts, persisted resource context and the selected provider before handler execution. Unannotated requests do not receive imaginary automatic protection; they require established handler-owned authority. `RuntimeAuthorizationProvider.cs:228-285` denies selected Cerbos failure rather than silently authorizing through local mode.

Concrete inspected user paths:

- `UserController.cs:131-149` resolves the current provider-linked identity; profile read is not a caller-selected account.
- `UserController.cs:260-290` compares target ID to current user and requires `If-Match`.
- `GetUserOrganizationsRequestHandler.cs:30-39` makes membership read self-only.
- `OrganizationMemberController.cs:63-107` is authenticated and its query contracts declare persisted resource facts (`GetOrganizationMembersRequest.cs:10-21`; `GetOrganizationMemberDetailsRequest.cs:7-18`).
- `UpdateUserDto.cs:3-7` contains profile groups, not balance/password/role/admin/tenant authority; `UpdateUserCommandHandler.cs:77-109` updates those groups explicitly and preserves omitted groups.
- Local creation/reset handlers resolve current administrator authority around sensitive operations, rather than accepting an anonymous posted role (`CreateLocalIdentityCommandHandler.cs:25-103`; `ResetLocalCredentialCommandHandler.cs:25-90`).

`ExternalApiKeyIntegrationTests.cs:298-329` asserts another user's update returns bounded absence and leaves persisted owner/scopes/name unchanged. That is stronger evidence than a mock expecting an authorization call, although it was not run here.

Public GET is not automatically harmless. Authentication discovery explicitly empties provider secret fields (`AuthProviderConfigurationService.cs:126-154`). Global actor reads deliberately expose public identity/federation associations, while excluding deleted/suspended identities (`ActorRepository.cs:205-224,261-271`) and internal user/tenant serialization (`ActorDto.cs:20-36`). DID, handle, PDS host, display name and organization/group associations are still published information; they must match product commitments.

Public event details recheck lifecycle/public eligibility after cached projection (`GetEventDetailsRequestHandler.cs:62-85`); public detail/list paths constrain Published/Public state (`GetPublicEventDetailsRequestHandler.cs:22-28`; `GetEventListRequestHandler.cs:117-118`). Existing `EventLocationPrivacyPublicEligibilityTests.cs:21-137` exercises anonymous sibling projections and hidden parents. These traces support the named paths, not a universal field-by-field pass for all 947 operations, counts, cache variations or public association disclosure.

HAL advertises server-authorized actions; hiding a link does not authorize or deny the direct route. Identifier entropy, entity immutability, tenant filters and role checks are complementary, not interchangeable protections.

### A12: CORS, CSRF and browser-to-API conversion

`CorsExtensions.cs:7-43` uses exact configured origins for credentialed internal policies and separate noncredentialed external policies. Development's any-origin policy does not allow credentials. The host selects DevPolicy in Development/Testing and InternalAppPolicy otherwise (`ApiHostApplicationExtensions.cs:64-76`). No unconditional origin predicate or alleged suffix/regex matcher was identified in these policies. Explicitly configuring `null` or an attacker-controlled origin can still undermine the intended trust set.

CORS decides which browser origins may read selected responses; it is not an authorization control. Sensitive profile/membership/payment data must remain independently scoped even when an allowed origin sends valid credentials. Actual preflight headers, reverse-proxy additions and deployed origin lists remain Unverified.

Split ingress uses `EventBffRequestPolicy.RequiresAntiforgeryValidation` (`EventBffRequestEnricher.cs:140-147`) for unsafe `/api` requests carrying cookies. `EventApiProxyExtensions.cs:118-143` validates before forwarding. `BffProxyHeaderSanitizer.cs:11-104` strips browser-supplied privileged authorization/key/setup/tenant/support headers; `EventBffTrustedRequest.ApplyTo` (`EventBffRequestEnricher.cs:91-122`) reconstructs trusted authority.

Combined ingress (`CombinedApiBridgeMiddleware.cs:17-67`) authenticates cookies, enforces applicable antiforgery, obtains the server-held token, rejects failed refresh/missing token outside exact onboarding, sanitizes headers, clears cookie identity and lets API MultiAuth authenticate again. `StandaloneHostApplicationExtensions.cs:43-54` establishes this order. Its early antiforgery rejection uses a plain response outside API exception normalization; response-contract consistency is a separate hardening concern, not evidence of CSRF bypass.

Direct no-cookie clients present explicit bearer/API-key/capability headers. They do not automatically send an authenticated browser cookie and do not need the BFF's cookie antiforgery token. Scanner routes pin their capability scheme and derive event/tenant/actions from authenticated authority, not scanner body claims.

Existing Split/Combined test pipelines check missing/valid antiforgery and forged-header stripping, but use test upstream/session adapters. Launch verification must repeat POST/PUT/PATCH/DELETE with missing, wrong-session and invalid proof, cookie-without-token, rejected refresh, forged tenant/setup/support headers, anonymous onboarding with cookies and direct external credentials through both actual topologies.

### A13: Amounts, quantity, payment state and guest authority

Order input validation and `CreateOrderWithHoldCommandHandler` resolve the published event catalog and authoritative snapshots, rather than treating a caller's aggregate price as owed value. `RegistrationOrderLine.cs:90` and `RegistrationOrderAddOnLine.cs:76` require positive quantities; add-ons check pinned published catalog/order currency before multiplication. Zero-priced items and zero quantities are not the same contract.

`Money.cs:15` rejects negative minor units and permits the no-currency sentinel only for zero. `MinorUnitMath.cs:5` uses `Int128` intermediates and rejects out-of-`long` addition/multiplication; basis points are bounded with defined rounding. `RegistrationOrder.cs:303` uses those helpers. Thus inspected positive-input monetary overflow and negative-credit paths are Protected. Not all counters are money: `RegistrationInventoryRepository.cs:221-229` sums nullable `int` quantities, so extreme aggregates/provider overflow handling remain Improvable rather than silently covered by `Money`.

`RegistrationRefundService.cs:35` requires captured lineage and positive amount no greater than refundable capacity; `RefundAttemptRepository.cs:225,318` reserves/deduplicates/locks. There is no identified loyalty-wallet subtraction counterpart for REF-107/108. Genuine provider acceptance, authoritative amount/currency, captures and refund capacity are the corresponding checkout duties.

Guest access uses cryptographically random capabilities with stored digest/fixed-time comparison (`GuestCapabilityTokenService.cs:10,35`) and event/order/attempt binding (`RegistrationOrderAccessGuard`, guest access handlers). Order UUID alone does not grant read/write authority. Authenticated/guest controllers are separate; mutations require appropriate capability/concurrency, and provider redirect/browser success does not establish paid state.

Guest start (`GuestRegistrationOrderController.cs:40`) has private-no-store/protected replay metadata; middleware encrypts a versioned body/header envelope and checks expiry/decryption (`IdempotencyMiddleware.cs:357,372`). This is a useful deliberate contrast to raw external-key issuance F016. Actual shared ring/rotation/restore remains F023/F003.

Payment checkout durably claims an attempt (`RegistrationPaymentAttemptClaimService`) and dispatches through durable work (`RegistrationPaymentCheckoutDispatchService.cs:185`), preserving lineage/fences/effects/uniqueness. Incoming callbacks use receipt processing and independently verified provider binding/signatures (`IncomingWebhooksController.cs:18`; `RegistrationProviderIncomingWebhookVerifier.cs:20,52,100`; `StripeConnectIncomingWebhookVerifier.cs:22`). None equates posted browser success with capture. Remote calls still need stable provider operation identity across ambiguity; every provider SDK/mode/account was not exercised.

Read tests include `MoneyTests.cs:29`, `TicketPricingRulesTests.cs:97` (rounding and long overflow), `EventTicketTypeMoneyBoundaryTests.cs:11`, `RegistrationPaymentAttemptPersistenceTests.cs:155,200` (SQLite concurrency), `PaymentReconciliationPostgreSqlClaimTests.cs` (PostgreSQL fences), and `RegistrationPaymentHttpSecurityTests.cs` (HTTP/capability). They are not passing-run claims. Future evidence must cover negative/zero/maximum quantity, amount/provider mismatch, concurrent holds/claims/refunds, wrong/expired capability and committed-effect-before-receipt failure (F027).

## Individual Vulnerability Comparison Ledger

This is the exhaustive comparison, not a shortlist. Each of the 209 records appears once as a table row. Dispositions apply to the named inspected mechanism; shared analysis sections contain its source controls, caveats, improvement and validation obligations. A Protected row never upgrades unreviewed providers/endpoints or out-of-scope browser behavior.

`CWE` identifies the failure class, not a vulnerability proof. Source labels are defined in the inventory. The reference locator is externally sourced provenance; the analysis key leads to Event-native evidence. Every Unverified row is an explicit launch/review obligation, not a silently skipped example.

### Interpretation and query boundaries

| Reference | Functional failure / CWE | Reference provenance and type | Event disposition | Event rationale, improvement and next evidence |
| --- | --- | --- | --- | --- |
| REF-001 | Search changes SQL predicates / 89 | `S/VulnerableDatabaseService.cs:67-109`; S | Protected | A01: inspected typed specifications, bound values/model identifiers; generated/provider internals and every caller not certified. |
| REF-002 | Search reads another secret table / 89 | `S/VulnerableDatabaseService.cs:67-109`; S | Protected | A01/A11: no caller-inserted SQL in traced families; resource/property authorization remains independently required. |
| REF-003 | Database parser details escape / 209 | `S/VulnerableDatabaseService.cs:110-117`; S | Protected | A05: production unclassified HTTP failures are generic; exception logs and known mappings remain separately unverified. |
| REF-004 | Windows shell treats data as commands / 78 | `S/VulnerableCommandService.cs:21-78`; S | Not applicable | A02: no matching scoped API shell/process sink identified; no all-dependency absence claim. |
| REF-005 | Unix shell interprets supplied data / 78 | `S/VulnerableCommandService.cs:21-78`; S | Not applicable | A02: same scoped sink result, independently retained platform variant. |
| REF-006 | Argument separation still invokes a shell / 78 | `P/CommandInjection.razor:90-109`; I/Q | Not applicable | A02: reject this alleged prevention pattern if a process feature is introduced. |
| REF-007 | Command execution has no deadline / 400 | `S/VulnerableCommandService.cs:44-52`; G | Not applicable | A02: no command sink; actual regex/response allocation gaps remain F012/F017, not collapsed into a shell exploit. |
| REF-008 | User filter executes C# / 94 | `P/CodeInjectionCSharpScript.razor:225-276`; S | Not applicable | A02: no scoped general-purpose server scripting evaluator found. |
| REF-009 | Dynamic predicate exposes hidden records / 917,862 | `P/ExpressionInjection.razor:160-204`; S | Not applicable | A02/A11: no matching expression interpreter; visibility still belongs to persisted authorization/projection. |
| REF-010 | Filter permits arbitrary expression logic / 917 | `P/ExpressionInjection.razor:170-204`; S | Not applicable | A02: native query contracts are not a payload-authored expression language. |
| REF-011 | Expressions consume resources or reveal parser errors / 400,209 | `P/ExpressionInjection.razor:170-215`; S/G | Not applicable | A02/A17: no matching evaluator; analogous custom-property regex defect is F012/REF-201. |
| REF-012 | Untrusted template evaluates arithmetic / 1336 | `P/TemplateInjection.razor:177-240`; S/Q | Unverified | A02: no matching engine found, but complete email rendering was not traced; incompatible reference syntax is not proof. |
| REF-013 | Template reads server information / 1336 | `P/TemplateInjection.razor:72-90,177-240`; S/I | Unverified | A02/A14: certify actual rendering facts/inputs before a template-wide pass. |
| REF-014 | Template launches a process / 1336 | `P/TemplateInjection.razor:138-155,177-240`; S/Q | Unverified | A02: no identified execution sink; reference fixture itself does not establish process execution. |
| REF-015 | Template exception details are returned / 209 | `P/TemplateInjection.razor:224-237`; S | Protected | A05: generic production failure response; rendering-specific mappings/logs remain unverified. |
| REF-016 | LDAP wildcard enumerates directory / 90 | `S/VulnerableLdapService.cs:62-104`; M | Not applicable | A04: no API LDAP filter boundary; external realm LDAP configuration is unreviewed. |
| REF-017 | LDAP conjunction broadens results / 90 | `S/VulnerableLdapService.cs:106-144`; M | Not applicable | A04: simulated reference, no matching scoped directory sink. |
| REF-018 | LDAP disjunction targets passwords / 90 | `P/LdapInjection.razor:35-42`; M | Not applicable | A04: preserve distinct disclosure variant; no caller-authored LDAP filter identified. |
| REF-019 | Special-character fallback discloses directory / 90 | `S/VulnerableLdapService.cs:146-163`; M | Not applicable | A04: no corresponding fallback implementation identified. |
| REF-020 | Ordinary directory result overexposes attributes / 200,256 | `S/VulnerableLdapService.cs:165-191`; S/M | Improvable | A11/A14/F026: no LDAP output; public identity/linkage contract and complete API projection review remain open. |
| REF-021 | XPath input bypasses product predicate / 643 | `P/XpathInjection.razor:191-265`; S | Not applicable | A04: no XPath evaluator found in scoped backend. |
| REF-022 | XPath traverses to admin credentials / 643 | `F/Data/products.xml:49-56`; S | Not applicable | A04/A14: no matching XPath source; secret custody remains separately assessed. |
| REF-023 | XPath traverses to database secret / 643 | `F/Data/products.xml:59-66`; S | Not applicable | A04: no matching query mechanism; this does not close secret-authority findings. |
| REF-024 | XPath extracts private/financial identities / 643 | `F/Data/products.xml:69-84`; S | Not applicable | A04/A11: mechanism absent in scoped search; property-level rights need separate review. |
| REF-025 | XPath extracts integration/signing secrets / 643 | `F/Data/products.xml:87-101`; S | Not applicable | A04/A14: no XPath sink; raw-key/Google persistence findings still apply. |
| REF-026 | Public product includes internal fields / 200 | `P/XpathInjection.razor:269-294`; G | Improvable | A11/F026: named projections avoid credential entities; stable public relationship fields need an explicit privacy contract. |
| REF-027 | XPath errors reveal parser details / 209 | `P/XpathInjection.razor:258-265`; S | Protected | A05: generic production unknown error envelope; no XPath sink and no universal log pass. |
| REF-028 | XML profile text inserts privileged role / 91 | `S/VulnerableXMLService.cs:9-57`; S | Not applicable | A04/A11: no posted XML profile authority; sitemap uses element writers. |
| REF-029 | XML text introduces alternate balance / 91 | `S/VulnerableXMLService.cs:9-76`; S | Not applicable | A04/A13: no XML-based monetary authority path identified. |
| REF-030 | XML biography injects admin boolean / 91 | `S/VulnerableXMLService.cs:9-93`; S | Not applicable | A04/A11: profile authority comes from typed server-owned updates, not XML nodes. |
| REF-031 | Repeated manipulated XML sample variants / 91 | `F/Data/user-profiles.xml:32-80`; D | Not applicable | A04: duplicates retain coverage without being counted as new live API endpoints. |
| REF-032 | JSON name inserts a role member / 74 | `P/JsonInjection.razor:73-89,168-211`; S/I | Protected | A04/A11: typed encoding plus profile authority allowlist; general duplicate-member semantics remain bounded to reviewed codec. |
| REF-033 | JSON biography inserts admin member / 74 | `P/JsonInjection.razor:90-100,168-211`; S/Q | Protected | A04/A11: DTO does not confer admin authority; reference duplicate-member escalation is parser-dependent. |
| REF-034 | JSON input inserts premium access / 74 | `S/VulnerabilityService.cs:269-309`; S/Q | Protected | A04/A11: client text is not an authoritative access field in reviewed profile/import contracts. |
| REF-035 | Unescaped JSON corrupts document / 74 | `P/JsonInjection.razor:168-211`; S | Protected | A04: declared serializers/writers and strict bounded configuration codec; inspect new converters when added. |
| REF-036 | Cookie-setting line enters header representation / 93,113 | `C/VulnerableRedirectController.cs:10-38`; M | Protected | A05: inspected correlation/download header inputs reject controls; reference emits body text, not a cookie header. |
| REF-037 | Custom privilege-like header lines / 93,113 | `S/VulnerabilityService.cs:109-165`; M | Protected | A05: fixed trusted header names and sanitized ingress; arbitrary configured outbound headers remain unverified. |
| REF-038 | Input creates second response-looking block / 93,113 | `C/VulnerableRedirectController.cs:10-38`; M/Q | Protected | A05: no confirmed wire split; actual guarded header paths, not the reference's textual success claim. |
| REF-039 | Log input forges login success / 117 | `P/LogInjection.razor:39-46,179-225`; S/M | Unverified | A05: bounded correlation/main route logs; every sink/exporter newline behavior was not inspected. |
| REF-040 | Log input forges a system error / 117 | `P/LogInjection.razor:39-46,179-225`; S/M | Unverified | A05: retain independent event-forgery variant; validate final formatter/output. |
| REF-041 | Log input forges admin action / 117 | `P/LogInjection.razor:39-46,179-225`; S/M | Unverified | A05: structured placeholders alone do not authenticate audit events. |
| REF-042 | Alleged structured-log safety ignores formatter / 117 | `P/LogInjection.razor:96-138`; I/Q | Unverified | A05: use real logger/exporter evidence; a JSON request sink does not certify all exception/audit sinks. |

### Browser execution and stored active content

| Reference | Functional failure / CWE | Reference provenance and type | Event disposition | Event rationale, improvement and next evidence |
| --- | --- | --- | --- | --- |
| REF-043 | Reflected script-element markup / 79 | `P/ReflectedXss.razor:175-215`; S/Q | Unverified | A06: API consumer/browser insertion excluded; reference script execution is insertion-dependent. |
| REF-044 | Reflected image-event markup / 79 | `P/ReflectedXss.razor:31-39,175-215`; S | Unverified | A06: event handlers require real downstream rendering review, not a JSON-only claim. |
| REF-045 | Reflected active SVG / 79 | `P/ReflectedXss.razor:31-39`; S | Unverified | A06/A07: API raster/document gates exist; full inline markup consumption not reviewed. |
| REF-046 | Stored script-element comment / 79 | `P/StoredXss.razor:27-40,207-226`; S/Q | Unverified | A06: stored API text may be consumed later; browser execution and actual persistence scope need separate evidence. |
| REF-047 | Stored event handler accesses cookies / 79 | `P/StoredXss.razor:27-40,207-226`; S | Unverified | A06: HTTP-only cookie controls are not a substitute for safe content rendering. |
| REF-048 | Supplied input executed as browser code / 95,79 | `P/DomBasedXss.razor:238-263`; S | Unverified | A06: client interop construction is outside API-only inspection. |
| REF-049 | Decoded untrusted content inserted as HTML / 79 | `P/DomBasedXss.razor:265-291`; S | Unverified | A06: transport encoding does not sanitize rendered HTML. |
| REF-050 | Text-rendering alternative builds injectable code / 95 | `P/DomBasedXss.razor:149-197`; I | Unverified | A06: a future client audit must examine code construction before text assignment. |
| REF-051 | HTML sanitizer used inside unsafe code string / 95 | `P/DomBasedXss.razor:134-144,149-197`; I | Unverified | A06: HTML sanitization and JavaScript string context are distinct. |
| REF-052 | Tooltip attribute breakout / 79 | `P/XssAttributes.razor:208-230`; S | Unverified | A06: ordinary framework binding is not this raw-markup sink; complete client attributes not audited. |
| REF-053 | Executable protocol in link / 79 | `P/XssAttributes.razor:232-251`; S | Unverified | A06/A07: event-resource destinations require HTTPS; every API-fed/custom client URL remains unverified. |
| REF-054 | Link value escapes markup attribute / 79 | `P/XssAttributes.razor:232-251`; G | Unverified | A06: scheme validation alone does not establish safe attribute construction. |
| REF-055 | Style attribute escapes into script markup / 79 | `P/XssCss.razor:178-193`; S/Q | Unverified | A06: raw HTML breakout, not a demonstrated CSS interpreter flaw. |
| REF-056 | Style value introduces event element / 79 | `P/XssCss.razor:178-193`; S | Unverified | A06: retain event-handler variant; browser/client scope needed. |
| REF-057 | Inline SVG executes an event / 79 | `P/XssSvg.razor:31-39,239-254`; S | Unverified | A06: no complete inline-SVG consumer audit; upload gate is a separate protection. |
| REF-058 | Inline SVG script element / 79 | `P/XssSvg.razor:31-39,239-254`; S/Q | Unverified | A06: actual insertion/browser behavior not supplied. |
| REF-059 | SVG active link on activation / 79 | `P/XssSvg.razor:91-102`; I | Unverified | A06: API-only evidence cannot certify inline active links. |
| REF-060 | SVG embeds HTML-bearing content / 79 | `P/XssSvg.razor:91-102`; I | Unverified | A06/A07: specialized documents reject unsupported parts; browser SVG contexts remain excluded. |
| REF-061 | SVG animation event / 79 | `P/XssSvg.razor:91-102`; I | Unverified | A06: no browser animation-event verification performed. |
| REF-062 | SVG reference active URL allegation / 79 | `P/XssSvg.razor:91-102`; I/Q | Unverified | A06: reference browser support is not established; do not manufacture an Event pass. |
| REF-063 | Unsanitized SVG served directly / 434,79 | `P/XssSvg.razor:190-225`; I | Improvable | A07: safe raster/document and attachment controls; sandbox is lost under F013, generic scope not fully certified. |
| REF-064 | Arbitrary upload served as HTML / 434,79 | `C/VulnerableFileUploadController.cs:23-75`; S | Improvable | A07: nonraster attachment/quarantine controls differ by workflow; preserve final sandbox F013 and audit generic MIME policy. |
| REF-065 | Uploaded active SVG served as HTML / 434,79 | `C/VulnerableFileUploadController.cs:23-75`; S | Improvable | A07: SVG not accepted as public safe raster/event document; generic/release paths and final delivery require closure. |
| REF-066 | Compound extension disguises active upload / 434 | `P/XssFileUpload.razor:91-104`; I/S | Improvable | A07: matching MIME/extension/framing in named flows; generic prefix/pass-through policy is not sanitization. |
| REF-067 | Certificate demo endpoint becomes raw markup / 79 | `P/CertificateValidation.razor:27-33,149-203`; G | Unverified | A06: no equivalent certificate UI is assumed; downstream API error/endpoint rendering remains outside scope. |

### Identity, authorization and request origin

| Reference | Functional failure / CWE | Reference provenance and type | Event disposition | Event rationale, improvement and next evidence |
| --- | --- | --- | --- | --- |
| REF-068 | Embedded admin password authenticates / 798 | `P/HardcodedCredentials.razor:223-261`; S | Protected | A09/A10: native Local verification and controlled enrollment, not a source-visible fixed admin password. |
| REF-069 | Fixed PIN gates confidential data / 798 | `C/MissingRateLimitingController.cs:17-52`; S | Protected | A09: setup uses generated/injected purpose-bound secret and current completion; injected quality remains operator-owned. |
| REF-070 | Anonymous listing includes account secrets / 862 | `C/MissingAuthorizationController.cs:24-36`; S | Protected | A11: named user/member sensitive reads use identity/resource authority; not a blanket all-list projection pass. |
| REF-071 | Anonymous caller obtains deletion success / 862 | `C/MissingAuthorizationController.cs:40-46`; S/M | Protected | A11: inspected deletion derives authenticated current user; reference's deletion itself is simulated. |
| REF-072 | UI-only admin restriction / 602,862 | `P/MissingAuthorization.razor:107-147`; I | Protected | A11: native server authorization precedes mutations; HAL is an affordance, not the authority. |
| REF-073 | Path ID reads another person's profile / 639 | `C/VulnerableUserProfileController.cs:64-85`; S | Protected | A11: current-user/profile equality and self-only membership reads; broader object families need their own evidence. |
| REF-074 | Query ID selects another profile / 639 | `C/IdorController.cs:24-39`; S/M | Protected | A11: same actual self/resource protections; duplicate reference mechanism retained. |
| REF-075 | Registration accepts privileged role / 269,915 | `C/PrivilegeEscalationController.cs:27-68`; S | Protected | A11: controlled native credential creation and authenticated provider facts, not posted role authority. |
| REF-076 | Duplicate registration accepts admin role / 269,915 | `C/IdorController.cs:43-75`; S/M | Protected | A11: independent duplicate covered by the same server-owned enrollment ceiling. |
| REF-077 | Profile update overposts balance / 915 | `C/MassAssignmentController.cs:46-57`; S | Protected | A11/A13: profile DTO/explicit mutation has no financial authority member. |
| REF-078 | Profile update overposts password hash / 915 | `C/MassAssignmentController.cs:46-57,77-84`; S | Protected | A11/A10: profile DTO excludes hash; credential mutations use native controlled lifecycle. |
| REF-079 | Profile update overposts admin boolean / 915 | `P/MassAssignment.razor:151-172`; I | Protected | A11: no admin flag in reviewed profile contract; cannot infer an illustrated field actually exists in reference endpoint. |
| REF-080 | Omitted fields destructively reset authority values / 915 | `C/MassAssignmentController.cs:46-57,77-84`; G | Protected | A11: explicit profile groups preserve absent groups; unrelated money/password values are not mapped. |
| REF-081 | Three-character password accepted / 521 | `P/WeakPassword.razor:226-277`; S/M | Protected | A10: minimum 12 rejects this exact failure; stronger single-factor baseline remains F005. |
| REF-082 | Common/default/sequential passwords accepted / 521 | `S/VulnerabilityService.cs:574-613`; S/M | Improvable | A10/F005: short examples blocked; long common/repeated values lack a full-password blocklist. |
| REF-083 | Illustrative short/weak Identity policy / 521 | `P/WeakPassword.razor:171-211`; I | Improvable | A10/F005: 12/128 and no composition rules; minimum 15/blocklist and actual hasher evidence needed. |
| REF-084 | Unsigned token marked authentic / 347 | `C/VulnerableJwtController.cs:12-99`; S | Unverified | A09/F008: Local/ATProto require signatures; Keycloak unsigned rejection not independently established by current tests. |
| REF-085 | Two-part token without signature accepted / 347 | `C/VulnerableJwtController.cs:59-70`; S | Unverified | A09/F008: require real selected-handler negative evidence rather than assume framework registration proves it. |
| REF-086 | Tampered privileged token claims accepted / 347 | `C/VulnerableJwtController.cs:72-98`; S | Protected | A09: signing/issuer authorities validate before claims; repair test fixture branch F008 and retain real scheme evidence. |
| REF-087 | No expiry/issuer/audience/purpose checks / 345 | `C/VulnerableJwtController.cs:12-99`; G | Improvable | A09/F006/F008: real checks exist, but Keycloak aud-or-azp and signed expiry/context proof need improvement. |
| REF-088 | Request Host controls recovery destination / 640,346 | `C/PasswordResetPoisoningController.cs:23-54`; S | Protected | A10: configured HTTPS public recovery authority, not request Host. Other host-dependent links remain F022. |
| REF-089 | Editable form host poisons recovery link / 640 | `P/PasswordResetPoisoning.razor:158-205`; M/D | Protected | A10: no caller-selected recovery host in inspected native delivery. |
| REF-090 | Second phishing destination reset fixture / 640 | `S/VulnerabilityService.cs:663-698`; M/D | Protected | A10: independent repeated fixture has the same configured-authority protection. |
| REF-091 | Any nonempty reset token succeeds / 640 | `C/PasswordResetPoisoningController.cs:57-71`; S/M | Protected | A10: purpose/current binding/expiry/single-use proof in serializable transaction. |
| REF-092 | GET resets without validated proof / 640,352 | `C/DataInUrlsController.cs:72-97`; S/M | Protected | A10: POST completion and bound proof; no credential-changing GET equivalent. |
| REF-093 | Cross-origin unverified purchase / 352,862 | `C/CsrfController.cs:42-67`; S | Protected | A12/A13: cookie ingress antiforgery in both hosts; explicit machine authentication is separate. |
| REF-094 | Cross-origin balance reset / 352,862 | `C/CsrfController.cs:70-77`; S | Protected | A12/A11: no shared anonymous balance reset; cookie writes gated. |
| REF-095 | Controllers broadly bypass antiforgery / 352 | `C/VulnerabilityDemoControllerBase.cs:9-11`; S | Protected | A12: cookie-to-API adapter enforces proof instead of blanket demo-base bypass. |
| REF-096 | Mutable state shared among anonymous callers / 862 | `C/CsrfController.cs:18-19,42-77`; G | Protected | A11/A19: persisted identity/tenant/resource/capability authority, not shared static demo account state. |
| REF-097 | Any origin may send credentials / 942 | `F/Program.cs:33-44`; S | Protected | A12: production explicit origin list; Development/Testing and effective operator config are separate. |
| REF-098 | Cross-origin balance/secret read / 942,200 | `C/CorsMisconfigurationController.cs:25-38`; S | Protected | A12/A11: origin acceptance does not authorize inspected sensitive reads; all finance DTOs not certified. |
| REF-099 | Cross-origin sensitive profile read / 942,200 | `C/CorsMisconfigurationController.cs:41-54`; S | Protected | A12/A11: named profile/member reads remain identity/resource scoped. |
| REF-100 | Cross-origin unprotected transfer-looking action / 942,862 | `C/CorsMisconfigurationController.cs:57-68`; S/M | Not applicable | A12/A13: no equivalent arbitrary simulated money transfer; actual payment flows need their own state/transaction checks. |
| REF-101 | Unconditional acceptance includes null origin / 942 | `F/Program.cs:33-44`; S/I | Protected | A12: exact origin policy does not accept all opaque origins; operator must not explicitly trust unsafe null origins. |
| REF-102 | Wildcard-plus-credentials allegation / 942 | `P/CorsMisconfiguration.razor:82-89`; I/Q | Not applicable | A12: actual policies are not that combination; reference itself implements reflection, not this alleged variant. |
| REF-103 | Faulty suffix/regex origin matching allegation / 942 | `P/CorsMisconfiguration.razor:82-89`; I/Q | Protected | A12: exact matching, not the alleged matcher; live proxy/config remains unverified. |

### Business values and attempt budgets

| Reference | Functional failure / CWE | Reference provenance and type | Event disposition | Event rationale, improvement and next evidence |
| --- | --- | --- | --- | --- |
| REF-104 | Negative purchase quantity reduces total / 20 | `P/NegativeQuantities.razor:185-222`; S/M | Protected | A13: validator/domain positive quantity and authoritative catalog snapshots, not caller-owned totals. |
| REF-105 | Zero purchase quantity accepted / 20 | `P/NegativeQuantities.razor:185-222`; G | Protected | A13: positive line quantity; zero-priced valid items do not imply zero quantity is allowed. |
| REF-106 | Positive quantity overflows cost arithmetic / 190 | `P/IntegerOverflow.razor:164-168`; S | Improvable | A13: monetary Int128/long overflow rejected; extreme int inventory aggregates need distinct provider boundary tests. |
| REF-107 | Negative loyalty quantity credits value / 20 | `P/IntegerOverflow.razor:164-168`; G | Not applicable | A13: no loyalty-wallet mechanism; native order quantities independently require positive values. |
| REF-108 | Purchase subtracts without affordability / 840 | `P/IntegerOverflow.razor:164-168`; G | Not applicable | A13: external checkout, not sample wallet subtraction; provider acceptance/capture/refund invariants apply. |
| REF-109 | Negative purchase amount increases balance / 20 | `C/CsrfController.cs:42-67`; G | Protected | A13: nonnegative Money, checked snapshots and positive bounded refunds; no negative-purchase wallet credit. |
| REF-110 | Unlimited credential guesses / 307 | `C/MissingRateLimitingController.cs:25-53`; S | Improvable | A10/A17: Local account lockout exists; F004/F019/F020 expose response/admission/loopback/fleet gaps. |
| REF-111 | Submitted PIN enters logs / 532 | `C/MissingRateLimitingController.cs:25-28`; S | Protected | A05/A14: named authentication/request logs exclude raw credentials; wider exception/exporter proof remains open. |

### Secret custody, private data and diagnostics

| Reference | Functional failure / CWE | Reference provenance and type | Event disposition | Event rationale, improvement and next evidence |
| --- | --- | --- | --- | --- |
| REF-112 | Configuration read exposes integration secrets / 200,798 | `C/ApiKeysExposureController.cs:24-41`; S | Improvable | A14: public/admin reads redact; authorized setup resolves Google secret and its DB copy is F015. |
| REF-113 | Listing enumerates provider secrets / 200,798 | `C/ApiKeysExposureController.cs:44-58`; S | Protected | A11/A14: inspected discovery/admin projection excludes raw provider/key values; not every administrative export certified. |
| REF-114 | Initialization returns raw service secret / 200,798 | `C/ApiKeysExposureController.cs:61-80`; S | Improvable | A14/A19: legitimate one-time key issuance is scoped, but generic replay retains raw key under F016. |
| REF-115 | Browser console records returned secret / 532 | `P/ApiKeysExposure.razor:186-225`; S | Unverified | A06/A14: browser/client console behavior excluded; API redaction alone cannot certify it. |
| REF-116 | Private key embedded in client script / 798 | `P/ApiKeysExposure.razor:119-148`; I | Unverified | A06/A14: shipped browser bundle and full-client secret scan not performed. |
| REF-117 | Distributed application config embeds cloud secrets / 798 | `P/ApiKeysExposure.razor:119-148`; I | Protected | A14: selected external authority and mismatch/no-fallback guards; actual deployment/export scan remains open. |
| REF-118 | Database credential fixed in source / 798 | `P/HardcodedCredentials.razor:175-217`; S/I | Protected | A14/A15: inspected DB binding uses selected authority, not embedded application password; no history-wide secret certification. |
| REF-119 | Integration credentials fixed/persisted without proper custody / 798 | `P/HardcodedCredentials.razor:175-217,223-261`; S/I | Improvable | A14/F015/F016: no matching hardcoded source value established; concrete recoverable settings/replay copies remain. |
| REF-120 | Encryption key is source-visible and fixed / 321 | `P/HardcodedCredentials.razor:204-211`; I | Improvable | A15/F023: external envelope material exists; separately wrapped DP persistence and deployed custody unverified. |
| REF-121 | Secret retained in source comment / 798 | `P/HardcodedCredentials.razor:213-216`; I | Unverified | A14: no source-history/package/container secret scan run; policies/workflow existence are not a clean scan. |
| REF-122 | Proposed password and token appear in reset query / 598 | `C/DataInUrlsController.cs:26-63`; S | Protected | A10/A14: native POST and fragment proof, no password-bearing query; fragment/browser/mail privacy still required. |
| REF-123 | API key supplied as query parameter / 598 | `P/DataInUrls.razor:74-106`; I | Protected | A09/A14: inspected API-key auth uses explicit headers; purpose-specific URL capabilities remain separate. |
| REF-124 | Full secret reset URL logged / 532 | `C/DataInUrlsController.cs:43-48`; S | Unverified | A05/A14: main request logger avoids raw URL; actual mail/exception/export chain not fully certified. |
| REF-125 | Recovery requester receives proof directly / 640,200 | `C/PasswordResetPoisoningController.cs:43-54`; S/M | Protected | A10: accepted response does not disclose proof; verified lifecycle recipient owns delivery. |
| REF-126 | Reset tokens/link recorded in activity log / 532 | `C/PasswordResetPoisoningController.cs:43,60`; S | Unverified | A05/A10: intended bounded logging, but actual exporter/provider/exception packet absent. |
| REF-127 | Plaintext password printed to log / 532 | `P/SensitiveDataLogs.razor:139-148`; S/M | Protected | A05: inspected authentication/request flows exclude credential fields; no global all-sink pass. |
| REF-128 | Session token printed to log / 532 | `P/SensitiveDataLogs.razor:139-148`; S/M | Protected | A05/A15: named logs and protected session storage avoid ordinary raw token logging; callback exceptions remain unverified. |
| REF-129 | Whole password request object logged / 532 | `P/SensitiveDataLogs.razor:108-128`; I | Protected | A05: request logger does not serialize bodies/credentials; other custom categories require their own review. |
| REF-130 | Database failure returns secrets/stack / 209 | `C/VerboseErrorsController.cs:22-68`; S/M | Protected | A05: generic production catch-all; known exceptions/logs are narrower independent obligations. |
| REF-131 | File/runtime failure exposes machine details / 209 | `C/VerboseErrorsController.cs:71-114`; S | Protected | A05/A18: generic production unclassified response and no raw filesystem serving inferred. |
| REF-132 | Processing failure exposes provider secret / 209 | `C/VerboseErrorsController.cs:117-168`; S/M | Protected | A05/A14: bounded public catch-all; actual provider/known-message redaction must be proven separately. |
| REF-133 | Generic response still logs secret exception / 532 | `C/VerboseErrorsController.cs:56-60,150-154`; G | Unverified | A05: exception objects remain in several logging calls; registered redactors do not certify final export. |
| REF-134 | User list overexposes recovery/private/payment fields / 200,213 | `C/ExcessiveDataExposureController.cs:23-72`; S/M | Unverified | A11/A14: named projections restrict authority/fields; complete public read/count/cache field review is not supplied. |
| REF-135 | Single profile returns unnecessary private fields / 200,213 | `C/ExcessiveDataExposureController.cs:75-98`; S/M | Improvable | A11: private self profile and public actor projection differ; public association/privacy expectations require explicit review. |
| REF-136 | Update response returns password hash / 200 | `C/MassAssignmentController.cs:46-57`; S | Protected | A11: explicit safe profile DTO/output, not a returned credential-bearing entity. |

### Files, host trust and public operational exposure

| Reference | Functional failure / CWE | Reference provenance and type | Event disposition | Event rationale, improvement and next evidence |
| --- | --- | --- | --- | --- |
| REF-137 | Public upload directory lists filenames / 548 | `F/Program.cs:164-177`; S | Protected | A18/A07: no directory-browser/data mount found; storage access runs through handlers/authority. |
| REF-138 | Public log directory listing / 548 | `F/Program.cs:180-191`; S | Protected | A18: no equivalent log mount in inspected compositions; actual ingress/container mounts unverified. |
| REF-139 | Static document discloses personal/payment data / 552,200 | `W/uploads/user_data.txt:1-11`; S | Protected | A07/A18: no arbitrary storage webroot mount; private file disclosure/quarantine checks, not complete content classification. |
| REF-140 | Static document exposes employee/banking data / 552,200 | `W/uploads/employee_records.txt:1-40`; S | Protected | A07/A18: same guarded-delivery boundary; distinct data-subject variant retained. |
| REF-141 | Static confidential business document / 552,200 | `W/uploads/confidential_report.txt:1-23`; S | Protected | A07/A18: API-owned storage delivery rather than raw static root; public intentional resources still need owner policy. |
| REF-142 | Static invoice exposes customer/bank details / 552,200 | `W/uploads/invoice_2024_001.txt:1-32`; S | Protected | A07/A18: named private file gates; no assertion all uploaded content is nonconfidential. |
| REF-143 | Secret environment file placed under web storage / 798,552 | `W/uploads/.env.production:1-27`; S/Q | Unverified | A14/A18: no equivalent mount found; full shipped artifact/history/ingress secret scan absent; reference serving itself uncertain. |
| REF-144 | Webroot text asset contains credentials / 552,798 | `W/secrets/database_config.txt:1-5`; S | Protected | A14/A18: no such arbitrary secrets/static mapping in reviewed hosts; artifact scan remains independent. |
| REF-145 | API reveals server secret-file path and URL / 200 | `C/XxeInjectionController.cs:28-41`; S | Protected | A05/A18: no corresponding file-path discovery API identified; generic failures do not expose raw locations. |
| REF-146 | Internal page relies only on loopback reachability / 862,200 | `A/ssrf-admin.html:76-239`; S | Improvable | A18/F022: no equivalent secret-bearing admin page; anonymous operational endpoints still require ingress confinement. |
| REF-147 | Internal administrative sample has public static alias / 552,200 | `F/Program.cs:194-201`; S/G | Not applicable | A18: no reference-like internal/public alias found; bundled UI assets are not raw admin secrets. |
| REF-148 | Arbitrary request Host controls authority / 346 | `F/appsettings.json:8`; S | Improvable | A18/F022: exact proxy trust is positive; wildcard/forwarded Host and absolute links need actual host boundary. |
| REF-149 | Public application permits plaintext HTTP / 319 | `F/Program.cs:69-76,153-155`; S/G | Improvable | A18/F018: HTTPS/default DB TLS controls exist; dedicated HSTS ordering and deployed TLS/listener exposure need closure. |

### Password hashing, randomness, cipher and certificates

| Reference | Functional failure / CWE | Reference provenance and type | Event disposition | Event rationale, improvement and next evidence |
| --- | --- | --- | --- | --- |
| REF-150 | Fast unsalted MD5 password hashes / 327,916,759 | `P/WeakHashing.razor:191-221`; S | Unverified | A10/A15: no matching MD5 sink found, native hasher invoked; exact stored algorithm/cost/salt evidence absent. |
| REF-151 | Fast unsalted SHA-1 password hashes / 327,916,759 | `P/WeakHashing.razor:223-253`; S | Unverified | A10/A15: retain separate algorithm variant; real Local/provider/historical parameters required. |
| REF-152 | Plain fast SHA-256 for passwords / 916,759 | `P/WeakHashing.razor:137-174`; I | Unverified | A10/A15: API-key digest is not human-password hashing; exact native password work factor not asserted. |
| REF-153 | Salted SHA-256 still cheap to guess / 916 | `P/WeakHashing.razor:162-173`; I | Unverified | A10/A15: verify calibrated work factor and rehash, not merely salt or hash verification. |
| REF-154 | Duplicate unsalted equal-password hashes / 759,916 | `P/NoSaltHashing.razor:176-190`; S/D | Unverified | A10/A15: native API usage positive; unique salts/effective provider policy not established by plaintext-inequality test. |
| REF-155 | Time-seeded security-token sequence / 338 | `P/WeakRandom.razor:204-242`; S/M | Protected | A09/A15: inspected opaque secrets use CSPRNG, not reproducible time seed. |
| REF-156 | General PRNG produces security bytes / 338 | `P/WeakRandom.razor:157-183`; I | Protected | A09/A15: `RandomNumberGenerator` for API/setup/capability secrets; review each new credential generator. |
| REF-157 | Small numeric noncryptographic session ID / 330,338 | `P/WeakRandom.razor:157-183`; I | Protected | A09/A15: random opaque or signed purpose-bound authority; ordinary aggregate ID is not authentication proof. |
| REF-158 | GUID inherently predictable allegation | `P/WeakRandom.razor:157-183`; I/Q | Not applicable | A09/A15: allegation not established; UUIDv7 entity IDs are intentionally nonsecret identifiers. |
| REF-159 | ECB leaks repeated plaintext patterns / 327 | `P/EcbMode.razor:190-218`; S/D | Protected | A15: inspected ATProto persistence uses context-bound AES-GCM, not ECB; not all encrypted artifacts certified. |
| REF-160 | Encryption uses source-derived fixed key / 321 | `P/EcbMode.razor:168-175,190-247`; G | Improvable | A15/F023: envelope keys selected externally; DP database custody/actual material/backup policy still need evidence. |
| REF-161 | Obsolete DES key strength / 326,327 | `P/InsufficientKeyLength.razor:246-274`; S | Protected | A15: no DES in scoped security paths; AES-256 envelope and exact ring-length checks. |
| REF-162 | Deprecated Triple-DES/block size / 327 | `P/InsufficientKeyLength.razor:276-302`; S | Protected | A15: no matching Triple-DES scoped cipher; provider/dependency suites not certified. |
| REF-163 | Per-client accepts any certificate / 295 | `P/CertificateValidation.razor:114-136,149-203`; I/M | Improvable | A08/A15: no unconditional production callback found; explicit TLS overrides/deployed transport need audit. |
| REF-164 | Global certificate validation disabled / 295 | `P/CertificateValidation.razor:125-128`; I | Protected | A08/A15: no corresponding global unconditional bypass in searched paths; not a live certificate-chain test. |
| REF-165 | Development certificate bypass leaks into exposure / 295 | `P/CertificateValidation.razor:130-136`; I | Improvable | A08/A15/A18: environment/host restrictions exist; never expose Development/Testing or assume their trust set is normal TLS. |

### Serialization, XML and session authority

| Reference | Functional failure / CWE | Reference provenance and type | Event disposition | Event rationale, improvement and next evidence |
| --- | --- | --- | --- | --- |
| REF-166 | Untrusted binary objects invoke behavior / 502 | `P/InsecureDeserialization.razor:158-226,293-347`; S/Q | Not applicable | A16: no scoped BinaryFormatter sink found; reference .NET 10 availability unverified. |
| REF-167 | Deserialized object supplies admin identity / 502,345 | `P/InsecureDeserialization.razor:194-210,285-291`; S/M | Protected | A09/A16/A11: signed/protected session and server-owned authority; serializer safety alone would be insufficient. |
| REF-168 | Unsafe binary feature explicitly enabled / 502 | `F/DotnetSecurityFailures.csproj:7,36-38`; S/Q | Not applicable | A16: no matching operative API mechanism identified; configuration switch is not runtime execution proof. |
| REF-169 | JSON selects arbitrary runtime types / 502 | `P/JsonDeserialization.razor:146-244`; S | Not applicable | A16/A04: declared contract converters, no matching payload-selected CLR type sink. |
| REF-170 | JSON demo explicitly launches a process / 94 | `P/JsonDeserialization.razor:168-173,246-291`; M | Not applicable | A02/A16: no analogous scoped process side effect; demo side effect is not a gadget proof. |
| REF-171 | YAML illustration selects object types / 502 | `P/YamlDeserialization.razor:120-143`; I/Q | Not applicable | A16: local policy parsing has no established payload-type selection; alias budget remains separately unverified. |
| REF-172 | YAML text match triggers process launch / 94 | `P/YamlDeserialization.razor:159-254`; M | Not applicable | A02/A16: no matching process sink; reference does not operate a YAML parser for ordinary input. |
| REF-173 | XML entity expansion has no budget / 776 | `P/XmlBomb.razor:132-211`; S | Protected | A16/A07: event-resource XML prohibits DTD and bounds characters/depth/expanded archive bytes. |
| REF-174 | XML document resolves local files / 611 | `C/XxeInjectionController.cs:44-90`; S | Protected | A16/A07: explicit resolver-null/DTD prohibition at named document parser; no third-party parser blanket pass. |
| REF-175 | XML document fetches external network / 611,918 | `C/XxeInjectionController.cs:44-90`; S/I | Protected | A16/A07: external relationships and resolver network access rejected in named inspection. |
| REF-176 | Second XML reader resolves local files / 611 | `C/XxeInjectionController.cs:147-199`; S/D | Protected | A16/A07: duplicate parser class receives the same explicit no-resolution disposition. |
| REF-177 | Second XML reader fetches external network / 611,918 | `C/XxeInjectionController.cs:147-199`; S/D | Protected | A16/A07: separately retain duplicate network variant; verify rejected inputs produce no connection. |
| REF-178 | XML parser returns full exception / 209 | `C/XxeInjectionController.cs:190-199`; S | Protected | A05/A16: bounded inspection outcomes/generic catch-all; operational exception logs remain unverified. |
| REF-179 | Restricted JSON used as unsigned session proof / 565,345 | `P/InsecureDeserialization.razor:82-120`; I/G | Protected | A09/A16: cookie/Data Protection and ATProto provider-restored identity; plain client JSON is not authority. |

### Paths, archives and network consumption

| Reference | Functional failure / CWE | Reference provenance and type | Event disposition | Event rationale, improvement and next evidence |
| --- | --- | --- | --- | --- |
| REF-180 | Relative path reads beyond intended root / 22 | `C/PathTraversalController.cs:34-60`; S | Improvable | A07: lexical/root/generated-key guards; symlink/hostile filesystem boundary not established. |
| REF-181 | Absolute path replaces intended root / 73,22 | `C/PathTraversalController.cs:34-60`; G | Protected | A07: local provider rejects absolute/invalid segments and verifies canonical root; filesystem custody remains required. |
| REF-182 | Backup name writes beyond root / 22,73 | `C/ArbitraryFileWriteController.cs:28-75`; S | Improvable | A07: server-selected keys and root validation prevent lexical escape; symlink replacement needs explicit threat model. |
| REF-183 | Absolute filename controls arbitrary write / 73,22 | `C/ArbitraryFileWriteController.cs:28-75`; G | Protected | A07: no absolute caller storage path; generated tenant-scoped keys and root enforcement. |
| REF-184 | Caller-selected backup overwrites existing file / 73 | `C/ArbitraryFileWriteController.cs:67-69`; G | Protected | A07: unique `CreateNew` temporary and non-overwriting final move in named local storage path. |
| REF-185 | Archive member traverses extraction root / 22 | `C/ZipSlipController.cs:50-99`; S | Protected | A07: specialized archive inspection rejects bad part names and does not extract member names. |
| REF-186 | Archive extraction overwrites outside authority / 23,73 | `C/ZipSlipController.cs:76-96`; S | Protected | A07: no extraction in reviewed upload policy, generated storage key; no inherited generic archive-import claim. |
| REF-187 | Archive expansion lacks byte/count cap / 409,400 | `C/ZipSlipController.cs:29-120`; G | Protected | A07: explicit entry/individual/aggregate/ratio/XML budgets in event-resource workflow; generic OOXML prefix policy is weaker. |
| REF-188 | Caller-selected URL reaches internal services / 918 | `C/SsrfController.cs:29-74`; S | Vulnerable | A08/F009-011: privileged webhook/AI/BYO Cerbos hostname paths can bypass restrictive destinations; prerequisites stated. |
| REF-189 | Caller-selected URL reaches metadata service / 918 | `P/BasicSsrf.razor:94-114`; S/I | Vulnerable | A08/F009-011: explicit direct webhook metadata block exists, but unbound/unchecked DNS can reach forbidden targets if network permits. |
| REF-190 | Redirect reaches unchecked second destination / 918 | `C/SsrfController.cs:19-25,40-43`; S/G | Vulnerable | A08/F010-011: AI/Cerbos lack redirect control; webhook/Photon/Keycloak paths explicitly disable it. |
| REF-191 | SVG image reference triggers server request / 918 | `C/XssSvgController.cs:35-111`; S | Not applicable | A07/A08: no scoped SVG reference-fetching processor; supported upload policy excludes SVG/unsupported relationships. |
| REF-192 | Legacy SVG reference triggers server request / 918 | `C/XssSvgController.cs:49-96`; S | Not applicable | A07/A08: distinct reference notation retained; no matching backend processor found. |
| REF-193 | One SVG causes unbounded outbound fan-out / 400,918 | `C/XssSvgController.cs:49-99`; G | Not applicable | A07/A08: no matching server fan-out; actual provider/queue budgets remain A17. |
| REF-194 | Provider response buffered before display limit / 400 | `C/SsrfController.cs:40-50`; G | Vulnerable | A08/A17/F014/F021: AI/Coop lack explicit byte caps; Photon/webhooks bounded; middleware buffering F017 is additional. |
| REF-195 | Arbitrary uploads accumulate without quota/cleanup / 400,770 | `C/VulnerableFileUploadController.cs:10-53`; G | Improvable | A07/A17: event-resource reservation/quota/byte controls; full generic/registration/provider cleanup and fleet storage remain unverified. |

### Hidden, duplicate and supplementary guard omissions

| Reference | Functional failure / CWE | Reference provenance and type | Event disposition | Event rationale, improvement and next evidence |
| --- | --- | --- | --- | --- |
| REF-196 | NoSQL inequality changes credential matching / 943 | `S/VulnerableNoSQLService.cs:57-86,117-141`; H | Not applicable | A01/A09: no MongoDB/BSON/operator-auth sink; native typed relational identity verification. |
| REF-197 | NoSQL regex bypasses exact credentials / 943 | `S/VulnerableNoSQLService.cs:57-86,143-152`; H | Not applicable | A01/A09: no caller-selected document operator; do not ignore regex abuse elsewhere (REF-201). |
| REF-198 | NoSQL ordered comparison substitutes credentials / 943 | `S/VulnerableNoSQLService.cs:57-86,154-162`; H | Not applicable | A01/A09: no matching document-query auth mechanism identified. |
| REF-199 | Unknown operator silently skips credential check / 287,943 | `S/VulnerableNoSQLService.cs:117-172`; H/G | Protected | A09/A11: failed/missing native credentials deny; selected authorization-provider failure does not switch to permissive mode. |
| REF-200 | Hidden sample stores plaintext passwords / 256 | `S/VulnerableNoSQLService.cs:21-53`; H | Protected | A10/A15: Local mutations invoke native password hasher, not plaintext; exact salt/cost remains unverified. |
| REF-201 | Attacker-selected regex has no match budget / 1333 | `S/VulnerableNoSQLService.cs:143-152`; H/G | Vulnerable | A16/A17/F012: concrete event/session custom-property sink lacks deadline even though no NoSQL login exists. |
| REF-202 | Alleged safe shell validator permits newline commands / 78 | `S/VulnerableCommandService.cs:82-113`; G | Not applicable | A02: no matching shell sink; do not adopt the reference's unsafe prevention snippet. |
| REF-203 | Anyone lists registration identities/roles / 862,200 | `C/PrivilegeEscalationController.cs:71-87`; S/G | Protected | A11: inspected private membership/registration authority, public bounded actor identity is a different explicit projection. |
| REF-204 | Anyone clears all registrations / 862 | `C/PrivilegeEscalationController.cs:89-98`; S/G | Protected | A11/A13: no shared anonymous collection-clearing operation; persistent commands enforce named authority. |
| REF-205 | Prefix disguises password instead of hashing / 256 | `C/IdorController.cs:55-63`; S/M | Protected | A10: native password hashing/mutation, not a fabricated hash string. |
| REF-206 | Anonymous caller resets shared profile defaults / 862 | `C/MassAssignmentController.cs:60-74`; S/G | Protected | A11: controlled self update/admin lifecycle, not an unauthenticated static-profile reset. |
| REF-207 | Anonymous caller reads shared private profile / 862 | `C/MassAssignmentController.cs:31-43`; S/G | Protected | A11: current-user profile and resource-scoped member paths; complete other private DTOs remain open. |
| REF-208 | Anyone reads shared balance/email status / 862,200 | `C/CsrfController.cs:27-38`; S/G | Protected | A11/A13: no shared demo account; actual commercial/registration reads need current resource authority. |
| REF-209 | Request Host port selects privileged internal page / 346 | `F/Program.cs:125-140`; G | Not applicable | A18/F022: no matching host-port admin-page branch; effective Host/operational boundaries are still improvable. |

## Recommendations

### Priority and decision ledger

`P0` means resolve or explicitly disable/constrain the affected launch path before enabling it; `P1` means address in the launch security workstream; `P2` means establish a deliberate documented defense-in-depth contract. These priorities are not blanket permission to accept risk or postpone an actual high-impact exploit. Closure evidence belongs to the exact candidate revision.

| Finding / mitigation | Priority and owner | Rejected alternative / trade-off | Required closure evidence |
| --- | --- | --- | --- |
| IVSD-F001 / IVSD-M001 | P1, release engineering | Green no-op or eventual nightly result as candidate security evidence | Real lane execution for hosting/auth/combined/shared-ingress-only changes. |
| IVSD-F002 / IVSD-M002 | P1, docs/persistence/API | Choosing the strongest inconsistent document as truth | One source-aligned RLS/pipeline/native-CQS contract and paired operator explanation. |
| IVSD-F003 / IVSD-M003 | P0, operator/release/security | Treating this report or unrun tests as approval | Candidate-revision operational/test/scan/recovery/incident packet with named owners. |
| IVSD-F004 / IVSD-M004 | P1 before Local exposure, identity | Removing lockout entirely or revealing identifier eligibility for usability | Uniform unauthenticated envelopes while real attempts remain bounded and recovery usable. |
| IVSD-F005 / IVSD-M005 | P1 before Local exposure, identity | Symbol rules/periodic resets instead of length and full-password blocklist | Same accepted policy on create/change/recovery/replacement; actual hash-cost evidence. |
| IVSD-F006 / IVSD-M006 | P1, auth/realm operator | Signature validity or browser client ID as sufficient API audience | Correctly signed complete aud/azp/type matrix through the actual issuer contract. |
| IVSD-F007 / IVSD-M007 | P1 before Local recovery, lifecycle/email | Broad exception swallowing or leaking target-only failures | Bounded target-specific failure produces the same public accepted result and retained internal diagnosis. |
| IVSD-F008 / IVSD-M008 | P1, identity tests | Unresigned expired payload or conditionally skipped assertion | Authentic expiry/unsigned/context negatives and unexpected fixture failure. |
| IVSD-F009 / IVSD-M009 | P0 before local webhook delivery, webhooks | Safe first DNS lookup as connection authorization | Exact vetted socket target; mixed/rebound/metadata answers rejected; no forbidden handoff. |
| IVSD-F010 / IVSD-M010 | P0 before unrestricted AI endpoints, AI/security | HTTP/HTTPS syntax and literal-IP denylist as hostname safety | Real redirect/DNS/byte tests with intentional local-origin policy preserved. |
| IVSD-F011 / IVSD-M011 | P0 before tenant BYO publishing, Cerbos/security | Loosening authorization when provider configuration fails | Restricted BYO connector proof without impairing explicit trusted instance-local policy. |
| IVSD-F012 / IVSD-M012 | P0 before untrusted pattern use, custom properties | Pattern length/request cancellation as CPU deadline | Fixed malformed/timeout failures and early length rejection through all affected commands. |
| IVSD-F013 / IVSD-M013 | P1, API delivery | Controller's intermediate CSP assignment as final response proof | Final attachment/nosniff/sandbox survives the real middleware response path. |
| IVSD-F014 / IVSD-M014 | P1 before configured AI use, infrastructure | Provider token/output contract as response-byte control | Known/chunked oversized body rejected before full allocation. |
| IVSD-F015 / IVSD-M015 | P0 before Google configuration, secrets/identity | Sensitive flag or redacted DTO as protection for DB copies | No recoverable secret in settings/export/replay; approved binding and rotation/removal handling. |
| IVSD-F016 / IVSD-M016 | P0 before key issuance, API keys/idempotency | HTTP no-store/digest entity as proof of one-time disclosure | Real keyed issuance retains/replays no raw key; logical creation dedup remains defined. |
| IVSD-F017 / IVSD-M017 | P1, middleware | Hash/store threshold after full buffering as memory ceiling | Threshold-crossing/streaming/large response has bounded capture and exact bytes. |
| IVSD-F018 / IVSD-M018 | P1, host/operator | HTTPS redirection alone as proof HSTS uses external scheme | Dedicated and combined trusted-proxy final-header tests. |
| IVSD-F019 / IVSD-M019 | P1, admission/auth/operator | Moving all identity policies ahead of authentication | Conservative pre-auth budget and retained verified-identity limits, with counted expensive work. |
| IVSD-F020 / IVSD-M020 | P1, hosting/operator | Loopback address as unlimited internal identity or Redis as distributed limiter | Missing/spoofed forwarding and multi-replica aggregate budget evidence. |
| IVSD-F021 / IVSD-M021 | P1 before Coop use, moderation | Duration deadline as maximum response size | Bounded provider response reader before parsing. |
| IVSD-F022 / IVSD-M022 | P1, ingress/hosting/operator | Internal-looking path or trusted proxy alone as host/metrics authorization | Forbidden host/forwarding/operational routes fail; legitimate domains/scrapers work. |
| IVSD-F023 / IVSD-M023 | P2, secrets/operator | Shared database ring as independent database-compromise protection | Threat model, separate wrapping if required, actual purpose/rotation/restore behavior. |
| IVSD-F024 / IVSD-M024 | P0 for PostgreSQL profile, persistence/operator | Enabling docs only, superuser workaround or EF bypass as RLS authority | Coherent installer/session/role/worker contract on real application tables under ordinary roles. |
| IVSD-F025 / IVSD-M025 | P1, Application/persistence | Audit reason or generic repository as ownership guard | Each API-reachable bypass has predicates/authority and tracked-entity negative tests. |
| IVSD-F026 / IVSD-M026 | P1, privacy/product/API | Stable identifier is "not a secret" as sufficient publication policy | Approved public field/relationship contract and real serialization/privacy cases. |
| IVSD-F027 / IVSD-M027 | P1 or P0 by effect, application/API | HTTP idempotency key as atomic business/provider transaction | Commit-before-receipt failure/retry leaves one logical effect and explicit ambiguity handling. |

### API-specific standards coverage beyond the demo labels

The reference catalogue does not replace a full verification standard. These API Top 10:2023 responsibilities remain visible in the report even when the demo has no separate page:

| OWASP API category | Event responsibility and evidence | Remaining gap |
| --- | --- | --- |
| API1: Broken Object Level Authorization | Persisted resource facts, self-only profile/membership, guest/scanner/receipt binding (A09/A11/A13) | All resource/read/write/replay variants, bypasses and actual negative tests. |
| API2: Broken Authentication | Multi-scheme validation, current Local authority, one-use setup/transient proofs (A09/A10) | F004-008; actual realm/MFA/hash/revocation/rotation evidence. |
| API3: Broken Object Property Level Authorization | Explicit DTO groups/projections and server-owned authority (A04/A11) | F026 and complete public/administrative field/export/count review. |
| API4: Unrestricted Resource Consumption | Limits/document/parser/quota and selected provider bounds (A07/A17) | F012/014/017/019-021 and real fleet/cost/storage tests. |
| API5: Broken Function Level Authorization | Native CQS decorators/handler guards, selected-provider fail closed, management tool policies (A09/A11) | Unannotated handler-owned boundaries and current role/replay coverage. |
| API6: Unrestricted Sensitive Business Flows | Durable checkout/refund/capability lineage and attempt budgets (A10/A13) | Automated reservation, messaging, recovery, expensive provider and consequential create abuse. |
| API7: SSRF | Actual endpoint owners and destination/redirect/connector policy (A08) | F009-011 and real network/SDK/provider destination evidence. |
| API8: Security Misconfiguration | Exact proxy trust, private response defaults, env/TLS/ops boundaries (A18/A19) | F018/020/022/024 plus deployed defaults/role proof. |
| API9: Improper Inventory Management | Two hosts, 947 documented operations, optional MCP/ops/worker/provider scope | Deployed enabled endpoints, disabled features, versioned contract/service inventories and retired paths. |
| API10: Unsafe Consumption of APIs | Provider response validation/budgets, callback verification, ambiguous dispatch recovery (A08/A13) | F014/021/027; SDK internals, live accounts and authenticated callback replay matrix. |

ASVS 5.0.0 can be adopted as a subsequent versioned requirements ledger, typically with an explicitly selected assurance level and higher-rigor treatment of admin/payment/tenant authority. This report does not make a full ASVS level claim, cover all infrastructure penetration tests, or fabricate exact requirement IDs. Dependency/SBOM/advisory, history-wide secret, container/base image, ingress/TLS, provider callback, backup/erasure, incident and stakeholder evidence remain explicit work.

### How future fixes should fit Event

Keep HTTP controllers as presentation adapters. Put authoritative validation and resource/property access in the native CQS handler/decorator boundary so HTTP, MCP, jobs and other legitimate callers cannot obtain different authority. Repositories continue to return entities; handlers perform explicit DTO projection with Mapperly or native mappings. Validators remain manually instantiated. Derive tenant and acting identity from trusted server context, not body fields. HAL controls action discovery; server-side checks control actual execution.

Use narrow value objects and domain state transitions for amounts, quantities and lifecycle rules. Treat transaction/idempotency fences as durable authority, not process-local locks. External effects use the existing outbox, delivery/reconciliation and provider-specific ambiguity handling rather than adding an uncoordinated second write. URL, file and parser guards belong at the actual network/filesystem/deserialization boundaries as well as validation entry points.

Pre-release freedom permits removing unsafe contracts and obsolete compatibility readers when future remediation is authorized. It does not permit bypassing tenant checks, fabricating migrated authority, hand-editing generated migrations, weakening tests, or substituting a generic security framework for concrete boundary ownership.

### Rejected assessment shortcuts

- Trust EF Core use as proof that all raw SQL and dynamic identifiers are safe.
- Infer authorization from UUIDs, HTTP verbs, administrative-looking routes, HAL visibility or CORS.
- Declare API XSS impossible because most responses are JSON; file delivery, XML/SVG/calendar output and downstream rendering have different contexts.
- Treat a URL validator or successful TLS handshake as proof of safe egress after redirects and DNS resolution.
- Fix weak password policy by adding arbitrary composition rules instead of length, blocklists, attempt controls and usable recovery.
- Treat encryption as a solution when keys are stored with the same authority, logs leak plaintext, or a backup resurrects capabilities.
- Add a dependency or copy the example application's safe snippets as remediation without clean-room and license review.
- Re-run or repair unrelated suites during a report-only audit, or silently turn source evidence into a claimed passing runtime check.

## Stakeholders

| Stakeholder | Security interest and possible harm | Provider responsibility |
| --- | --- | --- |
| Attendees and guest registrants | Private names/contact/answers, ticket or capability theft, inaccessible recovery, unwanted faith/location inference | Minimize projection and telemetry; enforce purpose/retention, exact-resource capability and recovery boundaries. |
| Personal organizers and group members | Same-tenant privilege escalation, event alteration, reputational harm or stolen attendee data | Enforce current membership and acting authority independently of supplied publisher IDs. |
| Organizations and tenant administrators | Cross-organization/tenant data access, forged identity, unsafe delegated integrations | Keep scoped administration and provider credentials separate; revalidate revocation and privileged changes. |
| Instance administrator and responders | Account takeover, unsafe bootstrap/recovery, secret custody, alert overload | MFA/recovery where supported, least privilege, reviewable admin actions and workable incident ownership. |
| Self-hosting adopters | Incorrect RLS/TLS/proxy assumptions, insecure defaults, lost keys/restore, unknown dependency risk | Truthful supported profiles, explicit security configuration, immutable evidence and tested recovery. |
| Non-users and households | Private-home disclosure, tracking, leaked attendee relationships, unsolicited contact | Avoid unnecessary exposure/inference even where no account holder can complain. |
| Provider/integration operators | Forged callbacks, SSRF, credential leakage, duplicate external effects, expensive abuse | Authenticate integrations, constrain egress, bound cost and reconcile ambiguous outcomes. |
| Maintainers and future users | Security debt hidden behind passing no-op checks or stale report dates | Preserve stable findings, current revisions, exact test scope and accountable accepted risks. |

## I-VSD Principles And Domains

Selected Sunni ethical principles are normative for the provider-responsibility analysis. OWASP, NIST and framework guidance operationalize compatible security responsibilities; they do not supply religious authority.

| Principle | Application in this assessment |
| --- | --- |
| Amanah / Trust | Steward credentials, tenant boundaries, admin power, capability authority and recovery evidence. |
| Sidq / Truthfulness | Distinguish illustrations from executable examples, prototype from deployed protection, and test source from passing results. |
| Adl / Justice | Protect same-tenant peers as well as tenant boundaries; prevent abuse controls from unfairly locking out shared-network users. |
| Non-Harm / La Darar | Reduce foreseeable account, data, financial, availability and reputation harm before exposing community users. |
| Rights of People | Restrict private data and consent, retain remedies, support safe erasure and avoid unnecessary disclosure. |
| Avoiding Spying / Tajassus | Do not make prompts, attendee data, URLs, identifiers or secrets part of ordinary analytics/security telemetry. |
| Promise-Keeping and avoiding deception | Keep public security, portability, support and recovery claims within demonstrated capabilities. |
| Ihsan / Excellence | Maintain concrete negative tests, clean architecture, current evidence and sustainable operational controls. |
| Reducing excessive uncertainty | Explain capability, retry, unknown payment/provider outcomes and unsupported hosting profiles accurately. |

The six domains are reviewed proportionally: Strategic (launch claims and supported profiles), Design (safe input/recovery/capability defaults), Technical (enforced mechanisms), Operational (keys, egress, incidents, limits and restore), Governance (who can accept risk/change authority), and Evaluation (negative tests and revision-bound audit evidence). Business-model and religious-content judgments outside API security remain out of scope; finance/religious-legal questions are not answered by a security pass.

## Common Overlooked Failures And Outcomes

| Overlooked failure | Foreseeable outcome | Required interpretation |
| --- | --- | --- |
| SingleTenant treated as one trusted user | One organizer reaches another organizer's private resources | Same-tenant negative access is a release check. |
| Testing environment used as production evidence | Disabled limiters and permissive diagnostics conceal abuse paths | Test enabled policies and production-equivalent settings explicitly. |
| Input validated before a second DNS lookup | Public-looking destination reaches private infrastructure | Validate the actual connection target or enforce equivalent controlled egress. |
| Exact-resource authority not rechecked on replay | Removed membership or consumed capability receives a cached success | Bind/revalidate replay according to the operation's actual authority contract. |
| Safe serializer used for unsigned authority | Client changes role/session fields despite no code-execution gadget | Serialization safety and authenticity are independent. |
| Filename/type checked but file served inline | Stored browser-origin active content executes on another user | Quarantine, immutable finalization and delivery headers matter. |
| Structured message contains a raw exception | Tokens/provider credentials enter sinks or exporter scopes | Redaction must cover actual values, scopes and exception messages. |
| Backup restores older keys or capabilities | Deleted identities or consumed tickets regain authority | Restore fencing and retained erasure authority must be rehearsed. |
| A green workflow intentionally did no work | Candidate lacks its advertised security evidence | Record execution and candidate identity, not just status color. |

## Validation Gaps

### Verification performed for this report

| Check | Result |
| --- | --- |
| Documented family inventory | 53/53 page families, including the hidden/supplemental catalogue beyond them. |
| Individual comparison coverage | Exactly one row for each REF-001 through REF-209; 209/209, valid disposition values and analysis mapping. |
| Disposition totals | 95 Protected, 5 Vulnerable, 25 Improvable, 43 Unverified, 41 Not applicable; sum 209. |
| Stable finding/mitigation integrity | 27 unique finding sections, 27 mitigation declarations and complete lifecycle/severity/principle/stakeholder/provider/evidence/mitigation/escalation fields. |
| Local links | All five unique relative link targets exist. External standards were retrieved as named; no claim that every external URL was independently link-crawled. |
| Rooted Event/source-reference locators | Referenced rooted paths exist; 226 explicit rooted/reference line ranges checked against actual file lengths. Out-of-range source and external-inventory endpoints were corrected. Bare-name references use the evidence register/class context. |
| Source freshness | Event HEAD, selected backend tracked-diff SHA-256 and OpenAPI SHA-256 still match the recorded evidence after assessment. |
| Markdown whitespace | Native Git whitespace check has no diagnostics; new-file diff exit status is distinguished from a whitespace error. |
| Language-server diagnostics | Markdown has no configured LSP server. This limitation is reported, not represented as a clean diagnostic result or repaired outside scope. |
| Product build/tests/manual exploitation | Not run: this is a report-only Tier 4 change, and no live-test environment was selected. Existing test assertions were read and their scope/weaknesses recorded. |
| Manual assessment | Critical secret-custody, replay, SSRF, regex, money, RLS and middleware findings independently corroborated against Event source. Security-workflow path-selection omission reproduced with its actual machine-consumed predicate. |
| Change scope | Only this maintained report was authored; pre-existing product/test/report changes remain untouched. No product fixes, migrations, dependency changes, commits or deployments. |

The report is **current for its recorded source/evidence revision** and still `changes-required`. Current means the assessment and reference ledger are complete for this snapshot; it does not resolve the 27 open findings or missing runtime, stakeholder and operational evidence.

### Evidence levels

- Design validation: supplied through the control/failure comparison, stakeholder duties and independent mitigation reasoning.
- Implementation traceability: supplied by the inspected source, configuration, documentation and test contents.
- Stakeholder validation: not supplied; no interviews, complaints, recovery usability study or shared-network abuse impact data were reviewed.
- Operational validation: not supplied; no launch host, production log/alert, actual realm, provider outcome, egress proof or restore rehearsal was reviewed.
- Theological validation: not claimed; no religious-legal determination is attempted.

### Minimum closure discipline

For any claim changed from Vulnerable/Improvable/Unverified to Protected, retain the original finding and reference IDs, identify the exact replacement code revision, and record a deterministic negative test or operational verification that exercises the actual surface. Subscribe to concurrency events before triggering the action; fixed sleeps, timing luck, mock-only permission mirroring and source-text tests are not acceptable closure evidence.

Use the repository's verification tiers when implementation is later authorized: sliced in-memory Domain/Application invariants, the touched project/provider phase gate, and the workstream's supported-provider/release checks. This report has no product edit, so those suites were not run merely to make an assurance claim.

## Escalation Needed

- Security experts: validate any confirmed access/SSRF/credential flaw and the candidate's unresolved scheme/provider boundaries in an authorized isolated environment.
- Operator and release owner: supply deployed identity/proxy/TLS/egress/key/worker profiles and decide named residual risks. A risk acceptance must identify owner, scope, expiry and revisit trigger.
- Privacy/legal specialists: determine jurisdiction-specific notification, processing, retention and incident duties; API source does not establish legal compliance.
- Qualified Sunni scholarly authority: only if remediation introduces a contested religious/legal requirement, finance ruling or public religious-content claim. Ordinary security recommendations here do not pronounce halal/haram or certify the product.

## Evidence Reviewed

### Repository-native evidence register

| ID | Source locator | What it supports |
| --- | --- | --- |
| E01 | `src/Explore.API/Program.cs`; `Hosting/ApiHostApplicationExtensions.cs:19-170`; `Hosting/ApiHostServiceCollectionExtensions.cs:85-175` | Dedicated API composition, middleware order, production/development branches and runtime mappings. |
| E02 | `src/Event.Standalone/Program.cs`; `Hosting/StandaloneHostApplicationExtensions.cs`; `Middleware/CombinedApiBridgeMiddleware.cs`; `src/Explore.API/Hosting/ApiHostRouteClassifier.cs` | Combined host, cookie bridge, route ownership, in-process dispatch and shared API enforcement. |
| E03 | `src/Explore.API/Extensions/AuthenticationExtensions.cs:311-337`; `Mcp/EventManagementMcpTools.cs:1-170`; `Mcp/EventMcpBounds.cs`; `Configuration/McpAdapterSettings*.cs` | MCP startup/runtime scope, explicit authorization policy and bounded public discovery. |
| E04 | `tests/Event.API.IntegrationTests/Features/McpAuthorizationTests.cs:1-210`; `EndpointAuthorizationMatrixTests.cs:1-180` | Test contracts for anonymous-safe MCP discovery, disabled/lifecycle outcomes and endpoint classification. Tests were read, not executed. |
| E05 | `src/Explore.API/Extensions/QuartzSchedulerExtensions.cs:190-217`; `Scheduling/QuartzSchedulerStatusEndpoint.cs`; `src/Explore.ServiceDefaults/Extensions.cs:173-209` | Scheduler authorization and separately mapped health/metrics surfaces. |
| E06 | `src/Explore.API/Middleware/CorrelationIdMiddleware.cs`; `SecurityHeadersMiddleware.cs`; `Extensions/RateLimitingExtensions.cs:55-140` | Correlation header bounds, defensive response headers and Testing limiter override. |
| E07 | `src/Explore.Persistence/PersistenceServicesRegistration.cs:86-88`; `src/Explore.Persistence/Schema/ProviderPrimitives/ExploreDatabaseMigrator.cs:82-88`; `src/Explore.Persistence/Security/ProviderPrimitives/PostgresTenantRowLevelSecurityModel.cs:114`; `docs/internal/SECURITY-MODEL.md:1122-1132`; `docs/internal/QUICK_REFERENCE.md:24,52-80`; `docs/internal/API.md:244-271` | RLS installation/session/role and pipeline assurance discrepancy; actual deployment is unverified. |
| E08 | `.github/workflows/security-tests.yml:79-113`; `.github/workflows/secret-scanning.yml`; `dependency-review.yml`; `codeql.yml` | Existing security automation and its path/no-op/scan-scope limits. These workflows were not executed or their remote settings verified. |
| E09 | `global.json`; `Directory.Packages.props`; `src/Explore.API/Explore.API.csproj`; `src/Explore.Application/Explore.Application.csproj` | Target framework, central declared versions and native CQS/Mapperly dependency context, not a clean vulnerability scan. |
| E10 | [Security policy](../../SECURITY.md); [launch consultation](i-vsd-v0-1-launch-consultancy-report.md); [architecture](../../docs/internal/ARCHITECTURE.md); [security model](../../docs/internal/SECURITY-MODEL.md) | Provider commitments, intended launch scope and implemented-versus-planned distinctions. |
| E11 | `.agents/skills/i-vsd/resources/` selected contracts/lenses; `AGENTS.md`; `AGENTS.local.md`; `.agents/CONTEXT_ENGINEERING.md`; API/auth rules; [IP governance](../../docs/internal/legal/IP_GOVERNANCE.md) | Assessment method, report identity, source isolation and documentation-only change constraints. |
| E12 | Reference repository HEAD, LICENSE, complete first-party text inventory, README/registry/page/action counts, ZIP central directory and sanitized functional catalogue | Exhaustive reference coverage at the pinned revision; static observations only. |
| E13 | `src/Explore.Persistence/Identity/LocalIdentityAuthService.cs:45-74`; `src/Explore.API/ExceptionHandling/LocalAuthenticationResultMapper.cs:21-30,67-78`; `src/Explore.Application/Configuration/LocalIdentityOptions.cs`; `src/Explore.Persistence/PersistenceServicesRegistration.cs:122-135` | Local enumeration, lockout and effective length/validator policy. |
| E14 | `src/Explore.Application/Features/Authentication/Local/Handlers/Commands/RequestLocalPasswordRecoveryCommandHandler.cs`; `src/Explore.Persistence/Identity/LocalIdentityLifecycleStore.cs`; `src/Explore.Infrastructure/Authentication/LocalIdentityLifecycleSmtpTransport.cs`; `tests/Event.Persistence.IntegrationTests/Identity/LocalIdentityAuthServiceTests.cs:54-76` | Recovery flow and service distinction; tests unrun. |
| E15 | `src/Explore.API/Extensions/AuthenticationExtensions.cs:85-251`; `src/Explore.API/Authentication/DynamicJwtBearerPostConfigureOptions.cs`; `tests/Event.API.IntegrationTests/Features/SecurityIntegrationTests.cs:187-300` | Keycloak context rule, Local signing and test-claim limitations. |
| E16 | `src/Explore.Application/Authorization/RequestAuthorization.cs`; `src/Explore.Infrastructure/Services/RuntimeAuthorizationProvider.cs`; `src/Explore.Application/Features/Users/Handlers/Commands/UpdateUserCommandHandler.cs`; `src/Explore.Application/DTOs/User/UpdateUserDto.cs`; `src/Explore.API/Controllers/UserController.cs` | Selected server guards and narrow profile assignment, not complete endpoint authorization certification. |
| E17 | `src/Event.Web.BffHosting/Security/EventBffRequestEnricher.cs`; `src/Event.Web.BffHosting/Security/BffProxyHeaderSanitizer.cs`; `src/Event.Web.BffHosting/Proxy/EventApiProxyExtensions.cs`; `src/Event.Standalone/Middleware/CombinedApiBridgeMiddleware.cs` | Cookie/antiforgery and server-owned privileged-header conversion; exact filesystem location checked in report validation. |
| E18 | `src/Explore.Infrastructure/Webhooks/WebhookEndpointSafetyPolicy.cs`; `WebhookDeliveryDrainService.cs`; `src/Explore.Infrastructure/Services/Registration/Providers/SubmissionSinks/WebhookRegistrationProviderSubmissionSink.cs`; `src/Explore.Infrastructure/InfrastructureServicesRegistration.cs:333-343,441-454,618-622,691-709,806-814` | DNS-versus-connection, redirects and the Web Push connector comparison. |
| E19 | `src/Explore.API/Controllers/AiAssistantController.cs:123-180`; `src/Explore.Infrastructure/Ai/AiProviderSettingsValidator.cs:166-234`; `OpenAiCompatibleChatProvider.cs`; `OpenAiResponsesChatProvider.cs`; `src/Explore.Infrastructure/Services/CerbosAdminEndpointValidator.cs`; `CerbosPolicyPackageService.cs` | Privileged destination and provider-byte budgets; operator/feature prerequisites retained. |
| E20 | `src/Explore.Application/Features/CustomProperties/CustomPropertyRuntimeValueValidator.cs:272-305`; `src/Explore.Application/Features/RegistrationSubmissions/RegistrationAnswerNormalizer.cs:89-104` | Unbounded regex versus native bounded alternative. |
| E21 | `src/Explore.Application/Services/EventResourceDocumentInspection.cs`; `src/Explore.Domain/Services/EventResources/EventResourceDocumentPolicy.cs`; `src/Explore.Application/Services/StorageContentSignaturePolicy.cs`; `src/Explore.Application/Services/StorageObjectContentReader.cs`; `src/Explore.API/Models/EventResourceFileResult.cs`; `src/Explore.API/Controllers/StorageObjectController.cs:357-397` | Specialized versus generic content inspection, authority and final delivery header limitations. |
| E22 | `src/Explore.Infrastructure/Storage/LocalFileStorageProvider.cs:316-358`; `S3FileStorageProvider.cs:96-119`; `src/Event.Wire.Contracts/ConfigurationPortability/ConfigurationPortabilityJsonCodec.cs:64-132`; `src/Explore.Application/Features/ConfigurationManifest/Importing/ConfigurationImportArtifactParser.cs` | Filesystem/codec bounds and deliberate limits; no complete SDK/package-chain assurance. |
| E23 | `src/Explore.Application/Services/AuthProviderConfigurationService.cs:264-345`; `src/Explore.Application/Features/InstanceOnboarding/Handlers/Commands/UpdateAuthProviderConfigurationCommandHandler.cs:102`; `src/Explore.Persistence/Configurations/Entities/SystemSettingConfiguration.cs:28`; `src/Explore.Persistence/Repositories/SystemSettingRepository.cs:175` | Google secret persisted as a settings value. |
| E24 | `src/Explore.API/Controllers/ExternalApiKeyController.cs:60-138`; `src/Explore.API/Middleware/IdempotencyMiddleware.cs:270-335,538-565`; `src/Explore.API/Middleware/ETagMiddleware.cs:25-101`; `src/Explore.API/Middleware/IdempotencyRequestIdentity.cs:27-33,167,248` | Raw secret replay, late buffer thresholds and explicit request-scope identity. |
| E25 | `src/Explore.Infrastructure/Services/Moderation/Coop/CoopReviewQueueProvider.cs:37-79`; `src/Explore.API/Extensions/RateLimitingExtensions.cs:579-621`; `src/Explore.API/Hosting/ApiHostApplicationExtensions.cs:64-115` | Response/admission/loopback and dedicated-host HSTS ordering. |
| E26 | `src/Explore.Domain/ValueObjects/Money.cs`; `MinorUnitMath.cs`; `src/Explore.Domain/RegistrationOrderLine.cs:85-137`; `src/Explore.Persistence/Repositories/RegistrationInventoryRepository.cs:214-244`; `src/Explore.Application/Services/Registration/RegistrationRefundService.cs:35`; `RegistrationPaymentAttemptClaimService.cs`; `RegistrationPaymentCheckoutDispatchService.cs` | Positive checked amounts/quantity, int-aggregate limits and durable provider work. |
| E27 | `src/Explore.Persistence/QueryFilters/QueryFilterExtensions.cs`; `src/Explore.Persistence/Repositories/GenericRepository.cs`; `src/Explore.Application/Mappings/EventMapper.Projections.cs:25,315`; `src/Explore.Application/DTOs/Event/EventDto.cs`; `src/Explore.Infrastructure/Identity/AdminContext.cs:77` | Bypass/identity-map/field-contract and current-admin boundaries. |

Bare filenames in detailed sections refer to the same named directories/classes in this register or the explicitly rooted construction-family table. They are not external code references. Actual existing-test contracts cited throughout the analysis were read; test sources were not modified, executed or represented as passing. The failed graph/LSP lookup and any speculative filename miss supplied no protection evidence.

### Official standards and framework sources

Sources were retrieved on 2026-10-01. Summaries are independently written; third-party implementation examples are not remediation templates.

| ID | Official source | Use and boundary |
| --- | --- | --- |
| S01 | [OWASP API Security Top 10:2023](https://api-security.owasp.org/editions/2023/en/0x11-t10/) | API object/property/function authority, authentication, resource/business-flow abuse, SSRF, misconfiguration, inventory and unsafe provider consumption. |
| S02 | [OWASP ASVS](https://owasp.org/www-project-application-security-verification-standard/) | Retrieved project page identifies stable 5.0.0. It informs a future verification baseline; this review does not claim complete ASVS coverage or invent requirement numbers. |
| S03 | [NIST SP 800-63B-4](https://pages.nist.gov/800-63-4/sp800-63b.html#passwordver) | Password length, full-password blocklists, account-bound rate limiting, salt/work factor and avoidance of arbitrary composition/periodic changes. Voluntary engineering reference here, not a claim of legal applicability or AAL certification. |
| S04 | [OWASP SSRF prevention](https://cheatsheetseries.owasp.org/cheatsheets/Server_Side_Request_Forgery_Prevention_Cheat_Sheet.html) | Destination allowlisting, redirect control, A/AAAA resolution, private/link-local denial and network segmentation. |
| S05 | [OWASP file upload guidance](https://cheatsheetseries.owasp.org/cheatsheets/File_Upload_Cheat_Sheet.html) | Layered type/signature/name/size validation, storage outside webroot and decompressed-byte budgeting. |
| S06 | [ASP.NET Core JWT bearer authentication](https://learn.microsoft.com/aspnet/core/security/authentication/configure-jwt-bearer-authentication?view=aspnetcore-10.0) | Official documentation retrieved through Context7; signature, issuer, audience and expiry are independent verification obligations. |
| S07 | [Reference repository](https://github.com/AlexGoOn/the-most-vulnerable-dotnet-app/tree/60d060faf08079887e71a98498fe9a9d623e8ffc) | Source identity and access provenance only; educational examples are not standards or proof of equivalent Event defects. |

The reference catalogue uses CWE classifications and some OWASP Web Top 10:2021 names for historical traceability. Event conclusions use API Top 10:2023 as the API-specific mapping. No provisional future Top 10 edition, exact CVE, ASVS requirement or framework default is invented.

## Missing Evidence

- Actual launch topology/database/provider matrix, effective identity realm/password/MFA policies and admin recovery.
- Executed candidate-revision test/build/security scan outputs, SBOM/dependency-advisory results, source-history secret scan and published-container/native library posture.
- Runtime cookie/bearer/antiforgery behavior in Split and Combined hosts; full out-of-scope frontend active-content consumption.
- Effective ingress allowlists, proxy addresses, original-IP behavior, TLS termination, internal listeners and cloud metadata/egress restrictions.
- Representative logs/scopes/traces/exporters, log injection behavior, redaction of raw exception data, retention and responder access.
- Replica-wide abuse budgets, workload/cost tests, upload/storage/parser limits and dependency-failure behavior.
- Executed key/identity/media/erasure-authority restore, revocation after restore, actual provider retries/reconciliation and incident acknowledgment.
- Stakeholder feedback, independent penetration testing and legal/scholarly determinations where applicable.

## Context Inventory

- Available: repository policies/architecture/operator docs; API code/config/test contracts; existing I-VSD launch and domain reports; the user-specified external local repository and its license.
- Considered integrations: no usable knowledge-graph tool was discoverable despite initial and broadened discovery attempts. Native file search/read and bounded symbol retrieval were used; a C# LSP symbol request timed out and supplied no evidence.
- Retrieval: read-only functional-catalogue research, independent Event subsystem inspections, official OWASP/NIST retrieval and Context7 framework corroboration.
- Unavailable: no connected production environment, realm export, incident/support packet, remote CI evidence or stakeholder consultation was supplied.
- Intake: recommended repository-only evidence scope was presented; the question timed out. Work proceeds under the original report-only request and the recommended scope, with runtime gaps explicit rather than assumed.
- Change boundary: this subject report only; pre-existing working-tree changes and unrelated reports are preserved.

## Review Lifecycle

| Date | Previous status | New status | Trigger | Evidence/replacement |
| --- | --- | --- | --- | --- |
| 2026-10-01 | none | draft | Initial complete-reference API assessment requested; source and hosting inventory established | Event and reference revisions named in metadata; operational validation outstanding |
| 2026-10-01 | draft | current | All 209 records compared; independent subsystem evidence reconciled, including actual PostgreSQL installer/session mismatch | 27 findings/mitigations, 53-family/209-record integrity, links/paths/ranges and unchanged source fingerprints verified; disposition remains changes-required |

### Maintained-report update procedure

1. Reuse this file only for the same Event API security assessment. Do not create a competing report per iteration.
2. Capture Event commit, relevant working-tree content/diff fingerprints, reference revision, supported API hosts/schemes/providers and evidence cutoff.
3. Preserve `REF-*`, analysis keys, `IVSD-F*` and `IVSD-M*`. Add new reference examples with new IDs; keep resolved/superseded history instead of renumbering.
4. Mark affected evidence stale when code, defaults, endpoint inventory, identity authority, tenant boundaries, serializer/network/file sink, provider matrix or launch topology changes.
5. Re-evaluate the affected actual path and tests; a date edit, test filename or stale scan does not make a report current.
6. Record disposition changes, prerequisites, new negative/runtime evidence and named ownership. A removed mechanism may become Not applicable, with its removal revision retained.
7. Accept residual risk only with owner, scope, deadline, compensating control and revisit trigger; do not equate acceptance with Protected.
8. Record a material lifecycle transition and rerun coverage/link/whitespace checks. Keep the report current for its evidence revision even while findings remain open.

Refresh immediately for a new authentication scheme, changed role/membership authorization, secret authority/rotation, body authority field, raw SQL/parser/evaluator, outbound provider/URL, upload/delivery path, idempotency/replay behavior, database migration/provider support, proxy/TLS/topology, diagnostic export or public security claim. A new launch candidate needs new operational evidence even if the report's prose remains valid.
