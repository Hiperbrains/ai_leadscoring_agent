using LeadScoring.Api.Contracts;

namespace LeadScoring.Api.Services;

public interface IBatchProcessingService
{
    Task ProcessActiveConfigsAsync(CancellationToken cancellationToken);
    Task<BatchRetryResultDto> RetryFailedLeadsAsync(long sourceBatchId, CancellationToken cancellationToken);
}
