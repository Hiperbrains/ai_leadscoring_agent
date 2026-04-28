using LeadScoring.Api.Contracts;
using LeadScoring.Api.Models;
using LeadScoring.Api.Repositories;

namespace LeadScoring.Api.Services;

public class BatchProcessingService(
    IBatchRepository batchRepository,
    ILogger<BatchProcessingService> logger) : IBatchProcessingService
{
    private static readonly DateTime DefaultStartDate = new(1900, 1, 1, 0, 0, 0, DateTimeKind.Utc);

    public async Task ProcessActiveConfigsAsync(CancellationToken cancellationToken)
    {
        var activeConfigs = await batchRepository.GetActiveConfigsAsync(cancellationToken);
        foreach (var config in activeConfigs)
        {
            await ProcessConfigAsync(config, cancellationToken);
        }
    }

    public async Task<BatchRetryResultDto> RetryFailedLeadsAsync(long sourceBatchId, CancellationToken cancellationToken)
    {
        var sourceBatch = await batchRepository.GetBatchByIdAsync(sourceBatchId, cancellationToken);
        if (sourceBatch is null)
        {
            throw new InvalidOperationException($"Batch {sourceBatchId} not found.");
        }

        var failedLeads = await batchRepository.GetFailedBatchLeadsAsync(sourceBatchId, cancellationToken);
        if (failedLeads.Count == 0)
        {
            throw new InvalidOperationException($"Batch {sourceBatchId} has no failed leads to retry.");
        }

        var retryBatch = new Batch
        {
            ProductId = sourceBatch.ProductId,
            BatchType = sourceBatch.BatchType,
            StartTime = DateTime.UtcNow,
            Status = BatchStatus.Running,
            CreatedAt = DateTime.UtcNow
        };
        await batchRepository.CreateBatchAsync(retryBatch, cancellationToken);

        var retryBatchLeads = failedLeads.Select(x => new BatchLead
        {
            BatchId = retryBatch.BatchId,
            LeadId = x.LeadId,
            Status = BatchLeadStatus.Pending,
            RetryCount = x.RetryCount + 1
        }).ToList();
        await batchRepository.CreateBatchLeadsAsync(retryBatchLeads, cancellationToken);

        var successCount = 0;
        var failedCount = 0;
        foreach (var batchLead in retryBatchLeads)
        {
            var result = await ProcessLeadAsync(batchLead.LeadId, cancellationToken);
            if (result.IsSuccess)
            {
                batchLead.Status = BatchLeadStatus.Success;
                batchLead.ErrorMessage = null;
                successCount++;
            }
            else
            {
                batchLead.Status = BatchLeadStatus.Failed;
                batchLead.ErrorMessage = result.ErrorMessage;
                failedCount++;
            }

            batchLead.ProcessedAt = DateTime.UtcNow;
        }

        retryBatch.TotalLeads = retryBatchLeads.Count;
        retryBatch.SuccessCount = successCount;
        retryBatch.FailedCount = failedCount;
        retryBatch.Status = failedCount > 0 ? BatchStatus.Failed : BatchStatus.Completed;
        retryBatch.EndTime = DateTime.UtcNow;
        await batchRepository.SaveChangesAsync(cancellationToken);

        return new BatchRetryResultDto(
            sourceBatchId,
            retryBatch.BatchId,
            retryBatch.TotalLeads,
            retryBatch.SuccessCount,
            retryBatch.FailedCount,
            retryBatch.Status);
    }

    private async Task ProcessConfigAsync(BatchConfig config, CancellationToken cancellationToken)
    {
        try
        {
            var lastRun = await batchRepository.GetLastSuccessfulBatchEndTimeAsync(config.ProductId, config.BatchType, cancellationToken);
            var leads = await batchRepository.GetLeadsAfterAsync(config.ProductId, lastRun ?? DefaultStartDate, cancellationToken);
            var batch = new Batch
            {
                ProductId = config.ProductId,
                BatchType = config.BatchType,
                StartTime = DateTime.UtcNow,
                Status = BatchStatus.Running,
                CreatedAt = DateTime.UtcNow
            };

            await batchRepository.CreateBatchAsync(batch, cancellationToken);

            var batchLeads = leads.Select(lead => new BatchLead
            {
                BatchId = batch.BatchId,
                LeadId = lead.Id,
                Status = BatchLeadStatus.Pending,
                RetryCount = 0
            }).ToList();

            await batchRepository.CreateBatchLeadsAsync(batchLeads, cancellationToken);

            var successCount = 0;
            var failedCount = 0;
            foreach (var batchLead in batchLeads)
            {
                var result = await ProcessLeadAsync(batchLead.LeadId, cancellationToken);
                if (result.IsSuccess)
                {
                    batchLead.Status = BatchLeadStatus.Success;
                    batchLead.ErrorMessage = null;
                    successCount++;
                }
                else
                {
                    batchLead.Status = BatchLeadStatus.Failed;
                    batchLead.ErrorMessage = result.ErrorMessage;
                    failedCount++;
                }

                batchLead.ProcessedAt = DateTime.UtcNow;
            }

            batch.TotalLeads = batchLeads.Count;
            batch.SuccessCount = successCount;
            batch.FailedCount = failedCount;
            batch.Status = failedCount > 0 ? BatchStatus.Failed : BatchStatus.Completed;
            batch.EndTime = DateTime.UtcNow;
            await batchRepository.SaveChangesAsync(cancellationToken);

            logger.LogInformation(
                "Processed batch {BatchId} for product {ProductId}. Total={Total}, Success={Success}, Failed={Failed}",
                batch.BatchId,
                batch.ProductId,
                batch.TotalLeads,
                batch.SuccessCount,
                batch.FailedCount);
        }
        catch (Exception ex)
        {
            logger.LogError(ex, "Failed processing config {ConfigId} for product {ProductId}", config.ConfigId, config.ProductId);
        }
    }

    private static Task<LeadProcessResult> ProcessLeadAsync(Guid leadId, CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();
        var isSuccess = leadId.GetHashCode() % 5 != 0;
        return Task.FromResult(isSuccess
            ? LeadProcessResult.Success()
            : LeadProcessResult.Failure("Simulated processing failure."));
    }

    private sealed record LeadProcessResult(bool IsSuccess, string? ErrorMessage)
    {
        public static LeadProcessResult Success() => new(true, null);
        public static LeadProcessResult Failure(string errorMessage) => new(false, errorMessage);
    }
}
