using System.Security.Cryptography;
using Explore.Application.Contracts.Identity;
using Explore.Application.Contracts.Infrastructure;
using Explore.Application.Contracts.Services;
using Explore.Application.Exceptions;
using Explore.Application.Features.Authentication.Local.Models;
using Explore.Domain;
using Explore.Domain.Enums;
using Microsoft.Extensions.DependencyInjection;

namespace Explore.Application.Services;

public sealed class LocalVisitorEnrollmentService(
    ILocalIdentityExecutionScopeFactory scopes, ILocalVisitorEnrollmentIntentProtector intents)
    : ILocalVisitorEnrollmentService
{
    public async Task<LocalVisitorEnrollmentReservation> ReserveAsync(
        Guid operationId, string proofDigest, CancellationToken cancellationToken)
    {
        await using var selected = await scopes.CreateAsync(cancellationToken).ConfigureAwait(false);
        var store = selected.Services.GetRequiredService<ILocalVisitorEnrollmentStore>();
        var admission = new TenantEnrollmentAdmissionOperation(store.CreateAdmissionWriter(operationId),
            selected.Services.GetRequiredService<IVisitorAccessCapabilityResolver>());
        var dispatcher = selected.Services.GetRequiredService<IAuthenticationProviderDispatcher>();
        var reserved = await store.ReserveAsync(operationId, proofDigest, async token =>
        {
            await RequireLocalProviderAsync(dispatcher, token).ConfigureAwait(false);
            await admission.AuthorizeNewMembershipAsync(token).ConfigureAwait(false);
        }, cancellationToken).ConfigureAwait(false);
        // Protect the committed storage image, including its persisted timestamp precision.
        var current = await store.ReadAsync(operationId, cancellationToken).ConfigureAwait(false);
        if (current is null || current.TenantId != store.TenantId
            || current.IntentDigest != proofDigest || current.Generation != reserved.Generation
            || current.LocalSubjectId != reserved.LocalSubjectId
            || current.PersonalActorId != reserved.PersonalActorId
            || current.ExternalLoginId != reserved.ExternalLoginId
            || current.CredentialOperationId != reserved.CredentialOperationId)
            throw new UnauthorizedAccessException("The original enrollment reservation changed.");
        string claim = intents.Protect(new LocalVisitorEnrollmentIntent(
            current.Id, current.TenantId, current.IntentDigest, current.ExpiresAt));
        return new LocalVisitorEnrollmentReservation(current.Id, claim, current.ExpiresAt);
    }

    public async Task<Guid> RegisterAsync(
        LocalVisitorRegistrationRequestDto request, string proofDigest, CancellationToken cancellationToken)
    {
        var validation = await new Features.Authentication.Local.Validators.LocalVisitorRegistrationRequestDtoValidator()
            .ValidateAsync(request, cancellationToken).ConfigureAwait(false);
        if (!validation.IsValid)
            throw new LocalVisitorEnrollmentRejectedException();
        var intent = intents.Unprotect(request.EnrollmentIntent)
            ?? throw new UnauthorizedAccessException("The original enrollment intent is unavailable.");
        if (intent.OperationId != request.OperationId || !MatchesProof(intent.ProofDigest, proofDigest))
            throw new UnauthorizedAccessException("The original enrollment intent changed.");

        await using var selected = await scopes.CreateAsync(cancellationToken).ConfigureAwait(false);
        var store = selected.Services.GetRequiredService<ILocalVisitorEnrollmentStore>();
        var current = await store.ReadAsync(request.OperationId, cancellationToken).ConfigureAwait(false);
        if (current is null || current.TenantId != store.TenantId || current.TenantId != intent.TenantId
            || current.IntentDigest != intent.ProofDigest || current.ExpiresAt != intent.ExpiresAt
            || current.Stage == VisitorEnrollmentStage.Canceled)
            throw new UnauthorizedAccessException("The original enrollment reservation is unavailable.");
        if (current.Stage == VisitorEnrollmentStage.Active)
            return current.Id;

        if (current.Stage is VisitorEnrollmentStage.Reserved or VisitorEnrollmentStage.IdentityPrepared)
        {
            var prepared = await store.PrepareAsync(current.Id, request.Username.Trim(), request.Password,
                string.IsNullOrWhiteSpace(request.Email) ? null : request.Email.Trim(),
                cancellationToken).ConfigureAwait(false);
            if (prepared is null)
                throw new LocalVisitorEnrollmentRejectedException();
            _ = await store.RecordPreparedAsync(current.Id, cancellationToken).ConfigureAwait(false)
                ?? throw new UnauthorizedAccessException("The original credential preparation changed.");
            var dispatcher = selected.Services.GetRequiredService<IAuthenticationProviderDispatcher>();
            var admission = new TenantEnrollmentAdmissionOperation(store.CreateAdmissionWriter(current.Id),
                selected.Services.GetRequiredService<IVisitorAccessCapabilityResolver>());
            try
            {
                await admission.ExecuteAsync(token => RequireLocalProviderAsync(dispatcher, token), cancellationToken)
                    .ConfigureAwait(false);
            }
            catch (TenantEnrollmentClosedException)
            {
                // A definite admission refusal precedes any membership commit.
                // Preserve the original receipt and cancel before native cleanup.
                _ = await store.CancelAsync(current.Id, false, cancellationToken).ConfigureAwait(false);
                throw;
            }
        }

        LocalCredentialActivationOutcome outcome = await store.ActivateAsync(current.Id, cancellationToken)
            .ConfigureAwait(false);
        if (outcome is not (LocalCredentialActivationOutcome.Activated or LocalCredentialActivationOutcome.AlreadyActivated))
            throw new UnauthorizedAccessException("The original credential activation is unavailable.");
        var acknowledged = await store.AcknowledgeAsync(current.Id, cancellationToken).ConfigureAwait(false);
        if (acknowledged?.Stage != VisitorEnrollmentStage.Active)
            throw new UnauthorizedAccessException("The original enrollment acknowledgement is unavailable.");
        return acknowledged.Id;
    }

    private static async Task RequireLocalProviderAsync(
        IAuthenticationProviderDispatcher dispatcher, CancellationToken cancellationToken)
    {
        if (await dispatcher.GetActivePrimaryProviderAsync(cancellationToken).ConfigureAwait(false)
            != AuthenticationProviderKind.Local)
            throw new TenantEnrollmentClosedException();
    }

    private static bool MatchesProof(string expected, string supplied)
    {
        if (supplied is not { Length: 64 }
            || supplied.Any(character => character is not (>= '0' and <= '9' or >= 'a' and <= 'f')))
            return false;
        return CryptographicOperations.FixedTimeEquals(Convert.FromHexString(expected), Convert.FromHexString(supplied));
    }
}
