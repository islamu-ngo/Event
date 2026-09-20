# I-VSD Review — Provider Credential Onboarding And Runtime Custody

Last Updated: 2026-09-20 Europe/Brussels

## Review Metadata

- **Mode:** planning
- **Subject:** Keycloak and Cerbos administrator credential custody
- **Workstream:** `provider-credential-onboarding`
- **Report kind:** planning handoff
- **Revalidation:** planning-mode refresh after the mandatory `PC-CTO-r2` plan-review split; no scope, authority, release-order, or CTO revision change
- **Report status:** current
- **Disposition:** plan-aligned
- **Evidence cutoff:** 2026-09-20
- **Reviewed input:** `PC-CTO-r2`, final freshness-synchronized
  `provider-credential-onboarding-plan.md`
  `sha256:d65973b1c6299240a06429fdafa6fc440a7bbeb5148f972e65ff179f1255f9ab`;
  `provider-credential-onboarding-tasks.md`
  `sha256:a06d6fde4ab01bab1e86a687932f1243f10cd6c29bf9a374dc3cf61962fa7ae2`
- **Supersedes:** none

## Scope

This review covers provider-controlled choices for Keycloak and Cerbos administrator
credentials, application runtime client credentials, infrastructure provisioning
credentials, browser/BFF/API custody, selected secret-authority storage, setup and
administrator workflows, headless/GitOps operation, failure recovery, diagnostics,
and self-hoster burden.

It covers both external-provider onboarding and bundled local infrastructure. It
does not claim that every physical memory copy can be erased immediately, does not
certify security outcomes, and does not issue a religious-legal ruling.

The mandatory split makes PR 1 HTTP cache/replay nonretention only: seven existing
credential POSTs and cache policy on the existing internal secret-bearing GET.
PRs 2-6 retain the approved custody, UI, service-identity, publication and deployment
work. Every finding remains open. Planned partial mitigation is not shipped
mitigation; the current / plan-aligned disposition describes design traceability,
not acceptance of deferred risks as a safe final state.

## Claim Boundary

This is provider-responsibility analysis for software design. It applies principles
of entrusted custody, harm prevention, truthfulness, justice between operators and
users, accountable recovery, and self-hoster autonomy. It does not declare the
design halal, haram, wajib, makrooh, or Sharia-compliant; such conclusions remain
with qualified Sunni scholarly authority.

The full-program target promise is that provider-administrator credentials are
processed only for an explicitly authorized, bounded operation and are never
durably retained by ISLAMU Event. That promise is not established by PR 1 or this
review: standing credentials, database values, logs, browser state and historical
replay rows remain outside its remediation scope. A server handling a
browser-submitted credential necessarily observes it transiently in process
memory; the product must not claim that the credential was never present in a
server process. PR 1 neither reads nor erases historical replay rows; it must not
claim historical retention eradication.

## Findings

### IVSD-F001 — Durable administrator credentials create avoidable control-plane takeover risk

- **Lifecycle:** open
- **Severity:** blocker
- **Claim type:** entrusted authority and preventable harm
- **Principle/domain:** amanah, non-maleficence; architecture, security, operations
- **Stakeholders:** self-hosters, instance administrators, tenants, end users, incident responders
- **Provider-controlled decision:** whether broad Keycloak/Cerbos administrator credentials become standing application runtime secrets
- **Evidence:** `SecretDefinitionRegistry` registers Keycloak and Cerbos administrator credentials as non-bootstrap secrets; the public Infisical guide places them in application-readable folders
- **Validation level:** repository source and documentation evidence
- **Mitigation:** `IVSD-M001`
- **Owner/next validation:** implementing agents for PRs 2/3/5, via the runtime-authority, Keycloak-admin and Cerbos-publication backlog destinations in Planning Handoff. Scenarios `SCN-CRED-001` through `SCN-CRED-004`; D1 `PC-101`, D2 `KC-201`, D4 `CB-301`; `KG-101` owns graduation. PR 1 does not remove standing credentials; remove live bindings only with their consumer cutover.
- **Escalation boundary:** security and operator responsibility, not scholarly

### IVSD-F002 — Recursive mixed-purpose secret folders violate least privilege

