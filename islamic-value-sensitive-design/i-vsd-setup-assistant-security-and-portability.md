<!-- ABOUTME: I-VSD consultancy report for the cross-platform ISLAMU Event Setup Assistant. -->
<!-- ABOUTME: Governs manifest authoring, relevant-only dotenv generation, optional secret entry, licensing, and release trust. -->

# I-VSD Consultancy Report: Setup Assistant Security And Portability

Last Updated: 2026-09-01

## Review Metadata

- Mode: planning
- Subject: ISLAMU Event Setup Assistant
- Workstream: setup-assistant-security-and-portability
- Report kind: consultancy-report
- Report status: current
- Disposition: plan-aligned
- Evidence cutoff: 2026-09-01
- Reviewed input revision: `sha256:752972e2b5e85e2bbb792a92618fabb6cffccdb6bccb278d222c4ed8ee6681d9`
- Supersedes: D2-3 plan-aligned report revision
  `sha256:edb31317a5c48168e35e97bdd60e61df1e1625f463efa93cd76101768baa9eb8`

## Scope

This report evaluates a new, small, cross-platform ISLAMU Event Setup
Assistant delivered through independently approved successors. Successor A
retains a Green headless Core and deterministic noninteractive CLI; its former
repository-native console wizard is superseded and must be removed. Successor
B retains the user-approved human-presentation/browser/desktop outcomes and
now owns a framework-neutral CommunityToolkit state model plus the sole human
terminal target backed by the separately named, minimally patched
`ISLAMU.Terminal.Gui` package. The product
is a shipped user-facing application under `src/`, not an internal contributor
tool.

The historical successor-B B0 triad, intake, binding, CTO review, and separate
B0 plan-review I-VSD report cover a restricted shared-Razor/static-browser
candidate. Every B0 artifact now marks that candidate superseded,
non-executable, non-authorizing, never user-approved, and replaced before any
probe. B0's binding and `IVSD-F047`–`IVSD-F053` remain historical provenance
only: they cannot be revived, refreshed, transferred, or imported as approval
or findings for B1.

It covers:

- B1's CommunityToolkit-only framework-neutral human-presentation state model,
  target-root-only DI, one injected messenger per operator session, explicit
  recipient activation/deactivation, strictly monotonic operation-generation
  fencing, single settlement, and value-free immutable messages;
- disabled Avalonia Browser/Desktop candidates and one required Terminal.Gui
  target whose dependency, secret, accessibility, and release evidence remain
  independently gated;
- Browser/Desktop target-owned disposable secret sessions outside shared
  ViewModels, messages, commands, bindings, validation, automation metadata,
  and shared DI, with the required Terminal.Gui target owning the sole human
  terminal secret path;
- six-stage isolated non-shipping graph probes that preserve false product,
  generated-capability, secret-entry, support, release, and shipping flags and
  grant no authority merely by succeeding;
- Windows, Linux, and macOS distribution;
- Linux portable archives plus `.deb`, `.rpm`, Arch package, AppImage, and
  optional Flatpak paths subject to license and release review;
- whole-instance `ConfigurationManifest` and tenant-scoped
  `TenantConfigurationPackage` authoring, validation, comparison, and export;
- a separate `.env` generation workflow for operators who do not use
  Infisical;
- progressive disclosure of deployment topology, providers, optional
  capabilities, environment variables, and secrets;
- a default web mode that never asks for secret values;
- an optional web mode that accepts secrets only after an explicit trust
  decision;
- client-side-only web processing with no secret-bearing request to ISLAMU or
  another server;
- the approved expanded plan for bounded YAML/directory composition, live
  target enrollment and write-only secret-provider binding, direct-transfer/
  live apply orchestration, and separately gated application-data/payment-
  operation migration;
- desktop file permissions, atomic writes, overwrite safety, memory,
  clipboard, logging, crash, update, and packaging risks;
- a complete per-target FOSS dependency gate covering direct, transitive,
  native, tooling, asset, and packaging obligations;
- typed instance and tenant legal-document configuration, including terms,
  privacy, cookie, accessibility, conduct, moderation, payment, and other
  accountable public texts;
- an offline legal-template library and safe Markdown editor;
- localized legal-document variants, lifecycle, preview, comparison,
  portability, target review, and publication handoff;
- one noninteractive `Event.SetupAssistant.Cli` executable with deterministic
  machine commands and one separate `Event.SetupAssistant.Terminal`
  executable using the same Core workflows;
- a future project skill that teaches external AI agents to use versioned CLI
  commands without embedding an AI model, provider, prompt runtime, or agent
  loop in the product;
- official hosting, source availability, provenance, branding, and the limits
  of trying to prevent malicious third-party hosting;
- a FOSS-only dependency philosophy that rejects commercial, proprietary, and
  source-available components while evaluating permissive and reciprocal
  licenses against each target’s public AGPL and alternative outbound paths;
- accessibility, localization, RTL, support, release, incident, and evaluation
  duties.

This is a product and security design report. It records successors A, B's
approved/disabled slices, and C through Phase 8 as Green. Successor D has only
the approved D2-1 package-free Wire vocabulary; no Domain aggregate, live
capability, server endpoint, provider write, profile, adapter, deployment, or
release exists. The current revalidation binds that contract-only Green over
the accepted Tier 1 live-enrollment threat/Red packet and preserves D2-2
onward as conditional under exact authorization, tenant, replay,
protected-profile, write-only secret, provider, HAL, CTO/MAD, and stage-review
gates. D1 owns one observed API Red; the Setup adapter Red occurs at SA-920's
public-contract checkpoint before adapter behavior.

## Claim Boundary

This report is provider-responsibility design reasoning under I-VSD. It is not:

- a fatwa, halal/haram decision, or Sharia certification;
- legal, privacy, security, accessibility, supply-chain, or license
  certification;
- proof that browser, operating-system, extension, endpoint-protection, or
  local-device compromise cannot expose a secret;
- proof that an online hosting origin is incapable of serving a modified
  malicious build;
- permission to place proprietary, commercial, source-available, unknown, or
  otherwise unapproved dependencies into the release, or to add reciprocal
  FOSS without target-specific compatibility review;
- proof that generated `.env` values are valid for an external provider;
- a substitute for threat modeling, code review, reproducible-build evidence,
  penetration testing, accessibility testing, or qualified legal review.

The strongest truthful web claim is:

> The identified, reviewed release is designed to process entered secrets in
> browser memory and generate the file locally without transmitting secret
> values. Users of the online-hosted build still trust the hosting origin to
> deliver that reviewed code on every load.

The product must never claim “ISLAMU technically cannot obtain your secrets”
for an online-hosted build. The origin controls the HTML, JavaScript, WebAssembly
modules, headers, and future deployment. Transparency, public source,
reproducible artifacts, strict browser policy, testing, and legal commitments
can provide evidence and accountability, but do not erase that technical trust
boundary.

## Context And Current Repository Facts

1. The repository accepts Infisical or `.env` as secret sources. Secrets must
   never be embedded in AppHost, appsettings, source, tests, or manifest files.
2. `.env.example` is currently a large operator-facing template of 618 lines.
   `docker-compose.yml` is 985 lines and contains substantial conditional
   deployment configuration.
3. `SecretDefinitionRegistry` is the current Domain source of truth for
   recognized secret-backed settings, allowed scopes, sources, and default
   environment-variable names.
4. `ConfigurationManifest` intentionally excludes secrets, PII, provider
   credentials, and operational state.
5. The current manifest schema generator references all of
   `Explore.Application`; that dependency is too broad for a small offline
   desktop/browser client.
6. No Avalonia project or package is currently present.
7. The repository centrally pins package versions, commits package lock files,
   and requires dependency-license review for every shipped graph.
8. Avalonia’s official documentation states that the framework is MIT
   licensed, while professional tooling has separate licensing.
9. Avalonia Browser publishes a static site containing HTML, WebAssembly,
   runtime files, and assets; no server-side .NET code is required.
10. Avalonia supports Windows, macOS, Linux, and browser targets, but browser
    file-system capabilities and desktop security controls differ materially.
11. The user selected two web experiences:
    - no-secret input, selected by default;
    - optional secret input for users who explicitly trust the official host.
12. The user requires relevant-only output: required secret keys for selected
    capabilities remain empty in no-secret mode; irrelevant variables and
    defaulted settings are omitted.
13. The active ConfigurationManifest worktree now contains typed
    instance/tenant legal documents, constrained rendering, an anonymous
    last-published API, and role-labelled Terms/Privacy pages. The Setup
    Assistant still has no legal editor or approved template library.
14. Current public footer contracts already distinguish tenant directory
    operator links from instance platform-operator links; legal text must
    preserve that role separation.
15. Official Terminal.Gui `2.4.17` remains unusable as-published because its
    dependency graph contains the provenance-blocked
    `TextMateSharp.Grammars` corpus. On 2026-09-01 the Project Steward
    authorized a temporary, separately named `ISLAMU.Terminal.Gui`
    `2.4.17-islamu.1` package based exactly on official commit
    `d0a0ed9b150d3fc8aacf4ab07b7f7d91264fe6d6`, with only that grammar/editor
    integration removed and full MIT attribution retained.
16. Existing repository CLIs use deterministic commands, bounded text output,
    stable usage failures, and nonzero exit codes.
17. The user revised the dependency policy from literal MIT-only to
    philosophy-compatible FOSS: no commercial/proprietary dependency, while
    GPL/AGPL and other free licenses may be considered.
18. The Setup Assistant browser source remains open and auditable. Only
    generated `wwwroot`, build, publish, and release artifacts are ignored.
19. The user closed ConfigurationManifest for archival on 2026-08-30. Its
    frozen current baseline contains v1alpha2 contracts, a closed 21-entry
    portability registry, typed legal documents, constrained legal Markdown,
    protected import sessions, semantic preview, and scope-safe
    HTTP/HAL/BFF/generated-client contracts. Retired later phases are not
    implementation evidence.
20. `Event.Wire.Contracts` is an existing package-free inner project for
    versioned codecs and values shared across server and isolated clients.
21. The sanitized SA-120 dependency handoff blocks Avalonia `12.1.1` Desktop
    and Browser runtime graphs because native component/license mapping remains
    unresolved and exact publish exclusion of `Avalonia.Remote.Protocol` is
    unproved. Successor A pins/restores no Avalonia package and may create only
    package-free disabled presentation contract shells.
22. Successor B retains every user-approved human-presentation/browser/desktop
    and accessibility outcome. Its decomposition is a CommunityToolkit-only
    ViewModel/message owner and one Terminal.Gui event/command target; machine
    CLI/Core remain outside MVVM and all custom/BCL terminal fallbacks are
    forbidden. Every exact graph still requires
    provenance-complete evidence plus fresh I-VSD, CTO, user, dependency,
    security, and accessibility approval before activation.
23. The expanded plan keeps YAML/directory inputs as bounded source adapters
    that converge on canonical v1alpha2 JSON; they do not become wire formats.
24. Live target, tenant, HAL, provider, import, transfer, and transaction
    authority remains server-side. Setup receives short-lived scoped authority
    and value-free state only.
25. Repository privacy erasure is authority-first, replayable, fenced against
    resurrection, payload-free in audit/receipts, and retention-governed. Data
    migration must preserve rather than replace that authority.
26. The current payment baseline is `OrganizerDirect`; merchant recipient and
    currency are pinned, partial refunds have deterministic line allocation,
    and provider acceptance is not terminal until reconciliation.
27. The corrected CTO and user approvals bind successor A's exact BCL-only
    revision, which is now Green. They grant no authority to the re-baselined
    successor-B architecture or any later Tier 0/1/2 successor; every later
    gate remains independent.
28. The successor-B B0 Razor/browser triad, intake, CTO review, binding, and
    separate plan-review I-VSD report are explicitly superseded,
    non-executable, non-authorizing, and historical. B0 received no user
    approval and ran no probe; none of its conditional decisions or IDs can be
    reused for B1.
29. The final-Red B1 binding
    `setup-assistant-security-and-portability-b1-final-red-20260831` binds the
    exact current plan, final tasks, context, clean-room/dependency and probe
    evidence, intake and post-probe verdicts, the second `Changes required` CTO
    review, three final test files, and unchanged product/central dependency
    preimages reviewed here.
30. SA-510 still approves CommunityToolkit.Mvvm `8.4.2` only for shared
    presentation and Microsoft DI `10.0.10` plus Abstractions `10.0.10` only
    for executable roots. The final SA-518 Red retains 18/18 owner-local
    presentation failures and 14 architecture tests with only
    `SA518-GRAPH-RATCHET` failing.
31. Avalonia shared, Browser, and Desktop `12.1.1` remain
    `ApprovedDisabled`, absent, unresolvable, and unsupported. The prior
    official Terminal.Gui graph remains blocked, but Phase 5R may replace it
    only with the exact Steward-authorized internal patched package after its
    final closure, SBOM, notices, security, and anti-reentry gates pass.
32. The Phase 8 binding
    `setup-assistant-security-and-portability-phase8-20260831` matches its
    expected SHA-256 and every named planning, Core, test, lock, and central-pin
    preimage was independently recomputed without drift.
33. The isolated YamlDotNet `18.1.0` probe resolves one direct node with no
    transitives and exercises only `YamlStream`, `YamlMappingNode`, and
    `YamlScalarNode` against bounded in-memory syntax. It is absent from product
    projects, locks, solution, CI, capabilities, support, release, and shipping.
34. Phase 8 remains unimplemented. No composition owner, parser reference,
    directory adapter, schema, generated artifact, or scale profile exists in
    the bound Core/test preimage; SA-810 is therefore the next intentional Red.
35. The corrected Phase 8 binding
    `setup-assistant-security-and-portability-phase8-corrected-20260901`
    matches its expected SHA-256 and every named planning, evidence, review,
    report, Core, test, lock, and central-pin preimage was recomputed without
    drift.
36. Corrected C1 Red owns only three test/fixture paths and a future public Core
    seam: `SetupCompositionCompiler`, `SetupCompositionLimits`, typed source/
    result/failure contracts, and an exact directory snapshot/commit barrier.
    Its fourteen matrices, numeric ceilings, deterministic barriers, and Phase
    8 Worst Break are now executable requirements rather than broad topics.
37. C1 Green is Core-only syntax-tree and canonical-parity closure. It excludes
    presentation/source pickers and C2 scale; Linux directory semantics require
    real-filesystem evidence, while Windows directory input remains disabled
    unless a Windows runner proves equivalent handle-safe behavior.
38. C2 begins only after C1 Green, measures named profiles, and keeps the
    canonical SA-810 defaults unchanged. Every slice owns exact paths,
    verification/failure disposition, explicit-path staging, unrelated-state
    preservation, material-override recording, and post-commit file/hash checks;
    this report authorizes no commit execution.
39. The final Phase 8 binding
    `setup-assistant-security-and-portability-phase8-final-20260901` matches its
    expected SHA-256 and all fourteen named artifact/product preimages were
    recomputed without drift. Technical matrices, ceilings, public seams,
    platform rules, Worst Break, and C1/C2 split are unchanged.
40. Final governance narrows C1 Red to exactly two new test files; makes the
    existing central YamlDotNet pin read-only for C1 Green; pins exact Green
    Core/lock/docs/change-fragment paths and matching `Change-Id`; makes every
    C2 path a new file; and binds literal descriptions, changelog/trailers,
    `Message override: Not overridden`, mixed-author blockers, and exact
    post-commit file/hash checks without authorizing a commit.
41. Phase 8 is now Green. Generated profile verification passed 4/4, focused
    scale tests passed 4/4, Setup Core architecture passed 10/10, full Setup
    Core passed 65/65, and the Release build completed with zero errors.
42. The measured `small`, `medium`, `large`, and exact 4,096-entry `ceiling`
    profiles remain bound to canonical defaults, target Wire acceptance,
    cancellation, host/runtime/process limits, canonical size/SHA-256, and
    evidence-digest admission. `expanded` is known-disabled.
43. Configuration import already supplies a repository-native security pattern
    for successor D: one-time capability issuance, digest-only persistence,
    fixed-time comparison, exact target binding, expiry/cancellation/
    consumption, header-only transport, no token in HAL hrefs, and bounded
    ProblemDetails.
44. Normal bearer identity, `PlatformIdentityPrincipalExtensions`, server
    tenant resolution, resource authorization, idempotency middleware, and HAL
    filtering remain the only existing API authority chain. Setup adds no
    identity parser or browser-trusted authority header.
45. `SecretBinding` persists provider coordinates but no values;
    `ISecretResolver` is read-only and dispatches to exactly one selected
    source. No approved write-only Setup provider API exists, so D1 must test
    the future boundary and D2 must introduce a purpose-specific write seam
    without turning resolution into readback.
46. The Phase 9 intake binds normal bearer-to-purpose-capability enrollment,
    route-derived tenant authority, SHA-256-only capability persistence,
    terminal revocation, monotonic generation, UUIDv7 idempotency
    fingerprinting, protected-handle-only profiles, write-only allowlisted
    bindings, value-free readiness, generic failures, HAL-only actions, and
    offline continuity.
47. Corrected D1 SA-910 owns exactly one absent API integration test and no
    production path. The absent Setup adapter test moves to SA-920 after a
    public/generated live contract exists and before adapter behavior. Fresh
    CTO/MAD and exact-revision user approval remain mandatory before D1.
48. Independent review found that generic response idempotency could persist/
    replay a one-time capability, the first packet allowed capability identity
    ambiguity and lacked a revocation/effect race fence, and current request
    fingerprinting omits Setup enrollment generation. The corrected intake
    uses an Application-owned value-free issuance claim, current bearer/actor
    authorization on every action, exact generation-bound fingerprints, and
    transactional plus dispatch revocation fences.
49. Capability shape is now exact: 32 cryptographic bytes, canonical unpadded
    43-character Base64url, SHA-256-only persistence, bounded parsing, and
    fixed-time comparison. Every invalid branch is byte-identical RFC 7807.
50. Independent review also confirmed a relevant current defect:
    `SecretResolver` may synthesize a registry default after an explicit
    persisted source mismatch. D2 must make explicit mismatch `Invalid` and
    prove zero fallback calls before SA-930 can be Green.
51. D1 created exactly
    `SetupLiveAuthoritySecurityTests.cs`. The API integration project compiles
    with zero D1 warnings; 10/10 tests are discovered and all ten fail only for
    the absent exact controller/route family before deeper assertions.
52. Corrected D1 uses runtime cryptographic canaries, real HTTP/tenant/EF
    seams, deterministic time and exact structured-log milestone barriers, and
    capture-all observability.
    It adds no product, package, lock, migration, generated client, adapter,
    staging, commit, release, or shipping authority.
53. D2-0 makes every test reset/reseed database and tenant/actor state, time,
    authorization, and telemetry; fixes single request ownership; and narrows
    D1 claims so writer and resolver/source call counts, provider success, and
    final dispatch ordering begin only after real static D2 seams exist.
54. D2-0b requires the exact milestone event ID/name plus operation/milestone,
    describes explicit source mismatch as HTTP response-shape/value-exclusion
    evidence only, and records D2-1 through D2-11 plus generator-produced
    migrations/snapshots for PostgreSQL, MariaDB, MySQL, SQLite, and SQL Server
    in plan/tasks/context.
55. D2-0c corrects operational entry points only: tasks/context identify
    D2-0b technical acceptance, require final review and explicit `approve`,
    direct the next product action to D2-1 Wire contract Red only, prohibit
    later-layer work early, and keep capability flags false through D2-11.
56. D2-0d makes that resume path singular: all old session-progress,
    quick-resume, and handoff directions are explicitly historical,
    superseded, and non-executable; task/context status now names D2-0d final
    review and future explicit `approve`.
57. D2-0e corrects the sole remaining current contradiction: successor D now
    names D2-0e final review, absent product/capability ownership, required
    explicit `approve`, and D2-1 Wire Red as the sole first product slice.
58. D2-0f corrects the last stale authoritative planning-status sentence so
    every current surface names D2-0f final review and preserves the same
    explicit-approval and Wire-first boundary.
59. The user explicitly approved the reviewed D2 sequence. D2-1 alone is now
    Green: package-free Wire metadata, immutable strict data, closed enums,
    canonical redacted capability syntax, and source-generated JSON passed
    Wire/Core/Architecture Red then Green without creating live authority.
60. Initial D2-1 review found default capability serialization and permissive
    generated JSON. Corrected D2-1 makes capability release method-only,
    challenge/scope parsing canonical, every enum string-only, metadata closed,
    and tests execute the shipped source-generated context.
61. Corrected D2-1 review approved at 100/100. D2-2 now has a seven-test
    attributable Domain Red for absent enrollment, issuance-claim, and
    secret-operation owners, with no Domain product or outer-layer behavior.
62. Initial D2-2 review required stronger dispatch, replay, mutation,
    temporal, surface, audit, and concurrency closure. The corrected 14-test
    Red closes those findings, removes prospective Domain `Revision`, uses
    Persistence-managed `ConcurrencyStamp` only, and fails 5/3/6 on the exact
    three absent owners.
63. Second D2-2 review required complete accepted terminal results, actor/user
    audit separation, and value-free exception diagnostics. The final Red
    closes all three: exact named terminal snapshots, actor-only lineage with
    user audit fields left for later authenticated ownership, and runtime
    canary exclusion across exception chains.
64. Final D2-2 Red review approved at 100/100. Three Domain owners now
    implement the exact enrollment, issuance-claim, and secret-operation
    contract; focused and full Domain suites are Green. Green execution also
    corrected inherited-framework reflection false positives without
    weakening the closed Setup live public surface.
65. D2-2 Domain Green review approved at 100/100. D2-3 now has a seven-test
    attributable Application contract Red for one-way writer, versioned
    commitment, per-enrollment coordination, and exact dispatch-barrier ports,
    with no handler, Persistence, provider, API, or activation behavior.
66. Initial D2-3 review found unreachable closure contradictions, missing
    constructor/lifetime/diagnostic invariants, and overstated race ownership.
    Corrected Red closes executable constructor/metadata boundaries, defines
    request bytes as borrowed, and assigns all executable ordering/race/
    call-count/cancellation/lease proof to D2-7.
67. The corrected D2-3 review found inherited public-surface escape hatches,
    unenforced borrowed backing identity, and narrow UUID/diagnostic negatives.
    Final Red forbids inherited contract surfaces, proves exact backing-segment
    aliasing, rejects UUIDv1/v4/v6/v8, and denies transformed byte diagnostics.
68. The final D2-3 review found one remaining complete-member bypass through
    special-name interface methods and request/result fields/events. Complete
    Red closes every public member kind and scans public field types.
69. The complete D2-3 review found CLR-semantic bypasses in static/default/
    generic ports, indexers/explicit interfaces, enum numeric values,
    attributes, ownership, and forwarding. Exhaustive Red closes those
    modifiers, private metadata, values, compiler metadata, and ownership.
70. The exhaustive D2-3 review found private interface/metadata/enum behavior,
    incomplete implementation flags, permissive compiler-attribute provenance,
    and partial assembly/module inventory. Closed Red uses exact SDK witnesses,
    all-visibility closure, and exact assembly/module/prefix baselines.
71. The closed D2-3 review found residual Property/Param/Field table flags,
    constructor return metadata, enum storage metadata, and textual attribute
    identity. Approval-ready Red compares complete witness metadata and
    structural attribute identities/payloads.
72. The approval-ready D2-3 review found assembly/module attributes remained
    full-name based and manifest-only. Final approvable Red binds exact BCL
    attribute identities/payloads, one manifest module, and owner placement.

D2-0d governance evidence is:

- **E097:** D2-0d binding
  `setup-assistant-security-and-portability-phase9-d2-0d-final-20260901`,
  `sha256:9c935d1abb73002656bf4b2407c5700d43f316acf94e2dc98b7c3db1e1b886cb`.
- **E098:** D2-0d correction/approval/evidence:
  correction
  `sha256:67d0263c58a06325b4ecebdcd6122f7d38188e707c35b1ee59f611bbf3a014f2`,
  approval
  `sha256:9028de169d44fc54a8e2e096db6fca87d383ce66cbae5a29b20ba93e0708a3f1`,
  and evidence
  `sha256:8e63503c7f5f749f80495d83837cc334865e91d98f371cfec9b72c871fd9d089`.
- **E099:** D2-0c Changes-required review plus corrected canonical ledgers:
  review
  `sha256:e9f446c24b4ba9412e0ad7c133c3ad67855c2b17932819435e1ea293f9ef0282`,
  tasks
  `sha256:8ed36d8c25e32523423f13fd20c0497c7fef19070dbc2fddc00f01d6adff38e1`,
  context
  `sha256:59b65971ad93921afd3568e7b348a8c0705730f6594557e01b32311eb24133e7`,
  and unchanged plan
  `sha256:cbe493c4540ad230c6ee02665305724f8e28dcd5fe009b8984c981e78e158403`.

D2-0d changes no executable/product fact and introduces no new finding. It
preserves IVSD-F038/M038, F039/M039, and F040/M040 while removing contradictory
authorization-routing prose.

D2-0e governance evidence is:

- **E100:** D2-0e binding
  `setup-assistant-security-and-portability-phase9-d2-0e-final-20260901`,
  `sha256:15aed34796930152e3e2dee19ab042b84f978a4d37b743cf9d0bae52bc718c9c`.
- **E101:** D2-0e correction/approval/evidence:
  correction
  `sha256:5340ec315f6fcf38bab2ba1cc87c659fc2150fe291451f7f3d7771baa84c49a4`,
  approval
  `sha256:d19b17b242ba5532f6fb61ea49823a8fcbbeb5819296e86df232e35b1ee83c4a`,
  and evidence
  `sha256:308f4d25dcbff9057cf16e549d88b77b4edf5b6fbb71be18e178c2c60a0401c7`.
- **E102:** D2-0d Changes-required review plus corrected canonical ledgers:
  review
  `sha256:36c00e1ed7db2aa5acfd0f185545ae84f8487d5fdd03ec92b67417a98da59b88`,
  tasks
  `sha256:64e372be271d52389bfb50fadbf1f1b19d3b95f20cad163ac39cc4804b1ba2ea`,
  context
  `sha256:f4c0ce97848eb4bbb2c9c9c579fb67bffc6a39179072b38a3f522d6a75843fe2`,
  and unchanged plan
  `sha256:cbe493c4540ad230c6ee02665305724f8e28dcd5fe009b8984c981e78e158403`.

D2-0e changes no executable/product fact and introduces no new finding. It
preserves IVSD-F038/M038, F039/M039, and F040/M040 while making every current
ownership/status/resume surface coherent.

D2-0f governance evidence is:

- **E103:** D2-0f binding
  `setup-assistant-security-and-portability-phase9-d2-0f-final-20260901`,
  `sha256:9bdd703dd91801b436e602d9418b6d7d93a325cc8f35f037c6ed3620e077de73`.
- **E104:** D2-0f correction/approval/evidence:
  correction
  `sha256:7386fb21ec2e36615247f27dbcbb2e8c800faa93b23142bf4ae4f4a03396f30c`,
  approval
  `sha256:0ae75140cb02dd96da229e526d3bbb5fbf6cc837ca2b77e7c827dcef0505926e`,
  and evidence
  `sha256:e7d416f403feb432de8b7314031f1258a11b00c284f2f4bd9137e64d67563297`.
- **E105:** D2-0e Changes-required review plus corrected canonical ledgers:
  review
  `sha256:2d563b00938b614323a49c5d7cc2786e1f0651d2e05617198badca9ddeae5028`,
  tasks
  `sha256:1fa158bf1098624b87391c06c1568802c15ab299afad44716839ec089ec2a4c8`,
  context
  `sha256:56d2f1bf23b1674afb451f698bb966d82d4033fd8a6289ecf79276189fb970c3`,
  and unchanged plan
  `sha256:cbe493c4540ad230c6ee02665305724f8e28dcd5fe009b8984c981e78e158403`.

D2-0f changes no executable/product fact and introduces no new finding. It
preserves IVSD-F038/M038, F039/M039, and F040/M040 while making every current
planning/ownership/status/resume surface coherent.

D2-1 implementation evidence is:

- **E106:** D2-1 review binding
  `setup-assistant-security-and-portability-phase9-d2-1-green-20260901`,
  `sha256:cb2292e484bf137c5b6f4d963733fa282cc54100dc50ec06f226ca2b7261c3e9`.
- **E107:** explicit product approval and Red/Green evidence:
  approval
  `sha256:a5d01cb1d91a071c7885316edb3ec27f244d8f36e44687ebe6d4344dbeb6b97e`
  and evidence
  `sha256:cc73a3a6d36099f4593f08d6a90855e93174d69c0125ea13369a24a15f60e106`.
- **E108:** canonical ledgers:
  tasks
  `sha256:c5c44977a61e1b32821cdbe711382f5e05ddb5dbbfc9eb993c76d8792a4f8054`,
  context
  `sha256:55be28a8e76bd1d321ed2bc9fab9a595064b19f34550d0611d7d9323589c2873`,
  and unchanged plan
  `sha256:cbe493c4540ad230c6ee02665305724f8e28dcd5fe009b8984c981e78e158403`.

D2-1 adds only BCL/package-free transport syntax and immutable public data.
The capability value is canonical/redacted and absent from JSON response
contracts. Public outputs exclude authority, provider-coordinate, value, and
P9-008 registration-provider surfaces. No server/provider behavior exists.
This strengthens IVSD-F038/M038, F039/M039, and F040/M040 without adding a
finding.

Corrected D2-1 evidence is:

- **E109:** corrected D2-1 binding
  `setup-assistant-security-and-portability-phase9-d2-1-corrected-green-20260901`,
  `sha256:1fa56ed5cf964151826cfd68a712657cb963ff29f5c1f4d10291ccaaccd6c2f7`.
- **E110:** initial Changes-required review and corrected Red/Green evidence:
  review
  `sha256:61976036324b4f436ec3fd6271c18ae88de4ae2b14dbb1e5da6129f1873014ba`
  and evidence
  `sha256:fb4334dec215ac0fb03b23e6d6117772148dae2b642ff22c4ccde7ca2b439936`.
- **E111:** corrected canonical ledgers:
  tasks
  `sha256:0f1be477a9a9eb84e792c1f632443a5d2695f39c30aec859db1d77afd5f91fd0`,
  context
  `sha256:c1809f4170f30735d01463c14d9622ed82dd076b9e83c405f2dc57aa113140fb`,
  and unchanged plan
  `sha256:cbe493c4540ad230c6ee02665305724f8e28dcd5fe009b8984c981e78e158403`.

The correction moves lexical validity into the Wire boundary rather than
deferring it to Domain. It prevents default JSON/public-property capability
disclosure; rejects invalid challenge/scope/enum forms through the shipped
context; and closes metadata aliases. No authority or provider behavior was
added. IVSD-F038/M038, F039/M039, and F040/M040 remain sufficient.

D2-2 Red evidence is:

- **E112:** D2-2 Red binding
  `setup-assistant-security-and-portability-phase9-d2-2-red-20260901`,
  `sha256:08cd9144a496522f27da007dd611177eae8a87798f662ea30302d53b6a03556a`.
- **E113:** corrected D2-1 approval and D2-2 Red evidence:
  review
  `sha256:cd9b41d98111914e4aba5a392ebe0fe9d981c52f4d2d376a75eae74ca68ca72c`
  and evidence
  `sha256:44270c031d914201a48243cb68a945dfb77af27a8fda42bc6e13e0ad33cf2a12`.
