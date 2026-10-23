using System.Data;
using Event.Persistence.IntegrationTests.Fixtures;
using Explore.Application.Authentication;
using Explore.Domain;
using Explore.Domain.Enums;
using Explore.Persistence.Identity;
using Explore.Persistence.Repositories;
using Explore.Persistence.Security;
using Microsoft.EntityFrameworkCore;

namespace Event.Persistence.IntegrationTests;

/// <summary>Selected-owner binding reads; no fresh Global context or PostgreSQL grant proof is substituted.</summary>
[NotInParallel]
public sealed class IdentityWriterExternalLoginAuthenticationTests
{
    [Test]
    public async Task OriginalUncommittedBindingIsVisibleAndItsRollbackRemainsCallerOwned()
    {
        await using var fixture = await EventVisitorCapabilitySqliteFixture.CreateAsync();
        var context = fixture.Context;
        var selected = Bind(fixture);
        await using var gate = new HumanSecuritySourceGate(context);
        await using var held = await gate.AcquireAsync();
        var connector = context.Database.GetDbConnection();
        var repository = new IdentityWriterExternalLoginAuthenticationRepository(selected);
        var binding = NewBinding(fixture.UserId);
        var key = new ProviderAccountKey(AuthenticationProviderKind.Local, binding.ProviderKey);
        await using (var transaction = await context.Database.BeginTransactionAsync(IsolationLevel.Serializable))
        {
            context.UserExternalLogins.Add(binding);
            await context.SaveChangesAsync();

            var found = await repository.GetByProviderAndKey(key);

            await Assert.That(found).IsNotNull();
            await Assert.That(found!.Id).IsEqualTo(binding.Id);
            await Assert.That(found.UserId).IsEqualTo(binding.UserId);
            await Assert.That(found.ProviderDisplayName).IsEqualTo(binding.ProviderDisplayName);
            await Assert.That(found.CreatedAt).IsEqualTo(binding.CreatedAt);
            await Assert.That(found.CreatedBy).IsEqualTo(binding.CreatedBy);
            await Assert.That(found.UpdatedAt).IsEqualTo(binding.UpdatedAt);
            await Assert.That(found.UpdatedBy).IsEqualTo(binding.UpdatedBy);
            await Assert.That(ReferenceEquals(found, binding)).IsFalse();
            await Assert.That(context.Database.CurrentTransaction).IsSameReferenceAs(transaction);
            await Assert.That(context.Database.GetDbConnection()).IsSameReferenceAs(connector);
            gate.RequireCurrent();
            await transaction.RollbackAsync();
        }
        await Assert.That(await repository.GetByProviderAndKey(key)).IsNull();
        gate.RequireCurrent();
    }

    [Test]
    public async Task PersistedBindingRekeyDoesNotReturnTheStaleTrackedBinding()
    {
        await using var fixture = await EventVisitorCapabilitySqliteFixture.CreateAsync();
        var context = fixture.Context;
        var binding = NewBinding(fixture.UserId);
        context.UserExternalLogins.Add(binding);
        await context.SaveChangesAsync();
        string originalKey = binding.ProviderKey;
        string replacementKey = Guid.CreateVersion7().ToString("D");
        await context.UserExternalLogins.Where(row => row.Id == binding.Id)
            .ExecuteUpdateAsync(update => update.SetProperty(row => row.ProviderKey, replacementKey));
        var selected = Bind(fixture);
        await using var gate = new HumanSecuritySourceGate(context);
        await using var held = await gate.AcquireAsync();
        var repository = new IdentityWriterExternalLoginAuthenticationRepository(selected);

        await Assert.That(await repository.GetByProviderAndKey(
            new ProviderAccountKey(AuthenticationProviderKind.Local, originalKey))).IsNull();
        var current = await repository.GetByProviderAndKey(
            new ProviderAccountKey(AuthenticationProviderKind.Local, replacementKey));

        await Assert.That(current).IsNotNull();
        await Assert.That(current!.ProviderKey).IsEqualTo(replacementKey);
        await Assert.That(binding.ProviderKey).IsEqualTo(originalKey);
        await Assert.That(ReferenceEquals(current, binding)).IsFalse();
    }

    [Test]
    [Arguments(false)]
    [Arguments(true)]
    public async Task BothProviderAndAccountKeyMustMatchThePersistedTuple(bool changeProvider)
    {
        await using var fixture = await EventVisitorCapabilitySqliteFixture.CreateAsync();
        var binding = NewBinding(fixture.UserId);
        fixture.Context.UserExternalLogins.Add(binding);
        await fixture.Context.SaveChangesAsync();
        var selected = Bind(fixture);
        await using var gate = new HumanSecuritySourceGate(fixture.Context);
        await using var held = await gate.AcquireAsync();
        var repository = new IdentityWriterExternalLoginAuthenticationRepository(selected);
        var wrong = new ProviderAccountKey(changeProvider ? AuthenticationProviderKind.Google : AuthenticationProviderKind.Local,
            changeProvider ? binding.ProviderKey : Guid.CreateVersion7().ToString("D"));

        await Assert.That(await repository.GetByProviderAndKey(wrong)).IsNull();
        await Assert.That((await repository.GetByProviderAndKey(
            new ProviderAccountKey(AuthenticationProviderKind.Local, binding.ProviderKey)))!.Id).IsEqualTo(binding.Id);
    }

    [Test]
    public async Task SelectedPairWithoutItsSecuritySourceGateCannotResolveABinding()
    {
        await using var fixture = await EventVisitorCapabilitySqliteFixture.CreateAsync();
        var binding = NewBinding(fixture.UserId);
        fixture.Context.UserExternalLogins.Add(binding);
        await fixture.Context.SaveChangesAsync();
        var repository = new IdentityWriterExternalLoginAuthenticationRepository(Bind(fixture));

        await Assert.ThrowsAsync<InvalidOperationException>(() => repository.GetByProviderAndKey(
            new ProviderAccountKey(AuthenticationProviderKind.Local, binding.ProviderKey)));
        await Assert.That(fixture.Context.Database.CurrentTransaction).IsNull();
        await Assert.That(await fixture.Context.UserExternalLogins.AsNoTracking()
            .AnyAsync(row => row.Id == binding.Id)).IsTrue();
    }

    [Test]
    public async Task UnselectedPairCannotUseAnOtherwiseHeldGate()
    {
        await using var fixture = await EventVisitorCapabilitySqliteFixture.CreateAsync();
        await using var gate = new HumanSecuritySourceGate(fixture.Context);
        await using var held = await gate.AcquireAsync();
        var repository = new IdentityWriterExternalLoginAuthenticationRepository(new LocalIdentityExecutionLease());

        await Assert.ThrowsAsync<UnauthorizedAccessException>(() => repository.GetByProviderAndKey(
            new ProviderAccountKey(AuthenticationProviderKind.Local, fixture.UserId.ToString("D"))));
        gate.RequireCurrent();
    }

    private static LocalIdentityExecutionLease Bind(EventVisitorCapabilitySqliteFixture fixture)
    {
        var selected = new LocalIdentityExecutionLease();
        selected.Bind(fixture.Context, fixture.Context);
        return selected;
    }

    private static UserExternalLogin NewBinding(Guid userId) => new()
    {
        Id = Guid.CreateVersion7(),
        UserId = userId,
        User = null!,
        AuthenticationProviderId = (int)AuthenticationProviderKind.Local,
        AuthenticationProvider = null!,
        ProviderKey = userId.ToString("D"),
        ProviderDisplayName = "Selected Local binding",
        CreatedAt = DateTime.UtcNow,
        CreatedBy = userId
    };
}
