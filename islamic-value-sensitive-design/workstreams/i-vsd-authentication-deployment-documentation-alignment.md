<!-- ABOUTME: I-VSD planning assessment for truthful, low-barrier authentication deployment guidance. -->
<!-- ABOUTME: Evaluates provider defaults, hidden infrastructure, secret stewardship, and operator recovery. -->

# Authentication Deployment And Documentation Alignment — I-VSD Planning Assessment

Last Updated: 2026-09-05 Europe/Brussels

## Review Metadata

- Mode: planning
- Subject: authentication deployment defaults and operator documentation
- Workstream: `authentication-deployment-documentation-alignment`
- Report kind: provider-responsibility implementation assessment
- Report status: current
- Disposition: plan-aligned
- Evidence cutoff: 2026-09-05
- Reviewed input revision: plan
  `cbddc3675ce992f2880d6b321ed0a18ba39f715e818eae10282deeeb4d729d1d`;
  context
  `032cfdf11c216065e83c2d0eae099bccd99e8574c9b76e0e0080ebe4e2cfb990`;
  tasks
  `40c14e9ae5bcc930b30d2f0b9fac958846619bc16c2f273e748d83c00b0f05f2`
- Reviewed repository revision:
  `b9c3bfaeaf5eca190bb4675f7df4f276dc3dbcab`
- Review-state synchronization note: these digests bind the complete
  re-baselined design bodies before the non-material status/revision fields
  were updated to record this revalidation.
- Supersedes: none

## Scope

This assessment covers the provider-controlled decision to recommend Local
Identity by default, offer passwordless AT Protocol for public-HTTPS
self-hosters, retain Keycloak for advanced operators, and publish five
resource-proportional deployment presets. It evaluates whether generated
environment projections, canonical Compose fragments, scenario entrypoints,
CI, quickstarts, secrets, recovery guidance, and public/internal documentation
make those choices real, safe, and understandable.

## Claim Boundary

This is provider-responsibility design reasoning, not a fatwa, Sharia
certification, legal opinion, or proof that any deployment is secure or
ethically sufficient. It evaluates maintainers' responsibilities for truthful
defaults, equitable self-hosting, credential stewardship, explicit
dependencies, and recoverable operations.

## Findings

### IVSD-F001 — Hidden Keycloak Dependency Undermines The Local Default

- **Lifecycle:** accepted
- **Severity:** high
- **Claim type:** provider-controlled transparency, access, and sovereignty
- **Principle/domain:** amanah, sidq, 'adl; architecture and self-hosting
- **Stakeholders:** small community operators, mosques, non-profits, local
  developers, users whose credentials are hosted locally
- **Provider-controlled decision:** whether every deployment preset includes
  only the identity and infrastructure required by its declared capabilities
- **Evidence:** `docker-compose.yml` defines Keycloak without a profile and
  makes API/UI startup depend on Keycloak and `keycloak-init`;
  `AUTHENTICATION_PROVIDER=local` is the documented default.
- **Risk:** operators are told they selected a low-dependency provider while
  still paying Keycloak's memory, database, startup, patching, and failure
  costs.
- **Mitigation:** `IVSD-M001` establishes five canonical preset contexts,
  generated env examples, and thin Compose scenarios. Standalone Local excludes
  every external service; Split Local and Split AT Protocol exclude Keycloak;
  Split Keycloak preserves its complete initialization chain; External
  Infrastructure runs application processes only.
- **Rejected alternative:** documenting Keycloak as mandatory for split
  deployments. This would make the Local default technically misleading and
  preserve avoidable operating cost.

### IVSD-F002 — A Protective Default That Cannot Sign In Is Not A Usable Default

- **Lifecycle:** accepted
- **Severity:** critical
- **Claim type:** credential stewardship and truthful onboarding
- **Principle/domain:** amanah, sidq, raf' al-haraj; UX/defaults and operations
- **Stakeholders:** first-time evaluators, standalone operators, future users
- **Provider-controlled decision:** whether quickstarts supply every
  non-optional Local Identity prerequisite without exposing reusable secrets
