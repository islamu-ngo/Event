<!-- ABOUTME: I-VSD product-strategy review for extracting the Setup Assistant into a universal self-hosting configuration product. -->
<!-- ABOUTME: Recommends a schema-first offline core, progressive disclosure, and an optional governed definition store. -->

# I-VSD Product Strategy Review: Universal Self-Hosting Configuration Assistant

Last Updated: 2026-09-04

## Review Metadata

- Mode: standalone
- Subject: universal self-hosting configuration assistant
- Workstream: none; proposed future workstream `universal-self-hosting-configuration-assistant`
- Report kind: product-strategy-review
- Report status: current
- Disposition: ready-for-planning
- Evidence cutoff: 2026-09-04
- Reviewed input revision: `sha256:078a5af99159f12b5aa4d26f330020d2eef8765b86cb6aea7f6fffba91758b1f`
- Supersedes: report revision
  `sha256:966b6fcd3368434b11187fb23ddc9e5d6cda310a0014c53a6c30be2dab391e1c`

## Executive Recommendation

Extract a standalone product, but extract only the reusable configuration
authoring boundary. Do not move the complete ISLAMU Event Setup Assistant
umbrella into a generic repository.

The strongest product sequence is:

1. define a versioned, non-executable `SetupDefinition` format;
2. make local file import and a headless conformance CLI work first;
3. render the same definition through Terminal.Gui and Avalonia adapters;
4. prove the model with ISLAMU Event and Keycloak realm-import JSON;
5. add a curated store as an optional discovery and trust layer.

The store and direct-import ideas are complementary, but the schema must be
the primary contract. A store-first product recreates hardcoded maintenance at
larger scale. A schema-only product leaves discovery, authenticity, freshness,
and abandonment unresolved. The recommended hybrid keeps definitions portable
and usable without the store while allowing the store to add curation,
signatures, compatibility evidence, revocation, and update notices.

The first release should generate `.env` and bounded declarative JSON output.
Keycloak realm-import JSON is the named external application format planned for
the first cross-product conformance and support target. The ISLAMU
`ConfigurationManifest` codec, live target control, legal lifecycle,
application-data migration, and payment operations remain product-specific
adapters or services. They must not become universal schema powers.

## Scope

This report evaluates the proposed extraction of the existing ISLAMU Event
ConfigurationManifest and Setup Assistant foundations into an independent
cross-platform product for self-hosters, operators, and self-hosted software
maintainers.

It covers:

- a shared headless configuration engine;
- a versioned definition format supplied by software maintainers or users;
- local definition import without a software release;
- an optional curated definition store;
- `.env` and bounded structured-document generation;
- planned, version-bound support for non-secret Keycloak realm-import JSON;
- guided, advanced, and all-declared progressive disclosure;
- Terminal.Gui, Avalonia desktop, and Avalonia browser presentation targets;
- definition authenticity, versioning, review, revocation, and abandonment;
- secret handling, file output, offline use, accessibility, localization, and
  support;
- sustainable governance and non-exploitative monetization.

The review uses the complete active Setup Assistant plan directory as supplied
by the user, the current ConfigurationManifest and Setup Assistant I-VSD
reports, and the relevant current Core, CLI, presentation, and Terminal.Gui
implementation seams.

### Current implementation boundary

The current repository contains a valuable extraction seed, not yet the full
universal product:

- `Event.Setup.Core` has a closed environment catalogue, declarative
  activation graph, sensitivity and requiredness metadata, deterministic
  relevant-only dotenv composition, readiness, and value-safe diagnostics.
- `Event.SetupAssistant.Cli` has a deterministic, versioned, non-secret
  machine contract for catalogue, manifest, tenant package, environment,
  legal, and doctor operations.
- `Event.SetupAssistant` has framework-neutral operation/session state and
  value-free messaging.
- `Event.SetupAssistant.Terminal` is implemented and security-focused, but its
  current human experience is a narrow standalone `SETUP_SECRET` workflow. It
  does not yet render the full catalogue as a dynamic progressive-disclosure
  form.
- The current environment catalogue is compiled ISLAMU-specific metadata. It
  has no untrusted external definition loader or publisher trust model.
- Avalonia browser and desktop remain `ApprovedDisabled`; their existence in
  the plan is not implementation or accessibility evidence.
- The active plan's live control plane, application-data migration, and
  sovereign payment phases are ISLAMU-specific authority-bearing systems, not
  reusable configuration-form concerns.
- Keycloak is already an ISLAMU deployment integration, but the universal
  engine does not yet load a Keycloak definition, generate a realm-import JSON
  artifact, or prove acceptance against an exact Keycloak release.

## Claim Boundary

This is I-VSD provider-responsibility design reasoning. It is not:

- a fatwa, halal/haram ruling, or Sharia certification;
- security, privacy, accessibility, legal, supply-chain, or store-content
  certification;
- proof that the proposed market exists or that maintainers will adopt the
  definition format;
