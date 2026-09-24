# Keycloak Operator Safety — Implementation Assessment

Last Updated: 2026-09-22

## Review Metadata

- Mode: implementation verification
- Subject: Safe, consent-based Keycloak onboarding and operator maintenance
- Workstream: keycloak-operator-safety
- Report kind: provider-responsibility design assessment
- Report status: verified
- Disposition: implemented / mitigation-aligned
- Evidence cutoff: 2026-09-22
- Reviewed input: plan/tasks revision `keycloak-operator-safety-r1`, approved intake decisions, implemented branch `feat/keycloak-operator-safety`, reviewed operation tests, deployment guardrails, and current documentation
- Supersedes: none; distinct from completed headless-instance-onboarding work

## Scope

The product's authority over a self-hoster's Keycloak infrastructure: connection
configuration, discovery/diagnosis, existing-client repair, explicit provisioning,
preview/apply, credential rotation, and onboarding recovery. Existing users and
other applications in the realm are affected stakeholders even when absent from
the setup screen. This assessment does not authorize changes to any live realm.

## Claim Boundary

This is Islamic value-sensitive provider-responsibility reasoning, not a fatwa,
product certification, or proof that operations cannot cause harm. Evidence is
repository implementation traceability and the previously recorded bounded tests;
stakeholder and live-realm validation are absent. The reported live 401 has not
been proven to be caused by a missing subject claim.

## Findings

| ID | Lifecycle / severity / claim | Principle, domain, stakeholder, and controlled decision | Evidence and validation | Mitigation / owner |
|---|---|---|---|---|
| IVSD-F001 | accepted / high / design risk | Amanah, rights of people, non-harm; technical/governance; realm operators and users; scope of remote writes | E1/E2/E8: bootstrap performs realm-default and shared-scope writes before no-op checks; bundled startup also sets realm policies and exact client redirects; implementation traceability | IVSD-M001: prohibit existing-realm global writes, bound approved client operations, remove startup mutation and explain operator prerequisites |
| IVSD-F002 | accepted / high / design risk | Amanah and non-harm; technical/operations; operators and dependent applications; ownership and rotation of client secrets | E1/E3: remote secret changes precede local configuration, which may ignore deployment-managed inputs; implementation traceability | IVSD-M002: preserve authoritative deployment bindings; separate repair from rotation; reject target and ownership conflicts before remote changes |
| IVSD-F003 | accepted / high / design risk | Rights of people and non-harm; technical; other realm applications; protection of operator-owned clients/mappers | E1/E4: audience-mapper name collisions can be overwritten; no distinct BFF/API client-ID check; implementation traceability | IVSD-M003: collision-safe, field-bounded changes; explicit client adoption; reject contradictory targets |
| IVSD-F004 | accepted / high / design risk | Sidq, promise-keeping, Amanah; design/operations; self-hosters; truthful completion and recovery | E1/E3/E5: multiple remote/local writes, no atomic cross-system rollback, secondary UI save after bootstrap; implementation traceability | IVSD-M004: preview/consent, read-back verification, bounded progress outcomes and safe forward recovery; never imply an error rolled back changes |
| IVSD-F005 | accepted / high / privacy/security risk | Avoiding spying, Amanah, non-harm; technical/operations; operators and account holders; privileged credentials, transport and diagnostics | E1/E6: request-scoped credentials and existing private/no-store protections; HTTP accepted by bootstrap URL validation | IVSD-M005: least-privilege request-scoped authority, verified transport, no credential retention/replay, value-free diagnostics |
| IVSD-F006 | accepted / medium / evidence limitation | Sidq and Ihsan; evaluation/design; novice operators and support staff; safety and ease-of-use claims | E7: tested subject repair does not establish whole-realm preservation, partial-failure recovery or usability | IVSD-M006: invariant-first preservation/failure tests, accessible plain-language operation summaries, explicit limits on assurance claims |

## Recommendations

