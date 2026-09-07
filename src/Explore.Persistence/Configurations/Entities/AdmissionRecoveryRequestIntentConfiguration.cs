using Explore.Domain;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace Explore.Persistence.Configurations.Entities;

public sealed class AdmissionRecoveryRequestIntentConfiguration :
    IEntityTypeConfiguration<AdmissionRecoveryRequestIntent>
{
    public void Configure(EntityTypeBuilder<AdmissionRecoveryRequestIntent> builder)
    {
        builder.ToTable(table =>
        {
            table.HasCheckConstraint(
                "ck_admission_recovery_request_intents_version",
                "protection_version > 0");
        });
        builder.Property(value => value.Id).ValueGeneratedNever();
        builder.Property(value => value.ProtectedIdentity).HasMaxLength(4096).IsRequired();
        builder.Property(value => value.CreatedAt).IsRequired();
        builder.Property(value => value.ConcurrencyStamp).IsConcurrencyToken();
        builder.HasAlternateKey(value => new { value.TenantId, value.Id });
        builder.HasIndex(value => new { value.ProcessedAt, value.CreatedAt });
        builder.HasOne<Tenant>().WithMany()
            .HasForeignKey(value => value.TenantId)
            .OnDelete(DeleteBehavior.Restrict);
    }
}
