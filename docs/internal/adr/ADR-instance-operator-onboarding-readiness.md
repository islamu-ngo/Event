# ADR-030: Instance Operator Onboarding & Runtime Readiness Architecture

> **Audience:** Contributors | Operators | AI agents
> **Status:** Implemented
> **Owner:** Security / Platform
> **Last Verified:** 2026-09-14
> **Source Anchors:** `src/Explore.Domain/ValueObjects/InstanceOperatorIdentityReadiness.cs`, `src/Explore.Application/Services/InstanceOperatorIdentityService.cs`, `src/Explore.Application/Features/InstanceOnboarding/Services/InstanceOnboardingCompletionOperation.cs`, `src/Explore.Application/Services/Registration/PaidCheckoutActivationService.cs`, `src/Explore.API/Controllers/InstanceOperatorIdentityController.cs`

- **Decision:** Decouple process-level .NET host startup from operator business identity; persist operator identity as a versioned document in `SystemSetting` storage (`instance.operator_identity`); gate dependent disclosures and commerce fail-closed at runtime.
- **Date:** 2026-09-14
- **Extends:** [ADR-027](ADR-027-first-class-authentication-provider-matrix.md), [ADR-022](ADR-022-paid-event-commerce-and-stripe-connect.md), and [ADR-007](ADR-007-durable-security-admin-audit-trail.md).

## Context

Prior to this architecture, `Explore.API` and `Event.Standalone` enforced mandatory startup validation on the `Instance:OperatorIdentity` options section via `ValidateOnStart()` and registered a throwing singleton factory for `IInstanceOperatorIdentity`. If operator identity environment variables (`INSTANCE__OPERATORIDENTITY__*`) were absent or incomplete in `.env`, the host crashed with an `OptionsValidationException` during dependency injection container build.

This created a severe operational paradox for self-hosters: the host could not start to serve the very web onboarding wizard (`/setup`) intended to guide the operator through first-run setup. Furthermore, it forced legal entity attribution to be defined in process environment variables rather than managed through the platform's standard configuration and administration planes.

## Decisions

### 1. Decoupled Process Startup & Scoped Readiness Assessment

Host startup in `Explore.API` and `Event.Standalone` is decoupled from operator legal identity. Mandatory options validation on process boot and the throwing singleton `IInstanceOperatorIdentity` are eliminated.

In their place, runtime identity validity is evaluated per operation through the scoped interface `IInstanceOperatorIdentityReadinessEvaluator`, returning a strongly typed `InstanceOperatorIdentityReadinessAssessment`:

```csharp
public sealed record InstanceOperatorIdentityReadinessAssessment(
    bool IsReady,
    string? FailureCode,
    ImmutableArray<string> ReasonCodes,
    InstanceOperatorIdentity? Identity,
    Guid? DocumentRevision);
```

Health probes (`/health/live`, `/health/ready`) monitor infrastructure and database readiness only. They remain green when operator identity is incomplete, ensuring load balancers and orchestrators keep the onboarding wizard and setup endpoints reachable.

### 2. Persisted Identity in `SystemSetting` with Dedicated Mutation Guard

Operator identity is stored as a versioned JSON document in existing `SystemSetting` storage under key `instance.operator_identity`:

- **Storage Structure:** `InstanceOperatorIdentitySettings` containing `OperatorId` (server-generated UUIDv7), legal names, jurisdiction, registration identifier, contact email, HTTPS website, terms, privacy, legal notice URLs, and `Revision` (UUIDv7 optimistic concurrency stamp).
- **Generic API Mutation Guard:** `InstanceOperatorIdentitySettingKeys.RejectGenericMutation` is registered in `SystemSettingRepository` and `TenantSettingRepository`. Generic settings endpoints (`/api/settings/system/...`) are forbidden from reading, mutating, or bypassing the dedicated identity management lifecycle.
- **Dedicated Management API:** Exposed exclusively through `GET/PUT /api/instance-operator-identity`, authorized under active setup-secret authority during onboarding or authenticated `platform.admin` authority on completed instances.

### 3. Fail-Closed Consumer Gating

Rather than crashing the host or returning misleading placeholder values, dependent consumers explicitly evaluate readiness and fail closed:

1. **Public Legal Documents (`GetPublicLegalDocumentQueryHandler`):** If instance identity is incomplete, the handler returns `PublicLegalDocumentQueryResult.Unavailable()`, mapped by the controller to HTTP 503 Service Unavailable with `Cache-Control: no-store`.
2. **Public Experience Settings (`GetPublicExperienceSettingsQueryHandler`):** Returns `InstanceOperator = null` when identity is not ready, preventing unverified entity disclosures.
3. **Paid Commerce Gating (`PaidCheckoutActivationService` & `GetRegistrationCheckoutCompositionQueryHandler`):** Evaluates `EvaluateSaleControlAsync`. If operator identity is not ready, activation returns `PaidCheckoutActivationResult.Failure("instance_operator_identity_unavailable", ...)`, blocking payment reservations and gateway handoffs. Checkout composition returns `null` for paid ticket types.
4. **Historical Immutability:** Historical `PaidOrderAcceptanceSnapshot` records remain immutable; they snapshot validated identity at acceptance time and are never modified by subsequent operator identity updates.

### 4. Transactional Onboarding Completion Enforcement & Rollback

`InstanceOnboardingCompletionOperation.PersistAsync` evaluates `IInstanceOperatorIdentityReadinessEvaluator` within the serializable completion transaction before creating users, default tenants, or assigning administrator roles.

If operator identity is missing or incomplete, the transaction aborts with `BaseCommandResponse.Failure<Guid>("instance_operator_identity_incomplete", ...)`. Zero users, tenants, or roles are committed, and `InstanceBootstrapState.Status` remains `Pending`.

### 5. Permanent Setup Secret Lockout ("The Worst Break" Prevention)

`SetupSecretProvider` checks durable bootstrap completion state (`InstanceBootstrapState.Status == Completed`) independently of operator identity presence.

Even if an already-completed instance suffers corrupted or missing operator identity in `SystemSetting`, setup mode **never** re-enables and the setup secret remains permanently locked (returning HTTP 410 Gone). Only authenticated platform administrators (`platform.admin`) can repair identity via `/admin/instance`.

### 6. Optional Headless Bootstrap Input

Unattended setups may supply structured identity via `INSTANCE__OPERATORIDENTITY__*` in `.env`. When running in `ConfiguredAdministrator` mode, valid configured identity is persisted to `SystemSetting` during bootstrap completion. Once completed, runtime reads use the database only, and subsequent restarts never overwrite database-backed operator identity.

## Consequences

- **Positive:** Self-hosters can launch containers with a minimal `.env` (or zero identity variables) and complete legal identity configuration through the web UI at `/setup`.
- **Positive:** Infrastructure availability is strictly decoupled from business legal compliance.
- **Positive:** Attacking or corrupting stored identity cannot reopen the setup secret or grant unauthenticated platform administrative access.
- **Negative:** Dependent public notices and paid commerce features require valid operator identity to become active, returning HTTP 503 or checkout failure if identity is omitted.
