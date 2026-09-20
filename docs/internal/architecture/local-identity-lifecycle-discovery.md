# Local identity lifecycle discovery

> **Audience:** Contributors
> **Status:** Implemented
> **Owner:** Authentication
> **Source anchors:** `src/Explore.Application/Features/Authentication/Local/Handlers/Queries/GetLocalIdentityLifecycleCapabilitiesQueryHandler.cs`, `src/Explore.API/Hateoas/Assemblers/UserResourceAssembler.cs`, `src/Explore.API/Controllers/InstanceOnboardingController.cs`

## Native query boundary

`GetLocalIdentityLifecycleCapabilitiesQuery` implements `IQuery<LocalIdentityLifecycleCapabilities>` and is discovered by the existing `AddNativeOperations` Application scan. Its closed `IQueryHandler` is composed as authorization -> performance -> business handler. No explicit registration, compatibility request, bypass marker, or new authorization grant is needed.

`UserResourceAssembler` and `InstanceOnboardingController` constructor-inject this exact query port into readonly fields. The public onboarding authentication-configuration action retains its cancellation-token-only signature and calls the injected port, keeping unrelated operations in the mixed controller on their existing mediator contracts. This closes only this discovery query and its two consumers, not the Authentication, InstanceOnboarding, or Users operation families.

## Two distinct authorities

- Public discovery requires the active primary provider to be Local and instance email delivery to be available. It can advertise email verification and password recovery, never password change. The response shape, route names, HTTP methods and private/no-store behavior remain unchanged.
- Account discovery requires an exact persisted Local session and a linked Ready credential. Session reads compare the subject, security stamp and email-verification fact, then check credential operation state and the live application user/login/actor binding. A missing, stale, invalid or unavailable session produces no lifecycle capabilities. Ready Local account authority does not depend on which provider is currently primary.
- Email capability controls email affordances independently of password change. Email-delivery intent can also make an unverified session invalid under the existing sign-in policy. Discovery does not create a session, reset credentials, send mail, or authorize a subsequent mutation.
- Before account discovery, the assembler requires the authenticated Local subject to equal the DTO ID. Foreign DTOs and non-Local/anonymous principals cannot borrow account lifecycle affordances. HAL remains the server-owned UI signal; mutation handlers retain their own authority checks.

## Read-only classification and failure behavior

`ValidateSessionAsync` calls persisted no-tracking session/credential and setting reads; it does not refresh or replace credentials. `ReadLinkedIdentityAsync` reads metadata, operations and the exact application binding. `EmailDeliveryCapabilityResolver.ResolveAsync` reads authoritative SMTP settings and secret availability, without SMTP handoff or persisted mutation. Provider/secret resolution may populate memory caches and emit telemetry; these are not domain writes.

Cancellation is forwarded from the public action and from `HttpContext.RequestAborted` in the assembler to the query and its existing callees. Existing session-validation unavailability fails closed, while uncaught provider/email read errors remain errors rather than successful empty discovery responses. No catch or retry behavior was added by this migration.

## Verification anchors

- `Event.Application.UnitTests/Operations/NativeLocalIdentityLifecycleDiscoveryTests.cs`: automatic scoped native registration and removal of the legacy request shape.
- `Event.API.IntegrationTests/Features/NativeLocalIdentityLifecycleDiscoveryTests.cs`: real decorated query against SQLite-backed native identity/settings, real authentication, serialized assembler output, public and current-user HTTP HAL, ready-to-ChangeRequired reset revocation, invalid/stale sessions, foreign DTOs, missing/disabled email, external primary provider, cancellation and read-failure handling.

Tests reuse existing native identity fixtures and production authority services. No internal query/repository mocks, source-text inventories, timing sleeps, new secret values, or full database-provider matrix are introduced. The protocol did not change, so public adopter guidance and generated API/client contracts need no migration.
