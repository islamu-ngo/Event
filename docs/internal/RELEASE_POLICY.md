<!-- ABOUTME: Defines normative provider-neutral release invariants and their authority boundaries. -->
<!-- ABOUTME: Keeps ISLAMU policy independent from renderers, providers, and candidate-controlled inputs. -->

# Release Policy

> **Status:** Approved prospective policy; no release automation is active
> **Owner:** Platform/Ops and Project Steward

## Scope and precedence

This policy governs the future release engine. It does not replace the current manual
[Release Checklist](RELEASE_CHECKLIST.md) before activation. In a conflict, this
policy governs release invariants; [ADR-025](adr/ADR-025-provider-neutral-release-governance.md)
explains their architecture; [RELEASE_RUNBOOK.md](RELEASE_RUNBOOK.md) defines operator
steps.

## Governed release contract

- **The tag is the release.** `refs/tags/v<major>.<minor>.<patch>[-prerelease]` MUST be
  the sole immutable release identity. Attestation MUST read only the annotated tag
  object, the preparation commit `B` it targets, the tree at `B`, and ancestry from the
  base tag. Attestation MUST NOT resolve, compare against, or require any ref under
  `refs/heads/*`, so a release stays verifiable after the branch that carried its
  commits advances, is deleted, or was never fetched.
- Compare-and-swap against an observed branch head is permitted only inside a
  **mutating** step — the `main` fast-forward proposal and provider push preconditions —
  where a stale ref is a genuine race. It MUST NOT appear in any verification path.
- Every release MUST declare a version-line **label** `v<major>.<minor>`. The label
  classifies the release; it is not a ref and nothing may derive a branch name from it.
  `develop` MUST NOT receive generated `Unreleased` changelog writes.
- ISLAMU policy MUST decide commit validity, visibility, impacts, grouping inputs,
  SemVer, range/tag selection, trust, and authoritative evidence. git-cliff MUST render
  normalized context only and MUST NOT decide any of those matters.
- Normalized input MUST be provider-neutral. Provider metadata MAY appear only in a
  separate, non-authoritative publication view and MUST NOT alter content hash.
- The trusted release bundle MUST contain the authoritative engine, policy, context
  contract, renderer configuration, tool pin, and signer roots. Final attestation
  MUST verify that promoted bundle before reading candidate data.
- Candidate source, templates, policy, renderer config, tool locks, and trust roots
  MUST NOT self-promote or influence authoritative attestation.

## Release sources and exact identity

- `release.yaml` MUST contain the selected version, release line, fixed release date,
  range/base references, compatibility references, and impact dispositions.
- `summary.md` MUST be the sole maintainer-owned public narrative.
- `release-notes.md` MUST be fully generated from the validated sources and
  normalized context. It MUST NOT contain manually maintained generated regions.
- A release MUST finish at one preparation commit `B`. Candidate evidence, the signed
  annotated tag target, and the stable `main` target for the newest stable release MUST
  equal the same full Git object `B`. No branch head is part of that equality.
- Replacement, squash, merge, rebase, or regeneration drift MUST invalidate the
  candidate and require a new reviewed `B`, because each produces a different object.

## Change identity allocation and correction

- New public change identifiers MUST use the release engine's sortable
  ULID-style `CHG-<26 Crockford Base32 characters>` format. Sequential
  `CHG-<year>-<number>` identifiers are immutable historical inputs only and
  MUST NOT be allocated for new work.
- Fragment creation and footer emission MUST be one operation. The generated
  identifier is accepted only when it is absent from all visible local refs,
  existing fragments, and commit-bound correction records.
- Commit-time validation MUST reject a proposed footer already reachable from
  the configured target. Pre-merge validation MUST compare the complete
  `target..head` range before conflict resolution and reject target collisions,
  duplicate feature IDs, or missing/mismatched fragments.
- An immutable commit footer MAY be corrected without rewriting history only
  through one `change-id-rename.v1` record named by and bound to the exact full
  commit object ID. The record MUST state the old ID, a generated replacement
  ID, and a bounded reason. It is valid only while the bound commit still
  carries the old footer.
- A correction record MUST NOT be a branch alias, wildcard, range mapping,
  reused replacement, or authority to reinterpret any other commit. Candidate
  preparation applies the replacement before fragment linkage and records the
  resulting commit OID plus effective Change-Id in authoritative context. Relevant
  correction records are included in the candidate's governed release-source
  digest together with linked fragments.

## Ref namespace

- Version tags own the `v*` glob outright. Creating any branch matching
  `refs/heads/v*` MUST be rejected — by provider protected-ref settings and by
  `ReleaseRefNamespacePolicy` — so no branch can shadow or be confused with a version
  tag. This is enforced as policy, never resolved as an ambiguity after the fact.
