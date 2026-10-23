using Event.Api.IntegrationTests.Fixtures;
using Explore.Application.Contracts.Identity;
using Explore.Application.Features.Authentication.Local.Requests.Commands;
using Explore.Domain;
using Explore.Domain.Enums;
using Explore.Persistence.Identity;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;

namespace Event.Api.IntegrationTests.Authentication;

public sealed partial class LocalAdministrationCustodyTests
{
    [Test]
    [Arguments(false)]
    [Arguments(true)]
    public async Task ClosedBindingRejectsPartialGraphsWithoutActivatingTheCredential(bool includeActor)
    {
        await using var fixture = await LocalIdentityLifecycleHttpFixture.CreateAsync();
        await GrantAdministrator(fixture);
        await using var request = new Request(fixture.Host.Services);
        await request.Authenticate(await fixture.SignInAsync());
        var command = new CreateLocalIdentityCommand(Guid.CreateVersion7(),
            $"{Guid.CreateVersion7():N}@custody.test", "Partial", "Binding");
        var invocation = await request.Services.GetRequiredService<ILocalAdministrationPreparation>()
            .PrepareAsync(command, fixture.Binding.LocalSubjectId, default);
        var administration = request.Services.GetRequiredService<ILocalHumanCredentialAdministration>();
        var creation = await administration.CreatePendingAsync(invocation, command, default);
        await Assert.That(creation.Outcome).IsEqualTo(LocalCredentialCreateOutcome.Created);
        var snapshot = (await administration.ReadProvisioningAsync(invocation, command, default))!;
        var receipt = snapshot.Receipt;
        await using (var writer = fixture.Native.CreateDatabase())
        {
            var user = new User
            {
                Id = receipt.ApplicationUserId,
                Pii = new UserPii
                {
                    Email = snapshot.Email!,
                    FirstName = snapshot.FirstName,
                    LastName = snapshot.LastName
                },
                EmailVerified = snapshot.EmailVerified,
                CreatedAt = receipt.CreatedAt,
                CreatedBy = receipt.InitiatingApplicationUserId
            };
            writer.Users.Add(user);
            if (includeActor)
                writer.Actors.Add(new Actor
                {
                    Id = receipt.PersonalActorId,
                    ActorTypeId = (int)ActorTypeEnum.User,
                    ActorType = null!,
                    UserId = user.Id,
                    User = user,
                    Pii = new ActorPii { DisplayName = "Partial binding" },
                    CreatedAt = receipt.CreatedAt,
                    CreatedBy = receipt.InitiatingApplicationUserId
                });
            await writer.SaveChangesAsync();
        }

        var result = await administration.BindAsync(invocation, command, snapshot, default);
        await Assert.That(result.IsSuccess).IsFalse();
        await using var observer = fixture.Native.CreateDatabase();
        await Assert.That(await observer.Users.AsNoTracking().AnyAsync(row => row.Id == receipt.ApplicationUserId)).IsTrue();
        await Assert.That(await observer.Actors.AsNoTracking().AnyAsync(row => row.Id == receipt.PersonalActorId))
            .IsEqualTo(includeActor);
        await Assert.That(await observer.UserExternalLogins.AsNoTracking().AnyAsync(row => row.Id == receipt.ExternalLoginId)).IsFalse();
        await Assert.That(await observer.Set<LocalIdentityCredentialOperation>().AsNoTracking()
            .Where(row => row.Id == receipt.OperationId).Select(row => row.Stage).SingleAsync())
            .IsEqualTo(LocalCredentialOperationStage.ProvisioningPending);
    }
}
