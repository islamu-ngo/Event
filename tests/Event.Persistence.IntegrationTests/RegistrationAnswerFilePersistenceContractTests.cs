// ABOUTME: Verifies registration file metadata is tenant-filtered and storage references are tenant-contained.
// ABOUTME: Checks the EF model without modifying generated migrations or snapshots.

using Explore.Domain;
using Explore.Domain.Enums;
using Explore.Domain.ValueObjects;
using Explore.Persistence;
using Explore.Persistence.Repositories;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.EntityFrameworkCore.Metadata;

namespace Event.Persistence.IntegrationTests;

public sealed class RegistrationAnswerFilePersistenceContractTests
{
    [Test]
    public async Task ModelDeclaresTenantFiltersStorageContainmentAndQuarantineConstraints()
    {
        await using var context = new ExploreDbContext(
            TestDbContextOptions.Create<ExploreDbContext>()
                .UseNpgsql("Host=localhost;Database=unused;Username=unused;Password=unused")
                .UseSnakeCaseNamingConvention()
                .Options);
        IEntityType file = context.GetService<IDesignTimeModel>().Model
            .FindEntityType(typeof(RegistrationAnswerFile))!;
        IForeignKey storageForeignKey = file.GetForeignKeys()
            .Single(key => key.PrincipalEntityType.ClrType == typeof(StorageObject));
        IForeignKey submissionForeignKey = file.GetForeignKeys()
            .Single(key => key.PrincipalEntityType.ClrType == typeof(RegistrationSubmission));
        IForeignKey fieldForeignKey = file.GetForeignKeys()
            .Single(key => key.PrincipalEntityType.ClrType == typeof(RegistrationFormField));
        IEntityType release = context.GetService<IDesignTimeModel>().Model
            .FindEntityType(typeof(RegistrationAnswerFileRelease))!;
        string[] constraints = file.GetCheckConstraints()
            .Select(constraint => constraint.Name!)
            .ToArray();

        await Assert.That(file.GetDeclaredQueryFilters().Count()).IsEqualTo(2);
        await Assert.That(storageForeignKey.Properties.Select(property => property.Name))
            .IsEquivalentTo(["TenantId", "StorageObjectId"]);
        await Assert.That(storageForeignKey.PrincipalKey.Properties.Select(property => property.Name))
            .IsEquivalentTo(["TenantId", "Id"]);
        await Assert.That(submissionForeignKey.Properties.Select(property => property.Name))
            .IsEquivalentTo(["TenantId", "EventId", "RegistrationSubmissionId"]);
        await Assert.That(fieldForeignKey.Properties.Select(property => property.Name))
            .IsEquivalentTo([
                "TenantId", "EventId", "RegistrationFormId", "RegistrationFormVersionId",
                "RegistrationFormSectionId", "RegistrationFormFieldId", "FieldTypeId"]);
        await Assert.That(release.GetIndexes().Single(index => index.IsUnique).Properties.Select(property => property.Name))
            .IsEquivalentTo(["TenantId", "RegistrationAnswerFileId"]);
        await Assert.That(constraints).Contains("ck_registration_answer_files_quarantine_state");
        await Assert.That(constraints).Contains("ck_registration_answer_files_release_shape");
        await Assert.That(constraints).Contains("ck_registration_answer_files_scan_status");
        await Assert.That(constraints).Contains("ck_registration_answer_files_field_type");
    }

    [Test]
    public async Task QuarantineLookup_IgnoresOnlySoftDeleteAndStillHonorsTenant()
    {
        Guid tenantId = Guid.CreateVersion7();
        await using ExploreDbContext context = CreateContext(tenantId);
        StorageObject storageObject = CreateStorageObject(tenantId);
        RegistrationAnswerFile file = RegistrationAnswerFile.Create(
            tenantId, Guid.CreateVersion7(), CreateFileField(tenantId), storageObject, DateTime.UtcNow);
        file.IsDeleted = true;
        context.AddRange(storageObject, file);
        await context.SaveChangesAsync();

        var repository = new StorageObjectRepository(context);

        await Assert.That(await repository.IsRegistrationAnswerFileQuarantinedAsync(
            storageObject.Id, CancellationToken.None)).IsTrue();
        context.TenantContext = new TestTenantContext(Guid.CreateVersion7());
        await Assert.That(await repository.IsRegistrationAnswerFileQuarantinedAsync(
            storageObject.Id, CancellationToken.None)).IsFalse();
    }

