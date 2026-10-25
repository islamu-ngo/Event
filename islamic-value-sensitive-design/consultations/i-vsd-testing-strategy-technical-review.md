# ISLAMU Event Testing: Safety Evidence, Maintainability, and Cloud-Native Readiness

Last Updated: 2026-10-22

## Review Metadata

- Mode: standalone
- Subject: ISLAMU Event testing strategy, test quality, and verification coverage
- Workstream: none; consultation preceding any implementation plan
- Report kind: technical-review
- Report status: current
- Disposition: changes-required
- Evidence cutoff: 2026-10-22
- Reviewed input: Git commit `ac6e6a123c1ab9b32c16ab00e50e813bea29272f`, plus the user's testing requirements and explicit exclusion of NBomber
- Supersedes: none. The earlier test-suite rationalization workstream assessment remains a separate historical planning artifact.
- Validation: design reasoning and implementation traceability through source inspection; no test execution, stakeholder study, or operational certification
- Change scope: this report only. No tests, production code, workflows, dependencies, or existing reports were changed.

## Executive Summary

ISLAMU Event needs a testing strategy organized around observable failure modes and the evidence required to detect them. A high coverage percentage, a large test directory, or a successful mock verification cannot establish that an attendee's payment is safe, another tenant's information is inaccessible, an erased address stays erased after restoration, or an operator can recover from a failed delivery.

However, the current repository is substantially stronger than a description such as “all mocks and no integration tests” would suggest. It already contains real PostgreSQL refund races, row-level security tests, a five-engine provider contract matrix, privacy-erasure recovery tests, clustered Quartz recovery scenarios, Testcontainers fixtures, Respawn resets, Verify snapshots, and BenchmarkDotNet benchmarks. Deleting these because the product has not been released would remove valuable evidence about behavior that still exists. Development mode makes obsolete contracts expendable; it does not make current safety invariants obsolete.

The strongest findings are specific. Normal Build & Test CI explicitly disables the broad integration lane. AppHost-only and benchmark-only changes are excluded by its change detector. Some architecture tests assert source strings and local variable names. A sampled application test returns the exact object configured on a substitute and checks one forwarding call. A domain test invokes a private method to inspect its exception parameter name. The scheduled performance smoke uses an in-process host with EF InMemory, substituted authentication and authorization, and removed product background services. These checks have narrower assurance than their names or placement can imply.

There is also contrary evidence to the allegation that the repository currently enforces 100% coverage: the inspected Codecov statuses are informational, and the main workflow does collect coverage for selected fast suites. The problem is not a discovered 100% gate. It is the risk of confusing execution and coverage measurements with safety, combined with stale documentation and inconsistent lane selection.

The recommendation is to retain strong domain invariants and real-engine tests, replace tests coupled to internal implementation with public-behavior tests, retire tests whose sole subject is removed behavior, and close the gaps in deployed-topology, critical HTTP journeys, browser behavior, and realistic load evidence. Keep unit tests for pure logic. Use integration, component, protocol, architecture, and deployment tests for their distinct responsibilities instead of calling every fast test a unit test.

NBomber is excluded by the user's decision and is not part of the proposed stack. Keep the existing TUnit, Verify, Testcontainers, Respawn, and BenchmarkDotNet investments. Evaluate k6 for external load generation and Aspire testing for a small set of distributed-application scenarios. Neither should turn the fast feedback lane into a full distributed deployment on every edit.

“Changes required” means the reviewed testing strategy has concrete deficiencies and evidence gaps. It does not mean every suite is broken, that a runtime defect was reproduced during this review, or that a deletion/refactor has been approved or performed. The priority is stronger evidence with lower maintenance cost, without a deletion quota or a replacement coverage quota.

## Scope

This consultation examines provider responsibility for test reliability, maintainability, truthful quality claims, privacy of test artifacts, and the safety of future testing changes. The technical sample covers Domain, Application, API, Persistence, Blazor/BFF, architecture tests, benchmarks, dependency declarations, and CI routing. It also considers the earlier testing rationalization assessment and current contribution rules.

The repository's `test-suite-rationalization` intent is the relevant contract for a future implementation. Its requirement to give each removed test an invariant disposition is appropriate: retain its behavior elsewhere, replace it with stronger passing evidence, or identify the behavior that has actually been removed. This turn is a documentation-only consultation, not execution of that intent's full test matrix. Its write scope is this standalone report, as requested through I-VSD.

The user's direction is clear enough to complete the report without a separate intake interview: greenfield development, no backward-compatibility baggage, unit tests for pure logic, scrutiny of mocks and private-method tests, and stronger integration/contract/load testing. NBomber was explicitly excluded during the review. No traffic target, latency commitment, deployment hardware profile, or flake-rate budget was supplied; the report leaves those unmeasured instead of inventing them.

## Claim Boundary

This is Islamic Value Sensitive Design reasoning about a software provider's engineering responsibilities. It is not a fatwa, Sharia certification, legal opinion, security certification, or declaration that the platform is enterprise-ready.

Source inspection establishes that a test or workflow expresses a behavior; it does not establish that the test passes at the reviewed commit. A workflow definition establishes configured selection, not actual branch-protection settings, successful recent execution, artifact retention, or the validity of a production claim. Search absence is reported with its scope. No source-pattern count is treated as a count of defective tests.

“Enterprise-grade” is used here as an engineering goal requiring repeatable evidence: correct critical behavior, clear ownership, realistic operating limits, recovery, maintainable verification, and truthful documentation. Installing a collection of testing packages does not satisfy it.

## Current Inventory And What It Means

The committed inventory contains **19 test/benchmark project files** under `tests/` and `eng/**/tests/`: 13 regular projects under `tests/`, two dedicated mutation projects, one benchmark project, and three engineering-tool test projects. This is a project-file count, not a discovered or executed test count. The September checkpoint in `TESTING.md` lists 32 projects; it must not be presented as today's inventory. [E01, E02]

| Surface | Observed implementation | What this does and does not establish |
| --- | --- | --- |
| Unit/domain testing | TUnit; domain state, validation, money, privacy and other invariant tests | Valuable pure-logic evidence exists. Project names do not guarantee every contained test is a unit test. |
| Application testing | 478 visible C# files; 122 contain `Substitute.For<`; 18 contain `Received(` or `DidNotReceive(` patterns | These are search triage counts, not numbers of test methods or bad tests. A blanket “mock everything” conclusion is unsupported. |
| Persistence | PostgreSQL Testcontainers, migrations, tenant isolation, concurrency, reset infrastructure | Real relational evidence exists; it must be preserved and its provider scope stated. |
| Provider portability | Reusable CI matrix for PostgreSQL, file-backed SQLite, SQL Server, MariaDB, MySQL | The matrix invokes migration and runtime behavior checks. Its existence does not mean it runs in every caller workflow. |
| API contracts | HTTP host fixtures, authorization/metadata checks, ProblemDetails and HAL snapshots | Useful wire evidence; InMemory and substituted-authority profiles do not prove database or real identity-provider behavior. |
| Snapshot testing | Verify.TUnit; four `.verified.*` files found under the API integration project | Snapshot testing already exists. Four files are not a measure of contract completeness. |
| BFF/UI | bUnit and in-process BFF tests; separate security fixture | Fast rendering and forwarding tests are useful. Default substituted authentication does not prove real cookies across a process restart. |
| Performance | BenchmarkDotNet and scheduled/manual performance smoke; separate PostgreSQL benchmark exists | Controlled performance measurements exist. The scheduled smoke selects the InMemory benchmark, not a realistic load campaign. |
| Mutation tooling | Two standalone mutation projects and Stryker configuration | Useful domain tests exist inside projects not found in the solution or inspected workflow references. Consolidation requires preserving their invariants. |
| New candidates | No references to k6 execution, NBomber, Aspire.Hosting.Testing, DistributedApplicationTestingBuilder, PactNet, or Playwright in the searched package/test/engineering/workflow surfaces | This bounds the search. It does not establish that nobody has ever performed an external load test or manual browser review. NBomber is excluded regardless. |

Package pins at this revision include TUnit `1.63.0`, Verify.TUnit `31.28.0`, Respawn `7.0.0`, BenchmarkDotNet `0.15.8`, bUnit `2.8.6`, and Testcontainers packages `4.13.0`. These describe the repository, not a recommendation to upgrade or a claim that they are the latest releases. [E02]

