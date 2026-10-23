using Aspire.Hosting.ApplicationModel;
using Explore.AppHost;

namespace Event.Distributed.IntegrationTests;

[Category("Harness")]
public sealed class AppHostBffSessionAuthorityTests
{
    [Test]
    [Arguments("Split", OwnedAppHostProfile.Standard, OwnedBffPeerProtection.Shared)]
    [Arguments("Standalone", OwnedAppHostProfile.Standard, OwnedBffPeerProtection.Shared)]
    [Arguments("Split", OwnedAppHostProfile.ExternalIdentitySharedBff, OwnedBffPeerProtection.Shared)]
    [Arguments("Split", OwnedAppHostProfile.ExternalIdentitySharedBff, OwnedBffPeerProtection.IndependentValidAuthority)]
    public async Task NativeProjectionKeepsDedicatedSessionAuthorityWithinBffHosts(
        string topology, OwnedAppHostProfile profile, OwnedBffPeerProtection peerProtection)
    {
        using var deadline = new CancellationTokenSource(TimeSpan.FromMinutes(1));
        var completion = await Assert.That(async () => await OwnedAppHostFixture.CreateAsync(topology,
            false, "SingleTenant", null, async builder =>
            {
                var authority = builder.Resources.OfType<RedisResource>()
                    .Single(resource => resource.Name == "bff-session-authority");
                await Assert.That(authority.TryGetContainerImageName(out var image)).IsTrue();
                await Assert.That(image).IsEqualTo("docker.io/library/redis:7.2.16-alpine@sha256:"
                    + "f8548b29a1f1cb358b5fcfec1e0126beab57ad15776c1e87623f865579f16c5a");
                var cache = builder.Resources.OfType<RedisResource>().Single(resource => resource.Name == "cache");
                await Assert.That(cache.TryGetContainerImageName(out var cacheImage)).IsTrue();
                await Assert.That(cacheImage).IsEqualTo(image)
                    .Because("Both Redis backends must use the admitted supported server image.");
                var endpoint = authority.Annotations.OfType<EndpointAnnotation>().Single();
                endpoint.AllocatedEndpoint = new AllocatedEndpoint(endpoint, "127.0.0.1", 16379, "6379");
                string runId = builder.Configuration["ISLAMU_TEST_RUN_ID"]!;
                string selected = builder.Configuration["TestingEnvironment:BFF_SESSION_REDIS_PASSWORD"]!;
                await Assert.That(string.IsNullOrWhiteSpace(selected)).IsFalse();
                await Assert.That(authority.Annotations.OfType<ContainerMountAnnotation>()
                    .Single(mount => mount.Target == "/data").Source)
                    .IsEqualTo($"islamu-test-{runId}-islamu-event-bff-session-authority-data");

                int bffHosts = 0;
                foreach (var project in builder.Resources.OfType<ProjectResource>())
                {
                    await Assert.That(project.Annotations.OfType<WaitAnnotation>()
                        .Any(wait => ReferenceEquals(wait.Resource, authority))).IsFalse();
                    var context = new EnvironmentCallbackContext(builder.ExecutionContext, project);
                    foreach (var callback in project.Annotations.OfType<EnvironmentCallbackAnnotation>())
                        await callback.Callback(context);
                    bool isBff = project.Name is "explore-blazor" or "explore-blazor-peer" or "event-standalone";
                    if (isBff)
                    {
                        await Assert.That(context.EnvironmentVariables["BFF_SESSION_REDIS_PASSWORD"]
                            is ParameterResource { Secret: true }).IsTrue()
                            .Because("Aspire must preserve native secret redaction metadata during projection.");
                    }
                    string? password = await ResolveAsync(context.EnvironmentVariables["BFF_SESSION_REDIS_PASSWORD"], deadline.Token);
                    await Assert.That(password == (isBff ? selected : string.Empty)).IsTrue()
                        .Because("Only a selected Environment BFF consumer may receive the session credential.");
                    string? projectedEndpoint = await ResolveAsync(
                        context.EnvironmentVariables["BFF_SESSION_REDIS_ENDPOINT"], deadline.Token);
                    await Assert.That(projectedEndpoint).IsEqualTo(isBff ? "redis://127.0.0.1:16379" : string.Empty);
                    await Assert.That(context.EnvironmentVariables["BFF_SESSION_NAMESPACE"] as string)
                        .IsEqualTo(isBff ? runId : string.Empty);
                    if (isBff)
                        bffHosts++;
                }
                await Assert.That(bffHosts).IsEqualTo(profile == OwnedAppHostProfile.ExternalIdentitySharedBff ? 2 : 1);
                throw new InvalidOperationException("assurance_bff_session_projection_complete");
            }, deadline.Token, profile, peerProtection)).Throws<InvalidOperationException>();
        await Assert.That(completion!.Message).IsEqualTo("assurance_bff_session_projection_complete");
    }

    private static async Task<string?> ResolveAsync(object value, CancellationToken cancellationToken) => value switch
    {
        string text => text,
        IValueProvider provider => await provider.GetValueAsync(cancellationToken),
        _ => throw new InvalidOperationException("assurance_projection_value_invalid")
    };
}
