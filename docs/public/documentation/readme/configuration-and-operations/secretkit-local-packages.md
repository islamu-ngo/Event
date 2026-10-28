---
description: Restore Event's vendored SecretKit development packages without a public package feed.
---

# Local SecretKit package bundle

Event includes SecretKit **0.1.0-local.20261024.1** under
`vendor/secretkit/`. These packages are in development, not ready for production,
and may change APIs without backward compatibility. Event's server entrypoints,
runtime resolution and native writes now use these packages through explicit
selected owners. The obsolete Secrets projects are removed. Final integration
assurance is still in progress; this development cutover is not production
readiness or provider certification.

## Git and source distribution

Commit the whole `vendor/secretkit/` directory together with the root NuGet
configuration, ignore rules and Dockerfile changes. Its four `.nupkg` files are
explicitly allowed by `.gitignore` and marked binary in `.gitattributes`.
Git LFS, a machine-specific NuGet feed and a public NuGet upload are unnecessary.
Keep the MIT license, complete third-party notices, configuration and checksums
with all four immutable packages in source distributions and final images.
Third-party copyrights and MIT permission/warranty notices remain applicable
under Event's public license and any separately approved alternative offering;
Event's contributor agreement does not relicense those components.

Verify the bundle from the repository root:

```sh
(cd vendor/secretkit && sha256sum --check packages.sha256)
```

The repository's `NuGet.Config` maps `SecretKit.*` exclusively to
`vendor/secretkit/feed`. Other dependencies come from nuget.org. A normal clone
therefore contains the SecretKit packages, but a first restore still requires
access to external dependencies or an existing approved dependency cache.

## Server-side package boundary

Package availability is separate from foundation qualification and deployment.
The approved foundation requires these selected entries in Event's existing
`Directory.Packages.props`; exact bracketed versions prevent accidental
candidate changes:

```xml
<PackageVersion Include="SecretKit.Core" Version="[0.1.0-local.20261024.1]" />
<PackageVersion Include="SecretKit.Infisical" Version="[0.1.0-local.20261024.1]" />
<PackageVersion Include="SecretKit.Extensions.Configuration" Version="[0.1.0-local.20261024.1]" />
<PackageVersion Include="SecretKit.Extensions.Hosting" Version="[0.1.0-local.20261024.1]" />
```

The shared server-side boundary uses all four packages: Core for owned
material, Infisical for provider access, Configuration for deliberate legacy
configuration export, and Hosting for explicit lifecycle seams. Entry points
explicitly acquire their process owner and start token-only authentication
maintenance before dependent preparation. A project using
central versions references packages without repeating their versions:

```xml
<PackageReference Include="SecretKit.Core" />
<PackageReference Include="SecretKit.Infisical" />
<PackageReference Include="SecretKit.Extensions.Configuration" />
<PackageReference Include="SecretKit.Extensions.Hosting" />
```

The approved graph aligns Refit and Refit.HttpClientFactory at **16.3.0**.
Keep higher compatible Microsoft.Extensions versions when reconciling package
requirements; do not introduce an accidental mixed-major Refit graph.
Never rebuild different bytes under `0.1.0-local.20261024.1`; a replacement
candidate needs a new reviewed version and matching provenance.

When qualifying that graph, restore using Event's root configuration, regenerate
affected lockfiles through normal restore, and verify locked restore. Review
the application's existing dependency versions when adding the references.
Do not hand-edit lockfiles or put provider credentials in a project, package,
Dockerfile or build argument.

### What the approved design means for operators

Process authority and allowed secret roots are selected once from explicit
bootstrap input, before authentication. Required groups are prepared
asynchronously and completely before dependent services start. Optional
capabilities use separate groups so that an intentionally disabled feature
does not become a required startup dependency. A failed selected authority
does not fall back to another source or silently log in again.

A published group is complete within one local process generation; it does not
promise atomic remote reads or provider-wide snapshot isolation. The preparing
owner disposes acquired resources on failure and transfers ownership exactly
once on successful host composition. Export to ordinary .NET strings is
deliberate: disposing protected material cannot erase those strings or copies
held by clients. Only consumers that need strings should receive them.

