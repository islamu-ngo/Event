using System.Net;
using System.Security.Cryptography;
using System.Text.Json;
using Event.Persistence.IntegrationTests.Fixtures;
using Explore.API.Authentication;
using Explore.Application.Contracts.Persistence;
using Explore.Domain;
using Explore.Domain.Constants;
using Explore.Domain.Enums;
using Explore.Persistence;
using Explore.Persistence.Database;
using Explore.Persistence.Security;
using Explore.Persistence.Services;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Hosting.Server;
using Microsoft.AspNetCore.Hosting.Server.Features;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.IdentityModel.Tokens;
using Npgsql;

namespace Event.Persistence.IntegrationTests.Health;

[ClassDataSource<PostgreSqlContainerFixture>(Shared = SharedType.PerAssembly)]
[NotInParallel("PersistenceDb")]
public sealed class JwtConfigurationReadOwnershipTests(PostgreSqlContainerFixture fixture)
{
    /// <summary>PR79: JWT metadata reads public instance settings without acquiring secret access.</summary>
    [Test]
    public async Task ReloadReadsStoredPublicKeycloakSettingsWithoutPromotingItsRuntimeCaller()
    {
        await fixture.ResetAsync();
        using var rsa = RSA.Create(2048);
        var publicKey = rsa.ExportParameters(false);
        string keyId = Guid.CreateVersion7().ToString("N");
        string realmPath = "/realms/" + Guid.CreateVersion7().ToString("N");
        string authority = string.Empty;
        var builder = WebApplication.CreateSlimBuilder(new WebApplicationOptions
        {
            EnvironmentName = Environments.Development,
            Args = []
        });
        builder.WebHost.UseKestrel(options => options.Listen(IPAddress.Loopback, 0));
        await using var host = builder.Build();
        host.MapGet(realmPath + "/.well-known/openid-configuration", () => new
        {
            issuer = authority,
            jwks_uri = authority + "/jwks",
            authorization_endpoint = authority + "/authorize",
            token_endpoint = authority + "/token",
            response_types_supported = new[] { "code" },
            subject_types_supported = new[] { "public" },
            id_token_signing_alg_values_supported = new[] { "RS256" }
        });
        host.MapGet(realmPath + "/jwks", () => new
        {
            keys = new[]
            {
                new
                {
                    kty = "RSA",
                    use = "sig",
                    kid = keyId,
                    alg = "RS256",
                    n = Base64UrlEncoder.Encode(publicKey.Modulus!),
                    e = Base64UrlEncoder.Encode(publicKey.Exponent!)
                }
            }
        });
        await host.StartAsync();
        authority = host.Services.GetRequiredService<IServer>().Features
            .Get<IServerAddressesFeature>()!.Addresses.Single().TrimEnd('/') + realmPath;

        string[] keys =
        [
            GovernanceSettingKeys.Authentication.PrimaryProviderId,
            GovernanceSettingKeys.Authentication.KeycloakAuthority,
            GovernanceSettingKeys.Authentication.KeycloakClientId
        ];
        await using var migration = fixture.CreateDbContext();
        migration.BindTenantLease();
        var original = await migration.SystemSettings.AsNoTracking()
            .Where(row => keys.Contains(row.SettingKey)).ToArrayAsync();
        try
        {
            await migration.SystemSettings.Where(row => keys.Contains(row.SettingKey)).ExecuteDeleteAsync();
            migration.SystemSettings.AddRange(
                Setting(keys[0], JsonSerializer.Serialize((int)AuthenticationProviderKind.Keycloak), SettingValueType.Integer),
                Setting(keys[1], JsonSerializer.Serialize(authority), SettingValueType.String),
                Setting(keys[2], JsonSerializer.Serialize("jwt-reader-" + keyId), SettingValueType.String));
            await migration.SaveChangesAsync();

            var options = TestDbContextOptions.Create<ExploreDbContext>();
            PrimaryDatabaseProviderComposition.ConfigureApplication(options,
                fixture.GetNativeQueueOptions(PrimaryDatabaseRole.Runtime));
            options.AddInterceptors(PostgresTenantSessionInterceptor.RuntimeInstance);
            await using var runtime = new ExploreDbContext(options.Options);
            runtime.BindTenantLease();
            await runtime.Database.OpenConnectionAsync();

            var services = new ServiceCollection();
            services.AddScoped<IInstanceSystemSettingsReader>(_ => new InstanceSystemSettingsReader(
                new GlobalExploreDbContextFactory(fixture.GetNativeQueueOptions(PrimaryDatabaseRole.Global)),
                fixture.NativeQueueEnrollment));
            await using var provider = services.BuildServiceProvider();
            using var service = new DynamicJwtConfigurationService(
                provider.GetRequiredService<IServiceScopeFactory>(),
                new ConfigurationBuilder().Build(),
                host.Environment,
                NullLogger<DynamicJwtConfigurationService>.Instance);

            await service.ReloadAsync(TestContext.Current!.Execution.CancellationToken);

            await Assert.That(service.Authority).IsEqualTo(authority);
            await Assert.That(service.ConfigurationManager).IsNotNull();
            var metadata = await service.ConfigurationManager!.GetConfigurationAsync(
                TestContext.Current.Execution.CancellationToken);
            await Assert.That(metadata.Issuer).IsEqualTo(authority);
            await Assert.That(metadata.SigningKeys.Any(key => key.KeyId == keyId)).IsTrue();
            await Assert.That(runtime.TenantFilterTenantId).IsNull();
            await Assert.That(runtime.ChangeTracker.Entries()).IsEmpty();
            var secretBinding = runtime.Model.GetEntityTypes()
                .Single(entity => entity.GetTableName() == "secret_bindings");
            string table = new HumanPrincipalSourceSql(runtime).Table(secretBinding.ClrType);
            var denial = await Assert.ThrowsAsync<PostgresException>(async () =>
                await runtime.Database.ExecuteSqlRawAsync($"SELECT * FROM {table} LIMIT 0"));
            await Assert.That(denial.SqlState).IsEqualTo(PostgresErrorCodes.InsufficientPrivilege);
        }
        finally
        {
            migration.ChangeTracker.Clear();
            await migration.SystemSettings.Where(row => keys.Contains(row.SettingKey)).ExecuteDeleteAsync();
            migration.SystemSettings.AddRange(original);
            await migration.SaveChangesAsync();
        }
    }

    private static SystemSetting Setting(string key, string value, SettingValueType type) => new()
    {
        Id = Guid.CreateVersion7(),
        SettingKey = key,
        Value = value,
        ValueType = type,
        CreatedAt = DateTime.UtcNow
    };
}
