<!-- ABOUTME: Provider-responsibility I-VSD consultation for migrating API-hosted interval queue drains to Quartz.NET. -->
<!-- ABOUTME: Traces truthful operations, ambiguous outcomes, isolation, privacy, autonomy, recovery, resource bounds, and support duties to migration tasks. -->

# I-VSD Provider-Responsibility Consultation: Queue-Driven Worker Migration to Quartz.NET
Last Updated: 2026-08-24

## Action And Context

**Action:** `consultancy-report` with implementation traceability for migrating API-hosted interval queue drains to Quartz.NET.

### Scope

This report reviews the provider-controlled decisions in the three workstream authorities:

- [Implementation plan](../dev/active/queue-driven-worker-migration/queue-driven-worker-migration-plan.md)
- [Resume context](../dev/active/queue-driven-worker-migration/queue-driven-worker-migration-context.md)
- [Task checklist](../dev/active/queue-driven-worker-migration/queue-driven-worker-migration-tasks.md)

The action replaces interval wrappers for registration-provider submission and subscription work, integration sync, webhook delivery/intake/effects/bulk replay/provider publication, and PDS synchronization with payload-free Quartz triggers. Quartz is intended to own cadence only. Existing queues, claims, leases, fences, tenant execution, retries, reconciliation, parking, and dead-letter states remain the correctness authority. `OutboxProcessor` remains the documented hosted-service exception.

This is a design and implementation-readiness consultation, not evidence that the full migration has shipped. Confirmed implementation is 16/39: Task 4.1 RED is independently confirmed and Task 4.2 GREEN is next. Exact final matrices are persistence 2/8, drain/provider 8/14, direct Listmonk 4/5, Task 4.2 boundary 0/5, Task 4.3 cutover 0/4 with zero skips, and current authority 1/1. Six persistence stale/fence and six drain/provider ambiguity/tenant product REDs are observed with no production change. The unkeyed Listmonk POST facts require Task 4.2 to implement an approved explicit non-replay ambiguity/reconciliation classification before stale recovery.

## Claim Boundary

This report is I-VSD provider-responsibility design reasoning and traceability. It is **not a fatwa, Sharia certification, legal opinion, security certification, product or moral certification, or proof of ethical outcomes**. Passing tests would demonstrate specified technical behavior only; they would not prove that no person is harmed, that operations are trustworthy in practice, or that the system has religious or legal status.

No religious ruling is issued. Any future religious-legal classification of this design, an operator action, or its consequences must be routed to qualified Sunni scholars. Privacy, contractual, employment, and incident-notification obligations require qualified counsel in the relevant jurisdictions.

## Findings

