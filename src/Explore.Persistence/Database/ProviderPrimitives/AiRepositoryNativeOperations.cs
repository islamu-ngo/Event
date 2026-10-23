using Explore.Domain;
using Explore.Persistence.Security;
using Microsoft.EntityFrameworkCore;

namespace Explore.Persistence.Database.ProviderPrimitives;

/// <summary>Native actor reads and owner fences; graph custody remains with the repository.</summary>
internal sealed class AiRepositoryNativeOperations
{
    private readonly ExploreDbContext context;

    private AiRepositoryNativeOperations(ExploreDbContext context) => this.context = context;

    internal static AiRepositoryNativeOperations? For(ExploreDbContext context) =>
        context.Database.IsNpgsql() ? new(context) : null;

    internal Task<IReadOnlyList<Actor>> ReadActorsAsync(PostgresNativeQueueEnrollment enrollment,
        Guid tenant, Guid principal, CancellationToken token) =>
        PostgresAiActorContextReadContract.ReadAsync(context, enrollment, tenant, principal, token);

    internal Task<bool> FenceOwnerAsync(PostgresNativeQueueEnrollment enrollment,
        Guid tenant, Guid user, Guid? actor, Guid? admittedActor, bool privacy, CancellationToken token,
        bool requireOrganizer = false) =>
        PostgresAiConversationOwnerFenceContract.FenceAsync(
            context, enrollment, tenant, user, actor, admittedActor, privacy, token, requireOrganizer);

    internal Task<bool> IsPrivacyFencedAsync(PostgresNativeQueueEnrollment enrollment,
        Guid tenant, Guid user, CancellationToken token) =>
        PostgresAiConversationOwnerFenceContract.IsPrivacyFencedAsync(context, enrollment, tenant, user, token);

    internal void RetainGraphOwner(Guid root, Guid tenant, Guid user, Guid? actor, Guid? admittedActor) =>
        context.RetainNativeAiGraphOwner(root, tenant, user, actor, admittedActor);
}
