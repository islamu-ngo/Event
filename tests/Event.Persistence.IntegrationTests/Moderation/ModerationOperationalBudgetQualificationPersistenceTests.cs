using System.Diagnostics;
using Explore.Application.Contracts.Moderation;
using Explore.Domain;
using Explore.Persistence;
using ISLAMU.Moderation.Domain;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Diagnostics;
using SourceDatabase = Event.Persistence.IntegrationTests.Moderation.ModerationQualificationWriterTests.SourceDatabase;

namespace Event.Persistence.IntegrationTests.Moderation;

/// <summary>Native budget governance witnesses; capacity terms do not grant admission permission.</summary>
public sealed class ModerationOperationalBudgetQualificationPersistenceTests
{
    [Test]
    public async Task NamedIndependentOperatorAdoptsExactHumanReportBudget()
    {
        await using var database = await SourceDatabase.CreateAsync();
        var source = Request(database);
        await Assert.That((await database.ExecuteAsync(database.AdministratorId, source)).IsSuccess).IsTrue();
        var adoption = Adopt(database, source);
        await Assert.That((await database.ExecuteAsync(database.UrgencyAssessorId, adoption)).IsSuccess).IsTrue();
        await using var context = database.Context();
        context.ModerationTrustDomainId = database.Scope.TrustDomainId;
        var recorded = await context.Set<ModerationOperationalBudgetSource>().SingleAsync();
        var accepted = await context.Set<ModerationOperationalBudgetAdoption>().SingleAsync();
        await Assert.That(recorded.Terms).IsEqualTo(source.Terms);
        await Assert.That(recorded.RecordedByHumanId).IsEqualTo(database.AdministratorId);
        await Assert.That(recorded.OperatorHumanId).IsEqualTo(database.UrgencyAssessorId);
        await Assert.That(accepted.SourceId).IsEqualTo(source.SourceId);
        await Assert.That(accepted.SourceRevision).IsEqualTo(1L);
        await Assert.That(accepted.SourceWriterGeneration).IsEqualTo(2L);
        await Assert.That(accepted.AdoptedByHumanId).IsEqualTo(database.UrgencyAssessorId);
        await Assert.That(accepted.MatchesCurrent(recorded, database.EvaluationTime)).IsTrue();
        await Assert.That(await context.ModerationQualificationMutationReceipts.CountAsync()).IsEqualTo(2);
        await Assert.That((await context.ModerationQualificationEpochs.SingleAsync()).Revision).IsEqualTo(3L);
        await Assert.That(await context.ModerationCases.AnyAsync()).IsFalse();
        await Assert.That(await context.ModerationSanctions.AnyAsync()).IsFalse();
    }

    [Test]
    public async Task RecorderCannotNameThemselfAsIndependentOperator()
    {
        await using var database = await SourceDatabase.CreateAsync();
        var source = Request(database) with { OperatorHumanId = database.AdministratorId };
        await Assert.That((await database.ExecuteAsync(database.AdministratorId, source)).IsSuccess).IsFalse();
        await using var context = database.Context();
        context.ModerationTrustDomainId = database.Scope.TrustDomainId;
        await Assert.That(await context.Set<ModerationOperationalBudgetSource>().AnyAsync()).IsFalse();
        await Assert.That(await context.ModerationQualificationMutationReceipts.AnyAsync()).IsFalse();
        await Assert.That((await context.ModerationQualificationEpochs.SingleAsync()).Revision).IsEqualTo(1L);
    }

    [Test]
    [Arguments(false)]
    [Arguments(true)]
    public async Task OrdinaryReporterCannotRecordOrBecomeTheBudgetOperator(bool namedOperator)
    {
        await using var database = await SourceDatabase.CreateAsync();
        var source = Request(database);
        if (namedOperator) source = source with { OperatorHumanId = database.SubjectId };
        var caller = namedOperator ? database.AdministratorId : database.SubjectId;
        await Assert.That((await database.ExecuteAsync(caller, source)).IsSuccess).IsFalse();
        await using var context = database.Context();
        context.ModerationTrustDomainId = database.Scope.TrustDomainId;
        await Assert.That(await context.Set<ModerationOperationalBudgetSource>().AnyAsync()).IsFalse();
        await Assert.That(await context.ModerationQualificationMutationReceipts.AnyAsync()).IsFalse();
        await Assert.That(await context.PlatformUserRoles.AnyAsync(row => row.UserId == database.SubjectId)).IsFalse();
        await Assert.That((await context.ModerationQualificationEpochs.SingleAsync()).Revision).IsEqualTo(1L);
    }

