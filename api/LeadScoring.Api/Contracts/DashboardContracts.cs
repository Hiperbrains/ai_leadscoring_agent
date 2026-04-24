namespace LeadScoring.Api.Contracts;

public record LeadDashboardDto(Guid Id, string Email, int Score, string Stage, DateTime LastActivityUtc, DateTime? LastScoredAtUtc);
