<!-- ABOUTME: I-VSD planning report for tenant branding, operator attribution, and legal-identity authority. -->
<!-- ABOUTME: Binds branding convergence decisions to truthful disclosure, tenant autonomy, and paid-event safeguards. -->

# I-VSD: Branding And Legal-Identity Authority

Last Updated: 2026-08-28 Europe/Brussels

## Report Metadata

- **Report identity:** `branding-legal-identity-authority`
- **Mode:** planning
- **State:** current
- **Disposition:** plan-aligned
- **Evidence cutoff:** 2026-08-28
- **Reviewed input revision:** `sha256:2364e821f8455789cc00fe1c5f6c134c07b57e1db861a1ac6aaea607db2bfcb5`
- **Reviewed plan artifact revision:** `sha256:fbf862fe9270ca2420a3c013e982d4bfd7447c1489bf32f8e08fb4d74e6ec84e`
- **Planned workstream:** `dev/active/branding-legal-identity-authority/`
- **Planned artifacts:**
  - `dev/active/branding-legal-identity-authority/branding-legal-identity-authority-plan.md`
  - `dev/active/branding-legal-identity-authority/branding-legal-identity-authority-context.md`
  - `dev/active/branding-legal-identity-authority/branding-legal-identity-authority-tasks.md`

## Scope And Question

This report evaluates how ISLAMU Event should distinguish and present instance,
tenant, and organizer identities across single-tenant and multi-tenant
deployments. It covers tenant-branding completeness, single-to-multi transition
readiness, white-label governance, public/footer attribution, and paid-event
disclosures. It does not issue legal or religious rulings.

The user resolved the planning question in favor of the full scope: introduce a
first-class tenant legal/directory-operator identity that is independent of
cosmetic branding, preserve separate instance and organizer roles, and remove
old compatibility/fallback paths.

## Context And Current Evidence

- Tenant creation atomically creates a canonical `tenant.branding` typed
  settings document seeded from `Tenant.FullName`.
- Single-tenant onboarding seeds the default tenant branding document from the
  instance site name, and later single-tenant instance-name updates synchronize
  that tenant display name.
- `TypedSettingsDocumentResolver` returns tenant-owned documents without scalar
  or instance fallback. The typed query tests explicitly preserve this
  no-scalar-fallback contract.
- The public experience returns an empty brand when the branding document is
  absent, while `PaidEventDisclaimerFormatter` substitutes the literal
  `ISLAMU`. This differs from the documented instance-brand fallback.
- Instance branding locks govern tenant editability; they are not a substitute
  for a persisted tenant branding invariant.
- Paid checkout already distinguishes tenant directory disclosure from the
  instance operator disclosure in `IPaidCheckoutGovernance` and from the
  organizer merchant/provider disclosure.
- The current tenant disclaimer derives the named directory identity from
  `BrandingSettings.DisplayName`, which is a presentation value rather than an
  explicitly governed legal or directory-operator identity.
- `dev/active/configuration-manifest/` owns adjacent typed-document and
  instance/tenant authority work. `dev/pause/tenant-onboarding-enterprise/` is a
  stale, broader workstream and is not a safe plan authority for this focused
  change.

## Stakeholders And Authority Boundaries

| Stakeholder | Legitimate authority | Material risk |
|---|---|---|
| Attendee or visitor | Understand who operates the directory, platform, and paid event | Misattribution can obscure complaint, privacy, refund, or contractual responsibility |
| Tenant administrator | Configure the tenant's public presentation within instance governance | Silent instance fallback can erase tenant identity or hide incomplete onboarding |
| Instance operator | Govern the deployment, global surfaces, and platform/operator disclosures | White-labeling can hide material operator responsibility if treated as purely cosmetic |
| Organizer or merchant | Own event delivery and direct-charge commerce responsibilities | Tenant or instance branding must not imply that either party is the merchant |
| Self-hoster | Operate an instance under its own identity and policies | Hosted-instance assumptions must not be hard-coded into self-hosted defaults |
| Maintainer | Preserve one authoritative settings and disclosure model | Dual scalar/document semantics can drift and produce contradictory public output |

Provider-controlled decisions include provisioning invariants, fallback order,
white-label locks, whether paid activation fails closed, mandatory operator
attribution, disclosure labels, and the data model separating presentation from
legal identity.

## Principles And Values

