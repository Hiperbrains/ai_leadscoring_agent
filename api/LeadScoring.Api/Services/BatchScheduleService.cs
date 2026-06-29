using LeadScoring.Api.Contracts;
using LeadScoring.Api.Data;
using LeadScoring.Api.Models;
using LeadScoring.Api.Repositories;
using Microsoft.EntityFrameworkCore;

namespace LeadScoring.Api.Services;

public class BatchScheduleService(
    LeadScoringDbContext db,
    MasterDbContext masterDb,
    IBatchRepository batchRepository,
    IServiceScopeFactory scopeFactory,
    ITenantLeadScope tenantLeadScope,
    ITenantContext tenantContext,
    IBatchWorkerTelemetry workerTelemetry,
    IConfiguration configuration,
    ILogger<BatchScheduleService> logger) : IBatchScheduleService
{
  private static readonly CampaignBatchType[] SchedulableBatchTypes =
  [
      CampaignBatchType.Day1,
      CampaignBatchType.Day2,
      CampaignBatchType.Day3,
      CampaignBatchType.Day4,
      CampaignBatchType.Warm,
      CampaignBatchType.WarmFollowUp,
      CampaignBatchType.Mql,
      CampaignBatchType.MqlFollowUp,
      CampaignBatchType.Hot,
      CampaignBatchType.HotFollowUp
  ];

    public async Task<BatchScheduleListDto> GetForCurrentTenantAsync(CancellationToken cancellationToken)
    {
        tenantContext.RequireTenant();
        var companyName = await tenantLeadScope.ResolveCompanyNameAsync(cancellationToken).ConfigureAwait(false);
        var productId = await tenantLeadScope.ResolveCurrentProductIdAsync(cancellationToken).ConfigureAwait(false)
            ?? TenantLeadScope.ScopedProductId;
        var companyLower = companyName.ToLowerInvariant();

        var rows = await db.BatchScheduleSettings
            .AsNoTracking()
            .Where(x => x.CompanyName.ToLower() == companyLower && x.ProductId == productId)
            .ToListAsync(cancellationToken)
            .ConfigureAwait(false);

        var rowsByType = rows.ToDictionary(x => x.BatchType);
        var fallbackTime = ResolveFallbackDailyRunTimeUtc();
        var items = new List<BatchScheduleItemDto>(SchedulableBatchTypes.Length);

        foreach (var batchType in SchedulableBatchTypes)
        {
            rowsByType.TryGetValue(batchType, out var row);
            var isEnabled = row?.IsEnabled ?? false;
            var dailyRunTimeUtc = row?.DailyRunTimeUtc ?? fallbackTime;
            var lastRunUtc = await batchRepository
                .GetLastBatchRunUtcForScopeAndTypeAsync(
                    companyName,
                    productId,
                    batchType,
                    BatchRunSource.Automatic,
                    cancellationToken)
                .ConfigureAwait(false);

            items.Add(BuildItemDto(batchType, isEnabled, dailyRunTimeUtc, row is not null, lastRunUtc));
        }

        return new BatchScheduleListDto(companyName, productId, items, workerTelemetry.GetStatus());
    }

    public async Task<BatchScheduleListDto> UpsertForCurrentTenantAsync(
        UpsertBatchScheduleRequest request,
        CancellationToken cancellationToken)
    {
        tenantContext.RequireTenant();
        if (request.Items is null || request.Items.Count == 0)
        {
            throw new ArgumentException("At least one schedule item is required.");
        }

        var companyName = await tenantLeadScope.ResolveCompanyNameAsync(cancellationToken).ConfigureAwait(false);
        var productId = await tenantLeadScope.ResolveCurrentProductIdAsync(cancellationToken).ConfigureAwait(false)
            ?? TenantLeadScope.ScopedProductId;
        var companyLower = companyName.ToLowerInvariant();

        var existingRows = await db.BatchScheduleSettings
            .Where(x => x.CompanyName.ToLower() == companyLower && x.ProductId == productId)
            .ToListAsync(cancellationToken)
            .ConfigureAwait(false);
        var existingByType = existingRows.ToDictionary(x => x.BatchType);
        var nowUtc = DateTime.UtcNow;

        foreach (var item in request.Items)
        {
            if (!SchedulableBatchTypes.Contains(item.BatchType))
            {
                throw new ArgumentException($"Batch type {item.BatchType} cannot be scheduled.");
            }

            if (!TryParseDailyRunTimeUtc(item.DailyRunTimeUtc, out var dailyRunTimeUtc, out var error))
            {
                throw new ArgumentException($"{item.BatchType}: {error}");
            }

            if (existingByType.TryGetValue(item.BatchType, out var row))
            {
                row.CompanyName = companyName;
                row.ProductId = productId;
                row.DailyRunTimeUtc = dailyRunTimeUtc;
                row.IsEnabled = item.IsEnabled;
                row.UpdatedAtUtc = nowUtc;
            }
            else
            {
                db.BatchScheduleSettings.Add(new BatchScheduleSetting
                {
                    CompanyName = companyName,
                    ProductId = productId,
                    BatchType = item.BatchType,
                    DailyRunTimeUtc = dailyRunTimeUtc,
                    IsEnabled = item.IsEnabled,
                    UpdatedAtUtc = nowUtc
                });
            }
        }

        await db.SaveChangesAsync(cancellationToken).ConfigureAwait(false);

        logger.LogInformation(
            "Updated {Count} batch schedule item(s) for {CompanyName} product {ProductId}.",
            request.Items.Count,
            companyName,
            productId);

        return await GetForCurrentTenantAsync(cancellationToken).ConfigureAwait(false);
    }

    public async Task ProcessDueSchedulesAsync(CancellationToken cancellationToken)
    {
        var nowUtc = DateTime.UtcNow;
        var enabledSchedules = await db.BatchScheduleSettings
            .AsNoTracking()
            .Where(x => x.IsEnabled)
            .ToListAsync(cancellationToken)
            .ConfigureAwait(false);

        if (enabledSchedules.Count == 0)
        {
            await ProcessLegacyGlobalScheduleAsync(nowUtc, cancellationToken).ConfigureAwait(false);
            return;
        }

        foreach (var schedule in enabledSchedules)
        {
            if (!IsDueNow(schedule.DailyRunTimeUtc, nowUtc))
            {
                continue;
            }

            if (await batchRepository.HasBatchRunOnDateForScopeAndTypeAsync(
                    nowUtc,
                    schedule.CompanyName,
                    schedule.ProductId,
                    schedule.BatchType,
                    BatchRunSource.Automatic,
                    cancellationToken).ConfigureAwait(false))
            {
                continue;
            }

            var tenant = await masterDb.Tenants
                .AsNoTracking()
                .Where(t => t.CompanyName.ToLower() == schedule.CompanyName.ToLower())
                .Select(t => new { t.Id, t.CompanyName, t.DatabaseName })
                .FirstOrDefaultAsync(cancellationToken)
                .ConfigureAwait(false);

            if (tenant is null)
            {
                logger.LogWarning(
                    "Skipping scheduled batch {BatchType} for {CompanyName}: tenant not found in master registry.",
                    schedule.BatchType,
                    schedule.CompanyName);
                continue;
            }

            try
            {
                using var scope = scopeFactory.CreateScope();
                var ambient = scope.ServiceProvider.GetRequiredService<IAmbientTenantState>();
                ambient.Set(tenant.CompanyName, tenant.DatabaseName, tenant.Id, schedule.ProductId);

                var batchService = scope.ServiceProvider.GetRequiredService<IBatchProcessingService>();
                await batchService.ProcessScheduledBatchAsync(schedule.BatchType, cancellationToken).ConfigureAwait(false);

                logger.LogInformation(
                    "Completed scheduled batch {BatchType} for {CompanyName} product {ProductId}.",
                    schedule.BatchType,
                    schedule.CompanyName,
                    schedule.ProductId);
            }
            catch (Exception ex)
            {
                logger.LogError(
                    ex,
                    "Scheduled batch {BatchType} failed for {CompanyName} product {ProductId}.",
                    schedule.BatchType,
                    schedule.CompanyName,
                    schedule.ProductId);
            }
        }
    }

    private async Task ProcessLegacyGlobalScheduleAsync(DateTime nowUtc, CancellationToken cancellationToken)
    {
        var fallbackTime = ResolveFallbackDailyRunTimeUtc();
        if (!IsDueNow(fallbackTime, nowUtc))
        {
            return;
        }

        if (await batchRepository.HasBatchRunOnDateAsync(nowUtc, cancellationToken).ConfigureAwait(false))
        {
            return;
        }

        using var scope = scopeFactory.CreateScope();
        var batchService = scope.ServiceProvider.GetRequiredService<IBatchProcessingService>();
        await batchService.ProcessActiveConfigsAsync(cancellationToken).ConfigureAwait(false);
    }

    private static bool IsDueNow(TimeSpan dailyRunTimeUtc, DateTime nowUtc)
    {
        var scheduledTodayUtc = nowUtc.Date.Add(dailyRunTimeUtc);
        return nowUtc >= scheduledTodayUtc;
    }

    private TimeSpan ResolveFallbackDailyRunTimeUtc()
    {
        var schedule = configuration["BatchProcessing:DailyRunTimeUtc"] ?? "00:30";
        return TimeSpan.TryParse(schedule, out var runAtUtc)
            ? runAtUtc
            : new TimeSpan(0, 30, 0);
    }

    private static bool TryParseDailyRunTimeUtc(string? raw, out TimeSpan value, out string error)
    {
        if (string.IsNullOrWhiteSpace(raw))
        {
            value = default;
            error = "Daily run time is required.";
            return false;
        }

        if (!TimeSpan.TryParse(raw.Trim(), out value))
        {
            error = "Daily run time must be in HH:mm:ss format.";
            return false;
        }

        if (value < TimeSpan.Zero || value >= TimeSpan.FromDays(1))
        {
            error = "Daily run time must be between 00:00:00 and 23:59:59.";
            return false;
        }

        error = string.Empty;
        return true;
    }

    private static BatchScheduleItemDto BuildItemDto(
        CampaignBatchType batchType,
        bool isEnabled,
        TimeSpan dailyRunTimeUtc,
        bool isConfigured,
        DateTime? lastRunUtc)
    {
        var nowUtc = DateTime.UtcNow;
        DateTime? nextRunUtc = null;
        if (isEnabled)
        {
            var candidate = nowUtc.Date.Add(dailyRunTimeUtc);
            if (candidate <= nowUtc)
            {
                candidate = candidate.AddDays(1);
            }

            nextRunUtc = candidate;
        }

        return new BatchScheduleItemDto(
            batchType,
            isEnabled,
            dailyRunTimeUtc.ToString(@"c"),
            isConfigured,
            lastRunUtc,
            nextRunUtc);
    }
}
