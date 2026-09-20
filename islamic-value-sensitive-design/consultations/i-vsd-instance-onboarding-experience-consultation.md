<!-- ABOUTME: I-VSD evaluation of the instance onboarding user journey, ergonomics, and post-launch transition. -->
<!-- ABOUTME: Evaluates provider responsibility, cognitive ease (Taysir), operational stewardship (Amanah), and communal dignity. -->

# I-VSD: Instance Onboarding Experience — Holistic Journey, Operational Stewardship, and Communal Dignity

Last Updated: 2026-09-13 Europe/Brussels

## Review Metadata

- Mode: standalone
- Subject: `instance-onboarding-experience`
- Workstream: none
- Report kind: consultancy-report
- Report status: current
- Disposition: ready-for-planning
- Evidence cutoff: 2026-09-13
- Reviewed input revision: `6013832fd0cbebd36701c31fe76c44ab284722fc`
- Supersedes: none

---

## Scope

This report evaluates the **experiential, cognitive, ethical, and operational architecture** of the first-run instance onboarding flow in the ISLAMU Event platform. 

While existing technical and workstream reviews address backend fail-closed security invariants, startup dependency decoupling, and raw database persistence of legal operator identity, this consultation addresses the **lived human journey of the operator** bringing a self-hosted instance into the world. Specifically, it reviews:

1. **Cognitive load and barrier to entry**: Evaluating whether front-loading deep legal classification, registration numbers, and external URL mandates creates an unjust hurdle (*Haraj*) for grassroots community leaders, educators, and volunteers.
2. **Progressive disclosure vs. administrative exhaustiveness**: Designing a proportional "60-Second Fast Track" paired with progressive expanders for deep infrastructure and legal customization.
3. **Auto-generated sovereign legal scaffolding**: Providing ethically sound, standard privacy-first notice and operator attribution templates directly within the platform, eliminating the blocker of pre-drafted external legal websites.
4. **Active operational diagnostics ("Test Flight")**: Moving from passive, static preflight checkmarks to interactive, live test actions (e.g., SMTP dispatch ping, storage read/write validation, cache latency probes) that protect organizers from launching broken platforms (*Dar' al-Mafasid*).
5. **Bridging the Day-0 to Day-1 chasm**: Designing the post-launch "Launchpad" and an ephemeral 1-click "Community Sample Pack" to seed realistic event workflows (Halaqa, Iftar, RSVP, check-in) with an explicit 1-click clean purge guarantee.
6. **Workspace visual hierarchy and layout discipline**: Affirming that visual branding, site profile, and identity configuration belong squarely within the main-column form flow, while the right-hand sidebar rail is reserved strictly for functional task navigation (top) and contextual documentation/guidance links (bottom).
7. **Islamic Value-Sensitive Design as an experiential anchor**: Prominently enshrining the Sovereign Privacy Pledge (zero trackers, zero ad networks, zero behavioral profiling) and automated prayer-time and Hijri calendar scheduling awareness on Day 0.
8. **Dignity of technical stewards**: Ensuring full feature and diagnostic parity for sysadmins and DevOps operators via the terminal TUI (`Event.SetupAssistant.Terminal`) and headless CLI preflight tools.

---

## Claim Boundary

This document provides software design reasoning, architectural analysis, and provider-responsibility evaluation grounded in Islamic Value-Sensitive Design (I-VSD). It does **not** issue Sharia rulings, fatwas, or declarations of halal/haram/makrooh/wajib. Any religious-legal questions regarding specific commercial contracts, disputed prayer calculation methods, or formal legal entity liability in particular jurisdictions are explicitly marked for escalation to qualified scholarly and legal authorities under [Escalation Needed](#escalation-needed).

---

## Findings

| Finding ID | Title | Severity | Status | Principle / Domain | Decision / Rule | Linked Mitigation |
|---|---|---|---|---|---|---|
| **IVSD-F001** | **Premature Administrative Burden**: Front-loading deep legal entity fields induces cognitive fatigue and blocks grassroots adoption | High | open | Taysir & Raf' al-Haraj / User Resource Stewardship | `InstanceOnboarding.razor` requires 9 mandatory legal and URL fields before instance launch | **IVSD-M001** |
| **IVSD-F002** | **Legal Prerequisite Vacuum**: Mandatory external policy URLs penalize small communities lacking established web infrastructure | High | open | Amanah / Sidq / Equity | Wizard halts if external `LegalNoticeUrl` and `PrivacyUrl` are not pre-existing | **IVSD-M002** |
| **IVSD-F003** | **Passive Preflight Blindness**: Static green checks fail to detect runtime delivery failures in mission-critical services (mail, storage) | High | open | Amanah (Custodianship) / Dar' al-Mafasid (Harm Prevention) | Preflight checks inspect static configuration presence rather than executing live end-to-end verification | **IVSD-M003** |
| **IVSD-F004** | **The Day-0 to Day-1 Chasm**: Abrupt post-launch redirect to an empty catalog creates desolation and disorientation | Medium | open | Ta'awun (Mutual Assistance) / Community Empowerment | Post-launch flow drops operator onto an empty `/events` or `/settings` page with zero momentum | **IVSD-M004** |
| **IVSD-F005** | **Layout Pollution**: Placing live mockups or visual branding in the side rail displaces critical task navigation and help docs | Medium | open | Bayan (Clarity) / Ergonomic Dignity | Attempting to crowd the workspace sidebar rail with secondary preview cards | **IVSD-M005** |
| **IVSD-F006** | **Cultural & Spiritual Context Alienation**: Generic temporal defaults ignore prayer schedules and Hijri calendar foundations | Low | open | Islamic Identity / Temporal Adl (Right Timing) | Onboarding omits timezone-based prayer calculation options and Hijri date defaults | **IVSD-M006** |
| **IVSD-F007** | **Unvoiced Sovereign Privacy**: Failing to articulate the zero-tracking pledge on Day 0 leaves user trust vulnerable to Big Tech skepticism | Medium | open | Hifz al-Khasusiyyah (Privacy) / Amanah | Onboarding treats privacy solely as a legal checkbox rather than a moral differentiator | **IVSD-M007** |
| **IVSD-F008** | **Sysadmin Role Degradation**: Restricting rich onboarding ergonomics to the browser alienates headless GitOps and terminal stewards | Medium | open | Adl (Fairness) / Respect for Craft & Roles | Preflight diagnostics and readiness evaluation lack headless CLI and TUI execution parity | **IVSD-M008** |

---

### Detailed Findings

#### IVSD-F001 — Premature Administrative Burden: Front-loading deep legal entity fields induces cognitive fatigue and blocks grassroots adoption
- **Context**: In [`InstanceOnboarding.razor`](file:///home/amir/ISLAMU/Github/Event/src/Explore.Blazor.Client/Pages/Onboarding/InstanceOnboarding.razor), the wizard immediately presents fields for `LegalName`, `OperatorKindCode`, `JurisdictionCountryCode`, `RegistrationIdentifier`, `PublicContactEmail`, `LegalNoticeUrl`, and `PrivacyUrl`.
- **Moral Risk**: The prophetic paradigm is characterized by *Taysir* (facilitation and ease): *"Make things easy and do not make them difficult; give glad tidings and do not repel"* (Sahih al-Bukhari). When an educational circle, local musalla, or youth club deploys the platform, confronting them with complex statutory classification codes and legal notice requirements on their first encounter creates unwarranted hardship (*Haraj*). Many volunteers abandon the setup, returning to centralized surveillance platforms (WhatsApp, Eventbrite) that collect and monetize their congregation's data.
- **Provider Controlled Decision**: The sequence and progressive disclosure of onboarding fields in [`InstanceOnboarding.razor`](file:///home/amir/ISLAMU/Github/Event/src/Explore.Blazor.Client/Pages/Onboarding/InstanceOnboarding.razor) and the validation rules in [`CompleteInstanceOnboardingRequestValidator.cs`](file:///home/amir/ISLAMU/Github/Event/src/Explore.Application/DTOs/Onboarding/Validators/CompleteInstanceOnboardingRequestValidator.cs).

#### IVSD-F002 — Legal Prerequisite Vacuum: Mandatory external policy URLs penalize small communities lacking established web infrastructure
- **Context**: The onboarding contract enforces non-empty, valid HTTPS URLs for `LegalNoticeUrl` and `PrivacyUrl`.
- **Moral Risk**: This requirement presumes that the community already hosts an independent corporate website with formal legal counsel. For emerging communities, this leads to two immoral outcomes: either the operator enters false/fictitious URLs (violating *Sidq* - truthfulness) merely to bypass the validator, or they are locked out of launching their platform.
- **Provider Controlled Decision**: The mechanism of legal document fulfillment in the domain and application layers (whether URLs must point externally or can be fulfilled by internal platform-hosted documents).

#### IVSD-F003 — Passive Preflight Blindness: Static green checks fail to detect runtime delivery failures in mission-critical services
- **Context**: Preflight launch checks in [`GetOnboardingPreflightQueryHandler.cs`](file:///home/amir/ISLAMU/Github/Event/src/Explore.Application/Features/InstanceOnboarding/Handlers/Queries/GetOnboardingPreflightQueryHandler.cs) report connectivity based on configuration presence or basic ping checks.
- **Moral Risk**: In self-hosted community software, outbound SMTP is the most common point of failure (blocked port 25, invalid TLS negotiation, SPF/DKIM misconfigurations). If the platform launches with a passive green checkmark, the operator believes the system is healthy. When community members subsequently register for an event, confirmation emails fail silently, tickets are lost, and communal trust is damaged. Under the principle of *Dar' al-Mafasid* (prevention of harm) and *Amanah* (custodianship), a steward must proactively verify capabilities before assuming responsibility for attendee communication.
- **Provider Controlled Decision**: The absence of interactive diagnostic commands in the onboarding API and UI.

#### IVSD-F004 — The Day-0 to Day-1 Chasm: Abrupt post-launch redirect to an empty catalog creates desolation and disorientation
- **Context**: Upon clicking "Complete and Launch", the wizard terminates and redirects to `/events` or `/settings/instance`.
- **Moral Risk**: Creating community software is an act of *Ta'awun* (mutual cooperation in goodness and piety). Dropping a volunteer into a cold, empty list creates cognitive disorientation: *What do I do now? How does ticket scanning work? What does an event page actually look like?* Without guidance, the momentum of launching the platform is extinguished, leading to shelfware.
- **Provider Controlled Decision**: The post-launch routing and onboarding completion experience in [`InstanceOnboarding.razor`](file:///home/amir/ISLAMU/Github/Event/src/Explore.Blazor.Client/Pages/Onboarding/InstanceOnboarding.razor).

#### IVSD-F005 — Layout Pollution: Placing live mockups or visual branding in the side rail displaces critical task navigation and help docs
- **Context**: Suggestions to place real-time visual mockups, social card previews, or branding controls into the right-hand sidebar rail of the onboarding workspace.
- **Moral Risk**: The right-hand column in [`OnboardingWorkspace.razor`](file:///home/amir/ISLAMU/Github/Event/src/Explore.Blazor.Client/Pages/Onboarding/Components/OnboardingWorkspace.razor) serves a specific ergonomic function: anchoring the operator's mental model of setup progress (top: task steps and status links) and providing dependable contextual aid (bottom: documentation links, troubleshooting runbooks). Crowding this rail with dynamic rendering cards or cosmetic previews introduces visual noise, degrades accessibility, and obscures essential navigation and guidance. Visual branding is an intrinsic part of the core site profile and belongs in the primary narrative flow.
- **Provider Controlled Decision**: Layout composition and slot usage between `ChildContent`, `SummaryContent`, `HelpContent`, and `ActionsContent` in [`OnboardingWorkspace.razor`](file:///home/amir/ISLAMU/Github/Event/src/Explore.Blazor.Client/Pages/Onboarding/Components/OnboardingWorkspace.razor).

#### IVSD-F006 — Cultural & Spiritual Context Alienation: Generic temporal defaults ignore prayer schedules and Hijri calendar foundations
- **Context**: Instance setup collects standard timezone configuration but does not prompt for Islamic astronomical/prayer calculation preferences or Hijri calendar visibility.
- **Moral Risk**: Islamic community events revolve intrinsically around the five daily prayers (Salat) and sacred months. An event platform that operates in total detachment from prayer times risks scheduling youth talks or dinners directly across congregational prayers. Offering prayer awareness during onboarding honors the sacred rhythm of community life (*Hifz ad-Din* and *Adl* in temporal stewardship).
- **Provider Controlled Decision**: System configuration options captured during initial setup or initialized as instance defaults.

#### IVSD-F007 — Unvoiced Sovereign Privacy: Failing to articulate the zero-tracking pledge on Day 0 leaves user trust vulnerable
- **Context**: Privacy in onboarding is currently treated as an operational checkbox (`_acknowledgePublicExposure` and a link to a privacy policy).
- **Moral Risk**: Modern internet users have been traumatized by extractive platforms that monitor congregational attendance, sell demographic data to data brokers, and inject behavioral ads. A self-hosted Islamic platform must declare its moral stance explicitly. Silence on data sovereignty allows suspicion to linger (*Zann*), whereas transparent, unequivocal declaration of zero-tracking builds profound communal peace of mind (*Sakina* and *Amanah*).
- **Provider Controlled Decision**: The pedagogical framing and messaging presented to the operator during onboarding.

#### IVSD-F008 — Sysadmin Role Degradation: Restricting rich onboarding ergonomics to the browser alienates headless GitOps and terminal stewards
- **Context**: The web UI in [`InstanceOnboarding.razor`](file:///home/amir/ISLAMU/Github/Event/src/Explore.Blazor.Client/Pages/Onboarding/InstanceOnboarding.razor) receives all user experience enhancements, while CLI/TUI interfaces in [`Event.SetupAssistant.Terminal`](file:///home/amir/ISLAMU/Github/Event/src/Event.SetupAssistant.Terminal) remain secondary or basic.
- **Moral Risk**: Systems administrators and DevOps engineers are essential stewards (*Umana'*) of community infrastructure. Forcing a DevOps engineer deploying across Kubernetes, Ansible, or remote headless SSH to open a browser window and manually click buttons disrespects their craft (*Ihsan* in tooling) and introduces deployment inconsistencies across clusters.
- **Provider Controlled Decision**: Parity of validation rules, diagnostics, and setup workflows across CLI, TUI, and Web targets.

---

## Recommendations

### IVSD-M001 — Implement Progressive Disclosure: The "60-Second Fast Track" vs. Advanced Depth
- **Action**: Restructure the instance onboarding wizard into a tiered progressive flow:
  1. **Tier 1 (Fast Track — Minimal Viable Community Launch)**:
     - Site Name & Tagline.
     - Administrator Account (Username, Temporary Password, optional private recovery email).
     - Standard Sovereign Legal Selection (Default internal scaffold vs. custom URL).
     - 1-Click Launch.
  2. **Tier 2 (Progressive Expanders — Advanced Infrastructure & Governance)**:
     - Group deep technical and enterprise options into clearly titled, collapsed disclosure panels:
       - *External Authentication (Keycloak OIDC / Cerbos PDP)*.
       - *Storage Providers (MinIO / S3 / Local Blob)*.
       - *Multi-Tenant Federation Controls*.
       - *Statutory Legal Identifiers (VAT/Tax registration numbers, corporate registry codes)*.
- **Ethical Justification**: Fulfills *Taysir* by eliminating artificial barriers for small mosques, while preserving full governance rigor for large institutions.

### IVSD-M002 — Provide Auto-Generated Sovereign Legal & Privacy Scaffolding
- **Action**: When the operator does not possess pre-hosted legal URLs, offer a 1-click option:
  > *"Generate Standard Community Legal Notice & Privacy Pledge (Hosted Internally)"*
- **Mechanism**: The platform automatically provisions standard, localized, privacy-first legal pages at `/legal/notice` and `/legal/privacy`, populated with the entered `SiteName`, `PublicContactEmail`, and `JurisdictionCountryCode`.
- **Rule**: These documents clearly articulate that the instance is an independent, community-operated deployment with zero third-party commercial data sharing. The operator can edit or replace these texts at any time in post-launch instance settings.
- **Ethical Justification**: Promotes *Sidq* (eliminates the incentive to submit fake placeholder URLs) and shields operators from unintentional regulatory non-compliance.

### IVSD-M003 — Deploy Interactive "Test Flight" Diagnostic Actions
- **Action**: Enhance launch readiness preflight checks with active verification triggers:
  1. **Email Delivery Test Ping**: A dedicated *"Send Diagnostic Email"* trigger allowing the operator to input an email address and observe real-time SMTP handshake and dispatch verification.
  2. **Storage Health Probe**: A *"Verify Storage Permissions"* trigger that executes an ephemeral 1-byte write, read, and delete cycle against configured blob storage, displaying verified round-trip latency.
  3. **Database & Cache Health Probe**: Displays active connection latency, connection pool capacity, and cache topology with human-readable operational guidance.
- **Rule**: Diagnostic triggers execute via bounded MediatR commands (`SendDiagnosticEmailCommand`, `VerifyStorageHealthCommand`) with strict IP/session rate-limiting and zero database persistence.
- **Ethical Justification**: Upholds *Amanah* by ensuring the operator never launches an instance with unverified, broken notification channels that leave community members uninformed.

### IVSD-M004 — Bridge the Day-0 to Day-1 Chasm: Post-Launch Launchpad & Community Sample Pack
- **Action**: Replace the abrupt redirect upon setup completion with a celebratory, empowering **Post-Launch Launchpad**:
  1. **Celebratory Affirmation**: Welcoming the operator and confirming that the platform is live and secure.
  2. **Three Guided Action Pillars**:
     - *Pillar A (Live Creation)*: *"Create Your First Event"* (lightweight 3-field quick-modal: Title, Date/Time, Location/Online).
     - *Pillar B (Exploration & Training)*: *"Load Community Sample Pack"* (seeds 2 realistic sample events: e.g., *"Weekly Youth Halaqa"* and *"Annual Community Iftar & Dinner"*).
     - *Pillar C (Collaboration)*: *"Invite Team Organizers"* (generates secure single-use administrative invitation links).
  3. **Guaranteed Sample Data Hygiene**: When sample data is loaded, display a persistent, non-intrusive top banner:
     > *"Demo Mode Active: Exploring with sample community events. [Clear Demo Data in 1 Click]"*
  - Clicking purge executes an atomic, clean removal of all demo records without leaving orphaned entries.
- **Ethical Justification**: Cultivates *Ta'awun* and removes fear of failure, empowering volunteer organizers to learn event publishing and ticket workflows safely before inviting the public.

### IVSD-M005 — Preserve Workspace Layout Discipline: Content Flow vs. Rail Navigation
- **Action**: Strictly enforce the structural layout boundaries in [`InstanceOnboarding.razor`](file:///home/amir/ISLAMU/Github/Event/src/Explore.Blazor.Client/Pages/Onboarding/InstanceOnboarding.razor) and [`OnboardingWorkspace.razor`](file:///home/amir/ISLAMU/Github/Event/src/Explore.Blazor.Client/Pages/Onboarding/Components/OnboardingWorkspace.razor):
  - **Main Flow (`ChildContent`)**: Hosts the entire progressive form narrative, including site profile, visual theme accent selection, logo upload, admin credentials, legal choices, and interactive diagnostic cards.
  - **Right Rail (`SummaryContent` & `HelpContent`)**: Exclusively reserved for:
    - **Top**: Setup progress tracking, task steps, and live step navigation links.
    - **Bottom**: Documentation links, deployment topology guides, and troubleshooting runbooks.
- **Rule**: Never inject visual mockups, social card previews, or heavy widgets into the sidebar rail.
- **Ethical Justification**: Protects cognitive clarity (*Bayan*), preserves screen reading accessibility for visually impaired administrators, and ensures help documentation is never displaced by decorative elements.

### IVSD-M006 — Integrate Prayer-Aware & Hijri Calendar Foundations
- **Action**: During instance configuration (or derived automatically from the primary timezone/location), introduce an opt-in toggle:
  > *"Enable Prayer-Time Awareness and Hijri Calendar"*
- **Capability**: When enabled, the platform:
  1. Displays Hijri dates alongside Gregorian dates throughout public event calendars.
  2. Offers event organizers an optional prayer-time conflict check during event scheduling, warning if a planned lecture or dinner overlaps with Maghrib or Friday Jumu'ah prayer.
- **Ethical Justification**: Centers the spiritual rhythm of the community (*Hifz ad-Din*), preventing scheduling clashes that inconvenience attendees or detract from congregational obligations.

### IVSD-M007 — Enshrine the Sovereign Privacy Pledge on Day 0
- **Action**: Display a prominent, beautifully styled trust anchor during the setup flow:
  > **The ISLAMU Sovereign Privacy Standard**  
  > *"This platform is completely self-contained. Your congregation's attendance records, contact details, and community interactions are stored solely in your database. Zero trackers. Zero ad networks. Zero behavioral profiling. Complete data export and automated backups remain under your absolute control."*
- **Ethical Justification**: Upholds *Hifz al-Khasusiyyah* (sanctity of privacy) and reassures community members that their sacred spaces remain free from commercial surveillance.

### IVSD-M008 — Achieve Complete Parity for Terminal and Headless Operators
- **Action**: Ensure that every onboarding capability is accessible without a web browser:
  1. **Terminal TUI (`Event.SetupAssistant.Terminal`)**: Deliver a keyboard-navigable, colorized interactive terminal wizard with live diagnostic pings for console-based setups.
  2. **Headless CLI Command (`event-cli preflight`)**: Allow CI/CD pipelines and container startup scripts to execute headless preflight verification against `.env` or Infisical configurations:
     ```bash
     dotnet run --project src/Event.SetupAssistant.Cli -- preflight --env-file .env --output json
     ```
- **Ethical Justification**: Embodies *Adl* (justice and fairness) toward technical maintainers, honoring their operational methodologies and enabling reliable, automated deployments.

---

### Rejected Alternatives

| Alternative | Reason for Rejection | Ethical Risk |
|---|---|---|
| **Placing a Live Directory Mockup Card in the Right Sidebar Rail** | Displaces step navigation links and documentation help; creates visual clutter and degrades mobile responsiveness. | Violates ergonomic clarity (*Bayan*) and compromises accessibility for keyboard/screen-reader users. |
| **Strictly Enforcing External Legal URLs on Step 1** | Assumes every grassroots community possesses an existing corporate website and legal counsel. | Causes cognitive exhaustion (*Haraj*), setup abandonment, or submission of fraudulent placeholder URLs (*Kadhib*). |
| **Silent Auto-Redirect to `/events` Upon Launch** | Leaves new operators stranded in an empty application with zero orientation or momentum. | Undermines community empowerment (*Ta'awun*) and increases support burden. |
| **Persistent Demo Events Without Clean Purge Mechanism** | Risks polluting real community event listings with fake or obsolete sample data. | Violates data integrity and causes public confusion regarding real vs. demo gatherings. |
| **Relying Solely on Static Preflight Checkmarks** | Fails to detect real-world SMTP port blocks, invalid credentials, or storage permission faults. | Violates *Amanah*; results in silent delivery failures and lost event confirmations. |

---

## Stakeholders

- **Community Leaders & Volunteer Administrators**: Primary beneficiaries of progressive disclosure, cognitive ease, and auto-scaffolded legal notices.
- **DevOps & Sysadmin Stewards**: Beneficiaries of headless CLI diagnostics, terminal parity, and active "Test Flight" preflight tools.
- **Congregation Members & Event Attendees**: Beneficiaries of the Sovereign Privacy Pledge, verified email delivery, and prayer-aware event scheduling.
- **Platform Maintainers & Support Stewards**: Beneficiaries of reduced setup friction, fewer misconfigured SMTP tickets, and clean workspace architecture.

---

## I-VSD Principles And Domains

### Applied Sunni Ethical Principles
1. **Taysir & Raf' al-Haraj (Ease & Removal of Hardship)**: System workflows must be accessible to ordinary humans, eliminating unnecessary bureaucratic barriers while preserving essential boundaries.
2. **Amanah (Custodianship & Moral Stewardship)**: Entrusted software must actively verify that it can deliver on its promises (e.g., verifying email dispatch before claiming operational readiness).
3. **Sidq & Bayan (Truthfulness & Clarity)**: Interfaces must speak truthfully, avoid dark patterns, and present information with structural clarity and dignified visual hierarchy.
4. **Hifz al-Khasusiyyah wa-l-'Ird (Protection of Privacy & Honor)**: Sanctifying personal and congregational data, rejecting all forms of surveillance capitalism and behavioral profiling.
5. **Ta'awun (Mutual Cooperation in Goodness)**: Supporting community organizers with guided starter packs and launchpads to foster vibrant, active communal life.
6. **Adl (Justice & Proportionality)**: Providing fair, dignified tooling for all roles—from non-technical elders to command-line system architects.

### Touched Software Domains
- **User Experience & Information Architecture**: Progressive disclosure, workspace rails, modal dialogs, and launchpad screens.
- **Application & CQRS Orchestration**: Ephemeral diagnostic commands, demo data seeding operations, and cleanup handlers.
- **System Administration & Self-Hosting**: Headless preflight verification, terminal TUI, and container lifecycle.
- **Legal & Governance**: Standard sovereign legal scaffolding, internal document hosting, and operator attribution.

---

## Common Overlooked Failures And Outcomes

1. **The "Silent Blackhole" Email Failure**: Launching an instance with valid SMTP credentials that fail network egress (e.g. cloud provider blocking port 25), leading to unregistered attendees and angry community members. Prevented by **IVSD-M003** live test pings.
2. **The "Demo Data Contamination" Trap**: Operators seed sample data to explore, but cannot easily delete it without manual database intervention, resulting in test events showing up on Google search. Prevented by **IVSD-M004** atomic 1-click purge.
3. **The "Fictitious Legal URL" Evasion**: Frustrated operators entering `https://example.com` or `https://google.com` to bypass required URL fields. Prevented by **IVSD-M002** auto-generated internal legal scaffolding.
4. **The "Mobile Screen Crushing" Failure**: Squeezing rich visual mockup cards into sidebar rails, rendering the onboarding wizard unusable on tablets and mobile devices. Prevented by **IVSD-M005** strict layout separation.

---

## Validation Gaps

- **Empirical Usability Benchmarking**: Usability testing with actual non-technical mosque committee members to measure task completion time for the "60-Second Fast Track".
- **Multi-Jurisdiction Legal Template Review**: Reviewing the auto-generated sovereign legal notice template against GDPR, CCPA, and regional non-profit disclosure requirements.
- **Network Egress Test Coverage**: Validating interactive SMTP and storage diagnostic commands against firewalled environments, restrictive corporate proxies, and air-gapped networks.

---

## Escalation Needed

1. **Scholarly Consultation on Prayer Calculation Defaults**: Escalating to qualified regional Islamic authorities to establish standard default calculation conventions (e.g. Muslim World League, ISNA, Umm al-Qura) based on detected jurisdiction.
2. **Legal Review of Sovereign Notice Templates**: Engaging legal counsel to ensure auto-generated privacy and operator notice text provides appropriate disclaimer protections for self-hosted community operators across key jurisdictions.

---

## Evidence Reviewed

- **E1**: [`src/Explore.Blazor.Client/Pages/Onboarding/InstanceOnboarding.razor`](file:///home/amir/ISLAMU/Github/Event/src/Explore.Blazor.Client/Pages/Onboarding/InstanceOnboarding.razor): Current interactive wizard layout, required fields, and preflight status rendering.
- **E2**: [`src/Explore.Blazor.Client/Pages/Onboarding/Components/OnboardingWorkspace.razor`](file:///home/amir/ISLAMU/Github/Event/src/Explore.Blazor.Client/Pages/Onboarding/Components/OnboardingWorkspace.razor): Workspace two-column shell composition, summary content, and help content slots.
- **E3**: [`src/Explore.Application/Features/InstanceOnboarding/Handlers/Queries/GetOnboardingPreflightQueryHandler.cs`](file:///home/amir/ISLAMU/Github/Event/src/Explore.Application/Features/InstanceOnboarding/Handlers/Queries/GetOnboardingPreflightQueryHandler.cs): Current passive preflight check evaluation.
- **E4**: [`src/Event.SetupAssistant.Terminal`](file:///home/amir/ISLAMU/Github/Event/src/Event.SetupAssistant.Terminal) and [`src/Event.SetupAssistant.Cli`](file:///home/amir/ISLAMU/Github/Event/src/Event.SetupAssistant.Cli): Existing console presentation and CLI entrypoints.
- **E5**: [`docs/internal/SELF_HOSTING.md`](file:///home/amir/ISLAMU/Github/Event/docs/internal/SELF_HOSTING.md) and [`docs/internal/CONFIGURATION.md`](file:///home/amir/ISLAMU/Github/Event/docs/internal/CONFIGURATION.md): Deployment modes, setup secret mechanics, and self-hosting constraints.
- **E6**: [`islamic-value-sensitive-design/consultations/i-vsd-tenancy-experience-directory-vs-dedicatedportal.md`](file:///home/amir/ISLAMU/Github/Event/islamic-value-sensitive-design/consultations/i-vsd-tenancy-experience-directory-vs-dedicatedportal.md): Established precedents for public experience truthfulness and communal dignity.

---

## Missing Evidence

- No live telemetry on setup abandonment rates (by design, since ISLAMU Event collects zero remote telemetry).
- No direct user interviews with community volunteers who failed initial Docker/onboarding deployment.
- Formal legal opinions on whether an auto-generated community notice satisfies statutory impressum requirements in strict jurisdictions (e.g. Germany's *Telemediengesetz* / *DDG*).

---

## Context Inventory

- This consultation report is fully autonomous and standalone.
- It operates strictly as an architectural and ethical evaluation of the onboarding user experience.
- It does **not** modify, alter, or depend upon the active implementation plan [`dev/active/instance-operator-onboarding/instance-operator-onboarding-plan.md`](file:///home/amir/ISLAMU/Github/Event/dev/active/instance-operator-onboarding/instance-operator-onboarding-plan.md).
- Findings and recommendations are formulated for future feature planning when the core technical hardening workstream concludes.

---

## Review Lifecycle

| Date | Previous status | New status | Trigger | Evidence |
|---|---|---|---|---|
| 2026-09-13 | None | Current | Standalone consultation on onboarding user experience, cognitive ergonomics, and communal dignity | E1–E6; Findings IVSD-F001–F008; Mitigations IVSD-M001–M008 |
