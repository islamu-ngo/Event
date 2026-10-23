using Explore.Domain;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace Explore.Persistence.Identity.Configurations;

public sealed class LocalIdentityEligibilityConfiguration : IEntityTypeConfiguration<LocalIdentityEligibility>
{
    public void Configure(EntityTypeBuilder<LocalIdentityEligibility> builder)
    {
        builder.ToTable("local_identity_eligibilities", table =>
        {
            table.HasCheckConstraint("ck_local_identity_eligibility_state", "state BETWEEN 1 AND 5");
            table.HasCheckConstraint("ck_local_identity_eligibility_generation", "generation > 0");
            table.HasCheckConstraint("ck_local_identity_eligibility_audit", "updated_at IS NULL OR updated_at >= created_at");
        });
        builder.HasKey(row => row.Id);
        builder.Property(row => row.Id).ValueGeneratedNever();
        builder.Property(row => row.State).HasConversion<int>().IsRequired();
        builder.Property(row => row.Generation).IsConcurrencyToken().IsRequired();
        builder.Property(row => row.CreatedAt).HasConversion(
            value => value, value => DateTime.SpecifyKind(value, DateTimeKind.Utc)).IsRequired();
        builder.Property(row => row.UpdatedAt).HasConversion(
            value => value, value => value.HasValue ? DateTime.SpecifyKind(value.Value, DateTimeKind.Utc) : null);
        builder.Property(row => row.LockoutUntilUtc).HasConversion(
            value => value, value => value.HasValue ? DateTime.SpecifyKind(value.Value, DateTimeKind.Utc) : null);
        builder.HasIndex(row => row.LocalSubjectId).IsUnique();
        builder.HasOne<LocalIdentityUser>().WithMany().HasForeignKey(row => row.LocalSubjectId)
            .OnDelete(DeleteBehavior.Restrict);
        builder.HasOne<LocalIdentityCredentialOperation>().WithMany().HasForeignKey(row => row.CurrentOperationId)
            .OnDelete(DeleteBehavior.Restrict);
        builder.Ignore(row => row.IsEligible);
    }
}
