<!-- ABOUTME: Records existing CI prerequisite failures separately from Unicode search implementation. -->
<!-- ABOUTME: Defines restore-cache and executable-build evidence needed before affected verification jobs can run. -->

# CI Verification Tool Prerequisites

Last Updated: 2026-09-07 Europe/Brussels

Status: open, quarantined under PR #38's unrelated-failure rule. Owner: CI/tooling maintainer. No Unicode runtime, migration or dependency change is required by the observed failures.

## Observed Failures

At merged develop revision `d6b1e17ed0f4a4e924705d4e0122be975a69dcce`:

- [Build & Test run](https://github.com/islamu-ngo/Event/actions/runs/34117417266) stops at Dependency License Policy Audit. It cannot find local NuGet metadata for `Microsoft.Extensions.DependencyInjection.Abstractions`10.0.9 referenced by `eng/release/dependencies/terminal-gui/probe/packages.lock.json`. The validator scans this lock, but ordinary solution restore does not restore the probe. This is incomplete verification input, not an observed forbidden-license decision. Downstream database/integration jobs are skipped, not passed.
- [OpenAPI run](https://github.com/islamu-ngo/Event/actions/runs/34117416851) cannot start the absent `Explore.ApiContractInventory` executable. Its workflow builds the API/API tests and then invokes the inventory tool with `--no-build`, without building that tool first.

Inspection found the relevant probe lock/validator and missing-build sequence already present at pre-Unicode revision `3f9b796b4a0aea6fff40baaaf88e07add663e863`. This is source-parity plus current remote failure evidence, not a claimed untouched-base rerun. The user prohibited additional worktrees; none was created for attribution.

## Acceptance Criteria

- In an initially empty NuGet cache, restore the complete set of inputs that the license audit deliberately examines; retain fail-closed license/provenance checks rather than excluding a failing input merely to pass.
- Build the contract-inventory tool through its existing project before invoking it without a build; preserve generated-inventory drift and contract checks.
- Verify each corrected workflow from its real cold-cache/tool-absent starting state, then observe native success and actual execution of downstream verification jobs.
- Preserve secrets isolation; logs must not expose credentials or environment values.
- Keep these CI prerequisites separate from Unicode normalization and from the unrelated secret-binding provider uniqueness defect.

The original CI prevention workstream may adopt this bounded prerequisite work during its own intake. A merged PR or a successful local Unicode corpus does not establish whole-pipeline success.
