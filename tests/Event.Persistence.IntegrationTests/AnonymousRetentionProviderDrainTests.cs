using System.Runtime.ExceptionServices;
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
using Explore.Infrastructure.Storage;
using Explore.Infrastructure.Services.Registration.Providers.SubmissionSinks;
using Explore.Persistence.Repositories;
using Explore.Persistence;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using NSubstitute;

namespace Event.Persistence.IntegrationTests;

public sealed class AnonymousRetentionProviderDrainTests
{
    private static readonly DateTime Now = new(2027, 1, 8, 14, 0, 0, DateTimeKind.Utc);

    [Test]
    [Arguments("expired")]
    [Arguments("reclaimed")]
    [Arguments("completed")]
    [Arguments("parked")]
    [Arguments("retry")]
    [Arguments("dead-lettered")]
    public async Task LostOriginalClaimCannotAcquireSensitiveDeliveryOrInvokeSink(string loss)
    {
        await using var harness = await Harness.CreateAsync(Now);
        var graph = await harness.SeedAsync(Now.AddDays(1), anonymous: true);
        await harness.AddAnswerAsync(graph, "name", Now.AddHours(1));
        harness.Context.ChangeTracker.Clear();
        var repository = harness.CreateRepository();
        var claim = (await repository.ClaimDueAsync("original", 1, Now,
            TimeSpan.FromMinutes(1), CancellationToken.None)).Single();
        await harness.LoseClaimAsync(claim, loss);

        await Assert.That(await repository.GetDeliveryAsync(claim, CancellationToken.None)).IsNull();
        await Assert.That(await harness.DrainClaimAsync(claim)).IsFalse();
        await Assert.That(harness.Protector.UnprotectCalls).IsEqualTo(0);
        await Assert.That(harness.StorageCalls).IsEqualTo(0);
        await Assert.That(await harness.Context.RegistrationSensitiveAnswerValues.CountAsync()).IsEqualTo(1);
    }

    [Test]
    public async Task GenuineClaimCannotSubstituteAnotherInternallyConsistentDeliveryGraph()
    {
        await using var harness = await Harness.CreateAsync(Now);
        var first = await harness.SeedAsync(Now.AddDays(1), anonymous: true);
        var second = await harness.SeedAsync(Now.AddDays(1), anonymous: true);
        await harness.AddAnswerAsync(first, "name", Now.AddHours(1));
        await harness.AddAnswerAsync(second, "included", Now.AddHours(1));
        harness.Context.ChangeTracker.Clear();
        var repository = harness.CreateRepository();
        var claims = await repository.ClaimDueAsync("original", 10, Now,
            TimeSpan.FromMinutes(1), CancellationToken.None);
        var claim = claims.Single(value => value.RegistrationSubmissionId == first.Submission.Id);
        var substituted = claim with
        {
            RegistrationSubmissionId = second.Submission.Id,
            RegistrationAttemptId = second.Submission.RegistrationAttemptId,
            RegistrationProviderBindingId = second.Binding.Id
        };

        await Assert.That(await repository.GetDeliveryAsync(substituted, CancellationToken.None)).IsNull();
        await Assert.That(await harness.DrainClaimAsync(substituted)).IsFalse();
        await Assert.That(await repository.GetDeliveryAsync(claim, CancellationToken.None)).IsNotNull();
        await Assert.That(harness.Protector.UnprotectCalls).IsEqualTo(0);
        await Assert.That(harness.StorageCalls).IsEqualTo(0);
    }

    [Test]
    [Arguments("token")]
    [Arguments("fence")]
    [Arguments("expiry")]
    public async Task AlteredClaimIdentityCannotAcquireDelivery(string changed)
    {
        await using var harness = await Harness.CreateAsync(Now);
        var graph = await harness.SeedAsync(Now.AddDays(1), anonymous: true);
        await harness.AddAnswerAsync(graph, "name", Now.AddHours(1));
        harness.Context.ChangeTracker.Clear();
        var repository = harness.CreateRepository();
        var claim = (await repository.ClaimDueAsync("original", 1, Now,
            TimeSpan.FromMinutes(1), CancellationToken.None)).Single();
        var altered = changed switch
        {
            "token" => claim with { LeaseToken = Guid.CreateVersion7() },
            "fence" => claim with { ProcessingFence = claim.ProcessingFence + 1 },
            _ => claim with { LeaseExpiresAt = claim.LeaseExpiresAt.AddSeconds(1) }
        };

        await Assert.That(await repository.GetDeliveryAsync(altered, CancellationToken.None)).IsNull();
        await Assert.That(await harness.DrainClaimAsync(altered)).IsFalse();
        await Assert.That(harness.StorageCalls).IsEqualTo(0);
    }

