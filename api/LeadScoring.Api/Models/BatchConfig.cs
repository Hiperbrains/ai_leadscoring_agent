namespace LeadScoring.Api.Models;

public class BatchConfig
{
    public long ConfigId { get; set; }
    public int ProductId { get; set; }
    public BatchType BatchType { get; set; }
    public bool IsActive { get; set; }
    public DateTime CreatedAt { get; set; }
    public DateTime UpdatedAt { get; set; }
}
