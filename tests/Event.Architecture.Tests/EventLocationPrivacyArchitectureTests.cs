using Explore.Application.Features.TenantUsers.Handlers.Commands;

namespace Event.Architecture.Tests;

public sealed class EventLocationPrivacyArchitectureTests
{
    [Test]
    public async Task TenantMembershipFeature_MustNotReferenceGlobalDeletionOrHomeErasureAuthority()
    {
        var featureRoot = ContextSystemHelpers.RepoPath(
            "Explore.Application",
            "Features",
            "TenantUsers");
        var forbiddenTokens = new[]
        {
            "DeleteUserCommand",
            "IUserRepository",
            "ILocationRepository",
            "IUserLocationPrivacyErasureRepository",
            "IErasureAuthority"
        };
        var violations = Directory.GetFiles(featureRoot, "*.cs", SearchOption.AllDirectories)
            .SelectMany(file => forbiddenTokens
                .Where(token => File.ReadAllText(file).Contains(token, StringComparison.Ordinal))
                .Select(token => $"{Path.GetRelativePath(ContextSystemHelpers.RepoRoot, file)}:{token}"))
            .ToArray();

        await Assert.That(violations).IsEmpty();
    }

    [Test]
    public async Task MembershipHandler_MustUseOnlyTenantScopedRepositories()
    {
        var dependencyNames = typeof(RemoveTenantMembershipCommandHandler)
            .GetConstructors()
            .Single()
            .GetParameters()
            .Select(parameter => parameter.ParameterType.Name)
            .ToArray();

        await Assert.That(dependencyNames).Contains("ITenantUserRepository");
        await Assert.That(dependencyNames).Contains("ITenantUserRoleGrantRepository");
        await Assert.That(dependencyNames).DoesNotContain("ITenantUserProfileRepository");
        await Assert.That(dependencyNames.All(name => !name.Contains("Global", StringComparison.Ordinal))).IsTrue();
        await Assert.That(dependencyNames.All(name => !name.Contains("Erasure", StringComparison.Ordinal))).IsTrue();
    }

    [Test]
    public async Task MembershipMutation_MustNotBypassTenantOrQueryFilters()
    {
        var repositoryPath = ContextSystemHelpers.RepoPath(
            "Explore.Persistence",
            "Repositories",
            "TenantUserRepository.cs");
        var source = await File.ReadAllTextAsync(repositoryPath);
        var methodStart = source.IndexOf("TryRemoveMembershipAsync", StringComparison.Ordinal);

        await Assert.That(methodStart).IsGreaterThanOrEqualTo(0);
        var methodBody = source[methodStart..];
        await Assert.That(methodBody.Contains("IgnoreTenantFilter", StringComparison.Ordinal)).IsFalse();
        await Assert.That(methodBody.Contains("IgnoreQueryFilters", StringComparison.Ordinal)).IsFalse();
        await Assert.That(methodBody.Contains("TenantFilterTenantId != tenantId", StringComparison.Ordinal)).IsTrue();
    }
}
