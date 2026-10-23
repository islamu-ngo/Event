using Event.Persistence.IntegrationTests.Fixtures;
using Explore.Application.Contracts.Infrastructure;
using Explore.Application.Contracts.Persistence;
using Explore.Application.Contracts.Webhooks;
using Explore.Application.DTOs.EventReporting;
using Explore.Application.Features.EventReporting;
using Explore.Application.Features.EventReporting.Handlers.Commands;
using Explore.Application.Features.EventReporting.Requests.Commands;
using Explore.Application.Serialization;
using Explore.Application.Services.Webhooks;
using System.Security.Cryptography;
using System.Text.Json;
using Explore.Domain;
using Explore.Domain.Enums;
using Explore.Persistence;
using Explore.Persistence.Repositories;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Options;
using TUnit.Core;

namespace Event.Persistence.IntegrationTests.Repositories;

[ClassDataSource<PostgreSqlContainerFixture>(Shared = SharedType.PerAssembly)]
[NotInParallel("PersistenceDb")]
public sealed partial class EventReportPersistenceTests(PostgreSqlContainerFixture fixture)
{
    [Test]
    [Arguments("approve", EventReportRecommendedAction.Dismiss)]
    [Arguments("heavy_redact", EventReportRecommendedAction.HeavyRedact)]
    [Arguments("global_ban", EventReportRecommendedAction.Escalate)]
    public async Task CoopAdvice_PreservesLocalDecisionAndEnforcement_WithExactReplay(
        string action, EventReportRecommendedAction recommendation)
    {
        await fixture.ResetAsync();
        await using var context = fixture.CreateDbContext();
        var (tenant, @event, user, actor) = await SetupEventAsync(context, "coop-advice");
        var report = CreateReport(tenant.Id, @event.Id, user.Id, actor.Id);
        var reportCase = EventReportCase.Create(tenant.Id, report.Id, "default", EventReportPriority.Normal, null);
        var localDecision = EventReportDecision.Create(tenant.Id, reportCase.Id, report.Id,
            EventReportDecisionSource.LocalModerator, EventReportDecisionKind.NeedsMoreInfo,
            "needs_context", null, user.Id, null);
        reportCase.SelectDecision(localDecision, AdvisoryObservedAt);
        var link = CreateAdviceLink(report, reportCase, EventReportExternalProvider.Coop);
        var callback = CreateCoopAdvice(report, reportCase, action);
        var message = CreateAdviceMessage(callback);
        var pointer = IncomingWebhookEffectOutbox.CreatePending(tenant.Id, message.Id, "coop",
            callback.ProviderDecisionId!, CoopDecisionIncomingWebhookHandler.StableEffectKind,
            message.PayloadHash, AdvisoryObservedAt);
        var leaseToken = Guid.CreateVersion7();
        pointer.Claim("advisory-worker", leaseToken, AdvisoryObservedAt.AddMinutes(5), AdvisoryObservedAt);
        context.EventReports.Add(report);
        context.EventReportCases.Add(reportCase);
        context.EventReportDecisions.Add(localDecision);
        context.EventReportExternalLinks.Add(link);
        context.IncomingWebhookMessages.Add(message);
        context.IncomingWebhookEffectOutboxes.Add(pointer);
        await context.SaveChangesAsync();
        var caseStamp = reportCase.ConcurrencyStamp;
        var reportStatus = report.Status;
        var eventStatus = @event.EventStatusId;
        var enforcementCount = await context.EventModerationRecords.CountAsync();
        var outboxCount = await context.OutboxMessages.CountAsync();
        var handler = CreateCoopAdviceHandler(context, tenant.Id);
        var command = new ProcessCoopDecisionCallbackCommand { Request = callback, IncomingWebhookMessageId = message.Id };

        var first = await handler.ExecuteAsync(command, CancellationToken.None);
        // Recover the real advisory write after a worker stops before effect settlement.
        var service = new IncomingWebhookEffectProcessingService(
            new IncomingWebhookEffectOutboxRepository(context),
            new IncomingWebhookMessageRepository(context),
            new IncomingWebhookEffectReceiptRepository(context),
            new EfCoreUnitOfWork(context), handler,
            Options.Create(new IncomingWebhookProcessingSettings()),
            new AdvisoryTimeProvider());
        var effect = await service.ProcessAsync(new IncomingWebhookEffectClaim(pointer.Id, tenant.Id,
            leaseToken, pointer.ProcessingFence, pointer.ProcessingGeneration), CancellationToken.None);
        var replay = await handler.ExecuteAsync(command, CancellationToken.None);

        await Assert.That(first.IsSuccess).IsTrue();
        await Assert.That(replay.IsSuccess).IsTrue();
        await Assert.That(replay.Id).IsEqualTo(first.Id);
        await Assert.That(effect.Outcome).IsEqualTo(IncomingWebhookClaimExecutionOutcome.Completed);
        context.ChangeTracker.Clear();
        var retained = await new EventReportRepository(context).GetByIdAsync(tenant.Id, report.Id, CancellationToken.None);
        var advice = retained!.Signals.Single();
        await Assert.That(advice.RecommendedAction).IsEqualTo(recommendation);
        await Assert.That(advice.SignalType).IsEqualTo(action);
        await Assert.That(advice.CaseId).IsEqualTo(reportCase.Id);
        await Assert.That(advice.ProviderLinkId).IsEqualTo(link.Id);
        await Assert.That(advice.IncomingWebhookMessageId).IsEqualTo(message.Id);
        await Assert.That(advice.AdvisoryMaterialSha256).IsEqualTo(message.PayloadHash);
        await Assert.That(retained.Status).IsEqualTo(reportStatus);
        await Assert.That(retained.Cases.Single().ConcurrencyStamp).IsEqualTo(caseStamp);
        await Assert.That(retained.Cases.Single().Status).IsEqualTo(EventReportCaseStatus.DecisionReady);
        await Assert.That(retained.Cases.Single().CurrentDecisionId).IsEqualTo(localDecision.Id);
        await Assert.That(retained.Decisions.Count).IsEqualTo(1);
        await Assert.That(retained.Decisions.Single().Execution.State).IsEqualTo(EventReportDecisionExecutionState.Requested);
        await Assert.That(await context.EventModerationRecords.CountAsync()).IsEqualTo(enforcementCount);
        await Assert.That(await context.OutboxMessages.CountAsync()).IsEqualTo(outboxCount);
        var settledPointer = await context.IncomingWebhookEffectOutboxes.AsNoTracking().SingleAsync(candidate => candidate.Id == pointer.Id);
        var receipt = await context.IncomingWebhookEffectReceipts.AsNoTracking().SingleAsync(candidate => candidate.IncomingWebhookMessageId == message.Id);
        await Assert.That(settledPointer.Status).IsEqualTo(OutboxMessageStatus.Completed);
        await Assert.That(receipt.PayloadHash).IsEqualTo(message.PayloadHash);
        await Assert.That(receipt.EffectKind).IsEqualTo(CoopDecisionIncomingWebhookHandler.StableEffectKind);
        await Assert.That((await context.Events.SingleAsync(candidate => candidate.Id == @event.Id)).EventStatusId).IsEqualTo(eventStatus);
    }

