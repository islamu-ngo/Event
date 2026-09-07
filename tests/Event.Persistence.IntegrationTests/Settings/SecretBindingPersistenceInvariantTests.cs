namespace Event.Persistence.IntegrationTests.Settings;

using Event.Persistence.IntegrationTests.Fixtures;
using Explore.Domain.Enums;
using Explore.Domain.Secrets;
using Explore.Persistence;
using Explore.Persistence.Repositories;
using Microsoft.EntityFrameworkCore;
using TUnit.Core;

[ClassDataSource<PostgreSqlContainerFixture>(Shared = SharedType.PerAssembly)]
[NotInParallel("PersistenceDb")]
public sealed class SecretBindingPersistenceInvariantTests(PostgreSqlContainerFixture fixture)
{
    [Test]
    public async Task PersistenceModelContainsOnlyOpaqueSecretMetadata()
    {
        await using ExploreDbContext context = fixture.CreateDbContext();
        var binding = context.Model.FindEntityType(typeof(SecretBinding));

        await Assert.That(binding).IsNotNull();
        await Assert.That(binding!.FindProperty("InlineCiphertext")).IsNull();
        await Assert.That(binding.FindProperty("InlineCiphertextVersion")).IsNull();
    }

    [Test]
    public async Task ConcurrentTenantReadsReturnOnlyRequestedTenantMetadata()
    {
        await fixture.ResetAsync();
        Guid firstTenantId = Guid.CreateVersion7();
        Guid secondTenantId = Guid.CreateVersion7();
        SecretBinding first = NewBinding(firstTenantId, "FIRST_TENANT_TOKEN");
        SecretBinding second = NewBinding(secondTenantId, "SECOND_TENANT_TOKEN");

        await using (ExploreDbContext seed = fixture.CreateDbContext())
        {
            seed.SecretBindings.AddRange(first, second);
            await seed.SaveChangesAsync();
        }

        Task<SecretBinding?>[] reads = Enumerable.Range(0, 64)
            .Select(async index =>
            {
                Guid tenantId = index % 2 == 0 ? firstTenantId : secondTenantId;
                await using ExploreDbContext context = fixture.CreateDbContext();
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
