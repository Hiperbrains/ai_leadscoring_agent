using LeadScoring.Api.Contracts;
using LeadScoring.Api.Data;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;

namespace LeadScoring.Api.Controllers;

[ApiController]
[Route("api/dashboard")]
public class DashboardController(LeadScoringDbContext db) : ControllerBase
{
    [HttpGet]
    public async Task<IActionResult> Get()
    {
        var leads = await db.Leads
            .OrderByDescending(l => l.LastActivityUtc)
            .Select(l => new LeadDashboardDto(
                l.Id,
                l.Email,
                l.Score,
                l.Stage.ToString(),
                l.LastActivityUtc,
                l.LastScoredAtUtc))
            .ToListAsync();

        var eventsByType = await db.Events
            .GroupBy(e => e.Type)
            .Select(g => new { Type = g.Key.ToString(), Count = g.Count() })
            .ToDictionaryAsync(k => k.Type, v => v.Count);

        var stageCounts = leads
            .GroupBy(l => l.Stage)
            .ToDictionary(g => g.Key, g => g.Count());

        return Ok(new
        {
            totalLeads = leads.Count,
            stageCounts,
            eventsByType,
            leads
        });
    }
}
