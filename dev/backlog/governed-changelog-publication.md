<!-- ABOUTME: Required follow-up for the verified single-page changelog and protected GitBook publication. -->
<!-- ABOUTME: Preserves automation, setup, reader experience and recovery requirements from the CTO refinement. -->

# Governed Changelog Publication

Last Updated: 2026-09-10 Europe/Brussels

Status: **required sequential delivery**, not optional polish. Owner role: Platform/Ops with Documentation maintainer. Predecessor: `dev/active/governed-release-public-changelog/`. Schedule: next PR after categorized notes, before claiming an automated enterprise release experience. Rebaseline into its own triad with exact paths/commits and current I-VSD mappings before implementation.

## Outcome and State Contract

One public document, `docs/public/changelog/README.md`, contains every authorized governed release, newest release date first, with stable version anchors, concise summaries, breaking/upgrade actions, categories and durable evidence links. Reuse GitBook layout/Git Sync; offline generation and verification require no hosted account.

Flow: prepare canonical notes/context → commit `B` → candidate verification → human-controlled signing → final tag verification → retained authorization inventory → docs proposal → protected docs-branch acceptance → mutable GitBook sync mirror → observed GitBook delivery.

Keep publication receipts distinct: `verified`, `publication-pending`, `publication-delivered`, `publication-failed`, `publication-drift`. No new domain service is needed. GitBook/forge failure leaves the signed release valid; unsuccessful publication cannot claim delivery. Retry never retags, rebuilds binaries, moves stable `main` or changes canonical notes.

## P1 — Offline Single-Page Generator

Add `sync-public-changelog` write and `--check` modes to the existing C# engine. No arbitrary output path, forge API or token in deterministic composition. Inputs are complete local Git objects, existing final evidence and an explicit complete set of authorized published tags, pinned once to full annotated tag object IDs. Reuse tag/bundle verification. Emit a generated publication manifest recording the pinned input set and projection digest.

Directory existence or unsigned notes is not release proof. Preserve all previously accepted release identities: omission of any existing entry fails closed. A fresh rebuild uses the last accepted manifest plus the complete retained inventory of human-authorized, finally verified releases, not one branch's directories or just the triggering version. The inventory is retained by the final lane with tag-object IDs and disclosure authorization before dispatch; the publisher reads it but cannot grant authorization. This uses the existing retained evidence authority, not a new database or queue. A release whose dispatch is coalesced or never delivered must still be discovered on reconciliation. Missing complete inventory fails closed. Maintenance-line publication must retain newer-line entries. Historical final evidence may live in retained artifacts rather than at `B`; never assume it is committed or regenerate it with today's bundle.

Use explicit generated-region sentinels outside YAML frontmatter. Preserve static introduction and surrounding bytes. Rebuild the region from verified inputs rather than reparsing arbitrary Markdown headings. Missing/multiple/reversed markers, invalid UTF-8, unsupported inputs, oversized files, traversal, symlinked ancestors/files and unexpected content fail with a bounded diagnostic and no replacement. Reuse canonical/path checks, no generic parser framework.

`--check` is read-only and nonzero on missing/stale content. Identical generation changes no bytes/file; repeated dispatch creates no duplicate entry. Drift from the last accepted projection is reported before any overwrite and requires a reviewed correction. Valid new content is staged and published through atomic replacement; I/O failure cannot truncate accepted history.

Sort by descending descriptor release date, then descending parsed SemVer precedence for equal dates, then ordinal canonical version as final tie-break. Never filesystem/workflow time, locale or lexical version ordering. A newer `1.2.9` maintenance release may precede `2.0.0` by date. Label authorized prereleases **Pre-release**, retain them after stable publication, and identify the highest stable version separately from the latest dated entry. Do not invent supported/EOL policy. Backport IDs may appear in different releases, once within each.

Each entry contains:

