using Event.Persistence.IntegrationTests.Fixtures;
using Explore.Domain.Enums;
using Explore.Domain.Secrets;
using Explore.Persistence;
using Explore.Persistence.Repositories;
using Microsoft.EntityFrameworkCore;
using TUnit.Core;

namespace Event.Persistence.IntegrationTests.Database;

[RequiresStructuredPrimaryDatabase]
[NotInParallel("PrimaryDatabaseProviderBehaviorContract")]
public sealed class SecretBindingProviderContractTests
{
    [Test]
    public async Task ProviderPersistsOnlyTenantQualifiedOpaqueMetadata()
    {
        PrimaryDatabaseProviderBehaviorFixture fixture = PrimaryDatabaseProviderBehaviorFixture.Create();
        await fixture.PrepareAsync();
        Guid firstTenantId = Guid.CreateVersion7();
        Guid secondTenantId = Guid.CreateVersion7();
        SecretBinding first = NewBinding(firstTenantId, "FIRST_TENANT_TOKEN");
        SecretBinding second = NewBinding(secondTenantId, "SECOND_TENANT_TOKEN");

        await using (ExploreDbContext seed = fixture.CreateSystemContext())
        {
            var model = seed.Model.FindEntityType(typeof(SecretBinding));
            await Assert.That(model).IsNotNull();
            await Assert.That(model!.FindProperty("InlineCiphertext")).IsNull();
            await Assert.That(model.FindProperty("InlineCiphertextVersion")).IsNull();
            seed.SecretBindings.AddRange(first, second);
            await seed.SaveChangesAsync();
        }

        Task<SecretBinding?>[] reads = Enumerable.Range(0, 64)
            .Select(async index =>
            {
                Guid tenantId = index % 2 == 0 ? firstTenantId : secondTenantId;
                await using ExploreDbContext context = fixture.CreateSystemContext();
                return await new SecretBindingRepository(context).GetByKeyAndScopeAsync(
                    SecretDefinitionRegistry.Keys.RegistrationProviders.ApiToken,
                    SecretScope.Tenant,
                    tenantId,
                    CancellationToken.None);
            })
            .ToArray();

        SecretBinding?[] results = await Task.WhenAll(reads);
        for (int index = 0; index < results.Length; index++)
        {
            Guid expectedTenant = index % 2 == 0 ? firstTenantId : secondTenantId;
            string expectedVariable = index % 2 == 0 ? "FIRST_TENANT_TOKEN" : "SECOND_TENANT_TOKEN";
            await Assert.That(results[index]).IsNotNull();
            await Assert.That(results[index]!.ScopeId).IsEqualTo(expectedTenant);
            await Assert.That(results[index]!.EnvironmentVariableName).IsEqualTo(expectedVariable);
        }
    }

    private static SecretBinding NewBinding(Guid tenantId, string variableName) =>
        SecretBinding.CreateEnvironmentVariable(
            SecretDefinitionRegistry.Keys.RegistrationProviders.ApiToken,
            SecretScope.Tenant,
            tenantId,
            variableName);
}