- **Amanah (trust and responsibility):** every responsible party must be named
  according to its real role rather than whichever display string happens to be
  available.
- **Sidq (truthfulness):** public and checkout disclosures must not silently
  substitute the instance for a missing tenant or conflate a brand with a legal
  actor.
- **Adl (justice):** complaint, refund, dispute, privacy, and service-delivery
  responsibilities must remain attributable to the party that controls them.
- **Tenant autonomy with governance:** tenants may control their presentation
  where delegated, while instance operators retain explicit governance and
  operator-disclosure duties.
- **Risk prevention:** paid capability remains fail-closed when required
  operator, merchant, policy, or role-identity evidence is incomplete.
- **Self-hosting dignity:** the architecture must work when instance and tenant
  roles are held by one organization without collapsing their data scopes.

## Findings And Tradeoffs

### IVSD-BLIA-001 — Directory-operator identity must exist before activation

Treating instance fallback as an acceptable tenant state would conceal
provisioning failures and weaken truthful tenant attribution. Every tenant,
including the single-tenant default, must own a capability-valid
directory-operator identity before it becomes Active. Cosmetic branding cannot
satisfy this requirement.

### IVSD-BLIA-002 — Missing legal identity must fail closed without substitution

Cosmetic rendering may have presentation defaults, but a legal/operator
disclosure has no truthful fallback. Missing, malformed, or unsupported tenant
identity must make the affected public or paid capability observably
unavailable while preserving enough bounded reason-code context for repair.

### IVSD-BLIA-003 — Cosmetic branding is insufficient legal authority

Using `BrandingSettings.DisplayName` as the directory actor is convenient but
does not prove the brand is the tenant's legal or accountable operator name.
This can become materially misleading in white-label deployments and in paid
flows. A first-class role identity gives the strongest long-term model but adds
schema, API, UI, migration-generation, and governance scope.

### IVSD-BLIA-004 — Instance attribution is role-based, not universal co-branding

The instance operator should be disclosed on tenant surfaces wherever it owns
platform, privacy, security, complaint, or payment-infrastructure duties.
Requiring the instance logo or brand in every tenant presentation is a separate
white-label product decision and is not itself a substitute for clear operator
terms, privacy, and legal links.

### IVSD-BLIA-005 — Paid disclosure must preserve three-party separation

Organizer/merchant, tenant/directory operator, and instance/platform operator
must remain independent facts. Missing tenant identity must not cause the
instance to be presented as the merchant, and no fallback may redirect
commercial responsibility or payment authority away from OrganizerDirect.

### IVSD-BLIA-006 — Development mode permits a clean contract cut

The user explicitly rejects backward-compatibility work. The plan may delete
legacy scalar tenant-branding read/write paths once every authoritative caller
uses typed documents, while preserving persisted-data correctness through
generated migrations or an explicit development reset strategy selected under
repository migration policy.

## Mitigations And Safeguards

| Mitigation ID | Mitigation | Findings |
|---|---|---|
| IVSD-BLIA-M001 | Make capability-valid tenant directory-operator identity an explicit creation, activation, and onboarding readiness invariant | 001, 002 |
| IVSD-BLIA-M002 | Forbid legal-identity substitution; return one non-cacheable public unavailability contract, bounded PII-free telemetry, and an operator repair path | 001, 002 |
| IVSD-BLIA-M003 | Preserve explicit instance, tenant, and organizer disclosure slots rather than constructing one blended brand sentence | 003, 004, 005 |
| IVSD-BLIA-M004 | Require operator legal links on materially operator-controlled surfaces while leaving cosmetic co-branding to explicit white-label policy | 004 |
| IVSD-BLIA-M005 | Fail paid publication, checkout composition, and acceptance closed when tenant, instance, organizer, provider, or policy authority is incomplete | 003, 005 |
| IVSD-BLIA-M006 | Remove legacy branding compatibility paths in one repository-native cut, regenerate affected contracts/migrations, and forbid dual writes | 002, 006 |
| IVSD-BLIA-M007 | Test single-tenant, transition-ready, multi-tenant, locked, corrupted/missing-document, self-hosted, and paid-event scenarios before production code | 001-006 |

## Plan And Task Mapping

