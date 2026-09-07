namespace Explore.API.Configuration;

public sealed class QuartzSchedulerSettings
{
    public const string SectionName = "Scheduler:Quartz";
    public const string InstanceAdminPolicyName = "quartz_instance_admin";
    public const string DefaultTablePrefix = "QRTZ_";
    public const string DefaultStatusEndpointPath = "/admin/scheduler";
    public const string DefaultDashboardPath = "/quartz";
    public const string AutoInstanceId = "AUTO";

    public bool Enabled { get; set; } = true;

    /// <summary>Scheduler name recorded in the persistent store; must be stable across restarts of one logical instance.</summary>
    public string SchedulerName { get; set; } = "islamu-event-scheduler";

    /// <summary><c>AUTO</c> lets Quartz generate a unique per-process id, which is required for clustering.</summary>
    public string InstanceId { get; set; } = AutoInstanceId;

    public int MaxConcurrency { get; set; } = Math.Max(1, Environment.ProcessorCount);

    /// <summary>When false the scheduler runs on the in-memory store and loses state on restart.</summary>
    public bool UsePersistentStore { get; set; } = true;

    /// <summary>Prefix for the co-located scheduler tables in the primary application database.</summary>
    public string TablePrefix { get; set; } = DefaultTablePrefix;

    /// <summary>Enables the database-backed clustering protocol for multi-instance deployments.</summary>
    public bool ClusteringEnabled { get; set; }

    public int ClusterCheckinIntervalSeconds { get; set; } = 20;

    /// <summary>Applies the idempotent scheduler DDL at startup; disable when a migration job owns schema.</summary>
    public bool ApplySchemaOnStartup { get; set; } = true;

    /// <summary>
    /// Asks Quartz to verify the persistent store's tables and columns during scheduler initialization.
    /// It is on by default because the alternative failure mode is silent: Quartz downgrades a missing
    /// optional column to a warning and keeps running with degraded behaviour, which this platform has
    /// already shipped once. Validation turns that class of drift into a startup failure that names the
    /// offending table. It is meaningful only over a persistent store.
    /// </summary>
    public bool ValidateSchemaOnStartup { get; set; } = true;

    public bool StatusEndpointEnabled { get; set; }

    public string StatusEndpointPath { get; set; } = DefaultStatusEndpointPath;

    public string StatusEndpointAuthorizationPolicy { get; set; } = InstanceAdminPolicyName;

    /// <summary>
    /// Enables the first-party scheduler administration API under <c>/api/admin/scheduler</c>. It is an operator
    /// surface that widens the authenticated attack surface, so it stays opt-in rather than on by default.
    /// </summary>
    public bool AdminApiEnabled { get; set; }

    /// <summary>
    /// Keeps the administration API to reads only. Write affordances disappear from HAL, so a client that gates
    /// on links stops offering the actions rather than discovering them and failing at call time.
    /// </summary>
    public bool AdminApiReadOnly { get; set; } = true;

    /// <summary>
    /// Mounts the first-party Quartz.NET Blazor dashboard. Only a host that owns Razor components can serve it,
    /// so the combined <c>Event.Standalone</c> process honours this flag and the split API host ignores it.
    /// </summary>
    public bool DashboardEnabled { get; set; }

    /// <summary>Disables the dashboard's own mutating controls without affecting the administration API.</summary>
    public bool DashboardReadOnly { get; set; } = true;

    /// <summary>
    /// Base path for the dashboard UI. The package fixes this to <c>/quartz</c> whenever the dashboard shares an
    /// existing Blazor app's endpoints, which is exactly how the combined host mounts it.
    /// </summary>
    public string DashboardPath { get; set; } = DefaultDashboardPath;

    public string DashboardAuthorizationPolicy { get; set; } = InstanceAdminPolicyName;
}
