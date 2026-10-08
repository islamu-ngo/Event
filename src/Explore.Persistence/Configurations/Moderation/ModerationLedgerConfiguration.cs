using ISLAMU.Moderation.Domain;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace Explore.Persistence.Configurations.Moderation;

public sealed class ModerationLedgerConfiguration :
    IEntityTypeConfiguration<ModerationCase>,
    IEntityTypeConfiguration<ModerationDecision>,
    IEntityTypeConfiguration<ModerationSanction>,
    IEntityTypeConfiguration<ModerationProtectiveMeasure>
{
    public void Configure(EntityTypeBuilder<ModerationCase> builder)
    {
        ConfigureOwner(builder, "moderation_cases");
        builder.Ignore(entity => entity.Subject);
        builder.Ignore(entity => entity.Scope);
        builder.HasIndex(entity => new { entity.ScopeTrustDomainId, entity.ScopeTenantId, entity.OccurrenceId }).IsUnique();
        builder.ToTable(table => table.HasCheckConstraint("ck_moderation_case_state", "state IN (1, 2)"));
    }

    public void Configure(EntityTypeBuilder<ModerationDecision> builder)
    {
        ConfigureOwner(builder, "moderation_decisions");
        builder.Ignore(entity => entity.Subject);
        builder.Ignore(entity => entity.Issuer);
        builder.Ignore(entity => entity.Scope);
        builder.Ignore(entity => entity.IssuingScope);
        builder.Ignore(entity => entity.Rule);
        builder.Ignore(entity => entity.Capabilities);
        builder.Ignore(entity => entity.Approvals);
        builder.Property(entity => entity.Approval1Kind).HasColumnName("approval1_kind");
        builder.Property(entity => entity.Approval1Id).HasColumnName("approval1_id");
        builder.Property(entity => entity.Approval1OriginKind).HasColumnName("approval1_origin_kind");
        builder.Property(entity => entity.Approval1OriginTrustDomainId).HasColumnName("approval1_origin_trust_domain_id");
        builder.Property(entity => entity.Approval1OriginSolutionId).HasColumnName("approval1_origin_solution_id");
        builder.Property(entity => entity.Approval1OriginInstanceId).HasColumnName("approval1_origin_instance_id");
        builder.Property(entity => entity.Approval1OriginTenantId).HasColumnName("approval1_origin_tenant_id");
        builder.Property(entity => entity.Approval1OriginCommunityId).HasColumnName("approval1_origin_community_id");
        builder.Property(entity => entity.Approval1OriginResourceId).HasColumnName("approval1_origin_resource_id");
        builder.Property(entity => entity.Approval2Kind).HasColumnName("approval2_kind");
        builder.Property(entity => entity.Approval2Id).HasColumnName("approval2_id");
        builder.Property(entity => entity.Approval2OriginKind).HasColumnName("approval2_origin_kind");
        builder.Property(entity => entity.Approval2OriginTrustDomainId).HasColumnName("approval2_origin_trust_domain_id");
        builder.Property(entity => entity.Approval2OriginSolutionId).HasColumnName("approval2_origin_solution_id");
        builder.Property(entity => entity.Approval2OriginInstanceId).HasColumnName("approval2_origin_instance_id");
        builder.Property(entity => entity.Approval2OriginTenantId).HasColumnName("approval2_origin_tenant_id");
        builder.Property(entity => entity.Approval2OriginCommunityId).HasColumnName("approval2_origin_community_id");
        builder.Property(entity => entity.Approval2OriginResourceId).HasColumnName("approval2_origin_resource_id");
        ConfigureSubject(builder, "issuer");
        ConfigureScope(builder, "issuing_scope");
        ConfigureSubject(builder, "approval1", optional: true);
        ConfigureSubject(builder, "approval2", optional: true);
        ConfigureRestriction(builder, "moderation_decision");
        builder.HasOne<ModerationCase>().WithMany().HasForeignKey(entity => entity.CaseId).OnDelete(DeleteBehavior.Restrict);
        builder.ToTable(table =>
        {
            table.HasCheckConstraint("ck_moderation_decision_state", "state BETWEEN 1 AND 8");
            table.HasCheckConstraint("ck_moderation_decision_quorum",
                "required_approvals IN (1, 2) AND (scope_kind NOT IN (1, 2) OR required_approvals = 2) " +
                "AND (ends_at IS NOT NULL OR required_approvals = 2)");
            table.HasCheckConstraint("ck_moderation_decision_approval_order",
                "approval2_id IS NULL OR (approval1_id IS NOT NULL AND approval2_id <> approval1_id)");
            table.HasCheckConstraint("ck_moderation_decision_approval_independence",
                "(approval1_id IS NULL OR (approval1_kind = 1 AND approval1_id <> issuer_id AND approval1_id <> subject_id)) " +
                "AND (approval2_id IS NULL OR (approval2_kind = 1 AND approval2_id <> issuer_id AND approval2_id <> subject_id))");
        });
    }

    public void Configure(EntityTypeBuilder<ModerationSanction> builder)
    {
        ConfigureOwner(builder, "moderation_sanctions");
        builder.Ignore(entity => entity.Subject);
        builder.Ignore(entity => entity.Scope);
        builder.Ignore(entity => entity.Rule);
        builder.Ignore(entity => entity.Capabilities);
        ConfigureRestriction(builder, "moderation_sanction");
        builder.HasOne<ModerationDecision>().WithMany().HasForeignKey(entity => entity.DecisionId).OnDelete(DeleteBehavior.Restrict);
        builder.ToTable(table =>
        {
            table.HasCheckConstraint("ck_moderation_sanction_state", "state BETWEEN 1 AND 5");
            table.HasCheckConstraint("ck_moderation_sanction_supersession",
                "(state = 5 AND superseding_decision_id IS NOT NULL AND superseding_decision_id <> decision_id) " +
                "OR (state <> 5 AND superseding_decision_id IS NULL)");
        });
    }

    public void Configure(EntityTypeBuilder<ModerationProtectiveMeasure> builder)
    {
        ConfigureOwner(builder, "moderation_protective_measures");
        builder.Ignore(entity => entity.Subject);
        builder.Ignore(entity => entity.Protector);
        builder.Ignore(entity => entity.Scope);
        builder.Ignore(entity => entity.IssuingScope);
        builder.Ignore(entity => entity.Capabilities);
        builder.Ignore(entity => entity.Reviewer);
        ConfigureSubject(builder, "protector");
        ConfigureScope(builder, "issuing_scope");
        ConfigureSubject(builder, "reviewer", optional: true);
        builder.Property(entity => entity.Grounds).HasMaxLength(100).IsRequired();
        ConfigureMask(builder, "moderation_protection");
        builder.ToTable(table => table.HasCheckConstraint("ck_moderation_protection_interval",
            "ends_at > starts_at AND review_at > starts_at AND review_at <= ends_at " +
            "AND ((reviewed_at IS NULL AND reviewer_id IS NULL) OR " +
            "(reviewed_at IS NOT NULL AND reviewer_id IS NOT NULL AND reviewed_at >= starts_at AND reviewed_at < ends_at))"));
    }

    private static void ConfigureOwner<TEntity>(EntityTypeBuilder<TEntity> builder, string tableName) where TEntity : class
    {
        builder.ToTable(tableName);
        builder.HasKey("Id");
        builder.Property<Guid>("Id").ValueGeneratedNever();
        builder.Property<long>("Revision").IsConcurrencyToken();
        ConfigureScope(builder, "scope");
        ConfigureSubject(builder, "subject");
        builder.HasIndex("ScopeTrustDomainId", "SubjectKind", "SubjectId", "ScopeTenantId");
        builder.ToTable(table =>
        {
            table.HasCheckConstraint($"ck_{tableName}_revision", "revision > 0");
            table.HasCheckConstraint($"ck_{tableName}_subject_trust",
                "subject_origin_trust_domain_id = scope_trust_domain_id");
        });
    }

    private static void ConfigureRestriction<TEntity>(EntityTypeBuilder<TEntity> builder, string name) where TEntity : class
    {
        builder.Property<string>("RuleCode").HasMaxLength(100).IsRequired();
        ConfigureMask(builder, name);
        builder.ToTable(table =>
        {
            table.HasCheckConstraint($"ck_{name}_rule_revision", "rule_revision > 0");
            table.HasCheckConstraint($"ck_{name}_interval",
                "(ends_at IS NULL OR ends_at > starts_at) " +
                "AND (ends_at IS NOT NULL OR review_at IS NOT NULL) " +
                "AND (review_at IS NULL OR (review_at > starts_at AND (ends_at IS NULL OR review_at <= ends_at)))");
        });
    }

    private static void ConfigureMask<TEntity>(EntityTypeBuilder<TEntity> builder, string name) where TEntity : class =>
        builder.ToTable(table => table.HasCheckConstraint($"ck_{name}_capability_mask",
            "capability_mask > 0 AND (capability_mask & 511) = capability_mask AND (capability_mask & 1) = 0"));

    private static void ConfigureSubject<TEntity>(
        EntityTypeBuilder<TEntity> builder, string columnPrefix, bool optional = false) where TEntity : class
    {
        ConfigureScope(builder, $"{columnPrefix}_origin", optional);
        var kind = $"{columnPrefix}_kind";
        var id = $"{columnPrefix}_id";
        var origin = $"{columnPrefix}_origin_kind";
        var expression = optional
            ? $"({kind} IS NULL AND {id} IS NULL AND {origin} IS NULL) OR " +
                $"({kind} IS NOT NULL AND {kind} BETWEEN 1 AND 4 AND {id} IS NOT NULL AND {origin} IS NOT NULL)"
            : $"{kind} BETWEEN 1 AND 4";
        builder.ToTable(table => table.HasCheckConstraint(
            $"ck_{builder.Metadata.GetTableName()}_{columnPrefix}_subject", expression));
    }

    private static void ConfigureScope<TEntity>(
        EntityTypeBuilder<TEntity> builder, string columnPrefix, bool optional = false) where TEntity : class
    {
        var kind = $"{columnPrefix}_kind";
        var trust = $"{columnPrefix}_trust_domain_id";
        var solution = $"{columnPrefix}_solution_id";
        var instance = $"{columnPrefix}_instance_id";
        var tenant = $"{columnPrefix}_tenant_id";
        var community = $"{columnPrefix}_community_id";
        var resource = $"{columnPrefix}_resource_id";
        var shape =
            $"({kind} = 1 AND {trust} IS NOT NULL AND {solution} IS NULL AND {instance} IS NULL AND {tenant} IS NULL AND {community} IS NULL AND {resource} IS NULL) OR " +
            $"({kind} = 2 AND {trust} IS NOT NULL AND {solution} IS NOT NULL AND {instance} IS NULL AND {tenant} IS NULL AND {community} IS NULL AND {resource} IS NULL) OR " +
            $"({kind} = 3 AND {trust} IS NOT NULL AND {solution} IS NOT NULL AND {instance} IS NOT NULL AND {tenant} IS NULL AND {community} IS NULL AND {resource} IS NULL) OR " +
            $"({kind} = 4 AND {trust} IS NOT NULL AND {solution} IS NOT NULL AND {instance} IS NOT NULL AND {tenant} IS NOT NULL AND {community} IS NULL AND {resource} IS NULL) OR " +
            $"({kind} = 5 AND {trust} IS NOT NULL AND {solution} IS NOT NULL AND {instance} IS NOT NULL AND {tenant} IS NOT NULL AND {community} IS NOT NULL AND {resource} IS NULL) OR " +
            $"({kind} = 6 AND {trust} IS NOT NULL AND {solution} IS NOT NULL AND {instance} IS NOT NULL AND {tenant} IS NOT NULL AND {resource} IS NOT NULL)";
        shape = $"{kind} IS NOT NULL AND ({shape})";
        if (optional)
        {
            shape = $"({kind} IS NULL AND {trust} IS NULL AND {solution} IS NULL AND {instance} IS NULL AND {tenant} IS NULL AND {community} IS NULL AND {resource} IS NULL) OR ({shape})";
        }

        builder.ToTable(table => table.HasCheckConstraint(
            $"ck_{builder.Metadata.GetTableName()}_{columnPrefix}_scope", shape));
    }
}
