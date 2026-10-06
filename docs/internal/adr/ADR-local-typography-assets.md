# ADR: Host-Owned Local Typography Assets

> **Status:** Accepted and implemented; integration limits recorded below
> **Date:** 2026-10-06
> **Scope:** Blazor host typography and its Split/Standalone publication consumers
> **Evidence:** Executed host contracts, published consumers and observed browser behavior

## Context

The Blazor document previously linked Google's Inter stylesheet. A self-hosted
instance therefore instructed visitors to contact a third-party font service,
disclosing network/request metadata outside the operator's typography delivery
boundary. Replacing that dependency is a bounded privacy and availability
improvement, not an application-wide offline or zero-external-traffic guarantee.
Authentication and explicitly configured integrations retain their own contracts.

Typography already has an owner: `tokens.css` defines the primary and secondary
Inter/system stacks, `base.css` consumes the primary token, and `layers.css`
orders the global cascade. The shared host document owns static-resource links.
Neither client components nor the shared BFF library need a new font service,
tenant setting, or parallel theme abstraction.

## Decision

### Asset delivery and token ownership

`src/Explore.Blazor/Components/App.razor` links
`Assets["css/fonts.css"]` using the existing static-web-asset convention.
`src/Explore.Blazor/wwwroot/css/fonts.css` owns a single normal roman
`@font-face`, with `font-weight: 300 800`, `font-display: swap`, and the
stylesheet-relative URL `../fonts/inter/InterVariable.woff2`.

`src/Explore.Blazor/wwwroot/fonts/inter/` owns the admitted font and complete
`LICENSE.txt`. Existing primary/secondary token stacks remain
`"Inter", system-ui, -apple-system, sans-serif`; token consumption, tenant
branding, theme selection, cascade layers, render policy, and prerender behavior
are not replaced by this resource change. Resolve emitted stylesheet URLs
against the document's emitted base, then font URLs against the stylesheet,
preserving mounted paths. Same-origin comparison includes scheme, host, and port.

The variable face preserves all six application weights: 300, 400, 500, 600,
700, and 800. It is not a universal script font. Arabic and other unsupported
glyphs deliberately use locally installed system fonts. `font-display: swap`
allows text to use that stack while the local face loads; fonts failing to load
must not cause an external retry or fallback. A browser-only missing-font probe
observed the actual loading failure and readable system fallback; this is not
inferred solely from the CSS declaration.

### Admitted bytes and distributor obligations

The [Inter admission record](../legal/dependencies/inter.md) is authoritative
for provenance and terms. Admission covers the unmodified roman variable face
`web/InterVariable.woff2` from Inter 4.1, stored under a project-owned canonical
filename, not upstream application CSS or source.

| Artifact | Identity |
|---|---|
| Font | 352,240 bytes |
| Font SHA-256 | `693b77d4f32ee9b8bfc995589b5fad5e99adf2832738661f5402f9978429a8e3` |
| License SHA-256 | `262481e844521b326f5ecd053e59b98c8b2da78c8ee1bdbb6e8174305e54935a` |
| Release archive SHA-256 | `9883fdd4a49d4fb66bd8177ba6625ef9a64aa45899767dde3d36aa425756b11e` |

The font retains SIL Open Font License 1.1 (OFL-1.1), including the Inter
Project Authors' copyright. Every distributed font copy must retain that
copyright and the complete license. Do not sell the font by itself, imply
author endorsement, or represent it as application-owned material. The admitted
header declares no Reserved Font Name. Font updates, subsetting, conversion,
or derivative naming require fresh admission rather than silently extending
this unchanged-byte decision.

OFL permits bundling, embedding, redistribution, and sale with software; its
conditions attach to the font, not independently owned application material.
The font remains OFL under public AGPL and permitted alternative application
distributions. The application CLA cannot relicense it. This component decision
does not expand the Project Steward's alternative-licensing authority or waive
the repository's outbound-license covenant. No seat, hosting, source-offer, or
application relicensing obligation is introduced by this unmodified face.
The dependency validator checks package policy, not the legal sufficiency of
asset admission.

### Typography CSP and privacy boundary

