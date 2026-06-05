using LeadScoring.Api.Contracts;
using LeadScoring.Api.Data;
using LeadScoring.Api.Models;
using Microsoft.EntityFrameworkCore;
using Stripe;
using Stripe.Checkout;

namespace LeadScoring.Api.Services;

public class PaymentService(
    MasterDbContext masterDb,
    ITenantDbContextAccessor tenantDbAccessor,
    IConfiguration configuration,
    ILogger<PaymentService> logger) : IPaymentService
{
    private const int DefaultAnnualDiscountPercent = 11;

    public async Task<PaymentOptionsResponse> GetOptionsAsync(Guid userId, CancellationToken cancellationToken = default)
    {
        var (user, tenant) = await LoadUserTenantAsync(userId, cancellationToken);
        var planNames = GetConfiguredPlanNames();
        var currentPlan = NormalizePlanName(tenant.SelectedPlan?.Trim() ?? "");

        var activeSubscription = await masterDb.Subscriptions
            .AsNoTracking()
            .Where(s => s.CompanyId == tenant.Id && s.IsActive)
            .OrderByDescending(s => s.CreatedDate)
            .Select(s => s.CurrentPlan)
            .FirstOrDefaultAsync(cancellationToken);

        var paidPlan = string.IsNullOrWhiteSpace(activeSubscription)
            ? null
            : NormalizePlanName(activeSubscription);

        var options = planNames.Select(name =>
        {
            var meta = GetPlanMeta(name);
            var normalizedName = NormalizePlanName(name);
            var isCurrent = paidPlan is not null
                && string.Equals(normalizedName, paidPlan, StringComparison.OrdinalIgnoreCase);
            return new PaymentPlanOptionDto(
                normalizedName,
                meta.MonthlyPrice,
                meta.Description,
                meta.Features,
                isCurrent,
                string.Equals(normalizedName, "Growth", StringComparison.OrdinalIgnoreCase),
                CanCheckoutPlan(normalizedName, isCurrent),
                CanCheckoutPlan(normalizedName, isCurrent));
        }).ToList();

        return new PaymentOptionsResponse(
            tenant.CompanyName,
            currentPlan,
            GetAnnualDiscountPercent(),
            GetPersistPayments(),
            options);
    }

    public async Task<CreateCheckoutSessionResponse> CreateCheckoutSessionAsync(
        Guid userId,
        string plan,
        string billingInterval,
        string? returnBaseUrl,
        bool isRenewal = false,
        CancellationToken cancellationToken = default)
    {
        var normalizedPlan = NormalizePlanName(plan?.Trim() ?? "");
        var interval = NormalizeBillingInterval(billingInterval);
        var planNames = GetConfiguredPlanNames().Select(NormalizePlanName).ToList();

        if (!planNames.Contains(normalizedPlan, StringComparer.OrdinalIgnoreCase))
        {
            throw new PaymentValidationException("Please select a valid plan.");
        }

        var (user, tenant) = await LoadUserTenantAsync(userId, cancellationToken);
        var hasActiveSubscription = await masterDb.Subscriptions
            .AnyAsync(s => s.CompanyId == tenant.Id && s.IsActive, cancellationToken);

        if (!isRenewal && hasActiveSubscription)
        {
            var activePlan = await masterDb.Subscriptions
                .AsNoTracking()
                .Where(s => s.CompanyId == tenant.Id && s.IsActive)
                .OrderByDescending(s => s.CreatedDate)
                .Select(s => s.CurrentPlan)
                .FirstOrDefaultAsync(cancellationToken);

            if (!string.IsNullOrWhiteSpace(activePlan)
                && string.Equals(NormalizePlanName(activePlan), normalizedPlan, StringComparison.OrdinalIgnoreCase))
            {
                throw new PaymentValidationException("You are already on this plan.");
            }
        }

        if (!CanCheckoutPlan(normalizedPlan, isCurrent: false))
        {
            throw new PaymentValidationException(
                "Online checkout is not available for this plan. Contact sales for Enterprise pricing.");
        }

        var secretKey = configuration["Stripe:SecretKey"]?.Trim();
        if (string.IsNullOrWhiteSpace(secretKey))
        {
            throw new PaymentValidationException("Stripe is not configured. Set Stripe:SecretKey in application settings.");
        }

        var meta = GetPlanMeta(normalizedPlan);
        if (meta.MonthlyPrice is null)
        {
            throw new PaymentValidationException("Enterprise plans require contacting sales.");
        }

        StripeConfiguration.ApiKey = secretKey;

        var successUrl = BuildPaymentReturnUrl(returnBaseUrl, "success");
        var cancelUrl = BuildPaymentReturnUrl(returnBaseUrl, "cancelled");

        var priceId = GetStripePriceId(normalizedPlan, interval);
        var lineItem = !string.IsNullOrWhiteSpace(priceId)
            ? new SessionLineItemOptions { Price = priceId, Quantity = 1 }
            : BuildInlineLineItem(normalizedPlan, interval, meta);

        var persistPayments = GetPersistPayments();
        var sessionService = new SessionService();
        var session = await sessionService.CreateAsync(new SessionCreateOptions
        {
            Mode = "subscription",
            CustomerEmail = user.Email,
            ClientReferenceId = tenant.Id.ToString(),
            LineItems = [lineItem],
            Metadata = new Dictionary<string, string>
            {
                ["tenantId"] = tenant.Id.ToString(),
                ["userId"] = user.Id.ToString(),
                ["plan"] = normalizedPlan,
                ["billingInterval"] = interval,
                ["companyName"] = tenant.CompanyName,
                ["persistPayments"] = persistPayments.ToString().ToLowerInvariant(),
                ["isRenewal"] = isRenewal.ToString().ToLowerInvariant()
            },
            SuccessUrl = successUrl,
            CancelUrl = cancelUrl
        }, cancellationToken: cancellationToken);

        if (string.IsNullOrWhiteSpace(session.Url))
        {
            throw new PaymentValidationException("Stripe did not return a checkout URL.");
        }

        return new CreateCheckoutSessionResponse(session.Url);
    }

    public async Task HandleWebhookAsync(string json, string? stripeSignature, CancellationToken cancellationToken = default)
    {
        if (!GetPersistPayments())
        {
            logger.LogInformation(
                "Stripe webhook ignored because PersistPayments is disabled (subscription tables not live).");
            return;
        }

        var webhookSecret = configuration["Stripe:WebhookSecret"]?.Trim();
        if (string.IsNullOrWhiteSpace(webhookSecret))
        {
            logger.LogWarning("Stripe webhook received but Stripe:WebhookSecret is not configured.");
            return;
        }

        var stripeEvent = EventUtility.ConstructEvent(json, stripeSignature, webhookSecret);

        if (stripeEvent.Type != EventTypes.CheckoutSessionCompleted)
        {
            return;
        }

        if (stripeEvent.Data.Object is not Session session)
        {
            return;
        }

        await ProcessCompletedCheckoutSessionAsync(session, cancellationToken);
    }

    public async Task<SubscriptionSummaryResponse> ConfirmCheckoutSessionAsync(
        Guid userId,
        string sessionId,
        CancellationToken cancellationToken = default)
    {
        if (string.IsNullOrWhiteSpace(sessionId))
        {
            throw new PaymentValidationException("Checkout session id is required.");
        }

        if (!GetPersistPayments())
        {
            throw new PaymentValidationException(
                "Payment persistence is disabled. Set Stripe:PersistPayments to true in application settings.");
        }

        var (_, tenant) = await LoadUserTenantAsync(userId, cancellationToken);

        var secretKey = configuration["Stripe:SecretKey"]?.Trim();
        if (string.IsNullOrWhiteSpace(secretKey))
        {
            throw new PaymentValidationException("Stripe is not configured.");
        }

        StripeConfiguration.ApiKey = secretKey;
        var session = await new SessionService().GetAsync(sessionId.Trim(), cancellationToken: cancellationToken);

        if (!string.Equals(session.Status, "complete", StringComparison.OrdinalIgnoreCase))
        {
            throw new PaymentValidationException("Checkout is not complete yet. Wait a moment and refresh.");
        }

        if (!Guid.TryParse(session.Metadata.GetValueOrDefault("tenantId"), out var sessionTenantId))
        {
            if (!Guid.TryParse(session.ClientReferenceId, out sessionTenantId))
            {
                throw new PaymentValidationException("Checkout session is missing company information.");
            }
        }

        if (sessionTenantId != tenant.Id)
        {
            throw new PaymentValidationException("This checkout session does not belong to your company.");
        }

        if (session.Metadata.GetValueOrDefault("persistPayments") is "false")
        {
            throw new PaymentValidationException("This checkout was created in preview mode and cannot be saved.");
        }

        await ProcessCompletedCheckoutSessionAsync(session, cancellationToken);

        return (await GetSubscriptionSummaryAsync(userId, cancellationToken))!;
    }

    private async Task ProcessCompletedCheckoutSessionAsync(Session session, CancellationToken cancellationToken)
    {
        if (!GetPersistPayments())
        {
            logger.LogInformation(
                "Skipping checkout session {SessionId} because PersistPayments is disabled.",
                session.Id);
            return;
        }

        if (session.Metadata.GetValueOrDefault("persistPayments") is "false")
        {
            logger.LogInformation(
                "Checkout session {SessionId} completed in preview mode; skipping persistence.",
                session.Id);
            return;
        }

        if (!string.IsNullOrWhiteSpace(session.Id))
        {
            var alreadyRecorded = await masterDb.Payments
                .AnyAsync(p => p.StripeSessionId == session.Id, cancellationToken);
            if (alreadyRecorded)
            {
                logger.LogInformation("Checkout session {SessionId} was already recorded.", session.Id);
                return;
            }
        }

        var plan = session.Metadata.GetValueOrDefault("plan");
        if (string.IsNullOrWhiteSpace(plan))
        {
            logger.LogWarning("Checkout session {SessionId} completed without plan metadata.", session.Id);
            return;
        }

        if (!Guid.TryParse(session.Metadata.GetValueOrDefault("tenantId"), out var tenantId))
        {
            if (!Guid.TryParse(session.ClientReferenceId, out tenantId))
            {
                logger.LogWarning("Checkout session {SessionId} completed without tenant id.", session.Id);
                return;
            }
        }

        var tenant = await masterDb.Tenants.FirstOrDefaultAsync(t => t.Id == tenantId, cancellationToken);
        if (tenant is null)
        {
            logger.LogWarning("Checkout session {SessionId} references unknown tenant {TenantId}.", session.Id, tenantId);
            return;
        }

        var billingInterval = session.Metadata.GetValueOrDefault("billingInterval") ?? "monthly";
        await PersistSuccessfulPaymentAsync(
            tenantId,
            plan,
            billingInterval,
            session.AmountTotal,
            session.Currency,
            session.Id,
            session.PaymentIntentId,
            session.CustomerId,
            session.SubscriptionId,
            cancellationToken);

        logger.LogInformation(
            "Recorded subscription and payment for tenant {TenantId} ({Company}) plan {Plan}.",
            tenantId,
            tenant.CompanyName,
            NormalizePlanName(plan.Trim()));
    }

    public async Task<SubscriptionSummaryResponse?> GetSubscriptionSummaryAsync(
        Guid userId,
        CancellationToken cancellationToken = default)
    {
        var (_, tenant) = await LoadUserTenantAsync(userId, cancellationToken);
        var currentPlan = NormalizePlanName(tenant.SelectedPlan?.Trim() ?? "");

        var subscription = await masterDb.Subscriptions
            .AsNoTracking()
            .Where(s => s.CompanyId == tenant.Id && s.IsActive)
            .OrderByDescending(s => s.CreatedDate)
            .FirstOrDefaultAsync(cancellationToken);

        var productCreditUsage = await LoadProductCreditUsageAsync(tenant.CompanyName, cancellationToken);
        var creditsUsed = productCreditUsage.Sum(x => x.CreditsUsed);
        var creditsTotal = subscription?.ProjectCreditsTotal ?? GetProjectCreditsForPlan(currentPlan);
        var creditsRemaining = Math.Max(0, creditsTotal - creditsUsed);

        var payments = await masterDb.Payments
            .AsNoTracking()
            .Where(p => p.CompanyId == tenant.Id)
            .OrderByDescending(p => p.PaymentDate)
            .Take(10)
            .Select(p => new PaymentHistoryItemDto(
                p.PaymentId,
                p.Amount,
                p.Currency,
                p.PaymentStatus,
                p.PaymentDate,
                p.StripeSessionId))
            .ToListAsync(cancellationToken);

        if (subscription is null)
        {
            return new SubscriptionSummaryResponse(
                tenant.CompanyName,
                currentPlan,
                null,
                null,
                null,
                null,
                null,
                null,
                creditsTotal,
                creditsUsed,
                creditsRemaining,
                false,
                productCreditUsage,
                payments);
        }

        return new SubscriptionSummaryResponse(
            tenant.CompanyName,
            subscription.CurrentPlan,
            subscription.BillingType,
            subscription.StartDate,
            subscription.RenewDate,
            subscription.ExpireDate,
            subscription.LastPaymentAmount,
            subscription.PaymentStatus,
            creditsTotal,
            creditsUsed,
            creditsRemaining,
            subscription.IsActive,
            productCreditUsage,
            payments);
    }

    private async Task<IReadOnlyList<ProductCreditUsageDto>> LoadProductCreditUsageAsync(
        string companyName,
        CancellationToken cancellationToken)
    {
        var normalizedCompany = companyName.Trim();
        var companyLower = normalizedCompany.ToLowerInvariant();

        await using var tenantDb = tenantDbAccessor.GetDbContext();

        var products = await tenantDb.CompanyProductConfigs
            .AsNoTracking()
            .Where(x => x.CompanyName.ToLower() == companyLower)
            .OrderBy(x => x.ProductName)
            .ThenBy(x => x.ProductId)
            .Select(x => new { x.ProductId, x.ProductName })
            .ToListAsync(cancellationToken);

        if (products.Count == 0)
        {
            return [];
        }

        var leadCounts = await tenantDb.Leads
            .AsNoTracking()
            .Where(l =>
                l.CompanyName != null
                && EF.Functions.ILike(l.CompanyName, normalizedCompany))
            .GroupBy(l => l.ProductId)
            .Select(g => new { ProductId = g.Key, Count = g.Count() })
            .ToListAsync(cancellationToken);

        var countByProduct = leadCounts
            .Where(x => x.ProductId.HasValue)
            .ToDictionary(x => x.ProductId!.Value, x => x.Count);

        return products
            .Select(p => new ProductCreditUsageDto(
                p.ProductId,
                p.ProductName,
                countByProduct.GetValueOrDefault(p.ProductId)))
            .ToList();
    }

    public async Task<SubscriptionSummaryResponse> RecordTestPaymentAsync(
        Guid userId,
        string plan,
        string billingInterval,
        CancellationToken cancellationToken = default)
    {
        var normalizedPlan = NormalizePlanName(plan?.Trim() ?? "");
        var interval = NormalizeBillingInterval(billingInterval);
        var planNames = GetConfiguredPlanNames().Select(NormalizePlanName).ToList();

        if (!planNames.Contains(normalizedPlan, StringComparer.OrdinalIgnoreCase))
        {
            throw new PaymentValidationException("Please select a valid plan.");
        }

        if (string.Equals(normalizedPlan, "Enterprise", StringComparison.OrdinalIgnoreCase))
        {
            throw new PaymentValidationException("Enterprise plans require contacting sales.");
        }

        var (_, tenant) = await LoadUserTenantAsync(userId, cancellationToken);
        var amount = CalculatePlanAmountCents(normalizedPlan, interval);

        await PersistSuccessfulPaymentAsync(
            tenant.Id,
            normalizedPlan,
            interval,
            amount,
            "usd",
            stripeSessionId: $"test_sess_{Guid.NewGuid():N}",
            stripePaymentIntentId: $"test_pi_{Guid.NewGuid():N}",
            stripeCustomerId: null,
            stripeSubscriptionId: null,
            cancellationToken);

        return (await GetSubscriptionSummaryAsync(userId, cancellationToken))!;
    }

    private async Task PersistSuccessfulPaymentAsync(
        Guid tenantId,
        string plan,
        string billingInterval,
        long? amountCents,
        string? currency,
        string? stripeSessionId,
        string? stripePaymentIntentId,
        string? stripeCustomerId,
        string? stripeSubscriptionId,
        CancellationToken cancellationToken)
    {
        var tenant = await masterDb.Tenants.FirstOrDefaultAsync(t => t.Id == tenantId, cancellationToken);
        if (tenant is null)
        {
            logger.LogWarning("Cannot persist payment: tenant {TenantId} not found.", tenantId);
            return;
        }

        var normalizedPlan = NormalizePlanName(plan.Trim());
        var interval = NormalizeBillingInterval(billingInterval);
        var billingType = interval == "annual" ? "Annual" : "Monthly";
        var now = DateTime.UtcNow;
        var amount = amountCents ?? CalculatePlanAmountCents(normalizedPlan, interval);

        var activeSubscriptions = await masterDb.Subscriptions
            .Where(s => s.CompanyId == tenantId && s.IsActive)
            .ToListAsync(cancellationToken);

        foreach (var existing in activeSubscriptions)
        {
            existing.IsActive = false;
            existing.UpdatedDate = now;
            existing.ExpireDate = now;
            existing.PaymentStatus = "replaced";
        }

        var renewDate = interval == "annual" ? now.AddYears(1) : now.AddMonths(1);
        var subscription = new CompanySubscription
        {
            SubscriptionId = Guid.NewGuid(),
            CompanyId = tenantId,
            CurrentPlan = normalizedPlan,
            BillingType = billingType,
            StartDate = now,
            RenewDate = renewDate,
            LastPaymentAmount = amount,
            StripeCustomerId = stripeCustomerId,
            StripeSubscriptionId = stripeSubscriptionId,
            PaymentStatus = "active",
            IsActive = true,
            CreatedDate = now,
            ProjectCreditsTotal = GetProjectCreditsForPlan(normalizedPlan)
        };

        var payment = new CompanyPayment
        {
            PaymentId = Guid.NewGuid(),
            CompanyId = tenantId,
            SubscriptionId = subscription.SubscriptionId,
            StripeSessionId = stripeSessionId,
            StripePaymentIntentId = stripePaymentIntentId,
            Amount = amount,
            Currency = (currency ?? "usd").ToLowerInvariant(),
            PaymentStatus = "paid",
            PaymentDate = now,
            CreatedDate = now
        };

        tenant.SelectedPlan = normalizedPlan;
        masterDb.Subscriptions.Add(subscription);
        masterDb.Payments.Add(payment);
        await masterDb.SaveChangesAsync(cancellationToken);
    }

    private long CalculatePlanAmountCents(string planName, string interval)
    {
        var meta = GetPlanMeta(planName);
        if (meta.MonthlyPrice is null)
        {
            return 0;
        }

        if (interval == "annual")
        {
            var discount = GetAnnualDiscountPercent() / 100m;
            var yearlyTotal = meta.MonthlyPrice.Value * 12m * (1m - discount);
            return (long)Math.Round(yearlyTotal * 100m, MidpointRounding.AwayFromZero);
        }

        return (long)Math.Round(meta.MonthlyPrice.Value * 100m, MidpointRounding.AwayFromZero);
    }

    private static int GetProjectCreditsForPlan(string planName) =>
        planName switch
        {
            "Starter" => 1000,
            "Growth" => 5000,
            _ => 0
        };

    private async Task<(Models.AppUser User, Models.Tenant Tenant)> LoadUserTenantAsync(
        Guid userId,
        CancellationToken cancellationToken)
    {
        var user = await masterDb.Users
            .Include(u => u.Tenant)
            .FirstOrDefaultAsync(u => u.Id == userId, cancellationToken);

        if (user?.Tenant is null)
        {
            throw new PaymentValidationException("User account not found.");
        }

        return (user, user.Tenant);
    }

    private IReadOnlyList<string> GetConfiguredPlanNames() =>
        configuration.GetSection("Auth:Plans").Get<string[]>()
        ?? ["Starter", "Growth", "Enterprise"];

    private int GetAnnualDiscountPercent() =>
        configuration.GetValue("Stripe:AnnualDiscountPercent", DefaultAnnualDiscountPercent);

    private bool IsStripeConfigured() =>
        !string.IsNullOrWhiteSpace(configuration["Stripe:SecretKey"]?.Trim());

    private bool GetPersistPayments() =>
        configuration.GetValue("Stripe:PersistPayments", false);

    private bool CanCheckoutPlan(string planName, bool isCurrent)
    {
        if (isCurrent || string.Equals(planName, "Enterprise", StringComparison.OrdinalIgnoreCase))
        {
            return false;
        }

        return IsStripeConfigured();
    }

    private string BuildPaymentReturnUrl(string? returnBaseUrl, string status)
    {
        var path = string.Equals(status, "success", StringComparison.OrdinalIgnoreCase)
            ? "/settings/payment?status=success&session_id={CHECKOUT_SESSION_ID}"
            : "/settings/payment?status=cancelled";

        if (TryResolveFrontendOrigin(returnBaseUrl, out var origin))
        {
            return $"{origin}{path}";
        }

        var configuredSuccess = configuration["Stripe:SuccessUrl"]?.Trim();
        if (status.Equals("success", StringComparison.OrdinalIgnoreCase)
            && !string.IsNullOrWhiteSpace(configuredSuccess))
        {
            return AppendCheckoutSessionPlaceholder(configuredSuccess);
        }

        var configuredCancel = configuration["Stripe:CancelUrl"]?.Trim();
        if (status.Equals("cancelled", StringComparison.OrdinalIgnoreCase)
            && !string.IsNullOrWhiteSpace(configuredCancel))
        {
            return configuredCancel;
        }

        var fallbackOrigin = configuration["Stripe:FrontendBaseUrl"]?.Trim().TrimEnd('/')
            ?? "http://localhost:4201";
        return $"{fallbackOrigin}{path}";
    }

    private static string AppendCheckoutSessionPlaceholder(string successUrl)
    {
        if (successUrl.Contains("{CHECKOUT_SESSION_ID}", StringComparison.Ordinal))
        {
            return successUrl;
        }

        var separator = successUrl.Contains('?', StringComparison.Ordinal) ? '&' : '?';
        return $"{successUrl}{separator}session_id={{CHECKOUT_SESSION_ID}}";
    }

    private bool TryResolveFrontendOrigin(string? returnBaseUrl, out string origin)
    {
        origin = string.Empty;
        if (string.IsNullOrWhiteSpace(returnBaseUrl))
        {
            return false;
        }

        if (!Uri.TryCreate(returnBaseUrl.Trim(), UriKind.Absolute, out var uri))
        {
            return false;
        }

        if (uri.Scheme is not ("http" or "https") || string.IsNullOrWhiteSpace(uri.Host))
        {
            return false;
        }

        var candidate = $"{uri.Scheme}://{uri.Authority}";
        var allowedOrigins = configuration.GetSection("Cors:AllowedOrigins").Get<string[]>()
            ?? Array.Empty<string>();

        if (allowedOrigins.Contains(candidate, StringComparer.OrdinalIgnoreCase))
        {
            origin = candidate;
            return true;
        }

        if (uri.Host.Equals("localhost", StringComparison.OrdinalIgnoreCase) || uri.Host == "127.0.0.1")
        {
            origin = candidate;
            return true;
        }

        return false;
    }

    private SessionLineItemOptions BuildInlineLineItem(
        string planName,
        string interval,
        (decimal? MonthlyPrice, string Description, IReadOnlyList<string> Features) meta)
    {
        var monthly = meta.MonthlyPrice!.Value;
        var isAnnual = interval.Equals("annual", StringComparison.OrdinalIgnoreCase);
        long unitAmountCents;
        string stripeInterval;

        if (isAnnual)
        {
            var discount = GetAnnualDiscountPercent() / 100m;
            var yearlyTotal = monthly * 12m * (1m - discount);
            unitAmountCents = (long)Math.Round(yearlyTotal * 100m, MidpointRounding.AwayFromZero);
            stripeInterval = "year";
        }
        else
        {
            unitAmountCents = (long)Math.Round(monthly * 100m, MidpointRounding.AwayFromZero);
            stripeInterval = "month";
        }

        return new SessionLineItemOptions
        {
            Quantity = 1,
            PriceData = new SessionLineItemPriceDataOptions
            {
                Currency = "usd",
                UnitAmount = unitAmountCents,
                Recurring = new SessionLineItemPriceDataRecurringOptions
                {
                    Interval = stripeInterval,
                    IntervalCount = 1
                },
                ProductData = new SessionLineItemPriceDataProductDataOptions
                {
                    Name = $"{planName} plan (LeadScoring)",
                    Description = meta.Description
                }
            }
        };
    }

    private string? GetStripePriceId(string planName, string interval) =>
        configuration[$"Stripe:PriceIds:{planName}:{CapitalizeInterval(interval)}"]?.Trim()
        ?? configuration[$"Stripe:PriceIds:{planName}"]?.Trim();

    private static string CapitalizeInterval(string interval) =>
        interval.Equals("annual", StringComparison.OrdinalIgnoreCase) ? "Annual" : "Monthly";

    private static string NormalizeBillingInterval(string? interval)
    {
        if (string.Equals(interval, "annual", StringComparison.OrdinalIgnoreCase))
        {
            return "annual";
        }

        return "monthly";
    }

    private static string NormalizePlanName(string planName)
    {
        if (planName.Equals("Professional", StringComparison.OrdinalIgnoreCase))
        {
            return "Growth";
        }

        return planName;
    }

    private (decimal? MonthlyPrice, string Description, IReadOnlyList<string> Features) GetPlanMeta(string planName)
    {
        var normalized = NormalizePlanName(planName);
        var section = configuration.GetSection($"Stripe:PlanDetails:{normalized}");
        var description = section["Description"]?.Trim() ?? "";
        var monthlyPrice = section.GetValue<decimal?>("MonthlyPrice");
        var features = section.GetSection("Features").Get<string[]>() ?? [];

        if (!string.IsNullOrWhiteSpace(description) && features.Length > 0)
        {
            return (monthlyPrice, description, features);
        }

        return normalized switch
        {
            "Starter" => (
                99m,
                "Everything you need to start discovering and contacting your first prospects.",
                [
                    "Up to 1,000 leads / month",
                    "1 active ICP profile",
                    "Email campaign automation",
                    "Basic engagement tracking",
                    "Full engagement intelligence",
                    "AI lead scoring + priority queue",
                    "Salesforce & HubSpot integration",
                    "Revenue forecasting dashboard"
                ]),
            "Growth" => (
                199m,
                "The full autonomous growth engine. Ideal for sales teams ready to scale pipeline.",
                [
                    "Up to 5,000 leads / month",
                    "Unlimited ICP profiles",
                    "Multi-channel campaigns (Email + LinkedIn)",
                    "Full engagement intelligence",
                    "AI lead scoring + priority queue",
                    "Salesforce & HubSpot integration",
                    "Revenue forecasting dashboard"
                ]),
            "Enterprise" => (
                null,
                "Dedicated infrastructure, custom AI models, and white-glove onboarding for large teams.",
                [
                    "Unlimited leads & seats",
                    "Dedicated AI models per ICP",
                    "Custom data integrations & APIs",
                    "SSO, SCIM + advanced security",
                    "99.9% SLA + priority support",
                    "Dedicated customer success manager",
                    "SOC 2 Type II, GDPR, HIPAA"
                ]),
            _ => (null, "", Array.Empty<string>())
        };
    }
}

public class PaymentValidationException(string message) : Exception(message);
