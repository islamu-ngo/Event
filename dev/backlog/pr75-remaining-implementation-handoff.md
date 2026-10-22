# PR #75 — deferred implementation and verification handoff

> Date: 2026-10-22 (Europe/Brussels)
> PR: https://github.com/islamu-ngo/Event/pull/75
> Branch: `feat/event-api-security-remediation`
> Execution worktree: `.worktrees/event-api-security-remediation`
> Last independently verified and pushed implementation checkpoint before this handoff: `84fc67c7acd7d8e1c272898a3a4b33fa5a055841`
> Disposition: the maintainer requested that remaining local work be preserved and pushed now, with implementation completion deferred. **This is not evidence that the resulting branch is functionally complete or that its required checks pass.**

## 1. Read this first

The approved destination is **C# application authority with a database storage engine**. Domain owns business invariants; Application owns authorization, workflows, concurrency coordination and completion; Persistence uses EF Core/LINQ and supported provider APIs. Controllers remain transport adapters. The database provides transactions, foreign keys, uniqueness, constraints and useful tenant row-level security (RLS), not competing business workflows.

The work is substantially authored, but the entire SQL-to-EF programme remains incomplete. A class named Contract, Fence, Guard, NativeQuery or ProviderPrimitive is not automatically a legitimate SQL exception. Conversely, SQL used to install fixed grants, inspect catalogs or install generated constraints is not a stored business workflow.

This report is the durable resume source. Older `dev/active/event-api-security-remediation/` notes are local/ignored working memory and contain superseded candidate recipes. Do not require those files or machine-local caches to understand the remaining scope. Git history and the source/test anchors below are authoritative. The appendix records every pending path at handoff; preserving a path does **not** certify its correctness.

### Completion status — five separate groups, not a completion percentage

**Substantial implementation and acceptance work remains. This is not technically at “push the last commit and merge a completed refactor.”** The maintainer's decision to preserve and potentially merge an incomplete checkpoint is a separate decision. A clean worktree, successful commit hooks or ready-for-review flag does not turn unfinished product work into a passing release candidate.

There is no trustworthy completion percentage. The 13 workstreams have different sizes, partially implemented workflows and unequal verification coverage. Generated artifacts inflate file counts without representing independent tasks. Neither paths committed nor tests passed is a defensible denominator for programme completion.

#### Group 1 — Finish the EF-first cutovers: OPEN

Section 6 names all 13 SQL workstreams and their owning source families. They are **without final sign-off**, not necessarily unstarted. Sections 3–5 distinguish committed bounded outcomes, authored replacements and unverified integration. Do not discard successful existing EF work or repeat it from scratch.

For each capability, completion requires all of the following:

- Inventory the real production callers, selected connection/provider, source observations, writes, transaction boundaries and post-commit effects. Include inline SQL and indirect installers; counting CREATE statements alone misses business behavior.
- Move ordinary reads/writes to EF Core/LINQ and keep authorization, business predicates, state transitions, concurrency coordination, replay and completion in C#. Controllers remain transport adapters, not the home for all business logic.
- Preserve the original selected owner's transaction, tenant/actor identity, cancellation lifetime, optimistic concurrency/CAS, audit/outbox writes and known-versus-unknown commit outcome. Do not acquire a different privileged context mid-operation to make a query succeed.
- Remove the retired business routine and its invocation, installer, obsolete privileges and body-attestation assumptions when their consumers are actually cut over. Renaming SQL wrappers or leaving reachable business SQL behind is not retirement.
- Retain only documented, bounded provider primitives that EF/provider APIs cannot express appropriately: generated schema/constraints, useful RLS, fixed permission/catalog mechanics and proven small lock/claim/clock gaps. A primitive exception must state its owner, provider limitation, exact inputs/outputs, connection/transaction custody and tests.
- Exercise real positive and adversarial behavior, update paired operator/internal guidance where applicable, and reconcile the current architecture/scanner evidence without exclusions or weakened guards.

**Close this group only when:** the reachable SQL capability inventory has an explicit disposition for every family, its C# workflows and real callers are integrated, required provider evidence exists and there are no undocumented business-SQL exceptions. Individual lanes can close independently; closing one does not close all 13.

#### Group 2 — Repair end-to-end integration: OPEN

This is not satisfied by constructor compilation, isolated permission tests or an implementation existing on disk. Work through the concrete blockers in Section 2 and the vertical acceptance in Section 4:

1. **Setup enrollment/import:** implement the missing bounded issuance-claim/enrollment storage admission, starting from the real Runtime 42501 regression. Prove actual enrollment creation/replay/rotation/revocation and the genuine HTTP enrollment→upload→preview→concurrent apply flow. Do not borrow Migrator authority, fabricate authorization or broaden private secret-binding/profile reads.
2. **Scanner management and check-in:** finish current-image issue/list/revoke/check-in acceptance under the fixed scanner, parent and required-document permissions. Prove original-transaction admission, personal Actor linkage, foreign-tenant rejection, competing issuance/check-in and rollback. Reconcile Gitar's actual remaining finding rather than treating its successful bot execution as product approval.
3. **Selected identity and onboarding:** prove the configured identity writer is selected before the complete handler/unit-of-work lifecycle; exercise both colocated and external-identity topologies, temporary lockout versus terminal eligibility, revocation and administrator linkage. Prove final bootstrap writes cannot consume a disposed or foreign receipt and prove startup hands durable effects to a genuinely claimed Runtime worker.
4. **Federation policy and ordinary/mixed settings:** finish the dedicated three-key storage/read/write/hierarchy cutover with real authorization, constraints and seeds. Keep those keys out of generic settings/manifests; prove instance-parent/tenant-child ordering and positive behavior after removing the former governance routine.
5. **Canonical revision metadata and creation:** cover every real tenant/metadata producer, exactly one canonical revision row per actual tenant creation, correct epoch effects, original-transaction rollback, no read-time repair, and concurrent source/settings changes. C# cooperative coordination must be held by the actual writer; accepted direct-credential risk does not excuse races between legitimate API workflows.
6. **Provisioning worker/scheduler and policy projection:** acquire Human custody before protected source reads/BEGIN, then the required Local/Discovery/settings gates in order, retain custody through settlement and release only owned gates. Complete actual producer/projection/creator activation, durable outbox delivery and acknowledged-commit cleanup classification; do not normalize committed work to Unknown because later disposal failed.
7. **Other authored EF consumers:** verify preferences, membership, retention discovery, event-read/catalogue coexistence and typed Listmonk evidence on the final composed source. Keep fleet-health snapshots coherent and private provider diagnostics out of business decisions.

**Close this group only when:** genuine production-owner workflows pass their unchanged positive, negative, concurrency, replay and failure-recovery assertions. A test that fails in fixture setup has not reached the semantic assertion; a later successful build does not close the earlier runtime counterexample.

#### Group 3 — Reconcile, preserve and commit local work: PACKAGING DONE; FUNCTIONAL RECONCILIATION OPEN

The earlier user-supplied status described 534 changed/untracked paths:339 tracked plus 195 untracked, with the 28-path Actor slice still staged and GitHub at `12b43f2a`. Preserve that as a **historical status snapshot**, not today's dirty-tree state or a verified task count.

At the later frozen handoff snapshot, there were 514 pending paths:319 tracked plus 195 untracked. Those counts belong to different checkpoints; no unsupported explanation or “20 tasks completed” inference is made from their difference. The complete 514-path manifest is retained in Section 11. Three release fragments and five durable backlog files produced the 522-path preservation range.

Packaging subsequently committed and normally pushed the Actor slice as `84fc67c7`, then the six source packets, documentation packet and evidence follow-up recorded in Section 9. At the 2026-10-22T20:47:59Z inspection, local/GitHub HEAD was `4ee2a396a260a65fd026ab19587b3f4595c427a3`; fsmonitor-disabled worktree status was clean. This documentation clarification is a later small follow-up, not a revival of those staged changes.

Preservation is therefore complete for the recorded pending tree, but correctness reconciliation remains:

- Verify all consumers, untracked-at-handoff helpers, context partials, fixed permission intersections, composition roots, test fixtures and generated source belong to the same functioning model/owner contract.
- Resolve drift by changing the actual C# model/configuration/workflow and regenerating unapplied development artifacts when necessary. Do not hand-edit migrations/snapshots, restore removed Configurator projects or add compatibility shims.
- Keep future fixes independently reviewable and record actual commands/results. Do not replay the archived stash, old private indices or obsolete candidate commit packets.
- Distinguish authored source, successful compilation, bounded test acceptance and complete workflow acceptance. The two affected Release builds passed, but source packaging did not establish the final runtime/provider matrix.

**Close the remaining part of this group only when:** the committed replacement graph is operationally coherent under its real selected owners and all attributed integration failures are repaired or explicitly quarantined with untouched-base evidence. The Git housekeeping portion is already complete; do not report it as remaining 534-file cleanup.

#### Group 4 — Establish final combined acceptance: OPEN

The Actor slice has bounded verified evidence: 28 PostgreSQL cases, including initial connection-failure cleanup and same-context recovery after backend termination, plus 11 Application cases. Section 3 records run IDs, counts, exits, source hash and the pushed commit. Those results must not be promoted to acceptance of unrelated workflows or of the entire combined programme.

Final acceptance still requires:

- **Provider/model lifecycle:** actual PostgreSQL, SQLite, SQL Server, MySQL and MariaDB behavior, including application and external-identity histories and generated Up→Down→Up where supported. Ten offline model checks, four migration-project builds and the bounded SQLite lifecycle receipt are useful existing evidence, not a completed physical five-engine matrix.
- **Real authority:** ordinary Runtime, Global, Migrator and selected identity-writer configurations; sealed current tenant, null/foreign-tenant refusal, fixed column access, nondelegation, source drift and fail-closed readiness. Useful RLS and constraints remain supporting database safeguards, not replacement business workflows.
- **Actual endpoints/workers:** SetupLive/onboarding/import, scanner issue/check-in, identity lifecycle, federation/settings, canonical creation, policy projection, worker delivery and affected private-management/registration consumers. Include the broader native class inventory and workstream-specific tests in Sections 6 and 8.
- **State and concurrency:** competing legitimate owners, last-slot/duplicate actions where applicable, transaction rollback, original-owner replacement, cancellation, disposed/borrowed custody, uncertain acknowledgements and recovery without duplicate effects. Resolve SQLite cross-process coordination instead of testing only threads sharing one semaphore.
- **Architecture and layer coverage:** final Persistence capability-boundary guards and affected Domain/Application/Infrastructure/API tests. Do not absorb unrelated localization or fixture failures without attribution; preserve a concrete untouched-base reproduction if quarantining them.
- **Candidate-bound evidence:** exact revision/artifact, provider, selected configuration/topology, test filter, nonzero expected/discovered counts, passed/failed/skipped totals, elapsed time, exit code and completed logs. Interrupted, killed, zero-selected, setup-failed, stale-image or cancelled runs are not passing evidence.

Follow the approved socket, TMPDIR, shared locks and verified TUnit filters in Section 8. Do not run duplicate builds or a full provider matrix while merely editing this report. At future implementation completion, run the applicable final workstream matrix once on the final combined image and retain actual receipts.

**Close this group only when:** the complete applicable matrix and end-to-end workflow union has accepted evidence for the same final candidate, with any legitimate exclusions explicitly justified. A build with zero compiler errors is not a substitute for product tests, and a provider-neutral test is not native authority proof.

#### Group 5 — Resolve review/checks and independent admission: OPEN

The accepted trusted-base Release Impact exception is **not** a waiver of Sonar, CodeQL, application correctness, migration/provider, security or independent review requirements. A maintainer can choose to merge this unfinished checkpoint, but that choice does not make those product-verification requirements complete.

Required remaining work:

- Reconcile Sonar's current analysis with the exact source revision. The historical gate was ERROR, including 4.3% duplicated new lines against a 3% maximum and independent reliability/security failures. The dated per-finding SQL audit distinguishes 136 supported false-positive recommendations from two actual literal defects, plus a later separate bootstrap literal defect; it is not an external disposition or a blanket SQL-safety certificate.
- Obtain actual authorized false-positive dispositions where justified, and fix genuine findings in source. Preserve immutable enrollment/body checks and proper literal/identifier treatment. Do not rename sinks, hide interpolation, widen allowlists, hand-edit generated artifacts or weaken the quality gate.
- Inspect CodeQL aggregate and language-specific jobs separately. A neutral aggregate, running C# analysis or a cancelled job is not successful analysis of the candidate; an older failure must be attributed to its own head/run before remediation or rerun.
- Finish Gitar/manual review with the genuine workflow evidence, not merely the bot's SUCCESS status. Reconcile GitGuardian incident 38333356 and the single approved Gitleaks fingerprint without broader exclusions. Current secret/dependency check success does not independently certify private diagnostic handling, all-history findings or every accepted credential boundary.
- Review applicable package/version advisory evidence and license/provenance. GitHub's 119 default-branch dependency warning is recorded in Section 9; a PR Dependency Review success does not by itself close every inherited advisory.
- Obtain required review decisions and final candidate/operational admission. Keep incomplete browser/deployment, restore/keyring, independent assessment and broader security-programme contracts in Section 10 and its companion snapshot visible.

Latest directly inspected rollup at `4ee2a396a2`, 2026-10-22T20:47:59Z: **OPEN, not draft, BLOCKED, REVIEW_REQUIRED**. Release Impact had completed FAILURE entries and a cancelled invocation. Build & Test, Security Integration Tests, OpenAPI Contract Guard and two C# analysis entries were IN_PROGRESS; the aggregate CodeQL entry was NEUTRAL. A CLA invocation was CANCELLED alongside successful CLA invocations. Dependency Review, Documentation Quality, Secret Scanning, Workflow Security and Gitar reported SUCCESS; GitGuardian was NEUTRAL. No SonarCloud entry appeared in that observed rollup, so it does not establish a fresh Sonar pass or analysis revision.