- **Evidence:** the standalone quickstart supplies no Local JWT signing key;
  the Compose quickstart copies a template whose Local key is intentionally
  blank.
- **Risk:** first sign-in fails after setup, or operators respond by committing
  a shared placeholder key.
- **Mitigation:** `IVSD-M002` gives evaluation-only and production-safe
  procedures that generate unique key material locally, pass only the
  environment-variable name to containers, distinguish ephemeral from durable
  secrets, and never print or commit the resulting value.
- **Rejected alternative:** shipping a default signing key. A shared key would
  turn convenience into cross-instance token-forgery risk.

### IVSD-F003 — Provider Choice Must Disclose Real Trade-Offs Before Commitment

- **Lifecycle:** accepted
- **Severity:** high
- **Claim type:** provider-controlled informed choice
- **Principle/domain:** sidq, amanah, 'adl; UX/defaults and communications
- **Stakeholders:** self-hosters, SaaS operators, account holders
- **Provider-controlled decision:** provider ordering, prerequisite disclosure,
  password-custody explanation, and administration guidance
- **Evidence:** the canonical provider guide states Local first, AT Protocol
  second for public HTTPS, and Keycloak for advanced SSO/MFA; multiple landing,
  federation, admin, and request-flow pages still describe Keycloak as the only
  or universal path.
- **Risk:** operators choose a provider without understanding password custody,
  public-HTTPS limitations, operational dependencies, or recovery needs.
- **Mitigation:** `IVSD-M003` projects one provider-neutral decision matrix and
  consistent terminology through onboarding, administration, federation,
  self-hosting, and navigation pages without duplicating the technical source.
- **Rejected alternative:** retaining page-local provider narratives. That
  makes future drift structurally likely.

### IVSD-F004 — Secret And Recovery Guidance Is Incomplete For AT Protocol

- **Lifecycle:** accepted
- **Severity:** critical
- **Claim type:** security stewardship and operational continuity
- **Principle/domain:** amanah, raf' al-haraj; security and operations
- **Stakeholders:** AT Protocol users, instance administrators, incident
  responders
- **Provider-controlled decision:** whether operators receive complete,
  value-free instructions for purpose-separated keys, readiness, rotation,
  loss, and direct-database recovery
- **Evidence:** internal secret documentation defines three purpose-separated
  AT Protocol key rings, while the public secret/environment guides do not
  provide a complete readiness and rotation path.
- **Risk:** key reuse, premature key removal, failed OAuth callbacks, silent
  provider unavailability, or unsafe recovery handling.
- **Mitigation:** `IVSD-M004` documents the three logical secret bindings,
  overlap rotation, public-origin requirements, readiness interpretation,
  reauthentication consequences, and bounded break-glass behavior without
  exposing secret values or provider payloads.
- **Rejected alternative:** exposing internal key material formats as
  copy-paste defaults. Public guidance should describe secure generation and
  authority ownership, not reusable values.

### IVSD-F005 — Configuration Drift Transfers Debugging Cost To Operators

- **Lifecycle:** accepted
- **Severity:** high
- **Claim type:** promise-keeping and maintainability
- **Principle/domain:** sidq, amanah, ihsan; governance and operations
- **Stakeholders:** operators, maintainers, support contributors
- **Provider-controlled decision:** source anchors, canonical names, drift
  detection, and documentation lifecycle
- **Evidence:** operator identity names, Keycloak endpoint/realm/client
  defaults, request-flow diagrams, metadata blocks, and internal indexes
  disagree with current options, canonical metadata, realm export, or
  authentication architecture.
- **Risk:** valid-looking configuration fails closed, support guidance points
  at the wrong dependency, and maintainers repeat the same reconciliation.
- **Mitigation:** `IVSD-M005` normalizes documentation against source-owned
  configuration names, extends the activation graph with typed named presets,
  generates every env projection, renders every env/Compose pair in CI,
  strengthens bounded diagnostics, and records one durable self-hosting lesson.
