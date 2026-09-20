# Mapperly Dependency And Projection Contract

> **Audience:** Contributors | Maintainers | Distribution reviewers
> **Owner:** Contributor Experience
> **Reviewed:** 2026-09-19 (repository implementation); upstream admission sources retained from 2026-09-11
> **Scope:** Application DTO projections, generator provenance and distribution obligations

## Runtime Mapping Retirement

All former Application AutoMapper profile families now use repository-native
static projections with Mapperly-generated implementations or explicit
handler-owned input allowlists. AutoMapper registration, unused constructor
dependencies, package pins and the empty profile namespace are removed.
Regenerated locks remove its dependency edges; compiled-reference and service
registration guards replace the obsolete vendor depth-limit check.

The exact AutoMapper license override and vulnerability exception are also
removed. The vulnerability validator rejects the formerly suppressed advisory
instead of relying on a runtime mapping-depth ceiling. MediatR and
MediatR.Contracts are also removed after native request/caller migration. The
single supported build has no commercial edition selector, version override or
vendor licensing configuration. See [mapping and operations](../../MAPPING_AND_OPERATIONS.md)
and [ADR-030](../../adr/ADR-030-generated-mapping-and-native-operations.md).
The pilot evidence below is historical admission evidence, not the current
dependency inventory.

## Admission

Admit `Riok.Mapperly` **4.3.1**, licensed under **Apache-2.0**, as a
build-time source generator in `Explore.Application`. The central version pin
and Application `PackageReference` with `PrivateAssets="all"` keep generator
assets private to that project. Domain and browser projects must not reference
Mapperly. Private assets control dependency propagation, not licensing rights.

The official package metadata identifies repository revision
`036698914bd48be888b25322938b565db72ff7ff` and declares no NuGet dependencies
for its .NET Standard 2.0 dependency group. Actual restore and artifact checks
remain the authority for the consumed closure.

The admission permits the existing public AGPL-3.0-or-later distribution,
Steward-approved enterprise internal on-premises/VPC terms, and nonprofit or
humanitarian grants. Apache-2.0 imposes no copyleft, hosting, seat, or
field-of-use restriction on independently authored ISLAMU code. The repository's
separate prohibition on proprietary SaaS licensing remains in force. The CLA
does not relicense Mapperly or confer rights in third-party material.

## Redistribution Obligations

Any distribution containing Mapperly or covered upstream material must include
the Apache-2.0 license, retain applicable copyright and attribution notices, and
carry any applicable upstream NOTICE. Modified upstream files require change
notices. No upstream files are modified by this migration.

Apache-2.0 includes a contributor patent grant with patent-litigation termination,
does not grant trademark rights, and includes warranty and liability limitations.
No source offer is required by Apache-2.0 itself. Applicable obligations of the
assembled ISLAMU distribution remain separate.

The pinned upstream LICENSE names riok GmbH. The independent metadata review
located no NOTICE at the pinned repository revision. Inspection of all 30
entries in the restored nupkg found neither a LICENSE nor a NOTICE file; the
nuspec instead declares the SPDX expression and NuGet license URL. Anyone
redistributing the tool must supply the license, not assume the nupkg embeds it.
Build environments that convey the package must retain its license and notices;
this document is an admission record, not a substitute for a distributed license.

## Generated Output

Mapperly generates mapping implementations during compilation; generated files
belong under `obj`, not in source control. The authoring inputs and bounded DTO
contracts are repository-native. No third-party implementation
source, test, or expressive design was used to author them.

The official introduction establishes the build-time role and absence of a
required Mapperly runtime dependency. The FAQ does not establish a specific
copyright or licensing grant for generated output. This record therefore does
not assert that output is license-free or that the generator's license
automatically becomes the output's license. Review the actual generated scalar
assignments and preserve Apache-2.0 obligations wherever covered upstream
material is conveyed. Any uncertain upstream expression or additional generated
runtime dependency requires renewed review before distribution.

## Subscription Authoring Boundary

`ActorSubscriptionMapper` owns two one-way, statically called projections from
`ActorSubscription`: detail and list. Repository filtering and handler identity
resolution remain the authority for visibility; mapping grants no access.

- Detail has 17 scalar properties; list has 15 and excludes both subscriber
  identifiers. Neither serializes navigation, audit, or nested PII objects.
- The six flattened lookup/actor strings preserve missing navigation as null,
  including missing `TargetActor.Pii`, despite required C# annotations.
- Null and empty strings, identifiers, timestamps, unsubscribe time and
  concurrency stamps retain their existing meanings.
- List projection preserves count and order and creates its own outer list.
  `PaginatedResult` is not redefined as a defensive-copy boundary.
- `RequiredMappingStrategy.Both` is explicit. RMG012, RMG020, RMG037 and RMG038
  are errors in `.editorconfig`; their Mapperly 4.x defaults are warnings.
  Unmapped-member diagnostics do not prove privacy for same-name matches.
- No reverse mapping, aggregate mutation, private-setter access, generic mapper
  service, recursive cloning or authorization/configuration change is introduced.

The exhausted subscription AutoMapper profile was removed with both consumers.
The remaining mapping families have since migrated; no AutoMapper fallback remains.

## Source Register And Independent Design

