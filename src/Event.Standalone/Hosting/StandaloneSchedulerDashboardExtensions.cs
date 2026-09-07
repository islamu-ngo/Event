using Explore.API.Configuration;
using Quartz;

namespace Event.Standalone.Hosting;

/// <summary>
/// Standalone-only composition for the Quartz.NET dashboard.
/// <para>
/// The dashboard is a Blazor Server app that reaches the scheduler in-process, so it can only exist where Razor
/// components and <c>IScheduler</c> share a process — the combined host. The split-mode API host deliberately
/// carries no Razor infrastructure and therefore never mounts it; split-mode operators use the first-party
/// scheduler administration section instead, which is served over the normal HAL API.
/// </para>
/// <para>
/// The <em>self-contained</em> mapping is used rather than the documented "existing Blazor app" overload. That
/// overload expects the host's <c>Router</c> to resolve the dashboard's attribute-routed pages through
/// <c>AdditionalAssemblies</c>. This application routes through Blazouter's explicit route table, which resolves
/// only components listed in it and has no attribute-route fallback, so the overload would render the app's
/// not-found page at every dashboard path. The self-contained mapping brings the dashboard's own root component
/// and route table, leaving it independent of the host's router — and it keeps every dashboard component out of
/// the shared client project, which is also compiled into the WebAssembly bundle.
/// </para>
/// </summary>
public static class StandaloneSchedulerDashboardExtensions
{
    public static WebApplicationBuilder AddStandaloneSchedulerDashboard(this WebApplicationBuilder builder)
    {
        ArgumentNullException.ThrowIfNull(builder);

        var settings = ReadSettings(builder.Configuration);
        if (!IsDashboardEnabled(settings, builder.Environment))
        {
            return builder;
        }

        builder.Services.AddQuartzDashboard(options =>
        {
            // Authorization is enforced by the package across the dashboard's pages, its SignalR circuit, and its
            // static assets, so the whole surface is gated rather than only its entry page.
            options.AuthorizationPolicy = ResolvePolicy(settings);
            options.ReadOnly = settings.DashboardReadOnly;
            options.DashboardPath = ResolvePath(settings);
        });

        return builder;
    }

    public static WebApplication MapStandaloneSchedulerDashboard(this WebApplication app)
    {
        ArgumentNullException.ThrowIfNull(app);

        var settings = ReadSettings(app.Configuration);
        if (!IsDashboardEnabled(settings, app.Environment))
        {
            return app;
        }

        // The scheduler is registered by the API host only under its own conditions, which include the selected
        // email-dispatch mode — so `Scheduler:Quartz:Enabled=true` alone does not guarantee a scheduler exists.
        // The dashboard reaches the scheduler in-process, so mounting it without one would produce pages that
        // fail on first render. Checking the container asks reality rather than restating those conditions here,
        // which would drift the moment the host changed them.
        if (app.Services.GetService<ISchedulerFactory>() is null)
        {
            app.Logger.LogWarning(
                "Scheduler dashboard is enabled but no scheduler is registered in this host, so it was not mounted. " +
                "Enable the scheduler (Scheduler:Quartz:Enabled) and a scheduler-backed processing mode first.");
            return app;
        }

        app.MapQuartzDashboard();
        return app;
    }

    /// <summary>
    /// The dashboard follows the scheduler: mounting an operator console over a scheduler that was never started
    /// would show a permanently empty instance, which reads as "nothing is scheduled" rather than "scheduling is
    /// off". The Testing environment is excluded for the same reason the scheduler itself is.
    /// </summary>
    private static bool IsDashboardEnabled(QuartzSchedulerSettings settings, IWebHostEnvironment environment) =>
        settings.DashboardEnabled
        && settings.Enabled
        && !environment.IsEnvironment("Testing");

    private static QuartzSchedulerSettings ReadSettings(IConfiguration configuration) =>
        configuration.GetSection(QuartzSchedulerSettings.SectionName).Get<QuartzSchedulerSettings>()
        ?? new QuartzSchedulerSettings();

    private static string ResolvePolicy(QuartzSchedulerSettings settings) =>
        string.IsNullOrWhiteSpace(settings.DashboardAuthorizationPolicy)
            ? QuartzSchedulerSettings.InstanceAdminPolicyName
            : settings.DashboardAuthorizationPolicy;

    private static string ResolvePath(QuartzSchedulerSettings settings) =>
        string.IsNullOrWhiteSpace(settings.DashboardPath)
            ? QuartzSchedulerSettings.DefaultDashboardPath
            : settings.DashboardPath;
}
