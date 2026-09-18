<!-- ABOUTME: Canonical I-VSD governance report on the shared multi-tenant community instance model. -->
<!-- ABOUTME: Defines ethical guardrails for maintainer labor protection, resource rationing, spam mitigation, and community sponsorship. -->

# I-VSD Strategy Review — Shared Community Instance Governance, Maintainer Labor Protection, and Resource Rationing

Last Updated: 2026-09-17

## Review Metadata
- Mode: standalone
- Subject: Shared Multi-Tenant Community Instance Governance, Maintainer Labor Protection, and Resource Rationing
- Workstream: none
- Report kind: governance-strategy-review
- Report status: current
- Disposition: advisory
- Evidence cutoff: 2026-09-17
- Reviewed input: Consultation Dialogue, `docs/internal/DEPLOYMENT_TIERS.md`, `docs/internal/MULTI_TENANCY.md`, `docs/internal/SELF_HOSTING.md`, `islamic-value-sensitive-design/governance/i-vsd-ecosystem-strategy-and-infrastructural-commons.md`
- Supersedes: none

---

## Scope

This review establishes the ethical, operational, and architectural governance for offering a **gratis, managed, shared multi-tenant instance** (implementing Tier 2 Community Tier from `docs/internal/DEPLOYMENT_TIERS.md`) to non-profit Islamic organizations, mosques, and community initiatives. 

