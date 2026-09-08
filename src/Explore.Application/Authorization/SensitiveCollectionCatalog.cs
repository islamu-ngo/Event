namespace Explore.Application.Authorization;

/// <summary>
/// The row constraint a sensitive collection must apply <em>before</em> <c>Count</c>, <c>Skip</c>, and
/// <c>Take</c>. Naming it makes the requirement reviewable; applying it late is what leaks a count.
/// </summary>
public enum SensitiveCollectionScope
{
    /// <summary>
    /// Rows are confined to the ambient tenant. Satisfied by the EF global tenant query filter, which
    /// stays authoritative — this value records the dependency rather than duplicating the filter.
    /// </summary>
    Tenant = 0,

    /// <summary>Rows are confined to one organization in addition to the tenant filter.</summary>
    Organization = 1,

    /// <summary>Rows are confined to one event in addition to the tenant filter.</summary>
    Event = 2,

    /// <summary>Rows are confined to the actor they were granted to, in addition to the tenant filter.</summary>
    RecipientActor = 3,

    /// <summary>Rows are confined to the subscribing actor, in addition to the tenant filter.</summary>
    SubscriberActor = 4,

    /// <summary>
    /// Rows are confined to one support-access session, and to the tenant that session targets. The
    /// target tenant is not the ambient tenant during support access, so the global tenant filter does
    /// not constrain this on its own.
    /// </summary>
    SupportAccessSession = 5
}

/// <summary>
/// One authorization-sensitive paged collection.
/// </summary>
/// <param name="CollectionName">
/// The query request type name. Used as the catalog key so a rename cannot silently orphan an entry.
/// </param>
/// <param name="ResourceKind">Resource kind whose permission gates access to the collection.</param>
/// <param name="Action">Action checked before the collection is read.</param>
/// <param name="Scope">The row constraint that must be applied before counting or paging.</param>
/// <param name="Sensitivity">Why unauthorized disclosure of rows, counts, or existence matters here.</param>
public sealed record SensitiveCollection(
    string CollectionName,
    string ResourceKind,
    string Action,
    SensitiveCollectionScope Scope,
    string Sensitivity);

