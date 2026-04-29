using LeadScoring.Api.Models;

namespace LeadScoring.Api.Services;

public interface IUserSignupStatusService
{
    Task<UserSignupStatusData> CheckUserSignupStatusAsync(Lead lead, CancellationToken cancellationToken);
}
