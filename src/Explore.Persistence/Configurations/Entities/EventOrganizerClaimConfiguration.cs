using Explore.Domain;
using Explore.Persistence.ValueGenerators;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace Explore.Persistence.Configurations.Entities;

public sealed class EventOrganizerClaimConfiguration : IEntityTypeConfiguration<EventOrganizerClaim>
{
    public void Configure(EntityTypeBuilder<EventOrganizerClaim> builder)
    {
        builder.Property(row => row.Id).HasValueGenerator<GuidVersion7ValueGenerator>();
        builder.HasAlternateKey(row => new { row.TenantId, row.Id });
        builder.Property(row => row.EvidenceType).IsRequired().HasMaxLength(100);
        builder.Property(row => row.EvidenceReference).IsRequired().HasMaxLength(2048);
        builder.Property(row => row.DecisionReasonCode).HasMaxLength(100);
        builder.Property(row => row.ConcurrencyStamp).IsConcurrencyToken();
        builder.Property(row => row.IsDeleted).HasDefaultValue(false);

        builder.HasOne(row => row.Tenant)
            .WithMany()
            .HasForeignKey(row => row.TenantId)
            .OnDelete(DeleteBehavior.Restrict);

        builder.HasOne(row => row.Event)
            .WithMany(@event => @event.OrganizerClaims)
            .HasForeignKey(row => new { row.TenantId, row.EventId })
            .HasPrincipalKey(@event => new { @event.TenantId, @event.Id })
            .OnDelete(DeleteBehavior.Restrict);

        builder.HasOne(row => row.ClaimantActor)
            .WithMany()
            .HasForeignKey(row => row.ClaimantActorId)
            .OnDelete(DeleteBehavior.Restrict);

        builder.HasOne(row => row.Status)
            .WithMany()
            .HasForeignKey(row => row.StatusId)
            .OnDelete(DeleteBehavior.Restrict);

        builder.HasOne(row => row.ReviewerUser)
            .WithMany()
            .HasForeignKey(row => row.ReviewerUserId)
            .OnDelete(DeleteBehavior.Restrict);
    }
}