## Findings

### IVSD-F001 — Preserve the tests that already protect money, tenant boundaries, and recovery

The highest-risk mistake in a cleanup would be removing tests that exercise failures a user cannot repair themselves. An organizer cannot correct a double refund inside the database; a person whose home address reappears after restoration cannot fix the erasure replay protocol. The provider controls both the implementation and the evidence used to accept changes.

`TwoRacingReservationsCannotExceedCapturedCapacity` starts competing PostgreSQL reservations, checks that only one is accepted, and checks the persisted reserved amount. The privacy recovery suite includes `PrimaryOnlyRestore_RetainsEmbeddedAuthorityAndReplayConvergesExactlyOnce`. The email cluster recovery test checks that transport acceptance followed by failed local settlement leads to durable `Unknown`, avoids an automatic resend, and keeps sensitive canaries out of logs and metrics. These are meaningful behavioral assertions. [E09, E10, E11]

The Quartz payment restart test has a narrower but useful role: a real persisted trigger reaches a repository callback after the scheduler host is recreated. Because that repository is substituted, it does not by itself prove financial reconciliation after a crash. Preserve this distinction instead of dismissing the test or inflating its claim. [E11]

- Lifecycle / severity / claim type: open / high / risk introduced by indiscriminate deletion; positive existing assurance anchors
- Principles / domains: Amanah, Non-Harm, Rights of People / technical, operational, evaluation
- Stakeholders / controlled decision: attendees, organizers, data subjects, operators / which safety evidence survives rationalization
- Evidence / validation: E09–E12 / implementation traceability only
- Mitigation: **IVSD-M001**, preserve invariant coverage before deleting or moving a cohort
- Owner / next validation: domain and persistence maintainers; run retained/replacement tests in their real provider lanes before removal
- Escalation: any deletion that removes the sole evidence for a current critical invariant needs a resolved replacement or an explicit decision removing the product behavior

### IVSD-F002 — Source-string tests and private implementation tests create fragile confidence

`AppHostTopologyArchitectureTests` reads `AppHost.cs` and checks exact strings such as local variable declarations, conditional syntax, and a parsing function's body. Renaming a local variable can break the test without changing the application. Conversely, the expected string can remain while wiring or runtime behavior is incorrect. `CacheGovernanceTests` rejects two spellings of a metrics option; that does not prove the effective runtime option or emitted telemetry is safe. [E05]

There are also concrete nonpublic-coupling examples. `LocationCoordinateInvariantTests` inspects the parameter type of an **internal** factory; this is not a private-method invocation and should be described accurately. `LocationSemanticInvariantTests.AddressAndPiiBoundaryGuardsExposeTypedParameterIdentity` does invoke the private `Location.AttachPii` through reflection to check its null-argument exception. The public address/erasure scenarios in those files remain valuable; the private signature and private exception detail are candidates for removal or replacement. [E07]

Reflection itself is not the problem. Compiled dependency directions, actual public contract shape, runtime endpoint metadata, and a parser's machine-consumed schema can be legitimate architecture seams. The distinction is whether the assertion protects a current boundary or pins the implementation chosen to enforce it. Private-state fixture manipulation also deserves separate treatment: constructing a corrupt persisted state may be justified for a recovery scenario, while bypassing valid domain factories for ordinary setup usually increases maintenance cost.

- Lifecycle / severity / claim type: open / medium / observed weak assurance and refactoring coupling
- Principles / domains: Ihsan, Sidq, Amanah / technical, governance, evaluation
- Stakeholders / controlled decision: maintainers and operators / chosen assertion seam
- Evidence / validation: E05, E07, E08 / inspected test bodies and relevant member visibility
- Mitigation: **IVSD-M002**, replace source/private-detail assertions with public behavior, effective configuration, compiled boundaries, or emitted telemetry
- Owner / next validation: owning component maintainers; demonstrate that the replacement detects a relevant behavioral defect and survives a harmless refactor
- Escalation: preserve privacy, topology and authorization intent when replacing a weak mechanism

### IVSD-F003 — Mock forwarding is present, but interaction assertions must be judged by purpose

`DownloadAuthorizationPolicyPackageQueryHandlerTests.Handle_ReturnsArchiveFromPackageService` configures a substitute to return an archive, invokes the handler, asserts the same reference, and verifies one call. This can detect broken forwarding, but does not test archive contents, authorization, or delivery of the archive over HTTP. It is a strong low-value-test candidate once the meaningful service and endpoint contracts have suitable evidence. [E06]

The opposite example matters just as much. `InstanceBootstrapAuditLoggerTests` observes log state to verify that identity information is absent and only presence metadata is emitted. That is a privacy contract at an output boundary. Deleting it merely because it uses `Received(1)` would lose useful evidence. Its `Assert.That(true).IsTrue()` statements add no evidence and can be removed without removing the privacy assertions. A capturing log sink would make the observed output easier to inspect. [E06]

An external payment transport called twice can be a real duplicate-effect risk; an internal repository called exactly once usually describes implementation. Negative assertions such as “no provider request after failed authorization” can be important, but should be paired with the actual denied result and, where appropriate, unchanged durable state. This review does not classify every substitute as defective.

- Lifecycle / severity / claim type: open / medium / observed low-value forwarding test alongside valid output-boundary tests
- Principles / domains: Ihsan, Non-Harm, Rights of People / technical, evaluation
- Stakeholders / controlled decision: maintainers and people whose data or money is processed / mock placement and assertion meaning
- Evidence / validation: E06 and bounded Application search / source inspection, not whole-suite semantic classification
- Mitigation: **IVSD-M003**, retain pure logic units; move orchestrated behavior to integration tests; use controlled doubles at genuine external boundaries
- Owner / next validation: Application maintainers; classify each candidate against an explicit failure mode
- Escalation: do not remove fail-closed, privacy, or duplicate-effect checks as generic mock cleanup

### IVSD-F004 — The normal CI path does not provide the runtime assurance the documentation can suggest

The main `test.yml` caller sets `collect-coverage: true` and `run-integration-tests: false`. The reusable workflow does have real integration and five-provider jobs, but those jobs depend on the caller enabling them. Scheduled/manual coverage and deployment workflows enable runtime testing; the security workflow also selects specific API categories. Therefore “the repository has an integration matrix” and “this proposed change was integration-tested before merge” are different statements. [E03, E04]

The change detector explicitly ignores `src/Explore.AppHost/*` and `tests/Event.Benchmarks/*`. An AppHost-only change can therefore produce an intentional no-op in this workflow even though resource wiring affects startup. Benchmark changes also lack an ordinary PR execution gate; performance smoke is scheduled/manual. This is an observable routing gap, not proof that a recent deployment failed.

Documentation lists API contract tests as a PR lane where possible, but the inspected fast steps do not run a general API Fast/contract selection. They include a specific SetupLive authority selection alongside other fast projects. Some selected commands enforce a nonzero minimum; several broad commands do not supply that explicit guard. Actual runner defaults and recent counts were not measured here.

- Lifecycle / severity / claim type: open / high / observed CI selection gap
- Principles / domains: Amanah, Sidq, Promise-Keeping / governance, operational, evaluation
- Stakeholders / controlled decision: contributors, maintainers, operators / which evidence is required at each acceptance boundary
- Evidence / validation: E03, E04, E14 / workflow definitions and documentation comparison; branch protection not inspected
- Mitigation: **IVSD-M004**, give critical change paths explicit bounded PR lanes and give AppHost a real topology gate; retain the full matrix at the appropriate workstream/release boundary
- Owner / next validation: CI owner; verify routing with representative changed paths and inspect same-commit nonzero execution artifacts
- Escalation: required-check and release-evidence policy belongs to the maintainer; a workflow file alone cannot establish it

### IVSD-F005 — Current performance smoke is not a capacity or load test

The scheduled smoke selects `ApiEndpointBenchmarks`, with one warmup and three measured iterations. Its `ApiBenchmarkHostFactory` uses the `Testing` environment, EF InMemory, distributed-memory cache, a test authentication handler, allow-all authorization, and removed product hosted services. It measures requests through that deliberately simplified in-process host. It does not include the real network path, relational query contention, identity-provider behavior, or the competing work of dispatchers. [E13]