    [Test]
    [Arguments("instance", "instance")]
    [Arguments("tenant", "another-tenant")]
    public async Task CoopAdvice_ForgedDispatchScope_IsRejectedWithoutAuthorityWrites(string scope, string target)
    {
        await fixture.ResetAsync();
        await using var context = fixture.CreateDbContext();
        var (tenant, @event, user, actor) = await SetupEventAsync(context, "coop-forged");
        var report = CreateReport(tenant.Id, @event.Id, user.Id, actor.Id);
        var reportCase = EventReportCase.Create(tenant.Id, report.Id, "default", EventReportPriority.Normal, null);
        var link = CreateAdviceLink(report, reportCase, EventReportExternalProvider.Coop);
        var callback = CreateCoopAdvice(report, reportCase, "heavy_redact") with
        {
            ProviderTargetScope = scope, ProviderTargetId = target
        };
        var message = CreateAdviceMessage(callback);
        context.EventReports.Add(report);
        context.EventReportCases.Add(reportCase);
        context.EventReportExternalLinks.Add(link);
        context.IncomingWebhookMessages.Add(message);
        await context.SaveChangesAsync();
        var stamp = reportCase.ConcurrencyStamp;

        var result = await CreateCoopAdviceHandler(context, tenant.Id).ExecuteAsync(
            new ProcessCoopDecisionCallbackCommand { Request = callback, IncomingWebhookMessageId = message.Id },
            CancellationToken.None);

        await Assert.That(result.IsSuccess).IsFalse();
        await Assert.That(result.FailureCode).IsEqualTo(EventReportFailureCodes.ValidationFailed);
        context.ChangeTracker.Clear();
        var retained = await new EventReportRepository(context).GetByIdAsync(tenant.Id, report.Id, CancellationToken.None);
        await Assert.That(retained!.Signals.Count).IsEqualTo(0);
        await Assert.That(retained.Decisions.Count).IsEqualTo(0);
        await Assert.That(retained.Cases.Single().CurrentDecisionId).IsNull();
        await Assert.That(retained.Cases.Single().ConcurrencyStamp).IsEqualTo(stamp);
        await Assert.That(retained.Cases.Single().Status).IsEqualTo(EventReportCaseStatus.Open);
        await Assert.That(await context.EventModerationRecords.CountAsync()).IsEqualTo(0);
    }

    [Test]
    public async Task CoopAdvice_ChangedAuthenticatedMaterialOrForgedEnvelope_PreservesOriginalAdvice()
    {
        await fixture.ResetAsync();
        await using var context = fixture.CreateDbContext();
        var (tenant, @event, user, actor) = await SetupEventAsync(context, "coop-material");
        var report = CreateReport(tenant.Id, @event.Id, user.Id, actor.Id);
        var reportCase = EventReportCase.Create(tenant.Id, report.Id, "default", EventReportPriority.Normal, null);
        var callback = CreateCoopAdvice(report, reportCase, "approve");
        var message = CreateAdviceMessage(callback);
        context.EventReports.Add(report);
        context.EventReportCases.Add(reportCase);
        context.EventReportExternalLinks.Add(CreateAdviceLink(report, reportCase, EventReportExternalProvider.Coop));
        context.IncomingWebhookMessages.Add(message);
        await context.SaveChangesAsync();
        var handler = CreateCoopAdviceHandler(context, tenant.Id);
        var command = new ProcessCoopDecisionCallbackCommand { Request = callback, IncomingWebhookMessageId = message.Id };
        var first = await handler.ExecuteAsync(command, CancellationToken.None);
        var forged = await handler.ExecuteAsync(command with { IncomingWebhookMessageId = Guid.CreateVersion7() }, CancellationToken.None);
        var changed = await handler.ExecuteAsync(command with { Request = callback with { SafeNote = "replacement material" } }, CancellationToken.None);
        message.ClassifyDuplicate("sha256:" + new string('b', 64), AdvisoryObservedAt.AddMinutes(1));
        await context.SaveChangesAsync();
        var conflicted = await handler.ExecuteAsync(command, CancellationToken.None);

        await Assert.That(first.IsSuccess).IsTrue();
        await Assert.That(forged.IsSuccess).IsFalse();
        await Assert.That(changed.IsSuccess).IsFalse();
        await Assert.That(conflicted.IsSuccess).IsFalse();
        var retained = await context.EventReportSignals.AsNoTracking().SingleAsync(signal => signal.ReportId == report.Id);
        await Assert.That(retained.Id).IsEqualTo(first.Id);
        await Assert.That(retained.SafeSummary).IsEqualTo(callback.SafeNote);
        await Assert.That(retained.AdvisoryMaterialSha256).IsEqualTo(message.PayloadHash);
        await Assert.That(await context.EventReportDecisions.CountAsync()).IsEqualTo(0);
    }