- **Lifecycle:** open
- **Severity:** blocker
- **Claim type:** confidentiality and blast-radius minimization
- **Principle/domain:** amanah, harm prevention; deployment, architecture, support
- **Stakeholders:** operators and every user whose identity or authorization depends on the external providers
- **Provider-controlled decision:** whether BFF/API machine identities can read provider-administration or database-provisioning credentials they never consume
- **Evidence:** the BFF admits recursive `/keycloak` reads while the documented folder includes administrator and SMTP credentials; `/cerbos` mixes runtime endpoint data, administrator credentials, password verifier, and PostgreSQL credentials
- **Validation level:** repository configuration and documentation evidence
- **Mitigation:** `IVSD-M002`
- **Owner/next validation:** implementing agents for PR 2 (auth-specific BFF roots) and PR 6 (remaining deployment isolation), via their named backlog destinations. Scenarios `SCN-PATH-001` and `SCN-PATH-002`; D6 `DP-401` through `DP-403`; `KG-101` owns graduation. PR 1 changes no machine identity or secret acquisition.
- **Escalation boundary:** security and deployment operations, not scholarly

### IVSD-F003 — Application-database secret values expand backup and recovery harm

- **Lifecycle:** open
- **Severity:** blocker
- **Claim type:** entrusted confidentiality and truthful secret ownership
- **Principle/domain:** amanah, sidq; data, backup, recovery, product UX
- **Stakeholders:** operators, backup custodians, users protected by OIDC credentials
- **Provider-controlled decision:** whether authentication client secrets are stored as `SystemSetting.Value` despite the documented external-authority contract
- **Evidence:** `AuthProviderConfigurationService` writes Keycloak and Google client secrets to `SystemSetting`; internal documentation says databases hold only binding/reference metadata
- **Validation level:** source and contract contradiction verified
- **Mitigation:** `IVSD-M003`
- **Owner/next validation:** PR 2 runtime-authority implementing agent. Scenarios `SCN-STORE-001` through `SCN-STORE-003`; D1 `PC-101` through `PC-104`; `KG-101` owns graduation. Replace the selected-authority writer and BFF consumer, fence old writers/readers, then enable exact cleanup. PR 1 changes no database values and does not erase backups.
- **Escalation boundary:** security, data custody, and operations, not scholarly

### IVSD-F004 — Standing runtime administration turns application compromise into provider compromise

- **Lifecycle:** open
- **Severity:** critical
- **Claim type:** least authority and containment
- **Principle/domain:** amanah, non-maleficence; architecture, runtime operations
- **Stakeholders:** operators, tenant administrators, identity-account holders, authorized resource owners
- **Provider-controlled decision:** whether runtime processes automatically administer Keycloak or publish Cerbos policies using long-lived broad credentials
- **Evidence:** Keycloak lifecycle email uses password-grant administrator credentials; Cerbos startup reconciliation and role handlers publish through deployment credentials
- **Validation level:** inherited repository source and official provider-documentation evidence from the shared packet; provider capabilities were not independently reverified during this refresh
- **Mitigation:** `IVSD-M004`
- **Owner/next validation:** implementing agents for PR 4 lifecycle identity and PR 5 Cerbos publication. Scenarios `SCN-KC-003`, `SCN-CB-002`, `SCN-CB-003`; D3 `KC-205`, D4 `CB-302`, D5 `CB-305`; `KG-101` owns graduation. PR 4 requires real minimum-role evidence; PR 5 establishes operator publication/readiness before removing runtime fallback in the same release boundary. PR 1 leaves both runtime authorities unchanged.
- **Escalation boundary:** security and operational architecture, not scholarly

### IVSD-F005 — Secret-bearing retries and diagnostics can silently defeat transient custody

