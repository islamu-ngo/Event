using Explore.Domain;
using Explore.Domain.Enums;
using Explore.Persistence.Seed;
using Explore.Persistence.ValueGenerators;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace Explore.Persistence.Configurations.Entities;

public class ActorConfiguration : IEntityTypeConfiguration<Actor>
{
    public void Configure(EntityTypeBuilder<Actor> builder)
    {
        builder.Property(e => e.Id).HasValueGenerator<GuidVersion7ValueGenerator>();

        builder.Property(e => e.Description).HasMaxLength(500);
        builder.Property(e => e.ProfilePictureCid).HasMaxLength(500);
        builder.Property(e => e.BackgroundColor).HasMaxLength(50);
        builder.Property(e => e.BackgroundEffect).HasMaxLength(50);
        builder.Property(e => e.BannerColor).HasMaxLength(50);
        builder.Property(e => e.ConcurrencyStamp).IsConcurrencyToken();

        builder.HasOne(e => e.ActorType)
            .WithMany()
            .HasForeignKey(e => e.ActorTypeId)
            .OnDelete(DeleteBehavior.Restrict);

        builder.HasOne(e => e.Pii)
            .WithOne(e => e.Actor)
            .HasForeignKey<ActorPii>(e => e.ActorId)
            .OnDelete(DeleteBehavior.Cascade);

        builder.Navigation(e => e.Pii).AutoInclude();

        // User relationship - An Actor can be owned by a User (personal actor)
        builder.HasOne(e => e.User)
            .WithOne(e => e.Actor)
            .HasForeignKey<Actor>(e => e.UserId)
            .OnDelete(DeleteBehavior.Restrict);

        // Organization relationship - An Actor can be owned by an Organization
        builder.HasOne(e => e.Organization)
            .WithOne(e => e.Actor)
            .HasForeignKey<Actor>(e => e.OrganizationId)
            .OnDelete(DeleteBehavior.Restrict);

        // Group relationship - An Actor can be owned by a Group
        builder.HasOne(e => e.Group)
            .WithOne(e => e.Actor)
            .HasForeignKey<Actor>(e => e.GroupId)
            .OnDelete(DeleteBehavior.Restrict);

        builder.HasOne(e => e.ExternalActorSubject)
            .WithOne(e => e.Actor)
            .HasForeignKey<Actor>(e => e.ExternalActorSubjectId)
            .OnDelete(DeleteBehavior.Restrict);

        builder.HasOne(e => e.ServicePrincipal)
            .WithOne(e => e.Actor)
            .HasForeignKey<Actor>(e => e.ServicePrincipalId)
            .OnDelete(DeleteBehavior.Restrict);

        builder.HasIndex(e => e.UserId)
            .IsUnique()
            .HasFilter("user_id IS NOT NULL");

        builder.HasIndex(e => e.OrganizationId)
            .IsUnique()
            .HasFilter("organization_id IS NOT NULL");

        builder.HasIndex(e => e.GroupId)
            .IsUnique()
            .HasFilter("group_id IS NOT NULL");

        builder.HasIndex(e => e.ExternalActorSubjectId)
            .IsUnique()
            .HasFilter("external_actor_subject_id IS NOT NULL");

        builder.HasIndex(e => e.ServicePrincipalId)
            .IsUnique()
            .HasFilter("service_principal_id IS NOT NULL");

        builder.ToTable(t =>
        {
            t.HasCheckConstraint(
                "ck_actors_exactly_one_owner",
                "num_nonnulls(user_id, organization_id, group_id, external_actor_subject_id, service_principal_id) = 1"
                + " OR (is_deleted AND num_nonnulls(user_id, organization_id, group_id, external_actor_subject_id, service_principal_id) = 0)");
            t.HasCheckConstraint(
                "ck_actors_external_type_matches_owner",
                "(external_actor_subject_id IS NULL AND actor_type_id <> 6) OR (external_actor_subject_id IS NOT NULL AND actor_type_id = 6)");
        });

        // NOTE: Business entity seed data moved to DatabaseSeeder for conditional (Development-only) seeding.
        // See Explore.Persistence/Seed/DatabaseSeeder.cs and SeedData.cs
    }
}
