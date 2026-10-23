using System.Runtime.InteropServices;
using System.Security.Cryptography;
using System.Text.Json;
using Explore.Application;
using Explore.Application.Contracts.Moderation;
using Explore.Application.Contracts.Operations;
using Explore.Application.Features.Moderation.Handlers.Commands;
using Explore.Application.Features.Moderation.Requests.Commands;
using Microsoft.Extensions.DependencyInjection;

namespace Event.Application.UnitTests.Moderation;

public sealed class SubmitModerationAllegationCommandTests
{
    [Test]
    public async Task Submission_names_only_exact_intake_intent_and_preserves_opaque_token_bytes()
    {
        var command = Command();
        var result = await new SubmitModerationAllegationCommandValidator().ValidateAsync(command);
        await Assert.That(result.IsValid).IsTrue();
        var request = command.ToRequest();
        await Assert.That(request.Purpose).IsEqualTo(ModerationCaseAccessPurpose.SubmitAllegation);
        await Assert.That(request.OperationId).IsEqualTo(command.OperationId);
        await Assert.That(request.CaseId).IsEqualTo(Guid.Empty);
        await Assert.That(request.ExpectedCaseRevision).IsEqualTo(0L);
        await Assert.That(request.ReceiptId).IsEqualTo(command.ReceiptId);
        await Assert.That(request.PresentedToken.ToArray()).IsEquivalentTo(command.PresentedToken.ToArray());
        await Assert.That(request.Narrative.ToArray()).IsEquivalentTo(command.Narrative.ToArray());
    }

    [Test]
    [Arguments(0)]
    [Arguments(1)]
    public async Task Operation_and_receipt_identity_require_uuidv7(int identity)
    {
        var command = Command();
        var invalid = Guid.NewGuid();
        command = identity switch
        {
            0 => command with { OperationId = invalid },
            _ => command with { ReceiptId = invalid }
        };
        await Assert.That((await new SubmitModerationAllegationCommandValidator().ValidateAsync(command)).IsValid).IsFalse();
    }

    [Test]
    public async Task Submission_serialization_carries_no_canonical_case_coordinates()
    {
        var serialized = JsonSerializer.SerializeToElement(Command());
        await Assert.That(serialized.TryGetProperty("CaseId", out _)).IsFalse();
        await Assert.That(serialized.TryGetProperty("ExpectedCaseRevision", out _)).IsFalse();
    }

    [Test]
    [Arguments(0)]
    [Arguments(31)]
    [Arguments(33)]
    public async Task Token_requires_exact_native_digest_input_width(int length)
    {
        var command = Command() with { PresentedToken = RandomNumberGenerator.GetBytes(length) };
        await Assert.That((await new SubmitModerationAllegationCommandValidator().ValidateAsync(command)).IsValid).IsFalse();
    }

    [Test]
    public async Task Token_snapshot_cannot_change_after_validation_or_through_exposed_memory()
    {
        var bytes = RandomNumberGenerator.GetBytes(32);
        var expected = bytes.ToArray();
        var command = Command() with { PresentedToken = bytes };
        var copy = command with { OperationId = Guid.CreateVersion7() };
        var request = command.ToRequest();
        CryptographicOperations.ZeroMemory(bytes);
        await Assert.That(command.PresentedToken.ToArray()).IsEquivalentTo(expected);
        await Assert.That(copy.PresentedToken.ToArray()).IsEquivalentTo(expected);
        await Assert.That(request.PresentedToken.ToArray()).IsEquivalentTo(expected);
        if (MemoryMarshal.TryGetArray(command.PresentedToken, out var exposed))
            CryptographicOperations.ZeroMemory(exposed.Array!);
        await Assert.That(command.PresentedToken.ToArray()).IsEquivalentTo(expected);
        await Assert.That(copy.PresentedToken.ToArray()).IsEquivalentTo(expected);
    }

    [Test]
    public async Task Serializer_round_trip_preserves_base64_token_and_exact_intake_coordinates()
    {
        var command = Command();
        var restored = JsonSerializer.Deserialize<SubmitModerationAllegationCommand>(JsonSerializer.Serialize(command));
        await Assert.That(restored).IsNotNull();
        await Assert.That(restored!.OperationId).IsEqualTo(command.OperationId);
        await Assert.That(restored.ReceiptId).IsEqualTo(command.ReceiptId);
        await Assert.That(restored.PresentedToken.ToArray()).IsEquivalentTo(command.PresentedToken.ToArray());
        await Assert.That(restored.Narrative.ToArray()).IsEquivalentTo(command.Narrative.ToArray());
    }

    [Test]
    public async Task Production_native_discovery_registers_one_scoped_confidential_intake_handler()
    {
        var services = new ServiceCollection();
        services.AddNativeOperations();
        var contract = typeof(ICommandHandler<SubmitModerationAllegationCommand, ModerationAllegationResponse>);
        await Assert.That(services.Count(descriptor => descriptor.ServiceType == contract)).IsEqualTo(1);
        await Assert.That(services.Single(descriptor => descriptor.ServiceType == contract).Lifetime)
            .IsEqualTo(ServiceLifetime.Scoped);
    }

    private static SubmitModerationAllegationCommand Command() => new()
    {
        OperationId = Guid.CreateVersion7(),
        ReceiptId = Guid.CreateVersion7(),
        PresentedToken = RandomNumberGenerator.GetBytes(32),
        Narrative = new byte[] { 1, 2, 3 }
    };
}
