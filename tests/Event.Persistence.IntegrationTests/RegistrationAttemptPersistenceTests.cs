// ABOUTME: Characterizes Phase 8.1 registration persistence identities, containment, and retry behavior.
// ABOUTME: Proves tenant-safe replay classification and existing-row-safe PostgreSQL model derivation.

using System.Security.Cryptography;
using System.Text;
using Event.Persistence.IntegrationTests.Fixtures;
using Explore.Application.Contracts.Infrastructure;
using Explore.Application.Contracts.Persistence;
using Explore.Domain;
using Explore.Domain.Enums;
using Explore.Domain.ValueObjects;
using Explore.Persistence;
using Explore.Persistence.Database;
using Explore.Persistence.Repositories;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Diagnostics;
using Microsoft.EntityFrameworkCore.Metadata;
using Npgsql;
using TUnit.Assertions;
using TUnit.Core;

namespace Event.Persistence.IntegrationTests;

public sealed class RegistrationAttemptPersistenceCharacterizationTests
{
    private static readonly DateTime UtcNow = new(2026, 8, 2, 8, 0, 0, DateTimeKind.Utc);

    [Test]
    public async Task ExistingRegistrationLineageExposesTenantContainedPrincipalKeys()
    {
        await using var context = CreateModelContext();

        IModel model = context.Model;

        await Assert.That(HasKey<RegistrationOrder>(model, "TenantId", "Id")).IsTrue();
        await Assert.That(HasKey<RegistrationWorkflow>(model, "TenantId", "EventId", "Id")).IsTrue();
        await Assert.That(HasKey<RegistrationRequirement>(model, "TenantId", "EventId", "RegistrationWorkflowId", "Id")).IsTrue();
        await Assert.That(HasKey<RegistrationForm>(model, "TenantId", "EventId", "Id")).IsTrue();
        await Assert.That(HasKey<RegistrationFormVersion>(model, "TenantId", "EventId", "RegistrationFormId", "Id")).IsTrue();
    }

    [Test]
    public async Task Phase81ModelDeclaresFiltersConvertersBusinessUniquenessAndRevisionOrdering()
    {
        await using var context = CreateModelContext();

        IEntityType attempt = context.Model.FindEntityType(typeof(RegistrationAttempt))!;
        IEntityType submission = context.Model.FindEntityType(typeof(RegistrationSubmission))!;
        IEntityType revision = context.Model.FindEntityType(typeof(RegistrationSubmissionRevision))!;

        await Assert.That(attempt.GetDeclaredQueryFilters().Count()).IsEqualTo(2);
        await Assert.That(submission.GetDeclaredQueryFilters().Count()).IsEqualTo(2);
        await Assert.That(revision.GetDeclaredQueryFilters().Count()).IsEqualTo(2);
        await Assert.That(attempt.FindProperty(nameof(RegistrationAttempt.CapabilityTokenHash))!.GetValueConverter()).IsNotNull();
        await Assert.That(submission.FindProperty(nameof(RegistrationSubmission.ReceivedEvidenceHash))!.GetValueConverter()).IsNotNull();
        await Assert.That(submission.FindProperty(nameof(RegistrationSubmission.HttpIdempotencyKeyHash))!.GetValueConverter()).IsNotNull();
        await Assert.That(submission.GetIndexes().Count(index => index.IsUnique)).IsEqualTo(2);
        IIndex nativeIdentity = submission.GetIndexes().Single(index =>
            index.Properties.Any(property => property.Name == nameof(RegistrationSubmission.BusinessDeduplicationKey)));
        IIndex providerIdentity = submission.GetIndexes().Single(index =>
            index.Properties.Any(property => property.Name == nameof(RegistrationSubmission.ProviderSubmissionId)));
        IIndex revisionIdentity = revision.GetIndexes().Single(index => index.IsUnique);
        await Assert.That(submission.FindPrimaryKey()!.GetName()).IsEqualTo("pk_registration_submissions");
        await Assert.That(revision.FindPrimaryKey()!.GetName()).IsEqualTo("pk_registration_submission_revisions");
        await Assert.That(nativeIdentity.GetDatabaseName()).IsNotNull();
        await Assert.That(providerIdentity.GetDatabaseName()).IsNotNull();
        await Assert.That(revisionIdentity.GetDatabaseName()).IsNotNull();
        await Assert.That(revisionIdentity.Properties.Select(property => property.Name))
            .IsEquivalentTo(["TenantId", "RegistrationSubmissionId", "RevisionNumber"]);
    }

    [Test]
    public async Task PersistenceOutcomeDistinguishesEvidenceOnlyConflict()
    {
        await Assert.That(Enum.GetNames<RegistrationSubmissionPersistenceOutcome>())
            .Contains("EvidenceOnlyConflict");
    }

    [Test]
    public async Task AttemptChannelForeignKeyPinsNormalizedProviderBinding()
    {
        await using ExploreDbContext context = CreateModelContext();
        IForeignKey channelForeignKey = context.Model.FindEntityType(typeof(RegistrationAttempt))!.GetForeignKeys()
            .Single(key => key.PrincipalEntityType.ClrType == typeof(RegistrationChannel));

        await Assert.That(channelForeignKey.Properties.Select(property => property.Name).SequenceEqual([
                "TenantId", "EventId", "RegistrationWorkflowId", "RegistrationRequirementId",
                "RegistrationChannelId", "RegistrationProviderBindingKey"
            ])).IsTrue();
        await Assert.That(channelForeignKey.PrincipalKey.Properties.Select(property => property.Name).SequenceEqual([
                "TenantId", "EventId", "RegistrationWorkflowId", "RegistrationRequirementId",
                "Id", "RegistrationProviderBindingKey"
            ])).IsTrue();
    }

    [Test]
    public async Task AttemptSupersessionAllowsReplacementChannelAndForm()
    {
        await using ExploreDbContext context = CreateModelContext();
        IForeignKey supersessionForeignKey = context.Model.FindEntityType(typeof(RegistrationAttempt))!.GetForeignKeys()
            .Single(key => key.PrincipalEntityType.ClrType == typeof(RegistrationAttempt));

        await Assert.That(supersessionForeignKey.Properties.Select(property => property.Name).SequenceEqual([
                "TenantId", "EventId", "RegistrationOrderId", "RegistrationWorkflowId",
                "RegistrationRequirementId", "SupersededByRegistrationAttemptId"
            ])).IsTrue();
    }

    [Test]
    public async Task RevisionTransactionRunsInsideExecutionStrategy()
    {
        string source = await File.ReadAllTextAsync(Path.Combine(
            FindRepositoryRoot(), "src", "Explore.Persistence", "Repositories", "RegistrationSubmissionRepository.cs"));
        int methodStart = source.IndexOf("public async Task<bool> PersistRevisionAsync", StringComparison.Ordinal);
        int methodEnd = source.IndexOf("public async Task<bool> PersistFinalizationAsync", methodStart, StringComparison.Ordinal);

        await Assert.That(source[methodStart..methodEnd]).Contains("CreateExecutionStrategy()");
    }

    [Test]
    public async Task BusinessAndCapabilityUniquenessSurviveSoftDelete()
    {
        await using ExploreDbContext context = CreateModelContext();
        IEntityType attempt = context.Model.FindEntityType(typeof(RegistrationAttempt))!;
        IEntityType submission = context.Model.FindEntityType(typeof(RegistrationSubmission))!;
        IIndex capability = attempt.GetIndexes().Single(index => index.IsUnique);
        IIndex native = submission.GetIndexes().Single(index =>
            index.IsUnique && index.Properties.Any(property => property.Name == nameof(RegistrationSubmission.BusinessDeduplicationKey)));
        IIndex provider = submission.GetIndexes().Single(index =>
            index.IsUnique && index.Properties.Any(property => property.Name == nameof(RegistrationSubmission.ProviderSubmissionId)));

        await Assert.That(capability.Properties.Select(property => property.Name))
            .IsEquivalentTo(["TenantId", "CapabilityTokenHash"]);
        await Assert.That(native.Properties.Select(property => property.Name))
            .IsEquivalentTo(["TenantId", "RegistrationAttemptId", "BusinessDeduplicationKey"]);
        await Assert.That(provider.Properties.Select(property => property.Name))
            .IsEquivalentTo(["TenantId", "RegistrationProviderBindingId", "ProviderSubmissionId", "ProviderResponseRevision"]);
        await Assert.That(new[] { capability.GetFilter(), native.GetFilter(), provider.GetFilter() }
            .All(filter => filter?.Contains("is_deleted", StringComparison.OrdinalIgnoreCase) != true)).IsTrue();
    }

