using LeadScoring.Api.Contracts;
using LeadScoring.Api.Data;
using LeadScoring.Api.Services;
using Microsoft.AspNetCore.Mvc;

namespace LeadScoring.Api.Controllers;

[ApiController]
[Route("api/leads")]
public class LeadsController(
    LeadScoringDbContext db,
    TokenService tokenService,
    IEmailService emailService,
    LeadImportService leadImportService) : ControllerBase
{
    [HttpPost("import-file")]
    [Consumes("multipart/form-data")]
    public async Task<IActionResult> ImportFile([FromForm] ImportLeadsRequest request)
    {
        var file = request.File;
        if (file is null)
        {
            return BadRequest("File is required.");
        }

        if (file.Length == 0)
        {
            return BadRequest("Empty file.");
        }

        var result = await leadImportService.ImportFromFileAsync(file, request.Source);
        return Ok(result);
    }

    [HttpPost("import-hubspot-csv")]
    [Consumes("multipart/form-data")]
    public Task<IActionResult> ImportHubspotCsv([FromForm] ImportLeadsRequest request)
    {
        request.Source ??= "hubspot";
        return ImportFile(request);
    }

    [HttpPost("import-json")]
    public async Task<IActionResult> ImportJson([FromBody] LeadImportPayload payload)
    {
        if (payload.Leads.Count == 0)
        {
            return BadRequest("No leads provided.");
        }

        var result = await leadImportService.ImportFromPayloadAsync(payload);
        return Ok(result);
    }

    [HttpPost("{leadId:guid}/send-email")]
    public async Task<IActionResult> SendEmail(Guid leadId, [FromBody] SendEmailRequest request)
    {
        var lead = await db.Leads.FindAsync(leadId);
        if (lead is null)
        {
            return NotFound("Lead not found.");
        }

        var token = tokenService.CreateLeadToken(lead.Id);
        var baseUrl = $"{Request.Scheme}://{Request.Host}";
        var openPixel = $"{baseUrl}/track/open?token={Uri.EscapeDataString(token)}";
        var trackedLink = $"{baseUrl}/track/click?token={Uri.EscapeDataString(token)}&redirect={Uri.EscapeDataString(request.RedirectUrl)}";
        var html = $"{request.HtmlBody}<img alt=\"\" width=\"1\" height=\"1\" src=\"{openPixel}\" /><p><a href=\"{trackedLink}\">Continue</a></p>";

        await emailService.SendAsync(lead.Email, request.Subject, html);
        return Ok(new { message = "Email queued.", trackedLink, openPixel });
    }

    [HttpPost("{leadId:guid}/send-welcome-email")]
    public async Task<IActionResult> SendWelcomeEmail(Guid leadId)
    {
        var lead = await db.Leads.FindAsync(leadId);
        if (lead is null)
        {
            return NotFound("Lead not found.");
        }

        var token = tokenService.CreateLeadToken(lead.Id);
        var baseUrl = $"{Request.Scheme}://{Request.Host}";
        var encodedToken = Uri.EscapeDataString(token);
        var encodedEmail = Uri.EscapeDataString(lead.Email);
        var encodedLeadId = Uri.EscapeDataString(lead.Id.ToString("D"));

        const string frontendBaseUrl = "http://localhost:5173";
        var primaryRedirect = $"{frontendBaseUrl}/book-demo/?event=book_demo_primary&email={encodedEmail}&leadId={encodedLeadId}";
        var secondaryRedirect = $"{frontendBaseUrl}/book-demo/?event=book_demo_secondary&email={encodedEmail}&leadId={encodedLeadId}";
        var trackedPrimary = $"{baseUrl}/track/click?token={encodedToken}&redirect={Uri.EscapeDataString(primaryRedirect)}";
        var trackedSecondary = $"{baseUrl}/track/click?token={encodedToken}&redirect={Uri.EscapeDataString(secondaryRedirect)}";
        var openPixel = $"{baseUrl}/track/open?token={encodedToken}";

        var htmlBody = FirstTimeLeadEmailTemplate
            .BuildHtml(lead.Email, "book_demo", lead.Id)
            .Replace(primaryRedirect, trackedPrimary, StringComparison.Ordinal)
            .Replace(secondaryRedirect, trackedSecondary, StringComparison.Ordinal);

        var html = $"{htmlBody}<img alt=\"\" width=\"1\" height=\"1\" src=\"{openPixel}\" />";

        await emailService.SendAsync(lead.Email, FirstTimeLeadEmailTemplate.Subject, html);

        return Ok(new
        {
            message = "Welcome email queued.",
            lead.Email,
            trackedPrimary,
            trackedSecondary,
            openPixel
        });
    }
}
