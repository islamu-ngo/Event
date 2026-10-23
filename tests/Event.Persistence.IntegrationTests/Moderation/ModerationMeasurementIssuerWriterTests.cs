using System.Security.Cryptography;
using Explore.Application.Contracts.Persistence;
using Explore.Domain.Enums;
using Microsoft.EntityFrameworkCore;

namespace Event.Persistence.IntegrationTests.Moderation;

public sealed class ModerationMeasurementIssuerWriterTests
{
    private static readonly DateTimeOffset Now = new(2026, 10, 8, 12, 0, 0, TimeSpan.Zero);

    [Test]
    public async Task NativeIssuerEnrollment_ExactReplayAndTerminalRevocationRetainOriginalTrust()
    {
        await using var database = await ModerationQualificationWriterTests.SourceDatabase.CreateAsync();
        using var key = ECDsa.Create(ECCurve.NamedCurves.nistP256);
        var request = Enrollment(database, key);
        await Assert.That((await database.ExecuteAsync(database.AdministratorId, request)).IsSuccess).IsTrue();
        await Assert.That((await database.ExecuteAsync(database.AdministratorId, request)).IsSuccess).IsTrue();
        var revoke = new RevokeModerationMeasurementIssuerSource
        {
            OperationId = Guid.CreateVersion7(), Target = database.Scope, ExpectedQualificationRevision = 2,
            EnrollmentId = request.EnrollmentId, ExpectedEnrollmentRevision = 1
        };
        await Assert.That((await database.ExecuteAsync(database.UrgencyAssessorId, revoke)).IsSuccess).IsTrue();
        await Assert.That((await database.ExecuteAsync(database.UrgencyAssessorId, revoke)).IsSuccess).IsTrue();
        await using var reader = database.Context();
        reader.ModerationTrustDomainId = database.Scope.TrustDomainId;
        var original = await reader.ModerationMeasurementIssuerEnrollments.SingleAsync();
        await Assert.That(original.PublicKeySpkiBase64).IsEqualTo(request.PublicKeySpkiBase64);
        await Assert.That(original.ApprovedByHumanId).IsEqualTo(database.AdministratorId);
        await Assert.That(original.RevokedByHumanId).IsEqualTo((Guid?)database.UrgencyAssessorId);
        await Assert.That(original.WriterGeneration).IsEqualTo(2L);
        await Assert.That(original.Revision).IsEqualTo(2L);
        await Assert.That((await reader.ModerationQualificationEpochs.SingleAsync()).Revision).IsEqualTo(3L);
        await Assert.That(await reader.ModerationSanctions.CountAsync()).IsEqualTo(0);
    }

    [Test]
    [Arguments(0)]
    [Arguments(1)]
    [Arguments(2)]
    [Arguments(3)]
    [Arguments(4)]
    public async Task UnqualifiedTrust_CannotCommitIssuerOrQualificationPrefix(int change)
    {
        await using var database = await ModerationQualificationWriterTests.SourceDatabase.CreateAsync();
        using var key = ECDsa.Create(change == 0 ? ECCurve.NamedCurves.nistP384 : ECCurve.NamedCurves.nistP256);
        var request = Enrollment(database, key);
        request = change switch
        {
            1 => request with { PublicKeySpkiBase64 = Convert.ToBase64String([1, 2, 3]) },
            2 => request with { ValidUntil = Now },
            _ => request
        };
        if (change == 4) database.ExpireGovernanceAfterSave = true;
        await Assert.That((await database.ExecuteAsync(change == 3 ? database.SubjectId : database.AdministratorId, request))
            .IsSuccess).IsFalse();
        await using var reader = database.Context();
        reader.ModerationTrustDomainId = database.Scope.TrustDomainId;
        await Assert.That(await reader.ModerationMeasurementIssuerEnrollments.CountAsync()).IsEqualTo(0);
        await Assert.That((await reader.ModerationQualificationEpochs.SingleAsync()).Revision).IsEqualTo(1L);
    }

    [Test]
    public async Task AKeyIdentityCannotBeReusedForDifferentTrustOrReplaceItsOriginalEnrollment()
    {
        await using var database = await ModerationQualificationWriterTests.SourceDatabase.CreateAsync();
        using var key = ECDsa.Create(ECCurve.NamedCurves.nistP256);
        var request = Enrollment(database, key);
        await Assert.That((await database.ExecuteAsync(database.AdministratorId, request)).IsSuccess).IsTrue();
        var replacement = request with
        {
            OperationId = Guid.CreateVersion7(), EnrollmentId = Guid.CreateVersion7(),
            ExpectedQualificationRevision = 2, Issuer = "substituted-issuer"
        };
        await Assert.That((await database.ExecuteAsync(database.AdministratorId, replacement)).IsSuccess).IsFalse();
        await Assert.That((await database.ExecuteAsync(database.AdministratorId,
            request with { CatalogueDigest = new string('e', 64) })).IsSuccess).IsFalse();
        await using var reader = database.Context();
        reader.ModerationTrustDomainId = database.Scope.TrustDomainId;
        await Assert.That(await reader.ModerationMeasurementIssuerEnrollments.CountAsync()).IsEqualTo(1);
        await Assert.That((await reader.ModerationQualificationEpochs.SingleAsync()).Revision).IsEqualTo(2L);
    }

