using System.Collections.Immutable;
using Event.Api.IntegrationTests.Fixtures;
using Explore.Application.Contracts.Operations;
using Explore.Application.Contracts.Persistence;
using Explore.Application.Features.Settings.Requests.Commands;
using Explore.Domain;
using Explore.Domain.Constants;
using Explore.Domain.Settings;
using Explore.Persistence;
using Explore.Persistence.Security;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;

namespace Event.Api.IntegrationTests.Authentication;

public sealed partial class HumanSettingsMutationPreparationTests
{
    [Test]
    [Arguments(false)]
    [Arguments(true)]
    public async Task OrdinarySystemValueDoesNotAuthorizeCallerDefinitionOrCreationMetadata(bool insert)
    {
        await using var fixture = await CreateOrdinaryCustodyFixtureAsync();
        await SeedPlatformAdministratorAsync(fixture);
        const string key = "event_list.page_size";
        Guid originalId = Guid.CreateVersion7();
        var originalCreated = new DateTime(2026, 1, 2, 3, 4, 5, DateTimeKind.Utc);
        await using (var seed = fixture.Native.CreateDatabase())
        {
            await seed.SystemSettings.Where(row => row.SettingKey == key).ExecuteDeleteAsync();
            if (!insert)
            {
                seed.SystemSettings.Add(new SystemSetting
                {
                    Id = originalId,
                    SettingKey = key,
                    Value = "24",
                    ValueType = SettingValueType.Integer,
                    Description = "Original description",
                    Category = "Original category",
                    AllowedValues = "[12,24]",
                    DisplayOrder = 7,
                    CreatedAt = originalCreated,
                    CreatedBy = fixture.Binding.LocalSubjectId
                });
                await seed.SaveChangesAsync();
            }
        }
        await using var request = await Request.CreateAsync(fixture);
        await using var selected = await request.Services.GetRequiredService<IPolicyMutationExecutionScopeFactory>()
            .CreateAsync(PolicyMutationScope.Instance, CancellationToken.None);
        var services = selected.Services;
        var preparation = services.GetRequiredService<IHumanSettingsMutationPreparation>();
        var proof = await preparation.PrepareAsync(
            new UpdateSettingCommand { Key = key, Value = "12", Scope = SettingScope.Instance }, "12");
        var forgedId = Guid.CreateVersion7();
        var forgedCreator = Guid.CreateVersion7();
        await preparation.ExecutePreparedAsync(proof, async (_, token) =>
        {
            await services.GetRequiredService<ISystemSettingRepository>().UpsertAsync(new SystemSetting
            {
                Id = forgedId,
                SettingKey = key,
                Value = "12",
                ValueType = SettingValueType.Json,
                Description = "Caller replacement",
                Category = "Caller category",
                AllowedValues = "[]",
                DisplayOrder = 999,
                CreatedAt = originalCreated.AddYears(-10),
                CreatedBy = forgedCreator,
                UpdatedBy = fixture.Binding.LocalSubjectId,
                UpdatedAt = originalCreated.AddYears(-5)
            }, token);
            return true;
        });
        await using var observer = fixture.Native.CreateDatabase();
        var actual = await observer.SystemSettings.AsNoTracking().SingleAsync(row => row.SettingKey == key);
        await Assert.That(actual.Value).IsEqualTo("12");
        await Assert.That(actual.ValueType).IsEqualTo(SettingValueType.Integer);
        await Assert.That(actual.Id == forgedId).IsFalse();
        await Assert.That(actual.CreatedBy).IsEqualTo(fixture.Binding.LocalSubjectId);
        if (insert)
        {
            await Assert.That(actual.Description).IsEqualTo(SettingRegistry.Get(key)!.Description);
            await Assert.That(actual.Category).IsEqualTo(SettingRegistry.Get(key)!.Category);
            await Assert.That(actual.AllowedValues).IsNull();
            await Assert.That(actual.DisplayOrder).IsEqualTo(0);
            await Assert.That(actual.CreatedAt).IsEqualTo(fixture.Clock.GetUtcNow().UtcDateTime);
        }
        else
        {
            await Assert.That(actual.Id).IsEqualTo(originalId);
            await Assert.That(actual.CreatedAt).IsEqualTo(originalCreated);
            await Assert.That(actual.Description).IsEqualTo("Original description");
            await Assert.That(actual.Category).IsEqualTo("Original category");
            await Assert.That(actual.AllowedValues).IsEqualTo("[12,24]");
            await Assert.That(actual.DisplayOrder).IsEqualTo(7);
            await Assert.That(actual.UpdatedBy).IsEqualTo(fixture.Binding.LocalSubjectId);
        }
    }

