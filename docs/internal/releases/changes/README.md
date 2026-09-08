<!-- ABOUTME: Documents immutable public change-fragment YAML for high-impact release entries. -->
<!-- ABOUTME: Defines Change-Id, Backport-Of, impacts, grouping, supersedes, and embargo boundaries. -->

# Public Change Fragments

Public change fragments carry reviewed release facts that do not belong in commit trailers: breaking changes, security disclosure metadata, migrations, configuration changes, OpenAPI changes, operator impact, backports, and deterministic multi-commit grouping.

Fragments are append-only after merge. Do not delete or mutate an existing fragment. To correct one, add a new fragment and list the old `Change-Id` in `Supersedes`.

## Example

```yaml
Change-Id: CHG-01K3Q8Y7M6N5P4R3T2V1W0X9ZA
Title: Attendee credential migration
Type: feat
Scope: registration
Summary: Attendees use a single credential during check-in.
Group: registration-upgrade
Backport-Of: 0123456789abcdef0123456789abcdef01234567
Supersedes: []
Impacts:
  Breaking:
    Reference: docs/internal/releases/README.md
    Disposition: documented
    Detail: Check-in integrations must send credential after upgrading.
  Security:
    Reference: SECURITY.md
    Disposition: coordinated
    Public-Disclosure: coordinated
  Migration:
    Reference: docs/RELEASE_RUNBOOK.md
    Disposition: planned
  Configuration:
    Reference: docs/CONFIGURATION.md
    Disposition: not-applicable
  OpenAPI:
    Reference: docs/API_CHANGELOG.md
    Disposition: documented
  Operator:
    Reference: docs/RELEASE_CHECKLIST.md
    Disposition: documented
```

Rules:

- New `Change-Id` values use the sortable collision-resistant
  `CHG-<26-character Crockford Base32 ULID>` form emitted by
  `create-change`. Historical `CHG-<year>-<number>` values remain valid only
  because existing Git provenance is immutable; they are never allocated for
  new work.
- Run `preflight-range --target develop --head HEAD` before merging. A feature
  ID already reachable from the target fails before conflict resolution.
- A correction for an immutable colliding footer is an exact-commit
  `change-id-rename.v1` record under
  `docs/internal/releases/change-id-renames/`, never a loose alias or history rewrite.
- `Backport-Of`, when present, is the full original Git object ID, not a short display ID.
- `Type` and `Scope` follow the release commit policy registries.
- `Group` is optional and deterministic; fragments in one group must share a compatible public scope.
- Every fragment must include structured `Breaking`, `Security`, `Migration`, `Configuration`, `OpenAPI`, and `Operator` impact objects with `Reference` and `Disposition`.
- Public fragments must not include embargoed disclosure values, restricted-detail markers (including confusable spellings), Unicode control/format ambiguity, secret shapes, or forge identity metadata. Embargoed details belong in the later restricted security lane outside the public checkout.
- Secret checks are bounded to recognizable private-key headers, bearer/provider token prefixes, and credential assignments. Forge checks are bounded to forge URLs, `@handle` attribution, and numeric run/job/pipeline metadata; ordinary product-name and email prose remains valid.
- Unknown YAML keys fail closed so misspelled policy fields cannot silently pass.

Low-impact feature and fix commits do not need a fragment unless they need grouping or one of the required high-impact categories applies.
