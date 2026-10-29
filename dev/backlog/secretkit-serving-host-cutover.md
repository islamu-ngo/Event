# SecretKit serving-host cutover and complete project retirement

Last Updated: 2026-10-25 Europe/Brussels

> **Status:** Deferred dependency-ordered successor programme; not implementation-authorized.
> **Owner:** Event platform and security maintainers.
> **Prerequisite:** Complete the shared acquisition foundation in `dev/active/secretkit-adoption/`, then revalidate I-VSD and approve each successor delivery boundary.
> **Candidate:** `0.1.0-local.20261024.1`; the immutable Event-vendored archives are the execution truth.

## Outcome and activation contract

Complete the originally requested adoption: replace BFF and Data Protection transport, move database bootstrap to Persistence, move tenant-aware resolution and purpose-bound writes to Infrastructure, and delete both obsolete Secrets projects and their build/CI paths. Nothing here authorizes provider provisioning, credential revocation, database reset, new providers, new UI or compatibility shims.

The active triad is narrowed because the original programme has 19 major tasks and independently deliverable acquisition, host, persistence, and runtime boundaries. Use four ordered successor PRs (host admission; database ownership; runtime read/write lifecycle; project retirement). Keep prerequisite commits available on develop before activating a successor. Do not turn this brief into four simultaneous active triads.

## Required corrections before successor activation

1. Rebaseline this preserved design against the then-current code and the exact packages. The detailed task/commit specifications below are retained planning inputs, not executable approval or passing evidence. Freeze literal source/generated paths, fill native release allocation outputs, and write complete path-limited CLI commit packets before activation.
2. Run the owning deterministic Red invariant slice and observe assertion failure before the Green implementation. A compiler error is not Red evidence. Follow with Refactor and the owning targeted Green lane; do not use the previous author-only/phase-end-only convention.
3. The single worst break is cross-purpose or cross-tenant credential publication after authority substitution or an invalidated late completion. Carry the foundation poison-response/partial-publication cases forward and add tenant collision, retained-reference admission and late-fill races before replacing runtime handlers. Tests use barriers and injected time, not sleeps or mock-call mirrors.
4. Bind local revision evidence to the operation/generation that owns it. Token eviction or restart must invalidate outstanding candidates, not merely allocate another equal-looking string. Inventory limits must be measured and bounded. A provider revision is not a binding configuration revision.
5. Preserve post-dispatch mutation evidence outside the canceled waiter; distinguish confirmed effects from cache-invalidation failure, approval-pending, no-dispatch and unknown. A controller failure cannot authorize blind resubmission. Specify an existing operation owner and its evidence lifetime before adapting writer ports.
6. Close the async startup ownership gap: start required token maintenance before slow bootstrap can outlive its token, but never let it reauthenticate. Transfer one lease into the host on success; dispose it on any preparation/Build/start failure. Combined owns one session, specialized Data Protection owns separate admitted reads, and finite/periodic semantics come from actual login-bound policy.
7. Replace only Data Protection transport; keep independent pin/floor admission, partition, lease, purpose and RejectUntilRestart enforcement at the real serving boundary. An unhealthy readiness response alone is not a guarantee that existing traffic stopped. Verify the actual protect/unprotect admission path as well as health.
8. Require actual supported IL container builds for all four deployables before retirement is release-ready; unavailable Docker is a visible release blocker, not a permanently optional success criterion. Non-default trimming/Native AOT remains a distinct optional qualification, not a blocker for supported untrimmed IL.
9. Validate no model change for each actually affected context/provider family, not only ExploreDbContext. No migrations or native rights changes are planned; do not invent a migration to satisfy an ownership move. Apply layer-bounded test selection and the inherited minimum suites once at final programme assurance.
10. Preserve no-credential model/OpenAPI modes and existing SetupLive commitments. No generic retry policy may replay authentication or native mutation; exercise actual transport handlers, redirect/TLS boundaries, and safe output sinks.

### Source-verified successor blockers

- **Persisted approval revisions:** `KeycloakOperationService.cs:856-868` checks persisted binding fingerprints. Do not apply the original "all fences are operation-local" assumption. Qualify stable native evidence or explicitly redesign owner-bound approval/re-review on restart, cross-replica execution and token eviction. `EnvironmentSecretSource.cs:48-57` uses binding metadata/process revision rather than material, so changed Environment/UserSecrets values also need a real fence. No secret-byte hashes or persisted credentials. This remains a runtime activation blocker until the evidence contract is decided and tested.
- **Cross-request invalidation:** `SecretResolutionServiceCollectionExtensions.cs:36-51` shares cache but scopes the resolver, whose `_cacheKeys` index is local. Index and invalidation generations must share cache lifetime while repository access stays scoped. Red tests must use two DI scopes and a blocked late read, not only one resolver instance.
- **Canceled SetupLive writes:** `SetupLiveApplicationService.cs:277-280,1030-1047` currently settles Cancelled without dispatch evidence. Change the actual Application settlement protocol as well as the writer port. Preserve value-free effect evidence outside the canceled waiter; only proven no-dispatch can mean Cancelled.
- **Data Protection facts:** `ExternalDataProtectionRing.cs:39-52` accepts up to 24 hours. Preserve that maximum; the original 12-hour language below describes no verified validator ceiling. `ExternalDataProtectionRegistration.cs:38-62` resolves the provider before hosted-service StartAsync. Prepare earlier. Readiness already fresh-reads and permanently rejects on failure at lines 82-108; async conversion must preserve genuine failure rejection and test pre-attempt versus post-dispatch cancellation explicitly.

## Revision and I-VSD traceability

This brief preserves P1 successor tasks 2.1-5.4 and S01-S12 as refined inputs to triad P2/E2. The I-VSD report `islamic-value-sensitive-design/workstreams/i-vsd-secretkit-adoption.md` is revalidated for the P2/E2 foundation only. Successor activation must remap IVSD-F001/M001 through F005/M005 against then-current code and scope. Foundation approval is not successor approval.

## Preserved successor specifications

All paths are repository-relative unless stated otherwise. Scenario S01-S12 and architecture references are preserved in the standalone sections below; the ignored active triad is execution memory, not the only surviving design source. These sections retain the full original task/acceptance/commit and test-disposition detail so narrowing the active ledger does not discard requirements. The correction list above supersedes incompatible P1 verification/readiness wording below.

## Successor phase 2: BFF and Data Protection transport replacement

**Dependencies:** Phase 1.
**Guidance:** plan Sections 5.3, 5.5-5.6; BFF, auth, diagnostics, tests and Data Protection source contracts.

