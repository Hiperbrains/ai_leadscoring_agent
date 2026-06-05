namespace LeadScoring.Api.Models;

public class CompanySubscription
{
    public Guid SubscriptionId { get; set; }
    public Guid CompanyId { get; set; }
    public string CurrentPlan { get; set; } = string.Empty;
    public string BillingType { get; set; } = "Monthly";
    public DateTime StartDate { get; set; }
    public DateTime? RenewDate { get; set; }
    public DateTime? ExpireDate { get; set; }
    public long? LastPaymentAmount { get; set; }
    public string? StripeCustomerId { get; set; }
    public string? StripeSubscriptionId { get; set; }
    public string PaymentStatus { get; set; } = "active";
    public bool IsActive { get; set; } = true;
    public DateTime CreatedDate { get; set; }
    public DateTime? UpdatedDate { get; set; }
    public int ProjectCreditsTotal { get; set; }

    public Tenant Company { get; set; } = null!;
    public ICollection<CompanyPayment> Payments { get; set; } = [];
}
