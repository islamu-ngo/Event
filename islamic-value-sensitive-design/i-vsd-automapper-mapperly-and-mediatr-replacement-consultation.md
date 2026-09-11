# I-VSD Consultancy Report: Exiting Lucky Penny Dependencies (AutoMapper to Mapperly, MediatR Replacement)

Last Updated: 2026-09-11

## Review Metadata
- Mode: standalone
- Subject: AutoMapper and MediatR dependency sovereignty for ISLAMU Event (Lucky Penny exit)
- Workstream: none (pre-planning consultation)
- Report kind: consultancy-report
- Report status: current
- Disposition: ready-for-planning
- Evidence cutoff: 2026-09-11
- Reviewed input: working-tree on `develop` at 2026-09-11 (uncommitted erasure-key rename in progress; unrelated to this subject)
- Supersedes: none. Extends `docs/internal/DUAL_VERSIONING.md` section 12 ("Long-Term Migration"), which deferred this decision.

## Scope

This consultation reviews two provider-controlled dependency decisions for the ISLAMU Event platform:

1. **AutoMapper to Mapperly (decided).** The Project Steward has already decided to migrate every AutoMapper usage to Mapperly. This report records the provider-responsibility reasoning behind that decision, the benefits and duties it creates, the migration surface, and the failures a careless migration would introduce. It does not reopen the decision.
2. **MediatR replacement (decided 2026-09-11).** The Steward has decided to replace MediatR with repository-native command/query handler abstractions and Gang-of-Four generic decorators for cross-cutting concerns, with **zero libraries** (Scrutor is explicitly excluded). The design reference is Chapter 10 of *Dependency Injection Principles, Practices, and Patterns* (Seemann and van Deursen, Manning, 2019) and van Deursen's 2011 "Meanwhile... on the command side / query side of my architecture" articles. This report records the basis, the trade-offs the Steward accepts, and the repository-specific constraints the textbook pattern collides with.

Both decisions share a root cause: AutoMapper 15+ and MediatR 13+ moved to Lucky Penny commercial licensing, and the repository currently carries a **dual-versioning build** (`docs/internal/DUAL_VERSIONING.md`) to stay on the last permissive releases by default while allowing a commercial build. The Steward has rejected maintaining two container images (FOSS and commercial) as operationally unacceptable, which removes the only mechanism that made dual-versioning usable for prebuilt distribution.

**Reviewed:** dependency licensing, the security posture of the frozen FOSS versions, the migration surface in `Explore.Application` and its tests, the build/deploy plumbing the dual-versioning strategy created, outbound-licensing compatibility, and the operational duties toward self-hosters and contributors.

**Not reviewed:** implementation sequencing (belongs to the implementation plan), performance benchmarks (none exist; see Missing Evidence), and any question of religious-legal permissibility of commercial software licensing (out of I-VSD scope; see Escalation Needed).

## Claim Boundary

This is I-VSD provider-responsibility design reasoning and implementation traceability. It is not a legal opinion on license compatibility, a security certification, a performance benchmark, a fatwa, or proof that the migration will succeed. License-compatibility statements below are design-level readings of well-known permissive terms and must be confirmed by the Project Steward's dependency-license policy check (`.ci/scripts/validate-dependency-license-policy.cs`) before merge. No third-party source code was read or ingested for this report; all library claims come from official license files, package metadata, and the repository's own usage.

## Findings

### IVSD-F001: The dual-versioning strategy has become a promise the provider cannot keep
- Lifecycle: `open`
- Severity: High
- Claim type: Design reasoning, implementation traceability
- Principle and domain: Promise-Keeping, Truthfulness (Sidq), Avoiding Gharar | Strategic, Operational, Governance
- Stakeholders: self-hosters, contributors, ISLAMU operators
- Provider-controlled decision: whether to keep a build-time edition switch that no distribution path exercises
- Evidence: E01, E02, E03, E04, E05
- Description: The edition is selected at image build time (`Directory.Packages.props:11-35`, `src/Explore.API/Dockerfile:12-48`). `docker-compose.yml:481` and `:551` build without `args:`, `.ci/` and `.github/` never pass the flag, and the runtime `USE_COMMERCIAL_LUCKYPENNY` variable is mapped to `Licensing:LuckyPenny:Enabled` (`ConfigurationExtensions.cs:462`) and then read by nothing. `docker-compose.yml:156-157` sets `AUTOMAPPER_COMMERCIAL_VERSION: false`, a version variable defaulting to a boolean. The public documentation and the Infisical setup guide list these as real configuration. Operators are being told a switch exists that, in practice, only a manual `docker build --build-arg` exercises. This is an unkept promise and a source of uncertainty (gharar) about what edition a given deployment is running.
- Linked mitigation: IVSD-M001, IVSD-M004
- Owner: Project Steward