- Deferred: **2.1 Author host and partition specifications and verify adversarial coverage.**
  - New: `tests/Explore.Blazor.IntegrationTests/Configuration/SecretKitHostAdmissionTests.cs`, `tests/Explore.Blazor.IntegrationTests/Configuration/SecretKitDataProtectionAdmissionTests.cs`.
  - Existing: `tests/Shared/ExternalDataProtectionTestAuthority.cs`; old Data Protection tests remain until their replacement disposition is recorded.
  - Acceptance: S02/S03/S06/S09/S10 exercise wrong partition, provider outage, changed pin/floor, admission expiry, late optional completion and startup cancellation with bounded dynamic canaries.
  - Effort: M. Dependencies: Phase 1.

- Deferred: **2.2 Replace BFF bootstrap and verify async combined-host composition.**
  - Existing: `src/Explore.Blazor/Extensions/ConfigurationExtension.cs`, `Hosting/BlazorHostServiceCollectionExtensions.cs`, `Program.cs`; `src/Explore.API/Hosting/ApiHostServiceCollectionExtensions.cs`, `Program.cs`; `src/Event.Standalone/Program.cs`; their existing host-composition test callers.
  - Delete: `src/Explore.Blazor/Configuration/InfisicalConfigurationProvider.cs` and `InfisicalConfigurationSource.cs`.
  - Map existing BFF keys/receipts through shared preparation. Await before host Build/OIDC composition; Combined uses its explicit union and one authority owner. Add/retain explicit shared UserSecretsId on server owners.
  - Preserve explicit no-I/O build-time OpenAPI generation and controlled Testing authority composition; generated contracts must remain unchanged unless a genuine source contract changed.
  - Acceptance: no BFF backend reference, raw HTTP parser, mandatory IPv4 override, implicit source inference or sync network Load; S01-S03/S06/S10 hold through the host composition seam.
  - Include paired `docs/internal/SECRETS.md` / public `secrets.md` and `infisical.md` explanation of async admission and explicit selector cutover.
  - Effort: L. Dependencies: 2.1.

- Deferred: **2.3 Replace specialized Data Protection transport and verify fresh readiness admission.**
  - Existing: `src/Explore.ServiceDefaults/DataProtection/ExternalDataProtectionSource.cs`, `ExternalDataProtectionRegistration.cs`; specialized tests from 2.1.
  - Add async preparation before provider construction and an awaited fresh bounded read in readiness. Synchronous access is prepared-memory access only. Keep independent process pin/floor, partitions, discriminators, parsers, lease and RejectUntilRestart.
  - Acceptance: S09/S10 hold; a new source read cannot silently replace active ring state; bulk export contains no ring/admission material; failure never authorizes filesystem/Redis/database fallback.
  - Include paired `docs/internal/SELF_HOSTING.md` / public `secrets.md` key-custody and restart guidance.
  - Effort: L. Dependencies: 2.1-2.2.

### Successor phase 2 Verification

- Deferred: Run `dotnet build --configuration Release --verbosity quiet` once and verify exit 0.
- Deferred: Run `dotnet test --project tests/Explore.Blazor.IntegrationTests/Explore.Blazor.IntegrationTests.csproj --configuration Release --verbosity quiet -- --treenode-filter "/*/*/SecretKit*AdmissionTests/*" --minimum-expected-tests 1` once and verify selected host/partition cases pass.

### Planned Commit Contract B

- Type & scope: `refactor(self-hosting)!`.
- Title: `admit BFF and key custody through SecretKit before startup`.
- Description: `The BFF and Data Protection loader currently authenticate and parse Infisical independently during synchronous configuration loading. Prepare their required inputs asynchronously through the selected authority before host construction, and make key-custody readiness perform a fresh bounded admission check. Preserve process partitions, independent pins and floors, and the BFF dependency boundary; remove the unconditional IPv4 override.`
- Changelog treatment: Tier 2, governed operator/security/configuration change.
- Required trailers: native emitted `Change-Id`; `BREAKING CHANGE: Select SECRET_PROVIDER explicitly and restart affected hosts with the documented selected-authority inputs. BFF bootstrap no longer infers authority or performs remote I/O in synchronous configuration loading.`
- Allocation command: `dotnet run --project eng/release/src/ISLAMU.ReleaseEngineering/ISLAMU.ReleaseEngineering.csproj -- create-change --target develop --type refactor --scope self-hosting --title "admit BFF and key custody through SecretKit before startup" --summary "Prepare BFF and Data Protection inputs asynchronously under the explicitly selected authority; preserve partition admission and require coordinated restart for cutover."`
- Commit paths: exact existing/deleted/new files in 2.1-2.3; changed server project files and actual changed locks; mechanically affected host test callers; the four named paired documentation anchors; the newly emitted fragment. Fill the fragment's Breaking, Security, Configuration and Operator references from those actual public/internal guides; Migration/OpenAPI are not applicable.
- Message override: Not overridden.

- Deferred: Allocate/complete B's native fragment, execute B with frozen literal paths and verify the emitted fragment and footer match.

## Successor phase 3: Persistence-owned bootstrap and database configuration

**Dependencies:** Phase 1 and Phase 2's async host composition.
**Guidance:** plan Section 5.7; EF, Clean Architecture, tests and existing model-only tooling contracts.

- Deferred: **3.1 Author database/tooling specifications and verify role isolation assertions.**
  - New: `tests/Explore.Infrastructure.Tests/Secrets/SecretKitDatabaseBootstrapTests.cs`.
  - Retain/move relevant existing pure database tests from the old suite as specified in the disposition table below.
  - Acceptance: S01-S03/S09/S12 cover Runtime versus Migrator separation, no credential access in model-only mode, Production UserSecrets rejection, no raw connection-string fallback and correct structured provider construction.
  - Effort: M. Dependencies: Phase 1.

- Deferred: **3.2 Move database ownership atomically and verify every namespace caller resolves.**
  - Move the nine existing Database files, listed below, and `BootstrapPostgresCredentials.cs` / `BootstrapSecretLoader.cs` into `src/Explore.Persistence/Database/`.
  - Move `HealthChecks/SecretResolverHealthCheck.cs` into `src/Explore.Persistence/HealthChecks/SecretResolverHealthCheck.cs`; its registration moves to host composition so the old project does not acquire a Persistence dependency.
  - Existing owners/callers: `src/Explore.Persistence/Explore.Persistence.csproj`; its Database, Identity, Privacy/ErasureAuthority, Schema and security provider primitives; API configuration/hosting/startup/Quartz composition; AppHost; MigrationService; Standalone; `eng/tools/EmergencyAdminProvisioner.cs`; exact test caller list returned by namespace references.
  - Add only the narrow Persistence -> ServiceDefaults design-time dependency. Remove driver dependencies from the old project when their final owner moves; keep actual provider behavior unchanged.
  - Acceptance: the complete namespace move compiles as one increment, no Persistence -> Infrastructure edge/cycle appears, and no old Database namespace remains in product/tooling callers. No schema, grants or migrations change.
  - Effort: L. Dependencies: 3.1.

