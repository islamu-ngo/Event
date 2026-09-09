
using System.Data.Common;
using System.IdentityModel.Tokens.Jwt;
using System.Security.Claims;
using System.Security.Cryptography;
using System.Text.Json;
using Explore.Application.Configuration;
using Explore.Application.Contracts.Identity;
using Explore.Application.Contracts.Infrastructure;
using Explore.Application.Contracts.Secrets;
using Explore.Application.Features.Authentication.Local.Models;
using Explore.Domain;
using Explore.Domain.Constants;
using Explore.Domain.Enums;
using Explore.Domain.Secrets;
using Explore.Infrastructure.Authentication;
using Explore.Persistence;
using Explore.Persistence.Database;
using Explore.Persistence.Identity;
using Explore.Persistence.Repositories;
using Event.Persistence.IntegrationTests.Fixtures;
using Microsoft.AspNetCore.Identity;
using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Diagnostics;
using Microsoft.Extensions.Caching.Memory;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Options;
using Microsoft.IdentityModel.Tokens;
using NSubstitute;

namespace Event.Persistence.IntegrationTests.Identity;

public sealed class LocalCredentialFirstUseTests
{
    public enum SessionMutation
    {
        None, UnverifiedWithDeliveryOff, Reset, SecurityStamp, MissingState, PendingState, MissingBinding,
        VerificationRequired, VerificationRevoked, VerificationGranted, MalformedVerificationPolicy,
        MissingHash, MissingOperation, NonReplacedReceipt, SuspendedActor
    }
    public enum InvalidPasswordLength
    {
        TooShort,
        TooLong
    }

    public enum ReplacementWriteBoundary
    {
        AfterFirstWrite = 1,
        AfterSecondWrite = 2,
        AfterThirdWrite = 3
    }

    [Test]
    [Arguments(IdentityDatabaseTopology.Colocated)]
    [Arguments(IdentityDatabaseTopology.External)]
    public async Task TemporaryPasswordIssuesOnlyShortLivedPurposeIsolatedChallenge(IdentityDatabaseTopology topology)
    {
        await using Fixture fixture = await Fixture.CreateAsync(topology);

        LocalAuthResponseDto response = await fixture.AuthenticateAsync(fixture.TemporaryPassword);

        await Assert.That(response.Outcome).IsEqualTo(LocalAuthOutcome.ReplacementRequired);
        await Assert.That(response.Success).IsFalse();
        await Assert.That(string.IsNullOrEmpty(response.Token)).IsTrue();
        await Assert.That(response.ExpiresAt).IsNull();
        await Assert.That(response.UserId).IsNull();
        await Assert.That(response.Email).IsNull();
        await Assert.That(response.FirstName).IsNull();
        await Assert.That(response.LastName).IsNull();
        await Assert.That(response.Roles.Count).IsEqualTo(0);
        await Assert.That(response.ReplacementChallenge).IsNotNull();
        LocalIssuedReplacementChallenge challenge = response.ReplacementChallenge!;
        var handler = new JwtSecurityTokenHandler { MapInboundClaims = false };
        ClaimsPrincipal principal = handler.ValidateToken(challenge.Token,
            fixture.ValidationParameters(LocalCredentialChallengeToken.Audience), out SecurityToken validated);
        var jwt = (JwtSecurityToken)validated;
        await Assert.That(jwt.Header.Typ).IsEqualTo(LocalCredentialChallengeToken.TokenType);
        await Assert.That(jwt.Header.Alg).IsEqualTo(LocalCredentialChallengeToken.RequiredAlgorithm);
        await Assert.That(principal.FindFirst(LocalCredentialChallengeToken.PurposeClaim)?.Value)
            .IsEqualTo(LocalCredentialChallengeToken.Purpose);
        await Assert.That(principal.FindFirst(JwtRegisteredClaimNames.Sub)?.Value).IsEqualTo(fixture.Receipt.LocalSubjectId.ToString("D"));
        await Assert.That(principal.FindFirst(LocalCredentialChallengeToken.OperationIdClaim)?.Value)
            .IsEqualTo(fixture.Receipt.OperationId.ToString("D"));
        Snapshot current = await fixture.ReadAsync();
        await Assert.That(string.Equals(principal.FindFirst(LocalCredentialChallengeToken.SecurityStampClaim)?.Value,
            current.SecurityStamp, StringComparison.Ordinal)).IsTrue();
        await Assert.That(jwt.ValidTo > jwt.ValidFrom && jwt.ValidTo - jwt.ValidFrom <= TimeSpan.FromMinutes(5)).IsTrue();
        await Assert.That(challenge.ExpiresAt > fixture.Clock.GetUtcNow()
            && challenge.ExpiresAt - fixture.Clock.GetUtcNow() <= TimeSpan.FromMinutes(5)).IsTrue();
        await Assert.That(principal.Claims.Any(claim => claim.Type is "roles" or "role" or "email" or "given_name" or "family_name")).IsFalse();
        await Assert.ThrowsAsync<SecurityTokenInvalidAudienceException>(() =>
        {
            _ = handler.ValidateToken(challenge.Token, fixture.ValidationParameters(LocalIdentityOptions.Audience), out _);
            return Task.CompletedTask;
        });
    }

    [Test]
    [Arguments(IdentityDatabaseTopology.Colocated)]
    [Arguments(IdentityDatabaseTopology.External)]
    public async Task ReplacementAtomicallyReadiesCredentialAndRequiresFreshLogin(IdentityDatabaseTopology topology)
    {
        await using Fixture fixture = await Fixture.CreateAsync(topology);
        Snapshot before = await fixture.ReadAsync();
        LocalCredentialReplacementAuthority authority = fixture.Authority(before);
        string replacement = NewPassword();

        LocalCredentialReplacementOutcome outcome = await fixture.ReplaceAsync(authority: authority, password: replacement);

        await Assert.That(outcome).IsEqualTo(LocalCredentialReplacementOutcome.Replaced);
        Snapshot after = await fixture.ReadAsync();
        await Assert.That(after.Stage).IsEqualTo(LocalCredentialOperationStage.Replaced);
        await Assert.That(after.OperationStamp == before.OperationStamp).IsFalse();
        await Assert.That(string.Equals(after.PasswordHash, before.PasswordHash, StringComparison.Ordinal)).IsFalse();
        await Assert.That(string.Equals(after.SecurityStamp, before.SecurityStamp, StringComparison.Ordinal)).IsFalse();
        await Assert.That(string.Equals(after.ConcurrencyStamp, before.ConcurrencyStamp, StringComparison.Ordinal)).IsFalse();
        await fixture.AssertStateAsync(LocalCredentialState.Ready);
        await Assert.That(await fixture.PasswordIsValidAsync(replacement)).IsTrue();
        await Assert.That(await fixture.PasswordIsValidAsync(fixture.TemporaryPassword)).IsFalse();
        LocalAuthResponseDto rejected = await fixture.AuthenticateAsync(fixture.TemporaryPassword);
        await Assert.That(rejected.Outcome).IsEqualTo(LocalAuthOutcome.Failed);
        await Assert.That(string.IsNullOrEmpty(rejected.Token)).IsTrue();
        LocalAuthResponseDto authenticated = await fixture.AuthenticateAsync(replacement);
        await Assert.That(authenticated.Outcome).IsEqualTo(LocalAuthOutcome.Authenticated);
        await Assert.That(authenticated.ReplacementChallenge).IsNull();
        await Assert.That(string.IsNullOrEmpty(authenticated.Token)).IsFalse();
        var handler = new JwtSecurityTokenHandler { MapInboundClaims = false };
        ClaimsPrincipal principal = handler.ValidateToken(authenticated.Token!,
            fixture.ValidationParameters(LocalIdentityOptions.Audience), out _);
        await Assert.That(principal.FindFirst(JwtRegisteredClaimNames.Sub)?.Value).IsEqualTo(fixture.Receipt.LocalSubjectId.ToString("D"));
        await Assert.That(principal.HasClaim(claim => claim.Type == LocalCredentialChallengeToken.PurposeClaim)).IsFalse();
    }

