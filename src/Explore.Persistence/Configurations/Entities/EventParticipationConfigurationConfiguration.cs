using Explore.Domain;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace Explore.Persistence.Configurations.Entities;

public sealed class EventParticipationConfigurationConfiguration
    : IEntityTypeConfiguration<EventParticipationConfiguration>
{
    public void Configure(EntityTypeBuilder<EventParticipationConfiguration> builder)
    {
        builder.Property(row => row.Id).ValueGeneratedNever();
        builder.Property(row => row.CreatedAt).IsRequired();
        builder.Property(row => row.IsDeleted).HasDefaultValue(false);
        builder.Property(row => row.ConcurrencyStamp).IsConcurrencyToken();
        builder.HasAlternateKey(row => new { row.TenantId, row.Id });

        builder.HasOne(row => row.Tenant)
            .WithMany()
            .HasForeignKey(row => row.TenantId)
            .OnDelete(DeleteBehavior.Restrict);

        builder.HasOne(row => row.Event)
            .WithOne(@event => @event.ParticipationConfiguration)
            .HasForeignKey<EventParticipationConfiguration>(row => new { row.TenantId, row.Id })
            .HasPrincipalKey<Event>(@event => new { @event.TenantId, @event.Id })
            .OnDelete(DeleteBehavior.Cascade)
            .IsRequired();

        builder.HasOne(row => row.ParticipationHandlingMode)
            .WithMany()
            .HasForeignKey(row => row.ParticipationHandlingModeId)
            .OnDelete(DeleteBehavior.Restrict);

        builder.HasOne(row => row.AdvanceRegistrationObligation)
            .WithMany()
            .HasForeignKey(row => row.AdvanceRegistrationObligationId)
            .OnDelete(DeleteBehavior.Restrict);

        builder.HasOne(row => row.IdentityAccessMode)
            .WithMany()
            .HasForeignKey(row => row.IdentityAccessModeId)
            .OnDelete(DeleteBehavior.Restrict);
    }
}