- Deferred: **3.3 Replace credential-bearing tooling acquisition and verify model-only paths stay offline.**
  - Existing: `src/Event.MigrationService/Extensions/ConfigurationExtensions.cs`, `Program.cs`, `Worker.cs`; Persistence's main, DataProtection, identity and erasure design-time factories; emergency-admin tool.
  - Await runtime/migration acquisition before dependent construction. At EF's unavoidable synchronous factory interface, isolate a single bounded bridge using explicit Migration purpose; never hide it in reusable configuration Load.
  - Preserve shared UserSecrets ID and document `src/Explore.API` as CLI owner; use no hard-coded credentials to construct a model. Update internal `OPERATIONS.md` / public `secrets.md` and `environment-variables.md` together.
  - Acceptance: model-only execution cannot authenticate; credential-bearing tooling cannot acquire BFF/API sibling roots; S01-S03/S12 hold for all existing context families.
  - Effort: M. Dependencies: 3.2.

### Exact Database Move Set

The source prefix is `src/Explore.Secrets/Database/`; the destination prefix is `src/Explore.Persistence/Database/`. Each row is one exact existing file, not a wildcard.

| File |
|---|
| EmbeddedPrivacyErasureAuthorityOptions.cs |
| PrimaryDatabaseConfiguration.cs |
| PrimaryDatabaseConnectionOptions.cs |
| PrimaryDatabaseConnectionResult.cs |
| PrimaryDatabaseProvider.cs |
| PrimaryDatabaseRole.cs |
| PrimaryDatabaseServerFlavor.cs |
| PrimaryDatabaseTlsMode.cs |
| PrivacyErasureAuthorityDatabaseConfiguration.cs |

### Successor phase 3 Verification

- Deferred: Run `dotnet build --configuration Release --verbosity quiet` once and verify exit 0.
- Deferred: Run `dotnet test --project tests/Explore.Infrastructure.Tests/Explore.Infrastructure.Tests.csproj --configuration Release --verbosity quiet -- --treenode-filter "/*/*/SecretKitDatabaseBootstrapTests/*" --minimum-expected-tests 1` once and verify the in-memory role/tooling cases pass.

### Planned Commit Contract C

- Type & scope: `refactor(database)`.
- Title: `own database bootstrap and configuration in Persistence`.
- Description: `Database drivers and structured credential construction currently live in the generic Secrets project. Move them beside provider composition, preserve explicit runtime and migrator purposes, and reuse shared authority acquisition only at bootstrap and design-time entrypoints. Keep model-only tooling offline and preserve all existing database and Data Protection semantics without a schema migration.`
- Changelog treatment: Tier 3, internal ownership move; operator selector cutover is already recorded by B.
- Required trailers: `Changelog: skip` and `Changelog-Reason: Mechanically indivisible database ownership move with unchanged storage contracts.`
- Commit paths: both old/new paths for all 12 moves in 3.2, exact caller/reference files frozen by 3.2, affected project files/locks, new database specification and migrated pure tests, the three paired tooling docs in 3.3.
- Large-packet justification: splitting the namespace move from its actual caller updates produces an uncompilable tree. Only mechanical callers belong; unrelated fixture improvements do not.
- Message override: Not overridden.

- Deferred: Execute C with frozen literal paths and verify the complete namespace move is represented without unrelated persistence changes.

## Successor phase 4: Tenant resolution and explicit runtime lifecycle

**Dependencies:** Phases 1-3.
**Guidance:** plan Sections 5.4-5.5 and 5.8; Domain/Application, criticality, auth, diagnostics, refactoring and tests guidance.

- Deferred: **4.1 Author runtime invariant-breakers and verify race/effect coverage.**
  - New: `tests/Explore.Infrastructure.Tests/Secrets/SecretKitRuntimeContractTests.cs`; update/move existing resolver, retained-reference, writer, confidentiality and rotation tests.
  - Add Domain policy tests under new `tests/Event.Domain.UnitTests/Secrets/SecretBindingAdmissionTests.cs` only for the extracted pure binding invariant.
  - Acceptance: S01-S08/S10/S11 cover tenant collision, invalidate-versus-late-fill, update/recreation revision equality, waiter cancellation, post-dispatch uncertainty, pending approval, shutdown with held material, and positive output capture.
  - Effort: L. Dependencies: Phases 1-3.

- Deferred: **4.2 Rehome resolver policy and verify native evidence survives Event translation.**
  - Existing moves: `Services/SecretResolver.cs`, `Sources/EnvironmentSecretSource.cs`, `Sources/InfisicalSecretSource.cs`, `Configuration/UserSecretsAuthority.cs`, runtime metrics/status/audit/health and applicable options from the old project into `src/Explore.Infrastructure/Secrets/`.
  - New: `src/Explore.Domain/Secrets/SecretBindingAdmission.cs`; split the binding-dependent policy from process-root policy. Preserve Application business-facing ports; no SDK types enter Domain/Application.
  - Adapt `ResolvedSecret.BindingRevision` documentation to the explicitly local comparison-token contract. Hold native equality in the outer owner, qualify by binding, bound inventory, and conservatively invalidate on restart/eviction. Never use material hashes or formatted SDK objects.
  - Acceptance: S04/S07 hold, retained references remain admitted and hierarchy-independent, late fills cannot reauthorize invalidated bindings, and all old resolver consumers use one Event adapter.
  - Effort: L. Dependencies: 4.1.

- Deferred: **4.3 Replace purpose-bound writers and verify pending/unknown is never success.**
  - Move/adapt `Services/AuthenticationRuntimeSecretWriter.cs` and `Services/SetupSecretBindingAuthority.cs` into Infrastructure/Secrets.
  - Existing Application ports/results: `IAuthenticationRuntimeSecretWriter.cs`, `ISetupSecretBindingWriter.cs`, `Contracts/SetupLive/SetupSecretBindingContracts.cs`; extend internal evidence to represent pending and unknown without exposing SDK objects or changing public API routes/DTOs.
  - Preserve existing server authorization, canonical instance selections and SetupLive commitment/enrollment barriers. Establish target-bound native receipt before dispatch; retain its effect disposition across cancellation.
  - Delete the old `IInfisicalClientFactory.cs` facade and its old implementation/source users together after migration. No Boolean facade, blind upsert retry or raw HTTP escape hatch.
  - Acceptance: S08 holds through the actual Event writer ports; known write plus failed invalidation remains confirmed; public pending/unknown is bounded failure, never Written/Ready or automatic replay.
  - Effort: L. Dependencies: 4.1-4.2.

