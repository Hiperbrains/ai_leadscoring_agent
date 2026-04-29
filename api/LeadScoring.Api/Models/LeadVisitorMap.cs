namespace LeadScoring.Api.Models;

public class LeadVisitorMap
{
    public long Id { get; set; }
    public Guid LeadId { get; set; }
    public string VisitorId { get; set; } = string.Empty;
    public string Email { get; set; } = string.Empty;
    public string? FirstName { get; set; }
    public string? LastName { get; set; }
    public string? PhoneNumber { get; set; }
    public string? CompanyName { get; set; }
    public string? Country { get; set; }
    public string? Notes { get; set; }
    public DateTime CreatedAtUtc { get; set; }

    public Lead Lead { get; set; } = null!;
}
