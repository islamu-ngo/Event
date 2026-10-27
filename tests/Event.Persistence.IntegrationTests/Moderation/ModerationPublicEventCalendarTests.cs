using Explore.Application.Contracts.Persistence;
using Explore.Persistence.Repositories;
using ISLAMU.Moderation.Domain.Policies;

namespace Event.Persistence.IntegrationTests.Moderation;

public sealed partial class ModerationPolicyPublicationTests
{
    [Test]
    public async Task PublicCalendar_AcceptedGeneralMaterialCandidateDoesNotAuthorizeAnonymousDelivery()
    {
        await RunClassificationMaterialFixtureAsync(MaterialChange.None, ModerationReferenceProfileId.CFG29,
            async fixture =>
            {
                var selection = await ClassificationSelectionRequest(fixture);
                var recorded = await fixture.Writer(fixture.Database.AdminId).ExecuteAsync(selection);
                await Assert.That(recorded.Outcome).IsEqualTo(ModerationQualificationWriteOutcome.Known);
                var epoch = recorded.Receipt!.QualificationRevision;
                var ordinals = selection.Contribution.Effects.Length + selection.Contribution.Consequences.Length;
                for (var ordinal = 0; ordinal < ordinals; ordinal++)
                {
                    var adopted = await fixture.Writer(fixture.Adopter).ExecuteAsync(new AdoptModerationClassificationEventTarget
                    {
                        OperationId = Guid.CreateVersion7(),
                        Target = selection.Target,
                        ExpectedQualificationRevision = epoch,
                        AdoptionId = Guid.CreateVersion7(),
                        SelectionId = selection.SelectionId,
                        ExpectedSelectionRevision = 1,
                        BindingOrdinal = ordinal,
                        EventGenerationId = fixture.Generation.SourceId,
                        ExpectedEventGenerationRevision = 1,
                        ValidUntil = Now.AddMinutes(1)
                    });
                    await Assert.That(adopted.Outcome).IsEqualTo(ModerationQualificationWriteOutcome.Known);
                    epoch = adopted.Receipt!.QualificationRevision;
                }
                fixture.Context.ModerationTrustDomainId = null;
                var repository = new ModerationPublicEventCalendarRepository(fixture.Context,
                    fixture.Database.Inventory, fixture.Clock,
                    fixture.Database.Privacy.Authority(fixture.Context, fixture.Clock));
                var outcome = await repository.ExecuteSerializedAsync(async token =>
                {
                    var acquired = await repository.AcquireAsync(
                        new(fixture.Database.TenantId, fixture.Generation.EventId), token);
                    await Assert.That(acquired.Lease).IsNull();
                    return acquired.Outcome;
                }, default);
                await Assert.That(outcome).IsEqualTo(ModerationPublicEventCalendarOutcome.NotFound);
                await Assert.That(fixture.Context.ModerationTrustDomainId).IsNull();
                await Assert.That(fixture.Context.Database.CurrentTransaction).IsNull();
            }, true);
    }

    [Test]
    public async Task PublicCalendar_RequiresItsOriginalPrivacySerialization()
    {
        await using var database = await Database.CreateAsync();
        await using var context = database.Context();
        var clock = new DeploymentClock();
        var repository = new ModerationPublicEventCalendarRepository(context, database.Inventory,
            clock, database.Privacy.Authority(context, clock));
        var acquired = await repository.AcquireAsync(new(database.TenantId, Guid.CreateVersion7()), default);
        await Assert.That(acquired.Outcome).IsEqualTo(ModerationPublicEventCalendarOutcome.Unavailable);
        await Assert.That(acquired.Lease).IsNull();
        await Assert.That(context.Database.CurrentTransaction).IsNull();
    }

    [Test]
    public async Task PublicCalendar_ForeignTenantCannotBorrowCurrentContext()
    {
        await using var database = await Database.CreateAsync();
        await using var context = database.Context();
        var clock = new DeploymentClock();
        var repository = new ModerationPublicEventCalendarRepository(context, database.Inventory,
            clock, database.Privacy.Authority(context, clock));
        var outcome = await repository.ExecuteSerializedAsync(async token =>
        {
            var acquired = await repository.AcquireAsync(new(Guid.CreateVersion7(), Guid.CreateVersion7()), token);
            await Assert.That(acquired.Lease).IsNull();
            return acquired.Outcome;
        }, default);
        await Assert.That(outcome).IsEqualTo(ModerationPublicEventCalendarOutcome.Unavailable);
        await Assert.That(context.ModerationTrustDomainId).IsNull();
    }
}
