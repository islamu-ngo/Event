using Explore.Domain;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace Explore.Persistence.Configurations.Moderation;

public sealed class ModerationEventGenerationConfiguration : IEntityTypeConfiguration<ModerationEventGeneration>
{
    public void Configure(EntityTypeBuilder<ModerationEventGeneration> builder)
    {
        builder.ToTable("moderation_event_generations", table =>
        {
            table.HasCheckConstraint("ck_moderation_event_generation_revision", "revision > 0 AND writer_generation > 0");
            table.HasCheckConstraint("ck_moderation_event_generation_validity", "valid_until > observed_at");
            table.HasCheckConstraint("ck_moderation_event_generation_organizer",
                "(organizer_actor_id IS NULL AND organizer_actor_stamp IS NULL) OR " +
                "(organizer_actor_id IS NOT NULL AND organizer_actor_stamp IS NOT NULL)");
        });
        builder.HasKey(row => row.Id);
        builder.Property(row => row.Id).ValueGeneratedNever();
        builder.HasIndex(row => new { row.HostBindingId, row.TenantId, row.EventId, row.Revision }).IsUnique();
        builder.HasOne<ModerationHostBinding>().WithMany().HasForeignKey(row => row.HostBindingId).OnDelete(DeleteBehavior.Restrict);
        builder.HasOne<Tenant>().WithMany().HasForeignKey(row => row.TenantId).OnDelete(DeleteBehavior.Restrict);
        builder.HasOne<User>().WithMany().HasForeignKey(row => row.ObservedByHumanId).OnDelete(DeleteBehavior.Restrict);
        // Do not block erasure of native Events/Actors or their former submitter.
        // Final use must independently resolve their retained coordinates again.
        foreach (var property in builder.Metadata.GetProperties())
            property.SetAfterSaveBehavior(PropertySaveBehavior.Throw);
    }
}
