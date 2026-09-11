<!-- ABOUTME: I-VSD planning report for the test-suite health remediation workstream. -->
<!-- ABOUTME: Records provider-responsibility findings, mitigations, and escalation gates for restoring a trustworthy verification lane. -->

# Test Suite Health Remediation — I-VSD Planning Report

Last Updated: 2026-09-03

## Review Metadata
- Mode: planning
- Subject: Repository verification lane health and the integrity of safety evidence it produces
- Workstream: test-suite-health-remediation
- Report kind: planning report
- Report status: plan-aligned
- Disposition: plan-aligned
- Evidence cutoff: 2026-09-03
- Reviewed input revision: `9e8a728595bb9ac7a6d41b3ac3a2afb83572872d`
- Supersedes: none

## Scope

This report covers provider-controlled decisions in restoring the repository's automated verification lane to a trustworthy state. In scope: how failing tests are dispositioned, whether safety-relevant coverage may be weakened to reach green, and the operator-facing defects the failures exposed (self-hosted BFF availability without an identity provider, public Web Push endpoints without VAPID configuration, an unclassified mutating instance-administrator command, and a test lane that performs unbounded external network egress).

Out of scope: product feature behavior, scholarly rulings, and the licensing/IP posture of test dependencies.

## Claim Boundary

Claims describe the repository at revision `f49dea080` as executed on a single Linux workstation with rootless Podman. The Persistence and API integration lanes were executed but at least one earlier run was terminated by a harness deadline; counts for those two lanes are lower bounds pending the completed baseline recorded in the workstream context. No claim is made about CI-hosted results, other operating systems, or provider behavior under real Keycloak/Infisical endpoints.

## Findings

| ID | Lifecycle | Severity | Claim type | Principle / Domain | Stakeholder | Provider-controlled decision | Evidence | Validation | Mitigation |
|---|---|---|---|---|---|---|---|---|---|
| `IVSD-F001` | open | High | Risk | Amanah (trust), truthfulness | Operators, contributors, future maintainers | Whether failing safety tests may be deleted, skipped, or weakened to reach a green board | 190+ failures across 6 lanes; `.agents/contract/intents.yaml::test-suite-rationalization.forbidden_without_approval` prohibits weakening critical coverage without stronger replacement | Repository-verified | `IVSD-M001` |
| `IVSD-F002` | open | High | Harm | Amanah to self-hosters; anti-lock-in | Self-hosting operators, air-gapped deployments | Whether the Blazor BFF must serve its application shell when no identity provider is configured | `BffNoKeycloakResilienceTests.StaticPages_AreAccessible` expects 200, observed 302; `.agents/rules/auth-trust-boundaries.md` rule 6 mandates air-gapped fallback | Repository-verified | `IVSD-M002` |
| `IVSD-F003` | open | Medium | Harm | Ihsan (excellence), operator dignity | Self-hosting operators, end users | Whether unconfigured optional capabilities may fail with opaque 500 responses on public endpoints | `EndpointAuthorizationMatrixTests.Public_Get_Endpoints_ReturnOk`: `/api/notification/web-push/config` and `/vapid-public-key` return 500 InternalServerError | Repository-verified | `IVSD-M003` |
| `IVSD-F004` | open | High | Risk | Fail-closed stewardship of authority | All tenants, instance operators | Whether a mutating instance-administrator claim command may remain outside the authorization classification inventory | `AuthorizationSurfaceGuardrailTests` reports `ClaimConfiguredInstanceAdministratorCommand` as unclassified | Repository-verified | `IVSD-M004` |
| `IVSD-F005` | open | Medium | Risk | Sovereignty, no silent external dependency | Contributors, CI operators | Whether a unit-test lane may perform unbounded outbound network calls | `Explore.Secrets.UnitTests` produced one line of output and was killed at 30 minutes; discovery alone completes in 547 ms | Repository-verified | `IVSD-M005` |
| `IVSD-F006` | open | Low | Risk | Truthfulness of invariants | Contributors | Whether test fixtures may seed identifiers that violate a production domain invariant | `SetupSecretAuthorizationMatrixTests.SeedInstanceAdminAsync` seeds `Guid.NewGuid()`; `InstanceBootstrapState.RequireUuidV7` rejects it | Repository-verified | `IVSD-M006` |
| `IVSD-F007` | open | Medium | Risk | Honest reporting | Maintainers, reviewers | Whether cascading fixture failures may be reported as independent defects, inflating or masking real risk | 72 `ObjectDisposedException` failures in 5 API classes; `TagControllerTests` passes 9/9 in isolation | Repository-verified | `IVSD-M007` |