- proof that generated configuration is correct for an application version;
- proof that an imported or curated definition is safe merely because it is
  signed;
- authorization to read, upload, retain, or redistribute operator secrets;
- authorization to copy third-party documentation, logos, schemas, code, or
  expressive configuration guidance into a definition store.

The current evidence supports design validation and implementation
traceability for the ISLAMU-specific seed. Stakeholder and operational
validation are still absent.

## Recommended Product Boundary

| Boundary | Universal product owns | Application integration owns |
|---|---|---|
| Definition | Closed `SetupDefinition` syntax, limits, identities, metadata, validation, and disclosure rules | Application-specific field inventory, help text, compatibility range, and output mappings |
| Engine | Parse, validate, evaluate conditions, calculate readiness, diff, and render built-in output kinds | Runtime validation, startup behavior, migrations, provider calls, and business authority |
| Presentation | Framework-neutral form state plus Terminal.Gui and Avalonia adapters | Product-specific branding and optional application adapters |
| Secrets | Sensitivity classification, target-owned entry intent, local generation policy, and value-free readiness | Deployment secret authority, provider coordinates, rotation, and runtime retrieval |
| Store | Discovery index, immutable definition digest, publisher identity, review metadata, revocation, and cache | Publisher statements and application-version support claims |
| Live actions | None in the portable core | Authenticated application API, HAL/capability authority, tenant checks, idempotency, and receipts |

The reusable center should stay network-free and filesystem-free. Definition
loading, store access, file selection, protected writing, browser download, and
live application APIs are outer adapters.

### Proposed definition model

`SetupDefinition` is the portable, non-secret description consumed by the
assistant. JSON Schema is the meta-schema used to validate that description.
The two must not be confused.

A v1 definition needs only these bounded concepts:

- immutable definition identity, revision, digest, publisher, source, and
  supported application-version range;
- topologies, capabilities, and providers selected by the operator;
- fields with stable IDs, output keys or paths, category, sensitivity,
  requiredness, safe default, restart impact, and help references;
- a closed activation expression using `all`, `any`, `not`, and declared
  identifiers;
- disclosure level: `guided`, `advanced`, or `all-declared`;
- closed validator IDs and bounded enum/range/pattern parameters;
- built-in output mappings for dotenv and bounded JSON documents;
- localizable labels, descriptions, consequences, examples, and documentation
  links;
- review metadata that is separate from the publisher-authored definition.

V1 should not support scripts, commands, arbitrary template engines, dynamic
code, downloaded plugins, remote schema resolution, environment reads, file
reads, secret-provider reads, or network callbacks. These are execution
capabilities, not configuration metadata.

### Keycloak external proof target

Keycloak realm-import JSON is the first planned external application format
used to test and support the generic definition model. This target should prove
that the engine handles bounded nested objects, arrays, references, stable
identifiers, deterministic ordering, structured diff, and version-specific
compatibility without adding `if Keycloak` behavior to the universal core.

Initial support must be an explicit allowlisted, non-secret realm bootstrap
subset. The definition and generated artifact must reject or omit user
credentials, client secrets, identity-provider credentials, private key
material, mail credentials, sessions, exported personal data, and unreviewed
administrative or operational state. The product must never infer that a
publisher signature makes those categories safe to transport.

Generation is not application. The universal core may produce, validate, and
diff the JSON file, but it must not connect to Keycloak, authenticate as an
administrator, or import the realm. Any future direct-import adapter requires
a separate authority and threat review, explicit target/version selection,
preview, human approval, bounded credentials, idempotency, and recovery.

Support claims must bind to exact tested Keycloak versions or image digests.
“Valid SetupDefinition,” “valid JSON,” and “accepted safely by Keycloak” are
three different evidence states and must be presented separately.

### Progressive disclosure contract

The profiles should be semantic, not merely three different filters:

| View | Required behavior |
|---|---|
| Guided (recommended) | Shows every relevant required field, every choice needed to determine relevance, unresolved blockers, and a small set of high-value recommended options |
| Advanced | Shows every relevant declared field, grouped by concern, with defaults, restart effects, and consequences |
| All declared | Shows all declared fields, including inactive fields with the exact reason they are inactive; it does not silently emit them |

No profile may hide a required field, unresolved dependency, destructive
effect, security consequence, or readiness blocker. Switching profiles must
not discard entered values without explicit review. Search and a persistent
“why is this hidden?” explanation are part of the disclosure model.

## Approach Comparison

| Approach | Strength | Primary risk | I-VSD disposition |
|---|---|---|---|
| Hardcoded official catalogue only | Strong curation and polished experiences | Every application/revision requires product-maintainer intervention; creates dependency and eventual lock-in | Reject as primary architecture |
| Importable schema only | Decentralized, offline, fast adoption | Weak discovery, authenticity, freshness, revocation, and maintenance signals | Accept as foundational capability, insufficient alone |
| Schema plus optional curated store | Portable local contract plus discoverability and evidence | Requires explicit governance and truthful trust labels | Recommend |