    [Test]
    public async Task OspreyAdvice_ExactReplayAndChangedMaterial_PreserveOriginalHumanReviewState()
    {
        await fixture.ResetAsync();
        await using var context = fixture.CreateDbContext();
        var (tenant, @event, user, actor) = await SetupEventAsync(context, "osprey-advice");
        var report = CreateReport(tenant.Id, @event.Id, user.Id, actor.Id);
        var reportCase = EventReportCase.Create(tenant.Id, report.Id, "default", EventReportPriority.Normal, null);
        var link = CreateAdviceLink(report, reportCase, EventReportExternalProvider.Osprey);
        context.EventReports.Add(report);
        context.EventReportCases.Add(reportCase);
        context.EventReportExternalLinks.Add(link);
        await context.SaveChangesAsync();
        var callback = new OspreySignalCallbackRequestDto
        {
            TenantId = tenant.Id, EventId = @event.Id, ReportId = report.Id, CaseId = reportCase.Id,
            ProviderSignalId = "provider-signal", CorrelationId = "dispatch",
            ProviderTargetScope = "tenant", ProviderTargetId = tenant.Id.ToString("N"),
            Signals =
            [
                new OspreySignalCallbackItemDto
                {
                    SignalType = "policy_match", PolicyCode = "spam", Verdict = "urgent",
                    RecommendedAction = "heavy_redact", SafeSummary = "Advisory evidence.", CreatedAtUtc = AdvisoryObservedAt
                },
                new OspreySignalCallbackItemDto
                {
                    SignalType = "context_match", PolicyCode = "context", Verdict = "needs_review",
                    SafeSummary = "Separate material fact.", CreatedAtUtc = AdvisoryObservedAt
                }
            ]
        };
        var handler = new RecordOspreySignalCallbackCommandHandler(
            new EventReportRepository(context), new EfCoreUnitOfWork(context), new StaticTenantContext(tenant.Id));
        var command = new RecordOspreySignalCallbackCommand { Request = callback };
        var first = await handler.ExecuteAsync(command, CancellationToken.None);
        var replay = await handler.ExecuteAsync(command, CancellationToken.None);
        var changed = await handler.ExecuteAsync(command with
        {
            Request = callback with
            {
                Signals = [callback.Signals[0] with { SafeSummary = "Changed evidence." }, callback.Signals[1]]
            }
        }, CancellationToken.None);
        var forged = await handler.ExecuteAsync(command with
        {
            Request = callback with { ProviderTargetScope = "instance", ProviderTargetId = "instance" }
        }, CancellationToken.None);
        var foreignTenant = await handler.ExecuteAsync(command with
        {
            Request = callback with { TenantId = Guid.CreateVersion7() }
        }, CancellationToken.None);

        await Assert.That(first.IsSuccess).IsTrue();
        await Assert.That(replay.IsSuccess).IsTrue();
        await Assert.That(changed.IsSuccess).IsFalse();
        await Assert.That(forged.IsSuccess).IsFalse();
        await Assert.That(foreignTenant.IsSuccess).IsFalse();
        await Assert.That(foreignTenant.FailureCode).IsEqualTo(EventReportFailureCodes.TenantUnresolved);
        context.ChangeTracker.Clear();
        var retained = await new EventReportRepository(context).GetByIdAsync(tenant.Id, report.Id, CancellationToken.None);
        await Assert.That(retained!.Signals.Count).IsEqualTo(2);
        var originals = retained.Signals.OrderBy(signal => signal.AdvisoryOrdinal).ToArray();
        await Assert.That(originals[0].SafeSummary).IsEqualTo("Advisory evidence.");
        await Assert.That(originals[1].SafeSummary).IsEqualTo("Separate material fact.");
        foreach (var signal in originals)
            await Assert.That(signal.ProviderLinkId).IsEqualTo(link.Id);
        await Assert.That(retained.Cases.Single().Status).IsEqualTo(EventReportCaseStatus.Open);
        await Assert.That(retained.Cases.Single().Priority).IsEqualTo(EventReportPriority.Urgent);
        await Assert.That(retained.Cases.Single().CurrentDecisionId).IsNull();
        await Assert.That(retained.Decisions.Count).IsEqualTo(0);
        await Assert.That(await context.EventModerationRecords.CountAsync()).IsEqualTo(0);
    }