- **Lifecycle:** open
- **Severity:** critical
- **Claim type:** confidentiality, transparency, and accountable failure
- **Principle/domain:** amanah, sidq; API, observability, support, incident response
- **Stakeholders:** operators, support personnel, provider-account owners
- **Provider-controlled decision:** whether request bodies, provider responses, exception text, generic idempotency storage, queued jobs, or screenshots retain credentials
- **Evidence:** current operations use credential-bearing request DTOs and some handlers log exception objects. The PC-CTO-r2 packet identifies missing endpoint nonretention metadata and existing middleware suppression before replay identity/repository access. This supports the HTTP boundary design, not a claim that raw request bodies are currently persisted. Background work remains an exclusion requirement, not an observed credential-queue incident.
- **Validation level:** inherited repository risk evidence plus revalidated scenario/task traceability; HTTP and repository-wide output behavior remain to be proven
- **Mitigation:** `IVSD-M005`
- **Owner/next validation:** PR 1 implementing agent: `SCN-HTTP-001` through `SCN-HTTP-004`, `HTTP-101` through `HTTP-103` for HTTP cache/replay only. Remaining `SCN-OBS-001` through `SCN-OBS-003`: PR 2/D1 `PC-101`, PR 3/D2 `KC-202`, PR 5/D4 `CB-302`, with `KG-101` graduation. Runtime-authority custody review retains legacy retention/backup obligations; Keycloak-admin and Cerbos-publication owners retain logs, provider output/redirects, UI clearing and partial-completion proof. Historical replay rows are not purged in PR 1. No full mitigation is claimed.
- **Escalation boundary:** security/privacy operations, not scholarly

### IVSD-F006 — Removing automation without truthful recovery would burden small self-hosters

- **Lifecycle:** open
- **Severity:** high
- **Claim type:** self-hosting autonomy and removal of hardship
- **Principle/domain:** justice, removal of hardship, accountability; UX, documentation, operations
- **Stakeholders:** small/community self-hosters, GitOps operators, managed deployments
- **Provider-controlled decision:** whether operators retain usable interactive, manual, and CI/GitOps paths after application runtime administration is removed
- **Evidence:** repository already provides one-time onboarding, manual Cerbos package download, `cerbosctl`, and production CI publishing; current documentation inconsistently presents deployment credentials as application secrets
- **Validation level:** repository workflow evidence; usability not yet validated
- **Mitigation:** `IVSD-M006`
- **Owner/next validation:** PR 1 implementing agent preserves existing operator paths under `SCN-HTTP-004` / `HTTP-103`; this is continuity/retry truthfulness, not completion of the cutover mitigation. PRs 3/5/6 implementing agents own `SCN-OPS-001` through `SCN-OPS-003`: D2 `KC-203`, D4 `CB-303`, D5 `CB-305`, D6 `DP-402`/`DP-403`, with auth-specific handoff in PR 2 and `KG-101` graduation. Usability and operational recovery evidence remain absent.
- **Escalation boundary:** product and operator experience, not scholarly

## Recommendations

### IVSD-M001 — Make provider administration request-scoped by default

Require complete one-time credentials for each explicit Keycloak or Cerbos
administrative operation. Keep them out of databases, secret bindings, runtime
configuration, browser storage, background jobs, idempotency response storage,
logs, traces, metrics, support artifacts, and API responses. Require re-entry
after failure, timeout, cancellation, or a later administrative action.

Deferred to PRs 2/3/5; PR 1's HTTP policy does not implement this custody promise.

### IVSD-M002 — Separate runtime, provisioning, and administration authorities

Use consumer-specific secret paths and machine identities. BFF identities may
read only BFF runtime credentials. API identities may read only API runtime
credentials. Provisioning/container/CI credentials stay outside application
identities. Avoid recursive parent paths that mix those classes.

Deferred to auth-specific PR 2 and remaining isolation PR 6; admission must occur
before acquisition, not through post-fetch filtering.

### IVSD-M003 — Keep runtime secrets in the selected external authority

Persist only non-secret settings and binding/reference metadata in the
application database. Remove application-managed raw-secret storage. Infisical
may receive a write-only onboarding value through the planned capability-specific
selected-authority writer; Environment and Development/Testing User Secrets remain operator-written
and require refresh/restart. Existing secret-bearing rows must be eradicated by
a bounded, value-free migration path rather than retained as compatibility data.

Deferred to PR 2. Writer/BFF consumer convergence must precede exact auth-row
cleanup; live Cerbos admin bindings await PR 5. Recovery must disclose backup
retention and require revocation/rotation, not claim physical erasure.

### IVSD-M004 — Replace standing broad administration with narrow purpose authority

Remove runtime Cerbos policy publishing and startup reconciliation. Use explicit
one-time onboarding/admin synchronization, CI/GitOps publishing, or manual
`cerbosctl` package installation. Remove role-change publishing where the package
is static and does not depend on role rows.