    [Test]
    [Arguments("value")]
    [Arguments("metadata")]
    [Arguments("absence")]
    [Arguments("ceiling-absence")]
    public async Task OrdinaryOriginalRowsAndNegativeSourcesCannotBeRebasedBeforeBegin(string changed)
    {
        await using var fixture = await CreateOrdinaryCustodyFixtureAsync();
        await SeedPlatformAdministratorAsync(fixture);
        if (changed == "ceiling-absence") await SeedMixedTenantAdministratorAsync(fixture);
        string key = changed == "ceiling-absence"
            ? GovernanceSettingKeys.Organizations.VerificationRequired : "event_list.page_size";
        await using (var seed = fixture.Native.CreateDatabase())
        {
            await seed.SystemSettings.Where(row => row.SettingKey == key
                || row.SettingKey == GovernanceSettingKeys.Organizations.TenantCanOmitVerification).ExecuteDeleteAsync();
            if (changed != "absence")
            {
                seed.SystemSettings.Add(new SystemSetting
                {
                    SettingKey = key,
                    Value = changed == "ceiling-absence" ? "true" : "24",
                    ValueType = changed == "ceiling-absence" ? SettingValueType.Boolean : SettingValueType.Integer
                });
                await seed.SaveChangesAsync();
            }
        }
        await using var request = await Request.CreateAsync(fixture);
        await using var selected = await request.Services.GetRequiredService<IPolicyMutationExecutionScopeFactory>()
            .CreateAsync(changed == "ceiling-absence"
                ? new(PlatformDefaults.DefaultTenantId) : PolicyMutationScope.Instance, CancellationToken.None);
        var services = selected.Services;
        var preparation = services.GetRequiredService<IHumanSettingsMutationPreparation>();
        var proof = changed == "ceiling-absence"
            ? await preparation.PrepareAsync(new UpdateSettingCommand
            { Key = key, Scope = SettingScope.Tenant, Value = "false" }, "false")
            : await preparation.PrepareAsync(
                new UpdateSettingCommand { Key = key, Value = "12", Scope = SettingScope.Instance }, "12");
        await using (var independent = fixture.Native.CreateDatabase())
        {
            if (changed is "absence" or "ceiling-absence")
            {
                independent.SystemSettings.Add(new SystemSetting
                {
                    SettingKey = changed == "absence" ? key
                        : GovernanceSettingKeys.Organizations.TenantCanOmitVerification,
                    Value = changed == "absence" ? "36" : "true",
                    ValueType = SettingValueType.Integer
                });
                await independent.SaveChangesAsync();
            }
            else if (changed == "value")
                await independent.SystemSettings.Where(row => row.SettingKey == key)
                    .ExecuteUpdateAsync(setters => setters.SetProperty(row => row.Value, "36"));
            else
                await independent.SystemSettings.Where(row => row.SettingKey == key)
                    .ExecuteUpdateAsync(setters => setters.SetProperty(row => row.Description, "Changed source"));
        }
        int callbacks = 0;
        await Assert.ThrowsAsync<UnauthorizedAccessException>(() => preparation.ExecutePreparedAsync(proof, async (_, token) =>
        {
            callbacks++;
            if (changed == "ceiling-absence")
            {
                await services.GetRequiredService<ITenantSettingRepository>().SetValueAsync(
                    PlatformDefaults.DefaultTenantId, key, "false", token, fixture.Binding.LocalSubjectId);
                return true;
            }
            var row = (await services.GetRequiredService<ISystemSettingRepository>().GetByKey(key, token))!;
            row.Value = "12";
            row.UpdatedBy = fixture.Binding.LocalSubjectId;
            await services.GetRequiredService<ISystemSettingRepository>().UpsertAsync(row, token);
            return true;
        }));
        await Assert.That(callbacks).IsEqualTo(0);
        await using var observer = fixture.Native.CreateDatabase();
        await Assert.That(await observer.SystemSettings.AnyAsync(row => row.SettingKey == key && row.Value == "12")).IsFalse();
    }

