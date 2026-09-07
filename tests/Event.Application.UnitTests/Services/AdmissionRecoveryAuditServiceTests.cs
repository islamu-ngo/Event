using Explore.Application.Contracts.Admissions;
using Explore.Application.Contracts.Persistence;
using Explore.Application.Services.Registration;
using Explore.Domain;
using NSubstitute;

namespace Event.Application.UnitTests.Services;

public sealed class AdmissionRecoveryAuditServiceTests
{
    [Test]
    public async Task AppendPersistsOnlyRecoveryLineageActionGenerationAndTime()
    {
        IAuditLogRepository repository = Substitute.For<IAuditLogRepository>();
        AuditLog? captured = null;
        repository.Create(Arg.Any<AuditLog>())
            .Returns(call =>
            {
                captured = call.Arg<AuditLog>();
                return Task.FromResult(captured!);
            });
        var service = new AdmissionRecoveryAuditService(repository);
        var fact = new AdmissionRecoveryAuditFact(
            Guid.Parse("018e4e5c-7f00-7000-8000-000000000461"),
            Guid.Parse("018e4e5c-7f00-7000-8000-000000000462"),
            "AdmissionRecoveryRotated",
            2,
            new DateTimeOffset(2026, 8, 25, 12, 0, 0, TimeSpan.Zero));

        await service.AppendAsync(fact, CancellationToken.None);

        await Assert.That(captured).IsNotNull();
        await Assert.That(captured!.EntityType).IsEqualTo(nameof(AdmissionRecoveryCapability));
        await Assert.That(captured.EntityId).IsEqualTo(fact.RecoveryRequestId.ToString("D"));
        await Assert.That(captured.Action).IsEqualTo(fact.ActionCode);
        await Assert.That(captured.NewValues).IsEqualTo("{\"CapabilityVersion\":2}");
        await Assert.That(captured.ActorId).IsNull();
        string serialized = string.Join(
            '|',
            captured.EntityType,
            captured.EntityId,
            captured.Action,
            captured.NewValues,
            captured.AffectedColumns);
        await Assert.That(serialized).DoesNotContain("Email");
        await Assert.That(serialized).DoesNotContain("Recipient");
        await Assert.That(serialized).DoesNotContain("Capability\":\"");
        await Assert.That(serialized).DoesNotContain("Digest");
        await Assert.That(serialized).DoesNotContain("Credential");
    }
}
