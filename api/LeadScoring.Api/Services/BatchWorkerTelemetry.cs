using LeadScoring.Api.Contracts;

namespace LeadScoring.Api.Services;

public interface IBatchWorkerTelemetry
{
    void RecordCycleCompleted(DateTime utcNow);
    void RecordAutomaticRun(string companyName, int productId, Models.CampaignBatchType batchType, int successCount, int failureCount, DateTime utcNow);
    BatchAutomationStatusDto GetStatus();
}

public sealed class BatchWorkerTelemetry : IBatchWorkerTelemetry
{
    private DateTime? _workerLastCheckUtc;
    private DateTime? _lastAutomaticRunUtc;
    private string? _lastAutomaticRunSummary;

    public void RecordCycleCompleted(DateTime utcNow) => _workerLastCheckUtc = utcNow;

    public void RecordAutomaticRun(
        string companyName,
        int productId,
        Models.CampaignBatchType batchType,
        int successCount,
        int failureCount,
        DateTime utcNow)
    {
        _lastAutomaticRunUtc = utcNow;
        _lastAutomaticRunSummary =
            $"{batchType} for {companyName} product {productId}: {successCount} sent, {failureCount} failed";
    }

    public BatchAutomationStatusDto GetStatus() => new(
        _workerLastCheckUtc.HasValue && DateTime.UtcNow - _workerLastCheckUtc.Value < TimeSpan.FromMinutes(3),
        _workerLastCheckUtc,
        _lastAutomaticRunUtc,
        _lastAutomaticRunSummary);
}
