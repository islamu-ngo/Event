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
- Reviewed input: current working-tree r3 plan, tasks, context, and task-owned Phase 4 and Phase 5 QA evidence for `local-agent-browser-authentication`
- Baseline Git object: `8c99a27b366365fd0db9252459e63b4e828f2032` (Phase 4 documentation); subsequent Phase 5 working-tree evidence is reviewed below
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
| IVSD-F001 | accepted / critical / design risk | Amanah and prevention of harm; access governance | Operators and users; whether development credentials can create production privilege | E1, E2, E6: source and r3-plan review | IVSD-M001; provisioning, topology, and S10 reset tasks |
| IVSD-F002 | accepted / critical / design risk | Justice and entrusted authority; tenant boundaries | Tenants, organizers, attendees; exact scope of each persona's authority | E3, E8, E12: source and Phase 4 observed authority checks | IVSD-M002; persona and authorization acceptance tasks |
| IVSD-F003 | accepted / major / design risk | Privacy and data minimization | Developers and operators; whether secrets enter logs, screenshots, source or browser storage | E1, E4, E12: source and real BFF/browser observations | IVSD-M003; secret configuration and browser protocol tasks |
| IVSD-F004 | accepted / major / evidence claim | Truthfulness and accountability | Future maintainers; whether local success is advertised as exhaustive provider parity or unmeasured performance | E5, E10, E12: provider-lane boundaries and measured local observations | IVSD-M004; verification, documentation, and S9 tasks |
| IVSD-F005 | open / major / bounded residual risk | Privacy, amanah, and accountability; data lifecycle | Developers, data subjects, and operators; whether an agent database reset is misrepresented as privacy erasure or removes audit/erasure state | E6, E13-E15: database-only reset and preservation are observed; replay of a prior erasure receipt against recreated synthetic IDs is unproven | IVSD-M005; S10 / Tasks 5.1-5.2 |
| IVSD-F006 | open / major / future product boundary | Privacy and prevention of harm; spatial-data governance | Attendees and operators; whether PostGIS readiness is treated as approval to collect or publicly expose LocationPii | E6, E13-E14: installed extension and spatial expression on an agent-only image, no product location feature | IVSD-M006; S9 / Tasks 5.1-5.2 |

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
  real credential integration and browser evidence. Report measured startup
  observations without converting them into a latency guarantee or provider-wide claim.
- **IVSD-M005:** Keep S10 database reset narrowly database-only: preserve the
  embedded privacy-erasure authority, audit/erasure obligations, Redis, Mailpit,
  and storage unless a separately authorized lifecycle governs them. Keep readiness
  closed on reset failure and document that a test reset is not privacy erasure.
- **IVSD-M006:** Treat a development-only PostGIS image and extension smoke as
  engine readiness only. The recorded development image passed the license-policy
  validator, but redistribution and a product spatial feature remain separate
  decisions. Prohibit public LocationPii collection, proximity behavior, or
  disclosure until separately reviewed and approved.

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
Phase 4 has local, task-owned evidence from a user-authorized newly initialized
agent volume: six fresh visible-form Local sign-ins, scoped positive and negative
checks, cold readiness of 52,682 ms, and no-reset warm readiness of 33,652 ms
and 41,833 ms. The retained newly initialized administrator cookie survived the
second warm restart; the original pre-reset session was intentionally invalidated
and is not represented as continuous. The real Local corpus passed seven tests,
and one independent live Cerbos integration lane passed outside the Local-only
profile; neither establishes cross-provider equivalence. Phase 5 then exercised
the agent-only PostgreSQL 18/PostGIS image, preserved extension and lookup data,
and completed two distinct owner-scoped reset entrypoints. Six real API HTTP
logins passed after reset; the final warm API/BFF readiness was 32,845 ms.
The native reset tool measured 2,823 ms; the Aspire command measured 3,860 ms
before the tenant-wide storage guard and 4,170 ms afterward request-to-ready:
the 2,000 ms target was missed, not silently graduated into
a guarantee. Production outcomes, operator studies, stale erasure-receipt
behavior, and public location-data approval are not established by this local
workstream.

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
- E6: current `dev/active/local-agent-browser-authentication/` plan, tasks,
  and context, revision r3, reviewed at the evidence cutoff. The plan
  adds S9/S10 and Phase 5; their implementation evidence is E14-E15.
