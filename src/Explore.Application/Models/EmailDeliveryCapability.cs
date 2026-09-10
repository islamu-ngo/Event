
using Explore.Domain.Enums;

namespace Explore.Application.Models;

public sealed record EmailDeliveryCapability(
    EmailDeliveryState State,
    bool Enabled,
    SecretScope Scope,
    Guid? TenantId,
    string ReasonCode);
