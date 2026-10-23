using Explore.Application.Contracts.Persistence;
using Explore.Application.Features.Moderation.Policy;
using Explore.Application.Services.Moderation;
using Explore.Persistence.Repositories;
using ISLAMU.Moderation.Domain;

namespace Explore.Persistence.Services;

/// <summary>
/// Composes concrete native profile and prerequisite owners on the original borrowed transaction.
/// Every consumer requires its own genuine measured receipt and independently current runtime.
/// The finite inventory remains unchanged; missing owners or proof cannot activate a profile.
/// </summary>
public sealed class ModerationActivationSourceOwner(
    ExploreDbContext context, ModerationProfileDeploymentSourceOwner profiles,
    ModerationConstitutionSourceOwner constitution, ModerationRecognitionPrivacySourceOwner privacy,
    ModerationEventRightsSourceOwner eventRights, ModerationChildAgePolicySourceOwner childAge,
    ModerationMeasuredOperationsSourceOwner operations,
    ModerationConsumerConformanceReader? consumerConformance,
    IEnumerable<IModerationConsumerRuntimeSource> consumerSources, TimeProvider clock)
    : IModerationActivationSourceOwner
{
    public Task<ModerationActivationProfileObservation?> AcquireProfileAsync(
        IModerationConsumerEvidenceSession evidenceSession, Guid hostBindingId, Guid transactionId,
        ModerationPolicyCompiledDraft policy, ModerationPolicyCompiledProfile profile,
        ModerationDecision decision, DateTimeOffset observedAt, CancellationToken cancellationToken) =>
        profiles.AcquireProfileAsync(evidenceSession, hostBindingId, transactionId, policy, profile,
            decision, observedAt, cancellationToken);

    public async Task<ModerationCapabilityRegistration?> ReadConformanceAsync(
        IModerationConsumerEvidenceSession evidenceSession, Guid transactionId,
        ModerationActivationBinding binding, string consumerId, DateTimeOffset observedAt,
        CancellationToken cancellationToken)
    {
        if (consumerConformance is null
            || !await OwnsAsync(evidenceSession, transactionId, binding, observedAt, cancellationToken))
            return null;
        var runtime = consumerSources.SingleOrDefault(row => row.ConsumerId == consumerId && row.Mode == binding.Mode);
        if (runtime is null) return null;
        var registration = await consumerConformance.ReadAsync(
            evidenceSession, runtime, transactionId, binding, observedAt, cancellationToken);
        return registration is not null && clock.GetUtcNow() < registration.ValidUntil
            && await OwnsAsync(evidenceSession, transactionId, binding, observedAt, cancellationToken)
            ? registration : null;
    }

    public async Task<ModerationCapabilitySourceObservation?> AcquireConsumerAsync(
        IModerationConsumerEvidenceSession evidenceSession, Guid transactionId,
        ModerationActivationBinding binding, string consumerId, DateTimeOffset observedAt,
        CancellationToken cancellationToken)
    {
        if (!await OwnsAsync(evidenceSession, transactionId, binding, observedAt, cancellationToken))
            return null;
        var source = await evidenceSession.AcquireInstallationAsync(binding, consumerId, observedAt, cancellationToken);
        return source is not null && clock.GetUtcNow() < source.ValidUntil
            && await OwnsAsync(evidenceSession, transactionId, binding, observedAt, cancellationToken)
            ? source : null;
    }

    public async Task<ModerationPrerequisiteObservation?> AcquirePrerequisiteAsync(
        IModerationConsumerEvidenceSession evidenceSession, Guid transactionId,
        ModerationActivationBinding binding, ModerationActivationPrerequisite prerequisite,
        DateTimeOffset observedAt, CancellationToken cancellationToken)
    {
        if (evidenceSession is not ModerationConsumerEvidenceSession session
            || !await OwnsAsync(session, transactionId, binding, observedAt, cancellationToken)
            || await session.AcquireNativeAsync(transactionId, observedAt, cancellationToken) is not { } native
            || native.Policy.Prerequisites.SingleOrDefault(row => row.Rule == binding.Rule && row.Mode == binding.Mode)
                is not { } requirements || !requirements.ActivationGates.Contains(prerequisite))
            return null;
        var evidence = prerequisite switch
        {
            ModerationActivationPrerequisite.ConstitutionAndReviewers =>
                await constitution.AcquirePrerequisiteAsync(session, transactionId, binding, observedAt, cancellationToken),
            ModerationActivationPrerequisite.RecognitionAndPrivacy =>
                await privacy.AcquirePrerequisiteAsync(session, transactionId, binding, observedAt, cancellationToken),
            ModerationActivationPrerequisite.EventRightsAndPremises =>
                await eventRights.AcquirePrerequisiteAsync(session, transactionId, binding, observedAt, cancellationToken),
            ModerationActivationPrerequisite.ChildAndAgePolicy =>
                await childAge.AcquirePrerequisiteAsync(session, transactionId, binding, observedAt, cancellationToken),
            ModerationActivationPrerequisite.MeasuredOperations =>
                await operations.AcquirePrerequisiteAsync(session, transactionId, binding, observedAt, cancellationToken),
            _ => null
        };
        return evidence is not null && clock.GetUtcNow() < evidence.ValidUntil
            && await OwnsAsync(session, transactionId, binding, observedAt, cancellationToken) ? evidence : null;
    }

    private async Task<bool> OwnsAsync(
        IModerationConsumerEvidenceSession evidenceSession, Guid transactionId, ModerationActivationBinding binding,
        DateTimeOffset observedAt, CancellationToken cancellationToken) =>
        evidenceSession is ModerationConsumerEvidenceSession session
        && await session.AcquireNativeAsync(transactionId, observedAt, cancellationToken) is { } native
        && ReferenceEquals(native.Context, context)
        && ReferenceEquals(native.Transaction, context.Database.CurrentTransaction)
        && native.Policy.Consumers.Any(row => row.Binding == binding);
}
