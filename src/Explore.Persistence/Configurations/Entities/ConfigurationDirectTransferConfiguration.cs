namespace Explore.Persistence.Configurations.Entities;

using Explore.Domain;
using Explore.Persistence.Entities;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

public sealed class ConfigurationDirectTransferSessionConfiguration
    : IEntityTypeConfiguration<ConfigurationDirectTransferSession>
{
    public void Configure(EntityTypeBuilder<ConfigurationDirectTransferSession> builder)
    {
        builder.ToTable("configuration_direct_transfer_sessions");
        builder.HasKey(session => session.Id);
        builder.Property(session => session.SourceAuthority)
            .HasMaxLength(ConfigurationDirectTransferSession.MaximumAuthorityLength)
            .IsRequired();
        builder.Property(session => session.TargetAuthorityKey)
            .HasMaxLength(ConfigurationDirectTransferSession.MaximumAuthorityLength)
            .IsRequired();
        builder.Property(session => session.TargetTenantId);
        Digest(builder.Property(session => session.DestinationOriginDigest));
        Digest(builder.Property(session => session.DestinationProofDigest));
        Digest(builder.Property(session => session.NonceDigest));
        Digest(builder.Property(session => session.ArtifactDigest));
        builder.Property(session => session.ArtifactByteLength).IsRequired();
        builder.Property(session => session.NextOffset).IsRequired();
        builder.Property(session => session.LastChunkOffset).IsRequired();
        builder.Property(session => session.LastChunkByteLength).IsRequired();
        builder.Property(session => session.LastChunkDigest)
            .HasMaxLength(64)
            .IsFixedLength();
        builder.Property(session => session.SourceApprovedBy);
        builder.Property(session => session.DestinationApprovedBy);
        builder.Property(session => session.Status).HasConversion<int>().IsRequired();
        Utc(builder.Property(session => session.CreatedAt), required: true);
        Utc(builder.Property(session => session.ExpiresAt), required: true);
        Utc(builder.Property(session => session.CompletedAt));
        builder.HasIndex(session => session.NonceDigest).IsUnique();
        builder.HasIndex(session => session.DestinationProofDigest).IsUnique();
        builder.HasIndex(session => new
        {
            session.TargetAuthorityKey,
            session.CreatedAt
        });
    }

    private static void Digest(PropertyBuilder<string> property) =>
        property.HasMaxLength(64).IsFixedLength().IsRequired();

    private static void Utc(
        PropertyBuilder<DateTime> property,
        bool required = false)
    {
        property.HasConversion(
            value => value,
            value => DateTime.SpecifyKind(value, DateTimeKind.Utc));
        if (required)
            property.IsRequired();
    }

    private static void Utc(PropertyBuilder<DateTime?> property) =>
        property.HasConversion(
            value => value,
            value => value.HasValue
                ? DateTime.SpecifyKind(value.Value, DateTimeKind.Utc)
                : null);
}

public sealed class ConfigurationDirectTransferChunkConfiguration
    : IEntityTypeConfiguration<ConfigurationDirectTransferChunk>
{
    public void Configure(EntityTypeBuilder<ConfigurationDirectTransferChunk> builder)
    {
        builder.ToTable("configuration_direct_transfer_chunks");
        builder.HasKey(chunk => chunk.Id);
        builder.Property(chunk => chunk.Offset).IsRequired();
        builder.Property(chunk => chunk.ByteLength).IsRequired();
        builder.Property(chunk => chunk.Digest)
            .HasMaxLength(64)
            .IsFixedLength()
            .IsRequired();
        builder.Property(chunk => chunk.ProtectedPayload).IsRequired();
        builder.Property(chunk => chunk.ExpiresAt)
            .HasConversion(
                value => value,
                value => DateTime.SpecifyKind(value, DateTimeKind.Utc))
            .IsRequired();
        builder.HasIndex(chunk => new { chunk.SessionId, chunk.Offset }).IsUnique();
        builder.HasOne<ConfigurationDirectTransferSession>()
            .WithMany()
            .HasForeignKey(chunk => chunk.SessionId)
            .OnDelete(DeleteBehavior.Cascade);
    }
}