The initial “store” should be a small signed index of immutable definition
artifacts, not a new database-backed marketplace service. Add a service only
when measured submission, moderation, search, or update volume proves a Git-
style index insufficient.

## Findings

### Finding Register

| ID | Lifecycle | Severity | Claim type | Principle and domain | Provider-controlled decision and risk | Evidence / validation level | Mitigation | Owner or next validation |
|---|---|---|---|---|---|---|---|---|
| IVSD-F001 | open | Critical | Product-boundary finding | Amanah, Ihsan; Strategic/Technical | Extracting the whole active Setup umbrella would mix reusable authoring with ISLAMU-specific legal, tenant, live-control, migration, and money authority | E001-E007; design validation and implementation traceability | IVSD-M001 | Product architecture; validate with one external application |
| IVSD-F002 | open | Blocker | Extensibility requirement | Promise-Keeping, Autonomy; Technical | If application metadata remains compiled into product code, the proposed ecosystem still requires software releases for every definition change | E001-E003, E008; implementation traceability | IVSD-M002 | Schema owner; conformance prototype |
| IVSD-F003 | open | Critical | Distribution finding | Autonomy, Justice; Strategic/Governance | A mandatory official store would make one provider the availability, approval, and discoverability gate for all self-hosted software | E001-E003; design validation | IVSD-M003 | Product and governance |
| IVSD-F004 | open | Blocker | Trust-boundary requirement | Amanah, Non-Harm; Technical/Governance | Imported definitions are untrusted supply-chain inputs and may attempt resource abuse, misleading defaults, unsafe destinations, or secret capture | E001-E009; design validation | IVSD-M004 | Security; adversarial parser review |
| IVSD-F005 | open | Critical | Update-integrity requirement | Truthfulness, Promise-Keeping; Operational/Technical | Mutable definitions or silent updates can change generated configuration without the operator understanding publisher, version, or behavioral differences | E001-E007; design validation | IVSD-M005 | Release/store governance |
| IVSD-F006 | open | Blocker | Execution-boundary requirement | Non-Harm, Avoiding Spying; Technical | Scripts, template code, remote references, or plugins inside definitions would turn a form schema into an arbitrary execution and exfiltration surface | E001-E009; design validation | IVSD-M006 | Security and architecture |
| IVSD-F007 | open | Critical | UX safety requirement | Justice, Truthfulness; Design | Progressive disclosure can become deceptive if “basic” hides required, risky, costly, or readiness-blocking configuration | E001-E003, E008-E010; implementation traceability and design validation | IVSD-M007 | UX/accessibility; usability testing |
| IVSD-F008 | open | Critical | Default-safety requirement | Amanah, Avoiding Deception; Design/Technical | A publisher can label an unsafe, privacy-invasive, externally dependent, or expensive value as the recommended default | E001-E003, E008; design validation | IVSD-M008 | Definition review governance |
| IVSD-F009 | open | Blocker | Secret boundary | Amanah, Avoiding Spying; Technical/Operational | A portable definition, store, browser session, support bundle, or machine output may become a secret collection channel | E001-E006, E008-E011; implementation traceability | IVSD-M009 | Security and target owners |
| IVSD-F010 | open | Blocker | Output-safety requirement | Non-Harm, Amanah; Technical | Cross-platform writes can overwrite files, follow links, inherit unsafe permissions, or leave plaintext backups and partial output | E001-E003, E009-E011; implementation traceability | IVSD-M010 | Desktop/TUI adapter owners |
| IVSD-F011 | open | Critical | Correctness and freshness concern | Truthfulness, Promise-Keeping; Technical/Operational | A definition may be structurally valid yet stale or semantically wrong for the selected software image or release | E001-E008; design validation | IVSD-M011 | Publisher plus conformance owner |
| IVSD-F012 | open | High | Product-truth finding | Truthfulness; Design/Strategic | The current Terminal.Gui target is a protected `SETUP_SECRET` workflow, not yet the broad dynamic form experience described in the product vision | E001-E003, E011; implementation traceability | IVSD-M012 | Terminal presentation owner |
| IVSD-F013 | open | High | Capability-claim finding | Truthfulness, Promise-Keeping; Strategic/Operational | Avalonia browser and desktop are planned but currently disabled; marketing the seed as cross-platform GUI software would overstate implementation and evidence | E001-E003; implementation traceability | IVSD-M013 | Product/release owner |
| IVSD-F014 | open | Critical | Accessibility requirement | Justice, Ihsan; Design/Evaluation | A shared definition does not guarantee usable keyboard, screen-reader, small-terminal, RTL, localization, or error behavior across adapters | E001-E003, E006; design validation | IVSD-M014 | Accessibility owner; target audits |
| IVSD-F015 | open | Critical | Store-governance requirement | Amanah, Justice; Governance/Operational | Curated definitions can become abandoned, compromised, disputed, malicious, or incompatible, while a curated badge may falsely imply complete safety | E001-E007; design validation | IVSD-M015 | Store governance and incident response |
| IVSD-F016 | open | High | Business-model concern | Justice, Avoiding Deception; Strategic/Design | Paid ranking, promoted defaults, surveillance analytics, or paywalled export would bias operator choices and undermine the sovereignty promise | E006-E007; design validation | IVSD-M016 | Project Steward/business owner |
| IVSD-F017 | open | Critical | Continuity requirement | Autonomy, Promise-Keeping; Operational/Technical | A network-required store, uncached definition, or unsupported shutdown path can block an operator from configuring software they already possess | E001-E007; design validation | IVSD-M017 | Product operations |
| IVSD-F018 | open | Blocker | Authority-separation requirement | Amanah, Justice; Technical/Governance | Generalizing live target, tenant, legal publication, application-data migration, or payment authority into the portable schema would let metadata claim powers that belong to each application | E001-E007; implementation traceability | IVSD-M018 | Architecture and application owners |
| IVSD-F019 | open | High | Attribution and ecosystem concern | Truthfulness, Rights of People; Governance/Strategic | Community definitions may misuse names, logos, licenses, documentation, or “official” language and obscure who maintains or reviewed them | E004-E007; design validation | IVSD-M019 | Legal/IP and store governance |
| IVSD-F020 | open | High | Evaluation concern | Amanah, Avoiding Spying; Evaluation/Operational | Without outcome evidence, the provider may optimize catalogue size or completion clicks while missing broken outputs, support burden, exclusion, and abandoned definitions | E001-E010; design validation | IVSD-M020 | Product operations; opt-in research |
| IVSD-F021 | open | Blocker | External bootstrap authority boundary | Amanah, Non-Harm, Justice; Technical/Governance | Keycloak realm-import JSON can define security and administrative state; treating arbitrary realm exports as ordinary portable configuration could transfer credentials, identities, excessive privileges, or unsafe defaults | E001-E012; design validation; exact Keycloak behavior not yet externally validated | IVSD-M021 | Keycloak integration owner, security review, and exact-version conformance |

