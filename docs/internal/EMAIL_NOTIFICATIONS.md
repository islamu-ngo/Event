ABOUTME: Documents implemented SMTP email delivery and its boundary from in-app notifications.
ABOUTME: Prevents unsupported claims about notification fanout, queueing, unsubscribe, or delivery tracking.

# Email And Notifications

> **Audience:** Operators | Admins | Contributors
> **Status:** Mixed
> **Owner:** Platform/Ops
> **Last Verified:** 2026-07-20
> **Source Anchors:** `Explore.Infrastructure/Mail/`, `Explore.Infrastructure/EmailDispatchDrainService.cs`, `Explore.Infrastructure.Tests/Infrastructure/EmailDispatchDrainMailpitTests.cs`, `Explore.Infrastructure.Tests/Infrastructure/SmtpEmailServiceMailpitTests.cs`, `Explore.Persistence/Seed/DatabaseSeeder.cs`, `Explore.AppHost/AppHost.cs`, `docker-compose.yml`, `Explore.Domain/Settings/Definitions/EmailSettingDefinitions.cs`, `Explore.API/HealthChecks/SmtpHealthCheck.cs`, `Explore.Application/Services/EventPublishedNotificationFanoutService.cs`, `Explore.Application/Services/EventModerationNotificationFanoutService.cs`, `docs/CONFIGURATION.md`, `docs/SECRETS.md`

Email transport is implemented through durable `EmailDispatchOutbox` work drained to SMTP. In-app notifications are a separate authenticated inbox feature; heavy moderation is the deliberate exception that materializes required in-app and email-channel siblings through the generic occurrence worker, while light moderation and actor-subscription fanout remain in-app only.

## Approved Lifecycle Expansion (Partially Implemented)

The approved workstream in `dev/active/email-responsibility-architecture/` retains MailKit, Mailpit, and PostgreSQL `EmailDispatchOutbox`. Explicit `NotificationIntent` and per-channel `NotificationDelivery` relationships, atomic recipient materialization, parent-aware email content retention, event/session fanout, reporter consent withdrawal, report receipts, decision-bound reporter outcomes, non-final requests for more information, required heavy-moderation availability delivery, and one approved-registration reminder for the earliest covered published session are implemented. Reminder cancellation, supersession, stale-pointer, and DST/timezone hardening remain staged by the task ledger.

Channel policy is fixed: registration and critical event changes use required in-app plus optional verified/preference-gated email; reporter receipt/outcome and follow-up use separate consent purposes; heavy moderation uses required in-app plus operational email when a current verified address exists; light moderation email stays deferred; reminders use optional in-app plus optional verified/preference-gated email under `ReminderOptional`; managed tenant-administrator invitations retain their authorization-bound destination. A disabled reminder-email preference persists only a typed skipped delivery, never an SMTP outbox, body/address snapshot, or scheduler pointer. Dispatch may narrow the snapshotted policy but never broaden it.

The provider-handoff transition is the suppression fence. Consent, preference, cancellation, and supersession can stop work before it; after it, SMTP/persistence uncertainty is `Unknown`, not an automatic retry or a claim that an in-flight message was recalled. Sent/skipped content redacts after 180 days, unresolved replay material waits for operator resolution, and redacted work is never replayable.

```mermaid
flowchart TD
    subgraph Core["Core Business Transaction"]
        Cmd[Business Command<br/>e.g. RegisterAttendeeCommand] --> DBTx[(PostgreSQL / SQLite<br/>Atomic Database Transaction)]
        DBTx --> State[Entity State Updated<br/>Order / Admission Minted]
        DBTx --> OutboxRow[EmailDispatchOutbox Row Inserted<br/>Status: Pending]
    end

    subgraph Worker["Asynchronous Dispatch Engine"]
        Poller[EmailDispatchWorker Poller] -->|Fetch Pending Rows| OutboxRow
        Poller --> Context[Resolve Tenant Branding & Sender Headers]
        Context --> SMTPConnect[Connect to SMTP Relay via MailKit]
        
        SMTPConnect -->|Success 250 OK| Dispatched[Mark Row: Dispatched<br/>Record Timestamp]
        
        SMTPConnect -->|Transient Failure<br/>Timeout / Rate-Limit| RetryCheck{Retry Count < Max?}
        RetryCheck -->|Yes| Backoff[Exponential Backoff with Jitter<br/>Schedule Next Attempt]
        Backoff --> OutboxRow
        RetryCheck -->|No| DeadLetter[Mark Row: DeadLetter<br/>Emit OpenTelemetry Alert]
    end

    classDef core fill:#eef2ff,stroke:#6366f1,stroke-width:2px;
    classDef engine fill:#f0fdf4,stroke:#22c55e,stroke-width:2px;
    class Core core;
    class Worker engine;
```

