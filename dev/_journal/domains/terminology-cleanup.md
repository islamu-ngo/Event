# Terminology Precision

**Last Updated:** 2026-09-29 Europe/Brussels
**Status:** Implemented

## Decision

Repository-owned names must describe their concrete role rather than rely on broad prestige modifiers. Choose terms such as `target`, `primary`, `authoritative`, `normalized`, `stable`, `stored`, or `preferred` only when that meaning is true for the specific contract.

## Applied Model

- Actor consolidation uses `SourceActorId` and `TargetActorId`.
- Registration synchronization uses `FullSync` and `FULL_SYNC`.
- JSON, text, digests, schema bundles, and payloads use normalization terminology when they are transformed into deterministic forms.
- Release artifacts use `ReleaseArtifactPolicy`, `ArtifactPolicyResult`, `NormalizeJson`, and `NormalizeText`.
- Setup metadata uses `PlatformEnvironmentCatalogue` and `PlatformEnvironmentMetadata`.
- Public URL construction uses `AbsoluteUrlBuilder`.
- HATEOAS uses the `Primary` link relation where the server identifies the preferred resource.

## Persistence Consequence

The platform is pre-release and has no external adopters, so the four application migration histories were regenerated through EF Core tooling as clean provider-specific `Init` migrations. PostgreSQL, SQLite, SQL Server, and MySQL now materialize `actor_merges.target_actor_id` directly. Independent External Identity, Data Protection, and privacy-erasure authority histories were not reset.

Existing development databases must be recreated. No compatibility aliases, dual claims, duplicate columns, or migration shims were introduced.

## External Standard Tokens

Repository-owned vocabulary was removed without altering standards-defined behavior:

- HTML keeps `rel="canonical"` because that literal is defined by the web platform and drives search-engine URL selection.
- DAG-CBOR keeps `CborConformanceMode.Canonical` because that enum member is defined by the .NET framework and enforces protocol conformance.

These literals are external protocol contracts, not project terminology.

## Verification Contract

Completion requires:

1. Zero case-insensitive matches in project-owned source, tests, documentation, scripts, schemas, and filenames.
2. Only the two standards-defined exceptions above.
3. Generated OpenAPI, client, inventory, setup, and EF artifacts produced by their owning tools.
4. Release builds, focused security/domain tests, release tooling tests, architecture guardrails, and provider migration checks passing.
