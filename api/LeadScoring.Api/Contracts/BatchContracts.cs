using LeadScoring.Api.Models;

namespace LeadScoring.Api.Contracts;

public record BatchRetryResultDto(
    long SourceBatchId,
    long RetryBatchId,
    int RetriedCount,
    int SuccessCount,
    int FailedCount,
    BatchStatus Status);

public record BatchPreviewLeadAggregates(
    int TotalLeads,
    int Stage0Count,
    int Stage1Count,
    int Stage2Count,
    int Stage3Count,
    int Stage4Count,
    int NewLeadsCount,
    int Last2DaysInactiveCount,
    int Last4DaysSinceLastEmailCount,
    int DidNotOpenEmailCount);

public record BatchPreviewResultDto(
    string CompanyName,
    CampaignBatchType BatchType,
    int TotalLeadsCount,
    int Stage0Count,
    int Stage1Count,
    int Stage2Count,
    int Stage3Count,
    int Stage4Count,
    int NewLeadsCount,
    int Last2DaysInactiveCount,
    int Last4DaysSinceLastEmailCount,
    int DidNotOpenEmailCount,
    int TotalEligibleCount);

public record BatchManualRunRequestDto(
    string? Scope,
    int? MaxLeads = null);

public record BatchFailureInfoDto(
    Guid LeadId,
    string Email,
    string Reason);

public record BatchManualRunResultDto(
    CampaignBatchType BatchType,
    int TotalLeads,
    int SuccessCount,
    int FailureCount,
    IReadOnlyList<BatchFailureInfoDto> Failures);

public record BatchManualRunStartDto(
    Guid JobId,
    CampaignBatchType BatchType,
    string Scope);

public record BatchManualRunStatusDto(
    Guid JobId,
    CampaignBatchType BatchType,
    string Scope,
    bool IsRunning,
    int TotalLeads,
    int ProcessedCount,
    int SuccessCount,
    int FailureCount,
    BatchManualRunResultDto? Result);

public record BatchLogHistoryDto(
    long BatchId,
    DateTime RunDateUtc,
    CampaignBatchType BatchType,
    int TotalLeadsProcessed,
    int SuccessCount,
    int FailureCount,
    string? CompanyName,
    int? ProductId,
    string? ProductName,
    BatchRunSource RunSource,
    bool AdminMirrorSent);

/// <summary>
/// Send sequence HTML to specific inboxes without updating leads, batch logs, or admin batches (QA only).
/// Each address receives the HTML that matches the requested sequence day (<see cref="CampaignBatchType"/>) and, for synthetic lead snapshots, ProductId / TemplateStage.
/// </summary>
public record TestMarketingEmailRequestDto(
    IReadOnlyList<string>? Recipients,
    string? RecipientsRaw,
    CampaignBatchType BatchType,
    int? ProductId,
    /// <summary>When the recipient is not an existing lead, sets stage for Day3/Day4 template selection (<c>Warm</c>, <c>Mql</c>, <c>Hot</c>). Ignored when a DB lead matches the email.</summary>
    string? TemplateStage);

public record TestMarketingEmailResultDto(
    int Attempted,
    int SuccessCount,
    int FailureCount,
    IReadOnlyList<BatchFailureInfoDto> Failures);

public record BatchScheduleItemDto(
    CampaignBatchType BatchType,
    bool IsEnabled,
    string DailyRunTimeUtc,
    bool IsConfigured,
    DateTime? LastRunUtc,
    DateTime? NextRunUtc);

public record BatchScheduleListDto(
    string CompanyName,
    int ProductId,
    IReadOnlyList<BatchScheduleItemDto> Items,
    BatchAutomationStatusDto Automation);

public record UpsertBatchScheduleItemRequest(
    CampaignBatchType BatchType,
    bool IsEnabled,
    string DailyRunTimeUtc);

public record UpsertBatchScheduleRequest(
    IReadOnlyList<UpsertBatchScheduleItemRequest> Items);

public record BatchAutomationStatusDto(
    bool WorkerActive,
    DateTime? WorkerLastCheckUtc,
    DateTime? LastAutomaticRunUtc,
    string? LastAutomaticRunSummary);
