<!-- ABOUTME: I-VSD project-case review for configuration portability, UI import/export, and cross-instance migration. -->
<!-- ABOUTME: Expands whole-instance and tenant-scoped configuration while preserving autonomy, safety, and truthful authority. -->

# I-VSD Project-Case Review: Configuration Portability And Administration

Last Updated: 2026-08-31

## Review Metadata

- Mode: implementation-review
- Subject: configuration-manifest portability and administration
- Workstream: configuration-manifest
- Report kind: project-case-review
- Report status: current
- Disposition: plan-aligned
- Evidence cutoff: 2026-08-31
- Reviewed input revision: `sha256:b1bb05932eef7c11ec0af43b307d4afdb4eac17ac3b8d563f095cbe16c99f26d`
- Supersedes: the 2026-08-26 revision of this report

## Scope

This review evaluates a materially expanded configuration-portability product
for ISLAMU Event. It covers:

- whole-instance manifest import and export in the instance administration UI;
- a tenant-scoped configuration package that tenant administrators can import
  and export without cross-tenant authority;
- migration of tenant configuration between independent ISLAMU Event
  instances;
- preview, diff, conflict handling, approval, apply, rollback, recovery, and
  audit;
- portable settings, typed documents, footer, navigation, templates, lookup
  configuration, custom-property definitions, localization, registration
  configuration, module governance, and extension packs;
- typed, localized instance and tenant legal documents, including Markdown
  source, templates, lifecycle intent, target review, and publication handoff;
- deferred Terminal.Gui TUI and deterministic CLI parity for humans, scripts,
  CI, and external agents, reviewed only to preserve a clean future boundary;
- a deferred versioned agentic skill that may teach safe no-secret CLI use
  without embedding AI in the product after stable commands exist;
- extensibility contracts for future configuration sections;
- customization, authoring, collaboration, automation, observability,
  accessibility, localization, RTL, and other quality-of-life improvements;
- secrets, PII, legal identity, payment policy, provider state, and
  instance/tenant authority boundaries;
- a clean development-mode contract replacement with no compatibility layer.

The current implementation includes the secure export/bootstrap foundation,
v1alpha2 instance and tenant artifacts, typed legal Markdown, preview-first
sessions, selected atomic apply, receipts, forward rollback, tenant migration,
HAL-gated administration, declarative extension validation, managed-ownership
planning, and opt-in direct-transfer staging. Final generated-artifact,
criticality, and phase gates still determine completion; this report does not
treat implementation presence as release proof.

The active ConfigurationManifest plan now maps IVSD-F001 through IVSD-F024 into
Phases 16-23. IVSD-F025 through IVSD-F030 remain accepted future-product
findings and are explicitly deferred to a separate Setup Assistant workstream;
their deferral is part of this plan-aligned review, not a claim that the
capabilities are implemented.

## Requested Direction Coverage

| Requested direction | Report coverage |
|---|---|
| Whole-instance import in the UI | IVSD-F001, IVSD-F004-F006, Whole-Instance Administration Experience |
| Tenant-scoped configuration artifact | IVSD-F002-F003, `TenantConfigurationPackage`, Portable Configuration Coverage |
| Tenant-admin UI import/export | IVSD-F002, IVSD-F014-F015, Tenant Administration Experience |
| Easy migration between independent instances | IVSD-F007, IVSD-F011, Cross-Instance Migration Workflow |
| Migration beyond federation/PDS | IVSD-F002 and IVSD-F017 separate configuration, application-data, and backup responsibilities |
| All configuration and customization domains | Portable Configuration Coverage |
| Extensibility and future modules | IVSD-F012-F013 and Extensibility Architecture |
| Broad quality-of-life improvements | Quality-Of-Life And Advanced Improvement Catalogue |
| Security, privacy, payment, and autonomy | IVSD-F003-F011, IVSD-F015, safeguard checklist, mitigations |
| Complex and advanced features are allowed | Direct transfer, GitOps/drift, approvals, snapshots, rollback, signed packs, extension registry, migration dashboard |
| No backward compatibility | IVSD-F016 and Core Product Direction require a clean v1alpha2 replacement |
| Updated I-VSD lifecycle | Review Metadata, Evidence Reviewed, Missing Evidence, and Review Lifecycle |
| Instance and tenant terms/privacy/legal texts | IVSD-F019-F024 and Legal Document Portability |
| Templates and safe Markdown editing | IVSD-F021-F022 and Setup Assistant Legal Authoring |
| Terminal.Gui and CLI parity | IVSD-F025-F026 remain explicitly deferred until this workstream is complete |
| External agentic skill without embedded AI | IVSD-F027-F030 remain explicitly deferred until stable commands exist |

## Claim Boundary

This is provider-responsibility design reasoning and implementation
traceability. It is not:

- a fatwa or halal/haram ruling;
- Sharia, legal, privacy, security, accessibility, backup, or payment
  certification;
- proof that migration will be lossless for every future extension;
- proof that users find the workflow understandable;
- authorization to copy secrets, PII, operational payment state, or
  deployment credentials between instances;
- proof that the mapped implementation is complete, deployed, or effective.

Religious-legal conclusions about payment policies, liability, identity
disclosures, or community duties require qualified Sunni scholarly review.
Jurisdiction-specific privacy, identity, copyright, consumer, and data-transfer
conclusions require qualified legal review.

## Findings

### Finding Register

