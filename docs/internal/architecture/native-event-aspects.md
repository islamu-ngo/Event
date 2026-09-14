# Native EventAspects operation boundary

> **Status:** Implemented, canonical SQLite verification.
> **Source anchors:** `src/Explore.Application/Features/EventAspects/`, `src/Explore.API/Controllers/EventAspectController.cs`.

## Ownership and composition

The cohort contains **ten declarations**, not six files or eight operations: Create, Update and Delete for each of Islamic and Tech, plus public and managed detail queries for each. The two `UpsertEvent*AspectCommand.cs` files each declare Create and Update; each detail-query file also declares a managed query. Create/update return `BaseCommandResponse<Guid>`, deletion returns `bool`, and queries return nullable aspect DTOs.

Every request uses exactly one native `ICommand<TResult>` or `IQuery<TResult>`. `EventAspectController` injects ten closed handler interfaces and calls `ExecuteAsync` or `QueryAsync`. Automatic Application discovery provides scoped authorization -> performance -> handler composition, without an explicit registration, MediatR shim, service locator or generic dispatcher. The same port instance is reused within a scope; different scopes get different protected instances. Existing repository, manual-validator and HybridCache dependencies remain unchanged.

All six commands retain Event/Update authorization, including deletion. Both managed queries retain Event/ViewManagement. `RequestAuthorization<TRequest>` uses `AuthorizationResourceContextResolver` to load persisted parent facts; `EventRepository.GetEventWithDetails` uses no-tracking identity resolution, so an unsaved tracked parent cannot substitute authority and a later persisted parent change is observed. Public reads retain `IsPubliclyEligibleAsync`: publication, visibility, publisher participation and tenant filters are not replaced with a weaker status-only test.

## Public absence repair

Before this change, both public controller methods returned `Ok(null)`. Real SQLite HTTP tests observed **204 No Content**, contradicting their existing documented404 contract. Only the two public null branches now emit the existing not-found ProblemDetails envelope with404 and `application/problem+json`. A controller-local `JsonResult` preserves that media type because the controller's success-only `Produces` filter rewrites an `ObjectResult` content type. Routes, operation IDs, schemas and generated clients do not change; shipped OpenAPI parity covers all ten operations and aspect schemas.

Unavailable, private, draft, foreign-tenant and missing-aspect public reads share the same non-disclosing response. Authorized managed absence retains its previous null/HTTP204 behavior. Create-conflict409, grouped update-missing404, manual validation400 and repeated delete204 are unchanged. No new concurrency contract is introduced.

## Persistence, patch and cache limits

PATCH preserves omitted groups and fields. Nullable replacements use the existing `{ "hasValue": true, "value": null }` shape. Cleared nullable response properties remain omitted by the configured serializer; enum responses retain their existing string representation. Aspect IDs remain server-owned shared parent keys.

Successful Create/Update continue evicting `event:detail:{eventId}` and `CacheTags.EventListByTenant(tenantId)` after persistence. Delete deliberately retains its existing lack of HybridCache invalidation; this is not a claim of immediate output-cache freshness. No persistence APIs or output-cache policy change.

Cancellation reaches the native authorization boundary and existing token-aware validator/cache/public-eligibility calls. Tests cancel all eight secured operations at the external authorization boundary and synchronize real HTTP aborts with an entered-authorization signal before cancelling. Cancellation propagates without aspect mutation. Inherited repository CRUD, aspect detail loads and the parent authorization load remain tokenless: **full database cancellation, transactional cancellation rollback and an atomic authorization/write race fence are not provided by this migration**.

## Dormant AI limit

The four aspect AI mappers retain their exact concrete command/result types. Both Upsert mappers still produce **Update** commands, not Create. Tool definitions and proposal validation remain unchanged. At base `b706fb0c01367c668b47fcacb1ccce388ebeba3d`, confirmation executes only CreateEventDraft and rejects other kinds; there are zero runtime aspect sends to replace. This migration does not activate dormant proposals, enforce their currently unused concurrency claims, or redesign AI execution. Enabling those actions is a separate runtime capability change.

## Evidence and scope

`NativeEventAspectsOperationTests` first failed native discovery (zero native shapes for CreateEventIslamicAspectCommand); existing creation-mapper tests passed. `NativeEventAspectsHttpTests.PublicAbsenceAndPrivacy_ReturnDocumented404` first failed with204 instead of404 on both kinds, after correcting fixture publisher participation. Early fixture corrections also aligned PATCH inputs with the existing OptionalUpdate shape and response assertions with omitted null/string-enum serialization; these were test corrections, not product behavior changes.

The focused green run contains19 API tests (including existing controller contracts) and17 Application tests covering native discovery, creation mapping and AI aspect mapper/tool contracts. SQLite tests use real Application/Persistence and the API TestServer, real local authorization and persisted tenant/role fixtures; only the external authorization provider is substituted for controlled failure/cancellation. They cover all ten ports, same/cross-scope execution, fresh parent facts, foreign-owner denial/no mutation, public versus managed privacy, writes, cache effects, cancellation and shipped OpenAPI parity. Multi-provider matrix and full-platform suites belong to the parent workstream, not this SQLite evidence.

Release solution build completed with zero errors and1374 warnings in unchanged repository files. The focused Application rebuild also retains two CS8620 warnings in the unchanged NSubstitute setup at `EventAspectMapperTests.cs:24,76`; the same warnings are present in the pre-product-edit Red receipt. No warning is suppressed. The complete architecture run passed607 tests with one pre-existing skip (the broad response-metadata contract); authorization parity is included. LSP directory diagnostics reported no errors for the12 feature files; fresh per-file diagnostics timed out for API/test files, so their successful compiler/analyzer execution is the available verification, not an asserted clean LSP result.

Reproduction commands, from the isolated worktree with `TMPDIR=$HOME/.cache/agent-tmp` and `DOTNET_PROCESSOR_COUNT=4`:

```bash
dotnet build --configuration Release --verbosity quiet -m:4
dotnet test --project tests/Event.API.IntegrationTests/Event.API.IntegrationTests.csproj --configuration Release --no-build -- --treenode-filter '/*/*/NativeEventAspectsHttpTests|EventAspectControllerTests/*' --minimum-expected-tests 19 --no-progress --maximum-parallel-tests 1
dotnet test --project tests/Event.Application.UnitTests/Event.Application.UnitTests.csproj --configuration Release --no-build -- --treenode-filter '/*/*/*EventAspect*|CreateEventDraftAiToolExecutorTests/*' --minimum-expected-tests 4 --no-progress --maximum-parallel-tests 1
dotnet test --project tests/Event.Architecture.Tests/Event.Architecture.Tests.csproj --configuration Release --no-build -- --minimum-expected-tests 1 --no-progress --maximum-parallel-tests 1
```

No Series registration, AgendaItems controller/MCP port, shared authorization, AI runtime or schema files are owned by this slice. Rollback is code-only; no database migration or operator repair is required. It would restore legacy dispatch and the incorrect public204 result.

Public impact: [Event aspect management](../../public/features/event-aspect-management.md).