    [Test]
    [Arguments("expired")]
    [Arguments("reclaimed")]
    [Arguments("completed")]
    [Arguments("source-deleted")]
    [Arguments("binding-changed")]
    [Timeout(120_000)]
    public async Task CustodyLostDuringPreparationCannotCrossProviderBoundary(string loss)
    {
        await using var harness = await Harness.CreateAsync(Now);
        var graph = await harness.SeedAsync(Now.AddDays(1), anonymous: true);
        await harness.AddAnswerAsync(graph, "name", Now.AddHours(1));
        harness.Context.ChangeTracker.Clear();
        var claim = (await harness.CreateRepository().ClaimDueAsync("original", 1, Now,
            TimeSpan.FromMinutes(1), CancellationToken.None)).Single();
        var preparing = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var resume = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        harness.Protector.BeforeUnprotect = () =>
        {
            preparing.TrySetResult();
            resume.Task.WaitAsync(TimeSpan.FromSeconds(10)).GetAwaiter().GetResult();
        };
        // Subscribe before triggering a separate worker; no shared DbContext runs concurrently.
        Task<bool> draining = Task.Run(() => harness.DrainClaimAsync(claim));
        await RunDuringProviderHandoffAsync(preparing.Task, resume, draining, async () =>
        {
            if (loss == "source-deleted")
            {
                await harness.Context.RegistrationSubmissions.Where(value => value.Id == graph.Submission.Id)
                    .ExecuteUpdateAsync(setters => setters.SetProperty(value => value.IsDeleted, true));
            }
            else if (loss == "binding-changed")
            {
                await harness.Context.RegistrationProviderBindings.Where(value => value.Id == graph.Binding.Id)
                    .ExecuteUpdateAsync(setters => setters.SetProperty(value => value.ConcurrencyStamp, Guid.CreateVersion7()));
            }
            else
            {
                await harness.LoseClaimAsync(claim, loss);
            }
        });

        await Assert.That(await draining).IsFalse();
        await Assert.That(harness.Protector.UnprotectCalls).IsEqualTo(1);
        await Assert.That(harness.StorageCalls).IsEqualTo(0);
        await Assert.That(await harness.Context.StorageObjects.CountAsync()).IsEqualTo(0);
    }

    [Test]
    public async Task PersistedEffectLineageMustMatchBeforeSensitiveAcquisition()
    {
        await using var harness = await Harness.CreateAsync(Now);
        var first = await harness.SeedAsync(Now.AddDays(1), anonymous: true);
        var second = await harness.SeedAsync(Now.AddDays(1), anonymous: true);
        await harness.AddAnswerAsync(first, "name", Now.AddHours(1));
        harness.Context.ChangeTracker.Clear();
        var repository = harness.CreateRepository();
        var claims = await repository.ClaimDueAsync("original", 10, Now,
            TimeSpan.FromMinutes(1), CancellationToken.None);
        var claim = claims.Single(value => value.RegistrationSubmissionId == first.Submission.Id);
        // Preserve the effect's composite order/Event foreign key while breaking
        // its native submission lineage, rather than testing a rejected FK write.
        await harness.Context.RegistrationProviderSubmissionWriteEffects.Where(value => value.Id == claim.EffectId)
            .ExecuteUpdateAsync(setters => setters
                .SetProperty(value => value.RegistrationOrderId, second.Order.Id)
                .SetProperty(value => value.EventId, second.Order.EventId));

        await Assert.That(await repository.GetDeliveryAsync(claim, CancellationToken.None)).IsNull();
        await Assert.That(await harness.DrainClaimAsync(claim)).IsFalse();
        await Assert.That(harness.Protector.UnprotectCalls).IsEqualTo(0);
        await Assert.That(harness.StorageCalls).IsEqualTo(0);
    }