| ID | Lifecycle | Severity | Claim type | Principle and domain | Provider-controlled decision and risk | Evidence / validation level | Mitigation | Owner or escalation |
|---|---|---|---|---|---|---|---|---|
| IVSD-F001 | accepted | Critical | Portability concern | Promise-Keeping, Rights of People; Strategic/Technical | Export-only UI makes migration incomplete and keeps import dependent on startup file access or custom operator work | E001-E006; implementation traceability | IVSD-M001 | Product + Platform |
| IVSD-F002 | accepted | Critical | Tenant-autonomy concern | Justice, Amanah; Governance/Design | Instance-only export authority prevents a tenant administrator from moving configuration they legitimately govern | E003-E007; implementation traceability | IVSD-M002 | Product + Security |
| IVSD-F003 | accepted | Critical | Authority-boundary requirement | Non-Harm, Justice; Technical/Governance | A tenant-scoped artifact can become cross-tenant authority if it carries or selects target tenant identity | E002-E005; design validation | IVSD-M003 | Security |
| IVSD-F004 | accepted | Blocker | Import-safety requirement | Amanah, Ihsan; Technical/Operational | Direct upload-to-write can hide destructive changes, invalid references, policy broadening, or partial failure | E001-E006; design validation | IVSD-M004 | Platform + Database |
| IVSD-F005 | accepted | Critical | Ownership concern | Autonomy, Truthfulness; Design/Governance | Unnamed merge/replace semantics turn convenience into covert reconciliation or overwrite | E001, E004, E006; design validation | IVSD-M005 | Product |
| IVSD-F006 | accepted | Blocker | Recovery requirement | Amanah, Non-Harm; Technical/Operational | A large import without atomicity, snapshot evidence, and forward recovery can strand an instance or tenant | E001-E006; implementation traceability | IVSD-M006 | Database + Operations |
| IVSD-F007 | accepted | Critical | Portability requirement | Truthfulness, Ihsan; Technical | Source UUIDs, provider IDs, domains, lookup IDs, and extension identities are not portable authority on another instance | E007-E013; implementation traceability | IVSD-M007 | Architecture |
| IVSD-F008 | accepted | Blocker | Secret/privacy boundary | Amanah, Avoiding Spying; Technical/Governance | “Full migration” can be misunderstood as permission to export credentials, tokens, secret references, PII, or private operational data | E001-E006, E011; implementation traceability | IVSD-M008 | Security + Privacy |
| IVSD-F009 | accepted | Critical | Accountability concern | Sidq, Rights of People; Governance/Design | Copying operator identity or legal disclosures blindly can misstate who is accountable on the target deployment | E010-E011; implementation traceability | IVSD-M009 | Legal + Product |
| IVSD-F010 | accepted | Blocker | Financial-authority concern | Justice, Non-Harm; Technical/Governance | Imported paid policy can broaden authority, imply provider readiness, or copy operational payment responsibility | E001, E012; implementation traceability | IVSD-M010 | Payments + Security + scholarly/legal escalation |
| IVSD-F011 | accepted | High | Cross-instance transfer concern | Privacy, Promise-Keeping; Technical/Operational | Direct source-to-target transfer can create hidden network trust, SSRF, replay, retention, and source-deletion ambiguity | E001-E006; design validation | IVSD-M011 | Security + Operations |
| IVSD-F012 | accepted | Critical | Extensibility concern | Amanah, Ihsan; Technical/Governance | Ad hoc extension sections can smuggle executable behavior, unsafe JSON, or ungoverned authority into import | E007-E009; implementation traceability | IVSD-M012 | Architecture + Extension owner |
| IVSD-F013 | accepted | High | Completeness concern | Promise-Keeping, Truthfulness; Strategic/Technical | A “full configuration” claim is misleading unless section coverage, omissions, dependencies, and fidelity are machine-readable | E001-E013; design validation | IVSD-M013 | Product + Docs |
| IVSD-F014 | accepted | High | Accessibility concern | Justice, Ihsan; Design/Evaluation | Dense diffs and conflict resolution can exclude keyboard, screen-reader, low-vision, RTL, mobile, and non-technical administrators | E014; implementation traceability | IVSD-M014 | Frontend + Accessibility |
| IVSD-F015 | accepted | Critical | Privileged-admin concern | Amanah, Justice; Governance/Technical | Imports can become an unlogged mass-administration bypass or let a tenant admin cross instance locks | E003-E006, E010; implementation traceability | IVSD-M015 | Authorization + Audit |
| IVSD-F016 | resolved | High | Planning-integrity concern | Truthfulness, Promise-Keeping; Governance | Plan, context, and tasks could falsely claim alignment if tenant portability, UI import, or deferred-client boundaries were absent or stale | E001-E003; exact revision review | IVSD-M016 | Planning owner |
| IVSD-F017 | accepted | High | Scope-clarity concern | Avoiding Gharar, Rights of People; Strategic/Design | Configuration migration can be mistaken for event/content/user/payment migration or backup | E001, E006-E009; design validation | IVSD-M017 | Product + Docs |
| IVSD-F018 | accepted | High | Evidence concern | Ihsan, Amanah; Evaluation/Operational | Format correctness does not prove real migration fidelity, usability, recovery, or long-term extension compatibility | E001-E014; implementation traceability only | IVSD-M018 | QA + Operations + stakeholder research |
| IVSD-F019 | accepted | Critical | Legal-authority requirement | Truthfulness, Justice; Governance/Technical | Generic legal text can conflate instance, tenant, organizer, and merchant responsibility | E010-E011, E015-E018; implementation traceability | IVSD-M019 | Legal + Architecture |
| IVSD-F020 | accepted | Blocker | Evidence-integrity requirement | Amanah, Rights of People; Governance/Technical | Import can overwrite immutable published history or fabricate user acceptance | E015-E018; implementation traceability | IVSD-M020 | Legal + Domain |
| IVSD-F021 | accepted | High | Template-governance concern | Truthfulness, Avoiding Gharar; Design/Governance | Legal templates can be mistaken for legal advice or automatic compliance | E018; design validation | IVSD-M021 | Legal + Product |
| IVSD-F022 | accepted | Critical | Markdown-safety requirement | Non-Harm, Amanah; Technical/Design | Unrestricted Markdown/HTML can execute, track, deceive, or render inaccessible public content | E015-E018; implementation traceability | IVSD-M022 | Security + Accessibility |
| IVSD-F023 | accepted | High | Portability requirement | Promise-Keeping, Ihsan; Strategic/Technical | Exporting only legal URLs leaves migrated deployments dependent on source origins | E015-E018; implementation traceability | IVSD-M023 | Product + Architecture |
| IVSD-F024 | accepted | High | Bounded-content requirement | Amanah, Ihsan; Technical/Operational | Localized legal source can exceed bounded v1alpha2 content/file limits and overwhelm diff UX | E001, E015-E018; implementation traceability | IVSD-M024 | Architecture + UX |
| IVSD-F025 | accepted | High | Access/parity requirement | Justice, Ihsan; Design/Technical | GUI-only portability excludes remote-shell, terminal-first, CI, and automation users | E018-E020; functional/documentation evidence | IVSD-M025 | Product + CLI |
| IVSD-F026 | accepted | Critical | Automation-contract requirement | Truthfulness, Amanah; Technical | Agents cannot safely automate full-screen TUI state or localized prose | E018-E020; design validation | IVSD-M026 | CLI + Tooling |
| IVSD-F027 | accepted | Blocker | Agent secret boundary | Amanah, Avoiding Spying; Technical/Governance | A skill can lead agents to read, request, log, or transmit secret-bearing `.env` values | E018-E020; design validation | IVSD-M027 | Security + Agent Governance |
| IVSD-F028 | accepted | High | AI-scope requirement | Truthfulness, Avoiding Gharar; Strategic/Technical | Embedded AI adds model providers, prompts, cost, nondeterminism, and data flows unnecessary for configuration correctness | E018; user decision | IVSD-M028 | Product |
| IVSD-F029 | accepted | Critical | Approval requirement | Autonomy, Justice; Design/Governance | Agent-generated configuration can broaden policy, overwrite state, or publish legal text without informed approval | E018-E020; design validation | IVSD-M029 | Product + Agent Governance |
| IVSD-F030 | accepted | High | Skill-lifecycle requirement | Promise-Keeping, Truthfulness; Governance | A skill published before stable commands exist becomes fictional and unsafe | E018-E020; skill-contract evidence | IVSD-M030 | Skill owner + CLI owner |

### IVSD-F001 — Whole-Instance UI Import Is A Portability Obligation

The current instance UI can download `Overrides` and `Portable` files, but
there is no corresponding import flow. This creates an avoidable gap between
the promise of portable configuration and what an administrator can actually
do without filesystem/deployment access.

The provider should treat import as a first-class instance administration
capability, not as an operator-only afterthought. The UI must expose the same
strict validation and authority as startup bootstrap while adding preview,
selection, conflict resolution, approval, progress, and recovery.

### IVSD-F002 — Tenant Configuration Is A Legitimate Tenant Portability Right

Cross-tenant whole-instance export must remain instance-authorized. That does
not justify denying tenant administrators a package containing only the
configuration of the tenant they currently administer.

A tenant administrator should be able to:

- export the tenant's portable configuration;
- inspect explicit omissions and target requirements;
- import it into an existing tenant they administer on another instance;
- clone it into a newly created tenant when the target instance authorizes
  tenant creation;
- selectively migrate sections instead of accepting an all-or-nothing package;
- retain a receipt and rollback package.

This supports credible self-hosting and reduces dependence on federation or PDS
publication for migration. Federation moves public records and identity-linked
content; it does not replace tenant configuration portability.

### IVSD-F003 — A Tenant-Scoped Package Must Never Select Target Authority

The portable tenant artifact should use a distinct kind such as
`TenantConfigurationPackage`, not masquerade as a complete instance bootstrap
root. The target tenant is selected from the authenticated route/session and
server authorization.

The artifact may contain a source tenant slug and portable display metadata for
provenance, but those values never decide where writes occur. Tenant IDs,
instance IDs, actor IDs, database IDs, provider accounts, and source HAL URLs
are not target authority.

### IVSD-F004 — Import Must Be Preview-First

Uploading bytes must not immediately mutate state. The minimum safe sequence is:

1. bounded upload to the BFF;
2. strict server-side lexical/contract scan;
3. creation of a short-lived import session identified by digest;
4. source/target scope verification;
5. section dependency and reference analysis;
6. current-state diff;
7. blockers, warnings, omissions, and approval requirements;
8. explicit administrator selection and confirmation;
9. fresh authority and revision replay under locks;
10. atomic apply and durable receipt;
11. post-commit effects and verification;
12. rollback-as-forward-import when required.

Preview must be side-effect-free. It must not create tenants, resolve secrets,
call providers, write audit rows that imply application, or reserve locks for
an unbounded period.

### IVSD-F005 — Import Modes Must Be Named And Understandable

The product should not expose a vague “Import” button with hidden behavior.
Supported modes should be explicit:

| Mode | Intended use | Protective behavior |
|---|---|---|
| `PreviewOnly` | Inspect compatibility and diff | No configuration writes |
| `CreateNew` | Create a new tenant from a tenant-scoped package | Target slug/name chosen under target authority; fails if occupied |
| `MergeMissing` | Fill only absent portable fields | Never overwrites existing target values |
| `ApplySelected` | Apply administrator-selected sections/fields | Diff and consequences shown before confirmation |
| `ReplacePortableConfiguration` | Make selected portable sections match the package | Requires enhanced confirmation, snapshot, and full dependency validation |
| `ReconcileManaged` | Optional future GitOps ownership | Requires explicit field ownership, drift, takeover, deletion, and conflict design |

`ReplacePortableConfiguration` is not deletion of application data. Omitted or
nonportable sections remain untouched unless a future section contract
explicitly defines safe deletion and the UI names it.

### IVSD-F006 — Recovery Is Part Of Import, Not Documentation Polish

Every accepted import should produce:

- operation UUIDv7;
- source digest and normalized selected-section digest;
- source artifact kind/version;
- target scope;
- selected sections and mode;
- pre-import target revisions;
- changed key/section identities without raw secret/PII values;
- created/skipped/replaced counts;
- warnings and unresolved external setup requirements;
- post-import revisions;
- payload-free outbox effects;
- a deterministic verification result;
- a rollback package or pointer to a protected pre-import snapshot.

Rollback should be a new, authorized forward operation using the captured
portable state. It should not mutate or delete history.

### IVSD-F007 — References Need Stable Mapping, Not Blind Copying

Portable artifacts should prefer stable machine keys and natural identities:

- tenant slugs and manifest-local symbols;
- lookup `MasterCode`;
- module keys;
- custom-property `Namespace + Key`;
- template keys and explicit versions;
- BCP-47 language tags;
- ISO currency codes;
- section-local symbolic references.

When a source value references a target-owned resource, the preview should
classify it as:

- automatically mapped by stable identity;
- requires administrator mapping;
- can be created safely;
- unavailable but optional;
- unavailable and blocking;
- intentionally omitted.

The UI should show source label, target candidate, mapping basis, confidence,
and consequence. It must never silently map by localized display text.

### IVSD-F008 — Full Configuration Must Still Exclude Secrets And Private Data

Maximum migration ease does not require copying secret material. A package can
carry non-secret provider intent and a machine-readable requirement such as
“SMTP credential must be configured,” but not:

