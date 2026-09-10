using System.Text;
using Event.Persistence.IntegrationTests.Fixtures;
using Explore.Application.Contracts.Infrastructure;
using Explore.Application.Contracts.Persistence;
using Explore.Application.Contracts.Services;
using Explore.Application.Contracts.Services.Registration;
using Explore.Application.Models.Storage;
using Explore.Application.Services.Registration.Commands;
using Explore.Domain;
using Explore.Domain.Enums;
using Explore.Domain.ValueObjects;
using Explore.Infrastructure.Registration;
using Explore.Infrastructure.Services.Registration.Providers.SubmissionSinks;
using Explore.Persistence.Repositories;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using NSubstitute;

namespace Event.Persistence.IntegrationTests;

public sealed class AnonymousRetentionProviderDrainTests
{
    private static readonly DateTime Now = new(2027, 1, 8, 14, 0, 0, DateTimeKind.Utc);

    [Test]
    [Arguments(false)]
    [Arguments(true)]
    public async Task ExpiredAnonymousOrderNeverDecryptsOrHandsOffAndHeldRowsRemain(bool held)
    {
        await using var harness = await Harness.CreateAsync(Now);
        var graph = await harness.SeedAsync(Now, anonymous: true);
        await harness.AddAnswerAsync(graph, "name", held ? null : Now.AddDays(1));
        harness.Context.ChangeTracker.Clear();

        int completed = await harness.DrainAsync();

        await Assert.That(completed).IsEqualTo(0);
        await Assert.That(harness.Protector.UnprotectCalls).IsEqualTo(0);
        await Assert.That(harness.StorageCalls).IsEqualTo(0);
        var effect = await harness.Context.RegistrationProviderSubmissionWriteEffects.AsNoTracking().SingleAsync();
        await Assert.That(effect.FailureCode).IsEqualTo("registration_data_retention_expired");
        await Assert.That(effect.Status).IsEqualTo(OutboxMessageStatus.DeadLettered);
        await Assert.That(effect.ParkedAt).IsNull();
        await Assert.That(await harness.Context.RegistrationAnswers.CountAsync()).IsEqualTo(1);
        await Assert.That(await harness.Context.RegistrationSensitiveAnswerValues.CountAsync()).IsEqualTo(1);
        await Assert.That((await harness.Context.RegistrationAnswers.SingleAsync()).RetentionUntil)
            .IsEqualTo(held ? null : Now.AddDays(1));
    }

    [Test]
    public async Task HistoricalAnonymousMissingOriginalBoundNeverDecrypts()
    {
        await using var harness = await Harness.CreateAsync(Now);
        var graph = await harness.SeedAsync(null, anonymous: true);
        await harness.AddAnswerAsync(graph, "name", null);
        harness.Context.ChangeTracker.Clear();

        await harness.DrainAsync();

        await Assert.That(harness.Protector.UnprotectCalls).IsEqualTo(0);
        await Assert.That(harness.StorageCalls).IsEqualTo(0);
        await Assert.That((await harness.Context.RegistrationProviderSubmissionWriteEffects.AsNoTracking().SingleAsync()).FailureCode)
            .IsEqualTo("registration_data_retention_expired");
    }

    [Test]
    public async Task MixedAnswersFilterBothDeadlinesBeforeDecryptionAndPersistMinimumActuallyIncludedDeadline()
    {
        await using var harness = await Harness.CreateAsync(Now);
        var graph = await harness.SeedAsync(Now.AddDays(2), anonymous: true);
        await harness.AddAnswerAsync(graph, "expired", Now);
        await harness.AddAnswerAsync(graph, "ciphertext", Now.AddDays(1), sensitiveDeadline: Now);
        await harness.AddAnswerAsync(graph, "unmapped", Now.AddTicks(1));
        await harness.AddAnswerAsync(graph, "blocked", Now.AddTicks(1), transferable: false);
        await harness.AddAnswerAsync(graph, "included", Now.AddDays(1), sensitiveDeadline: Now.AddHours(2));
        await harness.AddAnswerAsync(graph, "held", null);
        harness.Context.ChangeTracker.Clear();

        int completed = await harness.DrainAsync();

        await Assert.That(completed).IsEqualTo(1);
        await Assert.That(harness.Protector.UnprotectCalls).IsEqualTo(2);
        await Assert.That(harness.Csv).Contains("private-included");
        await Assert.That(harness.Csv).Contains("private-held");
        await Assert.That(harness.Csv).DoesNotContain("private-expired");
        await Assert.That(harness.Csv).DoesNotContain("private-ciphertext");
        await Assert.That(harness.Csv).DoesNotContain("private-unmapped");
        await Assert.That(harness.Csv).DoesNotContain("private-blocked");
        var artifact = await harness.Context.StorageObjects.AsNoTracking().SingleAsync();
        await Assert.That(artifact.RegistrationContentRetentionUntilUtc).IsEqualTo(Now.AddHours(2));
        await Assert.That(artifact.OwningResourceId).IsEqualTo(graph.Submission.Id);
        await Assert.That(await harness.Context.RegistrationAnswers.CountAsync()).IsEqualTo(6);
    }

