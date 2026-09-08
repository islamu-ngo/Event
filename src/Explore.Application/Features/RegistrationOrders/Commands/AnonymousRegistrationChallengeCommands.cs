// ABOUTME: Owns tenant/event/visitor issuance policy and the trusted typed-request proof-consumption boundary.
// ABOUTME: Allocates no inventory and snapshots the already-digested request before granting internal authority.

using System.Collections.Immutable;
using System.Globalization;
using System.Text.Json;
using Explore.Application.Contracts.Infrastructure;
using Explore.Application.Contracts.Persistence;
using Explore.Application.Contracts.Services;
using Explore.Application.Contracts.Services.Registration;
using Explore.Application.DTOs.RegistrationOrders;
using Explore.Application.Features.RegistrationOrders.Requests.Commands;
using Explore.Application.Models;
using Explore.Application.Responses;
using Explore.Application.Services;
using Explore.Application.Settings;
using Explore.Domain;
using Explore.Domain.Constants;
using Explore.Domain.Enums;
using Explore.Domain.Settings.Definitions;
using MediatR;

namespace Explore.Application.Features.RegistrationOrders.Commands;

public sealed record IssueAnonymousRegistrationChallengeCommand(
    Guid EventId, string CanonicalRequestDigest, string IdempotencyKey)
    : IRequest<AnonymousRegistrationChallengeIssueResult>
{
    public override string ToString() => "IssueAnonymousRegistrationChallengeCommand { Redacted = true }";
}

public sealed record ConsumeAnonymousRegistrationChallengeCommand(
    Guid EventId, string CanonicalRequestDigest, string IdempotencyKey,
    string? ProtectedChallenge, string? Nonce, StartGuestRegistrationOrderCommand IntendedRequest)
    : IRequest<AnonymousRegistrationChallengeAuthority?>
{
    public override string ToString() => "ConsumeAnonymousRegistrationChallengeCommand { Redacted = true }";
}

public sealed record AnonymousRegistrationChallengeIssueResult : BaseCommandResponse<Guid>
{
    private AnonymousRegistrationChallengeIssueResult(BaseCommandResponse<Guid> state,
        AnonymousRegistrationChallengeDto? challenge) : base(state, true) => Challenge = challenge;

    public AnonymousRegistrationChallengeDto? Challenge { get; }

    public static AnonymousRegistrationChallengeIssueResult Issued(Guid eventId, AnonymousRegistrationChallengeDto challenge) =>
        new(BaseCommandResponse.Success(eventId, "Anonymous registration challenge issued."), challenge);

    public static AnonymousRegistrationChallengeIssueResult Denied(Guid eventId, string failureCode) =>
        new(BaseCommandResponse.Failure<Guid>(failureCode, "Anonymous registration challenge is unavailable.", id: eventId), null);

    public override string ToString() => "AnonymousRegistrationChallengeIssueResult { Redacted = true }";
}

public sealed class IssueAnonymousRegistrationChallengeCommandHandler(
    ITenantContext tenant,
    IEventRepository events,
    IVisitorAccessCapabilityResolver visitorCapabilities,
    IAnonymousRegistrationChallengeQuota quota,
    IAnonymousRegistrationChallengeService challenges,
    IUnitOfWork unitOfWork,
    ISettingMutationLock mutationLock,
    ISystemSettingRepository systemSettings,
    ITenantSettingRepository tenantSettings)
    : IRequestHandler<IssueAnonymousRegistrationChallengeCommand, AnonymousRegistrationChallengeIssueResult>
{
    public async Task<AnonymousRegistrationChallengeIssueResult> Handle(
        IssueAnonymousRegistrationChallengeCommand request, CancellationToken cancellationToken)
    {
        AnonymousRegistrationChallengeBinding binding;
        try
        {
            binding = new(tenant.TenantId, request.EventId, request.CanonicalRequestDigest, request.IdempotencyKey);
        }
        catch (ArgumentException)
        {
            return AnonymousRegistrationChallengeIssueResult.Denied(request.EventId, "anonymous_registration_challenge_invalid");
        }

        return await mutationLock.ExecuteOrderedGroupsAsync(
            [AnonymousRegistrationChallengeIssuePolicy.AuthoritySettingKeys],
            outerToken => unitOfWork.ExecuteBootstrapConvergenceAsync(async token =>
            {
                var eventTarget = await events.GetAuthorizationTargetByIdAsync(request.EventId, token);
                var capability = await visitorCapabilities.ResolveAsync(tenant.TenantId, token);
                if (!AnonymousRegistrationChallengeIssuePolicy.CanIssue(eventTarget, tenant.TenantId, capability)
                    || !await events.IsPubliclyEligibleAsync(tenant.TenantId, request.EventId, token))
                    return AnonymousRegistrationChallengeIssueResult.Denied(request.EventId, "anonymous_registration_challenge_unavailable");

                int? difficulty = await ReadDifficultyAsync(token);
                if (difficulty is null)
                    return AnonymousRegistrationChallengeIssueResult.Denied(request.EventId, "anonymous_registration_challenge_configuration_invalid");

                // The quota adapter requires this ambient transaction and atomically charges both scopes.
                // Native protection failure rolls back its debit; no inventory or plaintext intent is stored.
                if (!await quota.TryAcquireAsync(tenant.TenantId, request.EventId, token))
                    return AnonymousRegistrationChallengeIssueResult.Denied(request.EventId, "anonymous_registration_challenge_quota_exceeded");

                return AnonymousRegistrationChallengeIssueResult.Issued(request.EventId, challenges.Issue(binding, difficulty.Value));
            }, outerToken), cancellationToken);
    }

    private async Task<int?> ReadDifficultyAsync(CancellationToken cancellationToken)
    {
        var definition = AnonymousRegistrationChallengeSettingDefinitions.Difficulty;
        var instance = await systemSettings.GetByKey(definition.Key, cancellationToken);
        var tenantOverride = await tenantSettings.GetByTenantAndKey(tenant.TenantId, definition.Key, cancellationToken);
        Dictionary<string, SystemSetting> instanceValues = [];
        Dictionary<string, TenantSetting> tenantValues = [];
        if (instance is not null) instanceValues.Add(definition.Key, instance);
        if (tenantOverride is not null) tenantValues.Add(definition.Key, tenantOverride);
        string raw = HierarchicalSettingMerge.Resolve(definition.Key, instanceValues, tenantValues)!.Value;
        string? value;
        try
        {
            value = JsonSerializer.Deserialize<string>(raw);
        }
        catch (JsonException)
        {
            return null;
        }

        return value is not null && definition.AllowedValues!.Contains(value)
            ? int.Parse(value, CultureInfo.InvariantCulture) : null;
    }
}

