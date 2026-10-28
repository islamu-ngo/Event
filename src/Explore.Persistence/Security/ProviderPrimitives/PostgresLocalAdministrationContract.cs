using System.Data;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using Explore.Application.Contracts.Identity;
using Explore.Domain;
using Explore.Persistence.Identity;
using Microsoft.AspNetCore.Identity;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Storage;
using Npgsql;
using NpgsqlTypes;

namespace Explore.Persistence.Security;

/// <summary>
/// Execute-only lifecycle data custody on the original ordinary Runtime connector.
/// This capability neither authenticates a human nor replaces the original S/P/route/H
/// sources. Its owner can touch only the eight declared lifecycle relations and its
/// private epoch/attempt records. Installation is explicit, never runtime repair.
/// </summary>
public static partial class PostgresLocalAdministrationContract
{
    public const string OwnerRole = "event_local_administration_owner";
    public const string MigratorRole = "event_local_administration_migrator";
    public const string RuntimeRole = "event_local_administration_runtime";
    public const string SourceTable = "local_administration_source";
    private const string AttemptTable = "local_administration_attempt";
    private const string Prefix = "local_administration_";
    private const string Stamp = "local-administration:v1:";
    private const string Policy = "local_administration_data";
    private static readonly string[] Roles = [OwnerRole, MigratorRole, RuntimeRole];

    // Reviewed against the actual EF mappings. Adding a mapped property must require
    // an explicit inventory/compiler review, never enlarge native rights implicitly.
    internal sealed record Relation(Type Type, string Key, string[] Properties)
    {
        internal string[] MappedProperties { get; init; } = Properties;
    }

    internal static readonly Relation[] Inventory =
    [
        new(typeof(LocalIdentityUser), "Id",
            ["Id", "AccessFailedCount", "ConcurrencyStamp", "CreatedAt", "Email", "EmailConfirmed",
                "FirstName", "LastName", "LockoutEnabled", "LockoutEnd", "NormalizedEmail",
                "NormalizedUserName", "PasswordHash", "PhoneNumber", "PhoneNumberConfirmed",
                "SecurityStamp", "TwoFactorEnabled", "UpdatedAt", "UserName"]),
        new(typeof(LocalIdentityCredentialOperation), "Id",
            ["Id", "ConcurrencyStamp", "CreatedAt", "ExternalLoginId", "InitiatingApplicationUserId",
                "Kind", "LocalSubjectId", "PersonalActorId", "PreviousOperationConcurrencyStamp",
                "PreviousOperationId", "ResetReason", "Stage", "UpdatedAt", "VerifiedAt",
                "VerifiedByApplicationUserId"]),
        new(typeof(IdentityUserToken<Guid>), "UserId", ["UserId", "LoginProvider", "Name", "Value"]),
        new(typeof(User), "Id",
            ["Id", "ConcurrencyStamp", "CreatedAt", "CreatedBy", "DeletedAt", "DeletedBy",
                "EmailVerified", "IsDeleted", "LastActiveTenantId", "UpdatedAt", "UpdatedBy"]),
        new(typeof(UserPii), "UserId", ["UserId", "Email", "FirstName", "LastName"])
        {
            MappedProperties = ["UserId", "Email", "FirstName", "LastName", "_dateOfBirth",
                "_birthDateCollectionReceiptId", "_birthDateRevision"]
        },
        new(typeof(Actor), "Id",
            ["Id", "ActorTypeId", "BackgroundColor", "BackgroundEffect", "BannerColor", "ConcurrencyStamp",
                "CreatedAt", "CreatedBy", "DeletedAt", "DeletedBy", "Description", "ExternalActorSubjectId",
                "GroupId", "IsDeleted", "IsSuspended", "ModerationReasonCode", "OrganizationId",
                "ProfilePictureCid", "ServicePrincipalId", "SuspendedAt", "SuspendedBy", "UpdatedAt",
                "UpdatedBy", "UserId"]),
        new(typeof(ActorPii), "ActorId",
            ["ActorId", "DisplayName", "ExternalProfilePictureUri", "ProfilePictureStorageObjectId"]),
        new(typeof(UserExternalLogin), "Id",
            ["Id", "AuthenticationProviderId", "CreatedAt", "CreatedBy", "ProviderDisplayName",
                "ProviderKey", "UpdatedAt", "UpdatedBy", "UserId"])
    ];

