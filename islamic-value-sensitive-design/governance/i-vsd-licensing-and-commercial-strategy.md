<!-- ABOUTME: Canonical I-VSD strategy review for ISLAMU Event licensing, enterprise compliance, and partner monetization. -->
<!-- ABOUTME: Defines the three-pillar ecosystem: AGPLv3 FOSS, CLA-backed Anti-SaaS Enterprise licensing, and Official Partners. -->

# I-VSD Strategy Review — Open-Source Licensing, Anti-SaaS Governance, and Commercial Model

Last Updated: 2026-08-27

## Review Metadata
- Mode: standalone
- Subject: ISLAMU Event Licensing, Dual-Licensing Architecture, and Commercial Sustainability
- Workstream: licensing-and-commercial-strategy
- Report kind: strategy-review
- Report status: current
- Disposition: advisory
- Evidence cutoff: 2026-08-27
- Reviewed input revision: Git HEAD (`legal/CLA.md`, `docs/legal/IP_GOVERNANCE.md`, `docs/DUAL_VERSIONING.md`)
- Supersedes: none

## Scope

This review evaluates the strategic, legal-architectural, and moral governance models for distributing and commercializing ISLAMU Event. It analyzes:
1. The public `AGPL-3.0-or-later` distribution model and community reciprocity.
2. The mechanism for offering non-AGPL alternative terms under the Contributor License Agreement (`legal/CLA.md`) for enterprise compliance.
3. The risk of proprietary SaaS forks creating asymmetric market advantage over the open community.
4. The commercialization policy for alternative enterprise licenses (paid vs. gratis).
5. The boundaries of the "Official Partner" program.

**Exclusions:** This review does not draft specific legal contract clauses under Belgian corporate law nor issue formal Sharia rulings on specific contractual fees.

## Claim Boundary

This document represents provider-responsibility design reasoning and Islamic Value-Sensitive Design (I-VSD) analysis. It is not a legal opinion, Sharia ruling (fatwa), commercial warranty, or proof of market outcome. Legal agreements require qualified legal counsel; religious-legal questions require qualified Sunni scholarly authority.

## Findings

### IVSD-F001: Asymmetric Closed-SaaS Free-Rider Risk
- **Lifecycle:** `open`
- **Severity:** High (Strategic & Moral Governance Risk)
- **Claim Type:** Design reasoning
- **Principle & Domain:** *Justice (Adl)*, *Trust (Amanah)*, *Non-Harm (La Darar)* | Strategic & Governance Domains
- **Provider-Controlled Decision:** Selection of terms for alternative outbound licenses granted under the CLA.
- **Description:** If ISLAMU grants an unrestricted permissive or commercial license to a commercial entity to bypass AGPLv3, that entity could develop proprietary closed-source features, launch a competing multi-tenant SaaS, and deny its improvements to the upstream open-source community. This would destroy the foundational principle of community parity and violate the trust of volunteer contributors who signed the CLA.
- **Evidence:** `legal/CLA.md` Section *Copyright License Grant* (grants broad relicensing authority to Project Steward).
- **Linked Mitigation:** `IVSD-M001`
- **Owner:** Project Steward

### IVSD-F002: Enterprise Procurement & AGPLv3 Compliance Rejection
- **Lifecycle:** `open`
- **Severity:** Medium (Adoption & Accessibility Barrier)
- **Claim Type:** Design reasoning
- **Principle & Domain:** *Excellence (Ihsan)*, *Avoiding Gharar* | Strategic & Operational Domains
- **Provider-Controlled Decision:** Availability of compliance-compatible licensing vehicles.
- **Description:** Many enterprise legal departments enforce strict blanket bans on `AGPL-3.0` due to fear of Section 13 (Remote Network Interaction) copyleft contagion affecting their internal private APIs, Active Directory/SSO integrations, or internal corporate data. These organizations often have no desire to sell SaaS, but are barred from adopting ISLAMU Event for private internal company events.
- **Evidence:** Standard enterprise open-source compliance policies.
- **Linked Mitigation:** `IVSD-M002`
- **Owner:** Project Steward