    [Test]
    [Arguments(false)]
    [Arguments(true)]
    public async Task AnotherNativeAdministratorCannotAcceptTheNamedOperatorsBudget(bool recorder)
    {
        await using var database = await SourceDatabase.CreateAsync();
        var source = Request(database);
        await Assert.That((await database.ExecuteAsync(database.AdministratorId, source)).IsSuccess).IsTrue();
        var adoption = Adopt(database, source);
        await Assert.That((await database.ExecuteAsync(
            recorder ? database.AdministratorId : database.ReviewerId, adoption)).IsSuccess).IsFalse();
        await using var context = database.Context();
        context.ModerationTrustDomainId = database.Scope.TrustDomainId;
        await Assert.That(await context.Set<ModerationOperationalBudgetAdoption>().AnyAsync()).IsFalse();
        await Assert.That(await context.ModerationQualificationMutationReceipts.CountAsync()).IsEqualTo(1);
        await Assert.That((await context.ModerationQualificationEpochs.SingleAsync()).Revision).IsEqualTo(2L);
    }

    [Test]
    public async Task ExactSourceAndAdoptionReplayRetainOriginalReceiptsWithoutAdvancingEpoch()
    {
        await using var database = await SourceDatabase.CreateAsync();
        var source = Request(database);
        var firstSource = await database.ExecuteAsync(database.AdministratorId, source);
        var replaySource = await database.ExecuteAsync(database.AdministratorId, source);
        await Assert.That(firstSource.IsSuccess).IsTrue();
        await Assert.That(replaySource.IsSuccess).IsTrue();
        await Assert.That(replaySource.Id).IsEqualTo(firstSource.Id);
        await Assert.That(replaySource.Revision).IsEqualTo(firstSource.Revision);
        await Assert.That(replaySource.QualificationRevision).IsEqualTo(2L);
        var adoption = Adopt(database, source);
        var firstAdoption = await database.ExecuteAsync(database.UrgencyAssessorId, adoption);
        var replayAdoption = await database.ExecuteAsync(database.UrgencyAssessorId, adoption);
        await Assert.That(firstAdoption.IsSuccess).IsTrue();
        await Assert.That(replayAdoption.IsSuccess).IsTrue();
        await Assert.That(replayAdoption.Id).IsEqualTo(firstAdoption.Id);
        await Assert.That(replayAdoption.Revision).IsEqualTo(firstAdoption.Revision);
        await Assert.That(replayAdoption.QualificationRevision).IsEqualTo(3L);
        await Assert.That((await database.ExecuteAsync(database.UrgencyAssessorId,
            adoption with { AdoptionId = Guid.CreateVersion7() })).IsSuccess).IsFalse();
        await using var context = database.Context();
        context.ModerationTrustDomainId = database.Scope.TrustDomainId;
        await Assert.That(await context.Set<ModerationOperationalBudgetSource>().CountAsync()).IsEqualTo(1);
        await Assert.That(await context.Set<ModerationOperationalBudgetAdoption>().CountAsync()).IsEqualTo(1);
        await Assert.That(await context.ModerationQualificationMutationReceipts.CountAsync()).IsEqualTo(2);
        await Assert.That((await context.ModerationQualificationEpochs.SingleAsync()).Revision).IsEqualTo(3L);
    }

