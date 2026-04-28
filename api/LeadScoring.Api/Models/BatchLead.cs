namespace LeadScoring.Api.Models;

public class BatchLead
{
    public long BatchLeadId { get; set; }
    public long BatchId { get; set; }
    public Guid LeadId { get; set; }
    public BatchLeadStatus Status { get; set; } = BatchLeadStatus.Pending;
    public string? ErrorMessage { get; set; }
    public int RetryCount { get; set; }
    public DateTime? ProcessedAt { get; set; }

    public Batch Batch { get; set; } = default!;
    public Lead Lead { get; set; } = default!;
}
