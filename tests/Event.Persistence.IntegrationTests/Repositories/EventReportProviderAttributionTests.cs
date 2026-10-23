using Explore.Application.DTOs.EventReporting;
using Explore.Application.Features.EventReporting;
using Explore.Application.Features.EventReporting.Handlers.Commands;
using Explore.Application.Features.EventReporting.Requests.Commands;
using Explore.Domain;
using Explore.Domain.Enums;
using Explore.Persistence;
using Explore.Persistence.Repositories;
using Microsoft.EntityFrameworkCore;

namespace Event.Persistence.IntegrationTests.Repositories;

public sealed partial class EventReportPersistenceTests
{
    [Test]
    [Arguments(EventReportExternalProvider.Coop)]
    [Arguments(EventReportExternalProvider.Osprey)]
    public async Task ProviderAdvice_WithoutClaimedVersionRejectsChangedNativeDispatchGeneration(EventReportExternalProvider provider)
    {
        await fixture.ResetAsync();
        await using var context = fixture.CreateDbContext();
        var (tenant, @event, user, actor) = await SetupEventAsync(context, "stale-native-dispatch");
        var report = CreateReport(tenant.Id, @event.Id, user.Id, actor.Id);
        var reportCase = EventReportCase.Create(tenant.Id, report.Id, "default", EventReportPriority.Normal, null);
        var link = CreateAdviceLink(report, reportCase, provider);
        var coop = CreateCoopAdvice(report, reportCase, "heavy_redact");
        var osprey = CreateAttributionOspreyAdvice(report, reportCase);
        var message = CreateAdviceMessage(coop);
        context.AddRange(report, reportCase, link);
        if (provider == EventReportExternalProvider.Coop) context.Add(message);
        await context.SaveChangesAsync();
        context.Entry(reportCase).Property(row => row.ConcurrencyStamp).CurrentValue = Guid.CreateVersion7();
        await context.SaveChangesAsync();
        var currentStamp = reportCase.ConcurrencyStamp;
        var result = provider == EventReportExternalProvider.Coop
            ? await CreateCoopAdviceHandler(context, tenant.Id).ExecuteAsync(new ProcessCoopDecisionCallbackCommand
                { Request = coop, IncomingWebhookMessageId = message.Id }, CancellationToken.None)
            : await new RecordOspreySignalCallbackCommandHandler(
                new EventReportRepository(context), new EfCoreUnitOfWork(context), new StaticTenantContext(tenant.Id)).ExecuteAsync(
                new RecordOspreySignalCallbackCommand { Request = osprey }, CancellationToken.None);
        await Assert.That(result.IsSuccess).IsFalse();
        await Assert.That(result.FailureCode).IsEqualTo(EventReportFailureCodes.CaseConcurrencyConflict);
        context.ChangeTracker.Clear();
        var retained = (await new EventReportRepository(context).GetByIdAsync(
            tenant.Id, report.Id, CancellationToken.None))!;
        await Assert.That(retained.Signals.Count).IsEqualTo(0);
        await Assert.That(retained.Decisions.Count).IsEqualTo(0);
        await Assert.That(retained.Cases.Single().ConcurrencyStamp).IsEqualTo(currentStamp);
        await Assert.That(retained.ExternalLinks.Single().CaseConcurrencyStamp).IsEqualTo(link.CaseConcurrencyStamp);
        await Assert.That(await context.EventModerationRecords.CountAsync()).IsEqualTo(0);
        await Assert.That(await context.OutboxMessages.CountAsync()).IsEqualTo(0);
    }

