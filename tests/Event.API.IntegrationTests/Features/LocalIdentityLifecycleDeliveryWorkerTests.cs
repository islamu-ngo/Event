
using Event.Api.IntegrationTests.Fixtures;
using Explore.Application.Contracts.Identity;
using Explore.Application.Contracts.Persistence;
using Explore.Infrastructure.Authentication;
using Explore.Persistence.Identity;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging.Abstractions;

namespace Event.Api.IntegrationTests.Features;

[NotInParallel]
public sealed class LocalIdentityLifecycleDeliveryWorkerTests
{
    [Test]
    [Arguments(IdentityDatabaseTopology.Colocated, "https://lifecycle.test")]
    [Arguments(IdentityDatabaseTopology.External, "https://lifecycle.test")]
    [Arguments(IdentityDatabaseTopology.Colocated, "invalid")]
    [Arguments(IdentityDatabaseTopology.External, "invalid")]
    [Arguments(IdentityDatabaseTopology.Colocated, null)]
    [Arguments(IdentityDatabaseTopology.External, null)]
    public async Task ExistingWorkerRepairsExpiredConsumedReceiptWhileEmailIsDisabled(
        IdentityDatabaseTopology topology, string? publicBaseUrl)
    {
        await using var fixture = await LocalIdentityLifecycleHttpFixture.CreateAsync(
            verified: false, topology: topology, publicBaseUrl: publicBaseUrl,
            storedPublicBaseUrl: "https://lifecycle.test");
        using var timeout = new CancellationTokenSource(TimeSpan.FromSeconds(30));
        var token = timeout.Token;
        await using (var intake = fixture.Host.Services.CreateAsyncScope())
        {
            var lifecycle = intake.ServiceProvider.GetRequiredService<ILocalIdentityLifecycleStore>();
            var request = await lifecycle.FindRequestByIdentifierAsync(fixture.Login.Identifier, LocalIdentityLifecyclePurpose.EmailVerification, token);
            await Assert.That(request).IsNotNull();
            await Assert.That(await lifecycle.BeginAsync(request!, token)).IsNotNull();
        }
        var handoff = await fixture.DrainOneAsync();
        await using (var consume = fixture.Host.Services.CreateAsyncScope())
        {
            var result = await consume.ServiceProvider.GetRequiredService<ILocalIdentityLifecycleStore>()
                .ConsumeAsync(new(handoff.Operation, handoff.Token), token);
            await Assert.That(result.Outcome).IsEqualTo(LocalIdentityLifecycleOutcome.Consumed);
            // Fixture setup is a nonhuman lifecycle operation, not an administrator request.
            var disabled = await consume.ServiceProvider.GetRequiredService<IInternalEmailDeliverySettingsWriter>()
                .DisableInternalAsync(null, actorUserId: null, token);
            await Assert.That(disabled.Status).IsEqualTo(EmailDeliverySettingsWriteStatus.Applied);
        }
        await Assert.That((await fixture.ReadMirrorAsync()).Verified).IsFalse();
        fixture.Clock.Advance(TimeSpan.FromMinutes(31));
        var worker = new LocalIdentityLifecycleDeliveryWorker(fixture.Host.Services.GetRequiredService<IServiceScopeFactory>(),
            NullLogger<LocalIdentityLifecycleDeliveryWorker>.Instance);
        using (worker)
        {
            await worker.RunOnceAsync(token);
            await worker.RunOnceAsync(token);
        }
        await Assert.That((await fixture.ReadMirrorAsync()).Verified).IsTrue();
        await Assert.That(fixture.Smtp.Handoffs.Count).IsEqualTo(1);
        await using var identity = fixture.Native.CreateIdentityDatabase();
        var operation = await identity.Set<LocalIdentityLifecycleOperation>().AsNoTracking().SingleAsync(row => row.Id == handoff.Operation.OperationId, token);
        await Assert.That(operation.SynchronizedAt).IsNotNull();
        await Assert.That(operation.DeliveryAttemptCount).IsEqualTo(1);
    }

    [Test]
    [Arguments(IdentityDatabaseTopology.Colocated, null, true)]
    [Arguments(IdentityDatabaseTopology.External, null, true)]
    [Arguments(IdentityDatabaseTopology.Colocated, "invalid", false)]
    [Arguments(IdentityDatabaseTopology.External, "invalid", false)]
    [Arguments(IdentityDatabaseTopology.Colocated, "http://lifecycle.test", false)]
    [Arguments(IdentityDatabaseTopology.External, "http://lifecycle.test", false)]
    public async Task WorkerUsesPersistedInstanceOriginUnlessExplicitOverrideRejectsDelivery(
        IdentityDatabaseTopology topology, string? publicBaseUrl, bool sends)
    {
        await using var fixture = await LocalIdentityLifecycleHttpFixture.CreateAsync(
            verified: false, topology: topology, publicBaseUrl: publicBaseUrl,
            storedPublicBaseUrl: "https://lifecycle.test");
        var token = LocalIdentityLifecycleHttpFixture.CancellationToken;
        await using (var intake = fixture.Host.Services.CreateAsyncScope())
        {
            var lifecycle = intake.ServiceProvider.GetRequiredService<ILocalIdentityLifecycleStore>();
            var request = await lifecycle.FindRequestByIdentifierAsync(
                fixture.Login.Identifier, LocalIdentityLifecyclePurpose.EmailVerification, token);
            await Assert.That(request).IsNotNull();
            await Assert.That(await lifecycle.BeginAsync(request!, token)).IsNotNull();
        }

        using var worker = new LocalIdentityLifecycleDeliveryWorker(
            fixture.Host.Services.GetRequiredService<IServiceScopeFactory>(),
            NullLogger<LocalIdentityLifecycleDeliveryWorker>.Instance);
        await worker.RunOnceAsync(token);
        await worker.RunOnceAsync(token);

        await Assert.That(fixture.Smtp.Handoffs.Count).IsEqualTo(sends ? 1 : 0);
        await Assert.That(fixture.Smtp.Callbacks.Count).IsEqualTo(sends ? 1 : 0);
        if (sends)
            await Assert.That(fixture.Smtp.Callbacks.Single())
                .IsEqualTo(new Uri("https://lifecycle.test/auth/local-account-recovery"));
        await using var identity = fixture.Native.CreateIdentityDatabase();
        var operation = await identity.Set<LocalIdentityLifecycleOperation>().AsNoTracking().SingleAsync(token);
        await Assert.That(operation.DeliveryAttemptCount).IsEqualTo(sends ? 1 : 0);
        await Assert.That(operation.DeliveryState).IsEqualTo(sends
            ? LocalIdentityLifecycleDeliveryState.Accepted : LocalIdentityLifecycleDeliveryState.Pending);
    }
}
