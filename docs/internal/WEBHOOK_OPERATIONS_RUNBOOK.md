<!-- ABOUTME: Operational SLO, alerting, startup, incident, recovery, and migration runbook for webhooks. -->
<!-- ABOUTME: Covers Local and self-hosted Svix without exposing tenant identity, payloads, URLs, or credentials. -->

# Webhook Operations Runbook

> **Audience:** Operators | SREs | Maintainers
> **Status:** Implemented
> **Owner:** Platform/Ops
> **Last Verified:** 2026-07-14
> **Source Anchors:** `Explore.Application/Telemetry/`, `Explore.Infrastructure/Webhooks/`, `Explore.Infrastructure/HealthChecks/`, `Explore.API/Program.cs`, `docs/WEBHOOKS.md`, `docs/BACKUP_RESTORE_UPGRADE.md`

This runbook is for outgoing Local delivery and the supported self-hosted Svix profile. Managed
Svix SaaS is not a supported profile. Use aggregate telemetry to find an incident, then use the
authorized management API and its HAL links to inspect or act on tenant-owned resources. Never put
tenant, message, endpoint, event, or publication IDs into metric labels or alert annotations.

## Readiness Contract

| Endpoint | Scope | Expected use |
|---|---|---|
| `/health` | Aggregate service readiness | Deployment and traffic admission. |
| `/health/webhooks/local` | `webhook-local-delivery` only | Diagnose Local queue health independently of Svix. |
| `/health/webhooks/svix` | `webhook-svix-provider` only | Diagnose the selected self-hosted Svix profile independently of Local. |
| `/metrics` | Prometheus scrape | Bounded counters and histograms listed below. |

`Local` readiness is healthy when Local is not selected, degraded when Local is selected but its
processor is disabled or its bounded queue thresholds are exceeded, and unhealthy when its queue
state cannot be read. `Svix` readiness is healthy when Svix is not selected. When selected, it is
unhealthy unless the provider-publication processor is enabled, the exact environment/version/
capability-policy tuple has executed conformance evidence, enabled capabilities are proven, and the
server-side auth-token binding resolves. Readiness returns only safe booleans, counts, versioned
capability metadata, and bounded status text.

Startup option validation rejects unknown providers, unsupported or zero-evidence Svix tuples,
unproven enabled capabilities, invalid URLs, and invalid secret-definition bindings before the host
accepts traffic. Readiness rechecks the dynamic secret and processor state.

## Metrics And Cardinality Budget

| Instrument | Bounded dimensions | Meaning |
|---|---|---|
| `explore.webhooks.claim_lag` | `provider`, `operation` | Seconds from durable due time to a Local delivery or Svix publication/reconciliation claim. |
| `explore.webhooks.processing_outcomes` | `provider`, `operation`, `outcome` | Claimed and durably settled worker outcomes. |
| `explore.webhooks.retries_scheduled` | `provider`, `operation` | Automatic retries accepted by the durable state machine. |
| `explore.webhooks.dead_letters` | `provider`, `operation` | Terminal dead-letter transitions. |
| `explore.webhooks.publication_unknown_age` | `provider` | Age of a Svix publication when automatic/manual reconciliation observes it. |
| `explore.webhooks.manual_reconciliations` | `provider` | Publications moved to operator-owned reconciliation. |
| `explore.webhooks.endpoint_auto_pauses` | `provider` | Transitions into automatic pause, counted once per transition. |
| `explore.webhooks.provider_health_checks` | `provider`, `outcome` | Local/Svix readiness observations. |
| `explore.webhooks.retention.cleanup_runs` | `mode`, `outcome` | Cleanup/dry-run pass outcomes. |
| `explore.webhooks.retention.cleanup_items` | `mode`, `data_kind` | Bounded cleanup selections or mutations by evidence category. |

Allowed provider values are `local` and `svix`; operations are `delivery`, `publication`,
`reconciliation`, `recovery`, and `readiness`; outcomes come from the closed enum-backed telemetry
vocabulary. Unknown cleanup modes or data kinds collapse to `unknown`. Payloads, signatures,
credentials, portal values, provider bodies/errors, tenant/resource IDs, event IDs, and URLs are
forbidden in metrics, traces, logs, and alert text. Webhook catch paths log only a bounded operation
message and exception type, never the exception object or raw provider response.

The Prometheus exporter normalizes dot-separated instrument names and counter/histogram suffixes.
Confirm the exact exported series on `/metrics` after an exporter upgrade before applying rules.

## SLOs And Alerts

These are initial production objectives. Rebaseline thresholds from 30 days of representative load;
do not add identity labels to make an alert easier to route.

