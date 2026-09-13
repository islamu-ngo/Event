# Instance Operator Onboarding — Provider Responsibility Review

Last Updated: 2026-09-13

## Review Metadata

- Mode: planning
- Subject: Collect instance operator identity during onboarding without preventing API startup.
- Workstream: instance-operator-onboarding
- Report kind: workstream design review
- Report status: current
- Disposition: plan-aligned
- Evidence cutoff: 2026-09-13
- Reviewed input: develop a9dff24b789d22448c9010a91a48028f7807d7f6 and the proposed instance-operator-onboarding workstream, revision 1.
- Supersedes: none; the existing branding/legal-identity governance remains authoritative for separate accountable roles.

## Scope

Interactive and configured-administrator setup, persisted instance identity, public disclosure, paid acceptance, and administrator repair. Provider decisions concern availability, truthful disclosure, authority, and operator effort. Payment recipient authority and jurisdiction-specific legal requirements are unchanged.

## Claim Boundary

This is repository-grounded software design reasoning. It is not a legal opinion, religious ruling, certification, or empirical UX study. Keeping existing field validation does not establish that those fields satisfy any jurisdiction's legal obligations.

## Findings

| ID | Lifecycle / severity / claim | Principle, stakeholder, provider decision | Evidence / validation | Mitigation / owner |
| --- | --- | --- | --- | --- |
| IVSD-F001 | Accepted / high / availability design | Avoid unnecessary burden on self-hosters; let operators reach setup before requiring public identity | E1, E2; source inspection, no runtime test | IVSD-M001; onboarding implementer |
| IVSD-F002 | Accepted / high / truthful accountability | Trust and accurate attribution for visitors and purchasers; incomplete identity must not become fabricated disclosures | E3, E4; source inspection | IVSD-M002; application implementer |
| IVSD-F003 | Accepted / critical / authority protection | Protect entrusted instance administration; missing identity must never grant first-admin privileges again | E2, E5; source inspection | IVSD-M003; authorization implementer |
| IVSD-F004 | Accepted / high / data integrity and privacy | Preserve purchaser agreements and minimize exposure of administrator drafts and operator details in telemetry | E1, E4; source inspection | IVSD-M004; persistence and commerce implementer |

## Recommendations

- IVSD-M001: Run the API with a visible incomplete-setup state; persist operator-entered drafts; require valid identity at onboarding completion. Keep unattended input optional and validate it through the same path.
- IVSD-M002: Return explicit unavailable results at identity-dependent public and commerce boundaries. Keep instance, tenant, organizer, and cosmetic branding separate. A user may explicitly copy entered details into a separate tenant form, with review; there is no automatic legal-identity fallback.
- IVSD-M003: Reuse setup-secret authority only while bootstrap remains pending; after completion, require existing instance-admin authority. Keep official-instance assertions deployment-controlled. Reject cross-tenant overrides and stale writes.
- IVSD-M004: Commit identity and completion consistently; retain immutable acceptance snapshots. Use bounded reason codes in public errors and telemetry; never log submitted names, emails, registration numbers, or URLs. Persist one server-generated stable operator identifier.

Rejected alternatives: deleting validation alone defers exceptions to service resolution; placeholder identity can falsely attribute responsibility; keeping required environment variables leaves the reported setup barrier; resetting completed bootstrap creates an administrator takeover path.

## Stakeholders

Self-hosting operators, existing instance administrators, tenant operators, event organizers, visitors, purchasers, and platform maintainers. No interviews or operational measurements were collected.

## I-VSD Principles And Domains

Trustworthiness in handling entrusted authority, truthful attribution, avoidance of preventable harm, and reducing unnecessary operator burden are the applied design lenses. Domains: UX, self-hosting operations, data handling, authorization, and commerce disclosure. Religious-content moderation, ranking, and monetization strategy are outside this change.

## Validation Gaps