    [Test]
    [Arguments(IdentityDatabaseTopology.Colocated)]
    [Arguments(IdentityDatabaseTopology.External)]
    public async Task SamePasswordDoesNotConsumeReplacementAuthority(IdentityDatabaseTopology topology)
    {
        await using Fixture fixture = await Fixture.CreateAsync(topology);
        Snapshot before = await fixture.ReadAsync();

        LocalCredentialReplacementOutcome outcome = await fixture.ReplaceAsync(
            authority: fixture.Authority(before), password: fixture.TemporaryPassword);

        await Assert.That(outcome).IsEqualTo(LocalCredentialReplacementOutcome.SamePassword);
        await fixture.AssertUnchangedAsync(before);
        await fixture.AssertStateAsync(LocalCredentialState.ChangeRequired);
    }

    [Test]
    [Arguments(IdentityDatabaseTopology.Colocated, InvalidPasswordLength.TooShort)]
    [Arguments(IdentityDatabaseTopology.Colocated, InvalidPasswordLength.TooLong)]
    [Arguments(IdentityDatabaseTopology.External, InvalidPasswordLength.TooShort)]
    [Arguments(IdentityDatabaseTopology.External, InvalidPasswordLength.TooLong)]
    public async Task ReplacementOutsideLoginPasswordLengthBoundsCannotConsumeAuthority(
        IdentityDatabaseTopology topology, InvalidPasswordLength invalidLength)
    {
        await using Fixture fixture = await Fixture.CreateAsync(topology);
        Snapshot before = await fixture.ReadAsync();
        string password = invalidLength switch
        {
            InvalidPasswordLength.TooShort => NewPassword()[..11],
            InvalidPasswordLength.TooLong => $"Aa1!{Convert.ToHexString(RandomNumberGenerator.GetBytes(64))}"[..129],
            _ => throw new ArgumentOutOfRangeException(nameof(invalidLength))
        };

        LocalCredentialReplacementOutcome outcome = await fixture.ReplaceAsync(
            authority: fixture.Authority(before), password: password);

        await Assert.That(outcome).IsEqualTo(LocalCredentialReplacementOutcome.InvalidPassword);
        await fixture.AssertUnchangedAsync(before);
        await fixture.AssertStateAsync(LocalCredentialState.ChangeRequired);
    }

    [Test]
    [Arguments(IdentityDatabaseTopology.Colocated)]
    [Arguments(IdentityDatabaseTopology.External)]
    public async Task AuthorityExpiringDuringNativePasswordValidationCannotCommitReplacement(IdentityDatabaseTopology topology)
    {
        await using Fixture fixture = await Fixture.CreateAsync(topology);
        Snapshot before = await fixture.ReadAsync();
        LocalCredentialReplacementAuthority authority = fixture.Authority(before);
        fixture.ValidationExpiry.Enabled = true;

        LocalCredentialReplacementOutcome outcome = await fixture.ReplaceAsync(authority: authority, password: NewPassword());

        await Assert.That(fixture.ValidationExpiry.Observed).IsTrue();
        await Assert.That(fixture.Clock.GetUtcNow() > authority.ExpiresAtUtc).IsTrue();
        await Assert.That(outcome).IsEqualTo(LocalCredentialReplacementOutcome.InvalidChallenge);
        await fixture.AssertUnchangedAsync(before);
        await fixture.AssertStateAsync(LocalCredentialState.ChangeRequired);
    }

    [Test]
    [Arguments(IdentityDatabaseTopology.Colocated, ReplacementWriteBoundary.AfterFirstWrite)]
    [Arguments(IdentityDatabaseTopology.Colocated, ReplacementWriteBoundary.AfterSecondWrite)]
    [Arguments(IdentityDatabaseTopology.Colocated, ReplacementWriteBoundary.AfterThirdWrite)]
    [Arguments(IdentityDatabaseTopology.External, ReplacementWriteBoundary.AfterFirstWrite)]
    [Arguments(IdentityDatabaseTopology.External, ReplacementWriteBoundary.AfterSecondWrite)]
    [Arguments(IdentityDatabaseTopology.External, ReplacementWriteBoundary.AfterThirdWrite)]
    public async Task AuthorityExpiringDuringReplacementWriteRollsBackAllCredentialAuthority(
        IdentityDatabaseTopology topology, ReplacementWriteBoundary boundary)
    {
        var expiry = new ReplacementWriteExpiry();
        await using Fixture fixture = await Fixture.CreateAsync(topology, transactionObserver: expiry);
        Snapshot before = await fixture.ReadAsync();
        LocalCredentialReplacementAuthority authority = fixture.Authority(before);
        string replacement = NewPassword();
        await using AsyncServiceScope request = fixture.Provider.CreateAsyncScope();
        expiry.Arm(identity: fixture.Identity(request), boundary: boundary, clock: fixture.Clock,
            expiresAt: authority.ExpiresAtUtc);

        LocalCredentialReplacementOutcome outcome = await fixture.StateStore(request).ReplaceAsync(
            new LocalCredentialReplacementRequest(authority: authority, newPassword: replacement), fixture.CancellationToken);

        await Assert.That(expiry.Observed).IsTrue();
        await Assert.That(expiry.WritesObserved).IsEqualTo((int)boundary);
        await Assert.That(expiry.AffectedRows).IsEqualTo(1);
        await Assert.That(fixture.Clock.GetUtcNow()).IsEqualTo(authority.ExpiresAtUtc);
        await Assert.That(outcome).IsEqualTo(LocalCredentialReplacementOutcome.InvalidChallenge);
        await fixture.AssertUnchangedAsync(before);
        await fixture.AssertStateAsync(LocalCredentialState.ChangeRequired);
        await Assert.That(await fixture.PasswordIsValidAsync(fixture.TemporaryPassword)).IsTrue();
        await Assert.That(await fixture.PasswordIsValidAsync(replacement)).IsFalse();
    }

    [Test]
    [Arguments(IdentityDatabaseTopology.Colocated)]
    [Arguments(IdentityDatabaseTopology.External)]
    public async Task StaleSecurityStampCannotReplaceTheStillCurrentOperation(IdentityDatabaseTopology topology)
    {
        await using Fixture fixture = await Fixture.CreateAsync(topology);
        Snapshot before = await fixture.ReadAsync();
        LocalCredentialReplacementAuthority authority = fixture.Authority(before);
        await using (AsyncServiceScope mutation = fixture.Provider.CreateAsyncScope())
        {
            UserManager<LocalIdentityUser> manager = mutation.ServiceProvider.GetRequiredService<UserManager<LocalIdentityUser>>();
            LocalIdentityUser user = (await manager.FindByIdAsync(fixture.Receipt.LocalSubjectId.ToString("D")))!;
            IdentityResult rotated = await manager.UpdateSecurityStampAsync(user).WaitAsync(fixture.CancellationToken);
            await Assert.That(rotated.Succeeded).IsTrue();
        }
        Snapshot current = await fixture.ReadAsync();
        await Assert.That(string.Equals(current.SecurityStamp, before.SecurityStamp, StringComparison.Ordinal)).IsFalse();
        await Assert.That(current.OperationStamp == before.OperationStamp).IsTrue();
        await Assert.That(string.Equals(current.TokenValue, before.TokenValue, StringComparison.Ordinal)).IsTrue();

        LocalCredentialReplacementOutcome outcome = await fixture.ReplaceAsync(authority: authority, password: NewPassword());

        await Assert.That(outcome).IsEqualTo(LocalCredentialReplacementOutcome.InvalidChallenge);
        await fixture.AssertUnchangedAsync(current);
        await fixture.AssertStateAsync(LocalCredentialState.ChangeRequired);
    }

    [Test]
    [Arguments(IdentityDatabaseTopology.Colocated)]
    [Arguments(IdentityDatabaseTopology.External)]
    public async Task ExpiredAuthorityCannotReplaceCredential(IdentityDatabaseTopology topology)
    {
        await using Fixture fixture = await Fixture.CreateAsync(topology);
        Snapshot before = await fixture.ReadAsync();
        LocalCredentialReplacementAuthority authority = fixture.Authority(before);
        fixture.Clock.Advance(TimeSpan.FromMinutes(6));

        LocalCredentialReplacementOutcome outcome = await fixture.ReplaceAsync(authority: authority, password: NewPassword());

        await Assert.That(outcome).IsEqualTo(LocalCredentialReplacementOutcome.InvalidChallenge);
        await fixture.AssertUnchangedAsync(before);
    }

