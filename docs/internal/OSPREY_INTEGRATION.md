<!-- ABOUTME: Architecture and authority boundaries for the Osprey machine-moderation integration. -->
<!-- ABOUTME: Documents multi-tenant isolation, signal-only callbacks, and local decision convergence. -->

# Osprey Machine-Moderation Integration

This document details the architectural integration, multi-tenancy challenges, and final system design for integrating **Osprey** (ROOST's real-time rules engine) into the multi-tenant ISLAMU Event platform.

---

## 1. Overview

**Osprey** is an automated, real-time moderation classifier and rule evaluation engine. It processes event payloads and returns safety scores, policy verdicts (e.g. Hate Speech, NSFW, Spambot detection), and recommended actions.

Unlike Coop, Osprey was designed for single-tenant internal applications (originally for Discord bot streams). It lacks native SaaS multi-tenancy. This document explains the security and isolation risks this presents and outlines our hybrid architectural solution.

---

## 2. The Multi-Tenancy Challenges

Integrating a single-tenant engine into a strict multi-tenant SaaS environment presented three critical architectural roadblocks:

### A. Scoped State Isolation in `labels_service` (Local vs. Global Banning)
Osprey tracks bad actors, offensive IPs, and user reputation metrics via its Postgres `labels_service` backend.
* **The Problem:** The `labels` table schema inside Osprey has no concept of a `tenant_id` or `namespace` column. All records are stored globally.
* **The Risk:** If Tenant A (e.g. Mosque A) marks `user_123` as a policy violator, that label is applied globally. As a result, `user_123` is blocked across Tenant B (Mosque B) and Tenant C.
* **The Solution:** True hierarchical multi-tenancy would require Osprey to support a nullable `tenant_id` column where `NULL` represents global platform rules and a UUID restricts rules/labels to a specific tenant:
  $$\text{Query} = \text{WHERE target\_id} = X \text{ AND (tenant\_id} = \text{tenant\_A OR tenant\_id IS NULL)}$$
  Since this is not natively supported by Osprey, stateful reputation tracking cannot be offloaded to Osprey's database without risking cross-tenant data leakage.

### B. Payload Security & Spoofing (Cross-Tenant Contamination)
If we attempt to run a single, shared Osprey container and pass tenant settings in the request payload:
* **The Problem:** Osprey does not natively authenticate or verify the contents of the payload JSON.
* **The Risk:** A compromised tenant credential (or a rogue admin) could send evaluation requests with spoofed tenant identifiers or overridden strictness settings, bypassing the Instance Admin's global lock constraints.

### C. Rule Compilation Scaling Limits
Osprey compiles its rules (written in SML or Rego) from files on the local filesystem into a static memory structure at startup.
* **The Problem:** If we attempt to write custom rules for hundreds of tenants inside a single Osprey rule file, the engine must compile a massive, complex AST (Abstract Syntax Tree) with a huge combinatorial tree of conditional statements:
  $$\text{Verdict} = \text{CoreRules} \lor (\text{TenantAToggle} \land \text{RulesA}) \lor (\text{TenantBToggle} \land \text{RulesB}) \dots$$
* **The Risk:** This leads to significant rule compilation delay, high memory usage, and AST parsing bottlenecks on every request evaluation. Furthermore, a syntax error in a single tenant's rule file could crash the entire shared Osprey engine.

---

## 3. The Solution: Hybrid Local + Machine Policy Engine

To bypass these limitations while preserving the platform promise of low operational footprint and absolute tenant isolation, ISLAMU Event implements a **Hybrid Local + Machine Policy Engine**.

```text
               Event Published / Reported
                            │
                            ▼
           ┌────────────────────────────────┐
           │   Local C# Evaluation Engine   │
           └────────────────┬───────────────┘
                            │
               (Passes local checks? Yes)
                            │
                            ▼
           ┌────────────────────────────────┐
           │ Build Osprey Evaluation Payload│
           │  - Includes tenant settings    │
           │  - Injects strictness flags    │
           └────────────────┬───────────────┘
                            │
                            ▼
           ┌────────────────────────────────┐
           │     Shared Osprey Instance     │
           │  - Runs stateless ML models    │
           │  - Applies global safety gate  │
           └────────────────┬───────────────┘
                            │
                            ▼
           ┌────────────────────────────────┐
           │   C# Aggregator & Verdict      │
           └────────────────────────────────┘
```

1. **Local Rules Evaluator (C#):**
   * All simple, deterministic rules (regex matches, keyword blocklists, character limits, coordinate boundaries) are stored in the ISLAMU Event PostgreSQL database.
   * These rules are evaluated directly in C# within the application handler, completely bypassing Osprey. This removes rule tree scaling overhead and ensures zero cross-tenant rule bleeding.

2. **Stateless Shared Osprey Instance:**
   * A single platform-wide Osprey instance is deployed. It is used strictly as a **stateless content classifier** (e.g. evaluating NSFW content, advanced spam probability, or semantic toxicity).
   * It does not store user history or local tenant state in the database.

3. **Payload-Driven Context Rules:**
   * When calling Osprey, the C# backend serializes the tenant's specific strictness toggles (configured in their `TenantSetting`) into the `action_data_json` request payload.
   * Osprey's rules act as stateless logical gates:
     $$\text{Verdict} = \text{CoreInstanceRules}(\text{content}) \lor (\text{TenantToggles} \land \text{TenantRules}(\text{content}))$$

---

## 4. Native Policy Management UI

This section describes the product direction for a native policy-management experience. It is not the authority path for provider callbacks or report decisions.

### The Policy Designer Dashboard
* **The Canvas:** Visual list of active moderation rules in a drag-and-drop workspace to reorder priority.
* **Affordance Gating:** The ability to edit, create, or toggle a policy card is gated strictly by checking the resource's HAL `_links` (complying with **Rule #6** of `AGENTS.md`).
* **The Rule Builder:** A declarative sentence composer (e.g., `If [Title] [Contains Keyword] [X] then [LightModerate]`).
* **Live Sandbox Simulator:** A side drawer where administrators can paste test text or media, click "Simulate", and see in real-time which rules trigger and what the final verdict would be before saving.

---

## 5. Callback Authority and Decision Convergence

Osprey is signal-only. `POST /api/integrations/moderation/osprey/callback` authorizes the provider request and sends `RecordOspreySignalCallbackCommand`. The handler may add idempotent `EventReportSignal` rows, synchronize the Osprey external link, and raise nonterminal report/case priority for human review. It does not create `EventReportDecision`, invoke `ExecuteReportDecisionCommand`, enforce an event action, complete a case, or materialize a reporter outcome email.

Signal replay is deduplicated by provider target plus external signal identity, or by normalized signal and correlation identity when the provider omits an external signal ID. A signal can inform a human moderator, but it cannot replace `EventReportCase.CurrentDecisionId` or reopen a completed case.

A later local moderator action remains an explicit two-step API flow:

```text
POST .../decision
    -> DecideEventReportCommand (capture/select local decision)
POST .../decision/execute
    -> ExecuteReportDecisionCommand (enforce, receipt, complete, notify)
```

The second call is the authoritative completion seam. Reporter outcome and needs-more-information notification intents are created there only after the exact decision enforcement receipt is valid. This keeps Osprey recommendations advisory while local and Coop decisions converge on one execution and notification owner.

---

## 6. Implementation Reference

* **Osprey Signal Provider:** `src/Explore.Infrastructure/Services/Moderation/OspreyModerationSignalProvider.cs`
* **Routing Policy Resolver:** `src/Explore.Infrastructure/Services/Moderation/ReportingRoutingPolicyResolver.cs`
* **Callback API:** `src/Explore.API/Controllers/ModerationIntegrationController.cs`
* **Signal Callback Handler:** `src/Explore.Application/Features/EventReporting/Handlers/Commands/RecordOspreySignalCallbackCommandHandler.cs`
* **Local Decision Capture:** `src/Explore.Application/Features/EventReporting/Handlers/Commands/DecideEventReportCommandHandler.cs`
* **Authoritative Decision Executor:** `src/Explore.Application/Features/EventReporting/Handlers/Commands/ExecuteReportDecisionCommandHandler.cs`
