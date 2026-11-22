using Event.Testing;

namespace Explore.Diagnostic.UnitTests.Execution;

public class ContainerResourceBoundsTests
{
    private const long BytesPerMegabyte = 1024L * 1024L;

    [Test]
    [Arguments(ContainerServiceRole.Redis, 128)]
    [Arguments(ContainerServiceRole.Mailpit, 128)]
    [Arguments(ContainerServiceRole.Cerbos, 256)]
    [Arguments(ContainerServiceRole.Svix, 256)]
    [Arguments(ContainerServiceRole.RabbitMq, 512)]
    [Arguments(ContainerServiceRole.Generic, 512)]
    [Arguments(ContainerServiceRole.MySql, 768)]
    [Arguments(ContainerServiceRole.MariaDb, 768)]
    [Arguments(ContainerServiceRole.PostgreSql, 1024)]
    [Arguments(ContainerServiceRole.PostGis, 1024)]
    [Arguments(ContainerServiceRole.Keycloak, 1024)]
    [Arguments(ContainerServiceRole.SqlServer, 2048)]
    public async Task GetDefaultMemoryMegabytes_ByRole_ReturnsBoundedCeiling(
        ContainerServiceRole role,
        int expectedMegabytes)
    {
        ContainerResourcePolicy policy = ContainerResourceBounds.Resolve(
            role,
            hasAssuranceOwner: false,
            hasExplicitContainerName: false,
            getEnv: _ => null);

        await Assert.That(policy.Role).IsEqualTo(role);
        await Assert.That(policy.MemoryLimitBytes).IsEqualTo(expectedMegabytes * BytesPerMegabyte);
        await Assert.That(policy.EnableReuse).IsFalse();
    }

    [Test]
    public async Task Resolve_WithContainerMemoryOverride_OverridesRoleDefault()
    {
        Func<string, string?> env = name => name == ContainerResourceBounds.ContainerMemoryLimitEnvVar
            ? "1536"
            : null;

        ContainerResourcePolicy policy = ContainerResourceBounds.Resolve(
            ContainerServiceRole.PostgreSql,
            getEnv: env);

        await Assert.That(policy.MemoryLimitBytes).IsEqualTo(1536L * BytesPerMegabyte);
    }

    [Test]
    [Arguments("0")]
    [Arguments("-256")]
    [Arguments("invalid")]
    [Arguments("32")]
    public async Task Resolve_WithInvalidOrUnsafeContainerMemoryOverride_FallsBackToRoleDefault(string rawValue)
    {
        Func<string, string?> env = name => name == ContainerResourceBounds.ContainerMemoryLimitEnvVar
            ? rawValue
            : null;

        ContainerResourcePolicy policy = ContainerResourceBounds.Resolve(
            ContainerServiceRole.PostgreSql,
            getEnv: env);

        await Assert.That(policy.MemoryLimitBytes).IsEqualTo(1024L * BytesPerMegabyte);
    }

    [Test]
    public async Task Resolve_KeycloakRole_SetsDefaultHeapLimitAndOverride()
    {
        ContainerResourcePolicy defaultPolicy = ContainerResourceBounds.Resolve(
            ContainerServiceRole.Keycloak,
            getEnv: _ => null);

        await Assert.That(defaultPolicy.KeycloakHeapLimitMegabytes).IsEqualTo(512);

        ContainerResourcePolicy nonKeycloak = ContainerResourceBounds.Resolve(
            ContainerServiceRole.PostgreSql,
            getEnv: _ => null);

        await Assert.That(nonKeycloak.KeycloakHeapLimitMegabytes).IsNull();

        ContainerResourcePolicy overridden = ContainerResourceBounds.Resolve(
            ContainerServiceRole.Keycloak,
            getEnv: name => name == ContainerResourceBounds.KeycloakHeapLimitEnvVar ? "768" : null);

        await Assert.That(overridden.KeycloakHeapLimitMegabytes).IsEqualTo(768);
    }

    [Test]
    [Arguments("true")]
    [Arguments("1")]
    [Arguments("TRUE")]
    public async Task ShouldEnableReuse_OptedInWithoutOwnershipOrExplicitName_ReturnsTrue(string flag)
    {
        Func<string, string?> env = name => name == ContainerResourceBounds.ContainerReuseEnvVar
            ? flag
            : null;

        ContainerResourcePolicy policy = ContainerResourceBounds.Resolve(
            ContainerServiceRole.PostgreSql,
            hasAssuranceOwner: false,
            hasExplicitContainerName: false,
            getEnv: env);

        await Assert.That(policy.EnableReuse).IsTrue();
    }

    [Test]
    [Arguments(true, false)]
    [Arguments(false, true)]
    [Arguments(true, true)]
    public async Task ShouldEnableReuse_WhenAssuranceOwnerOrExplicitNameActive_SuppressesReuseEvenWhenOptedIn(
        bool hasAssuranceOwner,
        bool hasExplicitContainerName)
    {
        Func<string, string?> env = name => name == ContainerResourceBounds.ContainerReuseEnvVar
            ? "true"
            : null;

        ContainerResourcePolicy policy = ContainerResourceBounds.Resolve(
            ContainerServiceRole.PostgreSql,
            hasAssuranceOwner: hasAssuranceOwner,
            hasExplicitContainerName: hasExplicitContainerName,
            getEnv: env);

        await Assert.That(policy.EnableReuse).IsFalse();
    }

    [Test]
    public async Task FormatKeycloakJavaOpts_PreservesExistingFlagsAndAppendsHeapBounds()
    {
        string fresh = ContainerResourceBounds.FormatKeycloakJavaOpts(null, 512);
        await Assert.That(fresh).IsEqualTo("-Xms64m -Xmx512m");

        string merged = ContainerResourceBounds.FormatKeycloakJavaOpts("-XX:+UseG1GC", 768);
        await Assert.That(merged).IsEqualTo("-XX:+UseG1GC -Xms64m -Xmx768m");
    }
}
