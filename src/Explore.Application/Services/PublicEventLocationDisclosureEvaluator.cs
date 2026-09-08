using Explore.Application.Contracts.LocationPrivacy;
using Explore.Application.Contracts.Services;
using Explore.Domain;

namespace Explore.Application.Services;

public sealed record PublicEventLocationDisclosureMemoKey(Guid EventLocationId, Guid? RoomId);

public sealed record PublicEventLocationDisclosureInput(
    Guid TenantId,
    Guid EventId,
    Guid EventLocationId,
    Guid? RoomId,
    EventLocation? EventLocation,
    Location? Location,
    LocationRoom? Room,
    DateTimeOffset ServerNowUtc,
    EventLocationDisclosureDerivativeValues? Derivatives)
{
    public PublicEventLocationDisclosureMemoKey MemoKey => new(EventLocationId, RoomId);
}

public sealed class PublicEventLocationDisclosureEvaluator(
    ILocationPrivacyGovernanceService governanceService,
    EventLocationDisclosureEvaluator evaluator)
{
    public async Task<EventLocationDisclosureResult> EvaluateAsync(
        PublicEventLocationDisclosureInput input,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(input);
        EffectiveLocationPrivacyGovernance governance = await governanceService.ResolveAsync(
            input.TenantId,
            cancellationToken);
        return Evaluate(input, governance);
    }

    public async Task<IReadOnlyDictionary<PublicEventLocationDisclosureMemoKey, EventLocationDisclosureResult>> EvaluateManyAsync(
        IReadOnlyCollection<PublicEventLocationDisclosureInput> inputs,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(inputs);
        if (inputs.Count == 0)
        {
            return new Dictionary<PublicEventLocationDisclosureMemoKey, EventLocationDisclosureResult>();
        }

        Guid tenantId = inputs.First().TenantId;
        if (inputs.Any(input => input.TenantId != tenantId))
        {
            throw new ArgumentException("All public disclosure inputs must belong to one tenant.", nameof(inputs));
        }

        EffectiveLocationPrivacyGovernance governance = await governanceService.ResolveAsync(
            tenantId,
            cancellationToken);
        return inputs
            .GroupBy(input => input.MemoKey)
            .ToDictionary(group => group.Key, group => Evaluate(group.First(), governance));
    }

    private EventLocationDisclosureResult Evaluate(
        PublicEventLocationDisclosureInput input,
        EffectiveLocationPrivacyGovernance governance)
    {
        var request = new EventLocationDisclosureRequest(
            input.TenantId,
            input.EventId,
            input.EventLocationId,
            input.RoomId,
            RequesterUserId: null,
            EventLocationDisclosurePurpose.Public);
        EventLocationDisclosureAuthorityFact? authority = input.TenantId != Guid.Empty
            && input.EventId != Guid.Empty
            && input.EventLocationId != Guid.Empty
                ? EventLocationDisclosureAuthorityFact.ForPublic(
                    input.TenantId,
                    input.EventId,
                    input.EventLocationId)
                : null;

        return evaluator.Evaluate(new(
            request,
            input.EventLocation,
            input.Location,
            input.Room,
            new(
                governance.IsResolved,
                governance.AllowHomeLocations,
                governance.AllowPublicExactAddress,
                governance.AllowPublicCoordinates,
                governance.MinimumHomeAudience,
                governance.DefaultRevealOffset),
            authority,
            input.ServerNowUtc,
            input.Derivatives));
    }
}