    [Test]
    [Arguments(false)]
    [Arguments(true)]
    public async Task ExpiredMappedAnswersArePermanentNotAmbiguous(bool cleanupBeforeDrain)
    {
        await using var harness = await Harness.CreateAsync(Now);
        var graph = await harness.SeedAsync(Now.AddDays(1), anonymous: true);
        await harness.AddAnswerAsync(graph, "name", Now);
        harness.Context.ChangeTracker.Clear();
        if (cleanupBeforeDrain)
        {
            var cleanup = await harness.CleanupAsync();
            await Assert.That(cleanup.AnswersDeleted).IsEqualTo(1);
            await Assert.That(cleanup.SensitiveValuesDeleted).IsEqualTo(1);
            await Assert.That(await harness.Context.RegistrationAnswers.CountAsync()).IsEqualTo(0);
            await Assert.That(await harness.Context.RegistrationSensitiveAnswerValues.CountAsync()).IsEqualTo(0);
        }

        await Assert.That(await harness.DrainAsync()).IsEqualTo(0);

        var effect = await harness.Context.RegistrationProviderSubmissionWriteEffects.AsNoTracking().SingleAsync();
        await Assert.That(effect.FailureCode).IsEqualTo("registration_data_retention_expired");
        await Assert.That(effect.Status).IsEqualTo(OutboxMessageStatus.DeadLettered);
        await Assert.That(effect.DeadLetteredAt).IsEqualTo(Now);
        await Assert.That(effect.ParkedAt).IsNull();
        await Assert.That(harness.Protector.UnprotectCalls).IsEqualTo(0);
        await Assert.That(harness.StorageCalls).IsEqualTo(0);
    }

    [Test]
    public async Task CleanupPreservesDeliveryOfRemainingLiveMappedAnswers()
    {
        await using var harness = await Harness.CreateAsync(Now);
        var graph = await harness.SeedAsync(Now.AddDays(1), anonymous: true);
        await harness.AddAnswerAsync(graph, "expired", Now);
        await harness.AddAnswerAsync(graph, "included", Now.AddHours(1));
        harness.Context.ChangeTracker.Clear();

        await Assert.That((await harness.CleanupAsync()).AnswersDeleted).IsEqualTo(1);
        await Assert.That(await harness.DrainAsync()).IsEqualTo(1);
        await Assert.That(harness.Protector.UnprotectCalls).IsEqualTo(1);
        await Assert.That(harness.Csv).Contains("private-included");
        await Assert.That(harness.Csv).DoesNotContain("private-expired");
    }

    [Test]
    [Arguments("unmapped", true)]
    [Arguments("blocked", false)]
    public async Task CleanupDoesNotInventExpiryEvidenceForMissingMappedAnswers(string key, bool transferable)
    {
        await using var harness = await Harness.CreateAsync(Now);
        var graph = await harness.SeedAsync(Now.AddDays(1), anonymous: true);
        await harness.AddAnswerAsync(graph, "name", Now.AddHours(1));
        await harness.AddAnswerAsync(graph, key, Now, transferable);
        harness.Context.ChangeTracker.Clear();
        await harness.Context.RegistrationAnswers.Where(answer => answer.RetentionUntil > Now).ExecuteDeleteAsync();

        await Assert.That((await harness.CleanupAsync()).AnswersDeleted).IsEqualTo(1);
        await harness.DrainAsync();

        var effect = await harness.Context.RegistrationProviderSubmissionWriteEffects.AsNoTracking().SingleAsync();
        await Assert.That(effect.FailureCode).IsEqualTo("provider_submission_mapped_answers_empty");
        await Assert.That(effect.ParkedAt).IsEqualTo(Now);
        await Assert.That(effect.DeadLetteredAt).IsNull();
        await Assert.That(harness.StorageCalls).IsEqualTo(0);
    }

