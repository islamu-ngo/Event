using System.Security.Cryptography;
using System.Text.Json;
using Explore.Application.Authentication;
using Explore.Application.Contracts.Identity;
using Explore.Application.Responses;
using Explore.Domain;
using Explore.Domain.Enums;
using Native = Explore.Persistence.Security.PostgresLocalAdministrationContract;

namespace Explore.Persistence.Identity;

internal sealed partial class LocalIdentityCredentialStateStore
{
    private async Task<LocalCredentialCreateResult> NativeCreateAsync(
        NativeAdministrationAttempt native, IServiceProvider services, CancellationToken token)
    {
        var request = native.Request;
        var operation = Row(native.Before, nameof(LocalIdentityCredentialOperation), request.OperationId!.Value);
        if (operation is { } replay)
        {
            var receipt = NativeReceipt(replay);
            var existing = Row(native.Before, nameof(LocalIdentityUser), receipt.LocalSubjectId);
            if (receipt.Kind != LocalCredentialOperationKind.Create
                || receipt.InitiatingApplicationUserId != request.ActorId || existing is not { } user
                || Text(user, "NormalizedUserName") != request.NormalizedUserName
                || Text(user, "NormalizedEmail") != request.NormalizedEmail
                || Text(user, "FirstName") != request.FirstName || Text(user, "LastName") != request.LastName)
                return LocalCredentialCreateResult.Rejected(LocalCredentialCreateOutcome.Conflict);
            await identityFence.EnsureSubjectMayEnrollAsync(receipt.LocalSubjectId, token);
            await native.ReadOnlyAsync(token);
            return LocalCredentialCreateResult.Replayed(receipt);
        }
        if (Rows(native.Before, nameof(LocalIdentityUser)).Any())
            return LocalCredentialCreateResult.Rejected(LocalCredentialCreateOutcome.Conflict);
        var candidate = new LocalIdentityUser
        {
            Id = Guid.CreateVersion7(),
            UserName = request.Email,
            Email = request.Email,
            FirstName = request.FirstName!,
            LastName = request.LastName!,
            EmailConfirmed = true,
            LockoutEnabled = true,
            CreatedAt = NativeInstant()
        };
        await identityFence.EnsureSubjectMayEnrollAsync(candidate.Id, token);
        string password = $"Aa1!{Convert.ToHexString(RandomNumberGenerator.GetBytes(32))}";
        using var manager = PrivateManager(native, candidate, services, token);
        var result = await manager.CreateAsync(candidate, password);
        token.ThrowIfCancellationRequested();
        if (!result.Succeeded)
            return LocalCredentialCreateResult.Rejected(LocalCredentialCreateOutcome.Invalid);
        var created = Row(native.Expected, nameof(LocalIdentityCredentialOperation), request.OperationId.Value)
            ?? throw new UnauthorizedAccessException("The complete pending credential operation is absent.");
        var createdReceipt = NativeReceipt(created);
        await AddEligibilityAsync(createdReceipt.LocalSubjectId, createdReceipt.OperationId,
            createdReceipt.PersonalActorId, createdReceipt.ExternalLoginId,
            created.GetProperty("CreatedAt").GetDateTimeOffset().UtcDateTime, token);
        _nativeIssued = new(_administrationAttempt!.Invocation, native.OriginalRequest, native.Expected, null);
        return LocalCredentialCreateResult.Created(NativeReceipt(created), password);
    }

