using Explore.Application.Contracts.Infrastructure;
using Explore.Application.Contracts.Services;
using Explore.Application.Services.Federation;
using Explore.Application.Settings;
using Explore.Domain;
using Explore.Domain.Constants;
using Explore.Domain.Enums;
using Explore.Domain.Federation;
using Explore.Domain.ValueObjects;
using Explore.Persistence.Database.ProviderPrimitives;
using Explore.Secrets.Database;
using Microsoft.EntityFrameworkCore;
using NSubstitute;

namespace Event.Persistence.IntegrationTests.Repositories;

public sealed partial class PublicEventResponseClockNativeContractTests
{
    [Test]
    [Arguments(false)]
    [Arguments(true)]
    public async Task FederationClockIncludesImportedDraftsAndOwnedLocalEchoes(bool echo)
    {
        var seeded = await SeedAsync();
        Guid recordId = Guid.CreateVersion7();
        await using (var writer = fixture.CreateDbContext())
        {
            var local = await writer.Events.SingleAsync(value => value.Id == seeded.Event);
            string did = $"did:web:{seeded.Tenant:N}.example.org";
            writer.Add(new AtprotoIdentity(AtprotoDid.Parse(did))
            {
                Id = Guid.CreateVersion7(), ActorId = local.ActorId, IsActive = true,
                PdsHost = "https://pds.example.org", LastResolvedAt = Now.UtcDateTime
            });
            writer.Add(new AtprotoRecord
            {
                Id = recordId, Did = did, Collection = "community.lexicon.calendar.event",
                RecordKey = recordId.ToString("N"),
                Direction = echo ? AtprotoRecordDirection.Outbound : AtprotoRecordDirection.Inbound,
                Provenance = echo ? AtprotoRecordProvenance.LocalLifecycle : AtprotoRecordProvenance.Jetstream,
                SourceVersion = 1, UpdatedAt = Now.UtcDateTime, RecordJson = "{}"
            });
            writer.Add(new AtprotoEventProjection
            {
                AtprotoRecordId = recordId, Name = "Clock projection", CreatedAt = Now,
                StartsAt = null, EndsAt = Now.AddSeconds(5),
                SourceUrl = "https://events.example.org/event", SourceVersion = 1, MaterializedAt = Now.UtcDateTime
            });
            writer.Add(new AtprotoRecordTenantPresentation
            {
                TenantId = seeded.Tenant, AtprotoRecordId = recordId, IsVisible = true,
                SourceVersion = 1, EvaluatedAt = Now.UtcDateTime
            });
            if (echo)
            {
                local.AtprotoRecordId = recordId;
                writer.Add(new AtprotoOutboundRecordOwnership
                {
                    TenantId = seeded.Tenant, AtprotoRecordId = recordId, UserId = seeded.User,
                    SourceEntityType = nameof(Explore.Domain.Event), SourceEntityId = local.Id,
                    SourceVersion = Guid.CreateVersion7(), CreatedAt = Now.UtcDateTime, UpdatedAt = Now.UtcDateTime
                });
            }
            else
            {
                writer.Add(new Explore.Domain.Event(EventStatusEnum.Draft)
                {
                    Id = Guid.CreateVersion7(), TenantId = seeded.Tenant, Tenant = null!,
                    ActorId = local.ActorId, Actor = null!, Title = "Imported clock",
                    PublicCode = recordId.ToString("N")[^12..], AtprotoRecordId = recordId,
                    EventStatus = null!, VisibilityTypeId = (int)VisibilityTypeEnum.Public, VisibilityType = null!,
                    EventFormatId = (int)EventFormatEnum.Local, EventFormat = null!,
                    EventProvenanceTypeId = (int)EventProvenanceTypeEnum.OrganizerCreated
                });
            }
            await writer.SaveChangesAsync();
        }
        await using (var disabled = Open(PrimaryDatabaseRole.Runtime, seeded.Tenant))
            await Assert.That(await ReadAsync(disabled)).IsEqualTo(Now.AddSeconds(20));

        var settings = Substitute.For<IHierarchicalSettingsReader>();
        settings.ResolveBatchAsync(Arg.Any<IEnumerable<string>>(), Arg.Any<SettingContext>(), Arg.Any<CancellationToken>())
            .Returns(new[]
            {
                new ResolvedSetting
                {
                    Key = GovernanceSettingKeys.Federation.AtprotoEventsEnabled,
                    Value = "true", ValueType = SettingValueType.Boolean
                }
            });
        await using (var enabled = Open(PrimaryDatabaseRole.Runtime, seeded.Tenant))
            await Assert.That(await PublicEventResponseClockProviderOperations.GetNextAsync(enabled, Binding(enabled),
                Substitute.For<ILocationPrivacyGovernanceService>(), new AtprotoEventGovernanceResolver(settings), Now, default))
                .IsEqualTo(Now.AddSeconds(5));

        await using (var writer = fixture.CreateDbContext())
        {
            (await writer.AtprotoRecords.SingleAsync(value => value.Id == recordId)).TombstonedAt = Now.UtcDateTime;
            await writer.SaveChangesAsync();
        }
        await using var tombstoned = Open(PrimaryDatabaseRole.Runtime, seeded.Tenant);
        await Assert.That(await PublicEventResponseClockProviderOperations.GetNextAsync(tombstoned, Binding(tombstoned),
            Substitute.For<ILocationPrivacyGovernanceService>(), new AtprotoEventGovernanceResolver(settings), Now, default))
            .IsEqualTo(Now.AddSeconds(20));
    }
}
