ABOUTME: Primary entrypoint for authentication, authorization, and trust-boundary documentation.
ABOUTME: Points readers to the maintained security model while preserving documentation index links.

# Security

> **Audience:** Operators | Contributors | AI agents
> **Status:** Mixed
> **Owner:** Security
> **Last Verified:** 2026-08-30
> **Source Anchors:** `docs/SECURITY-MODEL.md`, `src/Explore.Application/Features/ConfigurationManifest/Importing/`, `src/Explore.Application/Features/ConfigurationManifest/Managed/`, `Explore.Blazor/Extensions/YarpProxyExtensions.cs`, `Explore.Blazor/Services/CircuitTokenStore.cs`

The maintained security model lives in [SECURITY-MODEL.md](SECURITY-MODEL.md).

Use that document for the current BFF trust boundaries, token-forwarding model, antiforgery contract, auth diagnostic safety, support-access boundary, upload-session binding, and circuit token lifecycle notes.

Configuration portability is a Tier 1 administration boundary. Upload tokens,
direct-transfer nonces, and destination proofs are header-only capabilities;
responses are private/no-store and bounded by size, rate, and timeout policies.
The authenticated route selects instance or tenant authority, HAL controls UI
affordances, and authorization is rechecked on every write. Apply recomputes the
preview under ordered locks and one serializable transaction. Secrets, PII,
application data, provider bindings, operational state, and deployment topology
are excluded by the closed registry. Direct transfer accepts only public
HTTPS/443 destinations, distinct mutual approvals, digest-bound chunks, and
promotion into the ordinary preview/apply workflow; a signature or transfer
proof never grants configuration authority.

The approved lifecycle-email plan adds dispatch-time privacy authorization without changing SMTP into an authority. Email may target only a current verified tenant member or an authorization-bound same-tenant managed tenant-administrator invitation. Incompatible pre-1.0 delivery ledgers are reset instead of receiving a synthetic legacy authority; inbox notifications and unrelated business/audit/settings data remain preserved. Consent, preference, disclosure, deletion, and supersession may narrow a snapshotted policy before provider handoff, never broaden it.
