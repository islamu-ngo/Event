<!-- ABOUTME: Current planning handoff for provider responsibility in the secrets authority refactor. -->
<!-- ABOUTME: Binds stable findings to the exact reviewed plan and task revisions. -->

# I-VSD Review — Secrets Authority And Control Plane Refactor

Last Updated: 2026-08-30 Europe/Brussels

## Review Metadata

- **Mode:** planning
- **Subject:** Secret authority, provider responsibility, and self-hoster recovery
- **Workstream:** `secrets-refactor-control-plane`
- **Report kind:** planning handoff
- **Report status:** current
- **Disposition:** plan-aligned
- **Evidence cutoff:** 2026-08-30
- **Reviewed input revision:**
  plan `sha256:fed15e71ffeb739aa2dd2e62ef06317fcdde060420e9a7c4c9093105e295f6c9`;
  tasks `sha256:10a49e6dcfd55e39234dba068eed6a22634f1ed06f3419f19861d7b425772f84`;
  combined plan then tasks
  `sha256:825b965e4937a7544ec99ea93c20456f2737c43441d45d44ac00549586805757`
- **Supersedes:** same-path stale report
  `sha256:8a58a7d59ac8d826c61222f3822966c65c08206c269bc463e93afbcc2f1daaf9`

## Scope

This review covers provider-controlled choices in secret source authority,
secret-zero handling, tenant/instance references, required and optional capability
state, diagnostics, rotation, recovery, self-hosting, configuration portability,
and operator support. It covers Aspire, Standalone, Compose, Infisical,
environment injection, runtime resolution, API/HAL/BFF/UI status, and operational
documentation.

It does not evaluate the moral character of operators, provide a religious-legal
ruling, certify security outcomes, or approve implementation. It does not expand
the configuration-manifest product or select future secret providers.

## Claim Boundary

This report applies Islamic value-sensitive reasoning to software-provider
responsibility: entrusted confidentiality, truthful representation of capability
state, justice between tenants, self-hoster autonomy, harm prevention, and
accountable recovery. It does not declare the design halal, haram, wajib, or
Sharia-compliant. Any such conclusion requires qualified Sunni scholarly review.

This fresh planning-mode revalidation is bound to the exact plan/tasks bytes under
`Evidence Reviewed`. It confirms the corrected runtime/rotation split, replica
overlap/restart/partial-activation/stale behavior, same-PR operator contracts,
provider×topology and five-database evidence, secret-free portability, and the
destructive local-development authority boundary. The report is `plan-aligned` for
those bytes; no product implementation or CTO approval follows from this record.

The user separately authorized disposal/recreation of whole local-development
databases and volumes when needed for the clean migration path. This excludes
production, shared, staging, CI evidence, external-provider/Infisical state,
deployment secret stores, and unnamed targets; exact target identity remains an
execution-time safety gate. Product implementation approval is still absent.

Authority remains separated: I-VSD owns provider-responsibility findings,
mitigations, evidence levels, and escalation; the implementation plan owns behavior,
architecture, scenarios, sequencing, and mappings; `tasks.md` owns execution;
`context.md` owns current state; the CTO review owns technical readiness; the user
owns scope and implementation/data-disposal approval; and qualified authorities own
religious-legal determinations.

## Findings

### IVSD-F001 — Ambiguous source authority can conceal unsafe fallback

- **Lifecycle:** open
- **Severity:** blocker
- **Claim type:** provider-controlled architecture and operator truthfulness
- **Principle/domain:** amanah (entrustment), harm prevention; architecture and
  operations
- **Stakeholders:** self-hosters, tenant administrators, end users, support staff
- **Provider-controlled decision:** whether an unavailable selected source silently
  falls back to a lower-authority value
- **Evidence:** `BootstrapSecretLoader` currently uses Infisical, environment, then
  configuration; `.env.example` documents compatibility loaders; the rewritten
  plan defines `SCN-SEC-001`
- **Validation level:** repository and plan evidence; implementation not validated
- **Mitigation:** `IVSD-M001`
- **Owner/next validation:** Phase 1 invariant test and implementation review
- **Escalation boundary:** security/operations, not scholarly

### IVSD-F002 — Diagnostics can betray entrusted secret material

- **Lifecycle:** open
- **Severity:** blocker
- **Claim type:** confidentiality and preventable harm
- **Principle/domain:** amanah, privacy, non-maleficence; data, operations, support
- **Stakeholders:** deployers, tenants, users whose services depend on credentials,
  incident responders
