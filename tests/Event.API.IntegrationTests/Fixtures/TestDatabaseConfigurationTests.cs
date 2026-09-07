using TUnit.Core;

namespace Event.Api.IntegrationTests.Fixtures;

public sealed class TestDatabaseConfigurationTests
{
    [Test]
    public async Task AddPostgreSql_ProjectsOnlyStructuredRuntimeSettings()
    {
        var configuration = new Dictionary<string, string?>();

        TestDatabaseConfiguration.AddPostgreSql(
            configuration,
            "Host=postgres.test;Port=5544;Database=event_test;Username=test_user;Password=test-password;SSL Mode=Require;Trust Server Certificate=true");

        await Assert.That(configuration["Database:Provider"]).IsEqualTo("PostgreSql");
        await Assert.That(configuration["Database:Host"]).IsEqualTo("postgres.test");
        await Assert.That(configuration["Database:Port"]).IsEqualTo("5544");
        await Assert.That(configuration["Database:Database"]).IsEqualTo("event_test");
        await Assert.That(configuration["Database:Runtime:Username"]).IsEqualTo("test_user");
        await Assert.That(configuration["Database:Runtime:Password"]).IsEqualTo("test-password");
        await Assert.That(configuration["Database:Runtime:TlsMode"]).IsEqualTo("Required");
        await Assert.That(configuration["Database:Runtime:TrustServerCertificate"]).IsEqualTo("True");
    }
}
