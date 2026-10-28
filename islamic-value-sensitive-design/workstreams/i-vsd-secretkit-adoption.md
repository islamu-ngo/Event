# SecretKit adoption: credential stewardship and truthful readiness

Last Updated: 2026-10-25

## Review Metadata

- Mode: planning
- Subject: Replace Event-owned provider mechanics with the vendored SecretKit runtime libraries.
- Workstream: secretkit-adoption
- Report kind: implementation-planning assessment
- Report status: current
- Disposition: plan-aligned
- Evidence cutoff: 2026-10-25
- Reviewed input: completed secretkit-adoption plan/tasks/context revision P1, dated 2026-10-25; Event working tree based on `fefcea5f16e3bd723e22076d505a65c696c4e40b`; SecretKit candidate `0.1.0-local.20261024.1`; evidence packet E1.
- Supersedes: none. The paused February secret-management workstream is historical, not this assessment.

## Executive Summary

Adopting a shared provider library can remove duplicated credential transport without surrendering Event's responsibility for tenant isolation, deployment authority, and safe recovery. SecretKit supplies provider-native acquisition and protected material ownership. Event must still decide which process may acquire a credential, which tenant binding wins, whether a capability is required, and when a prepared client is allowed to serve requests.

The principal recommendation is to remove the old Secrets projects, not rename them or reproduce their provider facade. Preserve the three explicitly selected authorities, isolate optional capabilities, and replace broad configuration reload assumptions with truthful acquisition and activation evidence. A successful provider read, local configuration publication, and deployment-wide rotation are separate facts.

This current report is plan-aligned with triad P1 and supports design reasoning and source traceability only. Package checksums passed during planning; no Event integration, product test, provider permission challenge, image build, or operational recovery was performed. The user explicitly approved SecretKit Hosting's narrow per-process authentication-maintenance exception on 2026-10-25. That choice and every material finding/mitigation are mapped into the completed plan/tasks; implementation approval remains separate.

## Scope

Server-side acquisition, bootstrap, runtime resolution, selected provider writes, authentication lifetime, cache/reference isolation, diagnostics, packaging, and operator recovery. No change to monetization, moderation, content ranking, public secret-management UI, or provider provisioning is requested.

## Claim Boundary

This is provider-responsibility reasoning under the I-VSD framework, not a religious ruling, security certification, or proof of operational reliability. Public interface facts and independently written functional observations are admissible evidence; no third-party implementation source or decompiled code was used. SecretKit's preview status remains explicit.

## Findings

### IVSD-F001: Library replacement must preserve each process's authority

An API process, BFF, migration worker, or tenant must not acquire credentials belonging to another purpose merely because one reusable library now serves them all. A broader shared identity would move the cost of an implementation shortcut onto every affected community.

Event controls source selection, process roots, tenant bindings, cache qualification, and credential injection. Retain exact-root admission before authentication and repeat binding checks before cache access. Treat provider ACLs as an independent operator obligation: application admission does not prove those ACLs exist.

- Lifecycle / severity / claim type: accepted / high / prospective security design constraint.
- Principles / domains: Amanah and La Darar / technical, governance, operations.
- Stakeholders: attendees, tenant administrators, instance operators, maintainers.
- Evidence / validation: E02 and E03 below; implementation traceability, not executed assurance.
- Mitigation: **IVSD-M001**, retain single-authority selection, consumer admission, tenant-qualified caches, retained-reference checks, and independent ACL qualification.
- Owner / next validation: integration maintainer; adversarial authority and isolation tests.
- Escalation: implementation must stop if an SDK gap requires broader identity or fallback authority.

### IVSD-F002: Renewal failure must not silently create new authority

A lost renewal response can leave the provider's state unknown. Replaying a login, trying another identity, or treating the failure as an absent optional setting would obscure the operator's actual authority and recovery duties. A one-use bootstrap credential may already be consumed.

SecretKit separates explicit login, token maintenance, and authorized recovery. Event must preserve original lifetime evidence, retain dispatch uncertainty, and report recovery-required without provider prose. The user approved only per-process authentication maintenance as an exception to the scheduler rule; business-secret polling, business jobs, automatic relogin and fallback identities remain excluded.

- Lifecycle / severity / claim type: accepted / high / resolved design decision with implementation obligations.
- Principles / domains: Amanah, Sidq, and avoidance of excessive uncertainty / technical and operational.
- Stakeholders: self-hosters and people depending on service availability.
- Evidence / validation: E03, E04, E06; interface/source traceability and official protocol guidance.
- Mitigation: **IVSD-M002**, explicit selected-principal login, bounded maintenance, no automatic replay/relogin, fail-closed recovery, and documented operator replacement.
- Owner / next validation: platform maintainer; deterministic lifetime/uncertainty tests and identical narrow rule-twin updates.
- Escalation: a proposal to broaden this exception or automate recovery requires a new explicit decision.

### IVSD-F003: Protected buffers do not protect exported strings