    [Test]
    [Arguments("processing")]
    [Arguments("expired-claim")]
    [Arguments("retry")]
    [Arguments("completed")]
    [Arguments("parked")]
    public async Task CleanupNeverSettlesPreviouslyClaimedWork(string state)
    {
        await using var harness = await Harness.CreateAsync(Now);
        var graph = await harness.SeedAsync(Now.AddDays(1), anonymous: true);
        await harness.AddAnswerAsync(graph, "name", Now);
        harness.Context.ChangeTracker.Clear();
        var repository = new RegistrationProviderSubmissionWriteEffectRepository(harness.Context);
        DateTime claimedAt = Now.AddMinutes(-2);
        var claim = (await repository.ClaimDueAsync("in-flight", 1, claimedAt,
            TimeSpan.FromMinutes(state == "expired-claim" ? 1 : 3), CancellationToken.None)).Single();
        if (state == "retry")
        {
            await repository.RetryAsync(claim, "provider_unavailable", Now.AddMinutes(1), Now, CancellationToken.None);
        }
        else if (state == "completed")
        {
            await repository.CompleteAsync(claim, Now, CancellationToken.None);
        }
        else if (state == "parked")
        {
            await repository.ParkAmbiguousAsync(claim, "provider_handoff_uncertain", Now, CancellationToken.None);
        }
        var before = await harness.Context.RegistrationProviderSubmissionWriteEffects.AsNoTracking().SingleAsync();
        harness.Context.ChangeTracker.Clear();

        await Assert.That((await harness.CleanupAsync()).AnswersDeleted).IsEqualTo(1);

        var after = await harness.Context.RegistrationProviderSubmissionWriteEffects.AsNoTracking().SingleAsync();
        await Assert.That(after.Status).IsEqualTo(before.Status);
        await Assert.That(after.FailureCode).IsEqualTo(before.FailureCode);
        await Assert.That(after.DeadLetteredAt).IsEqualTo(before.DeadLetteredAt);
        await Assert.That(after.ParkedAt).IsEqualTo(before.ParkedAt);
        await Assert.That(after.ProcessingLeaseToken).IsEqualTo(before.ProcessingLeaseToken);
        await Assert.That(after.ProcessingFence).IsEqualTo(before.ProcessingFence);
        await Assert.That(after.AttemptCount).IsEqualTo(before.AttemptCount);
        await Assert.That(after.NextAttemptAt).IsEqualTo(before.NextAttemptAt);
    }

    [Test]
    public async Task CleanupDuringProviderHandoffPreservesTheAmbiguousOutcome()
    {
        await using var harness = await Harness.CreateAsync(Now);
        var graph = await harness.SeedAsync(Now.AddDays(1), anonymous: true);
        await harness.AddAnswerAsync(graph, "name", Now.AddTicks(1));
        harness.Context.ChangeTracker.Clear();
        var handedOff = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var releaseProvider = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        Task<int> draining = harness.DrainAsync(async () =>
        {
            handedOff.SetResult();
            await releaseProvider.Task.WaitAsync(TimeSpan.FromSeconds(10));
            throw new RegistrationProviderSubmissionDeliveryException(
                RegistrationProviderSubmissionDeliveryFailureKind.AmbiguousAfterHandoff, "provider_handoff_uncertain");
        });

        try
        {
            await handedOff.Task.WaitAsync(TimeSpan.FromSeconds(10));
            harness.SetUtcNow(Now.AddTicks(1));
            await Assert.That((await harness.CleanupAsync()).AnswersDeleted).IsEqualTo(1);
            var inFlight = await harness.Context.RegistrationProviderSubmissionWriteEffects.AsNoTracking().SingleAsync();
            await Assert.That(inFlight.Status).IsEqualTo(OutboxMessageStatus.Processing);
            await Assert.That(inFlight.DeadLetteredAt).IsNull();
        }
        finally
        {
            releaseProvider.TrySetResult();
            await draining.WaitAsync(TimeSpan.FromSeconds(10));
        }

        var effect = await harness.Context.RegistrationProviderSubmissionWriteEffects.AsNoTracking().SingleAsync();
        await Assert.That(effect.FailureCode).IsEqualTo("provider_handoff_uncertain");
        await Assert.That(effect.ParkedAt).IsEqualTo(Now.AddTicks(1));
        await Assert.That(effect.DeadLetteredAt).IsNull();
        await Assert.That(harness.StorageCalls).IsEqualTo(1);
    }

