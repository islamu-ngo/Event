ABOUTME: Canonical contributor guide for record contracts, value semantics, and generated client records.
ABOUTME: Explains ownership, class exclusions, persistence boundaries, privacy, generation, and verification.

# Record Contracts And Value Semantics

> **Audience:** Contributors | Integrators | AI agents
> **Status:** Implemented
> **Owner:** Contributor Experience
> **Last Verified:** 2026-08-26
> **Source Anchors:** `src/Explore.Application/Responses/BaseCommandResponse.cs`, `src/Explore.Domain/ValueObjects/`, `src/Explore.Persistence/`, `eng/tools/Explore.GeneratedContracts/`, `tests/Explore.GeneratedContracts.Tests/`, `tests/Event.Architecture.Tests/GeneratedClientRecordArchitectureTests.cs`

This page is the canonical implementation guide for choosing records or
classes, preserving shallow immutability, and evolving generated C# contracts.
The shorter rules in [GOVERNANCE.md](GOVERNANCE.md) remain normative.

## Decision Rule

Choose the representation from ownership and behavior, not naming convention.

| Contract shape | Representation | Reason |
|---|---|---|
| Immutable data with consumed value equality | Sealed record class | Value-oriented state with controlled construction |
| Small self-contained value | `readonly record struct` | Copy/value semantics without reference identity |
| EF entity, aggregate, or persisted lifecycle row | Class | Identity and controlled lifecycle mutation |
| Handler, service, controller, validator, or repository | Class | Behavior and dependency ownership |
| Mutable Blazor edit/component state | Class | Framework and user-driven mutation |
| Immutable Application command result | Record with named factories | Invalid or contradictory states stay unrepresentable |
| Generated response/value contract | Generator-owned nominal record | Immutable client construction and `with` copies |
| Generated protocol input, HAL, inherited, file, or exception shape | Generator-owned class | Binding, identity, inheritance, or framework requirements |

Development mode does not justify compatibility constructors, aliases,
duplicate DTOs, or legacy generated clients. Break the source contract, migrate
all callers, regenerate governed artifacts, and document the change.

## Clean Architecture Ownership

### Domain Values

`Money`, `GeoCoordinate`, `LocalDateRange`, `UtcInstantRange`, and `AtprotoDid` are immutable
Domain values under `src/Explore.Domain/ValueObjects/`.

- Factories normalize and reject invalid state before construction.
- Money uses normalized currency and checked minor units.
- Coordinates enforce latitude and longitude bounds.
- Local calendar ranges and UTC instant ranges remain different concepts.
- Domain values do not depend on EF Core, serializers, API models, or provider
  packages.

Entities remain classes. They accept semantic values at business boundaries
and retain identity and lifecycle ownership.

`AtprotoDid` represents only a live AT Protocol DID. Parsing is explicit,
preserves ordinal case-sensitive identity, accepts syntactically valid future
methods, and exposes the exact scalar only through `.Value`. Its exceptions and
`ToString()` are value-free. `AtprotoIdentity` accepts the typed value for live
construction and verified refresh while retaining its scalar `Did` owner
property for EF. Privacy erasure is aggregate-owned: it replaces the live value
with an internal `did:deleted:*` tombstone and clears provider metadata without
passing that tombstone through the live parser.

### Application Requests And Results

Concrete native and remaining MediatR requests default to sealed records. Native
requests implement exactly one of `ICommand`, `ICommand<TResult>` or
`IQuery<TResult>` from `Explore.Application.Contracts.Operations`; they never also
implement a MediatR request contract. A request contains client-owned intent and
trusted facts supplied by server adapters; body `TenantId` or `UserId` never
becomes current authority. Existing immutable result factories and JSON shapes
are unchanged by native `ExecuteAsync`/`QueryAsync` handlers.

`BaseCommandResponse<TKey>` and its concrete descendants use immutable
valid-state factories. Callers select success, validation, not-found,
authorization, authentication, conflict, or quota outcomes instead of setting
properties after construction. API mapping continues through the shared RFC
7807 command-response mapper.

### Published Collections

Records are shallowly immutable. A handwritten immutable contract must copy a
caller-owned mutable collection and expose a serializer-compatible read-only or
immutable shape.

- Do not infer sequence equality from record equality.
- Test list, set, dictionary, array, and byte semantics explicitly.
- Replace immutable collections with `SetItem`, a new snapshot, or `with`;
  never mutate through a retained indexer.
- Keep aggregate and service backing mutation private behind read-only views.

`PublishedCollectionContractArchitectureTests` owns the cross-layer
classification and rejects new or stale mutable exposure.

## Persistence Boundary

EF Core intentionally persists the selected Domain concepts through existing
scalar owner columns. The values are not independently tracked entities,
complex properties, owned types, or broad converters.

Generated migrations add these portable owner-table checks:

| Constraint | Enforced invariant |
|---|---|
| `CK_EventTicketType_MoneyNonnegative` | Ticket amounts cannot be negative |
| `CK_LocationPii_CoordinateShape` | Coordinates are absent together or valid together |
| `CK_EventAgendaItem_LocalDateRange` | Agenda local dates are ordered |
| `CK_EventSession_LocalDateRange` | Session local dates are ordered when present |

