namespace LeadScoring.Api.Models;

public class Batch
{
    public long BatchId { get; set; }
    public int ProductId { get; set; }
    public BatchType BatchType { get; set; }
    public DateTime StartTime { get; set; }
    public DateTime? EndTime { get; set; }
    public BatchStatus Status { get; set; } = BatchStatus.Running;
    public int TotalLeads { get; set; }
    public int SuccessCount { get; set; }
    public int FailedCount { get; set; }
    public DateTime CreatedAt { get; set; }

    public ICollection<BatchLead> BatchLeads { get; set; } = new List<BatchLead>();
}
