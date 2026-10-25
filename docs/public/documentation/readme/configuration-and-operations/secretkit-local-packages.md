---
description: Restore Event's vendored SecretKit development packages without a public package feed.
---

# Local SecretKit package bundle

Event includes SecretKit **0.1.0-local.20261024.1** under
`vendor/secretkit/`. These packages are in development, not ready for production,
and may change APIs without backward compatibility. They support an approved
shared-acquisition foundation, not a serving-host cutover. Foundation
qualification checks remain pending; Event's existing secrets implementation is
not declared retired or replaced.

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

## Approved foundation package boundary

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

The shared server-side foundation uses all four packages: Core for owned
material, Infisical for provider access, Configuration for deliberate legacy
configuration export, and Hosting for explicit lifecycle seams. Hosting being
available does not enable a service or background maintenance. A project using
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
operations; any future database-tooling dependency is design-time bootstrap
only.

These are architectural commitments, not instructions to enable a new runtime
path. The foundation registers no serving host or background authentication
maintenance. Runtime activation, recovery and retirement of the existing
implementation require the separately qualified successor.

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

After package references and code are integrated, normal .NET publish will
also include the required runtime assemblies and dependencies. Merely putting
the archives in an image does not load SecretKit into the running application.
Runtime provider credentials must still be injected through Event's approved
secret authority; none are baked into these packages or images.

## Readiness limits

The source candidate passed Linux package installation, relocation and offline
behavior checks. Its four package hashes are retained in this repository.
Those historical package receipts are separate from Event's foundation checks.
The unused shared acquisition boundary now has a passing Release build,
controlled-provider invariant tests, backend/browser Refit checks, fresh-cache
source/hash verification and dependency-license inventory. The architecture
suite still has four independently reproduced baseline failures; it is not
reported as clean. Exact commands and evidence are in the technical provenance
guide.

Serving-host activation, native Infisical actor/ACL enforcement and actual
supported image builds remain separate successor work. No old Secrets project
deletion or runtime qualification is claimed. The selected SecretKit
trimming/Native AOT profile is not qualified; do not assume Event's publish mode
is supported without testing it.

See the [technical provenance and integration boundary](../../../../internal/SECRETKIT_LOCAL_PACKAGES.md)
and [existing secret-authority guide](secrets.md).