## Recommendations

### Mitigation Register

| ID | Linked finding | Recommendation | Acceptance evidence |
|---|---|---|---|
| IVSD-M001 | IVSD-F001 | Extract only the definition contract, headless evaluation/rendering engine, framework-neutral presentation state, CLI, TUI, and GUI adapters. Keep ISLAMU live control, tenant authority, legal lifecycle, migrations, and payments outside the universal core. | Dependency graph and public API review show no application-specific authority in the reusable core |
| IVSD-M002 | IVSD-F002 | Define a canonical `SetupDefinition` JSON format and generated JSON Schema with closed objects, exact bounds, stable IDs, declarative activation, sensitivity, requiredness, validators, disclosure level, documentation, and output mappings. | ISLAMU Event and a Keycloak realm-import definition pass the same conformance engine without a Keycloak branch in the universal core |
| IVSD-M003 | IVSD-F003 | Make local file import a permanent first-class path. The store supplies discovery and evidence only; it is never required to open, validate, edit, or render an already acquired definition. | Offline test opens a pinned definition and produces identical output with no store/network access |
| IVSD-M004 | IVSD-F004 | Treat every imported definition as untrusted. Parse with exact byte/depth/count limits, reject unknown members and cycles, preview permissions and outputs, and assign explicit trust states: local-unverified, publisher-signed, and curated-reviewed. | Adversarial corpus rejects bombs, collisions, hidden fields, unsafe destinations, and malformed signatures without partial state |
| IVSD-M005 | IVSD-F005 | Bind every definition to identity, revision, digest, publisher, source, application compatibility, and schema version. Pin by default; show semantic diffs and require approval before changing revision. | Update tests prove no silent mutation and preserve the previous usable revision for rollback |
| IVSD-M006 | IVSD-F006 | Keep v1 data-only: no scripts, commands, remote references, arbitrary templates, dynamic code, plugins, or environment/file/network reads. Add a new capability only through a separately reviewed host adapter, never through store content. | Schema rejects all executable or remote capability forms |
| IVSD-M007 | IVSD-F007 | Implement `guided`, `advanced`, and `all-declared` views with invariants: guided always includes relevance decisions, required fields, risks, and blockers; advanced includes every relevant field; all-declared explains inactive fields without emitting them. | Cross-adapter tests and stakeholder usability sessions show no hidden blocker or discarded value |
| IVSD-M008 | IVSD-F008 | Distinguish canonical defaults, publisher recommendations, and user choices. Show the source and consequence of a recommendation; forbid secret defaults and require review for telemetry, public exposure, paid services, or weaker security. | Definition lint and curation review flag high-impact recommendations |
| IVSD-M009 | IVSD-F009 | Definitions, store records, profiles, logs, diagnostics, and machine output remain non-secret. Secret entry is target-owned, explicit, local, short-lived, never uploaded, and omitted from portable state. Browser secret mode remains disabled by default. | Zero-secret scans plus browser/TUI/desktop trust-boundary tests |
| IVSD-M010 | IVSD-F010 | Keep rendering separate from writing. Desktop and terminal use platform-specific protected writers, create-new by default, explicit redacted overwrite review, atomic installation, and no backup. Browser defaults to non-secret download. | Real filesystem race/link/permission tests per supported platform |
| IVSD-M011 | IVSD-F011 | Publish a conformance kit that checks definition structure, semantic graph closure, required-profile completeness, deterministic output, declared application version/image digest, and golden startup validation supplied by the application maintainer. | CI evidence for each listed application/version; stale definitions are visibly yanked or unsupported |
| IVSD-M012 | IVSD-F012 | Expand Terminal.Gui from the current one-secret window into a definition-driven workspace only after the external schema engine exists. Reuse current secret-buffer and protected-write boundaries rather than adding another terminal path. | One definition produces equivalent readiness and bytes in CLI and TUI; keyboard/small-terminal tests pass |
| IVSD-M013 | IVSD-F013 | Describe the current seed as headless plus Terminal.Gui. Add Avalonia desktop/browser claims only after exact dependencies, builds, accessibility, file/download, and secret-mode evidence become Active. | Target capability manifest and release evidence match shipped artifacts |
| IVSD-M014 | IVSD-F014 | Put semantic labels, descriptions, error associations, logical ordering, locale keys, and RTL-safe grouping in the definition/presentation model while keeping target accessibility claims separate. Always retain an evidenced CLI alternative. | Keyboard, screen-reader, 200% scale, high-contrast, RTL, localization, and small-terminal audits per Active target |
| IVSD-M015 | IVSD-F015 | Govern the store through immutable submissions, maintainer/publisher identity, reproducible validation results, review scope, last-verified date, vulnerability reporting, revocation/yank, dispute, correction, abandonment, and fork succession procedures. A badge states evidence reviewed, not “safe.” | Public governance policy, signed index history, incident drill, and correction SLA |
| IVSD-M016 | IVSD-F016 | Keep search/ranking neutral and inspectable. Prohibit pay-to-rank, promoted recommended values, surveillance ads, and export paywalls. Prefer transparent paid support, managed hosting, sponsorship, or enterprise policy support. | Public ranking and funding policy; no hidden commercial weighting |
| IVSD-M017 | IVSD-F017 | Cache verified definition artifacts by digest, support local export/import, publish EOL and mirror instructions, and keep the engine usable after store shutdown. | Offline continuity and restore drill from a local cache/export |
| IVSD-M018 | IVSD-F018 | Represent live application actions only through separately installed, application-owned adapters with authenticated APIs and explicit capabilities. Portable definitions may describe fields and outputs but never tenant, migration, publication, provider, or payment authority. | Architecture tests reject those authorities from the core schema and engine |
| IVSD-M019 | IVSD-F019 | Use `publisher`, `maintainer`, `source`, `license`, `trademark status`, and `reviewedBy` as separate facts. Reserve “official” for an application publisher's verified namespace; call project-reviewed entries “curated.” | Namespace and attribution policy plus legal/IP review of submitted assets and prose |
| IVSD-M020 | IVSD-F020 | Evaluate deterministic generation success, startup-validation success, stale/yanked definitions, correction time, support burden, accessibility defects, and portability outcomes. Collect no default remote telemetry; use opt-in studies and public issue evidence. | Metric register with owner, source, cadence, threshold, stakeholder impact, and limitations |
| IVSD-M021 | IVSD-F021 | Make Keycloak realm-import JSON the first named external support profile. Bind each profile to exact Keycloak versions/image digests, allowlist the supported non-secret realm subset, fail on excluded identity/credential/authority categories, render deterministic JSON, show a semantic diff, and require operator-controlled import outside the universal core. | Golden JSON plus disposable exact-version Keycloak acceptance tests; rejection tests for secret, identity, privilege, unknown-member, size, and version-drift cases |

