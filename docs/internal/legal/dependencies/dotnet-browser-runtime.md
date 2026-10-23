# .NET browser runtime dependency selection

## Resolution boundary

Select Microsoft's Components.Web, Components.WebAssembly and
Components.WebAssembly.Server at `10.0.13`. The server package requires the
matching WebAssembly package, which requires the matching Web components.
The associated central Configuration.Abstractions, DependencyInjection,
DependencyInjection.Abstractions and Logging.Abstractions entries follow that
dependency closure at `10.0.13`.

The combined `Event.Standalone` host explicitly selects SignalR.Client `8.0.32`
to replace the older transitive SignalR graph brought by its dashboard.
This selects a patched dependency, not a new application transport or endpoint.
No vulnerability-audit suppression is permitted.

An isolated API integration-project restore rejected WebAssembly.Server
`10.0.10` under GHSA-g27p-qf6j-c96c and GHSA-qjpx-jqm5-h2c4, and
SignalR.Common `8.0.8` under GHSA-334f-vvp4-7g6p.
Fresh restore and build evidence must establish the effective graph; a central
version declaration by itself does not override every transitive reference.

## Provenance and obligations

Locally restored package manifests identify Microsoft as author and `MIT` as
the license expression for all three selected Components packages and
SignalR.Client. The Components packages identify dotnet/dotnet revision
`4099f83977f4d29604a9aeb971fb0a7bc1d5f699`; SignalR.Client identifies
dotnet/aspnetcore revision `7413d24330774263a78a4f3837ca53493518455a`.
Only package metadata was inspected; no third-party implementation source was
retrieved or copied.

MIT permits inclusion in Event's public AGPL distribution and alternative
ISLAMU-owned licensing paths without extending MIT to Event's own code.
Retain Microsoft copyright and permission notices and applicable packaged
notices when distributing these components. No trademark rights are granted.
The repository license-policy validator remains a separate assembled-graph
check, not a substitute for distribution obligations.

## Verification

Record exact build, audit, test and license-policy outcomes in the programme
assurance ledger. This decision does not certify an unexecuted browser flow
or unrelated dependencies.
