# SecretKit local development packages

Version: **0.1.0-local.20261024.1**. In development, not ready for production.
APIs may change without backward compatibility.

This directory is a portable, Git-trackable dependency bundle for the upcoming
Event secrets refactor. It contains four unchanged packages in `feed/`, their
SHA-256 inventory, a standalone NuGet configuration, the SecretKit MIT license,
and notices extracted from the shipped Core package.

From this directory:

```sh
sha256sum --check packages.sha256
```

Event's root `NuGet.Config` resolves `SecretKit.*` only from this feed.
Other packages still resolve from nuget.org: this is not a fully offline
dependency cache. To use the bundle outside Event, pass this directory's
`NuGet.Config` explicitly to `dotnet restore`.

The API, Blazor server, Standalone and MigrationService Dockerfiles copy the
bundle before restore and retain it at `/app/vendor/secretkit` in the final
image. No provider credential or runtime configuration is included.

Vendoring does not replace Event's existing secrets implementation or add any
PackageReference. See the [adopter guide](../../docs/public/documentation/readme/configuration-and-operations/secretkit-local-packages.md)
for integration steps and the [technical record](../../docs/internal/SECRETKIT_LOCAL_PACKAGES.md)
for exact provenance, package hashes and distribution boundaries.