    [Test]
    [Arguments(EventReportExternalProvider.Coop)]
    [Arguments(EventReportExternalProvider.Osprey)]
    public async Task ProviderAdvice_ReplayCannotAdoptChangedTargetOnTheSameDispatchRow(EventReportExternalProvider provider)
    {
        await fixture.ResetAsync();
        await using var context = fixture.CreateDbContext();
        var (tenant, @event, user, actor) = await SetupEventAsync(context, "changed-advice-dispatch");
        var report = CreateReport(tenant.Id, @event.Id, user.Id, actor.Id);
        var reportCase = EventReportCase.Create(tenant.Id, report.Id, "default", EventReportPriority.Normal, null);
        var link = CreateAdviceLink(report, reportCase, provider);
        context.AddRange(report, reportCase, link);
        var coop = CreateCoopAdvice(report, reportCase, "heavy_redact") with
        {
            ProviderTargetScope = null, ProviderTargetId = null
        };
        var message = CreateAdviceMessage(coop);
        if (provider == EventReportExternalProvider.Coop)
            context.IncomingWebhookMessages.Add(message);
        await context.SaveChangesAsync();
        var osprey = new OspreySignalCallbackRequestDto
        {
            TenantId = tenant.Id, EventId = @event.Id, ReportId = report.Id, CaseId = reportCase.Id,
            ProviderSignalId = "provider-signal", CorrelationId = "dispatch",
            Signals = [new OspreySignalCallbackItemDto
            {
                SignalType = "policy_match", PolicyCode = "spam", Verdict = "needs_review",
                CreatedAtUtc = AdvisoryObservedAt
            }]
        };
        async Task<Explore.Application.Responses.BaseCommandResponse<Guid>> RecordAsync() =>
            provider == EventReportExternalProvider.Coop
                ? await CreateCoopAdviceHandler(context, tenant.Id).ExecuteAsync(new ProcessCoopDecisionCallbackCommand
                {
                    Request = coop, IncomingWebhookMessageId = message.Id
                }, CancellationToken.None)
                : await new RecordOspreySignalCallbackCommandHandler(
                    new EventReportRepository(context), new EfCoreUnitOfWork(context), new StaticTenantContext(tenant.Id))
                    .ExecuteAsync(new RecordOspreySignalCallbackCommand { Request = osprey }, CancellationToken.None);

        await Assert.That((await RecordAsync()).IsSuccess).IsTrue();
        await context.EventReportExternalLinks.Where(row => row.Id == link.Id).ExecuteUpdateAsync(update =>
            update.SetProperty(row => row.ProviderTargetScope, EventReportProviderTargetScope.Instance)
                .SetProperty(row => row.ProviderTargetId, "instance"));
        context.ChangeTracker.Clear();
        var replay = await RecordAsync();
        await Assert.That(replay.IsSuccess).IsFalse();
        await Assert.That(replay.FailureCode).IsEqualTo(EventReportFailureCodes.DecisionInvalid);
        var advice = await context.EventReportSignals.AsNoTracking().SingleAsync();
        await Assert.That(advice.ProviderTargetScope).IsEqualTo(EventReportProviderTargetScope.Tenant);
        await Assert.That(advice.ProviderTargetId).IsEqualTo(tenant.Id.ToString("N"));
        await Assert.That(await context.EventReportDecisions.CountAsync()).IsEqualTo(0);
        await Assert.That(await context.EventModerationRecords.CountAsync()).IsEqualTo(0);
    }

    private static readonly DateTime AdvisoryObservedAt = new(2026, 10, 9, 12, 0, 0, DateTimeKind.Utc);

    [Test]
    [Arguments(EventReportDecisionSource.CoopReviewer, EventReportFailureCodes.DecisionInvalid)]
    [Arguments(EventReportDecisionSource.OspreyAuto, EventReportFailureCodes.DecisionInvalid)]
    [Arguments(EventReportDecisionSource.LocalModerator, EventReportFailureCodes.UserUnresolved)]
    public async Task LegacyExecution_RejectsProviderDecisionBeforeClaim_AndRetainsLocalIdentityGate(
        EventReportDecisionSource source, string expectedFailure)
    {
        await fixture.ResetAsync();
        await using var context = fixture.CreateDbContext();
        var (tenant, @event, user, actor) = await SetupEventAsync(context, "provider-execution");
        var report = CreateReport(tenant.Id, @event.Id, user.Id, actor.Id);
        var reportCase = EventReportCase.Create(tenant.Id, report.Id, "default", EventReportPriority.Normal, null);
        var decision = EventReportDecision.Create(tenant.Id, reportCase.Id, report.Id, source,
            EventReportDecisionKind.HeavyRedact, "spam", null,
            source == EventReportDecisionSource.LocalModerator ? user.Id : null,
            source == EventReportDecisionSource.LocalModerator ? null : "legacy-provider-decision");
        reportCase.SelectDecision(decision, AdvisoryObservedAt);
        context.EventReports.Add(report);
        context.EventReportCases.Add(reportCase);
        context.EventReportDecisions.Add(decision);
        await context.SaveChangesAsync();
        var stamp = reportCase.ConcurrencyStamp;
        // No enforcement or notification dependency can return a fabricated success.
        // Crossing the preflight boundary instead of rejecting must fail this test.
        var handler = new ExecuteReportDecisionCommandHandler(
            eventReportRepository: new EventReportRepository(context),
            executionRepository: new EventReportDecisionExecutionRepository(context),
            moderationRecordRepository: null!,
            eventRoleAssignmentRepository: null!,
            tenantUserRepository: null!,
            userRepository: null!,
            notificationPreferenceResolver: null!,
            recipientNotificationMaterializer: null!,
            reportOutcomeNotificationFactory: null!,
            reportNeedsMoreInformationNotificationFactory: null!,
            organizerWarningNotificationFactory: null!,
            unitOfWork: new EfCoreUnitOfWork(context),
            tenantContext: new StaticTenantContext(tenant.Id),
            currentUserService: new AnonymousCurrentUserService(),
            moderateCommandHandler: null!,
            heavyRedactCommandHandler: null!,
            mutationLock: null!);

        var result = await handler.ExecuteAsync(new ExecuteReportDecisionCommand
        {
            EventId = @event.Id, ReportId = report.Id, CaseId = reportCase.Id, DecisionId = decision.Id,
            ExpectedCaseConcurrencyStamp = stamp
        }, CancellationToken.None);

        await Assert.That(result.IsSuccess).IsFalse();
        await Assert.That(result.FailureCode).IsEqualTo(expectedFailure);
        context.ChangeTracker.Clear();
        var retained = await new EventReportRepository(context).GetByIdAsync(tenant.Id, report.Id, CancellationToken.None);
        await Assert.That(retained!.Decisions.Single().Execution.State).IsEqualTo(EventReportDecisionExecutionState.Requested);
        await Assert.That(retained.Cases.Single().CurrentDecisionId).IsEqualTo(decision.Id);
        await Assert.That(retained.Cases.Single().ConcurrencyStamp).IsEqualTo(stamp);
        await Assert.That(retained.Cases.Single().Status).IsEqualTo(EventReportCaseStatus.DecisionReady);
        await Assert.That(await context.EventModerationRecords.CountAsync()).IsEqualTo(0);
        await Assert.That(await context.OutboxMessages.CountAsync()).IsEqualTo(0);
        await Assert.That((await context.Events.SingleAsync(candidate => candidate.Id == @event.Id)).EventStatusId)
            .IsEqualTo((int)EventStatusEnum.Published);
    }