public static class AnonymousRegistrationChallengeIssuePolicy
{
    public static IReadOnlyList<string> AuthoritySettingKeys { get; } = VisitorAccessCapabilityResolver.AuthoritySettingKeys
        .Concat([
            GovernanceSettingKeys.AnonymousRegistrationChallenge.TenantPermitsPerMinute,
            GovernanceSettingKeys.AnonymousRegistrationChallenge.EventPermitsPerMinute,
            GovernanceSettingKeys.AnonymousRegistrationChallenge.Difficulty
        ])
        .Order(StringComparer.Ordinal).ToImmutableArray();

    public static bool CanIssue(Explore.Domain.Event? eventTarget, Guid tenantId, VisitorAccessCapability capability) =>
        tenantId != Guid.Empty && capability.AllowsAnonymousParticipation
        && eventTarget is
        {
            IsDeleted: false,
            EventStatusId: (int)EventStatusEnum.Published,
            VisibilityTypeId: (int)VisibilityTypeEnum.Public,
            ParticipationConfiguration:
            {
                IsDeleted: false,
                ParticipationHandlingModeId: (int)ParticipationHandlingModeEnum.PlatformManaged,
                IdentityAccessModeId: (int)IdentityAccessModeEnum.GuestAllowed or (int)IdentityAccessModeEnum.CapabilityTokenAllowed
            } participation
        }
        && eventTarget.TenantId == tenantId && participation.TenantId == tenantId && participation.Id == eventTarget.Id;
}

public sealed class ConsumeAnonymousRegistrationChallengeCommandHandler(
    ITenantContext tenant, IAnonymousRegistrationChallengeService challenges)
    : IRequestHandler<ConsumeAnonymousRegistrationChallengeCommand, AnonymousRegistrationChallengeAuthority?>
{
    public Task<AnonymousRegistrationChallengeAuthority?> Handle(
        ConsumeAnonymousRegistrationChallengeCommand request, CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();
        AnonymousRegistrationChallengeBinding binding;
        try
        {
            binding = new(tenant.TenantId, request.EventId, request.CanonicalRequestDigest, request.IdempotencyKey);
        }
        catch (ArgumentException)
        {
            return Task.FromResult<AnonymousRegistrationChallengeAuthority?>(null);
        }

        if (request.IntendedRequest is null || request.IntendedRequest.EventId != request.EventId
            || request.IntendedRequest.Lines is null)
            return Task.FromResult<AnonymousRegistrationChallengeAuthority?>(null);

        var validated = challenges.Validate(binding, request.ProtectedChallenge, request.Nonce);
        return Task.FromResult<AnonymousRegistrationChallengeAuthority?>(validated is null
            ? null : new BoundAuthority(validated, request.IntendedRequest));
    }

    private sealed class BoundAuthority : AnonymousRegistrationChallengeAuthority
    {
        private readonly Guid _catalogId;
        private readonly BookingPartyTypeEnum _bookingPartyType;
        private readonly int? _contribution;
        private readonly ImmutableArray<RegistrationOrderLineSelection> _lines;

        internal BoundAuthority(AnonymousRegistrationChallengeAuthority validated, StartGuestRegistrationOrderCommand intended)
            : base(validated.TenantId, validated.EventId, validated.OrderId, validated.ExpiresAt,
                validated.GuestCapabilityToken, validated.GuestAccessTokenHash)
        {
            _catalogId = intended.TicketCatalogVersionId;
            _bookingPartyType = intended.BookingPartyType;
            _contribution = intended.PlatformContributionBasisPoints;
            _lines = intended.Lines.ToImmutableArray();
        }

        public override bool Matches(StartGuestRegistrationOrderCommand request) =>
            MatchesSelection(request.EventId, request.TicketCatalogVersionId, request.BookingPartyType,
                request.PlatformContributionBasisPoints, request.Lines);

        public override bool Matches(CreateRegistrationOrderWithHoldCommand request) =>
            request.AccountUserId is null && request.PurchaserActorId is null
            && request.VerifiedContactNormalizedEmail is null && request.GuestAccessTokenHash == GuestAccessTokenHash
            && MatchesSelection(request.EventId, request.TicketCatalogVersionId, request.BookingPartyType,
                request.PlatformContributionBasisPoints, request.Lines);

        private bool MatchesSelection(Guid eventId, Guid catalogId, BookingPartyTypeEnum bookingPartyType,
            int? contribution, IReadOnlyList<RegistrationOrderLineSelection>? lines) =>
            eventId == EventId && catalogId == _catalogId && bookingPartyType == _bookingPartyType
            && contribution == _contribution && lines is not null && _lines.SequenceEqual(lines);
    }
}
