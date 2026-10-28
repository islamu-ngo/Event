---
description: Restore Event's vendored SecretKit development packages without a public package feed.
---

# Local SecretKit package bundle

Event includes SecretKit **0.1.0-local.20261024.1** under
`vendor/secretkit/`. These packages are in development, not ready for production,
and may change APIs without backward compatibility. They prepare the next
secrets refactor; Event still uses its current secrets implementation.

## Git and source distribution

Commit the whole `vendor/secretkit/` directory together with the root NuGet
configuration, ignore rules and Dockerfile changes. Its four `.nupkg` files are
explicitly allowed by `.gitignore` and marked binary in `.gitattributes`.
Git LFS, a machine-specific NuGet feed and a public NuGet upload are unnecessary.
Keep the MIT license, notices, configuration and checksums with the packages.

Verify the bundle from the repository root:

```sh
(cd vendor/secretkit && sha256sum --check packages.sha256)
```

The repository's `NuGet.Config` maps `SecretKit.*` exclusively to
`vendor/secretkit/feed`. Other dependencies come from nuget.org. A normal clone
therefore contains the SecretKit packages, but a first restore still requires
access to external dependencies or an existing approved dependency cache.

## Starting the integration refactor

This task adds package availability, not runtime integration. The refactor
should add the selected entries to Event's existing
`Directory.Packages.props`; exact bracketed versions prevent accidental
candidate changes:

```xml
<PackageVersion Include="SecretKit.Core" Version="[0.1.0-local.20261024.1]" />
<PackageVersion Include="SecretKit.Infisical" Version="[0.1.0-local.20261024.1]" />
<PackageVersion Include="SecretKit.Extensions.Configuration" Version="[0.1.0-local.20261024.1]" />
<PackageVersion Include="SecretKit.Extensions.Hosting" Version="[0.1.0-local.20261024.1]" />
```

Reference only the packages needed by each server-side owner. Core and
Infisical provide the minimal path; Configuration and Hosting are independently
optional. With central versions configured, a consuming project uses:

```xml
<PackageReference Include="SecretKit.Core" />
<PackageReference Include="SecretKit.Infisical" />
```

Then restore that project using Event's root configuration, regenerate its
affected lockfiles through normal restore, and verify locked restore. Review
the application's existing dependency versions when adding the references.
Do not hand-edit lockfiles or put provider credentials in a project, package,
Dockerfile or build argument.

Keep credentials and typed Infisical access in their application-owned
server-side boundary. Collect and validate required material before constructing
dependent clients; export strings only where a consumer needs them. Renewal,
recovery and replacing a live client remain explicit lifecycle operations.
Vendoring does not authorize a new fallback authority or start background work.

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

After the refactor adds PackageReferences and code, normal .NET publish will
also include the required runtime assemblies and dependencies. Merely putting
the archives in an image does not load SecretKit into the running application.
Runtime provider credentials must still be injected through Event's approved
secret authority; none are baked into these packages or images.

## Readiness limits

The source candidate passed Linux package installation, relocation and offline
behavior checks. Its four package hashes are retained in this repository.
Native Infisical deployment qualification and Event's actual integration remain
separate work. The selected SecretKit trimming/Native AOT profile is not
qualified; do not assume Event's publish mode is supported without testing it.

See the [technical provenance and integration boundary](../../../../internal/SECRETKIT_LOCAL_PACKAGES.md)
and [existing secret-authority guide](secrets.md).