Evidence handles: [Release Impact failure](https://github.com/islamu-ngo/Event/actions/runs/39851739627/job/120079445527), [Build & Test](https://github.com/islamu-ngo/Event/actions/runs/39851397016/job/120078934896), [Security Integration Tests](https://github.com/islamu-ngo/Event/actions/runs/39851396792/job/120078451695), [C# analysis](https://github.com/islamu-ngo/Event/actions/runs/39851396757/job/120079061449), [neutral CodeQL aggregate](https://github.com/islamu-ngo/Event/runs/120079366838). These are dated observation links, not assertions about their eventual outcomes.

The earlier message's “draft, blocked, review required; Sonar/aggregate CodeQL failure; cancelled build/security runs” remains the user-provided older snapshot. Do not overwrite fresh observations with it, infer that a cancelled run passed, or infer that changing draft→ready closed review or product issues.

**Close this group only when:** applicable checks and independent review have genuine current-candidate acceptance or an explicitly recorded authorized disposition for that specific requirement. Record any maintainer merge exception narrowly; do not silently extend it to unrelated failing checks.

### Accepted decisions — do not reopen routine approvals

- This is clean, pre-release development. There is no retained development database/history to preserve. Breaking changes and replacement of unapplied draft migrations through EF CLI are approved. Never hand-edit generated migrations/snapshots or silently reset an actual database.
- C# owns canonical discovery-revision creation, authorization and coordination. Fixed bounded Runtime/Global INSERT, read and epoch-update rights are approved. No privileged parent-lock routine and no dummy Runtime parent UPDATE to acquire PostgreSQL locks.
- Cooperative API coordination replaces inherited privileged business table/row-lock protocols. Direct database edits outside that protocol can race with or bypass C# rules. This credential-only compromise risk was accepted; document it, do not repeatedly use it to veto ordinary EF operations.
- Global tenant creation may insert memberships and role grants. A stolen Global credential can misuse those fixed INSERT rights in existing tenants. This specific consequence was accepted; retain normal C# authorization and adversarial tests.
- A purpose-bound configured Local identity writer is approved. Select it before the whole lifecycle workflow, preserve the original transaction, and keep ordinary Runtime/Global narrower. Writer credential compromise can change the identity data it is legitimately authorized to manage.
- Purpose-bound onboarding owner selection, startup-to-worker handoff repair and a versioned durable effect carrying only changed key names, tenant IDs and notification time are approved. Workers must consume their admitted effect without reading private audit tables.
- The fixed three-column identity synchronization read was approved. Do not expand it into private profile reads.
- The three non-secret ATProto federation keys remain **excluded from both public configuration manifests**. They use dedicated storage and C# policy interpretation, not a generic-settings fallback.
- All remaining PR changes were transferred to this owner. Preserve non-Persistence work as well as Persistence helpers, consumers, tests and generated artifacts.
- Preserve the Configurator extraction merged in `2f4317e1`. Do not restore `Event.Setup.Core`, `Event.Setup.Artifacts`, `Event.SetupAssistant*`, extracted focused tests, `eng/setup-assistant` or the obsolete Terminal.Gui probe. Event's backend onboarding/import and SetupLive authority remain in scope. Current `schemas/islamu-event.md` changes concern IntegrationSync documentation, not restoration of the Configurator.
- Keep one PR. The maintainer chose to handle the trusted-base Release Impact check/force-merge decision. No separate develop-fix PR, direct develop push, force-push, rebase or history rewrite is authorized by this handoff.
- One exact reviewed Gitleaks false-positive fingerprint was approved. Do not turn that into broad scanner exclusions or claim an external disposition that was never performed.

## 2. Current blockers and exact restart point

### 2.1 Confirmed: Runtime cannot read Setup enrollment issuance claims

The narrow Actor replacement is committed and verified. The next real enrollment operation fails independently:

`SetupLiveApplicationService.CreateAsync:371`
→ `SetupLiveRepository.FindIssuanceClaimAsync`
→ PostgreSQL `42501: permission denied for table setup_enrollment_issuance_claims`.

The new regression is preserved at:

`tests/Event.Persistence.IntegrationTests/SetupLive/SetupLiveRuntimeStorageTests.cs`
→ `SelectedRuntimeCanObserveAnUnusedEnrollmentIssuanceKey`.

It uses an actually configured Runtime context, a sealed selected tenant and its original real transaction; it grants nothing and does not fabricate authentication. Run87269 compiled successfully (Release, 28.71s, 748 warnings, 0 errors), then **1 failed, 0 passed/skipped, 64.510s; method165ms; exit2**. This is a genuine capability failure, not container absence.

Two genuine HTTP cases also fail before enrollment returns Created:

`SetupLiveConfigurationImportTests.UploadPreviewAndConcurrentApplyUseTheExistingImportStateMachine(false/true)`.

Latest diagnostic run67947: API Release build exit0/31.86s/506 warnings; **2 failed, 0 passed/skipped, 219.488s, test exit2**. Safe numeric stack-frame line metadata established the exact failing await; no SQL, values, exception messages or file paths were added to exported metadata. The overall wrapper exited1 because it combined two failing test exit statuses.

**No Setup storage permission helper has been authored.** Do not infer implementation from the regression or the source-review proposal.

Next implementation:

1. Add an owner-local fixed Setup enrollment/claim storage descriptor, using existing EF metadata/quoting, configured enrollment, original Migrator transaction and existing tenant RLS. No new business routine, generic privileged executor, permissive bypass policy, Global grant or private Actor/User grant.
2. Issuance claim SELECT/INSERT only: Id, TenantId, ActorId, OperationKey, EnrollmentId, EnrollmentGeneration, RequestFingerprint, ClaimedAt. No UPDATE/DELETE.
3. Enrollment SELECT/INSERT only on its fixed16 existing scalar properties. UPDATE only CapabilityDigest, ExpiresAt, Generation, State, RevokedAt, ExpiredAt, UpdatedAt, UpdatedBy, ConcurrencyStamp. No DELETE.
4. Grant only a wholly absent package; accept complete reapplication unchanged; reject partial/widened authority without catalog repair. Validate direct Migrator-origin, nondelegable rights, MEMBER closure, PUBLIC/table/system-column excess and existing exact tenant-policy metadata.
5. Wire the actual Migrator and native fixture paths. Do not broaden SecretBinding/SetupSecretBindingOperation access merely to unblock enrollment/import.
6. Prove the prerequisite query, real create/replay/changed-intent conflict and rotate/revoke, optimistic concurrency, foreign/null-tenant denial and forbidden DML. Prove partial/extra/grant-option/PUBLIC/dormant-role/RLS-policy drift refuses readiness and reinstall without repair.
7. Rerun the genuine HTTP enrollment→upload→preview→concurrent apply cases. Keep existing authorization, replay and atomicity assertions. Only then assess remaining full SetupLive prerequisites.

The broader35-case SetupLive/DP/onboarding lane has **no completed current passing receipt**. Do not bypass authorization or use Migrator as the HTTP request owner.

### 2.2 Scanner management / Gitar finding: authored fix still needs final acceptance

Latest inspected Gitar issue comment: `6348628096`, updated2026-10-22T18:54:16Z, five of six findings closed. Remaining product finding: scanner authentication forbids Runtime source rights needed by issue/list/revoke/check-in.

Auth/scanner EF lookup is already committed; do not redo it. Existing pending repair introduces fixed scanner storage SELECT16/INSERT16/UPDATE5, exact authentication-profile intersections, Event parent SELECT3 and Participation parent SELECT4, private/write/delegation denials, personal Actor linkage inside the original transaction and API-owned parent coordination.

Important files: `AdmissionScannerCapabilityRepository`, `AdmissionCheckInRepository`, `AdmissionTargetOperationsRepository`, `IAdmissionTargetTransaction`, `Database/ProviderPrimitives/AdmissionTargetTransaction.cs`, admission Application services and `PostgresTenantApiKeyStoragePermissions` / authentication catalogue intersections.

Prior bounded receipts: native3PASS258.632s; Application23PASS281ms; stop/issue2 plus concurrent issue1PASS347.611s; parent15PASS247.281s. These predate the latest physical-owner relocation and pre-COMMIT admission changes. Fresh full-source native management/authentication/check-in acceptance is still required. Architecture placement success does not prove scanner workflow success. Recheck Gitar after the final pushed analysis; never obfuscate source to make a scanner quiet.

### 2.3 Managed worker/scheduler Human custody is still incomplete

Current source already activates Discovery→Membership→Actor→Creator→Projection→Worker permission installation and registers selected identity/creator/worker paths. Do not call that merely dormant scaffolding.

`ManagedTenantWorkerCreationGraphWriter` currently reads readiness/operation state, then optionally acquires Local before Discovery, without the required prior Human gate. `ManagedTenantProvisioningSchedulingOperation` begins with Discovery; its scheduling port performs readiness first.

Resume anchors:

- `ManagedTenantWorkerCreationNativeTests.OriginalGlobalWorkerHoldsHumanCustodyBeforeSourceReadAndThroughSettlement`.
- `OriginalGlobalSchedulerTakesHumanBeforeLocalAndRetainsBothThroughSettlement` in the managed-worker/scheduling tests.
- `HumanSecuritySourceMutationGuardTests`, `HumanAdministratorBulkProducerGateTests`, `DatabaseSeederSecurityGateTests`.

Acquire the real original Human custody before protected source observation and BEGIN, then Local/Discovery in the approved global order; preserve borrowed versus owned custody, current authorization after waits, original transaction, reverse cleanup and terminal commit classification. Do not satisfy tests by weakening source guards or inventing an empty authorization context.

### 2.4 Bootstrap receipt custody: a demonstrated persisted-write counterexample

A disposed bootstrap receipt must not remain usable to authorize final settings/administrator/marker writes. The independent packet recorded run42581: **2 failed in21.054s**, with settings, platform administrator and completion marker persisted after the synchronous final guard returned true even though its receipt had been disposed. This is a real authorization-lifetime counterexample, not a mock interaction check.

The authored correction adds `ILocalBootstrapReceiptSource.RequireHeld(lease, expected)`. The issuer must prove its own original lease/snapshot, active and undisposed lifetime, original null-context/connector, Human/lifecycle custody and cancellation at the final synchronous callback in `TrustedSetupSettingsAuthority`. It must reject foreign, released, replaced or mismatched custody before persistence.

The five-path dependency closure is the Application receipt contract, Persistence `ConfiguredLocalBootstrapReceiptSource`, Infrastructure `TrustedSetupSettingsAuthority`, Application onboarding completion tests and Persistence `LocalBootstrapConvergenceTests.SettingsCustody`. **A current passing receipt for this correction is not established.** Rerun unchanged adversarial tests and the genuine positive onboarding flow in both supported topologies. Do not treat source presence as proof that the persisted-write counterexample is closed.

### 2.5 Legitimate multi-process SQLite coordination remains unproven

The SQLite implementation of the named cooperative session gates uses process-local semaphores. These coordinate threads in one process, not separate API/worker processes sharing a database. Existing standalone evidence does not establish that every supported SQLite deployment is single-process. This is a legitimate application concurrency question, **not** the already accepted stolen-credential/direct-edit trade-off.

Before declaring SQLite support complete, either establish a database-backed transaction reservation/revalidation protocol for bootstrap, eligibility, discovery and settings across processes, or explicitly enforce and document a supported single-process topology. Test competing real owners; do not substitute an in-memory semaphore test for cross-process safety.

The managed worker, scheduling and Infrastructure worker settlement paths also need the acknowledged-commit/cleanup review already applied in `12b43f2a`: a cleanup failure after an acknowledged COMMIT must not turn a known committed result into Unknown or trigger unsafe replay. Keep original causal exceptions and owned/borrowed custody distinctions.

## 3. What is already delivered — do not restart it

These are bounded outcomes, not whole-PR approval.

| Checkpoint | Delivered behavior and limits |
| --- | --- |
| `84fc67c7acd7d8e1c272898a3a4b33fa5a055841` | Narrow `IActorOwnershipReader`/immutable ownership facts; six caller cutover; original selected-context EF projection with membership/soft-delete filters; fixed Actor SELECT5/TenantUser SELECT3; exact source-guard intersections; original-connection cleanup on readiness failure and on broken-query recovery. Source packet27 plus durable journal =28 paths; normal hooks and push exit0; remote hash verified. |
| `12b43f2a897676a31d8550df2fcf194120a9f0a9` | Preserve known committed versus unknown outcomes through original transaction disposal and outer cleanup. No busy/deadlock-shaped inner exception may cause replay of a terminal outcome or a false Failed receipt. |
| `b11c8f72cac8fe297d4ee6904068f41ebc2ca0c2` | Human→Discovery before import generation/settings/BEGIN; original-context custody; lazy Human-only preparation before BEGIN; reverse cleanup and post-gate delivery. Genuine Setup HTTP acceptance remains blocked as described above. |
| `e188f261bf` and separate report follow-up | EF authentication/storage source changes and fixed report source rights. Old routine-shaped identifiers can remain solely as forbidden-artifact checks; that is not invocation. Historical auth native101PASS43m45.696s and wrong-tenant API1PASS68.667s are bounded older evidence, not the final514-path matrix. |
| Earlier committed work | User navigation, external-login lookup, reference catalogue, localization Boolean status read, email-retention discovery, several SQL literal fixes, owner-local SQL compilation consolidation and architecture-authority docs. Verify current source/history before classifying old scanner findings as still present. |

Actor final acceptance run42258: isolated Persistence Release build exit0/77.26s/1054 warnings; exact Actor reader13 plus permission15 = **28 passed, 0 failed/skipped, 318.930s, exit0**. App ownership11 passed302ms in run62443. Actor-only Support catalogue controls4 passed in run58367. Both independent final source reviews approved reader SHA256 `ed1a2df426abb9df18598ab0c0803506b8eaaa9aa3742fce1a76efe42d545351`.

The two connection defects were reproduced before correction: initial-readiness rejection left the physical connector Open; separate backend termination left EF's successful-opening count unbalanced and made the next successful same-context read finish Open. One earlier backend-failure run timed out during seeding and is explicitly **not** the semantic RED. The new journal entry in `dev/_journal/domains/persistence-and-db.md` records the reusable lesson and exact command.

Transaction-disposal acceptance: final run45456 exit0, original-owner9PASS38.511s and API committed-import disposal2PASS33.264s, both affected Release builds exit0. Real fault injection performs base transaction Dispose, and the API arms only the actual Applied operation/original transaction. PG/SQL Server/MySQL mid-Dispose/borrowed-identity variants are not thereby certified.

The earlier allegation that Actor requires the complete membership/creator/worker runtime cycle is **superseded**. The actual same-owner pure inventory partial `PostgresTenantMembershipMutationPermissions.Inventory.cs` makes the committed Actor slice independent. Do not re-import all pending runtime activation just to build Actor.

## 4. Authored but unfinished verticals and integration work

### 4.1 Federation settings, policy reads and ordinary/mixed-setting separation

Dedicated instance/tenant federation entities and EF configurations, Application setting facts, ordinary/admin scalar and batch routing, dedicated repositories/writer/mutation store, Jetstream and PDS policy consumers, fixed permission profile, seeds and moderation policy reads are authored.

Key files: `AtprotoFederationSettingFacts`, `AtprotoFederationSettingRepository`, `AtprotoFederationGlobalReadRepository`, `AtprotoFederationSettingsMutationStore*`, `AtprotoFederationSettingsWriter`, `AtprotoFederationReadAdmission`, `PostgresAtprotoFederationSettingPermissions`, `HierarchicalSettingsReader/Resolver`, `AtprotoJetstreamTenantPresentationResolver`, `AtprotoPdsRecoveryPolicyResolver`.

Finish/prove:

- All generic scalar/batch/reset/lock/unlock/admin routes reject or deliberately route the three dedicated keys; no duplicate generic row, generic fallback, dual write or silent repair.
- Original acquired-source image, cancellation lifetime, hierarchy locks/defaults and authorization survive dedicated versus ordinary partitioning.
- Genuine concurrent mutation, rollback, unsupported/malformed keys, null tenant, fixed-role grants, private generic-setting denial and original-connection replacement attacks.
- Dedicated seeding is idempotent and uses stable IDs; retain the generic validation-profile default. Existing seed1PASS204.221s and bootstrap1PASS220.799s do not prove the complete vertical.
- Moderation compensation must not retain a native source projection that still requires removed generic federation keys. It still has native lineage/effect behavior to retire.
- Remove obsolete governance DI/installers and forbid obsolete cluster artifacts coherently, without automatic DROP or retained-data migration.
- Preserve manifest exclusion and public/internal configuration/backup documentation parity.

Tests: `AtprotoFederationSettingFactsTests`, command-routing tests, Jetstream/PDS resolver tests, `AtprotoFederationSettingsReadTests`, persistence/mutation Batch/Concurrency tests, legacy `AtprotoFederationPersistenceTests`, moderation compensation tests and bootstrap federation-preservation cases.

### 4.2 Discovery metadata, parent coordination and complete producer coverage

Fixed Runtime/Global metadata reads, bounded canonical INSERT and epoch UPDATE are authored. The API creates canonical revision rows in each creator's original transaction; it must not lazily fabricate a missing revision row while handling an unrelated update.

Key files: `ExploreDbContext.DiscoveryEnrollment/Disclosure/StorageReferences`, `EventDiscoveryDisclosureMutationScope`, `EventDiscoveryDisclosureProviderOperations`, `EventDiscoverySourceProviderOperations`, `ManagedSettingsDiscoveryDisclosure`, `SettingsDiscoveryDisclosureObservation`, `PostgresDiscoveryMetadataPermissions`, bootstrap native data/effect paths and creator/worker writers.

Finish/prove:

- Inventory every revision producer: user tenant creation, managed worker, bootstrap bypassing tracked SaveChanges, tenant lifecycle, membership/identity changes, settings imports, settings scalar/batch, actor/group/organization deletion/PII and fanout.
- Preserve Human/source custody and parent-setting cooperation before BEGIN, same-transaction canonical row creation, exact epoch effects, constraints and rollback. Prevent duplicate native+tracked increments.
- Retire inherited privileged parent lock/table/row routines and nested native parent fencing. Metadata EF DML alone does not retire traversal/disclosure/snapshot maintenance.
- Verify deletion/fanout/canonical/missing-row cases and borrowed transaction/original backend admission.
- One SQLite no-lazy-creation regression went RED8.375s then GREEN8.485s. PostgreSQL and the complete producer class are still pending.

Tests: `DiscoveryMetadataAuthorityTests*`, `DiscoveryMetadataFreshEnrollmentTests`, `EventDiscoveryEnrollmentProducerTests*` (Canonical, Deletion, Fanout, LocalWriters, WorkerSources, OrganizationPii, LocalWriterCleanup), import Revisions partials and `TenantDiscoveryCreationNativeTests*`.

### 4.3 Tenant creation, membership, provisioning-policy projection and worker graph

Human tenant creation is now a port/operation chain, not an isolated handler hunk: `IHumanTenantCreationPort`, `HumanTenantCreationPrincipalProjection`, Infrastructure port and Persistence graph writer must travel together.

Worker creation/scheduling has contracts, selected Global owner, graph/scheduling sources, fixed creation/membership/projection profiles and current DI/installer activation. `ManagedTenantProvisioningPolicyProjection` and `ExploreDbContext.ProvisioningPolicy` maintain a dedicated projection from original source changes.

Finish/prove:

- Correct Human→Local→Discovery acquisition before source read/BEGIN as above.
- Complete the source-to-projection producer inventory (historical inventory13 hooks), original transaction and canonical revision handoff.
- Actual registration/provisioning/bootstrap lifecycle, administrator Actor/membership/role-grant linkage, operation state Pending→Processing→terminal, exact snapshot/CAS and one terminal effect.
- Claim/attempt/completion seals, ambiguity and cleanup cannot be replaced by a replayable generic retry.
- Exact fixed creator/worker/member/Actor/projection/preference permission intersections remain declarations, not inferred live ACL unions.
- Activated projection consumers cannot be used with an empty/uninitialized projection. Seed initialization and SaveChanges hooks must match activation.
- Real worker delivery and concurrency tests must reach their target assertion, not fail in fixture setup.

Tests: `ManagedTenantWorkerCreationNativeTests*`, `ManagedTenantProvisioningPolicyProjectionTests`, `TenantCreationPermissionsNativeTests*`, external permissions/catalogue composition/lookup tests, membership mutation tests, support membership coexistence and API native tenant-creation tests.

### 4.4 Local identity selected writer, eligibility and owner lifetime

Source includes purpose-bound context factories/scopes, Application `LocalIdentityOperationScope`, writer inventory/permissions, eligibility lifecycle/revocation, bootstrap receipt authority, external-login binding reader, managed-local eligibility source, signing/issuer configuration and original execution custody.

Finish/prove:

- Select owner before constructing handler/unit of work for Local login/lifecycle, bootstrap, administrator operations and onboarding; do not switch an active context/transaction to a new identity.
- Colocated and external identity topologies, Runtime/Global versus writer permissions, readiness/replacement/cancellation and whole lifecycle concurrency.
- Eligibility rows created/revoked atomically with the legitimate workflow; all13 related producers tracked, no lazy trust repair.
- Restore correct dependency direction and approved primitive placement after the moved custody classes. Do not add direct-ADO registry exceptions.
- Local identity native Administration DATA routines are **still reachable** on Npgsql. The selected EF writer and eligibility table do not retire the full Local administration/compile/effect family.
- Authentication signing-key ownership, configured issuer/audience, key selection and Data Protection remain honest startup boundaries.
- Worker-delivery leases must be claim-fenced; do not dispatch bootstrap effects synchronously without the Runtime worker's claim.

Tests: `LocalIdentityWriterPermissionsNativeTests*`, writer-factory/execution-scope/lease/lifecycle/selected-lifecycle/eligibility tests, configured revocation/managed eligibility/receipt source tests, identity-writer authentication/human-source owner/query/registration tests, Local bootstrap convergence, native signing and API owner-selection/startup tests.

Prior bounded writer readiness1PASS289.417s/replacement1PASS250.364s and Human14PASS11m27.014s predate parts of the latest move/activation. They are not final acceptance.

### 4.5 Preference and other authored EF read retirements

Membership enumeration, tenant preference, IntegrationSync discovery, hold-expiry discovery, event-resource audit discovery and registration-retention discovery have authored EF replacements/fixed permission owners; old names can remain as forbidden-object identities. Navigation/localization/email-retention are already committed. Do not restart these as if all still invoked old SQL.

- Preference: original prepared Human GlobalInstance Serializable owner; resolved-self/active membership/target tenant checks, exactly one update and completion image. Suspended tenant remains an intended positive; do not add a new Tenant-status predicate or old-value CAS. Fixed User SELECT5/UPDATE3. Prior native21PASS16m57.856s, but worker/profile coexistence and current whole-source acceptance remain open.
- IntegrationSync: keep original connector witness outside execution-strategy retry; exact claim/rollback/usage semantics. Current typed evidence must replace business decisions based on arbitrary LastError.
- Hold/registration/audit discovery: EF eligibility/order/group/limit, bounded Global metadata only, real nonempty and private-column/role-drift controls. Account for `DateTimeOffset` precision/order per provider rather than assuming SQLite parity.
- Navigation source review exposed a potential witness re-anchoring shape inside retries. It was **not** a newly executed exploit; inspect current source and write a real replacement-backend test before changing it.
- Keep actual per-family obsolete-role/routine refusal, complete installers/DI/native fixtures and source-guard intersections. Moving SQL to an approved directory is not SQL retirement.

### 4.6 Listmonk typed durable evidence and post-commit effects

`IntegrationSyncEvidenceKind` and `RequireValidEvidence`, evidence column/checks, claim-fenced outbox updates, drain service, health aggregate and recovery documentation are authored.

Finish/prove:

- Provider text never authorizes ambiguity recovery or supplies a health classification.
- Preserve exact tenant, current claim owner, expiry, attempt, retry and terminal transitions; uncertain provider handoff is not proof of safe replay.
- Versioned setting-change effect is built in the original transaction with key names/tenant IDs/time only. Worker consumes the admitted message without private audit reads.
- Startup registers all delivery services even when immediate dispatch is disabled, and hands off to the actual claim-owning worker.
- Verify real PostgreSQL bootstrap/startup→worker delivery, duplicate delivery, no provider recall after uncertain/terminal results and exact notification/projection effects.
- Old evidenceKind0 means unknown; do not infer a typed classification from historical diagnostic text. Down drops recovery meaning.

Tests: `IntegrationSyncEvidenceTests`, outbox repository/drain tests, native Listmonk HTTP tests, bootstrap worker native tests, receipt/projection tests and fresh healthy-host acceptance.

## 5. Model and migration completion

Eight coherent migrations were generated with matching EF CLI10.0.10, not hand-edited. Their24 artifacts (eight migration/designer pairs plus eight snapshots) travel with the complete16 handwritten model-source closure.

| Provider | Application migration | External identity migration |
| --- | --- | --- |
| PostgreSQL | `20261021034329_ApplicationOwnedProgrammeModel` | `20261021034331_ApplicationOwnedLocalIdentityEligibility` |
| SQLite | `20261021034341_ApplicationOwnedProgrammeModel` | `20261021034343_ApplicationOwnedLocalIdentityEligibility` |
| SQL Server | `20261021034353_ApplicationOwnedProgrammeModel` | `20261021034355_ApplicationOwnedLocalIdentityEligibility` |
| MySQL / MariaDB | `20261021034405_ApplicationOwnedProgrammeModel` | `20261021034407_ApplicationOwnedLocalIdentityEligibility` |

Application model: dedicated federation instance/tenant tables, Local eligibility and provisioning-policy tables, checks/FKs/indexes, typed IntegrationSync evidence and generic-setting exclusions. ExternalIdentity owns its own eligibility schema/history. Preserve separate privacy authority and Data Protection owners.

Actual receipts: four provider migration-project Release builds passed; ten offline model/pending-change checks passed for application/external models across PG/SQLite/SQL Server/MySQL8.4/MariaDB10.11; SQLite application+external Up→Down→Up1PASS25.678s. **The remaining physical provider/owner lifecycle matrix has not been certified.** Older notes saying the SQLite helper never ran are superseded by that bounded receipt.

Resume:

1. Reconcile the final model snapshot against all activated source hooks; run pending-model detection using the complete current model, not a temporary subset.
2. Actual PostgreSQL, SQL Server, MySQL8.4 and MariaDB10.11 application/external Up→Down→Up; retain the SQLite case at final matrix exit.
3. Assert separate histories, required constrained columns, restrictive FKs, malformed-row rejection and intermediate DDL legality. MySQL-family DDL is not atomic.
4. Recheck source defaults/seeding/projection initialization and ordinary-setting exclusions during actual startup.
5. If model corrections are needed, regenerate unapplied development artifacts with the matching CLI; never patch generated output.
6. Document Down as destructive to new dedicated/eligibility/policy/evidence state, not a data-preserving rollback.

Historical32 draft migration/designer files and eight old snapshots were recoverably archived, not reapplied. Local archive SHA256: `90b9c699c57d4f5191a88e5df558e92a05c38fc58b11efbd8c82aac7eb99c018`. Do not restore those drafts or replay recovery stash `798b674479eaf8ef4731fbdd28fd4c7c8d37dd9a`; it predates extraction and is already applied.

### 5.1 Exact handwritten model closure and semantic checks

The generated programme artifacts must remain paired with these16 model-source files (seven new and nine modified at the initial snapshot):

```text
src/Explore.Domain/LocalIdentityEligibility.cs
src/Explore.Domain/ManagedTenantProvisioningPolicySetting.cs
src/Explore.Domain/Settings/ManagedTenantProvisioningPolicyInventory.cs
src/Explore.Domain/IntegrationSyncOutbox.cs
src/Explore.Persistence/Configurations/Entities/AtprotoFederationInstanceSettingConfiguration.cs
src/Explore.Persistence/Configurations/Entities/AtprotoFederationTenantSettingConfiguration.cs
src/Explore.Persistence/Configurations/Entities/ManagedTenantProvisioningPolicySettingConfiguration.cs
src/Explore.Persistence/Configurations/Entities/IntegrationSyncOutboxConfiguration.cs
src/Explore.Persistence/Configurations/Entities/SystemSettingConfiguration.cs
src/Explore.Persistence/Configurations/Entities/TenantSettingConfiguration.cs
src/Explore.Persistence/Identity/Configurations/LocalIdentityEligibilityConfiguration.cs
src/Explore.Persistence/Identity/ExternalIdentityDbContextFactory.cs
src/Explore.Persistence/Identity/IdentityDatabaseProviderComposition.cs
src/Explore.Persistence/Schema/PortableRelationalModelPolicy.cs
src/Explore.Persistence/ExploreDbContext.DbSets.cs
src/Explore.Persistence/ExploreDbContext.QueryFilters.cs
```

The federation Domain entities already existed at the verified checkpoint; automatic configuration discovery must not be supplemented by duplicate ApplyConfiguration calls. The IntegrationSyncOutbox model change is additive: required `EvidenceKind`, bounded validation and enum, not a reason to invent compatibility overloads.

Model acceptance must cover:

- Exactly three ordinal/case-sensitive finite federation keys and valid finite values, unique keys, tenant foreign keys and correct provider collation behavior. SQL Server uses DATALENGTH rather than trimming LEN for the four federation key/value checks.
- Seed `federation.atproto_events_enabled=false`, `federation.atproto_events_backfill_enabled=false`, `federation.atproto_events_backfill_mode=downtime_only` in dedicated storage. Preserve the generic validation-profile default. Legacy generic fixture seed rows violate the new exclusion constraint; constructor compilation does not repair those fixtures.
- Local eligibility has ten scalar fields, unique subject, generation concurrency, valid lifecycle/audit fields and restrictive user/current-operation foreign keys. A temporary lockout is deadline-aware, not permanent revocation; terminal eligibility cannot be resurrected. External identity stores persist durable intent first and replay it; this is **not** distributed atomicity across separate databases.
- Provisioning policy has six scalars, unique key, revision compare-and-swap and explicit absence tombstones. Only Deployment.Mode, WhiteLabelingEnabled, DefaultTenantQuotaBytes and AllowTenantCustomDomain retain values; other projection values must be null. The older SQLite8PASS30.615s receipt predates the later initializer/execution-strategy correction.
- Outbox EvidenceKind is required, defaults to0 and has range0–5. Zero means unknown; private diagnostic text is not a substitute for typed evidence.
- Canonical discovery metadata is exactly one UUIDv7 row with IdentityEpoch0/DisclosureEpoch1 per **actual** tenant INSERT, in that creator's original atomic transaction. No speculative reserved-tenant row, collision repair, lazy read repair or double epoch update.
- Current erasure does not hard-delete the referenced User/operation; restrictive foreign keys are a future hard-delete design consideration, not an established current erasure failure.

Physical migration restart anchors are `SqliteApplicationInitialLifecycleTests.GeneratedInitial_AppliesRollsBackAndReapplies`, `SqlServerApplicationInitialLifecycleTests.GeneratedInitial_AppliesRollsBackAndReapplies` (the SQL Server/MySQL/MariaDB argument cases), and PostgreSQL `GeneratedInitMigrationBehaviorTests.ExploreInitial_AppliesRollsBackAndReapplies`. Exercise both application and external-identity stores; generated model checks alone do not prove grants, activation, bootstrap or real provider execution.

Registration retention's authored EF path uses the existing membership read profile. Do not invent a separate installer merely because a historical note expected one.

## 6. Remaining SQL-to-C# programme — all13 workstreams

The latest source navigation map found **92 literal routine-emitter C# files in18 groups**. This is not92 routines, not a verified current production-call count and not a completion percentage. Shared emitters and inline business SQL fall outside simple CREATE searches. Reconcile source/DI/migrator/callers again after the handoff commits; classify each reachable routine by behavior, not its name.

Paths below are under `src/Explore.Persistence`. `S` means `Security/ProviderPrimitives/`, `D` means `Database/ProviderPrimitives/`. A stem refers to the corresponding Postgres contract and its partials.

### 6.1 Authentication, API-key management and exact-source coexistence

Already committed EF authentication/scanner lookup is not remaining native lookup work. Finish scanner storage coexistence (section2.2). Separately retire the live `InstanceApiKeyCapabilitySql`-generated management, issuance and usage-report routines: `PostgresInstanceApiKeyManagementContract`, `PostgresInstanceApiKeyIssuanceContract`, `PostgresPlatformApiKeyUsageReportContract`, `InstanceApiKeyCapabilityStore`, D `InstanceApiKeyCapabilityQueries`.

C# owns metadata/redaction, quotas, issuance/recovery and historical report interpretation; fixed hash/private data permissions remain purpose-specific. Preserve paired lookup-key version/digest semantics, ambiguity/duplicate rejection, threshold usage CAS and no key/scope/owner writes from authentication. Tests: API-key/scanner read ports, authentication authority, machine/room and instance capability cohorts.

### 6.2 Directory, preferences and administration visibility

Finish authored membership/preference cuts rather than recreating them. Still-live owners include `TenantLookup`, `AdministrativeAuthority`, `ManagedTenantDomain` and `ActorConsolidation`. D tenant/admin/domain query seams still perform directory filtering, domain-setting extraction, visibility and graph-consolidation effects.

Tenant lookup reads only two domain-setting keys; do not solve row restriction by exposing all generic `TenantSetting.Value`. Use dedicated mapped non-secret storage or a justified narrow projection when required. Preserve authenticated-current-user redirection, active/nondeleted membership, active tenants, newest fallback, foreign subject denial and original tenantless Global authority.

### 6.3 Human/machine source acquisition and commit authority

Still-live: `HumanPrincipalSource`, `HumanAuthorizationRoute`, `HumanAdministratorCommitAuthority`, human EventAssignment/SupportAccess/Email/WebhookResource/EventProvider/AI-target sources, `PlatformApiKeySource`, plus machine mutation and room authority generated through shared builders.

Move current-policy acquisition/validation/completion into C# with genuine authentication lifetime, selected context and original transaction. Retain fixed catalog/grant/RLS defenses but do not preserve SQL business projections as if catalog attestation requires them. Preserve authorization after waits, revocation, borrowed custody, no cross-owner capability unions and all negative credential/role tests. Main anchor: `HumanPrincipalNativeSourceFenceTests` and actual Application/API producer tests.

### 6.4 Ordinary/mixed settings and specialized editors

Still-live: OrdinarySettings, MixedSettings, PolicySourceSet, PaidPolicyCeiling, VisitorPolicyAuthority, ProviderConfiguration, ResolverConfiguration, StorageConfiguration, StorageProbeSource, OperatorIdentityEditor, EventResourceProvider and typed SQLite writer branches.

C# must own hierarchy/locks/ceilings, original source image, validation, authorization, exact effects, audit/outbox and completion. Do not exempt whole settings compilers as lock primitives or add generic EXECUTE wrappers. Retain atomic batches and provider-value semantics, including SQLite boolean/DateTimeOffset correctness. Tests: ordinary/mixed/provider/resolver/storage/operator/event-resource native and original-human cases.

### 6.5 Local/support administration and managed provisioning DATA

Native LocalAdministration, SupportAdministration and ManagedProviderProvisioning read/compile/effect routines remain reachable. Local Npgsql chooses a native administration path; selected writer work alone does not remove it. ManagedTenantDomain and ActorConsolidation have separate live behavior.

Retire complete observe→authorize→compile→mutate→audit/completion workflows in C#, preserving distinct local/support owners and physical attempts, completion seals, support auditing and no role unions. Test both colocated/external identity and real support/local administration, not only installer source.

### 6.6 Configuration import, replay and bootstrap

Still-live bootstrap/replay plus import Snapshot, State, Receipt, ScalarSetting, TenantSetting, TenantIdentity, Branding, LegalSource/LegalMutation, PaidPolicy, PublicationPolicy and EventResource families.

C# coordination/effect payload and SQL compilation consolidation are not retirement of native source/effects/receipts. Replace one complete source/effect/receipt family at a time using the original transaction; preserve generation/replay, stable digest/fingerprint, exact allowed columns, legal/publication decisions, fidelity checks and terminal receipt classification. Receipt's distinct system-column proof stays explicit until replacement. Resolve Setup storage first so actual HTTP acceptance can execute the importer.

### 6.7 Events, public/private discovery and AI reads

Eight source-read emitter families remain: PublicEventDiscovery, PublicEventDetail, PrivateEventManagement, RegistrationPrivateRead, PublicEventResponseClock, AiActorContextRead, AiConversationOwnerFence and EventCreationPublisherRead. EventReportOperations is another live family.

LINQ projections/specifications should own visibility, current authorization, filtering/order and result semantics. Preserve public/private differences, parent-column registration, tenant predicates, exact temporal semantics, response-clock/fingerprint behavior and actor/AI source isolation. The committed ownership-facts reader is not retirement of all Actor/AI/event reads.

Separately replace DiscoveryTraversal, Disclosure and SnapshotMaintenance business behavior: capture/reuse/page/purge, revision binding and trigger-driven disclosure. Keep a bounded justified storage clock/claim primitive if needed; no business view/function/trigger substitute. Tests include public event discovery/detail/clock, private management/registration, publisher/AI and shared disclosure cohorts.

### 6.8 Durable outbox, fanout, webhooks and web push

Live GenericOutboxAdmission, OutboxExecution, PublicationFanoutExecution, NotificationFanout/NotificationOutbox admission, IncomingWebhookQueue, WebhookLocalDeliveryAdmission/Health, WebhookClaimEndpointState and WebPushDispatchAdmission.

C# owns eligibility, order, attempt/claim/lease lifecycle, selected execution, completion, retry and ambiguity. Retire claim+lease+settlement together; never use an unclaimed direct delivery path. Keep atomic outbox creation and idempotent duplicate consumption. Real concurrency, expiry, replaced credential/connection and uncertain-provider outcomes are required.

### 6.9 Email dispatch and delivery policy

Email retention discovery is already EF, but EmailDispatchAdmission and EmailDeliveryPolicy Controls/ProcessorMutation remain native. Replace caps, policy/control resolution, stale recovery, claims and processor mutation in C#. Retain no-email deployments, original policy/claim authority and provider ambiguity. Test actual workers and policy writers; SMTP retention success does not establish queue retirement.

### 6.10 Registration, checkout and payments

Live RegistrationPrivateRead, RegistrationFinalizationQueue, RegistrationProviderWriteQueue, RegistrationProviderSubscriptionClaim, CheckoutDispatch and PaymentReconciliation.

These are money/state-machine operations. Preserve exact capacity and payment/provider ownership, idempotency, subscription claim, lease/recovery, atomic finalization and completion under concurrency. Use EF conditional updates/transactions; isolate only unavoidable claim syntax. Real native last-slot/double-submit/cancel/provider ambiguity cases are mandatory before retirement.

### 6.11 ATProto queue, settlement, materialization and compensation

Live PdsQueueAdmission, AtprotoMaterialization, AtprotoModerationCompensation and inline transient cleanup. Dedicated federation policy reads and existing C# settlement do not retire their lineage/effects.

Move policy decisions and transitions into C# while retaining original attempt, CAS, receipt lineage and coherent discovery effects. Complete the moderation policy projection transition against the new dedicated tables. Tests: moderation compensation, PDS claim/materialization/settlement and transient-cleanup provider cases.

### 6.12 Storage accounting, location governance and privacy erasure

Live InstanceStorageAdministration/Recalculation, StorageReconciliation, LocalStorageQuarantine, StorageProbeSource, LocationPrivacyReview/Governance Reads/Corrections/Receipts and external privacy-erasure/identity-fence contracts.

C# owns quota/accounting, reference reconciliation, quarantine classification, sensitive location selection/correction/replay and erasure/retention decisions. Protect PII/secret boundaries and independent purpose credentials. The repository name `EfCorePrivacyErasureAuthorityRepository` does not mean native append/read/compaction is gone. Preserve exact-instant comparisons, evidence retention and erasure completion/identity resurrection defenses. Do not expose private text for an aggregate convenience.

### 6.13 Retention discovery, fleet health and idempotency receipts

Authored EF retention/discovery cuts still need complete current acceptance. Live fleet health includes QueueDrainHealth, WebhookLocalDeliveryHealth, RefundReconciliationHealth and TicketingRecoveryHealth. Per-tenant EF `TicketingRecoveryRepository.GetHealthAsync` is **not** the fleet health observer, which still calls native health SQL.

Queue-drain design has unresolved privacy composition: Integration.LastError is arbitrary provider text; Global's existing PDS settlement table SELECT plus a cross-tenant health policy can expose private rows. Prefer C#-maintained non-private metadata in original producer transactions; inventory every producer. Preserve one coherent13-count observation; do not split snapshots or infer business state from diagnostics. Ticketing health's `LastFailureCode.StartsWith("POISON_")` needs typed business evidence, not a broad private-text grant.

IdempotencyReceipt still implements read/claim/complete/expired-count/scrub and is consumed by cleanup and ATProto. Move replay interpretation/retention policy into C# while retaining atomic unique/CAS storage mechanics and ambiguity handling. Test real replay races, scrub, original actor/tenant and uncertain commit.

### SQL that may legitimately remain

- EF-generated migration DDL, constraints/indexes/FKs and model-derived RLS.
- Fixed configured-role GRANT/revoke admission and catalog/ACL checks.
- Proven provider gaps: small parameterized lock/session operations, SKIP LOCKED claim syntax and database clocks, provided business eligibility/order/transitions stay in C#.
- Configurable schema/identifier generators, safe provider literals and exact-time translation that supported EF/provider APIs cannot express.

For every exception record owner, capability gap, inputs/outputs, provider behavior, transaction/connection custody, threat boundary, tests and operator documentation. Ordinary values remain parameterized; identifiers remain separately delimited. No stored procedure, business function/view or trigger is a shortcut to avoid the approved C# design.

## 7. Review gates, diagnostics and external dispositions

### Sonar / duplication / SQL construction

Fresh public quality-gate query at this handoff returned ERROR: new reliability4 (threshold1), security5 (threshold1), duplicated new lines4.3% (maximum3%); maintainability1 and hotspots-reviewed100% passed. This endpoint response is not proof of the analysis revision matching the eventual handoff tip.

The original source audit covered138 S2077 items in75 files:136 supported false-positive recommendations and two real deployment-login literal defects. Those two keys were `AaFLPk18idhKYWVJGE-q` and `AaFLPlDaidhKYWVJGFI4`; the neighboring navigation installer key `AaFLPk18idhKYWVJGE-p` was separately a false-positive recommendation. Later bootstrap literal defect was additional, not silently reclassified into that dated audit. Latest reconciliation had137 items (131 retained plus6 new;7 absent); re-query after final analysis and inspect current source.

Complete E-string literals escape backslash, apostrophe and dollar (`\044`) independently from identifier quoting. Preserve immutable enrollment/body checks; renamed-logins parse but fail old enrollment attestation. No values should be moved into unquoted SQL or identifiers incorrectly value-parameterized.

Original duplication evidence:16,722/325,488 new lines≈5.1%; later4.3% is still failing. Owner-local import/actor/administration/human-source/event-read/fleet-health SQL consolidation does not prove the gate passes. Measure current duplication; extend only genuine common mechanics without merged security authority. No broad exclusions, renamed sinks, warning suppression or generated-artifact edits to manufacture success.

No external Sonar disposition has been written on this owner's behalf. Obtain actual authorized access for disposition and record the result; local false-positive evidence is not scanner closure.

### GitGuardian / Gitleaks

Original GitGuardian38333356 referred to the provisioning compiler tuple of column names/SQL expressions, not credentials. Reconfirm current source and analysis; coordinate an actual false-positive disposition. Do not obfuscate identifiers. Preserve only the explicitly reviewed exact Gitleaks fingerprint, with owner/evidence/removal condition; all other secret rules/files stay scanned. Never publish connection strings or private fixture material in the report, logs or appendices.

### Architecture guards and current source ownership

Re-run `PersistenceCapabilityBoundaryArchitectureTests`, especially:

- `RuntimeRepositories_ShouldContainNoProviderSpecificPersistencePrimitives`.
- `RuntimeDirectAdoApis_ShouldMatchTemporaryRegistry`.

Historical findings named navigation, localization and preference; current source placement differs and bounded two-test runs passed earlier. Latest identity execution custody and admission transaction helpers were relocated to approved Database/ProviderPrimitives with consumer imports changed; fresh final guards remain required. `IsRelational`, ordinary EF connection APIs and calling owner-local schema/readiness helpers are not by themselves the prohibited patterns. No registry expansion/allowlist/suppression is approved merely to pass.

A separate localization/TMS registration failure must be attributed or quarantined against an untouched base; it is not automatically this SQL workstream. Likewise an old Local signin503 reproduced on untouched `e188f261bf`; do not misattribute it to the import coordination repair.

### GitHub checks and Release Impact

At inspected head84fc67c7 GitHub reported draft, BLOCKED, REVIEW_REQUIRED. Release Impact failed; several builds/security/CodeQL checks were still running. Gitar check SUCCESS does not mean its product finding is approved.

The base workflow's changed-file collector stops at3,000 paths; this PR exceeds that. The complete collector is already on the feature branch, but a head-only change cannot activate trusted-base workflow behavior. The maintainer chose the merge disposition. Do not disable or falsify the check, merge/push develop, or create another PR without a new instruction.

### Research and provenance

Tavily MCP was explicitly retried and returned quota status432. No successful Tavily research is claimed. Context7 retrieved official EF concepts, including restoring manually managed underlying connection state; documentation is not native execution proof. Installed Microsoft.Data.Sqlite10.0.10 API docs established timeout0 means infinite. Functional facts only informed project-native implementation; no third-party implementation source, SQL, tests, prose or dependency was copied.

## 8. Verification recipe for the later completion

Use the repository's exact current project commands and discover filters before expensive runs. Native tests must use the approved socket even when only assembly cleanup touches Testcontainers.

```bash
export TMPDIR="$HOME/.cache/agent-tmp"
export DOCKER_HOST="unix://$HOME/.cache/agent-tmp/event-security-podman.sock"
export DOTNET_HOSTBUILDER__RELOADCONFIGONCHANGE=false
```

Coordinate using both `$TMPDIR/event-security-native-class.lock` and `$TMPDIR/event-security-build.lock`, with `flock --close`, one monitored background run and an explicit adequate deadline. Do not start duplicate builds or treat killed/interrupted runs as passes.

Build affected projects:

```bash
dotnet build tests/Event.Persistence.IntegrationTests/Event.Persistence.IntegrationTests.csproj --configuration Release --verbosity quiet
dotnet build tests/Event.API.IntegrationTests/Event.API.IntegrationTests.csproj --configuration Release --verbosity quiet
```

Targeted native example after build:

```bash
dotnet tests/Event.Persistence.IntegrationTests/bin/Release/net10.0/Event.Persistence.IntegrationTests.dll \
  --treenode-filter '/*/*/(ActorOwnershipReaderTests)|(ActorOwnershipReadPermissionTests)/*' \
  --minimum-expected-tests 28 --progress off --maximum-parallel-tests 1
```

The class-segment OR form is important. Whole-path OR filters previously selected an entire architecture assembly. Navigation/preference verified discovery form:
`/*/*/(UserTenantNavigationContractTests)|(TenantPreferenceContractTests)/*`.
Single-class/exact-method filters work. Require nonzero/minimum expected tests and record passed/failed/skipped, elapsed time, exit code, provider, source tree and prerequisite failures separately.

Suggested completion sequence:

1. Reconcile this report with the final pushed source and build receipts; do not replay old private indices, patches, stash or draft migrations.
2. Fix Setup enrollment storage; prove the genuine HTTP importer can reach and finish its assertions.
3. Fix worker/scheduler source custody and finish creator/projection/selected identity activation.
4. Close authored federation/discovery/preference/retention/health slices and scanner finding with actual native authority tests.
5. Retire remaining reachable business SQL in the13 lanes, using focused failing business/adversarial tests first; preserve successful committed EF cuts.
6. Once at workstream exit, run full PostgreSQL/SQLite/SQL Server/MySQL/MariaDB lifecycle/provider matrix, architecture and appropriate Application/Domain/API/Infrastructure suites. Include private management/registration, Local/support admin, AI/publisher, human source, import, health and retention tests.
7. Reconcile new analysis/checks and actual external dispositions. Review docs/secret configuration/public contract/release metadata parity.
8. Final normal hooked commits/push, exact remote hash, clean worktree and honest PR status. Only passing required gates/accepted explicit dispositions establish technical merge readiness.

If a final run produces more than10 failures, cluster by fixture/authority/model/root cause; do not blindly weaken dozens of tests. Reproduce unrelated failures on an untouched base and quarantine with evidence. No backward-compatibility shims, warning suppressions or restoration of the extracted Setup Assistant.

## 9. Handoff packaging and evidence ledger

The initial pending snapshot at84fc67c7 contained319 tracked changed/deleted paths and195 untracked files (514 total). The exact inventory is appended below. Generated artifacts are inseparable from their model source. New helpers must travel with consumers. Do not commit a consumer while dropping its untracked helper. Three native-allocated release fragments and the five durable backlog reports add further packaging paths. Exact commit commands and path manifests are preserved in [the checkpoint packets](pr75-checkpoint-commit-packets.md).

The original worktree index was empty after the Actor commit. The handoff uses explicit owned manifests, normal repository hooks and normal pushes to the same branch. The larger policy cutover is a connected selected-owner/context/interface/installer dependency closure; independent schema, producer, notification, scanner, finite-read and documentation packets are separated. This stopped checkpoint does not certify each intermediate tree as a releasable feature. No database was reset or data deleted for this handoff. Temporary/source-verification caches are not product deliverables.

### Recorded preservation commits and packaging checks

Six source packets were recorded through normal hooks:

| Commit | Preserved outcome |
| --- | --- |
| `ae4160f7c2417b8e0eea22f16d002a0d8516e02f` | Exact generated programme/identity model closure,47 paths. |
| `0291aac05d97861f8d161b7bf1c10b020b62b309` | Connected selected-owner, federation/discovery, bootstrap and creator workflow closure,383 paths. |
| `f67e89bbe8ab3236f8d7654e697f94a60ca40754` | Membership/tenant-status producer coordination,7 paths. |
| `0d23b9a4dcbd6fe778960e1d0c927a1d26608728` | Typed Listmonk recovery/health evidence,13 paths. |
| `13aed43894c21a4688858520c2aae0c5d3173499` | Scanner/document storage and original-transaction admission,17 paths. |
| `437b681ccc3d5bc916a1d30a08ec91aac454af0d` | Finite EF preference/membership/retention/discovery reads,37 paths. |

The final documentation packet adds the13 remaining tracked documentation changes and five intentionally tracked backlog reports. Together the seven packets preserve the original514 paths plus three native-allocated release fragments and five backlog files:522 changed paths relative to the pre-handoff checkpoint. The containing documentation commit identifies this record; do not expect a commit to contain its own future hash.

Physically executed source-checkpoint provenance command:

```bash
dotnet eng/release/src/ISLAMU.ReleaseEngineering/bin/Release/net10.0/ISLAMU.ReleaseEngineering.dll preflight-range --target develop --head HEAD
```

At `437b681ccc`: **exit0**, `change_range_verified: target=develop head=HEAD commits=236 change-ids=169`. Every new source commit passed the enabled commit-msg hook; no bypass was used. The complete source range's `git -c core.fsmonitor=false diff 84fc67c7acd7d8e1c272898a3a4b33fa5a055841..HEAD --check` also exited0. One earlier whitespace check rejected a newly authored extra blank EOF line; removing that line was the only source formatting correction during packaging. No business fix or test weakening was added after the deferral instruction.

The five reports were compared byte-for-byte between the requested root backlog directory and the PR worktree. A bounded filename-only search of those reports for obvious credential/private-key shapes found no matches (exit1 for no matches; wrapper exit0). **That is not a complete Gitleaks/GitGuardian scan.** No new full product/provider/architecture/license/secret-scan acceptance is asserted. The final push, remote-hash comparison, clean-index/worktree confirmation and current GitHub state are reported on the PR/chat after the documentation commit, rather than inventing future evidence in this file.

Machine-local logs below are optional forensic handles, not prerequisites to understand the results already recorded:

- `pr75-actor-final-recovery-green.jOGb06.log`: final Actor28 GREEN.
- `pr75-actor-recovery-enrollment-diagnostic.mzdIis.log`: actual recovery RED and exact Setup callback failure.
- `pr75-setup-runtime-storage-red.PuKfFg.log`: exact Runtime claim read42501.
- `pr75-disposal-no-finally-acceptance.9Qb2zv.log`: committed-disposal9+2 GREEN.
- `pr75-handoff-build.U71OHK.log`: final whole-WIP Release builds completed, wrapper exit0. Persistence integration project: exit0,128.72s,1654 warnings,0 errors. API integration project: exit0,102.26s,762 warnings,0 errors. These are build receipts, not a final passing test matrix.

**Packaging completion does not close any unchecked implementation or verification task above.** The maintainer may choose to force-merge; this document preserves the actual remaining engineering work rather than representing that choice as a passing technical gate.

### Post-push evidence and dependency warning

The initial seven preservation packets were normally pushed through `2c5220bb3e2387dbcbc359846bb3a1a536d33549`. The remote branch returned that exact hash; fsmonitor-disabled status was empty. Final native release-range preflight at that documentation checkpoint passed with237 commits and169 Change-Ids. The two explicit path manifests were also compared: the522-path committed range exactly matched all owned packet paths, with no missing helper or extra staged file.

During that successful push, GitHub reported **119 default-branch dependency vulnerabilities:92 high and27 moderate**, linking to the repository's Dependabot security view. This is a hosting-platform warning, not a fresh PR-specific dependency audit; the PR may change affected versions, and those counts must not be presented as a reviewed inventory for this candidate. Later completion must retrieve accessible advisory/package/version evidence, attribute each applicable issue and remediate or explicitly disposition it under normal license/provenance and regression rules. Do not suppress alerts or assume the final dependency/license/security scan is green.

This small post-push evidence update is a documentation-only follow-up to the seven initial preservation packets. It does not implement dependency upgrades or change the known runtime blockers. The final containing hash and ready-for-review state are reported on the PR; ready-for-review is not a passing technical merge/release gate.

## 10. Broader security programme: preserve scope without restarting it

The SQL/EF sections above are the current source-reviewed workstream. The older PR also contains unfinished or execution-deferred security work outside Persistence. Its unchecked boxes are not a fresh defect inventory: many source fixes were committed without reconciling every old checkbox. Do not reimplement them blindly or automatically resume the entire programme.

The complete bounded acceptance extraction is retained in [the broader programme acceptance snapshot](pr75-programme-open-acceptance-snapshot.md). It preserves every older phase heading, starting path list, dependencies and unchecked task's acceptance description, including exact historical scenario aliases. This makes later reconciliation possible without the ignored implementation folder.

The capabilities to reconcile against the final pushed source are:

1. **External credentials and provider administration:** genuine Google/Keycloak credential lifetime; coherent PostgreSQL tenant/RLS activation and portable binding uniqueness; connection-bound webhook destinations; fresh request-scoped Keycloak administration with partial-completion recovery; separate least-privilege Keycloak lifecycle identity and rotation.
2. **Privileged transports and deployment:** all real AI transport adapters use the selected endpoint/credential policy; Cerbos instance/BYO separation, publication prerequisite/receipt and removal of one-time administration from runtime; independent external Data Protection ring custody/recovery/overlap; isolated consumer roots and machine identities through configuration, Compose and Aspire.
3. **Authentication and validation:** genuinely signed token/context matrix rather than fabricated claims; uniform non-enumerating Local login/recovery; shared password policy and adaptive salted hash policy; bounded nullable custom-property patterns and intentional syntax/wire behavior.
4. **Resource budgets:** bound actual AI and Coop reads before allocation/parsing; enforce ETag/idempotency capture at threshold crossings; complete file intake/final-delivery authorization, relative availability, protected destination hops and restore behavior; production forwarding/effective-host/reserved-route and private operational listener authority; finite early pre-auth admission and aggregate fleet ingress budgets.
5. **Caller and data authority:** complete caller/transaction inventory for resource/object/property actions, minimal public identity projections with regenerated clients, race-safe tenant slug allocation and post-commit cache effects, and durable business-operation identity across ambiguous completion. Never transparently retry a credential-bearing mutation with fresh authority.
6. **Confidential diagnostics and clients:** inventory final log/metric/template/parser sinks, preserve useful fixed outcomes without private values; safe API-fed Markdown/URL/identity rendering, BFF/Combined ingress/header/local-return parity; exact Window/Worker/MessagePort owner/schema/correlation/cleanup and no stale or secret-bearing replies.
7. **AI actions and native processes:** server-owned model/tool selection, exact confirmation preview and current provider authorization; prompt/tool isolation, action lifetime and original primary/image custody; typed process argv (no shell interpretation), bounded output, cancellation/kill/reap and diagnostic redaction.
8. **Candidate admission and independent evidence:** bind actual tested revision/image/config/profile and trusted signer to nonzero executed results. Preserve all209 references,31 qualified findings (27 consultation plus4 workstream),22 classes and50 scenarios with evidence or bounded non-applicability;10 BUG/12 VULN records need explicit dispositions. Recomputed digests are content identity, not authenticated evidence. Finish SBOM/license, realm/roles, ingress, rings, restore, generated-wire, non-Testing activation/shutdown and authorized independent security assessment.

**Excluded AI image-authority candidate:** an older independently authored candidate was retained only in ignored verification directories, not selectively integrated after implementation stopped. Its source/evidence discussed exact preview rechecks, Active conversation admission before a new primary, primary linkage lifetime, provider-byte handoff outside database transactions, retained finalized upload/object identity, no replacement of a selected image and independently authorized recovery after ambiguous writes. Earlier notes say the Active-parent gap was closed in candidate source, but no current integrated acceptance is established. The last native byte-finalization attempt was not accepted as passing. Do not copy an entire cached repository or call those isolated source claims current PR behavior. Reconcile the actual `ConfirmAiProposedActionCommandHandler`, `AttachAiDraftImageCommandHandler`, `AiToolExecution`, `AiEventDraftRecoveryRepository`, `AiEventDraftRecoveryTests` and `NativeEventPublicActionsHttpTests.AiGraphAuthority` against committed source and write real outcome tests before any future selective integration.

Historical final assurance also recorded PaymentReconciliationNativeAuthorityTests32pass/1bootstrap failure and infrastructure constructor/build interruptions; those receipts predate current composition and are not current passing or failing classifications. Reconcile rather than absorbing unrelated rot. Browser/manual deployment acceptance, authenticated provider/ring recovery, complete dependency/provenance audit and final candidate admission remain unestablished.

Supporting dated audits are preserved as [the per-finding SQL review](pr75-historical-sql-review.md) and [the duplication investigation](pr75-historical-duplication-review.md). They retain rationale and source anchors, not external Sonar dispositions. Current gate status and current-source corrections in this main report take precedence.

## 11. Exact pending-path inventory

Snapshot paths below are repository-relative and record source preservation, not individual implementation completion.

```text
dev/_journal/journal.md
docs/internal/API.md
docs/internal/BACKUP_RESTORE_UPGRADE.md
docs/internal/CONFIGURATION_MANIFEST.md
docs/internal/INTEGRATIONS.md
docs/internal/OPERATIONS.md
docs/internal/SECRETS.md
docs/public/documentation/readme/administration-and-branding/admin-guide.md
docs/public/documentation/readme/communications-and-notifications/listmonk.md
docs/public/documentation/readme/configuration-and-operations/backup-restore-upgrade.md
docs/public/documentation/readme/configuration-and-operations/environment-variables.md
docs/public/documentation/readme/configuration-and-operations/troubleshooting-and-health.md
islamic-value-sensitive-design/workstreams/i-vsd-event-api-security-remediation.md
schemas/islamu-event.md
src/Event.MigrationService/Worker.cs
src/Explore.API/Extensions/AuthenticationExtensions.cs
src/Explore.API/Extensions/ConfigurationExtensions.cs
src/Explore.API/Hosting/AgentBrowserPersonaStartup.cs
src/Explore.API/Hosting/ApiHostStartupExtensions.cs
src/Explore.Application/Configuration/LocalIdentityOptions.cs
src/Explore.Application/Contracts/Admissions/AdmissionScannerCapabilityContracts.cs
src/Explore.Application/Contracts/Operations/IPolicyMutationExecutionScopeFactory.cs
src/Explore.Application/Contracts/Persistence/IAtprotoGovernanceSettingRepository.cs
src/Explore.Application/Contracts/Persistence/IExternalLoginAuthenticationRepository.cs
src/Explore.Application/Contracts/Persistence/IHumanSettingsMutationPreparation.cs
src/Explore.Application/Contracts/Persistence/IIntegrationSyncOutboxRepository.cs
src/Explore.Application/Contracts/Persistence/IOrdinarySettingsMutationStore.cs
src/Explore.Application/Contracts/Persistence/IPrivacyErasureStateRepository.cs
src/Explore.Application/Contracts/Persistence/IUserRepository.cs
src/Explore.Application/Features/EmailDispatch/IHumanEmailMutationPreparation.cs
src/Explore.Application/Features/InstanceAdmin/Handlers/Commands/ApplyInstanceTenantPlanAssignmentCommandHandler.cs
src/Explore.Application/Features/InstanceAdmin/Handlers/Commands/LockInstanceTenantSettingCommandHandler.cs
src/Explore.Application/Features/InstanceAdmin/Handlers/Commands/SetInstanceTenantSettingCommandHandler.cs
src/Explore.Application/Features/InstanceAdmin/Handlers/Commands/TransitionInstanceTenantLifecycleCommandHandler.cs
src/Explore.Application/Features/InstanceAdmin/Handlers/Commands/UnlockInstanceTenantSettingCommandHandler.cs
src/Explore.Application/Features/InstanceOnboarding/Handlers/Commands/SaveInstanceOnboardingProfileCommandHandler.cs
src/Explore.Application/Features/InstanceOnboarding/Handlers/Commands/UpdateInstanceSubResourceHandlers.cs
src/Explore.Application/Features/InstanceOnboarding/Services/ITrustedSetupSettingsWriter.cs
src/Explore.Application/Features/InstanceOnboarding/Services/InstanceOnboardingCompletionOperation.cs
src/Explore.Application/Features/InstanceOnboarding/Services/LocalAdministratorBootstrapOperation.cs
src/Explore.Application/Features/InstanceOnboarding/Services/ProviderConfigurationMutation.cs
src/Explore.Application/Features/ManagedProviderProvisioning/Handlers/Commands/EnsureManagedProviderClientProvisionedCommandHandler.cs
src/Explore.Application/Features/ManagedProviderProvisioning/HumanManagedProviderProvisioningCoordinator.cs
src/Explore.Application/Features/Management/Handlers/ManagedTenantProvisioningHandlers.cs
src/Explore.Application/Features/Settings/Handlers/Commands/LockSettingCommandHandler.cs
src/Explore.Application/Features/Settings/Handlers/Commands/ResetSettingCommandHandler.cs
src/Explore.Application/Features/Settings/Handlers/Commands/UnlockSettingCommandHandler.cs
src/Explore.Application/Features/Settings/Handlers/Commands/UpdateSettingBatchCommandHandler.cs
src/Explore.Application/Features/Settings/Handlers/Commands/UpdateSettingCommandHandler.cs
src/Explore.Application/Features/Settings/MixedSettingsBatchMutation.cs
src/Explore.Application/Features/TenantUsers/Handlers/Commands/RemoveTenantMembershipCommandHandler.cs
src/Explore.Application/Features/Tenants/Handlers/Commands/CreateTenant/CreateTenantCommandHandler.cs
src/Explore.Application/Features/Users/Handlers/Commands/SyncUserCommandHandler.cs
src/Explore.Application/OperationServicesRegistration.cs
src/Explore.Application/Operations/HumanScopeQueryHandler.cs
src/Explore.Application/Operations/MutationScopeCommandHandler.cs
src/Explore.Application/Operations/PolicyMutationCommandScope.cs
src/Explore.Application/Services/EventResourceProviderControlPlane.cs
src/Explore.Application/Services/Federation/AtprotoJetstreamTenantPresentationResolver.cs
src/Explore.Application/Services/Federation/AtprotoPdsRecoveryPolicyResolver.cs
src/Explore.Application/Services/IdentityAccountResolver.cs
src/Explore.Application/Services/InstanceOperatorIdentityService.cs
src/Explore.Application/Services/PrivacyErasureApplier.cs
src/Explore.Application/Services/Registration/AdmissionCheckInOperationsService.cs
src/Explore.Application/Services/Registration/AdmissionCheckInService.cs
src/Explore.Application/Services/Registration/AdmissionScannerCapabilityService.cs
src/Explore.Application/Services/RetainedAuthorityPrivacyErasureWorkflow.cs
src/Explore.Application/Settings/StorageSettingsMutation.cs
src/Explore.Domain/IntegrationSyncOutbox.cs
src/Explore.Infrastructure/Authentication/LocalIdentityLifecycleDeliveryWorker.cs
src/Explore.Infrastructure/Authentication/LocalIdentityLifecycleEmailService.cs
src/Explore.Infrastructure/Authentication/LocalJwtTokenGenerator.cs
src/Explore.Infrastructure/InfrastructureServicesRegistration.cs
src/Explore.Infrastructure/IntegrationSyncDrainService.cs
src/Explore.Infrastructure/Mail/HumanEmailMutationPreparation.Mixed.cs
src/Explore.Infrastructure/Mail/HumanEmailMutationPreparation.cs
src/Explore.Infrastructure/Services/ConfiguredAdministratorBootstrapStartupRunner.cs
src/Explore.Infrastructure/Services/HierarchicalSettingsReader.cs
src/Explore.Infrastructure/Services/HierarchicalSettingsResolver.cs
src/Explore.Infrastructure/Services/HumanManagedProviderProvisioningPreparationRegistration.cs
src/Explore.Infrastructure/Services/HumanSettingsMutationPreparation.EventResourceProvider.cs
src/Explore.Infrastructure/Services/HumanSettingsMutationPreparation.cs
src/Explore.Infrastructure/Services/HumanTenantMutationPreparation.cs
src/Explore.Infrastructure/Services/TrustedSetupSettingsAuthority.cs
src/Explore.Persistence.Migrations.MySql/Migrations/ExploreDbContextModelSnapshot.cs
src/Explore.Persistence.Migrations.MySql/Migrations/Identity/ExternalIdentityDbContextModelSnapshot.cs
src/Explore.Persistence.Migrations.SqlServer/Migrations/ExploreDbContextModelSnapshot.cs
src/Explore.Persistence.Migrations.SqlServer/Migrations/Identity/ExternalIdentityDbContextModelSnapshot.cs
src/Explore.Persistence.Migrations.Sqlite/Migrations/ExploreDbContextModelSnapshot.cs
src/Explore.Persistence.Migrations.Sqlite/Migrations/Identity/ExternalIdentityDbContextModelSnapshot.cs
src/Explore.Persistence/Configurations/Entities/IntegrationSyncOutboxConfiguration.cs
src/Explore.Persistence/Configurations/Entities/SystemSettingConfiguration.cs
src/Explore.Persistence/Configurations/Entities/TenantSettingConfiguration.cs
src/Explore.Persistence/Database/PolicyMutationExecutionScopeFactory.cs
src/Explore.Persistence/Database/PrimaryDatabaseProviderComposition.cs
src/Explore.Persistence/Database/ProviderPrimitives/AtprotoGovernanceSettingQuery.cs
src/Explore.Persistence/Database/ProviderPrimitives/ConfigurationImportRepositoryNativeOperations.cs
src/Explore.Persistence/Database/ProviderPrimitives/ConfigurationRepositoryNativeOperations.cs
src/Explore.Persistence/Database/ProviderPrimitives/EventDiscoveryDisclosureProviderOperations.cs
src/Explore.Persistence/Database/ProviderPrimitives/EventDiscoveryDisclosureTransactionInterceptor.cs
src/Explore.Persistence/Database/ProviderPrimitives/EventDiscoverySourceProviderOperations.cs
src/Explore.Persistence/Database/ProviderPrimitives/EventResourceAuditOwnerQuery.cs
src/Explore.Persistence/Database/ProviderPrimitives/GenericOutboxExecutionAuthority.cs
src/Explore.Persistence/Database/ProviderPrimitives/IntegrationSyncDiscoveryQuery.cs
src/Explore.Persistence/Database/ProviderPrimitives/InventoryHoldExpiryQuery.cs
src/Explore.Persistence/Database/ProviderPrimitives/QueueDrainHealthQuery.cs
src/Explore.Persistence/Database/ProviderPrimitives/RegistrationRetentionTenantDiscoveryRepository.cs
src/Explore.Persistence/Database/ProviderPrimitives/UserTenantMembershipNativeQuery.cs
src/Explore.Persistence/EfCoreUnitOfWork.cs
src/Explore.Persistence/ExploreDbContext.DbSets.cs
src/Explore.Persistence/ExploreDbContext.Disclosure.cs
src/Explore.Persistence/ExploreDbContext.HumanTenantAuthority.cs
src/Explore.Persistence/ExploreDbContext.QueryFilters.cs
src/Explore.Persistence/ExploreDbContext.SaveChanges.cs
src/Explore.Persistence/ExploreDbContext.StorageReferences.cs
src/Explore.Persistence/ExploreDbContext.cs
src/Explore.Persistence/Identity/ExternalIdentityDatabaseMigrator.cs
src/Explore.Persistence/Identity/ExternalIdentityDbContextFactory.cs
src/Explore.Persistence/Identity/IdentityDatabaseProviderComposition.cs
src/Explore.Persistence/Identity/LocalIdentityAuthService.cs
src/Explore.Persistence/Identity/LocalIdentityCredentialStateStore.Administration.cs
src/Explore.Persistence/Identity/LocalIdentityCredentialStateStore.AdministrationEffects.cs
src/Explore.Persistence/Identity/LocalIdentityCredentialStateStore.cs
src/Explore.Persistence/Identity/LocalIdentityLifecycleDeliveryStore.cs
src/Explore.Persistence/Identity/LocalIdentityLifecycleStore.cs
src/Explore.Persistence/Identity/Migrations/ExternalIdentityDbContextModelSnapshot.cs
src/Explore.Persistence/InventoryHoldExpiryServicesRegistration.cs
src/Explore.Persistence/Migrations/ExploreDbContextModelSnapshot.cs
src/Explore.Persistence/OriginalOwnerSettlement.cs
src/Explore.Persistence/PersistenceServicesRegistration.cs
src/Explore.Persistence/RelationalSettingMutationLock.cs
src/Explore.Persistence/Repositories/AdmissionCheckInRepository.cs
src/Explore.Persistence/Repositories/AdmissionScannerCapabilityRepository.cs
src/Explore.Persistence/Repositories/AdmissionTargetOperationsRepository.cs
src/Explore.Persistence/Repositories/AnonymousCancellationRepository.cs
src/Explore.Persistence/Repositories/AtprotoGovernanceSettingRepository.cs
src/Explore.Persistence/Repositories/EventResourceAuditOwnerDiscoveryRepository.cs
src/Explore.Persistence/Repositories/IntegrationSyncOutboxRepository.cs
src/Explore.Persistence/Repositories/PrivacyErasureStateRepository.cs
src/Explore.Persistence/Repositories/SystemSettingRepository.cs
src/Explore.Persistence/Repositories/TenantRepository.cs
src/Explore.Persistence/Repositories/TenantSettingRepository.cs
src/Explore.Persistence/Repositories/TenantUserRepository.cs
src/Explore.Persistence/Repositories/UserRepository.cs
src/Explore.Persistence/Repositories/UserTenantMembershipRepository.cs
src/Explore.Persistence/Schema/PortableRelationalModelPolicy.cs
src/Explore.Persistence/Schema/ProviderPrimitives/ExploreDatabaseMigrator.cs
src/Explore.Persistence/Schema/ProviderPrimitives/PostgresExternalLoginReadPermissions.cs
src/Explore.Persistence/Schema/ProviderPrimitives/PostgresLookupCatalogueReadPermissions.cs
src/Explore.Persistence/Schema/ProviderPrimitives/PostgresTenantApiKeyStoragePermissions.cs
src/Explore.Persistence/Schema/ProviderPrimitives/PostgresUserTenantNavigationReadPermissions.cs
src/Explore.Persistence/Security/ConfigurationManifestBootstrapOwnerFactory.Compilation.cs
src/Explore.Persistence/Security/ConfigurationManifestBootstrapOwnerFactory.cs
src/Explore.Persistence/Security/HumanAdministratorCommitScope.cs
src/Explore.Persistence/Security/HumanAuthorizationRouteSource.cs
src/Explore.Persistence/Security/HumanMixedSettingsSourceProtocol.cs
src/Explore.Persistence/Security/HumanPrincipalSourceFence.cs
src/Explore.Persistence/Security/HumanTenantPreferenceStore.cs
src/Explore.Persistence/Security/OrdinarySettingsDataImage.cs
src/Explore.Persistence/Security/OrdinarySettingsMutationStore.cs
src/Explore.Persistence/Security/PolicySourceSetFence.cs
src/Explore.Persistence/Security/ProviderPrimitives/ActorPublisherCatalogProof.cs
src/Explore.Persistence/Security/ProviderPrimitives/ConfigurationManifestBootstrapData.cs
src/Explore.Persistence/Security/ProviderPrimitives/ConfigurationManifestBootstrapProviderOperations.cs
src/Explore.Persistence/Security/ProviderPrimitives/EventReadProjectionSql.cs
src/Explore.Persistence/Security/ProviderPrimitives/HumanAuthorizationRouteQueries.cs
src/Explore.Persistence/Security/ProviderPrimitives/HumanPrincipalSourceQueries.cs
src/Explore.Persistence/Security/ProviderPrimitives/HumanTenantPreferenceDataOperations.cs
src/Explore.Persistence/Security/ProviderPrimitives/PolicySourceSetProviderLock.cs
src/Explore.Persistence/Security/ProviderPrimitives/PostgresAtprotoGovernanceContract.cs
src/Explore.Persistence/Security/ProviderPrimitives/PostgresAtprotoModerationCompensationContract.cs
src/Explore.Persistence/Security/ProviderPrimitives/PostgresConfigurationManifestBootstrapContract.Sql.cs
src/Explore.Persistence/Security/ProviderPrimitives/PostgresConfigurationManifestBootstrapContract.cs
src/Explore.Persistence/Security/ProviderPrimitives/PostgresDiscoveryTraversalContract.Installer.cs
src/Explore.Persistence/Security/ProviderPrimitives/PostgresDiscoveryTraversalContract.Sql.cs
src/Explore.Persistence/Security/ProviderPrimitives/PostgresDiscoveryTraversalContract.cs
src/Explore.Persistence/Security/ProviderPrimitives/PostgresEmailDeliveryPolicyContract.Controls.cs
src/Explore.Persistence/Security/ProviderPrimitives/PostgresEventResourceAuditRetentionContract.cs
src/Explore.Persistence/Security/ProviderPrimitives/PostgresEventResourceProviderContract.Installer.cs
src/Explore.Persistence/Security/ProviderPrimitives/PostgresExternalApiKeyAuthenticationContract.cs
src/Explore.Persistence/Security/ProviderPrimitives/PostgresIntegrationSyncDiscoveryContract.cs
src/Explore.Persistence/Security/ProviderPrimitives/PostgresInventoryHoldExpiryDiscoveryContract.cs
src/Explore.Persistence/Security/ProviderPrimitives/PostgresManagedProviderProvisioningContract.Attestation.cs
src/Explore.Persistence/Security/ProviderPrimitives/PostgresMixedSettingsContract.Attestation.cs
src/Explore.Persistence/Security/ProviderPrimitives/PostgresMixedSettingsContract.Sql.cs
src/Explore.Persistence/Security/ProviderPrimitives/PostgresOperatorIdentityEditorContract.Attestation.cs
src/Explore.Persistence/Security/ProviderPrimitives/PostgresOrdinarySettingsContract.Attestation.cs
src/Explore.Persistence/Security/ProviderPrimitives/PostgresOrdinarySettingsContract.Disclosure.cs
src/Explore.Persistence/Security/ProviderPrimitives/PostgresOrdinarySettingsContract.Sql.cs
src/Explore.Persistence/Security/ProviderPrimitives/PostgresOrdinarySettingsContract.cs
src/Explore.Persistence/Security/ProviderPrimitives/PostgresProviderConfigurationContract.Attestation.cs
src/Explore.Persistence/Security/ProviderPrimitives/PostgresPublicEventResponseClockContract.Attestation.cs
src/Explore.Persistence/Security/ProviderPrimitives/PostgresPublicEventResponseClockContract.Installer.cs
src/Explore.Persistence/Security/ProviderPrimitives/PostgresPublicationFanoutExecutionContract.Installer.cs
src/Explore.Persistence/Security/ProviderPrimitives/PostgresQueueDrainHealthContract.cs
src/Explore.Persistence/Security/ProviderPrimitives/PostgresRegistrationPrivateReadContract.Installer.cs
src/Explore.Persistence/Security/ProviderPrimitives/PostgresRegistrationPrivateReadContract.cs
src/Explore.Persistence/Security/ProviderPrimitives/PostgresRegistrationRetentionDiscoveryContract.cs
src/Explore.Persistence/Security/ProviderPrimitives/PostgresResolverConfigurationContract.Attestation.cs
src/Explore.Persistence/Security/ProviderPrimitives/PostgresStorageConfigurationContract.Attestation.cs
src/Explore.Persistence/Security/ProviderPrimitives/PostgresStorageProbeSourceContract.Attestation.cs
src/Explore.Persistence/Security/ProviderPrimitives/PostgresSupportAdministrationContract.Attestation.cs
src/Explore.Persistence/Security/ProviderPrimitives/PostgresTenantPreferenceContract.cs
src/Explore.Persistence/Security/ProviderPrimitives/PostgresTenantRuntimeReadiness.cs
src/Explore.Persistence/Security/ProviderPrimitives/PostgresTenantSessionInterceptor.cs
src/Explore.Persistence/Security/ProviderPrimitives/PostgresUserTenantMembershipContract.cs
src/Explore.Persistence/Security/ProviderPrimitives/ProvisioningSharedWriterAssertions.cs
src/Explore.Persistence/Seed/DatabaseSeeder.cs
src/Explore.Persistence/Seed/LookupTableSeeder.cs
src/Explore.Persistence/Seed/SeedIds.cs
src/Explore.Persistence/Services/AtprotoModerationCompensationStore.cs
src/Explore.Persistence/Services/EventDiscoveryDisclosureMutationScope.cs
src/Explore.Persistence/Services/EventResourceSettingsWriter.cs
src/Explore.Persistence/Services/HumanSettingsDataBoundary.cs
src/Explore.Persistence/Services/InstanceApiKeyCapabilityStore.cs
src/Explore.Persistence/Services/IntegrationSyncQueueDiscovery.cs
src/Explore.Persistence/Services/InternalEmailDeliverySettingsWriter.cs
src/Explore.Persistence/Services/InventoryHoldExpiryDiscovery.cs
src/Explore.Persistence/Services/MixedSettingsBatchStore.Location.cs
src/Explore.Persistence/Services/MixedSettingsBatchStore.cs
src/Explore.Persistence/Services/ProviderConfigurationSettingsWriter.cs
src/Explore.Persistence/Services/ResolverConfigurationSettingsWriter.cs
src/Explore.Persistence/Services/StorageConfigurationWriter.cs
src/Explore.Persistence/Services/TypedSettingsOriginalStore.Governance.cs
src/Explore.Persistence/Services/TypedSettingsOriginalStore.cs
src/Explore.Persistence/Services/VisitorAccessSettingsWriter.cs
tests/Event.API.IntegrationTests/Authentication/HumanAuthenticationLifetimeTests.cs
tests/Event.API.IntegrationTests/Authentication/HumanSettingsMutationPreparationTests.Mixed.cs
tests/Event.API.IntegrationTests/Authentication/HumanSettingsMutationPreparationTests.MixedSources.cs
tests/Event.API.IntegrationTests/Authentication/HumanSettingsMutationPreparationTests.Sources.cs
tests/Event.API.IntegrationTests/Authentication/HumanTenantDeletionOwnershipTests.cs
tests/Event.API.IntegrationTests/Authentication/ManagedProviderProvisioningOrchestrationTests.cs
tests/Event.API.IntegrationTests/Authentication/TenantPlanCatalogueOriginalAdmissionTests.Scenarios.cs
tests/Event.API.IntegrationTests/Authentication/TenantPlanCatalogueOriginalAdmissionTests.cs
tests/Event.API.IntegrationTests/Authentication/TrustedSetupSettingsAuthorityTests.cs
tests/Event.API.IntegrationTests/Event.API.IntegrationTests.csproj
tests/Event.API.IntegrationTests/Features/ConfigurationManifestBootstrapWorkerNativeTests.cs
tests/Event.API.IntegrationTests/Features/LocalRbacAuthorizationTests.cs
tests/Event.API.IntegrationTests/Features/NativeListmonkHttpTests.cs
tests/Event.API.IntegrationTests/Features/SetupLiveAuthoritySecurityTests.cs
tests/Event.API.IntegrationTests/Fixtures/AuthenticatedWebApplicationFactory.cs
tests/Event.API.IntegrationTests/Fixtures/LocalAdmissionWebApplicationFactory.cs
tests/Event.API.IntegrationTests/Fixtures/LocalIdentityLifecycleHttpFixture.cs
tests/Event.API.IntegrationTests/Fixtures/TestDbContextServiceCollectionExtensions.cs
tests/Event.Application.UnitTests/Contracts/Admissions/AdmissionCheckInOrchestrationRedTests.cs
tests/Event.Application.UnitTests/Features/InstanceAdmin/Commands/TenantIdentityActivationConcurrencyTests.cs
tests/Event.Application.UnitTests/Features/InstanceOnboarding/InstanceOnboardingCompletionOperationTests.cs
tests/Event.Application.UnitTests/Notifications/DefaultNotificationOrchestratorTests.cs
tests/Event.Application.UnitTests/Profiles/ActorFederationMapperTests.cs
tests/Event.Application.UnitTests/Settings/AtprotoJetstreamTenantPresentationResolverTests.cs
tests/Event.Architecture.Tests/ConfiguredAdministratorBootstrapArchitectureTests.cs
tests/Event.Persistence.IntegrationTests/AdmissionCheckInPersistenceRedTests.cs
tests/Event.Persistence.IntegrationTests/ConfigurationManifest/ConfigurationManifestAtomicPersistenceTests.cs
tests/Event.Persistence.IntegrationTests/ConfigurationManifest/ConfigurationManifestBootstrapOriginTests.cs
tests/Event.Persistence.IntegrationTests/ConfigurationManifest/ConfigurationManifestBootstrapTestHost.cs
tests/Event.Persistence.IntegrationTests/ConfigurationManifest/ConfigurationManifestConcurrencyTests.cs
tests/Event.Persistence.IntegrationTests/ConfigurationManifest/ConfigurationManifestProviderValueNativeTests.cs
tests/Event.Persistence.IntegrationTests/ConfigurationManifest/PostgresManifestExportAuthorityTests.cs
tests/Event.Persistence.IntegrationTests/Discovery/PostgresDiscoveryTraversalTests.cs
tests/Event.Persistence.IntegrationTests/EventResourceAuditRetentionPersistenceTests.cs
tests/Event.Persistence.IntegrationTests/EventResourceCleanupTests.cs
tests/Event.Persistence.IntegrationTests/Federation/ModerationCommandCompensationTests.cs
tests/Event.Persistence.IntegrationTests/Fixtures/AuthenticatedSettingsFixture.Postgres.cs
tests/Event.Persistence.IntegrationTests/Fixtures/AuthenticatedSettingsFixture.Sqlite.cs
tests/Event.Persistence.IntegrationTests/Fixtures/AuthenticatedSettingsFixture.cs
tests/Event.Persistence.IntegrationTests/Fixtures/EmailDispatchSqliteFixture.cs
tests/Event.Persistence.IntegrationTests/Fixtures/InstanceSettingsCommandFixture.cs
tests/Event.Persistence.IntegrationTests/Fixtures/LocalIdentitySqliteTemplate.cs
tests/Event.Persistence.IntegrationTests/Fixtures/LocalLifecycleDeliveryFixture.cs
tests/Event.Persistence.IntegrationTests/Fixtures/PostgreSqlContainerFixture.cs
tests/Event.Persistence.IntegrationTests/HumanPrincipalNativeSourceFenceTests.cs
tests/Event.Persistence.IntegrationTests/Identity/LocalBootstrapConvergenceTests.CompletionFixture.cs
tests/Event.Persistence.IntegrationTests/Identity/LocalBootstrapConvergenceTests.SettingsCustody.cs
tests/Event.Persistence.IntegrationTests/Identity/LocalBootstrapConvergenceTests.cs
tests/Event.Persistence.IntegrationTests/Identity/LocalCredentialCreationTests.cs
tests/Event.Persistence.IntegrationTests/Identity/LocalCredentialFirstUseTests.cs
tests/Event.Persistence.IntegrationTests/Identity/LocalCredentialLifecycleTests.cs
tests/Event.Persistence.IntegrationTests/Identity/LocalIdentityAuthServiceTests.cs
tests/Event.Persistence.IntegrationTests/Identity/ManagedTenantLocalAdministratorLinkageTests.cs
tests/Event.Persistence.IntegrationTests/Migrations/ApplicationInitialLifecycleProviderTests.cs
tests/Event.Persistence.IntegrationTests/Onboarding/ConfiguredAdministratorBootstrapStartupConcurrencyTests.cs
tests/Event.Persistence.IntegrationTests/OrdinarySettingsNativeContractTests.Disclosure.cs
tests/Event.Persistence.IntegrationTests/Privacy/GlobalLocationPrivacyErasureTests.cs
tests/Event.Persistence.IntegrationTests/Privacy/PostgresSubjectPrivacyErasureAuthorityTests.cs
tests/Event.Persistence.IntegrationTests/Privacy/TenantMembershipRemovalTests.cs
tests/Event.Persistence.IntegrationTests/Repositories/EventDiscoveryDisclosureMutationTests.cs
tests/Event.Persistence.IntegrationTests/Repositories/IntegrationSyncOutboxRepositoryTests.cs
tests/Event.Persistence.IntegrationTests/Repositories/LocationUnicodeWriteAtomicityTests.cs
tests/Event.Persistence.IntegrationTests/Repositories/PolicyMutationExecutionScopeSqliteFixture.cs
tests/Event.Persistence.IntegrationTests/Repositories/PolicyMutationExecutionScopeTests.cs
tests/Event.Persistence.IntegrationTests/Repositories/PublicEventDetailNativeContractTests.Catalogue.cs
tests/Event.Persistence.IntegrationTests/Repositories/QueueDrainHealthNativeAuthorityTests.cs
tests/Event.Persistence.IntegrationTests/Repositories/TenantLifecycleTransitionRepositoryTests.cs
tests/Event.Persistence.IntegrationTests/Repositories/WebhookLocalDeliveryNativeAuthorityTests.Health.cs
tests/Event.Persistence.IntegrationTests/Seed/DatabaseSeederTests.cs
tests/Event.Persistence.IntegrationTests/Settings/AtprotoFederationPersistenceTests.cs
tests/Event.Persistence.IntegrationTests/SupportAdministrationNativeContractTests.Custody.cs
tests/Event.Persistence.IntegrationTests/SupportAdministrationNativeContractTests.cs
tests/Event.Persistence.IntegrationTests/TenantIsolation/AuthenticationEfSourceTests.cs
tests/Event.Persistence.IntegrationTests/TenantIsolation/ExternalApiKeyRepositoryBypassTests.cs
tests/Event.Persistence.IntegrationTests/TenantIsolation/PostgresAuthenticationLookupAuthorityTests.cs
tests/Event.Persistence.IntegrationTests/TenantIsolation/PostgresEventResourceAuditRetentionLeaseTests.cs
tests/Event.Persistence.IntegrationTests/TenantIsolation/PostgresInventoryHoldExpiryScopeTests.cs
tests/Event.Persistence.IntegrationTests/TenantIsolation/PostgresMachineMutationAuthorityTests.cs
tests/Event.Persistence.IntegrationTests/TenantIsolation/PostgresMachineRoomMutationAuthorityTests.cs
tests/Event.Persistence.IntegrationTests/TenantIsolation/PostgresModerationCompensationAuthorityTests.cs
tests/Event.Persistence.IntegrationTests/TenantIsolation/PostgresRegistrationRetentionAuthorityTests.cs
tests/Event.Persistence.IntegrationTests/TenantIsolation/PostgresTenantApplicationTableIsolationTests.cs
tests/Event.Persistence.IntegrationTests/TenantIsolation/PostgresUserIdentityLookupAuthorityTests.cs
tests/Event.Persistence.IntegrationTests/TenantIsolation/RegistrationRetentionUnsupportedProviderTests.cs
tests/Event.Persistence.IntegrationTests/TenantIsolation/ScannerAuthenticationReadPortTests.cs
tests/Event.Persistence.IntegrationTests/TenantIsolation/TenantPreferenceContractTests.cs
tests/Event.Persistence.IntegrationTests/TenantIsolation/UserMembershipReadPortTests.cs
tests/Event.Persistence.IntegrationTests/TicketTransferConcurrencyTests.cs
tests/Explore.Infrastructure.Tests/Authentication/LocalJwtTokenGeneratorTests.cs
tests/Explore.Infrastructure.Tests/Infrastructure/ConfiguredAdministratorBootstrapProviderTests.cs
tests/Explore.Infrastructure.Tests/Infrastructure/Listmonk/IntegrationSyncDrainServiceTests.cs
tests/Explore.Infrastructure.Tests/Settings/HierarchicalSettingsResolverTests.cs
tests/Shared/Settings/SmtpSettingsDatabase.cs
docs/internal/integrations/listmonk-outbox.md
docs/internal/releases/changes/CHG-01M58P7943Z4SP14QHVSR1YHPS.yaml
docs/internal/releases/changes/CHG-01M5D3N71ES2PKP3818HDN69K2.yaml
docs/internal/releases/changes/CHG-01M5DMVM8Q7ER86B0VXE0DKPMM.yaml
src/Explore.Application/Contracts/Admissions/IAdmissionTargetTransaction.cs
src/Explore.Application/Contracts/Identity/ILocalBootstrapReceiptSource.cs
src/Explore.Application/Contracts/Identity/ILocalIdentityEligibilityRevocation.cs
src/Explore.Application/Contracts/Identity/ILocalIdentityExecutionScopeFactory.cs
src/Explore.Application/Contracts/Identity/IManagedTenantLocalEligibilitySource.cs
src/Explore.Application/Contracts/Persistence/IAtprotoFederationGlobalReadRepository.cs
src/Explore.Application/Contracts/Persistence/IAtprotoFederationSettingRepository.cs
src/Explore.Application/Contracts/Persistence/IAtprotoFederationSettingsMutationStore.cs
src/Explore.Application/Contracts/Persistence/IAtprotoFederationSettingsSource.cs
src/Explore.Application/Contracts/Persistence/IAtprotoFederationSettingsWriter.cs
src/Explore.Application/Contracts/Persistence/IHumanTenantCreationPort.cs
src/Explore.Application/Contracts/Persistence/IIdentitySynchronizationBootstrapReader.cs
src/Explore.Application/Contracts/Persistence/IManagedTenantProvisioningSchedulingPort.cs
src/Explore.Application/Contracts/Persistence/IManagedTenantProvisioningSchedulingSource.cs
src/Explore.Application/Contracts/Persistence/IManagedTenantWorkerCreationPort.cs
src/Explore.Application/Features/Management/ManagedTenantProvisioningSchedulingOperation.cs
src/Explore.Application/Features/Tenants/HumanTenantCreationPrincipalProjection.cs
src/Explore.Application/Operations/LocalIdentityOperationScope.cs
src/Explore.Application/Settings/AtprotoFederationSettingFacts.cs
src/Explore.Domain/LocalIdentityEligibility.cs
src/Explore.Domain/ManagedTenantProvisioningPolicySetting.cs
src/Explore.Domain/Settings/ManagedTenantProvisioningPolicyInventory.cs
src/Explore.Infrastructure/Services/HumanTenantCreationPort.cs
src/Explore.Infrastructure/Services/ManagedTenantWorkerCreationPort.cs
src/Explore.Persistence.Migrations.MySql/Migrations/20261021034405_ApplicationOwnedProgrammeModel.Designer.cs
src/Explore.Persistence.Migrations.MySql/Migrations/20261021034405_ApplicationOwnedProgrammeModel.cs
src/Explore.Persistence.Migrations.MySql/Migrations/Identity/20261021034407_ApplicationOwnedLocalIdentityEligibility.Designer.cs
src/Explore.Persistence.Migrations.MySql/Migrations/Identity/20261021034407_ApplicationOwnedLocalIdentityEligibility.cs
src/Explore.Persistence.Migrations.SqlServer/Migrations/20261021034353_ApplicationOwnedProgrammeModel.Designer.cs
src/Explore.Persistence.Migrations.SqlServer/Migrations/20261021034353_ApplicationOwnedProgrammeModel.cs
src/Explore.Persistence.Migrations.SqlServer/Migrations/Identity/20261021034355_ApplicationOwnedLocalIdentityEligibility.Designer.cs
src/Explore.Persistence.Migrations.SqlServer/Migrations/Identity/20261021034355_ApplicationOwnedLocalIdentityEligibility.cs
src/Explore.Persistence.Migrations.Sqlite/Migrations/20261021034341_ApplicationOwnedProgrammeModel.Designer.cs
src/Explore.Persistence.Migrations.Sqlite/Migrations/20261021034341_ApplicationOwnedProgrammeModel.cs
src/Explore.Persistence.Migrations.Sqlite/Migrations/Identity/20261021034343_ApplicationOwnedLocalIdentityEligibility.Designer.cs
src/Explore.Persistence.Migrations.Sqlite/Migrations/Identity/20261021034343_ApplicationOwnedLocalIdentityEligibility.cs
src/Explore.Persistence/Configurations/Entities/AtprotoFederationInstanceSettingConfiguration.cs
src/Explore.Persistence/Configurations/Entities/AtprotoFederationTenantSettingConfiguration.cs
src/Explore.Persistence/Configurations/Entities/ManagedTenantProvisioningPolicySettingConfiguration.cs
src/Explore.Persistence/Database/ManagedTenantWorkerCreationOwnerFactory.cs
src/Explore.Persistence/Database/PostgresRuntimeIdentityOptionsExtension.cs
src/Explore.Persistence/Database/ProviderPrimitives/AdmissionTargetTransaction.cs
src/Explore.Persistence/Database/ProviderPrimitives/LocalIdentityExecutionContextCustody.cs
src/Explore.Persistence/Database/ProviderPrimitives/LocalIdentityLifecycleGateCustody.cs
src/Explore.Persistence/Database/ProviderPrimitives/ManagedTenantWorkerCreationAdmission.cs
src/Explore.Persistence/ExploreDbContext.DiscoveryEnrollment.cs
src/Explore.Persistence/ExploreDbContext.ManagedWorkerCreation.cs
src/Explore.Persistence/ExploreDbContext.ProvisioningPolicy.cs
src/Explore.Persistence/Identity/Configurations/LocalIdentityEligibilityConfiguration.cs
src/Explore.Persistence/Identity/ConfiguredLocalBootstrapReceiptSource.cs
src/Explore.Persistence/Identity/ConfiguredLocalIdentityEligibilityRevocation.cs
src/Explore.Persistence/Identity/ConfiguredManagedTenantLocalEligibilitySource.cs
src/Explore.Persistence/Identity/LocalIdentityCredentialStateStore.Authentication.cs
src/Explore.Persistence/Identity/LocalIdentityCredentialStateStore.Eligibility.cs
src/Explore.Persistence/Identity/LocalIdentityEligibilityRevocation.cs
src/Explore.Persistence/Identity/LocalIdentityExecutionLease.cs
src/Explore.Persistence/Identity/LocalIdentityExecutionScopeFactory.cs
src/Explore.Persistence/Identity/LocalIdentityLifecycleExecutionCustody.cs
src/Explore.Persistence/Identity/LocalIdentityWriterDbContextFactory.cs
src/Explore.Persistence/Identity/LocalIdentityWriterInventory.cs
src/Explore.Persistence/Identity/ManagedTenantLocalEligibilitySource.cs
src/Explore.Persistence/Identity/Migrations/20261021034331_ApplicationOwnedLocalIdentityEligibility.Designer.cs
src/Explore.Persistence/Identity/Migrations/20261021034331_ApplicationOwnedLocalIdentityEligibility.cs
src/Explore.Persistence/Migrations/20261021034329_ApplicationOwnedProgrammeModel.Designer.cs
src/Explore.Persistence/Migrations/20261021034329_ApplicationOwnedProgrammeModel.cs
src/Explore.Persistence/Repositories/AtprotoFederationGlobalReadRepository.cs
src/Explore.Persistence/Repositories/AtprotoFederationSettingRepository.cs
src/Explore.Persistence/Repositories/IdentityWriterExternalLoginAuthenticationRepository.cs
src/Explore.Persistence/Schema/ProviderPrimitives/AtprotoFederationReadAdmission.cs
src/Explore.Persistence/Schema/ProviderPrimitives/PostgresAtprotoFederationSettingPermissions.cs
src/Explore.Persistence/Schema/ProviderPrimitives/PostgresDiscoveryMetadataPermissions.cs
src/Explore.Persistence/Schema/ProviderPrimitives/PostgresEventResourceAuditReadPermissions.cs
src/Explore.Persistence/Schema/ProviderPrimitives/PostgresIntegrationSyncReadPermissions.cs
src/Explore.Persistence/Schema/ProviderPrimitives/PostgresInventoryHoldExpiryReadPermissions.cs
src/Explore.Persistence/Schema/ProviderPrimitives/PostgresLocalIdentitySystemSettingPolicies.cs
src/Explore.Persistence/Schema/ProviderPrimitives/PostgresLocalIdentityWriterPermissions.cs
src/Explore.Persistence/Schema/ProviderPrimitives/PostgresManagedTenantWorkerCreationPermissions.cs
src/Explore.Persistence/Schema/ProviderPrimitives/PostgresProvisioningPolicyPermissions.cs
src/Explore.Persistence/Schema/ProviderPrimitives/PostgresRegistrationRetentionReadPermissions.cs
src/Explore.Persistence/Schema/ProviderPrimitives/PostgresTenantCreationPermissions.cs
src/Explore.Persistence/Schema/ProviderPrimitives/PostgresTenantMembershipMutationPermissions.cs
src/Explore.Persistence/Schema/ProviderPrimitives/PostgresTenantPreferencePermissions.cs
src/Explore.Persistence/Schema/ProviderPrimitives/PostgresUserTenantMembershipReadPermissions.cs
src/Explore.Persistence/Security/AtprotoFederationSettingImage.cs
src/Explore.Persistence/Security/AtprotoFederationSettingsMutationStore.Source.cs
src/Explore.Persistence/Security/AtprotoFederationSettingsMutationStore.Writes.cs
src/Explore.Persistence/Security/AtprotoFederationSettingsMutationStore.cs
src/Explore.Persistence/Security/HumanTenantCreationGraphWriter.cs
src/Explore.Persistence/Security/IdentityWriterHumanSourceQueries.cs
src/Explore.Persistence/Security/ManagedTenantProvisioningSchedulingSource.cs
src/Explore.Persistence/Security/ManagedTenantWorkerCreationGraphWriter.cs
src/Explore.Persistence/Security/ManagedTenantWorkerCreationInventory.cs
src/Explore.Persistence/Security/ManagedTenantWorkerCreationSources.cs
src/Explore.Persistence/Services/AtprotoFederationSettingsWriter.cs
src/Explore.Persistence/Services/IdentitySynchronizationBootstrapReader.cs
src/Explore.Persistence/Services/ManagedSettingsDiscoveryDisclosure.cs
src/Explore.Persistence/Services/ManagedTenantProvisioningPolicyProjection.cs
src/Explore.Persistence/Services/ManagedTenantProvisioningSchedulingPort.cs
src/Explore.Persistence/Services/SettingsDiscoveryDisclosureObservation.cs
tests/Event.API.IntegrationTests/Authentication/HumanSettingsMutationPreparationTests.DiscoveryCoordination.cs
tests/Event.API.IntegrationTests/Authentication/HumanSettingsMutationPreparationTests.ResourceProviderCoordination.cs
tests/Event.API.IntegrationTests/Authentication/LocalSigningAuthorityConfigurationTests.cs
tests/Event.API.IntegrationTests/Features/ApiStartupOwnerSelectionTests.cs
tests/Event.API.IntegrationTests/Features/LocalIdentityLifecycleOwnerSelectionTests.cs
tests/Event.API.IntegrationTests/Features/TenantDiscoveryCreationNativeTests.SecurityGate.cs
tests/Event.API.IntegrationTests/Features/TenantDiscoveryCreationNativeTests.cs
tests/Event.Application.UnitTests/Operations/InstanceOnboardingOwnerSelectionTests.cs
tests/Event.Application.UnitTests/Operations/LocalIdentityOwnerSelectionTests.cs
tests/Event.Application.UnitTests/Settings/AtprotoFederationSettingFactsTests.cs
tests/Event.Application.UnitTests/Settings/AtprotoFederationSettingsCommandRoutingTests.cs
tests/Event.Application.UnitTests/Settings/AtprotoPdsRecoveryPolicyResolverTests.cs
tests/Event.Application.UnitTests/Tenants/HumanTenantCreationPrincipalProjectionTests.cs
tests/Event.Domain.UnitTests/Entities/IntegrationSyncEvidenceTests.cs
tests/Event.Domain.UnitTests/Identity/LocalIdentityEligibilityLockoutTests.cs
tests/Event.Domain.UnitTests/Identity/LocalIdentityEligibilityTests.cs
tests/Event.Domain.UnitTests/Settings/ManagedTenantProvisioningPolicyProjectionTests.cs
tests/Event.Persistence.IntegrationTests/ConfigurationManifest/ConfigurationManifestAtomicPersistenceTests.Revisions.cs
tests/Event.Persistence.IntegrationTests/ConfigurationManifest/ConfigurationManifestBootstrapOriginTests.FederationPreservation.cs
tests/Event.Persistence.IntegrationTests/ConfigurationManifest/ConfigurationManifestBootstrapOriginTests.Revisions.cs
tests/Event.Persistence.IntegrationTests/Federation/AtprotoFederationSettingsMutationTests.Batch.cs
tests/Event.Persistence.IntegrationTests/Federation/AtprotoFederationSettingsMutationTests.Concurrency.cs
tests/Event.Persistence.IntegrationTests/Federation/AtprotoFederationSettingsMutationTests.cs
tests/Event.Persistence.IntegrationTests/Federation/AtprotoFederationSettingsPersistenceTests.cs
tests/Event.Persistence.IntegrationTests/Federation/AtprotoFederationSettingsReadTests.cs
tests/Event.Persistence.IntegrationTests/Federation/DiscoveryMetadataAuthorityTests.Enrollment.cs
tests/Event.Persistence.IntegrationTests/Federation/DiscoveryMetadataAuthorityTests.Migrator.cs
tests/Event.Persistence.IntegrationTests/Federation/DiscoveryMetadataAuthorityTests.cs
tests/Event.Persistence.IntegrationTests/Federation/DiscoveryMetadataFreshEnrollmentTests.cs
tests/Event.Persistence.IntegrationTests/Federation/EventDiscoveryEnrollmentProducerTests.Canonical.cs
tests/Event.Persistence.IntegrationTests/Federation/EventDiscoveryEnrollmentProducerTests.Deletion.cs
tests/Event.Persistence.IntegrationTests/Federation/EventDiscoveryEnrollmentProducerTests.Fanout.cs
tests/Event.Persistence.IntegrationTests/Federation/EventDiscoveryEnrollmentProducerTests.LocalWriterCleanup.cs
tests/Event.Persistence.IntegrationTests/Federation/EventDiscoveryEnrollmentProducerTests.LocalWriters.cs
tests/Event.Persistence.IntegrationTests/Federation/EventDiscoveryEnrollmentProducerTests.OrganizationPii.cs
tests/Event.Persistence.IntegrationTests/Federation/EventDiscoveryEnrollmentProducerTests.WorkerSources.cs
tests/Event.Persistence.IntegrationTests/Federation/EventDiscoveryEnrollmentProducerTests.cs
tests/Event.Persistence.IntegrationTests/Fixtures/AuthenticatedSettingsFixture.Email.cs
tests/Event.Persistence.IntegrationTests/Fixtures/AuthenticatedSettingsFixture.ManagedWorker.cs
tests/Event.Persistence.IntegrationTests/Fixtures/AuthenticatedSettingsFixture.TenantCreation.cs
tests/Event.Persistence.IntegrationTests/Fixtures/DiscoveryMetadataEnrollmentPostgresFixture.cs
tests/Event.Persistence.IntegrationTests/HumanAdministratorBulkProducerGateTests.cs
tests/Event.Persistence.IntegrationTests/HumanSecuritySourceMutationGuardTests.cs
tests/Event.Persistence.IntegrationTests/Identity/ConfiguredLocalIdentityEligibilityRevocationTests.cs
tests/Event.Persistence.IntegrationTests/Identity/ConfiguredManagedTenantLocalEligibilitySourceTests.cs
tests/Event.Persistence.IntegrationTests/Identity/ExternalIdentityModelScaffoldingTests.cs
tests/Event.Persistence.IntegrationTests/Identity/LocalBootstrapConvergenceTests.ReceiptSource.cs
tests/Event.Persistence.IntegrationTests/Identity/LocalIdentityEligibilityLifecycleTests.cs
tests/Event.Persistence.IntegrationTests/Identity/LocalIdentityExecutionLeaseTests.cs
tests/Event.Persistence.IntegrationTests/Identity/LocalIdentityExecutionScopeFactoryTests.cs
tests/Event.Persistence.IntegrationTests/Identity/LocalIdentitySelectedLifecycleTests.cs
tests/Event.Persistence.IntegrationTests/Identity/LocalIdentitySigningAuthorityNativeTests.cs
tests/Event.Persistence.IntegrationTests/Identity/LocalIdentityWriterDbContextFactoryTests.cs
tests/Event.Persistence.IntegrationTests/Identity/LocalIdentityWriterPermissionsNativeTests.Authentication.cs
tests/Event.Persistence.IntegrationTests/Identity/LocalIdentityWriterPermissionsNativeTests.Migrations.cs
tests/Event.Persistence.IntegrationTests/Identity/LocalIdentityWriterPermissionsNativeTests.cs
tests/Event.Persistence.IntegrationTests/IdentityWriterExternalLoginAuthenticationTests.cs
tests/Event.Persistence.IntegrationTests/IdentityWriterHumanSourceOwnerTests.cs
tests/Event.Persistence.IntegrationTests/IdentityWriterHumanSourceQueryTests.cs
tests/Event.Persistence.IntegrationTests/IdentityWriterSourceRegistrationTests.cs
tests/Event.Persistence.IntegrationTests/ManagedTenantWorkerCreationNativeTests.SchedulingGate.cs
tests/Event.Persistence.IntegrationTests/ManagedTenantWorkerCreationNativeTests.SecurityGate.cs
tests/Event.Persistence.IntegrationTests/ManagedTenantWorkerCreationNativeTests.Settlement.cs
tests/Event.Persistence.IntegrationTests/ManagedTenantWorkerCreationNativeTests.Workflow.cs
tests/Event.Persistence.IntegrationTests/ManagedTenantWorkerCreationNativeTests.cs
tests/Event.Persistence.IntegrationTests/OrdinarySettingsNativeContractTests.Reads.cs
tests/Event.Persistence.IntegrationTests/Repositories/HumanEmailSecurityGateTests.cs
tests/Event.Persistence.IntegrationTests/Repositories/InternalEmailDeliverySecurityGateTests.cs
tests/Event.Persistence.IntegrationTests/Repositories/ManagedTenantProvisioningPolicyProjectionTests.cs
tests/Event.Persistence.IntegrationTests/Repositories/MutationScopeHumanGateTests.cs
tests/Event.Persistence.IntegrationTests/Repositories/PolicyMutationExecutionScopeTests.HumanGate.cs
tests/Event.Persistence.IntegrationTests/Seed/DatabaseSeederSecurityGateTests.cs
tests/Event.Persistence.IntegrationTests/SetupLive/SetupLiveRuntimeStorageTests.cs
tests/Event.Persistence.IntegrationTests/SupportAdministrationNativeContractTests.MembershipPermissions.cs
tests/Event.Persistence.IntegrationTests/TenantIsolation/DocumentKeyAdmissionNativeTests.cs
tests/Event.Persistence.IntegrationTests/TenantIsolation/EventResourceAuditOwnerEfNativeTests.cs
tests/Event.Persistence.IntegrationTests/TenantIsolation/InstanceIssuanceTenantKeyStorageCoexistenceTests.cs
tests/Event.Persistence.IntegrationTests/TenantIsolation/ManagedTenantWorkerCreationPermissionsNativeTests.Migrations.cs
tests/Event.Persistence.IntegrationTests/TenantIsolation/ManagedTenantWorkerCreationPermissionsNativeTests.cs
tests/Event.Persistence.IntegrationTests/TenantIsolation/PostgresIntegrationSyncDiscoveryEfTests.cs
tests/Event.Persistence.IntegrationTests/TenantIsolation/PostgresInventoryHoldExpiryReadPermissionTests.cs
tests/Event.Persistence.IntegrationTests/TenantIsolation/ProvisioningPolicyPermissionsNativeTests.cs
tests/Event.Persistence.IntegrationTests/TenantIsolation/TenantCreationCatalogueCompositionTests.cs
tests/Event.Persistence.IntegrationTests/TenantIsolation/TenantCreationExternalPermissionsNativeTests.cs
tests/Event.Persistence.IntegrationTests/TenantIsolation/TenantCreationLookupCatalogueTests.cs
tests/Event.Persistence.IntegrationTests/TenantIsolation/TenantCreationPermissionsNativeTests.Checks.cs
tests/Event.Persistence.IntegrationTests/TenantIsolation/TenantCreationPermissionsNativeTests.Drift.cs
tests/Event.Persistence.IntegrationTests/TenantIsolation/TenantCreationPermissionsNativeTests.MembershipBaseline.cs
tests/Event.Persistence.IntegrationTests/TenantIsolation/TenantCreationPermissionsNativeTests.cs
tests/Event.Persistence.IntegrationTests/TenantIsolation/TenantMembershipMutationPermissionsNativeTests.cs
tests/Event.Persistence.IntegrationTests/TenantIsolation/TenantPreferenceEfTests.cs
tests/Event.Persistence.IntegrationTests/TenantIsolation/UserTenantMembershipReadEnrollmentTests.cs
```