### Delivery sequence

1. **Definition spike:** Freeze the smallest `SetupDefinition` v1alpha1 and
   adapt the current ISLAMU environment catalogue into it without changing
   current output bytes.
2. **External proof:** Hand-author the first Keycloak realm-import definition
   for a bounded non-secret realm bootstrap subset. Generate deterministic JSON
   and validate it against an exact disposable Keycloak release. If the core
   needs a Keycloak-specific branch, repair the definition/output model before
   extraction.
3. **Headless release:** Ship parser, validator, linter, deterministic `.env`
   renderer, bounded JSON renderer, diff, readiness, and machine CLI.
4. **Human surfaces:** Make Terminal.Gui definition-driven, then activate
   Avalonia targets independently when their evidence clears.
5. **Curated index:** Add immutable signed definitions, review metadata,
   search, update diff, revocation, and offline cache. Start with a repository
   index, not a marketplace backend.

### Rejected alternatives

- **Move the entire Setup Assistant workstream to a generic repository:**
  rejected because live authority, legal publication, application migration,
  and payments are not universal configuration concerns.
- **Build the official store before the import format:** rejected because it
  preserves central release coupling and makes the store the product's real
  schema.
- **Allow arbitrary scripts or plugin code in definitions:** rejected because
  it changes a bounded form engine into a package manager and remote-code
  execution platform.
