namespace Explore.Domain;

public class IdempotencyRecord
{
    public const int InProgressStatusCode = 0;

    public Guid Id { get; set; }
    public string Key { get; set; } = string.Empty;
    public Guid TenantId { get; set; }
    public string? UserId { get; set; }
    public string RequestMethod { get; set; } = string.Empty;
    public string RequestTarget { get; set; } = string.Empty;
    public string? RequestContentType { get; set; }
    public string RequestBodyHash { get; set; } = string.Empty;
    public string PrincipalFingerprint { get; set; } = string.Empty;
    public int StatusCode { get; set; }
    public string? ResponseBody { get; set; }
    public string? ContentType { get; set; }
    public DateTime CreatedAt { get; set; }
    public DateTime ExpiresAt { get; set; }
}