    [Test]
    [Arguments(false)]
    [Arguments(true)]
    public async Task OrdinaryPrivateWriterCompletionCannotBeReplacedByEqualScalarSql(bool afterWriter)
    {
        await using var fixture = await CreateOrdinaryCustodyFixtureAsync();
        await SeedPlatformAdministratorAsync(fixture);
        const string key = "event_list.page_size";
        await using (var seed = fixture.Native.CreateDatabase())
        {
            await seed.SystemSettings.Where(row => row.SettingKey == key).ExecuteDeleteAsync();
            seed.SystemSettings.Add(new SystemSetting
            {
                SettingKey = key,
                Value = "24",
                ValueType = SettingValueType.Integer,
                Description = "Preserved"
            });
            await seed.SaveChangesAsync();
        }
        await using var request = await Request.CreateAsync(fixture);
        await using var selected = await request.Services.GetRequiredService<IPolicyMutationExecutionScopeFactory>()
            .CreateAsync(PolicyMutationScope.Instance, CancellationToken.None);
        var services = selected.Services;
        var preparation = services.GetRequiredService<IHumanSettingsMutationPreparation>();
        var proof = await preparation.PrepareAsync(
            new UpdateSettingCommand { Key = key, Value = "12", Scope = SettingScope.Instance }, "12");
        int callbacks = 0;
        await Assert.ThrowsAsync<UnauthorizedAccessException>(() => preparation.ExecutePreparedAsync(proof, async (_, token) =>
        {
            callbacks++;
            if (afterWriter)
            {
                var repository = services.GetRequiredService<ISystemSettingRepository>();
                var row = (await repository.GetByKey(key, token))!;
                row.Value = "12";
                row.UpdatedBy = fixture.Binding.LocalSubjectId;
                await repository.UpsertAsync(row, token);
            }
            var context = services.GetRequiredService<ExploreDbContext>();
            if (afterWriter)
                await context.SystemSettings.Where(row => row.SettingKey == key)
                    .ExecuteUpdateAsync(setters => setters.SetProperty(row => row.Description, "Substituted"), token);
            else
                await context.SystemSettings.Where(row => row.SettingKey == key)
                    .ExecuteUpdateAsync(setters => setters.SetProperty(row => row.Value, "12")
                        .SetProperty(row => row.UpdatedBy, fixture.Binding.LocalSubjectId), token);
            return true;
        }));
        await Assert.That(callbacks).IsEqualTo(1);
        await using var observer = fixture.Native.CreateDatabase();
        var actual = await observer.SystemSettings.AsNoTracking().SingleAsync(row => row.SettingKey == key);
        await Assert.That(actual.Value).IsEqualTo("24");
        await Assert.That(actual.Description).IsEqualTo("Preserved");
    }

