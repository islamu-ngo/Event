using Explore.Domain;
using Explore.Domain.Secrets;
using Explore.Persistence.ValueGenerators;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace Explore.Persistence.Configurations.Entities;

public sealed class ManagedControlPlaneRegistrationConfiguration
    : IEntityTypeConfiguration<ManagedControlPlaneRegistration>
{
    public void Configure(EntityTypeBuilder<ManagedControlPlaneRegistration> builder)
    {
        builder.HasKey(entity => entity.Id);
        builder.Property(entity => entity.Id).HasValueGenerator<GuidVersion7ValueGenerator>();
        builder.Property(entity => entity.ControlPlaneEndpoint).HasMaxLength(500).IsRequired();
        builder.Property(entity => entity.ManagementApiVersion).HasMaxLength(50).IsRequired();
        builder.Property(entity => entity.EventVersion).HasMaxLength(100).IsRequired();
        builder.Property(entity => entity.DeploymentMode).HasConversion<string>().HasMaxLength(20).IsRequired();
        builder.Property(entity => entity.RequestHash).HasMaxLength(64).IsFixedLength().IsRequired();
        builder.Property(entity => entity.EventToControlPlaneKeyId).HasMaxLength(64).IsRequired();
        builder.Property(entity => entity.EventToControlPlaneSecretHash).HasMaxLength(500).IsRequired();
        builder.Property(entity => entity.ControlPlaneToEventKeyId).HasMaxLength(64).IsRequired();
        builder.Property(entity => entity.ControlPlaneToEventSecretHash).HasMaxLength(500).IsRequired();
        builder.Property(entity => entity.Status).HasConversion<string>().HasMaxLength(20).IsRequired();
        builder.Property(entity => entity.LastFailureCode).HasMaxLength(100);
        builder.Property(entity => entity.RowVersion).HasColumnName("xmin").IsRowVersion();
        builder.HasOne<SecretBinding>()
            .WithMany()
            .HasForeignKey(entity => entity.CredentialSecretBindingId)
            .OnDelete(DeleteBehavior.Restrict);
        builder.HasIndex(entity => entity.ManagedInstanceId).IsUnique();
        builder.HasIndex(entity => entity.EventInstanceId).IsUnique();
        builder.HasIndex(entity => entity.EventToControlPlaneKeyId).IsUnique();
        builder.HasIndex(entity => entity.ControlPlaneToEventKeyId).IsUnique();
        builder.ToTable(table =>
        {
            table.HasCheckConstraint(
                "ck_managed_control_plane_registration_expiry",
                "event_to_control_plane_credential_expires_at > created_at "
                + "AND control_plane_to_event_credential_expires_at > created_at");
            table.HasCheckConstraint(
                "ck_managed_control_plane_registration_registered",
                "(status IN ('Registered', 'Revoked')) = (registered_at IS NOT NULL)");
            table.HasCheckConstraint(
                "ck_managed_control_plane_registration_revoked",
                "(status = 'Revoked') = (revoked_at IS NOT NULL)");
        });
    }
}
