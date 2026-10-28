# Vendored SecretKit runtime packages

> **Audience:** Maintainers and refactor authors
> **Status:** Package availability implemented; runtime integration pending
> **Verified candidate date:** 2026-10-24
> **Owners:** `vendor/secretkit/`, `NuGet.Config`, application Dockerfiles

## Artifact and distribution decision

Vendor the four unchanged SecretKit runtime packages at
`0.1.0-local.20261024.1`. Do not add Azure, management, database clients or the
native conformance tool. No Event PackageReference, central package version,
lockfile or runtime secret-authority behavior changes in this increment.

SecretKit is MIT, copyright 2026 ISLAMU. Its license permits redistribution with
the copyright and permission notice retained. The vendored `LICENSE` preserves
that text; `THIRD-PARTY-NOTICES.md` is extracted unchanged from the shipped Core
archive. All four archives also retain their embedded README and notices.
The reviewed selected runtime dependency closure is MIT: Refit 16.3.0,
ReactiveUI.Primitives, ReactiveUI.Primitives.Core and ReactiveUI.Disposables
8.2.0, and the selected Microsoft.Extensions packages 10.0.12. Their individual
copyrights and notice obligations remain in force.

This is an approved local binary dependency distribution, not copied vendor
implementation or a claim that Event's CLA relicenses third-party material.
Retain these terms in public Git/source distributions and container images,
including any separately approved alternative Event offering. The existing
license-policy scanner still applies when the refactor adds actual package
references and lockfile entries. Vendored packages alone are outside that
scanner's lockfile-derived inventory.

## Exact provenance

Source repository identity: `islamu-ngo/SecretKit`.
Source commit: `e635985239a3146228f1bb75d65a3f13c84a292c`, plus its explicitly
qualified working-tree content; the commit alone does not identify these bytes.
The local producer artifact is
`artifacts/runtime-adoption-hardening/candidate-r3/handoff/`.

| Identity | SHA-256 |
| --- | --- |
| Source-content-v1 | d65bc880e8dcc73516dc2a67557246ec632568b4bb6e14d4130ff296e77236e5 |
| Local qualification record | 35fb09d806556b17119c8c5e1b13e3ff53cb603ff31ca7e382ca2f914f705bcd |
| Local profile | f6852109621539a12ea29c328c077338b20d6198ca974df45c8cf7d4d25537b2 |
| SecretKit.Core | 2ba34bf144ae7e0016ba9a4140a9941b9a5e380b62ff19614a9e7ea8acfa8a35 |
| SecretKit.Infisical | f68538a334acd7d7b345d5c2192b13ae20e653da8b29197946addbea0aab2374 |
| SecretKit.Extensions.Configuration | fe487c4136c034ba10ad69e01a5b049067256e3389294c65bf37b8950a063ca4 |
| SecretKit.Extensions.Hosting | e2628da535c9f610566ac72582eaf0e0df92f163a66e45ea1f24221aaa8ffea0 |

The producer's final Linux run passed 4,460 solution cases and 183 separate
offline conformance cases, all four installed modes and relocated feed tests.
Current source/original/record consistency and bundle checksums were also
verified before transfer. These are producer/offline results, not Event runtime
acceptance, native actor/ACL proof or production approval.

## Restore and image flow

Root NuGet configuration selects the repository-relative feed for `SecretKit.*`.
NuGet's more-specific prefix mapping takes precedence over nuget.org's wildcard.
The standalone bundle configuration clears inherited sources and mappings and
uses its own relative `feed/`. Neither configuration contains a developer path.
External dependencies still require nuget.org or an approved dependency cache.

The API, Blazor server, Standalone and MigrationService Dockerfiles copy
NuGet.Config and the bundle into `/src` before restore. Their final stages
copy the entire directory to `/app/vendor/secretkit`, preserving raw archives,
checksums, license and notices. Narrow `.dockerignore` exceptions retain the
bundle even when root README/license exclusions would otherwise apply.
No provider input or credential is part of this build flow.

The following refactor must add package versions/references, adapt Event-owned
credential/bootstrap/client lifecycle code and regenerate affected locks.
It must test actual Docker publication, particularly Standalone trimming/AOT:
the producer's selected trim/AOT profile remains unqualified. Preserve explicit
required/optional admission, no automatic fallback or hidden re-login, scoped
consumer authority and application-owned activation/retirement.

## Replacement and verification

Replace all four archives, inventory and provenance together with a new reviewed
candidate; never rebuild different bytes under the same immutable version.
Use `sha256sum --check packages.sha256` from the bundle directory and a fresh
cache restore using Event's root NuGet.Config. Validate the actual
`.nupkg.metadata` source and restored archive hashes, not just a successful
restore exit code. Keep package credentials out of Git and image layers.

The [public adopter guide](../public/documentation/readme/configuration-and-operations/secretkit-local-packages.md)
describes Git inclusion, central-version references and Docker usage.

### Event-side transfer verification

On 2026-10-24, all four vendored archive checksums matched the producer inventory.
An isolated net10.0 console consumer with all four exact PackageReferences
restored using Event's root NuGet.Config into a previously absent package
cache, repeated restore with `--locked-mode`, built in Release and loaded all
four assemblies successfully. Each restored `.nupkg.metadata` identified
Event's `vendor/secretkit/feed` as its source; cached archive SHA-256 values
matched the vendored originals. This checks package availability, not Event's
future runtime integration.

`dotnet run .ci/scripts/validate-dependency-license-policy.cs -- .` passed for
753 existing package/version pairs. Its existing SqlClient native-runtime,
Visual Studio container tooling, NetArchTest metadata and Sonar analyzer
exceptions remain visible and unchanged; the scanner does not inventory the
four vendored packages until they enter a product lockfile.

Docker image execution was not performed: both the selected Docker Desktop
socket and the native default daemon socket were unavailable. The copy stages
and narrow context-inclusion exceptions are implemented, but a real image
build remains an integration check. No full Event application build or tests
were run because this increment changes no product code, PackageReference or
dependency lockfile.
