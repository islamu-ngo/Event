# Distributed And Browser Test Dependencies

Reviewed: 2026-10-23. Scope: the approved testing-assurance modernization programme. These dependencies belong only to engineering test projects; this decision does not add them to Event product runtime images or deployment bundles.

## Pinned Components And Provenance

| Component | Role | Declared license | Authoritative package and source identity |
|---|---|---|---|
| Aspire.Hosting.Testing 13.4.6 | Direct test-only support for the real AppHost graph; matches the existing Aspire pins | MIT | [NuGet package](https://www.nuget.org/packages/Aspire.Hosting.Testing/13.4.6); NUSPEC repository `https://github.com/microsoft/aspire`, revision `87fe259e4fc244c599019a7b1304c85a1488f248` |
| Microsoft.Playwright 1.64.1 | Direct test-only .NET browser automation | MIT | [NuGet package](https://www.nuget.org/packages/Microsoft.Playwright/1.64.1); NUSPEC repository `https://github.com/microsoft/playwright-dotnet.git`, revision `8cc0f7b4e67f7a2ceb91a6cb0a5a8e6de6141ae1` |

The official NuGet archives were inspected for package metadata only. Their SHA-256 digests are respectively `3828d5e2656f6e6ca486471fb5ff8f743c31fedbe30eacb9a94fa99b9fee1f9a` and `f88df0f810c240588a321f7764ea17106448346d042443cf4abf0390db31b729`. Both NUSPEC files identify Microsoft as author and declare the [MIT license](https://licenses.nuget.org/MIT).

MIT permits the intended public and alternative outbound offerings while preserving the component's copyright and permission notice. That permission applies to the component; it does not transfer its ownership or waive third-party notices. Retain required notices with any authorized redistribution, and review the resolved transitive packages under the repository dependency policy. No modified upstream source, copied implementation, source-derived structure, or vendored archive is introduced by this change.

## Independent Implementation Boundary

The implementation handoff contains public API signatures, observable behavior, exact package identities and repository-native requirements. Fixture ownership, startup, cancellation, cleanup, browser journeys and retained evidence are designed from Event's own contracts. Package source and external implementation examples are excluded. The two central package pins express version selection; the test project and its generated lockfile define the resolved dependency graph.

On 2026-10-24, two runtime contexts independently reported incidental external implementation examples: one during a public-documentation search, the other during an installed SDK XML lookup. Both stopped before making runtime model or Formbricks changes and are permanently restricted to research and read-only work. Existing temporary observation tests were authored and verified before the second exposure; no subsequent implementation was accepted from either context. A fresh implementation owner receives only sanitized functional requirements and repository-native context, excluding installed XML and external implementation research. Foreign snippets are neither retained as implementation inputs nor forwarded. Process-snapshot PID field names remain unverified and must not be used to attribute running resources to assumed Release binaries.

## Browser And External Tool Boundary

The Playwright NuGet license does not establish the license of a downloaded browser, its bundled components, or FFmpeg. Browser binaries remain separately obtained developer/CI tools in the external Playwright cache. They are not vendored or included in Event product distribution. Preserve their upstream license and notice files and inspect those component terms before admitting the actual browser execution profile; no blanket MIT claim or product-bundling approval is made here.

The selected execution profile uses Chrome Headless Shell 156.0.8078.4, Playwright revision 1248, obtained through Playwright's documented [headless-only installation](https://playwright.dev/dotnet/docs/browsers). The independently inspected aggregate `LICENSE.headless_shell` notice has SHA-256 `80253764cf5dde31aae6aa57ff29d460947d110f3eae31cb9cf208fb5ffcc089` and contains Chromium BSD-3-Clause and other component terms, including Apache, MIT, LGPL and MPL notices. Neither this inventory nor [Chromium's license](https://chromium.googlesource.com/chromium/src/+/refs/heads/main/LICENSE) establishes one license for the assembled binary. Retain the aggregate notice and applicable [Google executable terms](https://www.google.com/chrome/terms/).

Playwright's separately cached FFmpeg reports `n7.0.1-playwright-build-1013`. Its bundled `COPYING.LGPLv2.1` has SHA-256 `b634ab5640e258563c536e658cad87080553df6f34f62269a21d554844e58bfe`. [FFmpeg's license guidance](https://ffmpeg.org/legal.html) distinguishes its LGPL baseline from optional GPL components; the inspected binary metadata reports no `--enable-gpl` or `--enable-nonfree`. Browser journeys do not enable media recording. This evidence supports external engineering-tool use under its own terms, not an MIT or unrestricted redistribution claim.

The approved installer excludes full Chrome, whose separate cache contained a proprietary Widevine CDM notice; no Widevine filename or notice was found in the selected headless-shell directory. All executable components stay outside Event's repository, linked product and release bundles. Any future browser/FFmpeg bundling or media use needs a separate component and distribution review; external-tool approval is not a legal certification of an assembled offering.

The separately approved k6 executable follows its own [tooling and distribution decision](../../adr/ADR-testing-assurance-k6-tooling.md). It is not a NuGet dependency and this document does not broaden that approval.

### Existing Cerbos policy compiler receipt

The programme reuses Cerbos `0.53.0`, already selected by the repository's policy
check/publication workflows and Compose declarations. This is not a new product
dependency or a version change. The official release archive SHA-256 is
`56427176bd2e5e676097f9a08743ed211e7c0123af1e71a48f94d497e3c9e131`;
the admitted executable SHA-256 is
`a9754bffb82f09dd0fef8b809048d063d4646df485fb33eb984da14535a39574`.
The archive matched the official checksum. Only the executable and Apache-2.0
license were extracted into the external task cache; no upstream source,
README examples or `cerbosctl` entered implementation context. Signature
verification was not performed because `cosign` was unavailable.

The scoped `cerbos --version` command reported `0.53.0`, build
`78d494cab93cf3e3723da18d5390bf9222593560`, with `vcs.modified=false`.
The existing `cerbos compile --tests=cerbos/tests cerbos/policies` command
executed 884 policy tests, all successful. These are native compiler/policy-test
results, not live PDP, HTTP authentication or same-candidate application evidence.
Retain the Apache license and normal tool notices; this receipt does not authorize
copying implementation or bundling the executable in Event product images.

## Verification

Run `dotnet run .ci/scripts/validate-dependency-license-policy.cs -- .` after changing the central package inputs and again against the complete integrated dependency graph. The validator checks repository policy and visible exceptions; passing it does not certify browser-component or assembled-distribution terms. Test-project restore/build, generated lockfiles, actual AppHost readiness and browser execution are separate evidence and must not be inferred from package admission.

The pin-admission run on 2026-10-23 exited 0 for 468 package/version pairs. It retained the existing visible exceptions for Microsoft.Data.SqlClient.SNI.runtime 6.0.2, Microsoft.VisualStudio.Azure.Containers.Tools.Targets 1.23.0, NetArchTest.Rules 1.3.2 and SonarAnalyzer.CSharp 10.31.0.145097; none was added or broadened here. Repeat validation after integrating the distributed project's resolved lockfile.