Do not treat a broad provisioning routine as a harmless repair. Preserve valid
operator configuration; do nothing when requirements are already satisfied.
Separate diagnosis, connection, repair, creation and credential lifecycle actions.
Explain each proposed remote effect before authorization and report verified
outcomes after it. Do not delete/reimport realms or edit users as recovery.

Approved user decisions: never change realm-wide settings in an existing realm;
explain operator prerequisites. Replace duplicate shell repair with one foreground
C# Admin REST implementation. Administrator credentials come only from fresh
advanced-form input for each privileged submission, never deployment secrets or
retained inspection sessions. Runtime BFF client secrets remain a separate,
deployment-owned concern. These operations never create or modify users.

Rejected direction: silently configuring everything deemed useful for Event.
It burdens other realm applications and makes consent and recovery ambiguous.
Do not promise transactional rollback across Event and Keycloak.

## Stakeholders

- Self-hosters, including novices and operators of shared identity infrastructure.
- Existing realm users, whose stable IDs, credentials, MFA and memberships must remain untouched.
- Other applications and administrators sharing realm policies or client configuration.
- Event maintainers and support staff who must diagnose failure without access to secrets.

## I-VSD Principles And Domains

Applicable principles: trust/stewardship (Amanah), truthfulness (Sidq), non-harm,
rights of people, promise-keeping, excellence (Ihsan), avoiding unjustified data
collection, and avoiding misleading consent/defaults. Applicable domains:
strategic operator independence, design consent/accessibility, technical boundaries,
operational recovery, governance authority, and evaluation evidence.
Finance, content moderation and religious-content classifications are not involved.

## Validation Gaps

No production preservation audit or self-hoster usability study exists in the
reviewed evidence. The implemented domain, application, provider, HTTP, BFF,
Blazor, persistence and architecture fixtures establish bounded regression
evidence, not proof of production outcomes. Local coordination and
read-before/write/read-after still cannot provide atomic compare-and-swap
against concurrent external administrators; exact preconditions, captured
provider IDs and explicit conflicts mitigate rather than eliminate that limit.

## Escalation Needed

No material implementation blocker remains. Any expansion into global realm
administration or retained administrator
authority requires a new scope decision. No religious-legal determination is
needed for these engineering decisions; refer future such questions to qualified
scholarly authority rather than treating this as certification.

## Evidence Reviewed

- E1: `src/Explore.Infrastructure/Services/Keycloak/KeycloakBootstrapService.cs` — BootstrapAsync, EnsureRealmAsync, EnsureClientSecretAsync, EnsureAudienceMapper, EnsureSubjectMapper, doctor/preview/apply/rotation.
- E2: Same service — EnsureOfflineAccessRealmRoleAsync and EnsureOfflineAccessClientScopeMappingAsync; realm-wide side effects.
- E3: `src/Explore.Application/Features/InstanceOnboarding/Handlers/Commands/BootstrapKeycloakRealmCommandHandler.cs` and `src/Explore.Application/Services/AuthProviderConfigurationService.cs` — remote-first ordering and deployment ownership.
- E4: `src/Explore.Application/DTOs/Onboarding/Validators/KeycloakBootstrapRequestDtoValidator.cs` — target validation limits.
- E5: `src/Explore.Blazor.Client/Pages/Onboarding/AuthProviderConfiguration.razor` — BootstrapKeycloakAndApplyPublicOnboardingAsync, clearing and subsequent save.
- E6: `docs/internal/SECURITY-MODEL.md` — Provider credential HTTP response boundary; `docs/internal/CONFIGURATION.md` — Runtime Configuration Sources and Keycloak onboarding metadata.
- E7: `KeycloakOperationRepairTests`, `KeycloakProvisioningOperationTests`,
  `KeycloakOperationHttpTests`, `ProviderCredentialHttpBoundaryTests`,
  `KeycloakOperatorPanelTests`, and `BffSessionRefreshServiceTests`; bounded
  mutation, uncertainty, credential lifetime, and recovery evidence.
