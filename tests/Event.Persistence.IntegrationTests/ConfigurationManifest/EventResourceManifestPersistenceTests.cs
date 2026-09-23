using System.Text.Json;
using Explore.Application.Contracts.Persistence;
using Explore.Application.Features.ConfigurationManifest.Requests.Commands;
using Explore.Domain.Constants;
using Explore.Persistence.Repositories;
using Microsoft.EntityFrameworkCore;
using NSubstitute;

namespace Event.Persistence.IntegrationTests.ConfigurationManifest;

[ClassDataSource<EventResourcePersistenceTests.TestDatabase>(Shared = SharedType.PerClass)]
[NotInParallel("EventResourceManifest")]
public sealed class EventResourceManifestPersistenceTests(EventResourcePersistenceTests.TestDatabase database)
{
    [Test]
    [Arguments(5, true)]
    [Arguments(20, false)]
    public async Task BootstrapValidatesTenantChoicesAgainstTheProposedInstanceAndRollsBackAllScopes(int tenantCapacity, bool accepted)
    {
        string key = GovernanceSettingKeys.EventResources.MaxActiveResources;
        string slug = $"resources-{Guid.CreateVersion7():N}";
        var source = ConfigurationManifestApplicationTestSupport.Source(slug);
        var tenant = source.Manifest.Spec.Tenants.Single();
        var settings = tenant.Spec.Settings.ToDictionary(pair => pair.Key, pair => pair.Value, StringComparer.Ordinal);
        settings.Add(key, JsonSerializer.SerializeToElement(tenantCapacity));
        source = source with
        {
            Manifest = source.Manifest with
            {
                Spec = source.Manifest.Spec with
                {
                    Instance = source.Manifest.Spec.Instance with
                    {
                        Settings = new Dictionary<string, JsonElement> { [key] = JsonSerializer.SerializeToElement(10) }
                    },
                    Tenants = [tenant with { Spec = tenant.Spec with { Settings = settings } }]
                }
            }
        };
        await using (var context = database.CreateContext())
        {
            await context.SystemSettings.Where(row => row.SettingKey == key).ExecuteDeleteAsync();
            var handler = ConfigurationManifestApplicationTestSupport.CreateHandler(
                context,
                new ConfigurationManifestApplicationTestSupport.ExistencePreflight(new TenantRepository(context)),
                new ConfigurationManifestOperationRepository(context),
                Substitute.For<IConfigurationManifestFailureRecorder>(),
                useRealPolicyBoundary: true);
            var result = await handler.ExecuteAsync(new ApplyConfigurationManifestCommand(source), CancellationToken.None);
            await Assert.That(result.IsSuccess).IsEqualTo(accepted).Because(result.FailureCode ?? "accepted");
        }
        await using var verification = database.CreateContext();
        var persistedTenant = await verification.Tenants.AsNoTracking().SingleOrDefaultAsync(row => row.Slug == slug);
        var persistedInstance = await verification.SystemSettings.AsNoTracking().SingleOrDefaultAsync(row => row.SettingKey == key);
        if (!accepted)
        {
            await Assert.That(persistedTenant).IsNull();
            await Assert.That(persistedInstance).IsNull();
            return;
        }
        await Assert.That(persistedTenant).IsNotNull();
        await Assert.That(persistedInstance!.Value).IsEqualTo("10");
        var tenantRows = await verification.TenantSettingOverrides.AsNoTracking()
            .Where(row => row.TenantId == persistedTenant!.Id).ToArrayAsync();
        await Assert.That(tenantRows.Single(row => row.SettingKey == key).Value).IsEqualTo("5");
        await Assert.That(tenantRows.Any(row => row.SettingKey == GovernanceSettingKeys.PublicExperience.EventCatalogLabel)).IsTrue();
    }
}