- `## v<version> — YYYY-MM-DD` with a version-only stable anchor, not an unresolved Markdown link label.
- Stable/pre-release label, release line and curated outcome summary.
- Breaking Changes and Upgrade actions before ordinary changes; security/migration/configuration/API impact evidence and applicable adopter instructions.
- Features, Bug Fixes, Performance and Other Improvements only when nonempty; no author handles, raw bodies or PR noise.
- Compact Verify this release section with tag reference, canonical-notes SHA-256 and durable canonical/evidence links. Include verified tag-object/B information where useful; no self-referential hash in bytes defining `B`.

Compose from validated summary/context/impact fields through shared presentation code; do not regex-extract arbitrary Markdown or duplicate classification. Security is fragment impact, not a title heuristic. Keep the complete technical range available via canonical notes. Invalid/missing required upgrade evidence blocks publication; do not invent “safe upgrade” or rollback claims.

Links must resolve from the GitBook changelog space: use public upgrade routes or a configured publication base with immutable tag/path, not internal-only relative links. A publication base is noncanonical metadata. Validate schemes/escaping. Only generator-owned callouts/anchors may introduce GitBook syntax; untrusted prose cannot inject HTML/scripts/directives. Semantic headings and text carry meaning independently of emoji.

GitBook's current troubleshooting documentation states a 100 MB individual-file transfer ceiling (accessed 2026-09-10). This is not a usable-page rendering budget. Start with an explicit **1 MiB UTF-8 generated-page budget** as an ISLAMU design choice, including introduction/markers; verify desktop/mobile rendering and synchronization at that bound during activation. The existing engine cap is not evidence of hosted support. Add near-limit and over-limit cases. Fail explicitly rather than truncating history, raising the cap silently or splitting the user's single document; revisit with measured evidence if the ceiling is reached.

## P2 — Protected Transport and Setup

Use a dedicated protected acceptance branch (proposed `docs/publication`) containing the full existing `docs/public` tree. Generated history belongs to the publisher; other spaces continue through reviewed docs changes. GitBook two-way sync documents app bypass of branch protection, so it MUST NOT receive bypass on this acceptance branch. Connect it instead to a separate mutable mirror (proposed `docs/gitbook-sync`) with no reverse automatic merge into the acceptance branch. The adapter transports only the accepted public tree; the accepted source commit and digest remain in protected receipts, not trusted merely because the mirror names them. Any required GitBook app bypass is restricted to that mirror and requires explicit operator settings approval. Never broaden bypass to `develop`, `main`, signing refs or approval workflows.

The mirror is another mutable publication surface, not a trust root. If write-back changes it, preserve/report drift and require reviewed repair; no automatic merge, silent overwrite or signed-history rewrite. Proposed ref names are not live setup evidence. The extra ref separates SaaS write authority from repository approval without introducing a content API or service. Do not connect public sync to `develop` or a candidate branch, and do not add docs-only commits after `B` on stable `main`.

Verify actual GitBook repository, mirror branch, **Project directory `docs/public`**, space mapping and initial Git→GitBook direction. Official monorepo documentation confirms Project-directory-relative paths; repository YAML cannot attest live settings. Preserve every existing section/space key, particularly Changelog `space-4`; renaming keys replaces GitBook space identities and is not harmless cleanup. Keep assets inside their mapped space and use public-resolvable cross-space links. Review a complete-tree preview before a site-wide branch switch so other spaces/routes do not regress. A GitHub PR does not automatically create a GitBook change request; capture the actual available preview/delivery evidence rather than inventing a change-request URL.

The engine emits a proposal; the adapter owns network, credentials, protected writes and receipts. Reuse `.ci/release/adapter-contract.md`, existing provider definitions and trusted-default-branch final lanes. `.github/workflows/release-publish.yml` currently prints a declaration and reports drift; implement actual transport and fix its `docs/releases` path within this slice. A no-op is not a publisher.