| ID | Severity | Finding | Principle / domain | Stakeholders | Provider-controlled decision | Evidence | Mitigation / task |
|---|---|---|---|---|---|---|---|
| F1 | Critical gate | Quartz can replace cadence but cannot make external effects exactly once. Clustering and single-flight do not replace claims, fences, idempotency, or reconciliation. | Trust (`Amanah`), Excellence (`Ihsan`); Technical, Evaluation | Recipients, registrants, consumers, operators | Whether Quartz is cadence or falsely promoted to correctness authority | Plan Decisions A/D; Task 1.1 independently confirmed the production drain/recovery path; official Quartz job/store guidance. | Keep one bounded pass and durable lane authority. Block later cutovers on each phase's claim/fence evidence. |
| F2 | Critical gate | A timeout or crash after provider acceptance can cause duplicate email, publication, or registration submission if retried blindly. | Non-harm (`La Darar`), Trust, Truthfulness (`Sidq`); Technical, Operational | Recipients, registrants, organizers, provider customers | Whether uncertainty is admitted or hidden behind retry | Email uses `Unknown`; publication uses `PublicationUnknown`; registration submissions park ambiguity. Implementation precedents exist. | Preserve these exact states; never auto-replay uncertain handoffs. Tasks 1.1-1.3, 3.1-3.3, and 6.4-6.6. |
| F3 | High | Disabled, paused, standby, or errored scheduling stops claims while durable backlog can grow. Reporting healthy would conceal delayed service. | Truthfulness, Promise-keeping; Operational, Governance | Users, tenants, self-hosters, support | Health semantics, status copy, alerts, backlog visibility | `SchedulerHealthCheck`; Tasks 2.1-2.3 and every lane CUTOVER. Per-target-lane proof is planned, not complete. | Say no drains are claiming and backlog may grow. Keep scheduler posture separate from lane backlog, oldest age, and stale leases. |
| F4 | High | One generic retry/dead-letter policy would erase materially different lane contracts. | Justice (`Adl`), Non-harm; Design, Technical | All lane beneficiaries | Whether convenience overrides lane-specific safety | Plan keeps retry below jobs; repository has Email, integration, webhook, PDS, and submission-specific states. | No scheduler retry and no universal dead-letter abstraction. Preserve attempt ceilings, retryability, parking, dead-letter, reconciliation, and redrive rules. Tasks 3.1-8.3. |
| F5 | Critical gate | Scheduler work is privileged host execution without an end-user tenant context. Broad bypass can cross tenants unless each claim supplies authoritative tenant and fence data. | Rights of people, Justice, Avoiding spying (`Tajassus`); Technical, Governance | Every tenant and data subject | Scope of machine authority and tenant derivation | Workstream requires fresh tenant/machine scopes; current webhook executors are precedents. | Bypass only bounded claim discovery; derive tenant from claim; fresh scope/principal; exact tenant/fence; context cleanup; interleaved-tenant tests. Tasks 3.1-8.3. |
| F6 | High | Quartz, telemetry, health, alerts, and support bundles can become unnecessary secondary stores for payloads or PII. | Privacy, Avoiding spying, Trust; Technical, Operational | Registrants, recipients, webhook subjects, operators | JobDataMap and telemetry content | Empty maps and bounded labels are planned; status/listener and architecture tests provide current precedents. | Exclude payloads, destinations, tenant/user IDs, secrets, provider responses, and raw errors. Tasks 1.1-8.3. |
| F7 | High | Pause/resume and run-now are privileged interventions. Unattributed control or direct SQL weakens accountability and can silently stall queues. | Accountability, Trust, Justice; Governance, Operational | Operators, tenants, affected users | Authorization, affordances, confirmation, audit and retention | Phase 2 real HTTP proof covers administrator success, authenticated non-administrator/anonymous refusal, and unrelated-denial isolation. Documentation requires immutable/tamper-evident WORM-or-equivalent retention, but the deployment backend remains operator-owned and is not evidenced. | Preserve least privilege and audit success/refusal with bounded fields; establish retained audit evidence. Tasks 2.1-2.3 and R.1. |
| F8 | High | Requiring a cloud scheduler, broker, or external control service would reduce small self-hoster autonomy. | Stewardship, Ease (`Taysir`), Trust; Strategic, Technical | Mosques, nonprofits, community operators | Whether local persistent scheduling remains supported | Workstream and selection report specify SQLite/PostgreSQL and no mandatory service. Rehearsal pending. | Keep in-process Quartz and supported primary DB; no mandatory scheduler SaaS, broker, dashboard service, or telemetry backend. Tasks 2.3 and R.1. |
| F9 | Critical gate | Mixed old/new replicas can create two cadence authorities. Rollback before leases expire can repeat effects or accept stale settlement. | Non-harm, Promise-keeping, Excellence; Operational, Evaluation | All queue beneficiaries | Upgrade topology and rollback discipline | Task 3.3 confirmed atomic submission-write transfer: one Quartz trigger, hosted worker removed, exact-fence stale refusal retained. | Preserve coordinated stop, observe active leases, start and verify; use the same sequence for rollback and complete measured R.1 rehearsal. |
| F10 | High | Unbounded catch-up, synchronized misfires, oversized batches, or a noisy lane can exhaust DB connections, scheduler threads, provider quotas, or tenant capacity. | Non-harm, Justice, Stewardship; Technical, Operational | Small and large tenants, providers, operators | Batch, cadence, concurrency, lease, fairness, misfire policy | Plan requires bounded passes and current controls. Load evidence is absent. | One bounded batch; collapse missed interval passes; preserve fairness/rates; expose backlog age and stop criteria. Tasks 3.1-8.3 and R.1. |
| F11 | High | The migration expands support responsibility for backlog, ambiguity, terminal states, pause, capacity, and recovery. Controls without ownership shift harm to users and volunteers. | Mercy (`Rahmah`), Trust, Promise-keeping; Operational, Governance | Users, tenant admins, instance operators, maintainers | Documentation, escalation ownership, support boundary | Operator docs are required, but no complete responsibility matrix or response targets are evidenced. | Carry this report's support split into Task 2.3, every lane CUTOVER, and R.1 before release. |

## Recommendations

### Decision And Hard Gate

**Approve for implementation only under the rewritten workstream's hard, per-slice RED-first gate.** P.1 traceability was accepted on 2026-08-24 through the Senior CTO feedback workflow. Phases 1 and 2 and the Registration Provider Drains phase are GREEN; Task 4.1 RED is independently confirmed at 16/39 and Task 4.2 GREEN is next. The real SQLite/provider/scheduler proofs show stale and malformed `Processing`, id-only settlement, missing claim-derived tenant scope, and an unkeyed Listmonk subscriber-create POST whose identical replay sends two POSTs; HTTP 408, 500, 503, and accepted-before-response-loss transport ambiguity are currently retryable. Task 4.2 must first implement an approved explicit non-replay ambiguity/reconciliation classification; ordinary stale replay is forbidden. Exact Task 4.1 matrices are persistence 2/8, drain/provider 8/14, direct Listmonk 4/5, Task 4.2 0/5, Task 4.3 0/4 with zero skips, and authority 1/1. Six stale/fence and six ambiguity/tenant contracts are intended product REDs, and no production change occurred.

Task 3.3 portable evidence is [`../dev/active/queue-driven-worker-migration/evidence/3.3-cutover.md`](../dev/active/queue-driven-worker-migration/evidence/3.3-cutover.md), SHA-256 `b5ffbf9eee9dd60a34e4e87a037e44948af4f7e03f02008e4b3b9faa5d42453d`; local raw `.omo/start-work/evidence/3.3-cutover.txt` is SHA-256 `9074ce7587338438eb75a03232e77e6c29598e011e9b487e8e07c066d219be5b` and is not claimed portable. Runtime reviewer `st_01a033f6` confirmed all behavioral gates subject only to final status synchronization. Architecture/privacy/operations reviewer `st_01a033f7` confirmed all substantive implementation defects closed and identified portability as the sole remaining blocker; the tracked snapshot resolves it.

Task 3.4 portable evidence is [`../dev/active/queue-driven-worker-migration/evidence/3.4-red.md`](../dev/active/queue-driven-worker-migration/evidence/3.4-red.md), SHA-256 `f6a9dc40237dd553630466fc4c1563b74bbaf9618245ee0802359643c24f52dc`; local raw `.omo/start-work/evidence/3.4-red.txt` is SHA-256 `1b4580453178aefe79a181806e5468ea923012b7921844dae8715af671af5b84` and is not claimed portable. Independent reviewer `st_01a03495` confirmed Task 3.4 RED and the Task 3.5 handoff. AnySearch MCP and Context7 MCP were unavailable; no result from either is claimed.