- **E114:** D2-2 Red and canonical ledgers:
  test
  `sha256:3c08bb1b93816f6ac5b2f1010c6c6215de71d063753e95508d2632f23d0d81b0`,
  tasks
  `sha256:d0dbb5ec0897d6616047dab9925a0807000916a0bad100570c10cf605dee7dfa`,
  context
  `sha256:5450da31bd12c7e841d6e394d1f79e5476b6b3add65ac7fc11e84ff396b64899`,
  and unchanged plan
  `sha256:cbe493c4540ad230c6ee02665305724f8e28dcd5fe009b8984c981e78e158403`.

The Red freezes tenant/actor/generation lineage, monotonic rotation, terminal
revocation/expiry, value-free issuance matching, accepted-to-terminal secret
operation behavior, dispatch fencing, and digest/commitment-only evidence.
Seven tests fail only for absent exact Domain owners. No authority or provider
behavior exists yet. IVSD-F038/M038, F039/M039, and F040/M040 remain sufficient.

Corrected D2-2 Red evidence is:

- **E115:** corrected D2-2 Red binding
  `setup-assistant-security-and-portability-phase9-d2-2-corrected-red-20260901`,
  `sha256:1bb0b910eee24795f8bed638d2f39613107b87d02647a7235dce855185cdce0f`.
- **E116:** initial changes-required review and corrected evidence:
  review
  `sha256:9176183c69a1b595182acbfdb58a603d4a478be6b3622dfaa06476223a010c05`
  and evidence
  `sha256:e3f10d42bceed7d92fe9bffb12b4a1739b6a8be7ebcfd1d80d64f7bb395d912f`.
- **E117:** corrected Red and canonical ledgers:
  test
  `sha256:c37c566a4262763c96b609e1f43b806d1dfca706c39cf58a2f8602720f13a63d`,
  tasks
  `sha256:58eb6d7352d36f4238558aa7e362c0144b450e6ae265677a6963c9a42349c3cb`,
  context
  `sha256:816990edb12c4b989dc624dfe0043479913a1e1de036f2312ce44e096b4e2af7`,
  and unchanged plan
  `sha256:cbe493c4540ad230c6ee02665305724f8e28dcd5fe009b8984c981e78e158403`.

The corrected Red closes cross-bound dispatch, exact replay/commitment
conflicts, rejected-mutation atomicity, UUIDv7/UTC/chronology/overflow,
terminal matrices, exact value-free public surfaces, audit ownership, and the
single Persistence-managed concurrency token. Fourteen tests compile and fail
5/3/6 on the exact absent owners. No authority or provider behavior exists
yet. IVSD-F038/M038, F039/M039, and F040/M040 remain sufficient.

Final D2-2 Red evidence is:

- **E118:** final D2-2 Red binding
  `setup-assistant-security-and-portability-phase9-d2-2-final-red-20260901`,
  `sha256:90fc0b3be5ff307ffcc6466abb4b25d53711db1d344e3bd660f3812d4d6d34e7`.
- **E119:** second changes-required review and final evidence:
  review
  `sha256:225120ba60178747284f57d2d837736bf381df8b4f77c7590920a0475b4fc114`
  and evidence
  `sha256:dff83e5860c548a0428413a56dc7399b4e59d0326c46deb3211a25620e8654d0`.
- **E120:** final Red and canonical ledgers:
  test
  `sha256:6e2146f7520c2466a93a8285fdc55b6d24d94871ce26094e3051a887c7a8eaea`,
  tasks
  `sha256:95b3c6ec6099b867adbc958e8ad1c8211773264a41ecc6057b7bf0d6e2f83280`,
  context
  `sha256:6db2f721f8c9f537a121907e78eca4c3147a2772c30ce59c0c192b874c3a9b83`,
  and unchanged plan
  `sha256:cbe493c4540ad230c6ee02665305724f8e28dcd5fe009b8984c981e78e158403`.

The final Red requires exact accepted terminal results, separates actor lineage
from later authenticated-user audit assignment, and excludes runtime evidence
canaries from exception chains. Fourteen tests still compile and fail 5/3/6
on exact absent owners. No authority or provider behavior exists yet.
IVSD-F038/M038, F039/M039, and F040/M040 remain sufficient.

D2-2 Domain Green evidence is:

- **E121:** D2-2 Green binding
  `setup-assistant-security-and-portability-phase9-d2-2-green-20260901`,
  `sha256:a34e6535fc6d8cbd599c618f26b8c7da002c37096ca19ba9661bcd4301122a02`.
- **E122:** final Red approval and Green evidence:
  review
  `sha256:7723a3bd5663ba19a65f5c03f38b1a3c9df336692baaff7ca67ab04ca0725bd4`
  and evidence
  `sha256:2dfaad6ee5b78725c98f7009cf865d880695418a16bb72918e560d1e432fe01c`.
- **E123:** Domain owners, final test, and ledgers:
  enrollment
  `sha256:c5b7b84cee92a35efe57e5834ff472853d5bcea8cd294bfdaaf4c2b36c313cde`,
  claim
  `sha256:11c9397d7fddc2972de7e4c74a403cf78098f9d87000ab2a54368bd899c89a9f`,
  operation
  `sha256:7ed7ab4340f218aa079c442e22ec11d2cb243264126aa76d44e338e71d5daf39`,
  test
  `sha256:ab3d630e7d4dad93f45a806aa557b9783590dced76fdf0433d111842a92092d3`,
  tasks
  `sha256:690b372347bcd8d08cc23e075f479c7e0f48565d88bb6bd936547f95f0af2f1e`,
  context
  `sha256:bc15d40ed289f56b2f1bbd50413405b78fa698befdf698e2511dd2f5b7562122`,
  and unchanged plan
  `sha256:cbe493c4540ad230c6ee02665305724f8e28dcd5fe009b8984c981e78e158403`.

Domain Green is 14/14 focused and 1089/1089 full Domain, with 17/17 relevant
architecture guards and a zero-warning Domain build. The full architecture
suite has one D2-2-unrelated Phase 5/8 SetupAssistant lock-ratchet mismatch,
bound and disclosed in E122. No outer owner or active capability exists.
IVSD-F038/M038, F039/M039, and F040/M040 remain sufficient.

D2-3 Application Red evidence is:

- **E124:** D2-3 Red binding
  `setup-assistant-security-and-portability-phase9-d2-3-red-20260901`,
  `sha256:bea912c635f0238a5bee2abc20f7b37f2123780d20fb816728580056b407e3a6`.
- **E125:** D2-2 Green approval and D2-3 Red evidence:
  review
  `sha256:bf46d5c7fe2e3a8c56d8e0ffb0dc72375e38b0bb2b4eb38d1508adcf97479db2`
  and evidence
  `sha256:b4be470d195860a3359868c11feff96dd4e0d579f633349b20a01a23119d6e1f`.
- **E126:** D2-3 Red and canonical ledgers:
  test
  `sha256:c9f5b901896d35ef29bac9e9df70e48e9519d1c301bf1627f206fce3f30d7ccc`,
  tasks
  `sha256:c2416986bf812fb90351765ddee9cac56c90fb2c93908cd43c2154d965d2868b`,
  context
  `sha256:4336d2f811b020c6c8c3412560228045a3cc6f155b55ed75607650e3fa57aabe`,
  and unchanged plan
  `sha256:cbe493c4540ad230c6ee02665305724f8e28dcd5fe009b8984c981e78e158403`.

The Red freezes cancellation-aware one-way writer, versioned HMAC commitment,
per-enrollment-generation lock, and payload-free dispatch-barrier contracts.
Seven tests compile and fail only for exact absent Application owners. Actual
dispatch, HMAC, distributed locking, races, call counts, handlers, and provider
effects remain later owners. IVSD-F038/M038, F039/M039, and F040/M040 remain
sufficient.

Corrected D2-3 Application Red evidence is:

- **E127:** corrected D2-3 Red binding
  `setup-assistant-security-and-portability-phase9-d2-3-corrected-red-20260901`,
  `sha256:0dbc26bc5628e6e174e431e3a3e912239fa661f60666e178797bdbbf59079db0`.
- **E128:** initial changes-required review and corrected evidence:
  review
  `sha256:eb4fdef0afae5633fdf0451327895df516f2f894f3f0c0473171543a641ee9b9`
  and evidence
  `sha256:16bb842e3e298058733682e50c294c3bb2f367058b315c6e208aa22a691d55ed`.
- **E129:** corrected Red and canonical ledgers:
  test
  `sha256:801d964556d3e3cc0ca11500823d091b9c389a539baf47350b0ffb37c7637dcc`,
  tasks
  `sha256:912499ed4ae255a6c671f58c80329c65f83a6ca7525fa95cd7e4ef4b2fdd2142`,
  context
  `sha256:6038e66e124cacc6a2a6fb0a9dd51341d08d426e5794c30b263689b659db2b7f`,
  and corrected plan
  `sha256:0fc0037f1ac0cca143cb832e42c59302937901d7a1464b39f00162e968d8f06e`.

Corrected Red is Green-satisfiable: constructor boundaries validate UUIDv7,
positive versions, closed binding keys, bounded borrowed bytes, and commitment
syntax; runtime diagnostics are value-free; metadata is exact; lock identity
cannot split linearization; and D2-7 truthfully retains executable writer/race
proof. Seven tests retain exact absent-owner attribution. IVSD-F038/M038,
F039/M039, and F040/M040 remain sufficient.

Final D2-3 Application Red evidence is:

- **E130:** final D2-3 Red binding
  `setup-assistant-security-and-portability-phase9-d2-3-final-red-20260901`,
  `sha256:732ad07fd18177326b3afc9a78833201b96dba383d67373919420affc89f0286`.
- **E131:** second changes-required review and final evidence:
  review
  `sha256:8fd588fa609f9884eabc1feff9197ad3e55c5476234dfcc9056bf2d792406550`
  and evidence
  `sha256:d855661aa02e6721865bf49ef696a0f52c1eb70faaff0937c1ec7e37d61fdff7`.
- **E132:** final Red and canonical ledgers:
  test
  `sha256:2cada8a0adeb210f1fa8cd626321c946d3e3ebe8b080c0e7597897736d057463`,
  tasks
  `sha256:302db716520657d3b4f0a8534dd32525a727e18ab5880f925950ab2c9d6109da`,
  context
  `sha256:dc25ca4a2fb13e46536847e200ab1bb82b3c42aae2123f748e7f4e876d8f15a7`,
  and plan
  `sha256:0fc0037f1ac0cca143cb832e42c59302937901d7a1464b39f00162e968d8f06e`.

Final Red closes inherited interfaces and class bases, inspects complete public
member/type semantics, proves exact borrowed array-segment identity and
mutation visibility, rejects UUIDv1/v4/v6/v8, and denies plaintext, hex,
Base64, and decimal byte diagnostics. Seven tests retain exact absent-owner
attribution. IVSD-F038/M038, F039/M039, and F040/M040 remain sufficient.

Complete D2-3 Application Red evidence is:

- **E133:** complete D2-3 Red binding
  `setup-assistant-security-and-portability-phase9-d2-3-complete-red-20260901`,
  `sha256:d7cbbcb14ecf15abdbd649cbb9ccce7b52672d97d972c534498225d81b27e39d`.
- **E134:** third changes-required review and complete evidence:
  review
  `sha256:b2175ea8442cbc0af882bfacc1948f75aa5055e5052528bb3d9d8a0c9d326d99`
  and evidence
  `sha256:732bb6cf66a8f95505104518731801166d0ed84edb6e7b0659f4916fd1e33432`.
- **E135:** complete Red and canonical ledgers:
  test
  `sha256:25a4086f985a6228c5f2fc6d0d801ac2f1c3bad256b78fc35f4982755dc068d2`,
  tasks
  `sha256:976be808357aa67934e39a46dff1d3aa7ef782b6aa9948d18edcca9505941770`,
  context
  `sha256:dc1ac9876d79c2f02e76157063611a071908e36c9cb3aa9134d285036fe0e8bd`,
  and plan
  `sha256:0fc0037f1ac0cca143cb832e42c59302937901d7a1464b39f00162e968d8f06e`.

Complete Red includes special-name interface methods in exact comparison,
forbids every unapproved public property/field/event/method/operator/nested
type, extends static metadata closure, and scans public field types. Seven
tests retain exact absent-owner attribution. IVSD-F038/M038, F039/M039, and
F040/M040 remain sufficient.

Exhaustive D2-3 Application Red evidence is:

- **E136:** exhaustive D2-3 Red binding
  `setup-assistant-security-and-portability-phase9-d2-3-exhaustive-red-20260901`,
  `sha256:fc1e95aef0afd6c759e2bf277d0359e8b39530445d67910d510eb6d401150c1d`.
- **E137:** fourth changes-required review and exhaustive evidence:
  review
  `sha256:d5eadd2c34368a30c8343b16209803cb208c650b45408bebfb3589185e3de3d4`
  and evidence
  `sha256:8fdfb632915f51e5219a70e6f899db89caaacea897f66316e2379c7090bac074`.
- **E138:** exhaustive Red and canonical ledgers:
  test
  `sha256:627567d68e080f45bc4f698dd48382062821160c86b14bf1d452010e1234a971`,
  tasks
  `sha256:cac0aa4d59d3636ca632b013124fb8658b3eab5bcba61014f784eb8d740e801b`,
  context
  `sha256:00ff43b016965ce28799d1bf8cab62c3bca3346cd9f639d10037d7d624e849d5`,
  and plan
  `sha256:0fc0037f1ac0cca143cb832e42c59302937901d7a1464b39f00162e968d8f06e`.

Exhaustive Red freezes injectable abstract method semantics, non-indexed
properties, zero explicit interfaces, exact readonly backing storage, enum
numeric values, compiler-only metadata, defining assembly/top-level
ownership, and absence of forwarding. Seven tests retain exact absent-owner
attribution. IVSD-F038/M038, F039/M039, and F040/M040 remain sufficient.

Closed D2-3 Application Red evidence is:

- **E139:** closed D2-3 Red binding
  `setup-assistant-security-and-portability-phase9-d2-3-closed-red-20260901`,
  `sha256:0c7cc11e2b895f474290c2939a97cb3f34f2ca498fa8df3ddfc5d654b0cfadce`.
- **E140:** fifth changes-required review and closed evidence:
  review
  `sha256:fea22d226d869581063ea8afb18ce3669c89e7c8fe93ee4c4de0953544b46cd4`
  and evidence
  `sha256:a3bb1997a2faab3bfef611f2fe7ac4813075cb005654c320e906009fc7032149`.
- **E141:** closed Red and canonical ledgers:
  test
  `sha256:ad6ecc9a6c8bd78cd1a066241fdcfdcf05c9563a158126d764039d46b8b041bf`,
  tasks
  `sha256:a90bcaed5d334c5e401044b87cd38d40fc3ef302e3c5c73d1a364af22c9c89d5`,
  context
  `sha256:2f3eb75ddc3f0efcedf6653e2f342dac66510ee7e47c639c7ef2cc0324b4c5ae`,
  and plan
  `sha256:0fc0037f1ac0cca143cb832e42c59302937901d7a1464b39f00162e968d8f06e`.

Closed Red rejects private port/metadata/enum behavior, binds exact SDK
method/getter/constructor and attribute metadata through same-shape witnesses,
binds the existing Application assembly/module security baseline, and closes
full exported/forwarded prefixes. Seven tests retain exact absent-owner
attribution. IVSD-F038/M038, F039/M039, and F040/M040 remain sufficient.

Approval-ready D2-3 Application Red evidence is:

- **E142:** approval-ready D2-3 Red binding
  `setup-assistant-security-and-portability-phase9-d2-3-approved-ready-red-20260901`,
  `sha256:3883929342439280a627915abbd7adbadeb819376cc8e9c5b6262e51c4d3b5de`.
- **E143:** sixth changes-required review and approval-ready evidence:
  review
  `sha256:042f62d5bcd4ac92749e314640304202e6527a94221fdd390bb4dfb9723f6e80`
  and evidence
  `sha256:944f36e4bcca75458103f36838b8add977b43565e6f1b326e84f241eb3431c28`.
- **E144:** approval-ready Red and canonical ledgers:
  test
  `sha256:fac62f11818682ead979b5c19f53f23e521a622262d6efe599fe83eecae1be01`,
  tasks
  `sha256:19c9e83a96a78286f321fa8ae43022a0b6e05557b529d271ac815126d166bda6`,
  context
  `sha256:c72ffa055b559a6f3b45033cff54ff957a6a6615c642552b417a18023585bfdb`,
  and plan
  `sha256:0fc0037f1ac0cca143cb832e42c59302937901d7a1464b39f00162e968d8f06e`.

Approval-ready Red compares complete Property/Param/Field metadata, inspects
constructor return MethodDef metadata without unstable cross-assembly token
bytes, and structurally compares custom-attribute type/constructor/member/
typed-payload identity. Seven tests retain exact absent-owner attribution.
IVSD-F038/M038, F039/M039, and F040/M040 remain sufficient.

Final approvable D2-3 Application Red evidence is:

- **E145:** final approvable D2-3 Red binding
  `setup-assistant-security-and-portability-phase9-d2-3-final-approvable-red-20260901`,
  `sha256:edb31317a5c48168e35e97bdd60e61df1e1625f463efa93cd76101768baa9eb8`.
- **E146:** seventh changes-required review and final approvable evidence:
  review
  `sha256:eb968040e608dce3408aeb379829894a2f0e377531f64f47ef16532fec4efb92`
  and evidence
  `sha256:c443616f85d308542aac18f767ecbe16a97c4b9caba4661437d50fd08ba4c33c`.
- **E147:** final approvable Red and canonical ledgers:
  test
  `sha256:71e4dbceee9fb438f7f25d511a696b5bccc9b8e5b36c8c96db9e457094153791`,
  tasks
  `sha256:54157840db8e67003843d085bd3447c70e8d1214e9d7abbcd4601f6f47e4ea80`,
  context
  `sha256:b47a9226f94f1e37e4edcfc17771c9bb6bd10f680247456ff9b30fc64be0ae37`,
  and plan
  `sha256:0fc0037f1ac0cca143cb832e42c59302937901d7a1464b39f00162e968d8f06e`.

Final approvable Red binds exact BCL assembly attribute identities/
constructors/payloads, the exact single manifest module and its structural
`RefSafetyRules(11)` metadata, and every owner to that module. Seven tests
retain exact absent-owner attribution. IVSD-F038/M038, F039/M039, and
F040/M040 remain sufficient.

## Requested Direction Coverage

| Requested direction | Report coverage |
|---|---|
| New Setup product under `src/` | Product Direction and Project Architecture |
| Web and desktop from one framework-neutral experience | Successor-B Target Architecture and Target Matrix |
| Windows, Linux, and macOS | Desktop Distribution And Packaging |
| Debian, Arch, and additional Linux paths | Linux packaging matrix |
| No commercial/proprietary dependencies | FOSS Philosophy And Outbound Compatibility Gate |
| GPL/AGPL and other FOSS may be considered | Target-specific reciprocal-license boundary and legal approval |
| Manifest authoring/export | Configuration Portability Workspace |
| `.env` generation | Environment Setup Workspace |
| Progressive optional variables and secrets | Environment Catalogue and Wizard |
| Web defaults to no secret entry | Default No-Secret Web Mode |
| Optional trusted web secret entry | Optional Web Secret Mode |
| Secret values never sent to a server | Browser Network Denial Contract |
| Empty relevant secret placeholders | Relevant-Only Dotenv Rendering |
| Omit irrelevant and defaulted variables | Relevance and omission rules |
| Header explains intentionally partial output | Generated File Header Contract |
| Official-host transparency | Official Web Trust And Provenance |
| Optional gitignored web target | Gitignore And Exclusivity Assessment |
| Instance and tenant legal texts in portable configuration | Legal Document Configuration Model |
| Terms, privacy, and broader legal-content catalogue | Legal Document Kind Catalogue |
| Templates and Markdown editing | Legal Template Library and Safe Markdown Editor |
| Interactive terminal workflow plus machine CLI | BCL Terminal Wizard And CLI Product Architecture |
| Same functional outcomes across GUI and terminal UI | Functional Parity Contract |
| Agent-automatable commands | Versioned Command And Machine-Output Contract |
| Agentic skill instead of embedded AI | Setup Assistant Agentic Skill and No Embedded AI Boundary |
| YAML and directory composition | IVSD-F037/M037 and Scenario 3.13 / SA-810–SA-830 mapping |
| Live enrollment and secret-provider binding | IVSD-F038–F040 / M038–M040 and Scenario 3.14 / SA-910–SA-1030 mapping |
| Application-data custody and privacy | IVSD-F041–F042, F044–F046 / matching mitigations and Scenario 3.15 / SA-1110–SA-1130 mapping |
| Payment/refund operational migration | IVSD-F043, F045–F046 / matching mitigations and Tier 0 SA-1110/SA-1140 gates |
| All prior consultation recommendations | Architecture, security, packaging, rejected alternatives, and validation sections |

## Findings

### Finding Register

| ID | Lifecycle | Severity | Claim type | Principle/domain | Provider-controlled decision and risk | Evidence and validation level | Mitigation | Owner or escalation |
|---|---|---|---|---|---|---|---|---|
| IVSD-F001 | accepted | High | Product opportunity | Ihsan, Promise-Keeping; Strategic | A focused setup application can materially improve self-hosting and portability | E001-E009; design and implementation traceability | IVSD-M001 | Product |
| IVSD-F002 | accepted | Critical | Architecture requirement | Amanah, Truthfulness; Technical | Duplicated manifest/environment rules across server and UI will drift | E001-E006; implementation traceability | IVSD-M002 | Architecture |
| IVSD-F003 | accepted | Blocker | Web-origin trust boundary | Amanah, Truthfulness; Technical/Governance | An online origin can serve modified code that exfiltrates secrets | E010-E014; standards-based design validation | IVSD-M003 | Security + Legal |
| IVSD-F004 | accepted | Critical | Protective-default requirement | Non-Harm, Avoiding Gharar; Design | Asking for secrets by default normalizes unnecessary exposure | User decision; design validation | IVSD-M004 | Product + Security |
| IVSD-F005 | accepted | Blocker | Web network boundary | Avoiding Spying, Amanah; Technical | Client-side code can transmit secrets through many browser request channels | E010-E014; standards-based design validation | IVSD-M005 | Web Security |
| IVSD-F006 | accepted | Critical | Browser-state boundary | Privacy, Non-Harm; Technical | Storage, crash, extension, autofill, clipboard, memory, and service-worker behavior can retain or expose secrets | E012-E014; design validation | IVSD-M006 | Web Security + UX |
| IVSD-F007 | accepted | High | No-secret usability requirement | Ihsan, Autonomy; Design | Omitting all secret keys would leave users without an actionable deployment file | User decision, E001-E005; implementation traceability | IVSD-M007 | Product |
| IVSD-F008 | accepted | Critical | Catalogue requirement | Truthfulness, Promise-Keeping; Technical | Scraping `.env.example` or duplicating metadata creates undocumented drift | E001-E006; implementation traceability | IVSD-M008 | Architecture + Docs |
| IVSD-F009 | accepted | Blocker | Separation requirement | Amanah, Privacy; Technical | Combining secrets with portable manifests makes shareable configuration unsafe | E006-E007; implementation traceability | IVSD-M009 | Security |
| IVSD-F010 | accepted | Blocker | Desktop file safety | Amanah, Non-Harm; Technical/Operational | Plaintext `.env` can be exposed by weak permissions, symlinks, backups, or unsafe overwrite | E015-E016; standards-based design validation | IVSD-M010 | Desktop + Security |
| IVSD-F011 | accepted | Critical | Secret-memory limitation | Truthfulness, Privacy; Technical | Managed/browser memory cannot promise deterministic secret erasure | E012-E016; design validation | IVSD-M011 | Security |
| IVSD-F012 | accepted | Critical | Observability boundary | Avoiding Spying, Amanah; Technical/Operational | Telemetry, analytics, logs, crash capture, or update checks can violate the local-only promise | E004-E005, E012-E014; implementation traceability | IVSD-M012 | Security + Operations |
| IVSD-F013 | accepted | Blocker | Dependency-license requirement | Amanah, Promise-Keeping; Governance | “FOSS” or a permissive top-level license does not prove complete component provenance/notices or compatibility for every outbound path; blocked graphs and prior approvals must not be inherited by a replacement | E008-E009, E017, E027-E028, E041-E043, E048-E054, E056-E066; repository policy, exact B1 graph verdicts, final content-hash ratchet, and unchanged product preimages | IVSD-M013 | IP/Legal + Release |
| IVSD-F014 | accepted | High | Distribution integrity | Amanah, Ihsan; Operational | Unsigned or unverifiable desktop/web artifacts expose secret-handling users to supply-chain substitution | E009-E011, E017; design validation | IVSD-M014 | Release + Security |
| IVSD-F015 | accepted | High | Platform-support requirement | Justice, Ihsan; Design/Operational | Package-free shells, shared ViewModels, `ApprovedDisabled` adapters, or framework-neutral outcome mappings do not prove a GUI target; “cross-platform” remains misleading without real per-OS/browser packaging, launch, file, accessibility, and upgrade evidence | E010-E011, E018-E020, E034-E037, E041, E048-E066; official functional, exact B1 planning, graph, final Red, and non-drift evidence | IVSD-M015 | Release + QA |
| IVSD-F016 | accepted | High | Hosting-governance concern | Truthfulness, Justice; Strategic/Governance | Ignoring web source cannot prevent malicious rebuilds or third-party hosting and can weaken public auditability | E009, E017, E021; legal/governance evidence | IVSD-M016 | Project Steward + Legal |
| IVSD-F017 | accepted | Critical | Official-instance identity | Truthfulness, Amanah; Governance/Design | Users can confuse an unofficial fork with the official secret-capable service | E021; design validation | IVSD-M017 | Legal + Product |
| IVSD-F018 | accepted | High | Accessibility requirement | Justice, Ihsan; Design/Evaluation | Complex forms, masked values, review, and file generation can exclude disabled users; shared ViewModels and disabled adapters do not establish target accessibility | E022, E034-E037, E048-E055, E057-E060; standards, exact B1 planning, architecture verdict, corrected Red, and target-agnostic ownership evidence | IVSD-M018 | Accessibility + UI |
| IVSD-F019 | accepted | High | Localization requirement | Justice, Ihsan; Design | Deployment and security explanations can be misunderstood without localization and RTL | E022-E023; implementation traceability | IVSD-M019 | Localization + UI |
| IVSD-F020 | accepted | Blocker | Live-secret authority boundary | Amanah, Avoiding Spying; Technical | A convenience tool can drift into extracting secrets from live instances or Infisical | E003-E007; implementation traceability | IVSD-M020 | Security + Secrets |
| IVSD-F021 | accepted | High | Claim-governance requirement | Truthfulness, Avoiding Gharar; Strategic/Governance | “Fully safe” or “we cannot get secrets” overstates what hosted or local software can prove | E012-E014; design validation | IVSD-M021 | Legal + Product |
| IVSD-F022 | accepted | High | Evidence requirement | Ihsan, Amanah; Evaluation | Passing tests, isolated probes, signatures, or top-level metadata cannot prove zero disclosure, complete component provenance/notices, publish exclusions, package integrity, accessibility, support, usability, or long-term safety | E009-E022, E041, E048-E066; design, policy, exact B1 graph/final-Red evidence, non-forgeable completion and retained-exhaustion contracts, governed commit closure, and unchanged product preimages | IVSD-M022 | QA + Security + Operations |
| IVSD-F023 | accepted | Critical | Legal-authority requirement | Truthfulness, Justice; Governance/Technical | Portable legal text can misattribute instance, tenant, organizer, or merchant responsibility if scope is generic | E007, E024-E026; implementation traceability | IVSD-M023 | Legal + Architecture |
| IVSD-F024 | accepted | Critical | Legal-lifecycle requirement | Amanah, Rights of People; Governance/Technical | Importing legal text can overwrite published history or fabricate prior user acceptance | E024-E026; implementation traceability | IVSD-M024 | Legal + Domain |
| IVSD-F025 | accepted | High | Template-governance concern | Truthfulness, Avoiding Gharar; Design/Governance | A legal template can be mistaken for legal advice or jurisdictional compliance | User direction, E009; design validation | IVSD-M025 | Legal + Product |
| IVSD-F026 | accepted | Critical | Markdown-content boundary | Non-Harm, Amanah; Technical/Design | Unrestricted Markdown/HTML can introduce scripts, tracking, deceptive links, inaccessible output, or remote resource loads | E012-E014, E022; standards and implementation traceability | IVSD-M026 | Security + Accessibility |
| IVSD-F027 | accepted | High | Portability-completeness requirement | Promise-Keeping, Ihsan; Strategic/Technical | Legal links without portable source text leave migrations incomplete and dependent on old origins | E007, E024-E026; implementation traceability | IVSD-M027 | Product + Architecture |
| IVSD-F028 | accepted | High | Content-scale requirement | Amanah, Ihsan; Technical/Operational | Legal Markdown and localized variants can exceed current manifest string/file limits or make diffs unusable | E007; implementation traceability | IVSD-M028 | Architecture + UX |
| IVSD-F029 | accepted | High | Access/parity requirement | Justice, Ihsan; Design/Technical | Desktop/web-only operation excludes terminal-first, remote-shell, and automation users; the sole Terminal.Gui target must preserve both deterministic machine outcomes and local human TTY secret completion without a fallback renderer | E031-E037, E041, E048-E055; repository patterns, exact B1 planning/Red evidence, and 2026-09-01 Steward decision | IVSD-M029 | Product + CLI |
| IVSD-F030 | accepted | Critical | Automation-contract requirement | Truthfulness, Amanah; Technical | Agents and scripts cannot safely automate an interactive TUI or unstable prose output | E031-E033; repository implementation traceability | IVSD-M030 | CLI + Tooling |
| IVSD-F031 | accepted | Blocker | Terminal secret boundary | Amanah, Avoiding Spying; Technical/Operational | Arguments, shell history, scrollback, process listings, pipes, logs, and stdout can expose secrets | Design validation | IVSD-M031 | CLI + Security |
| IVSD-F032 | accepted | Critical | Agent-safety requirement | Amanah, Non-Harm; Governance/Technical | An agentic skill can encourage agents to read, transmit, infer, or persist secrets unless explicitly prohibited | E032-E033; skill-contract evidence | IVSD-M032 | Agent Governance + Security |
| IVSD-F033 | accepted | High | AI-boundary requirement | Truthfulness, Avoiding Gharar; Strategic/Technical | Embedded AI would add providers, data flows, cost, nondeterminism, and privacy duties unrelated to deterministic setup | User decision; design validation | IVSD-M033 | Product |
| IVSD-F034 | accepted | High | Human-approval requirement | Autonomy, Justice; Design/Governance | Agent-generated configuration can silently broaden policy or publish legal text without informed review | E007, E032-E033; design validation | IVSD-M034 | Agent Governance + Product |
| IVSD-F035 | accepted | High | Terminal accessibility limitation | Justice, Truthfulness; Design/Evaluation | Selecting Terminal.Gui as the sole human terminal target does not prove screen-reader, RTL, keyboard, color, Unicode, resize, signal, scrollback, or small-terminal behavior | E022, E031-E037, E041, E048-E055; requirements, exact B1 planning, and architecture evidence only | IVSD-M035 | Accessibility + CLI |
| IVSD-F036 | accepted | Critical | Skill-lifecycle requirement | Promise-Keeping, Truthfulness; Governance | Publishing a skill before versioned commands exist teaches fictional or stale behavior | E032-E033; skill-contract evidence | IVSD-M036 | Skill owner + CLI owner |
| IVSD-F037 | accepted | High | Composition-integrity requirement | Amanah, Truthfulness; Technical | YAML/directory ambiguity, path metadata, unsafe parser authority, platform overclaim, mixed ownership, or unmeasured scale can change canonical meaning or conceal prohibited content | E034-E037, E067-E074; final Phase 8 governance, unchanged technical contract/product preimages, independent reviews, and plan/task traceability | IVSD-M037 | Setup Core; C1 Red/Green and C2 |
| IVSD-F038 | accepted | Blocker | Tenant-isolation boundary | Justice, Rights of People; Technical/Governance | Source identifiers, mappings, profiles, or capabilities can cross target or tenant authority during live and data migration | E034-E036, E038, E075-E147; current repository, predecessor, intake, binding, debate, Red, correction, approval, strict D2-1, D2-2 Domain Green, and final approvable D2-3 Application Red traceability | IVSD-M038 | Server authorization; SA-910, SA-1110–SA-1130 |
| IVSD-F039 | accepted | Blocker | Authorization/replay boundary | Amanah, Non-Harm; Technical | Stale HAL, bearer replay, duplicate transfer, or local authority synthesis can mutate the wrong target or repeat effects | E034-E036, E075-E147; current repository, predecessor, intake, binding, debate, Red, correction, approval, strict D2-1, D2-2 Domain Green, and final approvable D2-3 Application Red traceability | IVSD-M039 | Security; SA-910–SA-1030 |
| IVSD-F040 | accepted | Blocker | Secret-provider boundary | Privacy, Avoiding Spying; Technical/Operational | Secret readback or provider-coordinate disclosure would turn Setup into a privileged extraction and reconnaissance client | E004-E006, E034-E036, E075-E147; current repository, predecessor, intake, binding, debate, Red, correction, approval, strict D2-1, D2-2 Domain Green, and final approvable D2-3 Application Red traceability | IVSD-M040 | Secrets + Security; SA-910–SA-930 |
| IVSD-F041 | accepted | Blocker | Data/PII custody boundary | Rights of People, Privacy; Technical/Governance | Application-data migration creates custody, purpose, retention, erasure, access, staging, and breach responsibilities independent of configuration portability | E034-E036, E039; repository and plan traceability | IVSD-M041 | Privacy authority; SA-1110–SA-1130 |
| IVSD-F042 | accepted | Critical | Migration-continuity requirement | Amanah, Promise-Keeping; Technical/Operational | Non-durable checkpoints, mappings, or idempotency can duplicate, omit, corrupt, or resurrect application records after interruption | E034-E036, E039; repository and plan traceability | IVSD-M042 | Migration owner; SA-1110–SA-1130 |
| IVSD-F043 | accepted | Blocker | Sovereign-money boundary | Justice, Amanah; Financial/Governance | Migrating payment/refund operations without provider, ledger, currency, recipient, and allocation reconciliation can mutate money falsely or unfairly | E034-E036, E040; repository and plan traceability | IVSD-M043 | Tier 0 payment authority; SA-1110, SA-1140 |
| IVSD-F044 | accepted | Critical | Source-retention and autonomy requirement | Autonomy, Rights of People; Strategic/Operational | Destructive or coerced transfer can strand operators, erase remedy evidence, or create lock-in before target completion is proven | E034-E036; plan/task traceability | IVSD-M044 | Product + Operations; SA-1030, SA-1120–SA-1130 |
| IVSD-F045 | accepted | Critical | Truthful-recovery requirement | Truthfulness, Avoiding Gharar; Design/Operational | Progress, receipts, rollback, refund, or completion claims can mislead users when server/provider effects remain pending, unknown, or compensating | E034-E036, E040; repository and plan traceability | IVSD-M045 | Operations + UX; SA-1010–SA-1140 |
| IVSD-F046 | accepted | High | Human-agency requirement | Autonomy, Justice; Design/Governance | Bundled migration categories or agent/live defaults can broaden data, payment, or target authority without informed category-level approval | E034-E036; plan/task traceability | IVSD-M046 | Product + Governance; SA-1020, SA-1030, SA-1110–SA-1140, SA-1240 |

