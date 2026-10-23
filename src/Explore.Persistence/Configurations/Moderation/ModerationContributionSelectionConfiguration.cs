using Explore.Domain;
using ISLAMU.Moderation.Domain;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace Explore.Persistence.Configurations.Moderation;

public sealed class ModerationContributionSelectionConfiguration :
    IEntityTypeConfiguration<ModerationContributionSelection>, IEntityTypeConfiguration<ModerationContributionBinding>
{
    public void Configure(EntityTypeBuilder<ModerationContributionSelection> builder)
    {
        builder.ToTable("moderation_contribution_selections", table =>
        {
            table.HasCheckConstraint("ck_moderation_contribution_selection_generations",
                "revision = 1 AND decision_revision > 1 AND case_revision > 0 AND policy_revision > 0 AND writer_generation > 0");
            table.HasCheckConstraint("ck_moderation_contribution_selection_validity", "valid_until > recorded_at");
        });
        builder.HasKey(row => row.Id);
        builder.Property(row => row.Id).ValueGeneratedNever();
        builder.Property(row => row.SelectionDigest).HasMaxLength(64).IsRequired();
        builder.HasIndex(row => new { row.HostBindingId, row.TrustDomainId, row.TenantId, row.EffectId }).IsUnique();
        builder.HasOne<ModerationHostBinding>().WithMany().HasForeignKey(row => row.HostBindingId).OnDelete(DeleteBehavior.Restrict);
        builder.HasOne<Tenant>().WithMany().HasForeignKey(row => row.TenantId).OnDelete(DeleteBehavior.Restrict);
        builder.HasOne<ModerationCase>().WithMany().HasForeignKey(row => row.CaseId).OnDelete(DeleteBehavior.Restrict);
        builder.HasOne<ModerationDecision>().WithMany().HasForeignKey(row => row.DecisionId).OnDelete(DeleteBehavior.Restrict);
        builder.HasOne<ModerationPolicyPublication>().WithMany().HasForeignKey(row => row.PublicationId).OnDelete(DeleteBehavior.Restrict);
        builder.HasOne<User>().WithMany().HasForeignKey(row => row.AuthoredByHumanId).OnDelete(DeleteBehavior.Restrict);
        builder.HasMany(row => row.Bindings).WithOne().HasForeignKey(row => row.SelectionId).OnDelete(DeleteBehavior.Restrict);
        foreach (var property in builder.Metadata.GetProperties())
            property.SetAfterSaveBehavior(PropertySaveBehavior.Throw);
    }

    public void Configure(EntityTypeBuilder<ModerationContributionBinding> builder)
    {
        builder.ToTable("moderation_contribution_bindings", table =>
        {
            table.HasCheckConstraint("ck_moderation_contribution_binding_partition",
                "ordinal >= 0 AND ordinal < 64 AND target_revision > 0");
            table.HasCheckConstraint("ck_moderation_contribution_binding_meaning",
                "(kind = 1 AND effect_kind IS NOT NULL AND effect_mode IS NOT NULL AND effect_target IS NOT NULL " +
                "AND execution IS NOT NULL AND strategy IS NULL) OR " +
                "(kind = 2 AND effect_kind IS NULL AND effect_mode IS NULL AND effect_target IS NULL " +
                "AND capability IS NULL AND execution IS NULL)");
        });
        builder.HasKey(row => new { row.SelectionId, row.Ordinal });
        builder.Property(row => row.Operation).HasMaxLength(100).IsRequired();
        builder.Ignore(row => row.Scope);
        ModerationLedgerConfiguration.ConfigureScope(builder, "scope");
        builder.Ignore(row => row.Effect);
        foreach (var property in builder.Metadata.GetProperties())
            property.SetAfterSaveBehavior(PropertySaveBehavior.Throw);
    }
}
