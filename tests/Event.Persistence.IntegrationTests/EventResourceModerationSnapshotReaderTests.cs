using System.Data;
using System.Collections.Immutable;
using System.Security.Claims;
using Event.Persistence.IntegrationTests.Fixtures;
using Event.Persistence.IntegrationTests.Moderation;
using Explore.Application.Authentication;
using Explore.Application.DTOs.StorageObject;
using Explore.Application.Exceptions;
using Explore.Application.Responses;
using Explore.Application.Contracts.Infrastructure;
using Explore.Application.Contracts.Persistence;
using Explore.Application.Contracts.Services;
using Explore.Application.Features.Moderation.Policy;
using Explore.Application.Services.Moderation;
using Explore.Application.Services;
using Explore.Domain;
using Explore.Domain.Enums;
using Explore.Infrastructure.Identity;
using Explore.Persistence;
using Explore.Persistence.Database;
using Explore.Persistence.Repositories;
using Explore.Persistence.Services;
using ISLAMU.Moderation.Domain;
using ISLAMU.Moderation.Domain.Policies;
using Microsoft.AspNetCore.Http;
using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Diagnostics;
using Microsoft.Extensions.Logging.Abstractions;

namespace Event.Persistence.IntegrationTests;

/// <summary>
/// Exercises persisted overlay facts, not production publication readiness or consumer conformance.
/// Each test owns a complete native EF model and an isolated SQLite database.
/// </summary>
public sealed class EventResourceModerationSnapshotReaderTests
{
    private static readonly DateTimeOffset Now = new(2026, 10, 9, 12, 0, 0, TimeSpan.Zero);

    [Test]
    public async Task Installation_without_adoption_preserves_native_evaluation()
    {
        await using var database = await ReaderDatabase.CreateAsync();
        await database.EstablishBindingAsync();

        var facts = await database.ReadAsync(database.Request());

        await Assert.That(facts).IsEqualTo(EventResourceModerationFacts.NotAdopted);
        await Assert.That(facts.PermitsNativeEvaluationAt(Now)).IsTrue();
    }

    [Test]
    public async Task Unsupported_declared_read_profile_still_requires_current_canonical_provenance()
    {
        await using var database = await ReaderDatabase.CreateAsync();
        await database.StorePolicyAsync(database.TenantScope, supported: false);

        var facts = await database.ReadAsync(database.Request());

        await AssertUnavailable(facts);
    }

    [Test]
    public async Task Draft_adoption_does_not_create_a_restrictive_effect_for_a_qualified_subject()
    {
        await using var database = await ReaderDatabase.CreateAsync();
        await database.EstablishBindingAsync();
        await database.StorePolicyAsync(database.TenantScope, supported: false);
        var facts = await database.ReadAsync(database.Request());
        await Assert.That(facts.IsAdopted).IsTrue();
        await Assert.That(facts.Subject).IsEqualTo(database.ServiceSubject);
        await Assert.That(facts.Outcome).IsEqualTo(ModerationRestrictionOutcome.Unrestricted);
        await Assert.That(facts.PermitsNativeEvaluationAt(Now)).IsTrue();
    }

    [Test]
    public async Task Adopted_read_without_canonical_binding_is_unavailable()
    {
        await using var database = await ReaderDatabase.CreateAsync();
        await database.StorePolicyAsync(database.TenantScope);

        await AssertUnavailable(await database.ReadAsync(database.Request()));
    }

    [Test]
    public async Task Adopted_read_without_qualification_epoch_is_unavailable()
    {
        await using var database = await ReaderDatabase.CreateAsync();
        await database.EstablishBindingAsync();
        await database.StorePolicyAsync(database.TenantScope);
        await using (var writer = database.Context())
        {
            await using var transaction = await writer.Database.BeginTransactionAsync(IsolationLevel.Serializable);
            await writer.ModerationQualificationEpochs.Where(row => row.HostBindingId == database.Host.Id)
                .ExecuteDeleteAsync();
            await transaction.CommitAsync();
        }

        await AssertUnavailable(await database.ReadAsync(database.Request()));
    }

    [Test]
    public async Task Current_binding_returns_exact_canonical_service_and_binding_revision()
    {
        await using var database = await ReaderDatabase.CreateAsync();
        var binding = await database.EstablishBindingAsync();
        await database.StorePolicyAsync(database.TenantScope);

        var facts = await database.ReadAsync(database.Request());

        await Assert.That(facts.IsAdopted).IsTrue();
        await Assert.That(facts.Subject).IsEqualTo(database.ServiceSubject);
        await Assert.That(facts.BindingId).IsEqualTo((Guid?)binding.Id);
        await Assert.That(facts.BindingRevision).IsEqualTo((long?)1);
        await Assert.That(facts.Outcome).IsEqualTo(ModerationRestrictionOutcome.Unrestricted);
        await Assert.That(facts.PermitsNativeEvaluationAt(Now)).IsTrue();
        await Assert.That(facts.Generation).IsNotEqualTo("not-adopted");
        await Assert.That(facts.Subject!.Id).IsNotEqualTo(database.AdministratorId);
        await Assert.That(facts.Subject!.Id).IsNotEqualTo(database.Host.MaintenanceServicePrincipalId);
    }

