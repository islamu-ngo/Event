using Explore.Domain;

namespace Explore.Persistence.Security;

internal static partial class MixedSettingsDataImage
{
    internal static readonly Type[] PlanReadTypes =
    [
        typeof(TenantPlanAssignment), typeof(TenantPlanVersion), typeof(TenantPlanVersionSetting),
        typeof(TenantPlanVersionQuota), typeof(TenantPlan)
    ];

    internal static string PlanPredicate(ExploreDbContext context, Type type, string assignmentId)
    {
        var sql = new HumanPrincipalSourceSql(context);
        string assignments = sql.Table(typeof(TenantPlanAssignment));
        string assignmentKey = sql.Column(typeof(TenantPlanAssignment), nameof(TenantPlanAssignment.Id));
        string versionKey = sql.Column(typeof(TenantPlanAssignment), nameof(TenantPlanAssignment.TenantPlanVersionId));
        if (type == typeof(TenantPlanAssignment))
            return $"r.{assignmentKey} = {assignmentId}";
        string selectedVersion = $"SELECT a.{versionKey} FROM {assignments} a WHERE a.{assignmentKey} = {assignmentId}";
        if (type == typeof(TenantPlanVersion))
            return $"r.{sql.Column(type, "Id")} IN ({selectedVersion})";
        if (type == typeof(TenantPlan))
            return $"r.{sql.Column(type, "Id")} IN (SELECT v.{sql.Column(typeof(TenantPlanVersion), "TenantPlanId")}"
                + $" FROM {sql.Table(typeof(TenantPlanVersion))} v WHERE v.{sql.Column(typeof(TenantPlanVersion), "Id")} IN ({selectedVersion})"
                + $" UNION SELECT a.{sql.Column(typeof(TenantPlanAssignment), "TenantPlanId")} FROM {assignments} a"
                + $" WHERE a.{assignmentKey} = {assignmentId})";
        return $"r.{sql.Column(type, "TenantPlanVersionId")} IN ({selectedVersion})";
    }
}