- **Rejected alternative:** prose-only repair without an executable topology
  contract. It would not prevent the hidden dependency from returning.

### IVSD-F006 — Resource-Proportional Presets Must Not Hide Capability Loss

- **Lifecycle:** accepted
- **Severity:** high
- **Claim type:** equitable access, operational honesty, and continuity
- **Principle/domain:** 'adl, sidq, amanah, raf' al-haraj; architecture and
  operations
- **Stakeholders:** low-resource community operators, production split
  operators, paid-event organizers, external-infrastructure operators
- **Provider-controlled decision:** which infrastructure is bundled, excluded,
  or transferred to the operator in each preset
- **Evidence:** Standalone can operate without Redis or PostgreSQL; split host
  startup can fall back to memory; the concurrent stateless checkout work
  removes the split ticket-store Redis authority; the current monolith also
  contains optional services unrelated to the baseline.
- **Risk:** an alleged minimum remains resource-heavy, or an alleged production
  preset silently retains obsolete cache infrastructure, or an external preset
  hides migration, cache, identity, or recovery ownership.
- **Mitigation:** `IVSD-M006` makes Standalone Local the one-container absolute
  minimum; includes PostgreSQL and migrator in full bundled split presets;
  leaves Redis as an explicit optional add-on after stateless checkout lands;
  gives Keycloak its advanced split preset; makes external
  infrastructure an explicit externally operated PostgreSQL-and-Keycloak
  contract; and preserves auxiliary services as inactive add-ons outside all
  baselines.
- **Rejected alternatives:** preserve Redis solely for obsolete ticket-store
  state (hidden resource cost); include all optional services everywhere
  (unjustified resource and patch burden); maintain five complete Compose
  copies (drift and false operational claims).

## Recommendations

1. Treat `IVSD-M001`, `IVSD-M002`, and `IVSD-M006` as release-blocking: a
   recommended Local default must be one-container and key-safe, while a
   production split preset must include every capability-required dependency.
2. Keep provider comparison factual and responsibility-oriented. Local
   Identity reduces infrastructure but makes the operator a password
   custodian; AT Protocol delegates password custody but requires public HTTPS;
   Keycloak increases operating cost in exchange for advanced identity
   administration.
3. Keep secrets in the selected authority and expose only logical binding
   names, readiness states, rotation order, and bounded recovery outcomes.
4. Use the existing canonical authentication pages as the technical source;
   landing pages should link rather than reproduce the full provider matrix.
5. Add no new dependency and perform no external product/source research.
6. Generate all five environment examples from typed activation contexts and
   make CI render every matching Compose pair without starting services.

## Stakeholders

- Small community, mosque, and non-profit self-hosters.
- Localhost evaluators and contributors.
- Users whose passwords are stored through Local Identity.
- AT Protocol users delegating authentication to their PDS.
- Professional hosting teams and SaaS operators using Keycloak.
- Instance administrators and incident responders.
- Maintainers responsible for truthful release and support documentation.
- Paid-event organizers relying on split checkout continuity.
- External-infrastructure operators assuming responsibility for migrations,
  cache, identity, storage, and recovery.

## I-VSD Principles And Domains

- **Amanah:** custody of passwords, JWT keys, OAuth signing keys, and
  administrator access.
- **Sidq:** truthful provider labels, prerequisites, defaults, and topology
  claims.
- **'Adl:** a credible low-resource deployment path for small organizations.
- **Raf' al-haraj:** avoiding preventable setup failure and administrator
  lockout.
- **Ihsan:** source-grounded documentation, tested configuration, and clear
  recovery instructions.
- **Resource proportionality under 'adl:** minimum deployments exclude
  enterprise dependencies, while production presets disclose required
  infrastructure rather than hiding capability loss.

Applicable domains are architecture, UX/defaults, operations, governance, and
communications. Monetization, content moderation, ranking/AI, and religious
content classification are not affected.

## Validation Gaps

- No rendered-Compose contract currently proves the five exact service graphs,
  root Standalone Local alias, relative paths, or retained optional services.
