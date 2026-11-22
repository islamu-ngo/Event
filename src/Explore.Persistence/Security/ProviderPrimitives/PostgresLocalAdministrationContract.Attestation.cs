namespace Explore.Persistence.Security;

public static partial class PostgresLocalAdministrationContract
{
    private const string CatalogueGuardName = "assert_catalogue";

    // Stored entries run as OwnerRole. Independent callers retain the inline
    // assertion and exact bodies because the helper has no application grant.
    private static string Guard(ExploreDbContext context, PostgresNativeQueueEnrollment enrollment) =>
        PostgresContractAttestationSql.BuildGuardInvocationSql(
            CatalogueGuardWitness(context, enrollment),
            FunctionSql(context, CatalogueGuardName));

    private static string CatalogueGuardWitness(ExploreDbContext context, PostgresNativeQueueEnrollment enrollment) =>
        PostgresContractAttestationSql.BuildCatalogueGuardWitnessSql(
            FunctionSql(context, CatalogueGuardName),
            OwnerRole,
            Hash(CatalogueGuardBody(context, enrollment)),
            Stamp,
            "local_administration_catalogue_guard_drift");

    private static string CatalogueGuardBody(ExploreDbContext context, PostgresNativeQueueEnrollment enrollment) =>
        PostgresContractAttestationSql.BuildCatalogueGuardBodySql(CatalogueGuard(context, enrollment));

    private static string ExactBodies(ExploreDbContext context, PostgresNativeQueueEnrollment enrollment) =>
        PostgresContractAttestationSql.BuildExactBodiesSql(
            context,
            Functions(context, enrollment),
            function => (function.Name, function.Signature, function.Body),
            FunctionSql,
            "local_administration_exact_body_drift");
}
