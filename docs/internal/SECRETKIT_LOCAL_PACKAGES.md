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
and subsequent source-project retirement, separately from final implementation,
provider and image qualification.

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

All four build stages use the same digest-pinned SDK **10.0.302** required
by `global.json`. Do not replace that pin with the moving `10.0` SDK tag:
its current feature band may not satisfy the repository's SDK selection.
This alignment does not weaken global SDK resolution or qualify an image
before its actual restore and publish complete.

`src/Directory.Build.props` imports the unchanged repository compiler and
central-package policy, then declares `linux-x64;linux-arm64` for server
restore graphs. The WASM project retains `browser-wasm`. NuGet generates
the resulting runtime-aware locks; image restore remains locked and copies
the complete referenced project set, including `Event.Wire.Contracts`,
before restore. Declaring an RID is not proof that an image was built or
executed on that architecture.

The restore context must also retain real Razor content: the SDK infers
its implicit `Microsoft.AspNetCore.App.Internal.Assets` dependency from
`.razor` items. The BFF and Standalone recipes copy the server `App.razor`
and client `_Imports.razor` before locked restore. Omitting them changes
the dependency graph even when the project and lock files are identical.
The generated-contract helper builds for the SDK host, not the application's
deployment RID; its nested MSBuild call removes inherited runtime, platform
and publish-output properties while retaining the selected artifacts root.

The actual API Linux x64 image restore, build and publish completed on
October 26, 2026 with exit 0 using:

```bash
podman build --pull=never --layers \
  --cpu-period=100000 --cpu-quota=200000 --memory=4g \
  --build-arg TARGETARCH=amd64 --build-arg BUILDPLATFORM=linux/amd64 \
  -f src/Explore.API/Dockerfile \
  -t localhost/islamu-secretkit-api:qualification-20261026 .
```

The built image contains the bundle and all four native SecretKit DLLs,
whose SHA256 values match the frozen package DLLs; `Explore.Secrets.dll`
is absent. The private build log is
`$HOME/.cache/agent-tmp/secretkit-final-api-image-host-tool.log`.
The BFF Linux x64 image also completed locked restore, build and publish
with exit 0 using the same command flags and
`-f src/Explore.Blazor/Dockerfile -t localhost/islamu-secretkit-bff:qualification-20261026`.
Its four DLL hashes likewise match the frozen package DLLs, the full bundle
is present, and the retired Secrets assembly is absent. Its private log is
`$HOME/.cache/agent-tmp/secretkit-final-bff-image-razor.log`.
These receipts cover image construction and assembly provenance, not
credentialed service startup or arm64 execution. MigrationService Linux x64
also completed locked restore and publish with the same resource/architecture
flags, `-f src/Event.MigrationService/Dockerfile` and
`-t localhost/islamu-secretkit-migration:qualification-20261026`.
The bundle, four matching native DLL hashes and retired assembly absence
were verified in that image too; its private log is
`$HOME/.cache/agent-tmp/secretkit-final-migration-image.log`.
Standalone Linux x64 completed its final locked restore and single-file
publish on October 27, 2026 with the same build flags,
`-f src/Event.Standalone/Dockerfile` and
`-t localhost/islamu-secretkit-standalone:qualification-20261027`.
Its private build log is
`$HOME/.cache/agent-tmp/secretkit-final-standalone-image-standard-lock.log`.
Filesystem inspection found the expected `/app/Event.Standalone`
executable and complete local bundle; all four retained archive checksums
passed. The image runs as UID 1654. Unlike the three framework-dependent
images, its runtime assemblies are bundled: archive checksum inspection
does not claim independently extracted runtime DLL hashes or a successful
credentialed service startup.

Standalone's self-contained single-file publish profile has a different
implicit SDK dependency graph from an ordinary build. It uses
`container/packages.lock.json`, generated by NuGet with
`StandaloneContainerPublish=true`; ordinary solution restore retains
`packages.lock.json`. Both restores remain locked in the image recipe.
Do not force these two graphs into a shared lock or disable lock enforcement.
The standard lock basename also keeps the container graph in the repository's
existing dependency-submission and policy inventories.

### Merged-source image qualification (2026-10-28)

The merged integration source at
`fc1d64b42d754c0d9085e04d6e0aa00f6d288fa8` completed actual Linux x64
construction for all four recipes. Local tags are
`localhost/islamu-secretkit-<name>:qualification-merged-20261028`:

| Name | Image ID prefix | Successful builder memory limit |
| --- | --- | --- |
| `api` | `d55f00c005b6` | 12 GiB |
| `bff` | `85421b56acbe` | 4 GiB |
| `migration` | `f2bcd306b59d` | 12 GiB |
| `standalone` | `bf2f809a4c5f` | 12 GiB |

