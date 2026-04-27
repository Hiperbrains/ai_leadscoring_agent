using LeadScoring.Api.Contracts;
using System.Text.Json;
using LeadScoring.Api.Models;
using LeadScoring.Api.Services;
using Microsoft.AspNetCore.Mvc;

namespace LeadScoring.Api.Controllers;

[ApiController]
[Route("track")]
public class TrackingController(
    TokenService tokenService,
    LeadScoringService scoringService) : ControllerBase
{
    [HttpGet("open")]
    public IActionResult TrackOpen([FromQuery] string token)
    {
        var leadId = tokenService.ValidateLeadToken(token);
        if (leadId is null)
        {
            return NotFound();
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
            MetadataJson = $$"""{"redirect":"{{redirect}}","eventName":"Email click"}"""
        });

        return Redirect(redirect);
    }

    [HttpPost("event")]
    public async Task<IActionResult> TrackEvent([FromBody] TrackEventRequest request)
    {
        var eventType = Enum.TryParse<EventType>(request.EventType, true, out var parsed) ? parsed : EventType.WebsiteActivity;
        var metadataJson = MergeMetadata(request.MetadataJson, request.EventType);

        await scoringService.AddEventAsync(new LeadEvent
        {
            Id = Guid.NewGuid(),
            LeadId = request.LeadId,
            Type = eventType,
            Source = EventSource.Website,
            TimestampUtc = DateTime.UtcNow,
            MetadataJson = metadataJson
        });

        return Accepted();
    }

    private static string? MergeMetadata(string? metadataJson, string? eventType)
    {
        if (string.IsNullOrWhiteSpace(eventType))
        {
            return metadataJson;
        }

        if (string.IsNullOrWhiteSpace(metadataJson))
        {
            return $$"""{"eventName":"{{eventType}}"}""";
        }

        try
        {
            using var doc = JsonDocument.Parse(metadataJson);
            if (doc.RootElement.ValueKind != JsonValueKind.Object)
            {
                return metadataJson;
            }

            var properties = new List<string>();
            foreach (var property in doc.RootElement.EnumerateObject())
            {
                properties.Add($"\"{property.Name}\":{property.Value.GetRawText()}");
            }

            if (!doc.RootElement.TryGetProperty("eventName", out _))
            {
                properties.Add($"\"eventName\":\"{eventType}\"");
            }

            return $"{{{string.Join(",", properties)}}}";
        }
        catch
        {
            return metadataJson;
        }
    }
}