    [Test]
    [Arguments(EventReportExternalProvider.Coop)]
    [Arguments(EventReportExternalProvider.Osprey)]
    public async Task ProviderAdvice_ExactReplayCannotBorrowReboundNativeGeneration(EventReportExternalProvider provider)
    {
        await fixture.ResetAsync();
        await using var context = fixture.CreateDbContext();
        var (tenant, @event, user, actor) = await SetupEventAsync(context, "rebound-native-dispatch");
        var report = CreateReport(tenant.Id, @event.Id, user.Id, actor.Id);
        var reportCase = EventReportCase.Create(tenant.Id, report.Id, "default", EventReportPriority.Normal, null);
        var link = CreateAdviceLink(report, reportCase, provider);
        var originalStamp = reportCase.ConcurrencyStamp;
        var coop = CreateCoopAdvice(report, reportCase, "heavy_redact");
        var osprey = CreateAttributionOspreyAdvice(report, reportCase);
        var message = CreateAdviceMessage(coop);
        context.AddRange(report, reportCase, link);
        if (provider == EventReportExternalProvider.Coop) context.Add(message);
        await context.SaveChangesAsync();
        async Task<Explore.Application.Responses.BaseCommandResponse<Guid>> RecordAsync() =>
            provider == EventReportExternalProvider.Coop
                ? await CreateCoopAdviceHandler(context, tenant.Id).ExecuteAsync(new ProcessCoopDecisionCallbackCommand
                    { Request = coop, IncomingWebhookMessageId = message.Id }, CancellationToken.None)
                : await new RecordOspreySignalCallbackCommandHandler(
                    new EventReportRepository(context), new EfCoreUnitOfWork(context), new StaticTenantContext(tenant.Id)).ExecuteAsync(
                    new RecordOspreySignalCallbackCommand { Request = osprey }, CancellationToken.None);
        await Assert.That((await RecordAsync()).IsSuccess).IsTrue();
        var substitute = Guid.CreateVersion7();
        await context.EventReportExternalLinks.Where(row => row.Id == link.Id).ExecuteUpdateAsync(
            update => update.SetProperty(row => row.CaseConcurrencyStamp, substitute));
        context.ChangeTracker.Clear();
        var replay = await RecordAsync();
        await Assert.That(replay.IsSuccess).IsFalse();
        await Assert.That(replay.FailureCode).IsEqualTo(EventReportFailureCodes.DecisionInvalid);
        var retained = await context.EventReportSignals.AsNoTracking().SingleAsync();
        await Assert.That(retained.SourceCaseConcurrencyStamp).IsEqualTo(originalStamp);
        await Assert.That(await context.EventReportDecisions.CountAsync()).IsEqualTo(0);
        await Assert.That(await context.EventModerationRecords.CountAsync()).IsEqualTo(0);
        await Assert.That(await context.OutboxMessages.CountAsync()).IsEqualTo(0);
    }

    [Test]
    [Arguments(false)]
    [Arguments(true)]
    public async Task CoopAdvice_AuthenticatedNewAdviceCannotIgnoreItsStaleExpectedNativeCaseStamp(bool snakeCase)
    {
        await fixture.ResetAsync();
        await using var context = fixture.CreateDbContext();
        var (tenant, @event, user, actor) = await SetupEventAsync(context, "coop-stale-case");
        var report = CreateReport(tenant.Id, @event.Id, user.Id, actor.Id);
        var reportCase = EventReportCase.Create(tenant.Id, report.Id, "default", EventReportPriority.Normal, null);
        var link = CreateAdviceLink(report, reportCase, EventReportExternalProvider.Coop);
        var previousStamp = reportCase.ConcurrencyStamp;
        var callback = CreateCoopAdvice(report, reportCase, "heavy_redact") with
        {
            ExpectedCaseConcurrencyStamp = snakeCase ? null : previousStamp,
            ExpectedCaseConcurrencyStampSnake = snakeCase ? previousStamp : null
        };
        var message = CreateAdviceMessage(callback);
        context.AddRange(report, reportCase, link, message);
        await context.SaveChangesAsync();
        context.Entry(reportCase).Property(row => row.ConcurrencyStamp).CurrentValue = Guid.CreateVersion7();
        await context.SaveChangesAsync();
        var currentStamp = reportCase.ConcurrencyStamp;
        var rejected = await CreateCoopAdviceHandler(context, tenant.Id).ExecuteAsync(
            new ProcessCoopDecisionCallbackCommand { Request = callback, IncomingWebhookMessageId = message.Id },
            CancellationToken.None);
        await Assert.That(rejected.IsSuccess).IsFalse();
        await Assert.That(rejected.FailureCode).IsEqualTo(EventReportFailureCodes.CaseConcurrencyConflict);
        context.ChangeTracker.Clear();
        var retained = (await new EventReportRepository(context).GetByIdAsync(
            tenant.Id, report.Id, CancellationToken.None))!;
        await Assert.That(retained.Signals.Count).IsEqualTo(0);
        await Assert.That(retained.Decisions.Count).IsEqualTo(0);
        await Assert.That(retained.Cases.Single().ConcurrencyStamp).IsEqualTo(currentStamp);
        await Assert.That(retained.Cases.Single().CurrentDecisionId).IsNull();
        await Assert.That(await context.EventModerationRecords.CountAsync()).IsEqualTo(0);
        await Assert.That(await context.OutboxMessages.CountAsync()).IsEqualTo(0);
    }

