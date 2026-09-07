using Explore.Application.Contracts.Infrastructure;
using Explore.Application.Contracts.Admissions;
using Explore.Application.Contracts.Persistence;
using Explore.Application.DTOs.RegistrationOrders;
using Explore.Application.Features.RegistrationOrders.Handlers.Commands;
using Explore.Application.Features.RegistrationOrders.Requests.Commands;
using Explore.Application.Responses;
using Explore.Application.Services;
using Explore.Application.Services.Registration;
using Explore.Domain;
using Explore.Domain.Enums;
using NSubstitute;

namespace Event.Application.UnitTests.Features.RegistrationOrders.Commands;

public sealed class RegistrationParticipantCommandHandlerTests
{
    [Test]
    public async Task FamilyBooking_TwoOptionalAdultsAndThreeRequiredChildren_FinalizesExactAdmissions()
    {
        var fixture = new HandlerFixture(2, ParticipantDataCollectionModeEnum.PerTicketOptional, 3, ParticipantDataCollectionModeEnum.PerTicketRequired, childRequiresGuardian: true);
        Guid firstAdult = await fixture.AddParticipantAsync(ParticipantTypeEnum.Adult, null, new ParticipantDetailsDto(null, null, null));
        Guid secondAdult = await fixture.AddParticipantAsync(ParticipantTypeEnum.Adult, null, new ParticipantDetailsDto(null, null, null));
        Guid firstChild = await fixture.AddParticipantAsync(ParticipantTypeEnum.Child, firstAdult, new ParticipantDetailsDto("Child A", null, null));
        Guid secondChild = await fixture.AddParticipantAsync(ParticipantTypeEnum.Child, firstAdult, new ParticipantDetailsDto("Child B", null, null));
        Guid thirdChild = await fixture.AddParticipantAsync(ParticipantTypeEnum.Child, secondAdult, new ParticipantDetailsDto("Child C", null, null));
        BaseCommandResponse<Guid> updated = await fixture.Update.Handle(
            new UpdateRegistrationParticipantCommand(
                fixture.Order.Id, secondAdult, (int)ParticipantTypeEnum.Adult, null, new ParticipantDetailsDto(null, null, null)),
            CancellationToken.None);
        BaseCommandResponse<Guid> singlyAssigned = await fixture.Assign.Handle(
            new AssignRegistrationTicketCommand(fixture.Order.Id, fixture.FirstLine.Id, 1, firstAdult),
            CancellationToken.None);

        BaseCommandResponse<Guid> assigned = await fixture.BulkAssign.Handle(
            new BulkAssignRegistrationTicketsCommand(fixture.Order.Id,
            [
                new(fixture.FirstLine.Id, 2, secondAdult),
                new(fixture.SecondLine.Id, 1, firstChild),
                new(fixture.SecondLine.Id, 2, secondChild),
                new(fixture.SecondLine.Id, 3, thirdChild)
            ]), CancellationToken.None);
        RegistrationOrderLifecycleResponseDto finalized = await fixture.Lifecycle.FinalizeFreeAsync(
            fixture.Order.Id, fixture.TenantId, CancellationToken.None);

        await Assert.That(updated.IsSuccess && singlyAssigned.IsSuccess && assigned.IsSuccess).IsTrue();
        await Assert.That(finalized.IsSuccess).IsTrue();
        await Assert.That(fixture.ParticipantRows).Count().IsEqualTo(5);
        await Assert.That(fixture.AssignmentRows).Count().IsEqualTo(5);
        await Assert.That(fixture.AssignmentRows.Select(value => (value.RegistrationOrderLineId, value.Ordinal)).Distinct()).Count().IsEqualTo(5);
        await Assert.That(fixture.ParticipantRows.Where(value => value.ParticipantTypeId == (int)ParticipantTypeEnum.Adult).All(value => value.Pii is null)).IsTrue();
        await Assert.That(fixture.ParticipantRows.Where(value => value.ParticipantTypeId == (int)ParticipantTypeEnum.Child).All(value => value.GuardianParticipantId.HasValue && !string.IsNullOrWhiteSpace(value.Pii?.DisplayName))).IsTrue();
        await Assert.That(fixture.Admissions).Count().IsEqualTo(5);
        await Assert.That(fixture.Admissions.Select(value => (value.RegistrationOrderLineId, value.EntitlementOrdinal, value.EventSessionId)).Distinct()).Count().IsEqualTo(5);
        await Assert.That((RegistrationOrderStatusEnum)fixture.Order.RegistrationOrderStatusId).IsEqualTo(RegistrationOrderStatusEnum.Confirmed);
    }