    [Test]
    public async Task ReleaseAsync_RepeatedRequestReturnsFirstImmutableAudit()
    {
        Guid tenantId = Guid.CreateVersion7();
        Guid firstActor = Guid.CreateVersion7();
        DateTime now = new(2026, 8, 2, 21, 0, 0, DateTimeKind.Utc);
        await using ExploreDbContext context = CreateContext(tenantId);
        StorageObject storageObject = CreateStorageObject(tenantId);
        RegistrationAnswerFile file = RegistrationAnswerFile.Create(
            tenantId, Guid.CreateVersion7(), CreateFileField(tenantId), storageObject, now);
        context.AddRange(storageObject, file);
        await context.SaveChangesAsync();
        var repository = new RegistrationAnswerFileRepository(context);

        var first = await repository.ReleaseAsync(
            tenantId, file.Id, firstActor, "First review", now.AddMinutes(1), CancellationToken.None);
        var retry = await repository.ReleaseAsync(
            tenantId, file.Id, Guid.CreateVersion7(), "Retry must not overwrite", now.AddMinutes(2), CancellationToken.None);

        await Assert.That(first!.WasAlreadyReleased).IsFalse();
        await Assert.That(retry!.WasAlreadyReleased).IsTrue();
        await Assert.That(await context.RegistrationAnswerFileReleases.CountAsync()).IsEqualTo(1);
        RegistrationAnswerFileRelease audit = await context.RegistrationAnswerFileReleases.SingleAsync();
        await Assert.That(audit.ReleasedBy).IsEqualTo(firstActor);
        await Assert.That(audit.Reason).IsEqualTo("First review");
        await Assert.That(audit.ReleasedAt).IsEqualTo(now.AddMinutes(1));
    }

    [Test]
    public async Task PrivacyErasure_QueuesProviderDeletionAndFencesStorageBeforeRemovingMetadata()
    {
        Guid tenantId = Guid.CreateVersion7();
        Guid subjectId = Guid.CreateVersion7();
        await using ExploreDbContext context = CreateContext(tenantId);
        RegistrationFileScope scope = CreateRegistrationFileScope(tenantId, subjectId);
        context.AddRange(scope.Order, scope.Submission, scope.StorageObject, scope.File);
        await context.SaveChangesAsync();
        var erasure = new UserLocationPrivacyErasureRepository(context);

        IReadOnlyList<Explore.Application.Contracts.Persistence.PrivacyErasureProviderCandidate> candidates =
            await erasure.GetProviderCandidatesAsync(subjectId, CancellationToken.None);
        await erasure.EraseRegistrationAnswerFilesAsync(subjectId, CancellationToken.None);

        await Assert.That(candidates.Any(candidate =>
            candidate.ProviderKind == Explore.Domain.PrivacyErasureProviderKind.ObjectStorage &&
            candidate.Action == Explore.Domain.PrivacyErasureProviderAction.DeleteOwnedObject &&
            candidate.TargetId == scope.StorageObject.Id &&
            candidate.Locator == scope.ObjectKey)).IsTrue();
        await Assert.That(await context.RegistrationAnswerFiles
            .IgnoreQueryFilters().CountAsync()).IsEqualTo(0);
        StorageObject erasedStorage = await context.StorageObjects
            .IgnoreQueryFilters().SingleAsync(item => item.Id == scope.StorageObject.Id);
        await Assert.That(erasedStorage.LifecycleState).IsEqualTo(StorageObjectLifecycleStates.Deleted);
        await Assert.That(erasedStorage.IsDeleted).IsTrue();
        await Assert.That(erasedStorage.ObjectKey).IsNull();
        context.ChangeTracker.Clear();
        var storageRepository = new StorageObjectRepository(context);
        await Assert.That(await storageRepository.GetById(scope.StorageObject.Id)).IsNull();
    }

    private static ExploreDbContext CreateContext(Guid tenantId)
    {
        var context = new ExploreDbContext(TestDbContextOptions.Create<ExploreDbContext>()
            .UseTestInMemoryDatabase($"registration-answer-file-{Guid.NewGuid():N}")
            .Options)
        {
            TenantContext = new TestTenantContext(tenantId)
        };
        return context;
    }

    private static RegistrationFormField CreateFileField(Guid tenantId)
    {
        DateTime now = DateTime.UtcNow;
        RegistrationForm form = RegistrationForm.Create(
            tenantId, Guid.CreateVersion7(), "native", "files", "Files", now);
        RegistrationFormVersion version = RegistrationFormVersion.Create(form, 1, "en", null, null, now);
        RegistrationFormSection section = RegistrationFormSection.Create(
            Guid.CreateVersion7(), version, 1, "Documents", now);
        return RegistrationFormField.Create(
            Guid.CreateVersion7(), section, 1, "native", "document", "Document",
            RegistrationFieldTypeEnum.File, 1,
            RegistrationOrganizerVisibilityEnum.AuthorizedOrganizers,
            false, false, now);
    }