### IVSD-F002: The default (FOSS) AutoMapper is security-frozen with a known DoS advisory
- Lifecycle: `open`
- Severity: High
- Claim type: Implementation traceability
- Principle and domain: Non-Harm (La Darar), Trust (Amanah), Excellence (Ihsan) | Technical, Operational
- Stakeholders: every self-hosted instance and its attendees; ISLAMU operators
- Provider-controlled decision: whether the default build ships a library that will never receive a patch
- Evidence: E03, E06
- Description: AutoMapper 14.0.0 carries CVE-2026-32933 (uncontrolled recursion, denial of service) and will not be patched under MIT. The repository mitigates it with a global `MaxDepth(64)` ceiling in `ApplicationServicesRegistration.cs` and suppresses only that advisory via `NuGetAuditSuppress`. This is a reasonable stopgap, but it is a provider-owned mitigation of a vendor defect that the vendor has chosen to monetize. Every future advisory in the frozen line will require the same treatment or a paid upgrade. The default edition is the one every community self-hoster runs, so the least-resourced operators carry the most exposure.
- Linked mitigation: IVSD-M001
- Owner: Project Steward

### IVSD-F003: AutoMapper's runtime-configured mapping hides contract errors until execution
- Lifecycle: `open`
- Severity: Medium
- Claim type: Design reasoning
- Principle and domain: Excellence (Ihsan), Truthfulness (Sidq) | Technical, Evaluation
- Stakeholders: maintainers, API consumers, tenants relying on correct DTO data
- Provider-controlled decision: whether mapping correctness is a compile-time guarantee or a test-time hope
- Evidence: E07
- Description: The 11 profiles (1,151 lines) contain roughly 590 `ForMember` configurations, 297 `MapFrom`, 290 `Ignore`, and 45 `ReverseMap` calls. Mapping validity is checked only when `MapperConfiguration` is built or when a test calls configuration validation. A renamed DTO property or a new entity field is silently dropped or defaulted at runtime unless a test happens to cover it. In a platform that maps PII-bearing entities to public DTOs, a silent unmapped-member is a correctness and privacy hazard in both directions: data that should reach the client does not, or a newly added sensitive field is copied by convention because a profile did not `Ignore` it.
- Linked mitigation: IVSD-M001, IVSD-M002
- Owner: Application layer maintainers

### IVSD-F004: MediatR's frozen Apache-2.0 line creates the same long-term exposure with a much larger surface
- Lifecycle: `open`
- Severity: High
- Claim type: Implementation traceability
- Principle and domain: Non-Harm (La Darar), Promise-Keeping, Avoiding Gharar | Technical, Strategic
- Stakeholders: maintainers, self-hosters, every API consumer (every request passes through it)
- Provider-controlled decision: replacement strategy for the request dispatch backbone
- Evidence: E08, E09
- Description: MediatR 12.5.0 is the last Apache-2.0 release and will not receive patches. Unlike AutoMapper, MediatR is on the hot path of every command and query: 910 `IRequestHandler` implementations, 848 request declarations, 947 `Send`/`Publish` call sites, 1,406 referencing files in `Explore.Application` and 181 in `Explore.API`, and 137 `IMediator`/`ISender` test substitutes across 112 test files. The repository uses a narrow feature subset: two pipeline behaviors (`AuthorizationBehavior`, `PerformanceBehavior`), two notifications with three handlers, three void requests (two `IRequest<Unit>`, one `IRequest`), and zero streaming, pre/post-processors, or exception handlers. The narrowness of the subset is the decisive fact: the platform depends on a small dispatch contract, not on MediatR's full feature set.
- Linked mitigation: IVSD-M003
- Owner: Project Steward

### IVSD-F005: Internal documentation misstates Mapperly's license
- Lifecycle: `open`
- Severity: Low
- Claim type: Implementation traceability
- Principle and domain: Truthfulness (Sidq) | Governance
- Stakeholders: contributors, Project Steward (outbound-licensing duty)
- Provider-controlled decision: accuracy of the dependency record before adoption
- Evidence: E10, E11
- Description: `docs/internal/DUAL_VERSIONING.md:289` describes Mapperly as MIT. The upstream `LICENSE` file and NuGet metadata state Apache License 2.0. Apache-2.0 is permissive and compatible with the platform's AGPL-3.0-or-later outbound path and with permissive alternative enterprise licensing, so the practical outcome is unchanged, but the dependency record must be correct before the IP clean-room checklist is signed. A secondary open question exists upstream about whether generated mapper output carries any license obligation (E11); the design reading is that generated code in the consuming project is the consumer's own, but this is recorded as an evidence gap, not a conclusion.
- Linked mitigation: IVSD-M005
- Owner: Project Steward

