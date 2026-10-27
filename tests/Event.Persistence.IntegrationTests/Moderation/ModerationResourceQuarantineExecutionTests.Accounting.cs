using System.Data.Common;
using System.Text.Json;
using Explore.Application.Contracts.Notifications;
using Explore.Application.Contracts.Persistence;
using Explore.Domain;
using Explore.Persistence.Repositories;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Diagnostics;

namespace Event.Persistence.IntegrationTests.Moderation;

public sealed partial class ModerationResourceQuarantineExecutionTests
{
    [Test]
    [Arguments(false)]
    [Arguments(true)]
    public async Task Original_cause_accounting_preserves_exact_receipts_without_completing_the_recipe(bool storedFile)
    {
        await using var fixture = await Fixture.CreateAsync(storedFile);
        await using var context = fixture.Context();
        var identity = await fixture.AuthenticateAsync(context);
        var owner = new ModerationResourceQuarantineRepository(context, identity.Machine, identity.Bindings,
            new ExternalApiKeyAuthenticationRepository(new SecurityTestSupport.AdministrativeAuthorityTestFactory(context)), fixture.Clock,
            fixture.Database.Privacy.Authority(context, fixture.Clock));
        var result = await owner.ExecuteOriginalAsync(fixture.EffectId, fixture.LeaseId, fixture.LeaseRevision);
        await Assert.That(result.Outcome).IsEqualTo(ModerationOriginalQuarantineOutcome.Accounted);
        await Assert.That(result.ExpectedCauseCount).IsEqualTo(1);
        await Assert.That(result.Receipts.Count).IsEqualTo(result.ExpectedCauseCount);
        var original = result.Receipts.Single();
        await Assert.That(original.BindingOrdinal).IsEqualTo(0);
        await Assert.That(original.EventResourceId).IsEqualTo(fixture.ResourceId);
        await Assert.That(original.MaterialVersionId).IsEqualTo(fixture.MaterialId);
        await Assert.That(original.ReleaseId).IsNull();
        fixture.Clock.UtcNow = Now.AddSeconds(1);
        var replay = await owner.ExecuteOriginalAsync(fixture.EffectId, fixture.LeaseId, fixture.LeaseRevision);
        await Assert.That(replay.Outcome).IsEqualTo(ModerationOriginalQuarantineOutcome.Accounted);
        await Assert.That(replay.ExpectedCauseCount).IsEqualTo(1);
        await Assert.That(replay.Receipts.Single().Id).IsEqualTo(original.Id);
        await Assert.That(replay.Receipts.Single().AppliedAt).IsEqualTo(original.AppliedAt);
        await AssertState(fixture, 1);
    }

    [Test]
    [Arguments(false)]
    [Arguments(true)]
    public async Task Incomplete_original_discovery_never_executes_a_valid_target_prefix(bool missingGeneration)
    {
        await using var fixture = await Fixture.CreateAsync();
        await using (var writer = fixture.Context())
        {
            writer.ModerationTrustDomainId = fixture.Database.Scope.TrustDomainId;
            var effect = await writer.ModerationEffectIntents.SingleAsync(row => row.Id == fixture.EffectId);
            var source = await writer.ModerationNoticeSources.SingleAsync(row => row.EffectId == fixture.EffectId);
            var capture = JsonSerializer.Deserialize<ModerationNoticeCaptureSnapshot>(source.SnapshotJson)!;
            var first = capture.Notice!.Effects.Single();
            var unsupported = first with
            {
                Target = missingGeneration
                    ? first.Target with { Source = first.Target.Source with { Id = Guid.CreateVersion7() } }
                    : first.Target with { Owner = "unsupported.resource-owner" }
            };
            var changed = capture with { Notice = capture.Notice with { Effects = [first, unsupported] } };
            // Inject an inconsistent original at the accepted-input boundary. Native discovery,
            // not a mocked executor, must reject it before applying the otherwise valid prefix.
            await writer.ModerationNoticeSources.Where(row => row.EffectId == fixture.EffectId).ExecuteDeleteAsync();
            writer.ChangeTracker.Clear();
            writer.ModerationNoticeSources.Add(ModerationNoticeSource.Capture(effect, source.TenantId,
                JsonSerializer.Serialize(changed), source.RetainUntil));
            await writer.SaveChangesAsync();
        }
        await using var context = fixture.Context();
        var identity = await fixture.AuthenticateAsync(context);
        var result = await new ModerationResourceQuarantineRepository(context, identity.Machine, identity.Bindings,
            new ExternalApiKeyAuthenticationRepository(new SecurityTestSupport.AdministrativeAuthorityTestFactory(context)), fixture.Clock,
            fixture.Database.Privacy.Authority(context, fixture.Clock))
            .ExecuteOriginalAsync(fixture.EffectId, fixture.LeaseId, fixture.LeaseRevision);
        await Assert.That(result.Outcome).IsEqualTo(ModerationOriginalQuarantineOutcome.Unavailable);
        await Assert.That(result.ExpectedCauseCount).IsEqualTo(0);
        await Assert.That(result.Receipts.Count).IsEqualTo(0);
        await AssertState(fixture, 0);
    }

    [Test]
    public async Task Purpose_expiry_after_original_discovery_keeps_cause_accounting_partial()
    {
        await using var fixture = await Fixture.CreateAsync();
        var expiry = new ExpireAfterOriginalMaterialDiscovery(fixture);
        await using var context = fixture.Context(expiry);
        var identity = await fixture.AuthenticateAsync(context);
        var result = await new ModerationResourceQuarantineRepository(context, identity.Machine, identity.Bindings,
            new ExternalApiKeyAuthenticationRepository(new SecurityTestSupport.AdministrativeAuthorityTestFactory(context)), fixture.Clock,
            fixture.Database.Privacy.Authority(context, fixture.Clock))
            .ExecuteOriginalAsync(fixture.EffectId, fixture.LeaseId, fixture.LeaseRevision);
        await Assert.That(expiry.Observed).IsTrue();
        await Assert.That(result.Outcome).IsEqualTo(ModerationOriginalQuarantineOutcome.Partial);
        await Assert.That(result.ExpectedCauseCount).IsEqualTo(1);
        await Assert.That(result.Receipts.Count).IsEqualTo(0);
        await AssertState(fixture, 0);
    }

    private sealed class ExpireAfterOriginalMaterialDiscovery(Fixture fixture) : DbCommandInterceptor
    {
        public bool Observed { get; private set; }

        public override ValueTask<DbDataReader> ReaderExecutedAsync(
            DbCommand command, CommandExecutedEventData eventData, DbDataReader result,
            CancellationToken cancellationToken = default)
        {
            if (!Observed && result.FieldCount == 1
                && result.GetName(0) == "material_version_id"
                && command.CommandText.Contains("moderation_resource_generations", StringComparison.Ordinal))
            {
                Observed = true;
                fixture.Clock.UtcNow = fixture.PurposeDeadline;
            }
            return ValueTask.FromResult(result);
        }
    }
}
