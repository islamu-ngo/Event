<!-- ABOUTME: Required follow-up for the verified single-page changelog and protected GitBook publication. -->
<!-- ABOUTME: Preserves automation, setup, reader experience and recovery requirements from the CTO refinement. -->

# Governed Changelog Publication

Last Updated: 2026-09-06 Europe/Brussels

Status: **required sequential delivery**, not optional polish. Owner role: Platform/Ops with Documentation maintainer. Predecessor: `dev/active/governed-release-public-changelog/`. Schedule: next PR after categorized notes, before claiming an automated enterprise release experience. Rebaseline into its own triad with exact paths/commits and current I-VSD mappings before implementation.

## Outcome and State Contract

One public document, `docs/public/changelog/README.md`, contains every authorized governed release, newest release date first, with stable version anchors, concise summaries, breaking/upgrade actions, categories and durable evidence links. Reuse GitBook layout/Git Sync; offline generation and verification require no hosted account.

Flow: prepare canonical notes/context → commit `B` → candidate verification → human-controlled signing → final tag verification → docs proposal → protected docs-branch acceptance → observed GitBook delivery.

Keep publication receipts distinct: `verified`, `publication-pending`, `publication-delivered`, `publication-failed`, `publication-drift`. No new domain service is needed. GitBook/forge failure leaves the signed release valid; unsuccessful publication cannot claim delivery. Retry never retags, rebuilds binaries, moves stable `main` or changes canonical notes.

## P1 — Offline Single-Page Generator

Add `sync-public-changelog` write and `--check` modes to the existing C# engine. No arbitrary output path, forge API or token in deterministic composition. Inputs are complete local Git objects, existing final evidence and an explicit complete set of authorized published tags, pinned once to full annotated tag object IDs. Reuse tag/bundle verification. Emit a generated publication manifest recording the pinned input set and projection digest.

Directory existence or unsigned notes is not release proof. Preserve all previously accepted release identities: omission of any existing entry fails closed. A fresh rebuild uses the last accepted manifest plus newly authorized releases, not one branch's directories. Maintenance-line publication must retain newer-line entries. Historical final evidence may live in retained artifacts rather than at `B`; never assume it is committed or regenerate it with today's bundle.

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

Before choosing aggregate size limits, verify current GitBook limits and record the source. Existing engine's 1 MiB cap does not prove hosted support. Add near-limit and rejection cases. Fail explicitly rather than truncating history or silently splitting the user's single document; revisit with measured evidence only if the ceiling is reached.

## P2 — Protected Transport and Setup

Use a dedicated protected docs publication branch (proposed `docs/publication`) containing the full existing `docs/public` tree. Generated history belongs to the publisher; other spaces continue through reviewed docs changes. Do not connect public sync to `develop` or a candidate branch. Do not add docs-only commits after `B` on stable `main`.

Verify actual GitBook repository, branch, configuration root support for `docs/public/gitbook-docs.yaml`, space mapping and initial Git→GitBook direction. Repository YAML cannot attest live settings. Preserve other spaces/routes; review a complete-tree preview before a site-wide branch switch. Git Sync supports editor write-back: branch protection and ownership must stop generated-history edits bypassing checks.

The engine emits a proposal; the adapter owns network, credentials, protected writes and receipts. Reuse `.ci/release/adapter-contract.md`, existing provider definitions and trusted-default-branch final lanes. `.github/workflows/release-publish.yml` currently prints a declaration and reports drift; implement actual transport and fix its `docs/releases` path within this slice. A no-op is not a publisher.

Normal approved release completion dispatches publication through existing final-lane transport, with a manual retry entrypoint. Preserve human signing/disclosure/environment gates, required-check names and SHA-pinned external actions. No automatic version/tag authority. Providers still in discovery-only mode cannot gain protected writes from a manifest flag alone.

One global concurrency group covers all release lines. Use expected-old-commit CAS, never force push or cancel during a write. On conflict reread accepted branch/manifest, recompute the union and retry at most three times; exhausted retries remain pending. Both concurrent releases must survive. Any proposal review binds the pinned tag set and generated digest; stale approval cannot authorize newly discovered tags.

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
| Concurrent maintenance/mainline runs | CAS loser recomputes union; preserve both. |
| Tag moved/deleted, evidence mismatch | Integrity failure; quarantine proposal, never substitute current tag silently. |
| Manual page mutation/missing history | Retain drift evidence; explicit reviewed repair only. |
| Correction after signing | Forward release correction; any public clarification is visibly dated/noncanonical and cannot replace signed notes. |
| Revoked token/wrong sync branch | Actionable diagnostic and pending status; no broader-permission fallback. |

## Red-First Verification and Activation

Before Green, use existing tests/disposable repositories and process/adapter seams to prove premature/unverified/embargoed publication rejection, all-line union, `1.9`/`1.10` ordering, equal dates, prereleases, exact reruns, moved tags, missing evidence, path attacks, marker corruption, write failure, concurrent CAS, crash recovery, drift and read-only checks. Assert product output/state, not source text or `Received(1)` calls.

Activation evidence must cover existing two-person trust bootstrap, immutable bundle promotion, signer roots, protected refs/environment approval; GitBook mapping/initial direction; public page route/anchors/callouts; safe contributor previews; docs-only writes excluded from deployment; maintenance publication; outage retry; durable evidence download. Offline tag verification must survive branch movement/deletion.

Automated checks stay in existing release/adapter projects. Live GitBook settings and delivery confirmation are separate operator activation evidence, not a hidden browser/app-start step in every implementation phase. Until recorded, public docs call publication prospective/unverified.

Ship public adopter upgrade/changelog help with internal runbook/checklist/policy, CI governance, relevant Operations headings, adapter contract and ADR-025 changes. Cover fresh setup, rotation, upgrade and disable/recovery without invalidating releases. Do not duplicate raw configs across docs.

## I-VSD and Provenance

Revalidate `islamic-value-sensitive-design/i-vsd-release-governance.md` for the exact new triad; map actual IDs for public-record truth, human embargo timing, identity privacy and offline verification. Its current findings use headings; do not invent IDs or approval.

Interface facts accessed 2026-09-06: [GitBook setup](https://gitbook.com/docs/guides/editing-and-publishing-documentation/import-or-migrate-your-content-to-gitbook-with-git-sync) selects repository/branch/initial direction; [quickstart](https://gitbook.com/docs/getting-started/quickstart) describes merged updates reaching live docs. These do not prove this installation's settings. No third-party implementation source, snippets/assets/prose or dependencies imported.

Done means the single page and publication/recovery path are implemented, verified and activated. A formatter-only PR, print-only workflow or successful no-op cannot satisfy this workstream.
