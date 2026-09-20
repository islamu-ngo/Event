# Progressive Instance Onboarding - I-VSD Planning Review

Last Updated: 2026-09-20 Europe/Brussels

## Review Metadata

- Mode: planning
- Subject: Installation reliability, progressive operator disclosure, and commerce readiness
- Workstream: progressive-instance-onboarding
- Report kind: Provider-responsibility design review
- Report status: current
- Disposition: plan-aligned
- Evidence cutoff: 2026-09-20
- Reviewed input: progressive-instance-onboarding triad revision `R1`, grounded in develop `7f78e938f`
- Supersedes: none

## Scope

Review the provider-controlled installation burden, disclosure defaults, setup
authority, recovery experience, and separation of public and paid capabilities.
The provider-credential-onboarding workstream separately owns credential custody.
This workstream must not retain administrative credentials or widen setup-secret
authority to make a screen work.

## Claim Boundary

This is design reasoning and implementation traceability, not a religious ruling,
legal certification, or proof of runtime correctness. The user's screenshot and
telemetry establish reported failures, not the root cause of lost interactivity.
No claim is made that non-payment deployments have no disclosure obligations.

## Findings

| ID | Lifecycle / severity / claim | Principle, stakeholder, and controlled decision | Evidence / level | Mitigation / owner |
|---|---|---|---|---|
| IVSD-F001 | Accepted / high / design risk | Data minimization and avoiding unnecessary collection; individual self-hosters; requiring commercial identity before installation | E1, E2 / implementation traceability | IVSD-M001; onboarding and domain owners |
| IVSD-F002 | Accepted / high / design risk | Amanah and non-harm; visitors and buyers; weakening disclosure or paid readiness while removing installation friction | E2, E3 / implementation traceability | IVSD-M002; publication and commerce owners |
| IVSD-F003 | Accepted / high / observed defect plus unresolved cause | Amanah, truthfulness, and excellence; operators; hidden authorization failures and non-interactive loading screens | E4, E5 / reported operational evidence plus source trace | IVSD-M003; BFF and UI owners |
| IVSD-F004 | Accepted / medium / design defect | Justice, accessibility, and avoiding uncertainty; novice, mobile, keyboard, and assistive-technology users; machine-code form fields and unexplained duplicate roles | E6 / implementation traceability and user report | IVSD-M004; UI owner |
| IVSD-F005 | Accepted / high / security invariant | Amanah and rights of people; all tenants; confusing setup authority with authenticated identity or reopening setup for repair | E7 / implementation traceability | IVSD-M005; security owner |
| IVSD-F006 | Accepted / high / privacy invariant | Rights of people and avoiding spying; individual operators; publishing drafts or exporting legal details through diagnostics | E2, E3 / implementation traceability | IVSD-M006; API and operations owners |

## Recommendations

- IVSD-M001: Separate installation completion from contextual identity readiness.
  Keep draft information editable after installation; do not manufacture legal
  facts from branding, login email, provider account, or another operator.
- IVSD-M002: Require server-authoritative readiness at public activation and paid
  publication/checkout, preserving transaction fencing and immutable acceptance.
  Explain the blocker and route an authorized administrator to its repair screen.
- IVSD-M003: Show distinct loading, authority-expired, unavailable, and connection
  states. Reproduce the failure before assigning its cause. Provide recovery that
  works when component event handling does not.
- IVSD-M004: Use human-labeled operator choices, country selection, clear optional
  registration information, keyboard/touch-accessible help, and explicit role copy.
- IVSD-M005: Preserve permanent setup lockout, exact-tenant server authorization,
  sanitized BFF forwarding, and HAL action authority. Repair never grants authority.
- IVSD-M006: Separate private draft DTOs from public disclosures; emit only bounded
  reason codes and correlation identifiers in telemetry, not legal/contact values.

Rejected alternatives: making frontend fields optional while leaving backend gates
unchanged; publishing drafts automatically; disabling authorization on failing
reads; silently replacing configured Cerbos with local permissions; merging
instance, directory, and merchant identities into one implicit authority.

## Stakeholders

Individual and organizational self-hosters; initial and tenant administrators;
visitors; event organizers and buyers; support operators; keyboard, touch,
screen-reader, and RTL users; maintainers of related onboarding workstreams.

## I-VSD Principles And Domains

Trust/Amanah governs setup authority and recovery. Truthfulness/Sidq requires
readiness statuses to reflect evidence rather than absence of an error.
Justice/Adl and excellence/Ihsan require comprehensible and accessible setup.
Rights of people, non-harm, and avoiding spying constrain data collection and
disclosure. Strategic and design review favor a useful minimal installation;
technical and operational review preserve enforceable feature boundaries;
governance and evaluation require explicit activation and verifiable outcomes.

## Validation Gaps

The current failure has no captured browser console/circuit evidence. No new
usability study, accessibility audit, deployment trial, or legal review occurred.
No implementation or tests were performed for this report.

## Escalation Needed

