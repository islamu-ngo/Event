using System.Runtime.InteropServices;
using System.Security.Cryptography;
using System.Text.Json;
using Explore.Application;
using Explore.Application.Contracts.Operations;
using Explore.Application.Features.Moderation.Handlers.Commands;
using Explore.Application.Features.Moderation.Requests.Commands;
using Explore.Domain;
using ISLAMU.Moderation.Domain;
using Microsoft.Extensions.DependencyInjection;

namespace Event.Application.UnitTests.Moderation;

public sealed class IssueModerationSubmissionReceiptCommandTests
{
    [Test]
    [Arguments(ModerationCaseTargetKind.EventVersion)]
    [Arguments(ModerationCaseTargetKind.ParticipantIncident)]
    [Arguments(ModerationCaseTargetKind.Actor)]
    [Arguments(ModerationCaseTargetKind.Service)]
    [Arguments(ModerationCaseTargetKind.Resource)]
    [Arguments(ModerationCaseTargetKind.Form)]
    [Arguments(ModerationCaseTargetKind.Destination)]
    [Arguments(ModerationCaseTargetKind.HiddenContentReceipt)]
    [Arguments(ModerationCaseTargetKind.LocalIncident)]
    public async Task Defined_target_lookup_preserves_existing_native_identities(
        ModerationCaseTargetKind kind)
    {
        var command = Command() with { Kind = kind };
        await Assert.That((await new IssueModerationSubmissionReceiptCommandValidator()
            .ValidateAsync(command)).IsValid).IsTrue();
        var request = command.ToRequest();
        await Assert.That(request.Kind).IsEqualTo(kind);
        await Assert.That(request.NativeKind).IsEqualTo(command.NativeKind);
        await Assert.That(request.NativeTargetId).IsEqualTo(command.NativeTargetId);
        await Assert.That(request.EventId).IsEqualTo(command.EventId);
        await Assert.That(request.RelationshipSourceId).IsEqualTo(command.RelationshipSourceId);
        await Assert.That(request.RelationshipKind).IsEqualTo(command.RelationshipKind);
    }

    [Test]
    [Arguments(0)]
    [Arguments(1)]
    [Arguments(2)]
    [Arguments(3)]
    [Arguments(4)]
    public async Task Undefined_or_read_only_target_and_relationship_lookup_is_rejected(int defect)
    {
        var command = Command();
        command = defect switch
        {
            0 => command with { Kind = (ModerationCaseTargetKind)0 },
            1 => command with { NativeKind = ModerationCaseTargetKind.HiddenContentReceipt },
            2 => command with { NativeKind = ModerationCaseTargetKind.LocalIncident },
            3 => command with { RelationshipKind = (ModerationCaseRelationshipKind)0 },
            _ => command with { RelationshipKind = ModerationCaseRelationshipKind.CaseReadGrant }
        };
        await Assert.That((await new IssueModerationSubmissionReceiptCommandValidator()
            .ValidateAsync(command)).IsValid).IsFalse();
    }

    [Test]
    [Arguments(0)]
    [Arguments(1)]
    [Arguments(2)]
    public async Task Empty_native_lookup_identity_is_rejected(int identity)
    {
        var command = Command();
        command = identity switch
        {
            0 => command with { NativeTargetId = Guid.Empty },
            1 => command with { EventId = Guid.Empty },
            _ => command with { RelationshipSourceId = Guid.Empty }
        };
        await Assert.That((await new IssueModerationSubmissionReceiptCommandValidator()
            .ValidateAsync(command)).IsValid).IsFalse();
    }

    [Test]
    [Arguments(true, 0, false)]
    [Arguments(true, 1, true)]
    [Arguments(true, 65536, true)]
    [Arguments(true, 65537, false)]
    [Arguments(false, 0, false)]
    [Arguments(false, 1, true)]
    [Arguments(false, 65536, true)]
    [Arguments(false, 65537, false)]
    public async Task Both_material_buffers_have_exact_native_custody_bounds(
        bool occurrence, int length, bool valid)
    {
        var command = Command();
        var material = RandomNumberGenerator.GetBytes(length);
        command = occurrence ? command with { OccurrenceMaterial = material } : command with { Material = material };
        await Assert.That((await new IssueModerationSubmissionReceiptCommandValidator()
            .ValidateAsync(command)).IsValid).IsEqualTo(valid);
    }