    [Test]
    public async Task Persisted_service_sanction_denies_the_canonical_service()
    {
        await using var database = await ReaderDatabase.CreateAsync();
        var binding = await database.EstablishBindingAsync();
        var policy = await database.StorePolicyAsync(database.TenantScope);
        var before = await database.ReadAsync(database.Request());
        await database.StoreSanctionAsync(database.ServiceSubject, policy);

        var facts = await database.ReadAsync(database.Request());

        await Assert.That(before.Outcome).IsEqualTo(ModerationRestrictionOutcome.Unrestricted);
        await Assert.That(facts.Subject).IsEqualTo(database.ServiceSubject);
        await Assert.That(facts.BindingId).IsEqualTo((Guid?)binding.Id);
        await Assert.That(facts.BindingRevision).IsEqualTo((long?)1);
        await Assert.That(facts.Outcome).IsEqualTo(ModerationRestrictionOutcome.Denied);
        await Assert.That(facts.PermitsNativeEvaluationAt(Now)).IsFalse();
        await Assert.That(facts.Generation).IsNotEqualTo(before.Generation);
    }

    [Test]
    public async Task Retiring_activation_support_does_not_erase_an_existing_resource_sanction()
    {
        await using var database = await ReaderDatabase.CreateAsync();
        await database.EstablishBindingAsync();
        var policy = await database.StorePolicyAsync(database.TenantScope);
        await database.StoreSanctionAsync(database.ServiceSubject, policy);
        var before = await database.ReadAsync(database.Request());
        await database.RetireSupportAsync(policy);
        var after = await database.ReadAsync(database.Request());
        await Assert.That(before.Outcome).IsEqualTo(ModerationRestrictionOutcome.Denied);
        await Assert.That(after.IsAdopted).IsTrue();
        await Assert.That(after.Subject).IsEqualTo(database.ServiceSubject);
        await Assert.That(after.Outcome).IsEqualTo(ModerationRestrictionOutcome.Denied);
        await Assert.That(after.PermitsNativeEvaluationAt(Now)).IsFalse();
        await Assert.That(after.Generation).IsNotEqualTo(before.Generation);
    }

    [Test]
    [Arguments("scope")]
    [Arguments("effect")]
    [Arguments("base-plan")]
    public async Task Missing_selected_profile_children_fail_closed_instead_of_erasing_adoption(string child)
    {
        await using var database = await ReaderDatabase.CreateAsync();
        await database.EstablishBindingAsync();
        var policy = await database.StorePolicyAsync(database.TenantScope);
        await using (var writer = database.Context())
        {
            await using var transaction = await writer.Database.BeginTransactionAsync(IsolationLevel.Serializable);
            writer.ModerationTrustDomainId = database.Host.TrustDomainId;
            switch (child)
            {
                case "scope":
                    await writer.Set<ModerationPolicyPublicationScope>()
                        .Where(row => row.PublicationId == policy.Id).ExecuteDeleteAsync();
                    break;
                case "effect":
                    await writer.Set<ModerationPolicyPublicationEffect>()
                        .Where(row => row.PublicationId == policy.Id).ExecuteDeleteAsync();
                    break;
                case "base-plan":
                    await writer.Set<ModerationPolicyPublicationPlan>()
                        .Where(row => row.PublicationId == policy.Id).ExecuteDeleteAsync();
                    break;
            }
            await transaction.CommitAsync();
        }

        await AssertUnavailable(await database.ReadAsync(database.Request()));
    }

    [Test]
    public async Task Human_owner_sanction_does_not_replace_the_service_subject()
    {
        await using var database = await ReaderDatabase.CreateAsync();
        await database.EstablishBindingAsync();
        var policy = await database.StorePolicyAsync(database.TenantScope);
        await database.StoreSanctionAsync(
            SubjectReference.Create(ModerationSubjectKind.Human, database.AdministratorId, database.InstanceScope),
            policy);

        var facts = await database.ReadAsync(database.Request());

        await Assert.That(facts.Subject).IsEqualTo(database.ServiceSubject);
        await Assert.That(facts.Outcome).IsEqualTo(ModerationRestrictionOutcome.Unrestricted);
        await Assert.That(facts.PermitsNativeEvaluationAt(Now)).IsTrue();
    }

    [Test]
    public async Task Binding_revocation_prevents_subsequent_qualification()
    {
        await using var database = await ReaderDatabase.CreateAsync();
        var binding = await database.EstablishBindingAsync();
        await database.StorePolicyAsync(database.TenantScope);
        // Reuse the same reader to prove that no tracked or cached positive binding survives revocation.
        await using var context = database.Context();
        var reader = database.Reader(context);
        EventResourceModerationFacts before;
        await using (var transaction = await context.Database.BeginTransactionAsync(IsolationLevel.Serializable))
        {
            before = await reader.ReadAsync(database.Request(), Now, CancellationToken.None);
            await transaction.CommitAsync();
        }
        await using (var writer = database.Context())
        {
            await using var transaction = await writer.Database.BeginTransactionAsync(IsolationLevel.Serializable);
            var current = await writer.ModerationMachineSubjectBindings.SingleAsync(row => row.Id == binding.Id);
            current.Revoke(binding.Revision, Now);
            await writer.SaveChangesAsync();
            await transaction.CommitAsync();
        }
        await using var subsequent = await context.Database.BeginTransactionAsync(IsolationLevel.Serializable);
        var after = await reader.ReadAsync(database.Request(), Now, CancellationToken.None);

        await Assert.That(before.Subject).IsEqualTo(database.ServiceSubject);
        await AssertUnavailable(after);
        await Assert.That(after.Generation).IsNotEqualTo(before.Generation);
        await subsequent.CommitAsync();
    }