Acquisition stays server-side. It adds no SDK to browser/WASM or business
contracts and requires no BFF link to backend implementation projects.
The shared hosting boundary does not turn normal database queries into hosted
operations; Persistence uses the shared boundary for explicit migration and
tooling bootstrap, not ordinary database queries.

API, BFF, Combined and migration hosts require their documented consumer
inputs. Combined shares one selected owner rather than opening an independent
BFF session. Data Protection ring/admission preparation finishes before its
synchronous consumers start. Configure the selected authority and per-consumer
lifetime policy using the [secret-authority guide](secrets.md); recovery after
expiry or uncertain renewal is explicit, never an automatic fallback or
relogin.

## Docker behavior

Build from the repository root, as the existing Dockerfiles require:

```sh
docker build -f src/Explore.API/Dockerfile .
docker build -f src/Event.Standalone/Dockerfile .
```

All four application Dockerfiles copy the root NuGet configuration and local
feed before their first restore. Their final stages also retain the complete
bundle, including license and notices, at `/app/vendor/secretkit`. The four
archives occupy approximately 204 KiB in the source tree.

The four build stages share the digest-pinned SDK 10.0.302 selected by
`global.json`. Keep those versions aligned; a moving `10.0` SDK image may
contain a different feature band and fail before restore starts.

Server lockfiles include the declared Linux x64 and arm64 restore targets;
the browser client keeps its WASM target. This declaration does not certify
either image architecture. Container restore stays locked: update dependency
locks through NuGet when changing the declared runtime graph, rather than
disabling locked restore in an image build.

After package references and code are integrated, normal .NET publish will
also include the required runtime assemblies and dependencies. Merely putting
the archives in an image does not load SecretKit into the running application.
Runtime provider credentials must still be injected through Event's approved
secret authority; none are baked into these packages or images.

## Readiness limits

The source candidate passed Linux package installation, relocation and offline
behavior checks. Its four package hashes are retained in this repository.
Those historical package receipts are separate from Event's foundation checks.
The shared acquisition boundary has a passing Release build,
controlled-provider invariant tests, backend/browser Refit checks, fresh-cache
source/hash verification and dependency-license inventory. The architecture
suite's current merged-source run has seven independently reproduced
develop-baseline failures; it is not
reported as clean. Exact commands and evidence are in the technical provenance
guide.

Serving-host activation and old-project retirement are implemented. Final
suite assurance remains open, with newly exposed failures being attributed
before they can be accepted as unrelated baseline defects. Native Infisical
actor/ACL enforcement and fleet recovery remain unqualified. API, BFF,
MigrationService and Standalone Linux x64 images completed actual locked
restore and publish. The October 27, 2026 refresh covered the later routing and
writer corrections. A further October 28, 2026 qualification completed all four
recipes after merging develop. On that source, API, MigrationService and
Standalone exhausted a 4 GiB builder limit during Persistence compilation
and succeeded with a 12 GiB limit; BFF succeeded at 4 GiB. These are measured
local Podman build budgets, not a runtime memory requirement. Locked restore
and compiler checks stayed enabled. The three
framework-dependent images' four native
DLL hashes match the frozen packages, and the retired Secrets assembly
is absent. Standalone's single-file image retains the complete package
bundle with all four archive checksums verified; its stopped-container
inspection also confirmed the Linux x64 executable and absence of loose
retired DLLs. Bundled runtime DLLs were not separately extracted or hashed.
These checks do not
certify credentialed service startup or another architecture.
Standalone's normal container profile uses untrimmed IL;
trimming/Native AOT support must not be inferred from that profile.
Its single-file publish dependencies use a separately generated container
lockfile; ordinary solution builds keep their existing lockfile.

See the [technical provenance and integration boundary](../../../../internal/SECRETKIT_LOCAL_PACKAGES.md)
and [existing secret-authority guide](secrets.md).
