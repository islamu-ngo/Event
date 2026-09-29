<!-- ABOUTME: Governing policy for AI Context Disclosure across Blazor rail, Application, and MCP. -->
<!-- ABOUTME: Backed by ADR-012 and the field-classification-matrix; enforced by AiContextDisclosureSchemaTests. -->

# AI Context Security

**Status:** Proposed (matches ADR-012)
**Authority:** `docs/adr/ADR-012-ai-context-disclosure-policy.md`
**Last Updated:** 2026-07-04

This document is the **governing policy** that governs how platform data may be disclosed to AI model providers, persisted into prompt transcripts, and surfaced through MCP tool responses. It is the human-readable counterpart to ADR-012 and the field classification matrix.

---

## 1. Why this document exists

The AI Assistant stack (`Explore.Blazor.Client/Components/Shell/AiAssistantRail.razor`, `Explore.Application/Features/AiAssistant/**`, `Explore.API/Mcp/AiAssistantMcp*`) historically used a deliberately narrow allow-list to keep prompts free of regulated PII. That allow-list was too narrow to answer users' questions about a referenced event's sessions, speakers, languages, audience, or attendance, and too implicit to keep PII out of new AI flows. The AI Context Disclosure Policy replaces the ad-hoc allow-list with an explicit, machine-checked framework.

## 2. Framework overview

| Component | Source of truth |
|---|---|
| Sensitivity classification | `AiContextSensitivityEnum` (`Explore.Domain/Enums/AiContextSensitivityEnum.cs`) |
| Disclosure rule vocabulary | `AiContextDisclosureRuleEnum` (`Explore.Domain/Enums/AiContextDisclosureRuleEnum.cs`) |
| Provider trust tiers | `AiProviderTrustTierEnum` (`Explore.Domain/Enums/AiProviderTrustTierEnum.cs`) |
| Administrative scope | `AiAdministrativeContextScopeEnum` (`Explore.Domain/Enums/AiAdministrativeContextScopeEnum.cs`) |
| Field registry | `AiContextDisclosureRegistry` (`Explore.Application/Features/AiAssistant/Disclosure/AiContextDisclosureRegistry.cs`) |
| Field matrix | `dev/active/ai-context-disclosure-policy/field-classification-matrix.md` |
| Completeness test | `Event.Architecture.Tests/AiContextDisclosureSchemaTests.cs` |
| Gateway (Phase 2) | `IAiContextGateway` (`Explore.Application/Features/AiAssistant/Disclosure/`) |
| Consent (Phase 2) | `AiContextConsent` domain entity |

## 3. Data sensitivity classification

Five sensitivity tiers (aligned with NIST SP 800-122, GDPR Art. 4(1), ISO/IEC 27001 A.5.12):

| Tier | Name | Permitted base disclosure |
|---|---|---|
| `0` | **Public** | Allow to all tiers |
| `1` | **Internal** | Allow to all tiers |
| `2` | **Confidential** | Allow only at local model tier; elsewhere Deny |
| `3` | **Restricted** | Allow only at local model tier with Phase-4 gating; elsewhere Deny |
| `4` | **Special** | Deny at every tier, including local model |

**Classification is the responsibility of the registry, not the consumer.** Consumers ask the gateway "what is the effective rule for this field under these conditions?" and never inspect the enum directly.

## 4. Disclosure rule vocabulary

| Rule | Meaning |
|---|---|
| `Deny` | Field never reaches the AI. Default for unclassified fields. |
| `Redact` | Field is masked (e.g., `a***@example.com`, city-only for address). |
| `Aggregate` | Field is emitted only as a count, sum, or bin (e.g., geo coordinates → city centroid). |
| `Allow` | Field is emitted in full, subject to consent + provider trust + transcript controls. |

Higher values are more permissive. The effective rule is always the **most restrictive** of the contributing inputs.

## 5. Provider trust tiers