- **Provider-controlled decision:** whether provider bodies, exceptions,
  coordinates, identifiers, tokens, or values enter output channels
- **Evidence:** bootstrap stderr and Infisical source exception logging; rewritten
  `SCN-OBS-001`
- **Validation level:** source reviewed; repository-wide output behavior not proven
- **Mitigation:** `IVSD-M002`
- **Owner/next validation:** Phases 1 and 3 canary-output tests
- **Escalation boundary:** security/privacy, not scholarly

### IVSD-F003 — Weak rotation and recovery shifts disproportionate harm to self-hosters

- **Lifecycle:** open
- **Severity:** critical
- **Claim type:** self-hosting autonomy and operational justice
- **Principle/domain:** removal of hardship, accountability, stewardship;
  operations and governance
- **Stakeholders:** small self-hosters, volunteer administrators, hosted operators,
  affected communities
- **Provider-controlled decision:** whether rotation claims reflect actual provider
  capabilities and whether rollback/rerun/restore instructions are executable
- **Evidence:** current docs lack one coherent lifecycle; old plan assumed universal
  zero-downtime versioning; current `SCN-OPS-001` and `SCN-ROT-001`–`SCN-ROT-003`
  assign overlap, partial activation, delayed revocation, restart, stale-replica,
  setup-authority, cleanup, and recovery to `SEC-221`–`SEC-226`, `SEC-401`,
  `SEC-402`, and `SEC-404`
- **Validation level:** documentation gap verified; recovery behavior not validated
- **Mitigation:** `IVSD-M003`
- **Owner/next validation:** executable replica convergence and operator validation
  in Phases 4 and 6
- **Escalation boundary:** operations/product responsibility, not scholarly

### IVSD-F004 — Tenant crossover violates entrusted authority and equal treatment

- **Lifecycle:** open
- **Severity:** blocker
- **Claim type:** tenant justice and authorization
- **Principle/domain:** justice, amanah; architecture, security, governance
- **Stakeholders:** all tenants and their administrators/users
- **Provider-controlled decision:** scope-qualified references, cache keys,
  repository filters, and server-derived authority
- **Evidence:** current tenant-to-instance fallback and existing binding tests;
  hostile concurrency remains unproven; rewritten `SCN-TEN-001`
- **Validation level:** partial repository evidence
- **Mitigation:** `IVSD-M004`
- **Owner/next validation:** Phase 2 real-provider concurrency tests
- **Escalation boundary:** security, not scholarly

### IVSD-F005 — Silent degradation misrepresents actual capability

- **Lifecycle:** open
- **Severity:** critical
- **Claim type:** truthful UX/operations and informed choice
- **Principle/domain:** sidq (truthfulness), transparency, accountability; product,
  operations, support
- **Stakeholders:** operators, tenant administrators, users of optional features
- **Provider-controlled decision:** whether unavailable, unauthorized, invalid, and
  unconfigured are collapsed into “feature disabled”
- **Evidence:** current adapters/resolver return null for multiple failure classes;
  rewritten `SCN-SEC-002` and `SCN-CAP-001`
- **Validation level:** source reviewed; future contract not implemented
- **Mitigation:** `IVSD-M005`
- **Owner/next validation:** Phase 1 typed outcomes and Phase 3 status tests
- **Escalation boundary:** product/security, not scholarly

### IVSD-F006 — Secret-bearing portability transfers entrusted material

- **Lifecycle:** open
- **Severity:** blocker
- **Claim type:** confidentiality, portability, and self-hoster autonomy
- **Principle/domain:** amanah, rights, promise-keeping; architecture, product,
  operations
- **Stakeholders:** self-hosters, tenant administrators, support staff, downstream
  operators
- **Provider-controlled decision:** whether configuration export or control-plane
  contracts expose values, reversible ciphertext, tokens, credentials, or sensitive
  provider coordinates
- **Evidence:** `SCN-MAN-001` and current `SEC-301`–`SEC-305`
- **Validation level:** plan and task traceability; implementation not validated
- **Mitigation:** `IVSD-M006`
- **Owner/next validation:** Phase 5 executable contract and boundary tests
- **Escalation boundary:** security/product responsibility, not scholarly

### IVSD-F007 — Destructive migration authority can exceed consent

- **Lifecycle:** accepted
- **Severity:** blocker
- **Claim type:** consent, authority, and irreversible operational harm
- **Principle/domain:** amanah, justice, non-harm; operations and governance
- **Stakeholders:** the user, local developers, self-hosters, and anyone whose data
  could be reached by an incorrectly identified target
