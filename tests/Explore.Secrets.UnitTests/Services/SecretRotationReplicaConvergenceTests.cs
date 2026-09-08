using System.Reflection;
using Explore.Secrets.Configuration;
using Explore.Secrets.Services;
using Explore.Domain.Secrets;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;
using NSubstitute;

namespace Explore.Secrets.UnitTests.Services;

public sealed class SecretRotationReplicaConvergenceTests
{
    [Test]
    public async Task HttpRotationReturnsValueFreeLocalAcknowledgement()
    {
        MethodInfo method = typeof(RotationAwareHttpClientFactory)
            .GetMethod(nameof(RotationAwareHttpClientFactory.ForceRotateAsync))!;

        await Assert.That(method.ReturnType.IsGenericType).IsTrue();
        await Assert.That(method.ReturnType.GenericTypeArguments.Single().Name)
            .IsEqualTo("SecretRotationLocalAcknowledgement");
    }

    [Test]
    public async Task DatabaseRotationReturnsValueFreeLocalAcknowledgement()
    {
        MethodInfo method = typeof(IRotationAwareDbContextFactory)
            .GetMethod(nameof(IRotationAwareDbContextFactory.ForceRefresh))!;

        await Assert.That(method.ReturnType.Name)
            .IsEqualTo("SecretRotationLocalAcknowledgement");
    }

    [Test]
    public async Task DeploymentConvergenceRequiresEveryReplicaAcknowledgement()
    {
        Type? convergence = typeof(RotationAwareHttpClientFactory).Assembly.GetType(
            "Explore.Secrets.Services.SecretRotationReplicaConvergence");

        await Assert.That(convergence).IsNotNull();
    }

    [Test]
    public async Task PartialActivationNeverConvergesAndFailsClosedAtDeadline()
    {
        Guid attemptId = Guid.CreateVersion7();
        var required = new HashSet<string>(StringComparer.Ordinal) { "replica-a", "replica-b" };
        DateTimeOffset now = DateTimeOffset.UtcNow;
        var first = new SecretRotationLocalAcknowledgement(
            attemptId,
            "replica-a",
            "http",
            SecretRotationLocalStatus.Activated,
            now);

        SecretRotationConvergenceResult pending = SecretRotationReplicaConvergence.Evaluate(
            attemptId,
            required,
            [first],
            now,
            now.AddMinutes(5),
            providerSupportsOverlap: true);
        SecretRotationConvergenceResult stale = SecretRotationReplicaConvergence.Evaluate(
            attemptId,
            required,
            [first],
            now.AddMinutes(5),
            now.AddMinutes(5),
            providerSupportsOverlap: true);

        await Assert.That(pending.Status).IsEqualTo(SecretRotationConvergenceStatus.Pending);
        await Assert.That(stale.Status).IsEqualTo(SecretRotationConvergenceStatus.FailedClosed);
    }

    [Test]
    public async Task EveryReplicaMustAcknowledgeTheSameAttempt()
    {
        Guid attemptId = Guid.CreateVersion7();
        var required = new HashSet<string>(StringComparer.Ordinal) { "replica-a", "replica-b" };
        DateTimeOffset now = DateTimeOffset.UtcNow;
        SecretRotationLocalAcknowledgement[] acknowledgements =
        [
            new(attemptId, "replica-a", "http", SecretRotationLocalStatus.Activated, now),
            new(attemptId, "replica-b", "http", SecretRotationLocalStatus.Activated, now)
        ];

        SecretRotationConvergenceResult result = SecretRotationReplicaConvergence.Evaluate(
            attemptId,
            required,
            acknowledgements,
            now,
            now.AddMinutes(5),
            providerSupportsOverlap: true);

        await Assert.That(result.IsConverged).IsTrue();
        await Assert.That(result.ActivatedReplicaCount).IsEqualTo(2);
    }

    [Test]
    public async Task NoOverlapProviderRequiresCoordinatedRestart()
    {
        SecretRotationConvergenceResult result = SecretRotationReplicaConvergence.Evaluate(
            Guid.CreateVersion7(),
            new HashSet<string> { "replica-a" },
            [],
            DateTimeOffset.UtcNow,
            DateTimeOffset.UtcNow.AddMinutes(5),
            providerSupportsOverlap: false);

        await Assert.That(result.Status)
            .IsEqualTo(SecretRotationConvergenceStatus.CoordinatedRestartRequired);
    }

    [Test]
    public async Task EveryRegisteredConsumerHasOneNormativeRotationProfile()
    {
        SecretRotationProfile[] profiles = SecretDefinitionRegistry.All.Keys
            .Select(SecretDefinitionRegistry.GetRotationProfile)
            .ToArray();

        await Assert.That(profiles.Length).IsEqualTo(SecretDefinitionRegistry.All.Count);
        await Assert.That(profiles.All(profile =>
            !string.IsNullOrWhiteSpace(profile.Owner)
            && profile.CandidateValidationRequired
            && !string.IsNullOrWhiteSpace(profile.BreakGlassAction))).IsTrue();
    }

    [Test]
    public async Task RejectedHttpCandidateLeavesCurrentClientActive()
    {
        var credentials = Monitor(new HttpClientCredentialOptions());
        using var factory = new RotationAwareHttpClientFactory(
            credentials,
            Monitor(new RotationOptions()),
            Substitute.For<ILogger<RotationAwareHttpClientFactory>>(),
            validateCandidate: (_, _) => Task.FromResult(false),
            replicaId: "replica-a");
        HttpClient current = factory.CreateClient("provider");

        SecretRotationLocalAcknowledgement acknowledgement =
            await factory.ForceRotateAsync("provider");

        await Assert.That(acknowledgement.Status).IsEqualTo(SecretRotationLocalStatus.Rejected);
        await Assert.That(factory.CreateClient("provider")).IsSameReferenceAs(current);
    }

    [Test]
    public async Task RejectedDatabaseCandidateLeavesRotationCountUnchanged()
    {
        using var factory = new RotationAwareDbContextFactory<ReplicaTestDbContext>(
            options => new ReplicaTestDbContext(options),
            Monitor(new DatabaseConnectionOptions { ConnectionString = "Host=database;Database=event" }),
            Monitor(new RotationOptions()),
            NullLogger<RotationAwareDbContextFactory<ReplicaTestDbContext>>.Instance,
            validateCandidate: _ => false,
            replicaId: "replica-a");

        SecretRotationLocalAcknowledgement acknowledgement = factory.ForceRefresh();

        await Assert.That(acknowledgement.Status).IsEqualTo(SecretRotationLocalStatus.Rejected);
        await Assert.That(factory.RotationCount).IsEqualTo(0);
    }

    private static IOptionsMonitor<T> Monitor<T>(T value)
    {
        var monitor = Substitute.For<IOptionsMonitor<T>>();
        monitor.CurrentValue.Returns(value);
        return monitor;
    }

    private sealed class ReplicaTestDbContext(DbContextOptions<ReplicaTestDbContext> options)
        : DbContext(options);
}