    [Test]
    public async Task DeliveryAcquisitionCannotBorrowAnExistingTransactionForProviderWork()
    {
        await using var harness = await Harness.CreateAsync(Now);
        var graph = await harness.SeedAsync(Now.AddDays(1), anonymous: true);
        await harness.AddAnswerAsync(graph, "name", Now.AddHours(1));
        harness.Context.ChangeTracker.Clear();
        var repository = harness.CreateRepository();
        var claim = (await repository.ClaimDueAsync("original", 1, Now,
            TimeSpan.FromMinutes(1), CancellationToken.None)).Single();
        await using var transaction = await harness.Context.Database.BeginTransactionAsync();

        await Assert.That(await repository.GetDeliveryAsync(claim, CancellationToken.None)).IsNull();
        await Assert.That(harness.Protector.UnprotectCalls).IsEqualTo(0);
        await Assert.That(harness.StorageCalls).IsEqualTo(0);
    }

    [Test]
    public async Task NativeDrainClaimsGloballyThenConstructsIndependentTenantDelivery()
    {
        await using var harness = await Harness.CreateAsync(Now);
        var graph = await harness.SeedAsync(Now.AddDays(1), anonymous: true);
        await harness.AddAnswerAsync(graph, "name", Now.AddHours(1));
        harness.Context.ChangeTracker.Clear();

        await Assert.That(await harness.DrainAsync()).IsEqualTo(1);
        await Assert.That(harness.DependencyTenant).IsEqualTo(graph.Submission.TenantId);
        await Assert.That(harness.DependencyContextTenant).IsEqualTo(graph.Submission.TenantId);
        await Assert.That(harness.DependencyContextWasIndependent).IsTrue();
        await Assert.That(harness.ProviderHadTransaction).IsFalse();
        await Assert.That(harness.StorageCalls).IsEqualTo(1);
        await Assert.That(harness.Protector.UnprotectCalls).IsEqualTo(1);
        await Assert.That(await harness.DrainAsync()).IsEqualTo(0);
        await Assert.That(harness.StorageCalls).IsEqualTo(1);
        await Assert.That(harness.Protector.UnprotectCalls).IsEqualTo(1);
    }