    [Test]
    public async Task OspreyAdvice_ProviderIdentityCannotMoveToAnotherNativeReportOrDispatch()
    {
        await fixture.ResetAsync();
        await using var context = fixture.CreateDbContext();
        var (tenant, @event, user, actor) = await SetupEventAsync(context, "osprey-source-reuse");
        var firstReport = CreateReport(tenant.Id, @event.Id, user.Id, actor.Id);
        var secondReport = CreateReport(tenant.Id, @event.Id, user.Id, actor.Id);
        var firstCase = EventReportCase.Create(tenant.Id, firstReport.Id, "default", EventReportPriority.Normal, null);
        var secondCase = EventReportCase.Create(tenant.Id, secondReport.Id, "default", EventReportPriority.Normal, null);
        var firstLink = CreateAdviceLink(firstReport, firstCase, EventReportExternalProvider.Osprey);
        var secondLink = EventReportExternalLink.CreatePending(tenant.Id, secondReport.Id, secondCase.Id,
            secondCase.ConcurrencyStamp, EventReportExternalProvider.Osprey, "dispatch", AdvisoryObservedAt,
            EventReportProviderTargetScope.Instance, "instance");
        secondLink.MarkSynced(null, "provider-signal", null, AdvisoryObservedAt);
        context.EventReports.AddRange(firstReport, secondReport);
        context.EventReportCases.AddRange(firstCase, secondCase);
        context.EventReportExternalLinks.AddRange(firstLink, secondLink);
        await context.SaveChangesAsync();
        var callback = new OspreySignalCallbackRequestDto
        {
            TenantId = tenant.Id, EventId = @event.Id, ReportId = firstReport.Id, CaseId = firstCase.Id,
            ProviderSignalId = "provider-signal", CorrelationId = "dispatch",
            ProviderTargetScope = "tenant", ProviderTargetId = tenant.Id.ToString("N"),
            Signals = [new OspreySignalCallbackItemDto
            {
                SignalType = "policy_match", PolicyCode = "spam", Verdict = "urgent", CreatedAtUtc = AdvisoryObservedAt
            }]
        };
        var handler = new RecordOspreySignalCallbackCommandHandler(
            new EventReportRepository(context), new EfCoreUnitOfWork(context), new StaticTenantContext(tenant.Id));
        var first = await handler.ExecuteAsync(new RecordOspreySignalCallbackCommand { Request = callback }, CancellationToken.None);
        var reused = await handler.ExecuteAsync(new RecordOspreySignalCallbackCommand
        {
            Request = callback with
            {
                ReportId = secondReport.Id, CaseId = secondCase.Id,
                ProviderTargetScope = "instance", ProviderTargetId = "instance"
            }
        }, CancellationToken.None);

        await Assert.That(first.IsSuccess).IsTrue();
        await Assert.That(reused.IsSuccess).IsFalse();
        await Assert.That(reused.FailureCode).IsEqualTo(EventReportFailureCodes.DecisionInvalid);
        context.ChangeTracker.Clear();
        var retained = await context.EventReportSignals.AsNoTracking().SingleAsync();
        await Assert.That(retained.ReportId).IsEqualTo(firstReport.Id);
        await Assert.That(retained.ProviderLinkId).IsEqualTo(firstLink.Id);
        var unchanged = await new EventReportRepository(context).GetByIdAsync(tenant.Id, secondReport.Id, CancellationToken.None);
        await Assert.That(unchanged!.Signals.Count).IsEqualTo(0);
        await Assert.That(unchanged.Decisions.Count).IsEqualTo(0);
        await Assert.That(unchanged.Cases.Single().Priority).IsEqualTo(EventReportPriority.Normal);
        await Assert.That(unchanged.Cases.Single().CurrentDecisionId).IsNull();
    }

    private sealed class AnonymousCurrentUserService : ICurrentUserService
    {
        public Guid? UserId => null;
        public bool IsAuthenticated => false;
    }

    private sealed class AdvisoryTimeProvider : TimeProvider
    {
        public override DateTimeOffset GetUtcNow() => new(AdvisoryObservedAt.AddSeconds(1));
    }

    private static EventReportExternalLink CreateAdviceLink(
        EventReport report, EventReportCase reportCase, EventReportExternalProvider provider)
    {
        var link = EventReportExternalLink.CreatePending(report.TenantId, report.Id, reportCase.Id,
            reportCase.ConcurrencyStamp, provider, "dispatch", AdvisoryObservedAt,
            EventReportProviderTargetScope.Tenant, report.TenantId.ToString("N"));
        link.MarkSynced(provider == EventReportExternalProvider.Coop ? "provider-case" : null,
            provider == EventReportExternalProvider.Osprey ? "provider-signal" : null, null, AdvisoryObservedAt);
        return link;
    }

    private static CoopDecisionCallbackRequestDto CreateCoopAdvice(EventReport report, EventReportCase reportCase, string action) => new()
    {
        TenantId = report.TenantId, ReportId = report.Id, EventId = report.EventId, CaseId = reportCase.Id,
        ProviderDecisionId = "provider-advice", ProviderCaseId = "provider-case", CorrelationId = "dispatch",
        ProviderTargetScope = "tenant", ProviderTargetId = report.TenantId.ToString("N"),
        Action = new CoopDecisionCallbackActionDto { Id = action }, SafeNote = "Retained advisory evidence."
    };

