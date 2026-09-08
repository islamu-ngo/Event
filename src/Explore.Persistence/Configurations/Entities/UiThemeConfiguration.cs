namespace Explore.Persistence.Configurations.Entities;

using Explore.Domain;
using Explore.Domain.ValueObjects;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

public class UiThemeConfiguration : IEntityTypeConfiguration<UiTheme>
{
    public void Configure(EntityTypeBuilder<UiTheme> builder)
    {
        builder.HasKey(e => e.Id);

        builder.Property(e => e.Id)
            .HasDefaultValueSql("uuidv7()");

        builder.Property(e => e.TenantId);

        builder.Property(e => e.ThemeKey)
            .IsRequired()
            .HasMaxLength(128);

        builder.Property(e => e.DisplayName)
            .IsRequired()
            .HasMaxLength(200);

        builder.Property(e => e.Description)
            .HasMaxLength(1000);

        builder.Property(e => e.IsActive)
            .IsRequired()
            .HasDefaultValue(true);

        builder.Property(e => e.IsDefault)
            .IsRequired()
            .HasDefaultValue(false);

        builder.Property(e => e.SortOrder)
            .IsRequired()
            .HasDefaultValue(0);

        builder.Property(e => e.CreatedAt)
            .IsRequired()
            .HasDefaultValueSql("NOW()");

        builder.Property(e => e.RowVersion)
            .HasColumnName("xmin")
            .IsRowVersion();

        builder.HasIndex(e => e.ThemeKey)
            .HasFilter("tenant_id IS NULL")
            .IsUnique();

        builder.HasIndex(e => new { e.TenantId, e.ThemeKey })
            .HasFilter("tenant_id IS NOT NULL")
            .IsUnique();

        builder.HasIndex(e => e.IsDefault)
            .HasFilter("tenant_id IS NULL AND is_default = true")
            .IsUnique();

        builder.HasIndex(e => new { e.TenantId, e.IsDefault })
            .HasFilter("tenant_id IS NOT NULL AND is_default = true")
            .IsUnique();

        builder.HasOne(e => e.Tenant)
            .WithMany()
            .HasForeignKey(e => e.TenantId)
            .OnDelete(DeleteBehavior.Cascade);

        ConfigurePalette(builder.OwnsOne(e => e.LightPalette), "light");
        ConfigurePalette(builder.OwnsOne(e => e.DarkPalette), "dark");
    }

    private static void ConfigurePalette(OwnedNavigationBuilder<UiTheme, UiThemePalette> builder, string prefix)
    {
        builder.Property(p => p.Primary).IsRequired().HasMaxLength(7).HasColumnName($"{prefix}_primary");
        builder.Property(p => p.PrimaryContrastText).IsRequired().HasMaxLength(7).HasColumnName($"{prefix}_primary_contrast_text");
        builder.Property(p => p.Secondary).IsRequired().HasMaxLength(7).HasColumnName($"{prefix}_secondary");
        builder.Property(p => p.SecondaryContrastText).IsRequired().HasMaxLength(7).HasColumnName($"{prefix}_secondary_contrast_text");
        builder.Property(p => p.Background).IsRequired().HasMaxLength(7).HasColumnName($"{prefix}_background");
        builder.Property(p => p.Surface).IsRequired().HasMaxLength(7).HasColumnName($"{prefix}_surface");
        builder.Property(p => p.AppbarBackground).IsRequired().HasMaxLength(32).HasColumnName($"{prefix}_appbar_background");
        builder.Property(p => p.AppbarText).IsRequired().HasMaxLength(7).HasColumnName($"{prefix}_appbar_text");
        builder.Property(p => p.DrawerBackground).IsRequired().HasMaxLength(32).HasColumnName($"{prefix}_drawer_background");
        builder.Property(p => p.DrawerText).IsRequired().HasMaxLength(7).HasColumnName($"{prefix}_drawer_text");
        builder.Property(p => p.DrawerIcon).IsRequired().HasMaxLength(7).HasColumnName($"{prefix}_drawer_icon");
        builder.Property(p => p.TextPrimary).IsRequired().HasMaxLength(7).HasColumnName($"{prefix}_text_primary");
        builder.Property(p => p.TextSecondary).IsRequired().HasMaxLength(7).HasColumnName($"{prefix}_text_secondary");
        builder.Property(p => p.Info).IsRequired().HasMaxLength(7).HasColumnName($"{prefix}_info");
        builder.Property(p => p.Success).IsRequired().HasMaxLength(7).HasColumnName($"{prefix}_success");
        builder.Property(p => p.Warning).IsRequired().HasMaxLength(7).HasColumnName($"{prefix}_warning");
        builder.Property(p => p.Error).IsRequired().HasMaxLength(7).HasColumnName($"{prefix}_error");
        builder.Property(p => p.LinesDefault).IsRequired().HasMaxLength(7).HasColumnName($"{prefix}_lines_default");
        builder.Property(p => p.Divider).IsRequired().HasMaxLength(32).HasColumnName($"{prefix}_divider");
    }
}