For recurring Keycloak lifecycle actions that genuinely require server automation,
use a dedicated confidential service client with the minimum realm-management
roles and the OAuth client-credentials flow. Do not reuse a human administrator
username/password or the browser BFF client.

Deferred to PRs 4/5. A successful token does not prove absence of excess roles.
Cerbos D4/D5 remain one release-safety boundary: establish same-revision/environment
publication readiness before runtime administration removal, not after it.

### IVSD-M005 — Build a zero-secret output and replay boundary

Credential-bearing responses must be `private, no-store`; request handling must
exclude generic response replay and payload persistence. Log only bounded
operation codes, provider kind, safe status, correlation ID, and timestamps.
External mutation remains additive and idempotent, but a retry obtains fresh
credentials rather than persisting the prior request.

PR 1 implements only the existing private/no-store and replay-suppression HTTP
policies, with real middleware/current-authority denial tests, including poisoned
historical replay and early denied responses. It does not sanitize logs or provider
payloads, clear browser state, erase old records, or add provider-side deduplication.
PRs 2/3/5 retain the remaining custody/output/recovery mitigation and validation.

### IVSD-M006 — Preserve progressive self-hosting paths

Keep three explicit paths:

1. Interactive setup with request-scoped credentials.
2. Headless/GitOps provisioning where external tooling owns administrative credentials.
3. Bundled provider infrastructure where one-shot provisioning containers receive
   only the credentials they require and application containers receive none.

Document truthful recovery, rerun, rotation, restart, manual upload, and
credential-revocation steps for each path.

PR 1 preserves current routes, contracts and operator paths while documenting fresh
retry intent. Authority-specific handoff, one-time forms, publication recovery and
bundled isolation remain deferred to PRs 2/3/5/6; operator validation is still needed.

### Rejected Alternatives

- **Rejected:** retaining administrator credentials in Infisical merely because
  Infisical encrypts them. Encryption at rest does not correct excessive lifetime
  or application read authority.
- **Rejected:** encrypting credentials in the application database. This creates a
  second secret store and couples backups, key custody, recovery, and breach scope.
- **Rejected:** browser-to-provider administration. It weakens the BFF audit and
  antiforgery boundary, creates CORS and endpoint-exposure pressure, and transfers
  provider details into client code.
- **Rejected:** background jobs containing raw credentials. A delayed/retried job
  is incompatible with request-scoped custody.
- **Rejected:** silently keeping current credential aliases and fallback readers.
  The repository is pre-release and requires clean breaking replacement.
- **Rejected:** disabling all operator automation. Narrow runtime service
  credentials remain valid when a recurring product capability truly requires them.
- **Rejected:** marking the six findings resolved after HTTP-only PR 1, or treating
  the split as abandonment of custody obligations. The smaller release is defensible
  only with explicit deferred ownership, graduation and fresh review of each follow-up.
- **Chosen for revalidation:** update this subject report in place rather than
  create a replacement report; preserve stable IDs/history and use one-way final
  plan/tasks -> report -> context hash binding to avoid circular freshness edits.

## Stakeholders

| Stakeholder | Interest and potential burden |
|---|---|
| Community self-hosters | Safe defaults, low setup burden, recoverable manual paths |
| Enterprise/GitOps operators | Deterministic ownership, CI publishing, least-privilege identities |
| Instance administrators | Clear one-time credential prompts and safe retry behavior |
| Tenant administrators and users | Identity and authorization providers cannot be seized through an app compromise |
| Support and incident responders | Useful status and correlation without secret-bearing evidence |
| Developers and reviewers | One enforceable custody taxonomy instead of provider-specific exceptions |

## I-VSD Principles And Domains

- **Amanah (entrustment):** administrator credentials confer authority over identity
  and authorization control planes and must receive the narrowest custody.
- **Sidq (truthfulness):** UI and runbooks must distinguish “last synchronized,”
  “currently verified,” “runtime configured,” and “operator action required.”
- **Justice:** one tenant or application host must not gain provider-wide
  administration beyond its legitimate scope.
- **Removal of hardship:** safe interactive and manual paths remain available to
  operators who do not run enterprise CI or a writable secret manager.