    [Test]
    public async Task SelectedTenantIsBoundBeforeSinkConstructionAndCompletedReplayDoesNotDiscloseAgain()
    {
        await using var harness = await Harness.CreateAsync(Now);
        var graph = await harness.SeedAsync(Now.AddDays(1), anonymous: true);
        await harness.AddAnswerAsync(graph, "name", Now.AddHours(1));
        harness.Context.ChangeTracker.Clear();
        var claim = (await harness.CreateRepository().ClaimDueAsync("original", 1, Now,
            TimeSpan.FromMinutes(1), CancellationToken.None)).Single();

        await Assert.That(await harness.DrainClaimAsync(claim)).IsTrue();
        await Assert.That(harness.DependencyTenant).IsEqualTo(claim.TenantId);
        await Assert.That(harness.DependencyContextTenant).IsEqualTo(claim.TenantId);
        await Assert.That(harness.DependencyContextWasIndependent).IsTrue();
        await Assert.That(harness.ProviderHadTransaction).IsFalse();
        await Assert.That(await harness.DrainClaimAsync(claim)).IsFalse();
        await Assert.That(await harness.DrainAsync()).IsEqualTo(0);
        await Assert.That(harness.StorageCalls).IsEqualTo(1);
        await Assert.That(harness.Protector.UnprotectCalls).IsEqualTo(1);
        await Assert.That(harness.Csv).Contains("private-name");
    }

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
        var repository = harness.CreateRepository();
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
    [Timeout(120_000)]
    public async Task CleanupDuringProviderHandoffPreservesTheAmbiguousOutcome()
    {
        CancellationToken cancellationToken = TestContext.Current!.Execution.CancellationToken;
        await using var harness = await Harness.CreateAsync(Now);
        var graph = await harness.SeedAsync(Now.AddDays(1), anonymous: true);
        await harness.AddAnswerAsync(graph, "name", Now.AddTicks(1));
        harness.Context.ChangeTracker.Clear();
        var handedOff = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var releaseProvider = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        Task<int> draining = harness.DrainAsync(async () =>
        {
            handedOff.SetResult();
            // Cleanup owns this pause; its database work is not a provider signal deadline.
            await releaseProvider.Task.WaitAsync(cancellationToken);
            throw new RegistrationProviderSubmissionDeliveryException(
                RegistrationProviderSubmissionDeliveryFailureKind.AmbiguousAfterHandoff, "provider_handoff_uncertain");
        });

        await RunDuringProviderHandoffAsync(handedOff.Task, releaseProvider, draining, async () =>
        {
            harness.SetUtcNow(Now.AddTicks(1));
            await Assert.That((await harness.CleanupAsync(cancellationToken)).AnswersDeleted).IsEqualTo(1);
            var inFlight = await harness.Context.RegistrationProviderSubmissionWriteEffects.AsNoTracking().SingleAsync(cancellationToken);
            await Assert.That(inFlight.Status).IsEqualTo(OutboxMessageStatus.Processing);
            await Assert.That(inFlight.DeadLetteredAt).IsNull();
        });

        var effect = await harness.Context.RegistrationProviderSubmissionWriteEffects.AsNoTracking().SingleAsync();
        await Assert.That(effect.FailureCode).IsEqualTo("provider_handoff_uncertain");
        await Assert.That(effect.ParkedAt).IsEqualTo(Now.AddTicks(1));
        await Assert.That(effect.DeadLetteredAt).IsNull();
        await Assert.That(harness.StorageCalls).IsEqualTo(1);
    }

    [Test]
    [Arguments(false)]
    [Arguments(true)]
    public async Task ProviderHandoffCoordinationPreservesCleanupFailure(bool providerFails)
    {
        var handedOff = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var releaseProvider = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var cleanupSuspended = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var resumeCleanup = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var cleanupFailure = new InvalidOperationException("controlled cleanup failure");
        var providerFailure = new InvalidOperationException("controlled provider failure");
        async Task DrainAsync()
        {
            handedOff.SetResult();
            await releaseProvider.Task;
            if (providerFails) throw providerFailure;
        }

        Task draining = DrainAsync();
        Task coordinating = RunDuringProviderHandoffAsync(handedOff.Task, releaseProvider, draining, async () =>
        {
            cleanupSuspended.SetResult();
            await resumeCleanup.Task.WaitAsync(TimeSpan.FromSeconds(10));
            throw cleanupFailure;
        });
        try
        {
            await cleanupSuspended.Task.WaitAsync(TimeSpan.FromSeconds(10));
            await Assert.That(draining.IsCompleted).IsFalse();
        }
        finally
        {
            resumeCleanup.TrySetResult();
        }

        Exception? observed = null;
        try { await coordinating.WaitAsync(TimeSpan.FromSeconds(10)); }
        catch (Exception exception) { observed = exception; }
        await Assert.That(releaseProvider.Task.IsCompletedSuccessfully).IsTrue();
        if (providerFails)
        {
            await Assert.That(observed).IsTypeOf<AggregateException>();
            var failures = ((AggregateException)observed!).InnerExceptions;
            await Assert.That(failures.Count).IsEqualTo(2);
            await Assert.That(failures[0]).IsSameReferenceAs(cleanupFailure);
            await Assert.That(failures[1]).IsSameReferenceAs(providerFailure);
        }
        else
        {
            await Assert.That(observed).IsSameReferenceAs(cleanupFailure);
        }
    }