Task 3.5 portable evidence is [`../dev/active/queue-driven-worker-migration/evidence/3.5-green.md`](../dev/active/queue-driven-worker-migration/evidence/3.5-green.md), SHA-256 `57b3770868f2428220cd2b6e1929feed3eedf36a9194c0ff9a24917b7e08ce4b`; local raw `.omo/start-work/evidence/3.5-green.txt` is SHA-256 `d9980eb6bcc8f46d395f7b0c84f34923c90e76b8729c63dab44554af18b27d2f` and is not claimed portable. Runtime reviewer `st_01a034c5` and architecture reviewer `st_01a034c6` independently confirmed GREEN. AnySearch MCP and Context7 MCP were unavailable; no result from either is claimed.

Task 3.6 portable evidence is [`../dev/active/queue-driven-worker-migration/evidence/3.6-cutover.md`](../dev/active/queue-driven-worker-migration/evidence/3.6-cutover.md), SHA-256 `cc737fe1f16b9a70d57e7042b103197404fa50feaa651705076705b23e5fca5a`; local raw `.omo/start-work/evidence/3.6-cutover.txt` is SHA-256 `b0b8a195b0456a0644d97b4101d3e2b881d5763eb0e0637e9a0aca1adad1ca36` and is not claimed portable. Runtime reviewer `st_01a034fe` and architecture/privacy/operations reviewer `st_01a034ff` independently confirmed CUTOVER. AnySearch MCP and Context7 MCP were unavailable; no result from either is claimed.

Task 4.1 portable evidence is [`../dev/active/queue-driven-worker-migration/evidence/4.1-red.md`](../dev/active/queue-driven-worker-migration/evidence/4.1-red.md), SHA-256 `4558c9a7c87604a515db0f6b191e2089ce51159d758ca91b9f209ecff545ecd1`; local raw `.omo/start-work/evidence/4.1-red.txt` is SHA-256 `5ba2dd9a40084ddc56f223441480c81d17784daaa29a47ab2766a715e95997b0` and is not claimed portable. Independent reviewer `st_01a03554` confirmed the corrected RED, exact matrices, unkeyed protocol facts, no-production-change boundary, required no-skip PostgreSQL/full-host proof, and cleanup. AnySearch MCP and Context7 MCP were unavailable; no result from either is claimed.

After each named RED exists, the smallest production correction may proceed to its stated GREEN, lane health/metrics, privacy/tenant, resource, and rollback gates. Later slices remain blocked by their dependency gates even when Phase 1 is green. The architecture remains acceptable only while Quartz owns cadence, durable lane state remains authoritative, self-hosting remains autonomous, and the requirements below are retained as acceptance evidence.

### Truthful Scheduler Disablement And Backlog

- `Scheduler:Quartz:Enabled=false` means no Quartz queue drains claim work. It is not a harmless preference when queues remain writable.
- Disabled or standby should be `Degraded`; unavailable, shutdown, or trigger error should be `Unhealthy` when scheduling is configured as required.
- A healthy scheduler is not a healthy queue. Each lane must separately report bounded backlog, oldest due age, stale leases, ambiguous/manual-review count, and terminal count where applicable.
- A pause leaves durable queue state unchanged. UI and runbooks must warn that work can continue to arrive and resume can create load.
- Alert copy must identify the lane and safe aggregate consequence without tenant, endpoint, recipient, event, or payload content.

### Duplicate, Ambiguous, Retry, And Dead-Letter Boundaries

| Lane | Outcome | Required post-migration behavior |
|---|---|---|
| Email dispatch | Provider handoff may have occurred but settlement is absent | Persist `Unknown`; do not automatically send again. Reconcile to delivered/not delivered only from controlled evidence. Generic replay excludes `Unknown`. |
| Webhook provider publication | Provider publication may have succeeded but acknowledgement is uncertain | Persist `PublicationUnknown`; run bounded reconciliation; exhausted cases require manual reconciliation. Generic bulk replay excludes uncertain publication. |
| Registration provider submission | Outcome is ambiguous after handoff | Persist `Parked`; require reviewed retry/redrive. Known non-retryable or exhausted safe-before-handoff failure becomes `DeadLettered`. |
| Integration sync | Retryable failure before confirmed effect | Retain its retry schedule and ceiling; dead-letter non-retryable or exhausted work under its own contract. |
| Incoming/local webhook and effects | Durable processing fails | Retain renewable leases, generation/fence, retry, dead-letter, and authorized redrive. Intake and effect outcomes remain distinct. |
| PDS delivery | Claim/provider failure or lease expiry | Retain PDS retryability, dead-letter, and fenced stale-settlement refusal. Quartz does not settle PDS rows. |

Quartz retries are forbidden for these business outcomes. Unexpected job exceptions may reach Quartz telemetry, but durable retries and terminal states remain lane-owned. `[DisallowConcurrentExecution]` reduces overlap for one job key; it is not an exactly-once guarantee.

### Tenant Isolation And Privileged Worker Bypass

Only bounded claim discovery may use host-level cross-tenant authority. For every claim:

1. derive tenant ID and fence from the durable row, never Quartz or external payload data;
2. open a fresh dependency-injection scope;
3. bind that exact tenant and a named machine principal or documented system reason;
4. use normal filters or exact tenant predicates;
5. settle only with matching tenant, claim token, generation, and fence;
6. dispose/clear context before the next claim;
7. emit no tenant ID into Quartz, telemetry, health, logs, or support bundles.

Tests must interleave two tenants, attempt stale settlement, and prove a failed item cannot leak context into the next item.

### Payload And PII Exclusion

Recurring drains use an empty `JobDataMap`. A future one-off wake-up may carry only a bounded opaque pointer after separate review. Quartz and telemetry must exclude:

- message bodies, registration answers, webhook bytes, event content, and provider responses;
- addresses, destination URLs, subjects, handles, tenant/user IDs, and provider message IDs;
- credentials, tokens, signatures, connection strings, and encryption material;
- raw exception messages, payload-bearing stack traces, free-form reasons, and support-ticket text.

Allowed telemetry is closed vocabulary: stable job/group/lane, bounded outcome or failure category, exception type, duration, counts, and aggregate ages. High-cardinality identifiers stay in access-controlled domain records.

### Audited Operator Pause And Resume

- Keep scheduler administration opt-in, authenticated, instance-scoped, rate-limited, HAL-gated, and read-only by default.
- Require typed confirmation for instance-wide pause and deliberate confirmation for future destructive controls.
- Audit successful and refused pause, resume, run-now, reset-error, and interrupt attempts.
- Record actor reference, action, bounded job key, timestamp, result, stable failure code, and correlation ID; never payload or tenant content.
- Establish a retained, access-controlled destination. A logging seam is useful, but an unretained console line is insufficient incident evidence.
- Show paused state, safe backlog/age, and a resume warning. Never offer unbounded catch-up.
- Never instruct operators to edit Quartz or queue tables to pause, unlock, retry, or settle work.

### Self-Hoster Autonomy And No Mandatory External Service

Continue to support in-process Quartz with the local primary database, including SQLite. PostgreSQL clustering is optional for multi-replica deployments, not a prerequisite for basic scheduling. This migration must not require an external scheduler, cloud control plane, broker, proprietary dashboard, or remote telemetry collector.

Self-hosters retain authority to disable, pause, resume, back up, restore, and remove scheduling. The corresponding duty is explicit: disabling drains can delay email, provider publication, submissions, webhooks, integrations, or federation, and the operator owns monitoring and recovery.

### Recovery And Rollback Evidence

For each wave, retain a payload-free rehearsal record:

1. record aggregate starting backlog and oldest age;
2. stop every old replica and prove no old timer authority remains;
3. observe aggregate active claims reach zero; after hard termination, observe every remaining lease become expired and reclaimable. Elapsed time alone is not evidence, and lease-token values are never recorded;
4. start the new release and verify readiness, expected jobs, one bounded pass, exclusive claims, and backlog convergence;
5. terminate one node after claim and verify lease/fence recovery;
6. exercise Email `Unknown`, publication `PublicationUnknown`, submission `Parked`, and lane-specific dead-letter paths without automatic duplicate handoff;
7. pause/resume one lane and the scheduler, verifying audit and bounded catch-up;
8. roll back with the same stop/wait/start discipline and prove stale completion is refused;
9. attach versions, redacted configuration identity, timestamps, aggregate counts, and test/runbook references.

No EF schema change does not by itself make rollback safe; runtime authority overlap and unexpired claims are the primary risks.

### Bounded Resource Use

- One trigger performs one bounded pass and returns; no sleep, poll loop, recursive trigger, or drain-until-empty behavior belongs in a job.
- Keep lane-specific batch, concurrency, timeout, lease, attempt, backoff, fairness, and provider-rate settings.
- Collapse missed interval passes into the next normal pass because the durable queue already stores backlog; do not replay every missed fire.
- Size Quartz and DB pools together and reserve capacity for requests and recovery.
- Define stop/escalate thresholds for oldest age, stale leases, repeated job failure, ambiguous/dead-letter growth, DB saturation, and provider throttling.

## Concrete Implementation Task Traceability

These are acceptance obligations synchronized to the current checklist; they do not authorize production edits.

| Consultation task | Obligation | Workstream task(s) | Evidence |
|---|---|---|---|
| I-VSD-Q1 | Prove accepted-but-unsettled Email becomes `Unknown` with one external call | 1.1-1.3 | Two-node PostgreSQL test, exact call count, durable state, payload-free Quartz rows |
| I-VSD-Q2 | Remove any promise that Quartz owns general outbox correctness | 1.4-1.6 | Catalog/docs absence; `OutboxProcessor` exception remains |
| I-VSD-Q3 | Make global disablement truthful and expose backlog consequence | 2.1-2.3; CUTOVERs 3.3, 3.6, 4.3, 5.3, 5.6, 5.9, 6.3, 6.6, 8.3 | Composition matrix, health result, operator copy, lane backlog evidence |
| I-VSD-Q4 | Preserve submission `Parked`/`DeadLettered` and integration retry/dead-letter rules | 3.1-3.3; 4.1-4.3 | Delegation and state-machine tests; no Quartz retry |
| I-VSD-Q5 | Prove claim-derived tenant execution and context cleanup | 3.1-3.6; 4.1-4.3; 5.1-5.9; 6.1-6.6; 7.1-8.3 | Two-tenant interleaving, fresh scope/principal, exact fence |
| I-VSD-Q6 | Preserve publication `PublicationUnknown`, reconciliation, and replay exclusion | 6.1-6.6 | Unknown/reconciliation/bulk replay tests and bounded signal |
| I-VSD-Q7 | Preserve PDS retry, dead-letter, lease, and stale-fence refusal | 7.1-7.3; 8.1-8.3 | Concurrent claim and stale-settlement tests |
| I-VSD-Q8 | Enforce empty maps and payload/PII-free telemetry | 1.1-1.3; 2.1-2.3; 3.1-8.3 | Architecture scan and telemetry/status assertions |
| I-VSD-Q9 | Audit pause/resume and refused controls with retained evidence | 2.1-2.3; R.1 | Authorized/denied records, retention/access evidence, no payload/tenant content |
| I-VSD-Q10 | Rehearse upgrade, node failure, recovery, pause/resume, and rollback | R.1 | Dated SQLite and two-node PostgreSQL record |
| I-VSD-Q11 | Demonstrate bounded catch-up and resource use | 3.1-8.3; R.1 | Limits, non-overlap, load observations, stop thresholds |
| I-VSD-Q12 | Publish support responsibility and escalation paths | 2.3; CUTOVERs 3.3, 3.6, 4.3, 5.3, 5.6, 5.9, 6.3, 6.6, 7.3, 8.3; R.1 | Runbook ownership matrix and safe diagnostics |

