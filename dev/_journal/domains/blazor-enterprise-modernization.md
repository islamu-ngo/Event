# Blazor Enterprise Modernization Knowledge Ledger

> **Scope:** Same-origin typography, published host composition, and truthful modernization evidence
> **Updated:** 2026-10-06 Europe/Brussels
> **Evidence:** Executed host contracts, actual published consumers and browser observations; limits remain explicit.

The [typography ADR](../../../docs/internal/adr/ADR-local-typography-assets.md)
owns the decision and recovery contract. The
[tracked roadmap](../../../docs/internal/BLAZOR_MODERNIZATION_ROADMAP.md)
retains deferred work. This ledger records reusable lessons, not a second task
ledger or a claim that the broader modernization program is complete.

## [2026-10-06 Europe/Brussels] Local-looking fonts need origin enforcement

**Context**: The typography slice replaced the host's Google Fonts link with
admitted local Inter while preserving existing token and render ownership.

**Symptom / Observation**: A shell can emit a local stylesheet yet still
authorize third-party typography through its CSP or load it through nested
CSS. An independent review also found that mounted-path tests must consume
the document's emitted base, not manufacture a base that bypasses the shell.

**Root Cause**: Typography privacy crosses the host document, stylesheet URL
resolution, font response, and browser policy. Appearance and shell-only
assertions cover only part of that path. Host-only URL comparison also misses
scheme/port changes.

**Resolution**: `TypographyAssetContractTests` consumes the emitted base,
compares complete origins, requests real stylesheet/font responses, and
checks the admitted font digest. A new self-only CSP invariant failed before
the obsolete Google style/font exceptions were removed from
`MiddlewareExtensions`. The affected existing BFF policy test was updated,
not disabled. Final results are typography 10/10 and BFF
13/13 passed:

```bash
dotnet test --project tests/Explore.Blazor.IntegrationTests/Explore.Blazor.IntegrationTests.csproj --configuration Release --no-build --treenode-filter "/*/*/TypographyAssetContractTests/*" --minimum-expected-tests 1
dotnet test --project tests/Explore.Blazor.IntegrationTests/Explore.Blazor.IntegrationTests.csproj --configuration Release --no-build --treenode-filter "/*/*/BffNoKeycloakResilienceTests/*" --minimum-expected-tests 1
```

The HTTP seam includes mounted `/community/` delivery and rejects HTML fallback
at typography URLs. The stylesheet declares the 300-800 variable face and
`font-display: swap`; existing tokens retain system/glyph fallback. Browser
CSS/font returned 200 with correct types, 352,240 font bytes, ETag, and
`max-age=3600, must-revalidate`. The font SHA-256 is
`693b77d4f32ee9b8bfc995589b5fad5e99adf2832738661f5402f9978429a8e3`.
Inter decoded for all six required weights.

**Why This Matters for Future Work**: Prove resource privacy through emitted
and consumed URLs plus enforced policy. Begin browser network observation
before navigation for delayed loads; keep only sanitized public typography
evidence, not raw cookies, credentials, environment output, or browsing
history. A successful load does not prove unavailable-font recovery. In the
separate failure probe, subscribe to `loadingerror` before changing the local
font source. A missing URL returned HTML at HTTP 200, but font decoding failed:
the face entered `error`, fallback metrics matched the system-only stack, and
text remained readable without external requests. Status alone is insufficient.
The
canonical font URL's revalidation policy must not be described as immutable.

