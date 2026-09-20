<!-- ABOUTME: I-VSD planning report for documentation visual enhancement, internal anti-drift consolidation, and CI quality gating. -->
<!-- ABOUTME: Evaluates Islamic Value Sensitive Design principles (Amanah, transparency, anti-monopoly, and cognitive ease) for documentation governance. -->

# I-VSD: Documentation Quality, Architectural Consolidation & Continuous Verification

Last Updated: 2026-09-03 Europe/Brussels

## Report Metadata

- **Report identity:** `docs-quality-and-consolidation`
- **Mode:** planning
- **State:** current
- **Disposition:** plan-aligned
- **Evidence cutoff:** 2026-09-03
- **Reviewed input revision:** `sha256:391cb1862670c9ce39fc8fce599ba82b54dcc40c`
- **Reviewed plan artifact revision:** `dev/active/docs-quality-and-consolidation/docs-quality-and-consolidation-plan.md`
- **Planned workstream:** `dev/active/docs-quality-and-consolidation/`
- **Planned artifacts:**
  - `dev/active/docs-quality-and-consolidation/docs-quality-and-consolidation-plan.md`
  - `dev/active/docs-quality-and-consolidation/docs-quality-and-consolidation-context.md`
  - `dev/active/docs-quality-and-consolidation/docs-quality-and-consolidation-tasks.md`

---

## Scope & Evaluated Questions

This report evaluates the Islamic Value Sensitive Design (I-VSD) impact of elevating the ISLAMU Event documentation ecosystem from an 8.5/10 baseline to a 10/10 standard. The scope encompasses:
1. **Visual Diagrammatic Enrichment (P1)**: Injecting pure, GitBook-compatible Mermaid sequence and state diagrams for complex lifecycles (paid ticketing, outbox workers, webhooks, custom properties privacy, and gate check-in).
2. **Internal Documentation Anti-Drift Consolidation (P3)**: Pruning oversized, duplicate operator runbooks from `docs/internal/` (1,557 lines in `SELF_HOSTING.md`, 2,329 lines in `CONFIGURATION.md`, 806 lines in `TROUBLESHOOTING.md`, and 277 lines in `CERBOS_COOLIFY.md`) to establish Single Responsibility between operator documentation (public GitBook) and C# software architecture (internal GitHub Markdown).
3. **Continuous Verification & Automated Gating (P4)**: Authoring a dedicated GitHub Actions workflow (`.github/workflows/docs-lint.yml`) enforcing link integrity, zero raw HTML, and environment variable documentation parity.
4. **Explicitly Deferred Items (Documented as Future TODOs)**:
   - **TODO-1 (P2: Zero-Clone Production Deployments)**: Standalone `curl | bash` installer and standalone downloadable `docker-compose.prod.yml`.
   - **TODO-2 (P5: Real-World Scenario Cookbooks & Playbooks)**: End-to-end recipes (e.g. "Zero to 1,500-Attendee Conference in 30 Mins", zero-downtime SQLite $\to$ Postgres migration).
   - **TODO-3 (P6: Interactive GitBook OpenAPI & Scalar Integration)**: Mounting live ASP.NET Core OpenAPI schemas directly in GitBook with multi-language code tabs.

---

## Islamic Value Sensitive Design (I-VSD) Analysis

### 1. *Amanah* (Trustworthiness & Truth in Documentation)
- **Ethical Concern**: When documentation duplicates information across multiple files (e.g. Docker Compose instructions in both `docs/internal/SELF_HOSTING.md` and `docs/public/.../docker-compose.md`), divergence and obsolescence become inevitable. An operator following a stale command breaches the trust placed in the software author.
- **Remediation**: Establish strict **Single Source of Truth** by pruning duplicate operator guides from `docs/internal/` and turning them into architectural specifications (`HOSTING_ARCHITECTURE.md`). Public GitBook documentation becomes the sole authoritative source for deployment commands.

### 2. *Taysir* & *Yusr* (Ease, Simplicity & Reducing Cognitive Friction)
- **Ethical Concern**: Text-dense, paragraph-only explanations of complex multi-party distributed state machines (such as Stripe Connect $\to$ Webhook $\to$ HMAC Ticket generation $\to$ Gate Check-In) create cognitive fatigue, misunderstandings, and configuration errors for community organizers.
- **Remediation**: Visualizing lifecycles via clean Mermaid diagrams provides immediate clarity (*Bayan*), respecting the user's time and mental bandwidth.

### 3. *Adl* & *Haqq* (Fairness, Data Sovereignty & Anti-Monopoly)
- **Ethical Concern**: Keeping operational deployment knowledge buried in deep internal developer directories disproportionately benefits advanced C# engineers while handicapping grass-roots community organizers, non-technical Masajid, and self-hosters.
- **Remediation**: Public GitBook documentation is democratized and comprehensive. Automated CI linting ensures that self-hosters never encounter broken links or undocumented environment variables.

---

## Stakeholder Impact Matrix

| Stakeholder | Legitimate Expectation | Potential Hazard | I-VSD Safeguard |
|---|---|---|---|
| **Self-Hoster / Community Admin** | Frictionless, accurate deployment guides with clear visual understanding of data flows. | Stale bash commands causing database locks or broken volumes. | Internal runbooks pruned; public docs become the sole tested operational truth. |
| **Event Attendee** | Unambiguous ticket purchase, payment transparency, and verifiable gate entry. | Misconfigured webhooks leading to payment without ticket issuance. | Mermaid sequence diagrams clarify exact Stripe webhook reconciliation requirements. |
| **Developer / Contributor** | Concise internal architecture specs without 1,500 lines of copy-pasted Docker configs. | Cognitive overload from monolithic markdown manuals. | Lean `HOSTING_ARCHITECTURE.md` and `CONFIGURATION.md` focusing strictly on C# invariants. |
| **AI Coding Agent** | Bounded, token-efficient context files without redundant operator manuals. | Context poisoning from outdated internal deployment instructions. | Drastic token reduction across `docs/internal/` via single-responsibility pruning. |

---

## Explicit Future Scope (Deferred TODOs for Future Workstreams)

The following items are recognized as highly valuable for the ecosystem but are deliberately deferred from the current implementation plan per user decision:

* **[TODO-1] P2: Turnkey Zero-Clone Production Deployments**:
  - Author a standalone `curl -fsSL https://get.openislamu.org/event.sh | bash` launch script for bare-metal VPS setups.
  - Create a self-contained `docker-compose.prod.yml` that can be downloaded independently without cloning the entire git repository.
* **[TODO-2] P5: Production Scenario Playbooks & Cookbooks**:
  - Author real-world walkthroughs: "Zero-to-Door-Ready: 1,500-Person Conference Setup", "Zero-Downtime SQLite to PostgreSQL Migration Runbook", and "Multi-Tenant Community Federation Setup".
* **[TODO-3] P6: Interactive GitBook OpenAPI & Multi-Language Code Playground**:
  - Wire automated ASP.NET Core `openapi.json` exports into GitBook’s native OpenAPI block.
  - Provide copy-pasteable tabs (cURL, C# HttpClient, TypeScript fetch, Python requests) for core public APIs.

---

## Conclusion & Disposition

The proposed workstream directly honors the core I-VSD tenets of **Amanah** (truth in documentation), **Bayan** (visual clarity), and **Taysir** (reducing operator friction).

**Disposition: Plan-Aligned.** The workstream may proceed to formal implementation planning.