- **Accountability:** value-free receipts and correlation make failures
  diagnosable without creating a secret-read surveillance ledger.

Applicable domains are architecture, security, data, UX, operations, governance,
support, portability, self-hosting, and evaluation.

## Common Overlooked Failures And Outcomes

- The credential is absent from the database but survives in browser component
  state after an error or navigation.
- Generic idempotency stores or replays a credential-bearing response.
- A provider exception contains request material and is logged through `LogError(ex, ...)`.
- A BFF identity reads a recursive folder containing provisioning secrets it never uses.
- A one-time Keycloak access token remains usable longer than the setup operation;
  operators need explicit temporary-account revocation guidance.
- Cerbos UI says policies are synchronized based only on a historical receipt after
  an external operator changed the store.
- Removing startup policy publishing leaves stale policies without a clear CI/manual
  upgrade gate.
- An Environment-authority onboarding flow promises to save a secret even though
  the provider is intentionally not writable.
- Bundled provider containers cannot start because infrastructure bootstrap values
  were confused with external-provider onboarding credentials.
- A cleanup logs the old secret value or copies it into migration evidence.

## Validation Gaps

- No PR 1 HTTP invariant tests have run; endpoint metadata, early-denial headers and
  replay suppression are planned behavior, not verified runtime outcomes.
- Deferred backlog briefs are assigned to `KG-101` but have not yet been created;
  plan D1-D6 and the task ledger remain their authoritative packets until graduation.
- No implementation evidence yet proves zero secret-bearing output across logs,
  traces, metrics, ProblemDetails, OpenAPI, generated clients, or support surfaces.
- No implementation evidence yet proves existing secret-bearing settings are
  eradicated without logging values.
- Keycloak service-account role selection requires verification against the exact
  Admin REST operations used by lifecycle email.
- Cerbos provides one Basic Auth administrator for the PDP Admin API; application
  removal therefore depends on CI/manual/operator publishing rather than a
  narrower native runtime account.
- Self-hoster usability of the Environment-authority restart handoff has not been tested.

## Escalation Needed

- **Before PR 1 implementation:** user authorization is already recorded separately
  from the applied `PC-CTO-r2` CTO review. This owner revalidation clears only the
  I-VSD freshness blocker and restores technical alignment; no repeat approval is
  requested. `GATE-001` remains open until report/triad transfer and SHA-256 equality
  in the execution workspace. No product edit precedes that operational gate.
- **Before each follow-up implementation:** its implementing agent must graduate the
  named brief into a separately reviewed active triad, with current I-VSD/CTO
  binding and its preserved authority/cutover gates.
- **Before merge:** anonymized Epistemic MAD review by security/privacy,
  platform-operations, and quality reviewers is required.
- **Scholarly escalation:** none for the current technical design. Any future
  religious-legal conclusion remains outside this report.

## Evidence Reviewed

- [PC-CTO-r2 plan](../../dev/active/provider-credential-onboarding/provider-credential-onboarding-plan.md)
  at `sha256:d65973b1c6299240a06429fdafa6fc440a7bbeb5148f972e65ff179f1255f9ab`.
- [PC-CTO-r2 tasks](../../dev/active/provider-credential-onboarding/provider-credential-onboarding-tasks.md)
  at `sha256:a06d6fde4ab01bab1e86a687932f1243f10cd6c29bf9a374dc3cf61962fa7ae2`.
- Context supplied approval, handoff status and preservation baselines; it is not
  a substitute for the exact reviewed plan/tasks above. Repository HEAD remains
  `7f78e938fa8765d6351c2b80801e895fa8f6abf5` on `develop`.
- Direct refresh review: plan sections 0, 3, 5-7, 9, 13-15; tasks transfer/freshness
  gates, HTTP-101 through HTTP-103, KG-101 and every deferred destination; all six
  prior finding/mitigation pairs. The entire triad and report were read.

The following source/documentation locators are preserved from the shared planning
and CTO evidence packet, not represented as new source inspection or external
research during this refresh:

