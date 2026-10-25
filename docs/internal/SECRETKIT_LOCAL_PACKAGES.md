# Vendored SecretKit runtime packages

> **Audience:** Contributors | Maintainers | Distribution reviewers
> **Status:** Mixed
> **Owner:** Security
> **Last Verified:** 2026-10-25 (documentation and retained evidence; no new execution)
> **Source Anchors:** `vendor/secretkit/`, `NuGet.Config`, `dev/active/secretkit-adoption/secretkit-adoption-plan.md` Sections 5.1-5.3, `dev/active/secretkit-adoption/secretkit-adoption-tasks.md` task 1.5

Package availability has historical transfer evidence below. The approved,
unused shared-acquisition foundation has separate pending qualification checks;
serving-host adoption is successor work. Neither architectural approval nor
package restore is a runtime acceptance receipt.

## Artifact and distribution decision

Vendor the four unchanged SecretKit runtime packages at
`0.1.0-local.20261024.1`. Do not add Azure, management, database clients or the
native conformance tool. The earlier availability increment changed no Event
PackageReference, central version or lockfile. The approved foundation requires
exact central pins and restore-generated locks, without registering the seam in
a serving host or changing runtime secret-authority behavior.

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

The [dependency admission record](legal/dependencies/secretkit.md) defines package
roles and outbound-license obligations. The
[ownership ADR](adr/ADR-secretkit-event-ownership.md) records the approved design
without claiming implementation qualification or completed project retirement.

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

The approved foundation pins all four packages to `[0.1.0-local.20261024.1]`
and aligns `Refit` and `Refit.HttpClientFactory` at **16.3.0**. Reconcile actual
Microsoft.Extensions minima with existing pins, keeping higher compatible
versions. Generate locks through restore, never manual edits. Remove
`Infisical.Sdk` only after its last Event user migrates.

### Approved shared acquisition boundary; qualification pending

`Explore.ServiceDefaults/Secrets/` owns immutable process authority, exact-root
admission and SDK lifetime/export composition without Domain, Application, EF
or database-driver dependencies. Domain keeps registry/binding and canonical
reference policy; the successor Infrastructure adapter applies it before
cache/provider access. SecretKit stays out of inner layers, wire DTOs and WASM.
The BFF uses shared hosting without a backend implementation link.
Persistence's future ServiceDefaults dependency is only for design-time
bootstrap composition, never ordinary persistence queries.

Await complete required-group preparation before dependent composition.
`SecretSnapshotLoader` treats every explicit selection in one loader as
required; enabled optional capabilities use separate groups. Reject invalid
coordinates and case-insensitive key collisions before local publication.
Authority identity, role, environment and selector come from captured bootstrap
input, not returned provider values. No fallback authority, hidden relogin,
temporary container or network work in synchronous configuration Load/DI
factories is permitted. Explicit build/model-only lanes remain no-I/O.

A complete published generation is a local admission guarantee, not atomic
remote reads or provider snapshot isolation. Export through
`SecretConfigurationExport.CreateAcknowledgingManagedStrings` only for consumers
that require configuration/Options strings. Controlled secret keys cannot be
overridden by later ordinary providers; non-secret precedence is unchanged.
Synchronous Build/Load accesses prepared memory.

Preparation owns its lease/session/owned transport until successful host
handoff exactly once; preparation, cancellation and host-construction failures
dispose that ownership. Caller-owned dependencies remain caller-owned. Dispose
held reads independently and retire asynchronously outside locks after accepted
work drains. Disposal of SDK material does not erase exported managed strings
or downstream copies; minimize export and bound retention explicitly.

The foundation registers no serving-host acquisition or Hosting maintenance and
changes no scheduler rule. The successor must qualify host wiring, close the
preparation-to-host-start lifetime gap and migrate real consumers before
retiring old Secrets projects. No such deletion is claimed here. Actual image
publication and Standalone trimming/Native AOT remain unqualified, as do native
actor/ACL behavior and fleet recovery.

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

These are historical availability receipts from 2026-10-24, not checks rerun
for the foundation documentation.

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
were run for that availability increment, which changed no product code,
PackageReference or dependency lockfile.

### Foundation qualification evidence

On 2026-10-25, the affected Infrastructure test project built successfully in
Release. The 25 selected authority tests passed, including protocol-level login,
independent required groups, finite/periodic lifetime admission, cancellation,
retirement, protected destinations, and value/coordinate/prose canaries. Five
existing backend analytics edge cases exercised real Refit request construction;
all 3,219 browser-client tests also passed against the aligned Refit graph.

A fresh locked restore of the actual ServiceDefaults project used the root
NuGet.Config with isolated intermediates and a previously absent package cache.
Each of the four restored `.nupkg.metadata` files named the worktree-local feed,
and all archive SHA-256 values matched the vendored inventory. Runtime test
assemblies matched the inspected Core and Infisical candidate DLL hashes.
The actual dependency-license gate passed for 485 package/version pairs; the
older 753-pair receipt is historical, not post-adoption assurance.

The architecture suite ran 671 tests with four failures. The identical four
failures reproduced on clean `e8aa58ae8f`: persistence DTO dependencies, TMS
retry-handler convention, legacy decentralization schema retirement, and
repository naming. They are quarantined baseline defects, not a passing gate
or permission to weaken tests. Existing compiler/analyzer warnings were not
suppressed.

Serving-host/runtime, native Infisical actor/ACL, supported image builds and
trim/AOT qualification remain separate successor obligations. Package delivery
and local publication semantics cannot certify those deployment properties.