    private async Task<LocalCredentialResetResult> NativeResetAsync(
        NativeAdministrationAttempt native, IServiceProvider services, CancellationToken token)
    {
        var request = native.Request;
        await identityFence.EnsureSubjectMayEnrollAsync(request.LocalSubjectId!.Value, token);
        var replay = Row(native.Before, nameof(LocalIdentityCredentialOperation), request.OperationId!.Value);
        if (replay is { } existing)
        {
            var receipt = NativeReceipt(existing);
            if (receipt.Kind != LocalCredentialOperationKind.Reset
                || receipt.InitiatingApplicationUserId != request.ActorId
                || receipt.LocalSubjectId != request.LocalSubjectId
                || existing.GetProperty("PreviousOperationId").GetGuid() != request.ExpectedCurrentOperationId
                || existing.GetProperty("PreviousOperationConcurrencyStamp").GetGuid()
                    != request.ExpectedCurrentOperationConcurrencyStamp
                || Text(existing, "ResetReason") != request.Reason)
                return LocalCredentialResetResult.Rejected(LocalCredentialResetOutcome.Conflict);
            await native.ReadOnlyAsync(token);
            return LocalCredentialResetResult.Replayed(NativeResetReceipt(existing));
        }
        var user = Row(native.Before, nameof(LocalIdentityUser), request.LocalSubjectId!.Value);
        if (user is null) return LocalCredentialResetResult.Rejected(LocalCredentialResetOutcome.NotFound);
        var previous = Row(native.Before, nameof(LocalIdentityCredentialOperation), request.ExpectedCurrentOperationId!.Value);
        var state = NativeMetadata(native.Before, request.LocalSubjectId.Value);
        if (previous is not { } prior || prior.GetProperty("LocalSubjectId").GetGuid() != request.LocalSubjectId
            || prior.GetProperty("ConcurrencyStamp").GetGuid() != request.ExpectedCurrentOperationConcurrencyStamp
            || !NativeCurrent(prior, state) || state!.State is not (LocalCredentialState.ChangeRequired or LocalCredentialState.Ready)
            || string.IsNullOrWhiteSpace(Text(user.Value, "PasswordHash"))
            || string.IsNullOrWhiteSpace(Text(user.Value, "SecurityStamp"))
            || string.IsNullOrWhiteSpace(Text(user.Value, "ConcurrencyStamp")))
            return LocalCredentialResetResult.Rejected(LocalCredentialResetOutcome.Conflict);
        if (!NativeBinding(native.Before, prior))
            return LocalCredentialResetResult.Rejected(LocalCredentialResetOutcome.BindingIncomplete);

        var candidate = user.Value.Deserialize<LocalIdentityUser>(SerializerOptions)!;
        string password = $"Aa1!{Convert.ToHexString(RandomNumberGenerator.GetBytes(32))}";
        using var manager = PrivateManager(native, candidate, services, token);
        foreach (var validator in manager.PasswordValidators)
        {
            var validation = await validator.ValidateAsync(manager, candidate, password);
            token.ThrowIfCancellationRequested();
            if (!validation.Succeeded)
                return LocalCredentialResetResult.Rejected(LocalCredentialResetOutcome.Invalid);
        }
        string hash = manager.PasswordHasher.HashPassword(candidate, password);
        await native.CompileAsync(new Native.Accepted(
            ChangedAt: NativeInstant(), OperationStamp: Guid.CreateVersion7(),
            PreviousOperationStamp: Guid.CreateVersion7(), PasswordHash: hash,
            IdentityConcurrencyStamp: Guid.CreateVersion7().ToString("N"),
            SecurityStamp: Guid.CreateVersion7().ToString("N")), token);
        await native.ApplyAsync(token);
        var operation = Row(native.Expected, nameof(LocalIdentityCredentialOperation), request.OperationId.Value)
            ?? throw new UnauthorizedAccessException("The complete reset credential operation is absent.");
        var reset = NativeResetReceipt(operation);
        await ChangeEligibilityAsync(request.LocalSubjectId.Value, request.ExpectedCurrentOperationId.Value,
            row => row.Reset(row.Generation, request.ExpectedCurrentOperationId.Value, request.OperationId.Value,
                operation.GetProperty("CreatedAt").GetDateTimeOffset().UtcDateTime), token);
        _nativeIssued = new(_administrationAttempt!.Invocation, native.OriginalRequest, native.Expected,
            request.LocalSubjectId == request.ActorId ? reset : null);
        return LocalCredentialResetResult.Reset(reset, password);
    }

