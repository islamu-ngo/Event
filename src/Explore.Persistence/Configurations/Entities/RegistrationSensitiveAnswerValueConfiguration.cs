using Explore.Domain;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace Explore.Persistence.Configurations.Entities;

public sealed class RegistrationSensitiveAnswerValueConfiguration : IEntityTypeConfiguration<RegistrationSensitiveAnswerValue>
{
    public void Configure(EntityTypeBuilder<RegistrationSensitiveAnswerValue> builder)
    {
        builder.ToTable(table => table.HasCheckConstraint(
            "ck_registration_sensitive_answer_values_shape",
            "key_version > 0 AND length(btrim(ciphertext)) > 0"));
        builder.Property(value => value.Id).ValueGeneratedNever();
        builder.Property(value => value.Ciphertext).IsRequired().HasMaxLength(131072);
        builder.Property(value => value.RetentionUntil).HasColumnType("timestamp with time zone");
        builder.Property(value => value.CreatedAt).IsRequired();
        builder.Property(value => value.IsDeleted).HasDefaultValue(false);
        builder.HasAlternateKey(value => new { value.TenantId, value.Id });
        builder.HasOne<Tenant>().WithMany().HasForeignKey(value => value.TenantId).OnDelete(DeleteBehavior.Restrict);
        builder.HasIndex(value => new { value.TenantId, value.KeyVersion });
    }
}
