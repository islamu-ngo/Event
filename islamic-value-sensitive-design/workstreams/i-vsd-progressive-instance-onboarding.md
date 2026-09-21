# Progressive Instance Onboarding - I-VSD Implementation Closure Review

Last Updated: 2026-09-21 Europe/Brussels

## Review Metadata

- Mode: implementation closure
- Subject: Installation reliability, progressive operator disclosure, and commerce readiness
- Workstream: progressive-instance-onboarding
- Report kind: Provider-responsibility implementation closure review
- Report status: implemented; final audit current
- Disposition: implementation-aligned with recorded validation gaps
- Evidence cutoff: 2026-09-21
- Reviewed input: progressive-instance-onboarding triad revision `R2`, grounded in `origin/develop` `7f78e938f`; final implementation `f5ccd58f468f374ff259a8d5c557c3c1cfeb62b1`
- Supersedes: R1 review and the R2 planning review at this same canonical report path

## Scope

Review the provider-controlled installation burden, disclosure defaults, setup
authority, recovery experience, post-install guidance, and separation of private
administration, public disclosure, activation, and paid commerce. The
provider-credential-onboarding workstream separately owns credential custody.
This workstream must not retain administrative credentials or widen setup-secret
authority to make a screen work.

## Claim Boundary

This is design reasoning and implementation traceability, not a religious ruling,
legal certification, or a claim of universal runtime assurance. Historical browser
failures were reproduced and repaired with the bounded Local and Split evidence
recorded below; residual assurance gaps remain explicit. No claim is made that
non-payment deployments have no disclosure obligations.

## Findings

| ID | Lifecycle / severity / claim | Principle, stakeholder, and controlled decision | Evidence / level | Mitigation / owner |
|---|---|---|---|---|
| IVSD-F001 | Accepted / high / design risk | Data minimization and avoiding unnecessary collection; individual self-hosters; requiring commercial identity before private administration | E1, E2, E9 / implementation traceability | IVSD-M001; onboarding and domain owners |
| IVSD-F002 | Accepted / high / design risk | Amanah and non-harm; visitors and buyers; weakening disclosure, activation, or paid readiness while removing installation friction | E2, E3, E9 / implementation traceability | IVSD-M002; publication and commerce owners |
| IVSD-F003 | Accepted / high / observed defect plus unresolved cause | Amanah, truthfulness, and excellence; operators; hidden provider failures, mixed-time snapshots, and non-interactive loading screens | E4, E5, E9 / reported operational evidence plus source trace | IVSD-M003; BFF and UI owners |
| IVSD-F004 | Accepted / medium / design defect | Justice, accessibility, and avoiding uncertainty; novice, mobile, keyboard, touch, screen-reader, and RTL users; machine-code fields and unclear actions | E6, E9 / implementation traceability and user report | IVSD-M004; UI owner |
| IVSD-F005 | Accepted / high / security invariant | Amanah and rights of people; all tenants; confusing setup authority, authenticated identity, readiness, and administration authority | E7, E9 / implementation traceability | IVSD-M005; security owner |
| IVSD-F006 | Accepted / high / privacy invariant | Rights of people and avoiding spying; operators and represented organizations; exposing private drafts or legal details through public paths, journey recovery, or diagnostics | E2, E3, E9 / implementation traceability | IVSD-M006; API and operations owners |

## Recommendations

- IVSD-M001: Separate private installation completion from capability-specific
  identity readiness. Keep valid drafts editable after installation and never
  manufacture legal facts from branding, login email, provider identity, or
  another operator.
- IVSD-M002: Require fresh server-authoritative readiness for public disclosure,
  explicit activation, and paid publication/checkout. Preserve transaction
  fencing, immutable acceptance, and narrow authorized repair paths.
- IVSD-M003: Replace independently timed setup reads with one bounded journey
  snapshot. Show explicit ready, action-required, restart-required, unavailable,
  failed, stale, uncertain-completion, and completed states. Reproduce the
  browser failure before assigning or fixing its cause.