- plaintext secrets;
- encrypted secrets whose target decryption authority is unrelated;
- secret-provider paths or environment variable names that reveal operational
  structure without explicit approval;
- API keys, OAuth tokens, webhook secrets, signing keys, or encryption keys;
- registration answers, user records, event attendees, payment attempts,
  provider payloads, or audit content;
- source infrastructure addresses when they are unsafe or environment-bound.

The preview should generate a target setup checklist for excluded dependencies
without claiming the package is incomplete or broken.

### IVSD-F009 — Identity And Legal Accountability Require Target Confirmation

Tenant directory-operator identity may be portable because it is tenant-owned
public accountability configuration, but it must be highlighted for review.
The target instance operator identity must never be imported from another
instance.

When cloning a tenant:

- cosmetic branding can be selected independently;
- tenant directory identity requires explicit confirmation;
- target instance/operator disclosures are recomposed from target authority;
- organizer merchant identities are not tenant configuration;
- domains and public origins require target-side verification;
- legal, terms, privacy, and contact URLs are checked for safe HTTPS and target
  relevance.

### IVSD-F010 — Paid Policy Import Is Policy Migration, Not Payment Readiness

Portable paid-event policy can express instance ceilings or tenant narrowing,
but importing it does not prove:

- provider credentials exist;
- connected organizer accounts are valid;
- the target operator accepts payment responsibility;
- sale control is open;
- legal/scholarly review is current;
- refund/reconciliation workers are healthy;
- historical orders can move.

Instance paid-policy import requires instance authority and revision fencing.
Tenant policy remains narrowing only and must be validated against fresh target
instance authority. Provider identity, connected accounts, checkout state,
acceptance, disputes, liability, reconciliation, and refund execution remain
excluded.

### IVSD-F011 — User-Mediated File Transfer Is The Safe Default

The default migration path should be:

```text
source export -> user-controlled file -> target preview -> target apply
```

An optional direct instance-to-instance transfer may be added later only with:

- explicit source and target approval;
- mutually authenticated short-lived transfer sessions;
- exact package digest and replay protection;
- target-controlled network egress and SSRF protections;
- no bearer token in browser-visible URLs;
- expiry, cancellation, and receipt;
- a clear answer about whether the source remains authoritative;
- no automatic source deletion.

Direct transfer is convenience, not a prerequisite for portability.

### IVSD-F012 — Extension Sections Need A Governed Contract

Future modules and plugins should contribute configuration through a registry
descriptor, not arbitrary root JSON. Each section descriptor should declare:

- stable section key and owner;
- current schema version;
- portable, target-mapped, environment-bound, secret-dependent, or
  nonexportable classification;
- source scope and permitted target scopes;
- required permissions and approval class;
- dependencies and stable reference types;
- validator, normalizer, preview/diff composer, applier, verifier, and rollback
  composer;
- export redactor;
- maximum size/cardinality;
- generated JSON Schema fragment;
- migration path from supported section versions;
- tests for wrong scope, secrets, PII, rollback, determinism, and extension
  absence.

An extension package may carry declarative configuration data. It must not
carry executable assemblies, scripts, SQL, migration code, expressions, or
remote code references.

### IVSD-F013 — “Full” Requires A Machine-Readable Coverage Ledger

Every export should contain or accompany a coverage summary:

| Classification | Meaning |
|---|---|
| `Included` | Exported and expected to apply with full fidelity |
| `IncludedWithMapping` | Exported but requires target resource mapping |
| `IncludedWithReview` | Exported but requires enhanced approval |
| `OmittedSecret` | Deliberately excluded secret material |
| `OmittedPrivateData` | Deliberately excluded PII or application data |
| `EnvironmentBound` | Must be configured on target infrastructure |
| `UnsupportedOnTarget` | Target lacks required module/section/version |
| `DeferredExternalSetup` | Import can succeed but a provider/setup task remains |

The UI should never label an export “complete” without showing this ledger.

### IVSD-F014 — Migration UX Must Be Accessible Under Complexity

The workflow should support:

- one page `h1` and sequential headings;
- keyboard-operable upload, section selection, mapping, review, and
  confirmation;
- semantic tables plus a linear list alternative for large diffs;
- non-color blocker/warning/change indicators;
- focus restoration after dialogs;
- focused alerts for capability loss;
- polite progress and completion announcements;
- resumable server-side import sessions rather than browser-memory dependence;
- responsive reflow at narrow widths;
- logical CSS and tested RTL;
- localized section names, consequences, and error guidance;
- reduced-motion progress;
- downloadable accessible plain-text/JSON diff;
- no requirement to visually compare raw JSON.

### IVSD-F015 — Import Is Privileged Administration

Every UI affordance comes from HAL. Every direct API call independently checks
authority. Import permission should be section-aware:

- tenant administrators may apply tenant-owned portable sections to their
  current target tenant;
- instance locks and ceilings remain authoritative;
- instance-only sections require instance authority;
- security/provider/payment broadening can require a second approver;
- creating a new tenant requires target instance authority or an explicitly
  delegated tenant-provisioning capability;
- import sessions and receipts are private, no-store resources;
- admin actions are rate-limited, size-limited, auditable, and protected by
  antiforgery at the BFF boundary.

### IVSD-F016 — The Existing Plan Must Be Re-Baselined

The previous plan deliberately removed tenant-scoped export and implemented no
UI import. The active re-baseline corrected that gap with explicit v1alpha2
artifact, authorization, UX, persistence, concurrency, accessibility,
migration, generation, and operations tasks. Alignment remains conditional on
the named phase gates; no phase is complete merely because code exists.

### IVSD-F017 — Configuration Portability Is Not Data Migration Or Backup

The product should present three separate tools:

1. configuration package for settings, typed documents, definitions, and
   templates;
2. application-data migration/export for events, users, registrations,
   orders, payments, and other aggregates;
3. backup/restore for database, object storage, authority stores, keys, and
   operational history.

They may be combined by a future migration orchestrator, but their artifacts,
permissions, retention, integrity, and recovery semantics remain distinct.

### IVSD-F018 — Outcome Evidence Must Go Beyond Green Tests

Required validation should include:

- real source-to-target migration drills;
- supported provider pairs and version pairs;
- import interruption and restart;
- rollback drills;
- wrong-tenant and forged-target attempts;
- stale preview and concurrent administrator changes;
- section-extension absence and version mismatch;
- accessibility and RTL usability;
- self-hoster and tenant-admin task studies;
- support burden and failure comprehension;
- fidelity measurements by section;
- long-term package compatibility policy.

### IVSD-F019 — Legal Documents Need First-Class Role Authority

Terms, privacy notices, cookie policy, accessibility statements, codes of
conduct, moderation rules, payment/refund notices, and other public legal texts
should be portable configuration. They must not use a generic document bag.

Each document is owned by an explicit role:

- instance/platform operator;
- tenant/directory operator;
- organizer/merchant for event-specific documents outside this package.

Instance documents cannot silently speak for a tenant. Tenant documents cannot
replace the instance operator’s platform, privacy, security, or payment
responsibilities. Single-tenant deployments still preserve the scopes even
when one organization fills both roles.

### IVSD-F020 — Portable Legal Text Is Not Acceptance History

The artifact may carry current source Markdown, drafts, localized variants,
template provenance, proposed publication state, and proposed effective dates.
It must not carry or synthesize:

- historical published versions;
- account/user acceptance facts;
- consent timestamps;
- notification delivery evidence;
- dispute/legal-hold state;
- source operator authority on the target.

Target import creates a draft or new target-owned legal version after review.
It never claims users accepted the source instance’s version.

### IVSD-F021 — Legal Templates Must Remain Non-Certifying

Templates can reduce friction, but they are starting points. Every template
needs role, language, jurisdiction assumptions, typed placeholders, version,
provenance, license, completeness checks, and legal-review status.

The product must not claim that selecting a template makes an instance
compliant, legally sufficient, or Islamically approved.

### IVSD-F022 — Markdown Requires One Constrained Profile

Legal source should use a safe Markdown subset:

- structural prose, lists, tables, block quotes, and safe links;
- no raw HTML;
- no scripts, styles, forms, SVG, iframe, object, embed, or executable content;
- no remote images, tracking pixels, data URLs, or automatic resource fetch;
- allowlisted link schemes;
- deterministic normalization, sanitization, and rendering;
- accessible heading/link checks;
- bounded bytes, links, placeholders, and locales.

Editor preview, Application validation, public rendering, import, and export
must use the same parser/sanitizer contract.

### IVSD-F023 — Portable Legal Source Must Replace Source-Origin Dependence

A migrated tenant should not remain dependent on old `TermsUrl` or
`PrivacyUrl` origins. Owned Markdown source and typed metadata belong in the
instance manifest or tenant-scoped package.

Import must identify source-origin links, rebind identity placeholders, require
target review, preserve provenance, and create a new target draft/version.
Auto-publication is forbidden.

### IVSD-F024 — Legal Content Requires New Contract Limits

Multiple document kinds and localized variants are larger than current compact
settings. The next clean contract needs justified limits for:

- document kinds per scope;
- locale variants;
- Markdown bytes;
- placeholders and links;
- aggregate package size;
- streaming parse and deterministic diff summaries.

The existing compact string/file ceilings must not be copied blindly.

### IVSD-F025 — CLI/TUI Is A Portability Surface

The Setup Assistant should expose the same manifest/package, environment, and
legal-document workflows through:

- Avalonia web/desktop;
- Terminal.Gui interactive TUI;
- deterministic noninteractive commands.

Parity means equivalent core decisions, diagnostics, and byte-identical
artifacts—not identical pixels or unsupported terminal accessibility claims.

### IVSD-F026 — Agents Must Use Commands, Not TUI Screens

External agents and scripts need:

- stable command names;
- versioned JSON;
- closed diagnostic codes;
- stable exit categories;
- explicit paths;
- no-secret and dry-run defaults;
- artifact digests and coverage/readiness summaries.

Full-screen TUI automation is fragile and should be reserved for humans.

### IVSD-F027 — The Skill Must Never Handle Secrets

The future Setup Assistant skill must prohibit agents from:

- reading existing `.env` files;
- asking users to paste secrets;
- passing secrets in arguments, captured stdin, logs, or reports;
- writing completed secret-bearing dotenv files;
- inferring credentials;
- using machine output that contains values.

Agents generate relevant empty placeholders and hand secret completion to the
user’s local desktop/TUI session.

### IVSD-F028 — AI Remains External

The product needs no model SDK, provider, prompt runtime, chat UI,
natural-language parser, autonomous loop, AI telemetry, or AI API key.

An optional external agent uses the deterministic CLI. Users who do not want AI
receive the complete product without an unused AI subsystem.

### IVSD-F029 — Agent Drafts Need Human Approval

Agents may generate and validate drafts, diffs, coverage, and no-secret
dotenv files. They cannot autonomously:

- apply to a live instance;
- replace configuration;
- broaden security/privacy/payment policy;
- publish legal documents;
- assert counsel review;
- complete secrets.

### IVSD-F030 — Skill Publication Follows CLI Implementation

The operational skill should be created only after command names, JSON schema,
exit categories, help output, secret enforcement, and examples are
implemented. It must declare compatible CLI versions and fail closed on
mismatch.

## Recommendations

### Core Product Direction

Adopt two explicit artifact families and replace the current v1alpha1 contract
cleanly:

| Artifact | Authority | Primary jobs | Must not do |
|---|---|---|---|
| `ConfigurationManifest` | Instance administrator or trusted startup owner | Whole-instance preview/import/export, initial bootstrap, multi-tenant migration, controlled instance configuration replacement | Grant tenant authority or carry secrets/application data |
| `TenantConfigurationPackage` | Current tenant administrator; target tenant resolved server-side | Tenant-scoped preview/import/export, clone, selective migration, reusable tenant template | Select another tenant, change instance state, bypass locks, or claim to be a complete instance root |

Use a new contract version such as `configuration.islamu.org/v1alpha2`. Remove
the old v1alpha1 public contract, schema, generated client types, routes,
components, media types, tests, and documentation in one cut. Do not add
aliases, dual reads, dual writes, converters, deprecated fields, redirects, or
compatibility modes.

Persisted application data should be handled through a deliberate development
reset or newly generated corrective migration strategy selected by the
implementation plan. Generated migrations and snapshots remain tool-owned.

### Whole-Instance Administration Experience

Add an instance administration workspace with:

1. **Export**
   - Overrides;
   - Portable;
   - section-selective export;
   - tenant subset selection;
   - coverage/omission ledger;
   - optional detached signature and checksum;
   - reusable baseline/template export.
2. **Import**
   - drag/drop and file picker;
   - upload digest and provenance;
   - contract/version detection;
   - target compatibility scan;
   - section tree and diff;
   - mapping workspace;
   - blocker/warning filters;
   - impact summary;
   - approval requirements;
   - apply mode selection;
   - confirmation phrase for replacement;
   - progress and resumability;
   - verification receipt;
   - rollback action.
3. **History**
   - prior exports/imports;
   - actor, time, digest, mode, target, and status;
   - changed section/key identities;
   - downloadable safe receipt;
   - compare two operations;
   - retry post-commit effects;
   - start rollback from a protected snapshot.
4. **Drift**
   - compare current state to an uploaded or retained package;
   - no write unless explicitly confirmed;
   - future managed ownership only after a separate approval.

### Tenant Administration Experience

Add a tenant administration `Configuration portability` section. HAL should
advertise independent relations for:

- export overrides;
- export portable;
- preview import;
- apply import;
- clone into a new tenant when delegated;
- view import history;
- download receipt;
- rollback;
- manage reusable tenant templates.

The tenant administrator flow should:

1. show exactly which tenant is the target;
2. forbid changing target identity inside the artifact;
3. explain instance locks and unavailable sections before upload;
4. export only current-tenant configuration;
5. preview source and target identities side-by-side;
6. permit section and field selection;
7. require target mappings where needed;
8. clearly separate skipped-by-lock from unsupported and unchanged;
9. never expose instance or other-tenant values in diff computation;
10. revalidate HAL and server authorization immediately before apply;
11. retain focus and accessible status when capability changes.

### Cross-Instance Migration Workflow

Recommended default:

```text
Source instance
  -> tenant admin exports TenantConfigurationPackage
  -> package includes digest, coverage, source versions, and safe provenance
  -> administrator uploads package to target instance
  -> target resolves current tenant from trusted authority
  -> target previews compatibility, mappings, locks, and omissions
  -> administrator selects mode and fields
  -> target snapshots current portable state
  -> target applies atomically
  -> target verifies and returns receipt
  -> administrator completes external setup checklist
```

For a full instance move, use the same sequence with
`ConfigurationManifest`, instance authority, optional tenant selection, and
stronger approvals.

### Portable Configuration Coverage

The expanded plan should evaluate every category below. Inclusion is never
automatic; each category needs a concrete owner and portability classification.

| Category | Recommended treatment |
|---|---|
| Explicit scalar settings | Include only through scope-specific safe catalogs |
| Typed tenant documents | Include all approved non-secret documents: public experience, render policy, module governance, branding, directory identity, event defaults |
| Instance legal documents | Include typed owned Markdown/metadata; target review creates new versions and never carries acceptance history |
| Tenant legal documents | Include tenant-owned Markdown/metadata in tenant-scoped packages; instance requirements remain visible and additive |
| Paid-event policy | Include policy only with target ceiling validation and enhanced review |
| Footer scalar settings | Include typed values subject to target instance locks |
| Footer link groups and links | Include stable order, labels, safe URLs, and active state |
| Navigation | Include tenant-owned links/order after safe URL and route validation |
| Module capabilities | Include desired tenant enablement only when target supports the module |
| Event and session templates | Include definitions, versions, options, and sync policy; never source runtime event IDs as authority |
| Custom-property definitions/options | Include tenant-owned definitions, governance flags, quotas, reservations, and template provenance |
| Custom-property values | Exclude by default because they are application data; offer only in a separate data-migration artifact |
| Lookup configuration | Include stable codes and labels when tenant-owned; map by `MasterCode`, never localized name |
| Localization governance | Include enabled/fallback languages and offline mode |
| Translation bundles | Optional section with language tag, bundle schema, checksum, and explicit target storage handling |
| Registration forms | Include immutable definitions/versions, language tags, mappings, and publication state only after a dedicated portability review |
| Registration answers/files | Exclude as participant application data and PII |
| Registration modes/scopes/policies | Include typed policy and capability configuration |
| Registration provider bindings | Include provider-neutral non-secret intent and mapping; omit tokens, webhook secrets, external object IDs, and live subscriptions |
| Public experience and SEO | Include portable tenant-owned presentation policy |
| Branding assets | Include safe URLs by default; optional embedded-asset package requires malware, license, size, and storage review |
| Custom domains | Export as environment-bound requirements; target must reverify ownership and TLS |
| Storage policy and quotas | Include non-secret ceilings/choices only when target provider supports them |
| Storage credentials/object data | Exclude; use secret setup and data migration tools |
| SMTP/email policy | Include non-secret policy; exclude credentials and sender verification state |
| Webhook configuration | Include non-secret endpoint intent only after SSRF and ownership review; exclude signing secrets and delivery history |
| Notification policy | Include tenant governance; exclude user subscriptions and recipient data |
| Analytics/privacy governance | Include only clearly portable non-secret policy with explicit review |
| Authorization mode/policies | Instance-only enhanced approval; never silently change target provider or import credentials |
| AI/MCP governance | Include non-secret enablement/limits only when target capabilities match; exclude provider keys and retained conversations |
| API keys | Exclude key material; optionally export names/scopes as recreation tasks |
| Domains, deployment, database, cache, scheduler | Environment-bound; never normal portable state |
| Audit, outbox, worker leases, health history | Exclude as operational state |
| Events, users, registrations, orders, tickets, payments | Separate application-data migration |

### Legal Document Portability

Use a first-class typed `LegalDocumentBundle`. Each entry should include:

- document kind;
- owner scope;
- language tag;
- audience;
- title and summary;
- constrained Markdown source;
- content digest;
- lifecycle intent;
- proposed effective date;
- fresh-acceptance requirement;
- accountable identity reference/revision;
- template ID/version/provenance;
- jurisdiction assumptions;
- change summary;
- typed placeholders and completeness state.

Recommended lifecycle:

```text
Draft -> ReviewRequired -> Approved -> Scheduled -> Published -> Retired
```

The artifact expresses desired configuration. Canonical Domain mutation creates
immutable target versions, publication facts, and acceptance requirements.

Candidate instance-owned kinds:

- terms of service;
- privacy notice;
- cookie policy;
- acceptable use;
- community/content rules;
- moderation, reporting, appeal, and correction;
- accessibility statement;
- legal notice/imprint;
- security/vulnerability disclosure;
- retention, erasure, and portability;
- subprocessors;
- open-source/license attribution;
- API/developer terms;
- federation/ATProto notice;
- payment operation, platform fee/contribution, refund, complaint, dispute,
  and reconciliation responsibilities;
- support, service availability, EOL, and migration notice.

Candidate tenant-owned kinds:

- directory/tenant terms;
- tenant privacy/controller notice;
- local code of conduct;
- organizer/event-submission terms;
- event publication/moderation policy;
- cancellation/refund baseline;
- registration/participant privacy;
- media/photography consent information;
- safeguarding/minor-participation policy;
- venue/accessibility information policy;
- complaint/correction/copyright contact;
- sponsorship/partner disclosure;
- local retention/contact-sharing notice.

The kind catalogue remains closed. Every addition needs authority, lifecycle,
public route, acceptance, portability, validation, and legal-review decisions.

Instance and tenant documents are role-labeled and additive. Instance
governance may require tenant document kinds or minimum disclosures, but it
must not silently author factual tenant claims.

### Setup Assistant Legal Authoring

The proposed cross-platform Setup Assistant should provide:

- bundled project-owned or approved-FOSS legal templates;
- role/kind/language/jurisdiction selection;
- constrained Markdown source, outline, and sanitized preview;
- typed placeholder insertion and unresolved-placeholder checks;
- identity/contact/jurisdiction consistency diagnostics;
- source-origin link detection;
- locale comparison and RTL preview;
- source/target/template diff;
- byte/link/heading/placeholder counts;
- publication and acceptance-impact checklists;
- manifest/package export.

Templates are versioned, provenance-recorded, non-certifying starting points.
External public legal prose must not be copied. The editor does not auto-publish
or replace qualified counsel.

### CLI/TUI And Agent Automation

Add `Event.SetupAssistant.Cli` over the same headless setup core.

Recommended command families:

```text
event-setup tui
event-setup catalog list|explain
event-setup manifest new|validate|format|diff|coverage|export
event-setup tenant-package new|validate|format|diff|coverage|export
event-setup env plan|render|validate|explain
event-setup legal template-list|new|validate|render|diff
event-setup doctor
```

Final naming belongs to planning. Once shipped, machine mode must use one
versioned JSON object containing status, closed diagnostics, artifact
kind/path/digest/sensitivity, coverage, and readiness. It must not contain
secret values, raw exceptions, ANSI control sequences, or localized prose as
authority.

Terminal.Gui provides the human TUI. Agents use machine commands. Secret entry
is interactive TTY-only and writes directly to a protected file; noninteractive
mode is no-secret only.

After implementation, create a schema-compliant
`.agents/skills/setup-assistant-cli/` workflow with focused resources for:

- command contract;
- secret safety;
- human TUI navigation;
- manifest/package workflows;
- dotenv workflows;
- legal-document workflows.

The skill defaults to no-secret and dry-run, requires semantic diff approval,
never reads `.env`, and hands secret completion to the local human UI. The
Setup Assistant itself contains no embedded AI.

### Extensibility Architecture

Create a configuration-section registry with one descriptor per section:

```text
section identity
  + schema/version
  + scope and authority
  + portability class
  + dependencies/reference kinds
  + export composer/redactor
  + preview/diff composer
  + validator
  + transaction-aware applier
  + verifier
  + rollback composer
  + generated schema/docs
  + contract tests
```

Recommended portability classes:

- `Portable`;
- `PortableWithMapping`;
- `PortableWithEnhancedReview`;
- `EnvironmentBound`;
- `SecretDependent`;
- `ApplicationData`;
- `OperationalState`;
- `Unsupported`.

Extension sections should be deterministic, declarative, bounded, and
source-generated. Missing optional extensions should produce an understandable
skip or blocker according to declared dependency rules. No extension may
silently disappear from export.

### Quality-Of-Life And Advanced Improvement Catalogue

#### Authoring and validation

- schema-aware editor integration;
- generated examples per section;
- inline safe fix suggestions;
- deterministic formatter;
- CLI and UI lint;
- semantic validation without deployment;
- policy simulation;
- paid-policy narrowing simulator;
- URL and domain safety checks;
- section dependency graph;
- extension compatibility matrix;
- package-size estimator;
- localization completeness check;
- target-capability preflight before export;
- machine-readable coverage report.

#### Import preview and diff

- nested field diff with old/new/source/target provenance;
- changed/unchanged/skipped/blocking/warning filters;
- side-by-side and linear accessible views;
- section select-all with safe defaults;
- search by setting or section;
- dependency-aware selection;
- impact explanation in user language;
- estimated write counts;
- target lock visualization;
- source and target version comparison;
- reference mapping suggestions with confidence;
- downloadable preview;
- shareable approval request without package values;
- stale-preview indicator and automatic safe refresh.

#### Migration and portability

- reusable tenant templates;
- clone tenant;
- promote staging configuration to production;
- compare two instances;
- compare two tenants;
- section-only transfer;
- tenant subset selection in instance export;
- baseline plus environment overlay;
- source package signature/checksum;
- import receipts and fidelity report;
- target setup checklist;
- optional embedded assets with independent governance;
- resumable upload;
- cancellation before commit;
- offline/air-gapped workflow;
- future mutually authenticated direct transfer;
- EOL bulk export;
- migration readiness dashboard.

#### Safety and recovery

- automatic pre-import portable snapshot;
- one-click forward rollback;
- two-person approval for security/payment broadening;
- confirmation phrase for replacement;
- maintenance-window scheduling;
- import rate limits and quotas;
- dry-run by default;
- stale revision fencing;
- per-section locks;
- atomic all-selected-section apply;
- durable post-commit effect retry;
- safe failure receipt;
- support bundle with no values;
- integrity verification after restart;
- backup readiness warning before high-impact import.

#### Collaboration and governance

- draft import plans;
- approval comments and decisions;
- separation of uploader, reviewer, and applier;
- section ownership contacts;
- policy exception requests;
- import history comparison;
- export provenance labels;
- signed curated configuration packs;
- community-maintained templates with source/license metadata;
- instance policy for which pack issuers are trusted;
- review reminders for stale packages;
- changelog generation from applied configuration.

#### Automation and GitOps

- CLI parity;
- Terminal.Gui human TUI;
- versioned machine JSON and stable exit categories;
- no-secret/dry-run agent defaults;
- artifact digest and coverage/readiness output;
- skill/CLI version compatibility check;
- API parity with BFF-safe browser flows;
- CI schema and policy check;
- signed artifact verification;
- declarative drift report;
- pull-request preview artifact;
- planned apply windows;
- webhooks/outbox after successful apply;
- no continuous reconciliation unless field ownership is enabled;
- explicit ownership relinquish/takeover;
- drift alerts without automatic overwrite;
- immutable operation receipts.

#### Accessibility, localization, and usability

- keyboard-complete wizard;
- screen-reader summaries;
- focus-safe capability loss;
- non-color status vocabulary;
- responsive narrow-screen workflow;
- logical CSS and RTL;
- localized setting and section labels;
- localized consequences and recovery;
- reduced-motion progress;
- plain-language and expert modes;
- raw JSON available but never required;
- accessible downloadable text diff;
- save/resume draft;
- administrator onboarding tutorial;
- contextual links to authoritative docs.

#### Operations and support

- import health/readiness;
- pending effect visibility;
- failed operation dashboard;
- safe retry and dead-letter reconciliation;
- section fidelity metrics;
- portability success/failure metrics;
- migration duration and rollback metrics;
- support-case correlation by operation ID;
- no configuration values in metrics/logs;
- retention policy for uploads/previews/snapshots;
- automatic expiry of abandoned import sessions;
- disaster-recovery rehearsal workflow;
- provider/version compatibility evidence ledger.

#### Legal content and counsel workflow

- document-kind coverage dashboard;
- required-clause checklist;
- template comparison;
- source-origin and broken-link diagnostics;
- legal identity placeholder binding;
- counsel-review status/evidence reference;
- effective-date and acceptance-impact preview;
- localized completeness and RTL review;
- previous-version and migration diff;
- public footer/navigation preview;
- accessible plain-text export;
- changelog and user-notification checklist;
- stale-review reminder;
- section-selective legal export/import.

### Security, Privacy, Payment, And Autonomy Safeguards

1. The BFF owns browser upload/download boundaries; tokens remain server-side.
2. Uploads are bounded, streamed to protected temporary storage, content-type
   independent, malware-scanned if assets are admitted, and expired
   automatically.
3. Artifact bytes never select target instance, tenant, user, actor, or
   provider authority.
4. HAL controls UI affordances; API/Application authorization controls actual
   reads and writes.
5. Tenant preview/export queries preserve tenant filters and cannot inspect
   other tenants.
6. Instance imports require instance authority and may require second approval.
7. Secrets and PII are excluded and scanned at export, preview, audit,
   ProblemDetails, logs, metrics, traces, and support artifacts.
8. Import does no external provider I/O inside the transaction.
9. Locks are deterministic and acquired before a fresh serializable snapshot.
10. Every selected section is preflighted before any selected section writes.
11. Paid policy remains target-instance constrained and revision fenced.
12. Tenant-admin import cannot weaken instance locks or ceilings.
13. Legal/operator identity changes are highlighted and explicitly confirmed.
14. Direct transfer is replay-protected, mutually approved, and optional.
15. Configuration, application data, operational state, and backup artifacts
    remain separate.
