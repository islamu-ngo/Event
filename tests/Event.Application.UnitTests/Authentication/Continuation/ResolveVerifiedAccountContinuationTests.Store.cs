using Explore.Application.Authentication;
using Explore.Application.Contracts.Persistence;
using Explore.Application.Features.Authentication.Continuation;
using Explore.Domain;
using Explore.Domain.Enums;

namespace Event.Application.UnitTests.Authentication.Continuation;

public sealed partial class ResolveVerifiedAccountContinuationTests
{
    private sealed class Store : IExternalLoginAuthenticationRepository,
        IUserTenantNavigationRepository, IUserTenantMembershipRepository
    {
        public ProviderAccountKey Key { get; set; } =
            PlatformIdentityPrincipalExtensions.CreateOidcAccountKey("https://identity.example/realm", "same-subject");
        public UserExternalLogin Login { get; }
        public User? User { get; set; } = new() { Id = Guid.CreateVersion7(), Pii = null! };
        public List<TenantUser> Rows { get; } = [];
        public bool HasLogin { get; set; } = true;
        public bool CaseInsensitiveLookup { get; set; }
        public int FailureStage { get; set; }
        public int CancelStage { get; set; }
        public CancellationTokenSource? Cancellation { get; set; }
        public ResolveVerifiedAccountContinuationQueryHandler Handler => new(this, this, this);

        public Store() => Login = new()
        {
            Id = Guid.CreateVersion7(),
            UserId = User!.Id,
            User = null!,
            AuthenticationProviderId = (int)Key.ProviderKind,
            AuthenticationProvider = null!,
            ProviderKey = Key.Value
        };

        public Task<VerifiedAccountContinuationResult> Resolve(Guid? target = null, CancellationToken token = default) =>
            Handler.QueryAsync(new(Key, target), token);

        public TenantUser Member(string slug, int day)
        {
            var tenant = new Tenant
            {
                Id = Guid.CreateVersion7(),
                FullName = slug,
                Slug = slug,
                TenantStatusId = (int)TenantStatusEnum.Active,
                TenantStatus = null!
            };
            return new()
            {
                Id = Guid.CreateVersion7(),
                UserId = Login.UserId,
                User = null!,
                TenantId = tenant.Id,
                Tenant = tenant,
                StatusId = (int)TenantUserStatusEnum.Active,
                JoinedAt = new DateTime(2026, 1, day, 0, 0, 0, DateTimeKind.Utc),
                CreatedAt = new DateTime(2026, 1, day, 0, 0, 0, DateTimeKind.Utc)
            };
        }

        public Task<UserExternalLogin?> GetByProviderAndKey(
            ProviderAccountKey accountKey, CancellationToken cancellationToken = default)
        {
            Read(1, cancellationToken);
            return Task.FromResult(HasLogin && Login.AuthenticationProviderId == (int)accountKey.ProviderKind
                && string.Equals(Login.ProviderKey, accountKey.Value,
                    CaseInsensitiveLookup ? StringComparison.OrdinalIgnoreCase : StringComparison.Ordinal)
                ? Login : null);
        }

        public Task<User?> GetNavigationUserAsync(Guid userId, CancellationToken cancellationToken)
        {
            Read(2, cancellationToken);
            return Task.FromResult(User);
        }

        public Task<List<TenantUser>> GetActiveTenantsForUserAsync(
            Guid userId, CancellationToken cancellationToken = default)
        {
            Read(3, cancellationToken);
            return Task.FromResult(Rows);
        }

        public Task<bool> IsActiveTenantUserAsync(
            Guid tenantId, Guid userId, CancellationToken cancellationToken = default) =>
            throw new InvalidOperationException("Continuation must enumerate through its selected membership owner.");

        private void Read(int stage, CancellationToken token)
        {
            token.ThrowIfCancellationRequested();
            if (FailureStage == stage) throw new UnauthorizedAccessException("Selected read owner unavailable.");
            if (CancelStage == stage) Cancellation!.Cancel();
        }
    }
}
