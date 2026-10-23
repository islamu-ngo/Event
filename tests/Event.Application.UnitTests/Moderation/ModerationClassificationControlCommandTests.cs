using Explore.Application.Features.Moderation.Handlers.Commands;
using Explore.Application.Features.Moderation.Requests.Commands;
using ISLAMU.Moderation.Domain;

namespace Event.Application.UnitTests.Moderation;

public sealed class ModerationClassificationControlCommandTests
{
    [Test]
    [Arguments(ModerationScopeKind.Event, true)]
    [Arguments(ModerationScopeKind.Resource, false)]
    [Arguments(ModerationScopeKind.Tenant, false)]
    public async Task ClassificationAdoptionRequiresTheTypedEventTarget(ModerationScopeKind kind, bool valid)
    {
        var command = Adoption();
        var tenant = command.Target.Parent!;
        command = command with
        {
            Target = kind switch
            {
                ModerationScopeKind.Event => command.Target,
                ModerationScopeKind.Resource => AuthorityScope.Resource(tenant, command.Target.Id),
                _ => tenant
            }
        };
        var validation = await new ModerationClassificationControlCommandValidator<AdoptModerationClassificationEventTargetCommand>()
            .ValidateAsync(command);
        await Assert.That(validation.IsValid).IsEqualTo(valid);
    }

    [Test]
    [Arguments(0)]
    [Arguments(1)]
    [Arguments(2)]
    [Arguments(3)]
    [Arguments(4)]
    [Arguments(5)]
    public async Task InvalidAdoptionCoordinatesCannotBecomeNativeIntent(int substitution)
    {
        var command = Adoption();
        command = substitution switch
        {
            0 => command with { ExpectedSelectionRevision = 2 },
            1 => command with { BindingOrdinal = 64 },
            2 => command with { EventGenerationId = Guid.Empty },
            3 => command with { ExpectedQualificationRevision = 0 },
            4 => command with { ValidUntil = DateTimeOffset.MaxValue },
            _ => command with { OperationId = Guid.Empty }
        };
        var validation = await new ModerationClassificationControlCommandValidator<AdoptModerationClassificationEventTargetCommand>()
            .ValidateAsync(command);
        await Assert.That(validation.IsValid).IsFalse();
    }

    private static AdoptModerationClassificationEventTargetCommand Adoption()
    {
        var tenant = AuthorityScope.Tenant(AuthorityScope.Instance(AuthorityScope.Solution(
            AuthorityScope.TrustDomain(Guid.CreateVersion7()), Guid.CreateVersion7()),
            Guid.CreateVersion7()), Guid.CreateVersion7());
        return new()
        {
            OperationId = Guid.CreateVersion7(), Target = AuthorityScope.Event(tenant, Guid.CreateVersion7()),
            ExpectedQualificationRevision = 12, AdoptionId = Guid.CreateVersion7(), SelectionId = Guid.CreateVersion7(),
            ExpectedSelectionRevision = 1, BindingOrdinal = 0, EventGenerationId = Guid.CreateVersion7(),
            ExpectedEventGenerationRevision = 1,
            ValidUntil = new DateTimeOffset(2026, 10, 14, 12, 5, 0, TimeSpan.Zero)
        };
    }
}
