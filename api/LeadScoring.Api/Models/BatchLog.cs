namespace LeadScoring.Api.Models;

public class BatchLog
{
    public long BatchId { get; set; }
    public DateTime RunDate { get; set; }
    public CampaignBatchType BatchType { get; set; }
    public int TotalLeadsProcessed { get; set; }
    public int SuccessCount { get; set; }
    public int FailureCount { get; set; }
    /// <summary>
    /// Company the batch was sent for. Nullable for legacy rows logged before company scoping shipped.
    /// </summary>
    public string? CompanyName { get; set; }
    /// <summary>
    /// Product the batch was sent for. Nullable for backward compatibility with rows logged
    /// before the global product navbar shipped (they will display "&mdash;" in the UI).
    /// </summary>
    public int? ProductId { get; set; }
}
