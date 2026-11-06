
using Explore.Application.Contracts.Identity;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace Explore.Persistence.Identity.Configurations;

public sealed class LocalIdentityCredentialOperationConfiguration
    : IEntityTypeConfiguration<LocalIdentityCredentialOperation>
{
    public void Configure(EntityTypeBuilder<LocalIdentityCredentialOperation> builder)
    {
        builder.HasKey(operation => operation.Id);
        builder.Property(operation => operation.Id).ValueGeneratedNever();
        builder.Property(operation => operation.Kind).HasConversion<int>().IsRequired();
        builder.Property(operation => operation.Stage).HasConversion<int>().IsRequired();
        builder.Property(operation => operation.ConcurrencyStamp).IsConcurrencyToken().ValueGeneratedNever();
        builder.Property(operation => operation.ResetReason).HasMaxLength(LocalCredentialResetRequest.MaximumReasonLength);
        builder.Ignore(operation => operation.ApplicationUserId);
        Utc(builder.Property(operation => operation.CreatedAt));
        builder.Property(operation => operation.VerifiedAt).HasConversion(
            value => value,
            value => value.HasValue ? DateTime.SpecifyKind(value.Value, DateTimeKind.Utc) : null);
        builder.Property(operation => operation.UpdatedAt).HasConversion(
            value => value,
            value => value.HasValue ? DateTime.SpecifyKind(value.Value, DateTimeKind.Utc) : null);

        builder.HasOne<LocalIdentityUser>()
            .WithMany()
            .HasForeignKey(operation => operation.LocalSubjectId)
            .OnDelete(DeleteBehavior.Restrict);
        builder.HasIndex(operation => operation.LocalSubjectId);

        builder.ToTable(table =>
        {
            table.HasCheckConstraint("ck_local_credential_operation_kind", "kind BETWEEN 1 AND 3");
            table.HasCheckConstraint("ck_local_credential_operation_stage", "stage BETWEEN 1 AND 5");
            table.HasCheckConstraint(
                "ck_local_credential_operation_reset_metadata",
                "(kind IN (1, 3) AND previous_operation_id IS NULL AND previous_operation_concurrency_stamp IS NULL AND reset_reason IS NULL "
                + "AND (kind <> 3 OR stage <> 2)) "
                + "OR (kind = 2 AND previous_operation_id IS NOT NULL AND previous_operation_id <> id "
                + "AND previous_operation_concurrency_stamp IS NOT NULL AND reset_reason IS NOT NULL "
                + "AND TRIM(reset_reason) <> '' AND stage <> 1)");
            table.HasCheckConstraint(
                "ck_local_credential_operation_timestamps",
                "((kind = 1 AND verified_at IS NOT NULL AND verified_at >= created_at) "
                + "OR (kind = 2 AND (verified_at IS NULL OR verified_at <= created_at)) "
                + "OR (kind = 3 AND verified_at IS NULL)) "
                + "AND (updated_at IS NULL OR updated_at >= created_at)");
            table.HasCheckConstraint(
                "ck_local_credential_operation_origin",
                "((kind IN (1, 2) AND initiating_application_user_id IS NOT NULL) "
                + "OR (kind = 3 AND initiating_application_user_id IS NULL)) AND "
                + "((kind IN (1, 2) AND visitor_enrollment_operation_id IS NULL "
                + "AND verified_by_application_user_id IS NOT NULL AND verified_at IS NOT NULL) "
                + "OR (kind IN (2, 3) AND visitor_enrollment_operation_id IS NOT NULL "
                + "AND verified_by_application_user_id IS NULL AND verified_at IS NULL))");
        });
    }

    private static void Utc(PropertyBuilder<DateTime> property) =>
        property.HasConversion(value => value, value => DateTime.SpecifyKind(value, DateTimeKind.Utc))
            .IsRequired();
}
