<!-- ABOUTME: I-VSD planning report for upgrading the Event solution to .NET 11 and accepting pre-release NuGet packages safely. -->
<!-- ABOUTME: Evaluates operational Amanah (trustworthiness), Raf' al-Haraj (operator/maintainer burden), Sidq (honest readiness signaling), and license stewardship. -->

# .NET 11 SDK & Runtime Upgrade — I-VSD Planning Report

Last Updated: 2026-11-12

## Review Metadata
- Mode: planning
- Subject: .NET 11 (`net11.0`) SDK, runtime, EF Core 11, OpenAPI 3.x, Blazor WASM, and NuGet pre-release dependency governance
- Workstream: dotnet-11-upgrade
- Report kind: planning report
- Report status: current
- Disposition: plan-aligned
- Evidence cutoff: 2026-11-12
- Reviewed input: dev/next/dotnet-11-upgrade/dotnet-11-upgrade-plan.md (initial planning revision aligned with dotnet-11-upgrade-context.md and dotnet-11-upgrade-tasks.md)
- Supersedes: none

## Executive Summary

Upgrading Event from `.NET 10` (LTS) to `.NET 11` (STS, released November 10, 2026) affects self-hosting mosque/community operators, contributors building containers, and automated release pipelines. Because `.NET 11` introduces breaking changes in EF Core 11, `Microsoft.OpenApi` 3.x, and Blazor WASM workloads — and because critical database providers (`Microting.EntityFrameworkCore.MySql 11.0.0-preview.1`, `Npgsql.EntityFrameworkCore.PostgreSQL 11.0.0-rc.3`) exist only as pre-releases while `EFCore.NamingConventions` still caps at `< 11.0.0` — rushing or loosely floating pre-release dependencies would risk database corruption, silent build regressions, or license/supply-chain surprises for non-profit Muslim event operators.

This report recommends proceeding via a gated, two-stage architecture parked in `dev/next/` until upstream dependencies catch up: (1) executing unblocked prerequisite modernization on `.NET 10` immediately (retiring dead `EntityFramework 6.5.2`, replacing archived `Blazored.FluentValidation` with MIT-licensed `Blazilla`, bumping `Quartz`/`Aspire`/`bunit`, and parameterizing hardcoded `net10.0` tool paths), and (2) gating the `net11.0` cutover on exact-version pre-release pinning, `packages.lock.json` deterministic lockfile verification, license-policy scanning, OpenAPI `3.1` stability for client codegen, and an explicit GA graduation backlog tracker before production `v1.0` release. `plan-aligned` status certifies traceability to plan tasks and scenarios; it is not runtime or multi-database execution proof.

## Scope

In scope:
- Provider-controlled decisions on SDK/TFM cutover (`net10.0` → `net11.0`), pre-release (`-rc.*` / `-preview.*`) package acceptance, lockfile pinning, database compatibility (`SQL Server compat_level 160`, PostgreSQL, MySQL/MariaDB, SQLite), OpenAPI contract preservation (`OpenApi 3.1` vs `3.2`), Blazor WASM validator replacement (`Blazilla`), container base-image digest pinning (`validate-dockerfile-base-images.cs`), and outbound license compliance (`validate-dependency-license-policy.cs`).
- Impacts on community event organizers, self-hosters (`Event.Standalone`, `Event.MigrationService`, Docker images), contributors, and maintainers.

Out of scope:
- Religious-legal (`fiqh`) rulings (none apply to runtime framework selection).
- Product feature additions, end-user authentication flow changes, payment processing rules, or privacy policy modifications.

## Claim Boundary

Findings trace directly to repository inspection of all 51 `.csproj` files, `Directory.Packages.props`, `Directory.Build.props`, `global.json`, `dotnet-tools.json`, `NuGet.Config`, `.ci/scripts/validate-dependency-license-policy.cs`, `.ci/scripts/validate-dockerfile-base-images.cs`, `vendor/secretkit/`, and NuGet/Microsoft release metadata verified on 2026-11-12. Upstream NuGet package versions (particularly `EFCore.NamingConventions` `11.0.0-*`, `Npgsql`, and `Microting`) reflect state as of 2026-11-12 and remain subject to upstream publication before Phase 2 starts.

## Findings

