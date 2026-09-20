<!-- ABOUTME: I-VSD planning assessment for configured-administrator headless instance onboarding. -->
<!-- ABOUTME: Maps trust, privacy, recovery, and self-hosting responsibilities into the implementation workstream. -->

# Headless Instance Onboarding — I-VSD Planning Assessment

Last Updated: 2026-09-01

## Review Metadata

- Mode: implementation closure
- Subject: configured-administrator headless instance onboarding
- Workstream: `headless-instance-onboarding`
- Report kind: provider-responsibility implementation assessment
- Report status: implemented and verified
- Disposition: implementation-aligned
- Evidence cutoff: 2026-09-01
- Reviewed input revision: `sha256:5c67de2a4b210f6d57239a6eb7f4c7ae38cf6bf4a0b9321e32c77410065a9ca9`
- Reviewed plan triad revision: `sha256:bddece5056ffa97ae877d3ddf86055c3036d27a06e99d5bf32e9def07d91a2bd`
- Implementation evidence:
  `5896449f3ae7f78f302cc8f4d85e29574f74a2a5`,
  `02e024a3e023a998209462610c48ed52f058d85b`, and
  `584be9624aa5c5367fe3d918d24866fd297acb07`
- Supersedes: none

## Scope

This assessment covers the provider-controlled design of first-run instance
onboarding when an operator supplies a portable ConfigurationManifest plus
deployment-local configuration naming one intended initial administrator.
The intended account may be an AT Protocol DID or a user already present in
the configured Keycloak realm. The system suppresses the interactive
onboarding UI, permits only the configured authentication provider before
completion, and grants initial platform authority only after the exact
configured identity authenticates successfully.

The offline Setup Assistant may generate `.env` and ConfigurationManifest
artifacts. It does not connect to, authenticate against, inspect, or mutate a
running instance.

## Claim Boundary

This is provider-responsibility design reasoning, not a fatwa, Sharia
certification, legal opinion, or proof that the resulting deployment is
ethically sufficient. The report evaluates choices under the platform
maintainer's control: identity authority, defaults, privacy boundaries,
recovery, operator clarity, and failure behavior. No halal/haram or contested
religious-legal conclusion is made.

## Findings

### IVSD-F001 — Initial authority must be intentionally assigned

- **Lifecycle:** accepted
- **Severity:** critical
- **Claim type:** provider-controlled security and governance responsibility
- **Principle/domain:** amanah (entrusted authority), justice, governance
- **Stakeholders:** instance operator, intended administrator, all instance
  users, affected tenants
- **Provider-controlled decision:** which authenticated identity may receive
  the first `platform.admin` grant
- **Evidence:** current completion assigns platform authority to the
  authenticated onboarding caller; current routing otherwise exposes only the
  interactive setup path
- **Risk:** "first successful login," email matching, usernames, handles, or
  provider-role claims could assign durable authority to the wrong person
- **Mitigation:** `IVSD-M001`
- **Owner/next validation:** Scenarios 3.1 and 3.2; Tasks 3.1, 3.2, 4.1, and 5.1
- **Escalation boundary:** implementation may not proceed if exact provider
  authority plus subject/DID matching is weakened

### IVSD-F002 — Portable configuration must not become privilege authority

- **Lifecycle:** accepted
- **Severity:** high
- **Claim type:** privacy, portability, and authority-boundary responsibility
- **Principle/domain:** amanah, privacy/satr, prevention of avoidable harm
- **Stakeholders:** self-hosting operators, named administrators, downstream
  instances receiving exported manifests
- **Provider-controlled decision:** whether provider subjects, DIDs, emails,
  or role grants enter ConfigurationManifest
- **Evidence:** the portability registry excludes PII, provider bindings,
  operational state, and deployment topology; manifest documentation excludes
  instance operator/provider authority
- **Risk:** exporting or copying a manifest could copy personal identifiers or
  silently reproduce a privilege assignment on another instance
- **Mitigation:** `IVSD-M002`
- **Owner/next validation:** Scenario 3.3; Tasks 1.1, 1.2, and 8.1
- **Escalation boundary:** any proposal to place a raw administrator selector
  or secret reference in the manifest requires a fresh I-VSD review

### IVSD-F003 — A configuration mistake must not irreversibly lock the operator out

- **Lifecycle:** accepted
- **Severity:** high
- **Claim type:** reliability, autonomy, and support responsibility
- **Principle/domain:** avoidance of harm, facilitation, accountable recovery
- **Stakeholders:** self-hosting operators and intended administrators
- **Provider-controlled decision:** when onboarding becomes complete and when
  the setup secret is locked