    [Test]
    [Arguments("owner")]
    [Arguments("tenant")]
    [Arguments("source")]
    public async Task Mismatched_authenticated_coordinates_cannot_qualify_a_binding(string coordinate)
    {
        await using var database = await ReaderDatabase.CreateAsync();
        await database.EstablishBindingAsync();
        await database.StorePolicyAsync(database.TenantScope);
        var request = database.Request();
        var principal = request.MachinePrincipal!;
        request = request with
        {
            MachinePrincipal = coordinate switch
            {
                "owner" => principal with { OwnerId = Guid.CreateVersion7() },
                "tenant" => principal with { AuthorityTenantId = database.TenantId },
                "source" => principal with { CredentialSource = ApiKeyCredentialSource.Configured },
                _ => throw new ArgumentOutOfRangeException(nameof(coordinate))
            }
        };

        await AssertUnavailable(await database.ReadAsync(request));
    }

    [Test]
    [Arguments("resource")]
    [Arguments("tenant")]
    [Arguments("instance")]
    [Arguments("solution")]
    [Arguments("trust-domain")]
    public async Task Rejected_foreign_or_resource_publication_does_not_adopt_local_reads(string coordinate)
    {
        await using var database = await ReaderDatabase.CreateAsync();
        // Deliberately leave the caller unbound: an unrelated active policy must not fail it closed.
        var trust = AuthorityScope.TrustDomain(
            coordinate == "trust-domain" ? Guid.CreateVersion7() : database.Host.TrustDomainId);
        var solution = AuthorityScope.Solution(trust,
            coordinate == "solution" ? Guid.CreateVersion7() : database.Host.SolutionId);
        var instance = AuthorityScope.Instance(solution,
            coordinate == "instance" ? Guid.CreateVersion7() : database.Host.InstanceId);
        var tenant = AuthorityScope.Tenant(instance,
            coordinate == "tenant" ? Guid.CreateVersion7() : database.TenantId);
        var scope = coordinate == "resource" ? AuthorityScope.Resource(tenant, Guid.CreateVersion7()) : tenant;
        var publication = await database.PublishPolicyAsync(database.Proposed(scope));
        await Assert.That(publication.Outcome).IsEqualTo(coordinate == "resource"
            ? ModerationPolicyPublicationOutcome.Invalid : ModerationPolicyPublicationOutcome.Denied);

        var facts = await database.ReadAsync(database.Request());

        await Assert.That(facts).IsEqualTo(EventResourceModerationFacts.NotAdopted);
        await Assert.That(facts.PermitsNativeEvaluationAt(Now)).IsTrue();
    }

    [Test]
    public async Task Batch_keeps_resource_sanctions_specific_within_a_declared_tenant_profile()
    {
        await using var database = await ReaderDatabase.CreateAsync();
        await database.EstablishBindingAsync();
        var policy = await database.StorePolicyAsync(database.TenantScope);
        await database.StoreSanctionAsync(database.ServiceSubject, policy);
        var adopted = database.Request();
        var unrelated = adopted with { ResourceId = Guid.CreateVersion7() };
        await using var context = database.Context();
        await using var transaction = await context.Database.BeginTransactionAsync(IsolationLevel.Serializable);

        var facts = await database.Reader(context).ReadBatchAsync(
            [unrelated, adopted, unrelated], Now, CancellationToken.None);

        await Assert.That(facts.Count).IsEqualTo(3);
        await Assert.That(facts[0].IsAdopted).IsTrue();
        await Assert.That(facts[0].Subject).IsEqualTo(database.ServiceSubject);
        await Assert.That(facts[0].Outcome).IsEqualTo(ModerationRestrictionOutcome.Unrestricted);
        await Assert.That(facts[1].Subject).IsEqualTo(database.ServiceSubject);
        await Assert.That(facts[1].Outcome).IsEqualTo(ModerationRestrictionOutcome.Denied);
        await Assert.That(facts[2]).IsEqualTo(facts[0]);
        await transaction.CommitAsync();
    }

    [Test]
    public async Task Upload_and_read_for_one_resource_keep_separate_adoption_and_effects()
    {
        await using var database = await ReaderDatabase.CreateAsync();
        await database.EstablishBindingAsync();
        var policy = await database.StorePolicyAsync(database.TenantScope,
            referenceId: ModerationReferenceProfileId.CFG07);
        await database.StoreSanctionAsync(database.ServiceSubject, policy, ModerationCapability.Upload);
        var upload = database.Request() with
        {
            Action = "update",
            RequiresUnquarantinedMaterial = true,
            IsUploadMutation = true
        };
        await using var context = database.Context();
        await using var transaction = await context.Database.BeginTransactionAsync(IsolationLevel.Serializable);
        var facts = await database.Reader(context).ReadBatchAsync(
            [database.Request(), upload, upload with { IsUploadMutation = false }, upload], Now, default);

        await Assert.That(facts[0]).IsEqualTo(EventResourceModerationFacts.NotAdopted);
        await Assert.That(facts[1].Outcome).IsEqualTo(ModerationRestrictionOutcome.Denied);
        await Assert.That(facts[2]).IsEqualTo(EventResourceModerationFacts.NotAdopted);
        await Assert.That(facts[3]).IsEqualTo(facts[1]);
    }

    [Test]
    public async Task Read_restriction_does_not_become_an_upload_restriction()
    {
        await using var database = await ReaderDatabase.CreateAsync();
        await database.EstablishBindingAsync();
        var policy = await database.StorePolicyAsync(database.TenantScope);
        await database.StoreSanctionAsync(database.ServiceSubject, policy);
        var upload = database.Request() with
        {
            Action = "update",
            RequiresUnquarantinedMaterial = true,
            IsUploadMutation = true
        };
        await Assert.That(await database.ReadAsync(upload)).IsEqualTo(EventResourceModerationFacts.NotAdopted);
        await Assert.That((await database.ReadAsync(database.Request())).Outcome)
            .IsEqualTo(ModerationRestrictionOutcome.Denied);
    }

