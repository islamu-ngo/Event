using Explore.Domain;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace Explore.Persistence.Configurations.Entities;

public class EventSessionSpeakerConfiguration : IEntityTypeConfiguration<EventSessionSpeaker>
{
    public void Configure(EntityTypeBuilder<EventSessionSpeaker> builder)
    {
        builder.Property(e => e.ConcurrencyStamp).IsConcurrencyToken();

        builder.HasOne(e => e.Actor)
            .WithMany()
            .HasForeignKey(e => e.ActorId)
            .OnDelete(DeleteBehavior.Cascade);

        builder.HasOne(e => e.EventSession)
            .WithMany()
            .HasForeignKey(e => new { e.TenantId, e.EventSessionId })
            .HasPrincipalKey(e => new { e.TenantId, e.Id })
            .OnDelete(DeleteBehavior.Cascade);

        builder.HasOne(e => e.Tenant)
            .WithMany()
            .HasForeignKey(e => e.TenantId)
            .OnDelete(DeleteBehavior.Restrict);

        builder.HasIndex(e => new { e.TenantId, e.EventSessionId, e.ActorId })
            .IsUnique();
    }
}
