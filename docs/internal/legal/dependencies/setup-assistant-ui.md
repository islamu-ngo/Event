# Setup Assistant UI Dependencies

> **Audience:** Maintainers and release reviewers
> **Owner:** Platform/Ops
> **Reviewed:** 2026-09-30
> **Evidence:** Central package versions, generated target lockfiles and NuGet package metadata

## Reviewed Components

| Component | Version | Role | Terms |
|---|---|---|---|
| Spectre.Console | 0.57.2 | CLI human rendering | MIT |
| Spectre.Console.Cli | 0.56.1 | CLI grammar | MIT |
| Spectre.Console.Ansi | 0.57.2 | Transitive CLI rendering | MIT |
| Avalonia | 12.1.3 | Explicit XAML build and native framework dependency | MIT |
| Avalonia.Desktop | 12.1.3 | Native desktop runtime | MIT |
| Avalonia.Themes.Fluent | 12.1.3 | Native controls and theme | MIT |
| Avalonia.Angle.Windows.Natives | 2.1.27548.20260419 | Windows rendering assets in the restore graph | BSD-3-Clause |
| Avalonia.Headless | 12.1.3 | Test-only real-control harness | MIT |
| Avalonia.Fonts.Inter | 12.1.3 | Headless harness dependency; not shipped | MIT package metadata |

The reviewed permissive terms preserve public AGPL distribution and the
project's alternative internal-use distribution paths. They do not transfer
third-party ownership to ISLAMU. Preserve every shipped component's copyright,
license conditions and disclaimer. ANGLE additionally prohibits unauthorized
endorsement using the named companies or contributors.

## ANGLE File-License Metadata

The exact ANGLE package stores its terms in `LICENSE` rather than an SPDX
expression. The installed file contains the three BSD redistribution and
non-endorsement conditions. The dependency-policy entry records that
classification for **2.1.27548.20260419 only**; it is not a general license
bypass or approval of future versions.

The generated lock records package SHA-512:

`l17nI3XVDN3oMnpjf2pnmJg0YTwK4m6NLsn/itAjDMdObTFxN77D5F1M9sRMSfViSY3KKcse1ROczwgoWLJsnA==`

This metadata decision does not establish Windows runtime or protected-save
support. Windows/macOS file output remains disabled pending native host
permission evidence.

## Native Closure And Distribution Gate

The desktop lock resolves a coherent SkiaSharp 3.119.4 and HarfBuzzSharp
8.3.1.3 family, including the matching native assets. Central versions used
by other independently hosted projects are not proof of the desktop's actual
runtime versions. Review the desktop lock and published RID dependency
manifest, not only `Directory.Packages.props`.

Each published target must exclude other targets, SetupLive, platform clients
and unapproved telemetry. Native asset presence is RID-specific: a restore
graph containing Windows or WebAssembly packages does not imply those files
ship in the Linux executable. Retain the third-party license material with
the distribution; a passing metadata validator alone is insufficient release
evidence.

The headless harness and its font dependency remain private test dependencies.
Native release-contract tests reject both packages in a shipped target's
dependency manifest. This record does not authorize moving test fonts into
application assets or changing their upstream font terms.

## Source Register And Independent Design

Accessed 2026-09-30; only package/interface facts and legal terms were used.
No external implementation source, tests, snippets or UI assets were copied.
The implementation follows repository Core operations, framework-neutral
workspace state and the shared protected-output adapter.

- [Spectre.Console metadata](https://api.nuget.org/v3-flatcontainer/spectre.console/0.57.2/spectre.console.nuspec)
- [Spectre.Console.Cli metadata](https://api.nuget.org/v3-flatcontainer/spectre.console.cli/0.56.1/spectre.console.cli.nuspec)
- [Avalonia.Desktop metadata](https://api.nuget.org/v3-flatcontainer/avalonia.desktop/12.1.3/avalonia.desktop.nuspec)
- [Avalonia Fluent theme metadata](https://api.nuget.org/v3-flatcontainer/avalonia.themes.fluent/12.1.3/avalonia.themes.fluent.nuspec)
- [Avalonia headless metadata](https://api.nuget.org/v3-flatcontainer/avalonia.headless/12.1.3/avalonia.headless.nuspec)
- [Headless font package metadata](https://api.nuget.org/v3-flatcontainer/avalonia.fonts.inter/12.1.3/avalonia.fonts.inter.nuspec)
- [ANGLE package license](https://www.nuget.org/packages/Avalonia.Angle.Windows.Natives/2.1.27548.20260419/license)
