using System.Collections.Immutable;
using Explore.Domain;
using Explore.Persistence.Identity;
using Explore.Persistence.Security;
using Microsoft.EntityFrameworkCore;

namespace Explore.Persistence.Database;

/// <summary>
/// Storage for a new external account and its ordinary tenant membership.
/// Reuses declared graph shapes without selecting either writer's execution purpose.
/// </summary>
internal static class ExternalTenantEnrollmentInventory
{
    internal sealed record Source(Type Entity, ImmutableArray<string> Mapped,
        ImmutableArray<string> Read, ImmutableArray<string> Insert);

    internal static ImmutableArray<Source> Sources { get; } =
    [
        Graph<User>(["Id", "IsDeleted"]),
        Graph<UserPii>(["_birthDateRevision"]),
        Graph<Actor>(["Id", "UserId", "ActorTypeId", "IsDeleted", "IsSuspended", "ConcurrencyStamp"]),
        Graph<ActorPii>([]),
        Graph<UserExternalLogin>(Local<UserExternalLogin>().MappedProperties),
        Graph<TenantUser>(["Id", "TenantId", "UserId", "ActorId", "StatusId", "IsDeleted"]),
        new(typeof(UserIdentityEmailClaim), Local<UserIdentityEmailClaim>().MappedProperties,
            Local<UserIdentityEmailClaim>().Select, Local<UserIdentityEmailClaim>().Insert),
        new(typeof(UserIdentityEmailEvidence), Local<UserIdentityEmailEvidence>().MappedProperties,
            Local<UserIdentityEmailEvidence>().Select, Local<UserIdentityEmailEvidence>().Insert),
        new(typeof(Tenant), Worker<Tenant>().Insert, Worker<Tenant>().Insert, []),
        new(typeof(PrivacyErasureSaga), Local<PrivacyErasureSaga>().MappedProperties, ["SubjectId"], [])
    ];

    internal static void RequireModel(ExploreDbContext context, Source source)
    {
        var entity = context.Model.FindEntityType(source.Entity)
            ?? throw new InvalidOperationException("external_enrollment_model_missing");
        if (entity.GetTableName() is null || entity.BaseType is not null || entity.GetDerivedTypes().Any()
            || entity.GetComplexProperties().Any() || entity.GetProperties().Any(property => property.IsShadowProperty())
            || !entity.GetProperties().Select(property => property.Name).Order(StringComparer.Ordinal)
                .SequenceEqual(source.Mapped.Order(StringComparer.Ordinal), StringComparer.Ordinal)
            || source.Read.Concat(source.Insert).Any(property => !source.Mapped.Contains(property, StringComparer.Ordinal)))
            throw new InvalidOperationException("external_enrollment_model_drift");
    }

    private static Source Graph<T>(ImmutableArray<string> read) =>
        new(typeof(T), Local<T>().MappedProperties, read, Worker<T>().Insert);

    private static LocalIdentityWriterInventory.Source Local<T>() =>
        LocalIdentityWriterInventory.Sources.Single(source => source.EntityType == typeof(T));

    private static ManagedTenantWorkerCreationInventory.Source Worker<T>() =>
        ManagedTenantWorkerCreationInventory.Sources.Single(source => source.Entity == typeof(T));
}
