using System.Data.Common;
using Explore.Application.Contracts.Identity;
using Explore.Application.Features.Authentication.Local.Models;

namespace Event.Application.UnitTests.Features.Authentication.Local;

public sealed class LocalPublicFailureContractTests
{
    [Test]
    [Arguments(LocalAuthFailure.InvalidCredentials)]
    [Arguments(LocalAuthFailure.AccountLocked)]
    public async Task NativeCredentialRejectionsRetainInternalCategoryWithoutAuthority(LocalAuthFailure failure)
    {
        LocalAuthResponseDto result = LocalAuthResponseDto.Failed(failure);
        await Assert.That(result.Failure).IsEqualTo(failure);
        await Assert.That(result.Success).IsFalse();
        await Assert.That(result.Token).IsNull();
        await Assert.That(result.ReplacementChallenge).IsNull();
        await Assert.That(result.UserId).IsNull();
        await Assert.That(result.Email).IsNull();
        await Assert.That(result.ExpiresAt).IsNull();
        await Assert.That(result.Roles.Count).IsEqualTo(0);
    }

    [Test]
    public async Task StoreClassifierAcceptsOnlyExplicitTransientDatabaseFailures()
    {
        await Assert.That(LocalIdentityIntakeFailureClassifier.IsTransientStoreFailure(new TransientStoreFailure())).IsTrue();
        await Assert.That(LocalIdentityIntakeFailureClassifier.IsTransientStoreFailure(
            new Exception("wrapper", new TransientStoreFailure()))).IsTrue();
        await Assert.That(LocalIdentityIntakeFailureClassifier.IsTransientStoreFailure(new PermanentStoreFailure())).IsFalse();
        await Assert.That(LocalIdentityIntakeFailureClassifier.IsTransientStoreFailure(new InvalidOperationException())).IsFalse();
        await Assert.That(LocalIdentityIntakeFailureClassifier.IsTransientStoreFailure(
            new InvalidOperationException("invariant", new TransientStoreFailure()))).IsFalse();
        await Assert.That(LocalIdentityIntakeFailureClassifier.IsTransientStoreFailure(new ArgumentException())).IsFalse();
        await Assert.That(LocalIdentityIntakeFailureClassifier.IsTransientStoreFailure(new OperationCanceledException())).IsFalse();
        await Assert.That(LocalIdentityIntakeFailureClassifier.IsTransientStoreFailure(
            new TransientStoreFailure(new OperationCanceledException()))).IsFalse();
    }

    [Test]
    [Arguments(LocalIdentityIntakeFailure.StoreUnavailable)]
    [Arguments(LocalIdentityIntakeFailure.Conflict)]
    public async Task ClassifiedPrecommitFailureCarriesOnlyValueFreeCategory(LocalIdentityIntakeFailure category)
    {
        var failure = new LocalIdentityIntakeException(category);
        await Assert.That(failure.Failure).IsEqualTo(category);
        await Assert.That(failure.InnerException).IsNull();
    }

    private sealed class TransientStoreFailure(Exception? inner = null) : DbException("Controlled store outage.", inner)
    {
        public override bool IsTransient => true;
    }

    private sealed class PermanentStoreFailure() : DbException("Controlled permanent database failure.");
}