    [Test]
    [Arguments(IdentityDatabaseTopology.Colocated)]
    [Arguments(IdentityDatabaseTopology.External)]
    public async Task SupersededCurrentOperationCannotBeReplacedUsingEarlierAuthority(IdentityDatabaseTopology topology)
    {
        await using Fixture fixture = await Fixture.CreateAsync(topology);
        LocalCredentialReplacementAuthority authority = fixture.Authority(await fixture.ReadAsync());
        await using (AsyncServiceScope mutation = fixture.Provider.CreateAsyncScope())
        {
            UserManager<LocalIdentityUser> manager = mutation.ServiceProvider.GetRequiredService<UserManager<LocalIdentityUser>>();
            LocalIdentityUser user = (await manager.FindByIdAsync(fixture.Receipt.LocalSubjectId.ToString("D")))!;
            var superseding = new LocalCredentialStateMetadata(version: LocalCredentialStateMetadata.CurrentVersion,
                state: LocalCredentialState.ChangeRequired, operationId: Guid.CreateVersion7(), applicationUserId: user.Id);
            IdentityResult written = await manager.SetAuthenticationTokenAsync(user,
                LocalCredentialStateMetadata.TokenLoginProvider, LocalCredentialStateMetadata.TokenName, JsonSerializer.Serialize(superseding));
            await Assert.That(written.Succeeded).IsTrue();
        }
        Snapshot current = await fixture.ReadAsync();

        LocalCredentialReplacementOutcome outcome = await fixture.ReplaceAsync(authority: authority, password: NewPassword());

        await Assert.That(outcome).IsEqualTo(LocalCredentialReplacementOutcome.InvalidChallenge);
        await fixture.AssertUnchangedAsync(current);
    }

    [Test]
    [Arguments(IdentityDatabaseTopology.Colocated, ReplacementWriteBoundary.AfterFirstWrite)]
    [Arguments(IdentityDatabaseTopology.Colocated, ReplacementWriteBoundary.AfterSecondWrite)]
    [Arguments(IdentityDatabaseTopology.Colocated, ReplacementWriteBoundary.AfterThirdWrite)]
    [Arguments(IdentityDatabaseTopology.External, ReplacementWriteBoundary.AfterFirstWrite)]
    [Arguments(IdentityDatabaseTopology.External, ReplacementWriteBoundary.AfterSecondWrite)]
    [Arguments(IdentityDatabaseTopology.External, ReplacementWriteBoundary.AfterThirdWrite)]
    public async Task FailureAfterExecutedReplacementWriteRollsBackAllCredentialAuthority(
        IdentityDatabaseTopology topology, ReplacementWriteBoundary boundary)
    {
        await using Fixture fixture = await Fixture.CreateAsync(topology);
        Snapshot before = await fixture.ReadAsync();
        await using AsyncServiceScope request = fixture.Provider.CreateAsyncScope();
        fixture.WriteFault.Arm(identity: fixture.Identity(request), boundary: boundary);

        await Assert.ThrowsAsync<InjectedReplacementFailure>(() => fixture.StateStore(request).ReplaceAsync(
            new LocalCredentialReplacementRequest(authority: fixture.Authority(before), newPassword: NewPassword()), fixture.CancellationToken));

        await Assert.That(fixture.WriteFault.Observed).IsTrue();
        await Assert.That(fixture.WriteFault.WritesObserved).IsEqualTo((int)boundary);
        await Assert.That(fixture.WriteFault.AffectedRows).IsEqualTo(1);
        await fixture.AssertUnchangedAsync(before);
        await fixture.AssertStateAsync(LocalCredentialState.ChangeRequired);
        await Assert.That(await fixture.PasswordIsValidAsync(fixture.TemporaryPassword)).IsTrue();
    }

    [Test]
    [Arguments(IdentityDatabaseTopology.Colocated)]
    [Arguments(IdentityDatabaseTopology.External)]
    public async Task ConcurrentReplacementHasOneWinnerAndConsumedAuthorityCannotReplay(IdentityDatabaseTopology topology)
    {
        await using Fixture fixture = await Fixture.CreateAsync(topology);
        Snapshot before = await fixture.ReadAsync();
        LocalCredentialReplacementAuthority authority = fixture.Authority(before);
        string firstPassword = NewPassword();
        string secondPassword = NewPassword();

        LocalCredentialReplacementOutcome[] outcomes = await fixture.ReplaceConcurrentlyAsync(
            authority: authority, firstPassword: firstPassword, secondPassword: secondPassword);

        await Assert.That(outcomes.Count(outcome => outcome == LocalCredentialReplacementOutcome.Replaced)).IsEqualTo(1);
        await Assert.That(outcomes.All(outcome => outcome is LocalCredentialReplacementOutcome.Replaced
            or LocalCredentialReplacementOutcome.InvalidChallenge or LocalCredentialReplacementOutcome.Conflict)).IsTrue();
        string winner = outcomes[0] == LocalCredentialReplacementOutcome.Replaced ? firstPassword : secondPassword;
        string loser = outcomes[0] == LocalCredentialReplacementOutcome.Replaced ? secondPassword : firstPassword;
        await Assert.That(await fixture.PasswordIsValidAsync(winner)).IsTrue();
        await Assert.That(await fixture.PasswordIsValidAsync(loser)).IsFalse();
        await Assert.That(await fixture.PasswordIsValidAsync(fixture.TemporaryPassword)).IsFalse();
        await fixture.AssertStateAsync(LocalCredentialState.Ready);
        Snapshot committed = await fixture.ReadAsync();
        await Assert.That(committed.Stage).IsEqualTo(LocalCredentialOperationStage.Replaced);

        LocalCredentialReplacementOutcome replay = await fixture.ReplaceAsync(authority: authority, password: NewPassword());

        await Assert.That(replay).IsEqualTo(LocalCredentialReplacementOutcome.InvalidChallenge);
        await fixture.AssertUnchangedAsync(committed);
        await Assert.That((await fixture.AuthenticateAsync(winner)).Outcome).IsEqualTo(LocalAuthOutcome.Authenticated);
    }

    [Test]
    [Arguments(IdentityDatabaseTopology.Colocated)]
    [Arguments(IdentityDatabaseTopology.External)]
    public async Task PasswordCheckBeforeConcurrentReplacementCannotAuthorizeReadySession(IdentityDatabaseTopology topology)
    {
        await using Fixture fixture = await Fixture.CreateAsync(topology);
        Snapshot before = await fixture.ReadAsync();
        LocalCredentialReplacementAuthority authority = fixture.Authority(before);
        string replacementPassword = NewPassword();
        Task<LocalAuthResponseDto> authentication = Task.Run(() => fixture.AuthenticateAsync(
            password: fixture.TemporaryPassword, stateReadBarrier: fixture.StateReadBarrier), fixture.CancellationToken);
        LocalCredentialReplacementOutcome replacement;
        LocalAuthResponseDto response;
        try
        {
            Task first = await Task.WhenAny(authentication, fixture.StateReadBarrier.Reached).WaitAsync(fixture.CancellationToken);
            await Assert.That(ReferenceEquals(first, fixture.StateReadBarrier.Reached)).IsTrue();
            replacement = await fixture.ReplaceAsync(authority: authority, password: replacementPassword);
        }
        finally
        {
            fixture.StateReadBarrier.Release();
            response = await authentication.WaitAsync(fixture.CancellationToken);
        }

        await Assert.That(replacement).IsEqualTo(LocalCredentialReplacementOutcome.Replaced);
        await Assert.That(response.Outcome == LocalAuthOutcome.Authenticated).IsFalse();
        await Assert.That(string.IsNullOrEmpty(response.Token)).IsTrue();
        await Assert.That(response.ReplacementChallenge).IsNull();
        await fixture.AssertStateAsync(LocalCredentialState.Ready);
        await Assert.That(await fixture.PasswordIsValidAsync(replacementPassword)).IsTrue();
        await Assert.That(await fixture.PasswordIsValidAsync(fixture.TemporaryPassword)).IsFalse();
    }

