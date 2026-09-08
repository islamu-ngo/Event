
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
        Utc(builder.Property(operation => operation.VerifiedAt));
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
            table.HasCheckConstraint("ck_local_credential_operation_kind", "kind BETWEEN 1 AND 2");
            table.HasCheckConstraint("ck_local_credential_operation_stage", "stage BETWEEN 1 AND 5");
            table.HasCheckConstraint(
                "ck_local_credential_operation_reset_metadata",
                "(kind = 1 AND previous_operation_id IS NULL AND previous_operation_concurrency_stamp IS NULL AND reset_reason IS NULL) "
                + "OR (kind = 2 AND previous_operation_id IS NOT NULL AND previous_operation_id <> id "
                + "AND previous_operation_concurrency_stamp IS NOT NULL AND reset_reason IS NOT NULL "
                + "AND TRIM(reset_reason) <> '' AND stage <> 1)");
            table.HasCheckConstraint(
                "ck_local_credential_operation_timestamps",
                "((kind = 1 AND verified_at >= created_at) OR (kind = 2 AND verified_at <= created_at)) "
                + "AND (updated_at IS NULL OR updated_at >= created_at)");
        });
    }

    private static void Utc(PropertyBuilder<DateTime> property) =>
        property.HasConversion(value => value, value => DateTime.SpecifyKind(value, DateTimeKind.Utc))
            .IsRequired();
}