## Recommendations

- `IVSD-M001`: Require an explicit written disposition for every failing cohort before any deletion or skip — retained, semantically replaced by a stronger public-seam test, or intentionally removed behavior. Security, tenant-isolation, privacy, and money-adjacent cohorts fail closed: they may not be deleted without a passing stronger replacement. Deletion justified only by "it is failing" is prohibited.
- `IVSD-M002`: Treat the BFF application shell as anonymous-by-contract and restore a 200 response for the shell when no identity provider is configured, keeping authenticated areas fail-closed. Retain the resilience test as the regression anchor rather than relaxing its assertion.
- `IVSD-M003`: Define a graceful, documented contract for unconfigured optional capabilities on public read endpoints — a disabled-state payload rather than a 500 — so operators can diagnose configuration without reading server logs.
- `IVSD-M004`: Classify the instance-administrator claim command inside the authorization surface and anchor it with an invariant test asserting fail-closed behavior for an unauthorized actor. Do not resolve this by adding the command to a bypass allowlist.
- `IVSD-M005`: Remove external network egress from the secrets unit lane by bounding the transport or substituting a fake, so the lane is deterministic and cannot hold CI hostage.
- `IVSD-M006`: Align test fixtures with the production identifier invariant (UUIDv7) rather than relaxing the domain guard.
- `IVSD-M007`: Report cascading fixture failures separately from independent defects in the workstream evidence, and fix fixture lifetime so a single disposal cannot mask genuine regressions.

Rejected alternatives:
- Quarantining the failing lanes behind a skipped category to reach a green board. Rejected: it converts absent evidence into apparent safety, directly contradicting `IVSD-F001` and the intent's fail-closed gate.
- Relaxing `StaticPages_AreAccessible` to accept a redirect. Rejected: it would encode a self-hosting regression as intended behavior.

## Stakeholders

- Self-hosting and air-gapped operators — most affected by `IVSD-F002` and `IVSD-F003`; they cannot rely on a hosted control plane.
- Tenants and instance operators — bear the risk of `IVSD-F004` if instance-administrator authority is not fail-closed.
- Contributors and reviewers — depend on the lane being honest (`IVSD-F001`, `IVSD-F007`) and fast/deterministic (`IVSD-F005`).
- End users — indirectly affected; no direct user-facing behavior change is planned beyond restoring intended availability.

## I-VSD Principles And Domains

- **Amanah (trust/stewardship)**: A verification lane is the custodian of safety claims. Suppressing failures to appear green is a breach of stewardship, which is why `IVSD-M001` gates deletion.
- **Truthfulness (sidq)**: Reports must distinguish cascade collateral from real defects (`IVSD-M007`) and must not present unexecuted lanes as passing.
- **Ihsan (excellence)**: Operator-facing failure modes should be legible (`IVSD-M003`) rather than opaque server errors.
- **Anti-lock-in / sovereignty**: The platform's self-hosting promise requires the shell to work without proprietary or external identity infrastructure (`IVSD-M002`) and test lanes not to depend on external networks (`IVSD-M005`).
- **Justice in authority**: Administrative authority must be classified and fail closed (`IVSD-M004`).

Non-applicable domains: AI/ranking behavior, monetization and refunds, moderation, and content policy — this workstream changes no such provider behavior.

## Validation Gaps

- Persistence and API integration counts are lower bounds; one earlier sweep was terminated by a harness deadline before those lanes finished.
- The precise redirect target for the BFF shell 302 was not captured; the remediation must confirm whether it targets login or the setup gate before choosing the fix.
- No CI-hosted execution was observed; all evidence is from a single workstation with rootless Podman.

