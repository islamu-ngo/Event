using Explore.Application.Authentication;
using Explore.Application.Contracts.Infrastructure;
using Explore.Application.Contracts.Persistence;
using Explore.Application.Features.Federation.Atproto.Services;
using Explore.Application.Services.Federation;
using Explore.Application.Settings;
using Explore.Domain;
using Explore.Domain.Constants;
using Explore.Domain.Federation;
using Explore.Domain.ValueObjects;
using NSubstitute;
using System.Collections.Immutable;

namespace Event.Application.UnitTests.Common;

internal static class AtprotoPublicationPlannerTestFactory
{
    public static AtprotoEventPublicationPlanner Disabled()
    {
        var settings = Substitute.For<IHierarchicalSettingsResolver>();
        settings.ResolveBatchAsync(
                Arg.Any<IEnumerable<string>>(),
                Arg.Any<SettingContext>(),
                Arg.Any<CancellationToken>())
            .Returns(call => call.Arg<IEnumerable<string>>().Select(key => new ResolvedSetting
            {
                Key = key,
                Value = key == GovernanceSettingKeys.Federation.AtprotoEventValidationProfile
                    ? "\"platform\""
                    : "false",
                Source = SettingSource.SystemDefault
            }).ToArray());
        return new(
            new AtprotoEventGovernanceResolver(settings),
            Substitute.For<IEventRepository>(),
            Substitute.For<IAtprotoRecordRepository>(),
            Substitute.For<IAtprotoSessionBindingReader>(),
            Substitute.For<IUserExternalLoginRepository>(),
            Substitute.For<IAtprotoPublicationPayloadBuilder>(),
            Substitute.For<IPdsSyncOutboxRepository>(),
            Microsoft.Extensions.Logging.Abstractions.NullLogger<AtprotoEventPublicationPlanner>.Instance);
    }

    public static AtprotoEventPublicationPlanner ExistingEventDelete(
        Guid tenantId,
        Guid eventId,
        Guid ownerUserId,
        IPdsSyncOutboxRepository outbox,
        IEventRepository? eventRepository = null,
        IAtprotoRecordRepository? recordRepository = null,
        string did = "did:plc:lifecycle-owner",
        IAtprotoPublicationPayloadBuilder? payloadBuilder = null)
    {
        var settings = Substitute.For<IHierarchicalSettingsResolver>();
        settings.ResolveBatchAsync(
                Arg.Any<IEnumerable<string>>(),
                Arg.Any<SettingContext>(),
                Arg.Any<CancellationToken>())
            .Returns(call => call.Arg<IEnumerable<string>>().Select(key => new ResolvedSetting
            {
                Key = key,
                Value = key == GovernanceSettingKeys.Federation.AtprotoEventValidationProfile
                    ? "\"platform\""
                    : "true",
                Source = SettingSource.UserPreference
            }).ToArray());
        var record = new AtprotoRecord
        {
            Id = Guid.CreateVersion7(),
            Did = did,
            Collection = AtprotoEventPublicationPlanner.EventCollection,
            RecordKey = "stable-lifecycle-key",
            Uri = $"at://{did}/community.lexicon.calendar.event/stable-lifecycle-key",
            Cid = "bafy-lifecycle",
            Direction = AtprotoRecordDirection.Outbound,
            Provenance = AtprotoRecordProvenance.LocalLifecycle,
            UpdatedAt = DateTime.UtcNow
        };
        var records = recordRepository ?? Substitute.For<IAtprotoRecordRepository>();
        records.GetOwnedRecordForSourceAsync(
                tenantId,
                AtprotoEventPublicationPlanner.EventSourceType,
                eventId,
                Arg.Any<CancellationToken>())
            .Returns(new AtprotoOutboundRecordOwnership
            {
                AtprotoRecordId = record.Id,
                TenantId = tenantId,
                UserId = ownerUserId,
                SourceEntityType = AtprotoEventPublicationPlanner.EventSourceType,
                SourceEntityId = eventId,
                SourceVersion = Guid.CreateVersion7(),
                AtprotoRecord = record
            });
        var sessions = Substitute.For<IAtprotoSessionBindingReader>();
        sessions.ReadAsync(
                tenantId,
                ownerUserId,
                Arg.Any<CancellationToken>())
            .Returns(ImmutableArray.Create(new AtprotoSessionBinding(did, "https://pds.example/")));
        var logins = Substitute.For<IUserExternalLoginRepository>();
        logins.GetByProviderAndKey(PlatformIdentityPrincipalExtensions.CreateAtprotoAccountKey(
            AtprotoDid.Parse(did)))
            .Returns(new UserExternalLogin
            {
                Id = Guid.CreateVersion7(),
                UserId = ownerUserId,
                User = null!,
                AuthenticationProviderId = (int)RepositoryBackedAtprotoSession.Provider.ParseAuthenticationProviderKind(),
                AuthenticationProvider = null!,
                ProviderKey = did
            });
        return new(
            new AtprotoEventGovernanceResolver(settings),
            eventRepository ?? Substitute.For<IEventRepository>(),
            records,
            sessions,
            logins,
            payloadBuilder ?? Substitute.For<IAtprotoPublicationPayloadBuilder>(),
            outbox,
            Microsoft.Extensions.Logging.Abstractions.NullLogger<AtprotoEventPublicationPlanner>.Instance);
    }
}