- **Evidence:** current completion locks setup authority only after the
  transaction; current binary completion state cannot represent a configured
  administrator waiting to authenticate
- **Risk:** completing at startup before identity proof can lock an instance
  around a mistyped or unavailable identity
- **Mitigation:** `IVSD-M003`
- **Owner/next validation:** Scenarios 3.4 and 3.5; Tasks 2.1, 3.2, 5.1, and 8.2
- **Escalation boundary:** automatic post-completion authority transfer remains
  forbidden without a separately approved governance design

### IVSD-F004 — Identity selectors must remain absent from observable surfaces

- **Lifecycle:** accepted
- **Severity:** high
- **Claim type:** privacy and security responsibility
- **Principle/domain:** privacy/satr, amanah, minimization
- **Stakeholders:** configured administrator and support/operations personnel
- **Provider-controlled decision:** what status, logs, metrics, health checks,
  errors, and support evidence disclose
- **Evidence:** provider subject, DID, issuer, and email are sufficient to
  identify or target a person/account; existing status and routing need only a
  bounded mode and provider kind
- **Risk:** subject or DID disclosure can enable correlation, targeted abuse,
  or credential-support leakage
- **Mitigation:** `IVSD-M004`
- **Owner/next validation:** Scenario 3.6; Tasks 1.2, 3.1, 4.1, and 8.1
- **Escalation boundary:** raw selectors, fingerprints, token claims, or
  profile values may not enter diagnostics

### IVSD-F005 — Offline tooling must remain advisory rather than authoritative

- **Lifecycle:** accepted
- **Severity:** medium
- **Claim type:** self-hosting and trust-boundary responsibility
- **Principle/domain:** autonomy, transparency, amanah
- **Stakeholders:** self-hosting operators and maintainers
- **Provider-controlled decision:** whether Setup Assistant gains live
  credentials or runtime mutation capability
- **Evidence:** the approved product boundary treats Setup Assistant as an
  offline generator for configuration artifacts
- **Risk:** adding live connectivity would create another credentialed control
  plane, expand attack surface, and blur responsibility for applied state
- **Mitigation:** `IVSD-M005`
- **Owner/next validation:** Scenario 3.7; Tasks 1.1 and 10.1
- **Escalation boundary:** any runtime connection, token storage, or live apply
  capability requires a separate plan and fresh trust review

## Recommendations

### IVSD-M001 — Exact authenticated identity claim

Accept only an exact match between the configured provider, configured
authority, and stable provider subject: Keycloak issuer plus `sub`, or the DID
returned by the verified AT Protocol OAuth security gateway. Never use email,
username, handle, display name, provider role, or login order as bootstrap
authority.

**Rejected alternatives:** first-login wins, verified-email matching,
Keycloak-role mirroring, or a browser-supplied identity selector.

### IVSD-M002 — Deployment-local selector and identity-free manifest

Keep bootstrap mode and administrator selector in deployment-local
environment/secret authority. Keep ConfigurationManifest responsible for
portable, non-secret instance and tenant configuration only. It may supply the
settings consumed by completion, but it never carries the identity selector,
PII, provider binding, role grant, or completion state.

**Rejected alternatives:** raw identity in manifest, secret references in
manifest, or exporting completed bootstrap authority.

### IVSD-M003 — Pending until proof, then atomic completion

Represent configured bootstrap as pending while authentication remains
possible. Complete onboarding, create or resolve local identity, grant roles,
and lock setup authority in one bounded transaction only after exact identity
proof. Before completion, configuration correction remains possible through
deployment configuration and restart. After completion, configuration changes
must never transfer authority automatically.

**Rejected alternatives:** startup-time unconditional completion or making the
pending state globally unready.

### IVSD-M004 — Bounded, value-free observability

Expose only state, provider kind, stable reason code, and operation outcome.
Never emit subject, DID, email, issuer URL, identity fingerprint, token claim,
profile name, or secret. Tests must scan captured logs and public status/error
contracts for these values.

### IVSD-M005 — Preserve the offline Setup Assistant boundary

Limit Setup Assistant work to validating and generating offline `.env` and
manifest artifacts. Runtime validation, authentication, claiming, recovery,
and completion remain server-owned.

## Stakeholders