    [Test]
    public async Task ConfirmedOptionalAndDeferredAssignments_AmendWithoutDuplicateAdmissions()
    {
        var fixture = new HandlerFixture(
            1,
            ParticipantDataCollectionModeEnum.PerTicketOptional,
            1,
            ParticipantDataCollectionModeEnum.DeferredAssignment,
            bookingPartyType: BookingPartyTypeEnum.Company);
        BaseCommandResponse<Guid> deferred = await fixture.BulkDefer.Handle(
            new BulkDeferRegistrationTicketsCommand(
                fixture.Order.Id,
                [new(fixture.SecondLine.Id, 1)],
                fixture.UtcNow.AddDays(7)), CancellationToken.None);
        RegistrationOrderLifecycleResponseDto finalized = await fixture.Lifecycle.FinalizeFreeAsync(
            fixture.Order.Id, fixture.TenantId, CancellationToken.None);
        Guid optionalAdmissionId = fixture.Admissions.Single().Id;
        string csv = $"registrationOrderLineId,ordinal,participantTypeId,displayName,email,phone\n{fixture.FirstLine.Id},1,{(int)ParticipantTypeEnum.Adult},Optional Person,optional@example.test,\n{fixture.SecondLine.Id},1,{(int)ParticipantTypeEnum.Employee},Employee,employee@example.test,";
        var amendment = new ImportCompanyRegistrationAssignmentsCsvCommand(fixture.EventId, fixture.Order.Id, csv, "company-roster-1");

        BaseCommandResponse<CompanyRegistrationAssignmentCsvResultDto> first = await fixture.ImportCompanyCsv.Handle(amendment, CancellationToken.None);
        Guid replayStamp = fixture.Order.ConcurrencyStamp;
        BaseCommandResponse<CompanyRegistrationAssignmentCsvResultDto> replay = await fixture.ImportCompanyCsv.Handle(amendment, CancellationToken.None);

        await Assert.That(deferred.IsSuccess && finalized.IsSuccess && first.IsSuccess && replay.IsSuccess).IsTrue();
        await Assert.That(fixture.Admissions).Count().IsEqualTo(2);
        await Assert.That(fixture.AmendmentRows).Count().IsEqualTo(2);
        await Assert.That(fixture.Admissions.Single(value => value.RegistrationOrderLineId == fixture.FirstLine.Id).Id).IsEqualTo(optionalAdmissionId);
        await Assert.That(fixture.Admissions.Select(value => (value.RegistrationOrderLineId, value.EntitlementOrdinal, value.EventSessionId)).Distinct()).Count().IsEqualTo(2);
        await Assert.That(replay.Id!.AlreadyApplied).IsTrue();
        await Assert.That(fixture.Admissions.Single(value => value.RegistrationOrderLineId == fixture.FirstLine.Id).RegistrationParticipant.Pii?.DisplayName).IsEqualTo("Optional Person");
        await Assert.That(fixture.Admissions.Single(value => value.RegistrationOrderLineId == fixture.SecondLine.Id).RegistrationParticipant.Pii?.DisplayName).IsEqualTo("Employee");
        await Assert.That(fixture.Order.ConcurrencyStamp).IsEqualTo(replayStamp);
    }

