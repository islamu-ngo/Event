// ABOUTME: Maps immutable instance-owned ATProto transient records to portable relational storage.
// ABOUTME: Enforces bounded ciphertext, closed purpose values, tenant binding, uniqueness, and expiry indexing.

using Explore.Domain;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
namespace Explore.Persistence.Configurations.Entities;

public sealed class AtprotoTransientRecordConfiguration : IEntityTypeConfiguration<AtprotoTransientRecord>
{
    public void Configure(EntityTypeBuilder<AtprotoTransientRecord> builder)
    {
        builder.ToTable(table => table.HasCheckConstraint(
            "ck_atproto_transients_tenant_purpose",
            "(purpose = 3 AND tenant_id IS NULL) OR (purpose IN (1, 2) AND tenant_id IS NOT NULL)"));
        builder.HasKey(record => record.Id);
        builder.Property(record => record.Id).ValueGeneratedNever();
        builder.Property(record => record.Purpose).HasConversion<int>().IsRequired();
        builder.Property(record => record.TokenDigest).HasMaxLength(64).IsFixedLength().IsRequired();
        builder.Property(record => record.TenantId);
        builder.Property(record => record.ProtectedPayload).IsRequired();
        builder.Property(record => record.ExpiresAtUnixMilliseconds).IsRequired();
        builder.HasIndex(record => new { record.Purpose, record.TokenDigest }).IsUnique().HasDatabaseName("ux_atproto_transients_purpose_digest");
        builder.HasIndex(record => record.ExpiresAtUnixMilliseconds).HasDatabaseName("ix_atproto_transients_expiry");

        foreach (var property in builder.Metadata.GetProperties())
        {
            property.SetAfterSaveBehavior(PropertySaveBehavior.Throw);
        }
    }
}
