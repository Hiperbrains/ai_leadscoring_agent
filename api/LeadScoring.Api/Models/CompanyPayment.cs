namespace LeadScoring.Api.Models;

public class CompanyPayment
{
    public Guid PaymentId { get; set; }
    public Guid CompanyId { get; set; }
    public Guid SubscriptionId { get; set; }
    public string? StripeSessionId { get; set; }
    public string? StripePaymentIntentId { get; set; }
    public long Amount { get; set; }
    public string Currency { get; set; } = "usd";
    public string PaymentStatus { get; set; } = string.Empty;
    public DateTime PaymentDate { get; set; }
    public string? InvoiceUrl { get; set; }
    public DateTime CreatedDate { get; set; }

    public Tenant Company { get; set; } = null!;
    public CompanySubscription Subscription { get; set; } = null!;
}