    private async Task<BaseCommandResponse<Guid>> NativeBindAsync(
        NativeAdministrationAttempt native, LocalCredentialProvisioningSnapshot snapshot, CancellationToken token)
    {
        Guid operationId = native.Request.OperationId!.Value;
        if (snapshot.Receipt.OperationId != operationId || snapshot != NativeProvisioning(native.Before, operationId)
            || snapshot.Receipt.Kind != LocalCredentialOperationKind.Create)
            return BaseCommandResponse.Conflict(id: operationId);
        await identityFence.EnsureSubjectMayEnrollAsync(snapshot.Receipt.LocalSubjectId, token);
        var op = Row(native.Before, nameof(LocalIdentityCredentialOperation), operationId)!.Value;
        bool bound = NativeBinding(native.Before, op);
        if (bound)
        {
            if (Row(native.Before, nameof(UserPii), snapshot.Receipt.LocalSubjectId, "UserId") is null
                || Row(native.Before, nameof(ActorPii), snapshot.Receipt.PersonalActorId, "ActorId") is null)
                return BaseCommandResponse.Conflict(id: operationId);
        }
        else if (snapshot.Email is null || Rows(native.Before, nameof(User)).Any()
            || Rows(native.Before, nameof(UserPii)).Any() || Rows(native.Before, nameof(Actor)).Any()
            || Rows(native.Before, nameof(ActorPii)).Any() || Rows(native.Before, nameof(UserExternalLogin)).Any())
            return BaseCommandResponse.Conflict(id: operationId);
        await native.CompileAsync(bound ? new() : new Native.Accepted(
            UserConcurrencyStamp: Guid.CreateVersion7(), ActorConcurrencyStamp: Guid.CreateVersion7()), token);
        await native.ApplyAsync(token);
        await identityFence.MatchAndBindAsync(
            new ProviderAccountKey(AuthenticationProviderKind.Local, snapshot.Receipt.LocalSubjectId.ToString("D")),
            snapshot.Receipt.ApplicationUserId, snapshot.Receipt.ExternalLoginId,
            applicationDbContext.TenantFilterTenantId, replacementProvisioning: !bound, token);
        AdvanceIssuedImage(native);
        return BaseCommandResponse.Success(id: operationId);
    }

    private async Task<LocalCredentialActivationOutcome> NativeActivateAsync(
        NativeAdministrationAttempt native, LocalCredentialProvisioningSnapshot snapshot, CancellationToken token)
    {
        Guid operationId = native.Request.OperationId!.Value;
        var operation = Row(native.Before, nameof(LocalIdentityCredentialOperation), operationId);
        if (operation is null) return LocalCredentialActivationOutcome.NotFound;
        if (snapshot.Receipt.OperationId != operationId || snapshot != NativeProvisioning(native.Before, operationId)
            || snapshot.Receipt.Kind != LocalCredentialOperationKind.Create)
            return LocalCredentialActivationOutcome.Conflict;
        await identityFence.EnsureSubjectMayEnrollAsync(snapshot.Receipt.LocalSubjectId, token);
        var state = NativeMetadata(native.Before, snapshot.Receipt.LocalSubjectId);
        if (!NativeCurrent(operation.Value, state)
            || state!.State is not (LocalCredentialState.ProvisioningPending or LocalCredentialState.ChangeRequired))
            return LocalCredentialActivationOutcome.Conflict;
        if (!NativeBinding(native.Before, operation.Value))
            return LocalCredentialActivationOutcome.BindingIncomplete;
        bool activated = state.State == LocalCredentialState.ChangeRequired;
        if (!activated)
        {
            var user = Row(native.Before, nameof(LocalIdentityUser), snapshot.Receipt.LocalSubjectId);
            if (user is null || string.IsNullOrWhiteSpace(Text(user.Value, "SecurityStamp"))
                || string.IsNullOrWhiteSpace(Text(user.Value, "ConcurrencyStamp")))
                return LocalCredentialActivationOutcome.Conflict;
        }
        await native.CompileAsync(activated ? new() : new Native.Accepted(
            ChangedAt: NativeInstant(), OperationStamp: Guid.CreateVersion7(),
            IdentityConcurrencyStamp: Guid.CreateVersion7().ToString("N"),
            SecurityStamp: Guid.CreateVersion7().ToString("N")), token);
        await native.ApplyAsync(token);
        if (!activated)
        {
            var after = Row(native.Expected, nameof(LocalIdentityCredentialOperation), operationId)!.Value;
            await ChangeEligibilityAsync(snapshot.Receipt.LocalSubjectId, operationId,
                row => row.Activate(row.Generation, operationId, after.GetProperty("UpdatedAt").GetDateTimeOffset().UtcDateTime), token);
        }
        AdvanceIssuedImage(native);
        return activated ? LocalCredentialActivationOutcome.AlreadyActivated : LocalCredentialActivationOutcome.Activated;
    }

    private void AdvanceIssuedImage(NativeAdministrationAttempt native)
    {
        if (_nativeIssued is not { } issued) return;
        issued.Data = native.Expected;
        issued.Committed = false;
    }
}