## Common Overlooked Failures And Outcomes

| Overlooked failure | Bad outcome | Control | Positive outcome |
|---|---|---|---|
| Disabled scheduler but green API readiness | Users see “healthy” while work accumulates | Degraded scheduler posture plus lane backlog/age | Operators detect delay before users repeat actions |
| Single-flight treated as exactly once | Crash repeats an accepted external effect | Durable idempotency, fence, unknown/reconciliation | Failover without pretending uncertainty vanished |
| Unknown enters generic replay | Duplicate email, publication, or submission | Exclude unknown/parked from automatic/bulk replay | Evidence is reconciled before remedy |
| One retry/dead-letter policy for all lanes | Retry storms, premature abandonment, erased ambiguity | Lane-owned policies | Failure handling stays proportional to each provider contract |
| Tenant or payload enters Quartz | Scheduler backup becomes sensitive cross-tenant storage | Empty maps and claim-derived tenant | Scheduler operations remain low sensitivity |
| Tenant context survives failure | Next claim runs under the wrong tenant | Fresh scope, `finally` cleanup, exact fence | Privileged execution remains bounded |
| Pause through database edits | Lost audit, broken state, orphaned lease | Authorized audited controls | Operators can explain who acted and outcome |
| Resume launches unbounded catch-up | DB/provider saturation and tenant starvation | Bounded pass, fairness, rate limits | Predictable backlog convergence |
| Mixed-version rolling cutover | Dual cadence authorities and duplicate pressure | Coordinated stop/lease-wait/start | Observable, reversible ownership change |
| Raw queue data copied into support systems | A second privacy incident | Aggregate diagnostics and controlled record access | Useful diagnosis without data spread |

### Positive Outcomes Sought

- Consistent scheduling across standalone SQLite and clustered PostgreSQL without mandatory external infrastructure.
- Truthful visibility into disabled scheduling, delays, stale leases, ambiguity, and terminal failures.
- Fewer private interval loops while retaining mature lane correctness controls.
- Safer failover and rollback because durable queues and fences remain authoritative.
- Lower privacy exposure because Quartz and OpenTelemetry contain bounded operational metadata only.
- Better accountability through audited controls and explicit support ownership.

## Stakeholders

| Stakeholder | Interest | Risk | Provider duty |
|---|---|---|---|
| Recipients and registrants | One truthful outcome | Duplicate, delay, loss, privacy exposure | Preserve ambiguity and support correction without blind replay |
| Organizers | Reliable notifications, registrations, integrations, publication | Hidden failure appears to be organizer failure | Expose bounded status and incident communication |
| Tenant administrators | Isolation and tenant-scoped remedy | Cross-tenant bypass or overbroad control | Tenant-bound execution and no global scheduler authority by convenience |
| Instance operators/self-hosters | Autonomy, controls, recovery, affordable operation | Hidden backlog, overload, unsafe rollback | Local scheduling, runbooks, health, audit, capacity guidance |
| Small mosques/nonprofits | Low infrastructure burden | Enterprise-only dependencies | SQLite/in-process mode and safe defaults |
| External providers/consumers | Idempotent, rate-respecting calls | Duplicate publication/delivery and retry storms | Lane identity, rate limits, reconciliation evidence |
| Federation peers | Bounded PDS delivery | Duplicate records or stale settlement | Preserve PDS lease/fence/dead-letter behavior |
| Maintainers/support | Diagnosable platform | Becoming operator for unrelated deployments | Honest upstream support boundary and upgrade/EOL notices |
| Data subjects/non-users | No unnecessary disclosure | PII in scheduler, telemetry, tickets | Minimization, access control, retention, safe support artifacts |

## I-VSD Principles And Domains

| Principle | Migration consequence | Domains |
|---|---|---|
| Trust (`Amanah`) | Queue state, not scheduler appearance, remains authoritative. | Strategic, Technical, Operational |
| Truthfulness (`Sidq`) | Disabled, paused, unknown, parked, and dead-letter states are named accurately. | Design, Operational, Governance |
| Justice (`Adl`) | Noisy tenants and privileged workers must not consume others' capacity or data rights. | Technical, Governance, Evaluation |
| Non-harm (`La Darar`) | Prevent blind duplicates, retry storms, unsafe rollout, and privacy leakage. | Technical, Operational |
| Rights of people | Tenant and personal data stay out of Quartz/telemetry; operator power is accountable. | Technical, Governance |
| Avoiding spying (`Tajassus`) | Diagnostics collect operational facts, not payload or identities. | Technical, Operational |
| Promise-keeping | Availability, support, pause, and rollback claims match evidence. | Strategic, Operational, Governance |
| Mercy (`Rahmah`) | Delayed or ambiguous outcomes receive escalation and correction paths. | Design, Operational |
| Ease (`Taysir`) | Self-hosters keep a no-mandatory-external-service path. | Strategic, Technical |
| Excellence (`Ihsan`) | Deterministic tests, fencing, bounded passes, and rehearsed recovery replace hope. | Technical, Evaluation |