16. Import history is append-only and value-minimized.
17. Rollback is a new authorized forward operation.
18. Extensions cannot execute code or supply migrations.
19. Legal documents preserve instance/tenant/organizer role labels.
20. Legal import creates target drafts/new versions and never acceptance
    history.
21. Markdown is constrained, sanitized, deterministic, and non-fetching.
22. Templates are project-owned or MIT-licensed, versioned, and
    non-certifying.
23. Source-origin links and unresolved placeholders block publication
    readiness.
24. Legal content limits are separately bounded and denial-of-service tested.
25. TUI and CLI adapt the same headless core as Avalonia.
26. Agents consume versioned JSON and never automate full-screen TUI state.
27. CLI arguments, stdout, stderr, JSON, history, and skill flows contain no
    secrets.
28. No model/provider/prompt/agent loop is embedded in the product.
29. Agent-generated writes and authority changes require human approval.
30. The operational skill is published only after the CLI contract exists.

### Mitigation Register

| Mitigation | Requirement | Findings |
|---|---|---|
| IVSD-M001 | Add whole-instance preview/import/history/rollback to instance UI and BFF/API | F001 |
| IVSD-M002 | Add tenant-scoped export/import under tenant-admin authority | F002 |
| IVSD-M003 | Use distinct `TenantConfigurationPackage`; resolve target only from trusted current authority | F003 |
| IVSD-M004 | Make import preview-first with strict scan, diff, mapping, and fresh apply authorization | F004 |
| IVSD-M005 | Expose named modes and explicit field/section ownership; no hidden overwrite/reconcile | F005 |
| IVSD-M006 | Use lock-ordered atomic apply, pre-import snapshot, durable receipt, and forward rollback | F006 |
| IVSD-M007 | Use stable symbolic identities and explicit target mapping | F007 |
| IVSD-M008 | Exclude and scan secrets, PII, application data, and operational state; generate setup tasks | F008 |
| IVSD-M009 | Require explicit identity/legal review and always recompose target instance disclosure | F009 |
| IVSD-M010 | Keep paid policy target-constrained; exclude provider/payment operational authority | F010 |
| IVSD-M011 | Default to user-mediated files; gate optional direct transfer behind mutual trust and replay protection | F011 |
| IVSD-M012 | Add governed extension-section descriptors; prohibit executable content | F012 |
| IVSD-M013 | Generate machine-readable coverage/omission/fidelity ledger | F013 |
| IVSD-M014 | Make upload, diff, mapping, apply, progress, and rollback WCAG 2.2 AA/RTL/localization ready | F014 |
| IVSD-M015 | Use HAL, section-aware authorization, enhanced approvals, audit, antiforgery, and rate limits | F015 |
| IVSD-M016 | Re-baseline plan, scenarios, tasks, contracts, tests, docs, and evidence before implementation | F016 |
| IVSD-M017 | Present configuration, data migration, and backup as separate tools | F017 |
| IVSD-M018 | Run real migration, rollback, accessibility, stakeholder, and operational validation | F018 |
| IVSD-M019 | Add role-scoped typed legal-document bundles for instance and tenant configuration | F019 |
| IVSD-M020 | Separate portable legal source/drafts from immutable publication and acceptance evidence | F020 |
| IVSD-M021 | Govern project-owned or MIT-licensed legal templates as non-certifying starting points | F021 |
| IVSD-M022 | Share one constrained Markdown parser/sanitizer across every surface | F022 |
| IVSD-M023 | Export owned legal source and import it as target-reviewed drafts/new versions | F023 |
| IVSD-M024 | Re-baseline bounded legal-content limits and diff UX | F024 |
| IVSD-M025 | Ship Terminal.Gui TUI and deterministic CLI adapters over the shared core | F025 |
| IVSD-M026 | Provide versioned machine JSON, diagnostics, exits, dry-run, and digests | F026 |
| IVSD-M027 | Make skill/CLI agent mode no-secret and prohibit `.env` value access | F027 |
| IVSD-M028 | Keep all model SDKs, prompts, providers, inference, and agent loops outside the product | F028 |
| IVSD-M029 | Require human approval for writes, live apply, legal publication, and authority broadening | F029 |
| IVSD-M030 | Publish the skill only after implemented CLI/version compatibility is verified | F030 |

### Rejected Alternatives

1. **Keep export-only UI.** Rejected because portability that requires
   deployment-shell access is incomplete for administrators.
2. **Give tenant administrators whole-instance export.** Rejected because it
   leaks cross-tenant and instance authority.
3. **Use the whole-instance root unchanged for tenant-only transfer.** Rejected
   because a partial root creates authority and deployment ambiguity.
4. **Upload and immediately apply.** Rejected because it hides consequences and
   makes failure recovery harder.
5. **Make merge behavior implicit.** Rejected because users cannot give
   informed approval to unknown overwrite semantics.
6. **Export secret references as “safe.”** Rejected because references can
   disclose topology and still fail on target; secret setup is separate.
7. **Map resources by localized names.** Rejected because names are ambiguous
   and mutable.
8. **Auto-create every missing dependency.** Rejected because providers,
   domains, legal identities, and security policies require target authority.
9. **Allow extensions to embed scripts or SQL.** Rejected because data import
   must not become remote code execution.
10. **Preserve v1alpha1 aliases.** Rejected because development mode permits one
    coherent replacement and the user explicitly rejects compatibility.
11. **Call the package a backup.** Rejected because configuration excludes
    application data, secrets, and operational history.
12. **Automatically delete source state after transfer.** Rejected because
    migration success and source retirement are separate accountable decisions.
13. **Keep legal text as hard-coded UI prose.** Rejected because self-hosters
    and tenants need portable, accountable, localized text.
14. **Export legal links only.** Rejected because migrations remain coupled to
    source origins.
15. **Store arbitrary HTML.** Rejected due execution, tracking, sanitization,
    accessibility, and portability risks.
16. **Auto-publish imported legal text.** Rejected because target identity,
    jurisdiction, effective date, and acceptance need review.
17. **Migrate acceptance history as configuration.** Rejected because it is
    immutable user evidence.
18. **Present templates as legal compliance.** Rejected because templates do
    not replace qualified counsel.
19. **Embed AI in the Setup Assistant.** Rejected because deterministic
    configuration needs no model/provider dependency or secret-bearing
    inference path.
20. **Have agents drive Terminal.Gui screens.** Rejected because machine
    commands are stable, testable, and safer.
21. **Pass secrets through CLI arguments/stdin.** Rejected because shell
    history, process listings, tool capture, and logs can disclose them.
22. **Publish a skill before commands exist.** Rejected because it teaches
    fictional and stale behavior.

## Common Overlooked Failures And Outcomes

Feature type: privileged configuration import/export and cross-instance
migration.

### Common overlooked failures

- preview reads mutate state or drain outbox effects;
- uploaded files persist indefinitely;
- source tenant ID becomes target authority;
- tenant admin preview leaks instance or other-tenant values;
- unsupported sections disappear silently;
- environment-bound URLs, domains, or providers are treated as portable;
- package claims “complete” while omitting major configuration domains;
- replacement resets fields omitted by an older package version;
- reference mapping uses display names;
- extensions import executable content;
- stale preview applies after another administrator changes state;
- post-commit cache failure is mistaken for transaction rollback;
- a rollback deletes audit history;
- legal identity is copied without target confirmation;
- paid policy is mistaken for provider/payment readiness;
- UI requires comparing raw JSON;
- wide diff tables fail on mobile, keyboard, screen reader, or RTL;
- import history stores raw values, secrets, or PII;
- direct transfer creates SSRF or replay paths;
- source configuration is deleted automatically;
- import is described as backup or full data migration;
- operators cannot tell which fields were skipped by locks;
- tenant administrators cannot take their configuration when leaving;
- legal text is attributed to the wrong operator role;
- imported terms rewrite or fabricate acceptance history;
- source-instance legal links survive target migration;
- arbitrary HTML/Markdown tracks users or executes content;
- templates publish unresolved placeholders;
- localized legal documents exceed unreviewed scanner limits;
- template text is marketed as automatic legal compliance;
- agents parse localized terminal prose instead of versioned JSON;
- agents drive stale TUI state and choose the wrong action;
- secrets enter shell history, process arguments, stdout, or skill context;
- an agent publishes legal text or applies configuration without approval;
- embedded AI adds provider keys and hidden data flows;
- skill and CLI versions drift.

### Possible bad outcomes

- tenant lock-in and costly manual migration;
- cross-tenant disclosure;
- privilege escalation;
- silent configuration loss;
- target service outage;
- misleading public or legal identity;
- weakened publication, privacy, security, or payment policy;
- leaked credentials or PII;
- inaccessible administration;
- unrecoverable partial state;
- extension supply-chain abuse;
- operator support overload;
- false confidence in backup/recovery;
- broken self-hosting and EOL promises;
- misleading legal/operator attribution;
- users shown or bound to the wrong policy version;
- inaccessible or unsafe public legal pages;
- cross-jurisdiction misstatement;
- terminal secret disclosure;
- agent-driven destructive/authority-broadening configuration;
- AI-provider lock-in and unnecessary privacy obligations;
- stale skill output that appears valid.

### Positive outcomes if implemented responsibly