`src/Explore.Blazor/Extensions/MiddlewareExtensions.cs` now emits
`font-src 'self'` and `style-src 'self' 'unsafe-inline'`, removing the obsolete
Google stylesheet/font allowances. This is self-only typography, not a claim
that every CSP directive forbids external integration traffic. Existing script
nonces and unrelated integration policy remain intact.

Tests check the actual response policy and emitted resource origins. Shell
checks alone cannot expose every delayed CSS import or nested font request;
final browser observation begins before navigation and must inspect actual
resource origins and initiators. Retain only sanitized public typography
paths/origins, status, media type, digest, cache policy, and policy outcome.
Do not retain cookies, authorization headers, token-bearing queries, raw
resource environments, personal browsing history, or participant content.
No new production telemetry is necessary.

### Published composition and caching

Split publishes `Explore.Blazor`; Standalone consumes that shared host and its
static assets through the SDK graph. A project reference or development response
is not proof of complete published output.

Publication exposed two related Standalone composition prerequisites, rather
than typography defects in API or database logic:

1. Referenced executable-root `appsettings*.json` files collided with
   Standalone's own configuration (`NETSDK1152`). Standalone's
   `PublishStandaloneConfiguration` target removes only foreign root appsettings
   publish items before SDK conflict resolution, retaining the composition
   root's configuration, nested static configuration, referenced assemblies,
   and duplicate-output diagnostics.
2. A fresh published host lacked the SQLite Data Protection migration assembly.
   `Event.Standalone.csproj` now explicitly references the existing MySQL,
   SQLite, and SQL Server Data Protection migration projects and embedded
   SQLite privacy-authority migration project. This replaces incidental
   workspace-binary copying with deterministic SDK dependency publication.
   The regenerated lockfile adds project entries, not new package versions or
   generated migration edits.

These changes make the combined host's existing startup dependencies explicit;
they do not change migration semantics or certify every provider topology.
Deploy complete publication output under the selected secret authority.

The shell stylesheet uses existing asset fingerprinting. The canonical WOFF2
URL is not independently immutable: observed font delivery uses
`max-age=3600, must-revalidate` and an ETag. Do not add an `immutable` assertion
to a mutable font URL or infer browser cache behavior from a filename.

## Alternatives

| Alternative | Disposition |
|---|---|
| Keep Google Fonts or restore it during failure | Rejected: recreates the unnecessary third-party typography contact. |
| System fonts as the primary design | Avoids a bundled font, but changes the chosen branding contract; retained as failure recovery, not silently substituted as the primary design. |
| Separate typography service/configuration | Rejected: duplicates existing token and host-asset ownership without an independent responsibility. |
| Replace Inter with a universal script family | Not established or admitted; local glyph fallback preserves the existing boundary without another dependency. |
| Treat project references/build success as runtime proof | Rejected: publication collisions and missing startup assemblies demonstrated the gap. |
| Broad component/style/test modernization in this decision | Rejected: independently shippable work has separate behavior and review boundaries. |

## Verification and integration limits

The following checks were physically executed on 2026-10-06. Their boundaries
matter: static delivery, font selection and client hydration are distinct.

| Surface | Observed result and boundary |
|---|---|
| Typography contract | 10/10 passed in 5.697 seconds after a successful 20.54-second Release build; actual Server/WebAssembly/Auto descriptors, shell/CSP, stylesheet/font responses, origin, admitted digest and emitted-base resolution at root and mounted `/community/`. |
| Existing affected BFF policy | 13/13 passed. |
| Blazor client architecture | 22/22 passed. |
| Publications | Split and Standalone publication passed; both published hosts subsequently served their real shell and typography successfully. |
| Standalone startup | Published output starts on new task-owned SQLite storage with the selected authority and explicit migration references. |
| Browser typography | CSS and font return 200 with correct media types; font is 352,240 bytes, with revalidation cache policy and ETag. Inter decoded for 300-800 and all six weights. |
| Readability | Desktop 1440 x 900 and phone 390 x 844 readable in light/dark; no phone horizontal overflow. Both consumers were inspected. |
| Language/direction | Arabic cookie yields `lang="ar"` and `dir="rtl"`; temporary Arabic, Greek, and Cyrillic glyph probe readable. This is representative evidence, not universal glyph coverage or full localization certification. |
| Missing font | Browser CSSOM switched only the local source to an unavailable resource. A prior `loadingerror` subscription completed; the HTML fallback could not decode as a font. Inter entered `error`, system-only and fallback text widths matched exactly, and text stayed readable without external font requests. |
| Review | Independent anonymized privacy and admission/publication proposals accepted; weighted approval 1.0, rejection 0.0, no blocking finding. |

