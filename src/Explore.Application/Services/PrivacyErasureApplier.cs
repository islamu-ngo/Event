using Explore.Application.Caching;
using Explore.Application.Configuration;
using Explore.Application.Contracts.Persistence;
using Explore.Application.Contracts.Services;
using Explore.Domain;
using Explore.Domain.Enums;
using Microsoft.Extensions.Caching.Hybrid;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

namespace Explore.Application.Services;

public sealed class PrivacyErasureApplier(
    IUserRepository userRepository,
    IGenericRepository<UserPii, Guid> userPiiRepository,
    IUserAuthenticationTokenRepository tokenRepository,
    IUserLocationPrivacyErasureRepository erasureRepository,
    IUserPrivacyErasureRepository privacyErasureRepository,
    IAiConversationRepository aiConversationRepository,
    IPrivacyErasureProviderWorkRepository providerWorkRepository,
    IPrivacyErasureProviderLocatorProtector providerLocatorProtector,
    IPrivacyErasureReplayCheckpointRepository checkpointRepository,
    IPrivacyErasureStateRepository stateRepository,
    IOutboxRepository outboxRepository,
    HybridCache cache,
    TimeProvider timeProvider,
    ILogger<PrivacyErasureApplier> logger,
    IOptions<PrivacyErasureOptions> options)
{
    private readonly PrivacyErasureOptions _options = options.Value;

    public async Task<PreparedErasure> PrepareAsync(
        PrivacyErasureIntent intent,
        CancellationToken cancellationToken)
    {
        IReadOnlyList<Location> homes = await erasureRepository.GetOwnedPrivateHomesAsync(
            intent.SubjectId,
            cancellationToken);
        IReadOnlyList<EventLocation> eventLocations = await erasureRepository.GetEventLocationsAsync(
            homes.Select(home => home.Id).ToArray(),
            cancellationToken);
        DateTime appliedAtUtc = timeProvider.GetUtcNow().UtcDateTime;
        if (appliedAtUtc < intent.RecordedAtUtc)
        {
            appliedAtUtc = intent.RecordedAtUtc;
        }

        return new PreparedErasure(
            homes.ToDictionary(home => home.Id, _ => Guid.CreateVersion7()),
            eventLocations.ToDictionary(item => item.Id, _ => Guid.CreateVersion7()),
            Guid.CreateVersion7(),
            Guid.CreateVersion7(),
            appliedAtUtc);
    }

    public async Task<AppliedErasure> ApplyInCurrentTransactionAsync(
        PrivacyErasureIntent intent,
        PreparedErasure prepared,
        CancellationToken cancellationToken)
    {
        PrivacyErasureReplayCheckpoint? current =
            await checkpointRepository.GetLatestAsync(cancellationToken);
        if (current?.Matches(intent) == true
            && await stateRepository.HasCoverageAsync(
                intent.IntentId,
                _options.CurrentPolicyVersion,
                cancellationToken))
        {
            return AppliedErasure.None;
        }

        if (current?.AuthoritySequence == intent.AuthoritySequence && !current.Matches(intent))
        {
            throw new InvalidOperationException(
                "The application erasure checkpoint does not match the retained authority.");
        }

        PrivacyErasureReplayCheckpoint? checkpoint = current switch
        {
            null => PrivacyErasureReplayCheckpoint.Start(
                intent,
                prepared.AppliedAtUtc,
                prepared.CheckpointId),
            { AuthoritySequence: var sequence } when sequence < intent.AuthoritySequence =>
                PrivacyErasureReplayCheckpoint.Advance(
                    current,
                    intent,
                    prepared.AppliedAtUtc,
                    prepared.CheckpointId),
            _ => null
        };
        IReadOnlyList<Location> homes = await erasureRepository.GetOwnedPrivateHomesAsync(
            intent.SubjectId,
            cancellationToken);
        IReadOnlyList<EventLocation> eventLocations = await erasureRepository.GetEventLocationsAsync(
            homes.Select(home => home.Id).ToArray(),
            cancellationToken);
        IReadOnlyList<Actor> actors = await erasureRepository.GetUserActorsAsync(
            intent.SubjectId,
            cancellationToken);
        IReadOnlyList<PrivacyErasureProviderCandidate> providerCandidates =
            await privacyErasureRepository.GetProviderCandidatesAsync(intent.SubjectId, cancellationToken);
        DateTime locatorExpiresAtUtc = prepared.AppliedAtUtc + _options.ProviderLocatorLifetime;
        PrivacyErasureProviderWork[] providerWork = providerCandidates
            .DistinctBy(candidate => (
                candidate.ProviderKind,
                candidate.Action,
                candidate.TenantId,
                candidate.TargetId))
            .Select(candidate => PrivacyErasureProviderWork.Create(
                Guid.CreateVersion7(),
                intent,
                candidate.ProviderKind,
                candidate.Action,
                candidate.TenantId,
                candidate.TargetId,
                candidate.LocatorKind,
                providerLocatorProtector.Protect(candidate.Locator, _options.ProviderLocatorLifetime),
                providerLocatorProtector.CurrentVersion,
                locatorExpiresAtUtc,
                prepared.AppliedAtUtc))
            .ToArray();
        int providerWorkCount = await providerWorkRepository.AddMissingAsync(providerWork, cancellationToken);
        await stateRepository.SaveChangesAsync(cancellationToken);

        await privacyErasureRepository.EraseProviderBackedLocalUserMetadataAsync(
            intent.SubjectId,
            cancellationToken);
        await privacyErasureRepository.AnonymizeRetainedAuditEvidenceAsync(
            intent.SubjectId,
            cancellationToken);
        await privacyErasureRepository.EraseRegistrationAndLocalNotificationsAsync(
            intent.SubjectId,
            cancellationToken);
        await privacyErasureRepository.EraseMembershipsAndPreferencesAsync(
            intent.SubjectId,
            cancellationToken);
        await aiConversationRepository.HardDeleteUserConversationGraphAsync(
            intent.SubjectId,
            cancellationToken);

        var audits = new List<EventLocationDisclosureAudit>(eventLocations.Count);
        foreach (EventLocation eventLocation in eventLocations)
        {
            audits.Add(eventLocation.ChangeDisclosurePolicy(
                EventLocationDisclosureFields.None,
                LocationDisclosureAudienceEnum.Never,
                null,
                eventLocation.PolicyVersion,
                intent.SubjectId,
                EventLocationDisclosureAuditReasonEnum.PrivacyErasureRemediation,
                prepared.AppliedAtUtc,
                needsPrivacyReview: true));
        }

        foreach (Location home in homes)
        {
            home.EraseOwnedPii(prepared.AppliedAtUtc, ToLocationReason(intent.ReasonCode));
        }

        foreach (Actor actor in actors)
        {
            actor.TombstoneForUserPrivacyErasure(prepared.AppliedAtUtc, intent.SubjectId);
            foreach (AtprotoIdentity identity in actor.AtprotoIdentities)
            {
                identity.EraseForPrivacy(prepared.AppliedAtUtc);
            }

            if (actor.Pii is null)
            {
                continue;
            }

            actor.Pii.DisplayName = "Deleted user";
            actor.Pii.ProfilePictureUri = null;
        }

        await erasureRepository.SaveChangesAsync(audits, cancellationToken);
        User? user = await userRepository.GetById(intent.SubjectId);
        UserPii? userPii = await userPiiRepository.GetById(intent.SubjectId);
        if (userPii is not null)
        {
            await userPiiRepository.Delete(userPii);
        }

        IReadOnlyList<UserAuthenticationToken> tokens =
            await tokenRepository.GetByUser(intent.SubjectId, cancellationToken);
        foreach (UserAuthenticationToken token in tokens)
        {
            await tokenRepository.Delete(token);
        }

        if (user is not null)
        {
            user.IsDeleted = true;
            user.DeletedAt = prepared.AppliedAtUtc;
            user.DeletedBy = intent.SubjectId;
            await userRepository.Update(user);
        }

        if (checkpoint is not null)
        {
            await checkpointRepository.AppendAsync(checkpoint, cancellationToken);
        }
        await stateRepository.AddCoverageAsync(
            PrivacyErasurePolicyCoverage.Record(
                intent,
                _options.CurrentPolicyVersion,
                prepared.AppliedAtUtc),
            cancellationToken);
        PrivacyErasureSaga? saga = await stateRepository.GetByIntentAsync(intent.IntentId, cancellationToken);
        if (saga?.Status == PrivacyErasureSagaStatus.Fenced)
        {
            saga.MarkLocalSettled(prepared.AppliedAtUtc, providerWorkCount, saga.ConcurrencyToken);
        }

        await stateRepository.SaveChangesAsync(cancellationToken);
        var messages = new List<OutboxMessage>(homes.Count + eventLocations.Count + 1)
        {
            PrivacyErasureCacheInvalidationOutboxMessageFactory.Create(
                prepared.CacheConvergenceMessageId,
                intent.SubjectId,
                prepared.AppliedAtUtc)
        };
        messages.AddRange(homes.Select(home => LocationPrivacyOutboxMessageFactory.CreateLocationErased(
            prepared.LocationMessageIds.TryGetValue(home.Id, out Guid id)
                ? id
                : throw new InvalidOperationException("The owned Home set changed while applying the erasure intent."),
            intent,
            home,
            prepared.AppliedAtUtc)));
        messages.AddRange(eventLocations.Select(eventLocation => LocationPrivacyOutboxMessageFactory.CreateCorrectionRequested(
            prepared.CorrectionMessageIds.TryGetValue(eventLocation.Id, out Guid id)
                ? id
                : throw new InvalidOperationException("The EventLocation correction set changed while applying the erasure intent."),
            intent,
            eventLocation,
            prepared.AppliedAtUtc)));
        await outboxRepository.CreateRange(messages, cancellationToken);

        return new AppliedErasure(
            intent.SubjectId,
            homes.Select(home => home.TenantId).Distinct().ToArray(),
            eventLocations.Select(item => new CorrectedEventLocation(item.TenantId, item.EventId, item.Id)).ToArray());
    }

    public async Task InvalidateAfterCommitAsync(AppliedErasure applied)
    {
        if (applied == AppliedErasure.None)
        {
            return;
        }

        try
        {
            await cache.RemoveAsync($"user:detail:{applied.UserId}", CancellationToken.None);
            await cache.RemoveByTagAsync(CacheTags.Events, CancellationToken.None);
            await cache.RemoveByTagAsync(CacheTags.EventLists, CancellationToken.None);
            await cache.RemoveByTagAsync(CacheTags.EventDetails, CancellationToken.None);
            await cache.RemoveByTagAsync(CacheTags.EventLocations, CancellationToken.None);
            foreach (Guid tenantId in applied.TenantIds)
            {
                await cache.RemoveByTagAsync(CacheTags.EventLocationsByTenant(tenantId), CancellationToken.None);
                await cache.RemoveByTagAsync(CacheTags.EventListByTenant(tenantId), CancellationToken.None);
            }

            foreach (CorrectedEventLocation corrected in applied.CorrectedEventLocations)
            {
                await cache.RemoveByTagAsync(CacheTags.Event(corrected.EventId), CancellationToken.None);
                await cache.RemoveByTagAsync(CacheTags.EventLocationsByEvent(corrected.EventId), CancellationToken.None);
                await cache.RemoveByTagAsync(CacheTags.EventLocation(corrected.EventLocationId), CancellationToken.None);
            }
        }
        catch (Exception)
        {
            logger.LogWarning("Post-commit privacy-erasure cache invalidation failed.");
        }
    }

    public async Task InvalidateRetainedIntentAsync(
        PrivacyErasureIntent intent,
        CancellationToken cancellationToken)
    {
        IReadOnlyList<Location> homes = await erasureRepository.GetOwnedPrivateHomesAsync(
            intent.SubjectId,
            cancellationToken);
        IReadOnlyList<EventLocation> eventLocations = await erasureRepository.GetEventLocationsAsync(
            homes.Select(home => home.Id).ToArray(),
            cancellationToken);
        await InvalidateAfterCommitAsync(new AppliedErasure(
            intent.SubjectId,
            homes.Select(home => home.TenantId)
                .Concat(eventLocations.Select(item => item.TenantId))
                .Distinct()
                .ToArray(),
            eventLocations.Select(item => new CorrectedEventLocation(item.TenantId, item.EventId, item.Id)).ToArray()));
    }

    private static LocationPrivacyErasureReasonEnum ToLocationReason(PrivacyErasureReasonCode reasonCode) =>
        reasonCode switch
        {
            PrivacyErasureReasonCode.AccountDeletion => LocationPrivacyErasureReasonEnum.AccountDeletion,
            PrivacyErasureReasonCode.SubjectErasureRequest => LocationPrivacyErasureReasonEnum.OwnerErasureRequest,
            PrivacyErasureReasonCode.PrivacyIncidentRemediation =>
                LocationPrivacyErasureReasonEnum.PrivacyIncidentRemediation,
            _ => throw new ArgumentOutOfRangeException(nameof(reasonCode))
        };

    public sealed record PreparedErasure(
        IReadOnlyDictionary<Guid, Guid> LocationMessageIds,
        IReadOnlyDictionary<Guid, Guid> CorrectionMessageIds,
        Guid CheckpointId,
        Guid CacheConvergenceMessageId,
        DateTime AppliedAtUtc);

    public sealed record AppliedErasure(
        Guid UserId,
        IReadOnlyList<Guid> TenantIds,
        IReadOnlyList<CorrectedEventLocation> CorrectedEventLocations)
    {
        public static AppliedErasure None { get; } = new(Guid.Empty, [], []);
    }

    public sealed record CorrectedEventLocation(Guid TenantId, Guid EventId, Guid EventLocationId);
}