    [Test]
    public async Task CompanyCsv_RejectsFormulaAndMalformedRowsWithoutWrites()
    {
        var fixture = new HandlerFixture(1, ParticipantDataCollectionModeEnum.PerTicketOptional, 1, ParticipantDataCollectionModeEnum.PerTicketOptional);
        BaseCommandResponse<CompanyRegistrationAssignmentCsvResultDto> formula = await fixture.ImportCompanyCsv.Handle(
            new ImportCompanyRegistrationAssignmentsCsvCommand(
                fixture.EventId,
                fixture.Order.Id,
                $"registrationOrderLineId,ordinal,participantTypeId,displayName,email,phone\n={fixture.FirstLine.Id},1,{(int)ParticipantTypeEnum.Adult},Name,,",
                "import-1"), CancellationToken.None);
        BaseCommandResponse<CompanyRegistrationAssignmentCsvResultDto> malformed = await fixture.ImportCompanyCsv.Handle(
            new ImportCompanyRegistrationAssignmentsCsvCommand(
                fixture.EventId,
                fixture.Order.Id,
                $"registrationOrderLineId,ordinal,participantTypeId,displayName,email,phone\n{fixture.FirstLine.Id},1",
                "import-2"), CancellationToken.None);

        await Assert.That(formula.IsSuccess || malformed.IsSuccess).IsFalse();
        await Assert.That(fixture.AssignmentRows).IsEmpty();
        await Assert.That(fixture.AmendmentRows).IsEmpty();
    }

    [Test]
    public async Task BulkPayload_RejectsDuplicateOrdinalCrossOrderParticipantAndExpiredDeadlineWithoutWrites()
    {
        var fixture = new HandlerFixture(2, ParticipantDataCollectionModeEnum.PerTicketOptional, 1, ParticipantDataCollectionModeEnum.DeferredAssignment);
        Guid participant = await fixture.AddParticipantAsync(ParticipantTypeEnum.Adult, null, new ParticipantDetailsDto(null, null, null));
        RegistrationParticipant otherOrderParticipant = RegistrationParticipant.Create(
            fixture.TenantId, Guid.CreateVersion7(), null, ParticipantTypeEnum.Adult, null);
        fixture.ParticipantRows.Add(otherOrderParticipant);
        int originalAssignments = fixture.AssignmentRows.Count;

        BaseCommandResponse<Guid> duplicate = await fixture.BulkAssign.Handle(new BulkAssignRegistrationTicketsCommand(
            fixture.Order.Id, [new(fixture.FirstLine.Id, 1, participant), new(fixture.FirstLine.Id, 1, participant)]), CancellationToken.None);
        BaseCommandResponse<Guid> crossOrder = await fixture.Assign.Handle(new AssignRegistrationTicketCommand(
            fixture.Order.Id, fixture.FirstLine.Id, 1, otherOrderParticipant.Id), CancellationToken.None);
        BaseCommandResponse<Guid> expired = await fixture.Defer.Handle(new DeferRegistrationTicketCommand(
            fixture.Order.Id, fixture.SecondLine.Id, 1, fixture.UtcNow), CancellationToken.None);

        await Assert.That(duplicate.IsSuccess || crossOrder.IsSuccess || expired.IsSuccess).IsFalse();
        await Assert.That(fixture.AssignmentRows).Count().IsEqualTo(originalAssignments);
        await Assert.That(fixture.Admissions).IsEmpty();
    }

    [Test]
    public async Task AssignmentMutation_UsesSerializableTransactionAndRejectsOrdinalOutsideLineQuantity()
    {
        var fixture = new HandlerFixture(
            1,
            ParticipantDataCollectionModeEnum.PerTicketOptional,
            1,
            ParticipantDataCollectionModeEnum.PerTicketOptional);
        Guid participant = await fixture.AddParticipantAsync(
            ParticipantTypeEnum.Adult,
            null,
            new ParticipantDetailsDto(null, null, null));

        BaseCommandResponse<Guid> response = await fixture.Assign.Handle(
            new AssignRegistrationTicketCommand(fixture.Order.Id, fixture.FirstLine.Id, 2, participant),
            CancellationToken.None);

        await Assert.That(response.IsSuccess).IsFalse();
        await Assert.That(fixture.AssignmentRows).IsEmpty();
        await Assert.That(fixture.SerializableExecutions).IsEqualTo(1);
    }