| Signal | Objective | Warning | Critical |
|---|---|---|---|
| Claim lag | p95 under 30 s and p99 under 120 s over 30 days | p95 over 30 s for 15 min | p99 over 120 s for 15 min |
| Automatic retry ratio | Under 5% of terminal processing outcomes | Over 5% for 15 min | Over 15% for 15 min |
| Dead-letter ratio | Under 0.1% of terminal outcomes | Any sustained increase for 15 min | Over 1% for 15 min |
| Unknown publication age | p99 under 15 min | Any observation over 15 min | Any observation over 60 min or manual backlog unacknowledged for 4 h |
| Endpoint auto-pause | No unexplained transition | Any transition opens an incident ticket | Five or more in 15 min for one provider |
| Provider readiness | Selected provider continuously ready | `degraded` Local observation | Any selected-provider `unhealthy`/`disabled` observation or readiness HTTP 503 |
| Retention cleanup | One successful pass within twice the configured interval; no repeated batch saturation | Partial failure or one saturated category for 3 passes | Failed pass, no success for 2 intervals, or repeated saturation for 6 passes |

PromQL templates below use the normal OpenTelemetry Prometheus names. Keep `provider`, `operation`,
and `outcome` as the only routing dimensions:

```promql
histogram_quantile(
  0.95,
  sum by (le, provider, operation) (rate(explore_webhooks_claim_lag_seconds_bucket[15m]))
) > 30

sum by (provider, operation) (rate(explore_webhooks_retries_scheduled_total[15m]))
/
clamp_min(sum by (provider, operation) (rate(explore_webhooks_processing_outcomes_total[15m])), 1)
> 0.05

sum by (provider, operation) (rate(explore_webhooks_dead_letters_total[15m]))
/
clamp_min(sum by (provider, operation) (rate(explore_webhooks_processing_outcomes_total[15m])), 1)
> 0.001

histogram_quantile(
  0.99,
  sum by (le, provider) (rate(explore_webhooks_publication_unknown_age_seconds_bucket[15m]))
) > 900

increase(explore_webhooks_endpoint_auto_pauses_total[15m]) > 0

increase(explore_webhooks_retention_cleanup_runs_total{outcome=~"failed|partial_failure"}[2h]) > 0
```

## Local-Only Startup And Incident Recovery

1. Select `WEBHOOKS_PROVIDER=Local`. Leave self-hosted Svix credential values empty; Local does not
   start or depend on Svix.
2. Start the selected Compose/Aspire topology and require `/alive`, `/health`, and
   `/health/webhooks/local` to return HTTP 200.
3. Confirm `localProviderSelected=true`, `processorEnabled=true`, no stale claims, and due work below
   the configured warning threshold. Do not inspect endpoint URLs or payloads through health data.
4. If lag/backlog rises, verify API/PostgreSQL health, processor logs, worker replica count, and the
   configured global/per-tenant/per-endpoint concurrency limits. Do not bypass tenant fairness by
   manually dispatching HTTP requests.
5. Let the recovery pass reclaim expired fenced leases. A `recovered` outcome means work returned to
   the normal durable scheduler; it does not prove external delivery.
6. Use the authorized delivery-attempt collection and only its emitted HAL actions for retry. If the
   relation is absent, resolve retention, endpoint state, or authorization instead of calling a
   guessed write route.

## Self-Hosted Svix Startup And Provider Outage

1. Start only the pinned self-hosted profile documented in `WEBHOOKS.md`. PostgreSQL and shared Redis
   queue/cache must be healthy before Svix.
2. Generate the self-hosted bearer token from the running container with
   `svix-server jwt generate`; store it in the configured secret source. Never print or commit it.
3. Set `WebhookProviderPublicationProcessor:Enabled=true` and select `Svix` or `Composite`; Quartz then owns the `webhook-provider-publication-drain` cadence.
4. Require both `/health/webhooks/local` and `/health/webhooks/svix` to return their expected
   independent state, then require aggregate `/health` before traffic admission.
5. On outage, stop provider-mode changes and credential rotation. Preserve stable publication
   identity and the twelve-hour idempotency window. Definitely-not-accepted failures may retry;
   ambiguous acceptance must become `PublicationUnknown`, never a blind duplicate submission.
6. After recovery, watch publication claim lag, retry ratio, unknown age, and provider health.
   Reconcile uncertain rows before resuming configuration changes.

## Unknown Publication And Manual Reconciliation

1. Alert from `publication_unknown_age` and `manual_reconciliations`; locate candidate rows through
   the authorized provider-publication collection, not metric labels or logs.
