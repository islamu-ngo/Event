<!-- ABOUTME: Domain journal for testing harnesses, TUnit execution, Testcontainers, and SDK environments. -->
<!-- ABOUTME: Captures durable findings on test runners, rate-limit bypassing, and containerized integration testing. -->

# Testing & Environment Knowledge Ledger

> **Scope**: TUnit test runner, `Event.*Tests`, `Explore.Diagnostic`, Testcontainers, Podman/Docker, and CI environment.

---

## 1. Architectural Decisions

- **Forward-Only Ratchets**: `ApiLiabilityRatchetTests` enforces an exact allowlist for legacy debt. Introducing a new occurrence fails, and fixing an issue without removing its ratchet entry also fails. Never relax ratchets.
- **Testing Environment Rate Limiting**: ASP.NET Core rate limiting is completely disabled in the `Testing` environment (`NoLimiter` policy) because `WebApplicationFactory` test clients lack loopback IP resolution.
- **Targeted Project Test Execution**: Tests must always be run at the project level (`dotnet test --project <path>.csproj`), never at the whole-solution level.

---

## 2. Technical Insights & Patterns

- **TUnit Runner Filtering Quirks**: TUnit / Microsoft Testing Platform does NOT accept standard VSTest `--filter` syntax. Use full project execution (`dotnet test --project ...`) or TUnit's native tree-node filter syntax (`--treenode-filter "/*/*/*TestClassName/*"`).
- **All-Skipped Visual Tests Exit Code**: When every test matched by a filter is marked with a manual skip reason, TUnit reports `Zero tests ran` and exits with code `8`. Treat this as a skip status, not a build failure.
- **SDK Workload Corruption vs Code Regression**: If `dotnet build` fails before compilation with `MSB4242` ("missing manifests likely removed by package management"), the root cause is corrupted host SDK workloads, not repository code. Run `dotnet workload repair`.
- **Podman Testcontainers Endpoint Configuration**: Running Testcontainers under Podman requires exporting:
  ```bash
  export DOCKER_HOST=unix:///run/user/$(id -u)/podman/podman.sock
  export TESTCONTAINERS_RYUK_DISABLED=true
  export TESTCONTAINERS_DOCKER_SOCKET_OVERRIDE=/run/user/$(id -u)/podman/podman.sock
  ```
  Without these variables, suites fail with `DockerUnavailableException`.

---

## 3. Failed Approaches & Lessons

- **Overnight Solution-Level Test Runs in Clean-Room Context**: Running `dotnet test` solution-wide causes massive context output and memory consumption. Always isolate test execution to the single relevant test project.
- **PowerShell Syntax in Bash Shell**: Shell verification commands must use strictly POSIX Bash syntax. Never use PowerShell constructs or aliases.

[2026-09-06 Europe/Brussels] — Compiler servers can inherit and retain build locks

**Context**: Serializing shared-output .NET builds during the approved Unicode workstream's Architecture/Persistence regression repairs.

**Symptom / Observation**: A finished build left later builds waiting indefinitely. Native `lslocks -o COMMAND,PID,MODE,BLOCKER,PATH` showed `VBCSCompiler`, not the waiting test processes, holding the task's build lock.

**Root Cause**: Plain `flock <lock-file> <command>` allowed the lock file descriptor to reach the compiler server started by the build. That daemon outlived the command and retained the lock. A waiter PID and elapsed time alone did not identify the blocker.

**Resolution**: Validate the exact lock owner and command before stopping an orphaned task-created process. Use `flock --close <lock-file> <command>` for subsequent serialized builds; native `flock --help` confirms that this closes the descriptor before running the command. Corrected focused Architecture executions passed6/6 and7/7, and lock inspection showed active flock owners instead of retained compiler-server ownership. No global build-server shutdown or repository helper script was needed.

**Why This Matters for Future Work**: Build serialization can itself create a false test hang when child daemons inherit file descriptors. Check the real lock owner before restarting tests, and prevent inheritance rather than adding timeouts or killing unrelated processes.

**References**:

- `.agents/CONTEXT_ENGINEERING.md#in-session-test-economy--clean-architecture-scoping`
- `tests/Event.Architecture.Tests/Event.Architecture.Tests.csproj`
- Native `flock --help` and `lslocks --help`
- Verified Architecture repair: `8e252c831465555fa6492071b82ef9e086348991`.

**Promotion Consideration**:

- [ ] Candidate for `docs/internal/QUICK_REFERENCE.md`.
- [ ] Candidate for new `.agents/rules/*.md` entry.
- [ ] Candidate for skill update.
- [ ] Candidate for ADR.
- [x] Stays in journal only (observed build-orchestration lesson).

---

[2026-09-06 Europe/Brussels] — Global signal callbacks can retain disposed test hosts

**Context**: Full API acceptance exhausted memory while constructing many hosts. A bounded investigation separated host disposal from process-wide callback ownership before another complete run.

**Symptom / Observation**: Native API and BFF host controls released caller-owned shutdown state after disposal. Registering the actual startup/shutdown callbacks kept that state reachable after both normal stop and disposal without starting. Each four-case Red had two passing controls and two failing registered cases.

**Root Cause**: `Console.CancelKeyPress` and BFF `ProcessExit` are process-global event roots. Disposing a WebApplication does not remove application-owned static event subscriptions. Their closures retained shutdown state and, in the BFF, the application. Additionally, application roots without lexical disposal could throw during initialization before reaching the disposal performed by `Run`.

**Resolution**: `HostProcessSignalSubscriptions`, created and disposed by the host DI container through `AddServiceDefaults`, records and removes the exact delegates. All three application roots use `await using` for their application. Existing signal callbacks remain unchanged. The native filtered commands `dotnet run --project tests/Event.API.IntegrationTests --configuration Release -- --treenode-filter "/*/*/*ApiHostLifetimeTests/*"` and the corresponding BFF project/`GracefulShutdownLifetimeTests` filter each passed four cases. This establishes released references, not total memory stability or the sole cause of the full-suite failure.

**Why This Matters for Future Work**: An `ApplicationStopped` callback alone is not a disposal owner for a host that never starts. Use a container-created disposable, not an externally constructed singleton instance, when relying on DI disposal. Weak-reference controls can isolate this defect without repeatedly driving the entire suite to OOM. An application log saying “SIGTERM received” is also not proof of an OS signal: the existing handler emits it for any `ApplicationStopping` notification; process termination attribution needs OS/process evidence.

**References**:

- `src/Explore.ServiceDefaults/HostProcessSignalSubscriptions.cs`
- `src/Explore.API/Hosting/ApiHostStartupExtensions.cs`
- `src/Explore.Blazor/Extensions/MiddlewareExtensions.cs`
- `tests/Event.API.IntegrationTests/Hosting/ApiHostLifetimeTests.cs`
- `tests/Explore.Blazor.IntegrationTests/Hosting/GracefulShutdownLifetimeTests.cs`
- `docs/internal/TESTING.md`

**Promotion Consideration**:

- [x] Stays in journal as diagnostic evidence; the host-fixture contract is recorded in TESTING.md.

---

[2026-10-03 Europe/Brussels] — Validate class-segment OR before bounded TUnit acceptance

**Context**: Exhaustive API and Persistence acceptance required separate processes to bound accumulated integration-host memory without omitting cases.

**Symptom / Observation**: Joining complete tree paths with `|` executed unrelated classes rather than the intended two-class slice. Native managed-stack inspection identified unrelated test executors. A leading class wildcard also selected both `PrimaryDatabaseProviderBehaviorContractTests` and `ContainerizedPrimaryDatabaseProviderBehaviorContractTests`.

**Root Cause**: TUnit tree filters express alternatives within a path segment, not as independently joined complete paths. A wildcard before a class name deliberately permits additional prefixes. Successful execution alone cannot prove the requested class set.

**Resolution**: Use exact, parenthesized class alternatives in one segment: `--treenode-filter '/*/*/(EmailDeliveryGraphRevisionTests)|(EventResourceStorageCleanupTests)/*'`. Before executing a new cohort, run the native project executable with `--list-tests json`, inspect `.tests[].type.typeName`, and verify the exact selected classes and case count. The corrected two-class inventory selected exactly 27 cases; separate exact-class executions passed 15/15 and 12/12. Validate exhaustive cohorts as a non-overlapping partition and give each run a unique TRX filename. `dotnet test` wrapper output that says zero tests ran while listing is not execution evidence.

**Why This Matters for Future Work**: A malformed slice can silently become a broad run, producing misleading provider coverage and defeating the memory bound. Inventory validation establishes selection; exit status and unique TRX counters establish execution. Neither substitutes for the other.

**References**:

- `docs/internal/OPERATIONS.md#verification-policy`
- `tests/Event.Persistence.IntegrationTests/Repositories/EmailDeliveryGraphRevisionTests.cs`
- `tests/Event.Persistence.IntegrationTests/EventResourceStorageCleanupTests.cs`
- https://tunit.dev/docs/execution/test-filters/

**Promotion Consideration**:

- [x] Operator guidance is recorded in `docs/internal/OPERATIONS.md`; this entry retains the observed diagnostic evidence.

---

[2026-10-23 Europe/Brussels] — Test navigation rejection through configured EF access

**Context**: Testing assurance modernization replaced private Location probes with public Domain transitions and configured SQLite materialization/tracking witnesses.

**Symptom / Observation**: Assigning another Location's PII through a tracked EF navigation threw, but the target navigation had already changed. An exception alone therefore did not prove rejection without mutation.

**Root Cause**: The configured navigation allowed EF to write the backing field before relationship fixup reached the Domain setter's ownership validation. A direct setter test bypassed that configured access path.

**Resolution**: `LocationConfiguration` selects `PropertyAccessMode.Property` for `Pii`, preserving `AutoInclude`. The existing setter rejects foreign, erased or ownerless attachments before changing either aggregate. Configured materialization, erasure, rightful attachment and scalar storage constraints remain covered. The owner executed these selections with `R` set to the task's NVMe `continuation/location` evidence directory:

```bash
dotnet run --project tests/Event.Persistence.IntegrationTests/Event.Persistence.IntegrationTests.csproj -c Release --no-build -- --treenode-filter "/*/*/LocationMaterializationInvariantTests/*" --minimum-expected-tests 15 --maximum-parallel-tests 1 --report-trx --results-directory "$R/sqlite-quiet-window"
dotnet run --project tests/Event.Domain.UnitTests/Event.Domain.UnitTests.csproj -c Release --no-build -- --treenode-filter "/*/*/Location*InvariantTests/*" --minimum-expected-tests 14 --report-trx --results-directory "$R/domain-final"
```

The configured SQLite selection passed 15/15 and retained Domain selection 14/14, with zero skips. The actual SQLite pending-model check reported no changes. This does not certify other database engines.

**Why This Matters for Future Work**: For an ORM mutation boundary, assert both the rejection and unchanged tracked aggregate state through the actual configured navigation. A private setter or an expected exception can miss mutation that happened before the exception; selecting property access keeps validation ahead of that mutation.

**References**:

- `src/Explore.Persistence/Configurations/Entities/LocationConfiguration.cs`
- `tests/Event.Persistence.IntegrationTests/LocationMaterializationInvariantTests.cs`
- `tests/Event.Domain.UnitTests/LocationCoordinateInvariantTests.cs`
- Commits `a3d6665899` and `43cc5b7279`.

**Promotion Consideration**:

- [x] Stays in the testing journal as observed configured-ORM evidence; existing Domain ownership invariants remain authoritative.

---

[2026-10-23 Europe/Brussels] — Await TUnit assertions outside synchronous bUnit waits

**Context**: A rendered Studio resource export test used TUnit assertions inside bUnit's synchronous `WaitForAssertion` callback.

**Symptom / Observation**: Deliberately expecting the wrong export title still passed before the correction. After awaiting the interaction and assertion, the same wrong-title control failed its exact export method; restoring the rightful title passed all 24 class cases with zero skips in 8.006 seconds.

**Root Cause**: The synchronous callback discarded the asynchronous TUnit assertion rather than awaiting its failure. Waiting for the callback to return was not proof that the assertion completed. Synchronous click calls also did not express completion of the asynchronous resource action.

**Resolution**: The three affected publish/export tests await `ClickAsync` and then await their TUnit rendered-outcome assertions directly. Existing HAL affordance, version, denied-action and confidentiality assertions remain. The final Client Release build passed with zero errors; the recorded before/after sensitivity and restored-class logs are under the task's NVMe `continuation/ui-bff` evidence directory. The integrated correction is `f8ec28c09f`; no production UI behavior changed.

**Why This Matters for Future Work**: A deliberately incorrect expectation is a useful check that an asynchronous assertion can actually fail its test. Every assertion task must reach an awaited test path; a synchronous polling helper cannot observe a discarded asynchronous failure. Await the meaningful UI event before checking its rendered result.

**References**:

