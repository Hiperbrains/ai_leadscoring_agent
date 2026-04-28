using LeadScoring.Api.Models;

namespace LeadScoring.Api.Contracts;

public record BatchRetryResultDto(
    long SourceBatchId,
    long RetryBatchId,
    int RetriedCount,
    int SuccessCount,
    int FailedCount,
    BatchStatus Status);
