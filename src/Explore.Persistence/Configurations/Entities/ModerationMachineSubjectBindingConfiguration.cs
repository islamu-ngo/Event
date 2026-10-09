using Explore.Domain;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace Explore.Persistence.Configurations.Entities;

public sealed class ModerationMachineSubjectBindingConfiguration :
    IEntityTypeConfiguration<ModerationMachineSubjectBinding>
{
    public void Configure(EntityTypeBuilder<ModerationMachineSubjectBinding> builder)
    {
        builder.ToTable("moderation_machine_subject_bindings", table =>
        {
            table.HasCheckConstraint("ck_moderation_machine_binding_source", "credential_source IN (1, 2)");
            table.HasCheckConstraint("ck_moderation_machine_binding_owner", "owner_type IN (1, 2, 3, 4, 5)");
            table.HasCheckConstraint("ck_moderation_machine_binding_revision", "revision > 0");
        });
        builder.HasKey(entity => entity.Id);
        builder.Property(entity => entity.Id).ValueGeneratedNever();
        builder.Property(entity => entity.KeyId).HasMaxLength(64).IsRequired();
        builder.Property(entity => entity.CredentialSource).HasConversion<int>();
        builder.Property(entity => entity.OwnerType).HasConversion<int>();
        builder.Property(entity => entity.Revision).IsConcurrencyToken();
        builder.HasIndex(entity => new { entity.HostBindingId, entity.CredentialSource, entity.KeyId }).IsUnique();
        builder.HasIndex(entity => entity.ServicePrincipalId);
        builder.HasOne<ModerationHostBinding>().WithMany()
            .HasForeignKey(entity => entity.HostBindingId).OnDelete(DeleteBehavior.Restrict);
        builder.HasOne<Tenant>().WithMany()
            .HasForeignKey(entity => entity.AuthorityTenantId).OnDelete(DeleteBehavior.Restrict);
        builder.HasOne<ServicePrincipal>().WithMany()
            .HasForeignKey(entity => entity.ServicePrincipalId).OnDelete(DeleteBehavior.Restrict);
    }
}
