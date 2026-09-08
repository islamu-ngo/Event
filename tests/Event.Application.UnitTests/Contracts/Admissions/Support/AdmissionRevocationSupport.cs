using Explore.Application.Contracts.Admissions;
using Explore.Application.Services.Registration;

namespace ApplicationUnitTests.Contracts.Admissions.Support;

internal sealed record AdmissionAllocationSeed(
    Guid LineId,
    bool IsAdmissionLine,
    long RefundedMinor,
    long RelevantLineTotalMinor);

internal sealed record AdmissionRevocationRow(
    IReadOnlyList<AdmissionAllocationSeed> Allocations,
    bool Cancellation,
    bool Invalid,
    Guid[] ExpectedRevoked,
    Guid[] ExpectedPreserved)
{
    private static readonly Guid FirstLine = Guid.Parse("018e4e5c-7f00-7000-8000-000000000101");
    private static readonly Guid SecondLine = Guid.Parse("018e4e5c-7f00-7000-8000-000000000102");
    private static readonly Guid AddOnLine = Guid.Parse("018e4e5c-7f00-7000-8000-000000000103");

    internal static AdmissionAssignmentSeed[] Assignments() =>
    [
        new(Guid.CreateVersion7(), FirstLine, Guid.CreateVersion7(), 500, true),
        new(Guid.CreateVersion7(), FirstLine, Guid.CreateVersion7(), 500, true),
        new(Guid.CreateVersion7(), SecondLine, Guid.CreateVersion7(), 500, true)
    ];

    internal static AdmissionRevocationRow For(string matrixCase, AdmissionTestScenario scenario)
    {
        Guid[] first = scenario.TicketIdsForLine(FirstLine);
        Guid[] second = scenario.TicketIdsForLine(SecondLine);
        Guid[] all = first.Concat(second).ToArray();
        return matrixCase switch
        {
            "full-relevant" => Row([new(FirstLine, true, 1_000, 1_000)], first, second),
            "one-relevant-partial" => Row([new(FirstLine, true, 999, 1_000)], [], all),
            "add-on-only" => Row([new(AddOnLine, false, 300, 0)], [], all),
            "mixed-full-ticket-and-addon" => Row(
                [new(FirstLine, true, 1_000, 1_000), new(AddOnLine, false, 300, 0)], first, second),
            "multiple-ticket-lines-partial" => Row(
                [new(FirstLine, true, 500, 1_000), new(SecondLine, true, 250, 500)], [], all),
            "zero-relevant" => Row([new(FirstLine, true, 0, 1_000)], [], all),
            "zero-over-zero" => Row([new(FirstLine, true, 0, 0)], [], all),
            "negative" => Row([new(FirstLine, true, -1, 1_000)], [], all, invalid: true),
            "over-allocation" => Row([new(FirstLine, true, 1_001, 1_000)], [], all, invalid: true),
            "cancellation" => new([], true, false, all, []),
            _ => throw new ArgumentOutOfRangeException(nameof(matrixCase), matrixCase, null)
        };
    }

    private static AdmissionRevocationRow Row(
        IReadOnlyList<AdmissionAllocationSeed> allocations,
        Guid[] revoked,
        Guid[] preserved,
        bool invalid = false) => new(allocations, false, invalid, revoked, preserved);
}

internal static class AdmissionRevocationPorts
{
    internal static AdmissionRevocationService TypedService(AdmissionTestScenario scenario) => new(
        new RevocationRepositoryFake(scenario),
        scenario.UnitOfWork,
        scenario.Clock);

    internal static AdmissionRevocationRequest TypedRequest(
        AdmissionTestScenario scenario,
        AdmissionRevocationRow row) => new(
        scenario.TenantId,
        scenario.OrderId,
        row.Cancellation
            ? AdmissionRevocationService.OrderCancellationReason
            : AdmissionRevocationService.RefundReconciledReason,
        row.Allocations.Select(value => new AdmissionRefundAllocationFact(
            value.LineId,
            value.IsAdmissionLine,
            value.RefundedMinor,
            value.RelevantLineTotalMinor)).ToArray());
}

internal sealed class RevocationRepositoryFake(AdmissionTestScenario scenario) : IAdmissionRevocationRepository
{
    public Task<AdmissionRevocationContext?> LoadAsync(
        AdmissionRevocationRequest request,
        CancellationToken cancellationToken) => Task.FromResult<AdmissionRevocationContext?>(new(
        scenario.TenantId,
        scenario.OrderId,
        scenario.TicketsByAssignment.Values.ToArray()));

    public Task<AdmissionRevocationResult> ApplyAsync(
        AdmissionRevocationPersistenceRequest request,
        CancellationToken cancellationToken)
    {
        scenario.RevocationWriteCalls++;
        return Task.FromResult(new AdmissionRevocationResult(
            AdmissionRevocationOutcome.Applied,
            request.RevokedTicketIds,
            request.PreservedTicketIds));
    }
}
