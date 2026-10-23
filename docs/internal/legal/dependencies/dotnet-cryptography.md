# .NET cryptography dependency selection

## Decision and scope

Select Microsoft's `System.Security.Cryptography.Pkcs` and
`System.Security.Cryptography.Xml` packages at `10.0.13`. These are existing
transitive runtime components, now explicitly referenced by their Event owners:
PKCS in `Explore.Secrets` and XML cryptography in `Explore.Persistence`.
Central version entries alone do not override every transitive dependency;
the project references make the selected versions effective in those graphs.
This decision does not certify unrelated application or browser packages.

An isolated persistence restore selected older PKCS versions (`9.0.4`,
`10.0.0`, `10.0.10`) and XML `10.0.10`. NuGet rejected them with `NU1902`
and `NU1903`. The correction changes dependency resolution, not Event's
cryptographic algorithms, key storage or authority contracts. Audit and build
checks must remain enabled; no warning suppression is part of this selection.

## Provenance and distribution

Both selected package manifests identify Microsoft as author, version `10.0.13`,
and license expression `MIT`. Package metadata identifies
`https://github.com/dotnet/dotnet` revision
`4099f83977f4d29604a9aeb971fb0a7bc1d5f699`; no implementation source was
retrieved or copied. The manifests were read from the locally restored NuGet
packages. XML's `net10.0` dependency is PKCS `10.0.13`; PKCS declares no
additional package dependencies for that target.

MIT permits these components in the public AGPL distribution and alternative
ISLAMU-owned licensing paths without imposing their license on Event's own
code. Distributions must retain the components' copyright and permission
notices and applicable packaged notices. This selection grants no Microsoft
trademark rights and does not replace review of the assembled distribution.
Keep the repository's dependency-license policy validation as a separate gate.

## Verification boundary

Record exact restore, build, test and license-policy receipts in the programme
assurance ledger. A successful build with stale restore assets is not evidence
that a fresh dependency audit accepts the selected graph.