    [Test]
    public async Task AttemptOrderForeignKeyPinsWorkflowVersion()
    {
        await using ExploreDbContext context = CreateModelContext();
        IForeignKey orderForeignKey = context.Model.FindEntityType(typeof(RegistrationAttempt))!.GetForeignKeys()
            .Single(key => key.PrincipalEntityType.ClrType == typeof(RegistrationOrder));

        await Assert.That(orderForeignKey.Properties.Select(property => property.Name)
            .SequenceEqual(["TenantId", "EventId", "RegistrationWorkflowId", "RegistrationOrderId"])).IsTrue();
        await Assert.That(orderForeignKey.PrincipalKey.Properties.Select(property => property.Name)
            .SequenceEqual(["TenantId", "EventId", "RegistrationWorkflowVersionKey", "Id"])).IsTrue();
    }

    [Test]
    public async Task ContainmentShadowKeysAreStoredComputedUuidColumnsAndCannotBeWritten()
    {
        await using ExploreDbContext context = CreateModelContext();
        IProperty attemptBinding = context.Model.FindEntityType(typeof(RegistrationAttempt))!
            .FindProperty("RegistrationProviderBindingKey")!;
        IProperty channelBinding = context.Model.FindEntityType(typeof(RegistrationChannel))!
            .FindProperty("RegistrationProviderBindingKey")!;
        IProperty orderWorkflow = context.Model.FindEntityType(typeof(RegistrationOrder))!
            .FindProperty("RegistrationWorkflowVersionKey")!;

        await AssertComputedUuid(attemptBinding,
            "COALESCE(registration_provider_binding_id, '00000000-0000-0000-0000-000000000000'::uuid)");
        await AssertComputedUuid(channelBinding,
            "COALESCE(registration_provider_binding_id, '00000000-0000-0000-0000-000000000000'::uuid)");
        await AssertComputedUuid(orderWorkflow,
            "COALESCE(registration_workflow_version_id, '00000000-0000-0000-0000-000000000000'::uuid)");
    }

    [Test]
    public async Task ExpectedNativeSubmissionIdentityConstraintsClassifyAsReplayRaces()
    {
        Guid tenantId = Guid.CreateVersion7();
        string constraintName;
        await using (ExploreDbContext probeContext = CreateInMemoryContext(tenantId))
        {
            constraintName = RelationalConstraintDescriptorResolver.UniqueIndex<RegistrationSubmission>(
                probeContext,
                nameof(RegistrationSubmission.TenantId),
                nameof(RegistrationSubmission.RegistrationAttemptId),
                nameof(RegistrationSubmission.BusinessDeduplicationKey)).Name;
        }

        await using ExploreDbContext context = CreateInMemoryContext(
            tenantId,
            new ThrowingSaveChangesInterceptor(() => CreateUniqueViolation(constraintName)));
        RegistrationAttempt attempt = CreateAttempt(tenantId, 101);
        RegistrationSubmission evidence = RegistrationSubmission.CreateNativeEvidenceOnly(
            attempt, Evidence(102), UtcNow.AddMinutes(1), null);

        RegistrationSubmissionPersistenceResult result = await new RegistrationSubmissionRepository(context)
            .PersistEvidenceOnlyAsync(evidence, CancellationToken.None);

        await Assert.That(result.Outcome).IsEqualTo(RegistrationSubmissionPersistenceOutcome.AttemptUnavailable);
    }

    [Test]
    public async Task ExpectedProviderSubmissionIdentityConstraintsClassifyAsReplayRaces()
    {
        Guid tenantId = Guid.CreateVersion7();
        string constraintName;
        await using (ExploreDbContext probeContext = CreateInMemoryContext(tenantId))
        {
            constraintName = RelationalConstraintDescriptorResolver.UniqueIndex<RegistrationSubmission>(
                probeContext,
                nameof(RegistrationSubmission.TenantId),
                nameof(RegistrationSubmission.RegistrationProviderBindingId),
                nameof(RegistrationSubmission.ProviderSubmissionId),
                nameof(RegistrationSubmission.ProviderResponseRevision)).Name;
        }

        await using ExploreDbContext context = CreateInMemoryContext(
            tenantId,
            new ThrowingSaveChangesInterceptor(() => CreateUniqueViolation(constraintName)));
        RegistrationAttempt attempt = CreateAttempt(tenantId, 101);
        RegistrationSubmission evidence = RegistrationSubmission.CreateNativeEvidenceOnly(
            attempt, Evidence(102), UtcNow.AddMinutes(1), null);

        RegistrationSubmissionPersistenceResult result = await new RegistrationSubmissionRepository(context)
            .PersistEvidenceOnlyAsync(evidence, CancellationToken.None);

        await Assert.That(result.Outcome).IsEqualTo(RegistrationSubmissionPersistenceOutcome.AttemptUnavailable);
    }

    [Test]
    public async Task UnrelatedSubmissionUniqueConstraintPropagates()
    {
        Guid tenantId = Guid.CreateVersion7();
        DbUpdateException expected = CreateUniqueViolation("ux_unrelated_registration_constraint");
        await using ExploreDbContext context = CreateInMemoryContext(
            tenantId,
            new ThrowingSaveChangesInterceptor(() => expected));
        RegistrationAttempt attempt = CreateAttempt(tenantId, 103);
        RegistrationSubmission evidence = RegistrationSubmission.CreateNativeEvidenceOnly(
            attempt, Evidence(104), UtcNow.AddMinutes(1), null);

        DbUpdateException actual = (await Assert.ThrowsAsync<DbUpdateException>(() =>
            new RegistrationSubmissionRepository(context)
                .PersistEvidenceOnlyAsync(evidence, CancellationToken.None)))!;

        await Assert.That(actual).IsSameReferenceAs(expected);
    }

    [Test]
    public async Task SubmissionPrimaryKeyViolationPropagatesOriginalException()
    {
        Guid tenantId = Guid.CreateVersion7();
        DbUpdateException expected = CreateUniqueViolation("pk_registration_submissions");
        await using ExploreDbContext context = CreateInMemoryContext(
            tenantId,
            new ThrowingSaveChangesInterceptor(() => expected));
        RegistrationAttempt attempt = CreateAttempt(tenantId, 115);
        RegistrationSubmission evidence = RegistrationSubmission.CreateNativeEvidenceOnly(
            attempt, Evidence(116), UtcNow.AddMinutes(1), null);

        DbUpdateException actual = (await Assert.ThrowsAsync<DbUpdateException>(() =>
            new RegistrationSubmissionRepository(context)
                .PersistEvidenceOnlyAsync(evidence, CancellationToken.None)))!;

        await Assert.That(actual).IsSameReferenceAs(expected);
    }

    [Test]
    public async Task ExpectedRevisionIdentityConstraintIsResolvedFromMetadata()
    {
        await using ExploreDbContext context = CreateModelContext();
        string constraintName = RelationalConstraintDescriptorResolver
            .UniqueIndex<RegistrationSubmissionRevision>(
                context,
                nameof(RegistrationSubmissionRevision.TenantId),
                nameof(RegistrationSubmissionRevision.RegistrationSubmissionId),
                nameof(RegistrationSubmissionRevision.RevisionNumber))
            .Name;
        await Assert.That(RegistrationSubmissionRepository.IsRevisionIdentityUniqueViolation(
            context,
            CreateUniqueViolation(constraintName))).IsTrue();
    }

    [Test]
    public async Task RevisionPrimaryKeyViolationPropagatesOriginalException()
    {
        await using ExploreDbContext context = CreateModelContext();
        string primaryKey = RelationalConstraintDescriptorResolver
            .PrimaryKey<RegistrationSubmissionRevision>(context)
            .Name;
        DbUpdateException expected = CreateUniqueViolation(primaryKey);

        DbUpdateException actual = (await Assert.ThrowsAsync<DbUpdateException>(() =>
            ApplyRevisionUniqueFilterAsync(context, expected)))!;

        await Assert.That(actual).IsSameReferenceAs(expected);
    }

