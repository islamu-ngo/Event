using Event.Api.IntegrationTests.Fixtures;
using Explore.Application.Contracts.Operations;
using Explore.Application.Contracts.Persistence;
using Explore.Application.DTOs.Settings;
using Explore.Application.Features.Settings.Handlers.Commands;
using Explore.Application.Features.Settings.Requests.Commands;
using Explore.Application.Notifications;
using Explore.Application.Settings;
using Explore.Domain;
using Explore.Domain.Constants;
using Explore.Domain.Enums;
using Explore.Domain.Settings;
using Explore.Domain.Settings.Definitions;
using Explore.Persistence;
using Explore.Persistence.Repositories;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;

namespace Event.Api.IntegrationTests.Authentication;

public sealed partial class HumanSettingsMutationPreparationTests
{
    [Test]
    [Arguments("publication", false)]
    [Arguments("publication", true)]
    [Arguments("resource", false)]
    [Arguments("resource", true)]
    [Arguments("visitor", false)]
    [Arguments("visitor", true)]
    [Arguments("location", false)]
    [Arguments("location", true)]
    public async Task AcceptedSpecializedSlicesKeepTheirRealAlgorithmsOnTheOriginalMixedOwner(string family, bool expire)
    {
        var observation = new MixedObservation();
        await using var fixture = await LocalIdentityLifecycleHttpFixture.CreateAsync(
            configureServices: services => ConfigureMixedObservation(services, observation));
        await SeedPlatformAdministratorAsync(fixture);
        string key = family switch
        {
            "publication" => EventSettingDefinitions.RequireApproval.Key,
            "resource" => EventResourceSettingDefinitions.MaxUploadBytes.Key,
            "visitor" => GovernanceSettingKeys.PublicExperience.VisitorAccessMode,
            _ => GovernanceSettingKeys.LocationPrivacy.AllowPublicExactAddress
        };
        string initial = family switch
        {
            "resource" => "10485760",
            "visitor" => "\"AnonymousOnly\"",
            _ => "true"
        };
        string supplied = family switch
        {
            "resource" => "8388608",
            "visitor" => "FullRegistrationAndAuth",
            _ => "false"
        };
        string stored = family == "visitor" ? "\"FullRegistrationAndAuth\"" : supplied;
        Guid? locationId = null;
        int policyVersion = 0;
        await using (var seed = fixture.Native.CreateDatabase())
        {
            foreach (string publication in PublicationPolicySettingKeys.All)
                await SeedSpecializedSettingAsync(seed, publication, "true");
            foreach (var definition in EventResourceSettingDefinitions.All)
                await SeedSpecializedSettingAsync(seed, definition.Key, definition.DefaultValue);
            await SeedSpecializedSettingAsync(seed, key, initial);
            if (family == "location")
            {
                var actor = await seed.Actors.SingleAsync(row => row.UserId == fixture.Binding.LocalSubjectId);
                var source = new Explore.Domain.Event(EventStatusEnum.Draft)
                {
                    TenantId = PlatformDefaults.DefaultTenantId,
                    Tenant = null!,
                    ActorId = actor.Id,
                    Actor = null!,
                    Title = "Mixed original projection",
                    EventStatus = null!,
                    EventProvenanceTypeId = (int)EventProvenanceTypeEnum.OrganizerCreated,
                    VisibilityTypeId = (int)VisibilityTypeEnum.Public,
                    VisibilityType = null!,
                    EventFormatId = (int)EventFormatEnum.Local,
                    EventFormat = null!,
                    CreatedAt = fixture.Clock.GetUtcNow().UtcDateTime,
                    ConcurrencyStamp = Guid.CreateVersion7()
                };
                var location = new Location
                {
                    TenantId = PlatformDefaults.DefaultTenantId,
                    Tenant = null!,
                    FullName = "Mixed fixture venue",
                    Country = "BE",
                    City = "Brussels",
                    CreatedAt = fixture.Clock.GetUtcNow().UtcDateTime,
                    ConcurrencyStamp = Guid.CreateVersion7()
                };
                location.SetManualAddress("Mixed fixture street", "1000");
                seed.AddRange(source, location);
                await seed.SaveChangesAsync();
                var placement = EventLocation.CreatePhysical(PlatformDefaults.DefaultTenantId, source.Id, location.Id,
                    fixture.Binding.LocalSubjectId, fixture.Clock.GetUtcNow().UtcDateTime);
                await new EventLocationRepository(seed).AddAsync(placement, CancellationToken.None);
                seed.EventLocationDisclosureAudits.Add(placement.ChangeDisclosurePolicy(
                    EventLocationDisclosureFields.StreetAddress, LocationDisclosureAudienceEnum.AnyCurrentRegistrant,
                    null, placement.PolicyVersion, fixture.Binding.LocalSubjectId,
                    EventLocationDisclosureAuditReasonEnum.OrganizerPolicyChange, fixture.Clock.GetUtcNow().UtcDateTime,
                    needsPrivacyReview: false));
                locationId = placement.Id;
                policyVersion = placement.PolicyVersion;
            }
            await seed.SaveChangesAsync();
        }
        await using var request = await Request.CreateAsync(fixture);
        await using var selected = await request.Services.GetRequiredService<IPolicyMutationExecutionScopeFactory>()
            .CreateAsync(PolicyMutationScope.MixedSettingsInstance, CancellationToken.None);
        var owner = selected.Services.GetRequiredService<ExploreDbContext>();
        var sink = new MixedNotifications(owner, observation);
        observation.AfterEffects = async (mutation, services, token) =>
        {
            // The supplied Email member belongs to a different category and must stay
            // invalid; a cross-category positive would not exercise the real batch action.
            await Assert.That(mutation.Accepted.Count).IsEqualTo(1);
            await Assert.That(mutation.Accepted.ContainsKey(key)).IsTrue();
            await Assert.That(await owner.SystemSettings.Where(row => row.SettingKey == key)
                .Select(row => row.Value).SingleAsync(token)).IsEqualTo(stored);
            if (expire)
                services.GetRequiredService<IHumanActionCommitFinalizer>().Register(_ =>
                {
                    ExpireMixedCredential(fixture, services);
                    return Task.CompletedTask;
                }, _ => { });
        };
        var command = new UpdateSettingBatchCommand
        {
            Category = SettingRegistry.Get(key)!.Category,
            Scope = SettingScope.Instance,
            Mode = BatchUpdateMode.BestEffort,
            Values = new Dictionary<string, string> { [key] = supplied, [GovernanceSettingKeys.Email.SmtpPort] = "2526" }
        };
        observation.Command = command;
        var attempt = ActivatorUtilities.CreateInstance<UpdateSettingBatchCommandHandler>(selected.Services,
                (IEnumerable<INotificationHandler<SettingChangedNotification>>)[sink])
            .ExecuteAsync(command, CancellationToken.None);
        if (expire) await Assert.ThrowsAsync<UnauthorizedAccessException>(() => attempt);
        else
        {
            var response = await attempt;
            await Assert.That(response.Success).IsTrue();
            await Assert.That(response.Results.Single(result => result.Key == GovernanceSettingKeys.Email.SmtpPort).Applied).IsFalse();
        }
        await Assert.That(observation.EffectCallbacks).IsEqualTo(1);
        await Assert.That(owner.Database.CurrentTransaction).IsNull();
        await Assert.That(sink.Published.Count).IsEqualTo(expire ? 0 : 1);
        await using var observer = fixture.Native.CreateDatabase();
        await Assert.That(await observer.SystemSettings.Where(row => row.SettingKey == key)
            .Select(row => row.Value).SingleAsync()).IsEqualTo(expire ? initial : stored);
        if (locationId is { } id)
        {
            var location = await observer.EventLocations.AsNoTracking().SingleAsync(row => row.Id == id);
            await Assert.That(location.PolicyVersion).IsEqualTo(policyVersion + (expire ? 0 : 1));
            await Assert.That(location.NeedsPrivacyReview).IsEqualTo(!expire);
            await Assert.That(await observer.EventLocationDisclosureAudits.CountAsync(row =>
                row.EventLocationId == id && row.Reason == EventLocationDisclosureAuditReasonEnum.GovernanceTightening)).IsEqualTo(expire ? 0 : 1);
            await Assert.That(await observer.Set<LocationPrivacyCorrectionReceipt>().CountAsync(row => row.EventLocationId == id))
                .IsEqualTo(expire ? 0 : 1);
        }
    }
}
