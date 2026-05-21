using LeadScoring.Api;
using LeadScoring.Api.Contracts;
using LeadScoring.Api.Data;
using LeadScoring.Api.Models;
using LeadScoring.Api.Services;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;

namespace LeadScoring.Api.Controllers;

[ApiController]
[Route("api/dashboard")]
[Authorize]
public class DashboardController(
    ICompanyLeadDbAccessor companyLeadDb,
    ITenantContext tenantContext,
    ITenantLeadScope tenantLeadScope) : ControllerBase
{
    /// <summary>KPIs and chart inputs only (no per-lead rows).</summary>
    [HttpGet("summary")]
    public async Task<IActionResult> GetSummary(CancellationToken cancellationToken)
    {
        tenantContext.RequireTenant();
        await tenantLeadScope.EnsureTenantContextMatchesUserAsync(cancellationToken);
        var companyName = await tenantLeadScope.ResolveCompanyNameAsync(cancellationToken);
        var db = companyLeadDb.GetDbContext();
        var scopedLeads = tenantLeadScope.ApplyScope(db.Leads, companyName);

        var s = await BuildSummaryPayloadAsync(db, scopedLeads, companyName, cancellationToken);
        return Ok(new
        {
            companyName = s.companyName,
            totalLeads = s.totalLeads,
            signedUpCount = s.signedUpCount,
            stageCounts = s.stageCounts,
            eventsByType = s.eventsByType,
            firstSourceCounts = s.firstSourceCounts
        });
    }

    /// <summary>Full lead rows for the workspace Leads table (heavy query).</summary>
    [HttpGet("leads")]
    public async Task<IActionResult> GetLeads(CancellationToken cancellationToken)
    {
        tenantContext.RequireTenant();
        await tenantLeadScope.EnsureTenantContextMatchesUserAsync(cancellationToken);
        var companyName = await tenantLeadScope.ResolveCompanyNameAsync(cancellationToken);
        var db = companyLeadDb.GetDbContext();
        var scopedLeads = tenantLeadScope.ApplyScope(db.Leads, companyName);

        var leads = await QueryLeadRowsAsync(db, scopedLeads, cancellationToken);
        return Ok(new { leads });
    }

    /// <summary>Full dashboard payload (summary + all leads). Prefer /summary and /leads for smaller responses.</summary>
    [HttpGet]
    public async Task<IActionResult> Get(CancellationToken cancellationToken)
    {
        tenantContext.RequireTenant();
        await tenantLeadScope.EnsureTenantContextMatchesUserAsync(cancellationToken);
        var companyName = await tenantLeadScope.ResolveCompanyNameAsync(cancellationToken);
        var db = companyLeadDb.GetDbContext();
        var scopedLeads = tenantLeadScope.ApplyScope(db.Leads, companyName);

        var summary = await BuildSummaryPayloadAsync(db, scopedLeads, companyName, cancellationToken);
        var leads = await QueryLeadRowsAsync(db, scopedLeads, cancellationToken);

        return Ok(new
        {
            summary.companyName,
            summary.totalLeads,
            summary.signedUpCount,
            summary.stageCounts,
            summary.eventsByType,
            firstSourceCounts = summary.firstSourceCounts,
            leads
        });
    }

    private static async Task<(
        string companyName,
        int totalLeads,
        int signedUpCount,
        Dictionary<string, int> stageCounts,
        Dictionary<string, int> eventsByType,
        Dictionary<string, int> firstSourceCounts)> BuildSummaryPayloadAsync(
        PublicCompanyDbContext db,
        IQueryable<Lead> scopedLeads,
        string companyName,
        CancellationToken cancellationToken)
    {
        var totalLeads = await scopedLeads.CountAsync(cancellationToken);
        var signedUpCount = await scopedLeads.CountAsync(l => l.SignupCompleted, cancellationToken);

        var stageCountsList = await scopedLeads
            .GroupBy(l => l.Stage)
            .Select(g => new { Stage = g.Key.ToString(), Count = g.Count() })
            .ToListAsync(cancellationToken);
        var stageCounts = stageCountsList.ToDictionary(x => x.Stage, x => x.Count);

        var scopedLeadIds = scopedLeads.Select(l => l.Id);
        var eventsByType = await db.Events
            .Where(e => e.LeadId != null && scopedLeadIds.Contains(e.LeadId.Value))
            .GroupBy(e => e.Type)
            .Select(g => new { Type = g.Key.ToString(), Count = g.Count() })
            .ToDictionaryAsync(k => k.Type, v => v.Count, cancellationToken);

        var firstSourceCounts = await scopedLeads
            .AsNoTracking()
            .GroupBy(l => l.FirstSource)
            .Select(g => new { Source = g.Key, Count = g.Count() })
            .ToListAsync(cancellationToken);

        var firstSourceBuckets = firstSourceCounts
            .GroupBy(x => (x.Source ?? EventSource.Unknown).ToString(), StringComparer.OrdinalIgnoreCase)
            .ToDictionary(
                g => g.Key,
                g => g.Sum(x => x.Count),
                StringComparer.OrdinalIgnoreCase);

        return (companyName, totalLeads, signedUpCount, stageCounts, eventsByType, firstSourceBuckets);
    }

    private static async Task<List<LeadDashboardDto>> QueryLeadRowsAsync(
        PublicCompanyDbContext db,
        IQueryable<Lead> scopedLeads,
        CancellationToken cancellationToken)
    {
        const int nextEmailDelayHours = 24;

        var leads = await (
            from l in scopedLeads
            let lastEv = db.Events
                .Where(e => e.LeadId == l.Id)
                .OrderByDescending(e => e.TimestampUtc)
                .FirstOrDefault()
            let lastNonEmptyCampaign = db.Events
                .Where(e => e.LeadId == l.Id && e.Campaign != null && e.Campaign != "")
                .OrderByDescending(e => e.TimestampUtc)
                .Select(e => e.Campaign)
                .FirstOrDefault()
            let eventScoreSum = db.Events
                .Where(e => e.LeadId == l.Id)
                .Sum(e => e.EventScore)
            let src = lastEv != null ? (EventSource?)lastEv.Source : l.LastSource
            orderby l.LastActivityUtc descending
            select new LeadDashboardDto(
                l.Id,
                l.Email,
                eventScoreSum,
                l.Stage.ToString(),
                l.LastActivityUtc,
                l.LastScoredAtUtc,
                lastEv != null ? lastEv.Type.ToString() : null,
                src == null
                    ? null
                    : src == EventSource.Unknown
                        ? "Unknown"
                        : src == EventSource.Email
                            ? "Email"
                            : src == EventSource.Website
                                ? "Website"
                                : src == EventSource.LinkedIn
                                    ? "LinkedIn"
                                    : src == EventSource.Direct
                                        ? "Direct"
                                        : src == EventSource.Organic
                                            ? "Organic"
                                            : "Unknown",
                lastNonEmptyCampaign,
                db.Events
                    .Where(e => e.LeadId == l.Id && e.Source == EventSource.Email)
                    .OrderByDescending(e => e.TimestampUtc)
                    .Select(e => e.MetadataJson != null && EF.Functions.Like(e.MetadataJson, "%welcome_email%")
                        ? "Welcome Email"
                        : e.Type.ToString())
                    .FirstOrDefault(),
                db.EmailTemplates
                    .Where(t => t.IsActive &&
                                t.Stage == (l.Stage == LeadStage.Cold
                                    ? LeadStage.Warm
                                    : l.Stage == LeadStage.Warm
                                        ? LeadStage.Mql
                                        : LeadStage.Hot) &&
                                (t.ProductId == l.ProductId || t.ProductId == null))
                    .OrderByDescending(t => t.ProductId == l.ProductId)
                    .ThenByDescending(t => t.UpdatedAt ?? t.CreatedAt)
                    .Select(t => t.Name)
                    .FirstOrDefault(),
                l.LastActivityUtc.AddHours(nextEmailDelayHours),
                (l.Stage == LeadStage.Cold
                    ? LeadStage.Warm
                    : l.Stage == LeadStage.Warm
                        ? LeadStage.Mql
                        : LeadStage.Hot).ToString(),
                l.SignupCompleted,
                l.ProfileCompletion,
                l.SelectedPlan,
                l.PlanRenewalDate))
            .ToListAsync(cancellationToken);

        return await ApplyCampaignMetadataFallbackAsync(db, leads);
    }

    private static async Task<List<LeadDashboardDto>> ApplyCampaignMetadataFallbackAsync(
        PublicCompanyDbContext db,
        List<LeadDashboardDto> leads)
    {
        var needs = leads.Where(x => string.IsNullOrWhiteSpace(x.LastEventCampaign)).Select(x => x.Id).ToHashSet();
        if (needs.Count == 0)
        {
            return leads;
        }

        var events = await db.Events.AsNoTracking()
            .Where(e => e.LeadId != null && needs.Contains(e.LeadId.Value))
            .Select(e => new { e.LeadId, e.MetadataJson, e.TimestampUtc })
            .ToListAsync();

        var byLead = events
            .Where(e => e.LeadId.HasValue)
            .GroupBy(e => e.LeadId!.Value)
            .ToDictionary(
                g => g.Key,
                g => g.OrderByDescending(x => x.TimestampUtc).ToList());

        return leads.Select(l =>
        {
            if (!string.IsNullOrWhiteSpace(l.LastEventCampaign))
            {
                return l;
            }

            if (!byLead.TryGetValue(l.Id, out var evs))
            {
                return l;
            }

            foreach (var ev in evs)
            {
                var c = EventCampaignResolver.FromMetadata(ev.MetadataJson);
                if (!string.IsNullOrWhiteSpace(c))
                {
                    return l with { LastEventCampaign = c };
                }
            }

            return l;
        }).ToList();
    }
}
