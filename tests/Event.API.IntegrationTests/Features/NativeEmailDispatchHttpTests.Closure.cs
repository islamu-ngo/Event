using System.Net;
using Event.Api.IntegrationTests.Fixtures;
using Explore.API.Controllers;
using Explore.Application.Contracts.Operations;
using Explore.Application.Contracts.Persistence;
using Explore.Application.Features.EmailDispatch;
using Explore.Application.Features.EmailDispatch.Requests.Commands;
using Explore.API.Hateoas;
using Explore.Application.Responses;
using Explore.Domain;
using Explore.Domain.Constants;
using Explore.Domain.Enums;
using Explore.Persistence;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;

namespace Event.Api.IntegrationTests.Features;

public sealed partial class NativeEmailDispatchHttpTests
{
    [Test]
    public async Task AdminControllerHasOnlyClosedNativePortsAndHalAssemblers()
    {
        foreach (var parameter in typeof(EmailDispatchAdminController).GetConstructors().Single().GetParameters())
        {
            var type = parameter.ParameterType;
            await Assert.That(type.IsGenericType).IsTrue();
            await Assert.That(new[] { typeof(ICommandHandler<,>), typeof(IQueryHandler<,>), typeof(IResourceAssembler<,>) })
                .Contains(type.GetGenericTypeDefinition());
            await Assert.That(type.ContainsGenericParameters).IsFalse();
        }
    }

    [Test]
    public async Task TenantPauseResumeRetainsSingletonAndDoesNotPauseAnotherTenant()
    {
        await using var factory = await NativeEmailDispatchWebApplicationFactory.CreateAsync();
        using var client = factory.Client(factory.TenantAdminId);
        string url = Root + $"/tenants/{PlatformDefaults.DefaultTenantId}/pause";
        var first = await CommandAsync(await client.PutAsync(url + "?reason=maintenance", null));
        var repeated = await CommandAsync(await client.PutAsync(url + "?reason=maintenance", null));
        await Assert.That(repeated.Id).IsEqualTo(first.Id);
        using var scope = factory.Services.CreateScope();
        var repository = scope.ServiceProvider.GetRequiredService<IEmailDispatchOutboxRepository>();
        await Assert.That(await repository.IsTenantPaused(PlatformDefaults.DefaultTenantId, default)).IsTrue();
        await Assert.That(await repository.IsTenantPaused(factory.OtherTenantId, default)).IsFalse();
        var control = await repository.GetTenantControl(PlatformDefaults.DefaultTenantId, default);
        await Assert.That(control!.PausedBy).IsEqualTo(factory.TenantAdminId);
        await Assert.That(control.PauseReason).IsEqualTo("maintenance");
        var resumed = await CommandAsync(await client.DeleteAsync(url));
        await Assert.That(resumed.Id).IsEqualTo(first.Id);
        control = await repository.GetTenantControl(PlatformDefaults.DefaultTenantId, default);
        await Assert.That(control!.IsPaused).IsFalse();
        await Assert.That(control.PauseReason).IsNull();
        await Assert.That(control.PausedAt).IsNull();
        await Assert.That(control.PausedBy).IsNull();
    }

    [Test]
    [Arguments(EmailDispatchStatus.Parked)]
    [Arguments(EmailDispatchStatus.DeadLettered)]
    [Arguments(EmailDispatchStatus.Unknown)]
    public async Task ResolutionNeverQueuesMailAndCannotBeRepeated(EmailDispatchStatus state)
    {
        await using var factory = await NativeEmailDispatchWebApplicationFactory.CreateAsync();
        Guid id = await factory.SeedDispatchAsync(state);
        using var client = factory.Client(factory.TenantAdminId);
        var result = await CommandAsync(await client.PostAsync(ClosureUrl("resolve-without-replay", id), null));
        await Assert.That(result.Id).IsEqualTo(id);
        var row = (await StatusAsync(factory)).Single();
        await Assert.That(row.DeliveryStatus).IsEqualTo(EmailDispatchStatus.Skipped);
        await Assert.That(row.AttemptCount).IsEqualTo(3);
        await Assert.That(row.ParkReason).IsNull();
        await Assert.That(row.LastFailureCategory).IsEqualTo("operator_resolved_without_replay");
        await ProblemAsync(await client.PostAsync(ClosureUrl("resolve-without-replay", id), null),
            HttpStatusCode.Conflict, EmailDispatchFailureCodes.InvalidTransition);
        await Assert.That((await StatusAsync(factory)).Single()).IsEqualTo(row);
        using var scope = AdminScope(factory);
        var db = scope.ServiceProvider.GetRequiredService<ExploreDbContext>();
        await Assert.That((await db.EmailDispatchReceipts.SingleAsync(value => value.EmailDispatchOutboxId == id)).Status)
            .IsEqualTo(EmailDispatchReceiptStatus.Skipped);
    }