## What Is Implemented

| Area | Implemented Behavior |
|---|---|
| SMTP sending | `SmtpEmailService` sends one message through MailKit `SmtpClient` using resolved tenant-aware SMTP configuration. |
| SMTP testing | `SmtpEmailService.TestConnectionAsync` and instance settings endpoints can test SMTP connectivity. |
| Resilience | `EmailResiliencePipelines` retries transient SMTP failures with exponential backoff and jitter. |
| Admin settings | Instance admins can read, update, and test SMTP settings in the instance admin settings surface. |
| Health check | `SmtpHealthCheck` participates in readiness as the `smtp` health check. |
| In-app notifications | Notification controller/client paths handle inbox actions such as read, archive, snooze, and delete separately from SMTP. Heavy moderation creates required sibling in-app/email delivery state; light moderation and actor-subscription fanout remain in-app only. |
| Notification preference matrix | Current-user, organization, and group preference matrices gate non-required in-app fanout and direct email dispatch before provider handoff. |
| Cross-replica dispatch admission | PostgreSQL atomically claims fair tenant rounds, enforces global/per-tenant processing ceilings, persists optional-reminder hysteresis, and reserves global/per-tenant one-minute SMTP buckets for batch and single-pointer drains. Rate deferral precedes attempt/fence creation. |
| Fenced provider settlement | Provider success/failure/reconciliation requires the exact tenant, outbox, processing lease, and attempt and aligns outbox, attempt, receipt, and channel delivery in one transaction. Stale unfenced claims retry; fenced uncertainty becomes `Unknown`. |
| Content retention | The `email-dispatch-retention-cleanup` Quartz job redacts sent/skipped content after the configured 180-day default, waits for unresolved failures to be replayed or explicitly transitioned to `Skipped`, redacts parent/attempt/receipt/delivery content in one transaction, immediately suppresses purged-tenant work, and permanently blocks replay/provider handoff after redaction. |
| Report receipt | Authenticated report intake commits one `report.receipt` intent, required in-app delivery, and optional email outbox or typed skipped email delivery with the report, case, evidence, and provider-sync pointer. Email requires case-update consent, the user-visible trust-safety email preference, a current verified persisted address, and current policy eligibility; dispatch revalidates those authorities before SMTP. Receipt copy is linkless and snapshots the bounded `Reporting:CaseSlaHours` value without evidence, fingerprint, provider, or event-private data. Anonymous reports create no recipient intent because no persisted recipient authority exists. |
| Report outcome | A terminal decision completes only after its durable execution records an exact enforcement receipt. The same serializable transaction closes/actions the case, writes one `report.outcome` intent, creates required linkless in-app delivery, and creates optional `ReportOutcome` email work or a typed skipped email delivery. Case-update consent, active tenant membership, a current verified persisted address, and the trust-safety email preference are revalidated; copy reveals only action/no-action. `Escalate` and `NeedsMoreInfo` remain nonterminal and send no final outcome. |
| Report needs more information | A current `NeedsMoreInfo` decision completes with a nonterminal receipt and atomically moves the case to `WaitingReporter`, completes its execution, and writes one decision-scoped `report.needs-more-information` intent. A persisted non-deleted reporter with active tenant membership is mandatory because in-app delivery is required; if that authority is absent or disappears, business completion fails before `WaitingReporter` and the execution stays resumable in `CompletionPending`. Email uses the distinct `ReportNeedsMoreInformation` kind and is optional under follow-up-contact consent, the trust-safety email preference, and a current verified persisted address. Missing email-only authority produces a typed skipped email delivery without suppressing in-app delivery. Dispatch revalidates follow-up consent before provider handoff, so a later withdrawal skips queued SMTP work. Copy is generic, linkless, and explicitly non-final; it contains no event title, slug, URL, evidence, fingerprint, moderator/provider identity, reason code, or reviewer note, and invents no reply action or response URL. |
| Organizer warning decision | `WarnOrganizer` resolves effective active `EventOwner` assignments and atomically writes a required linkless in-app `report.organizer-warning` plus optional verified/preference-gated `OrganizerNotification` email for each owner before the reporter receives action-taken copy. The warning carries no event/content link; the optional email may still include the standard category unsubscribe control. No owner means the decision remains incomplete and no false reporter outcome is created. |
| Heavy moderation attendee availability | Successful irreversible heavy enforcement writes or reuses one immediate, event-wide `event.moderation.unavailable` occurrence and its PII-free pointer in the same PostgreSQL transaction as the authoritative moderation record. The generic fanout materializes one required linkless in-app delivery and one required email-channel decision per eligible attendee. A current verified persisted address creates `ModerationAvailabilityRequired` SMTP work; a missing or unverified address creates a typed skipped required delivery. Required channels do not consult user notification preferences. Replays reuse the moderation-record/source-decision identity and do not create a second pointer. Light moderation remains in-app only. |