- The environment generator has no named-preset model or five generated
  projections.
- The public quickstart has not been protected by a machine-consumed
  configuration contract.
- Documentation link/metadata checks must be selected from existing repository
  tooling during implementation; prose must not become a unit-test input.
- Stakeholder usability evidence for provider comparison remains absent; the
  plan limits itself to factual prerequisite and responsibility disclosure.

## Escalation Needed

No scholarly or religious-legal escalation is required. Security review is
required before release because the work changes the deployment graph around
authentication infrastructure. Any future third-party dependency or externally
informed provider design would require IP and license review; none is planned
here.

## Evidence Reviewed

- Git revision `b9c3bfaeaf5eca190bb4675f7df4f276dc3dbcab`.
- Prior plan body
  `203ae79d77b15b887fdbb8aa104bb7442ccc52d4d036a1913ccf648a086549df`.
- Re-baselined plan body
  `cbddc3675ce992f2880d6b321ed0a18ba39f715e818eae10282deeeb4d729d1d`,
  context body
  `032cfdf11c216065e83c2d0eae099bccd99e8574c9b76e0e0080ebe4e2cfb990`,
  and tasks body
  `40c14e9ae5bcc930b30d2f0b9fac958846619bc16c2f273e748d83c00b0f05f2`.
- `docker-compose.yml`, `.env.example`, and
  `src/Event.Standalone/{Dockerfile,appsettings.json,Program.cs}`.
- `InstanceOperatorIdentityOptions`,
  `ConfiguredAdministratorBootstrapProvider`, and
  `AtprotoInfrastructureOptions`.
- `CanonicalEnvironmentMetadata`, Keycloak realm export, authentication
  provider dispatch, and Local Identity persistence/service ownership.
- Public provider, authentication, self-hosting, environment, secrets,
  troubleshooting, federation, admin, and quickstart pages.
- Internal authentication, provider matrix, security, request-flow,
  configuration, secrets, operations, self-hosting, documentation architecture,
  and ADR-027 pages.
- Existing Compose diagnostic, setup-catalogue, standalone composition,
  authentication, and architecture tests.
- Concurrent shared-tree diff in
  `BlazorHostServiceCollectionExtensions`,
  `RegistrationPaymentCheckoutTicketStore`, and its integration tests removing
  Redis-backed checkout ticket authority.
- Docker Compose official include and merge documentation:
  `https://docs.docker.com/compose/how-tos/multiple-compose-files/include/`
  and
  `https://docs.docker.com/compose/how-tos/multiple-compose-files/merge/`.
- Completed predecessor workstream
  `dev/active/aspnet-identity-auth/`.

## Missing Evidence

- Post-change `docker compose config` evidence for all five env/scenario pairs.
- A committed/final stateless checkout revision confirming Redis remains
  optional before preset implementation starts.
- Byte-equivalence evidence for root and Standalone Local env projections.
- Inventory evidence that every optional service survived decomposition but is
  absent from baseline runtime graphs.
- A post-change proof that no secret value enters Git history, test fixtures,
  logs, health output, or documentation examples.
- A post-change link and GitBook syntax report for all changed public pages.

## Context Inventory

- **Provider-mediated capability:** authentication provider selection,
  generated deployment presets, self-hosting topology, quickstart, secret
  custody, cache/migration ownership, and recovery.
- **Provider authority:** deployment operator selects infrastructure and
  secrets; an authenticated instance administrator selects runtime login
  admission within server-enforced lockout constraints.
- **Data in scope:** configuration names and secret references only; no secret
  values, credentials, tokens, DIDs, user IDs, or PII.
- **Out of scope:** authentication runtime redesign, new identity providers,
  password reset/MFA implementation, database migrations, public API changes,
  and religious/legal rulings.

## Review Lifecycle