    [Test]
    public async Task MissingOriginalApprovalReceipt_CannotBeReplacedByARevocationAssertion()
    {
        await using var database = await ModerationQualificationWriterTests.SourceDatabase.CreateAsync();
        using var key = ECDsa.Create(ECCurve.NamedCurves.nistP256);
        var request = Enrollment(database, key);
        await Assert.That((await database.ExecuteAsync(database.AdministratorId, request)).IsSuccess).IsTrue();
        await using (var writer = database.Context())
        {
            writer.ModerationTrustDomainId = database.Scope.TrustDomainId;
            writer.ModerationQualificationMutationReceipts.Remove(await writer.ModerationQualificationMutationReceipts
                .SingleAsync(row => row.Id == request.OperationId));
            await writer.SaveChangesAsync();
        }
        await Assert.That((await database.ExecuteAsync(database.AdministratorId, new RevokeModerationMeasurementIssuerSource
        {
            OperationId = Guid.CreateVersion7(), Target = database.Scope, ExpectedQualificationRevision = 2,
            EnrollmentId = request.EnrollmentId, ExpectedEnrollmentRevision = 1
        })).IsSuccess).IsFalse();
        await using var reader = database.Context();
        reader.ModerationTrustDomainId = database.Scope.TrustDomainId;
        await Assert.That((await reader.ModerationMeasurementIssuerEnrollments.SingleAsync()).Revision).IsEqualTo(1L);
    }

    private static EnrollModerationMeasurementIssuerSource Enrollment(
        ModerationQualificationWriterTests.SourceDatabase database, ECDsa key) => new()
    {
        OperationId = Guid.CreateVersion7(), Target = database.Scope, ExpectedQualificationRevision = 1,
        EnrollmentId = Guid.CreateVersion7(), KeyId = Guid.CreateVersion7(), Issuer = "native-measurement-runner",
        PublicKeySpkiBase64 = Convert.ToBase64String(key.ExportSubjectPublicKeyInfo()),
        RunnerDigest = new string('a', 64), CatalogueDigest = new string('b', 64), TestBundleDigest = new string('c', 64),
        ValidUntil = Now.AddMinutes(5)
    };

    [Test]
    public async Task RemainingTenantAdministration_CannotReplayOrRevokeDeploymentSignerApproval()
    {
        await using var database = await ModerationQualificationWriterTests.SourceDatabase.CreateAsync();
        using var key = ECDsa.Create(ECCurve.NamedCurves.nistP256);
        var request = Enrollment(database, key);
        await Assert.That((await database.ExecuteAsync(database.AdministratorId, request)).IsSuccess).IsTrue();
        await using (var writer = database.Context())
        {
            writer.PlatformUserRoles.Remove(await writer.PlatformUserRoles.SingleAsync(row => row.UserId == database.AdministratorId));
            var membership = await writer.TenantUsers.SingleAsync(row => row.UserId == database.AdministratorId);
            var role = await writer.Roles.SingleAsync(row => row.Id == (int)RoleEnum.TenantAdmin);
            writer.TenantUserRoleGrants.Add(new()
            {
                Id = Guid.CreateVersion7(), TenantId = database.TenantId,
                Tenant = await writer.Tenants.SingleAsync(), TenantUserId = membership.Id, TenantUser = membership,
                RoleId = role.Id, RoleScopeId = (int)RoleScopeEnum.Tenant, Role = role, GrantedAt = Now.UtcDateTime
            });
            await writer.SaveChangesAsync();
        }
        await Assert.That((await database.ExecuteAsync(database.AdministratorId, request)).IsSuccess).IsFalse();
        await Assert.That((await database.ExecuteAsync(database.AdministratorId, new RevokeModerationMeasurementIssuerSource
        {
            OperationId = Guid.CreateVersion7(), Target = database.Scope, ExpectedQualificationRevision = 2,
            EnrollmentId = request.EnrollmentId, ExpectedEnrollmentRevision = 1
        })).IsSuccess).IsFalse();
        await using var reader = database.Context();
        reader.ModerationTrustDomainId = database.Scope.TrustDomainId;
        await Assert.That((await reader.ModerationMeasurementIssuerEnrollments.SingleAsync()).Revision).IsEqualTo(1L);
        await Assert.That((await reader.ModerationQualificationEpochs.SingleAsync()).Revision).IsEqualTo(2L);
    }
}