- E7: adversarial plan review checked `src/Event.Standalone/Program.cs`,
  `src/Event.MigrationService/Worker.cs`, `ExploreDatabaseMigrator.cs`,
  `LocalAdministratorBootstrapOperation.cs`, configured bootstrap preparation,
  JWT validation configuration and credential replay checks. The lead corrected
  the four startup/replay blockers in r2 and revalidated IVSD-M001/M003.
- E8: current implementation in `src/Explore.API/Hosting/AgentBrowserPersonaStartup.cs`,
  `src/Explore.Persistence/Seed/AgentBrowserPersonaBindingSeeder.cs`,
  `src/Explore.API/Middleware/ApiTenantResolutionMiddleware.cs`, and the BFF
  Local login/session boundary. PostgreSQL `AgentBrowserPersonaHttpTests`
  exercises Local subjects, HAL authority, wrong-tenant denial and tenantless
  administrator login without admitting tenant-scoped reads or self-deletion.
- E9: task-owned `.omo/evidence/20260926-local-agent-browser-authentication/qa.md`
  records fresh visible-form browser sessions for all six synthetic personas,
  anonymous control, BFF status identities and scoped UI affordances. It
  records observed behavior, not possession of credentials or a certification.
- E10: the separate `LocalProviderParityLaneTests` run passed 7/7 and the live
  `CerbosProviderParityLaneTests` run passed 1/1. The latter used its own
  integration topology, not the Local-only agent profile.
- E11: independent security review found a stale mutation-side cache key after
  the event-detail read became tenant-qualified. A real anonymous
  read/authorized Local PATCH/anonymous read failed on the old title, then
  passed after every event mutation switched to the existing per-event cache
  tag; the complete five-case persona HTTP class passed.
- E12: the same QA ledger records a user-authorized fresh-volume recovery,
  then six fresh visible-form sign-ins and scoped/negative checks. Readiness
  was measured from readiness-watch registration through both HTTP 200
  responses: cold 52,682 ms; no-reset warm 33,652 ms and 41,833 ms. The
  retained new administrator cookie returned authenticated status and the
  admin control plane after the second warm restart. The pre-reset cookie was
  intentionally invalidated by the authorized reset and is not claimed to survive.
- E13: r3's S9/S10 and Phase 5 tasks define PostgreSQL-only enforcement,
  PostGIS image/license review, and a database-only reset that preserves
  privacy-erasure/audit-adjacent state. QA's isolated PostGIS candidate
  preflight is feasibility evidence, not Phase 5 implementation or approval
  to expose LocationPii.
- E14: `Explore.AppHost/AppHost.cs` selects the digest-pinned development-only
  PostgreSQL 18/PostGIS image, and the agent migrator initialized extension
  3.6.4 in `islamu_event`. A live `ST_DWithin` expression returned true after
  the Aspire and process-ID reset commands, including the final tenant-aware
  Aspire reset; the legal dependency record passed
  `.ci/scripts/validate-dependency-license-policy.cs`. The native tool reported
  2,823 ms, the earlier Aspire command 3,860 ms, and the final Aspire command
  4,170 ms, all above target.
- E15: `AgentDatabaseResetLifecycleTests` and
  `AgentBrowserPostgresConcurrencyTests` passed 10/10 and 4/4 after the
  tenant-wide storage guard against disposable PostgreSQL. An unlocked
  tenant-specific S3 override first made reset proceed despite a Local instance
  policy; the exact real PostgreSQL test then passed after instance and tenant
  effective routes were checked before purge. The suites also cover the real
  C# tool, both owner-scoped pipe addresses, HTTP/worker/Quartz admission,
  partial native recovery, stale token denial, tenant denial and lookup
  preservation. The native startup/replay class passed 16/16 after the guard.
  The API/BFF Local OIDC readiness fix was confirmed through HTTP 200 and a
  two-provider regression; six selected-vault persona API logins returned
  success after an owned database reset. A security review found arbitrary
  vault-key export in the launcher; the corrected six-key allowlist reached
  both API and BFF HTTP readiness without emitting values.

## Missing Evidence
The two-second full reset target is not met in measured use; a later change
would require optimization and fresh measurement rather than a claim here.
Additional evidence is needed for stale erasure receipts across recreation of
synthetic IDs, non-amd64 PostGIS distribution, production operational feedback,
stakeholder validation, and separate approval for any LocationPii collection
or public exposure. Local
security findings are resolved only at the tested HTTP boundary; neither that
result nor the independent Cerbos lane establishes production or provider-wide
certification.

