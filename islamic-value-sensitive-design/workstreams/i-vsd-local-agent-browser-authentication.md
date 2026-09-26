# Local Agent Browser Authentication - Provider Responsibility Review

Last Updated: 2026-09-26

## Review Metadata
- Mode: planning
- Subject: isolated development authentication and scoped browser personas
- Workstream: local-agent-browser-authentication
- Report kind: feature design
- Report status: current
- Disposition: plan-aligned
- Evidence cutoff: 2026-09-26
- Reviewed input: repository `91b246e96407207336887140def224d76c9b8835`; completed local-agent-browser-authentication plan/tasks revision r1
- Supersedes: none

## Scope
Development-only local authentication, administrative bootstrap, synthetic role
personas, dedicated Aspire resources, secret handling, and truthful verification
claims. No production access policy, commercial policy, religious-content policy,
or authentication-provider removal is proposed.

## Claim Boundary
This is provider-responsibility design reasoning, not a religious ruling,
certification, security certification, or evidence that an unimplemented feature
already works. Normative principles guide safeguards; runtime evidence remains
an implementation obligation.

## Findings
| ID | Lifecycle / severity / claim | Principle and domain | Stakeholder / provider decision | Evidence and validation | Mitigation / owner |
|---|---|---|---|---|---|
| IVSD-F001 | accepted / critical / design risk | Amanah and prevention of harm; access governance | Operators and users; whether development credentials can create production privilege | E1, E2: source inspection; original report embeds a shared credential and lacks deployment isolation | IVSD-M001; provisioning and topology tasks |
| IVSD-F002 | accepted / critical / design risk | Justice and entrusted authority; tenant boundaries | Tenants, organizers, attendees; exact scope of each persona's authority | E3: source inspection; roles exist at platform, tenant, organization and event scopes | IVSD-M002; persona and authorization acceptance tasks |
| IVSD-F003 | accepted / major / design risk | Privacy and data minimization | Developers and operators; whether secrets enter logs, screenshots, source or browser storage | E1, E4: source inspection; BFF antiforgery/cookie boundary already exists | IVSD-M003; secret configuration and browser protocol tasks |
| IVSD-F004 | accepted / major / evidence claim | Truthfulness and accountability | Future maintainers; whether local test success is advertised as exhaustive provider parity | E5: cited adapter test explicitly disclaims policy verification | IVSD-M004; verification and documentation tasks |

## Recommendations
- **IVSD-M001:** Require explicit opt-in, Development, the dedicated agent mode,
  isolated storage and a single selected secret authority before any privileged
  write. Preserve ordinary bootstrap and credential-replacement invariants.
  Reject collisions or unsupported topology without mutating existing accounts.
- **IVSD-M002:** Use synthetic identities and explicit scoped grants; verify
  both positive authority and wrong-tenant/wrong-event denials. Do not restore
  revoked privileges or reset established passwords on startup.
- **IVSD-M003:** Keep secret values out of committed material and telemetry;
  document key names only. Browser sign-in must use real antiforgery-protected
  BFF login with HttpOnly cookies, never fabricated sessions or browser tokens.
- **IVSD-M004:** Distinguish adapter tests, local policy tests, live Cerbos tests,
  real credential integration and browser evidence. Measure startup rather than
  asserting the report's unsupported latency guarantees.

Rejected alternatives: shared ordinary development storage creates an avoidable
privilege-contamination risk; a documented universal password creates avoidable
exposure; cookie fabrication hides the boundary being tested; treating local
authorization as exhaustive external-provider verification misrepresents evidence.

## Stakeholders
Developers and autonomous agents need repeatable access; maintainers need a
bounded implementation; tenant operators and attendees need isolation; self-hosters
need development conveniences that cannot silently become production defaults.
No stakeholder interviews or production-user studies were conducted.

## I-VSD Principles And Domains
Amanah informs credential custody and authority; justice informs bounded grants;
prevention of harm informs fail-closed provisioning; privacy informs minimization;
truthfulness informs test and performance claims. Applicable domains are
architecture, operations, development UX, documentation and evaluation.
Payments, ranking, religious content, moderation policy and monetization are
outside this workstream.

