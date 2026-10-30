using System.Data;
using System.Data.Common;
using Event.Api.IntegrationTests.Fixtures;
using Explore.Application.Contracts.Infrastructure;
using Explore.Application.Contracts.Services;
using Explore.Application.DTOs.Instance;
using Explore.Application.Features.InstanceOnboarding.Requests.Commands;
using Explore.Application.Features.InstanceOnboarding.Services;
using Explore.Application.Models.Common;
using Explore.Domain.Constants;
using Microsoft.AspNetCore.TestHost;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Storage;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;

namespace Event.Api.IntegrationTests.Authentication;

public sealed partial class HumanSettingsMutationPreparationTests
{
    [Test]
    [Arguments("unchanged")]
    [Arguments("grant")]
    [Arguments("binding")]
    public async Task ProviderHumanCommit_RechecksAuthorityAfterOriginalWriterCompletion(string change)
    {
        var observer = new NativeProviderObserver();
        await using var fixture = await LocalIdentityLifecycleHttpFixture.CreateAsync(configureServices: services =>
        {
            RegisterNativeProvider(services, observer);
            var original = services.Last(item => item.ServiceType == typeof(IAuthorizationProviderModeCacheInvalidator));
            services.RemoveAll<IAuthorizationProviderModeCacheInvalidator>();
            services.AddScoped<IAuthorizationProviderModeCacheInvalidator>(provider => new ProviderAuthorizationActivationObserver(
                (IAuthorizationProviderModeCacheInvalidator)original.ImplementationFactory!(provider), observer));
        });
        await SeedPlatformAdministratorAsync(fixture);
        await using var host = fixture.Host.WithWebHostBuilder(builder => builder.ConfigureAppConfiguration((_, configuration) =>
            configuration.AddInMemoryCollection(new Dictionary<string, string?> { ["Authorization:Provider"] = "" })));
        await using var request = await ProviderRequestAsync(host.Services, await fixture.SignInAsync(), null,
            "/api/instance/settings/authz-provider");
        var service = request.Services.GetRequiredService<IAuthorizationProviderConfigurationService>();
        var before = await service.ReadConfigurationAsync();
        await using var observation = await service.ObserveConfigurationAsync(new UpdateAuthorizationProviderConfigurationCommand
        {
            UserId = fixture.Binding.LocalSubjectId,
            Patch = new()
            {
                Configuration = OptionalUpdate<AuthorizationProviderConfigurationWriteDto>.Set(new() { Provider = "local" })
            }
        }, default);
        string[] keys = [GovernanceSettingKeys.Security.AuthorizationProvider, GovernanceSettingKeys.Cerbos.GrpcEndpoint];
        await Assert.That(observation.Effects.Select(row => row.Key)).IsEquivalentTo(keys);
        Guid originalGrantId;
        string originalBindingKey;
        await using (var initial = fixture.Native.CreateDatabase())
        {
            originalGrantId = await initial.PlatformUserRoles.Where(row => row.UserId == fixture.Binding.LocalSubjectId)
                .Select(row => row.Id).SingleAsync();
            originalBindingKey = await initial.UserExternalLogins.Where(row => row.Id == fixture.Binding.ExternalLoginId)
                .Select(row => row.ProviderKey).SingleAsync();
        }
        string changedBindingKey = Guid.CreateVersion7().ToString("D");
        DbTransaction? completedWriterTransaction = null;
        int changedSources = 0;
        observer.AfterWrite = async (database, actual, token) =>
        {
            await Assert.That(actual).IsSameReferenceAs(observation);
            completedWriterTransaction = database.Database.CurrentTransaction?.GetDbTransaction()
                ?? throw new InvalidOperationException("The native provider writer lost its original transaction.");
            await Assert.That(completedWriterTransaction.IsolationLevel).IsEqualTo(IsolationLevel.Serializable);
            var leaves = await database.SystemSettings.AsNoTracking().Where(row => keys.Contains(row.SettingKey))
                .ToDictionaryAsync(row => row.SettingKey, token);
            await Assert.That(leaves.Count).IsEqualTo(2);
            await Assert.That(leaves[keys[0]].Value).IsEqualTo("\"local\"");
            await Assert.That(leaves[keys[1]].Value).IsEqualTo("\"\"");
            await Assert.That(leaves.Values.All(row => row.UpdatedBy == fixture.Binding.LocalSubjectId)).IsTrue();
            changedSources = change switch
            {
                "grant" => await database.PlatformUserRoles.Where(row => row.UserId == fixture.Binding.LocalSubjectId)
                    .ExecuteDeleteAsync(token),
                "binding" => await database.UserExternalLogins.Where(row => row.Id == fixture.Binding.ExternalLoginId)
                    .ExecuteUpdateAsync(set => set.SetProperty(row => row.ProviderKey, changedBindingKey), token),
                _ => 0
            };
            if (change != "unchanged") await Assert.That(changedSources).IsEqualTo(1);
            if (change == "binding")
                await Assert.That(await database.UserExternalLogins.Where(row => row.Id == fixture.Binding.ExternalLoginId)
                    .Select(row => row.ProviderKey).SingleAsync(token)).IsEqualTo(changedBindingKey);
            await Assert.That(database.Database.CurrentTransaction?.GetDbTransaction())
                .IsSameReferenceAs(completedWriterTransaction);
        };
        observer.ObserveCommitted = () =>
        {
            using var independent = fixture.Native.CreateDatabase();
            return observer.Context!.Database.CurrentTransaction is null
                && independent.SystemSettings.Count(row => keys.Contains(row.SettingKey)
                    && row.UpdatedBy == fixture.Binding.LocalSubjectId
                    && (row.SettingKey == keys[0] && row.Value == "\"local\""
                        || row.SettingKey == keys[1] && row.Value == "\"\"")) == 2;
        };

        if (change == "unchanged") await service.ApplyConfigurationAsync(observation, default);
        else await Assert.ThrowsAsync<UnauthorizedAccessException>(() => service.ApplyConfigurationAsync(observation, default));

        await Assert.That(observer.Applied).IsTrue();
        await Assert.That(completedWriterTransaction).IsNotNull();
        await Assert.That(observer.Context!.Database.CurrentTransaction).IsNull();
        await Assert.That(observer.ActivatedAfterCommit).IsEqualTo(change == "unchanged" ? true : (bool?)null);
        await using var final = fixture.Native.CreateDatabase();
        foreach (string key in keys)
        {
            var row = await final.SystemSettings.AsNoTracking().SingleOrDefaultAsync(row => row.SettingKey == key);
            if (change == "unchanged")
            {
                await Assert.That(row).IsNotNull();
                await Assert.That(row!.Value).IsEqualTo(key == keys[0] ? "\"local\"" : "\"\"");
                await Assert.That(row.UpdatedBy).IsEqualTo(fixture.Binding.LocalSubjectId);
            }
            else
                await Assert.That(row is null ? null : ProviderConfigurationRow.From(row)).IsEqualTo(observation.Before.System[key]);
        }
        await Assert.That(await final.PlatformUserRoles.Where(row => row.UserId == fixture.Binding.LocalSubjectId)
            .Select(row => row.Id).SingleAsync()).IsEqualTo(originalGrantId);
        await Assert.That(await final.UserExternalLogins.Where(row => row.Id == fixture.Binding.ExternalLoginId)
            .Select(row => row.ProviderKey).SingleAsync()).IsEqualTo(originalBindingKey);
        var after = await service.ReadConfigurationAsync();
        await Assert.That(after.Provider).IsEqualTo(change == "unchanged" ? "local" : before.Provider);
        await Assert.That(after.AuthorizationProviderConfigured).IsEqualTo(change == "unchanged" || before.AuthorizationProviderConfigured);
        if (change != "unchanged") await Assert.That(after.CerbosGrpcEndpoint).IsEqualTo(before.CerbosGrpcEndpoint);
    }

    private sealed class ProviderAuthorizationActivationObserver(
        IAuthorizationProviderModeCacheInvalidator inner, NativeProviderObserver observer)
        : IAuthorizationProviderModeCacheInvalidator
    {
        public void InvalidateInstanceMode()
        {
            if (observer.ObserveCommitted is { } read) observer.ActivatedAfterCommit = read();
            inner.InvalidateInstanceMode();
        }
    }
}