| Finding / mitigation | Plan mapping | Task mapping |
|---|---|---|
| IVSD-BLIA-001 / M001 | BLIA-R2, BLIA-R3; D3-D5; Phases 1-2 | 1.1-1.2, 2.1-2.4 |
| IVSD-BLIA-002 / M002 | BLIA-R3, BLIA-R9; D3, D5; Phases 2 and 4 | 2.3-2.4, 4.1-4.2, 6.3 |
| IVSD-BLIA-003 / M003 | BLIA-R1, BLIA-R5, BLIA-R6; D1-D3, D7-D9 | 1.1-1.2, 3.1-3.5, 4.1-4.4, 5.1-5.4 |
| IVSD-BLIA-004 / M004 | BLIA-R5, BLIA-R7; D6-D7; Phases 1, 4, and 5 | 1.3-1.4, 4.1-4.2, 5.3-5.4 |
| IVSD-BLIA-005 / M005 | BLIA-R3, BLIA-R6; D5, D8; Phase 4 | 2.3-2.4, 4.3-4.5, 6.7-6.8 |
| IVSD-BLIA-006 / M006 | BLIA-R8; D7, D10; Phases 3, 4, and 6 | 3.3-3.5, 4.5, 6.1-6.4 |
| IVSD-BLIA-M007 | BLIA-R1 through BLIA-R9; plan Section 7 | Every Red task, V1-V6, 5.5, 6.7, and 6.8 |

## Uncertainty And Confidence

- **High confidence:** tenant branding already has a typed-document authority
  and is created with the tenant; the current public/disclaimer fallback paths
  are inconsistent with each other and with documentation.
- **High confidence:** instance operator, tenant directory, and organizer
  merchant responsibilities must remain distinct in the existing
  OrganizerDirect architecture.
- **Medium confidence:** the correct storage shape for a general tenant legal
  identity. No current repository artifact establishes whether it belongs in a
  dedicated typed settings document, an aggregate, or a narrower
  directory-operator disclosure contract.
- **Not determined here:** jurisdiction-specific legal wording, whether a
  tenant is a data controller or processor in a particular deployment, and
  whether specific branding placement is legally mandatory.

Refresh this report when the scope decision changes, the configuration-manifest
authority model changes, or qualified legal/scholarly review changes a role or
disclosure requirement.

## Escalation Needed

- Qualified legal review must approve final operator, tenant, merchant,
  privacy-controller, complaint, and contractual-role wording.
- Qualified scholarly review is required for any claim that a specific
  disclosure format or commerce relationship is religiously required or
  prohibited.
- The user selected first-class tenant legal/directory-operator identity on
  2026-08-28. No planning-scope escalation remains.

## Implementation Evidence Reconciliation (2026-08-29)

Responsibility separation, the value this report treats as central, is
implemented as designed. Cosmetic branding, tenant directory-operator identity,
instance operator identity, and organizer merchant identity are four distinct
contracts, and no runtime path substitutes one for another. The literal
`ISLAMU` prose fallback that this report flagged as a truthfulness defect is
removed rather than replaced.

Truthful attribution now survives time as well as presentation. Paid acceptance
pins structured tenant identity values together with the source document ID and
revision, so a later identity edit cannot rewrite what a buyer actually
accepted. White-labeling continues to affect visuals without concealing the
instance operator's role in the public footer.

Fail-closed handling matches the reviewed design: incomplete or corrupt tenant
identity blocks activation without any state or audit write, and public
settings and shell both return non-cacheable RFC 7807 `503` with
`tenant_identity_unavailable`. Operator telemetry carries closed reason codes
and approved identifiers only, never identity payloads.

No religious or legal conclusion in this report changes. The open question about
jurisdiction-specific fields and wording still belongs to qualified counsel, and
engineering has not resolved it.

## Next Steps

1. Complete the outstanding closure gates: restore the merged `20260828*_Init`
   migration catalogs, generate the additive migrations, then run the
   persistence, mutation, and MAD gates.
2. Obtain the manual visual verdict once a real browser surface is available.
3. Refresh this report if a material architecture, provider-responsibility, or
   qualified-authority decision changes.
4. Confirm this reconciliation at final closure.

## Lifecycle

- **Current state:** current
- **Current disposition:** plan-aligned
- **Reviewed artifacts:** repository evidence packet and the triad revision
  `sha256:fbf862fe9270ca2420a3c013e982d4bfd7447c1489bf32f8e08fb4d74e6ec84e`
- **Supersedes:** none
- **Superseded by:** none