- **Provider-controlled decision:** which databases, volumes, Data Protection
  material, or external stores an implementation agent may destroy
- **Evidence:** explicit user authorization and exclusions in plan Section 13,
  `GATE-003`, and current `SEC-104`–`SEC-106`
- **Validation level:** user decision and plan traceability; no target has been
  identified or destroyed
- **Mitigation:** `IVSD-M007`
- **Owner/next validation:** `GATE-003` and immediate pre-execution target proof in
  Phase 2
- **Escalation boundary:** user authority and security operations, not scholarly

## Recommendations

### IVSD-M001 — Make source authority explicit and fail closed

Require one source mode for each deployment/secret class. Treat a selected source's
unavailable, unauthorized, invalid, and absent outcomes distinctly. Never fall
back to an unselected source merely to preserve apparent availability.

### IVSD-M002 — Establish a zero-secret output boundary

Represent failures with bounded reason codes and safe remediation references.
Exclude provider payloads, exception messages, coordinates, identifiers, tokens,
credentials, values, and reversible material from every output and support path.

### IVSD-M003 — Give operators truthful lifecycle and recovery contracts

Document purpose-specific candidate, validation, activation/reload, verification,
rollback, revocation, restart/maintenance, rerun, setup-secret cleanup, backup,
restore, and break-glass behavior for each supported topology. Do not promise zero
downtime or multi-replica consistency where the provider/consumer and deployment
contract do not support it.

### IVSD-M004 — Derive tenant authority server-side and test hostile concurrency

Qualify cache/reference ownership by authenticated tenant and instance scope,
preserve repository isolation, avoid query-filter bypasses, and test simultaneous
cross-tenant resolution/mutation on supported providers.

### IVSD-M005 — Expose truthful, non-sensitive capability state

Classify required/core and optional capabilities. Required failures block startup
or activation; optional failures affect only their owner. Status distinguishes
unconfigured, degraded, and failed closed without revealing source coordinates.

### IVSD-M006 — Preserve a closed, value-free portability boundary

Keep configuration manifests and all API/HAL/BFF/generated-client/UI surfaces
value-free and allowlisted. Omit secret values, reversible material, source
credentials, and sensitive coordinates while truthfully indicating omission.

### IVSD-M007 — Constrain destructive authority and fail on ambiguity

Permit disposal only for whole, specifically named LOCAL DEVELOPMENT databases and
volumes required by the clean migration path. Immediately before execution, prove
environment, provider, database/container identity, and volume/path are local and
non-shared. Production, shared, staging, CI evidence, external-provider/Infisical
state, deployment secret stores, unnamed targets, and every ambiguous target remain
outside authorization.

### Rejected Alternatives

- Rejected database ciphertext because it violates the repository source-of-truth
  rule and expands breach/recovery responsibility.
- Rejected silent fallback because availability obtained through ambiguous
  authority misleads operators and can conceal compromise.
- Rejected universal rotation state because it promises capabilities providers and
  consumers do not share.
- Rejected a duplicate secret CRUD UI because deployment-owned values should not be
  transferred into browser/application responsibility.
- Rejected row-only deletion and broad environment reset authority: the accepted
  clean path recreates whole proven local-development targets, while every shared,
  staging, CI, production, external-provider, deployment, unnamed, or ambiguous
  target remains forbidden.

## Stakeholders

| Stakeholder | Interest and potential burden |
|---|---|
| Self-hosters | Clear setup, low secret-zero burden, reliable recovery, no hidden provider lock-in |
| Hosted operators | Deterministic authority, rotation at scale, auditable safe metadata |
| Tenant administrators | Equal isolation, truthful capability state, no cross-tenant discovery |
| End users | Continuity and confidentiality of services dependent on credentials |
| Developers/support | Actionable diagnostics without access to secret material |
| Security/incident responders | Revocation, recovery receipts, bounded blast radius, no log leakage |
| Local developers | Exact destructive target proof, no accidental loss outside authorized local stores |

## I-VSD Principles And Domains

- **Amanah (entrustment):** credentials and tenant authority are entrusted data and
  power; provider-controlled systems must minimize exposure and ambiguity.
- **Sidq (truthfulness):** status and runbooks must reflect actual authority and
  failure rather than presenting silent fallback as health.
- **Justice:** tenant isolation and equal failure behavior must not privilege or
  expose one tenant through another's cache/reference path.