- `tests/Explore.Blazor.Client.Tests/Pages/Studio/StudioEventResourcesTests.cs`
- Methods `StalePublishDenialRefreshesAndRemovesTheAction`, `ExportRequiresCollectionLinkAndRendersAuthorizedMetadata` and `ChangedVersionDeniesPublishWithoutSubmittingAndRefreshes`.
- `.agents/skills/blazor-ui-conventions/SKILL.md`
- Commit `f8ec28c09f`.

**Promotion Consideration**:

- [x] Stays in the testing journal as an observed false-green mechanism; the existing UI workflow already requires awaiting the actual event.

---

[2026-10-23 Europe/Brussels] — Keep finite process-child module dispatch synchronous

**Context**: The owned k6 runner harness launches its own executable in finite child modes to verify process cancellation, output limits, version refusal and descendant cleanup without starting an application or real load.

**Symptom / Observation**: The original selection passed 30 pure cases but timed out all eight real process cases. A direct four-second POSIX timeout of the version child returned 124 after writing its owned marker and producing no stdout. With only finite child dispatch made synchronous, the same child returned 0 in 66 milliseconds with the expected version output and marker.

**Root Cause**: The test executable dispatched asynchronous child work during module initialization and synchronously waited for it. The actual child stalled after its asynchronous marker write. This was a child harness execution defect, not evidence that k6 or the application exceeded a latency budget.

**Resolution**: `K6LoadTests` uses synchronous file/process work only for its finite module-initializer child dispatch. The parent `K6LoadRunner.RunOwnedProcessAsync` remains asynchronous, with its original deadlines, cancellation, process-tree termination and output draining. The exact closed commit subset rebuilt with zero warnings/errors and executed:

```bash
dotnet run --project tests/Event.Distributed.IntegrationTests/Event.Distributed.IntegrationTests.csproj --configuration Release --no-build -- --treenode-filter "/*/*/K6Load*HarnessTests/*" --minimum-expected-tests 38 --progress off --maximum-parallel-tests 1
```

All 38 cases, including the eight real process controls, passed with zero skips in 5.211 seconds. The integrated checksum/finite-child commit is `31e897b75e`. Actual k6 workloads, application recovery and capacity measurements remain separate unexecuted profiles at this checkpoint.

**Why This Matters for Future Work**: When every process case times out but pure parsing cases pass, first run the finite child directly with a bounded timeout and an owned safe marker. That separates a broken child entry path from the parent's cancellation implementation without increasing deadlines or weakening cleanup.

**References**:

- `tests/Event.Distributed.IntegrationTests/K6LoadTests.cs`, `DispatchChild` and `RunChild`.
- `tests/Event.Distributed.IntegrationTests/K6LoadRunner.cs`, `RunOwnedProcessAsync`.
- `docs/internal/testing/LOAD_ASSURANCE.md`
- Commit `31e897b75e`; task scratch logs `load-child-before-sync.log`, `load-child-after-sync.log` and `load-harness-38-sync.log`.

**Promotion Consideration**:

- [x] Stays in the testing journal as a demonstrated process-harness failure and differential check.

---

[2026-10-23 Europe/Brussels] — Join the actual AppHost task before deleting owned stores

**Context**: Owned AppHost cancellation tests held a real `IHostedLifecycleService.StartingAsync` callback open while requesting shutdown through the pinned Aspire 13.4.6 testing facade.

**Symptom / Observation**: Shutdown reached the held callback's cancellation token, yet facade stop completion and service disposal could finish before the callback settled. A separate faulted task carrying `OperationCanceledException` was also incorrectly accepted as cancellation by an initial observer implementation.

**Root Cause**: The fixture treated completion of the testing facade's operation as evidence that the existing AppHost execution had settled. Catching an exception by type additionally confused a faulted task with one whose actual state was canceled. Neither assumption proves that application code has stopped using its stores.

**Resolution**: The owned AppHost reports its one existing `RunAsync` task to `TestingApplicationExecution`; it does not start another host operation. The fixture awaits that exact task before removing stores, including a built host that was never explicitly started through the fixture. Missing or duplicate observations fail closed, duplicate observation permanently invalidates completion, and a held task retains its owned root. Cancellation is accepted only when the observed task is actually canceled. Independent authority/cleanup reviews bind the final source; the observer four-case selection, Harness 34 and four actual lifecycle controls passed before integration in `a913977a22`. Full topology readiness remains a separate pending gate.