    [Test]
    public async Task CleanupDoesNotClassifyNonanonymousExpiryAsPermanent()
    {
        await using var harness = await Harness.CreateAsync(Now);
        var graph = await harness.SeedAsync(null, anonymous: false);
        await harness.AddAnswerAsync(graph, "name", Now);
        harness.Context.ChangeTracker.Clear();

        await Assert.That((await harness.CleanupAsync()).AnswersDeleted).IsEqualTo(1);
        await harness.DrainAsync();

        var effect = await harness.Context.RegistrationProviderSubmissionWriteEffects.AsNoTracking().SingleAsync();
        await Assert.That(effect.FailureCode).IsEqualTo("provider_submission_mapped_answers_empty");
        await Assert.That(effect.ParkedAt).IsEqualTo(Now);
        await Assert.That(effect.DeadLetteredAt).IsNull();
    }

    [Test]
    public async Task NonanonymousDeliveryKeepsExistingBehaviorWithoutAnAnonymousArtifactBound()
    {
        await using var harness = await Harness.CreateAsync(Now);
        var graph = await harness.SeedAsync(null, anonymous: false);
        await harness.AddAnswerAsync(graph, "name", Now.AddDays(-1));
        harness.Context.ChangeTracker.Clear();

        await Assert.That(await harness.DrainAsync()).IsEqualTo(1);
        await Assert.That(harness.Protector.UnprotectCalls).IsEqualTo(1);
        await Assert.That(harness.Csv).Contains("private-name");
        await Assert.That((await harness.Context.StorageObjects.AsNoTracking().SingleAsync()).RegistrationContentRetentionUntilUtc).IsNull();
    }

    [Test]
    public async Task DeliveryRepositoryRejectsAClaimWithAnotherOrdersSubmissionOrTenant()
    {
        await using var harness = await Harness.CreateAsync(Now);
        var first = await harness.SeedAsync(Now.AddDays(1), anonymous: true);
        var second = await harness.SeedAsync(Now.AddDays(1), anonymous: true);
        harness.Context.ChangeTracker.Clear();
        var repository = new RegistrationProviderSubmissionWriteEffectRepository(harness.Context);
        var claims = await repository.ClaimDueAsync("lineage", 10, Now, TimeSpan.FromMinutes(1), CancellationToken.None);
        var claim = claims.Single(value => value.RegistrationSubmissionId == first.Submission.Id);

        await Assert.That(await repository.GetDeliveryAsync(claim with { RegistrationSubmissionId = second.Submission.Id }, CancellationToken.None)).IsNull();
        await Assert.That(await repository.GetDeliveryAsync(claim with { TenantId = Guid.CreateVersion7() }, CancellationToken.None)).IsNull();
        await Assert.That(await repository.GetDeliveryAsync(claim, CancellationToken.None)).IsNotNull();
    }

    private sealed record Graph(RegistrationOrder Order, RegistrationRequirement Requirement, RegistrationFormVersion Version,
        RegistrationFormSection Section, RegistrationProviderBinding Binding, RegistrationSubmission Submission);

    private sealed class Harness(EventVisitorCapabilitySqliteFixture fixture, Clock clock) : IAsyncDisposable
    {
        public Explore.Persistence.ExploreDbContext Context => fixture.Context;
        public CountingProtector Protector { get; } = new(fixture.Services.GetRequiredService<IRegistrationSensitiveValueProtector>());
        public int StorageCalls { get; private set; }
        public string Csv { get; private set; } = string.Empty;

        public static async Task<Harness> CreateAsync(DateTime now)
        {
            var clock = new Clock(now);
            return new(await EventVisitorCapabilitySqliteFixture.CreateAsync(services => services.AddSingleton<TimeProvider>(clock)), clock);
        }