## Escalation Needed

No scholarly or legal escalation is required. This workstream involves engineering stewardship, not a contested ruling. Escalation to the Project Steward is required only if remediation would require deleting a security, tenant-isolation, or privacy cohort without a passing stronger replacement.

## Evidence Reviewed

- Executed all 22 test projects (21 under `tests/`, plus `eng/release/tests/ISLAMU.ReleaseEngineering.Tests`).
- `.agents/contract/intents.yaml::test-suite-rationalization`
- `.agents/rules/tests.md` (container runtime contract), `.agents/rules/auth-trust-boundaries.md`
- `tests/Event.API.IntegrationTests/Features/SetupSecretAuthorizationMatrixTests.cs`, `.../SetupSecretFlowTests.cs`, `.../EndpointAuthorizationMatrixTests.cs`
- `tests/Event.Application.UnitTests/Features/ConfigurationManifest/ConfigurationManifestContractTests.cs`
- `tests/Explore.Blazor.IntegrationTests/Endpoints/BffNoKeycloakResilienceTests.cs`
- `eng/release/tests/ISLAMU.ReleaseEngineering.Tests/ReleaseInputPolicyTests.cs`
- `src/Explore.API/Controllers/NotificationController.cs`, `src/Explore.Domain/InstanceBootstrapState.cs`, `src/Event.Wire.Contracts/ConfigurationPortability/ConfigurationManifestV1Alpha2.cs`

## Missing Evidence

- Completed, uninterrupted Persistence baselines (recorded as lower bound ≥108; Task 6.1 produces completed counts via sharded class-filtered runs to prevent 30-minute runner timeouts).
- The exact resolution mechanism between `NoKeycloakBlazorBffWebApplicationFactory` mock and `HandleStartupRedirectAsync` in `MiddlewareExtensions.cs` (which redirects `/` to `/setup` on `InteractivePending`).
- Whether CI currently executes the Secrets lane, and if so how it avoids the hang.

## Context Inventory

- Provided: verified current-state evidence packet, stable task name, proposed scope, affected stakeholders, provider-controlled decisions.
- Not provided by the user and not assumed: any instruction to delete cohorts, relax invariants, or accept reduced coverage. The user's directive was explicitly the opposite — eliminate technical debt and reach a genuinely clean state.

## Review Lifecycle

| Date | Previous status | New status | Trigger | Evidence/replacement |
|---|---|---|---|---|
| 2026-09-02 | (none) | draft | Planning intake for `test-suite-health-remediation` | Full 22-project execution at `f49dea080` |
| 2026-09-02 | draft | plan-aligned | Senior CTO review & triad rewrite | CTO feedback incorporated; triad rewritten without relaxing any safety mitigation (`IVSD-M001` through `IVSD-M007` strictly preserved) |
| 2026-09-03 | plan-aligned | plan-aligned | Senior CTO review update & technical approval | Planning triad verified and approved for implementation; no refresh trigger fired |

## Planning Handoff
- Workstream: test-suite-health-remediation
- Status: plan-aligned
- Reviewed input revision: `9e8a728595bb9ac7a6d41b3ac3a2afb83572872d`
- Findings and mitigations: `IVSD-F001 -> IVSD-M001`, `IVSD-F002 -> IVSD-M002`, `IVSD-F003 -> IVSD-M003`, `IVSD-F004 -> IVSD-M004`, `IVSD-F005 -> IVSD-M005`, `IVSD-F006 -> IVSD-M006`, `IVSD-F007 -> IVSD-M007`
- Required plan mappings: each ID maps to a named Section 3 scenario and an owning task in plan Section 9
- Escalations required before: implementation, only if a security/tenant/privacy cohort would be deleted without a passing stronger replacement
- Refresh triggers: a decision to delete safety cohorts; a change to the BFF anonymous-shell contract; a change to authorization classification authority; any change to the disposition rule in `IVSD-M001`