    [Test]
    [Arguments(IdentityDatabaseTopology.Colocated, LocalCredentialState.ChangeRequired)]
    [Arguments(IdentityDatabaseTopology.Colocated, LocalCredentialState.Ready)]
    [Arguments(IdentityDatabaseTopology.External, LocalCredentialState.ChangeRequired)]
    [Arguments(IdentityDatabaseTopology.External, LocalCredentialState.Ready)]
    public async Task PasswordCheckBeforeSupervisedResetCannotIssueSessionOrReplacementChallenge(
        IdentityDatabaseTopology topology, LocalCredentialState finalState)
    {
        await using Fixture fixture = await Fixture.CreateAsync(topology);
        Snapshot before = await fixture.ReadAsync();
        var request = new LocalCredentialResetRequest(
            operationId: Guid.CreateVersion7(), initiatingApplicationUserId: fixture.Receipt.InitiatingApplicationUserId,
            localSubjectId: fixture.Receipt.LocalSubjectId, expectedCurrentOperationId: fixture.Receipt.OperationId,
            expectedCurrentOperationConcurrencyStamp: before.OperationStamp, reason: "Supervised credential recovery");
        Task<LocalAuthResponseDto> authentication = Task.Run(() => fixture.AuthenticateAsync(
            password: fixture.TemporaryPassword, stateReadBarrier: fixture.StateReadBarrier), fixture.CancellationToken);
        LocalCredentialResetResult reset;
        string? finalPassword = null;
        LocalAuthResponseDto response;
        try
        {
            Task first = await Task.WhenAny(authentication, fixture.StateReadBarrier.Reached).WaitAsync(fixture.CancellationToken);
            await Assert.That(ReferenceEquals(first, fixture.StateReadBarrier.Reached)).IsTrue();
            await using (AsyncServiceScope scope = fixture.Provider.CreateAsyncScope())
                reset = await fixture.StateStore(scope).ResetAsync(request, fixture.CancellationToken);
            await Assert.That(reset.Outcome).IsEqualTo(LocalCredentialResetOutcome.Reset);
            finalPassword = reset.TemporaryPassword!;
            if (finalState == LocalCredentialState.Ready)
            {
                Snapshot current = await fixture.ReadAsync();
                var authority = new LocalCredentialReplacementAuthority(
                    subject: new LocalCredentialReplacementSubject(localSubjectId: fixture.Receipt.LocalSubjectId,
                        operationId: request.OperationId, securityStamp: current.SecurityStamp!),
                    issuedAtUtc: fixture.Clock.GetUtcNow(), expiresAtUtc: fixture.Clock.GetUtcNow().AddMinutes(5));
                finalPassword = NewPassword();
                await Assert.That(await fixture.ReplaceAsync(authority: authority, password: finalPassword))
                    .IsEqualTo(LocalCredentialReplacementOutcome.Replaced);
            }
        }
        finally
        {
            fixture.StateReadBarrier.Release();
            response = await authentication.WaitAsync(fixture.CancellationToken);
        }

        await Assert.That(reset.Outcome).IsEqualTo(LocalCredentialResetOutcome.Reset);
        await Assert.That(response.Outcome == LocalAuthOutcome.Authenticated).IsFalse();
        await Assert.That(string.IsNullOrEmpty(response.Token)).IsTrue();
        await Assert.That(response.ReplacementChallenge).IsNull();
        Snapshot committed = await fixture.ReadAsync();
        LocalCredentialStateMetadata metadata = JsonSerializer.Deserialize<LocalCredentialStateMetadata>(committed.TokenValue!)!;
        await Assert.That(metadata.OperationId).IsEqualTo(request.OperationId);
        await Assert.That(metadata.State).IsEqualTo(finalState);
        await Assert.That(committed.Stage).IsEqualTo(LocalCredentialOperationStage.Superseded);
        await Assert.That(await fixture.PasswordIsValidAsync(finalPassword!)).IsTrue();
        if (finalState == LocalCredentialState.Ready)
            await Assert.That(await fixture.PasswordIsValidAsync(reset.TemporaryPassword!)).IsFalse();
        await Assert.That(await fixture.PasswordIsValidAsync(fixture.TemporaryPassword)).IsFalse();
    }

