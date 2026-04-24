using LeadScoring.Api.Data;
using LeadScoring.Api.Models;
using Microsoft.EntityFrameworkCore;

namespace LeadScoring.Api.Services;

public class LeadScoringService(LeadScoringDbContext db, IEmailService emailService)
{
    public async Task AddEventAsync(LeadEvent leadEvent)
    {
        var lead = await db.Leads.FindAsync(leadEvent.LeadId);
        if (lead is null)
        {
            throw new InvalidOperationException("Lead not found.");
        }

        var previousStage = lead.Stage;
        db.Events.Add(leadEvent);
        lead.LastActivityUtc = leadEvent.TimestampUtc;
        ApplyScore(lead, leadEvent.Type);
        lead.LastScoredAtUtc = DateTime.UtcNow;
        await db.SaveChangesAsync();

        if (lead.Stage > previousStage)
        {
            var eventName = $"lead_stage_{lead.Stage.ToString().ToLowerInvariant()}";
            var htmlBody = FirstTimeLeadEmailTemplate.BuildHtml(lead.Email, eventName, lead.Id);
            await emailService.SendAsync(lead.Email, FirstTimeLeadEmailTemplate.Subject, htmlBody);
        }
    }

    public async Task ReScoreInactiveLeadsAsync(TimeSpan inactivityThreshold)
    {
        var threshold = DateTime.UtcNow.Subtract(inactivityThreshold);
        var staleLeads = await db.Leads
            .Where(l => l.LastActivityUtc <= threshold)
            .ToListAsync();

        foreach (var lead in staleLeads)
        {
            lead.LastScoredAtUtc = DateTime.UtcNow;
        }

        await db.SaveChangesAsync();
    }

    private static void ApplyScore(Lead lead, EventType eventType)
    {
        lead.Score += eventType switch
        {
            EventType.EmailClick => 10,
            EventType.WebsiteActivity => 20,
            _ => 0
        };

        lead.Stage = lead.Score switch
        {
            <= 30 => LeadStage.Cold,
            <= 60 => LeadStage.Warm,
            <= 99 => LeadStage.Mql,
            _ => LeadStage.Hot
        };
    }
}