- Maintenance branches MUST be named `release/<major>.<minor>`. They are **lazy**: none
  is created at release time. One MAY be opened on demand when a real backport is
  required, and its only legal source is a verified signed stable tag on that line.
  Deleting it afterwards is supported and MUST leave every release on that line fully
  verifiable, because every release is closed by its tag.

## Trust, determinism, and privacy

- The renderer MUST run from the trusted bundle with explicit configuration,
  `--offline`, and `--no-exec`; it MUST NOT use provider APIs, network configuration,
  or command processors.
- Primary file MUST use UTF-8 without BOM, LF, NFC, invariant ordering, a fixed
  `release.yaml` date, and no current-clock fields. Evidence MUST retain full object
  IDs; abbreviated display IDs require collision-safe validation.
- Release tags MUST be SSH-signed annotated tags. Lightweight, unsigned,
  unauthorized, revoked, or replaced tags MUST fail verification.
- The first governed release MAY use one explicit lower-bound tag named exactly
  `changelog-baseline-YYYY-MM-DD` instead of SemVer history, but only after an
  operator verifies that annotated SSH-signed tag with the promoted bundle and records
  both the full tag object ID and full target commit ID in `release-baseline.v1`
  evidence. This baseline is not a release, is not SemVer history, and MUST be ignored
  by strict SemVer release-tag discovery. The selected version remains an external
  steward approval decision; tooling MUST NOT infer first-release eligibility from a
  hardcoded version number.
- Fake SemVer baselines such as `v0.0.0`, lightweight tags, unsigned tags,
  unauthorized signers, recreated/moved tags, wrong dates, wrong targets, and short
  object IDs MUST fail closed. If any governed stable SemVer tag is already reachable
  from the candidate, baseline lower bounds MUST fail closed and the release MUST use
  stable SemVer tags as its lower bound.
- Candidate and final manifests MUST be distinct: the pre-tag candidate digest MAY
  be referenced by tag text, while post-tag evidence MUST record the tag object ID.
- Release artifact MUST omit author/committer identities, raw commit bodies,
  provider identities, tokens, and unbounded error text.

## Disclosure and operation

- Breaking changes MUST NOT be skipped. Every nonbreaking skip MUST include a
  reviewable reason.
- High-impact, grouped, migration, configuration, OpenAPI, operator, and public
  security changes MUST have the structured public evidence required by the release
  engine. Simple low-impact changes MAY remain commit-subject driven.
- Embargoed security information MUST remain outside the public checkout and normal
  public artifacts until authorized disclosure. A public release MAY include only
  approved disclosure fields.
- The tool MUST verify and emit evidence; it MUST NOT approve, tag, push, publish,
  or deploy. A provider adapter MAY perform protected operations only after the
  independent verification and operator-approval boundaries hold.
- The historical report design has not shipped. Implementations MUST NOT add
  compatibility shims for its split-output, candidate-`A`, or renderer-policy model.

## Prospective public changelog publication

Publication MUST remain described as prospective/unverified until retained
operator evidence proves protected acceptance controls and live GitBook
synchronization. A local renderer, workflow definition or branch name MUST NOT
be treated as activation evidence.

- The public changelog MUST contain dated release entries only. General adopter
  help MUST live separately; projection content MUST remain non-authoritative.
- The final lane MUST retain a complete finally verified, human-authorized
  inventory before dispatch, pinned to full annotated tag-object IDs and durable
  final evidence. The publisher MUST NOT grant disclosure authorization.
- Reconciliation MUST combine complete retained inventory with last accepted
  identities across all lines, including absent/coalesced dispatches. Missing
  inventory, missing evidence, omitted accepted history or moved/deleted tag
  identities MUST fail closed.
- Protected `docs/publication` acceptance MUST require independent review and
  checks without GitBook/publisher bypass. Mutations MUST enforce expected-old
  commit CAS; changed input unions/digests MUST receive fresh review.
- GitBook MUST synchronize through separate mutable `docs/gitbook-sync` only.
  Any app bypass MUST require operator approval restricted to that mirror;
  write-back MUST NOT automatically merge into protected acceptance. Drift MUST
  be preserved and repaired only after explicit review.
- Manifests and receipts MUST retain pinned identities, input/projection digests,
  accepted commits and bounded outcomes beyond expiring artifacts. Delivery
  MUST require observed live content, not merely acceptance or a mirror push.
- Required bot checks MUST actually execute; missing checks MUST leave
  publication pending, never authorize a bypass. Preview lanes MUST remain
  secret-free; docs-only writes MUST NOT deploy runtime artifacts.
- Rotation, retry, publisher upgrade and disable/recovery MUST preserve signed
  identity. Publication failure MUST NOT invalidate releases, retag, rebuild
  binaries, move stable `main` or rewrite authoritative notes.

The operator gates are in the
[activation checklist](RELEASE_CHECKLIST.md#prospective-changelog-publication-activation)
and [runbook](RELEASE_RUNBOOK.md#governed-changelog-publication-and-activation).
