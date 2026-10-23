using System.Text.Json;
using Explore.Application.Contracts.Identity;
using Explore.Domain;
using Explore.Domain.Enums;

namespace Explore.Persistence.Identity;

internal sealed partial class LocalIdentityCredentialStateStore
{
    private static LocalCredentialOperationReceipt NativeReceipt(JsonElement operation) => new(
        operation.GetProperty("Id").GetGuid(),
        (LocalCredentialOperationKind)operation.GetProperty("Kind").GetInt32(),
        (LocalCredentialOperationStage)operation.GetProperty("Stage").GetInt32(),
        operation.GetProperty("InitiatingApplicationUserId").GetGuid(),
        operation.GetProperty("LocalSubjectId").GetGuid(),
        operation.GetProperty("PersonalActorId").GetGuid(),
        operation.GetProperty("ExternalLoginId").GetGuid(),
        operation.GetProperty("CreatedAt").GetDateTime());

    private static LocalCredentialResetReceipt NativeResetReceipt(JsonElement operation) => new(
        NativeReceipt(operation), operation.GetProperty("PreviousOperationId").GetGuid(),
        operation.GetProperty("PreviousOperationConcurrencyStamp").GetGuid(),
        Text(operation, "ResetReason")!);

    private static LocalCredentialStateMetadata? NativeMetadata(JsonElement data, Guid subject)
    {
        var row = Row(data, "CredentialToken", subject, "UserId");
        return row is null ? null : DeserializeMetadata(Text(row.Value, "Value"));
    }

    private static bool NativeCurrent(JsonElement operation, LocalCredentialStateMetadata? state)
    {
        var receipt = NativeReceipt(operation);
        return state is not null && state.OperationId == receipt.OperationId
            && state.ApplicationUserId == receipt.LocalSubjectId
            && receipt.Kind is LocalCredentialOperationKind.Create or LocalCredentialOperationKind.Reset
            && (state.State == LocalCredentialState.ProvisioningPending
                    && receipt.Kind == LocalCredentialOperationKind.Create
                    && receipt.Stage == LocalCredentialOperationStage.ProvisioningPending
                || state.State == LocalCredentialState.ChangeRequired
                    && receipt.Stage == LocalCredentialOperationStage.ChangeRequired
                || state.State == LocalCredentialState.Ready
                    && receipt.Stage == LocalCredentialOperationStage.Replaced);
    }

    private static LocalCredentialProvisioningSnapshot? NativeProvisioning(JsonElement data, Guid operationId)
    {
        var operation = Row(data, nameof(LocalIdentityCredentialOperation), operationId);
        if (operation is null) return null;
        var receipt = NativeReceipt(operation.Value);
        var user = Row(data, nameof(LocalIdentityUser), receipt.LocalSubjectId);
        if (user is null || string.IsNullOrWhiteSpace(Text(user.Value, "UserName"))
            || string.IsNullOrWhiteSpace(Text(user.Value, "FirstName")))
            return null;
        return new(receipt, operation.Value.GetProperty("ConcurrencyStamp").GetGuid(),
            Text(user.Value, "Email"), Text(user.Value, "FirstName")!, Text(user.Value, "LastName")!,
            user.Value.GetProperty("EmailConfirmed").GetBoolean(), Text(user.Value, "UserName"));
    }

    private static LocalCredentialOperationStatus? NativeStatus(JsonElement data, Guid operationId)
    {
        var operation = Row(data, nameof(LocalIdentityCredentialOperation), operationId);
        if (operation is null) return null;
        var row = operation.Value;
        var receipt = NativeReceipt(row);
        var state = NativeMetadata(data, receipt.LocalSubjectId);
        bool current = NativeCurrent(row, state);
        return new(receipt, row.GetProperty("ConcurrencyStamp").GetGuid(),
            row.GetProperty("VerifiedByApplicationUserId").GetGuid(), row.GetProperty("VerifiedAt").GetDateTime(),
            row.GetProperty("UpdatedAt").ValueKind == JsonValueKind.Null ? null : row.GetProperty("UpdatedAt").GetDateTime(),
            current, current ? state!.State : null,
            receipt.Kind == LocalCredentialOperationKind.Reset ? NativeResetReceipt(row) : null);
    }

    private static bool NativeBinding(JsonElement data, JsonElement operation)
    {
        var receipt = NativeReceipt(operation);
        var user = Row(data, nameof(User), receipt.LocalSubjectId);
        var actor = Row(data, nameof(Actor), receipt.PersonalActorId);
        var login = Row(data, nameof(UserExternalLogin), receipt.ExternalLoginId);
        return user is { } u && !u.GetProperty("IsDeleted").GetBoolean()
            && actor is { } a && !a.GetProperty("IsDeleted").GetBoolean() && !a.GetProperty("IsSuspended").GetBoolean()
            && a.GetProperty("ActorTypeId").GetInt32() == (int)ActorTypeEnum.User
            && a.GetProperty("UserId").GetGuid() == receipt.LocalSubjectId
            && a.GetProperty("OrganizationId").ValueKind == JsonValueKind.Null
            && a.GetProperty("GroupId").ValueKind == JsonValueKind.Null
            && a.GetProperty("ServicePrincipalId").ValueKind == JsonValueKind.Null
            && a.GetProperty("ExternalActorSubjectId").ValueKind == JsonValueKind.Null
            && login is { } l && l.GetProperty("UserId").GetGuid() == receipt.LocalSubjectId
            && l.GetProperty("AuthenticationProviderId").GetInt32() == (int)AuthenticationProviderKind.Local
            && Text(l, "ProviderKey") == receipt.LocalSubjectId.ToString("D");
    }

    private static LocalIdentityPage NativePage(JsonElement data, int pageNumber, int pageSize)
    {
        var summaries = new List<LocalIdentitySummary>();
        foreach (var id in data.GetProperty("Page").EnumerateArray())
        {
            Guid subject = id.GetGuid();
            var user = Row(data, nameof(LocalIdentityUser), subject)
                ?? throw new UnauthorizedAccessException("The native Local page has no matching identity.");
            var state = NativeMetadata(data, subject);
            var operation = state is null ? null : Row(data, nameof(LocalIdentityCredentialOperation), state.OperationId);
            bool current = operation is { } op && op.GetProperty("LocalSubjectId").GetGuid() == subject
                && NativeCurrent(op, state);
            summaries.Add(new(subject, Text(user, "Email"), Text(user, "FirstName"), Text(user, "LastName"),
                user.GetProperty("EmailConfirmed").GetBoolean(), current ? state!.State : null,
                current ? state!.OperationId : null,
                current ? operation!.Value.GetProperty("ConcurrencyStamp").GetGuid() : null,
                current && NativeBinding(data, operation!.Value)));
        }
        return new(summaries, pageNumber, pageSize, data.GetProperty("TotalCount").GetInt32());
    }
}
