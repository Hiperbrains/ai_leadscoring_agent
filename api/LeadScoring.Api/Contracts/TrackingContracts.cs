namespace LeadScoring.Api.Contracts;

public record TrackEventRequest(Guid LeadId, string? EventType, string? MetadataJson);