    [Test]
    public async Task Caller_and_exposed_buffer_mutation_cannot_replace_retained_occurrence_or_material()
    {
        var occurrence = RandomNumberGenerator.GetBytes(32);
        var material = RandomNumberGenerator.GetBytes(32);
        var expectedOccurrence = occurrence.ToArray();
        var expectedMaterial = material.ToArray();
        var command = Command() with { OccurrenceMaterial = occurrence, Material = material };
        var copy = command with { EventId = null };
        var request = command.ToRequest();
        CryptographicOperations.ZeroMemory(occurrence);
        CryptographicOperations.ZeroMemory(material);
        if (MemoryMarshal.TryGetArray(command.OccurrenceMaterial, out var exposedOccurrence))
            CryptographicOperations.ZeroMemory(exposedOccurrence.Array!);
        if (MemoryMarshal.TryGetArray(command.Material, out var exposedMaterial))
            CryptographicOperations.ZeroMemory(exposedMaterial.Array!);
        await Assert.That(command.OccurrenceMaterial.ToArray()).IsEquivalentTo(expectedOccurrence);
        await Assert.That(command.Material.ToArray()).IsEquivalentTo(expectedMaterial);
        await Assert.That(copy.OccurrenceMaterial.ToArray()).IsEquivalentTo(expectedOccurrence);
        await Assert.That(copy.Material.ToArray()).IsEquivalentTo(expectedMaterial);
        await Assert.That(request.OccurrenceMaterial.ToArray()).IsEquivalentTo(expectedOccurrence);
        await Assert.That(request.Material.ToArray()).IsEquivalentTo(expectedMaterial);
    }

    [Test]
    public async Task Serializer_round_trip_preserves_base64_material_and_untrusted_lookup_coordinates()
    {
        var command = Command();
        var restored = JsonSerializer.Deserialize<IssueModerationSubmissionReceiptCommand>(
            JsonSerializer.Serialize(command));
        await Assert.That(restored).IsNotNull();
        await Assert.That(restored!.Kind).IsEqualTo(command.Kind);
        await Assert.That(restored.NativeKind).IsEqualTo(command.NativeKind);
        await Assert.That(restored.EventId).IsEqualTo(command.EventId);
        await Assert.That(restored.NativeTargetId).IsEqualTo(command.NativeTargetId);
        await Assert.That(restored.RelationshipKind).IsEqualTo(command.RelationshipKind);
        await Assert.That(restored.RelationshipSourceId).IsEqualTo(command.RelationshipSourceId);
        await Assert.That(restored.OccurrenceMaterial.ToArray()).IsEquivalentTo(command.OccurrenceMaterial.ToArray());
        await Assert.That(restored.Material.ToArray()).IsEquivalentTo(command.Material.ToArray());
    }

    [Test]
    public async Task Production_native_discovery_registers_one_scoped_receipt_issuance_handler()
    {
        var services = new ServiceCollection();
        services.AddNativeOperations();
        var contract = typeof(ICommandHandler<IssueModerationSubmissionReceiptCommand, ModerationSubmissionReceiptResponse>);
        await Assert.That(services.Count(descriptor => descriptor.ServiceType == contract)).IsEqualTo(1);
        await Assert.That(services.Single(descriptor => descriptor.ServiceType == contract).Lifetime)
            .IsEqualTo(ServiceLifetime.Scoped);
    }

    private static IssueModerationSubmissionReceiptCommand Command()
    {
        var targetId = Guid.NewGuid();
        return new()
        {
            Kind = ModerationCaseTargetKind.EventVersion, NativeKind = ModerationCaseTargetKind.EventVersion,
            EventId = targetId, NativeTargetId = targetId,
            RelationshipKind = ModerationCaseRelationshipKind.PublicEvent, RelationshipSourceId = targetId,
            OccurrenceMaterial = RandomNumberGenerator.GetBytes(32), Material = RandomNumberGenerator.GetBytes(32)
        };
    }
}