| ID | Lifecycle | Severity | Claim type | Principle / Domain | Stakeholder | Provider-controlled decision | Evidence | Validation | Mitigation |
|---|---|---|---|---|---|---|---|---|---|
| `IVSD-F001` | open | High | Reliability / Trust | Amanah (trustworthy stewardship), La Darar (harm prevention) | Community organizers, attendees, self-hosters | Whether to allow `-preview.*` / `-rc.*` NuGet packages (`Npgsql`, `Microting`, `EFCore.NamingConventions`) for production-bound database drivers without explicit pin and GA graduation controls | `Directory.Packages.props`, `Microting` `11.0.0-preview.1`, `Npgsql` `11.0.0-rc.3`, `EFCore.NamingConventions` `10.0.1` (`< 11.0.0`) | Verified from `.nuspec` & NuGet metadata | `IVSD-M001` |
| `IVSD-F002` | open | High | Truthfulness / Continuity | Sidq (truthfulness in claims), Maslahah (operational continuity) | Self-hosting operators (SQL Server / MySQL / PostgreSQL), contributors | Whether adopting .NET 11 STS + EF Core 11 SQL Server compat level `160` default and new EF `Migrate()` / split-query exceptions could silently break self-hosters running older SQL Server instances or unmigrated DBs | EF Core 11 breaking changes (`UseSqlServer` compat 160, `Migrate()`, split-query errors), `PrimaryDatabaseProviderComposition.cs:195`, `AdmissionAuthorityProviderFixture.cs:101` | Verified code & Microsoft breaking-change notes | `IVSD-M002` |
| `IVSD-F003` | open | Medium | Security / Licensing | Amanah (license integrity), Adalah (fair IP compliance) | Downstream FOSS adopters, maintainers, contributors | Whether replacing `Blazored.FluentValidation` (`2.2.0` archived/EOL), updating `Quartz.Dashboard` (`4.x`), or pulling .NET 11 transitive runtime packages introduces unreviewed licenses or bypasses `.ci/scripts/validate-dependency-license-policy.cs` | `validate-dependency-license-policy.cs:377-411` (exact pins e.g. `Microsoft.Data.SqlClient.SNI.runtime` `6.0.2`), `Blazilla` MIT license, `Quartz.Dashboard` Apache-2.0 | Verified script & NuGet license metadata | `IVSD-M003` |
| `IVSD-F004` | open | Medium | Harm / Hardship | Raf' al-Haraj (avoiding unnecessary burden), Ihsan (engineering rigor) | Self-hosters consuming OpenAPI / contributors building Blazor WASM | Whether letting `Microsoft.AspNetCore.OpenApi 11.0.0` switch silently to OpenAPI `3.2` breaks NSwag `14.x` client generation or external integrator tooling | `schemas/openapi_islamu-event.json` (`3.1.1`), `nswag.pertag.json`, `ApiHostServiceCollectionExtensions.cs:436` | Verified repo OpenAPI config & .NET 11 default shift | `IVSD-M004` |

## Recommendations

- `IVSD-M001`: **Bounded Pre-Release Acceptance & GA Graduation Gate** — Allow pre-release packages (`-rc.*` / `-preview.*`) ONLY as exact, non-floating pins in `Directory.Packages.props` with full deterministic `packages.lock.json` lockfiles (`RestoreLockedMode=true` enforced in CI), passing `.ci/scripts/validate-nuget-vulnerabilities.cs`, deferred behind `dev/next/` until `EFCore.NamingConventions` `11.0.0-*` is available, and paired with a tracked `dev/backlog/dotnet-11-prerelease-ga-graduation.md` item blocking public `v1.0` GA tags while any database driver remains `-preview`/`-rc`.
  - *Rejected alternative:* Floating wildcard pre-releases (`11.0.0-*`) or bypassing lockfiles — rejected because non-deterministic restores violate reproducible build Amanah and CI lockfile policy.