| Stakeholder | Interest | Main risk | Planned protection |
|---|---|---|---|
| Intended initial administrator | Correct authority and private identity | Wrong-account grant or identifier disclosure | Exact provider identity proof and value-free telemetry |
| Self-hosting operator | Predictable unattended deployment and recovery | Irrecoverable configuration lockout | Pending state, fail-closed startup validation, explicit recovery |
| Ordinary users and tenants | Legitimate platform governance | Unauthorized administrator obtains system authority | Atomic, exact, server-side claim |
| Support and operations staff | Actionable diagnostics | Sensitive identity leaks into evidence | Stable reason codes and bounded status |
| Maintainers | Portable, maintainable architecture | Manifest/auth/setup boundaries drift | Clean Architecture ownership and architecture tests |

## I-VSD Principles And Domains

- **Amanah / entrusted authority:** initial platform authority is granted only
  to the identity intentionally configured and cryptographically authenticated.
- **Justice and due authority:** no user gains privilege by arrival order,
  mutable profile data, or an unrelated provider role.
- **Privacy/satr:** personal identifiers and provider selectors remain
  deployment-local and absent from portable/public/diagnostic artifacts.
- **Avoidance of harm:** misconfiguration fails closed without completing the
  instance or transferring authority.
- **Autonomy and portability:** self-hosters retain offline configuration and
  explicit recovery without a mandatory remote setup control plane.
- **Transparency:** status distinguishes interactive setup, configured pending
  claim, completion, and invalid configuration without exposing identity data.

## Validation Gaps

- No stakeholder testing has yet validated operator comprehension of the
  pending-authentication state and recovery instructions.
- No implementation evidence yet proves exact Keycloak issuer/subject matching,
  verified-DID matching, concurrency convergence, or zero-identifier logs.
- Multi-provider database and BFF routing evidence belongs to implementation,
  not this planning assessment.

## Escalation Needed

No scholarly determination is required for the approved technical scope.
Escalate before implementation approval if the design changes to infer
religious authority, identity legitimacy, or moral fitness from provider
metadata. Escalate for fresh technical and I-VSD review if identity enters the
manifest, Setup Assistant gains live connectivity, or post-completion
authority transfer becomes automatic.

## Evidence Reviewed

| Evidence ID | Repository evidence | Validation level |
|---|---|---|
| E-001 | `src/Explore.Application/Features/InstanceOnboarding/Handlers/Commands/CompleteInstanceOnboardingCommandHandler.cs` | Owning implementation read |
| E-002 | `src/Explore.API/Controllers/InstanceOnboardingController.cs` | Owning HTTP adapter read |
| E-003 | `src/Explore.Application/Authentication/PlatformIdentityPrincipalExtensions.cs` | Canonical claims authority read |
| E-004 | `src/Explore.Application/Authentication/CurrentUserResolutionExtensions.cs` | Canonical local-user resolver read |
| E-005 | `src/Explore.Application/Features/Users/Handlers/Commands/SyncUserCommandHandler.cs` | Existing synchronization flow read |
| E-006 | `src/Explore.Application/Features/Authentication/Atproto/Handlers/Commands/BootstrapAtprotoSessionCommandHandler.cs` | Verified-DID boundary read |
| E-007 | `src/Explore.Blazor/Services/BffAdminClaimsTransformation.cs` and `src/Explore.Blazor/Extensions/BffAuthEndpoints.cs` | BFF sign-in/gating flow read |
| E-008 | `src/Explore.Domain/InstanceBootstrapState.cs` and persistence configuration | Completion state and concurrency guard read |
| E-009 | `src/Explore.Application/Features/ConfigurationManifest/Catalog/ConfigurationPortabilityRegistry.cs` | Portability exclusion contract read |
| E-010 | `docs/CONFIGURATION_MANIFEST.md` and `docs/SELF_HOSTING.md` | Operator contract read |
| E-011 | `dev/active/headless-instance-onboarding/` plan, tasks, and context triad | Completed triad revalidated at `sha256:bddece5056ffa97ae877d3ddf86055c3036d27a06e99d5bf32e9def07d91a2bd` |

## Missing Evidence

- Implemented code and tests for the configured pending-claim state.
- Real relational concurrency evidence for simultaneous matching and
  nonmatching authentication.
- Captured BFF routing behavior for Keycloak and ATProto before completion.
- Release-fragment, generated contract, and operator documentation evidence.

## Context Inventory

- Stable workstream: `headless-instance-onboarding`
- Primary intent: `external-infrastructure-bootstrap` (Tier 1 Security)
- Supporting intents: `add-cqrs-handler`, `bff-auth-bug`,
  `openapi-contract-change`, and `add-ef-migration`
- Approved user decisions:
  - Setup Assistant remains offline-only.
  - ConfigurationManifest and `.env` establish unattended instance
    configuration.
  - The exact configured ATProto or Keycloak account receives initial
    administration after authentication.
  - Backward compatibility is explicitly not required.
