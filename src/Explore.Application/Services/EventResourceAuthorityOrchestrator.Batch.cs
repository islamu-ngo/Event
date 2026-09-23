using Explore.Application.Contracts.Services;

namespace Explore.Application.Services;

public sealed partial class EventResourceAuthorityOrchestrator
{
    /// <summary>
    /// Returns non-reusable capability decisions from shared A/provider/B reads. Native callers retain
    /// their request cancellation deadline; an explicit earlier deadline is never reset on retry.
    /// Mutation handlers still recheck authority inside their own transaction.
    /// </summary>
    public async Task<IReadOnlyList<EventResourceAuthorityOutcome>> AuthorizeCapabilitiesAsync(
        IReadOnlyList<EventResourceAuthorityRequest> requests, CancellationToken cancellationToken = default)
    {
        if (requests.Count == 0) return [];
        if (requests.Count > EventResourceAuthorityRequest.MaximumBatchChecks)
            return requests.Select(_ => EventResourceAuthorityOutcome.Forbidden).ToArray();
        var targets = requests.ToArray();
        var first = targets[0];
        if (first.TenantId == Guid.Empty || first.SubjectUserId == Guid.Empty
            || targets.Any(request => request.TenantId != first.TenantId
                || request.SubjectUserId != first.SubjectUserId || request.IsMachineCaller != first.IsMachineCaller
                || request.ResourceId == Guid.Empty)
            || targets.Select(request => request.ResourceId).Distinct().Count()
                > EventResourceAuthorityRequest.MaximumBatchResources)
            return targets.Select(_ => EventResourceAuthorityOutcome.Forbidden).ToArray();

        var initialTime = timeProvider.GetUtcNow();
        var deadlineUtc = targets.Min(request => request.DeadlineUtc);
        if (deadlineUtc <= initialTime)
            return targets.Select(_ => EventResourceAuthorityOutcome.Expired).ToArray();
        using var deadline = deadlineUtc is { } end
            ? new CancellationTokenSource(end - initialTime, timeProvider) : null;
        using var linked = deadline is null ? null
            : CancellationTokenSource.CreateLinkedTokenSource(cancellationToken, deadline.Token);
        var ct = linked?.Token ?? cancellationToken;
        var conceal = new bool[targets.Length];
        try
        {
            for (int attempt = 0; attempt < 2; attempt++)
            {
                ct.ThrowIfCancellationRequested();
                var aTime = attempt == 0 ? initialTime : timeProvider.GetUtcNow();
                var a = await unitOfWork.ExecuteSerializableAsync(
                    token => ReadBatchAsync(targets, aTime, token), ct);
                var outcomes = new EventResourceAuthorityOutcome[targets.Length];
                var providerIndexes = new List<int>();
                for (int index = 0; index < targets.Length; index++)
                {
                    conceal[index] = ConcealsFailure(targets[index], a[index]);
                    outcomes[index] = Denial(targets[index], a[index]) ?? EventResourceAuthorityOutcome.Allowed;
                    if (outcomes[index] == EventResourceAuthorityOutcome.Unavailable && conceal[index])
                        outcomes[index] = EventResourceAuthorityOutcome.NotFound;
                    if (outcomes[index] == EventResourceAuthorityOutcome.Allowed
                        && a[index]!.Evaluation.ProviderInput is not null)
                        providerIndexes.Add(index);
                }
                if (providerIndexes.Count != 0)
                {
                    var inputs = providerIndexes.Select(index => a[index]!.Evaluation.ProviderInput!).ToArray();
                    var decisions = await provider.CheckBatchAsync(inputs, ct);
                    if (decisions is null || decisions.Count != inputs.Length)
                        return targets.Select((_, index) => conceal[index]
                            ? EventResourceAuthorityOutcome.NotFound : EventResourceAuthorityOutcome.Unavailable).ToArray();
                    for (int index = 0; index < decisions.Count; index++)
                    {
                        int position = providerIndexes[index];
                        outcomes[position] = decisions[index] switch
                        {
                            EventResourceProviderDecision.Allow => EventResourceAuthorityOutcome.Allowed,
                            _ when conceal[position] => EventResourceAuthorityOutcome.NotFound,
                            EventResourceProviderDecision.Deny => EventResourceAuthorityOutcome.Forbidden,
                            _ => EventResourceAuthorityOutcome.Unavailable
                        };
                    }
                }
                ct.ThrowIfCancellationRequested();
                if (!outcomes.Contains(EventResourceAuthorityOutcome.Allowed)) return outcomes;

                var bTime = timeProvider.GetUtcNow();
                var b = await unitOfWork.ExecuteSerializableAsync(
                    token => ReadBatchAsync(targets, bTime, token), ct);
                ct.ThrowIfCancellationRequested();
                bool changed = false;
                for (int index = 0; index < targets.Length; index++)
                {
                    if (outcomes[index] != EventResourceAuthorityOutcome.Allowed) continue;
                    var denial = Denial(targets[index], b[index]);
                    if (denial.HasValue)
                    {
                        outcomes[index] = denial.Value == EventResourceAuthorityOutcome.Unavailable && conceal[index]
                            ? EventResourceAuthorityOutcome.NotFound : denial.Value;
                    }
                    else if (!SameAuthority(a[index]!, b[index]!))
                    {
                        changed = true;
                        outcomes[index] = conceal[index]
                            ? EventResourceAuthorityOutcome.NotFound : EventResourceAuthorityOutcome.Unavailable;
                    }
                }
                if (changed && attempt == 0) continue;

                var finalTime = timeProvider.GetUtcNow();
                ct.ThrowIfCancellationRequested();
                for (int index = 0; index < targets.Length; index++)
                {
                    if (outcomes[index] != EventResourceAuthorityOutcome.Allowed) continue;
                    var snapshot = b[index]!;
                    var final = snapshot.Facts.Evaluate(targets[index], snapshot.Route, finalTime);
                    if (deadlineUtc <= finalTime)
                        outcomes[index] = EventResourceAuthorityOutcome.Expired;
                    else if (!final.Allowed || final.ProviderInput != snapshot.Evaluation.ProviderInput
                        || final.Disclosure != snapshot.Evaluation.Disclosure)
                        outcomes[index] = conceal[index]
                            ? EventResourceAuthorityOutcome.NotFound : EventResourceAuthorityOutcome.Forbidden;
                }
                return outcomes;
            }
            return targets.Select(_ => EventResourceAuthorityOutcome.Unavailable).ToArray();
        }
        catch (OperationCanceledException)
        {
            return targets.Select(_ => cancellationToken.IsCancellationRequested
                ? EventResourceAuthorityOutcome.Cancelled : EventResourceAuthorityOutcome.Expired).ToArray();
        }
        catch (Exception)
        {
            return targets.Select((_, index) => conceal[index]
                ? EventResourceAuthorityOutcome.NotFound : EventResourceAuthorityOutcome.Unavailable).ToArray();
        }
    }