    [Test]
    [Arguments(false, false)]
    [Arguments(false, true)]
    [Arguments(true, false)]
    [Arguments(true, true)]
    public async Task LostOrSubstitutedOriginalReceiptCannotQualifyAdoptionReplay(
        bool adoptionReceipt, bool substituted)
    {
        await using var database = await SourceDatabase.CreateAsync();
        var source = Request(database);
        await Assert.That((await database.ExecuteAsync(database.AdministratorId, source)).IsSuccess).IsTrue();
        var adoption = Adopt(database, source);
        await Assert.That((await database.ExecuteAsync(database.UrgencyAssessorId, adoption)).IsSuccess).IsTrue();
        var operation = adoptionReceipt ? adoption.OperationId : source.OperationId;
        await using (var writer = database.Context())
        {
            writer.ModerationTrustDomainId = database.Scope.TrustDomainId;
            var receipt = await writer.ModerationQualificationMutationReceipts.SingleAsync(row => row.Id == operation);
            if (substituted)
            {
                var other = await writer.ModerationQualificationMutationReceipts.SingleAsync(row => row.Id != operation);
                writer.Entry(receipt).Property(row => row.RequestDigest).CurrentValue = other.RequestDigest;
            }
            else
                writer.ModerationQualificationMutationReceipts.Remove(receipt);
            await writer.SaveChangesAsync();
        }
        await Assert.That((await database.ExecuteAsync(database.UrgencyAssessorId, adoption)).IsSuccess).IsFalse();
        await using var context = database.Context();
        context.ModerationTrustDomainId = database.Scope.TrustDomainId;
        await Assert.That(await context.Set<ModerationOperationalBudgetAdoption>().CountAsync()).IsEqualTo(1);
        await Assert.That((await context.ModerationQualificationEpochs.SingleAsync()).Revision).IsEqualTo(3L);
        await Assert.That(await context.ModerationQualificationMutationReceipts.CountAsync()).IsEqualTo(substituted ? 2 : 1);
    }

    [Test]
    public async Task LatestSourceReplacementMakesOldAdoptionUnusableWithoutRewritingHistory()
    {
        await using var database = await SourceDatabase.CreateAsync();
        var source = Request(database);
        await Assert.That((await database.ExecuteAsync(database.AdministratorId, source)).IsSuccess).IsTrue();
        var adoption = Adopt(database, source);
        await Assert.That((await database.ExecuteAsync(database.UrgencyAssessorId, adoption)).IsSuccess).IsTrue();
        var replacement = Request(database) with
        {
            ExpectedQualificationRevision = 3,
            Terms = new(ModerationSubjectKind.Human, ModerationOperationalAction.SubmitReport, 2, TimeSpan.FromHours(1))
        };
        await Assert.That((await database.ExecuteAsync(database.AdministratorId, replacement)).IsSuccess).IsTrue();
        await Assert.That((await database.ExecuteAsync(database.UrgencyAssessorId, adoption)).IsSuccess).IsFalse();
        await Assert.That((await database.ExecuteAsync(database.UrgencyAssessorId, adoption with
        {
            OperationId = Guid.CreateVersion7(),
            AdoptionId = Guid.CreateVersion7(),
            ExpectedQualificationRevision = 4
        })).IsSuccess).IsFalse();
        var currentAdoption = Adopt(database, replacement) with
        {
            ExpectedQualificationRevision = 4,
            ExpectedSourceWriterGeneration = 4
        };
        await Assert.That((await database.ExecuteAsync(database.UrgencyAssessorId, currentAdoption)).IsSuccess).IsTrue();
        await using var context = database.Context();
        context.ModerationTrustDomainId = database.Scope.TrustDomainId;
        await Assert.That(await context.Set<ModerationOperationalBudgetSource>().CountAsync()).IsEqualTo(2);
        await Assert.That(await context.Set<ModerationOperationalBudgetAdoption>().CountAsync()).IsEqualTo(2);
        await Assert.That((await context.Set<ModerationOperationalBudgetAdoption>()
            .SingleAsync(row => row.Id == adoption.AdoptionId)).Revision).IsEqualTo(1L);
        await Assert.That((await context.ModerationQualificationEpochs.SingleAsync()).Revision).IsEqualTo(5L);
    }