This is useful only when its question is explicit, for example whether a serializer or in-process response path became more expensive. It cannot support a claim about sustained registrations per second, checkout correctness during a spike, latency under a saturated database pool, or how long an outbox takes to recover after an outage. A separate PostgreSQL endpoint benchmark exists, but the scheduled filter does not select it.

The provider controls capacity claims and acceptance thresholds. Users bear the consequences when a popular event creates demand beyond tested limits. Add workload evidence with business correctness assertions and recovery observation, while retaining isolated benchmarks for diagnosis. BenchmarkDotNet and a load generator are complementary. [S03–S05]

- Lifecycle / severity / claim type: open / high / absent load evidence in the inspected tooling and limited existing benchmark realism
- Principles / domains: Non-Harm, Sidq, Ihsan / strategic, technical, operational, evaluation
- Stakeholders / controlled decision: attendees, organizers, operators / performance claims, workload model and infrastructure budget
- Evidence / validation: E13, E02; S03–S05 / source and workflow inspection; no measured capacity result
- Mitigation: **IVSD-M005**, establish realistic load, spike, stress and soak scenarios against a bounded deployed environment, with agreed targets and retained telemetry
- Owner / next validation: performance and operations owners; establish workload, hardware and acceptance envelope before interpreting results
- Escalation: no invented throughput or latency SLA; k6 adoption requires the tooling decision described below

### IVSD-F006 — Snapshot and contract testing exist, but their assurance must stay specific

The four reviewed Verify scenarios cover anonymous/authenticated event-list JSON and two ProblemDetails shapes. They use `ContractApiFixture`, which explicitly uses InMemory and substituted authorization. Error cases inject exceptions through a repository substitute. This is useful evidence for response formatting and error conversion, but it does not establish real query behavior, authorization decisions, or a consumer's successful use of the response. [E15]

The normalizer removes properties named `traceId`, `timestamp`, and `correlationId` recursively. Scrubbing nondeterminism is reasonable, but blanket removal of a property name would hide a meaningful field with that name if one enters a business payload. Keep snapshots small; scrub by the contract's known volatile paths and verify important relations and forbidden fields with explicit assertions.

A contract test is not synonymous with a snapshot or with Pact. For this repository's generated client, a high-value test sends a real request, deserializes the actual response through the supported contract/client, follows the allowed HAL relation, and checks the server's rejection when that action is unauthorized. Use consumer-driven contracts when independently evolving consumers justify them; do not add a broker and another framework merely to rename existing tests. [S06, S07]

- Lifecycle / severity / claim type: open / medium / narrow current evidence and opportunity for stronger consumer-facing contracts
- Principles / domains: Amanah, Sidq, Promise-Keeping / technical, design, evaluation
- Stakeholders / controlled decision: client developers, organizers and users / which payload and action promises are verified
- Evidence / validation: E15, E02; S06, S07 / inspected fixtures, snapshots and normalization
- Mitigation: **IVSD-M006**, extend existing Verify and HTTP/client tests at meaningful boundaries; pair snapshots with semantic and negative assertions
- Owner / next validation: API/client maintainers; prove a contract change fails the relevant test and reviewed intentional changes can update it cleanly
- Escalation: no backward-compatibility preservation requirement is introduced

### IVSD-F007 — Mutation-project isolation can hide useful domain tests from ordinary execution

`Event.Domain.AddOns.MutationTests` and `Event.Domain.Recovery.MutationTests` still exist as independent executable projects. Their bodies include catalog lifecycle, invalid commercial facts, monotonic inventory release, replay-safe refund behavior, and recovery-fence validation. These are current domain concerns, not automatically obsolete mutation scaffolding. [E16]

Neither project was found in the reviewed solution or workflow references. The root Stryker configuration still contains an 86 mutation break threshold, while repository instructions say mutation gating is disabled during greenfield development. That configuration alone does not prove an active gate. The immediate concern is unclear ownership and execution of valuable tests, plus unnecessary project overhead.

- Lifecycle / severity / claim type: open / medium / observed project-routing and governance drift
- Principles / domains: Ihsan, Amanah, Sidq / technical, governance, evaluation
- Stakeholders / controlled decision: maintainers / test placement, executable ownership and mutation policy
- Evidence / validation: E16, E01 / source and reference search; no execution result
- Mitigation: **IVSD-M007**, compare with the normal domain suite, move unique invariants into it, consolidate duplicates, then remove redundant wrapper projects/configuration
- Owner / next validation: Domain and CI maintainers; prove the surviving lane executes the retained invariants
- Escalation: deletion of the wrappers is conditional on invariant disposition, not their names or their mutation score

### IVSD-F008 — Conflicting guidance and stale metrics make verification harder to trust

`TESTING.md` records an explicitly dated 32-project checkpoint and describes dense test volume. Current source inventory differs. Historical data can remain historical, but test lines and ratios are not evidence of correctness. Codecov is informational at this revision; no inspected repository-wide 100% line or branch target was found. [E01–E03]

More consequentially, `PROJECTS.md` presents `--filter FullyQualifiedName` examples, while `TESTING.md` says TUnit uses `--treenode-filter` and not VSTest-style filters. Several rules describe a “five-database” matrix while listing four names; the actual matrix distinguishes MariaDB and MySQL. The rules also impose unconditional sub-two-second and sub-fifteen-second timing budgets without evidence from this review that cold build, restore, or container startup meets them. [E01, E04, E14]

Two other policies need clarification during implementation: “never query raw database state when a query exists” can discourage independent evidence of transaction/uniqueness invariants, and “never mock internal collaborators” conflicts with retained orchestration probes such as the Quartz callback test. Do not silently violate the current rules. Rewrite the authoritative testing guidance to distinguish product outcome assertions, persistence-level inspection, and deliberately limited host probes.

- Lifecycle / severity / claim type: open / medium / observed guidance inconsistency; unsupported quantitative proxies
- Principles / domains: Sidq, Ihsan / governance, evaluation
- Stakeholders / controlled decision: contributors and maintainers / authoritative commands, labels and quality claims
- Evidence / validation: E01, E03, E04, E11, E14 / documentary and source comparison
- Mitigation: **IVSD-M008**, establish one verified command/role matrix, distinguish measured timings from goals, and keep coverage diagnostic
- Owner / next validation: contributor-experience and CI owners; execute documented commands in the relevant implementation lanes
- Escalation: changes to repository invariants require explicit maintenance through their authoritative files and twin rules

### IVSD-F009 — Test fixtures and artifacts must respect the same secret and privacy boundaries

The reusable workflow contains literal database credentials in ephemeral service configuration and provider-matrix entries. The benchmark factory contains literal placeholder credential values. These are not evidence of leaked production credentials, and this report deliberately does not reproduce their values. They nevertheless conflict with the repository's strict prohibition on defining credentials in source and fixtures. [E03, E04, E13]

A real-container test can legitimately need a credential. It should receive it through the approved explicit test authority, or use a suitable secret-provider double where the secret boundary is not under test. The artifact path also matters: HTTP snapshots, failed assertion messages, traces, load-generator results, database diagnostics and received snapshot files can expose more than the test itself. Existing zero-PII logger and email telemetry tests are useful models to preserve.

- Lifecycle / severity / claim type: open / medium / observed secret-governance inconsistency; artifact exposure risk
- Principles / domains: Amanah, Rights of People, Avoiding Spying / technical, operational, governance
- Stakeholders / controlled decision: data subjects, operators, contributors / fixture configuration and retained diagnostic content
- Evidence / validation: E01, E03, E04, E06, E11, E13 / source inspection, not a production secret audit
- Mitigation: **IVSD-M009**, use approved injected test authority, synthetic data, output redaction and bounded artifact retention
- Owner / next validation: security/CI owners; exercise failure output and confirm secrets and synthetic PII canaries are absent
- Escalation: security expertise for any real secret discovery; no current claim of production credential compromise

### IVSD-F010 — Component and host tests need a small layer of deployed-journey and operational evidence

The default BFF fixture installs a test authentication scheme, bypasses claims transformation, uses ephemeral Data Protection, substitutes resolver/onboarding services, and uses memory cache. The separate security fixture adds real Keycloak-oriented configuration but still has deliberate substitutions. Those are useful targeted tests. They do not by themselves prove a browser can log in, maintain the intended session across a restart, follow tenant-scoped routes, and complete an authorized action through the deployed topology. [E17]