    [Test]
    [Arguments(false)]
    [Arguments(true)]
    public async Task Retired_upload_support_does_not_remove_current_restrictions(bool establishBinding)
    {
        await using var database = await ReaderDatabase.CreateAsync();
        if (establishBinding) await database.EstablishBindingAsync();
        var policy = await database.StorePolicyAsync(database.TenantScope,
            referenceId: ModerationReferenceProfileId.CFG07);
        if (establishBinding)
            await database.StoreSanctionAsync(database.ServiceSubject, policy, ModerationCapability.Upload);
        await database.RetireSupportAsync(policy);
        var facts = await database.ReadAsync(database.Request() with
        {
            Action = "update",
            RequiresUnquarantinedMaterial = true,
            IsUploadMutation = true
        });
        if (establishBinding)
            await Assert.That(facts.Outcome).IsEqualTo(ModerationRestrictionOutcome.Denied);
        else
            await AssertUnavailable(facts);
    }

    [Test]
    [Arguments("download", false)]
    [Arguments("update", true)]
    public async Task Upload_purpose_cannot_be_relabelled_as_read_or_parent_collection(string action, bool collection)
    {
        await using var database = await ReaderDatabase.CreateAsync();
        await AssertUnavailable(await database.ReadAsync(database.Request() with
        {
            Action = action,
            IsEventCollection = collection,
            RequiresUnquarantinedMaterial = true,
            IsUploadMutation = true
        }));
    }

    [Test]
    [Arguments(false, true)]
    [Arguments(true, true)]
    [Arguments(true, false)]
    public async Task Upload_commit_boundary_rolls_back_denied_success_but_preserves_failure_cleanup(
        bool restricted, bool successfulResult)
    {
        await using var database = await ReaderDatabase.CreateAsync();
        await using var privacy = await ModerationNoticePrivacyFixture.CreateAsync();
        await database.EstablishBindingAsync();
        if (restricted)
        {
            var policy = await database.StorePolicyAsync(database.TenantScope,
                referenceId: ModerationReferenceProfileId.CFG07);
            await database.StoreSanctionAsync(database.ServiceSubject, policy, ModerationCapability.Upload);
        }
        var request = database.Request() with
        {
            Action = "update",
            RequiresUnquarantinedMaterial = true,
            IsUploadMutation = true
        };
        var original = await database.ReadAsync(request);
        await using var context = database.Context();
        var boundary = new EventResourceUploadMutationBoundary(context, privacy.Authority(context, new FixtureClock()),
            database.Reader(context), new FixtureClock());
        async Task ExecuteAsync() => _ = await boundary.ExecuteAsync(request, original, async token =>
        {
            var counter = await new StorageUsageCounterRepository(context)
                .GetOrCreateAsync(database.TenantId, "boundary-fixture", token);
            counter.Reserve(17, 100);
            await context.SaveChangesAsync(token);
            return successfulResult
                ? BaseCommandResponse.Success(new StorageUploadSessionDto
                {
                    ContentType = "application/pdf",
                    SafeDisplayName = "Resource document",
                    Purpose = "event-resource",
                    Visibility = "private-owner",
                    Status = "reserved"
                })
                : BaseCommandResponse.Failure<StorageUploadSessionDto>("upload_denied");
        }, CancellationToken.None);
        if (restricted && successfulResult)
            await Assert.ThrowsAsync<ConcurrencyConflictException>(ExecuteAsync);
        else
            await ExecuteAsync();
        await using var verify = database.Context();
        var saved = await verify.StorageUsageCounters.AsNoTracking().SingleOrDefaultAsync();
        await Assert.That(saved?.ReservedBytes ?? 0).IsEqualTo(restricted && successfulResult ? 0L : 17L);
        await Assert.That(context.Database.CurrentTransaction).IsNull();
    }

    [Test]
    [Arguments(false)]
    [Arguments(true)]
    public async Task Upload_cannot_commit_when_a_scheduled_restriction_starts_during_its_write(bool afterFlush)
    {
        await using var database = await ReaderDatabase.CreateAsync();
        await using var privacy = await ModerationNoticePrivacyFixture.CreateAsync();
        await database.EstablishBindingAsync();
        var startsAt = Now.AddMinutes(1);
        var policy = await database.StorePolicyAsync(database.TenantScope,
            referenceId: ModerationReferenceProfileId.CFG07);
        await database.StoreSanctionAsync(database.ServiceSubject, policy, ModerationCapability.Upload, startsAt);
        var request = database.Request() with
        {
            Action = "update",
            RequiresUnquarantinedMaterial = true,
            IsUploadMutation = true
        };
        var original = await database.ReadAsync(request);
        await Assert.That(original.Outcome).IsEqualTo(ModerationRestrictionOutcome.Unrestricted);
        await Assert.That(original.CurrentUntil).IsEqualTo(startsAt);
        var clock = new UploadClock();
        var checkpoint = new StartRestrictionAfterFlush(clock, startsAt);
        await using var context = database.Context(afterFlush ? checkpoint : null);
        var boundary = new EventResourceUploadMutationBoundary(context, privacy.Authority(context, clock),
            database.Reader(context), clock);

        await Assert.ThrowsAsync<ConcurrencyConflictException>(async () =>
            _ = await boundary.ExecuteAsync(request, original, async token =>
            {
                var counter = await new StorageUsageCounterRepository(context)
                    .GetOrCreateAsync(database.TenantId, "scheduled-upload-boundary", token);
                counter.Reserve(17, 100);
                if (!afterFlush) clock.Now = startsAt;
                await context.SaveChangesAsync(token);
                return BaseCommandResponse.Success(new StorageUploadSessionDto
                {
                    ContentType = "application/pdf",
                    SafeDisplayName = "Resource document",
                    Purpose = "event-resource",
                    Visibility = "private-owner",
                    Status = "reserved"
                });
            }, CancellationToken.None));

        await using var verify = database.Context();
        await Assert.That(await verify.StorageUsageCounters.AsNoTracking().AnyAsync()).IsFalse();
        await Assert.That(checkpoint.Observed).IsEqualTo(afterFlush);
        await Assert.That(context.Database.CurrentTransaction).IsNull();
    }

