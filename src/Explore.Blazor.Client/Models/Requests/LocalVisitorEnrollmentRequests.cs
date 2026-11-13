namespace Explore.Blazor.Client.Models.Requests;

public sealed record LocalVisitorEnrollmentSubmission(string Username, string Password, string? Email)
{
    public override string ToString() => nameof(LocalVisitorEnrollmentSubmission);
}

public sealed record LocalVisitorEnrollmentBrowserResult(bool Ok, int Status, string? State, string? LoginPath);
