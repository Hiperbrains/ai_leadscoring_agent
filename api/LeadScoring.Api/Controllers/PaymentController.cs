using System.Security.Claims;
using LeadScoring.Api.Contracts;
using LeadScoring.Api.Services;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;

namespace LeadScoring.Api.Controllers;

[ApiController]
[Route("api/payment")]
public class PaymentController(IPaymentService paymentService, IWebHostEnvironment environment) : ControllerBase
{
    [HttpGet("options")]
    [Authorize]
    public async Task<ActionResult<PaymentOptionsResponse>> GetOptions(CancellationToken cancellationToken)
    {
        var userId = GetUserId();
        if (userId is null)
        {
            return Unauthorized();
        }

        return Ok(await paymentService.GetOptionsAsync(userId.Value, cancellationToken));
    }

    [HttpPost("checkout-session")]
    [Authorize]
    public async Task<ActionResult<CreateCheckoutSessionResponse>> CreateCheckoutSession(
        [FromBody] CreateCheckoutSessionRequest request,
        CancellationToken cancellationToken)
    {
        var userId = GetUserId();
        if (userId is null)
        {
            return Unauthorized();
        }

        try
        {
            var response = await paymentService.CreateCheckoutSessionAsync(
                userId.Value,
                request.Plan,
                request.BillingInterval,
                request.ReturnBaseUrl,
                request.IsRenewal,
                cancellationToken);
            return Ok(response);
        }
        catch (PaymentValidationException ex)
        {
            return BadRequest(new { message = ex.Message });
        }
    }

    [HttpGet("subscription")]
    [Authorize]
    public async Task<ActionResult<SubscriptionSummaryResponse>> GetSubscription(CancellationToken cancellationToken)
    {
        var userId = GetUserId();
        if (userId is null)
        {
            return Unauthorized();
        }

        var summary = await paymentService.GetSubscriptionSummaryAsync(userId.Value, cancellationToken);
        return Ok(summary);
    }

    /// <summary>Development only — records a test payment without Stripe checkout.</summary>
    [HttpPost("record-test")]
    [Authorize]
    public async Task<ActionResult<SubscriptionSummaryResponse>> RecordTestPayment(
        [FromBody] CreateCheckoutSessionRequest request,
        CancellationToken cancellationToken)
    {
        if (!environment.IsDevelopment())
        {
            return NotFound();
        }

        var userId = GetUserId();
        if (userId is null)
        {
            return Unauthorized();
        }

        try
        {
            var summary = await paymentService.RecordTestPaymentAsync(
                userId.Value,
                request.Plan,
                request.BillingInterval,
                cancellationToken);
            return Ok(summary);
        }
        catch (PaymentValidationException ex)
        {
            return BadRequest(new { message = ex.Message });
        }
    }

    [HttpPost("confirm-checkout")]
    [Authorize]
    public async Task<ActionResult<SubscriptionSummaryResponse>> ConfirmCheckout(
        [FromBody] ConfirmCheckoutRequest request,
        CancellationToken cancellationToken)
    {
        var userId = GetUserId();
        if (userId is null)
        {
            return Unauthorized();
        }

        try
        {
            var summary = await paymentService.ConfirmCheckoutSessionAsync(
                userId.Value,
                request.SessionId,
                cancellationToken);
            return Ok(summary);
        }
        catch (PaymentValidationException ex)
        {
            return BadRequest(new { message = ex.Message });
        }
    }

    [HttpPost("webhook")]
    [AllowAnonymous]
    public async Task<IActionResult> Webhook(CancellationToken cancellationToken)
    {
        var json = await new StreamReader(HttpContext.Request.Body).ReadToEndAsync(cancellationToken);
        var signature = Request.Headers["Stripe-Signature"].FirstOrDefault();

        try
        {
            await paymentService.HandleWebhookAsync(json, signature, cancellationToken);
            return Ok();
        }
        catch (Stripe.StripeException ex)
        {
            return BadRequest(new { message = ex.Message });
        }
    }

    private Guid? GetUserId()
    {
        var sub = User.FindFirstValue("sub") ?? User.FindFirstValue(ClaimTypes.NameIdentifier);
        return Guid.TryParse(sub, out var userId) ? userId : null;
    }
}