- **Call curated definitions verified or safe:** rejected because signatures
  and review prove only specific evidence, not application correctness or
  absence of harm.
- **Show every variable by default:** rejected because volume is not
  transparency; relevant consequences and blockers matter more than an
  undifferentiated wall of fields.
- **Enable hosted browser secret entry by default:** rejected because the
  serving origin controls delivered code and no UI claim removes that trust
  boundary.
- **Import arbitrary Keycloak realm exports:** rejected because a broad export
  can mix portable realm structure with credentials, identities, privileges,
  and operational state outside the universal product's authority.

## Stakeholders

| Stakeholder | Interest or exposure | Provider duty |
|---|---|---|
| New self-hoster | Needs a working deployment without understanding every environment variable | Guided completeness, plain consequences, safe defaults, recovery |
| Experienced operator | Needs full control, diffability, automation, and no hidden behavior | All-declared visibility, deterministic output, CLI, version pinning |
| Application maintainer | Needs definitions to track real releases without support ambiguity | Conformance tooling, compatibility metadata, correction path |
| Keycloak realm operator | Needs repeatable realm bootstrap without accidental credential or privilege transfer | Exact-version scope, non-secret subset, semantic review, operator-controlled import |
| Community definition author | Needs a fair submission and attribution process | Clear namespace, review scope, dispute and succession rules |
| Disabled, localized, and RTL users | May be excluded by visually polished but inaccessible forms | Equivalent task access and target-specific evidence |
| Security and operations teams | Bear incident, secret, supply-chain, and recovery costs | Data-only schema, bounded parsing, protected output, incident process |
| Store reviewers and maintainers | Bear review load and possible exposure to malicious submissions | Automation, bounded claims, rotation, conflict controls, sustainable workload |
| People affected by deployed instances | May be harmed by insecure, misleading, or privacy-invasive defaults chosen during setup | High-impact defaults visible and reviewable before generation |

## I-VSD Principles And Domains

| Principle | Application to this product |
|---|---|
| Amanah / Trust | Treat configuration, secrets, store curation, support claims, and update identity as entrusted responsibilities |
| Sidq / Truthfulness | Separate planned, implemented, signed, curated, compatible, and operationally verified states |
| Adl / Justice | Make the guided path usable without withholding expert control; treat accessibility and localization as core requirements |
| Non-Harm | Reject executable definitions, unsafe writes, secret collection, silent updates, and hidden risky defaults |
| Rights of People | Preserve offline use, local import, export, correction, exit, attribution, and operator control |
| Avoiding Spying | Keep telemetry absent by default and secrets outside definitions, stores, logs, and support data |
| Avoiding Deception | Prevent “official,” “recommended,” “verified,” and ranking labels from implying evidence they do not possess |
| Promise-Keeping | Bind definitions and support claims to exact revisions and provide deprecation, cache, migration, and EOL paths |
| Ihsan / Excellence | Provide deterministic tools, conformance evidence, accessibility, recovery, and maintainable governance beyond a form renderer |

All six I-VSD domains apply:

- **Strategic:** narrow mission, open format, funding, stewardship, and EOL;
- **Design:** progressive disclosure, defaults, explanations, accessibility,
  localization, and high-impact review;
- **Technical:** bounded schema, deterministic engine, secret separation,
  protected writes, signatures, adapters, and offline operation;
- **Operational:** store review, correction, revocation, incidents, compatibility,
  support, and abandonment;
- **Governance:** namespaces, curation, ranking, commercial influence, disputes,
  and external escalation;
- **Evaluation:** startup success, stale definitions, support burden,
  accessibility findings, incidents, and operator feedback.

## Common Overlooked Failures And Outcomes

Feature type: community-supplied configuration definitions, generated `.env`
files, and cross-platform progressive-disclosure forms.

### Common overlooked failures

- A definition validates structurally but targets the wrong application
  version, container tag, or renamed variable.
- A generated Keycloak realm file broadens roles, clients, or administrative
  behavior beyond what the operator reviewed.
- A community-provided Keycloak realm file contains credentials, identities,
  private material, or state copied from another deployment.
- The basic profile hides a required field or a privacy/security consequence.
- An “all variables” view emits inactive keys and changes application behavior.
- A signed definition is mistaken for a correct or safe definition.
- A definition update changes defaults without a semantic diff or approval.
- Store reviewers become an unstaffed security and support bottleneck.
- A malicious definition creates huge graphs, cycles, collisions, unsafe file
  destinations, misleading copy, or secret-shaped fields.
- Browser, terminal, and desktop adapters share business state but quietly
  diverge in accessibility, secret handling, or output semantics.
- Application logos, copied documentation, examples, or schemas enter the
  store without license or trademark authority.