    [Test]
    public async Task CoopAdvice_ExactHistoricalRetryDoesNotBecomeNewAdviceAfterNativeCaseStampChanges()
    {
        await fixture.ResetAsync();
        await using var context = fixture.CreateDbContext();
        var (tenant, @event, user, actor) = await SetupEventAsync(context, "coop-historical-case");
        var report = CreateReport(tenant.Id, @event.Id, user.Id, actor.Id);
        var reportCase = EventReportCase.Create(tenant.Id, report.Id, "default", EventReportPriority.Normal, null);
        var link = CreateAdviceLink(report, reportCase, EventReportExternalProvider.Coop);
        var callback = CreateCoopAdvice(report, reportCase, "heavy_redact") with
        { ExpectedCaseConcurrencyStamp = reportCase.ConcurrencyStamp };
        var message = CreateAdviceMessage(callback);
        context.AddRange(report, reportCase, link, message);
        await context.SaveChangesAsync();
        var handler = CreateCoopAdviceHandler(context, tenant.Id);
        var command = new ProcessCoopDecisionCallbackCommand { Request = callback, IncomingWebhookMessageId = message.Id };
        var accepted = await handler.ExecuteAsync(command, CancellationToken.None);
        await Assert.That(accepted.IsSuccess).IsTrue();
        context.Entry(reportCase).Property(row => row.ConcurrencyStamp).CurrentValue = Guid.CreateVersion7();
        await context.SaveChangesAsync();
        var replay = await handler.ExecuteAsync(command, CancellationToken.None);
        await Assert.That(replay.IsSuccess).IsTrue();
        await Assert.That(replay.Id).IsEqualTo(accepted.Id);
        context.ChangeTracker.Clear();
        var retained = (await new EventReportRepository(context).GetByIdAsync(
            tenant.Id, report.Id, CancellationToken.None))!;
        await Assert.That(retained.Signals.Count).IsEqualTo(1);
        await Assert.That(retained.Decisions.Count).IsEqualTo(0);
        await Assert.That(await context.EventModerationRecords.CountAsync()).IsEqualTo(0);
        await Assert.That(await context.OutboxMessages.CountAsync()).IsEqualTo(0);
    }

    [Test]
    [Arguments(false)]
    [Arguments(true)]
    public async Task CoopAdvice_SameInboxIdentityRejectsChangedSourceOrClaimedCaseVersion(bool changeVersion)
    {
        await fixture.ResetAsync();
        await using var context = fixture.CreateDbContext();
        var (tenant, @event, user, actor) = await SetupEventAsync(context, "coop-attribution");
        var firstReport = CreateReport(tenant.Id, @event.Id, user.Id, actor.Id);
        var secondReport = CreateReport(tenant.Id, @event.Id, user.Id, actor.Id);
        var firstCase = EventReportCase.Create(tenant.Id, firstReport.Id, "default", EventReportPriority.Normal, null);
        var secondCase = EventReportCase.Create(tenant.Id, secondReport.Id, "default", EventReportPriority.Normal, null);
        var firstLink = CreateAdviceLink(firstReport, firstCase, EventReportExternalProvider.Coop);
        var secondLink = EventReportExternalLink.CreatePending(tenant.Id, secondReport.Id, secondCase.Id,
            secondCase.ConcurrencyStamp, EventReportExternalProvider.Coop, "dispatch", AdvisoryObservedAt,
            EventReportProviderTargetScope.Instance, "instance");
        secondLink.MarkSynced("provider-case", null, null, AdvisoryObservedAt);
        var callback = CreateCoopAdvice(firstReport, firstCase, "heavy_redact") with
        {
            ExpectedCaseConcurrencyStamp = firstCase.ConcurrencyStamp
        };
        var message = CreateAdviceMessage(callback);
        context.AddRange(firstReport, secondReport, firstCase, secondCase, firstLink, secondLink, message);
        await context.SaveChangesAsync();
        var firstStamp = firstCase.ConcurrencyStamp;
        var secondStamp = secondCase.ConcurrencyStamp;
        var handler = CreateCoopAdviceHandler(context, tenant.Id);
        var command = new ProcessCoopDecisionCallbackCommand { Request = callback, IncomingWebhookMessageId = message.Id };
        var accepted = await handler.ExecuteAsync(command, CancellationToken.None);
        await Assert.That(accepted.IsSuccess).IsTrue();

        // Both sources have real dispatch rows. The original authenticated envelope
        // must not be usable as authority for a different source or claimed version.
        var substituted = changeVersion
            ? callback with { ExpectedCaseConcurrencyStamp = Guid.CreateVersion7() }
            : callback with
            {
                ReportId = secondReport.Id, CaseId = secondCase.Id,
                ProviderTargetScope = "instance", ProviderTargetId = "instance"
            };
        var rejected = await handler.ExecuteAsync(command with { Request = substituted }, CancellationToken.None);

        await Assert.That(rejected.IsSuccess).IsFalse();
        await Assert.That(rejected.FailureCode).IsEqualTo(EventReportFailureCodes.ValidationFailed);
        context.ChangeTracker.Clear();
        var repository = new EventReportRepository(context);
        var retained = (await repository.GetByIdAsync(tenant.Id, firstReport.Id, CancellationToken.None))!;
        var untouched = (await repository.GetByIdAsync(tenant.Id, secondReport.Id, CancellationToken.None))!;
        await Assert.That(retained.Signals.Single().Id).IsEqualTo(accepted.Id);
        await Assert.That(retained.Signals.Single().ProviderLinkId).IsEqualTo(firstLink.Id);
        await Assert.That(retained.Signals.Single().IncomingWebhookMessageId).IsEqualTo(message.Id);
        await Assert.That(retained.Signals.Single().AdvisoryMaterialSha256).IsEqualTo(message.PayloadHash);
        await Assert.That(retained.Cases.Single().ConcurrencyStamp).IsEqualTo(firstStamp);
        await Assert.That(untouched.Signals.Count).IsEqualTo(0);
        await Assert.That(untouched.Cases.Single().ConcurrencyStamp).IsEqualTo(secondStamp);
        foreach (var report in new[] { retained, untouched })
        {
            await Assert.That(report.Decisions.Count).IsEqualTo(0);
            await Assert.That(report.Cases.Single().CurrentDecisionId).IsNull();
            await Assert.That(report.Cases.Single().Status).IsEqualTo(EventReportCaseStatus.Open);
        }
        await Assert.That(await context.EventModerationRecords.CountAsync()).IsEqualTo(0);
        await Assert.That(await context.OutboxMessages.CountAsync()).IsEqualTo(0);
    }