### IVSD-F001 — The Setup Assistant Advances Credible Self-Hosting

The product is justified because it reduces the expertise required to produce
two difficult artifacts without weakening their boundaries:

- non-secret portable configuration;
- deployment-local environment configuration, which may contain secrets.

It supports small communities that do not operate Infisical while preserving
advanced self-hosters’ ability to use the same schemas, validation, and
deployment profiles. It also gives contributors one visible contract for
configuration coverage and missing documentation.

### IVSD-F002 — Shared Rules Must Be Headless

The Setup presentation adapters and BCL terminal wizard must not reference
`Explore.Application`, `Explore.Infrastructure`, persistence, MediatR, EF Core,
or provider SDKs. A
small pure library should own:

- manifest/package contracts;
- strict lexical reading and static validation;
- deterministic serialization;
- environment-variable metadata and activation predicates;
- dotenv parsing and rendering;
- safe value-format validation;
- generated-output diagnostics.

Server runtime validation remains authoritative for current instance state,
tenant authority, locks, policy ceilings, reference mapping, and transactional
import. Offline static validation must never claim to prove those runtime facts.

Planning revalidation refines the ownership without weakening this finding:
the existing package-free `Event.Wire.Contracts` owns exact versioned
manifest/package wire contracts and constrained legal Markdown, while the new
`Event.Setup.Core` owns environment metadata, dotenv, offline validation,
diffs, readiness, and workflow state. Both remain headless; the split prevents
the offline product from referencing all of `Explore.Application`.

### IVSD-F003 — Hosted Web Secret Entry Always Trusts The Origin

A successor-B static browser client can execute all product logic without
server-side application code. This does not mean the host is unable to receive
secrets. The host supplies the executable client on every uncached load and can
change it.

The optional official web secret mode is acceptable only as an explicit
trust-based convenience:

- default remains no-secret mode;
- users must affirm that they trust the displayed official origin and release;
- the page states that source and build evidence apply to an identified
  release, not to every possible future response;
- desktop remains the recommended path for higher-assurance secret entry;
- an offline, checksum-verifiable web bundle may be offered as another path.

### IVSD-F004 — No-Secret Mode Is The Protective Default

Every new browser session starts in no-secret mode. A remembered preference
must not silently reopen secret mode. The no-secret path remains fully useful:

- asks topology and feature questions;
- asks non-secret values;
- identifies relevant secret variables;
- emits those relevant secret keys with empty values;
- reports that secret completion remains;
- omits unrelated variables and features;
- relies on documented runtime defaults where absence is intentional.

### IVSD-F005 — Secret Mode Requires A Browser Network-Denial Contract

“We do not call our API” is too narrow. Browser code can communicate through
fetch, XMLHttpRequest, WebSocket, EventSource, beacon, forms, images, media,
fonts, frames, workers, navigation, dynamic scripts, and future APIs.

The reviewed secret-capable web build must:

1. load all required local assets before secret entry;
2. use no third-party scripts, fonts, images, analytics, tags, or CDNs;
3. deny scripted connections;
4. deny forms, embedding, objects, remote images/media/fonts, and unapproved
   workers;
5. avoid CSP violation reporting because reporting itself sends a request;
6. contain no lazy localization, documentation, update, or feature fetch after
   secret entry;
7. prevent external navigation while secret values remain;
8. prove zero requests after the secret-mode transition in supported browsers.

CSP is defense in depth, not proof against a malicious origin, compromised
browser, extension, or user override.

### IVSD-F006 — Browser-Local Is Not The Same As Ephemeral Or Secret

Secret values must never enter:

- URL, query, fragment, history, title, referrer, or route state;
- localStorage, sessionStorage, IndexedDB, Cache API, service-worker state, or
  browser-managed application state;
- cookies;
- browser logs, console, diagnostics, exceptions, or source maps;
- DOM attributes, hidden fields, accessibility labels, validation messages, or
  clipboard unless the user explicitly asks to copy;
- telemetry or CSP reports.

Masked input does not protect against browser extensions, password managers,
screen capture, accessibility tooling, compromised operating systems, or
developer tools. The UI must disclose this without frightening or shaming the
user.

### IVSD-F007 — Empty Relevant Placeholders Preserve Utility

In no-secret mode:

- selected required secret variables render as `KEY=`;
- each is marked as required before startup through adjacent comments and the
  readiness report;
- optional secrets for selected optional features render only when that
  feature requires them;
- secrets for unselected features do not appear;
- fake example secrets and insecure defaults are forbidden;
- generated cryptographic values are not produced unless the user explicitly
  enables secret mode or uses the desktop generator.

An empty secret placeholder is not a valid deployment. The final review must
state `Incomplete: secret values still required` and name the keys without
inventing values.

### IVSD-F008 — Environment Metadata Needs One Canonical Catalogue

The Setup Assistant must not parse human prose in `.env.example` as product
logic. Introduce a pure canonical catalogue with metadata such as:

- key;
- category and description resource key;
- value type and safe validation;
- secret classification;
- required/optional/defaulted status;
- declarative `RequiredWhen` conditions;
- deployment topology/profile;
- provider/capability dependency;
- generation policy;
- restart requirement;
- documentation anchor;
- example policy;
- scope and output format.

The catalogue contains no secret values. It should generate or validate:

- `.env.example`;
- Setup Assistant form metadata;
- startup configuration coverage;
- configuration documentation;
- Compose-variable coverage;
- CI drift checks.

`SecretDefinitionRegistry` remains authoritative for secret-binding semantics,
but the planned architecture must resolve its current Domain/Application
coupling without copying registry data into the UI.

### IVSD-F009 — Manifests And Dotenv Files Must Stay Separate

`ConfigurationManifest` and `TenantConfigurationPackage` remain non-secret and
shareable according to their authority. `.env` is deployment-local and may be
secret-bearing.

The product may generate both into one user-selected directory, but it must
never:

- embed `.env` values in a manifest;
- create one combined JSON or ZIP by default;
- label the files as having equivalent sensitivity;
- attach `.env` to an import/export package;
- upload `.env` to an instance;
- treat `.env` as tenant configuration.

### IVSD-F010 — Desktop Writing Must Fail Safely

Desktop builds can provide stronger file guarantees than browsers:

- native save picker;
- same-directory temporary file;
- owner-only mode established before or immediately with creation;
- atomic replacement only after a redacted review;
- symlink/reparse-point and unexpected file-type refusal;
- explicit overwrite confirmation;
- no automatic plaintext backup;
- post-write permission verification;
- safe failure that does not leave a partial file.

On Unix-like systems the target is owner read/write only. On Windows the target
is an ACL limited to the current user and required system authority. If the
filesystem cannot represent the requested protection, default behavior is to
refuse and explain; an advanced override must be explicit and cannot be called
safe.

Browser downloads cannot reliably impose equivalent filesystem permissions.
The secret-capable web mode must state that limitation before download.

### IVSD-F011 — Secret Lifetime Can Be Reduced, Not Proven Erased

.NET strings, selected-GUI bindings, browser runtime memory, DOM state, and OS
buffers can copy secret values. The product should:

- minimize copies and conversions;
- avoid immutable secret-containing display strings where practical;
- clear view models and rendered values immediately after generation,
  cancellation, navigation, or idle expiry;
- dispose buffers where supported;
- never retain secret state for reopening;
- not claim deterministic secure erasure.

### IVSD-F012 — Secret Workflows Must Be Observability-Free

Production builds must have:

- no product analytics;
- no remote telemetry;
- no automatic crash upload;
- no session replay;
- no remote logging;
- no CSP reporting endpoint;
- no update check during or after a secret session;
- no developer tools package;
- no source maps containing application secrets or user values;
- only local bounded diagnostics that never include entered values.

An optional user-created support report may contain build identity, platform,
selected feature keys, and closed error codes. It must not contain values,
paths that reveal usernames without consent, clipboard contents, environment
contents, or raw exceptions.

### IVSD-F013 — FOSS Philosophy Does Not Remove License Compatibility

The user revised the policy to permit free/open-source licenses, including
reciprocal GPL/AGPL families, while rejecting commercial, proprietary, and
source-available dependencies. This is coherent with open-source stewardship,
but legal compatibility remains target-specific.

Apply three boundaries:

1. `Event.Setup.Core`, because it can be shared with the main server and every
   UI, must preserve every intended ISLAMU outbound path. A reciprocal
   dependency that prevents alternative licensing is blocked there.
2. Public Setup Assistant executables may be explicitly AGPL-only when a
   reciprocal dependency is compatible with the assembled public work and the
   Project Steward documents that the target is excluded from alternative
   licensing.
3. A separate executable invoked through a bounded process protocol may have
   different obligations, but separation must be legally and technically real
   rather than a wrapper around intimately linked functionality.

No target may include:

- commercial-license runtime packages;
- proprietary or source-available components;
- field-of-use, seat, hosting, or noncommercial restrictions;
- unknown/unverified licenses;
- a package whose source, notice, installation-information, relinking, or
  network-source obligations cannot be satisfied.

The exact SA-120 evidence still blocks official Terminal.Gui 2.4.17
as-published because mandatory TextMateSharp.Grammars 2.0.4 has incomplete
component-level grammar provenance and notices. The 2026-09-01 Steward
exception does not approve that graph: it authorizes only a temporary,
separately named package built from the pinned official source after removing
the grammar/editor integration and proving the resulting artifact. Avalonia
12.1.1 Desktop and Browser graphs remain blocked because
native binary/component/license mapping remains unresolved and exact publish
absence of `Avalonia.Remote.Protocol` is unproved. Signed-package integrity,
ANGLE's resolved license, build-only conditionality, or a permissive top-level
license does not cure those gaps.

Successor A originally pinned/restored neither graph. Phase 5R now permits B1
to admit only `ISLAMU.Terminal.Gui` `2.4.17-islamu.1` after its exact patch,
source identity, MIT attribution, closure, SBOM, provenance, notices,
vulnerability, outbound-license, and anti-grammar-reentry gates pass. Avalonia
remains `ApprovedDisabled`. No prior approval, probe result, or isolated graph
fact is inherited.

### IVSD-F014 — Release Identity Protects Secret-Handling Users

Every artifact should bind:

- product version;
- Git commit;
- target RID/format;
- package-lock digest;
- SBOM digest;
- build manifest digest;
- signing identity;
- checksum file;
- source URL;
- reproducibility status.

Desktop artifacts require platform signing where available. macOS release
requires signing and notarization. Windows release requires Authenticode or the
selected signed package mechanism. Linux packages and portable archives require
detached signatures plus checksums and repository-key documentation.

The official web page must display the release identity and source link before
the user can enter secrets.

### IVSD-F015 — Cross-Platform Is An Evidence Claim

The support matrix must distinguish:

- source compiles;
- application launches;
- file picker works;
- dotenv can be saved;
- permissions are enforced;
- screen reader and keyboard work;
- package installs and uninstalls;
- upgrade preserves no secret drafts;
- signed artifact verifies;
- supported architecture.

Native AOT may improve startup and reduce runtime surface, but is an
optimization after functional and accessibility parity. It must not be used to
justify weaker test coverage or unverifiable native dependencies.

### IVSD-F016 — Gitignore Is Not An Anti-Malicious-Hosting Control

Two proposals must be separated:

1. **Ignore generated web publish output.** Recommended. `bin/`, `obj/`, and
   release artifacts should remain generated, signed, and retained by the
   release system rather than committed.
2. **Ignore or withhold the web target source so only ISLAMU can host it.**
   Rejected as a security control.

Withholding source:

- does not prevent someone from building a similar malicious page;
- does not prevent phishing on another domain;
- reduces public auditability and reproducible-build evidence;
- conflicts with the project’s open-source trust and may create AGPL source
  obligations for the official network service;
- creates false confidence that exclusivity prevents impersonation.

The user accepted the corrected boundary: track and publish the browser source;
ignore only generated `wwwroot`, build, publish, and release artifacts.

### IVSD-F017 — Official Hosting Needs Truthful Identity, Not Technical Monopoly

Realistic controls are:

- one documented official HTTPS origin;
- visible instance/operator legal identity;
- source and exact release links;
- signed release manifest and checksums;
- reproducible-build evidence;
- immutable content-addressed assets;
- strict CSP and security headers;
- no third-party resources;
- public security contact and incident process;
- trademark and brand-use policy;
- explicit warning that unofficial forks and lookalike domains are not
  ISLAMU-operated;
- optionally a downloadable offline bundle for independent verification.

Open-source users remain free to self-host compliant builds under the
repository license. Trademark and truthful attribution—not hidden source—are
the appropriate way to distinguish official operation.

### IVSD-F018 — Secret Forms Must Remain Accessible

Masked controls require real labels, description and error association,
keyboard reveal controls, and non-color indicators. B1's shared ViewModels,
compiled-binding plan, and `ApprovedDisabled` states prove none of those
outcomes; each `Active` target needs rendered and assistive-technology evidence.
The workflow must support:

- one logical heading structure;
- skip navigation;
- complete keyboard operation;
- visible focus;
- screen-reader mode/status announcements;
- no forced timeout without warning and extension;
- accessible review of key presence without announcing secret values;
- responsive reflow;
- high contrast and reduced motion;
- platform screen-reader testing.

### IVSD-F019 — Security Instructions Need Localization And RTL

The UI must localize:

- secret/no-secret mode choice;
- trust disclosure;
- relevant/omitted/defaulted explanations;
- validation and incomplete readiness;
- file-permission warning;
- official/unofficial host identity;
- recovery and support guidance.

RTL uses logical layout. Translation resources are bundled before secret mode;
secret entry must not trigger a TMS request.

### IVSD-F020 — The App Generates Secrets; It Does Not Retrieve Them

Offline/no-secret workflows and Phases 1–8 must not:

- connect directly to Infisical or another secret provider;
- retrieve existing server environment variables;
- query container process environments;
- call instance endpoints for credential values;
- import browser password-manager secrets automatically;
- test credentials directly against providers; or
- transfer `.env` between instances.

The approved Phase 9 expansion permits only target-authorized write and
value-free readiness operations through server adapters under IVSD-F040/M040.
It does not permit raw readback, direct provider SDK use, provider-coordinate
disclosure, portable secret bindings, or machine/agent secret handling.
IVSD-F020 therefore remains accepted rather than superseded.

### IVSD-F021 — Legal Transparency Does Not Change Technical Capability

Terms, privacy notices, public source, and organizational commitments are
important accountability controls. They cannot support the absolute claim that
the provider is technically unable to obtain a secret entered into code the
provider serves.

Approved copy should say what the identified build does, what evidence exists,
what trust remains, what is stored, what is transmitted, and which path offers
stronger assurance.

### IVSD-F022 — Zero Disclosure And Dependency Replacement Need Adversarial Evidence

The release must be tested as if a developer accidentally added:

- analytics;
- remote fonts;
- image beacons;
- exception upload;
- CSP reporting;
- lazy translation;
- update checks;
- form submission;
- service-worker synchronization;
- secret-bearing logs;
- browser storage;
- unsafe file backup;
- incorrect permissions;
- dependency with an unapproved license;
- a blocked graph member, package exception, or replacement GUI/TUI package;
- incomplete component provenance/notices; or
- an assumed build/publish exclusion such as `Avalonia.Remote.Protocol`.

The security promise is release evidence, not developer intention.

### IVSD-F023 — Legal Text Requires Explicit Role Authority

Instance and tenant legal texts should be portable, but not through a generic
key/value document bag.

The contract must preserve distinct accountable authors:

- instance/platform operator;
- tenant/directory operator;
- organizer or merchant where an event-specific contract applies.

An instance document cannot silently become the tenant’s statement. A tenant
document cannot replace the instance operator’s terms, privacy disclosure,
security notice, or platform responsibilities. Single-tenant deployment may
have one organization filling multiple roles, but the stored scopes and public
labels remain separate.

### IVSD-F024 — Legal Configuration Must Not Rewrite Evidence

Portable configuration may contain:

- legal-document drafts;
- current source Markdown;
- localized variants;
- publication intent;
- template provenance;
- proposed effective date;
- acceptance requirement.

It must not contain or rewrite:

- historical acceptance records;
- historical published versions;
- user/account acceptance timestamps;
- consent evidence;
- notification delivery evidence;
- old operator identities;
- legal-hold or dispute state.

Importing published-looking content on a target creates a new target-owned
version after explicit review. It never asserts that users accepted the source
instance’s version.

### IVSD-F025 — Templates Are Starting Points, Not Legal Approval

The Setup Assistant can materially improve quality with structured templates,
but every template must show:

- template identity and version;
- scope and intended operator role;
- language and jurisdiction assumptions;
- required placeholders;
- missing sections;
- provenance and license;
- date of legal review, when one exists;
- a prominent statement that local counsel and operator review remain
  necessary.

No template may be marketed as automatically compliant, universally valid, or
Islamically approved.

### IVSD-F026 — Markdown Must Be A Safe Typed Content Format

The editor should support a constrained Markdown profile rather than arbitrary
HTML:

- headings, paragraphs, emphasis, ordered/unordered lists, block quotes,
  tables, and safe links;
- no raw HTML;
- no script, style, iframe, object, embed, form, SVG, or executable content;
- no remote images, tracking pixels, data URLs, protocol-relative URLs, or
  automatic resource fetch;
- allowlisted link schemes and visible destination review;
- deterministic parsing, sanitization, normalization, and rendering;
- accessible heading and link validation;
- bounded document/locale/package size.

The same parser and sanitizer must be shared by editor preview, server
validation, public rendering, export, and import.

### IVSD-F027 — Legal Source Text Belongs In Portability

Exporting only `TermsUrl` and `PrivacyUrl` can leave a migrated tenant
dependent on the old instance’s domain. Portable configuration should include
the approved Markdown source and metadata for owned legal documents.

Target import must:

- rebind instance/tenant identity placeholders;
- identify links that still point to the source origin;
- require review of jurisdiction, contact, processor, payment, and complaint
  claims;
- preserve source/template provenance;
- create a target draft or newly reviewed version;
- never auto-publish silently.

### IVSD-F028 — Legal Content Changes Contract Limits And UX

Current manifest limits were designed for compact configuration documents.
Multiple legal-document kinds and localized Markdown can be substantially
larger.

The clean next contract must define:

- maximum documents per scope;
- maximum locales per kind;
- maximum Markdown bytes per document;
- maximum placeholder and link counts;
- maximum aggregate package size;
- streaming/bounded parsing;
- deterministic diff summaries;
- optional section-selective legal export.

Limits should be justified by realistic legal content and denial-of-service
protection, not inherited unchanged from the compact v1alpha1 contract.

### IVSD-F029 — Machine CLI And Terminal.Gui Are First-Class Product Targets

Ship two deliberately separate executables:

- `Event.SetupAssistant.Cli` with deterministic noninteractive commands for
  humans, scripts, CI, and external agents;
- `Event.SetupAssistant.Terminal` as the sole Terminal.Gui experience for
  terminal-first human use, including protected interactive secret completion.

“Same experience” means the same use cases, core rules, diagnostics, previews,
and generated bytes. It does not mean identical visual composition or an
unsupported claim of accessibility parity.

### IVSD-F030 — Agents Need Machine Contracts, Not Terminal-UI Automation

Terminal full-screen state is fragile for automation. Agents should use
versioned noninteractive commands with:

- stable command names and exit categories;
- versioned JSON output;
- diagnostic codes instead of prose parsing;
- explicit input/output paths;
- dry-run and no-secret defaults;
- artifact digests and coverage/readiness summaries;
- no ANSI control sequences in machine mode.

The Terminal.Gui executable can teach and assist humans. The future skill may
explain how to open it, but must direct agents to the machine command surface.

### IVSD-F031 — Terminal Secret Entry Has Distinct Leakage Paths

Secret values must never be accepted through:

- command-line arguments;
- process environment used as value transport;
- shell interpolation;
- filenames;
- standard output/error;
- JSON output;
- shell completion;
- terminal scrollback;
- command history.

Terminal.Gui secret entry remains interactive TTY-only through target-owned
masked, non-echoing fields, and secret-bearing output goes directly to a
protected file. The noninteractive CLI defaults to placeholders and rejects
secret values. No console fallback or shared presentation state may receive a
secret.

### IVSD-F032 — The Skill Must Protect Secrets From The Agent

The skill should teach an agent to:

- inspect catalogue metadata, never secret values;
- use no-secret, dry-run, and machine-output modes;
- never read an existing `.env`;
- never ask the user to paste a secret into chat;
- never pass secrets through tool arguments, captured stdin, logs, or reports;
- hand secret completion to the user’s local approved Terminal.Gui or desktop
  session;
- obtain approval for semantic diffs before writing;
- treat legal templates as drafts requiring counsel/operator review.

The skill is guidance, not an authorization or secret boundary. The CLI must
enforce every rule independently.

### IVSD-F033 — AI Remains Outside The Product

The Setup Assistant contains no:

- model SDK;
- AI provider;
- prompt runtime;
- chat UI;
- natural-language command parser;
- autonomous agent loop;
- remote inference;
- model telemetry;
- AI-specific secret.

Users may choose any external agent that can invoke the deterministic CLI.
This preserves local/offline operation and avoids forcing one AI vendor or data
flow onto users who do not want AI.

### IVSD-F034 — Agent Output Requires Human Approval

An agent may propose or generate:

- manifest/package drafts;
- relevant-only no-secret `.env`;
- legal-document drafts from approved templates;
- semantic diffs;
- validation and coverage reports.

An agent must not autonomously:

- enter or generate live provider credentials;
- read/write a completed secret-bearing `.env`;
- publish legal documents;
- assert counsel approval;
- apply to a live instance;
- broaden payment/security/privacy authority;
- erase or replace configuration without explicit approval.

### IVSD-F035 — Terminal Parity Has Real Accessibility Limits

Terminal.Gui does not by itself establish accessible behavior. Actual
operation depends on terminal
emulator, shell, font, color, width, locale, input/echo APIs, signals, resize,
scrollback/recording, and assistive technology.

The product must test and publish a separate terminal support matrix covering
keyboard completion, non-color status, Unicode/RTL, echo restoration, signals,
resize, bounded output, and known screen-reader limitations, while preserving
web/desktop alternatives through successor B. A narrow or inaccessible
terminal must not be the only path for a required operation.

### IVSD-F036 — Publish The Skill After The CLI Contract

The planned path is:

```text
.agents/skills/setup-assistant-cli/
```

Do not create an operational skill that names commands until:

- command names and JSON schemas are implemented;
- help and exit categories are tested;
- secret/no-secret behavior is enforced;
- examples run against the shipped version;
- the skill can declare its compatible CLI version range.

An early planning draft may describe principles, but it must not masquerade as
usable operational guidance.

### IVSD-F037 — Composition Must Not Create Competing Meaning

YAML and directory trees are bounded authoring inputs, not new portable wire
authorities. Duplicate keys, non-scalar keys, aliases, anchors, tags, merge
keys, ambiguous scalar coercion, conflicting fragments, links/reparse points,
traversal, changed entries, cycles, unknown files, source ordering, and
unmeasured expansion must fail deterministically before partial output. Source
paths, source-only metadata, secret values/references, provider identifiers,
and application data must not enter the normalized model, canonical output,
diagnostics, measurement records, or hashes.

Every accepted representation compiles through one normalized model and the
existing serializer/validator to the same canonical v1alpha2 JSON bytes,
digest, section coverage, legal limits, and diagnostics. JSON remains the only
wire identity. Composition stays self-hostable and offline: no network,
telemetry, provider, remote-reference, resolver, or application-service role is
introduced.

Phase 5R implementation evidence now binds the upstream tag object and commit,
one frozen patch/assembly/closure approval ratchet, a 21-component final SBOM,
and a CI source rebuild with semantic package comparison. The Terminal control
stores bullets rather than the secret, disables clipboard/context/undo paths,
and retains the real input only in a locked mutable target buffer until the
canonical Core handoff. Protected Unix output is flushed in an owner-only
temporary and atomically installed without overwrite; cancellation before that
commit removes the temporary. English/Arabic resources cover visible outcomes,
while the UI and operator docs continue to disclose unverified screen-reader,
braille, RTL shaping, scrollback, and post-Core managed-heap erasure rather than
claiming parity.

The isolated YamlDotNet `18.1.0` result and independent dependency/IP verdict
support only bounded parser-event and representation/syntax-tree parsing.
Generic object deserialization, polymorphic or dynamic construction,
serializer/emitter use, naming-convention policy, and wire or validation
authority remain prohibited. Product references remain gated on the corrected
C1 Red disposition and exact graph/content revalidation.

C1 Red pins fourteen independent matrices: key shape; alias/anchor/tag;
scalar parity; document shape; parser ceilings; directory escape; links/cycles;
deterministic TOCTOU; conflict/order; cancellation/partial output; smuggling;
canonical convergence; zero-value failure; and unknown profiles. The exact
future public seam is `SetupCompositionCompiler`, `SetupCompositionLimits`,
typed source/result/failure contracts, and a directory snapshot/commit barrier;
tests must not implement a mirror parser, merger, filesystem policy, serializer,
or canonicalizer.

Its positive defaults are exact: 4,194,304 aggregate source bytes; one YAML
document; 131,072 parser events; 65,536 normalized or aggregate directory
nodes; depth 32; 4,096 mapping and sequence entries per container; 65,536
characters per scalar; 1,048,576 aggregate scalar characters; 256 directories;
1,024 files; 256 entries per directory; 512 relative-path characters; path
depth 16; 524,288 bytes per file; and 4,194,304 aggregate directory bytes.
Checked tests accept each limit and reject `limit + 1`.

The Phase 8 Worst Break combines a post-open entry mutation into a link or
changed file with a ceiling alias/parser bomb at the exact publication barrier.
Deterministic discovery/open/read/revalidation/cancellation/commit barriers must
produce one stable value-free failure and no model, bytes, digest, coverage,
metric value, partial file, or retained handle. No sleep, polling, path-only
precheck, or mocked filesystem identity counts as evidence.

C1 Green is Core-only and must close all matrices through one normalized model
and existing canonical authorities. Linux claims require real-filesystem
evidence; Windows directory composition remains disabled absent equivalent
Windows-runner handle/reparse evidence. C2 is scale-only after C1 Green: it
measures small/medium/large/ceiling profiles while preserving the C1 defaults
and enables no larger profile without client and target-server evidence.

Final ownership is exact. C1 Red owns only the two new files
`tests/Event.Setup.Core.Tests/SetupCompositionInvariantTests.cs` and
`tests/Event.Setup.Core.Tests/SetupCompositionTestContract.cs`. C1 Green treats
`Directory.Packages.props` as a read-only verified input and owns only:

- `src/Event.Setup.Core/Event.Setup.Core.csproj` and `packages.lock.json`;
- `SetupCompositionContracts.cs`, `SetupCompositionLimits.cs`,
  `SetupCompositionCompiler.cs`, `SetupCompositionYamlParser.cs`,
  `SetupCompositionDirectoryReader.cs`, and `SetupCompositionNormalizer.cs`
  under `src/Event.Setup.Core/Composition/`;
- `tests/Event.Setup.Core.Tests/packages.lock.json`;
- new `docs/SETUP_COMPOSITION.md`; and
- new `docs/internal/releases/changes/CHG-01M1C8MP8S1T10N8D3D5A7B9CX.yaml`.

C1 Red tests are verification inputs, never Green commit paths. The fragment's
`Change-Id` matches the literal commit footer. C2 owns only these new files:
`SetupCompositionScaleProfile.cs`, `SetupCompositionScaleTests.cs`, the
controlled `phase8-scale-results.md`,
`eng/setup-assistant/GenerateSetupCompositionScaleProfiles.cs`, generated
`composition-scale-profiles.json`, and `docs/SETUP_COMPOSITION_SCALE.md`.