    private static StorageObject CreateStorageObject(Guid tenantId) => new()
    {
        Id = Guid.CreateVersion7(),
        TenantId = tenantId,
        FileTypeId = 1,
        FileType = null!,
        Tenant = null!,
        Uri = "/api/storageobject/file/content",
        ObjectKey = $"tenants/{tenantId:N}/{Guid.NewGuid():N}.pdf",
        Provider = StorageProviders.Local,
        FullName = "document.pdf",
        SafeDisplayName = "document.pdf",
        Extension = "pdf",
        ContentType = "application/pdf",
        Sha256Checksum = new string('a', 64),
        Size = 4,
        Visibility = StorageObjectVisibilities.PrivateOwner,
        Purpose = StorageObjectPurposes.Document,
        LifecycleState = StorageObjectLifecycleStates.Active,
        ConcurrencyStamp = Guid.CreateVersion7()
    };

    private static RegistrationFileScope CreateRegistrationFileScope(Guid tenantId, Guid subjectId)
    {
        DateTime now = new(2026, 8, 2, 22, 0, 0, DateTimeKind.Utc);
        Guid eventId = Guid.CreateVersion7();
        RegistrationWorkflow workflow = RegistrationWorkflow.Create(tenantId, eventId, "FILES", now);
        RegistrationRequirement requirement = RegistrationRequirement.Create(
            workflow, 1, RegistrationRequirementCriticalityEnum.Required, false,
            RegistrationRequirementCompletionEffectEnum.BlocksRegistration,
            RegistrationAnswerSyncModeEnum.FULL_CANONICAL,
            RegistrationRequirementSubjectTypeEnum.AllOrders, null, now);
        RegistrationChannel channel = RegistrationChannel.Create(requirement, 1, true, null, now);
        requirement.AddChannel(channel);
        workflow.AddRequirement(requirement);
        RegistrationForm form = RegistrationForm.Create(
            tenantId, eventId, "native", "files", "Files", now);
        RegistrationFormVersion version = RegistrationFormVersion.Create(form, 1, "en", null, null, now);
        RegistrationFormSection section = RegistrationFormSection.Create(
            Guid.CreateVersion7(), version, 1, "Documents", now);
        RegistrationFormField field = RegistrationFormField.Create(
            Guid.CreateVersion7(), section, 1, "native", "document", "Document",
            RegistrationFieldTypeEnum.File, 1,
            RegistrationOrganizerVisibilityEnum.AuthorizedOrganizers,
            false, false, now);
        RegistrationOrder order = RegistrationOrder.Create(
            tenantId, eventId, subjectId, null, BookingPartyTypeEnum.Individual,
            Guid.CreateVersion7(),
            RegistrationParticipationSnapshot.Create(
                Guid.CreateVersion7(), 4, 3, 2, GuestRecoveryPolicyEnum.VerifiedEmailRequired),
            workflow.Id, null, "EUR", now, now.AddHours(1));
        RegistrationAttempt attempt = RegistrationAttempt.Create(
            tenantId, eventId, order.Id, workflow.Id, requirement.Id, channel.Id, form.Id, version.Id,
            CapabilityTokenHash.Create(Convert.ToBase64String(new byte[32])), null, null,
            now, now.AddMinutes(10));
        RegistrationSubmission submission = RegistrationSubmission.Create(
            attempt, RegistrationEvidenceHash.Create(Convert.ToBase64String(new byte[32])),
            now.AddMinutes(1), null, null, null, null);
        StorageObject storageObject = CreateStorageObject(tenantId);
        string objectKey = storageObject.ObjectKey!;
        RegistrationAnswerFile file = RegistrationAnswerFile.Create(
            tenantId, submission.Id, field, storageObject, now.AddMinutes(2));
        return new RegistrationFileScope(order, submission, storageObject, file, objectKey);
    }

    private sealed record RegistrationFileScope(
        RegistrationOrder Order,
        RegistrationSubmission Submission,
        StorageObject StorageObject,
        RegistrationAnswerFile File,
        string ObjectKey);

    private sealed record TestTenantContext(Guid TenantId)
        : Explore.Application.Contracts.Infrastructure.ITenantContext;
}
