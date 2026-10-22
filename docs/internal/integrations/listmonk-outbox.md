# Listmonk outbox recovery evidence

> **Audience:** Backend contributors and operators investigating delivery uncertainty
> **Source anchors:** `IntegrationSyncOutbox`, `IntegrationSyncOutboxRepository`, `IntegrationSyncDrainService`

## Application-owned decisions

The Application workflow authorizes an operator's recovery decision; Persistence applies it through EF Core. The database stores the outcome, tenant relationship and claim identity. It does not interpret provider messages or decide whether another delivery is safe.

`IntegrationSyncOutbox.EvidenceKind` is a closed, non-private integer enum, separate from the private `LastError` diagnostic. A provider can return text resembling an internal status name. That text must never authorize a recovery action or count as an ambiguous provider outcome.

| Value | Evidence | Compatible status |
|---|---|---|
| 0 | None | Any defined ordinary status |
| 1 | Provider handoff in doubt | Processing |
| 2 | Provider outcome ambiguous | DeadLettered |
| 3 | Operator confirmed accepted | Completed |
| 4 | Operator established definitely not accepted; retry requested | RetryScheduled |
| 5 | Operator retained dead letter | DeadLettered |

`RequireValidEvidence` rejects unknown values and incompatible evidence/status pairs on creation and tracked saves. The EF model also bounds the persisted integer to 0–5. Repository bulk transitions use fixed enum values and change status and evidence in the same conditional update; they do not depend on tracked-save validation.

## Claims, uncertainty and recovery

The worker claims a row with its tenant, ID, fresh lease token and processing timestamp. Every subsequent worker transition must match that complete active claim. Before contacting Listmonk, it records `ProviderHandoffInDoubt`. A stale handoff is parked as `ProviderOutcomeAmbiguous`, not automatically delivered again: a missing response does not establish that Listmonk rejected the request.

Ordinary failure stores bounded diagnostic text and `None` evidence. Even a diagnostic literally equal to `provider_outcome_ambiguous` remains an ordinary failure. Explicit parking clears the diagnostic and records typed ambiguity. An authorized recovery request conditionally replaces only a matching tenant's still-ambiguous dead letter, recording the actor, evidence reference and decision time. Repeated or competing decisions cannot recover an already-resolved row. The next ordinary claim resets the previous recovery evidence.

Queue-health ambiguity classification uses the typed field. The PostgreSQL health reader's fixed source inventory replaces `LastError` with `EvidenceKind`; it does not gain access to subscriber details or diagnostic text. This change does not retire the remaining PostgreSQL fleet-health routine or PDS workflows.

## Development deployment and rollback

The column and check constraint are generated through EF migrations for each application provider. This is a pre-release schema boundary: reprovision development databases containing old pending, ambiguous or operator-resolved Listmonk work. Do not infer new evidence from old error strings, assume a generated zero value reconstructs historical decisions, or automatically replay uncertain work.

Stop dispatch before changing schema or restoring an earlier build. A schema downgrade removes typed evidence and cannot preserve its recovery meaning; restore a matching application/database backup instead of downgrading an active queue. Inspect external provider outcomes before deliberately re-enqueuing any uncertain work. No migration or deployment action is performed by this documentation.

Direct database writes can bypass C# workflow rules. Fixed permissions, tenant isolation and constraints remain supporting protections, not a second business-logic authority.

## Verification anchors

`IntegrationSyncEvidenceTests` covers valid state pairs, invalid values and diagnostic-text separation. `IntegrationSyncOutboxRepositoryTests.ProviderDiagnosticCannotAuthorizeAmbiguityRecovery` exercises actual claim/failure/parking/recovery behavior, including foreign-tenant and stale-claim rejection. Native queue-health tests verify the typed column is readable and private diagnostic text remains denied. Passing source or Domain checks alone does not establish native privilege or full-provider migration coverage.