    [Test]
    [Arguments("instance", "instance")]
    [Arguments("tenant", "another-tenant")]
    public async Task OspreyAdvice_ForgedScopeBeforeFirstAdviceCannotPromoteOrDecide(string scope, string target)
    {
        await fixture.ResetAsync();
        await using var context = fixture.CreateDbContext();
        var (tenant, @event, user, actor) = await SetupEventAsync(context, "osprey-forged-first");
        var report = CreateReport(tenant.Id, @event.Id, user.Id, actor.Id);
        var reportCase = EventReportCase.Create(tenant.Id, report.Id, "default", EventReportPriority.Normal, null);
        var link = CreateAdviceLink(report, reportCase, EventReportExternalProvider.Osprey);
        context.AddRange(report, reportCase, link);
        await context.SaveChangesAsync();
        var stamp = reportCase.ConcurrencyStamp;
        var callback = CreateAttributionOspreyAdvice(report, reportCase) with
        {
            ProviderTargetScope = scope, ProviderTargetId = target
        };
        var handler = new RecordOspreySignalCallbackCommandHandler(
            new EventReportRepository(context), new EfCoreUnitOfWork(context), new StaticTenantContext(tenant.Id));

        var rejected = await handler.ExecuteAsync(new RecordOspreySignalCallbackCommand { Request = callback }, CancellationToken.None);

        await Assert.That(rejected.IsSuccess).IsFalse();
        await Assert.That(rejected.FailureCode).IsEqualTo(EventReportFailureCodes.ValidationFailed);
        context.ChangeTracker.Clear();
        var retained = (await new EventReportRepository(context).GetByIdAsync(tenant.Id, report.Id, CancellationToken.None))!;
        await Assert.That(retained.Signals.Count).IsEqualTo(0);
        await Assert.That(retained.Decisions.Count).IsEqualTo(0);
        await Assert.That(retained.Priority).IsEqualTo(EventReportPriority.Normal);
        await Assert.That(retained.Cases.Single().Priority).IsEqualTo(EventReportPriority.Normal);
        await Assert.That(retained.Cases.Single().ConcurrencyStamp).IsEqualTo(stamp);
        await Assert.That(retained.Cases.Single().CurrentDecisionId).IsNull();
        await Assert.That(retained.Cases.Single().Status).IsEqualTo(EventReportCaseStatus.Open);
        await Assert.That(await context.EventModerationRecords.CountAsync()).IsEqualTo(0);
        await Assert.That(await context.OutboxMessages.CountAsync()).IsEqualTo(0);
    }

