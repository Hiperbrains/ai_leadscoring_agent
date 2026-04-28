using LeadScoring.Api.Models;

namespace LeadScoring.Api.Repositories;

public interface IBatchRepository
{
    Task<List<BatchConfig>> GetActiveConfigsAsync(CancellationToken cancellationToken);
    Task<DateTime?> GetLastSuccessfulBatchEndTimeAsync(int productId, BatchType batchType, CancellationToken cancellationToken);
    Task<Batch?> GetBatchByIdAsync(long batchId, CancellationToken cancellationToken);
    Task<List<Lead>> GetLeadsAfterAsync(int productId, DateTime sinceUtc, CancellationToken cancellationToken);
    Task<Batch> CreateBatchAsync(Batch batch, CancellationToken cancellationToken);
    Task CreateBatchLeadsAsync(IEnumerable<BatchLead> batchLeads, CancellationToken cancellationToken);
    Task<List<BatchLead>> GetFailedBatchLeadsAsync(long batchId, CancellationToken cancellationToken);
    Task SaveChangesAsync(CancellationToken cancellationToken);
}
