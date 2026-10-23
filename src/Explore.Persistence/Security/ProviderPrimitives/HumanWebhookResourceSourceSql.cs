using Explore.Domain;
using Microsoft.EntityFrameworkCore;

namespace Explore.Persistence.Security;

/// <summary>Fixed lineage vocabulary. Each nested MySQL read is current when the owner is held.</summary>
internal sealed class HumanWebhookResourceSourceSql(ExploreDbContext context, bool currentReads = false)
{
    internal static readonly (Type Type, string[] Columns)[] Sources =
    [
        (typeof(WebhookConsumer), ["Id", "TenantId", "InstanceId", "OrganizationId", "GroupId", "OwnerUserId",
            "ConfigurationScopeId", "ConsumerKindId", "StatusId", "ProviderModeId", "ConfigurationVersion"]),
        (typeof(WebhookEndpoint), ["Id", "TenantId", "InstanceId", "ConfigurationScopeId", "ConsumerId",
            "StatusId", "ConfigurationVersion", "DeliveryStateVersion"]),
        (typeof(WebhookDeliveryAttempt), ["Id", "TenantId", "MessageId", "EndpointId", "OutcomeId",
            "AttemptNumber", "ProcessingFence", "CompletedAt"]),
        (typeof(WebhookMessage), ["Id", "TenantId", "ConsumerId", "PayloadRetentionUntil", "PayloadClearedAt", "_payloadBytes"]),
        (typeof(WebhookLocalTargetSnapshot), ["Id", "TenantId", "WebhookMessageId", "DeliveryPlanSnapshotId",
            "WebhookEndpointId", "DeliveryStatusId", "ConcurrencyVersion", "DeliveryFence", "EndpointConfigurationVersion"]),
        (typeof(WebhookDeliveryPlanSnapshot), ["Id", "TenantId", "WebhookMessageId", "WebhookConsumerId",
            "ProviderModeId", "ConfigurationVersion"]),
        (typeof(WebhookProviderPublication), ["Id", "TenantId", "WebhookMessageId", "WebhookDeliveryPlanSnapshotId",
            "ProviderBindingId", "StatusId", "ConcurrencyVersion", "PublicationFence", "AbandonedAt"]),
        (typeof(WebhookBulkReplayOperation), ["Id", "TenantId", "OperationKey", "WebhookConsumerId",
            "WebhookEndpointId", "StatusId", "ConcurrencyVersion", "CancelledAt", "CompletedAt"]),
        (typeof(WebhookConsumerProviderBinding), ["Id", "TenantId", "WebhookConsumerId", "InstanceId",
            "ConfigurationScopeId", "ProviderKindId", "ProviderVersion", "ProviderEnvironment", "NormalizedEnvironment",
            "ApplicationUid", "NormalizedApplicationUid", "ExternalApplicationId", "NormalizedExternalApplicationId",
            "VerificationStateId", "VerifiedTenantId",
            "VerifiedWebhookConsumerId", "VerifiedAtUtc", "Capabilities", "GovernanceAllowedCapabilities",
            "CapabilityResolutionVersion", "CapabilitiesResolvedAtUtc", "IsEnabled", "ConcurrencyVersion", "VerificationFence"]),
        (typeof(InstanceBootstrapState), ["Id", "Status", "Generation", "CreatedAt"]),
        (typeof(Organization), ["Id", "IsDeleted"]),
        (typeof(OrganizationTenant), ["Id", "TenantId", "OrganizationId", "IsDeleted"]),
        (typeof(Group), ["Id", "IsDeleted"]),
        (typeof(GroupTenant), ["Id", "TenantId", "GroupId", "IsDeleted"]),
        (typeof(TenantUser), ["Id", "TenantId", "UserId", "StatusId", "IsDeleted"])
    ];

