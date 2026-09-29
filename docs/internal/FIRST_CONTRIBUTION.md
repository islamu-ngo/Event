ABOUTME: First contribution guide with short docs-only and small-bug paths.
ABOUTME: Links to authoritative contributing and testing docs without duplicating governance rules.

# First Contribution

> **Audience:** Contributors | AI agents
> **Status:** Implemented
> **Owner:** Contributor Experience
> **Last Verified:** 2026-05-06
> **Source Anchors:** `docs/CONTRIBUTING.md`, `docs/TESTING.md`, `.github/PULL_REQUEST_TEMPLATE.md`, `.github/ISSUE_TEMPLATE/`

This guide is the shortest safe path for a first contribution. Use [CONTRIBUTING.md](CONTRIBUTING.md) for the full workflow and [TESTING.md](TESTING.md) for the complete test matrix.

## Path 1: Docs-Only PR

Use this path for typo fixes, stale doc corrections, missing links, or source-grounded documentation updates.

1. Pick a small documentation issue or open a Documentation issue in GitHub using the documentation issue form.
2. Edit only the relevant docs and keep claims tied to source anchors.
3. If you add a new primary documentation, include the metadata block from [DOCUMENTATION_ARCHITECTURE.md](DOCUMENTATION_ARCHITECTURE.md).
4. In the pull request template, set documentation impact to `Updated` and summarize the reviewed links and source anchors.

## Path 2: Small Bug PR

Use this path for a narrowly scoped fix with clear reproduction and one affected area.

1. Start from a bug report that includes reproduction steps, expected behavior, actual behavior, and affected branch.
2. Read the closest source docs before editing. Start with [QUICK_REFERENCE.md](QUICK_REFERENCE.md), then the feature or layer doc linked from [index.md](index.md).
3. Add or update the smallest relevant test when behavior changes.
4. Run the build and the smallest relevant project tests. At minimum, run architecture checks:

   ```bash
   dotnet build --configuration Release --verbosity quiet
   dotnet test --project Event.Architecture.Tests/Event.Architecture.Tests.csproj --configuration Release --verbosity quiet
   ```

5. If the fix touches a specific project, run that project directly. Examples:

   ```bash
   dotnet test --project Event.Application.UnitTests/Event.Application.UnitTests.csproj --configuration Release --verbosity quiet
   dotnet test --project Event.API.IntegrationTests/Event.API.IntegrationTests.csproj --configuration Release --verbosity quiet
   dotnet test --project Explore.Blazor.Client.Tests/Explore.Blazor.Client.Tests.csproj --configuration Release --verbosity quiet
   ```

Do not use solution-level `dotnet test`. This repository runs test projects individually.

## PR Checklist For First-Time Contributors

- Keep the scope independently reviewable.
- Include exact validation commands and results.
- Record documentation impact as `Updated`, `Not needed`, or `Deferred`.
- Attach screenshots for UI changes.
- Call out migrations, configuration, secrets, deployment, backup/restore, or release impact.
- Update active dev docs or handoff notes if the work spans sessions.

## Related

- [CONTRIBUTING.md](CONTRIBUTING.md) — full contribution workflow.
- [TESTING.md](TESTING.md) — test projects, TUnit rules, and validation lanes.
- [DOCUMENTATION_ARCHITECTURE.md](DOCUMENTATION_ARCHITECTURE.md) — metadata, source anchors, and docs impact contract.
- [RELEASE_CHECKLIST.md](RELEASE_CHECKLIST.md) — release-impacting change checklist.
