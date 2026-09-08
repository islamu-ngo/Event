using Explore.Domain;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace Explore.Persistence.Configurations.Entities;

public class AtprotoRecordConfiguration : IEntityTypeConfiguration<AtprotoRecord>
{
    public void Configure(EntityTypeBuilder<AtprotoRecord> builder)
    {
        builder.ToTable(table =>
        {
            table.HasCheckConstraint("ck_atproto_records_direction", "direction BETWEEN 1 AND 3");
            table.HasCheckConstraint("ck_atproto_records_provenance", "provenance BETWEEN 1 AND 3");
            table.HasCheckConstraint("ck_atproto_records_source_version", "source_version >= 0");
            table.HasCheckConstraint(
                "ck_atproto_records_record_hash",
                "record_hash IS NULL OR record_hash ~ '^[0-9a-f]{64}$'");
        });

        builder.Property(e => e.Id).HasDefaultValueSql("uuidv7()");

        builder.Property(e => e.Did).HasMaxLength(255).IsRequired();
        builder.Property(e => e.Collection).HasMaxLength(255).IsRequired();
        builder.Property(e => e.RecordKey).HasMaxLength(255).IsRequired();
        builder.Property(e => e.Cid).HasMaxLength(255);
        builder.Property(e => e.Uri).HasMaxLength(500);
        builder.Property(e => e.RecordJson).HasColumnType("jsonb");
        builder.Property(e => e.RecordHash).HasMaxLength(64);
        builder.Property(e => e.SubjectUri).HasMaxLength(500);
        builder.Property(e => e.SubjectCid).HasMaxLength(255);
        builder.Property(e => e.Direction).IsRequired();
        builder.Property(e => e.Provenance).IsRequired();
        builder.Property(e => e.UpdatedAt).HasDefaultValueSql("NOW()").IsRequired();

        builder.HasIndex(e => new { e.Did, e.Collection, e.RecordKey })
            .IsUnique();
        builder.HasIndex(e => e.Uri)
            .IsUnique()
            .HasFilter("uri IS NOT NULL");
        builder.HasIndex(e => e.SubjectUri)
            .HasFilter("subject_uri IS NOT NULL");
    }
}