## Support Responsibilities

| Party | Responsible for | Boundary |
|---|---|---|
| Upstream maintainers | Safe defaults, docs, migration/rollback guidance, notices, tests, issue path | Must not imply unrelated self-hosted instances are monitored, secured, or recovered by ISLAMU |
| Official hosted operator | Capacity, alerting, audit retention, backups, on-call, incidents, reconciliation, user remedy | Provider dashboards and green tests do not prove user receipt or correct outcomes |
| Independent self-hoster | Configuration, DB, credentials, monitoring, backup/restore, upgrades, audit destination, provider contracts, backlog/dead-letter review, local support | Responsibility cannot be delegated to Quartz or upstream merely by using the software |
| Tenant administrator | Tenant-scoped inspection, authorized redrive, organizer/user communication, escalation | No instance-wide scheduler control or cross-tenant payload access by convenience |
| External provider | Provider-side status and reconciliation evidence under contract | Acceptance does not prove local settlement, receipt, or ethical outcome |

Runbooks must name an owner and escalation route for scheduler unavailable, prolonged pause, backlog, stale lease, repeated failure, Email `Unknown`, publication `PublicationUnknown`, submission `Parked`, dead-letter growth, suspected cross-tenant execution, telemetry leakage, failed rollback, and data restoration. Do not promise response times until staffing and evidence support them.

## Rejected Alternatives

1. **Keep all interval loops** — rejected because lifecycle and multi-node behavior remain fragmented; preserve queue logic, not private timers.
2. **Make Quartz the queue and carry payloads** — rejected because it duplicates sensitive state and confuses cadence with correctness.
3. **One retry/dead-letter policy** — rejected because lane-safe outcomes differ.
4. **Automatically retry unknown outcomes** — rejected because uncertainty becomes likely duplication.
5. **Treat clustering or single-flight as exactly once** — rejected because external acceptance and local settlement are not atomic.
6. **Mixed-version rolling cutover** — rejected because old loops and Quartz can be simultaneous authorities.
7. **Healthy-disabled scheduler** — rejected because intent does not prevent user-visible delay.
8. **Mandatory cloud scheduler, broker, or proprietary dashboard** — rejected because it weakens self-hoster autonomy.
9. **Direct SQL controls** — rejected because they bypass authorization, audit, state transitions, and fencing.
10. **Tenant/provider identifiers in telemetry** — rejected because search convenience does not justify sensitive high-cardinality data.

## Validation Plan

1. Run exactly one Release build and at most the one selected test-project command named by each phase; task-level focused RED/GREEN commands remain narrower evidence and never become a post-hoc multi-project megaphase.
2. Await exact job completion or durable state transitions with bounded timeouts; do not use fixed sleeps or timing-luck polling.
3. Prove the Email accepted-but-unsettled window with two PostgreSQL scheduler nodes and one provider call.
4. Per lane, prove one bounded delegation, no Quartz retry, non-overlap, exception propagation, and old worker absence.
5. Test retryable, non-retryable, exhausted, ambiguous, expired-lease, stale-fence, cancellation, and authorized-redrive outcomes.
6. Interleave two tenants and assert Quartz, health, metrics, logs, and traces contain no tenant or payload markers.
7. Exercise disabled, global and job pause/resume, error/reset, run-now, and interrupt through the real authorized surface; verify success and refusal audits.
8. Rehearse SQLite and two-node PostgreSQL startup, node loss, recovery, coordinated upgrade, and rollback.
9. Observe DB/Quartz pool use, provider throttling, pass duration, oldest age, and fairness under representative backlog.
10. Review runbooks with assigned operators/support staff and record real feedback later; do not invent interviews or approvals.

## Validation Gaps

- The synchronized workstream records show P.1 complete and 16/39 implementation tasks complete. Task 4.1 RED is independently confirmed with the exact final matrices above. This is not GREEN evidence for Tasks 4.2-8.3 or R.1.
- Phase 2 is GREEN: RequestLoggingPrivacyTests 2/2, focused scheduler audit/privacy 1/1, QuartzSchedulerCompositionTests 7/7, SchedulerHealthCheckTests 6/6, ApiLiabilityRatchetTests 15/15, Release build 0 errors/305 warnings, and diff check passed. Real concurrent PostgreSQL reconciliation/foreign-key/idempotency and real HTTP administrator/non-administrator/anonymous plus unrelated-denial isolation were confirmed by two independent reviews; logs, scopes, and metrics were canary-free.
- Generic Quartz trigger acquisition does not prove later lane-specific duplicate prevention.
- No dated final SQLite/PostgreSQL cutover and rollback rehearsal was found for this workstream.
- No representative load evidence establishes safe batch, concurrency, pool, or provider-rate defaults.
- Phase 2 documentation requires immutable/tamper-evident WORM-or-equivalent retained audit storage, but deployment-specific backend configuration, access review, tamper-resistance operation, and retrieval remain operator-owned and are not evidenced.
- No production support rota, response targets, escalation contacts, or incident examples were reviewed.
- No stakeholder interviews were conducted or represented.
- Production logs, provider accounts, raw payloads, secrets, PII, support tickets, and audit exports were not inspected.