- Deferred: **4.4 Wire per-process lifecycle and verify bounded recovery and disposal.**
  - Existing/new shared acquisition files; API/BFF host registration/startup, Infrastructure registration, existing refresh and rotation code; `.agents/rules/api-scheduling.md` and twin `.omo/rules/api-scheduling.md`.
  - Start exactly one selected session's `InfisicalAuthenticationMaintenance` per long-lived owner; stop scheduling, drain work and dispose owned resources on shutdown/startup failure. Use explicit finite/periodic policy; no automatic bootstrap/relogin/replay.
  - Record the user's narrow authentication-maintenance exception identically in both rule twins. Business sweeps remain Quartz; no new business timer loop. Short-lived migration/tooling starts no helper.
  - Remove `SecretRefreshService` timer/reload-as-Activated registration. Preserve actual existing client activation/convergence semantics and required bounded freshness rather than recreating the old service.
  - Acceptance: S05/S06/S10/S11 hold; health distinguishes authority recovery from capability readiness; a helper's success cannot claim deployment convergence or revoke credentials.
  - Effort: L. Dependencies: 4.1-4.3.

- Deferred: **4.5 Publish the lifecycle cutover contract and verify operator guidance is complete.**
  - Existing `.env.example`, `docker-compose.yml`, `src/Explore.AppHost/AppHost.cs` and host-owned configuration options/project attributes only as needed for explicit policy projection.
  - Internal/public pairs: `SECRETS.md`, `CONFIGURATION.md`, `SECURITY-MODEL.md`, `OPERATIONS.md`, `TROUBLESHOOTING.md`, `BACKUP_RESTORE_UPGRADE.md`; public `secrets.md`, `infisical.md`, `environment-variables.md`.
  - Define exact per-consumer lifetime-mode/period inputs, remove retired broad-refresh configuration, and document explicit recovery, native mutation uncertainty, controlled restart, five-minute runtime cache and no fleet proof from one process.
  - Acceptance: every removed operator input has a concrete replacement/removal action; no secret defaults or raw connection strings; required recovery is self-contained and does not instruct unsafe credential printing.
  - Effort: M. Dependencies: 4.4.

### Successor phase 4 Verification

- Deferred: Run `dotnet build --configuration Release --verbosity quiet` once and verify exit 0.
- Deferred: Run `dotnet test --project tests/Explore.Infrastructure.Tests/Explore.Infrastructure.Tests.csproj --configuration Release --verbosity quiet -- --treenode-filter "/*/*/SecretKitRuntimeContractTests/*" --minimum-expected-tests 1` once and verify runtime boundary cases pass.

### Planned Commit Contract D1: Tenant-aware runtime resolution

- Type & scope: `refactor(architecture)`.
- Title: `resolve tenant credentials through admitted SecretKit readers`.
- Description: `Keep Event's binding hierarchy and retained references while replacing duplicated provider reads. Move resolver policy to its proper owners, admit scope before cache/provider access, and translate qualified native revision equality into explicitly local comparison tokens. Preserve bounded managed-string caching, cancellation and safe diagnostics without exposing library types to inner layers.`
- Changelog treatment: Tier 3, skip.
- Required trailers: `Changelog: skip` and `Changelog-Reason: Internal runtime adapter replacement preserving business-facing resolution contracts.`
- Commit paths: 4.2's exact old/new resolver/source/policy/metrics files, applicable Domain/Application contract docs, registration/caller updates needed for this read path, corresponding migrated/new tests and actual affected locks. Do not include writer/lifecycle policy changes belonging to D2.
- Message override: Not overridden.

### Planned Commit Contract D2: Explicit credential lifecycle

- Type & scope: `refactor(self-hosting)!`.
- Title: `maintain Infisical sessions without implicit recovery or replay`.
- Description: `Make each process own its selected authentication lifetime and truthful recovery state. Use SecretKit Hosting only for explicit per-process token maintenance, preserve native write receipts and pending or uncertain effects, and stop treating configuration reload as activated credentials. Keep business scheduling in Quartz and publish the operator inputs and coordinated-restart procedure.`
- Changelog treatment: Tier 2, governed security/configuration/operator change.
- Required trailers: native emitted `Change-Id`; `BREAKING CHANGE: Supply verified per-consumer Infisical lifetime policy and use explicit authorized recovery after expiry or uncertain renewal. Retired SecretRefresh polling and implicit relogin are removed; startup-bound credential changes require coordinated restart.`
- Allocation command: `dotnet run --project eng/release/src/ISLAMU.ReleaseEngineering/ISLAMU.ReleaseEngineering.csproj -- create-change --target develop --type refactor --scope self-hosting --title "maintain Infisical sessions without implicit recovery or replay" --summary "Use explicit per-process token maintenance, preserve write uncertainty and require authorized recovery; remove broad reload-as-activation behavior."`
- Commit paths: 4.3-4.5's exact source/contract/caller deletions and additions, both scheduler rule twins, lifecycle/writer tests, `.env.example`, applicable compose/AppHost projections, named paired operator docs, newly emitted fragment and affected locks.
- Message override: Not overridden.

- Deferred: Confirm D1 is independently buildable and its read cutover does not require D2-only deletions; execute D1, then allocate/complete/execute D2, and verify each literal path list.

## Successor phase 5: Remove obsolete projects and graduate operational knowledge

**Dependencies:** Phase 4.
**Guidance:** plan Sections 7-8 and 13-16; CI/CD, release, IP, refactoring and test invariants.

- Deferred: **5.1 Finish invariant disposition and verify no live old project remains.**
  - Reconcile every remaining old test/source file against the table below: move real Event invariants, replace weaker implementation assertions, or remove a test only for deliberately removed behavior.
  - Delete remaining `src/Explore.Secrets` files/project/lock and `tests/Explore.Secrets.UnitTests` files/project/lock. Remove their entries in `Explore.slnx`, all actual project references and central `Infisical.Sdk` after its last use; regenerate affected locks.
  - Replace affected source-scraping tests such as `SecretAuthorityLeastPrivilegeArchitectureTests` with runtime admission/compiled dependency evidence. Update machine-consumed coverage/intent/project manifests, not historical journal records.
  - Acceptance: S12 holds; no dangling namespace, assembly, test command or provider SDK remains in active product/build contracts; test removal has an explicit stronger replacement or intentionally removed behavior.
  - Effort: L. Dependencies: Phase 4.

- Deferred: **5.2 Remove stale restore and CI routing and verify every surviving owner is selected.**
  - Existing four Dockerfiles; `.github/workflows/_build-test.yml`, `test.yml`, `deploy-coolify.yml`, `deploy-coolify-develop.yml`.
  - Remove old project COPY/restore inputs and old test step/input/result/summary only after callers are updated. Route shared-host/SecretKit changes to surviving Infrastructure, BFF, Standalone, architecture and native security lanes as appropriate; do not silently drop required coverage or rename required checks without evidence.
  - Keep exact feed mapping and final `/app/vendor/secretkit` bundle. Preserve SHA-pinned external actions, protected deployment environments and tool restore.
  - Acceptance: a clean restore graph contains only existing project inputs; actual changes to the shared boundary cannot become a CI no-op; source mapping/notices survive all four image definitions.
  - Effort: M. Dependencies: 5.1.