PostgreSQL, SQLite, SQL Server, MariaDB, and MySQL own generated migration
artifacts. MariaDB and MySQL run a shared PII-free pre-DDL check so malformed
legacy rows fail before non-transactional DDL can install a partial constraint
set.

Never hand-edit a migration or model snapshot. Correct the Domain/EF model or
generation input, remove only the unapplied development migration through the
approved workflow, and regenerate every provider artifact.

## Generated C# Client

The OpenAPI document owns wire behavior. The repository generation pipeline
owns the checked-in C# representation:

1. Pinned NSwag emits the raw C# client.
2. The repository fixes the known NSwag `void` response artifact.
3. `eng/tools/Explore.GeneratedContracts` parses the source with SDK Roslyn.
4. The policy computes protocol-input closure and both sides of inheritance.
5. Eligible response/value classes become nominal records and ordinary setters
   become `init`.
6. Final byte normalization produces deterministic checked-in output.

The transformer does not copy an NSwag template and adds no runtime package.
Its exact mutable-class manifest is
`eng/tools/Explore.GeneratedContracts/mutable-generated-contracts.txt`.

### Protected Shapes

The policy retains classes for:

- API clients and interfaces;
- protocol inputs and nested input graphs;
- HAL resource and collection wrappers;
- both base and derived participants in inheritance;
- PATCH/update contracts;
- file and exception infrastructure; and
- contracts with evidenced post-construction UI or service mutation.

The architecture ratchet compares the complete computed eligibility set with
the generated declarations. A record/class swap cannot hide behind an unchanged
record count.

### JSON, AOT, And Diagnostics

Generated record properties are `init` except
`[JsonExtensionData] AdditionalProperties`, which remains settable so
System.Text.Json AOT can populate unknown members such as future HAL `_links`.
`AppJsonSerializerContext` includes the required `JsonElement` metadata for
round trips.

Every generated record receives a value-free `PrintMembers` implementation.
Compiler-generated `ToString()` therefore cannot enumerate PII, tokens, free
text, or provider values. This defense does not make whole-record logging
acceptable: log only bounded approved scalars.

## Construction Patterns

Use named factories for command results:

```csharp
return BaseCommandResponse<Guid>.ValidationFailure(errors);
```

Use initializers and `with` for generated response/value records:

```csharp
var actor = new ActorDto { Id = actorId };
var replacement = actor with { Id = replacementId };
```

Do not add a mutable compatibility path when a caller fails to compile. Migrate
the caller to the owning construction pattern.

## Change Workflow

1. Identify the owning layer and decide whether behavior needs value or
   reference identity.
2. Write a focused test that fails for the missing semantic behavior.
3. Change the owning source contract; do not start with downstream adapters.
4. Migrate callers and preserve trusted identity, PATCH, HAL, serialization,
   and collection behavior.
5. Regenerate OpenAPI, migrations, or the C# client only through repository
   commands.
6. Update this page only when the architecture changes; update
   [API_CHANGELOG.md](API_CHANGELOG.md) for public contract breaks.
7. Run the smallest owning test slice, then the phase-level project gate.

## Focused Verification

Generated-contract policy and behavior:

```bash
dotnet test --project tests/Explore.GeneratedContracts.Tests/Explore.GeneratedContracts.Tests.csproj --configuration Release --verbosity quiet
dotnet test --project tests/Event.Architecture.Tests/Event.Architecture.Tests.csproj --configuration Release --no-build --verbosity quiet -- --treenode-filter "/*/*/*GeneratedClientRecordArchitectureTests/*" --minimum-expected-tests 4 --no-progress --maximum-parallel-tests 1
dotnet test --project tests/Explore.Blazor.Client.Tests/Explore.Blazor.Client.Tests.csproj --configuration Release --no-build --verbosity quiet -- --treenode-filter "/*/*/*EventApiClientSerializationTests/*" --minimum-expected-tests 12 --no-progress --maximum-parallel-tests 1
```

Regenerate the client through MSBuild:

```bash
dotnet msbuild src/Explore.Blazor.Client/Explore.Blazor.Client.csproj -target:GenerateApiClient -property:Configuration=Release
```

The second generation run must be byte-identical. The OpenAPI contract workflow
treats the tool manifest, transformer source, mutable manifest, direct tests,
MSBuild integration, and generated product as relevant inputs.

## Review Checklist

- Does the type represent value data rather than entity or lifecycle identity?
- Are invalid states impossible or factory-controlled?
- Are caller-owned collections defensively copied?
- Are trusted tenant/user facts absent from body authority?
- Are PATCH omission, explicit null, and replacement still distinct?
- Do HAL links remain the client action authority?
- Does generated extension data still round-trip under AOT?
- Can diagnostic text expose a member value?
- Were generated artifacts produced only by repository commands?
- Did the change avoid compatibility shims and whole-record logging?

## Related Documentation

- [GOVERNANCE.md](GOVERNANCE.md) - normative record-selection rules.
- [ARCHITECTURE.md](ARCHITECTURE.md) - layer ownership and contract flow.
- [DOMAIN.md](DOMAIN.md) - entity model and persisted invariants.
- [API.md](API.md) - OpenAPI and generated-client contract.
- [BLAZOR.md](BLAZOR.md) - generated-client consumption and HAL authority.
- [TESTING.md](TESTING.md) - project roles and test commands.
- [API_CHANGELOG.md](API_CHANGELOG.md) - public breaking changes.