    /// <summary>
    /// Private physical-stage request. Producer is one of Create, Reconcile, Reset,
    /// Status, List. Normalized values come only from the retained UserManager bridge.
    /// The original request and this value must remain retained by the managed owner.
    /// </summary>
    internal sealed record Request(
        string Producer, LocalAdministrationStage Stage, Guid ActorId, Guid TenantId,
        Guid? OperationId = null, Guid? LocalSubjectId = null,
        Guid? ExpectedCurrentOperationId = null, Guid? ExpectedCurrentOperationConcurrencyStamp = null,
        Guid? ExpectedOperationConcurrencyStamp = null, string? Reason = null,
        string? Email = null, string? FirstName = null, string? LastName = null,
        string? NormalizedUserName = null, string? NormalizedEmail = null,
        int PageNumber = 1, int PageSize = 20)
    {
        public override string ToString() => nameof(Request);
    }

    /// <summary>
    /// Validator/hasher output, not row patches. Null fields are required for stages
    /// which do not consume them. Temporary plaintext credentials never cross this port.
    /// Times must be UTC and representable at PostgreSQL microsecond precision.
    /// </summary>
    internal sealed record Accepted(
        DateTime? ChangedAt = null, Guid? LocalSubjectId = null,
        Guid? PersonalActorId = null, Guid? ExternalLoginId = null,
        Guid? OperationStamp = null, Guid? PreviousOperationStamp = null,
        string? PasswordHash = null, string? IdentityConcurrencyStamp = null,
        string? SecurityStamp = null, Guid? UserConcurrencyStamp = null,
        Guid? ActorConcurrencyStamp = null)
    {
        public override string ToString() => nameof(Accepted);
    }

    internal static string FunctionSql(ExploreDbContext context, string name) =>
        new HumanPrincipalSourceSql(context).Name(Prefix + name);

    internal static void RequireInventory(ExploreDbContext context)
    {
        foreach (var source in Inventory)
            RequireModel(context, source);
    }

    internal static void RequireModel(ExploreDbContext context, Relation source)
    {
        var entity = context.Model.FindEntityType(source.Type)
            ?? throw new InvalidOperationException("local_administration_colocated_model_required");
        if (!entity.GetProperties().Select(property => property.Name).Order(StringComparer.Ordinal)
                .SequenceEqual(source.MappedProperties.Order(StringComparer.Ordinal), StringComparer.Ordinal)
            || entity.GetProperties().Any(property => property.IsShadowProperty())
            || source.Properties.Any(property => entity.FindProperty(property) is null)
            || entity.FindPrimaryKey() is not { } key
            || !key.Properties.Select(property => property.Name).SequenceEqual(
                source.Type == typeof(IdentityUserToken<Guid>) ? ["UserId", "LoginProvider", "Name"] : [source.Key],
                StringComparer.Ordinal)
            || entity.GetTableName() is null)
            throw new InvalidOperationException("local_administration_explicit_inventory_drift");
    }

    public static async Task ApplyAsync(ExploreDbContext context, PostgresNativeQueueEnrollment enrollment,
        CancellationToken cancellationToken = default)
    {
        if (!context.Database.IsNpgsql())
            throw new NotSupportedException("local_administration_native_provider_required");
        await using var owned = context.Database.CurrentTransaction is null
            ? await context.Database.BeginTransactionAsync(cancellationToken) : null;
        await using var command = context.Database.GetDbConnection().CreateCommand();
        command.Transaction = context.Database.CurrentTransaction!.GetDbTransaction();
        command.CommandText = BuildSql(context, enrollment);
        command.CommandTimeout = context.Database.GetCommandTimeout() ?? command.CommandTimeout;
        await command.ExecuteNonQueryAsync(cancellationToken);
        if (owned is not null) await owned.CommitAsync(cancellationToken);
    }

    public static async Task RequireReadyAsync(ExploreDbContext context, PostgresNativeQueueEnrollment enrollment,
        CancellationToken cancellationToken = default)
    {
        RequireInventory(context);
        if (!context.Database.IsNpgsql() || context.Database.GetDbConnection().State != ConnectionState.Open)
            throw new UnauthorizedAccessException("Local native custody requires its retained open PostgreSQL connection.");
        await using var command = context.Database.GetDbConnection().CreateCommand();
        command.Transaction = context.Database.CurrentTransaction?.GetDbTransaction();
        // No private-table reads here: the caller has EXECUTE, not scalar authority.
        command.CommandText = $"DO $ready$ BEGIN {Guard(context, enrollment)} {ExactBodies(context, enrollment)} END $ready$;";
        await command.ExecuteNonQueryAsync(cancellationToken);
    }

