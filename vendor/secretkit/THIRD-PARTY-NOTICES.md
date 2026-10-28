# Dependency provenance

SecretKit is independently authored under the repository MIT license. No vendor,
Event or Configurator implementation source or expressive schema is incorporated.
Public protocol identifiers are retained only where interoperability requires them.

Direct packages are distributed through nuget.org; the candidate package metadata
checked on 2026-10-09 declares MIT for TUnit 1.73.19, Azure.Security.KeyVault.Secrets
4.11.2, Azure.Core 1.63.0 and Microsoft.Extensions packages 10.0.12.
Their copyrights and licenses remain with their respective contributors.

Core has no external dependency. Azure packages are optional provider dependencies;
Microsoft.Extensions packages are optional composition dependencies. TUnit is
test-only and is not distributed as a runtime dependency.

Restore-generated lockfiles identify exact direct/transitive versions and
content hashes. Resolved dependency notices and vulnerability evidence are a
required gate before distribution, not proved by this direct-metadata record.
No copied third-party source is included in these packages.

Infisical access uses independently handwritten typed native HTTP contracts and
Refit 16.3.0. No production, test or assessment project depends on the official
Infisical .NET SDK. The prior SDK-dependent assessment projects were retired.
Refit's official NuGet nuspec was inspected on 2026-10-09: MIT, source repository
https://github.com/reactiveui/refit at commit
053873c309b518d21aa128c246ff8c8b2e38bed4. Refit generates HTTP bindings for the
handwritten interfaces, not public models from a vendor OpenAPI schema.
Core retains no Refit dependency. Exact restored runtime/analyzer transitives
and package content hashes belong to their generated locks and release gate.

Optional Azure administration uses Azure.ResourceManager.KeyVault 1.4.0 and
Azure.Monitor.Query 1.7.1. Their NuGet nuspec metadata was inspected on 2026-10-09:
both declare MIT and the Azure SDK repository. Recorded source commits are
75a7ef8ab6eae983be2334991e4584a4f5f91d71 and
36824c3ca78ab202640254522dd2bf309a18ac2e respectively. These dependencies belong
only to the management package, not Core, common reads or application bootstrap.
Their transitive graph and distribution evidence remain part of the release gate.

Remaining admitted Azure RBAC and notification families select optional
Azure.ResourceManager.Authorization 1.1.7 and Azure.ResourceManager.EventGrid
1.1.0. Their official NuGet nuspecs declare MIT and Azure SDK source commits
f1ce8e8a389e35c76d1c108ec29b06e58ad7960c and
707f264e1c541be0f5af8d9215f00ed5eabb5173, inspected on 2026-10-09. They remain
management-only dependencies; selection does not establish callable family
coverage, permission or live conformance.

## Four-package local runtime closure

### Generic Host worker application closure

The nonpackable `samples/Infisical.WorkerDemo` separately pins
Microsoft.Extensions.Hosting 10.0.12. Reviewed on 2026-10-24 against its
tool-generated `packages.lock.json`, restored nuspecs, `.nupkg.metadata` source
and matching content hashes: the `net10.0` app has three SecretKit packages
(Core, Infisical, Hosting) and 33 external packages. This does not add a
dependency to a shipped SDK library or weaken minimal/configuration/hosting/full
installed-profile witnesses. DemoProcess checks a separate exact worker graph.

Beyond the four-package closure recorded below, the app adds the following
19 exact packages, all version **10.0.12**, all declaring **MIT**:

- Microsoft.Extensions.Hosting.
- Microsoft.Extensions.Configuration.Binder, .CommandLine,
  .EnvironmentVariables, .FileExtensions, .Json and .UserSecrets.
- Microsoft.Extensions.DependencyInjection and Microsoft.Extensions.Diagnostics.
- Microsoft.Extensions.FileProviders.Physical and Microsoft.Extensions.FileSystemGlobbing.
- Microsoft.Extensions.Logging, .Configuration, .Console, .Debug, .EventLog
  and .EventSource.
- Microsoft.Extensions.Options.ConfigurationExtensions.
- System.Diagnostics.EventLog.