/// <summary>
/// Closed inventory of the paged collections where unauthorized membership, existence, count, or
/// pagination shape is a disclosure, together with the public collections deliberately left out.
/// <para>
/// This exists because "is this list sensitive?" is a judgement that has to be made once, in the open,
/// rather than re-derived by whoever next touches a repository. A paginated read that filters after
/// paging still leaks: the caller learns the total, and the page sizes reveal where the hidden rows sit.
/// </para>
/// <para>
/// Scope discipline: this is an inventory of named collections, not a universal query planner. Public
/// collections stay out. A collection that cannot express its constraint as one of
/// <see cref="SensitiveCollectionScope"/> must deny rather than post-filter.
/// </para>
/// </summary>
public static class SensitiveCollectionCatalog
{
    /// <summary>Collections whose rows, counts, and existence require authorization.</summary>
    public static IReadOnlyList<SensitiveCollection> Protected { get; } =
    [
        new(
            CollectionName: "GetOrganizationSharedContactsQuery",
            ResourceKind: ResourceKinds.EventContactShareConsent,
            Action: AuthorizationActions.ViewSharedContacts,
            Scope: SensitiveCollectionScope.RecipientActor,
            Sensitivity:
                "Registrant email addresses released under explicit consent to one recipient organization. "
                + "A count alone discloses how many people consented to share with that organization, and a "
                + "row discloses an individual's contact details to a party they did not consent to."),

        new(
            CollectionName: "GetModerationReportQueueRequest",
            ResourceKind: ResourceKinds.Event,
            Action: AuthorizationActions.Events.ViewManagement,
            Scope: SensitiveCollectionScope.Event,
            Sensitivity:
                "Abuse reports against an event, including reporter-linked signals and moderator assignment. "
                + "Existence and count disclose that an event is under moderation, which is actionable "
                + "information for both the reported party and the reporters."),

        new(
            CollectionName: "GetStorageObjectListRequest",
            ResourceKind: ResourceKinds.StorageObject,
            Action: AuthorizationActions.StorageObjects.View,
            Scope: SensitiveCollectionScope.Tenant,
            Sensitivity:
                "Uploaded files with owning-actor PII joined in. Cross-tenant disclosure of filenames and "
                + "uploader identity is a tenant-isolation breach, and the count reveals tenant activity volume."),

        new(
            CollectionName: "GetActorSubscriptionsRequest",
            ResourceKind: ResourceKinds.ActorSubscription,
            Action: AuthorizationActions.View,
            Scope: SensitiveCollectionScope.SubscriberActor,
            Sensitivity:
                "Who an actor follows. The subscription graph is personal association data; a count discloses "
                + "the size of someone's follow list even when individual rows are withheld."),

        new(
            CollectionName: "GetEventTemplateSyncHistoryQuery",
            ResourceKind: ResourceKinds.CustomPropertyTemplate,
            Action: AuthorizationActions.SyncDiff,
            Scope: SensitiveCollectionScope.Tenant,
            Sensitivity:
                "Audit-log rows describing template sync operations and the operators who ran them. Audit "
                + "history discloses administrative activity and staffing patterns across a tenant."),

        new(
            CollectionName: "GetEventSessionTemplateSyncHistoryQuery",
            ResourceKind: ResourceKinds.CustomPropertyTemplate,
            Action: AuthorizationActions.SyncDiff,
            Scope: SensitiveCollectionScope.Tenant,
            Sensitivity:
                "Session-template counterpart of the template sync audit history, with the same exposure."),

        new(
            CollectionName: "GetCustomPropertyProjectionDirtyScopesQuery",
            ResourceKind: ResourceKinds.CustomPropertyProjection,
            Action: AuthorizationActions.View,
            Scope: SensitiveCollectionScope.Tenant,
            Sensitivity:
                "Pending projection work per tenant. The pending count is an operational signal about a "
                + "tenant's data volume and processing backlog."),

        new(
            CollectionName: "ListSupportAccessSessionsQuery",
            ResourceKind: ResourceKinds.SupportAccessSession,
            Action: AuthorizationActions.SupportAccessSessions.List,
            Scope: SensitiveCollectionScope.SupportAccessSession,
            Sensitivity:
                "When support staff held access into a tenant, in what mode, and for how long. Existence "
                + "and count disclose that a tenant was being supported or investigated, and leaking this "
                + "across tenants would expose one customer's support history to another."),

        new(
            CollectionName: "GetSupportAccessAuditEventsQuery",
            ResourceKind: ResourceKinds.SupportAccessSession,
            Action: AuthorizationActions.SupportAccessSessions.ViewAudit,
            Scope: SensitiveCollectionScope.SupportAccessSession,
            Sensitivity:
                "What support staff actually did inside a tenant during one session. This is the record a "
                + "tenant relies on to hold the operator accountable, so it must be visible to exactly the "
                + "session's target tenant and to nobody else — including other tenants under support."),

        new(
            CollectionName: "GetCustomPropertyGovernanceReportQuery",
            ResourceKind: ResourceKinds.CustomPropertyGovernance,
            Action: AuthorizationActions.View,
            Scope: SensitiveCollectionScope.Tenant,
            Sensitivity:
                "Governance report over a tenant's custom property definitions and their usage. Row content "
                + "and counts describe how a tenant models its data, which is competitively meaningful and "
                + "must not cross a tenant boundary.")
    ];

    /// <summary>
    /// Paged secure queries deliberately treated as non-sensitive, each with the reason. Listing them is
    /// what makes <see cref="Protected"/> trustworthy: an unclassified query is a gap, whereas a query
    /// named here is a decision someone made and can be argued with.
    /// </summary>
    public static IReadOnlyDictionary<string, string> PublicByDesign { get; } =
        new Dictionary<string, string>(StringComparer.Ordinal)
        {
            ["GetLocationListRequest"] =
                "Venue directory. Locations are published so attendees can find events; the set and its size "
                + "are already public through event listings. Location privacy for individual events is "
                + "enforced on the event, not by hiding the venue catalog.",

            ["GetEventListAggregateViewQuery"] =
                "Read model over published events, which are public by definition. Draft and unpublished "
                + "events are excluded by the projection itself, not by hiding the collection."
        };

    /// <summary>
    /// Whether <paramref name="collectionName"/> has been classified either way. An unclassified paged
    /// secure query is the failure this catalog exists to catch.
    /// </summary>
    public static bool IsClassified(string collectionName) =>
        PublicByDesign.ContainsKey(collectionName)
        || Protected.Any(collection => string.Equals(
            collection.CollectionName,
            collectionName,
            StringComparison.Ordinal));

    /// <summary>Returns the protected entry for <paramref name="collectionName"/>, or <c>null</c>.</summary>
    public static SensitiveCollection? Find(string collectionName) =>
        Protected.FirstOrDefault(collection => string.Equals(
            collection.CollectionName,
            collectionName,
            StringComparison.Ordinal));
}
