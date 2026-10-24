using Explore.Domain;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace Explore.Persistence.EntityConfigurations;

public sealed class ModerationDeploymentSourceConfiguration : IEntityTypeConfiguration<ModerationDeploymentSource>
{
    public void Configure(EntityTypeBuilder<ModerationDeploymentSource> builder)
    {
        builder.ToTable("moderation_deployment_sources", table =>
        {
            table.HasCheckConstraint("ck_moderation_deployment_source_revision",
                "revision = 1 AND policy_revision > 0 AND writer_generation > 0");
            table.HasCheckConstraint("ck_moderation_deployment_source_mode", "mode_code BETWEEN 1 AND 2");
            table.HasCheckConstraint("ck_moderation_deployment_source_validity", "valid_until > recorded_at");
        });
        builder.HasKey(row => row.Id);
        builder.Property(row => row.Id).ValueGeneratedNever();
        builder.Property(row => row.Revision).IsConcurrencyToken();
        builder.Property(row => row.ProposedStateDigest).HasMaxLength(64).IsRequired();
        builder.Property(row => row.InventoryFingerprint).HasMaxLength(64).IsRequired();
        builder.Property(row => row.DatabaseConfigurationDigest).HasMaxLength(64).IsRequired();
        builder.Property(row => row.DatabaseProviderCode).HasMaxLength(32).IsRequired();
        builder.Property(row => row.DatabaseProviderVersion).HasMaxLength(256).IsRequired();
        builder.HasIndex(row => new { row.HostBindingId, row.TenantId, row.PublicationId, row.ProfileId, row.ModeCode, row.WriterGeneration })
            .IsUnique();
        builder.HasOne<ModerationPolicyPublication>().WithMany().HasForeignKey(row => row.DefinitionId).OnDelete(DeleteBehavior.Restrict);
        builder.HasOne<User>().WithMany().HasForeignKey(row => row.RecordedByHumanId).OnDelete(DeleteBehavior.Restrict);
        builder.HasMany(row => row.Consumers).WithOne().HasForeignKey(row => row.SourceId).OnDelete(DeleteBehavior.Restrict);
        foreach (var property in builder.Metadata.GetProperties())
            property.SetAfterSaveBehavior(PropertySaveBehavior.Throw);
    }
}

public sealed class ModerationDeploymentConsumerConfiguration : IEntityTypeConfiguration<ModerationDeploymentConsumer>
{
    public void Configure(EntityTypeBuilder<ModerationDeploymentConsumer> builder)
    {
        builder.ToTable("moderation_deployment_consumers", table =>
            table.HasCheckConstraint("ck_moderation_deployment_consumer_revision",
                "ordinal >= 0 AND ordinal < 256 AND installation_revision = 1 AND installation_writer_generation > 0"));
        builder.HasKey(row => new { row.SourceId, row.Ordinal });
        builder.Property(row => row.ConsumerId).HasMaxLength(128).IsRequired();
        builder.Property(row => row.BindingDigest).HasMaxLength(64).IsRequired();
        builder.Property(row => row.ContractDigest).HasMaxLength(64).IsRequired();
        builder.Property(row => row.RuntimeGeneration).HasMaxLength(64).IsRequired();
        builder.Property(row => row.ProductArtifactDigest).HasMaxLength(64).IsRequired();
        builder.HasIndex(row => new { row.SourceId, row.BindingDigest, row.ConsumerId }).IsUnique();
        builder.HasOne<ModerationConsumerInstallation>().WithMany().HasForeignKey(row => row.InstallationId).OnDelete(DeleteBehavior.Restrict);
        foreach (var property in builder.Metadata.GetProperties())
            property.SetAfterSaveBehavior(PropertySaveBehavior.Throw);
    }
}

public sealed class ModerationDeploymentAdoptionConfiguration : IEntityTypeConfiguration<ModerationDeploymentAdoption>
{
    public void Configure(EntityTypeBuilder<ModerationDeploymentAdoption> builder)
    {
        builder.ToTable("moderation_deployment_adoptions", table =>
        {
            table.HasCheckConstraint("ck_moderation_deployment_adoption_generation",
                "source_revision = 1 AND source_writer_generation > 0 AND writer_generation > source_writer_generation");
            table.HasCheckConstraint("ck_moderation_deployment_adoption_validity", "valid_until > adopted_at");
            table.HasCheckConstraint("ck_moderation_deployment_adoption_withdrawal",
                "(revision = 1 AND withdrawn_at IS NULL AND withdrawn_by_human_id IS NULL) OR " +
                "(revision = 2 AND withdrawn_at IS NOT NULL AND withdrawn_by_human_id IS NOT NULL AND withdrawn_at >= adopted_at)");
        });
        builder.HasKey(row => row.Id);
        builder.Property(row => row.Id).ValueGeneratedNever();
        builder.Property(row => row.Revision).IsConcurrencyToken();
        builder.HasIndex(row => row.SourceId).IsUnique();
        builder.HasOne<ModerationDeploymentSource>().WithMany().HasForeignKey(row => row.SourceId).OnDelete(DeleteBehavior.Restrict);
        builder.HasOne<User>().WithMany().HasForeignKey(row => row.AdoptedByHumanId).OnDelete(DeleteBehavior.Restrict);
        builder.HasOne<User>().WithMany().HasForeignKey(row => row.WithdrawnByHumanId).OnDelete(DeleteBehavior.Restrict);
        foreach (var property in builder.Metadata.GetProperties().Where(property =>
            property.Name is not (nameof(ModerationDeploymentAdoption.Revision)
                or nameof(ModerationDeploymentAdoption.WithdrawnAt) or nameof(ModerationDeploymentAdoption.WithdrawnByHumanId))))
            property.SetAfterSaveBehavior(PropertySaveBehavior.Throw);
    }
}