    [Test]
    public async Task UnrelatedUniqueConstraintIsExcludedFromEveryReplayWhitelist()
    {
        await using ExploreDbContext context = CreateModelContext();
        DbUpdateException unrelated = CreateUniqueViolation("ux_unrelated_registration_constraint");

        await Assert.That(RegistrationSubmissionRepository.IsSubmissionIdentityUniqueViolation(context, unrelated)).IsFalse();
        await Assert.That(RegistrationSubmissionRepository.IsRevisionIdentityUniqueViolation(context, unrelated)).IsFalse();
    }

    [Test]
    public async Task ExactCommittedRevisionRetryReturnsSuccessWithoutAppendingAgain()
    {
        Guid tenantId = Guid.CreateVersion7();
        await using ExploreDbContext context = CreateInMemoryContext(tenantId);
        RegistrationAttempt attempt = CreateAttempt(tenantId, 105);
        RegistrationSubmission submission = attempt.SubmitNative(Evidence(106), UtcNow.AddMinutes(1), null);
        Guid expectedStamp = submission.ConcurrencyStamp;
        RegistrationSubmissionRevision revision = submission.AddRevision(Evidence(107), UtcNow.AddMinutes(2), "provider-r1");
        context.RegistrationSubmissions.Add(submission);
        await context.SaveChangesAsync();
        context.ChangeTracker.Clear();
        await Assert.That(await context.RegistrationSubmissionRevisions.CountAsync()).IsEqualTo(1);

        bool replayed = await new RegistrationSubmissionRepository(context)
            .PersistRevisionAsync(submission, revision, expectedStamp, CancellationToken.None);

        await Assert.That(replayed).IsTrue();
        await Assert.That(await context.RegistrationSubmissionRevisions.CountAsync()).IsEqualTo(1);
    }

    [Test]
    [Arguments("Id")]
    [Arguments("EventId")]
    [Arguments("ReceivedEvidenceHash")]
    [Arguments("ProviderRevisionId")]
    [Arguments("ReceivedAt")]
    [Arguments("CreatedAt")]
    public async Task CommittedRevisionWithAnyDifferentImmutablePayloadReturnsFalse(string propertyName)
    {
        Guid tenantId = Guid.CreateVersion7();
        await using ExploreDbContext context = CreateInMemoryContext(tenantId);
        RegistrationAttempt attempt = CreateAttempt(tenantId, 108);
        RegistrationSubmission submission = attempt.SubmitNative(Evidence(109), UtcNow.AddMinutes(1), null);
        Guid expectedStamp = submission.ConcurrencyStamp;
        RegistrationSubmissionRevision revision = submission.AddRevision(Evidence(110), UtcNow.AddMinutes(2), "provider-r1");
        context.RegistrationSubmissions.Add(submission);
        await context.SaveChangesAsync();
        context.ChangeTracker.Clear();
        await Assert.That(await context.RegistrationSubmissionRevisions.CountAsync()).IsEqualTo(1);
        switch (propertyName)
        {
            case "Id":
                context.Entry(revision).Property(candidate => candidate.Id).CurrentValue = Guid.CreateVersion7();
                break;
            case "EventId":
                context.Entry(revision).Property(candidate => candidate.EventId).CurrentValue = Guid.CreateVersion7();
                break;
            case "ReceivedEvidenceHash":
                context.Entry(revision).Property(candidate => candidate.ReceivedEvidenceHash).CurrentValue = Evidence(211);
                break;
            case "ProviderRevisionId":
                context.Entry(revision).Property(candidate => candidate.ProviderRevisionId).CurrentValue = "provider-r2";
                break;
            case "ReceivedAt":
                context.Entry(revision).Property(candidate => candidate.ReceivedAt).CurrentValue = UtcNow.AddMinutes(3);
                break;
            case "CreatedAt":
                context.Entry(revision).Property(candidate => candidate.CreatedAt).CurrentValue = UtcNow.AddMinutes(3);
                break;
        }

        bool replayed = await new RegistrationSubmissionRepository(context)
            .PersistRevisionAsync(submission, revision, expectedStamp, CancellationToken.None);

        await Assert.That(replayed).IsFalse();
        await Assert.That(await context.RegistrationSubmissionRevisions.CountAsync()).IsEqualTo(1);
    }

    [Test]
    public async Task SameAcceptedConsumptionClaimReplaysAsExisting()
    {
        Guid tenantId = Guid.CreateVersion7();
        await using ExploreDbContext context = CreateInMemoryContext(tenantId);
        RegistrationAttempt attempt = CreateAttempt(tenantId, 111);
        Guid expectedStamp = attempt.ConcurrencyStamp;
        RegistrationSubmission accepted = attempt.SubmitNative(Evidence(112), UtcNow.AddMinutes(1), null);
        context.RegistrationSubmissions.Add(accepted);
        await context.SaveChangesAsync();
        context.ChangeTracker.Clear();

        RegistrationSubmissionPersistenceResult replayed = await new RegistrationSubmissionRepository(context)
            .PersistAcceptedAsync(attempt, accepted, expectedStamp, CancellationToken.None);

        await Assert.That(replayed.Outcome).IsEqualTo(RegistrationSubmissionPersistenceOutcome.Existing);
        await Assert.That(replayed.Submission!.AttemptConsumptionClaimId)
            .IsEqualTo(accepted.AttemptConsumptionClaimId);
    }

    [Test]
    public async Task SoftDeletedAcceptedConsumptionClaimCannotReplayAsExisting()
    {
        Guid tenantId = Guid.CreateVersion7();
        await using ExploreDbContext context = CreateInMemoryContext(tenantId);
        RegistrationAttempt attempt = CreateAttempt(tenantId, 113);
        Guid expectedStamp = attempt.ConcurrencyStamp;
        RegistrationSubmission accepted = attempt.SubmitNative(Evidence(114), UtcNow.AddMinutes(1), null);
        accepted.IsDeleted = true;
        accepted.DeletedAt = UtcNow.AddMinutes(2);
        context.RegistrationSubmissions.Add(accepted);
        await context.SaveChangesAsync();
        context.ChangeTracker.Clear();

        RegistrationSubmissionPersistenceResult replayed = await new RegistrationSubmissionRepository(context)
            .PersistAcceptedAsync(attempt, accepted, expectedStamp, CancellationToken.None);

        await Assert.That(replayed.Outcome).IsEqualTo(RegistrationSubmissionPersistenceOutcome.EvidenceOnlyConflict);
    }

    private static ExploreDbContext CreateModelContext()
    {
        var builder = TestDbContextOptions.Create<ExploreDbContext>()
            .UseNpgsql("Host=localhost;Database=unused;Username=unused;Password=unused")
            .UseSnakeCaseNamingConvention();
        return new ExploreDbContext(builder.Options);
    }

    private static ExploreDbContext CreateInMemoryContext(
        Guid tenantId,
        SaveChangesInterceptor? interceptor = null)
    {
        var options = TestDbContextOptions.Create<ExploreDbContext>()
            .UseTestInMemoryDatabase($"registration-attempt-{Guid.NewGuid():N}")
            .UseSnakeCaseNamingConvention();
        if (interceptor is not null)
        {
            options.AddInterceptors(interceptor);
        }

        return new ExploreDbContext(options.Options) { TenantContext = new TestTenantContext(tenantId) };
    }

    private static RegistrationAttempt CreateAttempt(Guid tenantId, int seed)
    {
        RegistrationAttempt attempt = RegistrationAttempt.Create(
            Guid.CreateVersion7(), tenantId, Guid.CreateVersion7(), Guid.CreateVersion7(), Guid.CreateVersion7(),
            Guid.CreateVersion7(), Guid.CreateVersion7(), Guid.CreateVersion7(), Guid.CreateVersion7(),
            Capability(seed), null, null, UtcNow, UtcNow.AddHours(1));
        attempt.ConcurrencyStamp = Guid.CreateVersion7();
        return attempt;
    }