Disposing an SDK read cannot erase strings already copied into configuration, options, cached results, or downstream clients. Claiming otherwise would give operators false confidence and make diagnostic exposure more likely.

Event controls export scope and instrumentation. Use one acquired generation for each multi-key construction, export only explicitly mapped values, dispose every owned SDK result, and preserve zero-secret output tests across logs, activities, metrics, stdout/stderr, health, and HTTP errors. Existing Application results that intentionally contain strings remain a documented plaintext boundary, not a promise of process-wide erasure.

- Lifecycle / severity / claim type: accepted / high / confidentiality design constraint.
- Principles / domains: Amanah, rights of people, avoidance of Tajassus / technical and operational.
- Stakeholders: credential owners, tenants, operators, support personnel.
- Evidence / validation: E02-E04; design and implementation traceability.
- Mitigation: **IVSD-M003**, narrow export, explicit disposal/ownership, bounded retention, safe diagnostics, dynamically generated test canaries.
- Owner / next validation: runtime integration maintainer; output-capture tests with positive capture controls.
- Escalation: no diagnostic endpoint or support artifact may expose values or sensitive provider coordinates.

### IVSD-F004: Optional capability failure must not become global failure or false success

Missing mail or analytics credentials should not invalidate unrelated required database startup. Conversely, an unreachable provider is not proof that an optional credential is simply unconfigured. These distinctions affect availability and truthful operator diagnosis.

Use separate required groups per enabled capability. Preserve non-resolved outcomes and existing readiness semantics. Publication of a complete local generation must not be described as simultaneous remote reads, downstream client activation, or fleet convergence.

- Lifecycle / severity / claim type: accepted / high / reliability and truthful-state constraint.
- Principles / domains: La Darar, Sidq, Ihsan / technical, design, operations.
- Stakeholders: users of unaffected capabilities and self-hosters diagnosing partial failures.
- Evidence / validation: E02-E04.
- Mitigation: **IVSD-M004**, independent capability groups, bounded statuses, current-intent activation fences, and coordinated restart/convergence policy.
- Owner / next validation: host/runtime maintainers; required/optional, refresh, and stale-completion tests.
- Escalation: no library snapshot or health response can stand in for deployment-wide acknowledgement.

### IVSD-F005: Local package success must not be advertised as production qualification

The supplied MIT archives are redistributable with their notices, but restore and offline producer results do not establish Event behavior, actual provider ACLs, platform compatibility, or recovery. Removing attribution or overstating qualification would breach both licensing stewardship and truthful communication.

Retain the complete bundle and its provenance, use exact versions and isolated-cache restore evidence, reconcile Refit dependencies, and keep actual image/native/AOT evidence separate. Do not introduce new provider or management packages under the label of full adoption.

- Lifecycle / severity / claim type: accepted / medium / supply-chain and claim-integrity constraint.
- Principles / domains: Sidq, promise-keeping, Amanah, Ihsan / strategic, governance, evaluation.
- Stakeholders: adopters, maintainers, dependency rightsholders.
- Evidence / validation: E01, E04-E06; archive integrity checked, integration untested.
- Mitigation: **IVSD-M005**, retained MIT notices, immutable candidate identity, reviewed locks/license scan, and bounded readiness/release claims.
- Owner / next validation: dependency/release maintainer; actual consumer restore and final assurance.
- Escalation: changed bytes under the same version, incompatible dependency terms, or unsupported required SDK behavior blocks adoption.

## Recommendations

Adopt the selected library interfaces directly at outer-layer boundaries. Keep Event's business-facing contracts independent of SDK types. Remove obsolete HTTP, SDK, factory, and broad refresh implementations only after their Event-owned invariants have replacement owners and tests.

Rejected alternatives are silently switching authority on provider failure, introducing a generic replacement Secrets library, treating all supplied selections as one required capability, and treating a reload notification as successful rotation. Each either expands authority, recreates duplication, or misstates what has been established. Demand-only token renewal was rejected because idle expiry can impose avoidable recovery on operators. A clustered singleton business job was rejected as evidence of per-process renewal.

## Stakeholders

Attendees and organizers rely on confidentiality and availability without controlling credentials. Tenant administrators rely on scope isolation. Instance operators own injected credentials, provider ACLs, lifetime policy, backups, and fleet recovery. Maintainers own integration correctness and truthful support claims. Dependency rightsholders retain their terms.

## I-VSD Principles And Domains

Trust is expressed through purpose-bound acquisition and explicit ownership; non-harm through fail-closed authority without unnecessarily disabling unrelated capabilities; truthfulness through distinct observed outcomes; excellence through removal of duplication and executable invariants. Strategic and governance review covers licensing and self-hosting promises. Evaluation lacks stakeholder and deployed operational evidence.

Riba, religious-content decisions, advertising, and moderation are not applicable: this work changes no such policy. No new religious-legal conclusion requires scholarly escalation.

