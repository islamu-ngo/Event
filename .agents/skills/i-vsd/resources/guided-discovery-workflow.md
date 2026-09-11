<!-- ABOUTME: Interview-led I-VSD workflow for shaping new projects, ventures, missions, or orientations. -->
<!-- ABOUTME: Converts low-context user answers into contextual strategy, business, governance, moderation, and implementation recommendations. -->

# Guided Discovery Workflow

Use this resource for the `guided-discovery` action when the user does not yet have enough context, artifacts, or decisions for an evidence review. The goal is to ask structured questions first, help the user clarify mission and constraints, then produce `islamic-value-sensitive-design/consultations/i-vsd-guided-discovery.md` with contextual I-VSD recommendations.

## Core Rule

Do not produce a generic product strategy. I-VSD is contextual: the same Islamic principles may lead to different recommendations depending on mission, stakeholders, provider capacity, funding pressure, harm profile, operational ability, and governance maturity. A nonprofit, SaaS, consultancy, community project, curated directory, self-hosted product, or service business can each be appropriate in different contexts.

## Conversation Flow

1. Run the concise context gate from [context-discovery.md](context-discovery.md). If little or no context exists, say this is acceptable for guided discovery.
2. Follow [Grill-Me](../../grill-me/SKILL.md): resolve available facts directly, recommend an answer when a real choice exists, ask exactly one material question per response, and use the answer to choose the next dependent branch.
3. Reflect back the emerging mission, stakeholders, constraints, and moral risks before making recommendations.
4. Ask targeted follow-ups where the recommended model depends on unresolved facts.
5. Continue until mission/decision, provider/deployment model, affected stakeholders, relevant data-money-content-AI surfaces, and operational constraints are captured or explicitly unavailable. Then resolve the canonical subject path from [report-contract.md](report-contract.md) and update or create the report.
6. Mark unanswered areas as assumptions, validation gaps, or questions for scholarly, legal, operational, or stakeholder review.

## Interview Areas

Ask questions across these areas, adapting language to the user's context:

- Mission and intended benefit: purpose, problem, beneficiaries, non-negotiables, what the provider refuses even if profitable.
- Stakeholders and vulnerability: users, non-users affected, administrators, scholars, parents, communities, customers, customers' customers, harmed parties.
- Business model: donations, grants, sponsorships, paid hosting/support, SaaS, services, consultancy, marketplace, ads, affiliates, freemium, open source, or hybrid models.
- Funding pressure: investors, sponsors, platform dependencies, grants, debt/financing, revenue targets, riba or gharar-sensitive areas.
- Product surface: user-generated content, uploads, images, AI, search, ranking, messaging, payments, identity, events, community features, public profiles.
- Moderation and provider capacity: what can realistically be reviewed, appeal process, abuse handling, escalation, whether constrained input or curated assets are safer than open upload.
- Data and privacy: what data is necessary, retention, tracking, analytics, portability, deletion, privileged access, children or vulnerable users.
- Governance: decision rights, policy ownership, conflicts of interest, sponsor influence, community participation, auditability, accountability.
- Technical and operational capacity: team size, hosting, support, security, backups, incident response, self-hosting, portability, maintenance horizon.
- Market and communication: ideal customer profile, claims, pricing clarity, onboarding, cancellation/refunds, comparison language, growth pressure.

## Recommendation Logic

Use the user's answers to compare alternatives rather than force one default:

- Recommend nonprofit, waqf-like stewardship, donation, grant, or community models when the mission is public-benefit, religious/community trust is central, growth pressure would corrupt commitments, and the provider can sustain transparent stewardship.
- Recommend SaaS only when recurring paid software creates fair value, terms are clear, data practices are minimal, switching/cancellation are honest, and revenue does not depend on confusion, surveillance, addiction, spiritual pressure, or lock-in.
- Recommend services, consultancy, paid support, or implementation help when the value is expert guidance, customization, or operations rather than scalable software usage.
- Recommend open source or self-hosting only when the provider can maintain honest documentation, security updates, governance, and realistic support boundaries.
- Recommend constrained input, curated assets, two-step verification, premoderation, postmoderation, or no user uploads based on harm risk, audience, provider capacity, appeals, and operational budget.
- Recommend avoiding a feature when the provider cannot meet the duty it creates, even if the feature is common in the market.

## Report Shape

The generated report must follow the standard report contract from [report-templates.md](report-templates.md) and include these action-specific sections when useful:

```text
## Interview Summary
## Mission And Non-Negotiables
## Recommended Implementation Model
## Business Model Recommendation
## Governance Recommendation
## Moderation And User Input Recommendation
## Data And Privacy Recommendation
## Technical And Operational Recommendation
## Rejected Alternatives
## Open Questions For Next Interview
```

Each recommendation must include rationale tied to I-VSD principles/domains, the user's answers, tradeoffs, and evidence level. Do not present the report as a final religious ruling, certification, or proof that the future implementation will be ethical.