    [Test]
    public async Task CompanyCsvImport_ValidatesWholeBatchAndCreatesAmendmentsWithAdmissionsAtomically()
    {
        var fixture = new HandlerFixture(
            2,
            ParticipantDataCollectionModeEnum.DeferredAssignment,
            1,
            ParticipantDataCollectionModeEnum.PerTicketOptional,
            bookingPartyType: BookingPartyTypeEnum.Company);
        await fixture.BulkDefer.Handle(new BulkDeferRegistrationTicketsCommand(
            fixture.Order.Id,
            [new(fixture.FirstLine.Id, 1), new(fixture.FirstLine.Id, 2)],
            fixture.UtcNow.AddDays(7)), CancellationToken.None);
        RegistrationOrderLifecycleResponseDto finalized = await fixture.Lifecycle.FinalizeFreeAsync(
            fixture.Order.Id, fixture.TenantId, CancellationToken.None);
        string csv = string.Join('\n',
            "registrationOrderLineId,ordinal,participantTypeId,displayName,email,phone",
            $"{fixture.FirstLine.Id},1,{(int)ParticipantTypeEnum.Employee},Employee One,one@example.test,",
            $"{fixture.FirstLine.Id},2,{(int)ParticipantTypeEnum.Employee},Employee Two,two@example.test,");

        BaseCommandResponse<CompanyRegistrationAssignmentCsvResultDto> imported = await fixture.ImportCompanyCsv.Handle(
            new ImportCompanyRegistrationAssignmentsCsvCommand(fixture.EventId, fixture.Order.Id, csv, "import-001"), CancellationToken.None);
        BaseCommandResponse<CompanyRegistrationAssignmentCsvResultDto> replayed = await fixture.ImportCompanyCsv.Handle(
            new ImportCompanyRegistrationAssignmentsCsvCommand(fixture.EventId, fixture.Order.Id, csv, "import-001"), CancellationToken.None);

        await Assert.That(finalized.IsSuccess && imported.IsSuccess && replayed.IsSuccess).IsTrue();
        await Assert.That(imported.Id!.AssignmentCount).IsEqualTo(2);
        await Assert.That(replayed.Id!.AlreadyApplied).IsTrue();
        await Assert.That(fixture.ParticipantRows.Where(value => value.ParticipantTypeId == (int)ParticipantTypeEnum.Employee)).Count().IsEqualTo(2);
        await Assert.That(fixture.AssignmentRows).Count().IsEqualTo(2);
        await Assert.That(fixture.Admissions.Where(value => value.RegistrationOrderLineId == fixture.FirstLine.Id)).Count().IsEqualTo(2);
        await Assert.That(fixture.AmendmentRows.Select(value => (value.Source, value.LineageKey)).Distinct()).IsEquivalentTo([("company-csv", "import-001")]);
    }

    [Test]
    public async Task CompanyCsvImport_InvalidRowWritesNothing()
    {
        var fixture = new HandlerFixture(
            1,
            ParticipantDataCollectionModeEnum.DeferredAssignment,
            1,
            ParticipantDataCollectionModeEnum.PerTicketOptional,
            bookingPartyType: BookingPartyTypeEnum.Company);
        await fixture.BulkDefer.Handle(new BulkDeferRegistrationTicketsCommand(
            fixture.Order.Id,
            [new(fixture.FirstLine.Id, 1)],
            fixture.UtcNow.AddDays(7)), CancellationToken.None);
        await fixture.Lifecycle.FinalizeFreeAsync(fixture.Order.Id, fixture.TenantId, CancellationToken.None);
        int originalAssignmentCount = fixture.AssignmentRows.Count;
        string csv = string.Join('\n',
            "registrationOrderLineId,ordinal,participantTypeId,displayName,email,phone",
            $"{fixture.FirstLine.Id},1,{(int)ParticipantTypeEnum.Employee},Employee One,one@example.test,",
            $"{fixture.SecondLine.Id},2,{(int)ParticipantTypeEnum.Employee},Employee Two,two@example.test,");

        BaseCommandResponse<CompanyRegistrationAssignmentCsvResultDto> imported = await fixture.ImportCompanyCsv.Handle(
            new ImportCompanyRegistrationAssignmentsCsvCommand(fixture.EventId, fixture.Order.Id, csv, "import-002"), CancellationToken.None);

        await Assert.That(imported.IsSuccess).IsFalse();
        await Assert.That(fixture.ParticipantRows.Where(value => value.ParticipantTypeId == (int)ParticipantTypeEnum.Employee)).IsEmpty();
        await Assert.That(fixture.AssignmentRows).Count().IsEqualTo(originalAssignmentCount);
        await Assert.That(fixture.AmendmentRows).IsEmpty();
    }