- Deferred: **5.3 Graduate the replacement architecture and verify active documentation has no obsolete owner.**
  - New `docs/internal/adr/ADR-secretkit-event-ownership.md` and `dev/_journal/domains/secretkit-adoption.md`.
  - Existing `PROJECTS.md`, `CONTRIBUTING.md`, internal `ARCHITECTURE.md`, `CODEBASE_STRUCTURE.md`, `HOSTING_ARCHITECTURE.md`, `TESTING.md`, `CONTRIBUTING.md`, `CI_CD_GOVERNANCE.md`, local-package guide; public local-package guide and `secrets.md`; `.agents/contract/intents.yaml` and `.agents/CONTEXT_ENGINEERING.md` only to remove obsolete test/owner mappings.
  - Record shared-host acquisition, the narrow Persistence tooling dependency, native-versus-local revision meaning, and approved authentication exception as durable decisions.
  - Acceptance: no active instructions require a removed project; no historical archive is falsely rewritten; material deferred work has an actionable standalone backlog home rather than disappearing with this triad.
  - Effort: M. Dependencies: 5.1-5.2.

- Deferred: **5.4 Close package and release evidence and verify qualification claims stay bounded.**
  - Existing dependency record, paired local-package guides, release fragments from B/D2 and new `islamic-value-sensitive-design/workstreams/i-vsd-secretkit-adoption.md`.
  - Finalize source-free provenance, notice retention, actual source/hash/lock/license evidence and the IVSD mapping. Run mandatory final lanes below; record unrun/blocked evidence without weakening checks.
  - Knowledge Graduation: promote newly discovered non-obvious lessons to the domain journal. If selected non-default AOT qualification remains intentionally outside supported IL mode, create new `dev/backlog/secretkit-nondefault-aot-qualification.md` with exact candidate/profile, acceptance workload, activation trigger and owner. Do not defer required Event integration failures into that item.
  - Acceptance: all requested code adoption is complete with honest verification; native provider/image/platform limits are explicit and no “production-ready” claim is inferred from offline checks.
  - Effort: M. Dependencies: 5.1-5.3.

### Successor phase 5 Verification

- Deferred: Run `dotnet build --configuration Release --verbosity quiet` once and verify exit 0.
- Deferred: Run `dotnet test --project tests/Event.Architecture.Tests/Event.Architecture.Tests.csproj --configuration Release --verbosity quiet` once and verify surviving architecture contracts pass.

### Final Workstream Obligations

These are Ring 3 obligations at workstream exit, not another phase, per-task checks, browser QA or intermediate provider sweeps. Do not rerun a successful unchanged final lane.

- Deferred: Run the following exact surviving intent commands once and verify each outcome or record its proven unrelated blocker:

```sh
dotnet test --project tests/Event.Domain.UnitTests/Event.Domain.UnitTests.csproj --configuration Release --verbosity quiet
dotnet test --project tests/Event.Application.UnitTests/Event.Application.UnitTests.csproj --configuration Release --verbosity quiet
dotnet test --project tests/Event.Persistence.IntegrationTests/Event.Persistence.IntegrationTests.csproj --configuration Release --verbosity quiet
dotnet test --project tests/Explore.Infrastructure.Tests/Explore.Infrastructure.Tests.csproj --configuration Release --verbosity quiet
dotnet test --project tests/Event.API.IntegrationTests/Event.API.IntegrationTests.csproj --configuration Release --verbosity quiet
dotnet test --project tests/Explore.Blazor.IntegrationTests/Explore.Blazor.IntegrationTests.csproj --configuration Release --verbosity quiet
dotnet test --project tests/Explore.Blazor.Client.Tests/Explore.Blazor.Client.Tests.csproj --configuration Release --verbosity quiet
dotnet test --project tests/Explore.Diagnostic.UnitTests/Explore.Diagnostic.UnitTests.csproj --configuration Release --verbosity quiet
dotnet test --project tests/Event.Standalone.IntegrationTests/Event.Standalone.IntegrationTests.csproj --configuration Release --verbosity quiet
```

- Deferred: Verify the final persistence run exercises the existing PostgreSQL/SQLite/SQL Server/MySQL/MariaDB corpus and record actual provider membership, not a fictitious fifth migration assembly.
- Deferred: Run `dotnet ef migrations has-pending-model-changes --project src/Explore.Persistence/Explore.Persistence.csproj --startup-project src/Explore.API/Explore.API.csproj --configuration Release --context ExploreDbContext` once and verify no model change under admitted tooling inputs; never generate a migration merely to make the gate pass.
- Deferred: Run `dotnet restore Explore.slnx --locked-mode` and the actual clean-cache consumer/source/hash check, and verify both resolve the immutable candidate through Event's local feed.
- Deferred: Run `dotnet run .ci/scripts/validate-dependency-license-policy.cs -- .` and `dotnet run --project eng/agent-workflow/src/ISLAMU.AgentWorkflow/ISLAMU.AgentWorkflow.csproj -- validate-intents .agents/contract/intents.yaml`, and verify the new actual dependency closure and removed-owner manifests pass.
- Deferred: Conduct the required anonymized security/platform/quality review and verify findings are resolved or explicitly block merge; no source inspection or one-process test may certify ACLs/fleet convergence.
- Deferred: Record actual existing CI image-build evidence if available; otherwise leave actual-image/platform acceptance explicitly pending. Do not introduce a live-service smoke task or claim that copying archives proves image acceptance.

### Planned Commit Contract E1: Complete project retirement

- Type & scope: `refactor(architecture)`.
- Title: `remove the retired Event secrets projects and build paths`.
- Description: `Finish the provider-library replacement without keeping a second secrets implementation. Remove unused source and test projects, their SDK dependency and Docker restore inputs, and route CI to the surviving behavior owners. Preserve every retained Event invariant through its replacement test rather than deleting coverage with the old project.`
- Changelog treatment: Tier 3, skip; public cutover belongs to B/D2.
- Required trailers: `Changelog: skip` and `Changelog-Reason: Complete retirement of replaced internal projects and obsolete build paths.`
- Commit paths: exact remaining old product/test files and locks; `Explore.slnx`, central props, remaining referencing project files and changed locks; four Dockerfiles; four named workflows; actual migrated/replaced tests and machine-consumed coverage/intent metadata.
- Large-packet justification: deletion must travel with project/restore/test-CI consumers, or clean builds/CI target nonexistent paths. Separate unrelated documentation and lessons into E2.
- Message override: Not overridden.

### Planned Commit Contract E2: Durable ownership and adoption guidance

- Type & scope: `docs(architecture)`.
- Title: `document SecretKit ownership and recovery boundaries`.
- Description: `Explain which responsibilities SecretKit owns and which remain Event policy after the old projects disappear. Record shared-host and persistence-tooling ownership, local revision-token limits, explicit credential recovery, retained license obligations and the evidence still required for native or non-default platform claims. Graduate durable findings before local workstream notes are retired.`
- Changelog treatment: Tier 3, skip; no new runtime behavior beyond B/D2.
- Required trailers: `Changelog: skip` and `Changelog-Reason: Durable architecture, contributor and qualification guidance for the completed replacement.`
- Commit paths: exact durable docs/maps/journal/backlog paths in 5.3-5.4, affected context-owner mapping docs and the I-VSD report. Do not include local triad files or unrelated reports.
- Message override: Not overridden.

