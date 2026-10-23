using Explore.Application.Contracts.Persistence;
using Explore.Application.Features.Moderation.Handlers.Commands;
using Explore.Application.Features.Moderation.Requests.Commands;
using ISLAMU.Moderation.Domain;

namespace Event.Application.UnitTests.Moderation;

public sealed class ModerationQualificationPolicyCommandTests
{
    private static readonly AuthorityScope Target = AuthorityScope.Tenant(
        AuthorityScope.Instance(AuthorityScope.Solution(AuthorityScope.TrustDomain(Guid.CreateVersion7()),
            Guid.CreateVersion7()), Guid.CreateVersion7()), Guid.CreateVersion7());
    private static readonly DateTimeOffset Until = new(2026, 10, 11, 12, 0, 0, TimeSpan.Zero);

    [Test]
    public async Task Publication_maps_explicit_limits_and_stable_coordinates_exactly()
    {
        var command = Publish();
        var mutation = (PublishModerationQualificationPolicySource)command.ToMutation();
        await Assert.That(mutation.OperationId).IsEqualTo(command.OperationId);
        await Assert.That(mutation.SourceId).IsEqualTo(command.SourceId);
        await Assert.That(mutation.CaseId).IsEqualTo(command.CaseId);
        await Assert.That(mutation.ExpectedCaseRevision).IsEqualTo(2L);
        await Assert.That(mutation.ExpectedQualificationRevision).IsEqualTo(4L);
        await Assert.That(mutation.ExpectedSourceRevision).IsEqualTo(0L);
        await Assert.That(mutation.MaximumReviewerLifetime).IsEqualTo(TimeSpan.FromMinutes(30));
        await Assert.That(mutation.MaximumUrgencyLifetime).IsEqualTo(TimeSpan.FromMinutes(5));
        await Assert.That(mutation.ValidUntil).IsEqualTo(Until);
        await Assert.That((await new ModerationQualificationPolicyCommandValidator<PublishModerationQualificationPolicyCommand>()
            .ValidateAsync(command)).IsValid).IsTrue();
        await Assert.That((await new ModerationQualificationValidator()
            .ValidateAsync(new ApplyModerationQualificationCommand(mutation))).IsValid).IsTrue();
    }

    [Test]
    public async Task Invalid_limits_and_missing_case_generation_fail_both_entrypoints()
    {
        foreach (var command in new[]
        {
            Publish() with { MaximumReviewerLifetime = TimeSpan.Zero },
            Publish() with { MaximumUrgencyLifetime = TimeSpan.MaxValue },
            Publish() with { ExpectedCaseRevision = 0 },
            Publish() with { ExpectedSourceRevision = -1 },
            Publish() with { SourceId = Guid.NewGuid() },
            Publish() with { ValidUntil = DateTimeOffset.MaxValue }
        })
        {
            await Assert.That((await new ModerationQualificationPolicyCommandValidator<PublishModerationQualificationPolicyCommand>()
                .ValidateAsync(command)).IsValid).IsFalse();
            await Assert.That((await new ModerationQualificationValidator()
                .ValidateAsync(new ApplyModerationQualificationCommand(command.ToMutation()))).IsValid).IsFalse();
        }
    }

    [Test]
    public async Task Adoption_and_revocation_preserve_exact_source_and_adoption_generations()
    {
        var command = new AdoptModerationQualificationPolicyCommand
        {
            OperationId = Guid.CreateVersion7(), Target = Target, ExpectedQualificationRevision = 5,
            SourceId = Guid.CreateVersion7(), ExpectedSourceRevision = 2, ExpectedWriterGeneration = 4,
            AdoptionId = Guid.CreateVersion7(), CaseId = Guid.CreateVersion7(), ExpectedCaseRevision = 1,
            ValidUntil = Until
        };
        var mutation = (AdoptModerationQualificationPolicySource)command.ToMutation();
        await Assert.That(mutation.ExpectedSourceRevision).IsEqualTo(2L);
        await Assert.That(mutation.ExpectedWriterGeneration).IsEqualTo(4L);
        await Assert.That(mutation.AdoptionId).IsEqualTo(command.AdoptionId);
        await Assert.That((await new ModerationQualificationPolicyCommandValidator<AdoptModerationQualificationPolicyCommand>()
            .ValidateAsync(command with { ExpectedWriterGeneration = 0 })).IsValid).IsFalse();
        var revoke = new RevokeModerationQualificationPolicyAdoptionCommand
        {
            OperationId = Guid.CreateVersion7(), Target = Target, ExpectedQualificationRevision = 6,
            SourceId = command.SourceId, ExpectedSourceRevision = 2, AdoptionId = command.AdoptionId,
            ExpectedAdoptionRevision = 1, CaseId = command.CaseId, ExpectedCaseRevision = 1
        };
        await Assert.That(((RevokeModerationQualificationPolicyAdoption)revoke.ToMutation())
            .ExpectedAdoptionRevision).IsEqualTo(1L);
        await Assert.That((await new ModerationQualificationPolicyCommandValidator<RevokeModerationQualificationPolicyAdoptionCommand>()
            .ValidateAsync(revoke)).IsValid).IsTrue();
    }

    private static PublishModerationQualificationPolicyCommand Publish() => new()
    {
        OperationId = Guid.CreateVersion7(), Target = Target, ExpectedQualificationRevision = 4,
        SourceId = Guid.CreateVersion7(), CaseId = Guid.CreateVersion7(), ExpectedCaseRevision = 2,
        ExpectedSourceRevision = 0, MaximumReviewerLifetime = TimeSpan.FromMinutes(30),
        MaximumUrgencyLifetime = TimeSpan.FromMinutes(5), ValidUntil = Until
    };
}