The user instructed the planner to continue without selecting an intake option.
The plan adopts the recommended administration-first default for review; it does
not claim an explicit selection or implementation approval.
Jurisdiction-specific disclosure or finance rulings require qualified authorities
before claiming compliance; this work does not add jurisdictional rules.

## Evidence Reviewed

- E1: `src/Explore.Application/Features/InstanceOnboarding/Services/InstanceOnboardingCompletionOperation.cs:205`
  unconditionally requires complete instance identity.
- E2: `src/Explore.Domain/ValueObjects/InstanceOperatorIdentityReadiness.cs:56`
  evaluates PaidCommerce; `TenantDirectoryOperatorIdentity.cs` owns fixed operator
  kinds, draft validation, and capability requirements.
- E3: `src/Explore.Application/Features/PublicExperience/Handlers/Queries/GetPublicExperienceSettingsQueryHandler.cs:77`;
  `src/Explore.Application/Features/EventTicketing/Services/PaidEventPublicationPreflightService.cs:48`;
  `docs/internal/PAYMENTS.md`, structured legal identity and paid acceptance.
- E4: `src/Explore.Blazor/Services/SetupSecretForwardingHandler.cs:38`;
  `src/Explore.API/Controllers/InstancePresentationSettingsController.cs:85`.
- E5: User screenshot and redacted-purpose logs for UI-shell identity failure,
  branding 403, and public-shell 503; read-only Aspire exception search;
  `src/Explore.Blazor.Client/Pages/Onboarding/AuthorizationProviderConfiguration.razor:469`.
- E6: `src/Explore.Blazor.Client/Components/Onboarding/InstanceOperatorIdentityEditor.razor`;
  `src/Explore.Blazor.Client/Pages/Onboarding/InstanceOnboarding.razor:205`.
- E7: `docs/internal/adr/ADR-instance-operator-onboarding-readiness.md`,
  permanent setup-secret lockout; `docs/internal/SECURITY-MODEL.md`,
  header and secret hardening.
- E8: [R1 plan](../../dev/active/progressive-instance-onboarding/progressive-instance-onboarding-plan.md),
  [R1 tasks](../../dev/active/progressive-instance-onboarding/progressive-instance-onboarding-tasks.md),
  and [context](../../dev/active/progressive-instance-onboarding/progressive-instance-onboarding-context.md).
  Independent bounded review identified and the planner corrected the Phase 5
  UI dependency and whole-file commit ownership boundary.

## Missing Evidence

Authenticated-browser reproduction of the lost interactivity; implementation
verification; field usability feedback. The R1 scenario/task mapping is complete,
but those scenarios are planned, not executed.

## Context Inventory

Shared evidence was collected once in this conversation. Existing workstreams
checked: provider-credential-onboarding, headless-instance-onboarding,
setup-assistant-security-and-portability, the superseded presentation-target
candidate, and the older paused tenant-onboarding-enterprise plan. None owns the
complete new progressive-instance-onboarding scope.

## Common Overlooked Failures And Outcomes

Removing a form requirement without changing completion, tenant lifecycle, legal
rendering, and admin repair leaves the installation blocked elsewhere. A default
public mode is not consent to publish draft identity. A caught HTTP failure does
not prove a circuit crash. A ready-to-publish indicator must not grant authority
or replace the command's fresh readiness check.

## Review Lifecycle

| Date | Previous status | New status | Trigger | Evidence/replacement |
|---|---|---|---|---|
| 2026-09-20 | None | Draft | Planning request following source-grounded investigation | E1-E7; publication intake pending |
| 2026-09-20 | Draft | Current / plan-aligned | User requested continuation; recommended default adopted; completed R1 mapping revalidated | E8; all six finding/mitigation pairs assigned |

## Planning Handoff

- Workstream: progressive-instance-onboarding
- Status: current
- Disposition: plan-aligned
- Reviewed input: R1 triad and develop `7f78e938f`
- Findings and mitigations: IVSD-F001 -> IVSD-M001 through IVSD-F006 -> IVSD-M006
- Required plan mappings:
  - IVSD-F001 / IVSD-M001 -> S08/S11, Tasks 3.1-3.3 and 5.1-5.3.
  - IVSD-F002 / IVSD-M002 -> S12-S16/S18-S19, Tasks 3.1-3.3 and 4.1-4.3.
  - IVSD-F003 / IVSD-M003 -> S01-S07, Tasks 1.1-1.3 and 2.1-2.3.
  - IVSD-F004 / IVSD-M004 -> S20-S23, Tasks 6.1-6.3.
  - IVSD-F005 / IVSD-M005 -> S02/S09-S10/S14-S16, Tasks 1.1-1.3, 4.1-4.3, 5.1-5.3.
  - IVSD-F006 / IVSD-M006 -> S11-S13/S15/S22, Tasks 3.1-3.3, 4.1-4.3, 6.1-6.3.
- Escalations required before: User approval before implementation; qualified
  review before any jurisdiction-specific or religious-legal compliance claim.
- Refresh triggers: Publication default, required identity fields, authority
  boundaries, data exposure, payment gates, or accessibility behavior changes.