- Maintainer abandonment leaves a popular entry looking current.
- Support asks users to upload generated `.env` files, disclosing secrets.
- Ranking rewards popularity, sponsorship, or paid placement instead of
  compatibility and evidence.

### Possible bad outcomes

- broken or insecure deployments;
- leaked credentials and support artifacts;
- operators believing irrelevant or stale settings are required;
- exclusion of keyboard, screen-reader, RTL, or small-terminal users;
- centralization and lock-in around the store operator;
- reputational and legal disputes over “official” definitions;
- maintainer burnout and slow security correction;
- false confidence from signatures, badges, or polished forms;
- increased support burden for upstream self-hosted applications.

### Positive outcomes if implemented responsibly

- materially lower self-hosting friction without taking authority from the
  operator;
- safer defaults with full expert visibility and deterministic automation;
- earlier discovery of documentation/configuration drift;
- reusable accessibility and localization investment across applications;
- stronger evidence for secret minimization, release identity, and operator
  recovery;
- easier migration between manual documentation, CLI automation, TUI, desktop,
  and web workflows;
- an open ecosystem where applications can publish their own definitions and
  communities can maintain transparent alternatives.

### Provider questions before implementation

- Who owns the schema and backward-incompatible revision process?
- What exact evidence earns publisher-signed versus curated-reviewed status?
- What is the correction and revocation SLA for a dangerous definition?
- How does a fork obtain a namespace without impersonating upstream?
- Which high-impact defaults always require explicit operator review?
- How will maintainers prove compatibility with a release or image digest?
- What remains usable if the store, signing service, or project shuts down?
- Who funds review and support without influencing ranking or defaults?

## Validation Gaps

| Gap | Current evidence level | Required next validation |
|---|---|---|
| Demand across unrelated projects | Not reviewed | Interviews with self-hosters and maintainers from at least three different deployment stacks |
| Definition-model generality | Design only | Implement the ISLAMU definitions and one Keycloak realm-import definition with no Keycloak branch in the universal core |
| Keycloak realm-import support | Planned only | Define the allowlisted non-secret subset and run golden generation, rejection, semantic diff, and disposable exact-version Keycloak acceptance tests |
| Guided-profile comprehension | Not reviewed | Task-based usability testing with novice and advanced operators |
| TUI dynamic-form usability | Current narrow implementation only | Keyboard, resize, screen-reader/terminal, secret, and error-recovery evaluation over a full definition |
| Avalonia desktop/browser | Planned and disabled | Exact dependency, build, accessibility, download/write, browser-origin, and release evidence |
| Store signing and update model | Design only | Threat model covering compromise, rollback, key rotation, forks, revocation, mirrors, and offline cache |
| Definition correctness | Not reviewed operationally | Maintainer CI conformance plus startup validation against exact application releases |
| Governance capacity | Not reviewed | Submission/review cost model, maintainer rotation, conflict policy, and incident exercise |
| Business sustainability | Not reviewed | Transparent cost and funding model with ranking-independence controls |
| Real-world outcomes | None | Opt-in longitudinal support, failure, accessibility, and correction evidence after release |

## Escalation Needed

- Independent security review before accepting remote/store definitions,
  enabling signatures as a trust signal, or enabling any hosted secret mode.
- Qualified accessibility review before claiming parity or conformance for any
  Terminal.Gui, Avalonia desktop, or browser release.
- Qualified legal/IP review for definition redistribution, copied
  documentation, schemas, logos, trademarks, publisher verification, store
  terms, notices, and outbound licensing.
- Qualified privacy/legal review if future telemetry, accounts, hosted
  profiles, cloud storage, or definition analytics are introduced.
- Qualified Sunni scholarly review only if future funding, subscriptions,
  financing, sponsorship, marketing, or product claims raise a specific
  religious-legal question. This report makes no such ruling.

## Evidence Reviewed

