using System.Text.Json;
using Explore.Application;
using Explore.Application.Contracts.Operations;
using Explore.Application.Contracts.Persistence;
using Explore.Application.Features.Moderation.Handlers.Commands;
using Explore.Application.Features.Moderation.Requests.Commands;
using Explore.Application.Operations;
using Explore.Domain;
using ISLAMU.Moderation.Domain;
using Microsoft.Extensions.DependencyInjection;

namespace Event.Application.UnitTests.Moderation;

public sealed class ModerationProfileCollectionProcedureCommandTests
{
    private static readonly AuthorityScope Target = AuthorityScope.Tenant(
        AuthorityScope.Instance(AuthorityScope.Solution(AuthorityScope.TrustDomain(Id(30)), Id(31)), Id(32)), Id(33));
    private static readonly DateTimeOffset Until = new(2026, 10, 10, 12, 0, 0, TimeSpan.Zero);
    private static readonly ModerationProfileCollectionProcedure Procedure = new(
        ModerationIndividualPurpose.ProfileCompletion,
        ModerationProfileCollectionApplicability.NativeIndependentlyAcceptedAssessment,
        [ModerationProfilePreliminaryFieldKind.ApplicabilityDeclaration, ModerationProfilePreliminaryFieldKind.SubjectConsentReference],
        [ModerationProfileNormalFieldKind.DateOfBirth, ModerationProfileNormalFieldKind.FirstName],
        "Independent native guardian relationship review.", "Accountable manual issuer review.");

    [Test]
    [Arguments(0)]
    [Arguments(1)]
    [Arguments(2)]
    public async Task Typed_operations_preserve_every_original_coordinate_and_procedure(int variant)
    {
        var command = Create(variant);
        await Assert.That((await Validate(command)).IsValid).IsTrue();
        ModerationQualificationMutation expected = variant switch
        {
            0 => new RecordModerationProfileCollectionProcedureSource
            {
                OperationId = Id(1),
                Target = Target,
                ExpectedQualificationRevision = 13,
                SourceId = Id(2),
                ChildAgeSourceId = Id(3),
                ExpectedChildAgeSourceRevision = 1,
                ExpectedChildAgeSourceWriterGeneration = 5,
                ChildAgeAdoptionId = Id(4),
                ExpectedChildAgeAdoptionRevision = 1,
                ExpectedChildAgeAdoptionWriterGeneration = 7,
                Procedure = Procedure,
                ValidUntil = Until
            },
            1 => new AdoptModerationProfileCollectionProcedureSource
            {
                OperationId = Id(1),
                Target = Target,
                ExpectedQualificationRevision = 13,
                SourceId = Id(2),
                ExpectedSourceRevision = 1,
                ExpectedSourceWriterGeneration = 11,
                AdoptionId = Id(5),
                ValidUntil = Until
            },
            _ => new WithdrawModerationProfileCollectionProcedureAdoption
            {
                OperationId = Id(1),
                Target = Target,
                ExpectedQualificationRevision = 13,
                AdoptionId = Id(5),
                ExpectedAdoptionRevision = 1
            }
        };
        await Assert.That(command.ToMutation()).IsEqualTo(expected);
    }

    [Test]
    [Arguments(0)]
    [Arguments(1)]
    [Arguments(2)]
    public async Task Malformed_common_intent_returns_validation_before_native_repository_access(int variant)
    {
        var command = Create(variant);
        var invalidId = Guid.Parse("019a0000-0000-7000-7000-000000000001");
        ModerationQualificationCommand[] invalid =
        [
            command with { OperationId = Guid.Empty },
            command with { OperationId = Guid.Parse("01234567-89ab-4cde-8fab-0123456789ab") },
            command with { OperationId = invalidId },
            command with { Target = null! },
            command with { Target = AuthorityScope.TrustDomain(Id(30)) },
            command with { ExpectedQualificationRevision = 0 },
            command with { ExpectedQualificationRevision = -1 },
            command with { ExpectedQualificationRevision = long.MaxValue - 1 },
            command with { ExpectedQualificationRevision = long.MaxValue }
        ];
        foreach (var request in invalid)
            await Reject(request);
    }

