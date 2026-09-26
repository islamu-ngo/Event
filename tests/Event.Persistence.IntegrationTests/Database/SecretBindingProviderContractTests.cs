using Event.Persistence.IntegrationTests.Fixtures;
using Explore.Domain.Enums;
using Explore.Domain.Secrets;
using Explore.Persistence;
using Explore.Persistence.Database;
using Explore.Persistence.Repositories;
using Explore.Secrets.Database;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.EntityFrameworkCore.Metadata;
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

    [Test]
    [Arguments("")]
    [Arguments("provider-a")]
    public async Task ProviderEnforcesUniquenessWithinEachScopeAndQualifier(string qualifier)
    {
        PrimaryDatabaseProviderBehaviorFixture fixture = PrimaryDatabaseProviderBehaviorFixture.Create();
        await fixture.PrepareAsync();
        await AssertScopeUniquenessAsync(fixture, qualifier);
    }

    internal static async Task AssertScopeUniquenessAsync(
        PrimaryDatabaseProviderBehaviorFixture fixture,
        string qualifier)
    {
        Guid firstTenantId = Guid.CreateVersion7();
        Guid secondTenantId = Guid.CreateVersion7();
        const string key = SecretDefinitionRegistry.Keys.Smtp.Password;
        const string otherKey = SecretDefinitionRegistry.Keys.Smtp.Username;
        SecretBinding[] bindings =
        [
            SecretBinding.CreateEnvironmentVariable(key, SecretScope.Instance, null, "INSTANCE_TOKEN", qualifier: qualifier),
            SecretBinding.CreateEnvironmentVariable(key, SecretScope.Tenant, firstTenantId, "FIRST_TENANT_TOKEN", qualifier: qualifier),
            SecretBinding.CreateEnvironmentVariable(key, SecretScope.Tenant, secondTenantId, "SECOND_TENANT_TOKEN", qualifier: qualifier),
            SecretBinding.CreateEnvironmentVariable(key, SecretScope.Instance, null, "OTHER_INSTANCE_TOKEN", qualifier: "provider-b"),
            SecretBinding.CreateEnvironmentVariable(key, SecretScope.Tenant, firstTenantId, "OTHER_TENANT_TOKEN", qualifier: "provider-b"),
            SecretBinding.CreateEnvironmentVariable(otherKey, SecretScope.Instance, null, "INSTANCE_USERNAME", qualifier: qualifier),
            SecretBinding.CreateEnvironmentVariable(otherKey, SecretScope.Tenant, firstTenantId, "TENANT_USERNAME", qualifier: qualifier)
        ];

        try
        {
            await using (ExploreDbContext seed = fixture.CreateSystemContext())
            {
                seed.SecretBindings.AddRange(bindings);
                await seed.SaveChangesAsync();
            }

            foreach (SecretBinding expected in bindings)
            {
                await using ExploreDbContext read = fixture.CreateSystemContext();
                var repository = new SecretBindingRepository(read);
                SecretBinding? actual = await repository.GetByKeyScopeAndQualifierAsync(
                    expected.SettingKey, expected.Scope, expected.ScopeId, expected.Qualifier);
                await Assert.That(actual).IsNotNull();
                await Assert.That(actual!.Id).IsEqualTo(expected.Id);
                await Assert.That(actual.EnvironmentVariableName).IsEqualTo(expected.EnvironmentVariableName);
                await Assert.That(await repository.GetByTenantAndIdAsync(Guid.CreateVersion7(), expected.Id)).IsNull();

                if (expected.Qualifier == qualifier && expected.SettingKey == key)
                {
                    SecretBinding? unqualified = await repository.GetByKeyAndScopeAsync(key, expected.Scope, expected.ScopeId);
                    if (qualifier.Length == 0)
                    {
                        await Assert.That(unqualified!.Id).IsEqualTo(expected.Id);
                    }
                    else
                    {
                        await Assert.That(unqualified).IsNull();
                    }
                }

                // Independent writes must not bypass database uniqueness, including normalized qualifiers.
                await using ExploreDbContext duplicate = fixture.CreateSystemContext();
                duplicate.SecretBindings.Add(SecretBinding.CreateEnvironmentVariable(
                    expected.SettingKey, expected.Scope, expected.ScopeId, "DUPLICATE_TOKEN",
                    qualifier: $" {expected.Qualifier} "));
                await Assert.That(async () => await duplicate.SaveChangesAsync()).Throws<DbUpdateException>();
            }
        }
        finally
        {
            Guid[] ids = bindings.Select(binding => binding.Id).ToArray();
            await using ExploreDbContext cleanup = fixture.CreateSystemContext();
            await cleanup.SecretBindings.Where(binding => ids.Contains(binding.Id)).ExecuteDeleteAsync();
        }
    }

    private static SecretBinding NewBinding(Guid tenantId, string variableName) =>
        SecretBinding.CreateEnvironmentVariable(
            SecretDefinitionRegistry.Keys.RegistrationProviders.ApiToken,
            SecretScope.Tenant,
            tenantId,
            variableName);
}