### IVSD-F006: The migration touches the identity/tenant trust boundary indirectly
- Lifecycle: `open`
- Severity: Medium
- Claim type: Design reasoning
- Principle and domain: Trust (Amanah), Rights of People, Justice (Adl) | Technical, Governance
- Stakeholders: every tenant and authenticated user
- Provider-controlled decision: whether `AuthorizationBehavior` semantics are preserved bit-for-bit when it becomes a pair of generic decorators applied by the composition helper
- Evidence: E09
- Description: `AuthorizationBehavior<TRequest,TResponse>` is the pipeline stage that enforces resource-level authorization before any handler runs. It is constrained on `IRequest<TResponse>` and relies on behavior ordering guaranteed by the dispatcher. Any MediatR replacement must preserve: behaviors execute in registration order, the authorization behavior wraps every request type without opt-out, a request with no registered handler fails closed with a diagnosable error rather than a null result, and cancellation propagates. A replacement that silently skips a behavior for some generic shape (for example open-generic resolution differences) would be a tenant-isolation regression with no compile error.
- Linked mitigation: IVSD-M003, IVSD-M006
- Owner: Application layer maintainers; security review

### IVSD-F007: The textbook validation decorator contradicts the repository's validator convention
- Lifecycle: `open`
- Severity: Medium
- Claim type: Implementation traceability
- Principle and domain: Promise-Keeping, Excellence (Ihsan) | Technical, Governance
- Stakeholders: contributors, reviewers
- Provider-controlled decision: whether adopting the decorator pattern silently changes an established governance rule
- Evidence: E15, E16
- Description: QUICK_REFERENCE rule #2 states validators are manually instantiated in handlers, never injected as `IValidator<T>`. The repository has 283 manual instantiations and zero DI resolutions of `IValidator<>`. The reference design's `ValidationCommandHandlerDecorator` resolves validators from the container. Adopting it as-is would overturn a documented convention as a side effect of a dependency migration, without a governance decision.
- Linked mitigation: IVSD-M008
- Owner: Project Steward

### IVSD-F008: A blanket transaction decorator would nest around handler-owned transactions
- Lifecycle: `open`
- Severity: High
- Claim type: Implementation traceability
- Principle and domain: Non-Harm (La Darar), Trust (Amanah) | Technical, Operational
- Stakeholders: registrants and tenants whose writes must be atomic; privacy-erasure subjects
- Provider-controlled decision: where transaction boundaries are owned
- Evidence: E15
- Description: 152 handlers begin and own their own transactions, including the serializable privacy-erasure purge path and outbox-coordinated writes that must commit in one unit. A generic transaction decorator applied to every command would open an outer transaction around those, producing nested-transaction behavior that differs by database provider (the platform supports five) and risking double-commit or silent no-op rollbacks. This is the class of failure that corrupts atomic-purge guarantees.
- Linked mitigation: IVSD-M009
- Owner: Application layer maintainers

### IVSD-F009: Explicit handler injection exposes oversized controllers
- Lifecycle: `open`
- Severity: Low
- Claim type: Implementation traceability
- Principle and domain: Excellence (Ihsan) | Technical
- Stakeholders: contributors, reviewers
- Provider-controlled decision: controller granularity when dependencies become visible
- Evidence: E15
- Description: 166 controllers inject `IMediator`. The largest make 25, 17, and 17 `Send` calls each. `IMediator` hid that fan-in; explicit injection surfaces it as a 25-parameter constructor. This is a maintainability finding, not a harm, but the migration must decide how to handle it rather than accept the smell.
- Linked mitigation: IVSD-M010
- Owner: API layer maintainers

## Recommendations

### Decision (AutoMapper)

**Migrate fully to Mapperly. Accepted by the Project Steward; this report records the basis.**

Why it is the right provider decision, traced to duties:

| Duty | How Mapperly serves it |
|---|---|
| Non-Harm: remove the frozen CVE-2026-32933 surface | Source-generated mapping has no runtime reflection-driven recursion; the `MaxDepth` ceiling and the `NuGetAuditSuppress` entry become deletable. |
| Excellence: correctness at compile time | Unmapped source or target members are reported as build diagnostics that can be promoted to errors; the 590 `ForMember` lines become explicit `[MapProperty]`/`[MapperIgnore*]` attributes or plain generated code that reviewers can read. |
| Rights of People: PII does not leak by convention | Mapperly's default is to report unmapped members; the platform should configure the generator so that a newly added entity property must be explicitly mapped or ignored, which turns the current "copied unless ignored" hazard into "fails to build unless decided". |
| Promise-Keeping: one edition, one image | No commercial line exists; the FOSS/commercial switch, both Dockerfile `ARG`s, the conditional package groups, the `USE_COMMERCIAL_LUCKYPENNY_LIBS` preprocessor guards (registration plus six test files), and the four environment variables can all be removed. |
| Avoiding Gharar: contributors know what they build | `dotnet build` produces the only edition. `DUAL_VERSIONING.md` retires. |
| Trust: outbound licensing stays clean | Apache-2.0 is compatible with AGPL-3.0-or-later and with permissive enterprise relicensing under the CLA. |

Migration surface (evidence E07): 11 profiles, 1,151 LOC; roughly 400 `IMapper` injection sites in handlers; 43 test sites that construct `MapperConfiguration` or substitute `IMapper`; 21 test files. Notably **zero** `ProjectTo`, `AfterMap`, `BeforeMap`, `ConvertUsing`, `IValueResolver`, or `ITypeConverter` usages. The absence of imperative hooks and `IQueryable` projection means the migration is a declarative rewrite, not a behavioral one. Nothing outside `Explore.Application` and its tests references AutoMapper.

