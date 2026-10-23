using System.Reflection;
using System.Text.Json;
using Explore.API.Attributes;
using Explore.API.Controllers;
using Explore.API.Hateoas;
using Explore.API.Serialization;
using Explore.Application.Contracts.Operations;
using Explore.Application.Features.Moderation.Requests.Commands;
using ISLAMU.Moderation.Domain;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Http.Metadata;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.OutputCaching;
using Microsoft.AspNetCore.RateLimiting;

namespace Event.Api.IntegrationTests.Features;

public sealed class ModerationOperationalBudgetsControllerContractTests
{
    private static readonly JsonSerializerOptions Options = new(JsonSerializerDefaults.Web)
    {
        Converters = { new ModerationAuthorityScopeJsonConverter(), new System.Text.Json.Serialization.JsonStringEnumConverter() }
    };

    [Test]
    [Arguments("record", "recordedByHumanId")]
    [Arguments("record", "subjectId")]
    [Arguments("adopt", "adoptedByHumanId")]
    [Arguments("withdraw", "isAdministrator")]
    [Arguments("terms", "mandateId")]
    [Arguments("terms", "isAuthorized")]
    public async Task UnknownIdentityOrAuthorityCannotBindAtAnyBudgetInputBoundary(string boundary, string member)
    {
        var instance = AuthorityScope.Instance(AuthorityScope.Solution(
            AuthorityScope.TrustDomain(Guid.CreateVersion7()), Guid.CreateVersion7()), Guid.CreateVersion7());
        var record = new RecordModerationOperationalBudgetCommand
        {
            OperationId = Guid.CreateVersion7(),
            Target = AuthorityScope.Tenant(instance, Guid.CreateVersion7()),
            ExpectedQualificationRevision = 1,
            SourceId = Guid.CreateVersion7(),
            OperatorHumanId = Guid.CreateVersion7(),
            Terms = new(ModerationSubjectKind.Human, ModerationOperationalAction.SubmitReport, 3, TimeSpan.FromHours(1)),
            ValidUntil = new(2026, 10, 16, 12, 0, 0, TimeSpan.Zero)
        };
        ModerationQualificationCommand command = boundary switch
        {
            "adopt" => new AdoptModerationOperationalBudgetCommand
            {
                OperationId = Guid.CreateVersion7(),
                Target = record.Target,
                ExpectedQualificationRevision = 2,
                SourceId = record.SourceId,
                ExpectedSourceRevision = 1,
                ExpectedSourceWriterGeneration = 2,
                AdoptionId = Guid.CreateVersion7(),
                ValidUntil = record.ValidUntil
            },
            "withdraw" => new WithdrawModerationOperationalBudgetCommand
            {
                OperationId = Guid.CreateVersion7(),
                Target = record.Target,
                ExpectedQualificationRevision = 3,
                AdoptionId = Guid.CreateVersion7(),
                ExpectedAdoptionRevision = 1
            },
            _ => record
        };
        var document = JsonSerializer.SerializeToNode(command, command.GetType(), Options)!.AsObject();
        if (boundary == "terms") document["terms"]!.AsObject()[member] = "caller assertion";
        else document[member] = "caller assertion";
        await Assert.That(() => JsonSerializer.Deserialize(document.ToJsonString(), command.GetType(), Options))
            .Throws<JsonException>();
    }

    [Test]
    [Arguments(nameof(ModerationOperationalBudgetsController.Record), "sources", RouteNames.RecordModerationOperationalBudget)]
    [Arguments(nameof(ModerationOperationalBudgetsController.Adopt), "adoptions", RouteNames.AdoptModerationOperationalBudget)]
    [Arguments(nameof(ModerationOperationalBudgetsController.Withdraw), "adoptions/{adoptionId:guid}/withdraw",
        RouteNames.WithdrawModerationOperationalBudget)]
    public async Task BudgetAdministrationRetainsAuthenticatedBoundedNoncachedWriteMetadata(
        string action, string template, string routeName)
    {
        var type = typeof(ModerationOperationalBudgetsController);
        var method = type.GetMethod(action)!;
        await Assert.That(type.GetCustomAttribute<AuthorizeAttribute>()).IsNotNull();
        await Assert.That(method.GetCustomAttribute<AllowAnonymousAttribute>()).IsNull();
        await Assert.That(type.GetCustomAttribute<EnableRateLimitingAttribute>()?.PolicyName).IsEqualTo("write");
        await Assert.That(((IRequestSizeLimitMetadata?)type.GetCustomAttribute<RequestSizeLimitAttribute>())?.MaxRequestBodySize)
            .IsEqualTo(32L * 1024);
        await Assert.That(type.GetCustomAttribute<ResponseCacheAttribute>()?.NoStore).IsTrue();
        await Assert.That(type.GetCustomAttribute<OutputCacheAttribute>()?.NoStore).IsTrue();
        await Assert.That(method.GetCustomAttribute<SuppressIdempotencyResponseStorageAttribute>()).IsNotNull();
        var route = method.GetCustomAttribute<HttpPostAttribute>();
        await Assert.That(route?.Name).IsEqualTo(routeName);
        await Assert.That(route?.Template).IsEqualTo(template);
    }

    [Test]
    public async Task WithdrawalUsesTheRouteAdoptionWithoutMutatingCallerOwnedIntent()
    {
        WithdrawModerationOperationalBudgetCommand? observed = null;
        var controller = new ModerationOperationalBudgetsController(
            new Handler<RecordModerationOperationalBudgetCommand>(),
            new Handler<AdoptModerationOperationalBudgetCommand>(),
            new Handler<WithdrawModerationOperationalBudgetCommand>(command => observed = command));
        var instance = AuthorityScope.Instance(AuthorityScope.Solution(
            AuthorityScope.TrustDomain(Guid.CreateVersion7()), Guid.CreateVersion7()), Guid.CreateVersion7());
        var bodyId = Guid.CreateVersion7();
        var command = new WithdrawModerationOperationalBudgetCommand
        {
            OperationId = Guid.CreateVersion7(),
            Target = AuthorityScope.Tenant(instance, Guid.CreateVersion7()),
            ExpectedQualificationRevision = 3,
            AdoptionId = bodyId,
            ExpectedAdoptionRevision = 1
        };
        var routeId = Guid.CreateVersion7();
        await controller.Withdraw(routeId, command, default);
        await Assert.That(observed).IsNotNull();
        await Assert.That(observed!.AdoptionId).IsEqualTo(routeId);
        await Assert.That(observed.OperationId).IsEqualTo(command.OperationId);
        await Assert.That(observed.Target).IsEqualTo(command.Target);
        await Assert.That(observed.ExpectedQualificationRevision).IsEqualTo(command.ExpectedQualificationRevision);
        await Assert.That(command.AdoptionId).IsEqualTo(bodyId);
    }

    private sealed class Handler<TCommand>(Action<TCommand>? observe = null)
        : ICommandHandler<TCommand, ModerationQualificationResponse>
        where TCommand : ICommand<ModerationQualificationResponse>
    {
        public Task<ModerationQualificationResponse> ExecuteAsync(
            TCommand command, CancellationToken cancellationToken = default)
        {
            observe?.Invoke(command);
            return Task.FromResult(ModerationQualificationResponse.Invalid(["The native handler rejected this request."]));
        }
    }
}