    private static async Task RunDuringProviderHandoffAsync(
        Task handedOff, TaskCompletionSource releaseProvider, Task draining, Func<Task> whileInFlight)
    {
        ExceptionDispatchInfo? inFlightFailure = null;
        try
        {
            await handedOff.WaitAsync(TimeSpan.FromSeconds(10));
            await whileInFlight();
        }
        catch (Exception exception)
        {
            inFlightFailure = ExceptionDispatchInfo.Capture(exception);
        }
        finally
        {
            releaseProvider.TrySetResult();
        }

        try
        {
            await draining.WaitAsync(TimeSpan.FromSeconds(10));
        }
        catch (Exception exception) when (inFlightFailure is not null)
        {
            throw new AggregateException(inFlightFailure.SourceException, exception);
        }

        inFlightFailure?.Throw();
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
        var repository = harness.CreateRepository();
        var claims = await repository.ClaimDueAsync("lineage", 10, Now, TimeSpan.FromMinutes(1), CancellationToken.None);
        var claim = claims.Single(value => value.RegistrationSubmissionId == first.Submission.Id);

        await Assert.That(await repository.GetDeliveryAsync(claim with { RegistrationSubmissionId = second.Submission.Id }, CancellationToken.None)).IsNull();
        await Assert.That(await repository.GetDeliveryAsync(claim with { TenantId = Guid.CreateVersion7() }, CancellationToken.None)).IsNull();
        await Assert.That(await repository.GetDeliveryAsync(claim, CancellationToken.None)).IsNotNull();
    }

    private sealed record Graph(RegistrationOrder Order, RegistrationRequirement Requirement, RegistrationFormVersion Version,
        RegistrationFormSection Section, RegistrationProviderBinding Binding, RegistrationSubmission Submission);

    [Test]
    [Arguments("completed")]
    [Arguments("dead-lettered")]
    [Arguments("parked")]
    [Arguments("retry")]
    public async Task DeliverySettlementFencesCsvAndPreservesUnresolvedAuthority(string outcome)
    {
        await using var harness = await Harness.CreateAsync(Now);
        var graph = await harness.SeedAsync(Now.AddDays(1), anonymous: true);
        await harness.AddAnswerAsync(graph, "name", Now.AddHours(1));
        var storage = await harness.SeedArtifactAsync(graph);
        var repository = harness.CreateRepository();
        var claim = (await repository.ClaimDueAsync("settlement", 1, Now,
            TimeSpan.FromMinutes(1), CancellationToken.None)).Single();
        Guid before = await harness.Context.StorageObjects.AsNoTracking()
            .Where(item => item.Id == storage.Id).Select(item => item.ConcurrencyStamp).SingleAsync();

        bool settled = outcome switch
        {
            "completed" => await repository.CompleteAsync(claim, Now, CancellationToken.None),
            "dead-lettered" => await repository.DeadLetterAsync(claim, "rejected", Now, CancellationToken.None),
            "parked" => await repository.ParkAmbiguousAsync(claim, "uncertain", Now, CancellationToken.None),
            _ => await repository.RetryAsync(claim, "unavailable", Now.AddMinutes(1), Now, CancellationToken.None)
        };

        await Assert.That(settled).IsTrue();
        Guid after = await harness.Context.StorageObjects.AsNoTracking()
            .Where(item => item.Id == storage.Id).Select(item => item.ConcurrencyStamp).SingleAsync();
        await Assert.That(after).IsNotEqualTo(before);
        await using var transaction = await harness.Context.Database.BeginTransactionAsync();
        await Assert.That(await new StorageObjectReferenceRepository(harness.Context)
            .HasBlockingHoldsAsync(storage.Id, Now.AddDays(2), CancellationToken.None))
            .IsEqualTo(outcome is "parked" or "retry");
        await Assert.That(harness.StorageCalls).IsEqualTo(0);
    }

    private sealed class Harness(EventVisitorCapabilitySqliteFixture fixture, Clock clock) : IAsyncDisposable
    {
        public Explore.Persistence.ExploreDbContext Context => fixture.Context;
        public CountingProtector Protector { get; } = new(fixture.Services.GetRequiredService<IRegistrationSensitiveValueProtector>());
        public int StorageCalls { get; private set; }
        public string Csv { get; private set; } = string.Empty;
        public Guid? DependencyTenant { get; private set; }
        public Guid? DependencyContextTenant { get; private set; }
        public bool DependencyContextWasIndependent { get; private set; }
        public bool ProviderHadTransaction { get; private set; }
        private Func<Task>? _afterHandoff;