- `docs/public/documentation/readme/configuration-and-operations/infisical.md`
- `docs/internal/SECRETS.md`
- `docs/internal/CONFIGURATION.md`
- `docs/internal/SECURITY-MODEL.md`
- `.agents/contract/intents.yaml` intents `secrets-authority` and `external-infrastructure-bootstrap`
- `src/Explore.Domain/Secrets/SecretDefinitionRegistry.cs`
- `src/Explore.Application/Services/AuthProviderConfigurationService.cs`
- `src/Explore.Infrastructure/Services/Keycloak/KeycloakBootstrapService.cs`
- `src/Explore.Infrastructure/Services/Keycloak/KeycloakAccountAuthorityLifecycleEmailService.cs`
- `src/Explore.Infrastructure/Services/AuthorizationProviderConfigurationService.cs`
- `src/Explore.Infrastructure/Services/CerbosConfigResolver.cs`
- `src/Explore.Infrastructure/Services/CerbosPolicyPackageService.cs`
- `src/Explore.API/BackgroundServices/CerbosPolicyBootSyncRunner.cs`
- Keycloak onboarding/settings controllers, DTOs, handlers, Blazor components, and focused tests identified by the implementation-plan evidence packet
- Cerbos official Admin API and server configuration documentation, accessed 2026-09-20
- Keycloak official client-management documentation describing client-credentials administration, accessed 2026-09-20

## Missing Evidence

- Executable invariant-breaker results and zero-secret canary output.
- Exact generated OpenAPI/client diff after contract cleanup.
- Multi-provider eradication and startup evidence.
- Runtime verification of the Keycloak lifecycle service client.
- Operator review of updated onboarding and upgrade instructions.
- Follow-up-specific I-VSD/CTO reviews and execution workspace report-transfer proof;
  PR 1 user approval and PC-CTO-r2 technical alignment are recorded, not missing.

## Context Inventory

- **Included:** shared repository-native source/test/doc and CTO evidence packet,
  the user's recorded accepted architecture and approval, exact current triad/report,
  I-VSD contracts, and local preservation hashes. Prior official-document research
  remains inherited evidence only; no external research or third-party source was
  accessed for revalidation.
- **Excluded:** secret values, provider response bodies, private tenant data,
  third-party implementation source, copied external prose/code, and
  religious-legal conclusions.
- **Evidence quality:** this refresh establishes design validation and scenario/task
  traceability against exact artifacts. Current-code claims retain the shared packet's
  evidence level; no new implementation, stakeholder, operational, audit, or scholarly
  validation is inferred. Business model/monetization, AI, content and religious-law
  behavior are unchanged; no new duty in those domains is claimed.

## Review Lifecycle

| Date | Previous status | New status | Trigger | Evidence/replacement |
|---|---|---|---|---|
| 2026-09-20 | none | draft | Initial planning-mode provider-responsibility review | Repository evidence packet; triad not yet authored |
| 2026-09-20 | draft | current | Original full-scope triad mapped every finding/mitigation | Original plan `061b171c2d8247f18ba4ad5854a54587275cee392939451d1d044dde31924a4d`, tasks `a3a2b0b71e4f716551a962965f68d4480fa58c29ed812cc8f0b5995034423e5d`; disposition `plan-aligned` at that revision only |
| 2026-09-20 | current | stale | PC-CTO-r2 mandatory split changed scope/task mappings; triad recorded staleness before report-owner refresh | Prior report `a919cac9b6ce9b8006b7645c840f274eca3d6f34a92b7669f19bd399100e767b`; old on-disk current claim invalidated |
| 2026-09-20 | stale | current | Owner re-evaluated all six pairs against HTTP-only PR 1 and named deferred PR/backlog owners | Exact final PC-CTO-r2 plan/tasks hashes below; disposition `plan-aligned`; all findings open; report-transfer gate still open |

## Planning Handoff

- **Workstream:** `provider-credential-onboarding`
- **Status:** current
- **Reviewed input:** final `PC-CTO-r2` plan
  `sha256:d65973b1c6299240a06429fdafa6fc440a7bbeb5148f972e65ff179f1255f9ab`;
  tasks
  `sha256:a06d6fde4ab01bab1e86a687932f1243f10cd6c29bf9a374dc3cf61962fa7ae2`
