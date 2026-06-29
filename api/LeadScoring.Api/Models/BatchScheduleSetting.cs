namespace LeadScoring.Api.Models;

public class BatchScheduleSetting
{
    public long Id { get; set; }
    public string CompanyName { get; set; } = string.Empty;
    public int ProductId { get; set; }
    public CampaignBatchType BatchType { get; set; }
    /// <summary>Daily run time stored as UTC wall-clock (HH:mm:ss).</summary>
    public TimeSpan DailyRunTimeUtc { get; set; }
    public bool IsEnabled { get; set; } = true;
    public DateTime UpdatedAtUtc { get; set; }
}