Sources were accessed on 2026-09-11 for package metadata, legal terms and public
interface facts only:

| Source | Evidence |
|---|---|
| [Pinned NuGet metadata](https://api.nuget.org/v3-flatcontainer/riok.mapperly/4.3.1/riok.mapperly.nuspec) | Version, Apache-2.0 expression, repository revision and dependency group |
| [Pinned package page](https://www.nuget.org/packages/Riok.Mapperly/4.3.1) | Publisher package identity |
| [Pinned LICENSE](https://github.com/riok/mapperly/blob/036698914bd48be888b25322938b565db72ff7ff/LICENSE) | Applicable Apache-2.0 terms |
| [Introduction](https://mapperly.riok.app/docs/intro/) | Build-time generation and runtime role |
| [FAQ](https://mapperly.riok.app/docs/getting-started/faq/) | No explicit generated-output licensing answer located |
| [Mapper configuration](https://mapperly.riok.app/docs/configuration/mapper/) | Required mapping strategy and supported configuration |
| [Version 4 migration](https://mapperly.riok.app/docs/breaking-changes/4-0/) | Strict mapping diagnostic defaults |

The source-free implementation handoff is the approved subscription pilot's
scalar/disclosure contract. Abstraction: replace runtime object projection, not
business behavior. Filtration: DTO names, property types, JSON values and
Mapperly attribute names are constrained by existing contracts and public APIs.
Comparison: the named Application mapper, explicit scalar boundary, nullable
navigation handling and independent serialization tests derive from this
repository's entities and query consumers, not upstream implementation structure.

Independent provenance and privacy reviews inspected the completed mapper,
consumers, tests, generated output and package evidence and approved the pilot
with no remaining findings. The separately captured list Red closes the initial
detail-first assertion gap. This engineering admission is not legal certification.

## Full-Migration Provenance Review

The September 19 review used repository-native mapper inputs, operation
registration/composition code, build/configuration diffs and the existing source
register. No new external implementation source, snippets, tests or assets were
used. The upstream access dates above are inherited admission evidence, not a
claim of fresh web research or a new latest-version check.

AFC/SSO disposition: entity/DTO identities and public interface spellings are
constrained by repository contracts; the static mapper families, explicit inbound
allowlists and closed operation ports follow the application's own capability
boundaries. Microsoft DI and generic decorator mechanics are platform patterns,
not a copied third-party mediator design. There is no vendor-source translation
or runtime compatibility facade. This bounded review does not replace final
privacy/serialization and real-provider assurance.

The Apache-2.0 terms, pinned package identity, notice obligations and cautious
generated-output disposition above still apply. Removal of the old vendor
exceptions does not approve unrelated dependency exceptions or certify container
redistribution. Retain restored graph, audit, license-policy and artifact/SBOM
evidence for the exact final build; old pilot counts cannot close those gates.

## Historical Pilot Verification Evidence

- Application restore passed and changed only its `packages.lock.json`, adding
  the direct package without transitive dependencies.
- NuGet lock content hash (SHA-512/base64):
  `oSE+OVLHyiojrws0QVbQaAT2YKsYbyHqJXPdV7EhGseR/dYgeWS2x1T5qY5I4E+RJkJqMSG+FyZOq4SljWgJWQ==`.
- Restored nupkg SHA-256:
  `77be3205434872d5e96db24dc96540a47fa45753775c442f968dcc1659f5a0a6`.
- The archive contains Roslyn analyzer variants and a compile-time abstractions
  assembly. Its presence alone is not proof of a runtime dependency.
- Untouched upstream Release baseline passed with 0 errors and 2,595 existing
  warnings in 3 minutes 30.74 seconds. Existing
  `DtoMappingSerializationContractTests` passed all 16 tests in 702 ms.
- Dependency policy passed for 475 unique NuGet package/version pairs. No
  Mapperly exception was needed. Existing AutoMapper, MediatR, SQL Server native
  runtime, Visual Studio container tooling, NetArchTest and SonarAnalyzer
  exceptions remain visible; this admission does not waive them.
- Generated output was inspected: two scalar DTO constructions with 17/15
  assignments, no nested graph copying, no entity mutation and no calls to
  Mapperly runtime code. The compiled Application assembly-reference test passed
  without a Mapperly runtime reference.

- Existing-map characterization passed 16 cases. A compiling empty-detail
  projection failed eight scalar/null contracts. A separate list-only test
  failed when `ActorName` deliberately returned null instead of the display
  name (exit 2, 245 ms); the defect was then removed.
- Final `ActorSubscriptionMapperTests`: 18 passed, 0 failed, 271 ms, after
  successful compilation. Tests invoke the actual static projections and query
  handlers with deterministic in-memory read stores; they do not claim real
  database discoverability enforcement.
- Whole-solution Release build passed. Full Application: 2,179 passed, 0 failed.
  Full Architecture: 591 passed, 0 failed, one pre-existing skip for incomplete
  API response metadata. No test was disabled by this pilot.
- `dotnet restore --locked-mode` passed. No DTO, API, OpenAPI or generated-client
  artifact changed; serialization expectations remain the observable contract.
- C# diagnostics and `git diff --check` passed. Existing repository warnings and
  dependency exceptions were not suppressed or repaired by this slice.