    /// <summary>
    /// Read is intentionally available before Acquire, including the later Disclose
    /// owner's private self-reset comparison. It does not require a new valid stamp.
    /// </summary>
    internal static Task<string> ReadAsync(ExploreDbContext context, PostgresNativeQueueEnrollment enrollment,
        Request request, CancellationToken token) => InvokeAsync(context, enrollment, "read", request, null, token);

    internal static Task<string> AcquireAsync(ExploreDbContext context, PostgresNativeQueueEnrollment enrollment,
        Request request, string originalImage, CancellationToken token) =>
        InvokeAsync(context, enrollment, "acquire", request, originalImage, token);

    internal static Task<string> CompileAsync(ExploreDbContext context, PostgresNativeQueueEnrollment enrollment,
        Request request, Accepted accepted, CancellationToken token)
    {
        if (accepted.ChangedAt is { } instant && (instant.Kind != DateTimeKind.Utc || instant.Ticks % 10 != 0))
            throw new ArgumentException("Native lifecycle time must be UTC at microsecond precision.", nameof(accepted));
        var payload = JsonSerializer.SerializeToNode(accepted)!.AsObject();
        if (accepted.ChangedAt is { } changedAt)
            payload[nameof(Accepted.ChangedAt)] = changedAt.ToString("O", System.Globalization.CultureInfo.InvariantCulture);
        return InvokeAsync(context, enrollment, "compile", request, payload.ToJsonString(), token);
    }

    internal static Task<string> ApplyPlanAsync(ExploreDbContext context, PostgresNativeQueueEnrollment enrollment,
        Request request, CancellationToken token) => InvokeAsync(context, enrollment, "apply", request, null, token);

    /// <summary>
    /// Call in the last asynchronous source-finalization callback, before authentic
    /// synchronous deadlines and OriginalOwnerSettlement. The deferred trigger only
    /// checks the private completion seal; it never reopens human source validation.
    /// </summary>
    internal static Task<string> CompleteAsync(ExploreDbContext context, PostgresNativeQueueEnrollment enrollment,
        Request request, string disclosedImage, CancellationToken token) =>
        InvokeAsync(context, enrollment, "complete", request, disclosedImage, token);

    private static async Task<string> InvokeAsync(ExploreDbContext context, PostgresNativeQueueEnrollment enrollment,
        string function, Request request, string? argument, CancellationToken token)
    {
        await RequireReadyAsync(context, enrollment, token);
        if (context.Database.CurrentTransaction?.GetDbTransaction() is not { IsolationLevel: IsolationLevel.Serializable }
            || System.Transactions.Transaction.Current is not null)
            throw new UnauthorizedAccessException("Local native data requires its original Serializable physical owner.");
        await using var command = context.Database.GetDbConnection().CreateCommand();
        command.Transaction = context.Database.CurrentTransaction.GetDbTransaction();
        command.CommandText = $"SELECT {FunctionSql(context, function)}(@request{(argument is null ? "" : ",@argument")})::text";
        command.Parameters.Add(new NpgsqlParameter("request", NpgsqlDbType.Jsonb) { Value = JsonSerializer.Serialize(request) });
        if (argument is not null)
            command.Parameters.Add(new NpgsqlParameter("argument", NpgsqlDbType.Jsonb) { Value = argument });
        return (string)(await command.ExecuteScalarAsync(token)
            ?? throw new UnauthorizedAccessException("The retained native lifecycle result is absent."));
    }

    private static string Text(string value) => "E'" + value.Replace("\\", "\\\\", StringComparison.Ordinal)
        .Replace("'", "''", StringComparison.Ordinal).Replace("$", "\\044", StringComparison.Ordinal) + "'";

    private static string Hash(string value) => Convert.ToHexStringLower(SHA256.HashData(Encoding.UTF8.GetBytes(value)));
    private static string Label(Type type) => type == typeof(IdentityUserToken<Guid>) ? "CredentialToken" : type.Name;
}