    [Test]
    [Arguments(true)]
    [Arguments(false)]
    public async Task ReconciliationSettlesAllLedgersExactlyOnceWithoutSending(bool delivered)
    {
        await using var factory = await NativeEmailDispatchWebApplicationFactory.CreateAsync();
        Guid id = await factory.SeedDispatchAsync(EmailDispatchStatus.Unknown);
        using var client = factory.Client(factory.TenantAdminId);
        string url = ClosureUrl("reconcile", id) + $"&outcome={(delivered ? "Delivered" : "NotDelivered")}&providerMessageId=%20receipt-evidence%20";
        await CommandAsync(await client.PostAsync(url, null));
        var row = (await StatusAsync(factory)).Single();
        await Assert.That(row.DeliveryStatus).IsEqualTo(delivered ? EmailDispatchStatus.Sent : EmailDispatchStatus.Pending);
        await Assert.That(row.UnknownAt).IsNull();
        await Assert.That(row.DeliveredAt.HasValue).IsEqualTo(delivered);
        await Assert.That(row.AttemptCount).IsEqualTo(3);
        await ProblemAsync(await client.PostAsync(url, null), HttpStatusCode.Conflict, EmailDispatchFailureCodes.InvalidTransition);
        await Assert.That((await StatusAsync(factory)).Single()).IsEqualTo(row);
        using var scope = AdminScope(factory);
        var db = scope.ServiceProvider.GetRequiredService<ExploreDbContext>();
        var attempt = await db.EmailDispatchAttempts.SingleAsync(value => value.EmailDispatchOutboxId == id);
        var receipt = await db.EmailDispatchReceipts.SingleAsync(value => value.EmailDispatchOutboxId == id);
        var delivery = await db.NotificationDeliveries.SingleAsync(value => value.EmailDispatchOutboxId == id);
        await Assert.That(attempt.Outcome).IsEqualTo(delivered ? EmailDispatchAttemptOutcome.Succeeded : EmailDispatchAttemptOutcome.Failed);
        await Assert.That(receipt.Status).IsEqualTo(delivered ? EmailDispatchReceiptStatus.Completed : EmailDispatchReceiptStatus.Received);
        await Assert.That(delivery.StatusId).IsEqualTo((int)(delivered ? NotificationDeliveryStatusEnum.Delivered : NotificationDeliveryStatusEnum.Queued));
        await Assert.That(attempt.ProviderMessageId).IsEqualTo(delivered ? "receipt-evidence" : null);
        await Assert.That(receipt.ProviderMessageId).IsEqualTo(attempt.ProviderMessageId);
        await Assert.That(delivery.ProviderMessageId).IsEqualTo(attempt.ProviderMessageId);
        await Assert.That(attempt.UpdatedBy).IsEqualTo(factory.TenantAdminId);
    }

    [Test]
    [Arguments("park", EmailDispatchStatus.Processing, false)]
    [Arguments("park", EmailDispatchStatus.Unknown, false)]
    [Arguments("park", EmailDispatchStatus.Sent, false)]
    [Arguments("park", EmailDispatchStatus.Skipped, false)]
    [Arguments("park", EmailDispatchStatus.Pending, true)]
    [Arguments("resolve-without-replay", EmailDispatchStatus.Pending, false)]
    [Arguments("resolve-without-replay", EmailDispatchStatus.Sent, false)]
    [Arguments("resolve-without-replay", EmailDispatchStatus.Parked, true)]
    [Arguments("reconcile", EmailDispatchStatus.Parked, false)]
    [Arguments("reconcile", EmailDispatchStatus.Unknown, true)]
    public async Task RemainingTransitionsFailClosedForUnsafeOrRedactedRows(string operation, EmailDispatchStatus state, bool redacted)
    {
        await using var factory = await NativeEmailDispatchWebApplicationFactory.CreateAsync();
        Guid id = await factory.SeedDispatchAsync(state, redacted);
        var before = (await StatusAsync(factory)).Single();
        using var client = factory.Client();
        using var request = new HttpRequestMessage(operation == "park" ? HttpMethod.Put : HttpMethod.Post,
            ClosureUrl(operation, id) + (operation == "reconcile" ? "&outcome=Delivered" : string.Empty));
        await ProblemAsync(await client.SendAsync(request), HttpStatusCode.Conflict, EmailDispatchFailureCodes.InvalidTransition);
        await Assert.That((await StatusAsync(factory)).Single()).IsEqualTo(before);
    }