- IVSD-M004: Use human-labeled canonical choices, visible essential guidance,
  one primary action, categorized post-install work, keyboard/touch-accessible
  help, and verified responsive, zoom, contrast, screen-reader, and RTL behavior.
- IVSD-M005: Preserve permanent setup lockout, exact-tenant server authorization,
  sanitized purpose-bound BFF forwarding, and HAL action authority. Durable
  status readback may explain recovery but never grants or recreates authority.
- IVSD-M006: Keep Provisioning tenants and private draft DTOs out of public
  selectors, caches, feeds, media, signup, visitor allocation, and telemetry.
  Emit only bounded reason codes and correlation identifiers.

Rejected alternatives: making frontend fields optional while leaving backend
gates unchanged; publishing drafts automatically; adding Active-plus-unpublished
state; disabling authorization on failing reads; silently replacing Cerbos with
Local authorization; replaying credentials after uncertain completion; and
merging instance, directory, and merchant identity into one authority.

## Stakeholders

Individual and organizational self-hosters; initial and tenant administrators;
visitors; event organizers and buyers; support operators; keyboard, touch,
screen-reader, high-contrast, zoom, and RTL users; maintainers of related
onboarding and provider workstreams.

## I-VSD Principles And Domains

Trust/Amanah governs setup authority, irreversible completion, and recovery.
Truthfulness/Sidq requires provider and readiness states to reflect current
evidence rather than the absence of an error. Justice/Adl and excellence/Ihsan
require comprehensible, accessible setup and administration. Rights of people,
non-harm, and avoiding spying constrain collection, public exposure, and
diagnostics. Strategic and design review favor a useful minimal installation;
technical and operational review preserve enforceable capability boundaries;
governance and evaluation require explicit activation and observable evidence.

## Validation Gaps

Fresh real-browser evidence now covers Local lost-response recovery and an explicit
Keycloak Split callback through durable administrator readback; the former is in
`.omo/evidence/20260921-mad-required-repairs/f4-summary.md` and the latter in
`.omo/evidence/20260921-external-circuit-identity/RESULT.md`. Those desktop
journeys do not constitute a fresh usability study, accessibility/mobile/RTL audit,
or legal review.

Whole-project assurance is intentionally not represented as globally green. The
API attempt is incomplete (320 failures and one skipped record without a final
pass/total/exit), and the Persistence attempt observed one failure and five skips
while its wrapper supplied no final summary; newer Application/Persistence/Standalone
failures lack paired baseline attribution. See
`.omo/evidence/20260921-ring3-repairs/summary.md`. The provider exit evidence proves
live PostgreSQL activation plus five-provider model/composition and pending-model
checks, not five live activation backends or independent capacity-race/post-commit-
invalidation ordering. See
`.omo/evidence/20260920-progressive-instance-onboarding/provider-matrix-final/summary.md`.

## Escalation Needed

No unresolved provider-responsibility decision blocks implementation of R2. The
user explicitly approved full implementation on 2026-09-20. Jurisdiction-specific
disclosure, finance, halal/haram, or compliance conclusions remain outside this
workstream and require qualified authority before any such claim is released.

## Evidence Reviewed

- E1: R1 baseline evidence recorded that
  `src/Explore.Application/Features/InstanceOnboarding/Services/InstanceOnboardingCompletionOperation.cs`
  required complete instance identity during completion; the implemented R2 outcome removed that coupling.
- E2: `src/Explore.Domain/ValueObjects/InstanceOperatorIdentityReadiness.cs` and
  tenant identity values own draft and capability requirements.
- E3: Public experience and paid event preflight consumers demonstrate distinct
  disclosure and commerce boundaries.
- E4: `src/Explore.Blazor/Services/SetupSecretForwardingHandler.cs` and
  `InstancePresentationSettingsController.cs` define the purpose-bound setup path.
- E5: The R2 reported browser failure established the reproduction obligation;
  current Split callback and Local response-loss evidence records the bounded repairs and outcomes.