Normal approved release completion records authorization durably, then dispatches publication through existing final-lane transport, with a manual retry/reconciliation entrypoint. Preserve human signing/disclosure/environment gates, required-check names and SHA-pinned external actions. No automatic version/tag authority. Providers still in discovery-only mode cannot gain protected writes from a manifest flag alone.

Verify the chosen bot's actual required-check trigger route. Current GitHub documentation says ordinary `GITHUB_TOKEN` pushes do not trigger subsequent push workflows, while selected PR events require manual workflow approval; `workflow_dispatch` and `repository_dispatch` are exceptions. Default to explicit dispatch of protected validation bound to the exact proposal commit, or the existing approved bot identity when available. Do not invent a new credential solely to avoid an approval or silently remove checks. The provider adapter must demonstrate check execution and acceptance for its selected mechanism before activation.

One global concurrency group covers all release lines. It is mutual exclusion, not durable delivery: GitHub's default queue can replace pending runs even with `cancel-in-progress: false`; documented `queue: max` only raises the bounded pending capacity to 100. Correctness therefore rests on the retained authorized inventory and reconciliation, not a queue setting or arrival order. Use expected-old-commit CAS, never force push or cancel during a write. On conflict reread accepted branch/manifest and authorized inventory, recompute the union and retry at most three times; exhausted retries remain pending. All concurrent/coalesced releases must survive. Any proposal review binds the pinned tag set and generated digest; a changed union requires renewed review, not reuse of stale approval.

Idempotency key: pinned tag-object set plus projection digest. Repeated dispatch reuses one proposal and creates no duplicate PR/commit. Transient network/rate-limit failures have bounded backoff and honor retry headers; permission, identity, disclosure and integrity failures do not auto-retry. Delivery requires accepted docs commit plus observed GitBook sync status; absent provider confirmation is pending/unverified.

Generator needs no credentials. Adapter secrets use approved environment/secret authority, limited to documented branch/content rights; forks/previews never receive signing/write authority. Document rotation and revoked-token remediation. Do not add a GitBook content API token when Git Sync provides transport. If actual new external keys are selected, update `.env.example` and public/internal docs together with defaults/validation; never preinvent secrets or log values.

## P3 — Recovery, Drift and Retained Evidence

Reuse `report-publication-drift`: canonical tag/hash attribution, advisory discrepancy, `autoRepair: false`, no signed-release invalidation. Record tag object/version, input-set/projection digests, expected/actual docs commit, attempt, status and bounded diagnostic. No credentials, contributor identity or restricted prose. Retain publication receipts and canonical bundles beyond expiring CI artifacts without a new database.

| Failure | Required outcome/recovery |
| --- | --- |
| Missing activation, bundle, signature or disclosure evidence | Stop before publication; satisfy named prerequisite. |
| Forge/GitBook unavailable after release verification | Release valid; publication pending; retry same pinned proposal. |
| Crash before branch acceptance | No accepted change; rerun idempotently. |
| Crash after acceptance but before receipt | Reconcile accepted commit/digest and observe sync, without duplicate entries. |
| Concurrent maintenance/mainline runs | CAS loser recomputes union; preserve both; changed proposal requires fresh review. |
| Pending dispatch replaced or never delivered | Reconciliation discovers the authorized release from retained final-lane inventory, even if absent from the accepted page. |
| Bot-created proposal checks do not run | Pending with explicit validation/approval remedy; no merge/check bypass. |
| GitBook writes back into sync mirror | Drift against protected accepted digest; no reverse merge or silent repair. |
| Tag moved/deleted, evidence mismatch | Integrity failure; quarantine proposal, never substitute current tag silently. |
| Manual page mutation/missing history | Retain drift evidence; explicit reviewed repair only. |
| Correction after signing | Forward release correction; any public clarification is visibly dated/noncanonical and cannot replace signed notes. |
| Revoked token/wrong sync branch | Actionable diagnostic and pending status; no broader-permission fallback. |

