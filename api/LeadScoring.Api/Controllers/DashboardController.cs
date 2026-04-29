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
        const int nextEmailDelayHours = 24;

        var leads = await db.Leads
            .OrderByDescending(l => l.LastActivityUtc)
            .Select(l => new LeadDashboardDto(
                l.Id,
                l.Email,
                l.Score,
                l.Stage.ToString(),
                l.LastActivityUtc,
                l.LastScoredAtUtc,
                db.Events
                    .Where(e => e.LeadId == l.Id)
                    .OrderByDescending(e => e.TimestampUtc)
                    .Select(e => e.Type.ToString())
                    .FirstOrDefault(),
                db.Events
                    .Where(e => e.LeadId == l.Id && e.Source == Models.EventSource.Email)
                    .OrderByDescending(e => e.TimestampUtc)
                    .Select(e => e.MetadataJson != null && EF.Functions.Like(e.MetadataJson, "%welcome_email%")
                        ? "Welcome Email"
                        : e.Type.ToString())
                    .FirstOrDefault(),
                db.EmailTemplates
                    .Where(t => t.IsActive &&
                                t.Stage == (l.Stage == Models.LeadStage.Cold
                                    ? Models.LeadStage.Warm
                                    : l.Stage == Models.LeadStage.Warm
                                        ? Models.LeadStage.Mql
                                        : Models.LeadStage.Hot) &&
                                (t.ProductId == l.ProductId || t.ProductId == null))
                    .OrderByDescending(t => t.ProductId == l.ProductId)
                    .ThenByDescending(t => t.UpdatedAt ?? t.CreatedAt)
                    .Select(t => t.Name)
                    .FirstOrDefault(),
                l.LastActivityUtc.AddHours(nextEmailDelayHours),
                (l.Stage == Models.LeadStage.Cold
                    ? Models.LeadStage.Warm
                    : l.Stage == Models.LeadStage.Warm
                        ? Models.LeadStage.Mql
                        : Models.LeadStage.Hot).ToString(),
                l.SignupCompleted,
                l.ProfileCompletion,
                l.SelectedPlan,
                l.PlanRenewalDate))
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