- E8: `KeycloakRealmOwnershipTests`, `docker-compose.yml`, and
  `src/Explore.AppHost/AppHost.cs`; initializer/script/import hooks and obsolete
  startup inputs are absent while provider volumes remain.

## Missing Evidence

Live token shape; the user's actual realm topology and custom mappings; operational
backup/restore evidence; stakeholder feedback; interrupted-operation and concurrent
external-admin preservation tests. None is silently assumed favorable.

## Context Inventory

Related headless-instance-onboarding work is marked implemented; its account
binding and first-admin authority invariants must be preserved. The new workstream
addresses external-provider operation safety, not a replacement identity provider
or a rewrite of Cerbos, global secret authority, or generic onboarding completion.

## Common Overlooked Failures And Outcomes

- No user deletion can still mean broken access after secret/configuration drift.
- A shared-realm default change can affect people who never used Event setup.
- A network timeout does not reveal whether a provider write committed.
- A healthy/no-op action must not write realm roles in the background.
- Previews, errors, caches and support bundles can leak privileged values.
- Fresh sign-in is necessary after token-issuance repair; retrying an old token is not verification.

## Review Lifecycle

| Date | Previous status | New status | Trigger | Evidence/replacement |
|---|---|---|---|---|
| 2026-09-22 | none | draft | Explicit implementation-plan request after whole-flow safety audit | E1–E7; material authority boundary awaiting decision |
| 2026-09-22 | draft | current / plan-aligned | User resolved realm authority, REST-only implementation and form-only administrator input; triad mapped | keycloak-operator-safety-r1; implementation not started |
| 2026-09-22 | current / plan-aligned | verified / mitigation-aligned | End-to-end implementation and phase-scoped independent review | ADR-033; reviewed receipts; explicit provisioning; startup mutation retired |

## Implementation Disposition

- Workstream: keycloak-operator-safety
- Status: verified / mitigation-aligned
- Reviewed input: implemented workstream, user decisions, E1–E8 and final test/review evidence
- Findings and mitigations: IVSD-F001–F006 map respectively to IVSD-M001–M006
- Implementation mappings: all six pairs below have code, test, documentation or explicit prohibition dispositions
- Escalations required before: any future expansion of provider authority, retained credentials or automatic reconciliation
- Refresh triggers: changes to operation scope, secret custody, consent, automation, recovery, telemetry, or affected stakeholders

| Finding / mitigation | Behavioral scenarios | Implementation tasks |
|---|---|---|
| IVSD-F001 / IVSD-M001 | S03–S05, S08, S18 | 1.1–1.3 safety policy; 3.2 narrow writes; 6.1 startup removal |
| IVSD-F002 / IVSD-M002 | S01, S10–S12 | 1.2 binding ownership; 4.1–4.3 provisioning/rotation separation; 5.1 transient form |
| IVSD-F003 / IVSD-M003 | S05–S09, S19 | 3.1–3.3 mapper preservation; 4.1 creation conflicts |
| IVSD-F004 / IVSD-M004 | S06, S09, S13–S15, S17 | 2.1–2.3 receipts/coordination; 3.3 reconciliation; 5.2 truthful recovery |
| IVSD-F005 / IVSD-M005 | S02, S10, S16–S17 | 1.3 transport; 3.4 HTTP boundary; 5.1/5.3 no retention |
| IVSD-F006 / IVSD-M006 | S15, S19–S20 | 5.2–5.3 accessible explanation; 6.2–6.3 preservation evidence/graduation |

The earlier F001 intake escalation is resolved by the user's explicit prohibition
on existing-realm global writes. No advanced override is implemented.

Official interface evidence: [Keycloak Admin REST](https://www.keycloak.org/docs-api/latest/rest-api/index.html),
[administration](https://www.keycloak.org/docs/latest/server_admin/), and
[import/export](https://www.keycloak.org/server/importExport), reviewed 2026-09-22
through source-free functional research. These interfaces do not promise a
multi-call transaction or conditional-write guarantee. Pinned-version behavior
is covered by the implemented contract fixtures; no provider implementation was copied.
