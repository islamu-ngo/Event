using Explore.Application.Contracts.Infrastructure;
using Explore.Application.Contracts.Persistence;
using Explore.Application.Exceptions;
using Explore.Application.Features.Learning;
using Explore.Application.Features.Learning.Requests.Commands;
using Explore.Application.Features.Learning.Validators;
using Explore.Domain.Learning;
using ISLAMU.Wire.Contracts.Learning;
using LearningRequirementPolicy = Explore.Domain.Learning.LearningRequirementPolicy;

namespace Event.Application.UnitTests.Learning;

public sealed class LearningRequirementAdmissionTests
{
    private static readonly DateTime Now = new(2026, 10, 30, 12, 0, 0, DateTimeKind.Utc);
    private static readonly LearningGuidancePolicy Required =
        new(requirementPolicy: LearningRequirementPolicy.TenantAdminOrientationAcknowledgement);

    [Test]
    public async Task AdministratorAuthorityPrecedesOptionalPolicyValidation()
    {
        var operation = Operation();
        await Assert.That(() => LearningAccessPolicy.RequireAcknowledgement(
            Intent(operation.Sources!), false, operation.Sources!, LearningGuidancePolicy.Default))
            .Throws<AuthorizationException>();
        await Assert.That(() => LearningAccessPolicy.RequireAcknowledgement(
            Intent(operation.Sources!), true, operation.Sources!, LearningGuidancePolicy.Default))
            .Throws<ValidationException>();
    }

    [Test]
    public async Task SubmissionRequiresTheCurrentSourceEvenWhenMaterialWasAlreadyAcknowledged()
    {
        var operation = Operation();
        var original = Intent(operation.Sources!);
        var current = Sources(operation.TenantId, 'C');
        operation.State!.AcknowledgeRequirement(Guid.CreateVersion7(), LearningRequirementDefinition.Current,
            operation.Sources!.PolicySourceStamp, original.IntentId, 0, Now);

        await Assert.That(() => LearningAccessPolicy.RequireAcknowledgement(original, true, current, Required))
            .Throws<ConcurrencyConflictException>();
        await Assert.That(LearningAccessPolicy.Requirement(operation with { Sources = current }, Required).Status)
            .IsEqualTo(LearningRequirementStatus.Satisfied);
    }

    [Test]
    public async Task SuppressionAndHiddenLearningDoNotHideThePendingRequirementOrWriteEvidence()
    {
        var operation = Operation();
        operation.State!.UpdatePreferences(true, true, false, 0, Now);

        var projected = LearningAccessPolicy.Requirement(operation, Required);

        await Assert.That(projected.Status).IsEqualTo(LearningRequirementStatus.Pending);
        await Assert.That(projected.AcknowledgedAtUtc).IsNull();
        await Assert.That(operation.State.RequirementAcknowledgements).IsEmpty();
        await Assert.That(operation.State.Revision).IsEqualTo(1);
        await Assert.That(operation.Space).IsNull();
    }

    [Test]
    public async Task CurrentMaterialStaysSatisfiedAcrossPolicyChangesWithoutRewritingItsEvidence()
    {
        var operation = Operation();
        string originalStamp = operation.Sources!.PolicySourceStamp;
        var acknowledgement = operation.State!.AcknowledgeRequirement(Guid.CreateVersion7(),
            LearningRequirementDefinition.Current, originalStamp, Guid.CreateVersion7(), 0, Now);
        var changed = operation with { Sources = Sources(operation.TenantId, 'D') };

        var projected = LearningAccessPolicy.Requirement(changed, Required);

        await Assert.That(projected.Status).IsEqualTo(LearningRequirementStatus.Satisfied);
        await Assert.That(projected.PolicySourceStamp).IsEqualTo(changed.Sources!.PolicySourceStamp);
        await Assert.That(projected.AcknowledgedAtUtc).IsEqualTo(new DateTimeOffset(Now));
        await Assert.That(acknowledgement.PolicySourceStamp).IsEqualTo(originalStamp);
        await Assert.That(operation.State.Revision).IsEqualTo(1);
    }

    [Test]
    public async Task OptionalPolicyOrLostAdministratorRoleMakesEvidenceInapplicableWithoutErasingIt()
    {
        var operation = Operation();
        operation.State!.AcknowledgeRequirement(Guid.CreateVersion7(), LearningRequirementDefinition.Current,
            operation.Sources!.PolicySourceStamp, Guid.CreateVersion7(), 0, Now);

        var optional = LearningAccessPolicy.Requirement(operation, LearningGuidancePolicy.Default);
        var nonAdministrator = LearningAccessPolicy.Requirement(operation with { CanManageSpace = false }, Required);

        await Assert.That(optional.Status).IsEqualTo(LearningRequirementStatus.NotApplicable);
        await Assert.That(nonAdministrator.Status).IsEqualTo(LearningRequirementStatus.NotApplicable);
        await Assert.That(optional.AcknowledgedAtUtc).IsNull();
        await Assert.That(nonAdministrator.AcknowledgedAtUtc).IsNull();
        await Assert.That(operation.State.RequirementAcknowledgements.Count).IsEqualTo(1);
    }

    [Test]
    public async Task ForeignSourceOrLearnerCannotSupplyPrivateRequirementEvidence()
    {
        var operation = Operation();
        await Assert.That(() => LearningAccessPolicy.Requirement(
            operation with { Sources = Sources(Guid.CreateVersion7(), 'B') }, Required))
            .Throws<UnauthorizedAccessException>();
        await Assert.That(() => LearningAccessPolicy.Requirement(
            operation with { UserId = Guid.CreateVersion7() }, Required))
            .Throws<UnauthorizedAccessException>();
    }

    [Test]
    [Arguments("other-requirement", 1)]
    [Arguments("tenant-admin-orientation", 2)]
    public async Task UnknownRequirementOrMaterialCannotBecomeAnAcknowledgement(string key, int version)
    {
        var operation = Operation();
        var command = new AcknowledgeLearningRequirementCommand(key, new()
        {
            IntentId = Guid.CreateVersion7(),
            ExpectedScopeStamp = LearningAccessPolicy.ScopeStamp(operation.TenantId, operation.UserId),
            ExpectedRevision = 0,
            ExpectedPolicySourceStamp = operation.Sources!.PolicySourceStamp,
            MaterialVersion = version
        });
        var validation = await new AcknowledgeLearningRequirementValidator().ValidateAsync(command);
        await Assert.That(validation.IsValid).IsFalse();
        await Assert.That(() => LearningAccessPolicy.RequireAcknowledgement(
            Intent(operation.Sources!) with { RequirementKey = key, MaterialVersion = version },
            true, operation.Sources!, Required)).Throws<ValidationException>();
    }

    private static LearningOperationState Operation()
    {
        Guid tenant = Guid.CreateVersion7(), user = Guid.CreateVersion7();
        return new(tenant, user, "self", null, null,
            UserLearningState.Create(Guid.CreateVersion7(), tenant, user, Now),
            null, [], true, false)
        {
            Sources = Sources(tenant, 'B'),
            ObservedAtUtc = Now
        };
    }

    private static LearningSourceFacts Sources(Guid tenant, char stamp) =>
        new(tenant, new string('A', 64), new string(stamp, 64), false, true, null,
            new Dictionary<string, ResolvedSetting>());

    private static LearningRequirementAcknowledgementIntent Intent(LearningSourceFacts sources) =>
        new(0, Guid.CreateVersion7(), LearningRequirementDefinition.Current.RequirementKey,
            LearningRequirementDefinition.Current.MaterialVersion, sources.PolicySourceStamp);
}
