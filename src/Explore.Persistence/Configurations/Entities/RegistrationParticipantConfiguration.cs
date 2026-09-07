using Explore.Domain;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace Explore.Persistence.Configurations.Entities;

public sealed class RegistrationParticipantConfiguration : IEntityTypeConfiguration<RegistrationParticipant>
{
    public void Configure(EntityTypeBuilder<RegistrationParticipant> builder)
    {
        builder.Property(participant => participant.Id).ValueGeneratedNever();
        builder.Property(participant => participant.CreatedAt).IsRequired();
        builder.Property(participant => participant.IsDeleted).HasDefaultValue(false);
        builder.Property(participant => participant.ConcurrencyStamp).IsConcurrencyToken();
        builder.HasAlternateKey(participant => new { participant.TenantId, participant.Id });
        builder.HasAlternateKey(participant => new { participant.TenantId, participant.RegistrationOrderId, participant.Id });
        builder.HasOne<Tenant>().WithMany().HasForeignKey(participant => participant.TenantId).OnDelete(DeleteBehavior.Restrict);
        builder.HasOne(participant => participant.RegistrationOrder).WithMany(order => order.Participants)
            .HasForeignKey(participant => new { participant.TenantId, participant.RegistrationOrderId })
            .HasPrincipalKey(order => new { order.TenantId, order.Id }).OnDelete(DeleteBehavior.Restrict);
        builder.HasOne(participant => participant.GuardianParticipant).WithMany()
            .HasForeignKey(participant => new { participant.TenantId, participant.RegistrationOrderId, participant.GuardianParticipantId })
            .HasPrincipalKey(guardian => new { guardian.TenantId, guardian.RegistrationOrderId, guardian.Id }).OnDelete(DeleteBehavior.Restrict);
        builder.HasOne(participant => participant.LinkedUser).WithMany()
            .HasForeignKey(participant => participant.LinkedUserId).OnDelete(DeleteBehavior.Restrict);
        builder.HasOne(participant => participant.ParticipantType).WithMany()
            .HasForeignKey(participant => participant.ParticipantTypeId).OnDelete(DeleteBehavior.Restrict);
        builder.HasOne(participant => participant.Pii).WithOne(pii => pii.RegistrationParticipant)
            .HasForeignKey<RegistrationParticipantPii>(pii => new { pii.TenantId, pii.RegistrationParticipantId })
            .HasPrincipalKey<RegistrationParticipant>(participant => new { participant.TenantId, participant.Id })
            .OnDelete(DeleteBehavior.Restrict);
        builder.HasIndex(participant => new { participant.TenantId, participant.RegistrationOrderId });
        builder.HasIndex(participant => new { participant.TenantId, participant.LinkedUserId });
        builder.HasIndex(participant => new { participant.TenantId, participant.GuardianParticipantId });
    }
}