        public static async Task<Harness> CreateAsync(DateTime now)
        {
            var clock = new Clock(now);
            Harness? harness = null;
            var fixture = await EventVisitorCapabilitySqliteFixture.CreateAsync(services =>
            {
                services.AddSingleton<TimeProvider>(clock);
                services.AddScoped<IRegistrationProviderRegistry>(provider => harness!.CreateProviderRegistry(provider));
                services.AddScoped(provider => new RegistrationProviderSubmissionWriteEffectDeliveryService(
                    provider.GetRequiredService<IRegistrationProviderSubmissionWriteEffectRepository>(),
                    provider.GetRequiredService<IRegistrationProviderRegistry>(), harness!.Protector, clock));
            });
            harness = new Harness(fixture, clock);
            return harness;
        }

        public RegistrationProviderSubmissionWriteEffectRepository CreateRepository() => new(Context, clock);

        public async Task<bool> DrainClaimAsync(RegistrationProviderSubmissionWriteClaim claim)
        {
            await using var scope = await fixture.Services
                .GetRequiredService<IRegistrationProviderSubmissionWriteExecutionScopeFactory>()
                .CreateAsync(claim.TenantId, CancellationToken.None);
            return await scope.DrainAsync(claim, CancellationToken.None);
        }

        public async Task LoseClaimAsync(RegistrationProviderSubmissionWriteClaim claim, string loss)
        {
            var repository = CreateRepository();
            switch (loss)
            {
                case "expired":
                    SetUtcNow(claim.LeaseExpiresAt);
                    break;
                case "reclaimed":
                    SetUtcNow(claim.LeaseExpiresAt);
                    Context.ChangeTracker.Clear();
                    var replacement = (await repository.ClaimDueAsync("replacement", 1, claim.LeaseExpiresAt,
                        TimeSpan.FromMinutes(1), CancellationToken.None)).Single();
                    await Assert.That(replacement.ProcessingFence).IsGreaterThan(claim.ProcessingFence);
                    break;
                case "completed":
                    await Assert.That(await repository.CompleteAsync(claim, Now, CancellationToken.None)).IsTrue();
                    break;
                case "parked":
                    await Assert.That(await repository.ParkAmbiguousAsync(claim, "uncertain", Now, CancellationToken.None)).IsTrue();
                    break;
                case "retry":
                    await Assert.That(await repository.RetryAsync(claim, "unavailable", Now.AddMinutes(1), Now, CancellationToken.None)).IsTrue();
                    break;
                case "dead-lettered":
                    await Assert.That(await repository.DeadLetterAsync(claim, "rejected", Now, CancellationToken.None)).IsTrue();
                    break;
                default:
                    throw new ArgumentOutOfRangeException(nameof(loss));
            }
        }