    [Test]
    public async Task CompanyCsvImport_DuplicateAssignmentKeyWritesNothing()
    {
        var fixture = new HandlerFixture(
            1,
            ParticipantDataCollectionModeEnum.DeferredAssignment,
            1,
            ParticipantDataCollectionModeEnum.PerTicketOptional,
            bookingPartyType: BookingPartyTypeEnum.Company);
        await fixture.BulkDefer.Handle(new BulkDeferRegistrationTicketsCommand(
            fixture.Order.Id,
            [new(fixture.FirstLine.Id, 1)],
            fixture.UtcNow.AddDays(7)), CancellationToken.None);
        await fixture.Lifecycle.FinalizeFreeAsync(fixture.Order.Id, fixture.TenantId, CancellationToken.None);
        int originalAssignmentCount = fixture.AssignmentRows.Count;
        string csv = string.Join('\n',
            "registrationOrderLineId,ordinal,participantTypeId,displayName,email,phone",
            $"{fixture.FirstLine.Id},1,{(int)ParticipantTypeEnum.Employee},Employee One,one@example.test,",
            $"{fixture.FirstLine.Id},1,{(int)ParticipantTypeEnum.Employee},Employee Two,two@example.test,");

        BaseCommandResponse<CompanyRegistrationAssignmentCsvResultDto> imported = await fixture.ImportCompanyCsv.Handle(
            new ImportCompanyRegistrationAssignmentsCsvCommand(fixture.EventId, fixture.Order.Id, csv, "import-duplicate"), CancellationToken.None);

        await Assert.That(imported.IsSuccess).IsFalse();
        await Assert.That(fixture.ParticipantRows.Where(value => value.ParticipantTypeId == (int)ParticipantTypeEnum.Employee)).IsEmpty();
        await Assert.That(fixture.AssignmentRows).Count().IsEqualTo(originalAssignmentCount);
        await Assert.That(fixture.AmendmentRows).IsEmpty();
    }

    private sealed class HandlerFixture
    {
        private readonly IRegistrationInventoryRepository _inventory = Substitute.For<IRegistrationInventoryRepository>();
        private readonly IRegistrationParticipantRepository _participants = Substitute.For<IRegistrationParticipantRepository>();
        private readonly IEventTicketCatalogRepository _catalogs = Substitute.For<IEventTicketCatalogRepository>();
        private readonly IEventSessionRepository _sessions = Substitute.For<IEventSessionRepository>();
        private readonly ICurrentUserService _currentUser = Substitute.For<ICurrentUserService>();
        private readonly IPlatformContributionSettingRepository _contributions = Substitute.For<IPlatformContributionSettingRepository>();
        private readonly IOutboxRepository _outbox = Substitute.For<IOutboxRepository>();
        private readonly IRegistrationFinalizationRepository _finalization = Substitute.For<IRegistrationFinalizationRepository>();
        private readonly IRegistrationOrderTransitionCoordinator _transitions =
            Substitute.For<IRegistrationOrderTransitionCoordinator>();
        private readonly InlineUnitOfWork _unitOfWork = new();