User-facing notification preferences are implemented for the optional dispatch and in-app fanout paths described here. They do not create general notification-to-email fanout: every SMTP delivery still comes from explicit `EmailDispatchOutbox` work, and required heavy-moderation availability bypasses optional preferences by policy.

## SMTP Settings

The email settings model is defined by `EmailSettingDefinitions` and grouped by `EmailSettingGroup`.

| Setting | Purpose |
|---|---|
| `email.smtp_host` | SMTP host. Required with from-address for resolved config. |
| `email.smtp_port` | SMTP port. |
| `email.smtp_security` | SMTP security mode mapped by the SMTP service. |
| `email.from_address` | Default sender address. Required with host for resolved config. |
| `email.from_name` | Default sender display name. |
| `email.smtp_timeout_seconds` | SMTP connection/send timeout. |
| `email.smtp_skip_cert_validation` | Certificate validation bypass for controlled environments only. |
| `smtp.username` | External-authority SMTP username; never a setting value. |
| `smtp.password` | External-authority SMTP password; never a setting value. |

`SmtpConfigResolver` reads the cascading settings model and caches resolved configuration per tenant for five minutes. It returns no SMTP configuration when required values such as host or from-address are missing.

Local Aspire and Compose runs start Mailpit for email capture. Aspire exposes SMTP on `localhost:1025` and the UI at `http://localhost:8025`; Compose containers use SMTP host `mailpit` and port `1025`. Development seeding may set non-secret host/sender governance only; credentials have no database or alias fallback.

## Secret Handling

`smtp.username` and `smtp.password` are external-authority bindings. They are absent
from governance settings, write DTOs, generated clients, logs, exports, support
bundles, and screenshots.

- Use [SECRETS.md](SECRETS.md) for provider setup and redaction expectations.
- Use [CONFIGURATION.md](CONFIGURATION.md) for runtime configuration and persisted settings boundaries.
- Do not document plaintext SMTP credentials in sample files or issue reports.
- If SMTP settings are restored after a backup, validate the connection before reopening workflows that depend on email.

The docs should not claim more about email-secret encryption than the source proves. The implementation marks the credentials sensitive and routes them through the settings/secrets model.

## Sending Boundary

`SmtpEmailService` supports the message shape represented by the infrastructure email models:

- `To`, `Cc`, and `Bcc` recipients.
- `ReplyTo`.
- Custom headers.
- HTML and plain-text body content.
- Attachments and inline images.