Evidence-based (CTO correction #6), never naming-based. The least-trusted tier wins when evidence is ambiguous.

| Tier | Evidence |
|---|---|
| `0` `LocalInProcessOrSameNetworkModel` | Endpoint resolves to loopback or same-VPC private address; no public egress. |
| `1` `TenantControlledPrivateEndpoint` | Tenant-configured endpoint on a tenant-controlled private network. |
| `2` `TenantConfiguredExternalProcessor` | Tenant-configured external provider with explicit opt-in. |
| `3` `PlatformConfiguredExternalProcessor` | Platform-default external provider shared across tenants. |
| `4` `Unknown` | Evidence cannot be established. Behaves as the most-restrictive tier. |

Provider classification rules:

- The **least** specific positive signal wins. A model that is reachable over loopback but routes through a tenant-external proxy is classified at the more restrictive tier of the two.
- The classification is computed by `AiProviderTrustResolver` (Phase 2). It inspects tenant settings, network configuration, and any platform deployment mode (sovereign cloud, regional, etc.).
- Unknown evidence MUST resolve to `Unknown`. There is no optimistic default.

## 6. Policy hierarchy

The effective disclosure rule is the **intersection** of three policies:

```
effectiveRule = instancePolicy ∩ tenantPolicy ∩ userConsent
```

- **Instance policy** is the registry's base rule plus any instance-wide overrides.
- **Tenant policy** is the tenant's configured overrides for its own data (when supported).
- **User consent** is the per-user consent record for fields that require it.

A user consent can NEVER override an instance or tenant deny (CTO correction #7). If any of the three returns `Deny`, the effective rule is `Deny`.

## 7. Administrative scope

Instance administrators receive AI access only via `AiAdministrativeContextScopeEnum` (CTO correction #1):

| Scope | Permitted disclosure |
|---|---|
| `InstanceAggregate` | Instance-wide counts/totals; never row-level user PII. |
| `TenantAggregate` | Tenant-scoped counts/totals. |
| `OperationalDiagnostics` | Operational health (queue depth, error rates); no user content. |

Row-level user PII is never authorized through the general AI assistant for any administrative scope. Administrative contexts use a separate `AiAdministrativeContextScope` workstream.

## 8. Phase gating

PII disclosure (any field classified `Restricted` or `Confidential` reaching an AI prompt) is **disabled** until Phase 4 completes.

| Phase | Tasks | Effect |
|---|---|---|
| Phases 1–3 | 1.1–3.4 | Only `Public` and `Internal` fields reach AI prompts. |
| Phase 4 (prerequisite) | 4.1 persistence max-sensitivity, 4.2 log redaction, 4.3 deletion propagation | PII disclosure becomes possible. |
| Task 4.4 (flip) | Gated on 4.1–4.3 verified | PII disclosure enabled per matrix + consent + provider trust. |

The flip is a single configuration change that is audit-logged. It cannot be set per-tenant without an instance-wide enable.

## 9. AI Context Gateway (Phase 2 preview)

`IAiContextGateway` is the **single evaluation point** for AI disclosure. Every AI flow routes through it.

```csharp
public interface IAiContextGateway
{
    Task<AiSanitizedContextEnvelope> ResolveAsync(
        AiContextResolutionRequest request,
        CancellationToken cancellationToken);
}
```

- **Input:** the requesting principal (actor context, scopes), the target entity reference, the provider trust tier, the current phase flags.
- **Output:** an immutable `AiSanitizedContextEnvelope` containing only fields whose effective disclosure rule was `Allow`, with redaction / aggregation applied where required.
- **Failure mode:** fail closed. Any exception during resolution returns an envelope with no fields.

The gateway never returns raw entity references. Callers receive a sanitized envelope; redaction and aggregation are performed at the gateway boundary, not at the consumer.

## 10. Bypass prevention (Phase 3)

Architecture tests enforce that the gateway is the only authorized path (CTO correction #8):

- `Explore.Application/Features/AiAssistant/**` may NOT depend on `*Pii` entities, broad repositories, or `Event` aggregate roots directly except via `IAiContextGateway`.
- `Explore.API/Mcp/**` is subject to the same restriction.
- The test (`AiContextGatewayBypassTests`) uses the project dependency graph to detect violations.

The existing `AiSafeDataContextRegistry` and `AiReferencePromptPacker` continue to operate, but they consume envelopes from the gateway rather than DTOs from the repositories.

## 11. MCP narrowing (Phase 5 preview)

Every MCP tool in `Explore.API/Mcp/AiAssistantMcp*.cs` routes through the gateway. Unsafe MCP tools are blocked in Phase 2 (Task 2.5) and refactored in Phase 5 to expose:

- Field-level metadata (which fields were disclosed, which were denied).
- Provider trust tier used for resolution.
- Consent status for any PII fields.

Tool consumers (model clients) MUST honor the metadata. Tools MUST NOT return raw PII even when the consumer requests it explicitly.

## 12. UI and consent (Phase 6 preview)

- **JIT consent:** When the AI flow needs a `Restricted` field that requires consent, the UI surfaces a consent prompt rather than silently failing or silently disclosing.
- **Consent records:** Persisted per user, per tenant. A consent record carries the field list, the consent timestamp, and the policy version that was disclosed.
- **Revocation:** A consent record may be revoked at any time. The gateway re-evaluates immediately on revocation.
- **Admin deployment modes:** Sovereign-cloud and air-gapped deployments default to `LocalInProcessOrSameNetworkModel`. Hybrid deployments default to `TenantControlledPrivateEndpoint` unless configured otherwise.

## 13. Field matrix summary

For the full per-field classification, see `dev/zarchive/ai-context-disclosure-policy/field-classification-matrix.md`. Summary counts:

| Entity | Properties | Public | Internal | Confidential | Restricted |
|---|---|---|---|---|---|
| `UserPii` | 4 (excl. nav) | 0 | 1 | 0 | 3 (Phase-4 gated) |
| `OrganizationPii` | 7 (excl. nav) | 1 | 4 | 1 (Phase-4 gated) | 1 (Phase-4 gated) |
| `ActorPii` | 5 (excl. nav) | 4 | 1 | 0 | 0 |
| `LocationPii` | 7 (excl. nav) | 0 | 2 | 0 | 5 |

Navigation properties (`User`, `Organization`, `Actor`, `Location`) are intentionally not classified.

`Postcode` is contextual rather than globally safe: an organization postcode remains coarse `Internal` context, while a venue postcode in raw `LocationPii` is `Restricted` and Phase-4 gated. EventLocation consumers must first apply the purpose-specific location disclosure policy; passing that projection through the AI gateway never upgrades its field ceiling.

`AddressSubstringKey` is reversible normalized exact-address search material, so it is
`Restricted`, denied from every model-context tier, and hard-deleted with its owning
location. `AddressSubstringKeyVersion` is `Internal` persistence metadata and is likewise
denied because it has no model-context purpose.

## 14. Drift control

- **Adding a new `*Pii` property** without a matrix row fails the build (Phase 1 reflection test).
- **Changing a sensitivity classification** requires updating the matrix, the registry seed, and this document's summary table.
- **Adding a fifth `*Pii` entity** requires: new matrix section, registry seed extension, reflection allowlist update.

## 15. Operator runbook

When the AI assistant returns empty context for a referenced entity, check in this order:

1. Provider trust tier classification in tenant settings.
2. Tenant-level policy overrides (when supported).
3. User consent records for the requested field.
4. Phase gating flag (`AiContextDisclosureOptions.PiiDisclosureEnabled`).

When a tool consumer reports unexpected denial:

1. Inspect the envelope metadata (`deniedFields` and `deniedReasons`).
2. Verify the provider trust tier for the calling tenant.
3. Verify the user consent record exists and is not revoked.

## 16. Failure modes

| Condition | Behavior |
|---|---|
| Gateway exception during resolution | Fail closed; empty envelope. |
| Provider trust evidence missing | Tier = `Unknown`; effective rule = `Deny` for `Confidential`/`Restricted`. |
| Tenant policy missing | Policy = base registry. |
| User consent missing | Consent = `Deny` for fields that require consent. |
| Phase flag disabled | All `Restricted`/`Confidential` fields = `Deny`. |

## 17. Cross-references

- **Decision record:** `docs/adr/ADR-012-ai-context-disclosure-policy.md`
- **Field matrix:** `dev/active/ai-context-disclosure-policy/field-classification-matrix.md`
- **Plan:** `dev/active/ai-context-disclosure-policy/ai-context-disclosure-policy-plan.md`
- **Tasks:** `dev/active/ai-context-disclosure-policy/ai-context-disclosure-policy-tasks.md`
- **Security model:** `docs/SECURITY-MODEL.md`
- **Authorization:** `docs/AUTHORIZATION.md`
- **RAG foundation:** `docs/AI_RAG_FOUNDATION.md`
- **Agent contract inventory:** `docs/AI_AGENT_CONTRACT_INVENTORY.md`
- **MCP hosting:** `docs/adr/ADR-010-mcp-adapter-hosting-strategy.md`, `docs/adr/ADR-011-local-mcp-stdio-diagnostic-host.md`