        public HandlerFixture(
            int firstQuantity,
            ParticipantDataCollectionModeEnum firstMode,
            int secondQuantity,
            ParticipantDataCollectionModeEnum secondMode,
            bool childRequiresGuardian = false,
            BookingPartyTypeEnum bookingPartyType = BookingPartyTypeEnum.Household)
        {
            TenantId = Guid.CreateVersion7();
            EventId = Guid.CreateVersion7();
            UtcNow = new DateTime(2026, 7, 31, 12, 0, 0, DateTimeKind.Utc);
            Catalog = EventTicketCatalogVersion.Create(TenantId, EventId, "USD", 1);
            EventTicketType firstTicket = CreateTicket("Adult", firstMode, false);
            EventTicketType secondTicket = CreateTicket("Child", secondMode, childRequiresGuardian);
            Catalog.AddTicketType(firstTicket, null);
            Catalog.AddTicketType(secondTicket, null);
            Catalog.AddEntitlement(firstTicket, TicketTypeEntitlement.CreateForEvent(firstTicket.Id, TenantId, EventId, 1));
            Catalog.AddEntitlement(secondTicket, TicketTypeEntitlement.CreateForEvent(secondTicket.Id, TenantId, EventId, 1));
            Catalog.Publish();
            Order = RegistrationOrder.Create(
                TenantId, EventId, Guid.CreateVersion7(), null, bookingPartyType, Catalog.Id,
                RegistrationParticipationSnapshot.Create(Guid.CreateVersion7(), 4, 3, 2, GuestRecoveryPolicyEnum.VerifiedEmailRequired),
                null, null, "USD", UtcNow, UtcNow.AddMinutes(15));
            FirstLine = RegistrationOrderLine.Create(Catalog, firstTicket, Order.Id, firstQuantity, null, null);
            SecondLine = RegistrationOrderLine.Create(Catalog, secondTicket, Order.Id, secondQuantity, null, null);
            Order.AddLine(FirstLine);
            Order.AddLine(SecondLine);
            Order.ApplyTotals(RegistrationOrderTotalsSnapshot.Create("USD", 0, 0, 0, 0));
            MoveToReady(Order, UtcNow);
            EventSession session = new()
            {
                Id = Guid.CreateVersion7(),
                EventId = EventId,
                Event = null!,
                TenantId = TenantId,
                Tenant = null!,
                RegistrationModeId = (int)RegistrationModeEnum.Open
            };
            Sessions = [session];
            ConfigureSubstitutes();
            _currentUser.UserId.Returns(Guid.CreateVersion7());
            _currentUser.IsAuthenticated.Returns(true);
            var commandService = new RegistrationParticipantCommandService(
                _inventory,
                _participants,
                _catalogs,
                _sessions,
                Substitute.For<
                    IParticipantAdmissionEligibilityRepository>(),
                _currentUser,
                new TenantContext(TenantId),
                _unitOfWork,
                new FixedTimeProvider(UtcNow));
            Add = new AddRegistrationParticipantCommandHandler(commandService);
            Update = new UpdateRegistrationParticipantCommandHandler(commandService);
            Assign = new AssignRegistrationTicketCommandHandler(commandService);
            BulkAssign = new BulkAssignRegistrationTicketsCommandHandler(commandService);
            ImportCompanyCsv = new ImportCompanyRegistrationAssignmentsCsvCommandHandler(commandService);
            Defer = new DeferRegistrationTicketCommandHandler(commandService);
            BulkDefer = new BulkDeferRegistrationTicketsCommandHandler(commandService);
            Lifecycle = new RegistrationOrderLifecycleService(
                _inventory, Substitute.For<IPromotionRedemptionRepository>(), _participants, _catalogs, _contributions, _sessions, _outbox, _unitOfWork, _finalization,
                Substitute.For<IRegistrationPaymentAttemptRepository>(),
                new NoOpScheduledDeadlineDispatcher(),
                new FixedTimeProvider(UtcNow),
                Substitute.For<IPaidOrderAcceptanceService>(),
                _transitions);
        }

