using Explore.Domain;
using Explore.Domain.Enums;

namespace Event.Domain.UnitTests;

public sealed class OrganizerPaymentProviderAccountOperationTests
{
    private static readonly Guid TenantId = Guid.Parse("018e4e5c-7f00-7000-8000-000000000001");
    private static readonly Guid OrganizerActorId = Guid.Parse("018e4e5c-7f00-7000-8000-000000000111");
    private static readonly Guid OperationId = Guid.Parse("018e4e5c-7f00-7000-8000-000000000222");
    private static readonly DateTime Now = new(2026, 8, 14, 12, 0, 0, DateTimeKind.Utc);

    [Test]
    public async Task CreateRequested_NormalizesScopeAndDerivesProviderIdempotencyKeyFromOperationId()
    {
        OrganizerPaymentProviderAccountOperation operation = Operation();

        await Assert.That(operation.Id).IsEqualTo(OperationId);
        await Assert.That(operation.ProviderCode).IsEqualTo("stripe");
        await Assert.That(operation.ConnectPlatformId).IsEqualTo("platform-live-eu");
        await Assert.That(operation.ProviderIdempotencyKey).IsEqualTo($"organizer-payment-account-{OperationId:N}");
        await Assert.That(operation.ActiveScopeKey).IsEqualTo($"{TenantId:N}|{OrganizerActorId:N}|stripe|platform-live-eu");
        await Assert.That(operation.ActiveUniquenessSlot).IsEqualTo("active");
        await Assert.That(operation.StatusId).IsEqualTo((int)OrganizerPaymentProviderAccountOperationStatus.ProviderCreateRequested);
        await Assert.That(operation.IsUnresolved).IsTrue();
    }

    [Test]
    public async Task ManualReconciliation_RemainsActiveAndBlocksRetry()
    {
        OrganizerPaymentProviderAccountOperation operation = Operation();

        operation.MarkManualReconciliationRequired("network", "req_123", Now.AddMinutes(1));

        await Assert.That(operation.StatusId).IsEqualTo((int)OrganizerPaymentProviderAccountOperationStatus.ManualReconciliationRequired);
        await Assert.That(operation.ActiveUniquenessSlot).IsEqualTo("active");
        await Assert.That(operation.FailureCode).IsEqualTo("network");
        await Assert.That(operation.ProviderRequestId).IsEqualTo("req_123");
        await Assert.That(operation.IsUnresolved).IsTrue();
    }

    [Test]
    public async Task BindAndReject_AreTerminalAndCannotReactivate()
    {
        OrganizerPaymentProviderAccountOperation bound = Operation();
        OrganizerPaymentProviderAccountOperation rejected = OrganizerPaymentProviderAccountOperation.CreateRequested(Guid.CreateVersion7(), TenantId, OrganizerActorId, "stripe", "platform-live-eu", Now);

        bound.BindToConnection(Guid.Parse("018e4e5c-7f00-7000-8000-000000000333"), "acct_123", Now.AddMinutes(1));
        rejected.RejectByProvider("account_invalid", "req_bad", Now.AddMinutes(1));

        await Assert.That(bound.StatusId).IsEqualTo((int)OrganizerPaymentProviderAccountOperationStatus.BoundToConnection);
        await Assert.That(bound.ActiveUniquenessSlot).IsEqualTo($"boundtoconnection:{bound.Id:N}");
        await Assert.That(rejected.StatusId).IsEqualTo((int)OrganizerPaymentProviderAccountOperationStatus.ProviderRejected);
        await Assert.That(rejected.ActiveUniquenessSlot).IsEqualTo($"providerrejected:{rejected.Id:N}");
        await Assert.That(() => bound.MarkManualReconciliationRequired("late", null, Now.AddMinutes(2))).Throws<InvalidOperationException>();
        await Assert.That(() => rejected.BindToConnection(Guid.CreateVersion7(), "acct_late", Now.AddMinutes(2))).Throws<InvalidOperationException>();
    }

    [Test]
    public async Task ConfirmNoProviderAccount_IsTerminalAndRequiresReason()
    {
        OrganizerPaymentProviderAccountOperation operation = Operation();

        operation.ConfirmNoProviderAccount("operator_verified_absent", Now.AddMinutes(1));

        await Assert.That(operation.StatusId).IsEqualTo((int)OrganizerPaymentProviderAccountOperationStatus.NoProviderAccountConfirmed);
        await Assert.That(operation.ActiveUniquenessSlot).IsEqualTo($"noprovideraccountconfirmed:{operation.Id:N}");
        await Assert.That(operation.ResolutionReason).IsEqualTo("operator_verified_absent");
        await Assert.That(() => operation.RejectByProvider("late", null, Now.AddMinutes(2))).Throws<InvalidOperationException>();
    }

    private static OrganizerPaymentProviderAccountOperation Operation() =>
        OrganizerPaymentProviderAccountOperation.CreateRequested(
            OperationId,
            TenantId,
            OrganizerActorId,
            " STRIPE ",
            " platform-live-eu ",
            Now);
}
