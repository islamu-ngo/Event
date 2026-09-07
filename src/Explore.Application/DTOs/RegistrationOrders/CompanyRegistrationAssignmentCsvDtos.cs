namespace Explore.Application.DTOs.RegistrationOrders;

public sealed record CompanyRegistrationAssignmentCsvInputDto(string CsvUtf8, string LineageKey);

public sealed record CompanyRegistrationAssignmentCsvResultDto(Guid RegistrationOrderId, int AssignmentCount, bool AlreadyApplied);