## Red-First Verification and Activation

Before Green, use existing tests/disposable repositories and process/adapter seams to prove premature/unverified/embargoed publication rejection, all-line union, `1.9`/`1.10` ordering, equal dates, prereleases, exact reruns, moved tags, missing evidence, path attacks, marker corruption, write failure, concurrent CAS, crash recovery, drift and read-only checks. Add three-dispatch coalescing with an unpublished middle release, replay after absent dispatch, changed-union stale approval, missing bot checks and mirror write-back. Subscribe to exact state events before triggering interleavings; no fixed sleeps, polling delays or timing luck. Assert product output/state, not source text or `Received(1)` calls.

Activation evidence must cover existing two-person trust bootstrap, immutable bundle promotion, signer roots, protected refs/environment approval; actual acceptance/mirror separation and branch-specific app rights; GitBook mapping/initial direction with stable keys; public page route/anchors/callouts at desktop/mobile and at the chosen size budget; actual bot required-check execution; safe contributor previews; docs-only writes excluded from deployment; maintenance/coalesced-release reconciliation; outage retry; durable evidence download. Offline tag verification must survive branch movement/deletion. A test-generated signer or dummy engine DLL cannot satisfy production bundle promotion.

Automated checks stay in existing release/adapter projects. Live GitBook settings and delivery confirmation are separate operator activation evidence, not a hidden browser/app-start step in every implementation phase. Until recorded, public docs call publication prospective/unverified.

Ship public adopter upgrade/changelog help with internal runbook/checklist/policy, CI governance, relevant Operations headings, adapter contract and ADR-025 changes. Cover fresh setup, rotation, upgrade and disable/recovery without invalidating releases. Do not duplicate raw configs across docs.

## I-VSD and Provenance

Revalidate `islamic-value-sensitive-design/i-vsd-release-governance.md` for the exact new triad; map actual IDs for public-record truth, human embargo timing, identity privacy and offline verification. Its current findings use headings; do not invent IDs or approval.

Interface facts accessed 2026-09-10: [GitBook monorepos](https://gitbook.com/docs/docs-as-code/git-sync/monorepos), [content configuration](https://gitbook.com/docs/docs-as-code/git-sync/content-configuration), [troubleshooting](https://gitbook.com/docs/docs-as-code/git-sync/troubleshooting), [GitHub concurrency](https://docs.github.com/en/actions/how-tos/write-workflows/choose-when-workflows-run/control-workflow-concurrency), and [workflow triggering](https://docs.github.com/en/actions/how-tos/write-workflows/choose-when-workflows-run/trigger-a-workflow). These explain interface constraints, not this installation's settings. The mirror and inventory are independent repository-native decisions; no third-party implementation source, snippets/assets/prose or dependencies imported. Changed publication authority requires I-VSD revalidation; do not infer approval.

## Quarantined Renderer Hardening Prerequisite

Owner: release-engine maintainer; schedule a separate narrowly scoped correction before this publisher claims literal title display or generic template safety. At reviewed source `a312b9190`, `PresentationConfigGrammar` records loop presence without balancing/counting; `GitCliffRenderer` checks `EscapeUntrustedMarkdown(...).IsValid` but discards escaped output. These are pre-existing gaps, not a proven runtime exploit and not tasks hidden inside the category-formatting commit.

During follow-up planning, choose literal-title semantics explicitly and apply escaping once at presentation boundaries without altering canonical context or double-escaping composed sections. Red cases must exercise real output for Markdown punctuation, existing forbidden identity/HTML input, duplicate/nested/reversed loops and any supported new template form. Keep policy validation, presentation and trusted-bundle promotion separate. Graduate this prerequisite into its own atomic commit contract before implementing the publisher; do not create a generic parser or a compatibility template mode.

Done means the single page and publication/recovery path are implemented, verified and activated. A formatter-only PR, print-only workflow or successful no-op cannot satisfy this workstream.