    private static async Task AssertComputedUuid(IProperty property, string expectedSql)
    {
        await Assert.That(property.GetComputedColumnSql()).IsEqualTo(expectedSql);
        await Assert.That(property.GetIsStored()).IsTrue();
        await Assert.That(property.GetColumnType()).IsEqualTo("uuid");
        await Assert.That(property.IsNullable).IsFalse();
        await Assert.That(property.ValueGenerated).IsEqualTo(ValueGenerated.OnAdd);
        await Assert.That(property.GetBeforeSaveBehavior()).IsEqualTo(PropertySaveBehavior.Ignore);
        await Assert.That(property.GetAfterSaveBehavior()).IsEqualTo(PropertySaveBehavior.Throw);
    }

    private static DbUpdateException CreateUniqueViolation(string constraintName) => new(
        $"Simulated unique violation for {constraintName}.",
        new PostgresException(
            "duplicate key value violates unique constraint",
            "ERROR",
            "ERROR",
            PostgresErrorCodes.UniqueViolation,
            constraintName: constraintName));

    private static Task ApplyRevisionUniqueFilterAsync(
        ExploreDbContext context,
        DbUpdateException exception)
    {
        try
        {
            throw exception;
        }
        catch (DbUpdateException candidate) when (
            RegistrationSubmissionRepository.IsRevisionIdentityUniqueViolation(context, candidate))
        {
            return Task.CompletedTask;
        }
    }

    private static CapabilityTokenHash Capability(int seed) => CapabilityTokenHash.Create(Hash(seed));
    private static RegistrationEvidenceHash Evidence(int seed) => RegistrationEvidenceHash.Create(Hash(seed));
    private static string Hash(int seed) => Convert.ToBase64String(
        SHA256.HashData(Encoding.UTF8.GetBytes($"registration-characterization-{seed}")));

    private static string FindRepositoryRoot()
    {
        DirectoryInfo? directory = new(AppContext.BaseDirectory);
        while (directory is not null && !File.Exists(Path.Combine(directory.FullName, "Explore.slnx")))
        {
            directory = directory.Parent;
        }

        return directory?.FullName ?? throw new DirectoryNotFoundException("Repository root was not found.");
    }

    private static bool HasKey<TEntity>(IModel model, params string[] propertyNames) =>
        model.FindEntityType(typeof(TEntity))!.GetKeys().Any(key =>
            key.Properties.Select(property => property.Name).SequenceEqual(propertyNames));

    private sealed record TestTenantContext(Guid TenantId) : ITenantContext;

    private sealed class ThrowingSaveChangesInterceptor(Func<Exception> exceptionFactory) : SaveChangesInterceptor
    {
        public override ValueTask<InterceptionResult<int>> SavingChangesAsync(
            DbContextEventData eventData,
            InterceptionResult<int> result,
            CancellationToken cancellationToken = default) => throw exceptionFactory();
    }
}

[ClassDataSource<PostgreSqlContainerFixture>(Shared = SharedType.PerAssembly)]
[NotInParallel("PersistenceDb")]
public sealed class RegistrationAttemptPostgreSqlPersistenceTests(PostgreSqlContainerFixture fixture)
{
    private static readonly DateTime UtcNow = new(2026, 8, 2, 8, 0, 0, DateTimeKind.Utc);

    [Test]
    [Category("Runtime")]
    public async Task NativeSubmissionPersistsOneClaimDeduplicatesRevisesFinalizesAndFiltersTenant()
    {
        await fixture.ResetAsync();
        RuntimeScope scope = await SeedScopeAsync("attempt-native");
        RegistrationAttempt attempt = CreateAttempt(scope, 1);
        await using (ExploreDbContext setup = TenantContext(scope.TenantId))
        {
            setup.RegistrationAttempts.Add(attempt);
            await setup.SaveChangesAsync();
        }

        Guid expectedStamp = attempt.ConcurrencyStamp;
        RegistrationSubmission accepted = attempt.SubmitNative(Evidence(2), UtcNow.AddMinutes(1), Transport(3));
        RegistrationSubmissionPersistenceResult inserted;
        await using (ExploreDbContext context = TenantContext(scope.TenantId))
        {
            inserted = await new RegistrationSubmissionRepository(context)
                .PersistAcceptedAsync(attempt, accepted, expectedStamp, CancellationToken.None);
        }

        await using (ExploreDbContext replayContext = TenantContext(scope.TenantId))
        {
            RegistrationAttempt persistedAttempt = await replayContext.RegistrationAttempts.AsNoTracking().SingleAsync();
            RegistrationSubmission replay = RegistrationSubmission.CreateNativeEvidenceOnly(
                persistedAttempt, Evidence(2), UtcNow.AddMinutes(2), Transport(4));
            RegistrationSubmissionPersistenceResult replayed = await new RegistrationSubmissionRepository(replayContext)
                .PersistEvidenceOnlyAsync(replay, CancellationToken.None);
            await Assert.That(replayed.Outcome).IsEqualTo(RegistrationSubmissionPersistenceOutcome.EvidenceOnlyConflict);
        }

        Guid submissionStamp;
        await using (ExploreDbContext revisionContext = TenantContext(scope.TenantId))
        {
            var repository = new RegistrationSubmissionRepository(revisionContext);
            RegistrationSubmission submission = (await repository.GetSubmissionAsync(scope.TenantId, accepted.Id, CancellationToken.None))!;
            submissionStamp = submission.ConcurrencyStamp;
            RegistrationSubmissionRevision revision = submission.AddRevision(Evidence(5), UtcNow.AddMinutes(3), "revision-1");
            await Assert.That(await repository.PersistRevisionAsync(submission, revision, submissionStamp, CancellationToken.None)).IsTrue();
        }

        await using (ExploreDbContext rotationContext = TenantContext(scope.TenantId))
        {
            await rotationContext.RegistrationAttempts.ExecuteUpdateAsync(setters => setters
                .SetProperty(row => row.ConcurrencyStamp, Guid.CreateVersion7()));
        }

        await using (ExploreDbContext finalizationContext = TenantContext(scope.TenantId))
        {
            var repository = new RegistrationSubmissionRepository(finalizationContext);
            RegistrationAttempt persistedAttempt = (await repository.GetAttemptAsync(scope.TenantId, attempt.Id, CancellationToken.None))!;
            RegistrationSubmission submission = (await repository.GetSubmissionAsync(scope.TenantId, accepted.Id, CancellationToken.None))!;
            Guid expectedSubmissionStamp = submission.ConcurrencyStamp;
            submission.Finalize(persistedAttempt, UtcNow.AddMinutes(4));
            await Assert.That(await repository.PersistFinalizationAsync(submission, expectedSubmissionStamp, CancellationToken.None)).IsTrue();
        }

        await using (ExploreDbContext verification = TenantContext(scope.TenantId))
        {
            RegistrationAttempt persistedAttempt = await verification.RegistrationAttempts.AsNoTracking().SingleAsync();
            RegistrationSubmission persistedSubmission = await verification.RegistrationSubmissions
                .AsNoTracking().Include(row => row.Revisions).SingleAsync();
            await Assert.That(inserted.Outcome).IsEqualTo(RegistrationSubmissionPersistenceOutcome.Inserted);
            await Assert.That(await verification.RegistrationSubmissions.CountAsync()).IsEqualTo(1);
            await Assert.That(persistedAttempt.SubmissionConsumptionClaimId).IsEqualTo(persistedSubmission.AttemptConsumptionClaimId);
            await Assert.That(persistedSubmission.StatusId).IsEqualTo((int)RegistrationSubmissionStatusEnum.Finalized);
            await Assert.That(persistedSubmission.Revisions.Single().RevisionNumber).IsEqualTo(1);
        }

        await using (ExploreDbContext otherTenant = TenantContext(Guid.CreateVersion7()))
        {
            await Assert.That(await otherTenant.RegistrationAttempts.CountAsync()).IsEqualTo(0);
            await Assert.That(await otherTenant.RegistrationSubmissions.CountAsync()).IsEqualTo(0);
        }
    }