### IVSD-F003: Maintainer Exploitation & Unjust Resource Asymmetry
- **Lifecycle:** `open`
- **Severity:** Medium (Economic & Sustainability Risk)
- **Claim Type:** Design reasoning
- **Principle & Domain:** *Justice (Adl)*, *Non-Harm (La Darar)* | Business Model Domain
- **Provider-Controlled Decision:** Pricing policy for bilateral enterprise licensing.
- **Description:** Providing custom compliance reviews, indemnification, and private bilateral licenses to large for-profit corporations for free creates an unjust asymmetric burden (*Darar*) on the nonprofit maintainers while commercial corporations capture private economic value without contributing code back.
- **Evidence:** `docs/legal/IP_GOVERNANCE.md` (compliance verification and audit requirements).
- **Linked Mitigation:** `IVSD-M003`
- **Owner:** Project Steward

### IVSD-F004: Ecosystem Integrity in Partner Monetization
- **Lifecycle:** `open`
- **Severity:** Low (Trust & Truthfulness Governance)
- **Claim Type:** Design reasoning
- **Principle & Domain:** *Truthfulness (Sidq)*, *Trust (Amanah)* | Governance Domain
- **Provider-Controlled Decision:** Official Partner Program tier definitions and licensing scope.
- **Description:** If Official Partners receive proprietary source code advantages over the community, the project devolves into "open-core" bait-and-switch. Official Partner value must remain anchored in service quality, trust branding, and directory exposure, not closed-source technical privilege.
- **Evidence:** Project charter and repository governance policies.
- **Linked Mitigation:** `IVSD-M004`
- **Owner:** Project Steward

---

## Recommendations

### IVSD-M001: Mandatory Anti-SaaS Covenant in Alternative Licenses
Every alternative commercial or institutional license issued under the CLA must include an explicit **Anti-SaaS Covenant**:
1. **Permitted Scope:** Licensee is granted the right to run, modify, and integrate the software *solely for internal organizational operations and private events*.
2. **Forbidden Scope:** Licensee is *expressly prohibited from offering the software as a multi-tenant cloud service, managed service, API service, or commercial SaaS to external third parties*.
3. **Parity Enforcement:** Any entity wishing to offer a commercial SaaS must use `AGPL-3.0-or-later`, guaranteeing that all SaaS vendors must publish their source code and contribute equally to the public commons.

### IVSD-M002: Establish the "Enterprise On-Premises / Internal-Use License"
Formally standardize a bilateral enterprise license that:
- Explicitly waives AGPLv3 Section 13 network-copyleft contagion over internal enterprise systems, private single-sign-on (SSO), internal APIs, and private databases.
- Provides legal certainty and indemnity against viral copyleft claims for on-premise and private VPC deployments.

