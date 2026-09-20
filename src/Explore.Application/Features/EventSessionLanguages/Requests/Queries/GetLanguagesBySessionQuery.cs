using System;
using System.Collections.Generic;
using Explore.Application.Authorization;
using Explore.Application.DTOs.EventSessionLanguage;
using Explore.Application.Contracts.Operations;

namespace Explore.Application.Features.EventSessionLanguages.Requests.Queries;

public sealed record GetLanguagesBySessionQuery : IQuery<List<EventSessionLanguageListDto>>
{
    public Guid EventSessionId { get; init; }
}

[AuthorizeResource(ResourceKinds.Event, AuthorizationActions.Events.ViewManagement)]
public sealed record GetManagedLanguagesBySessionQuery : IQuery<List<EventSessionLanguageListDto>>, ISecureRequest
{
    public Guid EventId { get; init; }
    public Guid EventSessionId { get; init; }

    string? ISecureRequest.ResourceId => EventId.ToString();
}