Commit copy is literal. C1 Red uses title
`test(self-hosting): lock bounded setup composition invariants`, its two bound
description paragraphs, `Changelog: skip`, and
`Changelog-Reason: test-only security contract for unimplemented setup composition`.
C1 Green uses `feat(self-hosting): add bounded setup composition`, its two bound
description paragraphs, and
`Change-Id: CHG-01M1C8MP8S1T10N8D3D5A7B9CX`, matching the exact public change
fragment. C2 uses
`perf(self-hosting): record setup composition scale profiles`, its two bound
description paragraphs, `Changelog: skip`, and
`Changelog-Reason: measurement-only governance with unchanged canonical composition defaults`.
All three bind `Message override: Not overridden`.

Index and full diff inspection precede edits or staging. Any path with another
contributor's hunk blocks the slice until coordinated, separately committed, or
clean. Only wholly owned explicit paths may be staged, followed by exact commit
file-list and content-hash verification. Material divergence requires literal
ledger replacement and re-review. Actual commit execution remains outside
I-VSD and requires the active agent's explicit git authority.

### IVSD-F038 — Every Live And Migrated Record Retains Tenant Authority

A source tenant ID, profile, receipt, object ID, or human label is correlation
evidence only. The target server reauthorizes the actor, target, tenant,
category, and action for every enrollment, apply, transfer, resume, and
promotion. Durable mappings are tenant-qualified and immutable. Any missing,
ambiguous, or conflicting lineage pauses without write; no instance
administrator fallback silently acquires tenant or merchant authority.

### IVSD-F039 — Authorization Is Fresh, Scoped, And Replay-Fenced

Setup may display server HAL affordances but cannot invent authority from a
previous response. Enrollment and operation capabilities are short-lived,
header-only, target-qualified, revocable, and bound to request identity and
idempotency. Saved profiles hold only protected revocable handles. Expiry,
revocation, stale HAL, retry, cancellation, and duplicate delivery produce a
value-free durable state rather than a second effect or local rollback claim.

### IVSD-F040 — Provider Bindings Are Write-Only And Coordinate-Free

The expanded scope does not supersede IVSD-F020's raw-value prohibition.
Setup may submit a new human-entered value to a server-authorized write or ask
for value-free readiness, but it never retrieves an existing value, enumerates
provider paths/projects/environments, receives provider credentials, or uses a
provider SDK directly. Target-local opaque binding identifiers remain outside
portable artifacts and grant no authority. Any unavailable allowlist,
provider, or policy response fails closed without fallback to environment or
plaintext storage.

### IVSD-F041 — Application Data Creates A Separate Custody Contract

Events, users, registrations, orders, tickets, files, and their PII cannot ride
inside configuration artifacts. Before a category moves, the operator sees its
purpose, source and target custodians, data classes, compatibility blockers,
retention and source-retention behavior, privacy/erasure authority, and
failure consequences. Protected staging is purpose-limited and bounded. The
existing authority-first erasure fact, anti-resurrection fence, canonical PII
paths, and payload-free evidence remain authoritative across source, staging,
and target; migration cannot restore erased data or bypass a pending erasure.

### IVSD-F042 — Resume Must Be Durable And Idempotent

A resumable label is truthful only when category selection, source identity,
target mapping, integrity digest, checkpoint generation, idempotency key, and
receipt survive interruption and concurrent retry. Commit and side effects
use the server transaction/outbox boundary. A conflicting mapping, digest,
checkpoint, or unknown effect pauses for reconciliation. Retry resumes the
same plan; it does not create a best-effort replacement plan or duplicate
aggregate.

### IVSD-F043 — Money Requires Provider And Ledger Reconciliation

Payment migration is a separate sovereign state machine, never inferred from
configuration or ordinary data copies. The current repository baseline resolves
safe defaults: `OrganizerDirect` remains the only active profile; organizer
recipient and currency snapshots remain immutable; payout authority remains
with the organizer/provider rather than Setup; partial refunds use accepted
line allocations; and provider acceptance is pending until reconciliation.
Before SA-1110, Tier 0 intake must bind hold-expiration/finalization race
precedence, payout routing, and partial-refund/fee allocation to those current
contracts or disable the sovereign slice. Before SA-1140, exact target/provider
identities, ledger totals, amounts, currencies, recipients, idempotency,
refund capacity, unknown outcomes, and approval actors require executable
reconciliation evidence. Conflict pauses with no money mutation.

This is not a conclusion about riba, halal status, financial regulation, or
legal liability. Those conclusions remain with qualified scholarly, legal,
provider, and deployment authorities.

### IVSD-F044 — Migration Preserves Source State And Exit Choice

Direct transfer and application migration copy through explicit, category-
selectable plans. They never delete or disable source state as an implicit
success step. Source retention continues until independently governed expiry,
erasure, or operator action after target integrity and recovery evidence are
available. Offline export/import remains usable where technically applicable,
and revoking enrollment does not make an operator's canonical source artifact
unusable.

### IVSD-F045 — Recovery Evidence Must Match Authoritative State

Setup distinguishes uploaded, staged, validated, mapped, committed, effects
pending, provider pending, reconciled, compensated, failed, cancelled, and
unknown. A local request, accepted API call, chunk completion, or provider
handoff is not completion. Receipts contain stable plan/category/checkpoint and
integrity evidence but no secret, PII, provider coordinate, raw exception, or
false rollback promise. Only authoritative server/provider reconciliation can
advance the corresponding completion claim.

### IVSD-F046 — Live And Migration Actions Preserve Human Agency

Enrollment, target/tenant selection, category selection, secret write, apply,
direct-transfer approval, payment handoff, compensation, retry after conflict,
and source deletion are distinct approvals. No default bundles all categories
or treats a prior configuration approval as consent to data or money movement.
Agents and automation may prepare non-secret plans and bounded diffs, but every
live or mutating authority transition remains an explicit human action exposed
by current server HAL policy.

## Recommendations

### Decisive Product Direction

Create one product named **ISLAMU Event Setup Assistant** with six project
boundaries:

```text
src/Event.Setup.Core/
src/Event.SetupAssistant/
src/Event.SetupAssistant.Desktop/
src/Event.SetupAssistant.Browser/
src/Event.SetupAssistant.Cli/
src/Event.SetupAssistant.Terminal/
```

- Existing `src/Event.Wire.Contracts/` is the package-free inner contract
  dependency reused by the five new projects and server static validation; it
  is not a sixth Setup product executable.
- `Event.Setup.Core` is pure, deterministic, headless, trim/AOT-friendly, and
  contains no network, persistence, provider SDK, UI, or secret storage.
- Successor A made `Event.SetupAssistant.Cli` functional with stable commands,
  versioned machine output, and a historical BCL human wizard. Phase 5R must
  delete that wizard and retain the CLI as machine/noninteractive only.
- Successor A created `Event.SetupAssistant`, Desktop, and Browser only as
  package-free, disabled, non-shipped contract shells. They are not UI,
  runtime-target, accessibility, support, or release evidence.
- B1 binds CommunityToolkit.Mvvm `8.4.2` and Microsoft DI `10.0.10` plus
  Abstractions `10.0.10` to their already approved roles. Avalonia `12.1.1`
  remains disabled. Phase 5R may build only the separately named
  `ISLAMU.Terminal.Gui` `2.4.17-islamu.1` package from official commit
  `d0a0ed9b150d3fc8aacf4ab07b7f7d91264fe6d6`, with the grammar/editor
  integration removed, MIT attribution preserved, and the final patched
  closure independently gated. `Event.SetupAssistant` remains the
  CommunityToolkit-only human ViewModel/message owner; DI remains
  target-root-only. Browser/Desktop and Terminal own secret sessions outside
  shared state and DI; machine CLI/Core remain presentation-free. This report
  permits the B1 plan to progress to its next
  review gates only and does not approve a probe, package, implementation,
  target, support claim, release, or shipping.

The BCL terminal wizard and future successor-B human adapters consume the same
workflow contracts; none owns validation, readiness, sensitivity, or rendering
truth. Session-bounded messages never carry secrets or replace direct
parent/child composition. Tests mirror each project boundary. Release and packaging implementation belongs under `eng/`,
not `.ci/scripts/`; CI discovery and release adapters remain under the
repository’s established CI/CD paths.

### Product Workspaces

#### Configuration Portability Workspace

- create, open, edit, validate, normalize, and export
  `ConfigurationManifest`;
- create, open, edit, validate, and export
  `TenantConfigurationPackage`;
- section tree, typed fields, deterministic JSON preview, diff, coverage
  ledger, and documentation links;
- no secret fields or secret-reference values;
- offline static validation only;
- future live manifest operations through authorized APIs and HAL, never by
  embedding access tokens in the client.

#### Environment Setup Workspace

- choose deployment topology;
- choose database and infrastructure providers;
- choose optional capabilities;
- ask only relevant non-secret variables;
- classify secret requirements;
- select no-secret or secret-entry mode;
- render a deterministic, relevant-only `.env`;
- show redacted readiness and omissions;
- save/download locally.

The two workspaces may share a non-secret setup profile, but no secret value.

#### Legal Documents Workspace

- choose instance or tenant authority;
- choose document kind, audience, language, jurisdiction, and lifecycle;
- start from a governed template or blank source;
- edit constrained Markdown with outline, preview, and accessibility checks;
- resolve typed identity/contact placeholders;
- compare locales, templates, source, and target;
- validate links without making network requests during secret mode;
- export legal source and metadata into the correct manifest/package section;
- generate a publication/readiness checklist;
- never publish, obtain acceptance, or provide legal approval from the offline
  editor.

#### Machine CLI And Terminal.Gui Workspace

- `Event.SetupAssistant.Terminal` opens the sole human Terminal.Gui
  experience;
- direct commands expose every static validation, render, diff, catalogue,
  legal-template, and readiness workflow;
- terminal navigation, validation, and generated bytes use the same Core as
  future successor-B GUI adapters;
- machine mode never emits terminal control sequences;
- no-secret operation is the default;
- terminal secret entry is local, interactive, masked, and never echoed;
- agents and scripts use commands, not interactive terminal automation.

### Target Architecture

| Target | Shared UI/core | Secret mode | File behavior | Network |
|---|---|---|---|---|
| Successor-B browser default | Framework-neutral outcome; graph pending | Disabled | Local download or supported picker | Static asset load only |
| Successor-B browser optional | Framework-neutral outcome; graph pending | Explicit opt-in each session | Local download; permissions not enforceable | No request after secret mode begins |
| Successor-B Windows desktop | Framework-neutral outcome; graph pending | Available after approval | Native picker, user-only ACL, atomic write | None by default |
| Successor-B Linux desktop | Framework-neutral outcome; graph pending | Available after approval | Native picker, owner-only mode, atomic write | None by default |
| Successor-B macOS desktop | Framework-neutral outcome; graph pending | Available after approval | Native picker, owner-only mode, sandbox-aware access | None by default |
| CLI machine mode | Core only | No-secret only initially | Explicit path or stdout for non-secret artifacts | None |
| Terminal.Gui target | Core plus terminal adapter | Interactive TTY only | Protected file output | None |

### Functional Parity Contract

| Capability | Successor-B GUI/browser/desktop | Terminal.Gui target | CLI machine mode |
|---|---:|---:|---:|
| Browse/explain catalogue | Yes | Yes | Yes |
| Manifest/package create and validate | Yes | Yes | Yes |
| Deterministic format/render | Yes | Yes | Yes |
| Semantic diff and coverage | Yes | Yes | Yes |
| Relevant-only no-secret dotenv | Yes | Yes | Yes |
| Interactive secret completion | Optional web/Desktop | TTY only | No |
| Legal template selection | Yes | Yes | Yes |
| Markdown editing | Rich editor | Terminal editor | File-based validate/render |
| Accessibility/RTL evidence | Per target | Separate terminal matrix | Machine output |
| Agent automation | No UI automation | No terminal-UI automation | Versioned JSON commands |

Parity is asserted only when the same core inputs produce byte-identical
artifacts and equivalent closed diagnostics.

### Versioned Command And Machine-Output Contract

Recommended command families:

```text
event-setup catalog list|explain
event-setup manifest new|validate|format|diff|coverage|export
event-setup tenant-package new|validate|format|diff|coverage|export
event-setup env plan|render|validate|explain
event-setup legal template-list|new|validate|render|diff
event-setup doctor
```

Planning owns final names, but once shipped they require a versioning policy.
Every command supports:

- help;
- deterministic noninteractive operation;
- explicit input/output paths;
- dry-run where a write is possible;
- text output for humans;
- one JSON object for machine mode;
- stable exit categories;
- bounded diagnostics;
- artifact digest and schema version;
- no secret values.

Machine output should contain:

```text
schemaVersion
command
status
diagnostics[] { code, severity, path }
artifacts[] { kind, path, digest, sensitivity }
coverage
readiness
```

It must not contain localized prose as authority, raw exceptions, terminal
escapes, stack traces, or entered values.

### Terminal.Gui Secret-Safety Contract

The CLI rejects secret values supplied through arguments, option values,
process environment, or captured standard input. Initial supported paths are:

- no-secret machine commands;
- interactive Terminal.Gui/TTY secret entry;
- direct protected file output.

Secret terminal-wizard requirements:

- real TTY required;
- masked, non-echoing controls;
- no value in terminal title, status bar, accessible label, scrollback, or
  clipboard by default;
- no stdout/stderr secret output;
- no `--output -` when secret mode is active;
- no persistent wizard history or autosave;
- clear state on cancel, completion, suspension, terminal resize failure, and
  process signal where safely possible;
- explicit warning that terminal recorders, multiplexers, extensions,
  accessibility tools, and compromised local systems remain trust boundaries.

### Setup Assistant Agentic Skill

After the command contract is implemented, create:

```text
.agents/skills/setup-assistant-cli/SKILL.md
.agents/skills/setup-assistant-cli/resources/command-contract.md
.agents/skills/setup-assistant-cli/resources/secret-safety.md
.agents/skills/setup-assistant-cli/resources/tui-guide.md
.agents/skills/setup-assistant-cli/resources/workflows.md
```

The skill should load for generating, validating, diffing, or explaining
manifests, tenant packages, relevant-only dotenv files, and legal-document
bundles through the Setup Assistant CLI. It should exclude implementation of
the CLI itself and any request to ingest secret values.

The terminal guide may teach a user how to navigate the human interface,
explain prompts, and select safe modes. Agent execution still uses machine
commands rather than terminal-screen automation.

The skill workflow should:

1. verify CLI/version compatibility;
2. select no-secret and dry-run behavior;
3. request machine JSON;
4. validate source artifacts;
5. generate a draft or diff;
6. summarize diagnostics without values;
7. obtain explicit approval before non-secret file writes;
8. validate the final artifact and digest;
9. hand secret completion to the local human UI.

### No Embedded AI Boundary

The product exposes deterministic commands and schemas only. It does not
embed, recommend, proxy, or configure an AI provider. The external agent owns
its model and invocation environment; the Setup Assistant remains useful and
complete without AI.

This separation prevents:

- an AI dependency in every self-hosted build;
- hidden prompt/data transmission;
- model-provider lock-in;
- AI API keys in `.env`;
- nondeterministic validation;
- claims that agent suggestions are legally or operationally authoritative.

### Default No-Secret Web Mode

The start page should make no-secret mode the primary action:

```text
Generate without entering secrets (Recommended)
```

The workflow:

1. loads the complete static app;
2. asks topology and capability questions;
3. asks relevant non-secret values;
4. identifies required secret keys without requesting their values;
5. generates relevant empty placeholders;
6. omits unrelated keys and intentionally defaulted variables;
7. shows `Incomplete until required secret values are supplied`;
8. downloads the file locally;
9. persists no form data; explicit clear/page close ends the product session,
   while the documented browser/OS memory limitations remain.

This mode still requires the same no-telemetry and local-only design. It simply
reduces the sensitivity of entered data.

### Optional Web Secret Mode

Secret mode requires a separate trust interstitial every session:

- exact official origin;
- release version and digest;
- source link;
- statement that this release is designed to send no secret values;
- statement that the hosting origin remains trusted to deliver the code;
- browser-extension/local-device warning;
- desktop recommendation;
- acknowledgment that browser download permissions cannot be enforced;
- explicit `Continue with secret entry` action;
- equal, prominent return to no-secret mode.

Secret mode must not be activated by query string, local preference, browser
storage, deep link, or remembered choice.

After activation:

- all resources are already loaded;
- network is denied;
- documentation/update/source links require clearing secrets first;
- mode change clears all secret state;
- inactivity expiry warns and then clears state;
- download or cancellation clears state;
- browser back/forward does not restore values.

### Browser Network Denial Contract

The exact production CSP must be derived and tested against successor B's
approved, pinned browser framework/runtime graph. Its policy intent is:

- same-origin, content-addressed framework/app assets only;
- no connections;
- no form submission;
- no framing;
- no objects;
- no remote images, fonts, styles, media, or manifests;
- no inline event handlers;
- only the minimum WebAssembly execution permission required by the runtime;
- no reporting endpoint.

Required controls:

- CSP response header plus an early meta fallback where compatible;
- `frame-ancestors 'none'` as a response header;
- SRI/integrity evidence for supported static script/style entrypoints;
- HTTPS and HSTS on the official origin;
- no service worker in the initial secret-capable release;
- no PWA background sync;
- no CDN;
- no dynamic import from remote URLs;
- no third-party JavaScript;
- no external CSS, fonts, icons, or images;
- no analytics or consent manager;
- no browser error-reporting endpoint;
- automated browser network recording from secret-mode entry through clear.

Initial asset requests happen before secret entry and therefore are not “zero
requests for the page.” The exact promise is zero requests after secret mode
starts and zero secret value in any request at any time.

### Relevant-Only Dotenv Rendering

The output algorithm is:

1. Resolve selected deployment profile and capabilities.
2. Include required non-secret keys that have no safe implicit default.
3. Include user-overridden non-secret defaults.
4. Omit unchanged values intentionally supplied by runtime defaults.
5. Include required secret keys for selected capabilities.
6. In no-secret mode, render those secret values empty.
7. In secret mode, render entered or locally generated values.
8. Include optional keys only when the user selected the associated feature or
   explicitly chose advanced inclusion.
9. Sort deterministically by deployment phase, category, and canonical key.
10. Render a redacted coverage/readiness report separately.

The generated header must communicate this meaning:

```dotenv
# Generated by ISLAMU Event Setup Assistant.
# This file intentionally contains only variables relevant to the selected
# deployment and features. Supported variables not shown here use documented
# defaults or belong to features you did not select.
# See the canonical configuration documentation for the complete catalogue.
```

The prose may evolve and must not be pinned by tests. Tests should assert
machine-consumed classifications, key inclusion/omission, deterministic
ordering, and safe rendering.

Relevant empty secret example:

```dotenv
# Required before startup for the selected authentication profile.
KEYCLOAK_BLAZOR_CLIENT_SECRET=
```

No fake value such as `change-me`, `password`, or a copied example credential
is permitted.

### Dotenv Format Safety

The renderer must define one explicit dialect compatible with the supported
ISLAMU Event deployment command. It must test:

- empty values;
- leading/trailing whitespace;
- `#`;
- quotes;
- backslashes;
- dollar signs and Compose interpolation;
- multiline values;
- Unicode and normalization;
- CRLF/LF;
- duplicate keys;
- invalid key names;
- comments;
- values that resemble commands;
- round-trip parse/render behavior.

The tool generates data; it never executes a generated value or shells it into
a command.

### Legal Document Configuration Model

Use a first-class typed `LegalDocumentBundle` rather than arbitrary JSON. Each
entry should carry:

- stable document kind;
- owner scope (`Instance` or `Tenant`);
- language tag;
- audience;
- title and optional short summary;
- constrained Markdown source;
- content digest;
- lifecycle intent;
- effective date when proposed;
- whether fresh acceptance is required;
- accountable identity revision or manifest-local identity reference;
- template ID/version/provenance;
- jurisdiction assumptions;
- superseded source version reference when known;
- change summary;
- typed placeholders and completeness state.

Recommended lifecycle:

```text
Draft -> ReviewRequired -> Approved -> Scheduled -> Published -> Retired
```

The manifest/package expresses desired legal configuration. Canonical Domain
mutation creates immutable target versions and acceptance requirements.
Published/retired history remains persisted evidence outside portable
configuration.

### Legal Document Kind Catalogue

Candidate instance-owned kinds:

- platform terms of service;
- instance privacy notice;
- cookie notice/policy;
- acceptable-use policy;
- community/content rules;
- moderation, reporting, appeal, and correction policy;
- accessibility statement;
- legal notice/imprint;
- security and vulnerability disclosure;
- retention, erasure, and portability notice;
- subprocessors/service-provider disclosure;
- open-source/license and attribution notice;
- API/developer terms;
- federation/ATProto disclosure;
- platform payment-operation notice;
- platform fee/contribution notice;
- complaint, refund, dispute, and reconciliation responsibilities;
- service availability, support, EOL, and migration notice.

Candidate tenant-owned kinds:

- tenant/directory terms;
- tenant privacy/controller notice;
- tenant cookie additions;
- local code of conduct/community rules;
- organizer/event-submission terms;
- event publication and moderation policy;
- cancellation/refund baseline;
- registration/participant privacy notice;
- media/photography consent information;
- safeguarding/minor-participation policy;
- venue/accessibility information policy;
- complaint/correction/copyright contact policy;
- sponsorship/partner disclosure;
- local retention and contact-sharing notice.

The catalogue is closed and typed. Adding a kind requires an accountable owner,
public rendering location, scope, lifecycle, validation, portability,
acceptance, and legal-review decision.

### Instance And Tenant Composition

Public legal navigation should present role-labeled documents:

- `Platform operator`;
- `Directory operator`;
- `Organizer/Merchant` when applicable.

Tenant legal text is additive within its authority. It cannot hide required
instance documents. Instance governance may require specific tenant document
kinds or minimum disclosures, but should not silently write factual tenant
claims. Missing required documents remove affected HAL capabilities or make
activation/readiness fail closed with bounded repair guidance.

### Legal Template Library

Template packs must be:

- project-authored or independently licensed under an approved FOSS license
  compatible with the target distribution;
- bundled locally;
- versioned and immutable after release;
- source- and license-attributed;
- scoped by role, document kind, language, and jurisdiction assumptions;
- composed from typed placeholders;
- accompanied by completeness rules;
- reviewed for accessibility and plain language;
- clearly non-certifying.

External legal prose must not be copied into the repository merely because it
is publicly visible. New templates require clean-room provenance and qualified
legal review. A signed future template-pack update is a separate network and
supply-chain feature; the first release uses only bundled templates.

### Safe Markdown Editor

The editor should provide:

- source, structured outline, and sanitized preview;
- keyboard-complete formatting commands;
- heading-order and link-text diagnostics;
- typed placeholder insertion rather than freehand magic strings;
- unresolved-placeholder panel;
- locale comparison and missing-translation indicators;
- source/preview cursor coordination where accessible;
- word/byte/link/heading counts;
- deterministic formatter;
- change diff and summary;
- safe undo/redo contained in process memory;
- local file open/save;
- template reset without silent data loss;
- export-readiness and publication-readiness as distinct results.

It must not provide:

- raw HTML mode;
- embedded browser content;
- remote image preview;
- arbitrary plugins;
- executable macros;
- network spellcheck/grammar/legal review;
- AI-generated legal text without a separate approved workstream;
- auto-publication.

Any Markdown parser/editor dependency must independently pass the FOSS and
target-outbound compatibility gate.

### Legal Content Quality-Of-Life Improvements

- template comparison;
- clause outline and navigation;
- required-section checklist;
- operator/tenant identity placeholder binding;
- contact and jurisdiction consistency checks;
- source-origin link detector;
- broken relative-link detector;
- safe scheme validation;
- language and RTL preview;
- accessible plain-text export;
- Markdown/PDF/HTML publication preview, with PDF/HTML generation added only
  after dependency review;
- locale completeness dashboard;
- stale legal-review reminder;
- effective-date scheduler preview;
- acceptance-impact warning;
- changelog generation;
- previous-version diff;
- target-instance migration review;
- document-kind coverage ledger;
- publication and notification checklist;
- counsel-review status and evidence reference;
- public footer/navigation preview;
- machine-readable manifest/package export.

### Secret Generation

Only approved secret classes may be generated locally. Every generator
declares:

- required entropy;
- byte length;
- encoding;
- prefix/version when required;
- target variable;
- whether the value is accepted by the target system;
- rotation/recovery documentation.

Use platform cryptographic random APIs. Never use timestamps, GUIDs, ordinary
pseudorandom generators, human words, or one shared value for unrelated keys.
Provider-issued credentials remain provider-issued and are requested from the
user; the assistant does not fabricate or verify them online.

### Desktop File-Security Contract

Before writing:

- display exact target path;
- detect existing file, directory, symlink, reparse point, or special file;
- show a redacted key-level diff;
- require explicit overwrite;
- avoid following links;
- create in the destination directory;
- apply restrictive access before secret content is exposed where the platform
  permits;
- flush and atomically replace;
- verify final permissions and ownership;
- delete incomplete temporary output on failure;
- emit only a closed local error code.

The application does not automatically retain:

- plaintext backup;
- recent-file secret content;
- autosave draft;
- restore file;
- clipboard copy;
- crash attachment.

A non-secret profile may retain feature selections and non-secret values only
after explicit user choice.

### Desktop Distribution And Packaging

#### Windows

| Artifact | Architecture | Initial recommendation |
|---|---|---|
| Portable `.zip` | `win-x64`, `win-arm64` | Required |
| Signed installer/package | `win-x64`, `win-arm64` | Add after FOSS/tooling compatibility review |
| `win-x86` | x86 | Optional only if demand and framework support justify it |

Every executable/package is signed, checksum-published, and smoke-tested on a
clean supported Windows environment.

#### Linux

| Artifact | Architecture/distro role | Recommendation |
|---|---|---|
| `.tar.gz` | `linux-x64`, `linux-arm64` | Baseline portable release |
| `.deb` | Debian/Ubuntu families | Required |
| `.rpm` | Fedora/RHEL/openSUSE families | Required after packaging validation |
| `.pkg.tar.zst` or reviewed PKGBUILD | Arch/Manjaro families | Required for the requested Arch path |
| AppImage | Broad desktop convenience | Optional after complete license/tool review |
| Flatpak | Sandboxed desktop distribution | Optional after portal, permission, manifest, and license review |

Use portable non-version-specific RIDs. Package format does not replace testing
on representative distributions. Initial Linux backend should use the stable
supported path; experimental Wayland-only behavior is not the default.

#### macOS

| Artifact | Architecture | Recommendation |
|---|---|---|
| Signed/notarized `.app` in `.zip` | `osx-x64`, `osx-arm64` | Required |
| Signed/notarized `.dmg` | x64/arm64 or universal | Recommended after release pipeline is stable |

Use hardened runtime with only required entitlements, user-selected file
access, signing, notarization, and staple verification. No network entitlement
should be requested for an offline-only desktop release unless a later
approved feature needs it.

#### Browser

Publish a static, immutable, content-addressed bundle with:

- checksum manifest;
- SBOM;
- source revision;
- integrity metadata;
- CSP header configuration;
- deploy receipt;
- official origin identity;
- archived release bundle for independent verification.

#### CLI/BCL Terminal Wizard

Publish `event-setup` as:

- self-contained `win-x64`, `win-arm64`, `linux-x64`, `linux-arm64`,
  `osx-x64`, and `osx-arm64` executables;
- part of each desktop archive/package;
- an optional framework-dependent .NET global tool after package/license
  review;
- checksum/SBOM/signature-bound artifacts using the same release identity.

Shell-completion definitions may contain command/option names only. They must
never persist paths, recent values, generated keys, or secret-bearing history.

### FOSS Philosophy And Outbound Compatibility Gate

For successor A and every later target:

1. enforce A's selected BCL plus package-free `Event.Wire.Contracts` product
   graph; do not pin, reference, restore, lock, vendor, or publish Terminal.Gui,
   Avalonia, a replacement TUI/GUI package, or an exception for A;
2. keep A's presentation/Browser/Desktop shells package-free, disabled, and
   non-shipped;
3. require successor B to select a provenance-complete GUI graph or supply new
   publisher-authoritative evidence before any shell activation;
4. restore every selected target RID with lock files;
5. inspect direct, transitive, native, build, test, asset, font, icon,
   template, and packaging dependencies;
6. map every shipped binary/component to provenance, applicable license,
   notices, patent/source/source-offer duties, publish role, and target;
7. determine whether the component preserves all outbound paths or makes one
   executable explicitly AGPL-only;
8. produce an SBOM and notices/source-offer evidence for each target;
9. run locked vulnerability and repository dependency-license validation;
10. have a human compare release artifacts to lock/SBOM/publish-exclusion
    evidence;
11. block unknown, commercial, proprietary, source-available,
    obligation-incompatible, provenance-incomplete, or exclusion-unproved
    components; and
12. record OS-provided libraries and signing/notarization services separately.

GPLv3 and AGPLv3 components may be philosophically acceptable and can be
compatible with an AGPL public executable. They are not automatically
compatible with the Project Steward’s alternative outbound paths. Any such
dependency requires a target-specific legal decision and documented
distribution boundary.

### Official Web Trust And Provenance

The official service should expose:

- operator public/legal identity;
- official domain;
- privacy and security statement;
- release version, commit, and artifact digest;
- link to the exact source revision;
- reproducible-build status;
- date and scope of last independent security review;
- no-secret default;
- desktop/offline alternatives;
- vulnerability-reporting route;
- incident notice policy.

Marketing and UI must distinguish:

- **Designed local processing:** implementation property of an identified
  release;
- **Official hosting commitment:** organizational/legal promise;
- **Origin trust:** unavoidable online delivery trust;
- **Desktop/offline verification:** stronger user-controlled delivery path.

### Gitignore And Hosting Exclusivity Decision

Recommended:

```text
Track:
  src/Event.SetupAssistant.Browser/
  CSP and release configuration
  browser tests
  reproducibility metadata

Ignore:
  bin/
  obj/
  artifacts/
  generated publish/wwwroot output
```

Not recommended:

- gitignore browser source;
- private browser-only implementation;
- claim that hidden source prevents malicious hosting;
- special official binary with secret behavior absent from public source.

This is the approved direction. The security and licensing benefits of
auditable source outweigh the ineffective exclusivity claim.

### Accessibility And Localization

Both targets share:

- semantic names and descriptions;
- keyboard access;
- predictable focus;
- one error summary plus field errors;
- status announcements;
- non-color state;
- 200% text scaling and reflow;
- contrast and target-size requirements;
- reduced motion;
- LTR/RTL parity;
- bundled translations;
- plain-language and expert explanations.

Secret values must never be placed in accessible names, announcements, or
error text.

### Expanded Capabilities And Separate Approval Gates

Successor A's corrected CTO and user approvals remain bound to its exact Green
BCL-only revision. This planning revalidation grants no approval or
implementation authority to successor B or later work: fresh revision-bound
CTO review and exact-revision user approval are mandatory before SA-510 package
or shell activation. No later successor inherits approval. The expanded
capabilities remain gated as follows:

- final-governance C1 Red may start under IVSD-F037/M037 only after a fresh CTO
  confirmation binds the final revision; the standing scope direction then needs no new
  user prompt. C1 Green is conditional on one attributable absent-owner Red,
  fourteen independently passing matrices, exact owned-path/commit closure, and
  exact graph/role revalidation. C2 follows only C1 Green, changes no default,
  and enables no profile without measured client and target-server evidence;
