# I-VSD Workstream Report: Repository-Wide Terminology Precision Cleanup

Last Updated: 2026-09-29 Europe/Brussels

## Review Metadata
- Mode: implementation review
- Subject: Removal of an ambiguous modifier across codebase, documentation, and agent governance
- Workstream: terminology-precision
- Report kind: planning-workstream-report
- Report status: current
- Disposition: plan-aligned
- Evidence cutoff: 2026-09-13
- Reviewed input: terminology-precision workstream intake and codebase scan
- Supersedes: none

## Scope
Evaluation of the complete removal and replacement of the retired vague modifier across:
1. Agent governance files (`AGENTS.md`, `.agents/`, `.omo/`, `.codex/`).
2. Internal and public documentation (`docs/internal/`, `dev/_journal/`, `islamic-value-sensitive-design/`).
3. Domain models and enums (`ActorMerge`, `RegistrationProviderTrustLevelEnum`, `RegistrationAnswerSyncModeEnum`, `EventTicketType`, `LegalDocument`).
4. Persistence layer and EF Core migrations (`ActorIdentityConfiguration`, `RelationalSettingMutationLock`, `AtprotoJetstreamRepository`, database column `target_actor_id`).
5. Application layer and CQRS contracts (`ISettingMutationLock`, `IActorReferenceConsolidationRepository`, `LinkRelations`, `EventHeavyRedactedNotificationFanoutPayloadParser`).
6. API, Authentication, and Infrastructure (`AtprotoJwtOptions.TargetActorIdClaim`, `AtprotoSessionController`, `IdempotencyRequestIdentity`, `EnvironmentCatalogue`).
7. Blazor client and BFF services (`TenantPublicExperienceAdminService`, `AtprotoOAuthEndpointExtensions`).
8. Release engineering tooling (`ReleaseArtifactPolicy`, `ReleasePreparation`).

## Claim Boundary
This workstream is primarily a linguistic precision and non-behavioral architectural refactoring effort. Removing the repository-owned vague modifier does not degrade or alter cryptographic properties, data integrity guarantees, or tenant boundaries. It replaces vague AI/academic jargon with precise, domain-aligned terminology such as "primary", "authoritative", "target", "normalized", and "standard". The external .NET symbol `System.Formats.Cbor.CborConformanceMode.Canonical` remains unchanged because it is outside repository ownership.

## Findings

| Finding ID | Severity | Claim Type | Principle & Domain | Summary | Mitigation ID | Status |
|---|---|---|---|---|---|---|
| `IVSD-F001` | Medium | Technical Integrity | Truthfulness & Clarity (Amanah) | Overuse of the retired vague modifier creates false precision in documentation and code, obscuring whether an item is authoritative, normalized, default, or primary. | `IVSD-M001` | accepted |
| `IVSD-F002` | Low | Portability & Stability | Data Sovereignty | Renaming the actor-merge destination column to `target_actor_id` must not break actor consolidation history or identity merging invariants. | `IVSD-M002` | accepted |
| `IVSD-F003` | Low | Security | Authentication Integrity | Renaming JWT claims (`target_actor_id`) must be synchronized between token issuance and authentication middleware to prevent session invalidation. | `IVSD-M003` | accepted |

## Recommendations
1. Replace the retired modifier in agent and documentation contexts with exact terms:
   - Use "agent contract" without an inflated qualifier.
   - Use "source of truth", "primary entrypoint", "release artifact", or "selected provider" only where each phrase states the real contract.
2. In Domain and Persistence:
   - Model actor consolidation explicitly as source and target actors.
   - Use `settingKey` and `settingKeys` at mutation-lock boundaries.
   - Use `FullSync` and `FULL_SYNC` for complete provider synchronization.
3. In Release Engineering:
   - Use `ReleaseArtifactPolicy`, `ArtifactPolicyResult`, `NormalizeText`, and `NormalizeJson`.

## Stakeholders
- Platform Operators & Administrators (benefit from straightforward, unambiguous documentation and CLI instructions).
- Software Developers & AI Coding Agents (eliminate cognitive overhead and bloated prompt jargon).
- Platform End-Users & Event Organizers (preserve exact functional integrity and security guarantees).

## I-VSD Principles And Domains
- **Amanah (Trustworthiness & Truthfulness)**: Clear, honest, and precise language in software contracts and documentation reflects responsible stewardship. Vague buzzwords that add no meaning contradict direct, truthful communication.
- **Data Sovereignty & Preservation**: Actor merges and audit histories represent real people and relationships; schema refactoring must preserve relational integrity and avoid accidental data loss.

## Validation Gaps
- None. Full test suites (domain invariant tests, integration tests, and architecture guardrails) provide comprehensive verification of all renamed symbols.

## Escalation Needed
- None. Greenfield pre-release status allows breaking changes without external backward-compatibility compromises.

## Evidence Reviewed
- Codebase-wide ripgrep scan identifying 2,938 occurrences across 680+ files.
- `src/Explore.Domain/ActorMerge.cs`, `src/Explore.Persistence/Configurations/Entities/ActorIdentityConfiguration.cs`.
- `eng/release/src/ISLAMU.ReleaseEngineering/ReleaseArtifactPolicy.cs`.
- `AGENTS.md` and `.agents/contract/intents.yaml`.

## Missing Evidence
- None.

## Context Inventory
- Zero active production deployments; development workspace is clean and positioned on `develop`.

## Review Lifecycle
| Date | Previous status | New status | Trigger | Evidence/replacement |
|---|---|---|---|---|
| 2026-09-29 | none | current | User directive to remove the vague modifier repository-wide | Implemented workstream and verification evidence |

## Planning Handoff
- Workstream: `terminology-precision`
- Status: `current`
- Reviewed input: `terminology-precision-plan.md`
- Findings and mitigations: `IVSD-F001` -> `IVSD-M001`, `IVSD-F002` -> `IVSD-M002`, `IVSD-F003` -> `IVSD-M003`
- Required plan mappings:
  - `IVSD-M001`: Mapped to Phase 1, Phase 2, and Phase 5.
  - `IVSD-M002`: Mapped to Phase 3 and Phase 4.
  - `IVSD-M003`: Mapped to Phase 6.
- Escalations required before: None
- Refresh triggers: Alteration to database migration strategy or retention of deprecated compatibility shims.
