namespace Explore.Persistence.Security;

public static partial class PostgresPublicEventResponseClockContract
{
    private static string CallerPrivilegeGuard(ExploreDbContext context, SourceSql sql, PostgresNativeQueueEnrollment enrollment) =>
        EventReadProjectionSql.RuntimeCallerPrivilegeGuard(context, sql.Names, sql.Sources.Keys.ToArray(),
            enrollment, "public_clock", allowLookupReferences: false);

    private static string PolicyGuard(SourceSql sql, IReadOnlyDictionary<Type, string> policies) =>
        string.Join("\n", policies.Select(pair => EventReadProjectionSql.ReadPolicyGuard(
            sql.Names.Table(pair.Key), Policy, OwnerRole, Stamp + Hash(pair.Value) + ":", "public_clock")));

    private static string SourceGuard(ExploreDbContext context, SourceSql sql, PostgresNativeQueueEnrollment enrollment) =>
        EventReadProjectionSql.PublicSourceGuard(context, sql.Names, sql.Sources, enrollment.MigratorLogin, "public_clock");
}
