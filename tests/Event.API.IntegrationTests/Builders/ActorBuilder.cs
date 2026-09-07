using Explore.Domain;
using Explore.Domain.Enums;

namespace Event.Api.IntegrationTests.Builders;

/// <summary>
/// Builds <see cref="Actor"/> instances with embedded <see cref="ActorPii"/> for test data seeding.
/// </summary>
public sealed class ActorBuilder
{
    private Guid _id = Guid.NewGuid();
    private string _displayName = "Test Actor";
    private int _actorTypeId = (int)ActorTypeEnum.User;
    private Guid? _userId;

    public ActorBuilder WithId(Guid id) { _id = id; return this; }
    public ActorBuilder WithDisplayName(string name) { _displayName = name; return this; }
    public ActorBuilder WithActorType(ActorTypeEnum type) { _actorTypeId = (int)type; return this; }
    public ActorBuilder WithUserId(Guid userId) { _userId = userId; return this; }

    public Actor Build() => new()
    {
        Id = _id,
        Pii = new ActorPii { DisplayName = _displayName },
        ActorTypeId = _actorTypeId,
        ActorType = null!,
        UserId = _userId
    };
}