## Validation Gaps
No new code, migrations, profile, credentials or browser flow has been executed.
Concurrency, interruption recovery, no-secret output and cross-tenant denial
require implementation evidence. Planning alignment is not mitigation completion.

## Escalation Needed
No religious-legal question is raised by this scope. A future change involving
religious rulings must go to qualified Sunni scholarly authority. Any request
to allow production persona provisioning requires a new technical and provider
responsibility review rather than an implicit exception.

## Evidence Reviewed
- E1: `AGENTS.md` secret-authority, tenant isolation and development-mode rules;
  `docs/internal/SECRETS.md` selected-authority and bootstrap-password contract.
- E2: `src/Explore.Persistence/Identity/LocalIdentityCredentialStateStore.cs`
  ready-session, exact binding, and reconciliation invariants.
- E3: `src/Explore.Domain/Enums/RoleEnum.cs`;
  `src/Explore.Persistence/Seed/DatabaseSeeder.cs` and `SeedData.cs`.
- E4: `src/Explore.Blazor/Extensions/BffAuthEndpoints.cs` local login admission
  and session creation; existing BFF integration tests.
- E5: `tests/Explore.Infrastructure.Tests/Authorization/ProviderNeutralAuthorizationParityTests.cs`,
  `LocalProviderParityLaneTests.cs`, and
  `tests/Event.API.IntegrationTests/Features/CerbosProviderParityLaneTests.cs`.
- Source proposal: `auth-testing-and-agent-browser-profile-report.md`, dated
  2026-09-26, found in the prior agent's artifact directory. Claims were checked
  against repository evidence rather than accepted as implementation facts.
- E6: `dev/active/local-agent-browser-authentication/local-agent-browser-authentication-plan.md`
  and `local-agent-browser-authentication-tasks.md`, revision r1. Scenarios
  S1-S8 and mapped tasks were revalidated against the four accepted mitigations.

## Missing Evidence
Completed implementation, runtime verification, measured startup times,
production operational feedback and stakeholder validation.

## Context Inventory
Scope and no-backward-compatibility preference are explicit user instructions.
Existing source and the preceding CTO review provide the shared evidence packet.
No new external code, dependency, deployment, or provider policy is introduced.

## Common Overlooked Failures And Outcomes
Fresh credentials with incomplete bindings must not authenticate. Reusing a
database must not promote unrelated users. A failed persona switch must not leave
the previous administrator mistaken for the next user. Completed-state restarts
must not repair deliberate password or privilege changes. Local test success
must not be represented as proof of every Cerbos or OIDC behavior.

## Review Lifecycle
| Date | Previous status | New status | Trigger | Evidence/replacement |
|---|---|---|---|---|
| 2026-09-26 | none | draft | Planning intake from report and source-grounded CTO review | E1-E5 |
| 2026-09-26 | draft | current | Revalidated completed r1 scenarios, boundaries and task mappings | E6; plan-aligned, not implementation-verified |

## Planning Handoff
- Workstream: local-agent-browser-authentication
- Status: current
- Reviewed input: completed local-agent-browser-authentication plan/tasks r1
- Findings and mitigations: IVSD-F001 -> IVSD-M001; IVSD-F002 -> IVSD-M002;
  IVSD-F003 -> IVSD-M003; IVSD-F004 -> IVSD-M004
- Required plan mappings, verified in plan Section 9:
  - IVSD-F001 / IVSD-M001 -> S1/S5/S6; Tasks 1.1-1.2, 2.1-2.2, 3.1-3.2.
  - IVSD-F002 / IVSD-M002 -> S4/S6; Tasks 2.1-2.2, 4.1.
  - IVSD-F003 / IVSD-M003 -> S1/S7; Tasks 1.1-1.2, 2.1-2.2, 4.1-4.2.
  - IVSD-F004 / IVSD-M004 -> S2/S8; Tasks 3.1-3.2, 4.1-4.2 and final gate.
- Escalations required before: none within the defined development-only scope
- Refresh triggers: production support, changed storage isolation, changed
  credential authority or lifecycle, broader role grants, browser token exposure,
  altered verification claims, or changed mapped scenarios/tasks.
