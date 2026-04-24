using LeadScoring.Api.Contracts;
using LeadScoring.Api.Data;
using LeadScoring.Api.Models;
using LeadScoring.Api.Services;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;

namespace LeadScoring.Api.Controllers;

[ApiController]
[Route("track")]
public class TrackingController(
    LeadScoringDbContext db,
    TokenService tokenService,
    LeadScoringService scoringService) : ControllerBase
{
    [HttpGet("open")]
    public async Task<IActionResult> TrackOpen([FromQuery] string token)
    {
        var leadId = tokenService.ValidateLeadToken(token);
        if (leadId is null)
        {
            return NotFound();
        }

        var now = DateTime.UtcNow;
        var duplicateWindow = now.AddMinutes(-5);
        var duplicate = await db.Events.AnyAsync(e =>
            e.LeadId == leadId &&
            e.Type == EventType.Open &&
            e.TimestampUtc >= duplicateWindow);

        if (!duplicate)
        {
            var userAgent = Request.Headers.UserAgent.ToString();
            var suspectedBot = userAgent.Contains("GoogleImageProxy", StringComparison.OrdinalIgnoreCase)
                               || userAgent.Contains("Outlook", StringComparison.OrdinalIgnoreCase)
                               || userAgent.Contains("Barracuda", StringComparison.OrdinalIgnoreCase);

            await scoringService.AddEventAsync(new LeadEvent
            {
                Id = Guid.NewGuid(),
                LeadId = leadId.Value,
                Type = EventType.Open,
                Source = EventSource.Email,
                TimestampUtc = now,
                SuspectedBot = suspectedBot,
                MetadataJson = $$"""{"userAgent":"{{userAgent}}"}"""
            });
        }

        var pixel = Convert.FromBase64String("R0lGODlhAQABAPAAAAAAAAAAACH5BAEAAAAALAAAAAABAAEAAAICRAEAOw==");
        return File(pixel, "image/gif");
    }

    [HttpGet("click")]
    public async Task<IActionResult> TrackClick([FromQuery] string token, [FromQuery] string redirect)
    {
        var leadId = tokenService.ValidateLeadToken(token);
        if (leadId is null || !Uri.IsWellFormedUriString(redirect, UriKind.Absolute))
        {
            return BadRequest("Invalid token or redirect URL.");
        }

        await scoringService.AddEventAsync(new LeadEvent
        {
            Id = Guid.NewGuid(),
            LeadId = leadId.Value,
            Type = EventType.EmailClick,
            Source = EventSource.Email,
            TimestampUtc = DateTime.UtcNow,
            MetadataJson = $$"""{"redirect":"{{redirect}}"}"""
        });

        return Redirect(redirect);
    }

    [HttpPost("event")]
    public async Task<IActionResult> TrackEvent([FromBody] TrackEventRequest request)
    {
        var eventType = Enum.TryParse<EventType>(request.EventType, true, out var parsed) ? parsed : EventType.WebsiteActivity;

        await scoringService.AddEventAsync(new LeadEvent
        {
            Id = Guid.NewGuid(),
            LeadId = request.LeadId,
            Type = eventType,
            Source = EventSource.Website,
            TimestampUtc = DateTime.UtcNow,
            MetadataJson = request.MetadataJson
        });

        return Accepted();
    }
}