    [Test]
    public async Task Upload_rechecks_expiry_after_the_final_authority_queries()
    {
        await using var database = await ReaderDatabase.CreateAsync();
        await using var privacy = await ModerationNoticePrivacyFixture.CreateAsync();
        await database.EstablishBindingAsync();
        var startsAt = Now.AddMinutes(1);
        var policy = await database.StorePolicyAsync(database.TenantScope,
            referenceId: ModerationReferenceProfileId.CFG07);
        await database.StoreSanctionAsync(database.ServiceSubject, policy, ModerationCapability.Upload, startsAt);
        var request = database.Request() with
        {
            Action = "update",
            RequiresUnquarantinedMaterial = true,
            IsUploadMutation = true
        };
        var original = await database.ReadAsync(request);
        var clock = new UploadClock();
        var checkpoint = new StartRestrictionDuringRead(clock, startsAt);
        await using var context = database.Context(checkpoint);
        var boundary = new EventResourceUploadMutationBoundary(context, privacy.Authority(context, clock),
            database.Reader(context), clock);

        await Assert.ThrowsAsync<ConcurrencyConflictException>(async () =>
            _ = await boundary.ExecuteAsync(request, original, async token =>
            {
                var counter = await new StorageUsageCounterRepository(context)
                    .GetOrCreateAsync(database.TenantId, "upload-final-read", token);
                counter.Reserve(17, 100);
                await context.SaveChangesAsync(token);
                checkpoint.Armed = true;
                return BaseCommandResponse.Success(new StorageUploadSessionDto
                {
                    ContentType = "application/pdf",
                    SafeDisplayName = "Resource document",
                    Purpose = "event-resource",
                    Visibility = "private-owner",
                    Status = "reserved"
                });
            }, CancellationToken.None));

        await using var verify = database.Context();
        await Assert.That(await verify.StorageUsageCounters.AsNoTracking().AnyAsync()).IsFalse();
        await Assert.That(checkpoint.Observed).IsTrue();
        await Assert.That(context.Database.CurrentTransaction).IsNull();
    }

    [Test]
    public async Task Upload_colocated_privacy_commit_rechecks_authority_and_rolls_back_the_write()
    {
        await using var database = await ReaderDatabase.CreateAsync();
        await database.EstablishBindingAsync();
        var startsAt = Now.AddMinutes(1);
        var policy = await database.StorePolicyAsync(database.TenantScope,
            referenceId: ModerationReferenceProfileId.CFG07);
        await database.StoreSanctionAsync(database.ServiceSubject, policy, ModerationCapability.Upload, startsAt);
        var request = database.Request() with
        {
            Action = "update",
            RequiresUnquarantinedMaterial = true,
            IsUploadMutation = true
        };
        var original = await database.ReadAsync(request);
        var clock = new UploadClock();
        await using var context = database.Context();
        await using var privacy = await ModerationNoticePrivacyFixture.CreateColocatedAsync(context);
        var boundary = new EventResourceUploadMutationBoundary(context, privacy.Authority(context, clock),
            database.Reader(context), clock);
        var observedPrivacyOwnedCommit = false;

        await Assert.ThrowsAsync<ConcurrencyConflictException>(async () =>
            _ = await boundary.ExecuteAsync(request, original, async token =>
            {
                observedPrivacyOwnedCommit = context.IdentityFenceOwnsTransaction;
                var counter = await new StorageUsageCounterRepository(context)
                    .GetOrCreateAsync(database.TenantId, "colocated-upload-boundary", token);
                counter.Reserve(17, 100);
                await context.SaveChangesAsync(token);
                clock.Now = startsAt;
                return BaseCommandResponse.Success(new StorageUploadSessionDto
                {
                    ContentType = "application/pdf",
                    SafeDisplayName = "Resource document",
                    Purpose = "event-resource",
                    Visibility = "private-owner",
                    Status = "reserved"
                });
            }, CancellationToken.None));

        await using var verify = database.Context();
        await Assert.That(observedPrivacyOwnedCommit).IsTrue();
        await Assert.That(await verify.StorageUsageCounters.AsNoTracking().AnyAsync()).IsFalse();
        await Assert.That(context.IdentityFenceOwnsTransaction).IsFalse();
        await Assert.That(context.Database.CurrentTransaction).IsNull();
    }

    private static async Task AssertUnavailable(EventResourceModerationFacts facts)
    {
        await Assert.That(facts.IsAdopted).IsTrue();
        await Assert.That(facts.Outcome).IsEqualTo(ModerationRestrictionOutcome.Unavailable);
        await Assert.That(facts.Subject).IsNull();
        await Assert.That(facts.BindingId).IsNull();
        await Assert.That(facts.BindingRevision).IsNull();
        await Assert.That(facts.PermitsNativeEvaluationAt(Now)).IsFalse();
        await Assert.That(facts.Generation).IsNotEqualTo("not-adopted");
    }