- live enrollment, optional protected handles, and target-local provider writes
  may start only after SA-910's fresh Tier 1 adversarial boundary and the
  revision-bound CTO review;
- live apply and direct transfer remain target-authoritative, mutually approved,
  replay-fenced, resumable, source-retaining, and dependent on green or
  explicitly waived upstream ConfigurationManifest gates;
- application-data migration remains category-selectable and subject to the
  existing privacy-erasure authority, anti-resurrection, retention, and PII
  custody rules;
- SA-1110 cannot start until Tier 0 intake records hold/finalization precedence,
  OrganizerDirect payout routing, partial-refund/fee allocation, category
  custody, erasure ordering, and recovery evidence contracts;
- SA-1140 cannot start until exact payment/provider actors and coordinates are
  supplied through server authority, reconciliation evidence exists, and the
  explicit payment/provider decision gate is recorded; and
- missing operational, privacy, provider, legal, accessibility, security, or
  payment evidence disables only the affected capability and never authorizes
  a weaker fallback.

PWA/service worker, auto-update, runtime plugins or downloaded packs, mobile,
raw secret retrieval, direct provider SDK access from Setup, and destructive
source migration remain outside this revision.

## Mitigation Register

| Mitigation | Requirement | Findings |
|---|---|---|
| IVSD-M001 | Ship a focused Setup Assistant with separate manifest and environment workspaces | F001 |
| IVSD-M002 | Extract pure shared contracts, catalogue, validators, and renderers; keep runtime authority server-side | F002 |
| IVSD-M003 | Disclose origin trust; identify exact release; recommend desktop for higher assurance | F003 |
| IVSD-M004 | Default every browser session to no-secret mode; require explicit per-session opt-in | F004 |
| IVSD-M005 | Load first, then enforce and test zero requests after secret-mode entry with strict CSP and no reporters | F005 |
| IVSD-M006 | Forbid browser persistence and minimize DOM/memory/clipboard exposure; disclose extension/device limits | F006 |
| IVSD-M007 | Render empty placeholders only for relevant selected secrets and mark readiness incomplete | F007 |
| IVSD-M008 | Introduce one non-secret canonical environment catalogue that generates/validates every consumer | F008 |
| IVSD-M009 | Keep manifests/packages and `.env` separate in data model, UX, files, and sensitivity labels | F009 |
| IVSD-M010 | Use native picker, link refusal, restrictive permissions, atomic write, verification, and no plaintext backup | F010 |
| IVSD-M011 | Minimize secret copies and lifetime; never promise deterministic memory erasure | F011 |
| IVSD-M012 | Remove telemetry, remote logs, crash upload, CSP reports, update calls, and production developer tools | F012 |
| IVSD-M013 | Fail closed on incomplete provenance/notices or unproved exclusions; build the exact Steward-approved `ISLAMU.Terminal.Gui` package from pinned official source, preserve MIT attribution, verify the patched artifact's final closure/SBOM, fail CI if the grammar corpus returns, and migrate back to an approved modular upstream release | F013 |
| IVSD-M014 | Sign, attest, checksum, SBOM, archive, and identify every desktop/web release | F014 |
| IVSD-M015 | Treat package-free shells, shared models, probes, and ApprovedDisabled adapters as non-support evidence; publish a matrix only for independently activated and evidenced targets | F015 |
| IVSD-M016 | Track browser source and ignore generated output; reject source withholding as hosting protection | F016 |
| IVSD-M017 | Use official origin, legal identity, trademark, source/release provenance, and fork disclosure | F017 |
| IVSD-M018 | Meet WCAG 2.2 AA-aligned interaction and require rendered, keyboard, and real assistive-technology evidence independently for every Active target | F018 |
| IVSD-M019 | Bundle localization/RTL resources before secret mode and localize all security consequences | F019 |
| IVSD-M020 | Generate or accept new local values only; never retrieve live instance/Infisical secrets | F020 |
| IVSD-M021 | Govern claims to identified behavior/evidence and state remaining trust explicitly | F021 |
| IVSD-M022 | Keep probes non-shipping and run adversarial browser, desktop, terminal, packaging, component-provenance, publish-exclusion, license, accessibility, support, and recovery evidence gates | F022 |
| IVSD-M023 | Add typed, role-scoped legal-document bundles for instance and tenant authority | F023 |
| IVSD-M024 | Separate portable drafts/current source from immutable publication and acceptance evidence | F024 |
| IVSD-M025 | Govern project-owned or approved FOSS templates as non-certifying, versioned starting points | F025 |
| IVSD-M026 | Use one constrained Markdown parser/sanitizer across editor, server, public rendering, and packages | F026 |
| IVSD-M027 | Export owned legal source/metadata and import it as target-reviewed drafts or new versions | F027 |
| IVSD-M028 | Re-baseline bounded contract limits and legal-content diff UX | F028 |
| IVSD-M029 | Keep the deterministic machine CLI noninteractive and make the independently evidenced Terminal.Gui executable the sole human terminal and secret-completion target; retain no custom/BCL fallback | F029 |
| IVSD-M030 | Provide versioned JSON, exit categories, help, dry-run, digests, and bounded diagnostics | F030 |
| IVSD-M031 | Forbid argument, environment, captured-stdin, stdout, and machine-mode secret transport; allow secret completion only in the Terminal.Gui target's owned masked TTY session with protected direct file output | F031 |
| IVSD-M032 | Make the skill default to no-secret machine commands and prohibit agent access to secret-bearing files/values | F032 |
| IVSD-M033 | Keep model SDKs, providers, prompts, chat, inference, and agent loops outside the product | F033 |
| IVSD-M034 | Require human approval for writes, legal publication, live apply, and authority broadening | F034 |
| IVSD-M035 | Publish Terminal.Gui-specific keyboard, screen-reader, RTL, Unicode, resize, signal, scrollback, and small-terminal evidence and state unsupported environments truthfully | F035 |
| IVSD-M036 | Publish the operational skill only after the implemented CLI/version contract is verified | F036 |
| IVSD-M037 | Compile bounded JSON/YAML/directory sources offline through one normalized model and the existing canonical serializer; reject ambiguity, path/sensitivity smuggling, parser authority, partial output, and evidence-free scale | F037 |
| IVSD-M038 | Reauthorize exact target, tenant, category, and actor server-side; keep mappings tenant-qualified and fail closed on lineage conflict | F038 |
| IVSD-M039 | Use short-lived target-qualified HAL capabilities, protected revocable handles, request binding, expiry, and replay fencing for every live effect | F039 |
| IVSD-M040 | Permit only server-authorized write/readiness operations; prohibit raw readback, provider-coordinate disclosure, portable bindings, and provider SDK access from Setup | F040 |
| IVSD-M041 | Establish category-level custody, purpose, staging, retention, erasure, anti-resurrection, and PII-safe evidence before application-data movement | F041 |
| IVSD-M042 | Persist immutable plan scope, mappings, checkpoints, digests, idempotency, receipts, and outbox effects; reconcile conflicts before resume | F042 |
| IVSD-M043 | Separate sovereign operations and require Tier 0 decisions plus provider/ledger/recipient/currency/refund reconciliation before money mutation | F043 |
| IVSD-M044 | Retain source state, preserve offline exit paths, and require separate governed action for expiry, erasure, disablement, or deletion | F044 |
| IVSD-M045 | Expose authoritative granular states and value-free receipts; never claim completion, refund, rollback, or recovery before reconciliation | F045 |
| IVSD-M046 | Require distinct informed human approvals for enrollment, categories, writes, apply, transfer, payment, compensation, conflict retry, and deletion | F046 |

### Rejected Alternatives

1. **Desktop only.** Rejected because a no-install web path materially improves
   access, provided secret mode remains optional and transparent.
2. **Web only.** Rejected because hosted delivery and browser downloads cannot
   provide the desktop path’s stronger origin and file-permission controls.
3. **Secret entry enabled by default.** Rejected as unnecessary exposure and a
   dark default.
4. **No secret placeholders in safe mode.** Rejected because the output would
   not guide deployment completion.
5. **Generate every known environment variable.** Rejected because it creates
   noise, unsafe accidental activation, and an unmaintainable file.
6. **Write documented defaults explicitly.** Rejected by default because
   omitted values should continue to receive canonical runtime defaults; an
   advanced explicit-default export may be separately labeled.
7. **Combine manifest and `.env`.** Rejected because a shareable artifact would
   become secret-bearing.
8. **Server-side generation.** Rejected for secret mode because it would
   require transmitting user secrets.
9. **CSP reporting in secret mode.** Rejected because reports are outbound
   requests and can contain contextual data.
10. **Third-party analytics, fonts, or CDN.** Rejected because they add network
    and supply-chain paths to a secret-handling page.
11. **Service worker/PWA in the first release.** Rejected pending a separate
    cache/update threat model.
12. **Automatic plaintext backup.** Rejected because it creates another secret
    copy.
13. **Persist secret projects.** Rejected because convenience expands exposure
    and recovery obligations.
14. **Retrieve existing Infisical/server secrets.** Rejected because it changes
    the tool into a privileged secret client.
15. **Gitignore/withhold browser source.** Rejected as ineffective against
    phishing or reimplementation and harmful to auditability.
16. **Use Avalonia professional/commercial tooling to simplify packaging.**
    Rejected under the no-commercial-dependency policy.
17. **Pin Terminal.Gui, Avalonia, a partial graph, or a replacement package in
    successor A.** Rejected: Terminal.Gui's 24-package graph lacks complete
    grammar provenance/notices; Avalonia runtime component mapping and Remote
    Protocol exclusion remain unproved; A needs neither graph.
18. **Treat package-free presentation shells as functional or support
    evidence.** Rejected because only successor B owns GUI/runtime activation.
19. **Assume a primary FOSS license, package signature, or isolated resolved
    component makes a graph compatible.** Rejected until exact binary/component,
    notices, publish, vulnerability, and outbound obligations are proven.
20. **Claim “fully safe” or “we cannot access secrets.”** Rejected as
    technically unprovable for hosted code and compromised local devices.
21. **Keep static hard-coded legal pages.** Rejected because self-hosters and
    tenants need accountable, portable, localized operator-owned texts.
22. **Store arbitrary HTML.** Rejected because it introduces executable,
    tracking, sanitization, accessibility, and portability risk.
23. **Copy public legal templates.** Rejected by clean-room and license
    governance; templates require project-owned or approved-FOSS provenance and
    legal review.
24. **Auto-publish imported legal text.** Rejected because target identity,
    jurisdiction, acceptance, and effective-date authority must be reviewed.
25. **Migrate acceptance history in configuration.** Rejected because
    acceptance is immutable application evidence, not portable configuration.
26. **Embed an AI assistant.** Rejected because deterministic setup needs no
    model/provider dependency or secret-bearing inference path.
27. **Have agents drive the interactive terminal wizard.** Rejected because
    terminal state is unstable and machine commands are safer and testable.
28. **Pass secrets in CLI arguments or captured stdin.** Rejected because
    process listings, shell history, and tool logs can disclose them.
29. **Publish the skill before the CLI exists.** Rejected because it would
    teach fictional commands and unsafe assumptions.
30. **Treat YAML as a second wire format or merge directories implicitly.**
    Rejected because source syntax and ordering would become competing authority.
31. **Store long-lived bearer tokens or provider coordinates in profiles.**
    Rejected because profile convenience would create extraction, replay, and
    cross-target reconnaissance risk.
32. **Read provider secrets back to verify migration.** Rejected; readiness and
    write outcomes remain value-free and server-authoritative.
33. **Copy application tables or databases directly.** Rejected because it
    bypasses tenant authorization, aggregate invariants, erasure fences,
    mappings, idempotency, and provider truth.
34. **Delete source data after target promotion.** Rejected as an implicit
    transfer effect; source retention, expiry, erasure, and deletion remain
    separately governed decisions.
35. **Reconstruct payment/refund state from configuration or copied rows.**
    Rejected because only reconciled provider and ledger evidence can authorize
    sovereign state.
36. **Display request acceptance as migration/refund completion.** Rejected
    because pending, unknown, and compensating states require truthful recovery.

## Common Overlooked Failures And Outcomes

Feature type: cross-platform configuration, legal-content authoring, and
secret-bearing dotenv generation.

### Common overlooked failures

- secret values appear in validation messages;
- browser autofill stores values;
- secret-mode state survives back navigation;
- analytics or crash tooling is inherited from a shared app template;
- remote fonts or icons create requests after secret entry;
- CSP violation reporting contradicts the zero-request promise;
- lazy translation loads after values are entered;
- service worker serves stale or tampered application code;
- SRI covers subresources but not a malicious main document;
- browser extension reads the DOM;
- managed memory retains copied strings;
- browser download permissions are assumed secure;
- desktop writes through a symlink;
- overwrite creates a plaintext backup;
- generated file quotes values incorrectly for Compose;
- empty relevant secret keys are mistaken for valid readiness;
- irrelevant optional secrets clutter output;
- documented defaults are frozen into generated files and later drift;
- manifest accidentally contains a secret;
- generated source maps or support bundles contain form state;
- auto-update runs during a secret session;
- macOS/Windows signing is skipped for “small” releases;
- Linux packaging is claimed from one tested distribution;
- license-incompatible native library enters through a transitive package;
- bundled grammar or native component lacks complete provenance/notices;
- a blocked Terminal.Gui/Avalonia node enters a pin, lock, or publish graph;
- a package-free shell is activated or advertised as a supported target;
- `Avalonia.Remote.Protocol` publish absence is assumed rather than proved;
- a replacement package inherits an obsolete approval;
- commercial packaging tooling enters the build unnoticed;
- hidden browser source is presented as protection against malicious hosting;
- unofficial lookalike site uses ISLAMU branding;
- accessibility status announces a secret value;
- a tenant document is shown as an instance/platform promise;
- a source instance’s legal URL remains after migration;
- imported terms overwrite acceptance history;
- template prose is presented as legal compliance;
- raw HTML or remote Markdown content executes or tracks visitors;
- untranslated legal text silently falls back across responsible parties;
- legal Markdown exceeds scanner limits or makes import unusable;
- template placeholders publish unresolved;
- agent parses localized prose instead of versioned JSON;
- agent drives interactive terminal state and chooses the wrong action;
- secrets appear in shell history, process arguments, scrollback, or stdout;
- skill asks the user to paste secrets into chat;
- skill and CLI versions drift;
- agent publishes legal text or applies configuration without approval;
- embedded AI adds provider keys, telemetry, or nondeterministic behavior;
- GPL/AGPL dependency is linked into a target expected to remain
  alternatively licensable without explicit approval;
- YAML merge order, aliases, paths, or directory enumeration change canonical
  meaning;
- a source tenant/object identifier is trusted as target authority;
- an expired capability or retried chunk repeats a live effect;
- a provider-readiness endpoint leaks a secret value or provider coordinate;
- migration staging outlives its purpose or bypasses erasure/retention;
- resume creates duplicate records or resurrects erased PII;
- target promotion deletes source state before recovery is proven;
- payment rows are copied without provider/ledger reconciliation; and
- Setup reports applied, refunded, rolled back, or complete while effects are
  pending or unknown.

### Possible bad outcomes

- credential theft;
- compromised instance, payment, identity, storage, email, or federation
  infrastructure;
- false confidence in a generated but incomplete `.env`;
- accidental Git commit or cloud backup of plaintext credentials;
- deployment outage;
- cross-platform users receiving unsupported or unverifiable artifacts;
- inaccessible setup for disabled administrators;
- license incompatibility blocking public or alternative distribution;
- reputational and legal harm from an absolute security claim;
- self-hosters becoming more dependent rather than more autonomous;
- support overload caused by noisy or irrelevant generated files;
- erosion of open-source trust through a hidden official web implementation;
- legally misleading operator attribution;
- users bound to text they were never shown or never accepted;
- target deployment relying on obsolete source-instance legal pages;
- cross-jurisdiction misstatement;
- inaccessible or unsafe public legal pages;
- agent-driven destructive or authority-broadening configuration;
- terminal secret disclosure;
- stale skills producing invalid artifacts;
- loss of an intended outbound licensing path;
- AI-provider lock-in and unnecessary privacy obligations;
- cross-tenant disclosure or mutation;
- PII copied beyond its stated purpose or resurrected after erasure;
- duplicate, omitted, or corrupt application records after retry;
- irreversible source loss and migration lock-in;
- payment/refund mutation against the wrong recipient, amount, currency, or
  allocation; and
- users acting on false completion or recovery evidence.

### Positive outcomes if implemented responsibly

- lower self-hosting barrier;
- safer alternative for operators without Infisical;
- practical no-secret default;
- fewer irrelevant environment variables;
- clearer setup readiness;
- consistent manifest/environment validation;
- credible cross-platform access;
- stronger release provenance and dependency evidence;
- improved accessibility and localization;
- reduced vendor and hosted-service lock-in;
- truthful understanding of web versus desktop assurance;
- portable, localized, role-accurate legal texts;
- easier counsel review through structured templates and diffs;
- preserved historical acceptance integrity;
- safer public rendering through constrained Markdown;
- deterministic automation without embedding AI;
- terminal-first access for remote and low-resource operators;
- auditable human approval around agent-generated drafts;
- broader FOSS participation without commercial dependencies.

### Provider questions before implementation

- Which FOSS licenses are compatible with each executable and outbound path?
- Which exact secrets may be generated rather than provider-issued?
- Which environment variables have safe defaults and activation predicates?
- Which browser versions can enforce the reviewed CSP?
- Can the selected successor-B browser build run without any post-load request?
- How are main-document and origin compromise explained?
- Which desktop filesystems cannot enforce expected permissions?
- What Linux distributions and architectures are genuinely supported?
- What source/release evidence is displayed before secret opt-in?
- What event triggers an I-VSD and threat-model refresh?
- Which legal document kinds are instance-, tenant-, or organizer-owned?
- Which imported legal texts become drafts versus scheduled versions?
- Which changes require fresh user acceptance and notification?
- Which template sources and licenses are approved?
- Which commands and JSON schemas are stable enough for the skill?
- Which operations remain human-only?
- Which terminal environments and assistive technologies are supported?

## Stakeholders

| Stakeholder | Interest | Provider-controlled protection |
|---|---|---|
| New self-hoster | Complete setup without mastering hundreds of variables | Progressive profile, relevant-only output, docs, readiness |
| Experienced operator | Deterministic, inspectable, offline tooling | Raw preview, exact catalogue, no hidden defaults, signatures |
| User without Infisical | Safe local secret-entry option | Desktop path, optional web mode, no persistence/network |
| Security-conscious user | Strongest available assurance | Signed desktop, offline bundle, source/digest evidence |
| Web convenience user | No-install experience | Default no-secret mode and explicit trust choice |
| Disabled administrator | Equal ability to configure deployment | Accessible controls, status, review, platform testing |
| Arabic/RTL user | Understandable, correct setup workflow | Bundled localization and logical layout |
| Instance/tenant administrator | Portable non-secret configuration | Manifest/package workspace and strict separation |
| Maintainer | One source of truth and supportable releases | Shared core, generated catalogue, CI drift gates |
| Release operator | Verifiable multi-platform artifacts | lock files, SBOM, signatures, attestations, smoke tests |
| ISLAMU steward | Truthful official trust and legal accountability | official origin, identity, transparent claim boundary |
| Instance operator/legal counsel | Accurate platform texts and responsibilities | typed instance documents, templates, review lifecycle |
| Tenant operator/legal counsel | Local autonomy without false platform claims | tenant-owned documents, additive composition, target review |
| Terminal-first operator | Full functionality over SSH/console | Terminal.Gui human target, stable machine commands, no graphical desktop requirement |
| External AI-agent user | Deterministic automation without bundled AI | machine JSON, no-secret defaults, approval gates |
| Skill maintainer | Commands and guidance remain aligned | version range, executable examples, schema/link tests |
| Third-party self-hoster | Freedom to audit and host compliant code | tracked source, AGPL obligations, brand distinction |
| People affected by compromise | Protection from downstream misuse | minimum exposure, incident process, no overclaims |
| Source and target tenant operators | Controlled, reversible movement without authority broadening | explicit enrollment, category selection, tenant-qualified mappings, source retention |
| Migrated users and data subjects | Purpose-limited custody, privacy, erasure, and no resurrection | authority-first erasure, protected bounded staging, PII-free evidence |
| Buyers and organizer merchants | Correct recipient, amount, currency, refund, and reconciliation truth | separate Tier 0 state machine, immutable snapshots, provider/ledger reconciliation |
| Support and recovery operators | Actionable evidence without secrets or PII | granular authoritative states, value-free receipts, explicit unknown/reconciliation paths |

## I-VSD Principles And Domains

| Principle | Application |
|---|---|
| Amanah / Trust | Secret handling, release identity, source, permissions, and limits are explicit and auditable. |
| Sidq / Truthfulness | Hosted origin trust and memory/browser limitations are never hidden by “client-side” marketing. |
| Adl / Justice | Web, desktop, Linux, accessibility, localization, and self-hosting paths avoid excluding less-resourced users. |
| Non-Harm | No-secret default, network denial, no persistence, local generation, and safe writes reduce foreseeable compromise. |
| Rights of People | Operators retain control of configuration and can avoid sending secrets to ISLAMU. |
| Avoiding Spying | No telemetry, analytics, session replay, remote logs, or secret-bearing reports. |
| Avoiding Gharar | Relevant/omitted/defaulted/incomplete states and trust boundaries are known before download. |
| Promise-Keeping | Cross-platform, FOSS-only, local-only, CLI-parity, and self-hosting claims require concrete release evidence. |
| Ihsan / Excellence | Accessibility, RTL, deterministic generation, signatures, SBOMs, and adversarial tests are core quality. |

Domain review:

- **Strategic:** lower self-hosting barrier without making users dependent on
  official hosting.
- **Design:** no-secret default, explicit trust, progressive disclosure,
  relevant-only output, and accessible review.
- **Technical:** pure shared core, client-side execution, network denial,
  memory minimization, safe file output, versioned CLI, and per-target FOSS
  compatibility.
- **Operational:** signed multi-platform releases, incident response, support,
  package lifecycle, and upgrade evidence.
- **Governance:** official identity, truthful legal copy, AGPL source
  obligations, trademark boundaries, and dependency approval.
- **Evaluation:** request capture, storage inspection, filesystem checks,
  usability studies, accessibility audits, and package smoke tests.

## Validation Gaps

- Successor A and Phases 1-4 are Green. The exact Toolkit shared-presentation
  and DI executable-root graphs are approved for only those roles, and the
  final 18/18 owner-local plus 14/1 architecture-ratchet Red is accepted, but
  no successor-B shared model, adapter, browser/desktop runtime, support
  surface, or release capability is implemented or activated.
- The archived ConfigurationManifest baseline must be pinned before
  extraction; closure does not prove its retired atomic-apply, managed
  ownership, migration UI, or direct-transfer phases.
- Successor A's historical BCL-only graph, ten package-free project
  boundaries/locks, and generated ratchets are verified; the console wizard is
  nevertheless superseded and must be deleted rather than retained as fallback.
- Official Terminal.Gui 2.4.17 as-published remains blocked for incomplete
  bundled grammar provenance/notices. The authorized internal package has not
  yet been built or proven, so no Terminal.Gui target is currently active.
- Avalonia 12.1.1 Desktop/Browser runtime graphs remain blocked for unresolved
  native component/license mapping and unproved `Avalonia.Remote.Protocol`
  publish exclusion; A pins/restores no Avalonia package.
- B1 now has approved exact Toolkit `8.4.2` and DI/Abstractions `10.0.10`
  closures for their bound roles plus non-forgeable real-continuation and
  retained-exhaustion Red tests and an exact project/lock/assembly/content-hash
  graph ratchet. The product package reference, central pin, product lock,
  shared implementation, generated capabilities, support, release, and
  shipping surfaces remain unchanged in the bound preimage.
- B0 supersession is verified across its triad, intake, binding, CTO review,
  and separate I-VSD report. Those records are historical only and create no
  B1 authority.
- No exact CSP has been proven against an approved successor-B browser output.
- No browser test proves zero requests after secret-mode entry.
- No memory/DOM/storage inspection has been performed.
- No desktop permission writer has been tested across target filesystems.
- Successor A's implemented environment catalogue and activation predicates
  are repository evidence for A only; no B1 human presentation or target has
  been tested against them.
- No B1 support matrix has been approved. `ApprovedDisabled` is not runtime,
  accessibility, support, release, or shipping evidence.
- No real users have tested the B1 progressive form or understood omissions.
- No assistive-technology or RTL evidence exists for an activated successor-B
  GUI/browser/desktop target.
- No reproducible build, code-signing, notarization, Linux package signature,
  SBOM, or attestation pipeline exists for this product.
- No legal review has approved the official web trust disclosure, privacy
  statement, or trademark wording.
- Server-side legal aggregates, a typed kind catalogue, constrained Markdown,
  lifecycle, publication evidence, and public rendering now exist; no Setup
  legal editor or approved template library exists.
- No legal-template provenance or qualified legal review exists.
- No content-size analysis proves realistic localized legal documents fit the
  next manifest/package limits.
- No security review has approved hosted secret entry.
- Successor A's CLI, command schema, exit categories, and BCL terminal wizard
  are Green under A's evidence; B1 must not reinterpret them as presentation-
  target, rich-TUI, accessibility, support, or release evidence.
- No approved B1 Terminal.Gui graph or no-secret adapter exists. The BCL
  terminal support/accessibility matrix remains a later release-evidence gate.
- No Setup Assistant skill exists; its future release must bind the implemented
  CLI version/schema and no-secret/human-approval behavior rather than infer
  readiness from the now-Green A command contract.
- No explicit decision identifies which Setup executables, if any, may become
  AGPL-only because of reciprocal dependencies.
- No corrected C1 Red test owner, aggregate absent-owner Red, C1 Green
  implementation, Linux/Windows claimed-platform run, or C2 measured profile
  exists. The corrected ledger now specifies fourteen matrices, exact ceilings,
  public seams, deterministic barriers, Worst Break, owned paths, and commit
  governance, but planning and isolated probe/review evidence do not prove
  those future runtime outcomes.
- No target-enrollment, capability-replay, revocation, saved-profile, or
  secret-provider write/readiness evidence exists.
- No proof yet shows provider coordinates and raw values are absent from every
  live response, log, receipt, diagnostic, and support surface.
- No application-data category inventory, target compatibility matrix,
  migration mapping/checkpoint store, protected-staging retention rule, or
  production custody assignment exists.
- No migration evidence yet proves tenant isolation, concurrent idempotent
  resume, file integrity, authority-first erasure replay, anti-resurrection,
  or source retention.
- No Tier 0 decision record yet binds hold-expiration/finalization precedence,
  payout routing, and partial-refund/fee allocation to the migration state
  machine; this blocks SA-1110.
- No exact provider/ledger reconciliation fixture, actor approval matrix, or
  sovereign recovery rehearsal exists; this blocks SA-1140.
- No stakeholder or operational evidence shows users understand category
  custody, pending/unknown states, source retention, or irreversible actions.

The current evidence supports design reasoning and repository implementation
traceability only. It does not establish stakeholder or operational validation.

## Escalation Needed

- Fresh Senior CTO confirmation of final Phase 8 binding
  `setup-assistant-security-and-portability-phase8-final-20260901` before C1
  Red. E073 is an `Approve with required changes` verdict on the prior corrected
  revision; its governance changes are now present, but it is not confirmation
  of E074. The standing user direction is sufficient after final technical
  approval; no duplicate user prompt is required for unchanged scope.
- Before C1 Green adds YamlDotNet to Setup Core, the exact approved YamlDotNet
  `18.1.0` one-node content identity, notices/outbound fit, vulnerability/
  deprecation state, parser-event/syntax-tree-only role, safe CLI environment,
  and C1 Red disposition must be reverified together. Drift disables YAML.
- C1 Green may claim directory support only on platforms with real handle/type/
  identity evidence. Linux and Windows claims are independent; Windows remains
  disabled until equivalent reparse-safe runner evidence exists.
- C2 may enable a named larger profile only after C1 Green and measured resource
  plus target-server-limit evidence. Missing or nondeterministic evidence
  preserves the exact C1 default and disables the larger profile.
- Fresh revision-bound Senior CTO review of final-Red binding
  `setup-assistant-security-and-portability-b1-final-red-20260831` before
  SA-518 changes the central pin, product reference, product lock, or shared
  source. The user's persistent full-implementation/no-backward-compatibility
  direction supplies the current product direction but does not replace this
  technical gate or authorize any adapter, rendered accessibility, secret
  mode, support, release, or shipping claim.
- The bound post-probe security verdict governs SA-518's session, generation,
  cancellation, settlement, secret-exclusion, and target boundaries. Any drift
  in those boundaries requires fresh Tier 1 security review before continuing.
- Independent security review before enabling online-hosted secret entry.
- IP/legal dependency review must preserve A's blocked Terminal.Gui/Avalonia
  decisions, then review successor B's independently selected complete
  CommunityToolkit/DI/Avalonia/Terminal.Gui/runtime/packaging graphs and any
  reciprocal-license target.
- Qualified legal review for hosted secret-mode copy, privacy promises,
  official/unofficial attribution, AGPL network-source obligations, trademark,
  and incident notices.
- Qualified legal review for every bundled legal template, role assignment,
  jurisdiction assumption, acceptance rule, and public legal claim.
- Accessibility review before UI architecture approval and real platform
  audits before release.
- Release-engineering approval for signing, notarization, SBOM, provenance,
  package retention, and update policy.
- Fresh I-VSD review before raw secret retrieval, direct Setup-to-provider SDK
  access, provider credential read/test flows, service worker/PWA, auto-update,
  non-revocable token persistence, plugins, or any provider authority broader
  than IVSD-F040/M040.
- Agent-context review before publishing the skill, plus security review of
  every command the skill may invoke.
- Tier 1 authorization and replay review before SA-910; no live capability may
  ship with long-lived plaintext authority, source-ID authority, provider
  coordinate disclosure, or secret readback.
- Tier 2 privacy/custody review before SA-1110 must bind category purposes,
  custodians, retention, staging disposal, authority-first erasure ordering,
  anti-resurrection fences, and value-free recovery evidence.
- Tier 0 intake before SA-1110 must record current hold-expiration versus
  payment-finalization precedence, `OrganizerDirect` payout routing, and
  deterministic partial-refund/fee allocation. An unresolved branch disables
  sovereign migration rather than selecting a fallback.
- SA-1140 additionally requires exact provider and ledger reconciliation
  evidence, explicit authorized actors, idempotent unknown-outcome recovery,
  and provider/legal/operational approval for the enabled deployment profile.
- Qualified Sunni scholarly review only if future product claims, contracts,
  payment features, or marketing introduce religious-legal conclusions. No
  such ruling is made here.

## Evidence Reviewed

### Repository Evidence