    private static IncomingWebhookMessage CreateAdviceMessage(CoopDecisionCallbackRequestDto callback)
    {
        var bytes = JsonSerializer.SerializeToUtf8Bytes(callback, ExploreJsonContext.Default.CoopDecisionCallbackRequestDto);
        return IncomingWebhookMessage.CreateVerified(callback.TenantId, "coop", callback.ProviderDecisionId!, null,
            CoopDecisionIncomingWebhookHandler.StableEffectKind, bytes,
            "sha256:" + Convert.ToHexStringLower(SHA256.HashData(bytes)), "application/json", "identity", null,
            AdvisoryObservedAt, AdvisoryObservedAt, AdvisoryObservedAt.AddDays(2), "advisory-tests",
            AdvisoryObservedAt.AddDays(2), AdvisoryObservedAt.AddDays(3), AdvisoryObservedAt.AddDays(1), AdvisoryObservedAt.AddDays(2));
    }

    private static ProcessCoopDecisionCallbackCommandHandler CreateCoopAdviceHandler(ExploreDbContext context, Guid tenantId) =>
        new(new EventReportRepository(context), new IncomingWebhookMessageRepository(context),
            new EfCoreUnitOfWork(context), new StaticTenantContext(tenantId));

    [Test]
    public async Task EventReportGraph_WhenSaved_PersistsTenantScopedRows()
    {
        await fixture.ResetAsync();
        await using var context = fixture.CreateDbContext();
        var (tenant, @event, user, actor) = await SetupEventAsync(context, "report-graph");
        var report = CreateReport(tenant.Id, @event.Id, user.Id, actor.Id);
        var reportCase = EventReportCase.Create(tenant.Id, report.Id, "default", EventReportPriority.Normal, DateTime.UtcNow.AddHours(24));
        Guid preSaveCaseConcurrencyStamp = reportCase.ConcurrencyStamp;
        var decision = EventReportDecision.Create(
            tenant.Id,
            reportCase.Id,
            report.Id,
            EventReportDecisionSource.LocalModerator,
            EventReportDecisionKind.NoViolation,
            "not_violation",
            "Reviewed safely.",
            user.Id,
            externalDecisionId: null);

        context.EventReports.Add(report);
        context.EventReportTargets.Add(EventReportTarget.CreateEventTarget(tenant.Id, report.Id, @event.Id));
        context.EventReportEvidenceItems.Add(EventReportEvidence.CreateReporterText(
            tenant.Id,
            report.Id,
            "encrypted:reporter-text",
            EventReportEvidenceClassification.Sensitive,
            DateTime.UtcNow.AddDays(30),
            user.Id));
        context.EventReportCases.Add(reportCase);
        context.EventReportSignals.Add(EventReportSignal.Create(
            tenant.Id,
            report.Id,
            @event.Id,
            EventReportSignalProvider.Local,
            "keyword_match",
            "community_spam",
            0.82m,
            EventReportSignalVerdict.NeedsReview,
            EventReportRecommendedAction.LightModerate,
            "Potential promotional abuse.",
            "signal-" + Guid.NewGuid().ToString("N"),
            "correlation-" + Guid.NewGuid().ToString("N")[..12]));
        context.EventReportDecisions.Add(decision);
        context.EventReportExternalLinks.Add(EventReportExternalLink.CreatePending(
            tenant.Id,
            report.Id,
            reportCase.Id,
            reportCase.ConcurrencyStamp,
            EventReportExternalProvider.Coop,
            "external-" + Guid.NewGuid().ToString("N")[..12]));
        await context.SaveChangesAsync();

        await using var verifyContext = fixture.CreateTenantFilteredDbContext(new StaticTenantContext(tenant.Id));
        var persisted = await verifyContext.EventReports
            .Include(e => e.Targets)
            .Include(e => e.EvidenceItems)
            .Include(e => e.Cases)
            .Include(e => e.Signals)
            .Include(e => e.Decisions)
                .ThenInclude(e => e.Execution)
            .Include(e => e.ExternalLinks)
            .SingleAsync(e => e.Id == report.Id);

        await Assert.That(persisted.Targets.Count).IsEqualTo(1);
        await Assert.That(persisted.EvidenceItems.Count).IsEqualTo(1);
        await Assert.That(persisted.Cases.Count).IsEqualTo(1);
        await Assert.That(persisted.Cases.Single().ConcurrencyStamp).IsEqualTo(preSaveCaseConcurrencyStamp);
        await Assert.That(persisted.Signals.Count).IsEqualTo(1);
        await Assert.That(persisted.Decisions.Count).IsEqualTo(1);
        await Assert.That(persisted.Decisions.Single().Execution.State)
            .IsEqualTo(EventReportDecisionExecutionState.Requested);
        await Assert.That(persisted.Decisions.Single().Execution.DecisionId).IsEqualTo(decision.Id);
        await Assert.That(persisted.Decisions.Single().Execution.ReportId).IsEqualTo(report.Id);
        await Assert.That(persisted.ExternalLinks.Count).IsEqualTo(1);
    }

