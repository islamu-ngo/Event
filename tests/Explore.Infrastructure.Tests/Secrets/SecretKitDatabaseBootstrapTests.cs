using Explore.Persistence;
using Explore.Persistence.Database;
using Explore.Persistence.Identity;
using Explore.Persistence.Privacy.ErasureAuthority;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;

namespace Explore.Infrastructure.Tests.Secrets;

public sealed class SecretKitDatabaseBootstrapTests
{
    [Test]
    [Arguments("DataProtection")]
    [Arguments("ExternalErasure")]
    [Arguments("CoLocatedErasure")]
    [Arguments("Application")]
    [Arguments("Identity")]
    public async Task ExplicitModelOnlyFactoryCannotAcquireCredentialAuthority(string family)
    {
        string[] arguments =
        [
            "--DesignTime:ModelOnly=true",
            "--DOTNET_ENVIRONMENT=Testing",
            "--Database:Provider=PostgreSql",
            "--IdentityDatabase:Provider=PostgreSql",
            "--IdentityDatabase:Schema=islamu_identity",
            "--SecretProvider:Provider=UnavailableForModelScaffolding"
        ];
        await using DbContext context = family switch
        {
            "DataProtection" => new DataProtectionKeyContextFactory().CreateDbContext(arguments),
            "ExternalErasure" => new PrivacyErasureAuthorityDbContextFactory().CreateDbContext(arguments),
            "Application" => new ExploreDbContextFactory().CreateDbContext(arguments),
            "Identity" => new ExternalIdentityDbContextFactory().CreateDbContext(arguments),
            _ => new CoLocatedPrivacyErasureAuthorityDbContextFactory().CreateDbContext(arguments)
        };

        await Assert.That(context.Database.GetConnectionString()).IsNull();
        await Assert.That(context.Database.IsNpgsql()).IsTrue();
        await Assert.That(context.Model.GetEntityTypes().Any()).IsTrue();
    }

    [Test]
    [Arguments(false)]
    [Arguments(true)]
    public async Task MigrationAcquisitionUsesOnlySelectedAuthorityAndPreservesDedicatedRoles(bool flatAliases)
    {
        var roles = new Dictionary<string, string>
        {
            ["Runtime"] = "RUNTIME",
            ["Migrator"] = "MIGRATOR",
            ["Global"] = "GLOBAL",
            ["InstanceKeyRead"] = "INSTANCE_KEY_READ",
            ["InstanceKeyWrite"] = "INSTANCE_KEY_WRITE",
            ["InstanceKeyIssuance"] = "INSTANCE_KEY_ISSUANCE",
            ["PlatformKeyReport"] = "PLATFORM_KEY_REPORT",
            ["EmailPolicyInstanceMutation"] = "EMAIL_POLICY_INSTANCE_MUTATION",
            ["EmailPolicyTenantMutation"] = "EMAIL_POLICY_TENANT_MUTATION",
            ["LocalIdentityWriter"] = "LOCAL_IDENTITY_WRITER"
        };
        var usernames = roles.ToDictionary(pair => pair.Key, _ => Guid.NewGuid().ToString("N"));
        var values = new Dictionary<string, string?>
        {
            ["Database:Provider"] = "Sqlite",
            ["Database:Name"] = Path.Combine(Path.GetTempPath(), Guid.NewGuid().ToString("N") + ".db")
        };
        foreach (var role in roles)
            values[flatAliases ? $"DATABASE_{role.Value}_USERNAME" : $"Database:{role.Key}:Username"]
                = usernames[role.Key];
        IConfiguration selected = new ConfigurationBuilder().AddInMemoryCollection(values).Build();
        IConfiguration bootstrap = new ConfigurationBuilder().AddInMemoryCollection(
            new Dictionary<string, string?>
            {
                ["SecretProvider:Provider"] = "Environment",
                ["Database:Provider"] = "PostgreSql",
                ["Database:Migrator:Password"] = Guid.NewGuid().ToString("N")
            }).Build();

        await using var acquisition = await MigrationDatabaseAcquisition.OpenAsync(
            bootstrap, "Testing", ["/database"], testingAuthority: selected);

        await Assert.That(acquisition.Configuration["Database:Provider"]).IsEqualTo("Sqlite");
        await Assert.That(acquisition.Configuration["Database:Migrator:Password"]).IsNull();
        foreach (string role in roles.Keys)
            await Assert.That(PrimaryDatabaseConfiguration.BindUsername(
                acquisition.Configuration, Enum.Parse<PrimaryDatabaseRole>(role)))
                .IsEqualTo(usernames[role]);
        IConfiguration overlaid = acquisition.ApplyTo(
            new ConfigurationBuilder().AddConfiguration(bootstrap)).Build();
        await Assert.That(overlaid["Database:Migrator:Password"]).IsNull();
    }

