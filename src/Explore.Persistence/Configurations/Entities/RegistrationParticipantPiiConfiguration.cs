using Explore.Domain;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace Explore.Persistence.Configurations.Entities;

public sealed class RegistrationParticipantPiiConfiguration : IEntityTypeConfiguration<RegistrationParticipantPii>
{
    public void Configure(EntityTypeBuilder<RegistrationParticipantPii> builder)
    {
        builder.HasKey(pii => pii.RegistrationParticipantId);
        builder.HasAlternateKey(pii => new { pii.TenantId, pii.RegistrationParticipantId });
        builder.Property(pii => pii.DisplayName).HasMaxLength(200);
        builder.Property(pii => pii.Email).HasMaxLength(320);
        builder.Property(pii => pii.NormalizedEmail).HasMaxLength(320);
        builder.Property(pii => pii.Phone).HasMaxLength(50);
        builder.Property(pii => pii.RetentionUntil).HasColumnType("timestamp with time zone");
        builder.Property(pii => pii.CreatedAt).IsRequired();
        builder.HasOne<Tenant>().WithMany().HasForeignKey(pii => pii.TenantId).OnDelete(DeleteBehavior.Restrict);
        builder.HasIndex(pii => new { pii.TenantId, pii.NormalizedEmail });
    }
}