        public async Task<Graph> SeedAsync(DateTime? deadline, bool anonymous)
        {
            DateTime createdAt = Now.AddDays(-10);
            var target = await fixture.SeedEventAsync();
            var ticket = await fixture.SeedTicketAsync(target.Id);
            var workflow = RegistrationWorkflow.Create(fixture.TenantId, target.Id, "RETENTION", createdAt);
            var requirement = RegistrationRequirement.Create(workflow, 1, RegistrationRequirementCriticalityEnum.Required, false,
                RegistrationRequirementCompletionEffectEnum.BlocksRegistration, RegistrationAnswerSyncModeEnum.FULL_SYNC,
                RegistrationRequirementSubjectTypeEnum.AllOrders, null, createdAt);
            workflow.AddRequirement(requirement);
            var form = RegistrationForm.Create(fixture.TenantId, target.Id, "registration", "retention", "Retention", createdAt);
            var version = RegistrationFormVersion.Create(form, 1, "en", null, null, createdAt);
            var section = RegistrationFormSection.Create(Guid.CreateVersion7(), version, 1, "Details", createdAt);
            version.AddSection(section);
            form.AddVersion(version);
            var tuple = CsvRegistrationProviderSubmissionSink.SupportedTuple;
            var connection = await Context.RegistrationProviderConnections.SingleOrDefaultAsync(value =>
                value.TenantId == fixture.TenantId && value.ProviderCode == tuple.ProviderCode
                && value.ProviderWorkspaceId == StorageProviders.Local);
            if (connection is null)
            {
                connection = RegistrationProviderConnection.Create(fixture.TenantId, "CSV " + target.Id.ToString("N"), RegistrationProviderKindEnum.ExternalApi,
                    RegistrationProviderDeploymentKindEnum.HostedSaas, tuple.ProviderCode, tuple.ProviderDeploymentCode, tuple.ApiVersion,
                    tuple.AdapterPolicyVersion, tuple.ConformanceEvidenceRevision, "https://8.8.8.8", "https://8.8.8.8",
                    StorageProviders.Local, null, null, createdAt);
                Context.Add(connection);
            }
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
            Context.AddRange(workflow, form, binding, order);
            await Context.SaveChangesAsync();
            var attempt = RegistrationAttempt.Create(fixture.TenantId, target.Id, order.Id, workflow.Id, requirement.Id, channel.Id,
                form.Id, version.Id, CapabilityTokenHash.Create(Convert.ToBase64String(Guid.NewGuid().ToByteArray().Concat(Guid.NewGuid().ToByteArray()).ToArray())),
                binding.Id, revision, createdAt, Now.AddDays(3));
            var submission = attempt.SubmitHeadlessProvider(revision, createdAt.AddMinutes(1), null);
            Context.AddRange(attempt, submission, RegistrationProviderSubmissionWriteEffect.Create(attempt, submission, createdAt.AddMinutes(1)));
            // Validate against the persisted owners before INSERT generates these FK components.
            // Both properties remain store-generated; these values do not override the database.
            Context.Entry(attempt).Property<Guid>("RegistrationProviderBindingKey").CurrentValue =
                Context.Entry(channel).Property<Guid>("RegistrationProviderBindingKey").CurrentValue;
            Context.Entry(attempt).Property<string>("ProviderMappingRevisionHashKey").CurrentValue =
                binding.PublishedMappingRevisionHashKey;
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
            // Seeding another event can detach this version. Enroll the new field
            // explicitly so answer lineage never depends on tracked navigation fixup.
            Context.RegistrationFormFields.Add(field);
            var protectedValue = Protector.Protect("private-" + key);
            var sensitive = RegistrationSensitiveAnswerValue.Create(fixture.TenantId, protectedValue.Ciphertext, protectedValue.KeyVersion,
                retentionPolicyId, createdAt, sensitiveDeadline ?? retentionUntil);
            var answer = RegistrationAnswer.CreateSensitive(graph.Submission, field, graph.Requirement,
                RegistrationAnswerSubjectTypeEnum.RegistrationOrder, graph.Order.Id, 1, sensitive, createdAt, anonymousUpperBoundUtc: retentionUntil);
            Context.RegistrationAnswers.Add(answer);
            await Context.SaveChangesAsync();
        }

        public async Task<RegistrationRetentionCleanupResult> CleanupAsync(CancellationToken cancellationToken = default)
        {
            await using var scope = fixture.CreateScope();
            return await scope.ServiceProvider.GetRequiredService<IRegistrationRetentionCleanupRepository>()
                .CleanupTenantAsync(fixture.TenantId, clock.GetUtcNow().UtcDateTime, 1, cancellationToken);
        }

        public void SetUtcNow(DateTime now) => clock.UtcNow = now;