    [Test]
    [Arguments(IdentityDatabaseTopology.Colocated, SessionMutation.None)]
    [Arguments(IdentityDatabaseTopology.Colocated, SessionMutation.Reset)]
    [Arguments(IdentityDatabaseTopology.Colocated, SessionMutation.SecurityStamp)]
    [Arguments(IdentityDatabaseTopology.Colocated, SessionMutation.MissingState)]
    [Arguments(IdentityDatabaseTopology.Colocated, SessionMutation.PendingState)]
    [Arguments(IdentityDatabaseTopology.Colocated, SessionMutation.MissingBinding)]
    [Arguments(IdentityDatabaseTopology.Colocated, SessionMutation.VerificationRequired)]
    [Arguments(IdentityDatabaseTopology.Colocated, SessionMutation.UnverifiedWithDeliveryOff)]
    [Arguments(IdentityDatabaseTopology.Colocated, SessionMutation.VerificationRevoked)]
    [Arguments(IdentityDatabaseTopology.Colocated, SessionMutation.MissingHash)]
    [Arguments(IdentityDatabaseTopology.Colocated, SessionMutation.MissingOperation)]
    [Arguments(IdentityDatabaseTopology.Colocated, SessionMutation.NonReplacedReceipt)]
    [Arguments(IdentityDatabaseTopology.Colocated, SessionMutation.SuspendedActor)]
    [Arguments(IdentityDatabaseTopology.Colocated, SessionMutation.VerificationGranted)]
    [Arguments(IdentityDatabaseTopology.Colocated, SessionMutation.MalformedVerificationPolicy)]
    [Arguments(IdentityDatabaseTopology.External, SessionMutation.None)]
    [Arguments(IdentityDatabaseTopology.External, SessionMutation.Reset)]
    [Arguments(IdentityDatabaseTopology.External, SessionMutation.SecurityStamp)]
    [Arguments(IdentityDatabaseTopology.External, SessionMutation.MissingState)]
    [Arguments(IdentityDatabaseTopology.External, SessionMutation.PendingState)]
    [Arguments(IdentityDatabaseTopology.External, SessionMutation.MissingBinding)]
    [Arguments(IdentityDatabaseTopology.External, SessionMutation.VerificationRequired)]
    [Arguments(IdentityDatabaseTopology.External, SessionMutation.UnverifiedWithDeliveryOff)]
    [Arguments(IdentityDatabaseTopology.External, SessionMutation.VerificationRevoked)]
    [Arguments(IdentityDatabaseTopology.External, SessionMutation.MissingHash)]
    [Arguments(IdentityDatabaseTopology.External, SessionMutation.MissingOperation)]
    [Arguments(IdentityDatabaseTopology.External, SessionMutation.NonReplacedReceipt)]
    [Arguments(IdentityDatabaseTopology.External, SessionMutation.SuspendedActor)]
    [Arguments(IdentityDatabaseTopology.External, SessionMutation.VerificationGranted)]
    [Arguments(IdentityDatabaseTopology.External, SessionMutation.MalformedVerificationPolicy)]
    public async Task SignedSessionRequiresCurrentReadyCredentialBindingAndInstanceVerification(
        IdentityDatabaseTopology topology, SessionMutation mutation)
    {
        await using Fixture fixture = await Fixture.CreateAsync(topology);
        LocalSessionAuthority authority = await fixture.IssueReadySessionAsync(
            emailConfirmed: mutation is not (SessionMutation.UnverifiedWithDeliveryOff or SessionMutation.VerificationRequired
                or SessionMutation.VerificationGranted or SessionMutation.MalformedVerificationPolicy));
        await using (AsyncServiceScope scope = fixture.Provider.CreateAsyncScope())
        {
            DbContext identity = fixture.Identity(scope);
            var application = scope.ServiceProvider.GetRequiredService<ExploreDbContext>();
            switch (mutation)
            {
                case SessionMutation.None:
                case SessionMutation.UnverifiedWithDeliveryOff: break;
                case SessionMutation.Reset:
                    Snapshot current = await fixture.ReadAsync();
                    LocalCredentialResetResult reset = await fixture.StateStore(scope).ResetAsync(new LocalCredentialResetRequest(
                        operationId: Guid.CreateVersion7(), initiatingApplicationUserId: fixture.Receipt.InitiatingApplicationUserId,
                        localSubjectId: fixture.Receipt.LocalSubjectId, expectedCurrentOperationId: fixture.Receipt.OperationId,
                        expectedCurrentOperationConcurrencyStamp: current.OperationStamp, reason: "Supervised session revocation"), fixture.CancellationToken);
                    await Assert.That(reset.Outcome).IsEqualTo(LocalCredentialResetOutcome.Reset);
                    break;
                case SessionMutation.SecurityStamp:
                    var manager = scope.ServiceProvider.GetRequiredService<UserManager<LocalIdentityUser>>();
                    LocalIdentityUser user = (await manager.FindByIdAsync(authority.LocalSubjectId.ToString("D")))!;
                    await Assert.That((await manager.UpdateSecurityStampAsync(user)).Succeeded).IsTrue();
                    break;
                case SessionMutation.MissingState:
                    await identity.Set<IdentityUserToken<Guid>>().Where(row => row.UserId == authority.LocalSubjectId
                        && row.LoginProvider == LocalCredentialStateMetadata.TokenLoginProvider && row.Name == LocalCredentialStateMetadata.TokenName)
                        .ExecuteDeleteAsync(fixture.CancellationToken);
                    break;
                case SessionMutation.PendingState:
                    string pending = JsonSerializer.Serialize(new LocalCredentialStateMetadata(
                        version: LocalCredentialStateMetadata.CurrentVersion, state: LocalCredentialState.ProvisioningPending,
                        operationId: fixture.Receipt.OperationId, applicationUserId: authority.LocalSubjectId));
                    await identity.Set<IdentityUserToken<Guid>>().Where(row => row.UserId == authority.LocalSubjectId
                        && row.LoginProvider == LocalCredentialStateMetadata.TokenLoginProvider && row.Name == LocalCredentialStateMetadata.TokenName)
                        .ExecuteUpdateAsync(setters => setters.SetProperty(row => row.Value, pending), fixture.CancellationToken);
                    break;
                case SessionMutation.MissingBinding:
                    await application.UserExternalLogins.Where(row => row.Id == fixture.Receipt.ExternalLoginId)
                        .ExecuteDeleteAsync(fixture.CancellationToken);
                    break;
                case SessionMutation.VerificationRequired:
                case SessionMutation.MalformedVerificationPolicy:
                    application.SystemSettings.Add(new SystemSetting
                    {
                        Id = Guid.CreateVersion7(), SettingKey = GovernanceSettingKeys.Email.DeliveryEnabled,
                        Value = mutation == SessionMutation.MalformedVerificationPolicy ? "not-a-boolean" : "true",
                        ValueType = SettingValueType.Boolean, CreatedAt = fixture.Clock.GetUtcNow().UtcDateTime
                    });
                    await application.SaveChangesAsync(fixture.CancellationToken);
                    break;
                case SessionMutation.VerificationGranted:
                    await identity.Set<LocalIdentityUser>().Where(row => row.Id == authority.LocalSubjectId)
                        .ExecuteUpdateAsync(setters => setters.SetProperty(row => row.EmailConfirmed, true), fixture.CancellationToken);
                    await application.Users.Where(row => row.Id == authority.LocalSubjectId)
                        .ExecuteUpdateAsync(setters => setters.SetProperty(row => row.EmailVerified, true), fixture.CancellationToken);
                    break;
                case SessionMutation.VerificationRevoked:
                    await identity.Set<LocalIdentityUser>().Where(row => row.Id == authority.LocalSubjectId)
                        .ExecuteUpdateAsync(setters => setters.SetProperty(row => row.EmailConfirmed, false), fixture.CancellationToken);
                    await application.Users.Where(row => row.Id == authority.LocalSubjectId)
                        .ExecuteUpdateAsync(setters => setters.SetProperty(row => row.EmailVerified, false), fixture.CancellationToken);
                    break;
                case SessionMutation.MissingHash:
                    await identity.Set<LocalIdentityUser>().Where(row => row.Id == authority.LocalSubjectId)
                        .ExecuteUpdateAsync(setters => setters.SetProperty(row => row.PasswordHash, (string?)null), fixture.CancellationToken);
                    break;
                case SessionMutation.MissingOperation:
                    await identity.Set<LocalIdentityCredentialOperation>().Where(row => row.Id == fixture.Receipt.OperationId)
                        .ExecuteDeleteAsync(fixture.CancellationToken);
                    break;
                case SessionMutation.NonReplacedReceipt:
                    await identity.Set<LocalIdentityCredentialOperation>().Where(row => row.Id == fixture.Receipt.OperationId)
                        .ExecuteUpdateAsync(setters => setters.SetProperty(row => row.Stage, LocalCredentialOperationStage.ChangeRequired), fixture.CancellationToken);
                    break;
                case SessionMutation.SuspendedActor:
                    await application.Actors.Where(row => row.Id == fixture.Receipt.PersonalActorId)
                        .ExecuteUpdateAsync(setters => setters.SetProperty(row => row.IsSuspended, true), fixture.CancellationToken);
                    break;
                default: throw new InvalidOperationException("Unsupported session mutation.");
            }
        }
        await using AsyncServiceScope validation = fixture.Provider.CreateAsyncScope();
        LocalSessionValidationOutcome outcome = await fixture.Authentication(validation).ValidateSessionAsync(authority, fixture.CancellationToken);
        LocalSessionValidationOutcome expected = mutation switch
        {
            SessionMutation.None or SessionMutation.UnverifiedWithDeliveryOff => LocalSessionValidationOutcome.Valid,
            SessionMutation.MalformedVerificationPolicy => LocalSessionValidationOutcome.Unavailable,
            _ => LocalSessionValidationOutcome.Invalid
        };
        await Assert.That(outcome).IsEqualTo(expected);
    }

    [Test]
    [Arguments(IdentityDatabaseTopology.Colocated)]
    [Arguments(IdentityDatabaseTopology.External)]
    public async Task PretrackedReadyRowsCannotHideAnIndependentlyCommittedResetFromSessionValidation(IdentityDatabaseTopology topology)
    {
        await using Fixture fixture = await Fixture.CreateAsync(topology);
        LocalSessionAuthority authority = await fixture.IssueReadySessionAsync();
        await using AsyncServiceScope stale = fixture.Provider.CreateAsyncScope();
        DbContext identity = fixture.Identity(stale);
        await identity.Set<LocalIdentityUser>().SingleAsync(row => row.Id == authority.LocalSubjectId, fixture.CancellationToken);
        await identity.Set<IdentityUserToken<Guid>>().SingleAsync(row => row.UserId == authority.LocalSubjectId
            && row.LoginProvider == LocalCredentialStateMetadata.TokenLoginProvider && row.Name == LocalCredentialStateMetadata.TokenName,
            fixture.CancellationToken);
        await identity.Set<LocalIdentityCredentialOperation>().SingleAsync(row => row.Id == fixture.Receipt.OperationId, fixture.CancellationToken);
        LocalIdentityAuthService authentication = fixture.Authentication(stale);
        await Assert.That(await authentication.ValidateSessionAsync(authority, fixture.CancellationToken)).IsEqualTo(LocalSessionValidationOutcome.Valid);
        Snapshot before = await fixture.ReadAsync();
        await using (AsyncServiceScope reset = fixture.Provider.CreateAsyncScope())
        {
            LocalCredentialResetResult result = await fixture.StateStore(reset).ResetAsync(new LocalCredentialResetRequest(
                operationId: Guid.CreateVersion7(), initiatingApplicationUserId: fixture.Receipt.InitiatingApplicationUserId,
                localSubjectId: authority.LocalSubjectId, expectedCurrentOperationId: fixture.Receipt.OperationId,
                expectedCurrentOperationConcurrencyStamp: before.OperationStamp, reason: "Supervised freshness check"), fixture.CancellationToken);
            await Assert.That(result.Outcome).IsEqualTo(LocalCredentialResetOutcome.Reset);
        }
        await Assert.That(await authentication.ValidateSessionAsync(authority, fixture.CancellationToken)).IsEqualTo(LocalSessionValidationOutcome.Invalid);
    }

    [Test]
    [Arguments(IdentityDatabaseTopology.Colocated)]
    [Arguments(IdentityDatabaseTopology.External)]
    public async Task SessionValidationDatabaseFailureCannotReturnValidAuthority(IdentityDatabaseTopology topology)
    {
        await using Fixture fixture = await Fixture.CreateAsync(topology);
        LocalSessionAuthority authority = await fixture.IssueReadySessionAsync();
        await using (AsyncServiceScope broken = fixture.Provider.CreateAsyncScope())
        {
            DbContext identity = fixture.Identity(broken);
            await identity.Database.ExecuteSqlRawAsync("DROP TABLE \"ie_identity_user_tokens\"", fixture.CancellationToken);
        }
        await using AsyncServiceScope validation = fixture.Provider.CreateAsyncScope();
        await Assert.That(await fixture.Authentication(validation).ValidateSessionAsync(authority, fixture.CancellationToken))
            .IsEqualTo(LocalSessionValidationOutcome.Unavailable);
    }

