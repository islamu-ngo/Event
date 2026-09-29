<!-- ABOUTME: Architectural decision record for registration data collection ownership and provider channels. -->
<!-- ABOUTME: Defines standard form, answer, capability, callback, and finalization boundaries. -->

# ADR-016: Registration Data Collection Context And Provider Channels

| | |
|---|---|
| **Status** | Accepted |
| **Date** | 2026-07-26 |
| **Deciders** | ISLAMU Event Platform — Architecture, Security, Registration workstreams |
| **Supersedes** | Registration questionnaires modeled as event custom properties or one external URL |
| **Superseded by** | — |

## Context

Event custom properties describe an event. Registration answers describe a purchaser, participant, ticket assignment, or session-selection relationship with an event. Those subjects have different ownership, privacy, consent, retention, validation, and lifecycle rules. Reusing event custom-property rows would hide workflow-critical registration state in a generic extension mechanism.

Native ISLAMU forms, Formbricks, Google Forms, and Microsoft Forms also expose materially different schema, presentation, collection, completion, and trust capabilities. A provider name cannot safely determine whether ISLAMU can render, submit, verify, fetch, synchronize, reconcile, or automatically finalize a response.

The governing invariant is:

> ISLAMU owns the registration workflow and normalized registration record. A form provider supplies a versioned collection channel and evidence of completion. Provider evidence never bypasses ISLAMU validation, capacity, approval, deduplication, consent, or finalization authority.

## Decision

### Bounded-context ownership

1. A dedicated Registration Data Collection bounded context owns `Registration*` workflows, immutable form versions, attempts, submissions, normalized answers, issues, requirement fulfillment, and finalization effects.
2. The bounded context may share domain validation value objects and mirror the custom-property subsystem's typed-column, `Ordinal`, `Namespace + Key`, constraint, and governance vocabulary. It does not reuse custom-property entities, tables, or projections.
3. Authoritative answers use one row per atomic typed value. Exactly one value column is populated, the populated column agrees with the field type, and multivalue answers use separate rows ordered by `Ordinal`.
4. Sensitive values are stored separately as key-versioned ciphertext. Optional blind indexes require an explicit governed use case. Raw provider payloads may exist only in the bounded, short-retention incoming-message evidence store; they are never authoritative answers.
5. Relational form rows remain authoritative. Publishing a form version deterministically generates immutable, content-hashed JSON Schema 2020-12 data, UI, logic, and provider-mapping artifacts.

### Workflow and channel model

1. A `RegistrationWorkflow` belongs to an event and purpose. Mandatory workflow requirements use `ALL`; alternative channels inside one requirement use `ANY`; optional requirements never block finalization.
2. A `RegistrationChannel` binds one requirement to one provider binding. It records independent schema authority, presentation mode, collection mode, completion mode, trust level, and answer synchronization mode. There is no single provider enum and no composite-provider class.
3. Provider adapters implement only the capability-specific Application contracts they support: descriptor, presentation, schema reading, provisioning, response writing or reading, callback verification, subscription management, reconciliation, or submission sink.
4. Effective capability is the intersection of a proven profile, connection configuration, tenant governance, mapping compatibility, and authorization. Profiles bind to the exact provider code, deployment kind, API version, adapter-policy version, and conformance-evidence revision. Unknown tuples fail closed for automatic finalization.
5. `IRegistrationSubmissionSink` remains separate from collection capabilities so approved authoritative fields can be mirrored after commit without making the destination a registration authority.

### Evidence, synchronization, and finalization

1. Trust levels are explicit lookup data: `FirstParty`, `SignedProvider`, `AuthenticatedProviderFetch`, `DelegatedAutomation`, `UserReturnOnly`, and `ManualImport`. Event or tenant policy defines the minimum automatic-finalization trust level; lower-trust evidence enters `NeedsReconciliation`.
2. Answer synchronization modes are explicit lookup data: `NONE`, `COMPLETION_ONLY`, `SELECTED_FIELDS`, `FULL_SYNC`, and `MIRROR_ONLY`. `NONE` stores no provider answers and cannot fulfill a required data requirement. `COMPLETION_ONLY` stores completion evidence but no authoritative answers.
3. Formbricks and delegated Microsoft callbacks, plus Google Pub/Sub pushes, extend the existing `IncomingWebhookMessage` and `IncomingWebhookEffectOutbox` mechanism. Intake retains exact bounded bytes, verifies provider proof, deduplicates, persists one durable effect, and acknowledges promptly.
4. A fenced worker re-verifies evidence, fetches provider data when supported, normalizes against the pinned form and mapping revisions, validates, records fulfillment, and invokes Application finalization. Callback controllers never mutate registration aggregates.
5. External completion is evidence only. ISLAMU confirms registration only after its own transactional identity, deduplication, workflow, approval, and capacity checks succeed.

## Transaction boundaries

- Callback acceptance, incoming-message persistence, deduplication, and durable effect creation share one local transaction.
- Provider reads and writes occur outside business transactions and only from durable, retryable work.
- Normalized answer persistence, requirement fulfillment, registration finalization, and required outbox records commit through `IUnitOfWork` with retry-stable identities.
- Delivery or provider settlement cannot rewrite a published form version, mapping revision, or in-flight attempt.

## Rejected alternatives

The following consultation anti-patterns are forbidden:

1. Treating one `ExternalRegistrationUrl` as the integration model.
2. Storing attendee answers in Event custom-property values.
3. Storing all authoritative response data only as JSONB.
4. Using provider question IDs as authoritative field IDs.
5. Letting webhook controllers insert registrations directly.
6. Treating an external success page as completion proof.
7. Treating iframe navigation as completion proof.
8. Treating provider completion as capacity confirmation.
9. Overloading `ApprovalStatus` with synchronization states.
10. Defining one provider interface whose implementations mostly throw unsupported-operation errors.
11. Inferring provider capabilities from a provider-name string.
12. Silently switching in-flight attempts to another provider.
13. Silently applying external schema changes to published form versions.
14. Depending on undocumented Microsoft internal APIs.
15. Placing raw answers in ordinary outgoing registration webhooks.
16. Representing consent only as a Boolean answer.
17. Attaching an anonymous external response directly to a User without verified correlation.
 18. Making a provider-specific field mandatory across alternative channels without an authoritative equivalent.

## Consequences

- Registration data gains an explicit privacy and lifecycle owner, at the cost of additional relational tables and mappings.
- Provider integrations remain honest about supported capabilities and can fail closed without combinatorial provider classes.
- Published form interpretation and in-flight attempts remain stable across provider, schema, and mapping changes.
- Typed answers remain filterable and governable, while sensitive values and raw provider evidence receive narrower handling.
- Existing webhook fencing, replay, retention, and redrive infrastructure is reused instead of duplicated.
- Deterministic schema generation, database constraints, provider conformance fixtures, callback replay tests, and idempotent finalization tests become required verification surfaces.

## Related

- `dev/active/registration-data-collection/registration-data-collection-consultation.md` §§1–11, 17–18, 24
- `dev/active/registration-data-collection/registration-data-collection-plan.md` D1–D3, D5–D7, D14
- `docs/CUSTOM_PROPERTIES.md`
- `docs/WEBHOOKS.md`
- ADR-002: Outbox Pattern