The service creates a new SMTP client per send. It does not document a queue, background dispatcher, delivery tracking, bounce processor, or unsubscribe workflow in the SMTP send path.

## Basic Dispatch Test Evidence

Basic Dispatch uses `EmailDispatchDrainService` as the scheduler-neutral boundary. Quartz jobs, the hosted-service fallback, and future transports must delegate to this drain instead of sending SMTP directly from handlers or controllers.

| Behavior | Evidence |
|---|---|
| Direct SMTP provider handoff | `SmtpEmailServiceMailpitTests` sends through MailKit to Mailpit and verifies recipient, sender, subject, text body, HTML body, connection success, and result-field redaction for sentinel body/secret values. |
| SMTP settings resolution | `SmtpConfigResolverTests` verifies tenant `SettingContext` propagation, per-tenant cache separation, missing required settings, defaults, and cache invalidation. |
| Pending outbox drain | `EmailDispatchDrainMailpitTests` starts with a pending `EmailDispatchOutbox`, runs `ProcessBatchAsync`, sends through real SMTP to Mailpit, and records `Sent` outbox state plus succeeded attempt and completed receipt state. |
| Duplicate claim protection | `EmailDispatchDrainMailpitTests` races two `ProcessSingleAsync` consumers for one outbox row and verifies exactly one Mailpit message, one attempt, and one completed receipt. |
| Failure and admission outcomes | `EmailDispatchDrainServiceTests` covers evidence-free rate deferral, cancellation before the provider fence, retry-scheduled SMTP failures, exhausted dead-letter outcomes, timeout-like unknown outcomes, preference skips, and exact settlement calls. `EmailDispatchOutboxTransitionRepositoryTests` covers persisted global/per-tenant admission plus unfenced-versus-fenced stale recovery against PostgreSQL. |
| Tenant pause/resume and operator actions | `EmailDispatchTenantControlRepositoryTests`, `EmailDispatchAdminControllerTests`, and `EmailDispatchAdminHateoasTests` cover PostgreSQL pause/resume state, API problem mapping, write-route policies, and HAL replay/park/resolve-without-replay affordance rules. |
| Scheduler triggers | `EmailDispatchQuartzJobsTests` and `EmailDispatchProcessorTests` prove the Quartz and hosted-service fallback paths call the same scheduler-neutral drain service instead of owning SMTP, RabbitMQ, or payload logic. |
| Readiness states | `EmailDispatchHealthCheckTests` covers Basic Dispatch enabled, intentionally disabled, `Mode=Disabled`, Quartz scheduler disabled, and HostedService states. `EmailDispatchRetentionCleanupHealthCheck` exposes enabled/dry-run retention posture without PII. `EmailDispatchRabbitMqHealthCheckTests` covers RabbitMQ independently from Basic Dispatch. |
| RabbitMQ runtime fixture | `RabbitMqContainerFixtureTests` starts a Testcontainers RabbitMQ management image, verifies an AMQP connection string, and reads bounded management overview diagnostics for later live transport tests. |
| RabbitMQ live topology and publish outcomes | `RabbitMqEmailDispatchTransportLiveTests` enables the real transport against the fixture, declares dispatch/DLX/parking topology, verifies durable direct exchanges and queues through the management API, verifies dispatch queue DLX arguments, confirms readiness is healthy, confirms routable pointer publishes, returns `mandatory_return` for an unbound routing key, and reads the live broker payload to prove only pointer fields are serialized. |
| RabbitMQ live consumer | `RabbitMqEmailDispatchConsumerMailpitTests` starts the manual-ack consumer, publishes a valid pointer, drains the durable outbox through real SMTP to Mailpit, persists `Sent` attempt/receipt state, and waits for RabbitMQ ready/unacknowledged counters to reach zero after the durable outcome. The same class proves malformed JSON and valid pointers without a durable outbox reject to the DLQ without sending Mailpit email. |
| RabbitMQ DLQ replay and parking | `RabbitMqEmailDispatchDeadLetterReplayLiveTests` starts the replay worker, resets a dead-lettered durable row before republishing to the dispatch queue, ACKs the original DLQ delivery, and parks missing-outbox payloads to the parking queue. |
| Browser registration email | `RegistrationFlowTests` clears Mailpit, registers through the Aspire-backed API/BFF/browser stack, waits for the durable outbox row to become `Sent`, verifies attempt/receipt rows, finds the Mailpit message for the registrant, and checks semantic body text plus event title. |