No Playwright or Aspire testing references were found in the bounded package/test/engineering/workflow search. This is an automated-lane evidence gap, not proof of no manual QA. Browser semantics such as keyboard focus, layout clipping, RTL presentation, actual cookie behavior and reconnect behavior need their own evidence. Likewise, a test simulating restore is not an operator successfully restoring a deployment within a documented recovery objective.

The provider controls these operational and accessibility promises. Keep the majority of rendering checks in bUnit, add a small risk-selected browser/distributed suite, and require rehearsals for the recovery claims the project intends to make. Test the session/storage configuration actually supported by Event; the presence of a Redis test dependency is not a reason to introduce Redis into the product.

- Lifecycle / severity / claim type: open / high before readiness claims / bounded deployed-journey and operational evidence gap
- Principles / domains: Adl, Amanah, Promise-Keeping, Ihsan / design, technical, operational, evaluation
- Stakeholders / controlled decision: disabled and RTL-language users, attendees, administrators, self-hosting operators / journey acceptance and recovery claims
- Evidence / validation: E17, E02, E18; S02 / source and package searches; browser and operational outcomes not tested here
- Mitigation: **IVSD-M010**, add a small deployed journey suite and owned recovery/accessibility validation appropriate to supported topologies
- Owner / next validation: UI/BFF, operations and accessibility owners; actual browser and restart/restore evidence
- Escalation: accessibility and operational expert review where the project makes corresponding readiness claims

## Recommendations

### Adopt a behavior-based test portfolio

The unit-test rule should be simple: **unit tests exercise pure logic without infrastructure or a network**. That includes money allocation, normalization, authorization decision functions that operate on explicit inputs, immutable value behavior, lifecycle transitions, and retry/backoff calculations. Pure logic is not limited to one assembly, though Domain should own business invariants under this repository's architecture.

A deterministic public input-to-output or input-to-state test should survive renaming a private helper, changing repository call ordering without changing semantics, or reorganizing internal classes. A test that fails for those changes needs a documented public contract reason.

| Test category | Boundary and principal assertion | Recommended Event tooling and role |
| --- | --- | --- |
| Pure unit | Inputs and explicit state produce expected values, transitions or rejection | Existing TUnit; fixed time/input; literal worked examples and useful parameterized cases |
| Application integration | Real command/query handler, validation, repository and transaction produce observable results and state | Existing TUnit and real engine fixture; substitute only the external dependency needed to make the scenario controlled |
| Persistence/provider | SQL translation, constraints, isolation, locks, migrations, key storage and transaction semantics | Existing Testcontainers and CI provider matrix; separate real connections for races |
| HTTP contract | Serialization, status, ProblemDetails, headers, HAL, authentication/authorization and request binding | Existing API hosts and generated clients; state whether authority/storage is real or substituted |
| Snapshot | Reviewed stable output structure | Existing Verify; small curated snapshots and precise volatile-field normalization |
| Consumer contract | A supported consumer can use the provider's actual response and allowed interactions | Generated-client round trips first; Pact only if independent consumer/provider evolution creates a concrete need |
| UI component | Rendered affordances, validation, events and semantic accessibility | Existing bUnit; server HAL links govern action visibility |
| Distributed integration | Actual process/resource wiring, startup order, health and selected cross-service journeys | Small Aspire.Hosting.Testing lane, after package/version and provenance review |
| Browser journey | Real browser cookies, navigation, keyboard/focus, RTL and reconnect behavior | Small browser automation lane plus manual accessibility review; runner selection remains implementation work |
| Load/capacity | Defined offered workload meets correctness, latency, error and recovery targets | k6 candidate, external to the application; NBomber excluded |
| Benchmark | Relative time/allocation cost of a named isolated operation or controlled endpoint harness | Existing BenchmarkDotNet; production-representative data where applicable; no capacity claim from microbenchmarks |
| Recovery/resilience | Restart, timeout, lost acknowledgement and restoration preserve invariants | Existing recovery/Quartz/provider tests, supplemented by deployment rehearsals |

Aspire testing launches an AppHost and its resources as a distributed application; Testcontainers provides disposable infrastructure for focused tests. They solve different scopes. Keep direct fixtures for most database tests; use Aspire where the wiring between actual processes is the subject. Respawn resets database contents; it neither creates the database server nor establishes concurrency isolation. [S02, S08, S09]

### IVSD-M001–IVSD-M003: Use an explicit retain, replace, consolidate, or delete disposition

The following is a reviewed candidate ledger, not an authorization to remove every listed file. Recommendations apply to the specified assertion/cohort; other tests in the same file may be essential.

| Candidate | Disposition | Reason and completion condition |
| --- | --- | --- |
| PostgreSQL refund reservation race | Retain | Checks the durable financial bound across competing connections; preserve independent final-state assertion. |
| Privacy primary-only restore and replay | Retain | Protects against resurrection and replay errors after restoration. |
| Email accepted-send/lost-settlement recovery | Retain | Protects durable ambiguity, duplicate-send prevention and privacy of diagnostics. |
| PostgreSQL RLS and worker-bypass tenant tests | Retain | Actual database authority boundaries cannot be replaced by mocks or HTTP happy paths. |
| `DownloadAuthorizationPolicyPackageQueryHandlerTests` forwarding-only case | Delete after disposition, or replace | Record where archive semantics and the endpoint's response/authorization are tested; do not build another wrapper solely to preserve this test. |
| `AppHostTopologyArchitectureTests` source-body assertions | Replace | Assert effective topology/resource behavior and invalid-input failure; retain useful parsed project/solution checks only if they express a current build contract. |
| `CacheGovernanceTests` string scans and test-owned classification table | Replace/consolidate | Observe effective metrics configuration and emitted label values; checking a test's own allowlist is not sufficient evidence of production telemetry. |
| `LocationCoordinateInvariantTests` nonpublic factory-signature assertion | Remove or move to a justified contract seam | Keep coordinate completeness, no-clamping and erasure behavior; internal parameter layout is not a user contract. |
| Private `AttachPii` invocation and exact private parameter-name assertion | Remove/replace that fragment | Exercise public location transitions and corruption/recovery through an appropriate persistence seam if needed. |
| `InstanceBootstrapAuditLoggerTests` unconditional true assertions | Delete the no-op assertions; keep the privacy behavior | A log sink should expose actual structured output; no need to remove the meaningful negative disclosure checks. |
| Generated-client tests checking retired names in source text | Consolidate or retire | If a current supported API-shape rule exists, inspect the compiled/generated contract; otherwise do not keep a permanent historical-name blacklist. |
| Dedicated AddOns/Recovery mutation projects | Consolidate, then remove wrappers | Move unique current domain invariants into the ordinary executed domain suite before removing project files and dead references. |
| Tests solely preserving a retired route alias, DTO shape or adapter | Delete once that sole purpose is established | No compatibility shim or replacement test is required for intentionally removed behavior. No comprehensive list of such tests was established in this sample. |
| Constructor/getter tests | Classify individually | A plain property echo is expendable; validation, immutable ownership, equality, redaction and serialization boundaries can be important pure logic. |

Do not infer duplication merely because two tests touch the same class. A pure refund-allocation calculation and a concurrent database refund reservation protect different failures. Conversely, many endpoint permutations with identical assertions may be consolidated into clear scenario data without losing their distinct boundary cases.

For each removed cohort, record the current behavior, why the old test is weak/obsolete, the retained or replacement evidence, owning lane, and the actual passing result. Do not set a percentage-of-tests-to-delete target. Use a behavior-preserving refactor as a maintainability check where useful; do not introduce a new mutation project to establish it.

### IVSD-M004: Make CI evidence explicit at the boundary where it matters

Keep the three-ring workflow, but treat timings as measurable goals and make each lane's claim precise.

1. **Inner loop:** run the affected pure-logic TUnit slice with no Docker/network/browser startup. Measure test execution separately from build/restore. No blanket migration of every Application test into Domain is justified.
2. **Change/PR boundary:** run affected projects, a bounded real-engine integration slice for persistence/orchestration changes, API/client contract checks for wire changes, and topology smoke when AppHost wiring changes. Critical trust and money changes need their relevant adversarial scenarios before acceptance, not only a later scheduled report.
3. **Workstream/release boundary:** run the full supported provider matrix, migration checks, critical distributed/browser journeys, and the applicable recovery/load campaigns once against the candidate revision. Scheduled tests supplement this evidence; they do not prove an arbitrary later commit.

