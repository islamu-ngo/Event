# GuideFlow Dependency Decision

> **Audience:** Maintainers and distributors
>
> **Status:** Selected; runtime conformance pending
>
> **Decision date:** 2026-10-31
>
> **Owner:** Blazor presentation

## Selection And Rights

The developer confirmed MIT and explicitly selected GuideFlow for contextual
tours on 2026-10-31. The application pins exactly 1.0.1, not an open-ended
upgrade. GuideFlow retains its own terms; the project CLA does not confer rights
over it. Preserve applicable upstream terms and notices in assembled
distributions. Do not infer or replace missing copyright-holder fields.

Earlier metadata checks observed MIT in the package manifest and unresolved
placeholders in the pinned upstream license. Those historical observations are
retained in programme evidence; the developer's subsequent confirmation and
selection resolve the earlier execution hold. They do not claim authorship of
the component or prove runtime/accessibility behavior.

## Native Dependency Evidence

`Directory.Packages.props` declares `[1.0.1]`; only
`Explore.Blazor.Client` directly references the package. Native restore of
`Event.Standalone` completed successfully on 2026-10-31 and generated the
client, Blazor host and Standalone lockfile projections.

The client lock records exact range `[1.0.1, 1.0.1]`, resolved version 1.0.1,
and content hash:

```text
cKlgbz6FwNYHzWSCOQiQqRSGZb9Cj9+kbKOCVlSJaz2hctTqNCGJWxBQ9+xBo9qtOL9+U9YlY7IdYBpFGuDD9w==
```

Its declared dependency is `Microsoft.AspNetCore.Components.Web >= 8.0.27`.
The lockfiles, not a manually transcribed dependency tree, remain authoritative.
Restore is graph evidence, not proof that the actual overlay, callbacks or
framework host work.

## Application Boundary And Provenance

GuideFlow renders tours. Event owns tenant eligibility, HAL affordances,
original identity, server claims, semantic progress, erasure, acknowledgement,
scope cancellation and arbitration with existing forms/dialogs. Renderer-local
persistence and automatic starts do not supply application authority.

Integration is independently designed from the programme's functional
requirements, native learning contracts and the developer-supplied GuideFlow
interface skill. No vendor implementation is copied into Event. A renderer
callback alone cannot complete an unvisited semantic step.

Source identities for the historical metadata/notice checks:

```text
https://api.nuget.org/v3/registration5-gz-semver2/guideflow/1.0.1.json
https://api.nuget.org/v3-flatcontainer/guideflow/1.0.1/guideflow.nuspec
https://api.github.com/repos/MiracleFoundation/GuideFlow/license?ref=acbef633e0f4b67da0920e18a519dd3d5e211b9a
```

See [contextual learning](../../CONTEXTUAL_LEARNING.md#renderer-admission-and-contribution-ledger)
for the capability/contribution ledger and separately recorded host gates.