- Deferred: Execute E1 and E2 with their frozen literal paths after final attributable failures are resolved, and verify all owned work is committed.
- Deferred: Run native `preflight-range --target develop --head HEAD` using the release-engine command and verify complete-range metadata before PR readiness.

## Test Invariant Disposition

The existing old-suite inventory is 51 C# files, including helper/fixture files. Apply this disposition to **each exact file** before deleting it; counts alone cannot justify removal.

| Existing group/file under `tests/Explore.Secrets.UnitTests` | Surviving owner / disposition |
|---|---|
| AdmissionCredentialSecretDefinitionTests, AdmissionRecoverySecretDefinitionTests, AdmissionScannerCapabilitySecretDefinitionTests, AtprotoSecretDefinitionTests, StripeSecretDefinitionTests | Move pure registry invariants to `tests/Event.Domain.UnitTests/Secrets/` with matching filenames. |
| BootstrapSecretLoaderTests; DatabaseUsernameDeclarationTests; LocalIdentityWriterConfigurationTests; PrimaryDatabaseGlobalAuthorityConfigurationTests; PrimaryDatabaseInstanceKeyAuthorityConfigurationTests; PrimaryDatabasePasswordValidationTests; Database/DeploymentCredentialConfigurationTests; EmbeddedPrivacyErasureAuthorityOptionsTests; PrivacyErasureAuthorityDatabaseConfigurationTests | Move pure structured-configuration assertions into `tests/Explore.Infrastructure.Tests/Secrets/Database/`; update provider namespaces, not expected authority semantics. |
| ConsumerSecretAcquisitionTests; InfisicalConfigurationProviderTests; LocalBootstrapSecretContractTests; RuntimeSecretEnvironmentTests; SecretAuthorityConfigurationTests; SecretProviderOptionsValidatorTests | Replace old transport/option mechanics with Phase 1's actual-package admission scenarios; retain every relevant authority, mapping, rejection and no-partial-publication assertion. |
| AuthenticationCredentialProfileReaderTests; PrivacyIdentityFenceKeyTests; SecretResolverBindingTests; ConsumerSecretBindingTests; RetainedStorageSecretTests; GoogleSecretAuthorityTests | Move to `tests/Explore.Infrastructure.Tests/Secrets/` and assert the preserved Event-facing contracts. |
| ExternalDataProtectionRingTests; ExternalDataProtectionSourceTests | Retain specialized algorithmic tests in surviving Infrastructure tests; host/readiness cases belong to Phase 2's BFF host tests; no generic bulk-provider replacement. |
| AuthenticationRuntimeSecretWriterTests; SetupLive/SetupSecretBindingAuthorityTests; SetupLive/InfisicalSdkFailureBoundaryTests | Replace with native-result/receipt tests through the specific Event writer ports, including pending/unknown/cancellation; remove obsolete SDK-exception translation assertions. |
| SecretResolverHealthCheckTests; InfisicalPathDiagnosticTests; SecretHealthDiagnosticConfidentialityTests; SecretAuditSinkConfidentialityTests; SecretRuntimeRedactionTests; SecretRefreshMetricsTests; StructuredSecretAuditLoggerTests; SecretProviderHealthCheckTests | Move relevant safe-output/readiness/metric semantics to existing Infrastructure/host owners; remove only metrics for deliberately removed polling behavior. |
| RotationAwareDbContextFactoryTests; RotationAwareHttpClientFactoryTests; SecretRotationReplicaConvergenceTests | Preserve real consumed activation/convergence invariants at Persistence/Infrastructure or pure Domain owner; delete unconsumed factory mechanics only after reference evidence. |
| AuditingSecretProviderDecoratorTests; EnvironmentSecretProviderTests; InfisicalSecretProviderTests; SecretProviderFactoryTests; SecretRefreshServiceTests; SecretRefreshOptionsTests | Replace real acquisition/audit invariants in surviving tests. Delete only old factory/SDK/timer mechanics and deliberate reload-as-activation behavior; do not rebuild a provider framework to keep its tests. |
| Sources/UserSecretsAuthoritySourceTests; TicketingRecoveryOperatorContractTests | Move local-authority/recovery policy tests to Infrastructure; preserve Development/Testing and operator-purpose restrictions. |
| SecretsTestValues.cs | Keep one appropriately scoped dynamic-value test helper in the surviving suite or use its existing equivalent; no hard-coded secret fixture. |

## Deferred Work And Knowledge Graduation

No required Event integration behavior is deferred. Azure, management packages, generic provider orchestration, a public NuGet release, and universal rotation are out of scope rather than hidden future phases.

Non-default trim/Native AOT qualification is outside the supported IL/untrimmed image profile; graduate its explicit follow-up only if still relevant. Actual native deployment/image claims remain limited by evidence, not silently marked complete.

Permanent architecture belongs in the ADR; non-obvious implementation lessons belong in the journal. This durable brief preserves successor input; an ignored execution ledger is not the only surviving evidence artifact.

## Standalone scenario catalogue

The following retained programme requirements make this brief independent of the ignored active triad. Foundation-only statements are historical delivery context; downstream activation still requires its own approved task contract.

## 3. Observable Behavioral Contract And Scenarios

Internal classes and library mechanisms belong to Section 5. These scenarios describe outcomes, including preserved behavior.