Every selected lane should report its commit, topology/provider, exact command, started/completed status, pass/fail/skip counts, expected nonzero selection, and artifact location. A required lane with no tests, absent required infrastructure, a timeout, or missing artifacts must not masquerade as a pass. Preserve real failures even if a diagnostic rerun later succeeds.

Do not demand every slow provider/browser/load scenario on every local edit. Do not leave high-risk changes dependent solely on an unrelated nightly run either. The correct partition is by risk and the boundary under test.

### IVSD-M005: Define realistic load campaigns before choosing thresholds

Use **k6 as the candidate external load runner**, not a second implementation of business rules. NBomber is excluded. k6's arrival-rate/open model is useful when requests must continue arriving as the service slows; a closed virtual-user loop can unintentionally reduce offered demand when responses become slow. Use the model matching the user behavior and record attempted, completed and dropped work. Configure explicit pass/fail thresholds; collecting charts or checks alone is not the acceptance policy. [S03, S04]

The user request does not supply traffic or hardware targets. Establish synthetic data size, tenant distribution, read/write mix, authentication profile, request arrival rate, concurrency, ramp, steady duration and resource limits. Then agree thresholds for latency distributions, unexpected errors, completed business operations, correctness and recovery. Do not invent a universal “p95 < 200 ms” or a fixed concurrent-user target.

| Campaign | Event-specific question | Required observations |
| --- | --- | --- |
| Baseline mixed traffic | Can realistic discovery and management reads coexist with registrations? | p50/p95/p99 by operation, throughput, expected vs unexpected failures, database and runtime resource pressure |
| Registration spike | Does a popular event oversell or unfairly starve other tenants? | Accepted/rejected outcomes, inventory invariants, queue time, tenant distribution, rate-limit behavior |
| Duplicate-write pressure | Do retries after timeout create duplicate orders, payments or dispatch effects? | Idempotency outcomes and durable effect counts, including same key/different payload rejection |
| Contention stress | What fails first when a hot event or refund boundary is saturated? | Lock waits, deadlocks/retries, connection-pool saturation, bounded rejection and recovery after pressure falls |
| Soak | Do memory, connections, storage or background backlog grow without bound? | GC/allocations, thread-pool and pool pressure, queue depth/age, resource trends across the full run |
| Dependency degradation | What happens when a provider is slow, returns transient errors, or accepts a request before the connection is lost? | Timeout budgets, retry behavior, durable ambiguity, no duplicate irreversible effects, backlog drain after recovery |
| Noisy-tenant test | Can one tenant degrade or expose another's work? | Per-tenant outcome distribution using synthetic bounded labels, isolation checks and admission fairness |

Separate expected capacity/rate-limit rejection from system failure. A fast HTTP 200 returning the wrong result is not a performance success. Disable automatic client retries when they would hide a failure, or explicitly include and measure the retry policy being evaluated. The load generator must have enough resources that it is not the bottleneck.

Use a dedicated disposable environment with synthetic tenants and payment/provider sandboxes or controlled external simulators. Never run this workload against live payment operations or real personal data. Record application and generator versions, resource limits, provider versions, seed size and configuration; store redacted results with the tested commit.

**Tooling decision:** k6 workloads use JavaScript, while repository agent rules constrain JavaScript and persistent engineering tools. Before implementation, resolve explicitly whether reviewed k6 scenario assets are allowed and where they belong; do not silently call them an exception. Its dependency/distribution terms also need the repository's normal provenance review. This is a future adoption decision, not a reason to defer defining the load scenarios or to introduce NBomber.

### IVSD-M006: Use snapshots and contracts where they reduce ambiguity

Keep Verify for small stable HAL responses, ProblemDetails variants and representative protocol payloads. Review both the changed snapshot and the semantic reason for it. Do not auto-accept `.received` output in CI, snapshot production PII, or snapshot enormous object graphs that reviewers cannot evaluate. Explicitly assert that prohibited properties, cross-tenant data and unauthorized action links are absent.

Prefer targeted serialization/deserialization and generated-client round trips over source-text assertions about generated clients. Test omitted versus null fields, typed IDs, money units, enum/code representation, pagination, cancellation/error results, and HAL relation semantics where those are actual supported contracts. A pre-release intentional API change can update its current consumer tests and snapshots together; it does not need a permanent compatibility branch.

Pact remains optional. Its value is verification between independently evolving consumer and provider releases. For a generated client and server updated together in this repository, direct contract tests may achieve the requirement with less infrastructure. [S06, S07]

### IVSD-M007–IVSD-M009: Simplify project ownership, commands, and fixtures

Use the existing projects and fixture patterns first. Consolidate mutation-only wrappers into the correct domain suite after comparing their scenarios. Avoid a new general-purpose test platform, wrapper hierarchy or data-builder framework unless existing duplication demonstrates a need.

For relational tests, use the actual supported engine for semantics that differ by provider. SQLite is a legitimate provider to test in its own right; it is not proof of PostgreSQL RLS, SQL Server locking or MySQL behavior. EF InMemory should not be used as evidence for relational translation, constraints or transactions. Microsoft explicitly describes these limits and the value of real-database tests. [S01]

Share expensive container startup at a bounded fixture scope, but make each test's data ownership explicit. A shared database plus Respawn requires serialization around reset and use; a reset must not delete another concurrent test's rows. Alternatives include dedicated databases/schemas or separately owned identifiers, chosen according to what the test actually mutates. Stop or coordinate background writers before reset. Reset cached state, relevant external stores and test-owned authority data where the scenario requires it; resetting one PostgreSQL schema is not an environment-wide reset. [E12, S08, S09]

Use readiness signals rather than arbitrary startup sleeps. Use fixed `TimeProvider` inputs for pure time rules; retain real elapsed time only where timeout/scheduler behavior is itself under test, with bounded waiting and useful failure diagnostics. A timeout protecting a test from hanging is different from sleeping and hoping an operation finished.

Document fixture fidelity: real database, fake identity, real scheduler, disabled limiter, fake external provider, and so on. These are useful choices when visible. They become misleading when a test named “end to end” quietly removes the boundary it claims to cover.

### IVSD-M010: Complete the smallest critical journey and recovery portfolio

The following are acceptance scenarios to map to retained tests or add where no adequate evidence exists. “Not established” means this review did not establish the complete scenario, not that every related component is untested.

| Risk/journey | Current evidence reviewed | Additional evidence to establish |
| --- | --- | --- |
| Two refunds exceed captured capacity | Real PostgreSQL race and persisted total | Preserve; connect external retry/lost-acknowledgement scenarios to the financial state where applicable |
| Duplicate paid HTTP request | API idempotency/governance tests and lower-layer money races were identified | Complete simultaneous public HTTP requests through durable order/payment effects was not established in this review |
| Hold expiry races with payment completion | Related ticket/payment domain and recovery surfaces exist | Map both winning orderings and final inventory/payment outcomes before deciding anything is missing or redundant |
| Another tenant reads or changes data | RLS, query-filter and worker-bypass tests exist | Add/map the BFF/API-to-cache-to-database journey and pooled/reused connection cases for supported profiles |
| Erased PII returns after restore | Embedded authority recovery and external restore class exist | Preserve; operator rehearsal must verify actual backup topology, replay fence and release of readiness |
| Provider accepted a send, local commit failed | Clustered email test retains `Unknown` and avoids automatic resend | Preserve; verify authorized operator recovery and provider reconciliation where available |
| Persisted work after restart | Quartz restart/cluster tests exist | Preserve each test's narrow claim; map scheduler plus real work to durable outcome for critical jobs |
| Migration or seed failure | Five-provider migration and idempotency workflow exists | Require same-candidate artifacts; do not require historical-release upgrade matrices where no released baseline exists |
| BFF identity/session after restart | In-process BFF and separate security fixtures exist | Actual supported identity/session/key-ring topology, expiry/revocation and cross-tenant routing in a deployed journey |
| HAL action visibility and denial | bUnit and API contract checks exist | Pair visible/hidden links with server-side rejection; hiding a button alone is not authorization |
| Accessible registration/admin flow | Component semantics exist | Browser keyboard/focus/error announcements, RTL layout and manual assistive-technology validation |
| Sustained demand and dependency outage | Controlled benchmarks and targeted recovery tests exist | Load/spike/soak campaign plus bounded degradation and post-outage recovery evidence |