    [Test]
    [Category("Runtime")]
    public async Task ConcurrentAcceptedConsumersHaveOneWinnerAndOnePersistedRow()
    {
        await fixture.ResetAsync();
        RuntimeScope scope = await SeedScopeAsync("attempt-race");
        RegistrationAttempt seedAttempt = CreateAttempt(scope, 10);
        await using (ExploreDbContext setup = TenantContext(scope.TenantId))
        {
            setup.RegistrationAttempts.Add(seedAttempt);
            await setup.SaveChangesAsync();
        }

        var bothRead = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        int readers = 0;
        async Task<RegistrationSubmissionPersistenceOutcome> ConsumeAsync(int evidenceSeed)
        {
            await using ExploreDbContext context = TenantContext(scope.TenantId);
            RegistrationAttempt attempt = await context.RegistrationAttempts.AsNoTracking().SingleAsync();
            if (Interlocked.Increment(ref readers) == 2) bothRead.TrySetResult();
            await bothRead.Task.WaitAsync(TimeSpan.FromSeconds(30));
            Guid expectedStamp = attempt.ConcurrencyStamp;
            RegistrationSubmission submission = attempt.SubmitNative(Evidence(evidenceSeed), UtcNow.AddMinutes(1), null);
            return (await new RegistrationSubmissionRepository(context)
                .PersistAcceptedAsync(attempt, submission, expectedStamp, CancellationToken.None)).Outcome;
        }

        RegistrationSubmissionPersistenceOutcome[] outcomes = await Task.WhenAll(ConsumeAsync(11), ConsumeAsync(12));
        await Assert.That(outcomes.Count(outcome => outcome == RegistrationSubmissionPersistenceOutcome.Inserted)).IsEqualTo(1);
        await Assert.That(outcomes.Count(outcome => outcome == RegistrationSubmissionPersistenceOutcome.AttemptUnavailable)).IsEqualTo(1);
        await using ExploreDbContext verification = TenantContext(scope.TenantId);
        await Assert.That(await verification.RegistrationSubmissions.CountAsync()).IsEqualTo(1);
        await Assert.That((await verification.RegistrationAttempts.AsNoTracking().SingleAsync()).StatusId)
            .IsEqualTo((int)RegistrationAttemptStatusEnum.Consumed);
    }

    [Test]
    [Category("Runtime")]
    public async Task ProviderTupleIgnoresMappingRevisionAndDatabaseRejectsMalformedTuple()
    {
        await fixture.ResetAsync();
        RuntimeScope providerScope = await SeedScopeAsync("attempt-provider", true);
        Guid bindingId = providerScope.ProviderBindingId
            ?? throw new InvalidOperationException("Provider scope did not create a binding.");
        RegistrationEvidenceHash mappingRevisionHash = providerScope.ProviderMappingRevisionHash
            ?? throw new InvalidOperationException("Provider scope did not publish a binding mapping revision.");
        RegistrationAttempt firstAttempt = CreateAttempt(providerScope, 20, bindingId, mappingRevisionHash);
        RegistrationAttempt secondAttempt = CreateAttempt(providerScope, 22, bindingId, mappingRevisionHash);
        await using (ExploreDbContext invalidMapping = TenantContext(providerScope.TenantId))
        {
            invalidMapping.RegistrationAttempts.Add(CreateAttempt(providerScope, 23, bindingId, Evidence(23)));
            await Assert.That(async () => await invalidMapping.SaveChangesAsync()).Throws<InvalidOperationException>();
        }
        await using (ExploreDbContext setup = TenantContext(providerScope.TenantId))
        {
            setup.RegistrationAttempts.AddRange(firstAttempt, secondAttempt);
            await setup.SaveChangesAsync();
            PostgresException? rejectedMapping = await Assert.ThrowsAsync<PostgresException>(() =>
                setup.Database.ExecuteSqlInterpolatedAsync(
                    $"UPDATE registration_attempts SET provider_mapping_revision_hash = {Evidence(23).Value} WHERE id = {firstAttempt.Id}"));
            await Assert.That(rejectedMapping!.SqlState).IsEqualTo(PostgresErrorCodes.ForeignKeyViolation);
        }

        Guid expectedStamp = firstAttempt.ConcurrencyStamp;
        RegistrationSubmission accepted = firstAttempt.SubmitProvider(
            Evidence(24), UtcNow.AddMinutes(1), null, "provider-submission", "response-1", null, null);
        await using (ExploreDbContext firstContext = TenantContext(providerScope.TenantId))
        {
            RegistrationSubmissionPersistenceResult result = await new RegistrationSubmissionRepository(firstContext)
                .PersistAcceptedAsync(firstAttempt, accepted, expectedStamp, CancellationToken.None);
            await Assert.That(result.Outcome).IsEqualTo(RegistrationSubmissionPersistenceOutcome.Inserted);
        }

        RegistrationSubmission duplicate = RegistrationSubmission.CreateProviderEvidenceOnly(
            secondAttempt, Evidence(25), UtcNow.AddMinutes(2), null,
            "provider-submission", "response-1", null, null);
        await using (ExploreDbContext secondContext = TenantContext(providerScope.TenantId))
        {
            RegistrationSubmissionPersistenceResult result = await new RegistrationSubmissionRepository(secondContext)
                .PersistEvidenceOnlyAsync(duplicate, CancellationToken.None);
            await Assert.That(result.Outcome).IsEqualTo(RegistrationSubmissionPersistenceOutcome.EvidenceOnlyConflict);
        }

        await using ExploreDbContext verification = TenantContext(providerScope.TenantId);
        var providerIdentity = verification.Model.FindEntityType(typeof(RegistrationSubmission))!
            .GetIndexes().Single(index => index.Properties.Select(property => property.Name).SequenceEqual(
            [
                nameof(RegistrationSubmission.TenantId),
                nameof(RegistrationSubmission.RegistrationProviderBindingId),
                nameof(RegistrationSubmission.ProviderSubmissionId),
                nameof(RegistrationSubmission.ProviderResponseRevision)
            ]));
        await Assert.That(providerIdentity.IsUnique).IsTrue();
        await Assert.That(await verification.RegistrationSubmissions.CountAsync()).IsEqualTo(1);
        RegistrationSubmission persisted = await verification.RegistrationSubmissions.AsNoTracking().SingleAsync();
        await Assert.That(persisted.ProviderMappingRevisionHash).IsEqualTo(mappingRevisionHash);

        await using NpgsqlConnection connection = new(fixture.ConnectionString);
        await connection.OpenAsync();
        await using NpgsqlCommand malformed = new(
            "UPDATE registration_submissions SET provider_response_revision = NULL WHERE id = @id", connection);
        malformed.Parameters.AddWithValue("id", persisted.Id);
        PostgresException? exception = await Assert.ThrowsAsync<PostgresException>(async () => await malformed.ExecuteNonQueryAsync());
        await Assert.That(exception).IsNotNull();
        await Assert.That(exception!.SqlState).IsEqualTo(PostgresErrorCodes.CheckViolation);
    }

    [Test]
    [Category("Runtime")]
    public async Task ProviderEvidenceOnlyThenAcceptedReturnsTypedConflictWithoutConsumingAttempt()
    {
        await fixture.ResetAsync();
        RuntimeScope scope = await SeedScopeAsync("attempt-provider-evidence-conflict", true);
        Guid bindingId = scope.ProviderBindingId
            ?? throw new InvalidOperationException("Provider scope did not create a binding.");
        RegistrationEvidenceHash mappingRevisionHash = scope.ProviderMappingRevisionHash
            ?? throw new InvalidOperationException("Provider scope did not publish a binding mapping revision.");
        RegistrationAttempt evidenceAttempt = CreateAttempt(scope, 40, bindingId, mappingRevisionHash);
        RegistrationAttempt acceptedAttempt = CreateAttempt(scope, 42, bindingId, mappingRevisionHash);
        await using (ExploreDbContext setup = TenantContext(scope.TenantId))
        {
            setup.RegistrationAttempts.AddRange(evidenceAttempt, acceptedAttempt);
            await setup.SaveChangesAsync();
        }

        RegistrationSubmission evidence = RegistrationSubmission.CreateProviderEvidenceOnly(
            evidenceAttempt, Evidence(44), UtcNow.AddMinutes(1), null, "provider-id", "response-1", null, null);
        await using (ExploreDbContext evidenceContext = TenantContext(scope.TenantId))
        {
            await Assert.That((await new RegistrationSubmissionRepository(evidenceContext)
                .PersistEvidenceOnlyAsync(evidence, CancellationToken.None)).Outcome)
                .IsEqualTo(RegistrationSubmissionPersistenceOutcome.Inserted);
        }

        Guid expectedStamp = acceptedAttempt.ConcurrencyStamp;
        RegistrationSubmission accepted = acceptedAttempt.SubmitProvider(
            Evidence(45), UtcNow.AddMinutes(2), null, "provider-id", "response-1", null, null);
        await using ExploreDbContext acceptedContext = TenantContext(scope.TenantId);
        RegistrationSubmissionPersistenceResult result = await new RegistrationSubmissionRepository(acceptedContext)
            .PersistAcceptedAsync(acceptedAttempt, accepted, expectedStamp, CancellationToken.None);

        await Assert.That(result.Outcome).IsEqualTo(RegistrationSubmissionPersistenceOutcome.EvidenceOnlyConflict);
        await Assert.That((await acceptedContext.RegistrationAttempts.AsNoTracking()
            .SingleAsync(candidate => candidate.Id == acceptedAttempt.Id)).StatusId)
            .IsEqualTo((int)RegistrationAttemptStatusEnum.Active);
    }

