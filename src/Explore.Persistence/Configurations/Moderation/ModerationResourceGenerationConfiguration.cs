using Explore.Domain;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace Explore.Persistence.Configurations.Moderation;

public sealed class ModerationResourceGenerationConfiguration : IEntityTypeConfiguration<ModerationResourceGeneration>
{
    public void Configure(EntityTypeBuilder<ModerationResourceGeneration> builder)
    {
        builder.ToTable("moderation_resource_generations", table =>
        {
            table.HasCheckConstraint("ck_moderation_resource_generation_revision", "revision > 0 AND writer_generation > 0");
            table.HasCheckConstraint("ck_moderation_resource_generation_validity", "valid_until > observed_at");
            table.HasCheckConstraint("ck_moderation_resource_generation_material",
                "material_version_id <> '00000000-0000-0000-0000-000000000000'");
            table.HasCheckConstraint("ck_moderation_resource_generation_storage",
                "(delivery_type = 1 AND storage_object_id IS NOT NULL AND storage_object_stamp IS NOT NULL " +
                "AND storage_provider_binding_id IS NOT NULL AND storage_provider IS NOT NULL) OR " +
                "(delivery_type = 2 AND storage_object_id IS NULL AND storage_object_stamp IS NULL " +
                "AND storage_provider_binding_id IS NULL AND storage_provider IS NULL)");
        });
        builder.HasKey(row => row.Id);
        builder.Property(row => row.Id).ValueGeneratedNever();
        builder.HasIndex(row => new { row.HostBindingId, row.TenantId, row.ResourceId, row.Revision }).IsUnique();
        builder.Property(row => row.StorageProvider).HasMaxLength(100);
        builder.HasOne<ModerationHostBinding>().WithMany().HasForeignKey(row => row.HostBindingId).OnDelete(DeleteBehavior.Restrict);
        builder.HasOne<Tenant>().WithMany().HasForeignKey(row => row.TenantId).OnDelete(DeleteBehavior.Restrict);
        builder.HasOne<User>().WithMany().HasForeignKey(row => row.ObservedByHumanId).OnDelete(DeleteBehavior.Restrict);
        // Native objects may later be erased. The retained observation is invalidated by current
        // native resolution, not made an erasure-blocking resource/actor/storage foreign key.
        foreach (var property in builder.Metadata.GetProperties())
            property.SetAfterSaveBehavior(PropertySaveBehavior.Throw);
    }
}
