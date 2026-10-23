using Explore.Application;
using Explore.Application.Contracts.Operations;
using Explore.Application.Features.Moderation.Handlers.Commands;
using Explore.Application.Features.Moderation.Requests.Commands;
using Explore.Domain;
using ISLAMU.Moderation.Domain;
using Microsoft.Extensions.DependencyInjection;

namespace Event.Application.UnitTests.Moderation;

public sealed class ModerationRecognitionPrivacyCommandTests
{
    [Test]
    [Arguments(false)]
    [Arguments(true)]
    public async Task Null_retention_entries_reject_without_throwing_or_reaching_native_writes(bool onlyNull)
    {
        var command = Record();
        command = command with
        {
            Retention = onlyNull ? [null!] : command.Retention.SetItem(1, null!)
        };
        var validation = await new ModerationRecognitionPrivacyCommandValidator<RecordModerationRecognitionPrivacyCommand>()
            .ValidateAsync(command);
        await Assert.That(validation.IsValid).IsFalse();
    }

    [Test]
    public async Task Invalid_UUID_variant_cannot_become_a_native_operation_identity()
    {
        var bytes = Guid.CreateVersion7().ToByteArray(bigEndian: true);
        bytes[8] = 1;
        var command = Record() with { OperationId = new Guid(bytes, bigEndian: true) };
        var validation = await new ModerationRecognitionPrivacyCommandValidator<RecordModerationRecognitionPrivacyCommand>()
            .ValidateAsync(command);
        await Assert.That(validation.IsValid).IsFalse();
    }

    [Test]
    public async Task Substantive_privacy_commands_are_discovered_once_with_native_scoped_handlers()
    {
        var command = Record();
        await Assert.That((await new ModerationRecognitionPrivacyCommandValidator<RecordModerationRecognitionPrivacyCommand>()
            .ValidateAsync(command)).IsValid).IsTrue();
        var services = new ServiceCollection();
        services.AddNativeOperations();
        var contracts = new[]
        {
            typeof(ICommandHandler<RecordModerationRecognitionPrivacyCommand, ModerationQualificationResponse>),
            typeof(ICommandHandler<AdoptModerationRecognitionPrivacyCommand, ModerationQualificationResponse>),
            typeof(ICommandHandler<WithdrawModerationRecognitionPrivacyCommand, ModerationQualificationResponse>)
        };
        foreach (var contract in contracts)
        {
            await Assert.That(services.Count(descriptor => descriptor.ServiceType == contract)).IsEqualTo(1);
            await Assert.That(services.Single(descriptor => descriptor.ServiceType == contract).Lifetime)
                .IsEqualTo(ServiceLifetime.Scoped);
        }
    }

    private static RecordModerationRecognitionPrivacyCommand Record()
    {
        var trust = AuthorityScope.TrustDomain(Guid.CreateVersion7());
        var scope = AuthorityScope.Tenant(AuthorityScope.Instance(
            AuthorityScope.Solution(trust, Guid.CreateVersion7()), Guid.CreateVersion7()), Guid.CreateVersion7());
        var privacy = Guid.CreateVersion7();
        var recovery = Guid.CreateVersion7();
        return new()
        {
            OperationId = Guid.CreateVersion7(), Target = scope, ExpectedQualificationRevision = 2,
            SourceId = Guid.CreateVersion7(), PolicyId = Guid.CreateVersion7(), ExpectedPolicyRevision = 1,
            PublicationId = Guid.CreateVersion7(), ProfileId = Guid.CreateVersion7(),
            Mode = Explore.Application.Services.Moderation.ModerationNativeMode.Embedded,
            ValidUntil = new DateTimeOffset(2026, 10, 10, 12, 5, 0, TimeSpan.Zero),
            Terms = new(ModerationRecognitionPrivacyPurpose.ScopedActionContinuity, ModerationScopeKind.Tenant,
                ModerationRecognitionPrivacyLinkage.ExactFinalActiveSanctionScopeRevisionAndInterval,
                ModerationRecognitionPrivacyOrdinaryErasure.IndependentExistingAuthority,
                ModerationRecognitionPrivacyCorrection.RevisionBoundRemoveOnlyAffectedContributionAndPreserveRemedies,
                ModerationRecognitionPrivacyCatastrophicDisposition.QualifiedRecoveryOrLegalErasureByDeadline,
                ModerationRecognitionPrivacyKeyObligations.OriginalComputedTokensAndAcknowledgedReplicaBackupHorizon,
                6, TimeSpan.FromHours(1), TimeSpan.FromDays(30), TimeSpan.FromDays(30), TimeSpan.FromDays(7)),
            Ownership = new(privacy, privacy, recovery, recovery, Guid.CreateVersion7(),
                "Independent canonical controllers.", "Accountable privacy and legal mandate.",
                "Accountable recovery and disposition mandate."),
            Retention =
            [
                new(ModerationRecognitionPrivacyRecordClass.PendingComputedRecognition, TimeSpan.FromDays(30),
                    ModerationRecognitionPrivacyHold.None, TimeSpan.Zero, TimeSpan.Zero),
                new(ModerationRecognitionPrivacyRecordClass.EligibleMatchingMaterial, TimeSpan.FromDays(365),
                    ModerationRecognitionPrivacyHold.None, TimeSpan.Zero, TimeSpan.Zero),
                new(ModerationRecognitionPrivacyRecordClass.ProtectedCorrectionEvidence, TimeSpan.FromDays(366),
                    ModerationRecognitionPrivacyHold.None, TimeSpan.Zero, TimeSpan.Zero),
                new(ModerationRecognitionPrivacyRecordClass.DispositionAndReplayReceipt, TimeSpan.FromDays(90),
                    ModerationRecognitionPrivacyHold.None, TimeSpan.Zero, TimeSpan.Zero)
            ]
        };
    }
}