Executed verification commands include:

```bash
dotnet test --project tests/Explore.Blazor.IntegrationTests/Explore.Blazor.IntegrationTests.csproj --configuration Release --no-build --treenode-filter "/*/*/TypographyAssetContractTests/*" --minimum-expected-tests 1
dotnet test --project tests/Explore.Blazor.IntegrationTests/Explore.Blazor.IntegrationTests.csproj --configuration Release --no-build --treenode-filter "/*/*/BffNoKeycloakResilienceTests/*" --minimum-expected-tests 1
dotnet test --project tests/Event.Architecture.Tests/Event.Architecture.Tests.csproj --configuration Release --no-build --treenode-filter "/*/*/BlazorClientArchitectureTests/*" --minimum-expected-tests 1
dotnet publish src/Explore.Blazor/Explore.Blazor.csproj --configuration Release --output "$TMPDIR/blazor-modernization/split"
dotnet publish src/Event.Standalone/Event.Standalone.csproj --configuration Release --output "$TMPDIR/blazor-modernization/standalone"
```

The context also records passing dependency policy for 502 package/version
pairs with the existing NetArchTest.Rules metadata exception visible, and
passing affected-source formatting after an earlier whole-solution format
timeout. Existing warnings were not suppressed. These records do not imply
a clean full-solution build or a legal approval supplied by a scanner.

Live browser hydration was observed only for Server. WebAssembly/Auto were
verified at the HTTP renderer-descriptor and asset boundary, not through live
hydration or governance-driven mode selection. The local profile lacks the
accountable identity needed for that selection. Both shells reflowed at
720 x 450 with DPR 2, the 200% zoom-equivalent CSS viewport; native browser-menu
200% zoom was not established. Split restored English after an Arabic cookie
reload; RTL glyph evidence is from Standalone's shared host. These are explicit
integration limits, not an all-render-mode, zoom or localization certification.

An existing launch API 308 response prevented localized translation/footer
identity validation. Correct HTML language/direction and a readable temporary
glyph probe do not resolve that integration observation, and it is not
attributed to typography.

## Consequences and recovery

Typography now has admitted local bytes, a single host-owned delivery path, and
an enforced same-origin font policy. Operators no longer need a third-party
font service for that path. Operators/distributors must preserve the font's
license and publish complete host output.

For a damaged or unavailable font, restore the previously admitted local bytes
with their notice, or deliberately use the existing system stack. Verify
legibility and absence of third-party typography requests after recovery.
Never relax CSP or restore a CDN link as a recovery step. No tenant data
migration, new typography secret, or compatibility flag is required.

This decision implements and verifies the bounded typography mitigation for
`IVSD-F002` / `IVSD-M002` with the integration limits above. The
[tracked roadmap](../BLAZOR_MODERNIZATION_ROADMAP.md) preserves every separate
modernization follow-up and its promotion gates.

## Related

- [Blazor architecture](../BLAZOR.md)
- [Host typography workflow](../BLAZOR_DEV_WORKFLOW.md#5-host-owned-typography)
- [Operator white-labeling guidance](../../public/documentation/readme/administration-and-branding/white-labeling.md)
- [Standalone deployment guidance](../../public/documentation/readme/self-hosting/docker-standalone.md)
- [Inter admission](../legal/dependencies/inter.md)
- [CSS layer decision](ADR-003-css-layer-architecture.md)
- [I-VSD assessment](../../../islamic-value-sensitive-design/workstreams/i-vsd-blazor-enterprise-modernization.md)
- [Durable lessons](../../../dev/_journal/domains/blazor-enterprise-modernization.md)
