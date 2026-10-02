using Explore.Domain;
using Explore.Persistence.Schema;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace Explore.Persistence.Privacy.ErasureAuthority.Configurations;

public sealed class PrivacyErasureIdentityFenceConfiguration(bool embedded)
    : IEntityTypeConfiguration<PrivacyErasureIdentityFence>
{
    public void Configure(EntityTypeBuilder<PrivacyErasureIdentityFence> builder)
    {
        builder.ToTable((embedded ? RelationalModelNamespace.Prefix : "") + "identity_fences", table =>
        {
            table.HasCheckConstraint("ck_identity_fences_kind", "identity_kind BETWEEN 1 AND 5");
            table.HasCheckConstraint("ck_identity_fences_key", "length(key_id) BETWEEN 1 AND 64");
            table.HasCheckConstraint("ck_identity_fences_digest", "length(fingerprint) = 64");
        });
        builder.HasKey(fence => new
        {
            fence.AuthoritySequence, fence.IdentityKind, fence.KeyId, fence.Fingerprint
        });
        builder.Property(fence => fence.IdentityKind).HasConversion<int>();
        builder.Property(fence => fence.KeyId).HasMaxLength(64);
        builder.Property(fence => fence.Fingerprint).HasMaxLength(64);
        builder.HasIndex(fence => new { fence.IdentityKind, fence.KeyId, fence.Fingerprint });
        builder.HasOne<PrivacyErasureIntent>().WithMany(intent => intent.IdentityFences)
            .HasForeignKey(fence => fence.AuthoritySequence).OnDelete(DeleteBehavior.Cascade);
        if (embedded)
            builder.Property(fence => fence.RetentionExpiresAtUtc)
                .HasConversion(value => value.Ticks, value => new DateTime(value, DateTimeKind.Utc));
    }
}
