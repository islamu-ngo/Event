using System.Collections.Immutable;
using Explore.Domain;
using Microsoft.AspNetCore.Identity;
using Microsoft.EntityFrameworkCore;

namespace Explore.Persistence.Identity;

/// <summary>Closed storage manifest for the C# Local lifecycle, independent of live database grants.</summary>
internal static class LocalIdentityWriterInventory
{
    internal enum Store { Identity, Application }

    internal sealed record Source(Store Database, Type EntityType,
        ImmutableArray<string> MappedProperties, ImmutableArray<string> Select,
        ImmutableArray<string> Insert, ImmutableArray<string> Update, bool AllowDelete = false);

    internal static readonly ImmutableArray<Source> Sources =
    [
        Identity<LocalIdentityUser>(
            ["Id", "AccessFailedCount", "ConcurrencyStamp", "CreatedAt", "Email", "EmailConfirmed",
                "FirstName", "LastName", "LockoutEnabled", "LockoutEnd", "NormalizedEmail",
                "NormalizedUserName", "PasswordHash", "PhoneNumber", "PhoneNumberConfirmed",
                "SecurityStamp", "TwoFactorEnabled", "UpdatedAt", "UserName"],
            ["AccessFailedCount", "ConcurrencyStamp", "CreatedAt", "Email", "EmailConfirmed",
                "FirstName", "LastName", "LockoutEnabled", "LockoutEnd", "NormalizedEmail",
                "NormalizedUserName", "PasswordHash", "PhoneNumber", "PhoneNumberConfirmed",
                "SecurityStamp", "TwoFactorEnabled", "UpdatedAt", "UserName"]),
        Identity<LocalIdentityCredentialOperation>(
            ["Id", "ConcurrencyStamp", "CreatedAt", "ExternalLoginId", "InitiatingApplicationUserId",
                "Kind", "LocalSubjectId", "PersonalActorId", "PreviousOperationConcurrencyStamp",
                "PreviousOperationId", "ResetReason", "Stage", "UpdatedAt", "VerifiedAt", "VerifiedByApplicationUserId"],
            ["Stage", "ConcurrencyStamp", "UpdatedAt"]),
        Identity<IdentityUserToken<Guid>>(["UserId", "LoginProvider", "Name", "Value"], ["Value"]),
        Identity<LocalIdentityLifecycleOperation>(
            ["Id", "LocalSubjectId", "PersonalActorId", "ExternalLoginId", "Purpose", "Generation",
                "CredentialOperationId", "PendingAddress", "SecurityStamp", "CreatedAt", "ExpiresAt",
                "ConsumedAt", "ResultSecurityStamp", "SynchronizedAt", "DeliveryState", "DeliveryAttemptId",
                "DeliveryAttemptCount", "DeliveryAdmittedAt", "DeliveryCompletedAt"],
            ["ConsumedAt", "ResultSecurityStamp", "SynchronizedAt", "DeliveryState", "DeliveryAttemptId",
                "DeliveryAttemptCount", "DeliveryAdmittedAt", "DeliveryCompletedAt"]),
        Identity<LocalIdentityEligibility>(
            ["Id", "LocalSubjectId", "CurrentOperationId", "PersonalActorId", "ExternalLoginId",
                "State", "Generation", "CreatedAt", "UpdatedAt", "LockoutUntilUtc"],
            ["CurrentOperationId", "PersonalActorId", "ExternalLoginId", "State", "Generation", "UpdatedAt", "LockoutUntilUtc"]),
        new(Store.Identity, typeof(LocalIdentityRole), ["Id", "Name", "NormalizedName", "ConcurrencyStamp"],
            ["Id", "Name"], [], []),
        new(Store.Identity, typeof(IdentityUserRole<Guid>), ["UserId", "RoleId"], ["UserId", "RoleId"], [], []),
        Application<User>(
            ["Id", "ConcurrencyStamp", "CreatedAt", "CreatedBy", "DeletedAt", "DeletedBy", "EmailVerified",
                "IsDeleted", "LastActiveTenantId", "UpdatedAt", "UpdatedBy"],
            ["ConcurrencyStamp", "EmailVerified", "UpdatedAt", "UpdatedBy"]),
        new(Store.Application, typeof(UserPii),
            ["UserId", "Email", "FirstName", "LastName", "_dateOfBirth", "_birthDateCollectionReceiptId", "_birthDateRevision"],
            ["UserId", "Email", "FirstName", "LastName"],
            ["UserId", "Email", "FirstName", "LastName", "_dateOfBirth", "_birthDateCollectionReceiptId", "_birthDateRevision"],
            ["Email", "FirstName", "LastName"]),
        Application<Actor>(
            ["Id", "ActorTypeId", "BackgroundColor", "BackgroundEffect", "BannerColor", "ConcurrencyStamp",
                "CreatedAt", "CreatedBy", "DeletedAt", "DeletedBy", "Description", "ExternalActorSubjectId",
                "GroupId", "IsDeleted", "IsSuspended", "ModerationReasonCode", "OrganizationId", "ProfilePictureCid",
                "ServicePrincipalId", "SuspendedAt", "SuspendedBy", "UpdatedAt", "UpdatedBy", "UserId"], []),
        Application<ActorPii>(["ActorId", "DisplayName", "ExternalProfilePictureUri", "ProfilePictureStorageObjectId"],
            ["DisplayName"]),
        Application<UserExternalLogin>(
            ["Id", "AuthenticationProviderId", "CreatedAt", "CreatedBy", "ProviderDisplayName", "ProviderKey",
                "UpdatedAt", "UpdatedBy", "UserId"], ["ProviderDisplayName", "UpdatedAt", "UpdatedBy"]),
        Application<UserIdentityEmailClaim>(["Id", "UserId", "NormalizedEmail"], [], allowDelete: true),
        Application<UserIdentityEmailEvidence>(["Id", "UserId", "ClaimId", "ExternalLoginId", "ObservedAt", "IsActive"],
            [], allowDelete: true),
        new(Store.Application, typeof(InstanceBootstrapState),
            ["Id", "Status", "Mode", "ProviderKind", "DeploymentMode", "Generation", "ConfigurationFingerprint",
                "SelectorFingerprint", "CompletedIdentityFingerprint", "CreatedAt", "SupersededAt", "CompletedAt", "CompletedByUserId"],
            ["Id", "Status", "Mode", "ProviderKind", "Generation", "CreatedAt", "CompletedByUserId"], [], []),
        new(Store.Application, typeof(PrivacyErasureSaga),
            ["IntentId", "SubjectKind", "SubjectId", "PolicyVersion", "FenceToken", "ReceiptHash", "ReceiptExpiresAtUtc",
                "FencedAtUtc", "ConcurrencyToken", "Status", "ProviderWorkCount", "CompletedProviderWorkCount",
                "LocalSettledAtUtc", "CompletedAtUtc", "UpdatedAtUtc"], ["SubjectId"], [], [])
    ];

    internal static void RequireModel(DbContext context, Source source)
    {
        var entity = context.Model.FindEntityType(source.EntityType)
            ?? throw new InvalidOperationException("local_identity_writer_source_model_missing");
        if (entity.GetTableName() is null || entity.GetProperties().Any(property => property.IsShadowProperty())
            || !entity.GetProperties().Select(property => property.Name).Order(StringComparer.Ordinal)
                .SequenceEqual(source.MappedProperties.Order(StringComparer.Ordinal), StringComparer.Ordinal)
            || source.Select.Concat(source.Insert).Concat(source.Update)
                .Any(property => !source.MappedProperties.Contains(property, StringComparer.Ordinal)))
            throw new InvalidOperationException("local_identity_writer_source_model_drift");
    }

    private static Source Identity<T>(ImmutableArray<string> properties, ImmutableArray<string> update) =>
        new(Store.Identity, typeof(T), properties, properties, properties, update);

    private static Source Application<T>(ImmutableArray<string> properties, ImmutableArray<string> update,
        bool allowDelete = false) => new(Store.Application, typeof(T), properties, properties, properties, update, allowDelete);
}
