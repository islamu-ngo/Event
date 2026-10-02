namespace Explore.Persistence.Configurations.Entities;

using Explore.Domain;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

public class ActorPiiConfiguration : IEntityTypeConfiguration<ActorPii>
{
    public void Configure(EntityTypeBuilder<ActorPii> builder)
    {
        builder.HasKey(e => e.ActorId);

        builder.Property(e => e.DisplayName)
            .HasMaxLength(500)
            .IsRequired();

        builder.Property(e => e.ExternalProfilePictureUri)
            .HasMaxLength(500);

        builder.HasOne(e => e.ProfilePicture)
            .WithMany()
            .HasForeignKey(e => e.ProfilePictureStorageObjectId)
            .OnDelete(DeleteBehavior.Restrict);
        builder.Navigation(e => e.ProfilePicture).AutoInclude();

        builder.ToTable(t => t.HasCheckConstraint(
            "ck_actor_pii_profile_picture_shape",
            "(profile_picture_storage_object_id IS NULL OR "
            + "(profile_picture_storage_object_id <> '00000000-0000-0000-0000-000000000000' AND external_profile_picture_uri IS NULL))"
            + " AND (external_profile_picture_uri IS NULL OR "
            + "(LOWER(external_profile_picture_uri) LIKE 'https://_%' OR LOWER(external_profile_picture_uri) LIKE 'http://_%'))"));
    }
}