        public Guid TenantId { get; }
        public Guid EventId { get; }
        public DateTime UtcNow { get; }
        public EventTicketCatalogVersion Catalog { get; }
        public RegistrationOrder Order { get; }
        public RegistrationOrderLine FirstLine { get; }
        public RegistrationOrderLine SecondLine { get; }
        public List<EventSession> Sessions { get; }
        public List<RegistrationParticipant> ParticipantRows { get; } = [];
        public List<RegistrationTicketAssignment> AssignmentRows { get; } = [];
        public List<RegistrationAmendment> AmendmentRows { get; } = [];
        public List<EventRegistration> Admissions { get; } = [];
        public AddRegistrationParticipantCommandHandler Add { get; }
        public UpdateRegistrationParticipantCommandHandler Update { get; }
        public AssignRegistrationTicketCommandHandler Assign { get; }
        public BulkAssignRegistrationTicketsCommandHandler BulkAssign { get; }
        public ImportCompanyRegistrationAssignmentsCsvCommandHandler ImportCompanyCsv { get; }
        public DeferRegistrationTicketCommandHandler Defer { get; }
        public BulkDeferRegistrationTicketsCommandHandler BulkDefer { get; }
        public RegistrationOrderLifecycleService Lifecycle { get; }
        public int SerializableExecutions => _unitOfWork.SerializableExecutions;

        public async Task<Guid> AddParticipantAsync(ParticipantTypeEnum type, Guid? guardianId, ParticipantDetailsDto details)
        {
            BaseCommandResponse<Guid> response = await Add.Handle(
                new AddRegistrationParticipantCommand(Order.Id, (int)type, guardianId, details), CancellationToken.None);
            await Assert.That(response.IsSuccess).IsTrue();
            return response.Id;
        }

        private EventTicketType CreateTicket(string name, ParticipantDataCollectionModeEnum mode, bool requiresGuardian) =>
            EventTicketType.Create(Guid.CreateVersion7(), TenantId, Catalog.Id, name, "USD", TicketPricingModeEnum.Free,
                null, null, null, mode, null, null, null, requiresGuardian, false, null, null, null, null);