## Validation Gaps

Event product tests, real provider permissions and lifetime policy, cross-replica recovery, actual images on supported architectures, and the producer's trim/AOT profile remain unverified. Package-only producer evidence is not inherited as Event acceptance.

## Escalation Needed

No material planning decision remains unresolved. Obtain separate authority before destructive provider actions, credential revocation, data resets, or changes outside Event. Escalate incompatible distribution terms or a missing required SDK capability rather than adding a raw-HTTP escape hatch. The narrow renewal decision does not authorize automatic recovery or business scheduling.

## Evidence Reviewed

| ID | Evidence | Level and limit |
|---|---|---|
| E01 | `docs/internal/SECRETKIT_LOCAL_PACKAGES.md`, public local-package guide, `vendor/secretkit/packages.sha256`, four archive nuspec files | Current archive checks passed; prior restore/build claims are dated records only. |
| E02 | `src/Explore.Secrets/Configuration/SecretAuthorityConfiguration.cs`, `SecretAcquisitionPolicy.cs`, `Services/SecretResolver.cs`, Application/Domain contracts | Existing policy inspected, not executed. |
| E03 | API/BFF host registration, BFF provider, migration configuration, Persistence design-time factories, `SECRETS.md` | Separate acquisition entrypoints and specialized Data Protection custody identified. |
| E04 | Local SecretKit public package guides and `docs/internal/local/infisical-runtime.json`; profile hash `f6852109621539a12ea29c328c077338b20d6198ca974df45c8cf7d4d25537b2` matches Event's provenance record | Interface facts only; local producer HEAD differs from package source label, so installed packages remain execution truth. |
| E05 | `AGENTS.md`, relevant secrets/bootstrap/CI intents, IP governance, scheduler rule, planning/I-VSD contracts | Normative constraints, not operational proof. |
| E06 | Official Infisical Universal Auth and Microsoft configuration/DI disposal documentation; exact Refit factory package listing, retrieved 2026-10-25 using Anysearch and Context7. Provider web search was invoked but supplied no usable additional content. | Protocol/framework/package guidance, not validation of this integration; source mapping is verified from Event's own NuGet.Config and provenance record. |
| E07 | `dev/active/secretkit-adoption/secretkit-adoption-plan.md`, `secretkit-adoption-tasks.md`, `secretkit-adoption-context.md`, revision P1; user maintenance answer on 2026-10-25 | Completed design/task traceability; not implementation approval. |

## Missing Evidence

No stakeholder interviews, deployment audit, incident record, native Event permission challenge, or integration test result was supplied or produced. No access to actual credential stores was needed.

## Context Inventory

One Event workstream will own the plan/tasks/context triad. The paused February plan describes superseded architecture and is not resumed. The existing local package/deployment edits and unrelated staged issue-template rename belong to other work and must be preserved.

## Common Overlooked Failures And Outcomes

Per-process credentials cannot be renewed by a cluster-wide singleton job on another replica. A denied or malformed native read cannot be converted into absence. Cancellation after dispatch cannot prove a write did not happen. A disposed read cannot invalidate another caller's copy. Data Protection must keep independent admission pins/floors rather than entering bulk export. A new package version or a changed source document cannot silently requalify earlier bytes.

## Review Lifecycle

| Date | Previous status | New status | Trigger | Evidence/replacement |
|---|---|---|---|---|
| 2026-10-25 | none | draft | Shared evidence packet established; maintenance decision remains open. | E1 / E01-E06 |
| 2026-10-25 | draft | current / plan-aligned | User selected narrow maintenance exception; completed triad reviewed and material mappings reconciled. | P1 / E1 / E07 |

## Planning Handoff

- Workstream: secretkit-adoption
- Status: current
- Reviewed input: completed triad P1 and evidence packet E1.
- Findings and mitigations: IVSD-F001 -> IVSD-M001; IVSD-F002 -> IVSD-M002; IVSD-F003 -> IVSD-M003; IVSD-F004 -> IVSD-M004; IVSD-F005 -> IVSD-M005.
- Required plan mappings: IVSD-F001/M001 -> S01/S02/S04/S09, tasks 1.1-1.3/2.1-2.3/3.1-3.3/4.1-4.3; F002/M002 -> S05/S08/S10, tasks 1.2/4.1/4.4/4.5; F003/M003 -> S06/S10, tasks 1.1-1.3/2.1-2.3/4.1-4.4; F004/M004 -> S03/S07/S11, tasks 1.2-1.3/4.2-4.5; F005/M005 -> S12, tasks 1.1/1.4/5.1-5.4. All are design-accepted implementation obligations, not resolved implementation findings.
- Escalations required before: user approval before implementation; attributable test failures before implementation completion; native/platform qualification before corresponding release claims.
- Refresh triggers: changed authority/fallback policy, identity scope, maintenance/recovery, export/retention, activation, telemetry, dependency bytes/terms, or material task mappings.
