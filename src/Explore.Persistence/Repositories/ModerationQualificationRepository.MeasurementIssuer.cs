using System.Security.Cryptography;
using Explore.Application.Contracts.Persistence;
using Explore.Domain;
using Explore.Persistence.Database;
using Microsoft.EntityFrameworkCore;

namespace Explore.Persistence.Repositories;

public sealed partial class ModerationQualificationRepository
{
    private async Task<QualifiedSource> EnrollMeasurementIssuerAsync(
        EnrollModerationMeasurementIssuerSource source, ModerationSourceAuthority authority, DateTimeOffset now, CancellationToken token)
    {
        if (!authority.IsInstanceAdmin || source.Target != authority.Tenant || source.ValidUntil <= now)
            throw new AbortQualification(ModerationQualificationWriteOutcome.Denied);
        try
        {
            var bytes = Convert.FromBase64String(source.PublicKeySpkiBase64);
            using var key = ECDsa.Create();
            key.ImportSubjectPublicKeyInfo(bytes, out var read);
            if (read != bytes.Length || key.KeySize != 256 || key.ExportParameters(false).Curve.Oid.Value != "1.2.840.10045.3.1.7"
                || Convert.ToBase64String(key.ExportSubjectPublicKeyInfo()) != source.PublicKeySpkiBase64)
                throw new AbortQualification(ModerationQualificationWriteOutcome.Denied);
        }
        catch (FormatException) { throw new AbortQualification(ModerationQualificationWriteOutcome.Denied); }
        catch (CryptographicException) { throw new AbortQualification(ModerationQualificationWriteOutcome.Denied); }
        await using var slot = await RelationalNamedLock.AcquireTransactionAsync(context,
            $"moderation.measurement-issuer:{authority.Binding.Id:N}:{authority.Tenant.Id:N}:{source.KeyId:N}", token);
        if (await context.ModerationMeasurementIssuerEnrollments.AsNoTracking()
            .AnyAsync(row => row.HostBindingId == authority.Binding.Id && row.KeyId == source.KeyId, token))
            throw new AbortQualification(ModerationQualificationWriteOutcome.Conflict);
        await RelationalEntityRowFence.AcquireGlobalAsync<ModerationMeasurementIssuerEnrollment>(context, source.EnrollmentId, token);
        if (await context.ModerationMeasurementIssuerEnrollments.AnyAsync(row => row.Id == source.EnrollmentId, token))
            throw new AbortQualification(ModerationQualificationWriteOutcome.Conflict);
        var enrollment = ModerationMeasurementIssuerEnrollment.Enroll(source.EnrollmentId, authority.Binding.Id,
            authority.Binding.TrustDomainId, authority.Tenant.Id, source.KeyId, source.Issuer, source.PublicKeySpkiBase64,
            source.RunnerDigest, source.CatalogueDigest, source.TestBundleDigest, authority.UserId,
            checked(authority.Epoch.Revision + 1), now, source.ValidUntil);
        context.ModerationMeasurementIssuerEnrollments.Add(enrollment);
        return new(ModerationQualificationSourceKind.MeasurementIssuerEnrollment, enrollment.Id, 1,
            MutationValidUntil: enrollment.ValidUntil);
    }

    private async Task<QualifiedSource> RevokeMeasurementIssuerAsync(
        RevokeModerationMeasurementIssuerSource source, ModerationSourceAuthority authority, DateTimeOffset now, CancellationToken token)
    {
        if (!authority.IsInstanceAdmin || source.Target != authority.Tenant)
            throw new AbortQualification(ModerationQualificationWriteOutcome.Denied);
        var discovered = await context.ModerationMeasurementIssuerEnrollments.AsNoTracking()
            .SingleOrDefaultAsync(row => row.Id == source.EnrollmentId, token)
            ?? throw new AbortQualification(ModerationQualificationWriteOutcome.NotFound);
        await using var slot = await RelationalNamedLock.AcquireTransactionAsync(context,
            $"moderation.measurement-issuer:{authority.Binding.Id:N}:{authority.Tenant.Id:N}:{discovered.KeyId:N}", token);
        await RelationalEntityRowFence.AcquireGlobalAsync<ModerationMeasurementIssuerEnrollment>(context, source.EnrollmentId, token);
        var enrollment = await context.ModerationMeasurementIssuerEnrollments.SingleAsync(row => row.Id == source.EnrollmentId, token);
        enrollment.ValidatePersistedState();
        if (enrollment.HostBindingId != authority.Binding.Id || enrollment.TrustDomainId != authority.Binding.TrustDomainId
            || enrollment.TenantId != authority.Tenant.Id || enrollment.KeyId != discovered.KeyId
            || !authority.FencedHumans.Contains(enrollment.ApprovedByHumanId))
            throw new AbortQualification(ModerationQualificationWriteOutcome.Denied);
        if (enrollment.Revision != source.ExpectedEnrollmentRevision || enrollment.RevokedAt is not null)
            throw new AbortQualification(ModerationQualificationWriteOutcome.Conflict);
        await RequireTargetSourceReceiptAsync(new EnrollModerationMeasurementIssuerSource
        {
            OperationId = Guid.Empty,
            Target = authority.Tenant,
            ExpectedQualificationRevision = enrollment.WriterGeneration - 1,
            EnrollmentId = enrollment.Id,
            KeyId = enrollment.KeyId,
            Issuer = enrollment.Issuer,
            PublicKeySpkiBase64 = enrollment.PublicKeySpkiBase64,
            RunnerDigest = enrollment.RunnerDigest,
            CatalogueDigest = enrollment.CatalogueDigest,
            TestBundleDigest = enrollment.TestBundleDigest,
            ValidUntil = enrollment.ValidUntil
        }, ModerationQualificationSourceKind.MeasurementIssuerEnrollment, enrollment.Id, 1,
            enrollment.ApprovedByHumanId, enrollment.WriterGeneration, enrollment.ApprovedAt, authority, token);
        enrollment.Revoke(source.ExpectedEnrollmentRevision, authority.UserId, now);
        return new(ModerationQualificationSourceKind.MeasurementIssuerEnrollment, enrollment.Id, enrollment.Revision);
    }
}