### Decision (MediatR): repository-native CQS handlers with generic decorators, zero libraries

**Decided by the Project Steward on 2026-09-11.** MediatR's `IRequest`/`IRequestHandler`/`IPipelineBehavior`/`ISender` model is replaced by two project-owned handler abstractions following Command-Query Separation, with cross-cutting concerns implemented as generic Decorator-pattern wrappers composed by the standard `Microsoft.Extensions.DependencyInjection` container. No mediator, no dispatcher, no reflection-based `Send`. No third-party wiring library: Scrutor is excluded by decision.

Functional shape (design-level; the implementation is written independently in the repository, not copied from any book or article):

- `ICommand`, `ICommand<TResult>`, `ICommandHandler<TCommand>`, `ICommandHandler<TCommand, TResult>` for state changes. The repository's commands overwhelmingly return values (only three of ~331 command requests are void: two `IRequest<Unit>` plus one non-generic `IRequest`), so a result-bearing command interface is required; strict void-only CQS would force artificial follow-up queries.
- `IQuery<TResult>`, `IQueryHandler<TQuery, TResult>` for reads (~312 query handlers).
- Consumers (166 controllers, 5 MCP tool classes, 3 Infrastructure workers) inject the specific handler interface they need instead of `IMediator`. Dependencies become visible in the constructor signature and compile-time checked.
- Cross-cutting concerns are generic decorators implementing the same handler interface and wrapping an inner handler: authorization and performance timing migrate from the two existing pipeline behaviors; each becomes a command decorator and a query decorator (two interfaces, two wrappers).
- Notifications (2 notification types, 3 handlers) are not a decorator concern. They keep a small project-owned `INotification` / `INotificationHandler<T>` pair with consumers injecting `IEnumerable<INotificationHandler<T>>` and awaiting every handler; failures surface, never swallowed.

Why this is the right provider decision, traced to duties:

| Duty | How the decorator design serves it |
|---|---|
| Trust (Amanah): the authorization choke point is owned code | Authorization is a decorator the repository writes, tests, and reads; no vendor owns the request backbone. |
| Truthfulness (Sidq) and Excellence (Ihsan): dependencies are explicit | A controller declaring `ICommandHandler<PublishEventCommand>` tells the reader and the compiler exactly what it calls. `IMediator.Send` hid that behind a generic method and forced 137 generic mock setups in tests. |
| Non-Harm (La Darar): no dynamic dispatch on the hot path | Handler resolution is a normal DI lookup of a closed generic type; call stacks are plain decorator chains, debuggable without reflection frames. |
| Promise-Keeping: zero external licensing on the backbone | There is nothing to relicense, freeze, or dual-version. |
| Avoiding Gharar: no hidden framework behavior | Every cross-cutting step is a class in the repository with a test; execution order is the decorator nesting order, visible in one composition-root file. |

The trade-off the Steward accepts: this is a **full migration of ~1,400 files**, not a namespace swap. Every request type is reclassified as command or query, every handler changes interface and method name, every consumer changes from `IMediator` to explicit handler injection, and every test substitute of `IMediator`/`ISender` (137 sites) is rewritten as a plain handler substitute. The report treats that cost as the price of removing dynamic dispatch entirely, which the namespace-swap alternative would have retained.

Design constraints the implementation must satisfy (these become plan acceptance criteria):

