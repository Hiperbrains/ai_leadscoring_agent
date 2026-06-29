using LeadScoring.Api.Contracts;

namespace LeadScoring.Api.Services;

public interface IBatchScheduleService
{
    Task<BatchScheduleListDto> GetForCurrentTenantAsync(CancellationToken cancellationToken);
    Task<BatchScheduleListDto> UpsertForCurrentTenantAsync(UpsertBatchScheduleRequest request, CancellationToken cancellationToken);
    Task ProcessDueSchedulesAsync(CancellationToken cancellationToken);
}