- **Findings and mitigations:** `IVSD-F001` → `IVSD-M001`; `IVSD-F002` → `IVSD-M002`; `IVSD-F003` → `IVSD-M003`; `IVSD-F004` → `IVSD-M004`; `IVSD-F005` → `IVSD-M005`; `IVSD-F006` → `IVSD-M006`
- **Required plan mappings:** verified against plan section 9 and tasks' deferred-work
  table below; original PC/KC/CB/DP IDs are retained design-packet references, not
  executable PR 1 tasks. `KG-101` assigns their durable graduation to packet K.
- **Escalations required before:** report transfer/workspace verification before PR 1
  product edits; each follow-up's separate I-VSD/CTO review before its implementation;
  anonymized Epistemic MAD before merge. User approval is already satisfied.
- **Technical alignment:** PC-CTO-r2 remains unchanged architecturally; clearing
  freshness does not certify implementation or close the report-transfer gate.
- **Refresh triggers:** any change to credential lifetime, provider authority,
  database secret storage, machine-identity paths, Keycloak runtime automation,
  Cerbos publishing ownership, operator recovery, or mapped scenarios/tasks

### Revalidated Mapping And Deferred Ownership

All six findings have lifecycle **open**. Owners below are the implementing agents
for the named PR/backlog packets, not evidence that those packets have shipped.
Backlog paths are planned destinations (not existing linked artifacts); until
`KG-101` creates them, the triad's D1-D6 packets remain authoritative.

| Finding / mitigation | Scenario / task mapping | PR 1 contribution and remaining owner |
|---|---|---|
| `IVSD-F001` / `IVSD-M001` | `SCN-CRED-001`-`004`; D1 `PC-101`, D2 `KC-201`, D4 `CB-301`; `KG-101` | No standing-credential removal; deferred PRs 2/3/5: runtime-authority, Keycloak-admin, Cerbos-publication |
| `IVSD-F002` / `IVSD-M002` | `SCN-PATH-001`-`002`; D6 `DP-401`-`DP-403`; `KG-101` | No acquisition/identity change; deferred PR 2 auth roots and PR 6 remaining deployment isolation |
| `IVSD-F003` / `IVSD-M003` | `SCN-STORE-001`-`003`; D1 `PC-101`-`PC-104`; `KG-101` | No data cleanup; deferred PR 2 runtime-authority, after writer/BFF convergence |
| `IVSD-F004` / `IVSD-M004` | `SCN-KC-003`, `SCN-CB-002`-`003`; D3 `KC-205`, D4 `CB-302`, D5 `CB-305`; `KG-101` | No runtime-admin removal; deferred PR 4 lifecycle identity and PR 5 coupled Cerbos publication/cutover |
| `IVSD-F005` / `IVSD-M005` | `SCN-HTTP-001`-`004`, `HTTP-101`-`HTTP-103`; remaining `SCN-OBS-001`-`003`, D1 `PC-101`, D2 `KC-202`, D4 `CB-302`; `KG-101` | Planned partial HTTP cache/replay mitigation only; PRs 2/3/5 own remaining retention/output/recovery evidence |
| `IVSD-F006` / `IVSD-M006` | `SCN-HTTP-004`, `HTTP-103`; `SCN-OPS-001`-`003`, D2 `KC-203`, D4 `CB-303`, D5 `CB-305`, D6 `DP-402`-`DP-403`; `KG-101` | Preserve existing paths and truthful retry docs; deferred cutover/recovery in PRs 2/3/5/6, no usability proof |

| Deferred owner | Durable destination assigned to KG-101 |
|---|---|
| PR 2 runtime-authority implementing agent (D1 + auth-only D6) | `dev/backlog/provider-credential-runtime-authority.md` |
| PR 3 Keycloak-admin implementing agent (D2) | `dev/backlog/provider-credential-keycloak-admin.md` |
| PR 4 lifecycle-identity implementing agent (D3) | `dev/backlog/provider-credential-keycloak-lifecycle.md` |
| PR 5 Cerbos-publication implementing agent (D4/D5 together) | `dev/backlog/provider-credential-cerbos-publication.md` |
| PR 6 deployment-isolation implementing agent (remaining D6) | `dev/backlog/provider-credential-deployment-isolation.md` |

The separate `dev/backlog/cerbos-custom-role-parity.md` destination remains outside
the credential program; removing no-op publication does not resolve that existing
authorization gap. No follow-up ordering or provider authority decision is changed
by this revalidation.