**Why This Matters for Future Work**: A stop request, cancellation signal or disposed service is not a join on the task still running application code. Cleanup must observe that specific task's completion and state. Retaining proven-owned stores on ambiguous completion is safer than deleting files under an unsettled application.

**References**:

- `src/Explore.AppHost/AppHost.cs`
- `src/Explore.AppHost/TestingApplicationExecution.cs`
- `tests/Event.Distributed.IntegrationTests/OwnedAppHostFixture.cs`
- `tests/Event.Distributed.IntegrationTests/AppHostRuntimeTests.Lifecycle.cs`
- `docs/internal/testing/DISTRIBUTED_ASSURANCE.md`
- Commit `a913977a22`.

**Promotion Consideration**:

- [x] The owned distributed guide already records the lifecycle contract; retain this observed failure and task-state distinction in the testing journal.

---

[2026-10-23 Europe/Brussels] — Reauthentication containers are not credential identity

**Context**: Native JWT/SQLite storage-configuration tests exercised the registered authentication pipeline and captured original-request authority through `StorageConfigurationAuthority`.

**Symptom / Observation**: Two successful real `AuthenticateAsync` calls returned different ticket wrappers while retaining the native principal, scheme and lifetime receipt. The original storage source rejected the valid request with `UnauthorizedAccessException`. A separate genuine `IClaimsTransformation` decorator invoked the real `AdminClaimsTransformation` and returned a fresh `ClaimsPrincipal` container over the same native identity objects; that legitimate container also failed the initial guard. Its precondition proved distinct containers with identical identity references before source capture.

**Root Cause**: The source treated authentication-result ticket/principal container reference equality as credential continuity. These wrappers can be recreated without changing the authenticated identities. The original request principal is a separate custody boundary and must still remain the captured object. Conversely, observing only the selected persistence tenant missed mutation of the distinct request tenant marker.

**Resolution**: Preserve original request/principal/services/command custody and compare the exact native identity and claim sequences, authentication scheme, issued/expiry properties and human lifetime receipt across reauthentication. Retain the persisted principal/support authority checks. Capture request tenant-marker presence and its nonempty typed Guid separately from the selected persistence tenant. This does not permit cloned identities, rewritten claims or a different request principal. The normal/fresh-container selection first produced one pass and one failure against the old container guard, then both named cases passed with zero skips in 36.882 seconds after the correction. The separately observed HTTP writer transaction-ordering failure remains open; these cases do not certify the whole storage HTTP profile.

The physically executed passing command was:

```bash
export TMPDIR="$HOME/.cache/agent-tmp"
dotnet tests/Event.API.IntegrationTests/bin/Release/net10.0/Event.API.IntegrationTests.dll --treenode-filter "/*/*/StorageProbeOriginalActionTests/NativeStorageConfigurationAcceptsReauthenticationAndRejectsChangedOriginalSources" --minimum-expected-tests 2 --progress off --maximum-parallel-tests 1 --report-trx --report-trx-filename storage-auth-principal-green.trx --results-directory "$TMPDIR/testing-assurance-modernization/continuation/storage-auth-principal-green-results"
```

**Why This Matters for Future Work**: Native authority tests should challenge both legitimate framework reconstruction and actual source replacement. A recreated result container is different from a changed authenticated identity. Keep request custody, credential identity, lifetime and persisted authority as explicit boundaries rather than binding them all to a wrapper object's allocation.

**References**:

- `src/Explore.Infrastructure/Services/StorageConfigurationAuthority.cs`
- `tests/Event.API.IntegrationTests/Authentication/StorageProbeOriginalActionTests.AuthenticationCustody.cs`
- `docs/internal/API.md`, Validated Human Credential Lifetime.
- `docs/public/documentation/readme/integrations-and-ai/storage.md`, Storage configuration authority.
- Candidate source SHA-256 `6f6d4f1e9646e6f08031d2b48eca82b83b4bdc4b0aa7e18b05048ce95ca3bbba`; named Green TRX SHA-256 `5b863984f580fdec13fe3cc7d3ddd0a0d851ec53284c399bc377677375e9ef79`.
- Storage-only correction commit `e324167a6eb68ad987923eddd81756fef758514a`; transaction-observation correction remains separate.

**Promotion Consideration**:

- [x] Retain this measured framework/container distinction and independent request-tenant custody lesson in the testing journal; the API/storage guides own the production contract.

---
