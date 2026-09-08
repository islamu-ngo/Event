using Explore.Domain;
using Explore.Domain.Enums;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace Explore.Persistence.Configurations.Entities;

public sealed class NotificationPreferenceCategoryConfiguration : IEntityTypeConfiguration<NotificationPreferenceCategory>
{
    public void Configure(EntityTypeBuilder<NotificationPreferenceCategory> builder)
    {
        builder.Property(e => e.Id).ValueGeneratedNever();
        builder.Property(e => e.MasterCode).IsRequired().HasMaxLength(100);
        builder.Property(e => e.FullName).IsRequired().HasMaxLength(200);
        builder.Property(e => e.Description).HasMaxLength(500);
        builder.Property(e => e.DefaultPushEnabled).HasDefaultValue(false).IsRequired();
        builder.Property(e => e.SortOrder).IsRequired();
        builder.HasIndex(e => e.MasterCode).IsUnique();
    }
}

public sealed class NotificationPreferenceChannelConfiguration : IEntityTypeConfiguration<NotificationPreferenceChannel>
{
    public void Configure(EntityTypeBuilder<NotificationPreferenceChannel> builder)
    {
        builder.Property(e => e.Id).ValueGeneratedNever();
        builder.Property(e => e.MasterCode).IsRequired().HasMaxLength(100);
        builder.Property(e => e.FullName).IsRequired().HasMaxLength(200);
        builder.Property(e => e.Description).HasMaxLength(500);
        builder.Property(e => e.SortOrder).IsRequired();
        builder.HasIndex(e => e.MasterCode).IsUnique();
    }
}

public sealed class NotificationChannelPreferenceConfiguration : IEntityTypeConfiguration<NotificationChannelPreference>
{
    public void Configure(EntityTypeBuilder<NotificationChannelPreference> builder)
    {
        builder.ToTable(table =>
        {
            table.HasCheckConstraint("ck_notification_channel_preferences_scope_target", ScopeTargetCheckSql());
        });

        builder.Property(e => e.Id).HasDefaultValueSql("uuidv7()");
        builder.Property(e => e.CreatedAt).HasDefaultValueSql("NOW()").IsRequired();
        builder.Property(e => e.UpdatedAt).HasDefaultValueSql("NOW()").IsRequired();
        builder.Property(e => e.ConcurrencyStamp).IsConcurrencyToken();

        builder.HasOne(e => e.Tenant)
            .WithMany()
            .HasForeignKey(e => e.TenantId)
            .OnDelete(DeleteBehavior.Restrict);

        builder.HasOne(e => e.Scope)
            .WithMany()
            .HasForeignKey(e => e.ScopeId)
            .OnDelete(DeleteBehavior.Restrict);

        builder.HasOne(e => e.User)
            .WithMany()
            .HasForeignKey(e => e.UserId)
            .OnDelete(DeleteBehavior.Restrict);

        builder.HasOne(e => e.Organization)
            .WithMany()
            .HasForeignKey(e => e.OrganizationId)
            .OnDelete(DeleteBehavior.Restrict);

        builder.HasOne(e => e.Group)
            .WithMany()
            .HasForeignKey(e => e.GroupId)
            .OnDelete(DeleteBehavior.Restrict);

        builder.HasOne(e => e.Category)
            .WithMany()
            .HasForeignKey(e => e.CategoryId)
            .OnDelete(DeleteBehavior.Restrict);

        builder.HasOne(e => e.Channel)
            .WithMany()
            .HasForeignKey(e => e.ChannelId)
            .OnDelete(DeleteBehavior.Restrict);

        builder.HasIndex(e => new { e.TenantId, e.ScopeId, e.CategoryId, e.ChannelId })
            .IsUnique()
            .HasFilter("is_deleted = false AND user_id IS NULL AND organization_id IS NULL AND group_id IS NULL");

        builder.HasIndex(e => new { e.TenantId, e.ScopeId, e.UserId, e.CategoryId, e.ChannelId })
            .IsUnique()
            .HasFilter("is_deleted = false AND user_id IS NOT NULL");

        builder.HasIndex(e => new { e.TenantId, e.ScopeId, e.OrganizationId, e.CategoryId, e.ChannelId })
            .IsUnique()
            .HasFilter("is_deleted = false AND organization_id IS NOT NULL");

        builder.HasIndex(e => new { e.TenantId, e.ScopeId, e.GroupId, e.CategoryId, e.ChannelId })
            .IsUnique()
            .HasFilter("is_deleted = false AND group_id IS NOT NULL");

        builder.HasIndex(e => new { e.TenantId, e.CategoryId, e.ChannelId, e.ScopeId });
    }

