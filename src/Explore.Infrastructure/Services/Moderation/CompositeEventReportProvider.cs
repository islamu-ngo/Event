using Explore.Application.Contracts.Infrastructure;
using Explore.Application.Features.EventReporting.Models;
using Explore.Domain.Enums;

namespace Explore.Infrastructure.Services.Moderation;

public sealed class CompositeEventReportProvider : IEventReportProvider
{
    private readonly LocalEventReportProvider _localProvider;
    private readonly IModerationSignalProvider _signalProvider;
    private readonly IReviewQueueProvider _reviewQueueProvider;
    private readonly IReportingRoutingPolicyResolver _routingPolicyResolver;

    public CompositeEventReportProvider(
        LocalEventReportProvider localProvider,
        IModerationSignalProvider signalProvider,
        IReviewQueueProvider reviewQueueProvider,
        IReportingRoutingPolicyResolver routingPolicyResolver)
    {
        _localProvider = localProvider;
        _signalProvider = signalProvider;
        _reviewQueueProvider = reviewQueueProvider;
        _routingPolicyResolver = routingPolicyResolver;
    }

    public async Task<EventReportProviderSyncResult> SyncReportAsync(
        EventReportProviderEnvelope envelope,
        CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();

        var localResult = await _localProvider.SyncReportAsync(envelope, cancellationToken);
        if (!localResult.Succeeded)
        {
            return localResult;
        }

        ReportingRoutingPolicy policy = await _routingPolicyResolver.ResolveAsync(cancellationToken);
        if (!policy.ExternalSyncEnabled)
        {
            return localResult;
        }

        var signals = new List<EventSafetySignalEnvelope>();
        var externalLinks = new List<EventReportProviderExternalLinkEnvelope>();
        foreach (ReportingProviderTarget target in policy.OspreyTargets)
        {
            EventReportProviderEnvelope targetEnvelope = ApplyTarget(envelope, target, policy.EvidenceMode);
            var signalResult = await _signalProvider.EvaluateAsync(targetEnvelope, cancellationToken);
            if (!signalResult.Succeeded)
            {
                return EventReportProviderSyncResult.Failure(
                    signalResult.Error?.Category ?? "signal_provider_failed",
                    signalResult.Error?.IsTransient ?? true,
                    signalResult.Error?.SafeDetail);
            }

            signals.AddRange(signalResult.Signals);
            string? providerSignalId = signalResult.Signals.FirstOrDefault()?.ExternalSignalId;
            if (!string.IsNullOrWhiteSpace(providerSignalId))
            {
                externalLinks.Add(new EventReportProviderExternalLinkEnvelope(
                    target.Provider,
                    target.Scope,
                    target.TargetId,
                    ProviderSignalId: providerSignalId));
            }
        }

        foreach (ReportingProviderTarget target in policy.CoopTargets)
        {
            var reviewResult = await _reviewQueueProvider.MirrorCaseAsync(
                CreateReviewCaseEnvelope(envelope, target, policy.EvidenceMode),
                cancellationToken);
            if (!reviewResult.Succeeded && !reviewResult.ProviderDisabled)
            {
                return EventReportProviderSyncResult.Failure(
                    reviewResult.Error?.Category ?? "review_queue_provider_failed",
                    reviewResult.Error?.IsTransient ?? true,
                    reviewResult.Error?.SafeDetail);
            }

            if (reviewResult.Succeeded)
            {
                externalLinks.Add(new EventReportProviderExternalLinkEnvelope(
                    target.Provider,
                    target.Scope,
                    target.TargetId,
                    reviewResult.ProviderCaseId,
                    ProviderUrl: reviewResult.ProviderUrl));
            }
        }

        return EventReportProviderSyncResult.Success(
            providerCaseId: externalLinks.FirstOrDefault(link => !string.IsNullOrWhiteSpace(link.ProviderCaseId))?.ProviderCaseId,
            providerSignalId: externalLinks.FirstOrDefault(link => !string.IsNullOrWhiteSpace(link.ProviderSignalId))?.ProviderSignalId,
            providerUrl: externalLinks.FirstOrDefault(link => !string.IsNullOrWhiteSpace(link.ProviderUrl))?.ProviderUrl,
            signals,
            externalLinks);
    }

    private static EventReportProviderEnvelope ApplyTarget(
        EventReportProviderEnvelope envelope,
        ReportingProviderTarget target,
        EventReportProviderEvidenceMode evidenceMode) =>
        envelope with
        {
            EvidenceMode = evidenceMode,
            ProviderTargetScope = target.Scope,
            ProviderTargetId = target.TargetId,
            ProviderEndpointUrl = target.EndpointUrl,
            ProviderApiKey = target.ApiKey
        };

    private static ReviewCaseEnvelope CreateReviewCaseEnvelope(
        EventReportProviderEnvelope envelope,
        ReportingProviderTarget target,
        EventReportProviderEvidenceMode evidenceMode) =>
        new(
            envelope.TenantId,
            envelope.ReportId,
            envelope.EventId,
            envelope.CaseId,
            envelope.CaseConcurrencyStamp,
            target.Provider,
            envelope.QueueCode,
            envelope.CaseStatusCode,
            envelope.PriorityCode,
            envelope.ReasonCode,
            envelope.SubmittedAtUtc,
            null,
            envelope.IdempotencyKey,
            envelope.CorrelationId,
            evidenceMode,
            target.Scope,
            target.TargetId,
            target.EndpointUrl,
            target.ApiKey);
}