Focused commands:

These commands are focused development lanes, not release evidence by themselves. Lifecycle-email release evidence records each named test class and its exact non-zero count, runs the full affected projects, and runs the explicit `Email` Mailpit lane; a broad OR filter plus `--minimum-expected-tests 1` is not accepted as proof of a new behavior.

```bash
dotnet test --project Explore.Infrastructure.Tests/Explore.Infrastructure.Tests.csproj --configuration Release --verbosity quiet -- --treenode-filter "/*/*/*/*[Category=Email]" --minimum-expected-tests 1
dotnet test --project Explore.Infrastructure.Tests/Explore.Infrastructure.Tests.csproj --configuration Release --verbosity quiet -- --treenode-filter "/*/*/*/*[Category=RabbitMQ]" --minimum-expected-tests 1
dotnet test --project Event.Persistence.IntegrationTests/Event.Persistence.IntegrationTests.csproj --configuration Release --verbosity quiet -- --treenode-filter "/*/*/EmailDispatchTenantControlRepositoryTests/*" --minimum-expected-tests 1
dotnet test --project Event.API.IntegrationTests/Event.API.IntegrationTests.csproj --configuration Release --verbosity quiet -- --treenode-filter "/*/*/EmailDispatchAdminControllerTests/*|/*/*/EmailDispatchAdminHateoasTests/*" --minimum-expected-tests 1
dotnet test --project Event.API.IntegrationTests/Event.API.IntegrationTests.csproj --configuration Release --verbosity quiet -- --treenode-filter "/*/*/EmailDispatchQuartzJobsTests/*|/*/*/EmailDispatchProcessorTests/*" --minimum-expected-tests 1
dotnet test --project Event.API.IntegrationTests/Event.API.IntegrationTests.csproj --configuration Release --verbosity quiet -- --treenode-filter "/*/*/EmailDispatchHealthCheckTests/*" --minimum-expected-tests 1
dotnet test --project Explore.Infrastructure.Tests/Explore.Infrastructure.Tests.csproj --configuration Release --verbosity quiet -- --treenode-filter "/*/*/EmailDispatchRabbitMqHealthCheckTests/*" --minimum-expected-tests 1
dotnet test --project Explore.Infrastructure.Tests/Explore.Infrastructure.Tests.csproj --configuration Release --verbosity quiet -- --treenode-filter "/*/*/RabbitMqContainerFixtureTests/*" --minimum-expected-tests 1
dotnet test --project Explore.Infrastructure.Tests/Explore.Infrastructure.Tests.csproj --configuration Release --verbosity quiet -- --treenode-filter "/*/*/RabbitMqEmailDispatchTransportLiveTests/*" --minimum-expected-tests 1
dotnet test --project Explore.Infrastructure.Tests/Explore.Infrastructure.Tests.csproj --configuration Release --verbosity quiet -- --treenode-filter "/*/*/RabbitMqEmailDispatchConsumerMailpitTests/*" --minimum-expected-tests 1
dotnet test --project Explore.Infrastructure.Tests/Explore.Infrastructure.Tests.csproj --configuration Release --verbosity quiet -- --treenode-filter "/*/*/RabbitMqEmailDispatchDeadLetterReplayLiveTests/*" --minimum-expected-tests 1
```

## Admin Workflow

Instance administrators manage SMTP from the instance settings surface:

1. Open the instance admin settings page.
2. Review SMTP host, port, security, sender, timeout, and credential fields.
3. Save the settings.
4. Run the SMTP connection test.
5. Check readiness health if the connection test or mail send fails.

