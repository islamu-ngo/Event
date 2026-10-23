using Explore.Application.Contracts.Operations;
using Explore.Application.Features.Settings;
using Explore.Application.Features.Settings.Handlers.Commands;
using Explore.Application.Features.Settings.Requests.Commands;
using Explore.Application.Notifications;
using Event.Api.IntegrationTests.Fixtures;
using Explore.Domain.Settings;
using Explore.Domain.Settings.Definitions;
using Explore.Persistence;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;

namespace Event.Api.IntegrationTests.Authentication;

public sealed partial class HumanSettingsMutationPreparationTests
{
    [Test]
    [Arguments(SettingScope.Instance, "events.max_sessions_per_event", "events.card_click_opens_detail_page", false)]
    [Arguments(SettingScope.Instance, "events.require_approval", "events.user_submission_enabled", false)]
    [Arguments(SettingScope.User, "events.max_sessions_per_event", "events.require_approval", false)]
    [Arguments(SettingScope.Tenant, "events.max_sessions_per_event", "events.require_approval", true)]
    public async Task CombinedSourceSelectionRetainsPureAndUserScopes(
        SettingScope scope, string first, string second, bool combined)
    {
        var command = new UpdateSettingBatchCommand
        {
            Category = "Events",
            Scope = scope,
            Values = new Dictionary<string, string> { [first] = "1", [second] = "true" }
        };
        await Assert.That(MixedSettingsBatchMutation.RequiresCombinedSource(command)).IsEqualTo(combined);
    }

    [Test]
    [Arguments(false)]
    [Arguments(true)]
    public async Task NonEmailOrdinaryAndPublicationBatchRetainsOriginalCombinedOwner(bool corrupt)
    {
        var observation = new MixedObservation();
        string ordinaryKey = EventSettingDefinitions.MaxSessionsPerEvent.Key;
        string publicationKey = EventSettingDefinitions.RequireApproval.Key;
        await using var fixture = await LocalIdentityLifecycleHttpFixture.CreateAsync(
            configureServices: services => ConfigureMixedObservation(services, observation));
        await SeedPlatformAdministratorAsync(fixture);
        await using (var seed = fixture.Native.CreateDatabase())
        {
            await seed.SystemSettings.Where(row => row.SettingKey == ordinaryKey).ExecuteDeleteAsync();
            await SeedSpecializedSettingAsync(seed, publicationKey, "true");
            await SeedSpecializedSettingAsync(seed, EventReportingIntakeSettingDefinitions.IntakeEnabled.Key, "true");
        }
        var command = new UpdateSettingBatchCommand
        {
            Category = "Events",
            Scope = SettingScope.Instance,
            Values = new Dictionary<string, string>
            {
                [ordinaryKey] = "60",
                [publicationKey] = "false"
            }
        };
        await Assert.That(MixedSettingsBatchMutation.RequiresCombinedSource(command)).IsTrue();
        await using var request = await Request.CreateAsync(fixture);
        await using var selected = await request.Services.GetRequiredService<IPolicyMutationExecutionScopeFactory>()
            .CreateAsync(PolicyMutationScope.MixedSettingsInstance, CancellationToken.None);
        var owner = selected.Services.GetRequiredService<ExploreDbContext>();
        var sink = new MixedNotifications(owner, observation);
        observation.Command = command;
        bool completedOrdinary = false;
        observation.AfterEffects = async (_, services, token) =>
        {
            var context = services.GetRequiredService<ExploreDbContext>();
            await Assert.That(ReferenceEquals(owner, context)).IsTrue();
            completedOrdinary = await context.SystemSettings.AsNoTracking().AnyAsync(row =>
                row.SettingKey == ordinaryKey && row.Value == "60", token);
            if (!corrupt)
                return;
            await context.SystemSettings.Where(row => row.SettingKey == ordinaryKey)
                .ExecuteUpdateAsync(setters => setters.SetProperty(row => row.Value, "61"), token);
        };
        var handler = ActivatorUtilities.CreateInstance<UpdateSettingBatchCommandHandler>(
            selected.Services, (IEnumerable<INotificationHandler<SettingChangedNotification>>)[sink]);
        if (corrupt)
            await Assert.ThrowsAsync<UnauthorizedAccessException>(() => handler.ExecuteAsync(command, CancellationToken.None));
        else
        {
            var response = await handler.ExecuteAsync(command, CancellationToken.None);
            await Assert.That(response.Success).IsTrue();
            await Assert.That(response.Results.Count(item => item.Applied)).IsEqualTo(2);
            await Assert.That(sink.Published.Count).IsEqualTo(2);
        }
        await Assert.That(observation.Executions).IsEqualTo(1);
        await Assert.That(observation.EffectCallbacks).IsEqualTo(1);
        await Assert.That(completedOrdinary).IsTrue();
        await Assert.That(owner.Database.CurrentTransaction).IsNull();
        await using var independent = fixture.Native.CreateDatabase();
        var values = await independent.SystemSettings.AsNoTracking().Where(row =>
            row.SettingKey == ordinaryKey || row.SettingKey == publicationKey)
            .ToDictionaryAsync(row => row.SettingKey, row => row.Value);
        if (corrupt)
        {
            await Assert.That(values.ContainsKey(ordinaryKey)).IsFalse();
            await Assert.That(values[publicationKey]).IsEqualTo("true");
        }
        else
        {
            await Assert.That(values[ordinaryKey]).IsEqualTo("60");
            await Assert.That(values[publicationKey]).IsEqualTo("false");
        }
        if (corrupt)
            await Assert.That(sink.Published.Count).IsEqualTo(0);
    }
}