    internal static string ScopeTargetCheckSql()
    {
        return $"(scope_id IN ({(int)ConfigurationScopeEnum.System}, {(int)ConfigurationScopeEnum.Instance}, {(int)ConfigurationScopeEnum.Tenant}) "
            + "AND user_id IS NULL AND organization_id IS NULL AND group_id IS NULL) "
            + $"OR (scope_id = {(int)ConfigurationScopeEnum.Organization} AND organization_id IS NOT NULL AND user_id IS NULL AND group_id IS NULL) "
            + $"OR (scope_id = {(int)ConfigurationScopeEnum.Group} AND group_id IS NOT NULL AND user_id IS NULL AND organization_id IS NULL) "
            + $"OR (scope_id = {(int)ConfigurationScopeEnum.User} AND user_id IS NOT NULL AND organization_id IS NULL AND group_id IS NULL)";
    }
}

public sealed class NotificationPreferenceProfileConfiguration : IEntityTypeConfiguration<NotificationPreferenceProfile>
{
    public void Configure(EntityTypeBuilder<NotificationPreferenceProfile> builder)
    {
        builder.ToTable(table =>
        {
            table.HasCheckConstraint("ck_notification_preference_profiles_scope_target", NotificationChannelPreferenceConfiguration.ScopeTargetCheckSql());
        });

        builder.Property(e => e.Id).HasDefaultValueSql("uuidv7()");
        builder.Property(e => e.CreatedAt).HasDefaultValueSql("NOW()").IsRequired();
        builder.Property(e => e.UpdatedAt).HasDefaultValueSql("NOW()").IsRequired();
        builder.Property(e => e.ConcurrencyStamp).IsConcurrencyToken();

        builder.HasOne(e => e.Tenant)
            .WithMany()
            .HasForeignKey(e => e.TenantId)
            .OnDelete(DeleteBehavior.Restrict);

        builder.HasOne(e => e.Scope)
            .WithMany()
            .HasForeignKey(e => e.ScopeId)
            .OnDelete(DeleteBehavior.Restrict);

        builder.HasOne(e => e.User)
            .WithMany()
            .HasForeignKey(e => e.UserId)
            .OnDelete(DeleteBehavior.Restrict);

        builder.HasOne(e => e.Organization)
            .WithMany()
            .HasForeignKey(e => e.OrganizationId)
            .OnDelete(DeleteBehavior.Restrict);

        builder.HasOne(e => e.Group)
            .WithMany()
            .HasForeignKey(e => e.GroupId)
            .OnDelete(DeleteBehavior.Restrict);

        builder.HasIndex(e => new { e.TenantId, e.ScopeId })
            .IsUnique()
            .HasFilter("is_deleted = false AND user_id IS NULL AND organization_id IS NULL AND group_id IS NULL");

        builder.HasIndex(e => new { e.TenantId, e.ScopeId, e.UserId })
            .IsUnique()
            .HasFilter("is_deleted = false AND user_id IS NOT NULL");

        builder.HasIndex(e => new { e.TenantId, e.ScopeId, e.OrganizationId })
            .IsUnique()
            .HasFilter("is_deleted = false AND organization_id IS NOT NULL");

        builder.HasIndex(e => new { e.TenantId, e.ScopeId, e.GroupId })
            .IsUnique()
            .HasFilter("is_deleted = false AND group_id IS NOT NULL");

    }
}
