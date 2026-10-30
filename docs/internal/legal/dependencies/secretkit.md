# SecretKit Dependency Admission And Provenance

> **Audience:** Contributors | Maintainers | Distribution reviewers
> **Status:** Mixed
> **Owner:** Security
> **Last Verified:** 2026-10-25
> **Source Anchors:** `vendor/secretkit/`, `docs/internal/SECRETKIT_LOCAL_PACKAGES.md`, `dev/active/secretkit-adoption/secretkit-adoption-plan.md` Sections 5.1-5.3, `dev/active/secretkit-adoption/secretkit-adoption-tasks.md` task 1.5

## Admission and exact roles

The approved foundation uses four unchanged local binary packages, all at the
immutable version **0.1.0-local.20261024.1**. Central versions must use the exact
range `[0.1.0-local.20261024.1]`; replacing bytes requires a new reviewed version.
This record admits the dependency boundary, not a completed runtime cutover.

| Package | Approved foundation role |
| --- | --- |
| SecretKit.Core | Common reader/outcome contracts, authority-bound references and owned secret material |
| SecretKit.Infisical | Typed provider acquisition and explicit authentication lifetime |
| SecretKit.Extensions.Configuration | Required-group preparation and deliberate export for legacy .NET configuration consumers |
| SecretKit.Extensions.Hosting | Explicit lifecycle integration seam; no serving-host registration in this foundation |

The shared owner is the backend-independent Secrets module in
`Explore.ServiceDefaults`. No SecretKit dependency belongs in Domain,
Application, wire contracts or WASM. The BFF may consume shared hosting without
referencing backend implementation projects. Infrastructure's direct
Core/Infisical references belong to its future typed tenant adapter, not a
speculative foundation reference. Persistence's future ServiceDefaults
reference is limited to design-time bootstrap composition.

The approved Event graph aligns **Refit and Refit.HttpClientFactory to 16.3.0**.
The retained candidate notices identify Refit 16.3.0, ReactiveUI.Primitives,
ReactiveUI.Primitives.Core and ReactiveUI.Disposables 8.2.0, and ten selected
Microsoft.Extensions packages at 10.0.12, all declaring MIT. Core has no external
package dependency in that recorded net10.0 closure. Event must reconcile actual
Microsoft.Extensions minima with existing pins, retaining higher compatible
versions rather than blindly replacing them. Event's restored locks and actual
license inventory remain pending qualification; the producer's closure is not
the complete Event graph.

Azure, management, database clients, conformance tools and producer test/sample
dependencies are outside this four-package admission. No SDK ProjectReference,
repacking or remote package publication is authorized by it.

## Source register and clean-room boundary

The source identity is `islamu-ngo/SecretKit`, commit
`e635985239a3146228f1bb75d65a3f13c84a292c` **plus explicitly qualified working-tree
content**. The commit alone cannot reproduce or identify the package bytes.
The producer artifact identity is
`artifacts/runtime-adoption-hardening/candidate-r3/handoff/`.
The [local package record](../../SECRETKIT_LOCAL_PACKAGES.md#exact-provenance)
retains source-content, qualification-record, profile and all four archive
SHA-256 identities; `vendor/secretkit/packages.sha256` is the archive inventory.

This documentation uses the Event-owned approved plan, existing package guides,
and retained `vendor/secretkit/README.md`, `LICENSE` and
`THIRD-PARTY-NOTICES.md` as identity, interface and legal evidence. No producer
implementation source, decompilation, AST, tests or expressive implementation
design was inspected or incorporated. Package/member compatibility still needs
the foundation's permitted README/nuspec/public-interface review and actual
compiled seam checks; this record does not supply that execution receipt.

The independently documented Event design separates process admission from
Domain binding policy and places ownership at existing outer composition
boundaries. Protocol names and required interface identifiers are functional
constraints, not permission to copy an external implementation's structure.

## Redistribution and outbound licensing

SecretKit is MIT, copyright 2026 ISLAMU. Retain its copyright, permission and
warranty text in `vendor/secretkit/LICENSE`, the complete retained third-party
notices, and applicable embedded package notices. Each dependency retains its
own attribution and terms. Keep the four archives, notices and provenance
together in source/Git distributions and final container bundles, including
any separately approved alternative Event offering.

MIT permits redistribution and sublicensing without requiring a source offer,
hosting fee, seat restriction or field-of-use restriction. It does not transfer
third-party ownership, grant trademark rights or provide an express patent
grant. Event's public AGPL-3.0-or-later obligations and any Steward-selected
alternative outbound terms remain separate. The CLA cannot relicense
third-party material or remove its notices. This admission does not itself
authorize another Event licensing or distribution model.

The [IP governance gate](../IP_GOVERNANCE.md#dependency-and-license-gate)
still applies to the actual restored graph and assembled distribution. A
passing scanner is inventory evidence, not legal certification or proof that
every distribution obligation has been fulfilled.

## Qualification limits

Historical package-transfer and producer/offline receipts remain attributed in
the local package record. They prove neither Event runtime behavior nor native
Infisical actor/ACL enforcement. Event's current foundation build, 25 authority
invariants, backend/browser Refit checks, fresh locked-cache source/hash checks
and 485-pair dependency-license inventory are recorded in the local package
guide. Four architecture failures independently reproduce on the untouched
base and remain quarantined; the suite is not described as clean. Domain,
Application, wire and browser restored graphs contain no SecretKit packages.

No serving-host registration, background maintenance, old-project deletion,
tenant adapter cutover, image execution or trimming/Native AOT qualification is
claimed. See the [ownership ADR](../../adr/ADR-secretkit-event-ownership.md)
for the approved architecture and its separate successor obligations.
