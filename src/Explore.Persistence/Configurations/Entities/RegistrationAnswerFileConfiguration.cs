using Explore.Domain;
using Explore.Domain.Enums;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace Explore.Persistence.Configurations.Entities;

public sealed class RegistrationAnswerFileConfiguration : IEntityTypeConfiguration<RegistrationAnswerFile>
{
    public void Configure(EntityTypeBuilder<RegistrationAnswerFile> builder)
    {
        builder.ToTable(table =>
        {
            table.HasCheckConstraint("ck_registration_answer_files_size_nonnegative", "size >= 0");
            table.HasCheckConstraint("ck_registration_answer_files_quarantine_state", "quarantine_state IN ('quarantined', 'released')");
            table.HasCheckConstraint("ck_registration_answer_files_scan_status", "scan_status = 'not_scanned'");
            table.HasCheckConstraint("ck_registration_answer_files_field_type",
                $"field_type_id = {(int)RegistrationFieldTypeEnum.File}");
            table.HasCheckConstraint("ck_registration_answer_files_release_shape",
                "(quarantine_state = 'quarantined' AND released_at IS NULL AND released_by IS NULL) OR " +
                "(quarantine_state = 'released' AND released_at IS NOT NULL AND released_by IS NOT NULL)");
        });
        builder.Property(file => file.Id).ValueGeneratedNever();
        builder.Property(file => file.SafeDisplayName).HasMaxLength(500).IsRequired();
        builder.Property(file => file.ContentType).HasMaxLength(255).IsRequired();
        builder.Property(file => file.Extension).HasMaxLength(50).IsRequired();
        builder.Property(file => file.Sha256Checksum).HasMaxLength(64);
        builder.Property(file => file.QuarantineState).HasMaxLength(20).IsRequired();
        builder.Property(file => file.ScanStatus).HasMaxLength(20).IsRequired();
        builder.Property(file => file.QuarantinedAt).IsRequired();
        builder.Property(file => file.CreatedAt).IsRequired();
        builder.Property(file => file.IsDeleted).HasDefaultValue(false);
        builder.Property(file => file.ConcurrencyStamp).IsConcurrencyToken();
        builder.HasAlternateKey(file => new { file.TenantId, file.Id });
        builder.HasOne<Tenant>().WithMany().HasForeignKey(file => file.TenantId).OnDelete(DeleteBehavior.Restrict);
        builder.HasOne<RegistrationSubmission>().WithMany()
            .HasForeignKey(file => new { file.TenantId, file.EventId, Id = file.RegistrationSubmissionId })
            .HasPrincipalKey(submission => new { submission.TenantId, submission.EventId, submission.Id })
            .OnDelete(DeleteBehavior.Restrict);
        builder.HasOne<RegistrationFormField>().WithMany()
            .HasForeignKey(file => new
            {
                file.TenantId,
                file.EventId,
                file.RegistrationFormId,
                file.RegistrationFormVersionId,
                file.RegistrationFormSectionId,
                Id = file.RegistrationFormFieldId,
                file.FieldTypeId
            })
            .HasPrincipalKey(field => new
            {
                field.TenantId,
                field.EventId,
                field.RegistrationFormId,
                field.RegistrationFormVersionId,
                field.RegistrationFormSectionId,
                field.Id,
                field.FieldTypeId
            })
            .OnDelete(DeleteBehavior.Restrict);
        builder.HasOne<StorageObject>().WithMany()
            .HasForeignKey(file => new { file.TenantId, Id = file.StorageObjectId })
            .HasPrincipalKey(storageObject => new { storageObject.TenantId, storageObject.Id })
            .OnDelete(DeleteBehavior.Restrict);
        builder.HasIndex(file => new { file.TenantId, file.RegistrationSubmissionId, file.RegistrationFormFieldId, file.StorageObjectId })
            .IsUnique();
        builder.HasIndex(file => new { file.TenantId, file.StorageObjectId }).IsUnique();
        builder.HasIndex(file => new { file.TenantId, file.StorageObjectId, file.QuarantineState });
    }
}
