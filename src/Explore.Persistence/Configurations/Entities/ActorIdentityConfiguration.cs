using Explore.Domain;
using Explore.Persistence.Schema;
using Explore.Persistence.ValueGenerators;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace Explore.Persistence.Configurations.Entities;

public sealed class AtprotoIdentityConfiguration : IEntityTypeConfiguration<AtprotoIdentity>
{
    public void Configure(EntityTypeBuilder<AtprotoIdentity> builder)
    {
        builder.Property(e => e.Id).HasValueGenerator<GuidVersion7ValueGenerator>();
        builder.Property(e => e.Did).HasMaxLength(2048).IsRequired().UsePortableOrdinalAscii();
        builder.Property(e => e.Handle).HasMaxLength(253);
        builder.Property(e => e.PdsHost).HasMaxLength(2048).IsRequired();
        builder.Property(e => e.SigningKey).HasMaxLength(2048);
        builder.Property(e => e.ModerationReasonCode).HasMaxLength(128);
        builder.Property(e => e.ConcurrencyStamp).IsConcurrencyToken();
        builder.HasIndex(e => e.Did).IsUnique();

        builder.HasOne(e => e.Actor)
            .WithMany(e => e.AtprotoIdentities)
            .HasForeignKey(e => e.ActorId)
            .OnDelete(DeleteBehavior.Restrict);

        builder.HasOne(e => e.DidCustodyType)
            .WithMany()
            .HasForeignKey(e => e.DidCustodyTypeId)
            .OnDelete(DeleteBehavior.Restrict);
    }
}

public sealed class ExternalActorSubjectConfiguration : IEntityTypeConfiguration<ExternalActorSubject>
{
    public void Configure(EntityTypeBuilder<ExternalActorSubject> builder)
    {
        builder.Property(e => e.Id).HasValueGenerator<GuidVersion7ValueGenerator>();
        builder.Property(e => e.ConcurrencyStamp).IsConcurrencyToken();
    }
}

public sealed class ServicePrincipalConfiguration : IEntityTypeConfiguration<ServicePrincipal>
{
    public void Configure(EntityTypeBuilder<ServicePrincipal> builder)
    {
        builder.Property(e => e.Id).HasValueGenerator<GuidVersion7ValueGenerator>();
        builder.Property(e => e.Code).HasMaxLength(128).IsRequired();
        builder.Property(e => e.DisplayName).HasMaxLength(500).IsRequired();
        builder.Property(e => e.ConcurrencyStamp).IsConcurrencyToken();
        builder.HasIndex(e => e.Code).IsUnique();
    }
}

public sealed class ActorMergeConfiguration : IEntityTypeConfiguration<ActorMerge>
{
    public void Configure(EntityTypeBuilder<ActorMerge> builder)
    {
        builder.Property(e => e.Id).HasValueGenerator<GuidVersion7ValueGenerator>();
        builder.Property(e => e.EvidenceReference).HasMaxLength(2048).IsRequired();
        builder.HasIndex(e => e.SourceActorId).IsUnique();

        builder.HasOne(e => e.SourceActor)
            .WithMany(e => e.MergesFrom)
            .HasForeignKey(e => e.SourceActorId)
            .OnDelete(DeleteBehavior.Restrict);

        builder.HasOne(e => e.CanonicalActor)
            .WithMany(e => e.MergesInto)
            .HasForeignKey(e => e.CanonicalActorId)
            .OnDelete(DeleteBehavior.Restrict);

        builder.ToTable(t => t.HasCheckConstraint(
            "ck_actor_merges_distinct_actors",
            "source_actor_id <> canonical_actor_id"));
    }
}

public sealed class ActorModerationRecordConfiguration : IEntityTypeConfiguration<ActorModerationRecord>
{
    public void Configure(EntityTypeBuilder<ActorModerationRecord> builder)
    {
        builder.Property(e => e.Id).HasValueGenerator<GuidVersion7ValueGenerator>();
        builder.Property(e => e.ReasonCode).HasMaxLength(128).IsRequired();
        builder.HasOne(e => e.Actor)
            .WithMany(e => e.ModerationRecords)
            .HasForeignKey(e => e.ActorId)
            .OnDelete(DeleteBehavior.Restrict);
    }
}

public sealed class AtprotoIdentityModerationRecordConfiguration : IEntityTypeConfiguration<AtprotoIdentityModerationRecord>
{
    public void Configure(EntityTypeBuilder<AtprotoIdentityModerationRecord> builder)
    {
        builder.Property(e => e.Id).HasValueGenerator<GuidVersion7ValueGenerator>();
        builder.Property(e => e.ReasonCode).HasMaxLength(128).IsRequired();
        builder.HasOne(e => e.AtprotoIdentity)
            .WithMany(e => e.ModerationRecords)
            .HasForeignKey(e => e.AtprotoIdentityId)
            .OnDelete(DeleteBehavior.Restrict);
    }
}