        public async Task<Graph> SeedAsync(DateTime? deadline, bool anonymous)
        {
            DateTime createdAt = Now.AddDays(-10);
            var target = await fixture.SeedEventAsync();
            var ticket = await fixture.SeedTicketAsync(target.Id);
            var workflow = RegistrationWorkflow.Create(fixture.TenantId, target.Id, "RETENTION", createdAt);
            var requirement = RegistrationRequirement.Create(workflow, 1, RegistrationRequirementCriticalityEnum.Required, false,
                RegistrationRequirementCompletionEffectEnum.BlocksRegistration, RegistrationAnswerSyncModeEnum.FULL_CANONICAL,
                RegistrationRequirementSubjectTypeEnum.AllOrders, null, createdAt);
            workflow.AddRequirement(requirement);
            var form = RegistrationForm.Create(fixture.TenantId, target.Id, "registration", "retention", "Retention", createdAt);
            var version = RegistrationFormVersion.Create(form, 1, "en", null, null, createdAt);
            var section = RegistrationFormSection.Create(Guid.CreateVersion7(), version, 1, "Details", createdAt);
            version.AddSection(section);
            form.AddVersion(version);
            var tuple = CsvRegistrationProviderSubmissionSink.SupportedTuple;
            var connection = RegistrationProviderConnection.Create(fixture.TenantId, "CSV " + target.Id.ToString("N"), RegistrationProviderKindEnum.ExternalApi,
                RegistrationProviderDeploymentKindEnum.HostedSaas, tuple.ProviderCode, tuple.ProviderDeploymentCode, tuple.ApiVersion,
                tuple.AdapterPolicyVersion, tuple.ConformanceEvidenceRevision, "https://8.8.8.8", "https://8.8.8.8",
                "csv-" + target.Id.ToString("N"), null, null, createdAt);
            var binding = RegistrationProviderBinding.Create(fixture.TenantId, connection.Id, form.Id, version.Id,
                RegistrationProviderPresentationModeEnum.Manual, RegistrationProviderCollectionModeEnum.MirrorOnly,
                RegistrationProviderCompletionModeEnum.Callback, RegistrationProviderTrustLevelEnum.SelectedFields, null, createdAt);
            binding.AddCapability(RegistrationProviderCapability.Create(binding, tuple.ProviderCode, tuple.ProviderDeploymentCode,
                tuple.ApiVersion, tuple.AdapterPolicyVersion, tuple.ConformanceEvidenceRevision, RegistrationProviderCapabilityCodes.SubmissionSink));
            foreach (string key in new[] { "name", "expired", "ciphertext", "blocked", "included", "held" })
            {
                binding.AddFieldMapping(RegistrationProviderFieldMapping.Create(binding, "registration." + key, key, true));
            }
            var channel = RegistrationChannel.Create(requirement, 1, false, binding.Id, createdAt);
            requirement.AddChannel(channel);
            var revision = RegistrationEvidenceHash.Create(Convert.ToBase64String(new byte[32]));
            binding.Publish(revision, createdAt);
            var order = RegistrationOrder.Create(fixture.TenantId, target.Id, anonymous ? null : fixture.UserId, null,
                BookingPartyTypeEnum.Individual, ticket.CatalogId,
                RegistrationParticipationSnapshot.Create(Guid.CreateVersion7(), (int)ParticipationHandlingModeEnum.PlatformManaged,
                    (int)AdvanceRegistrationObligationEnum.Required, (int)IdentityAccessModeEnum.GuestAllowed, GuestRecoveryPolicyEnum.EmailOptional),
                workflow.Id, anonymous ? CapabilityTokenHash.Create(Convert.ToBase64String(new byte[32])) : null,
                "USD", createdAt, Now.AddDays(3), deadline);
            Context.AddRange(workflow, form, connection, binding, order);
            await Context.SaveChangesAsync();
            var attempt = RegistrationAttempt.Create(fixture.TenantId, target.Id, order.Id, workflow.Id, requirement.Id, channel.Id,
                form.Id, version.Id, CapabilityTokenHash.Create(Convert.ToBase64String(Guid.NewGuid().ToByteArray().Concat(Guid.NewGuid().ToByteArray()).ToArray())),
                binding.Id, revision, createdAt, Now.AddDays(3));
            var submission = attempt.SubmitHeadlessProvider(revision, createdAt.AddMinutes(1), null);
            Context.AddRange(attempt, submission, RegistrationProviderSubmissionWriteEffect.Create(attempt, submission, createdAt.AddMinutes(1)));
            await Context.SaveChangesAsync();
            return new(order, requirement, version, section, binding, submission);
        }