Their nuspecs identify Microsoft Corporation as copyright owner and
`dotnet/dotnet` revision `95017c711e6afc1085133d440e42b4bd78155701`;
installed metadata records nuget.org. Retain the Microsoft attribution and
complete MIT permission/warranty text below for these components as well.
Every added package's `THIRD-PARTY-NOTICES.TXT` has the existing .NET Runtime
payload hash `6d15e10a101c6bfff2ab4429ed061bf76c456fc4b23ad6b03e0d0f8377148a21`.
The retained HealthChecks.Abstractions dependency still carries its distinct
ASP.NET notice payload. Preserve both full legal payloads and original archives
when distributing the app or its local dependency feed. Transitive logger and
configuration assemblies being installed does not mean their providers are
enabled: the worker uses a bare HostBuilder and clears logger providers.

This is metadata/license/notice admission for local evaluation, not advisory
clearance, signature verification or permission to publish. The remaining
distribution checks below still apply.

### SDK closure evidence

Reviewed on 2026-10-24 for `net10.0` only: `SecretKit.Core`,
`SecretKit.Infisical`, `SecretKit.Extensions.Configuration` and
`SecretKit.Extensions.Hosting`. Evidence is their checked-in
`src/<project>/packages.lock.json`, existing `obj/project.assets.json` runtime
selections, and installed NuGet `.nuspec`, `.nupkg.metadata`, license and notice
files. Installed metadata records nuget.org as the source; its `contentHash`
values match the corresponding locks. No dependency was upgraded or lock edited.
This is a metadata/notice review, not fresh restore, signature verification,
vulnerability clearance or inspection of a newly packed SecretKit archive.

All 14 external packages below declare MIT. The distribution decision is to
retain these exact dependencies under their own terms for local evaluation,
carrying their copyright, permission and warranty notices. SecretKit's
first-party MIT license does not replace those notices.

| Exact package | Role in the selected runtime closure | Retained legal evidence |
| --- | --- | --- |
| Refit 16.3.0 | Direct Infisical dependency; also reached through Hosting | `LICENSE/LICENSE`, including original GitHub attribution |
| ReactiveUI.Primitives 8.2.0 | Refit transitive | `LICENSE/LICENSE` |
| ReactiveUI.Primitives.Core 8.2.0 | ReactiveUI.Primitives transitive | `LICENSE/LICENSE` |
| ReactiveUI.Disposables 8.2.0 | ReactiveUI.Primitives and Primitives.Core transitive | `LICENSE/LICENSE` |
| Microsoft.Extensions.Configuration 10.0.12 | Direct Configuration dependency | MIT expression and runtime notice payload below |
| Microsoft.Extensions.Configuration.Abstractions 10.0.12 | Configuration and Hosting transitive | MIT expression and runtime notice payload below |
| Microsoft.Extensions.Primitives 10.0.12 | Configuration and Hosting transitive | MIT expression and runtime notice payload below |
| Microsoft.Extensions.Hosting.Abstractions 10.0.12 | Direct Hosting dependency | MIT expression and runtime notice payload below |
| Microsoft.Extensions.Diagnostics.HealthChecks.Abstractions 10.0.12 | Direct Hosting dependency | MIT expression and distinct ASP.NET notice payload below |
| Microsoft.Extensions.DependencyInjection.Abstractions 10.0.12 | Hosting transitive | MIT expression and runtime notice payload below |
| Microsoft.Extensions.Diagnostics.Abstractions 10.0.12 | Hosting transitive | MIT expression and runtime notice payload below |
| Microsoft.Extensions.FileProviders.Abstractions 10.0.12 | Hosting transitive | MIT expression and runtime notice payload below |
| Microsoft.Extensions.Logging.Abstractions 10.0.12 | Hosting transitive | MIT expression and runtime notice payload below |
| Microsoft.Extensions.Options 10.0.12 | Hosting transitive through Diagnostics.Abstractions | MIT expression and runtime notice payload below |

Core's selected graph has no external packages. Configuration references Core,
not Infisical. Hosting references Core and Infisical, so its selected closure
also includes Refit and all three ReactiveUI packages. Other-framework
dependencies in the upstream nuspecs are not additions to this `net10.0` graph.
Azure, management, test tooling, and the .NET runtime itself are outside this
four-package dependency decision; their separate distribution gates remain.

### Exact-version source identities

The installed nuspecs identify these upstream repositories and revisions:

- Refit 16.3.0: `reactiveui/refit`,
  `053873c309b518d21aa128c246ff8c8b2e38bed4`.