    [Test]
    public async Task ExactEnforcementClaimReplay_WhenRequestedLeaseHasExpired_ReturnsUnavailable()
    {
        await fixture.ResetAsync();
        await using var context = fixture.CreateDbContext();
        var (tenant, @event, user, actor) = await SetupEventAsync(context, "report-expired-enforcement-lease");
        var report = CreateReport(tenant.Id, @event.Id, user.Id, actor.Id);
        var reportCase = EventReportCase.Create(tenant.Id, report.Id, "default", EventReportPriority.Normal, null);
        var decision = EventReportDecision.Create(
            tenant.Id,
            reportCase.Id,
            report.Id,
            EventReportDecisionSource.LocalModerator,
            EventReportDecisionKind.NoViolation,
            "not_violation",
            null,
            user.Id,
            null);
        context.EventReports.Add(report);
        context.EventReportCases.Add(reportCase);
        context.EventReportDecisions.Add(decision);
        await context.SaveChangesAsync();

        var repository = new EventReportDecisionExecutionRepository(context);
        Guid leaseToken = Guid.CreateVersion7();
        DateTime claimedAtUtc = DateTime.UtcNow.AddMinutes(-5);
        DateTime expiredAtUtc = claimedAtUtc.AddMinutes(1);

        EventReportDecisionExecutionClaimOutcome claimed = await repository.TryClaimEnforcementAsync(
            tenant.Id,
            decision.Id,
            leaseToken,
            claimedAtUtc,
            expiredAtUtc,
            CancellationToken.None);
        EventReportDecisionExecutionClaimOutcome reconciled = await repository.TryClaimEnforcementAsync(
            tenant.Id,
            decision.Id,
            leaseToken,
            claimedAtUtc,
            expiredAtUtc,
            CancellationToken.None);

        await Assert.That(claimed).IsEqualTo(EventReportDecisionExecutionClaimOutcome.Claimed);
        await Assert.That(reconciled).IsEqualTo(EventReportDecisionExecutionClaimOutcome.Unavailable);
    }

    [Test]
    public async Task ExactCompletionClaimReplay_WhenRequestedLeaseHasExpired_ReturnsUnavailable()
    {
        await fixture.ResetAsync();
        await using var context = fixture.CreateDbContext();
        var (tenant, @event, user, actor) = await SetupEventAsync(context, "report-expired-completion-lease");
        var report = CreateReport(tenant.Id, @event.Id, user.Id, actor.Id);
        var reportCase = EventReportCase.Create(tenant.Id, report.Id, "default", EventReportPriority.Normal, null);
        var decision = EventReportDecision.Create(
            tenant.Id,
            reportCase.Id,
            report.Id,
            EventReportDecisionSource.LocalModerator,
            EventReportDecisionKind.NoViolation,
            "not_violation",
            null,
            user.Id,
            null);
        DateTime enforcementClaimedAtUtc = DateTime.UtcNow.AddMinutes(-10);
        Guid enforcementLeaseToken = Guid.CreateVersion7();
        decision.Execution.ClaimEnforcement(
            enforcementLeaseToken,
            enforcementClaimedAtUtc,
            enforcementClaimedAtUtc.AddMinutes(2));
        decision.Execution.RecordEnforcementReceipt(
            enforcementLeaseToken,
            EventReportDecisionEnforcementReceiptKind.NoAction,
            null,
            enforcementClaimedAtUtc.AddMinutes(1));
        context.EventReports.Add(report);
        context.EventReportCases.Add(reportCase);
        context.EventReportDecisions.Add(decision);
        await context.SaveChangesAsync();

        var repository = new EventReportDecisionExecutionRepository(context);
        Guid completionLeaseToken = Guid.CreateVersion7();
        DateTime completionClaimedAtUtc = DateTime.UtcNow.AddMinutes(-5);
        DateTime expiredAtUtc = completionClaimedAtUtc.AddMinutes(1);

        EventReportDecisionExecutionClaimOutcome claimed = await repository.TryClaimCompletionAsync(
            tenant.Id,
            decision.Id,
            completionLeaseToken,
            completionClaimedAtUtc,
            expiredAtUtc,
            CancellationToken.None);
        EventReportDecisionExecutionClaimOutcome reconciled = await repository.TryClaimCompletionAsync(
            tenant.Id,
            decision.Id,
            completionLeaseToken,
            completionClaimedAtUtc,
            expiredAtUtc,
            CancellationToken.None);

        await Assert.That(claimed).IsEqualTo(EventReportDecisionExecutionClaimOutcome.Claimed);
        await Assert.That(reconciled).IsEqualTo(EventReportDecisionExecutionClaimOutcome.Unavailable);
    }

    [Test]
    public async Task CommunicationConsent_WhenChanged_PersistsBothPurposesAndRotatesConcurrencyStamp()
    {
        await fixture.ResetAsync();
        await using var context = fixture.CreateDbContext();
        var (tenant, @event, user, actor) = await SetupEventAsync(context, "report-consent");
        var report = CreateReport(tenant.Id, @event.Id, user.Id, actor.Id);
        context.EventReports.Add(report);
        await context.SaveChangesAsync();
        var previousConcurrencyStamp = report.ConcurrencyStamp;
        var changedAt = new DateTime(2026, 7, 19, 18, 30, 0, DateTimeKind.Utc);

        report.ChangeReporterCommunicationConsent(
            reportCaseUpdatesConsent: true,
            reportFollowUpContactConsent: false,
            changedAt);
        await context.SaveChangesAsync();

        await using var verifyContext = fixture.CreateTenantFilteredDbContext(new StaticTenantContext(tenant.Id));
        var persisted = await verifyContext.EventReports.AsNoTracking().SingleAsync(value => value.Id == report.Id);
        await Assert.That(persisted.ReportCaseUpdatesConsent).IsTrue();
        await Assert.That(persisted.ReportFollowUpContactConsent).IsFalse();
        await Assert.That(persisted.UpdatedAt).IsEqualTo(changedAt);
        await Assert.That(persisted.ConcurrencyStamp).IsNotEqualTo(previousConcurrencyStamp);
    }