    [Test]
    public async Task MigrationAcquisitionDoesNotRequireServerCredentialsForSqlite()
    {
        IConfiguration bootstrap = new ConfigurationBuilder().AddInMemoryCollection(
            new Dictionary<string, string?> { ["SecretProvider:Provider"] = "Environment" }).Build();
        IConfiguration selected = new ConfigurationBuilder().AddInMemoryCollection(
            new Dictionary<string, string?>
            {
                ["DATABASE_PROVIDER"] = "Sqlite",
                ["DATABASE_NAME"] = Path.Combine(Path.GetTempPath(), Guid.NewGuid().ToString("N") + ".db")
            }).Build();
        await using var acquisition = await MigrationDatabaseAcquisition.OpenAsync(
            bootstrap, "Testing", ["/database"], testingAuthority: selected);
        PrimaryDatabaseConnectionOptions options = PrimaryDatabaseConfiguration.BindMigrator(acquisition.Configuration);

        await Assert.That(options.Role).IsEqualTo(PrimaryDatabaseRole.Migrator);
        await Assert.That(options.Provider).IsEqualTo(PrimaryDatabaseProvider.Sqlite);
        await Assert.That(options.Username).IsNull();
        await Assert.That(options.Password).IsNull();
    }

    [Test]
    public async Task MigrationAcquisitionCannotSubstituteRawConnectionsForStructuredAuthority()
    {
        IConfiguration bootstrap = new ConfigurationBuilder().AddInMemoryCollection(
            new Dictionary<string, string?> { ["SecretProvider:Provider"] = "Environment" }).Build();
        IConfiguration selected = new ConfigurationBuilder().AddInMemoryCollection(
            new Dictionary<string, string?>
            {
                ["ConnectionStrings:DefaultConnection"] = Guid.NewGuid().ToString("N")
            }).Build();
        await Assert.That(async () =>
        {
            await using var acquisition = await MigrationDatabaseAcquisition.OpenAsync(
                bootstrap, "Testing", ["/database"], testingAuthority: selected);
        }).Throws<InvalidOperationException>();
    }

    [Test]
    public async Task ExplicitEmptyRoleCredentialCannotActivateAnotherAlias()
    {
        IConfiguration bootstrap = new ConfigurationBuilder().AddInMemoryCollection(
            new Dictionary<string, string?> { ["SecretProvider:Provider"] = "Environment" }).Build();
        IConfiguration selected = new ConfigurationBuilder().AddInMemoryCollection(
            new Dictionary<string, string?>
            {
                ["Database:Migrator:Password"] = string.Empty,
                ["DATABASE_MIGRATOR_PASSWORD"] = Guid.NewGuid().ToString("N")
            }).Build();

        await Assert.That(async () =>
        {
            await using var acquisition = await MigrationDatabaseAcquisition.OpenAsync(
                bootstrap, "Testing", ["/database"], testingAuthority: selected);
        }).Throws<InvalidOperationException>();
    }

    [Test]
    public async Task MigrationAcquisitionCannotUseServingConsumerRoots()
    {
        IConfiguration bootstrap = new ConfigurationBuilder().AddInMemoryCollection(
            new Dictionary<string, string?> { ["SecretProvider:Provider"] = "Environment" }).Build();
        IConfiguration selected = new ConfigurationBuilder().Build();

        await Assert.That(async () =>
        {
            await using var acquisition = await MigrationDatabaseAcquisition.OpenAsync(
                bootstrap, "Testing", ["/runtime/bff/oidc/keycloak"], testingAuthority: selected);
        }).Throws<InvalidOperationException>();
    }

    [Test]
    public async Task MigrationAcquisitionRejectsProductionUserSecretsBeforeReading()
    {
        IConfiguration bootstrap = new ConfigurationBuilder().AddInMemoryCollection(
            new Dictionary<string, string?> { ["SecretProvider:Provider"] = "UserSecrets" }).Build();

        await Assert.That(async () =>
        {
            await using var acquisition = await MigrationDatabaseAcquisition.OpenAsync(
                bootstrap, "Production", ["/database"]);
        }).Throws<InvalidOperationException>();
    }
}