    [Test]
    [Arguments(false)]
    [Arguments(true)]
    public async Task CurrentAdministratorCanWithdrawAfterOriginalExpiryOrOperatorDeparture(bool departed)
    {
        await using var database = await SourceDatabase.CreateAsync();
        var source = Request(database);
        await Assert.That((await database.ExecuteAsync(database.AdministratorId, source)).IsSuccess).IsTrue();
        var adoption = Adopt(database, source);
        await Assert.That((await database.ExecuteAsync(database.UrgencyAssessorId, adoption)).IsSuccess).IsTrue();
        if (departed)
        {
            await using var writer = database.Context();
            await writer.PlatformUserRoles.Where(row => row.UserId == database.UrgencyAssessorId).ExecuteDeleteAsync();
        }
        else
            database.EvaluationTime = source.ValidUntil;
        var withdrawal = new WithdrawModerationOperationalBudgetAdoption
        {
            OperationId = Guid.CreateVersion7(),
            Target = database.Scope,
            ExpectedQualificationRevision = 3,
            AdoptionId = adoption.AdoptionId,
            ExpectedAdoptionRevision = 1
        };
        await Assert.That((await database.ExecuteAsync(database.ReviewerId, withdrawal)).IsSuccess).IsTrue();
        await Assert.That((await database.ExecuteAsync(database.UrgencyAssessorId, adoption)).IsSuccess).IsFalse();
        await using var context = database.Context();
        context.ModerationTrustDomainId = database.Scope.TrustDomainId;
        var withdrawn = await context.Set<ModerationOperationalBudgetAdoption>().SingleAsync();
        await Assert.That(withdrawn.Revision).IsEqualTo(2L);
        await Assert.That(withdrawn.WithdrawnByHumanId).IsEqualTo((Guid?)database.ReviewerId);
        await Assert.That(withdrawn.WithdrawnAt).IsEqualTo((DateTimeOffset?)database.EvaluationTime);
        await Assert.That(await context.ModerationQualificationMutationReceipts.CountAsync()).IsEqualTo(3);
        await Assert.That((await context.ModerationQualificationEpochs.SingleAsync()).Revision).IsEqualTo(4L);
    }

    [Test]
    [Arguments(false, false)]
    [Arguments(false, true)]
    [Arguments(true, false)]
    [Arguments(true, true)]
    public async Task FinalFlushOwnerAuthorityLossRollsBackSourceOrAdoptionAndReceipt(
        bool adopting, bool operatorLost)
    {
        await using var database = await SourceDatabase.CreateAsync();
        var source = Request(database);
        if (adopting)
            await Assert.That((await database.ExecuteAsync(database.AdministratorId, source)).IsSuccess).IsTrue();
        var adoption = Adopt(database, source);
        var operationId = adopting ? adoption.OperationId : source.OperationId;
        var revoked = operatorLost ? database.UrgencyAssessorId : database.AdministratorId;
        using var flush = new QualificationFlush(operationId, context =>
            context.PlatformUserRoles.Where(row => row.UserId == revoked).ExecuteDelete());
        await Assert.That((adopting
            ? await database.ExecuteAsync(database.UrgencyAssessorId, adoption)
            : await database.ExecuteAsync(database.AdministratorId, source)).IsSuccess).IsFalse();
        await Assert.That(flush.Observed).IsTrue();
        await Assert.That(flush.AffectedRows).IsEqualTo(1);
        await using var reader = database.Context();
        reader.ModerationTrustDomainId = database.Scope.TrustDomainId;
        await Assert.That(await reader.Set<ModerationOperationalBudgetSource>().CountAsync()).IsEqualTo(adopting ? 1 : 0);
        await Assert.That(await reader.Set<ModerationOperationalBudgetAdoption>().AnyAsync()).IsFalse();
        await Assert.That(await reader.ModerationQualificationMutationReceipts.AnyAsync(row => row.Id == operationId)).IsFalse();
        await Assert.That(await reader.ModerationQualificationMutationReceipts.CountAsync()).IsEqualTo(adopting ? 1 : 0);
        await Assert.That((await reader.ModerationQualificationEpochs.SingleAsync()).Revision).IsEqualTo(adopting ? 2L : 1L);
        await Assert.That(await reader.PlatformUserRoles.AnyAsync(row => row.UserId == revoked)).IsTrue();
    }