| Requirement | Normative contract and testable scenarios |
|---|---|
| R01 / S01 Explicit authority | The process MUST select exactly one supported authority. **WHEN** the selector is absent, unsupported or contradictory, **THEN** required admission fails without credential acquisition. **WHEN** local-only authority is selected outside Development/Testing, **THEN** startup is rejected. **WHEN** the selected remote authority denies or fails, **THEN** lower-priority configuration canaries are never consumed. |
| R02 / S02 Consumer isolation | A process MUST acquire only its declared purpose. **WHEN** a foreign root, environment, reference expression, duplicate mapped key or contradictory returned coordinate is supplied, **THEN** acquisition is rejected before unauthorized use and no partial configuration is published. **WHEN** API, BFF, Combined and migration run with their intended identities, **THEN** each receives only its declared inputs. |
| R03 / S03 Required and optional admission | Required material MUST be complete before dependent construction. **WHEN** one required member is missing/denied/invalid, **THEN** no dependent client is created. **WHEN** optional mail or analytics acquisition fails, **THEN** unrelated required capabilities remain usable and the optional capability reports the actual bounded failure, not successful absence. |
| R04 / S04 Tenant and reference isolation | Resolution MUST preserve the approved binding hierarchy and scope. **WHEN** two tenants resolve the same logical key concurrently, **THEN** neither sees the other's cached value. **WHEN** a warm binding changes or is invalidated during an in-flight read, **THEN** the old completion cannot repopulate current authority. **WHEN** a retained reference outlives binding retargeting, **THEN** it still resolves only its admitted original authority. |
| R05 / S05 Lifetime and recovery | Authentication MUST retain the selected principal and original lifetime bounds. **WHEN** a finite lifetime ends, bootstrap was consumed, or renewal completion is uncertain, **THEN** provider-dependent work fails closed and recovery requires an explicitly authorized replacement, not automatic login/replay. **WHEN** one renewal waiter cancels, **THEN** it cannot cancel another authorized waiter's shared operation. **WHEN** a long-lived process starts/stops, **THEN** its explicitly enabled maintenance starts/stops only that process's session; short-lived tooling starts no maintenance. |
| R06 / S06 Material ownership | Each acquisition MUST have an explicit owner. **WHEN** one read or generation handle is disposed, **THEN** independently owned reads still work until their own disposal. **WHEN** a multi-key candidate is replaced mid-operation, **THEN** the operation sees one accepted local generation. **WHEN** shutdown drains held work, **THEN** asynchronous client retirement completes exactly once and never under an ownership lock. |
| R07 / S07 Revisions and freshness | Provider identity/version evidence MUST remain truthful. **WHEN** a selected resource is updated or recreated at version one, **THEN** its qualified revision differs. **WHEN** the same resource/version is read through a replacement client, **THEN** it compares equal. **WHEN** evidence is missing or malformed, **THEN** it is not manufactured from time, material or local publication counters. Successful runtime cache age MUST remain at most five minutes and no longer than known validity. |
| R08 / S08 Mutation uncertainty | Provider writes MUST distinguish confirmed application from pending/unknown effects. **WHEN** a response is lost or cancellation arrives after dispatch, **THEN** uncertainty survives and no automatic replay occurs. **WHEN** approval is pending, **THEN** the operation is not reported as written/activated. **WHEN** invalidation fails after a confirmed write, **THEN** that failure cannot erase the confirmed effect or authorize resubmission. |
| R09 / S09 Data Protection | Each process MUST admit only its partition, with independent process-injected pin/floor evidence. **WHEN** authority material changes, admission expires, or a floor/pin contradicts the active provider, **THEN** readiness rejects further serving until the existing restart procedure succeeds. **WHEN** bulk configuration is acquired, **THEN** ring/admission values are absent from that export. |
| R10 / S10 Safe outputs and transport | Controlled outputs MUST contain no material, credential, sensitive provider coordinate or hostile provider prose. **WHEN** startup/read/write/renewal/health/shutdown fails, **THEN** stdout, stderr, logs, traces, metrics and HTTP errors contain only approved bounded categories. **WHEN** a redirect, invalid certificate, foreign origin, oversized body or deadline breach occurs, **THEN** the operation fails without a bypass, diagnostic leak or replay. |
| R11 / S11 Activation and fleet truth | A refresh/check MUST NOT silently enable a capability or certify rotation. **WHEN** a candidate completes after a newer disable/revision, **THEN** it is rejected and retired. **WHEN** only one replica acknowledges, **THEN** deployment convergence is not reported and old credentials are not revoked. Startup-bound credentials use coordinated restart; existing explicitly live-capable consumers retain their own activation protocol. |
| R12 / S12 Build and tooling | A clean consumer MUST restore the exact reviewed packages from the repository feed. **WHEN** restoration starts with an empty cache, **THEN** the actual restored archive hashes and source metadata identify that feed/candidate. **WHEN** model-only tooling or build-time API contract generation runs, **THEN** it acquires no credentials. **WHEN** credential-bearing tooling runs, **THEN** it uses its explicit migration purpose. Final product/test dependencies MUST contain neither removed Secrets project nor the obsolete provider SDK. |


## Standalone successor architecture

The retained target design below is subject to the source-verified blockers and activation corrections above. No repository-native implementation or behavior is claimed merely by preserving it.

### 5.4 Retain Event policy; delete provider mirroring

Move `SecretResolver`, selected Environment/UserSecrets adapters, reference checks, cache ownership, diagnostics and purpose-bound writer implementations into `Explore.Infrastructure/Secrets/`. Preserve Application's `ISecretResolver`, `IRetainedSecretResolver`, `ISecretSource`, writer ports and Domain bindings/registry.

Delete Application's `IInfisicalClientFactory`/`IInfisicalClient`/`SecretProviderValue` facade after real consumers move. SDK references, sessions, native authorities and receipts stay in outer implementation. Keep one deep Event adapter where scope admission, outcome translation, revision projection and cache policy earn their existence; do not recreate `Read`, `Login` and `Write` pass-through interfaces.

Map actual SDK outcomes explicitly: absence -> Unconfigured; denial -> Unauthorized; incompatible selection/coordinates -> Invalid; transport/throttle/unknown evidence -> non-success Unavailable with bounded internal evidence. Cancellation propagates. Required failures never become success, absence, or an alternate-authority read.

Use `ReadNativeAsync` with Raw and no imports for revision-sensitive values. Keep opaque native revision equality in the outer authority owner. A local comparison token is allowed only for a consumer proven process/operation-local and qualified by admitted binding and owner generation. Never label it a durable provider version. Preserve equality for the same native revision across replacement clients within one owner and inequality on update/recreation; restart or inventory eviction invalidates all dependent candidates before reuse.

**Persisted approval exception:** `KeycloakOperationService` persists and later checks revision-derived binding fingerprints; it does not compare credential material at apply. The former claim that all fences are operation-local is false. Before runtime cutover, specify a provider-neutral fence with explicit evidence scope and qualify any durable native export against the actual package. If durable evidence is unavailable, approval must fail closed on restart, cross-owner execution or eviction and require re-review under an explicitly revalidated approval contract. Do not silently weaken multi-replica operation semantics. Environment/UserSecrets also need material-sensitive current-authority comparison or an immutable admitted generation; unchanged binding metadata cannot validate changed credentials. Do not persist values or hash secret bytes into a purported provider revision. This is a successor activation blocker, not a solved foundation feature.

The existing five-minute managed-string cache remains an acknowledged Event boundary in this scope. Do not expand this refactor into a solution-wide disposable-result API migration. Dispose SDK reads immediately after deliberate export, bound cached string retention, fence late completion against invalidation, and never cache provider failures as absence. If protected material is cached internally, the cache owns it and each caller receives a copy; eviction/shutdown cannot dispose a caller's owner.

The current shared `IMemoryCache` is paired with a scoped resolver-local `_cacheKeys` index. Moving that implementation unchanged preserves broken cross-request invalidation. The successor MUST give entry indexing and invalidation generations the cache's lifetime while retaining scoped binding/repository access. Test two separate DI scopes plus a blocked provider read: invalidate from the writer scope, release the older read, and prove neither its returned result nor a subsequent read accepts the retired generation. Tenant-qualified keys alone do not close this race.

