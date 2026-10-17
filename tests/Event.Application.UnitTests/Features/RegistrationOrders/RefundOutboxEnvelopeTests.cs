using System.Text.Json;
using Explore.Application.Services.Registration;
using Explore.Domain;

namespace Event.Application.UnitTests.Features.RegistrationOrders;

public sealed class RefundOutboxEnvelopeTests
{
    public static IEnumerable<(string Route, string Changed)> Cases()
    {
        string[] routes = [RefundOutboxMessageFactory.CampaignProcessRequested,
            RefundOutboxMessageFactory.DispatchRequested, RefundOutboxMessageFactory.ReconciliationRequested,
            RefundOutboxMessageFactory.PaymentCancellationRequested];
        foreach (string route in routes)
            foreach (string changed in new[] { "none", "id", "type", "route", "tenant", "source" })
                yield return (route, changed);
        yield return (RefundOutboxMessageFactory.PaymentCancellationRequested, "campaign");
    }

    [Test]
    [MethodDataSource(nameof(Cases))]
    public async Task FinancialTriggerRequiresOriginalTypedEnvelope((string Route, string Changed) input)
    {
        Guid tenantId = Guid.CreateVersion7();
        Guid sourceId = Guid.CreateVersion7();
        Guid campaignId = Guid.CreateVersion7();
        Guid payloadTenant = input.Changed == "tenant" ? Guid.Empty : tenantId;
        Guid payloadSource = input.Changed == "source" ? Guid.Empty : sourceId;
        bool campaign = input.Route == RefundOutboxMessageFactory.CampaignProcessRequested;
        bool payment = input.Route == RefundOutboxMessageFactory.PaymentCancellationRequested;
        object payload = campaign ? new RefundCampaignProcessPayload(payloadTenant, payloadSource)
            : payment ? new PaymentCancellationProcessPayload(payloadTenant,
                input.Changed == "campaign" ? Guid.Empty : campaignId, payloadSource)
            : new RefundAttemptProcessPayload(payloadTenant, payloadSource);
        var message = new OutboxMessage
        {
            Id = Guid.CreateVersion7(),
            EventType = input.Changed == "route" ? "unrelated" : input.Route,
            AggregateType = input.Changed == "type" ? "Other"
                : campaign ? nameof(RefundCampaign) : payment ? nameof(PaymentAttempt) : nameof(RefundAttempt),
            AggregateId = input.Changed == "id" ? Guid.CreateVersion7() : sourceId,
            Payload = JsonSerializer.Serialize(payload),
            CreatedAt = DateTime.UtcNow
        };
        object Read() => campaign ? RefundOutboxMessageFactory.ReadCampaign(message)
            : payment ? RefundOutboxMessageFactory.ReadPaymentCancellation(message)
            : RefundOutboxMessageFactory.ReadAttempt(message);
        if (input.Changed != "none")
        {
            await Assert.That(Read).Throws<InvalidOperationException>();
            return;
        }
        var result = Read();
        Guid actualTenant = result switch
        {
            RefundCampaignProcessPayload value => value.TenantId,
            PaymentCancellationProcessPayload value => value.TenantId,
            RefundAttemptProcessPayload value => value.TenantId,
            _ => throw new InvalidOperationException("Unexpected financial trigger.")
        };
        Guid actualSource = result switch
        {
            RefundCampaignProcessPayload value => value.CampaignId,
            PaymentCancellationProcessPayload value => value.PaymentAttemptId,
            RefundAttemptProcessPayload value => value.RefundAttemptId,
            _ => throw new InvalidOperationException("Unexpected financial trigger.")
        };
        await Assert.That(actualTenant).IsEqualTo(tenantId);
        await Assert.That(actualSource).IsEqualTo(sourceId);
    }
}