Do not build a Cartesian product of every provider, topology, identity mode, locale and browser for every scenario. Map the invariant to the smallest valid environment, then choose representative cross-boundary combinations and the separately required provider contracts.

### Rejected alternatives

- **Delete most tests because there is no release.** Rejected: release history and current behavior are different. Delete obsolete contracts, preserve current safety invariants.
- **Reach 100% coverage or a fixed mutation score.** Rejected as acceptance criteria: execution extent does not establish meaningful assertions, concurrency behavior, correct environment or absence of leaked data.
- **Delete every `Received` assertion or every use of reflection.** Rejected: external-effect, telemetry and compiled public-contract checks can be meaningful. Judge the boundary and assertion.
- **Mock every internal dependency to keep all tests fast.** Rejected: this removes the interactions most likely to fail under persistence, auth and transaction changes.
- **Move every test to Aspire/Testcontainers.** Rejected: pure logic and component feedback become unnecessarily slow and harder to diagnose.
- **Use snapshots for everything.** Rejected: huge baselines can normalize mistakes and hide forbidden behavior; important business assertions must stay explicit.
- **Install every suggested tool.** Rejected: existing tools already cover several needs. Add tools only for an established evidence gap. NBomber is expressly excluded.
- **Replace TUnit or introduce a new test abstraction layer during cleanup.** Rejected absent a demonstrated blocker: it adds migration work without directly improving safety evidence.
- **Turn each discovered unrelated failure into cleanup work.** Rejected: preserve the repository's quarantine rule during future implementation, while ensuring critical required evidence is not falsely reported green.

## Stakeholders

- **Attendees and buyers:** correct admission, clear failures, protected contact information, and no duplicate financial effects.
- **Organizers and tenant administrators:** reliable capacity, refunds and isolation; failures remain recoverable and understandable.
- **Data subjects, including private-home owners:** consent and erasure boundaries survive retries, logs, exports, delayed work and restoration.
- **Disabled and RTL-language users:** critical flows remain usable in the real browser, not only structurally plausible in markup.
- **Self-hosting operators:** supported provider/topology claims have executable evidence and usable recovery procedures.
- **Contributors and maintainers:** tests identify meaningful defects without breaking on every harmless refactor or consuming disproportionate diagnostic effort.
- **Project steward:** quality claims, dependency decisions, release gates and residual risks have named owners and evidence.

## I-VSD Principles And Domains

**Amanah — trust and responsible stewardship:** Event receives authority over money, private information and service operation. Testing is part of exercising that authority responsibly. Preserve evidence for the entrusted boundary rather than optimizing a dashboard total.

**Sidq — truthfulness:** distinguish source inspection, executed tests, measured coverage, measured performance and operational outcomes. A green fast lane is not full-provider evidence; a benchmark is not a capacity commitment; a snapshot is not a security audit.

**Non-Harm and Rights of People:** prioritize duplicate financial effects, tenant leaks, unauthorized actions, resurrection after erasure and unsafe recovery. Synthetic test data and redacted artifacts prevent the verification process itself from creating avoidable exposure.

**Adl — justice:** include accessibility and the distribution of load/failure between tenants. A service that is fast on average while excluding keyboard users or starving smaller tenants has not met the relevant responsibility.

**Ihsan — excellence:** maintainable tests, clear failure messages and proportionate infrastructure support sustained quality. This principle does not require maximizing test count, framework count or CI expense.

**Promise-Keeping:** portability, recovery, supported databases and availability claims need corresponding evidence. Pre-release freedom removes historical compatibility duties; it does not justify documenting unsupported current capabilities.

| Domain | Application to this consultation |
| --- | --- |
| Strategic | Define readiness by failure risk and supported operating limits; avoid quality claims based on coverage or package adoption. |
| Design | Verify accessible user journeys, understandable failures and HAL-governed actions. |
| Technical | Exercise pure logic, real persistence, authority boundaries, contracts and distributed wiring at appropriate seams. |
| Operational | Validate restart, recovery, capacity, artifact privacy and operator procedures. |
| Governance | Own lane selection, deletion disposition, command accuracy, dependency choices and residual risk. |
| Evaluation | Measure meaningful failures, flakiness, diagnostic cost, workload outcomes and recovery evidence over time. |

The business-model implication is limited to honest readiness claims and sustainable verification cost. This report makes no judgment about finance contracts, religious content or the permissibility of the business model.

## Evaluation And Ownership

These are proposed operating measures, not collected results. Assign actual people during planning; role names below are recommended ownership.

| Measure | Owner and source | Review cadence / action threshold | Stakeholders and limitation |
| --- | --- | --- | --- |
| Critical-invariant evidence completeness | Technical owner; scenario-to-test/lane register and same-commit artifacts | Every critical change and release candidate; any required missing/failed result blocks the corresponding readiness claim | Users/operators; a complete register still cannot prove absence of all defects |
| Lane execution integrity | CI owner; TRX and selection summaries | Every run; unexpected zero tests, required skips or missing artifacts require investigation | Contributors; a nonzero result alone does not prove correct selection |
| Flake frequency | Suite owner; first-attempt outcomes and reruns | Weekly; repeated nondeterminism gets an owner and bounded repair decision | Contributors; retries must not erase original failures |
| Feedback and diagnostic time | Developer-experience owner; measured warm execution, cold setup and triage separately | Weekly; sustained breach of an agreed budget triggers profiling and lane review | Maintainers; faster is not better if assurance disappears |
| Escaped critical defects | Feature owner; incident/defect records | Each incident and periodic review; every critical escape gets a regression scenario and causal review | Users; no incidents in a pre-release system is not a safety measurement |
| Capacity and recovery envelope | Operations/performance owner; controlled load runs and telemetry | Material hot-path/topology changes and readiness review; breach of the agreed envelope requires remediation or a narrower claim | Attendees/operators; hardware and workload assumptions must accompany results |
| Privacy of artifacts | Security owner; synthetic-canary/redaction checks and artifact review | New logging/snapshot/trace paths and periodic review; any prohibited disclosure blocks publication | Data subjects; absence of one canary does not prove all PII is excluded |
| Accessibility journey evidence | UI/accessibility owner; browser checks and expert/manual review | Material journey changes and release review; unresolved critical obstruction blocks that journey's readiness claim | Disabled/RTL users; automation alone is incomplete |

Coverage remains a diagnostic map for unexecuted branches and unexpected changes. Do not reward higher percentage, lower test count, or fewer CI minutes independently of the behavior and evidence retained.

## Validation Gaps

1. No .NET build, TUnit suite, container, benchmark, load test or browser test was executed for this report. The repository's documentation-only verification policy applies.
2. No actual current pass/fail/skip counts, runtime durations, flake rates, coverage percentage or capacity figures were collected.
3. CI definitions were inspected; live runs, GitHub required-check settings, downloaded TRX/coverage artifacts and deployment history were not audited.
4. Test bodies were sampled. The report does not label every test, quantify the fraction that should be deleted, or claim a complete private-method/mock audit.
5. The graph entrypoint was used for architecture discovery; its initial response was slow and supplied high-level communities. Detailed findings use bounded source/CI inspection rather than claiming a complete graph-derived execution analysis.
6. Cross-service paid HTTP duplicate requests, browser identity recovery and several other complete journeys were not established by this review. Related lower-layer tests exist and must be traced before adding duplicates.
7. Installed package compatibility with a proposed Aspire testing lane and k6 adoption/provenance constraints were not validated by installation or execution.
8. The report cannot infer real stakeholder experience, operator preparedness or scholarly validation from source code.

## Escalation Needed

No religious-legal ruling is requested or supplied. The principles used here support engineering responsibility and do not require this report to pronounce on finance permissibility or certify the product.

Future implementation needs technical ownership of critical deletion/replacement decisions, accurate CI acceptance boundaries, and a supported workload/recovery envelope. k6 scenario policy and dependency/distribution review must be resolved before adoption; this consultation does not grant an exception. NBomber remains excluded and requires no further selection work.

Security, accessibility and operational experts should validate the corresponding readiness claims. Unverified claims remain unverified until the relevant evidence exists. No user decision is needed to complete this report.

## Evidence Reviewed

### Repository evidence

All locators below refer to the reviewed Git revision unless explicitly identified as user instructions. Line landmarks are navigation aids, not immutable anchors after later edits.