### 5.5 Explicit per-process authentication maintenance

The package provides explicit `InfisicalUniversalAuthentication.LoginAsync`, caller-held `InfisicalUniversalLoginReceipt`, login-bound lifetime evidence and `InfisicalPeriodicTokenSession`. Reuse one selected session per process/credential generation; do not make a login per path/key. Keep runtime reads and separately authorized native writes distinct.

Use opt-in `SecretKit.Extensions.Hosting.InfisicalAuthenticationMaintenance` for long-lived process sessions. On 2026-10-25 the user explicitly selected this **narrow exception** for protocol authentication maintenance to `.agents/rules/api-scheduling.md`'s one-scheduler rule. Record the same narrow exception in both rule twins during implementation. It must not run business-secret polling, Quartz work, automatic login or recovery callbacks. A cluster-wide Quartz trigger cannot renew another replica's in-memory token.

This paragraph records P1's resolved design choice, not current-session authority to change scheduler rules or start maintenance. The foundation exposes explicit ownership and cancellation seams and tests expiry; it neither registers background maintenance in serving hosts nor changes either rule twin. The runtime successor must revalidate the exception in its approved execution contract. Before slow bootstrap in that successor, either maintain the admitted session within its verified lifetime or reject preparation before it expires; waiting until host start must not create an unmaintained expiry gap.

Demand-only renewal was considered and rejected because idle expiry can require avoidable operator recovery. Short-lived migration and design-time operations never start background maintenance. The helper maintains each process's own credential; it is not a second business scheduler or a fleet coordinator.

Lifetime semantics must come from independently verified deployment policy bound to the exact login. Proposed non-secret per-consumer inputs are `INFISICAL_<CONSUMER>_TOKEN_LIFETIME_MODE` (`Finite` or `Periodic`) and, for periodic mode, `INFISICAL_<CONSUMER>_TOKEN_PERIOD_SECONDS`, where CONSUMER is API, BFF, COMBINED or MIGRATION. Finite mode must agree with positive returned maximum TTL and preserve original timing. A zero max TTL alone is not periodic-policy proof. Invalid/missing policy fails admission rather than guessing unlimited life. Document exact schema and projection in the owning slice.

No token persistence, same-credential relogin loop, fallback principal, renewal replay, or swallowed cancellation is permitted. A lost/uncertain token requires explicit selected-principal recovery; a restart is a fresh authorized startup only with bootstrap material that the operator knows remains usable.

### 5.6 Data Protection is not bulk configuration

Replace the raw HTTP in `ExternalDataProtectionSource` with the admitted SecretKit client, but keep ring/admission parsing, partitions, independently injected pin/floor, discriminator and no-key-generation invariant in the existing DataProtection module. Preserve the implemented maximum admission interval of **24 hours**; a 12-hour issuance convention is not the validator's ceiling. Tightening it would be a separate behavioral decision, not transport preservation.

Add an explicit async preparation path before the provider can be resolved; synchronous source access may only return an already prepared admitted input. Convert readiness to await a fresh bounded source read rather than making synchronous HTTP calls or checking a frozen snapshot forever. On pin/floor/material divergence preserve `RejectUntilRestart`; do not rotate the provider merely because a new source read succeeded.

Preparation MUST finish before any provider-dependent constructor resolves. Adding it inside `ExternalDataProtectionStartup.StartAsync` is too late because that startup service already receives the provider in its constructor. Keep protect/unprotect and key-manager calls synchronous and local afterward. Preserve readiness's existing permanent rejection on genuine source/admission failure. Caller cancellation before a fresh-read attempt propagates without changing the latch; a failed or uncertain authority read after dispatch retains fail-closed rejection. Test cancellation boundaries and actual serving admission, not only the health response.

This source is a specialized two-value group with its own roots and deadlines. It must not widen API/BFF bulk roots or export ring/admission values through ordinary configuration.

### 5.7 Database ownership and design-time tooling

Move the nine existing `Explore.Secrets/Database/*.cs` files and both `Bootstrap/*.cs` files into `Explore.Persistence/Database/` with new namespaces. Connection construction uses the existing Npgsql/SqlClient/Sqlite/MySql builders; SecretKit does not receive database semantics.

Move the database-aware `SecretResolverHealthCheck` into Persistence and register it from host composition, not from a generic provider library. Update exact namespace callers together, including EF factories, migration/Standalone composition, Quartz bootstrap and the emergency-admin tool. This is one indivisible namespace/owner move, not a reason to change grants, role purpose, SQL or schema.

EF model-only paths remain no-I/O. Credential-bearing design-time APIs are synchronous framework contracts: if they cannot await, isolate a single bounded async-to-sync bridge at that tooling entrypoint, not in the reusable configuration provider or request path. Explicitly select Migration acquisition; do not inherit the old API default. Preserve the shared `event-shared-secrets` store and document `dotnet user-secrets --project src/Explore.API` as the new CLI owner. Add that ID explicitly to required server owners rather than depending on the removed assembly's attributes.

### 5.8 Writes, refresh and client activation

Replace Boolean provider writes with native Create/UpdateValue outcomes and a caller-held target-bound receipt established before dispatch. Readiness is not write permission; runtime read identity is never silently promoted to native write authority. Keep current write-only capabilities and instance/canonical binding checks.

Preserve confirmed, submitted-for-approval, denied, not-dispatched and unknown outcomes in Event's internal operation evidence. Extend the **internal** purpose-specific result types where needed; pending/unknown projects to existing public bounded failure, never Ready/Written. Do not add new endpoints or expose provider handles. Cancellation retains receipt evidence at the operation owner even when it propagates.

That owner includes `SetupLiveApplicationService`, not only its Infrastructure writer. Today its catch unconditionally calls `CancelOperationAsync`, which persists Cancelled for an accepted operation without establishing whether dispatch happened. Before Green, test cancellation before dispatch, after accepted dispatch and after confirmed effect through the actual Application service. Application-owned value-free dispatch/effect evidence MUST survive cancellation and settlement; SDK receipts stay outside Application. Only proven pre-dispatch cancellation may settle as Cancelled. Confirmed effect remains confirmed despite failed invalidation; uncertain effect settles through an existing safe bounded outcome without implying non-execution or authorizing replay. Revalidate the operation-state contract if its existing persistence model cannot represent that distinction; no hidden schema or API change is approved by this transport plan.

Remove the old broad timer/reload service and its automatic Activated acknowledgement. Runtime resolutions retain bounded freshness; startup-bound consumers require coordinated restart. Existing consumers that already implement explicit live candidate activation retain that protocol. A local candidate generation is not a deployment transaction, and a successful check does not enable a disabled capability.

Preserve actual rotation/convergence policy and necessary rotation-aware factories at their owning Infrastructure/Persistence layer. Delete unreferenced generic factories only after proving their invariant disposition. Do not invent a new universal rotation state machine or a fleet coordinator.