    [Test]
    [Category("Runtime")]
    public async Task DatabaseRejectsChannelBindingAndOrderWorkflowMismatches()
    {
        await fixture.ResetAsync();
        RuntimeScope scope = await SeedScopeAsync("attempt-lineage-mismatch", true);
        RegistrationEvidenceHash mappingRevisionHash = scope.ProviderMappingRevisionHash
            ?? throw new InvalidOperationException("Provider scope did not publish a binding mapping revision.");
        await using (ExploreDbContext bindingContext = TenantContext(scope.TenantId))
        {
            RegistrationProviderConnection otherConnection = RegistrationProviderConnection.Create(
                scope.TenantId,
                "other provider",
                RegistrationProviderKindEnum.ExternalForm,
                RegistrationProviderDeploymentKindEnum.HostedSaas,
                "FORMBRICKS",
                "HOSTED_SAAS",
                "v1",
                "formbricks-policy-v1",
                "evidence-v1",
                "https://provider.example/management",
                "https://provider.example",
                "workspace-other-provider",
                null,
                null,
                UtcNow);
            RegistrationProviderBinding otherBinding = RegistrationProviderBinding.Create(
                scope.TenantId,
                otherConnection.Id,
                scope.FormId,
                scope.FormVersionId,
                RegistrationProviderPresentationModeEnum.Redirect,
                RegistrationProviderCollectionModeEnum.ProviderHosted,
                RegistrationProviderCompletionModeEnum.Callback,
                RegistrationProviderTrustLevelEnum.SelectedFields,
                null,
                UtcNow);
            otherBinding.Publish(mappingRevisionHash, UtcNow);
            bindingContext.AddRange(otherConnection, otherBinding);
            await bindingContext.SaveChangesAsync();

            RegistrationAttempt bindingMismatch = CreateAttempt(scope, 46, otherBinding.Id, mappingRevisionHash);
            bindingContext.RegistrationAttempts.Add(bindingMismatch);
            await Assert.That(async () => await bindingContext.SaveChangesAsync()).Throws<DbUpdateException>();
        }

        await using ExploreDbContext setup = TenantContext(scope.TenantId);
        RegistrationAttempt validAttempt = CreateAttempt(
            scope, 47, scope.ProviderBindingId, mappingRevisionHash);
        setup.RegistrationAttempts.Add(validAttempt);
        await setup.SaveChangesAsync();
        PostgresException? rejectedBinding = await Assert.ThrowsAsync<PostgresException>(() =>
            setup.Database.ExecuteSqlInterpolatedAsync(
                $"UPDATE registration_attempts SET registration_provider_binding_id = {Guid.CreateVersion7()} WHERE id = {validAttempt.Id}"));
        await Assert.That(rejectedBinding!.SqlState).IsEqualTo(PostgresErrorCodes.ForeignKeyViolation);
        RegistrationWorkflow otherWorkflow = RegistrationWorkflow.Create(
            scope.TenantId, scope.EventId, "OTHER_WORKFLOW", UtcNow.AddMinutes(1));
        RegistrationRequirement otherRequirement = RegistrationRequirement.Create(
            otherWorkflow, 1, RegistrationRequirementCriticalityEnum.Required, false,
            RegistrationRequirementCompletionEffectEnum.BlocksRegistration,
            RegistrationAnswerSyncModeEnum.FULL_CANONICAL,
            RegistrationRequirementSubjectTypeEnum.AllOrders, null, UtcNow.AddMinutes(1));
        RegistrationChannel otherChannel = RegistrationChannel.Create(
            otherRequirement, 1, true, null, UtcNow.AddMinutes(1));
        otherRequirement.AddChannel(otherChannel);
        otherWorkflow.AddRequirement(otherRequirement);
        setup.Add(otherWorkflow);
        await setup.SaveChangesAsync();

        RegistrationAttempt workflowMismatch = RegistrationAttempt.Create(
            scope.TenantId, scope.EventId, scope.OrderId, otherWorkflow.Id, otherRequirement.Id, otherChannel.Id,
            scope.FormId, scope.FormVersionId, Capability(48), null, null, UtcNow.AddMinutes(1), UtcNow.AddMinutes(10));
        setup.RegistrationAttempts.Add(workflowMismatch);
        await Assert.That(async () => await setup.SaveChangesAsync()).Throws<DbUpdateException>();
    }

    [Test]
    [Category("Runtime")]
    public async Task SupersessionCanRotateToAnotherFormVersionWithinTheSameLineage()
    {
        await fixture.ResetAsync();
        RuntimeScope scope = await SeedScopeAsync("attempt-version-rotation");
        Guid replacementVersionId;
        await using (ExploreDbContext versionContext = TenantContext(scope.TenantId))
        {
            RegistrationForm form = await versionContext.RegistrationForms.Include(candidate => candidate.Versions)
                .SingleAsync(candidate => candidate.Id == scope.FormId);
            RegistrationFormVersion replacementVersion = RegistrationFormVersion.Create(
                form, 2, "en", null, null, UtcNow.AddMinutes(1));
            form.AddVersion(replacementVersion);
            await versionContext.SaveChangesAsync();
            replacementVersionId = replacementVersion.Id;
        }

        RegistrationAttempt original = CreateAttempt(scope, 49);
        RegistrationAttempt replacement = RegistrationAttempt.Create(
            scope.TenantId, scope.EventId, scope.OrderId, scope.WorkflowId, scope.RequirementId, scope.ChannelId,
            scope.FormId, replacementVersionId, Capability(50), null, null, UtcNow, UtcNow.AddMinutes(10));
        await using ExploreDbContext context = TenantContext(scope.TenantId);
        context.RegistrationAttempts.AddRange(original, replacement);
        await context.SaveChangesAsync();
        original.Supersede(replacement.Id, UtcNow.AddMinutes(2), "form version rotated");
        await context.SaveChangesAsync();

        await Assert.That((await context.RegistrationAttempts.AsNoTracking()
            .SingleAsync(candidate => candidate.Id == original.Id)).SupersededByRegistrationAttemptId)
            .IsEqualTo(replacement.Id);
    }

    [Test]
    [Category("Runtime")]
    public async Task ConcurrentSameIdentityAcceptedConsumersPersistOneAndReturnTypedConflict()
    {
        await fixture.ResetAsync();
        RuntimeScope scope = await SeedScopeAsync("attempt-same-identity-race");
        RegistrationAttempt seedAttempt = CreateAttempt(scope, 51);
        await using (ExploreDbContext setup = TenantContext(scope.TenantId))
        {
            setup.RegistrationAttempts.Add(seedAttempt);
            await setup.SaveChangesAsync();
        }

        var bothRead = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        int readers = 0;
        async Task<RegistrationSubmissionPersistenceOutcome> ConsumeAsync()
        {
            await using ExploreDbContext context = TenantContext(scope.TenantId);
            RegistrationAttempt attempt = await context.RegistrationAttempts.AsNoTracking().SingleAsync();
            if (Interlocked.Increment(ref readers) == 2) bothRead.TrySetResult();
            await bothRead.Task.WaitAsync(TimeSpan.FromSeconds(30));
            Guid expectedStamp = attempt.ConcurrencyStamp;
            RegistrationSubmission submission = attempt.SubmitNative(Evidence(52), UtcNow.AddMinutes(1), null);
            return (await new RegistrationSubmissionRepository(context)
                .PersistAcceptedAsync(attempt, submission, expectedStamp, CancellationToken.None)).Outcome;
        }

        RegistrationSubmissionPersistenceOutcome[] outcomes = await Task.WhenAll(ConsumeAsync(), ConsumeAsync());
        await Assert.That(outcomes.Count(outcome => outcome == RegistrationSubmissionPersistenceOutcome.Inserted)).IsEqualTo(1);
        await Assert.That(outcomes.Count(outcome => outcome == RegistrationSubmissionPersistenceOutcome.EvidenceOnlyConflict)).IsEqualTo(1);
    }

