using System;

namespace Explore.Application.Features.Groups;

internal readonly record struct GroupParentTarget(Guid? ParentOrganizationId, Guid? ParentGroupId);
