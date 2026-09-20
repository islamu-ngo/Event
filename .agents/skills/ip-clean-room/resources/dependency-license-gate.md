<!-- ABOUTME: Dependency-license decision gate protecting all intended ISLAMU outbound distribution paths. -->
<!-- ABOUTME: Separates automated metadata checks from legal and commercial distribution approval. -->

# Dependency License Gate

## Required Record

For each added or changed dependency, record:

- component, version, source, and checksum/lock evidence;
- direct/transitive and runtime/build/test/asset/optional-service role;
- authoritative license expression or contract;
- obligations for public AGPL and each intended alternative offering;
- notices, source, patent, trademark, redistribution, hosting, seat, field-of-use, and sublicensing constraints;
- decision and approver.

## Decision

- **Approve:** the assembled offering can lawfully follow every intended outbound model while the third-party component retains its own terms.
- **Replace/version-pin:** a compatible version or dependency provides the required function.
- **Separate-license review:** documented rights cover every intended build, deploy, and distribution; default/community behavior remains explicit.
- **Block:** terms affect ISLAMU-owned material or prohibit an intended outbound model, or authority is unclear.

Passing `.ci/scripts/validate-dependency-license-policy.cs` is mandatory metadata evidence, not legal certification. Unknown metadata, source-available terms, commercial contracts, scanner overrides, assets, datasets, and generated output require human review.

## Single Supported Dependency Graph

`Directory.Packages.props` pins one supported graph. AutoMapper, MediatR and
MediatR.Contracts, their edition selectors, commercial version overrides and
vendor license inputs are retired. CI and the API/Blazor Dockerfiles enforce
locked restore; there is no commercial restore bypass.

[Mapperly provenance](../../../../docs/internal/legal/dependencies/mapperly.md)
records the Apache-2.0 build-time generator, notice obligations and limits of
generated-output claims. Native operations use repository-owned contracts and
Microsoft DI, not a substitute mediator package. Every new dependency must
independently pass this gate; neither the old edition arrangement nor the CLA
authorizes another vendor's terms.

## Verification

```bash
dotnet run .ci/scripts/validate-dependency-license-policy.cs -- .
dotnet restore --locked-mode
```

Also inspect the actual artifact/SBOM for each distributed runtime and topology.
One build graph does not waive third-party terms or prove container contents;
record the exact artifact identity and keep unrelated exceptions visible.
