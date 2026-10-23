using System.Globalization;
using Explore.Application.Contracts.Identity;
using Microsoft.AspNetCore.Identity;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using Native = Explore.Persistence.Security.PostgresLocalAdministrationContract;

namespace Explore.Persistence.Identity;

internal sealed partial class LocalIdentityCredentialStateStore
{
    private UserManager<LocalIdentityUser> PrivateManager(
        NativeAdministrationAttempt native, LocalIdentityUser candidate, IServiceProvider services,
        CancellationToken token) =>
        new(new NativeIdentityStore(native, candidate, token),
            Options.Create(userManager.Options), userManager.PasswordHasher,
            userManager.UserValidators, userManager.PasswordValidators,
            userManager.KeyNormalizer, userManager.ErrorDescriber, services,
            services.GetRequiredService<ILogger<UserManager<LocalIdentityUser>>>());

    /// <summary>
    /// Only candidate Identity validation and one complete native pending creation
    /// are supported. No ambient EF store, arbitrary user lookup or staged success
    /// is reachable through the private UserManager.
    /// </summary>
    private sealed class NativeIdentityStore(
        NativeAdministrationAttempt native, LocalIdentityUser candidate, CancellationToken ownerToken)
        : IUserPasswordStore<LocalIdentityUser>, IUserEmailStore<LocalIdentityUser>,
          IUserSecurityStampStore<LocalIdentityUser>, IUserLockoutStore<LocalIdentityUser>
    {
        private readonly HashSet<LocalIdentityUser> observed = [];
        private bool disposed;
        private bool written;

        private void Require(LocalIdentityUser user, CancellationToken token, bool mutation = false)
        {
            ObjectDisposedException.ThrowIf(disposed, this);
            ownerToken.ThrowIfCancellationRequested();
            token.ThrowIfCancellationRequested();
            native.RequireOwner();
            if ((!ReferenceEquals(user, candidate) && (mutation || !observed.Contains(user))) || written)
                throw new UnauthorizedAccessException("The private Identity store accepts only its original candidate.");
            if (mutation && native.Request.Stage != LocalAdministrationStage.CreatePending)
                throw new UnauthorizedAccessException("Only pending creation may mutate the private Identity candidate.");
        }

        public async Task<IdentityResult> CreateAsync(LocalIdentityUser user, CancellationToken cancellationToken)
        {
            Require(user, cancellationToken, mutation: true);
            if (native.Request.Stage != LocalAdministrationStage.CreatePending)
                throw new UnauthorizedAccessException("The private Identity store cannot create in this lifecycle.");
            await native.CompileAsync(new Native.Accepted(
                ChangedAt: user.CreatedAt, LocalSubjectId: user.Id,
                PersonalActorId: Guid.CreateVersion7(), ExternalLoginId: Guid.CreateVersion7(),
                OperationStamp: Guid.CreateVersion7(), PasswordHash: user.PasswordHash,
                IdentityConcurrencyStamp: user.ConcurrencyStamp, SecurityStamp: user.SecurityStamp), ownerToken);
            native.RequireCreatedUser(user);
            await native.ApplyAsync(ownerToken);
            if (native.Outcome != "Created")
                throw new UnauthorizedAccessException("The private Identity creation did not write its complete pending group.");
            written = true;
            return IdentityResult.Success;
        }

        public Task<IdentityResult> UpdateAsync(LocalIdentityUser user, CancellationToken cancellationToken) =>
            throw new UnauthorizedAccessException("The private Identity store has no general update capability.");

        public Task<IdentityResult> DeleteAsync(LocalIdentityUser user, CancellationToken cancellationToken) =>
            throw new UnauthorizedAccessException("The private Identity store has no delete capability.");

        public Task<LocalIdentityUser?> FindByIdAsync(string userId, CancellationToken cancellationToken)
        {
            Require(candidate, cancellationToken);
            if (!string.Equals(userId, candidate.Id.ToString("D"), StringComparison.Ordinal))
                throw new UnauthorizedAccessException("The private Identity lookup is outside the candidate.");
            return Task.FromResult(Find("Id", userId));
        }

        public Task<LocalIdentityUser?> FindByNameAsync(string normalizedUserName, CancellationToken cancellationToken)
        {
            Require(candidate, cancellationToken);
            string? allowed = native.Request.Stage == LocalAdministrationStage.CreatePending
                ? native.Request.NormalizedUserName : candidate.NormalizedUserName;
            if (!string.Equals(normalizedUserName, allowed, StringComparison.Ordinal))
                throw new UnauthorizedAccessException("The private Identity name lookup is outside the candidate.");
            return Task.FromResult(Find(nameof(LocalIdentityUser.NormalizedUserName), normalizedUserName));
        }

        public Task<LocalIdentityUser?> FindByEmailAsync(string normalizedEmail, CancellationToken cancellationToken)
        {
            Require(candidate, cancellationToken);
            string? allowed = native.Request.Stage == LocalAdministrationStage.CreatePending
                ? native.Request.NormalizedEmail : candidate.NormalizedEmail;
            if (!string.Equals(normalizedEmail, allowed, StringComparison.Ordinal))
                throw new UnauthorizedAccessException("The private Identity email lookup is outside the candidate.");
            return Task.FromResult(Find(nameof(LocalIdentityUser.NormalizedEmail), normalizedEmail));
        }

        private LocalIdentityUser? Find(string property, string value)
        {
            var row = Rows(native.Before, nameof(LocalIdentityUser))
                .Where(item => Text(item, property) == value)
                .Select(item => (System.Text.Json.JsonElement?)item).SingleOrDefault();
            if (row is null) return null;
            var user = System.Text.Json.JsonSerializer.Deserialize<LocalIdentityUser>(row.Value, SerializerOptions)
                ?? throw new InvalidOperationException("The native Identity candidate image is absent.");
            observed.Add(user);
            return user;
        }

        public Task<string> GetUserIdAsync(LocalIdentityUser user, CancellationToken cancellationToken)
        {
            Require(user, cancellationToken);
            return Task.FromResult(user.Id.ToString("D", CultureInfo.InvariantCulture));
        }

        public Task<string?> GetUserNameAsync(LocalIdentityUser user, CancellationToken cancellationToken)
        {
            Require(user, cancellationToken);
            return Task.FromResult(user.UserName);
        }

        public Task SetUserNameAsync(LocalIdentityUser user, string? userName, CancellationToken cancellationToken)
        {
            Require(user, cancellationToken, mutation: true);
            user.UserName = userName;
            return Task.CompletedTask;
        }

        public Task<string?> GetNormalizedUserNameAsync(LocalIdentityUser user, CancellationToken cancellationToken)
        {
            Require(user, cancellationToken);
            return Task.FromResult(user.NormalizedUserName);
        }

        public Task SetNormalizedUserNameAsync(LocalIdentityUser user, string? normalizedName, CancellationToken cancellationToken)
        {
            Require(user, cancellationToken, mutation: true);
            user.NormalizedUserName = normalizedName;
            return Task.CompletedTask;
        }

        public Task SetPasswordHashAsync(LocalIdentityUser user, string? passwordHash, CancellationToken cancellationToken)
        {
            Require(user, cancellationToken, mutation: true);
            user.PasswordHash = passwordHash;
            return Task.CompletedTask;
        }

        public Task<string?> GetPasswordHashAsync(LocalIdentityUser user, CancellationToken cancellationToken)
        {
            Require(user, cancellationToken);
            return Task.FromResult(user.PasswordHash);
        }

        public Task<bool> HasPasswordAsync(LocalIdentityUser user, CancellationToken cancellationToken)
        {
            Require(user, cancellationToken);
            return Task.FromResult(user.PasswordHash is not null);
        }

        public Task SetEmailAsync(LocalIdentityUser user, string? email, CancellationToken cancellationToken)
        {
            Require(user, cancellationToken, mutation: true);
            user.Email = email;
            return Task.CompletedTask;
        }

        public Task<string?> GetEmailAsync(LocalIdentityUser user, CancellationToken cancellationToken)
        {
            Require(user, cancellationToken);
            return Task.FromResult(user.Email);
        }

        public Task<bool> GetEmailConfirmedAsync(LocalIdentityUser user, CancellationToken cancellationToken)
        {
            Require(user, cancellationToken);
            return Task.FromResult(user.EmailConfirmed);
        }

        public Task SetEmailConfirmedAsync(LocalIdentityUser user, bool confirmed, CancellationToken cancellationToken)
        {
            Require(user, cancellationToken, mutation: true);
            user.EmailConfirmed = confirmed;
            return Task.CompletedTask;
        }

        public Task<string?> GetNormalizedEmailAsync(LocalIdentityUser user, CancellationToken cancellationToken)
        {
            Require(user, cancellationToken);
            return Task.FromResult(user.NormalizedEmail);
        }

        public Task SetNormalizedEmailAsync(LocalIdentityUser user, string? normalizedEmail, CancellationToken cancellationToken)
        {
            Require(user, cancellationToken, mutation: true);
            user.NormalizedEmail = normalizedEmail;
            return Task.CompletedTask;
        }

        public Task SetSecurityStampAsync(LocalIdentityUser user, string stamp, CancellationToken cancellationToken)
        {
            Require(user, cancellationToken, mutation: true);
            user.SecurityStamp = stamp;
            return Task.CompletedTask;
        }

        public Task<string?> GetSecurityStampAsync(LocalIdentityUser user, CancellationToken cancellationToken)
        {
            Require(user, cancellationToken);
            return Task.FromResult(user.SecurityStamp);
        }

        public Task<DateTimeOffset?> GetLockoutEndDateAsync(LocalIdentityUser user, CancellationToken cancellationToken)
        {
            Require(user, cancellationToken);
            return Task.FromResult(user.LockoutEnd);
        }

        public Task SetLockoutEndDateAsync(LocalIdentityUser user, DateTimeOffset? lockoutEnd, CancellationToken cancellationToken)
        {
            Require(user, cancellationToken, mutation: true);
            user.LockoutEnd = lockoutEnd;
            return Task.CompletedTask;
        }

        public Task<int> IncrementAccessFailedCountAsync(LocalIdentityUser user, CancellationToken cancellationToken) =>
            throw new UnauthorizedAccessException("The private Identity store has no login mutation capability.");

        public Task ResetAccessFailedCountAsync(LocalIdentityUser user, CancellationToken cancellationToken) =>
            throw new UnauthorizedAccessException("The private Identity store has no login mutation capability.");

        public Task<int> GetAccessFailedCountAsync(LocalIdentityUser user, CancellationToken cancellationToken)
        {
            Require(user, cancellationToken);
            return Task.FromResult(user.AccessFailedCount);
        }

        public Task<bool> GetLockoutEnabledAsync(LocalIdentityUser user, CancellationToken cancellationToken)
        {
            Require(user, cancellationToken);
            return Task.FromResult(user.LockoutEnabled);
        }

        public Task SetLockoutEnabledAsync(LocalIdentityUser user, bool enabled, CancellationToken cancellationToken)
        {
            Require(user, cancellationToken, mutation: true);
            user.LockoutEnabled = enabled;
            return Task.CompletedTask;
        }

        public void Dispose() => disposed = true;
    }
}