- `IVSD-M002`: **Operator Compatibility & STS Lifecycle Transparency** — Document `.NET 11` STS lifecycle (support ending Nov 2028 vs `.NET 10` LTS ending Nov 2028/2029) and EF Core 11 database requirements (SQL Server 2022 / `compat_level 160` default, or explicit `UseCompatibilityLevel(150)` guidance for older self-hosted SQL Server 2019), enforce full 5-provider Ring 3 verification (`PostgreSQL`, `SQLite`, `SqlServer`, `MySQL`, `MariaDB`), and emit a Tier-2 public change fragment (`CHG-...`) detailing operator upgrade obligations.
  - *Rejected alternative:* Silent upgrade without operator release notes or dropping multi-provider verification — rejected because non-profit operators deserve honest notice before runtime upgrades.
- `IVSD-M003`: **Clean-Room & License Gate Parity** — Standardize on MIT-licensed `Blazilla` `2.4.2` (removing abandoned `Blazored.FluentValidation` and dead `EntityFramework 6.5.2`) and Apache-2.0 `Quartz 4.x`, and update/verify `.ci/scripts/validate-dependency-license-policy.cs` + `validate-dockerfile-base-images.cs` after regenerating all lockfiles (`including src/Event.Standalone/container/packages.lock.json`).
  - *Rejected alternative:* Copying external MIT validation source files inline or waiving license-policy checks — rejected to preserve clean dependency provenance and automated license enforcement.
- `IVSD-M004`: **Pin OpenAPI `3.1` & Preserve Parity Baseline** — Explicitly configure `options.OpenApiVersion = OpenApiSpecVersion.OpenApi3_1` on `AddOpenApi("islamu-event")` while upgrading `Swashbuckle.AspNetCore` to `11.0.0` (`Microsoft.OpenApi 3.x`), preserving `schemas/openapi_islamu-event.json`, NSwag codegen, and `OpenApiParityTests` cleanly during `.NET 11` cutover.
  - *Rejected alternative:* Forcing immediate OpenAPI `3.2` cutover and ripping out Swashbuckle inside the same SDK upgrade — rejected as unnecessary blast-radius expansion that mixes runtime migration with breaking public OpenAPI tooling changes.

## Stakeholders

- **Muslim community organizers & attendees:** Depend on database migration reliability, uninterrupted event registration, and bug-free WASM admin forms (`Blazilla` validators).
- **Self-hosting mosque/non-profit operators:** Depend on digest-pinned OCI containers (`sdk:11.0`, `aspnet:11.0`, `runtime-deps:11.0`), clear SQL Server/PostgreSQL/MySQL version expectations, and honest STS vs LTS lifecycle disclosures.
- **Contributors & maintainers:** Benefit from clean `.NET 10` prerequisite hygiene now, predictable phased commits, and zero broken intermediate `NU1201` builds.

## I-VSD Principles And Domains

- **Amanah (Stewardship & Reliability):** Enforced via deterministic `packages.lock.json`, digest-pinned container base images, and mandatory GA graduation tracking before production tags.
- **Sidq (Truthfulness):** Enforced via explicit `.NET 11` STS vs `.NET 10` LTS disclosure, honest dependency gate (`EFCore.NamingConventions`), and unskipped multi-provider integration tests.
- **Raf' al-Haraj (Hardship Alleviation):** Staging Phase 0/1 prerequisites on `.NET 10` avoids forcing all dependencies to move in one chaotic big-bang cutover.
- **Domains Marked Non-Applicable:** Monetization/Riba, Content Moderation/Speech, Worship/Fiqh Rituals, and Biometric/Surveillance AI (unchanged by SDK/runtime upgrade).

## Validation Gaps

- Runtime behavior of pre-release `Microting.EntityFrameworkCore.MySql 11.0.0-preview.1` and `Npgsql.EntityFrameworkCore.PostgreSQL 11.0.0-rc.3` against Event's 5-provider migration suite cannot be executed until Phase 2/3 after `EFCore.NamingConventions` `11.0.0-*` availability.
- Exact SHA-256 digests of `.NET 11` MCR base images (`mcr.microsoft.com/dotnet/aspnet:11.0`, `sdk:11.0`, `runtime-deps:11.0-noble-chiseled-extra`) must be resolved at Phase 2 execution time.

## Escalation Needed

- **Human/Maintainer Release Escalation (Pre-Release Graduation):** Before cutting any stable `v1.0` public release artifact off `.NET 11`, maintainers must verify whether PostgreSQL/MySQL EF Core providers have reached GA (`11.0.0`); do not silently ship `-preview` database drivers in a stable release without explicit maintainer sign-off (tracked in `dev/backlog/dotnet-11-prerelease-ga-graduation.md`).
- **Scholarly Escalation:** None required (pure infrastructure, SDK, and software stewardship decision).

