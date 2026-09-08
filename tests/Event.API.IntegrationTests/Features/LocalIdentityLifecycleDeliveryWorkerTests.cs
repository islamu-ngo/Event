
using Event.Api.IntegrationTests.Fixtures;
using Explore.Application.Contracts.Identity;
using Explore.Application.Contracts.Persistence;
using Explore.Application.Contracts.Services;
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
    [Arguments(IdentityDatabaseTopology.Colocated)]
    [Arguments(IdentityDatabaseTopology.External)]
    public async Task ExistingWorkerRepairsExpiredConsumedReceiptWhileEmailIsDisabled(IdentityDatabaseTopology topology)
    {
        await using var fixture = await LocalIdentityLifecycleHttpFixture.CreateAsync(verified: false, topology: topology);
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
            var disabled = await consume.ServiceProvider.GetRequiredService<ISettingMutationLock>().ExecuteOrderedGroupsAsync(
                [Explore.Application.Settings.EmailDeliverySettingKeys.All], outer =>
                    consume.ServiceProvider.GetRequiredService<IUnitOfWork>().ExecuteSerializableAsync(async inner =>
                    {
                        var impact = (await consume.ServiceProvider.GetRequiredService<IEmailDeliveryDisableImpactReader>().ReadAsync(null, inner))!;
                        var proof = consume.ServiceProvider.GetRequiredService<IEmailDeliveryDisableTokenService>()
                            .Issue(fixture.Binding.LocalSubjectId, impact);
                        return await consume.ServiceProvider.GetRequiredService<IEmailDeliverySettingsWriter>().DisableAsync(new(null,
                            fixture.Binding.LocalSubjectId, impact.Revision, proof.Token, EmailDeliveryDisableConfirmation.RequiredAcknowledgement), inner);
                    }, outer), token);
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
}