    private sealed class ReaderDatabase : IAsyncDisposable, ITenantContext
    {
        private const string KeyId = "resource-reader-service";
        private readonly SqliteConnection connection;
        private readonly DbContextOptions<ExploreDbContext> options;

        private ReaderDatabase(SqliteConnection connection)
        {
            this.connection = connection;
            options = TestDbContextOptions.Create<ExploreDbContext>().UseSqlite(connection)
                .UseSnakeCaseNamingConvention().AddInterceptors(SqliteNamedLockTransactionInterceptor.Instance).Options;
            Host = ModerationHostBinding.Establish(Guid.CreateVersion7(), Guid.CreateVersion7(),
                Guid.CreateVersion7(), Guid.CreateVersion7(), Guid.CreateVersion7());
        }

        public Guid TenantId { get; } = Guid.CreateVersion7();
        public Guid AdministratorId { get; } = Guid.CreateVersion7();
        public Guid ControllerId { get; } = Guid.CreateVersion7();
        public Guid IssuerId { get; } = Guid.CreateVersion7();
        public Guid ServiceId { get; } = Guid.CreateVersion7();
        public Guid ResourceId { get; } = Guid.CreateVersion7();
        public ModerationHostBinding Host { get; }
        public AuthorityScope InstanceScope => AuthorityScope.Instance(
            AuthorityScope.Solution(AuthorityScope.TrustDomain(Host.TrustDomainId), Host.SolutionId), Host.InstanceId);
        public AuthorityScope TenantScope => AuthorityScope.Tenant(InstanceScope, TenantId);
        public SubjectReference ServiceSubject => SubjectReference.Create(ModerationSubjectKind.Service, ServiceId, InstanceScope);
        public ExploreDbContext Context(IInterceptor? interceptor = null) => new(interceptor is null ? options
            : new DbContextOptionsBuilder<ExploreDbContext>(options).AddInterceptors(interceptor).Options)
        { TenantContext = this };
        public EventResourceAuthorityRequest Request() => new(TenantId, ResourceId, null, true, "download")
        {
            MachinePrincipal = new(KeyId, null, ExternalApiKeyOwnerType.InstanceAdmin,
                AdministratorId, ApiKeyCredentialSource.Persisted)
        };

        public EventResourceModerationSnapshotReader Reader(ExploreDbContext context) =>
            new(context, Repository(context), ModerationRecognitionResourceFixture.Reader(context));

        private (AdminContext Admin, MachinePrincipalAccessor Machine) Identity(ExploreDbContext context)
        {
            var http = new HttpContextAccessor
            {
                HttpContext = new DefaultHttpContext
                {
                    User = new ClaimsPrincipal(new ClaimsIdentity(
                        [new Claim(ClaimTypes.NameIdentifier, AdministratorId.ToString())], "source-witness"))
                }
            };
            var admin = new AdminContext(http, new AdministrativeAuthorityRepository(new SecurityTestSupport.AdministrativeAuthorityTestFactory(context)),
                new OrganizationMemberRepository(context),
                new GroupMemberRepository(context), new ExternalLoginAuthenticationRepository(new SecurityTestSupport.AdministrativeAuthorityTestFactory(context), null), NullLogger<AdminContext>.Instance);
            return (admin, new MachinePrincipalAccessor(http));
        }

        private ModerationMachineSubjectBindingRepository Repository(ExploreDbContext context)
        {
            var (admin, machine) = Identity(context);
            return new(context, admin, machine,
                new ExternalApiKeyAuthenticationRepository(new SecurityTestSupport.AdministrativeAuthorityTestFactory(context)),
                new UnconfiguredKeys(), new FixtureClock());
        }

        public async Task<ModerationMachineSubjectBinding> EstablishBindingAsync()
        {
            await using var context = Context();
            var result = await Repository(context).EstablishAsync(new(
                Guid.CreateVersion7(), Host.Id, ModerationMachineCredentialSource.Persisted, KeyId,
                null, ExternalApiKeyOwnerType.InstanceAdmin, AdministratorId, ServiceId));
            await Assert.That(result.Outcome).IsEqualTo(ModerationAuthoritySourceOutcome.Established);
            await Assert.That(result.Binding).IsNotNull();
            return result.Binding!;
        }

        public async Task<EventResourceModerationFacts> ReadAsync(EventResourceAuthorityRequest request)
        {
            await using var context = Context();
            await using var transaction = await context.Database.BeginTransactionAsync(IsolationLevel.Serializable);
            var facts = await Reader(context).ReadAsync(request, Now, CancellationToken.None);
            await transaction.CommitAsync();
            return facts;
        }

