using System.Text.Json;
using Explore.Application;
using Explore.Application.Contracts.Operations;
using Explore.Application.Features.Moderation.RecognitionRecovery;
using Explore.Application.Responses;
using Explore.Domain;
using Microsoft.Extensions.DependencyInjection;

namespace Event.Application.UnitTests.Moderation;

public sealed class RecognitionRecoveryContractTests
{
    [Test]
    public async Task Json_cannot_supply_authenticated_actor_or_route_owned_scope()
    {
        var command = Assessment();
        string serialized = JsonSerializer.Serialize(command);
        using var original = JsonDocument.Parse(serialized);
        await Assert.That(original.RootElement.TryGetProperty("IntentId", out _)).IsFalse();
        await Assert.That(original.RootElement.TryGetProperty("SourceId", out _)).IsFalse();
        string forged = serialized[..^1] + $",\"IntentId\":\"{Guid.CreateVersion7():D}\",\"SourceId\":\"{Guid.CreateVersion7():D}\",\"AuthenticatedHuman\":\"{Guid.CreateVersion7():D}\"}}";
        var restored = JsonSerializer.Deserialize<AssessRecognitionRecoveryCommand>(forged)!;
        await Assert.That(restored.IntentId).IsEqualTo(Guid.Empty);
        await Assert.That(restored.SourceId).IsEqualTo(Guid.Empty);
        await Assert.That((await new AssessRecognitionRecoveryCommandValidator().ValidateAsync(restored)).IsValid).IsFalse();
    }

    [Test]
    [Arguments(0L)]
    [Arguments(long.MaxValue)]
    public async Task Invalid_revision_cannot_reach_recovery_write(long revision)
    {
        var validation = await new AssessRecognitionRecoveryCommandValidator().ValidateAsync(
            Assessment() with { ExpectedPendingRevision = revision });
        await Assert.That(validation.IsValid).IsFalse();
    }

    [Test]
    public async Task Native_registration_discovers_each_recovery_operation_once()
    {
        var services = new ServiceCollection();
        services.AddNativeOperations();
        Type[] contracts =
        [
            typeof(ICommandHandler<AssessRecognitionRecoveryCommand, BaseCommandResponse<Guid>>),
            typeof(ICommandHandler<AuthorizeRecognitionRecoveryCommand, BaseCommandResponse<Guid>>),
            typeof(ICommandHandler<AcknowledgeRecognitionRecoveryCommand, BaseCommandResponse<Guid>>),
            typeof(IQueryHandler<GetRecognitionRecoveryContextQuery, RecognitionRecoveryReadResponse<RecognitionRecoveryContextDto>>),
            typeof(IQueryHandler<GetRecognitionRecoveryEscalationQuery, RecognitionRecoveryReadResponse<RecognitionRecoveryEscalationDto>>),
            typeof(IQueryHandler<GetRecognitionRecoveryEscalationsQuery, RecognitionRecoveryReadResponse<IReadOnlyList<RecognitionRecoveryEscalationDto>>>),
            typeof(IQueryHandler<GetRecognitionCorrectionProofsQuery, RecognitionRecoveryReadResponse<IReadOnlyList<RecognitionCorrectionProofDto>>>)
        ];
        foreach (var contract in contracts)
        {
            await Assert.That(services.Count(descriptor => descriptor.ServiceType == contract)).IsEqualTo(1);
            await Assert.That(services.Single(descriptor => descriptor.ServiceType == contract).Lifetime).IsEqualTo(ServiceLifetime.Scoped);
        }
    }

    private static AssessRecognitionRecoveryCommand Assessment() => new()
    {
        OperationId = Guid.CreateVersion7(),
        IntentId = Guid.CreateVersion7(),
        SourceId = Guid.CreateVersion7(),
        AdoptionId = Guid.CreateVersion7(),
        ExpectedPendingRevision = 2,
        ExpectedSourceRevision = 1,
        ExpectedSourceGeneration = 3,
        ExpectedAdoptionRevision = 1,
        ExpectedAdoptionGeneration = 4,
        Grounds = ModerationRecognitionDispositionGrounds.OriginalKeyRecoveryUnavailable,
        ValidUntil = new DateTimeOffset(2026, 10, 11, 12, 5, 0, TimeSpan.Zero)
    };
}