## Context Inventory
Scope and no-backward-compatibility preference are explicit user instructions.
The reviewed r3 triad and task-owned QA ledger provide the shared evidence packet.
Phase 5 introduces a development-only PostGIS image, native reset tool and
Aspire resource command. Their local provenance and license-policy checks
passed; public-location and separate redistribution boundaries remain open.

## Common Overlooked Failures And Outcomes
Fresh credentials with incomplete bindings must not authenticate. Reusing a
database must not promote unrelated users. A failed persona switch must not leave
the previous administrator mistaken for the next user. Completed-state restarts
must not repair deliberate password or privilege changes. A database-only test
reset must not be called privacy erasure or silently remove audit/erasure state.
PostGIS readiness must not enable unapproved public LocationPii exposure. Local
test success must not be represented as proof of every Cerbos or OIDC behavior.

## Review Lifecycle
| Date | Previous status | New status | Trigger | Evidence/replacement |
|---|---|---|---|---|
| 2026-09-26 | none | draft | Planning intake from report and source-grounded CTO review | E1-E5 |
| 2026-09-26 | draft | current | Revalidated completed r1 scenarios, boundaries and task mappings | E6; plan-aligned, not implementation-verified |
| 2026-09-26 | current | stale | Adversarial review found earlier mutation and replay gaps in r1 | E7 |
| 2026-09-26 | stale | current | Revalidated r2 early admission, lookup-only migrator, pre-write authority checks and completed replay | E6-E7; plan-aligned, not implementation-verified |
| 2026-09-27 | current | current | Added bounded implementation and initial browser observations without changing the planning disposition | E8-E9; provider-specific gates remained open |
| 2026-09-27 | current | current | Revalidated r3 plan/tasks/context; replaced stale restart gap with user-authorized fresh-volume Phase 4 evidence and added S9/S10 findings | E6, E12-E13; Phase 5 remains planned, not implemented or verified |
| 2026-09-27 | current | current | Reviewed owner-scoped database reset, agent-only PostGIS image, allowlisted vault launcher and actual Local health/readiness; retained the missed latency target and unproven erasure replay as open limits | E14-E15; local Phase 5 behavior observed, not production/privacy certification |

## Planning Handoff
- Workstream: local-agent-browser-authentication
- Status: current
- Reviewed input: local-agent-browser-authentication r3 plan/tasks/context and Phase 4/5 QA evidence
- Findings and mitigations: IVSD-F001 -> IVSD-M001; IVSD-F002 -> IVSD-M002;
  IVSD-F003 -> IVSD-M003; IVSD-F004 -> IVSD-M004; IVSD-F005 -> IVSD-M005;
  IVSD-F006 -> IVSD-M006
- Required plan mappings, verified in plan Section 9 and Phase 5 tasks:
  - IVSD-F001 / IVSD-M001 -> S1/S5/S6/S10; Tasks 1.1-1.2, 2.1-2.2, 3.1-3.2, 5.1-5.2.
  - IVSD-F002 / IVSD-M002 -> S4/S6; Tasks 2.1-2.2, 4.1.
  - IVSD-F003 / IVSD-M003 -> S1/S7; Tasks 1.1-1.2, 2.1-2.2, 4.1-4.2.
  - IVSD-F004 / IVSD-M004 -> S2/S8/S9; Tasks 3.1-3.2, 4.1-4.2, 5.1-5.2 and final gate.
  - IVSD-F005 / IVSD-M005 -> S10; Tasks 5.1-5.2.
  - IVSD-F006 / IVSD-M006 -> S9; Tasks 5.1-5.2.
- Task ledger note: its execution checklist may evolve; this report records
  provider-responsibility mappings and evidence boundaries, not task completion.
- Escalations required before: distribution of the PostGIS image beyond the
  recorded development-only boundary and any LocationPii collection or public
  exposure require separate review and approval.
- Refresh triggers: production support, changed storage isolation, changed
  credential authority or lifecycle, broader role grants, browser token exposure,
  changed reset/erasure boundary, PostGIS/image or LocationPii scope, altered
  verification claims, or changed mapped scenarios/tasks.