    private async Task<EventResourceAuthoritySnapshot?[]> ReadBatchAsync(
        EventResourceAuthorityRequest[] requests, DateTimeOffset evaluationUtc, CancellationToken ct)
    {
        var facts = await reader.ReadBatchAsync(requests, evaluationUtc, ct);
        if (facts is null || facts.Count != requests.Length)
            throw new InvalidOperationException("Resource authority reader returned an incomplete batch.");
        var first = requests[0];
        var route = first.SubjectUserId.HasValue && !first.IsMachineCaller
            ? await routes.ReadAsync(first.TenantId, ct) : null;
        return facts.Select((fact, index) => fact is null ? null
            : new EventResourceAuthoritySnapshot(fact, route, fact.Evaluate(requests[index], route, evaluationUtc))).ToArray();
    }

    private static bool ConcealsFailure(EventResourceAuthorityRequest request, EventResourceAuthoritySnapshot? snapshot) =>
        request.Action is "view" or "access" or "download"
        && snapshot?.Facts.Policy is { DisclosureModeId: (int)Domain.Enums.EventResourceDisclosureModeEnum.EligibleOnly } policy
        && !policy.AudienceRules.Any(rule => rule.AudienceKindId == (int)Domain.Enums.EventResourceAudienceKindEnum.Public);
}