    [Test]
    [Arguments(false)]
    [Arguments(true)]
    public async Task OrdinaryUserPreferencePreservesOriginalIdentityAndCreationAudit(bool insert)
    {
        await using var fixture = await CreateOrdinaryCustodyFixtureAsync();
        const string key = "event_list.page_size";
        var originalId = Guid.CreateVersion7();
        var originalCreated = new DateTime(2026, 1, 2, 3, 4, 5, DateTimeKind.Utc);
        if (!insert)
        {
            await using var seed = fixture.Native.CreateDatabase();
            seed.UserPreferences.Add(new UserPreference
            {
                Id = originalId,
                TenantId = PlatformDefaults.DefaultTenantId,
                Tenant = null!,
                UserId = fixture.Binding.LocalSubjectId,
                SettingKey = key,
                Value = "24",
                CreatedAt = originalCreated,
                CreatedBy = fixture.Binding.LocalSubjectId
            });
            await seed.SaveChangesAsync();
        }
        await using var request = await Request.CreateAsync(fixture);
        var services = request.Services;
        var preparation = services.GetRequiredService<IHumanSettingsMutationPreparation>();
        var proof = await preparation.PrepareAsync(
            new UpdateSettingCommand { Key = key, Value = "12", Scope = SettingScope.User }, "12");
        var forgedId = Guid.CreateVersion7();
        await preparation.ExecutePreparedAsync(proof, async (_, _) =>
        {
            var repository = services.GetRequiredService<IUserPreferenceRepository>();
            var candidate = new UserPreference
            {
                Id = insert ? forgedId : originalId,
                TenantId = PlatformDefaults.DefaultTenantId,
                Tenant = null!,
                UserId = fixture.Binding.LocalSubjectId,
                SettingKey = key,
                Value = "12",
                CreatedAt = originalCreated.AddYears(-10),
                CreatedBy = Guid.CreateVersion7(),
                UpdatedAt = originalCreated.AddYears(-5),
                UpdatedBy = fixture.Binding.LocalSubjectId
            };
            if (insert) await repository.Create(candidate);
            else await repository.Update(candidate);
            return true;
        });
        await using var observer = fixture.Native.CreateDatabase();
        var actual = await observer.UserPreferences.AsNoTracking().SingleAsync(row =>
            row.TenantId == PlatformDefaults.DefaultTenantId && row.UserId == fixture.Binding.LocalSubjectId
            && row.SettingKey == key);
        await Assert.That(actual.Value).IsEqualTo("12");
        await Assert.That(actual.Id == forgedId).IsFalse();
        await Assert.That(actual.CreatedBy).IsEqualTo(fixture.Binding.LocalSubjectId);
        await Assert.That(actual.CreatedAt).IsEqualTo(insert ? fixture.Clock.GetUtcNow().UtcDateTime : originalCreated);
        if (!insert) await Assert.That(actual.Id).IsEqualTo(originalId);
    }

    private static Task<LocalIdentityLifecycleHttpFixture> CreateOrdinaryCustodyFixtureAsync() =>
        LocalIdentityLifecycleHttpFixture.CreateAsync(configureServices: services =>
            services.AddScoped<IOrdinarySettingsMutationStore, OrdinarySettingsMutationStore>());

    [Test]
    [Arguments(false)]
    [Arguments(true)]
    public async Task RejectedOrdinaryWriterEntryCannotBeCaughtToCommitEarlierEffects(bool dirtyTracker)
    {
        await using var fixture = await CreateOrdinaryCustodyFixtureAsync();
        await using var request = await Request.CreateAsync(fixture);
        var services = request.Services;
        var preparation = services.GetRequiredService<IHumanSettingsMutationPreparation>();
        var command = new UpdateSettingBatchCommand
        {
            Category = "EventList",
            Scope = SettingScope.User,
            Values = new Dictionary<string, string>
            {
                ["event_list.page_size"] = "12",
                ["event_list.browse_mode"] = "infinite-scroll"
            }
        };
        var proof = await preparation.PrepareAsync(command,
            ImmutableDictionary<string, string>.Empty.Add("event_list.page_size", "12")
                .Add("event_list.browse_mode", "\"infinite-scroll\""));
        string sibling = $"unadmitted-{Guid.CreateVersion7():N}";
        await Assert.ThrowsAsync<UnauthorizedAccessException>(() =>
            preparation.ExecutePreparedAsync(proof, async (_, _) =>
            {
                var repository = services.GetRequiredService<IUserPreferenceRepository>();
                await repository.Create(new UserPreference
                {
                    TenantId = PlatformDefaults.DefaultTenantId,
                    Tenant = null!,
                    UserId = fixture.Binding.LocalSubjectId,
                    SettingKey = "event_list.page_size",
                    Value = "12",
                    CreatedBy = fixture.Binding.LocalSubjectId
                });
                var context = services.GetRequiredService<ExploreDbContext>();
                if (dirtyTracker)
                    context.SystemSettings.Add(new SystemSetting { SettingKey = sibling, Value = "unadmitted" });
                await Assert.ThrowsAsync<UnauthorizedAccessException>(() => repository.Create(new UserPreference
                {
                    TenantId = PlatformDefaults.DefaultTenantId,
                    Tenant = null!,
                    UserId = fixture.Binding.LocalSubjectId,
                    SettingKey = dirtyTracker ? "event_list.browse_mode" : "event_list.page_size",
                    Value = dirtyTracker ? "\"infinite-scroll\"" : "12",
                    CreatedBy = fixture.Binding.LocalSubjectId
                }));
                // Removing the unflushed caller row cannot revive the rejected private writer.
                context.ChangeTracker.Clear();
                return true;
            }));
        await using var observer = fixture.Native.CreateDatabase();
        await Assert.That(await observer.UserPreferences.AnyAsync(row =>
            row.TenantId == PlatformDefaults.DefaultTenantId && row.UserId == fixture.Binding.LocalSubjectId
            && (row.SettingKey == "event_list.page_size" || row.SettingKey == "event_list.browse_mode"))).IsFalse();
        await Assert.That(await observer.SystemSettings.AnyAsync(row => row.SettingKey == sibling)).IsFalse();
    }

