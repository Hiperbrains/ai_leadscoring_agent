using LeadScoring.Api.Contracts;
using LeadScoring.Api.Data;
using LeadScoring.Api.Models;
using LeadScoring.Api.Services;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using System.Text.Json;

namespace LeadScoring.Api.Controllers;

[ApiController]
[Route("api/leads")]
public class LeadsController(
    LeadScoringDbContext db,
    TokenService tokenService,
    IEmailService emailService,
    LeadImportService leadImportService) : ControllerBase
{
    [HttpPost("email-exists")]
    public async Task<ActionResult<LeadEmailExistsResponse>> CheckEmailExists([FromBody] LeadEmailExistsRequest request)
    {
        if (string.IsNullOrWhiteSpace(request.Email))
        {
            return BadRequest("email is required.");
        }

        var normalizedEmail = request.Email.Trim().ToLowerInvariant();
        var lead = await db.Leads
            .AsNoTracking()
            .FirstOrDefaultAsync(x => EF.Functions.ILike(x.Email, normalizedEmail));

        return Ok(new LeadEmailExistsResponse(
            Email: normalizedEmail,
            Exists: lead is not null,
            LeadId: lead?.Id));
    }

    [HttpPost("website-demo/submit")]
    public async Task<ActionResult<WebsiteDemoSubmitResponse>> SubmitWebsiteDemo([FromBody] WebsiteDemoSubmitRequest request)
    {
        if (string.IsNullOrWhiteSpace(request.VisitorId))
        {
            return BadRequest("visitorId is required.");
        }

        if (string.IsNullOrWhiteSpace(request.Email))
        {
            return BadRequest("email is required.");
        }

        var normalizedEmail = request.Email.Trim().ToLowerInvariant();
        var nowUtc = DateTime.UtcNow;

        var lead = await db.Leads
            .FirstOrDefaultAsync(x => EF.Functions.ILike(x.Email, normalizedEmail));

        var leadCreated = false;
        if (lead is null)
        {
            lead = new Lead
            {
                Id = Guid.NewGuid(),
                VisitorId = request.VisitorId.Trim(),
                Email = normalizedEmail,
                FirstName = request.FirstName?.Trim(),
                LastName = request.LastName?.Trim(),
                ProductId = request.ProductId,
                WelcomeEmailSent = false,
                Score = 0,
                Stage = LeadStage.Cold,
                CreatedAtUtc = nowUtc,
                LastActivityUtc = nowUtc
            };
            db.Leads.Add(lead);
            leadCreated = true;
        }
        else
        {
            if (string.IsNullOrWhiteSpace(lead.VisitorId))
            {
                lead.VisitorId = request.VisitorId.Trim();
            }

            lead.FirstName = string.IsNullOrWhiteSpace(request.FirstName) ? lead.FirstName : request.FirstName.Trim();
            lead.LastName = string.IsNullOrWhiteSpace(request.LastName) ? lead.LastName : request.LastName.Trim();
            lead.ProductId = request.ProductId ?? lead.ProductId;
            lead.LastActivityUtc = nowUtc;
        }

        var visitorId = request.VisitorId.Trim();
        var existingMap = await db.LeadVisitorMaps
            .AsNoTracking()
            .FirstOrDefaultAsync(x => x.LeadId == lead.Id && x.VisitorId == visitorId);

        var visitorMapped = false;
        if (existingMap is null)
        {
            db.LeadVisitorMaps.Add(new LeadVisitorMap
            {
                LeadId = lead.Id,
                VisitorId = visitorId,
                Email = normalizedEmail,
                FirstName = request.FirstName?.Trim(),
                LastName = request.LastName?.Trim(),
                PhoneNumber = request.PhoneNumber?.Trim(),
                CompanyName = request.CompanyName?.Trim(),
                Country = request.Country?.Trim(),
                Notes = request.Notes?.Trim(),
                CreatedAtUtc = nowUtc
            });
            visitorMapped = true;
        }

        db.Events.Add(new LeadEvent
        {
            Id = Guid.NewGuid(),
            LeadId = lead.Id,
            Type = EventType.BookDemo,
            Source = EventSource.Website,
            TimestampUtc = nowUtc,
            MetadataJson = BuildWebsiteDemoMetadata(request, visitorId)
        });

        await db.SaveChangesAsync();

        return Ok(new WebsiteDemoSubmitResponse(
            lead.Id,
            lead.Email,
            leadCreated,
            visitorMapped,
            EventCreated: true));
    }

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

        var template = await db.EmailTemplates
            .Where(t => t.IsActive && t.Stage == LeadStage.Cold && (t.ProductId == lead.ProductId || t.ProductId == null))
            .OrderByDescending(t => t.ProductId == lead.ProductId)
            .ThenByDescending(t => t.UpdatedAt ?? t.CreatedAt)
            .FirstOrDefaultAsync();
        if (template is null)
        {
            return BadRequest("No active Cold stage template found.");
        }

        const string eventName = "welcome_email";
        var htmlBody = ResolveTemplate(template.EmailBodyHtml, lead, eventName, template.IsTrackingEnabled);

        await emailService.SendAsync(lead.Email, template.Subject, htmlBody);
        lead.WelcomeEmailSent = true;
        db.Events.Add(new LeadEvent
        {
            Id = Guid.NewGuid(),
            LeadId = lead.Id,
            Type = EventType.WebsiteActivity,
            Source = EventSource.Email,
            TimestampUtc = DateTime.UtcNow,
            MetadataJson = $$"""{"eventName":"welcome_email","systemMarker":"WelcomeEmailSent"}"""
        });
        await db.SaveChangesAsync();

        return Ok(new
        {
            message = "Welcome email queued.",
            lead.Email
        });
    }

    private static string ResolveTemplate(string value, Lead lead, string eventName, bool trackingEnabled)
    {
        var leadId = lead.Id.ToString("D");
        var emailValue = trackingEnabled ? Uri.EscapeDataString(lead.Email) : lead.Email;
        var eventValue = trackingEnabled ? Uri.EscapeDataString(eventName) : eventName;
        var leadIdValue = trackingEnabled ? Uri.EscapeDataString(leadId) : leadId;

        return value
            .Replace("{{email}}", emailValue, StringComparison.OrdinalIgnoreCase)
            .Replace("{{event}}", eventValue, StringComparison.OrdinalIgnoreCase)
            .Replace("{{leadId}}", leadIdValue, StringComparison.OrdinalIgnoreCase)
            .Replace("{{stage}}", LeadStage.Cold.ToString(), StringComparison.OrdinalIgnoreCase);
    }

    private static string BuildWebsiteDemoMetadata(WebsiteDemoSubmitRequest request, string visitorId)
    {
        var metadata = new Dictionary<string, object?>
        {
            ["eventName"] = "Book demo",
            ["systemMarker"] = "WebsiteDemoSubmitted",
            ["visitorId"] = visitorId,
            ["email"] = request.Email.Trim().ToLowerInvariant(),
            ["firstName"] = request.FirstName,
            ["lastName"] = request.LastName,
            ["phoneNumber"] = request.PhoneNumber,
            ["country"] = request.Country,
            ["companyName"] = request.CompanyName,
            ["notes"] = request.Notes,
            ["productId"] = request.ProductId
        };

        if (!string.IsNullOrWhiteSpace(request.MetadataJson))
        {
            metadata["rawMetadata"] = request.MetadataJson;
        }

        return JsonSerializer.Serialize(metadata);
    }
}