| Date | Previous status | New status | Trigger | Evidence/replacement |
|---|---|---|---|---|
| 2026-09-05 | none | draft | Implementation-plan intake and repository evidence review | Git revision `b9c3bfaeaf5eca190bb4675f7df4f276dc3dbcab` |
| 2026-09-05 | draft | current | Completed triad revalidation and corrected I-VSD task parity | Plan/context/tasks body digests in Review Metadata |
| 2026-09-05 | current | draft | User replaced the optional-Keycloak design with a generated five-preset deployment matrix | Re-baselined triad pending final digest |
| 2026-09-05 | draft | current | Revalidated proportional infrastructure, preset generation, CI, and task mappings | Re-baselined body digests in Review Metadata |
| 2026-09-05 | current | current | Concurrent stateless checkout work removed the last Redis hard requirement; baseline presets and F006 were revalidated without touching that work | Final documentation-led triad body digests in Review Metadata |

## Common Overlooked Failures And Outcomes

- A scenario file that copies API/BFF services is another monolith, not a
  preset.
- Independent Compose includes with conflicting service names fail instead of
  merging; base and overlays need one intentional `include.path` list.
- A Keycloak profile that remains an unconditional API/UI dependency is not
  optional.
- Removing startup ordering for the Keycloak profile can create a transient
  login failure even when all services eventually become healthy.
- A quickstart that reaches `/setup` but cannot issue the first Local token is
  still broken.
- A generated signing key printed to a terminal or embedded in command history
  converts documentation into a secret-leak path.
- An AT Protocol key rotation that removes a still-referenced key forces
  avoidable reauthentication or provider outage.
- Correct provider pages can remain undiscoverable when landing pages,
  diagrams, and root metadata continue to say “Keycloak only.”
- Treating a concurrently removed Redis authority as permanent can make every
  split preset needlessly resource-heavy.
- Changing Compose project names can look like data loss by selecting new
  project-scoped volumes.

## Planning Handoff

- Workstream: `authentication-deployment-documentation-alignment`
- Status: current; plan-aligned
- Reviewed input revision: plan
  `cbddc3675ce992f2880d6b321ed0a18ba39f715e818eae10282deeeb4d729d1d`;
  context
  `032cfdf11c216065e83c2d0eae099bccd99e8574c9b76e0e0080ebe4e2cfb990`;
  tasks
  `40c14e9ae5bcc930b30d2f0b9fac958846619bc16c2f273e748d83c00b0f05f2`
- Findings and mitigations:
  - `IVSD-F001` -> `IVSD-M001`
  - `IVSD-F002` -> `IVSD-M002`
  - `IVSD-F003` -> `IVSD-M003`
  - `IVSD-F004` -> `IVSD-M004`
  - `IVSD-F005` -> `IVSD-M005`
  - `IVSD-F006` -> `IVSD-M006`
- Required plan mappings:
  - `IVSD-F001` / `IVSD-M001` -> Scenarios 3.1–3.2; Tasks 1.1–1.8
  - `IVSD-F002` / `IVSD-M002` -> Scenarios 3.3 and 3.5B; Tasks
    1.1–1.5, 3.1
  - `IVSD-F003` / `IVSD-M003` -> Scenarios 3.5–3.6; Tasks 3.1–3.2,
    4.1–4.2
  - `IVSD-F004` / `IVSD-M004` -> Scenarios 3.3B and 3.5–3.6; Tasks
    1.1–1.8, 3.1–3.2, 4.1
  - `IVSD-F005` / `IVSD-M005` -> Scenarios 3.3–3.4; Tasks 1.1–1.8,
    2.1–2.4, 4.1–4.2
  - `IVSD-F006` / `IVSD-M006` -> Scenarios 3.1 and 3.5C; Tasks
    1.1–1.8, 3.1, 4.1–4.2
- Escalations required before: Phase 1 blocks until the concurrent stateless
  checkout work lands or Redis ownership is re-evaluated; security-sensitive
  implementation release review; no scholarly escalation.
- Refresh triggers: changing preset count/service sets, root default,
  Redis/migration ownership or the stateless checkout dependency, recommended provider order, adding a dependency,
  changing secret authority or runtime authentication, or removing a mapped
  mitigation.