- **Removal of hardship:** self-hosters need viable Standalone and explicit recovery
  paths, not enterprise-only operational assumptions.
- **Accountability:** mutation/rotation/recovery evidence should be value-free,
  attributable, and useful without creating a surveillance-style read ledger.

The relevant domains are architecture, data, UX/status, operations, governance,
support, portability, and evaluation.

## Common Overlooked Failures And Outcomes

- A provider's error body contains the attempted secret and is copied into stderr,
  traces, or support bundles.
- A selected provider outage silently activates an old environment value and
  appears healthy.
- A local cache key omits tenant/source identity and serves another tenant's
  result.
- Rotation revokes the old credential before the consumer validates the candidate.
- One replica reports activation and triggers revocation while another replica is
  stale, failed, or still using the old credential.
- A stale replica continues dependent work after the overlap deadline instead of
  draining, restarting, or failing closed.
- An export omits values but includes sufficiently sensitive provider coordinates
  or references to aid discovery.
- A single-instance generated setup secret is used in a multi-replica deployment
  and produces inconsistent authority. This risk is explicitly owned by
  `IVSD-F003` / `IVSD-M003`, `SCN-OPS-001`, `SEC-401`, and `SEC-404`; implementation
  evidence remains absent.
- Recovery documentation assumes Postgres/Infisical even though Standalone SQLite
  is the minimum topology.
- A local reset command reaches a shared, staging, CI, production, external-provider,
  deployment, unnamed, or ambiguous target.

## Validation Gaps

- No stakeholder review from small/community self-hosters has been collected.
- No fresh implementation evidence exists for deterministic source authority,
  runtime typed status, hostile tenant concurrency, output canary scans, or
  rotation rollback.
- Consumer categories and the automated/operator ownership boundary are inventoried,
  but executable overlap/restart/partial-activation/stale-replica evidence is absent.
- No destructive command has recorded immediate target identity and local/non-shared
  proof; authorization alone is not execution evidence.
- No implementation, stakeholder, or operational evidence yet validates the planned
  mitigations in use.

## Escalation Needed

- **Required approval escalation:** a fresh independent revision-bound CTO review is
  required after this GATE-001 completion. Explicit user product implementation
  approval remains required after technical approval.
- **Resolved destructive-scope decision:** whole local-development database/volume
  disposal and recreation is authorized when needed for the clean path. The
  authorization excludes production, shared, staging, CI evidence,
  external-provider/Infisical state, deployment secret stores, unnamed targets, and
  every ambiguous target; it does not waive immediate pre-execution identity proof.
- **Potential scholarly escalation:** none for the current technical design. Escalate
  only if future decisions require a religious-legal conclusion or contested
  normative priority beyond ordinary provider responsibility.

## Evidence Reviewed

- Rewritten plan:
  `sha256:fed15e71ffeb739aa2dd2e62ef06317fcdde060420e9a7c4c9093105e295f6c9`.
- Rewritten tasks:
  `sha256:10a49e6dcfd55e39234dba068eed6a22634f1ed06f3419f19861d7b425772f84`.
- Combined plan then tasks bytes:
  `sha256:825b965e4937a7544ec99ea93c20456f2737c43441d45d44ac00549586805757`.
- Repository sources named in plan Section 2 and the workstream context evidence
  ledger.
- Repository governance: `AGENTS.md`, `docs/QUICK_REFERENCE.md`, matched intent,
  planning/I-VSD/CTO skill contracts.
- I-VSD contracts: `integration-contract.md`, `report-contract.md`,
  `scope-boundaries.md`, and `principles-and-domains.md`.
- Current workstream context, including its approval blockers and evidence ledger.
- Official-source functional guidance named in plan Section 2.1, accessed
  2026-08-30 under clean-room rules.

## Missing Evidence

- Implemented scenario results plus automated/documentary operator validation
  evidence.
- Self-hoster usability evidence for rotation and recovery instructions.
- Revision-bound CTO approval and explicit user approval.
- Immediate pre-execution identity evidence for every destructive local-development
  target.

## Context Inventory

- Included: current source behavior, deployment topologies, tests, docs, matched
  intent, official framework/provider/standards guidance, and rewritten triad.
- Excluded: secret values, credentials, private tenant data, third-party source,
  copied implementation examples, unsupported future providers, and religious-legal
  rulings.
- Evidence quality: strong for current repository behavior and rule conflicts;
  provisional for future implementation and stakeholder outcomes.

## Review Lifecycle