Startup without identity, draft resumption, concurrent completion, repair authority, multi-provider transaction behavior, and paid-acceptance freshness need implementation tests. Source inspection alone establishes none of those future behaviors.

## Escalation Needed

No new legal or scholarly conclusion is required to change the timing of existing validation. Changing required legal fields, official-instance authority, or payment obligations would require a separate scoped decision and appropriate qualified review.

## Evidence Reviewed

- E1: `src/Explore.Application/ApplicationServicesRegistration.cs` and `Contracts/Services/InstanceOperatorIdentityOptions.cs`: startup validator and throwing singleton factory.
- E2: `src/Explore.API/Controllers/InstanceOnboardingController.cs`, `src/Explore.Application/Features/InstanceOnboarding/Services/InstanceOnboardingCompletionOperation.cs`, and `src/Explore.Blazor.Client/Pages/Onboarding/InstanceOnboarding.razor`: existing protected setup and shared completion transaction.
- E3: `src/Explore.Application/Features/LegalDocuments/Handlers/Queries/GetPublicLegalDocumentQueryHandler.cs` and `Features/PublicExperience/Handlers/Queries/GetPublicExperienceSettingsQueryHandler.cs`: public identity consumers.
- E4: `src/Explore.Application/Services/Registration/PaidOrderAcceptanceService.cs` and `PaidOrderAcceptanceFreshnessService.cs`: disclosure and acceptance freshness.
- E5: `src/Explore.Infrastructure/Services/SetupSecretProvider.cs`, `src/Explore.Domain/InstanceBootstrapState.cs`, and `src/Explore.API/Controllers/InstanceSettingsControllerBase.cs`: pending setup and completed-admin authority.
- E6: `dev/active/instance-operator-onboarding/instance-operator-onboarding-plan.md`, `instance-operator-onboarding-tasks.md`, and `instance-operator-onboarding-context.md`, revision 1: proposed scenarios, architecture, tasks and explicit evidence limits. Plan Section 9 maps every finding/mitigation to owned work.

## Missing Evidence

No runtime reproduction, usability study, legal review, or production deployment evidence. The local `.env` is empty, but effective configuration can have other authorities; that file alone does not prove the complete configuration-provider failure chain.

## Context Inventory

One shared source investigation; planning artifacts live in `dev/active/instance-operator-onboarding/`. Existing unrelated secret-configuration edits are outside the workstream. Optional unattended setup is retained as a proposed default, not recorded as a user-approved decision.

## Common Overlooked Failures And Outcomes

Throwing during dependency injection despite removing startup validation; setup blocked by its own preflight; replicas serving stale identity; first-admin setup reopening after record loss; pending payment attempts accepting changed identity; readiness probes making the wizard unreachable; completed installations unable to repair without environment edits.

## Review Lifecycle

| Date | Previous status | New status | Trigger | Evidence |
| --- | --- | --- | --- | --- |
| 2026-09-13 | None | Draft | Shared source investigation completed | E1–E5 |
| 2026-09-13 | Draft | Current / plan-aligned | Revalidated availability, authority, public disclosure and immutable-history mitigations against the completed proposal | E6; plan Section 9 |

## Planning Handoff

- Workstream: instance-operator-onboarding, revision 1.
- Status: current / plan-aligned for the proposed design; implementation and user approval remain outstanding.
- Findings and mitigations: IVSD-F001–F004 map respectively to IVSD-M001–M004.
- Required mappings: F001/M001 → S1–S3/S9/S10 and tasks 1.1–1.3/2.1–2.3/3.1; F002/M002 → S4/S7/S10 and tasks 2.2/3.1; F003/M003 → S5–S7 and tasks 1.1–1.3/2.1/3.2; F004/M004 → S6/S8/S10 and tasks 1.2/2.2/3.2.
- Escalations required before: implementation if scope changes required identity fields, official-instance authority, or payment obligations.
- Refresh triggers: unattended-setup removal, changed setup permissions, identity fallback, new public exposure, altered acceptance persistence, or different recovery behavior.
