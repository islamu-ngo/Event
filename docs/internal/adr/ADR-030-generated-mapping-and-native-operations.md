# ADR-030: Generated Mapping And Native Operations

> **Audience:** Contributors | Maintainers
> **Status:** Accepted; final workstream assurance remains separate
> **Owner:** Contributor Experience
> **Last Verified:** 2026-09-19
> **Source Anchors:** `src/Explore.Application/Mappings/`, `src/Explore.Application/OperationServicesRegistration.cs`, `src/Explore.Application/Operations/OperationCompositionValidation.cs`, `Directory.Packages.props`

## Context

Runtime mapping and hidden request dispatch made dependency ownership and caller
closure difficult to review. Keeping frozen packages alongside a commercial
edition also created two incompatible build/configuration promises. The approved
migration replaces these mechanisms, not the application's domain rules,
authorization providers, persistence model or transport contracts.

## Decision

Use Riok.Mapperly 4.3.1 as an Apache-2.0, Application-private build-time generator.
Name each static mapper for its source/destination family. Use explicit
Application allowlists for inbound construction and existing-target changes;
aggregate methods retain identity, tenancy, audit, lifecycle, concurrency and
navigation ownership. Generated output belongs under `obj`, not source control.
Strict diagnostics are necessary but do not prove a disclosure policy.

Use repository-owned `ICommand`, `ICommand<TResult>` and `IQuery<TResult>` markers,
with closed `ICommandHandler`/`IQueryHandler` dependencies and Task-based
`ExecuteAsync`/`QueryAsync`. Do not introduce a generic sender, service locator,
replacement mediator package or old-to-new adapter. Requests have exactly one
shape and exactly one handler. Classification follows effects, not folder names.

Microsoft DI composition discovers the Application graph once and builds scoped
chains in authorization -> performance -> business-handler order. Authorization
remains `RequestAuthorization<TRequest>` plus the capability's existing authority;
manual validators, transactions and durable outbox writes remain handler-owned.
Notification delivery is explicit, ordered and fail-first. A post-commit failure
does not undo a committed write.

Final descriptor validation rejects competing/unprotected registrations. Runtime
preflight checks cached constructor availability without constructing the full
graph; deep scoped construction is CI assurance. This distinction is deliberate:
DI factory bodies and open generics are not proven by `ValidateOnBuild`. The
native construction guard detects native re-entry but is not a general-purpose
factory cycle analyzer.

Ship one dependency graph. Remove AutoMapper, MediatR, MediatR.Contracts, edition
flags, vendor version overrides, licensing mappings and their exhausted audit
exceptions. CI and both API/Blazor Dockerfiles use locked restore. Public operator
instructions describe removal of inputs, not a choice of editions.

## Consequences And Boundaries

- Dependencies are visible at caller construction; capability-split controllers
  retain route, verb, operationId, tag, authorization and HAL semantics.
- Mapping privacy, nullability, collection ownership and aggregate mutation need
  independent behavioral assurance, not only compiler diagnostics.
- Authorization precedes timing; denied operations do not enter business timing.
  No allocation, throughput, AOT, trimming or cold-start improvement is claimed.
- Provider, tenant, capability, worker-scope, transaction, outbox and erasure
  invariants remain owned by their existing implementations. This decision does
  not certify final five-provider or security-review results.
- The package license does not automatically determine generated-output rights.
  Distribution owners retain notice and artifact-review responsibilities.
- Recovery is a reviewed forward correction. Do not restore hidden dispatch or
  obsolete edition switches as a compatibility path. No database migration is
  introduced by this build/configuration change.

## Rejected Alternatives

A mediator facade, another mediator library, Scrutor, indefinite frozen vendor
packages, separate commercial images and blanket transaction/validation decorators
would retain hidden dependencies or change independently governed behavior.
Source-generated DI and performance optimization are not approved deliverables
without a measured need; no speculative framework backlog is created.

## References

- [Mapping and native operation authoring](../MAPPING_AND_OPERATIONS.md)
- [Mapperly dependency, source register and generated-output obligations](../legal/dependencies/mapperly.md)
- [Single-edition dependency policy](../legal/IP_GOVERNANCE.md#single-edition-dependency-policy)
- [Protected native composition](../ARCHITECTURE.md#protected-native-operations)
- [I-VSD workstream assessment](../../../islamic-value-sensitive-design/i-vsd-mapping-and-cqs-migration.md)