        public async Task AddAnswerAsync(Graph graph, string key, DateTime? retentionUntil,
            bool transferable = true, DateTime? sensitiveDeadline = null)
        {
            DateTime createdAt = Now.AddDays(-10);
            int retentionPolicyId = (int)(retentionUntil is null
                ? RegistrationRetentionPolicyEnum.LegalHold : RegistrationRetentionPolicyEnum.StandardOperational);
            var field = RegistrationFormField.Create(Guid.CreateVersion7(), graph.Section, graph.Section.Fields.Count + 1,
                "registration", key, key, RegistrationFieldTypeEnum.ShortText, retentionPolicyId,
                RegistrationOrganizerVisibilityEnum.AuthorizedOrganizers, false, transferable, createdAt);
            graph.Version.AddField(graph.Section, field);
            var protectedValue = Protector.Protect("private-" + key);
            var sensitive = RegistrationSensitiveAnswerValue.Create(fixture.TenantId, protectedValue.Ciphertext, protectedValue.KeyVersion,
                retentionPolicyId, createdAt, sensitiveDeadline ?? retentionUntil);
            var answer = RegistrationAnswer.CreateSensitive(graph.Submission, field, graph.Requirement,
                RegistrationAnswerSubjectTypeEnum.RegistrationOrder, graph.Order.Id, 1, sensitive, createdAt, anonymousUpperBoundUtc: retentionUntil);
            Context.RegistrationAnswers.Add(answer);
            await Context.SaveChangesAsync();
        }

        public async Task<RegistrationRetentionCleanupResult> CleanupAsync()
        {
            await using var scope = fixture.CreateScope();
            return await scope.ServiceProvider.GetRequiredService<IRegistrationRetentionCleanupRepository>()
                .CleanupTenantAsync(fixture.TenantId, clock.GetUtcNow().UtcDateTime, 1, CancellationToken.None);
        }

        public void SetUtcNow(DateTime now) => clock.UtcNow = now;

        public async Task<int> DrainAsync(Func<Task>? afterHandoff = null)
        {
            var storage = Substitute.For<IFileStorageProvider>();
            storage.WriteAsync(Arg.Any<FileStorageWriteInput>(), Arg.Any<CancellationToken>()).Returns(async call =>
            {
                var input = call.ArgAt<FileStorageWriteInput>(0)!;
                using var reader = new StreamReader(input.Content, Encoding.UTF8, leaveOpen: true);
                Csv = await reader.ReadToEndAsync();
                StorageCalls++;
                if (afterHandoff is not null)
                {
                    await afterHandoff();
                }
                return new FileStorageWriteResult(StorageProviders.Local, input.ObjectKey!, input.ExpectedSizeBytes!.Value,
                    input.ContentType, "sha256:retention");
            });
            var resolver = Substitute.For<IFileStorageProviderResolver>();
            resolver.GetRequired(StorageProviders.Local).Returns(storage);
            var sink = new CsvRegistrationProviderSubmissionSink(resolver, fixture.Services.GetRequiredService<IStorageObjectRepository>(), clock);
            var handler = new DrainRegistrationProviderSubmissionWriteEffectsCommandHandler(
                new RegistrationProviderSubmissionWriteEffectRepository(Context), new RegistrationProviderRegistry([sink]),
                fixture.Services.GetRequiredService<ITenantContextAccessor>(), Protector, clock);
            return await handler.Handle(new("retention-provider", 10), CancellationToken.None);
        }

        public ValueTask DisposeAsync() => fixture.DisposeAsync();
    }

    private sealed class Clock(DateTime now) : TimeProvider
    {
        public DateTime UtcNow { get; set; } = now;
        public override DateTimeOffset GetUtcNow() => new(UtcNow);
    }

    private sealed class CountingProtector(IRegistrationSensitiveValueProtector inner) : IRegistrationSensitiveValueProtector
    {
        public int UnprotectCalls { get; private set; }
        public RegistrationProtectedValue Protect(string plaintext) => inner.Protect(plaintext);
        public string Unprotect(string ciphertext, int keyVersion)
        {
            UnprotectCalls++;
            return inner.Unprotect(ciphertext, keyVersion);
        }
    }
}