    [Test]
    public async Task TenantFilter_HidesReportsAndEvidenceFromOtherTenants()
    {
        await fixture.ResetAsync();
        await using var setupContext = fixture.CreateDbContext();
        var (tenant, @event, user, actor) = await SetupEventAsync(setupContext, "report-filter");
        var report = CreateReport(tenant.Id, @event.Id, user.Id, actor.Id);
        setupContext.EventReports.Add(report);
        setupContext.EventReportEvidenceItems.Add(EventReportEvidence.CreateReporterText(
            tenant.Id,
            report.Id,
            "encrypted:tenant-filter",
            EventReportEvidenceClassification.Normal,
            retentionUntil: null,
            user.Id));
        await setupContext.SaveChangesAsync();

        await using var wrongTenantContext = fixture.CreateTenantFilteredDbContext(new StaticTenantContext(Guid.CreateVersion7()));

        await Assert.That(await wrongTenantContext.EventReports.CountAsync()).IsEqualTo(0);
        await Assert.That(await wrongTenantContext.EventReportEvidenceItems.CountAsync()).IsEqualTo(0);
    }

    [Test]
    public async Task SoftDeleteReport_HidesReportEvidenceGraphFromFilteredQueries()
    {
        await fixture.ResetAsync();
        await using var context = fixture.CreateDbContext();
        var (tenant, @event, user, actor) = await SetupEventAsync(context, "report-soft-delete");
        var report = CreateReport(tenant.Id, @event.Id, user.Id, actor.Id);
        context.EventReports.Add(report);
        context.EventReportEvidenceItems.Add(EventReportEvidence.CreateReporterText(
            tenant.Id,
            report.Id,
            "encrypted:soft-delete",
            EventReportEvidenceClassification.Sensitive,
            retentionUntil: null,
            user.Id));
        await context.SaveChangesAsync();

        context.EventReports.Remove(report);
        await context.SaveChangesAsync();

        await using var filteredContext = fixture.CreateTenantFilteredDbContext(new StaticTenantContext(tenant.Id));
        await Assert.That(await filteredContext.EventReports.CountAsync()).IsEqualTo(0);
        await Assert.That(await filteredContext.EventReportEvidenceItems.CountAsync()).IsEqualTo(0);

        await using var rawContext = fixture.CreateDbContext();
        var rawReport = await rawContext.EventReports.IgnoreQueryFilters().SingleAsync(e => e.Id == report.Id);
        var rawEvidenceCount = await rawContext.EventReportEvidenceItems.IgnoreQueryFilters().CountAsync(e => e.ReportId == report.Id);

        await Assert.That(rawReport.IsDeleted).IsTrue();
        await Assert.That(rawEvidenceCount).IsEqualTo(1);
    }

    [Test]
    public async Task SaveChanges_ShouldRejectCaseReferencingReportFromAnotherTenant()
    {
        await fixture.ResetAsync();
        await using var context = fixture.CreateDbContext();
        var (tenantA, eventA, userA, actorA) = await SetupEventAsync(context, "report-tenant-a");
        var (tenantB, _, _, _) = await SetupEventAsync(context, "report-tenant-b");
        var report = CreateReport(tenantA.Id, eventA.Id, userA.Id, actorA.Id);
        context.EventReports.Add(report);
        await context.SaveChangesAsync();

        context.EventReportCases.Add(EventReportCase.Create(
            tenantB.Id,
            report.Id,
            "default",
            EventReportPriority.Normal,
            slaDueAt: null));

        await Assert.ThrowsAsync<DbUpdateException>(async () => await context.SaveChangesAsync());
    }

    private static EventReport CreateReport(Guid tenantId, Guid eventId, Guid userId, Guid actorId)
    {
        return EventReport.Create(
            tenantId,
            eventId,
            userId,
            actorId,
            EventReporterKind.AuthenticatedUser,
            EventReportSourceKind.UserReport,
            "spam",
            "misleading",
            EventReportPriority.Normal,
            EventReportSeverityHint.Medium,
            reportCaseUpdatesConsent: false,
            reportFollowUpContactConsent: true,
            "en",
            "iphash-" + Guid.NewGuid().ToString("N")[..16],
            "uahash-" + Guid.NewGuid().ToString("N")[..16]);
    }

    private static async Task<(Tenant Tenant, Explore.Domain.Event Event, User User, Actor Actor)> SetupEventAsync(
        ExploreDbContext context,
        string slugPrefix)
    {
        var tenant = new Tenant
        {
            FullName = "Reporting Test Tenant " + slugPrefix,
            Slug = slugPrefix + "-" + Guid.NewGuid().ToString("N")[..8],
            TenantStatusId = (int)TenantStatusEnum.Active,
            TenantStatus = null!
        };
        context.Tenants.Add(tenant);

        var user = new User
        {
            Pii = new UserPii
            {
                Email = $"{slugPrefix}-{Guid.NewGuid():N}@example.com",
                FirstName = "Report",
                LastName = "User"
            }
        };
        context.Users.Add(user);
        await context.SaveChangesAsync();

        var actor = new Actor
        {
            Pii = new ActorPii { DisplayName = "Reporting Actor " + slugPrefix },
            ActorTypeId = (int)ActorTypeEnum.User,
            ActorType = null!,
            UserId = user.Id
        };
        context.Actors.Add(actor);
        await context.SaveChangesAsync();

        var @event = new Explore.Domain.Event(EventStatusEnum.Published)
        {
            Id = Guid.CreateVersion7(),
            Title = "Reporting Event " + slugPrefix,
            EventProvenanceTypeId = (int)EventProvenanceTypeEnum.OrganizerCreated,
            ActorId = actor.Id,
            Actor = null!,
            TenantId = tenant.Id,
            Tenant = null!,
            VisibilityTypeId = (int)VisibilityTypeEnum.Public,
            VisibilityType = null!,
            EventStatus = null!,
            EventFormatId = (int)EventFormatEnum.Digital,
            EventFormat = null!
        };
        context.Events.Add(@event);
        await context.SaveChangesAsync();

        return (tenant, @event, user, actor);
    }

    private sealed record StaticTenantContext(Guid TenantId) : ITenantContext;
}
