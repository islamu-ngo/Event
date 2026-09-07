using Explore.Application.Contracts.Persistence;
using Explore.Application.Features.EmailDispatch.Handlers.Queries;
using Explore.Application.Features.EmailDispatch.Requests.Queries;
using Explore.Domain;
using NSubstitute;

namespace ApplicationUnitTests.Features.EmailDispatch.Queries;

public sealed class GetEmailDispatchStatusQueryHandlerTests
{
    private readonly IEmailDispatchOutboxRepository _repository = Substitute.For<IEmailDispatchOutboxRepository>();

    [Test]
    public async Task HandleWhenTenantIdMissingReturnsValidationFailure()
    {
        var result = await CreateHandler().Handle(
            new GetEmailDispatchStatusQuery { TenantId = Guid.Empty },
            CancellationToken.None);

        await Assert.That(result.IsSuccess).IsFalse();
        await Assert.That(result.Errors).IsNotNull();
        await Assert.That(result.Errors![0]).IsEqualTo("TenantId is required.");
    }

    [Test]
    public async Task HandleWhenLimitOutOfRangeReturnsValidationFailure()
    {
        var result = await CreateHandler().Handle(
            new GetEmailDispatchStatusQuery { TenantId = Guid.NewGuid(), Limit = 201 },
            CancellationToken.None);

        await Assert.That(result.IsSuccess).IsFalse();
        await Assert.That(result.Errors).IsNotNull();
        await Assert.That(result.Errors![0]).IsEqualTo("Limit must be between 1 and 200.");
    }

    [Test]
    public async Task HandleWhenRowsExistReturnsSafeOperationalFieldsOnly()
    {
        var tenantId = Guid.NewGuid();
        var outboxId = Guid.NewGuid();
        var sourceId = Guid.NewGuid();
        var deliveredAt = DateTime.UtcNow;
        var contentRedactedAt = deliveredAt.AddDays(180);

        _repository.GetStatusRows(tenantId, 50, Arg.Any<CancellationToken>())
            .Returns([
                new EmailDispatchOutbox
                {
                    Id = outboxId,
                    TenantId = tenantId,
                    SourceType = "registration_order",
                    SourceId = sourceId,
                    Kind = EmailDispatchKind.RegistrationConfirmation,
                    RecipientEmail = "registrant@example.test",
                    Subject = "Sensitive subject",
                    PlainTextBody = "Sensitive plain text body",
                    HtmlBody = "<p>Sensitive HTML body</p>",
                    ReplyTo = "reply@example.test",
                    ProviderMessageId = "provider-secret-message-id",
                    LastError = "raw provider error with token=secret",
                    Status = EmailDispatchStatus.Sent,
                    AttemptCount = 2,
                    SentAt = deliveredAt,
                    ContentRedactedAt = contentRedactedAt,
                    CorrelationId = "registration-correlation"
                }
            ]);

        var result = await CreateHandler().Handle(
            new GetEmailDispatchStatusQuery { TenantId = tenantId, Limit = 50 },
            CancellationToken.None);

        await Assert.That(result.IsSuccess).IsTrue();
        await Assert.That(result.Id).IsNotNull();
        await Assert.That(result.Id).Count().IsEqualTo(1);

        var dto = result.Id![0];
        await Assert.That(dto.OutboxId).IsEqualTo(outboxId);
        await Assert.That(dto.TenantId).IsEqualTo(tenantId);
        await Assert.That(dto.SourceType).IsEqualTo("registration_order");
        await Assert.That(dto.SourceId).IsEqualTo(sourceId);
        await Assert.That(dto.DeliveryStatus).IsEqualTo(nameof(EmailDispatchStatus.Sent));
        await Assert.That(dto.AttemptCount).IsEqualTo(2);
        await Assert.That(dto.DeliveredAt).IsEqualTo(deliveredAt);
        await Assert.That(dto.ContentRedactedAt).IsEqualTo(contentRedactedAt);
        await Assert.That(dto.CorrelationId).IsEqualTo("registration-correlation");

        var dtoPropertyNames = typeof(Explore.Application.DTOs.EmailDispatch.EmailDispatchStatusDto)
            .GetProperties()
            .Select(property => property.Name)
            .ToHashSet(StringComparer.OrdinalIgnoreCase);

        await Assert.That(dtoPropertyNames).DoesNotContain("RecipientEmail");
        await Assert.That(dtoPropertyNames).DoesNotContain("Subject");
        await Assert.That(dtoPropertyNames).DoesNotContain("PlainTextBody");
        await Assert.That(dtoPropertyNames).DoesNotContain("HtmlBody");
        await Assert.That(dtoPropertyNames).DoesNotContain("ReplyTo");
        await Assert.That(dtoPropertyNames).DoesNotContain("ProviderMessageId");
        await Assert.That(dtoPropertyNames).DoesNotContain("LastError");
    }

    private GetEmailDispatchStatusQueryHandler CreateHandler() => new(_repository);
}