        public async Task<StorageObject> SeedArtifactAsync(Graph graph)
        {
            new Explore.Application.Services.Registration.FormSchemaArtifactPublicationService(
                new Explore.Application.Services.Registration.FormSchemaArtifactGenerator()).Publish(graph.Version, Now);
            var binding = await fixture.Services.GetRequiredService<IStorageProviderBindingService>()
                .CaptureAsync(StorageProviders.Local, graph.Order.TenantId, CancellationToken.None);
            var storage = new StorageObject
            {
                Id = Guid.CreateVersion7(),
                TenantId = graph.Order.TenantId,
                Tenant = null!,
                FileTypeId = (int)FileTypeEnum.Document,
                FileType = null!,
                Provider = StorageProviders.Local,
                StorageProviderBindingId = binding.Id,
                ObjectKey = $"retention-test/{Guid.CreateVersion7():N}.csv",
                FullName = "answer.csv",
                SafeDisplayName = "answer.csv",
                Extension = ".csv",
                ContentType = "text/csv",
                Visibility = StorageObjectVisibilities.AuthenticatedTenant,
                Purpose = StorageObjectPurposes.Document,
                LifecycleState = StorageObjectLifecycleStates.Active,
                OwningResourceKind = "registration_submission_sink",
                OwningResourceId = graph.Submission.Id,
                RegistrationContentRetentionUntilUtc = Now,
                ConcurrencyStamp = Guid.CreateVersion7()
            };
            Context.Add(storage);
            await Context.SaveChangesAsync();
            Context.ChangeTracker.Clear();
            return storage;
        }

        public Task<int> DrainAsync(Func<Task>? afterHandoff = null)
        {
            _afterHandoff = afterHandoff;
            return fixture.Services.GetRequiredService<
                Explore.Application.Contracts.Operations.ICommandHandler<DrainRegistrationProviderSubmissionWriteEffectsCommand, int>>()
                .ExecuteAsync(new("retention-provider", 10), CancellationToken.None);
        }

        private IRegistrationProviderRegistry CreateProviderRegistry(IServiceProvider services)
        {
            var context = services.GetRequiredService<ExploreDbContext>();
            DependencyTenant = services.GetRequiredService<ITenantContextAccessor>().TenantId;
            DependencyContextTenant = context.TenantFilterTenantId;
            DependencyContextWasIndependent = !ReferenceEquals(context, Context);
            var storage = Substitute.For<IFileStorageProvider>();
            storage.Provider.Returns(StorageProviders.Local);
            storage.WriteAsync(Arg.Any<FileStorageWriteInput>(), Arg.Any<CancellationToken>()).Returns(async call =>
            {
                var input = call.ArgAt<FileStorageWriteInput>(0)!;
                using var reader = new StreamReader(input.Content, Encoding.UTF8, leaveOpen: true);
                Csv = await reader.ReadToEndAsync();
                StorageCalls++;
                ProviderHadTransaction = context.Database.CurrentTransaction is not null;
                if (_afterHandoff is not null)
                {
                    await _afterHandoff();
                }
                return new FileStorageWriteResult(StorageProviders.Local, input.ObjectKey!, input.ExpectedSizeBytes!.Value,
                    input.ContentType, new string('a', 64));
            });
            var actualBindings = services.GetRequiredService<IStorageProviderBindingService>();
            var bindings = Substitute.For<IStorageProviderBindingService>();
            bindings.CaptureAsync(StorageProviders.Local, fixture.TenantId, Arg.Any<CancellationToken>())
                .Returns(call => actualBindings.CaptureAsync(StorageProviders.Local, fixture.TenantId,
                    call.ArgAt<CancellationToken>(2)));
            bindings.ResolveAsync(Arg.Any<Guid>(), Arg.Any<CancellationToken>()).Returns(async call =>
            {
                await actualBindings.ResolveAsync(call.ArgAt<Guid>(0), call.ArgAt<CancellationToken>(1));
                return storage;
            });
            var producers = services.GetRequiredService<IStorageProducerOperationRepository>();
            var unitOfWork = services.GetRequiredService<IUnitOfWork>();
            var sink = new CsvRegistrationProviderSubmissionSink(
                new ManagedStorageProducer(bindings, producers, unitOfWork), producers, unitOfWork, clock);
            return new RegistrationProviderRegistry([sink]);
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
        public Action? BeforeUnprotect { get; set; }
        public RegistrationProtectedValue Protect(string plaintext) => inner.Protect(plaintext);
        public string Unprotect(string ciphertext, int keyVersion)
        {
            UnprotectCalls++;
            BeforeUnprotect?.Invoke();
            return inner.Unprotect(ciphertext, keyVersion);
        }
    }
}
