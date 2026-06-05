using LeadScoring.Api.Contracts;

namespace LeadScoring.Api.Services;

public interface IPaymentService
{
    Task<PaymentOptionsResponse> GetOptionsAsync(Guid userId, CancellationToken cancellationToken = default);

    Task<CreateCheckoutSessionResponse> CreateCheckoutSessionAsync(
        Guid userId,
        string plan,
        string billingInterval,
        string? returnBaseUrl,
        bool isRenewal = false,
        CancellationToken cancellationToken = default);

    Task HandleWebhookAsync(string json, string? stripeSignature, CancellationToken cancellationToken = default);

    Task<SubscriptionSummaryResponse?> GetSubscriptionSummaryAsync(
        Guid userId,
        CancellationToken cancellationToken = default);

    Task<SubscriptionSummaryResponse> RecordTestPaymentAsync(
        Guid userId,
        string plan,
        string billingInterval,
        CancellationToken cancellationToken = default);

    Task<SubscriptionSummaryResponse> ConfirmCheckoutSessionAsync(
        Guid userId,
        string sessionId,
        CancellationToken cancellationToken = default);
}