    [Test]
    public async Task RemainingCapabilitiesRejectAnonymousMemberAndForeignTenantAuthority()
    {
        await using var factory = await NativeEmailDispatchWebApplicationFactory.CreateAsync();
        Guid id = await factory.SeedDispatchAsync(EmailDispatchStatus.Unknown);
        Guid foreign = await factory.SeedDispatchAsync(EmailDispatchStatus.Unknown, tenantId: factory.OtherTenantId);
        using var anonymous = factory.CreateClient();
        using var member = factory.Client(factory.MemberId);
        using var admin = factory.Client(factory.TenantAdminId);
        foreach (string operation in new[] { "pause", "park", "resolve-without-replay", "reconcile" })
        {
            var method = operation is "pause" or "park" ? HttpMethod.Put : HttpMethod.Post;
            string Url(Guid tenant, Guid row) => operation == "pause"
                ? Root + $"/tenants/{tenant}/pause?reason=maintenance"
                : ClosureUrl(operation, row, tenant) + (operation == "reconcile" ? "&outcome=Delivered" : string.Empty);
            using (var request = new HttpRequestMessage(method, Url(PlatformDefaults.DefaultTenantId, id)))
                await ProblemAsync(await anonymous.SendAsync(request), HttpStatusCode.Unauthorized);
            using (var request = new HttpRequestMessage(method, Url(PlatformDefaults.DefaultTenantId, id)))
                await ProblemAsync(await member.SendAsync(request), HttpStatusCode.Forbidden);
            using (var request = new HttpRequestMessage(method, Url(factory.OtherTenantId, foreign)))
                await ProblemAsync(await admin.SendAsync(request), HttpStatusCode.Forbidden);
            if (operation != "pause")
            {
                using var request = new HttpRequestMessage(method, Url(PlatformDefaults.DefaultTenantId, foreign));
                await ProblemAsync(await admin.SendAsync(request), HttpStatusCode.NotFound, EmailDispatchFailureCodes.NotFound);
            }
        }
        await Assert.That((await StatusAsync(factory)).Single().DeliveryStatus).IsEqualTo(EmailDispatchStatus.Unknown);
    }

    [Test]
    [Arguments(false)]
    [Arguments(true)]
    public async Task ResolutionOrReconciliationLosesToAnotherCommittedTransition(bool reconcile)
    {
        var boundary = new TransactionBoundary();
        await using var factory = await NativeEmailDispatchWebApplicationFactory.CreateAsync(boundary);
        Guid id = await factory.SeedDispatchAsync(reconcile ? EmailDispatchStatus.Unknown : EmailDispatchStatus.Parked);
        using var client = factory.Client();
        boundary.BeforeStart = async () => await CommandAsync(await client.PostAsync(
            reconcile ? ClosureUrl("resolve-without-replay", id) : ReplayUrl(id), null));
        await ProblemAsync(await client.PostAsync(reconcile
                ? ClosureUrl("reconcile", id) + "&outcome=Delivered" : ClosureUrl("resolve-without-replay", id), null),
            HttpStatusCode.Conflict, EmailDispatchFailureCodes.ConcurrentTransition);
        await Assert.That((await StatusAsync(factory)).Single().DeliveryStatus)
            .IsEqualTo(reconcile ? EmailDispatchStatus.Skipped : EmailDispatchStatus.Pending);
    }