| ID | Evidence and useful landmarks | Use in this report |
| --- | --- | --- |
| E01 | [AGENTS.md](../../AGENTS.md); [test-suite-rationalization intent](../../.agents/contract/intents.yaml), lines 297–405; [test rules](../../.agents/rules/tests.md); [Quick Reference](../../docs/internal/QUICK_REFERENCE.md); [TESTING.md](../../docs/internal/TESTING.md), inventory, taxonomy and disabled-test sections | Greenfield constraints, deletion policy, test roles and documented commitments |
| E02 | [Directory.Packages.props](../../Directory.Packages.props); [Explore.slnx](../../Explore.slnx); committed `.csproj` inventory under `tests/` and `eng/**/tests/` | Installed tooling, 19-project inventory, bounded absence searches |
| E03 | [Build & Test caller](../../.github/workflows/test.yml), lines 45–57 and 261–277; [reusable build/test workflow](../../.github/workflows/_build-test.yml), fast steps around 130–284; [Codecov configuration](../../.github/codecov.yml) | Ignored paths, coverage enabled, broad integration disabled in normal caller, informational coverage |
| E04 | [Provider matrix](../../.github/workflows/_build-test.yml), around lines 624–860 and subsequent selections; [coverage workflow](../../.github/workflows/coverage.yml); [deployment workflow](../../.github/workflows/deploy-coolify.yml), around 160–166; [security workflow](../../.github/workflows/security-tests.yml) | Five real provider lanes, alternate callers and targeted security selection |
| E05 | [AppHostTopologyArchitectureTests](../../tests/Event.Architecture.Tests/AppHostTopologyArchitectureTests.cs), lines 19–101; [CacheGovernanceTests](../../tests/Event.Architecture.Tests/CacheGovernanceTests.cs), lines 46–90 | Source-string assurance and test-owned telemetry classifications |
| E06 | [DownloadAuthorizationPolicyPackageQueryHandlerTests](../../tests/Event.Application.UnitTests/Features/InstanceOnboarding/Queries/DownloadAuthorizationPolicyPackageQueryHandlerTests.cs), lines 19–39; [InstanceBootstrapAuditLoggerTests](../../tests/Event.Application.UnitTests/Services/InstanceBootstrapAuditLoggerTests.cs), lines 18–109 | Forwarding-only test versus meaningful privacy-output assertions |
| E07 | [LocationCoordinateInvariantTests](../../tests/Event.Domain.UnitTests/LocationCoordinateInvariantTests.cs), lines 10–87; [LocationSemanticInvariantTests](../../tests/Event.Domain.UnitTests/LocationSemanticInvariantTests.cs), around 263–286 and 423–440; [LocationPii](../../src/Explore.Domain/LocationPii.cs), line 33; [Location](../../src/Explore.Domain/Location.cs), line 134 | Internal factory signature and actual private-method invocation; preserve public privacy invariants |
| E08 | [ApiClientNamingTests](../../tests/Explore.Blazor.Client.Tests/ApiClientNamingTests.cs), around 193–212; [EventProjectionMapperTests](../../tests/Event.Application.UnitTests/Profiles/EventProjectionMapperTests.cs), around 132–167 | Historical name/source tests and reflection-based fixture construction |
| E09 | [RefundReservationPostgreSqlConcurrencyTests](../../tests/Event.Persistence.IntegrationTests/RefundReservationPostgreSqlConcurrencyTests.cs), lines 24–85; [PaymentReconciliationPostgreSqlClaimTests](../../tests/Event.Persistence.IntegrationTests/PaymentReconciliationPostgreSqlClaimTests.cs) | Real refund race, durable amount and bounded recovery claim evidence |
| E10 | [EmbeddedPrivacyErasureRecoveryTests](../../tests/Event.Persistence.IntegrationTests/Privacy/EmbeddedPrivacyErasureRecoveryTests.cs), especially restore at line 214; [GlobalLocationPrivacyErasureTests](../../tests/Event.Persistence.IntegrationTests/Privacy/GlobalLocationPrivacyErasureTests.cs), external restore class | Existing privacy replay/restore coverage |
| E11 | [EmailDispatchQuartzClusterRecoveryTests](../../tests/Event.API.IntegrationTests/Features/EmailDispatchQuartzClusterRecoveryTests.cs), lines 36–110; [PaymentReconciliationQuartzRestartTests](../../tests/Event.API.IntegrationTests/Features/PaymentReconciliationQuartzRestartTests.cs), lines 19–75 | Durable unknown-send recovery; narrower real-scheduler/substituted-work restart probe |
| E12 | [PostgreSqlContainerFixture](../../tests/Event.Persistence.IntegrationTests/Fixtures/PostgreSqlContainerFixture.cs); [PostgreSqlApiFixtureBase](../../tests/Event.API.IntegrationTests/Fixtures/PostgreSqlApiFixtureBase.cs); [TestDatabaseReset](../../tests/Event.API.IntegrationTests/Fixtures/TestDatabaseReset.cs); [RLS tests](../../tests/Event.Persistence.IntegrationTests/TenantIsolation/PostgresTenantSessionRlsPrototypeTests.cs); [outbox bypass tests](../../tests/Event.Persistence.IntegrationTests/TenantIsolation/EmailDispatchOutboxRepositoryBypassTests.cs) | Real infrastructure, reset boundaries and tenant evidence |
| E13 | [performance-smoke.yml](../../.github/workflows/performance-smoke.yml); [ApiEndpointBenchmarks](../../tests/Event.Benchmarks/Benchmarks/ApiEndpointBenchmarks.cs); [ApiBenchmarkHostFactory](../../tests/Event.Benchmarks/Api/ApiBenchmarkHostFactory.cs); [PostgreSQL endpoint benchmark](../../tests/Event.Benchmarks/Benchmarks/ApiEndpointPostgreSqlBenchmarks.cs) | Actual selected performance harness and its limitations |
| E14 | [PROJECTS.md](../../PROJECTS.md), lines 79–148; [TESTING.md](../../docs/internal/TESTING.md), lines 304–386, 478–505 and fixture sections; [Context Engineering](../../.agents/CONTEXT_ENGINEERING.md), progressive verification section | Command/filter, inventory, provider-count and timing guidance drift |
| E15 | [HateoasSnapshotTests](../../tests/Event.API.IntegrationTests/Features/Snapshots/HateoasSnapshotTests.cs); [VerifySnapshotSettings](../../tests/Event.API.IntegrationTests/VerifySnapshotSettings.cs); [ContractApiFixture](../../tests/Event.API.IntegrationTests/Fixtures/ContractApiFixture.cs) | Four snapshot scenarios, broad volatile-field scrubbing, fixture fidelity |
| E16 | [AddOns mutation project](../../tests/Event.Domain.AddOns.MutationTests/Event.Domain.AddOns.MutationTests.csproj) and [tests](../../tests/Event.Domain.AddOns.MutationTests/EventAddOnMutationTests.cs); [Recovery mutation project](../../tests/Event.Domain.Recovery.MutationTests/Event.Domain.Recovery.MutationTests.csproj) and [tests](../../tests/Event.Domain.Recovery.MutationTests/TicketingRecoveryMutationTests.cs); [Stryker configuration](../../stryker-config.json) | Useful current domain tests in separate mutation projects; no found normal routing |
| E17 | [BFF default factory](../../tests/Explore.Blazor.IntegrationTests/Fixtures/BlazorBffWebApplicationFactory.cs), lines 59–125; [BFF security factory](../../tests/Explore.Blazor.IntegrationTests/Fixtures/SecurityBlazorBffWebApplicationFactory.cs), lines 42–90 | Identity, cache and key-ring substitutions; bounded confidence |
| E18 | [Operations](../../docs/internal/OPERATIONS.md); [prior rationalization I-VSD report](../workstreams/i-vsd-test-suite-rationalization.md) | Operational commitments and historical findings; prior plan-aligned status is not current execution evidence |
| E19 | [PrimaryDatabaseProviderBehaviorContractTests](../../tests/Event.Persistence.IntegrationTests/Database/PrimaryDatabaseProviderBehaviorContractTests.cs) | Real-provider smoke/behavior entrypoints, including durable key-ring recreation |
| E20 | User request and follow-up on 2026-10-22 | Detailed analysis only, greenfield breaking-change freedom, pure-logic unit emphasis, NBomber exclusion |