The command retained `--pull=never --layers`,
`--cpu-period=100000 --cpu-quota=200000`,
`--build-arg TARGETARCH=amd64` and
`--build-arg BUILDPLATFORM=linux/amd64`, selecting each existing Dockerfile.
API, MigrationService and Standalone initially exhausted the 4 GiB limit
while Roslyn compiled Persistence. Only those three images were retried
with `--memory=12g`; the passing BFF image was not rebuilt. No compiler,
analyzer or locked-restore policy was relaxed.

Each resulting image reports `amd64` and UID 1654. Network-disabled shell
inspection of API, BFF and MigrationService verified all four retained
archive checksums, the four runtime DLL hashes against the unchanged
frozen packages, and absence of the retired Secrets and Infisical SDK
assemblies. Standalone's chiseled image was inspected through a stopped
container: its executable is stripped ELF64/x86-64, all four retained
archive checksums pass, and its filesystem inventory contains no loose
retired DLLs. Its bundled runtime DLLs were not separately extracted or
hashed.

Private build logs are
`$HOME/.cache/agent-tmp/secretkit-merged-image-bff.log` and
`secretkit-merged-image-12g-{api,migration,standalone}.log` in that same
directory. Inspection logs use `secretkit-merged-image-inspect-<name>.log`.
The stopped diagnostic container was removed after inspection; no serving
or migration entrypoint was invoked. These receipts establish construction
and bounded content inspection only, not credentialed startup, provider
permissions, fleet recovery, arm64, trimming or Native AOT qualification.

The current complete Architecture run executes 694 cases: 687 pass and
seven fail. All seven failures reproduce on untouched develop
`20737e450db0df748b3e5737bcb3405f54c37cd8` with identical complete errors
and first repository frames. The guardrails remain red; historical
foundation results below do not replace this merged-source result.
Complete API and Persistence corpus qualification remains in progress.

The approved foundation pins all four packages to `[0.1.0-local.20261024.1]`
and aligns `Refit` and `Refit.HttpClientFactory` at **16.3.0**. Reconcile actual
Microsoft.Extensions minima with existing pins, keeping higher compatible
versions. Generate locks through restore, never manual edits. The native cutover
has removed the last Event `Infisical.Sdk` user, both obsolete Secrets projects,
their solution entries and SDK license exception. Fresh native solution restore
now succeeds with the retained Infrastructure PKCS 10.0.13 reference. This is
dependency-closure evidence, not final image/provider qualification.

### Integrated shared acquisition boundary

`Explore.ServiceDefaults/Secrets/` owns immutable process authority, exact-root
admission and SDK lifetime/export composition without Domain, Application, EF
or database-driver dependencies. Domain keeps registry/binding and canonical
reference policy; the Infrastructure adapter applies it before
cache/provider access. SecretKit stays out of inner layers, wire DTOs and WASM.
The BFF uses shared hosting without a backend implementation link.
Persistence references ServiceDefaults for explicit migration/tooling acquisition
and bootstrap composition, never ordinary persistence queries.

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

API, BFF, Combined and migration entrypoints now acquire their selected native
owner explicitly. Serving processes start token-only authentication maintenance
before dependent preparation; Combined shares one owner between API and BFF.
Data Protection prepares ring/admission material asynchronously before
synchronous consumers can resolve it. Fresh readiness checks use the same
selected authority with the process's independent admission pin and generation
floor. The obsolete Secrets projects and `Infisical.Sdk` dependency are removed.

This implementation status is not deployment certification. The Linux x64
construction receipts below qualify restore and publish for their recorded
snapshots. The October 27, 2026 refresh covering the later routing and writer
fixes completed all four recipes. The three framework-dependent images match
all four frozen DLLs; Standalone's executable and retained archive bundle were
inspected separately.
Native actor/ACL enforcement, credentialed image startup, other architectures
and fleet recovery remain unqualified. Standalone's normal container profile
uses untrimmed IL; neither trimming nor Native AOT is qualified by that profile.

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

### Integration package proof (2026-10-26)

A fresh locked restore of the actual `Explore.ServiceDefaults` consumer used
the root `NuGet.Config`, a previously absent package cache and isolated
intermediates. No product or test assembly was rebuilt or overwritten.
All four restored `.nupkg.metadata` files identified the worktree-local
`vendor/secretkit/feed` as their source. Each cached archive matched its
vendored original byte for byte and matched the retained SHA-256 inventory.
All four candidate DLLs also matched the dependency copies in the Release
Infrastructure test output.

This establishes the package source and exact candidate bytes used by that
native test output. At this receipt's cutoff, the revision-ordering repair had
passed its 416-case native Secrets gate; BFF fixture corrections and full-suite
attribution remained open. Later bounded host checks and the final-source image
refresh are recorded separately in the execution ledger. None of these receipts
certifies native actor/ACL enforcement or fleet recovery.