        private void ConfigureSubstitutes()
        {
            _inventory.GetOrderByIdAsync(Order.Id, TenantId, Arg.Any<CancellationToken>()).Returns(Order);
            _inventory.GetOrderWithLinesAsync(Order.Id, TenantId, Arg.Any<CancellationToken>()).Returns(Order);
            _inventory.GetOrderForUpdateWithLinesAsync(Order.Id, TenantId, Arg.Any<CancellationToken>()).Returns(Order);
            _inventory.GetHoldsByOrderAsync(Order.Id, TenantId, Arg.Any<CancellationToken>()).Returns([]);
            _inventory.TryConsumeActiveHoldsForOrderAsync(Order.Id, TenantId, UtcNow, Arg.Any<CancellationToken>()).Returns(0);
            _transitions.PersistAsync(Order.Id, TenantId, Arg.Any<RegistrationOrderStatusEnum>(), Arg.Any<RegistrationOrderStatusEnum>(), UtcNow, Arg.Any<CancellationToken>())
                .Returns(call =>
                {
                    RegistrationOrderStatusEnum expected = call.ArgAt<RegistrationOrderStatusEnum>(2);
                    RegistrationOrderStatusEnum desired = call.ArgAt<RegistrationOrderStatusEnum>(3);
                    if (Order.RegistrationOrderStatusId != (int)expected) return false;
                    Order.TransitionTo(desired, UtcNow);
                    return true;
                });
            _inventory.AddEventRegistrationsAsync(Arg.Any<IReadOnlyCollection<EventRegistration>>(), Arg.Any<CancellationToken>())
                .Returns(call => { Admissions.AddRange(call.ArgAt<IReadOnlyCollection<EventRegistration>>(0)); return Task.CompletedTask; });
            _catalogs.GetOrderCatalogAsync(Catalog.Id, EventId, TenantId, Arg.Any<CancellationToken>()).Returns(Catalog);
            _sessions.GetSessionsByEvent(EventId).Returns(Sessions);
            _participants.GetParticipantForUpdateAsync(Arg.Any<Guid>(), Order.Id, TenantId, Arg.Any<CancellationToken>())
                .Returns(call => ParticipantRows.SingleOrDefault(value => value.Id == call.ArgAt<Guid>(0) && value.RegistrationOrderId == Order.Id));
            _participants.GetParticipantsByOrderAsync(Order.Id, TenantId, Arg.Any<CancellationToken>()).Returns(ParticipantRows);
            _participants.GetAssignmentsWithParticipantsByOrderAsync(Order.Id, TenantId, Arg.Any<CancellationToken>()).Returns(AssignmentRows);
            _participants.GetAssignmentsForUpdateByOrderAsync(Order.Id, TenantId, Arg.Any<CancellationToken>()).Returns(AssignmentRows);
            _participants.GetAdmissionsForUpdateAsync(Order.Id, Arg.Any<Guid>(), Arg.Any<int>(), TenantId, Arg.Any<CancellationToken>())
                .Returns(call => Admissions.Where(value => value.RegistrationOrderLineId == call.ArgAt<Guid>(1) && value.EntitlementOrdinal == call.ArgAt<int>(2)).ToArray());
            _participants.HasCompanyCsvAmendmentAsync(Order.Id, TenantId, Arg.Any<string>(), Arg.Any<CancellationToken>())
                .Returns(call => AmendmentRows.Any(value => value.Source == "company-csv" && value.LineageKey == call.ArgAt<string>(2)));
            _participants.AddParticipantAsync(Arg.Any<RegistrationParticipant>(), Arg.Any<CancellationToken>())
                .Returns(call => { ParticipantRows.Add(call.ArgAt<RegistrationParticipant>(0)); return Task.CompletedTask; });
            _participants.AddAssignmentsAsync(Arg.Any<IReadOnlyCollection<RegistrationTicketAssignment>>(), Arg.Any<CancellationToken>())
                .Returns(call => { AssignmentRows.AddRange(call.ArgAt<IReadOnlyCollection<RegistrationTicketAssignment>>(0)); return Task.CompletedTask; });
            _participants.AddAmendmentsAsync(Arg.Any<IReadOnlyCollection<RegistrationAmendment>>(), Arg.Any<CancellationToken>())
                .Returns(call => { AmendmentRows.AddRange(call.ArgAt<IReadOnlyCollection<RegistrationAmendment>>(0)); return Task.CompletedTask; });
            _participants.AddParticipantsAsync(Arg.Any<IReadOnlyCollection<RegistrationParticipant>>(), Arg.Any<CancellationToken>())
                .Returns(call => { ParticipantRows.AddRange(call.ArgAt<IReadOnlyCollection<RegistrationParticipant>>(0)); return Task.CompletedTask; });
            _participants.SaveChangesAsync(Arg.Any<CancellationToken>()).Returns(Task.CompletedTask);
            _outbox.Create(Arg.Any<OutboxMessage>()).Returns(call => Task.FromResult(call.ArgAt<OutboxMessage>(0)));
        }

        private static void MoveToReady(RegistrationOrder order, DateTime now)
        {
            order.TransitionTo(RegistrationOrderStatusEnum.AwaitingParticipantDetails, now);
            order.TransitionTo(RegistrationOrderStatusEnum.AwaitingRequirements, now);
            order.TransitionTo(RegistrationOrderStatusEnum.ReadyForCheckout, now);
        }
    }

    private sealed record TenantContext(Guid TenantId) : ITenantContext;

    private sealed class FixedTimeProvider(DateTime utcNow) : TimeProvider
    {
        public override DateTimeOffset GetUtcNow() => new(utcNow);
    }

    private sealed class InlineUnitOfWork : IUnitOfWork
    {
        public int SerializableExecutions { get; private set; }

        public Task ExecuteInTransactionAsync(Func<CancellationToken, Task> operation, CancellationToken ct = default) => operation(ct);
        public Task<T> ExecuteInTransactionAsync<T>(Func<CancellationToken, Task<T>> operation, CancellationToken ct = default) => operation(ct);
        public Task<T> ExecuteSerializableAsync<T>(Func<CancellationToken, Task<T>> operation, CancellationToken ct = default)
        {
            SerializableExecutions++;
            return operation(ct);
        }
    }
}