| Evidence ID | Locator | Contribution |
|---|---|---|
| E001 | `.env.example` | Current environment template and user-facing variable surface |
| E002 | `docker-compose.yml` | Compose interpolation and deployment profiles |
| E003 | `docs/CONFIGURATION.md` | Canonical configuration behavior and sources |
| E004 | `docs/SECRETS.md` | Secret-provider and `.env` boundaries |
| E005 | `docs/SECURITY-MODEL.md` | BFF, token, logging, and secret trust patterns |
| E006 | `src/Explore.Domain/Secrets/SecretDefinitionRegistry.cs` | Secret keys, scopes, source types, and environment names |
| E007 | `islamic-value-sensitive-design/i-vsd-configuration-manifest.md` | Manifest/package portability and strict non-secret boundary |
| E008 | `Directory.Packages.props` and `Directory.Build.props` | Central versions, lock files, and outbound package posture |
| E009 | `docs/legal/IP_GOVERNANCE.md` and clean-room dependency gate | Complete dependency and provenance requirements |
| E017 | `LICENSE` and `islamic-value-sensitive-design/i-vsd-licensing-and-commercial-strategy.md` | AGPL network-service and open-source governance context |
| E022 | `docs/ACCESSIBILITY.md` | WCAG 2.2 AA-aligned repository standards |
| E023 | `docs/LOCALIZATION.md` | Localization, offline bundles, and RTL behavior |
| E024 | `src/Explore.Blazor.Client/Pages/Legal/TermsOfService.razor`, `PrivacyPolicy.razor`, and the active legal-document feature | Current role-labelled last-published legal rendering |
| E025 | `docs/FOOTER_MANAGEMENT.md` | Existing instance/tenant legal-link and operator-role separation |
| E026 | `islamic-value-sensitive-design/i-vsd-branding-legal-identity-authority.md` | Legal identity, operator attribution, and no-fallback boundaries |
| E027 | `docs/DUAL_VERSIONING.md`, `docs/legal/IP_GOVERNANCE.md`, and `legal/CLA.md` | FOSS/commercial distinction, dependency obligations, and alternative outbound paths |
| E031 | `eng/release/src/ISLAMU.ReleaseEngineering/Program.cs` and schema-generator `Program.cs` | Existing deterministic command, exit, bounded-output, and help conventions |
| E032 | `.agents/skills/_SKILL_SCHEMA.md` | Required skill metadata, progressive disclosure, and verification shape |
| E033 | `.agents/skills/skill-authoring/SKILL.md` and resources | Skill lifecycle, command-evidence, and no-fiction requirements |
| E034 | `dev/active/setup-assistant-security-and-portability/setup-assistant-security-and-portability-plan.md` | Exact B1 Scenarios 3.1–3.16, CommunityToolkit-only shared model, target-root DI, session/fencing boundaries, six-stage probes, phases, risks, and mappings |
| E035 | `dev/active/setup-assistant-security-and-portability/setup-assistant-security-and-portability-tasks.md` | Corrected task ledger including C1 Red fourteen matrices/exact ceilings/public seam/Worst Break, Core-only C1 Green, scale-only C2, exact owned paths, and per-slice verification/commit governance |
| E036 | `dev/active/setup-assistant-security-and-portability/setup-assistant-security-and-portability-context.md` | Current successor-A Green state, B1 blockers, B0 supersession, known risks, and handoff |
| E037 | `dev/active/setup-assistant-security-and-portability/setup-assistant-security-and-portability-clean-room-evidence.md` | Source-free WPF/framework observations, independent B1 architecture, clean-room attestation, outbound boundaries, and evidence limits |
| E038 | `docs/AUTHORIZATION.md`, `docs/MULTI_TENANCY.md`, and repository authorization/tenant contracts | Existing server-owned actor, target, and tenant authority boundary |
| E039 | `docs/PRIVACY_ERASURE.md` and repository privacy-erasure authority contracts | Authority-first ordering, anti-resurrection fencing, payload-free receipts, replay, and retention |
| E040 | `docs/PAYMENTS.md`, `islamic-value-sensitive-design/i-vsd-paid-event-payments-consultation.md`, and repository payment/refund contracts | OrganizerDirect, immutable recipient/currency, deterministic allocation, idempotency, and reconciliation truth |
| E041 | `dev/active/setup-assistant-security-and-portability/setup-assistant-security-and-portability-dependency-evidence.md` | Sanitized handoff preserving A's BCL-only graph and defining unapproved B1 CommunityToolkit/Avalonia/Terminal.Gui geometry/re-entry evidence |
| E042 | `dev/active/setup-assistant-security-and-portability/setup-assistant-security-and-portability-cto-review.md` | Technical approval bound to successor A's exact Green revision; grants no approval to B or later work |
| E043 | `dev/active/setup-assistant-security-and-portability/setup-assistant-security-and-portability-approval.md` | User approval bound to successor A's exact Green revision; confirms no later-successor inheritance |
| E047 | `dev/active/setup-assistant-presentation-targets/` and `islamic-value-sensitive-design/i-vsd-setup-assistant-presentation-targets-b0.md` | Historical B0 shared-Razor/static-browser branch; explicitly superseded, non-executable, non-authorizing, never user-approved, and non-transferable to B1 |
| E048 | `dev/active/setup-assistant-security-and-portability/setup-assistant-security-and-portability-b1-review-bindings.md` | Immutable B1 binding ID, six exact artifact hashes, architecture scope, authority boundary, and drift invalidation |
| E049 | `dev/active/setup-assistant-security-and-portability/setup-assistant-security-and-portability-b1-intake-review.md` | Tier 1 decision-complete architecture, session/generation/secret invariants, adapter semantics, probe gates, and stop conditions |
| E050 | `dev/active/setup-assistant-security-and-portability/setup-assistant-security-and-portability-b1-post-red-review-bindings.md` | Exact post-Red binding, activation scope, safe CLI environment, unchanged product preimage, and drift invalidation |
| E051 | `dev/active/setup-assistant-security-and-portability/setup-assistant-security-and-portability-b1-probe-evidence.md` | Exact Toolkit/DI locks, roles, signatures, audits, egress observation, and authority non-drift |
| E052 | `dev/active/setup-assistant-security-and-portability/setup-assistant-security-and-portability-b1-dependency-review.md` | Post-probe dependency/IP approval for exact Toolkit shared and DI executable-root roles only |
| E053 | `dev/active/setup-assistant-security-and-portability/setup-assistant-security-and-portability-b1-security-review.md` | Post-probe security approval, session/secret boundaries, safe CLI mandate, and target exclusions |
| E054 | `dev/active/setup-assistant-security-and-portability/setup-assistant-security-and-portability-b1-accessibility-review.md` | Post-probe architecture approval without rendered accessibility or support claims |
| E055 | `tests/Event.SetupAssistant.Tests/SetupPresentationModelContract.cs` and `SetupPresentationModelTests.cs` | Bound ten-test intentional Red covering public session, lifecycle, fencing, projection, secrecy, and disabled-adapter seams |
| E056 | `src/Event.SetupAssistant/Event.SetupAssistant.csproj`, `src/Event.SetupAssistant/packages.lock.json`, and `Directory.Packages.props` | Bound unchanged product reference, product lock, and central-pin preimages |
| E057 | `dev/active/setup-assistant-security-and-portability/setup-assistant-security-and-portability-b1-corrected-red-review-bindings.md` | Exact corrected-Red binding, frozen product authority, activation scope, and drift invalidation |
| E058 | `dev/active/setup-assistant-security-and-portability/setup-assistant-security-and-portability-b1-post-red-cto-review.md` | Five retained CTO correction groups and `Changes required` provenance |
| E059 | `tests/Event.SetupAssistant.Tests/SetupPresentationModelContract.cs` and `SetupPresentationModelTests.cs` | Owner-local 18-test taxonomy; exact race orders; typed exhaustion/ABA; generated behavior; memory identity; dynamic-canary contracts |
| E060 | `tests/Event.Architecture.Tests/SetupAssistantArchitectureTests.cs` | Adapter ownership and exact evaluated target, project, lock, package-pin, and compiled-assembly ratchets |
| E061 | `dev/active/setup-assistant-security-and-portability/setup-assistant-security-and-portability-tasks.md` plus unchanged product preimages | Independent SA-518 correction-Red/Green verification, safe environment, exact ownership, failure, and planned commit closure |
| E062 | `dev/active/setup-assistant-security-and-portability/setup-assistant-security-and-portability-b1-final-red-review-bindings.md` | Exact final-Red binding, frozen product authority, activation scope, and drift invalidation |
| E063 | `dev/active/setup-assistant-security-and-portability/setup-assistant-security-and-portability-b1-corrected-red-cto-review.md` | Four final residual corrections and second `Changes required` provenance |
| E064 | `tests/Event.SetupAssistant.Tests/SetupPresentationModelContract.cs` and `SetupPresentationModelTests.cs` | No public workspace settlement injection; duplicate real continuations; retained final-generation completion after termination; unchanged 18/18 taxonomy |
| E065 | `tests/Event.Architecture.Tests/SetupAssistantArchitectureTests.cs` | Exact approved Toolkit `contentHash` plus unchanged 14/1 graph-ratchet taxonomy |
| E066 | `dev/active/setup-assistant-security-and-portability/setup-assistant-security-and-portability-tasks.md` plus unchanged product preimages | Explicit conventional-commit loading, truthful-message reuse, material override replacement state, and unchanged SA-518 closure |
| E067 | `dev/active/setup-assistant-security-and-portability/setup-assistant-security-and-portability-phase8-review-bindings.md` | Exact Phase 8 planning/Core/test/lock/central-pin preimages, proposed source grammar, parser role, authority boundary, safe CLI environment, and drift triggers |
| E068 | `dev/active/setup-assistant-security-and-portability/setup-assistant-security-and-portability-phase8-yaml-probe-evidence.md` | Isolated YamlDotNet 18.1.0 one-node graph, content and artifact hashes, signature/audit/license observations, syntax-tree-only execution, and product non-drift without activation authority |
| E069 | `dev/active/setup-assistant-security-and-portability/setup-assistant-security-and-portability-phase8-corrected-review-bindings.md` | Exact corrected C1 Red/C1 Green/C2 split, all bound hashes, unchanged product preimages, owned-path/commit closures, and drift/authority boundary |
| E070 | `dev/active/setup-assistant-security-and-portability/setup-assistant-security-and-portability-phase8-dependency-review.md` | Independent dependency/IP approval for exact YamlDotNet 18.1.0 one-node graph and bounded syntax-tree role only, with product activation withheld |
| E071 | `dev/active/setup-assistant-security-and-portability/setup-assistant-security-and-portability-phase8-security-review.md` | Fourteen adversarial matrices, parser/filesystem/canonical/zero-value boundaries, platform evidence requirements, and conditional Green/scale gates |
| E072 | `dev/active/setup-assistant-security-and-portability/setup-assistant-security-and-portability-phase8-cto-review.md` | Pre-correction `Split before approval` verdict that required the now-bound split, ceilings, public seam, Worst Break, owned paths, verification, and commit governance; not CTO approval of E069 |
| E073 | `dev/active/setup-assistant-security-and-portability/setup-assistant-security-and-portability-phase8-corrected-cto-review.md` | Corrected-revision `Approve with required changes` verdict accepting the technical contract while requiring literal commit copy, change-fragment/Change-Id governance, and mixed-author blockers before execution |
| E074 | `dev/active/setup-assistant-security-and-portability/setup-assistant-security-and-portability-phase8-final-review-bindings.md` | Final governance-only binding with exact two-file Red, exact Green/C2 new paths, central-pin read-only rule, literal messages/footers/override state, mixed-author blockers, unchanged technical scope, and all hashes |

### Official Functional References

