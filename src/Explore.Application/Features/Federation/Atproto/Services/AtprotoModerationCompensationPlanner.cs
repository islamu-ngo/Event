using Explore.Application.Contracts.Infrastructure;
using Explore.Application.Contracts.Operations;
using Explore.Application.Services.Federation;
using Explore.Application.Settings;
using Explore.Domain.Constants;
using Microsoft.Extensions.Logging;

namespace Explore.Application.Features.Federation.Atproto.Services;

public sealed class AtprotoModerationCompensationPlanner(
    IAtprotoModerationCompensationStore store,
    ILogger<AtprotoModerationCompensationPlanner> logger)
{
    public async Task PlanDeletesAsync(
        AtprotoModerationTarget target,
        IDictionary<AtprotoPublicationLineage, Guid> successorIds,
        DateTime createdAtUtc,
        CancellationToken cancellationToken)
    {
        await foreach (AtprotoModerationDeleteCandidate candidate in
                       store.ReadDeleteCandidatesAsync(target, cancellationToken))
        {
            var resolved = new Dictionary<string, ResolvedSetting>(StringComparer.Ordinal);
            AddPolicy(GovernanceSettingKeys.Federation.AtprotoEventsEnabled, candidate.Policy.EventsEnabled);
            AddPolicy(GovernanceSettingKeys.Federation.AtprotoEventValidationProfile, candidate.Policy.ValidationProfile);
            AddPolicy(GovernanceSettingKeys.Federation.AtprotoPublishMyEvents, candidate.Policy.PublishConsent);
            AtprotoEventGovernance governance = AtprotoEventGovernanceResolver.Evaluate(resolved);
            if (!governance.EventsEnabled || !governance.PublishMyEvents)
            {
                Skip(!governance.EventsEnabled ? "capability_disabled" : "consent_missing");
                continue;
            }

            AtprotoAccountBindingFacts account = candidate.Account;
            if (account.SessionCount != 1
                || !string.Equals(account.SubjectDid, candidate.Lineage.Did, StringComparison.Ordinal)
                || !Uri.TryCreate(account.PdsHost, UriKind.Absolute, out Uri? pds)
                || pds.Scheme != Uri.UriSchemeHttps
                || !string.IsNullOrEmpty(pds.Query)
                || !string.IsNullOrEmpty(pds.Fragment))
            {
                Skip("session_missing");
                continue;
            }
            if (!account.ExactExternalLoginExists)
            {
                Skip("account_not_linked");
                continue;
            }
            if (candidate.Unsettled.Any(mutation => !AtprotoEventPublicationPlanner.SamePds(pds, mutation.PdsHost)))
            {
                Skip("session_binding_changed");
                continue;
            }
            if (candidate.Unsettled.IsEmpty && string.IsNullOrWhiteSpace(candidate.Settled?.Cid))
            {
                Skip("remote_record_missing");
                continue;
            }

            DateTime? notBefore = null;
            foreach (AtprotoModerationMutation mutation in candidate.Unsettled)
            {
                DateTime? delay = AtprotoEventPublicationPlanner.CompensationNotBefore(
                    mutation.Status, mutation.NextRetryAt, mutation.LeaseExpiresAt, createdAtUtc);
                if (delay.HasValue && (!notBefore.HasValue || delay.Value > notBefore.Value))
                    notBefore = delay;
            }
            if (!successorIds.TryGetValue(candidate.Lineage, out Guid successorId))
            {
                successorId = Guid.CreateVersion7();
                successorIds.Add(candidate.Lineage, successorId);
            }
            await store.WriteDeleteAsync(target, new AtprotoModerationDeleteWrite(
                candidate.Lineage, candidate.Fence, successorId, createdAtUtc, pds.AbsoluteUri,
                candidate.Settled?.RecordId,
                candidate.Unsettled.IsEmpty ? candidate.Settled?.Cid : null,
                notBefore), cancellationToken);

            void AddPolicy(string key, SettingMergeFacts facts)
            {
                if (HierarchicalSettingMerge.Resolve(key, facts) is { } value)
                    resolved.Add(key, value);
            }
        }
    }

    private void Skip(string reasonCode) =>
        logger.LogInformation("ATProto moderation compensation skipped with bounded reason {ReasonCode}", reasonCode);
}