    [Test]
    public async Task Recording_rejects_nonoriginal_parents_aliases_missing_procedure_and_invalid_generations()
    {
        var command = (RecordModerationProfileCollectionProcedureCommand)Create(0);
        RecordModerationProfileCollectionProcedureCommand[] invalid =
        [
            command with { SourceId = Guid.Empty },
            command with { ChildAgeSourceId = Guid.Parse("01234567-89ab-4cde-8fab-0123456789ab") },
            command with { ChildAgeAdoptionId = Guid.Parse("019a0000-0000-7000-7000-000000000001") },
            command with { SourceId = command.ChildAgeSourceId },
            command with { SourceId = command.ChildAgeAdoptionId },
            command with { ChildAgeAdoptionId = command.ChildAgeSourceId },
            command with { ExpectedChildAgeSourceRevision = 0 },
            command with { ExpectedChildAgeSourceRevision = 2 },
            command with { ExpectedChildAgeAdoptionRevision = 0 },
            command with { ExpectedChildAgeAdoptionRevision = 2 },
            command with { ExpectedChildAgeSourceWriterGeneration = 0 },
            command with { ExpectedChildAgeSourceWriterGeneration = long.MaxValue },
            command with { ExpectedChildAgeAdoptionWriterGeneration = 0 },
            command with { ExpectedChildAgeAdoptionWriterGeneration = long.MaxValue },
            command with { ExpectedChildAgeAdoptionWriterGeneration = 5 },
            command with { ExpectedChildAgeAdoptionWriterGeneration = 4 },
            command with { Procedure = null! }
        ];
        foreach (var request in invalid)
            await Reject(request);
    }

    [Test]
    public async Task Adoption_and_withdrawal_require_exact_original_source_and_adoption_CAS()
    {
        var adopt = (AdoptModerationProfileCollectionProcedureCommand)Create(1);
        var withdraw = (WithdrawModerationProfileCollectionProcedureCommand)Create(2);
        ModerationQualificationCommand[] invalid =
        [
            adopt with { SourceId = Guid.Empty },
            adopt with { AdoptionId = Guid.Parse("019a0000-0000-7000-7000-000000000001") },
            adopt with { AdoptionId = adopt.SourceId },
            adopt with { ExpectedSourceRevision = 0 },
            adopt with { ExpectedSourceRevision = 2 },
            adopt with { ExpectedSourceWriterGeneration = 0 },
            adopt with { ExpectedSourceWriterGeneration = long.MaxValue },
            withdraw with { AdoptionId = Guid.Empty },
            withdraw with { AdoptionId = Guid.Parse("01234567-89ab-4cde-8fab-0123456789ab") },
            withdraw with { ExpectedAdoptionRevision = 0 },
            withdraw with { ExpectedAdoptionRevision = 2 }
        ];
        foreach (var request in invalid)
            await Reject(request);
    }

    [Test]
    [Arguments(0)]
    [Arguments(1)]
    public async Task Finite_UTC_lease_is_preserved_without_application_clock_renewal(int variant)
    {
        var command = Create(variant);
        foreach (var invalid in new[] { DateTimeOffset.MinValue, DateTimeOffset.MaxValue, Until.ToOffset(TimeSpan.FromHours(1)) })
            await Reject(WithLease(command, invalid));
        var past = Until.AddYears(-10);
        var request = WithLease(command, past);
        await Assert.That((await Validate(request)).IsValid).IsTrue();
        var lease = request.ToMutation() switch
        {
            RecordModerationProfileCollectionProcedureSource source => source.ValidUntil,
            AdoptModerationProfileCollectionProcedureSource adoption => adoption.ValidUntil,
            _ => throw new ArgumentOutOfRangeException(nameof(variant))
        };
        await Assert.That(lease).IsEqualTo(past);
    }

    [Test]
    [Arguments(0)]
    [Arguments(1)]
    [Arguments(2)]
    public async Task JSON_rejects_unknown_authority_or_renewal_fields(int variant)
    {
        // Jurisdiction is supplied by the trusted adapter; its factory-only Domain type
        // is not a standalone JSON constructor contract.
        var command = Create(variant) with { Target = null! };
        var json = JsonSerializer.Serialize(command, command.GetType());
        await Assert.That(JsonSerializer.Deserialize(json, command.GetType())).IsNotNull();
        foreach (var field in new[] { "\"Consent\":true", "\"GuardianPermission\":true", "\"RenewUntil\":\"2027-01-01T00:00:00Z\"" })
        {
            var payload = json[..^1] + "," + field + "}";
            await Assert.That(() => JsonSerializer.Deserialize(payload, command.GetType())).Throws<JsonException>();
        }
    }

    [Test]
    public async Task Procedure_JSON_and_with_variants_preserve_typed_field_vocabulary()
    {
        var command = (RecordModerationProfileCollectionProcedureCommand)Create(0);
        var json = JsonSerializer.SerializeToElement(command);
        var restored = json.GetProperty("Procedure").Deserialize<ModerationProfileCollectionProcedure>()!;
        await Assert.That(restored.Purpose).IsEqualTo(ModerationIndividualPurpose.ProfileCompletion);
        await Assert.That(restored.Applicability).IsEqualTo(ModerationProfileCollectionApplicability.NativeIndependentlyAcceptedAssessment);
        await Assert.That(restored.PreliminaryFields).IsEquivalentTo(Procedure.PreliminaryFields);
        await Assert.That(restored.NormalFields).IsEquivalentTo(Procedure.NormalFields);
        await Assert.That(restored.GuardianRelationshipReviewGrounds).IsEqualTo(Procedure.GuardianRelationshipReviewGrounds);
        await Assert.That(restored.ManualIssuerReviewGrounds).IsEqualTo(Procedure.ManualIssuerReviewGrounds);
        var original = (RecordModerationProfileCollectionProcedureSource)command.ToMutation();
        var changed = (RecordModerationProfileCollectionProcedureSource)(command with { ExpectedChildAgeAdoptionWriterGeneration = 9 }).ToMutation();
        await Assert.That(changed).IsEqualTo(original with { ExpectedChildAgeAdoptionWriterGeneration = 9 });
        await Assert.That(original.ExpectedChildAgeAdoptionWriterGeneration).IsEqualTo(7L);
    }