1. **Independent implementation (IP clean-room rule 8).** The book and articles are design references recorded in the source register; no book, article, or MediatR code is copied. The repository's own 910 handlers and two behaviors are the functional specification. Interface names describing the pattern (`ICommandHandler<T>`, `IQueryHandler<TQuery,TResult>`) are functional API surface, not protected expression.
2. **Decoration without a library (IVSD-M007).** `Microsoft.Extensions.DependencyInjection` has no open-generic decoration primitive. The repository owns a composition-root helper that performs a **one-time startup assembly scan** of handler implementations and registers each closed handler interface with a factory that builds the decorator chain (outermost last) using `ActivatorUtilities`. Startup-only reflection at the composition root is standard practice and is not the per-call dynamic dispatch the decision removes. Per-handler hand registration (~640 lines) is rejected as unmaintainable; a generated registration file is rejected as an unnecessary artifact.
3. **Authorization wraps everything, no opt-out (IVSD-M006).** The authorization decorator is applied to every command and query handler by the composition helper. An architecture test asserts that every resolvable `ICommandHandler<>`/`IQueryHandler<,>` instance is the authorization decorator type at the outermost layer, including a handler type added after the helper was written.
4. **Validation stays as it is (IVSD-M008).** The textbook `ValidationDecorator` that resolves `IEnumerable<IValidator<T>>` from DI conflicts with QUICK_REFERENCE rule #2 (validators are manually instantiated; 283 sites, 0 DI resolutions). No validation decorator is introduced in this migration. Changing the validator convention is a separate governance decision.
5. **No blanket transaction decorator (IVSD-M009).** 152 handlers begin and own their own transactions (including serializable privacy-erasure transactions and outbox-coordinated writes). A generic transaction wrapper would nest or double-commit around them. Transaction ownership stays in handlers; a decorator may be reconsidered only after a handler-by-handler inventory.
6. **Controller fan-in is managed, not ignored (IVSD-M010).** `RegistrationProviderManagementController` makes 25 `Send` calls; `SettingsController` and `GuestRegistrationOrderController` make 17 each. Constructor-injecting 25 handlers is a code smell that signals a controller doing too much. The plan must choose per controller between action-level `[FromServices]` injection (handler resolved per action, constructor stays small) and splitting the controller by aggregate. Either is acceptable; a 25-parameter constructor is not.
7. **Retire `Unit`.** The two `IRequest<Unit>` requests and the one non-generic `IRequest` (`ProcessAiRunCommand`) become void `ICommand`. (Correction 2026-09-11: an earlier draft counted six `Unit` text hits as six requests; the plan's source inventory established the true shape.)
8. **Fail closed on composition errors.** A handler interface with no implementation, or two implementations, fails at startup validation (`ValidateOnBuild`), never at first request.
9. **Zero-PII decorators.** The performance decorator logs the request *type name* and elapsed time only. No decorator serializes, interpolates, or destructures a request instance.

### Rejected alternatives (MediatR)

- **In-repo MediatR-shaped dispatcher (namespace swap).** This report's earlier recommendation. Rejected by the Steward on 2026-09-11: it would have kept `ISender.Send` dynamic dispatch, hidden handler dependencies behind a generic method, and preserved the generic-mock testing style. The cheaper migration was judged not worth keeping the mediator indirection.
- **Pay for Lucky Penny and build one commercial image.** Rejected by the Steward: it would make the only distributed image depend on a commercial license key, contradicting the self-hoster promise in `DUAL_VERSIONING.md` and `docs/public/.../sponsorship.md`. It also would not fix AutoMapper's runtime-configuration hazards.
- **Scrutor for scanning and decoration.** Rejected by the Steward. It is permissively licensed and would reduce the composition helper to a few lines, but it is one more external dependency on the request backbone, and its open-generic decoration relies on the same startup reflection the repository can own directly.
- **Maintain two images (FOSS and commercial).** Rejected by the Steward as too much operational surface; every release, security scan, and support answer would need to be doubled.
- **Stay on MediatR 12.5.0 indefinitely.** Rejected. It reproduces IVSD-F002's frozen-dependency exposure on the hot path, with a larger blast radius and no upstream patch route.
- **Adopt a third-party source-generated mediator (Apache/MIT-licensed community projects).** Not recommended. These are permissively licensed and fast, but each has a different pipeline signature (so the 1,400-file migration is no longer a namespace swap), most are single-maintainer projects (bus-factor risk on the request backbone), and a source generator adds build-time coupling that the repository would then own operationally anyway. The platform needs a dispatcher, not a framework.
- **Adopt a full messaging framework with mediator capabilities (Wolverine, Brighter).** Not recommended. They bring their own DI conventions, transactional outbox, and message transports. The repository already owns a transactional outbox pattern; overlapping infrastructure creates two sources of truth for message durability.
- **Direct handler injection without decorators.** Rejected. Injecting bare handlers removes the single choke point where authorization is enforced; controllers would have to remember to authorize, which is exactly the class of omission the behavior exists to prevent. The decided design keeps explicit injection but restores the choke point as the outermost decorator applied by the composition helper to every handler.

### Quick fixes (independent of the migration)
- IVSD-M004 (documentation truthfulness) can ship immediately: stop listing `USE_COMMERCIAL_LUCKYPENNY`, `AUTOMAPPER_COMMERCIAL_VERSION`, and `MEDIATR_COMMERCIAL_VERSION` as runtime configuration; correct the Mapperly license in `DUAL_VERSIONING.md`.

### Architecture changes
- IVSD-M001: Mapperly migration, one bounded slice per profile, unmapped-member diagnostics promoted to errors from the first slice.
- IVSD-M003: repository-native CQS handler abstractions and generic decorators with the nine constraints above. Suggested order for the plan: abstractions and composition helper with architecture tests first; authorization and performance decorators second; then feature-folder slices converting handlers and their consumers together, each slice building and passing Ring 1.
- IVSD-M007: composition-root helper that scans handler implementations once at startup and registers each closed handler interface with a decorator-chain factory; covered by an architecture test that every handler is registered exactly once and decorated in the declared order.
- IVSD-M008: no validation decorator in this migration; validators remain manually instantiated per QUICK_REFERENCE rule #2. If the Steward wants DI-resolved validators, that is a separate governance decision with its own rule change.
- IVSD-M009: no blanket transaction decorator; transaction ownership stays in handlers. A future decorator requires a handler-by-handler inventory proving no nesting across all five database providers.
- IVSD-M010: per controller, choose action-level `[FromServices]` handler injection or splitting by aggregate; no constructor may grow past the repository's existing parameter-count analyzer threshold.
- IVSD-M002: after both migrations, delete the dual-versioning plumbing in one commit: conditional groups in `Directory.Packages.props`, `DefineConstants` and locked-mode split in `Directory.Build.props`, both Dockerfile `ARG`s, the `#if USE_COMMERCIAL_LUCKYPENNY_LIBS` blocks, the four environment variables from the canonical catalogue and metadata, `docker-compose.yml:154-157`, the `NuGetAuditSuppress` entry, `DUAL_VERSIONING.md`, and the corresponding rows in the public environment-variable and Infisical guides.

### Operational changes
- IVSD-M006: add the authorization architecture test (every resolvable handler is the authorization decorator at its outermost layer, including a handler type added after the helper was written) and a zero-PII log assertion for the decorators' exception paths (log the request *type*, never serialize the request).

### Evidence-building steps
- IVSD-M005: record Mapperly (Apache-2.0) in the dependency register with the outbound-license decision, and run `dotnet run .ci/scripts/validate-dependency-license-policy.cs -- .` before the first Mapperly commit. Note the upstream generated-code question (E11) as reviewed-and-accepted or escalate to legal counsel.
- Capture a before/after allocation and latency measurement for one representative query handler. Mapperly's performance claim is plausible but the repository has no benchmark; do not repeat the claim in public documentation without one.

### Deferred scholarly/expert review
- None required. See Escalation Needed.

## Stakeholders

| Stakeholder | Interest | Effect of the decision |
|---|---|---|
| Community self-hosters (mosques, community centres, NGOs) | Run one image that is patched and needs no license key | Primary beneficiaries: the CVE mitigation stopgap and the edition ambiguity disappear. |
| Contributors | Build without commercial dependencies; understand mapping and dispatch by reading code | Generated mapping code, explicit handler injection, and readable decorator chains are more inspectable than reflection-configured libraries. Substantial churn during the ~1,400-file conversion, accepted by the Steward. |
| ISLAMU operators | One release pipeline, one security-scan target | Lose the theoretical option of a vendor-patched commercial build; gain a platform they fully own. |
| API consumers and tenants | Correct DTO data; no silent field drops; authorization enforced on every request | Compile-time mapping diagnostics and the authorization invariant test protect them. |
| Attendees and registrants (indirect) | Their PII is not copied to public DTOs by convention | Unmapped-member-as-error turns an implicit risk into an explicit decision. |
| Lucky Penny (vendor) | Commercial licensing | No relationship is created; the platform never adopted the commercial line in any distributed image. |

## I-VSD Principles And Domains

- **Trust (Amanah)** — Technical, Governance: the request backbone and the DTO boundary are stewardship-critical infrastructure; owning them in-repo is a stewardship choice.
- **Non-Harm (La Darar)** — Technical, Operational: removing a frozen, advisory-bearing dependency from every default deployment.
- **Truthfulness (Sidq)** — Governance, Strategic: documentation must not describe an edition switch that does not work as an operator would read it; license records must be accurate.
- **Promise-Keeping** — Strategic, Operational: "buildable and runnable without any commercial dependency" is a published promise; the migration is how it stays true without a second image.
- **Avoiding Gharar** — Strategic: one edition removes uncertainty about which binary a deployment runs.
- **Excellence (Ihsan)** — Technical, Evaluation: compile-time mapping diagnostics and architecture-tested decorator chains exceed the bare-minimum "it works at runtime" standard.
- **Rights of People** — Technical: explicit mapping decisions protect against convention-driven PII exposure.

## Common Overlooked Failures And Outcomes

Failures teams commonly miss in this kind of migration, and what they cost here:

1. **Migrating mapping one-to-one without turning on unmapped-member errors.** The main safety benefit is forfeited; you get faster AutoMapper with the same silent drops. Outcome if done right: every DTO field is a reviewed decision.
2. **Reproducing `ReverseMap` mechanically.** 45 reverse maps exist. A reverse map from DTO to entity is an *inbound* write path; some of those are probably unused or should not exist (entities should be constructed through domain factories). Treat each as a question, not a translation.
3. **Decorator nesting order drift.** If the performance decorator ends up outside the authorization decorator, timing logs include denied requests; if a handler registered through a different path (a manual `AddScoped` someone adds later) skips the composition helper, it silently runs undecorated with no authorization. The architecture test in IVSD-M006/M007 exists for exactly this: every resolvable handler must be the authorization decorator at its outermost layer.
4. **Swallowing notification handler exceptions to "keep the request succeeding".** `SettingCacheInvalidationHandler` and `PolicyChangedCacheInvalidationHandler` are correctness-relevant: a swallowed failure leaves stale authorization policy in cache. Failures must surface.
5. **Copying the textbook decorator set wholesale.** The reference design ships logging, validation, and transaction decorators. Two of the three conflict with this repository (IVSD-F007, IVSD-F008). The pattern is the lesson; the specific decorator list is not.
6. **Leaving dead plumbing behind.** Removing the libraries but keeping the `USE_COMMERCIAL_*` variables, Dockerfile args, and docs leaves operators with the same false switch. IVSD-M002 must be a deliberate deletion commit, not a leftover.
7. **Big-bang conversion in one PR.** 1,400 files in one review is unreviewable. Slice by feature folder, converting handlers and their consumers together so no slice leaves a controller half on `IMediator`; each slice builds and passes its Ring 1 tests.
8. **Reclassifying by folder name instead of behavior.** A handler in a `Queries` folder that writes (audit rows, cache warm, last-seen timestamps) is a command wearing a query's name. Classify by what the handler does; the CQS split is only useful if it is true.

Positive outcomes of doing this responsibly: one auditable image; no vendor-frozen CVE in the default path; mapping and dispatch code that a new contributor can read end-to-end; a shorter, truthful environment-variable catalogue; and a licensing story that matches what the platform actually ships.

## Validation Gaps

| Claim | Validation level | Gap |
|---|---|---|
| Mapperly removes the CVE-2026-32933 exposure | Design reasoning | True by construction (no reflection recursion), but must be confirmed by removing the `NuGetAuditSuppress` entry and observing a clean audit. |
| Mapperly is faster / allocates less | Not reviewed | No repository benchmark. Do not publish the claim without one. |
| The decorator composition preserves authorization semantics for every handler | Design reasoning | Requires the architecture test (IVSD-M006) and the composition helper (IVSD-M007); no handler may be registered outside it. |
| Every `Queries`-folder handler is read-only | Not reviewed | Classification pass required before the CQS split is trusted. |
| All 45 `ReverseMap` usages are needed | Not reviewed | Inventory during the profile slices. |
| Apache-2.0 generated-code obligations | Concern | Upstream discussion exists (E11); design reading is "no obligation on generated output", but this is a legal-counsel question if the Steward wants certainty. |

## Escalation Needed

- **Religious-legal:** none. This consultation concerns software licensing, security stewardship, and maintainability. It does not ask, and I-VSD cannot answer, whether commercial software licensing or any vendor's pricing is religiously permissible. If the Steward wants that question addressed, it belongs with qualified Sunni scholarly authority and is outside this report.
- **Legal counsel (optional):** Apache-2.0 compatibility with the AGPL-3.0-or-later outbound path and with CLA-backed alternative licensing is a well-established permissive-compatibility reading, and the generated-code question (E11) is low-risk, but neither is a legal opinion. Escalate only if the Steward wants written certainty before the first commit.

## Evidence Reviewed

| ID | Locator | What it establishes |
|---|---|---|
| E01 | `Directory.Packages.props:1-35`, `Directory.Build.props:44-60` | Edition selected by MSBuild property at build time; conditional package groups; preprocessor symbol. |
| E02 | `src/Explore.API/Dockerfile:12-48`, `src/Explore.Blazor/Dockerfile:12-45` | `ARG USE_COMMERCIAL_LUCKYPENNY_LIBS=false` forwarded to the publish. |
| E03 | `docs/internal/DUAL_VERSIONING.md` sections 1-6, 12 | Strategy intent, version matrix, CVE-2026-32933 disclosure, deferred migration note. |
| E04 | `docker-compose.yml:154-157, 481-483, 551-553`; `git grep` over `.ci/`, `.github/` (zero hits) | No distribution path passes the build arg; runtime variables include a boolean-valued version variable. |
| E05 | `src/Explore.API/Extensions/ConfigurationExtensions.cs:459-465`; `git grep "Licensing:LuckyPenny:Enabled"` (single hit: the mapping line) | Runtime `USE_COMMERCIAL_LUCKYPENNY` is mapped and never consumed. |
| E06 | `src/Explore.Application/ApplicationServicesRegistration.cs:110-137` | `MaxDepth(64)` mitigation; `#if USE_COMMERCIAL_LUCKYPENNY_LIBS` license-key injection. |
| E07 | `git grep` over `src/**/*.cs`, `tests/**/*.cs` on 2026-09-11 | AutoMapper: 11 profiles (1,151 LOC), ~590 `ForMember`, 297 `MapFrom`, 290 `Ignore`, 45 `ReverseMap`, 0 `ProjectTo`/`AfterMap`/`BeforeMap`/`ConvertUsing`/`IValueResolver`/`ITypeConverter`; ~400 `IMapper` sites; 21 test files; 43 test configuration/substitute sites; only `Explore.Application` references the package. |
| E08 | same grep wave | MediatR: 910 `IRequestHandler`, 848 request declarations, 947 `Send`/`Publish`, 1,406 files in Application, 181 in API, 3 in Infrastructure; 2 notifications, 3 notification handlers; 0 streams/pre-post-processors/exception handlers; 3 void requests (2 `IRequest<Unit>` + 1 `IRequest`; 6 `Unit` text hits); 112 test files, 137 substitutes. |
| E09 | `src/Explore.Application/Behaviors/AuthorizationBehavior.cs`, `PerformanceBehavior.cs`; registration order in `ApplicationServicesRegistration.cs:142-143` (Performance registered before Authorization) | Two pipeline behaviors; authorization is a generic behavior constrained on `IRequest<TResponse>`. |
| E10 | https://github.com/riok/mapperly/blob/main/LICENSE ; https://www.nuget.org/packages/Riok.Mapperly/ | Mapperly is Apache License 2.0 (not MIT as `DUAL_VERSIONING.md:289` states). |
| E11 | https://github.com/riok/mapperly/discussions/781 | Open community question on license status of generated output; no authoritative answer reviewed. |
| E12 | `islamic-value-sensitive-design/i-vsd-licensing-and-commercial-strategy.md` | Outbound model: AGPL-3.0-or-later plus CLA-backed alternative licensing; permissive dependencies preserve both paths. |
| E13 | User decision in session, 2026-09-11 | Steward rejects two-image distribution; AutoMapper-to-Mapperly decided. |
| E14 | Seemann and van Deursen, *Dependency Injection Principles, Practices, and Patterns* (Manning, 2019), Chapter 10; van Deursen, "Meanwhile... on the command side / query side of my architecture" (2011) | Design reference for CQS handler abstractions and decorator-based cross-cutting concerns. Recorded as a source-register entry; consulted for the functional idea only, no code reproduced. |
| E15 | `git grep` over `src/` on 2026-09-11 | 166 controllers inject `IMediator`; largest controllers make 25/17/17 `Send` calls; 152 handlers own transactions; 283 manual validator instantiations, 0 `IValidator<>` DI resolutions; ~312 query and ~331 command handler files; 5 MCP tool classes use the mediator; Scrutor is not referenced anywhere. |
| E16 | `docs/internal/QUICK_REFERENCE.md:14` | Rule #2: validators are manually instantiated, not injected as `IValidator<T>`. |
| E17 | User decision in session, 2026-09-11 | MediatR replaced by repository-native CQS handlers with generic decorators; zero libraries; Scrutor excluded; earlier namespace-swap dispatcher recommendation rejected. |

## Missing Evidence

- No performance or allocation benchmark for any handler; the Mapperly performance benefit is asserted upstream, not measured here.
- No inventory of which `ReverseMap` targets are actually consumed on a write path.
- No written legal confirmation of Apache-2.0 generated-code treatment.
- No test currently proves that authorization wraps a handler type added after the composition helper was written; the guarantee is inherited from MediatR's open-generic resolution and must be re-established as an architecture test over the decorator chain.
- The IP clean-room source register for the new handler abstractions and decorators does not yet exist; it must be created before implementation starts, recording the book and articles (E14) as design references and the repository's own usage as the functional specification, with no reproduced code.
- No classification pass has been done to confirm that every handler under a `Queries` folder is read-only; the command/query split must be verified by behavior, not folder name.
- No per-controller decision exists yet for the three largest controllers (25/17/17 handler dependencies) between action-level injection and splitting.

## Context Inventory

- Repository documentation: `docs/internal/DUAL_VERSIONING.md`, `docs/internal/legal/IP_GOVERNANCE.md` (referenced via rule), `docs/public/documentation/readme/configuration-and-operations/{environment-variables,infisical}.md`.
- Build and deploy: `Directory.Build.props`, `Directory.Packages.props`, `src/Explore.API/Dockerfile`, `src/Explore.Blazor/Dockerfile`, `docker-compose.yml`, `.ci/`, `.github/`.
- Code: `src/Explore.Application/{Profiles,Behaviors,Notifications}/`, `ApplicationServicesRegistration.cs`, `src/Explore.API/Extensions/ConfigurationExtensions.cs`, `src/Event.Setup.Core/Environment/CanonicalEnvironment{Catalogue,Metadata}.cs`.
- Tests: `tests/Event.Application.UnitTests/` (`#if USE_COMMERCIAL_LUCKYPENNY_LIBS` guards), MediatR/AutoMapper-referencing test files per E07/E08.
- Prior I-VSD reports: `i-vsd-licensing-and-commercial-strategy.md`, `i-vsd-records-adoption.md` (precedent for classification-first migration slicing).
- External: Mapperly license file and NuGet listing (E10, E11). No third-party source code was read.

## Review Lifecycle

| Date | Previous status | New status | Trigger | Evidence/replacement |
|---|---|---|---|---|
| 2026-09-11 | none | current | Steward decision to exit dual-versioning; AutoMapper-to-Mapperly decided; MediatR recommendation requested | E01-E13, working-tree 2026-09-11 |
| 2026-09-11 | current | current | Steward decided MediatR replacement: repository-native CQS handlers with generic decorators, zero libraries; namespace-swap dispatcher recommendation rejected; findings F007-F009 and mitigations M007-M010 added | E14-E17 |

Refresh triggers: an implementation plan is created for either migration (report moves to planning mode and gains a `Planning Handoff`); the Steward changes the validator convention (reopens IVSD-F007/M008); a new AutoMapper or MediatR advisory is published; the dependency-license policy check rejects Mapperly.
