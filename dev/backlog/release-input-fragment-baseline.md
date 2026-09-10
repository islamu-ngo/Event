# Release input fragment baseline failure

Status: Quarantined pre-existing failure.
Owner: Release-engine maintainer.
Observed: 2026-09-10, on pristine `origin/develop` at
`a312b9190ba72b609a10b53b1fb6d3850f86d42d`.

## Problem

`ReleaseInputPolicyTests.RepositoryChangeFragmentsPassReleaseInputPolicy`
fails with:

```text
fragment_restricted_detail_marker:CHG-01M1SMGJ33KVXHCN9BHBTMM9ME
fragment_incompatible_group:email-optional-self-hosting
fragment_incompatible_group:database-backed-atproto-auth
```

Before categorized-note implementation, the ordinary release-engine suite had
243 passing tests and this one failure. After implementation and the approved
range-order repair, it had 244 passing tests and the same one failure. No test was
disabled, weakened, or excluded. The fragment data and its validator were not
changed by that workstream.

## Acceptance criteria

- Review the identified fragments and their grouping/disclosure obligations with
  the release maintainer; do not infer that restricted text is safe to publish.
- Correct the source evidence or the validator's demonstrated defect in a
  separately reviewed change. Preserve fail-closed disclosure and incompatible
  group checks.
- Run the specific regression and the owning release-engine suite. The original
  assertion must pass without exclusions, warning suppression, or relaxed
  publication policy.
