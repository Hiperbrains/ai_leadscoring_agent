using LeadScoring.Api.Services;

namespace LeadScoring.Api.Background;

public class InactivityWorker(IServiceScopeFactory scopeFactory, ILogger<InactivityWorker> logger) : BackgroundService
{
    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        while (!stoppingToken.IsCancellationRequested)
        {
            try
            {
                using var scope = scopeFactory.CreateScope();
                var scoring = scope.ServiceProvider.GetRequiredService<LeadScoringService>();
                await scoring.CheckFirstEmailScoreUpdateAsync(TimeSpan.FromHours(1));
            }
            catch (Exception ex)
            {
                logger.LogError(ex, "Hourly score-check worker failed.");
            }

            await Task.Delay(TimeSpan.FromMinutes(60), stoppingToken);
        }
    }
}