### IVSD-M003: Two-Tiered Licensing Model (Fair Value Exchange vs. Public Good)
Structure alternative licensing fees based on Islamic principles of fair exchange (*Mu'awadah bi'l-Ma'ruf*) and benevolence (*Ihsan*):
- **For-Profit Commercial Tier:** Paid commercial license fee. Revenue is strictly dedicated to non-profit stewardship (security audits, CVE remediation, core developer bounties, infrastructure, and localization).
- **Humanitarian / Educational / NGO Tier:** Gratis ($0 waiver) grant license upon verification for registered charities, non-profits, humanitarian missions, and educational institutions facing public procurement constraints.

### IVSD-M004: Pure Service & Trust Model for Official Partners
The Official Partner program must operate strictly on top of the public AGPLv3 codebase:
- Partners build their businesses on deployment, hosting, integration, customization, and event support services.
- ISLAMU monetizes the Partner Program through certification review fees, quality audits, official directory listings, and trust mark licensing.
- No partner receives exclusive proprietary software extensions from the ISLAMU foundation.

### Rejected Alternatives
- **Permissive Dual-Licensing (MIT/Apache):** *Rejected.* Would allow commercial actors to create proprietary closed-source SaaS forks without contributing back, destroying community parity.
- **Universal Gratis Alternative Licensing for Corporations:** *Rejected.* Exploitative of volunteer and nonprofit maintainer labor; deprives the project of sustainability funds needed for security audits.
- **ISLAMU Operating a First-Party Proprietary SaaS:** *Rejected.* Creates direct conflict of interest with community hosters and compromises the nonprofit mission.

---

## Stakeholders

1. **Open Source Community & Contributors:** Protected against proprietary exploitation; their contributed code under the CLA cannot be enclosed by a third-party SaaS competitor.
2. **Commercial Enterprises (Internal Users):** Gain compliance peace of mind and legal safety for private on-premise event hosting.
3. **Nonprofits, Charities, & Schools:** Gain access to compliant internal deployments without financial barriers.
4. **Third-Party SaaS Hosters & Providers:** Compete on a 100% level playing field under AGPLv3 reciprocity.
5. **Official Partners:** Monetize legitimate support, hosting, and integration services backed by ISLAMU quality verification.
6. **ISLAMU Nonprofit Steward:** Achieves sustainable, ethical non-SaaS funding for long-term project maintenance.

---

## I-VSD Principles And Domains

| Principle | Meaning & Operationalization in this Strategy |
|---|---|
| **Justice (*Adl*)** | Eliminates asymmetric power: no single entity (including partners or enterprises) can create a closed proprietary SaaS advantage over others. |
| **Trust (*Amanah*)** | Protects the trust placed by contributors in the CLA; ensures inbound rights are used solely for community sustainability, not commercial enclosure. |
| **Non-Harm (*La Darar*)** | Protects maintainers from uncompensated corporate compliance burdens; protects community from predatory forks. |
| **Truthfulness (*Sidq*)** | Complete transparency in licensing terms: AGPL for public commons, Internal-Use for private compliance, Quality mark for Partners. |
| **Avoiding Uncertainty (*Gharar*)** | Clear contractual definitions of "Internal Use" vs "External Hosted Service" preventing legal ambiguity. |
| **Benevolence (*Ihsan*)** | Universal accessibility via $0 grant licenses for humanitarian and charitable institutions. |
| **Promise-Keeping (*Wafa bil Ahd*)** | Irrevocable commitment that the core ISLAMU Event project remains forever free and open-source under AGPL-3.0. |

---

## Validation Gaps

1. **Legal Drafting Validation:** Exact phrasing of the Anti-SaaS covenant and Internal-Use definitions requires formal review under Belgian law.
2. **Pricing Heuristic Validation:** Enterprise pricing bands must be benchmarked against fair market rates for open-source enterprise licensing without becoming prohibitive or rent-seeking.
3. **Community Transparency:** The licensing policy and CLA steward guidelines must be published transparently in public project documentation.

---

## Escalation Needed

- **Legal Counsel Escalation:** Formal drafting of the *ISLAMU Enterprise Internal-Use Commercial License Agreement* and verification of Belgian nonprofit governance requirements.
- **Scholarly Escalation:** Review of specific commercial fee structures, enterprise license agreements, and partnership revenue models by qualified Islamic finance scholars where required.

---

## Evidence Reviewed

- `legal/CLA.md` (ISLAMU Event Contributor License Agreement v1.0)
- `docs/legal/IP_GOVERNANCE.md` (IP Protection, Clean-Room Governance, And Audit Readiness)
- `docs/DUAL_VERSIONING.md` (Commercial and FOSS dependency separation precedent)
- `.ci/scripts/validate-dependency-license-policy.cs` (Automated license compliance rules)
- GNU Affero General Public License v3 (`LICENSE`)

---

## Missing Evidence

- Draft text of the commercial bilateral enterprise agreement (to be authored with legal counsel).
- Operational partner certification guidelines and quality rubric (to be authored during partner program launch).

---

## Context Inventory

- **Subject:** Dual-Licensing, Enterprise Compliance, Anti-SaaS Governance, and Partner Monetization.
- **Target Organization:** ISLAMU Nonprofit (in formation; interim trustee Amir Akrari).
- **Core Repository:** `ISLAMU/Event` (Clean Architecture, .NET 10, Blazor BFF, PostgreSQL).

---

## Review Lifecycle

| Date | Previous status | New status | Trigger | Evidence/replacement |
|---|---|---|---|---|
| 2026-08-27 | *none* | `current` | Initial strategy review requested under I-VSD | Analysis of `legal/CLA.md`, `IP_GOVERNANCE.md`, and community anti-monopoly requirements |