    [Test]
    public async Task Operation_discovery_registers_each_procedure_handler_once_as_scoped()
    {
        var services = new ServiceCollection();
        services.AddNativeOperations();
        services.ValidateNativeOperationRegistrations();
        Type[] contracts =
        [
            typeof(ICommandHandler<RecordModerationProfileCollectionProcedureCommand, ModerationQualificationResponse>),
            typeof(ICommandHandler<AdoptModerationProfileCollectionProcedureCommand, ModerationQualificationResponse>),
            typeof(ICommandHandler<WithdrawModerationProfileCollectionProcedureCommand, ModerationQualificationResponse>)
        ];
        foreach (var contract in contracts)
        {
            await Assert.That(services.Count(descriptor => descriptor.ServiceType == contract)).IsEqualTo(1);
            await Assert.That(services.Single(descriptor => descriptor.ServiceType == contract).Lifetime).IsEqualTo(ServiceLifetime.Scoped);
        }
    }

    private static async Task Reject(ModerationQualificationCommand command)
    {
        await Assert.That((await Validate(command)).IsValid).IsFalse();
        var response = command switch
        {
            RecordModerationProfileCollectionProcedureCommand record =>
                await new RecordModerationProfileCollectionProcedureCommandHandler(null!).ExecuteAsync(record),
            AdoptModerationProfileCollectionProcedureCommand adopt =>
                await new AdoptModerationProfileCollectionProcedureCommandHandler(null!).ExecuteAsync(adopt),
            WithdrawModerationProfileCollectionProcedureCommand withdraw =>
                await new WithdrawModerationProfileCollectionProcedureCommandHandler(null!).ExecuteAsync(withdraw),
            _ => throw new ArgumentOutOfRangeException(nameof(command))
        };
        await Assert.That(response.IsSuccess).IsFalse();
        await Assert.That(response.FailureCode).IsNull();
        await Assert.That(response.Errors).IsNotNull();
        await Assert.That(response.Revision).IsNull();
        await Assert.That(response.QualificationRevision).IsNull();
    }

    private static Task<FluentValidation.Results.ValidationResult> Validate(ModerationQualificationCommand command) =>
        new ModerationProfileCollectionProcedureCommandValidator<ModerationQualificationCommand>().ValidateAsync(command);

    private static ModerationQualificationCommand WithLease(ModerationQualificationCommand command, DateTimeOffset until) => command switch
    {
        RecordModerationProfileCollectionProcedureCommand record => record with { ValidUntil = until },
        AdoptModerationProfileCollectionProcedureCommand adopt => adopt with { ValidUntil = until },
        _ => throw new ArgumentOutOfRangeException(nameof(command))
    };

    private static Guid Id(int value) => Guid.Parse($"019a0000-0000-7000-8000-{value:x12}");

    private static ModerationQualificationCommand Create(int variant) => variant switch
    {
        0 => new RecordModerationProfileCollectionProcedureCommand
        {
            OperationId = Id(1),
            Target = Target,
            ExpectedQualificationRevision = 13,
            SourceId = Id(2),
            ChildAgeSourceId = Id(3),
            ExpectedChildAgeSourceRevision = 1,
            ExpectedChildAgeSourceWriterGeneration = 5,
            ChildAgeAdoptionId = Id(4),
            ExpectedChildAgeAdoptionRevision = 1,
            ExpectedChildAgeAdoptionWriterGeneration = 7,
            Procedure = Procedure,
            ValidUntil = Until
        },
        1 => new AdoptModerationProfileCollectionProcedureCommand
        {
            OperationId = Id(1),
            Target = Target,
            ExpectedQualificationRevision = 13,
            SourceId = Id(2),
            ExpectedSourceRevision = 1,
            ExpectedSourceWriterGeneration = 11,
            AdoptionId = Id(5),
            ValidUntil = Until
        },
        2 => new WithdrawModerationProfileCollectionProcedureCommand
        {
            OperationId = Id(1),
            Target = Target,
            ExpectedQualificationRevision = 13,
            AdoptionId = Id(5),
            ExpectedAdoptionRevision = 1
        },
        _ => throw new ArgumentOutOfRangeException(nameof(variant))
    };
}
