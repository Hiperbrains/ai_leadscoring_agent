using LeadScoring.Api.Services;

namespace LeadScoring.Api.Background;

public class BatchWorker(
    IServiceScopeFactory scopeFactory,
    IBatchWorkerTelemetry workerTelemetry,
    ILogger<BatchWorker> logger) : BackgroundService
{
    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        logger.LogInformation("Batch worker started. Checking schedules every minute.");

        while (!stoppingToken.IsCancellationRequested)
        {
            try
            {
                using var scope = scopeFactory.CreateScope();
                var scheduleService = scope.ServiceProvider.GetRequiredService<IBatchScheduleService>();
                await scheduleService.ProcessDueSchedulesAsync(stoppingToken);
            }
            catch (Exception ex)
            {
                logger.LogError(ex, "Batch worker cycle failed.");
            }
            finally
            {
                workerTelemetry.RecordCycleCompleted(DateTime.UtcNow);
            }

            await Task.Delay(TimeSpan.FromMinutes(1), stoppingToken);
        }
    }
}