The admin UI source is `Explore.Blazor.Client/Pages/Admin/Instance/Components/InstanceSmtpSection.razor`, and the API source is `Explore.API/Controllers/InstanceMessagingSettingsController.cs` (SMTP and resolver settings; split from the former `InstanceSettingsController`).

## In-App Notifications Boundary

In-app notifications are implemented through notification controller/client/repository paths, not through SMTP delivery.

| Capability | Boundary |
|---|---|
| Read/archive/snooze/delete notification inbox items | In-app notification feature. |
| SMTP send | `IEmailService` / `SmtpEmailService`. |
| User, organization, and group notification preferences | Implemented as matrix category/channel choices resolved before non-required in-app fanout rows and before direct SMTP provider handoff. |
| Actor-subscription fanout | Implemented only as durable in-app `Notification` row creation through the outbox fanout path, gated by the in-app preference channel before row creation. |
| Event moderation attendee fanout | Light moderation remains durable in-app `Notification` row creation and honors the user-controllable trust-safety preference. Heavy moderation uses the generic occurrence worker and atomic recipient delivery graph for required generic, linkless in-app plus required operational email when a current verified persisted address exists; otherwise the email channel is recorded as a typed required skip. |
| Notification-to-email fanout | There is no implicit conversion of arbitrary inbox rows to email. Heavy moderation explicitly materializes its required email channel into `EmailDispatchOutbox`; light moderation and actor-subscription fanout create no email work. |

`EmailDispatchDrainService` preserves tenant pause, atomic processing claims, persisted rate admission, current eligibility checks, retry/dead-letter handling, and operator park/replay behavior. When the matrix disables a non-required email category, the eligibility transition marks the durable delivery graph `Skipped` with failure category `recipient_notification_preference_disabled` before SMTP handoff. When capacity is exhausted, it instead releases the lease as `smtp_rate_deferred` without incrementing `AttemptCount` or writing attempt/receipt/provider evidence.

Keep the future notifications doc focused on in-app notification behavior when that doc is created, and link back here only for SMTP delivery boundaries.

## Troubleshooting

| Symptom | First Checks |
|---|---|
| SMTP health is degraded or unhealthy | Verify host, port, security mode, credentials, and network reachability. |
| Connection test fails | Run the instance SMTP test and check API logs from `SmtpEmailService.TestConnectionAsync`. |
| Settings update appears ignored | Wait for the resolver cache window or re-run the settings/test path that invalidates SMTP configuration for the current tenant. |
| Sends time out | Check `email.smtp_timeout_seconds`, firewall rules, and provider throttling. |
| TLS/certificate failures | Verify security mode and only use `email.smtp_skip_cert_validation` for controlled non-production scenarios. |
| Local development mail does not arrive | Open Mailpit at `http://localhost:8025` and confirm `email.smtp_host`/`email.smtp_port` resolve to `localhost:1025` for Aspire or `mailpit:1025` for Compose. |
| RabbitMQ dispatch health is unhealthy | Confirm `EmailDispatchRabbitMq:Enabled` is intentional, then check broker connectivity, topology names, parking queue settings, and bounded transport logs. Basic Dispatch can stay healthy without RabbitMQ when broker dispatch is disabled. |
| RabbitMQ DLQ grows | Inspect the HAL-gated EmailDispatch admin status before replay. Malformed or missing durable pointers should park or remain in DLQ evidence; replayable rows must reset durable state before republish. |

Local development seeding can provide Mailpit values when SMTP host is empty or still set to the retired `mailpit.openislamu.org` seed. Treat those as development conveniences, not production defaults.

## Related Documentation

- [CONFIGURATION.md](CONFIGURATION.md) - configuration and settings boundaries.
- [SECRETS.md](SECRETS.md) - secret-provider and sensitive value handling.
- [ADMIN_GUIDE.md](ADMIN_GUIDE.md) - instance admin SMTP workflow.
- [OPERATIONS.md](OPERATIONS.md) - health/readiness context.
- [TROUBLESHOOTING.md](TROUBLESHOOTING.md) - symptom-first operator triage.
