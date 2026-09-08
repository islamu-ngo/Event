using Explore.Domain;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace Explore.Persistence.Configurations.Entities;

public sealed class EventContactShareConsentHistoryConfiguration : IEntityTypeConfiguration<EventContactShareConsentHistory>
{
    public void Configure(EntityTypeBuilder<EventContactShareConsentHistory> builder)
    {
        builder.Property(history => history.Id).ValueGeneratedNever();
        builder.Property(history => history.PurposeCodeSnapshot).IsRequired().HasMaxLength(100);
        builder.Property(history => history.EmailSnapshot).IsRequired().HasMaxLength(320);
        builder.Property(history => history.EmailNormalizedSnapshot).IsRequired().HasMaxLength(320);
        builder.Property(history => history.ConsentTextSnapshot).IsRequired().HasMaxLength(4000);
        builder.Property(history => history.ConsentUiVersionSnapshot).IsRequired().HasMaxLength(100);
        builder.HasOne<Tenant>().WithMany().HasForeignKey(history => history.TenantId).OnDelete(DeleteBehavior.Restrict);
        builder.HasOne(history => history.Consent).WithMany()
            .HasForeignKey(history => new { history.TenantId, history.ConsentId })
            .HasPrincipalKey(consent => new { consent.TenantId, consent.Id })
            .OnDelete(DeleteBehavior.Restrict);
        builder.HasOne(history => history.SourceEvent).WithMany()
            .HasForeignKey(history => new { history.TenantId, history.SourceEventId })
            .HasPrincipalKey(@event => new { @event.TenantId, @event.Id }).OnDelete(DeleteBehavior.Restrict);
        builder.HasOne(history => history.SourceRegistrationOrder).WithMany()
            .HasForeignKey(history => new { history.TenantId, history.SourceRegistrationOrderId })
            .HasPrincipalKey(order => new { order.TenantId, order.Id }).OnDelete(DeleteBehavior.Restrict);
        builder.HasOne(history => history.Actor).WithMany().HasForeignKey(history => history.ActorId).OnDelete(DeleteBehavior.Restrict);
        builder.HasOne(history => history.User).WithMany().HasForeignKey(history => history.UserId).OnDelete(DeleteBehavior.Restrict);
        builder.HasIndex(history => new { history.TenantId, history.ConsentId, history.OccurredAt });
    }
}
