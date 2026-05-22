namespace LeadScoring.Api.Models;

public class CompanyProductConfig
{
    public Guid Id { get; set; }
    public string CompanyName { get; set; } = string.Empty;
    public string ProductName { get; set; } = string.Empty;
    /// <summary>Optional for legacy rows; new saves require this through the upsert validator.</summary>
    public string? ProductUrl { get; set; }
    public int ProductId { get; set; }
    public string ProductEventConfigJson { get; set; } = "{}";
    public string? StageThresholdsJson { get; set; }
    public DateTime CreatedAtUtc { get; set; }
}
