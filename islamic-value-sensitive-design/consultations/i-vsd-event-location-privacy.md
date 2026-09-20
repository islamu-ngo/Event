<!-- ABOUTME: I-VSD consultancy report on Event Location Privacy and Private Home disclosure for ISLAMU Event. -->
<!-- ABOUTME: Focuses on hurmat al-buyut (sanctity of homes), hifdh an-nafs/al-ird, amanah, la darar, and fail-closed disclosure authority. -->

# I-VSD Consultancy Report: Event Location Privacy

Last Updated: 2026-08-22

## Scope

This report evaluates the Islamic Value Sensitive Design (I-VSD) principles and provider responsibilities governing event location privacy, contextual disclosure, and personal residence protection within the ISLAMU Event platform. Specifically, it covers:
- The sanctity and protection of Private Homes (`Hurmat al-Buyut`, `Hifdh an-Nafs`, `Hifdh al-'Ird`).
- Provider moral responsibility (`Mas'uliyyah`, `Amanah`) in safeguarding physical addresses and coordinates.
- Fail-closed disclosure ceilings (`Lā Darar wa-Lā Dirār`) preventing accidental data leakage to unauthorized publics or search engines.
- Entitlement-bound disclosure (`'Adl`, `Amanah`) ensuring exact addresses are disclosed strictly to verified, confirmed participants with scope coverage.
- Irreversible erasure and tombstoning (`Tawbah`, `Ibra'`) ensuring deleted residence data is expunged and cannot be resurrected or exfiltrated via caches or logs.

**Exclusions**: This report does not constitute a formal fiqh ruling or fatwa. It establishes the design reasoning and moral trace for software engineering decisions.

## Claim Boundary

This report represents I-VSD design reasoning and traceability. It is not a fatwa, Sharia certification, product certification, or empirical proof of moral or ethical outcomes. Religious-legal questions concerning community hosting obligations or fiqh rulings should be referred to qualified scholarly authority.

## Core Findings & Principles

| # | Finding | Principle | Domain | Severity |
|---|---|---|---|---|
| 1 | **Sanctity of the Home (`Hurmat al-Buyut`) Must Be Protected by Default**: In Islamic ethics, the privacy and dignity of the home and family are sacred. Defaulting private residences to public disclosure creates severe physical and spiritual vulnerability. | Sanctity of Home (`Hurmat al-Buyut`), Protection of Honor/Life (`Hifdh an-Nafs / al-'Ird`) | Security, Privacy, Domain | Critical |
| 2 | **Provider Trusteeship (`Amanah`) Requires Fail-Closed Boundaries**: When community members register for home gatherings or halaqas, organizers and platforms act as custodians of trust. Any ambiguous registration state (null approval, pending, waitlisted) must fail closed. | Trusteeship (`Amanah`), Non-Harm (`Lā Darar`) | Application, Security | High |
| 3 | **Truthfulness (`Sidq`) Demands Honest Representation**: Disclosing placeholder states (such as "Location to be announced" or coarse city/country) must be transparent and never deceive attendees or leak exact details via unredacted ICS calendars, JSON-LD, or MCP AI summaries. | Truthfulness (`Sidq`), Transparency (`Bayan`) | API, UX | High |
| 4 | **Scope Justice (`'Adl`) Forbids Over-Granting Across Sessions/Days**: An attendee registered for Day 1 must never receive exact home addresses for Day 2 or unselected private workshops. Entitlement must be strictly bounded by placement coverage. | Justice (`'Adl`), Fairness | Application, Authorization | High |
| 5 | **Irreversible Erasure Upholds True Redress (`Ibra'`)**: When a home host revokes consent or requests account erasure, the system must irreversibly erase PII (`LocationPii`) and tombstone identifying home room labels, preventing unredacted historical resurrection. | Redress (`Ibra'`), Right to be Forgotten | Persistence, Infrastructure | Critical |

---

## Technical Mitigations & Traceability

1. **Explicit Private Home Governance**:
   - `LocationKind.PRIVATE_HOME` requires explicit `OwnerUserId` and consent.
   - Instance and tenant governance defaults enforce `location_privacy.allow_home_locations=false` and `minimum_home_audience=CONFIRMED_PARTICIPANT`.
   - Public representation of private residences is strictly locked to generic labels (e.g. "Private venue"), omitting street address, postal code, and exact coordinates.

2. **Entitlement & Server-Time Reveal**:
   - `EventLocationRegistrationAccessService` enforces exact registration coverage (Event, Day, SessionSelection).
   - `EventLocationDisclosureEvaluator` applies server UTC time gates (`RevealFullDetailsFromUtc`) only *after* attendee entitlement succeeds.

3. **Multi-Surface Convergence & PII-Free Audits**:
   - Calendar ICS, MCP/AI gateways, search projections, and Blazor JSON-LD consume the pure batched disclosure authority.
   - Outbox messages and audit trails (`EventLocationDisclosureAudit`, `EventLocationExactReadAudit`) strictly omit raw PII and coordinates.

4. **Zero Backward Compatibility Baggage in Development Mode**:
   - Pre-v1 development posture permits immediate elimination of legacy unsafe DTOs and direct migration to purpose-specific, HAL-gated contracts.