    [Test]
    [Category("Runtime")]
    public async Task SoftDeletedCapabilityNativeAndProviderIdentitiesCannotReplay()
    {
        await fixture.ResetAsync();
        RuntimeScope nativeScope = await SeedScopeAsync("attempt-soft-delete-native");
        RuntimeScope providerScope = await SeedScopeAsync("attempt-soft-delete-provider", true);
        Guid bindingId = providerScope.ProviderBindingId
            ?? throw new InvalidOperationException("Provider scope did not create a binding.");
        RegistrationEvidenceHash mappingRevisionHash = providerScope.ProviderMappingRevisionHash
            ?? throw new InvalidOperationException("Provider scope did not publish a binding mapping revision.");
        RegistrationAttempt nativeAttempt = CreateAttempt(nativeScope, 53);
        RegistrationAttempt providerAttempt = CreateAttempt(providerScope, 54, bindingId, mappingRevisionHash);
        RegistrationAttempt providerReplayAttempt = CreateAttempt(providerScope, 56, bindingId, mappingRevisionHash);
        await using (ExploreDbContext setup = fixture.CreateDbContext())
        {
            setup.RegistrationAttempts.AddRange(nativeAttempt, providerAttempt, providerReplayAttempt);
            await setup.SaveChangesAsync();
        }

        RegistrationSubmission native = RegistrationSubmission.CreateNativeEvidenceOnly(
            nativeAttempt, Evidence(58), UtcNow.AddMinutes(1), null);
        RegistrationSubmission provider = RegistrationSubmission.CreateProviderEvidenceOnly(
            providerAttempt, Evidence(59), UtcNow.AddMinutes(1), null, "retained-provider", "response-1", null, null);
        await using (ExploreDbContext insert = fixture.CreateDbContext())
        {
            var repository = new RegistrationSubmissionRepository(insert);
            await repository.PersistEvidenceOnlyAsync(native, CancellationToken.None);
            await repository.PersistEvidenceOnlyAsync(provider, CancellationToken.None);
            await insert.RegistrationAttempts.Where(row => row.Id == nativeAttempt.Id)
                .ExecuteUpdateAsync(setters => setters.SetProperty(row => row.IsDeleted, true));
            await insert.RegistrationSubmissions.ExecuteUpdateAsync(setters => setters.SetProperty(row => row.IsDeleted, true));
        }

        RegistrationAttempt duplicateCapability = RegistrationAttempt.Create(
            nativeScope.TenantId, nativeScope.EventId, nativeScope.OrderId, nativeScope.WorkflowId,
            nativeScope.RequirementId, nativeScope.ChannelId, nativeScope.FormId, nativeScope.FormVersionId,
            nativeAttempt.CapabilityTokenHash, null, null, UtcNow.AddMinutes(2), UtcNow.AddMinutes(10));
        await using (ExploreDbContext duplicateContext = TenantContext(nativeScope.TenantId))
        {
            duplicateContext.RegistrationAttempts.Add(duplicateCapability);
            await Assert.That(async () => await duplicateContext.SaveChangesAsync()).Throws<DbUpdateException>();
        }

        await using (ExploreDbContext replayContext = TenantContext(nativeScope.TenantId))
        {
            RegistrationSubmission nativeReplay = RegistrationSubmission.CreateNativeEvidenceOnly(
                nativeAttempt, Evidence(58), UtcNow.AddMinutes(3), null);
            await Assert.That((await new RegistrationSubmissionRepository(replayContext)
                .PersistEvidenceOnlyAsync(nativeReplay, CancellationToken.None)).Outcome)
                .IsEqualTo(RegistrationSubmissionPersistenceOutcome.EvidenceOnlyConflict);
        }

        await using (ExploreDbContext providerReplayContext = TenantContext(providerScope.TenantId))
        {
            RegistrationSubmission providerReplay = RegistrationSubmission.CreateProviderEvidenceOnly(
                providerReplayAttempt, Evidence(60), UtcNow.AddMinutes(3), null,
                "retained-provider", "response-1", null, null);
            await Assert.That((await new RegistrationSubmissionRepository(providerReplayContext)
                .PersistEvidenceOnlyAsync(providerReplay, CancellationToken.None)).Outcome)
                .IsEqualTo(RegistrationSubmissionPersistenceOutcome.EvidenceOnlyConflict);
        }
    }

    [Test]
    [Category("Runtime")]
    public async Task DistinctNativeAttemptsAndPayloadsRemainDistinct()
    {
        await fixture.ResetAsync();
        RuntimeScope scope = await SeedScopeAsync("attempt-distinct");
        RegistrationAttempt first = CreateAttempt(scope, 26);
        RegistrationAttempt second = CreateAttempt(scope, 27);
        await using (ExploreDbContext setup = TenantContext(scope.TenantId))
        {
            setup.RegistrationAttempts.AddRange(first, second);
            await setup.SaveChangesAsync();
        }

        RegistrationSubmission firstEvidence = RegistrationSubmission.CreateNativeEvidenceOnly(first, Evidence(28), UtcNow.AddMinutes(1), null);
        RegistrationSubmission differentPayload = RegistrationSubmission.CreateNativeEvidenceOnly(first, Evidence(29), UtcNow.AddMinutes(2), null);
        RegistrationSubmission differentAttempt = RegistrationSubmission.CreateNativeEvidenceOnly(second, Evidence(28), UtcNow.AddMinutes(1), null);
        await using (ExploreDbContext context = TenantContext(scope.TenantId))
        {
            var repository = new RegistrationSubmissionRepository(context);
            await Assert.That((await repository.PersistEvidenceOnlyAsync(firstEvidence, CancellationToken.None)).Outcome)
                .IsEqualTo(RegistrationSubmissionPersistenceOutcome.Inserted);
            await Assert.That((await repository.PersistEvidenceOnlyAsync(differentPayload, CancellationToken.None)).Outcome)
                .IsEqualTo(RegistrationSubmissionPersistenceOutcome.Inserted);
            await Assert.That((await repository.PersistEvidenceOnlyAsync(differentAttempt, CancellationToken.None)).Outcome)
                .IsEqualTo(RegistrationSubmissionPersistenceOutcome.Inserted);
            await Assert.That(await context.RegistrationSubmissions.CountAsync()).IsEqualTo(3);
        }
    }