    [Test]
    [Arguments(false)]
    [Arguments(true)]
    public async Task OrdinaryStoredValueMustComeFromTheOriginalValidatedCommand(bool batch)
    {
        await using var fixture = await CreateOrdinaryCustodyFixtureAsync();
        await using var request = await Request.CreateAsync(fixture);
        var preparation = request.Services.GetRequiredService<IHumanSettingsMutationPreparation>();
        if (batch)
            await Assert.ThrowsAsync<UnauthorizedAccessException>(() => preparation.PrepareAsync(new UpdateSettingBatchCommand
            {
                Category = "EventList",
                Scope = SettingScope.User,
                Values = new Dictionary<string, string> { ["event_list.page_size"] = "12" }
            }, ImmutableDictionary<string, string>.Empty.Add("event_list.page_size", "13")));
        else
            await Assert.ThrowsAsync<UnauthorizedAccessException>(() => preparation.PrepareAsync(new UpdateSettingCommand
            { Key = "event_list.page_size", Scope = SettingScope.User, Value = "12" }, "13"));
        await Assert.That(preparation.CurrentOrdinarySource).IsNull();
        await using var observer = fixture.Native.CreateDatabase();
        await Assert.That(await observer.UserPreferences.AnyAsync(row => row.TenantId == PlatformDefaults.DefaultTenantId
            && row.UserId == fixture.Binding.LocalSubjectId && row.SettingKey == "event_list.page_size")).IsFalse();
    }

    [Test]
    public async Task OrdinaryPreparationCannotRelabelAnOriginalMixedBatch()
    {
        await using var fixture = await CreateOrdinaryCustodyFixtureAsync();
        await SeedPlatformAdministratorAsync(fixture);
        await using var request = await Request.CreateAsync(fixture);
        await using var selected = await request.Services.GetRequiredService<IPolicyMutationExecutionScopeFactory>()
            .CreateAsync(PolicyMutationScope.Instance, CancellationToken.None);
        var preparation = selected.Services.GetRequiredService<IHumanSettingsMutationPreparation>();
        await Assert.ThrowsAsync<UnauthorizedAccessException>(() => preparation.PrepareAsync(new UpdateSettingBatchCommand
        {
            Category = "EventList",
            Scope = SettingScope.Instance,
            Values = new Dictionary<string, string>
            {
                ["event_list.page_size"] = "12",
                [GovernanceSettingKeys.TenantDelegation.LockSmtp] = "false"
            }
        }, ImmutableDictionary<string, string>.Empty.Add("event_list.page_size", "12")));
        await Assert.That(preparation.CurrentOrdinarySource).IsNull();
    }
}