    internal HumanPrincipalSourceSql Names { get; } = new(context);
    private string Current => currentReads ? " FOR UPDATE" : "";
    private string C(int index, string alias, string property) => $"{alias}.{Names.Column(Sources[index].Type, property)}";
    private string Scope(int index, string alias, bool instance = false) =>
        $"(@tenant IS NULL OR {C(index, alias, "TenantId")} = @tenant"
        + (instance ? $" OR {C(index, alias, "TenantId")} IS NULL)" : ")");
    private string Exists(int index, string alias, string predicate) =>
        $"EXISTS (SELECT 1 FROM {Names.Table(Sources[index].Type)} {alias} WHERE {predicate}{Current})";
    private string Attempt(string alias) =>
        $"@operation = 10 AND {C(2, alias, "Id")} = @id AND {Scope(2, alias)}";
    private string Publication(string alias) =>
        $"@operation IN (14,15) AND {C(6, alias, "Id")} = @id AND {Scope(6, alias)}";
    private string Bulk(string alias) =>
        $"(({C(7, alias, "Id")} = @id AND @operation = 12) OR ({C(7, alias, "OperationKey")} = @id AND @operation = 11)) AND {Scope(7, alias)}";
    private string LocalTarget(string alias) => $"{Scope(4, alias)} AND " + Exists(2, "ta",
        $"{Attempt("ta")} AND {C(2, "ta", "TenantId")} = {C(4, alias, "TenantId")}"
        + $" AND {C(2, "ta", "MessageId")} = {C(4, alias, "WebhookMessageId")}"
        + $" AND {C(2, "ta", "EndpointId")} = {C(4, alias, "WebhookEndpointId")}");
    private string Plan(string alias) => $"{Scope(5, alias)} AND ("
        + Exists(4, "lt", $"{LocalTarget("lt")} AND {C(4, "lt", "DeliveryPlanSnapshotId")} = {C(5, alias, "Id")}")
        + " OR " + Exists(6, "pub", $"{Publication("pub")} AND {C(6, "pub", "WebhookDeliveryPlanSnapshotId")} = {C(5, alias, "Id")}") + ")";
    private string Endpoint(string alias) => $"{Scope(1, alias, true)} AND ("
        + $"(@operation BETWEEN 4 AND 9 AND {C(1, alias, "Id")} = @id)"
        + " OR " + Exists(2, "ea", $"{Attempt("ea")} AND {C(2, "ea", "EndpointId")} = {C(1, alias, "Id")}")
        + $" OR (@operation = 11 AND {C(1, alias, "Id")} = @endpoint)"
        + " OR " + Exists(7, "eb", $"{Bulk("eb")} AND {C(7, "eb", "WebhookEndpointId")} = {C(1, alias, "Id")}") + ")";
    private string Consumer(string alias) => $"{Scope(0, alias, true)} AND ("
        + $"(@operation IN (2,3,13,16) AND {C(0, alias, "Id")} = @id)"
        + " OR " + Exists(1, "ce", $"{Endpoint("ce")} AND {C(1, "ce", "ConsumerId")} = {C(0, alias, "Id")}")
        + " OR " + Exists(5, "cp", $"{Plan("cp")} AND {C(5, "cp", "WebhookConsumerId")} = {C(0, alias, "Id")}")
        + $" OR (@operation = 11 AND {C(0, alias, "Id")} = @consumer)"
        + " OR " + Exists(7, "cb", $"{Bulk("cb")} AND {C(7, "cb", "WebhookConsumerId")} = {C(0, alias, "Id")}") + ")";

    private string Fields(int index, string alias) => string.Join(",", Sources[index].Columns.Select(property =>
    {
        if (property == "_payloadBytes")
        {
            string predicate = $"{C(index, alias, property)} IS NOT NULL";
            return (context.Database.IsNpgsql() ? $"({predicate})" : $"CASE WHEN {predicate} THEN 1 ELSE 0 END")
                + $" AS {Names.Quote("HasPayload")}";
        }
        string name = index is 11 or 13 && property is "OrganizationId" or "GroupId" ? "OwnerId" : property;
        return $"{C(index, alias, property)} AS {Names.Quote(name)}";
    }));

    private string Query(int index, string predicate) =>
        $"SELECT {Fields(index, "s")} FROM {Names.Table(Sources[index].Type)} s WHERE {predicate}{Current}";

    internal string[] Queries =>
    [
        Query(0, Consumer("s")),
        Query(1, Endpoint("s")),
        Query(2, Attempt("s")),
        Query(3, $"{Scope(3, "s")} AND (" + Exists(2, "ma",
            $"{Attempt("ma")} AND {C(2, "ma", "MessageId")} = {C(3, "s", "Id")}")
            + " OR " + Exists(6, "mp", $"{Publication("mp")} AND {C(6, "mp", "WebhookMessageId")} = {C(3, "s", "Id")}") + ")"),
        Query(4, LocalTarget("s")),
        Query(5, Plan("s")),
        Query(6, Publication("s")),
        Query(7, Bulk("s")),
        Query(8, $"{Scope(8, "s", true)} AND " + Exists(0, "bc",
            $"{Consumer("bc")} AND {C(0, "bc", "Id")} = {C(8, "s", "WebhookConsumerId")}")),
        Query(9, $"@operation = 1 AND @kind = {(int)WebhookConsumerKind.Instance}"),
        Query(10, $"@operation = 1 AND @kind = {(int)WebhookConsumerKind.Organization} AND {C(10, "s", "Id")} = @owner"),
        Query(11, $"@operation = 1 AND @kind = {(int)WebhookConsumerKind.Organization}"
            + $" AND {C(11, "s", "OrganizationId")} = @owner AND {C(11, "s", "TenantId")} = @tenant"),
        Query(12, $"@operation = 1 AND @kind = {(int)WebhookConsumerKind.Group} AND {C(12, "s", "Id")} = @owner"),
        Query(13, $"@operation = 1 AND @kind = {(int)WebhookConsumerKind.Group}"
            + $" AND {C(13, "s", "GroupId")} = @owner AND {C(13, "s", "TenantId")} = @tenant"),
        Query(14, $"@operation = 1 AND @kind = {(int)WebhookConsumerKind.User}"
            + $" AND {C(14, "s", "UserId")} = @actor AND {C(14, "s", "TenantId")} = @tenant")
    ];
}