## Escalation Needed

- **Operations/security:** stop release if duplicate/ambiguity, tenant isolation, payload exclusion, audit retention, or rollback evidence is absent.
- **Privacy/legal:** obtain counsel for telemetry and audit retention, support access, provider data, incident notification, and deployment responsibilities.
- **Provider specialists:** escalate sustained `Unknown`, `PublicationUnknown`, `Parked`, or contradictory provider evidence; never resolve from Quartz state alone.
- **Governance:** escalate support promises, prolonged pauses, unresolved harm, and capacity policies that systematically disadvantage smaller tenants.
- **Religious-legal:** any request to label the design or operation halal, haram, wajib, makruh, Sharia-compliant, or otherwise religiously classified goes to qualified Sunni scholars. This report supplies technical facts and stakeholder risks only.

## Evidence Reviewed

### Repository Evidence

| Path | Evidence used |
|---|---|
| [`dev/active/queue-driven-worker-migration/queue-driven-worker-migration-plan.md`](../dev/active/queue-driven-worker-migration/queue-driven-worker-migration-plan.md) | Scope, cadence-only architecture, safety gates, cutover, resource bounds, rollback |
| [`dev/active/queue-driven-worker-migration/queue-driven-worker-migration-context.md`](../dev/active/queue-driven-worker-migration/queue-driven-worker-migration-context.md) | Status, blocker, lane roster, resume evidence |
| [`dev/active/queue-driven-worker-migration/queue-driven-worker-migration-tasks.md`](../dev/active/queue-driven-worker-migration/queue-driven-worker-migration-tasks.md) | Task IDs, acceptance, verification |
| [`dev/active/queue-driven-worker-migration/evidence/3.3-cutover.md`](../dev/active/queue-driven-worker-migration/evidence/3.3-cutover.md) | Portable Task 3.3 raw evidence snapshot, digest, and dual reviewer confirmations; SHA-256 `b5ffbf9eee9dd60a34e4e87a037e44948af4f7e03f02008e4b3b9faa5d42453d` |
| [`dev/active/queue-driven-worker-migration/evidence/3.4-red.md`](../dev/active/queue-driven-worker-migration/evidence/3.4-red.md) | Portable exact Task 3.4 RED evidence, raw digest, and `st_01a03495` confirmation; SHA-256 `f6a9dc40237dd553630466fc4c1563b74bbaf9618245ee0802359643c24f52dc` |
| [`dev/active/queue-driven-worker-migration/evidence/3.5-green.md`](../dev/active/queue-driven-worker-migration/evidence/3.5-green.md) | Portable exact Task 3.5 GREEN evidence, raw digest, and `st_01a034c5`/`st_01a034c6` confirmations; SHA-256 `57b3770868f2428220cd2b6e1929feed3eedf36a9194c0ff9a24917b7e08ce4b` |
| [`dev/active/queue-driven-worker-migration/evidence/3.6-cutover.md`](../dev/active/queue-driven-worker-migration/evidence/3.6-cutover.md) | Portable exact Task 3.6 CUTOVER evidence, raw digest, and `st_01a034fe`/`st_01a034ff` confirmations; SHA-256 `cc737fe1f16b9a70d57e7042b103197404fa50feaa651705076705b23e5fca5a` |
| [`dev/active/queue-driven-worker-migration/evidence/4.1-red.md`](../dev/active/queue-driven-worker-migration/evidence/4.1-red.md) | Portable exact Task 4.1 RED evidence, raw digest, and `st_01a03554` confirmation; SHA-256 `4558c9a7c87604a515db0f6b191e2089ce51159d758ca91b9f209ecff545ecd1` |
| `dev/report/quartznet-background-jobs-selection-report.md` | Self-hosting rationale and alternatives |
| `dev/report/quartznet-background-jobs-implementation-report.md` | Quartz boundary, payload limits, caveats |
| `docs/OPERATIONS.md`, `docs/CONFIGURATION.md`, `docs/OUTBOX_PATTERN.md`, `docs/SELF_HOSTING.md` | Operational, configuration, durable-intent, and autonomy evidence |
| `src/Explore.API/HealthChecks/SchedulerHealthCheck.cs` | Disabled/standby/error truthfulness |
| `src/Explore.API/Controllers/SchedulerAdminController.cs` | Authorized controls and audit invocation |
| `src/Explore.API/Scheduling/QuartzSchedulerOperations.cs` | Pause/resume/run/reset/interrupt and safe projections |
| `src/Explore.Application/Contracts/Scheduling/ISchedulerAdminAuditSink.cs` | Bounded audit contract and logging boundary |
| `src/Explore.API/Scheduling/SchedulerTelemetryJobListener.cs`, `QuartzSchedulerStatusEndpoint.cs` | Bounded telemetry/status without data-map exposure |
| `tests/Event.Architecture.Tests/DurableSideEffectBoundaryTests.cs` | Sensitive scheduler-payload guardrail |
| `src/Explore.Infrastructure/EmailDispatchDrainService.cs` | Email retry, dead-letter, parked, and `Unknown` |
| `src/Explore.Infrastructure/Webhooks/WebhookProviderPublicationDrainService.cs` | `PublicationUnknown` and reconciliation |
| `src/Explore.Application/Services/Registration/RegistrationProviderSubmissionWriteEffectService.cs` | Submission parking, retry, and dead-letter |
| `src/Explore.Infrastructure/Webhooks/IncomingWebhookTenantExecutor.cs`, `IncomingWebhookEffectTenantExecutor.cs` | Fresh tenant/machine scopes |