### External primary-source register

Access date: **2026-10-22**. Sources were used for public capability/interface facts, not copied test implementations. Recommendations and scenario organization are independently derived from Event's reviewed behavior and constraints. No package, third-party source, image or generated artifact was added. New dependency approval remains separate.

| ID | Primary source | Fact used and limit |
| --- | --- | --- |
| S01 | [Microsoft: choosing an EF Core testing strategy](https://learn.microsoft.com/en-us/ef/core/testing/choosing-a-testing-strategy) | Real database tests and the limits of substitutes; does not establish Event's tests pass |
| S02 | [Aspire testing overview](https://aspire.dev/testing/overview/) | AppHost/resource lifecycle and distributed closed-box testing; not a reason to move pure logic into distributed tests |
| S03 | [Grafana k6: open and closed models](https://grafana.com/docs/k6/latest/using-k6/scenarios/concepts/open-vs-closed/) | Offered-load modeling; workload selection remains Event-specific |
| S04 | [Grafana k6: thresholds](https://grafana.com/docs/k6/latest/using-k6/thresholds/) | Explicit performance pass/fail criteria; no universal latency/throughput target supplied |
| S05 | [BenchmarkDotNet: good practices](https://benchmarkdotnet.org/articles/guides/good-practices.html) | Controlled Release benchmarking; not production capacity validation |
| S06 | [Verify project documentation](https://github.com/VerifyTests/Verify) | Reviewed snapshots of actual output; no claim that snapshot approval establishes correctness |
| S07 | [Pact documentation](https://docs.pact.io/) | Consumer-driven contract purpose; adoption not required or approved here |
| S08 | [Testcontainers for .NET](https://dotnet.testcontainers.org/) and [wait strategies](https://dotnet.testcontainers.org/api/wait_strategies/) | Disposable infrastructure and readiness; not application correctness or reset isolation by itself |
| S09 | [Respawn documentation](https://github.com/jbogard/Respawn) | Database reset role; not container orchestration or general environment cleanup |
| S10 | [Microsoft: unit-testing best practices](https://learn.microsoft.com/en-us/dotnet/core/testing/unit-testing-best-practices) | Behavior-oriented unit testing and avoiding private implementation coupling |

### Inspection and verification record

The source investigation used bounded `rtk proxy rg`, `rtk proxy sed`, `rtk proxy git ls-files` and direct fixture/workflow reads, plus read-only scouts. Examples of exact reproducible inventory commands:

```bash
rtk proxy git ls-files tests eng | rtk proxy rg '(^tests/.*\.csproj$|^eng/.*/tests/.*\.csproj$)' | rtk proxy wc -l
rtk proxy rg --files tests/Event.Application.UnitTests -g '*.cs' | rtk proxy wc -l
rtk proxy rg -l 'Substitute\.For<' tests/Event.Application.UnitTests --glob '*.cs' | rtk proxy wc -l
rtk proxy rg -l '\.Received\(|\.DidNotReceive\(' tests/Event.Application.UnitTests --glob '*.cs' | rtk proxy wc -l
rtk proxy rg --files tests/Event.API.IntegrationTests | rtk proxy rg '\.verified\.' | rtk proxy wc -l
```

Observed outputs, respectively: **19**, **478**, **122**, **18**, **4**. These commands succeeded. File counts exclude ignored build output through normal tracked-file or ripgrep behavior and are not TUnit discovery results. A combined search for the absent tool references named in the inventory returned no matches in `Directory.Packages.props`, `tests`, `eng` and `.github/workflows`, excluding lockfiles.

### Report verification

The following checks were physically executed for this artifact:

| Check | Outcome |
| --- | --- |
| `rtk proxy git diff --no-index --check /dev/null islamic-value-sensitive-design/consultations/i-vsd-testing-strategy-technical-review.md` | No whitespace diagnostics. Exit 1 reflects the new file's difference from `/dev/null` in no-index mode; it is not recorded as a zero-exit command. |
| Bash local-link validation below | Exit 0; 55 local links checked, zero missing targets |
| Bash required-section validation below | Exit 0; all 14 required report headings present |
| Bash trailing-whitespace validation below | Exit 0; no trailing whitespace |
| `rtk proxy git rev-parse HEAD` | Exit 0; reviewed commit unchanged |
| `rtk proxy git status --short` | Exit 0; only this new report and the pre-existing unrelated untracked schedule report were listed |

Exact content-validation commands, run from the repository root:

```bash
rtk proxy bash -c 'report="islamic-value-sensitive-design/consultations/i-vsd-testing-strategy-technical-review.md"; failures=0; checked=0; while IFS= read -r target; do case "$target" in http*|\#*) continue ;; esac; target="${target%%#*}"; if [ -n "$target" ]; then checked=$((checked + 1)); if [ ! -e "$(dirname "$report")/$target" ]; then printf "BROKEN: %s\n" "$target"; failures=$((failures + 1)); fi; fi; done < <(grep -oE "\]\([^)]*\)" "$report" | sed -e "s/^.\{2\}//" -e "s/.$//"); printf "Checked %s local links; failures=%s\n" "$checked" "$failures"; test "$failures" -eq 0'
rtk proxy bash -c 'report="islamic-value-sensitive-design/consultations/i-vsd-testing-strategy-technical-review.md"; failures=0; for heading in "Review Metadata" "Executive Summary" "Scope" "Claim Boundary" "Findings" "Recommendations" "Stakeholders" "I-VSD Principles And Domains" "Validation Gaps" "Escalation Needed" "Evidence Reviewed" "Missing Evidence" "Context Inventory" "Review Lifecycle"; do if ! grep -qFx "## $heading" "$report"; then printf "Missing section: %s\n" "$heading"; failures=$((failures + 1)); fi; done; printf "Required sections: failures=%s\n" "$failures"; test "$failures" -eq 0'
rtk proxy bash -c 'report="islamic-value-sensitive-design/consultations/i-vsd-testing-strategy-technical-review.md"; if grep -nE "[[:blank:]]+$" "$report"; then exit 1; fi; printf "Trailing-whitespace check passed\n"'
```

No product build or suite is appropriate for this documentation-only change; this report makes no runtime pass claim. No report-schema validator was identified or run; required metadata, stable finding/mitigation IDs, authority boundaries and evidence distinctions were reviewed directly.

## Missing Evidence

- Recent retained same-commit test, coverage, performance and runtime artifacts, including required-check configuration.
- Full per-test retain/replace/delete disposition and comparison of mutation-project invariants with the normal domain suite.
- Measured cold/warm execution times, flake history, failure triage cost and escaped-defect records.
- Agreed traffic, dataset, hardware, deployment topology, latency, error and recovery objectives.
- Complete browser and accessibility evidence for critical journeys, and operator restore rehearsal results.
- Approved k6 scripting/provenance decision and verified Aspire testing-package fit with the current AppHost/TUnit environment.
- Stakeholder validation and any future scholarly assessment outside this technical consultation's authority.

## Context Inventory

- Available and used: repository testing/governance/operations documents, source and fixtures, package pins, solution inventory, CI definitions, prior I-VSD rationalization report, user requirements and correction.
- Project tools: knowledge-graph architecture discovery attempted; read-only evidence scouts used for broad inventory, quality samples and integration coverage. Their absence claims were checked against the provider matrix and recovery sources before inclusion.
- External research: official upstream documentation, retrieved through Firecrawl, for testing roles and current tooling facts. Research cache is outside the repository; raw research material is not part of this deliverable.
- Not used as evidence: live GitHub runs, private issue trackers, production dashboards, personal data, user interviews or organizational incident records. No assertion is made that those systems contain no relevant evidence.
- Existing unrelated untracked schedule-redesign report: outside scope and left unchanged.

## Review Lifecycle

| Date | Previous status | New status | Trigger | Evidence/replacement |
| --- | --- | --- | --- | --- |
| 2026-10-22 | none | current | Requested repository-grounded testing consultation completed against named revision; NBomber excluded by user direction | E01–E20 and S01–S10; disposition changes-required; findings remain open |

Refresh this assessment after material changes to test projects, fixture fidelity, CI selection, critical application behavior, supported provider/topology claims, or the chosen load/distributed-testing approach. A successful future cleanup must resolve findings with actual retained/replacement evidence, not simply update this report's date.