    [Test]
    [Arguments(IdentityDatabaseTopology.Colocated)]
    [Arguments(IdentityDatabaseTopology.External)]
    public async Task SessionValidationCancellationAtNativeStateReadCannotBecomeAuthority(IdentityDatabaseTopology topology)
    {
        await using Fixture fixture = await Fixture.CreateAsync(topology);
        LocalSessionAuthority authority = await fixture.IssueReadySessionAsync();
        await using AsyncServiceScope scope = fixture.Provider.CreateAsyncScope();
        using var cancellation = CancellationTokenSource.CreateLinkedTokenSource(fixture.CancellationToken);
        fixture.StateReadBarrier.Arm(fixture.Identity(scope));
        Task<LocalSessionValidationOutcome> validation = Task.Run(() => fixture.Authentication(scope)
            .ValidateSessionAsync(authority, cancellation.Token), fixture.CancellationToken);
        try
        {
            Task first = await Task.WhenAny(validation, fixture.StateReadBarrier.Reached).WaitAsync(fixture.CancellationToken);
            await Assert.That(ReferenceEquals(first, fixture.StateReadBarrier.Reached)).IsTrue();
            await cancellation.CancelAsync();
            await Assert.ThrowsAsync<OperationCanceledException>(() => validation);
        }
        finally
        {
            await cancellation.CancelAsync();
            fixture.StateReadBarrier.Release();
            try { await validation; } catch (OperationCanceledException) { }
        }
    }

    private static string NewPassword() => $"Aa1!{Convert.ToHexString(RandomNumberGenerator.GetBytes(24))}";

    internal sealed record Snapshot(string? PasswordHash, string? SecurityStamp, string? ConcurrencyStamp,
        string? TokenValue, LocalCredentialOperationStage Stage, Guid OperationStamp, DateTime? OperationUpdatedAt, DateTime? UserUpdatedAt)
    {
        public override string ToString() => nameof(Snapshot);
    }

    internal sealed class Fixture : IAsyncDisposable
    {
        private readonly string _applicationPath = Path.Combine(Path.GetTempPath(), $"first-use-app-{Guid.CreateVersion7():N}.db");
        private readonly string _identityPath = Path.Combine(Path.GetTempPath(), $"first-use-identity-{Guid.CreateVersion7():N}.db");
        private readonly CancellationTokenSource _timeout = new(TimeSpan.FromSeconds(30));
        private readonly MemoryCache _metadataCache = new(new MemoryCacheOptions());
        private readonly byte[] _signingKey = RandomNumberGenerator.GetBytes(64);
        private ServiceProvider? _provider;
        private IdentityDatabaseTopology _topology;
        private IInterceptor? _transactionObserver;
        private string _email = string.Empty;
        internal ServiceProvider Provider => _provider!;
        internal CancellationToken CancellationToken => _timeout.Token;
        internal LocalCredentialOperationReceipt Receipt { get; private set; } = null!;
        internal string TemporaryPassword { get; private set; } = string.Empty;
        internal ControlledClock Clock { get; } = new();
        internal ReplacementWriteFault WriteFault { get; } = new();
        internal AuthenticationStateReadBarrier StateReadBarrier { get; } = new();
        internal ExpiryAdvancingPasswordValidator ValidationExpiry { get; }

        private Fixture() => ValidationExpiry = new ExpiryAdvancingPasswordValidator(Clock);

        internal static async Task<Fixture> CreateAsync(IdentityDatabaseTopology topology, IInterceptor? transactionObserver = null)
        {
            await LocalIdentitySqliteTemplate.InitializeAsync();
            var fixture = new Fixture { _topology = topology, _transactionObserver = transactionObserver };
            try { await fixture.InitializeAsync(); return fixture; }
            catch { await fixture.DisposeAsync(); throw; }
        }

        internal DbContext Identity(AsyncServiceScope scope) => _topology == IdentityDatabaseTopology.External
            ? scope.ServiceProvider.GetRequiredService<ExternalIdentityDbContext>()
            : scope.ServiceProvider.GetRequiredService<ExploreDbContext>();

        internal LocalIdentityCredentialStateStore StateStore(AsyncServiceScope scope) => new(
            identityDbContext: Identity(scope), applicationDbContext: scope.ServiceProvider.GetRequiredService<ExploreDbContext>(),
            userManager: scope.ServiceProvider.GetRequiredService<UserManager<LocalIdentityUser>>(), timeProvider: Clock);

        // Store tests receive nominal authenticated authority; only the issuance test proves JWT cryptography.
        internal LocalCredentialReplacementAuthority Authority(Snapshot snapshot) => new(
            subject: new LocalCredentialReplacementSubject(localSubjectId: Receipt.LocalSubjectId,
                operationId: Receipt.OperationId, securityStamp: snapshot.SecurityStamp!),
            issuedAtUtc: Clock.GetUtcNow(), expiresAtUtc: Clock.GetUtcNow().AddMinutes(5));

        internal TokenValidationParameters ValidationParameters(string audience) => new()
        {
            ValidateIssuerSigningKey = true, IssuerSigningKey = new SymmetricSecurityKey(_signingKey),
            ValidateIssuer = true, ValidIssuer = LocalIdentityOptions.Issuer,
            ValidateAudience = true, ValidAudience = audience, ValidateLifetime = true,
            RequireExpirationTime = true, RequireSignedTokens = true, ClockSkew = TimeSpan.Zero,
            ValidAlgorithms = [SecurityAlgorithms.HmacSha256]
        };

        internal async Task<LocalAuthResponseDto> AuthenticateAsync(
            string password, AuthenticationStateReadBarrier? stateReadBarrier = null)
        {
            await using AsyncServiceScope scope = Provider.CreateAsyncScope();
            LocalIdentityAuthService authentication = Authentication(scope);
            stateReadBarrier?.Arm(Identity(scope));
            return await authentication.AuthenticateAsync(new LocalAuthRequestDto(Identifier: _email, Password: password), CancellationToken);
        }

        internal LocalIdentityAuthService Authentication(AsyncServiceScope scope)
        {
            var application = scope.ServiceProvider.GetRequiredService<ExploreDbContext>();
            var settings = new SystemSettingRepository(dbContext: application,
                mutationLock: new RelationalSettingMutationLock(dbContext: application, unitOfWork: new EfCoreUnitOfWork(application)));
            var secrets = Substitute.For<ISecretResolver>();
            secrets.ResolveAsync(SecretDefinitionRegistry.Keys.Authentication.LocalJwtKey, null, Arg.Any<CancellationToken>())
                .Returns(SecretResolutionResult.Resolved(new ResolvedSecret(
                    SettingKey: SecretDefinitionRegistry.Keys.Authentication.LocalJwtKey, Value: Convert.ToBase64String(_signingKey),
                    Source: SecretSourceType.EnvironmentVariable, Scope: SecretScope.Instance, ScopeId: null, ResolvedAt: Clock.GetUtcNow())));
            var signer = new LocalJwtTokenGenerator(secretResolver: secrets,
                options: Options.Create(new LocalIdentityOptions()), timeProvider: Clock);
            return new LocalIdentityAuthService(
                userManager: scope.ServiceProvider.GetRequiredService<UserManager<LocalIdentityUser>>(), tokenGenerator: signer,
                systemSettings: settings, credentialStates: StateStore(scope));
        }