- credible self-hosting and exit;
- tenant autonomy without cross-tenant access;
- faster disaster recovery and staging promotion;
- repeatable onboarding and tenant cloning;
- clearer authority and section ownership;
- safer bulk administration;
- improved accessibility and operator comprehension;
- stronger audit and change-management evidence;
- lower support burden through deterministic previews and receipts;
- extensibility without arbitrary code execution;
- transparent omissions and provider setup requirements;
- portable, localized, role-accurate legal documents;
- structured counsel review and version diffs;
- preserved acceptance evidence;
- safe deterministic public rendering;
- deterministic external-agent automation without embedded AI;
- terminal-first access for remote operators;
- auditable human approval around agent-generated drafts.

### Provider questions before implementation

- Which sections are configuration versus application data?
- Which sections may a tenant administrator export and apply?
- Which changes require instance approval or two-person control?
- What does each import mode overwrite, preserve, create, or never delete?
- Which references are stable across instances?
- How are unsupported extensions handled?
- How long are uploads, previews, snapshots, and receipts retained?
- What is the rollback guarantee?
- Which provider/setup tasks remain after import?
- What evidence supports “portable,” “complete,” or “lossless” claims?
- Which legal kinds belong to instance, tenant, or organizer authority?
- Which imports become drafts, scheduled versions, or blockers?
- Which changes require fresh acceptance and notification?
- Which template sources/licenses have legal approval?
- Which commands/JSON schemas are stable enough for the skill?
- Which operations remain human-only?
- Which terminal/assistive environments are supported?

## Stakeholders

| Stakeholder | Interest | Provider responsibility |
|---|---|---|
| Tenant administrators | Move and reuse tenant configuration without instance-shell access | Tenant-scoped package, understandable diff, target-bound authority, rollback |
| Instance administrators | Bulk bootstrap, migration, governance, and recovery | Whole-instance import/export, approvals, audit, provider setup ledger |
| Self-hosters | Credible exit and low-friction deployment migration | Open deterministic formats, docs, CLI/UI parity, no hosted lock-in |
| Community members and attendees | Correct public policy, identity, privacy, and payment safeguards | Fail-closed imports, truthful disclosures, no PII leakage |
| Organizers | Stable event defaults and policy ceilings | Preserve tenant/instance authority and merchant separation |
| Accessibility users | Operable administration under high complexity | Keyboard, screen-reader, reflow, RTL, localization, plain-language alternatives |
| Extension authors | Predictable portable configuration contract | Governed section registry and compatibility tests |
| Operators/support | Diagnosable and recoverable bulk changes | Operation IDs, receipts, safe logs, retry, rollback, runbooks |
| Future users during EOL | Ability to leave with usable configuration | Maintained export/import tools and version policy |
| Instance operator/legal counsel | Accurate platform responsibility and portable texts | instance legal bundle, versioning, review lifecycle |
| Tenant operator/legal counsel | Local legal autonomy without false platform claims | tenant legal bundle, additive composition, target review |
| Terminal-first operator | Full portability over SSH/console | Terminal.Gui TUI and command parity |
| External-agent user | Deterministic help without embedded AI | no-secret machine commands, skill guidance, approval |
| Skill/CLI maintainer | Guidance matches shipped behavior | version contract, examples, schema/link tests |

## I-VSD Principles And Domains

| Principle | Application to this design |
|---|---|
| Amanah / Trust | Bulk admin power is bounded, auditable, reversible, and never secret-bearing. |
| Sidq / Truthfulness | Coverage, omissions, fidelity, target setup, and current implementation status are explicit. |
| Adl / Justice | Tenant administrators can move tenant-owned configuration without receiving cross-tenant authority. |
| Non-Harm | Preview, locks, atomicity, rollback, and target policy prevent foreseeable destructive imports. |
| Rights of People | Portability, correction, exit, and target choice are practical rather than nominal. |
| Avoiding Gharar | Import modes, mappings, omissions, and consequences are known before commitment. |
| Avoiding Spying | Packages and telemetry exclude PII, answers, identities beyond approved public accountability, and operational data. |
| Promise-Keeping | Self-hosting, export, EOL, and migration claims are backed by usable tooling. |
| Ihsan / Excellence | Accessibility, recovery, extensibility, docs, and real migration drills exceed minimal file export. |

Domain coverage:

- **Strategic:** credible portability, self-hosting, EOL, and anti-lock-in.
- **Design:** preview-first UX, explicit modes, accessibility, localization,
  and protective defaults.
- **Technical:** strict contracts, section registry, tenant isolation, mapping,
  locks, atomicity, receipts, rollback, and versioned CLI output.
- **Operational:** staging, retention, support, post-commit recovery, migration
  drills, and provider setup.
- **Governance:** section-aware permissions, enhanced approvals, identity and
  payment escalation, and extension ownership.
- **Evaluation:** fidelity, accessibility, migration outcomes, complaints,
  rollback success, and support burden.

## Validation Gaps

- No import API, BFF route, UI, or test currently exists.
- No tenant-scoped configuration-package contract exists.
- No stakeholder study has tested tenant-admin expectations for merge,
  replacement, mapping, or rollback.
- No real source-to-target migration drill covers the proposed configuration
  categories.
- No approved coverage ledger defines “full configuration.”
- No retention policy exists for import uploads, previews, or snapshots.
- No accessibility evidence exists for a complex import/diff/mapping wizard.
- No direct-transfer threat model exists.
- No extension-section compatibility certification or long-term support policy
  exists.
- No qualified legal review establishes which public identity or domain
  configuration may be moved between jurisdictions.
- No qualified scholarly/legal review approves paid-policy migration wording or
  responsibilities.
- Current Terms and Privacy pages are static and not instance/tenant-owned.
- No typed legal aggregate, kind catalogue, Markdown profile, template library,
  or acceptance boundary exists.
- No legal-template provenance or counsel review exists.
- No analysis proves realistic localized legal content fits proposed limits.
- No Terminal.Gui/CLI project, functional-parity suite, command schema, exit
  contract, or terminal security/accessibility matrix exists.
- No Setup Assistant skill exists; publishing one before the CLI would be
  unsafe.
- No agent approval/provenance tests or architecture gate proves AI remains
  outside the product.

The current evidence supports design reasoning and implementation traceability
only. It does not provide stakeholder or operational validation.

## Escalation Needed

- Security review before any import endpoint, temporary upload storage,
  direct transfer, package signature, or extension section.
- Privacy review for package contents, retention, support artifacts, and
  cross-jurisdiction transfer.
- Qualified legal review for tenant/operator identity, domains, terms,
  privacy URLs, registration configuration, and data-transfer claims.
- Qualified Sunni scholarly and legal review before paid-policy migration is
  described as religiously or legally sufficient.
- Accessibility review and real assistive-technology testing before UI release.
- Operations approval for snapshot retention, rollback promises, EOL support,
  and migration SLOs.
- Revision-bound technical review before implementation of the mapped plan.
- Qualified legal review for document kinds, role authority, templates,
  jurisdiction assumptions, publication, and acceptance rules.
- Security/accessibility review for the Markdown parser, sanitizer, editor, and
  renderer.
- Security/accessibility review for terminal secret entry and TUI behavior.
- Agent-context review before skill publication.

## Evidence Reviewed