    [Test]
    [Category("Runtime")]
    public async Task LateEvidencePersistsButCannotFinalizeAndDatabaseRejectsCrossTenantLineageAndMalformedTuple()
    {
        await fixture.ResetAsync();
        RuntimeScope first = await SeedScopeAsync("attempt-late-a");
        RuntimeScope second = await SeedScopeAsync("attempt-late-b");
        RegistrationAttempt replacement = CreateAttempt(first, 30);
        RegistrationAttempt superseded = CreateAttempt(first, 31);
        RegistrationAttempt expired = CreateAttempt(first, 34);
        await using (ExploreDbContext setup = TenantContext(first.TenantId))
        {
            setup.RegistrationAttempts.AddRange(replacement, superseded, expired);
            await setup.SaveChangesAsync();
            superseded.Supersede(replacement.Id, UtcNow.AddMinutes(1), "new form version");
            expired.Expire(UtcNow.AddMinutes(10));
            await setup.SaveChangesAsync();
        }

        RegistrationSubmission late = RegistrationSubmission.CreateNativeEvidenceOnly(
            superseded, Evidence(32), UtcNow.AddMinutes(2), null);
        await using (ExploreDbContext lateContext = TenantContext(first.TenantId))
        {
            RegistrationSubmissionPersistenceResult result = await new RegistrationSubmissionRepository(lateContext)
                .PersistEvidenceOnlyAsync(late, CancellationToken.None);
            await Assert.That(result.Outcome).IsEqualTo(RegistrationSubmissionPersistenceOutcome.Inserted);
            RegistrationSubmission expiredEvidence = RegistrationSubmission.CreateNativeEvidenceOnly(
                expired, Evidence(35), UtcNow.AddMinutes(11), null);
            RegistrationSubmissionPersistenceResult expiredResult = await new RegistrationSubmissionRepository(lateContext)
                .PersistEvidenceOnlyAsync(expiredEvidence, CancellationToken.None);
            await Assert.That(expiredResult.Outcome).IsEqualTo(RegistrationSubmissionPersistenceOutcome.Inserted);
            await Assert.That(expiredEvidence.IsFinalizable).IsFalse();
        }
        await Assert.That(late.IsFinalizable).IsFalse();
        await Assert.That(() => late.Finalize(superseded, UtcNow.AddMinutes(3))).Throws<InvalidOperationException>();

        RegistrationAttempt crossTenant = RegistrationAttempt.Create(
            first.TenantId, second.EventId, first.OrderId, first.WorkflowId, first.RequirementId, first.ChannelId,
            first.FormId, first.FormVersionId, Capability(33), null, null, UtcNow, UtcNow.AddMinutes(10));
        await using (ExploreDbContext invalid = fixture.CreateDbContext())
        {
            invalid.RegistrationAttempts.Add(crossTenant);
            DbUpdateException? exception = await Assert.ThrowsAsync<DbUpdateException>(async () => await invalid.SaveChangesAsync());
            await Assert.That(exception).IsNotNull();
            await Assert.That(exception!.ToString()).DoesNotContain(Capability(33).Value);
        }

        await using NpgsqlConnection connection = new(fixture.ConnectionString);
        await connection.OpenAsync();
        await using NpgsqlCommand columns = new(
            "SELECT COUNT(*) FROM information_schema.columns WHERE table_name = 'registration_attempts' AND column_name IN ('capability_token', 'plaintext_capability')", connection);
        await Assert.That(Convert.ToInt64(await columns.ExecuteScalarAsync())).IsEqualTo(0L);
    }

    private ExploreDbContext TenantContext(Guid tenantId) =>
        fixture.CreateTenantFilteredDbContext(new TestTenantContext(tenantId));

    private async Task<RuntimeScope> SeedScopeAsync(string slug, bool withProviderBinding = false)
    {
        await using ExploreDbContext context = fixture.CreateDbContext();
        Tenant tenant = new() { FullName = slug, Slug = $"{slug}-{Guid.NewGuid():N}", TenantStatusId = 2, TenantStatus = null! };
        User user = new() { Pii = new UserPii { Email = $"{Guid.NewGuid():N}@example.com", FirstName = "Runtime", LastName = "Owner" } };
        context.AddRange(tenant, user);
        await context.SaveChangesAsync();
        Actor actor = new() { Pii = new ActorPii { DisplayName = slug }, ActorTypeId = 1, ActorType = null!, UserId = user.Id };
        context.Actors.Add(actor);
        await context.SaveChangesAsync();
        Explore.Domain.Event @event = new(EventStatusEnum.Draft)
        {
            Id = Guid.CreateVersion7(),
            Title = slug,
            ActorId = actor.Id,
            Actor = null!,
            TenantId = tenant.Id,
            Tenant = null!,
            EventStatus = null!,
            EventFormatId = 1,
            EventFormat = null!,
            EventProvenanceTypeId = 1,
            VisibilityTypeId = 1,
            VisibilityType = null!,
            ConcurrencyStamp = Guid.CreateVersion7()
        };
        context.Events.Add(@event);
        await context.SaveChangesAsync();

        EventTicketCatalogVersion catalog = EventTicketCatalogVersion.Create(tenant.Id, @event.Id, "EUR", 1);
        RegistrationWorkflow workflow = RegistrationWorkflow.Create(tenant.Id, @event.Id, "ATTENDEE_REGISTRATION", UtcNow);
        RegistrationRequirement requirement = RegistrationRequirement.Create(
            workflow, 1, RegistrationRequirementCriticalityEnum.Required, false,
            RegistrationRequirementCompletionEffectEnum.BlocksRegistration,
            RegistrationAnswerSyncModeEnum.FULL_CANONICAL,
            RegistrationRequirementSubjectTypeEnum.AllOrders, null, UtcNow);
        RegistrationForm form = RegistrationForm.Create(tenant.Id, @event.Id, "platform.registration", "runtime", "Runtime", UtcNow);
        RegistrationFormVersion version = RegistrationFormVersion.Create(form, 1, "en", null, null, UtcNow);
        form.AddVersion(version);
        RegistrationProviderBinding? providerBinding = null;
        RegistrationEvidenceHash? providerMappingRevisionHash = null;
        if (withProviderBinding)
        {
            RegistrationProviderConnection connection = RegistrationProviderConnection.Create(
                tenant.Id,
                $"{slug} provider",
                RegistrationProviderKindEnum.ExternalForm,
                RegistrationProviderDeploymentKindEnum.HostedSaas,
                "FORMBRICKS",
                "HOSTED_SAAS",
                "v1",
                "formbricks-policy-v1",
                "evidence-v1",
                "https://provider.example/management",
                "https://provider.example",
                $"workspace-{slug}",
                null,
                null,
                UtcNow);
            providerBinding = RegistrationProviderBinding.Create(
                tenant.Id,
                connection.Id,
                form.Id,
                version.Id,
                RegistrationProviderPresentationModeEnum.Redirect,
                RegistrationProviderCollectionModeEnum.ProviderHosted,
                RegistrationProviderCompletionModeEnum.Callback,
                RegistrationProviderTrustLevelEnum.SelectedFields,
                null,
                UtcNow);
            providerMappingRevisionHash = Evidence(19);
            providerBinding.Publish(providerMappingRevisionHash, UtcNow);
            context.AddRange(connection, providerBinding);
        }

        RegistrationChannel channel = RegistrationChannel.Create(
            requirement,
            1,
            !withProviderBinding,
            providerBinding?.Id,
            UtcNow);
        requirement.AddChannel(channel);
        workflow.AddRequirement(requirement);
        context.AddRange(catalog, workflow, form);
        await context.SaveChangesAsync();

        RegistrationOrder order = RegistrationOrder.Create(
            tenant.Id, @event.Id, user.Id, null, BookingPartyTypeEnum.Individual, catalog.Id,
            RegistrationParticipationSnapshot.Create(Guid.CreateVersion7(), 4, 3, 2, GuestRecoveryPolicyEnum.VerifiedEmailRequired),
            workflow.Id, null, "EUR", UtcNow, UtcNow.AddHours(1));
        context.RegistrationOrders.Add(order);
        await context.SaveChangesAsync();
        return new(
            tenant.Id,
            @event.Id,
            order.Id,
            workflow.Id,
            requirement.Id,
            channel.Id,
            form.Id,
            version.Id,
            providerBinding?.Id,
            providerMappingRevisionHash);
    }

    private static RegistrationAttempt CreateAttempt(
        RuntimeScope scope,
        int seed,
        Guid? providerBindingId = null,
        RegistrationEvidenceHash? mappingHash = null) => RegistrationAttempt.Create(
        scope.TenantId, scope.EventId, scope.OrderId, scope.WorkflowId, scope.RequirementId, scope.ChannelId,
        scope.FormId, scope.FormVersionId, Capability(seed), providerBindingId, mappingHash, UtcNow, UtcNow.AddMinutes(10));

    private static CapabilityTokenHash Capability(int seed) => CapabilityTokenHash.Create(Hash(seed));
    private static RegistrationEvidenceHash Evidence(int seed) => RegistrationEvidenceHash.Create(Hash(seed));
    private static RegistrationTransportIdempotencyHash Transport(int seed) => RegistrationTransportIdempotencyHash.Create(Hash(seed));
    private static string Hash(int seed) => Convert.ToBase64String(SHA256.HashData(Encoding.UTF8.GetBytes($"registration-{seed}")));

    private sealed record RuntimeScope(
        Guid TenantId,
        Guid EventId,
        Guid OrderId,
        Guid WorkflowId,
        Guid RequirementId,
        Guid ChannelId,
        Guid FormId,
        Guid FormVersionId,
        Guid? ProviderBindingId,
        RegistrationEvidenceHash? ProviderMappingRevisionHash);

    private sealed record TestTenantContext(Guid TenantId) : ITenantContext;
}
