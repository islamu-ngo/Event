<!-- ABOUTME: Canonical I-VSD report on optional Sentry and GlitchTip error tracking integration. -->
<!-- ABOUTME: Governs open-source sponsorship, self-hoster parity, zero-telemetry defaults, and strict PII redaction. -->

# Sentry And GlitchTip Error Tracking — I-VSD Architectural Decision Report

Last Updated: 2026-09-11

## Review Metadata

- Mode: standalone
- Subject: Optional Sentry and GlitchTip error-tracking integration with OpenTelemetry and self-hoster parity
- Workstream: none
- Report kind: architectural decision and provider-responsibility review
- Report status: current
- Disposition: ready-for-planning
- Evidence cutoff: 2026-09-11
- Reviewed input: working tree at `97784ef6d22decab97a4da82c9e18f3ed8d7c2b1`
- Supersedes: none

## Scope

This report evaluates the provider responsibilities, ethical boundaries, and technical architecture for introducing advanced application crash reporting and error intelligence into the ISLAMU Event platform. It addresses the opportunity to utilize Sentry's generous Open Source Sponsorship program for ISLAMU's hosted platform without compromising the autonomy, privacy, or operational simplicity of independent self-hosters.

### In Scope

- **Pluggable crash tracking**: Integrating Sentry via `Explore.ServiceDefaults` and `Explore.Blazor` exclusively as an opt-in sink driven by an optional `SENTRY_DSN` environment variable.
- **Self-hoster parity and the GlitchTip path**: Explicit first-class documentation and verification of [GlitchTip](https://glitchtip.com) (an open-source, lightweight, Sentry-wire-compatible crash tracking server) as an on-premise alternative to Sentry SaaS.
- **Zero-footprint baseline**: Preserving the existing fully open-source, zero-external-dependency observability baseline (OpenTelemetry, Prometheus, Loki/Grafana, and .NET Aspire Dashboard) with zero network calls and zero external accounts required by default.
- **Privacy, data taxonomy, and redaction**: Enforcing strict sanitization and stripping of Personal Identifiable Information (PII), authentication tokens, attendee credentials, and financial payloads before error envelopes leave the application boundary.
- **Rejection of proprietary lock-in (elmah.io)**: Documenting the rejection of closed SaaS alternatives lacking self-hosted backends.

### Out Of Scope

- Core relational data schema, domain models, and MediatR business logic (Clean Architecture strictly decouples domain logic from error sinks).
- Third-party commercial contract negotiations and formal non-profit application approvals with Sentry, Inc.
- Religious-legal classification (*fatwa*) of external corporate sponsorship agreements or terms of service.

## Claim Boundary

This report is a provider-responsibility design analysis grounded in Islamic Value-Sensitive Design (I-VSD), technical architecture invariants, and software ethics. It is not a fatwa, Sharia certification, legal opinion, or commercial warranty. It evaluates maintainer duties toward users, operators, attendees, and community self-hosters regarding transparency, data stewardship, fairness, and non-harm.

## Common Overlooked Failures And Outcomes

### Failures And Their Evidence Limits

1. **Silent Telemetry Leaks (Tajassus / Breach of Amanah)**: Crash-reporting SDKs by default often capture HTTP request bodies, cookies, authorization headers, IP addresses, and query parameters. Unscrubbed stack traces could inadvertently transmit attendee passwords, bearer tokens, or sensitive contact details to external cloud collectors.
2. **Hidden Phoning-Home (Gharar / Deception)**: Packaging a third-party SDK that initializes silently or emits heartbeat/diagnostic telemetry even when the operator has not configured a DSN.
3. **Asymmetric Ecosystem Neglect (Injustice / Bias)**: Designing error logging so that only the primary platform maintainer has diagnostic visibility into bugs, treating community self-hosters as unmonitored second-class operators.
4. **Heavyweight Infrastructure Imposition (Haraj / Undue Hardship)**: Forcing self-hosters to run Sentry's official self-hosted distribution (which requires Kafka, ClickHouse, and 8GB+ RAM), effectively closing the door to self-hosted crash triage for resource-constrained masajid and non-profit hosts.
5. **Runtime Blocking and Cascade Failures**: Network latency, timeouts, or rate limits on the external error reporting endpoint degrading or blocking core HTTP request processing.

### Negative Consequences

- Loss of community trust (*Amanah*) if private attendee data is exposed to third-party telemetry vendors.
- Operational burden or unexpected cloud subscription costs imposed on community operators.
- Unfair platform dynamics where software quality is maintained only for the cloud deployment while self-hosted instances suffer from unaddressed, untriaged bugs.

### Intended Positive Outcomes

- **High Reliability and Fast Triage (*Ihsan*)**: ISLAMU Event core maintainers can immediately detect, fingerprint, and remediate production regressions, edge-case null references, and Blazor client crashes using Sentry's sponsored tier.
- **Self-Hoster Empowerment (*Tamkin*)**: Self-hosters have complete autonomy to remain 100% offline/local, connect to their own Sentry organization, or deploy a lightweight GlitchTip container in under 200MB of RAM.
- **Truthful and Transparent Operations (*Sidq*)**: Clear environment variable documentation, zero hidden trackers, and inspectable open-source data pipelines.

## Findings

### IVSD-F001 - Telemetry Ingestion and Privacy Redaction Boundary
- **Lifecycle**: open
- **Severity / claim**: High; data stewardship (*Amanah*) and avoiding intrusive tracking (*Tajassus*).
- **Principle/domain**: Non-Harm (*La Darar*), Privacy (*Hifdh al-'Ird*); Technical & Data Governance.
- **Stakeholders / provider decision**: Attendees, organizers, and operators; whether external error tracking captures unscrubbed HTTP request headers, query strings, cookies, or stack frame local variables.
- **Evidence**: `Explore.ServiceDefaults/Compliance/DataTaxonomy.cs` defines strict PII classifications. Sentry's default ASP.NET Core integration captures request bodies and authorization headers unless customized.
- **Linked mitigation**: [IVSD-M001](#ivsd-m001---strict-pii-scrubbing-and-before-send-redaction-filter).
- **Owner**: Platform / Infrastructure Engineers.

### IVSD-F002 - Self-Hoster Autonomy and Vendor Neutrality
- **Lifecycle**: open
- **Severity / claim**: High; justice (*'Adl*), removing hardship (*Raf' al-Haraj*), and avoiding dependency lock-in.
- **Principle/domain**: Trust (*Amanah*), Justice (*'Adl*); Strategic & Architecture.
- **Stakeholders / provider decision**: Grassroots masajid, non-profit self-hosters, air-gapped deployments; whether error tracking requires proprietary SaaS or allows self-hosted sinks.
- **Evidence**: `docs/internal/DEPLOYMENT_TIERS.md` mandates that Tier 1 (Humble) operates with minimal overhead and zero mandatory cloud services. Closed-source SaaS alternatives (such as elmah.io) offer no self-hostable server.
- **Linked mitigation**: [IVSD-M002](#ivsd-m002---opt-in-dsn-activation-and-glitchtip-compatibility).
- **Owner**: Architecture / Platform Lead.

### IVSD-F003 - Truthful Default Posture (Zero Phoning-Home)
- **Lifecycle**: open
- **Severity / claim**: Medium; truthfulness (*Sidq*) and avoiding unexpected operations (*Gharar*).
- **Principle/domain**: Truthfulness (*Sidq*), Promise-Keeping (*Wafa'*); Operational & UX.
- **Stakeholders / provider decision**: System operators and compliance officers; whether the application makes external network calls when unconfigured.
- **Evidence**: `Explore.ServiceDefaults/Extensions.cs` conditionally configures OTLP only when `OTEL_EXPORTER_OTLP_ENDPOINT` is present. Sentry must follow the exact same non-intrusive convention.
- **Linked mitigation**: [IVSD-M003](#ivsd-m003---dormant-sdk-when-sentry_dsn-is-absent).
- **Owner**: Infrastructure Lead.

## Recommendations

### IVSD-M001 - Strict PII Scrubbing and Before-Send Redaction Filter
Configure Sentry's .NET and Blazor SDKs with an explicit `BeforeSend` callback and custom `ISentryEventProcessor` wired to ISLAMU Event's existing compliance pipeline (`Explore.ServiceDefaults.Compliance.StarRedactor`). The scrubber must:
1. Strip all `Authorization`, `Cookie`, `X-Api-Key`, and authentication tokens.
2. Anonymize or drop user IP addresses (`SendDefaultPii = false`).
3. Redact attendee email addresses, phone numbers, and payment details from error messages and breadcrumbs.
4. Clean SQL query parameters before attaching database command spans.

### IVSD-M002 - Opt-In DSN Activation and GlitchTip Compatibility
1. Support error reporting via the standard `SENTRY_DSN` environment variable.
2. Keep Sentry completely decoupled from domain and application logic. Handlers emit standard `ILogger` and `ActivitySource` spans; Sentry listens at the hosting level via `Explore.ServiceDefaults`.
3. Explicitly test and document the [GlitchTip](https://glitchtip.com) integration in self-hosting documentation (`docs/public/` and `docs/internal/`). GlitchTip uses the standard Sentry wire protocol, enabling self-hosters to run their own crash-reporting dashboard in a lightweight container backed by their existing PostgreSQL database.

### IVSD-M003 - Dormant SDK When SENTRY_DSN Is Absent
If `SENTRY_DSN` is empty or unset:
1. The Sentry SDK must remain completely inert—no background threads, no HTTP hooks, no network requests.
2. The platform continues to route all logs, metrics, and traces exclusively to the local OpenTelemetry pipeline (Aspire Dashboard, Prometheus `/metrics`, or Loki).
3. No warning logs or degraded-state indicators should appear simply because Sentry is not configured.

### Rejected Alternatives

- **Alternative 1: Mandatory Sentry Cloud Dependency**: Rejected. Violates self-hosting sovereignty and imposes external commercial dependencies on small community deployments.
- **Alternative 2: elmah.io Integration**: Rejected. While elmah.io offers a generous open-source program, it is closed-source SaaS only with no self-hostable server. Adopting it would benefit only ISLAMU and leave self-hosters completely locked out of self-hosted error tracking.
- **Alternative 3: No Crash Reporting (Logs Only)**: Rejected. Triaging complex regressions and client-side Blazor crashes from raw Loki logs is inefficient. Utilizing Sentry's free open-source sponsorship elevates software quality (*Ihsan*) without incurring financial cost.

## Stakeholders

- **Platform Maintainers (ISLAMU Foundation)**: Responsible for overall platform stability, security patches, and release quality; beneficiaries of Sentry's sponsored tier.
- **Self-Hosted Community Operators (Masajid, Halaqat, NGOs)**: Need full autonomy, simple maintenance, and zero mandatory cloud overhead, with the option to self-host crash diagnostics.
- **Event Attendees and Donors**: Entitled to total privacy, confidentiality, and data minimization (*Hifdh al-'Ird*), ensuring their identities and transactions are never leaked to external telemetry.
- **Open-Source Contributors**: Benefit from standardized OpenTelemetry instrumentation and reproducible issue logs with stack unwinding.

## I-VSD Principles And Domains

| Principle | Meaning in this Decision | Applied Domain |
| :--- | :--- | :--- |
| **Trust / Stewardship (*Amanah*)** | Protecting attendee and operator data by ensuring telemetry does not harvest PII or lock operators into proprietary clouds. | Strategic, Technical |
| **Justice / Fairness (*'Adl*)** | Treating self-hosters equitably by providing an open-source, self-hosted path (GlitchTip) rather than a cloud-only privilege. | Strategic, Operational |
| **Non-Harm (*La Darar*)** | Preventing credential leakage in stack traces and eliminating performance overhead when error tracking is inactive. | Technical, Data Governance |
| **Excellence (*Ihsan*)** | Raising the bar for code reliability, rapid bug fixes, and Blazor UX stability using modern error intelligence. | Technical, Evaluation |
| **Truthfulness (*Sidq*)** | Complete transparency in configuration; zero hidden phoning-home or undeclared telemetry. | Operational, Design |
| **Avoiding Spying (*Tajassus*)** | Scrubbing IP addresses, request bodies, and personal identifiers before error reporting. | Data Governance |

## Validation Gaps

1. **GlitchTip Integration Verification**: While GlitchTip implements the Sentry DSN protocol, specific advanced features (such as Blazor WebAssembly Session Replay) may have partial compatibility in GlitchTip. Compatibility boundaries must be empirically tested and documented.
2. **Redaction Test Suite Coverage**: Automated unit and architecture tests must verify that synthetic exceptions containing JWTs, passwords, and PII are completely redacted before serialization by the Sentry client.
3. **Blazor WASM Bundle Overhead**: Measuring the client-side WebAssembly binary size impact when `Sentry.AspNetCore.Blazor.WebAssembly` is linked.

## Escalation Needed

- **Religious-Legal Boundary**: This report handles technical architecture and provider duties. Formal religious rulings on non-profit sponsorships or software license terms are outside I-VSD scope and remain reserved for qualified Sunni scholarly authority.
- **Commercial / Sponsorship Formalities**: The ISLAMU Foundation steward must submit the formal Open Source Sponsorship application to Sentry, Inc., ensuring acceptance of open-source program requirements.

## Evidence Reviewed

- `Explore.ServiceDefaults/Extensions.cs`: Inspection of current OpenTelemetry, Prometheus, and OTLP configuration.
- `Explore.ServiceDefaults/Compliance/DataTaxonomy.cs` & `StarRedactor.cs`: Verification of existing PII redaction capabilities.
- `docs/internal/DEPLOYMENT_TIERS.md` & `docs/internal/SELF_HOSTING.md`: Verification of Tier 1 (Humble) and self-hosting constraints.
- `.agents/skills/error-tracking/SKILL.md`: Verification of existing error-handling and observability guardrails.
- Official Sentry for Open Source Program terms and documentation.
- Official GlitchTip architecture and Sentry SDK compatibility specifications.

## Missing Evidence

- Empirical test results for GlitchTip running alongside ISLAMU Event in a docker-compose environment.
- Concrete benchmark measurements of memory allocation when Sentry SDK is in dormant (unconfigured) state in .NET 10.

## Context Inventory

- Current Observability: OpenTelemetry (traces/metrics), Prometheus exporter (`/metrics`), Serilog structured logs, Loki / Grafana support, Aspire Dashboard.
- Target Error Tracking: Sentry .NET SDK + Sentry Blazor WebAssembly.
- Self-Hosted Alternative Target: GlitchTip container pointing to application PostgreSQL instance.

## Review Lifecycle

| Date | Previous status | New status | Trigger | Evidence/replacement |
| :--- | :--- | :--- | :--- | :--- |
| 2026-09-11 | None | Current | User architectural decision to implement Sentry with open-source sponsorship, self-hoster GlitchTip support, and strict optionality | Initial standalone I-VSD decision report |