**References**:
- `src/Explore.Blazor/Components/App.razor:34-38`
- `src/Explore.Blazor/Extensions/MiddlewareExtensions.cs:18-28`
- `src/Explore.Blazor/wwwroot/css/fonts.css:1-7`
- `src/Explore.Blazor/wwwroot/css/tokens.css:31-33`
- `tests/Explore.Blazor.IntegrationTests/Endpoints/TypographyAssetContractTests.cs`
- [Inter admission and OFL obligations](../../../docs/internal/legal/dependencies/inter.md)
- [Host typography workflow](../../../docs/internal/BLAZOR_DEV_WORKFLOW.md#5-host-owned-typography)

**Promotion Consideration**: The architectural rule is captured in the linked
ADR and workflow. Keep the testing/policy-boundary lesson here; no duplicate
skill or global rule is needed.

## [2026-10-06 Europe/Brussels] Publish the composition root, not workspace luck

**Context**: The shared Blazor host's typography had to survive both Split and
Standalone publication before a release claim could be made.

**Symptom / Observation**: Standalone publication reported `NETSDK1152` for
colliding API/Blazor/Standalone root appsettings. The failure reproduced on
untouched `origin/develop`, not only the feature worktree. After publication
succeeded, a fresh-SQLite launch could not load
`Explore.Persistence.DataProtection.Migrations.Sqlite`. Successful publication
and font presence had not proved complete startup dependencies.

**Root Cause**: Referenced executables contributed root configuration to a
combined host that already owns its configuration. Migration assembly copying
depended on unrelated projects' existing workspace binaries instead of SDK
project dependencies. Those composition problems can remain hidden when
development storage or build artifacts already exist.

**Resolution**: `Event.Standalone.csproj` filters foreign executable-root
appsettings publish items while retaining its own configuration, nested
static files, assemblies, and SDK collision diagnostics. It explicitly
references the three existing Data Protection migration projects and the
embedded SQLite privacy-authority migration project. The SDK-regenerated
lockfile adds project entries, not dependency version changes. No migration
SQL/snapshot or domain/provider behavior was edited.

The execution owner verified both publications and published Standalone
startup on new task-owned SQLite storage with the selected authority:

```bash
dotnet publish src/Explore.Blazor/Explore.Blazor.csproj --configuration Release --output "$TMPDIR/blazor-modernization/split"
dotnet publish src/Event.Standalone/Event.Standalone.csproj --configuration Release --output "$TMPDIR/blazor-modernization/standalone"
```

Complete-output deployment is documented in the existing internal topology
anchor and public Standalone guide. The published Split host subsequently
served its actual shell and decoded font with the same admitted bytes, cache
and CSP boundary. Neither publication nor this typography runtime observation
certifies a multi-provider migration matrix or unrelated application features.

**Why This Matters for Future Work**: Validate the real publication on fresh,
task-owned storage without mutating inherited databases. Startup dependencies
belong in the composition root's build graph, not workspace globs. Reproduce
failures on an untouched base before assigning them to a resource change;
fix only related composition prerequisites, and keep unrelated failures
quarantined.

**References**:
- `src/Event.Standalone/Event.Standalone.csproj`
- `src/Event.Standalone/packages.lock.json`
- [Hosting topology and publication ownership](../../../docs/internal/BLAZOR.md#hosting-topology-split-and-standalone)
- [Public Standalone guide](../../../docs/public/documentation/readme/self-hosting/docker-standalone.md)
- Configuration prerequisite commit: `5610ed92f`
- Explicit startup-dependency prerequisite commit: `b22eed477`

**Promotion Consideration**: Composition ownership is recorded in the topology
anchor and typography ADR. Retain the development-versus-publication lesson in
this domain ledger.

## [2026-10-06 Europe/Brussels] Separate glyph evidence from localization proof

**Context**: Final typography inspection exercised representative languages,
directions, and viewports while the modernization follow-ups were graduated
from ignored local planning memory.

**Symptom / Observation**: The Arabic cookie produced `lang="ar"` and
`dir="rtl"`, and a temporary Arabic/Greek/Cyrillic glyph probe was readable.
An existing launch API 308 response nevertheless prevented localized
translation/footer identity verification. Correct document direction and
readable glyphs did not establish the complete localized application flow.
The four original missing-label examples were also stale: their controls
already had contextual accessible names.

**Root Cause**: Typeface decoding, system glyph fallback, document culture,
translation transport, accessible control semantics, and tenant identity are
different contracts. Aggregating them into a generic "RTL/accessibility passed"
claim hides which boundary was observed. Likewise, a completed font slice
does not implement state, styling, fixture, or HTTP/security improvements.

**Resolution**: Retain specific evidence: desktop 1440 x 900 and phone
390 x 844 readable; no phone horizontal overflow; light/dark phone and desktop
light/dark inspected; representative Standalone RTL glyph probe readable;
all six Inter weights decoded. Missing-font fallback and both published
consumers passed. All three actual renderer descriptors and root/mounted
delivery passed in the ten-case HTTP suite, but only Server hydration was
observed live. Do not attribute API 308 to typography or call these checks
complete WASM/Auto hydration. Both shells reflowed at 720 x 450 DPR 2;
this is zoom-equivalent reflow, not native browser-menu 200% zoom.

The selected architecture check passed 22/22:

```bash
dotnet test --project tests/Event.Architecture.Tests/Event.Architecture.Tests.csproj --configuration Release --no-build --treenode-filter "/*/*/BlazorClientArchitectureTests/*" --minimum-expected-tests 1
```

The roadmap preserves the complete root backlog, including all six accessible
surfaces, three independently scoped styling increments, nullable selection,
complete logo reset, deterministic interactions, separate project/mock cleanup,
HTTP retry/security remediation, and knowledge/convention promotion. It keeps
`IVSD-F001/M001` for accessibility, `F002/M002` for typography,
`F003/M003` for truthful diagnostics/project/mock/HTTP work, and `F004/M004`
for both state slices. Finding IDs remain open under the report owner's
assessment until their actual evidence gates close.

**Why This Matters for Future Work**: Report the observed seam and missing
evidence, not a certification inferred from attributes or counters. Preserve
ignored backlog scope in tracked documentation before retiring task memory.
Historical counts, universal labels, and zero graph flows are not completion
criteria.

**References**:
- [Tracked remaining roadmap](../../../docs/internal/BLAZOR_MODERNIZATION_ROADMAP.md)
- [I-VSD assessment](../../../islamic-value-sensitive-design/workstreams/i-vsd-blazor-enterprise-modernization.md)
- [Accessibility standards](../../../docs/internal/ACCESSIBILITY.md)
- `src/Explore.Blazor/Components/App.razor:16-28`
- `src/Explore.Blazor.Client/Shared/ImageUpload.razor`
- `src/Explore.Blazor.Client/Components/Events/EventPreviewWorkspace.razor.cs`

**Promotion Consideration**: Stays in the domain journal as an evidence-boundary
lesson. Future conventions are promoted only when new evidence changes a rule;
agent-rule edits require twin updates and their own validation.
