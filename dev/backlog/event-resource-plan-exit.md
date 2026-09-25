# Governed event resources: remaining plan-exit work

- Owner: Event-resource engineering lead.
- Activate when: Preparing the merged feature for a release or closing its remaining acceptance gates.
- Source: The implementation ledger and 42-scenario evidence captured before retiring the `event-resources` worktrees. Seven cumulative PRs [#49](https://github.com/islamu-ngo/Event/pull/49)–[#55](https://github.com/islamu-ngo/Event/pull/55) merged into `develop` at `c4586607da1947f56646a70f535e19972f5c2027` on 2026-09-25.
- Status: 21 implementation items shipped; 34 scenarios Verified and eight Partial. A merged PR is not proof that the remaining acceptance or CI gates passed.

## Reconciliation of every unchecked implementation-ledger item

| Unchecked ledger item | What landed | Remaining work |
| --- | --- | --- |
| Protected destinations: attack specification | Scope-bound protection, hostile-input denial, provider registration, native HTTP tests and Split BFF cases landed in #54. | [Controlled-origin transport and keyring assurance](event-resource-delivery-security-verification.md); real browser navigation in [browser acceptance](event-resource-browser-acceptance.md). |
| Protected destinations: native write and redirect | Encrypted write-only destinations, safe-origin projection, current-authority redirect and operator guidance landed in #54. | Prove the unexercised multi-hop/real-browser conditions in the two linked acceptance entries; do not reimplement the shipped endpoint. |
| Protected destinations: full verification | Targeted redirect, protector, telemetry, keyring restore and Release checks passed; the broad API suite did not yield a classified green result. | [Release verification and formatter repair](event-resource-release-verification.md) and controlled-origin transport assurance. |
| Protected destinations: path-limited commit packet | #54 merged with its verified published tree and original commit ancestry. | None; this is a stale checkbox, not uncommitted code. |
| Browser phase: architecture and knowledge graduation | [ADR-033](../../docs/internal/adr/ADR-033-governed-event-resource-delivery.md), public/internal guidance and the seven deferred-feature entries below landed with #55. | Close the eight Partial scenarios and confirm any reusable lessons not already graduated to the domain journal. This index makes the remaining work independent of ignored worktree notes. |
| Browser phase: full verification | Focused component/API slices, Blazor Integration and the five-engine resource provider matrix passed; #55's exact tree passed Release build. | [Browser acceptance](event-resource-browser-acceptance.md) and [release verification](event-resource-release-verification.md); do not claim WCAG certification or broad-suite success. |
| Browser phase: ordered commit packets | #55 merged with the expected published tree and all earlier heads preserved in `develop` ancestry. | None; this is a stale checkbox, not a missing commit. |

The eight Partial scenarios are **S02, S05, S12, S17, S18, S22, S31 and S34**; each has an explicit real-surface acceptance step in [browser acceptance](event-resource-browser-acceptance.md). The separate [availability database constraint](event-resource-availability-order-constraint.md) records a valid post-publication review finding, not an eighth unimplemented phase.

## Deferred features already present in the develop backlog

These existing entries need no duplicate tickets: [malware scanning](event-resource-malware-scanning.md), [guest/dependent access](event-resource-guest-dependent-access.md), [provider-generated links](event-resource-provider-generated-links.md), [identified access audit](event-resource-access-audit.md), [template/federation support](event-resource-template-federation.md), [portable file bundles](event-resource-portable-bundles.md), and [individual certificates](event-resource-individual-certificates.md). Their owners, activation triggers and acceptance criteria remain in those files.