    [Test]
    [Arguments(EventReportExternalProvider.Coop)]
    [Arguments(EventReportExternalProvider.Osprey)]
    public async Task ProviderAdvice_CopiedMaterialReissuedUnderNewIdentityRemainsAdvisory(EventReportExternalProvider provider)
    {
        await fixture.ResetAsync();
        await using var context = fixture.CreateDbContext();
        var (tenant, @event, user, actor) = await SetupEventAsync(context, "reissued-advice");
        var report = CreateReport(tenant.Id, @event.Id, user.Id, actor.Id);
        var reportCase = EventReportCase.Create(tenant.Id, report.Id, "default", EventReportPriority.Normal, null);
        var link = CreateAdviceLink(report, reportCase, provider);
        context.AddRange(report, reportCase, link);
        await context.SaveChangesAsync();
        var reportStatus = report.Status;
        var eventStatus = @event.EventStatusId;
        var coop = CreateCoopAdvice(report, reportCase, "heavy_redact");
        var osprey = CreateAttributionOspreyAdvice(report, reportCase);
        var originalSummary = provider == EventReportExternalProvider.Coop ? coop.SafeNote : osprey.Signals[0].SafeSummary;

        for (var index = 0; index < 2; index++)
        {
            if (provider == EventReportExternalProvider.Coop)
            {
                var callback = coop with { ProviderDecisionId = $"reissued-advice-{index}" };
                var message = CreateAdviceMessage(callback);
                context.IncomingWebhookMessages.Add(message);
                await context.SaveChangesAsync();
                var accepted = await CreateCoopAdviceHandler(context, tenant.Id).ExecuteAsync(
                    new ProcessCoopDecisionCallbackCommand { Request = callback, IncomingWebhookMessageId = message.Id },
                    CancellationToken.None);
                await Assert.That(accepted.IsSuccess).IsTrue();
            }
            else
            {
                // Reissue through the real dispatch transition, not a forged callback.
                var identity = $"reissued-signal-{index}";
                link.MarkSynced(null, identity, null, AdvisoryObservedAt);
                await context.SaveChangesAsync();
                var accepted = await new RecordOspreySignalCallbackCommandHandler(
                    new EventReportRepository(context), new EfCoreUnitOfWork(context), new StaticTenantContext(tenant.Id))
                    .ExecuteAsync(new RecordOspreySignalCallbackCommand
                    {
                        Request = osprey with { ProviderSignalId = identity }
                    }, CancellationToken.None);
                await Assert.That(accepted.IsSuccess).IsTrue();
            }
        }

        context.ChangeTracker.Clear();
        var retained = (await new EventReportRepository(context).GetByIdAsync(tenant.Id, report.Id, CancellationToken.None))!;
        await Assert.That(retained.Signals.Count).IsEqualTo(2);
        await Assert.That(retained.Signals.Select(signal => signal.AdvisoryIdentity).Distinct().Count()).IsEqualTo(2);
        foreach (var signal in retained.Signals)
        {
            await Assert.That(signal.ProviderLinkId).IsEqualTo(link.Id);
            await Assert.That(signal.CaseId).IsEqualTo(reportCase.Id);
            await Assert.That(signal.RecommendedAction).IsEqualTo(EventReportRecommendedAction.HeavyRedact);
            await Assert.That(signal.SafeSummary).IsEqualTo(originalSummary);
        }
        await Assert.That(retained.Status).IsEqualTo(reportStatus);
        await Assert.That(retained.Cases.Single().Status).IsEqualTo(EventReportCaseStatus.Open);
        await Assert.That(retained.Cases.Single().CurrentDecisionId).IsNull();
        await Assert.That(retained.Decisions.Count).IsEqualTo(0);
        await Assert.That(await context.EventReportDecisionExecutions.CountAsync()).IsEqualTo(0);
        await Assert.That(await context.EventModerationRecords.CountAsync()).IsEqualTo(0);
        await Assert.That(await context.OutboxMessages.CountAsync()).IsEqualTo(0);
        await Assert.That((await context.Events.AsNoTracking().SingleAsync(row => row.Id == @event.Id)).EventStatusId)
            .IsEqualTo(eventStatus);
    }

    private static OspreySignalCallbackRequestDto CreateAttributionOspreyAdvice(EventReport report, EventReportCase reportCase) => new()
    {
        TenantId = report.TenantId, EventId = report.EventId, ReportId = report.Id, CaseId = reportCase.Id,
        ProviderSignalId = "provider-signal", CorrelationId = "dispatch",
        ProviderTargetScope = "tenant", ProviderTargetId = report.TenantId.ToString("N"),
        Signals = [new OspreySignalCallbackItemDto
        {
            SignalType = "policy_match", PolicyCode = "spam", Verdict = "urgent",
            RecommendedAction = "heavy_redact", SafeSummary = "Retained advisory evidence.",
            CreatedAtUtc = AdvisoryObservedAt
        }]
    };
}
