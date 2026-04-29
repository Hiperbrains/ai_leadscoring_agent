using System.Net.Http.Json;
using System.Text.Json.Serialization;
using LeadScoring.Api.Models;

namespace LeadScoring.Api.Services;

public class UserSignupStatusService(
    IHttpClientFactory httpClientFactory,
    IConfiguration configuration) : IUserSignupStatusService
{
    public async Task<UserSignupStatusData> CheckUserSignupStatusAsync(Lead lead, CancellationToken cancellationToken)
    {
        var baseUrl = configuration["UserStatusApi:BaseUrl"];
        var path = configuration["UserStatusApi:CheckSignupPath"] ?? "/api/Common/checkusersignupstatus";
        if (string.IsNullOrWhiteSpace(baseUrl))
        {
            throw new InvalidOperationException("UserStatusApi:BaseUrl is missing.");
        }

        var client = httpClientFactory.CreateClient(nameof(UserSignupStatusService));
        var payload = new UserSignupStatusRequest(
            lead.Email,
            lead.FirstName,
            lead.LastName,
            null,
            null);

        using var response = await client.PostAsJsonAsync($"{baseUrl.TrimEnd('/')}{path}", payload, cancellationToken);
        if (!response.IsSuccessStatusCode)
        {
            throw new InvalidOperationException($"Signup status API returned {(int)response.StatusCode}.");
        }

        var apiResponse = await response.Content.ReadFromJsonAsync<CommonApiResponse>(cancellationToken: cancellationToken);
        if (apiResponse?.Data is null)
        {
            throw new InvalidOperationException("Signup status API returned empty data.");
        }

        return apiResponse.Data;
    }

    private sealed record UserSignupStatusRequest(
        string Email,
        string? FirstName,
        string? LastName,
        string? Country,
        string? PhoneNumber);

    private sealed record CommonApiResponse(
        int StatusCode,
        bool IsSuccess,
        string Message,
        UserSignupStatusData? Data);
}

public sealed record UserSignupStatusData(
    string Email,
    bool UserExists,
    bool SignupCompleted,
    bool LoginDataExists,
    bool ProfileCompletion,
    bool IsPlanSelected,
    string? SelectedPlan,
    DateTime? PlanRenewalDate);