## Evidence Reviewed

- `Directory.Packages.props`, `Directory.Build.props`, `src/Directory.Build.props`, `global.json`, `dotnet-tools.json`, `NuGet.Config`, `Explore.slnx`
- All 51 `.csproj` files across `src/` (24), `tests/` (14), `eng/` (13)
- `.ci/scripts/validate-dependency-license-policy.cs`, `.ci/scripts/validate-dockerfile-base-images.cs`, `.ci/scripts/validate-nuget-vulnerabilities.cs`
- `.github/workflows/{_build-test,openapi-contract,performance-smoke,security-tests,test}.yml`
- `src/Explore.API/{Explore.API.csproj,Hosting/ApiHostServiceCollectionExtensions.cs,Hosting/ApiHostApplicationExtensions.cs,Extensions/ServiceCollectionExtensions.cs}`
- `src/Explore.Blazor.Client/{Explore.Blazor.Client.csproj,nswag.pertag.json}`, `src/Event.Standalone/{Event.Standalone.csproj,Hosting/StandaloneSchedulerDashboardExtensions.cs}`
- `src/Explore.Persistence/{Explore.Persistence.csproj,Database/PrimaryDatabaseProviderComposition.cs}`, `vendor/secretkit/README.md`
- `dev/next/dotnet-11-upgrade/{dotnet-11-upgrade-plan.md,dotnet-11-upgrade-context.md,dotnet-11-upgrade-tasks.md}`

## Missing Evidence

- Live `dotnet build` / `dotnet test` execution on `.NET 11` SDK (blocked until `EFCore.NamingConventions 11.0.0-*` catch-up gate and user execution approval from `dev/next/`).

## Context Inventory

- Related workstreams: `dev/next/dotnet-11-upgrade/`, `event/work/active-dotnet-11-upgrade.yaml`
- Related governance: `docs/internal/legal/IP_GOVERNANCE.md`, `docs/internal/adr/ADR-secretkit-event-ownership.md`, `docs/internal/releases/changes/README.md`

## Planning Handoff

- Workstream: `dotnet-11-upgrade`
- Status: `current`
- Reviewed input: `dev/next/dotnet-11-upgrade/dotnet-11-upgrade-plan.md`
- Findings and mitigations: `IVSD-F001 -> IVSD-M001`, `IVSD-F002 -> IVSD-M002`, `IVSD-F003 -> IVSD-M003`, `IVSD-F004 -> IVSD-M004`
- Required plan mappings:
  - `IVSD-F001` / `IVSD-M001` -> Plan §§3, 5, 9, Phase 2 & Phase 3 (`dev/backlog/dotnet-11-prerelease-ga-graduation.md`)
  - `IVSD-F002` / `IVSD-M002` -> Plan §§3, 5, 9, 13, Phase 1, Phase 3 (`CHG-...` change fragment & operator docs)
  - `IVSD-F003` / `IVSD-M003` -> Plan §§5, 9, Phase 0 & Phase 3 (`Blazilla`, `Quartz`, `validate-dependency-license-policy.cs`, `validate-dockerfile-base-images.cs`)
  - `IVSD-F004` / `IVSD-M004` -> Plan §§3, 5, 9, Phase 2 (`OpenApiSpecVersion.OpenApi3_1`, `Swashbuckle.AspNetCore 11.0.0`, NSwag parity)
- Escalations required before: release (`v1.0` GA sign-off if any DB provider remains `-preview`/`-rc`)
- Refresh triggers: Abandonment of `EFCore.NamingConventions`; license change on any upgraded NuGet dependency; decision to retire Swashbuckle or force OpenAPI `3.2`; dropping `.NET 11` STS in favor of waiting for `.NET 12` LTS.

## Review Lifecycle

| Date | Previous status | New status | Trigger | Evidence/replacement |
|---|---|---|---|---|
| 2026-11-12 | none | current (plan-aligned) | Integrated I-VSD planning intake & triad validation | Initial workstream triad in `dev/next/dotnet-11-upgrade/` |