        internal async Task<LocalSessionAuthority> IssueReadySessionAsync(bool emailConfirmed = true)
        {
            string password = NewPassword();
            await Assert.That(await ReplaceAsync(authority: Authority(await ReadAsync()), password: password))
                .IsEqualTo(LocalCredentialReplacementOutcome.Replaced);
            if (!emailConfirmed)
            {
                await using AsyncServiceScope scope = Provider.CreateAsyncScope();
                await Identity(scope).Set<LocalIdentityUser>().Where(user => user.Id == Receipt.LocalSubjectId)
                    .ExecuteUpdateAsync(setters => setters.SetProperty(user => user.EmailConfirmed, false), CancellationToken);
                await scope.ServiceProvider.GetRequiredService<ExploreDbContext>().Users.Where(user => user.Id == Receipt.LocalSubjectId)
                    .ExecuteUpdateAsync(setters => setters.SetProperty(user => user.EmailVerified, false), CancellationToken);
            }
            LocalAuthResponseDto login = await AuthenticateAsync(password);
            await Assert.That(login.Outcome).IsEqualTo(LocalAuthOutcome.Authenticated);
            var handler = new JwtSecurityTokenHandler { MapInboundClaims = false };
            ClaimsPrincipal principal = handler.ValidateToken(login.Token!, ValidationParameters(LocalIdentityOptions.Audience), out _);
            return new LocalSessionAuthority(localSubjectId: Guid.Parse(principal.FindFirst(JwtRegisteredClaimNames.Sub)!.Value),
                securityStamp: principal.FindFirst(LocalSessionToken.SecurityStampClaim)!.Value,
                emailVerified: bool.Parse(principal.FindFirst("email_verified")!.Value));
        }

        internal async Task<bool> PasswordIsValidAsync(string password)
        {
            await using AsyncServiceScope scope = Provider.CreateAsyncScope();
            var manager = scope.ServiceProvider.GetRequiredService<UserManager<LocalIdentityUser>>();
            LocalIdentityUser user = (await manager.FindByIdAsync(Receipt.LocalSubjectId.ToString("D")))!;
            return await manager.CheckPasswordAsync(user, password).WaitAsync(CancellationToken);
        }

        internal async Task<LocalCredentialReplacementOutcome> ReplaceAsync(LocalCredentialReplacementAuthority authority, string password)
        {
            await using AsyncServiceScope scope = Provider.CreateAsyncScope();
            return await StateStore(scope).ReplaceAsync(new LocalCredentialReplacementRequest(authority: authority, newPassword: password), CancellationToken);
        }

        internal async Task<LocalCredentialReplacementOutcome[]> ReplaceConcurrentlyAsync(
            LocalCredentialReplacementAuthority authority, string firstPassword, string secondPassword)
        {
            var start = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
            var firstReady = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
            var secondReady = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
            Task<LocalCredentialReplacementOutcome> Run(string password, TaskCompletionSource ready) => Task.Run(async () =>
            {
                await using AsyncServiceScope scope = Provider.CreateAsyncScope();
                LocalIdentityCredentialStateStore store = StateStore(scope);
                ready.SetResult();
                await start.Task.WaitAsync(CancellationToken);
                return await store.ReplaceAsync(new LocalCredentialReplacementRequest(authority: authority, newPassword: password), CancellationToken);
            }, CancellationToken);
            Task<LocalCredentialReplacementOutcome> first = Run(firstPassword, firstReady);
            Task<LocalCredentialReplacementOutcome> second = Run(secondPassword, secondReady);
            await Task.WhenAll(firstReady.Task, secondReady.Task).WaitAsync(CancellationToken);
            start.SetResult();
            return await Task.WhenAll(first, second).WaitAsync(CancellationToken);
        }

        internal async Task<Snapshot> ReadAsync()
        {
            await using AsyncServiceScope scope = Provider.CreateAsyncScope();
            DbContext identity = Identity(scope);
            LocalIdentityUser user = await identity.Set<LocalIdentityUser>().AsNoTracking()
                .SingleAsync(row => row.Id == Receipt.LocalSubjectId, CancellationToken);
            LocalIdentityCredentialOperation operation = await identity.Set<LocalIdentityCredentialOperation>().AsNoTracking()
                .SingleAsync(row => row.Id == Receipt.OperationId, CancellationToken);
            string? token = await identity.Set<IdentityUserToken<Guid>>().AsNoTracking()
                .Where(row => row.UserId == user.Id && row.LoginProvider == LocalCredentialStateMetadata.TokenLoginProvider
                    && row.Name == LocalCredentialStateMetadata.TokenName).Select(row => row.Value).SingleAsync(CancellationToken);
            return new Snapshot(PasswordHash: user.PasswordHash, SecurityStamp: user.SecurityStamp, ConcurrencyStamp: user.ConcurrencyStamp,
                TokenValue: token, Stage: operation.Stage, OperationStamp: operation.ConcurrencyStamp,
                OperationUpdatedAt: operation.UpdatedAt, UserUpdatedAt: user.UpdatedAt);
        }

        internal async Task AssertUnchangedAsync(Snapshot before)
        {
            Snapshot after = await ReadAsync();
            await Assert.That(string.Equals(after.PasswordHash, before.PasswordHash, StringComparison.Ordinal)).IsTrue();
            await Assert.That(string.Equals(after.SecurityStamp, before.SecurityStamp, StringComparison.Ordinal)).IsTrue();
            await Assert.That(string.Equals(after.ConcurrencyStamp, before.ConcurrencyStamp, StringComparison.Ordinal)).IsTrue();
            await Assert.That(string.Equals(after.TokenValue, before.TokenValue, StringComparison.Ordinal)).IsTrue();
            await Assert.That(after.Stage).IsEqualTo(before.Stage);
            await Assert.That(after.OperationStamp == before.OperationStamp).IsTrue();
            await Assert.That(after.OperationUpdatedAt).IsEqualTo(before.OperationUpdatedAt);
            await Assert.That(after.UserUpdatedAt).IsEqualTo(before.UserUpdatedAt);
        }

        internal async Task AssertStateAsync(LocalCredentialState state)
        {
            Snapshot current = await ReadAsync();
            await using AsyncServiceScope scope = Provider.CreateAsyncScope();
            LocalCredentialStateMetadata? metadata = await StateStore(scope).ReadAsync(
                localSubjectId: Receipt.LocalSubjectId, expectedSecurityStamp: current.SecurityStamp!, cancellationToken: CancellationToken);
            await Assert.That(metadata).IsNotNull();
            await Assert.That(metadata!.State).IsEqualTo(state);
            await Assert.That(metadata.OperationId).IsEqualTo(Receipt.OperationId);
            await Assert.That(metadata.ApplicationUserId).IsEqualTo(Receipt.LocalSubjectId);
        }