It specifically evaluates:
1. **Maintainer Labor Protection & Livelihood Stewardship:** Governing the personal subsidization of infrastructure funded through student wages under Islamic principles of justice (*Adl*), self-preservation (*Hifz al-Nafs*), and avoidance of harm (*La Darar*).
2. **Intake Security & Abuse Prevention:** Guarding against public registration abuse (spammers, phishing, blacklisting of IP/email reputation) via gatekeeping and whitelisting (*Sadd al-Dharai'*).
3. **Equitable Resource Allocation ("Noisy Neighbor" Governance):** Enforcing technical boundaries and fair-use quotas (*Qist*) across concurrent organizations sharing a single physical host and database cluster.
4. **Dignified Value Framing vs. The "Free Software Trap":** Structuring the offering as a curated "Community Fellowship Grant" rather than cheap abandonware to foster organizational commitment and trust.
5. **Transition to Community Sustainability (*Waqf* / Sponsorship):** Establishing a transparent mechanism for participating organizations with operational budgets to sponsor server slots and lift the financial burden off the student maintainer.

**Exclusions:** This review does not dictate server network topologies beyond Hetzner infrastructure limits, nor does it establish binding corporate contracts for commercial enterprise tenants.

---

## Claim Boundary

This document represents provider-responsibility design reasoning and Islamic Value-Sensitive Design (I-VSD) analysis. It does not constitute a formal fatwa, binding legal contract, or commercial service level agreement (SLA). Ethical principles cited derive from classical Sunni jurisprudence (*Fiqh al-Mu'amalat*) and higher ethical objectives (*Maqasid al-Shariah*).

---

## Findings

### IVSD-F001: Asymmetric Maintainer Subsidy and Livelihood Depletion
- **Lifecycle:** `open`
- **Severity:** Critical (Maintainer Welfare & Sustainability Hazard)
- **Claim Type:** Ethical and economic risk analysis
- **Principle & Domain:** *Justice (Adl)*, *Non-Harm (La Darar wa la Dirar)*, *Preservation of Life and Wealth (Hifz al-Nafs wa'l-Mal)* | Business Model & Operational Domains
- **Stakeholder:** Project Steward (Student Founder)
- **Provider-Controlled Decision:** Subsidy model and financial underwriting of hosting infrastructure.
- **Description:** Financing server hosting, object storage, and domain infrastructure out of personal student job earnings (representing months of manual labor) to provide gratis enterprise-grade services to established organizations creates an unjust economic asymmetry. In Islamic ethics, charity (*Sadaqah*) is noble, but one is forbidden from harming one's own basic livelihood or family obligations to subsidize solvent third-party institutions (*"Charity is only out of abundance"*, Sahih Bukhari). Without a structured path to collective sustainability, maintainer exhaustion will force a sudden shutdown, harming all enrolled communities.
- **Evidence:** Founder personal accounting (student wages funding €400+/mo infrastructure and subscriptions); `docs/internal/DEPLOYMENT_TIERS.md`.
- **Linked Mitigation:** `IVSD-M001`
- **Owner:** Project Steward
- **Escalation Boundary:** Scholarly consultation on personal financial limits in communal obligation (*Fard Kifayah*).

### IVSD-F002: Public Self-Service Registration Abuse Risk
- **Lifecycle:** `open`
- **Severity:** High (Operational Integrity & Reputation Risk)
- **Claim Type:** Threat model and security analysis
- **Principle & Domain:** *Trust (Amanah)*, *Blocking the Means to Evil (Sadd al-Dharai')*, *Safeguarding Honor (Hifz al-Ird)* | Security & UX Domains
- **Stakeholder:** Enrolled Mosques, Community Attendees, Maintainer
- **Provider-Controlled Decision:** Onboarding gatekeeping and tenant provisioning mechanisms.
- **Description:** Providing an unvetted public registration flow where any actor can instantly create an organization tenant and dispatch outbox emails would invite rapid exploitation by malicious actors (SEO spam, phishing campaigns, bulk commercial spam). Because all tenants on this shared instance share an outbound SMTP provider and server IP, a single malicious tenant will cause immediate domain blacklisting, preventing genuine mosques from delivering registration confirmations and Eid tickets.
- **Evidence:** Industry threat data on open multi-tenant platforms; `Explore.Infrastructure` email dispatch mechanisms.
- **Linked Mitigation:** `IVSD-M002`
- **Owner:** Project Steward
- **Escalation Boundary:** N/A.

### IVSD-F003: "Noisy Neighbor" Monopolization of Shared Resources
- **Lifecycle:** `open`
- **Severity:** High (Performance & Fair Treatment Hazard)
- **Claim Type:** Architectural and operational analysis
- **Principle & Domain:** *Equitable Distribution (Qist)*, *Fair Dealing (Mu'amalah bil-Adl)*, *Removal of Hardship (Raf' al-Haraj)* | Architecture & Operational Domains
- **Stakeholder:** Co-hosted Community Organizations
- **Provider-Controlled Decision:** Tenant quota enforcement, rate limiting, and asset boundaries.
- **Description:** In a single PostgreSQL and ASP.NET Core deployment on a modest Hetzner server, one tenant running a massive 5,000-person ticket drop or uploading uncompressed 20MB flyer images can exhaust database connections, CPU cycles, and disk space, degrading or crashing service for other concurrent community organizations.
- **Evidence:** `docs/internal/DEPLOYMENT_TIERS.md` (Tier 2 Community single database cluster constraints); PostgreSQL connection pool metrics.
- **Linked Mitigation:** `IVSD-M003`
- **Owner:** Project Steward
- **Escalation Boundary:** N/A.

### IVSD-F004: The "Free Software Trap" and Organizational Commitment Deficit
- **Lifecycle:** `open`
- **Severity:** Medium (Adoption & Community Psychology Risk)
- **Claim Type:** Psychological and organizational behavior analysis
- **Principle & Domain:** *Truthfulness & Dignity (Sidq wa Karamah)*, *Excellence (Ihsan)* | Market & Community Domains
- **Stakeholder:** Mosque Leadership, Community Directors
- **Provider-Controlled Decision:** Program framing, onboarding branding, and cohort sizing.
- **Description:** Offering an open-ended "free plan" without criteria often devalues the software in the eyes of organizational leadership, who mistake "free" for "unstable amateur experiment" or "abandonware." Furthermore, organizations with zero skin in the game frequently request extensive support, fail to properly configure events, and abandon accounts, consuming maintainer support bandwidth without delivering community value.
- **Evidence:** Standard non-profit software adoption studies; Founder market observations.
- **Linked Mitigation:** `IVSD-M004`
- **Owner:** Project Steward
- **Escalation Boundary:** N/A.

---

## Recommendations

### IVSD-M001: The "Community Waqf Sponsorship" Footnote Model
- **Structural Action:** Establish an explicit, dignified sponsorship mechanism integrated directly into the tenant administration dashboard and transactional email footers:
  1. *Clear Transparency:* State that the instance is provided gratis as a community grant by ISLAMU, funded through volunteer sacrifice and community donations.
  2. *Sponsorship Invitation:* Provide a simple link enabling solvent mosques and non-profits to sponsor a "Server Capacity Slot" (€15 to €25/month).
  3. *Sustainability Threshold:* The moment 2 to 3 organizations sponsor their slots, the Hetzner hosting bill is 100% neutralized, shielding the student founder's personal income from ongoing operational drain.

### IVSD-M002: Strict Application-Only Intake (No Self-Service Provisioning)
- **Gatekeeping Policy:** Public tenant registration on the shared community instance is strictly disabled.
- **Intake Flow:**
  1. Organizations apply through a structured, short eligibility questionnaire (*"ISLAMU Community Fellowship Application"*).
  2. Verification requires demonstrating legitimate non-profit community presence, authentic event history, and an assigned point-of-contact.
  3. Tenant provisioning is performed manually by the Project Steward via administrative scripts or CLI, ensuring complete provenance of every active domain.

### IVSD-M003: Tenant Quotas and Fair-Use Invariants
- **Technical Invariants on Shared Community Instance:**
  1. *Attendee Boundary:* Default cap of 500 registered attendees per event. Organizations hosting large conferences (>500 attendees) are guided to self-host their own Tier 1 instance or contribute toward dedicated cluster resources.
  2. *Outbox Email Throttling:* Implement a per-tenant rate-limiter in Quartz dispatch (e.g., maximum 120 emails/minute per tenant) to prevent SMTP queue exhaustion and maintain delivery guarantees for all co-tenants.
  3. *Media Enclosure:* Enforce client-side image compression and server-side size caps (e.g., max 1.5MB per event banner/flyer) to safeguard Hetzner NVMe/S3 storage.

### IVSD-M004: Launch with "Cohort 1 (The Founding Five)"
- **Packaging Strategy:** Frame the offering as an exclusive, high-value fellowship rather than a generic commodity:
  - *Title:* **ISLAMU Community Fellowship — Cohort 1 (Limited to 5 Organizations)**.
  - *Selective Scarcity:* Honest positioning that server capacity is intentionally capped to guarantee white-glove concierge onboarding and five-nines operational reliability.
  - *Reciprocal Commitment:* In exchange for free managed hosting, selected organizations agree to:
    1. Actively run at least one live community event within 45 days.
    2. Provide direct qualitative feedback on organizer workflows.
    3. Allow ISLAMU to publish an anonymized or approved community case study demonstrating successful adoption.

---

## Stakeholders

| Stakeholder Group | Primary Interest / Moral Duty | Impact of Strategy |
| :--- | :--- | :--- |
| **Project Steward (Student Founder)** | Duty of self-care, preservation of livelihood, and avoidance of economic burnout. | Livelihood is protected; student wages are not drained by ungrateful free-riders; path to community self-funding is secured. |
| **Enrolled Mosques & Non-Profits** | Reliable, secure event registration without the prohibitive cost and complexity of self-hosting. | Receive enterprise-grade managed platform at zero cost, with high reliability guaranteed by strict cohort limits. |
| **Community Attendees** | Fast, reliable registration; protection of personal data; reliable delivery of tickets/emails. | Protected from noisy-neighbor server outages and spam blacklisting; assured data privacy on clean infrastructure. |
| **Broader Ummah** | Establishment of enduring, independent digital infrastructure (*Sadaqah Jariyah*). | Platform survives long-term rather than dying from premature founder burnout, serving as a model of ethical software stewardship. |

---

## I-VSD Principles And Domains

| Islamic Principle | Meaning & Translation | Application in this Strategy |
| :--- | :--- | :--- |
| **Adl** (*العَدْل*) | Divine Justice, equity, and balance. | Prohibits asymmetric exploitation where the student maintainer exhausts their wages to provide free software to wealthy organizations. |
| **La Darar wa la Dirar** (*لَا ضَرَرَ وَلَا ضِرَارَ*) | "Neither harm nor reciprocation of harm." | Protects the maintainer from financial harm and protects co-tenants from "noisy neighbor" performance harm. |
| **Hifz al-Mal** (*حِفْظُ المَال*) | Preservation and stewardship of wealth. | Ensuring community donation capital and founder student earnings are managed with extreme care and zero waste. |
| **Sadd al-Dharai'** (*سَدُّ الذَّرَائِع*) | Preemptively blocking the means that lead to harm. | Disabling public self-registration to block spam, phishing, and IP blacklisting before they occur. |
| **Qist** (*القِسْط*) | Fair and equitable distribution of shared resources. | Hard quotas on memory, disk, and email dispatch ensuring all community tenants receive equal quality of service. |
| **Karamah** (*الكَرَامَة*) | Inherent dignity and self-respect. | Packaging the free tier as an honorable "Fellowship Grant" rather than a desperate giveaway, commanding respect from leadership. |

---

## Validation Gaps

1. **Hetzner Resource Baseline under Multi-Tenant Concurrency:** The exact CPU, RAM, and database connection overhead of running five active concurrent organizations on a single Hetzner CPX31/CPX41 instance has not yet been benchmarked under live peak loads.
2. **Community Sponsorship Conversion Rate:** The willingness of mosques to contribute voluntary €15-€25/mo sponsorships to support the underlying instance remains an unverified hypothesis until Cohort 1 is completed.
3. **Application Intake Velocity:** The exact volume of applications received when Cohort 1 is announced will determine whether manual vetting remains sustainable or requires delegated reviewer workflows.

---

## Escalation Needed

- **Institutional Fiqh Consultation on Software Waqf:** Consult with Islamic scholars and non-profit legal specialists regarding the optimal legal structure for accepting voluntary server sponsorships (e.g., establishing an official non-profit association or formal *Waqf* trust).

---

## Evidence Reviewed

- `docs/internal/DEPLOYMENT_TIERS.md`: Tier 2 Community deployment specifications (single PostgreSQL cluster with multi-tenancy).
- `docs/internal/MULTI_TENANCY.md`: Tenant isolation boundaries and security context.
- `docs/internal/SELF_HOSTING.md`: Operational burden and sysadmin requirements for local hosting.
- `islamic-value-sensitive-design/governance/i-vsd-ecosystem-strategy-and-infrastructural-commons.md`: Foundational infrastructural commons posture.
- Founder financial accounts and student employment constraints.

---

## Missing Evidence

- Production load test measuring connection pool saturation when two concurrent tenants trigger batch email dispatches.
- Completed pilot agreements with candidate organizations for Cohort 1.

---

## Context Inventory

- **Hosting Infrastructure:** Hetzner Cloud Linux environment (CPX tier), NVMe storage, managed backups.
- **Tenant Isolation:** Single shared database with row-level tenant discriminators and ASP.NET Core tenant resolution middleware.
- **Maintainer Profile:** Solo developer, student employment self-funding, pre-revenue.
- **Target Audience:** Grassroots Islamic non-profits, student associations, and regional community mosques.

---

## Review Lifecycle

| Date | Previous Status | New Status | Trigger | Evidence / Replacement |
| :--- | :--- | :--- | :--- | :--- |
| 2026-09-17 | None | `current` | Strategic consultation on managed multi-tenant community hosting, maintainer labor ethics, and cohort gating. | Initial publication (`i-vsd-shared-community-instance-and-maintainer-sustainability.md`). |