2. Preserve the original provider event ID, idempotency key, request hash, credential snapshot, and
   validity cutoff. Do not reconstruct or mutate immutable publication identity.
3. Use the publication resource's `reconcile` HAL relation when present. The supported self-hosted
   v1.96.1 profile cannot prove request-hash tags through list/get, so unresolved ambiguity normally
   becomes manual reconciliation.
4. Compare provider evidence and the normalized input hash through approved operator surfaces.
   Never copy payloads, tokens, portal URLs, signatures, or raw provider errors into incident notes.
5. Use `abandon` only when its HAL relation is present and an authorized operator has recorded the
   reason. Abandonment is a terminal administrative decision, not proof that Svix did not accept.

## Credential Rotation

Inside the publication idempotency window:

1. Pause new configuration changes, retain the previous credential version, and install the new
   credential through the server-side resolver.
2. Keep already-materialized publications on their immutable credential reference/version.
3. Validate readiness and a new test publication, then allow new materialization to snapshot the new
   version. Do not rewrite pending publication snapshots.

Outside the idempotency window:

1. Do not retry an uncertain publication with a new identity or credential. Move it to manual
   reconciliation/abandonment using HAL and audit.
2. Rotate the runtime credential for new work, validate readiness, and keep historical evidence
   until its retention cutoff.
3. If the old credential was compromised, revoke it immediately and accept that unresolved old work
   requires operator settlement; security takes precedence over automated replay.

## Endpoint Auto-Pause And Resume

1. Any `endpoint_auto_pauses` increase is an operator signal. Inspect the bounded failure category
   and attempt history through the tenant-authorized endpoint resource.
2. Fix DNS, TLS, timeout, receiver availability, SSRF allow-list, or secret-rotation cause. Never
   lower SSRF controls or expose the signing secret to make a test pass.
3. Use only the endpoint's `resume` HAL relation and expected concurrency version. Resume is audited
   and restores normal scheduler eligibility; it does not synchronously send a webhook.
4. If the endpoint immediately auto-pauses again, leave it paused, contact the tenant owner, and
   investigate before another resume.

## Retention Hold And Cleanup Failure

1. A hold must be created through the governed persistence/application path and include its owner,
   reason, scope, and expiry. Do not extend retention by disabling cleanup globally without an
   incident owner and expiry.
2. Use `WebhookRetention:DryRun=true` to measure eligible categories safely. Repeated values at the
   configured batch size indicate backlog saturation.
3. On partial/failed cleanup, keep delivery running if readiness is otherwise healthy, stop any
   destructive manual SQL, and fix database/tenant-scope/audit persistence first.
4. Resume cleanup with the same bounded tenant rotation. Verify successful passes and decreasing
   dry-run counts before removing the incident.
5. Never delete nonterminal Local work, unknown/manual-reconciliation publications, live provider
   idempotency windows, replay-window inbox rows, or active hold data.

## Migration Forward-Fix And Backup Restore

1. Stop webhook writers and take a verified PostgreSQL backup plus release/migration manifest.
2. Generate migrations only with the repository's `dotnet ef` command line. Never hand-edit a
   generated migration, designer, or model snapshot.
3. Apply the reviewed migration through `Event.MigrationService`/the documented deployment path.
   No network I/O or provider call may run inside a migration.
4. If validation fails before traffic resumes, restore the verified backup into a clean database,
   restore the matching release/secrets, run the migration smoke checks, then reopen traffic.
5. If a defect is found after traffic resumes, stop writers and create a new additive forward-fix
   migration with `dotnet ef`; do not destructively roll back evidence-bearing webhook rows.
6. Require schema signature, migration history, lookup parity, row-count/checksum, readiness, and a
   representative Local/Svix operation before closing the incident.

## Incident Closure Evidence

- Record alert times, bounded metric snapshots, readiness status, selected provider tuple, and the
  authorized audit event IDs in the private incident system.
- Record no payload, secret, signature, portal value, endpoint URL, provider body, or raw error.
- Confirm SLO recovery for one complete alert window and close or time-bound any retention hold.
- Link the verified backup/restore or forward-fix evidence when schema work was involved.

## Related

- [WEBHOOKS.md](WEBHOOKS.md)
- [OPERATIONS.md](OPERATIONS.md)
- [BACKUP_RESTORE_UPGRADE.md](BACKUP_RESTORE_UPGRADE.md)
- [SECRETS.md](SECRETS.md)
- [SECURITY_OVERVIEW.md](SECURITY_OVERVIEW.md)