        public ModerationPolicyProposedState Proposed(AuthorityScope scope, bool supported = true,
            ModerationReferenceProfileId referenceId = ModerationReferenceProfileId.CFG08)
        {
            var reference = ModerationReferenceCatalogue.GetProfile(referenceId);
            var response = reference.Responses[0];
            var inventory = ModerationCapabilityRegistry.Parse(ModerationCapabilityRegistry.ReadInventory());
            ModerationRuleReference Rule(string code) => ModerationRuleReference.Create(scope.Id, code, 1);
            var profile = new ModerationPolicyProfileDefinition(Guid.CreateVersion7(), Rule("resource-read"),
                Rule("resource-read-reason"), reference.Id, reference.Revision, response.Grade,
                [ModerationScopeKind.Resource], [ModerationSubjectKind.Human, ModerationSubjectKind.Service],
                reference.Effects.ToImmutableArray(), reference.Bundle, reference.ProtectionPurpose, [],
                new(response.Timing.Kind, response.Timing.Duration), response.RequiredApprovals, 6,
                ModerationReferenceRecognition.Never);
            return new(Guid.CreateVersion7(), 0, Guid.CreateVersion7(), scope,
                ModerationReferenceCatalogue.Revision, inventory.Revision, inventory.Fingerprint,
                ModerationPolicyRequestedState.Draft, [ModerationNativeMode.Embedded],
                [new(Rule("resource-read-class"), "Restricted resource disclosure.", reference.Ground, reference.Basis)],
                [new(profile.Reason, "Reason for selected resource disclosure restriction.", Rule("resource-read-class"))],
                [profile], supported ? [profile.Rule] : []);
        }

        public async Task<ModerationPolicyPublicationResult> PublishPolicyAsync(
            ModerationPolicyProposedState proposed, Guid? expectedPublication = null)
        {
            await using var context = Context();
            var epoch = await context.ModerationQualificationEpochs.AsNoTracking().SingleAsync();
            var (admin, machine) = Identity(context);
            var writer = new ModerationPolicyPublicationRepository(context, admin, machine,
                ModerationCapabilityRegistry.Parse(ModerationCapabilityRegistry.ReadInventory()), null, new FixtureClock());
            return await writer.PublishAsync(new(Guid.CreateVersion7(), Host.Id, epoch.Id, epoch.Revision,
                ModerationPolicyPublicationProjection.WriterGeneration, expectedPublication, proposed));
        }

        public async Task<ModerationPolicyPublication> StorePolicyAsync(AuthorityScope scope, bool supported = true,
            ModerationReferenceProfileId referenceId = ModerationReferenceProfileId.CFG08)
        {
            // Native draft publication establishes declarations, not measured activation readiness.
            var result = await PublishPolicyAsync(Proposed(scope, supported, referenceId));
            await Assert.That(result.Outcome).IsEqualTo(ModerationPolicyPublicationOutcome.Known);
            return result.Publication!;
        }

        public async Task RetireSupportAsync(ModerationPolicyPublication publication)
        {
            var declared = ModerationPolicyPublicationProjection.RestoreHistory(publication)!.Proposed;
            var result = await PublishPolicyAsync(declared with
            {
                PublicationId = Guid.CreateVersion7(),
                ExpectedRevision = publication.Revision,
                SupportedProfiles = []
            }, publication.Id);
            await Assert.That(result.Outcome).IsEqualTo(ModerationPolicyPublicationOutcome.Known);
        }

        public async Task StoreSanctionAsync(SubjectReference subject, ModerationPolicyPublication policy,
            ModerationCapability capability = ModerationCapability.ReadResource, DateTimeOffset? startsAt = null)
        {
            var scope = AuthorityScope.Resource(TenantScope, ResourceId);
            var profile = policy.Profiles.Single();
            var rule = ModerationRuleReference.Create(policy.Jurisdiction.Id, profile.RuleCode, profile.RuleRevision);
            var reason = ModerationRuleReference.Create(policy.Jurisdiction.Id, profile.ReasonCode, profile.ReasonRevision);
            var issuer = SubjectReference.Create(ModerationSubjectKind.Human, IssuerId, InstanceScope);
            var controller = SubjectReference.Create(ModerationSubjectKind.Human, ControllerId, InstanceScope);
            var allegation = ModerationCase.Open(Guid.CreateVersion7(), subject, scope, Guid.CreateVersion7(), Now);
            // Ledger fixture facts exercise persisted restrictions, not the native finding writer's authority.
            var finding = ModerationFinding.Finalize(Guid.CreateVersion7(), allegation, 1,
                allegation.Scope, null, null, rule, rule, reason, reason, controller,
                ModerationFindingEvidenceReference.Response(Guid.CreateVersion7(), allegation.Id, 1, Now),
                "Fixture finding for persisted resource-read restriction.", Now);
            var decision = ModerationDecision.Propose(Guid.CreateVersion7(), allegation.Id, subject, issuer,
                scope, scope, rule, [capability], startsAt ?? Now, Now.AddHours(1), null, 1, Now);
            decision.RecordResponse(decision.Revision, Now);
            decision.AcceptFinding(finding.Id, decision.Revision, Now);
            decision.Approve(controller, scope, decision.Revision, Now);
            var sanction = decision.Activate(Guid.CreateVersion7(), decision.Revision, Now);
            decision.ValidatePersistedState();
            sanction.ValidatePersistedState();
            await using var context = Context();
            await using var transaction = await context.Database.BeginTransactionAsync(IsolationLevel.Serializable);
            context.AddRange(allegation, finding, decision, sanction);
            await context.SaveChangesAsync();
            await transaction.CommitAsync();
        }

