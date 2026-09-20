# Generated Mapping And Native Operations

> **Audience:** Contributors | Maintainers | AI agents
> **Status:** Implemented; final integrated assurance is a separate gate
> **Owner:** Contributor Experience
> **Last Verified:** 2026-09-19
> **Source Anchors:** `src/Explore.Application/Mappings/`, `src/Explore.Application/Contracts/Operations/`, `src/Explore.Application/OperationServicesRegistration.cs`, `src/Explore.Application/Operations/`, `src/Explore.Application/Notifications/NotificationHandlerExtensions.cs`

## Mapping Ownership

Application handlers obtain entities from repositories and call named static
mappers. `Mappings/` contains Mapperly partial methods and explicit projections;
there is no injectable general-purpose mapper or runtime profile discovery.
Riok.Mapperly 4.3.1 is pinned centrally and private to Application. Domain and the
browser do not take a generator dependency. See the
[dependency/source register](legal/dependencies/mapperly.md) for terms, hashes,
notices and the limits of generated-output claims.

Use `RequiredMappingStrategy.Both` for generated maps; `.editorconfig` promotes
RMG012, RMG020, RMG037 and RMG038 to errors. A same-name sensitive member can still
map successfully: independently review destination disclosure, serialized fields,
nullable navigation flattening and ownership of mutable nested collections.
Do not weaken a response's nullable contract just to silence a mapping warning.

Inbound data is not an authority source. Explicit Application construction/update
allowlists and aggregate methods own mutation; never use generated reverse maps
to write tenant, identity, audit, lifecycle, concurrency or navigation state.
Existing behavior and independently specified DTO contracts determine tests.
Generated C# stays under `obj`; commit mapper inputs and any affected transport
artifacts through their normal generators, not hand-edited generated files.

## Operation Shapes And Callers

A request implements exactly one marker:

| Shape | Consumer dependency | Method |
|---|---|---|
| Void command | `ICommandHandler<TCommand>` | `Task ExecuteAsync(command, cancellationToken)` |
| Result command | `ICommandHandler<TCommand, TResult>` | `Task<TResult> ExecuteAsync(command, cancellationToken)` |
| Query | `IQueryHandler<TQuery, TResult>` | `Task<TResult> QueryAsync(query, cancellationToken)` |

Classify by observable effects. Concrete handwritten requests normally use sealed
records; legitimate target identifiers are distinct from trusted current actor
and tenant context. Inject the exact closed interface in HTTP, MCP, nested
handlers, jobs, middleware and other consumers. Singleton workers retain their
established per-execution scopes. No generic sender or container lookup belongs
in a controller. Capability-specific service aliases are not dispatch facades.

## Protection And Composition

`OperationServicesRegistration.AddNativeOperations` discovers Application once,
checks one shape/handler per request, and registers a scoped concrete owner plus
closed decorated interfaces. Its cached factories compose:

```text
caller -> authorization -> performance -> business handler -> repository/service
```

`RequestAuthorization<TRequest>` preserves request facts, typed enrichment,
persisted-resource resolution and provider evaluation. An unannotated request
may have reviewed public, capability, handler-owned or worker authority; it is
not permission to bypass those checks. Denial and provider unavailability remain
distinct failures. Native timing excludes authorization and logs only request
type and elapsed time for successful slow calls, never payload or identity.

Validators are manually instantiated. Transactions, optimistic concurrency,
durable outbox insertion and privacy-erasure fences stay in their existing
owners. There is no universal transaction, validation or retry decorator.
Non-disposable service aliases share the same scoped concrete owner; disposable
handlers must not acquire another disposal owner through an alias.

`ValidateNativeOperationRegistrations` checks the final service descriptors.
`ValidateNativeOperations` checks cached constructor dependency availability;
it does not execute a request or construct the whole graph. Deep construction
uses `ValidateNativeOperationsDeepAsync` against the actual final provider in a
disposable scope in CI. The scoped re-entry guard clears state in `finally` and
reports bounded type-only errors. It cannot prove arbitrary unrelated factories.
OpenAPI generation remains descriptor-only; normal/Testing shared API startup
performs bounded preflight before its workers/traffic. Standalone's own migrations
and administrator bootstrap precede that shared runtime preflight.

## Notifications And Durable Effects

Settings producers inject typed `IEnumerable<INotificationHandler<T>>` and await
`NotificationHandlerExtensions.HandleAsync`. Registration order is delivery
order: setting cache invalidation precedes audit, and the policy cache handler
owns policy changes. Delivery is sequential and stops at the first exception.
Preserve supplied cancellation and deliberate post-commit `CancellationToken.None`
sites. Failure after commit is an incomplete effect, not a rolled-back write.
Existing durable outboxes retain retry/idempotency ownership; typed in-process
notifications do not replace them.

## One Build And Operator Contract

AutoMapper, MediatR and MediatR.Contracts are absent from the supported dependency
graph. There is no edition-dependent C# define, commercial version group or
vendor license binding. CI and both API/Blazor Dockerfiles require locked restore.
The canonical Setup catalogue owns supported environment metadata; its generator
owns both the JSON catalogue and the public reference's generated section.

```bash
dotnet restore
dotnet run --project eng/setup-assistant/EnvironmentCatalogueGenerator/EnvironmentCatalogueGenerator.csproj -- --write
dotnet restore --locked-mode
dotnet run .ci/scripts/validate-dependency-license-policy.cs -- .
```

Never edit lockfiles or generated catalogue sections manually. The
[operator migration checklist](../public/documentation/readme/configuration-and-operations/environment-variables.md#removed-edition-inputs)
owns removed environment/build inputs; [IP governance](legal/IP_GOVERNANCE.md#single-edition-dependency-policy)
owns licensing policy. This removal does not change database schema or secret
provider selection.

## Assurance And Follow-Ups

Build/Setup, lock, audit and dependency-policy evidence must identify its exact
inputs and failures. Compiled/runtime architecture, HTTP/HAL, generated-contract,
privacy and real-provider checks remain separate final workstream obligations.
A license-policy pass is not legal certification, an SBOM is not a vulnerability
scan, and a source search is not runtime proof. No measured performance or
source-generation follow-up is justified by this migration alone; create a
bounded backlog item only from an actual unresolved defect or measured need.

See [ADR-030](adr/ADR-030-generated-mapping-and-native-operations.md) for the decision
and [architecture](ARCHITECTURE.md#protected-native-operations) for composition.