| Date | Previous status | New status | Trigger | Evidence/replacement |
|---|---|---|---|---|
| 2026-08-30 | none | stale | CTO rewrite materially replaced provider/deployment responsibility before planning-mode I-VSD revalidation | Rewritten plan/tasks digest above; fresh review required |
| 2026-08-30 | stale | current | Initial planning-mode revalidation claim | Combined `sha256:9b54d4ccb4677f05dfabd4d950777b8eee684dbcc524da21481d4f82f4fe7261`; later rejected by adversarial verification |
| 2026-08-30 | current | stale | Adversarial verification found no explicit multi-replica setup-secret behavior/acceptance and contradictory gate state | Rejected report `sha256:55467586b6baee3d21a41b2429bb48054602949608a92c9c6fc799216b75d78b` |
| 2026-08-30 | stale | current | Plan/tasks now explicitly own multi-replica authority, fail-closed behavior, cleanup, and recovery; triad gate state synchronized | Combined `sha256:550d566b643ef46d98ffd13f8049fe039b9398d755090bb3e6be98d2f835a448`; disposition `plan-aligned` |
| 2026-08-30 | current | stale | CTO-blocker correction split runtime policy from rotation, added replica overlap/restart/partial-activation/stale behavior, and changed `IVSD-*` task mappings | Corrected combined `sha256:d280829bf7e8e0f086f7423ba55f9f516d0165625f375a8c94396c654031c895` requires fresh planning-mode revalidation; disposition `changes-required` |
| 2026-08-30 | stale | current | Fresh planning-mode revalidation confirmed corrected rotation, operator, portability, destructive-authority mappings/exclusions, and synchronized GATE-001 | Combined `sha256:825b965e4937a7544ec99ea93c20456f2737c43441d45d44ac00549586805757`; disposition `plan-aligned` |

## Planning Handoff

- **Workstream:** `secrets-refactor-control-plane`
- **Status:** current
- **Reviewed input revision:** combined plan then tasks
  `sha256:825b965e4937a7544ec99ea93c20456f2737c43441d45d44ac00549586805757`
- **Escalations required before implementation:** fresh independent revision-bound
  CTO review; explicit user product implementation approval. Local-development reset
  scope is authorized but every target still requires immediate pre-execution
  identity proof.
- **Scholarly escalation:** none required for this technical design. Any future
  halal/haram, wajib, makrooh, riba, or Sharia-compliance conclusion remains outside
  I-VSD and requires qualified Sunni scholarly authority.
- **Refresh triggers:** any material change to provider authority, secret custody,
  tenant trust boundaries, capability-state truthfulness, diagnostic exposure,
  rotation/recovery responsibility, self-hosting topology, or an `IVSD-*` mapping.

| Finding | Mitigation | Plan scenario | Task mapping | Revalidation state |
|---|---|---|---|---|
| `IVSD-F001` | `IVSD-M001` | `SCN-SEC-001` | `SEC-001`–`SEC-003` | confirmed current |
| `IVSD-F002` | `IVSD-M002` | `SCN-OBS-001` | `SEC-004`, `SEC-201`, `SEC-202`, `SEC-205`, `SEC-207` | confirmed current |
| `IVSD-F003` | `IVSD-M003` | `SCN-OPS-001`, `SCN-ROT-001`–`SCN-ROT-003` | `SEC-221`–`SEC-226`, `SEC-401`, `SEC-402`, `SEC-404` | confirmed current |
| `IVSD-F004` | `IVSD-M004` | `SCN-TEN-001` | `SEC-101`, `SEC-104`, `SEC-105` | confirmed current |
| `IVSD-F005` | `IVSD-M005` | `SCN-SEC-002`, `SCN-CAP-001` | `SEC-002`, `SEC-202`, `SEC-203`, `SEC-205`, `SEC-207` | confirmed current |
| `IVSD-F006` | `IVSD-M006` | `SCN-MAN-001` | `SEC-301`–`SEC-305` | confirmed current |
| `IVSD-F007` | `IVSD-M007` | Section 13 authority boundary; no product scenario | `GATE-003`, `SEC-104`–`SEC-106` | accepted boundary; target proof pending |

All material findings and mitigations map exactly to current plan scenarios/tasks or,
for destructive authority, the explicit user-owned Section 13/GATE-003 boundary.
GATE-001 is complete for the reviewed bytes. Implementation SHALL NOT begin until a
fresh independent CTO approval and explicit user product implementation approval are
recorded; every destructive target also requires immediate identity proof.