        public static async Task<ReaderDatabase> CreateAsync()
        {
            var connection = await SqliteTestDatabaseFactory.CreateOpenIsolatedConnectionAsync();
            var database = new ReaderDatabase(connection);
            await using var context = database.Context();
            await context.Database.EnsureCreatedAsync();
            var status = new TenantStatus
            {
                Id = (int)TenantStatusEnum.Active,
                MasterCode = "ACTIVE",
                FullName = "Active",
                IsActiveState = true
            };
            var tenant = new Tenant
            {
                Id = database.TenantId,
                FullName = "Resource reader tenant",
                Slug = $"reader-{database.TenantId:N}",
                TenantStatusId = status.Id,
                TenantStatus = status
            };
            var user = NativeUser(database.AdministratorId);
            var role = new Role
            {
                Id = (int)RoleEnum.Admin,
                MasterCode = "platform.admin",
                FullName = "Admin",
                IsSystem = true,
                RoleScopeId = (int)RoleScopeEnum.Platform,
                RoleScope = new RoleScope
                {
                    Id = (int)RoleScopeEnum.Platform,
                    MasterCode = "PLATFORM",
                    FullName = "Platform"
                }
            };
            await using var transaction = await context.Database.BeginTransactionAsync(IsolationLevel.Serializable);
            context.AddRange(tenant, user, NativeUser(database.ControllerId), NativeUser(database.IssuerId),
                new ServicePrincipal
                {
                    Id = database.Host.MaintenanceServicePrincipalId,
                    Code = $"maintenance-{database.TenantId:N}",
                    DisplayName = "Maintenance"
                },
                new ServicePrincipal
                {
                    Id = database.ServiceId,
                    Code = $"reader-{database.TenantId:N}",
                    DisplayName = "Explicit reader service"
                },
                database.Host,
                new PlatformUserRole
                {
                    Id = Guid.CreateVersion7(),
                    UserId = user.Id,
                    User = user,
                    RoleId = role.Id,
                    Role = role
                },
                ModerationQualificationEpoch.Initialize(Guid.CreateVersion7(), database.Host, tenant),
                new ExternalApiKeyStatus
                {
                    Id = (int)ExternalApiKeyStatusEnum.Active,
                    MasterCode = "ACTIVE",
                    FullName = "Active",
                    IsUsable = true
                },
                new ExternalApiKeyCreditPeriod
                {
                    Id = (int)ExternalApiKeyCreditPeriodEnum.None,
                    MasterCode = "NONE",
                    FullName = "None"
                },
                new ExternalApiKeyOwnerTypeLookup
                {
                    Id = (int)ExternalApiKeyOwnerType.InstanceAdmin,
                    MasterCode = "INSTANCE_ADMIN",
                    FullName = "Instance Admin"
                });
            await context.SaveChangesAsync();
            context.ExternalApiKeys.Add(new ExternalApiKey
            {
                Id = Guid.CreateVersion7(),
                KeyId = KeyId,
                Name = "Native resource reader credential",
                SecretHash = ApiKeyHashing.ComputeHash(ApiKeyHashing.CreateSecret()),
                Scopes = "events:read",
                OwnerType = ExternalApiKeyOwnerType.InstanceAdmin,
                OwnerId = database.AdministratorId,
                ExternalApiKeyStatusId = (int)ExternalApiKeyStatusEnum.Active,
                ExternalApiKeyStatus = null!,
                ExternalApiKeyCreditPeriodId = (int)ExternalApiKeyCreditPeriodEnum.None,
                ExternalApiKeyCreditPeriod = null!
            });
            await context.SaveChangesAsync();
            await transaction.CommitAsync();
            return database;
        }

        private static User NativeUser(Guid id) => new()
        {
            Id = id,
            Pii = new UserPii
            {
                UserId = id,
                Email = $"{id:N}@example.invalid",
                FirstName = "Reader",
                LastName = "Fixture"
            }
        };

        public ValueTask DisposeAsync() => connection.DisposeAsync();
    }

    private sealed class UploadClock : TimeProvider
    {
        public DateTimeOffset Now { get; set; } = EventResourceModerationSnapshotReaderTests.Now;
        public override DateTimeOffset GetUtcNow() => Now;
    }

    private sealed class StartRestrictionAfterFlush(UploadClock clock, DateTimeOffset startsAt) : SaveChangesInterceptor
    {
        public bool Observed { get; private set; }
        public override ValueTask<int> SavedChangesAsync(
            SaveChangesCompletedEventData eventData, int result, CancellationToken cancellationToken = default)
        {
            Observed = true;
            clock.Now = startsAt;
            return ValueTask.FromResult(result);
        }
    }

    private sealed class FixtureClock : TimeProvider
    {
        public override DateTimeOffset GetUtcNow() => Now;
    }

    private sealed class StartRestrictionDuringRead(UploadClock clock, DateTimeOffset startsAt) : DbCommandInterceptor
    {
        public bool Armed { get; set; }
        public bool Observed { get; private set; }
        public override ValueTask<System.Data.Common.DbDataReader> ReaderExecutedAsync(
            System.Data.Common.DbCommand command, CommandExecutedEventData eventData,
            System.Data.Common.DbDataReader result, CancellationToken cancellationToken = default)
        {
            if (Armed)
            {
                Armed = false;
                Observed = true;
                clock.Now = startsAt;
            }
            return ValueTask.FromResult(result);
        }
    }

    private sealed class UnconfiguredKeys : IConfiguredApiKeyResourceAuthorityReader
    {
        public ValueTask<IConfiguredApiKeyResourceAuthorityLease?> AcquireAuthenticatedAsync(
            string keyId, CancellationToken cancellationToken) =>
            ValueTask.FromResult<IConfiguredApiKeyResourceAuthorityLease?>(null);

        public ValueTask<IConfiguredApiKeyResourceAuthorityLease?> AcquireAsync(
            string keyId, CancellationToken cancellationToken) =>
            ValueTask.FromResult<IConfiguredApiKeyResourceAuthorityLease?>(null);

        public ValueTask<ApiKeyPrincipalContext?> ReadAsync(
            string keyId, DateTimeOffset evaluationUtc, CancellationToken cancellationToken) =>
            ValueTask.FromResult<ApiKeyPrincipalContext?>(null);
    }
}