| Evidence ID | Locator | What it supports |
|---|---|---|
| E001 | `dev/active/configuration-manifest/configuration-manifest-plan.md` | Implemented v1alpha1 foundation plus mapped v1alpha2 manifest, tenant package, import, legal, recovery, extensibility, and deferred-client boundaries |
| E002 | `dev/active/configuration-manifest/configuration-manifest-context.md` | Implemented state, current phase, authority, blockers, first task, and explicit Setup Assistant deferment |
| E003 | `dev/active/configuration-manifest/configuration-manifest-tasks.md` | Phases 16-23 execution ledger, IVSD-F001-F024 mappings, phase gates, and IVSD-F025-F030 deferment |
| E004 | `src/Explore.Application/Features/ConfigurationManifest/` | Current contract, catalog, validator, compiler, preflight, apply, export, and outbox |
| E005 | `src/Explore.API/Controllers/ConfigurationManifestExportsController.cs` | Instance-authorized export-only API |
| E006 | `src/Explore.Blazor/Extensions/BffConfigurationManifestEndpoints.cs` and `src/Explore.Blazor.Client/Pages/Admin/Instance/Components/ConfigurationManifestExportSection.razor` | Fixed same-origin export BFF and instance export UI; no import |
| E007 | `src/Explore.Domain/Settings/SettingRegistry.cs` | Broad settings categories that require explicit portability classification |
| E008 | `src/Explore.Domain/Settings/Documents/SettingsDocumentKeys.cs` | Current typed tenant document families |
| E009 | `docs/EXTENSIBILITY.md` and `docs/CUSTOM_PROPERTIES.md` | Module, template, custom-property, projection, and extension boundaries |
| E010 | `docs/FOOTER_MANAGEMENT.md` and `docs/ADMIN_GUIDE.md` | Tenant/instance administration, footer, templates, navigation, and authority |
| E011 | `docs/SECRETS.md`, `docs/SECURITY-MODEL.md`, and `docs/CONFIGURATION_MANIFEST.md` | Secret, BFF, trust, and current manifest boundaries |
| E012 | `islamic-value-sensitive-design/consultations/i-vsd-paid-event-payments-consultation.md` | Paid-policy, provider, liability, refund, and organizer authority separation |
| E013 | `docs/LOCALIZATION.md` | Localization governance and static bundle import/export precedent |
| E014 | `docs/ACCESSIBILITY.md` | WCAG 2.2 AA, focus, announcements, reflow, and RTL requirements |
| E015 | `src/Explore.Blazor.Client/Pages/Legal/TermsOfService.razor` and `PrivacyPolicy.razor` | Current static legal-text implementation |
| E016 | `docs/FOOTER_MANAGEMENT.md` | Existing role-separated instance/tenant legal links |
| E017 | `islamic-value-sensitive-design/governance/i-vsd-branding-legal-identity-authority.md` | Legal identity and no-substitution boundaries |
| E018 | `islamic-value-sensitive-design/consultations/i-vsd-setup-assistant-security-and-portability.md` | Legal templates, Markdown editor, FOSS licensing, CLI/TUI, agent skill, and cross-platform constraints |
| E019 | [Terminal.Gui documentation](https://tui-cs.github.io/Terminal.Gui/index.html) and [NuGet metadata](https://api.nuget.org/v3/catalog0/data/2026.07.07.12.25.25/terminal.gui.2.4.17.json) | Cross-platform TUI capabilities, MIT package metadata, and transitive inventory |
| E020 | `eng/release/src/ISLAMU.ReleaseEngineering/Program.cs`, schema-generator `Program.cs`, `.agents/skills/_SKILL_SCHEMA.md`, and skill-authoring workflow | Existing CLI and skill contracts |
| E021 | `docs/legal/IP_GOVERNANCE.md`, `docs/DUAL_VERSIONING.md`, and `legal/CLA.md` | FOSS/commercial, reciprocal, and alternative-outbound boundaries |

Reviewed input revision is the deterministic combined SHA-256 of E001-E003:
`configuration-manifest-plan.md`, `configuration-manifest-context.md`, and
`configuration-manifest-tasks.md`, archived in sorted path order with normalized
metadata. The remaining repository evidence supports the report but is not part
of that revision-binding digest. User direction requires whole-instance UI
import, tenant-admin import/export, cross-instance migration, broad
extensibility/customization/QoL, no backward compatibility, and portable
instance/tenant legal texts. Terminal.Gui, CLI/TUI, `.env`, Setup Assistant,
and agent-skill delivery are deliberately deferred until this plan's Definition
of Done is proven.

No external product source, code, schema, tests, migrations, prose, or assets
were used for this update.

## Missing Evidence

- user interviews with tenant and instance administrators;
- migration task-completion and error-comprehension studies;
- production support and incident data;
- real package retention and storage threat model;
- version-support and EOL policy approved by maintainers;
- target-provider compatibility matrix;
- direct-transfer protocol design and security review;
- empirical accessibility and RTL results;
- legal and scholarly review described above;
- implementation evidence for mapped IVSD-F001 through IVSD-F018 tasks;
- legal document kind/authority matrix;
- legal Markdown schema and sanitizer;
- template provenance and counsel review;
- publication/acceptance migration semantics;
- localized legal-content size and usability evidence;
- implementation evidence for mapped IVSD-F019 through IVSD-F024 tasks;
- CLI command/JSON/exit contract and parity matrix;
- terminal secret/accessibility threat model;
- skill routing, version range, resources, and executable examples;
- a future Setup Assistant plan mapping IVSD-F025 through IVSD-F030 after the
  ConfigurationManifest Definition of Done is proven.

## Context Inventory

Reviewed:

- current configuration-manifest plan, context, and tasks;
- current I-VSD configuration report;
- current manifest contract, validator, compiler, preflight, apply, startup,
  export API/BFF/UI, persistence, outbox, schema, and tests;
- settings registry and typed document taxonomy;
- admin, footer, localization, custom-property, extensibility, security,
  secrets, accessibility, and configuration documentation;
- adjacent paid-event and legal-identity I-VSD reports;
- current static Terms/Privacy pages, footer legal links, and Setup Assistant
  I-VSD report;
- current repository CLI and skill-authoring conventions plus Terminal.Gui
  functional/package metadata.

Not reviewed:

- raw production logs or support cases;
- stakeholder interviews;
- external commercial migration products;
- deployment-specific secrets or configuration values;
- legal contracts or jurisdiction-specific transfer assessments.

## Implementation And Planning Impact

The active workstream is aligned for revision-bound technical review:

1. Phase 16 maps artifact authority, v1alpha2 contracts, section coverage,
   exclusions, and non-executable extensibility.
2. Phase 17 maps typed role-scoped legal documents, immutable evidence
   separation, target review, templates, and constrained Markdown.
3. Phases 18-19 map preview-first sessions, semantic diff, mappings,
   approvals, atomic selected apply, protected snapshots, receipts, and
   forward rollback.
4. Phase 20 maps tenant-authorized packages, target-bound import, clone,
   cross-instance migration, omissions, fidelity, and source independence.
5. Phase 21 maps HAL-gated instance/tenant administration, accessibility,
   localization, RTL, and capability-loss recovery.
6. Phase 22 maps declarative extensions, signatures, managed ownership,
   GitOps drift, approval separation, and opt-in secure direct transfer.
7. Phase 23 maps generated artifacts, truthful coverage, operator/legal
   documentation, criticality evidence, I-VSD reconciliation, and release.
8. IVSD-F025 through IVSD-F030 are mapped to explicit non-applicability in
   this workstream and deferred to the later Setup Assistant plan.

This report does not prescribe phase order. The implementation plan owns
architecture sequencing and tasks; this review confirms that each accepted
provider-responsibility finding has a mapped task, explicit deferment, or named
release escalation.

## Planning Handoff

- Workstream: configuration-manifest
- Status: plan-aligned / active
- Reviewed input revision:
  `sha256:b1bb05932eef7c11ec0af43b307d4afdb4eac17ac3b8d563f095cbe16c99f26d`
- Findings and mitigations: IVSD-F001 through IVSD-F030 retain their matching
  IVSD-M001 through IVSD-M030 identifiers.
- Required plan mappings: IVSD-F001-F024 map to Phases 16-23 and named CM
  tasks; IVSD-F025-F030 map to explicit non-applicability and the future Setup
  Assistant workstream.
- Escalations required before: revision-bound technical approval before
  implementation; security/privacy/accessibility/legal/scholarly/operations
  gates before the affected behavior is released.
- Refresh triggers: artifact authority, import modes, legal ownership/evidence,
  deletion/managed ownership, secret/PII/payment scope, direct-transfer trust,
  application-data migration, or any mapped mitigation/task materially changes.
- Continuation disposition: the later user instruction supersedes the archived
  wording. Configuration Manifest remains active until its unwaived Phase
  18-23 gates and Definition of Done pass. The future Setup Assistant consumes
  only the frozen v1alpha2 wire/extraction/no-secret/Markdown contract and is
  not implemented by this workstream.

## Review Lifecycle

| Date | Previous status | New status | Trigger | Evidence/replacement |
|---|---|---|---|---|
| 2026-08-26 | none | current / plan-aligned | Instance-wide manifest rebase and export-only architecture | Superseded report revision |
| 2026-08-29 | current / plan-aligned | stale | User required UI import, tenant-scoped portability, cross-instance migration, extensibility, and no compatibility | User direction plus E001-E014 |
| 2026-08-29 | stale | current / changes-required | Full I-VSD re-evaluation and expanded recommendation | This revision, reviewed input `sha256:12aff969f585109dc6ed374ebaad8f1a9efec12a0c7c176a58508a255235df2b` |
| 2026-08-29 | current / changes-required | current / changes-required | User added instance/tenant legal texts, templates, Markdown editing, and broader legal QoL | This revision, reviewed input `sha256:b247ad694150d750cfbd1f63d4090c2bfdd74ad988298b7d75dff703a9e51ceb` |
| 2026-08-29 | current / changes-required | current / changes-required | User added Terminal.Gui CLI/TUI parity, compatible-FOSS policy, external-agent skill, and no embedded AI | This revision, reviewed input `sha256:21ae0c2feee79a79a7c2e724dfb909a6d24456d75df4c238bf51a0f52a6c8ea7` |
| 2026-08-30 | current / changes-required | current / plan-aligned | Active triad re-baselined with Phases 16-23, exact IVSD-F001-F024 task mappings, and explicit IVSD-F025-F030 deferment | Reviewed input `sha256:b1bb05932eef7c11ec0af43b307d4afdb4eac17ac3b8d563f095cbe16c99f26d` |
| 2026-08-30 | current / plan-aligned | superseded / closed-by-user-directive | User declared the workstream completely finished for archival and directed Setup Assistant planning to pin the frozen implemented baseline | Archived triad plus successor Setup plan |
| 2026-08-30 | superseded / closed-by-user-directive | current / plan-aligned | Latest user instruction reactivated continuation from Phase 18 verification through CM-2330 | Active triad, v1alpha2 implementation, and deferred final gates |

Refresh this report when:

- artifact families, import modes, deletion/ownership semantics, or section
  coverage changes;
- secrets, PII, identity, payments, direct transfer, extensions, or
  application-data migration enter scope;
- the implementation plan is re-baselined;
- stakeholder, accessibility, security, legal, scholarly, or operational
  evidence becomes available;
- implementation materially changes any IVSD-F001 through IVSD-F030 mitigation;
- legal kinds, templates, Markdown profile, publication, acceptance, or
  responsible-party composition changes;
- CLI commands, JSON schema, TUI behavior, skill guidance, agent approval, or
  no-embedded-AI boundary changes.