| Evidence ID | Evidence | Revision | Use and level |
|---|---|---|---|
| E001 | `setup-assistant-security-and-portability-plan.md`, fully reviewed | `sha256:752972e2b5e85e2bbb792a92618fabb6cffccdb6bccb278d222c4ed8ee6681d9` | Current and proposed architecture, requirements, phases, risks; implementation traceability |
| E002 | `setup-assistant-security-and-portability-tasks.md`, fully reviewed | `sha256:ca6f1fb86b5edefdd187f8147c0643d500b7689197a0764cf5f44b838e06fb99` | Actual completion, disabled targets, verification, current blocker; implementation traceability |
| E003 | `setup-assistant-security-and-portability-context.md`, fully reviewed | `sha256:d388624ddf7f478d0ca22050764b5b2b500d9f35827ef1059c885eb1c190e68e` | Current state, historical supersession, risks, evidence limits; implementation traceability |
| E004 | `setup-assistant-security-and-portability-cto-review.md`, fully reviewed | `sha256:c0d87cf984794305deab53c2e9bb3f2125756d8900504cba2a5d0cff92f290e0` | Historical technical decision and scope limits; implementation traceability |
| E005 | `setup-assistant-terminal-gui-steward-approval.md`, fully reviewed | `sha256:b88937468eb334d55622cd92a3fd892352d616007bdbeb4071855d3392fd9a14` | Exact Terminal.Gui downstream-package authority; governance evidence |
| E006 | Existing Setup Assistant I-VSD consultancy report | `sha256:be9caaff617238ce6ca599db83d97b8e084804a788a3166eda80922d94749f65` | Existing provider-responsibility boundaries and stable findings; design validation |
| E007 | Existing ConfigurationManifest I-VSD project-case review | `sha256:97235659bde68f038ce569e839d5788e1c4f887b87126a8e11ab7d5871f231be` | Portability, authority, extensibility, and future-product boundaries; design validation |
| E008 | Environment catalogue and activation contracts | `sha256:a1fe8a77c5110340823e95f4c41d33c149b28fd2e8e89529b9a325392e8dbffd` aggregate with E009-E011 | Current metadata, closed graph, sensitivity, requirement, default, and relevance behavior; implementation traceability |
| E009 | Dotenv composer/readiness/codec and CLI parser/machine contract | same implementation aggregate | Relevant-only output, value-safe readiness, deterministic machine operation, and current hardcoded command surface; implementation traceability |
| E010 | Framework-neutral presentation contracts | same implementation aggregate | Current shared human-operation state and value-free messaging; implementation traceability |
| E011 | Terminal.Gui application, window, and artifact operation | same implementation aggregate | Current one-secret workflow, protected output, hardcoded standalone activation, and actual TUI scope; implementation traceability |
| E012 | Project Steward direction: `2026-09-04:planned-external-application=Keycloak-realm-import-JSON` | User-provided decision; included with the E001-E011 base digest in the reviewed-input revision | Names Keycloak realm-import JSON as the planned external conformance and support target; design authority, not implementation evidence |

The aggregate reviewed-input revision covers E001-E012 at the exact revisions
or decision statement listed above. No external competitor source, design,
code, schema, asset, or proprietary material was reviewed for this report.

## Missing Evidence

- stakeholder interviews with novice self-hosters, experienced operators,
  application maintainers, store reviewers, and disabled users;
- a source-free market and alternative-product analysis;
- a generic definition draft and conformance implementation;
- a bounded Keycloak realm-import definition, exact supported subset, and
  successful disposable exact-version import evidence;
- dynamic full-catalogue Terminal.Gui usability evidence;
- Active Avalonia desktop or browser implementation evidence;
- store threat model, signing/key-rotation design, moderation policy, and
  incident drill;
- legal conclusions about third-party definition content, trademarks, and
  redistribution;
- measured maintenance cost, review throughput, support demand, and funding;
- production incidents, accessibility audits, operator completion outcomes,
  and longitudinal trust evidence.

## Context Inventory

- Complete implementation-plan directory:
  `dev/active/setup-assistant-security-and-portability/`.
- Existing I-VSD reports:
  `i-vsd-configuration-manifest.md` and
  `i-vsd-setup-assistant-security-and-portability.md`.
- Headless environment and dotenv implementation:
  `src/Event.Setup.Core/Environment/` and `src/Event.Setup.Core/Dotenv/`.
- Machine CLI implementation:
  `src/Event.SetupAssistant.Cli/`.
- Shared presentation contracts:
  `src/Event.SetupAssistant/Presentation/`.
- Current Terminal.Gui target:
  `src/Event.SetupAssistant.Terminal/`.
- Repository configuration, self-hosting, secrets, security, governance, and
  IP boundaries under `docs/internal/`.
- I-VSD framework resources for principles, evidence levels, strategy,
  architecture, UX/defaults, business model, governance, evaluation, and
  feature-risk analysis.
- Project Steward direction naming Keycloak realm-import JSON as the planned
  external application test and support target.

## Review Lifecycle

| Date | Previous status | New status | Trigger | Evidence/replacement |
|---|---|---|---|---|
| 2026-09-04 | none | current | User requested an I-VSD analysis of extracting the ConfigurationManifest/Setup Assistant into a universal standalone product with schema import and an optional store | E001-E011; reviewed input `sha256:72b3a72d6cfab34aaad928cdcd88063b44796ed07556e87bdc5e27ef2d6a6965` |
| 2026-09-04 | current | current | Project Steward selected Keycloak realm-import JSON as the planned external application conformance and support target | E012; reviewed input `sha256:078a5af99159f12b5aa4d26f330020d2eef8765b86cb6aea7f6fffba91758b1f`; supersedes report `sha256:966b6fcd3368434b11187fb23ddc9e5d6cda310a0014c53a6c30be2dab391e1c` |

Refresh this report if the proposed workstream changes the schema's execution
power, secret custody, store requirement, trust labels, ranking/monetization,
update behavior, output types, plugin model, hosted profile storage, telemetry,
application-specific authority boundary, Keycloak supported subset/import
authority, or target accessibility claims.
