using LeadScoring.Api.Services;

namespace LeadScoring.Api.Background;

public class BatchWorker(
    IServiceScopeFactory scopeFactory,
    IConfiguration configuration,
    ILogger<BatchWorker> logger) : BackgroundService
{
    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        var intervalMinutes = configuration.GetValue<int?>("BatchProcessing:IntervalMinutes") ?? 5;
        if (intervalMinutes < 1)
        {
            intervalMinutes = 1;
        }

        logger.LogInformation("Batch worker started. Interval={IntervalMinutes} minute(s).", intervalMinutes);

        while (!stoppingToken.IsCancellationRequested)
        {
            try
            {
                using var scope = scopeFactory.CreateScope();
                var batchService = scope.ServiceProvider.GetRequiredService<IBatchProcessingService>();
                await batchService.ProcessActiveConfigsAsync(stoppingToken);
            }
            catch (Exception ex)
            {
                logger.LogError(ex, "Batch worker cycle failed.");
            }

            await Task.Delay(TimeSpan.FromMinutes(intervalMinutes), stoppingToken);
        }
    }
}