| Evidence ID | Source | Observed functional fact |
|---|---|---|
| E010 | [Avalonia WebAssembly deployment](https://docs.avaloniaui.net/docs/deployment/webassembly) | Browser publish output is a static client-side WebAssembly site |
| E011 | [Avalonia framework FAQ](https://docs.avaloniaui.net/tools/faq) | Avalonia framework is MIT; professional tooling has separate licensing |
| E012 | [MDN CSP guide](https://developer.mozilla.org/en-US/docs/Web/HTTP/Guides/CSP) | CSP restricts resource/action channels but is defense in depth |
| E013 | [MDN `connect-src`](https://developer.mozilla.org/en-US/docs/Web/HTTP/Reference/Headers/Content-Security-Policy/connect-src) | Script connection APIs controlled by `connect-src` |
| E014 | [Microsoft Blazor CSP guidance](https://learn.microsoft.com/en-us/aspnet/core/blazor/security/content-security-policy?view=aspnetcore-10.0) | Client-side WebAssembly can use `connect-src 'none'`; CSP does not guarantee complete security |
| E015 | [.NET Unix file-mode API](https://learn.microsoft.com/en-us/dotnet/api/system.io.file.setunixfilemode?view=net-10.0) | .NET exposes Unix file-mode control |
| E016 | [.NET cryptographic random API](https://learn.microsoft.com/en-us/dotnet/api/system.security.cryptography.randomnumbergenerator.getbytes?view=net-10.0) | .NET exposes cryptographically strong random byte generation |
| E018 | [Avalonia Linux guide](https://docs.avaloniaui.net/docs/platform-specific-guides/linux) | Linux desktop, backend, and accessibility behavior |
| E019 | [Avalonia macOS guide](https://docs.avaloniaui.net/docs/platform-specific-guides/macos) | macOS backend, app bundle, platform, and accessibility behavior |
| E020 | [.NET RID catalogue](https://learn.microsoft.com/en-us/dotnet/core/rid-catalog) | Portable Windows, Linux, and macOS runtime identifiers |
| E021 | [GNU AGPL-3.0-or-later repository license](../LICENSE) | Network-service source and distribution governance |
| E028 | [GNU license FAQ](https://www.gnu.org/licenses/gpl-faq.html#AGPLGPL) | GPLv3/AGPLv3 linking compatibility and combined-work obligations |
| E029 | [Terminal.Gui documentation](https://tui-cs.github.io/Terminal.Gui/index.html) | Windows/macOS/Linux TUI, editor, wizard, keyboard/mouse, Unicode, and inline/full-screen behavior |
| E030 | [Terminal.Gui 2.4.17 NuGet metadata](https://api.nuget.org/v3/catalog0/data/2026.07.07.12.25.25/terminal.gui.2.4.17.json) | MIT package metadata, net10 target, and transitive dependency inventory |
| E044 | [CommunityToolkit.Mvvm overview, generators, messenger, and recipient lifecycle](https://learn.microsoft.com/en-us/dotnet/communitytoolkit/mvvm/) | UI-framework-independent observable/command/message behavior and explicit recipient lifetime; no exact package graph approval |
| E045 | [Avalonia compiled bindings](https://docs.avaloniaui.net/docs/data-binding/compiled-bindings), [application lifetimes](https://docs.avaloniaui.net/docs/fundamentals/application-lifetimes), [dependency injection](https://docs.avaloniaui.net/docs/app-development/dependency-injection), [storage provider](https://docs.avaloniaui.net/docs/services/storage/storage-provider), and [style selectors](https://docs.avaloniaui.net/docs/styling/style-selectors) | Typed compiled binding plus distinct target lifetime/service/style adaptation requirements; no runtime graph approval |
| E046 | [Terminal.Gui v2 documentation](https://gui-cs.github.io/Terminal.GuiV2Docs/docs/) | Event/key-command application model requiring an explicit adapter rather than assumed WPF binding parity |

This revalidation used the source-free E037/E041 handoffs, historical official
E044-E046 functional documentation, prior post-Red/corrected-Red E050-E061
evidence, and exact final-Red E062-E066 evidence. It did not ingest external
implementation source, XAML, assets, copied prose, or expressive application
organization.
The historical official functional references remain context from prior
reviews, not newly accessed evidence. No external source code, AST, schema,
tests, migrations, prose, assets, screenshots, or product implementation
structure was retained or copied.

The prior B1 final-Red revalidation was bound by binding ID
`setup-assistant-security-and-portability-b1-final-red-20260831`. That binding
file's verified SHA-256 is
`ae262c99d9fb39b7263e057b1cfc9f10ec6b5f89637c91ca80512881e0bacf49`.
Every artifact named by that binding was independently recomputed and matched:

| Bound B1 input | File SHA-256 |
|---|---|
| `dev/active/setup-assistant-security-and-portability/setup-assistant-security-and-portability-plan.md` | `9ce42649427ec14c201af129e2d8781814905c6f70d9a23deac4c68d13b77649` |
| `dev/active/setup-assistant-security-and-portability/setup-assistant-security-and-portability-tasks.md` | `ed219ef034888becfe900a3e3323a397c1e1a0c7bef267827e5be0ace1d26343` |
| `dev/active/setup-assistant-security-and-portability/setup-assistant-security-and-portability-context.md` | `64eb113b4e3bb682465367189cfb9ede1e109f6f5067fdbecff47918227413aa` |
| `dev/active/setup-assistant-security-and-portability/setup-assistant-security-and-portability-clean-room-evidence.md` | `d0a0d0e1a581d6b930e2bfb7b66d8787acd8b8e933660d3844eea9ccf5d3687c` |
| `dev/active/setup-assistant-security-and-portability/setup-assistant-security-and-portability-dependency-evidence.md` | `ce005b80fd4853a93e2bc20a393e9f015387454738484f5854c70ce20be4bee1` |
| `dev/active/setup-assistant-security-and-portability/setup-assistant-security-and-portability-b1-intake-review.md` | `bf595568582af05e1900126abe65a4adca67196eedd340cee4b0873904e37d59` |
| `dev/active/setup-assistant-security-and-portability/setup-assistant-security-and-portability-b1-probe-evidence.md` | `424ef6b6e3b7b7700b4d26b11149545b0f97fe0165a22890d35a67f9e8e14be8` |
| `dev/active/setup-assistant-security-and-portability/setup-assistant-security-and-portability-b1-dependency-review.md` | `f3a1b451dddfdae0c890eea518820a3e2ef0bb1392e290c79e06215903af0cde` |
| `dev/active/setup-assistant-security-and-portability/setup-assistant-security-and-portability-b1-security-review.md` | `bfc463df3f929d533399d0701ded38b963401a7020c2719062558d39d4f8978b` |
| `dev/active/setup-assistant-security-and-portability/setup-assistant-security-and-portability-b1-accessibility-review.md` | `d012e3383da39f5f14ef2e1897b9ef2b4217c9cb3ec1f32692b5c6b5930826cb` |
| `dev/active/setup-assistant-security-and-portability/setup-assistant-security-and-portability-b1-corrected-red-cto-review.md` | `270ab1644c72ae06c2dc8022c38ecaa4ba4eb58dfbc5e371570c10c8a2a76255` |
| `tests/Event.SetupAssistant.Tests/SetupPresentationModelContract.cs` | `7bfb63fe317b0df30d8a8c7c9a1427db5ce027c4a34ec91da130548242229322` |
| `tests/Event.SetupAssistant.Tests/SetupPresentationModelTests.cs` | `eabeafe4442e06ec3e0476fc6d847c857f9711cee5a19b4969495f7ebc9d13eb` |
| `tests/Event.Architecture.Tests/SetupAssistantArchitectureTests.cs` | `f3a1a3f286ee820a09bf1a986163212aa7c852de5986dfa3a9eb93179afe2399` |
| `src/Event.SetupAssistant/Event.SetupAssistant.csproj` | `25a96b0efe99f70f142d62b2635eed8532f979c0af724134e79d814ca907ed09` |
| `src/Event.SetupAssistant/packages.lock.json` | `94dd640c9d61220d025a020fb2f2381d82a644638707a5b0a0b335f50999a65f` |
| `Directory.Packages.props` | `9a3f4fba1708461971ba9f73a275422681367ee91b3f6ae5179954e24a80ad9e` |

The binding—not a date, old aggregate, B0 digest, report hash, or prior review
artifact—is the reviewed input revision. The bound evidence approves Toolkit
and DI only for their exact roles and accepts the final owner-local Red; it
does not establish rendered accessibility, target activation, secret
capability, support, release, shipping, legal certification, or scholarly
approval. Any material drift in a bound artifact, graph role/content hash, Red
invariant, product preimage, target disposition, secret boundary, safe CLI
environment, verification/commit contract, or review result makes this report
stale.

Review of the three final test files and task ledger confirms only the four
final corrections: the public workspace has no settlement-injection seam and
duplicate completion reaches the internal commit only through two real
`ExecuteAsync` continuations; exhaustion terminates and cancels an in-flight
final-generation operation before its retained completion is released and
proven inert; the structured lock ratchet requires the exact approved Toolkit
`contentHash`; and commit execution explicitly loads `conventional-commit`,
reuses the planned message while truthful, and permits a material override only
after recording the complete replacement reason, message, changelog/trailers,
paths, and verification state. The observed Red remains 18/18 owner-local and
14 total with only `SA518-GRAPH-RATCHET` failing. This is intentional Red and
planning evidence, not implementation or runtime-target proof.

Every future B1 restore, build, test, or publish evidence run must use
`DOTNET_CLI_TELEMETRY_OPTOUT=1`,
`DOTNET_SKIP_FIRST_TIME_EXPERIENCE=1`,
`DOTNET_CLI_WORKLOAD_UPDATE_NOTIFY_DISABLE=1`, `DOTNET_NOLOGO=1`, and an
isolated package cache. The first unguarded SDK build showed workload-
advertising egress and is not accepted as no-egress evidence.

The prior initial Phase 8 revalidation was bound by binding ID
`setup-assistant-security-and-portability-phase8-20260831`. Its independently
recomputed SHA-256 is
`a9a1e4f05de526a6dc2af5e407ebcf118c49d6daa66b163dddb12826f0326ae1`,
matching the expected digest. Every artifact named by the binding was also
recomputed and matched:

| Bound Phase 8 input | File SHA-256 |
|---|---|
| `dev/active/setup-assistant-security-and-portability/setup-assistant-security-and-portability-plan.md` | `9ce42649427ec14c201af129e2d8781814905c6f70d9a23deac4c68d13b77649` |
| `dev/active/setup-assistant-security-and-portability/setup-assistant-security-and-portability-tasks.md` | `953259b780b6335b07f08a40025063f4b21521baf8911e57658088f136a7e34d` |
| `dev/active/setup-assistant-security-and-portability/setup-assistant-security-and-portability-context.md` | `a6459a97e175bf29a68c1960de3e82f7315fde7e4990fad81327aa7d46e6ceb1` |
| `dev/active/setup-assistant-security-and-portability/setup-assistant-security-and-portability-clean-room-evidence.md` | `d0a0d0e1a581d6b930e2bfb7b66d8787acd8b8e933660d3844eea9ccf5d3687c` |
| `dev/active/setup-assistant-security-and-portability/setup-assistant-security-and-portability-dependency-evidence.md` | `ce005b80fd4853a93e2bc20a393e9f015387454738484f5854c70ce20be4bee1` |
| `islamic-value-sensitive-design/i-vsd-setup-assistant-security-and-portability.md` (review preimage) | `84978de42607bdd8c6459de7777a2d4331134384c31d67b88900902b68a326a5` |
| `src/Event.Setup.Core/Event.Setup.Core.csproj` | `b038160cde81ff2b188739d3a8f67eb014323e5b46f8833e48fdc7c6b59737cf` |
| `src/Event.Setup.Core/packages.lock.json` | `d8d75d293ca094de8a27aaa566500176f5185cf1c06149668bb0a225efe8e8a8` |
| `tests/Event.Setup.Core.Tests/Event.Setup.Core.Tests.csproj` | `49f51e7f7c93c134bc359134be73fc1060033ba9c015265dbb50f9ff01f40d9e` |
| `tests/Event.Setup.Core.Tests/packages.lock.json` | `fae9592f8dc76d561c8741cc8469eee2d25413951fd286a86e011fe2f728b4c2` |
| `Directory.Packages.props` | `1cba249bd5e7520e7c4b1a5d22a4a44dc3fa251e80771ca44e009952441f529e` |

The independently recomputed Phase 8 YAML probe-evidence SHA-256 is
`39998d1f4f97c22399d60900d990bacfecd22d2548331a0e101839544b5ecf1b`.
The probe records YamlDotNet `18.1.0` as one direct `net10.0` node with no
transitives, a valid NuGet.org repository signature, no vulnerability or
deprecation finding, MIT metadata, and successful restore/build/execution. Its
role is limited to bounded in-memory syntax-tree parsing; no deserializer,
serializer/emitter, dynamic type, naming policy, remote resolver, file,
directory, network, telemetry, provider, Setup schema, canonical serializer,
validator, or wire authority was exercised. The product and test preimages did
not drift.

This evidence is sufficient for I-VSD alignment of SA-810's tests and
conditional Phase 8 progression under IVSD-F037/M037. It is not dependency/IP,
security, CTO, package-reference, implementation, support, release, or shipping
approval. All Phase 8 restore/build/test commands must use the binding's four
safe CLI variables and isolated package cache. Material drift in a bound file,
parser identity/version/graph/role, source grammar, limits, canonical authority,
smuggling boundary, measurement profile, CLI environment, or review verdict
makes this report stale.

The prior corrected Phase 8 revalidation was bound by binding ID
`setup-assistant-security-and-portability-phase8-corrected-20260901`. Its
independently recomputed SHA-256 is
`dbd6007b3d495334a6e749159be74ea979a4659f075a5378c7b7a224f8573c36`,
matching the expected digest. Every artifact named by that binding matched:

| Bound corrected Phase 8 input | File SHA-256 |
|---|---|
| `dev/active/setup-assistant-security-and-portability/setup-assistant-security-and-portability-plan.md` | `9ce42649427ec14c201af129e2d8781814905c6f70d9a23deac4c68d13b77649` |
| `dev/active/setup-assistant-security-and-portability/setup-assistant-security-and-portability-tasks.md` | `eef411ec153669b3d5284563b2f9063846ed3404d5a203ed28d43575d94aaf29` |
| `dev/active/setup-assistant-security-and-portability/setup-assistant-security-and-portability-context.md` | `992045fa95ff75e536084149658362f45e282e1c9ebebebbab6c8cec6ac42dc6` |
| `dev/active/setup-assistant-security-and-portability/setup-assistant-security-and-portability-clean-room-evidence.md` | `d0a0d0e1a581d6b930e2bfb7b66d8787acd8b8e933660d3844eea9ccf5d3687c` |
| `dev/active/setup-assistant-security-and-portability/setup-assistant-security-and-portability-dependency-evidence.md` | `ce005b80fd4853a93e2bc20a393e9f015387454738484f5854c70ce20be4bee1` |
| `dev/active/setup-assistant-security-and-portability/setup-assistant-security-and-portability-phase8-yaml-probe-evidence.md` | `39998d1f4f97c22399d60900d990bacfecd22d2548331a0e101839544b5ecf1b` |
| `dev/active/setup-assistant-security-and-portability/setup-assistant-security-and-portability-phase8-dependency-review.md` | `d055ae1ab361d1be3cfe3a54a589df76bdd36634f3a16cb4305483337af2f051` |
| `dev/active/setup-assistant-security-and-portability/setup-assistant-security-and-portability-phase8-security-review.md` | `83b07a08a533a763557ac0b0f8b801d89ce7570274ef0094da20fde75ab966cf` |
| `dev/active/setup-assistant-security-and-portability/setup-assistant-security-and-portability-phase8-cto-review.md` | `a732deaef47aac604dfc2003836a08b72827efde821efb46e4ff4f00c1632ce2` |
| `islamic-value-sensitive-design/i-vsd-setup-assistant-security-and-portability.md` (review preimage) | `f13b603a4429e233ff241a0161d86c0812b822dc7e5797997e1a0803ebd5dbf6` |
| `src/Event.Setup.Core/Event.Setup.Core.csproj` | `b038160cde81ff2b188739d3a8f67eb014323e5b46f8833e48fdc7c6b59737cf` |
| `src/Event.Setup.Core/packages.lock.json` | `d8d75d293ca094de8a27aaa566500176f5185cf1c06149668bb0a225efe8e8a8` |
| `tests/Event.Setup.Core.Tests/Event.Setup.Core.Tests.csproj` | `49f51e7f7c93c134bc359134be73fc1060033ba9c015265dbb50f9ff01f40d9e` |
| `tests/Event.Setup.Core.Tests/packages.lock.json` | `fae9592f8dc76d561c8741cc8469eee2d25413951fd286a86e011fe2f728b4c2` |
| `Directory.Packages.props` | `1cba249bd5e7520e7c4b1a5d22a4a44dc3fa251e80771ca44e009952441f529e` |

The corrected split is provider-responsibility aligned. C1 Red now makes every
material composition failure independently attributable and binds exact public
seams, ceilings, deterministic barriers, claimed-platform evidence, the Phase
8 Worst Break, and no-partial/value-free outcomes. C1 Green isolates Core parser
and canonical parity from presentation and scale pressure. C2 cannot change the
canonical default and cannot enable an unevidenced profile. Exact owned paths,
phase-attributable failure ownership, explicit-path staging, unrelated-state
preservation, material-override recording, and post-commit file/hash checks
make each slice auditable without granting commit authority.

The bound dependency review approves the exact one-node syntax-tree role, and
the bound security review supplies the retained adversarial constraints. The
bound CTO review predates the corrected tasks and concludes `Split before
approval`; its requested corrections are present in E069, but it does not
approve E069. Fresh CTO review of the corrected binding remains the sole
technical gate before C1 Red. The standing user direction is sufficient after
that approval for unchanged scope. No product preimage changed, and no package,
source, schema, generated artifact, adapter, profile, documentation, support,
release, shipping, or commit authority is granted here.

The current governance-only revalidation is bound by final Phase 8 binding ID
`setup-assistant-security-and-portability-phase8-final-20260901`. Its
independently recomputed SHA-256 is
`58a6673b0f1555467a28661ef110cbfb3b6aae2ce3b83a9f2b5342778c305169`,
matching the expected digest. Every named preimage matched before this report
update:

| Bound final Phase 8 input | File SHA-256 |
|---|---|
| `dev/active/setup-assistant-security-and-portability/setup-assistant-security-and-portability-plan.md` | `9ce42649427ec14c201af129e2d8781814905c6f70d9a23deac4c68d13b77649` |
| `dev/active/setup-assistant-security-and-portability/setup-assistant-security-and-portability-tasks.md` | `b76f04c0e0809d6b2c3ccec3b5f9a8da569a7b44d84eb8abd42ea536c1e720c5` |
| `dev/active/setup-assistant-security-and-portability/setup-assistant-security-and-portability-context.md` | `992045fa95ff75e536084149658362f45e282e1c9ebebebbab6c8cec6ac42dc6` |
| `dev/active/setup-assistant-security-and-portability/setup-assistant-security-and-portability-phase8-yaml-probe-evidence.md` | `39998d1f4f97c22399d60900d990bacfecd22d2548331a0e101839544b5ecf1b` |
| `dev/active/setup-assistant-security-and-portability/setup-assistant-security-and-portability-phase8-dependency-review.md` | `d055ae1ab361d1be3cfe3a54a589df76bdd36634f3a16cb4305483337af2f051` |
| `dev/active/setup-assistant-security-and-portability/setup-assistant-security-and-portability-phase8-security-review.md` | `83b07a08a533a763557ac0b0f8b801d89ce7570274ef0094da20fde75ab966cf` |
| `dev/active/setup-assistant-security-and-portability/setup-assistant-security-and-portability-phase8-cto-review.md` | `a732deaef47aac604dfc2003836a08b72827efde821efb46e4ff4f00c1632ce2` |
| `dev/active/setup-assistant-security-and-portability/setup-assistant-security-and-portability-phase8-corrected-cto-review.md` | `ff9c0099a9bd2fea7ec92d8a81ae1bcab6d45dbf3953b2ac8f88f745f180534b` |
| `islamic-value-sensitive-design/i-vsd-setup-assistant-security-and-portability.md` (review preimage) | `ccb3d207f06926d9d34d8695a85ce61e685b6474d4eaad1142ef4c5519b9c13f` |
| `src/Event.Setup.Core/Event.Setup.Core.csproj` | `b038160cde81ff2b188739d3a8f67eb014323e5b46f8833e48fdc7c6b59737cf` |
| `src/Event.Setup.Core/packages.lock.json` | `d8d75d293ca094de8a27aaa566500176f5185cf1c06149668bb0a225efe8e8a8` |
| `tests/Event.Setup.Core.Tests/Event.Setup.Core.Tests.csproj` | `49f51e7f7c93c134bc359134be73fc1060033ba9c015265dbb50f9ff01f40d9e` |
| `tests/Event.Setup.Core.Tests/packages.lock.json` | `fae9592f8dc76d561c8741cc8469eee2d25413951fd286a86e011fe2f728b4c2` |
| `Directory.Packages.props` | `1cba249bd5e7520e7c4b1a5d22a4a44dc3fa251e80771ca44e009952441f529e` |

E074 changes governance only. C1 Red now owns exactly two new files. C1 Green
uses the existing central pin read-only, excludes Red tests from commit paths,
and owns exact Core/lock/docs/change-fragment files with matching
`Change-Id: CHG-01M1C8MP8S1T10N8D3D5A7B9CX`. C2 owns only exact new files.
Every title, description paragraph, changelog outcome, trailer, and `Message
override: Not overridden` value is literal. Mixed-author paths block rather than
leak unrelated hunks into a slice. These corrections strengthen truthful,
auditable ownership under IVSD-F037/M037 without changing a matrix, ceiling,
public seam, parser role, platform claim, canonical authority, Worst Break,
default, provider decision, stakeholder impact, or product preimage. No new
finding or mitigation is needed.

E073 accepted the technical contract but required precisely these governance
corrections. Because E073 reviewed the predecessor revision, fresh CTO
confirmation must bind E074 before C1 Red. I-VSD grants no staging or commit
execution authority.

The current Phase 9 planning revalidation is bound by:

- **E075:** Phase 9 review binding
  `setup-assistant-security-and-portability-phase9-20260901`,
  `sha256:f1e33d2e2fe25c36abd1fde9c8d63dce47aa6f3ca69344aa215869d4b9e602f0`.
- **E076:** Phase 9 Tier 1 intake
  `sha256:54341bb7614b2415221769d3146017b69367b2ea88d0a1610779218eecacb866`.
- **E077:** Green Phase 8 machine/profile evidence and observed gates:
  generated profile record
  `sha256:24f7141ccf2ede8ee0f5fda96a7f11dd2e0cc3e36abeb7b3f41286f680e00333`,
  generated verification 4/4, scale 4/4, architecture 10/10, full Core 65/65,
  and Release build zero errors.
- **E078:** Exact existing authority preimages for ConfigurationImport's
  target-bound digest-only capability/session/API pattern,
  `ISecretResolver`'s one-source read-only boundary, `SecretBinding`'s
  value-free persistence role, authenticated integration fixtures, and both
  unchanged test projects, as enumerated in E075.

Every E075 hash was recomputed before this report update. Both D1 test paths
were absent. The intake resolves all repository-answerable Grill-Me branches:
normal bearer identity remains authoritative; tenant scope comes from trusted
route/request context; the new capability is purpose-bound, header-only,
digest-persisted, expiring, terminally revocable, generation-fenced, and
idempotency-fingerprinted; profiles contain protected handles only; secret
binding is allowlisted and write-only; provider coordinates and values never
cross the public boundary; HAL remains client action authority; and offline
authoring survives any live failure.

This is aligned with IVSD-F038/M038, IVSD-F039/M039, and IVSD-F040/M040. It
does not prove the future implementation. D1 remains tests-only and requires
fresh CTO/MAD plus exact-revision user approval. D2 additionally requires
passing HTTP/persistence/provider/log breakers, generated-contract review,
platform protected-store evidence or `ApprovedDisabled`, and no product or
release capability activation before Green.

Independent review then produced `changes-required`. The corrected
revalidation is bound by:

- **E079:** corrected Phase 9 binding
  `setup-assistant-security-and-portability-phase9-corrected-20260901`,
  `sha256:3b70f8f5f190ac0c4088d73b8d4f468bb6a8c03a6ccca39d4848d7e08fa48011`.
- **E080:** corrected Tier 1 intake
  `sha256:ff1f957f483e11f2be0d8921b5dafc240a8ab0bcec9578cb636c80a6642b1169`.
- **E081:** anonymized weighted MAD findings
  `sha256:147dfaafe9a04cd4a04c3e5133ad76a254fb2ad4f41429ae2d8fd8fb40e2ebf6`.

E079's hashes and both absent future test paths were recomputed. The corrected
intake accepts P9-001 through P9-007: value-free issuance idempotency, D1/D2
adapter-Red sequencing, current bearer/actor authorization on every
capability action, generation-aware application fingerprints, atomic and
dispatch revocation fences, exact token/failure shape, and fail-closed explicit
secret-binding mismatch before SA-930. P9-008 is contained as a pre-existing
registration-provider risk: Setup must not reuse raw callback payloads,
coordinate-rich event connection DTOs, or reusable embed tickets, while their
own remediation remains outside D1.

The correction strengthens IVSD-F038/M038, F039/M039, and F040/M040 without a
new moral finding. D1 is now one compilable HTTP integration-test file. The
Setup adapter Red moves to the SA-920 checkpoint after a real public/generated
contract exists and before adapter behavior, preventing both compile-only Red
and test-owned mirrors.

Fresh CTO review then returned `Changes required` because the current I-VSD
still contained two stale two-file/adapter-failure statements and the packet
did not bind literal machine-consumed endpoint contracts. Final revalidation is
bound by:

- **E082:** final Phase 9 binding
  `setup-assistant-security-and-portability-phase9-final-20260901`,
  `sha256:8fc5f86ebb1775a4fa0942d702dc18676ebae73329143ee2d100c445a8b61952`.
- **E083:** final D1 machine-contract amendment
  `sha256:41796242f3baccaea482380f808d3af6efb75ef7a7d1a2efa27bc49fc9cfbea4`.
- **E084:** corrected-packet CTO `Changes required` review
  `sha256:26221e0b9a146481907ad36a4d820b4d51e457b19cc0ee9febd48e6e85da1c9b`.

E082's hashes and both absent future paths were recomputed. E083 freezes the
exact API owner; seven method/routes and route names; capability/idempotency
headers; media, size, rate, timeout, success and cache contracts; closed request
and response fields; seven HAL relations; complete invalid-capability and
idempotency-conflict RFC 7807 tuples; three Domain/table identities; digest/
commitment-only persistence; writer/barrier identities; and closed
ActivitySource/Meter/instrument/operation/outcome vocabularies.

This makes the D1 404 attributable rather than guessed. Positive owner/status
assertions fail before deeper checks, so persistence/effect/observability
assertions do not pass vacuously. Negative flows first require a real positive
enrollment. D1 covers absent-owner and no-dispatch paths without defining a
mirror. Provider-success/inverse-dispatch, the explicit source-mismatch fix,
generated contracts, protected profiles, and adapter behavior remain D2 tests-
first gates after their real seams exist.

Every current I-VSD ownership statement now names one D1 API file. Adapter
absence is not a valid D1 failure; the future adapter Red remains SA-920-owned.
The final contract preserves IVSD-F038/M038, F039/M039, and F040/M040 and adds
no new finding.

The user then approved the exact final binding/report/CTO hashes by directing
the workstream to continue. D1 execution is bound by:

- **E085:** post-Red binding
  `setup-assistant-security-and-portability-phase9-post-red-20260901`,
  `sha256:494db8d6a5c0be3bb5f66b12f50ce70fe2995626355ae0545fc6021778133dd3`.
- **E086:** D1 Red evidence
  `sha256:149a27cdd0f4752e606e4a72b45207722764f754e016501abbb3a94033b66d1d`.
- **E087:** exact approval
  `sha256:4b26b5bba144dc389913839997d38643b7bf2975ae78304609203229d9ff9b24`
  and sole test
  `sha256:ba8af4190b7b04540cbe5d5112f19bb3f5764f07336da0987aad323456fcf028`.

The final API project build has zero D1 errors/warnings. The focused selector
discovers ten independent tests; all ten fail at the absent exact owner or the
first literal route status, with zero pass/skip. An initial test-local EF
assertion-order error was corrected before the final run. In the bound result,
no deeper persistence/effect/observability check executes before positive HTTP
ownership/status, and negative flows first require real enrollment.

This is valid evidence for IVSD-F038/M038, F039/M039, and F040/M040, not Green
implementation evidence. D2 remains blocked on fresh weighted post-Red review,
an exact inward-to-outward owner set, current I-VSD/CTO revisions, and exact
user approval. No new provider-responsibility finding emerged from D1.

The first post-Red review accepted the attributable absence Red but returned
`Changes required` for Green readiness. D2-0 correction is bound by:

- **E088:** D2-0 review binding
  `setup-assistant-security-and-portability-phase9-d2-0-final-20260901`,
  `sha256:b159e98a4202efff1cb5b175343d6befa093035160d3d6c9a90ded29ffa66a43`.
- **E089:** corrected Red evidence
  `sha256:cfc025406e86a19c435e38282453fc7694fd0298ab01255635456f14dd64c917`
  and corrected test
  `sha256:586be7e7af867b8d1df06ab198de6dcd9ee2b21d7d249ba44c4567345f6054e3`.
- **E090:** exact correction proposal/approval/review chain:
  proposal
  `sha256:365792ba799bc540b33c6942cdfb3cfa407659df633de00443a016a5e3fa6951`,
  approval
  `sha256:84f405f37a7deb3dd994a9c6c4570c146e8aaa8056d75548aec5c0236e305d7d`,
  and Changes-required review
  `sha256:fa6d9ad2f13624e3e8b0af6a5abb2477fb7eea7d553c6b1ce672028561d00994`.

E088's hashes were recomputed. The corrected test isolates every database/
tenant/actor/time/authorization/telemetry state, owns every request/response
once, and coordinates only on value-free structured event
`SetupLiveMilestone` (`19620`) with exact operation/milestone fields. The
focused result remains ten exact absent-owner/route failures with zero
pass/skip and no fixture, reset, disposal, telemetry, EF, environment, timeout,
or unrelated failure.

D1 claims are now epistemically bounded: it does not claim writer or
resolver/source call counts, provider success/idempotency, final
authorization-before-dispatch ordering, or inverse winner. Those are staged
tests-first after the real D2 static seams exist. The D2-1 through D2-11
inward-to-outward sequence is bound but not implemented. This remains aligned
with IVSD-F038/M038, F039/M039, and F040/M040 and introduces no new
provider-responsibility finding.

Fresh D2-0 review accepted isolation, ownership, and claim scope but returned
`Changes required` because the barrier did not match event ID/name and the
canonical ledgers omitted the bound D2 stages/providers. Final D2-0b is bound
by:

- **E091:** D2-0b review binding
  `setup-assistant-security-and-portability-phase9-d2-0b-final-20260901`,
  `sha256:6fac4deb374ad98f1d9baad71e129d150ca8a841311f5633191d52d3993b92ea`.
- **E092:** D2-0b evidence/test:
  evidence
  `sha256:3e1406058fc89a92e591c5da3f1509b0998a9c8a183d71205e1c472f85205f93`
  and test
  `sha256:131784835bd0b81f885c2f553decb56b31e5f5dfe3b20eb465072f634f2b48ed`.
- **E093:** proposal/approval/review and ledgers:
  proposal
  `sha256:2769c37e9a58ae1691092e5acc6b43236eb419a81e158b48d4eecc9af656423c`,
  approval
  `sha256:6dbac904f044dc89c7e6be368b71b0a5e3556bfa1f03110a3af63d7519f4fccf`,
  Changes-required review
  `sha256:c6a4bc75f9a887802eb5fa518b055902980869f60e7786f9ecaafd6ea4741cc2`,
  plan
  `sha256:cbe493c4540ad230c6ee02665305724f8e28dcd5fe009b8984c981e78e158403`,
  tasks
  `sha256:211e1ea2e093b1613be0bc132ad24973fbc50a2925b2e80232761a1caac3fe5c`,
  and context
  `sha256:eadd443ff803f4b1ec886eb269b5d93ee64b2c1a7d5ea903abc83dc9b04ef053`.

The barrier now matches `EventId(19620, "SetupLiveMilestone")` and exact
structured operation/milestone values. The source-mismatch test asserts only
future HTTP `invalid` response shape plus value/coordinate exclusion; zero
resolver/source calls remain D2-6 evidence. The focused Red remains ten exact
failures with a clean build/diagnostics.

Plan/tasks/context now expose D2-1 through D2-11 and all five generated
migration providers. No migration, provider, product, client, protected
profile, adapter, or capability behavior exists. This remains aligned with
IVSD-F038/M038, F039/M039, and F040/M040 without a new finding.

D2-0b technical review then returned `Changes required` only because canonical
tasks/context still advertised obsolete resume state. Status-only D2-0c is
bound by:

- **E094:** D2-0c review binding
  `setup-assistant-security-and-portability-phase9-d2-0c-final-20260901`,
  `sha256:83bdf2889ed66f5c4aab6e407d025d15e6b8d6e53fe03ea25afee37c39f70c34`.
- **E095:** D2-0c correction/approval/evidence:
  correction
  `sha256:e93af09ac989c936c82cce23bc11117cc27d59c5a06165c642286743cf5dd6eb`,
  approval
  `sha256:9ea7773cb3abb1012060721ce462927935554c24109e696b3bd04d788b5a5f86`,
  and evidence
  `sha256:ef101a63285e88a592a0482a12b26c06b30cc4407a5175c30dd7e45001fcf7d0`.
- **E096:** accepted-technical/metadata-blocked review and corrected ledgers:
  review
  `sha256:4e2b243aeed3872606652481858d233034bc3c8d59de801b4b93e7be8c13bb2c`,
  tasks
  `sha256:b5746719a5dd06908f7a739b69870653bfcc7a7edcdab171e288ab955d543ea5`,
  context
  `sha256:31b15a0cf9655d0b53d332f52ed87539c84f71ec6b12c393db73af02e591e8dc`,
  and unchanged plan
  `sha256:cbe493c4540ad230c6ee02665305724f8e28dcd5fe009b8984c981e78e158403`.

Tasks/context now agree: D2-0b technical Red/staging is accepted, D2-0c is
status-only, final review plus explicit user `approve` is next, and D2-1
package-free Wire contract Red is the sole first product slice. Later layers
and all five migration generators remain gated in order; capability flags stay
false.

No executable/product evidence changed after D2-0b. D2-0c adds no finding and
does not alter IVSD-F038/M038, F039/M039, or F040/M040.

User decisions on 2026-08-29 additionally establish:

- web and desktop targets;
- no-secret web mode as default;
- optional official-host secret mode;
- secret values remain client-side;
- relevant empty secret placeholders;
- omission of irrelevant/defaulted variables;
- explanatory generated header;
- open browser source with only generated `wwwroot`/artifacts ignored;
- portable instance and tenant Terms, Privacy, and broader legal texts;
- legal templates and Markdown editing;
- FOSS-only dependencies with no commercial/proprietary components and
  target-specific GPL/AGPL compatibility review;
- an interactive terminal workflow plus versioned CLI commands (historically
  satisfied in A by a BCL wizard and now re-baselined to the sole Terminal.Gui
  target plus a separate noninteractive CLI);
- an external agentic skill instead of embedded AI.

Historical planning revalidation on 2026-08-30 reviewed:

- `dev/active/setup-assistant-security-and-portability/`
  `setup-assistant-security-and-portability-clean-room-evidence.md`;
- the plan, context, and task triad for the same workstream;
- the current active ConfigurationManifest context/tasks and v1alpha2 contract,
  portability registry, import-preview, legal Markdown, and shared
  `Event.Wire.Contracts` boundaries;
- current official Avalonia WebAssembly, supported-platform, accessibility, and
  licensing documentation;
- current official Terminal.Gui documentation and package metadata;
- .NET 10 CSP and Unix file-mode documentation, Windows SignTool, Apple
  notarization, SLSA provenance, and Flatpak sandbox/portal guidance.

Historical planning-mode revalidation on 2026-08-31 additionally reviewed
expanded Scenarios 3.13–3.15, Phases 8–11, SA-810–SA-1250, and the BCL-only A
strategy. The prior B1 final-Red revalidation bound all 17 artifacts in that
binding and reviewed only non-forgeable completion authority, retained
final-generation exhaustion, exact Toolkit lock content identity, governed
commit override state, the unchanged Red taxonomies, and unchanged product and
target authority without ingesting external implementation source.

## Missing Evidence

- a provenance-complete B1 Avalonia/Terminal.Gui graph, or new authoritative
  Avalonia binary/component/license/notice and `Avalonia.Remote.Protocol`
  publish-exclusion evidence;
- authoritative license record for every selected transitive/native artifact;
- independent review of the planned threat model and exact-build misuse cases;
- official host deployment architecture;
- browser compatibility matrix;
- main-document/release reproducibility proof;
- desktop filesystem/ACL behavior matrix;
- code-signing and notarization credentials/process;
- Linux package repository/signing strategy;
- user research;
- accessibility and RTL audits;
- legal privacy/trademark/source-offer review;
- incident and vulnerability response capacity for the new product;
- approved Setup template/authority handoff matrix;
- legal template provenance and counsel review;
- legal publication/acceptance migration semantics;
- localized legal-content size and usability evidence;
- final release evidence for the implemented CLI command/JSON/exit contract;
- BCL terminal security/accessibility/TTY/echo/signal/resize/scrollback support
  matrix and separate no-secret Terminal.Gui target evidence;
- skill routing, resources, compatible CLI range, and executable examples;
- Project Steward/legal decision for any reciprocal AGPL-only target;
- fresh weighted post-Red review bound to E085-E087/current-report revision,
  an exact D2 owner/file/staging set, and exact-revision user approval before
  D2 Green;
- D2 Green live enrollment/capability/revocation, cross-target/tenant, replay,
  HAL-synthesis, protected-profile downgrade, RFC 7807, log/support leakage,
  provider-fallback, and secret-readback evidence turning the observed D1 API
  Red Green without weakening it;
- D2 real HTTP/persistence/provider integration evidence, generated contract
  review, deterministic time/concurrency coordination, and terminal revocation;
- platform protected-credential-store evidence for every claimed target, with
  unsupported targets recorded `ApprovedDisabled`;
- exact target-local provider allowlist and proof of value/coordinate-free
  write/readiness contracts;
- category-level application-data/PII custody, purpose, compatibility,
  retention, staging-disposal, and source-retention records;
- concurrent resume/idempotency, mapping, integrity, outbox, erasure replay,
  and anti-resurrection evidence;
- Tier 0 hold/finalization, payout routing, and partial-refund/fee allocation
  decision record required before SA-1110;
- exact provider/ledger/payment reconciliation and recovery rehearsal required
  before SA-1140; and
- stakeholder evidence for migration comprehension, category autonomy,
  pending/unknown status, and recovery usability.

## Context Inventory

Reviewed:

- current configuration, secrets, self-hosting, security, accessibility,
  localization, footer/legal identity, governance, and licensing
  documentation;
- current `.env.example` and Compose deployment surface;
- secret registry and manifest portability report;
- current typed legal documents and role-labelled Terms/Privacy pages;
- solution/project/package structure;
- historical official Avalonia and Terminal.Gui functional/package context
  retained from prior reviews;
- the repository-local sanitized dependency handoff that supersedes candidate
  package assumptions for successor A;
- the exact final-Red binding, all 17 bound planning/evidence/review/test
  artifacts, and the three unchanged product dependency preimages;
- the initial and corrected Phase 8 bindings/reviews; and the final Phase 8
  binding with all fourteen named planning/review/report/Core/test/lock/
  central-pin preimages, unchanged technical contract, exact final ownership,
  literal commit copy, change-fragment/Change-Id, and mixed-author blockers;
- Green Phase 8 bounded composition, scale profiles, generator/evidence,
  focused/full test results, architecture closure, and Release build result;
- the exact Phase 9 intake/binding, current ConfigurationImport capability and
  HTTP boundaries, current secret resolver/binding boundaries, authenticated
  integration fixtures, and absent D1 test/product owners;
- the exact D1 approval, sole API test, compile/discovery/failure output,
  post-Red evidence/binding, and continued absence of every product and future
  adapter owner;
- existing repository CLI and skill-authoring conventions;
- official browser CSP/SRI guidance and .NET file/random APIs;
- user’s initial architecture proposal and resolved web-mode decisions.

Not reviewed:

- Avalonia, Terminal.Gui, or other third-party source code/prose;
- rendered UI, browser, desktop, terminal, assistive-technology, support,
  release, or shipping behavior;
- Phase 10 live configuration-operation behavior or any Phase 11 application-
  data/payment migration behavior;
- SA-120 restore/build output or generated locks for the proposed A graph;
- raw external commercial tooling terms or package payloads;
- external competitor implementation or UI;
- user secrets or private deployment configuration;
- production logs, incidents, support cases, or stakeholder interviews.

## Planning Requirements

A later implementation plan should map every open/accepted finding and
mitigation into:

1. product scenarios and claim vocabulary;
2. pure shared-core extraction;
3. environment catalogue and generated artifacts;
4. manifest/package workspace;
5. no-secret web workflow;
6. secret web threat model and invariant-breaker tests;
7. desktop safe-write adapters;
8. browser CSP/network/storage tests;
9. accessibility/localization/RTL;
10. FOSS/reciprocal per-target dependency proof;
11. Windows/Linux/macOS/web packaging and signing;
12. source/provenance/reproducibility;
13. legal/privacy/trademark review;
14. operator and security documentation;
15. release and incident evidence;
16. typed instance/tenant legal-document contracts and lifecycle;
17. safe Markdown parser/editor and public renderer;
18. project-owned or approved-FOSS legal-template provenance;
19. legal import/publication/acceptance invariant-breaker tests;
20. localized legal-content accessibility and size evidence;
21. sole Terminal.Gui human target and separate noninteractive CLI adapter;
22. versioned machine JSON and stable exit categories;
23. terminal secret invariant-breaker tests;
24. FOSS/reciprocal license target map and SBOMs;
25. external-agent approval and no-secret scenarios;
26. fail-closed enforcement of A's blocked-package exclusions, package-free
    disabled shells, and no approval inheritance;
27. successor-B framework-neutral GUI/browser/desktop mappings plus fresh
    exact-graph and authority gates;
28. a schema-compliant skill created only after CLI implementation;
29. architecture tests proving no embedded AI/provider dependency;
30. canonical JSON/YAML/directory composition and bounded scale;
31. live target/tenant enrollment, revocation, and replay fencing;
32. write-only target-local secret binding without provider-coordinate exposure;
33. HAL-authoritative live apply, transfer, cancellation, receipt, and recovery;
34. application-data category custody, privacy, retention, and source retention;
35. durable tenant-qualified mappings, checkpoints, idempotency, integrity, and
    outbox effects;
36. authority-first erasure replay and anti-resurrection through migration;
37. Tier 0 hold/finalization, payout, and partial-refund/fee decisions;
38. provider/ledger/recipient/currency/refund reconciliation before money
    mutation;
39. granular value-free recovery evidence and truthful pending/unknown states;
40. category-level human approval and no agent authority broadening.

Planning owns architecture sequencing and task status. This report owns
provider-responsibility constraints and refresh triggers.

## D2-11 I-VSD Revalidation

This revision revalidates `IVSD-F038/M038`, `IVSD-F039/M039`, and
`IVSD-F040/M040` against the implemented D2-4 through D2-11 boundary: durable
tenant/actor-bound enrollment authority, generation and expiry fencing,
UUIDv7 idempotency, server-authored HAL affordances, protected-handle-only
profile state, value-free readiness/receipts, and a write-only target-local
secret seam. The separately owned SetupLive outer adapter obtains a fresh
caller-supplied bearer, keeps the enrollment capability only in memory, and
clears it on every terminal or ambiguous failure.

The findings and mitigations remain unchanged. The implementation does not
transfer source authority, expose provider coordinates, add a secret readback
path, persist a bearer/capability/raw value, create a saved profile, or fall
back from the target's selected authority. Infisical is the only current live-
write provider; Environment and User Secrets remain non-writable through this
boundary.

This revalidation grants no release activation. The generated SetupLive
capability manifest must retain the exact closed root and keep `targetEnabled`,
`targetEnrollment`, `secretBindingReadiness`, `secretBindingWrite`, and
`savedProfiles` false. Phase 10/11 behavior, GUI exposure, shipping, support,
and payment authority remain outside this review.

## Terminal.Gui-only Steward Revalidation

The 2026-09-01 Steward decision revalidates `IVSD-F013/M013`,
`IVSD-F029/M029`, `IVSD-F031/M031`, and `IVSD-F035/M035` without adding or
renumbering a finding. Official Terminal.Gui 2.4.17 as-published remains
blocked. Phase 5R may instead build the distinctly named
`ISLAMU.Terminal.Gui` `2.4.17-islamu.1` package only from official tag
`v2.4.17`, commit `d0a0ed9b150d3fc8aacf4ab07b7f7d91264fe6d6`, with the
grammar/editor integration removed and upstream MIT license, copyrights, and
attribution preserved.

The patched artifact receives no inherited approval. Its actual final
dependency closure, SBOM, notices, vulnerabilities, and outbound-license paths
must pass independently, and CI must fail if `TextMateSharp.Grammars` returns.
The package is a temporary downstream packaging patch with a recorded series
and migration path back to a suitable official modular release, not an
ISLAMU-owned divergent framework.

Once those gates pass, `Event.SetupAssistant.Terminal` is the sole human
terminal and secret-completion target. `Event.SetupAssistant.Cli` remains
machine/noninteractive, the former BCL wizard is deleted, and no console or
custom fallback survives. The Terminal.Gui target owns masked secret memory
outside shared ViewModels/messages and writes directly through the protected
file boundary. Legal authoring remains file-based; no replacement editor,
syntax highlighter, grammar corpus, or speculative terminal framework is
introduced.

## Planning Handoff

- **Workstream:** `setup-assistant-security-and-portability`
- **Status:** current
- **Reviewed input revision:**
  plan `sha256:752972e2b5e85e2bbb792a92618fabb6cffccdb6bccb278d222c4ed8ee6681d9`;
  tasks `sha256:470fa034846bebb517933b4a49ea18a24be53adc39e3fc85165859b81e511951`;
  context `sha256:e31e5ef74fbffb99ca1442f0e0bf300dac2e79031868589fb98beb8e9de4c0f0`;
  Steward approval `sha256:b88937468eb334d55622cd92a3fd892352d616007bdbeb4071855d3392fd9a14`
  (binding ID
  `setup-assistant-terminal-gui-only-rebaseline-20260901`).
- **Findings and mitigations:** `IVSD-F001` through `IVSD-F046` remain accepted
  and map one-to-one, without renumbering, to `IVSD-M001` through
  `IVSD-M046`. No new stable finding or mitigation is required: IVSD-F037/M037
  already governs source ambiguity and evidence-bound scale.
  IVSD-F038/M038, F039/M039, and F040/M040 now govern exact D1 tenant,
  capability/replay/HAL, protected-profile, provider-coordinate, write-only
  secret, and readback boundaries; F002/F009/F020/F022 preserve canonical Core,
  secret separation, no provider extraction, and evidence limits.
- **Required plan mappings for preserved scope:** Plan Section 9 maps
  `IVSD-F001/M001` through `IVSD-F036/M036` to Scenarios 3.1–3.12 and 3.16 and
  SA-110–SA-1240, including SA-515/SA-520/SA-525R/SA-526/SA-527/SA-530R/
  SA-540R for the re-baselined human presentation. SA-1250 performs final
  I-VSD/criticality reconciliation
  and disables any unevidenced shipped capability.
- **Required plan mappings for expanded scope:**
  - `IVSD-F037/M037` -> Scenario 3.13; SA-810, SA-820, SA-830, SA-1220,
    SA-1250.
  - `IVSD-F038/M038` -> Scenarios 3.14 and 3.15; SA-910, SA-920, SA-1010,
    SA-1030, SA-1110, SA-1120, SA-1130, SA-1250.
  - `IVSD-F039/M039` -> Scenario 3.14; SA-910, SA-920, SA-1010, SA-1020,
    SA-1030, SA-1250.
  - `IVSD-F040/M040` -> Scenario 3.14; SA-910, SA-930, SA-1220, SA-1250.
  - `IVSD-F041/M041` -> Scenario 3.15A; Tier 2 custody/erasure gate before
    SA-1110; SA-1110, SA-1120, SA-1130, SA-1220, SA-1250.
  - `IVSD-F042/M042` -> Scenario 3.15A; SA-1110, SA-1120, SA-1130,
    SA-1250.
  - `IVSD-F043/M043` -> Scenario 3.15B; Tier 0 decision gate before SA-1110
    and provider/ledger reconciliation gate before SA-1140; SA-1110, SA-1140,
    SA-1220, SA-1250.
  - `IVSD-F044/M044` -> Scenarios 3.14A and 3.15A; SA-1030, SA-1120,
    SA-1130, SA-1220, SA-1250.
  - `IVSD-F045/M045` -> Scenarios 3.14A–B and 3.15A–B; SA-1010, SA-1020,
    SA-1030, SA-1110, SA-1120, SA-1130, SA-1140, SA-1220, SA-1250.
  - `IVSD-F046/M046` -> Scenarios 3.12, 3.14, and 3.15; SA-1020, SA-1030,
    SA-1110, SA-1130, SA-1140, SA-1240, SA-1250.
- **Disposition:** `plan-aligned` through the observed D2-11 fail-closed release
  boundary under IVSD-F038/M038, F039/M039, and F040/M040. This disposition
  grants no Phase 10/11 operation, GUI activation, staging, commit, support,
  release, shipping, legal-certification, or scholarly authority.
- **Satisfied D2 chain:** D2-1 through D2-10 provide the closed Wire, Domain,
  Application, Persistence, provider-write, API/OpenAPI/generated-client, and
  outer-adapter boundaries with focused invariant evidence. D2-11 adds only the
  exact generated disabled release manifest, CI execution/routing ratchets, and
  operator documentation. The full API project was executed on 2026-09-01:
  2,520 passed, 28 failed, and 1 skipped; no SetupLive failure was observed,
  but the repository-wide phase-exit gate is red. No capability may activate
  and Phase 10 may not start while that gate remains incomplete.
- **Approval boundary:** successors A, the selected/disabled B slices, and C
  are Green under their own historical exact approvals. The user explicitly
  approved the reviewed staged D sequence. This revalidation covers only the
  implemented D2 fail-closed boundary and supplies no Phase 10/11 authority.
- **Satisfied predecessor gates:** SA-518 and Phase 8 completed their exact
  focused, architecture, graph, evidence, and Release gates. Every later
  restore/build/test/publish evidence run still sets
  `DOTNET_CLI_TELEMETRY_OPTOUT=1`,
  `DOTNET_SKIP_FIRST_TIME_EXPERIENCE=1`,
  `DOTNET_CLI_WORKLOAD_UPDATE_NOTIFY_DISABLE=1`, `DOTNET_NOLOGO=1`, and an
  isolated package cache.
- **B0 lifecycle:** verified superseded, non-executable, non-authorizing,
  never user-approved, and replaced before probing across the historical B0
  triad, intake, binding, CTO review, and separate I-VSD report. B0 cannot be
  revived or re-baselined as a fallback, and no conditional disposition,
  digest, finding, or mitigation transfers to B1.
- **Successor-A dependency boundary:** A's headless Core, machine CLI,
  `Event.Wire.Contracts`, and approved exact YamlDotNet graph remain. Its BCL
  wizard is superseded and removal-only. Official Terminal.Gui 2.4.17
  as-published remains blocked; Avalonia 12.1.1 is neither pinned nor restored.
- **Successor-B boundary:** B retains all user-directed human-presentation,
  browser, desktop, accessibility, self-hosting, protective-default, and
  portability outcomes. CommunityToolkit.Mvvm `8.4.2` is approved only for the
  shared model; Microsoft DI `10.0.10` plus Abstractions `10.0.10` is approved
  only at executable roots. Avalonia shared/Browser/Desktop `12.1.1` remain
  `ApprovedDisabled`. Terminal.Gui is now the required sole human terminal
  target, but stays inactive until Phase 5R proves the exact internal patched
  package and target. Browser/Desktop and Terminal.Gui own target-local secret
  sessions; machine CLI/Core remain presentation-free.
- **SA-910/SA-920 boundary:** the live backend and outer adapter are implemented
  and focused Green, but the generated release manifest keeps every capability
  disabled. Their presence grants no UI, distribution, or migration-operation
  authority.
- **Mandatory before SA-1110:** Tier 2 category custody/PII/retention/staging,
  authority-first erasure, anti-resurrection, and value-free receipt decisions;
  and Tier 0 decisions binding hold-expiration/finalization precedence,
  `OrganizerDirect` payout routing, and partial-refund/fee allocation. Any
  unresolved branch leaves Phase 11 disabled.
- **Mandatory before SA-1140:** exact target/provider/ledger/recipient/currency/
  refund reconciliation evidence, authorized approval actors, idempotent
  unknown-outcome recovery, and the explicit payment/provider decision record.
  No fail-open or guessed provider behavior is permitted.
- **Escalations required before release:** exact-graph dependency/license and
  component-provenance review, proved publish exclusions, security review for
  hosted secret mode and live capabilities, legal review for origin/templates/
  payment claims, target accessibility evidence, privacy and payment
  operational rehearsal, and release-engineering signing/package approval.
  Missing evidence leaves the exact capability or target disabled.
- **Refresh triggers:** product scope, stakeholder or provider authority,
  composition semantics/limits, target enrollment/HAL/capabilities, secret
  write/readback/provider-coordinate behavior, data categories/custody/PII/
  retention/erasure, mapping/checkpoint/idempotency, source retention, payment/
  refund/payout/reconciliation, recovery claims, human approvals, dependency
  graph/license/provenance/notices/publish role, package or framework selection,
  shell activation, target support, signing/provenance, approval revision, or
  any mapped mitigation/task changes materially.

## Review Lifecycle

| Date | Previous status | New status | Trigger | Evidence/replacement |
|---|---|---|---|---|
| 2026-08-29 | none | draft | Initial cross-platform Setup Assistant consultation | Repository evidence and official framework/security references |
| 2026-08-29 | draft | current / ready-for-planning | User selected default no-secret web mode plus optional trusted official-host secret mode | Initial report revision |
| 2026-08-29 | current / ready-for-planning | current / ready-for-planning | User added portable instance/tenant legal texts, templates, Markdown editing, and broader legal-content QoL | This revision, reviewed input `sha256:b4ebca52a625ba32daaefd6f2517b0f41ffc3833e2bcda023de608e953b96c11` |
| 2026-08-29 | current / ready-for-planning | current / ready-for-planning | User confirmed open browser source, broadened to compatible FOSS, and added Terminal.Gui CLI/TUI plus an external agentic skill | This revision, reviewed input `sha256:b053b7f69ca3822efbd1dc2333d2138d6361df8dd5eade311a2f43e2532b17ef` |
| 2026-08-30 | current / ready-for-planning | current / plan-aligned | Repository-grounded implementation plan, clean-room evidence, scenarios, architecture, tasks, and release gates completed | Workstream evidence `sha256:c76acd050aa7b0bf1e49a2bb1cc7634e470566489be828f7610586c49cdc27aa` |
| 2026-08-30 | current / plan-aligned | current / plan-aligned | User closed ConfigurationManifest for archival; Setup now pins the frozen current baseline and rejects capability overclaim | Workstream evidence `sha256:8c86d6be6f612861bba9c4ea641a451722fe0d6b5feccad09ac310b5cdce1637` |
| 2026-08-31 | current / plan-aligned | stale / changes-required | User promoted ConfigurationManifest deferred composition, live authority, secret-provider, application-data, and payment-operation scope into Setup phases | Expanded plan Scenarios 3.13–3.15 and Phases 8–11 require fresh findings and revision binding |
| 2026-08-31 | stale / changes-required | current / plan-aligned | Planning-mode revalidation added IVSD-F037–F046/M037–M046, resolved repository-answerable privacy/payment defaults, mapped every accepted ID, and preserved fail-closed Tier 0/1/2 gates | Exact four-file review revision `sha256:055fb1dd8c0dfcdbd809bbfb89cbd2660904469fd3d866d6d6349af091793d4f` |
| 2026-08-31 | current / plan-aligned | stale / changes-required | SA-120 blocked Terminal.Gui/Avalonia graphs and replaced A with a BCL-only terminal strategy while moving GUI graph selection to B | Changed plan/tasks/context/clean-room/dependency evidence; prior CTO/user approvals became revision-obsolete |
| 2026-08-31 | stale / changes-required | current / plan-aligned | Planning-mode revalidation preserved F001-F046/M001-M046, bound fail-closed replacement/no-inheritance to existing controls, and confirmed framework-neutral B outcomes | Exact five-file review revision `sha256:d2bbba40455c013e20883ab6202f84411bb05f2c20f6060a9e73095f44a8e4b1` |
| 2026-08-31 | current / plan-aligned | current / plan-aligned | Successor A became Green and the user directed a WPF-style shared human-presentation architecture; revalidation mapped Scenario 3.16 and SA-515/SA-520/SA-525 without changing provider authority, protective defaults, data flows, or release gates | Exact ordered five-file review revision `sha256:0576be649c1b1f7afc8e40e2c7ada54aec7cc1109d88e889de3a38b73e4ac265` |
| 2026-08-31 | current / plan-aligned | current / plan-aligned | A concurrent unapproved B0 Razor/browser workstream appeared; the umbrella review preserved it untouched, classified it as non-transferable overlap, and added a mandatory supersession/re-baseline gate for the newer B1 direction | Exact ordered five-file review revision `sha256:f087c810ef74c31685c5472e33bfbd865ee21f3519192e3a57deb301c791bdc6` |
| 2026-08-31 | current / plan-aligned | current / plan-aligned | Fresh B1 planning-mode revalidation verified B0's superseded/non-authorizing lifecycle and bound the CommunityToolkit-only model, target-root DI, per-session messaging, generation fencing, target-owned secret sessions, adapter states, six-stage non-shipping probes, accessibility/support truth, clean-room/outbound licensing, and later gates without granting probe or delivery authority | B1 binding `setup-assistant-security-and-portability-b1-20260831`, `sha256:bcadc5f8e8d7eba68a198629be694acf787e412e06270d8dce6f62dbf52ce4b7` |
| 2026-08-31 | current / plan-aligned | current / plan-aligned | Post-Red revalidation approved SA-518 shared-model progression under preserved F001-F046/M001-M046 after exact Toolkit/DI role approvals, accepted the ten-test absent-owner Red, preserved disabled target and claim boundaries, and made the safe CLI environment mandatory | Post-Red binding `setup-assistant-security-and-portability-b1-post-red-20260831`, `sha256:e67b951bf34652fe507d5c28aa2fc8880cf1c5c6fc942448aa079ef53f0d04cb`; fresh bound CTO review remains required before SA-518 |
| 2026-08-31 | current / plan-aligned | current / plan-aligned | Corrected-Red revalidation accepted all five CTO correction groups as stronger evidence for preserved F001-F046/M001-M046, moved adapter ownership to architecture checks, bound exact graph and independent SA-518 closure, and kept all product preimages and target claims unchanged | Corrected-Red binding `setup-assistant-security-and-portability-b1-corrected-red-20260831`, `sha256:d76bbf90507749a0f189ed6e6493f781afed168864c81efa935d9e77ab3e3f2d`; fresh bound CTO verdict remains required before SA-518 |
| 2026-08-31 | current / plan-aligned | current / plan-aligned | Final-Red revalidation closed the four residuals with no public settlement injection, two real completion continuations, retained in-flight exhaustion rejection, exact Toolkit content-hash ratcheting, and explicit conventional-commit/override governance while preserving all product and claim exclusions | Final-Red binding `setup-assistant-security-and-portability-b1-final-red-20260831`, `sha256:ae262c99d9fb39b7263e057b1cfc9f10ec6b5f89637c91ca80512881e0bacf49`; fresh bound CTO verdict remains required before SA-518 |
| 2026-09-01 | current / plan-aligned | current / plan-aligned | Phase 8 revalidation recomputed every bound preimage, evaluated isolated YamlDotNet syntax-tree evidence and product non-drift, preserved F001-F046/M001-M046, aligned SA-810 Red, and made SA-820/SA-830 conditional without granting package, security, dependency, CTO, support, or shipping authority | Phase 8 binding `setup-assistant-security-and-portability-phase8-20260831`, `sha256:a9a1e4f05de526a6dc2af5e407ebcf118c49d6daa66b163dddb12826f0326ae1`; probe evidence `sha256:39998d1f4f97c22399d60900d990bacfecd22d2548331a0e101839544b5ecf1b` |
| 2026-09-01 | current / plan-aligned | current / plan-aligned | Corrected Phase 8 revalidation bound the C1 Red/Core-only C1 Green/scale-only C2 split, fourteen matrices, exact ceilings/public seam/barriers, Linux/Windows claim evidence, Phase 8 Worst Break, unchanged defaults/product preimages, and exact per-slice ownership/verification/commit governance while preserving F001-F046/M001-M046 | Corrected binding `setup-assistant-security-and-portability-phase8-corrected-20260901`, `sha256:dbd6007b3d495334a6e749159be74ea979a4659f075a5378c7b7a224f8573c36`; fresh corrected-revision CTO approval remains required before C1 Red |
| 2026-09-01 | current / plan-aligned | current / plan-aligned | Final governance-only revalidation preserved every technical/provider-responsibility constraint and F001-F046/M001-M046 while binding exact two-file Red, central-pin-read-only exact Green with new docs/change fragment/Change-Id, new-file-only C2, literal messages/footers/override state, mixed-author blockers, and post-commit file/hash verification | Final binding `setup-assistant-security-and-portability-phase8-final-20260901`, `sha256:58a6673b0f1555467a28661ef110cbfb3b6aae2ce3b83a9f2b5342778c305169`; fresh final-revision CTO confirmation remains required before C1 Red |
| 2026-09-01 | current / plan-aligned | current / plan-aligned | Phase 9 planning revalidation recorded Green Phase 8 predecessor evidence and bound D1's normal bearer-to-purpose-capability, route-tenant, replay, protected-handle, write-only provider, no-readback, HAL-only, and exact two-file Red contract without granting live implementation authority | Phase 9 binding `setup-assistant-security-and-portability-phase9-20260901`, `sha256:f1e33d2e2fe25c36abd1fde9c8d63dce47aa6f3ca69344aa215869d4b9e602f0`; fresh CTO/MAD and exact-revision user approval remain required before D1 |
| 2026-09-01 | current / plan-aligned | stale / changes-required | Independent review found secret-bearing generic idempotency replay, absent-adapter Red contradiction, actor/authorization continuity gaps, generation-blind replay, revocation/effect race, underspecified token/failure shape, and explicit secret-binding fallback | Anonymized MAD findings `sha256:147dfaafe9a04cd4a04c3e5133ad76a254fb2ad4f41429ae2d8fd8fb40e2ebf6` |
| 2026-09-01 | stale / changes-required | current / plan-aligned | Corrected revalidation accepted every relevant finding, narrowed D1 to one compilable API Red, moved adapter Red to SA-920 contract-first TDD, bound current bearer/actor authorization, value-free issuance idempotency, generation-aware replay, atomic/dispatch revocation fencing, exact capability/failure shape, and explicit binding mismatch rejection | Corrected binding `setup-assistant-security-and-portability-phase9-corrected-20260901`, `sha256:3b70f8f5f190ac0c4088d73b8d4f468bb6a8c03a6ccca39d4848d7e08fa48011`; fresh corrected CTO/MAD and exact-revision user approval remain required |
| 2026-09-01 | current / plan-aligned | stale / changes-required | Corrected CTO review found two stale D1 ownership statements and no literal route/HAL/ProblemDetails/persistence/observability contract, making a guessed 404 and deeper negative assertions non-attributable | CTO review `sha256:26221e0b9a146481907ad36a4d820b4d51e457b19cc0ee9febd48e6e85da1c9b` |
| 2026-09-01 | stale / changes-required | current / plan-aligned | Final revalidation removed all two-file/adapter-failure D1 authority and bound the exact owner/routes/metadata/shapes/HAL/problems/records/effect/telemetry contract, positive-before-deep assertion order, and D1 no-dispatch versus D2 provider-success split | Final Phase 9 binding `setup-assistant-security-and-portability-phase9-final-20260901`, `sha256:8fc5f86ebb1775a4fa0942d702dc18676ebae73329143ee2d100c445a8b61952`; fresh final CTO/MAD and exact-revision user approval remain required |
| 2026-09-01 | current / plan-aligned | current / plan-aligned | Exact user approval authorized one API test; final D1 compiled and discovered 10/10 tests, all ten failed only for the absent exact owner/literal routes with no pass/skip or premature deeper assertion | Post-Red binding `setup-assistant-security-and-portability-phase9-post-red-20260901`, `sha256:494db8d6a5c0be3bb5f66b12f50ce70fe2995626355ae0545fc6021778133dd3`; D2 remains conditional on fresh post-Red review and exact approval |
| 2026-09-01 | current / plan-aligned | stale / changes-required | Post-Red review accepted the absence Red but found shared database/time/authorization/telemetry state, request double-disposal, arbitrary activity barriers, and unsupported writer/resolver-call claims | Review `sha256:fa6d9ad2f13624e3e8b0af6a5abb2477fb7eea7d553c6b1ce672028561d00994` |
| 2026-09-01 | stale / changes-required | current / plan-aligned | D2-0 isolated every test, fixed single HTTP ownership, replaced arbitrary activity barriers with exact value-free structured log milestones, narrowed claims to observed evidence, and retained 10/10 attributable absence failures | D2-0 binding `setup-assistant-security-and-portability-phase9-d2-0-final-20260901`, `sha256:b159e98a4202efff1cb5b175343d6befa093035160d3d6c9a90ded29ffa66a43`; product D2 still requires fresh review and exact approval |
| 2026-09-01 | current / plan-aligned | stale / changes-required | D2-0 review accepted isolation/ownership/claim scope but found milestone matching omitted exact event ID/name and canonical ledgers omitted D2-1 through D2-11 plus all migration providers | Review `sha256:c6a4bc75f9a887802eb5fa518b055902980869f60e7786f9ecaafd6ea4741cc2` |
| 2026-09-01 | stale / changes-required | current / plan-aligned | D2-0b matches event ID/name/operation/milestone, narrows mismatch evidence to HTTP shape/value exclusion, records D2-1 through D2-11 in plan/tasks/context, names all five generated migration providers, and retains 10/10 attributable Red | D2-0b binding `setup-assistant-security-and-portability-phase9-d2-0b-final-20260901`, `sha256:6fac4deb374ad98f1d9baad71e129d150ca8a841311f5633191d52d3993b92ea`; product D2 still requires fresh review and explicit approval |
| 2026-09-01 | current / plan-aligned | stale / changes-required | D2-0b review accepted technical Red/staging but found tasks/context resume metadata still pointed at obsolete B1/Phase-8 work and omitted D2-1 as the next gated slice | Review `sha256:4e2b243aeed3872606652481858d233034bc3c8d59de801b4b93e7be8c13bb2c` |
| 2026-09-01 | stale / changes-required | current / plan-aligned | Status-only D2-0c makes tasks/context identify final review plus explicit `approve` as next, D2-1 Wire Red as the sole first product slice, later layers/providers/generators gated, and capability flags false | D2-0c binding `setup-assistant-security-and-portability-phase9-d2-0c-final-20260901`, `sha256:83bdf2889ed66f5c4aab6e407d025d15e6b8d6e53fe03ea25afee37c39f70c34`; product D2 still requires fresh review and explicit approval |
| 2026-09-01 | current / plan-aligned | stale / changes-required | D2-0c review accepted technical readiness but found executable-looking historical `IN PROGRESS`, `NEXT`, `Quick Resume`, `Current Handoff`, fresh-intake, and task-priority labels that contradicted the intended sole resume path | Review `sha256:e9f446c24b4ba9412e0ad7c133c3ad67855c2b17932819435e1ea293f9ef0282` |
| 2026-09-01 | stale / changes-required | current / plan-aligned | D2-0d marks all old session-progress, quick-resume, and handoff directions historical/superseded/non-executable and makes final review plus explicit `approve`, then D2-1 Wire Red only, the singular current path | D2-0d binding `setup-assistant-security-and-portability-phase9-d2-0d-final-20260901`, `sha256:9c935d1abb73002656bf4b2407c5700d43f316acf94e2dc98b7c3db1e1b886cb`; product D2 still requires fresh review and explicit approval |
| 2026-09-01 | current / plan-aligned | stale / changes-required | D2-0d review accepted technical/readiness evidence and the singular resume path but found the current successor-D ownership row still said D2-0c review was pending | Review `sha256:36c00e1ed7db2aa5acfd0f185545ae84f8487d5fdd03ec92b67417a98da59b88` |
| 2026-09-01 | stale / changes-required | current / plan-aligned | D2-0e makes the successor-D ownership row agree that final D2-0e review then explicit `approve` is current, no product owner/capability exists, and D2-1 Wire Red is the sole first product slice | D2-0e binding `setup-assistant-security-and-portability-phase9-d2-0e-final-20260901`, `sha256:15aed34796930152e3e2dee19ab042b84f978a4d37b743cf9d0bae52bc718c9c`; product D2 still requires fresh review and explicit approval |
| 2026-09-01 | current / plan-aligned | stale / changes-required | D2-0e review accepted the technical packet, ownership row, and singular resume path but found the authoritative context planning-status sentence still named D2-0c as current | Review `sha256:2d563b00938b614323a49c5d7cc2786e1f0651d2e05617198badca9ddeae5028` |
| 2026-09-01 | stale / changes-required | current / plan-aligned | D2-0f makes every authoritative planning, ownership, status, and resume surface agree that final D2-0f review then explicit `approve` is current and D2-1 Wire Red is the sole first product slice | D2-0f binding `setup-assistant-security-and-portability-phase9-d2-0f-final-20260901`, `sha256:9bdd703dd91801b436e602d9418b6d7d93a325cc8f35f037c6ed3620e077de73`; product D2 still requires fresh review and explicit approval |
| 2026-09-01 | current / plan-aligned | current / plan-aligned | Final D2-0f review approved the complete staged packet at 100/100 and the user explicitly approved it; D2-1 Wire Red became the sole authorized first product action | Review `sha256:fba631d7436e08ab397c72df4de04dacbcebfa150be258fd4bd03897bf5510e8`; approval `sha256:a5d01cb1d91a071c7885316edb3ec27f244d8f36e44687ebe6d4344dbeb6b97e` |
| 2026-09-01 | current / plan-aligned | current / plan-aligned | D2-1 observed attributable Wire/Core/Architecture Red, then added only package-free immutable transport vocabulary and passed focused/full Wire, Core, Architecture, product-build, LSP, package-graph, and whitespace gates | D2-1 binding `setup-assistant-security-and-portability-phase9-d2-1-green-20260901`, `sha256:cb2292e484bf137c5b6f4d963733fa282cc54100dc50ec06f226ca2b7261c3e9`; D2-2 requires fresh focused review |
| 2026-09-01 | current / plan-aligned | stale / changes-required | Initial D2-1 review reproduced clear capability default serialization plus null/invalid challenge, null/duplicate/numeric scope, numeric enum, and incomplete shipped-context test failures | Review `sha256:61976036324b4f436ec3fd6271c18ae88de4ae2b14dbb1e5da6129f1873014ba` |
| 2026-09-01 | stale / changes-required | current / plan-aligned | Corrected D2-1 removes public capability data, adds canonical typed challenge and strict scope/enum converters, exercises the shipped context/default serializer, closes metadata aliases, and passes 8/8 focused plus 35/35 full Wire and architecture gates | Corrected binding `setup-assistant-security-and-portability-phase9-d2-1-corrected-green-20260901`, `sha256:1fa56ed5cf964151826cfd68a712657cb963ff29f5c1f4d10291ccaaccd6c2f7`; D2-2 requires fresh focused review |
| 2026-09-01 | current / plan-aligned | current / plan-aligned | Corrected D2-1 review closed every serializer/capability/metadata finding at 100/100 and authorized D2-2 Domain Red only | Review `sha256:cd9b41d98111914e4aba5a392ebe0fe9d981c52f4d2d376a75eae74ca68ca72c` |
| 2026-09-01 | current / plan-aligned | current / plan-aligned | D2-2 added one reflection-driven public-behavior Domain test file; seven tests compile and fail only for absent enrollment, issuance-claim, and secret-operation owners, with no Domain product path | D2-2 Red binding `setup-assistant-security-and-portability-phase9-d2-2-red-20260901`, `sha256:08cd9144a496522f27da007dd611177eae8a87798f662ea30302d53b6a03556a`; Green requires focused Red review |
| 2026-09-01 | current / plan-aligned | current / changes-required | Initial D2-2 review found incomplete cross-bound dispatch, replay/commitment, mutation atomicity, temporal/overflow, public-surface, audit, and concurrency contracts | Review `sha256:9176183c69a1b595182acbfdb58a603d4a478be6b3622dfaa06476223a010c05`; no Green authority |
| 2026-09-01 | current / changes-required | current / plan-aligned | Corrected D2-2 expands to 14 exact-surface and lifecycle tests, removes Domain `Revision`, assigns `ConcurrencyStamp` exclusively to Persistence, and produces exact 5/3/6 owner-absence failures | Corrected binding `setup-assistant-security-and-portability-phase9-d2-2-corrected-red-20260901`, `sha256:1bb0b910eee24795f8bed638d2f39613107b87d02647a7235dce855185cdce0f`; Green requires fresh focused review |
| 2026-09-01 | current / plan-aligned | current / changes-required | Second D2-2 review found incomplete accepted terminal results, actor/user audit conflation, and value-bearing exception-diagnostic escape | Review `sha256:225120ba60178747284f57d2d837736bf381df8b4f77c7590920a0475b4fc114`; no Green authority |
| 2026-09-01 | current / changes-required | current / plan-aligned | Final D2-2 requires exact named terminal snapshots, leaves user audit attribution to later authenticated ownership, and excludes runtime evidence canaries from exception chains while preserving exact 5/3/6 Red attribution | Final binding `setup-assistant-security-and-portability-phase9-d2-2-final-red-20260901`, `sha256:90fc0b3be5ff307ffcc6466abb4b25d53711db1d344e3bd660f3812d4d6d34e7`; Green requires fresh focused review |
| 2026-09-01 | current / plan-aligned | current / plan-aligned | Final D2-2 Red review approved every closed lifecycle, audit, concurrency, and diagnostic matrix at 100/100 and authorized Domain Green only | Review `sha256:7723a3bd5663ba19a65f5c03f38b1a3c9df336692baaff7ca67ab04ca0725bd4` |
| 2026-09-01 | current / plan-aligned | current / plan-aligned | D2-2 implements three exact Domain owners, turns 14/14 focused and 1089/1089 full Domain Green, and corrects Green-executable reflection false positives without weakening raw-material closure | Green binding `setup-assistant-security-and-portability-phase9-d2-2-green-20260901`, `sha256:a34e6535fc6d8cbd599c618f26b8c7da002c37096ca19ba9661bcd4301122a02`; D2-3 requires fresh Green review |
| 2026-09-01 | current / plan-aligned | current / plan-aligned | D2-2 Domain Green review approved at 100/100 and authorized D2-3 Application Red only | Review `sha256:bf46d5c7fe2e3a8c56d8e0ffb0dc72375e38b0bb2b4eb38d1508adcf97479db2` |
| 2026-09-01 | current / plan-aligned | current / plan-aligned | D2-3 added one reflection-driven static Application contract test file; seven tests compile and fail only for exact absent writer, commitment, coordinator, barrier, and vocabulary owners | D2-3 Red binding `setup-assistant-security-and-portability-phase9-d2-3-red-20260901`, `sha256:bea912c635f0238a5bee2abc20f7b37f2123780d20fb816728580056b407e3a6`; Green requires focused review |
| 2026-09-01 | current / plan-aligned | current / changes-required | Initial D2-3 review found contradictory semantic exclusions, unexecuted constructor/lifetime/metadata boundaries, and overstated race/call-count ownership | Review `sha256:eb4fdef0afae5633fdf0451327895df516f2f894f3f0c0473171543a641ee9b9`; no Green authority |
| 2026-09-01 | current / changes-required | current / plan-aligned | Corrected D2-3 closes UUIDv7/version/binding/byte/commitment constructors, borrowed lifetime, value-free diagnostics, exact metadata, coordination identity, and assigns executable race/call-count proof to D2-7 | Corrected binding `setup-assistant-security-and-portability-phase9-d2-3-corrected-red-20260901`, `sha256:0dbc26bc5628e6e174e431e3a3e912239fa661f60666e178797bdbbf59079db0`; Green requires fresh review |
| 2026-09-01 | current / plan-aligned | current / changes-required | Second D2-3 review found inherited public-surface escape hatches, borrowed-memory copying remained possible, and UUID/diagnostic negatives were narrower than claimed | Review `sha256:8fd588fa609f9884eabc1feff9197ad3e55c5476234dfcc9056bf2d792406550`; no Green authority |
| 2026-09-01 | current / changes-required | current / plan-aligned | Final D2-3 forbids inherited contract surfaces, requires direct object bases, proves exact borrowed segment aliasing, rejects UUIDv1/v4/v6/v8, and denies transformed byte diagnostics | Final binding `setup-assistant-security-and-portability-phase9-d2-3-final-red-20260901`, `sha256:732ad07fd18177326b3afc9a78833201b96dba383d67373919420affc89f0286`; Green requires fresh review |
| 2026-09-01 | current / plan-aligned | current / changes-required | Third D2-3 review found special-name interface methods plus request/result public fields/events/nested members could bypass complete-surface claims | Review `sha256:b2175ea8442cbc0af882bfacc1948f75aa5055e5052528bb3d9d8a0c9d326d99`; no Green authority |
| 2026-09-01 | current / changes-required | current / plan-aligned | Complete D2-3 closes every interface/request/result public member kind, metadata events/nested types, and semantic public field types | Complete binding `setup-assistant-security-and-portability-phase9-d2-3-complete-red-20260901`, `sha256:d7cbbcb14ecf15abdbd649cbb9ccce7b52672d97d972c534498225d81b27e39d`; Green requires fresh review |
| 2026-09-01 | current / plan-aligned | current / changes-required | Fourth D2-3 review found static/default/generic port, indexer/explicit-interface, enum-value, attribute, ownership, and forwarding bypasses | Review `sha256:d5eadd2c34368a30c8343b16209803cb208c650b45408bebfb3589185e3de3d4`; no Green authority |
| 2026-09-01 | current / changes-required | current / plan-aligned | Exhaustive D2-3 freezes CLR method/property/backing-field/enum semantics, compiler-only attributes, Application ownership, and absence of forwarding | Exhaustive binding `setup-assistant-security-and-portability-phase9-d2-3-exhaustive-red-20260901`, `sha256:fc1e95aef0afd6c759e2bf277d0359e8b39530445d67910d510eb6d401150c1d`; Green requires fresh review |
| 2026-09-01 | current / plan-aligned | current / changes-required | Fifth D2-3 review found private interface/metadata/enum behavior, incomplete implementation flags, permissive compiler provenance, and partial assembly/module prefix inventory | Review `sha256:fea22d226d869581063ea8afb18ce3669c89e7c8fe93ee4c4de0953544b46cd4`; no Green authority |
| 2026-09-01 | current / changes-required | current / plan-aligned | Closed D2-3 uses same-SDK exact metadata witnesses, all-visibility owner closure, exact assembly/module security payloads, and full exported/forwarded prefixes | Closed binding `setup-assistant-security-and-portability-phase9-d2-3-closed-red-20260901`, `sha256:0c7cc11e2b895f474290c2939a97cb3f34f2ca498fa8df3ddfc5d654b0cfadce`; Green requires fresh review |
| 2026-09-01 | current / plan-aligned | current / changes-required | Sixth D2-3 review found residual Property/Param/Field table flags, constructor return metadata, enum storage metadata, and textual custom-attribute identity | Review `sha256:042f62d5bcd4ac92749e314640304202e6527a94221fdd390bb4dfb9723f6e80`; no Green authority |
| 2026-09-01 | current / changes-required | current / plan-aligned | Approval-ready D2-3 compares complete witness member metadata, semantic constructor return MethodDef state, and structural custom-attribute identities/payloads | Approval-ready binding `setup-assistant-security-and-portability-phase9-d2-3-approved-ready-red-20260901`, `sha256:3883929342439280a627915abbd7adbadeb819376cc8e9c5b6262e51c4d3b5de`; Green requires fresh review |
| 2026-09-01 | current / plan-aligned | current / changes-required | Seventh D2-3 review found assembly/module identities remained full-name based and only the manifest module was inventoried | Review `sha256:eb968040e608dce3408aeb379829894a2f0e377531f64f47ef16532fec4efb92`; no Green authority |
| 2026-09-01 | current / changes-required | current / plan-aligned | Final approvable D2-3 binds exact BCL manifest attributes/payloads, exactly one named/scoped manifest module, structural module metadata, and every owner to that module | Final approvable binding `setup-assistant-security-and-portability-phase9-d2-3-final-approvable-red-20260901`, `sha256:edb31317a5c48168e35e97bdd60e61df1e1625f463efa93cd76101768baa9eb8`; Green requires fresh review |
| 2026-09-01 | current / plan-aligned | current / plan-aligned | Project Steward selected Terminal.Gui as the sole human terminal target, rejected every console fallback, and authorized only an exact minimal downstream package that removes the provenance-blocked grammar/editor integration | Binding `setup-assistant-terminal-gui-only-rebaseline-20260901`; plan `sha256:752972e2b5e85e2bbb792a92618fabb6cffccdb6bccb278d222c4ed8ee6681d9`; exact patched artifact remains inactive until Phase 5R evidence and fresh review pass |

Refresh this report when:

- a framework/package/version is selected, blocked, replaced, conditionally
  reconsidered, or changes license/provenance/notice/publish obligations;
- the environment catalogue changes secret/default/relevance semantics;
- web origin, CSP, service worker, telemetry, crash, update, or hosting behavior
  changes;
- desktop secret persistence or live provider integration enters scope;
- legal kinds, templates, Markdown, publication, acceptance, or operator-role
  composition changes;
- CLI commands, JSON schema, Terminal.Gui/TTY behavior, skill instructions,
  agent approval, or embedded-AI boundary changes;
- a package-free presentation shell is activated, shipped, or used as support
  evidence;
- human-presentation state, message payloads/lifetimes, validation/readiness
  ownership, machine-CLI isolation, or target-adapter parity changes;
- a dependency/license choice or approval-inheritance assumption changes an
  executable's outbound path or authority;
- packaging, signing, official identity, source availability, or legal copy
  changes;
- Phase 8 split, fourteen matrices, exact ceilings, public seam, deterministic
  barriers, Worst Break, owned paths, verification/commit governance, source
  grammar, parser identity/version/graph/role, platform claims, directory safety,
  canonical bytes/diagnostics, source-path or sensitivity exclusion, defaults,
  measurement profiles, target-server compatibility, safe CLI environment, or
  review verdict changes; or
- implementation evidence, stakeholder feedback, incidents, approval revision,
  or audits change any IVSD-F001 through IVSD-F046 conclusion.
