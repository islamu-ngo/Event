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
