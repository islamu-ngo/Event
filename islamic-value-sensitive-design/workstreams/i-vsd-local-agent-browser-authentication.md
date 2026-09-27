# Local Agent Browser Authentication - Provider Responsibility Review

Last Updated: 2026-09-27

## Review Metadata
- Mode: planning
- Subject: isolated development authentication and scoped browser personas
- Workstream: local-agent-browser-authentication
- Report kind: feature design
- Report status: current
- Disposition: plan-aligned
- Evidence cutoff: 2026-09-27
- Reviewed input: repository `91b246e96407207336887140def224d76c9b8835`; completed local-agent-browser-authentication plan/tasks revision r2; current local-agent implementation and browser evidence
- Reviewed plan SHA-256: `f1dece79f7ce60de23146d00f0d776a3277c1612bd3a5eb4f2349899f6e6489e`
- Reviewed tasks SHA-256: `e4273d84354a9e5c77bc5c5a4c0f467e43a63f99b51cedcc1a4b65f9b6147935`
- Planning freshness check: HEAD `67d6069fb62959d7f8ed9e71bc10a9b8d2e390a8`; implementation evidence is separately reviewed below
- Supersedes: none

## Scope
Development-only local authentication, administrative bootstrap, synthetic role
personas, dedicated Aspire resources, secret handling, and truthful verification
claims. No production access policy, commercial policy, religious-content policy,
or authentication-provider removal is proposed.

## Claim Boundary
This is provider-responsibility design reasoning, not a religious ruling,
certification, or security certification. Normative principles guide safeguards;
the implementation observations below prove only their named local behavior,
not production outcomes or provider-wide parity.

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
  This includes early Standalone/migrator admission, suppressing ordinary
  business seeds in agent mode, ownership/secret preflight under a lock, and
  state dispatch before initialization operations on replay.
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
The original planning review preceded implementation. The Development-only
profile, native credential lifecycle, scoped HTTP denials, and six independent
Local browser sign-ins now have local evidence. The real Local corpus passed
seven tests, and one independent live Cerbos integration lane passed outside
the Local-only profile; neither establishes cross-provider equivalence.
Restart persistence and measured cold/warm startup remain unverified: the
selected Infisical project lacks the required `/postgresql` folder, so the
profile fails closed before resources launch. Production outcomes and operator
studies are not established by this local workstream.

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
  and `local-agent-browser-authentication-tasks.md`, revision r2. Scenarios
  S1-S8 and mapped tasks were revalidated against the four accepted mitigations.
- E7: adversarial plan review checked `src/Event.Standalone/Program.cs`,
  `src/Event.MigrationService/Worker.cs`, `ExploreDatabaseMigrator.cs`,
  `LocalAdministratorBootstrapOperation.cs`, configured bootstrap preparation,
  JWT validation configuration and credential replay checks. The lead corrected
  the four startup/replay blockers in r2 and revalidated IVSD-M001/M003.
- E8: current implementation in `src/Explore.API/Hosting/AgentBrowserPersonaStartup.cs`,
  `src/Explore.Persistence/Seed/AgentBrowserPersonaBindingSeeder.cs`,
  `src/Explore.API/Middleware/ApiTenantResolutionMiddleware.cs`, and the BFF
  Local login/session boundary. PostgreSQL
  `AgentBrowserPersonaHttpTests` exercises Local subjects, HAL authority,
  wrong-tenant denial and tenantless administrator login without admitting
  tenant-scoped reads or self-deletion.
- E9: task-owned `.omo/evidence/20260926-local-agent-browser-authentication/qa.md`
  records fresh visible-form browser sessions for all six synthetic personas,
  anonymous control, BFF status identities and scoped UI affordances. It
  records observed behavior, not possession of credentials or a certification.
- E10: the separate `LocalProviderParityLaneTests` run passed 7/7 and the live
  `CerbosProviderParityLaneTests` run passed 1/1. The latter used its own
  integration topology, not the Local-only agent profile. An owned profile
  restart returned `secret_authority_unavailable`; read-only authority
  probes found a healthy login and twelve readable folders but no
  `/postgresql` folder. The existing isolated database volume was preserved.
- E11: independent security review found a stale mutation-side cache key after
  the event-detail read became tenant-qualified. A real anonymous
  read/authorized Local PATCH/anonymous read failed on the old title, then
  passed after every event mutation switched to the existing per-event cache
  tag; the complete five-case persona HTTP class passed. Independent
  operations review confirmed the authority restart gap rather than
  treating a preserved volume as proof of restart success.

## Missing Evidence
Successful final profile restart with the original database credential,
measured cold/warm observations, production operational feedback and
stakeholder validation. Local security review findings are resolved at the
tested HTTP boundary; neither that result nor the independent Cerbos lane
establishes production or provider-wide certification.

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
| 2026-09-26 | current | stale | Adversarial review found earlier mutation and replay gaps in r1 | E7 |
| 2026-09-26 | stale | current | Revalidated r2 early admission, lookup-only migrator, pre-write authority checks and completed replay | E6-E7; plan-aligned, not implementation-verified |
| 2026-09-27 | current | current | Added bounded implementation and browser observations without changing the planning disposition | E8-E9; final restart and provider-specific gates remain open |

## Planning Handoff
- Workstream: local-agent-browser-authentication
- Status: current
- Reviewed input: completed local-agent-browser-authentication plan/tasks r2
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