    [Test]
    [Arguments(false, false)]
    [Arguments(true, false)]
    [Arguments(false, true)]
    [Arguments(true, true)]
    public async Task SettlementFailureOrCancellationRollsBackEveryLedger(bool reconcile, bool cancel)
    {
        var entered = new TaskCompletionSource<CancellationToken>(TaskCreationOptions.RunContinuationsAsynchronously);
        var release = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var boundary = new ReceiptFailure
        {
            BeforeWrite = async token =>
            {
                if (!cancel) throw new IOException("Injected settlement failure.");
                entered.SetResult(token);
                await release.Task.WaitAsync(token);
            }
        };
        await using var factory = await NativeEmailDispatchWebApplicationFactory.CreateAsync(boundary);
        Guid id = await factory.SeedDispatchAsync(EmailDispatchStatus.Unknown);
        var before = (await StatusAsync(factory)).Single();
        using (var scope = AdminScope(factory))
        using (var cancellation = new CancellationTokenSource())
        {
            boundary.Armed = true;
            Task<BaseCommandResponse<Guid>> pending = reconcile
                ? scope.ServiceProvider.GetRequiredService<ICommandHandler<ReconcileUnknownEmailDispatchCommand, BaseCommandResponse<Guid>>>()
                    .ExecuteAsync(new()
                    {
                        TenantId = PlatformDefaults.DefaultTenantId,
                        OutboxId = id,
                        Outcome = EmailDispatchUnknownReconciliationOutcome.Delivered,
                        Reason = "reviewed"
                    }, cancellation.Token)
                : scope.ServiceProvider.GetRequiredService<ICommandHandler<ResolveEmailDispatchWithoutReplayCommand, BaseCommandResponse<Guid>>>()
                    .ExecuteAsync(new() { TenantId = PlatformDefaults.DefaultTenantId, OutboxId = id, Reason = "reviewed" }, cancellation.Token);
            try
            {
                if (cancel)
                {
                    await Assert.That(await entered.Task.WaitAsync(TimeSpan.FromSeconds(10))).IsEqualTo(cancellation.Token);
                    await cancellation.CancelAsync();
                    await Assert.That(async () => await pending.WaitAsync(TimeSpan.FromSeconds(10))).Throws<OperationCanceledException>();
                }
                else await Assert.That(async () => await pending).Throws<IOException>();
            }
            finally { release.TrySetResult(); }
        }
        await Assert.That(boundary.Triggered).IsTrue();
        await Assert.That((await StatusAsync(factory)).Single()).IsEqualTo(before);
        using var observer = AdminScope(factory);
        var db = observer.ServiceProvider.GetRequiredService<ExploreDbContext>();
        await Assert.That((await db.EmailDispatchAttempts.SingleAsync(value => value.EmailDispatchOutboxId == id)).Outcome)
            .IsEqualTo(EmailDispatchAttemptOutcome.Unknown);
        await Assert.That((await db.EmailDispatchReceipts.SingleAsync(value => value.EmailDispatchOutboxId == id)).Status)
            .IsEqualTo(EmailDispatchReceiptStatus.Unknown);
        await Assert.That((await db.NotificationDeliveries.SingleAsync(value => value.EmailDispatchOutboxId == id)).StatusId)
            .IsEqualTo((int)NotificationDeliveryStatusEnum.Unknown);
    }

    [Test]
    public async Task RemainingNativePortsOwnValidationBeforeAnyDurableMutation()
    {
        await using var factory = await NativeEmailDispatchWebApplicationFactory.CreateAsync();
        Guid id = await factory.SeedDispatchAsync(EmailDispatchStatus.Unknown);
        var before = (await StatusAsync(factory)).Single();
        using var scope = AdminScope(factory);
        var services = scope.ServiceProvider;
        var park = await services.GetRequiredService<ICommandHandler<ParkEmailDispatchCommand, BaseCommandResponse<Guid>>>()
            .ExecuteAsync(new() { TenantId = PlatformDefaults.DefaultTenantId, OutboxId = id, Reason = "" }, default);
        var resolve = await services.GetRequiredService<ICommandHandler<ResolveEmailDispatchWithoutReplayCommand, BaseCommandResponse<Guid>>>()
            .ExecuteAsync(new() { TenantId = PlatformDefaults.DefaultTenantId, OutboxId = id, Reason = "" }, default);
        foreach (var response in new[] { park, resolve })
        {
            await Assert.That(response.IsSuccess).IsFalse();
            await Assert.That(response.Errors).IsNotEmpty();
            await Assert.That(response.FailureCode).IsNull();
        }
        var reconcile = await services.GetRequiredService<ICommandHandler<ReconcileUnknownEmailDispatchCommand, BaseCommandResponse<Guid>>>()
            .ExecuteAsync(new()
            {
                TenantId = PlatformDefaults.DefaultTenantId,
                OutboxId = id,
                Reason = "reviewed",
                Outcome = (EmailDispatchUnknownReconciliationOutcome)int.MaxValue
            }, default);
        var pause = await services.GetRequiredService<ICommandHandler<SetEmailDispatchTenantPauseStateCommand, BaseCommandResponse<Guid>>>()
            .ExecuteAsync(new() { TenantId = PlatformDefaults.DefaultTenantId, IsPaused = true, PauseReason = new string('x', 501) }, default);
        await Assert.That(reconcile.FailureCode).IsEqualTo(EmailDispatchFailureCodes.ValidationFailed);
        await Assert.That(pause.FailureCode).IsEqualTo(EmailDispatchFailureCodes.ValidationFailed);
        await Assert.That(await services.GetRequiredService<IEmailDispatchOutboxRepository>()
            .GetTenantControl(PlatformDefaults.DefaultTenantId, default)).IsNull();
        await Assert.That((await StatusAsync(factory)).Single()).IsEqualTo(before);
    }

    private static string ClosureUrl(string operation, Guid id, Guid? tenant = null) =>
        Root + $"/tenants/{tenant ?? PlatformDefaults.DefaultTenantId}/outbox/{id}/{operation}?reason=%20reviewed%20";
}