public sealed class SecretBindingModelContractTests
{
    [Test]
    [Arguments(PrimaryDatabaseProvider.PostgreSql)]
    [Arguments(PrimaryDatabaseProvider.Sqlite)]
    [Arguments(PrimaryDatabaseProvider.SqlServer)]
    [Arguments(PrimaryDatabaseProvider.MariaDb)]
    [Arguments(PrimaryDatabaseProvider.MySql)]
    public async Task UniquenessKeepsTenantAndQualifierDimensionsOnEveryProvider(PrimaryDatabaseProvider provider)
    {
        var options = TestDbContextOptions.Create<ExploreDbContext>();
        PrimaryDatabaseProviderComposition.ConfigureApplication(options, CreateOptions(provider));
        await using var context = new ExploreDbContext(options.Options);
        IEntityType binding = context.GetService<IDesignTimeModel>().Model.FindEntityType(typeof(SecretBinding))!;
        IIndex[] uniqueIndexes = binding.GetIndexes().Where(index => index.IsUnique).ToArray();
        await Assert.That(uniqueIndexes.Length).IsEqualTo(2);

        IIndex tenantIndex = uniqueIndexes.Single(index => index.Properties.Any(property => property.Name == nameof(SecretBinding.ScopeId)));
        await Assert.That(tenantIndex.Properties.Select(property => property.Name).ToArray())
            .IsEquivalentTo([nameof(SecretBinding.SettingKey), nameof(SecretBinding.ScopeId), nameof(SecretBinding.Qualifier)]);
        await Assert.That(tenantIndex.GetFilter()).IsEqualTo("scope_id IS NOT NULL");

        IIndex instanceIndex = uniqueIndexes.Single(index => index != tenantIndex);
        IProperty? slot = binding.FindProperty("InstanceSlot");
        if (provider is PrimaryDatabaseProvider.MariaDb or PrimaryDatabaseProvider.MySql)
        {
            await Assert.That(slot).IsNotNull();
            await Assert.That(slot!.ClrType).IsEqualTo(typeof(int?));
            await Assert.That(slot.IsNullable).IsTrue();
            await Assert.That(slot.ValueGenerated).IsEqualTo(ValueGenerated.OnAddOrUpdate);
            await Assert.That(slot.GetComputedColumnSql()).IsEqualTo("CASE WHEN setting_scope_id = 1 THEN 1 ELSE NULL END");
            await Assert.That(slot.GetIsStored()).IsTrue();
            await Assert.That(instanceIndex.GetFilter()).IsNull();
            await Assert.That(instanceIndex.Properties.Select(property => property.Name).ToArray())
                .IsEquivalentTo([nameof(SecretBinding.SettingKey), nameof(SecretBinding.Qualifier), "InstanceSlot"]);
        }
        else
        {
            await Assert.That(slot).IsNull();
            await Assert.That(instanceIndex.GetFilter()).IsEqualTo("scope_id IS NULL");
            await Assert.That(instanceIndex.Properties.Select(property => property.Name).ToArray())
                .IsEquivalentTo([nameof(SecretBinding.SettingKey), nameof(SecretBinding.Qualifier)]);
        }

        await Assert.That(binding.GetCheckConstraints().Single(check => check.Name == "ck_secret_bindings_setting_scope_scope_id").Sql)
            .IsEqualTo("(setting_scope_id = 1 AND scope_id IS NULL) OR (setting_scope_id = 2 AND scope_id IS NOT NULL)");
    }

    private static PrimaryDatabaseConnectionOptions CreateOptions(PrimaryDatabaseProvider provider) =>
        provider == PrimaryDatabaseProvider.Sqlite
            ? new PrimaryDatabaseConnectionOptions
            {
                Role = PrimaryDatabaseRole.Migrator,
                Provider = provider,
                Database = Path.Combine(Path.GetTempPath(), "secret-binding-model.db")
            }
            : new PrimaryDatabaseConnectionOptions
            {
                Role = PrimaryDatabaseRole.Migrator,
                Provider = provider,
                Host = "localhost",
                Database = "secret_binding_model",
                Username = "model",
                Password = Guid.CreateVersion7().ToString("N"),
                TlsMode = PrimaryDatabaseTlsMode.Disabled,
                ServerFlavor = provider switch
                {
                    PrimaryDatabaseProvider.MariaDb => PrimaryDatabaseServerFlavor.MariaDb,
                    PrimaryDatabaseProvider.MySql => PrimaryDatabaseServerFlavor.MySql,
                    _ => null
                },
                ServerVersion = provider switch
                {
                    PrimaryDatabaseProvider.MariaDb => new Version(11, 4),
                    PrimaryDatabaseProvider.MySql => new Version(8, 4),
                    _ => null
                }
            };
}
