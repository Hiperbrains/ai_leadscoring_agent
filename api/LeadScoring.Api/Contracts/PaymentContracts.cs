namespace LeadScoring.Api.Contracts;

public record PaymentPlanOptionDto(
    string Name,
    decimal? MonthlyPrice,
    string Description,
    IReadOnlyList<string> Features,
    bool IsCurrent,
    bool IsMostPopular,
    bool CanCheckoutMonthly,
    bool CanCheckoutAnnual);

public record PaymentOptionsResponse(
    string CompanyName,
    string CurrentPlan,
    int AnnualDiscountPercent,
    bool PersistPayments,
    IReadOnlyList<PaymentPlanOptionDto> Plans);

public record ConfirmCheckoutRequest(string SessionId);

public record CreateCheckoutSessionRequest(string Plan, string BillingInterval, string? ReturnBaseUrl, bool IsRenewal = false);

public record ProductCreditUsageDto(int ProductId, string ProductName, int CreditsUsed);

public record CreateCheckoutSessionResponse(string CheckoutUrl);

public record PaymentHistoryItemDto(
    Guid PaymentId,
    long Amount,
    string Currency,
    string PaymentStatus,
    DateTime PaymentDate,
    string? StripeSessionId);

public record SubscriptionSummaryResponse(
    string CompanyName,
    string CurrentPlan,
    string? BillingType,
    DateTime? StartDate,
    DateTime? RenewDate,
    DateTime? ExpireDate,
    long? LastPaymentAmount,
    string? PaymentStatus,
    int ProjectCreditsTotal,
    int ProjectCreditsUsed,
    int ProjectCreditsRemaining,
    bool IsActive,
    IReadOnlyList<ProductCreditUsageDto> ProductCreditUsage,
    IReadOnlyList<PaymentHistoryItemDto> RecentPayments);