    [Test]
    public async Task OriginalSourceReceiptLossAtAdoptionFlushRollsBackAdoptionAndRestoresReceipt()
    {
        await using var database = await SourceDatabase.CreateAsync();
        var source = Request(database);
        await Assert.That((await database.ExecuteAsync(database.AdministratorId, source)).IsSuccess).IsTrue();
        var adoption = Adopt(database, source);
        using var flush = new QualificationFlush(adoption.OperationId, context =>
            context.ModerationQualificationMutationReceipts.Where(row => row.Id == source.OperationId).ExecuteDelete());
        await Assert.That((await database.ExecuteAsync(database.UrgencyAssessorId, adoption)).IsSuccess).IsFalse();
        await Assert.That(flush.Observed).IsTrue();
        await Assert.That(flush.AffectedRows).IsEqualTo(1);
        await using var reader = database.Context();
        reader.ModerationTrustDomainId = database.Scope.TrustDomainId;
        await Assert.That(await reader.Set<ModerationOperationalBudgetSource>().CountAsync()).IsEqualTo(1);
        await Assert.That(await reader.Set<ModerationOperationalBudgetAdoption>().AnyAsync()).IsFalse();
        await Assert.That((await reader.ModerationQualificationMutationReceipts.SingleAsync()).Id).IsEqualTo(source.OperationId);
        await Assert.That((await reader.ModerationQualificationEpochs.SingleAsync()).Revision).IsEqualTo(2L);
    }

    private static RecordModerationOperationalBudgetSource Request(SourceDatabase database) => new()
    {
        OperationId = Guid.CreateVersion7(),
        Target = database.Scope,
        ExpectedQualificationRevision = 1,
        SourceId = Guid.CreateVersion7(),
        Terms = new(ModerationSubjectKind.Human, ModerationOperationalAction.SubmitReport, 3, TimeSpan.FromHours(1)),
        OperatorHumanId = database.UrgencyAssessorId,
        ValidUntil = database.EvaluationTime.AddHours(2)
    };

    private static AdoptModerationOperationalBudgetSource Adopt(
        SourceDatabase database, RecordModerationOperationalBudgetSource source) => new()
        {
            OperationId = Guid.CreateVersion7(),
            Target = database.Scope,
            ExpectedQualificationRevision = 2,
            SourceId = source.SourceId,
            ExpectedSourceRevision = 1,
            ExpectedSourceWriterGeneration = 2,
            AdoptionId = Guid.CreateVersion7(),
            ValidUntil = source.ValidUntil
        };

    /// <summary>
    /// Observes the helper's actual completed save and mutates only its owning transaction.
    /// The exact operation receipt scopes this subscription away from parallel test contexts.
    /// </summary>
    private sealed class QualificationFlush : IObserver<DiagnosticListener>,
        IObserver<KeyValuePair<string, object?>>, IDisposable
    {
        private readonly Guid _operationId;
        private readonly Func<ExploreDbContext, int> _afterSave;
        private readonly IDisposable _allListeners;
        private readonly List<IDisposable> _subscriptions = [];

        public QualificationFlush(Guid operationId, Func<ExploreDbContext, int> afterSave)
        {
            _operationId = operationId;
            _afterSave = afterSave;
            _allListeners = DiagnosticListener.AllListeners.Subscribe(this);
        }

        public bool Observed { get; private set; }
        public int AffectedRows { get; private set; }

        public void OnNext(DiagnosticListener listener)
        {
            if (listener.Name == DbLoggerCategory.Name)
                _subscriptions.Add(listener.Subscribe(this, name => name == CoreEventId.SaveChangesCompleted.Name));
        }

        public void OnNext(KeyValuePair<string, object?> value)
        {
            if (Observed || value.Value is not SaveChangesCompletedEventData { Context: ExploreDbContext context }
                || !context.ChangeTracker.Entries<ModerationQualificationMutationReceipt>()
                    .Any(entry => entry.Entity.Id == _operationId))
                return;
            if (context.Database.CurrentTransaction is null)
                throw new InvalidOperationException("The qualification flush must still own its native transaction.");
            Observed = true;
            AffectedRows = _afterSave(context);
        }

        public void OnCompleted() { }
        public void OnError(Exception error) => throw error;

        public void Dispose()
        {
            _allListeners.Dispose();
            foreach (var subscription in _subscriptions) subscription.Dispose();
        }
    }
}