No production code, migration, SQL, test, configuration, or runbook file was changed by this consultation.

Research disclosure: AnySearch MCP and Context7 MCP were unavailable; no result from either tool is claimed.

### Official External Functional Evidence

AnySearch MCP and Context7 MCP were **not exposed in this session**. The fallback used direct official-source URLs only; no community article or third-party implementation was used.

| Official source | Functional fact used | Accessed |
|---|---|---|
| [Quartz.NET: Job Stores](https://www.quartz-scheduler.net/documentation/quartz-3.x/tutorial/job-stores.html) | Persistent and in-memory stores have different durability/operations | 2026-08-24 |
| [Quartz.NET: More About Jobs](https://www.quartz-scheduler.net/documentation/quartz-3.x/tutorial/more-about-jobs.html) | Job identity and non-concurrency are scheduler mechanisms, not external settlement | 2026-08-24 |
| [Quartz.NET: More About Triggers](https://www.quartz-scheduler.net/documentation/quartz-3.x/tutorial/more-about-triggers.html) | Trigger and misfire behavior require explicit choice | 2026-08-24 |
| [Quartz.NET: ASP.NET Core Integration](https://www.quartz-scheduler.net/documentation/quartz-3.x/packages/aspnet-core-integration.html) | Quartz can follow in-process ASP.NET Core host lifecycle | 2026-08-24 |
| [Microsoft: Background tasks with hosted services](https://learn.microsoft.com/en-us/aspnet/core/fundamentals/host/hosted-services?view=aspnetcore-10.0) | Host lifetime, cancellation, and scope behavior remain explicit concerns | 2026-08-24 |
| [Microsoft: Data redaction in .NET](https://learn.microsoft.com/en-us/dotnet/core/extensions/data-redaction) | Sensitive telemetry needs classification/redaction; omission is safer when unnecessary | 2026-08-24 |
| [OpenTelemetry: Handling sensitive data](https://opentelemetry.io/docs/security/handling-sensitive-data/) | Collection/export/storage can expose sensitive telemetry; minimize before export | 2026-08-24 |
| [OpenTelemetry: Metrics semantic conventions](https://opentelemetry.io/docs/specs/semconv/general/metrics/) | Use stable dimensions, not payload-derived attributes | 2026-08-24 |

Clean-room boundary: official documentation supplied functional constraints only. No third-party code, SQL, tests, snippets, schemas, migrations, or expressive implementation sequence was copied.

## Missing Evidence

- GREEN retained artifacts for workstream Tasks 4.2-8.3 and release-owner gate R.1. Task 4.1 RED portable evidence is retained at `dev/active/queue-driven-worker-migration/evidence/4.1-red.md`; local `.omo` evidence is digest-bound but not claimed portable.
- Exact target registrations, empty maps, and old-worker absence after each phase.
- A signed-off lane matrix of retry ceilings, backoff, dead-letter, parking, reconciliation, redrive, and health thresholds.
- Production-like clustering, network interruption, DB failover, provider timeout, and shutdown evidence.
- Audit retention/access/tamper controls and incident retrieval without payload or tenant content.
- Capacity/fairness evidence for small standalone and clustered deployments.
- A release-runbook support matrix with real owners and contact routes.
- Feedback from actual self-hosters and people affected by delayed, duplicate, parked, unknown, or dead-letter outcomes.
- Legal/privacy review and any future qualified Sunni scholarly response. Their absence blocks those claims, not this technical consultation.

## Context Inventory

| Dimension | Reviewed assumption or boundary |
|---|---|
| Provider role | Maintainers choose software defaults/docs; each operator owns its deployment |
| Product | Open-source, self-hostable event platform with durable queue side effects |
| Objective | Replace interval cadence wrappers while preserving durable processing semantics |
| Scheduler authority | `Scheduler:Quartz:Enabled`; Quartz owns cadence only |
| Durable authority | Lane state machines own claims, leases, fences, retries, ambiguity, settlement, parking, dead-letter, redrive |
| Lanes | Registration submission/subscription, integration, webhook delivery/intake/effects/replay/publication, PDS |
| Exception | `OutboxProcessor` remains hosted-service authority |
| Deployment | SQLite standalone and PostgreSQL clustered; no mandatory external scheduler |
| Tenancy | Privileged claim discovery then exact claim-derived tenant execution |
| Sensitive data | Payload/PII excluded from Quartz and telemetry |
| Consistency | At-least-once attempts with lane idempotency/reconciliation; no exactly-once claim |
| Controls | Authorized and audited instance/job pause/resume and related actions |
| Rollout | Coordinated stop, lease wait, start, verify; reverse for rollback |
| Resources | One bounded pass; lane-specific limits; no unbounded catch-up |
| Support | Upstream supports software; operators own monitoring, recovery, providers, local users |
| Lifecycle | 16/39 implementation tasks confirmed; Task 4.1 RED confirmed; Task 4.2 GREEN next; Quartz remains the sole current IntegrationSync hosted cadence authority |
| Research tooling | AnySearch MCP and Context7 MCP unavailable; official-source fallback only |
| Cumulative architecture repair | `SchedulerAdminAuthorizationPolicy` is the sole instance-admin policy authority; no `QuartzSchedulerSettings.InstanceAdminPolicyName` or Quartz-coupled `SchedulerAdminController` remains |
| Religious boundary | No ruling; future classification goes to qualified Sunni scholars |
