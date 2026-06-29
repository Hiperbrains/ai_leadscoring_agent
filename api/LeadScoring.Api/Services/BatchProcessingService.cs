using LeadScoring.Api.Contracts;
using LeadScoring.Api.Models;
using LeadScoring.Api.Repositories;
using System.Collections.Concurrent;
using System.Net;
using System.Net.Mail;
using System.Text;
using System.Text.RegularExpressions;

namespace LeadScoring.Api.Services;

public class BatchProcessingService(
    IBatchRepository batchRepository,
    IEmailService emailService,
    TokenService tokenService,
    IConfiguration configuration,
    ILogger<BatchProcessingService> logger,
    IServiceScopeFactory scopeFactory,
    ManualBatchProgressStore progressStore,
    ITenantLeadScope tenantLeadScope,
    IProductContext productContext,
    ITenantContext tenantContext,
    IBatchWorkerTelemetry workerTelemetry) : IBatchProcessingService
{
    private sealed record SentEmailSample(int TemplateId, bool IsFollowUp, string Subject, string HtmlBody, string ExampleRecipientEmail);

    private async Task<int?> ResolveActiveProductIdAsync(CancellationToken cancellationToken)
    {
        try
        {
            return await productContext.GetCurrentProductIdAsync(cancellationToken).ConfigureAwait(false);
        }
        catch
        {
            return null;
        }
    }

    public async Task ProcessActiveConfigsAsync(CancellationToken cancellationToken)
    {
        var runDateUtc = DateTime.UtcNow;
        var companyName = await TryResolveCompanyNameAsync(cancellationToken).ConfigureAwait(false);
        var productId = await TryResolveProductIdAsync(cancellationToken).ConfigureAwait(false);

        if (await batchRepository.HasBatchRunOnDateForScopeAsync(
                runDateUtc,
                companyName,
                productId,
                cancellationToken).ConfigureAwait(false))
        {
            logger.LogInformation(
                "Daily batch already executed for {Date} (company={CompanyName}, product={ProductId}).",
                runDateUtc.Date,
                companyName ?? "(all)",
                productId?.ToString() ?? "(all)");
            return;
        }

        var batchType = await GetNextBatchTypeAsync(companyName, productId, cancellationToken).ConfigureAwait(false);
        await ExecuteBatchRunAsync(batchType, runDateUtc, companyName, productId, cancellationToken).ConfigureAwait(false);
    }

    public async Task ProcessScheduledBatchAsync(CampaignBatchType batchType, CancellationToken cancellationToken)
    {
        var runDateUtc = DateTime.UtcNow;
        var companyName = await TryResolveCompanyNameAsync(cancellationToken).ConfigureAwait(false);
        var productId = await TryResolveProductIdAsync(cancellationToken).ConfigureAwait(false);

        if (await batchRepository.HasBatchRunOnDateForScopeAndTypeAsync(
                runDateUtc,
                companyName,
                productId,
                batchType,
                BatchRunSource.Automatic,
                cancellationToken).ConfigureAwait(false))
        {
            logger.LogInformation(
                "Scheduled batch {BatchType} already executed for {Date} (company={CompanyName}, product={ProductId}).",
                batchType,
                runDateUtc.Date,
                companyName ?? "(all)",
                productId?.ToString() ?? "(all)");
            return;
        }

        await ExecuteBatchRunAsync(batchType, runDateUtc, companyName, productId, cancellationToken).ConfigureAwait(false);
    }

    private async Task ExecuteBatchRunAsync(
        CampaignBatchType batchType,
        DateTime runDateUtc,
        string? companyName,
        int? productId,
        CancellationToken cancellationToken)
    {
        var leads = await GetLeadsForBatchTypeAsync(batchType, runDateUtc, cancellationToken);
        var cycleStartUtc = runDateUtc.Date.AddDays(-3);
        var marker = $"DailySequence_{batchType}_Sent";

        var processed = 0;
        var success = 0;
        var failed = 0;
        var stageCounts = new int[5];
        var adminRecipients = await ResolveAdminRecipientsAsync(cancellationToken).ConfigureAwait(false);
        var sentSamples = new ConcurrentDictionary<int, SentEmailSample>();

        foreach (var lead in leads)
        {
            cancellationToken.ThrowIfCancellationRequested();
            if (await batchRepository.HasBatchMarkerEventAsync(lead.Id, marker, cycleStartUtc, cancellationToken))
            {
                continue;
            }

            if (SetsReengagementNextSendQuietWindow(batchType) && lead.LastEmailSentDateUtc.HasValue)
            {
                var hasEngagement = await batchRepository.HasEngagementSinceLastEmailAsync(
                    lead.Id,
                    lead.LastEmailSentDateUtc.Value,
                    cancellationToken);
                if (hasEngagement)
                {
                    continue;
                }
            }

            processed++;
            stageCounts[MapStage(lead.Stage)]++;

            var sendResult = await ProcessLeadForBatchAsync(lead, batchType, marker, sentSamples, cancellationToken);
            if (sendResult.Success)
            {
                success++;
            }
            else
            {
                failed++;
            }
        }

        var dailyProductId = productId ?? await ResolveActiveProductIdAsync(cancellationToken).ConfigureAwait(false);
        var dailyCompanyName = companyName ?? await TryResolveCompanyNameAsync(cancellationToken).ConfigureAwait(false);
        await UpdateAdminReportAggregatesAsync(adminRecipients, stageCounts, cancellationToken);
        await FillAdminMirrorSamplesIfEmptyAsync(
            batchType,
            dailyCompanyName,
            dailyProductId,
            sentSamples,
            adminRecipients,
            cancellationToken).ConfigureAwait(false);
        var adminMirrorSent = await SendBatchMirrorEmailToAdminsAsync(
            adminRecipients,
            batchType,
            runDateUtc,
            sentSamples.Values,
            cancellationToken).ConfigureAwait(false);

        await batchRepository.CreateBatchLogAsync(new BatchLog
        {
            RunDate = runDateUtc,
            BatchType = batchType,
            TotalLeadsProcessed = processed,
            SuccessCount = success,
            FailureCount = failed,
            CompanyName = dailyCompanyName,
            ProductId = dailyProductId,
            RunSource = BatchRunSource.Automatic,
            AdminMirrorSent = adminMirrorSent
        }, cancellationToken);

        if (dailyCompanyName is not null && dailyProductId.HasValue)
        {
            workerTelemetry.RecordAutomaticRun(
                dailyCompanyName,
                dailyProductId.Value,
                batchType,
                success,
                failed,
                runDateUtc);
        }
    }

    public async Task<BatchPreviewResultDto> PreviewAsync(CampaignBatchType batchType, CancellationToken cancellationToken)
    {
        var nowUtc = DateTime.UtcNow;
        // Sequential: eligible query and aggregates share one scoped DbContext.
        var eligibleLeads = await GetLeadsForBatchTypeAsync(batchType, nowUtc, cancellationToken).ConfigureAwait(false);
        var agg = await batchRepository.GetLeadAggregatesForPreviewAsync(nowUtc, cancellationToken).ConfigureAwait(false);
        var companyName = await tenantLeadScope.ResolveCompanyNameAsync(cancellationToken).ConfigureAwait(false);

        return new BatchPreviewResultDto(
            companyName,
            batchType,
            agg.TotalLeads,
            agg.Stage0Count,
            agg.Stage1Count,
            agg.Stage2Count,
            agg.Stage3Count,
            agg.Stage4Count,
            agg.NewLeadsCount,
            agg.Last2DaysInactiveCount,
            agg.Last4DaysSinceLastEmailCount,
            agg.DidNotOpenEmailCount,
            eligibleLeads.Count);
    }

    public async Task<BatchManualRunResultDto> RunManualAsync(CampaignBatchType batchType, string? scope, int? maxLeads, CancellationToken cancellationToken)
    {
        var nowUtc = DateTime.UtcNow;
        var eligibleLeads = await GetLeadsForBatchTypeAsync(batchType, nowUtc, cancellationToken);
        var allLeads = await batchRepository.GetAllLeadsForPreviewAsync(cancellationToken);
        var filtered = await FilterManualLeadsByScopeAsync(eligibleLeads, allLeads, scope, nowUtc, cancellationToken);
        var leads = ApplyManualMaxLeads(filtered, maxLeads);
        var marker = $"DailySequence_{batchType}_Manual_{nowUtc:yyyyMMddHHmmss}";

        var processed = 0;
        var success = 0;
        var failed = 0;
        var failures = new ConcurrentBag<BatchFailureInfoDto>();
        var stageCounts = new int[5];
        var maxParallelism = Math.Clamp(configuration.GetValue<int?>("BatchProcessing:ManualMaxParallelism") ?? 5, 1, 20);
        var adminRecipients = await ResolveAdminRecipientsAsync(cancellationToken).ConfigureAwait(false);
        var sentSamples = new ConcurrentDictionary<int, SentEmailSample>();

        await Parallel.ForEachAsync(
            leads,
            new ParallelOptions
            {
                MaxDegreeOfParallelism = maxParallelism,
                CancellationToken = cancellationToken
            },
            async (lead, ct) =>
        {
            ct.ThrowIfCancellationRequested();

            Interlocked.Increment(ref processed);
            Interlocked.Increment(ref stageCounts[MapStage(lead.Stage)]);

            var sendResult = await ProcessLeadInIsolatedScopeAsync(lead.Id, batchType, marker, sentSamples, ct);
            if (sendResult.Success)
            {
                Interlocked.Increment(ref success);
            }
            else
            {
                Interlocked.Increment(ref failed);
                failures.Add(new BatchFailureInfoDto(lead.Id, lead.Email, sendResult.FailureReason ?? "Send failed."));
            }
        });

        var runManualProductId = await ResolveActiveProductIdAsync(cancellationToken).ConfigureAwait(false);
        var runManualCompanyName = await tenantLeadScope.ResolveCompanyNameAsync(cancellationToken).ConfigureAwait(false);
        await UpdateAdminReportAggregatesAsync(adminRecipients, stageCounts, cancellationToken);
        await FillAdminMirrorSamplesIfEmptyAsync(
            batchType,
            runManualCompanyName,
            runManualProductId,
            sentSamples,
            adminRecipients,
            cancellationToken).ConfigureAwait(false);
        var adminMirrorSent = await SendBatchMirrorEmailToAdminsAsync(
            adminRecipients,
            batchType,
            nowUtc,
            sentSamples.Values,
            cancellationToken).ConfigureAwait(false);

        await batchRepository.CreateBatchLogAsync(new BatchLog
        {
            RunDate = nowUtc,
            BatchType = batchType,
            TotalLeadsProcessed = processed,
            SuccessCount = success,
            FailureCount = failed,
            CompanyName = runManualCompanyName,
            ProductId = runManualProductId,
            RunSource = BatchRunSource.Manual,
            AdminMirrorSent = adminMirrorSent
        }, cancellationToken);

        return new BatchManualRunResultDto(
            batchType,
            processed,
            success,
            failed,
            failures.ToList());
    }

    public async Task<BatchManualRunStartDto> StartManualAsync(CampaignBatchType batchType, string? scope, int? maxLeads, CancellationToken cancellationToken)
    {
        var nowUtc = DateTime.UtcNow;
        var normalizedScope = string.IsNullOrWhiteSpace(scope) ? "TotalEligible" : scope.Trim();
        var normalizedMaxLeads = NormalizeManualMaxLeads(maxLeads);
        var eligibleLeads = await GetLeadsForBatchTypeAsync(batchType, nowUtc, cancellationToken);
        var allLeads = await batchRepository.GetAllLeadsForPreviewAsync(cancellationToken);
        var filtered = await FilterManualLeadsByScopeAsync(eligibleLeads, allLeads, normalizedScope, nowUtc, cancellationToken);
        var leads = ApplyManualMaxLeads(filtered, normalizedMaxLeads);
        var state = progressStore.CreateJob(batchType, normalizedScope, leads.Count);

        // The HTTP request returns immediately after this method, so HttpContext (and thus claims +
        // X-Product-Id) is gone by the time Task.Run executes. Capture the tenant + product the user
        // is actually working on now and replay them inside the background DI scope; otherwise the
        // batch repository falls back to the unauthenticated branch and pulls every lead across all
        // products/tenants.
        var capturedCompanyName = await tenantLeadScope.ResolveCompanyNameAsync(cancellationToken).ConfigureAwait(false);
        var capturedProductId = await productContext.GetCurrentProductIdAsync(cancellationToken).ConfigureAwait(false);
        var capturedSchemaName = tenantContext.SchemaName;
        var capturedTenantId = tenantContext.TenantId;

        _ = Task.Run(async () =>
        {
            try
            {
                using var backgroundScope = scopeFactory.CreateScope();
                var ambient = backgroundScope.ServiceProvider.GetRequiredService<IAmbientTenantState>();
                ambient.Set(capturedCompanyName, capturedSchemaName, capturedTenantId, capturedProductId);

                var scopedService = backgroundScope.ServiceProvider.GetRequiredService<IBatchProcessingService>();
                var result = await scopedService.RunManualTrackedAsync(state.JobId, batchType, normalizedScope, normalizedMaxLeads, CancellationToken.None);
                progressStore.Complete(state.JobId, result);
            }
            catch (Exception ex)
            {
                logger.LogError(ex, "Manual batch background job failed for {BatchType}. JobId={JobId}", batchType, state.JobId);
                progressStore.Fail(state.JobId);
            }
        });

        return new BatchManualRunStartDto(state.JobId, batchType, normalizedScope);
    }

    public BatchManualRunStatusDto? GetManualStatus(Guid jobId) => progressStore.GetStatus(jobId);

    public async Task<IReadOnlyList<BatchLogHistoryDto>> GetBatchLogHistoryAsync(int take, int days, CancellationToken cancellationToken)
    {
        days = Math.Clamp(days, 1, 30);
        var sinceUtc = DateTime.UtcNow.Date.AddDays(-(days - 1));
        var companyName = await tenantLeadScope.ResolveCompanyNameAsync(cancellationToken).ConfigureAwait(false);
        var activeProductId = await tenantLeadScope.ResolveCurrentProductIdAsync(cancellationToken).ConfigureAwait(false);
        var rows = await batchRepository
            .GetRecentBatchLogsAsync(take, companyName, activeProductId, sinceUtc, cancellationToken)
            .ConfigureAwait(false);

        var productIds = rows
            .Where(r => r.ProductId.HasValue)
            .Select(r => r.ProductId!.Value)
            .Distinct()
            .ToArray();

        var productNamesById = productIds.Length == 0
            ? new Dictionary<int, string>()
            : await batchRepository.GetProductNamesByIdAsync(productIds, cancellationToken).ConfigureAwait(false);

        var list = new List<BatchLogHistoryDto>(rows.Count);
        foreach (var x in rows)
        {
            string? productName = null;
            if (x.ProductId.HasValue && productNamesById.TryGetValue(x.ProductId.Value, out var name))
            {
                productName = name;
            }

            list.Add(new BatchLogHistoryDto(
                x.BatchId,
                x.RunDate,
                x.BatchType,
                x.TotalLeadsProcessed,
                x.SuccessCount,
                x.FailureCount,
                x.CompanyName,
                x.ProductId,
                productName,
                x.RunSource,
                x.AdminMirrorSent));
        }

        return list;
    }

    public async Task<TestMarketingEmailResultDto> SendTestMarketingEmailsAsync(
        TestMarketingEmailRequestDto request,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(request);

        var recipients = NormalizeTestRecipients(request);
        if (recipients.Count == 0)
        {
            throw new ArgumentException("Enter at least one valid email address.", nameof(request));
        }

        var maxRecipients = Math.Clamp(configuration.GetValue<int?>("BatchProcessing:TestMarketingMaxRecipients") ?? 50, 1, 200);
        if (recipients.Count > maxRecipients)
        {
            throw new ArgumentException($"At most {maxRecipients} test recipients are allowed per request.");
        }

        var syntheticProductId = await ResolveSyntheticProductIdAsync(request.ProductId, cancellationToken).ConfigureAwait(false);
        var successes = 0;
        var failures = new List<BatchFailureInfoDto>();

        foreach (var to in recipients)
        {
            cancellationToken.ThrowIfCancellationRequested();

            var surrogate = await ResolveTestSurrogateLeadAsync(
                to,
                syntheticProductId,
                request.BatchType,
                request.TemplateStage,
                cancellationToken).ConfigureAwait(false);

            var template = await batchRepository.GetTemplateByBatchTypeAsync(request.BatchType, surrogate, cancellationToken)
                .ConfigureAwait(false);

            if (template is null)
            {
                failures.Add(new BatchFailureInfoDto(surrogate.Id, to, "No active email template for this batch type and lead profile."));
                continue;
            }

            var resolvedBody = ComposeBatchEmailHtml(template, surrogate, request.BatchType, useTrackedCta: true);

            var sent = await SendWithRetryAsync(to, template.Subject, resolvedBody, suppressObserverBcc: true, cancellationToken)
                .ConfigureAwait(false);
            if (sent)
            {
                successes++;
            }
            else
            {
                failures.Add(new BatchFailureInfoDto(surrogate.Id, to, "Max retries exceeded or SMTP error."));
            }
        }

        return new TestMarketingEmailResultDto(recipients.Count, successes, failures.Count, failures);
    }

    public async Task<BatchManualRunResultDto> RunManualTrackedAsync(Guid jobId, CampaignBatchType batchType, string? scope, int? maxLeads, CancellationToken cancellationToken)
    {
        var nowUtc = DateTime.UtcNow;
        var eligibleLeads = await GetLeadsForBatchTypeAsync(batchType, nowUtc, cancellationToken);
        var allLeads = await batchRepository.GetAllLeadsForPreviewAsync(cancellationToken);
        var filtered = await FilterManualLeadsByScopeAsync(eligibleLeads, allLeads, scope, nowUtc, cancellationToken);
        var leads = ApplyManualMaxLeads(filtered, maxLeads);
        var marker = $"DailySequence_{batchType}_Manual_{nowUtc:yyyyMMddHHmmss}";

        var processed = 0;
        var success = 0;
        var failed = 0;
        var failures = new ConcurrentBag<BatchFailureInfoDto>();
        var stageCounts = new int[5];
        var maxParallelism = Math.Clamp(configuration.GetValue<int?>("BatchProcessing:ManualMaxParallelism") ?? 5, 1, 20);
        var adminRecipients = await ResolveAdminRecipientsAsync(cancellationToken).ConfigureAwait(false);
        var sentSamples = new ConcurrentDictionary<int, SentEmailSample>();

        await Parallel.ForEachAsync(
            leads,
            new ParallelOptions
            {
                MaxDegreeOfParallelism = maxParallelism,
                CancellationToken = cancellationToken
            },
            async (lead, ct) =>
            {
                ct.ThrowIfCancellationRequested();

                Interlocked.Increment(ref processed);
                Interlocked.Increment(ref stageCounts[MapStage(lead.Stage)]);

                var sendResult = await ProcessLeadInIsolatedScopeAsync(lead.Id, batchType, marker, sentSamples, ct);
                if (sendResult.Success)
                {
                    Interlocked.Increment(ref success);
                }
                else
                {
                    Interlocked.Increment(ref failed);
                    failures.Add(new BatchFailureInfoDto(lead.Id, lead.Email, sendResult.FailureReason ?? "Send failed."));
                }

                progressStore.IncrementProcessed(jobId, sendResult.Success);
            });

        var trackedManualProductId = await ResolveActiveProductIdAsync(cancellationToken).ConfigureAwait(false);
        var trackedManualCompanyName = await tenantLeadScope.ResolveCompanyNameAsync(cancellationToken).ConfigureAwait(false);
        await UpdateAdminReportAggregatesAsync(adminRecipients, stageCounts, cancellationToken);
        await FillAdminMirrorSamplesIfEmptyAsync(
            batchType,
            trackedManualCompanyName,
            trackedManualProductId,
            sentSamples,
            adminRecipients,
            cancellationToken).ConfigureAwait(false);
        var trackedAdminMirrorSent = await SendBatchMirrorEmailToAdminsAsync(
            adminRecipients,
            batchType,
            nowUtc,
            sentSamples.Values,
            cancellationToken).ConfigureAwait(false);

        await batchRepository.CreateBatchLogAsync(new BatchLog
        {
            RunDate = nowUtc,
            BatchType = batchType,
            TotalLeadsProcessed = processed,
            SuccessCount = success,
            FailureCount = failed,
            CompanyName = trackedManualCompanyName,
            ProductId = trackedManualProductId,
            RunSource = BatchRunSource.Manual,
            AdminMirrorSent = trackedAdminMirrorSent
        }, cancellationToken);

        return new BatchManualRunResultDto(
            batchType,
            processed,
            success,
            failed,
            failures.ToList());
    }

    private async Task<(bool Success, string? FailureReason)> ProcessLeadInIsolatedScopeAsync(
        Guid leadId,
        CampaignBatchType batchType,
        string marker,
        ConcurrentDictionary<int, SentEmailSample> sentSamples,
        CancellationToken cancellationToken)
    {
        using var scope = scopeFactory.CreateScope();
        var scopedRepository = scope.ServiceProvider.GetRequiredService<IBatchRepository>();

        var lead = await scopedRepository.GetLeadForUpdateAsync(leadId, cancellationToken);
        if (lead is null)
        {
            return (false, "Lead not found.");
        }

        return await ProcessLeadForBatchAsync(scopedRepository, lead, batchType, marker, sentSamples, cancellationToken);
    }

    private async Task<List<Lead>> FilterManualLeadsByScopeAsync(
        List<Lead> eligibleLeads,
        List<Lead> allLeads,
        string? scope,
        DateTime nowUtc,
        CancellationToken cancellationToken)
    {
        if (string.IsNullOrWhiteSpace(scope) || scope.Equals("TotalEligible", StringComparison.OrdinalIgnoreCase))
        {
            return eligibleLeads;
        }

        if (scope.Equals("TotalLeads", StringComparison.OrdinalIgnoreCase))
        {
            return allLeads;
        }

        if (scope.Equals("Stage0", StringComparison.OrdinalIgnoreCase))
        {
            return allLeads.Where(x => x.Stage == LeadStage.Cold).ToList();
        }

        if (scope.Equals("Stage1", StringComparison.OrdinalIgnoreCase))
        {
            return allLeads.Where(x => x.Stage == LeadStage.Warm).ToList();
        }

        if (scope.Equals("Stage2", StringComparison.OrdinalIgnoreCase))
        {
            return allLeads.Where(x => x.Stage == LeadStage.Mql).ToList();
        }

        if (scope.Equals("Stage3", StringComparison.OrdinalIgnoreCase))
        {
            return allLeads.Where(x => x.Stage == LeadStage.Hot).ToList();
        }

        if (scope.Equals("Stage4", StringComparison.OrdinalIgnoreCase))
        {
            var known = new HashSet<LeadStage> { LeadStage.Cold, LeadStage.Warm, LeadStage.Mql, LeadStage.Hot };
            return allLeads.Where(x => !known.Contains(x.Stage)).ToList();
        }

        if (scope.Equals("NewLeads", StringComparison.OrdinalIgnoreCase))
        {
            return allLeads.Where(x => x.CreatedAtUtc >= nowUtc.AddDays(-1)).ToList();
        }

        if (scope.Equals("Last2DaysInactive", StringComparison.OrdinalIgnoreCase))
        {
            return allLeads.Where(x => x.LastActivityUtc <= nowUtc.AddDays(-2)).ToList();
        }

        if (scope.Equals("Last4DaysSinceEmail", StringComparison.OrdinalIgnoreCase))
        {
            return allLeads.Where(x => x.LastEmailSentDateUtc.HasValue && x.LastEmailSentDateUtc <= nowUtc.AddDays(-4)).ToList();
        }

        if (scope.Equals("DidNotOpenEmail", StringComparison.OrdinalIgnoreCase))
        {
            return await batchRepository.GetLeadsDidNotOpenSinceLastEmailAsync(cancellationToken).ConfigureAwait(false);
        }

        return eligibleLeads;
    }

    private static int? NormalizeManualMaxLeads(int? maxLeads)
    {
        if (maxLeads is null || maxLeads.Value < 1)
        {
            return null;
        }

        return maxLeads.Value;
    }

    private static List<Lead> ApplyManualMaxLeads(List<Lead> leads, int? maxLeads)
    {
        if (maxLeads is null || maxLeads.Value < 1)
        {
            return leads;
        }

        var take = Math.Min(maxLeads.Value, leads.Count);
        return leads.Take(take).ToList();
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
        var adminRecipients = await ResolveAdminRecipientsAsync(cancellationToken).ConfigureAwait(false);
        var sentSamples = new ConcurrentDictionary<int, SentEmailSample>();
        foreach (var batchLead in retryBatchLeads)
        {
            var lead = await batchRepository.GetLeadForUpdateAsync(batchLead.LeadId, cancellationToken);
            if (lead is null)
            {
                batchLead.Status = BatchLeadStatus.Failed;
                batchLead.ErrorMessage = "Lead not found.";
                failedCount++;
            }
            else
            {
                var result = await ProcessLeadForBatchAsync(lead, CampaignBatchType.Day3, "RetryBatchSent", sentSamples, cancellationToken);
                batchLead.Status = result.Success ? BatchLeadStatus.Success : BatchLeadStatus.Failed;
                batchLead.ErrorMessage = result.Success ? null : (result.FailureReason ?? "Retry failed.");
                if (result.Success)
                {
                    successCount++;
                }
                else
                {
                    failedCount++;
                }
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

    private async Task<(bool Success, string? FailureReason)> ProcessLeadForBatchAsync(
        Lead lead,
        CampaignBatchType batchType,
        string marker,
        ConcurrentDictionary<int, SentEmailSample> sentSamples,
        CancellationToken cancellationToken)
        => await ProcessLeadForBatchAsync(batchRepository, lead, batchType, marker, sentSamples, cancellationToken);

    private async Task<(bool Success, string? FailureReason)> ProcessLeadForBatchAsync(
        IBatchRepository repository,
        Lead lead,
        CampaignBatchType batchType,
        string marker,
        ConcurrentDictionary<int, SentEmailSample> sentSamples,
        CancellationToken cancellationToken)
    {
        var template = await repository.GetTemplateByBatchTypeAsync(batchType, lead, cancellationToken);
        if (template is null)
        {
            return (false, $"No active email template found for {batchType} (company={lead.CompanyName ?? "n/a"}, product={lead.ProductId}).");
        }

        var eventName = $"batch_{batchType.ToString().ToLowerInvariant()}";
        var resolvedBody = ComposeBatchEmailHtml(template, lead, batchType, useTrackedCta: true);

        var sent = await SendWithRetryAsync(lead.Email, template.Subject, resolvedBody, template.IsFollowUp, cancellationToken);
        if (!sent)
        {
            return (false, "Email send failed after retries.");
        }

        sentSamples.TryAdd(
            template.TemplateId,
            new SentEmailSample(template.TemplateId, template.IsFollowUp, template.Subject, resolvedBody, lead.Email));

        var sentAtUtc = DateTime.UtcNow;
        lead.LastEmailSentDateUtc = sentAtUtc;
        lead.LastActivityUtc = sentAtUtc;
        if (batchType == CampaignBatchType.Day1)
        {
            lead.WelcomeEmailSent = true;
        }
        else if (SetsReengagementNextSendQuietWindow(batchType))
        {
            lead.NextEmailSendDateUtc = sentAtUtc.AddDays(7);
        }

        await repository.AddEventAsync(new LeadEvent
        {
            Id = Guid.NewGuid(),
            LeadId = lead.Id,
            Type = EventType.WebsiteActivity,
            Source = EventSource.Email,
            TimestampUtc = sentAtUtc,
            MetadataJson = $$"""{"eventName":"{{eventName}}","batchType":"{{batchType}}","systemMarker":"{{marker}}"}"""
        }, cancellationToken);

        await repository.SaveChangesAsync(cancellationToken);
        return (true, null);
    }

    private async Task<bool> SendWithRetryAsync(
        string to,
        string subject,
        string htmlBody,
        bool suppressObserverBcc,
        CancellationToken cancellationToken)
    {
        var maxRetries = configuration.GetValue<int?>("BatchProcessing:EmailRetryCount") ?? 3;
        var delayMs = configuration.GetValue<int?>("BatchProcessing:EmailRetryDelayMs") ?? 1000;

        for (var attempt = 1; attempt <= maxRetries; attempt++)
        {
            cancellationToken.ThrowIfCancellationRequested();
            try
            {
                await emailService.SendAsync(to, subject, htmlBody, suppressObserverBcc);
                return true;
            }
            catch (Exception ex)
            {
                logger.LogWarning(ex, "Email send failed for {Email}. Attempt {Attempt}/{Max}.", to, attempt, maxRetries);
                if (attempt == maxRetries)
                {
                    break;
                }

                await Task.Delay(delayMs * attempt, cancellationToken);
            }
        }

        return false;
    }

    private async Task<List<string>> ResolveAdminRecipientsAsync(CancellationToken cancellationToken)
    {
        var adminEmailConfig = configuration["BatchProcessing:AdminEmail"];
        var configuredRecipients = (adminEmailConfig ?? string.Empty)
            .Split([',', ';'], StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries)
            .Where(x => !string.IsNullOrWhiteSpace(x))
            .ToList();

        var storedRecipients = await batchRepository.GetAdminReportEmailsAsync(cancellationToken).ConfigureAwait(false);

        var adminRecipients = configuredRecipients
            .Concat(storedRecipients)
            .Where(x => !string.IsNullOrWhiteSpace(x))
            .Select(x => x.Trim())
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .ToList();

        if (adminRecipients.Count == 0)
        {
            logger.LogWarning("No admin recipients found in BatchProcessing:AdminEmail or AdminBatchReports table.");
        }

        return adminRecipients;
    }

    private async Task UpdateAdminReportAggregatesAsync(
        IReadOnlyList<string> adminRecipients,
        IReadOnlyList<int> stageCounts,
        CancellationToken cancellationToken)
    {
        if (adminRecipients.Count == 0)
        {
            return;
        }

        foreach (var recipient in adminRecipients)
        {
            await batchRepository.UpsertAdminReportAsync(
                recipient,
                stageCounts[0],
                stageCounts[1],
                stageCounts[2],
                stageCounts[3],
                stageCounts[4],
                cancellationToken).ConfigureAwait(false);
        }
    }

    private static bool SetsReengagementNextSendQuietWindow(CampaignBatchType batchType)
        => batchType is CampaignBatchType.Day4
            or CampaignBatchType.WarmFollowUp
            or CampaignBatchType.MqlFollowUp
            or CampaignBatchType.HotFollowUp;

    private static string FormatCampaignBatchTypeLabel(CampaignBatchType batchType) => batchType switch
    {
        CampaignBatchType.Day1 => "COLD",
        CampaignBatchType.Day2 => "COLD FOLLOWUP",
        CampaignBatchType.Day3 => "Day 3",
        CampaignBatchType.Day4 => "Day 4",
        CampaignBatchType.Warm => "WARM",
        CampaignBatchType.WarmFollowUp => "WARM FOLLOWUP",
        CampaignBatchType.Mql => "MQL",
        CampaignBatchType.MqlFollowUp => "MQL FOLLOWUP",
        CampaignBatchType.Hot => "HOT",
        CampaignBatchType.HotFollowUp => "HOT FOLLOWUP",
        _ => batchType.ToString()
    };

    private static IReadOnlyList<LeadStage> GetStagesForAdminMirror(CampaignBatchType batchType) => batchType switch
    {
        CampaignBatchType.Day1 or CampaignBatchType.Day2 => [LeadStage.Cold],
        CampaignBatchType.Day3 => [LeadStage.Warm, LeadStage.Mql, LeadStage.Hot],
        CampaignBatchType.Day4 => [LeadStage.Mql, LeadStage.Hot],
        CampaignBatchType.Warm or CampaignBatchType.WarmFollowUp => [LeadStage.Warm],
        CampaignBatchType.Mql or CampaignBatchType.MqlFollowUp => [LeadStage.Mql],
        CampaignBatchType.Hot or CampaignBatchType.HotFollowUp => [LeadStage.Hot],
        _ => [LeadStage.Cold]
    };

    private async Task FillAdminMirrorSamplesIfEmptyAsync(
        CampaignBatchType batchType,
        string? companyName,
        int? productId,
        ConcurrentDictionary<int, SentEmailSample> sentSamples,
        IReadOnlyList<string> adminRecipients,
        CancellationToken cancellationToken)
    {
        if (!sentSamples.IsEmpty)
        {
            return;
        }

        var previewEmail = adminRecipients.FirstOrDefault() ?? "admin@preview.local";
        foreach (var stage in GetStagesForAdminMirror(batchType))
        {
            cancellationToken.ThrowIfCancellationRequested();

            var surrogate = new Lead
            {
                Id = Guid.NewGuid(),
                Email = previewEmail,
                CompanyName = companyName,
                ProductId = productId,
                Stage = stage
            };

            var template = await batchRepository.GetTemplateByBatchTypeAsync(batchType, surrogate, cancellationToken)
                .ConfigureAwait(false);
            if (template is null)
            {
                continue;
            }

            var resolvedBody = ComposeBatchEmailHtml(template, surrogate, batchType, useTrackedCta: false);
            sentSamples.TryAdd(
                template.TemplateId,
                new SentEmailSample(template.TemplateId, template.IsFollowUp, template.Subject, resolvedBody, previewEmail));
        }
    }

    private async Task<bool> SendBatchMirrorEmailToAdminsAsync(
        IReadOnlyList<string> adminRecipients,
        CampaignBatchType batchType,
        DateTime runUtc,
        IEnumerable<SentEmailSample> samples,
        CancellationToken cancellationToken)
    {
        var ordered = samples.Where(s => !s.IsFollowUp).OrderBy(s => s.TemplateId).ToList();
        if (adminRecipients.Count == 0 || ordered.Count == 0)
        {
            return false;
        }

        var subject = ordered.Count == 1
            ? ordered[0].Subject
            : $"[Batch mirror] {FormatCampaignBatchTypeLabel(batchType)} — {ordered.Count} variants ({runUtc:yyyy-MM-dd HH:mm} UTC)";

        string htmlBody;
        if (ordered.Count == 1)
        {
            htmlBody = ordered[0].HtmlBody;
        }
        else
        {
            var body = new StringBuilder();
            body.Append("""<div style="font-family:Segoe UI,Helvetica,Arial,sans-serif;color:#022232;">""");
            for (var i = 0; i < ordered.Count; i++)
            {
                var s = ordered[i];
                body.Append("""<div style="margin-bottom:32px;padding-bottom:24px;border-bottom:1px solid #ccc;">""");
                body.Append("""<p style="margin:0 0 12px;font-size:12px;color:#555;">""");
                body.Append(WebUtility.HtmlEncode(
                    $"Variant {i + 1} of {ordered.Count} — example recipient: {s.ExampleRecipientEmail}"));
                body.Append("</p>");
                body.Append(s.HtmlBody);
                body.Append("</div>");
            }

            body.Append("</div>");
            htmlBody = body.ToString();
        }

        foreach (var recipient in adminRecipients)
        {
            try
            {
                var mirrorBody = OutboundEmailRecipientLinkRewrite.ApplyRecipientEmailToHiperbrainsLinks(htmlBody, recipient);
                await SendAdminEmailWithoutBccAsync(recipient, subject, mirrorBody, cancellationToken).ConfigureAwait(false);
            }
            catch (Exception ex)
            {
                logger.LogError(ex, "Failed to send batch mirror email to {Recipient}.", recipient);
            }
        }

        return true;
    }

    private async Task SendAdminEmailWithoutBccAsync(string to, string subject, string htmlBody, CancellationToken cancellationToken)
    {
        var smtpSection = configuration.GetSection("SMTP");
        var emailSection = configuration.GetSection("Email");
        var host = smtpSection["Host"] ?? throw new InvalidOperationException("SMTP:Host is missing.");
        var port = int.Parse(smtpSection["Port"] ?? throw new InvalidOperationException("SMTP:Port is missing."));
        var username = smtpSection["Username"] ?? throw new InvalidOperationException("SMTP:Username is missing.");
        var password = smtpSection["Password"] ?? throw new InvalidOperationException("SMTP:Password is missing.");
        var fromAddress = emailSection["FromAddress"] ?? throw new InvalidOperationException("Email:FromAddress is missing.");

        using var message = new MailMessage(fromAddress, to, subject, htmlBody) { IsBodyHtml = true };
        using var client = new SmtpClient(host, port)
        {
            EnableSsl = true,
            Credentials = new NetworkCredential(username, password)
        };

        cancellationToken.ThrowIfCancellationRequested();
        await client.SendMailAsync(message);
    }

    private static IReadOnlyList<string> NormalizeTestRecipients(TestMarketingEmailRequestDto request)
    {
        var set = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        if (request.Recipients is not null)
        {
            foreach (var r in request.Recipients)
            {
                var t = NormalizeOneTestEmail(r);
                if (t is not null)
                {
                    set.Add(t);
                }
            }
        }

        if (!string.IsNullOrWhiteSpace(request.RecipientsRaw))
        {
            foreach (var part in Regex.Split(request.RecipientsRaw.Trim(), @"[\s,;]+", RegexOptions.CultureInvariant))
            {
                var t = NormalizeOneTestEmail(part);
                if (t is not null)
                {
                    set.Add(t);
                }
            }
        }

        return set.OrderBy(static x => x, StringComparer.OrdinalIgnoreCase).ToList();
    }

    private static string? NormalizeOneTestEmail(string? raw)
    {
        if (string.IsNullOrWhiteSpace(raw))
        {
            return null;
        }

        var t = raw.Trim();
        if (t.Length > 254 || !t.Contains('@', StringComparison.Ordinal))
        {
            return null;
        }

        return t;
    }

    private Task<int?> ResolveSyntheticProductIdAsync(int? requested, CancellationToken cancellationToken) =>
        requested is null ? batchRepository.GetAnyLeadProductIdAsync(cancellationToken) : Task.FromResult(requested);

    private async Task<Lead> ResolveTestSurrogateLeadAsync(
        string recipientEmail,
        int? syntheticProductId,
        CampaignBatchType batchType,
        string? templateStage,
        CancellationToken cancellationToken)
    {
        var existing = await batchRepository.GetLeadByEmailAsync(recipientEmail, cancellationToken).ConfigureAwait(false);
        if (existing is not null)
        {
            return new Lead
            {
                Id = existing.Id,
                Email = recipientEmail.Trim(),
                ProductId = existing.ProductId,
                Stage = existing.Stage
            };
        }

        return new Lead
        {
            Id = Guid.NewGuid(),
            Email = recipientEmail.Trim(),
            ProductId = syntheticProductId,
            Stage = ResolveSyntheticStageForTest(batchType, templateStage)
        };
    }

    private static LeadStage ResolveSyntheticStageForTest(CampaignBatchType batchType, string? templateStage)
    {
        var parsed = TryParseLeadStageForTest(templateStage);
        return batchType switch
        {
            CampaignBatchType.Day1 => LeadStage.Cold,
            CampaignBatchType.Day2 => LeadStage.Cold,
            CampaignBatchType.Day3 => parsed ?? LeadStage.Warm,
            CampaignBatchType.Day4 when parsed is LeadStage.Mql or LeadStage.Hot => parsed!.Value,
            CampaignBatchType.Day4 => LeadStage.Mql,
            CampaignBatchType.Warm => parsed ?? LeadStage.Warm,
            CampaignBatchType.WarmFollowUp => parsed ?? LeadStage.Warm,
            CampaignBatchType.Mql => parsed ?? LeadStage.Mql,
            CampaignBatchType.MqlFollowUp => parsed ?? LeadStage.Mql,
            CampaignBatchType.Hot => parsed ?? LeadStage.Hot,
            CampaignBatchType.HotFollowUp => parsed ?? LeadStage.Hot,
            _ => LeadStage.Cold
        };
    }

    private static LeadStage? TryParseLeadStageForTest(string? templateStage)
    {
        if (string.IsNullOrWhiteSpace(templateStage))
        {
            return null;
        }

        return Enum.TryParse<LeadStage>(templateStage.Trim(), true, out var s) ? s : null;
    }

    private string ComposeBatchEmailHtml(EmailTemplate template, Lead lead, CampaignBatchType batchType, bool useTrackedCta)
    {
        var eventName = $"batch_{batchType.ToString().ToLowerInvariant()}";
        var resolvedBody = ResolveTemplate(template.EmailBodyHtml, lead, eventName, template.IsTrackingEnabled);
        if (!string.IsNullOrWhiteSpace(template.CtaButtonText) &&
            !string.IsNullOrWhiteSpace(template.CtaLink) &&
            !EmailBodyCtaPolicy.ShouldSuppressAppendedCta(resolvedBody))
        {
            var resolvedLink = ResolveTemplate(template.CtaLink, lead, eventName, template.IsTrackingEnabled);
            var href = useTrackedCta ? BuildTrackedClickUrl(lead, resolvedLink) : resolvedLink;
            resolvedBody += $"""

                <p style="margin-top:20px;">
                  <a href="{href}" style="display:inline-block;background:#2de06a;color:#00233c;text-decoration:none;font-weight:700;padding:12px 24px;border-radius:8px;">{WebUtility.HtmlEncode(template.CtaButtonText)}</a>
                </p>
                """;
        }

        return OutboundEmailRecipientLinkRewrite.ApplyRecipientEmailToHiperbrainsLinks(resolvedBody, lead.Email);
    }

    private async Task<CampaignBatchType> GetNextBatchTypeAsync(
        string? companyName,
        int? productId,
        CancellationToken cancellationToken)
    {
        var last = companyName is not null || productId.HasValue
            ? await batchRepository.GetLastCompletedDailyBatchTypeForScopeAsync(companyName, productId, cancellationToken)
                .ConfigureAwait(false)
            : await batchRepository.GetLastCompletedDailyBatchTypeAsync(cancellationToken).ConfigureAwait(false);

        return last switch
        {
            null => CampaignBatchType.Day1,
            CampaignBatchType.Day1 => CampaignBatchType.Day2,
            CampaignBatchType.Day2 => CampaignBatchType.Day3,
            CampaignBatchType.Day3 => CampaignBatchType.Day4,
            _ => CampaignBatchType.Day1
        };
    }

    private async Task<string?> TryResolveCompanyNameAsync(CancellationToken cancellationToken)
    {
        try
        {
            return await tenantLeadScope.ResolveCompanyNameAsync(cancellationToken).ConfigureAwait(false);
        }
        catch
        {
            return null;
        }
    }

    private async Task<int?> TryResolveProductIdAsync(CancellationToken cancellationToken)
    {
        try
        {
            return await productContext.GetCurrentProductIdAsync(cancellationToken).ConfigureAwait(false);
        }
        catch
        {
            return null;
        }
    }

    private async Task<List<Lead>> GetLeadsForBatchTypeAsync(CampaignBatchType batchType, DateTime runDateUtc, CancellationToken cancellationToken)
    {
        return batchType switch
        {
            CampaignBatchType.Day1 => await batchRepository.GetDay1LeadsAsync(runDateUtc, cancellationToken),
            CampaignBatchType.Day2 => await batchRepository.GetDay2LeadsAsync(runDateUtc, cancellationToken),
            CampaignBatchType.Day3 => await batchRepository.GetDay3LeadsAsync(runDateUtc, cancellationToken),
            CampaignBatchType.Day4 => await batchRepository.GetDay4LeadsAsync(runDateUtc, cancellationToken),
            CampaignBatchType.Warm => await batchRepository.GetDay3LeadsForStageAsync(LeadStage.Warm, runDateUtc, cancellationToken),
            CampaignBatchType.Mql => await batchRepository.GetDay3LeadsForStageAsync(LeadStage.Mql, runDateUtc, cancellationToken),
            CampaignBatchType.Hot => await batchRepository.GetDay3LeadsForStageAsync(LeadStage.Hot, runDateUtc, cancellationToken),
            CampaignBatchType.WarmFollowUp => await batchRepository.GetDay4LeadsForStageAsync(LeadStage.Warm, runDateUtc, cancellationToken),
            CampaignBatchType.MqlFollowUp => await batchRepository.GetDay4LeadsForStageAsync(LeadStage.Mql, runDateUtc, cancellationToken),
            CampaignBatchType.HotFollowUp => await batchRepository.GetDay4LeadsForStageAsync(LeadStage.Hot, runDateUtc, cancellationToken),
            _ => []
        };
    }

    private static int MapStage(LeadStage stage) => stage switch
    {
        LeadStage.Cold => 0,
        LeadStage.Warm => 1,
        LeadStage.Mql => 2,
        LeadStage.Hot => 3,
        _ => 4
    };

    private static string ResolveTemplate(string value, Lead lead, string eventName, bool trackingEnabled)
    {
        var leadId = lead.Id.ToString("D");
        var emailValue = trackingEnabled ? Uri.EscapeDataString(lead.Email) : lead.Email;
        var eventValue = trackingEnabled ? Uri.EscapeDataString(eventName) : eventName;
        var leadIdValue = trackingEnabled ? Uri.EscapeDataString(leadId) : leadId;

        return value
            .Replace("{{email}}", emailValue, StringComparison.OrdinalIgnoreCase)
            .Replace("{{event}}", eventValue, StringComparison.OrdinalIgnoreCase)
            .Replace("{{leadId}}", leadIdValue, StringComparison.OrdinalIgnoreCase)
            .Replace("{{stage}}", lead.Stage.ToString(), StringComparison.OrdinalIgnoreCase);
    }

    private string BuildTrackedClickUrl(Lead lead, string destinationUrl)
    {
        if (!Uri.IsWellFormedUriString(destinationUrl, UriKind.Absolute))
        {
            return destinationUrl;
        }

        var token = tokenService.CreateLeadToken(lead.Id);
        var trackingBaseUrl = (configuration["Tracking:BaseUrl"] ?? "http://localhost:8211").TrimEnd('/');
        return $"{trackingBaseUrl}/track/click?token={Uri.EscapeDataString(token)}&redirect={Uri.EscapeDataString(destinationUrl)}";
    }
}