- ReactiveUI.Primitives, ReactiveUI.Primitives.Core and ReactiveUI.Disposables
  8.2.0: `reactiveui/Primitives`,
  `2a8b66d0a18ce590e2551e9fcafbaf55b34f40e5`.
- All ten Microsoft.Extensions packages 10.0.12 above: `dotnet/dotnet`,
  `95017c711e6afc1085133d440e42b4bd78155701`.

These are package metadata identities, not evidence that vendor implementation
was read or incorporated into SecretKit. Only dependency metadata and legal
files were inspected for this update.

### Copyright and MIT permission notice

Retain the following attributions with the MIT permission and warranty text
below when distributing the corresponding components or substantial portions:

- Refit 16.3.0: Copyright (c) ReactiveUI and contributors 2012 - 2026.
  All rights reserved.
- Original Refit attribution: Copyright (c) 2012 GitHub.
  Original author: Anna Betts, at GitHub in 2012. The current license retains
  this original attribution; the current maintainer notice does not replace it.
- ReactiveUI.Primitives, ReactiveUI.Primitives.Core and ReactiveUI.Disposables
  8.2.0: Copyright (c) 2019-2026 ReactiveUI Association Incorporated.
- Microsoft.Extensions packages 10.0.12 listed above:
  Copyright (c) Microsoft Corporation. All rights reserved.

The following MIT text applies to each of those attributions:

```text
Permission is hereby granted, free of charge, to any person obtaining a copy
of this software and associated documentation files (the "Software"), to deal
in the Software without restriction, including without limitation the rights
to use, copy, modify, merge, publish, distribute, sublicense, and/or sell
copies of the Software, and to permit persons to whom the Software is
furnished to do so, subject to the following conditions:

The above copyright notice and this permission notice shall be included in all
copies or substantial portions of the Software.

THE SOFTWARE IS PROVIDED "AS IS", WITHOUT WARRANTY OF ANY KIND, EXPRESS OR
IMPLIED, INCLUDING BUT NOT LIMITED TO THE WARRANTIES OF MERCHANTABILITY,
FITNESS FOR A PARTICULAR PURPOSE AND NONINFRINGEMENT. IN NO EVENT SHALL THE
AUTHORS OR COPYRIGHT HOLDERS BE LIABLE FOR ANY CLAIM, DAMAGES OR OTHER
LIABILITY, WHETHER IN AN ACTION OF CONTRACT, TORT OR OTHERWISE, ARISING FROM,
OUT OF OR IN CONNECTION WITH THE SOFTWARE OR THE USE OR OTHER DEALINGS IN THE
SOFTWARE.
```

### Microsoft bundled notice retention

Each listed Microsoft package contains `THIRD-PARTY-NOTICES.TXT`. The nine
packages other than HealthChecks.Abstractions carry identical .NET Runtime
notice bytes; HealthChecks.Abstractions carries a different ASP.NET notice
payload. SHA-256 identities of the installed files are:

| Notice payload | SHA-256 |
| --- | --- |
| .NET Runtime, nine packages | `6d15e10a101c6bfff2ab4429ed061bf76c456fc4b23ad6b03e0d0f8377148a21` |
| ASP.NET, HealthChecks.Abstractions | `307d014f65d8482314f1400ddeae7a0cbabb96c2207bcc77f6233cc10588e5d9` |

These are upstream umbrella notices, not a claim that every component they
mention is linked into each selected assembly. Preserve each dependency's
unmodified `.nupkg` and its legal files when bundling a local feed. If assemblies
are extracted or repackaged, carry both complete notice payloads with the
distribution, alongside the MIT attributions and permission text above. This
summary and the hashes do not replace the upstream notice files. Do not discard
the distinct HealthChecks notice as a duplicate or replace third-party terms
with SecretKit's first-party MIT declaration.

### Remaining distribution checks

Historical audit results and a consumer fixture's `NuGetAudit=false` do not
establish current clearance. Candidate verification must separately record an advisory
query, for example
`dotnet package list --project SecretKit.slnx --include-transitive --vulnerable`,
without automatically changing dependencies.

The build operator must also inspect newly packed nuspecs and embedded
`README.md`/`THIRD-PARTY-NOTICES.md`, and verify that any redistributed dependency
archives or extracted assemblies retain the legal files described above.
Existing build props embed the root README and this notice file; source metadata
inspection alone does not prove the contents of a future archive or local-feed
handoff.