- Shared-tree constraint: unrelated Setup Assistant, OpenAPI, generated-client,
  persistence, and agent-context work is present and must remain untouched.

## Common Overlooked Failures And Outcomes

- A Keycloak `sub` is realm-scoped; matching it without the configured issuer
  can bind the wrong authority after realm replacement.
- ATProto currently rejects an unknown DID before local session issuance; the
  bootstrap exception must occur only after cryptographic verification.
- Current user synchronization may auto-match verified email; that behavior
  must never choose the initial administrator.
- Completing at process startup can lock the instance around a mistyped
  selector before anyone proves control.
- Completing on a GET/status request violates HTTP semantics and hides a
  security mutation.
- Leaving the pending instance globally unready prevents the intended
  administrator from reaching authentication.
- Logging a subject, DID, issuer, or fingerprint defeats the private
  configuration boundary even when no password is logged.

## Planning Handoff

- Workstream: `headless-instance-onboarding`
- Status: current
- Reviewed input revision: `sha256:5c67de2a4b210f6d57239a6eb7f4c7ae38cf6bf4a0b9321e32c77410065a9ca9`
- Reviewed plan triad revision: `sha256:bddece5056ffa97ae877d3ddf86055c3036d27a06e99d5bf32e9def07d91a2bd`
- Findings and mitigations:
  - `IVSD-F001` -> `IVSD-M001`
  - `IVSD-F002` -> `IVSD-M002`
  - `IVSD-F003` -> `IVSD-M003`
  - `IVSD-F004` -> `IVSD-M004`
  - `IVSD-F005` -> `IVSD-M005`
- Required plan mappings:
  - `IVSD-F001/M001` -> exact-identity and adversarial claim scenarios/tasks
  - `IVSD-F002/M002` -> manifest-boundary scenario and configuration tasks
  - `IVSD-F003/M003` -> pending/recovery/concurrency scenarios and tasks
  - `IVSD-F004/M004` -> zero-disclosure scenario and observability tests
  - `IVSD-F005/M005` -> offline-tooling non-goal and architecture gate
- Escalations required before: implementation if any named refresh trigger is
  introduced; none for planning approval under the approved scope
- Refresh triggers: administrator identity enters ConfigurationManifest;
  Setup Assistant gains runtime connectivity; provider/subject matching is
  weakened; completion moves before authentication proof; automatic
  post-completion authority transfer is introduced; telemetry disclosure
  expands

## Review Lifecycle

| Date | Previous status | New status | Trigger | Evidence/replacement |
|---|---|---|---|---|
| 2026-09-01 | none | draft / ready-for-planning | Integrated implementation-plan intake | Evidence revision `sha256:5c67de2a4b210f6d57239a6eb7f4c7ae38cf6bf4a0b9321e32c77410065a9ca9` |
| 2026-09-01 | draft / ready-for-planning | current / plan-aligned | Completed triad mapping revalidation | Triad revision `sha256:e62b30f20c3bf6c0a4db9939d8daf7207593ed0845cf48abf0b6d548cd8d26fb` |
| 2026-09-01 | implementation active | current / plan-aligned | Phase 1 path-contract rebaseline | Added only `DotenvComposer.cs` after confirmed Red proved catalogue metadata could not execute value/matrix validation; triad revision `sha256:8de6ca33af025e1ea4d71cd16c258ba10ce6b5588a6d46d9916b089662ce2484` |
| 2026-09-01 | implementation active | current / plan-aligned | Phase 1 generated-output rebaseline | Added `docs/CONFIGURATION.md` because the canonical generator owns its environment-catalogue block atomically with machine JSON; triad revision `sha256:b6072d7df9b14522f2a236f23f97d559f63b4f7be24d239faef0a0e0d2d432ff` |
| 2026-09-01 | implementation active | current / plan-aligned | Phase 1 completed | Offline contract committed at `5896449f3ae7f78f302cc8f4d85e29574f74a2a5`; Phase 2 invariant Red active; triad revision `sha256:393c1c4d695310150976c858fcb7da69ee906058ab7a94e535e53c3a0cea93b0` |
| 2026-09-01 | implementation active | current / plan-aligned | Phase 2 architecture rebaseline | Moved typed schema/migration and active-caller cutover into Phase 2, retained Phase 3 for locking/convergence, and replaced reflective Red with strong typing; triad revision `sha256:bddece5056ffa97ae877d3ddf86055c3036d27a06e99d5bf32e9def07d91a2bd` |