- E6: Current identity editors and onboarding page expose code-shaped choices and
  broad pre-completion demands.
- E7: The onboarding readiness ADR and security model establish permanent
  setup-secret lockout and trusted-header boundaries.
- E8: The R1 plan, task checklist, and context established the original six
  finding/mitigation mappings and inherited source evidence.
- E9: The R2 plan, task checklist, and context define scenarios S01-S26, the
  private Provisioning lifecycle, one journey snapshot, durable handoff,
  categorized getting-started guidance, and mandatory real-surface verification.

## Missing Evidence

No fresh usability study, accessibility tree/mobile/RTL audit, fresh-install timing
measurement, legal review, zero-sensitive-telemetry inspection, or five-live-provider
activation matrix was performed. Full API/Persistence project assurance remains
incomplete and attribution-limited as stated in Validation Gaps. These do not
invalidate the completed bounded implementation evidence or its R2 mappings.

## Context Inventory

The R2 triad and R1 I-VSD report were reviewed together. Existing adjacent
workstreams remain separate: provider-credential-onboarding owns credential
custody; no new Local offline break-glass authority, payment protocol redesign,
deployment topology, dependency, or workflow engine enters this workstream.

## Common Overlooked Failures And Outcomes

Removing a form requirement without changing completion, lifecycle enforcement,
legal rendering, and private repair leaves installation blocked or unsafe. A
known Provisioning tenant ID must not bypass public denial. A readiness result is
evidence, not transition authority. A lost completion response must converge by
status without credential replay. Optional workers disabled in the bounded
Standalone profile must not appear operational. A caught HTTP failure does not
prove or repair a Blazor circuit defect.

## Review Lifecycle

| Date | Previous status | New status | Trigger | Evidence/replacement |
|---|---|---|---|---|
| 2026-09-20 | None | Draft | Initial R1 planning review | E1-E7 |
| 2026-09-20 | Draft | Current / plan-aligned | Completed R1 mapping | Original R1 plan/tasks |
| 2026-09-20 | Current | Stale | Senior CTO material rewrite to R2 | Revised private-completion journey and task mapping |
| 2026-09-20 | Stale | Current / plan-aligned | Full R2 revalidation | E9; mappings below |
| 2026-09-21 | Current / plan-aligned | Implemented / implementation-aligned with recorded gaps | Final implementation closure audit at `f5ccd58f4` | Provider, browser, MAD, and full-project attribution evidence referenced above |

## Implementation Closure Handoff

- Workstream: progressive-instance-onboarding
- Status: implemented; final audit current
- Disposition: implementation-aligned with recorded validation gaps
- Reviewed input: R2 triad grounded in `origin/develop` `7f78e938f`; final implementation `f5ccd58f4`
- Findings and mitigations: IVSD-F001 -> IVSD-M001 through IVSD-F006 -> IVSD-M006
- Required plan mappings:
  - IVSD-F001 / IVSD-M001 -> S02, S09, S20; Tasks 1.1-1.2, 2.1-2.3, 3.1-3.3.
  - IVSD-F002 / IVSD-M002 -> S10, S18-S22; Tasks 1.1-1.3 and 3.1-3.3.
  - IVSD-F003 / IVSD-M003 -> S03, S05-S08, S16; Tasks 2.1-2.2.
  - IVSD-F004 / IVSD-M004 -> S18-S19, S23-S26; Tasks 3.1-3.3.
  - IVSD-F005 / IVSD-M005 -> S07, S10-S17; Tasks 1.1-1.3 and 2.1-2.3.
  - IVSD-F006 / IVSD-M006 -> S05-S06, S10, S14-S17, S19-S20; Tasks 1.1-1.3, 2.1-2.3, and 3.1-3.3.
- Escalations required before: Qualified review before any jurisdiction-specific,
  religious-legal, or compliance claim; none before R2 implementation.
- Refresh triggers: Publication default, required identity fields, authority
  boundaries, data exposure, payment gates, recovery authority, or accessibility
  behavior changes.