        private async Task InitializeAsync()
        {
            var services = new ServiceCollection();
            services.AddLogging();
            services.AddDbContext<ExploreDbContext>(options => Configure(options, _applicationPath));
            services.AddSingleton<Microsoft.AspNetCore.DataProtection.IDataProtectionProvider>(
                new Microsoft.AspNetCore.DataProtection.EphemeralDataProtectionProvider());
            IdentityBuilder identity = services.AddIdentityCore<LocalIdentityUser>().AddRoles<LocalIdentityRole>()
                .AddUserValidator<OptionalEmailLocalIdentityUserValidator>()
                .AddLocalLifecycleTokenProviders();
            services.AddSingleton<IPasswordValidator<LocalIdentityUser>>(ValidationExpiry);
            if (_topology == IdentityDatabaseTopology.External)
            {
                services.AddDbContext<ExternalIdentityDbContext>(options => Configure(options, _identityPath));
                identity.AddEntityFrameworkStores<ExternalIdentityDbContext>();
            }
            else identity.AddEntityFrameworkStores<ExploreDbContext>();
            _provider = services.BuildIsolatedServiceProvider();
            await LocalIdentitySqliteTemplate.CopyAsync(_applicationPath,
                _topology == IdentityDatabaseTopology.External ? _identityPath : null, seedLookups: true, CancellationToken);
            Guid initiatorId = Guid.CreateVersion7();
            await using (AsyncServiceScope seed = Provider.CreateAsyncScope())
            {
                var application = seed.ServiceProvider.GetRequiredService<ExploreDbContext>();
                application.Users.Add(new User
                {
                    Id = initiatorId, EmailVerified = true, CreatedAt = Clock.GetUtcNow().UtcDateTime,
                    Pii = new UserPii { Email = $"initiator-{initiatorId:N}@example.test", FirstName = "Instance", LastName = "Administrator" }
                });
                await application.SaveChangesAsync(CancellationToken);
            }
            _email = $"first-use-{Guid.CreateVersion7():N}@example.test";
            await using (AsyncServiceScope create = Provider.CreateAsyncScope())
            {
                LocalCredentialCreateResult result = await StateStore(create).CreatePendingAsync(new LocalCredentialCreateRequest(
                    operationId: Guid.CreateVersion7(), initiatingApplicationUserId: initiatorId,
                    email: _email, firstName: "Credential", lastName: "Owner"), CancellationToken);
                await Assert.That(result.Outcome).IsEqualTo(LocalCredentialCreateOutcome.Created);
                Receipt = result.Receipt!;
                TemporaryPassword = result.TemporaryPassword!;
            }
            await using (AsyncServiceScope bind = Provider.CreateAsyncScope())
            {
                var application = bind.ServiceProvider.GetRequiredService<ExploreDbContext>();
                var user = new User
                {
                    Id = Receipt.LocalSubjectId, EmailVerified = true, CreatedAt = Clock.GetUtcNow().UtcDateTime,
                    Pii = new UserPii { Email = _email, FirstName = "Credential", LastName = "Owner" }
                };
                application.Actors.Add(new Actor
                {
                    Id = Receipt.PersonalActorId, UserId = user.Id, User = user, ActorTypeId = (int)ActorTypeEnum.User, ActorType = null!,
                    Pii = new ActorPii { DisplayName = "Credential Owner" }, CreatedAt = Clock.GetUtcNow().UtcDateTime
                });
                application.UserExternalLogins.Add(new UserExternalLogin
                {
                    Id = Receipt.ExternalLoginId, UserId = user.Id, User = user,
                    AuthenticationProviderId = (int)AuthenticationProviderKind.Local, AuthenticationProvider = null!,
                    ProviderKey = user.Id.ToString("D"), CreatedAt = Clock.GetUtcNow().UtcDateTime
                });
                await application.SaveChangesAsync(CancellationToken);
            }
            await using (AsyncServiceScope activate = Provider.CreateAsyncScope())
            {
                LocalCredentialProvisioningSnapshot snapshot = (await StateStore(activate).ReadProvisioningAsync(Receipt.OperationId, CancellationToken))!;
                LocalCredentialActivationOutcome result = await StateStore(activate).ActivateChangeRequiredAsync(
                    new LocalCredentialActivationRequest(operationId: Receipt.OperationId,
                        expectedOperationConcurrencyStamp: snapshot.OperationConcurrencyStamp), CancellationToken);
                await Assert.That(result).IsEqualTo(LocalCredentialActivationOutcome.Activated);
                await Assert.That(await activate.ServiceProvider.GetRequiredService<ExploreDbContext>().LocalIdentityUsers.CountAsync(CancellationToken))
                    .IsEqualTo(_topology == IdentityDatabaseTopology.Colocated ? 1 : 0);
            }
        }

        private void Configure(DbContextOptionsBuilder options, string path) => options
            .UseSqlite(new SqliteConnectionStringBuilder { DataSource = path, Pooling = false }.ToString())
            .UseMemoryCache(_metadataCache)
            .UseSnakeCaseNamingConvention().AddInterceptors(_transactionObserver is null ? [] : new[] { _transactionObserver })
            .AddInterceptors(SqliteNamedLockTransactionInterceptor.Instance, WriteFault, StateReadBarrier);

        public async ValueTask DisposeAsync()
        {
            try { if (_provider is not null) await _provider.DisposeAsync(); }
            finally
            {
                _metadataCache.Dispose();
                foreach (string path in new[] { _applicationPath, _identityPath })
                {
                    File.Delete(path); File.Delete(path + "-wal"); File.Delete(path + "-shm");
                }
                _timeout.Dispose();
            }
        }
    }

    internal sealed class ControlledClock : TimeProvider
    {
        private DateTimeOffset _utcNow = DateTimeOffset.UtcNow;
        public override DateTimeOffset GetUtcNow() => _utcNow;
        internal void Advance(TimeSpan duration) => _utcNow = _utcNow.Add(duration);
    }

    private sealed class InjectedReplacementFailure : Exception;

    internal sealed class AuthenticationStateReadBarrier : DbCommandInterceptor
    {
        private readonly TaskCompletionSource _reached = new(TaskCreationOptions.RunContinuationsAsynchronously);
        private readonly TaskCompletionSource _released = new(TaskCreationOptions.RunContinuationsAsynchronously);
        private DbContext? _identity;
        private string? _tokenTable;
        internal Task Reached => _reached.Task;

        internal void Arm(DbContext identity)
        {
            _identity = identity;
            _tokenTable = identity.Model.FindEntityType(typeof(IdentityUserToken<Guid>))!.GetTableName()!;
        }

        internal void Release() => _released.TrySetResult();

        public override async ValueTask<InterceptionResult<DbDataReader>> ReaderExecutingAsync(
            DbCommand command, CommandEventData eventData, InterceptionResult<DbDataReader> result,
            CancellationToken cancellationToken = default)
        {
            if (ReferenceEquals(eventData.Context, _identity) && _tokenTable is not null
                && command.CommandText.TrimStart().StartsWith("SELECT ", StringComparison.OrdinalIgnoreCase)
                && command.CommandText.Contains(_tokenTable, StringComparison.Ordinal))
            {
                _identity = null;
                _reached.TrySetResult();
                await _released.Task.WaitAsync(cancellationToken);
            }
            return result;
        }
    }

    internal sealed class ExpiryAdvancingPasswordValidator(ControlledClock clock) : IPasswordValidator<LocalIdentityUser>
    {
        internal bool Enabled { get; set; }
        internal bool Observed { get; private set; }

        public Task<IdentityResult> ValidateAsync(UserManager<LocalIdentityUser> manager, LocalIdentityUser user, string? password)
        {
            if (Enabled)
            {
                Enabled = false;
                Observed = true;
                clock.Advance(TimeSpan.FromMinutes(6));
            }
            return Task.FromResult(IdentityResult.Success);
        }
    }

    private sealed class ReplacementWriteExpiry : DbCommandInterceptor
    {
        private DbContext? _identity;
        private ReplacementWriteBoundary _boundary;
        private ControlledClock? _clock;
        private DateTimeOffset _expiresAt;
        internal bool Observed { get; private set; }
        internal int AffectedRows { get; private set; }
        internal int WritesObserved { get; private set; }

        internal void Arm(DbContext identity, ReplacementWriteBoundary boundary, ControlledClock clock, DateTimeOffset expiresAt)
        {
            _identity = identity;
            _boundary = boundary;
            _clock = clock;
            _expiresAt = expiresAt;
        }

        public override ValueTask<int> NonQueryExecutedAsync(DbCommand command, CommandExecutedEventData eventData,
            int result, CancellationToken cancellationToken = default)
        {
            if (ReferenceEquals(eventData.Context, _identity) && _identity?.Database.CurrentTransaction is not null
                && command.CommandText.TrimStart().StartsWith("UPDATE ", StringComparison.OrdinalIgnoreCase))
            {
                WritesObserved++;
                if (WritesObserved == (int)_boundary)
                {
                    _identity = null;
                    Observed = true;
                    AffectedRows = result;
                    _clock!.Advance(_expiresAt - _clock.GetUtcNow());
                }
            }
            return base.NonQueryExecutedAsync(command, eventData, result, cancellationToken);
        }
    }

    internal sealed class ReplacementWriteFault : DbCommandInterceptor
    {
        private DbContext? _identity;
        private ReplacementWriteBoundary _boundary;
        internal bool Observed { get; private set; }
        internal int AffectedRows { get; private set; }
        internal int WritesObserved { get; private set; }
        internal void Arm(DbContext identity, ReplacementWriteBoundary boundary)
        {
            _identity = identity;
            _boundary = boundary;
        }
        public override ValueTask<int> NonQueryExecutedAsync(DbCommand command, CommandExecutedEventData eventData,
            int result, CancellationToken cancellationToken = default)
        {
            if (ReferenceEquals(eventData.Context, _identity) && _identity?.Database.CurrentTransaction is not null
                && command.CommandText.TrimStart().StartsWith("UPDATE ", StringComparison.OrdinalIgnoreCase))
            {
                WritesObserved++;
                if (WritesObserved != (int)_boundary)
                    return base.NonQueryExecutedAsync(command, eventData, result, cancellationToken);
                _identity = null;
                Observed = true;
                AffectedRows = result;
                throw new InjectedReplacementFailure();
            }
            return base.NonQueryExecutedAsync(command, eventData, result, cancellationToken);
        }
    }
}
